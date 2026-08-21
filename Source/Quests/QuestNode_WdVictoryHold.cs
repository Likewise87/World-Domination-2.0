using RimWorld;
using TSA_WorldDomination;
using Verse;

namespace RimWorld.QuestGen
{
    /// <summary>Adds <see cref="QuestPart_WdVictoryHold"/> to track the 15-day #1 streak.</summary>
    public class QuestNode_WdVictoryHold : QuestNode
    {
        public SlateRef<int> holdDays = WdWorldDominationVictory.RequiredHoldDays;

        protected override bool TestRunInt(Slate slate) => true;

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            int days = holdDays.GetValue(slate);
            if (days <= 0)
                days = WdWorldDominationVictory.RequiredHoldDays;

            var part = new QuestPart_WdVictoryHold
            {
                holdDaysRequired = days,
                inSignalEnable = QuestGen.slate.Get<string>("inSignal")
                    ?? QuestGen.quest.InitiateSignal
            };
            QuestGen.quest.AddPart(part);
        }
    }
}
