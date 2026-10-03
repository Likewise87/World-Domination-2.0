using System.Collections.Generic;
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
    /// <b>camera-facing</b> icons only (Dot in the object's <see cref="PlanetLayer.Origin"/> frame) →
    /// near-surface camera chords false-positive the planet obstruction test and would blank every
    /// front-side settlement/outpost once Material is skipped. Far-side icons stay hidden so you do
    /// not see through the planet.</item>
    /// <item>VeryClose <see cref="GUI.DrawTexture"/> re-blit for ForceFixedIcon (with
    /// <see cref="WorldObject.ExpandingIconRotation"/>) → vanilla
    /// <c>ExpandableWorldObjectsOnGUI</c> can report would-draw but still leave no visible pixels at
    /// VeryClose once Material is skipped; this keeps the ExpandingIcon alive (no Material swap).</item>
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
        /// Mortar / AA / AT shells: hide at the same far zoom as road blocks / <see cref="MortarWorldFx"/>.
        /// Drop pods stay visible (do not use <see cref="WD_PathFollower.IsBallisticWorldFlight"/>).
        /// Must skip both Material layers or the tilted mesh reappears once TransitionPct is no longer forced.
        /// </summary>
        private static bool HideShellAtFarZoom(WorldObject wo)
        {
            if (wo is not WorldObject_Traveler t) return false;
            if (!WorldObject_Traveler.IsShellMission(t.mission)) return false;
            return WD_WorldMapZoomUtil.IsSurfaceOverlayZoomedTooFarOut();
        }

        /// <summary>
        /// WD float-menu "Road blocks, traps, and AT" (hotkey R): hide AT turret icons when off.
        /// Road blocks / spike traps gate their own overlay draw; AT is a WorldObject so it needs
        /// the same TransitionPct=0 + Material skip path as shells. Must run before
        /// <see cref="ForceFixedIcon"/> or TransitionPct would stay forced to 1.
        /// </summary>
        private static bool HideAtTurretWhenFortificationsHidden(WorldObject wo)
        {
            if (wo is not WorldObject_AT_Turret) return false;
            return !WorldComponent_WDVisualizerToggle.ShowRoadBlocksAndTraps;
        }

        /// <summary>True when this object's ExpandingIcon and Material should be fully suppressed.</summary>
        private static bool SuppressWorldIcon(WorldObject wo)
            => HideShellAtFarZoom(wo) || HideAtTurretWhenFortificationsHidden(wo);

        /// <summary>
        /// Call after changing either always-show-icon setting so close-zoom Material meshes rebuild.
        /// No game restart required — settings are read live; only the world draw layers are cached.
        /// Also call after flipping <see cref="WorldComponent_WDVisualizerToggle.ShowRoadBlocksAndTraps"/>.
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

        /// <summary>
        /// VeryClose: vanilla OnGUI can leave ForceFixedIcon blank while gates still say would-draw
        /// (Material already skipped → no fallback mesh). Re-blit ExpandingIcon with GUI.DrawTexture
        /// and apply <see cref="WorldObject.ExpandingIconRotation"/> (AT turrets, shells).
        /// </summary>
        [HarmonyPatch(typeof(ExpandableWorldObjectsUtility), nameof(ExpandableWorldObjectsUtility.ExpandableWorldObjectsOnGUI))]
        public static class ExpandableWorldObjectsOnGUI_VeryCloseBlit_Patch
        {
            [HarmonyPostfix]
            public static void Postfix()
            {
                if (!WorldRendererUtility.WorldSelected) return;
                if (Event.current.type != EventType.Repaint) return;

                WorldCameraDriver cam = Find.WorldCameraDriver;
                if (cam == null || cam.CurrentZoom != WorldCameraZoomRange.VeryClose) return;

                List<WorldObject> all = Find.WorldObjects?.AllWorldObjects;
                if (all == null) return;

                for (int i = 0; i < all.Count; i++)
                {
                    WorldObject wo = all[i];
                    if (wo == null || wo.Destroyed) continue;
                    if (!ForceFixedIcon(wo) || SuppressWorldIcon(wo)) continue;
                    if (wo.def == null || !wo.def.expandingIcon) continue;

                    float transition = ExpandableWorldObjectsUtility.TransitionPct(wo);
                    if (transition == 0f) continue;
                    if (wo.HiddenBehindTerrainNow()) continue;

                    Vector2 screen = wo.ScreenPos();
                    // Skip off-screen before tex/rect/blit (full AllWorldObjects scan at VeryClose).
                    if (screen.x < -40f || screen.y < -40f
                        || screen.x > UI.screenWidth + 40f || screen.y > UI.screenHeight + 40f)
                        continue;

                    Texture2D tex = wo.ExpandingIcon;
                    if (tex == null) continue;

                    Color c = wo.ExpandingIconColor;
                    c.a *= transition;
                    GUI.color = c;
                    Rect rect = ExpandableWorldObjectsUtility.ExpandedIconScreenRect(wo);
                    float angle = wo.ExpandingIconRotation;
                    if (Mathf.Abs(angle) > 0.01f)
                    {
                        Matrix4x4 matrix = GUI.matrix;
                        UI.RotateAroundPivot(angle, rect.center);
                        GUI.DrawTexture(rect, tex, ScaleMode.ScaleToFit, alphaBlend: true);
                        GUI.matrix = matrix;
                    }
                    else
                    {
                        GUI.DrawTexture(rect, tex, ScaleMode.ScaleToFit, alphaBlend: true);
                    }
                }

                GUI.color = Color.white;
            }
        }

        [HarmonyPatch(typeof(ExpandableWorldObjectsUtility), nameof(ExpandableWorldObjectsUtility.TransitionPct))]
        public static class TransitionPct_Patch
        {
            /// <summary>
            /// Skip vanilla VeryClose fade-to-zero for ForceFixedIcon so Material-skipped objects
            /// never lose their ExpandingIcon to the global transitionPct clamp.
            /// </summary>
            [HarmonyPrefix]
            [HarmonyPriority(Priority.First)]
            public static bool Prefix(WorldObject wo, ref float __result)
            {
                if (SuppressWorldIcon(wo))
                {
                    __result = 0f;
                    return false;
                }
                if (ForceFixedIcon(wo))
                {
                    __result = 1f;
                    return false;
                }
                return true;
            }

            [HarmonyPostfix]
            [HarmonyPriority(Priority.Last)]
            public static void Postfix(WorldObject wo, ref float __result)
            {
                if (SuppressWorldIcon(wo))
                {
                    __result = 0f;
                    return;
                }
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
                if (SuppressWorldIcon(worldObject) || ForceFixedIcon(worldObject))
                    __result = true;
            }
        }

        [HarmonyPatch(typeof(WorldDrawLayer_WorldObjects_NonExpandable), "ShouldSkip")]
        public static class NonExpandableLayer_ShouldSkip_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(WorldObject worldObject, ref bool __result)
            {
                if (SuppressWorldIcon(worldObject) || ForceFixedIcon(worldObject))
                    __result = true;
            }
        }

        /// <summary>
        /// At Close/VeryClose the camera sits near the surface. Segment camera→icon is a short chord that
        /// dips inside the Surface sphere, so <see cref="PlanetLayer.LineIntersects"/> reports obstruction
        /// even for on-screen front tiles. Vanilla then draws Material; we skipped that, so clear that
        /// false hide only for icons on the camera-facing hemisphere (in the object's
        /// <see cref="PlanetLayer.Origin"/> frame). Far-side icons keep vanilla hide so they do not show
        /// through the planet. Far/VeryFar zoom is unchanged (no bypass).
        /// </summary>
        [HarmonyPatch(typeof(WorldObjectSelectionUtility), nameof(WorldObjectSelectionUtility.HiddenBehindTerrainNow))]
        public static class HiddenBehindTerrainNow_Patch
        {
            [HarmonyPostfix]
            [HarmonyPriority(Priority.Last)]
            public static void Postfix(WorldObject o, ref bool __result)
            {
                if (!__result) return;
                if (!ForceFixedIcon(o)) return;
                WorldCameraDriver cam = Find.WorldCameraDriver;
                if (cam == null || (int)cam.CurrentZoom > (int)WorldCameraZoomRange.Close)
                    return;

                // Layer-origin frame: DrawPos / CameraPosition are Layer.Origin + sphere vector.
                // World-origin Dot fails when Origin is offset (or camera uses layer offset).
                Vector3 origin = Vector3.zero;
                if (o.Tile.Valid)
                {
                    PlanetLayer layer = o.Tile.Layer;
                    if (layer != null)
                        origin = layer.Origin;
                }

                Vector3 camPos = cam.CameraPosition - origin;
                Vector3 iconPos = o.DrawPos - origin;
                if (Vector3.Dot(iconPos, camPos) <= 0f)
                    return;

                __result = false;
            }
        }
    }
}
