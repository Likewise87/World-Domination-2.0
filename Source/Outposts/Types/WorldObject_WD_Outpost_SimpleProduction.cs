using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Simple production outpost: fixed item, baseline × cumulative skill (e.g. components, drugs). XML <c>productionOptions</c>; math and UI formulas live in <see cref="Outpost_Production_Utils"/> (preview uses <see cref="Outpost_Production_Utils.GetScalingSkillTotalForProductionPreview"/>, delivery uses <see cref="Outpost_Production_Utils.GetEligibleSkillForProduction"/>).</summary>
    public class WorldObject_WD_Outpost_SimpleProduction : WorldObject_WD_Outpost
    {
        // No overrides; all logic in base, Outpost_Production / Outpost_Production_Utils, and Dialog_OutpostProduction.
    }
}
