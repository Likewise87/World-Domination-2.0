using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    public static class DebugActions_Quests
    {
        public static void VictoryForceGenerateQuest()
        {
            var gc = WdWorldDominationVictoryQuestHelper.Comp;
            if (gc != null)
            {
                gc.alreadyWon = false;
                gc.permanentlyDone = false;
                gc.victoryDialogOpen = false;
            }

            if (WdWorldDominationVictoryQuestHelper.AnyActive())
            {
                Messages.Message("WD debug: victory quest already active.", MessageTypeDefOf.RejectInput);
                return;
            }

            if (WdWorldDominationVictoryQuestHelper.GenerateQuest(ignoreSetting: true))
                Messages.Message("WD debug: victory quest generated.", MessageTypeDefOf.TaskCompletion);
            else
                Messages.Message("WD debug: victory quest generate failed.", MessageTypeDefOf.RejectInput);
        }

        public static void VictoryStreak14()
        {
            if (!WdWorldDominationVictoryQuestHelper.AnyActive())
            {
                Messages.Message("WD debug: no active victory quest.", MessageTypeDefOf.RejectInput);
                return;
            }
            WdWorldDominationVictoryQuestHelper.SetHoldDaysStreakForDebug(14);
            Messages.Message("WD debug: victory streak = 14.", MessageTypeDefOf.TaskCompletion);
        }

        public static void VictoryForceComplete()
        {
            var gc = WdWorldDominationVictoryQuestHelper.Comp;
            if (gc != null)
            {
                gc.alreadyWon = false;
                gc.permanentlyDone = false;
                gc.victoryDialogOpen = false;
            }

            if (WdWorldDominationVictoryQuestHelper.AnyActive())
            {
                WdWorldDominationVictoryQuestHelper.CompleteIfActive();
                Messages.Message("WD debug: victory quest completed.", MessageTypeDefOf.TaskCompletion);
                return;
            }

            WdWorldDominationVictory.TryOpenVictoryDialog();
            Messages.Message("WD debug: opened victory dialog (no active quest).", MessageTypeDefOf.TaskCompletion);
        }

        public static void RoadBuildingForceGenerate()
        {
            if (WdColonyRoadLinkQuestHelper.AnyActive())
            {
                Messages.Message("WD debug: road-building quest already active.", MessageTypeDefOf.RejectInput);
                return;
            }
            if (WdColonyRoadLinkQuestHelper.GenerateQuest(ignoreSetting: true))
                Messages.Message("WD debug: road-building quest generated.", MessageTypeDefOf.TaskCompletion);
            else
                Messages.Message(
                    "WD debug: road-building quest generate failed (need colony + nearby ally/neutral town).",
                    MessageTypeDefOf.RejectInput);
        }

        public static void BuildOutpostForceGenerate()
        {
            if (WdFirstOutpostQuestHelper.AnyActive())
            {
                Messages.Message("WD debug: build-outpost quest already active.", MessageTypeDefOf.RejectInput);
                return;
            }
            if (WdFirstOutpostQuestHelper.GenerateQuest(ignoreSetting: true))
                Messages.Message("WD debug: build-outpost quest generated.", MessageTypeDefOf.TaskCompletion);
            else
                Messages.Message(
                    "WD debug: build-outpost quest generate failed (need nearby ally/neutral asker).",
                    MessageTypeDefOf.RejectInput);
        }

        public static void CaptureSettlementForceGenerate()
        {
            if (WdCommonEnemySettlementQuestHelper.AnyActive())
            {
                Messages.Message("WD debug: capture-settlement quest already active.", MessageTypeDefOf.RejectInput);
                return;
            }
            if (WdCommonEnemySettlementQuestHelper.GenerateQuest(ignoreSetting: true))
                Messages.Message("WD debug: capture-settlement quest generated.", MessageTypeDefOf.TaskCompletion);
            else
                Messages.Message(
                    "WD debug: capture-settlement quest generate failed (need asker + common enemy town).",
                    MessageTypeDefOf.RejectInput);
        }

        public static void RaidBiasClickedToPlayer()
        {
            Faction attacker = FindFactionAtMouseTile();
            if (attacker == null)
            {
                Messages.Message("WD debug: click an NPC settlement/outpost.", MessageTypeDefOf.RejectInput);
                return;
            }
            WdQuestRaidBias.Apply(attacker, Faction.OfPlayer, 15);
            Messages.Message($"WD debug: raid bias {attacker.Name} → player (15d).", MessageTypeDefOf.TaskCompletion);
        }

        public static void RaidBiasClearClicked()
        {
            Faction attacker = FindFactionAtMouseTile();
            if (attacker == null)
            {
                Messages.Message("WD debug: click an NPC settlement/outpost.", MessageTypeDefOf.RejectInput);
                return;
            }
            WdQuestRaidBias.Clear(attacker);
            Messages.Message($"WD debug: cleared raid bias for {attacker.Name}.", MessageTypeDefOf.TaskCompletion);
        }

        private static Faction FindFactionAtMouseTile()
        {
            int tile = GenWorld.MouseTile();
            if (tile < 0) return null;
            foreach (WorldObject wo in Find.WorldObjects.ObjectsAt(tile))
            {
                if (wo == null || wo.Destroyed || wo.Faction == null || wo.Faction.IsPlayer)
                    continue;
                if (wo is Settlement || wo is WorldObject_WD_Outpost)
                    return wo.Faction;
            }
            return null;
        }
    }
}
