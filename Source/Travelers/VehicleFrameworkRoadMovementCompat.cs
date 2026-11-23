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
    /// Soft Vehicle Framework compat: (1) inject WD glitter into VehicleDef <c>customRoadCosts</c>
    /// (runs even when Roads of the Rim is active); (2) on-road hop Harmony re-bake for tiers /
    /// winter / blocks (skipped when RotR is active — RotR owns those tiers).
    /// No hard reference to Vehicles.dll.
    /// </summary>
    /// <remarks>
    /// Released VF 1.6 exposes <c>RoadCostHelper.GetRoadMovementDifficultyMultiplier</c> (float).
    /// Newer/develop builds may use <c>GetRoadMovementMultiplier</c> returning a nested
    /// <c>RoadMultiplier</c> struct; both shapes are discovered and patched when present.
    /// VVE <c>AssignDefaults</c> stamps every RoadDef into <c>customRoadCosts</c> at a flat cost,
    /// which erases WD glitter's better RoadDef mult — overwritten here.
    /// </remarks>
    [StaticConstructorOnStartup]
    public static class VehicleFrameworkRoadMovementCompat
    {
        public const string PackageId = "SmashPhil.VehicleFramework";
        public const string GlitterRoadDefName = "TSA_WD_GlitterRoad";

        private static bool active;
        private static bool glitterInjectReady;
        private static Type vehicleDefType;
        private static Type vehiclePawnType;
        private static PropertyInfo vehicleDefOnPawn;
        private static FieldInfo vehiclePropertiesField;
        private static FieldInfo customRoadCostsField;
        private static MethodInfo calculatedMovementDifficultyAt;
        private static Type roadMultiplierType;
        private static FieldInfo roadMultiplierRoadDefField;
        private static FieldInfo roadMultiplierMultiplierField;
        private static ConstructorInfo roadMultiplierCtor;

        static VehicleFrameworkRoadMovementCompat()
        {
            if (!IsVehicleFrameworkActive())
                return;

            try
            {
                if (!TryResolveVehicleDefReflection())
                {
                    Log.Warning("[TSA WD] Vehicle Framework: VehicleDef reflection incomplete; glitter inject + hop shim disabled.");
                    return;
                }

                glitterInjectReady = true;

                // Hop Harmony: WD road mult ownership only without RotR (same gate as RoadDef writes).
                if (WorldActions_Roads.RoadsOfTheRimActive)
                {
                    Log.Message("[TSA WD] Vehicle Framework road hop shim skipped (Roads of the Rim active); glitter customRoadCosts inject still available.");
                    return;
                }

                Type helperType = AccessTools.TypeByName("Vehicles.RoadCostHelper");
                if (helperType == null)
                {
                    Log.Warning("[TSA WD] Vehicle Framework road movement: RoadCostHelper not found; hop shim disabled.");
                    return;
                }

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
                    Log.Warning("[TSA WD] Vehicle Framework road movement: no compatible GetRoad* methods; hop shim disabled.");
                    return;
                }

                active = true;
                Log.Message($"[TSA WD] Vehicle Framework road movement hop shim active ({patched} patch(es)).");
            }
            catch (Exception ex)
            {
                Log.Warning($"[TSA WD] Vehicle Framework road movement compat disabled: {ex.Message}");
            }
        }

        public static bool Active => active;

        private static bool TryResolveVehicleDefReflection()
        {
            vehicleDefType = AccessTools.TypeByName("Vehicles.VehicleDef");
            if (vehicleDefType == null) return false;

            vehiclePawnType = AccessTools.TypeByName("Vehicles.VehiclePawn");
            if (vehiclePawnType != null)
                vehicleDefOnPawn = AccessTools.Property(vehiclePawnType, "VehicleDef");

            // VehicleDef.properties → VehicleProperties.customRoadCosts (Dictionary<RoadDef, float>).
            vehiclePropertiesField = AccessTools.Field(vehicleDefType, "properties")
                ?? AccessTools.DeclaredField(vehicleDefType, "properties");
            if (vehiclePropertiesField == null) return false;

            Type propsType = vehiclePropertiesField.FieldType;
            customRoadCostsField = AccessTools.Field(propsType, "customRoadCosts")
                ?? AccessTools.DeclaredField(propsType, "customRoadCosts");
            return customRoadCostsField != null;
        }

        /// <summary>
        /// Overwrite every VehicleDef's <c>customRoadCosts[TSA_WD_GlitterRoad]</c> with WD glitter movement.
        /// Call from <see cref="WorldActions_Roads.ApplyVanillaRoadMovementSettings"/> (and settings apply).
        /// Runs with or without RotR / hop shim.
        /// </summary>
        public static void ApplyGlitterCustomRoadCosts()
        {
            if (!IsVehicleFrameworkActive()) return;
            if (!glitterInjectReady && !TryResolveVehicleDefReflection())
            {
                Log.WarningOnce("[TSA WD] VF glitter customRoadCosts: VehicleDef reflection unavailable.",
                    "TSA_WD_VF_GlitterInject_Reflect".GetHashCode());
                return;
            }
            glitterInjectReady = true;

            WorldDominationSettings s = WorldDominationMod.settings;
            if (s == null) return;

            RoadDef glitter = DefDatabase<RoadDef>.GetNamedSilentFail(GlitterRoadDefName);
            if (glitter == null) return;

            float mult = s.GetFallbackRoadMovement(SettlementTier.T4);
            try
            {
                Type defDb = typeof(DefDatabase<>).MakeGenericType(vehicleDefType);
                PropertyInfo allProp = AccessTools.Property(defDb, "AllDefsListForReading");
                if (allProp == null) return;
                object listObj = allProp.GetValue(null);
                if (listObj is not IList defs || defs.Count == 0) return;

                int written = 0;
                for (int i = 0; i < defs.Count; i++)
                {
                    object def = defs[i];
                    if (def == null) continue;
                    object props = vehiclePropertiesField.GetValue(def);
                    if (props == null) continue;
                    object dictObj = customRoadCostsField.GetValue(props);
                    if (dictObj == null)
                    {
                        // Allocate empty dict if missing (rare).
                        dictObj = Activator.CreateInstance(customRoadCostsField.FieldType);
                        customRoadCostsField.SetValue(props, dictObj);
                    }
                    if (dictObj is not IDictionary dict) continue;
                    dict[glitter] = mult;
                    written++;
                }

                if (written > 0)
                    Log.Message($"[TSA WD] VF glitter customRoadCosts set to {mult:0.###} on {written} VehicleDef(s).");
            }
            catch (Exception ex)
            {
                Log.WarningOnce($"[TSA WD] VF glitter customRoadCosts inject failed: {ex.Message}",
                    "TSA_WD_VF_GlitterInject_Fail".GetHashCode());
            }
        }

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
                // Match float postfix: tile road link, not only whether VF packed a RoadDef.
                float adjusted = AdjustMultiplier(mult, HasRoadLink(from, to), __0, from, to);
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
