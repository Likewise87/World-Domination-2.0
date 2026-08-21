using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Hover-only mortar / AA range gizmos on eligible enemy T4 settlements (not toggles).</summary>
    public static class Patch_SettlementT4TurretGizmos
    {
        /// <summary>Frame when a mortar/AA range hover gizmo last ran (world overlays may draw before gizmos in the same frame).</summary>
        private static int lastTurretRangeHoverFrame = -1;

        /// <summary>True while (or one frame after) a T4 mortar/AA hover gizmo is active, so the red raid-attack ring can be suppressed.</summary>
        public static bool ShouldSuppressSettlementAttackRadius =>
            Time.frameCount - lastTurretRangeHoverFrame <= 1;

        public static void MarkTurretRangeHoverPublic() => MarkTurretRangeHover();

        private static void MarkTurretRangeHover() => lastTurretRangeHoverFrame = Time.frameCount;

        public static IEnumerable<Gizmo> GetGizmos(Settlement settlement)
        {
            if (settlement == null || settlement.Destroyed || settlement.Faction == null || settlement.Faction.IsPlayer)
                yield break;
            if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(settlement))
                yield break;

            var comp = settlement.GetComponent<CompViralSpread>();
            if (comp == null || comp.tier != SettlementTier.T4) yield break;

            bool mortarEligible = WorldDominationMod.settings?.enableNpcT4Mortar ?? WorldDominationSettings.DefEnableNpcT4Mortar;
            bool aaEligible = WorldDominationMod.settings?.enableNpcT4AntiAir ?? WorldDominationSettings.DefEnableNpcT4AntiAir;
            if (!mortarEligible && !aaEligible) yield break;

            TechLevel minTech = WorldDominationMod.settings?.npcT4MortarMinTechLevel ?? WorldDominationSettings.DefNpcT4MortarMinTechLevel;
            if (settlement.Faction.def.techLevel < minTech) yield break;

            foreach (var g in RadiusHoverGizmos.GetT4TurretRadius(settlement, mortarEligible, aaEligible))
                yield return g;
        }
    }

    /// <summary>
    /// Vanilla pods initialize travel in <see cref="TravellingTransporters.PostAdd"/>:
    /// that is where <c>initialTile</c> is captured for the Start→End slerp.
    /// Hook there instead of generic SpawnSetup so AA wakes only after the pod has real travel endpoints.
    /// </summary>
    [HarmonyPatch(typeof(TravellingTransporters), "PostAdd")]
    public static class Patch_TravellingTransporters_PostAdd_AntiAir
    {
        public static void Postfix(TravellingTransporters __instance)
        {
            if (__instance == null || __instance.Destroyed) return;
            WorldComponent_InterceptionScheduler.Current?.RegisterVanillaPods(__instance);
        }
    }
}
