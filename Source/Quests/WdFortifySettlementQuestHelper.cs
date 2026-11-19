using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Early one-shot tutorial: successful Fortify on an asker's settlement (ally/neutral).
    /// </summary>
    public static class WdFortifySettlementQuestHelper
    {
        public const string QuestDefName = "TSA_WD_FortifySettlement";
        public const int OfferDelayMinDays = 1;
        public const int OfferDelayMaxDays = 4;
        public const int OfferWhenSpyOpsOffMinDays = 12;
        public const int OfferWhenSpyOpsOffMaxDays = 18;
        public const int TimeoutDays = 8;
        public const int MaxTargetDistanceTiles = 50;

        public static bool IsSettingEnabled()
        {
            return WorldDominationMod.settings == null
                || WorldDominationMod.settings.enableFortifySettlementQuest;
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

            if (!TryPickAskerAndTarget(out Faction asker, out Settlement allySettlement, out int goodwill))
                return false;

            var slate = new Slate();
            Map? home = Find.AnyPlayerHomeMap;
            if (home != null)
                slate.Set("points", StorytellerUtility.DefaultThreatPointsNow(home));
            slate.Set("faction", asker);
            slate.Set("allySettlement", allySettlement);
            slate.Set("goodwillAmount", goodwill);

            Quest? quest;
            try
            {
                quest = QuestGen.Generate(def, slate);
            }
            catch (System.Exception e)
            {
                Log.Error("[WD] QuestGen.Generate failed for fortify settlement quest: " + e);
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
                $"Quest{quest.id}.{QuestNode_WdFortifyGoodwillReward.SuccessSignal}"));
            quest.End(QuestEndOutcome.Success);
            GameComponent_WdFortifySettlementQuest.NotifyQuestEnded();
            if (asker != null)
                GoodwillChangeNotifier.NotifyQuestReward(asker, reward);
        }

        public static void FailIfActive()
        {
            Quest? quest = FindActiveQuest();
            if (quest == null) return;
            quest.End(QuestEndOutcome.Fail);
            GameComponent_WdFortifySettlementQuest.NotifyQuestEnded();
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
                "TSA_WD_FortifyQuest_LetterLabelTargetGone".Translate(quest.name),
                "TSA_WD_FortifyQuest_LetterTextTargetGone".Translate(
                    asker?.Name ?? "Faction",
                    settlementLabel),
                LetterDefOf.NegativeEvent,
                part?.settlement != null && !part.settlement.Destroyed
                    ? (LookTargets)part.settlement
                    : LookTargets.Invalid);

            quest.End(QuestEndOutcome.Fail);
            GameComponent_WdFortifySettlementQuest.NotifyQuestEnded();
        }

        public static void NotifySuccessfulFortify(Settlement? target)
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
                return;
            }

            // Asker's town must stay non-hostile to the player.
            Faction? player = Faction.OfPlayerSilentFail;
            if (player != null && s.Faction != null && WorldActions_Utils.SafeHostileTo(s.Faction, player))
                FailIfActive();
        }

        public static Faction? TryGetAsker(Quest quest) => WdFirstOutpostQuestHelper.TryGetAsker(quest);

        public static bool TryPickAskerAndTarget(out Faction asker, out Settlement allySettlement, out int goodwillAmount)
        {
            asker = null!;
            allySettlement = null!;
            goodwillAmount = 10;

            if (!WdFirstOutpostQuestHelper.TryPickAsker(out asker))
                return false;

            if (!TryPickAskerSettlement(asker, out allySettlement, out SettlementTier tier))
                return false;

            goodwillAmount = WdCommonEnemySettlementQuestHelper.GoodwillForTier(tier);
            return true;
        }

        public static bool TryPickAskerSettlement(Faction asker, out Settlement chosen, out SettlementTier tier)
        {
            chosen = null!;
            tier = SettlementTier.T1;

            Faction? player = Faction.OfPlayerSilentFail;
            if (player == null || asker == null) return false;

            PlanetTile originTile = WdCommonEnemySettlementQuestHelper.GetPlayerHomeTile();
            if (!originTile.Valid) return false;

            Settlement best = null;
            SettlementTier bestTier = SettlementTier.T1;
            int bestHops = int.MaxValue;
            float bestApprox = float.MaxValue;

            List<Settlement> settlements = Find.WorldObjects.Settlements;
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement s = settlements[i];
                if (s == null || s.Destroyed || s.Faction != asker)
                    continue;
                if (!WorldActions_Utils.IsWdSurfaceWorldObject(s))
                    continue;
                if (s.GetComponent<CompViralSpread>() == null)
                    continue;
                if (WorldActions_Utils.SafeHostileTo(s.Faction, player))
                    continue;

                float approx = Find.WorldGrid.ApproxDistanceInTiles(originTile, s.Tile);
                if (approx > MaxTargetDistanceTiles)
                    continue;

                if (!WdRoadQuestUtility.TryGetSaneLandPath(originTile, s.Tile, out int hopCount, out _))
                    continue;

                if (hopCount > bestHops)
                    continue;
                if (hopCount == bestHops && approx >= bestApprox)
                    continue;

                best = s;
                bestTier = s.GetComponent<CompViralSpread>()?.tier ?? SettlementTier.T1;
                bestHops = hopCount;
                bestApprox = approx;
            }

            if (best == null)
                return false;

            chosen = best;
            tier = bestTier;
            return true;
        }
    }
}
