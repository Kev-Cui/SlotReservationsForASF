using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace SlotReservations
{
    public class CompProperties_SlotRules : CompProperties
    {
        public CompProperties_SlotRules() => compClass = typeof(CompSlotRules);
    }

    /// <summary>Holds the slot rules of one storage building and provides the gizmos.</summary>
    [StaticConstructorOnStartup] // textures must load on the main thread
    public class CompSlotRules : ThingComp
    {
        public List<SlotRule> rules = new List<SlotRule>();

        static List<SlotRule> clipboard; // shared copy/paste buffer

        static readonly Texture2D IconRules = ContentFinder<Texture2D>.Get("UI/Designators/ZoneCreate_Stockpile");
        static readonly Texture2D IconCopy = ContentFinder<Texture2D>.Get("UI/Commands/CopySettings");
        static readonly Texture2D IconPaste = ContentFinder<Texture2D>.Get("UI/Commands/PasteSettings");

        public Building_Storage Storage => parent as Building_Storage;
        public int ReservedSlots => rules.Sum(r => r.count);

        public override void PostExposeData()
        {
            Scribe_Collections.Look(ref rules, "slotRules", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && rules == null) rules = new List<SlotRule>();
        }

        // --- registry of spawned buildings that have rules -------------------
        // Lets the Harmony patches skip all other storage with one hash lookup,
        // and lets the map component visit only buildings that matter.

        public static readonly HashSet<CompSlotRules> Active = new HashSet<CompSlotRules>();
        public static readonly HashSet<Thing> ActiveBuildings = new HashSet<Thing>();

        void UpdateRegistry()
        {
            if (parent.Spawned && rules.Any(r => r.count > 0)) { Active.Add(this); ActiveBuildings.Add(parent); }
            else { Active.Remove(this); ActiveBuildings.Remove(parent); }
        }

        public override void PostSpawnSetup(bool respawningAfterLoad) => UpdateRegistry();

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            Active.Remove(this);
            ActiveBuildings.Remove(parent);
        }

        public void Notify_RulesChanged()
        {
            UpdateRegistry();
            SlotRuleSolver.ClearCache();
            // Let haulers re-evaluate items that might now (not) fit here.
            if (parent.Spawned && Storage?.slotGroup != null)
                parent.Map.listerHaulables.Notify_SlotGroupChanged(Storage.slotGroup);
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (parent.Faction != Faction.OfPlayer) yield break;

            yield return new Command_Action
            {
                defaultLabel = "SR_SlotRules".Translate(),
                defaultDesc = "SR_SlotRulesDesc".Translate(ReservedSlots, SlotRuleSolver.TotalSlots(Storage)),
                icon = IconRules,
                action = () => Find.WindowStack.Add(new Dialog_SlotRules(this)),
            };
            yield return new Command_Action
            {
                defaultLabel = "SR_CopyRules".Translate(),
                icon = IconCopy,
                action = () => { clipboard = rules.Select(r => r.Copy()).ToList(); SoundDefOf.Tick_High.PlayOneShotOnCamera(); },
            };
            var paste = new Command_Action
            {
                defaultLabel = "SR_PasteRules".Translate(),
                icon = IconPaste,
                action = () =>
                {
                    rules = clipboard.Select(r => r.Copy()).ToList();
                    ClampToCapacity();
                    Notify_RulesChanged();
                    SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                },
            };
            if (clipboard == null) paste.Disable("SR_ClipboardEmpty".Translate());
            yield return paste;
        }

        /// <summary>Trim rule counts from the last rule backwards so the sum fits the building.</summary>
        public void ClampToCapacity()
        {
            int over = ReservedSlots - SlotRuleSolver.TotalSlots(Storage);
            for (int i = rules.Count - 1; i >= 0 && over > 0; i--)
            {
                int cut = Mathf.Min(over, rules[i].count);
                rules[i].count -= cut;
                over -= cut;
            }
        }

        public override string CompInspectStringExtra()
        {
            if (rules.Count == 0 || !parent.Spawned) return null;
            string s = "SR_InspectReserved".Translate(ReservedSlots, SlotRuleSolver.TotalSlots(Storage));
            int excess = SlotRuleSolver.Excess(Storage, this).Count;
            if (excess > 0) s += "\n" + "SR_InspectToMoveOut".Translate(excess);
            return s;
        }
    }
}
