using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// When the player executes a trade (silver currency mode), optionally grant goodwill with the traded faction
    /// based on the total market value of the deal—including goods-for-goods. Value is in silver equivalent
    /// (configurable goodwill per 1000 silver). Favor-based trades are excluded.
    /// </summary>
    [HarmonyPatch(typeof(TradeDeal))]
    [HarmonyPatch(nameof(TradeDeal.TryExecute))]
    public static class Patch_GoodwillFromTrade
    {
        [HarmonyPrefix]
        public static void Prefix(ref List<Tradeable> ___tradeables, ref object[] __state)
        {
            __state = new object[] { 0f, null, false };

            if (!WorldDominationMod.settings.goodwillFromTradeEnabled) return;
            if (TradeSession.giftMode)
            {
                __state[2] = true;
                return;
            }
            if (TradeSession.TradeCurrency == TradeCurrency.Favor)
            {
                __state[2] = true;
                return;
            }
            if (TradeSession.TradeCurrency != TradeCurrency.Silver) return;

            Faction otherFaction = null;
            if (TradeSession.trader is Pawn pawn)
                otherFaction = pawn.Faction;
            else if (TradeSession.trader is Thing thing)
                otherFaction = thing.Faction;

            if (otherFaction == null || otherFaction == Faction.OfPlayer)
            {
                __state[2] = true;
                return;
            }

            __state[1] = otherFaction;

            float totalSilverEquivalent = 0f;
            foreach (Tradeable tradeable in ___tradeables)
            {
                if (tradeable.ThingDef.defName == "Silver") continue;
                if (tradeable.ActionToDo == TradeAction.None) continue;

                float valuePerUnit = tradeable.AnyThing.MarketValue;
                float count = Math.Abs(tradeable.CountToTransfer);
                totalSilverEquivalent += valuePerUnit * count;
            }

            __state[0] = totalSilverEquivalent;
        }

        [HarmonyPostfix]
        public static void Postfix(object[] __state, ref bool __result)
        {
            if (!__result) return;
            if (!WorldDominationMod.settings.goodwillFromTradeEnabled) return;
            if (__state[2] is bool skip && skip) return;

            float totalSilver = (float)__state[0];
            if (totalSilver <= 0f) return;

            Faction otherFaction = __state[1] as Faction;
            if (otherFaction == null || otherFaction == Faction.OfPlayer) return;

            float goodwillPer1000 = WorldDominationMod.settings.goodwillFromTradePer1000Silver;
            int goodwillChange = (int)Math.Round((totalSilver / 1000f) * goodwillPer1000);
            if (goodwillChange <= 0) return;

            // Trade-only: no HistoryEventDef passed, so conquest block never applies here.
            GoodwillChangeNotifier.NotifyTrade(otherFaction, goodwillChange);
        }
    }
}
