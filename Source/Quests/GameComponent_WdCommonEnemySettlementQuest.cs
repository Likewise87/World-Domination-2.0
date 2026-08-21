using RimWorld;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Timed offer / monitor / cooldown for TSA_WD_CommonEnemySettlement.
    /// </summary>
    public class GameComponent_WdCommonEnemySettlementQuest : GameComponent
    {
        public int nextOfferTick = -1;
        public int trackedQuestId = -1;

        public GameComponent_WdCommonEnemySettlementQuest(Game game) : base() { }

        public static void NotifyQuestEnded()
        {
            var gc = Current.Game?.GetComponent<GameComponent_WdCommonEnemySettlementQuest>();
            gc?.ScheduleNextOffer();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref nextOfferTick, "nextOfferTick", -1);
            Scribe_Values.Look(ref trackedQuestId, "trackedQuestId", -1);
        }

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % 250 != 0)
                return;

            if (nextOfferTick < 0)
                nextOfferTick = WdCommonEnemySettlementQuestHelper.FirstOfferAfterTicks;

            bool active = WdCommonEnemySettlementQuestHelper.AnyActive();

            if (active)
            {
                Quest? q = WdCommonEnemySettlementQuestHelper.FindActiveQuest();
                if (q != null)
                    trackedQuestId = q.id;

                WdCommonEnemySettlementQuestHelper.MonitorActiveQuestOutcome();
                return;
            }

            if (trackedQuestId >= 0)
            {
                // Quest ended via XML timeout / hostile without helper NotifyQuestEnded.
                ScheduleNextOffer();
                trackedQuestId = -1;
            }

            if (!WdCommonEnemySettlementQuestHelper.IsSettingEnabled())
                return;

            if (Find.TickManager.TicksGame < nextOfferTick)
                return;

            if (WdCommonEnemySettlementQuestHelper.GenerateQuest())
            {
                Quest? q = WdCommonEnemySettlementQuestHelper.FindActiveQuest();
                trackedQuestId = q?.id ?? -1;
                // Prevent re-offer until this one ends; cooldown applied on end.
                nextOfferTick = int.MaxValue;
            }
            else
            {
                // No valid asker/target yet; retry in a day.
                nextOfferTick = Find.TickManager.TicksGame + GenDate.TicksPerDay;
            }
        }

        public void ScheduleNextOffer()
        {
            int days = Rand.RangeInclusive(
                WdCommonEnemySettlementQuestHelper.CooldownDaysMin,
                WdCommonEnemySettlementQuestHelper.CooldownDaysMax);
            nextOfferTick = Find.TickManager.TicksGame + days * GenDate.TicksPerDay;
            trackedQuestId = -1;
        }
    }
}
