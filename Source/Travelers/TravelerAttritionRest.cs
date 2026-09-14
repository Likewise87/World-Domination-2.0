using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Mid/Late attrition rest: walkers stop near the efficiency floor, regen to full initial strength, then resume.
    /// Forecasts use <see cref="GetMinTravelEfficiency"/> so raid gates assume at least the rest floor (Def 80%).
    /// </summary>
    public static class TravelerAttritionRest
    {
        public static bool IsFeatureActive(WorldDominationSettings seth, WorldComponent_SpreadManager manager)
        {
            if (seth == null) return false;
            return WdEscalation.PassesGate(seth.gateThreatAttritionRest, manager);
        }

        /// <summary>Minimum travel efficiency for forecasts and Mid/Late attrition clamp (max of legacy floor and rest ratio).</summary>
        public static float GetMinTravelEfficiency(WorldDominationSettings seth, WorldComponent_SpreadManager manager = null)
        {
            if (seth == null) return 0.4f;
            float legacyFloor = 1f - Mathf.Clamp01(seth.maxTravelPercentageStrengthLoss);
            manager ??= Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (!IsFeatureActive(seth, manager))
                return legacyFloor;
            float restFloor = Mathf.Clamp01(seth.attritionRestMinRatio);
            return Mathf.Max(legacyFloor, restFloor);
        }

        public static float RestThresholdStrength(WorldObject_Traveler traveler, WorldDominationSettings seth)
        {
            if (traveler == null || seth == null) return 0f;
            return traveler.initialStrength * Mathf.Clamp01(seth.attritionRestMinRatio);
        }

        public static bool IsWithinFireGrace(WorldObject_Traveler traveler, WorldDominationSettings seth)
        {
            if (traveler == null || seth == null) return false;
            if (traveler.lastHostileFireTick < 0) return false;
            int graceTicks = Mathf.RoundToInt(Mathf.Max(0f, seth.attritionRestFireGraceDays) * GenDate.TicksPerDay);
            if (graceTicks <= 0) return false;
            return Find.TickManager.TicksGame - traveler.lastHostileFireTick < graceTicks;
        }

        public static bool IsNearDestination(WorldObject_Traveler traveler, WorldDominationSettings seth)
        {
            if (traveler?.pather == null || seth == null) return false;
            float bufferDays = Mathf.Max(0f, seth.attritionRestNearDestBufferDays);
            if (bufferDays <= 0f) return false;
            if (!traveler.pather.moving) return false;
            float remaining = traveler.pather.GetRemainingTravelTicks();
            return remaining <= bufferDays * GenDate.TicksPerDay;
        }

        /// <summary>True if a hostile AT turret is in range and eligible to engage this traveler.</summary>
        public static bool HostileAtCanEngage(WorldObject_Traveler traveler)
        {
            if (traveler == null || traveler.Destroyed) return false;
            if (!AtTurretUtility.IsGroundAtTurretTravelerTarget(traveler)) return false;

            Faction travelerFaction = traveler.Faction;
            if (travelerFaction == null) return false;

            int travelerTileId = traveler.Tile.tileId;
            if (travelerTileId < 0) return false;

            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            var all = Find.WorldObjects?.AllWorldObjects;
            if (all == null) return false;

            for (int i = 0; i < all.Count; i++)
            {
                if (!(all[i] is WorldObject_AT_Turret gun) || gun.Destroyed || !gun.DefenseActive)
                    continue;

                Faction gunFaction = gun.Faction;
                if (gunFaction == null || gunFaction == travelerFaction)
                    continue;
                if (!WorldActions_Utils.SafeHostileTo(gunFaction, travelerFaction))
                    continue;

                if (travelerFaction.IsPlayer)
                {
                    if (!AtTurretUtility.CanAutoTargetPlayerTraveler(gun, traveler))
                        continue;
                }
                else if (!RapidResponseUtility.IsEligibleAutoInterceptTarget(traveler, gun.DefenseRaidTargetMask)
                         || !InterceptionMissionMaskUtils.Matches(traveler.mission, gun.DefenseMask))
                {
                    continue;
                }

                int gunTileId = gun.Tile.tileId;
                if (gunTileId < 0) continue;

                float dist = manager != null
                    ? (float)WorldActions_Utils.GetDistance(gunTileId, travelerTileId, manager)
                    : Find.WorldGrid.ApproxDistanceInTiles(gunTileId, travelerTileId);
                if (dist <= gun.EffectiveRangeTiles)
                    return true;
            }

            return false;
        }

        public static bool CanBeginRest(WorldObject_Traveler traveler, WorldDominationSettings seth)
        {
            if (traveler == null || seth == null) return false;
            if (IsWithinFireGrace(traveler, seth)) return false;
            if (IsNearDestination(traveler, seth)) return false;
            if (HostileAtCanEngage(traveler)) return false;
            return true;
        }

        public static void BeginRest(WorldObject_Traveler traveler)
        {
            if (traveler == null || traveler.Destroyed || traveler.pather == null) return;
            PlanetTile dest = traveler.pather.destTile;
            if (!dest.Valid && traveler.targetObject != null)
                dest = traveler.targetObject.Tile;
            traveler.attritionRestDestTile = dest.Valid ? dest.tileId : -1;
            traveler.pather.StopDead();
            traveler.attritionResting = true;
        }

        public static void ResumeFromRest(WorldObject_Traveler traveler)
        {
            if (traveler == null || traveler.Destroyed || traveler.pather == null) return;
            traveler.attritionResting = false;
            PlanetTile dest = PlanetTile.Invalid;
            if (traveler.attritionRestDestTile >= 0)
                dest = PlanetSurfaceWorldActions.PlanetTileForWdTravel(traveler.attritionRestDestTile, traveler);
            if (!dest.Valid)
                dest = traveler.pather.destTile;
            if (!dest.Valid && traveler.targetObject != null)
                dest = traveler.targetObject.Tile;
            traveler.attritionRestDestTile = -1;
            if (!dest.Valid) return;
            traveler.pather.StartPath(dest, skipLaunchTravelCache: true);
        }

        public static void NotifyHostileFire(WorldObject_Traveler traveler)
        {
            if (traveler == null || traveler.Destroyed) return;
            traveler.lastHostileFireTick = Find.TickManager.TicksGame;
            if (traveler.attritionResting)
                ResumeFromRest(traveler);
        }

        public static void TickRestingRegen(WorldObject_Traveler traveler, WorldDominationSettings seth, int delta)
        {
            if (traveler == null || !traveler.attritionResting || seth == null) return;
            if (!traveler.IsHashIntervalTick(180, delta)) return;

            float regen = traveler.initialStrength * Mathf.Max(0f, seth.attritionRestRegenPerHour) * (180f / 2500f);
            traveler.travelerStrength = Mathf.Min(traveler.initialStrength, traveler.travelerStrength + regen);
            if (traveler.travelerStrength >= traveler.initialStrength - 0.01f)
            {
                traveler.travelerStrength = traveler.initialStrength;
                ResumeFromRest(traveler);
            }
        }
    }
}
