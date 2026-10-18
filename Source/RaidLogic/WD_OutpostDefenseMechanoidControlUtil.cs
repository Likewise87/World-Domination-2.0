using RimWorld;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Scoped bypass for Biotech mechanitor checks on WD temporary fight maps
    /// (manual outpost defense + active caravan/camp/bridge clash). Not bare vanilla Ambush.
    /// </summary>
    public static class WD_OutpostDefenseMechanoidControlUtil
    {
        private const string OutpostDefenseSiteDefName = "TSA_WD_OutpostDefenseSite";

        public static bool IsOutpostDefenseMap(Map map)
            => map?.Parent?.def?.defName == OutpostDefenseSiteDefName;

        public static bool IsWdTemporaryFightMap(Map map)
        {
            if (map == null) return false;
            if (IsOutpostDefenseMap(map)) return true;
            WD_MapComponent_CaravanClash clash = map.GetComponent<WD_MapComponent_CaravanClash>();
            return clash != null && clash.EncounterActive;
        }

        public static bool ShouldBypassMechanitorControl(Pawn mech)
        {
            if (!ModsConfig.BiotechActive || mech == null || mech.Destroyed || mech.Dead)
                return false;
            if (mech.Faction != Faction.OfPlayer)
                return false;
            if (mech.RaceProps == null || !mech.RaceProps.IsMechanoid)
                return false;
            return IsWdTemporaryFightMap(mech.MapHeld);
        }
    }
}
