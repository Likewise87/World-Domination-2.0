using RimWorld;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Right-side alert only while the player holds #1 during an active victory quest.</summary>
    public class Alert_WdWorldDominationVictory : Alert
    {
        public Alert_WdWorldDominationVictory()
        {
            defaultPriority = AlertPriority.Medium;
        }

        public override string GetLabel()
        {
            QuestPart_WdVictoryHold? part = WdWorldDominationVictoryQuestHelper.FindActiveHoldPart();
            if (part == null || WdWorldDominationVictory.AlreadyWon)
                return "";
            return "TSA_WD_Alert_VictoryHold".Translate(part.holdDaysStreak, part.holdDaysRequired);
        }

        public override TaggedString GetExplanation()
        {
            QuestPart_WdVictoryHold? part = WdWorldDominationVictoryQuestHelper.FindActiveHoldPart();
            if (part == null || WdWorldDominationVictory.AlreadyWon)
                return "";
            return "TSA_WD_Alert_VictoryHold_DescLeader".Translate(part.holdDaysStreak, part.holdDaysRequired);
        }

        public override AlertReport GetReport()
        {
            if (Current.ProgramState != ProgramState.Playing) return false;
            if (WdWorldDominationVictory.AlreadyWon) return false;
            if (!WdWorldDominationVictoryQuestHelper.AnyActive()) return false;
            if (!WdWorldDominationVictory.IsPlayerWorldLeader()) return false;
            return AlertReport.Active;
        }
    }
}
