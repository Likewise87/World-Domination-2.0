using HarmonyLib;
using RimWorld;
using RimWorld.Planet;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Experimental kill switch: block vanilla storyteller fires targeting traveling caravans
    /// (Ambush, ManhunterAmbush, CaravanMeeting, CaravanDemand, disease). WD clash/ambush systems are unchanged.
    /// </summary>
    [HarmonyPatch(typeof(Storyteller), nameof(Storyteller.TryFire))]
    public static class Patch_DisableVanillaCaravanIncidents
    {
        [HarmonyPrefix]
        public static bool Prefix(FiringIncident fi)
        {
            var s = WorldDominationMod.settings;
            if (s == null || !s.experimentalDisableVanillaCaravanIncidents) return true;
            if (fi?.parms?.target is Caravan) return false;
            return true;
        }
    }
}
