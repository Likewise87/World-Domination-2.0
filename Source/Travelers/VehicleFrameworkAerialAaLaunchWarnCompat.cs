using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Soft confirm when the player picks a VF aerial destination on the world map
    /// (before takeoff / before OrderFlyToTiles). Mid/Late gates match RR drop-pod warn.
    /// Removable: delete this file (+ i18n keys). Does not touch combat AA hooks in
    /// <see cref="VehicleFrameworkAerialAaCompat"/>.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class VehicleFrameworkAerialAaLaunchWarnCompat
    {
        private static bool active;
        private static FieldInfo targetDataTargetsField;
        private static bool allowNext;
        private static bool loggedSignatureFail;

        private static MethodInfo pendingMethod;
        private static object pendingInstance;
        private static object[] pendingArgs;

        static VehicleFrameworkAerialAaLaunchWarnCompat()
        {
            if (!VehicleFrameworkAerialAaCompat.IsVehicleFrameworkActive())
                return;

            try
            {
                Type targetDataType = AccessTools.TypeByName("SmashTools.Targeting.TargetData`1")
                    ?.MakeGenericType(typeof(GlobalTargetInfo));
                if (targetDataType != null)
                    targetDataTargetsField = AccessTools.Field(targetDataType, "targets");

                if (targetDataTargetsField == null)
                {
                    Log.Warning("[TSA WD] VF aerial AA launch warn: TargetData.targets not found; disabled.");
                    return;
                }

                var harmony = new Harmony("TSA.WorldDomination.VehicleFrameworkAerialAaLaunchWarn");
                int patched = 0;

                // Map launch: runs when world destination is confirmed, before skyfaller / world aerial.
                patched += TryPatchLaunch(
                    harmony,
                    AccessTools.TypeByName("Vehicles.CompVehicleLauncher"),
                    nameof(Launch_Prefix));

                // Caravan launch: creates aerial + flies; warn before that.
                patched += TryPatchLaunch(
                    harmony,
                    AccessTools.TypeByName("Vehicles.World.VehicleCaravan"),
                    nameof(Launch_Prefix));

                // Already-in-flight retarget: warn before path change.
                patched += TryPatchLaunch(
                    harmony,
                    AccessTools.TypeByName(VehicleFrameworkAerialAaCompat.AerialTypeName),
                    nameof(Launch_Prefix));

                if (patched == 0)
                {
                    Log.Warning("[TSA WD] VF aerial AA launch warn: no Launch methods patched; disabled.");
                    return;
                }

                active = true;
                Log.Message($"[TSA WD] Vehicle Framework aerial AA launch warning active ({patched} Launch hook(s)).");
            }
            catch (Exception ex)
            {
                Log.Warning($"[TSA WD] Vehicle Framework aerial AA launch warning disabled: {ex.Message}");
            }
        }

        public static bool Active => active;

        private static int TryPatchLaunch(Harmony harmony, Type type, string prefixName)
        {
            if (type == null) return 0;
            MethodInfo launch = AccessTools.Method(type, "Launch");
            if (launch == null) return 0;

            harmony.Patch(
                launch,
                prefix: new HarmonyMethod(typeof(VehicleFrameworkAerialAaLaunchWarnCompat), prefixName));
            return 1;
        }

        /// <summary>
        /// Shared prefix for CompVehicleLauncher / VehicleCaravan / AerialVehicleInFlight.Launch.
        /// Returns false to skip the original when a confirm dialog is shown.
        /// </summary>
        public static bool Launch_Prefix(object __instance, MethodBase __originalMethod, object targetData, object arrivalAction)
        {
            if (!active) return true;
            if (__instance == null || __originalMethod == null) return true;

            if (allowNext)
            {
                allowNext = false;
                return true;
            }

            if (!TryResolvePlayerOrigin(__instance, out int originTile))
                return true;

            if (!TryGetLastTargetTileId(targetData, out int destTile) || destTile < 0)
            {
                if (!loggedSignatureFail)
                {
                    loggedSignatureFail = true;
                    Log.Warning("[TSA WD] VF aerial AA launch warn: could not resolve destination tile; allowing launch without warn.");
                }
                return true;
            }

            var threats = new List<Settlement>();
            if (!AntiAirFireUtils.TryGetHostileSettlementAaThreatsForDropPodFlight(originTile, destTile, threats)
                || threats.Count == 0)
                return true;

            string names = threats[0].LabelCap;
            for (int i = 1; i < threats.Count; i++)
                names += ", " + threats[i].LabelCap;

            pendingMethod = __originalMethod as MethodInfo;
            pendingInstance = __instance;
            pendingArgs = new object[] { targetData, arrivalAction };

            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "TSA_WD_AntiAir_VfAerial_AaWarning".Translate(names),
                ConfirmAndReinvoke,
                destructive: true));

            return false;
        }

        private static void ConfirmAndReinvoke()
        {
            MethodInfo method = pendingMethod;
            object instance = pendingInstance;
            object[] args = pendingArgs;
            pendingMethod = null;
            pendingInstance = null;
            pendingArgs = null;

            if (method == null || instance == null || args == null)
                return;

            if (instance is WorldObject wo && wo.Destroyed)
                return;
            if (instance is ThingComp tc && (tc.parent == null || tc.parent.Destroyed))
                return;

            try
            {
                allowNext = true;
                method.Invoke(instance, args);
            }
            catch (Exception ex)
            {
                allowNext = false;
                Log.Warning($"[TSA WD] VF aerial AA launch warn re-invoke failed: {ex.Message}");
            }
        }

        private static bool TryResolvePlayerOrigin(object instance, out int originTile)
        {
            originTile = -1;

            if (instance is WorldObject worldObject)
            {
                if (worldObject.Faction == null || !worldObject.Faction.IsPlayer)
                    return false;
                if (!worldObject.Tile.Valid)
                    return false;
                originTile = worldObject.Tile.tileId;
                return originTile >= 0;
            }

            if (instance is ThingComp comp)
            {
                Thing parent = comp.parent;
                if (parent == null || parent.Faction == null || !parent.Faction.IsPlayer)
                    return false;

                // Prefer map tile while still on a colony map; fall back to Comp.Tile if present.
                if (parent.Map != null && parent.Map.Tile.Valid)
                {
                    originTile = parent.Map.Tile.tileId;
                    return originTile >= 0;
                }

                PropertyInfo tileProp = AccessTools.Property(comp.GetType(), "Tile");
                if (tileProp != null)
                {
                    object tileObj = tileProp.GetValue(comp, null);
                    if (tileObj is PlanetTile pt && pt.Valid)
                    {
                        originTile = pt.tileId;
                        return originTile >= 0;
                    }
                }
            }

            return false;
        }

        private static bool TryGetLastTargetTileId(object targetData, out int tileId)
        {
            tileId = -1;
            if (targetData == null || targetDataTargetsField == null)
                return false;

            try
            {
                object targetsObj = targetDataTargetsField.GetValue(targetData);
                if (targetsObj is IList list)
                {
                    if (list.Count == 0) return false;
                    return TryReadGlobalTargetTile(list[list.Count - 1], out tileId);
                }

                object last = null;
                if (targetsObj is IEnumerable enumerable)
                {
                    foreach (object item in enumerable)
                        last = item;
                }
                if (last == null) return false;
                return TryReadGlobalTargetTile(last, out tileId);
            }
            catch (Exception ex)
            {
                WDVerbose.Msg($"VF aerial AA warn TargetData parse fail: {ex.Message}");
                return false;
            }
        }

        private static bool TryReadGlobalTargetTile(object target, out int tileId)
        {
            tileId = -1;
            if (target is GlobalTargetInfo gti && gti.IsValid)
            {
                tileId = gti.Tile.tileId;
                return tileId >= 0;
            }
            return false;
        }
    }
}
