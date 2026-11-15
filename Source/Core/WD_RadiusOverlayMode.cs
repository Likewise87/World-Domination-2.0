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
        /// Pass <paramref name="accuracyBandMaxRange"/> (absolute configured max) so shrink only clips the disk; colors stay locked to full max.
        /// Pass <paramref name="attackRangeBands"/> for NPC Attack equal-quarter raid-range rings.
        /// Pass <paramref name="allyInnerRadiusSplit"/> for Tunnel Network ally annulus (Cyan inner / CyanDeep outer).
        /// Pass <paramref name="sightLosBands"/> for Intel MaxLOS target-size discovery rings.
        /// </summary>
        public static void DrawOrFill(
            PlanetTile center,
            float radius,
            OutpostCoverageFillKind fillKind,
            Material hopRingMat,
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
            if (!PlanetSurfaceWorldActions.IsPlanetSurfaceLayerSelected())
                return;
            if (UseHopRadiusRings)
            {
                if (sightLosBands)
                {
                    float f1 = Mathf.Clamp01(sightFracT1);
                    float f2 = Mathf.Clamp01(Mathf.Max(sightFracT1, sightFracT2));
                    float f3 = Mathf.Clamp01(Mathf.Max(sightFracT2, sightFracT3));
                    WorldMapRadiusVisual.DrawApproxRadiusRing(center, radius * f1, WorldOverlayLineMaterials.RadiusRed);
                    if (f2 > f1 + 0.01f)
                        WorldMapRadiusVisual.DrawApproxRadiusRing(center, radius * f2, WorldOverlayLineMaterials.RadiusOrange);
                    if (f3 > f2 + 0.01f)
                        WorldMapRadiusVisual.DrawApproxRadiusRing(center, radius * f3, WorldOverlayLineMaterials.RadiusYellow);
                    if (radius > radius * f3 + 0.05f)
                        WorldMapRadiusVisual.DrawApproxRadiusRing(center, radius, WorldOverlayLineMaterials.RadiusGold);
                    else
                        WorldMapRadiusVisual.DrawApproxRadiusRing(center, radius, hopRingMat ?? WorldOverlayLineMaterials.RadiusGold);
                }
                else if (allyInnerRadiusSplit > 0f && fillKind == OutpostCoverageFillKind.Cyan)
                {
                    WorldMapRadiusVisual.DrawApproxRadiusRing(center, allyInnerRadiusSplit, WorldOverlayLineMaterials.RadiusCyan);
                    WorldMapRadiusVisual.DrawApproxRadiusRing(center, radius, WorldOverlayLineMaterials.RadiusCyanDeep);
                }
                else
                {
                    WorldMapRadiusVisual.DrawApproxRadiusRing(center, radius, hopRingMat);
                }
                return;
            }

            RadiusFillHoverController.Begin(
                center, radius, fillKind, accuracyBands, attackRangeBands, zealAttackInnerCyan,
                accuracyBandMaxRange, allyInnerRadiusSplit, sightLosBands, sightFracT1, sightFracT2, sightFracT3);
        }

        public static void DrawOrFill(
            WorldObject worldObject,
            float radius,
            OutpostCoverageFillKind fillKind,
            Material hopRingMat,
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
            DrawOrFill(
                new PlanetTile(worldObject.Tile, layer), radius, fillKind, hopRingMat,
                accuracyBands, attackRangeBands, zealAttackInnerCyan, accuracyBandMaxRange, allyInnerRadiusSplit,
                sightLosBands, sightFracT1, sightFracT2, sightFracT3);
        }

        public static void DrawOrFill(
            int centerTileId,
            float radius,
            OutpostCoverageFillKind fillKind,
            Material hopRingMat,
            PlanetLayer layer = null,
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
            if (layer == null)
                layer = Find.WorldGrid?.Surface ?? WorldDomination_UIUtils.GetDefaultPlanetLayer();
            DrawOrFill(
                new PlanetTile(centerTileId, layer), radius, fillKind, hopRingMat,
                accuracyBands, attackRangeBands, zealAttackInnerCyan, accuracyBandMaxRange, allyInnerRadiusSplit,
                sightLosBands, sightFracT1, sightFracT2, sightFracT3);
        }
    }
}
