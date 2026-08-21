using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Applies player-facing goodwill changes and posts top-of-screen messages (not letters).</summary>
    public static class GoodwillChangeNotifier
    {
        public static bool TryAffectPlayerGoodwill(Faction faction, int change, out int newGoodwill)
        {
            newGoodwill = 0;
            Faction player = Faction.OfPlayerSilentFail;
            if (faction == null || player == null || change == 0)
                return false;

            if (!faction.TryAffectGoodwillWith(player, change))
                return false;

            newGoodwill = faction.RelationWith(player, true)?.baseGoodwill ?? 0;
            return true;
        }

        public static int GetPlayerGoodwill(Faction faction)
        {
            return faction?.RelationWith(Faction.OfPlayerSilentFail, true)?.baseGoodwill ?? 0;
        }

        public static void NotifyTrade(Faction faction, int change)
        {
            if (change <= 0 || faction == null)
                return;

            if (!TryAffectPlayerGoodwill(faction, change, out int now))
                return;

            Post(
                "TSA_WD_GoodwillMsg_Trade".Translate(FactionLabel(faction), FormatSigned(change), now),
                faction,
                MessageTypeDefOf.PositiveEvent);
        }

        public static bool TryPayAlliedRaidOrder(Faction ally, WorldObject target, int cost, out int newGoodwill)
        {
            newGoodwill = 0;
            if (ally == null || cost <= 0)
                return false;

            if (!TryAffectPlayerGoodwill(ally, -cost, out newGoodwill))
                return false;

            Post(
                "TSA_WD_GoodwillMsg_AlliedRaidOrdered".Translate(
                    FactionLabel(ally),
                    TargetLabel(target),
                    cost,
                    newGoodwill),
                ally,
                MessageTypeDefOf.TaskCompletion);
            return true;
        }

        public static void RefundAlliedRaidOrder(Faction ally, WorldObject target, int refund)
        {
            if (ally == null || refund <= 0)
                return;

            if (!TryAffectPlayerGoodwill(ally, refund, out int now))
                return;

            Post(
                "TSA_WD_GoodwillMsg_AlliedRaidRefund".Translate(
                    FactionLabel(ally),
                    TargetLabel(target),
                    refund,
                    now),
                ally,
                MessageTypeDefOf.PositiveEvent);
        }

        public static void NotifyConquestGift(Faction ally, WorldObject settlement, int gain)
        {
            if (ally == null || gain <= 0)
                return;

            if (!TryAffectPlayerGoodwill(ally, gain, out int now))
                return;

            GlobalTargetInfo lookTarget = settlement != null ? settlement : GlobalTargetInfo.Invalid;
            Messages.Message(
                "TSA_WD_GoodwillMsg_ConquestGift".Translate(
                    FactionLabel(ally),
                    TargetLabel(settlement),
                    gain,
                    now),
                lookTarget,
                MessageTypeDefOf.PositiveEvent);
        }

        public static void NotifySpyOpFailure(Faction faction, Settlement settlement, string opNameKey, int penalty)
        {
            if (faction == null || penalty >= 0)
                return;

            if (!TryAffectPlayerGoodwill(faction, penalty, out int now))
                return;

            Post(
                "TSA_WD_GoodwillMsg_SpyOpFailure".Translate(
                    opNameKey.Translate(),
                    TargetLabel(settlement),
                    -penalty,
                    FactionLabel(faction),
                    now),
                faction,
                MessageTypeDefOf.NegativeEvent);
        }

        public static void RefundOrderedRoad(Faction faction, WorldObject builder, WorldObject target, int refund, int remainingSegments, RoadProjectClearReason reason)
        {
            if (faction == null || refund <= 0)
                return;

            if (!TryAffectPlayerGoodwill(faction, refund, out int now))
                return;

            string reasonKey = reason switch
            {
                RoadProjectClearReason.PlayerCancel => "TSA_WD_GoodwillMsg_OrderedRoadRefundCancel",
                RoadProjectClearReason.SettlementDestroyed => "TSA_WD_GoodwillMsg_OrderedRoadRefundDestroyed",
                RoadProjectClearReason.FactionHostile => "TSA_WD_GoodwillMsg_OrderedRoadRefundHostile",
                _ => "TSA_WD_GoodwillMsg_OrderedRoadRefundAbort"
            };
            Post(
                reasonKey.Translate(FactionLabel(faction), TargetLabel(target), refund, remainingSegments, now),
                faction,
                MessageTypeDefOf.PositiveEvent);
        }

        public static bool TryPayOrderedRoadOrder(Faction faction, WorldObject builder, WorldObject target, int cost, out int newGoodwill)
        {
            newGoodwill = 0;
            if (faction == null || cost <= 0)
                return false;

            if (!TryAffectPlayerGoodwill(faction, -cost, out newGoodwill))
                return false;

            Post(
                "TSA_WD_GoodwillMsg_OrderedRoadOrdered".Translate(
                    FactionLabel(faction),
                    TargetLabel(builder),
                    TargetLabel(target),
                    cost,
                    newGoodwill),
                faction,
                MessageTypeDefOf.TaskCompletion);
            return true;
        }

        public static bool TryPaySettlementBuy(Faction faction, WorldObject settlement, int cost, out int newGoodwill)
        {
            newGoodwill = 0;
            if (faction == null || cost <= 0)
                return false;

            if (!TryAffectPlayerGoodwill(faction, -cost, out newGoodwill))
                return false;

            Post(
                "TSA_WD_GoodwillMsg_SettlementBuyPaid".Translate(
                    FactionLabel(faction),
                    TargetLabel(settlement),
                    cost,
                    newGoodwill),
                faction,
                MessageTypeDefOf.TaskCompletion);
            return true;
        }

        public static bool TryPayOrderedTraderOrder(Faction faction, WorldObject sender, WorldObject destination, TraderKindDef kind, int cost, out int newGoodwill)
        {
            newGoodwill = 0;
            if (faction == null || cost <= 0)
                return false;

            if (!TryAffectPlayerGoodwill(faction, -cost, out newGoodwill))
                return false;

            Post(
                "TSA_WD_GoodwillMsg_OrderedTraderOrdered".Translate(
                    FactionLabel(faction),
                    TargetLabel(sender),
                    TargetLabel(destination),
                    kind?.LabelCap ?? "?",
                    cost,
                    newGoodwill),
                faction,
                MessageTypeDefOf.TaskCompletion);
            return true;
        }

        public static void RefundSettlementBuy(Faction faction, WorldObject settlement, int refund)
        {
            if (faction == null || refund <= 0)
                return;

            if (!TryAffectPlayerGoodwill(faction, refund, out int now))
                return;

            Post(
                "TSA_WD_GoodwillMsg_SettlementBuyRefund".Translate(
                    FactionLabel(faction),
                    TargetLabel(settlement),
                    refund,
                    now),
                faction,
                MessageTypeDefOf.PositiveEvent);
        }

        public static bool CanPayOrderedRoadCost(Faction faction, int cost, int floor = 10)
        {
            if (faction == null || cost <= 0) return false;
            int goodwill = GetPlayerGoodwill(faction);
            return goodwill - cost >= floor;
        }

        public static void NotifyTraderCaravanArrival(Faction sender, Faction receiver, int change)
        {
            if (change <= 0)
                return;

            Faction player = Faction.OfPlayerSilentFail;
            if (player == null)
                return;

            Faction other;
            if (sender == player)
                other = receiver;
            else if (receiver == player)
                other = sender;
            else
                return;

            if (other == null)
                return;

            int now = GetPlayerGoodwill(other);
            Post(
                "TSA_WD_GoodwillMsg_TraderCaravan".Translate(FactionLabel(other), FormatSigned(change), now),
                other,
                MessageTypeDefOf.PositiveEvent);
        }

        /// <summary>Embassy outpost cycle goodwill (TryAffect already applied).</summary>
        public static void NotifyEmbassyCycle(Faction faction, int change, int newGoodwill, WorldObject_WD_Outpost source)
        {
            if (change <= 0 || faction == null)
                return;

            GlobalTargetInfo look = source != null ? source : GlobalTargetInfo.Invalid;
            Messages.Message(
                "TSA_WD_GoodwillMsg_Embassy".Translate(
                    FactionLabel(faction),
                    FormatSigned(change),
                    newGoodwill,
                    source?.LabelCap ?? "?"),
                look,
                MessageTypeDefOf.PositiveEvent);
        }

        /// <summary>
        /// Top-of-screen message after a WD quest already applied goodwill
        /// (e.g. via <see cref="QuestPart_FactionGoodwillChange"/>). Does not change goodwill again.
        /// </summary>
        public static void NotifyQuestReward(Faction faction, int change)
        {
            if (change <= 0 || faction == null)
                return;

            int now = GetPlayerGoodwill(faction);
            Post(
                "TSA_WD_GoodwillMsg_QuestReward".Translate(FactionLabel(faction), FormatSigned(change), now),
                faction,
                MessageTypeDefOf.PositiveEvent);
        }

        private static void Post(string text, Faction faction, MessageTypeDef type)
        {
            Messages.Message(text, type);
        }

        private static string FactionLabel(Faction faction)
        {
            return faction?.Name ?? faction?.def?.LabelCap ?? "?";
        }

        private static string TargetLabel(WorldObject target)
        {
            return target?.LabelCap ?? "?";
        }

        private static string FormatSigned(int change)
        {
            return change > 0 ? "+" + change : change.ToString();
        }
    }
}
