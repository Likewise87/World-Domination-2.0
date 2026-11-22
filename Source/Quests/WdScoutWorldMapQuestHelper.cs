using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// One-shot FoW tutorial: launch a player scout traveler on the world map.
    /// </summary>
    public static class WdScoutWorldMapQuestHelper
    {
        public const string QuestDefName = "TSA_WD_ScoutWorldMap";
        public const string ThoughtDefName = "TSA_WD_StartedScouting";
        public const int OfferAfterMinDays = 1;
        public const int OfferAfterMaxDays = 5;
        public const int TimeoutDays = 10;
        public const float RevealRadiusTiles = 6f;

        public static int TimeoutTicks => TimeoutDays * GenDate.TicksPerDay;

        public static bool IsSettingEnabled()
        {
            return WorldDominationMod.settings == null
                || WorldDominationMod.settings.enableScoutWorldMapQuest;
        }

        public static bool GenerateQuest(bool ignoreSetting = false)
        {
            if (!ignoreSetting && !IsSettingEnabled())
                return false;

            if (!WorldComponent_SettlementIntel.IsTracking() && !ignoreSetting)
                return false;

            QuestScriptDef? def = DefDatabase<QuestScriptDef>.GetNamedSilentFail(QuestDefName);
            if (def == null)
            {
                Log.Warning("[WD] Quest def missing: " + QuestDefName);
                return false;
            }

            if (!TryPickClosestAskerSettlement(out Faction asker, out Settlement askerSettlement))
                return false;

            var slate = new Slate();
            Map? home = Find.AnyPlayerHomeMap;
            if (home != null)
                slate.Set("points", StorytellerUtility.DefaultThreatPointsNow(home));
            slate.Set("faction", asker);
            slate.Set("askerSettlement", askerSettlement);

            Quest? quest;
            try
            {
                quest = QuestGen.Generate(def, slate);
            }
            catch (System.Exception e)
            {
                Log.Error("[WD] QuestGen.Generate failed for scout world map quest: " + e);
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

            RevealAskerSettlement(askerSettlement);

            Find.QuestManager.Add(quest);
            QuestUtility.SendLetterQuestAvailable(quest);
            return true;
        }

        public static void RevealAskerSettlement(Settlement? settlement)
        {
            WdPlayerIntel.RevealSettlementToPlayer(settlement);
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

            GrantStartedScoutingThought();
            quest.End(QuestEndOutcome.Success);
            GameComponent_WdScoutWorldMapQuest.NotifyQuestEnded();
        }

        public static void FailIfActive()
        {
            Quest? quest = FindActiveQuest();
            if (quest == null) return;
            quest.End(QuestEndOutcome.Fail);
            GameComponent_WdScoutWorldMapQuest.NotifyQuestEnded();
        }

        public static void FailAskerGoneIfActive()
        {
            Quest? quest = FindActiveQuest();
            if (quest == null) return;

            QuestPart_WdTrackedSettlement? part = FindActiveTrackedPart();
            string settlementLabel = part?.settlement?.LabelCap
                ?? part?.settlementLabelFallback
                ?? "Settlement";
            Faction? asker = TryGetAsker(quest);

            Find.LetterStack.ReceiveLetter(
                "TSA_WD_ScoutWorldMap_LetterLabelAskerGone".Translate(quest.name),
                "TSA_WD_ScoutWorldMap_LetterTextAskerGone".Translate(
                    asker?.Name ?? "Faction",
                    settlementLabel),
                LetterDefOf.NegativeEvent,
                part?.settlement != null && !part.settlement.Destroyed
                    ? (LookTargets)part.settlement
                    : LookTargets.Invalid);

            quest.End(QuestEndOutcome.Fail);
            GameComponent_WdScoutWorldMapQuest.NotifyQuestEnded();
        }

        public static void NotifyPlayerScoutLaunched()
        {
            if (!AnyActive()) return;
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
                FailAskerGoneIfActive();
                return;
            }

            if (HasLivePlayerScoutTraveler())
                CompleteIfActive();
        }

        public static Faction? TryGetAsker(Quest quest) => WdFirstOutpostQuestHelper.TryGetAsker(quest);

        /// <summary>
        /// Closest ally/neutral WD settlement by approx distance; Ally wins ties; sane land path is tertiary.
        /// Asker may be fogged until issue (no discovery filter).
        /// </summary>
        public static bool TryPickClosestAskerSettlement(out Faction asker, out Settlement askerSettlement)
        {
            asker = null!;
            askerSettlement = null!;

            Faction? player = Faction.OfPlayerSilentFail;
            if (player == null) return false;

            PlanetTile originTile = WdCommonEnemySettlementQuestHelper.GetPlayerHomeTile();
            if (!originTile.Valid) return false;

            Settlement best = null;
            float bestApprox = float.MaxValue;
            int bestRelationRank = int.MaxValue;
            int bestHops = int.MaxValue;

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
                if (!WorldActions_Utils.IsWdParticipant(s.Faction))
                    continue;

                FactionRelationKind kind = WorldActions_Utils.SafeRelationKindWith(s.Faction, player);
                if (kind != FactionRelationKind.Ally && kind != FactionRelationKind.Neutral)
                    continue;

                float approx = Find.WorldGrid.ApproxDistanceInTiles(originTile, s.Tile);
                int relationRank = kind == FactionRelationKind.Ally ? 0 : 1;

                bool hasPath = WdRoadQuestUtility.TryGetSaneLandPath(originTile, s.Tile, out int hopCount, out _);
                if (!hasPath)
                    hopCount = int.MaxValue / 4;

                if (approx > bestApprox)
                    continue;
                if (approx < bestApprox)
                {
                    best = s;
                    bestApprox = approx;
                    bestRelationRank = relationRank;
                    bestHops = hopCount;
                    continue;
                }

                // approx equal
                if (relationRank > bestRelationRank)
                    continue;
                if (relationRank < bestRelationRank)
                {
                    best = s;
                    bestApprox = approx;
                    bestRelationRank = relationRank;
                    bestHops = hopCount;
                    continue;
                }

                if (hopCount >= bestHops)
                    continue;

                best = s;
                bestApprox = approx;
                bestRelationRank = relationRank;
                bestHops = hopCount;
            }

            if (best == null)
                return false;

            askerSettlement = best;
            asker = best.Faction!;
            return true;
        }

        public static bool HasLivePlayerScoutTraveler()
        {
            List<WorldObject> all = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is not WorldObject_Traveler traveler || traveler.Destroyed)
                    continue;
                if (traveler.Faction == null || !traveler.Faction.IsPlayer)
                    continue;
                if (traveler.mission != TravelerMission.Scout)
                    continue;
                if (traveler.playerScoutExpireTick > 0)
                    return true;
            }
            return false;
        }

        /// <summary>True if the player already launched a scout (live traveler with expire clock).</summary>
        public static bool PlayerAlreadyLaunchedScout()
        {
            return HasLivePlayerScoutTraveler();
        }

        public static void GrantStartedScoutingThought()
        {
            ThoughtDef? thought = DefDatabase<ThoughtDef>.GetNamedSilentFail(ThoughtDefName);
            if (thought == null) return;

            HashSet<int> granted = new HashSet<int>();

            void TryGive(Pawn? p)
            {
                if (p == null || p.Destroyed || p.Dead) return;
                if (!p.RaceProps.Humanlike) return;
                if (p.Faction == null || !p.Faction.IsPlayer) return;
                if (p.IsPrisoner || p.IsSlave) return;
                if (p.needs?.mood?.thoughts?.memories == null) return;
                if (!granted.Add(p.thingIDNumber)) return;
                p.needs.mood.thoughts.memories.TryGainMemory(thought);
            }

            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                Map map = maps[i];
                if (map?.mapPawns == null) continue;
                List<Pawn> free = map.mapPawns.FreeColonists;
                for (int j = 0; j < free.Count; j++)
                    TryGive(free[j]);
            }

            List<Caravan> caravans = Find.WorldObjects.Caravans;
            for (int i = 0; i < caravans.Count; i++)
            {
                Caravan c = caravans[i];
                if (c == null || c.Destroyed || c.Faction == null || !c.Faction.IsPlayer)
                    continue;
                List<Pawn> pawns = c.PawnsListForReading;
                for (int j = 0; j < pawns.Count; j++)
                    TryGive(pawns[j]);
            }

            List<WorldObject_WD_Outpost> outposts = WorldStatsUtils.CollectPlayerOutposts();
            for (int i = 0; i < outposts.Count; i++)
            {
                WorldObject_WD_Outpost o = outposts[i];
                if (o == null || o.Destroyed) continue;
                List<Pawn> occupants = o.Occupants;
                for (int j = 0; j < occupants.Count; j++)
                    TryGive(occupants[j]);
            }
        }
    }
}
