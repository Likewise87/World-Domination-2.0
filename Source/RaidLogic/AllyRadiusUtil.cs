using System.Text;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Effective ally pull radius: ceil(base × mid/late mult) + optional Tunnel Network flat on WD outposts.
    /// Same formula for attacker and defender primaries.
    /// </summary>
    public static class AllyRadiusUtil
    {
        public static float GetMidLateMultiplier(WorldDominationSettings seth = null, WorldComponent_SpreadManager manager = null)
        {
            seth ??= WorldDominationMod.settings;
            if (seth == null || !seth.enableLateGameScaling)
                return 1f;

            manager ??= Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (manager == null)
                return 1f;

            WdEscalationStage stage = manager.cachedEscalationStage;
            if (stage == WdEscalationStage.Late && seth.enableLateGameAllyRadiusScaling)
                return 1f + Mathf.Max(0f, seth.lateGameAllyRadiusBonusPct);
            if (stage == WdEscalationStage.Mid && seth.enableMidGameAllyRadiusScaling)
                return 1f + Mathf.Max(0f, seth.midGameAllyRadiusBonusPct);
            return 1f;
        }

        public static float GetScaledBaseRadius(WorldDominationSettings seth = null, WorldComponent_SpreadManager manager = null)
        {
            seth ??= WorldDominationMod.settings;
            float baseR = seth?.raidAllyRadius ?? WorldDominationSettings.DefRaidAllyRadius;
            float mult = GetMidLateMultiplier(seth, manager);
            return Mathf.Ceil(baseR * mult);
        }

        public static float GetTunnelBonus(WorldObject primary)
        {
            if (primary is WorldObject_WD_Outpost outpost)
                return Mathf.Max(0f, outpost.GetBuiltUpgradeAllyPullRadiusBonus());
            return 0f;
        }

        /// <summary>Effective ally pull radius for this primary (attacker or defender).</summary>
        public static float GetEffective(WorldObject primary, WorldDominationSettings seth = null, WorldComponent_SpreadManager manager = null)
        {
            return GetScaledBaseRadius(seth, manager) + GetTunnelBonus(primary);
        }

        public static string BuildTooltip(WorldObject primary, WorldDominationSettings seth = null, WorldComponent_SpreadManager manager = null)
        {
            seth ??= WorldDominationMod.settings;
            manager ??= Find.World?.GetComponent<WorldComponent_SpreadManager>();
            float baseR = seth?.raidAllyRadius ?? WorldDominationSettings.DefRaidAllyRadius;
            float scaled = GetScaledBaseRadius(seth, manager);
            float flat = GetTunnelBonus(primary);
            float effective = scaled + flat;

            var sb = new StringBuilder();
            sb.AppendLine("TSA_WD_OutpostStats_Row_AllyRadiusTip".Translate().ToString());
            sb.AppendLine();
            sb.AppendLine("TSA_WD_AllyRadius_BreakdownBase".Translate(baseR.ToString("F0")).ToString());

            WdEscalationStage stage = manager?.cachedEscalationStage ?? WdEscalationStage.None;
            if (seth == null || !seth.enableLateGameScaling || stage == WdEscalationStage.None)
            {
                sb.AppendLine("TSA_WD_AllyRadius_BreakdownStageNone".Translate().ToString());
            }
            else if (stage == WdEscalationStage.Late)
            {
                if (seth.enableLateGameAllyRadiusScaling)
                    sb.AppendLine("TSA_WD_AllyRadius_BreakdownLate".Translate(
                        (seth.lateGameAllyRadiusBonusPct * 100f).ToString("F0")).ToString());
                else
                    sb.AppendLine("TSA_WD_AllyRadius_BreakdownLateOff".Translate().ToString());
            }
            else if (stage == WdEscalationStage.Mid)
            {
                if (seth.enableMidGameAllyRadiusScaling)
                    sb.AppendLine("TSA_WD_AllyRadius_BreakdownMid".Translate(
                        (seth.midGameAllyRadiusBonusPct * 100f).ToString("F0")).ToString());
                else
                    sb.AppendLine("TSA_WD_AllyRadius_BreakdownMidOff".Translate().ToString());
            }

            if (flat > 1e-6f)
                sb.AppendLine("TSA_WD_AllyRadius_BreakdownTunnel".Translate(flat.ToString("F0")).ToString());

            sb.Append("TSA_WD_AllyRadius_BreakdownResult".Translate(effective.ToString("F0")).ToString());
            return sb.ToString().TrimEnd();
        }
    }
}
