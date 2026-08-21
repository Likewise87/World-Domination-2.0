using RimWorld;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Daily streak: player WD strength #1 for <see cref="holdDaysRequired"/> consecutive days, then victory.
    /// </summary>
    public class QuestPart_WdVictoryHold : QuestPartActivable
    {
        public int holdDaysRequired = WdWorldDominationVictory.RequiredHoldDays;
        public int holdDaysStreak;
        private int lastCheckedDay = -1;

        /// <summary>Quest tab expiry line only — not pawn/inspect cards (those wrongly inherit ExtraInspectString).</summary>
        public override string ExpiryInfoPart =>
            "TSA_WD_Victory_HoldProgress".Translate(holdDaysStreak, holdDaysRequired);

        public override void QuestPartTick()
        {
            if (quest == null || quest.State != QuestState.Ongoing)
                return;
            if (State != QuestPartState.Enabled)
                return;

            int day = Find.TickManager.TicksGame / GenDate.TicksPerDay;
            if (day == lastCheckedDay)
                return;
            lastCheckedDay = day;

            if (WdWorldDominationVictory.IsPlayerWorldLeader())
                holdDaysStreak++;
            else
                holdDaysStreak = 0;

            if (holdDaysStreak >= holdDaysRequired)
                WdWorldDominationVictoryQuestHelper.CompleteIfActive();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref holdDaysRequired, "holdDaysRequired", WdWorldDominationVictory.RequiredHoldDays);
            Scribe_Values.Look(ref holdDaysStreak, "holdDaysStreak", 0);
            Scribe_Values.Look(ref lastCheckedDay, "lastCheckedDay", -1);
        }
    }
}
