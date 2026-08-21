using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Bribe hostile WD settlements (faction ceasefire for new raids) or ground raid caravans (dissolve that raid).
    /// Payment uses colony/warehouse goods; success applies partial investment + half-gift goodwill.
    /// </summary>
    public static class SettlementBribeUtility
    {
        public enum BribeDurationPackage
        {
            Short = 0,
            Medium = 1,
            Long = 2
        }

        public enum BribeFailReason
        {
            None,
            LostInTransit,
            TargetGone,
            BadRelation,
            Invalid
        }

        public static int GetCeasefireDays(BribeDurationPackage package)
        {
            var s = WorldDominationMod.settings;
            return package switch
            {
                BribeDurationPackage.Medium => s?.bribeCeasefireDaysMedium ?? WorldDominationSettings.DefBribeCeasefireDaysMedium,
                BribeDurationPackage.Long => s?.bribeCeasefireDaysLong ?? WorldDominationSettings.DefBribeCeasefireDaysLong,
                _ => s?.bribeCeasefireDaysShort ?? WorldDominationSettings.DefBribeCeasefireDaysShort
            };
        }

        public static float GetDurationPriceMult(BribeDurationPackage package)
        {
            var s = WorldDominationMod.settings;
            int days = GetCeasefireDays(package);
            int shortDays = Mathf.Max(1, s?.bribeCeasefireDaysShort ?? WorldDominationSettings.DefBribeCeasefireDaysShort);
            float linear = days / (float)shortDays;
            float discount = package switch
            {
                BribeDurationPackage.Medium => s?.bribeCeasefireDiscountMedium ?? WorldDominationSettings.DefBribeCeasefireDiscountMedium,
                BribeDurationPackage.Long => s?.bribeCeasefireDiscountLong ?? WorldDominationSettings.DefBribeCeasefireDiscountLong,
                _ => 0f
            };
            return linear * (1f - Mathf.Clamp01(discount));
        }

        public static float GetSettlementSilverPerStrength()
        {
            var s = WorldDominationMod.settings;
            return Mathf.Max(0.01f, s?.bribeSettlementSilverPerStrength ?? WorldDominationSettings.DefBribeSettlementSilverPerStrength);
        }

        public static float GetCaravanSilverPerStrength()
        {
            var s = WorldDominationMod.settings;
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (s == null || !s.enableLateGameScaling || manager == null)
                return Mathf.Max(0.01f, s?.bribeCaravanSilverPerStrengthEarly ?? WorldDominationSettings.DefBribeCaravanSilverPerStrengthEarly);

            return manager.cachedEscalationStage switch
            {
                WdEscalationStage.Late => Mathf.Max(0.01f, s.bribeCaravanSilverPerStrengthLate),
                WdEscalationStage.Mid => Mathf.Max(0.01f, s.bribeCaravanSilverPerStrengthMid),
                _ => Mathf.Max(0.01f, s.bribeCaravanSilverPerStrengthEarly)
            };
        }

        public static float GetBribeInvestmentFraction()
        {
            var s = WorldDominationMod.settings;
            return Mathf.Clamp01(s?.bribeInvestmentFraction ?? WorldDominationSettings.DefBribeInvestmentFraction);
        }

        public static int GetBribeCaravanInvestmentRadius()
        {
            var s = WorldDominationMod.settings;
            return Mathf.Max(0, s?.bribeCaravanInvestmentRadiusTiles ?? WorldDominationSettings.DefBribeCaravanInvestmentRadiusTiles);
        }

        public static float GetGoodwillDivisor()
        {
            var s = WorldDominationMod.settings;
            return Mathf.Max(1f, s?.bribeGoodwillDivisor ?? WorldDominationSettings.DefBribeGoodwillDivisor);
        }

        public static void ComputeFactionStrengthParts(Faction faction, out float threatInRange, out float globalStrength)
        {
            threatInRange = 0f;
            globalStrength = 0f;
            if (faction == null || faction.IsPlayer || Find.WorldObjects?.Settlements == null || Find.WorldGrid == null)
                return;

            var seth = WorldDominationMod.settings;
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            var playerAssets = CollectPlayerThreatTargets();
            var settlements = Find.WorldObjects.Settlements;
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement town = settlements[i];
                if (town == null || town.Destroyed || town.Faction != faction) continue;
                if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(town)) continue;
                var comp = town.GetComponent<CompViralSpread>();
                if (comp == null) continue;

                float off = Mathf.Max(0f, comp.offensiveStrength);
                globalStrength += off;

                if (seth == null || playerAssets.Count == 0) continue;
                float range = SettlementAttackRangeUtil.GetNpcSettlementAttackRangeWithZeal(town, seth, manager);
                if (CanThreatenAnyPlayerAsset(town.Tile, range, playerAssets))
                    threatInRange += off;
            }
        }

        public static float GetWeightedFactionStrength(Faction faction)
        {
            ComputeFactionStrengthParts(faction, out float threat, out float global);
            return 0.7f * threat + 0.3f * global;
        }

        public static float GetSettlementBribeAsk(Faction faction, BribeDurationPackage package)
        {
            float weighted = GetWeightedFactionStrength(faction);
            return SettlementBuyUtility.RoundSilver(weighted * GetSettlementSilverPerStrength() * GetDurationPriceMult(package));
        }

        public static float GetRaidBribeAsk(WorldObject_Traveler raid)
        {
            if (raid == null) return 0f;
            float current = Mathf.Max(0f, raid.travelerStrength);
            float launch = Mathf.Max(current, raid.initialStrength);
            float floorFrac = WorldDominationMod.settings?.bribeRaidAskFloorFraction
                ?? WorldDominationSettings.DefBribeRaidAskFloorFraction;
            float effective = Mathf.Max(current, Mathf.Max(0f, floorFrac) * launch);
            return SettlementBuyUtility.RoundSilver(effective * GetCaravanSilverPerStrength());
        }

        public static int ExpectedGoodwillFromOffer(float paymentMarketValue)
            => Mathf.RoundToInt(Mathf.Max(0f, paymentMarketValue) / GetGoodwillDivisor());

        public static float ExpectedInvestFromOffer(float paymentMarketValue)
            => SettlementBuyUtility.RoundSilver(Mathf.Max(0f, paymentMarketValue) * GetBribeInvestmentFraction());

        public static bool CanShowSettlementBribeGizmo(Settlement settlement, out string disabledReason)
        {
            disabledReason = null;
            var s = WorldDominationMod.settings;
            if (s != null && !s.enableFactionBribe)
            {
                disabledReason = "TSA_WD_Bribe_Disabled".Translate();
                return false;
            }
            if (settlement == null || settlement.Destroyed || settlement.Faction == null || settlement.Faction.IsPlayer)
                return false;
            if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(settlement))
                return false;
            if (settlement.GetComponent<CompViralSpread>() == null)
                return false;

            Faction player = Faction.OfPlayerSilentFail;
            if (player == null || !WorldActions_Utils.SafeHostileTo(settlement.Faction, player))
                return false;

            if (HasPendingSettlementBribe(settlement))
            {
                disabledReason = "TSA_WD_Bribe_Pending".Translate();
                return true;
            }
            if (!SettlementBuyUtility.HasPlayerPaymentOrigin(settlement.Tile, out disabledReason))
                return true;
            return true;
        }

        public static bool CanShowRaidBribeGizmo(WorldObject_Traveler raid, out string disabledReason)
        {
            disabledReason = null;
            var s = WorldDominationMod.settings;
            if (s != null && !s.enableFactionBribe)
            {
                disabledReason = "TSA_WD_Bribe_Disabled".Translate();
                return false;
            }
            if (raid == null || raid.Destroyed || raid.Faction == null || raid.Faction.IsPlayer)
                return false;
            if (raid.mission != TravelerMission.Raid)
                return false;
            if (!raid.IsTargetingPlayer)
                return false;

            Faction player = Faction.OfPlayerSilentFail;
            if (player == null || !WorldActions_Utils.SafeHostileTo(raid.Faction, player))
                return false;

            if (HasPendingRaidBribe(raid))
            {
                disabledReason = "TSA_WD_Bribe_Pending".Translate();
                return true;
            }
            if (!SettlementBuyUtility.HasPlayerPaymentOrigin(raid.Tile, out disabledReason))
                return true;
            return true;
        }

        public static bool HasPendingSettlementBribe(Settlement settlement)
        {
            if (settlement == null || Find.WorldObjects == null) return false;
            var all = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is WorldObject_Traveler_SettlementBribe bribe
                    && !bribe.Destroyed
                    && bribe.bribeKind == WorldObject_Traveler_SettlementBribe.BribeKind.Settlement
                    && bribe.targetObject == settlement)
                    return true;
            }
            return false;
        }

        public static bool HasPendingRaidBribe(WorldObject_Traveler raid)
        {
            if (raid == null || Find.WorldObjects == null) return false;
            var all = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is WorldObject_Traveler_SettlementBribe bribe
                    && !bribe.Destroyed
                    && bribe.bribeKind == WorldObject_Traveler_SettlementBribe.BribeKind.Raid
                    && bribe.targetObject == raid)
                    return true;
            }
            return false;
        }

        public static bool IsBribeStillValid(WorldObject_Traveler_SettlementBribe bribe) =>
            IsBribeStillValid(bribe, out _);

        public static bool IsBribeStillValid(WorldObject_Traveler_SettlementBribe bribe, out BribeFailReason failReason)
        {
            failReason = BribeFailReason.None;
            if (bribe == null)
            {
                failReason = BribeFailReason.TargetGone;
                return false;
            }

            Faction player = Faction.OfPlayerSilentFail;
            if (bribe.bribeKind == WorldObject_Traveler_SettlementBribe.BribeKind.Settlement)
            {
                if (!(bribe.targetObject is Settlement settlement) || settlement.Destroyed)
                {
                    failReason = BribeFailReason.TargetGone;
                    return false;
                }
                if (bribe.targetFaction != null && settlement.Faction != bribe.targetFaction)
                {
                    failReason = BribeFailReason.BadRelation;
                    return false;
                }
                if (player == null || settlement.Faction == null || !WorldActions_Utils.SafeHostileTo(settlement.Faction, player))
                {
                    failReason = BribeFailReason.BadRelation;
                    return false;
                }
                return true;
            }

            if (!(bribe.targetObject is WorldObject_Traveler raid) || raid.Destroyed)
            {
                failReason = BribeFailReason.TargetGone;
                return false;
            }
            if (raid.mission != TravelerMission.Raid || !raid.IsTargetingPlayer)
            {
                failReason = BribeFailReason.Invalid;
                return false;
            }
            if (bribe.targetFaction != null && raid.Faction != bribe.targetFaction)
            {
                failReason = BribeFailReason.BadRelation;
                return false;
            }
            if (player == null || raid.Faction == null || !WorldActions_Utils.SafeHostileTo(raid.Faction, player))
            {
                failReason = BribeFailReason.BadRelation;
                return false;
            }
            return true;
        }

        public static bool TryLaunchSettlementBribe(
            Settlement settlement,
            List<ThingDefCountClass> paymentItems,
            BribeDurationPackage package,
            out string reason)
        {
            reason = null;
            if (settlement == null || settlement.Destroyed)
            {
                reason = "TSA_WD_Bribe_TargetGone".Translate();
                return false;
            }
            var s = WorldDominationMod.settings;
            if (s != null && !s.enableFactionBribe)
            {
                reason = "TSA_WD_Bribe_Disabled".Translate();
                return false;
            }
            Faction player = Faction.OfPlayerSilentFail;
            if (player == null || settlement.Faction == null || !WorldActions_Utils.SafeHostileTo(settlement.Faction, player))
            {
                reason = "TSA_WD_Bribe_BadRelation".Translate();
                return false;
            }
            if (HasPendingSettlementBribe(settlement))
            {
                reason = "TSA_WD_Bribe_Pending".Translate();
                return false;
            }
            if (!SettlementBuyUtility.HasPlayerPaymentOrigin(settlement.Tile, out reason))
                return false;

            float ask = GetSettlementBribeAsk(settlement.Faction, package);
            float goodsMv = SettlementBuyUtility.MarketValueOf(paymentItems);
            if (!SettlementBuyUtility.MeetsAsk(goodsMv, ask))
            {
                reason = "TSA_WD_Bribe_UnderAsk".Translate(ask.ToString("F0"));
                return false;
            }

            var paymentCopy = ClonePayments(paymentItems);
            if (!SettlementBuyUtility.DeductPaymentItems(settlement.Tile, paymentCopy, out reason, out var contributed))
                return false;

            Map colonyMap = Find.AnyPlayerHomeMap;
            var warehouses = SettlementBuyUtility.GetContributingWarehouses(settlement.Tile);
            WorldObject origin = SettlementBuyUtility.ResolveBuyOrigin(settlement.Tile, colonyMap, warehouses, contributed);
            if (origin == null || origin.Tile == settlement.Tile)
            {
                SettlementBuyUtility.RefundItems(colonyMap?.Parent, paymentCopy);
                reason = origin != null && origin.Tile == settlement.Tile
                    ? "TSA_WD_Bribe_SameTile".Translate()
                    : "TSA_WD_Bribe_NoOrigin".Translate();
                return false;
            }

            if (!WorldActions_Traveler.SpawnSettlementBribeTraveler(
                    settlement,
                    origin,
                    paymentCopy,
                    settlement.Faction,
                    GetCeasefireDays(package),
                    ask))
            {
                SettlementBuyUtility.RefundItems(origin, paymentCopy);
                reason = "TSA_WD_Bribe_SpawnFailed".Translate();
                return false;
            }
            return true;
        }

        public static bool TryLaunchRaidBribe(
            WorldObject_Traveler raid,
            List<ThingDefCountClass> paymentItems,
            out string reason)
        {
            reason = null;
            if (raid == null || raid.Destroyed)
            {
                reason = "TSA_WD_Bribe_TargetGone".Translate();
                return false;
            }
            var s = WorldDominationMod.settings;
            if (s != null && !s.enableFactionBribe)
            {
                reason = "TSA_WD_Bribe_Disabled".Translate();
                return false;
            }
            Faction player = Faction.OfPlayerSilentFail;
            if (player == null || raid.Faction == null || !WorldActions_Utils.SafeHostileTo(raid.Faction, player)
                || raid.mission != TravelerMission.Raid || !raid.IsTargetingPlayer)
            {
                reason = "TSA_WD_Bribe_Cannot".Translate();
                return false;
            }
            if (HasPendingRaidBribe(raid))
            {
                reason = "TSA_WD_Bribe_Pending".Translate();
                return false;
            }
            if (!SettlementBuyUtility.HasPlayerPaymentOrigin(raid.Tile, out reason))
                return false;

            float ask = GetRaidBribeAsk(raid);
            float goodsMv = SettlementBuyUtility.MarketValueOf(paymentItems);
            if (!SettlementBuyUtility.MeetsAsk(goodsMv, ask))
            {
                reason = "TSA_WD_Bribe_UnderAsk".Translate(ask.ToString("F0"));
                return false;
            }

            var paymentCopy = ClonePayments(paymentItems);
            if (!SettlementBuyUtility.DeductPaymentItems(raid.Tile, paymentCopy, out reason, out var contributed))
                return false;

            Map colonyMap = Find.AnyPlayerHomeMap;
            var warehouses = SettlementBuyUtility.GetContributingWarehouses(raid.Tile);
            WorldObject origin = SettlementBuyUtility.ResolveBuyOrigin(raid.Tile, colonyMap, warehouses, contributed);
            if (origin == null)
            {
                SettlementBuyUtility.RefundItems(colonyMap?.Parent, paymentCopy);
                reason = "TSA_WD_Bribe_NoOrigin".Translate();
                return false;
            }

            if (!WorldActions_Traveler.SpawnRaidBribeTraveler(raid, origin, paymentCopy, raid.Faction, ask))
            {
                SettlementBuyUtility.RefundItems(origin, paymentCopy);
                reason = "TSA_WD_Bribe_SpawnFailed".Translate();
                return false;
            }
            return true;
        }

        public static void RefundPayment(WorldObject_Traveler_SettlementBribe bribe, BribeFailReason failReason = BribeFailReason.TargetGone)
        {
            if (bribe == null || bribe.paymentRefunded) return;
            bribe.paymentRefunded = true;
            bool hadGoods = bribe.paymentItems != null && bribe.paymentItems.Count > 0;
            SettlementBuyUtility.RefundItems(bribe.originObject, bribe.paymentItems);
            bribe.paymentItems?.Clear();
            NotifyAborted(bribe, failReason, refundedPayment: hadGoods);
        }

        public static void MarkPaymentLostInTransit(WorldObject_Traveler_SettlementBribe bribe, Faction looter = null)
        {
            if (bribe == null || bribe.paymentRefunded || bribe.completed) return;
            bribe.paymentRefunded = true;

            float budget = SettlementBuyUtility.MarketValueOf(bribe.paymentItems);
            int clashTile = bribe.Tile;
            bribe.paymentItems?.Clear();

            if (looter != null && !looter.IsPlayer && budget > 0.01f)
            {
                SettlementCaravanLootUtility.AwardLootToFaction(looter, clashTile, budget, isGiftMission: true);
                return;
            }

            NotifyAborted(bribe, BribeFailReason.LostInTransit, refundedPayment: false);
        }

        public static void ExecuteSettlementBribeArrival(WorldObject_Traveler_SettlementBribe bribe)
        {
            if (bribe == null) return;
            if (!IsBribeStillValid(bribe, out var failReason))
            {
                RefundPayment(bribe, failReason);
                return;
            }

            var settlement = bribe.targetObject as Settlement;
            if (settlement == null) return;

            bribe.completed = true;
            bribe.paymentRefunded = true;
            float silverBudget = SettlementBuyUtility.MarketValueOf(bribe.paymentItems);
            var itemsCopy = ClonePayments(bribe.paymentItems);
            bribe.paymentItems?.Clear();

            float invest = ExpectedInvestFromOffer(silverBudget);
            if (invest > 0.01f)
            {
                FactionSettlementInvestment.AwardFromSilverBudget(
                    settlement.Faction,
                    settlement.Tile,
                    invest,
                    preferFirst: settlement,
                    notify: FactionSettlementInvestment.NotifyKind.Bribe);
            }

            ApplyBribeGoodwill(settlement.Faction, itemsCopy);
            int days = Mathf.Max(1, bribe.ceasefireDays);
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            manager?.SetPlayerBribeCeasefire(settlement.Faction, days);

            if (WorldDominationMod.settings?.notifyBribeSettlementCompleted ?? WorldDominationSettings.DefNotifyBribeSettlementCompleted)
            {
                Find.LetterStack.ReceiveLetter(
                    "TSA_WD_Bribe_SettlementCompletedLabel".Translate(),
                    "TSA_WD_Bribe_SettlementCompletedText".Translate(
                        settlement.LabelCap,
                        settlement.Faction?.Name ?? "?",
                        days.ToString(),
                        invest.ToString("F0")),
                    LetterDefOf.PositiveEvent,
                    settlement);
            }

            Find.World?.GetComponent<WorldComponent_SpreadManager>()?.AddLog(new SpreadLogEntry(
                "TSA_WD_Log_BribeSettlementCompleted".Translate(settlement.LabelCap, settlement.Faction?.Name ?? "?", days.ToString()),
                settlement));
        }

        public static void ExecuteRaidBribeArrival(WorldObject_Traveler_SettlementBribe bribe)
        {
            if (bribe == null) return;
            if (!IsBribeStillValid(bribe, out var failReason))
            {
                RefundPayment(bribe, failReason);
                return;
            }

            var raid = bribe.targetObject as WorldObject_Traveler;
            if (raid == null || raid.Destroyed) return;

            bribe.completed = true;
            bribe.paymentRefunded = true;
            float silverBudget = SettlementBuyUtility.MarketValueOf(bribe.paymentItems);
            var itemsCopy = ClonePayments(bribe.paymentItems);
            bribe.paymentItems?.Clear();

            Faction raidFaction = raid.Faction ?? bribe.targetFaction;
            int investTile = bribe.Tile;
            Settlement prefer = null;
            if (raid.originObject is Settlement originSettlement && !originSettlement.Destroyed)
            {
                investTile = originSettlement.Tile;
                prefer = originSettlement;
            }
            else if (TravelerEndpointUtility.IsLiveEndpoint(raid.originObject))
            {
                investTile = raid.originObject.Tile;
            }

            float invest = ExpectedInvestFromOffer(silverBudget);
            if (invest > 0.01f && raidFaction != null)
            {
                FactionSettlementInvestment.AwardFromSilverBudget(
                    raidFaction,
                    investTile,
                    invest,
                    preferFirst: prefer,
                    notify: FactionSettlementInvestment.NotifyKind.Bribe,
                    radiusOverride: GetBribeCaravanInvestmentRadius());
            }

            ApplyBribeGoodwill(raidFaction, itemsCopy);
            string raidLabel = raid.LabelCap;
            SafeDissolveRaid(raid);

            if (WorldDominationMod.settings?.notifyBribeRaidCompleted ?? WorldDominationSettings.DefNotifyBribeRaidCompleted)
            {
                Find.LetterStack.ReceiveLetter(
                    "TSA_WD_Bribe_RaidCompletedLabel".Translate(),
                    "TSA_WD_Bribe_RaidCompletedText".Translate(
                        raidLabel,
                        raidFaction?.Name ?? "?",
                        invest.ToString("F0")),
                    LetterDefOf.PositiveEvent,
                    bribe.originObject);
            }

            Find.World?.GetComponent<WorldComponent_SpreadManager>()?.AddLog(new SpreadLogEntry(
                "TSA_WD_Log_BribeRaidCompleted".Translate(raidLabel, raidFaction?.Name ?? "?"),
                bribe.originObject));
        }

        private static void SafeDissolveRaid(WorldObject_Traveler raid)
        {
            if (raid == null || raid.Destroyed) return;
            // Do not refund raid strength to origin; the bribe bought the stand-down.
            raid.travelerStrength = 0f;
            raid.suppressDestroyedWorldFx = true;
            raid.pather?.StopDead();
            if (WorldObject_Traveler.IsRaidMission(raid.mission))
                Raid_Simulated.RefundAlliedRaidOrderGoodwill(raid);
            if (!raid.Destroyed)
                raid.Destroy();
        }

        private static int ApplyBribeGoodwill(Faction faction, List<ThingDefCountClass> items)
        {
            if (faction == null || items == null || items.Count == 0) return 0;
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

            int change = Mathf.RoundToInt(marketValue / GetGoodwillDivisor());
            if (change == 0) return 0;
            faction.TryAffectGoodwillWith(
                Faction.OfPlayer,
                change,
                canSendMessage: true,
                canSendHostilityLetter: true,
                reason: HistoryEventDefOf.GaveGift);
            return change;
        }

        private static void NotifyAborted(WorldObject_Traveler_SettlementBribe bribe, BribeFailReason failReason, bool refundedPayment)
        {
            bool isRaid = bribe?.bribeKind == WorldObject_Traveler_SettlementBribe.BribeKind.Raid;
            if (failReason == BribeFailReason.LostInTransit)
            {
                if (!(WorldDominationMod.settings?.notifyBribeLostInTransit ?? WorldDominationSettings.DefNotifyBribeLostInTransit))
                    return;
            }
            else if (isRaid)
            {
                if (!(WorldDominationMod.settings?.notifyBribeRaidAborted ?? WorldDominationSettings.DefNotifyBribeRaidAborted))
                    return;
            }
            else if (!(WorldDominationMod.settings?.notifyBribeSettlementAborted ?? WorldDominationSettings.DefNotifyBribeSettlementAborted))
                return;

            string label = isRaid
                ? "TSA_WD_Bribe_RaidAbortLabel".Translate()
                : "TSA_WD_Bribe_SettlementAbortLabel".Translate();
            string target = bribe?.targetObject?.LabelCap ?? "?";
            string text = failReason switch
            {
                BribeFailReason.LostInTransit => "TSA_WD_Bribe_AbortTextLost".Translate(target),
                BribeFailReason.BadRelation => "TSA_WD_Bribe_AbortTextBadRelation".Translate(target),
                BribeFailReason.TargetGone => "TSA_WD_Bribe_AbortTextTargetGone".Translate(target),
                _ => refundedPayment
                    ? "TSA_WD_Bribe_AbortTextRefund".Translate(target)
                    : "TSA_WD_Bribe_AbortText".Translate(target)
            };
            Find.LetterStack.ReceiveLetter(label, text, LetterDefOf.NeutralEvent, bribe?.targetObject ?? bribe?.originObject);
        }

        private static List<ThingDefCountClass> ClonePayments(List<ThingDefCountClass> paymentItems)
        {
            var paymentCopy = new List<ThingDefCountClass>();
            if (paymentItems == null) return paymentCopy;
            for (int i = 0; i < paymentItems.Count; i++)
            {
                var tc = paymentItems[i];
                if (tc?.thingDef == null || tc.count <= 0) continue;
                paymentCopy.Add(SettlementBuyUtility.CloneStockRow(tc, tc.count));
            }
            return paymentCopy;
        }

        private static List<(int tile, float rangeNeeded)> CollectPlayerThreatTargets()
        {
            var list = new List<(int tile, float rangeNeeded)>();
            if (Find.WorldObjects == null) return list;

            Map home = Find.AnyPlayerHomeMap;
            if (home?.Parent != null && PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(home.Parent))
                list.Add((home.Parent.Tile, 0f));

            var all = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is WorldObject_WD_Outpost outpost
                    && !outpost.Destroyed
                    && outpost.Faction != null
                    && outpost.Faction.IsPlayer
                    && PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(outpost))
                {
                    list.Add((outpost.Tile, 0f));
                }
            }
            return list;
        }

        private static bool CanThreatenAnyPlayerAsset(int settlementTile, float attackRange, List<(int tile, float rangeNeeded)> assets)
        {
            if (attackRange <= 0f || assets == null || Find.WorldGrid == null) return false;
            for (int i = 0; i < assets.Count; i++)
            {
                float dist = Find.WorldGrid.ApproxDistanceInTiles(settlementTile, assets[i].tile);
                if (dist <= attackRange + 0.01f)
                    return true;
            }
            return false;
        }
    }
}
