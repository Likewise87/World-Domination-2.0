using RimWorld;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Timed offer / success / fail driver for TSA_WD_FirstOutpostIntro (chip-quest GameComponent pattern).
    /// Quest does not time out; fails only if the asker becomes hostile or is gone.
    /// </summary>
    public class GameComponent_WdFirstOutpostQuest : GameComponent
    {
        public bool permanentlyDone;
        /// <summary>TicksGame when the quest may first be offered. -1 = not rolled yet.</summary>
        public int offerAtTick = -1;

        public GameComponent_WdFirstOutpostQuest(Game game) : base() { }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref permanentlyDone, "permanentlyDone", false);
            Scribe_Values.Look(ref offerAtTick, "offerAtTick", -1);
        }

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % 250 != 0)
                return;

            bool active = WdFirstOutpostQuestHelper.AnyActive();

            if (active)
            {
                Quest? quest = FindActiveQuest();
                Faction? asker = quest != null ? WdFirstOutpostQuestHelper.TryGetAsker(quest) : null;

                if (WdFirstOutpostQuestHelper.IsAskerHostileOrGone(asker))
                {
                    WdFirstOutpostQuestHelper.FailIfActive();
                    permanentlyDone = true;
                    return;
                }

                if (WdFirstOutpostQuestHelper.HasAnyPlayerOutpost())
                {
                    WdFirstOutpostQuestHelper.CompleteIfActive();
                    permanentlyDone = true;
                }

                return;
            }

            if (permanentlyDone)
                return;

            // Ended without us (hostile XML End, abandoned, etc.): do not re-offer.
            if (EverHadThisQuest())
            {
                permanentlyDone = true;
                return;
            }

            if (WdFirstOutpostQuestHelper.HasAnyPlayerOutpost())
            {
                permanentlyDone = true;
                return;
            }

            if (offerAtTick < 0)
            {
                int days = Rand.RangeInclusive(
                    WdFirstOutpostQuestHelper.OfferAfterMinDays,
                    WdFirstOutpostQuestHelper.OfferAfterMaxDays);
                offerAtTick = days * GenDate.TicksPerDay;
            }

            if (Find.TickManager.TicksGame < offerAtTick)
                return;

            if (!WdFirstOutpostQuestHelper.IsSettingEnabled())
                return;

            WdFirstOutpostQuestHelper.GenerateQuest();
        }

        private static Quest? FindActiveQuest()
        {
            QuestScriptDef? def = DefDatabase<QuestScriptDef>.GetNamedSilentFail(WdFirstOutpostQuestHelper.QuestDefName);
            if (def == null) return null;
            var list = Find.QuestManager.QuestsListForReading;
            for (int i = 0; i < list.Count; i++)
            {
                Quest q = list[i];
                if (q.root == def && q.State == QuestState.Ongoing)
                    return q;
            }
            return null;
        }

        private static bool EverHadThisQuest()
        {
            QuestScriptDef? def = DefDatabase<QuestScriptDef>.GetNamedSilentFail(WdFirstOutpostQuestHelper.QuestDefName);
            if (def == null) return false;
            var list = Find.QuestManager.QuestsListForReading;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].root == def)
                    return true;
            }
            return false;
        }
    }
}
