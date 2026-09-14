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
        /// <summary>One free world tile between Vanguard dig-ins (ApproxDistance &gt;= 2).</summary>
        public const int VanguardPeerDistance = 2;
        /// <summary>Relaxed pad vs existing settlements/outposts for Vanguard (ignore normal outpost min spacing).</summary>
        public const int VanguardBlockerDistance = 1;
        /// <summary>Legacy alias used by older call sites; equals <see cref="VanguardBlockerDistance"/>.</summary>
        public const int VanguardMinDistance = VanguardBlockerDistance;

        /// <summary>
        /// Reserve <paramref name="count"/> Vanguard tiles near <paramref name="seedTile"/>.
        /// Peer spacing = 2 (one free tile between). Blockers use 1-tile pad (no outpost MinDistanceTiles).
        /// Optional colony keep-out rejects tiles closer than <paramref name="minDistFromColony"/> to <paramref name="colonyTile"/>.
        /// </summary>
        public static bool TryReserveClusterNear(
            int seedTile,
            int count,
            Faction faction,
            out List<int> tiles,
            int colonyTile = -1,
            int minDistFromColony = 0)
        {
            tiles = new List<int>();
            if (seedTile < 0 || count <= 0 || Find.WorldGrid == null)
            {
                WDVerbose.Msg($"VanguardCluster abort seed={seedTile} count={count} (bad args / no grid)");
                return false;
            }

            WDVerbose.Msg(
                $"VanguardCluster start seed={seedTile} need={count} faction={faction?.Name ?? "?"} peerDist={VanguardPeerDistance} blockerDist={VanguardBlockerDistance} colonyKeepOut={(minDistFromColony > 0 ? $"{minDistFromColony} from {colonyTile}" : "off")}");

            if (TryReserveClusterPass(
                    seedTile, count, faction, VanguardPeerDistance, VanguardBlockerDistance,
                    colonyTile, minDistFromColony, passLabel: 1, out tiles))
            {
                WDVerbose.Msg($"VanguardCluster SUCCESS tiles=[{string.Join(",", tiles)}]");
                return true;
            }

            WDVerbose.Msg($"VanguardCluster FAIL seed={seedTile} need={count}");
            tiles = new List<int>();
            return false;
        }

        private static bool TryReserveClusterPass(
            int seedTile,
            int count,
            Faction faction,
            int peerDist,
            int blockerDist,
            int colonyTile,
            int minDistFromColony,
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
                    colonyTile,
                    minDistFromColony,
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
        /// Nearest free settlement tile for Vanguard refound/redirect using the tight Vanguard pad (no outpost MinDistanceTiles).
        /// </summary>
        public static int FindNearestValidSettlementTile(int seedTile, Faction faction)
        {
            if (seedTile < 0) return -1;

            WDVerbose.Msg(
                $"VanguardNearest start seed={seedTile} faction={faction?.Name ?? "?"} blocker={VanguardBlockerDistance}");

            int tile = FindNearbyValidTile(
                seedTile,
                alreadyReserved: null,
                peerDist: VanguardPeerDistance,
                blockerDist: VanguardBlockerDistance,
                faction,
                colonyTile: -1,
                minDistFromColony: 0,
                logContext: "nearest");
            if (tile >= 0)
                WDVerbose.Msg($"VanguardNearest SUCCESS tile={tile}");
            else
                WDVerbose.Msg($"VanguardNearest FAIL seed={seedTile}");

            return tile;
        }

        /// <summary>True if a Vanguard settlement may found here under the relaxed blocker pad.</summary>
        public static bool CanFoundVanguardAt(int tile, Faction faction = null)
        {
            return IsValidSettlementTile(
                tile, alreadyReserved: null, peerDist: VanguardPeerDistance, blockerDist: VanguardBlockerDistance,
                faction, colonyTile: -1, minDistFromColony: 0, rejectReason: out _);
        }

        private static int FindNearbyValidTile(
            int seedTile,
            List<int> alreadyReserved,
            int peerDist,
            int blockerDist,
            Faction faction,
            int colonyTile,
            int minDistFromColony,
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
            int rejectColony = 0;

            while (queue.Count > 0 && attempts < MaxAttempts)
            {
                (int tile, int dist) = queue.Dequeue();
                attempts++;

                if (IsValidSettlementTile(
                        tile, alreadyReserved, peerDist, blockerDist, faction,
                        colonyTile, minDistFromColony, out string reject))
                {
                    WDVerbose.Msg(
                        $"VanguardCluster BFS {logContext} hit tile={tile} bfsDist={dist} attempts={attempts} rejects(invalid={rejectInvalid} occ={rejectOccupied} blocker={rejectBlocker} peer={rejectPeer} camp={rejectCamp} sat={rejectSaturated} colony={rejectColony})");
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
                    case "colony": rejectColony++; break;
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
                $"VanguardCluster BFS {logContext} exhausted attempts={attempts} visited={visited.Count} rejects(invalid={rejectInvalid} occ={rejectOccupied} blocker={rejectBlocker} peer={rejectPeer} camp={rejectCamp} sat={rejectSaturated} colony={rejectColony}) peer={peerDist} blocker={blockerDist}");
            return -1;
        }

        private static bool IsValidSettlementTile(
            int tile,
            List<int> alreadyReserved,
            int peerDist,
            int blockerDist,
            Faction faction,
            int colonyTile,
            int minDistFromColony,
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
            if (minDistFromColony > 0 && colonyTile >= 0 && grid != null
                && grid.ApproxDistanceInTiles(tile, colonyTile) < minDistFromColony)
            {
                rejectReason = "colony";
                return false;
            }

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
