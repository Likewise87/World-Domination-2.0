using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Early one-shot tutorial: successful sabotage or disinformation on a shared-hostile settlement.
    /// </summary>
    public static class WdSpyOpsSettlementQuestHelper
    {
        public const string QuestDefName = "TSA_WD_SpyOpsSettlement";
        public const int OfferAfterMinDays = 10;
        public const int OfferAfterMaxDays = 16;
        public const int TimeoutDays = 8;
        public const int MaxTargetDistanceTiles = 50;

        public static int TimeoutTicks => TimeoutDays * GenDate.TicksPerDay;

        public static bool IsSettingEnabled()
        {
            return WorldDominationMod.settings == null
                || WorldDominationMod.settings.enableSpyOpsSettlementQuest;
        }

        public static bool GenerateQuest(bool ignoreSetting = false)
        {
            if (!ignoreSetting && !IsSettingEnabled())
                return false;

            QuestScriptDef? def = DefDatabase<QuestScriptDef>.GetNamedSilentFail(QuestDefName);
            if (def == null)
            {
                Log.Warning("[WD] Quest def missing: " + QuestDefName);
                return false;
            }

            if (!TryPickAskerAndTarget(out Faction asker, out Settlement enemySettlement, out int goodwill))
                return false;

            var slate = new Slate();
            Map? home = Find.AnyPlayerHomeMap;
            if (home != null)
                slate.Set("points", StorytellerUtility.DefaultThreatPointsNow(home));
            slate.Set("faction", asker);
            slate.Set("enemySettlement", enemySettlement);
            slate.Set("goodwillAmount", goodwill);

            Quest? quest;
            try
            {
                quest = QuestGen.Generate(def, slate);
            }
            catch (System.Exception e)
            {
                Log.Error("[WD] QuestGen.Generate failed for spy-ops settlement quest: " + e);
                return false;
            }

            if (quest == null)
                return false;

            bool hasTarget = false;
            List<QuestPart> parts = quest.PartsListForReading;
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i] is QuestPart_WdTrackedSettlement tracked
                    && tracked.settlement != null
                    && !tracked.settlement.Destroyed)
                {
                    hasTarget = true;
                    break;
                }
            }
            if (!hasTarget)
                return false;

            Find.QuestManager.Add(quest);
            QuestUtility.SendLetterQuestAvailable(quest);
            return true;
        }

        public static bool AnyActive()
        {
            QuestScriptDef? def = DefDatabase<QuestScriptDef>.GetNamedSilentFail(QuestDefName);
            if (def == null) return false;
            return Find.QuestManager.QuestsListForReading.Any(q => q.root == def && q.State == QuestState.Ongoing);
        }

        public static Quest? FindActiveQuest()
        {
            QuestScriptDef? def = DefDatabase<QuestScriptDef>.GetNamedSilentFail(QuestDefName);
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

        public static QuestPart_WdTrackedSettlement? FindActiveTrackedPart()
        {
            Quest? quest = FindActiveQuest();
            if (quest == null) return null;
            List<QuestPart> parts = quest.PartsListForReading;
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i] is QuestPart_WdTrackedSettlement tracked)
                    return tracked;
            }
            return null;
        }

        public static void CompleteIfActive()
        {
            Quest? quest = FindActiveQuest();
            if (quest == null) return;

            Faction? asker = TryGetAsker(quest);
            QuestPart_WdTrackedSettlement? part = FindActiveTrackedPart();
            int reward = part != null
                ? WdCommonEnemySettlementQuestHelper.GoodwillForTier(part.targetTier)
                : 10;

            Find.SignalManager.SendSignal(new Signal(
                $"Quest{quest.id}.{QuestNode_WdSpyOpsGoodwillReward.SuccessSignal}"));
            quest.End(QuestEndOutcome.Success);
            GameComponent_WdSpyOpsSettlementQuest.NotifyQuestEnded();
            if (asker != null)
                GoodwillChangeNotifier.NotifyQuestReward(asker, reward);
        }

        public static void FailIfActive()
        {
            Quest? quest = FindActiveQuest();
            if (quest == null) return;
            quest.End(QuestEndOutcome.Fail);
            GameComponent_WdSpyOpsSettlementQuest.NotifyQuestEnded();
        }

        public static void FailTargetGoneIfActive()
        {
            Quest? quest = FindActiveQuest();
            if (quest == null) return;

            QuestPart_WdTrackedSettlement? part = FindActiveTrackedPart();
            string settlementLabel = part?.settlement?.LabelCap
                ?? part?.settlementLabelFallback
                ?? "Settlement";
            Faction? asker = TryGetAsker(quest);

            Find.LetterStack.ReceiveLetter(
                "TSA_WD_SpyOps_LetterLabelTargetGone".Translate(quest.name),
                "TSA_WD_SpyOps_LetterTextTargetGone".Translate(
                    asker?.Name ?? "Faction",
                    settlementLabel),
                LetterDefOf.NegativeEvent,
                part?.settlement != null && !part.settlement.Destroyed
                    ? (LookTargets)part.settlement
                    : LookTargets.Invalid);

            quest.End(QuestEndOutcome.Fail);
            GameComponent_WdSpyOpsSettlementQuest.NotifyQuestEnded();
        }

        /// <summary>Successful sabotage or disinformation on the tracked town (strength damage is enough).</summary>
        public static void NotifySuccessfulSpyOp(Settlement? target)
        {
            if (target == null) return;
            QuestPart_WdTrackedSettlement? part = FindActiveTrackedPart();
            if (part == null || part.settlement != target)
                return;

            part.playerAttributed = true;
            CompleteIfActive();
        }

        public static void MonitorActiveQuestOutcome()
        {
            QuestPart_WdTrackedSettlement? part = FindActiveTrackedPart();
            if (part == null) return;

            Quest? quest = FindActiveQuest();
            Faction? asker = quest != null ? TryGetAsker(quest) : null;
            if (WdFirstOutpostQuestHelper.IsAskerHostileOrGone(asker))
            {
                FailIfActive();
                return;
            }

            Settlement? s = part.settlement;
            if (s == null || s.Destroyed)
            {
                if (part.playerAttributed)
                    CompleteIfActive();
                else
                    FailTargetGoneIfActive();
            }
        }

        public static Faction? TryGetAsker(Quest quest) => WdFirstOutpostQuestHelper.TryGetAsker(quest);

        public static bool TryPickAskerAndTarget(out Faction asker, out Settlement enemySettlement, out int goodwillAmount)
        {
            asker = null!;
            enemySettlement = null!;
            goodwillAmount = 10;

            if (!WdFirstOutpostQuestHelper.TryPickAsker(out asker))
                return false;

            if (!WdCommonEnemySettlementQuestHelper.TryPickCommonEnemySettlement(asker, out enemySettlement, out SettlementTier tier))
                return false;

            goodwillAmount = WdCommonEnemySettlementQuestHelper.GoodwillForTier(tier);
            return true;
        }
    }
}
