using System.Collections.Generic;
using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Single Settlement.GetGizmos postfix: vanilla result then WD settlement gizmos in fixed order.</summary>
    [HarmonyPatch(typeof(Settlement), nameof(Settlement.GetGizmos))]
    public static class Patch_SettlementGetGizmos
    {
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Settlement __instance)
        {
            if (__result != null)
            {
                foreach (Gizmo g in __result)
                    yield return g;
            }

            foreach (Gizmo g in Patch_AlliedSettlementRaidOrderGizmo.GetGizmos(__instance))
                yield return g;
            foreach (Gizmo g in Patch_SettlementBribeGizmo.GetGizmos(__instance))
                yield return g;
            foreach (Gizmo g in Patch_SettlementBuyGizmo.GetGizmos(__instance))
                yield return g;
            foreach (Gizmo g in Patch_SettlementOrderedRoadGizmo.GetGizmos(__instance))
                yield return g;
            foreach (Gizmo g in Patch_SettlementOrderedTraderGizmo.GetGizmos(__instance))
                yield return g;
            foreach (Gizmo g in Patch_SettlementAllyRadiusGizmo.GetGizmos(__instance))
                yield return g;
            foreach (Gizmo g in Patch_SettlementT4TurretGizmos.GetGizmos(__instance))
                yield return g;
        }
    }
}
