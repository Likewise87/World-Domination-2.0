using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using UnityEngine;

namespace TSA_WorldDomination
{
    [StaticConstructorOnStartup]
    [HarmonyPatch(typeof(WorldObject), nameof(WorldObject.DrawExtraSelectionOverlays))]
    public static class Patch_WorldObject_DrawAttackRadius
    {
        [HarmonyPostfix]
        public static void Postfix(WorldObject __instance)
        {
            Action_Outpost_BuildRoad.DrawRoadOverlayIfSelected(__instance);
            Action_Outpost_RoadBlocks.DrawRoadBlockOverlayIfSelected(__instance);
            Action_Outpost_SpikeTraps.DrawSpikeTrapOverlayIfSelected(__instance);
            Action_Outpost_AtTurrets.DrawAtTurretOverlayIfSelected(__instance);
            Action_Outpost_Decontamination.DrawDecontaminationOverlayIfSelected(__instance);
            // White routes for active construction crews from this origin (orange = full project only).
            WorldObject_Traveler.DrawConstructionTravelerPathsForOrigin(__instance);

            // Global-per-category radius fill/ring from toggle prefs (single select only).
            WD_RadiusOverlayPrefs.DrawSelectDrivenIfNeeded(__instance);
        }
    }
}
