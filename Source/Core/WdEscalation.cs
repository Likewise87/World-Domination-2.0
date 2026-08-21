using System.Text;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Player-power escalation: Mid then Late. Late supersedes Mid for active effect values.</summary>
    public enum WdEscalationStage
    {
        None = 0,
        Mid = 1,
        Late = 2
    }

    /// <summary>
    /// Resolves Mid/Late gates and the active effect values for the current stage.
    /// Master switch remains <see cref="WorldDominationSettings.enableLateGameScaling"/>.
    /// </summary>
    public static class WdEscalation
    {
        public static string StageLabel(WdEscalationStage stage) => stage switch
        {
            WdEscalationStage.Late => "TSA_WD_Escalation_StageLate".Translate().ToString(),
            WdEscalationStage.Mid => "TSA_WD_Escalation_StageMid".Translate().ToString(),
            _ => "TSA_WD_Escalation_StageNone".Translate().ToString()
        };

        public static WdEscalationStage GetStage(float playerOutpostStrength, float globalShare, WorldDominationSettings seth)
        {
            if (seth == null || !seth.enableLateGameScaling) return WdEscalationStage.None;

            bool late = globalShare >= seth.lateGameShareThreshold
                || playerOutpostStrength >= seth.lateGameOutpostStrengthThreshold;
            if (late) return WdEscalationStage.Late;

            bool mid = globalShare >= seth.midGameShareThreshold
                || playerOutpostStrength >= seth.midGameOutpostStrengthThreshold;
            return mid ? WdEscalationStage.Mid : WdEscalationStage.None;
        }

        public static WdEscalationStage GetCachedStage(WorldComponent_SpreadManager manager)
        {
            if (manager == null) return WdEscalationStage.None;
            return manager.cachedEscalationStage;
        }

        public static bool IsLate(WorldComponent_SpreadManager manager) =>
            GetCachedStage(manager) == WdEscalationStage.Late;

        public static bool IsMidOrLate(WorldComponent_SpreadManager manager)
        {
            WdEscalationStage stage = GetCachedStage(manager);
            return stage == WdEscalationStage.Mid || stage == WdEscalationStage.Late;
        }

        public static float GetRaidBiasPct(WorldDominationSettings seth, WdEscalationStage stage)
        {
            if (seth == null) return 0f;
            return stage switch
            {
                WdEscalationStage.Late => Mathf.Max(0f, seth.lateGameRaidBiasPct),
                WdEscalationStage.Mid => Mathf.Max(0f, seth.midGameRaidBiasPct),
                _ => 0f
            };
        }

        public static float GetGrowthMult(WorldDominationSettings seth, WdEscalationStage stage)
        {
            if (seth == null) return 1f;
            return stage switch
            {
                WdEscalationStage.Late => Mathf.Max(1f, seth.lateGameGrowthMult),
                WdEscalationStage.Mid => Mathf.Max(1f, seth.midGameGrowthMult),
                _ => 1f
            };
        }

        public static float GetGarrisonBoostPct(WorldDominationSettings seth, WdEscalationStage stage)
        {
            if (seth == null) return 0f;
            return stage switch
            {
                WdEscalationStage.Late => Mathf.Max(0f, seth.lateGameGarrisonBoostPct),
                WdEscalationStage.Mid => Mathf.Max(0f, seth.midGameGarrisonBoostPct),
                _ => 0f
            };
        }

        public static int GetExpandTowardPlayerMaxTiles(WorldDominationSettings seth, WdEscalationStage stage)
        {
            if (seth == null) return 0;
            return stage switch
            {
                WdEscalationStage.Late => Mathf.Max(0, seth.lateGameExpandTowardPlayerMaxTiles),
                WdEscalationStage.Mid => Mathf.Max(0, seth.midGameExpandTowardPlayerMaxTiles),
                _ => 0
            };
        }

        public static bool OutpostIncidentsEnabled(WorldDominationSettings seth, WdEscalationStage stage)
        {
            if (seth == null) return false;
            return stage switch
            {
                WdEscalationStage.Late => seth.enableOutpostIncidents,
                WdEscalationStage.Mid => seth.enableMidGameOutpostIncidents,
                _ => false
            };
        }

        public static float GetOutpostIncidentSeverity(WorldDominationSettings seth, WdEscalationStage stage)
        {
            if (seth == null) return 0f;
            return stage switch
            {
                WdEscalationStage.Late => Mathf.Max(0f, seth.outpostIncidentSeverity),
                WdEscalationStage.Mid => Mathf.Max(0f, seth.midGameOutpostIncidentSeverity),
                _ => 0f
            };
        }

        public static float GetOutpostIncidentDailyChance(WorldDominationSettings seth, WdEscalationStage stage)
        {
            if (seth == null) return 0f;
            return stage switch
            {
                WdEscalationStage.Late => Mathf.Clamp01(seth.outpostIncidentDailyChance),
                WdEscalationStage.Mid => Mathf.Clamp01(seth.midGameOutpostIncidentDailyChance),
                _ => 0f
            };
        }

        public static int GetGoodwillDrainAmount(WorldDominationSettings seth, WdEscalationStage stage)
        {
            if (seth == null || !seth.enableGoodwillDrain) return 0;
            return stage switch
            {
                WdEscalationStage.Late => Mathf.Max(0, seth.lateGameGoodwillDrainAmount),
                WdEscalationStage.Mid => Mathf.Max(0, seth.midGameGoodwillDrainAmount),
                _ => 0
            };
        }

        /// <summary>T4 mortar may target the player for the active stage flag (Mid or Late).</summary>
        public static bool CanTargetPlayerWithT4Mortar(WorldDominationSettings seth, WdEscalationStage stage)
        {
            if (seth == null) return false;
            return stage switch
            {
                WdEscalationStage.Late => seth.enableT4SettlementMortar,
                WdEscalationStage.Mid => seth.enableMidGameT4SettlementMortar,
                _ => false
            };
        }

        /// <summary>T4 AA may target the player for the active stage flag (Mid or Late).</summary>
        public static bool CanTargetPlayerWithT4AntiAir(WorldDominationSettings seth, WdEscalationStage stage)
        {
            if (seth == null) return false;
            return stage switch
            {
                WdEscalationStage.Late => seth.enableT4SettlementAntiAir,
                WdEscalationStage.Mid => seth.enableMidGameT4SettlementAntiAir,
                _ => false
            };
        }

        /// <summary>
        /// Dashboard mouseover: one line per Mid/Late effect with the live setting values.
        /// </summary>
        public static string BuildActiveEffectsTooltip(WorldDominationSettings seth, WdEscalationStage stage)
        {
            if (seth == null || (stage != WdEscalationStage.Mid && stage != WdEscalationStage.Late))
                return "";

            bool late = stage == WdEscalationStage.Late;
            bool allyScaleOn = late ? seth.enableLateGameAllyRadiusScaling : seth.enableMidGameAllyRadiusScaling;
            float allyBonusPct = late ? seth.lateGameAllyRadiusBonusPct : seth.midGameAllyRadiusBonusPct;
            float attackBonusPct = late ? seth.lateGameAttackRangeBonusPct : seth.midGameAttackRangeBonusPct;
            float raidBiasPct = GetRaidBiasPct(seth, stage);
            float growthMult = GetGrowthMult(seth, stage);
            float garrisonPct = GetGarrisonBoostPct(seth, stage);
            int expandTiles = GetExpandTowardPlayerMaxTiles(seth, stage);
            bool t4Mortar = CanTargetPlayerWithT4Mortar(seth, stage);
            bool t4Aa = CanTargetPlayerWithT4AntiAir(seth, stage);
            bool incidentsOn = OutpostIncidentsEnabled(seth, stage);
            float incidentSev = GetOutpostIncidentSeverity(seth, stage);
            float incidentChance = GetOutpostIncidentDailyChance(seth, stage);
            int goodwillDrain = GetGoodwillDrainAmount(seth, stage);
            int goodwillDays = Mathf.Max(1, seth.goodwillDrainIntervalDays);

            var sb = new StringBuilder();
            if (allyScaleOn)
                sb.AppendLine("TSA_WD_Dash_EscalationTip_AllyRadius".Translate(Pct0(allyBonusPct)).ToString());
            else
                sb.AppendLine("TSA_WD_Dash_EscalationTip_AllyRadiusOff".Translate().ToString());

            sb.AppendLine("TSA_WD_Dash_EscalationTip_AttackRange".Translate(Pct0(attackBonusPct)).ToString());
            sb.AppendLine("TSA_WD_Dash_EscalationTip_RaidBias".Translate(Pct0(raidBiasPct)).ToString());
            sb.AppendLine("TSA_WD_Dash_EscalationTip_Growth".Translate(growthMult.ToString("0.##")).ToString());
            sb.AppendLine("TSA_WD_Dash_EscalationTip_Garrison".Translate(Pct0(garrisonPct)).ToString());
            sb.AppendLine("TSA_WD_Dash_EscalationTip_Expand".Translate(expandTiles.ToString()).ToString());
            sb.AppendLine((t4Mortar
                ? "TSA_WD_Dash_EscalationTip_T4MortarOn"
                : "TSA_WD_Dash_EscalationTip_T4MortarOff").Translate().ToString());
            sb.AppendLine((t4Aa
                ? "TSA_WD_Dash_EscalationTip_T4AaOn"
                : "TSA_WD_Dash_EscalationTip_T4AaOff").Translate().ToString());

            if (incidentsOn)
                sb.AppendLine("TSA_WD_Dash_EscalationTip_IncidentsOn".Translate(
                    incidentSev.ToString("F0"), Pct0(incidentChance)).ToString());
            else
                sb.AppendLine("TSA_WD_Dash_EscalationTip_IncidentsOff".Translate().ToString());

            if (seth.enableGoodwillDrain && goodwillDrain > 0)
                sb.AppendLine("TSA_WD_Dash_EscalationTip_Goodwill".Translate(
                    goodwillDrain.ToString(), goodwillDays.ToString()).ToString());
            else
                sb.AppendLine("TSA_WD_Dash_EscalationTip_GoodwillOff".Translate().ToString());

            if (seth.enableOutpostUpkeep)
                sb.AppendLine("TSA_WD_Dash_EscalationTip_Upkeep".Translate(
                    seth.upkeepSilverPerOccupant.ToString(),
                    seth.upkeepIntervalDays.ToString()).ToString());
            else
                sb.AppendLine("TSA_WD_Dash_EscalationTip_UpkeepOff".Translate().ToString());

            return sb.ToString().TrimEnd();
        }

        private static string Pct0(float fraction) => (Mathf.Max(0f, fraction) * 100f).ToString("F0");
    }
}
