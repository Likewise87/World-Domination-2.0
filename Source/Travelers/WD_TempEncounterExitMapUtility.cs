using RimWorld;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Shared gate for suppressing vanilla drafted map-edge auto-caravan exit on WD temporary fight maps.
    /// Colony homes and NPC settlement attack maps are unaffected.
    /// </summary>
    public static class WD_TempEncounterExitMapUtility
    {
        private const int BlockedMessageThrottleTicks = 300;
        private static int lastBlockedMessageTick = -999999;

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
        /// Player-faction pawns only. Enemies may still leave via their own exit toils.
        /// </summary>
        public static bool ShouldBlockPawnExit(Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned) return false;
            if (pawn.Faction == null || !pawn.Faction.IsPlayer) return false;
            return BlocksPlayerEdgeExit(pawn.Map);
        }

        public static void NotifyBlocked(Pawn pawn)
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            if (now - lastBlockedMessageTick < BlockedMessageThrottleTicks)
                return;
            lastBlockedMessageTick = now;
            Messages.Message(
                "TSA_WD_TempEncounter_EdgeExitBlocked".Translate(),
                pawn,
                MessageTypeDefOf.RejectInput,
                historical: false);
        }

        /// <summary>
        /// WD clash Ambush maps own win/loss messaging. Presence of the tracker (not only encounterActive)
        /// so vanilla <c>CaravansBattlefield.CheckWonBattle</c> cannot fire before the raid arrives or after WD wins.
        /// </summary>
        public static bool SuppressesVanillaAmbushWonBattle(Map map)
            => map != null && map.GetComponent<WD_MapComponent_CaravanClash>() != null;
    }
}
