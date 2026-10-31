using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Build menu entry, tile targeting, cancel gizmo and overlay for powerlines (a timed project, like AT turrets).</summary>
    [StaticConstructorOnStartup]
    public static class Action_Outpost_Powerline
    {
        private static Texture2D cachedIcon;
        private static Texture2D cachedCancelIcon;

        public static Material LineMat => WorldOverlayLineMaterials.RoadOrange;

        public static Texture2D Icon =>
            cachedIcon ??= ContentFinder<Texture2D>.Get("WorldObjects/Fortifications/WD_Powerline", false)
                ?? ContentFinder<Texture2D>.Get("UI/Commands/Build", false)
                ?? TexCommand.Replant;

        public static IEnumerable<Gizmo> GetGizmos(WorldObject outpost)
        {
            if (!(outpost is WorldObject_WD_Outpost) || outpost.Faction != Faction.OfPlayer) yield break;
            if (!OutpostPowerGrid.Enabled) yield break;
            CompViralSpread comp = outpost.GetComponent<CompViralSpread>();
            if (!WorldActions_Powerline.HasActiveProject(comp)) yield break;

            yield return new Command_Action
            {
                defaultLabel = "TSA_WD_Powerline_Cancel".Translate(),
                defaultDesc = "TSA_WD_Powerline_CancelDesc".Translate(),
                icon = cachedCancelIcon ??= ContentFinder<Texture2D>.Get("UI/Designators/Cancel", false) ?? TexCommand.ClearPrioritizedWork,
                action = delegate
                {
                    WorldActions_Powerline.ClearProject(comp);
                    Messages.Message("TSA_WD_Powerline_Cancelled".Translate(outpost.LabelCap), MessageTypeDefOf.NeutralEvent);
                }
            };
        }

        /// <summary>Null when powerlines are unavailable (power rule off, no def, or the source is not a player outpost).</summary>
        public static FloatMenuOption MakeBuildPowerlineMenuOption(WorldObject source, CompViralSpread comp)
        {
            if (!(source is WorldObject_WD_Outpost outpost) || source.Faction != Faction.OfPlayer) return null;
            if (!OutpostPowerGrid.Enabled || WorldActions_Powerline.PowerlineDef == null || comp == null) return null;

            Texture2D icon = Icon;

            if (WorldActions_Powerline.HasActiveProject(comp))
            {
                string insufficient = comp.GetInsufficientConstructionMessage();
                string label = insufficient
                    ?? (comp.powerlineBuilderInField
                        ? "TSA_WD_Inspect_PowerlineStatus_InTransit".Translate(comp.powerlinePlannedTile.ToString(), (Mathf.Min(1f, comp.powerlineProgress) * 100f).ToString("F0")).ToString()
                        : "TSA_WD_Inspect_PowerlineStatus".Translate((Mathf.Min(1f, comp.powerlineProgress) * 100f).ToString("F0"), comp.powerlinePlannedTile.ToString()).ToString());
                return new FloatMenuOption(label, () => { }, icon, Color.cyan) { Disabled = true };
            }

            if (WorldActions_Powerline.OutpostHasPowerline(outpost))
            {
                return new FloatMenuOption("TSA_WD_Powerline_OnePerOutpost".Translate(), () => { }, icon, Color.cyan) { Disabled = true };
            }

            bool otherBusy = comp.roadTargetTile != -1
                || WorldActions_BuildBridge.HasActiveBridgeProject(comp)
                || WorldActions_RoadBlocks.HasActiveRoadBlockProject(comp)
                || WorldActions_SpikeTraps.HasActiveSpikeTrapProject(comp)
                || WorldActions_AtTurrets.HasActiveAtTurretProject(comp)
                || WorldActions_Decontamination.HasActiveDecontaminationProject(comp);
            if (otherBusy)
            {
                return new FloatMenuOption("TSA_WD_CancelCurrentProjectFirst".Translate(), () => { }, icon, Color.cyan) { Disabled = true };
            }

            float days = WorldActions_Powerline.GetEstimatedDays(source);
            var option = new FloatMenuOption(
                "TSA_WD_Powerline_Build".Translate(),
                WdCascadingFloatMenu.WrapLeaf(() => StartTargeting(source, comp)),
                icon,
                Color.cyan)
            {
                tooltip = "TSA_WD_Powerline_BuildDesc".Translate(
                    OutpostPowerGrid.PowerlineRangeBonusTiles.ToString("F0"),
                    WorldActions_Powerline.MinSpacingTiles.ToString("F0"),
                    WorldActions_Powerline.GetExpeditionStrengthCost().ToString("F0"),
                    days > 0f ? days.ToString("F1") : "?")
            };

            ColonyWorldBuildRequirements.ApplyGate(
                option,
                ColonyWorldBuildUtility.GetActorConstructionSkillRaw(source),
                WorldActions_Powerline.GetMinConstruction(),
                WorldActions_Powerline.GetRequiredResearch(),
                WorldActions_Powerline.GetMaterialCosts());
            return option;
        }

        private static void StartTargeting(WorldObject source, CompViralSpread comp)
        {
            if (!WorldActions_Powerline.MeetsRequirements(source))
            {
                Messages.Message("TSA_WD_BuildReq_Unmet".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            float range = WorldActions_AtTurrets.GetMaxRange(source);
            PlanetLayer layer = PlanetSurfaceWorldActions.LayerOf(source);

            bool InBuildRange(int tile) =>
                Find.WorldGrid.ApproxDistanceInTiles(source.Tile.tileId, tile) <= range + 0.01f;

            Messages.Message("TSA_WD_Powerline_TargetPrompt".Translate(), MessageTypeDefOf.NeutralEvent);
            Find.WorldTargeter.BeginTargeting(
                (GlobalTargetInfo target) =>
                {
                    if (!target.IsValid || target.Tile < 0) return false;
                    int tile = target.Tile;
                    if (!InBuildRange(tile))
                    {
                        Messages.Message("TSA_WD_RoadBlockWaypointOutOfRange".Translate(), MessageTypeDefOf.RejectInput);
                        return false;
                    }
                    if (!WorldActions_Powerline.IsValidBuildTile(tile, comp, out string reasonKey))
                    {
                        Messages.Message(reasonKey.Translate(), MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    WorldActions_Powerline.CommitProject(comp, tile);
                    Messages.Message("TSA_WD_Powerline_TargetSet".Translate(), MessageTypeDefOf.PositiveEvent);
                    return true;
                },
                true,
                null,
                false,
                () =>
                {
                    WD_RadiusOverlayMode.DrawOrFill(new PlanetTile(source.Tile, layer), range, OutpostCoverageFillKind.Orange, LineMat);
                    int mouse = GenWorld.MouseTile();
                    if (mouse >= 0 && InBuildRange(mouse) && WorldActions_Powerline.IsValidBuildTile(mouse, comp, out _))
                    {
                        Action_Outpost_BuildRoad.DrawOrangeStar(mouse);
                        WorldMapRadiusVisual.DrawApproxRadiusRing(new PlanetTile(mouse, layer), OutpostPowerGrid.PowerlineRangeBonusTiles, LineMat);
                    }
                },
                (target) => "TSA_WD_Powerline_TargetPrompt".Translate(),
                t => t.IsValid && t.Tile >= 0 && InBuildRange(t.Tile)
                    && WorldActions_Powerline.IsValidBuildTile(t.Tile, comp, out _),
                null,
                true);
        }

        /// <summary>Selected outpost with a powerline project: mark the worksite and the radius the tower will cover.</summary>
        public static void DrawOverlayIfSelected(WorldObject worldObject)
        {
            if (worldObject == null || !Find.WorldSelector.IsSelected(worldObject)) return;
            if (!(worldObject is WorldObject_WD_Outpost) || worldObject.Faction != Faction.OfPlayer) return;

            CompViralSpread comp = worldObject.GetComponent<CompViralSpread>();
            if (!WorldActions_Powerline.HasActiveProject(comp)) return;

            int tile = comp.powerlinePlannedTile;
            Action_Outpost_BuildRoad.DrawOrangeStar(tile);
            PlanetLayer layer = PlanetSurfaceWorldActions.LayerOf(worldObject);
            WorldMapRadiusVisual.DrawApproxRadiusRing(new PlanetTile(tile, layer), OutpostPowerGrid.PowerlineRangeBonusTiles, LineMat);
        }
    }
}
