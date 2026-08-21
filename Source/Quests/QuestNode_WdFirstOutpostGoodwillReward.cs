using Verse;

namespace RimWorld.QuestGen
{
    /// <summary>
    /// Adds a fixed goodwill reward that shows in the quest UI (Reward_Goodwill icon stack)
    /// and applies via QuestPart_FactionGoodwillChange when FirstOutpostSuccess fires.
    /// </summary>
    public class QuestNode_WdFirstOutpostGoodwillReward : QuestNode
    {
        public const string SuccessSignal = "FirstOutpostSuccess";

        public SlateRef<Faction> faction;
        public SlateRef<int> amount = 10;

        protected override bool TestRunInt(Slate slate) => true;

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            Faction asker = faction.GetValue(slate) ?? slate.Get<Faction>("faction");
            if (asker == null || asker.IsPlayer)
                return;

            int change = amount.GetValue(slate);
            if (change == 0)
                change = 10;

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
