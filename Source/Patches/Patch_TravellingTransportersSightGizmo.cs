using System.Collections.Generic;
using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Sight gizmo on vanilla transport pods / Odyssey shuttle flights.
    /// <see cref="TravellingTransporters"/> does not override <see cref="WorldObject.GetGizmos"/> —
    /// patch the base and filter (Settlement/Caravan have their own overrides + WD postfixes).
    /// </summary>
    [HarmonyPatch(typeof(WorldObject), nameof(WorldObject.GetGizmos))]
    public static class Patch_TravellingTransportersSightGizmo
    {
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, WorldObject __instance)
        {
            if (__result != null)
            {
                foreach (Gizmo g in __result)
                    yield return g;
            }

            if (__instance is not TravellingTransporters pods || pods.Destroyed)
                yield break;

            foreach (Gizmo g in RadiusHoverGizmos.GetSightForMobile(pods))
                yield return g;
        }
    }
}
