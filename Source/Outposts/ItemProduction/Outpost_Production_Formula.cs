using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Builds compact production formula strings for tooltips (left-column output, row hover).</summary>
    internal static class Outpost_Production_Formula
    {
        private static string Tag(string key, string fallback)
        {
            string s = key.Translate().ToString();
            return s == key || s.Contains("TSA_WD_") ? fallback : s;
        }

        public static string BuildDeliveryFormulaTooltip(
            WorldObject_WD_Outpost outpost,
            float deliveryCapacity,
            bool useProducingForCycle = true)
        {
            if (outpost?.def == null) return "";

            if (Outpost_Production_Utils.IsRecruitingOutpost(outpost.def)
                || Outpost_Production_Utils.IsTradingOutpost(outpost.def)
                || Outpost_Production_Utils.IsScavengingOutpost(outpost.def)
                || Outpost_Production_Utils.IsAcademyOutpost(outpost.def))
                return "";

            string outputSuffix = Outpost_Production_Utils.BuildProductionOutputFactorSuffix(outpost);
            string skillName = GetSkillName(outpost);

            if (Outpost_Production_Utils.IsHuntingOutpost(outpost.def))
            {
                var kind = useProducingForCycle
                    ? outpost.GetProducingPawnKindForCurrentCycle() ?? outpost.SelectedPawnKindForHunting
                    : outpost.SelectedPawnKindForHunting;
                if (kind == null) return "";

                float huntTileF = Outpost_Production_Utils.GetHuntingTileProductionFactor(outpost);
                int huntEffPct = UnityEngine.Mathf.RoundToInt(huntTileF * 100f);
                string baseTag = Tag("TSA_WD_Production_Formula_Baseline", "(Baseline)");
                string abundTag = Tag("TSA_WD_Production_Formula_AnimalAbundance", "(Animal Abundance)");
                string animalSkillTag = Tag("TSA_WD_Production_Formula_OutpostSkill", "Outpost {0} Skill")
                    .Formatted("Animals");
                if (animalSkillTag.Contains("TSA_WD_")) animalSkillTag = "Outpost Animals Skill";

                var previewDel = Outpost_Hunting.BuildHuntingDeliveryItems(kind, deliveryCapacity, outpost);
                var products = previewDel != null && previewDel.Count > 0
                    ? CollectProductDefs(previewDel)
                    : kind.race != null ? new List<ThingDef> { kind.race } : new List<ThingDef>();

                var sb = new StringBuilder();
                for (int i = 0; i < products.Count; i++)
                {
                    ThingDef prod = products[i];
                    if (prod == null) continue;
                    float bups = Outpost_Hunting.GetHuntingBaselineUnitsPerSkillForProduct(kind, prod, outpost);
                    int rawCount = UnityEngine.Mathf.Max(0, UnityEngine.Mathf.RoundToInt(bups * huntTileF * deliveryCapacity));
                    int fCount = Outpost_Production_Utils.ScaleOutputStackCount(rawCount, outpost);
                    if (sb.Length > 0) sb.AppendLine();
                    sb.Append(fCount).Append(" ").Append(prod.LabelCap).Append(" = ")
                        .Append(bups.ToString("F1")).Append(" ").Append(prod.LabelCap).Append(" ").Append(baseTag)
                        .Append(" × ").Append(huntEffPct).Append("% ").Append(abundTag)
                        .Append(" × ").Append(deliveryCapacity.ToString("F0")).Append(" ").Append(animalSkillTag)
                        .Append(outputSuffix);
                }
                return sb.ToString();
            }

            if (Outpost_Production_Utils.IsFishingOutpost(outpost.def))
            {
                var fish = useProducingForCycle
                    ? outpost.GetProducingFishForCurrentCycle() ?? outpost.SelectedFishDef
                    : outpost.SelectedFishDef;
                if (fish == null) return "";
                float fishTileF = Outpost_Production_Utils.GetFishingTileProductionFactor(outpost);
                int fishEffPct = UnityEngine.Mathf.RoundToInt(fishTileF * 100f);
                string baseTag = Tag("TSA_WD_Production_Formula_Baseline", "(Baseline)");
                string abundTag = Tag("TSA_WD_Production_Formula_FishAbundance", "(Fish Abundance)");
                string animalSkillTag = Tag("TSA_WD_Production_Formula_OutpostSkill", "Outpost {0} Skill")
                    .Formatted("Animals");
                if (animalSkillTag.Contains("TSA_WD_")) animalSkillTag = "Outpost Animals Skill";
                float bups = Outpost_Fishing.GetFishBaselineUnitsPerSkill(fish, outpost);
                int rawCount = UnityEngine.Mathf.Max(0, UnityEngine.Mathf.RoundToInt(bups * fishTileF * deliveryCapacity));
                int fCount = Outpost_Production_Utils.ScaleOutputStackCount(rawCount, outpost);
                return fCount + " " + fish.LabelCap + " = "
                    + bups.ToString("F1") + " " + fish.LabelCap + " " + baseTag
                    + " × " + fishEffPct + "% " + abundTag
                    + " × " + deliveryCapacity.ToString("F0") + " " + animalSkillTag
                    + outputSuffix;
            }

            ThingDef producing = useProducingForCycle
                ? outpost.GetProducingDefForCurrentCycle() ?? outpost.SelectedProductionDef
                : outpost.SelectedProductionDef;
            if (producing == null) return "";

            if (Outpost_Production_Utils.IsProductionOrTradingOutpost(outpost.def))
            {
                var opt = Outpost_Production_Utils.GetProductionOption(outpost, producing);
                if (opt == null) return "";
                SkillDef scaleSkillDef = Outpost_Production_Utils.GetScalingSkillDefForProduction(outpost, opt);
                string rowSkillLabel = Outpost_Production_Utils.SkillLabelCap(scaleSkillDef);
                if (string.IsNullOrEmpty(rowSkillLabel)) rowSkillLabel = skillName;
                float ranchTileFactor = Outpost_Production_Utils.IsRanchOutpost(outpost.def)
                    ? Outpost_Production_Utils.GetRanchTileProductionFactor(outpost)
                    : 1f;
                int totalScaled = Outpost_Production_Utils.ScaleOutputStackCount(
                    UnityEngine.Mathf.RoundToInt(deliveryCapacity * opt.amountPerSkillLevel * ranchTileFactor), outpost);
                string baseTag = Tag("TSA_WD_Production_Formula_Baseline", "(Baseline)");
                string skillTag = Tag("TSA_WD_Production_Formula_OutpostSkill", "Outpost {0} Skill").Formatted(rowSkillLabel);
                if (skillTag.Contains("TSA_WD_")) skillTag = "Outpost " + rowSkillLabel + " Skill";

                var sb = new StringBuilder();
                sb.Append(totalScaled).Append(" ").Append(producing.LabelCap).Append(" = ")
                    .Append(opt.amountPerSkillLevel.ToString("F1")).Append(" ").Append(producing.LabelCap).Append(" ").Append(baseTag);
                if (Outpost_Production_Utils.IsRanchOutpost(outpost.def))
                {
                    int effPct = Outpost_Production_Utils.GetFarmingFertilityPercentInt(outpost);
                    string fertTag = Tag("TSA_WD_Production_Formula_Fertility", "(Fertility)");
                    sb.Append(" × ").Append(effPct).Append("% ").Append(fertTag);
                }
                sb.Append(" × ").Append(deliveryCapacity.ToString("F0")).Append(" ").Append(skillTag).Append(outputSuffix);
                return sb.ToString();
            }

            if (Outpost_Production_Utils.IsMiningOutpost(outpost.def))
            {
                float outputPerSkill = Outpost_Mining.GetOutputPerSkillPoint(outpost, producing);
                int totalScaled = Outpost_Production_Utils.ScaleOutputStackCount(
                    UnityEngine.Mathf.Max(0, UnityEngine.Mathf.RoundToInt(deliveryCapacity * outputPerSkill)), outpost);
                float miningBaseline = Outpost_Baselines.GetMiningBaselinePerSkill(producing);
                int effPct = UnityEngine.Mathf.RoundToInt(Outpost_Production_Utils.GetMiningTileProductionFactor(outpost) * 100f);
                string baseTag = Tag("TSA_WD_Production_Formula_Baseline", "(Baseline)");
                string effTag = Tag("TSA_WD_Production_Formula_MiningEfficiency", "(Mining Efficiency)");
                string skillTag = Tag("TSA_WD_Production_Formula_OutpostSkill", "Outpost {0} Skill").Formatted(skillName);
                if (skillTag.Contains("TSA_WD_")) skillTag = "Outpost " + skillName + " Skill";
                return totalScaled + " " + producing.LabelCap + " = "
                    + miningBaseline.ToString("F1") + " " + producing.LabelCap + " " + baseTag
                    + " × " + effPct + "% " + effTag
                    + " × " + deliveryCapacity.ToString("F0") + " " + skillTag
                    + outputSuffix;
            }

            if (Outpost_Production_Utils.IsFarmingOutpost(outpost.def) || Outpost_Production_Utils.IsRanchOutpost(outpost.def))
            {
                float outputPerSkill = Outpost_Farming.GetOutputPerSkillPoint(outpost, producing);
                int totalScaled = Outpost_Production_Utils.ScaleOutputStackCount(
                    UnityEngine.Mathf.Max(0, UnityEngine.Mathf.RoundToInt(deliveryCapacity * outputPerSkill)), outpost);
                float cropBaseline = Outpost_Baselines.GetCropBaselinePerSkill(producing);
                int effPct = Outpost_Production_Utils.GetFarmingFertilityPercentInt(outpost);
                string baseTag = Tag("TSA_WD_Production_Formula_Baseline", "(Baseline)");
                string fertTag = Tag("TSA_WD_Production_Formula_Fertility", "(Fertility)");
                string skillTag = Tag("TSA_WD_Production_Formula_OutpostSkill", "Outpost {0} Skill").Formatted(skillName);
                if (skillTag.Contains("TSA_WD_")) skillTag = "Outpost " + skillName + " Skill";
                return totalScaled + " " + producing.LabelCap + " = "
                    + cropBaseline.ToString("F1") + " " + producing.LabelCap + " " + baseTag
                    + " × " + effPct + "% " + fertTag
                    + " × " + deliveryCapacity.ToString("F0") + " " + skillTag
                    + outputSuffix;
            }

            return "";
        }

        private static List<ThingDef> CollectProductDefs(List<ThingDefCountClass> items)
        {
            var list = new List<ThingDef>();
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i]?.thingDef != null && !list.Contains(items[i].thingDef))
                    list.Add(items[i].thingDef);
            }
            return list;
        }

        private static string GetSkillName(WorldObject_WD_Outpost outpost)
        {
            string skillFallback = "Skill";
            string k = "TSA_WD_Production_SkillFallback";
            string t = k.Translate().ToString();
            if (t != k && !t.Contains("TSA_WD_")) skillFallback = t;
            var skills = WorldObject_WD_Outpost.GetRelevantSkillDefs(outpost.def);
            if (skills == null || skills.Count == 0) return skillFallback;
            if (skills.Count == 1) return Outpost_Production_Utils.SkillLabelCap(skills[0]);
            var sb = new StringBuilder();
            for (int i = 0; i < skills.Count; i++)
            {
                if (i > 0) sb.Append('/');
                sb.Append(Outpost_Production_Utils.SkillLabelCap(skills[i]));
            }
            return sb.ToString();
        }
    }
}
