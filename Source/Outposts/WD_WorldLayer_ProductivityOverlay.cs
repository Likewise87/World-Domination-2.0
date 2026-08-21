using System.Collections;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public class WD_WorldLayer_ProductivityOverlay : WorldDrawLayer
    {
        private const int BandCount = 40;
        public const int OverlayRadius = 20;
        private const float SurfaceOffset = 0.012f;
        private const float OverlayAlpha = 0.45f;
        private const int RenderQueue = 3580;

        private static readonly Dictionary<int, Material> MaterialsByBand = new Dictionary<int, Material>();
        private static readonly Dictionary<WD_ProductivityOverlayMode, int[]> CachedBandsByMode = new Dictionary<WD_ProductivityOverlayMode, int[]>();
        private static readonly Dictionary<WD_ProductivityOverlayMode, float[]> CachedScoresByMode = new Dictionary<WD_ProductivityOverlayMode, float[]>();
        /// <summary>Pollution fingerprint used when the score was cached (-1 = ecology penalty off / N/A).</summary>
        private static readonly Dictionary<WD_ProductivityOverlayMode, float[]> CachedPollutionKeyByMode = new Dictionary<WD_ProductivityOverlayMode, float[]>();
        private static PlanetTile centerTile = PlanetTile.Invalid;

        private readonly List<Vector3> tileVerts = new List<Vector3>(8);
        private readonly List<PlanetTile> neighborTiles = new List<PlanetTile>(8);
        private readonly Queue<PlanetTile> openTiles = new Queue<PlanetTile>();
        private readonly Dictionary<int, int> distancesByTileId = new Dictionary<int, int>();

        public override bool Visible => WorldComponent_WDVisualizerToggle.IsProductivityScoreOverlayActive();
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
            CachedBandsByMode.Clear();
            CachedScoresByMode.Clear();
            CachedPollutionKeyByMode.Clear();
        }

        /// <summary>Drop score caches and redraw if a fertility/animals/fish/mining overlay is active.</summary>
        public static void InvalidateAndDirtyIfActive()
        {
            InvalidateCache();
            if (!WorldComponent_WDVisualizerToggle.IsProductivityScoreOverlayActive())
                return;
            SurfaceLayer surface = Find.WorldGrid?.Surface;
            if (surface == null) return;
            Find.World?.renderer?.SetDirty<WD_WorldLayer_ProductivityOverlay>(surface);
        }

        public override IEnumerable Regenerate()
        {
            foreach (object step in base.Regenerate())
                yield return step;

            WD_ProductivityOverlayMode mode = WorldComponent_WDVisualizerToggle.ProductivityOverlayMode;
            if (!WorldComponent_WDVisualizerToggle.IsProductivityScoreOverlayActive())
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

                int band = GetCachedBand(mode, tile);
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

        private static float GetScore(WD_ProductivityOverlayMode mode, int tile)
        {
            float score;
            switch (mode)
            {
                case WD_ProductivityOverlayMode.Fertility:
                    score = WorldTileProductivity.GetFarmingFertilityScore(tile);
                    break;
                case WD_ProductivityOverlayMode.AnimalAbundance:
                    score = WorldTileProductivity.GetHuntingScore(tile);
                    break;
                case WD_ProductivityOverlayMode.FishAbundance:
                    score = WorldTileProductivity.GetFishingScore(tile);
                    break;
                case WD_ProductivityOverlayMode.MiningRichness:
                    score = WorldTileProductivity.GetMiningOutputMultiplier(tile);
                    break;
                default:
                    score = 0f;
                    break;
            }
            return Mathf.Clamp(score, 0f, WorldTileProductivity.ProductivityScoreCap);
        }

        private static bool ModeUsesPollutionEcology(WD_ProductivityOverlayMode mode)
        {
            return mode == WD_ProductivityOverlayMode.Fertility
                || mode == WD_ProductivityOverlayMode.AnimalAbundance
                || mode == WD_ProductivityOverlayMode.FishAbundance;
        }

        /// <summary>Fingerprint for ecology-affected modes so debug pollution / setting toggles bust stale scores.</summary>
        private static float GetPollutionCacheKey(WD_ProductivityOverlayMode mode, int tileId)
        {
            if (!ModeUsesPollutionEcology(mode))
                return -1f;
            var seth = WorldDominationMod.settings;
            if (seth == null || !seth.pollutionEcologyPenaltyEnabled)
                return -1f;
            return WorldTileProductivity.GetTilePollution01(tileId);
        }

        private static int GetCachedBand(WD_ProductivityOverlayMode mode, PlanetTile tile)
        {
            int tileId = tile.tileId;
            int[] bands = GetCacheForMode(mode);
            if (tileId < 0 || tileId >= bands.Length) return 0;

            float liveKey = GetPollutionCacheKey(mode, tileId);
            float[] polKeys = GetPollutionKeyCacheForMode(mode);
            int cached = bands[tileId];
            if (cached >= 0 && Mathf.Approximately(polKeys[tileId], liveKey))
                return cached;

            int band = ScoreToBand(GetCachedScore(mode, tile));
            bands[tileId] = band;
            return band;
        }

        public static float GetCachedScore(WD_ProductivityOverlayMode mode, PlanetTile tile)
        {
            int tileId = tile.tileId;
            float[] scores = GetScoreCacheForMode(mode);
            if (tileId < 0 || tileId >= scores.Length) return 0f;

            float liveKey = GetPollutionCacheKey(mode, tileId);
            float[] polKeys = GetPollutionKeyCacheForMode(mode);
            float cached = scores[tileId];
            if (!float.IsNaN(cached) && Mathf.Approximately(polKeys[tileId], liveKey))
                return cached;

            float score = GetScore(mode, tileId);
            scores[tileId] = score;
            polKeys[tileId] = liveKey;
            // Band must be recomputed when score invalidates.
            int[] bands = GetCacheForMode(mode);
            if (tileId >= 0 && tileId < bands.Length)
                bands[tileId] = -1;
            return score;
        }

        private static int[] GetCacheForMode(WD_ProductivityOverlayMode mode)
        {
            int tilesCount = Find.WorldGrid?.TilesCount ?? 0;
            if (!CachedBandsByMode.TryGetValue(mode, out int[] bands) || bands == null || bands.Length != tilesCount)
            {
                bands = new int[tilesCount];
                for (int i = 0; i < bands.Length; i++)
                    bands[i] = -1;
                CachedBandsByMode[mode] = bands;
            }
            return bands;
        }

        private static float[] GetScoreCacheForMode(WD_ProductivityOverlayMode mode)
        {
            int tilesCount = Find.WorldGrid?.TilesCount ?? 0;
            if (!CachedScoresByMode.TryGetValue(mode, out float[] scores) || scores == null || scores.Length != tilesCount)
            {
                scores = new float[tilesCount];
                for (int i = 0; i < scores.Length; i++)
                    scores[i] = float.NaN;
                CachedScoresByMode[mode] = scores;
            }
            return scores;
        }

        private static float[] GetPollutionKeyCacheForMode(WD_ProductivityOverlayMode mode)
        {
            int tilesCount = Find.WorldGrid?.TilesCount ?? 0;
            if (!CachedPollutionKeyByMode.TryGetValue(mode, out float[] keys) || keys == null || keys.Length != tilesCount)
            {
                keys = new float[tilesCount];
                for (int i = 0; i < keys.Length; i++)
                    keys[i] = float.NaN;
                CachedPollutionKeyByMode[mode] = keys;
            }
            return keys;
        }

        private static int ScoreToBand(float score)
        {
            float normalized = Mathf.Clamp01(score / WorldTileProductivity.ProductivityScoreCap);
            return Mathf.Clamp(Mathf.RoundToInt(normalized * (BandCount - 1)), 0, BandCount - 1);
        }

        private static Material GetMaterial(int band)
        {
            if (MaterialsByBand.TryGetValue(band, out Material material) && material != null)
                return material;

            float score = band / (float)(BandCount - 1) * WorldTileProductivity.ProductivityScoreCap;
            Color color = GetScoreColor(score);
            color.a = OverlayAlpha;
            material = MaterialPool.MatFrom(BaseContent.WhiteTex, ShaderDatabase.WorldOverlayTransparent, color, RenderQueue);
            MaterialsByBand[band] = material;
            return material;
        }

        private static Color GetScoreColor(float score)
        {
            if (score <= 0.35f)
                return Color.Lerp(new Color(0.45f, 0f, 0f), Color.red, Mathf.InverseLerp(0f, 0.35f, score));
            if (score <= 0.70f)
                return Color.Lerp(new Color(0.95f, 0.55f, 0f), Color.yellow, Mathf.InverseLerp(0.36f, 0.70f, score));
            if (score <= 1f)
                return Color.Lerp(new Color(0.75f, 1f, 0.25f), new Color(0f, 0.5f, 0f), Mathf.InverseLerp(0.70f, 1f, score));
            return Color.Lerp(new Color(0.52f, 0.32f, 0.72f), new Color(0.32f, 0.04f, 0.42f), Mathf.InverseLerp(1f, WorldTileProductivity.ProductivityScoreCap, score));
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
