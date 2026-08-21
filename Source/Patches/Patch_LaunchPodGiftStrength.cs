using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Launch-pod gifts: convert market value into faction settlement investment (prefer gifted town).</summary>
    [HarmonyPatch(typeof(FactionGiftUtility), nameof(FactionGiftUtility.GiveGift), new[] { typeof(List<ActiveTransporterInfo>), typeof(Settlement) })]
    public static class Patch_LaunchPodGiftStrength
    {
        [HarmonyPrefix]
        public static void Prefix(List<ActiveTransporterInfo> pods, Settlement giveTo, ref float __state)
        {
            __state = giveTo == null ? 0f : FactionSettlementInvestment.SumPodMarketValue(pods);
        }

        [HarmonyPostfix]
        public static void Postfix(Settlement giveTo, float __state)
        {
            if (giveTo == null || __state <= 0f) return;
            FactionSettlementInvestment.AwardFromSilverBudget(
                giveTo.Faction,
                giveTo.Tile,
                __state,
                preferFirst: giveTo,
                notify: FactionSettlementInvestment.NotifyKind.Gift);
        }
    }

    /// <summary>Map/world tradeable gifts (Dialog_Trade gift mode / Offer gifts): invest silver-equivalent.</summary>
    [HarmonyPatch(typeof(FactionGiftUtility), nameof(FactionGiftUtility.GiveGift),
        new[] { typeof(List<Tradeable>), typeof(Faction), typeof(GlobalTargetInfo) })]
    public static class Patch_TradeableGiftInvestment
    {
        [HarmonyPrefix]
        public static void Prefix(List<Tradeable> tradeables, Faction giveTo, ref float __state)
        {
            __state = 0f;
            if (giveTo == null || giveTo.IsPlayer) return;
            __state = FactionSettlementInvestment.SumTradeableMarketValue(tradeables);
        }

        [HarmonyPostfix]
        public static void Postfix(Faction giveTo, GlobalTargetInfo lookTarget, float __state)
        {
            if (giveTo == null || giveTo.IsPlayer || __state <= 0f) return;

            Settlement prefer = null;
            int originTile = -1;

            if (TradeSession.trader is Settlement settlementTrader)
            {
                prefer = settlementTrader;
                originTile = settlementTrader.Tile;
            }
            else if (lookTarget.HasWorldObject && lookTarget.WorldObject is Settlement lookSettlement)
            {
                prefer = lookSettlement;
                originTile = lookSettlement.Tile;
            }
            else if (lookTarget.IsValid)
            {
                originTile = lookTarget.Tile;
            }
            else if (TradeSession.playerNegotiator?.Map != null)
            {
                originTile = TradeSession.playerNegotiator.Map.Tile;
            }
            else if (Find.CurrentMap != null)
            {
                originTile = Find.CurrentMap.Tile;
            }

            if (originTile < 0) return;

            int radius = WorldDominationMod.settings?.factionInvestmentRadiusTiles
                ?? WorldDominationSettings.DefFactionInvestmentRadiusTiles;
            if (prefer == null)
                prefer = FactionSettlementInvestment.FindNearestFactionSettlement(giveTo, originTile, radius);

            FactionSettlementInvestment.AwardFromSilverBudget(
                giveTo,
                originTile,
                __state,
                preferFirst: prefer,
                notify: FactionSettlementInvestment.NotifyKind.Gift);
        }
    }
}
