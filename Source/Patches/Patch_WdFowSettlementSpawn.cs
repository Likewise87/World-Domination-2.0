using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Event-based FoW when a settlement appears mid-game (expand / revolt / pack-up / conquest / debug).
    /// Skips load/worldgen via <see cref="WorldComponent_SettlementIntel.NotifySiteAppeared"/> seed gate.
    /// <see cref="Settlement"/> does not override <see cref="WorldObject.SpawnSetup"/> — hook the base
    /// (same pattern as <see cref="Patch_WdFowSettlementDestroy"/>).
    /// </summary>
    [HarmonyPatch(typeof(WorldObject), nameof(WorldObject.SpawnSetup))]
    public static class Patch_WdFowSettlementSpawn
    {
        public static void Postfix(WorldObject __instance)
        {
            if (__instance is not Settlement settlement || settlement.Destroyed) return;
            WorldComponent_SettlementIntel.Get()?.NotifySiteAppeared(settlement);
        }
    }
}
