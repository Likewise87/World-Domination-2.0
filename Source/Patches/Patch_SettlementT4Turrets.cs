using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Mortar/AA range hover marker. Settlements no longer carry mortar/AA gizmos; range toggles live on
    /// NPC artillery turrets (<see cref="RadiusHoverGizmos.GetForArtilleryTurret"/>).
    /// </summary>
    public static class Patch_SettlementT4TurretGizmos
    {
        /// <summary>Frame when a mortar/AA range hover gizmo last ran (world overlays may draw before gizmos in the same frame).</summary>
        private static int lastTurretRangeHoverFrame = -1;

        /// <summary>True while (or one frame after) a mortar/AA range overlay is active, so the red raid-attack ring can be suppressed.</summary>
        public static bool ShouldSuppressSettlementAttackRadius =>
            Time.frameCount - lastTurretRangeHoverFrame <= 1;

        public static void MarkTurretRangeHoverPublic() => MarkTurretRangeHover();

        private static void MarkTurretRangeHover() => lastTurretRangeHoverFrame = Time.frameCount;
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

            int startTile = WD_TempEncounterAerialLeaveUtility.TryGetTravellingTransportersStartTileId(__instance);
            if (startTile >= 0)
                WD_TempEncounterAerialLeaveUtility.NotifyWorldAirborneFromStartTile(startTile);
        }
    }
}
