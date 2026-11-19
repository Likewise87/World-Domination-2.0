using RimWorld;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// One-offer Fortify intro quest. Offered after SpyOps ends (or SpyOps setting off). Any end → permanentlyDone.
    /// </summary>
    public class GameComponent_WdFortifySettlementQuest : GameComponent
    {
        public bool permanentlyDone;
        public int offerAtTick = -1;
        public int trackedQuestId = -1;

        public GameComponent_WdFortifySettlementQuest(Game game) : base() { }

        public static void NotifyQuestEnded()
        {
            var gc = Current.Game?.GetComponent<GameComponent_WdFortifySettlementQuest>();
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

            bool active = WdFortifySettlementQuestHelper.AnyActive();

            if (active)
            {
                Quest? q = WdFortifySettlementQuestHelper.FindActiveQuest();
                if (q != null)
                    trackedQuestId = q.id;

                WdFortifySettlementQuestHelper.MonitorActiveQuestOutcome();
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

            if (WdSpyOpsSettlementQuestHelper.AnyActive())
                return;

            if (!SpyOpsGateOpen())
                return;

            if (offerAtTick < 0)
                ScheduleFirstOffer();

            if (!WdFortifySettlementQuestHelper.IsSettingEnabled())
                return;

            if (Find.TickManager.TicksGame < offerAtTick)
                return;

            if (WdFortifySettlementQuestHelper.GenerateQuest())
            {
                Quest? q = WdFortifySettlementQuestHelper.FindActiveQuest();
                trackedQuestId = q?.id ?? -1;
                offerAtTick = int.MaxValue;
            }
            else
            {
                offerAtTick = Find.TickManager.TicksGame + GenDate.TicksPerDay;
            }
        }

        private static bool SpyOpsGateOpen()
        {
            if (!WdSpyOpsSettlementQuestHelper.IsSettingEnabled())
                return true;

            var spyGc = Current.Game?.GetComponent<GameComponent_WdSpyOpsSettlementQuest>();
            return spyGc != null && spyGc.permanentlyDone;
        }

        private void ScheduleFirstOffer()
        {
            if (!WdSpyOpsSettlementQuestHelper.IsSettingEnabled())
            {
                int days = Rand.RangeInclusive(
                    WdFortifySettlementQuestHelper.OfferWhenSpyOpsOffMinDays,
                    WdFortifySettlementQuestHelper.OfferWhenSpyOpsOffMaxDays);
                offerAtTick = days * GenDate.TicksPerDay;
                return;
            }

            int delay = Rand.RangeInclusive(
                WdFortifySettlementQuestHelper.OfferDelayMinDays,
                WdFortifySettlementQuestHelper.OfferDelayMaxDays);
            offerAtTick = Find.TickManager.TicksGame + delay * GenDate.TicksPerDay;
        }

        private static bool EverHadThisQuest()
        {
            QuestScriptDef? def = DefDatabase<QuestScriptDef>.GetNamedSilentFail(WdFortifySettlementQuestHelper.QuestDefName);
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
