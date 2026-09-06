using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Shared gate for suppressing vanilla drafted map-edge auto-caravan exit on WD temporary fight maps.
    /// Colony homes and NPC settlement attack maps are unaffected.
    /// </summary>
    public static class WD_TempEncounterExitMapUtility
    {
        public static bool BlocksPlayerEdgeExit(Map map)
        {
            if (map == null) return false;

            WD_MapComponent_CaravanClash clash = map.GetComponent<WD_MapComponent_CaravanClash>();
            if (clash != null && clash.BlocksPlayerEdgeExit)
                return true;

            WD_MapComponent_OutpostDefense defense = map.GetComponent<WD_MapComponent_OutpostDefense>();
            if (defense != null && defense.BlocksPlayerEdgeExit)
                return true;

            return false;
        }

        /// <summary>
        /// WD clash Ambush maps own win/loss messaging. Presence of the tracker (not only encounterActive)
        /// so vanilla <c>CaravansBattlefield.CheckWonBattle</c> cannot fire before the raid arrives or after WD wins.
        /// </summary>
        public static bool SuppressesVanillaAmbushWonBattle(Map map)
            => map != null && map.GetComponent<WD_MapComponent_CaravanClash>() != null;
    }
}
