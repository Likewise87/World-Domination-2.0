using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    [StaticConstructorOnStartup]
    public static class Action_Outpost_Build
    {
        private static Texture2D cachedBuildIcon;

        public static IEnumerable<Gizmo> GetGizmos(WorldObject outpost)
        {
            if (outpost == null || outpost.Faction != Faction.OfPlayer) yield break;
            if (!(outpost is WorldObject_WD_Outpost)) yield break;

            var comp = outpost.GetComponent<CompViralSpread>();
            if (comp == null) yield break;

            // Never BuildRootMenuOptions here — RimWorld re-queries GetGizmos every GUI pass.
            // Menu (gates / material counts / AT cap) is built only when the player clicks.
            // AT is always in the root menu, so the Build button is always shown for player outposts.
            yield return new Command_Action
            {
                defaultLabel = BuildGizmoLabel(),
                defaultDesc = "TSA_WD_BuildDesc".Translate(),
                icon = cachedBuildIcon ??= ContentFinder<Texture2D>.Get("UI/Commands/Build", false) ?? TexCommand.Replant,
                defaultIconColor = Color.cyan,
                action = () => Find.WindowStack.Add(new WdCascadingFloatMenu(BuildRootMenuOptions(outpost, comp)))
            };

            foreach (var g in Action_Outpost_BuildRoad.GetGizmos(outpost))
                yield return g;
            foreach (var g in Action_Outpost_BuildTrench.GetGizmos(outpost))
                yield return g;
            foreach (var g in Action_Outpost_BuildBridge.GetGizmos(outpost))
                yield return g;
            foreach (var g in Action_Outpost_RoadBlocks.GetGizmos(outpost))
                yield return g;
            foreach (var g in Action_Outpost_SpikeTraps.GetGizmos(outpost))
                yield return g;
            foreach (var g in Action_Outpost_AtTurrets.GetGizmos(outpost))
                yield return g;
            foreach (var g in Action_Outpost_WatchTower.GetGizmos(outpost))
                yield return g;
            foreach (var g in Action_Outpost_Decontamination.GetGizmos(outpost))
                yield return g;
            foreach (var g in WorldActions_PlayerScout.GetGizmos(outpost))
                yield return g;
        }

        /// <summary>Experimental colony Build menu (roads / blocks / traps; no decontamination).</summary>
        public static IEnumerable<Gizmo> GetColonyGizmos(Settlement colony)
        {
            if (!ColonyWorldBuildUtility.IsPlayerColonyBuildActor(colony)) yield break;

            var comp = colony.GetComponent<CompViralSpread>();
            if (comp == null) yield break;

            yield return new Command_Action
            {
                defaultLabel = BuildGizmoLabel(),
                defaultDesc = "TSA_WD_ColonyBuildDesc".Translate(),
                icon = cachedBuildIcon ??= ContentFinder<Texture2D>.Get("UI/Commands/Build", false) ?? TexCommand.Replant,
                defaultIconColor = Color.cyan,
                action = () => Find.WindowStack.Add(new WdCascadingFloatMenu(BuildRootMenuOptions(colony, comp)))
            };

            foreach (var g in Action_Outpost_BuildRoad.GetGizmos(colony))
                yield return g;
            foreach (var g in Action_Outpost_BuildBridge.GetGizmos(colony))
                yield return g;
            foreach (var g in Action_Outpost_RoadBlocks.GetGizmos(colony))
                yield return g;
            foreach (var g in Action_Outpost_SpikeTraps.GetGizmos(colony))
                yield return g;
            foreach (var g in Action_Outpost_AtTurrets.GetGizmos(colony))
                yield return g;
            foreach (var g in Action_Outpost_WatchTower.GetGizmos(colony))
                yield return g;
            foreach (var g in WorldActions_PlayerScout.GetGizmos(colony))
                yield return g;
        }

        /// <summary>Build gizmo title: includes Scout when Settlement Intel / FoW tracking is on.</summary>
        public static string BuildGizmoLabel() =>
            WorldComponent_SettlementIntel.IsTracking()
                ? "TSA_WD_BuildAndScout".Translate()
                : "TSA_WD_Build".Translate();

        /// <summary>Root Build menu: build rows on top, remove rows at the bottom (via orderInPriority). Decontamination only for WD outposts.</summary>
        public static List<FloatMenuOption> BuildRootMenuOptions(WorldObject outpost, CompViralSpread comp)
        {
            var seth = WorldDominationMod.settings;
            var list = new List<FloatMenuOption>();

            bool roads = seth == null || seth.enableOutpostBuildRoads;
            bool blocks = seth == null || seth.enableOutpostBuildRoadBlocks;
            bool traps = seth == null || seth.enableOutpostBuildTraps;

            // Scout muster: player outposts + colony Build menu (trap-style waypoints, asphalt prep).
            if (outpost is WorldObject_WD_Outpost || ColonyWorldBuildUtility.IsPlayerColonyBuildActor(outpost))
            {
                FloatMenuOption scout = WorldActions_PlayerScout.MakeBuildMenuOption(outpost, comp);
                scout.orderInPriority = 600;
                list.Add(scout);
            }

            if (roads)
            {
                var road = Action_Outpost_BuildRoad.MakeBuildRoadMenuOption(outpost, comp);
                road.orderInPriority = 500;
                list.Add(road);

                var bridge = Action_Outpost_BuildBridge.MakeBuildBridgeMenuOption(outpost, comp);
                bridge.orderInPriority = 490;
                list.Add(bridge);

                var deconstructBridge = Action_Outpost_BuildBridge.MakeDeconstructBridgeMenuOption(outpost, comp);
                deconstructBridge.orderInPriority = 155;
                list.Add(deconstructBridge);

                var removeRoads = Action_Outpost_BuildRoad.MakeRemoveRoadsMenuOption(outpost, comp);
                removeRoads.orderInPriority = 160;
                list.Add(removeRoads);
            }

            // Player trenches are outpost-only (colony Build menu still offers roads/blocks/etc.).
            if ((seth == null || seth.enableTrenches) && outpost is WorldObject_WD_Outpost)
            {
                var buildTrench = Action_Outpost_BuildTrench.MakeBuildMenuOption(outpost, comp);
                buildTrench.orderInPriority = 480;
                list.Add(buildTrench);
                var removeTrench = Action_Outpost_BuildTrench.MakeRemoveMenuOption(outpost, comp);
                removeTrench.orderInPriority = 158;
                list.Add(removeTrench);
            }

            if (blocks)
            {
                var buildBlocks = Action_Outpost_RoadBlocks.MakeBuildRoadBlocksMenuOption(outpost, comp);
                buildBlocks.orderInPriority = 400;
                list.Add(buildBlocks);
            }

            if (traps)
            {
                var buildTraps = Action_Outpost_SpikeTraps.MakeBuildSpikeTrapMenuOption(outpost, comp);
                buildTraps.orderInPriority = 300;
                list.Add(buildTraps);
            }

            if (WdMedievalMode.IsAtTurretAllowed(AtTurretTier.Light)
                || WdMedievalMode.IsAtTurretAllowed(AtTurretTier.Medium)
                || WdMedievalMode.IsAtTurretAllowed(AtTurretTier.Heavy))
            {
                var buildTurrets = Action_Outpost_AtTurrets.MakeBuildAtTurretMenuOption(outpost, comp);
                buildTurrets.orderInPriority = 280;
                list.Add(buildTurrets);
            }

            // Player Watch Tower (FoW LOS post). Only when Settlement Intel / FoW is enabled.
            if (WorldComponent_SettlementIntel.IsGateConfigured()
                && WorldActions_WatchTowers.WatchTowerDef != null)
            {
                var buildWatch = Action_Outpost_WatchTower.MakeBuildMenuOption(outpost, comp);
                if (buildWatch != null)
                {
                    buildWatch.orderInPriority = 275;
                    list.Add(buildWatch);
                }
            }

            if (blocks || traps)
            {
                var clearForts = Action_Outpost_RoadBlocks.MakeRemoveFortificationsMenuOption(outpost, comp);
                clearForts.orderInPriority = 150;
                list.Add(clearForts);
            }

            if (outpost is WorldObject_WD_Outpost)
            {
                var buildDecontam = Action_Outpost_Decontamination.MakeBuildDecontaminationMenuOption(outpost, comp);
                buildDecontam.orderInPriority = 250;
                list.Add(buildDecontam);
            }

            return list;
        }
    }
}
