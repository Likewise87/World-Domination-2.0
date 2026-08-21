using System.Text;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Piecewise diminishing returns on cumulative outpost skill (production/capacity), then hard cap.
    /// Per-pawn levels and founding gates stay raw; call <see cref="ToEffective"/> only on cumulative totals.
    /// </summary>
    public static class OutpostSkillScaling
    {
        public const int BandCount = 5;

        public static readonly float[] DefBandEnds = { 60f, 100f, 160f, 220f, 280f };
        public static readonly float[] DefBandWeights = { 1f, 0.8f, 0.6f, 0.4f, 0.2f };
        public const float DefHardCapRaw = 280f;
        public const bool DefEnableDiminishingReturns = true;

        public static WorldDominationSettings Settings => WorldDominationMod.settings;

        public static bool IsEnabled => Settings?.enableOutpostSkillDiminishingReturns ?? DefEnableDiminishingReturns;

        public static float HardCapRaw =>
            Mathf.Max(1f, Settings?.outpostSkillHardCapRaw ?? DefHardCapRaw);

        /// <summary>Raw cumulative skill → effective skill for production/capacity.</summary>
        public static float ToEffective(float raw)
        {
            if (raw <= 0f) return 0f;
            if (!IsEnabled) return raw;

            var s = Settings;
            float[] ends = s?.outpostSkillBandEnds;
            float[] weights = s?.outpostSkillBandWeights;
            if (ends == null || weights == null || ends.Length < BandCount || weights.Length < BandCount)
            {
                ends = DefBandEnds;
                weights = DefBandWeights;
            }

            float hardCap = HardCapRaw;
            float cappedRaw = Mathf.Min(raw, hardCap);
            float effective = 0f;
            float prevEnd = 0f;

            for (int i = 0; i < BandCount; i++)
            {
                float end = Mathf.Max(prevEnd + 1f, ends[i]);
                float weight = Mathf.Clamp01(weights[i]);
                if (cappedRaw <= prevEnd) break;

                float segment = Mathf.Min(cappedRaw, end) - prevEnd;
                if (segment > 0f)
                    effective += segment * weight;

                prevEnd = end;
                if (cappedRaw <= end) break;
            }

            // Past last band end but under hard cap: use last weight until hard cap.
            if (cappedRaw > prevEnd)
            {
                float lastWeight = Mathf.Clamp01(weights[BandCount - 1]);
                effective += (cappedRaw - prevEnd) * lastWeight;
            }

            return effective;
        }

        public static bool IsDiminished(float raw) =>
            IsEnabled && raw > 0f && !Mathf.Approximately(ToEffective(raw), raw);

        public static bool IsAtOrAboveHardCap(float raw) =>
            IsEnabled && raw >= HardCapRaw - 0.0001f;

        public static float FirstFullBandEnd()
        {
            var ends = Settings?.outpostSkillBandEnds;
            if (ends != null && ends.Length > 0) return ends[0];
            return DefBandEnds[0];
        }

        public static string FormatRawEffective(float raw)
        {
            float eff = ToEffective(raw);
            if (!IsEnabled || Mathf.Approximately(raw, eff))
                return raw.ToString("F0");
            return "TSA_WD_SkillScaling_RawToEffective".Translate(raw.ToString("F0"), eff.ToString("F0")).ToString();
        }

        public static string BuildBandBreakdownTip(float raw)
        {
            if (!IsEnabled) return "TSA_WD_SkillScaling_DisabledTip".Translate();

            float eff = ToEffective(raw);
            var sb = new StringBuilder();
            sb.AppendLine("TSA_WD_SkillScaling_BreakdownHeader".Translate(raw.ToString("F0"), eff.ToString("F0")));
            var s = Settings;
            float[] ends = s?.outpostSkillBandEnds ?? DefBandEnds;
            float[] weights = s?.outpostSkillBandWeights ?? DefBandWeights;
            float prevEnd = 0f;
            for (int i = 0; i < BandCount && i < ends.Length && i < weights.Length; i++)
            {
                float end = ends[i];
                // Integer display: 0–60, then 61–100 (no shared boundary).
                float displayStart = i == 0 ? 0f : prevEnd + 1f;
                if (displayStart <= end)
                {
                    sb.AppendLine("TSA_WD_SkillScaling_BandLine".Translate(
                        displayStart.ToString("F0"),
                        end.ToString("F0"),
                        (weights[i] * 100f).ToString("F0")));
                }
                prevEnd = end;
            }
            sb.AppendLine("TSA_WD_SkillScaling_HardCapLine".Translate(HardCapRaw.ToString("F0")));
            return sb.ToString().TrimEnd();
        }

        public static void EnsureArrays(WorldDominationSettings s)
        {
            if (s == null) return;
            if (s.outpostSkillBandEnds == null || s.outpostSkillBandEnds.Length != BandCount)
                s.outpostSkillBandEnds = (float[])DefBandEnds.Clone();
            if (s.outpostSkillBandWeights == null || s.outpostSkillBandWeights.Length != BandCount)
                s.outpostSkillBandWeights = (float[])DefBandWeights.Clone();
        }

        public static void NormalizeBands(WorldDominationSettings s)
        {
            if (s == null) return;
            EnsureArrays(s);
            float prevEnd = 0f;
            float prevWeight = 1f;
            for (int i = 0; i < BandCount; i++)
            {
                float end = Mathf.Round(s.outpostSkillBandEnds[i]);
                if (end < prevEnd + 1f) end = prevEnd + 1f;
                s.outpostSkillBandEnds[i] = end;
                prevEnd = end;

                // Store as whole-percent fractions so HorizontalSlider(step=1) never sees off-step values.
                float wPct = Mathf.Round(Mathf.Clamp(s.outpostSkillBandWeights[i], 0.1f, 1f) * 100f);
                if (i == 0) wPct = Mathf.Clamp(wPct, 10f, 100f);
                else wPct = Mathf.Min(wPct, Mathf.Round(prevWeight * 100f));
                float w = wPct / 100f;
                s.outpostSkillBandWeights[i] = w;
                prevWeight = w;
            }
            float lastEnd = s.outpostSkillBandEnds[BandCount - 1];
            s.outpostSkillHardCapRaw = Mathf.Max(Mathf.Round(s.outpostSkillHardCapRaw), lastEnd);
        }

        public static void ResetToDefaults(WorldDominationSettings s)
        {
            if (s == null) return;
            s.enableOutpostSkillDiminishingReturns = DefEnableDiminishingReturns;
            s.outpostSkillHardCapRaw = DefHardCapRaw;
            s.outpostSkillBandEnds = (float[])DefBandEnds.Clone();
            s.outpostSkillBandWeights = (float[])DefBandWeights.Clone();
        }

        /// <summary>Raw cumulative skill used for production/capacity banners (0 = do not show).</summary>
        public static float GetBannerRawSkill(WorldObject_WD_Outpost outpost)
        {
            if (outpost?.def == null || !IsEnabled) return 0f;
            if (Outpost_Production_Utils.IsScavengingOutpost(outpost.def)) return 0f;
            if (Outpost_Production_Utils.IsFoodProducerOutpost(outpost.def))
                return outpost.GetFoodProductionCapacityRaw();
            if (Outpost_Production_Utils.IsMiningOutpost(outpost.def))
                return outpost.TotalMiningSkillRaw();
            if (Outpost_Production_Utils.IsResearchOutpost(outpost.def))
                return Outpost_Research.GetEffectiveCumulativeIntellectualRaw(outpost);
            if (Outpost_Production_Utils.IsRecruitingOutpost(outpost.def) || Outpost_Production_Utils.IsTradingOutpost(outpost.def) || Outpost_Production_Utils.IsEmbassyOutpost(outpost.def))
                return Outpost_Recruiting.GetDeliveryDrivingCapacityRaw(outpost);
            // Simple production / fabrication / construction-relevant: total relevant raw
            if (outpost.GetTotalRelevantSkillRaw() > 0f)
                return outpost.GetTotalRelevantSkillRaw();
            return 0f;
        }
    }
}
