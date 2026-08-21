using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Soft CAI 5000 compat: leaving a conquered settlement map can NRE inside
    /// AvoidanceTracker / SightGrid teardown (IBuckets.Clear on a null bucket store).
    /// Swallow only that NullReferenceException so MapDeiniter can finish cleanly
    /// and CAI's own "failed to stop thread" Log.Error does not fire.
    /// No hard dependency — patches are skipped when CAI is not loaded.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class Patch_CombatAI_MapRemovedSafe
    {
        private const string PackageId = "Krkr.rule56";
        private static bool appliedOnce;
        private static int applyAttempts;

        static Patch_CombatAI_MapRemovedSafe()
        {
            // CAI assembly is normally already loaded; defer one frame so forks that
            // register types late still get hooked, and retry if the first pass finds nothing.
            TryApplyPatches();
            LongEventHandler.ExecuteWhenFinished(TryApplyPatches);
        }

        private static void TryApplyPatches()
        {
            if (appliedOnce)
                return;
            if (!IsCombatAiPresent())
                return;

            applyAttempts++;
            try
            {
                var harmony = new Harmony("TSA.WorldDomination.CombatAI.MapRemovedSafe");
                int applied = 0;

                applied += TryFinalizer(harmony, "CombatAI.AvoidanceTracker", "MapRemoved");
                applied += TryFinalizer(harmony, "CombatAI.SightGrid", "Destroy");
                applied += TryFinalizer(harmony, "CombatAI.MapComponent_CombatAI", "MapRemoved");
                applied += TryFinalizer(harmony, "CombatAI.SightGridManager", "Notify_MapRemoved");

                // Root NRE site from the stack: IBuckets.Clear / Release during SightGrid.Destroy.
                applied += TryFinalizerOnOpenGeneric(harmony, "CombatAI.IBuckets`1", "Clear");
                applied += TryFinalizerOnOpenGeneric(harmony, "CombatAI.IBuckets`1", "Release");
                applied += TryFinalizerOnClosedIBucketsFromSightGrid(harmony);
                applied += TryFinalizerOnClosedIBucketsFromType(harmony, "CombatAI.AvoidanceTracker");

                if (applied > 0)
                {
                    appliedOnce = true;
                    Log.Message($"[TSA WD] CAI map-teardown NRE guard active ({applied} hooks).");
                }
                else if (applyAttempts >= 2)
                {
                    appliedOnce = true;
                    Log.Warning("[TSA WD] CAI loaded but map-teardown hooks not found; leave-after-conquest NRE guard inactive.");
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[TSA WD] CAI map-teardown NRE guard disabled: {ex.Message}");
                if (applyAttempts >= 2)
                    appliedOnce = true;
            }
        }

        private static bool IsCombatAiPresent()
        {
            if (ModsConfig.IsActive(PackageId) || ModsConfig.IsActive(PackageId + "_steam"))
                return true;

            return AccessTools.TypeByName("CombatAI.AvoidanceTracker") != null
                || AccessTools.TypeByName("CombatAI.SightGrid") != null
                || AccessTools.TypeByName("CombatAI.IBuckets`1") != null;
        }

        private static MethodInfo FindMethod(Type type, string methodName)
        {
            if (type == null || methodName.NullOrEmpty())
                return null;

            MethodInfo method = AccessTools.Method(type, methodName);
            if (method != null)
                return method;

            foreach (MethodInfo m in AccessTools.GetDeclaredMethods(type))
            {
                if (m != null && m.Name == methodName)
                    return m;
            }
            return null;
        }

        private static int TryFinalizer(Harmony harmony, string typeName, string methodName)
        {
            return TryPatchFinalizer(harmony, AccessTools.TypeByName(typeName), methodName, typeName);
        }

        private static int TryFinalizerOnOpenGeneric(Harmony harmony, string typeName, string methodName)
        {
            Type type = AccessTools.TypeByName(typeName);
            if (type == null)
                return 0;
            return TryPatchFinalizer(harmony, type, methodName, typeName);
        }

        /// <summary>
        /// Open-generic Harmony patches are unreliable; also hook closed IBuckets types
        /// declared as fields on SightGrid (the Destroy path that NREs).
        /// </summary>
        private static int TryFinalizerOnClosedIBucketsFromSightGrid(Harmony harmony)
        {
            return TryFinalizerOnClosedIBucketsFromType(harmony, "CombatAI.SightGrid");
        }

        private static int TryFinalizerOnClosedIBucketsFromType(Harmony harmony, string typeName)
        {
            Type owner = AccessTools.TypeByName(typeName);
            if (owner == null)
                return 0;

            int applied = 0;
            foreach (FieldInfo field in AccessTools.GetDeclaredFields(owner))
            {
                Type ft = field?.FieldType;
                if (!IsCombatAiIBucketsType(ft))
                    continue;

                applied += TryPatchFinalizer(harmony, ft, "Clear", ft.FullName);
                applied += TryPatchFinalizer(harmony, ft, "Release", ft.FullName);
            }
            return applied;
        }

        private static bool IsCombatAiIBucketsType(Type type)
        {
            if (type == null || !type.IsGenericType)
                return false;
            if (type.Name == "IBuckets`1" && type.Namespace == "CombatAI")
                return true;
            try
            {
                Type open = type.GetGenericTypeDefinition();
                return open != null && open.FullName == "CombatAI.IBuckets`1";
            }
            catch
            {
                return false;
            }
        }

        private static int TryPatchFinalizer(Harmony harmony, Type type, string methodName, string typeLabel)
        {
            MethodInfo method = FindMethod(type, methodName);
            if (method == null)
                return 0;

            try
            {
                // Prefer ref-Exception finalizer: return-Exception form does not always clear
                // the throw for CAI's Notify_MapRemoved try/catch on current Harmony/RW builds.
                harmony.Patch(
                    method,
                    finalizer: new HarmonyMethod(typeof(Patch_CombatAI_MapRemovedSafe), nameof(SwallowTeardownNre)));
                return 1;
            }
            catch (Exception ex)
            {
                Log.Warning($"[TSA WD] Failed to guard {typeLabel}.{methodName}: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Clears teardown NREs so callers (including CAI's own try/catch log) see a clean return.
        /// </summary>
        public static void SwallowTeardownNre(ref Exception __exception)
        {
            if (__exception is NullReferenceException)
                __exception = null;
        }
    }
}
