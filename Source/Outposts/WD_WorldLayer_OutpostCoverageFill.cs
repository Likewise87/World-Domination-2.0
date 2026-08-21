using System.Collections;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public enum OutpostCoverageFillKind : byte
    {
        Purple = 0,
        Green = 1,
        Cyan = 2,
        Red = 3,
        Orange = 4
    }

    /// <summary>
    /// Soft hex fill for ApproxDistance radius overlays (hover gizmos, dialogs, build/raid targeters).
    /// BFS only gathers candidate tiles; keep and band color use <see cref="WorldGrid.ApproxDistanceInTiles(PlanetTile, PlanetTile)"/>.
    /// Colors/alphas: edit <see cref="GetFillColor"/> / band alphas only.
    /// </summary>
    [StaticConstructorOnStartup]
    public class WD_WorldLayer_OutpostCoverageFill : WorldDrawLayer
    {
        private const float SurfaceOffset = 0.012f;
        private const int RenderQueue = 3585;

        // --- Tunable fill look (one place) ---
        private const float AlphaPurple = 0.36f;
        private const float AlphaGreen = 0.20f;
        private const float AlphaCyan = 0.18f;
        // Combat radius fills (attack / mortar / AA): keep denser than logistics greens.
        private const float AlphaRed = 0.28f;
        private const float AlphaOrange = 0.16f;
        // Mortar/AA accuracy bands (visual only). Inner matches Attack range red (@ AlphaRed);
        // outer bands shift hue (orange → yellow) with only a slight alpha taper.
        private const float AlphaAccuracyBand0 = AlphaRed;
        private const float AlphaAccuracyBand1 = 0.26f;
        private const float AlphaAccuracyBand2 = 0.24f;
        // NPC Attack equal-quarter raid-range bands (red → orange → yellow → pale yellow-green).
        private const float AlphaAttackBand0 = AlphaRed;
        private const float AlphaAttackBand0Zeal = 0.28f;
        private const float AlphaAttackBand1 = 0.26f;
        private const float AlphaAttackBand2 = 0.24f;
        private const float AlphaAttackBand3 = 0.22f;

        private static Material? purpleFillMaterial;
        private static Material? greenFillMaterial;
        private static Material? cyanFillMaterial;
        private static Material? redFillMaterial;
        private static Material? orangeFillMaterial;
        private static Material? accuracyBand0FillMaterial;
        private static Material? accuracyBand1FillMaterial;
        private static Material? accuracyBand2FillMaterial;
        // NPC Attack equal-quarter bands (separate from mortar/AA 3-band mats).
        private static Material? attackBand0FillMaterial;
        private static Material? attackBand0ZealFillMaterial;
        private static Material? attackBand1FillMaterial;
        private static Material? attackBand2FillMaterial;
        private static Material? attackBand3FillMaterial;

        private static bool hasTarget;
        private static PlanetTile centerTile = PlanetTile.Invalid;
        private static int radiusTiles;
        private static OutpostCoverageFillKind fillKind;
        private static bool accuracyBands;
        private static bool attackRangeBands;
        private static bool zealAttackInnerCyan;

        private readonly List<Vector3> tileVerts = new List<Vector3>(8);
        private readonly List<PlanetTile> neighborTiles = new List<PlanetTile>(8);
        private readonly Queue<PlanetTile> openTiles = new Queue<PlanetTile>();
        private readonly Dictionary<int, int> distancesByTileId = new Dictionary<int, int>();

        public static bool HasTarget => hasTarget;

        public override bool Visible => hasTarget;
        public override bool VisibleWhenLayerNotSelected => true;
        public override bool VisibleInBackground => false;

        public static Color GetFillColor(OutpostCoverageFillKind kind)
        {
            switch (kind)
            {
                case OutpostCoverageFillKind.Green:
                    return new Color(Color.green.r, Color.green.g, Color.green.b, AlphaGreen);
                case OutpostCoverageFillKind.Cyan:
                    return new Color(Color.cyan.r, Color.cyan.g, Color.cyan.b, AlphaCyan);
                case OutpostCoverageFillKind.Red:
                    return new Color(Color.red.r, Color.red.g, Color.red.b, AlphaRed);
                case OutpostCoverageFillKind.Orange:
                {
                    Color o = ColorLibrary.Orange;
                    return new Color(o.r, o.g, o.b, AlphaOrange);
                }
                default:
                {
                    Color p = WorldOverlayLineMaterials.RecruitPurple;
                    return new Color(p.r, p.g, p.b, AlphaPurple);
                }
            }
        }

        /// <summary>
        /// Mortar/AA accuracy-band fill colors (visual only). Band 0 matches Attack red;
        /// bands 1–2 use orange then yellow at similar alpha.
        /// </summary>
        public static Color GetBandedFillColor(OutpostCoverageFillKind kind, int bandIndex)
        {
            if (kind != OutpostCoverageFillKind.Red)
                return GetFillColor(kind);

            switch (bandIndex)
            {
                case 0:
                    return new Color(Color.red.r, Color.red.g, Color.red.b, AlphaAccuracyBand0);
                case 1:
                {
                    Color o = ColorLibrary.Orange;
                    return new Color(o.r, o.g, o.b, AlphaAccuracyBand1);
                }
                default:
                    return new Color(Color.yellow.r, Color.yellow.g, Color.yellow.b, AlphaAccuracyBand2);
            }
        }

        /// <summary>
        /// NPC Attack equal-quarter raid-range fill colors. Band 0 is cyan under zeal; otherwise red → orange → yellow → pale yellow-green.
        /// Separate from mortar/AA <see cref="GetBandedFillColor"/>.
        /// </summary>
        public static Color GetAttackRangeBandFillColor(int bandIndex, bool zealInnerCyan)
        {
            if (bandIndex <= 0)
            {
                if (zealInnerCyan)
                    return new Color(Color.cyan.r, Color.cyan.g, Color.cyan.b, AlphaAttackBand0Zeal);
                return new Color(Color.red.r, Color.red.g, Color.red.b, AlphaAttackBand0);
            }
            if (bandIndex == 1)
            {
                Color o = ColorLibrary.Orange;
                return new Color(o.r, o.g, o.b, AlphaAttackBand1);
            }
            if (bandIndex == 2)
                return new Color(Color.yellow.r, Color.yellow.g, Color.yellow.b, AlphaAttackBand2);
            // Pale yellow-green
            return new Color(0.72f, 0.88f, 0.35f, AlphaAttackBand3);
        }

        /// <summary>Radius + kind for hover gizmos (people / warehouse / food).</summary>
        public static bool TryGetCoverage(WorldObject_WD_Outpost outpost, out int radius, out OutpostCoverageFillKind kind)
        {
            radius = 0;
            kind = OutpostCoverageFillKind.Purple;
            if (outpost?.def == null) return false;

            if (Outpost_Production_Utils.IsTradingOutpost(outpost.def))
            {
                radius = Outpost_Trading.GetNearbyRadiusTiles(outpost);
                kind = OutpostCoverageFillKind.Purple;
                return radius > 0;
            }

            if (Outpost_Production_Utils.IsEmbassyOutpost(outpost.def))
            {
                radius = Outpost_Embassy.GetNearbyRadiusTiles(outpost);
                kind = OutpostCoverageFillKind.Purple;
                return radius > 0;
            }

            if (Outpost_Production_Utils.IsRecruitingOutpost(outpost.def))
            {
                var ext = outpost.def.GetModExtension<OutpostDefExtension>();
                if (ext != null && ext.minNearbyRadiusTiles > 0)
                    radius = ext.minNearbyRadiusTiles;
                kind = OutpostCoverageFillKind.Purple;
                return radius > 0;
            }

            if (Outpost_Production_Utils.IsWarehouseOutpost(outpost.def))
            {
                radius = (int)Mathf.Ceil(OutpostWarehouseAuraUtility.GetWarehouseAuraRadiusTiles(outpost));
                kind = OutpostCoverageFillKind.Purple;
                return radius > 0;
            }

            if (Outpost_Production_Utils.IsFoodProducerOutpost(outpost.def))
            {
                var settings = WorldDominationMod.settings;
                if (settings == null) return false;
                radius = settings.maxLogisticsRange;
                kind = OutpostCoverageFillKind.Green;
                return radius > 0;
            }

            return false;
        }

        /// <summary>Returns true if the target changed (caller should dirty the layer).</summary>
        public static bool TrySetTarget(
            PlanetTile center,
            float radius,
            OutpostCoverageFillKind kind,
            bool accuracyBands = false,
            bool attackRangeBands = false,
            bool zealAttackInnerCyan = false)
        {
            int r = Mathf.CeilToInt(radius);
            if (!center.Valid || r <= 0)
                return ClearTarget();

            if (hasTarget && centerTile == center && radiusTiles == r && fillKind == kind
                && WD_WorldLayer_OutpostCoverageFill.accuracyBands == accuracyBands
                && WD_WorldLayer_OutpostCoverageFill.attackRangeBands == attackRangeBands
                && WD_WorldLayer_OutpostCoverageFill.zealAttackInnerCyan == zealAttackInnerCyan)
                return false;

            hasTarget = true;
            centerTile = center;
            radiusTiles = r;
            fillKind = kind;
            WD_WorldLayer_OutpostCoverageFill.accuracyBands = accuracyBands;
            WD_WorldLayer_OutpostCoverageFill.attackRangeBands = attackRangeBands;
            WD_WorldLayer_OutpostCoverageFill.zealAttackInnerCyan = zealAttackInnerCyan;
            return true;
        }

        /// <summary>Legacy helper from outpost selection era.</summary>
        public static bool TrySetTarget(
            WorldObject_WD_Outpost outpost,
            int radius,
            OutpostCoverageFillKind kind,
            bool accuracyBands = false,
            bool attackRangeBands = false,
            bool zealAttackInnerCyan = false)
        {
            if (outpost == null || radius <= 0)
                return ClearTarget();
            PlanetLayer layer = PlanetSurfaceWorldActions.LayerOf(outpost);
            return TrySetTarget(new PlanetTile(outpost.Tile, layer), radius, kind, accuracyBands, attackRangeBands, zealAttackInnerCyan);
        }

        /// <summary>Returns true if a previous target was cleared.</summary>
        public static bool ClearTarget()
        {
            if (!hasTarget) return false;
            hasTarget = false;
            centerTile = PlanetTile.Invalid;
            radiusTiles = 0;
            fillKind = OutpostCoverageFillKind.Purple;
            accuracyBands = false;
            attackRangeBands = false;
            zealAttackInnerCyan = false;
            return true;
        }

        public override IEnumerable Regenerate()
        {
            foreach (object step in base.Regenerate())
                yield return step;

            if (!hasTarget || !centerTile.Valid || radiusTiles <= 0)
            {
                FinalizeMesh(MeshParts.All);
                yield break;
            }

            WorldGrid grid = Find.WorldGrid;
            if (grid == null)
            {
                FinalizeMesh(MeshParts.All);
                yield break;
            }

            Material solidMat = GetFillMaterial(fillKind);
            // Hop walk is a search bound so circle flats (and icosphere stretch) are reached.
            // Keep-test below is ApproxDistance, matching gameplay ranges.
            int hopWindow = Mathf.Min(
                Mathf.Max(
                    WorldMapRadiusVisual.GetHopDrawRadius(radiusTiles),
                    Mathf.Max(Mathf.CeilToInt(radiusTiles * 1.2f), radiusTiles + 2)),
                WorldMapRadiusVisual.MaxVisualHopRadius);
            // Band coloring uses the drawn disk so capped large-R overlays still show all bands.
            float bandMax = Mathf.Min(radiusTiles, hopWindow);

            // Checkpoints for WorldComponent_WDVisualizerToggle progressive regen.
            // Bare SetDirty→RegenerateNow drains yields in one frame; the slicer stops on null.
            const int yieldEveryLandTiles = 200;
            int landProcessed = 0;

            foreach (PlanetTile tile in EnumerateTilesInRadius(grid, centerTile, hopWindow))
            {
                Tile tileInfo = grid[tile];
                if (tileInfo == null || tileInfo.WaterCovered)
                    continue;

                float approxDist = grid.ApproxDistanceInTiles(centerTile, tile);
                if (approxDist > radiusTiles)
                    continue;

                Material mat = solidMat;
                if (attackRangeBands)
                {
                    int band = WD_TargetDistanceBandOrder.BandIndex(approxDist, bandMax);
                    mat = GetAttackRangeBandMaterial(band, zealAttackInnerCyan);
                }
                else if (accuracyBands && fillKind == OutpostCoverageFillKind.Red)
                {
                    int band = MortarFireUtils.GetAccuracyBandIndex(approxDist, bandMax);
                    mat = GetAccuracyBandMaterial(band);
                }

                AddTileToSubMesh(grid, tile, mat);
                landProcessed++;
                if (landProcessed % yieldEveryLandTiles == 0)
                    yield return null;
            }

            FinalizeMesh(MeshParts.All);
        }

        private IEnumerable<PlanetTile> EnumerateTilesInRadius(WorldGrid grid, PlanetTile root, int radius)
        {
            openTiles.Clear();
            distancesByTileId.Clear();

            openTiles.Enqueue(root);
            distancesByTileId[root.tileId] = 0;

            while (openTiles.Count > 0)
            {
                PlanetTile tile = openTiles.Dequeue();
                int distance = distancesByTileId[tile.tileId];
                yield return tile;

                if (distance >= radius) continue;

                neighborTiles.Clear();
                grid.GetTileNeighbors(tile, neighborTiles);
                for (int i = 0; i < neighborTiles.Count; i++)
                {
                    PlanetTile neighbor = neighborTiles[i];
                    if (!neighbor.Valid || distancesByTileId.ContainsKey(neighbor.tileId)) continue;
                    distancesByTileId[neighbor.tileId] = distance + 1;
                    openTiles.Enqueue(neighbor);
                }
            }
        }

        private static Material GetFillMaterial(OutpostCoverageFillKind kind)
        {
            switch (kind)
            {
                case OutpostCoverageFillKind.Green:
                    return greenFillMaterial ??= MakeFillMat(GetFillColor(kind));
                case OutpostCoverageFillKind.Cyan:
                    return cyanFillMaterial ??= MakeFillMat(GetFillColor(kind));
                case OutpostCoverageFillKind.Red:
                    return redFillMaterial ??= MakeFillMat(GetFillColor(kind));
                case OutpostCoverageFillKind.Orange:
                    return orangeFillMaterial ??= MakeFillMat(GetFillColor(kind));
                default:
                    return purpleFillMaterial ??= MakeFillMat(GetFillColor(kind));
            }
        }

        private static Material GetAccuracyBandMaterial(int bandIndex)
        {
            switch (bandIndex)
            {
                case 0:
                    return accuracyBand0FillMaterial ??= MakeFillMat(GetBandedFillColor(OutpostCoverageFillKind.Red, 0));
                case 1:
                    return accuracyBand1FillMaterial ??= MakeFillMat(GetBandedFillColor(OutpostCoverageFillKind.Red, 1));
                default:
                    return accuracyBand2FillMaterial ??= MakeFillMat(GetBandedFillColor(OutpostCoverageFillKind.Red, 2));
            }
        }

        private static Material GetAttackRangeBandMaterial(int bandIndex, bool zealInnerCyan)
        {
            if (bandIndex <= 0)
            {
                if (zealInnerCyan)
                    return attackBand0ZealFillMaterial ??= MakeFillMat(GetAttackRangeBandFillColor(0, true));
                return attackBand0FillMaterial ??= MakeFillMat(GetAttackRangeBandFillColor(0, false));
            }
            if (bandIndex == 1)
                return attackBand1FillMaterial ??= MakeFillMat(GetAttackRangeBandFillColor(1, false));
            if (bandIndex == 2)
                return attackBand2FillMaterial ??= MakeFillMat(GetAttackRangeBandFillColor(2, false));
            return attackBand3FillMaterial ??= MakeFillMat(GetAttackRangeBandFillColor(3, false));
        }

        private static Material MakeFillMat(Color color)
        {
            return MaterialPool.MatFrom(BaseContent.WhiteTex, ShaderDatabase.WorldOverlayTransparent, color, RenderQueue);
        }

        private void AddTileToSubMesh(WorldGrid grid, PlanetTile tile, Material material)
        {
            tileVerts.Clear();
            grid.GetTileVertices(tile, tileVerts);
            if (tileVerts.Count < 3) return;

            LayerSubMesh subMesh = GetSubMesh(material);
            int baseIndex = subMesh.verts.Count;
            for (int i = 0; i < tileVerts.Count; i++)
            {
                Vector3 v = tileVerts[i];
                subMesh.verts.Add(v + v.normalized * SurfaceOffset);
                subMesh.uvs.Add((GenGeo.RegularPolygonVertexPosition(tileVerts.Count, i) + Vector2.one) / 2f);
            }

            for (int i = 1; i < tileVerts.Count - 1; i++)
            {
                subMesh.tris.Add(baseIndex + i + 1);
                subMesh.tris.Add(baseIndex + i);
                subMesh.tris.Add(baseIndex);
            }
        }
    }
}
