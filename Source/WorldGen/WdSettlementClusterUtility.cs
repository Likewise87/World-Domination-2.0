using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Public cluster / nearby settlement-tile helpers (BFS), shared by forward assault and world setup.</summary>
    public static class WdSettlementClusterUtility
    {
        private const int SoftRadiusTiles = 24;
        private const int MaxAttempts = 400;
        /// <summary>Vanguard pack-up: mutual spacing and relaxed blocker pad (adjacent tiles allowed).</summary>
        public const int VanguardMinDistance = 1;

        /// <summary>
        /// Reserve <paramref name="count"/> Vanguard tiles near <paramref name="seedTile"/>.
        /// Pass 1: 1-tile spacing between reserved tiles; normal establishment min-distance vs existing settlements/outposts/colonies.
        /// Pass 2: same peer spacing; blockers also only require 1-tile distance.
        /// </summary>
        public static bool TryReserveClusterNear(int seedTile, int count, Faction faction, out List<int> tiles)
        {
            tiles = new List<int>();
            if (seedTile < 0 || count <= 0 || Find.WorldGrid == null)
            {
                WDVerbose.Msg($"VanguardCluster abort seed={seedTile} count={count} (bad args / no grid)");
                return false;
            }

            int normalBlocker = Mathf.Max(1, Outpost_EstablishmentRequirements.MinDistanceTiles);
            WDVerbose.Msg(
                $"VanguardCluster start seed={seedTile} need={count} faction={faction?.Name ?? "?"} peerDist={VanguardMinDistance} pass1BlockerDist={normalBlocker}");

            if (TryReserveClusterPass(seedTile, count, faction, VanguardMinDistance, normalBlocker, passLabel: 1, out tiles))
            {
                WDVerbose.Msg($"VanguardCluster pass=1 SUCCESS tiles=[{string.Join(",", tiles)}]");
                return true;
            }

            if (normalBlocker <= VanguardMinDistance)
            {
                WDVerbose.Msg($"VanguardCluster FAIL seed={seedTile} need={count} pass1 already at min blocker={normalBlocker}");
                tiles = new List<int>();
                return false;
            }

            WDVerbose.Msg(
                $"VanguardCluster pass=1 FAIL need={count} — relaxing blockerDist {normalBlocker}→{VanguardMinDistance}");

            if (TryReserveClusterPass(seedTile, count, faction, VanguardMinDistance, VanguardMinDistance, passLabel: 2, out tiles))
            {
                WDVerbose.Msg($"VanguardCluster pass=2 SUCCESS tiles=[{string.Join(",", tiles)}]");
                return true;
            }

            WDVerbose.Msg($"VanguardCluster FAIL seed={seedTile} need={count} both passes exhausted");
            tiles = new List<int>();
            return false;
        }

        private static bool TryReserveClusterPass(
            int seedTile,
            int count,
            Faction faction,
            int peerDist,
            int blockerDist,
            int passLabel,
            out List<int> tiles)
        {
            tiles = new List<int>();
            var reserved = new List<int>();

            for (int n = 0; n < count; n++)
            {
                int found = FindNearbyValidTile(
                    seedTile,
                    reserved,
                    peerDist,
                    blockerDist,
                    faction,
                    logContext: $"pass={passLabel} slot={n + 1}/{count}");
                if (found < 0)
                {
                    WDVerbose.Msg(
                        $"VanguardCluster pass={passLabel} slot={n + 1}/{count} FAIL reservedSoFar=[{string.Join(",", reserved)}] peer={peerDist} blocker={blockerDist}");
                    return false;
                }

                reserved.Add(found);
                WDVerbose.Msg(
                    $"VanguardCluster pass={passLabel} slot={n + 1}/{count} ACCEPT tile={found} peer={peerDist} blocker={blockerDist}");
            }

            tiles = reserved;
            return tiles.Count >= count;
        }

        /// <summary>
        /// Nearest free settlement tile for Vanguard refound/redirect: try normal blocker pad, then 1-tile pad.
        /// Peer spacing unused (single tile).
        /// </summary>
        public static int FindNearestValidSettlementTile(int seedTile, Faction faction)
        {
            if (seedTile < 0) return -1;

            int normalBlocker = Mathf.Max(1, Outpost_EstablishmentRequirements.MinDistanceTiles);
            WDVerbose.Msg(
                $"VanguardNearest start seed={seedTile} faction={faction?.Name ?? "?"} tryBlocker={normalBlocker} then={VanguardMinDistance}");

            int tile = FindNearbyValidTile(
                seedTile,
                alreadyReserved: null,
                peerDist: VanguardMinDistance,
                blockerDist: normalBlocker,
                faction,
                logContext: "nearest/pass1");
            if (tile >= 0)
            {
                WDVerbose.Msg($"VanguardNearest SUCCESS pass=1 tile={tile} blocker={normalBlocker}");
                return tile;
            }

            if (normalBlocker <= VanguardMinDistance)
            {
                WDVerbose.Msg($"VanguardNearest FAIL seed={seedTile} (already min blocker)");
                return -1;
            }

            WDVerbose.Msg($"VanguardNearest pass=1 miss — relaxing blocker→{VanguardMinDistance}");
            tile = FindNearbyValidTile(
                seedTile,
                alreadyReserved: null,
                peerDist: VanguardMinDistance,
                blockerDist: VanguardMinDistance,
                faction,
                logContext: "nearest/pass2");
            if (tile >= 0)
                WDVerbose.Msg($"VanguardNearest SUCCESS pass=2 tile={tile} blocker={VanguardMinDistance}");
            else
                WDVerbose.Msg($"VanguardNearest FAIL seed={seedTile}");

            return tile;
        }

        /// <summary>True if a Vanguard settlement may found here under the relaxed (1-tile) blocker pad.</summary>
        public static bool CanFoundVanguardAt(int tile, Faction faction = null)
        {
            return IsValidSettlementTile(tile, alreadyReserved: null, peerDist: VanguardMinDistance, blockerDist: VanguardMinDistance, faction, rejectReason: out _);
        }

        private static int FindNearbyValidTile(
            int seedTile,
            List<int> alreadyReserved,
            int peerDist,
            int blockerDist,
            Faction faction,
            string logContext)
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || !grid.InBounds(seedTile)) return -1;

            var visited = new HashSet<int>();
            var queue = new Queue<(int tile, int dist)>();
            queue.Enqueue((seedTile, 0));
            visited.Add(seedTile);

            var neighborScratch = new List<PlanetTile>();
            int attempts = 0;
            int rejectInvalid = 0;
            int rejectOccupied = 0;
            int rejectBlocker = 0;
            int rejectPeer = 0;
            int rejectCamp = 0;
            int rejectSaturated = 0;

            while (queue.Count > 0 && attempts < MaxAttempts)
            {
                (int tile, int dist) = queue.Dequeue();
                attempts++;

                if (dist > 0)
                {
                    if (IsValidSettlementTile(tile, alreadyReserved, peerDist, blockerDist, faction, out string reject))
                    {
                        WDVerbose.Msg(
                            $"VanguardCluster BFS {logContext} hit tile={tile} bfsDist={dist} attempts={attempts} rejects(invalid={rejectInvalid} occ={rejectOccupied} blocker={rejectBlocker} peer={rejectPeer} camp={rejectCamp} sat={rejectSaturated})");
                        return tile;
                    }

                    switch (reject)
                    {
                        case "invalid": rejectInvalid++; break;
                        case "occupied": rejectOccupied++; break;
                        case "blocker": rejectBlocker++; break;
                        case "peer": rejectPeer++; break;
                        case "camp": rejectCamp++; break;
                        case "saturated": rejectSaturated++; break;
                    }
                }

                if (dist >= SoftRadiusTiles) continue;

                neighborScratch.Clear();
                grid.GetTileNeighbors(tile, neighborScratch);
                for (int i = 0; i < neighborScratch.Count; i++)
                {
                    int n = neighborScratch[i].tileId;
                    if (!visited.Add(n)) continue;
                    if (!PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(n)) continue;
                    queue.Enqueue((n, dist + 1));
                }
            }

            WDVerbose.Msg(
                $"VanguardCluster BFS {logContext} exhausted attempts={attempts} visited={visited.Count} rejects(invalid={rejectInvalid} occ={rejectOccupied} blocker={rejectBlocker} peer={rejectPeer} camp={rejectCamp} sat={rejectSaturated}) peer={peerDist} blocker={blockerDist}");
            return -1;
        }

        private static bool IsValidSettlementTile(
            int tile,
            List<int> alreadyReserved,
            int peerDist,
            int blockerDist,
            Faction faction,
            out string rejectReason)
        {
            rejectReason = null;
            if (!TileFinder.IsValidTileForNewSettlement(tile))
            {
                rejectReason = "invalid";
                return false;
            }
            if (Find.WorldObjects.AnyWorldObjectAt(tile))
            {
                rejectReason = "occupied";
                return false;
            }
            if (IsBlockedByEstablishmentDistance(tile, blockerDist))
            {
                rejectReason = "blocker";
                return false;
            }
            if (Outpost_EstablishmentRequirements.TileHasActiveCamp(tile))
            {
                rejectReason = "camp";
                return false;
            }

            WorldGrid grid = Find.WorldGrid;
            if (alreadyReserved != null && grid != null && peerDist > 0)
            {
                for (int i = 0; i < alreadyReserved.Count; i++)
                {
                    if (grid.ApproxDistanceInTiles(tile, alreadyReserved[i]) < peerDist)
                    {
                        rejectReason = "peer";
                        return false;
                    }
                }
            }

            var seth = WorldDominationMod.settings;
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (seth != null && faction != null
                && WorldActions_GrowthExpand.IsTargetSaturated(tile, faction, seth, manager))
            {
                rejectReason = "saturated";
                return false;
            }

            return true;
        }

        /// <summary>
        /// True when any settlement / WD outpost / colony / WD ruin is closer than <paramref name="minDist"/> tiles.
        /// <paramref name="minDist"/> 1 ⇒ only same-tile (already covered by occupied); adjacent OK.
        /// </summary>
        public static bool IsBlockedByEstablishmentDistance(int tile, int minDist)
        {
            if (minDist <= 0) return false;
            WorldGrid grid = Find.WorldGrid;
            var all = Find.WorldObjects?.AllWorldObjects;
            if (grid == null || all == null || tile < 0) return false;

            // Fast path: normal setting matches the prewarmed establishment cache.
            if (minDist == Outpost_EstablishmentRequirements.MinDistanceTiles)
                return Outpost_EstablishmentRequirements.IsTileBlockedByMinDistanceCached(tile);

            for (int i = 0; i < all.Count; i++)
            {
                WorldObject o = all[i];
                if (!Outpost_EstablishmentRequirements.IsEstablishmentMinDistanceBlocker(o)) continue;
                if (grid.ApproxDistanceInTiles(tile, o.Tile.tileId) < minDist)
                    return true;
            }

            return false;
        }
    }
}
