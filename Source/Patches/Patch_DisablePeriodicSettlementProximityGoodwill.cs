using HarmonyLib;
using RimWorld.Planet;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Vanilla runs <see cref="SettlementProximityGoodwillUtility.CheckSettlementProximityGoodwillChange"/> every quadrum
    /// and applies goodwill penalties for player settlements near NPC bases. Optional via mod settings.
    /// </summary>
    [HarmonyPatch(typeof(SettlementProximityGoodwillUtility), nameof(SettlementProximityGoodwillUtility.CheckSettlementProximityGoodwillChange))]
    public static class Patch_DisablePeriodicSettlementProximityGoodwill
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            var s = WorldDominationMod.settings;
            if (s == null || !s.disableSettlementProximityGoodwill)
                return true;
            return false;
        }
    }
}
