using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    [HarmonyPatch(typeof(Caravan), nameof(Caravan.GetInspectString))]
    public static class Patch_CaravanRemoteEstablishInspect
    {
        public static void Postfix(Caravan __instance, ref string __result)
        {
            string line = CaravanArrivalAction_EstablishWdOutpost.GetInspectLine(__instance);
            if (string.IsNullOrEmpty(line)) return;
            if (string.IsNullOrEmpty(__result))
                __result = line;
            else
                __result = __result + "\n" + line;
        }
    }
}
