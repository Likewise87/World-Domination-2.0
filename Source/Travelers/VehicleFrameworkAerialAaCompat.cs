using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Soft Vehicle Framework AA hooks for <c>Vehicles.World.AerialVehicleInFlight</c>.
    /// No hard reference to Vehicles.dll — inactive when VF is not loaded.
    /// See https://github.com/SmashPhil/Vehicle-Framework
    /// </summary>
    [StaticConstructorOnStartup]
    public static class VehicleFrameworkAerialAaCompat
    {
        public const string PackageId = "SmashPhil.VehicleFramework";
        public const string AerialTypeName = "Vehicles.World.AerialVehicleInFlight";
        public const string AerialDefName = "AerialVehicle";

        private static bool active;
        private static Type aerialType;
        private static FieldInfo flightPathField;
        private static PropertyInfo pathProperty;
        private static PropertyInfo firstProperty;
        private static PropertyInfo lastProperty;
        private static PropertyInfo flyingProperty;
        private static PropertyInfo tileProperty; // FlightNode.Tile
        private static MethodInfo initiateCrashMethod;
        private static MethodInfo drawPosAheadMethod;

        static VehicleFrameworkAerialAaCompat()
        {
            if (!IsVehicleFrameworkActive())
                return;

            try
            {
                aerialType = AccessTools.TypeByName(AerialTypeName);
                if (aerialType == null) return;

                flightPathField = AccessTools.Field(aerialType, "flightPath");
                flyingProperty = AccessTools.Property(aerialType, "Flying");
                initiateCrashMethod = AccessTools.Method(aerialType, "InitiateCrashEvent");
                drawPosAheadMethod = AccessTools.Method(aerialType, "DrawPosAhead", new[] { typeof(int) });

                Type flightPathType = AccessTools.TypeByName("Vehicles.World.FlightPath");
                Type flightNodeType = AccessTools.TypeByName("Vehicles.World.FlightNode");
                if (flightPathType != null)
                {
                    pathProperty = AccessTools.Property(flightPathType, "Path");
                    firstProperty = AccessTools.Property(flightPathType, "First");
                    lastProperty = AccessTools.Property(flightPathType, "Last");
                }
                if (flightNodeType != null)
                    tileProperty = AccessTools.Property(flightNodeType, "Tile");

                if (flightPathField == null || pathProperty == null || tileProperty == null)
                {
                    Log.Warning("[TSA WD] Vehicle Framework aerial AA: incomplete reflection map; disabled.");
                    return;
                }

                var harmony = new Harmony("TSA.WorldDomination.VehicleFrameworkAerialAa");
                MethodInfo orderFly = AccessTools.Method(aerialType, "OrderFlyToTiles");
                MethodInfo spawnSetup = AccessTools.Method(aerialType, "SpawnSetup");
                if (orderFly != null)
                {
                    harmony.Patch(orderFly,
                        postfix: new HarmonyMethod(typeof(VehicleFrameworkAerialAaCompat), nameof(OrderFlyToTiles_Postfix)));
                }
                if (spawnSetup != null)
                {
                    harmony.Patch(spawnSetup,
                        postfix: new HarmonyMethod(typeof(VehicleFrameworkAerialAaCompat), nameof(SpawnSetup_Postfix)));
                }

                active = true;
                Log.Message("[TSA WD] Vehicle Framework aerial AA hooks active.");
            }
            catch (Exception ex)
            {
                Log.Warning($"[TSA WD] Vehicle Framework aerial AA hooks disabled: {ex.Message}");
            }
        }

        public static bool Active => active;

        public static bool IsVehicleFrameworkActive()
        {
            if (ModsConfig.IsActive(PackageId) || ModsConfig.IsActive(PackageId + "_steam"))
                return true;
            var mods = LoadedModManager.RunningModsListForReading;
            for (int i = 0; i < mods.Count; i++)
            {
                string id = mods[i]?.PackageId;
                if (string.Equals(id, PackageId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(id, PackageId + "_steam", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public static bool IsAerialVehicleInFlight(WorldObject wo)
        {
            if (wo == null || wo.Destroyed) return false;
            if (aerialType != null && aerialType.IsInstanceOfType(wo))
                return true;
            return wo.def != null && wo.def.defName == AerialDefName;
        }

        public static bool IsFlying(WorldObject aerial)
        {
            if (!IsAerialVehicleInFlight(aerial)) return false;
            if (flyingProperty == null) return aerial.Spawned;
            try
            {
                object v = flyingProperty.GetValue(aerial, null);
                return v is bool b && b;
            }
            catch
            {
                return aerial.Spawned;
            }
        }

        public static void SpawnSetup_Postfix(WorldObject __instance)
        {
            if (__instance == null || __instance.Destroyed) return;
            if (IsFlying(__instance))
                WorldComponent_InterceptionScheduler.Current?.RegisterExternalAirborne(__instance);
        }

        public static void OrderFlyToTiles_Postfix(WorldObject __instance)
        {
            if (__instance == null || __instance.Destroyed) return;
            WorldComponent_InterceptionScheduler.Current?.ArmExternalAirborneAaNow(__instance);
        }

        /// <summary>Final destination tile id, or -1.</summary>
        public static int TryGetDestinationTileId(WorldObject aerial)
        {
            if (!TryGetFlightNodes(aerial, out List<int> tiles) || tiles.Count == 0)
                return -1;
            return tiles[tiles.Count - 1];
        }

        /// <summary>Next waypoint tile id, or -1.</summary>
        public static int TryGetNextTileId(WorldObject aerial)
        {
            if (!TryGetFlightNodes(aerial, out List<int> tiles) || tiles.Count == 0)
                return -1;
            return tiles[0];
        }

        public static bool TryGetFlightNodes(WorldObject aerial, out List<int> tileIds)
        {
            tileIds = null;
            if (!IsAerialVehicleInFlight(aerial) || flightPathField == null || pathProperty == null || tileProperty == null)
                return false;
            try
            {
                object pathObj = flightPathField.GetValue(aerial);
                if (pathObj == null) return false;
                object pathList = pathProperty.GetValue(pathObj, null);
                if (pathList is not IList list || list.Count == 0) return false;

                tileIds = new List<int>(list.Count);
                for (int i = 0; i < list.Count; i++)
                {
                    object node = list[i];
                    if (node == null) continue;
                    object tileObj = tileProperty.GetValue(node, null);
                    if (tileObj is PlanetTile pt && pt.tileId >= 0)
                        tileIds.Add(pt.tileId);
                    else if (tileObj is int id && id >= 0)
                        tileIds.Add(id);
                }
                return tileIds.Count > 0;
            }
            catch (Exception ex)
            {
                WDVerbose.Msg($"VF aerial path reflect fail: {ex.Message}");
                return false;
            }
        }

        public static Vector3 GetAimPos(WorldObject aerial, int ticksAhead = 0)
        {
            if (aerial == null) return Vector3.zero;
            if (ticksAhead > 0 && drawPosAheadMethod != null)
            {
                try
                {
                    object r = drawPosAheadMethod.Invoke(aerial, new object[] { ticksAhead });
                    if (r is Vector3 v && v.sqrMagnitude > 0.0001f)
                        return v;
                }
                catch { /* fall through to DrawPos */ }
            }
            return aerial.DrawPos;
        }

        /// <summary>Crash the aerial via VF <c>InitiateCrashEvent</c> (creates downed-shuttle incident). Returns true if invoked.</summary>
        public static bool TryCrashFromAntiAir(WorldObject aerial, WorldObject aaOrigin)
        {
            if (!IsAerialVehicleInFlight(aerial) || aerial.Destroyed) return false;
            if (initiateCrashMethod == null)
            {
                aerial.Destroy();
                return true;
            }
            try
            {
                var parms = initiateCrashMethod.GetParameters();
                if (parms.Length >= 2 && parms[1].ParameterType == typeof(string[]))
                    initiateCrashMethod.Invoke(aerial, new object[] { aaOrigin, new[] { "TSA_WD_AntiAir_VfAerial_CrashReason".Translate().ToString() } });
                else if (parms.Length >= 1)
                    initiateCrashMethod.Invoke(aerial, new object[] { aaOrigin });
                else
                    initiateCrashMethod.Invoke(aerial, null);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning($"[TSA WD] VF aerial AA crash failed, destroying WO: {ex.Message}");
                if (!aerial.Destroyed)
                    aerial.Destroy();
                return true;
            }
        }
    }
}
