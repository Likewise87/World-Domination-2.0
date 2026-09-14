using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Persistent on-screen alert while Late Game escalation is active.
    /// </summary>
    public class Alert_WDLateGameActive : Alert
    {
        public Alert_WDLateGameActive()
        {
            defaultLabel = "TSA_WD_Alert_LateGameActive".Translate();
            defaultPriority = AlertPriority.High;
        }

        public override string GetLabel() => "TSA_WD_Alert_LateGameActive".Translate();

        public override TaggedString GetExplanation()
        {
            var seth = WorldDominationMod.settings;
            string body = WdEscalation.BuildStageLetterText(seth, WdEscalationStage.Late);
            return string.IsNullOrEmpty(body)
                ? "TSA_WD_Alert_LateGameActiveDesc".Translate()
                : body;
        }

        public override AlertReport GetReport()
        {
            if (Current.ProgramState != ProgramState.Playing) return false;
            if (WorldDominationMod.settings == null || !WorldDominationMod.settings.notifyLateGameActive) return false;
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (manager == null) return false;

            return manager.cachedLateGameModifierActive;
        }
    }
}
