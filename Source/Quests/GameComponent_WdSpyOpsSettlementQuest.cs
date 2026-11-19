using RimWorld;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// One-offer SpyOps intro quest. Any end (success, fail, expire) sets permanentlyDone.
    /// </summary>
    public class GameComponent_WdSpyOpsSettlementQuest : GameComponent
    {
        public bool permanentlyDone;
        public int offerAtTick = -1;
        public int trackedQuestId = -1;

        public GameComponent_WdSpyOpsSettlementQuest(Game game) : base() { }

        public static void NotifyQuestEnded()
        {
            var gc = Current.Game?.GetComponent<GameComponent_WdSpyOpsSettlementQuest>();
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

            bool active = WdSpyOpsSettlementQuestHelper.AnyActive();

            if (active)
            {
                Quest? q = WdSpyOpsSettlementQuestHelper.FindActiveQuest();
                if (q != null)
                    trackedQuestId = q.id;

                WdSpyOpsSettlementQuestHelper.MonitorActiveQuestOutcome();
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

            if (offerAtTick < 0)
            {
                int days = Rand.RangeInclusive(
                    WdSpyOpsSettlementQuestHelper.OfferAfterMinDays,
                    WdSpyOpsSettlementQuestHelper.OfferAfterMaxDays);
                offerAtTick = days * GenDate.TicksPerDay;
            }

            if (!WdSpyOpsSettlementQuestHelper.IsSettingEnabled())
                return;

            if (Find.TickManager.TicksGame < offerAtTick)
                return;

            if (WdSpyOpsSettlementQuestHelper.GenerateQuest())
            {
                Quest? q = WdSpyOpsSettlementQuestHelper.FindActiveQuest();
                trackedQuestId = q?.id ?? -1;
                offerAtTick = int.MaxValue;
            }
            else
            {
                // No valid asker/target yet; retry in a day (still before first successful issue).
                offerAtTick = Find.TickManager.TicksGame + GenDate.TicksPerDay;
            }
        }

        private static bool EverHadThisQuest()
        {
            QuestScriptDef? def = DefDatabase<QuestScriptDef>.GetNamedSilentFail(WdSpyOpsSettlementQuestHelper.QuestDefName);
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
