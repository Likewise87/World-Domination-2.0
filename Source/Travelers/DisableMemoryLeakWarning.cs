using HarmonyLib;
using Verse;
using System;
using System.Collections.Generic;
using RimWorld.Planet;

namespace TSA_WorldDomination
{
    [StaticConstructorOnStartup]
    public static class HarmonyLoader
    {
        static HarmonyLoader()
        {
            var harmony = new Harmony("TSA.WorldDomination.MovementFix");
            var assembly = typeof(HarmonyLoader).Assembly;
            foreach (var type in assembly.GetTypes())
            {
                if (!type.IsClass || !type.IsSealed || !type.IsAbstract) continue;
                var attrs = type.GetCustomAttributes(false);
                bool hasPatch = false;
                for (int i = 0; i < attrs.Length; i++)
                {
                    string fullName = attrs[i].GetType().FullName;
                    if (fullName != null && fullName.StartsWith("HarmonyLib.") && attrs[i].GetType().Name.Contains("HarmonyPatch"))
                    { hasPatch = true; break; }
                }
                if (!hasPatch) continue;
                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                }
                catch (Exception ex)
                {
                    Log.Warning($"[TSA World Domination] Harmony patch skipped ({type.Name}): {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Fully replaces vanilla GetEmptyWorldPath so the built-in "%.%.% leak"
    /// warning never fires.  Logic is identical (find free slot or grow the pool)
    /// minus the caravan-count leak check.  A periodic cleanup reclaims orphaned
    /// paths as a safety net.
    /// </summary>
    [HarmonyPatch(typeof(WorldPathPool), "GetEmptyWorldPath")]
    public static class Patch_WorldPathPool_Leak
    {
        private static readonly AccessTools.FieldRef<WorldPathPool, List<WorldPath>> PathsField =
            AccessTools.FieldRefAccess<WorldPathPool, List<WorldPath>>("paths");

        private static int lastCleanupTick = -999;
        private const int CleanupIntervalTicks = 30000;
        private static readonly HashSet<WorldPath> reusableActivePaths = new HashSet<WorldPath>();

        [HarmonyPrefix]
        public static bool Prefix(WorldPathPool __instance, ref WorldPath __result)
        {
            List<WorldPath> paths = PathsField(__instance);

            if (Current.ProgramState == ProgramState.Playing && Find.WorldObjects != null)
            {
                int tick = Find.TickManager?.TicksGame ?? 0;
                if (paths.Count > 200 && tick - lastCleanupTick >= CleanupIntervalTicks)
                {
                    lastCleanupTick = tick;
                    reusableActivePaths.Clear();
                    var all = Find.WorldObjects.AllWorldObjects;
                    for (int i = 0; i < all.Count; i++)
                    {
                        if (all[i] is WorldObject_Traveler t && t.pather?.curPath != null)
                            reusableActivePaths.Add(t.pather.curPath);
                    }
                    var caravans = Find.WorldObjects.Caravans;
                    if (caravans != null)
                    {
                        for (int i = 0; i < caravans.Count; i++)
                        {
                            WorldPath caravanPath = caravans[i]?.pather?.curPath;
                            if (caravanPath != null)
                                reusableActivePaths.Add(caravanPath);
                        }
                    }
                    for (int i = 0; i < paths.Count; i++)
                    {
                        if (paths[i].inUse && !reusableActivePaths.Contains(paths[i]))
                            paths[i].inUse = false;
                    }
                    reusableActivePaths.Clear();
                }
            }

            // Find a free path in the pool.
            for (int i = 0; i < paths.Count; i++)
            {
                if (!paths[i].inUse)
                {
                    paths[i].inUse = true;
                    __result = paths[i];
                    return false;
                }
            }

            // Pool exhausted — grow it (no leak warning).
            WorldPath newPath = new WorldPath();
            paths.Add(newPath);
            newPath.inUse = true;
            __result = newPath;
            return false;
        }
    }
}
