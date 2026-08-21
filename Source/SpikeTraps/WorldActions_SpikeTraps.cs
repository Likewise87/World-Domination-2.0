using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public static class WorldActions_SpikeTraps
    {
        public static float GetSpikeTrapProgressRequiredTicks(SpikeTrapKind kind)
        {
            SettlementTier baselineTier = SpikeTrapKindUtil.WorkBaselineTier(kind);
            float baseline = WorldActions_Roads.GetRoadProgressRequiredTicks(baselineTier);
            var s = WorldDominationMod.settings;
            float work = s != null ? s.GetSpikeTrapWork(kind) : WorldDominationSettings.DefSpikeTrapSpikeWork;
            float refWork = s != null
                ? s.GetFallbackRoadWork(baselineTier)
                : WorldDominationSettings.DefFallbackDirtRoadWork;
            if (refWork < 1f) refWork = 1f;
            return baseline * (Mathf.Max(1f, work) / refWork);
        }

        public static float GetSpikeTrapProgressRequiredTicks()
        {
            return GetSpikeTrapProgressRequiredTicks(SpikeTrapKind.Spike);
        }

        /// <summary>Estimated in-game days to fill progress for one spike-trap segment at current Construction skill. Excludes caravan travel.</summary>
        public static float GetEstimatedDaysPerSpikeTrapSegment(WorldObject actor, SpikeTrapKind kind)
        {
            if (actor == null) return -1f;
            float workSpeed = WorldActions_Roads.GetRoadProgressWorkSpeed(actor);
            if (workSpeed < 0.01f) return -1f;
            float ticks = GetSpikeTrapProgressRequiredTicks(kind) / workSpeed;
            return ticks / GenDate.TicksPerDay;
        }

        public static float GetEstimatedDaysPerSpikeTrapSegment(WorldObject_WD_Outpost outpost, SpikeTrapKind kind) =>
            GetEstimatedDaysPerSpikeTrapSegment((WorldObject)outpost, kind);

        public static float GetEstimatedDaysPerSpikeTrapSegment(WorldObject_WD_Outpost outpost)
        {
            return GetEstimatedDaysPerSpikeTrapSegment(outpost, SpikeTrapKind.Spike);
        }

        public static float GetExpeditionStrengthCost(SpikeTrapKind kind)
        {
            var s = WorldDominationMod.settings;
            return Mathf.Max(1f, s != null ? s.GetSpikeTrapExpeditionStrength(kind) : WorldDominationSettings.DefSpikeTrapSpikeExpeditionStrength);
        }

        public static float GetExpeditionStrengthCost()
        {
            return GetExpeditionStrengthCost(SpikeTrapKind.Spike);
        }

        public static int GetMinConstruction(SpikeTrapKind kind)
        {
            return WorldActions_Roads.GetMinConstructionToBuildRoad(SpikeTrapKindUtil.WorkBaselineTier(kind));
        }

        /// <summary>Localized label for the highest trap kind this site can start (by Construction skill).</summary>
        public static string GetHighestBuildableKindLabel(float totalConstruction)
        {
            if (totalConstruction >= GetMinConstruction(SpikeTrapKind.Caltrops))
                return SpikeTrapKindUtil.LabelKey(SpikeTrapKind.Caltrops).Translate().ToString();
            return SpikeTrapKindUtil.LabelKey(SpikeTrapKind.Spike).Translate().ToString();
        }

        public static float GetMaxRange()
        {
            var s = WorldDominationMod.settings;
            return s != null ? s.maxSpikeTrapRange : WorldDominationSettings.DefMaxSpikeTrapRange;
        }

        public static float GetMaxRange(WorldObject source)
        {
            float range = GetMaxRange();
            if (source is WorldObject_WD_Outpost wdOutpost)
                range *= 1f + OutpostExpertUtility.GetEngineerConstructionRadiusBonus(wdOutpost);
            return range;
        }

        public static bool HasActiveSpikeTrapProject(CompViralSpread comp)
        {
            return comp != null && comp.spikeTrapPlannedTiles != null && comp.spikeTrapPlannedTiles.Count > 0;
        }

        public static bool IsTileBaseEligibleForSpikeTrap(int tileId)
        {
            return WorldActions_RoadBlocks.IsTileBaseEligibleForRoadBlock(tileId);
        }

        public static bool IsValidBuildTile(int tileId, SpikeTrapKind kind, Faction builder = null)
        {
            if (!IsTileBaseEligibleForSpikeTrap(tileId)) return false;
            // Road blocks (any owner) are cleared on place; they must not block planning or arrival.

            WorldComponent_SpikeTraps traps = WorldComponent_SpikeTraps.Get();
            if (traps == null) return true;
            if (!traps.TryGet(tileId, out SpikeTrapRecord existing) || existing == null)
                return true;
            if (SpikeTrapKindUtil.CanUpgradeTo(existing.kind, kind))
                return true;
            return WorldActions_RoadBlocks.CanClaimHostileFortification(builder, existing.builtByFaction);
        }

        public static bool IsValidBuildTile(int tileId)
        {
            return IsValidBuildTile(tileId, SpikeTrapKind.Spike);
        }

        public static bool IsValidClearTile(int tileId)
        {
            WorldComponent_SpikeTraps traps = WorldComponent_SpikeTraps.Get();
            return traps != null && traps.HasTrapAt(tileId);
        }

        public static bool ClearIfPresent(int tileId)
        {
            WorldComponent_SpikeTraps traps = WorldComponent_SpikeTraps.Get();
            return traps != null && traps.TryClear(tileId);
        }

        public static bool IsValidBuildPlanNode(int tileId)
        {
            return IsTileBaseEligibleForSpikeTrap(tileId);
        }

        public static List<int> FilterPlannedTilesFromClickedNodes(List<int> clickedNodes, bool clearing, SpikeTrapKind kind, Faction builder = null)
        {
            var result = new List<int>();
            if (clickedNodes == null || clickedNodes.Count == 0) return result;

            var seen = new HashSet<int>();
            void TryAdd(int tile)
            {
                if (!seen.Add(tile)) return;
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

        public static List<int> FilterPlannedTilesFromClickedNodes(List<int> clickedNodes, bool clearing)
        {
            return FilterPlannedTilesFromClickedNodes(clickedNodes, clearing, SpikeTrapKind.Spike);
        }

        public static void ClearSpikeTrapProject(CompViralSpread comp)
        {
            if (comp == null) return;
            DestroyActiveSpikeTrapCrewsFrom(comp.parent);
            comp.spikeTrapPlannedTiles?.Clear();
            comp.spikeTrapClickedNodes?.Clear();
            comp.spikeTrapCachedPathTiles?.Clear();
            comp.spikeTrapProgress = 0f;
            comp.spikeTrapWorkIndex = 0;
            comp.spikeTrapCachedWorkTile = -1;
            comp.spikeTrapIsClearing = false;
            comp.spikeTrapTargetName = string.Empty;
            comp.selectedSpikeTrapKind = SpikeTrapKind.Spike;
            comp.NotifySpikeTrapCrewReturned();
        }

        public static void DestroyActiveSpikeTrapCrewsFrom(WorldObject origin)
        {
            if (origin == null || Find.WorldObjects == null) return;
            var allWo = Find.WorldObjects.AllWorldObjects;
            for (int wi = allWo.Count - 1; wi >= 0; wi--)
            {
                if (allWo[wi] is WorldObject_Traveler t
                    && t.mission == TravelerMission.SpikeTrap
                    && t.originObject == origin
                    && !t.Destroyed)
                {
                    t.Destroy();
                }
            }
        }

        public static bool HasActiveSpikeTrapCrewFrom(WorldObject origin)
        {
            if (origin == null || Find.WorldObjects == null) return false;
            foreach (var wo in Find.WorldObjects.AllWorldObjects)
            {
                if (wo is WorldObject_Traveler t && !t.Destroyed
                    && t.mission == TravelerMission.SpikeTrap
                    && t.originObject == origin)
                    return true;
            }
            return false;
        }

        public static int GetCurrentWorkTile(CompViralSpread comp)
        {
            if (comp?.spikeTrapPlannedTiles == null) return -1;
            SpikeTrapKind kind = comp.selectedSpikeTrapKind;
            Faction builder = comp.parent?.Faction;
            while (comp.spikeTrapWorkIndex < comp.spikeTrapPlannedTiles.Count)
            {
                int tile = comp.spikeTrapPlannedTiles[comp.spikeTrapWorkIndex];
                if (comp.spikeTrapIsClearing)
                {
                    if (IsValidClearTile(tile))
                        return tile;
                }
                else if (IsValidBuildTile(tile, kind, builder))
                {
                    return tile;
                }
                comp.spikeTrapWorkIndex++;
            }
            return -1;
        }

        public static bool LaunchSpikeTrapCrewFromOutpost(WorldObject actor)
        {
            var comp = actor?.GetComponent<CompViralSpread>();
            if (!HasActiveSpikeTrapProject(comp)) return false;

            int workTile = GetCurrentWorkTile(comp);
            comp.spikeTrapCachedWorkTile = workTile;
            if (workTile < 0)
            {
                ClearSpikeTrapProject(comp);
                return false;
            }

            if (!ColonyWorldBuildRequirements.MeetsSpikeTrapRequirements(actor, comp.selectedSpikeTrapKind))
                return false;

            float cost = GetExpeditionStrengthCost(comp.selectedSpikeTrapKind);
            if (!WorldActions_Utils.CanAffordExpeditionLeavingGarrison(comp, cost)) return false;

            return SpawnSpikeTrapTraveler(actor, workTile, cost);
        }

        private static bool SpawnSpikeTrapTraveler(WorldObject origin, int destTile, float cost)
        {
            var comp = origin.GetComponent<CompViralSpread>();
            if (comp == null) return false;
            if (!WorldActions_Utils.TryConsumeExpeditionStrength(comp, cost)) return false;

            WorldObjectDef def = DefDatabase<WorldObjectDef>.GetNamed("TSA_WD_Traveler_Outpost_SpikeTrap", false)
                ?? DefDatabase<WorldObjectDef>.GetNamed("TSA_WD_Traveler_Outpost_RoadBlock", false)
                ?? DefDatabase<WorldObjectDef>.GetNamed("TSA_WD_Traveler_Outpost_RoadBuilder", false);
            if (def == null)
            {
                WorldActions_Utils.RefundExpeditionStrength(comp, cost);
                return false;
            }

            WorldObject_Traveler traveler = (WorldObject_Traveler)WorldObjectMaker.MakeWorldObject(def);
            traveler.Tile = origin.Tile;
            traveler.SetFaction(origin.Faction);
            traveler.mission = TravelerMission.SpikeTrap;
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

        public static void ExecuteSpikeTrapArrival(WorldObject_Traveler traveler)
        {
            if (traveler == null) return;
            WorldObject origin = traveler.originObject;
            var comp = origin?.GetComponent<CompViralSpread>();
            if (comp == null || !HasActiveSpikeTrapProject(comp)) return;

            int tile = traveler.Tile.tileId;
            if (comp.spikeTrapIsClearing)
            {
                if (IsValidClearTile(tile))
                    WorldComponent_SpikeTraps.Get()?.TryClear(tile);
            }
            else
            {
                Faction builder = traveler.Faction ?? origin.Faction;
                if (IsValidBuildTile(tile, comp.selectedSpikeTrapKind, builder))
                    WorldComponent_SpikeTraps.Get()?.TryPlaceOrUpgrade(tile, builder, comp.selectedSpikeTrapKind);
            }

            if (comp.spikeTrapWorkIndex < comp.spikeTrapPlannedTiles.Count
                && comp.spikeTrapPlannedTiles[comp.spikeTrapWorkIndex] == tile)
            {
                comp.spikeTrapWorkIndex++;
            }
            else
            {
                int idx = comp.spikeTrapPlannedTiles.IndexOf(tile);
                if (idx >= 0)
                    comp.spikeTrapWorkIndex = idx + 1;
            }

            int next = GetCurrentWorkTile(comp);
            comp.spikeTrapCachedWorkTile = next;
            if (next < 0)
                ClearSpikeTrapProject(comp);
            else
                comp.spikeTrapCachedPathTiles?.Clear();
        }
    }
}
