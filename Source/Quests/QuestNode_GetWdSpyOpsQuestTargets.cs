using System.Collections.Generic;
using TSA_WorldDomination;
using RimWorld.Planet;
using Verse;
using Verse.Grammar;

namespace RimWorld.QuestGen
{
    public class QuestNode_GetWdSpyOpsQuestTargets : QuestNode
    {
        [NoTranslate]
        public string storeAskerAs = "faction";

        [NoTranslate]
        public string storeSettlementAs = "enemySettlement";

        [NoTranslate]
        public string storeGoodwillAs = "goodwillAmount";

        protected override bool TestRunInt(Slate slate)
        {
            if (TryGetFromSlate(slate, out _, out Settlement enemy, out _)
                && enemy != null && !enemy.Destroyed)
                return true;

            if (!WdSpyOpsSettlementQuestHelper.TryPickAskerAndTarget(
                    out Faction asker, out enemy, out int goodwill))
                return false;

            slate.Set(storeAskerAs, asker);
            slate.Set(storeSettlementAs, enemy);
            slate.Set(storeGoodwillAs, goodwill);
            return true;
        }

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;

            if (!TryGetFromSlate(slate, out Faction asker, out Settlement enemy, out int goodwill)
                || asker == null
                || enemy == null
                || enemy.Destroyed)
            {
                if (!WdSpyOpsSettlementQuestHelper.TryPickAskerAndTarget(out asker, out enemy, out goodwill))
                    throw new System.InvalidOperationException("[WD] Spy-ops settlement quest has no valid target.");
                slate.Set(storeAskerAs, asker);
                slate.Set(storeSettlementAs, enemy);
                slate.Set(storeGoodwillAs, goodwill);
            }

            if (!asker.Hidden)
            {
                var involved = new QuestPart_InvolvedFactions();
                involved.factions.Add(asker);
                if (enemy.Faction != null && !enemy.Faction.Hidden && enemy.Faction != asker)
                    involved.factions.Add(enemy.Faction);
                QuestGen.quest.AddPart(involved);
            }

            QuestGen.AddQuestDescriptionRules(new List<Rule>
            {
                new Rule_String("enemySettlement_label", enemy.Label)
            });
            QuestGen.AddQuestNameRules(new List<Rule>
            {
                new Rule_String("enemySettlement_label", enemy.Label)
            });

            SettlementTier tier = enemy.GetComponent<CompViralSpread>()?.tier ?? SettlementTier.T1;
            var tracked = new QuestPart_WdTrackedSettlement
            {
                settlement = enemy,
                originalEnemyFaction = enemy.Faction,
                targetTier = tier,
                playerAttributed = false,
                settlementLabelFallback = enemy.LabelCap
            };
            QuestGen.quest.AddPart(tracked);
        }

        private bool TryGetFromSlate(Slate slate, out Faction asker, out Settlement enemy, out int goodwill)
        {
            asker = null;
            enemy = null;
            goodwill = 0;
            if (!slate.TryGet(storeAskerAs, out asker) || asker == null)
                return false;
            if (!slate.TryGet(storeSettlementAs, out enemy) || enemy == null)
                return false;
            if (!slate.TryGet(storeGoodwillAs, out goodwill))
                goodwill = 10;
            return true;
        }
    }
}
