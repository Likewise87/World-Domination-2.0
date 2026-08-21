using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Player AT Turret construction projects (multi-tile queue). Tier unlocked by Construction skill.</summary>
    public static class WorldActions_AtTurrets
    {
        public const int MinConstructionLight = WorldDominationSettings.DefAtTurretLightMinConstruction;
        public const int MinConstructionMedium = WorldDominationSettings.DefAtTurretMediumMinConstruction;
        public const int MinConstructionHeavy = WorldDominationSettings.DefAtTurretHeavyMinConstruction;

        public static int GetMinConstruction(AtTurretTier tier)
        {
            var s = WorldDominationMod.settings;
            if (s != null)
                return s.GetAtTurretMinConstruction(tier);
            switch (tier)
            {
                case AtTurretTier.Heavy: return MinConstructionHeavy;
                case AtTurretTier.Medium: return MinConstructionMedium;
                default: return MinConstructionLight;
            }
        }

        /// <summary>Localized label for the highest AT Turret tier this site can start (by Construction skill).</summary>
        public static string GetHighestBuildableTierLabel(float totalConstruction)
        {
            if (totalConstruction >= GetMinConstruction(AtTurretTier.Heavy))
                return AtTurretUtility.LabelKey(AtTurretTier.Heavy).Translate().ToString();
            if (totalConstruction >= GetMinConstruction(AtTurretTier.Medium))
                return AtTurretUtility.LabelKey(AtTurretTier.Medium).Translate().ToString();
            if (totalConstruction >= GetMinConstruction(AtTurretTier.Light))
                return AtTurretUtility.LabelKey(AtTurretTier.Light).Translate().ToString();
            return "TSA_WD_OutpostStats_Value_RoadNone".Translate().ToString();
        }

        public static bool MeetsConstructionRequirement(WorldObject actor, AtTurretTier tier)
        {
            if (actor == null) return false;
            float skill = ColonyWorldBuildUtility.GetActorConstructionSkillRaw(actor);
            return skill >= GetMinConstruction(tier);
        }

        public static float GetAtTurretProgressRequiredTicks(AtTurretTier tier)
        {
            float baseline = WorldActions_Roads.GetRoadProgressRequiredTicks(SettlementTier.T3);
            var s = WorldDominationMod.settings;
            float work = s != null ? s.GetAtTurretWork(tier) : WorldDominationSettings.DefAtTurretMediumWork;
            float refWork = s != null
                ? s.GetFallbackRoadWork(SettlementTier.T3)
                : WorldDominationSettings.DefFallbackAsphaltRoadWork;
            if (refWork < 1f) refWork = 1f;
            return baseline * (Mathf.Max(1f, work) / refWork);
        }

        public static float GetEstimatedDaysPerAtTurret(WorldObject actor, AtTurretTier tier)
        {
            if (actor == null) return -1f;
            float workSpeed = WorldActions_Roads.GetRoadProgressWorkSpeed(actor);
            if (workSpeed < 0.01f) return -1f;
            return GetAtTurretProgressRequiredTicks(tier) / workSpeed / GenDate.TicksPerDay;
        }

        public static float GetExpeditionStrengthCost(AtTurretTier tier)
        {
            var s = WorldDominationMod.settings;
            return Mathf.Max(1f, s != null ? s.GetAtTurretExpeditionStrength(tier) : WorldDominationSettings.DefAtTurretMediumExpeditionStrength);
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

        public static bool HasActiveAtTurretProject(CompViralSpread comp) =>
            comp?.atTurretPlannedTiles != null && comp.atTurretPlannedTiles.Count > 0;

        public static void ClearAtTurretProject(CompViralSpread comp)
        {
            if (comp == null) return;
            comp.atTurretPlannedTiles?.Clear();
            comp.atTurretWorkIndex = 0;
            comp.atTurretProgress = 0f;
            comp.atTurretBuilderInField = false;
            comp.atTurretCachedWorkTile = -1;
            comp.atTurretTargetName = string.Empty;
            comp.lastAtTurretProgressTick = -1;
        }

        public static bool IsValidBuildTile(int tileId, Faction builder) =>
            AtTurretUtility.IsPlayerBuildableTurretTile(tileId);

        public static Settlement ResolveBuiltBySettlement(WorldObject actor)
        {
            if (actor is Settlement s && s.Faction?.IsPlayer == true)
                return s;
            return InfluenceUtils.GetPlayerColony();
        }

        public static int GetCurrentWorkTile(CompViralSpread comp)
        {
            if (comp?.atTurretPlannedTiles == null) return -1;
            while (comp.atTurretWorkIndex < comp.atTurretPlannedTiles.Count)
            {
                int tile = comp.atTurretPlannedTiles[comp.atTurretWorkIndex];
                if (IsValidBuildTile(tile, comp.parent?.Faction))
                    return tile;
                comp.atTurretWorkIndex++;
            }
            return -1;
        }

        public static bool LaunchAtTurretCrewFromOutpost(WorldObject actor)
        {
            var comp = actor?.GetComponent<CompViralSpread>();
            if (!HasActiveAtTurretProject(comp)) return false;
            if (!AtTurretUtility.IsTierBuildable(comp.selectedAtTurretTier)) return false;

            int workTile = GetCurrentWorkTile(comp);
            comp.atTurretCachedWorkTile = workTile;
            if (workTile < 0)
            {
                ClearAtTurretProject(comp);
                return false;
            }

            Settlement owner = ResolveBuiltBySettlement(actor);
            if (owner == null || !AtTurretUtility.CanPlayerSiteBuildAnother(actor))
                return false;
            if (!ColonyWorldBuildRequirements.MeetsAtTurretRequirements(actor, comp.selectedAtTurretTier))
                return false;

            float cost = GetExpeditionStrengthCost(comp.selectedAtTurretTier);
            if (!WorldActions_Utils.CanAffordExpeditionLeavingGarrison(comp, cost)) return false;

            return SpawnAtTurretTraveler(actor, workTile, cost);
        }

        private static bool SpawnAtTurretTraveler(WorldObject origin, int destTile, float cost)
        {
            var comp = origin.GetComponent<CompViralSpread>();
            if (comp == null) return false;
            if (!WorldActions_Utils.TryConsumeExpeditionStrength(comp, cost)) return false;

            WorldObjectDef def = DefDatabase<WorldObjectDef>.GetNamedSilentFail("TSA_WD_Traveler_Outpost_RoadBlock")
                ?? DefDatabase<WorldObjectDef>.GetNamedSilentFail("TSA_WD_Traveler_Outpost_RoadBuilder");
            if (def == null)
            {
                WorldActions_Utils.RefundExpeditionStrength(comp, cost);
                return false;
            }

            WorldObject_Traveler traveler = (WorldObject_Traveler)WorldObjectMaker.MakeWorldObject(def);
            traveler.Tile = origin.Tile;
            traveler.SetFaction(origin.Faction);
            traveler.mission = TravelerMission.AtTurret;
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

        public static void ExecuteAtTurretArrival(WorldObject_Traveler traveler)
        {
            if (traveler == null) return;
            WorldObject origin = traveler.originObject;
            var comp = origin?.GetComponent<CompViralSpread>();
            if (comp == null || !HasActiveAtTurretProject(comp)) return;

            int tile = traveler.Tile.tileId;
            Faction faction = traveler.Faction ?? origin.Faction;
            Settlement builtBy = ResolveBuiltBySettlement(origin);
            AtTurretTier tier = comp.selectedAtTurretTier;

            // Caps and wipe require a live builder; never spawn uncapped orphans.
            if (builtBy != null
                && AtTurretUtility.IsTierBuildable(tier)
                && ColonyWorldBuildRequirements.MeetsAtTurretRequirements(origin, tier)
                && IsValidBuildTile(tile, faction)
                && AtTurretUtility.CanPlayerSiteAcceptPlacedTurret(origin))
            {
                WorldObject_AT_Turret turret = AtTurretUtility.TrySpawn(tile, faction, tier, builtBy, origin);
                if (turret != null)
                {
                    Find.World?.GetComponent<WorldComponent_SpreadManager>()?.AddLog(
                        new SpreadLogEntry(
                            "TSA_WD_Log_AT_TurretBuilt".Translate(origin.LabelCap, tile.ToString()),
                            origin,
                            tile));
                }
            }

            if (comp.atTurretWorkIndex < comp.atTurretPlannedTiles.Count
                && comp.atTurretPlannedTiles[comp.atTurretWorkIndex] == tile)
            {
                comp.atTurretWorkIndex++;
            }
            else
            {
                int idx = comp.atTurretPlannedTiles.IndexOf(tile);
                if (idx >= 0)
                    comp.atTurretWorkIndex = idx + 1;
            }

            int next = GetCurrentWorkTile(comp);
            comp.atTurretCachedWorkTile = next;
            if (next < 0)
                ClearAtTurretProject(comp);
        }

        public static bool HasActiveAtTurretCrewFrom(WorldObject origin)
        {
            if (origin == null || Find.WorldObjects == null) return false;
            foreach (var wo in Find.WorldObjects.AllWorldObjects)
            {
                if (wo is WorldObject_Traveler t && !t.Destroyed
                    && t.mission == TravelerMission.AtTurret
                    && t.originObject == origin)
                    return true;
            }
            return false;
        }

        public static void CommitAtTurretProject(CompViralSpread comp, AtTurretTier tier, List<int> plannedTiles)
        {
            if (comp == null || plannedTiles == null || plannedTiles.Count == 0) return;
            comp.selectedAtTurretTier = tier;
            comp.atTurretPlannedTiles = new List<int>(plannedTiles);
            comp.atTurretWorkIndex = 0;
            comp.atTurretProgress = 0f;
            comp.atTurretBuilderInField = false;
            comp.atTurretCachedWorkTile = GetCurrentWorkTile(comp);
            comp.atTurretTargetName = plannedTiles.Count == 1
                ? plannedTiles[0].ToString()
                : plannedTiles.Count + " tiles";
            comp.lastAtTurretProgressTick = -1;
        }
    }
}
