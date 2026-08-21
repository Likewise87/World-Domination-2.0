using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Road connectivity and eligibility for the colony–settlement road-link quest.
    /// </summary>
    public static class WdRoadQuestUtility
    {
        public const int MaxOfferDistanceTiles = 30;

        private static readonly Queue<int> roadBfsQueue = new Queue<int>(256);
        private static readonly HashSet<int> roadBfsVisited = new HashSet<int>();
        private static readonly List<PlanetTile> roadBfsNeighbors = new List<PlanetTile>(8);

        public static bool AreRoadConnected(PlanetTile a, PlanetTile b)
        {
            if (!a.Valid || !b.Valid)
                return false;
            if (a.tileId == b.tileId)
                return true;

            WorldGrid grid = Find.WorldGrid;
            if (grid == null)
                return false;

            PlanetLayer layer = PlanetSurfaceWorldActions.WdSurfaceLayer ?? grid.Surface;
            if (layer == null)
                return false;
            if (a.Layer != layer || b.Layer != layer)
                return false;

            roadBfsQueue.Clear();
            roadBfsVisited.Clear();
            roadBfsQueue.Enqueue(a.tileId);
            roadBfsVisited.Add(a.tileId);

            while (roadBfsQueue.Count > 0)
            {
                int cur = roadBfsQueue.Dequeue();
                if (cur == b.tileId)
                    return true;

                roadBfsNeighbors.Clear();
                grid.GetTileNeighbors(cur, roadBfsNeighbors);
                for (int i = 0; i < roadBfsNeighbors.Count; i++)
                {
                    PlanetTile npt = roadBfsNeighbors[i];
                    if (!npt.Valid || npt.Layer != layer)
                        continue;
                    int next = npt.tileId;
                    if (!roadBfsVisited.Add(next))
                        continue;
                    if (grid.GetRoadDef(cur, next, visibleOnly: false) == null)
                        continue;
                    roadBfsQueue.Enqueue(next);
                }
            }

            return false;
        }

        public static bool AreRoadConnected(Settlement? a, Settlement? b)
        {
            if (a == null || b == null || a.Destroyed || b.Destroyed)
                return false;
            return AreRoadConnected(a.Tile, b.Tile);
        }

        /// <summary>
        /// Surface FindPath with hop cap: max(approx*2, approx+8).
        /// </summary>
        public static bool TryGetSaneLandPath(PlanetTile from, PlanetTile to, out int hopCount, out float approxDistance)
        {
            hopCount = 0;
            approxDistance = 0f;

            if (!from.Valid || !to.Valid)
                return false;

            WorldGrid grid = Find.WorldGrid;
            if (grid == null)
                return false;
            PlanetLayer layer = PlanetSurfaceWorldActions.WdSurfaceLayer ?? grid.Surface;
            if (layer == null)
                return false;
            if (from.Layer != layer || to.Layer != layer)
                return false;

            approxDistance = grid.ApproxDistanceInTiles(from, to);
            if (from.tileId == to.tileId)
                return true;

            int maxHops = Mathf.Max(
                Mathf.CeilToInt(approxDistance * 2f),
                Mathf.CeilToInt(approxDistance) + 8);

            using (WorldPath path = layer.Pather.FindPath(from, to, null))
            {
                if (path == null || !path.Found)
                    return false;

                hopCount = path.NodesReversed.Count - 1;
                if (hopCount < 0)
                    hopCount = 0;
                return hopCount <= maxHops;
            }
        }

        public static bool TryPickQuestSettlement(out Settlement settlement)
        {
            settlement = null!;

            Settlement? colony = InfluenceUtils.GetPlayerColony();
            if (colony == null || colony.Destroyed)
                return false;

            Faction? player = Faction.OfPlayerSilentFail;
            if (player == null)
                return false;

            PlanetTile colonyTile = colony.Tile;
            if (!colonyTile.Valid || !WorldActions_Utils.IsWdSurfaceTile(colonyTile))
                return false;

            Settlement best = null;
            int bestHops = int.MaxValue;
            float bestApprox = float.MaxValue;

            List<Settlement> settlements = Find.WorldObjects.Settlements;
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement s = settlements[i];
                if (s == null || s.Destroyed || s.Faction == null || s.Faction.IsPlayer)
                    continue;
                if (!WorldActions_Utils.IsWdSurfaceWorldObject(s))
                    continue;
                if (s.GetComponent<CompViralSpread>() == null)
                    continue;
                if (WorldActions_Utils.IsExcludedFaction(s.Faction))
                    continue;

                FactionRelationKind kind = WorldActions_Utils.SafeRelationKindWith(s.Faction, player);
                if (kind != FactionRelationKind.Ally && kind != FactionRelationKind.Neutral)
                    continue;

                float approx = Find.WorldGrid.ApproxDistanceInTiles(colonyTile, s.Tile);
                if (approx > MaxOfferDistanceTiles)
                    continue;

                if (AreRoadConnected(colony, s))
                    continue;

                if (!TryGetSaneLandPath(colonyTile, s.Tile, out int hopCount, out _))
                    continue;

                if (hopCount > bestHops)
                    continue;
                if (hopCount == bestHops && approx >= bestApprox)
                    continue;

                best = s;
                bestHops = hopCount;
                bestApprox = approx;
            }

            if (best == null)
                return false;

            settlement = best;
            return true;
        }
    }
}
