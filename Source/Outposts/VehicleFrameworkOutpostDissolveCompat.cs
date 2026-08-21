using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Optional integration when a player caravan is merged into a WD outpost (auto-add, add gizmo, founding).
    /// SmashPhil Vehicle Framework (github.com/SmashPhil/Vehicle-Framework): colonists can sit in
    /// <c>Vehicles.VehiclePawn</c> holders; vanilla <c>Caravan.RemovePawn</c> without VF
    /// <c>Ext_Vehicles.GetVehicle</c> / <c>VehiclePawn.RemovePawn</c> first breaks needs/path state (invalid
    /// <c>PlanetTile</c> in caravan ticks). <c>Vehicles.World.StashedVehicle</c> can remain on the tile after dissolve.
    /// No compile-time reference to VF — reflection and type names only.
    /// </summary>
    public static class VehicleFrameworkOutpostDissolveCompat
    {
        private const string VehiclePawnFullName = "Vehicles.VehiclePawn";
        private const string VehicleCaravanFullName = "Vehicles.World.VehicleCaravan";
        private const string StashedVehicleFullName = "Vehicles.World.StashedVehicle";
        private const string ExtVehiclesFullName = "Vehicles.Ext_Vehicles";
        /// <summary>VF / dissolve can spawn an empty player caravan whose label stays the generic default; real caravans use a colonist name (e.g. "Torben Caravan").</summary>
        private const string GenericGhostCaravanLabel = "Caravan";

        private static PropertyInfo vehiclesListProperty;
        private static bool extVehiclesLookupDone;
        private static MethodInfo extVehiclesGetVehicle;
        private static PropertyInfo vehiclePawnUiIconOverrideProperty;
        private static PropertyInfo vehiclePawnVehicleDefProperty;
        private static PropertyInfo vehiclePawnAllPawnsAboardProperty;
        private static PropertyInfo vehiclePawnAllInventoryPawnsProperty;
        private static MethodInfo vehiclePawnTryAddPawnMethod;

        private static bool vehicleCaravanStopReflectReady;
        private static bool vehicleCaravanStopReflectOk;
        private static FieldInfo vehicleCaravan_vehiclePatherField;
        private static PropertyInfo vehicleCaravanPather_MovingNow;
        private static PropertyInfo vehicleCaravanPather_Destination;
        private static PropertyInfo vehicleCaravan_AerialVehicleProperty;
        private static bool vehicleCaravanStopReflectFailLogged;

        /// <summary>
        /// VF <c>Vehicles.World.VehicleCaravan</c> subclasses <see cref="Caravan"/> but uses
        /// <c>vehiclePather</c> (<c>VehicleCaravan_PathFollower</c>) for movement. Vanilla <c>caravan.pather</c>
        /// stays out of sync, so <c>MovingNow</c> / destination checks must use <c>vehiclePather</c>.
        /// </summary>
        public static bool IsVehicleFrameworkVehicleCaravan(Caravan caravan) =>
            caravan != null && string.Equals(caravan.GetType().FullName, VehicleCaravanFullName, StringComparison.Ordinal);

        /// <summary>
        /// Landed VF aerial (lead vehicle <c>VehicleType.Air</c>). After <c>SwitchToCaravan</c>,
        /// <c>vehiclePather.Destination</c> is often stale — same founding issue as Odyssey shuttle caravans.
        /// </summary>
        public static bool IsAerialVehicleCaravan(Caravan caravan)
        {
            if (!IsVehicleFrameworkVehicleCaravan(caravan)) return false;
            if (!EnsureVehicleCaravanStopReflection()) return false;
            if (vehicleCaravan_AerialVehicleProperty == null) return false;
            try
            {
                object val = vehicleCaravan_AerialVehicleProperty.GetValue(caravan);
                return val is bool b && b;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// When <paramref name="caravan"/> is a VF vehicle caravan, evaluates stop state from
        /// <c>vehiclePather</c> and returns true (caller must not apply vanilla <c>pather</c> logic).
        /// Returns false if not a vehicle caravan or VF types are missing (vanilla path applies).
        /// </summary>
        public static bool TryEvaluateVehicleCaravanStoppedOnTile(
            Caravan caravan,
            int tile,
            bool requireDestinationMatchesTile,
            out bool ok,
            out string reason)
        {
            ok = false;
            reason = null;
            if (!IsVehicleFrameworkVehicleCaravan(caravan)) return false;
            if (!EnsureVehicleCaravanStopReflection())
            {
                if (!vehicleCaravanStopReflectFailLogged)
                {
                    vehicleCaravanStopReflectFailLogged = true;
                    Log.Warning("[WD] Vehicle Framework: could not resolve VehicleCaravan.vehiclePather for outpost stop checks; establishment may mis-detect movement.");
                }

                reason = "TSA_WD_EstablishOutpost_WaitUntilStopped".Translate().ToString();
                return true;
            }

            object pather;
            try
            {
                pather = vehicleCaravan_vehiclePatherField.GetValue(caravan);
            }
            catch (Exception ex)
            {
                Log.Warning($"[WD] Vehicle Framework compat: read vehiclePather failed: {ex.Message}");
                reason = "TSA_WD_EstablishOutpost_WaitUntilStopped".Translate().ToString();
                return true;
            }

            if (pather == null)
            {
                reason = "TSA_WD_EstablishOutpost_WaitUntilStopped".Translate().ToString();
                return true;
            }

            bool movingNow = true;
            try
            {
                object mn = vehicleCaravanPather_MovingNow.GetValue(pather);
                if (mn is bool b) movingNow = b;
            }
            catch (Exception ex)
            {
                Log.Warning($"[WD] Vehicle Framework compat: read MovingNow failed: {ex.Message}");
                reason = "TSA_WD_EstablishOutpost_WaitUntilStopped".Translate().ToString();
                return true;
            }

            if (movingNow)
            {
                reason = "TSA_WD_EstablishOutpost_WaitUntilStopped".Translate().ToString();
                return true;
            }

            if (caravan.Tile != tile)
            {
                reason = "TSA_WD_EstablishOutpost_WaitUntilStopped".Translate().ToString();
                return true;
            }

            // Aerial land / abandoned camp tile: destination may stay stale while parked (Odyssey shuttle parallel).
            bool checkDestination = requireDestinationMatchesTile
                && !IsAerialVehicleCaravan(caravan)
                && !Outpost_EstablishmentRequirements.TileHasVanillaAbandonedCamp(tile);
            if (checkDestination && vehicleCaravanPather_Destination != null)
            {
                try
                {
                    object dest = vehicleCaravanPather_Destination.GetValue(pather);
                    int destTileId = TryPlanetTileLikeToTileId(dest);
                    if (destTileId >= 0 && destTileId != tile)
                    {
                        reason = "TSA_WD_EstablishOutpost_WaitUntilStopped".Translate().ToString();
                        return true;
                    }
                }
                catch
                {
                    // If destination cannot be read, do not block (same spirit as relaxed add-to-outpost).
                }
            }

            ok = true;
            return true;
        }

        private static bool EnsureVehicleCaravanStopReflection()
        {
            if (vehicleCaravanStopReflectReady) return vehicleCaravanStopReflectOk;
            vehicleCaravanStopReflectReady = true;
            Type vcType = null;
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    vcType = asm.GetType(VehicleCaravanFullName, throwOnError: false);
                }
                catch
                {
                    continue;
                }

                if (vcType != null) break;
            }

            if (vcType == null) return false;
            vehicleCaravan_vehiclePatherField = vcType.GetField("vehiclePather", BindingFlags.Public | BindingFlags.Instance);
            if (vehicleCaravan_vehiclePatherField == null) return false;
            Type patherT = vehicleCaravan_vehiclePatherField.FieldType;
            vehicleCaravanPather_MovingNow = patherT.GetProperty("MovingNow", BindingFlags.Public | BindingFlags.Instance);
            vehicleCaravanPather_Destination = patherT.GetProperty("Destination", BindingFlags.Public | BindingFlags.Instance);
            vehicleCaravan_AerialVehicleProperty = vcType.GetProperty("AerialVehicle", BindingFlags.Public | BindingFlags.Instance);
            if (vehicleCaravanPather_MovingNow == null || vehicleCaravanPather_MovingNow.PropertyType != typeof(bool))
                return false;
            vehicleCaravanStopReflectOk = true;
            return true;
        }

        private static int TryPlanetTileLikeToTileId(object planetTile)
        {
            if (planetTile == null) return -1;
            Type t = planetTile.GetType();
            const BindingFlags bind = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
            foreach (string name in new[] { "tileId", "TileId", "tile" })
            {
                PropertyInfo prop = t.GetProperty(name, bind);
                if (prop != null && prop.PropertyType == typeof(int))
                    return (int)prop.GetValue(planetTile);
                FieldInfo fld = t.GetField(name, bind);
                if (fld != null && fld.FieldType == typeof(int))
                    return (int)fld.GetValue(planetTile);
            }

            return -1;
        }

        /// <summary>
        /// If VF is loaded and <paramref name="pawn"/> is aboard a <c>VehiclePawn</c>, call
        /// <c>VehiclePawn.RemovePawn(pawn)</c> (same order as VF stash/merge code). Call before <see cref="Caravan.RemovePawn"/>.
        /// </summary>
        public static void TryEjectPawnFromHostingVehicle(Pawn pawn)
        {
            if (pawn == null) return;
            EnsureExtVehiclesGetVehicle();
            if (extVehiclesGetVehicle == null) return;
            object vehicle;
            try
            {
                vehicle = extVehiclesGetVehicle.Invoke(null, new object[] { pawn });
            }
            catch (Exception ex)
            {
                Log.Warning($"[WD] Vehicle Framework compat: GetVehicle failed: {ex.Message}");
                return;
            }

            if (vehicle == null) return;
            MethodInfo remove = vehicle.GetType().GetMethod(
                "RemovePawn",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(Pawn) },
                null);
            if (remove == null) return;
            try
            {
                remove.Invoke(vehicle, new object[] { pawn });
            }
            catch (Exception ex)
            {
                Log.Warning($"[WD] Vehicle Framework compat: VehiclePawn.RemovePawn failed: {ex.Message}");
            }
        }

        private static void EnsureExtVehiclesGetVehicle()
        {
            if (extVehiclesLookupDone) return;
            extVehiclesLookupDone = true;
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t;
                try
                {
                    t = asm.GetType(ExtVehiclesFullName, throwOnError: false);
                }
                catch
                {
                    continue;
                }

                if (t == null) continue;
                foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (m.Name != "GetVehicle") continue;
                    ParameterInfo[] ps = m.GetParameters();
                    if (ps.Length != 1 || ps[0].ParameterType != typeof(Pawn)) continue;
                    extVehiclesGetVehicle = m;
                    return;
                }
            }
        }

        /// <summary>
        /// Remove VF vehicles from the caravan, destroy VF stashed-vehicle world objects at <paramref name="tile"/>.
        /// Call after virtual-food credit, before destroying remaining caravan pawns/inventory.
        /// </summary>
        public static void CleanupWhenDissolvingPlayerCaravanIntoOutpost(Caravan caravan, int tile)
        {
            DestroyStashedVehiclesAtTileForPlayer(tile);
            RemoveAllVehicleFrameworkVehiclePawnsFromCaravan(caravan);
        }

        /// <summary>True if this caravan instance is still listed in <c>Find.WorldObjects</c> (not only a dangling reference).</summary>
        public static bool CaravanIsRegisteredOnWorld(Caravan caravan)
        {
            if (caravan == null) return false;
            var all = Find.WorldObjects?.AllWorldObjects;
            if (all == null) return false;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] == caravan)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// After founding an outpost from a caravan: VF can leave an empty player <see cref="Caravan"/> on the tile
        /// whose label is still the generic <c>Caravan</c> (real caravans are named e.g. colonist + " Caravan").
        /// Only those generic-label shells are removed.
        /// </summary>
        public static void DestroyAllPlayerCaravansOnTileAfterOutpostFounding(int tile)
        {
            if (tile < 0 || Find.WorldObjects == null) return;
            var at = Find.WorldObjects.ObjectsAt(tile);
            if (at == null) return;

            var caravans = new List<Caravan>();
            foreach (WorldObject wo in at)
            {
                if (wo is Caravan c && !c.Destroyed && c.Faction == Faction.OfPlayer && CaravanHasGenericGhostLabel(c))
                    caravans.Add(c);
            }

            for (int i = 0; i < caravans.Count; i++)
            {
                Caravan c = caravans[i];
                if (c == null || c.Destroyed) continue;
                WDVerbose.Msg($"Outpost founding: tile {tile} sweep — removing player caravan ({c.GetType().Name}) {c.LabelCap}");
                try
                {
                    DestroyCaravanWorldObjectAfterOutpostDissolve(c);
                }
                catch (Exception ex)
                {
                    Log.Warning($"[WD] Outpost founding tile sweep: {ex.Message}");
                }

                if (!c.Destroyed && CaravanIsRegisteredOnWorld(c))
                {
                    try
                    {
                        Find.WorldObjects.Remove(c);
                    }
                    catch (Exception ex2)
                    {
                        Log.Warning($"[WD] Outpost founding tile sweep Remove: {ex2.Message}");
                    }
                }
            }
        }

        private static bool CaravanHasGenericGhostLabel(Caravan c)
        {
            if (c == null) return false;
            string cap = c.LabelCap?.Trim() ?? "";
            string lab = c.Label?.Trim() ?? "";
            return cap == GenericGhostCaravanLabel || lab == GenericGhostCaravanLabel;
        }

        /// <summary>
        /// Final step after outpost dissolve: <c>Vehicles.World.VehicleCaravan</c> can still satisfy
        /// <see cref="Caravan.PawnsListForReading"/> empty while keeping vehicle hulls off that list, so
        /// <see cref="Caravan.Destroy"/> does not always remove the world object. Strip vehicles again, destroy, then
        /// <c>Find.WorldObjects.Remove</c> if needed.
        /// </summary>
        public static void DestroyCaravanWorldObjectAfterOutpostDissolve(Caravan caravan)
        {
            if (caravan == null || caravan.Destroyed) return;
            if (!CaravanIsRegisteredOnWorld(caravan))
            {
                WDVerbose.Msg("Outpost dissolve: DestroyCaravanWorldObject skipped (caravan no longer in WorldObjects)");
                return;
            }

            RemoveAllVehicleFrameworkVehiclePawnsFromCaravan(caravan);

            try
            {
                caravan.Destroy();
            }
            catch (Exception ex)
            {
                Log.Warning($"[WD] Vehicle Framework compat: Caravan.Destroy after dissolve: {ex.Message}");
            }

            if (caravan.Destroyed)
            {
                WDVerbose.Msg($"Outpost dissolve: caravan world object destroyed ({caravan.GetType().Name})");
                return;
            }

            WDVerbose.Msg(
                $"Outpost dissolve: Caravan.Destroy left object alive ({caravan.GetType().FullName}); forcing Find.WorldObjects.Remove");
            try
            {
                Find.WorldObjects.Remove(caravan);
            }
            catch (Exception ex2)
            {
                Log.Warning($"[WD] Vehicle Framework compat: WorldObjects.Remove caravan after dissolve: {ex2.Message}");
            }
        }

        /// <summary>
        /// VF <c>VehiclePawn</c> may appear on a vanilla <see cref="Caravan"/> or only on <c>VehiclesListForReading</c>.
        /// Collect from both, disembark everyone, remove and destroy each vehicle.
        /// </summary>
        private static void RemoveAllVehicleFrameworkVehiclePawnsFromCaravan(Caravan caravan)
        {
            if (caravan == null || caravan.Destroyed) return;
            var vehicles = new HashSet<Pawn>();

            if (string.Equals(caravan.GetType().FullName, VehicleCaravanFullName, StringComparison.Ordinal))
            {
                vehiclesListProperty ??= caravan.GetType().GetProperty(
                    "VehiclesListForReading",
                    BindingFlags.Public | BindingFlags.Instance);
                object rawList = vehiclesListProperty?.GetValue(caravan);
                if (rawList is IEnumerable vehEnum)
                {
                    foreach (object o in vehEnum)
                    {
                        if (o is Pawn vp && !vp.Destroyed && IsVehicleFrameworkVehiclePawn(vp))
                            vehicles.Add(vp);
                    }
                }
            }

            var reading = caravan.PawnsListForReading;
            if (reading != null)
            {
                for (int i = 0; i < reading.Count; i++)
                {
                    Pawn p = reading[i];
                    if (p != null && !p.Destroyed && IsVehicleFrameworkVehiclePawn(p))
                        vehicles.Add(p);
                }
            }

            foreach (Pawn vp in vehicles)
            {
                if (vp == null || vp.Destroyed) continue;
                try
                {
                    WDVerbose.Msg($"Outpost dissolve: vehicle disembark/remove begin {vp.LabelShortCap}");
                    TryDisembarkEveryoneFromVehiclePawn(vp);
                    if (!caravan.Destroyed && CaravanContainsPawn(caravan, vp))
                        caravan.RemovePawn(vp);
                    if (!vp.Destroyed)
                        vp.Destroy(DestroyMode.Vanish);
                    WDVerbose.Msg($"Outpost dissolve: vehicle destroyed {vp.LabelShortCap}");
                }
                catch (Exception ex)
                {
                    Log.Warning($"[WD] Vehicle Framework compat: vehicle pawn cleanup: {ex.Message}");
                }
            }
        }

        private static bool CaravanContainsPawn(Caravan caravan, Pawn p)
        {
            var list = caravan?.PawnsListForReading;
            if (list == null) return false;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == p) return true;
            }

            return false;
        }

        /// <summary>
        /// Adds cargo held directly by VF vehicle pawns. Applies to both ground and aerial vehicle caravans,
        /// and skips anything already exposed by <see cref="CaravanInventoryUtility.AllInventoryItems"/>.
        /// </summary>
        public static void AppendVehicleInventoryItems(Caravan caravan, List<Thing> items)
        {
            if (caravan == null || caravan.Destroyed || items == null) return;

            var seenItems = new HashSet<Thing>(items);
            var vehicles = new List<Pawn>();
            CollectVehiclePawns(caravan, vehicles);
            for (int i = 0; i < vehicles.Count; i++)
            {
                ThingOwner inventory = vehicles[i]?.inventory?.innerContainer;
                if (inventory == null) continue;
                for (int j = 0; j < inventory.Count; j++)
                {
                    Thing thing = inventory[j];
                    if (thing != null && !thing.Destroyed && seenItems.Add(thing))
                        items.Add(thing);
                }
            }
        }

        /// <summary>Finds the VF vehicle pawn whose direct inventory owns <paramref name="thing"/>.</summary>
        public static Pawn TryGetVehicleInventoryOwner(Caravan caravan, Thing thing)
        {
            if (caravan == null || caravan.Destroyed || thing == null) return null;

            var vehicles = new List<Pawn>();
            CollectVehiclePawns(caravan, vehicles);
            for (int i = 0; i < vehicles.Count; i++)
            {
                Pawn vehicle = vehicles[i];
                ThingOwner inventory = vehicle?.inventory?.innerContainer;
                if (inventory == null) continue;
                for (int j = 0; j < inventory.Count; j++)
                {
                    if (inventory[j] == thing)
                        return vehicle;
                }
            }

            return null;
        }

        private static void CollectVehiclePawns(Caravan caravan, List<Pawn> vehicles)
        {
            if (caravan == null || vehicles == null) return;
            var seen = new HashSet<Pawn>();

            void Add(Pawn pawn)
            {
                if (pawn != null && !pawn.Destroyed && IsVehicleFrameworkVehiclePawn(pawn) && seen.Add(pawn))
                    vehicles.Add(pawn);
            }

            var pawns = caravan.PawnsListForReading;
            if (pawns != null)
            {
                for (int i = 0; i < pawns.Count; i++)
                    Add(pawns[i]);
            }

            if (!IsVehicleFrameworkVehicleCaravan(caravan)) return;
            vehiclesListProperty ??= caravan.GetType().GetProperty(
                "VehiclesListForReading",
                BindingFlags.Public | BindingFlags.Instance);
            try
            {
                if (vehiclesListProperty?.GetValue(caravan) is IEnumerable vehicleEnumerable)
                {
                    foreach (object entry in vehicleEnumerable)
                    {
                        if (entry is Pawn vehicle)
                            Add(vehicle);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[WD] Vehicle Framework compat: read vehicle inventory owners failed: {ex.Message}");
            }
        }

        public static Texture TryGetVehicleIcon(Pawn vehiclePawn)
        {
            if (!IsVehicleFrameworkVehiclePawn(vehiclePawn)) return null;
            Type vt = vehiclePawn.GetType();

            try
            {
                vehiclePawnUiIconOverrideProperty ??= vt.GetProperty("UIIconOverride", BindingFlags.Public | BindingFlags.Instance);
                if (vehiclePawnUiIconOverrideProperty?.GetValue(vehiclePawn) is Texture tex && tex != null)
                    return tex;
            }
            catch
            {
                // Fall through to def icons; UI drawing should never fail because a compat icon did.
            }

            if (vehiclePawn.def?.uiIcon != null)
                return vehiclePawn.def.uiIcon;

            try
            {
                vehiclePawnVehicleDefProperty ??= vt.GetProperty("VehicleDef", BindingFlags.Public | BindingFlags.Instance);
                object vehicleDef = vehiclePawnVehicleDefProperty?.GetValue(vehiclePawn);
                if (vehicleDef is ThingDef thingDef && thingDef.uiIcon != null)
                    return thingDef.uiIcon;
            }
            catch
            {
                // Ignore and return null.
            }

            return null;
        }

        public static void TryAutoBoardPawnsIntoSelectedVehicles(Caravan caravan, IReadOnlyList<Pawn> removedPawns)
        {
            if (caravan == null || caravan.Destroyed || removedPawns == null || removedPawns.Count == 0) return;

            var vehicles = new List<Pawn>();
            var candidates = new List<Pawn>();
            for (int i = 0; i < removedPawns.Count; i++)
            {
                Pawn pawn = removedPawns[i];
                if (pawn == null || pawn.Destroyed || pawn.Dead) continue;
                if (IsVehicleFrameworkVehiclePawn(pawn))
                    vehicles.Add(pawn);
                else if (pawn.RaceProps?.Humanlike == true)
                    candidates.Add(pawn);
            }

            if (vehicles.Count == 0 || candidates.Count == 0) return;

            for (int vi = 0; vi < vehicles.Count; vi++)
            {
                Pawn vehicle = vehicles[vi];
                if (vehicle == null || vehicle.Destroyed) continue;
                Type vt = vehicle.GetType();
                vehiclePawnTryAddPawnMethod ??= vt.GetMethod(
                    "TryAddPawn",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new[] { typeof(Pawn) },
                    null);
                if (vehiclePawnTryAddPawnMethod == null) return;

                for (int pi = candidates.Count - 1; pi >= 0; pi--)
                {
                    Pawn pawn = candidates[pi];
                    if (pawn == null || pawn.Destroyed || pawn.Dead) continue;
                    try
                    {
                        object result = vehiclePawnTryAddPawnMethod.Invoke(vehicle, new object[] { pawn });
                        if (result is bool boarded && boarded)
                            candidates.RemoveAt(pi);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[WD] Vehicle Framework compat: could not auto-board {pawn.LabelShortCap} into {vehicle.LabelShortCap}: {ex.Message}");
                    }
                }
            }
        }

        public static void TryDetachVehiclePawnFromCaravanForStorage(Caravan caravan, Pawn vehiclePawn)
        {
            if (caravan == null || caravan.Destroyed || vehiclePawn == null || vehiclePawn.Destroyed) return;
            if (!IsVehicleFrameworkVehiclePawn(vehiclePawn)) return;

            if (CaravanContainsPawn(caravan, vehiclePawn))
            {
                try
                {
                    caravan.RemovePawn(vehiclePawn);
                }
                catch (Exception ex)
                {
                    Log.Warning($"[WD] Vehicle Framework compat: caravan.RemovePawn before vehicle storage failed: {ex.Message}");
                }
            }

            if (!string.Equals(caravan.GetType().FullName, VehicleCaravanFullName, StringComparison.Ordinal))
                return;

            vehiclesListProperty ??= caravan.GetType().GetProperty(
                "VehiclesListForReading",
                BindingFlags.Public | BindingFlags.Instance);
            object rawList = null;
            try
            {
                rawList = vehiclesListProperty?.GetValue(caravan);
            }
            catch (Exception ex)
            {
                Log.Warning($"[WD] Vehicle Framework compat: read VehiclesListForReading before storage failed: {ex.Message}");
            }

            if (rawList is IList list && list.Contains(vehiclePawn))
            {
                try
                {
                    list.Remove(vehiclePawn);
                }
                catch (Exception ex)
                {
                    Log.Warning($"[WD] Vehicle Framework compat: remove vehicle from VehiclesListForReading before storage failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// After <see cref="EjectAllPawnsFromHostVehiclesForOutpostDissolve"/>: every non-humanlike that must not
        /// survive dissolve — caravan roster plus anyone still listed aboard a VF vehicle on this caravan.
        /// VF vehicle removal can drop animals from <see cref="Caravan.PawnsListForReading"/> without destroying them;
        /// the outpost uses this list for a final <see cref="Pawn.Destroy"/> pass.
        /// </summary>
        public static void CollectNonHumanlikeDissolveSnapshotAfterEject(Caravan caravan, List<Pawn> dest, HashSet<Pawn> seen)
        {
            if (caravan == null || caravan.Destroyed || dest == null || seen == null) return;

            void tryAdd(Pawn p)
            {
                if (p == null || p.Destroyed) return;
                if (p.RaceProps?.Humanlike == true) return;
                if (!seen.Add(p)) return;
                dest.Add(p);
            }

            var reading = caravan.PawnsListForReading;
            if (reading != null)
            {
                for (int i = 0; i < reading.Count; i++)
                    tryAdd(reading[i]);
            }

            var vehicles = new List<Pawn>();
            var vehDup = new HashSet<Pawn>();
            void addVehicle(Pawn vp)
            {
                if (vp == null || vp.Destroyed || !IsVehicleFrameworkVehiclePawn(vp) || !vehDup.Add(vp))
                    return;
                vehicles.Add(vp);
            }

            if (string.Equals(caravan.GetType().FullName, VehicleCaravanFullName, StringComparison.Ordinal))
            {
                vehiclesListProperty ??= caravan.GetType().GetProperty(
                    "VehiclesListForReading",
                    BindingFlags.Public | BindingFlags.Instance);
                object rawList = vehiclesListProperty?.GetValue(caravan);
                if (rawList is IEnumerable vehEnum)
                {
                    foreach (object o in vehEnum)
                    {
                        if (o is Pawn vp)
                            addVehicle(vp);
                    }
                }
            }

            if (reading != null)
            {
                for (int i = 0; i < reading.Count; i++)
                {
                    Pawn p = reading[i];
                    if (p != null && !p.Destroyed)
                        addVehicle(p);
                }
            }

            for (int vi = 0; vi < vehicles.Count; vi++)
            {
                Pawn veh = vehicles[vi];
                if (veh == null || veh.Destroyed) continue;
                tryAdd(veh);
                AppendNonHumanlikeAboardAndCargoFromVehiclePawn(veh, tryAdd);
            }
        }

        private static void AppendNonHumanlikeAboardAndCargoFromVehiclePawn(Pawn vehiclePawn, Action<Pawn> tryAdd)
        {
            if (vehiclePawn == null || vehiclePawn.Destroyed || tryAdd == null) return;
            Type vt = vehiclePawn.GetType();
            PropertyInfo aboardProp = vt.GetProperty("AllPawnsAboard", BindingFlags.Public | BindingFlags.Instance);
            object rawList = aboardProp?.GetValue(vehiclePawn);
            if (rawList is IEnumerable enumerable)
            {
                foreach (object o in enumerable)
                {
                    if (o is Pawn ab)
                        tryAdd(ab);
                }
            }

            foreach (PropertyInfo prop in vt.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.Name == "AllPawnsAboard") continue;
                Type pt = prop.PropertyType;
                if (pt == typeof(string) || !typeof(IEnumerable).IsAssignableFrom(pt)) continue;
                string nl = prop.Name.ToLowerInvariant();
                if (!(nl.Contains("pawn") || nl.Contains("cargo") || nl.Contains("crew") || nl.Contains("passenger")
                      || nl.Contains("aboard") || nl.Contains("load") || nl.Contains("mount") || nl.Contains("hold")))
                    continue;
                if (nl.Contains("texture") || nl.Contains("icon") || nl.Contains("graphic") || nl.Contains("material"))
                    continue;

                object val;
                try
                {
                    val = prop.GetValue(vehiclePawn);
                }
                catch
                {
                    continue;
                }

                if (val is not IEnumerable propEnum || val is string)
                    continue;
                foreach (object o in propEnum)
                {
                    if (o is Pawn ab)
                        tryAdd(ab);
                }
            }
        }

        /// <summary>
        /// Before virtual-food credit or vehicle removal: eject every pawn from any hosting <c>VehiclePawn</c>
        /// (crew, mounts, cargo) so they rejoin the caravan list. Prevents pack animals staying only "inside"
        /// the vehicle and being orphaned when the vehicle pawn is destroyed.
        /// </summary>
        public static void EjectAllPawnsFromHostVehiclesForOutpostDissolve(Caravan caravan)
        {
            if (caravan == null || caravan.Destroyed) return;
            var seen = new HashSet<Pawn>();
            var toProcess = new List<Pawn>();

            void tryAdd(Pawn px)
            {
                if (px == null || px.Destroyed || !seen.Add(px)) return;
                toProcess.Add(px);
            }

            for (int round = 0; round < 8; round++)
            {
                int countBefore = toProcess.Count;
                var reading = caravan.PawnsListForReading;
                if (reading != null)
                {
                    for (int i = 0; i < reading.Count; i++)
                        tryAdd(reading[i]);
                }

                // VehicleCaravan can keep VehiclePawn only on VehiclesListForReading, not in PawnsListForReading.
                if (string.Equals(caravan.GetType().FullName, VehicleCaravanFullName, StringComparison.Ordinal))
                {
                    vehiclesListProperty ??= caravan.GetType().GetProperty(
                        "VehiclesListForReading",
                        BindingFlags.Public | BindingFlags.Instance);
                    object rawVeh = vehiclesListProperty?.GetValue(caravan);
                    if (rawVeh is IEnumerable vehEnum)
                    {
                        foreach (object o in vehEnum)
                        {
                            if (o is Pawn vp && !vp.Destroyed)
                                tryAdd(vp);
                        }
                    }
                }

                for (int i = 0; i < toProcess.Count; i++)
                {
                    Pawn p = toProcess[i];
                    if (!IsVehicleFrameworkVehiclePawn(p)) continue;
                    AppendAllPawnsAboardVehiclePawnIntoWorkList(p, toProcess, seen);
                }

                if (toProcess.Count == countBefore)
                    break;
            }

            WDVerbose.Msg($"Outpost dissolve: ejecting {toProcess.Count} pawn(s) from vehicles / staging (caravan {caravan.LabelCap})");
            for (int i = 0; i < toProcess.Count; i++)
                TryEjectPawnFromHostingVehicle(toProcess[i]);
        }

        private static void AppendAllPawnsAboardVehiclePawnIntoWorkList(Pawn vehiclePawn, List<Pawn> toProcess, HashSet<Pawn> seen)
        {
            if (vehiclePawn == null || vehiclePawn.Destroyed) return;
            Type vt = vehiclePawn.GetType();
            PropertyInfo aboardProp = vt.GetProperty("AllPawnsAboard", BindingFlags.Public | BindingFlags.Instance);
            object rawList = aboardProp?.GetValue(vehiclePawn);
            if (rawList is IEnumerable enumerable)
            {
                foreach (object o in enumerable)
                {
                    if (o is Pawn ab && !ab.Destroyed && seen.Add(ab))
                        toProcess.Add(ab);
                }
            }

            AppendVehiclePawnExtraEnumerablePawnsReflection(vehiclePawn, toProcess, seen);
        }

        /// <summary>
        /// VF versions differ: some cargo/mount lists are separate from <c>AllPawnsAboard</c>. Pull any enumerable
        /// of <see cref="Pawn"/> from vehicle public properties whose names look like cargo/crew/passenger lists.
        /// </summary>
        private static void AppendVehiclePawnExtraEnumerablePawnsReflection(Pawn vehiclePawn, List<Pawn> toProcess, HashSet<Pawn> seen)
        {
            if (vehiclePawn == null || vehiclePawn.Destroyed) return;
            Type vt = vehiclePawn.GetType();
            foreach (PropertyInfo prop in vt.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.Name == "AllPawnsAboard") continue;
                Type pt = prop.PropertyType;
                if (pt == typeof(string) || !typeof(IEnumerable).IsAssignableFrom(pt)) continue;
                string nl = prop.Name.ToLowerInvariant();
                if (!(nl.Contains("pawn") || nl.Contains("cargo") || nl.Contains("crew") || nl.Contains("passenger")
                      || nl.Contains("aboard") || nl.Contains("load") || nl.Contains("mount") || nl.Contains("hold")))
                    continue;
                if (nl.Contains("texture") || nl.Contains("icon") || nl.Contains("graphic") || nl.Contains("material"))
                    continue;

                object val;
                try
                {
                    val = prop.GetValue(vehiclePawn);
                }
                catch
                {
                    continue;
                }

                if (val is not IEnumerable enumerable || val is string)
                    continue;
                foreach (object o in enumerable)
                {
                    if (o is Pawn ab && !ab.Destroyed && seen.Add(ab))
                        toProcess.Add(ab);
                }
            }
        }

        public static bool IsVehicleFrameworkVehiclePawn(Pawn p) =>
            p != null && string.Equals(p.GetType().FullName, VehiclePawnFullName, StringComparison.Ordinal);

        /// <summary>
        /// Cheap roster path: seated crew (<c>AllPawnsAboard</c>) plus inventory cargo pawns
        /// (<c>AllInventoryPawns</c>). Cached property lookups only — no <c>GetProperties</c> sweep.
        /// </summary>
        public static void CollectPawnsAboardVehicleForRoster(Pawn vehiclePawn, List<Pawn> dest, HashSet<Pawn> seen)
        {
            if (dest == null || seen == null) return;
            if (!IsVehicleFrameworkVehiclePawn(vehiclePawn) || vehiclePawn.Destroyed) return;

            Type vt = vehiclePawn.GetType();
            vehiclePawnAllPawnsAboardProperty ??= vt.GetProperty("AllPawnsAboard", BindingFlags.Public | BindingFlags.Instance);
            vehiclePawnAllInventoryPawnsProperty ??= vt.GetProperty("AllInventoryPawns", BindingFlags.Public | BindingFlags.Instance);

            AppendPawnsFromEnumerableProperty(vehiclePawnAllPawnsAboardProperty, vehiclePawn, dest, seen);
            AppendPawnsFromEnumerableProperty(vehiclePawnAllInventoryPawnsProperty, vehiclePawn, dest, seen);
        }

        private static void AppendPawnsFromEnumerableProperty(
            PropertyInfo prop,
            Pawn vehiclePawn,
            List<Pawn> dest,
            HashSet<Pawn> seen)
        {
            if (prop == null) return;
            object raw;
            try
            {
                raw = prop.GetValue(vehiclePawn);
            }
            catch
            {
                return;
            }

            if (raw is not IEnumerable enumerable || raw is string) return;
            foreach (object o in enumerable)
            {
                if (o is Pawn ab && !ab.Destroyed && seen.Add(ab))
                    dest.Add(ab);
            }
        }

        /// <summary>
        /// Disembark crew then vanish a VF <c>VehiclePawn</c> so temporary encounters do not leave
        /// ticking orphans in <see cref="WorldPawns"/>.
        /// </summary>
        public static void DestroyVehiclePawnForCleanup(Pawn vehiclePawn)
        {
            if (vehiclePawn == null || vehiclePawn.Destroyed) return;
            if (!IsVehicleFrameworkVehiclePawn(vehiclePawn))
            {
                try
                {
                    if (!vehiclePawn.Destroyed)
                        vehiclePawn.Destroy(DestroyMode.Vanish);
                }
                catch (Exception ex)
                {
                    Log.Warning($"[WD] Vehicle Framework compat: destroy pawn cleanup: {ex.Message}");
                }
                return;
            }

            try
            {
                TryDisembarkEveryoneFromVehiclePawn(vehiclePawn);
                if (!vehiclePawn.Destroyed)
                    vehiclePawn.Destroy(DestroyMode.Vanish);
                if (Find.WorldPawns != null && Find.WorldPawns.Contains(vehiclePawn))
                    Find.WorldPawns.RemovePawn(vehiclePawn);
            }
            catch (Exception ex)
            {
                Log.Warning($"[WD] Vehicle Framework compat: destroy vehicle cleanup: {ex.Message}");
            }
        }

        public static void DestroyStashedVehiclesAtTileForPlayer(int tile)
        {
            if (tile < 0 || Find.WorldObjects == null) return;
            var at = Find.WorldObjects.ObjectsAt(tile);
            if (at == null) return;
            var toRemove = new List<WorldObject>();
            foreach (WorldObject wo in at)
            {
                if (wo == null || wo.Destroyed) continue;
                if (wo.Faction != Faction.OfPlayer) continue;
                if (!string.Equals(wo.GetType().FullName, StashedVehicleFullName, StringComparison.Ordinal)) continue;
                toRemove.Add(wo);
            }
            for (int i = 0; i < toRemove.Count; i++)
            {
                try
                {
                    toRemove[i]?.Destroy();
                    WDVerbose.Msg($"Outpost dissolve: StashedVehicle world object destroyed at tile {tile}");
                }
                catch (Exception ex)
                {
                    Log.Warning($"[WD] Vehicle Framework compat: could not destroy StashedVehicle at tile {tile}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Empty crew/cargo pawns from a <c>VehiclePawn</c> before removal so VF/caravan lists stay consistent.
        /// </summary>
        private static void TryDisembarkEveryoneFromVehiclePawn(Pawn vehiclePawn)
        {
            if (vehiclePawn == null || vehiclePawn.Destroyed) return;
            Type vt = vehiclePawn.GetType();
            MethodInfo disembarkAll = vt.GetMethod(
                "DisembarkAll",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                Type.EmptyTypes,
                null);
            if (disembarkAll != null)
            {
                try
                {
                    disembarkAll.Invoke(vehiclePawn, Array.Empty<object>());
                    return;
                }
                catch (Exception ex)
                {
                    Log.Warning($"[WD] Vehicle Framework compat: DisembarkAll failed, falling back: {ex.Message}");
                }
            }

            PropertyInfo aboardProp = vt.GetProperty("AllPawnsAboard", BindingFlags.Public | BindingFlags.Instance);
            object rawList = aboardProp?.GetValue(vehiclePawn);
            if (rawList is not IList list) return;
            MethodInfo removePawn = vt.GetMethod(
                "RemovePawn",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(Pawn) },
                null);
            if (removePawn == null) return;

            var buffer = new List<Pawn>();
            foreach (object o in list)
            {
                if (o is Pawn p && !p.Destroyed)
                    buffer.Add(p);
            }

            for (int i = 0; i < buffer.Count; i++)
            {
                Pawn p = buffer[i];
                if (p == null || p.Destroyed) continue;
                try
                {
                    removePawn.Invoke(vehiclePawn, new object[] { p });
                }
                catch (Exception ex)
                {
                    Log.Warning($"[WD] Vehicle Framework compat: RemovePawn aboard vehicle: {ex.Message}");
                }
            }
        }
    }
}
