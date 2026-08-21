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

            List<FloatMenuOption> menu = BuildRootMenuOptions(outpost, comp);
            if (menu != null && menu.Count > 0)
            {
                yield return new Command_Action
                {
                    defaultLabel = "TSA_WD_Build".Translate(),
                    defaultDesc = "TSA_WD_BuildDesc".Translate(),
                    icon = cachedBuildIcon ??= ContentFinder<Texture2D>.Get("UI/Commands/Build", false) ?? TexCommand.Replant,
                    defaultIconColor = Color.cyan,
                    action = () => Find.WindowStack.Add(new WdCascadingFloatMenu(BuildRootMenuOptions(outpost, comp)))
                };
            }

            foreach (var g in Action_Outpost_BuildRoad.GetGizmos(outpost))
                yield return g;
            foreach (var g in Action_Outpost_RoadBlocks.GetGizmos(outpost))
                yield return g;
            foreach (var g in Action_Outpost_SpikeTraps.GetGizmos(outpost))
                yield return g;
            foreach (var g in Action_Outpost_AtTurrets.GetGizmos(outpost))
                yield return g;
            foreach (var g in Action_Outpost_Decontamination.GetGizmos(outpost))
                yield return g;
        }

        /// <summary>Experimental colony Build menu (roads / blocks / traps; no decontamination).</summary>
        public static IEnumerable<Gizmo> GetColonyGizmos(Settlement colony)
        {
            if (!ColonyWorldBuildUtility.IsPlayerColonyBuildActor(colony)) yield break;

            var comp = colony.GetComponent<CompViralSpread>();
            if (comp == null) yield break;

            List<FloatMenuOption> menu = BuildRootMenuOptions(colony, comp);
            if (menu != null && menu.Count > 0)
            {
                yield return new Command_Action
                {
                    defaultLabel = "TSA_WD_Build".Translate(),
                    defaultDesc = "TSA_WD_ColonyBuildDesc".Translate(),
                    icon = cachedBuildIcon ??= ContentFinder<Texture2D>.Get("UI/Commands/Build", false) ?? TexCommand.Replant,
                    defaultIconColor = Color.cyan,
                    action = () => Find.WindowStack.Add(new WdCascadingFloatMenu(BuildRootMenuOptions(colony, comp)))
                };
            }

            foreach (var g in Action_Outpost_BuildRoad.GetGizmos(colony))
                yield return g;
            foreach (var g in Action_Outpost_RoadBlocks.GetGizmos(colony))
                yield return g;
            foreach (var g in Action_Outpost_SpikeTraps.GetGizmos(colony))
                yield return g;
            foreach (var g in Action_Outpost_AtTurrets.GetGizmos(colony))
                yield return g;
        }

        /// <summary>Root Build menu: build rows on top, remove rows at the bottom (via orderInPriority). Decontamination only for WD outposts.</summary>
        public static List<FloatMenuOption> BuildRootMenuOptions(WorldObject outpost, CompViralSpread comp)
        {
            var seth = WorldDominationMod.settings;
            var list = new List<FloatMenuOption>();

            bool roads = seth == null || seth.enableOutpostBuildRoads;
            bool blocks = seth == null || seth.enableOutpostBuildRoadBlocks;
            bool traps = seth == null || seth.enableOutpostBuildTraps;

            if (roads)
            {
                var road = Action_Outpost_BuildRoad.MakeBuildRoadMenuOption(outpost, comp);
                road.orderInPriority = 500;
                list.Add(road);

                var removeRoads = Action_Outpost_BuildRoad.MakeRemoveRoadsMenuOption(outpost, comp);
                removeRoads.orderInPriority = 160;
                list.Add(removeRoads);
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

            var buildTurrets = Action_Outpost_AtTurrets.MakeBuildAtTurretMenuOption(outpost, comp);
            buildTurrets.orderInPriority = 280;
            list.Add(buildTurrets);

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
