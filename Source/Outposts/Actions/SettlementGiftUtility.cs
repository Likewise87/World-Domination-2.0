using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Player gift caravan to ally/neutral settlements: goods only, goodwill + investment on arrival.</summary>
    public static class SettlementGiftUtility
    {
        public const float MinGiftSilver = 1000f;
        public const float GiftMeterCapSilver = 20000f;

        public static bool CanShowGiftGizmo(Settlement settlement, out string disabledReason)
        {
            disabledReason = null;
            if (settlement == null || settlement.Destroyed || settlement.Faction == null || settlement.Faction.IsPlayer)
                return false;
            if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(settlement))
                return false;
            if (!SettlementBuyUtility.IsEligibleSellerRelation(settlement.Faction))
                return false;

            if (HasPendingGiftForSettlement(settlement))
            {
                disabledReason = "TSA_WD_GiftSettlement_Pending".Translate();
                return true;
            }

            if (!SettlementBuyUtility.HasPlayerPaymentOrigin(settlement.Tile, out disabledReason))
                return true;

            return true;
        }

        public static bool HasPendingGiftForSettlement(Settlement settlement)
        {
            if (settlement == null || Find.WorldObjects == null) return false;
            var all = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is WorldObject_Traveler_SettlementGift gift
                    && !gift.Destroyed
                    && gift.targetObject == settlement)
                    return true;
            }
            return false;
        }

        public static bool MeetsMinGift(float offeredSilver) =>
            SettlementBuyUtility.MeetsAsk(offeredSilver, MinGiftSilver);

        public static bool IsGiftStillValid(WorldObject_Traveler_SettlementGift gift) =>
            IsGiftStillValid(gift, out _);

        public static bool IsGiftStillValid(WorldObject_Traveler_SettlementGift gift, out SettlementGiftFailReason failReason)
        {
            failReason = SettlementGiftFailReason.None;
            if (gift == null)
            {
                failReason = SettlementGiftFailReason.SettlementGone;
                return false;
            }
            if (!(gift.targetObject is Settlement settlement) || settlement.Destroyed)
            {
                failReason = SettlementGiftFailReason.SettlementGone;
                return false;
            }
            if (gift.recipientFaction != null && settlement.Faction != gift.recipientFaction)
            {
                failReason = SettlementGiftFailReason.FactionChanged;
                return false;
            }
            if (!SettlementBuyUtility.IsEligibleSellerRelation(settlement.Faction))
            {
                failReason = SettlementGiftFailReason.BadRelation;
                return false;
            }
            return true;
        }

        public enum SettlementGiftFailReason
        {
            None,
            LostInTransit,
            SettlementGone,
            BadRelation,
            FactionChanged
        }

        public static void RefundPayment(WorldObject_Traveler_SettlementGift gift, SettlementGiftFailReason failReason = SettlementGiftFailReason.SettlementGone)
        {
            if (gift == null || gift.paymentRefunded) return;
            gift.paymentRefunded = true;
            bool hadGoods = gift.paymentItems != null && gift.paymentItems.Count > 0;
            SettlementBuyUtility.RefundItems(gift.originObject, gift.paymentItems);
            gift.paymentItems?.Clear();
            NotifyAborted(gift, failReason, refundedPayment: hadGoods);
        }

        /// <summary>
        /// Caravan destroyed en route. If <paramref name="looter"/> is set (hostile world clash), invest silver budget near the clash tile.
        /// </summary>
        public static void MarkPaymentLostInTransit(WorldObject_Traveler_SettlementGift gift, Faction looter = null)
        {
            if (gift == null || gift.paymentRefunded || gift.completed) return;
            gift.paymentRefunded = true;

            float budget = SettlementBuyUtility.MarketValueOf(gift.paymentItems);
            int clashTile = gift.Tile;
            gift.paymentItems?.Clear();

            if (looter != null && !looter.IsPlayer && budget > 0.01f)
            {
                SettlementCaravanLootUtility.AwardLootToFaction(looter, clashTile, budget, isGiftMission: true);
                return;
            }

            NotifyAborted(gift, SettlementGiftFailReason.LostInTransit, refundedPayment: false);
        }

        private static void NotifyAborted(WorldObject_Traveler_SettlementGift gift, SettlementGiftFailReason failReason, bool refundedPayment)
        {
            if (!(WorldDominationMod.settings?.notifySettlementBuyAborted ?? WorldDominationSettings.DefNotifySettlementBuyAborted))
                return;
            string label = "TSA_WD_GiftSettlement_AbortLetterLabel".Translate();
            string target = gift?.targetObject?.LabelCap ?? "?";
            string text = failReason switch
            {
                SettlementGiftFailReason.LostInTransit =>
                    "TSA_WD_GiftSettlement_AbortLetterTextLost".Translate(target),
                SettlementGiftFailReason.BadRelation =>
                    "TSA_WD_GiftSettlement_AbortLetterTextBadRelation".Translate(target),
                SettlementGiftFailReason.FactionChanged =>
                    "TSA_WD_GiftSettlement_AbortLetterTextFactionChanged".Translate(target),
                SettlementGiftFailReason.SettlementGone =>
                    "TSA_WD_GiftSettlement_AbortLetterTextSettlementGone".Translate(target),
                _ => refundedPayment
                    ? "TSA_WD_GiftSettlement_AbortLetterTextRefund".Translate(target)
                    : "TSA_WD_GiftSettlement_AbortLetterText".Translate(target)
            };
            Find.LetterStack.ReceiveLetter(label, text, LetterDefOf.NeutralEvent, gift?.targetObject);
        }

        public static bool TryLaunchGift(Settlement settlement, List<ThingDefCountClass> paymentItems, out string reason)
        {
            reason = null;
            if (settlement == null || settlement.Destroyed)
            {
                reason = "TSA_WD_GiftSettlement_TargetGone".Translate();
                return false;
            }
            if (!SettlementBuyUtility.IsEligibleSellerRelation(settlement.Faction))
            {
                reason = "TSA_WD_GiftSettlement_BadRelation".Translate();
                return false;
            }
            if (HasPendingGiftForSettlement(settlement))
            {
                reason = "TSA_WD_GiftSettlement_Pending".Translate();
                return false;
            }

            float goodsMv = SettlementBuyUtility.MarketValueOf(paymentItems);
            if (!MeetsMinGift(goodsMv))
            {
                reason = "TSA_WD_GiftSettlement_UnderMin".Translate(MinGiftSilver.ToString("F0"));
                return false;
            }

            var paymentCopy = new List<ThingDefCountClass>();
            if (paymentItems != null)
            {
                for (int i = 0; i < paymentItems.Count; i++)
                {
                    var tc = paymentItems[i];
                    if (tc?.thingDef == null || tc.count <= 0) continue;
                    paymentCopy.Add(SettlementBuyUtility.CloneStockRow(tc, tc.count));
                }
            }

            if (!SettlementBuyUtility.DeductPaymentItems(settlement.Tile, paymentCopy, out reason, out var contributed))
                return false;

            Map colonyMap = Find.AnyPlayerHomeMap;
            var warehouses = SettlementBuyUtility.GetContributingWarehouses(settlement.Tile);
            WorldObject origin = SettlementBuyUtility.ResolveBuyOrigin(settlement.Tile, colonyMap, warehouses, contributed);
            if (origin == null)
            {
                reason = "TSA_WD_GiftSettlement_NoOrigin".Translate();
                SettlementBuyUtility.RefundItems(colonyMap?.Parent, paymentCopy);
                return false;
            }
            if (origin.Tile == settlement.Tile)
            {
                reason = "TSA_WD_GiftSettlement_SameTile".Translate();
                SettlementBuyUtility.RefundItems(origin, paymentCopy);
                return false;
            }

            if (!WorldActions_Traveler.SpawnSettlementGiftTraveler(settlement, origin, paymentCopy, settlement.Faction))
            {
                reason = "TSA_WD_GiftSettlement_SpawnFailed".Translate();
                SettlementBuyUtility.RefundItems(origin, paymentCopy);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Vanilla-style goodwill from gift goods (does not call GiveGift; avoids double investment).
        /// Uses raw market value / 40 (same as FactionGiftUtility). Does not apply SellPriceFactor;
        /// that factor is for trader sell / WD payment valuation only.
        /// </summary>
        public static int ApplyVanillaGiftGoodwill(Settlement settlement, List<ThingDefCountClass> items)
        {
            if (settlement?.Faction == null || items == null || items.Count == 0) return 0;

            float marketValue = 0f;
            for (int i = 0; i < items.Count; i++)
            {
                var tc = items[i];
                if (tc?.thingDef == null || tc.count <= 0) continue;
                Thing probe = SettlementBuyUtility.CreatePaymentProbe(tc);
                if (probe == null)
                {
                    marketValue += tc.thingDef.BaseMarketValue * tc.count;
                    continue;
                }
                probe.stackCount = Mathf.Max(1, tc.count);
                marketValue += probe.GetStatValue(StatDefOf.MarketValue) * probe.stackCount;
                if (!probe.Destroyed)
                    probe.Destroy(DestroyMode.Vanish);
            }

            // Vanilla gift goodwill: roughly 1 point per 40 silver of gifted market value.
            int change = Mathf.RoundToInt(marketValue / 40f);
            if (change == 0) return 0;
            settlement.Faction.TryAffectGoodwillWith(
                Faction.OfPlayer,
                change,
                canSendMessage: true,
                canSendHostilityLetter: true,
                reason: HistoryEventDefOf.GaveGift);
            return change;
        }
    }
}
