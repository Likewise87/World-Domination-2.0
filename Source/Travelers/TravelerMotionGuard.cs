using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Prevents live travelers from sitting forever with <c>pather.moving == false</c> when not in an
    /// intentional park (attrition rest, rally host wait, RR lead wait, deferred outpost defense, etc.).
    /// </summary>
    public static class TravelerMotionGuard
    {
        public const int StrandedHealHashInterval = 600;

        /// <summary>True when a live traveler may legitimately remain stationary.</summary>
        public static bool IsIntentionallyStationary(WorldObject_Traveler traveler)
        {
            if (traveler == null || traveler.Destroyed)
                return true;

            if (traveler.attritionResting)
                return true;
            if (traveler.desperationIsHost)
                return true;
            if (traveler.antiAirLeadFlight)
                return true;

            if (traveler.mission == TravelerMission.RapidResponseIntercept
                || traveler.mission == TravelerMission.RaidBribe)
                return true;

            if (WdPostLoadGuard.ShouldDeferTravelerArrival()
                && (WorldObject_Traveler.IsRaidMission(traveler.mission)
                    || traveler.mission == TravelerMission.MortarStrike
                    || traveler.mission == TravelerMission.AntiAirStrike))
                return true;

            if (traveler.targetObject is WorldObject_WD_Outpost outpost
                && !outpost.Destroyed
                && outpost.Tile == traveler.Tile
                && outpost.BlocksAutoRaidResolution())
                return true;

            if (WD_MapComponent_CaravanClash.TileHasBusyCampClash(traveler.Tile))
                return true;

            return false;
        }

        /// <summary>
        /// Prefer live <see cref="WorldObject_Traveler.targetObject"/> tile over a stale pather dest
        /// (e.g. choke retarget left dest pointing at a destroyed settlement).
        /// </summary>
        public static PlanetTile ResolveResumeDest(WorldObject_Traveler traveler)
        {
            if (traveler == null)
                return PlanetTile.Invalid;

            if (TravelerEndpointUtility.IsLiveEndpoint(traveler.targetObject))
                return PlanetSurfaceWorldActions.PlanetTileForWdTravel(traveler.targetObject.Tile, traveler);

            if (traveler.pather != null && traveler.pather.destTile.Valid)
                return traveler.pather.destTile;

            if (traveler.attritionRestDestTile >= 0)
                return PlanetSurfaceWorldActions.PlanetTileForWdTravel(traveler.attritionRestDestTile, traveler);

            return PlanetTile.Invalid;
        }

        /// <summary>
        /// Repath or abort a stranded non-moving traveler. No-op while intentionally stationary.
        /// </summary>
        public static void TryHealStrandedMotion(WorldObject_Traveler traveler)
        {
            if (traveler == null || traveler.Destroyed)
                return;
            if (WorldObject_Traveler.IsShellMission(traveler.mission))
                return;
            if (traveler.pather == null)
                return;
            if (traveler.pather.moving)
                return;
            if (traveler.spawnTick == Find.TickManager.TicksGame)
                return;
            if (IsIntentionallyStationary(traveler))
                return;

            PlanetTile dest = ResolveResumeDest(traveler);
            if (!dest.Valid)
            {
                AbortStranded(traveler);
                return;
            }

            if (traveler.pather.destTile != dest)
                traveler.pather.destTile = dest;

            traveler.pather.StartPath(dest, skipLaunchTravelCache: true);
            if (traveler.Destroyed)
                return;
            if (traveler.pather != null && !traveler.pather.moving && !IsIntentionallyStationary(traveler))
                AbortStranded(traveler);
        }

        private static void AbortStranded(WorldObject_Traveler traveler)
        {
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            TravelerEndpointUtility.AbortTraveler(
                traveler,
                "TSA_WD_Log_TravelerAborted_Stranded".Translate(traveler.Label),
                manager);
        }
    }
}
