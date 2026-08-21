using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    [StaticConstructorOnStartup]
    public static class Action_Outpost_AtTurrets
    {
        private static Texture2D cachedBuildIcon;
        private static Texture2D cachedCancelIcon;

        public static Material LineMat => WorldOverlayLineMaterials.RoadOrange;

        public static Texture2D BuildAtTurretIcon =>
            cachedBuildIcon ??= WorldObject_AT_Turret.IconForTier(AtTurretTier.Medium)
                ?? ContentFinder<Texture2D>.Get("UI/Commands/Build", false)
                ?? TexCommand.Replant;

        public static IEnumerable<Gizmo> GetGizmos(WorldObject outpost)
        {
            if (outpost == null || outpost.Faction != Faction.OfPlayer) yield break;
            if (!(outpost is WorldObject_WD_Outpost) && !ColonyWorldBuildUtility.IsPlayerColonyBuildActor(outpost))
                yield break;

            var comp = outpost.GetComponent<CompViralSpread>();
            if (comp == null || !WorldActions_AtTurrets.HasActiveAtTurretProject(comp)) yield break;

            yield return new Command_Action
            {
                defaultLabel = "TSA_WD_CancelAT_Turret".Translate(),
                defaultDesc = "TSA_WD_CancelAT_TurretDesc".Translate(),
                icon = cachedCancelIcon ??= ContentFinder<Texture2D>.Get("UI/Designators/Cancel"),
                action = delegate
                {
                    WorldActions_AtTurrets.ClearAtTurretProject(comp);
                    Messages.Message("TSA_WD_AT_TurretCancelled".Translate(outpost.LabelCap), MessageTypeDefOf.NeutralEvent);
                }
            };
        }

        public static FloatMenuOption MakeBuildAtTurretMenuOption(WorldObject outpost, CompViralSpread comp)
        {
            Texture2D icon = BuildAtTurretIcon;

            Settlement owner = WorldActions_AtTurrets.ResolveBuiltBySettlement(outpost);
            if (owner == null)
            {
                return new FloatMenuOption(
                    "TSA_WD_AT_Turret_NeedsColony".Translate(),
                    () => { },
                    icon,
                    Color.cyan)
                { Disabled = true };
            }

            if (!AtTurretUtility.CanPlayerSiteBuildAnother(outpost))
            {
                return new FloatMenuOption(
                    AtTurretUtility.PlayerAtCapLabel(outpost),
                    () => { },
                    icon,
                    Color.cyan)
                { Disabled = true };
            }

            bool roadBusy = comp.roadTargetTile != -1;
            bool blockBusy = WorldActions_RoadBlocks.HasActiveRoadBlockProject(comp);
            bool trapBusy = WorldActions_SpikeTraps.HasActiveSpikeTrapProject(comp);
            bool decontamBusy = WorldActions_Decontamination.HasActiveDecontaminationProject(comp);
            bool turretBusy = WorldActions_AtTurrets.HasActiveAtTurretProject(comp);

            if (turretBusy)
            {
                string kindLabel = AtTurretUtility.LabelKey(comp.selectedAtTurretTier).Translate();
                string insufficient = comp.GetInsufficientStrengthConstructionMessage();
                string dest = comp.atTurretTargetName.NullOrEmpty() ? "…" : comp.atTurretTargetName;
                string label = insufficient
                    ?? "TSA_WD_Inspect_AT_TurretStatus".Translate(
                        kindLabel,
                        (Mathf.Min(1f, comp.atTurretProgress) * 100f).ToString("F0"),
                        dest).ToString();
                return new FloatMenuOption(label, () => { }, icon, Color.cyan) { Disabled = true };
            }

            if (roadBusy || blockBusy || trapBusy || decontamBusy)
            {
                return new FloatMenuOption("TSA_WD_CancelCurrentProjectFirst".Translate(), () => { }, icon, Color.cyan)
                {
                    Disabled = true
                };
            }

            var branch = WdCascadingFloatMenu.MakeBranchOption(
                "TSA_WD_BuildAT_Turret".Translate(),
                () => OpenTierMenu(outpost, comp),
                icon,
                Color.cyan);
            branch.tooltip = "TSA_WD_BuildAT_TurretDesc".Translate();
            return branch;
        }

        private static void OpenTierMenu(WorldObject outpost, CompViralSpread comp)
        {
            float totalConstruction = ColonyWorldBuildUtility.GetActorConstructionSkillRaw(outpost);
            var opts = new List<FloatMenuOption>
            {
                MakeTierOption(outpost, comp, AtTurretTier.Light, totalConstruction),
                MakeTierOption(outpost, comp, AtTurretTier.Medium, totalConstruction),
                MakeTierOption(outpost, comp, AtTurretTier.Heavy, totalConstruction)
            };
            WdCascadingFloatMenu.OpenAsChild(opts, () => Action_Outpost_Build.BuildRootMenuOptions(outpost, comp));
        }

        private static FloatMenuOption MakeTierOption(
            WorldObject outpost,
            CompViralSpread comp,
            AtTurretTier tier,
            float totalConstruction)
        {
            string label = AtTurretUtility.LabelKey(tier).Translate();
            Texture2D icon = WorldObject_AT_Turret.IconForTier(tier) ?? BuildAtTurretIcon;
            int minConstruction = WorldActions_AtTurrets.GetMinConstruction(tier);

            float days = WorldActions_AtTurrets.GetEstimatedDaysPerAtTurret(outpost, tier);
            float strength = WorldActions_AtTurrets.GetExpeditionStrengthCost(tier);
            string tip = "TSA_WD_AT_TurretTierTooltip".Translate(
                label,
                strength.ToString("F0"),
                days > 0f ? days.ToString("F1") : "?");

            var opt = new FloatMenuOption(
                label,
                WdCascadingFloatMenu.WrapLeaf(() => StartAtTurretTargeting(outpost, comp, tier)),
                icon,
                Color.cyan)
            {
                tooltip = tip
            };

            ColonyWorldBuildRequirements.ApplyGate(
                opt,
                totalConstruction,
                minConstruction,
                ColonyWorldBuildRequirements.GetRequiredResearchForAtTurret(tier),
                ColonyWorldBuildRequirements.GetMaterialCostsForAtTurret(tier));

            return opt;
        }

        private static void StartAtTurretTargeting(WorldObject source, CompViralSpread comp, AtTurretTier tier)
        {
            if (!ColonyWorldBuildRequirements.MeetsAtTurretRequirements(source, tier))
            {
                Messages.Message(
                    "TSA_WD_BuildReq_Unmet".Translate(),
                    MessageTypeDefOf.RejectInput);
                return;
            }

            float range = WorldActions_AtTurrets.GetMaxRange(source);
            PlanetLayer layer = PlanetSurfaceWorldActions.LayerOf(source);
            var sessionTiles = new List<int>();

            Messages.Message("TSA_WD_AT_Turret_TargetPrompt".Translate(), MessageTypeDefOf.NeutralEvent);
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

                    if (!WorldActions_AtTurrets.IsValidBuildTile(tileId, source.Faction))
                    {
                        Messages.Message("TSA_WD_AT_Turret_InvalidTile".Translate(), MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    if (!ColonyWorldBuildRequirements.MeetsAtTurretRequirements(source, tier))
                    {
                        Messages.Message(
                            "TSA_WD_BuildReq_Unmet".Translate(),
                            MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    bool shift = Event.current != null && Event.current.shift;
                    int remaining = AtTurretUtility.RemainingPlayerSlotsForSite(source);
                    int already = sessionTiles.Count;
                    if (sessionTiles.Contains(tileId))
                        return false;

                    if (shift)
                    {
                        if (already >= remaining)
                        {
                            Messages.Message(AtTurretUtility.PlayerAtCapLabel(source), MessageTypeDefOf.RejectInput);
                            return false;
                        }

                        sessionTiles.Add(tileId);
                        Messages.Message("TSA_WD_RoadWaypointAdded".Translate(sessionTiles.Count), MessageTypeDefOf.TaskCompletion);
                        return false;
                    }

                    // Non-shift: add this tile (if room) and commit the full list.
                    if (already + 1 > remaining)
                    {
                        Messages.Message(AtTurretUtility.PlayerAtCapLabel(source), MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    var planned = new List<int>(sessionTiles.Count + 1);
                    planned.AddRange(sessionTiles);
                    planned.Add(tileId);

                    WorldActions_AtTurrets.CommitAtTurretProject(comp, tier, planned);
                    Messages.Message("TSA_WD_AT_Turret_TargetSet".Translate(planned.Count), MessageTypeDefOf.PositiveEvent);
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
                        && WorldActions_AtTurrets.IsValidBuildTile(mouseTile, source.Faction)
                        && !sessionTiles.Contains(mouseTile))
                    {
                        Action_Outpost_BuildRoad.DrawOrangeStar(mouseTile);
                    }
                },
                (target) =>
                {
                    if (sessionTiles.Count > 0)
                        return "TSA_WD_RoadWaypointLabel".Translate(sessionTiles.Count);
                    return "TSA_WD_AT_Turret_TargetPrompt".Translate();
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
                    return WorldActions_AtTurrets.IsValidBuildTile(tileId, source.Faction);
                },
                null,
                true);
        }

        public static void DrawAtTurretOverlayIfSelected(WorldObject worldObject)
        {
            if (worldObject == null || !Find.WorldSelector.IsSelected(worldObject)) return;
            if (worldObject.Faction != Faction.OfPlayer) return;
            if (!(worldObject is WorldObject_WD_Outpost) && !ColonyWorldBuildUtility.IsPlayerColonyBuildActor(worldObject))
                return;

            var comp = worldObject.GetComponent<CompViralSpread>();
            if (!WorldActions_AtTurrets.HasActiveAtTurretProject(comp)) return;

            var planned = comp.atTurretPlannedTiles;
            if (planned == null || planned.Count == 0) return;

            int workIdx = Mathf.Clamp(comp.atTurretWorkIndex, 0, planned.Count);
            for (int i = workIdx; i < planned.Count - 1; i++)
                Action_Outpost_BuildRoad.DrawOrangeX(planned[i]);
            if (planned.Count > workIdx)
                Action_Outpost_BuildRoad.DrawOrangeStar(planned[planned.Count - 1]);

            int workTile = comp.atTurretCachedWorkTile;
            if (workTile < 0)
                workTile = WorldActions_AtTurrets.GetCurrentWorkTile(comp);
            if (workTile >= 0)
                Action_Outpost_BuildRoad.DrawOrangeCircle(workTile);
        }
    }
}
