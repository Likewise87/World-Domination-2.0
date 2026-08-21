using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    [StaticConstructorOnStartup]
    public static class Action_Outpost_Decontamination
    {
        private static Texture2D cachedBuildIcon;
        private static Texture2D cachedCancelIcon;

        public static Material LineMat => WorldOverlayLineMaterials.RoadOrange;

        public static Texture2D BuildIcon =>
            cachedBuildIcon ??= ContentFinder<Texture2D>.Get("UI/Commands/Decontaminate", false)
                ?? ContentFinder<Texture2D>.Get("UI/Commands/Build", false)
                ?? TexCommand.Replant;

        public static IEnumerable<Gizmo> GetGizmos(WorldObject outpost)
        {
            if (outpost == null || outpost.Faction != Faction.OfPlayer) yield break;
            if (outpost is not WorldObject_WD_Outpost) yield break;

            var comp = outpost.GetComponent<CompViralSpread>();
            if (comp == null || !WorldActions_Decontamination.HasActiveDecontaminationProject(comp)) yield break;

            yield return new Command_Action
            {
                defaultLabel = "TSA_WD_CancelDecontamination".Translate(),
                defaultDesc = "TSA_WD_CancelDecontaminationDesc".Translate(),
                icon = cachedCancelIcon ??= ContentFinder<Texture2D>.Get("UI/Designators/Cancel"),
                action = delegate
                {
                    WorldActions_Decontamination.ClearDecontaminationProject(comp);
                    Messages.Message("TSA_WD_DecontaminationCancelled".Translate(outpost.LabelCap), MessageTypeDefOf.NeutralEvent);
                }
            };
        }

        public static FloatMenuOption MakeBuildDecontaminationMenuOption(WorldObject outpost, CompViralSpread comp)
        {
            Texture2D icon = BuildIcon;
            if (!ModsConfig.BiotechActive)
            {
                return new FloatMenuOption("TSA_WD_DecontaminationRequiresBiotech".Translate(), () => { }, icon, Color.white)
                {
                    Disabled = true
                };
            }

            if (outpost is not WorldObject_WD_Outpost wd || !wd.HasBuiltDecontaminationUnlock())
            {
                return new FloatMenuOption("TSA_WD_DecontaminationRequiresUpgrade".Translate(), () => { }, icon, Color.white)
                {
                    Disabled = true
                };
            }

            bool roadBusy = comp.roadTargetTile != -1;
            bool blockBusy = WorldActions_RoadBlocks.HasActiveRoadBlockProject(comp);
            bool trapBusy = WorldActions_SpikeTraps.HasActiveSpikeTrapProject(comp);
            bool decontamBusy = WorldActions_Decontamination.HasActiveDecontaminationProject(comp);

            if (decontamBusy)
            {
                string insufficient = comp.GetInsufficientStrengthConstructionMessage();
                string label = insufficient
                    ?? "TSA_WD_DecontaminationBuildStatus".Translate(
                        comp.decontamTargetName.NullOrEmpty() ? "…" : comp.decontamTargetName,
                        (Mathf.Min(1f, comp.decontamProgress) * 100f).ToString("F0")).ToString();
                return new FloatMenuOption(label, () => { }, icon, Color.white) { Disabled = true };
            }

            if (roadBusy || blockBusy || trapBusy)
            {
                return new FloatMenuOption("TSA_WD_CancelCurrentProjectFirst".Translate(), () => { }, icon, Color.white)
                {
                    Disabled = true
                };
            }

            float totalConstruction = wd.TotalConstructionSkillRaw();
            int minConstruction = WorldActions_Decontamination.GetMinConstruction();
            string menuLabel = "TSA_WD_BuildDecontamination".Translate();
            var option = new FloatMenuOption(menuLabel, WdCascadingFloatMenu.WrapLeaf(() =>
            {
                StartDecontaminationTargeting(outpost, comp);
            }), icon, Color.white)
            {
                tooltip = BuildTooltip(wd)
            };
            if (totalConstruction < minConstruction)
            {
                option.Disabled = true;
                option.Label = "TSA_WD_DecontaminationKind_Disabled".Translate(menuLabel, totalConstruction.ToString("F0"), minConstruction);
            }
            return option;
        }

        private static string BuildTooltip(WorldObject_WD_Outpost outpost)
        {
            var s = WorldDominationMod.settings;
            float work = s != null ? s.GetDecontaminationWork() : WorldDominationSettings.DefDecontaminationWork;
            float strength = s != null ? s.GetDecontaminationExpeditionStrength() : WorldDominationSettings.DefDecontaminationExpeditionStrength;
            float reduction = s != null ? s.decontaminationPollutionReductionPp : WorldDominationSettings.DefDecontaminationPollutionReductionPp;
            int minC = WorldActions_Decontamination.GetMinConstruction();
            string timeStr = "—";
            if (outpost != null)
            {
                float days = WorldActions_Decontamination.GetEstimatedDaysPerSegment(outpost);
                if (days >= 0f) timeStr = days.ToString("F2");
            }
            return "TSA_WD_DecontaminationTooltip".Translate(
                work.ToString("F0"),
                reduction.ToString("F0"),
                timeStr,
                strength.ToString("F0"),
                minC.ToString());
        }

        private static void StartDecontaminationTargeting(WorldObject source, CompViralSpread comp)
        {
            CameraJumper.TryJump(source.Tile);
            float range = WorldActions_Decontamination.GetMaxRange(source);
            PlanetLayer layer = PlanetSurfaceWorldActions.LayerOf(source);

            var sessionNodes = new List<int>();
            List<int> committedNodePathTiles = null;
            int previewMouseTile = int.MinValue;
            List<int> previewLegTiles = null;
            int previewThrottleFrame = 0;

            int LastNodeOrNone() => sessionNodes.Count > 0 ? sessionNodes[sessionNodes.Count - 1] : -1;

            bool TileReachableFromOutpost(int tile)
            {
                if (Find.WorldGrid.ApproxDistanceInTiles(source.Tile, tile) > range)
                    return false;
                using (WorldPath path = layer.Pather.FindPath(
                    new PlanetTile(source.Tile, layer), new PlanetTile(tile, layer), null))
                {
                    return path != null && path.Found
                        && !WorldActions_Roads.RoadBuildingPathTouchesWater(path);
                }
            }

            Find.WorldTargeter.BeginTargeting(
                (target) =>
                {
                    if (target.Tile < 0) return false;
                    if (target.Tile == source.Tile) return false;

                    bool shift = Event.current != null && Event.current.shift;
                    int lastNode = LastNodeOrNone();

                    // Project range always from the building outpost/colony (not the last waypoint).
                    if (Find.WorldGrid.ApproxDistanceInTiles(source.Tile, target.Tile) > range)
                    {
                        Messages.Message("TSA_WD_DecontaminationWaypointOutOfRange".Translate(), MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    if (lastNode >= 0 && target.Tile == lastNode)
                        return false;

                    if (lastNode < 0 && !TileReachableFromOutpost(target.Tile))
                    {
                        Messages.Message("TSA_WD_DecontaminationNoPath".Translate(), MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    if (lastNode >= 0)
                    {
                        List<int> leg = WorldActions_RoadBlocks.FindFlatHopPathDestFirst(lastNode, target.Tile);
                        if (leg == null || leg.Count < 2)
                        {
                            Messages.Message("TSA_WD_DecontaminationNoPath".Translate(), MessageTypeDefOf.RejectInput);
                            return false;
                        }
                    }

                    bool nodeOk = WorldActions_Decontamination.IsValidPlanNode(target.Tile);
                    if (!nodeOk && lastNode < 0)
                    {
                        Messages.Message("TSA_WD_DecontaminationBuildEmpty".Translate(), MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    if (shift)
                    {
                        if (!nodeOk)
                        {
                            Messages.Message("TSA_WD_DecontaminationBuildEmpty".Translate(), MessageTypeDefOf.RejectInput);
                            return false;
                        }

                        sessionNodes.Add(target.Tile);
                        committedNodePathTiles = BuildPathAlongNodes(sessionNodes);
                        previewMouseTile = int.MinValue;
                        previewLegTiles = null;
                        Messages.Message("TSA_WD_RoadWaypointAdded".Translate(sessionNodes.Count), MessageTypeDefOf.TaskCompletion);
                        return false;
                    }

                    var clickedNodes = new List<int>(sessionNodes.Count + 1);
                    clickedNodes.AddRange(sessionNodes);
                    clickedNodes.Add(target.Tile);

                    List<int> planned = WorldActions_Decontamination.FilterPlannedTilesFromClickedNodes(clickedNodes);
                    if (planned.Count == 0)
                    {
                        Messages.Message("TSA_WD_DecontaminationBuildEmpty".Translate(), MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    string targetName = "Tile " + planned[planned.Count - 1];
                    var worldObject = Find.WorldObjects.ObjectsAt(planned[planned.Count - 1])
                        .FirstOrDefault(x => x is Settlement || x is WorldObject_WD_Outpost);
                    if (worldObject != null)
                        targetName = worldObject.LabelCap;

                    comp.decontamPlannedTiles = planned;
                    comp.decontamClickedNodes = new List<int>(clickedNodes);
                    var drawPath = BuildPathAlongNodes(clickedNodes);
                    comp.decontamCachedPathTiles = drawPath != null
                        ? new List<int>(drawPath)
                        : new List<int>(clickedNodes);
                    comp.decontamWorkIndex = 0;
                    comp.decontamProgress = 0f;
                    comp.decontamTargetName = targetName;
                    comp.decontamCachedWorkTile = WorldActions_Decontamination.GetCurrentWorkTile(comp);

                    Messages.Message("TSA_WD_DecontaminationBuildTargetSet".Translate(planned.Count), MessageTypeDefOf.PositiveEvent);
                    return true;
                },
                true,
                null,
                false,
                () =>
                {
                    WorldMapRadiusVisual.DrawApproxRadiusRing(new PlanetTile(source.Tile, layer), range, LineMat);

                    Action_Outpost_BuildRoad.DrawRoadPathFromCalculatedNodes(committedNodePathTiles, LineMat);
                    DrawWaypointMarkers(sessionNodes);

                    int mouseTile = GenWorld.MouseTile();
                    int anchor = LastNodeOrNone();

                    if (anchor < 0)
                    {
                        previewMouseTile = mouseTile;
                        previewLegTiles = null;
                        if (mouseTile >= 0
                            && Find.WorldGrid.ApproxDistanceInTiles(source.Tile, mouseTile) <= range
                            && WorldActions_Decontamination.IsValidPlanNode(mouseTile))
                        {
                            Action_Outpost_BuildRoad.DrawOrangeStar(mouseTile);
                        }
                    }
                    else if (mouseTile < 0 || Find.WorldGrid.ApproxDistanceInTiles(source.Tile, mouseTile) > range)
                    {
                        previewMouseTile = mouseTile;
                        previewLegTiles = null;
                    }
                    else if (mouseTile != previewMouseTile && Time.frameCount - previewThrottleFrame >= 5)
                    {
                        previewMouseTile = mouseTile;
                        previewThrottleFrame = Time.frameCount;
                        previewLegTiles = WorldActions_RoadBlocks.FindFlatHopPathDestFirst(anchor, mouseTile);
                    }

                    if (anchor >= 0)
                    {
                        Action_Outpost_BuildRoad.DrawRoadPathFromCalculatedNodes(previewLegTiles, LineMat);
                        if (mouseTile >= 0 && previewLegTiles != null && previewLegTiles.Count >= 2)
                            Action_Outpost_BuildRoad.DrawOrangeStar(mouseTile);
                    }
                },
                (target) =>
                {
                    if (sessionNodes.Count > 0)
                        return "TSA_WD_RoadWaypointLabel".Translate(sessionNodes.Count);
                    return "TSA_WD_DecontaminationBuildTip".Translate();
                },
                (target) =>
                {
                    if (!target.IsValid || target.Tile < 0) return false;
                    if (target.Tile == source.Tile) return false;

                    int anchor = LastNodeOrNone();
                    if (Find.WorldGrid.ApproxDistanceInTiles(source.Tile, target.Tile) > range) return false;

                    PlanetTile pTile = new PlanetTile(target.Tile, layer);
                    if (Find.World.Impassable(pTile)) return false;
                    if (Find.WorldGrid.InBounds(target.Tile) && Find.WorldGrid[target.Tile].WaterCovered)
                        return false;

                    if (anchor < 0)
                        return WorldActions_Decontamination.IsValidPlanNode(target.Tile);

                    if (previewMouseTile == target.Tile)
                        return previewLegTiles != null && previewLegTiles.Count >= 2;
                    return true;
                },
                null,
                true
            );
        }

        private static List<int> BuildPathAlongNodes(List<int> nodes)
        {
            if (nodes == null || nodes.Count < 2) return null;

            var forward = new List<int> { nodes[0] };
            for (int i = 0; i < nodes.Count - 1; i++)
            {
                List<int> pathDestFirst = WorldActions_RoadBlocks.FindFlatHopPathDestFirst(nodes[i], nodes[i + 1]);
                if (pathDestFirst == null || pathDestFirst.Count < 2) return null;
                for (int n = pathDestFirst.Count - 2; n >= 0; n--)
                    forward.Add(pathDestFirst[n]);
            }

            forward.Reverse();
            return forward;
        }

        private static void DrawWaypointMarkers(List<int> waypoints)
        {
            if (waypoints == null) return;
            for (int i = 0; i < waypoints.Count; i++)
                Action_Outpost_BuildRoad.DrawOrangeX(waypoints[i]);
        }

        public static void DrawDecontaminationOverlayIfSelected(WorldObject worldObject)
        {
            if (worldObject == null || !Find.WorldSelector.IsSelected(worldObject)) return;
            if (worldObject is not WorldObject_WD_Outpost || worldObject.Faction != Faction.OfPlayer) return;

            var comp = worldObject.GetComponent<CompViralSpread>();
            if (!WorldActions_Decontamination.HasActiveDecontaminationProject(comp)) return;

            if (comp.decontamCachedPathTiles != null && comp.decontamCachedPathTiles.Count >= 2)
                Action_Outpost_BuildRoad.DrawRoadPathFromCalculatedNodes(comp.decontamCachedPathTiles, LineMat);

            var nodes = comp.decontamClickedNodes;
            if (nodes == null || nodes.Count == 0)
                nodes = comp.decontamPlannedTiles;
            if (nodes != null && nodes.Count > 0)
            {
                for (int i = 0; i < nodes.Count - 1; i++)
                    Action_Outpost_BuildRoad.DrawOrangeX(nodes[i]);
                Action_Outpost_BuildRoad.DrawOrangeStar(nodes[nodes.Count - 1]);
            }

            if (comp.decontamCachedWorkTile != -1)
                Action_Outpost_BuildRoad.DrawOrangeCircle(comp.decontamCachedWorkTile);
        }
    }
}
