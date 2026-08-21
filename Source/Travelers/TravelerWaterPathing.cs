using System.Collections.Generic;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Traveler-only pathing that allows water-covered tiles. Runs only when <see cref="WorldDominationSettings.allowCaravansTravelOverWater"/>; combined with vanilla path per <see cref="WorldDominationSettings.onlyTravelAcrossWaterIfNoOtherWay"/>.
    /// Keeps normal land movement behavior while allowing water-covered tiles.
    /// Uses progressive corridor search with weighted A*: tries a narrow band along the
    /// straight line first (very fast), then widens progressively, falling back to a
    /// near-full search only if the shortcut isn't obvious.
    /// Reuses static data structures to eliminate per-call GC pressure.
    /// </summary>
    public static class TravelerWaterPathing
    {
        private static readonly Dictionary<int, float> dist = new Dictionary<int, float>();
        private static readonly Dictionary<int, int> prev = new Dictionary<int, int>();
        private static readonly HashSet<int> closed = new HashSet<int>();
        private static readonly MinHeap heap = new MinHeap();
        private static readonly List<PlanetTile> neighbors = new List<PlanetTile>(8);

        private const float HeuristicMinCostPerTile = 0.5f;

        private static readonly (float corridor, float weight)[] SearchPasses = new[]
        {
            (4f,   2.5f),
            (10f,  2.0f),
            (float.MaxValue, 1.5f)
        };

        public static bool TryBuildFallbackPath(PlanetTile start, PlanetTile dest, out List<PlanetTile> fullPath)
        {
            fullPath = null;
            if (!start.Valid || !dest.Valid) return false;
            if (start == dest)
            {
                fullPath = new List<PlanetTile> { start };
                return true;
            }

            PlanetLayer layer = start.Layer;
            if (dest.Layer != layer) return false;

            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return false;

            int startId = start.tileId;
            int destId = dest.tileId;
            int tileCount = grid.TilesCount;
            if (startId < 0 || destId < 0 || startId >= tileCount || destId >= tileCount) return false;

            float directDist = grid.ApproxDistanceInTiles(startId, destId);

            for (int p = 0; p < SearchPasses.Length; p++)
            {
                if (TrySearchPass(grid, layer, startId, destId, tileCount, directDist,
                        SearchPasses[p].corridor, SearchPasses[p].weight, out fullPath))
                    return true;
            }

            return false;
        }

        private static bool TrySearchPass(WorldGrid grid, PlanetLayer layer,
            int startId, int destId, int tileCount, float directDist,
            float corridorWidth, float heuristicWeight, out List<PlanetTile> path)
        {
            path = null;

            dist.Clear();
            prev.Clear();
            closed.Clear();
            heap.Clear();

            bool useCorridor = corridorWidth < float.MaxValue;

            dist[startId] = 0f;
            heap.Push(startId, heuristicWeight * Heuristic(grid, startId, destId));

            while (heap.Count > 0)
            {
                var (current, _) = heap.Pop();

                if (current == destId)
                {
                    path = ReconstructPath(startId, destId, layer);
                    return path != null && path.Count >= 2;
                }

                if (!closed.Add(current)) continue;

                float currentDist = dist[current];

                neighbors.Clear();
                grid.GetTileNeighbors(current, neighbors);

                for (int i = 0; i < neighbors.Count; i++)
                {
                    PlanetTile nextTile = neighbors[i];
                    int next = nextTile.tileId;
                    if (next < 0 || next >= tileCount) continue;
                    if (closed.Contains(next)) continue;
                    if (nextTile.Layer != layer) continue;
                    if (!IsAllowedFallbackTile(next, destId, layer)) continue;

                    if (useCorridor)
                    {
                        float excess = grid.ApproxDistanceInTiles(next, startId)
                                     + grid.ApproxDistanceInTiles(next, destId)
                                     - directDist;
                        if (excess > corridorWidth) continue;
                    }

                    var from = new PlanetTile(current, layer);
                    var to = new PlanetTile(next, layer);
                    float hopCost = TravelUtils.GetTravelerHopDifficultyUnits(from, to);
                    float cand = currentDist + hopCost;

                    if (!dist.TryGetValue(next, out float old) || cand < old)
                    {
                        dist[next] = cand;
                        prev[next] = current;
                        heap.Push(next, cand + heuristicWeight * Heuristic(grid, next, destId));
                    }
                }
            }

            return false;
        }

        private static float Heuristic(WorldGrid grid, int from, int to)
        {
            return grid.ApproxDistanceInTiles(from, to) * HeuristicMinCostPerTile;
        }

        private static bool IsAllowedFallbackTile(int tileId, int destId, PlanetLayer layer)
        {
            if (tileId == destId) return true;
            PlanetTile pt = new PlanetTile(tileId, layer);
            if (!PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(pt)) return false;

            WorldGrid grid = Find.WorldGrid;
            bool isWater = grid[pt].WaterCovered;
            if (isWater) return true;

            float diff = WorldPathGrid.CalculatedMovementDifficultyAt(pt, false);
            return diff < 1000f;
        }

        private static List<PlanetTile> ReconstructPath(int startId, int destId, PlanetLayer layer)
        {
            var reverse = new List<int> { destId };
            int cur = destId;
            while (cur != startId)
            {
                if (!prev.TryGetValue(cur, out int p)) return null;
                cur = p;
                reverse.Add(cur);
            }
            reverse.Reverse();

            var path = new List<PlanetTile>(reverse.Count);
            for (int i = 0; i < reverse.Count; i++)
                path.Add(new PlanetTile(reverse[i], layer));
            return path;
        }

        /// <summary>Binary min-heap keyed by float cost. Allows duplicate tile entries (lazy deletion via closed set).</summary>
        private sealed class MinHeap
        {
            private readonly List<(int tileId, float cost)> data = new List<(int, float)>();

            public int Count => data.Count;

            public void Clear() => data.Clear();

            public void Push(int tileId, float cost)
            {
                data.Add((tileId, cost));
                SiftUp(data.Count - 1);
            }

            public (int tileId, float cost) Pop()
            {
                var top = data[0];
                int last = data.Count - 1;
                data[0] = data[last];
                data.RemoveAt(last);
                if (data.Count > 0)
                    SiftDown(0);
                return top;
            }

            private void SiftUp(int i)
            {
                while (i > 0)
                {
                    int parent = (i - 1) >> 1;
                    if (data[i].cost >= data[parent].cost) break;
                    var tmp = data[i];
                    data[i] = data[parent];
                    data[parent] = tmp;
                    i = parent;
                }
            }

            private void SiftDown(int i)
            {
                int count = data.Count;
                while (true)
                {
                    int left = (i << 1) + 1;
                    if (left >= count) break;
                    int right = left + 1;
                    int smallest = (right < count && data[right].cost < data[left].cost) ? right : left;
                    if (data[i].cost <= data[smallest].cost) break;
                    var tmp = data[i];
                    data[i] = data[smallest];
                    data[smallest] = tmp;
                    i = smallest;
                }
            }
        }
    }
}
