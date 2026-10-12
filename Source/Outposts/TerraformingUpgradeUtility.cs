using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>One-off world pollution scrub when terraforming (or similar) upgrades are applied.</summary>
    public static class TerraformingUpgradeUtility
    {
        public const int BuildPollutionClearRadius = 2;

        private static readonly Dictionary<int, int> DistancesByTileId = new Dictionary<int, int>();
        private static readonly List<PlanetTile> NeighborBuffer = new List<PlanetTile>(8);

        /// <summary>
        /// Sets pollution to 0 on <paramref name="centerTile"/> and every tile within hop distance
        /// <paramref name="radius"/>. Biotech only; no-op otherwise. Not a daily pulse.
        /// </summary>
        public static void ClearPollutionInRadius(int centerTile, int radius)
        {
            if (!ModsConfig.BiotechActive) return;
            var grid = Find.WorldGrid;
            if (grid == null || centerTile < 0 || centerTile >= grid.TilesCount) return;

            int r = Mathf.Max(0, radius);
            PlanetTile root = new PlanetTile(centerTile, grid[centerTile].Layer);
            if (!root.Valid) return;

            foreach (PlanetTile tile in EnumerateTilesInRadius(grid, root, r))
            {
                int id = tile.tileId;
                if (id < 0 || id >= grid.TilesCount) continue;
                Tile t = grid[id];
                if (t.pollution <= 0f) continue;
                t.pollution = 0f;
                Find.World?.renderer?.Notify_TilePollutionChanged(new PlanetTile(id));
            }

            WD_WorldLayer_PollutionOverlay.InvalidateAndDirtyIfActive();
        }

        private static IEnumerable<PlanetTile> EnumerateTilesInRadius(WorldGrid grid, PlanetTile root, int radius)
        {
            DistancesByTileId.Clear();
            var queue = new Queue<PlanetTile>();
            queue.Enqueue(root);
            DistancesByTileId[root.tileId] = 0;
            yield return root;

            while (queue.Count > 0)
            {
                PlanetTile tile = queue.Dequeue();
                int distance = DistancesByTileId[tile.tileId];
                if (distance >= radius) continue;

                NeighborBuffer.Clear();
                grid.GetTileNeighbors(tile, NeighborBuffer);
                for (int i = 0; i < NeighborBuffer.Count; i++)
                {
                    PlanetTile neighbor = NeighborBuffer[i];
                    if (!neighbor.Valid || DistancesByTileId.ContainsKey(neighbor.tileId)) continue;
                    DistancesByTileId[neighbor.tileId] = distance + 1;
                    queue.Enqueue(neighbor);
                    yield return neighbor;
                }
            }
        }
    }
}
