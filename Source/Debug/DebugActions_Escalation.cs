using LudeonTK;
using RimWorld;
using Verse;

namespace TSA_WorldDomination
{
    public static class DebugActions_Escalation
    {
        [DebugAction("World Domination", "Force Early Game difficulty",
            allowedGameStates = AllowedGameStates.PlayingOnWorld)]
        public static void ForceEarlyGame()
        {
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (manager == null)
            {
                Messages.Message("No world.", MessageTypeDefOf.RejectInput);
                return;
            }
            manager.DebugForceEscalationStage(WdEscalationStage.None);
            Messages.Message("TSA_WD_Escalation_StageEarly".Translate(), MessageTypeDefOf.NeutralEvent);
        }

        [DebugAction("World Domination", "Force Mid Game difficulty",
            allowedGameStates = AllowedGameStates.PlayingOnWorld)]
        public static void ForceMidGame()
        {
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (manager == null)
            {
                Messages.Message("No world.", MessageTypeDefOf.RejectInput);
                return;
            }
            manager.DebugForceEscalationStage(WdEscalationStage.Mid);
            Messages.Message(WdEscalation.StageLetterLabel(WdEscalationStage.Mid), MessageTypeDefOf.NeutralEvent);
        }

        [DebugAction("World Domination", "Force Late Game difficulty",
            allowedGameStates = AllowedGameStates.PlayingOnWorld)]
        public static void ForceLateGame()
        {
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (manager == null)
            {
                Messages.Message("No world.", MessageTypeDefOf.RejectInput);
                return;
            }
            manager.DebugForceEscalationStage(WdEscalationStage.Late);
            Messages.Message(WdEscalation.StageLetterLabel(WdEscalationStage.Late), MessageTypeDefOf.NeutralEvent);
        }
    }
}
