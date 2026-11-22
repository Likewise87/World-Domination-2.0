using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Single active hex-fill slot. Hover gizmos, dialogs, and world targeters call
    /// <see cref="Begin"/> each frame they want the fill; <see cref="EndFrame"/> clears if untouched.
    /// </summary>
    public static class RadiusFillHoverController
    {
        private static int touchedFrame = -1;

        public static bool IsActive =>
            !WD_RadiusOverlayMode.UseHopRadiusRings && WD_WorldLayer_OutpostCoverageFill.HasTarget;

        public static void Begin(
            PlanetTile center,
            float radius,
            OutpostCoverageFillKind kind,
            bool accuracyBands = false,
            bool attackRangeBands = false,
            bool zealAttackInnerCyan = false,
            float accuracyBandMaxRange = -1f,
            float allyInnerRadiusSplit = -1f,
            bool sightLosBands = false,
            float sightFracT1 = 0.50f,
            float sightFracT2 = 0.75f,
            float sightFracT3 = 1.00f)
        {
            // Sight always fills (ApproxDistance match). Other kinds respect hop-ring rollback.
            if (WD_RadiusOverlayMode.UseHopRadiusRings && !sightLosBands) return;
            if (!center.Valid || radius <= 0f) return;

            touchedFrame = Time.frameCount;
            if (!WorldComponent_WDVisualizerToggle.IsOutpostCoverageFillLayerRegisteredPublic())
                WorldComponent_WDVisualizerToggle.EnsureOutpostCoverageFillLayerRegisteredPublic();
            bool changed = WD_WorldLayer_OutpostCoverageFill.TrySetTarget(
                    center, radius, kind, accuracyBands, attackRangeBands, zealAttackInnerCyan,
                    accuracyBandMaxRange, allyInnerRadiusSplit, sightLosBands, sightFracT1, sightFracT2, sightFracT3);
            // Abandoned mid-regen leaves hasTarget set but an empty mesh; force dirty even if params match.
            if (changed || WorldComponent_WDVisualizerToggle.ConsumeCoverageFillForceRebuild())
                WorldComponent_WDVisualizerToggle.MarkOutpostCoverageFillDirtyPublic();
        }

        public static void Begin(
            WorldObject worldObject,
            float radius,
            OutpostCoverageFillKind kind,
            bool accuracyBands = false,
            bool attackRangeBands = false,
            bool zealAttackInnerCyan = false,
            float accuracyBandMaxRange = -1f,
            float allyInnerRadiusSplit = -1f,
            bool sightLosBands = false,
            float sightFracT1 = 0.50f,
            float sightFracT2 = 0.75f,
            float sightFracT3 = 1.00f)
        {
            if (worldObject == null || worldObject.Destroyed) return;
            PlanetLayer layer = PlanetSurfaceWorldActions.LayerOf(worldObject);
            Begin(
                new PlanetTile(worldObject.Tile, layer), radius, kind, accuracyBands, attackRangeBands,
                zealAttackInnerCyan, accuracyBandMaxRange, allyInnerRadiusSplit,
                sightLosBands, sightFracT1, sightFracT2, sightFracT3);
        }

        /// <summary>
        /// Call once per WorldComponentOnGUI. Keeps fill if touched this frame or last frame
        /// so world render still sees it when hover gizmos run after this OnGUI pass.
        /// </summary>
        public static void EndFrame()
        {
            if (WD_RadiusOverlayMode.UseHopRadiusRings) return;
            // Grace: touched this frame or previous (covers OnGUI order vs world draw).
            if (touchedFrame >= Time.frameCount - 1) return;
            if (WD_WorldLayer_OutpostCoverageFill.ClearTarget())
            {
                WorldComponent_WDVisualizerToggle.ClearCoverageFillForceRebuild();
                WorldComponent_WDVisualizerToggle.MarkOutpostCoverageFillDirtyPublic();
            }
        }
    }
}
