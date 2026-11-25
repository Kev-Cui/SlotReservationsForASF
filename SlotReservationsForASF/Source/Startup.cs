using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SlotReservations
{
    [StaticConstructorOnStartup]
    public static class Startup
    {
        public const string Prefix = "[SlotReservations] ";

        static Startup()
        {
            SlotRuleSolver.AsfActive = ModsConfig.IsActive("adaptive.storage.framework");

            // Add the comp in code (not XML) so defs that inherit thingClass from an abstract parent are covered too.
            // Covers AdaptiveStorage.ThingClass (subclass of Building_Storage) and vanilla shelves alike.
            int added = 0;
            foreach (var def in DefDatabase<ThingDef>.AllDefs)
            {
                if (def.thingClass == null || !typeof(Building_Storage).IsAssignableFrom(def.thingClass)) continue;
                if (def.HasComp(typeof(CompSlotRules))) continue;
                (def.comps ??= new System.Collections.Generic.List<CompProperties>()).Add(new CompProperties_SlotRules());
                added++;
            }

            try
            {
                new Harmony("slotreservations.asf").PatchAll(typeof(Startup).Assembly);
                Log.Message($"{Prefix}Loaded. Comp added to {added} storage defs. ASF active: {SlotRuleSolver.AsfActive}.");
            }
            catch (Exception e)
            {
                Log.Error($"{Prefix}Harmony patching failed: {e}");
            }
        }
    }

    // ------------------------------------------------------------------------
    // Harmony patches. Both are postfixes that can only turn "true" into "false",
    // so they stack safely with ASF's own patches (ASF prefixes TryFindBestBetterStoreCellForWorker
    // and transpiles NoStorageBlockersIn / GetMaxItemsAllowedInCell; it does not touch these two).
    // ------------------------------------------------------------------------

    /// <summary>
    /// Building_Storage.Accepts is the IHaulDestination/ISlotGroupParent check used by
    /// IsValidStorageFor, IsInValidStorage, StoragePriorityAtFor, ... ASF's ThingClass.Accepts
    /// re-implements the interface but calls base.Accepts, so this covers ASF buildings too.
    /// </summary>
    [HarmonyPatch(typeof(Building_Storage), nameof(Building_Storage.Accepts))]
    static class Patch_BuildingStorage_Accepts
    {
        static void Postfix(Building_Storage __instance, Thing t, ref bool __result)
        {
            if (__result) __result = SlotRuleSolver.Allows(__instance, t, IntVec3.Invalid);
        }
    }

    /// <summary>
    /// The per-cell check haulers use when looking for a store cell
    /// (TryFindBestBetterStoreCellForWorker only checks the filter, not Accepts).
    /// Pick Up and Haul uses it too, both when planning a trip and before each drop.
    /// </summary>
    [HarmonyPatch(typeof(StoreUtility), nameof(StoreUtility.IsGoodStoreCell))]
    static class Patch_StoreUtility_IsGoodStoreCell
    {
        static void Postfix(IntVec3 c, Map map, Thing t, Pawn carrier, ref bool __result)
        {
            if (!__result || map == null) return;
            if (c.GetSlotGroup(map)?.parent is Building_Storage b)
                __result = SlotRuleSolver.Allows(b, t, c, carrier);
        }
    }
}
