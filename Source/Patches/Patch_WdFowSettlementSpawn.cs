using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Event-based FoW when a settlement appears mid-game (expand / revolt / pack-up / conquest / debug).
    /// Skips load/worldgen via <see cref="WorldComponent_SettlementIntel.NotifySiteAppeared"/> seed gate.
    /// </summary>
    [HarmonyPatch(typeof(Settlement), nameof(Settlement.SpawnSetup))]
    public static class Patch_WdFowSettlementSpawn
    {
        public static void Postfix(Settlement __instance)
        {
            if (__instance == null || __instance.Destroyed) return;
            WorldComponent_SettlementIntel.Get()?.NotifySiteAppeared(__instance);
        }
    }
}
