using RimWorld;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Timed offer / monitor / one-shot success for TSA_WD_ColonyRoadLink.
    /// </summary>
    public class GameComponent_WdColonyRoadLinkQuest : GameComponent
    {
        public int nextOfferTick = -1;
        public int trackedQuestId = -1;
        public bool permanentlyDone;

        public GameComponent_WdColonyRoadLinkQuest(Game game) : base() { }

        public static void NotifyQuestSucceeded()
        {
            var gc = Current.Game?.GetComponent<GameComponent_WdColonyRoadLinkQuest>();
            if (gc == null) return;
            gc.permanentlyDone = true;
            gc.trackedQuestId = -1;
            gc.nextOfferTick = int.MaxValue;
        }

        public static void NotifyQuestFailed()
        {
            var gc = Current.Game?.GetComponent<GameComponent_WdColonyRoadLinkQuest>();
            gc?.ScheduleNextOffer();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref nextOfferTick, "nextOfferTick", -1);
            Scribe_Values.Look(ref trackedQuestId, "trackedQuestId", -1);
            Scribe_Values.Look(ref permanentlyDone, "permanentlyDone", false);
        }

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % 250 != 0)
                return;

            if (permanentlyDone)
                return;

            if (nextOfferTick < 0)
                nextOfferTick = WdColonyRoadLinkQuestHelper.FirstOfferAfterTicks;

            bool active = WdColonyRoadLinkQuestHelper.AnyActive();

            if (active)
            {
                Quest? q = WdColonyRoadLinkQuestHelper.FindActiveQuest();
                if (q != null)
                    trackedQuestId = q.id;

                WdColonyRoadLinkQuestHelper.MonitorActiveQuestOutcome();
                return;
            }

            if (trackedQuestId >= 0)
            {
                // Quest ended via XML timeout / hostile without helper notify.
                ScheduleNextOffer();
                trackedQuestId = -1;
            }

            if (!WdColonyRoadLinkQuestHelper.IsSettingEnabled())
                return;

            if (Find.TickManager.TicksGame < nextOfferTick)
                return;

            if (WdColonyRoadLinkQuestHelper.GenerateQuest())
            {
                Quest? q = WdColonyRoadLinkQuestHelper.FindActiveQuest();
                trackedQuestId = q?.id ?? -1;
                nextOfferTick = int.MaxValue;
            }
            else
            {
                nextOfferTick = Find.TickManager.TicksGame + GenDate.TicksPerDay;
            }
        }

        public void ScheduleNextOffer()
        {
            if (permanentlyDone)
            {
                trackedQuestId = -1;
                nextOfferTick = int.MaxValue;
                return;
            }

            int days = Rand.RangeInclusive(
                WdColonyRoadLinkQuestHelper.CooldownDaysMin,
                WdColonyRoadLinkQuestHelper.CooldownDaysMax);
            nextOfferTick = Find.TickManager.TicksGame + days * GenDate.TicksPerDay;
            trackedQuestId = -1;
        }
    }
}
