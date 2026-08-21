using System.Collections.Generic;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    [StaticConstructorOnStartup]
    public static class Action_Settlement_Buy
    {
        private static Texture2D cachedIcon;

        public static Texture2D BuyIcon =>
            cachedIcon ??= ContentFinder<Texture2D>.Get("UI/Commands/Trade", false) ?? TexCommand.Install;

        public static IEnumerable<Gizmo> GetGizmos(Settlement settlement)
        {
            if (!SettlementBuyUtility.CanShowBuyGizmo(settlement, out string disabledReason))
                yield break;

            var buy = new Command_Action
            {
                defaultLabel = "TSA_WD_BuySettlement_GizmoLabel".Translate(),
                defaultDesc = "TSA_WD_BuySettlement_GizmoDesc".Translate(),
                icon = BuyIcon,
                action = () => Find.WindowStack.Add(new Dialog_SettlementBuyDeal(settlement))
            };
            if (!disabledReason.NullOrEmpty())
                buy.Disable(disabledReason);
            yield return buy;
        }

        /// <summary>
        /// Dev-console dump for faction investment from buy, gift, or bribe paths.
        /// </summary>
        public static void LogInvestmentDevConsole(
            Faction seller,
            int originTile,
            FactionSettlementInvestment.AwardResult result,
            List<string> awardLines,
            string noneReason,
            FactionSettlementInvestment.NotifyKind notify = FactionSettlementInvestment.NotifyKind.Buy)
        {
            string tag = notify == FactionSettlementInvestment.NotifyKind.Gift ? "[WD Gift]"
                : notify == FactionSettlementInvestment.NotifyKind.Loot ? "[WD Loot]"
                : notify == FactionSettlementInvestment.NotifyKind.Bribe ? "[WD Bribe]"
                : "[WD Buy]";
            string factionName = seller?.Name ?? "?";
            if (!noneReason.NullOrEmpty())
            {
                Log.Message(
                    $"{tag} Faction investment: none for {factionName} (tile {originTile}, budget {result.SilverBudget:F0} silver). Reason: {noneReason}");
                return;
            }

            var sb = new StringBuilder();
            sb.Append(
                $"{tag} Faction investment for {factionName} (tile {originTile}, budget {result.SilverBudget:F0} silver): ");
            sb.Append(
                $"{result.SettlementsStrengthened} strengthened, {result.SettlementsUpgraded} upgraded, {result.SettlementsUpgradeFailed} upgrade failed ");
            sb.Append(
                $"(strength silver {result.SilverSpentOnStrength:F0}, upgrade silver {result.SilverSpentOnUpgrades:F0}).");
            if (awardLines != null)
            {
                for (int i = 0; i < awardLines.Count; i++)
                    sb.Append("\n  - ").Append(awardLines[i]);
            }
            Log.Message(sb.ToString());
        }
    }

    public static class Patch_SettlementBuyGizmo
    {
        public static IEnumerable<Gizmo> GetGizmos(Settlement settlement)
        {
            foreach (Gizmo gizmo in Action_Settlement_Buy.GetGizmos(settlement))
                yield return gizmo;
            foreach (Gizmo gizmo in Action_Settlement_Gift.GetGizmos(settlement))
                yield return gizmo;
        }
    }
}
