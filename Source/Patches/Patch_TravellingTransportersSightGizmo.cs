using System.Collections.Generic;
using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Sight gizmo on vanilla transport pods / Odyssey shuttle flights.</summary>
    [HarmonyPatch(typeof(TravellingTransporters), nameof(TravellingTransporters.GetGizmos))]
    public static class Patch_TravellingTransportersSightGizmo
    {
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, TravellingTransporters __instance)
        {
            if (__result != null)
            {
                foreach (Gizmo g in __result)
                    yield return g;
            }

            if (__instance == null || __instance.Destroyed)
                yield break;

            foreach (Gizmo g in RadiusHoverGizmos.GetSightForMobile(__instance))
                yield return g;
        }
    }
}
