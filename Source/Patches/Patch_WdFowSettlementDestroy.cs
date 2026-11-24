using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Clear settlement intel knowledge when a settlement is destroyed mid-game.
    /// Also wipe player-colony Watch Towers (builtBySite = colony).
    /// <see cref="Settlement"/> does not override Destroy — hook <see cref="WorldObject.Destroy"/>.
    /// </summary>
    [HarmonyPatch(typeof(WorldObject), nameof(WorldObject.Destroy))]
    public static class Patch_WdFowSettlementDestroy
    {
        public static void Prefix(WorldObject __instance)
        {
            if (__instance is not Settlement settlement || settlement.Destroyed) return;
            WorldComponent_SettlementIntel.Get()?.NotifySiteDestroyed(settlement);
            if (settlement.Faction?.IsPlayer == true && settlement.HasMap)
                WorldActions_WatchTowers.WipeForBuilder(settlement);
        }
    }
}
