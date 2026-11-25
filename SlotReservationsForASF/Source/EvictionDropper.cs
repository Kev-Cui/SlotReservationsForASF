using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace SlotReservations
{
    /// <summary>
    /// Makes room for reserved slots even when the excess stacks have nowhere to go:
    /// normally haulers move excess stacks to other storage. If no storage accepts a stack,
    /// or it has been stuck for a long time (forbidden, unreachable, no haulers), it is
    /// dropped on the ground next to the building instead.
    /// </summary>
    public class MapComponent_SlotRulesDropper : MapComponent
    {
        const int CheckInterval = 250;       // TickRare
        const int GraceTicks = 250;          // give haulers a moment right after a rule change
        const int MaxWaitTicks = 5000;       // ~2 in-game hours: drop even if storage exists

        // thingIDNumber -> tick it was first seen as excess
        readonly Dictionary<int, int> firstSeen = new Dictionary<int, int>();
        readonly HashSet<int> stillExcess = new HashSet<int>();
        readonly List<Thing> toCheck = new List<Thing>();

        public MapComponent_SlotRulesDropper(Map map) : base(map) { }

        public override void MapComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (now % CheckInterval != 0 || CompSlotRules.Active.Count == 0) return;

            stillExcess.Clear();
            foreach (var comp in CompSlotRules.Active.ToList())
            {
                var b = comp.Storage;
                if (b == null || b.Map != map) continue;

                toCheck.Clear();
                toCheck.AddRange(SlotRuleSolver.Excess(b, comp));
                foreach (var th in toCheck)
                {
                    stillExcess.Add(th.thingIDNumber);
                    if (!firstSeen.TryGetValue(th.thingIDNumber, out int since)) { firstSeen[th.thingIDNumber] = now; continue; }
                    int waited = now - since;
                    if (waited < GraceTicks) continue;
                    if (map.reservationManager.IsReservedByAnyoneOf(th, Faction.OfPlayer)) continue; // someone is coming for it
                    if (waited < MaxWaitTicks && HasOtherStorage(th)) continue;                         // let haulers do it
                    DropOut(b, th);
                }
            }

            // Forget things that are no longer excess (hauled away, dropped, rules changed).
            if (firstSeen.Count > 0)
                foreach (var id in firstSeen.Keys.Where(id => !stillExcess.Contains(id)).ToList())
                    firstSeen.Remove(id);
        }

        bool HasOtherStorage(Thing th)
        {
            // th is excess, so its own building rejects it; this only finds other storage.
            try
            {
                return StoreUtility.TryFindBestBetterStorageFor(th, null, map, StoragePriority.Unstored, Faction.OfPlayer, out _, out _, needAccurateResult: false);
            }
            catch { return false; }
        }

        void DropOut(Building_Storage b, Thing th)
        {
            IntVec3 pos = th.Position;
            th.DeSpawn();
            // Near the stack, but never into any storage cell (it would just be "stored" again).
            if (!GenPlace.TryPlaceThing(th, pos, map, ThingPlaceMode.Near, null, c => c.GetSlotGroup(map) == null))
            {
                GenSpawn.Spawn(th, pos, map); // no free ground nearby: put it back, retry later
                return;
            }
            firstSeen.Remove(th.thingIDNumber);
            SlotRuleSolver.ClearCache();
            if (Prefs.DevMode) Log.Message($"{Startup.Prefix}{b.LabelCap} ({b.Position}): no storage for {th.LabelCap}, dropped it at {th.Position}.");
        }
    }

    /// <summary>Clears static state when a game is started or loaded.</summary>
    public class GameComponent_SlotRulesReset : GameComponent
    {
        public GameComponent_SlotRulesReset(Game game)
        {
            CompSlotRules.Active.Clear();
            CompSlotRules.ActiveBuildings.Clear();
            SlotRuleSolver.ResetForNewGame();
        }
    }
}
