using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Early diminishing returns on raw Cum. Social for outpost prisoner resistance drop only
    /// (separate from production <see cref="OutpostSkillScaling"/>).
    /// </summary>
    public static class OutpostPrisonerResistanceScaling
    {
        public const int BandCount = 5;
        public static readonly float[] BandEnds = { 20f, 40f, 80f, 160f, 200f };
        public static readonly float[] BandWeights = { 1f, 0.5f, 0.25f, 0.1f, 0.05f };
        public const float HardCapRaw = 200f;
        public const float SocialPerResistancePoint = 10f;

        public static float GetRawCumSocial(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return 0f;
            return Mathf.Max(0f, outpost.GetSkillSumRaw(SkillDefOf.Social));
        }

        /// <summary>Raw Cum. Social → resistance-effective Social for the /10 base drop.</summary>
        public static float ToResistanceEffective(float rawCumSocial)
        {
            if (rawCumSocial <= 0f) return 0f;
            float cappedRaw = Mathf.Min(rawCumSocial, HardCapRaw);
            float effective = 0f;
            float prevEnd = 0f;
            for (int i = 0; i < BandCount; i++)
            {
                float end = BandEnds[i];
                float weight = BandWeights[i];
                if (cappedRaw <= prevEnd) break;
                float segment = Mathf.Min(cappedRaw, end) - prevEnd;
                if (segment > 0f)
                    effective += segment * weight;
                prevEnd = end;
                if (cappedRaw <= end) break;
            }
            return effective;
        }

        public static float GetBaseDropPerDay(WorldObject_WD_Outpost outpost)
        {
            float eff = ToResistanceEffective(GetRawCumSocial(outpost));
            return eff / SocialPerResistancePoint;
        }

        public static float GetWardenBonusFraction(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return 0f;
            return OutpostExpertUtility.GetExpertBonusFraction(outpost, OutpostExpertRole.Recruiter);
        }

        public static float GetDailyDrop(WorldObject_WD_Outpost outpost)
        {
            float baseDrop = GetBaseDropPerDay(outpost);
            if (baseDrop <= 0f) return 0f;
            float bonus = GetWardenBonusFraction(outpost);
            return baseDrop * (1f + bonus);
        }

        /// <summary>How many Attempt Recruit captives this outpost can work at once. Always at least 1.</summary>
        public static int GetConcurrentRecruitSlots(WorldObject_WD_Outpost outpost)
        {
            int fromSocial = Mathf.FloorToInt(GetRawCumSocial(outpost) / SocialPerResistancePoint);
            return Mathf.Max(1, fromSocial);
        }

        public static string FormatRateLabel(float resistance, float dailyDrop)
        {
            if (dailyDrop > 0.0001f)
                return resistance.ToString("F1") + "\n" + ("-" + dailyDrop.ToString("F1") + "/d");
            return resistance.ToString("F1");
        }

        public static string BuildTooltip(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return "";
            float raw = GetRawCumSocial(outpost);
            float eff = ToResistanceEffective(raw);
            float baseDrop = eff / SocialPerResistancePoint;
            float bonus = GetWardenBonusFraction(outpost);
            float finalDrop = baseDrop * (1f + bonus);
            int bonusPct = Mathf.RoundToInt(bonus * 100f);

            var sb = new StringBuilder();
            sb.AppendLine("TSA_WD_Prisoners_ResistanceTip_CumSocial".Translate(
                raw.ToString("F0"),
                eff.ToString("F0"),
                baseDrop.ToString("F1")));
            if (bonusPct > 0)
                sb.AppendLine("TSA_WD_Prisoners_ResistanceTip_Warden".Translate(bonusPct.ToString()));
            sb.Append("TSA_WD_Prisoners_ResistanceTip_Result".Translate(finalDrop.ToString("F1")));
            return sb.ToString().TrimEnd();
        }

        /// <summary>Compact Stats-tab math: base from Cum. Social, Warden %, result.</summary>
        public static string BuildStatsTabTooltip(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return "";
            float raw = GetRawCumSocial(outpost);
            float baseDrop = GetBaseDropPerDay(outpost);
            float bonus = GetWardenBonusFraction(outpost);
            float finalDrop = baseDrop * (1f + bonus);
            int bonusPct = Mathf.RoundToInt(bonus * 100f);

            var sb = new StringBuilder();
            sb.AppendLine("TSA_WD_OutpostStats_Row_PrisonerResistanceTip_Base".Translate(
                raw.ToString("F0"),
                baseDrop.ToString("F1")));
            sb.AppendLine("TSA_WD_OutpostStats_Row_PrisonerResistanceTip_Warden".Translate(
                bonusPct.ToString()));
            sb.Append("TSA_WD_OutpostStats_Row_PrisonerResistanceTip_Result".Translate(
                finalDrop.ToString("F1")));
            return sb.ToString().TrimEnd();
        }
    }
}
