using System.Collections;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Cursor-centered world overlay of Biotech tile pollution (0–100%). Mutual exclusive with other
    /// productivity / movement overlay modes.
    /// </summary>
    public class WD_WorldLayer_PollutionOverlay : WorldDrawLayer
    {
        public const int OverlayRadius = 20;
        private const float SurfaceOffset = 0.012f;
        private const float OverlayAlpha = 0.45f;
        private const int RenderQueue = 3580;
        /// <summary>~90 seconds at 1x (60 ticks/sec).</summary>
        private const int CacheTtlTicks = 5400;

        private const int BandClean = 0;
        private const int BandLight = 1;
        private const int BandYellow = 2;
        private const int BandOrange = 3;
        private const int BandRed = 4;

        private static readonly Dictionary<int, Material> MaterialsByBand = new Dictionary<int, Material>();
        private static float[] cachedPollution;
        private static int[] cachedScoreTicks;
        private static int[] cachedBands;
        private static PlanetTile centerTile = PlanetTile.Invalid;

        private readonly List<Vector3> tileVerts = new List<Vector3>(8);
        private readonly List<PlanetTile> neighborTiles = new List<PlanetTile>(8);
        private readonly Queue<PlanetTile> openTiles = new Queue<PlanetTile>();
        private readonly Dictionary<int, int> distancesByTileId = new Dictionary<int, int>();

        public override bool Visible =>
            WorldComponent_WDVisualizerToggle.ProductivityOverlayMode == WD_ProductivityOverlayMode.Pollution;

        public override bool VisibleWhenLayerNotSelected => true;
        public override bool VisibleInBackground => false;

        public static PlanetTile CenterTile => centerTile;

        public static bool SetCenterTile(PlanetTile tile)
        {
            if (centerTile == tile) return false;
            centerTile = tile;
            return true;
        }

        public static void InvalidateCache()
        {
            cachedPollution = null;
            cachedScoreTicks = null;
            cachedBands = null;
        }

        public static void InvalidateAndDirtyIfActive()
        {
            InvalidateCache();
            // Fertility / animals / fish scores depend on tile pollution; bust those caches too.
            WD_WorldLayer_ProductivityOverlay.InvalidateAndDirtyIfActive();
            if (WorldComponent_WDVisualizerToggle.ProductivityOverlayMode != WD_ProductivityOverlayMode.Pollution)
                return;
            SurfaceLayer surface = Find.WorldGrid?.Surface;
            if (surface == null) return;
            Find.World?.renderer?.SetDirty<WD_WorldLayer_PollutionOverlay>(surface);
        }

        public override IEnumerable Regenerate()
        {
            foreach (object step in base.Regenerate())
                yield return step;

            if (WorldComponent_WDVisualizerToggle.ProductivityOverlayMode != WD_ProductivityOverlayMode.Pollution)
            {
                FinalizeMesh(MeshParts.All);
                yield break;
            }

            WorldGrid grid = Find.WorldGrid;
            if (grid == null || !centerTile.Valid)
            {
                FinalizeMesh(MeshParts.All);
                yield break;
            }

            foreach (PlanetTile tile in EnumerateTilesInRadius(grid, centerTile, OverlayRadius))
            {
                Tile tileInfo = grid[tile];
                if (tileInfo == null || tileInfo.WaterCovered)
                    continue;

                int band = GetCachedBand(tile);
                Material material = GetMaterial(band);
                AddTileToSubMesh(grid, tile, material);
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

        /// <summary>Pollution display percent 0–100 for hover.</summary>
        public static int GetDisplayPollutionPercent(PlanetTile tile)
        {
            return Mathf.Clamp(Mathf.RoundToInt(GetCachedPollution(tile) * 100f), 0, 100);
        }

        public static float GetCachedPollution(PlanetTile tile)
        {
            EnsureCaches();
            int tileId = tile.tileId;
            if (tileId < 0 || tileId >= cachedPollution.Length) return 0f;

            int now = Find.TickManager?.TicksGame ?? 0;
            if (!float.IsNaN(cachedPollution[tileId]) && now - cachedScoreTicks[tileId] < CacheTtlTicks)
                return cachedPollution[tileId];

            float pollution = ReadPollution(tile);
            cachedPollution[tileId] = pollution;
            cachedScoreTicks[tileId] = now;
            cachedBands[tileId] = -1;
            return pollution;
        }

        private static int GetCachedBand(PlanetTile tile)
        {
            EnsureCaches();
            int tileId = tile.tileId;
            if (tileId < 0 || tileId >= cachedBands.Length) return BandClean;

            int now = Find.TickManager?.TicksGame ?? 0;
            if (cachedBands[tileId] >= 0
                && !float.IsNaN(cachedPollution[tileId])
                && now - cachedScoreTicks[tileId] < CacheTtlTicks)
            {
                return cachedBands[tileId];
            }

            float pollution = GetCachedPollution(tile);
            int band = PollutionToBand(pollution);
            cachedBands[tileId] = band;
            return band;
        }

        private static float ReadPollution(PlanetTile tile)
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || !tile.Valid) return 0f;
            Tile info = grid[tile];
            if (info == null) return 0f;
            return Mathf.Clamp01(info.pollution);
        }

        /// <summary>
        /// Bands by percent: 0 green, 1–20 light green, 21–40 yellow, 41–69 orange, 70–100 red.
        /// </summary>
        private static int PollutionToBand(float pollution01)
        {
            int pp = Mathf.Clamp(Mathf.RoundToInt(pollution01 * 100f), 0, 100);
            if (pp <= 0) return BandClean;
            if (pp <= 20) return BandLight;
            if (pp <= 40) return BandYellow;
            if (pp <= 69) return BandOrange;
            return BandRed;
        }

        private static void EnsureCaches()
        {
            int tilesCount = Find.WorldGrid?.TilesCount ?? 0;
            if (cachedPollution != null && cachedPollution.Length == tilesCount) return;

            cachedPollution = new float[tilesCount];
            cachedScoreTicks = new int[tilesCount];
            cachedBands = new int[tilesCount];
            for (int i = 0; i < tilesCount; i++)
            {
                cachedPollution[i] = float.NaN;
                cachedScoreTicks[i] = int.MinValue;
                cachedBands[i] = -1;
            }
        }

        private static Material GetMaterial(int band)
        {
            if (MaterialsByBand.TryGetValue(band, out Material material) && material != null)
                return material;

            Color color = GetBandColor(band);
            color.a = OverlayAlpha;
            material = MaterialPool.MatFrom(BaseContent.WhiteTex, ShaderDatabase.WorldOverlayTransparent, color, RenderQueue);
            MaterialsByBand[band] = material;
            return material;
        }

        private static Color GetBandColor(int band)
        {
            switch (band)
            {
                case BandClean:
                    return new Color(0.15f, 0.75f, 0.20f);
                case BandLight:
                    return new Color(0.55f, 0.90f, 0.40f);
                case BandYellow:
                    return new Color(1f, 0.92f, 0.25f);
                case BandOrange:
                    return new Color(1f, 0.55f, 0.12f);
                default:
                    return new Color(0.90f, 0.15f, 0.12f);
            }
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
