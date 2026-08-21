using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace TSA_WorldDomination
{
    public enum DealItemCategory
    {
        All,
        Weapons,
        Apparel,
        Textiles,
        Food,
        Other
    }

    /// <summary>Shared confirm-button tip bits and deal-table helpers for buy/gift/bribe dialogs.</summary>
    public static class SettlementCaravanDealUi
    {
        public const float ColStar = 32f;
        public const float CategoryFilterWidth = 120f;

        /// <summary>Session-shared category filter across Buy / Gift / Bribe (not save-backed).</summary>
        public static DealItemCategory SessionCategory = DealItemCategory.All;

        private static string? _starTipCache;

        public static string StarTip()
        {
            if (_starTipCache == null)
                _starTipCache = "TSA_WD_DealItems_StarTip".Translate();
            return _starTipCache;
        }

        public static string InterceptWarningLine()
        {
            string key = "TSA_WD_SettlementCaravan_InterceptWarning";
            string t = key.Translate().ToString();
            if (t == key || t.Contains("TSA_WD_"))
                t = "Warning: If this caravan is intercepted, the attacker gains settlement strength from the value of the goods.";
            return t;
        }

        public static string BuildConfirmTooltip(string disableReason)
        {
            var sb = new StringBuilder();
            if (!disableReason.NullOrEmpty())
                sb.AppendLine(disableReason);
            if (sb.Length > 0)
                sb.AppendLine();
            sb.Append(InterceptWarningLine());
            return sb.ToString().TrimEnd();
        }

        public static DealItemCategory GetCategory(ThingDef def)
        {
            if (def == null) return DealItemCategory.Other;
            if (def.IsWeapon) return DealItemCategory.Weapons;
            if (def.IsApparel) return DealItemCategory.Apparel;
            if (def.IsNutritionGivingIngestible) return DealItemCategory.Food;
            if (IsTextile(def)) return DealItemCategory.Textiles;
            return DealItemCategory.Other;
        }

        private static bool IsTextile(ThingDef def)
        {
            if (def == null) return false;
            if (ThingCategoryDefOf.Textiles != null && def.IsWithinCategory(ThingCategoryDefOf.Textiles))
                return true;
            if (!def.IsStuff || def.stuffProps?.categories == null)
                return false;
            var cats = def.stuffProps.categories;
            for (int i = 0; i < cats.Count; i++)
            {
                StuffCategoryDef c = cats[i];
                if (c == null) continue;
                if (c == StuffCategoryDefOf.Fabric || c == StuffCategoryDefOf.Leathery)
                    return true;
                string n = c.defName ?? "";
                if (n.IndexOf("Wool", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Fabric", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Leather", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        public static string CategoryLabel(DealItemCategory cat)
        {
            switch (cat)
            {
                case DealItemCategory.Weapons: return "TSA_WD_DealItems_CatWeapons".Translate();
                case DealItemCategory.Apparel: return "TSA_WD_DealItems_CatApparel".Translate();
                case DealItemCategory.Textiles: return "TSA_WD_DealItems_CatTextiles".Translate();
                case DealItemCategory.Food: return "TSA_WD_DealItems_CatFood".Translate();
                case DealItemCategory.Other: return "TSA_WD_DealItems_CatOther".Translate();
                default: return "TSA_WD_DealItems_CatAll".Translate();
            }
        }

        public static bool PassesListFilter(ThingDefCountClass row, string textFilter, DealItemCategory category)
        {
            if (row?.thingDef == null) return false;
            if (category != DealItemCategory.All && GetCategory(row.thingDef) != category)
                return false;
            string f = (textFilter ?? "").Trim().ToLowerInvariant();
            if (f.NullOrEmpty()) return true;
            string label = SettlementBuyUtility.FormatStockLabel(row);
            if (label != null && label.ToLowerInvariant().Contains(f))
                return true;
            return row.thingDef.defName.ToLowerInvariant().Contains(f);
        }

        public static void DrawCategoryDropdown(Rect rect)
        {
            if (!Widgets.ButtonText(rect, CategoryLabel(SessionCategory)))
                return;
            var options = new List<FloatMenuOption>();
            foreach (DealItemCategory cat in Enum.GetValues(typeof(DealItemCategory)))
            {
                DealItemCategory captured = cat;
                options.Add(new FloatMenuOption(
                    CategoryLabel(captured),
                    () => SessionCategory = captured));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        public static void DrawStarHeader(Rect cell)
        {
            TextAnchor prev = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(cell, "TSA_WD_DealItems_ColStar".Translate());
            Text.Anchor = prev;
            TooltipHandler.TipRegion(cell, StarTip());
        }

        public static void DrawStarToggle(Rect cell, ThingDef def)
        {
            if (def == null) return;
            bool starred = WorldComponent_SettlementDealFavorites.Get()?.IsStarred(def) == true;
            TextAnchor prevA = Text.Anchor;
            Color prevC = GUI.color;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = starred ? new Color(1f, 0.85f, 0.2f) : new Color(0.55f, 0.55f, 0.55f, 0.7f);
            Widgets.Label(cell, starred ? "★" : "☆");
            GUI.color = prevC;
            Text.Anchor = prevA;
            TooltipHandler.TipRegion(cell, StarTip());
            if (Widgets.ButtonInvisible(cell))
            {
                WorldComponent_SettlementDealFavorites.Get()?.Toggle(def);
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
        }

        public static void NotifyAssignUnderfill()
        {
            Messages.Message(
                "TSA_WD_DealItems_AssignUnderfill".Translate(),
                MessageTypeDefOf.RejectInput,
                false);
        }

        /// <summary>
        /// Clears <paramref name="offered"/> and fills from highest unit value first until
        /// <paramref name="targetSilver"/> is met. Skips starred defs. The last row takes only as
        /// many items as needed (not the full stack).
        /// </summary>
        /// <returns>True if the target was met (or target was zero). False if still under ask.</returns>
        public static bool AssignGoodsToTarget(
            IList<ThingDefCountClass> rows,
            Dictionary<string, int> offered,
            Dictionary<string, string> countEditBuffers,
            float targetSilver,
            Func<ThingDefCountClass, float> unitValue)
        {
            offered?.Clear();
            countEditBuffers?.Clear();
            float target = SettlementBuyUtility.RoundSilver(targetSilver);
            if (target <= 0.0001f)
                return true;
            if (offered == null || rows == null || rows.Count == 0 || unitValue == null)
                return false;

            var favorites = WorldComponent_SettlementDealFavorites.Get();
            var order = new List<ThingDefCountClass>(rows);
            order.Sort((a, b) =>
            {
                int cmp = unitValue(b).CompareTo(unitValue(a));
                if (cmp != 0) return cmp;
                return string.CompareOrdinal(CompOutpostWarehouse.StockKey(a), CompOutpostWarehouse.StockKey(b));
            });

            float covered = 0f;
            for (int i = 0; i < order.Count; i++)
            {
                if (SettlementBuyUtility.MeetsAsk(covered, target))
                    break;

                ThingDefCountClass row = order[i];
                if (row?.thingDef == null || row.count <= 0) continue;
                if (favorites != null && favorites.IsStarred(row.thingDef))
                    continue;

                float unit = unitValue(row);
                if (unit <= 0.0001f) continue;

                float need = Mathf.Max(0f, target - SettlementBuyUtility.RoundSilver(covered));
                int count = Mathf.CeilToInt(need / unit - 1e-4f);
                if (count < 1) count = 1;
                if (count > row.count) count = row.count;

                offered[CompOutpostWarehouse.StockKey(row)] = count;
                covered += unit * count;
            }

            return SettlementBuyUtility.MeetsAsk(covered, target);
        }
    }

    /// <summary>Hostile world-clash seize of buy/gift payment → faction settlement investment near clash tile.</summary>
    public static class SettlementCaravanLootUtility
    {
        public static void TrySeizeBeforeDestroy(WorldObject_Traveler loser, Faction winnerFaction)
        {
            if (loser == null || loser.Destroyed || winnerFaction == null || winnerFaction.IsPlayer)
                return;
            if (loser.Faction != null && loser.Faction.IsPlayer
                && winnerFaction.HostileTo(Faction.OfPlayer))
            {
                if (loser is WorldObject_Traveler_SettlementBuy buy)
                    SettlementBuyUtility.MarkPaymentLostInTransit(buy, winnerFaction);
                else if (loser is WorldObject_Traveler_SettlementGift gift)
                    SettlementGiftUtility.MarkPaymentLostInTransit(gift, winnerFaction);
                else if (loser is WorldObject_Traveler_SettlementBribe bribe)
                    SettlementBribeUtility.MarkPaymentLostInTransit(bribe, winnerFaction);
                else if (loser is WorldObject_Traveler_DiplomacyNegotiate negotiate)
                    DiplomacyNegotiateUtility.MarkPaymentLostInTransit(negotiate, winnerFaction);
            }
        }

        public static void AwardLootToFaction(Faction looter, int clashTile, float silverBudget, bool isGiftMission)
        {
            if (looter == null || looter.IsPlayer || clashTile < 0 || silverBudget <= 0.01f)
                return;

            int radius = WorldDominationMod.settings?.factionInvestmentRadiusTiles
                ?? WorldDominationSettings.DefFactionInvestmentRadiusTiles;
            Settlement prefer = FactionSettlementInvestment.FindNearestFactionSettlement(looter, clashTile, radius);

            var result = FactionSettlementInvestment.AwardFromSilverBudget(
                looter,
                clashTile,
                silverBudget,
                preferFirst: prefer,
                notify: FactionSettlementInvestment.NotifyKind.Loot);

            SendLootLetter(looter, clashTile, silverBudget, radius, result, isGiftMission);
        }

        private static void SendLootLetter(
            Faction looter,
            int clashTile,
            float silverBudget,
            int radius,
            FactionSettlementInvestment.AwardResult result,
            bool isGiftMission)
        {
            string factionName = looter?.Name ?? "?";
            string missionWord = isGiftMission
                ? "TSA_WD_SettlementCaravan_LootMissionGift".Translate().ToString()
                : "TSA_WD_SettlementCaravan_LootMissionBuy".Translate().ToString();

            var sb = new StringBuilder();
            sb.AppendLine("TSA_WD_SettlementCaravan_LootLetterBody".Translate(
                factionName,
                missionWord,
                silverBudget.ToString("F0")).ToString());

            if (!result.HadCandidates)
            {
                sb.AppendLine();
                sb.AppendLine("TSA_WD_SettlementCaravan_LootLetterNoTowns".Translate(radius.ToString()).ToString());
            }
            else
            {
                sb.AppendLine();
                sb.AppendLine("TSA_WD_SettlementCaravan_LootLetterRadius".Translate(radius.ToString()).ToString());
                if (result.DetailLines != null && result.DetailLines.Count > 0)
                {
                    sb.AppendLine();
                    for (int i = 0; i < result.DetailLines.Count; i++)
                        sb.AppendLine(result.DetailLines[i]);
                }
                else if (!result.Applied)
                {
                    sb.AppendLine();
                    sb.AppendLine("TSA_WD_SettlementCaravan_LootLetterNoEffect".Translate().ToString());
                }
            }

            LookTargets look = new LookTargets(new GlobalTargetInfo(clashTile));
            Find.LetterStack.ReceiveLetter(
                "TSA_WD_SettlementCaravan_LootLetterLabel".Translate(),
                sb.ToString().TrimEnd(),
                LetterDefOf.ThreatBig,
                look);

            Find.World.GetComponent<WorldComponent_SpreadManager>()?.AddLog(
                new SpreadLogEntry(sb.ToString().TrimEnd()));
        }
    }
}
