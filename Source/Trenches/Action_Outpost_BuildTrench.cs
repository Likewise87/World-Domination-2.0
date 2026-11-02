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
            cachedBuildIcon ??= ContentFinder<Texture2D>.Get("UI/Commands/BuildTrench", false)
                ?? ContentFinder<Texture2D>.Get("UI/Commands/BuildRoad", false)
                ?? TexCommand.Replant;

        public static Texture2D RemoveIcon =>
            cachedRemoveIcon ??= ContentFinder<Texture2D>.Get("UI/Commands/RemoveTrench", false)
                ?? BuildIcon;

        public static IEnumerable<Gizmo> GetGizmos(WorldObject actor)
        {
            if (actor == null || actor.Faction != Faction.OfPlayer) yield break;
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
            if (!WorldComponent_WdTrenches.FeatureEnabled)
            {
                return new FloatMenuOption("TSA_WD_BuildTrench".Translate(), null, icon, Color.white)
                {
                    Disabled = true,
                    tooltip = "TSA_WD_Trench_Disabled".Translate()
                };
            }
            if (comp.trenchTargetTile != -1)
            {
                return new FloatMenuOption("TSA_WD_CancelCurrentProjectFirst".Translate(), () => { }, icon, Color.white)
                {
                    Disabled = true
                };
            }
            if (HasBlockingProject(comp))
            {
                return new FloatMenuOption("TSA_WD_CancelCurrentProjectFirst".Translate(), () => { }, icon, Color.white)
                {
                    Disabled = true
                };
            }

            return new FloatMenuOption("TSA_WD_BuildTrench".Translate(), () => StartTargeting(actor, comp, clearing: false), icon, Color.white)
            {
                tooltip = "TSA_WD_BuildTrenchDesc".Translate(
                    WorldComponent_WdTrenches.MaxBuildRadiusTiles.ToString(),
                    WorldComponent_WdTrenches.WoodPerSegment.ToString())
            };
        }

        public static FloatMenuOption MakeRemoveMenuOption(WorldObject actor, CompViralSpread comp)
        {
            Texture2D icon = RemoveIcon;
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
            var waypoints = new List<int>();
            float range = WorldComponent_WdTrenches.MaxBuildRadiusTiles;
            Find.WorldTargeter.BeginTargeting(
                target =>
                {
                    if (target.Tile < 0) return false;
                    int tile = target.Tile.tileId;
                    if (tile == actor.Tile.tileId) return false;

                    bool shift = Event.current != null && Event.current.shift;
                    if (Find.WorldGrid.ApproxDistanceInTiles(actor.Tile, target.Tile) > range)
                    {
                        Messages.Message("TSA_WD_Trench_OutOfRange".Translate(range.ToString("F0")), MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    if (shift)
                    {
                        var check = WorldComponent_WdTrenches.CanUseTileForTrench(tile, actor);
                        if (!check.Accepted && !WorldComponent_WdTrenches.HasTrenchAt(tile))
                        {
                            Messages.Message(check.Reason, MessageTypeDefOf.RejectInput, false);
                            return false;
                        }
                        waypoints.Add(tile);
                        return false;
                    }

                    var path = new List<int>();
                    if (!WorldActions_Trenches.TryBuildTrenchPath(actor, tile, waypoints, path) || path.Count < 2)
                    {
                        Messages.Message("TSA_WD_Trench_InvalidPath".Translate(), MessageTypeDefOf.RejectInput, false);
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
                    Messages.Message("TSA_WD_Trench_BuildStarted".Translate(actor.LabelCap), actor, MessageTypeDefOf.NeutralEvent);
                    return true;
                },
                true,
                BuildIcon,
                false,
                null,
                null);
        }

        public static void DrawTrenchOverlayIfSelected(WorldObject worldObject)
        {
            if (worldObject == null || !Find.WorldSelector.IsSelected(worldObject)) return;

            var comp = worldObject.GetComponent<CompViralSpread>();
            if (comp == null || !WorldActions_Trenches.HasActiveTrenchProject(comp)) return;

            bool isPlayerOutpost = worldObject.Faction == Faction.OfPlayer && worldObject is WorldObject_WD_Outpost;
            bool isColonyBuild = ColonyWorldBuildUtility.IsPlayerColonyBuildActor(worldObject);
            if (!isPlayerOutpost && !isColonyBuild) return;

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
