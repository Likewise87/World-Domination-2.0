using LudeonTK;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    public static class DebugActions_Quests
    {
        [DebugAction("World Domination", "Victory: Force generate quest",
            allowedGameStates = AllowedGameStates.PlayingOnWorld)]
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

        [DebugAction("World Domination", "Victory: Set streak to 14",
            allowedGameStates = AllowedGameStates.PlayingOnWorld)]
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

        [DebugAction("World Domination", "Victory: Force complete (open dialog)",
            allowedGameStates = AllowedGameStates.PlayingOnWorld)]
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

        [DebugAction("World Domination", "Quest raid bias: clicked faction → player (15d)",
            actionType = DebugActionType.ToolWorld,
            allowedGameStates = AllowedGameStates.PlayingOnWorld)]
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

        [DebugAction("World Domination", "Quest raid bias: clear clicked faction",
            actionType = DebugActionType.ToolWorld,
            allowedGameStates = AllowedGameStates.PlayingOnWorld)]
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

        [DebugAction("World Domination", "Quest goodwill: -20 from clicked faction",
            actionType = DebugActionType.ToolWorld,
            allowedGameStates = AllowedGameStates.PlayingOnWorld)]
        public static void GoodwillLossClicked()
        {
            Faction asker = FindFactionAtMouseTile();
            if (asker == null)
            {
                Messages.Message("WD debug: click an NPC settlement/outpost.", MessageTypeDefOf.RejectInput);
                return;
            }
            if (WdQuestGoodwillPenalties.ApplyLossOnQuestFailed(asker))
                Messages.Message($"WD debug: applied -20 goodwill vs {asker.Name}.", MessageTypeDefOf.TaskCompletion);
            else
                Messages.Message("WD debug: goodwill change rejected.", MessageTypeDefOf.RejectInput);
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
