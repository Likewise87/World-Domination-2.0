using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Optional (settings): always use the expanding (screen-upright) icon for WD outposts/travelers/turrets
    /// and/or vanilla settlements, and never draw the globe <see cref="WorldObject.Material"/> mesh.
    ///
    /// Why these hooks (do not remove any when a toggle is on):
    /// <list type="bullet">
    /// <item><see cref="ExpandableWorldObjectsUtility.TransitionPct"/> = 1 → expanding OnGUI icon at every zoom.</item>
    /// <item>Expandable layer <c>ShouldSkip</c> → never draw Material on the fade-with-zoom mesh layer.</item>
    /// <item>NonExpandable layer <c>ShouldSkip</c> → never draw Material there either.</item>
    /// <item><see cref="WorldObjectSelectionUtility.HiddenBehindTerrainNow"/> bypass at Close/VeryClose for
    /// <b>camera-facing</b> icons only → near-surface camera chords false-positive the planet obstruction
    /// test and would blank every front-side icon once Material is skipped. Far-side icons stay hidden
    /// (hemisphere Dot gate) so you do not see through the planet.</item>
    /// </list>
    /// Without the layer skips, zoomed-out camera would still show Material under the icon (double image).
    /// Side effect: skipping both draw layers means <see cref="WorldObject.Draw"/> never runs for those
    /// objects — traveler path polylines must be drawn from <see cref="WorldObject.DrawExtraSelectionOverlays"/> instead.
    ///
    /// When a toggle turns off, <see cref="NotifyIconModeChanged"/> must dirty those layers: TransitionPct
    /// reverts immediately (close-zoom expanding icons hide) but Material meshes stay empty until Regenerate.
    /// </summary>
    public static class Patch_WdWorldObjectNoExpandingIcon
    {
        private static bool ForceFixedIcon(WorldObject wo)
        {
            if (wo == null) return false;
            // Orbit / non-surface objects must keep vanilla layer visibility. Forcing expanding
            // icons + Close-zoom terrain-hide bypass makes space settlements pop onto the surface view.
            if (WorldActions_Utils.IsSpace(wo)) return false;
            var s = WorldDominationMod.settings;
            if (wo is WorldObject_WD_Outpost || wo is WorldObject_Traveler || wo is WorldObject_AT_Turret)
                return s?.alwaysShowOutpostTravelerIconsRegardlessOfZoom
                    ?? WorldDominationSettings.DefAlwaysShowOutpostTravelerIconsRegardlessOfZoom;
            if (wo is Settlement)
                return s?.alwaysShowSettlementIconsRegardlessOfZoom
                    ?? WorldDominationSettings.DefAlwaysShowSettlementIconsRegardlessOfZoom;
            return false;
        }

        /// <summary>
        /// Call after changing either always-show-icon setting so close-zoom Material meshes rebuild.
        /// No game restart required — settings are read live; only the world draw layers are cached.
        /// </summary>
        public static void NotifyIconModeChanged()
        {
            // Settings ExposeData / ResetNotifications can run before a world exists; Find.WorldGrid throws then.
            if (Find.World == null) return;
            SurfaceLayer surface = Find.WorldGrid?.Surface;
            if (surface == null) return;
            WorldRenderer renderer = Find.World.renderer;
            if (renderer == null) return;
            renderer.SetDirty<WorldDrawLayer_WorldObjects_Expandable>(surface);
            renderer.SetDirty<WorldDrawLayer_WorldObjects_NonExpandable>(surface);
        }

        [HarmonyPatch(typeof(ExpandableWorldObjectsUtility), nameof(ExpandableWorldObjectsUtility.TransitionPct))]
        public static class TransitionPct_Patch
        {
            [HarmonyPostfix]
            [HarmonyPriority(Priority.Last)]
            public static void Postfix(WorldObject wo, ref float __result)
            {
                if (!ForceFixedIcon(wo)) return;
                __result = 1f;
            }
        }

        [HarmonyPatch(typeof(WorldDrawLayer_WorldObjects_Expandable), "ShouldSkip")]
        public static class ExpandableLayer_ShouldSkip_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(WorldObject worldObject, ref bool __result)
            {
                if (ForceFixedIcon(worldObject))
                    __result = true;
            }
        }

        [HarmonyPatch(typeof(WorldDrawLayer_WorldObjects_NonExpandable), "ShouldSkip")]
        public static class NonExpandableLayer_ShouldSkip_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(WorldObject worldObject, ref bool __result)
            {
                if (ForceFixedIcon(worldObject))
                    __result = true;
            }
        }

        /// <summary>
        /// At Close/VeryClose the camera sits near the surface. Segment camera→icon is a short chord that
        /// dips inside the Surface sphere, so <see cref="PlanetLayer.LineIntersects"/> reports obstruction
        /// even for on-screen front tiles. Vanilla then draws Material; we skipped that, so clear that
        /// false hide only for icons on the camera-facing hemisphere. Far-side icons keep vanilla hide
        /// so they do not show through the planet. Far/VeryFar zoom is unchanged (no bypass).
        /// </summary>
        [HarmonyPatch(typeof(WorldObjectSelectionUtility), nameof(WorldObjectSelectionUtility.HiddenBehindTerrainNow))]
        public static class HiddenBehindTerrainNow_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(WorldObject o, ref bool __result)
            {
                if (!__result) return;
                if (!ForceFixedIcon(o)) return;
                WorldCameraDriver cam = Find.WorldCameraDriver;
                if (cam == null || (int)cam.CurrentZoom > (int)WorldCameraZoomRange.Close)
                    return;

                Camera worldCam = Find.WorldCamera;
                if (worldCam == null) return;

                // Planet center at origin: same half-space as the camera = front hemisphere.
                if (Vector3.Dot(o.DrawPos, worldCam.transform.position) <= 0f)
                    return;

                __result = false;
            }
        }
    }
}
