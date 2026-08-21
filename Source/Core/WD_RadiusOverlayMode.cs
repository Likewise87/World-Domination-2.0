using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Rollback switch for hop rings vs hex fills, plus shared draw helper.
    /// Fill RGB/alpha live in <see cref="WD_WorldLayer_OutpostCoverageFill"/>.
    /// </summary>
    public static class WD_RadiusOverlayMode
    {
        /// <summary>true = legacy hop rings; false = hex fills (default).</summary>
        public const bool UseHopRadiusRings = false;

        /// <summary>
        /// While targeting / hovering: hop ring if <see cref="UseHopRadiusRings"/>, else one-at-a-time hex fill.
        /// Pass <paramref name="accuracyBands"/> for mortar/AA concentric accuracy rings only.
        /// Pass <paramref name="attackRangeBands"/> for NPC Attack equal-quarter raid-range rings.
        /// </summary>
        public static void DrawOrFill(
            PlanetTile center,
            float radius,
            OutpostCoverageFillKind fillKind,
            Material hopRingMat,
            bool accuracyBands = false,
            bool attackRangeBands = false,
            bool zealAttackInnerCyan = false)
        {
            if (UseHopRadiusRings)
            {
                WorldMapRadiusVisual.DrawApproxRadiusRing(center, radius, hopRingMat);
                return;
            }

            RadiusFillHoverController.Begin(center, radius, fillKind, accuracyBands, attackRangeBands, zealAttackInnerCyan);
        }

        public static void DrawOrFill(
            WorldObject worldObject,
            float radius,
            OutpostCoverageFillKind fillKind,
            Material hopRingMat,
            bool accuracyBands = false,
            bool attackRangeBands = false,
            bool zealAttackInnerCyan = false)
        {
            if (worldObject == null || worldObject.Destroyed) return;
            PlanetLayer layer = PlanetSurfaceWorldActions.LayerOf(worldObject);
            DrawOrFill(new PlanetTile(worldObject.Tile, layer), radius, fillKind, hopRingMat, accuracyBands, attackRangeBands, zealAttackInnerCyan);
        }

        public static void DrawOrFill(
            int centerTileId,
            float radius,
            OutpostCoverageFillKind fillKind,
            Material hopRingMat,
            PlanetLayer layer = null,
            bool accuracyBands = false,
            bool attackRangeBands = false,
            bool zealAttackInnerCyan = false)
        {
            if (layer == null)
                layer = Find.WorldGrid?.Surface ?? WorldDomination_UIUtils.GetDefaultPlanetLayer();
            DrawOrFill(new PlanetTile(centerTileId, layer), radius, fillKind, hopRingMat, accuracyBands, attackRangeBands, zealAttackInnerCyan);
        }
    }
}
