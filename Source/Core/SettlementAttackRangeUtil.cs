using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// NPC settlement attack range:
    /// tierBaseline × midLateBaselineMult × settlementAgeFactor × (optional zeal).
    /// </summary>
    public static class SettlementAttackRangeUtil
    {
        /// <summary>1 + bonus for the active escalation stage (Late replaces Mid).</summary>
        public static float GetMidLateBaselineMultiplier(WorldDominationSettings? seth, WorldComponent_SpreadManager? manager)
        {
            if (seth == null || !seth.enableLateGameScaling || manager == null)
                return 1f;

            WdEscalationStage stage = manager.cachedEscalationStage;
            if (stage == WdEscalationStage.Late)
                return 1f + Mathf.Max(0f, seth.lateGameAttackRangeBonusPct);
            if (stage == WdEscalationStage.Mid)
                return 1f + Mathf.Max(0f, seth.midGameAttackRangeBonusPct);
            return 1f;
        }

        /// <summary>Per-settlement age factor from CompViralSpread.attackRangeFoundingTick.</summary>
        public static float GetSettlementAgeFactor(CompViralSpread? comp, WorldDominationSettings? seth)
        {
            if (seth == null) return 1f;
            float daysToMax = Mathf.Max(1f, seth.attackRangeDaysToMax);
            float bonusPct = Mathf.Max(0f, seth.attackRangeTimeMaxBonusPct);

            int founding = comp != null ? comp.EnsureAttackRangeFoundingTick() : Find.TickManager.TicksGame;
            float ageDays = Mathf.Max(0f, (Find.TickManager.TicksGame - founding) / 60000f);
            float progress = Mathf.Clamp01(ageDays / daysToMax);
            return 1f + progress * bonusPct;
        }

        /// <summary>Legacy name: now per-settlement age (needs settlement). Prefer GetSettlementAgeFactor.</summary>
        public static float GetTimeBonusMultiplier(WorldDominationSettings? seth)
            => GetSettlementAgeFactor(null, seth);

        public static float GetNpcSettlementAttackRangeTiles(Settlement settlement, WorldDominationSettings seth)
        {
            if (seth == null) return WorldDominationSettings.DefTier1AttackRangeBaseline;
            var comp = settlement?.GetComponent<CompViralSpread>();
            SettlementTier tier = comp?.tier ?? SettlementTier.T1;
            float baseline = seth.GetAttackRangeBaseline(tier);
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            return baseline
                * GetMidLateBaselineMultiplier(seth, manager)
                * GetSettlementAgeFactor(comp, seth);
        }

        public static float GetNpcSettlementAttackRangeWithZeal(Settlement settlement, WorldDominationSettings seth, WorldComponent_SpreadManager manager)
        {
            float range = GetNpcSettlementAttackRangeTiles(settlement, seth);
            if (manager != null && settlement?.Faction != null
                && settlement.Faction == manager.expansionistZealFaction
                && Find.TickManager.TicksGame < manager.expansionistZealExpiryTick)
            {
                range *= seth?.zealRaidRangeMult ?? WorldDominationSettings.DefZealRaidRangeMult;
            }
            return range;
        }

        /// <summary>Player-facing factor breakdown for inspect (no zeal).</summary>
        public static void GetRangeFactors(
            Settlement settlement,
            WorldDominationSettings seth,
            WorldComponent_SpreadManager manager,
            out float baseline,
            out float midLateMult,
            out float ageFactor,
            out float rangeWithoutZeal)
        {
            var comp = settlement?.GetComponent<CompViralSpread>();
            SettlementTier tier = comp?.tier ?? SettlementTier.T1;
            baseline = seth?.GetAttackRangeBaseline(tier) ?? WorldDominationSettings.DefTier1AttackRangeBaseline;
            midLateMult = GetMidLateBaselineMultiplier(seth, manager);
            ageFactor = GetSettlementAgeFactor(comp, seth);
            rangeWithoutZeal = baseline * midLateMult * ageFactor;
        }

        /// <summary>Stats-tab tooltip: short description plus factor breakdown (and zeal when active).</summary>
        public static string BuildSettlementAttackRangeTooltip(
            Settlement settlement,
            WorldDominationSettings seth,
            WorldComponent_SpreadManager manager,
            bool hasZeal,
            float effectiveRange)
        {
            GetRangeFactors(settlement, seth, manager,
                out float baseline, out float midLateMult, out float ageFactor, out _);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("TSA_WD_OutpostStats_Row_AttackRangeSettlementTip".Translate().ToString());
            sb.AppendLine();
            sb.AppendLine("TSA_WD_Inspect_AttackRangeBreakdown".Translate(
                baseline.ToString("F0"),
                (midLateMult * 100f).ToString("F0"),
                (ageFactor * 100f).ToString("F0")).ToString());
            if (hasZeal)
            {
                float zealMult = seth?.zealRaidRangeMult ?? WorldDominationSettings.DefZealRaidRangeMult;
                sb.AppendLine("TSA_WD_OutpostStats_Row_AttackRangeZealTip".Translate(
                    (zealMult * 100f).ToString("F0")).ToString());
            }
            sb.Append("TSA_WD_OutpostStats_Row_AttackRangeResult".Translate(effectiveRange.ToString("F0")).ToString());
            return sb.ToString().TrimEnd();
        }
    }
}
