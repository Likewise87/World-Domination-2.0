using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Farming (crop) outpost production: options, output per skill, delivery items, tooltips. DefName must contain "farming".</summary>
    public static class Outpost_Farming
    {
        /// <summary>Multiplier applied to all farming output (1 = full, 0.5 = halved).</summary>
        public const float FarmingOutputMultiplier = 0.35f;

        /// <summary>Farming options: harvest products from plant defs that can be sown in growing zones (excludes wild-only e.g. ambrosia, trees).</summary>
        public static List<ThingDef> GetProducibleOptions(WorldObject_WD_Outpost outpost)
        {
            var list = new List<ThingDef>();
            if (outpost?.def == null || !Outpost_Production_Utils.IsFarmingOutpost(outpost.def)) return list;
            foreach (ThingDef plant in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (plant?.plant?.harvestedThingDef == null) continue;
                if (!plant.plant.Sowable) continue;
                ThingDef harvest = plant.plant.harvestedThingDef;
                if (harvest == null || list.Contains(harvest)) continue;
                list.Add(harvest);
            }
            return list.Where(t => t != null).ToList();
        }

        /// <summary>Output per skill point for a crop: crop baseline × farming tile factor.</summary>
        public static float GetOutputPerSkillPoint(WorldObject_WD_Outpost outpost, ThingDef product)
        {
            if (outpost?.def == null || product == null) return 0f;
            if (!Outpost_Production_Utils.IsFarmingOutpost(outpost.def)) return 0f;
            float cropBaseline = Outpost_Baselines.GetCropBaselinePerSkill(product);
            float tileFactor = Outpost_Production_Utils.GetFarmingTileProductionFactor(outpost);
            return cropBaseline * tileFactor * FarmingOutputMultiplier;
        }

        /// <summary>Delivery items for farming: single selected crop, qty = colony-assigned skill × output per skill. Uses Logistics colony assignment when active. When overrideCapacity is set (spawn path), uses that value.</summary>
        public static List<ThingDefCountClass> GetDeliveryItems(WorldObject_WD_Outpost outpost, ThingDef producing, float? overrideCapacity = null)
        {
            if (outpost == null || producing == null) return null;
            float capacity = overrideCapacity ?? Outpost_Production_Utils.GetSkillAssignedToPhysicalProduction(outpost);
            float outputPer = GetOutputPerSkillPoint(outpost, producing);
            int qty = Mathf.Max(0, Mathf.RoundToInt(capacity * outputPer));
            if (qty <= 0) return null;
            return new List<ThingDefCountClass> { new ThingDefCountClass(producing, qty) };
        }

        /// <summary>Tooltip for farming: baseline, tile factor, effective, total.</summary>
        public static string GetProductionTooltip(WorldObject_WD_Outpost outpost, ThingDef crop)
        {
            if (outpost == null || crop == null) return "";
            float cropBaseline = Outpost_Baselines.GetCropBaselinePerSkill(crop);
            float harvestDifficulty = Outpost_Baselines.GetCropHarvestDifficultyFactor(crop);
            float fert01 = Mathf.Clamp(
                WorldTileProductivity.GetFarmingFertilityScore(outpost.Tile, outpost.GetBuiltUpgradeTileFertilityBonus()),
                0f,
                WorldTileProductivity.ProductivityScoreCap);
            float tileYieldMult = Outpost_Production_Utils.GetFarmingTileProductionFactor(outpost);
            float effective = GetOutputPerSkillPoint(outpost, crop);
            float capacity = outpost.GetCapacityForYieldPreview();
            int totalItems = Outpost_Production_Utils.ScaleOutputStackCount(Mathf.Max(0, Mathf.RoundToInt(capacity * effective)));
            string key = "TSA_WD_Production_TooltipFarming";
            string t = key.Translate(cropBaseline.ToString("F1"), fert01.ToString("F2"), effective.ToString("F2"), capacity.ToString("F0"), SkillDefOf.Plants.label, totalItems.ToString(), crop.LabelCap).ToString();
            if (t == key || t.Contains("TSA_WD_")) t = "Base output: " + cropBaseline.ToString("F1") + " per skill (from crop; potato harvest-difficulty benchmark = 1.00, this crop " + harvestDifficulty.ToString("F2") + ").\nWorld tile fertility (0–" + WorldTileProductivity.ProductivityScoreCap.ToString("F1") + " scale; can exceed 1 with mutators): " + fert01.ToString("F2") + " (same as outpost selection).\nEffective: " + effective.ToString("F2") + " per skill.\nTotal (incl. global output multiplier): " + capacity.ToString("F0") + " Plants Skill × " + effective.ToString("F2") + " → " + totalItems + " " + crop.LabelCap;
            return t;
        }

        /// <summary>Tooltip for crop baseline: how much item can be bought for silver from settings.</summary>
        public static string GetCropBaselineTooltip(ThingDef harvest)
        {
            if (harvest == null) return "";
            var plant = Outpost_Baselines.GetPlantDefForHarvest(harvest);
            if (plant?.plant == null) return harvest.LabelCap + " (no plant def).";
            return Outpost_Baselines.GetBaselineTooltipForProduct(harvest);
        }

        /// <summary>Summary line for farming.</summary>
        public static string FormatFarmingSummaryLine(WorldObject_WD_Outpost outpost)
        {
            if (outpost?.SelectedProductionDef == null) return null;
            ThingDef crop = outpost.SelectedProductionDef;
            float cycleDays = Outpost_Production_Utils.GetProductionCycleDays(outpost);
            var items = Outpost_Production.GetCurrentDeliveryItems(outpost);
            string cycleStr = cycleDays.ToString("F0");
            if (items == null || items.Count == 0)
                return "TSA_WD_Prod_FarmingSummary_None".Translate(crop.LabelCap, cycleStr).ToString();
            var parts = new List<string>();
            foreach (var tc in items)
                if (tc?.thingDef != null) parts.Add("x" + tc.count + " " + tc.thingDef.LabelCap);
            string yieldStr = string.Join(" and ", parts);
            return "TSA_WD_Prod_FarmingSummary".Translate(crop.LabelCap, yieldStr, cycleStr).ToString();
        }

        /// <summary>Tooltip for farming fertility factor (base + mutator breakdown).</summary>
        public static string GetFarmingEfficiencyTooltip(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return "";
            float b = outpost.GetBuiltUpgradeTileFertilityBonus();
            string lines = WorldTileProductivity.BuildOutpostUpgradeProductivityLines(outpost, d => d.tileFertilityBonus);
            return WorldTileProductivity.GetFarmingFertilityTooltipText(outpost.Tile, b, lines);
        }
    }
}
