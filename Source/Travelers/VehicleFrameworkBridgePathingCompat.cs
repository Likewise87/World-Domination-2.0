using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;
using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Soft Vehicle Framework compat so land vehicle caravans treat WD bridged water as passable.
    /// VF's WorldVehiclePathGrid marks Ocean impassable before roads; road-mult compat cannot help.
    /// No hard reference to Vehicles.dll — inactive when VF is not loaded.
    /// Not gated on Roads of the Rim (unlike road-movement compat).
    /// </summary>
    [StaticConstructorOnStartup]
    public static class VehicleFrameworkBridgePathingCompat
    {
        private const float ImpassableCost = 1000f;
        private const float BridgeBaseCost = 1f;
        private const int GridStateRegionsDirty = 2;

        private static bool active;
        private static Type vehicleDefType;
        private static Type pathGridType;
        private static PropertyInfo instanceProp;
        private static FieldInfo pathGridsField;
        private static MethodInfo recalculatePerceived;
        private static FieldInfo onReachabilityDirtyField;
        private static MethodInfo getOwnerMethod;
        private static object worldGridOwners;
        private static PropertyInfo defIndexProp;
        private static PropertyInfo allVehicleDefsProp;

        static VehicleFrameworkBridgePathingCompat()
        {
            if (!VehicleFrameworkRoadMovementCompat.IsVehicleFrameworkActive())
                return;

            try
            {
                pathGridType = AccessTools.TypeByName("Vehicles.World.WorldVehiclePathGrid");
                vehicleDefType = AccessTools.TypeByName("Vehicles.VehicleDef");
                if (pathGridType == null || vehicleDefType == null)
                {
                    Log.Warning("[TSA WD] Vehicle Framework bridge pathing: types not found; disabled.");
                    return;
                }

                instanceProp = AccessTools.Property(pathGridType, "Instance");
                pathGridsField = AccessTools.Field(pathGridType, "pathGrids");
                recalculatePerceived = AccessTools.Method(
                    pathGridType,
                    "RecalculatePerceivedMovementDifficultyAt",
                    new[] { typeof(PlanetTile), vehicleDefType });
                onReachabilityDirtyField = AccessTools.Field(pathGridType, "OnReachabilityDirty");
                defIndexProp = AccessTools.Property(vehicleDefType, "DefIndex");

                Type defDb = typeof(DefDatabase<>).MakeGenericType(vehicleDefType);
                allVehicleDefsProp = AccessTools.Property(defDb, "AllDefsListForReading");

                Type gridOwnersType = AccessTools.TypeByName("Vehicles.GridOwners");
                if (gridOwnersType != null)
                {
                    PropertyInfo worldProp = AccessTools.Property(gridOwnersType, "World");
                    worldGridOwners = worldProp?.GetValue(null);
                    if (worldGridOwners != null)
                        getOwnerMethod = AccessTools.Method(worldGridOwners.GetType(), "GetOwner", new[] { vehicleDefType });
                }

                MethodInfo calculated = AccessTools.Method(
                    pathGridType,
                    "CalculatedMovementDifficultyAt",
                    new[] { typeof(PlanetTile), vehicleDefType, typeof(StringBuilder), typeof(bool) });
                if (calculated == null)
                {
                    Log.Warning("[TSA WD] Vehicle Framework bridge pathing: CalculatedMovementDifficultyAt not found; disabled.");
                    return;
                }

                var harmony = new Harmony("TSA.WorldDomination.VehicleFrameworkBridgePathing");
                harmony.Patch(
                    calculated,
                    prefix: new HarmonyMethod(typeof(VehicleFrameworkBridgePathingCompat), nameof(PrefixCalculatedDifficulty)));

                if (recalculatePerceived == null || instanceProp == null)
                {
                    Log.Warning("[TSA WD] Vehicle Framework bridge pathing: grid refresh APIs missing; Prefix only.");
                }

                active = true;
                Log.Message("[TSA WD] Vehicle Framework bridge pathing compat active.");
            }
            catch (Exception ex)
            {
                active = false;
                Log.Warning($"[TSA WD] Vehicle Framework bridge pathing compat disabled: {ex.Message}");
            }
        }

        public static bool Active => active;

        /// <summary>
        /// Harmony Prefix: bridged water uses land-like cost so VF cache rebuilds stay passable.
        /// Omits VehicleDef from the signature so no optional-mod type appears in our IL;
        /// winter uses vanilla offset (close enough for bridge hops).
        /// </summary>
        public static bool PrefixCalculatedDifficulty(
            PlanetTile tile,
            StringBuilder explanation,
            ref float __result)
        {
            try
            {
                if (!tile.Valid || !WorldComponent_WdBridges.IsBridgedWaterTile(tile))
                    return true;

                if (explanation != null && explanation.Length > 0)
                    explanation.AppendLine();
                explanation?.Append("TSA_WD_Bridge_MovementDifficultyBase".Translate()
                    + ": " + BridgeBaseCost.ToStringWithSign("0.#"));

                __result = BridgeBaseCost + WorldPathGrid.GetCurrentWinterMovementDifficultyOffset(
                    tile, GenTicks.TicksAbs, explanation);
                return false;
            }
            catch (Exception ex)
            {
                Log.WarningOnce(
                    $"[TSA WD] VF bridge pathing Prefix failed: {ex.Message}",
                    "TSA_WD_VF_BridgePathing_Prefix".GetHashCode());
                return true;
            }
        }

        /// <summary>
        /// Heal VF path-grid cache + reachability after WD bridge register/unregister/load heal.
        /// </summary>
        public static void NotifyTilesChanged(IReadOnlyList<int> tileIds)
        {
            if (!active || tileIds == null || tileIds.Count == 0)
                return;
            if (recalculatePerceived == null || instanceProp == null || allVehicleDefsProp == null)
                return;

            try
            {
                object instance = instanceProp.GetValue(null);
                if (instance == null)
                    return;

                IList vehicleDefs = allVehicleDefsProp.GetValue(null) as IList;
                if (vehicleDefs == null || vehicleDefs.Count == 0)
                    return;

                PlanetLayer layer = PlanetSurfaceWorldActions.WdSurfaceLayer ?? Find.WorldGrid?.Surface;
                if (layer == null)
                    return;

                Array pathGrids = pathGridsField?.GetValue(instance) as Array;
                var dirtyOwners = new HashSet<object>();
                var dirtyDefsFallback = new List<object>();

                for (int d = 0; d < vehicleDefs.Count; d++)
                {
                    object vehicleDef = vehicleDefs[d];
                    if (vehicleDef == null)
                        continue;

                    bool flipped = false;
                    for (int i = 0; i < tileIds.Count; i++)
                    {
                        int id = tileIds[i];
                        if (id < 0)
                            continue;

                        PlanetTile pt = new PlanetTile(id, layer);
                        object stateObj = recalculatePerceived.Invoke(instance, new[] { pt, vehicleDef });
                        int state = stateObj is Enum e ? Convert.ToInt32(e) : 0;
                        if (state == GridStateRegionsDirty)
                            flipped = true;

                        // Mirror vanilla race fix: bridged water must stay < impassable in the cache.
                        if (WorldComponent_WdBridges.IsBridgedWaterTile(id) && pathGrids != null && defIndexProp != null)
                        {
                            int defIndex = (int)defIndexProp.GetValue(vehicleDef);
                            if (defIndex >= 0 && defIndex < pathGrids.Length)
                            {
                                object grid = pathGrids.GetValue(defIndex);
                                if (grid != null)
                                {
                                    float before = GetPathCost(grid, id);
                                    if (before >= ImpassableCost)
                                    {
                                        SetPathCost(grid, id, BridgeBaseCost);
                                        flipped = true;
                                    }
                                }
                            }
                        }
                    }

                    if (!flipped)
                        continue;

                    object owner = ResolveOwner(vehicleDef) ?? vehicleDef;
                    if (getOwnerMethod != null)
                    {
                        if (dirtyOwners.Add(owner))
                            dirtyDefsFallback.Add(owner);
                    }
                    else
                    {
                        dirtyDefsFallback.Add(vehicleDef);
                    }
                }

                for (int i = 0; i < dirtyDefsFallback.Count; i++)
                    InvokeReachabilityDirty(instance, dirtyDefsFallback[i]);
            }
            catch (Exception ex)
            {
                Log.WarningOnce(
                    $"[TSA WD] VF bridge pathing NotifyTilesChanged failed: {ex.Message}",
                    "TSA_WD_VF_BridgePathing_Notify".GetHashCode());
            }
        }

        private static object ResolveOwner(object vehicleDef)
        {
            if (getOwnerMethod == null || worldGridOwners == null || vehicleDef == null)
                return null;
            try
            {
                return getOwnerMethod.Invoke(worldGridOwners, new[] { vehicleDef });
            }
            catch
            {
                return null;
            }
        }

        private static void InvokeReachabilityDirty(object pathGridInstance, object vehicleDef)
        {
            if (onReachabilityDirtyField == null || pathGridInstance == null || vehicleDef == null)
                return;

            if (!(onReachabilityDirtyField.GetValue(pathGridInstance) is Delegate del) || del == null)
                return;

            try
            {
                del.DynamicInvoke(vehicleDef, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Log.WarningOnce(
                    $"[TSA WD] VF bridge pathing OnReachabilityDirty failed: {ex.Message}",
                    "TSA_WD_VF_BridgePathing_ReachDirty".GetHashCode());
            }
        }

        private static float GetPathCost(object pathGrid, int tileId)
        {
            PropertyInfo indexer = AccessTools.Property(pathGrid.GetType(), "Item");
            if (indexer != null)
                return (float)indexer.GetValue(pathGrid, new object[] { tileId });
            MethodInfo getter = AccessTools.Method(pathGrid.GetType(), "get_Item", new[] { typeof(int) });
            return getter != null ? (float)getter.Invoke(pathGrid, new object[] { tileId }) : 0f;
        }

        private static void SetPathCost(object pathGrid, int tileId, float cost)
        {
            PropertyInfo indexer = AccessTools.Property(pathGrid.GetType(), "Item");
            if (indexer != null)
            {
                indexer.SetValue(pathGrid, cost, new object[] { tileId });
                return;
            }

            MethodInfo setter = AccessTools.Method(pathGrid.GetType(), "set_Item", new[] { typeof(int), typeof(float) });
            setter?.Invoke(pathGrid, new object[] { tileId, cost });
        }
    }
}
