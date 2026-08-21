using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Soft Vehicle Framework compat so on-road hops use WD/vanilla road mult (tiers, winter)
    /// with road-block / pollution flat additives re-baked against VF terrain.
    /// No hard reference to Vehicles.dll — inactive when VF is not loaded.
    /// Skipped when Roads of the Rim is active (same gate as WD vanilla road movement writes);
    /// RotR owns road cost integration in that setup.
    /// </summary>
    /// <remarks>
    /// Released VF 1.6 exposes <c>RoadCostHelper.GetRoadMovementDifficultyMultiplier</c> (float).
    /// Newer/develop builds may use <c>GetRoadMovementMultiplier</c> returning a nested
    /// <c>RoadMultiplier</c> struct; both shapes are discovered and patched when present.
    /// </remarks>
    [StaticConstructorOnStartup]
    public static class VehicleFrameworkRoadMovementCompat
    {
        public const string PackageId = "SmashPhil.VehicleFramework";

        private static bool active;
        private static Type vehicleDefType;
        private static Type vehiclePawnType;
        private static PropertyInfo vehicleDefOnPawn;
        private static MethodInfo calculatedMovementDifficultyAt;
        private static Type roadMultiplierType;
        private static FieldInfo roadMultiplierRoadDefField;
        private static FieldInfo roadMultiplierMultiplierField;
        private static ConstructorInfo roadMultiplierCtor;

        static VehicleFrameworkRoadMovementCompat()
        {
            if (!IsVehicleFrameworkActive())
                return;

            // Match ApplyVanillaRoadMovementSettings: WD road mult ownership only without RotR.
            if (WorldActions_Roads.RoadsOfTheRimActive)
            {
                Log.Message("[TSA WD] Vehicle Framework road movement compat skipped (Roads of the Rim active).");
                return;
            }

            try
            {
                Type helperType = AccessTools.TypeByName("Vehicles.RoadCostHelper");
                if (helperType == null)
                {
                    Log.Warning("[TSA WD] Vehicle Framework road movement: RoadCostHelper not found; disabled.");
                    return;
                }

                vehicleDefType = AccessTools.TypeByName("Vehicles.VehicleDef");
                vehiclePawnType = AccessTools.TypeByName("Vehicles.VehiclePawn");
                if (vehiclePawnType != null)
                    vehicleDefOnPawn = AccessTools.Property(vehiclePawnType, "VehicleDef");

                Type pathGridType = AccessTools.TypeByName("Vehicles.World.WorldVehiclePathGrid");
                if (pathGridType != null)
                {
                    calculatedMovementDifficultyAt = AccessTools.Method(
                        pathGridType,
                        "CalculatedMovementDifficultyAt",
                        new[] { typeof(PlanetTile), vehicleDefType, typeof(StringBuilder), typeof(bool) });
                }

                roadMultiplierType = AccessTools.Inner(helperType, "RoadMultiplier");
                if (roadMultiplierType != null)
                {
                    roadMultiplierRoadDefField = AccessTools.Field(roadMultiplierType, "roadDef");
                    roadMultiplierMultiplierField = AccessTools.Field(roadMultiplierType, "multiplier");
                    roadMultiplierCtor = AccessTools.Constructor(roadMultiplierType, new[] { typeof(RoadDef), typeof(float) });
                }

                var harmony = new Harmony("TSA.WorldDomination.VehicleFrameworkRoadMovement");
                int patched = 0;

                foreach (MethodInfo method in AccessTools.GetDeclaredMethods(helperType))
                {
                    if (method == null || !method.IsStatic) continue;
                    if (method.Name != "GetRoadMovementDifficultyMultiplier"
                        && method.Name != "GetRoadMovementMultiplier")
                        continue;

                    ParameterInfo[] ps = method.GetParameters();
                    if (ps.Length < 3) continue;
                    // Tile overloads: (list, fromTile, toTile[, explanation]) — not the (list, RoadDef) helpers.
                    if (!IsTileIdOrPlanetTile(ps[1].ParameterType) || !IsTileIdOrPlanetTile(ps[2].ParameterType))
                        continue;

                    if (method.ReturnType == typeof(float))
                    {
                        harmony.Patch(method,
                            postfix: new HarmonyMethod(typeof(VehicleFrameworkRoadMovementCompat), nameof(PostfixFloat)));
                        patched++;
                    }
                    else if (roadMultiplierType != null && method.ReturnType == roadMultiplierType
                             && roadMultiplierCtor != null
                             && roadMultiplierRoadDefField != null
                             && roadMultiplierMultiplierField != null)
                    {
                        harmony.Patch(method,
                            postfix: new HarmonyMethod(typeof(VehicleFrameworkRoadMovementCompat), nameof(PostfixRoadMultiplier)));
                        patched++;
                    }
                }

                if (patched == 0)
                {
                    Log.Warning("[TSA WD] Vehicle Framework road movement: no compatible GetRoad* methods; disabled.");
                    return;
                }

                active = true;
                Log.Message($"[TSA WD] Vehicle Framework road movement compat active ({patched} patch(es)).");
            }
            catch (Exception ex)
            {
                Log.Warning($"[TSA WD] Vehicle Framework road movement compat disabled: {ex.Message}");
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

        private static bool IsTileIdOrPlanetTile(Type t) =>
            t == typeof(int) || t == typeof(PlanetTile);

        /// <summary>Harmony postfix for float-returning tile overloads (VF 1.6).</summary>
        public static void PostfixFloat(object __0, object __1, object __2, ref float __result)
        {
            try
            {
                if (!TryResolveTiles(__1, __2, out PlanetTile from, out PlanetTile to))
                    return;

                float adjusted = AdjustMultiplier(__result, HasRoadLink(from, to), __0, from, to);
                if (!Mathf.Approximately(adjusted, __result))
                    __result = adjusted;
            }
            catch (Exception ex)
            {
                Log.WarningOnce($"[TSA WD] VF road movement postfix failed: {ex.Message}",
                    "TSA_WD_VF_RoadMovement_PostfixFloat".GetHashCode());
            }
        }

        /// <summary>Harmony postfix for RoadMultiplier-returning overloads (newer VF).</summary>
        public static void PostfixRoadMultiplier(object __0, object __1, object __2, ref object __result)
        {
            try
            {
                if (__result == null || roadMultiplierType == null) return;
                if (!roadMultiplierType.IsInstanceOfType(__result)) return;
                if (!TryResolveTiles(__1, __2, out PlanetTile from, out PlanetTile to))
                    return;

                RoadDef roadDef = roadMultiplierRoadDefField.GetValue(__result) as RoadDef;
                float mult = (float)roadMultiplierMultiplierField.GetValue(__result);
                float adjusted = AdjustMultiplier(mult, roadDef != null, __0, from, to);
                if (Mathf.Approximately(adjusted, mult)) return;

                __result = roadMultiplierCtor.Invoke(new object[] { roadDef, adjusted });
            }
            catch (Exception ex)
            {
                Log.WarningOnce($"[TSA WD] VF road movement RoadMultiplier postfix failed: {ex.Message}",
                    "TSA_WD_VF_RoadMovement_PostfixStruct".GetHashCode());
            }
        }

        private static float AdjustMultiplier(
            float currentMult,
            bool onRoad,
            object vehicleList,
            PlanetTile from,
            PlanetTile to)
        {
            float blockPenalty = to.Valid ? WorldComponent_RoadBlocks.GetFlatPenalty(to.tileId) : 0f;
            float pollutionPenalty = GetPollutionFlatPenalty(to);
            float flat = blockPenalty + pollutionPenalty;

            if (onRoad)
            {
                if (!from.Valid || !to.Valid)
                    return currentMult;

                float wd = Find.WorldGrid.GetRoadMovementDifficultyMultiplier(from, to);
                float vTerrain = WorldPathGrid.CalculatedMovementDifficultyAt(to, false);
                if (vTerrain < 0.01f) vTerrain = 0.01f;
                float vfTerrain = GetBestVfTerrain(vehicleList, to);
                // WD baked flat/vTerrain into wd; VF hop uses vfTerrain * mult — re-bake so flats stay flat.
                return wd - flat / vTerrain + flat / vfTerrain;
            }

            if (flat <= 0f)
                return currentMult;

            float vfOff = GetBestVfTerrain(vehicleList, to.Valid ? to : from);
            return currentMult + flat / vfOff;
        }

        private static float GetPollutionFlatPenalty(PlanetTile to)
        {
            if (!WdPollutionPathContext.Active || !to.Valid) return 0f;
            var s = WorldDominationMod.settings;
            if (s == null || !s.travelerPollutionDamageEnabled || !s.pollutionPathCostEnabled)
                return 0f;

            float pollution01 = WorldTileProductivity.GetTilePollution01(to.tileId);
            float dmg = s.GetPollutionExitDamage(pollution01);
            if (dmg <= 0.01f) return 0f;
            return dmg * WdPollutionPathContext.DamageToRoadMultScale * WdPollutionPathContext.Weight;
        }

        private static bool TryResolveTiles(object fromArg, object toArg, out PlanetTile from, out PlanetTile to)
        {
            from = PlanetTile.Invalid;
            to = PlanetTile.Invalid;

            PlanetLayer surface = Find.WorldGrid?.Surface;
            if (surface == null) return false;

            if (!TryToPlanetTile(fromArg, surface, out from) || !from.Valid)
                return false;

            if (toArg is int toInt && toInt < 0)
            {
                to = Find.WorldGrid.FindMostReasonableAdjacentTileForDisplayedPathCost(from);
                return to.Valid;
            }

            return TryToPlanetTile(toArg, surface, out to) && to.Valid;
        }

        private static bool TryToPlanetTile(object arg, PlanetLayer surface, out PlanetTile tile)
        {
            if (arg is PlanetTile pt)
            {
                tile = pt;
                return true;
            }
            if (arg is int id)
            {
                tile = new PlanetTile(id, surface);
                return true;
            }
            tile = PlanetTile.Invalid;
            return false;
        }

        private static bool HasRoadLink(PlanetTile from, PlanetTile to)
        {
            if (!from.Valid || !to.Valid) return false;
            // Match VF: surface tile road links (vehicle caravans are surface-bound).
            SurfaceLayer surface = Find.WorldGrid?.Surface;
            if (surface == null) return false;
            List<SurfaceTile.RoadLink> roads = surface[from.tileId].Roads;
            if (roads == null) return false;
            for (int i = 0; i < roads.Count; i++)
            {
                if (roads[i].neighbor == to && roads[i].road != null)
                    return true;
            }
            return false;
        }

        private static float GetBestVfTerrain(object vehicleList, PlanetTile tile)
        {
            float fallback = WorldPathGrid.CalculatedMovementDifficultyAt(tile, false);
            if (fallback < 0.01f) fallback = 0.01f;

            if (calculatedMovementDifficultyAt == null || vehicleList == null || !tile.Valid)
                return fallback;

            IEnumerable list = vehicleList as IEnumerable;
            if (list == null) return fallback;

            float best = float.MaxValue;
            bool any = false;
            foreach (object item in list)
            {
                object def = ResolveVehicleDef(item);
                if (def == null) continue;
                try
                {
                    object raw = calculatedMovementDifficultyAt.Invoke(
                        null, new object[] { tile, def, null, true });
                    if (raw is float d)
                    {
                        any = true;
                        if (d < best) best = d;
                    }
                }
                catch
                {
                    // Fall through to vanilla terrain.
                }
            }

            if (!any) return fallback;
            if (best < 0.01f) best = 0.01f;
            return best;
        }

        private static object ResolveVehicleDef(object item)
        {
            if (item == null) return null;
            if (vehicleDefType != null && vehicleDefType.IsInstanceOfType(item))
                return item;
            if (vehiclePawnType != null && vehiclePawnType.IsInstanceOfType(item) && vehicleDefOnPawn != null)
                return vehicleDefOnPawn.GetValue(item);
            return null;
        }
    }
}
