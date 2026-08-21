using System.Collections;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Cursor-centered world overlay of inspect-style movement difficulty (terrain × road mult,
    /// including road-block penalty). Mutual exclusive with productivity overlay modes.
    /// </summary>
    public class WD_WorldLayer_MovementDifficultyOverlay : WorldDrawLayer
    {
        public const int OverlayRadius = 20;
        private const float SurfaceOffset = 0.012f;
        private const float OverlayAlpha = 0.45f;
        private const int RenderQueue = 3580;
        /// <summary>~90 seconds at 1x (60 ticks/sec).</summary>
        private const int CacheTtlTicks = 5400;
        private const int ShadesPerBand = 8;
        private const int BandImpassable = 0;
        private const int BandPurple = 1;
        private const int BandGreen = 2;
        private const int BandYellow = 3;
        private const int BandRed = 4;
        private const int BandDarkRed = 5;

        private static readonly Dictionary<int, Material> MaterialsByKey = new Dictionary<int, Material>();
        private static float[] cachedScores;
        private static int[] cachedScoreTicks;
        private static int[] cachedBandKeys;
        private static PlanetTile centerTile = PlanetTile.Invalid;

        private readonly List<Vector3> tileVerts = new List<Vector3>(8);
        private readonly List<PlanetTile> neighborTiles = new List<PlanetTile>(8);
        private readonly Queue<PlanetTile> openTiles = new Queue<PlanetTile>();
        private readonly Dictionary<int, int> distancesByTileId = new Dictionary<int, int>();

        public override bool Visible =>
            WorldComponent_WDVisualizerToggle.ProductivityOverlayMode == WD_ProductivityOverlayMode.MovementDifficulty;

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
            cachedScores = null;
            cachedScoreTicks = null;
            cachedBandKeys = null;
        }

        /// <summary>Invalidate cache and mark the layer dirty if the movement overlay is active.</summary>
        public static void InvalidateAndDirtyIfActive()
        {
            InvalidateCache();
            if (WorldComponent_WDVisualizerToggle.ProductivityOverlayMode != WD_ProductivityOverlayMode.MovementDifficulty)
                return;
            SurfaceLayer surface = Find.WorldGrid?.Surface;
            if (surface == null) return;
            Find.World?.renderer?.SetDirty<WD_WorldLayer_MovementDifficultyOverlay>(surface);
        }

        public override IEnumerable Regenerate()
        {
            foreach (object step in base.Regenerate())
                yield return step;

            if (WorldComponent_WDVisualizerToggle.ProductivityOverlayMode != WD_ProductivityOverlayMode.MovementDifficulty)
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

                int bandKey = GetCachedBandKey(tile);
                Material material = GetMaterial(bandKey);
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

        /// <summary>
        /// Inspect-style difficulty for hover. Returns false when impassable (caller shows Impassable).
        /// </summary>
        public static bool TryGetDisplayDifficulty(PlanetTile tile, out float difficulty)
        {
            difficulty = 0f;
            if (!tile.Valid) return false;
            if (IsImpassableForOverlay(tile))
                return false;
            difficulty = GetCachedScore(tile);
            return true;
        }

        /// <summary>
        /// True when pathing or terrain marks the tile impassable. Includes Hilliness.Impassable /
        /// biome.impassable even when <see cref="World.Impassable"/> is false but inspect still shows Impassable.
        /// </summary>
        private static bool IsImpassableForOverlay(PlanetTile tile)
        {
            if (!tile.Valid) return true;
            World world = Find.World;
            if (world == null) return true;
            if (world.Impassable(tile)) return true;

            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return true;
            Tile tileInfo = grid[tile];
            if (tileInfo == null) return true;
            if (tileInfo.hilliness == Hilliness.Impassable) return true;
            if (tileInfo.PrimaryBiome != null && tileInfo.PrimaryBiome.impassable) return true;
            return false;
        }

        public static float GetCachedScore(PlanetTile tile)
        {
            EnsureCaches();
            int tileId = tile.tileId;
            if (tileId < 0 || tileId >= cachedScores.Length) return 0f;

            int now = Find.TickManager?.TicksGame ?? 0;
            if (!float.IsNaN(cachedScores[tileId]) && now - cachedScoreTicks[tileId] < CacheTtlTicks)
                return cachedScores[tileId];

            float score = ComputeDifficulty(tile);
            cachedScores[tileId] = score;
            cachedScoreTicks[tileId] = now;
            cachedBandKeys[tileId] = -1;
            return score;
        }

        private static int GetCachedBandKey(PlanetTile tile)
        {
            EnsureCaches();
            int tileId = tile.tileId;
            if (tileId < 0 || tileId >= cachedBandKeys.Length) return BandImpassable;

            int now = Find.TickManager?.TicksGame ?? 0;
            if (cachedBandKeys[tileId] >= 0
                && !float.IsNaN(cachedScores[tileId])
                && now - cachedScoreTicks[tileId] < CacheTtlTicks)
            {
                return cachedBandKeys[tileId];
            }

            int key;
            if (IsImpassableForOverlay(tile))
            {
                cachedScores[tileId] = float.PositiveInfinity;
                cachedScoreTicks[tileId] = now;
                key = MakeBandKey(BandImpassable, 0);
            }
            else
            {
                float score = GetCachedScore(tile);
                key = DifficultyToBandKey(score);
            }

            cachedBandKeys[tileId] = key;
            return key;
        }

        private static float ComputeDifficulty(PlanetTile tile)
        {
            if (IsImpassableForOverlay(tile))
                return float.PositiveInfinity;

            float terrain = WorldPathGrid.CalculatedMovementDifficultyAt(tile, false);
            if (terrain < 0.01f) terrain = 0.01f;
            // Invalid toTile: same path as world tile inspect; road-block patch applies on fromTile.
            float roadMult = Find.WorldGrid.GetRoadMovementDifficultyMultiplier(tile, PlanetTile.Invalid);
            return terrain * roadMult;
        }

        private static int DifficultyToBandKey(float difficulty)
        {
            int band;
            float lo;
            float hi;
            if (difficulty < 0.6f)
            {
                band = BandPurple;
                lo = 0f;
                hi = 0.59f;
            }
            else if (difficulty < 1.5f)
            {
                band = BandGreen;
                lo = 0.6f;
                hi = 1.49f;
            }
            else if (difficulty < 2.5f)
            {
                band = BandYellow;
                lo = 1.5f;
                hi = 2.49f;
            }
            else if (difficulty < 3.5f)
            {
                band = BandRed;
                lo = 2.5f;
                hi = 3.49f;
            }
            else
            {
                band = BandDarkRed;
                lo = 3.5f;
                hi = 6f;
            }

            float t = hi > lo ? Mathf.Clamp01(Mathf.InverseLerp(lo, hi, difficulty)) : 0f;
            int shade = Mathf.Clamp(Mathf.RoundToInt(t * (ShadesPerBand - 1)), 0, ShadesPerBand - 1);
            return MakeBandKey(band, shade);
        }

        private static int MakeBandKey(int band, int shade) => (band * ShadesPerBand) + shade;

        private static void EnsureCaches()
        {
            int tilesCount = Find.WorldGrid?.TilesCount ?? 0;
            if (cachedScores != null && cachedScores.Length == tilesCount) return;

            cachedScores = new float[tilesCount];
            cachedScoreTicks = new int[tilesCount];
            cachedBandKeys = new int[tilesCount];
            for (int i = 0; i < tilesCount; i++)
            {
                cachedScores[i] = float.NaN;
                cachedScoreTicks[i] = int.MinValue;
                cachedBandKeys[i] = -1;
            }
        }

        private static Material GetMaterial(int bandKey)
        {
            if (MaterialsByKey.TryGetValue(bandKey, out Material material) && material != null)
                return material;

            Color color = GetBandKeyColor(bandKey);
            color.a = OverlayAlpha;
            material = MaterialPool.MatFrom(BaseContent.WhiteTex, ShaderDatabase.WorldOverlayTransparent, color, RenderQueue);
            MaterialsByKey[bandKey] = material;
            return material;
        }

        private static Color GetBandKeyColor(int bandKey)
        {
            int band = bandKey / ShadesPerBand;
            int shade = bandKey % ShadesPerBand;
            float t = ShadesPerBand > 1 ? shade / (float)(ShadesPerBand - 1) : 0f;

            switch (band)
            {
                case BandImpassable:
                    return Color.black;
                case BandPurple:
                    return Color.Lerp(new Color(0.72f, 0.45f, 0.95f), new Color(0.28f, 0.05f, 0.42f), t);
                case BandGreen:
                    return Color.Lerp(new Color(0.55f, 0.95f, 0.45f), new Color(0.05f, 0.45f, 0.12f), t);
                case BandYellow:
                    return Color.Lerp(new Color(1f, 0.95f, 0.35f), new Color(0.85f, 0.65f, 0.05f), t);
                case BandRed:
                    return Color.Lerp(new Color(1f, 0.45f, 0.35f), new Color(0.75f, 0.12f, 0.08f), t);
                default:
                    return Color.Lerp(new Color(0.55f, 0.08f, 0.08f), new Color(0.22f, 0.02f, 0.02f), t);
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
