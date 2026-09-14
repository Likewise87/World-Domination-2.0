using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Shared fortify-kit layout for Turtle / desperation finalize and daily NPC Fortify.
    /// Radius 1: AT turrets (spread). Kit AT counts are soft targets; World Actions
    /// <c>atTurretMaxT*</c> caps how many a settlement may own. Radius 2: traps on roads, blocks on non-roads.
    /// Ensures at least one road exit on the outer ring before placing traps.
    /// </summary>
    public static class WorldActions_FortifyKit
    {
        private static readonly List<PlanetTile> tmpNeighbors = new List<PlanetTile>();
        private static readonly List<int> tmpRingTiles = new List<int>();
        private static readonly List<int> tmpInnerRingTiles = new List<int>();
        private static readonly List<int> tmpAtPick = new List<int>();
        private static readonly List<int> tmpAtEligibleIdx = new List<int>();
        private static readonly List<int> tmpAtPickedIdx = new List<int>();
        private static readonly List<int> tmpPath = new List<int>();
        private static readonly HashSet<int> tmpSeen = new HashSet<int>();

        public enum FortifyPhase : byte
        {
            Traps = 0,
            Blocks = 1,
            AtTurrets = 2,
            Complete = 3
        }

        public static void GetKit(
            SettlementTier tier,
            out SpikeTrapKind trapKind,
            out RoadBlockKind blockKind,
            out AtTurretTier atTier,
            out int maxAt)
        {
            switch (tier)
            {
                case SettlementTier.T4:
                    trapKind = SpikeTrapKind.Caltrops;
                    blockKind = RoadBlockKind.Heavy;
                    atTier = AtTurretTier.Heavy;
                    maxAt = 3;
                    break;
                case SettlementTier.T3:
                    trapKind = SpikeTrapKind.Caltrops;
                    blockKind = RoadBlockKind.Normal;
                    atTier = AtTurretTier.Medium;
                    maxAt = 3;
                    break;
                case SettlementTier.T2:
                    trapKind = SpikeTrapKind.Spike;
                    blockKind = RoadBlockKind.Light;
                    atTier = AtTurretTier.Light;
                    maxAt = 2;
                    break;
                default:
                    // T1: Spike trap, Light block, 1 Light AT (daily Fortify + Turtle / desperation).
                    trapKind = SpikeTrapKind.Spike;
                    blockKind = RoadBlockKind.Light;
                    atTier = AtTurretTier.Light;
                    maxAt = 1;
                    break;
            }
        }

        public static bool IsFortifyTileOk(int tileId)
        {
            if (tileId < 0 || !Find.WorldGrid.InBounds(tileId)) return false;
            if (!PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(tileId)) return false;
            if (Find.World.Impassable(tileId)) return false;
            if (Find.WorldObjects.AnySettlementAt(tileId)) return false;
            if (Outpost_EstablishmentRequirements.TileHasActiveCamp(tileId)) return false;
            return true;
        }

        public static void CollectOrderedNeighbors(int center, List<int> into)
        {
            into.Clear();
            if (center < 0) return;
            tmpNeighbors.Clear();
            Find.WorldGrid.GetTileNeighbors(center, tmpNeighbors);
            for (int i = 0; i < tmpNeighbors.Count; i++)
                into.Add(tmpNeighbors[i].tileId);
        }

        /// <summary>
        /// Pick up to <paramref name="want"/> tiles from a circular neighbor ring, maximizing
        /// minimum ring-distance to already chosen tiles (opposite sites first; leftovers otherwise).
        /// Distances use the full neighbor winding so blocked tiles still reserve angular slots.
        /// </summary>
        public static void PickSpreadRingTiles(
            List<int> ringOrdered,
            int want,
            List<int> into,
            Func<int, bool> eligible)
        {
            into.Clear();
            if (ringOrdered == null || ringOrdered.Count == 0 || want <= 0 || eligible == null) return;

            int n = ringOrdered.Count;
            tmpAtEligibleIdx.Clear();
            for (int i = 0; i < n; i++)
            {
                if (eligible(ringOrdered[i]))
                    tmpAtEligibleIdx.Add(i);
            }
            if (tmpAtEligibleIdx.Count == 0) return;

            int take = Mathf.Min(want, tmpAtEligibleIdx.Count);
            int firstSlot = Rand.Range(0, tmpAtEligibleIdx.Count);
            tmpAtPickedIdx.Clear();
            tmpAtPickedIdx.Add(tmpAtEligibleIdx[firstSlot]);
            into.Add(ringOrdered[tmpAtEligibleIdx[firstSlot]]);

            while (into.Count < take)
            {
                int bestIdx = -1;
                int bestMinDist = int.MinValue;
                for (int e = 0; e < tmpAtEligibleIdx.Count; e++)
                {
                    int i = tmpAtEligibleIdx[e];
                    if (tmpAtPickedIdx.Contains(i)) continue;

                    int minDist = int.MaxValue;
                    for (int p = 0; p < tmpAtPickedIdx.Count; p++)
                    {
                        int d = Mathf.Abs(i - tmpAtPickedIdx[p]);
                        d = Mathf.Min(d, n - d);
                        if (d < minDist) minDist = d;
                    }

                    if (minDist > bestMinDist)
                    {
                        bestMinDist = minDist;
                        bestIdx = i;
                    }
                }

                if (bestIdx < 0) break;
                tmpAtPickedIdx.Add(bestIdx);
                into.Add(ringOrdered[bestIdx]);
            }
        }

        public static void CollectTilesAtExactRadius(int center, int radius, List<int> into)
        {
            into.Clear();
            if (center < 0 || radius < 1) return;
            tmpSeen.Clear();
            var q = new Queue<(int tile, int dist)>();
            q.Enqueue((center, 0));
            tmpSeen.Add(center);
            while (q.Count > 0)
            {
                var (tile, dist) = q.Dequeue();
                if (dist == radius)
                {
                    into.Add(tile);
                    continue;
                }
                if (dist >= radius) continue;
                tmpNeighbors.Clear();
                Find.WorldGrid.GetTileNeighbors(tile, tmpNeighbors);
                for (int i = 0; i < tmpNeighbors.Count; i++)
                {
                    int n = tmpNeighbors[i].tileId;
                    if (!tmpSeen.Add(n)) continue;
                    q.Enqueue((n, dist + 1));
                }
            }
        }

        /// <summary>
        /// If the r=2 ring has no road among eligible tiles, paint a short corridor from the hub
        /// to one eligible outer tile (road def from kit tier). Returns the outer tile that was
        /// paved when a road was created; otherwise -1. Caller places the trap after pave.
        /// </summary>
        public static int EnsureRingHasRoadExit(int centerTile, SettlementTier kitTier)
        {
            if (centerTile < 0) return -1;

            CollectTilesAtExactRadius(centerTile, 2, tmpRingTiles);
            for (int i = 0; i < tmpRingTiles.Count; i++)
            {
                int t = tmpRingTiles[i];
                if (!IsFortifyTileOk(t)) continue;
                if (AtTurretUtility.TileHasRoad(t))
                    return -1;
            }

            int dest = PickEligibleOuterTile(centerTile);
            if (dest < 0) return -1;

            RoadDef road = WorldActions_Roads.GetRoadDefByTier(kitTier);
            if (road == null) return -1;
            if (!TryPaintShortRoadPath(centerTile, dest, road)) return -1;
            return dest;
        }

        public static void TryPlaceFortifyKit(
            int centerTile,
            Faction faction,
            SettlementTier kitTier,
            Settlement? builtBySettlement,
            WorldObject? builtBySite)
        {
            if (centerTile < 0 || faction == null) return;

            GetKit(kitTier, out SpikeTrapKind trapKind, out RoadBlockKind blockKind, out AtTurretTier atTier, out int maxAt);

            EnsureRingHasRoadExit(centerTile, kitTier);

            var roadBlocks = WorldComponent_RoadBlocks.Get();
            var traps = WorldComponent_SpikeTraps.Get();

            int atWant = AdditionalAtWant(
                maxAt, builtBySettlement, CountAtTurretsAtRadius1(centerTile, faction), includeInFlight: false);
            CollectOrderedNeighbors(centerTile, tmpInnerRingTiles);
            PickSpreadRingTiles(tmpInnerRingTiles, atWant, tmpAtPick, IsFortifyTileOk);
            int atPlaced = 0;
            for (int i = 0; i < tmpAtPick.Count; i++)
            {
                if (AtTurretUtility.TrySpawn(
                        tmpAtPick[i], faction, atTier, builtBySettlement, builtBySite,
                        requirePlayerBuildSite: false,
                        allowRoadTile: true,
                        ignoreSettlementCap: false) != null)
                    atPlaced++;
            }

            CollectTilesAtExactRadius(centerTile, 2, tmpRingTiles);
            int trapsPlaced = 0;
            int blocksPlaced = 0;

            for (int i = 0; i < tmpRingTiles.Count; i++)
            {
                int t = tmpRingTiles[i];
                if (!IsFortifyTileOk(t)) continue;
                if (!AtTurretUtility.TileHasRoad(t)) continue;
                if (traps != null && traps.TryPlaceOrUpgrade(t, faction, trapKind, builtBySettlement))
                    trapsPlaced++;
            }

            for (int i = 0; i < tmpRingTiles.Count; i++)
            {
                int t = tmpRingTiles[i];
                if (!IsFortifyTileOk(t)) continue;
                if (AtTurretUtility.TileHasRoad(t)) continue;
                if (roadBlocks != null && roadBlocks.TryPlaceOrUpgrade(t, faction, blockKind, builtBySettlement))
                    blocksPlaced++;
            }

            for (int i = 0; i < tmpRingTiles.Count; i++)
            {
                int t = tmpRingTiles[i];
                if (!IsFortifyTileOk(t)) continue;
                if (!AtTurretUtility.TileHasRoad(t)) continue;
                if (traps?.HasTrapAt(t) == true) continue;
                if (roadBlocks != null && roadBlocks.TryPlaceOrUpgrade(t, faction, blockKind, builtBySettlement))
                    blocksPlaced++;
            }

            WDVerbose.Msg(
                $"Fortify kit tier={kitTier} tile={centerTile} atR=1 outerR=2 traps={trapsPlaced} blocks={blocksPlaced} at={atPlaced}");
        }

        public static FortifyPhase ResolveNextPhase(
            int centerTile,
            Faction faction,
            SettlementTier kitTier,
            Settlement builtBySettlement)
        {
            if (centerTile < 0 || faction == null)
                return FortifyPhase.Complete;

            GetKit(kitTier, out SpikeTrapKind trapKind, out RoadBlockKind blockKind, out _, out int maxAt);

            if (NeedsTrapWork(centerTile, faction, trapKind, builtBySettlement))
                return FortifyPhase.Traps;
            if (NeedsBlockWork(centerTile, faction, blockKind, builtBySettlement))
                return FortifyPhase.Blocks;
            if (NeedsAtWork(centerTile, faction, maxAt, builtBySettlement))
                return FortifyPhase.AtTurrets;
            return FortifyPhase.Complete;
        }

        public static void CollectMissingTrapTiles(
            int centerTile,
            Faction faction,
            SpikeTrapKind trapKind,
            Settlement builtBySettlement,
            HashSet<int> exclude,
            List<int> into)
        {
            into.Clear();
            if (centerTile < 0) return;
            CollectTilesAtExactRadius(centerTile, 2, tmpRingTiles);
            var traps = WorldComponent_SpikeTraps.Get();
            for (int i = 0; i < tmpRingTiles.Count; i++)
            {
                int t = tmpRingTiles[i];
                if (exclude != null && exclude.Contains(t)) continue;
                if (!IsFortifyTileOk(t)) continue;
                if (!AtTurretUtility.TileHasRoad(t)) continue;
                if (WorldComponent_FortifyBlacklist.BlocksNpcFortify(t, faction)) continue;
                if (!NeedsTrapAt(t, traps, trapKind)) continue;
                into.Add(t);
            }
        }

        public static void CollectMissingBlockTiles(
            int centerTile,
            Faction faction,
            RoadBlockKind blockKind,
            Settlement builtBySettlement,
            HashSet<int> exclude,
            List<int> into)
        {
            into.Clear();
            if (centerTile < 0) return;
            CollectTilesAtExactRadius(centerTile, 2, tmpRingTiles);
            var blocks = WorldComponent_RoadBlocks.Get();
            var traps = WorldComponent_SpikeTraps.Get();
            for (int i = 0; i < tmpRingTiles.Count; i++)
            {
                int t = tmpRingTiles[i];
                if (exclude != null && exclude.Contains(t)) continue;
                if (!IsFortifyTileOk(t)) continue;
                if (WorldComponent_FortifyBlacklist.BlocksNpcFortify(t, faction)) continue;
                if (AtTurretUtility.TileHasRoad(t))
                {
                    if (traps?.HasTrapAt(t) == true) continue;
                }
                if (!NeedsBlockAt(t, blocks, blockKind)) continue;
                into.Add(t);
            }
        }

        public static void CollectMissingAtTiles(
            int centerTile,
            Faction faction,
            int maxAt,
            Settlement builtBySettlement,
            HashSet<int> exclude,
            List<int> into)
        {
            into.Clear();
            if (centerTile < 0 || maxAt <= 0) return;

            int ringExisting = CountAtTurretsAtRadius1(centerTile, faction);
            int want = AdditionalAtWant(maxAt, builtBySettlement, ringExisting, includeInFlight: true);
            if (want <= 0) return;

            CollectOrderedNeighbors(centerTile, tmpInnerRingTiles);
            PickSpreadRingTiles(tmpInnerRingTiles, want, tmpAtPick, t =>
            {
                if (exclude != null && exclude.Contains(t)) return false;
                if (!IsFortifyTileOk(t)) return false;
                if (WorldComponent_FortifyBlacklist.BlocksNpcFortify(t, faction)) return false;
                return AtTurretUtility.IsEmptyTurretSite(t, requireOffRoad: false);
            });
            into.AddRange(tmpAtPick);
        }

        private static bool NeedsTrapWork(
            int centerTile,
            Faction faction,
            SpikeTrapKind trapKind,
            Settlement builtBySettlement)
        {
            CollectTilesAtExactRadius(centerTile, 2, tmpRingTiles);
            bool anyEligible = false;
            bool anyRoad = false;
            var traps = WorldComponent_SpikeTraps.Get();
            for (int i = 0; i < tmpRingTiles.Count; i++)
            {
                int t = tmpRingTiles[i];
                if (!IsFortifyTileOk(t)) continue;
                if (WorldComponent_FortifyBlacklist.BlocksNpcFortify(t, faction)) continue;
                anyEligible = true;
                if (!AtTurretUtility.TileHasRoad(t)) continue;
                anyRoad = true;
                if (NeedsTrapAt(t, traps, trapKind))
                    return true;
            }

            // No road exit yet among eligible tiles: trap phase paints then places.
            if (anyEligible && !anyRoad)
                return true;
            return false;
        }

        private static bool NeedsBlockWork(
            int centerTile,
            Faction faction,
            RoadBlockKind blockKind,
            Settlement builtBySettlement)
        {
            CollectTilesAtExactRadius(centerTile, 2, tmpRingTiles);
            var blocks = WorldComponent_RoadBlocks.Get();
            var traps = WorldComponent_SpikeTraps.Get();
            for (int i = 0; i < tmpRingTiles.Count; i++)
            {
                int t = tmpRingTiles[i];
                if (!IsFortifyTileOk(t)) continue;
                if (WorldComponent_FortifyBlacklist.BlocksNpcFortify(t, faction)) continue;
                if (AtTurretUtility.TileHasRoad(t) && traps?.HasTrapAt(t) == true)
                    continue;
                if (NeedsBlockAt(t, blocks, blockKind))
                    return true;
            }
            return false;
        }

        private static bool NeedsAtWork(int centerTile, Faction faction, int maxAt, Settlement builtBySettlement)
        {
            int ringExisting = CountAtTurretsAtRadius1(centerTile, faction);
            if (AdditionalAtWant(maxAt, builtBySettlement, ringExisting, includeInFlight: true) <= 0)
                return false;
            CollectOrderedNeighbors(centerTile, tmpInnerRingTiles);
            for (int i = 0; i < tmpInnerRingTiles.Count; i++)
            {
                int t = tmpInnerRingTiles[i];
                if (!IsFortifyTileOk(t)) continue;
                if (WorldComponent_FortifyBlacklist.BlocksNpcFortify(t, faction)) continue;
                if (AtTurretUtility.IsEmptyTurretSite(t, requireOffRoad: false))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// How many more ATs to place: min(kit remaining vs ring, World Actions settlement capacity remaining).
        /// Null builder → 0 (no uncapped orphans).
        /// </summary>
        private static int AdditionalAtWant(
            int kitMaxAt,
            Settlement builtBySettlement,
            int ringExisting,
            bool includeInFlight)
        {
            if (builtBySettlement == null || builtBySettlement.Destroyed || kitMaxAt <= 0) return 0;
            var comp = builtBySettlement.GetComponent<CompViralSpread>();
            SettlementTier tier = comp?.tier ?? SettlementTier.T1;
            int cap = AtTurretUtility.MaxTurretsForSettlementTier(tier);
            int committed = AtTurretUtility.CountTurretsBuiltBy(builtBySettlement);
            if (includeInFlight)
                committed += AtTurretUtility.CountInFlightTurretCrews(builtBySettlement);
            int remainingCap = Mathf.Max(0, cap - committed);
            int kitRemaining = Mathf.Max(0, kitMaxAt - Mathf.Max(0, ringExisting));
            return Mathf.Min(kitRemaining, remainingCap);
        }

        private static bool NeedsTrapAt(int tileId, WorldComponent_SpikeTraps traps, SpikeTrapKind want)
        {
            if (traps == null) return true;
            if (!traps.TryGet(tileId, out SpikeTrapRecord existing) || existing == null)
                return true;
            return SpikeTrapKindUtil.CanUpgradeTo(existing.kind, want);
        }

        private static bool NeedsBlockAt(int tileId, WorldComponent_RoadBlocks blocks, RoadBlockKind want)
        {
            if (blocks == null) return true;
            if (!blocks.TryGet(tileId, out RoadBlockRecord existing) || existing == null)
                return true;
            return RoadBlockKindUtil.CanUpgradeTo(existing.kind, want);
        }

        private static int CountAtTurretsAtRadius1(int centerTile, Faction faction)
        {
            int count = 0;
            CollectOrderedNeighbors(centerTile, tmpInnerRingTiles);
            for (int i = 0; i < tmpInnerRingTiles.Count; i++)
            {
                WorldObject_AT_Turret t = AtTurretUtility.FindTurretAt(tmpInnerRingTiles[i]);
                if (t == null || t.Destroyed) continue;
                if (faction != null && t.Faction != faction) continue;
                count++;
            }
            return count;
        }

        private static int PickEligibleOuterTile(int centerTile)
        {
            CollectTilesAtExactRadius(centerTile, 2, tmpRingTiles);
            tmpPath.Clear();
            for (int i = 0; i < tmpRingTiles.Count; i++)
            {
                int t = tmpRingTiles[i];
                if (!IsFortifyTileOk(t)) continue;
                if (!CanReachWithPassableMids(centerTile, t)) continue;
                tmpPath.Add(t);
            }
            if (tmpPath.Count == 0) return -1;
            return tmpPath[Rand.Range(0, tmpPath.Count)];
        }

        private static bool CanReachWithPassableMids(int center, int dest)
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return false;
            if (grid.IsNeighbor(center, dest)) return true;

            tmpNeighbors.Clear();
            grid.GetTileNeighbors(center, tmpNeighbors);
            for (int i = 0; i < tmpNeighbors.Count; i++)
            {
                int mid = tmpNeighbors[i].tileId;
                if (!grid.IsNeighbor(mid, dest)) continue;
                if (Find.World.Impassable(mid)) continue;
                if (!PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(mid)) continue;
                return true;
            }
            return false;
        }

        private static bool TryPaintShortRoadPath(int center, int dest, RoadDef road)
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || road == null) return false;

            // Direct neighbor (should not happen for r=2, but safe).
            if (grid.IsNeighbor(center, dest))
            {
                WorldActions_Roads.ApplyRoadLink(
                    new PlanetTile(center, grid[center].Layer),
                    new PlanetTile(dest, grid[dest].Layer),
                    road);
                return true;
            }

            tmpNeighbors.Clear();
            grid.GetTileNeighbors(center, tmpNeighbors);
            List<int> mids = new List<int>();
            for (int i = 0; i < tmpNeighbors.Count; i++)
            {
                int mid = tmpNeighbors[i].tileId;
                if (!grid.IsNeighbor(mid, dest)) continue;
                if (Find.World.Impassable(mid)) continue;
                if (!PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(mid)) continue;
                mids.Add(mid);
            }
            if (mids.Count == 0) return false;

            int midPick = mids[Rand.Range(0, mids.Count)];
            WorldActions_Roads.ApplyRoadLink(
                new PlanetTile(center, grid[center].Layer),
                new PlanetTile(midPick, grid[midPick].Layer),
                road);
            WorldActions_Roads.ApplyRoadLink(
                new PlanetTile(midPick, grid[midPick].Layer),
                new PlanetTile(dest, grid[dest].Layer),
                road);
            return true;
        }
    }
}
