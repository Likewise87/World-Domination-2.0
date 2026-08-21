using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    [StaticConstructorOnStartup]
    public static class Action_Outpost_SpikeTraps
    {
        private static Texture2D cachedBuildIcon;
        private static Texture2D cachedRemoveIcon;
        private static Texture2D cachedCancelIcon;

        public static Material LineMat => WorldOverlayLineMaterials.RoadOrange;

        public static Texture2D BuildSpikeTrapIcon =>
            cachedBuildIcon ??= ContentFinder<Texture2D>.Get("WorldObjects/WorldSpikeTrap", false) ?? TexCommand.Replant;

        public static Texture2D ClearSpikeTrapIcon =>
            cachedRemoveIcon ??= ContentFinder<Texture2D>.Get("UI/Commands/Remove_WorldSpikeTrap", false) ?? TexCommand.Replant;

        public static IEnumerable<Gizmo> GetGizmos(WorldObject outpost)
        {
            if (outpost == null || outpost.Faction != Faction.OfPlayer) yield break;
            if (!(outpost is WorldObject_WD_Outpost) && !ColonyWorldBuildUtility.IsPlayerColonyBuildActor(outpost))
                yield break;

            var comp = outpost.GetComponent<CompViralSpread>();
            if (comp == null || !WorldActions_SpikeTraps.HasActiveSpikeTrapProject(comp)) yield break;

            yield return new Command_Action
            {
                defaultLabel = "TSA_WD_CancelSpikeTraps".Translate(),
                defaultDesc = "TSA_WD_CancelSpikeTrapsDesc".Translate(),
                icon = cachedCancelIcon ??= ContentFinder<Texture2D>.Get("UI/Designators/Cancel"),
                action = delegate
                {
                    WorldActions_SpikeTraps.ClearSpikeTrapProject(comp);
                    Messages.Message("TSA_WD_SpikeTrapCancelled".Translate(outpost.LabelCap), MessageTypeDefOf.NeutralEvent);
                }
            };
        }

        public static FloatMenuOption MakeBuildSpikeTrapMenuOption(WorldObject outpost, CompViralSpread comp)
        {
            Texture2D icon = BuildSpikeTrapIcon;
            bool roadBusy = comp.roadTargetTile != -1;
            bool blockBusy = WorldActions_RoadBlocks.HasActiveRoadBlockProject(comp);
            bool trapBusy = WorldActions_SpikeTraps.HasActiveSpikeTrapProject(comp);
            bool decontamBusy = WorldActions_Decontamination.HasActiveDecontaminationProject(comp);

            if (trapBusy && !comp.spikeTrapIsClearing)
            {
                string kindLabel = SpikeTrapKindUtil.LabelKey(comp.selectedSpikeTrapKind).Translate();
                string insufficient = comp.GetInsufficientStrengthConstructionMessage();
                string dest = comp.spikeTrapTargetName.NullOrEmpty() ? "…" : comp.spikeTrapTargetName;
                string label = insufficient
                    ?? "TSA_WD_Inspect_SpikeTrapStatus".Translate(
                        kindLabel,
                        (Mathf.Min(1f, comp.spikeTrapProgress) * 100f).ToString("F0"),
                        dest).ToString();
                return new FloatMenuOption(label, () => { }, icon, Color.cyan) { Disabled = true };
            }

            if (trapBusy)
            {
                return new FloatMenuOption("TSA_WD_CancelCurrentProjectFirst".Translate(), () => { }, icon, Color.cyan)
                {
                    Disabled = true
                };
            }

            if (roadBusy)
            {
                return new FloatMenuOption("TSA_WD_CancelCurrentProjectFirst".Translate(), () => { }, icon, Color.cyan)
                {
                    Disabled = true
                };
            }

            if (blockBusy)
            {
                return new FloatMenuOption("TSA_WD_CancelCurrentProjectFirst".Translate(), () => { }, icon, Color.cyan)
                {
                    Disabled = true
                };
            }

            if (decontamBusy)
            {
                return new FloatMenuOption("TSA_WD_CancelCurrentProjectFirst".Translate(), () => { }, icon, Color.cyan)
                {
                    Disabled = true
                };
            }

            var branch = WdCascadingFloatMenu.MakeBranchOption(
                "TSA_WD_BuildSpikeTraps".Translate(),
                () => OpenSpikeTrapKindMenu(outpost, comp),
                icon,
                Color.cyan);
            branch.tooltip = "TSA_WD_BuildSpikeTrapsDesc".Translate();
            return branch;
        }

        public static void OpenSpikeTrapKindMenu(WorldObject outpost, CompViralSpread comp)
        {
            float totalConstruction = ColonyWorldBuildUtility.GetActorConstructionSkillRaw(outpost);
            var options = new List<FloatMenuOption>
            {
                MakeKindOption(outpost, comp, SpikeTrapKind.Spike, totalConstruction),
                MakeKindOption(outpost, comp, SpikeTrapKind.Caltrops, totalConstruction)
            };
            WdCascadingFloatMenu.OpenAsChild(options, () => Action_Outpost_Build.BuildRootMenuOptions(outpost, comp));
        }

        private static FloatMenuOption MakeKindOption(WorldObject outpost, CompViralSpread comp, SpikeTrapKind kind, float totalConstruction)
        {
            int minConstruction = WorldActions_SpikeTraps.GetMinConstruction(kind);
            string label = SpikeTrapKindUtil.LabelKey(kind).Translate();
            Texture2D icon = kind == SpikeTrapKind.Caltrops
                ? (ContentFinder<Texture2D>.Get("WorldObjects/Caltrops", false) ?? BuildSpikeTrapIcon)
                : BuildSpikeTrapIcon;
            var option = new FloatMenuOption(label, WdCascadingFloatMenu.WrapLeaf(() =>
            {
                comp.selectedSpikeTrapKind = kind;
                StartSpikeTrapTargeting(outpost, comp, clearing: false);
            }), icon, Color.cyan)
            {
                tooltip = BuildKindTooltip(outpost, kind)
            };
            ColonyWorldBuildRequirements.ApplyGate(
                option,
                totalConstruction,
                minConstruction,
                ColonyWorldBuildRequirements.GetRequiredResearchForSpikeTrap(kind),
                ColonyWorldBuildRequirements.GetMaterialCostsForSpikeTrap(kind));
            return option;
        }

        private static string BuildKindTooltip(WorldObject actor, SpikeTrapKind kind)
        {
            var s = WorldDominationMod.settings;
            float work = s != null ? s.GetSpikeTrapWork(kind) : WorldDominationSettings.DefSpikeTrapSpikeWork;
            float damage = s != null ? s.GetSpikeTrapDamage(kind) : WorldDominationSettings.DefSpikeTrapSpikeDamage;
            float strength = s != null ? s.GetSpikeTrapExpeditionStrength(kind) : WorldDominationSettings.DefSpikeTrapSpikeExpeditionStrength;
            float hp = s != null ? s.GetSpikeTrapMaxHealth(kind) : WorldDominationSettings.DefSpikeTrapSpikeMaxHealth;
            string timeStr = "-";
            if (actor != null)
            {
                float days = WorldActions_SpikeTraps.GetEstimatedDaysPerSpikeTrapSegment(actor, kind);
                if (days >= 0f) timeStr = days.ToString("F2");
            }
            if (ColonyWorldBuildUtility.IsPlayerColonyBuildActor(actor))
                strength = 0f;
            return "TSA_WD_SpikeTrapKindTooltip".Translate(
                work.ToString("F0"),
                damage.ToString("F0"),
                timeStr,
                strength.ToString("F0"),
                hp.ToString("F0"));
        }

        public static FloatMenuOption MakeClearSpikeTrapMenuOption(WorldObject outpost, CompViralSpread comp)
        {
            Texture2D icon = ClearSpikeTrapIcon;
            bool roadBusy = comp.roadTargetTile != -1;
            bool blockBusy = WorldActions_RoadBlocks.HasActiveRoadBlockProject(comp);
            bool trapBusy = WorldActions_SpikeTraps.HasActiveSpikeTrapProject(comp);
            bool decontamBusy = WorldActions_Decontamination.HasActiveDecontaminationProject(comp);

            if (trapBusy && comp.spikeTrapIsClearing)
            {
                string insufficient = comp.GetInsufficientStrengthConstructionMessage();
                string label = insufficient
                    ?? "TSA_WD_SpikeTrapClearStatus".Translate(
                        comp.spikeTrapTargetName.NullOrEmpty() ? "…" : comp.spikeTrapTargetName,
                        (Mathf.Min(1f, comp.spikeTrapProgress) * 100f).ToString("F0")).ToString();
                return new FloatMenuOption(label, () => { }, icon, Color.white) { Disabled = true };
            }

            if (trapBusy)
            {
                return new FloatMenuOption("TSA_WD_CancelCurrentProjectFirst".Translate(), () => { }, icon, Color.white)
                {
                    Disabled = true
                };
            }

            if (roadBusy)
            {
                return new FloatMenuOption("TSA_WD_CancelCurrentProjectFirst".Translate(), () => { }, icon, Color.white)
                {
                    Disabled = true
                };
            }

            if (blockBusy)
            {
                return new FloatMenuOption("TSA_WD_CancelCurrentProjectFirst".Translate(), () => { }, icon, Color.white)
                {
                    Disabled = true
                };
            }

            if (decontamBusy)
            {
                return new FloatMenuOption("TSA_WD_CancelCurrentProjectFirst".Translate(), () => { }, icon, Color.white)
                {
                    Disabled = true
                };
            }

            return new FloatMenuOption(
                "TSA_WD_ClearSpikeTraps".Translate(),
                WdCascadingFloatMenu.WrapLeaf(() => StartSpikeTrapTargeting(outpost, comp, clearing: true)),
                icon,
                Color.white)
            {
                tooltip = "TSA_WD_ClearSpikeTrapsDesc".Translate()
            };
        }

        private static void StartSpikeTrapTargeting(WorldObject source, CompViralSpread comp, bool clearing)
        {
            CameraJumper.TryJump(source.Tile);
            float range = WorldActions_SpikeTraps.GetMaxRange(source);
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
                        Messages.Message("TSA_WD_SpikeTrapWaypointOutOfRange".Translate(), MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    if (lastNode >= 0 && target.Tile == lastNode)
                        return false;

                    if (lastNode < 0 && !TileReachableFromOutpost(target.Tile))
                    {
                        Messages.Message("TSA_WD_SpikeTrapNoPath".Translate(), MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    if (lastNode >= 0)
                    {
                        List<int> leg = WorldActions_RoadBlocks.FindFlatHopPathDestFirst(lastNode, target.Tile);
                        if (leg == null || leg.Count < 2)
                        {
                            Messages.Message("TSA_WD_SpikeTrapNoPath".Translate(), MessageTypeDefOf.RejectInput);
                            return false;
                        }
                    }

                    bool nodeOk = clearing
                        ? WorldActions_SpikeTraps.IsValidClearTile(target.Tile)
                        : WorldActions_SpikeTraps.IsValidBuildPlanNode(target.Tile);
                    if (!nodeOk && lastNode < 0)
                    {
                        Messages.Message(
                            clearing ? "TSA_WD_SpikeTrapClearEmpty".Translate() : "TSA_WD_SpikeTrapBuildEmpty".Translate(),
                            MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    if (shift)
                    {
                        if (!nodeOk && !clearing)
                        {
                            Messages.Message("TSA_WD_SpikeTrapBuildEmpty".Translate(), MessageTypeDefOf.RejectInput);
                            return false;
                        }
                        if (!nodeOk && clearing)
                        {
                            Messages.Message("TSA_WD_SpikeTrapClearEmpty".Translate(), MessageTypeDefOf.RejectInput);
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

                    List<int> planned = WorldActions_SpikeTraps.FilterPlannedTilesFromClickedNodes(
                        clickedNodes, clearing, clearing ? SpikeTrapKind.Spike : comp.selectedSpikeTrapKind, source.Faction);
                    if (planned.Count == 0)
                    {
                        Messages.Message(
                            clearing ? "TSA_WD_SpikeTrapClearEmpty".Translate() : "TSA_WD_SpikeTrapBuildEmpty".Translate(),
                            MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    string targetName = "Tile " + planned[0];
                    var worldObject = Find.WorldObjects.ObjectsAt(planned[planned.Count - 1])
                        .FirstOrDefault(x => x is Settlement || x is WorldObject_WD_Outpost);
                    if (worldObject != null)
                        targetName = worldObject.LabelCap;
                    else
                        targetName = "Tile " + planned[planned.Count - 1];

                    comp.spikeTrapPlannedTiles = planned;
                    comp.spikeTrapClickedNodes = new List<int>(clickedNodes);
                    var drawPath = BuildPathAlongNodes(clickedNodes);
                    comp.spikeTrapCachedPathTiles = drawPath != null
                        ? new List<int>(drawPath)
                        : new List<int>(clickedNodes);
                    comp.spikeTrapWorkIndex = 0;
                    comp.spikeTrapProgress = 0f;
                    comp.spikeTrapIsClearing = clearing;
                    comp.spikeTrapTargetName = targetName;
                    comp.spikeTrapCachedWorkTile = WorldActions_SpikeTraps.GetCurrentWorkTile(comp);

                    Messages.Message(
                        clearing
                            ? "TSA_WD_SpikeTrapClearTargetSet".Translate(planned.Count)
                            : "TSA_WD_SpikeTrapBuildTargetSet".Translate(planned.Count),
                        MessageTypeDefOf.PositiveEvent);
                    return true;
                },
                true,
                null,
                false,
                () =>
                {
                    WD_RadiusOverlayMode.DrawOrFill(new PlanetTile(source.Tile, layer), range, OutpostCoverageFillKind.Orange, LineMat);

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
                            && (clearing
                                ? WorldActions_SpikeTraps.IsValidClearTile(mouseTile)
                                : WorldActions_SpikeTraps.IsValidBuildPlanNode(mouseTile)))
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
                        previewLegTiles = CalculatePathNodes(anchor, mouseTile);
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
                    return clearing
                        ? "TSA_WD_SpikeTrapClearTip".Translate()
                        : "TSA_WD_SpikeTrapBuildTip".Translate();
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
                    {
                        return clearing
                            ? WorldActions_SpikeTraps.IsValidClearTile(target.Tile)
                            : WorldActions_SpikeTraps.IsValidBuildPlanNode(target.Tile);
                    }

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

        private static List<int> CalculatePathNodes(int start, int end)
        {
            return WorldActions_RoadBlocks.FindFlatHopPathDestFirst(start, end);
        }

        private static void DrawWaypointMarkers(List<int> waypoints)
        {
            if (waypoints == null || waypoints.Count == 0) return;
            for (int i = 0; i < waypoints.Count; i++)
                Action_Outpost_BuildRoad.DrawOrangeX(waypoints[i]);
        }

        public static void DrawSpikeTrapOverlayIfSelected(WorldObject worldObject)
        {
            if (worldObject == null || !Find.WorldSelector.IsSelected(worldObject)) return;
            if (worldObject.Faction != Faction.OfPlayer) return;
            if (!(worldObject is WorldObject_WD_Outpost) && !ColonyWorldBuildUtility.IsPlayerColonyBuildActor(worldObject))
                return;

            var comp = worldObject.GetComponent<CompViralSpread>();
            if (!WorldActions_SpikeTraps.HasActiveSpikeTrapProject(comp)) return;

            EnsureSpikeTrapOverlayCache(comp);

            if (comp.spikeTrapCachedPathTiles != null && comp.spikeTrapCachedPathTiles.Count >= 2)
                Action_Outpost_BuildRoad.DrawRoadPathFromCalculatedNodes(comp.spikeTrapCachedPathTiles, LineMat);

            var nodes = comp.spikeTrapClickedNodes;
            if (nodes == null || nodes.Count == 0)
                nodes = comp.spikeTrapPlannedTiles;
            if (nodes != null && nodes.Count > 0)
            {
                for (int i = 0; i < nodes.Count - 1; i++)
                    Action_Outpost_BuildRoad.DrawOrangeX(nodes[i]);
                Action_Outpost_BuildRoad.DrawOrangeStar(nodes[nodes.Count - 1]);
            }

            if (comp.spikeTrapCachedWorkTile != -1)
                Action_Outpost_BuildRoad.DrawOrangeCircle(comp.spikeTrapCachedWorkTile);
        }

        private static void EnsureSpikeTrapOverlayCache(CompViralSpread comp)
        {
            if (comp == null) return;

            var planned = comp.spikeTrapPlannedTiles;
            if (planned == null || planned.Count == 0) return;

            if (comp.spikeTrapClickedNodes == null || comp.spikeTrapClickedNodes.Count == 0)
                comp.spikeTrapClickedNodes = new List<int>(planned);

            bool needPath = comp.spikeTrapCachedPathTiles == null || comp.spikeTrapCachedPathTiles.Count < 2;
            if (!needPath)
            {
                // Same adjacency check as road blocks — rebuild if no drawable hop remains.
                WorldGrid grid = Find.WorldGrid;
                bool anyHop = false;
                if (grid != null)
                {
                    var tiles = comp.spikeTrapCachedPathTiles;
                    for (int i = 0; i < tiles.Count - 1; i++)
                    {
                        int a = tiles[i];
                        int b = tiles[i + 1];
                        if (grid.InBounds(a) && grid.InBounds(b) && grid.IsNeighbor(a, b))
                        {
                            anyHop = true;
                            break;
                        }
                    }
                }
                if (!anyHop) needPath = true;
            }

            if (!needPath) return;

            var nodes = comp.spikeTrapClickedNodes;
            if (nodes != null && nodes.Count >= 2)
            {
                var drawPath = BuildPathAlongNodes(nodes);
                comp.spikeTrapCachedPathTiles = drawPath != null
                    ? new List<int>(drawPath)
                    : new List<int>(nodes);
            }
            else if (planned.Count >= 2)
            {
                var drawPath = BuildPathAlongNodes(planned);
                comp.spikeTrapCachedPathTiles = drawPath != null
                    ? new List<int>(drawPath)
                    : new List<int>(planned);
            }
            else
            {
                comp.spikeTrapCachedPathTiles = new List<int>(nodes ?? planned);
            }
        }
    }
}
