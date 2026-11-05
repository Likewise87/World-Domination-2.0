using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public static class WorldActions_Trenches
    {
        public const float SegmentWork = 1f;

        public static bool HasActiveTrenchProject(CompViralSpread comp) =>
            comp != null && comp.trenchTargetTile != -1;

        public static void ClearTrenchProject(CompViralSpread comp)
        {
            if (comp == null) return;
            comp.trenchTargetTile = -1;
            comp.trenchProgress = 0f;
            comp.trenchIsClearing = false;
            comp.trenchBuilderInField = false;
            comp.trenchCachedWorkTile = -1;
            comp.trenchTargetName = string.Empty;
            comp.cachedTrenchPathTiles?.Clear();
            comp.trenchWaypointTiles?.Clear();
        }

        public static bool HasActiveTrenchBuilderFrom(WorldObject origin)
        {
            if (origin == null || Find.WorldObjects == null) return false;
            var list = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] is WorldObject_Traveler t
                    && !t.Destroyed
                    && (t.mission == TravelerMission.TrenchBuilding || t.mission == TravelerMission.TrenchClearing)
                    && t.originObject == origin)
                    return true;
            }
            return false;
        }

        public static void DestroyActiveTrenchBuildersFrom(WorldObject origin)
        {
            if (origin == null || Find.WorldObjects == null) return;
            var list = Find.WorldObjects.AllWorldObjects;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i] is WorldObject_Traveler t
                    && !t.Destroyed
                    && (t.mission == TravelerMission.TrenchBuilding || t.mission == TravelerMission.TrenchClearing)
                    && t.originObject == origin)
                {
                    t.Destroy();
                }
            }
        }

        public static List<OutpostUpgradeCostEntry> GetWoodCostPerSegment()
        {
            int wood = WorldComponent_WdTrenches.WoodPerSegment;
            var s = WorldDominationMod.settings;
            if (s != null)
                wood = s.ScaleWorldBuildMaterialCount(wood);
            if (wood <= 0) return ColonyWorldBuildMaterials.EmptyCostList;
            return new List<OutpostUpgradeCostEntry>
            {
                new OutpostUpgradeCostEntry
                {
                    thingDef = ThingDefOf.WoodLog,
                    count = wood
                }
            };
        }

        /// <summary>
        /// Build path tiles hub-first (includes hub as first node).
        /// Waypoints are optional legs; path must be a simple polyline (no loops / no tile revisits).
        /// Existing owned trench tiles (including tips) are walked along the trench graph so arms can be extended;
        /// new digs leave the hub or a degree-1 tip onto empty land only.
        /// </summary>
        public static bool TryBuildTrenchPath(WorldObject hub, int destTile, List<int> waypoints, List<int> intoHubFirst)
            => TryBuildTrenchPath(hub, destTile, waypoints, intoHubFirst, out _);

        public static bool TryBuildTrenchPath(
            WorldObject hub,
            int destTile,
            List<int> waypoints,
            List<int> intoHubFirst,
            out string failReason)
        {
            intoHubFirst?.Clear();
            failReason = null;
            if (hub == null || intoHubFirst == null || destTile < 0)
            {
                failReason = "TSA_WD_Trench_InvalidPath".Translate();
                return false;
            }
            // Player trenches are outpost-only. NPC Settlements may own trenches later; block player Settlement hubs only.
            if (hub.Faction == Faction.OfPlayer && hub is not WorldObject_WD_Outpost)
            {
                failReason = "TSA_WD_Trench_OutpostOnly".Translate();
                return false;
            }
            int hubTile = hub.Tile.tileId;
            if (destTile == hubTile)
            {
                failReason = "TSA_WD_Trench_InvalidPath".Translate();
                return false;
            }

            var trenches = WorldComponent_WdTrenches.Get();
            if (trenches == null)
            {
                failReason = "TSA_WD_Trench_InvalidPath".Translate();
                return false;
            }

            var nodes = new List<int>();
            nodes.Add(hubTile);
            if (waypoints != null)
            {
                for (int i = 0; i < waypoints.Count; i++)
                {
                    int w = waypoints[i];
                    if (w < 0) continue;
                    if (w == nodes[nodes.Count - 1]) continue;
                    // Explicit waypoint must not revisit an earlier node (loop / overlap).
                    for (int j = 0; j < nodes.Count; j++)
                    {
                        if (nodes[j] == w)
                        {
                            failReason = "TSA_WD_Trench_NoLoop".Translate();
                            return false;
                        }
                    }
                    nodes.Add(w);
                }
            }
            if (destTile != nodes[nodes.Count - 1])
            {
                for (int j = 0; j < nodes.Count; j++)
                {
                    if (nodes[j] == destTile)
                    {
                        failReason = "TSA_WD_Trench_NoLoop".Translate();
                        return false;
                    }
                }
                nodes.Add(destTile);
            }

            intoHubFirst.Add(hubTile);
            var seen = new HashSet<int> { hubTile };
            for (int i = 0; i < nodes.Count - 1; i++)
            {
                int from = nodes[i];
                int to = nodes[i + 1];
                bool fromOwn = IsOwnTrenchNetworkTile(trenches, hub, from);
                bool toOwn = IsOwnTrenchNetworkTile(trenches, hub, to);

                if (fromOwn && toOwn)
                {
                    if (!TryAppendTrenchGraphPath(trenches, hub, from, to, intoHubFirst, seen, out failReason))
                    {
                        intoHubFirst.Clear();
                        return false;
                    }
                    continue;
                }

                // Dig / land hop: start must already be on the planned path (hub, tip, or prior undug tile).
                if (!TryAppendLandDigPath(from, to, intoHubFirst, seen, out failReason))
                {
                    intoHubFirst.Clear();
                    return false;
                }
            }

            if (intoHubFirst.Count < 2 || intoHubFirst[0] != hubTile)
            {
                failReason = "TSA_WD_Trench_InvalidPath".Translate();
                return false;
            }

            // Radius + exclusivity + no branch/overlap onto foreign or mid-arm trench tiles.
            for (int i = 1; i < intoHubFirst.Count; i++)
            {
                int tile = intoHubFirst[i];
                int prev = intoHubFirst[i - 1];
                float dist = Find.WorldGrid.ApproxDistanceInTiles(hub.Tile, new PlanetTile(tile));
                if (dist > WorldComponent_WdTrenches.MaxBuildRadiusTiles + 0.01f)
                {
                    failReason = "TSA_WD_Trench_OutOfRange".Translate(
                        WorldComponent_WdTrenches.MaxBuildRadiusTiles.ToString());
                    return false;
                }

                bool tileHas = WorldComponent_WdTrenches.HasTrenchAt(tile);
                bool prevHas = WorldComponent_WdTrenches.HasTrenchAt(prev);
                bool edgeExists = trenches.HasEdge(prev, tile);

                if (edgeExists)
                    continue; // walking an existing segment of this network

                if (tileHas)
                {
                    // Landing on an existing trench tile without an edge = merge/overlap.
                    failReason = "TSA_WD_Trench_NoOverlap".Translate();
                    return false;
                }

                // New dig onto empty tile: previous must be hub, tip (deg 1), or mid-arm (deg 2 → T).
                // Undug prev in this planned path (prevHas false) is fine.
                if (prevHas && prev != hubTile)
                {
                    int deg = trenches.Degree(prev);
                    if (deg < 1 || deg >= WorldComponent_WdTrenches.MaxArmDegree)
                    {
                        failReason = deg >= WorldComponent_WdTrenches.MaxArmDegree
                            ? "TSA_WD_Trench_MaxDegree".Translate()
                            : "TSA_WD_Trench_NoBranch".Translate();
                        return false;
                    }
                }

                var report = WorldComponent_WdTrenches.CanUseTileForTrench(tile, hub);
                if (!report.Accepted)
                {
                    failReason = report.Reason;
                    return false;
                }

                // Same arm-gap rule as TryAddEdge (hex: touching the dig point's existing arm tiles is OK).
                if (trenches.WouldViolateArmSeparation(tile, prev, hub.ID))
                {
                    failReason = "TSA_WD_Trench_ArmTooClose".Translate();
                    return false;
                }
            }

            return true;
        }

        /// <summary>True when the planned hub-first path still has at least one undug edge.</summary>
        public static bool PathHasNewDig(List<int> hubFirstPath) =>
            TryGetFirstUnfinishedBuildEdge(hubFirstPath, out _, out _);

        private static bool IsOwnTrenchNetworkTile(WorldComponent_WdTrenches trenches, WorldObject hub, int tile)
        {
            if (hub == null || tile < 0) return false;
            if (tile == hub.Tile.tileId) return true;
            if (!WorldComponent_WdTrenches.HasTrenchAt(tile)) return false;
            return trenches.TryGetOwner(tile, out WorldObject owner) && owner == hub;
        }

        /// <summary>BFS along this hub's trench edges (and hub root) from <paramref name="from"/> to <paramref name="to"/>.</summary>
        private static bool TryAppendTrenchGraphPath(
            WorldComponent_WdTrenches trenches,
            WorldObject hub,
            int from,
            int to,
            List<int> intoHubFirst,
            HashSet<int> seen,
            out string failReason)
        {
            failReason = null;
            if (from == to) return true;
            if (intoHubFirst.Count == 0 || intoHubFirst[intoHubFirst.Count - 1] != from)
            {
                failReason = "TSA_WD_Trench_InvalidPath".Translate();
                return false;
            }

            var parent = new Dictionary<int, int>();
            var q = new Queue<int>();
            q.Enqueue(from);
            parent[from] = -1;
            var scratch = new List<int>(8);
            while (q.Count > 0)
            {
                int cur = q.Dequeue();
                if (cur == to) break;
                scratch.Clear();
                if (cur == hub.Tile.tileId)
                {
                    // Hub root: every trench neighbor of the hub is an arm start.
                    var edges = trenches.Edges;
                    for (int e = 0; e < edges.Count; e++)
                    {
                        WdTrenchEdge edge = edges[e];
                        if (edge == null || !edge.Involves(cur)) continue;
                        if (!trenches.TryGetOwner(edge.tileA, out WorldObject o) || o != hub) continue;
                        int other = edge.Other(cur);
                        if (other < 0 || parent.ContainsKey(other)) continue;
                        parent[other] = cur;
                        q.Enqueue(other);
                    }
                    continue;
                }

                if (!trenches.TryGetNeighbors(cur, scratch)) continue;
                for (int i = 0; i < scratch.Count; i++)
                {
                    int nb = scratch[i];
                    if (parent.ContainsKey(nb)) continue;
                    if (!IsOwnTrenchNetworkTile(trenches, hub, nb)) continue;
                    parent[nb] = cur;
                    q.Enqueue(nb);
                }
            }

            if (!parent.ContainsKey(to))
            {
                failReason = "TSA_WD_Trench_InvalidPath".Translate();
                return false;
            }

            var rev = new List<int>();
            for (int t = to; t != from; t = parent[t])
                rev.Add(t);
            for (int i = rev.Count - 1; i >= 0; i--)
            {
                int id = rev[i];
                if (intoHubFirst[intoHubFirst.Count - 1] == id) continue;
                if (!seen.Add(id))
                {
                    failReason = "TSA_WD_Trench_NoLoop".Translate();
                    return false;
                }
                intoHubFirst.Add(id);
            }
            return true;
        }

        private static bool TryAppendLandDigPath(
            int from,
            int to,
            List<int> intoHubFirst,
            HashSet<int> seen,
            out string failReason)
        {
            failReason = null;
            if (from == to) return true;
            if (intoHubFirst.Count == 0 || intoHubFirst[intoHubFirst.Count - 1] != from)
            {
                failReason = "TSA_WD_Trench_InvalidPath".Translate();
                return false;
            }

            var layer = PlanetSurfaceWorldActions.WdSurfaceLayer;
            if (layer?.Pather == null)
            {
                failReason = "TSA_WD_Trench_InvalidPath".Translate();
                return false;
            }
            using WorldPath path = layer.Pather.FindPath(new PlanetTile(from, layer), new PlanetTile(to, layer), null);
            if (path == null || !path.Found)
            {
                failReason = "TSA_WD_Trench_InvalidPath".Translate();
                return false;
            }
            var rev = path.NodesReversed;
            // NodesReversed is dest→start; walk reverse to append start→dest excluding duplicate start.
            for (int n = rev.Count - 2; n >= 0; n--)
            {
                int id = rev[n].tileId;
                if (intoHubFirst[intoHubFirst.Count - 1] == id)
                    continue;
                if (!seen.Add(id))
                {
                    failReason = "TSA_WD_Trench_NoLoop".Translate();
                    return false;
                }
                intoHubFirst.Add(id);
            }
            return true;
        }

        public static bool TryGetFirstUnfinishedBuildEdge(List<int> hubFirstPath, out int from, out int to)
        {
            from = to = -1;
            if (hubFirstPath == null || hubFirstPath.Count < 2) return false;
            var c = WorldComponent_WdTrenches.Get();
            if (c == null) return false;
            // Path is hub-first: [hub, t1, t2, ...]. Paint edges (hub,t1), (t1,t2), ...
            for (int i = 0; i < hubFirstPath.Count - 1; i++)
            {
                int a = hubFirstPath[i];
                int b = hubFirstPath[i + 1];
                if (!c.HasEdge(a, b))
                {
                    from = a;
                    to = b;
                    return true;
                }
            }
            return false;
        }

        public static bool TryGetNextClearTip(WorldObject owner, out int tipTile)
        {
            tipTile = -1;
            if (owner == null) return false;
            var c = WorldComponent_WdTrenches.Get();
            if (c == null || !c.HasAny) return false;
            int hub = owner.Tile.tileId;
            // Find any degree-1 tip owned by this owner farthest from hub.
            float best = -1f;
            var edges = c.Edges;
            var tips = new HashSet<int>();
            for (int i = 0; i < edges.Count; i++)
            {
                var e = edges[i];
                if (e == null) continue;
                if (!c.TryGetOwner(e.tileA, out var o) || o != owner) continue;
                if (c.Degree(e.tileA) == 1) tips.Add(e.tileA);
                if (c.Degree(e.tileB) == 1) tips.Add(e.tileB);
            }
            foreach (int t in tips)
            {
                if (t == hub) continue; // never remove from the origin cell; tip→hub only
                float d = Find.WorldGrid.ApproxDistanceInTiles(new PlanetTile(t), new PlanetTile(hub));
                if (d > best)
                {
                    best = d;
                    tipTile = t;
                }
            }
            return tipTile >= 0;
        }

        /// <summary>Called when <see cref="CompViralSpread.trenchProgress"/> has reached 1 and no crew is in field.</summary>
        public static void TickProject(CompViralSpread comp, WorldObject actor)
        {
            if (comp == null || actor == null || !HasActiveTrenchProject(comp)) return;
            if (comp.trenchBuilderInField) return;
            if (!WorldComponent_WdTrenches.FeatureEnabled)
            {
                ClearTrenchProject(comp);
                return;
            }
            if (comp.trenchProgress < 1f) return;

            if (comp.trenchIsClearing)
                TryLaunchClearCrew(actor, comp);
            else
                TryLaunchBuildCrew(actor, comp);
        }

        private static void TryLaunchBuildCrew(WorldObject actor, CompViralSpread comp)
        {
            if (!TryGetFirstUnfinishedBuildEdge(comp.cachedTrenchPathTiles, out int from, out int to))
            {
                ClearTrenchProject(comp);
                Messages.Message("TSA_WD_Trench_BuildComplete".Translate(actor.LabelCap), actor, MessageTypeDefOf.PositiveEvent);
                return;
            }

            // Road appeared on the next dig tile after the project was issued — cancel, do not redispatch.
            if (NextDigBlockedByExistingRoad(actor, from, to))
            {
                CancelTrenchProjectBlockedByRoad(actor);
                return;
            }

            float cost = WorldActions_Roads.GetExpeditionStrengthCost(SettlementTier.T1);
            if (!WorldActions_Utils.CanAffordExpeditionLeavingGarrison(comp, cost))
                return;

            var materials = GetWoodCostPerSegment();
            if (!ColonyWorldBuildMaterials.HasMaterialCosts(materials, out _))
                return;

            if (!WorldActions_Utils.TryConsumeExpeditionStrength(comp, cost))
                return;

            if (!SpawnTrenchTraveler(actor, to, TravelerMission.TrenchBuilding, cost, materials))
            {
                WorldActions_Utils.RefundExpeditionStrength(comp, cost);
                return;
            }

            comp.trenchBuilderInField = true;
            comp.trenchProgress = 0f;
            comp.trenchCachedWorkTile = to;
        }

        private static void TryLaunchClearCrew(WorldObject actor, CompViralSpread comp)
        {
            if (!TryGetNextClearTip(actor, out int tip))
            {
                ClearTrenchProject(comp);
                Messages.Message("TSA_WD_Trench_ClearComplete".Translate(actor.LabelCap), actor, MessageTypeDefOf.PositiveEvent);
                return;
            }

            float cost = WorldActions_Roads.GetExpeditionStrengthCost(SettlementTier.T1);
            if (!WorldActions_Utils.CanAffordExpeditionLeavingGarrison(comp, cost))
                return;
            if (!WorldActions_Utils.TryConsumeExpeditionStrength(comp, cost))
                return;

            if (!SpawnTrenchTraveler(actor, tip, TravelerMission.TrenchClearing, cost, null))
            {
                WorldActions_Utils.RefundExpeditionStrength(comp, cost);
                return;
            }

            comp.trenchBuilderInField = true;
            comp.trenchProgress = 0f;
            comp.trenchCachedWorkTile = tip;
        }

        private static bool SpawnTrenchTraveler(
            WorldObject origin,
            int destTile,
            TravelerMission mission,
            float cost,
            List<OutpostUpgradeCostEntry> materials)
        {
            var comp = origin.GetComponent<CompViralSpread>();
            if (comp == null) return false;

            WorldObjectDef def = DefDatabase<WorldObjectDef>.GetNamedSilentFail("TSA_WD_Traveler_Outpost_RoadBuilder")
                ?? DefDatabase<WorldObjectDef>.GetNamedSilentFail("TSA_WD_Traveler_RoadBuilder");
            if (def == null) return false;

            var traveler = (WorldObject_Traveler)WorldObjectMaker.MakeWorldObject(def);
            traveler.Tile = origin.Tile;
            traveler.SetFaction(origin.Faction);
            traveler.mission = mission;
            traveler.originObject = origin;
            traveler.travelerStrength = cost;
            traveler.initialStrength = cost;
            AdvancedBuildingEffects.ApplyNpcLandSpeed(traveler, origin);
            Find.WorldObjects.Add(traveler);
            traveler.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(destTile, origin));
            if (traveler.Destroyed) return false;

            if (mission == TravelerMission.TrenchBuilding && materials != null)
                return ColonyWorldBuildRequirements.TryFinalizeMaterialsOrAbort(origin, traveler, cost, materials);
            return true;
        }

        public static void NotifyBuilderDestroyed(WorldObject_Traveler traveler)
        {
            if (traveler?.originObject == null) return;
            traveler.originObject.GetComponent<CompViralSpread>()?.NotifyTrenchCrewReturned();
        }

        public static void ExecuteTrenchWork(WorldObject_Traveler traveler)
        {
            if (traveler?.originObject == null) return;
            var origin = traveler.originObject;
            var comp = origin.GetComponent<CompViralSpread>();
            var trenches = WorldComponent_WdTrenches.Get();
            if (trenches == null) return;

            if (traveler.mission == TravelerMission.TrenchClearing)
            {
                int tip = traveler.Tile.tileId;
                if (trenches.TryRemoveTipAt(tip, out _))
                    traveler.MarkConstructionWorkApplied();
                if (comp != null)
                {
                    comp.trenchBuilderInField = false;
                    comp.trenchProgress = 0f;
                    if (!TryGetNextClearTip(origin, out _))
                        ClearTrenchProject(comp);
                }
                traveler.Destroy();
                return;
            }

            // Build: apply first unfinished edge from planned path.
            if (comp != null && TryGetFirstUnfinishedBuildEdge(comp.cachedTrenchPathTiles, out int from, out int to))
            {
                if (NextDigBlockedByExistingRoad(origin, from, to))
                {
                    CancelTrenchProjectBlockedByRoad(origin);
                    return;
                }

                if (trenches.TryAddEdge(from, to, origin, out string addFail))
                    traveler.MarkConstructionWorkApplied();
                else if (!addFail.NullOrEmpty() && origin.Faction == Faction.OfPlayer)
                {
                    Messages.Message(
                        addFail,
                        new GlobalTargetInfo(to),
                        MessageTypeDefOf.RejectInput,
                        historical: false);
                }
            }

            if (comp != null)
            {
                comp.trenchBuilderInField = false;
                comp.trenchProgress = 0f;
                if (!TryGetFirstUnfinishedBuildEdge(comp.cachedTrenchPathTiles, out _, out _))
                    ClearTrenchProject(comp);
            }
            traveler.Destroy();
        }

        /// <summary>True when the next undug trench endpoint is already occupied by a road (existing roads only).</summary>
        private static bool NextDigBlockedByExistingRoad(WorldObject hub, int from, int to)
        {
            if (hub == null) return false;
            // Walking an existing trench edge is never a dig.
            var trenches = WorldComponent_WdTrenches.Get();
            if (trenches != null && trenches.HasEdge(from, to)) return false;

            // New dig lands on <paramref name="to"/> (and may leave <paramref name="from"/> when both empty).
            if (!WorldComponent_WdTrenches.HasTrenchAt(to)
                && WorldComponent_WdTrenches.TileHasRoadLink(to))
                return true;
            if (!WorldComponent_WdTrenches.HasTrenchAt(from)
                && from != hub.Tile.tileId
                && WorldComponent_WdTrenches.TileHasRoadLink(from))
                return true;
            return false;
        }

        /// <summary>Abort trench dig with a blue letter; clears project and active builders.</summary>
        public static void CancelTrenchProjectBlockedByRoad(WorldObject actor)
        {
            if (actor == null || actor.Destroyed) return;
            var comp = actor.GetComponent<CompViralSpread>();
            if (!HasActiveTrenchProject(comp)) return;

            string label = actor.LabelCap;
            DestroyActiveTrenchBuildersFrom(actor);
            ClearTrenchProject(comp);

            if (actor.Faction == Faction.OfPlayer
                && (WorldDominationMod.settings?.notifyTrenchCancelledByRoad
                    ?? WorldDominationSettings.DefNotifyTrenchCancelledByRoad))
            {
                Find.LetterStack.ReceiveLetter(
                    "TSA_WD_Letter_TrenchCancelledByRoad_Label".Translate(),
                    "TSA_WD_Letter_TrenchCancelledByRoad_Text".Translate(label),
                    LetterDefOf.NeutralEvent,
                    actor);
            }
        }

        public static void WipeForOwner(WorldObject owner)
        {
            WorldComponent_WdTrenches.Get()?.WipeOwnedBy(owner);
            if (owner != null)
            {
                var comp = owner.GetComponent<CompViralSpread>();
                if (HasActiveTrenchProject(comp))
                {
                    DestroyActiveTrenchBuildersFrom(owner);
                    ClearTrenchProject(comp);
                }
            }
        }

        /// <summary>Path B type-change: keep trenches, retarget ownership to the replacement outpost.</summary>
        public static void RemapOwner(WorldObject oldOwner, WorldObject newOwner)
        {
            if (oldOwner == null || newOwner == null) return;
            WorldComponent_WdTrenches.Get()?.RemapOwnerId(oldOwner.ID, newOwner);
        }

        /// <summary>
        /// After hub-connectivity prune: abort dig/clear if the planned path no longer sits on a connected trench.
        /// </summary>
        public static void NotifyOwnerNetworkPruned(WorldObject owner)
        {
            if (owner == null || owner.Destroyed) return;
            var comp = owner.GetComponent<CompViralSpread>();
            if (!HasActiveTrenchProject(comp)) return;

            var trenches = WorldComponent_WdTrenches.Get();
            if (trenches == null) return;

            List<int> path = comp.cachedTrenchPathTiles;
            if (path != null)
            {
                for (int i = 0; i < path.Count; i++)
                {
                    int t = path[i];
                    if (t == owner.Tile.tileId) continue;
                    if (!WorldComponent_WdTrenches.HasTrenchAt(t)
                        || !trenches.IsConnectedToOwnerHub(t, owner))
                    {
                        DestroyActiveTrenchBuildersFrom(owner);
                        ClearTrenchProject(comp);
                        return;
                    }
                }
            }
        }

        public static void OnFeatureDisabled()
        {
            var c = WorldComponent_WdTrenches.Get();
            c?.WipeAll();
            if (Find.WorldObjects == null) return;
            var list = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < list.Count; i++)
            {
                var o = list[i];
                if (o == null) continue;
                var comp = o.GetComponent<CompViralSpread>();
                if (!HasActiveTrenchProject(comp)) continue;
                DestroyActiveTrenchBuildersFrom(o);
                ClearTrenchProject(comp);
            }
        }
    }
}
