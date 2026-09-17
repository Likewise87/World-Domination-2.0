using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Shared Odyssey/VF aerial-leave bus for temporary fight maps (caravan clash Ambush + outpost defense).
    /// Launch / world-airborne hooks fan out here; map components own win/lose resolve.
    /// </summary>
    public static class WD_TempEncounterAerialLeaveUtility
    {
        private static readonly FieldInfo TravellingTransportersInitialTileField =
            AccessTools_InitialTile();

        private static readonly List<Pawn> aboardScratch = new List<Pawn>();
        private static readonly HashSet<Pawn> aboardSeen = new HashSet<Pawn>();

        private static FieldInfo AccessTools_InitialTile()
        {
            try
            {
                return typeof(TravellingTransporters).GetField(
                    "initialTile",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }
            catch
            {
                return null;
            }
        }

        public static void NotifyPossibleAerialLeaveFromMap(Map map)
        {
            if (map == null) return;
            map.GetComponent<WD_MapComponent_CaravanClash>()?.NotifyPossibleAerialLeave();
            map.GetComponent<WD_MapComponent_OutpostDefense>()?.NotifyPossibleAerialLeave();
        }

        public static void NotifyWorldAirborneFromStartTile(int startTileId)
        {
            if (startTileId < 0) return;
            WD_MapComponent_CaravanClash.FindClashNeedingAerialFleeOnTile(startTileId)
                ?.NotifyAirborneSurvivorsLeftThisClash();
            WD_MapComponent_OutpostDefense.FindDefenseNeedingAerialFleeOnTile(startTileId)
                ?.NotifyAirborneSurvivorsLeftThisDefense();
        }

        public static int TryGetTravellingTransportersStartTileId(TravellingTransporters pods)
        {
            if (pods == null) return -1;
            try
            {
                if (TravellingTransportersInitialTileField != null)
                {
                    object v = TravellingTransportersInitialTileField.GetValue(pods);
                    if (v is PlanetTile pt && pt.Valid)
                        return pt.tileId;
                }
            }
            catch
            {
                // fall through
            }
            return pods.Tile.Valid ? pods.Tile.tileId : -1;
        }

        /// <summary>
        /// Escape craft still on the fight map, or living player humanlikes in TravellingTransporters that started here.
        /// </summary>
        public static bool AnyPlayerSurvivorsEscaped(Map map)
        {
            if (AnyEscapedSurvivorsStillOnThisMap(map))
                return true;

            int tileId = map != null && map.Tile.Valid ? map.Tile.tileId : -1;
            if (tileId < 0 || Find.WorldObjects == null) return false;

            List<WorldObject> all = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < all.Count; i++)
            {
                WorldObject wo = all[i];
                if (wo is not TravellingTransporters pods || pods.Destroyed) continue;
                if (TryGetTravellingTransportersStartTileId(pods) != tileId) continue;
                if (PodsHaveLivingPlayerHumanlike(pods))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Player escape craft still on the map: boarded shuttle, leaving skyfaller, or VF crew aboard.
        /// </summary>
        public static bool AnyEscapedSurvivorsStillOnThisMap(Map map)
        {
            if (map?.listerThings?.AllThings != null)
            {
                List<Thing> all = map.listerThings.AllThings;
                for (int i = 0; i < all.Count; i++)
                {
                    Thing t = all[i];
                    if (t == null || t.Destroyed) continue;

                    if (t is FlyShipLeaving)
                        return true;

                    if (t is Skyfaller skyfaller)
                    {
                        if (IsPassengerShuttleLeaveSkyfaller(skyfaller))
                            return true;
                        if (SkyfallerHasLivingPlayerHumanlike(skyfaller))
                            return true;
                    }

                    if (ModsConfig.OdysseyActive
                        && t is Building_PassengerShuttle shuttle
                        && (shuttle.Faction == null || shuttle.Faction.IsPlayer)
                        && ShuttleHasLivingPlayerHumanlike(shuttle))
                        return true;
                }
            }

            var spawned = map?.mapPawns?.AllPawnsSpawned;
            if (spawned == null) return false;
            for (int i = 0; i < spawned.Count; i++)
            {
                Pawn vehicle = spawned[i];
                if (!VehicleFrameworkOutpostDissolveCompat.IsVehicleFrameworkVehiclePawn(vehicle))
                    continue;
                if (vehicle.Faction == null || !vehicle.Faction.IsPlayer)
                    continue;

                aboardScratch.Clear();
                aboardSeen.Clear();
                VehicleFrameworkOutpostDissolveCompat.CollectPawnsAboardVehicleForRoster(
                    vehicle, aboardScratch, aboardSeen);
                for (int j = 0; j < aboardScratch.Count; j++)
                {
                    if (IsLivingPlayerHumanlike(aboardScratch[j]))
                        return true;
                }
            }

            return false;
        }

        /// <summary>Player leave craft must not count as inbound raid threat.</summary>
        public static bool IsPlayerLeaveOrEscapeSkyfaller(Skyfaller skyfaller)
        {
            if (skyfaller == null || skyfaller.Destroyed) return false;
            if (skyfaller is FlyShipLeaving) return true;
            if (IsPassengerShuttleLeaveSkyfaller(skyfaller)) return true;
            if (skyfaller.Faction != null && skyfaller.Faction.IsPlayer)
                return true;
            return false;
        }

        public static bool IsHostileInboundRaidThing(Thing t)
        {
            if (t == null || t.Destroyed) return false;
            if (t is Skyfaller skyfaller)
            {
                if (IsPlayerLeaveOrEscapeSkyfaller(skyfaller))
                    return false;
                return true;
            }

            string defName = t.def?.defName;
            if (defName != null
                && defName.IndexOf("DropPod", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (t.Faction != null && t.Faction.IsPlayer)
                    return false;
                return true;
            }

            return false;
        }

        /// <summary>
        /// True if pawn already left the fight map via shuttle/pods (alive off-map escapee — do not Kill).
        /// </summary>
        public static bool IsAliveEscapeeOffMap(Pawn pawn, Map fightMap)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead) return false;
            if (pawn.Spawned && fightMap != null && pawn.Map == fightMap) return false;
            if (pawn.Spawned) return false;

            if (ModsConfig.OdysseyActive && Find.WorldObjects != null)
            {
                int tileId = fightMap != null && fightMap.Tile.Valid ? fightMap.Tile.tileId : -1;
                List<WorldObject> all = Find.WorldObjects.AllWorldObjects;
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i] is not TravellingTransporters pods || pods.Destroyed) continue;
                    if (tileId >= 0 && TryGetTravellingTransportersStartTileId(pods) != tileId) continue;
                    foreach (Pawn p in pods.Pawns)
                    {
                        if (p == pawn) return true;
                    }
                }
            }

            // Held in a leave skyfaller / shuttle still on the fight map.
            if (fightMap?.listerThings?.AllThings != null)
            {
                List<Thing> things = fightMap.listerThings.AllThings;
                for (int i = 0; i < things.Count; i++)
                {
                    Thing t = things[i];
                    if (t is Skyfaller sky && ThingOwnerContainsPawn(sky.innerContainer, pawn))
                        return true;
                    if (ModsConfig.OdysseyActive
                        && t is Building_PassengerShuttle shuttle
                        && ThingOwnerContainsPawn(shuttle.TransporterComp?.innerContainer, pawn))
                        return true;
                }
            }

            return pawn.holdingOwner != null;
        }

        public static bool IsLivingPlayerHumanlike(Pawn p)
        {
            if (p == null || p.Destroyed || p.Dead) return false;
            if (VehicleFrameworkOutpostDissolveCompat.IsVehicleFrameworkVehiclePawn(p)) return false;
            if (p.RaceProps == null || !p.RaceProps.Humanlike) return false;
            if (p.Faction == null || !p.Faction.IsPlayer) return false;
            return true;
        }

        public static bool IsCombatIneffective(Pawn p)
        {
            if (p == null || p.Destroyed || p.Dead || p.Downed) return true;
            if (p.MentalStateDef == MentalStateDefOf.PanicFlee) return true;
            return false;
        }

        public static bool IsStandingPlayerHumanlike(Pawn p)
        {
            if (IsCombatIneffective(p)) return false;
            if (VehicleFrameworkOutpostDissolveCompat.IsVehicleFrameworkVehiclePawn(p)) return false;
            if (p.RaceProps == null || !p.RaceProps.Humanlike) return false;
            if (p.Faction == null || !p.Faction.IsPlayer) return false;
            return true;
        }

        /// <summary>Spawned player humanlikes + VF crew still in the fight on this map.</summary>
        public static bool AnyPlayerForceStandingOnMap(Map map)
        {
            var spawned = map?.mapPawns?.AllPawnsSpawned;
            if (spawned == null) return false;

            for (int i = 0; i < spawned.Count; i++)
            {
                if (IsStandingPlayerHumanlike(spawned[i]))
                    return true;
            }

            for (int i = 0; i < spawned.Count; i++)
            {
                Pawn vehicle = spawned[i];
                if (!VehicleFrameworkOutpostDissolveCompat.IsVehicleFrameworkVehiclePawn(vehicle))
                    continue;
                if (vehicle.Faction == null || !vehicle.Faction.IsPlayer)
                    continue;

                aboardScratch.Clear();
                aboardSeen.Clear();
                VehicleFrameworkOutpostDissolveCompat.CollectPawnsAboardVehicleForRoster(
                    vehicle, aboardScratch, aboardSeen);
                for (int j = 0; j < aboardScratch.Count; j++)
                {
                    if (IsStandingPlayerHumanlike(aboardScratch[j]))
                        return true;
                }
            }

            return false;
        }

        private static bool IsPassengerShuttleLeaveSkyfaller(Skyfaller skyfaller)
        {
            string defName = skyfaller?.def?.defName;
            if (string.IsNullOrEmpty(defName)) return false;
            return defName.IndexOf("PassengerShuttleLeaving", StringComparison.OrdinalIgnoreCase) >= 0
                || defName.IndexOf("PassengerShuttleSkyfaller", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool SkyfallerHasLivingPlayerHumanlike(Skyfaller skyfaller)
        {
            ThingOwner container = skyfaller?.innerContainer;
            if (container == null) return false;
            for (int i = 0; i < container.Count; i++)
            {
                if (container[i] is Pawn p && IsLivingPlayerHumanlike(p))
                    return true;
                if (container[i] is Building_PassengerShuttle shuttle
                    && ShuttleHasLivingPlayerHumanlike(shuttle))
                    return true;
            }
            return false;
        }

        private static bool ShuttleHasLivingPlayerHumanlike(Building_PassengerShuttle shuttle)
        {
            CompTransporter transporter = shuttle?.TransporterComp;
            ThingOwner container = transporter?.innerContainer;
            if (container == null) return false;
            for (int i = 0; i < container.Count; i++)
            {
                if (container[i] is Pawn p && IsLivingPlayerHumanlike(p))
                    return true;
            }
            return false;
        }

        private static bool PodsHaveLivingPlayerHumanlike(TravellingTransporters pods)
        {
            if (pods == null) return false;
            foreach (Pawn p in pods.Pawns)
            {
                if (IsLivingPlayerHumanlike(p))
                    return true;
            }
            return false;
        }

        private static bool ThingOwnerContainsPawn(ThingOwner container, Pawn pawn)
        {
            if (container == null || pawn == null) return false;
            for (int i = 0; i < container.Count; i++)
            {
                if (container[i] == pawn) return true;
                if (container[i] is Building_PassengerShuttle shuttle
                    && ThingOwnerContainsPawn(shuttle.TransporterComp?.innerContainer, pawn))
                    return true;
            }
            return false;
        }
    }
}
