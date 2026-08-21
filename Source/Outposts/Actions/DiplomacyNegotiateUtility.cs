using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public enum DiplomacyNegotiateAction : byte
    {
        DeclareWar = 0,
        BecomeNeutral = 1,
        BecomeAlly = 2
    }

    /// <summary>
    /// Pay an Ally/Neutral NPC faction (goods caravan) to declare war, cease fire, or form an alliance
    /// with another NPC faction. Existing alliances cannot be broken.
    /// </summary>
    public static class DiplomacyNegotiateUtility
    {
        public const int GoodwillFloor = SettlementBuyUtility.GoodwillFloor;
        public const float DefAskMinSilver = 8000f;
        public const float DefAskMaxSilver = 40000f;
        public const float WarRejectRatio = 0.75f;
        public const float WarCheapRatio = 1.50f;
        public const float PeaceMinRatio = 1.25f;
        public const float PeaceMaxRatio = 2.00f;

        public static float AskMinSilver =>
            WorldDominationMod.settings?.negotiateAskMinSilver ?? DefAskMinSilver;

        public static float AskMaxSilver =>
            WorldDominationMod.settings?.negotiateAskMaxSilver ?? DefAskMaxSilver;

        public static bool IsFeatureEnabled =>
            WorldDominationMod.settings?.enableDiplomacyNegotiate ?? true;

        public static bool CanOpenNegotiate(Faction negotiator, out string disabledReason)
        {
            disabledReason = null;
            if (!IsFeatureEnabled)
            {
                disabledReason = "TSA_WD_Negotiate_Disabled".Translate();
                return false;
            }
            if (negotiator == null || negotiator.IsPlayer || WorldActions_Utils.IsExcludedFaction(negotiator))
            {
                disabledReason = "TSA_WD_Negotiate_BadPlayerRelation".Translate();
                return false;
            }
            if (!SettlementBuyUtility.IsEligibleSellerRelation(negotiator))
            {
                disabledReason = "TSA_WD_Negotiate_BadPlayerRelation".Translate();
                return false;
            }
            if (HasPendingNegotiateForFaction(negotiator))
            {
                disabledReason = "TSA_WD_Negotiate_Pending".Translate();
                return false;
            }
            if (!HasSurfaceWdSettlement(negotiator))
            {
                disabledReason = "TSA_WD_Negotiate_NoSettlement".Translate();
                return false;
            }
            return true;
        }

        /// <summary>Launch gate: overview may open with low goodwill, but confirm/launch still needs floor + payment origin.</summary>
        public static bool CanNegotiateWithFaction(Faction negotiator, out string disabledReason)
        {
            if (!CanOpenNegotiate(negotiator, out disabledReason))
                return false;
            if (GoodwillChangeNotifier.GetPlayerGoodwill(negotiator) < GoodwillFloor)
            {
                disabledReason = "TSA_WD_Negotiate_GoodwillFloor".Translate(GoodwillFloor);
                return false;
            }
            Settlement nearest = FindNearestSettlement(negotiator);
            if (nearest == null)
            {
                disabledReason = "TSA_WD_Negotiate_NoSettlement".Translate();
                return false;
            }
            if (!SettlementBuyUtility.HasPlayerPaymentOrigin(nearest.Tile, out disabledReason))
                return false;
            return true;
        }

        public static bool HasPendingNegotiateForFaction(Faction negotiator)
        {
            if (negotiator == null || Find.WorldObjects == null) return false;
            var all = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is WorldObject_Traveler_DiplomacyNegotiate n
                    && !n.Destroyed
                    && !n.completed
                    && n.negotiatorFaction == negotiator)
                    return true;
            }
            return false;
        }

        public static Settlement FindNearestSettlement(Faction faction, int fromTile = -1)
        {
            if (faction == null || Find.WorldObjects?.Settlements == null || Find.WorldGrid == null)
                return null;
            if (fromTile < 0)
            {
                Map home = Find.AnyPlayerHomeMap;
                fromTile = home?.Tile.tileId ?? -1;
            }

            Settlement best = null;
            int bestDist = int.MaxValue;
            var list = Find.WorldObjects.Settlements;
            for (int i = 0; i < list.Count; i++)
            {
                Settlement s = list[i];
                if (!IsNegotiatorSettlement(s, faction)) continue;
                int dist = fromTile >= 0
                    ? Find.WorldGrid.TraversalDistanceBetween(fromTile, s.Tile)
                    : 0;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = s;
                }
            }
            return best;
        }

        /// <summary>Cheap existence check for UI (no path distances).</summary>
        public static bool HasSurfaceWdSettlement(Faction faction)
        {
            if (faction == null || Find.WorldObjects?.Settlements == null) return false;
            var list = Find.WorldObjects.Settlements;
            for (int i = 0; i < list.Count; i++)
            {
                if (IsNegotiatorSettlement(list[i], faction))
                    return true;
            }
            return false;
        }

        private static bool IsNegotiatorSettlement(Settlement s, Faction faction) =>
            s != null
            && !s.Destroyed
            && s.Faction == faction
            && PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(s)
            && s.GetComponent<CompViralSpread>() != null;

        public static float GetFactionGlobalMilitaryPower(Faction faction)
        {
            float total = 0f;
            if (faction == null || Find.WorldObjects?.Settlements == null) return 0f;
            var list = Find.WorldObjects.Settlements;
            for (int i = 0; i < list.Count; i++)
            {
                Settlement s = list[i];
                if (s == null || s.Destroyed || s.Faction != faction) continue;
                if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(s)) continue;
                var comp = s.GetComponent<CompViralSpread>();
                if (comp == null) continue;
                total += comp.GetTotalLocalDefensePower();
            }
            return total;
        }

        public static float GetStrengthRatio(Faction negotiator, Faction target)
        {
            float n = GetFactionGlobalMilitaryPower(negotiator);
            float t = Mathf.Max(1f, GetFactionGlobalMilitaryPower(target));
            return n / t;
        }

        /// <summary>
        /// Combined military power of visible non-player, non-excluded peers the negotiator is already Hostile with.
        /// </summary>
        public static float GetWarEnemyPower(Faction negotiator)
        {
            if (negotiator == null || Find.FactionManager == null) return 0f;
            float total = 0f;
            foreach (Faction f in Find.FactionManager.AllFactionsVisible)
            {
                if (f == null || f == negotiator || f.IsPlayer) continue;
                if (WorldActions_Utils.IsExcludedFaction(f)) continue;
                if (WorldActions_Utils.SafeRelationKindWith(negotiator, f) != FactionRelationKind.Hostile)
                    continue;
                total += GetFactionGlobalMilitaryPower(f);
            }
            return total;
        }

        /// <summary>
        /// This faction's military power divided by combined power of visible peers it is already Hostile with.
        /// 0.2 means this faction is 20% of that combined war-foe strength. 0 when they have no war foes.
        /// </summary>
        public static float GetWarFoeRatio(Faction negotiator)
        {
            float e = GetWarEnemyPower(negotiator);
            if (e <= 0f) return 0f;
            return GetFactionGlobalMilitaryPower(negotiator) / e;
        }

        /// <summary>Declare War ask uses N/(T+E); values below the reject floor clamp to max ask via InverseLerp.</summary>
        public static float GetDeclareWarPricingRatio(Faction negotiator, Faction target, float warEnemyPower = -1f)
        {
            float n = GetFactionGlobalMilitaryPower(negotiator);
            float t = GetFactionGlobalMilitaryPower(target);
            float e = warEnemyPower >= 0f ? warEnemyPower : GetWarEnemyPower(negotiator);
            return n / Mathf.Max(1f, t + e);
        }

        public static FactionRelationKind DesiredKind(DiplomacyNegotiateAction action)
        {
            switch (action)
            {
                case DiplomacyNegotiateAction.DeclareWar:
                    return FactionRelationKind.Hostile;
                case DiplomacyNegotiateAction.BecomeAlly:
                    return FactionRelationKind.Ally;
                default:
                    return FactionRelationKind.Neutral;
            }
        }

        public static string ActionVerbLabel(DiplomacyNegotiateAction action)
        {
            switch (action)
            {
                case DiplomacyNegotiateAction.DeclareWar:
                    return "TSA_WD_Negotiate_ActionWar".Translate();
                case DiplomacyNegotiateAction.BecomeAlly:
                    return "TSA_WD_Negotiate_ActionAlly".Translate();
                default:
                    return "TSA_WD_Negotiate_ActionPeace".Translate();
            }
        }

        /// <summary>Whether this action is allowed for the pair (relation + strength + freeze + settings lock).</summary>
        /// <param name="warEnemyPower">
        /// Precomputed sum of hostile peer power for the negotiator, or negative to compute inside.
        /// </param>
        public static bool TryEvaluateAction(
            Faction negotiator,
            Faction target,
            DiplomacyNegotiateAction action,
            out float askSilver,
            out string rejectReason,
            float warEnemyPower = -1f)
        {
            askSilver = 0f;
            rejectReason = null;
            if (negotiator == null || target == null || negotiator == target)
            {
                rejectReason = "TSA_WD_Negotiate_InvalidPair".Translate();
                return false;
            }
            if (target.IsPlayer || WorldActions_Utils.IsExcludedFaction(target))
            {
                rejectReason = "TSA_WD_Negotiate_InvalidPair".Translate();
                return false;
            }

            var seth = WorldDominationMod.settings;
            if (seth != null && seth.IsPairLocked(negotiator, target))
            {
                rejectReason = "TSA_WD_Negotiate_PairLocked".Translate();
                return false;
            }

            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (WorldActions_DiplomacyBuffsNerfs.TryGetDiplomacyFreezeDaysRemaining(negotiator, target, manager, out float days)
                && days > 0.01f)
            {
                rejectReason = "TSA_WD_Negotiate_PairFrozen".Translate(days.ToString("F0"));
                return false;
            }

            FactionRelationKind kind = WorldActions_Utils.SafeRelationKindWith(negotiator, target);
            if (kind == FactionRelationKind.Ally)
            {
                rejectReason = "TSA_WD_Negotiate_AllyUntouchable".Translate();
                return false;
            }

            float rawRatio = GetStrengthRatio(negotiator, target);

            if (action == DiplomacyNegotiateAction.DeclareWar)
            {
                if (kind != FactionRelationKind.Neutral)
                {
                    rejectReason = "TSA_WD_Negotiate_WarNeedsNeutral".Translate();
                    return false;
                }
                if (rawRatio < WarRejectRatio)
                {
                    rejectReason = "TSA_WD_Negotiate_TooWeakForWar".Translate(
                        negotiator.Name,
                        target.Name,
                        Mathf.RoundToInt(WarRejectRatio * 100f));
                    return false;
                }

                // Overextension raises price (N/(T+E)); does not invent new rejects.
                float pricingRatio = GetDeclareWarPricingRatio(negotiator, target, warEnemyPower);
                askSilver = SettlementBuyUtility.RoundSilver(ComputeAskSilver(action, pricingRatio));
                return true;
            }

            if (action == DiplomacyNegotiateAction.BecomeAlly)
            {
                if (kind != FactionRelationKind.Neutral)
                {
                    rejectReason = "TSA_WD_Negotiate_AllyNeedsNeutral".Translate();
                    return false;
                }
                if (rawRatio < WarRejectRatio)
                {
                    rejectReason = "TSA_WD_Negotiate_TooWeakForAlly".Translate(
                        negotiator.Name,
                        target.Name,
                        Mathf.RoundToInt(WarRejectRatio * 100f));
                    return false;
                }

                askSilver = SettlementBuyUtility.RoundSilver(ComputeAskSilver(action, rawRatio));
                return true;
            }

            if (kind != FactionRelationKind.Hostile)
            {
                rejectReason = "TSA_WD_Negotiate_PeaceNeedsHostile".Translate();
                return false;
            }

            if (rawRatio < PeaceMinRatio)
            {
                rejectReason = "TSA_WD_Negotiate_TooWeakForPeace".Translate(
                    negotiator.Name,
                    target.Name,
                    Mathf.RoundToInt(PeaceMinRatio * 100f));
                return false;
            }

            askSilver = SettlementBuyUtility.RoundSilver(ComputeAskSilver(action, rawRatio));
            return true;
        }

        public static float ComputeAskSilver(DiplomacyNegotiateAction action, float ratio)
        {
            float min = AskMinSilver;
            float max = AskMaxSilver;
            if (action == DiplomacyNegotiateAction.DeclareWar
                || action == DiplomacyNegotiateAction.BecomeAlly)
            {
                // 0.75 → max, 1.50 → min. Ratio below 0.75 clamps to max ask.
                float t = Mathf.InverseLerp(WarRejectRatio, WarCheapRatio, ratio);
                return Mathf.Lerp(max, min, Mathf.Clamp01(t));
            }

            // Peace: 1.25 → min, 2.00 → max
            float tp = Mathf.InverseLerp(PeaceMinRatio, PeaceMaxRatio, ratio);
            return Mathf.Lerp(min, max, Mathf.Clamp01(tp));
        }

        public struct ActionOffer
        {
            public DiplomacyNegotiateAction Action;
            public float AskSilver;
            public string RejectReason;
            public bool CanAct;
        }

        public struct CounterpartRow
        {
            public Faction Target;
            public FactionRelationKind Relation;
            public DiplomacyNegotiateAction? Action;
            public float AskSilver;
            public float StrengthRatio;
            public float WarEnemyPower;
            public float WarFoeRatio;
            public string RejectReason;
            public bool CanAct;
            public float FreezeDays;
            public List<ActionOffer> Offers;
        }

        /// <summary>Build counterpart rows for overview; sortable by ask ascending (unavailable last).</summary>
        public static List<CounterpartRow> BuildCounterpartRows(Faction negotiator)
        {
            var rows = new List<CounterpartRow>();
            if (negotiator == null) return rows;

            float warEnemyPower = GetWarEnemyPower(negotiator);
            float warFoeRatio = GetWarFoeRatio(negotiator);

            foreach (Faction f in Find.FactionManager.AllFactionsVisible)
            {
                if (f == null || f == negotiator || f.IsPlayer) continue;
                if (WorldActions_Utils.IsExcludedFaction(f)) continue;

                FactionRelationKind kind = WorldActions_Utils.SafeRelationKindWith(negotiator, f);
                float freezeDays = 0f;
                var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
                WorldActions_DiplomacyBuffsNerfs.TryGetDiplomacyFreezeDaysRemaining(negotiator, f, manager, out freezeDays);

                var row = new CounterpartRow
                {
                    Target = f,
                    Relation = kind,
                    FreezeDays = freezeDays,
                    StrengthRatio = GetStrengthRatio(negotiator, f),
                    WarEnemyPower = warEnemyPower,
                    WarFoeRatio = warFoeRatio,
                    Offers = new List<ActionOffer>()
                };

                if (kind == FactionRelationKind.Ally)
                {
                    row.CanAct = false;
                    row.RejectReason = "TSA_WD_Negotiate_AllyUntouchable".Translate();
                    rows.Add(row);
                    continue;
                }

                if (kind == FactionRelationKind.Neutral)
                {
                    AddOffer(ref row, negotiator, f, DiplomacyNegotiateAction.DeclareWar, warEnemyPower);
                    AddOffer(ref row, negotiator, f, DiplomacyNegotiateAction.BecomeAlly, warEnemyPower);
                }
                else
                    AddOffer(ref row, negotiator, f, DiplomacyNegotiateAction.BecomeNeutral, warEnemyPower);

                rows.Add(row);
            }

            rows.Sort((a, b) =>
            {
                bool aOk = a.CanAct && a.AskSilver > 0f;
                bool bOk = b.CanAct && b.AskSilver > 0f;
                if (aOk != bOk) return aOk ? -1 : 1;
                if (aOk && bOk)
                {
                    int cmp = a.AskSilver.CompareTo(b.AskSilver);
                    if (cmp != 0) return cmp;
                }
                return string.CompareOrdinal(a.Target?.Name, b.Target?.Name);
            });
            return rows;
        }

        private static void AddOffer(
            ref CounterpartRow row,
            Faction negotiator,
            Faction target,
            DiplomacyNegotiateAction action,
            float warEnemyPower)
        {
            bool can = TryEvaluateAction(negotiator, target, action, out float ask, out string reason, warEnemyPower);
            row.Offers.Add(new ActionOffer
            {
                Action = action,
                CanAct = can,
                AskSilver = ask,
                RejectReason = reason
            });
            if (can)
            {
                if (!row.CanAct || (ask > 0.01f && (row.AskSilver <= 0.01f || ask < row.AskSilver)))
                {
                    row.CanAct = true;
                    row.AskSilver = ask;
                    row.Action = action;
                    row.RejectReason = reason;
                }
            }
            else if (!row.CanAct && row.RejectReason.NullOrEmpty())
                row.RejectReason = reason;
        }

        public enum NegotiateFailReason
        {
            None,
            LostInTransit,
            DestinationGone,
            BadRelation,
            DealInvalid,
            PairFrozen,
            PairLocked
        }

        public static bool IsDealStillValid(WorldObject_Traveler_DiplomacyNegotiate deal) =>
            IsDealStillValid(deal, out _);

        public static bool IsDealStillValid(WorldObject_Traveler_DiplomacyNegotiate deal, out NegotiateFailReason failReason)
        {
            failReason = NegotiateFailReason.None;
            if (deal == null)
            {
                failReason = NegotiateFailReason.DestinationGone;
                return false;
            }
            if (deal.negotiatorFaction == null || deal.targetFaction == null)
            {
                failReason = NegotiateFailReason.DealInvalid;
                return false;
            }
            if (!SettlementBuyUtility.IsEligibleSellerRelation(deal.negotiatorFaction))
            {
                failReason = NegotiateFailReason.BadRelation;
                return false;
            }
            if (GoodwillChangeNotifier.GetPlayerGoodwill(deal.negotiatorFaction) < GoodwillFloor)
            {
                failReason = NegotiateFailReason.BadRelation;
                return false;
            }
            if (!(deal.targetObject is Settlement dest) || dest.Destroyed || dest.Faction != deal.negotiatorFaction)
            {
                failReason = NegotiateFailReason.DestinationGone;
                return false;
            }

            var seth = WorldDominationMod.settings;
            if (seth != null && seth.IsPairLocked(deal.negotiatorFaction, deal.targetFaction))
            {
                failReason = NegotiateFailReason.PairLocked;
                return false;
            }

            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            // Already frozen by something else before we arrive: abort (we set freeze on success).
            // If our deal is still pending, freeze from another source blocks.
            if (WorldActions_DiplomacyBuffsNerfs.TryGetDiplomacyFreezeDaysRemaining(
                    deal.negotiatorFaction, deal.targetFaction, manager, out _))
            {
                failReason = NegotiateFailReason.PairFrozen;
                return false;
            }

            if (!TryEvaluateAction(deal.negotiatorFaction, deal.targetFaction, deal.action, out _, out _))
            {
                // Relation already matches desired, or strength gate failed, or ally.
                FactionRelationKind now = WorldActions_Utils.SafeRelationKindWith(deal.negotiatorFaction, deal.targetFaction);
                if (now == deal.desiredKind)
                {
                    failReason = NegotiateFailReason.DealInvalid;
                    return false;
                }
                failReason = NegotiateFailReason.DealInvalid;
                return false;
            }

            return true;
        }

        public static bool TryLaunch(
            Faction negotiator,
            Faction target,
            DiplomacyNegotiateAction action,
            List<ThingDefCountClass> paymentItems,
            out string reason)
        {
            reason = null;
            if (!IsFeatureEnabled)
            {
                reason = "TSA_WD_Negotiate_Disabled".Translate();
                return false;
            }
            if (!CanNegotiateWithFaction(negotiator, out reason) || HasPendingNegotiateForFaction(negotiator))
            {
                if (string.IsNullOrEmpty(reason))
                    reason = "TSA_WD_Negotiate_Pending".Translate();
                return false;
            }
            if (!TryEvaluateAction(negotiator, target, action, out float ask, out reason))
                return false;

            float goodsMv = SettlementBuyUtility.MarketValueOf(paymentItems);
            if (!SettlementBuyUtility.MeetsAsk(goodsMv, ask))
            {
                reason = "TSA_WD_Negotiate_UnderAsk".Translate(ask.ToString("F0"));
                return false;
            }

            Settlement dest = FindNearestSettlement(negotiator);
            if (dest == null)
            {
                reason = "TSA_WD_Negotiate_NoSettlement".Translate();
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

            if (!SettlementBuyUtility.DeductPaymentItems(dest.Tile, paymentCopy, out reason, out var contributed))
                return false;

            Map colonyMap = Find.AnyPlayerHomeMap;
            var warehouses = SettlementBuyUtility.GetContributingWarehouses(dest.Tile);
            WorldObject origin = SettlementBuyUtility.ResolveBuyOrigin(dest.Tile, colonyMap, warehouses, contributed);
            if (origin == null)
            {
                reason = "TSA_WD_Negotiate_NoOrigin".Translate();
                SettlementBuyUtility.RefundItems(colonyMap?.Parent, paymentCopy);
                return false;
            }
            if (origin.Tile == dest.Tile)
            {
                reason = "TSA_WD_Negotiate_SameTile".Translate();
                SettlementBuyUtility.RefundItems(origin, paymentCopy);
                return false;
            }

            if (!WorldActions_Traveler.SpawnDiplomacyNegotiateTraveler(
                    dest, origin, paymentCopy, negotiator, target, action, ask))
            {
                reason = "TSA_WD_Negotiate_SpawnFailed".Translate();
                SettlementBuyUtility.RefundItems(origin, paymentCopy);
                return false;
            }

            return true;
        }

        public static void RefundPayment(WorldObject_Traveler_DiplomacyNegotiate deal, NegotiateFailReason failReason = NegotiateFailReason.DestinationGone)
        {
            if (deal == null || deal.paymentRefunded) return;
            deal.paymentRefunded = true;
            bool hadGoods = deal.paymentItems != null && deal.paymentItems.Count > 0;
            SettlementBuyUtility.RefundItems(deal.originObject, deal.paymentItems);
            deal.paymentItems?.Clear();
            NotifyAborted(deal, failReason, refundedPayment: hadGoods);
        }

        public static void MarkPaymentLostInTransit(WorldObject_Traveler_DiplomacyNegotiate deal, Faction looter = null)
        {
            if (deal == null || deal.paymentRefunded || deal.completed) return;
            deal.paymentRefunded = true;

            float budget = SettlementBuyUtility.MarketValueOf(deal.paymentItems);
            int clashTile = deal.Tile;
            deal.paymentItems?.Clear();

            if (looter != null && !looter.IsPlayer && budget > 0.01f)
            {
                SettlementCaravanLootUtility.AwardLootToFaction(looter, clashTile, budget, isGiftMission: false);
                return;
            }

            NotifyAborted(deal, NegotiateFailReason.LostInTransit, refundedPayment: false);
        }

        private static void NotifyAborted(WorldObject_Traveler_DiplomacyNegotiate deal, NegotiateFailReason failReason, bool refundedPayment)
        {
            if (!(WorldDominationMod.settings?.notifyDiplomacyNegotiateAborted ?? true))
                return;
            string label = "TSA_WD_Negotiate_AbortLetterLabel".Translate();
            string neg = deal?.negotiatorFaction?.Name ?? "?";
            string tgt = deal?.targetFaction?.Name ?? "?";
            string text = failReason switch
            {
                NegotiateFailReason.LostInTransit =>
                    "TSA_WD_Negotiate_AbortLetterTextLost".Translate(neg, tgt),
                NegotiateFailReason.BadRelation =>
                    "TSA_WD_Negotiate_AbortLetterTextBadRelation".Translate(neg, tgt),
                NegotiateFailReason.PairFrozen =>
                    "TSA_WD_Negotiate_AbortLetterTextFrozen".Translate(neg, tgt),
                NegotiateFailReason.PairLocked =>
                    "TSA_WD_Negotiate_AbortLetterTextLocked".Translate(neg, tgt),
                NegotiateFailReason.DealInvalid =>
                    "TSA_WD_Negotiate_AbortLetterTextInvalid".Translate(neg, tgt),
                NegotiateFailReason.DestinationGone =>
                    "TSA_WD_Negotiate_AbortLetterTextGone".Translate(neg, tgt),
                _ => refundedPayment
                    ? "TSA_WD_Negotiate_AbortLetterTextRefund".Translate(neg, tgt)
                    : "TSA_WD_Negotiate_AbortLetterText".Translate(neg, tgt)
            };
            Find.LetterStack.ReceiveLetter(label, text, LetterDefOf.NeutralEvent, deal?.targetObject);
        }

        public static void CompleteArrival(WorldObject_Traveler_DiplomacyNegotiate deal)
        {
            if (deal == null || deal.completed) return;
            if (!IsDealStillValid(deal, out var fail))
            {
                RefundPayment(deal, fail);
                return;
            }

            float budget = SettlementBuyUtility.MarketValueOf(deal.paymentItems);
            int tile = deal.targetObject?.Tile.tileId ?? deal.Tile;

            if (!WorldActions_DiplomacyBuffsNerfs.TryForceDiplomacyWithFreeze(
                    deal.negotiatorFaction,
                    deal.targetFaction,
                    deal.desiredKind,
                    WorldActions_DiplomacyBuffsNerfs.NegotiateFreezeExpiryTick(),
                    out _))
            {
                RefundPayment(deal, NegotiateFailReason.DealInvalid);
                return;
            }

            deal.completed = true;
            deal.paymentRefunded = true;
            deal.paymentItems?.Clear();

            if (budget > 0.01f && deal.negotiatorFaction != null)
            {
                FactionSettlementInvestment.AwardFromSilverBudget(
                    deal.negotiatorFaction,
                    tile,
                    budget,
                    preferFirst: deal.targetObject as Settlement,
                    notify: FactionSettlementInvestment.NotifyKind.Gift,
                    radiusOverride: int.MaxValue);
            }

            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            string actionLabel = DiplomacyNegotiateUtility.ActionVerbLabel(deal.action);
            string logMsg = "TSA_WD_Log_NegotiateCompleted".Translate(
                deal.negotiatorFaction?.Name ?? "?",
                actionLabel,
                deal.targetFaction?.Name ?? "?");
            manager?.AddLog(new SpreadLogEntry(logMsg, deal.targetObject, null)
            {
                highlightKind = SpreadLogHighlightKind.Diplomacy
            });

            if (WorldDominationMod.settings?.notifyDiplomacyNegotiateCompleted ?? true)
            {
                Find.LetterStack.ReceiveLetter(
                    "TSA_WD_Negotiate_CompleteLetterLabel".Translate(),
                    "TSA_WD_Negotiate_CompleteLetterText".Translate(
                        deal.negotiatorFaction?.Name ?? "?",
                        actionLabel,
                        deal.targetFaction?.Name ?? "?"),
                    LetterDefOf.PositiveEvent,
                    deal.targetObject);
            }
        }
    }
}
