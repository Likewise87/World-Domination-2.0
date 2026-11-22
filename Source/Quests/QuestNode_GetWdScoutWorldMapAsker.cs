using System.Collections.Generic;
using TSA_WorldDomination;
using RimWorld.Planet;
using Verse;
using Verse.Grammar;

namespace RimWorld.QuestGen
{
    public class QuestNode_GetWdScoutWorldMapAsker : QuestNode
    {
        [NoTranslate]
        public string storeAskerAs = "faction";

        [NoTranslate]
        public string storeSettlementAs = "askerSettlement";

        protected override bool TestRunInt(Slate slate)
        {
            if (TryGetFromSlate(slate, out _, out Settlement settlement)
                && settlement != null && !settlement.Destroyed)
                return true;

            if (!WdScoutWorldMapQuestHelper.TryPickClosestAskerSettlement(out Faction asker, out settlement))
                return false;

            slate.Set(storeAskerAs, asker);
            slate.Set(storeSettlementAs, settlement);
            return true;
        }

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;

            if (!TryGetFromSlate(slate, out Faction asker, out Settlement settlement)
                || asker == null
                || settlement == null
                || settlement.Destroyed)
            {
                if (!WdScoutWorldMapQuestHelper.TryPickClosestAskerSettlement(out asker, out settlement))
                    throw new System.InvalidOperationException("[WD] Scout world map quest has no valid asker settlement.");
                slate.Set(storeAskerAs, asker);
                slate.Set(storeSettlementAs, settlement);
            }

            WdScoutWorldMapQuestHelper.RevealAskerSettlement(settlement);

            if (!asker.Hidden)
            {
                var involved = new QuestPart_InvolvedFactions();
                involved.factions.Add(asker);
                QuestGen.quest.AddPart(involved);
            }

            QuestGen.AddQuestDescriptionRules(new List<Rule>
            {
                new Rule_String("askerSettlement_label", settlement.Label)
            });
            QuestGen.AddQuestNameRules(new List<Rule>
            {
                new Rule_String("askerSettlement_label", settlement.Label)
            });

            SettlementTier tier = settlement.GetComponent<CompViralSpread>()?.tier ?? SettlementTier.T1;
            var tracked = new QuestPart_WdTrackedSettlement
            {
                settlement = settlement,
                originalEnemyFaction = settlement.Faction,
                targetTier = tier,
                playerAttributed = false,
                settlementLabelFallback = settlement.LabelCap
            };
            QuestGen.quest.AddPart(tracked);
        }

        private bool TryGetFromSlate(Slate slate, out Faction asker, out Settlement settlement)
        {
            asker = null;
            settlement = null;
            if (!slate.TryGet(storeAskerAs, out asker) || asker == null)
                return false;
            if (!slate.TryGet(storeSettlementAs, out settlement) || settlement == null)
                return false;
            return true;
        }
    }
}
