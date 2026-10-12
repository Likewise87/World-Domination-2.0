using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Effective ally pull radius: ceil(base × mid/late × dip) plus optional Tunnel Network flat.
    /// Tunnel annulus (beyond base) only pulls peers that also have Tunnel Network.
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

        public static bool HasTunnelNetwork(WorldObject worldObject)
        {
            return GetTunnelBonus(worldObject) > 1e-6f;
        }

        private static float ApplyDiplomacyRadiusMult(float radius, WorldObject primary, WorldDominationSettings seth, WorldComponent_SpreadManager manager)
        {
            Faction f = primary?.Faction;
            if (f != null && seth != null)
            {
                if (WorldActions_DiplomacyBuffsNerfs.IsActiveUnderdog(f, manager))
                    radius *= seth.underdogAllyRadiusMult;
                else if (WorldActions_DiplomacyBuffsNerfs.IsActiveLeader(f, manager))
                    radius *= seth.leaderAllyRadiusMult;
            }
            return Mathf.Ceil(radius);
        }

        /// <summary>Scaled base ally radius with underdog/leader mult, without Tunnel Network flat.</summary>
        public static float GetBaseEffective(WorldObject primary, WorldDominationSettings seth = null, WorldComponent_SpreadManager manager = null)
        {
            seth ??= WorldDominationMod.settings;
            manager ??= Find.World?.GetComponent<WorldComponent_SpreadManager>();
            return ApplyDiplomacyRadiusMult(GetScaledBaseRadius(seth, manager), primary, seth, manager);
        }

        /// <summary>Effective ally pull radius for this primary (attacker or defender).</summary>
        public static float GetEffective(WorldObject primary, WorldDominationSettings seth = null, WorldComponent_SpreadManager manager = null)
        {
            seth ??= WorldDominationMod.settings;
            manager ??= Find.World?.GetComponent<WorldComponent_SpreadManager>();
            float radius = GetScaledBaseRadius(seth, manager) + GetTunnelBonus(primary);
            return ApplyDiplomacyRadiusMult(radius, primary, seth, manager);
        }

        /// <summary>
        /// True when <paramref name="candidate"/> may reinforce <paramref name="primary"/> at <paramref name="distance"/>.
        /// Base disk: any eligible ally. Tunnel annulus: both must have Tunnel Network.
        /// </summary>
        public static bool MayPullAtDistance(
            WorldObject primary,
            WorldObject candidate,
            float distance,
            float fullRadius,
            WorldDominationSettings seth = null,
            WorldComponent_SpreadManager manager = null)
        {
            if (primary == null || candidate == null || fullRadius <= 0f || distance > fullRadius)
                return false;

            float baseR = GetBaseEffective(primary, seth, manager);
            if (distance <= baseR)
                return true;

            return HasTunnelNetwork(primary) && HasTunnelNetwork(candidate);
        }

        public static string BuildTooltip(WorldObject primary, WorldDominationSettings seth = null, WorldComponent_SpreadManager manager = null)
        {
            seth ??= WorldDominationMod.settings;
            manager ??= Find.World?.GetComponent<WorldComponent_SpreadManager>();
            float baseR = seth?.raidAllyRadius ?? WorldDominationSettings.DefRaidAllyRadius;
            float flat = GetTunnelBonus(primary);
            float effective = GetEffective(primary, seth, manager);

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
