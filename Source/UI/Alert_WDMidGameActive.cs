using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Persistent alert while Mid-game escalation is active (not Late).
    /// </summary>
    public class Alert_WDMidGameActive : Alert
    {
        public Alert_WDMidGameActive()
        {
            defaultLabel = "TSA_WD_Alert_MidGameActive".Translate();
            defaultExplanation = "TSA_WD_Alert_MidGameActiveDesc".Translate();
            defaultPriority = AlertPriority.Medium;
        }

        public override AlertReport GetReport()
        {
            if (Current.ProgramState != ProgramState.Playing) return false;
            if (WorldDominationMod.settings == null || !WorldDominationMod.settings.notifyMidGameActive) return false;
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (manager == null) return false;

            return manager.cachedMidGameModifierActive;
        }
    }
}
