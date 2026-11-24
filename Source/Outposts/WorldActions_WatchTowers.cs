using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Player Watch Tower construction (Build & Scout). Stone-road duration; 300 WoodLog; 8-tile radius.</summary>
    public static class WorldActions_WatchTowers
    {
        public const float BuildRadiusTiles = 8f;
        public const float ExpeditionStrengthBase = 80f;
        /// <summary>Hard max Watch Towers per outpost or per player colony (placed + remaining planned).</summary>
        public const int MaxPerBuilder = 3;

        public static int CountBuiltBy(WorldObject site)
        {
            if (site == null) return 0;
            int n = 0;
            IReadOnlyList<WorldObject_WD_PlayerWatchTower> live = WorldObject_WD_PlayerWatchTower.LiveTowers;
            for (int i = 0; i < live.Count; i++)
            {
                WorldObject_WD_PlayerWatchTower t = live[i];
                if (t == null || t.Destroyed) continue;
                if (ReferenceEquals(t.builtBySite, site))
                    n++;
            }
            return n;
        }

        /// <summary>Tiles still in the active project queue (includes the current work tile).</summary>
        public static int CountRemainingPlanned(WorldObject site)
        {
            var comp = site?.GetComponent<CompViralSpread>();
            if (comp?.watchTowerPlannedTiles == null || comp.watchTowerPlannedTiles.Count == 0)
                return 0;
            int idx = Mathf.Clamp(comp.watchTowerWorkIndex, 0, comp.watchTowerPlannedTiles.Count);
            return comp.watchTowerPlannedTiles.Count - idx;
        }

        public static int CountUsedSlots(WorldObject site) =>
            CountBuiltBy(site) + CountRemainingPlanned(site);

        public static int RemainingSlots(WorldObject site) =>
            Mathf.Max(0, MaxPerBuilder - CountUsedSlots(site));

        public static bool CanBuildAnother(WorldObject site) =>
            site != null && RemainingSlots(site) > 0;

        public static bool CanAcceptPlaced(WorldObject site) =>
            site != null && CountBuiltBy(site) < MaxPerBuilder;

        public static string PlayerAtCapLabel(WorldObject site) =>
            "TSA_WD_PlayerWatchTower_AtCap".Translate(MaxPerBuilder).ToString();

        public static WorldObjectDef WatchTowerDef =>
            DefDatabase<WorldObjectDef>.GetNamedSilentFail(WorldObject_WD_PlayerWatchTower.DefName);

        public static WdWorldBuildCostExtension GetBuildExtension() =>
            WatchTowerDef?.GetModExtension<WdWorldBuildCostExtension>();

        public static int GetMinConstruction()
        {
            WdWorldBuildCostExtension ext = GetBuildExtension();
            if (ext != null && ext.minCumulativeConstructionSkill >= 0)
                return ext.minCumulativeConstructionSkill;
            return WorldActions_Roads.GetMinConstructionToBuildRoad(SettlementTier.T2);
        }

        public static float GetProgressRequiredTicks() =>
            WorldActions_Roads.GetRoadProgressRequiredTicks(SettlementTier.T2);

        public static float GetEstimatedDays(WorldObject actor)
        {
            if (actor == null) return -1f;
            float workSpeed = WorldActions_Roads.GetRoadProgressWorkSpeed(actor);
            if (workSpeed < 0.01f) return -1f;
            return GetProgressRequiredTicks() / workSpeed / GenDate.TicksPerDay;
        }

        public static float GetExpeditionStrengthCost()
        {
            var s = WorldDominationMod.settings;
            float cost = Mathf.Max(1f, ExpeditionStrengthBase);
            return s != null ? s.ScaleWorldBuildStrengthCost(cost) : cost;
        }

        public static float GetMaxRange(WorldObject source)
        {
            float range = BuildRadiusTiles;
            if (source is WorldObject_WD_Outpost wdOutpost)
                range *= 1f + OutpostExpertUtility.GetEngineerConstructionRadiusBonus(wdOutpost);
            return range;
        }

        public static bool HasActiveWatchTowerProject(CompViralSpread comp) =>
            comp?.watchTowerPlannedTiles != null && comp.watchTowerPlannedTiles.Count > 0;

        public static void ClearWatchTowerProject(CompViralSpread comp)
        {
            if (comp == null) return;
            DestroyActiveCrewsFrom(comp.parent);
            comp.watchTowerPlannedTiles?.Clear();
            comp.watchTowerWorkIndex = 0;
            comp.watchTowerProgress = 0f;
            comp.watchTowerBuilderInField = false;
            comp.watchTowerCachedWorkTile = -1;
            comp.watchTowerTargetName = string.Empty;
            comp.lastWatchTowerProgressTick = -1;
            WorldConstructionProjectRegistry.NotifyWatchTowerChanged(comp);
        }

        public static void DestroyActiveCrewsFrom(WorldObject origin)
        {
            if (origin == null) return;
            IReadOnlyList<WorldObject_Traveler> live = WorldObject_Traveler.LiveTravelers;
            for (int wi = live.Count - 1; wi >= 0; wi--)
            {
                WorldObject_Traveler t = live[wi];
                if (t != null
                    && t.mission == TravelerMission.WatchTower
                    && t.originObject == origin
                    && !t.Destroyed)
                {
                    ColonyWorldBuildRequirements.RefundConstructionAbort(t);
                    t.Destroy();
                }
            }
        }

        /// <summary>Physical eligibility (no project-queue lock). Used by spawn after arrival already validated the queue.</summary>
        public static bool IsBaseEligibleBuildTile(int tileId)
        {
            if (tileId < 0) return false;
            if (WorldComponent_WdBridges.IsBridgedWaterTile(tileId)) return false;
            if (WorldComponent_WdTrenches.HasExclusiveTrenchAt(tileId)) return false;
            if (!WorldActions_RoadBlocks.IsTileBaseEligibleForRoadBlock(tileId)) return false;
            if (WorldComponent_RoadBlocks.Get()?.HasBlockAt(tileId) == true) return false;
            if (WorldComponent_SpikeTraps.Get()?.HasTrapAt(tileId) == true) return false;
            if (AtTurretUtility.TileHasAtTurret(tileId)) return false;
            if (WorldComponent_AdvancedBuildings.Get()?.HasAt(tileId) == true) return false;
            if (WorldObject_WD_PlayerWatchTower.FindAt(tileId) != null) return false;
            return true;
        }

        public static bool IsValidBuildTile(int tileId, Faction builder, CompViralSpread exclude = null) =>
            IsBaseEligibleBuildTile(tileId) && !IsWatchTowerTileProjectLocked(tileId, exclude);

        public static bool IsWatchTowerTileProjectLocked(int tileId, CompViralSpread exclude = null)
        {
            if (tileId < 0) return false;
            IReadOnlyList<CompViralSpread> projects = WorldConstructionProjectRegistry.ActiveWatchTowerProjects;
            for (int i = 0; i < projects.Count; i++)
            {
                CompViralSpread c = projects[i];
                if (c == null || c == exclude) continue;
                if (c.parent == null || c.parent.Destroyed) continue;
                List<int> planned = c.watchTowerPlannedTiles;
                if (planned == null) continue;
                for (int j = 0; j < planned.Count; j++)
                {
                    if (planned[j] == tileId)
                        return true;
                }
            }
            return false;
        }

        public static int GetCurrentWorkTile(CompViralSpread comp)
        {
            if (comp?.watchTowerPlannedTiles == null) return -1;
            while (comp.watchTowerWorkIndex < comp.watchTowerPlannedTiles.Count)
            {
                int tile = comp.watchTowerPlannedTiles[comp.watchTowerWorkIndex];
                if (IsValidBuildTile(tile, comp.parent?.Faction, comp))
                    return tile;
                comp.watchTowerWorkIndex++;
            }
            return -1;
        }

        public static bool LaunchCrewFromOutpost(WorldObject actor)
        {
            var comp = actor?.GetComponent<CompViralSpread>();
            if (!HasActiveWatchTowerProject(comp)) return false;

            int workTile = GetCurrentWorkTile(comp);
            comp.watchTowerCachedWorkTile = workTile;
            if (workTile < 0)
            {
                ClearWatchTowerProject(comp);
                return false;
            }

            if (!ColonyWorldBuildRequirements.MeetsWatchTowerRequirements(actor))
                return false;
            if (ColonyWorldBuildRequirements.ActorPaysWorldBuildMaterials(actor)
                && !ColonyWorldBuildRequirements.HasMaterialCostsForWatchTower())
                return false;

            float cost = GetExpeditionStrengthCost();
            if (ColonyWorldBuildUtility.IsPlayerColonyBuildActor(actor))
                cost = 0f;
            else if (!WorldActions_Utils.CanAffordExpeditionLeavingGarrison(comp, cost))
                return false;

            return SpawnCrewTraveler(actor, workTile, cost);
        }

        private static bool SpawnCrewTraveler(WorldObject origin, int destTile, float cost)
        {
            var comp = origin.GetComponent<CompViralSpread>();
            if (comp == null) return false;
            if (cost > 0f && !WorldActions_Utils.TryConsumeExpeditionStrength(comp, cost))
                return false;

            WorldObjectDef def = DefDatabase<WorldObjectDef>.GetNamedSilentFail("TSA_WD_Traveler_Outpost_RoadBlock")
                ?? DefDatabase<WorldObjectDef>.GetNamedSilentFail("TSA_WD_Traveler_Outpost_RoadBuilder");
            if (def == null)
            {
                if (cost > 0f)
                    WorldActions_Utils.RefundExpeditionStrength(comp, cost);
                return false;
            }

            WorldObject_Traveler traveler = (WorldObject_Traveler)WorldObjectMaker.MakeWorldObject(def);
            traveler.Tile = origin.Tile;
            traveler.SetFaction(origin.Faction);
            traveler.mission = TravelerMission.WatchTower;
            traveler.originObject = origin;
            traveler.travelerStrength = Mathf.Max(1f, cost);
            traveler.initialStrength = traveler.travelerStrength;

            Find.WorldObjects.Add(traveler);
            traveler.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(destTile, origin));
            if (traveler.Destroyed)
            {
                if (cost > 0f)
                    WorldActions_Utils.RefundExpeditionStrength(comp, cost);
                return false;
            }

            List<OutpostUpgradeCostEntry> materials = ColonyWorldBuildRequirements.GetMaterialCostsForWatchTower();
            return ColonyWorldBuildRequirements.TryFinalizeMaterialsOrAbort(origin, traveler, cost, materials);
        }

        public static void ExecuteArrival(WorldObject_Traveler traveler)
        {
            if (traveler == null) return;
            WorldObject origin = traveler.originObject;
            var comp = origin?.GetComponent<CompViralSpread>();
            if (comp == null || !HasActiveWatchTowerProject(comp)) return;

            int tile = traveler.Tile.tileId;
            Faction faction = traveler.Faction ?? origin.Faction;

            if (faction != null
                && ColonyWorldBuildRequirements.MeetsWatchTowerRequirements(origin)
                && CanAcceptPlaced(origin)
                && IsValidBuildTile(tile, faction, comp))
            {
                WorldObject_WD_PlayerWatchTower tower = WorldObject_WD_PlayerWatchTower.TrySpawn(tile, faction, origin);
                if (tower != null)
                {
                    traveler.MarkConstructionWorkApplied();
                    Outpost_ConstructionXp.TryGrant(traveler, Outpost_ConstructionXp.XpForRoadTier(SettlementTier.T2));
                    Find.World?.GetComponent<WorldComponent_SpreadManager>()?.AddLog(
                        new SpreadLogEntry(
                            "TSA_WD_Log_PlayerWatchTowerBuilt".Translate(origin.LabelCap, tile.ToString()),
                            origin,
                            tile) { category = SpreadLogCategory.BuiltFortification });
                }
            }

            if (comp.watchTowerWorkIndex < comp.watchTowerPlannedTiles.Count
                && comp.watchTowerPlannedTiles[comp.watchTowerWorkIndex] == tile)
            {
                comp.watchTowerWorkIndex++;
            }
            else
            {
                int idx = comp.watchTowerPlannedTiles.IndexOf(tile);
                if (idx >= 0)
                    comp.watchTowerWorkIndex = idx + 1;
            }

            int next = GetCurrentWorkTile(comp);
            comp.watchTowerCachedWorkTile = next;
            if (next < 0)
                ClearWatchTowerProject(comp);
        }

        public static bool HasActiveCrewFrom(WorldObject origin)
        {
            if (origin == null) return false;
            IReadOnlyList<WorldObject_Traveler> live = WorldObject_Traveler.LiveTravelers;
            for (int i = 0; i < live.Count; i++)
            {
                WorldObject_Traveler t = live[i];
                if (t != null && !t.Destroyed
                    && t.mission == TravelerMission.WatchTower
                    && t.originObject == origin)
                    return true;
            }
            return false;
        }

        public static void CommitProject(CompViralSpread comp, List<int> plannedTiles)
        {
            if (comp == null || plannedTiles == null || plannedTiles.Count == 0) return;
            WorldObject origin = comp.parent;
            // New project replaces any prior queue; slots = max - placed.
            int room = origin != null
                ? Mathf.Max(0, MaxPerBuilder - CountBuiltBy(origin))
                : MaxPerBuilder;
            if (room <= 0) return;
            if (plannedTiles.Count > room)
                plannedTiles = plannedTiles.GetRange(0, room);
            comp.watchTowerPlannedTiles = new List<int>(plannedTiles);
            comp.watchTowerWorkIndex = 0;
            comp.watchTowerProgress = 0f;
            comp.watchTowerBuilderInField = false;
            comp.watchTowerCachedWorkTile = GetCurrentWorkTile(comp);
            comp.watchTowerTargetName = plannedTiles.Count == 1
                ? plannedTiles[0].ToString()
                : plannedTiles.Count + " tiles";
            comp.lastWatchTowerProgressTick = -1;
            WorldConstructionProjectRegistry.NotifyWatchTowerChanged(comp);
        }

        /// <summary>Hostile ground traveler or player caravan smash on this tile.</summary>
        public static void TryResolveWalkOver(WorldObject_Traveler traveler, PlanetTile tile)
        {
            if (traveler == null || traveler.Destroyed || !tile.Valid) return;
            if (traveler.Faction == null) return;
            if (WorldObject_Traveler.IsShellMission(traveler.mission)
                || traveler.mission == TravelerMission.RaidDropPod
                || traveler.mission == TravelerMission.RaidGravship
                || traveler.mission == TravelerMission.RapidResponseDropPod)
                return;

            WorldObject_WD_PlayerWatchTower wt = WorldObject_WD_PlayerWatchTower.FindAt(tile.tileId);
            if (wt == null || wt.Destroyed || wt.Faction == null) return;
            if (traveler.targetObject == wt) return;
            if (!WorldActions_Utils.SafeHostileTo(traveler.Faction, wt.Faction)) return;

            DestroyTower(wt, traveler);
        }

        public static bool TryOverrunByHostilePlayer(int tileId, Faction playerFaction, WorldObject logSubject)
        {
            if (tileId < 0 || playerFaction == null || !playerFaction.IsPlayer) return false;
            WorldObject_WD_PlayerWatchTower wt = WorldObject_WD_PlayerWatchTower.FindAt(tileId);
            if (wt == null || wt.Destroyed || wt.Faction == null || wt.Faction.IsPlayer) return false;
            if (!WorldActions_Utils.SafeHostileTo(playerFaction, wt.Faction)) return false;
            DestroyTower(wt, logSubject);
            return true;
        }

        public static void DestroyTower(WorldObject_WD_PlayerWatchTower wt, WorldObject attacker)
        {
            if (wt == null || wt.Destroyed) return;
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (manager != null)
            {
                string text = attacker != null
                    ? "TSA_WD_PlayerWatchTower_Log_DestroyedBy".Translate(wt.LabelCap, WdPlayerIntel.LabelForLetter(attacker)).ToString()
                    : "TSA_WD_PlayerWatchTower_Log_Destroyed".Translate(wt.LabelCap).ToString();
                SpreadLogEntry entry = attacker != null
                    ? new SpreadLogEntry(text, attacker, wt.Tile.tileId)
                    : new SpreadLogEntry(text, null, wt.Tile.tileId);
                entry.category = SpreadLogCategory.BuiltFortification;
                manager.AddLog(entry);
            }
            wt.Destroy();
        }

        /// <summary>
        /// Quiet-destroy all Watch Towers owned by this builder and cancel its Watch Tower project/crews.
        /// Call from real outpost/colony teardown only (skip Path B type-change shell removal).
        /// </summary>
        public static void WipeForBuilder(WorldObject builder)
        {
            if (builder == null) return;

            IReadOnlyList<WorldObject_WD_PlayerWatchTower> live = WorldObject_WD_PlayerWatchTower.LiveTowers;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                WorldObject_WD_PlayerWatchTower t = live[i];
                if (t == null || t.Destroyed) continue;
                if (!ReferenceEquals(t.builtBySite, builder)) continue;
                t.Destroy();
            }

            var comp = builder.GetComponent<CompViralSpread>();
            if (HasActiveWatchTowerProject(comp))
                ClearWatchTowerProject(comp);
            else
                DestroyActiveCrewsFrom(builder);

            PlayerWatchTowerSelectionLines.ClearHighlight(builder);
        }

        /// <summary>Quiet-destroy towers whose builder is missing (pre-ownership saves / races). Never call during type-change shell removal.</summary>
        public static void WipeOrphans()
        {
            IReadOnlyList<WorldObject_WD_PlayerWatchTower> live = WorldObject_WD_PlayerWatchTower.LiveTowers;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                WorldObject_WD_PlayerWatchTower t = live[i];
                if (t == null || t.Destroyed) continue;
                if (t.builtBySite != null && !t.builtBySite.Destroyed) continue;
                t.Destroy();
            }
        }
    }
}
