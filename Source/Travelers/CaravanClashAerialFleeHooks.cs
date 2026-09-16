using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Dedicated clash aerial-flee hooks (not AA warn prefixes). Phase A on successful launch;
    /// Phase B when world airborne is added from the clash tile.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class CaravanClashAerialFleeHooks
    {
        static CaravanClashAerialFleeHooks()
        {
            try
            {
                var harmony = new Harmony("TSA.WorldDomination.CaravanClashAerialFlee");

                MethodInfo tryLaunch = AccessTools.Method(
                    typeof(CompLaunchable),
                    nameof(CompLaunchable.TryLaunch),
                    new[] { typeof(PlanetTile), typeof(TransportersArrivalAction) });
                if (tryLaunch != null)
                {
                    harmony.Patch(
                        tryLaunch,
                        postfix: new HarmonyMethod(typeof(CaravanClashAerialFleeHooks), nameof(CompLaunchable_TryLaunch_Postfix)));
                }

                int vfPatched = 0;
                vfPatched += TryPatchLaunchPostfix(harmony, AccessTools.TypeByName("Vehicles.CompVehicleLauncher"));
                vfPatched += TryPatchLaunchPostfix(harmony, AccessTools.TypeByName("Vehicles.World.VehicleCaravan"));
                vfPatched += TryPatchLaunchPostfix(
                    harmony,
                    AccessTools.TypeByName(VehicleFrameworkAerialAaCompat.AerialTypeName));

                Log.Message(
                    $"[TSA WD] Caravan clash aerial flee hooks active (TryLaunch={(tryLaunch != null)}, VF Launch={vfPatched}).");
            }
            catch (Exception ex)
            {
                Log.Warning($"[TSA WD] Caravan clash aerial flee hooks disabled: {ex.Message}");
            }
        }

        private static int TryPatchLaunchPostfix(Harmony harmony, Type type)
        {
            if (type == null) return 0;
            MethodInfo launch = AccessTools.Method(type, "Launch");
            if (launch == null) return 0;
            harmony.Patch(
                launch,
                postfix: new HarmonyMethod(typeof(CaravanClashAerialFleeHooks), nameof(VfLaunch_Postfix)));
            return 1;
        }

        public static void CompLaunchable_TryLaunch_Postfix(CompLaunchable __instance)
        {
            try
            {
                Thing parent = __instance?.parent;
                if (parent == null) return;
                if (parent.Faction == null || !parent.Faction.IsPlayer) return;
                WD_MapComponent_CaravanClash.NotifyPossibleAerialLeaveFromMap(parent.Map);
            }
            catch (Exception ex)
            {
                Log.WarningOnce(
                    $"[TSA WD] Clash aerial flee TryLaunch postfix failed: {ex.Message}",
                    "WD_ClashAerialFlee_TryLaunch".GetHashCode());
            }
        }

        public static void VfLaunch_Postfix(object __instance)
        {
            try
            {
                if (__instance is ThingComp comp)
                {
                    Thing parent = comp.parent;
                    if (parent == null) return;
                    if (parent.Faction == null || !parent.Faction.IsPlayer) return;
                    WD_MapComponent_CaravanClash.NotifyPossibleAerialLeaveFromMap(parent.Map);
                    return;
                }

                if (__instance is WorldObject wo)
                {
                    if (wo.Faction == null || !wo.Faction.IsPlayer) return;
                    if (wo.Tile.Valid)
                        WD_MapComponent_CaravanClash.NotifyWorldAirborneFromStartTile(wo.Tile.tileId);
                }
            }
            catch (Exception ex)
            {
                Log.WarningOnce(
                    $"[TSA WD] Clash aerial flee VF Launch postfix failed: {ex.Message}",
                    "WD_ClashAerialFlee_VfLaunch".GetHashCode());
            }
        }
    }
}
