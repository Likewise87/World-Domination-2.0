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
            return new List<OutpostUpgradeCostEntry>
            {
                new OutpostUpgradeCostEntry
                {
                    thingDef = ThingDefOf.WoodLog,
                    count = WorldComponent_WdTrenches.WoodPerSegment
                }
            };
        }

        /// <summary>Build path tiles hub-first (includes hub as first node). Trench edges are between consecutive non-equal tiles after hub.</summary>
        public static bool TryBuildTrenchPath(WorldObject hub, int destTile, List<int> waypoints, List<int> intoHubFirst)
        {
            intoHubFirst?.Clear();
            if (hub == null || intoHubFirst == null || destTile < 0) return false;
            int hubTile = hub.Tile.tileId;
            if (destTile == hubTile) return false;

            var nodes = new List<int>();
            nodes.Add(hubTile);
            if (waypoints != null)
            {
                for (int i = 0; i < waypoints.Count; i++)
                {
                    int w = waypoints[i];
                    if (w >= 0 && w != nodes[nodes.Count - 1])
                        nodes.Add(w);
                }
            }
            if (destTile != nodes[nodes.Count - 1])
                nodes.Add(destTile);

            intoHubFirst.Add(hubTile);
            for (int i = 0; i < nodes.Count - 1; i++)
            {
                int from = nodes[i];
                int to = nodes[i + 1];
                var layer = PlanetSurfaceWorldActions.WdSurfaceLayer;
                if (layer?.Pather == null) return false;
                using WorldPath path = layer.Pather.FindPath(new PlanetTile(from, layer), new PlanetTile(to, layer), null);
                if (path == null || !path.Found) return false;
                var rev = path.NodesReversed;
                // NodesReversed is dest→start; walk reverse to append start→dest excluding duplicate start.
                for (int n = rev.Count - 2; n >= 0; n--)
                {
                    int id = rev[n].tileId;
                    if (intoHubFirst.Count == 0 || intoHubFirst[intoHubFirst.Count - 1] != id)
                        intoHubFirst.Add(id);
                }
            }

            // Validate exclusivity / radius for every tile after hub. Hub is the forced first node.
            var trenches = WorldComponent_WdTrenches.Get();
            if (trenches == null) return false;
            if (intoHubFirst.Count < 2 || intoHubFirst[0] != hubTile) return false;
            for (int i = 1; i < intoHubFirst.Count; i++)
            {
                int tile = intoHubFirst[i];
                float dist = Find.WorldGrid.ApproxDistanceInTiles(hub.Tile, new PlanetTile(tile));
                if (dist > WorldComponent_WdTrenches.MaxBuildRadiusTiles + 0.01f)
                    return false;
                if (!WorldComponent_WdTrenches.HasTrenchAt(tile))
                {
                    var report = WorldComponent_WdTrenches.CanUseTileForTrench(tile, hub);
                    if (!report.Accepted) return false;
                }
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
                if (trenches.TryAddEdge(from, to, origin, out _))
                    traveler.MarkConstructionWorkApplied();
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
