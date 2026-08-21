using RimWorld;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Offer driver for TSA_WD_WorldDominationVictory (day 4–6 first offer; re-offer after abandon).
    /// </summary>
    public class GameComponent_WdWorldDominationVictoryQuest : GameComponent
    {
        public bool permanentlyDone;
        public bool alreadyWon;
        public bool victoryDialogOpen;
        /// <summary>TicksGame when the quest may next be offered. -1 = not rolled yet.</summary>
        public int nextOfferTick = -1;
        public int trackedQuestId = -1;

        public GameComponent_WdWorldDominationVictoryQuest(Game game) : base() { }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref permanentlyDone, "permanentlyDone", false);
            Scribe_Values.Look(ref alreadyWon, "alreadyWon", false);
            Scribe_Values.Look(ref victoryDialogOpen, "victoryDialogOpen", false);
            Scribe_Values.Look(ref nextOfferTick, "nextOfferTick", -1);
            Scribe_Values.Look(ref trackedQuestId, "trackedQuestId", -1);
        }

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % 250 != 0)
                return;

            if (alreadyWon || permanentlyDone)
                return;

            bool active = WdWorldDominationVictoryQuestHelper.AnyActive();

            if (active)
            {
                Quest? q = WdWorldDominationVictoryQuestHelper.FindActiveQuest();
                if (q != null)
                    trackedQuestId = q.id;
                return;
            }

            if (trackedQuestId >= 0)
            {
                // Ended without success (abandon / settings remove / fail). Re-offer after cooldown.
                ScheduleReoffer();
                trackedQuestId = -1;
            }

            if (!WdWorldDominationVictoryQuestHelper.IsSettingEnabled())
                return;

            if (nextOfferTick < 0)
            {
                int days = Rand.RangeInclusive(
                    WdWorldDominationVictoryQuestHelper.OfferAfterMinDays,
                    WdWorldDominationVictoryQuestHelper.OfferAfterMaxDays);
                nextOfferTick = days * GenDate.TicksPerDay;
            }

            if (Find.TickManager.TicksGame < nextOfferTick)
                return;

            WdWorldDominationVictoryQuestHelper.GenerateQuest();
        }

        public void ScheduleReoffer()
        {
            if (alreadyWon || permanentlyDone)
            {
                trackedQuestId = -1;
                nextOfferTick = int.MaxValue;
                return;
            }

            int days = Rand.RangeInclusive(
                WdWorldDominationVictoryQuestHelper.ReofferCooldownMinDays,
                WdWorldDominationVictoryQuestHelper.ReofferCooldownMaxDays);
            nextOfferTick = Find.TickManager.TicksGame + days * GenDate.TicksPerDay;
            trackedQuestId = -1;
        }
    }
}
