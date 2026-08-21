using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;

namespace TSA_WorldDomination
{
    public static class WdCommonEnemySettlementQuestHelper
    {
        public const string QuestDefName = "TSA_WD_CommonEnemySettlement";
        public const int FirstOfferAfterDays = 10;
        public const int TimeoutDays = 8;
        public const int CooldownDaysMin = 20;
        public const int CooldownDaysMax = 50;
        public const int MaxTargetDistanceTiles = 50;

        public static int FirstOfferAfterTicks => FirstOfferAfterDays * GenDate.TicksPerDay;
        public static int TimeoutTicks => TimeoutDays * GenDate.TicksPerDay;

        public static int GoodwillForTier(SettlementTier tier)
        {
            switch (tier)
            {
                case SettlementTier.T2: return 15;
                case SettlementTier.T3: return 22;
                case SettlementTier.T4: return 30;
                default: return 10;
            }
        }

        public static bool IsSettingEnabled()
        {
            return WorldDominationMod.settings == null
                || WorldDominationMod.settings.enableCommonEnemySettlementQuest;
        }

        public static bool GenerateQuest()
        {
            if (!IsSettingEnabled())
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
                Log.Error("[WD] QuestGen.Generate failed for common-enemy settlement quest: " + e);
                return false;
            }

            if (quest == null)
                return false;

            // Never issue a quest without a resolved common-enemy target.
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
            // Always send the blue available letter (same as first-outpost / road-link).
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
            int reward = part != null ? GoodwillForTier(part.targetTier) : 10;

            Find.SignalManager.SendSignal(new Signal(
                $"Quest{quest.id}.{QuestNode_WdCommonEnemyGoodwillReward.SuccessSignal}"));
            quest.End(QuestEndOutcome.Success);
            GameComponent_WdCommonEnemySettlementQuest.NotifyQuestEnded();
            if (asker != null)
                GoodwillChangeNotifier.NotifyQuestReward(asker, reward);
        }

        public static void FailIfActive()
        {
            Quest? quest = FindActiveQuest();
            if (quest == null) return;
            quest.End(QuestEndOutcome.Fail);
            GameComponent_WdCommonEnemySettlementQuest.NotifyQuestEnded();
        }

        public static void FailTakenByOthersIfActive()
        {
            Quest? quest = FindActiveQuest();
            if (quest == null) return;

            QuestPart_WdTrackedSettlement? part = FindActiveTrackedPart();
            string settlementLabel = part?.settlement?.LabelCap
                ?? part?.settlementLabelFallback
                ?? "Settlement";
            Faction? asker = TryGetAsker(quest);

            Find.LetterStack.ReceiveLetter(
                "TSA_WD_CommonEnemy_LetterLabelTakenByOthers".Translate(quest.name),
                "TSA_WD_CommonEnemy_LetterTextTakenByOthers".Translate(
                    asker?.Name ?? "Faction",
                    settlementLabel),
                LetterDefOf.NegativeEvent,
                part?.settlement != null && !part.settlement.Destroyed
                    ? (LookTargets)part.settlement
                    : LookTargets.Invalid);

            quest.End(QuestEndOutcome.Fail);
            GameComponent_WdCommonEnemySettlementQuest.NotifyQuestEnded();
        }

        public static void NotifyPlayerAttributedStrike(Settlement? target)
        {
            if (target == null) return;
            QuestPart_WdTrackedSettlement? part = FindActiveTrackedPart();
            if (part == null || part.settlement != target)
                return;
            part.playerAttributed = true;
        }

        /// <summary>
        /// Call just before / when a quest target settlement is wiped or conquered.
        /// Player-attributed strikes succeed; otherwise someone else got there first.
        /// </summary>
        public static void NotifySettlementRemoved(Settlement? target)
        {
            if (target == null) return;
            QuestPart_WdTrackedSettlement? part = FindActiveTrackedPart();
            if (part == null || part.settlement != target)
                return;

            if (part.playerAttributed)
                CompleteIfActive();
            else
                FailTakenByOthersIfActive();
        }

        /// <summary>
        /// On-map player defeat of a settlement: attribute + resolve common-enemy quest with one tracked-part lookup.
        /// </summary>
        public static void NotifyPlayerDefeatOfTrackedSettlement(Settlement? target)
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
            bool gone = s == null || s.Destroyed;
            bool factionChanged = !gone
                && part.originalEnemyFaction != null
                && s!.Faction != part.originalEnemyFaction;

            if (!gone && !factionChanged)
                return;

            if (part.playerAttributed)
                CompleteIfActive();
            else
                FailTakenByOthersIfActive();
        }

        public static Faction? TryGetAsker(Quest quest)
        {
            return WdFirstOutpostQuestHelper.TryGetAsker(quest);
        }

        public static bool TryPickAskerAndTarget(out Faction asker, out Settlement enemySettlement, out int goodwillAmount)
        {
            asker = null!;
            enemySettlement = null!;
            goodwillAmount = 10;

            if (!WdFirstOutpostQuestHelper.TryPickAsker(out asker))
                return false;

            if (!TryPickCommonEnemySettlement(asker, out enemySettlement, out SettlementTier tier))
                return false;

            goodwillAmount = GoodwillForTier(tier);
            return true;
        }

        public static bool TryPickCommonEnemySettlement(Faction asker, out Settlement chosen, out SettlementTier tier)
        {
            chosen = null!;
            tier = SettlementTier.T1;

            Faction? player = Faction.OfPlayerSilentFail;
            if (player == null || asker == null) return false;

            PlanetTile originTile = GetPlayerHomeTile();
            if (!originTile.Valid) return false;

            Settlement best = null;
            SettlementTier bestTier = SettlementTier.T1;
            int bestHops = int.MaxValue;
            float bestApprox = float.MaxValue;

            List<Settlement> settlements = Find.WorldObjects.Settlements;
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement s = settlements[i];
                if (s == null || s.Destroyed || s.Faction == null || s.Faction.IsPlayer)
                    continue;
                if (!WorldActions_Utils.IsWdSurfaceWorldObject(s))
                    continue;
                if (s.GetComponent<CompViralSpread>() == null)
                    continue;
                if (!WorldActions_Utils.SafeHostileTo(s.Faction, player))
                    continue;
                if (!WorldActions_Utils.SafeHostileTo(s.Faction, asker))
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

        private static PlanetTile GetPlayerHomeTile()
        {
            Settlement? colony = InfluenceUtils.GetPlayerColony();
            if (colony != null && !colony.Destroyed && colony.Tile.Valid)
                return colony.Tile;

            Map? home = Find.AnyPlayerHomeMap;
            if (home != null && home.Tile.Valid)
                return home.Tile;

            List<Settlement> settlements = Find.WorldObjects.Settlements;
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement s = settlements[i];
                if (s != null && !s.Destroyed && s.Faction != null && s.Faction.IsPlayer
                    && WorldActions_Utils.IsWdSurfaceWorldObject(s))
                    return s.Tile;
            }

            return PlanetTile.Invalid;
        }
    }
}
