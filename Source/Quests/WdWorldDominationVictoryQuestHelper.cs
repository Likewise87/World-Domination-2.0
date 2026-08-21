using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.QuestGen;
using Verse;

namespace TSA_WorldDomination
{
    public static class WdWorldDominationVictoryQuestHelper
    {
        public const string QuestDefName = "TSA_WD_WorldDominationVictory";
        public const string SuccessSignal = "WorldDominationVictorySuccess";

        public const int OfferAfterMinDays = 4;
        public const int OfferAfterMaxDays = 6;
        public const int ReofferCooldownMinDays = 5;
        public const int ReofferCooldownMaxDays = 10;

        public static bool IsSettingEnabled()
        {
            return WorldDominationMod.settings == null
                || WorldDominationMod.settings.enableWorldDominationVictoryQuest;
        }

        public static GameComponent_WdWorldDominationVictoryQuest? Comp =>
            Current.Game?.GetComponent<GameComponent_WdWorldDominationVictoryQuest>();

        public static bool AlreadyWon => Comp?.alreadyWon ?? false;

        public static bool GenerateQuest(bool ignoreSetting = false)
        {
            if (!ignoreSetting && !IsSettingEnabled())
                return false;

            var gc = Comp;
            if (gc != null && (gc.alreadyWon || gc.permanentlyDone))
                return false;

            if (AnyActive())
                return false;

            QuestScriptDef? def = DefDatabase<QuestScriptDef>.GetNamedSilentFail(QuestDefName);
            if (def == null)
            {
                Log.Warning("[WD] Quest def missing: " + QuestDefName);
                return false;
            }

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
                Log.Error("[WD] QuestGen.Generate failed for world-domination victory quest: " + e);
                return false;
            }

            if (quest == null)
                return false;

            Find.QuestManager.Add(quest);
            QuestUtility.SendLetterQuestAvailable(quest);

            if (gc != null)
            {
                gc.trackedQuestId = quest.id;
                gc.nextOfferTick = int.MaxValue;
            }

            return true;
        }

        /// <summary>Settings mid-game On: offer now if eligible (skips day 4–6 wait).</summary>
        public static bool TryLaunchNowIfEligible()
        {
            if (Current.ProgramState != ProgramState.Playing)
                return false;
            if (!IsSettingEnabled())
                return false;
            if (AlreadyWon)
                return false;
            if (AnyActive())
                return false;

            return GenerateQuest();
        }

        /// <summary>Settings mid-game Off: end ongoing quest without victory.</summary>
        public static void RemoveActiveIfAny()
        {
            QuestScriptDef? def = DefDatabase<QuestScriptDef>.GetNamedSilentFail(QuestDefName);
            if (def == null) return;

            var gc = Comp;
            foreach (Quest q in Find.QuestManager.QuestsListForReading)
            {
                if (q.root != def || q.State != QuestState.Ongoing)
                    continue;
                q.End(QuestEndOutcome.Fail);
            }

            if (gc != null && !gc.alreadyWon)
            {
                gc.trackedQuestId = -1;
                // Ready for immediate re-launch if the setting is turned back on.
                gc.nextOfferTick = Find.TickManager.TicksGame;
            }
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

        public static QuestPart_WdVictoryHold? FindActiveHoldPart()
        {
            Quest? quest = FindActiveQuest();
            if (quest == null) return null;
            List<QuestPart> parts = quest.PartsListForReading;
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i] is QuestPart_WdVictoryHold hold)
                    return hold;
            }
            return null;
        }

        /// <summary>Idempotent: ends the active victory quest and opens the keep-playing / credits dialog once.</summary>
        public static void CompleteIfActive()
        {
            var gc = Comp;
            if (gc != null && gc.alreadyWon)
                return;

            Quest? quest = FindActiveQuest();
            if (quest == null)
                return;

            if (gc != null)
            {
                gc.alreadyWon = true;
                gc.permanentlyDone = true;
                gc.trackedQuestId = -1;
                gc.nextOfferTick = int.MaxValue;
            }

            Find.SignalManager.SendSignal(new Signal($"Quest{quest.id}.{SuccessSignal}"));
            if (quest.State == QuestState.Ongoing)
                quest.End(QuestEndOutcome.Success);

            WdWorldDominationVictory.TryOpenVictoryDialog();
        }

        public static void SetHoldDaysStreakForDebug(int days)
        {
            QuestPart_WdVictoryHold? part = FindActiveHoldPart();
            if (part == null) return;
            part.holdDaysStreak = UnityEngine.Mathf.Clamp(days, 0, part.holdDaysRequired);
        }
    }
}
