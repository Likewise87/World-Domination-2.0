using RimWorld;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Scoped bypass for Biotech mechanitor checks on temporary manual outpost defense maps.</summary>
    public static class WD_OutpostDefenseMechanoidControlUtil
    {
        private const string OutpostDefenseSiteDefName = "TSA_WD_OutpostDefenseSite";

        public static bool IsOutpostDefenseMap(Map map)
            => map?.Parent?.def?.defName == OutpostDefenseSiteDefName;

        public static bool ShouldBypassMechanitorControl(Pawn mech)
            => ModsConfig.BiotechActive
                && mech?.IsColonyMech == true
                && IsOutpostDefenseMap(mech.MapHeld);
    }
}
