using RimWorld;
using Verse;

namespace TSA_WorldDomination
{
    public static class DebugActions_Escalation
    {

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
