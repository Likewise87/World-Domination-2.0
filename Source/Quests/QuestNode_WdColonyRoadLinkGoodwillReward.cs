using Verse;

namespace RimWorld.QuestGen
{
    /// <summary>
    /// Goodwill reward UI + QuestPart_FactionGoodwillChange for ColonyRoadLinkSuccess.
    /// </summary>
    public class QuestNode_WdColonyRoadLinkGoodwillReward : QuestNode
    {
        public const string SuccessSignal = "ColonyRoadLinkSuccess";

        public SlateRef<Faction> faction;
        public SlateRef<int> amount = 15;

        protected override bool TestRunInt(Slate slate) => true;

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            Faction asker = faction.GetValue(slate) ?? slate.Get<Faction>("faction");
            if (asker == null || asker.IsPlayer)
                return;

            int change = amount.GetValue(slate);
            if (change == 0)
                change = slate.Get("goodwillAmount", 15);
            if (change == 0)
                change = 15;

            string inSignal = QuestGenUtility.HardcodedSignalWithQuestID(SuccessSignal);

            var goodwillPart = new QuestPart_FactionGoodwillChange
            {
                change = change,
                faction = asker,
                inSignal = inSignal,
                canSendMessage = false,
                canSendHostilityLetter = true,
                historyEvent = HistoryEventDefOf.QuestGoodwillReward
            };
            QuestGen.quest.AddPart(goodwillPart);

            var reward = new Reward_Goodwill
            {
                amount = change,
                faction = asker
            };

            var choice = new QuestPart_Choice.Choice();
            choice.rewards.Add(reward);
            choice.questParts.Add(goodwillPart);

            var choicePart = new QuestPart_Choice
            {
                inSignalChoiceUsed = inSignal
            };
            choicePart.choices.Add(choice);
            QuestGen.quest.AddPart(choicePart);
        }
    }
}
