using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using RimWorld;
using Verse;
using Verse.AI;

namespace SlotReservations
{
    /// <summary>Capacity lookup and the "can every rule still be satisfied?" logic.</summary>
    public static class SlotRuleSolver
    {
        public static bool AsfActive;

        // --- capacity -------------------------------------------------------

        /// <summary>Total stack slots of the building (all cells), as ASF defines it.</summary>
        public static int TotalSlots(Building_Storage b)
        {
            if (b == null) return 0;
            if (AsfActive && AsfCompat.TryTotalSlots(b, out int n)) return n;
            return b.MaxItemsInCell * b.AllSlotCellsList().Count;
        }

        /// <summary>Slots currently usable (ASF allows the player to lower the limit).</summary>
        static int EffectiveSlots(Building_Storage b)
        {
            if (AsfActive && AsfCompat.TryCurrentSlotLimit(b, out int n)) return n;
            return TotalSlots(b);
        }

        // --- caches (valid for one tick) --------------------------------------

        static int cacheTick = -1;
        static readonly Dictionary<Building_Storage, HashSet<Thing>> excessCache = new Dictionary<Building_Storage, HashSet<Thing>>();
        static readonly Dictionary<(Building_Storage, Thing, Pawn), bool> newStackCache = new Dictionary<(Building_Storage, Thing, Pawn), bool>();

        public static void ClearCache() { excessCache.Clear(); newStackCache.Clear(); incomingCache.Clear(); }

        /// <summary>Called when a game is started/loaded so nothing from a previous save lingers.</summary>
        public static void ResetForNewGame() { ClearCache(); evicted.Clear(); }

        static void TickCache()
        {
            int tick = Find.TickManager?.TicksGame ?? 0;
            if (tick != cacheTick) { cacheTick = tick; ClearCache(); }
        }

        // --- public entry points ----------------------------------------------

        /// <summary>
        /// True if <paramref name="t"/> may be stored in <paramref name="b"/> regarding slot rules.
        /// <paramref name="cell"/> = the target cell when known (IsGoodStoreCell), Invalid for building-level checks.
        /// The building's normal filter is checked elsewhere; this only adds restrictions.
        /// </summary>
        public static bool Allows(Building_Storage b, Thing t, IntVec3 cell, Pawn carrier = null)
        {
            // Fast path: buildings without rules cost one hash lookup.
            if (t == null || !CompSlotRules.ActiveBuildings.Contains(b) || b.Map == null) return true;
            var comp = b.GetComp<CompSlotRules>();
            if (comp == null) return true;
            TickCache();

            var excess = Excess(b, comp);

            // Already stored here: valid unless it is one of the stacks that must make room (eviction).
            if (IsStoredIn(b, t)) return !excess.Contains(t);

            // Just evicted from here: never take it back (prevents pick-up/drop-back loops).
            if (RecentlyEvictedFrom(b, t)) return false;

            // Tops up a stack in that cell without creating a new one -> needs no new slot.
            if (MergesInto(b, t, cell, excess)) return true;

            var key = (b, t, carrier);
            if (!newStackCache.TryGetValue(key, out bool ok))
                newStackCache[key] = ok = CanAddNewStack(b, comp, t, carrier);
            return ok;
        }

        /// <summary>Stored stacks that break the rules and should be hauled away.</summary>
        public static HashSet<Thing> Excess(Building_Storage b, CompSlotRules comp)
        {
            TickCache();
            if (!excessCache.TryGetValue(b, out var set))
            {
                set = new HashSet<Thing>(Overflow(StoredStacks(b), comp.rules, EffectiveSlots(b)));
                excessCache[b] = set;
                foreach (var th in set) RememberEvicted(b, th);
            }
            return set;
        }

        // --- eviction memory --------------------------------------------------

        const int EvictMemoryTicks = 2500; // ~1 in-game hour
        static readonly Dictionary<int, (Building_Storage b, int tick)> evicted = new Dictionary<int, (Building_Storage, int)>();

        static void RememberEvicted(Building_Storage b, Thing th)
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            if (!evicted.TryGetValue(th.thingIDNumber, out var e) || e.b != b)
            {
                if (Prefs.DevMode) Log.Message($"{Startup.Prefix}{b.LabelCap} ({b.Position}): moving out {th.LabelCap} to make room for reserved slots.");
                if (evicted.Count > 2000) evicted.Clear(); // keep it small
            }
            evicted[th.thingIDNumber] = (b, now);
        }

        static bool RecentlyEvictedFrom(Building_Storage b, Thing t)
        {
            if (!evicted.TryGetValue(t.thingIDNumber, out var e) || e.b != b) return false;
            return (Find.TickManager?.TicksGame ?? 0) - e.tick < EvictMemoryTicks;
        }

        // --- helpers ----------------------------------------------------------

        static List<Thing> StoredStacks(Building_Storage b)
            => b.slotGroup != null ? b.slotGroup.HeldThings.ToList() : new List<Thing>();

        static bool IsStoredIn(Building_Storage b, Thing t)
            => t.Spawned && t.Map == b.Map && t.Position.GetSlotGroup(b.Map)?.parent == b;

        /// <summary>True if <paramref name="t"/> can be added in <paramref name="cell"/> (or anywhere in the
        /// building when cell is Invalid) by topping up an existing stack, without creating a new stack.</summary>
        static bool MergesInto(Building_Storage b, Thing t, IntVec3 cell, HashSet<Thing> excess)
        {
            if (t.def.stackLimit <= 1) return false;
            if (cell.IsValid) return MergesAtCell(b.Map, t, cell, excess);
            foreach (var c in b.AllSlotCellsList())
                if (MergesAtCell(b.Map, t, c, excess)) return true;
            return false;
        }

        static bool MergesAtCell(Map map, Thing t, IntVec3 c, HashSet<Thing> excess)
        {
            var list = map.thingGrid.ThingsListAtFast(c);
            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                if (s == t || s.def.category != ThingCategory.Item || s.stackCount >= s.def.stackLimit || !s.CanStackWith(t)) continue;
                if (excess != null && excess.Contains(s)) continue; // never feed a stack that is being moved out
                // The leftover of a partial merge would become a new stack in this cell (ASF multi-stack cells),
                // bypassing the rules. Only count it as a merge if all fits, or the cell has no room for another stack.
                if (s.stackCount + t.stackCount <= s.def.stackLimit || c.GetItemCount(map) >= c.GetMaxItemsAllowedInCell(map))
                    return true;
            }
            return false;
        }

        static bool CanAddNewStack(Building_Storage b, CompSlotRules comp, Thing t, Pawn carrier)
        {
            var stacks = StoredStacks(b);
            AddIncoming(b, t, carrier, stacks);
            stacks.Add(t);
            return Overflow(stacks, comp.rules, EffectiveSlots(b)).Count == 0;
        }

        /// <summary>
        /// Items other pawns are already bringing here (a haul job that reserved one of our cells).
        /// Without this, several haulers (or Pick Up and Haul trips) checked at the same time all see
        /// the same free slots. Works for vanilla HaulToCell and Pick Up and Haul's unload job.
        /// </summary>
        static void AddIncoming(Building_Storage b, Thing t, Pawn carrier, List<Thing> stacks)
        {
            var seen = new HashSet<Thing>(stacks);
            foreach (var (x, pawn) in IncomingFor(b))
            {
                if (pawn == carrier || x == t || HeldBy(t, pawn) || !seen.Add(x)) continue;
                stacks.Add(x);
            }
        }

        // The map's reservation list is scanned once per building per tick, not once per check.
        static readonly Dictionary<Building_Storage, List<(Thing, Pawn)>> incomingCache = new Dictionary<Building_Storage, List<(Thing, Pawn)>>();

        static List<(Thing, Pawn)> IncomingFor(Building_Storage b)
        {
            if (incomingCache.TryGetValue(b, out var list)) return list;
            list = new List<(Thing, Pawn)>();
            var map = b.Map;
            foreach (var r in map.reservationManager.ReservationsReadOnly)
            {
                var pawn = r.Claimant;
                var job = r.Job;
                if (job == null || pawn == null || r.Target.HasThing) continue;
                IntVec3 c = r.Target.Cell;
                if (!job.targetB.IsValid || job.targetB.HasThing || job.targetB.Cell != c) continue;
                var x = job.targetA.Thing;
                if (x == null || x.def.category != ThingCategory.Item) continue;
                if (c.GetSlotGroup(map)?.parent != b || MergesAtCell(map, x, c, null)) continue;
                list.Add((x, pawn));
            }
            incomingCache[b] = list;
            return list;
        }

        /// <summary>True if <paramref name="t"/> is in <paramref name="pawn"/>'s hands or inventory (it is the one checking).</summary>
        static bool HeldBy(Thing t, Pawn pawn)
            => (t.ParentHolder is Pawn_CarryTracker ct && ct.pawn == pawn)
            || (t.ParentHolder is Pawn_InventoryTracker it && it.pawn == pawn);

        /// <summary>
        /// Assigns stacks to rule slots and returns the stacks that do not fit.
        /// 1) Stacks matching some rule, fewest matching rules first, go to the most specific rule
        ///    (fewest allowed defs) that still has room.
        /// 2) All remaining stacks share the unreserved pool (capacity - sum of rule counts),
        ///    oldest stack (lowest thing ID) first. What does not fit is overflow, newest first.
        /// </summary>
        public static List<Thing> Overflow(List<Thing> things, List<SlotRule> rules, int capacity)
        {
            var ordered = rules.Where(r => r.count > 0).OrderBy(r => r.filter.AllowedDefCount).ToList();
            var left = ordered.Select(r => r.count).ToArray();
            int freePool = capacity - left.Sum();

            var withMatches = things.Select(th => (th, m: ordered.Select((r, i) => r.filter.Allows(th) ? i : -1).Where(i => i >= 0).ToList()))
                                    .OrderBy(x => x.m.Count).ThenBy(x => x.th.thingIDNumber);
            var unassigned = new List<Thing>();
            foreach (var (th, m) in withMatches)
            {
                int slot = m.FirstOrDefault(i => left[i] > 0, -1);
                if (slot >= 0) left[slot]--;
                else unassigned.Add(th);
            }

            unassigned.Sort((a, c) => a.thingIDNumber.CompareTo(c.thingIDNumber));
            int keep = System.Math.Max(0, freePool);
            return unassigned.Count > keep ? unassigned.GetRange(keep, unassigned.Count - keep) : new List<Thing>();
        }

        static int FirstOrDefault(this List<int> list, System.Func<int, bool> pred, int def)
        {
            foreach (var x in list) if (pred(x)) return x;
            return def;
        }
    }

    /// <summary>All direct ASF references live here so the mod still loads without ASF.</summary>
    static class AsfCompat
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static bool TryTotalSlots(Building_Storage b, out int n)
        {
            if (b is AdaptiveStorage.ThingClass tc) { n = tc.TotalSlots; return true; }
            n = 0; return false;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static bool TryCurrentSlotLimit(Building_Storage b, out int n)
        {
            if (b is AdaptiveStorage.ThingClass tc) { n = tc.CurrentSlotLimit; return true; }
            n = 0; return false;
        }
    }
}
