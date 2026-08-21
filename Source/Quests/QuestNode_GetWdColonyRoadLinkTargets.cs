using TSA_WorldDomination;
using RimWorld.Planet;
using Verse;

namespace RimWorld.QuestGen
{
    /// <summary>
    /// Picks Ally/Neutral WD settlement within 30 tiles for the colony road-link quest.
    /// </summary>
    public class QuestNode_GetWdColonyRoadLinkTargets : QuestNode
    {
        [NoTranslate]
        public string storeAskerAs = "faction";

        [NoTranslate]
        public string storeSettlementAs = "askerSettlement";

        [NoTranslate]
        public string storeGoodwillAs = "goodwillAmount";

        protected override bool TestRunInt(Slate slate)
        {
            if (TryGetFromSlate(slate, out _, out Settlement settlement, out _)
                && settlement != null && !settlement.Destroyed)
                return true;

            if (!WdColonyRoadLinkQuestHelper.TryPickTargets(out Faction asker, out settlement, out int goodwill))
                return false;

            slate.Set(storeAskerAs, asker);
            slate.Set(storeSettlementAs, settlement);
            slate.Set(storeGoodwillAs, goodwill);
            return true;
        }

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;

            if (!TryGetFromSlate(slate, out Faction asker, out Settlement settlement, out int goodwill)
                || asker == null
                || settlement == null
                || settlement.Destroyed)
            {
                if (!WdColonyRoadLinkQuestHelper.TryPickTargets(out asker, out settlement, out goodwill))
                    throw new System.InvalidOperationException("[WD] Colony road-link quest has no valid target.");
                slate.Set(storeAskerAs, asker);
                slate.Set(storeSettlementAs, settlement);
                slate.Set(storeGoodwillAs, goodwill);
            }

            if (!asker.Hidden)
            {
                var involved = new QuestPart_InvolvedFactions();
                involved.factions.Add(asker);
                QuestGen.quest.AddPart(involved);
            }

            var tracked = new QuestPart_WdTrackedRoadLink
            {
                settlement = settlement,
                askerFaction = asker,
                settlementLabelFallback = settlement.LabelCap
            };
            QuestGen.quest.AddPart(tracked);
        }

        private bool TryGetFromSlate(Slate slate, out Faction asker, out Settlement settlement, out int goodwill)
        {
            asker = null;
            settlement = null;
            goodwill = 0;
            if (!slate.TryGet(storeAskerAs, out asker) || asker == null)
                return false;
            if (!slate.TryGet(storeSettlementAs, out settlement) || settlement == null)
                return false;
            if (!slate.TryGet(storeGoodwillAs, out goodwill))
                goodwill = WdColonyRoadLinkQuestHelper.GoodwillReward;
            return true;
        }
    }
}
