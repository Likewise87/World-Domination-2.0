using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public static class WorldActions_Decontamination
    {
        /// <summary>Prep ticks for one scrub segment. Uses asphalt (T3) baseline ratio like heavy road work.</summary>
        public static float GetDecontaminationProgressRequiredTicks()
        {
            SettlementTier baselineTier = SettlementTier.T3;
            float baseline = WorldActions_Roads.GetRoadProgressRequiredTicks(baselineTier);
            var s = WorldDominationMod.settings;
            float work = s != null ? s.GetDecontaminationWork() : WorldDominationSettings.DefDecontaminationWork;
            float refWork = s != null
                ? s.GetFallbackRoadWork(baselineTier)
                : WorldDominationSettings.DefFallbackAsphaltRoadWork;
            if (refWork < 1f) refWork = 1f;
            return baseline * (Mathf.Max(1f, work) / refWork);
        }

        public static float GetEstimatedDaysPerSegment(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return -1f;
            float workSpeed = WorldActions_Roads.GetRoadProgressWorkSpeed(outpost);
            if (workSpeed < 0.01f) return -1f;
            float ticks = GetDecontaminationProgressRequiredTicks() / workSpeed;
            return ticks / GenDate.TicksPerDay;
        }

        public static float GetExpeditionStrengthCost()
        {
            var s = WorldDominationMod.settings;
            return Mathf.Max(1f, s != null
                ? s.GetDecontaminationExpeditionStrength()
                : WorldDominationSettings.DefDecontaminationExpeditionStrength);
        }

        public static int GetMinConstruction()
        {
            return WorldActions_Roads.GetMinConstructionToBuildRoad(SettlementTier.T3);
        }

        public static float GetMaxRange()
        {
            var s = WorldDominationMod.settings;
            return s != null ? s.maxDecontaminationRange : WorldDominationSettings.DefMaxDecontaminationRange;
        }

        public static float GetMaxRange(WorldObject source)
        {
            float range = GetMaxRange();
            if (source is WorldObject_WD_Outpost wdOutpost)
                range *= 1f + OutpostExpertUtility.GetEngineerConstructionRadiusBonus(wdOutpost);
            return range;
        }

        public static bool HasActiveDecontaminationProject(CompViralSpread comp)
        {
            return comp != null && comp.decontamPlannedTiles != null && comp.decontamPlannedTiles.Count > 0;
        }

        public static bool IsTilePolluted(int tileId)
        {
            var grid = Find.WorldGrid;
            if (grid == null || tileId < 0 || tileId >= grid.TilesCount) return false;
            return grid[tileId].pollution > 0.0001f;
        }

        public static bool IsValidWorkTile(int tileId)
        {
            // Scrub tiles may host settlements or outposts (NPC home-tile is top priority).
            // Do not reuse road-block eligibility, which rejects occupied tiles.
            if (!WorldActions_RoadBlocks.IsFlatPathWalkable(tileId)) return false;
            return IsTilePolluted(tileId);
        }

        public static bool IsValidPlanNode(int tileId)
        {
            return WorldActions_RoadBlocks.IsFlatPathWalkable(tileId);
        }

        public static List<int> FilterPlannedTilesFromClickedNodes(List<int> clickedNodes)
        {
            var result = new List<int>();
            if (clickedNodes == null || clickedNodes.Count == 0) return result;

            var seen = new HashSet<int>();
            void TryAdd(int tile)
            {
                if (!seen.Add(tile)) return;
                if (IsValidWorkTile(tile))
                    result.Add(tile);
            }

            TryAdd(clickedNodes[0]);

            for (int i = 0; i < clickedNodes.Count - 1; i++)
            {
                int a = clickedNodes[i];
                int b = clickedNodes[i + 1];
                if (a == b) continue;

                List<int> pathDestFirst = WorldActions_RoadBlocks.FindFlatHopPathDestFirst(a, b);
                if (pathDestFirst == null || pathDestFirst.Count < 2) continue;
                for (int n = pathDestFirst.Count - 1; n >= 0; n--)
                    TryAdd(pathDestFirst[n]);
            }

            return result;
        }

        public static void ClearDecontaminationProject(CompViralSpread comp)
        {
            if (comp == null) return;
            DestroyActiveDecontaminationCrewsFrom(comp.parent);
            comp.decontamPlannedTiles?.Clear();
            comp.decontamClickedNodes?.Clear();
            comp.decontamCachedPathTiles?.Clear();
            comp.decontamProgress = 0f;
            comp.decontamWorkIndex = 0;
            comp.decontamCachedWorkTile = -1;
            comp.decontamTargetName = string.Empty;
            comp.NotifyDecontaminationCrewReturned();
        }

        public static void DestroyActiveDecontaminationCrewsFrom(WorldObject origin)
        {
            if (origin == null || Find.WorldObjects == null) return;
            var allWo = Find.WorldObjects.AllWorldObjects;
            for (int wi = allWo.Count - 1; wi >= 0; wi--)
            {
                if (allWo[wi] is WorldObject_Traveler t
                    && t.mission == TravelerMission.Decontamination
                    && t.originObject == origin
                    && !t.Destroyed)
                {
                    t.Destroy();
                }
            }
        }

        public static bool HasActiveDecontaminationCrewFrom(WorldObject origin)
        {
            if (origin == null || Find.WorldObjects == null) return false;
            foreach (var wo in Find.WorldObjects.AllWorldObjects)
            {
                if (wo is WorldObject_Traveler t && !t.Destroyed
                    && t.mission == TravelerMission.Decontamination
                    && t.originObject == origin)
                    return true;
            }
            return false;
        }

        public static int GetCurrentWorkTile(CompViralSpread comp)
        {
            if (comp?.decontamPlannedTiles == null) return -1;
            while (comp.decontamWorkIndex < comp.decontamPlannedTiles.Count)
            {
                int tile = comp.decontamPlannedTiles[comp.decontamWorkIndex];
                if (IsValidWorkTile(tile))
                    return tile;
                comp.decontamWorkIndex++;
            }
            return -1;
        }

        public static bool LaunchDecontaminationCrewFromOutpost(WorldObject actor)
        {
            var comp = actor?.GetComponent<CompViralSpread>();
            if (!HasActiveDecontaminationProject(comp)) return false;

            int workTile = GetCurrentWorkTile(comp);
            comp.decontamCachedWorkTile = workTile;
            if (workTile < 0)
            {
                ClearDecontaminationProject(comp);
                return false;
            }

            float cost = GetExpeditionStrengthCost();
            if (comp.strength < cost) return false;

            return SpawnDecontaminationTraveler(actor, workTile, cost, cost);
        }

        /// <summary>
        /// NPC settlements: scrub the closest polluted work tile in range (home tile first when polluted).
        /// Pays up to settings cost without pulling offense below 10; still spawns if paid is 0 (traveler strength 1).
        /// </summary>
        public static bool TryNpcSettlementAutoDecontaminate(WorldObject settlement)
        {
            var comp = settlement?.GetComponent<CompViralSpread>();
            if (comp == null || !comp.IsSettlement) return false;
            if (settlement.Faction == null || settlement.Faction.IsPlayer) return false;
            if (PollutionImmunity.IsImmune(settlement)) return false;

            var s = WorldDominationMod.settings;
            if (s == null || !s.travelerPollutionDamageEnabled) return false;

            if (comp.DecontamBuilderInField || HasActiveDecontaminationCrewFrom(settlement))
                return false;

            int radius = s.pollutionDamageRadius;
            int workTile = SitePollutionDamage.FindClosestPollutedWorkTile(settlement.Tile, radius);
            if (workTile < 0) return false;

            if (HasActiveDecontaminationProject(comp))
                ClearDecontaminationProject(comp);

            float cost = Mathf.Max(1f, s.npcSettlementDecontaminationStrengthCost);
            const float offenseFloor = 10f;
            float paid = Mathf.Min(cost, Mathf.Max(0f, comp.offensiveStrength - offenseFloor));
            float travelerStr = Mathf.Max(1f, paid);

            if (comp.decontamPlannedTiles == null)
                comp.decontamPlannedTiles = new List<int>();
            if (comp.decontamClickedNodes == null)
                comp.decontamClickedNodes = new List<int>();
            if (comp.decontamCachedPathTiles == null)
                comp.decontamCachedPathTiles = new List<int>();

            comp.decontamPlannedTiles.Clear();
            comp.decontamPlannedTiles.Add(workTile);
            comp.decontamClickedNodes.Clear();
            comp.decontamClickedNodes.Add(workTile);
            comp.decontamCachedPathTiles.Clear();
            comp.decontamWorkIndex = 0;
            comp.decontamProgress = 0f;
            comp.decontamCachedWorkTile = workTile;
            comp.decontamTargetName = string.Empty;

            if (!SpawnDecontaminationTraveler(settlement, workTile, paid, travelerStr))
            {
                ClearDecontaminationProject(comp);
                return false;
            }

            // Same-tile scrub finishes inside Spawn (traveler already gone). Only mark in-field when a crew is traveling.
            if (HasActiveDecontaminationCrewFrom(settlement))
                comp.NotifyDecontaminationCrewDispatched();
            return true;
        }

        private static bool SpawnDecontaminationTraveler(WorldObject origin, int destTile, float deductAmount, float travelerStrength)
        {
            var comp = origin.GetComponent<CompViralSpread>();
            if (comp == null) return false;

            if (deductAmount > 0f)
            {
                comp.strength = Mathf.Max(0f, comp.strength - deductAmount);
                comp.CheckTierUpdate(false);
            }

            WorldObjectDef def = DefDatabase<WorldObjectDef>.GetNamed("TSA_WD_Traveler_Outpost_Decontamination", false)
                ?? DefDatabase<WorldObjectDef>.GetNamed("TSA_WD_Traveler_Outpost_RoadBlock", false)
                ?? DefDatabase<WorldObjectDef>.GetNamed("TSA_WD_Traveler_Outpost_RoadBuilder", false);
            if (def == null)
            {
                if (deductAmount > 0f)
                    comp.AddStrength(deductAmount);
                return false;
            }

            float strengthOnTraveler = Mathf.Max(1f, travelerStrength);
            WorldObject_Traveler traveler = (WorldObject_Traveler)WorldObjectMaker.MakeWorldObject(def);
            traveler.Tile = origin.Tile;
            traveler.SetFaction(origin.Faction);
            traveler.mission = TravelerMission.Decontamination;
            traveler.originObject = origin;
            traveler.travelerStrength = strengthOnTraveler;
            traveler.initialStrength = strengthOnTraveler;

            Find.WorldObjects.Add(traveler);

            PlanetTile destPt = PlanetSurfaceWorldActions.PlanetTileForWdTravel(destTile, origin);
            // Home-tile / same-tile scrub: StartPath would Arrive+Destroy before this method returns,
            // which callers used to treat as failure and which would leave DecontamBuilderInField stuck.
            if (destPt.tileId == origin.Tile.tileId)
            {
                WorldActions_Traveler.ExecuteArrival(traveler, -1);
                if (traveler != null && !traveler.Destroyed)
                    traveler.Destroy();
                return true;
            }

            traveler.pather.StartPath(destPt);
            if (traveler.Destroyed)
            {
                if (deductAmount > 0f)
                    comp.AddStrength(deductAmount);
                return false;
            }
            return true;
        }

        public static void ExecuteDecontaminationArrival(WorldObject_Traveler traveler)
        {
            if (traveler == null) return;
            WorldObject origin = traveler.originObject;
            var comp = origin?.GetComponent<CompViralSpread>();
            if (comp == null || !HasActiveDecontaminationProject(comp)) return;

            int tile = traveler.Tile.tileId;
            ApplyPollutionReduction(tile);

            // Stay on this tile until pollution is gone; each visit only removes settings pp.
            if (!IsTilePolluted(tile))
            {
                if (comp.decontamWorkIndex < comp.decontamPlannedTiles.Count
                    && comp.decontamPlannedTiles[comp.decontamWorkIndex] == tile)
                {
                    comp.decontamWorkIndex++;
                }
                else
                {
                    int idx = comp.decontamPlannedTiles.IndexOf(tile);
                    if (idx >= 0)
                        comp.decontamWorkIndex = idx + 1;
                }
            }

            int next = GetCurrentWorkTile(comp);
            comp.decontamCachedWorkTile = next;
            if (next < 0)
                ClearDecontaminationProject(comp);
            else if (next != tile)
                comp.decontamCachedPathTiles?.Clear();
        }

        public static void ApplyPollutionReduction(int tileId)
        {
            var grid = Find.WorldGrid;
            if (grid == null || tileId < 0 || tileId >= grid.TilesCount) return;
            Tile tile = grid[tileId];
            var s = WorldDominationMod.settings;
            float amount = s != null
                ? s.GetDecontaminationPollutionReduction()
                : WorldDominationSettings.DefDecontaminationPollutionReductionPp * 0.01f;
            tile.pollution = Mathf.Clamp01(tile.pollution - amount);
            // Vanilla pollution mesh is region-cached; data change alone does not redraw (same as Toxic Fissure).
            Find.World?.renderer?.Notify_TilePollutionChanged(new PlanetTile(tileId));
            WD_WorldLayer_PollutionOverlay.InvalidateAndDirtyIfActive();
        }
    }
}
