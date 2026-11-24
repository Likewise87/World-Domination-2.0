using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    [StaticConstructorOnStartup]
    public static class Action_Outpost_WatchTower
    {
        private static Texture2D cachedBuildIcon;
        private static Texture2D cachedCancelIcon;

        public static Material LineMat => WorldOverlayLineMaterials.RoadOrange;

        public static Texture2D BuildIcon =>
            cachedBuildIcon ??= ContentFinder<Texture2D>.Get(WorldObject_WD_PlayerWatchTower.ExpandingIconPath, false)
                ?? ContentFinder<Texture2D>.Get("UI/Commands/Build", false)
                ?? TexCommand.Replant;

        public static IEnumerable<Gizmo> GetGizmos(WorldObject outpost)
        {
            if (outpost == null || outpost.Faction != Faction.OfPlayer) yield break;
            if (!(outpost is WorldObject_WD_Outpost) && !ColonyWorldBuildUtility.IsPlayerColonyBuildActor(outpost))
                yield break;

            if (WorldActions_WatchTowers.CountBuiltBy(outpost) >= 1)
                yield return MakeHighlightGizmo(outpost);

            var comp = outpost.GetComponent<CompViralSpread>();
            if (comp == null || !WorldActions_WatchTowers.HasActiveWatchTowerProject(comp)) yield break;

            yield return new Command_Action
            {
                defaultLabel = "TSA_WD_CancelPlayerWatchTower".Translate(),
                defaultDesc = "TSA_WD_CancelPlayerWatchTowerDesc".Translate(),
                icon = cachedCancelIcon ??= ContentFinder<Texture2D>.Get("UI/Designators/Cancel"),
                action = delegate
                {
                    WorldActions_WatchTowers.ClearWatchTowerProject(comp);
                    Messages.Message("TSA_WD_PlayerWatchTowerCancelled".Translate(outpost.LabelCap), MessageTypeDefOf.NeutralEvent);
                }
            };
        }

        private static Command_Toggle MakeHighlightGizmo(WorldObject builder)
        {
            return new Command_Toggle
            {
                defaultLabel = "TSA_WD_Gizmo_HighlightWatchTowers".Translate(),
                defaultDesc = "TSA_WD_Gizmo_HighlightWatchTowersDesc".Translate(),
                icon = BuildIcon,
                defaultIconColor = Color.cyan,
                isActive = () => PlayerWatchTowerSelectionLines.IsHighlightActive(builder),
                toggleAction = () => PlayerWatchTowerSelectionLines.SetHighlight(
                    builder,
                    !PlayerWatchTowerSelectionLines.IsHighlightActive(builder))
            };
        }

        public static FloatMenuOption MakeBuildMenuOption(WorldObject outpost, CompViralSpread comp)
        {
            // FoW-only build. Cancel gizmo still shows if a project was already committed.
            if (!WorldComponent_SettlementIntel.IsGateConfigured())
                return null;

            Texture2D icon = BuildIcon;

            bool roadBusy = comp.roadTargetTile != -1 || WorldActions_BuildBridge.HasActiveBridgeProject(comp)
                || WorldActions_Trenches.HasActiveTrenchProject(comp);
            bool blockBusy = WorldActions_RoadBlocks.HasActiveRoadBlockProject(comp);
            bool trapBusy = WorldActions_SpikeTraps.HasActiveSpikeTrapProject(comp);
            bool decontamBusy = WorldActions_Decontamination.HasActiveDecontaminationProject(comp);
            bool turretBusy = WorldActions_AtTurrets.HasActiveAtTurretProject(comp);
            bool towerBusy = WorldActions_WatchTowers.HasActiveWatchTowerProject(comp);

            if (towerBusy)
            {
                string insufficient = comp.GetInsufficientConstructionMessage();
                string dest = comp.watchTowerTargetName.NullOrEmpty() ? "---" : comp.watchTowerTargetName;
                string label = insufficient
                    ?? "TSA_WD_Inspect_PlayerWatchTowerStatus".Translate(
                        (Mathf.Min(1f, comp.watchTowerProgress) * 100f).ToString("F0"),
                        dest).ToString();
                return new FloatMenuOption(label, () => { }, icon, Color.cyan) { Disabled = true };
            }

            if (roadBusy || blockBusy || trapBusy || decontamBusy || turretBusy
                || WorldActions_PlayerScout.HasActiveScoutProject(comp))
            {
                return new FloatMenuOption("TSA_WD_CancelCurrentProjectFirst".Translate(), () => { }, icon, Color.cyan)
                {
                    Disabled = true
                };
            }

            if (!WorldActions_WatchTowers.CanBuildAnother(outpost))
            {
                return new FloatMenuOption(
                    WorldActions_WatchTowers.PlayerAtCapLabel(outpost),
                    () => { },
                    icon,
                    Color.cyan)
                { Disabled = true };
            }

            float totalConstruction = ColonyWorldBuildUtility.GetActorConstructionSkillRaw(outpost);
            int minConstruction = WorldActions_WatchTowers.GetMinConstruction();
            float days = WorldActions_WatchTowers.GetEstimatedDays(outpost);
            float strength = WorldActions_WatchTowers.GetExpeditionStrengthCost();
            if (ColonyWorldBuildUtility.IsPlayerColonyBuildActor(outpost))
                strength = 0f;
            string daysStr = days > 0f ? days.ToString("F1") : "?";
            // Same as live GetLosRadius at default frac (scales with intelLosFrac once built).
            string losStr = WorldComponent_SettlementIntel.WatchTowerObserverDesiredLos.ToString("F0");
            string tip = strength <= 0f
                ? "TSA_WD_PlayerWatchTowerTooltip_NoStrength".Translate(losStr, daysStr).ToString()
                : "TSA_WD_PlayerWatchTowerTooltip".Translate(losStr, strength.ToString("F0"), daysStr).ToString();

            var opt = new FloatMenuOption(
                "TSA_WD_BuildPlayerWatchTower".Translate(),
                WdCascadingFloatMenu.WrapLeaf(() => StartTargeting(outpost, comp)),
                icon,
                Color.cyan)
            {
                tooltip = tip
            };

            ColonyWorldBuildRequirements.ApplyGate(
                opt,
                totalConstruction,
                minConstruction,
                ColonyWorldBuildRequirements.GetRequiredResearchForWatchTower(),
                ColonyWorldBuildRequirements.GetMaterialCostsForWatchTower());

            return opt;
        }

        private static void StartTargeting(WorldObject source, CompViralSpread comp)
        {
            if (!ColonyWorldBuildRequirements.MeetsWatchTowerRequirements(source))
            {
                Messages.Message("TSA_WD_BuildReq_Unmet".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            if (!WorldActions_WatchTowers.CanBuildAnother(source))
            {
                Messages.Message(WorldActions_WatchTowers.PlayerAtCapLabel(source), MessageTypeDefOf.RejectInput);
                return;
            }

            float range = WorldActions_WatchTowers.GetMaxRange(source);
            PlanetLayer layer = PlanetSurfaceWorldActions.LayerOf(source);
            var sessionTiles = new List<int>();
            int remaining = WorldActions_WatchTowers.RemainingSlots(source);

            Messages.Message("TSA_WD_PlayerWatchTower_TargetPrompt".Translate(), MessageTypeDefOf.NeutralEvent);
            Find.WorldTargeter.BeginTargeting(
                (GlobalTargetInfo target) =>
                {
                    if (!target.IsValid) return false;
                    int tileId = target.Tile;
                    if (tileId < 0) return false;
                    if (!PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(target.Tile))
                        return false;

                    float dist = Find.WorldGrid.ApproxDistanceInTiles(source.Tile.tileId, tileId);
                    if (dist > range + 0.01f)
                    {
                        Messages.Message("TSA_WD_RoadBlockWaypointOutOfRange".Translate(), MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    if (!WorldActions_WatchTowers.IsValidBuildTile(tileId, source.Faction, comp))
                    {
                        Messages.Message("TSA_WD_PlayerWatchTower_InvalidTile".Translate(), MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    bool shift = Event.current != null && Event.current.shift;
                    int already = sessionTiles.Count;
                    if (sessionTiles.Contains(tileId))
                        return false;

                    if (shift)
                    {
                        if (already >= remaining)
                        {
                            Messages.Message(WorldActions_WatchTowers.PlayerAtCapLabel(source), MessageTypeDefOf.RejectInput);
                            return false;
                        }

                        sessionTiles.Add(tileId);
                        Messages.Message("TSA_WD_RoadWaypointAdded".Translate(sessionTiles.Count), MessageTypeDefOf.TaskCompletion);
                        return false;
                    }

                    if (already + 1 > remaining)
                    {
                        Messages.Message(WorldActions_WatchTowers.PlayerAtCapLabel(source), MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    var planned = new List<int>(sessionTiles.Count + 1);
                    planned.AddRange(sessionTiles);
                    planned.Add(tileId);

                    WorldActions_WatchTowers.CommitProject(comp, planned);
                    Messages.Message("TSA_WD_PlayerWatchTower_TargetSet".Translate(planned.Count), MessageTypeDefOf.PositiveEvent);
                    return true;
                },
                true,
                null,
                false,
                () =>
                {
                    WD_RadiusOverlayMode.DrawOrFill(
                        new PlanetTile(source.Tile, layer),
                        range,
                        OutpostCoverageFillKind.Orange,
                        LineMat);

                    for (int i = 0; i < sessionTiles.Count; i++)
                        Action_Outpost_BuildRoad.DrawOrangeX(sessionTiles[i]);

                    int mouseTile = GenWorld.MouseTile();
                    if (mouseTile >= 0
                        && Find.WorldGrid.ApproxDistanceInTiles(source.Tile.tileId, mouseTile) <= range + 0.01f
                        && WorldActions_WatchTowers.IsValidBuildTile(mouseTile, source.Faction, comp)
                        && !sessionTiles.Contains(mouseTile)
                        && sessionTiles.Count < remaining)
                    {
                        Action_Outpost_BuildRoad.DrawOrangeStar(mouseTile);
                    }
                },
                (target) =>
                {
                    if (sessionTiles.Count > 0)
                        return "TSA_WD_RoadWaypointLabel".Translate(sessionTiles.Count);
                    return "TSA_WD_PlayerWatchTower_TargetPrompt".Translate();
                },
                t =>
                {
                    if (!t.IsValid) return false;
                    int tileId = t.Tile;
                    if (tileId < 0) return false;
                    if (!PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(t.Tile)) return false;
                    float dist = Find.WorldGrid.ApproxDistanceInTiles(source.Tile.tileId, tileId);
                    if (dist > range + 0.01f) return false;
                    if (sessionTiles.Contains(tileId)) return false;
                    if (sessionTiles.Count >= remaining) return false;
                    return WorldActions_WatchTowers.IsValidBuildTile(tileId, source.Faction, comp);
                },
                null,
                true);
        }

        /// <summary>
        /// When the building colony/outpost is selected, mark remaining Watch Tower sites (same markers as AT turrets / player scouts).
        /// </summary>
        public static void DrawWatchTowerOverlayIfSelected(WorldObject worldObject)
        {
            if (worldObject == null || !Find.WorldSelector.IsSelected(worldObject)) return;
            if (worldObject.Faction != Faction.OfPlayer) return;
            if (!(worldObject is WorldObject_WD_Outpost) && !ColonyWorldBuildUtility.IsPlayerColonyBuildActor(worldObject))
                return;

            var comp = worldObject.GetComponent<CompViralSpread>();
            if (!WorldActions_WatchTowers.HasActiveWatchTowerProject(comp)) return;

            var planned = comp.watchTowerPlannedTiles;
            if (planned == null || planned.Count == 0) return;

            int workIdx = Mathf.Clamp(comp.watchTowerWorkIndex, 0, planned.Count);
            for (int i = workIdx; i < planned.Count - 1; i++)
                Action_Outpost_BuildRoad.DrawOrangeX(planned[i]);
            if (planned.Count > workIdx)
                Action_Outpost_BuildRoad.DrawOrangeStar(planned[planned.Count - 1]);

            int workTile = comp.watchTowerCachedWorkTile;
            if (workTile < 0)
                workTile = WorldActions_WatchTowers.GetCurrentWorkTile(comp);
            if (workTile >= 0)
                Action_Outpost_BuildRoad.DrawOrangeCircle(workTile);
        }
    }
}
