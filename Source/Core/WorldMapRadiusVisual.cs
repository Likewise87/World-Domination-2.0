using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Converts gameplay <see cref="WorldGrid.ApproxDistanceInTiles"/> radii into hop counts for
    /// world radius rings. Caps and multi-caches edge tiles so multiple simultaneous rings
    /// (colony influence, attack range, logistics, …) do not thrash vanilla's single global cache.
    /// </summary>
    public static class WorldMapRadiusVisual
    {
        /// <summary>Visual hop cap — flood-fill cost grows roughly with radius².</summary>
        public const int MaxVisualHopRadius = 48;

        private const int MaxCachedRings = 24;

        private struct RingKey : System.IEquatable<RingKey>
        {
            public int TileId;
            public int LayerId;
            public int Hops;
            public int WorldSeed;

            public bool Equals(RingKey other) =>
                TileId == other.TileId && LayerId == other.LayerId && Hops == other.Hops && WorldSeed == other.WorldSeed;

            public override bool Equals(object obj) => obj is RingKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int h = TileId;
                    h = (h * 397) ^ LayerId;
                    h = (h * 397) ^ Hops;
                    h = (h * 397) ^ WorldSeed;
                    return h;
                }
            }
        }

        private sealed class RingCacheEntry
        {
            public readonly List<PlanetTile> EdgeTiles = new List<PlanetTile>(64);
            public int LastUsedFrame;
        }

        private static readonly Dictionary<RingKey, RingCacheEntry> ringCache = new Dictionary<RingKey, RingCacheEntry>(MaxCachedRings);
        private static readonly HashSet<PlanetTile> floodEdgeScratch = new HashSet<PlanetTile>();

        /// <summary>Hop count passed to the ring drawer (before visual cap).</summary>
        public static int GetHopDrawRadius(float approxLogicRadius)
        {
            if (approxLogicRadius <= 0f)
                return 0;

            float mult = 1.02f + approxLogicRadius * 0.003f;
            return Mathf.CeilToInt(approxLogicRadius * mult);
        }

        public static void DrawApproxRadiusRing(PlanetTile center, float approxLogicRadius, Material mat)
        {
            if (!center.Valid || approxLogicRadius <= 0f || mat == null)
                return;

            int hopRadius = GetHopDrawRadius(approxLogicRadius);
            if (hopRadius <= 0)
                return;

            hopRadius = Mathf.Min(hopRadius, MaxVisualHopRadius);
            DrawHopRadiusRing(center, hopRadius, mat);
        }

        public static void DrawApproxRadiusRing(int centerTile, float approxLogicRadius, Material mat)
        {
            if (centerTile < 0 || approxLogicRadius <= 0f || mat == null)
                return;

            var grid = Find.WorldGrid;
            if (grid == null || !grid.InBounds(centerTile))
                return;

            PlanetLayer layer = grid[centerTile].Layer;
            DrawApproxRadiusRing(new PlanetTile(centerTile, layer), approxLogicRadius, mat);
        }

        private static void DrawHopRadiusRing(PlanetTile center, int radius, Material mat)
        {
            if (!center.Valid || radius < 0 || mat == null || center.Layer == null)
                return;

            int seed = Find.World?.info?.Seed ?? 0;
            var key = new RingKey
            {
                TileId = center.tileId,
                LayerId = center.Layer.GetHashCode(),
                Hops = radius,
                WorldSeed = seed
            };

            int frame = Time.frameCount;
            if (!ringCache.TryGetValue(key, out RingCacheEntry entry))
            {
                EvictIfNeeded();
                entry = new RingCacheEntry();
                if (!TryBuildEdgeTiles(center, radius, entry.EdgeTiles))
                    return;
                ringCache[key] = entry;
            }

            entry.LastUsedFrame = frame;
            if (entry.EdgeTiles.Count < 3)
                return;

            float widthFactor = 5f * (center.LayerDef?.lineWidthFactor ?? 1f);
            GenDraw.DrawWorldLineStrip(entry.EdgeTiles, mat, widthFactor);
        }

        private static bool TryBuildEdgeTiles(PlanetTile center, int radius, List<PlanetTile> into)
        {
            into.Clear();
            floodEdgeScratch.Clear();
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || center.Layer?.Filler == null)
                return false;

            center.Layer.Filler.FloodFill(center, _ => true, (PlanetTile tile, int dist) =>
            {
                if (dist > radius + 1)
                    return true;
                if (dist == radius + 1 || grid.GetTileNeighborCount(tile) < 5)
                    floodEdgeScratch.Add(tile);
                return false;
            });

            if (floodEdgeScratch.Count < 5)
                return false;

            into.AddRange(floodEdgeScratch);
            Vector3 c = Vector3.zero;
            for (int i = 0; i < into.Count; i++)
                c += grid.GetTileCenter(into[i]);
            c /= into.Count;
            Vector3 n = c.normalized;
            Vector3 refDir = Vector3.ProjectOnPlane(grid.GetTileCenter(into[0]) - c, n).normalized;

            // Static comparer avoids allocating a Comparison<> lambda on each cache miss.
            edgeSortGrid = grid;
            edgeSortCenter = c;
            edgeSortNormal = n;
            edgeSortRefDir = refDir;
            into.Sort(EdgeAngleComparison);

            for (int i = 0; i < into.Count; i++)
            {
                PlanetTile tileA = into[i];
                PlanetTile mid = into[(i + 1) % into.Count];
                PlanetTile tileB = into[(i + 2) % into.Count];
                if (!grid.IsNeighbor(tileA, mid) && grid.IsNeighbor(mid, tileB) && grid.IsNeighbor(tileA, tileB))
                {
                    int swapIdx = (i + 1) % into.Count;
                    int otherIdx = (i + 2) % into.Count;
                    PlanetTile tmp = into[swapIdx];
                    into[swapIdx] = into[otherIdx];
                    into[otherIdx] = tmp;
                }
            }

            return true;
        }

        private static WorldGrid edgeSortGrid;
        private static Vector3 edgeSortCenter;
        private static Vector3 edgeSortNormal;
        private static Vector3 edgeSortRefDir;
        private static readonly System.Comparison<PlanetTile> EdgeAngleComparison = CompareEdgeAngle;

        private static int CompareEdgeAngle(PlanetTile a, PlanetTile b)
        {
            WorldGrid grid = edgeSortGrid;
            float angA = SignedAngle01(edgeSortRefDir, Vector3.ProjectOnPlane(grid.GetTileCenter(a) - edgeSortCenter, edgeSortNormal).normalized, edgeSortNormal);
            float angB = SignedAngle01(edgeSortRefDir, Vector3.ProjectOnPlane(grid.GetTileCenter(b) - edgeSortCenter, edgeSortNormal).normalized, edgeSortNormal);
            int cmp = angA.CompareTo(angB);
            if (cmp != 0) return cmp;
            return a.tileId.CompareTo(b.tileId);
        }

        private static float SignedAngle01(Vector3 from, Vector3 to, Vector3 axis)
        {
            float ang = Vector3.SignedAngle(from, to, axis);
            return ang < 0f ? ang + 360f : ang;
        }

        private static void EvictIfNeeded()
        {
            if (ringCache.Count < MaxCachedRings)
                return;

            int oldestFrame = int.MaxValue;
            RingKey oldestKey = default;
            bool found = false;
            foreach (var kv in ringCache)
            {
                if (kv.Value.LastUsedFrame < oldestFrame)
                {
                    oldestFrame = kv.Value.LastUsedFrame;
                    oldestKey = kv.Key;
                    found = true;
                }
            }
            if (found)
                ringCache.Remove(oldestKey);
        }
    }
}
