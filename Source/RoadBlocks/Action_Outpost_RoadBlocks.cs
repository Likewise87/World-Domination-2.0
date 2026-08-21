using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    [StaticConstructorOnStartup]
    public static class Action_Outpost_RoadBlocks
    {
        private static Texture2D cachedBuildIcon;
        private static Texture2D cachedRemoveIcon;
        private static Texture2D cachedCancelIcon;

        public static Material LineMat => WorldOverlayLineMaterials.RoadOrange;

        public static Texture2D BuildRoadBlockIcon =>
            cachedBuildIcon ??= ContentFinder<Texture2D>.Get("UI/Commands/Build_RoadBlock", false) ?? TexCommand.Replant;

        public static Texture2D ClearRoadBlockIcon =>
            cachedRemoveIcon ??= ContentFinder<Texture2D>.Get("UI/Commands/Remove_RoadBlock", false) ?? TexCommand.Replant;

        /// <summary>Cancel gizmo only; start actions live under <see cref="Action_Outpost_Build"/>.</summary>
        public static IEnumerable<Gizmo> GetGizmos(WorldObject outpost)
        {
            if (outpost == null || outpost.Faction != Faction.OfPlayer) yield break;
            if (!(outpost is WorldObject_WD_Outpost) && !ColonyWorldBuildUtility.IsPlayerColonyBuildActor(outpost))
                yield break;

            var comp = outpost.GetComponent<CompViralSpread>();
            if (comp == null || !WorldActions_RoadBlocks.HasActiveRoadBlockProject(comp)) yield break;

            yield return new Command_Action
            {
                defaultLabel = comp.roadBlockIsClearing && comp.roadBlockClearAnyFortification
                    ? "TSA_WD_CancelFortificationClear".Translate()
                    : "TSA_WD_CancelRoadBlocks".Translate(),
                defaultDesc = comp.roadBlockIsClearing && comp.roadBlockClearAnyFortification
                    ? "TSA_WD_CancelFortificationClearDesc".Translate()
                    : "TSA_WD_CancelRoadBlocksDesc".Translate(),
                icon = cachedCancelIcon ??= ContentFinder<Texture2D>.Get("UI/Designators/Cancel"),
                action = delegate
                {
                    bool wasFortClear = comp.roadBlockClearAnyFortification;
                    WorldActions_RoadBlocks.ClearRoadBlockProject(comp);
                    Messages.Message(
                        wasFortClear
                            ? "TSA_WD_FortificationClearCancelled".Translate(outpost.LabelCap)
                            : "TSA_WD_RoadBlockCancelled".Translate(outpost.LabelCap),
                        MessageTypeDefOf.NeutralEvent);
                }
            };
        }

        public static FloatMenuOption MakeBuildRoadBlocksMenuOption(WorldObject outpost, CompViralSpread comp)
        {
            Texture2D icon = BuildRoadBlockIcon;
            bool roadBusy = comp.roadTargetTile != -1;
            bool blockBusy = WorldActions_RoadBlocks.HasActiveRoadBlockProject(comp);
            bool trapBusy = WorldActions_SpikeTraps.HasActiveSpikeTrapProject(comp);
            bool decontamBusy = WorldActions_Decontamination.HasActiveDecontaminationProject(comp);

            if (blockBusy && !comp.roadBlockIsClearing)
            {
                string kindLabel = RoadBlockKindUtil.LabelKey(comp.selectedRoadBlockKind).Translate();
                string insufficient = comp.GetInsufficientStrengthConstructionMessage();
                string dest = comp.roadBlockTargetName.NullOrEmpty() ? "â€¦" : comp.roadBlockTargetName;
                string label = insufficient
                    ?? "TSA_WD_Inspect_RoadBlockStatus".Translate(
                        kindLabel,
                        (Mathf.Min(1f, comp.roadBlockProgress) * 100f).ToString("F0"),
                        dest).ToString();
                return new FloatMenuOption(label, () => { }, icon, Color.cyan) { Disabled = true };
            }

            if (blockBusy)
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

            if (trapBusy)
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
                "TSA_WD_BuildRoadBlocks".Translate(),
                () => OpenRoadBlockKindMenu(outpost, comp),
                icon,
                Color.cyan);
            branch.tooltip = "TSA_WD_BuildRoadBlocksDesc".Translate();
            return branch;
        }

        public static void OpenRoadBlockKindMenu(WorldObject outpost, CompViralSpread comp)
        {
            float totalConstruction = ColonyWorldBuildUtility.GetActorConstructionSkillRaw(outpost);
            var options = new List<FloatMenuOption>
            {
                MakeKindOption(outpost, comp, RoadBlockKind.Light, totalConstruction),
                MakeKindOption(outpost, comp, RoadBlockKind.Normal, totalConstruction),
                MakeKindOption(outpost, comp, RoadBlockKind.Heavy, totalConstruction)
            };
            WdCascadingFloatMenu.OpenAsChild(options, () => Action_Outpost_Build.BuildRootMenuOptions(outpost, comp));
        }

        private static FloatMenuOption MakeKindOption(WorldObject outpost, CompViralSpread comp, RoadBlockKind kind, float totalConstruction)
        {
            int minConstruction = WorldActions_RoadBlocks.GetMinConstruction(kind);
            string label = RoadBlockKindUtil.LabelKey(kind).Translate();
            Texture2D kindIcon = ContentFinder<Texture2D>.Get(RoadBlockKindUtil.TexturePath(kind), false) ?? BuildRoadBlockIcon;
            var option = new FloatMenuOption(label, WdCascadingFloatMenu.WrapLeaf(() =>
            {
                comp.selectedRoadBlockKind = kind;
                StartRoadBlockTargeting(outpost, comp, clearing: false);
            }), kindIcon, Color.white)
            {
                tooltip = BuildKindTooltip(outpost, kind)
            };
            ColonyWorldBuildRequirements.ApplyGate(
                option,
                totalConstruction,
                minConstruction,
                ColonyWorldBuildRequirements.GetRequiredResearchForRoadBlock(kind),
                ColonyWorldBuildRequirements.GetMaterialCostsForRoadBlock(kind));
            return option;
        }

        private static string BuildKindTooltip(WorldObject actor, RoadBlockKind kind)
        {
            var s = WorldDominationMod.settings;
            float work = s != null ? s.GetRoadBlockWork(kind) : WorldDominationSettings.DefRoadBlockNormalWork;
            float penalty = s != null ? s.GetRoadBlockFlatPenalty(kind) : WorldDominationSettings.DefRoadBlockNormalFlatPenalty;
            float strength = s != null ? s.GetRoadBlockExpeditionStrength(kind) : WorldDominationSettings.DefRoadBlockNormalExpeditionStrength;
            float hp = s != null ? s.GetRoadBlockMaxHealth(kind) : WorldDominationSettings.DefRoadBlockNormalMaxHealth;
            string timeStr = "-";
            if (actor != null)
            {
                float days = WorldActions_RoadBlocks.GetEstimatedDaysPerRoadBlockSegment(actor, kind);
                if (days >= 0f) timeStr = days.ToString("F2");
            }
            if (ColonyWorldBuildUtility.IsPlayerColonyBuildActor(actor))
                strength = 0f;
            return "TSA_WD_RoadBlockKindTooltip".Translate(
                work.ToString("F0"),
                penalty.ToString("0.#"),
                timeStr,
                strength.ToString("F0"),
                hp.ToString("F0"));
        }

        public static FloatMenuOption MakeRemoveFortificationsMenuOption(WorldObject outpost, CompViralSpread comp)
        {
            Texture2D icon = ClearRoadBlockIcon;
            bool roadBusy = comp.roadTargetTile != -1;
            bool blockBusy = WorldActions_RoadBlocks.HasActiveRoadBlockProject(comp);
            bool trapBusy = WorldActions_SpikeTraps.HasActiveSpikeTrapProject(comp);
            bool decontamBusy = WorldActions_Decontamination.HasActiveDecontaminationProject(comp);

            if (blockBusy && comp.roadBlockIsClearing)
            {
                string insufficient = comp.GetInsufficientStrengthConstructionMessage();
                string label = insufficient
                    ?? "TSA_WD_FortificationClearStatus".Translate(
                        comp.roadBlockTargetName.NullOrEmpty() ? "â€¦" : comp.roadBlockTargetName,
                        (Mathf.Min(1f, comp.roadBlockProgress) * 100f).ToString("F0")).ToString();
                return new FloatMenuOption(label, () => { }, icon, Color.white) { Disabled = true };
            }

            if (blockBusy)
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

            if (trapBusy)
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
                "TSA_WD_RemoveFortifications".Translate(),
                WdCascadingFloatMenu.WrapLeaf(() => StartRoadBlockTargeting(outpost, comp, clearing: true, clearAnyFortification: true)),
                icon,
                Color.white)
            {
                tooltip = "TSA_WD_RemoveFortificationsDesc".Translate()
            };
        }

        /// <summary>Obsolete separate clear; kept for call-site compatibility. Prefer <see cref="MakeRemoveFortificationsMenuOption"/>.</summary>
        public static FloatMenuOption MakeClearRoadBlocksMenuOption(WorldObject outpost, CompViralSpread comp) =>
            MakeRemoveFortificationsMenuOption(outpost, comp);

        private static void StartRoadBlockTargeting(WorldObject source, CompViralSpread comp, bool clearing) =>
            StartRoadBlockTargeting(source, comp, clearing, clearAnyFortification: false);

        private static void StartRoadBlockTargeting(WorldObject source, CompViralSpread comp, bool clearing, bool clearAnyFortification)
        {
            CameraJumper.TryJump(source.Tile);
            float range = WorldActions_RoadBlocks.GetMaxRange(source);
            PlanetLayer layer = PlanetSurfaceWorldActions.LayerOf(source);

            // Clicked nodes only (never includes the outpost). First node = first build tile.
            var sessionNodes = new List<int>();
            List<int> committedNodePathTiles = null; // path along nodes only (no outpost leg)
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

            bool NodePlanOk(int tile) =>
                WorldActions_RoadBlocks.IsValidBuildPlanNode(tile);

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
                        Messages.Message("TSA_WD_RoadBlockWaypointOutOfRange".Translate(), MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    if (lastNode >= 0 && target.Tile == lastNode)
                        return false;

                    // First node must be reachable from the outpost (crew travel), but that path is never planned/drawn.
                    if (lastNode < 0 && !TileReachableFromOutpost(target.Tile))
                    {
                        Messages.Message("TSA_WD_RoadBlockNoPath".Translate(), MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    if (lastNode >= 0)
                    {
                        List<int> leg = WorldActions_RoadBlocks.FindFlatHopPathDestFirst(lastNode, target.Tile);
                        if (leg == null || leg.Count < 2)
                        {
                            Messages.Message("TSA_WD_RoadBlockNoPath".Translate(), MessageTypeDefOf.RejectInput);
                            return false;
                        }
                    }

                    bool nodeOk = clearing ? NodePlanOk(target.Tile) : WorldActions_RoadBlocks.IsValidBuildPlanNode(target.Tile);
                    if (!nodeOk && lastNode < 0)
                    {
                        Messages.Message(
                            clearing ? "TSA_WD_FortificationClearEmpty".Translate() : "TSA_WD_RoadBlockBuildEmpty".Translate(),
                            MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    if (shift)
                    {
                        if (!nodeOk && !clearing)
                        {
                            Messages.Message("TSA_WD_RoadBlockBuildEmpty".Translate(), MessageTypeDefOf.RejectInput);
                            return false;
                        }
                        if (!nodeOk && clearing)
                        {
                            Messages.Message("TSA_WD_FortificationClearEmpty".Translate(), MessageTypeDefOf.RejectInput);
                            return false;
                        }

                        sessionNodes.Add(target.Tile);
                        committedNodePathTiles = BuildPathAlongNodes(sessionNodes);
                        previewMouseTile = int.MinValue;
                        previewLegTiles = null;
                        return false;
                    }

                    var clickedNodes = new List<int>(sessionNodes) { target.Tile };
                    List<int> planned = WorldActions_RoadBlocks.FilterPlannedTilesFromClickedNodes(
                        clickedNodes,
                        clearing,
                        clearing ? RoadBlockKind.Normal : comp.selectedRoadBlockKind,
                        source.Faction,
                        clearAnyFortification);
                    if (planned == null || planned.Count == 0)
                    {
                        Messages.Message(
                            clearing ? "TSA_WD_FortificationClearEmpty".Translate() : "TSA_WD_RoadBlockBuildEmpty".Translate(),
                            MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    string targetName = clickedNodes.Count > 0
                        ? clickedNodes[clickedNodes.Count - 1].ToString()
                        : target.Tile.ToString();

                    var drawPath = BuildPathAlongNodes(clickedNodes);
                    comp.roadBlockPlannedTiles = planned;
                    comp.roadBlockClickedNodes = new List<int>(clickedNodes);
                    comp.roadBlockCachedPathTiles = drawPath != null
                        ? new List<int>(drawPath)
                        : new List<int>(clickedNodes);
                    comp.roadBlockWorkIndex = 0;
                    comp.roadBlockProgress = 0f;
                    comp.roadBlockIsClearing = clearing;
                    comp.roadBlockClearAnyFortification = clearing && clearAnyFortification;
                    comp.roadBlockTargetName = targetName;
                    if (!clearing)
                        comp.selectedRoadBlockKind = comp.selectedRoadBlockKind;
                    else
                        comp.selectedRoadBlockKind = RoadBlockKind.Normal;
                    comp.roadBlockCachedWorkTile = WorldActions_RoadBlocks.GetCurrentWorkTile(comp);

                    Messages.Message(
                        clearing
                            ? "TSA_WD_FortificationClearTargetSet".Translate(planned.Count)
                            : "TSA_WD_RoadBlockBuildTargetSet".Translate(planned.Count),
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
                                ? WorldActions_RoadBlocks.IsValidBuildPlanNode(mouseTile)
                                : WorldActions_RoadBlocks.IsValidBuildPlanNode(mouseTile)))
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
                        ? "TSA_WD_FortificationClearTip".Translate()
                        : "TSA_WD_RoadBlockBuildTip".Translate();
                },
                (target) =>
                {
                    if (target.Tile < 0 || target.Tile == source.Tile) return false;
                    if (Find.WorldGrid.ApproxDistanceInTiles(source.Tile, target.Tile) > range)
                        return false;
                    return clearing
                        ? WorldActions_RoadBlocks.IsValidBuildPlanNode(target.Tile)
                        : WorldActions_RoadBlocks.IsValidBuildPlanNode(target.Tile);
                });
        }

        private static List<int> BuildPathAlongNodes(List<int> nodes)
        {
            if (nodes == null || nodes.Count < 2) return null;

            var forward = new List<int> { nodes[0] };
            for (int i = 0; i < nodes.Count - 1; i++)
            {
                List<int> pathDestFirst = WorldActions_RoadBlocks.FindFlatHopPathDestFirst(nodes[i], nodes[i + 1]);
                if (pathDestFirst == null || pathDestFirst.Count < 2) return null;
                // dest-first â†’ append travel order after first tile (already in forward).
                for (int n = pathDestFirst.Count - 2; n >= 0; n--)
                    forward.Add(pathDestFirst[n]);
            }

            // Convert to dest-first for DrawRoadPathFromCalculatedNodes.
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

        public static void DrawRoadBlockOverlayIfSelected(WorldObject worldObject)
        {
            if (worldObject == null || !Find.WorldSelector.IsSelected(worldObject)) return;
            if (worldObject.Faction != Faction.OfPlayer) return;
            if (!(worldObject is WorldObject_WD_Outpost) && !ColonyWorldBuildUtility.IsPlayerColonyBuildActor(worldObject))
                return;

            var comp = worldObject.GetComponent<CompViralSpread>();
            if (!WorldActions_RoadBlocks.HasActiveRoadBlockProject(comp)) return;

            EnsureRoadBlockOverlayCache(comp);

            // Same as road building: full path + X on intermediate nodes + star on final.
            if (comp.roadBlockCachedPathTiles != null && comp.roadBlockCachedPathTiles.Count >= 2)
                Action_Outpost_BuildRoad.DrawRoadPathFromCalculatedNodes(comp.roadBlockCachedPathTiles, LineMat);

            var nodes = comp.roadBlockClickedNodes;
            if (nodes == null || nodes.Count == 0)
                nodes = comp.roadBlockPlannedTiles;
            if (nodes != null && nodes.Count > 0)
            {
                for (int i = 0; i < nodes.Count - 1; i++)
                    Action_Outpost_BuildRoad.DrawOrangeX(nodes[i]);
                Action_Outpost_BuildRoad.DrawOrangeStar(nodes[nodes.Count - 1]);
            }

            if (comp.roadBlockCachedWorkTile != -1)
                Action_Outpost_BuildRoad.DrawOrangeCircle(comp.roadBlockCachedWorkTile);
        }

        /// <summary>
        /// Rebuild draw cache when missing or too short to draw a line.
        /// Older saves may only have work tiles; also recovers after list clears that left planned tiles intact.
        /// </summary>
        private static void EnsureRoadBlockOverlayCache(CompViralSpread comp)
        {
            if (comp == null) return;

            var planned = comp.roadBlockPlannedTiles;
            if (planned == null || planned.Count == 0) return;

            if (comp.roadBlockClickedNodes == null || comp.roadBlockClickedNodes.Count == 0)
                comp.roadBlockClickedNodes = new List<int>(planned);

            bool needPath = comp.roadBlockCachedPathTiles == null || comp.roadBlockCachedPathTiles.Count < 2;
            if (!needPath && !PathHasAdjacentHops(comp.roadBlockCachedPathTiles))
                needPath = true;

            if (!needPath) return;

            var nodes = comp.roadBlockClickedNodes;
            if (nodes != null && nodes.Count >= 2)
            {
                var drawPath = BuildPathAlongNodes(nodes);
                comp.roadBlockCachedPathTiles = drawPath != null
                    ? new List<int>(drawPath)
                    : new List<int>(nodes);
            }
            else if (planned.Count >= 2)
            {
                var drawPath = BuildPathAlongNodes(planned);
                comp.roadBlockCachedPathTiles = drawPath != null
                    ? new List<int>(drawPath)
                    : new List<int>(planned);
            }
            else
            {
                comp.roadBlockCachedPathTiles = new List<int>(nodes ?? planned);
            }
        }

        private static bool PathHasAdjacentHops(List<int> tiles)
        {
            if (tiles == null || tiles.Count < 2) return false;
            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return false;
            for (int i = 0; i < tiles.Count - 1; i++)
            {
                int a = tiles[i];
                int b = tiles[i + 1];
                if (grid.InBounds(a) && grid.InBounds(b) && grid.IsNeighbor(a, b))
                    return true;
            }
            return false;
        }
    }
}
