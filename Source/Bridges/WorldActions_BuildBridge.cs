using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public static class WorldActions_BuildBridge
    {
        private static readonly List<int> WaterScratch = new List<int>(16);
        private static readonly HashSet<int> AnchoredBankScratch = new HashSet<int>(32);
        private static readonly HashSet<int> NpcReservedScratch = new HashSet<int>(32);
        private static readonly HashSet<int> NpcStartBanksScratch = new HashSet<int>(16);
        private static readonly HashSet<int> NpcFarBanksScratch = new HashSet<int>(16);
        private static readonly Dictionary<int, List<int>> NpcChainsScratch = new Dictionary<int, List<int>>(16);
        private static readonly List<int> NpcStartBankListScratch = new List<int>(16);

        public const SettlementTier BridgeRoadTier = SettlementTier.T2;
        /// <summary>Max land hops from actor/target to their bridge bank for NPC hybrid corridors.</summary>
        public const int NpcBridgeBankMaxLandHops = 4;
        private const int NpcBridgeMaxStartBanks = 8;

        /// <summary>Painted onto water spans (label Stone Bridge; same movement as stone road).</summary>
        public static RoadDef GetBridgeRoadDef() =>
            DefDatabase<RoadDef>.GetNamedSilentFail("TSA_WD_StoneBridge")
            ?? DefDatabase<RoadDef>.GetNamedSilentFail("StoneRoad")
            ?? WorldActions_Roads.GetRoadDefByTier(BridgeRoadTier);

        /// <summary>Existing complete-bridge anchors plus in-flight build project banks (not deconstruct jobs).</summary>
        public static void CollectReservedBankTiles(HashSet<int> into, CompViralSpread exclude = null)
        {
            into?.Clear();
            if (into == null) return;
            WorldComponent_WdBridges bridges = WorldComponent_WdBridges.Get();
            bridges?.CopyExclusiveCompleteBanksInto(into);

            IReadOnlyList<CompViralSpread> projects = WorldConstructionProjectRegistry.ActiveBridgeProjects;
            for (int i = 0; i < projects.Count; i++)
            {
                CompViralSpread c = projects[i];
                if (c == null || c == exclude || c.parent == null || c.parent.Destroyed) continue;
                if (!HasActiveBridgeProject(c) || c.bridgeIsClearing) continue;
                List<int> other = c.bridgeSpanTiles;
                if (other == null || other.Count < 2) continue;
                into.Add(other[0]);
                into.Add(other[other.Count - 1]);
            }
        }

        /// <summary>Banks of existing bridges within ApproxDistance of origin.</summary>
        public static void CollectDeconstructBanksInRange(
            int originTileId, float maxApproxDistance, HashSet<int> into)
        {
            into?.Clear();
            if (into == null || originTileId < 0) return;
            WorldGrid grid = Find.WorldGrid;
            PlanetLayer layer = PlanetSurfaceWorldActions.WdSurfaceLayer ?? grid?.Surface;
            WorldComponent_WdBridges bridges = WorldComponent_WdBridges.Get();
            if (grid == null || layer == null || bridges == null || !bridges.HasAnyBridges) return;

            PlanetTile origin = new PlanetTile(originTileId, layer);
            WdBridgeGeometry.CollectAnchoredBanksFromBridgedWaters(AnchoredBankScratch);
            foreach (int bank in AnchoredBankScratch)
            {
                if (bank < 0 || !grid.InBounds(bank) || grid[bank].WaterCovered) continue;
                if (grid.ApproxDistanceInTiles(origin, new PlanetTile(bank, layer)) <= maxApproxDistance)
                    into.Add(bank);
            }
        }

        public static bool HasActiveBridgeProject(CompViralSpread comp) =>
            comp != null && comp.bridgeSpanTiles != null && comp.bridgeSpanTiles.Count >= 2;

        public static bool IsDeconstructProject(CompViralSpread comp) =>
            HasActiveBridgeProject(comp) && comp.bridgeIsClearing;

        public static void ClearBridgeProject(CompViralSpread comp, bool destroyCrews = true)
        {
            if (comp == null) return;
            if (destroyCrews)
                DestroyActiveBridgeCrewsFrom(comp.parent);
            comp.bridgeSpanTiles?.Clear();
            comp.bridgeTargetName = string.Empty;
            comp.bridgeIsClearing = false;
            comp.bridgeProgress = 0f;
            comp.cachedWorkTile = -1;
            comp.NotifyBridgeCrewReturned();
            WorldConstructionProjectRegistry.NotifyBridgeChanged(comp);
        }

        public static void DestroyActiveBridgeCrewsFrom(WorldObject origin)
        {
            if (origin == null) return;
            IReadOnlyList<WorldObject_Traveler> live = WorldObject_Traveler.LiveTravelers;
            for (int wi = live.Count - 1; wi >= 0; wi--)
            {
                WorldObject_Traveler t = live[wi];
                if (t != null
                    && (t.mission == TravelerMission.BridgeBuilding || t.mission == TravelerMission.BridgeDeconstruct)
                    && t.originObject == origin
                    && !t.Destroyed)
                {
                    if (t.mission == TravelerMission.BridgeBuilding)
                        ColonyWorldBuildRequirements.RefundConstructionAbort(t);
                    else
                        TravelerEndpointUtility.RefundTravelerStrength(t, 1f);
                    t.Destroy();
                }
            }
        }

        public static bool BeginBridgeProject(CompViralSpread comp, List<int> fullChain, bool deconstruct)
        {
            if (comp == null || fullChain == null || fullChain.Count < 2) return false;
            var chain = new List<int>(fullChain);
            if (!deconstruct)
                WdBridgeGeometry.OrientChainGrowFromPaintedBank(chain);
            else
                OrientChainNearBuilder(chain, comp.parent != null ? comp.parent.Tile.tileId : -1);
            if (!TryGetNextWaterWork(chain, deconstruct, -1, out _, out _))
                return false;

            comp.bridgeSpanTiles = chain;
            comp.bridgeIsClearing = deconstruct;
            comp.bridgeProgress = 0f;
            comp.bridgeBuilderInField = false;
            comp.selectedRoadTier = BridgeRoadTier;
            comp.bridgeTargetName = "Tile " + chain[chain.Count - 1];
            RefreshCachedWorkTile(comp);
            WorldConstructionProjectRegistry.NotifyBridgeChanged(comp);
            return true;
        }

        public static void RefreshCachedWorkTile(CompViralSpread comp)
        {
            if (comp == null || !HasActiveBridgeProject(comp))
            {
                if (comp != null) comp.cachedWorkTile = -1;
                return;
            }
            int builder = comp.parent != null ? comp.parent.Tile.tileId : -1;
            if (!TryGetNextWaterWork(comp.bridgeSpanTiles, comp.bridgeIsClearing, builder, out _, out int workTile))
            {
                comp.cachedWorkTile = -1;
                return;
            }
            comp.cachedWorkTile = workTile;
        }

        /// <summary>Near-outpost bank at index 0 so deconstruct peels from the far end.</summary>
        public static void OrientChainNearBuilder(List<int> chain, int builderTile)
        {
            if (chain == null || chain.Count < 2 || builderTile < 0) return;
            WorldGrid grid = Find.WorldGrid;
            PlanetLayer layer = PlanetSurfaceWorldActions.WdSurfaceLayer ?? grid?.Surface;
            if (grid == null || layer == null) return;
            if (!grid.InBounds(chain[0]) || !grid.InBounds(chain[chain.Count - 1])) return;
            float d0 = grid.ApproxDistanceInTiles(
                new PlanetTile(builderTile, layer), new PlanetTile(chain[0], layer));
            float dFar = grid.ApproxDistanceInTiles(
                new PlanetTile(builderTile, layer), new PlanetTile(chain[chain.Count - 1], layer));
            if (dFar < d0)
                chain.Reverse();
        }

        public static bool TryGetFirstUnfinishedEdge(List<int> chain, bool clearing, out int fromTile, out int toTile)
        {
            fromTile = -1;
            toTile = -1;
            if (!TryGetNextWaterIndex(chain, clearing, out int waterIndex))
                return false;
            GetWaterSegmentEdges(chain, waterIndex, out fromTile, out toTile, out int from2, out int to2);
            if (fromTile >= 0 && toTile >= 0)
                return true;
            if (from2 >= 0 && to2 >= 0)
            {
                fromTile = from2;
                toTile = to2;
                return true;
            }
            return false;
        }

        public static int CountUnfinishedEdges(List<int> chain, bool clearing)
        {
            int n = 0;
            if (chain == null || chain.Count < 2) return 0;
            for (int i = 1; i < chain.Count; i++)
            {
                if (IsChainWaterIndex(chain, i) && WaterSegmentNeedsWork(chain, i, clearing))
                    n++;
            }
            return n;
        }

        public static bool TryGetNextWaterWork(
            List<int> chain, bool clearing, int builderTile, out int waterIndex, out int workTile)
        {
            workTile = -1;
            if (!TryGetNextWaterIndex(chain, clearing, out waterIndex))
                return false;
            workTile = GetWaterSegmentWorkTile(chain, waterIndex, clearing, builderTile);
            return workTile >= 0 || clearing;
        }

        public static bool TryGetNextWaterIndex(List<int> chain, bool clearing, out int waterIndex)
        {
            waterIndex = -1;
            if (chain == null || chain.Count < 2) return false;
            if (clearing)
            {
                for (int i = chain.Count - 1; i >= 1; i--)
                {
                    if (!IsChainWaterIndex(chain, i)) continue;
                    if (!WaterSegmentNeedsWork(chain, i, clearing: true)) continue;
                    waterIndex = i;
                    return true;
                }
            }
            else
            {
                for (int i = 1; i < chain.Count; i++)
                {
                    if (!IsChainWaterIndex(chain, i)) continue;
                    if (!WaterSegmentNeedsWork(chain, i, clearing: false)) continue;
                    waterIndex = i;
                    return true;
                }
            }
            return false;
        }

        public static bool IsChainWaterIndex(List<int> chain, int index)
        {
            if (chain == null || index < 0 || index >= chain.Count) return false;
            WorldGrid grid = Find.WorldGrid;
            int id = chain[index];
            return grid != null && grid.InBounds(id) && grid[id].WaterCovered;
        }

        public static bool WaterSegmentNeedsWork(List<int> chain, int waterIndex, bool clearing)
        {
            GetWaterSegmentEdges(chain, waterIndex, out int a, out int b, out int c, out int d);
            if (a >= 0 && b >= 0 && EdgeNeedsWork(a, b, clearing)) return true;
            if (c >= 0 && d >= 0 && EdgeNeedsWork(c, d, clearing)) return true;
            return false;
        }

        private static bool EdgeNeedsWork(int from, int to, bool clearing)
        {
            if (clearing)
                return WorldActions_Roads.HasRoadLink(from, to);
            PlanetLayer layer = PlanetSurfaceWorldActions.WdSurfaceLayer;
            RoadDef planned = GetBridgeRoadDef();
            return planned != null && layer != null
                && WorldActions_Roads.ShouldUpgradeRoad(new PlanetTile(from, layer), new PlanetTile(to, layer), planned);
        }

        /// <summary>
        /// One stone-road-duration unit = one water tile: the back edge toward the near bank,
        /// plus the far-bank edge when this water is next to land.
        /// </summary>
        public static void GetWaterSegmentEdges(
            List<int> chain, int waterIndex, out int from1, out int to1, out int from2, out int to2)
        {
            from1 = to1 = from2 = to2 = -1;
            if (chain == null || waterIndex < 1 || waterIndex >= chain.Count) return;
            from1 = chain[waterIndex - 1];
            to1 = chain[waterIndex];
            if (waterIndex + 1 < chain.Count && !IsChainWaterIndex(chain, waterIndex + 1))
            {
                from2 = chain[waterIndex];
                to2 = chain[waterIndex + 1];
            }
        }

        public static int GetWaterSegmentWorkTile(List<int> chain, int waterIndex, bool clearing, int builderTile)
        {
            if (chain == null || waterIndex < 0 || waterIndex >= chain.Count) return -1;
            if (clearing)
            {
                int far = waterIndex + 1 < chain.Count ? chain[waterIndex + 1] : chain[waterIndex];
                if (IsPassableForBridgeCrew(far, allowUnbridgedWater: true) && far != builderTile)
                    return far;
                if (IsPassableForBridgeCrew(chain[waterIndex], allowUnbridgedWater: true))
                    return chain[waterIndex];
                return far;
            }

            GetWaterSegmentEdges(chain, waterIndex, out int a, out int b, out _, out _);
            return GetPassableWorkTile(a, b, builderTile);
        }

        public static void CollectUnfinishedDrawTiles(List<int> chain, bool clearing, List<int> into)
        {
            into?.Clear();
            if (into == null || chain == null || chain.Count < 2) return;
            PlanetLayer layer = PlanetSurfaceWorldActions.WdSurfaceLayer;
            RoadDef planned = GetBridgeRoadDef();
            for (int i = 0; i < chain.Count - 1; i++)
            {
                int from = chain[i];
                int to = chain[i + 1];
                bool unfinished;
                if (clearing)
                    unfinished = WorldActions_Roads.HasRoadLink(from, to);
                else
                    unfinished = planned != null && layer != null
                        && WorldActions_Roads.ShouldUpgradeRoad(new PlanetTile(from, layer), new PlanetTile(to, layer), planned);
                if (!unfinished) continue;
                if (into.Count == 0 || into[into.Count - 1] != from)
                    into.Add(from);
                into.Add(to);
            }
        }

        private static bool IsLegacyFullSpanCrew(WorldObject_Traveler traveler, List<int> span)
        {
            if (traveler == null || span == null || span.Count < 3) return false;
            List<int> cached = traveler.cachedPathTiles;
            if (cached == null || cached.Count != span.Count) return false;
            for (int i = 0; i < span.Count; i++)
            {
                if (cached[i] != span[i]) return false;
            }
            int dest = traveler.pather != null && traveler.pather.destTile.Valid
                ? traveler.pather.destTile.tileId
                : traveler.Tile.tileId;
            return dest == span[0] && CountUnfinishedEdges(span, traveler.mission == TravelerMission.BridgeDeconstruct) > 1;
        }

        private static List<int> BuildTravelTilesDestFirst(int originTile, List<int> chain, int workTile)
        {
            var forward = new List<int>(16);
            if (originTile >= 0)
                forward.Add(originTile);

            PlanetLayer layer = PlanetSurfaceWorldActions.WdSurfaceLayer;
            int approach = -1;
            if (chain != null && chain.Count > 0)
                approach = chain[0];

            if (layer != null && approach >= 0 && originTile >= 0 && originTile != approach)
            {
                using (WorldPath path = layer.Pather.FindPath(
                    new PlanetTile(originTile, layer), new PlanetTile(approach, layer), null))
                {
                    if (path != null && path.Found)
                    {
                        var nodes = path.NodesReversed;
                        for (int i = nodes.Count - 1; i >= 0; i--)
                        {
                            int id = nodes[i].tileId;
                            if (forward.Count > 0 && forward[forward.Count - 1] == id)
                                continue;
                            forward.Add(id);
                        }
                    }
                }
            }

            if (chain != null && workTile >= 0)
            {
                int workIdx = -1;
                for (int i = 0; i < chain.Count; i++)
                {
                    if (chain[i] == workTile)
                        workIdx = i;
                }
                if (workIdx < 0)
                    workIdx = chain.Count - 1;
                int fromIdx = 0;
                int toIdx = workIdx;
                if (fromIdx > toIdx)
                {
                    int tmp = fromIdx;
                    fromIdx = toIdx;
                    toIdx = tmp;
                }
                for (int i = fromIdx; i <= toIdx; i++)
                {
                    int id = chain[i];
                    if (forward.Count > 0 && forward[forward.Count - 1] == id)
                        continue;
                    forward.Add(id);
                }
            }

            if (workTile >= 0 && (forward.Count == 0 || forward[forward.Count - 1] != workTile))
                forward.Add(workTile);

            var destFirst = new List<int>(forward.Count);
            for (int i = forward.Count - 1; i >= 0; i--)
                destFirst.Add(forward[i]);
            return destFirst;
        }

        public static int GetPassableWorkTile(int fromTile, int toTile, int builderTile)
        {
            if (IsPassableForBridgeCrew(fromTile) && fromTile != builderTile)
                return fromTile;
            if (IsPassableForBridgeCrew(toTile) && toTile != builderTile)
                return toTile;
            if (IsPassableForBridgeCrew(fromTile))
                return fromTile;
            if (IsPassableForBridgeCrew(toTile))
                return toTile;
            return -1;
        }

        public static bool IsPassableForBridgeCrew(int tileId, bool allowUnbridgedWater = false)
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || tileId < 0 || !grid.InBounds(tileId)) return false;
            PlanetLayer layer = PlanetSurfaceWorldActions.WdSurfaceLayer ?? grid.Surface;
            PlanetTile pt = new PlanetTile(tileId, layer);
            if (!grid[tileId].WaterCovered)
                return !Find.World.Impassable(pt);
            if (allowUnbridgedWater)
                return true;
            return WorldComponent_WdBridges.IsBridgedWaterTile(tileId);
        }

        public static bool HasActiveBridgeCrewFrom(WorldObject origin)
        {
            if (origin == null) return false;
            IReadOnlyList<WorldObject_Traveler> live = WorldObject_Traveler.LiveTravelers;
            for (int i = 0; i < live.Count; i++)
            {
                WorldObject_Traveler t = live[i];
                if (t != null
                    && (t.mission == TravelerMission.BridgeBuilding || t.mission == TravelerMission.BridgeDeconstruct)
                    && t.originObject == origin
                    && !t.Destroyed)
                    return true;
            }
            return false;
        }

        public static bool LaunchBridgeCrewFromOutpost(WorldObject actor, List<int> fullChain, bool deconstruct) =>
            LaunchBridgeSegmentCrew(actor);

        /// <summary>
        /// NPC hybrid: find a short opposite-bank span with both banks within
        /// <see cref="NpcBridgeBankMaxLandHops"/> land hops of actor and target.
        /// </summary>
        public static bool TryFindNpcBridgeSpan(
            int actorTileId, int targetTileId, CompViralSpread exclude, out List<int> chain)
        {
            chain = null;
            if (actorTileId < 0 || targetTileId < 0) return false;
            WorldGrid grid = Find.WorldGrid;
            PlanetLayer layer = PlanetSurfaceWorldActions.WdSurfaceLayer ?? grid?.Surface;
            if (grid == null || layer == null) return false;
            if (!grid.InBounds(actorTileId) || !grid.InBounds(targetTileId)) return false;

            CollectReservedBankTiles(NpcReservedScratch, exclude);
            WdBridgeGeometry.CollectLandBanks(
                actorTileId,
                NpcBridgeBankMaxLandHops,
                maxApproxDistance: -1f,
                NpcReservedScratch,
                NpcStartBanksScratch,
                excludeTileId: -1,
                traverseWater: false);
            if (NpcStartBanksScratch.Count == 0) return false;

            NpcStartBankListScratch.Clear();
            NpcStartBankListScratch.AddRange(NpcStartBanksScratch);
            PlanetTile actorPt = new PlanetTile(actorTileId, layer);
            NpcStartBankListScratch.Sort((a, b) =>
            {
                float da = grid.ApproxDistanceInTiles(actorPt, new PlanetTile(a, layer));
                float db = grid.ApproxDistanceInTiles(actorPt, new PlanetTile(b, layer));
                int c = da.CompareTo(db);
                return c != 0 ? c : a.CompareTo(b);
            });
            if (NpcStartBankListScratch.Count > NpcBridgeMaxStartBanks)
                NpcStartBankListScratch.RemoveRange(
                    NpcBridgeMaxStartBanks, NpcStartBankListScratch.Count - NpcBridgeMaxStartBanks);

            List<int> best = null;
            int bestWater = int.MaxValue;
            for (int i = 0; i < NpcStartBankListScratch.Count; i++)
            {
                int start = NpcStartBankListScratch[i];
                WdBridgeGeometry.CollectFarBanksWithChains(
                    start, NpcReservedScratch, NpcFarBanksScratch, NpcChainsScratch);
                foreach (KeyValuePair<int, List<int>> kv in NpcChainsScratch)
                {
                    int end = kv.Key;
                    List<int> candidate = kv.Value;
                    if (candidate == null || candidate.Count < 3) continue;
                    if (!WdBridgeGeometry.IsWithinLandHopRange(targetTileId, end, NpcBridgeBankMaxLandHops))
                        continue;
                    if (IsSpanProjectLocked(candidate, exclude)) continue;
                    if (WdBridgeGeometry.SpanIsFullyBridged(candidate)) continue;
                    int waterCount = candidate.Count - 2;
                    if (waterCount < 1 || waterCount >= bestWater) continue;
                    bestWater = waterCount;
                    best = candidate;
                }
            }

            if (best == null) return false;
            chain = new List<int>(best);
            OrientChainNearBuilder(chain, actorTileId);
            return chain.Count >= 3;
        }

        /// <summary>NPC hybrid kickoff: begin project (if needed) and launch one segment crew, skipping player gates.</summary>
        public static bool LaunchNpcBridgeKickoff(WorldObject actor, List<int> plannedSpan)
        {
            var comp = actor?.GetComponent<CompViralSpread>();
            if (comp == null || plannedSpan == null || plannedSpan.Count < 2) return false;

            if (!HasActiveBridgeProject(comp))
            {
                var chain = new List<int>(plannedSpan);
                OrientChainNearBuilder(chain, actor.Tile.tileId);
                if (!BeginBridgeProject(comp, chain, deconstruct: false))
                    return false;
            }

            return LaunchBridgeSegmentCrew(actor);
        }

        public static bool LaunchBridgeSegmentCrew(WorldObject actor)
        {
            var comp = actor?.GetComponent<CompViralSpread>();
            if (comp == null || !HasActiveBridgeProject(comp)) return false;
            List<int> span = comp.bridgeSpanTiles;
            bool deconstruct = comp.bridgeIsClearing;

            if (IsSpanProjectLocked(span, exclude: comp))
                return false;

            int builderTile = actor.Tile.tileId;
            if (!TryGetNextWaterWork(span, deconstruct, builderTile, out int waterIndex, out int workTile))
            {
                ClearBridgeProject(comp, destroyCrews: false);
                return false;
            }
            if (workTile < 0)
                workTile = span[waterIndex];
            comp.cachedWorkTile = workTile;

            // NPC settlements skip skill / research / material gates (hybrid road action).
            bool npcSettlementBridge =
                actor is Settlement
                && actor.Faction != null
                && !actor.Faction.IsPlayer;
            if (!deconstruct && !npcSettlementBridge)
            {
                if (!ColonyWorldBuildRequirements.MeetsRoadRequirements(actor, BridgeRoadTier))
                    return false;
                if (ColonyWorldBuildRequirements.ActorPaysWorldBuildMaterials(actor)
                    && !HasMaterialCostsForBridge(1))
                    return false;
            }

            float cost = WorldActions_Roads.GetExpeditionStrengthCost(BridgeRoadTier);
            if (!WorldActions_Utils.CanAffordExpeditionLeavingGarrison(comp, cost))
                return false;

            WorldObjectDef def = DefDatabase<WorldObjectDef>.GetNamed("TSA_WD_Traveler_Outpost_RoadBuilder", false);
            if (def == null) return false;

            if (!WorldActions_Utils.TryConsumeExpeditionStrength(comp, cost))
                return false;

            WorldObject_Traveler traveler = (WorldObject_Traveler)WorldObjectMaker.MakeWorldObject(def);
            traveler.Tile = actor.Tile;
            traveler.SetFaction(actor.Faction);
            traveler.mission = deconstruct ? TravelerMission.BridgeDeconstruct : TravelerMission.BridgeBuilding;
            traveler.originObject = actor;
            traveler.travelerStrength = cost;
            traveler.initialStrength = cost;
            traveler.cachedPathTiles = BuildTravelTilesDestFirst(builderTile, span, workTile);

            Find.WorldObjects.Add(traveler);
            bool sameTile = workTile == builderTile;
            traveler.pather.StartPath(
                PlanetSurfaceWorldActions.PlanetTileForWdTravel(workTile, actor),
                skipLaunchTravelCache: sameTile);
            if (traveler.Destroyed)
            {
                WorldActions_Utils.RefundExpeditionStrength(comp, cost);
                return false;
            }

            if (!deconstruct
                && ColonyWorldBuildRequirements.ActorPaysWorldBuildMaterials(actor)
                && !ColonyWorldBuildRequirements.TryFinalizeMaterialsOrAbort(
                    actor, traveler, cost, GetBridgeMaterialCosts(1)))
            {
                return false;
            }

            // Same as roads: no launch toast (segment-complete / project-set messages only).
            comp.bridgeBuilderInField = true;
            return true;
        }

        public static void ExecuteBridgeArrival(WorldObject_Traveler traveler)
        {
            if (traveler == null) return;
            WorldObject origin = traveler.originObject;
            var comp = origin?.GetComponent<CompViralSpread>();
            List<int> span = comp?.bridgeSpanTiles;
            RoadDef road = GetBridgeRoadDef() ?? WorldActions_Roads.GetRoadDefForActor(origin);

            if (IsLegacyFullSpanCrew(traveler, span))
            {
                PaintEntireSpan(span, road);
                traveler.MarkConstructionWorkApplied();
                Outpost_ConstructionXp.TryGrant(traveler, Outpost_ConstructionXp.XpForRoadTier(BridgeRoadTier));
                TryNotifyBridgeMessage(origin, "TSA_WD_BridgeBuilt", MessageTypeDefOf.TaskCompletion);
                ClearBridgeProject(comp, destroyCrews: false);
                return;
            }

            if (span == null || !TryGetNextWaterIndex(span, false, out int waterIndex) || road == null)
            {
                RefreshCachedWorkTile(comp);
                return;
            }

            PaintWaterSegment(span, waterIndex, road);
            traveler.MarkConstructionWorkApplied();
            Outpost_ConstructionXp.TryGrant(traveler, Outpost_ConstructionXp.XpForRoadTier(BridgeRoadTier));

            if (comp != null && !TryGetNextWaterIndex(comp.bridgeSpanTiles, false, out _))
            {
                TryNotifyBridgeMessage(origin, "TSA_WD_BridgeBuilt", MessageTypeDefOf.TaskCompletion);
                ClearBridgeProject(comp, destroyCrews: false);
            }
            else
            {
                TryNotifyBridgeMessage(origin, "TSA_WD_BridgeSegmentComplete", MessageTypeDefOf.PositiveEvent);
                RefreshCachedWorkTile(comp);
            }
        }

        private static void TryNotifyBridgeMessage(WorldObject origin, string key, MessageTypeDef type)
        {
            if (origin?.Faction == null || !origin.Faction.IsPlayer) return;
            Messages.Message(key.Translate(origin.LabelCap ?? ""), type, false);
        }

        public static void ExecuteBridgeDeconstructArrival(WorldObject_Traveler traveler)
        {
            if (traveler == null) return;
            WorldObject origin = traveler.originObject;
            var comp = origin?.GetComponent<CompViralSpread>();
            List<int> span = comp?.bridgeSpanTiles;

            if (IsLegacyFullSpanCrew(traveler, span))
            {
                if (TryGetSpanOccupancyBlocker(span, out string blockerLabel, out int blockerTile, except: traveler))
                {
                    SendOccupiedArrivalLetter(origin, blockerLabel, blockerTile, span);
                    TravelerEndpointUtility.RefundTravelerStrength(traveler, 1f);
                    return;
                }
                WaterScratch.Clear();
                WdBridgeGeometry.CollectWaterTiles(span, WaterScratch);
                for (int i = 0; i < WaterScratch.Count; i++)
                    WorldActions_Fortifications.TryClearAt(WaterScratch[i]);
                UnlinkEntireSpan(span);
                WorldComponent_WdBridges bridges = WorldComponent_WdBridges.Get();
                for (int i = 0; i < WaterScratch.Count; i++)
                    bridges?.RefreshTileAfterRoadChange(WaterScratch[i]);
                bridges?.RecalculatePathCostsForTiles(WaterScratch);
                Find.WorldReachability?.ClearCache();
                WD_WorldLayer_MovementDifficultyOverlay.InvalidateAndDirtyIfActive();
                WdBridgeTravelerImpact.NotifySpanRemoved(WaterScratch, except: traveler);
                Messages.Message(
                    "TSA_WD_BridgeDeconstructed".Translate(origin?.LabelCap ?? ""),
                    MessageTypeDefOf.TaskCompletion,
                    false);
                ClearBridgeProject(comp, destroyCrews: false);
                return;
            }

            if (span == null || !TryGetNextWaterIndex(span, true, out int waterIndex))
            {
                RefreshCachedWorkTile(comp);
                return;
            }

            var occChain = new List<int>(1) { span[waterIndex] };
            if (TryGetSpanOccupancyBlocker(occChain, out string occLabel, out int occTile, except: traveler))
            {
                SendOccupiedArrivalLetter(origin, occLabel, occTile, occChain);
                TravelerEndpointUtility.RefundTravelerStrength(traveler, 1f);
                return;
            }

            UnlinkWaterSegment(span, waterIndex, traveler);
            traveler.MarkConstructionWorkApplied();

            if (comp != null && !TryGetNextWaterIndex(comp.bridgeSpanTiles, true, out _))
            {
                Messages.Message(
                    "TSA_WD_BridgeDeconstructed".Translate(origin?.LabelCap ?? ""),
                    MessageTypeDefOf.TaskCompletion,
                    false);
                ClearBridgeProject(comp, destroyCrews: false);
            }
            else
            {
                Messages.Message(
                    "TSA_WD_BridgeDeconstructSegmentComplete".Translate(origin?.LabelCap ?? ""),
                    MessageTypeDefOf.PositiveEvent,
                    false);
                RefreshCachedWorkTile(comp);
            }
        }

        public static void PaintWaterSegment(List<int> chain, int waterIndex, RoadDef road)
        {
            GetWaterSegmentEdges(chain, waterIndex, out int a, out int b, out int c, out int d);
            if (a >= 0 && b >= 0 && EdgeNeedsWork(a, b, clearing: false))
                PaintOneEdge(a, b, road);
            if (c >= 0 && d >= 0 && EdgeNeedsWork(c, d, clearing: false))
                PaintOneEdge(c, d, road);
        }

        public static void UnlinkWaterSegment(List<int> chain, int waterIndex, WorldObject_Traveler exceptTraveler)
        {
            GetWaterSegmentEdges(chain, waterIndex, out int a, out int b, out int c, out int d);
            if (c >= 0 && d >= 0 && EdgeNeedsWork(c, d, clearing: true))
                UnlinkOneEdge(c, d, exceptTraveler);
            if (a >= 0 && b >= 0 && EdgeNeedsWork(a, b, clearing: true))
                UnlinkOneEdge(a, b, exceptTraveler);
        }

        public static void PaintOneEdge(int fromTile, int toTile, RoadDef road)
        {
            if (road == null) return;
            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return;
            PlanetLayer layer = PlanetSurfaceWorldActions.WdSurfaceLayer ?? grid.Surface;
            bool fromLand = grid.InBounds(fromTile) && !grid[fromTile].WaterCovered;
            bool toLand = grid.InBounds(toTile) && !grid[toTile].WaterCovered;
            WorldActions_Roads.ApplyRoadLink(
                new PlanetTile(fromTile, layer),
                new PlanetTile(toTile, layer),
                road,
                clearFortsOnA: !fromLand,
                clearFortsOnB: !toLand);
            NotifyRoadLinkChanged(fromTile, toTile);
            WD_WorldLayer_MovementDifficultyOverlay.InvalidateAndDirtyIfActive();
        }

        public static void UnlinkOneEdge(int fromTile, int toTile, WorldObject_Traveler exceptTraveler = null)
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return;
            PlanetLayer layer = PlanetSurfaceWorldActions.WdSurfaceLayer ?? grid.Surface;
            WorldActions_Roads.RemoveRoadLink(new PlanetTile(fromTile, layer), new PlanetTile(toTile, layer));
            WaterScratch.Clear();
            if (grid.InBounds(fromTile) && grid[fromTile].WaterCovered)
            {
                WorldActions_Fortifications.TryClearAt(fromTile);
                WaterScratch.Add(fromTile);
            }
            if (grid.InBounds(toTile) && grid[toTile].WaterCovered)
            {
                WorldActions_Fortifications.TryClearAt(toTile);
                WaterScratch.Add(toTile);
            }
            NotifyRoadLinkChanged(fromTile, toTile);
            Find.WorldReachability?.ClearCache();
            WD_WorldLayer_MovementDifficultyOverlay.InvalidateAndDirtyIfActive();
            if (WaterScratch.Count > 0)
                WdBridgeTravelerImpact.NotifySpanRemoved(WaterScratch, except: exceptTraveler);
        }

        public static void PaintEntireSpan(List<int> fullChain, RoadDef road)
        {
            if (fullChain == null || fullChain.Count < 3 || road == null) return;
            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return;
            PlanetLayer layer = PlanetSurfaceWorldActions.WdSurfaceLayer ?? grid.Surface;

            for (int i = 0; i < fullChain.Count - 1; i++)
            {
                PlanetTile a = new PlanetTile(fullChain[i], layer);
                PlanetTile b = new PlanetTile(fullChain[i + 1], layer);
                bool aIsLandBank = i == 0;
                bool bIsLandBank = i == fullChain.Count - 2;
                WorldActions_Roads.ApplyRoadLink(a, b, road, clearFortsOnA: !aIsLandBank, clearFortsOnB: !bIsLandBank);
            }

            WaterScratch.Clear();
            WdBridgeGeometry.CollectWaterTiles(fullChain, WaterScratch);
            WorldComponent_WdBridges bridges = WorldComponent_WdBridges.Get();
            bridges?.RegisterBridgeWaterTiles(WaterScratch);
            bridges?.RecalculatePathCostsForTiles(WaterScratch);
            WD_WorldLayer_MovementDifficultyOverlay.InvalidateAndDirtyIfActive();
        }

        public static void UnlinkEntireSpan(List<int> fullChain)
        {
            if (fullChain == null || fullChain.Count < 2) return;
            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return;
            PlanetLayer layer = PlanetSurfaceWorldActions.WdSurfaceLayer ?? grid.Surface;
            for (int i = 0; i < fullChain.Count - 1; i++)
            {
                PlanetTile a = new PlanetTile(fullChain[i], layer);
                PlanetTile b = new PlanetTile(fullChain[i + 1], layer);
                WorldActions_Roads.RemoveRoadLink(a, b);
            }
        }

        public static void NotifyRoadLinkChanged(int tileA, int tileB)
        {
            WorldComponent_WdBridges bridges = WorldComponent_WdBridges.Get();
            if (bridges == null) return;
            bridges.RefreshTileAfterRoadChange(tileA);
            bridges.RefreshTileAfterRoadChange(tileB);
            var ids = new List<int>(2);
            WorldGrid grid = Find.WorldGrid;
            if (grid != null && grid.InBounds(tileA) && grid[tileA].WaterCovered)
                ids.Add(tileA);
            if (grid != null && grid.InBounds(tileB) && grid[tileB].WaterCovered)
                ids.Add(tileB);
            if (ids.Count > 0)
                bridges.RecalculatePathCostsForTiles(ids);
        }

        public static int WaterTileCount(List<int> fullChain)
        {
            WaterScratch.Clear();
            WdBridgeGeometry.CollectWaterTiles(fullChain, WaterScratch);
            return WaterScratch.Count;
        }

        public static List<OutpostUpgradeCostEntry> GetBridgeMaterialCosts(int waterTiles)
        {
            List<OutpostUpgradeCostEntry> baseCosts =
                ColonyWorldBuildRequirements.GetMaterialCostsForRoad(BridgeRoadTier);
            int scale = Mathf.Max(1, waterTiles) * 2;
            if (baseCosts == null || baseCosts.Count == 0) return ColonyWorldBuildMaterials.EmptyCostList;
            var scaled = new List<OutpostUpgradeCostEntry>(baseCosts.Count);
            for (int i = 0; i < baseCosts.Count; i++)
            {
                OutpostUpgradeCostEntry e = baseCosts[i];
                if (e == null) continue;
                scaled.Add(new OutpostUpgradeCostEntry
                {
                    thingDef = e.thingDef,
                    count = e.count * scale,
                    costMode = e.costMode
                });
            }
            return scaled;
        }

        /// <summary>AnyStoneBlocks count charged for one water tile (2× stone-road segment).</summary>
        public static int GetBridgeAnyStoneBlocksPerWaterTile() =>
            SumAnyStoneBlocks(GetBridgeMaterialCosts(1));

        public static int GetBridgeAnyStoneBlocksTotal(int waterTiles) =>
            SumAnyStoneBlocks(GetBridgeMaterialCosts(waterTiles));

        private static int SumAnyStoneBlocks(List<OutpostUpgradeCostEntry> costs)
        {
            if (costs == null || costs.Count == 0) return 0;
            int total = 0;
            for (int i = 0; i < costs.Count; i++)
            {
                OutpostUpgradeCostEntry e = costs[i];
                if (e == null || e.count <= 0) continue;
                if (ColonyWorldBuildMaterials.IsAnyStoneBlocksCost(e))
                    total += e.count;
            }
            return total;
        }

        public static bool HasMaterialCostsForBridge(int waterTiles) =>
            ColonyWorldBuildMaterials.HasMaterialCosts(GetBridgeMaterialCosts(waterTiles));

        /// <summary>True if another outpost's active bridge project already claims any water tile or bank of this span.</summary>
        public static bool IsSpanProjectLocked(List<int> fullChain, CompViralSpread exclude = null)
        {
            WaterScratch.Clear();
            WdBridgeGeometry.CollectWaterTiles(fullChain, WaterScratch);
            if (fullChain == null || fullChain.Count < 2) return false;

            var waters = new HashSet<int>(WaterScratch);
            int startBank = fullChain[0];
            int endBank = fullChain[fullChain.Count - 1];
            IReadOnlyList<CompViralSpread> projects = WorldConstructionProjectRegistry.ActiveBridgeProjects;
            for (int i = 0; i < projects.Count; i++)
            {
                CompViralSpread c = projects[i];
                if (c == null || c == exclude || c.parent == null || c.parent.Destroyed) continue;
                if (!HasActiveBridgeProject(c)) continue;
                List<int> other = c.bridgeSpanTiles;
                if (other == null || other.Count < 2) continue;
                if (other[0] == startBank || other[0] == endBank
                    || other[other.Count - 1] == startBank || other[other.Count - 1] == endBank)
                    return true;
                for (int j = 1; j < other.Count - 1; j++)
                {
                    if (waters.Contains(other[j]))
                        return true;
                }
            }
            return false;
        }

        /// <summary>Bank already used by a built bridge or an in-flight build project.</summary>
        public static bool IsBankReserved(int landTileId, CompViralSpread exclude = null)
        {
            if (WdBridgeGeometry.BankAlreadyAnchorsBridge(landTileId)) return true;
            IReadOnlyList<CompViralSpread> projects = WorldConstructionProjectRegistry.ActiveBridgeProjects;
            for (int i = 0; i < projects.Count; i++)
            {
                CompViralSpread c = projects[i];
                if (c == null || c == exclude || c.parent == null || c.parent.Destroyed) continue;
                if (!HasActiveBridgeProject(c) || c.bridgeIsClearing) continue;
                List<int> other = c.bridgeSpanTiles;
                if (other == null || other.Count < 2) continue;
                if (other[0] == landTileId || other[other.Count - 1] == landTileId)
                    return true;
            }
            return false;
        }

        public static bool TryGetSpanOccupancyBlocker(
            List<int> fullChain, out string blockerLabel, out int blockerTile, WorldObject except = null)
        {
            blockerLabel = null;
            blockerTile = -1;
            WaterScratch.Clear();
            WdBridgeGeometry.CollectWaterTiles(fullChain, WaterScratch);
            WorldGrid grid = Find.WorldGrid;
            PlanetLayer layer = PlanetSurfaceWorldActions.WdSurfaceLayer ?? grid?.Surface;
            if (layer == null) return false;

            for (int i = 0; i < WaterScratch.Count; i++)
            {
                int tileId = WaterScratch[i];
                PlanetTile pt = new PlanetTile(tileId, layer);

                if (WD_MapComponent_CaravanClash.TileHasBusyCaravanClashAmbush(pt))
                {
                    blockerLabel = "TSA_WD_BridgeOccupant_Ambush".Translate();
                    blockerTile = tileId;
                    return true;
                }
                if (WD_MapComponent_CaravanClash.TileHasBusyCampClash(pt))
                {
                    blockerLabel = "TSA_WD_BridgeOccupant_CampClash".Translate();
                    blockerTile = tileId;
                    return true;
                }

                foreach (WorldObject wo in Find.WorldObjects.ObjectsAt(pt))
                {
                    if (wo == null || wo.Destroyed || wo == except) continue;
                    blockerLabel = wo.LabelCap;
                    blockerTile = tileId;
                    return true;
                }
            }
            return false;
        }

        public static void SendOccupiedArrivalLetter(
            WorldObject origin, string blockerLabel, int blockerTile, List<int> span)
        {
            var seth = WorldDominationMod.settings;
            if (seth != null && !seth.notifyBridgeDeconstructBlocked)
            {
                Messages.Message(
                    "TSA_WD_BridgeDeconstructBlockedMessage".Translate(
                        origin?.LabelCap ?? "?",
                        blockerLabel ?? "?",
                        blockerTile),
                    MessageTypeDefOf.NeutralEvent);
                return;
            }

            Find.LetterStack.ReceiveLetter(
                "TSA_WD_BridgeDeconstructBlockedLetterLabel".Translate(),
                "TSA_WD_BridgeDeconstructBlockedLetterText".Translate(
                    origin?.LabelCap ?? "?",
                    blockerLabel ?? "?",
                    blockerTile),
                LetterDefOf.NeutralEvent,
                new GlobalTargetInfo(blockerTile >= 0 ? blockerTile : (origin?.Tile.tileId ?? 0)));
        }

        public static string RejectMessage(WdBridgeGeometry.RejectReason reason)
        {
            switch (reason)
            {
                case WdBridgeGeometry.RejectReason.StartNotLand:
                case WdBridgeGeometry.RejectReason.EndNotLand:
                    return "TSA_WD_BridgeReject_NeedLandBanks".Translate();
                case WdBridgeGeometry.RejectReason.StartNotAdjacentToWater:
                    return "TSA_WD_BridgeReject_StartNotAdjacentToWater".Translate();
                case WdBridgeGeometry.RejectReason.EndNotAdjacentToWater:
                    return "TSA_WD_BridgeReject_EndNotAdjacentToWater".Translate();
                case WdBridgeGeometry.RejectReason.SameTile:
                    return "TSA_WD_BridgeReject_SameTile".Translate();
                case WdBridgeGeometry.RejectReason.AlreadyBridged:
                    return "TSA_WD_BridgeReject_AlreadyBridged".Translate();
                case WdBridgeGeometry.RejectReason.BankAlreadyUsed:
                    return "TSA_WD_BridgeReject_BankAlreadyUsed".Translate();
                case WdBridgeGeometry.RejectReason.NotBridged:
                    return "TSA_WD_BridgeReject_NotBridged".Translate();
                case WdBridgeGeometry.RejectReason.AmbiguousBridge:
                    return "TSA_WD_BridgeReject_AmbiguousBridge".Translate();
                case WdBridgeGeometry.RejectReason.SpanOccupied:
                    return "TSA_WD_BridgeReject_SpanOccupied".Translate();
                case WdBridgeGeometry.RejectReason.ProjectLocked:
                    return "TSA_WD_BridgeReject_ProjectLocked".Translate();
                case WdBridgeGeometry.RejectReason.ImpassableBank:
                    return "TSA_WD_BridgeReject_ImpassableBank".Translate();
                case WdBridgeGeometry.RejectReason.NotStraightOrTooLong:
                    return "TSA_WD_BridgeReject_NotStraightOrTooLong".Translate(WorldComponent_WdBridges.MaxWaterTiles);
                case WdBridgeGeometry.RejectReason.SameShore:
                    return "TSA_WD_BridgeReject_SameShore".Translate();
                default:
                    return "TSA_WD_BridgeReject_Invalid".Translate();
            }
        }
    }
}
