using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Persistent (non-critical, white) on-screen alert shown while the late-game difficulty modifier
    /// is active. Not a letter; it stays on the right-side alert readout like vanilla "Need recreation".
    /// </summary>
    public class Alert_WDLateGameActive : Alert
    {
        public Alert_WDLateGameActive()
        {
            defaultLabel = "TSA_WD_Alert_LateGameActive".Translate();
            defaultExplanation = "TSA_WD_Alert_LateGameActiveDesc".Translate();
            defaultPriority = AlertPriority.High;
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
