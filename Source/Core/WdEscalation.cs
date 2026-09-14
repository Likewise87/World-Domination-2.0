using System.Text;
using RimWorld;
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

    /// <summary>When a combat/special threat feature may fire relative to escalation stage.</summary>
    public enum WdThreatStageGate : byte
    {
        Never = 0,
        FromMid = 1,
        FromLate = 2,
        Always = 3
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

        public static string ThreatGateLabel(WdThreatStageGate gate) => gate switch
        {
            WdThreatStageGate.Never => "TSA_WD_ThreatGate_Never".Translate().ToString(),
            WdThreatStageGate.FromMid => "TSA_WD_ThreatGate_FromMid".Translate().ToString(),
            WdThreatStageGate.FromLate => "TSA_WD_ThreatGate_FromLate".Translate().ToString(),
            WdThreatStageGate.Always => "TSA_WD_ThreatGate_Always".Translate().ToString(),
            _ => gate.ToString()
        };

        /// <summary>Never=false; Always=true; FromMid=Mid|Late; FromLate=Late only.</summary>
        public static bool PassesGate(WdThreatStageGate gate, WdEscalationStage stage)
        {
            return gate switch
            {
                WdThreatStageGate.Never => false,
                WdThreatStageGate.Always => true,
                WdThreatStageGate.FromMid => stage == WdEscalationStage.Mid || stage == WdEscalationStage.Late,
                WdThreatStageGate.FromLate => stage == WdEscalationStage.Late,
                _ => false
            };
        }

        public static bool PassesGate(WdThreatStageGate gate, WorldComponent_SpreadManager manager) =>
            PassesGate(gate, GetCachedStage(manager));

        /// <summary>Elapsed game days used for Mid/Late day thresholds.</summary>
        public static float DaysPassed()
        {
            if (Find.TickManager == null) return 0f;
            return Find.TickManager.TicksGame / (float)GenDate.TicksPerDay;
        }

        /// <summary>
        /// Candidate stage from live metrics (share OR strength OR days). Does not apply the world latch.
        /// </summary>
        public static WdEscalationStage GetStage(float playerOutpostStrength, float globalShare, WorldDominationSettings seth)
            => GetStage(playerOutpostStrength, globalShare, DaysPassed(), seth);

        public static WdEscalationStage GetStage(float playerOutpostStrength, float globalShare, float daysPassed, WorldDominationSettings seth)
        {
            if (seth == null || !seth.enableLateGameScaling) return WdEscalationStage.None;

            bool late = globalShare >= seth.lateGameShareThreshold
                || playerOutpostStrength >= seth.lateGameOutpostStrengthThreshold
                || daysPassed >= seth.lateGameDaysThreshold;
            if (late) return WdEscalationStage.Late;

            bool mid = globalShare >= seth.midGameShareThreshold
                || playerOutpostStrength >= seth.midGameOutpostStrengthThreshold
                || daysPassed >= seth.midGameDaysThreshold;
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

        /// <summary>T4 mortar may target the player for the active escalation stage (threat stage gate).</summary>
        public static bool CanTargetPlayerWithT4Mortar(WorldDominationSettings seth, WdEscalationStage stage)
        {
            if (seth == null) return false;
            return PassesGate(seth.gateThreatT4MortarVsPlayer, stage);
        }

        /// <summary>T4 AA may target the player for the active escalation stage (threat stage gate).</summary>
        public static bool CanTargetPlayerWithT4AntiAir(WorldDominationSettings seth, WdEscalationStage stage)
        {
            if (seth == null) return false;
            return PassesGate(seth.gateThreatT4AntiAirVsPlayer, stage);
        }

        /// <summary>Letter / alert body: intro + live Mid/Late effect lines from settings.</summary>
        public static string BuildStageLetterText(WorldDominationSettings seth, WdEscalationStage stage)
        {
            if (seth == null || (stage != WdEscalationStage.Mid && stage != WdEscalationStage.Late))
                return "";

            string intro = stage == WdEscalationStage.Late
                ? "TSA_WD_Letter_LateGameActiveIntro".Translate().ToString()
                : "TSA_WD_Letter_MidGameActiveIntro".Translate().ToString();
            string effects = BuildActiveEffectsTooltip(seth, stage);
            if (string.IsNullOrEmpty(effects))
                return intro;
            return intro + "\n\n" + effects;
        }

        public static string StageLetterLabel(WdEscalationStage stage) => stage switch
        {
            WdEscalationStage.Late => "TSA_WD_Alert_LateGameActive".Translate().ToString(),
            WdEscalationStage.Mid => "TSA_WD_Alert_MidGameActive".Translate().ToString(),
            _ => ""
        };

        public static void SendStageLetter(WorldDominationSettings seth, WdEscalationStage stage)
        {
            if (seth == null || (stage != WdEscalationStage.Mid && stage != WdEscalationStage.Late)) return;
            if (Find.LetterStack == null) return;
            string label = StageLetterLabel(stage);
            string text = BuildStageLetterText(seth, stage);
            if (string.IsNullOrEmpty(label) || string.IsNullOrEmpty(text)) return;
            Find.LetterStack.ReceiveLetter(label, text, LetterDefOf.NegativeEvent);
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
