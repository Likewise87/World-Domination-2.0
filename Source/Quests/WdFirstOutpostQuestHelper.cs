using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;

namespace TSA_WorldDomination
{
    public static class WdFirstOutpostQuestHelper
    {
        public const string QuestDefName = "TSA_WD_FirstOutpostIntro";
        public const int SuccessGoodwill = 10;

        /// <summary>Inclusive range of in-game days after colony start before the quest may be offered.</summary>
        public const int OfferAfterMinDays = 2;
        public const int OfferAfterMaxDays = 5;

        public static bool IsSettingEnabled()
        {
            return WorldDominationMod.settings == null
                || WorldDominationMod.settings.enableFirstOutpostQuest;
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

            if (!TryPickAsker(out _))
                return false;

            var slate = new Slate();
            Map? home = Find.AnyPlayerHomeMap;
            if (home != null)
                slate.Set("points", StorytellerUtility.DefaultThreatPointsNow(home));

            Quest? quest;
            try
            {
                quest = QuestGen.Generate(def, slate);
            }
            catch (System.Exception e)
            {
                Log.Error("[WD] QuestGen.Generate failed for first-outpost quest: " + e);
                return false;
            }

            if (quest == null)
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

        public static void CompleteIfActive()
        {
            QuestScriptDef? def = DefDatabase<QuestScriptDef>.GetNamedSilentFail(QuestDefName);
            if (def == null) return;

            foreach (Quest q in Find.QuestManager.QuestsListForReading)
            {
                if (q.root != def || q.State != QuestState.Ongoing)
                    continue;

                Faction? asker = TryGetAsker(q);
                // Fire reward signal first so QuestPart_FactionGoodwillChange / UI reward apply, then end.
                Find.SignalManager.SendSignal(new Signal($"Quest{q.id}.{QuestNode_WdFirstOutpostGoodwillReward.SuccessSignal}"));
                q.End(QuestEndOutcome.Success);
                if (asker != null)
                    GoodwillChangeNotifier.NotifyQuestReward(asker, SuccessGoodwill);
            }
        }

        public static void FailIfActive()
        {
            QuestScriptDef? def = DefDatabase<QuestScriptDef>.GetNamedSilentFail(QuestDefName);
            if (def == null) return;

            foreach (Quest q in Find.QuestManager.QuestsListForReading)
            {
                if (q.root == def && q.State == QuestState.Ongoing)
                    q.End(QuestEndOutcome.Fail);
            }
        }

        public static Faction? TryGetAsker(Quest quest)
        {
            if (quest == null) return null;

            foreach (Faction f in quest.InvolvedFactions)
            {
                if (f != null && !f.IsPlayer)
                    return f;
            }

            List<QuestPart> parts = quest.PartsListForReading;
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i] is QuestPart_ExtraFaction extra
                    && extra.extraFaction != null
                    && extra.extraFaction.faction != null
                    && !extra.extraFaction.faction.IsPlayer)
                    return extra.extraFaction.faction;
            }

            return null;
        }

        public static bool TryPickAsker(out Faction asker)
        {
            asker = null!;
            DailyWorldSnapshot snapshot = DailyWorldSnapshot.Build();
            if (snapshot.SettlementsByFaction == null)
                return false;

            Faction? player = Faction.OfPlayerSilentFail;
            if (player == null) return false;

            var allies = new List<Faction>();
            var neutrals = new List<Faction>();

            foreach (var kv in snapshot.SettlementsByFaction)
            {
                Faction f = kv.Key;
                if (f == null || f.IsPlayer || f.defeated || f.def == null || f.def.hidden)
                    continue;
                if (WorldActions_Utils.IsExcludedFaction(f))
                    continue;
                if (kv.Value == null || kv.Value.Count == 0)
                    continue;

                FactionRelationKind kind = WorldActions_Utils.SafeRelationKindWith(f, player);
                if (kind == FactionRelationKind.Ally)
                    allies.Add(f);
                else if (kind == FactionRelationKind.Neutral)
                    neutrals.Add(f);
            }

            if (allies.Count > 0)
            {
                asker = allies.RandomElement();
                return true;
            }

            if (neutrals.Count > 0)
            {
                asker = neutrals.RandomElement();
                return true;
            }

            return false;
        }

        public static bool HasAnyPlayerOutpost()
        {
            List<WorldObject_WD_Outpost> live = WorldStatsUtils.CollectPlayerOutposts();
            if (live == null) return false;
            for (int i = 0; i < live.Count; i++)
            {
                if (live[i] != null && !live[i].Destroyed)
                    return true;
            }
            return false;
        }

        public static bool IsAskerStillValid(Faction? asker)
        {
            if (asker == null || asker.defeated || asker.def == null || asker.def.hidden)
                return false;
            if (asker.IsPlayer)
                return false;
            if (WorldActions_Utils.IsExcludedFaction(asker))
                return false;
            return true;
        }

        public static bool IsAskerHostileOrGone(Faction? asker)
        {
            if (!IsAskerStillValid(asker) || asker == null)
                return true;
            Faction? player = Faction.OfPlayerSilentFail;
            if (player == null) return true;
            return WorldActions_Utils.SafeRelationKindWith(asker, player) == FactionRelationKind.Hostile;
        }
    }
}
