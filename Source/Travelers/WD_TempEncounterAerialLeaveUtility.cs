using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Shared arrive / leave / contending-force helpers for temporary fight maps
    /// (caravan clash Ambush + outpost defense). Classify by thingClass, not Faction heuristics.
    /// </summary>
    public static class WD_TempEncounterAerialLeaveUtility
    {
        private const string VfSkyfallerArrivingTypeName = "Vehicles.VehicleSkyfaller_Arriving";
        private const string VfSkyfallerLeavingTypeName = "Vehicles.VehicleSkyfaller_Leaving";
        private const string VfVehicleFieldName = "vehicle";

        private static readonly FieldInfo TravellingTransportersInitialTileField =
            AccessTools_InitialTile();

        private static readonly Type VfSkyfallerArrivingType =
            AccessTools_TypeByName(VfSkyfallerArrivingTypeName);
        private static readonly Type VfSkyfallerLeavingType =
            AccessTools_TypeByName(VfSkyfallerLeavingTypeName);
        private static readonly FieldInfo VfSkyfallerVehicleField =
            VfSkyfallerArrivingType?.GetField(VfVehicleFieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? VfSkyfallerLeavingType?.GetField(VfVehicleFieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? AccessTools_TypeByName("Vehicles.VehicleSkyfaller")
                ?.GetField(VfVehicleFieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

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

        private static Type AccessTools_TypeByName(string fullName)
        {
            try
            {
                return AccessTools.TypeByName(fullName);
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
        /// Player still has fight-relevant force: standing, opening/inbound arrival craft, or boarded hold.
        /// </summary>
        public static bool PlayerForceStillContending(Map map)
        {
            if (AnyPlayerForceStandingOnMap(map)) return true;
            if (AnyInboundPlayerForce(map)) return true;
            if (AnyBoardedPlayerForceOnMap(map)) return true;
            return false;
        }

        /// <summary>
        /// Departure craft still on the fight map, or player TravellingTransporters that started here.
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
                if (PodsHaveContendingPlayerFighter(pods))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Departure craft only: <see cref="FlyShipLeaving"/> / VF Leaving. Not arrival pods or boarded holds.
        /// </summary>
        public static bool AnyEscapedSurvivorsStillOnThisMap(Map map)
        {
            if (map?.listerThings?.AllThings == null) return false;
            List<Thing> all = map.listerThings.AllThings;
            for (int i = 0; i < all.Count; i++)
            {
                Thing t = all[i];
                if (t == null || t.Destroyed) continue;
                if (IsPlayerDepartureCraft(t))
                    return true;
            }
            return false;
        }

        /// <summary>Departure skyfaller / leave craft — never use Faction heuristics.</summary>
        public static bool IsPlayerDepartureCraft(Thing t)
        {
            if (t == null || t.Destroyed) return false;
            if (t is FlyShipLeaving) return true;
            if (IsVfSkyfallerLeaving(t)) return true;
            return false;
        }

        /// <summary>Legacy name — departure only (<see cref="FlyShipLeaving"/> / VF Leaving).</summary>
        public static bool IsPlayerLeaveOrEscapeSkyfaller(Skyfaller skyfaller)
            => IsPlayerDepartureCraft(skyfaller);

        /// <summary>
        /// Enemy arrival still unresolved. Departure craft and player-contending arrival craft excluded.
        /// </summary>
        public static bool IsHostileInboundRaidThing(Thing t)
        {
            if (t == null || t.Destroyed) return false;
            if (IsPlayerDepartureCraft(t)) return false;
            // Same arrival types as enemy pods — only skip when player fighters are inside.
            if (IsArrivalCraft(t) && ArrivalCraftHasContendingPlayerFighter(t))
                return false;

            if (t is DropPodIncoming) return true;
            if (t is ActiveTransporter) return true;
            if (t is ShuttleIncoming) return true;

            // Other enemy skyfallers (chunks, etc.) that are not leave / player arrival.
            if (t is Skyfaller)
                return true;

            return false;
        }

        public static bool AnyInboundRaidThreat(Map map)
        {
            if (map?.listerThings?.AllThings == null) return false;
            List<Thing> all = map.listerThings.AllThings;
            for (int i = 0; i < all.Count; i++)
            {
                if (IsHostileInboundRaidThing(all[i]))
                    return true;
            }
            return false;
        }

        public static bool AnyInboundPlayerForce(Map map)
        {
            if (map?.listerThings?.AllThings == null) return false;
            List<Thing> all = map.listerThings.AllThings;
            for (int i = 0; i < all.Count; i++)
            {
                Thing t = all[i];
                if (t == null || t.Destroyed) continue;
                if (!IsArrivalCraft(t)) continue;
                if (ArrivalCraftHasContendingPlayerFighter(t))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// DropPodIncoming / ActiveTransporter / ShuttleIncoming / VF Arriving (arrive-type, any faction).
        /// </summary>
        public static bool IsArrivalCraft(Thing t)
        {
            if (t == null || t.Destroyed) return false;
            if (t is DropPodIncoming) return true;
            if (t is ActiveTransporter) return true;
            if (t is ShuttleIncoming) return true;
            if (IsVfSkyfallerArriving(t)) return true;
            return false;
        }

        /// <summary>Legacy alias for <see cref="IsArrivalCraft"/>.</summary>
        public static bool IsPlayerArrivalCraft(Thing t) => IsArrivalCraft(t);

        /// <summary>
        /// Contending fighters boarded in a landed shuttle / VF vehicle (not yet launching).
        /// </summary>
        public static bool AnyBoardedPlayerForceOnMap(Map map)
        {
            if (map?.listerThings?.AllThings == null) return false;
            List<Thing> all = map.listerThings.AllThings;
            for (int i = 0; i < all.Count; i++)
            {
                Thing t = all[i];
                if (t == null || t.Destroyed) continue;

                if (ModsConfig.OdysseyActive
                    && t is Building_PassengerShuttle shuttle
                    && (shuttle.Faction == null || shuttle.Faction.IsPlayer)
                    && ShuttleHasContendingPlayerFighter(shuttle))
                    return true;
            }

            var spawned = map.mapPawns?.AllPawnsSpawned;
            if (spawned == null) return false;
            for (int i = 0; i < spawned.Count; i++)
            {
                Pawn vehicle = spawned[i];
                if (!VehicleFrameworkOutpostDissolveCompat.IsVehicleFrameworkVehiclePawn(vehicle))
                    continue;
                if (vehicle.Faction == null || !vehicle.Faction.IsPlayer)
                    continue;
                if (VfVehicleHasContendingCrew(vehicle))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// True if this VF hull is the <c>vehicle</c> field of a Leaving skyfaller still on the fight map.
        /// Flee owns those — do not absorb / reform-caravan them.
        /// </summary>
        public static bool IsVfVehicleDepartingOnMap(Pawn vehicle, Map fightMap)
        {
            if (vehicle == null || vehicle.Destroyed || fightMap?.listerThings?.AllThings == null)
                return false;
            List<Thing> things = fightMap.listerThings.AllThings;
            for (int i = 0; i < things.Count; i++)
            {
                Thing t = things[i];
                if (!IsVfSkyfallerLeaving(t)) continue;
                if (TryGetVfSkyfallerVehicle(t) == vehicle)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// True if pawn left via departure craft / world transporters — do not Kill or restore into outpost.
        /// Not arrival pods; not boarded-but-not-launched shuttle holds (those are still on the fight).
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

            if (fightMap?.listerThings?.AllThings != null)
            {
                List<Thing> things = fightMap.listerThings.AllThings;
                for (int i = 0; i < things.Count; i++)
                {
                    Thing t = things[i];
                    if (!IsPlayerDepartureCraft(t)) continue;
                    if (t is Skyfaller sky && ThingOwnerContainsPawn(sky.innerContainer, pawn))
                        return true;
                    if (IsVfSkyfallerLeaving(t) && VfSkyfallerContainsPawn(t, pawn))
                        return true;
                }
            }

            // Generic holdingOwner is too broad (covers arrival ActiveTransporter / boarded shuttle).
            return false;
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

        /// <summary>Combat-effective player humanlike or mechanoid (not VF vehicle husk).</summary>
        public static bool IsContendingPlayerFighter(Pawn p)
        {
            if (IsCombatIneffective(p)) return false;
            if (VehicleFrameworkOutpostDissolveCompat.IsVehicleFrameworkVehiclePawn(p)) return false;
            if (p.Faction == null || !p.Faction.IsPlayer) return false;
            if (p.RaceProps == null) return false;
            if (p.RaceProps.Humanlike) return true;
            if (p.RaceProps.IsMechanoid) return true;
            return false;
        }

        public static bool IsStandingPlayerHumanlike(Pawn p)
            => IsContendingPlayerFighter(p) && p.RaceProps != null && p.RaceProps.Humanlike;

        /// <summary>Spawned contending fighters + VF crew still in the fight on this map.</summary>
        public static bool AnyPlayerForceStandingOnMap(Map map)
        {
            var spawned = map?.mapPawns?.AllPawnsSpawned;
            if (spawned == null) return false;

            for (int i = 0; i < spawned.Count; i++)
            {
                if (IsContendingPlayerFighter(spawned[i]))
                    return true;
            }

            for (int i = 0; i < spawned.Count; i++)
            {
                Pawn vehicle = spawned[i];
                if (!VehicleFrameworkOutpostDissolveCompat.IsVehicleFrameworkVehiclePawn(vehicle))
                    continue;
                if (vehicle.Faction == null || !vehicle.Faction.IsPlayer)
                    continue;
                if (VfVehicleHasContendingCrew(vehicle))
                    return true;
            }

            return false;
        }

        private static bool ArrivalCraftHasContendingPlayerFighter(Thing t)
        {
            if (t is DropPodIncoming incoming)
                return DropPodIncomingHasContendingFighter(incoming);
            if (t is ActiveTransporter active)
                return ActiveTransporterHasContendingFighter(active);
            if (t is ShuttleIncoming shuttleIncoming)
                return ShuttleIncomingHasContendingFighter(shuttleIncoming);
            if (IsVfSkyfallerArriving(t))
                return VfSkyfallerHasContendingCrew(t);
            return false;
        }

        private static bool DropPodIncomingHasContendingFighter(DropPodIncoming incoming)
        {
            ThingOwner container = incoming?.innerContainer;
            if (container == null) return false;
            for (int i = 0; i < container.Count; i++)
            {
                if (container[i] is ActiveTransporter active
                    && ActiveTransporterHasContendingFighter(active))
                    return true;
                if (container[i] is Pawn p && IsContendingPlayerFighter(p))
                    return true;
            }
            return false;
        }

        private static bool ActiveTransporterHasContendingFighter(ActiveTransporter active)
        {
            ActiveTransporterInfo info = active?.Contents;
            ThingOwner container = info?.innerContainer;
            if (container == null) return false;
            for (int i = 0; i < container.Count; i++)
            {
                if (container[i] is Pawn p && IsContendingPlayerFighter(p))
                    return true;
            }
            return false;
        }

        private static bool ShuttleIncomingHasContendingFighter(ShuttleIncoming incoming)
        {
            ThingOwner container = incoming?.innerContainer;
            if (container == null) return false;
            for (int i = 0; i < container.Count; i++)
            {
                if (container[i] is Building_PassengerShuttle shuttle
                    && ShuttleHasContendingPlayerFighter(shuttle))
                    return true;
                if (container[i] is Pawn p && IsContendingPlayerFighter(p))
                    return true;
            }
            return false;
        }

        private static bool ShuttleHasContendingPlayerFighter(Building_PassengerShuttle shuttle)
        {
            CompTransporter transporter = shuttle?.TransporterComp;
            ThingOwner container = transporter?.innerContainer;
            if (container == null) return false;
            for (int i = 0; i < container.Count; i++)
            {
                if (container[i] is Pawn p && IsContendingPlayerFighter(p))
                    return true;
            }
            return false;
        }

        private static bool PodsHaveContendingPlayerFighter(TravellingTransporters pods)
        {
            if (pods == null) return false;
            foreach (Pawn p in pods.Pawns)
            {
                if (IsContendingPlayerFighter(p) || IsLivingPlayerHumanlike(p))
                    return true;
            }
            return false;
        }

        private static bool IsVfSkyfallerArriving(Thing t)
        {
            if (t == null || VfSkyfallerArrivingType == null) return false;
            return VfSkyfallerArrivingType.IsInstanceOfType(t);
        }

        private static bool IsVfSkyfallerLeaving(Thing t)
        {
            if (t == null || VfSkyfallerLeavingType == null) return false;
            return VfSkyfallerLeavingType.IsInstanceOfType(t);
        }

        private static Pawn TryGetVfSkyfallerVehicle(Thing skyfaller)
        {
            if (skyfaller == null || VfSkyfallerVehicleField == null) return null;
            try
            {
                return VfSkyfallerVehicleField.GetValue(skyfaller) as Pawn;
            }
            catch
            {
                return null;
            }
        }

        private static bool VfSkyfallerHasContendingCrew(Thing skyfaller)
        {
            Pawn vehicle = TryGetVfSkyfallerVehicle(skyfaller);
            return VfVehicleHasContendingCrew(vehicle);
        }

        private static bool VfSkyfallerContainsPawn(Thing skyfaller, Pawn pawn)
        {
            if (pawn == null) return false;
            Pawn vehicle = TryGetVfSkyfallerVehicle(skyfaller);
            if (vehicle == null) return false;
            aboardScratch.Clear();
            aboardSeen.Clear();
            VehicleFrameworkOutpostDissolveCompat.CollectPawnsAboardVehicleForRoster(
                vehicle, aboardScratch, aboardSeen);
            for (int i = 0; i < aboardScratch.Count; i++)
            {
                if (aboardScratch[i] == pawn) return true;
            }
            return false;
        }

        private static bool VfVehicleHasContendingCrew(Pawn vehicle)
        {
            if (vehicle == null || vehicle.Destroyed) return false;
            aboardScratch.Clear();
            aboardSeen.Clear();
            VehicleFrameworkOutpostDissolveCompat.CollectPawnsAboardVehicleForRoster(
                vehicle, aboardScratch, aboardSeen);
            for (int j = 0; j < aboardScratch.Count; j++)
            {
                if (IsContendingPlayerFighter(aboardScratch[j]))
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
                if (container[i] is ActiveTransporter active
                    && ThingOwnerContainsPawn(active.Contents?.innerContainer, pawn))
                    return true;
            }
            return false;
        }
    }
}
