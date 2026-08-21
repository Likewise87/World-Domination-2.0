using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// World-map label/FX visibility vs camera zoom. <see cref="WorldCameraDriver.AltitudePercent"/>
    /// is normalized to the planet's altitude range, so smaller worlds (My Little Planet, reduced
    /// radius/subdivisions) sit higher in that range at a "comfortable" overview — labels would
    /// vanish too early with a fixed cutoff. We raise the allowed altitude when tile count is
    /// below a vanilla-sized reference surface.
    /// </summary>
    public static class WD_WorldMapZoomUtil
    {
        /// <summary>
        /// Approx. surface tile count for a default-radius vanilla planet mesh (not globe coverage %).
        /// My Little Planet / similar mods reduce this by shrinking radius/subdivisions.
        /// </summary>
        public const int ReferenceSurfaceTileCount = 25000;

        private const float MaxScaledAltitudePercent = 0.90f;

        private static int cachedTiles = -1;
        private static float cachedSizeScale = 1f;

        /// <summary>Scale ≥ 1 on smaller-than-reference worlds (√ of inverse tile ratio).</summary>
        public static float GetSmallWorldAltitudeScale()
        {
            int tiles = Find.WorldGrid?.TilesCount ?? ReferenceSurfaceTileCount;
            if (tiles <= 0) tiles = ReferenceSurfaceTileCount;
            if (tiles == cachedTiles) return cachedSizeScale;

            cachedTiles = tiles;
            float relative = Mathf.Max(0.05f, tiles / (float)ReferenceSurfaceTileCount);
            cachedSizeScale = 1f / Mathf.Sqrt(relative);
            return cachedSizeScale;
        }

        /// <summary>
        /// Max <see cref="WorldCameraDriver.AltitudePercent"/> at which labels/FX still draw.
        /// Equals <paramref name="baseAtNormalWorld"/> on a normal-sized planet; higher on small ones.
        /// </summary>
        public static float GetMaxAltitudePercent(float baseAtNormalWorld)
        {
            float scaled = baseAtNormalWorld * GetSmallWorldAltitudeScale();
            return Mathf.Clamp(scaled, baseAtNormalWorld, MaxScaledAltitudePercent);
        }

        public static bool IsZoomedTooFarOut(float baseAtNormalWorld)
        {
            float altitude = Find.WorldCameraDriver?.AltitudePercent ?? 1f;
            return altitude > GetMaxAltitudePercent(baseAtNormalWorld);
        }

        /// <summary>
        /// Hide planet-surface overlays (road blocks, spike traps) at far zoom.
        /// The cutoff is raised on tiny planets via <see cref="GetMaxAltitudePercent"/>.
        /// Prefer this over <see cref="WorldCameraZoomRange.VeryFar"/> — that enum advances too early
        /// on small worlds where <see cref="WorldCameraDriver.AltitudePercent"/> sits high at overview.
        /// </summary>
        public const float SurfaceOverlayHideAltitudePercent = 0.40f;

        /// <summary>
        /// Hide selected-traveler path polylines. Same base cutoff as surface overlays so routes
        /// stay visible at medium zoom but clear the overview when pulled back quite far.
        /// </summary>
        public const float TravelerPathHideAltitudePercent = 0.40f;

        public static bool IsSurfaceOverlayZoomedTooFarOut()
            => IsZoomedTooFarOut(SurfaceOverlayHideAltitudePercent);

        /// <summary>
        /// Draw-size multiplier for planet-surface overlay quads (road blocks, spike traps).
        /// 1 on a normal-sized planet; &lt; 1 on tiny planets so <see cref="WorldGrid.AverageTileSize"/>-based
        /// quads do not dominate the smaller globe (inverse of <see cref="GetSmallWorldAltitudeScale"/>).
        /// On small worlds the shrink is eased by +30% so icons stay readable.
        /// </summary>
        public static float GetSurfaceOverlayDrawScale()
        {
            float altScale = GetSmallWorldAltitudeScale();
            float scale = 1f / Mathf.Clamp(altScale, 1f, 2.5f);
            if (scale < 1f)
                scale *= 1.3f;
            return scale;
        }

        /// <summary>World-space quad size for a surface overlay: tile-fraction × small-world draw scale.</summary>
        public static float GetSurfaceOverlayQuadSize(float averageTileSizeFraction)
        {
            float avg = Find.WorldGrid?.AverageTileSize ?? 1f;
            return avg * averageTileSizeFraction * GetSurfaceOverlayDrawScale();
        }

        /// <summary>
        /// Altitude for planet-tangent overlay quads (road blocks, spike traps).
        /// Higher at Close/VeryClose so hills / terrain depth do not swallow the icon.
        /// </summary>
        public static float GetSurfaceOverlayDrawAltitude()
        {
            WorldCameraDriver cam = Find.WorldCameraDriver;
            if (cam != null && (int)cam.CurrentZoom <= (int)WorldCameraZoomRange.Close)
                return 0.085f;
            return 0.018f;
        }
    }
}
