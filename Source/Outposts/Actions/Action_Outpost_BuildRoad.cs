using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using UnityEngine;

namespace TSA_WorldDomination
{
    [StaticConstructorOnStartup]
    public static class Action_Outpost_BuildRoad
    {
        private static Texture2D cachedBuildRoadIcon;
        private static Texture2D cachedRemoveRoadIcon;
        private static Texture2D cachedCancelIcon;
        private static Material orangeWorkCircleMat;

        public static Material RoadLineOrange => WorldOverlayLineMaterials.RoadOrange;

        /// <summary>90% opaque filled orange disc for the current worksite tile (shared by all build projects).</summary>
        private static Material OrangeWorkCircleMat =>
            orangeWorkCircleMat ??= CreateOrangeWorkCircleMat();

        private static Material CreateOrangeWorkCircleMat()
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.ARGB32, false)
            {
                name = "TSA_WD_OrangeWorkCircle",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            float mid = (size - 1) * 0.5f;
            float r = mid - 0.5f;
            float r2 = r * r;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - mid;
                    float dy = y - mid;
                    tex.SetPixel(x, y, dx * dx + dy * dy <= r2 ? Color.white : Color.clear);
                }
            }
            tex.Apply(false, true);

            Color orange = ColorLibrary.Orange;
            orange.a = 0.9f;
            return MaterialPool.MatFrom(tex, ShaderDatabase.Transparent, orange, 3590);
        }

        public static Texture2D BuildRoadIcon =>
            cachedBuildRoadIcon ??= ContentFinder<Texture2D>.Get("UI/Commands/BuildRoad", false) ?? TexCommand.Replant;

        public static Texture2D RemoveRoadIcon =>
            cachedRemoveRoadIcon ??= ContentFinder<Texture2D>.Get("UI/Commands/RemoveRoad", false) ?? BuildRoadIcon;

        /// <summary>Cancel gizmo only; start actions live under <see cref="Action_Outpost_Build"/>.</summary>
        public static IEnumerable<Gizmo> GetGizmos(WorldObject outpost)
        {
            if (outpost == null || outpost.Faction != Faction.OfPlayer) yield break;

            var comp = outpost.GetComponent<CompViralSpread>();
            if (comp == null || comp.roadTargetTile == -1) yield break;

            yield return new Command_Action
            {
                defaultLabel = comp.roadIsClearing
                    ? "TSA_WD_CancelRoadRemoval".Translate()
                    : "TSA_WD_CancelRoad".Translate(),
                defaultDesc = comp.roadIsClearing
                    ? "TSA_WD_CancelRoadRemovalDesc".Translate()
                    : "TSA_WD_CancelRoadDesc".Translate(),
                icon = cachedCancelIcon ??= ContentFinder<Texture2D>.Get("UI/Designators/Cancel"),
                action = delegate
                {
                    bool wasClearing = comp.roadIsClearing;
                    WorldActions_Roads.DestroyActiveRoadBuildersFrom(outpost);
                    WorldActions_Roads.ClearRoadProject(comp, RoadProjectClearReason.PlayerCancel);
                    Messages.Message(
                        wasClearing
                            ? "TSA_WD_RoadRemovalCancelled".Translate(outpost.LabelCap)
                            : "TSA_WD_RoadCancelled".Translate(outpost.LabelCap),
                        MessageTypeDefOf.NeutralEvent);
                }
            };
        }

        public static FloatMenuOption MakeBuildRoadMenuOption(WorldObject outpost, CompViralSpread comp)
        {
            Texture2D icon = BuildRoadIcon;
            if (comp.roadTargetTile != -1)
            {
                if (comp.roadIsClearing)
                {
                    return new FloatMenuOption("TSA_WD_CancelCurrentProjectFirst".Translate(), () => { }, icon, Color.white)
                    {
                        Disabled = true
                    };
                }

                string insufficient = comp.GetInsufficientStrengthConstructionMessage();
                if (insufficient != null)
                {
                    return new FloatMenuOption(insufficient, () => { }, icon, Color.white) { Disabled = true };
                }
                string targetName = !comp.roadTargetName.NullOrEmpty() ? comp.roadTargetName : "Tile " + comp.roadTargetTile;
                string label = "TSA_WD_BuildRoadStatus".Translate(targetName, (Mathf.Min(1f, comp.roadProgress) * 100f).ToString("F0"));
                var busy = new FloatMenuOption(label, () => { }, icon, Color.white) { Disabled = true };
                return busy;
            }

            if (WorldActions_RoadBlocks.HasActiveRoadBlockProject(comp))
            {
                return new FloatMenuOption("TSA_WD_CancelCurrentProjectFirst".Translate(), () => { }, icon, Color.white)
                {
                    Disabled = true
                };
            }

            if (WorldActions_SpikeTraps.HasActiveSpikeTrapProject(comp))
            {
                return new FloatMenuOption("TSA_WD_CancelCurrentProjectFirst".Translate(), () => { }, icon, Color.white)
                {
                    Disabled = true
                };
            }

            if (WorldActions_Decontamination.HasActiveDecontaminationProject(comp))
            {
                return new FloatMenuOption("TSA_WD_CancelCurrentProjectFirst".Translate(), () => { }, icon, Color.white)
                {
                    Disabled = true
                };
            }

            var branch = WdCascadingFloatMenu.MakeBranchOption(
                "TSA_WD_BuildRoad".Translate(),
                () => OpenRoadTypeMenu(outpost, comp),
                icon,
                Color.white);
            branch.tooltip = "TSA_WD_BuildRoadDesc".Translate();
            return branch;
        }

        public static FloatMenuOption MakeRemoveRoadsMenuOption(WorldObject outpost, CompViralSpread comp)
        {
            Texture2D icon = RemoveRoadIcon;
            if (comp.roadTargetTile != -1)
            {
                if (!comp.roadIsClearing)
                {
                    return new FloatMenuOption("TSA_WD_CancelCurrentProjectFirst".Translate(), () => { }, icon, Color.white)
                    {
                        Disabled = true
                    };
                }

                string insufficient = comp.GetInsufficientStrengthConstructionMessage();
                if (insufficient != null)
                {
                    return new FloatMenuOption(insufficient, () => { }, icon, Color.white) { Disabled = true };
                }
                string targetName = !comp.roadTargetName.NullOrEmpty() ? comp.roadTargetName : "Tile " + comp.roadTargetTile;
                string label = "TSA_WD_RemoveRoadsStatus".Translate(targetName, (Mathf.Min(1f, comp.roadProgress) * 100f).ToString("F0"));
                return new FloatMenuOption(label, () => { }, icon, Color.white) { Disabled = true };
            }

            if (WorldActions_RoadBlocks.HasActiveRoadBlockProject(comp)
                || WorldActions_SpikeTraps.HasActiveSpikeTrapProject(comp)
                || WorldActions_Decontamination.HasActiveDecontaminationProject(comp)
                || WorldActions_AtTurrets.HasActiveAtTurretProject(comp))
            {
                return new FloatMenuOption("TSA_WD_CancelCurrentProjectFirst".Translate(), () => { }, icon, Color.white)
                {
                    Disabled = true
                };
            }

            return new FloatMenuOption(
                "TSA_WD_RemoveRoads".Translate(),
                WdCascadingFloatMenu.WrapLeaf(() => StartRoadTargeting(outpost, comp, clearing: true)),
                icon,
                Color.white)
            {
                tooltip = "TSA_WD_RemoveRoadsDesc".Translate()
            };
        }

        public static void OpenRoadTypeMenu(WorldObject outpost, CompViralSpread comp)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            float totalConstruction = GetTotalConstructionSkill(outpost);
            Texture2D roadIcon = BuildRoadIcon;

            int dirtMinConstruction = WorldActions_Roads.GetMinConstructionToBuildRoad(SettlementTier.T1);
            var dirtOption = new FloatMenuOption("TSA_WD_RoadDirt".Translate(), WdCascadingFloatMenu.WrapLeaf(() =>
            {
                comp.selectedRoadTier = SettlementTier.T1;
                StartRoadTargeting(outpost, comp);
            }), roadIcon, Color.white)
            {
                tooltip = BuildRoadKindTooltip(outpost, SettlementTier.T1)
            };
            ColonyWorldBuildRequirements.ApplyGate(
                dirtOption,
                totalConstruction,
                dirtMinConstruction,
                ColonyWorldBuildRequirements.GetRequiredResearchForRoad(SettlementTier.T1),
                ColonyWorldBuildRequirements.GetMaterialCostsForRoad(SettlementTier.T1));
            options.Add(dirtOption);

            int stoneMinConstruction = WorldActions_Roads.GetMinConstructionToBuildRoad(SettlementTier.T2);
            var stoneOption = new FloatMenuOption("TSA_WD_RoadStone".Translate(), WdCascadingFloatMenu.WrapLeaf(() =>
            {
                comp.selectedRoadTier = SettlementTier.T2;
                StartRoadTargeting(outpost, comp);
            }), roadIcon, Color.white)
            {
                tooltip = BuildRoadKindTooltip(outpost, SettlementTier.T2)
            };
            ColonyWorldBuildRequirements.ApplyGate(
                stoneOption,
                totalConstruction,
                stoneMinConstruction,
                ColonyWorldBuildRequirements.GetRequiredResearchForRoad(SettlementTier.T2),
                ColonyWorldBuildRequirements.GetMaterialCostsForRoad(SettlementTier.T2));
            options.Add(stoneOption);

            int asphaltMinConstruction = WorldActions_Roads.GetMinConstructionToBuildRoad(SettlementTier.T3);
            var asphaltOption = new FloatMenuOption("TSA_WD_RoadAsphalt".Translate(), WdCascadingFloatMenu.WrapLeaf(() =>
            {
                comp.selectedRoadTier = SettlementTier.T3;
                StartRoadTargeting(outpost, comp);
            }), roadIcon, Color.white)
            {
                tooltip = BuildRoadKindTooltip(outpost, SettlementTier.T3)
            };
            ColonyWorldBuildRequirements.ApplyGate(
                asphaltOption,
                totalConstruction,
                asphaltMinConstruction,
                ColonyWorldBuildRequirements.GetRequiredResearchForRoad(SettlementTier.T3),
                ColonyWorldBuildRequirements.GetMaterialCostsForRoad(SettlementTier.T3));
            options.Add(asphaltOption);

            WdCascadingFloatMenu.OpenAsChild(options, () => Action_Outpost_Build.BuildRootMenuOptions(outpost, comp));
        }

        private static string BuildRoadKindTooltip(WorldObject actor, SettlementTier tier)
        {
            var s = WorldDominationMod.settings;
            float work = s != null ? s.GetFallbackRoadWork(tier) : WorldDominationSettings.DefFallbackDirtRoadWork;
            float movement = s != null ? s.GetFallbackRoadMovement(tier) : WorldDominationSettings.DefFallbackDirtRoadMovement;
            float strength = s != null ? s.GetFallbackRoadExpeditionStrength(tier) : WorldDominationSettings.DefFallbackDirtRoadExpeditionStrength;
            string timeStr = "-";
            if (actor != null)
            {
                float days = WorldActions_Roads.GetEstimatedDaysPerRoadSegment(actor, tier);
                if (days >= 0f) timeStr = days.ToString("F2");
            }
            if (ColonyWorldBuildUtility.IsPlayerColonyBuildActor(actor))
                strength = 0f;
            return "TSA_WD_RoadKindTooltip".Translate(
                work.ToString("F0"),
                movement.ToString("0.##"),
                timeStr,
                strength.ToString("F0"));
        }

        private static float GetTotalConstructionSkill(WorldObject outpost)
        {
            // Road tier unlocks are gates — use raw cumulative Construction.
            return ColonyWorldBuildUtility.GetActorConstructionSkillRaw(outpost);
        }

        private static void StartRoadTargeting(WorldObject source, CompViralSpread comp)
        {
            StartRoadTargeting(source, comp, null, clearing: false);
        }

        private static void StartRoadTargeting(WorldObject source, CompViralSpread comp, bool clearing)
        {
            StartRoadTargeting(source, comp, null, clearing);
        }

        public static void StartRoadTargeting(WorldObject source, CompViralSpread comp, Action<RoadTargetSelection> onTargetAccepted)
        {
            StartRoadTargeting(source, comp, onTargetAccepted, clearing: false);
        }

        public static void StartRoadTargeting(WorldObject source, CompViralSpread comp, Action<RoadTargetSelection> onTargetAccepted, bool clearing)
        {
            CameraJumper.TryJump(source.Tile);
            float range = WorldDominationMod.settings.maxRoadRange;
            if (source is WorldObject_WD_Outpost wdOutpost)
                range *= 1f + OutpostExpertUtility.GetEngineerConstructionRadiusBonus(wdOutpost);
            PlanetLayer layer = PlanetSurfaceWorldActions.LayerOf(source);

            // Session nodes are explicit clicks on the road corridor.
            // First click sets corridor start; final non-shift click sets corridor end.
            var sessionNodes = new List<int>();
            List<int> committedPathTiles = null; // dest-first through committed nodes only (no source leg)
            int previewMouseTile = int.MinValue;
            List<int> previewLegTiles = null;
            int previewThrottleFrame = 0;

            int LastNodeOrNone() => sessionNodes.Count > 0 ? sessionNodes[sessionNodes.Count - 1] : -1;

            bool TileReachableFromSource(int tile)
            {
                // Source itself is a valid first-node start (no path needed).
                if (tile == source.Tile) return true;
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

                    bool shift = Event.current != null && Event.current.shift;
                    int lastNode = LastNodeOrNone();
                    // After the first node, reject re-clicking the current anchor.
                    // First click may be the initiating outpost/settlement itself.
                    if (lastNode >= 0 && target.Tile == lastNode)
                        return false;

                    // Project range always from the building outpost/colony (not the last waypoint).
                    if (Find.WorldGrid.ApproxDistanceInTiles(source.Tile, target.Tile) > range)
                    {
                        Messages.Message("TSA_WD_RoadWaypointOutOfRange".Translate(), MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    // Detached-start mode: first click can be the source or anywhere within range.
                    // Ensure crews can still reach that tile over land (source tile always ok).
                    if (lastNode < 0 && !TileReachableFromSource(target.Tile))
                    {
                        Messages.Message("TSA_WD_RoadAlreadyExists".Translate(), MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    if (shift || sessionNodes.Count == 0)
                    {
                        // First click sets start node. Shift-click continues adding intermediate nodes.
                        if (lastNode >= 0)
                        {
                            if (!WorldActions_Roads.TryBuildPathAlongWaypoints(
                                new List<int> { lastNode, target.Tile }, out _))
                            {
                                Messages.Message("TSA_WD_RoadAlreadyExists".Translate(), MessageTypeDefOf.RejectInput);
                                return false;
                            }
                        }

                        sessionNodes.Add(target.Tile);
                        committedPathTiles = BuildPathAlongNodes(sessionNodes);
                        previewMouseTile = int.MinValue;
                        previewLegTiles = null;
                        Messages.Message("TSA_WD_RoadWaypointAdded".Translate(sessionNodes.Count), MessageTypeDefOf.TaskCompletion);
                        return false; // keep targeting
                    }

                    // Final destination (non-shift click after at least one node exists).
                    var clickedNodes = new List<int>(sessionNodes.Count + 1);
                    clickedNodes.AddRange(sessionNodes);
                    clickedNodes.Add(target.Tile);
                    if (!WorldActions_Roads.TryBuildPathAlongWaypoints(clickedNodes, out List<int> fullPath))
                    {
                        Messages.Message("TSA_WD_RoadAlreadyExists".Translate(), MessageTypeDefOf.RejectInput);
                        return false;
                    }
                    if (WorldActions_Roads.RoadBuildingTileListTouchesWater(fullPath))
                    {
                        Messages.Message("TSA_WD_RoadPathCrossesWater".Translate(), MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    int currentGap;
                    int segmentCount;
                    if (clearing)
                    {
                        currentGap = WorldActions_Roads.GetFirstRoadRemovalWorkTileOnTileList(fullPath, source.Tile);
                        if (currentGap == -1)
                        {
                            Messages.Message("TSA_WD_RoadRemovalEmpty".Translate(), MessageTypeDefOf.RejectInput);
                            return false;
                        }
                        segmentCount = WorldActions_Roads.CountRoadRemovalSegmentsOnTileList(fullPath);
                    }
                    else
                    {
                        currentGap = WorldActions_Roads.GetFirstRoadWorkTileOnTileList(fullPath, comp.selectedRoadTier, source.Tile);
                        if (currentGap == -1)
                        {
                            Messages.Message("TSA_WD_RoadAlreadyExists".Translate(), MessageTypeDefOf.RejectInput);
                            return false;
                        }
                        segmentCount = WorldActions_Roads.CountRoadWorkSegmentsOnTileList(fullPath, comp.selectedRoadTier);
                    }

                    var worldObject = Find.WorldObjects.ObjectsAt(target.Tile).FirstOrDefault(x => x is Settlement || x is WorldObject_WD_Outpost);
                    string targetName = worldObject != null ? worldObject.LabelCap : "Tile " + target.Tile;
                    var waypointsCopy = new List<int>(sessionNodes);

                    if (onTargetAccepted != null)
                    {
                        onTargetAccepted(new RoadTargetSelection
                        {
                            TargetTile = target.Tile,
                            TargetName = targetName,
                            PathTiles = fullPath,
                            WaypointTiles = waypointsCopy,
                            WorkTile = currentGap,
                            SegmentCount = segmentCount
                        });
                        return true;
                    }

                    if (clearing)
                        comp.selectedRoadTier = SettlementTier.T1;
                    comp.roadIsClearing = clearing;
                    comp.roadTargetTile = target.Tile;
                    comp.roadProgress = 0f;
                    comp.cachedRoadPathTiles = fullPath;
                    comp.roadWaypointTiles = waypointsCopy;
                    comp.lastPathSourceTile = source.Tile;
                    comp.roadTargetUsesDetachedStart = true;
                    comp.cachedWorkTile = currentGap;
                    comp.roadTargetName = targetName;
                    Messages.Message(
                        clearing
                            ? "TSA_WD_RoadRemovalTargetSet".Translate(segmentCount)
                            : "TSA_WD_RoadTargetSet".Translate(comp.roadTargetName),
                        MessageTypeDefOf.PositiveEvent);
                    return true;
                },
                true,
                null,
                false,
                () =>
                {
                    WD_RadiusOverlayMode.DrawOrFill(new PlanetTile(source.Tile, layer), range, OutpostCoverageFillKind.Orange, RoadLineOrange);
                    DrawRoadPathFromCalculatedNodes(committedPathTiles, RoadLineOrange);
                    DrawWaypointMarkers(sessionNodes, layer);

                    int mouseTile = GenWorld.MouseTile();
                    int anchor = LastNodeOrNone();
                    if (anchor < 0)
                    {
                        previewMouseTile = mouseTile;
                        previewLegTiles = null;
                        if (mouseTile >= 0
                            && Find.WorldGrid.ApproxDistanceInTiles(source.Tile, mouseTile) <= range
                            && TileReachableFromSource(mouseTile))
                        {
                            DrawOrangeStar(mouseTile);
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
                        DrawRoadPathFromCalculatedNodes(previewLegTiles, RoadLineOrange);
                        if (mouseTile >= 0 && previewLegTiles != null && previewLegTiles.Count >= 2)
                            DrawOrangeStar(mouseTile);
                    }
                },
                (target) =>
                {
                    if (sessionNodes.Count > 0)
                        return "TSA_WD_RoadWaypointLabel".Translate(sessionNodes.Count);
                    return clearing
                        ? "TSA_WD_RoadRemovalTip".Translate()
                        : "TSA_WD_RoadWaypointTip".Translate();
                },
                (target) =>
                {
                    if (!target.IsValid || target.Tile < 0) return false;
                    int anchor = LastNodeOrNone();
                    if (Find.WorldGrid.ApproxDistanceInTiles(source.Tile, target.Tile) > range) return false;
                    PlanetLayer pLayer = PlanetSurfaceWorldActions.LayerOf(source);
                    PlanetTile pTile = new PlanetTile(target.Tile, pLayer);
                    if (Find.World.Impassable(pTile)) return false;
                    if (Find.WorldGrid.InBounds(target.Tile) && Find.WorldGrid[target.Tile].WaterCovered)
                        return false;

                    // First node: no path preview from source (detached start). Reachability checked on confirm.
                    if (anchor < 0)
                        return true;

                    if (previewMouseTile == target.Tile)
                        return previewLegTiles != null && previewLegTiles.Count >= 2;
                    return true;
                },
                null,
                true
            );
        }

        /// <summary>Dest-first path along clicked nodes only (no implicit source leg).</summary>
        private static List<int> BuildPathAlongNodes(List<int> nodes)
        {
            if (nodes == null || nodes.Count < 2) return null;
            return WorldActions_Roads.TryBuildPathAlongWaypoints(nodes, out List<int> pathTiles) ? pathTiles : null;
        }

        private static List<int> CalculatePathNodes(int start, int end)
        {
            // Surface-only road preview; resolve the surface layer explicitly rather than via WorldGrid[int].
            PlanetLayer layer = PlanetSurfaceWorldActions.WdSurfaceLayer;
            using (WorldPath path = layer.Pather.FindPath(new PlanetTile(start, layer), new PlanetTile(end, layer), null))
            {
                if (path == null || !path.Found) return null;
                var nodes = path.NodesReversed;
                var result = new List<int>(nodes.Count);
                for (int i = 0; i < nodes.Count; i++)
                    result.Add(nodes[i].tileId);
                return result;
            }
        }

        public static void DrawRoadPathFromCalculatedNodes(List<int> tileIds, Material overrideMat = null)
        {
            if (tileIds == null || tileIds.Count < 2 || overrideMat == null) return;
            WorldGrid worldGrid = Find.WorldGrid;
            if (worldGrid == null) return;

            // Draw only adjacent hops so a bad junction cannot create a long chord through the planet.
            // Tile lists are dest-first (WorldPath.NodesReversed); adjacency is what matters for visuals.
            for (int i = 0; i < tileIds.Count - 1; i++)
            {
                int a = tileIds[i];
                int b = tileIds[i + 1];
                if (!worldGrid.InBounds(a) || !worldGrid.InBounds(b) || !worldGrid.IsNeighbor(a, b))
                    continue;
                GenDraw_WorldLineSmooth.DrawSmoothWorldLine(a, b, worldGrid, overrideMat, 1f, segments: 1);
            }
        }

        private static void DrawWaypointMarkers(List<int> waypoints, PlanetLayer layer)
        {
            if (waypoints == null || waypoints.Count == 0) return;
            for (int i = 0; i < waypoints.Count; i++)
                DrawOrangeX(waypoints[i]);
        }

        private static bool TryGetPlanetMarkerFrame(int tileId, out Vector3 center, out Vector3 tangent, out Vector3 bitangent, out float arm)
        {
            center = tangent = bitangent = default;
            arm = 0f;
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || !grid.InBounds(tileId)) return false;

            center = grid.GetTileCenter(tileId);
            Vector3 n = center.normalized;
            center += n * 0.08f;
            tangent = Vector3.Cross(n, Vector3.up);
            if (tangent.sqrMagnitude < 1e-6f)
                tangent = Vector3.Cross(n, Vector3.right);
            tangent.Normalize();
            bitangent = Vector3.Cross(n, tangent).normalized;
            arm = grid.AverageTileSize * 0.32f;
            return true;
        }

        public static void DrawOrangeX(int tileId)
        {
            if (!TryGetPlanetMarkerFrame(tileId, out Vector3 c, out Vector3 tangent, out Vector3 bitangent, out float arm))
                return;
            Material mat = WorldOverlayLineMaterials.RoadOrangeMarker;
            Vector3 d1 = (tangent + bitangent).normalized * arm;
            Vector3 d2 = (tangent - bitangent).normalized * arm;
            GenDraw.DrawWorldLineBetween(c + d1, c - d1, mat, 1.35f);
            GenDraw.DrawWorldLineBetween(c + d2, c - d2, mat, 1.35f);
        }

        public static void DrawOrangeStar(int tileId)
        {
            if (!TryGetPlanetMarkerFrame(tileId, out Vector3 c, out Vector3 tangent, out Vector3 bitangent, out float arm))
                return;
            Material mat = WorldOverlayLineMaterials.RoadOrangeMarker;
            // 4 equal-length diameters: + and ×.
            GenDraw.DrawWorldLineBetween(c + tangent * arm, c - tangent * arm, mat, 1.35f);
            GenDraw.DrawWorldLineBetween(c + bitangent * arm, c - bitangent * arm, mat, 1.35f);
            Vector3 d1 = (tangent + bitangent).normalized * arm;
            Vector3 d2 = (tangent - bitangent).normalized * arm;
            GenDraw.DrawWorldLineBetween(c + d1, c - d1, mat, 1.35f);
            GenDraw.DrawWorldLineBetween(c + d2, c - d2, mat, 1.35f);
        }

        /// <summary>Next worksite tile marker: filled opaque orange disc (roads, blocks, traps, AT, decontam).</summary>
        public static void DrawOrangeCircle(int tileId, int segments = 20)
        {
            _ = segments;
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || !grid.InBounds(tileId)) return;

            // Match prior outline diameter (arm = AverageTileSize * 0.32).
            float size = grid.AverageTileSize * 0.64f;
            Vector3 center = grid.GetTileCenter(tileId);
            WorldRendererUtility.DrawQuadTangentialToPlanet(
                center,
                size,
                WD_WorldMapZoomUtil.GetSurfaceOverlayDrawAltitude(),
                OrangeWorkCircleMat);
        }

        public static void DrawRoadOverlayIfSelected(WorldObject worldObject)
        {
            if (worldObject == null || !Find.WorldSelector.IsSelected(worldObject)) return;

            var comp = worldObject.GetComponent<CompViralSpread>();
            if (comp == null || comp.roadTargetTile == -1) return;

            bool isPlayerOutpost = worldObject.Faction == Faction.OfPlayer && worldObject is WorldObject_WD_Outpost;
            bool isColonyBuild = ColonyWorldBuildUtility.IsPlayerColonyBuildActor(worldObject);
            bool isOrderedSettlement = worldObject is Settlement && comp.playerOrderedRoad;
            if (!isPlayerOutpost && !isColonyBuild && !isOrderedSettlement) return;

            if (comp.cachedRoadPathTiles != null && comp.cachedRoadPathTiles.Count > 0)
            {
                DrawRoadPathFromCalculatedNodes(comp.cachedRoadPathTiles, RoadLineOrange);
                DrawOrangeStar(comp.roadTargetTile);
                DrawWaypointMarkers(comp.roadWaypointTiles, PlanetSurfaceWorldActions.LayerOf(worldObject));
            }

            if (comp.cachedWorkTile != -1)
                DrawOrangeCircle(comp.cachedWorkTile);
        }
    }
}
