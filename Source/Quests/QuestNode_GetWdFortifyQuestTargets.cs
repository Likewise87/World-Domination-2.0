using System.Collections.Generic;
using TSA_WorldDomination;
using RimWorld.Planet;
using Verse;
using Verse.Grammar;

namespace RimWorld.QuestGen
{
    public class QuestNode_GetWdFortifyQuestTargets : QuestNode
    {
        [NoTranslate]
        public string storeAskerAs = "faction";

        [NoTranslate]
        public string storeSettlementAs = "allySettlement";

        [NoTranslate]
        public string storeGoodwillAs = "goodwillAmount";

        protected override bool TestRunInt(Slate slate)
        {
            if (TryGetFromSlate(slate, out _, out Settlement ally, out _)
                && ally != null && !ally.Destroyed)
                return true;

            if (!WdFortifySettlementQuestHelper.TryPickAskerAndTarget(
                    out Faction asker, out ally, out int goodwill))
                return false;

            slate.Set(storeAskerAs, asker);
            slate.Set(storeSettlementAs, ally);
            slate.Set(storeGoodwillAs, goodwill);
            return true;
        }

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;

            if (!TryGetFromSlate(slate, out Faction asker, out Settlement ally, out int goodwill)
                || asker == null
                || ally == null
                || ally.Destroyed)
            {
                if (!WdFortifySettlementQuestHelper.TryPickAskerAndTarget(out asker, out ally, out goodwill))
                    throw new System.InvalidOperationException("[WD] Fortify settlement quest has no valid target.");
                slate.Set(storeAskerAs, asker);
                slate.Set(storeSettlementAs, ally);
                slate.Set(storeGoodwillAs, goodwill);
            }

            if (!asker.Hidden)
            {
                var involved = new QuestPart_InvolvedFactions();
                involved.factions.Add(asker);
                QuestGen.quest.AddPart(involved);
            }

            QuestGen.AddQuestDescriptionRules(new List<Rule>
            {
                new Rule_String("allySettlement_label", ally.Label)
            });
            QuestGen.AddQuestNameRules(new List<Rule>
            {
                new Rule_String("allySettlement_label", ally.Label)
            });

            SettlementTier tier = ally.GetComponent<CompViralSpread>()?.tier ?? SettlementTier.T1;
            var tracked = new QuestPart_WdTrackedSettlement
            {
                settlement = ally,
                originalEnemyFaction = ally.Faction,
                targetTier = tier,
                playerAttributed = false,
                settlementLabelFallback = ally.LabelCap
            };
            QuestGen.quest.AddPart(tracked);
        }

        private bool TryGetFromSlate(Slate slate, out Faction asker, out Settlement ally, out int goodwill)
        {
            asker = null;
            ally = null;
            goodwill = 0;
            if (!slate.TryGet(storeAskerAs, out asker) || asker == null)
                return false;
            if (!slate.TryGet(storeSettlementAs, out ally) || ally == null)
                return false;
            if (!slate.TryGet(storeGoodwillAs, out goodwill))
                goodwill = 10;
            return true;
        }
    }
}
