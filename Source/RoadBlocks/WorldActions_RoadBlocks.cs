using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public static class WorldActions_RoadBlocks
    {
        public static float GetRoadBlockProgressRequiredTicks(RoadBlockKind kind)
        {
            SettlementTier baselineTier = RoadBlockKindUtil.WorkBaselineTier(kind);
            float baseline = WorldActions_Roads.GetRoadProgressRequiredTicks(baselineTier);
            var s = WorldDominationMod.settings;
            float work = s != null ? s.GetRoadBlockWork(kind) : WorldDominationSettings.DefRoadBlockNormalWork;
            float refWork = s != null
                ? s.GetFallbackRoadWork(baselineTier)
                : WorldDominationSettings.DefFallbackDirtRoadWork;
            if (refWork < 1f) refWork = 1f;
            return baseline * (Mathf.Max(1f, work) / refWork);
        }

        public static float GetRoadBlockProgressRequiredTicks()
        {
            return GetRoadBlockProgressRequiredTicks(RoadBlockKind.Normal);
        }

        /// <summary>Estimated in-game days to fill progress for one road-block segment at current Construction skill. Excludes caravan travel.</summary>
        public static float GetEstimatedDaysPerRoadBlockSegment(WorldObject actor, RoadBlockKind kind)
        {
            if (actor == null) return -1f;
            float workSpeed = WorldActions_Roads.GetRoadProgressWorkSpeed(actor);
            if (workSpeed < 0.01f) return -1f;
            float ticks = GetRoadBlockProgressRequiredTicks(kind) / workSpeed;
            return ticks / GenDate.TicksPerDay;
        }

        public static float GetEstimatedDaysPerRoadBlockSegment(WorldObject_WD_Outpost outpost, RoadBlockKind kind) =>
            GetEstimatedDaysPerRoadBlockSegment((WorldObject)outpost, kind);

        public static float GetEstimatedDaysPerRoadBlockSegment(WorldObject_WD_Outpost outpost)
        {
            return GetEstimatedDaysPerRoadBlockSegment(outpost, RoadBlockKind.Normal);
        }

        public static float GetExpeditionStrengthCost(RoadBlockKind kind)
        {
            var s = WorldDominationMod.settings;
            return Mathf.Max(1f, s != null ? s.GetRoadBlockExpeditionStrength(kind) : WorldDominationSettings.DefRoadBlockNormalExpeditionStrength);
        }

        public static float GetExpeditionStrengthCost()
        {
            return GetExpeditionStrengthCost(RoadBlockKind.Normal);
        }

        public static int GetMinConstruction(RoadBlockKind kind)
        {
            return WorldActions_Roads.GetMinConstructionToBuildRoad(RoadBlockKindUtil.WorkBaselineTier(kind));
        }

        /// <summary>Localized label for the highest road-block kind this site can start (by Construction skill).</summary>
        public static string GetHighestBuildableKindLabel(float totalConstruction)
        {
            if (totalConstruction >= GetMinConstruction(RoadBlockKind.Heavy))
                return RoadBlockKindUtil.LabelKey(RoadBlockKind.Heavy).Translate().ToString();
            if (totalConstruction >= GetMinConstruction(RoadBlockKind.Normal))
                return RoadBlockKindUtil.LabelKey(RoadBlockKind.Normal).Translate().ToString();
            return RoadBlockKindUtil.LabelKey(RoadBlockKind.Light).Translate().ToString();
        }

        public static float GetMaxRange()
        {
            var s = WorldDominationMod.settings;
            return s != null ? s.maxRoadBlockRange : WorldDominationSettings.DefMaxRoadBlockRange;
        }

        public static float GetMaxRange(WorldObject source)
        {
            float range = GetMaxRange();
            if (source is WorldObject_WD_Outpost wdOutpost)
                range *= 1f + OutpostExpertUtility.GetEngineerConstructionRadiusBonus(wdOutpost);
            return range;
        }

        public static bool HasActiveRoadBlockProject(CompViralSpread comp)
        {
            return comp != null && comp.roadBlockPlannedTiles != null && comp.roadBlockPlannedTiles.Count > 0;
        }

        public static bool IsTileBaseEligibleForRoadBlock(int tileId)
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || tileId < 0 || !grid.InBounds(tileId)) return false;
            PlanetLayer layer = PlanetSurfaceWorldActions.WdSurfaceLayer ?? grid.Surface;
            if (layer == null) return false;
            PlanetTile pTile = new PlanetTile(tileId, layer);
            if (Find.World.Impassable(pTile)) return false;
            if (grid[tileId].WaterCovered) return false;
            if (TileHasSettlementOrOutpost(tileId)) return false;
            return true;
        }

        public static bool TileHasSettlementOrOutpost(int tileId)
        {
            if (Find.WorldObjects == null || tileId < 0) return false;

            // Layer-aware lookup first (Odyssey); int overload as fallback.
            PlanetLayer layer = PlanetSurfaceWorldActions.WdSurfaceLayer ?? Find.WorldGrid?.Surface;
            if (layer != null)
            {
                PlanetTile pt = new PlanetTile(tileId, layer);
                if (Find.WorldObjects.AnySettlementAt(pt)) return true;
                foreach (WorldObject wo in Find.WorldObjects.ObjectsAt(pt))
                {
                    if (IsBlockedFortifyOccupant(wo)) return true;
                }
            }

            if (Find.WorldObjects.AnySettlementAt(tileId)) return true;
            foreach (WorldObject wo in Find.WorldObjects.ObjectsAt(tileId))
            {
                if (IsBlockedFortifyOccupant(wo)) return true;
            }

            // Belt-and-suspenders: scan known settlements / outposts / player map parents by tile id.
            var settlements = Find.WorldObjects.Settlements;
            if (settlements != null)
            {
                for (int i = 0; i < settlements.Count; i++)
                {
                    Settlement s = settlements[i];
                    if (s != null && !s.Destroyed && s.Tile.tileId == tileId)
                        return true;
                }
            }

            var all = Find.WorldObjects.AllWorldObjects;
            if (all != null)
            {
                for (int i = 0; i < all.Count; i++)
                {
                    WorldObject wo = all[i];
                    if (wo == null || wo.Destroyed || wo.Tile.tileId != tileId) continue;
                    if (IsBlockedFortifyOccupant(wo)) return true;
                }
            }

            return false;
        }

        /// <summary>Settlement, WD outpost, or player colony/map occupying a tile (not fortifyable).</summary>
        public static bool IsBlockedFortifyOccupant(WorldObject wo)
        {
            if (wo == null || wo.Destroyed) return false;
            if (wo is Settlement || wo is WorldObject_WD_Outpost) return true;
            if (wo is MapParent && wo.Faction != null && wo.Faction.IsPlayer) return true;
            return false;
        }

        /// <summary>
        /// Builder may tear down a hostile (or ownerless) fortification when placing their own.
        /// </summary>
        public static bool CanClaimHostileFortification(Faction builder, Faction existingOwner)
        {
            if (builder == null) return false;
            if (existingOwner == null) return true;
            if (existingOwner == builder) return false;
            return WorldActions_Utils.SafeHostileTo(builder, existingOwner);
        }

        public static bool IsValidBuildTile(int tileId, RoadBlockKind kind, Faction builder = null)
        {
            if (!IsTileBaseEligibleForRoadBlock(tileId)) return false;
            // Spike traps (any owner) are cleared on place; they must not block planning or arrival.

            WorldComponent_RoadBlocks blocks = WorldComponent_RoadBlocks.Get();
            if (blocks == null) return true;
            if (!blocks.TryGet(tileId, out RoadBlockRecord existing) || existing == null)
                return true;
            if (RoadBlockKindUtil.CanUpgradeTo(existing.kind, kind))
                return true;
            return CanClaimHostileFortification(builder, existing.builtByFaction)
                && RoadBlockKindUtil.IsPlaceableFromUi(kind);
        }

        public static bool IsValidBuildTile(int tileId)
        {
            return IsValidBuildTile(tileId, RoadBlockKind.Normal);
        }

        public static bool IsValidClearTile(int tileId)
        {
            WorldComponent_RoadBlocks blocks = WorldComponent_RoadBlocks.Get();
            return blocks != null && blocks.HasBlockAt(tileId);
        }

        /// <summary>Removes a road block on <paramref name="tileId"/> if present (e.g. settlement/outpost spawn).</summary>
        public static bool ClearIfPresent(int tileId)
        {
            WorldComponent_RoadBlocks blocks = WorldComponent_RoadBlocks.Get();
            return blocks != null && blocks.TryClear(tileId);
        }

        /// <summary>
        /// Clickable planning node for build mode: any land tile that could hold a block
        /// (including tiles that already have one). Settlements/outposts still excluded.
        /// </summary>
        public static bool IsValidBuildPlanNode(int tileId)
        {
            return IsTileBaseEligibleForRoadBlock(tileId);
        }

        /// <summary>
        /// Uniform-cost hop path (ignores terrain/road/block movement difficulty).
        /// Returns dest-first tile ids (same convention as <see cref="WorldPath.NodesReversed"/>), or null.
        /// </summary>
        public static List<int> FindFlatHopPathDestFirst(int startTileId, int endTileId)
        {
            if (startTileId == endTileId)
                return new List<int> { startTileId };

            WorldGrid grid = Find.WorldGrid;
            PlanetLayer layer = PlanetSurfaceWorldActions.WdSurfaceLayer;
            if (grid == null || layer == null) return null;
            if (!grid.InBounds(startTileId) || !grid.InBounds(endTileId)) return null;

            // Bound search: road-block legs are short; avoid scanning the whole planet.
            float direct = grid.ApproxDistanceInTiles(startTileId, endTileId);
            int maxHops = Mathf.Max(8, Mathf.CeilToInt(direct * 3f) + 8);
            if (maxHops > 64) maxHops = 64;

            flatPrev.Clear();
            flatHops.Clear();
            flatQueue.Clear();
            flatClosed.Clear();

            flatQueue.Enqueue(startTileId);
            flatPrev[startTileId] = -1;
            flatHops[startTileId] = 0;
            flatClosed.Add(startTileId);

            bool found = false;
            while (flatQueue.Count > 0)
            {
                int cur = flatQueue.Dequeue();
                if (cur == endTileId)
                {
                    found = true;
                    break;
                }

                int hops = flatHops[cur];
                if (hops >= maxHops)
                    continue;

                flatNeighbors.Clear();
                grid.GetTileNeighbors(cur, flatNeighbors);
                for (int i = 0; i < flatNeighbors.Count; i++)
                {
                    PlanetTile npt = flatNeighbors[i];
                    if (!npt.Valid || npt.Layer != layer) continue;
                    int next = npt.tileId;
                    if (!grid.InBounds(next) || flatClosed.Contains(next)) continue;
                    if (!IsFlatPathWalkable(next, layer)) continue;

                    flatClosed.Add(next);
                    flatPrev[next] = cur;
                    flatHops[next] = hops + 1;
                    flatQueue.Enqueue(next);
                }
            }

            if (!found || !flatPrev.ContainsKey(endTileId))
                return null;

            // Reconstruct end→start (dest-first), same as WorldPath.NodesReversed.
            var destFirst = new List<int>();
            for (int t = endTileId; t >= 0; t = flatPrev[t])
            {
                destFirst.Add(t);
                if (t == startTileId) break;
            }
            if (destFirst.Count < 1 || destFirst[destFirst.Count - 1] != startTileId)
                return null;
            return destFirst;
        }

        /// <summary>Walkable for planning corridors: land only; settlements and existing blocks allowed.</summary>
        public static bool IsFlatPathWalkable(int tileId, PlanetLayer layer = null)
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || tileId < 0 || !grid.InBounds(tileId)) return false;
            layer ??= PlanetSurfaceWorldActions.WdSurfaceLayer ?? grid.Surface;
            if (layer == null) return false;
            PlanetTile pTile = new PlanetTile(tileId, layer);
            if (Find.World.Impassable(pTile)) return false;
            if (grid[tileId].WaterCovered) return false;
            return true;
        }

        private static readonly Dictionary<int, int> flatPrev = new Dictionary<int, int>(128);
        private static readonly Dictionary<int, int> flatHops = new Dictionary<int, int>(128);
        private static readonly Queue<int> flatQueue = new Queue<int>(128);
        private static readonly HashSet<int> flatClosed = new HashSet<int>();
        private static readonly List<PlanetTile> flatNeighbors = new List<PlanetTile>(8);

        /// <summary>
        /// Work tiles from player-clicked nodes only (waypoints + final click).
        /// Includes corridor tiles between consecutive nodes; never the outpost→first-node leg.
        /// Build mode corridors use flat hop pathfinding (ignores movement difficulty / existing blocks).
        /// </summary>
        public static List<int> FilterPlannedTilesFromClickedNodes(List<int> clickedNodes, bool clearing, RoadBlockKind kind, Faction builder = null)
        {
            return FilterPlannedTilesFromClickedNodes(clickedNodes, clearing, kind, builder, clearAnyFortification: false);
        }

        public static List<int> FilterPlannedTilesFromClickedNodes(
            List<int> clickedNodes,
            bool clearing,
            RoadBlockKind kind,
            Faction builder,
            bool clearAnyFortification)
        {
            var result = new List<int>();
            if (clickedNodes == null || clickedNodes.Count == 0) return result;

            var seen = new HashSet<int>();
            void TryAdd(int tile)
            {
                if (!seen.Add(tile)) return;
                if (clearing)
                {
                    if (clearAnyFortification
                        ? WorldActions_Fortifications.HasFortificationAt(tile)
                        : IsValidClearTile(tile))
                        result.Add(tile);
                }
                else if (IsValidBuildTile(tile, kind, builder))
                {
                    result.Add(tile);
                }
            }

            TryAdd(clickedNodes[0]);

            for (int i = 0; i < clickedNodes.Count - 1; i++)
            {
                int a = clickedNodes[i];
                int b = clickedNodes[i + 1];
                if (a == b) continue;

                List<int> pathDestFirst = FindFlatHopPathDestFirst(a, b);
                if (pathDestFirst == null || pathDestFirst.Count < 2) continue;
                for (int n = pathDestFirst.Count - 1; n >= 0; n--)
                    TryAdd(pathDestFirst[n]);
            }

            return result;
        }

        public static List<int> FilterPlannedTilesFromClickedNodes(List<int> clickedNodes, bool clearing)
        {
            return FilterPlannedTilesFromClickedNodes(clickedNodes, clearing, RoadBlockKind.Normal);
        }

        /// <summary>Legacy helper: path tiles excluding origin (prefer <see cref="FilterPlannedTilesFromClickedNodes"/>).</summary>
        public static List<int> FilterPlannedTilesFromPath(List<int> nodesDestFirst, int originTile, bool clearing, RoadBlockKind kind, Faction builder = null)
        {
            var result = new List<int>();
            if (nodesDestFirst == null || nodesDestFirst.Count < 2) return result;

            var seen = new HashSet<int>();
            for (int i = nodesDestFirst.Count - 2; i >= 0; i--)
            {
                int tile = nodesDestFirst[i];
                if (tile == originTile) continue;
                if (!seen.Add(tile)) continue;
                if (clearing)
                {
                    if (IsValidClearTile(tile))
                        result.Add(tile);
                }
                else if (IsValidBuildTile(tile, kind, builder))
                {
                    result.Add(tile);
                }
            }
            return result;
        }

        public static List<int> FilterPlannedTilesFromPath(List<int> nodesDestFirst, int originTile, bool clearing)
        {
            return FilterPlannedTilesFromPath(nodesDestFirst, originTile, clearing, RoadBlockKind.Normal);
        }

        public static void ClearRoadBlockProject(CompViralSpread comp)
        {
            if (comp == null) return;
            DestroyActiveRoadBlockCrewsFrom(comp.parent);
            comp.roadBlockPlannedTiles?.Clear();
            comp.roadBlockClickedNodes?.Clear();
            comp.roadBlockCachedPathTiles?.Clear();
            comp.roadBlockProgress = 0f;
            comp.roadBlockWorkIndex = 0;
            comp.roadBlockCachedWorkTile = -1;
            comp.roadBlockIsClearing = false;
            comp.roadBlockClearAnyFortification = false;
            comp.roadBlockTargetName = string.Empty;
            comp.selectedRoadBlockKind = RoadBlockKind.Normal;
            comp.NotifyRoadBlockCrewReturned();
        }

        public static void DestroyActiveRoadBlockCrewsFrom(WorldObject origin)
        {
            if (origin == null || Find.WorldObjects == null) return;
            var allWo = Find.WorldObjects.AllWorldObjects;
            for (int wi = allWo.Count - 1; wi >= 0; wi--)
            {
                if (allWo[wi] is WorldObject_Traveler t
                    && t.mission == TravelerMission.RoadBlock
                    && t.originObject == origin
                    && !t.Destroyed)
                {
                    t.Destroy();
                }
            }
        }

        public static bool HasActiveRoadBlockCrewFrom(WorldObject origin)
        {
            if (origin == null || Find.WorldObjects == null) return false;
            foreach (var wo in Find.WorldObjects.AllWorldObjects)
            {
                if (wo is WorldObject_Traveler t && !t.Destroyed
                    && t.mission == TravelerMission.RoadBlock
                    && t.originObject == origin)
                    return true;
            }
            return false;
        }

        public static int GetCurrentWorkTile(CompViralSpread comp)
        {
            if (comp?.roadBlockPlannedTiles == null) return -1;
            RoadBlockKind kind = comp.selectedRoadBlockKind;
            Faction builder = comp.parent?.Faction;
            while (comp.roadBlockWorkIndex < comp.roadBlockPlannedTiles.Count)
            {
                int tile = comp.roadBlockPlannedTiles[comp.roadBlockWorkIndex];
                if (comp.roadBlockIsClearing)
                {
                    if (comp.roadBlockClearAnyFortification
                        ? WorldActions_Fortifications.HasFortificationAt(tile)
                        : IsValidClearTile(tile))
                        return tile;
                }
                else if (IsValidBuildTile(tile, kind, builder))
                {
                    return tile;
                }
                // Already done / invalid — skip ahead (arrival no-op path).
                comp.roadBlockWorkIndex++;
            }
            return -1;
        }

        /// <summary>True if a crew was spawned. False if project finished or launch failed.</summary>
        public static bool LaunchRoadBlockCrewFromOutpost(WorldObject actor)
        {
            var comp = actor?.GetComponent<CompViralSpread>();
            if (!HasActiveRoadBlockProject(comp)) return false;

            int workTile = GetCurrentWorkTile(comp);
            comp.roadBlockCachedWorkTile = workTile;
            if (workTile < 0)
            {
                ClearRoadBlockProject(comp);
                return false;
            }

            if (!ColonyWorldBuildRequirements.MeetsRoadBlockRequirements(actor, comp.selectedRoadBlockKind))
                return false;

            float cost = GetExpeditionStrengthCost(comp.selectedRoadBlockKind);
            if (!WorldActions_Utils.CanAffordExpeditionLeavingGarrison(comp, cost)) return false;

            return SpawnRoadBlockTraveler(actor, workTile, cost);
        }

        private static bool SpawnRoadBlockTraveler(WorldObject origin, int destTile, float cost)
        {
            var comp = origin.GetComponent<CompViralSpread>();
            if (comp == null) return false;
            if (!WorldActions_Utils.TryConsumeExpeditionStrength(comp, cost)) return false;

            WorldObjectDef def = DefDatabase<WorldObjectDef>.GetNamed("TSA_WD_Traveler_Outpost_RoadBlock", false)
                ?? DefDatabase<WorldObjectDef>.GetNamed("TSA_WD_Traveler_Outpost_RoadBuilder", false);
            if (def == null)
            {
                WorldActions_Utils.RefundExpeditionStrength(comp, cost);
                return false;
            }

            WorldObject_Traveler traveler = (WorldObject_Traveler)WorldObjectMaker.MakeWorldObject(def);
            traveler.Tile = origin.Tile;
            traveler.SetFaction(origin.Faction);
            traveler.mission = TravelerMission.RoadBlock;
            traveler.originObject = origin;
            traveler.travelerStrength = cost;
            traveler.initialStrength = cost;

            Find.WorldObjects.Add(traveler);
            traveler.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(destTile, origin));
            if (traveler.Destroyed)
            {
                WorldActions_Utils.RefundExpeditionStrength(comp, cost);
                return false;
            }
            return true;
        }

        public static void ExecuteRoadBlockArrival(WorldObject_Traveler traveler)
        {
            if (traveler == null) return;
            WorldObject origin = traveler.originObject;
            var comp = origin?.GetComponent<CompViralSpread>();
            if (comp == null || !HasActiveRoadBlockProject(comp)) return;

            int tile = traveler.Tile.tileId;
            if (comp.roadBlockIsClearing)
            {
                if (comp.roadBlockClearAnyFortification)
                {
                    if (WorldActions_Fortifications.HasFortificationAt(tile))
                        WorldActions_Fortifications.TryClearAt(tile);
                }
                else if (IsValidClearTile(tile))
                {
                    WorldComponent_RoadBlocks.Get()?.TryClear(tile);
                }
            }
            else
            {
                Faction builder = traveler.Faction ?? origin.Faction;
                if (IsValidBuildTile(tile, comp.selectedRoadBlockKind, builder))
                    WorldComponent_RoadBlocks.Get()?.TryPlaceOrUpgrade(tile, builder, comp.selectedRoadBlockKind);
            }

            // Advance past the tile we just worked (or no-op'd).
            if (comp.roadBlockWorkIndex < comp.roadBlockPlannedTiles.Count
                && comp.roadBlockPlannedTiles[comp.roadBlockWorkIndex] == tile)
            {
                comp.roadBlockWorkIndex++;
            }
            else
            {
                int idx = comp.roadBlockPlannedTiles.IndexOf(tile);
                if (idx >= 0)
                    comp.roadBlockWorkIndex = idx + 1;
            }

            int next = GetCurrentWorkTile(comp);
            comp.roadBlockCachedWorkTile = next;
            if (next < 0)
                ClearRoadBlockProject(comp);
            else
            {
                // Force selection-overlay path/markers to rebuild (recovers empty/broken draw caches mid-project).
                comp.roadBlockCachedPathTiles?.Clear();
            }
        }
    }
}
