using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Player powerline construction. Built like the other world buildings: progress accrues on the outpost from its
    /// Construction skill, then a crew traveler carrying expedition strength and the materials walks to the tile and
    /// raises the tower. One powerline per outpost, no two powerlines within <see cref="MinSpacingTiles"/>, and the tile
    /// must already be inside the power range (plant range or another powerline's radius) so coverage has no gaps.
    /// </summary>
    public static class WorldActions_Powerline
    {
        public const string DefName = "TSA_WD_Powerline";

        /// <summary>No other powerline (built or planned) within this many tiles.</summary>
        public const float MinSpacingTiles = 5f;

        public static WorldObjectDef PowerlineDef => DefDatabase<WorldObjectDef>.GetNamedSilentFail(DefName);

        private static WdWorldBuildCostExtension BuildExtension => PowerlineDef?.GetModExtension<WdWorldBuildCostExtension>();

        public static int GetMinConstruction()
        {
            WdWorldBuildCostExtension ext = BuildExtension;
            return ext != null && ext.minCumulativeConstructionSkill >= 0 ? ext.minCumulativeConstructionSkill : 0;
        }

        public static ResearchProjectDef GetRequiredResearch()
        {
            List<ResearchProjectDef> list = BuildExtension?.researchPrerequisites;
            if (list == null) return null;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null) return list[i];
            }
            return null;
        }

        public static List<OutpostUpgradeCostEntry> GetMaterialCosts()
        {
            List<OutpostUpgradeCostEntry> cost = BuildExtension?.cost;
            return cost != null && cost.Count > 0 ? cost : ColonyWorldBuildMaterials.EmptyCostList;
        }

        public static bool HasMaterials() => ColonyWorldBuildMaterials.HasMaterialCosts(GetMaterialCosts());

        public static bool MeetsRequirements(WorldObject actor)
        {
            if (actor == null) return false;
            return ColonyWorldBuildRequirements.MeetsConstruction(ColonyWorldBuildUtility.GetActorConstructionSkillRaw(actor), GetMinConstruction())
                && ColonyWorldBuildRequirements.IsResearchMet(GetRequiredResearch());
        }

        public static float GetProgressRequiredTicks()
        {
            float baseline = WorldActions_Roads.GetRoadProgressRequiredTicks(SettlementTier.T3);
            var s = WorldDominationMod.settings;
            float work = s != null ? s.powerlineWork : WorldDominationSettings.DefPowerlineWork;
            float refWork = s != null
                ? s.GetFallbackRoadWork(SettlementTier.T3)
                : WorldDominationSettings.DefFallbackAsphaltRoadWork;
            if (refWork < 1f) refWork = 1f;
            return baseline * (Mathf.Max(1f, work) / refWork);
        }

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
            return Mathf.Max(1f, s != null ? s.powerlineExpeditionStrength : WorldDominationSettings.DefPowerlineExpeditionStrength);
        }

        // --- project state ---

        public static bool HasActiveProject(CompViralSpread comp) => comp != null && comp.powerlinePlannedTile >= 0;

        public static void ClearProject(CompViralSpread comp)
        {
            if (comp == null) return;
            DestroyActiveCrewsFrom(comp.parent);
            comp.powerlinePlannedTile = -1;
            comp.powerlineProgress = 0f;
            comp.powerlineBuilderInField = false;
            comp.lastPowerlineProgressTick = -1;
        }

        public static void CommitProject(CompViralSpread comp, int tile)
        {
            if (comp == null || tile < 0) return;
            comp.powerlinePlannedTile = tile;
            comp.powerlineProgress = 0f;
            comp.powerlineBuilderInField = false;
            comp.lastPowerlineProgressTick = -1;
        }

        public static void DestroyActiveCrewsFrom(WorldObject origin)
        {
            if (origin == null) return;
            IReadOnlyList<WorldObject_Traveler> live = WorldObject_Traveler.LiveTravelers;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                WorldObject_Traveler t = live[i];
                if (t != null && t.mission == TravelerMission.PowerLine && t.originObject == origin && !t.Destroyed)
                {
                    ColonyWorldBuildRequirements.RefundConstructionAbort(t);
                    t.Destroy();
                }
            }
        }

        public static bool HasActiveCrewFrom(WorldObject origin)
        {
            if (origin == null) return false;
            IReadOnlyList<WorldObject_Traveler> live = WorldObject_Traveler.LiveTravelers;
            for (int i = 0; i < live.Count; i++)
            {
                WorldObject_Traveler t = live[i];
                if (t != null && !t.Destroyed && t.mission == TravelerMission.PowerLine && t.originObject == origin)
                    return true;
            }
            return false;
        }

        // --- placement rules ---

        /// <summary>True when this outpost already supports a powerline: one built by it, or a project in progress.</summary>
        public static bool OutpostHasPowerline(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return false;
            if (HasActiveProject(outpost.GetComponent<CompViralSpread>())) return true;
            List<WorldObject> all = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is WorldObject_WD_Powerline line && !line.Destroyed && line.builtByOutpostId == outpost.ID)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Whether a powerline may be raised on <paramref name="tile"/>. <paramref name="exclude"/> is the project's own comp,
        /// so its own planned tile does not count against it. <paramref name="reasonKey"/> is a translation key when false.
        /// </summary>
        public static bool IsValidBuildTile(int tile, CompViralSpread exclude, out string reasonKey)
        {
            reasonKey = "TSA_WD_Powerline_InvalidTile";
            WorldGrid grid = Find.WorldGrid;
            if (tile < 0 || grid == null || !grid.InBounds(tile)) return false;
            if (!PlanetSurfaceWorldActions.TryGetPlanetSurfaceLayer(out PlanetLayer surface)
                || !PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(new PlanetTile(tile, surface)))
                return false;
            if (Find.World.Impassable(new PlanetTile(tile, surface))) return false;
            if (!WorldComponent_WdBridges.IsBridgedWaterTile(tile) && grid[tile].WaterCovered) return false;
            foreach (WorldObject o in Find.WorldObjects.ObjectsAt(tile))
            {
                if (o is WorldObject_WD_Powerline || o is Settlement || o is WorldObject_WD_Outpost)
                    return false;
            }

            if (HasPowerlineWithin(tile, MinSpacingTiles, exclude))
            {
                reasonKey = "TSA_WD_Powerline_TooClose";
                return false;
            }

            if (!OutpostPowerGrid.IsTileInPowerRange(tile))
            {
                reasonKey = "TSA_WD_Powerline_NeedsPowerRange";
                return false;
            }

            reasonKey = null;
            return true;
        }

        /// <summary>Built powerlines, and planned tiles of other outposts' powerline projects, within <paramref name="tiles"/> of the tile.</summary>
        private static bool HasPowerlineWithin(int tile, float tiles, CompViralSpread exclude)
        {
            WorldGrid grid = Find.WorldGrid;
            List<WorldObject> all = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is WorldObject_WD_Powerline line && !line.Destroyed
                    && grid.ApproxDistanceInTiles(tile, line.Tile) <= tiles)
                    return true;
            }

            IReadOnlyList<WorldObject_WD_Outpost> outposts = WdPlayerOutpostCache.PlayerOutposts;
            for (int i = 0; i < outposts.Count; i++)
            {
                WorldObject_WD_Outpost o = outposts[i];
                if (o == null || o.Destroyed) continue;
                CompViralSpread c = o.GetComponent<CompViralSpread>();
                if (c == null || c == exclude || !HasActiveProject(c)) continue;
                if (grid.ApproxDistanceInTiles(tile, c.powerlinePlannedTile) <= tiles)
                    return true;
            }
            return false;
        }

        // --- crew ---

        public static bool LaunchCrewFromOutpost(WorldObject actor)
        {
            CompViralSpread comp = actor?.GetComponent<CompViralSpread>();
            if (!HasActiveProject(comp)) return false;

            if (!IsValidBuildTile(comp.powerlinePlannedTile, comp, out _))
            {
                ClearProject(comp);
                return false;
            }
            if (!MeetsRequirements(actor) || !HasMaterials()) return false;

            float cost = GetExpeditionStrengthCost();
            if (!WorldActions_Utils.CanAffordExpeditionLeavingGarrison(comp, cost)) return false;

            return SpawnTraveler(actor, comp.powerlinePlannedTile, cost);
        }

        private static bool SpawnTraveler(WorldObject origin, int destTile, float cost)
        {
            CompViralSpread comp = origin.GetComponent<CompViralSpread>();
            if (comp == null) return false;
            if (!WorldActions_Utils.TryConsumeExpeditionStrength(comp, cost)) return false;

            WorldObjectDef def = DefDatabase<WorldObjectDef>.GetNamedSilentFail("TSA_WD_Traveler_Outpost_RoadBlock")
                ?? DefDatabase<WorldObjectDef>.GetNamedSilentFail("TSA_WD_Traveler_Outpost_RoadBuilder");
            if (def == null)
            {
                WorldActions_Utils.RefundExpeditionStrength(comp, cost);
                return false;
            }

            var traveler = (WorldObject_Traveler)WorldObjectMaker.MakeWorldObject(def);
            traveler.Tile = origin.Tile;
            traveler.SetFaction(origin.Faction);
            traveler.mission = TravelerMission.PowerLine;
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

            return ColonyWorldBuildRequirements.TryFinalizeMaterialsOrAbort(origin, traveler, cost, GetMaterialCosts());
        }

        public static void ExecuteArrival(WorldObject_Traveler traveler)
        {
            if (traveler == null) return;
            WorldObject origin = traveler.originObject;
            CompViralSpread comp = origin?.GetComponent<CompViralSpread>();
            if (comp == null || !HasActiveProject(comp)) return;

            int tile = traveler.Tile.tileId;
            if (tile == comp.powerlinePlannedTile
                && MeetsRequirements(origin)
                && IsValidBuildTile(tile, comp, out _))
            {
                if (Spawn(tile, origin as WorldObject_WD_Outpost) != null)
                {
                    traveler.MarkConstructionWorkApplied();
                    Outpost_ConstructionXp.TryGrant(traveler, Outpost_OccupantProgression.EventXpBuildCompleteT2);
                    Find.World?.GetComponent<WorldComponent_SpreadManager>()?.AddLog(
                        new SpreadLogEntry(
                            "TSA_WD_Log_Powerline_Built".Translate(origin.LabelCap, tile.ToString()),
                            origin,
                            tile));
                }
            }

            ClearProject(comp);
        }

        private static WorldObject_WD_Powerline Spawn(int tile, WorldObject_WD_Outpost builder)
        {
            WorldObjectDef def = PowerlineDef;
            if (def == null) return null;
            var line = WorldObjectMaker.MakeWorldObject(def) as WorldObject_WD_Powerline;
            if (line == null) return null;
            line.Tile = tile;
            line.SetFaction(Faction.OfPlayer);
            line.builtByOutpostId = builder?.ID ?? -1;
            Find.WorldObjects.Add(line);
            return line;
        }
    }
}
