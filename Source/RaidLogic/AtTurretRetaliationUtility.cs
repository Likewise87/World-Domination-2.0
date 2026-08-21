using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Always-on AT Turret opportunity magnet (radius = fire range) and post-hit retaliation:
    /// hostile raid travelers save/restore their original target, detour to the turret, clash with open-field
    /// raid math on arrival, then resume. At most one proximity pull and
    /// <see cref="MaxHitDetours"/> post-hit pulls per raid traveler. Same-tile walk-overs also clash.
    /// Never uses permanent settlement ToO <c>ApplyRetarget</c>.
    /// </summary>
    public static class AtTurretRetaliationUtility
    {
        /// <summary>Fallback magnet radius when settings are unavailable (Medium default).</summary>
        public const float OpportunityRadiusTiles = WorldObject_AT_Turret.DefaultRangeTiles;

        /// <summary>Max save/restore detours started after surviving an AT shell hit.</summary>
        public const int MaxHitDetours = 2;

        /// <summary>
        /// Feature A-adjacent tile-exit hook: always divert a hostile raid traveler onto the closest live
        /// hostile AT Turret watching this tile (Nearby @ <see cref="OpportunityRadiusTiles"/>).
        /// Bypasses experimental ToO toggle, eligibility roll, ratio advantage, and escalation.
        /// </summary>
        public static void TryCheckProximityDetour(WorldObject_Traveler traveler, int exitedTileId)
        {
            if (traveler == null || traveler.Destroyed || traveler.pather == null) return;
            if (traveler.isTurretDetour || traveler.atTurretProximityDetourConsumed) return;
            if (!WorldObject_Traveler.IsRaidMission(traveler.mission)) return;
            if (traveler.Faction == null) return;
            if (!TravelerEndpointUtility.IsLiveEndpoint(traveler.targetObject)) return;

            var watchIndex = WorldComponent_SettlementWatchIndex.Get();
            if (watchIndex == null) return;
            List<WorldObject> watchers = watchIndex.GetWatchers(exitedTileId, WatchCapability.Nearby);
            if (watchers == null || watchers.Count == 0) return;

            WorldObject_AT_Turret best = FindClosestHostileTurret(traveler, watchers, exitedTileId);
            if (best == null) return;

            if (!TryBeginTurretDetour(traveler, best, proximity: true))
                return;
        }

        /// <summary>
        /// After an AT shell hit that leaves the traveler alive: begin save/restore detour onto the firing turret.
        /// </summary>
        public static void TryBeginDetourAfterAtShellHit(WorldObject_Traveler target, WorldObject originObject)
        {
            if (!(originObject is WorldObject_AT_Turret turret)) return;
            if (target == null || target.Destroyed) return;
            if (target.travelerStrength <= 0.01f) return;
            TryBeginTurretDetour(target, turret, proximity: false);
        }

        /// <summary>
        /// Hostile raid landing on a tile that already has an enemy AT Turret: strength clash immediately.
        /// Skips the detour-target turret (arrival handler owns that clash).
        /// </summary>
        public static void TryClashOnSharedTile(WorldObject_Traveler traveler, PlanetTile tile)
        {
            if (traveler == null || traveler.Destroyed) return;
            if (!WorldObject_Traveler.IsRaidMission(traveler.mission)) return;
            if (traveler.mission == TravelerMission.RaidDropPod) return;
            if (traveler.Faction == null) return;
            if (!tile.Valid) return;

            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            foreach (WorldObject wo in Find.WorldObjects.ObjectsAt(tile))
            {
                if (!(wo is WorldObject_AT_Turret turret) || turret.Destroyed) continue;
                if (turret.Faction == null) continue;
                if (!WorldActions_Utils.SafeHostileTo(traveler.Faction, turret.Faction)) continue;
                // Arrival owns clash with the destination turret (detour or primary raid).
                if (traveler.targetObject == turret) continue;

                ResolveClash(traveler, turret, manager);
                if (traveler == null || traveler.Destroyed) return;
            }
        }

        /// <summary>
        /// Save original mission target once, set turret as target, and repath.
        /// No-op when already detouring, destroyed, or not hostile to the turret.
        /// </summary>
        public static bool TryBeginTurretDetour(WorldObject_Traveler traveler, WorldObject_AT_Turret turret, bool proximity)
        {
            if (traveler == null || traveler.Destroyed || traveler.pather == null) return false;
            if (traveler.isTurretDetour) return false;
            if (proximity)
            {
                if (traveler.atTurretProximityDetourConsumed) return false;
            }
            else if (traveler.atTurretHitDetourCount >= MaxHitDetours)
            {
                return false;
            }
            if (turret == null || turret.Destroyed) return false;
            if (traveler.targetObject == turret) return false;
            if (traveler.Faction == null || turret.Faction == null) return false;
            if (!WorldActions_Utils.SafeHostileTo(traveler.Faction, turret.Faction)) return false;

            traveler.preTurretDetourTarget = traveler.targetObject;
            traveler.preTurretDetourCachedKind = traveler.cachedTargetKind;
            traveler.preTurretDetourDestTileId = traveler.pather.destTile.Valid
                ? traveler.pather.destTile.tileId
                : (traveler.targetObject != null ? traveler.targetObject.Tile.tileId : -1);
            traveler.isTurretDetour = true;
            if (proximity)
                traveler.atTurretProximityDetourConsumed = true;
            else
                traveler.atTurretHitDetourCount++;

            traveler.targetObject = turret;
            traveler.cachedTargetKind = RaidLaunchGate.ClassifyTarget(turret);

            PlanetTile turretTile = PlanetSurfaceWorldActions.PlanetTileForWdTravel(turret.Tile, traveler);
            if (!traveler.pather.RetargetDestinationAfterCurrentHop(turretTile))
                traveler.pather.StartPath(turretTile, skipLaunchTravelCache: true);

            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            string key = proximity
                ? "TSA_WD_Log_AT_TurretProximityDetour"
                : "TSA_WD_Log_AT_TurretHitDetour";
            manager?.AddLog(new SpreadLogEntry(
                key.Translate(
                    traveler.LabelCap,
                    traveler.preTurretDetourTarget?.LabelCap ?? "?",
                    turret.LabelCap),
                traveler, turret));

            return true;
        }

        /// <summary>
        /// Arrival handler for AT turret dests. Detours clash then resume the original target.
        /// Primary turret raids clash then refund remnant home (never abort, never settlement conquest).
        /// Returns true when handled here. Caller must not run settlement raid resolution or destroy
        /// the traveler again (this method always disposes a primary-raid traveler).
        /// </summary>
        public static bool TryResolveTurretDetourArrival(WorldObject_Traveler traveler, WorldComponent_SpreadManager manager)
        {
            if (traveler == null || traveler.Destroyed) return false;
            if (!traveler.isTurretDetour && !(traveler.targetObject is WorldObject_AT_Turret))
                return false;

            manager ??= Find.World?.GetComponent<WorldComponent_SpreadManager>();
            bool primaryTurretRaid = !traveler.isTurretDetour;

            if (!(traveler.targetObject is WorldObject_AT_Turret turret) || turret.Destroyed)
            {
                if (primaryTurretRaid)
                    FinishPrimaryTurretRaid(traveler);
                else
                    TryResumeAfterTurretDetour(traveler);
                return true;
            }

            ResolveClash(traveler, turret, manager);
            if (traveler == null || traveler.Destroyed)
                return true;

            if (primaryTurretRaid)
                FinishPrimaryTurretRaid(traveler);
            else
                TryResumeAfterTurretDetour(traveler);
            return true;
        }

        /// <summary>Refund remnant to contributors and destroy the traveler. Does not abort or refund allied goodwill.</summary>
        private static void FinishPrimaryTurretRaid(WorldObject_Traveler traveler)
        {
            if (traveler == null || traveler.Destroyed) return;
            if (traveler.travelerStrength > 0.01f)
            {
                Raid_Simulated.RefundStrength(traveler, 1f);
                traveler.suppressDestroyedWorldFx = true;
            }
            traveler.pather?.StopDead();
            if (!traveler.Destroyed)
                traveler.Destroy();
        }

        /// <summary>Restore saved target/destination and repath. Returns false when restore fails (traveler aborted).</summary>
        public static bool TryResumeAfterTurretDetour(WorldObject_Traveler traveler)
        {
            if (traveler == null || traveler.Destroyed) return false;

            WorldObject savedTarget = traveler.preTurretDetourTarget;
            int savedDestId = traveler.preTurretDetourDestTileId;
            RaidLaunchTargetKind savedKind = traveler.preTurretDetourCachedKind;

            ClearDetourFlags(traveler);

            if (!TravelerEndpointUtility.IsLiveEndpoint(savedTarget))
            {
                TravelerEndpointUtility.AbortTraveler(traveler, "TSA_WD_Log_Raid_Aborted_TargetNull".Translate());
                return false;
            }

            traveler.targetObject = savedTarget;
            traveler.cachedTargetKind = savedKind;

            PlanetTile dest;
            if (savedDestId >= 0)
                dest = PlanetSurfaceWorldActions.PlanetTileForWdTravel(new PlanetTile(savedDestId, traveler.Tile.Layer), traveler);
            else
                dest = PlanetSurfaceWorldActions.PlanetTileForWdTravel(savedTarget.Tile, traveler);

            if (traveler.pather == null)
            {
                TravelerEndpointUtility.AbortTraveler(traveler, "TSA_WD_Log_Raid_Aborted_TargetNull".Translate());
                return false;
            }

            traveler.pather.StartPath(dest, skipLaunchTravelCache: true);

            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            manager?.AddLog(new SpreadLogEntry(
                "TSA_WD_Log_AT_TurretResume".Translate(traveler.LabelCap, savedTarget.LabelCap),
                traveler, savedTarget));

            return true;
        }

        /// <summary>
        /// Open-field raid math: traveler is always attacker, AT turret always defender. Winner keeps severity remnant.
        /// </summary>
        public static void ResolveClash(WorldObject_Traveler traveler, WorldObject_AT_Turret turret, WorldComponent_SpreadManager manager)
        {
            manager ??= Find.World?.GetComponent<WorldComponent_SpreadManager>();
            OpenFieldClashUtility.ResolveTravelerVsAtTurret(traveler, turret, manager);
        }

        private static WorldObject_AT_Turret FindClosestHostileTurret(
            WorldObject_Traveler traveler, List<WorldObject> watchers, int fromTileId)
        {
            WorldObject_AT_Turret best = null;
            float bestDist = float.MaxValue;
            WorldGrid grid = Find.WorldGrid;
            for (int i = 0; i < watchers.Count; i++)
            {
                if (!(watchers[i] is WorldObject_AT_Turret turret) || turret.Destroyed) continue;
                if (turret.Faction == null) continue;
                if (!WorldActions_Utils.SafeHostileTo(traveler.Faction, turret.Faction)) continue;
                if (traveler.targetObject == turret) continue;

                float dist = grid != null
                    ? grid.ApproxDistanceInTiles(fromTileId, turret.Tile.tileId)
                    : 0f;
                if (dist > turret.EffectiveRangeTiles + 0.01f) continue;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = turret;
                }
            }
            return best;
        }

        private static void ClearDetourFlags(WorldObject_Traveler traveler)
        {
            traveler.isTurretDetour = false;
            traveler.preTurretDetourTarget = null;
            traveler.preTurretDetourDestTileId = -1;
            traveler.preTurretDetourCachedKind = RaidLaunchTargetKind.NPC;
        }
    }
}
