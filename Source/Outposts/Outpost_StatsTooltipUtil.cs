using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    internal static class OutpostStatsTooltipUtil
    {
        public struct BonusLine
        {
            public string Source;
            public float Fraction;
        }

        public static string BuildMultiplierTooltip(
            string metricDescription,
            float baseline,
            string baselineFormat,
            List<BonusLine> bonuses,
            float result,
            string resultFormat)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(metricDescription))
                sb.AppendLine(metricDescription);
            sb.AppendLine();
            sb.AppendLine(Tr("TSA_WD_StatsTooltip_Baseline", "Baseline: {0}", baseline.ToString(baselineFormat)));

            int totalPctDisplay = 0;
            if (bonuses != null)
            {
                for (int i = 0; i < bonuses.Count; i++)
                {
                    BonusLine b = bonuses[i];
                    if (Mathf.Abs(b.Fraction) < 1e-6f) continue;
                    int pct = Mathf.RoundToInt(b.Fraction * 100f);
                    totalPctDisplay += pct;
                    sb.AppendLine(Tr("TSA_WD_StatsTooltip_BonusFrom", "Bonus from {0}: +{1}%", b.Source, pct.ToString()));
                }
            }

            sb.Append(Tr(
                "TSA_WD_StatsTooltip_ResultMult",
                "Result: {0} × {1} = {2}",
                baseline.ToString(baselineFormat),
                FormatDisplayMultiplier(totalPctDisplay),
                result.ToString(resultFormat)));
            return sb.ToString();
        }

        /// <summary>Multiplier string aligned with integer +N% bonus lines (avoids 1.33 vs +32% drift from float rounding).</summary>
        private static string FormatDisplayMultiplier(int totalPercentPoints)
        {
            if (totalPercentPoints == 0) return "1";
            return (1f + totalPercentPoints / 100f).ToString("0.##");
        }

        public static string BuildFlatAdditionTooltip(
            string metricDescription,
            float baseline,
            string baselineFormat,
            List<BonusLine> flatBonuses,
            float result,
            string resultFormat)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(metricDescription))
                sb.AppendLine(metricDescription);
            sb.AppendLine();
            sb.AppendLine(Tr("TSA_WD_StatsTooltip_Baseline", "Baseline: {0}", baseline.ToString(baselineFormat)));

            float add = 0f;
            if (flatBonuses != null)
            {
                for (int i = 0; i < flatBonuses.Count; i++)
                {
                    BonusLine b = flatBonuses[i];
                    if (Mathf.Abs(b.Fraction) < 1e-6f) continue;
                    add += b.Fraction;
                    sb.AppendLine(Tr("TSA_WD_StatsTooltip_BonusFlatFrom", "Bonus from {0}: +{1}", b.Source, b.Fraction.ToString("F0")));
                }
            }

            sb.Append(Tr(
                "TSA_WD_StatsTooltip_ResultFlat",
                "Result: {0} + {1} = {2}",
                baseline.ToString(baselineFormat),
                add.ToString("F0"),
                result.ToString(resultFormat)));
            return sb.ToString();
        }

        /// <summary>
        /// Range with optional shrink override: max capability (settings + flat + % bonuses), bonus lines, then configured/effective.
        /// <paramref name="flatBonuses"/> use <see cref="BonusLine.Fraction"/> as tile adds.
        /// <paramref name="percentBonuses"/> use Fraction as 0.25 = +25%.
        /// </summary>
        public static string BuildConfigurableRangeTooltip(
            string metricDescription,
            List<BonusLine> flatBonuses,
            float absoluteMax,
            float configuredEffective,
            List<BonusLine> percentBonuses = null)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(metricDescription))
                sb.AppendLine(metricDescription);
            sb.AppendLine();
            sb.AppendLine(Tr(
                "TSA_WD_StatsTooltip_MaxRange",
                "Max range: {0}",
                absoluteMax.ToString("F0")));

            if (flatBonuses != null)
            {
                for (int i = 0; i < flatBonuses.Count; i++)
                {
                    BonusLine b = flatBonuses[i];
                    if (Mathf.Abs(b.Fraction) < 1e-6f) continue;
                    sb.AppendLine(Tr(
                        "TSA_WD_StatsTooltip_BonusFlatFrom",
                        "Bonus from {0}: +{1}",
                        b.Source,
                        b.Fraction.ToString("F0")));
                }
            }

            if (percentBonuses != null)
            {
                for (int i = 0; i < percentBonuses.Count; i++)
                {
                    BonusLine b = percentBonuses[i];
                    if (Mathf.Abs(b.Fraction) < 1e-6f) continue;
                    int pct = Mathf.RoundToInt(b.Fraction * 100f);
                    sb.AppendLine(Tr(
                        "TSA_WD_StatsTooltip_BonusFrom",
                        "Bonus from {0}: +{1}%",
                        b.Source,
                        pct.ToString()));
                }
            }

            sb.Append(Tr(
                "TSA_WD_StatsTooltip_ConfiguredRange",
                "Configured range: {0}",
                configuredEffective.ToString("F0")));
            return sb.ToString();
        }

        /// <summary>
        /// Duration after fractional reductions (skill + upgrades), with a multiplier floor.
        /// <paramref name="reductionLines"/> use <see cref="BonusLine.Fraction"/> as duration cut (0.1 = −10%).
        /// </summary>
        public static string BuildDurationReductionTooltip(
            string metricDescription,
            float baseline,
            string baselineFormat,
            List<BonusLine> reductionLines,
            float durationMultiplier,
            float multiplierFloor,
            float result,
            string resultFormat,
            string absoluteFloorNote = null)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(metricDescription))
                sb.AppendLine(metricDescription);
            sb.AppendLine();
            sb.AppendLine(Tr("TSA_WD_StatsTooltip_Baseline", "Baseline: {0}", baseline.ToString(baselineFormat)));

            float totalReduction = 0f;
            if (reductionLines != null)
            {
                for (int i = 0; i < reductionLines.Count; i++)
                {
                    BonusLine b = reductionLines[i];
                    if (Mathf.Abs(b.Fraction) < 1e-6f) continue;
                    totalReduction += b.Fraction;
                    int pct = Mathf.RoundToInt(b.Fraction * 100f);
                    sb.AppendLine(Tr("TSA_WD_StatsTooltip_ReductionFrom", "Reduction from {0}: −{1}%", b.Source, pct.ToString()));
                }
            }

            float rawMult = Mathf.Max(0f, 1f - totalReduction);
            bool floorClamped = rawMult + 1e-6f < multiplierFloor;
            sb.AppendLine(Tr(
                "TSA_WD_StatsTooltip_DurationMult",
                "Duration multiplier: {0}% of base",
                (durationMultiplier * 100f).ToString("F0")));
            if (floorClamped)
            {
                sb.AppendLine(Tr(
                    "TSA_WD_StatsTooltip_DurationFloor",
                    "Floor: skill/upgrade cuts cannot go below {0}% of base (min {1}).",
                    (multiplierFloor * 100f).ToString("F0"),
                    (baseline * multiplierFloor).ToString(baselineFormat)));
            }

            sb.Append(Tr(
                "TSA_WD_StatsTooltip_ResultMult",
                "Result: {0} × {1} = {2}",
                baseline.ToString(baselineFormat),
                durationMultiplier.ToString("0.##"),
                result.ToString(resultFormat)));
            if (!string.IsNullOrEmpty(absoluteFloorNote))
            {
                sb.AppendLine();
                sb.Append(absoluteFloorNote);
            }
            return sb.ToString();
        }

        /// <summary>Hit chance tooltip: band base + flat percentage-point bonuses (skill / upgrades).</summary>
        public static string BuildHitChanceTooltip(
            string metricDescription,
            float bandBase,
            List<BonusLine> flatChanceBonuses,
            float result)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(metricDescription))
                sb.AppendLine(metricDescription);
            sb.AppendLine();
            sb.AppendLine(Tr(
                "TSA_WD_StatsTooltip_BandBase",
                "Band base: {0}%",
                (Mathf.Clamp01(bandBase) * 100f).ToString("F0")));

            float add = 0f;
            if (flatChanceBonuses != null)
            {
                for (int i = 0; i < flatChanceBonuses.Count; i++)
                {
                    BonusLine b = flatChanceBonuses[i];
                    if (Mathf.Abs(b.Fraction) < 1e-6f) continue;
                    add += b.Fraction;
                    sb.AppendLine(Tr(
                        "TSA_WD_StatsTooltip_BonusPpFrom",
                        "Bonus from {0}: +{1}pp",
                        b.Source,
                        (b.Fraction * 100f).ToString("F0")));
                }
            }

            sb.Append(Tr(
                "TSA_WD_StatsTooltip_ResultFlat",
                "Result: {0} + {1} = {2}",
                (Mathf.Clamp01(bandBase) * 100f).ToString("F0") + "%",
                (add * 100f).ToString("F0") + "pp",
                (Mathf.Clamp01(result) * 100f).ToString("F0") + "%"));
            return sb.ToString();
        }

        public static void AddExpertBonusLines(WorldObject_WD_Outpost outpost, ExpertEffect filter, List<BonusLine> lines)
        {
            if (outpost == null || lines == null || filter == ExpertEffect.None) return;
            foreach (OutpostExpertRole role in System.Enum.GetValues(typeof(OutpostExpertRole)))
            {
                if ((OutpostExpertUtility.GetApplicableRoleEffects(outpost, role) & filter) == ExpertEffect.None) continue;

                float bonus = OutpostExpertUtility.GetExpertBonusFractionForEffect(outpost, role, filter);
                if (Mathf.Abs(bonus) < 1e-6f) continue;

                Pawn pawn = outpost.GetAssignedExpert(role);
                string pawnName = pawn?.LabelShortCap ?? "—";
                string source = Tr(
                    "TSA_WD_StatsTooltip_ExpertSource",
                    "Expert \"{0}\" ({1})",
                    OutpostExpertUtility.GetRoleLabel(role),
                    pawnName);
                lines.Add(new BonusLine { Source = source, Fraction = bonus });
            }
        }

        public static void AddUpgradePercentLines(
            WorldObject_WD_Outpost outpost,
            System.Func<OutpostUpgradeDef, float> bonusPerLevel,
            List<BonusLine> lines)
        {
            if (outpost?.BuiltUpgradeLevels == null || lines == null) return;
            foreach (var kv in outpost.BuiltUpgradeLevels.OrderBy(x => x.Key))
            {
                if (kv.Value <= 0) continue;
                var def = DefDatabase<OutpostUpgradeDef>.GetNamedSilentFail(kv.Key);
                if (def == null) continue;
                float b = bonusPerLevel(def) * kv.Value;
                if (Mathf.Abs(b) < 1e-6f) continue;
                lines.Add(new BonusLine
                {
                    Source = Tr("TSA_WD_StatsTooltip_UpgradeSource", "Upgrade \"{0}\"", def.LabelCap),
                    Fraction = b
                });
            }
        }

        public static void AddUpgradeFlatLines(
            WorldObject_WD_Outpost outpost,
            System.Func<OutpostUpgradeDef, float> bonusPerLevel,
            List<BonusLine> lines)
        {
            if (outpost?.BuiltUpgradeLevels == null || lines == null) return;
            foreach (var kv in outpost.BuiltUpgradeLevels.OrderBy(x => x.Key))
            {
                if (kv.Value <= 0) continue;
                var def = DefDatabase<OutpostUpgradeDef>.GetNamedSilentFail(kv.Key);
                if (def == null) continue;
                float b = bonusPerLevel(def) * kv.Value;
                if (Mathf.Abs(b) < 1e-6f) continue;
                lines.Add(new BonusLine
                {
                    Source = Tr("TSA_WD_StatsTooltip_UpgradeSource", "Upgrade \"{0}\"", def.LabelCap),
                    Fraction = b
                });
            }
        }

        private static string Tr(string key, string fallback, params string[] args)
        {
            string s = args != null && args.Length > 0 ? key.Translate(args).ToString() : key.Translate().ToString();
            if (s == key || s.Contains("TSA_WD_")) s = fallback;
            if (args != null && args.Length > 0)
            {
                try { s = string.Format(s, args); }
                catch { s = string.Format(fallback, args); }
            }
            return s;
        }
    }
}
