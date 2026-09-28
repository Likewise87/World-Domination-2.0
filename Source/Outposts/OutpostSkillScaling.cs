using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Piecewise diminishing returns on cumulative outpost skill (production/capacity), then hard cap.
    /// Per-pawn levels and founding gates stay raw; call <see cref="ToEffective"/> only on cumulative totals.
    /// Recruiting / trading / embassy use an earlier first-band end (<see cref="DefSocialFirstBandEnd"/>).
    /// </summary>
    public static class OutpostSkillScaling
    {
        public const int BandCount = 5;

        public static readonly float[] DefBandEnds = { 60f, 100f, 160f, 220f, 280f };
        public static readonly float[] DefBandWeights = { 1f, 0.8f, 0.6f, 0.4f, 0.2f };
        public const float DefHardCapRaw = 280f;
        public const bool DefEnableDiminishingReturns = true;
        /// <summary>First full-efficiency band end for Social production outposts (recruiting / trading / embassy).</summary>
        public const float DefSocialFirstBandEnd = 40f;
        public const float SocialFirstBandEndMin = 10f;
        public const float SocialFirstBandEndMax = 120f;

        /// <summary>Scratch ends for social override (not re-entrant across threads; UI/sim is main-thread).</summary>
        private static readonly float[] SocialEndsScratch = new float[BandCount];

        public static WorldDominationSettings Settings => WorldDominationMod.settings;

        public static bool IsEnabled => Settings?.enableOutpostSkillDiminishingReturns ?? DefEnableDiminishingReturns;

        public static float HardCapRaw =>
            Mathf.Max(1f, Settings?.outpostSkillHardCapRaw ?? DefHardCapRaw);

        public static float SocialFirstBandEnd =>
            Mathf.Round(Mathf.Clamp(
                Settings?.outpostSkillSocialFirstBandEnd ?? DefSocialFirstBandEnd,
                SocialFirstBandEndMin,
                SocialFirstBandEndMax));

        public static bool UsesSocialProductionScaling(WorldObjectDef def) =>
            def != null
            && (Outpost_Production_Utils.IsRecruitingOutpost(def)
                || Outpost_Production_Utils.IsTradingOutpost(def)
                || Outpost_Production_Utils.IsEmbassyOutpost(def));

        /// <summary>Raw cumulative skill → effective skill (global band table).</summary>
        public static float ToEffective(float raw) => ToEffective(raw, null);

        /// <summary>
        /// Raw cumulative skill → effective skill. When <paramref name="def"/> is a Social production outpost,
        /// band 0 ends at <see cref="SocialFirstBandEnd"/> instead of the global first band end.
        /// </summary>
        public static float ToEffective(float raw, WorldObjectDef def)
        {
            if (raw <= 0f) return 0f;
            if (!IsEnabled) return raw;

            ResolveBands(def, out float[] ends, out float[] weights);
            return ApplyBands(raw, ends, weights);
        }

        /// <summary>Preview helper: apply Social first-band override without a concrete def.</summary>
        public static float ToEffectiveSocial(float raw) =>
            ToEffectiveWithSocialOverride(raw, true);

        private static float ToEffectiveWithSocialOverride(float raw, bool social)
        {
            if (raw <= 0f) return 0f;
            if (!IsEnabled) return raw;
            ResolveBands(social, out float[] ends, out float[] weights);
            return ApplyBands(raw, ends, weights);
        }

        private static float ApplyBands(float raw, float[] ends, float[] weights)
        {
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

            if (cappedRaw > prevEnd)
            {
                float lastWeight = Mathf.Clamp01(weights[BandCount - 1]);
                effective += (cappedRaw - prevEnd) * lastWeight;
            }

            return effective;
        }

        private static void ResolveBands(WorldObjectDef def, out float[] ends, out float[] weights) =>
            ResolveBands(UsesSocialProductionScaling(def), out ends, out weights);

        private static void ResolveBands(bool socialOverride, out float[] ends, out float[] weights)
        {
            var s = Settings;
            ends = s?.outpostSkillBandEnds;
            weights = s?.outpostSkillBandWeights;
            if (ends == null || weights == null || ends.Length < BandCount || weights.Length < BandCount)
            {
                ends = DefBandEnds;
                weights = DefBandWeights;
            }

            if (!socialOverride)
                return;

            for (int i = 0; i < BandCount; i++)
                SocialEndsScratch[i] = ends[i];

            float secondEnd = Mathf.Max(SocialEndsScratch[1], 2f);
            float socialFirst = SocialFirstBandEnd;
            socialFirst = Mathf.Clamp(socialFirst, 1f, secondEnd - 1f);
            SocialEndsScratch[0] = socialFirst;
            ends = SocialEndsScratch;
        }

        public static bool IsDiminished(float raw) => IsDiminished(raw, null);

        public static bool IsDiminished(float raw, WorldObjectDef def) =>
            IsEnabled && raw > 0f && !Mathf.Approximately(ToEffective(raw, def), raw);

        public static bool IsAtOrAboveHardCap(float raw) =>
            IsEnabled && raw >= HardCapRaw - 0.0001f;

        public static float FirstFullBandEnd(WorldObjectDef def = null)
        {
            ResolveBands(def, out float[] ends, out _);
            return ends[0];
        }

        public static string FormatRawEffective(float raw) => FormatRawEffective(raw, null);

        public static string FormatRawEffective(float raw, WorldObjectDef def)
        {
            float eff = ToEffective(raw, def);
            if (!IsEnabled || Mathf.Approximately(raw, eff))
                return raw.ToString("F0");
            return "TSA_WD_SkillScaling_RawToEffective".Translate(raw.ToString("F0"), eff.ToString("F0")).ToString();
        }

        public static string BuildBandBreakdownTip(float raw) => BuildBandBreakdownTip(raw, null);

        public static string BuildBandBreakdownTip(float raw, WorldObjectDef def) =>
            BuildBandBreakdownTip(raw, UsesSocialProductionScaling(def));

        /// <summary>Band tip for global (<paramref name="socialOverride"/> false) or Social-outpost curve.</summary>
        public static string BuildBandBreakdownTip(float raw, bool socialOverride)
        {
            if (!IsEnabled) return "TSA_WD_SkillScaling_DisabledTip".Translate();

            float eff = ToEffectiveWithSocialOverride(raw, socialOverride);
            var sb = new StringBuilder();
            sb.AppendLine("TSA_WD_SkillScaling_BreakdownHeader".Translate(raw.ToString("F0"), eff.ToString("F0")));
            ResolveBands(socialOverride, out float[] ends, out float[] weights);
            float prevEnd = 0f;
            for (int i = 0; i < BandCount && i < ends.Length && i < weights.Length; i++)
            {
                float end = ends[i];
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

                float wPct = Mathf.Round(Mathf.Clamp(s.outpostSkillBandWeights[i], 0.1f, 1f) * 100f);
                if (i == 0) wPct = Mathf.Clamp(wPct, 10f, 100f);
                else wPct = Mathf.Min(wPct, Mathf.Round(prevWeight * 100f));
                float w = wPct / 100f;
                s.outpostSkillBandWeights[i] = w;
                prevWeight = w;
            }
            float lastEnd = s.outpostSkillBandEnds[BandCount - 1];
            s.outpostSkillHardCapRaw = Mathf.Max(Mathf.Round(s.outpostSkillHardCapRaw), lastEnd);

            float secondEnd = s.outpostSkillBandEnds[1];
            float socialMax = Mathf.Min(SocialFirstBandEndMax, secondEnd - 1f);
            float socialMin = SocialFirstBandEndMin;
            if (socialMax < socialMin) socialMax = socialMin;
            s.outpostSkillSocialFirstBandEnd = Mathf.Clamp(
                Mathf.Round(s.outpostSkillSocialFirstBandEnd), socialMin, socialMax);
        }

        public static void ResetToDefaults(WorldDominationSettings s)
        {
            if (s == null) return;
            s.enableOutpostSkillDiminishingReturns = DefEnableDiminishingReturns;
            s.outpostSkillHardCapRaw = DefHardCapRaw;
            s.outpostSkillBandEnds = (float[])DefBandEnds.Clone();
            s.outpostSkillBandWeights = (float[])DefBandWeights.Clone();
            s.outpostSkillSocialFirstBandEnd = DefSocialFirstBandEnd;
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
            if (UsesSocialProductionScaling(outpost.def))
                return Outpost_Recruiting.GetDeliveryDrivingCapacityRaw(outpost);
            if (outpost.GetTotalRelevantSkillRaw() > 0f)
                return outpost.GetTotalRelevantSkillRaw();
            return 0f;
        }
    }
}
