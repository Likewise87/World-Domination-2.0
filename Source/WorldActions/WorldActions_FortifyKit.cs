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
    /// AT prefer r=1 then r=2. Outer traps/blocks: r=2 (T1/T2) or r=3 (T3/T4).
    /// Ensures at least one road exit on the outer ring before placing traps.
    /// </summary>
    public static class WorldActions_FortifyKit
    {
        private static readonly List<PlanetTile> tmpNeighbors = new List<PlanetTile>();
        private static readonly List<int> tmpRingTiles = new List<int>();
        private static readonly List<int> tmpInnerRingTiles = new List<int>();
        private static readonly List<int> tmpAtRing2 = new List<int>();
        private static readonly List<int> tmpAtPick = new List<int>();
        private static readonly List<int> tmpAtEligibleIdx = new List<int>();
        private static readonly List<int> tmpAtPickedIdx = new List<int>();
        private static readonly List<int> tmpPath = new List<int>();
        private static readonly HashSet<int> tmpSeen = new HashSet<int>();
        private static readonly Dictionary<int, int> tmpBfsParent = new Dictionary<int, int>();
        private static readonly Queue<(int tile, int dist)> tmpBfsQ = new Queue<(int, int)>();

        public enum FortifyPhase : byte
        {
            Traps = 0,
            Blocks = 1,
            AtTurrets = 2,
            Complete = 3
        }

        /// <summary>Trap/block ring: T1/T2 = 2, T3/T4 = 3.</summary>
        public static int OuterFortifyRadius(SettlementTier tier) =>
            tier >= SettlementTier.T3 ? 3 : 2;

        public static void GetKit(
            SettlementTier tier,
            out SpikeTrapKind trapKind,
            out RoadBlockKind blockKind,
            out AtTurretTier atTier,
            out int maxAt,
            Faction faction = null)
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

            float dens = FortifyDensityMult(faction);
            if (Mathf.Abs(dens - 1f) > 0.001f)
                maxAt = Mathf.Max(1, Mathf.CeilToInt(maxAt * dens));
        }

        public static float FortifyDensityMult(Faction faction)
        {
            var seth = WorldDominationMod.settings;
            if (seth == null || faction == null) return 1f;
            if (WorldActions_DiplomacyBuffsNerfs.IsActiveUnderdog(faction))
                return Mathf.Max(0.1f, seth.underdogFortifyDensityMult);
            if (WorldActions_DiplomacyBuffsNerfs.IsActiveLeader(faction))
                return Mathf.Max(0.1f, seth.leaderFortifyDensityMult);
            return 1f;
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
        /// If the outer fortify ring has no road among eligible tiles, paint a short corridor from the hub
        /// to one eligible outer tile (road def from kit tier). Returns the outer tile that was
        /// paved when a road was created; otherwise -1. Caller places the trap after pave.
        /// </summary>
        public static int EnsureRingHasRoadExit(int centerTile, SettlementTier kitTier)
        {
            if (centerTile < 0) return -1;
            int outer = OuterFortifyRadius(kitTier);

            CollectTilesAtExactRadius(centerTile, outer, tmpRingTiles);
            for (int i = 0; i < tmpRingTiles.Count; i++)
            {
                int t = tmpRingTiles[i];
                if (!IsFortifyTileOk(t)) continue;
                if (AtTurretUtility.TileHasRoad(t))
                    return -1;
            }

            int dest = PickEligibleOuterTile(centerTile, outer);
            if (dest < 0) return -1;

            RoadDef road = WorldActions_Roads.GetRoadDefByTier(kitTier);
            if (road == null) return -1;
            if (!TryPaintShortRoadPath(centerTile, dest, road, outer)) return -1;
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

            GetKit(kitTier, out SpikeTrapKind trapKind, out RoadBlockKind blockKind, out AtTurretTier atTier, out int maxAt, faction);
            int outer = OuterFortifyRadius(kitTier);

            EnsureRingHasRoadExit(centerTile, kitTier);

            var roadBlocks = WorldComponent_RoadBlocks.Get();
            var traps = WorldComponent_SpikeTraps.Get();

            int atWant = AdditionalAtWant(
                maxAt, builtBySettlement, CountAtTurretsInAtRings(centerTile, faction), includeInFlight: false);
            int atPlaced = PlaceAtOnPreferRings(
                centerTile, faction, atTier, atWant, builtBySettlement, builtBySite, exclude: null);

            CollectTilesAtExactRadius(centerTile, outer, tmpRingTiles);
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
                $"Fortify kit tier={kitTier} tile={centerTile} atR=1..2 outerR={outer} traps={trapsPlaced} blocks={blocksPlaced} at={atPlaced}");
        }

        public static FortifyPhase ResolveNextPhase(
            int centerTile,
            Faction faction,
            SettlementTier kitTier,
            Settlement builtBySettlement)
        {
            if (centerTile < 0 || faction == null)
                return FortifyPhase.Complete;

            GetKit(kitTier, out SpikeTrapKind trapKind, out RoadBlockKind blockKind, out _, out int maxAt, faction);

            if (NeedsTrapWork(centerTile, faction, trapKind, kitTier))
                return FortifyPhase.Traps;
            if (NeedsBlockWork(centerTile, faction, blockKind, kitTier))
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
            SettlementTier tier = builtBySettlement?.GetComponent<CompViralSpread>()?.tier ?? SettlementTier.T1;
            CollectTilesAtExactRadius(centerTile, OuterFortifyRadius(tier), tmpRingTiles);
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
            SettlementTier tier = builtBySettlement?.GetComponent<CompViralSpread>()?.tier ?? SettlementTier.T1;
            CollectTilesAtExactRadius(centerTile, OuterFortifyRadius(tier), tmpRingTiles);
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

            int ringExisting = CountAtTurretsInAtRings(centerTile, faction);
            int want = AdditionalAtWant(maxAt, builtBySettlement, ringExisting, includeInFlight: true);
            if (want <= 0) return;

            CollectAtCandidatesPreferRings(centerTile, faction, want, exclude, into);
        }

        private static void CollectAtCandidatesPreferRings(
            int centerTile,
            Faction faction,
            int want,
            HashSet<int> exclude,
            List<int> into)
        {
            into.Clear();
            if (want <= 0) return;

            Func<int, bool> eligible = t =>
            {
                if (exclude != null && exclude.Contains(t)) return false;
                if (!IsFortifyTileOk(t)) return false;
                if (WorldComponent_FortifyBlacklist.BlocksNpcFortify(t, faction)) return false;
                return AtTurretUtility.IsEmptyTurretSite(t, requireOffRoad: false);
            };

            CollectOrderedNeighbors(centerTile, tmpInnerRingTiles);
            PickSpreadRingTiles(tmpInnerRingTiles, want, tmpAtPick, eligible);
            into.AddRange(tmpAtPick);
            int remain = want - into.Count;
            if (remain <= 0) return;

            CollectTilesAtExactRadius(centerTile, 2, tmpAtRing2);
            // Ordered for PickSpread: use BFS order as-is.
            PickSpreadRingTiles(tmpAtRing2, remain, tmpAtPick, t =>
            {
                if (into.Contains(t)) return false;
                return eligible(t);
            });
            into.AddRange(tmpAtPick);
        }

        private static int PlaceAtOnPreferRings(
            int centerTile,
            Faction faction,
            AtTurretTier atTier,
            int want,
            Settlement builtBySettlement,
            WorldObject builtBySite,
            HashSet<int> exclude)
        {
            if (want <= 0) return 0;
            CollectAtCandidatesPreferRings(centerTile, faction, want, exclude, tmpPath);
            int placed = 0;
            for (int i = 0; i < tmpPath.Count; i++)
            {
                if (AtTurretUtility.TrySpawn(
                        tmpPath[i], faction, atTier, builtBySettlement, builtBySite,
                        requirePlayerBuildSite: false,
                        allowRoadTile: true,
                        ignoreSettlementCap: false) != null)
                    placed++;
            }
            return placed;
        }

        private static bool NeedsTrapWork(
            int centerTile,
            Faction faction,
            SpikeTrapKind trapKind,
            SettlementTier kitTier)
        {
            CollectTilesAtExactRadius(centerTile, OuterFortifyRadius(kitTier), tmpRingTiles);
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
            SettlementTier kitTier)
        {
            CollectTilesAtExactRadius(centerTile, OuterFortifyRadius(kitTier), tmpRingTiles);
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
            int ringExisting = CountAtTurretsInAtRings(centerTile, faction);
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

            CollectTilesAtExactRadius(centerTile, 2, tmpAtRing2);
            for (int i = 0; i < tmpAtRing2.Count; i++)
            {
                int t = tmpAtRing2[i];
                if (!IsFortifyTileOk(t)) continue;
                if (WorldComponent_FortifyBlacklist.BlocksNpcFortify(t, faction)) continue;
                if (AtTurretUtility.IsEmptyTurretSite(t, requireOffRoad: false))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// How many more ATs to place: min(kit remaining vs preferred AT rings, World Actions settlement capacity remaining).
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

        /// <summary>ATs already on preferred rings r=1 and r=2 (counts toward kit fill).</summary>
        private static int CountAtTurretsInAtRings(int centerTile, Faction faction)
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
            CollectTilesAtExactRadius(centerTile, 2, tmpAtRing2);
            for (int i = 0; i < tmpAtRing2.Count; i++)
            {
                WorldObject_AT_Turret t = AtTurretUtility.FindTurretAt(tmpAtRing2[i]);
                if (t == null || t.Destroyed) continue;
                if (faction != null && t.Faction != faction) continue;
                count++;
            }
            return count;
        }

        private static int PickEligibleOuterTile(int centerTile, int outerRadius)
        {
            CollectTilesAtExactRadius(centerTile, outerRadius, tmpRingTiles);
            tmpPath.Clear();
            for (int i = 0; i < tmpRingTiles.Count; i++)
            {
                int t = tmpRingTiles[i];
                if (!IsFortifyTileOk(t)) continue;
                if (!TryBuildShortPath(centerTile, t, outerRadius, paint: false, road: null)) continue;
                tmpPath.Add(t);
            }
            if (tmpPath.Count == 0) return -1;
            return tmpPath[Rand.Range(0, tmpPath.Count)];
        }

        private static bool TryPaintShortRoadPath(int center, int dest, RoadDef road, int maxHops) =>
            TryBuildShortPath(center, dest, maxHops, paint: true, road);

        /// <summary>
        /// BFS path of at most <paramref name="maxHops"/> hops with passable mids.
        /// When <paramref name="paint"/>, applies <paramref name="road"/> along the path.
        /// </summary>
        private static bool TryBuildShortPath(int center, int dest, int maxHops, bool paint, RoadDef road)
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || center < 0 || dest < 0 || maxHops < 1) return false;
            if (center == dest) return false;

            tmpBfsParent.Clear();
            tmpBfsQ.Clear();
            tmpSeen.Clear();
            tmpBfsQ.Enqueue((center, 0));
            tmpSeen.Add(center);
            tmpBfsParent[center] = -1;
            bool found = false;
            while (tmpBfsQ.Count > 0)
            {
                var (tile, dist) = tmpBfsQ.Dequeue();
                if (tile == dest)
                {
                    found = true;
                    break;
                }
                if (dist >= maxHops) continue;
                tmpNeighbors.Clear();
                grid.GetTileNeighbors(tile, tmpNeighbors);
                for (int i = 0; i < tmpNeighbors.Count; i++)
                {
                    int n = tmpNeighbors[i].tileId;
                    if (!tmpSeen.Add(n)) continue;
                    if (n != dest)
                    {
                        if (Find.World.Impassable(n)) continue;
                        if (!PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(n)) continue;
                    }
                    tmpBfsParent[n] = tile;
                    tmpBfsQ.Enqueue((n, dist + 1));
                }
            }
            if (!found || !tmpBfsParent.ContainsKey(dest)) return false;

            if (!paint) return true;
            if (road == null) return false;

            tmpPath.Clear();
            int walk = dest;
            while (walk >= 0)
            {
                tmpPath.Add(walk);
                if (!tmpBfsParent.TryGetValue(walk, out int parent) || parent < 0)
                    break;
                walk = parent;
            }
            // tmpPath is dest → … → center
            for (int i = tmpPath.Count - 1; i > 0; i--)
            {
                int a = tmpPath[i];
                int b = tmpPath[i - 1];
                WorldActions_Roads.ApplyRoadLink(
                    new PlanetTile(a, grid[a].Layer),
                    new PlanetTile(b, grid[b].Layer),
                    road);
            }
            return true;
        }
    }
}
