using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;

namespace TSA_WorldDomination
{
    public static class WdColonyRoadLinkQuestHelper
    {
        public const string QuestDefName = "TSA_WD_ColonyRoadLink";
        public const int FirstOfferAfterDays = 10;
        public const int TimeoutDays = 30;
        public const int CooldownDaysMin = 2;
        public const int CooldownDaysMax = 6;
        public const int GoodwillReward = 15;

        public static int FirstOfferAfterTicks => FirstOfferAfterDays * GenDate.TicksPerDay;
        public static int TimeoutTicks => TimeoutDays * GenDate.TicksPerDay;

        public static bool IsSettingEnabled()
        {
            return WorldDominationMod.settings == null
                || WorldDominationMod.settings.enableColonyRoadLinkQuest;
        }

        public static bool GenerateQuest()
        {
            if (!IsSettingEnabled())
                return false;

            var gc = Current.Game?.GetComponent<GameComponent_WdColonyRoadLinkQuest>();
            if (gc != null && gc.permanentlyDone)
                return false;

            QuestScriptDef? def = DefDatabase<QuestScriptDef>.GetNamedSilentFail(QuestDefName);
            if (def == null)
            {
                Log.Warning("[WD] Quest def missing: " + QuestDefName);
                return false;
            }

            if (!TryPickTargets(out Faction asker, out Settlement settlement, out int goodwill))
                return false;

            var slate = new Slate();
            Map? home = Find.AnyPlayerHomeMap;
            if (home != null)
                slate.Set("points", StorytellerUtility.DefaultThreatPointsNow(home));
            slate.Set("faction", asker);
            slate.Set("askerSettlement", settlement);
            slate.Set("goodwillAmount", goodwill);

            Quest? quest;
            try
            {
                quest = QuestGen.Generate(def, slate);
            }
            catch (System.Exception e)
            {
                Log.Error("[WD] QuestGen.Generate failed for colony road-link quest: " + e);
                return false;
            }

            if (quest == null)
                return false;

            // Never issue a quest without a resolved road-link target.
            bool hasTarget = false;
            List<QuestPart> parts = quest.PartsListForReading;
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i] is QuestPart_WdTrackedRoadLink tracked
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

        public static QuestPart_WdTrackedRoadLink? FindActiveTrackedPart()
        {
            Quest? quest = FindActiveQuest();
            if (quest == null) return null;
            List<QuestPart> parts = quest.PartsListForReading;
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i] is QuestPart_WdTrackedRoadLink tracked)
                    return tracked;
            }
            return null;
        }

        public static void CompleteIfActive()
        {
            Quest? quest = FindActiveQuest();
            if (quest == null) return;

            Faction? asker = TryGetAsker(quest);
            Find.SignalManager.SendSignal(new Signal(
                $"Quest{quest.id}.{QuestNode_WdColonyRoadLinkGoodwillReward.SuccessSignal}"));
            quest.End(QuestEndOutcome.Success);
            GameComponent_WdColonyRoadLinkQuest.NotifyQuestSucceeded();
            if (asker != null)
                GoodwillChangeNotifier.NotifyQuestReward(asker, GoodwillReward);
        }

        public static void FailIfActive(bool sendSettlementGoneLetter = false)
        {
            Quest? quest = FindActiveQuest();
            if (quest == null) return;

            if (sendSettlementGoneLetter)
            {
                QuestPart_WdTrackedRoadLink? part = FindActiveTrackedPart();
                string settlementLabel = part?.settlement?.LabelCap
                    ?? part?.settlementLabelFallback
                    ?? "Settlement";
                Faction? asker = TryGetAsker(quest) ?? part?.askerFaction;

                Find.LetterStack.ReceiveLetter(
                    "TSA_WD_ColonyRoadLink_LetterLabelSettlementGone".Translate(quest.name),
                    "TSA_WD_ColonyRoadLink_LetterTextSettlementGone".Translate(
                        asker?.Name ?? "Faction",
                        settlementLabel),
                    LetterDefOf.NegativeEvent,
                    LookTargets.Invalid);
            }

            quest.End(QuestEndOutcome.Fail);
            GameComponent_WdColonyRoadLinkQuest.NotifyQuestFailed();
        }

        public static void MonitorActiveQuestOutcome()
        {
            QuestPart_WdTrackedRoadLink? part = FindActiveTrackedPart();
            if (part == null) return;

            Quest? quest = FindActiveQuest();
            Faction? asker = quest != null ? TryGetAsker(quest) : part.askerFaction;
            if (WdFirstOutpostQuestHelper.IsAskerHostileOrGone(asker))
            {
                FailIfActive();
                return;
            }

            Settlement? s = part.settlement;
            bool gone = s == null || s.Destroyed;
            bool factionChanged = !gone
                && part.askerFaction != null
                && s!.Faction != part.askerFaction;

            if (gone || factionChanged)
            {
                FailIfActive(sendSettlementGoneLetter: true);
                return;
            }

            Settlement? colony = InfluenceUtils.GetPlayerColony();
            if (colony == null || colony.Destroyed)
                return;

            if (WdRoadQuestUtility.AreRoadConnected(colony, s))
                CompleteIfActive();
        }

        public static Faction? TryGetAsker(Quest quest)
        {
            return WdFirstOutpostQuestHelper.TryGetAsker(quest);
        }

        public static bool TryPickTargets(out Faction asker, out Settlement settlement, out int goodwillAmount)
        {
            asker = null!;
            settlement = null!;
            goodwillAmount = GoodwillReward;

            if (!WdRoadQuestUtility.TryPickQuestSettlement(out settlement))
                return false;

            asker = settlement.Faction!;
            goodwillAmount = GoodwillReward;
            return true;
        }
    }
}
