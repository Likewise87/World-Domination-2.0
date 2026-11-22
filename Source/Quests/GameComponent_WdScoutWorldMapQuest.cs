using RimWorld;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// One-offer Scout the world map quest. Schedules after intel tracking turns on.
    /// Any end (success, fail, expire) sets permanentlyDone.
    /// </summary>
    public class GameComponent_WdScoutWorldMapQuest : GameComponent
    {
        public bool permanentlyDone;
        public int offerAtTick = -1;
        public int trackedQuestId = -1;

        public GameComponent_WdScoutWorldMapQuest(Game game) : base() { }

        public static void NotifyQuestEnded()
        {
            var gc = Current.Game?.GetComponent<GameComponent_WdScoutWorldMapQuest>();
            if (gc == null) return;
            gc.permanentlyDone = true;
            gc.trackedQuestId = -1;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref permanentlyDone, "permanentlyDone", false);
            Scribe_Values.Look(ref offerAtTick, "offerAtTick", -1);
            Scribe_Values.Look(ref trackedQuestId, "trackedQuestId", -1);
        }

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % 250 != 0)
                return;

            bool active = WdScoutWorldMapQuestHelper.AnyActive();

            if (active)
            {
                Quest? q = WdScoutWorldMapQuestHelper.FindActiveQuest();
                if (q != null)
                    trackedQuestId = q.id;

                WdScoutWorldMapQuestHelper.MonitorActiveQuestOutcome();
                return;
            }

            if (permanentlyDone)
                return;

            if (trackedQuestId >= 0 || EverHadThisQuest())
            {
                permanentlyDone = true;
                trackedQuestId = -1;
                return;
            }

            if (!WorldComponent_SettlementIntel.IsTracking())
                return;

            if (WdScoutWorldMapQuestHelper.PlayerAlreadyLaunchedScout())
            {
                permanentlyDone = true;
                return;
            }

            if (offerAtTick < 0)
            {
                int days = Rand.RangeInclusive(
                    WdScoutWorldMapQuestHelper.OfferAfterMinDays,
                    WdScoutWorldMapQuestHelper.OfferAfterMaxDays);
                offerAtTick = Find.TickManager.TicksGame + days * GenDate.TicksPerDay;
            }

            if (!WdScoutWorldMapQuestHelper.IsSettingEnabled())
                return;

            if (Find.TickManager.TicksGame < offerAtTick)
                return;

            if (WdScoutWorldMapQuestHelper.GenerateQuest())
            {
                Quest? q = WdScoutWorldMapQuestHelper.FindActiveQuest();
                trackedQuestId = q?.id ?? -1;
                offerAtTick = int.MaxValue;
            }
            else
            {
                // No valid asker yet; retry in a day (still before first successful issue).
                offerAtTick = Find.TickManager.TicksGame + GenDate.TicksPerDay;
            }
        }

        private static bool EverHadThisQuest()
        {
            QuestScriptDef? def = DefDatabase<QuestScriptDef>.GetNamedSilentFail(WdScoutWorldMapQuestHelper.QuestDefName);
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
