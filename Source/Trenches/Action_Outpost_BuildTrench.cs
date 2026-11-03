using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    [StaticConstructorOnStartup]
    public static class Action_Outpost_BuildTrench
    {
        private static Texture2D cachedBuildIcon;
        private static Texture2D cachedRemoveIcon;
        private static Texture2D cachedCancelIcon;

        public static Texture2D BuildIcon =>
            cachedBuildIcon ??= ContentFinder<Texture2D>.Get("UI/Commands/Build_Trench", false)
                ?? ContentFinder<Texture2D>.Get("UI/Commands/BuildRoad", false)
                ?? TexCommand.Replant;

        public static Texture2D RemoveIcon =>
            cachedRemoveIcon ??= ContentFinder<Texture2D>.Get("UI/Commands/Remove_Trench", false)
                ?? BuildIcon;

        public static IEnumerable<Gizmo> GetGizmos(WorldObject actor)
        {
            // Player trenches are outpost-only (NPC Settlements may own later).
            if (actor is not WorldObject_WD_Outpost || actor.Faction != Faction.OfPlayer) yield break;
            var comp = actor.GetComponent<CompViralSpread>();
            if (comp == null || comp.trenchTargetTile == -1) yield break;

            yield return new Command_Action
            {
                defaultLabel = comp.trenchIsClearing
                    ? "TSA_WD_CancelTrenchRemoval".Translate()
                    : "TSA_WD_CancelTrench".Translate(),
                defaultDesc = "TSA_WD_CancelTrenchDesc".Translate(),
                icon = cachedCancelIcon ??= ContentFinder<Texture2D>.Get("UI/Designators/Cancel"),
                action = delegate
                {
                    WorldActions_Trenches.DestroyActiveTrenchBuildersFrom(actor);
                    WorldActions_Trenches.ClearTrenchProject(comp);
                    Messages.Message("TSA_WD_Trench_Cancelled".Translate(actor.LabelCap), MessageTypeDefOf.NeutralEvent);
                }
            };
        }

        public static FloatMenuOption MakeBuildMenuOption(WorldObject actor, CompViralSpread comp)
        {
            Texture2D icon = BuildIcon;
            if (actor is not WorldObject_WD_Outpost)
            {
                return new FloatMenuOption("TSA_WD_BuildTrench".Translate(), null, icon, Color.cyan)
                {
                    Disabled = true,
                    tooltip = "TSA_WD_Trench_OutpostOnly".Translate()
                };
            }
            if (!WorldComponent_WdTrenches.FeatureEnabled)
            {
                return new FloatMenuOption("TSA_WD_BuildTrench".Translate(), null, icon, Color.cyan)
                {
                    Disabled = true,
                    tooltip = "TSA_WD_Trench_Disabled".Translate()
                };
            }
            if (comp.trenchTargetTile != -1)
            {
                return new FloatMenuOption("TSA_WD_CancelCurrentProjectFirst".Translate(), () => { }, icon, Color.cyan)
                {
                    Disabled = true
                };
            }
            if (HasBlockingProject(comp))
            {
                return new FloatMenuOption("TSA_WD_CancelCurrentProjectFirst".Translate(), () => { }, icon, Color.cyan)
                {
                    Disabled = true
                };
            }

            var option = new FloatMenuOption(
                "TSA_WD_BuildTrench".Translate(),
                () => StartTargeting(actor, comp, clearing: false),
                icon,
                Color.cyan)
            {
                tooltip = "TSA_WD_BuildTrenchDesc".Translate(
                    WorldComponent_WdTrenches.MaxBuildRadiusTiles.ToString(),
                    WorldComponent_WdTrenches.WoodPerSegment.ToString())
            };
            // Top-level row shows wood-per-segment cost icons (same gate pattern as road/block kinds).
            ColonyWorldBuildRequirements.ApplyGate(
                option,
                ColonyWorldBuildUtility.GetActorConstructionSkillRaw(actor),
                minConstruction: 0,
                requiredResearch: null,
                WorldActions_Trenches.GetWoodCostPerSegment());
            return option;
        }

        public static FloatMenuOption MakeRemoveMenuOption(WorldObject actor, CompViralSpread comp)
        {
            Texture2D icon = RemoveIcon;
            if (actor is not WorldObject_WD_Outpost)
            {
                return new FloatMenuOption("TSA_WD_RemoveTrench".Translate(), null, icon, Color.white)
                {
                    Disabled = true
                };
            }
            if (!WorldComponent_WdTrenches.FeatureEnabled)
            {
                return new FloatMenuOption("TSA_WD_RemoveTrench".Translate(), null, icon, Color.white)
                {
                    Disabled = true
                };
            }
            if (comp.trenchTargetTile != -1 || HasBlockingProject(comp))
            {
                return new FloatMenuOption("TSA_WD_CancelCurrentProjectFirst".Translate(), () => { }, icon, Color.white)
                {
                    Disabled = true
                };
            }

            var trenches = WorldComponent_WdTrenches.Get();
            bool any = false;
            if (trenches != null && trenches.HasAny)
            {
                var edges = trenches.Edges;
                for (int i = 0; i < edges.Count; i++)
                {
                    if (edges[i] == null) continue;
                    if (trenches.TryGetOwner(edges[i].tileA, out var o) && o == actor)
                    {
                        any = true;
                        break;
                    }
                }
            }
            if (!any)
            {
                return new FloatMenuOption("TSA_WD_RemoveTrench".Translate(), () => { }, icon, Color.white)
                {
                    Disabled = true,
                    tooltip = "TSA_WD_Trench_NoneOwned".Translate()
                };
            }

            return new FloatMenuOption("TSA_WD_RemoveTrench".Translate(), () =>
            {
                comp.trenchIsClearing = true;
                comp.trenchTargetTile = actor.Tile.tileId;
                comp.trenchProgress = 0f;
                comp.trenchTargetName = actor.LabelCap;
                comp.cachedTrenchPathTiles ??= new List<int>();
                comp.cachedTrenchPathTiles.Clear();
                Messages.Message("TSA_WD_Trench_RemovalStarted".Translate(actor.LabelCap), actor, MessageTypeDefOf.NeutralEvent);
            }, icon, Color.white)
            {
                tooltip = "TSA_WD_RemoveTrenchDesc".Translate()
            };
        }

        public static bool HasBlockingProject(CompViralSpread comp)
        {
            if (comp == null) return true;
            if (comp.roadTargetTile != -1) return true;
            if (WorldActions_BuildBridge.HasActiveBridgeProject(comp)) return true;
            if (WorldActions_RoadBlocks.HasActiveRoadBlockProject(comp)) return true;
            if (WorldActions_SpikeTraps.HasActiveSpikeTrapProject(comp)) return true;
            if (WorldActions_AtTurrets.HasActiveAtTurretProject(comp)) return true;
            if (WorldActions_Decontamination.HasActiveDecontaminationProject(comp)) return true;
            return false;
        }

        private static void StartTargeting(WorldObject actor, CompViralSpread comp, bool clearing)
        {
            if (actor is not WorldObject_WD_Outpost) return;
            CameraJumper.TryJump(actor.Tile);

            float range = WorldComponent_WdTrenches.MaxBuildRadiusTiles;
            PlanetLayer layer = PlanetSurfaceWorldActions.LayerOf(actor);
            int hubTile = actor.Tile.tileId;

            // Waypoints are legs after the forced hub start (road-style Shift+click).
            var waypoints = new List<int>();
            List<int> committedPath = null;
            int previewMouseTile = int.MinValue;
            List<int> previewLeg = null;
            int previewThrottleFrame = 0;

            int LastAnchor() => waypoints.Count > 0 ? waypoints[waypoints.Count - 1] : hubTile;

            bool InRange(int tile) =>
                Find.WorldGrid.ApproxDistanceInTiles(actor.Tile, new PlanetTile(tile)) <= range + 0.01f;

            Find.WorldTargeter.BeginTargeting(
                target =>
                {
                    if (target.Tile < 0) return false;
                    int tile = target.Tile.tileId;
                    if (tile == hubTile) return false;
                    if (tile == LastAnchor()) return false;

                    bool shift = Event.current != null && Event.current.shift;
                    if (!InRange(tile))
                    {
                        Messages.Message(
                            "TSA_WD_Trench_OutOfRange".Translate(range.ToString("F0")),
                            MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    // Plain click on owned tip / branch point (deg 1–2) = waypoint (extend or T).
                    bool treatAsWaypoint = shift || IsOwnedBranchPoint(actor, tile);
                    if (treatAsWaypoint)
                    {
                        var probe = new List<int>();
                        if (!WorldActions_Trenches.TryBuildTrenchPath(actor, tile, waypoints, probe, out string fail)
                            || probe.Count < 2)
                        {
                            Messages.Message(
                                fail.NullOrEmpty() ? "TSA_WD_Trench_InvalidPath".Translate() : fail,
                                MessageTypeDefOf.RejectInput,
                                false);
                            return false;
                        }

                        waypoints.Add(tile);
                        committedPath = probe;
                        previewMouseTile = int.MinValue;
                        previewLeg = null;
                        Messages.Message(
                            IsOwnedBranchPoint(actor, tile) && !shift
                                ? "TSA_WD_Trench_TipSelected".Translate()
                                : "TSA_WD_Trench_WaypointAdded".Translate(waypoints.Count),
                            MessageTypeDefOf.TaskCompletion);
                        return false;
                    }

                    var path = new List<int>();
                    if (!WorldActions_Trenches.TryBuildTrenchPath(actor, tile, waypoints, path, out string failReason)
                        || path.Count < 2)
                    {
                        Messages.Message(
                            failReason.NullOrEmpty() ? "TSA_WD_Trench_InvalidPath".Translate() : failReason,
                            MessageTypeDefOf.RejectInput,
                            false);
                        return false;
                    }

                    if (!WorldActions_Trenches.PathHasNewDig(path))
                    {
                        Messages.Message("TSA_WD_Trench_NeedNewDig".Translate(), MessageTypeDefOf.RejectInput, false);
                        return false;
                    }

                    comp.trenchIsClearing = false;
                    comp.trenchTargetTile = tile;
                    comp.trenchProgress = 0f;
                    comp.trenchTargetName = tile.ToString();
                    comp.cachedTrenchPathTiles ??= new List<int>();
                    comp.cachedTrenchPathTiles.Clear();
                    comp.cachedTrenchPathTiles.AddRange(path);
                    comp.trenchWaypointTiles ??= new List<int>();
                    comp.trenchWaypointTiles.Clear();
                    comp.trenchWaypointTiles.AddRange(waypoints);
                    Messages.Message(
                        "TSA_WD_Trench_BuildStarted".Translate(actor.LabelCap),
                        actor,
                        MessageTypeDefOf.NeutralEvent);
                    return true;
                },
                true,
                BuildIcon,
                false,
                () =>
                {
                    WD_RadiusOverlayMode.DrawOrFill(
                        new PlanetTile(hubTile, layer),
                        range,
                        OutpostCoverageFillKind.Orange,
                        Action_Outpost_BuildRoad.RoadLineOrange);
                    Action_Outpost_BuildRoad.DrawRoadPathFromCalculatedNodes(
                        committedPath,
                        Action_Outpost_BuildRoad.RoadLineOrange);
                    Action_Outpost_BuildRoad.DrawWaypointMarkers(waypoints, layer);

                    int mouseTile = GenWorld.MouseTile();
                    int anchor = LastAnchor();
                    if (mouseTile < 0 || mouseTile == hubTile || !InRange(mouseTile))
                    {
                        previewMouseTile = mouseTile;
                        previewLeg = null;
                    }
                    else if (mouseTile != previewMouseTile && Time.frameCount - previewThrottleFrame >= 5)
                    {
                        previewMouseTile = mouseTile;
                        previewThrottleFrame = Time.frameCount;
                        var probe = new List<int>();
                        previewLeg = WorldActions_Trenches.TryBuildTrenchPath(actor, mouseTile, waypoints, probe)
                            ? probe
                            : null;
                    }

                    Action_Outpost_BuildRoad.DrawRoadPathFromCalculatedNodes(
                        previewLeg,
                        Action_Outpost_BuildRoad.RoadLineOrange);
                    if (mouseTile >= 0 && previewLeg != null && previewLeg.Count >= 2)
                        Action_Outpost_BuildRoad.DrawOrangeStar(mouseTile);
                },
                _ =>
                {
                    if (waypoints.Count > 0)
                        return "TSA_WD_Trench_WaypointLabel".Translate(waypoints.Count);
                    return "TSA_WD_Trench_WaypointTip".Translate();
                },
                target =>
                {
                    if (!target.IsValid || target.Tile < 0) return false;
                    int tile = target.Tile.tileId;
                    if (tile == hubTile || tile == LastAnchor()) return false;
                    if (!InRange(tile)) return false;
                    PlanetTile pTile = new PlanetTile(tile, layer);
                    if (Find.World.Impassable(pTile)) return false;
                    return true;
                });
        }

        /// <summary>Owned tip (deg 1) or mid-arm (deg 2) that can extend / T-branch.</summary>
        private static bool IsOwnedBranchPoint(WorldObject hub, int tile)
        {
            if (hub == null || tile < 0 || tile == hub.Tile.tileId) return false;
            var trenches = WorldComponent_WdTrenches.Get();
            if (trenches == null || !WorldComponent_WdTrenches.HasTrenchAt(tile)) return false;
            if (!trenches.TryGetOwner(tile, out WorldObject owner) || owner != hub) return false;
            int deg = trenches.Degree(tile);
            return deg >= 1 && deg < WorldComponent_WdTrenches.MaxArmDegree;
        }

        public static void DrawTrenchOverlayIfSelected(WorldObject worldObject)
        {
            if (worldObject == null || !Find.WorldSelector.IsSelected(worldObject)) return;

            var comp = worldObject.GetComponent<CompViralSpread>();
            if (comp == null || !WorldActions_Trenches.HasActiveTrenchProject(comp)) return;

            if (worldObject.Faction != Faction.OfPlayer || worldObject is not WorldObject_WD_Outpost) return;

            if (comp.cachedTrenchPathTiles != null && comp.cachedTrenchPathTiles.Count > 0)
            {
                Action_Outpost_BuildRoad.DrawRoadPathFromCalculatedNodes(
                    comp.cachedTrenchPathTiles,
                    Action_Outpost_BuildRoad.RoadLineOrange);
                if (comp.trenchTargetTile >= 0)
                    Action_Outpost_BuildRoad.DrawOrangeStar(comp.trenchTargetTile);
            }

            if (comp.trenchCachedWorkTile >= 0)
                Action_Outpost_BuildRoad.DrawOrangeCircle(comp.trenchCachedWorkTile);
        }
    }
}
