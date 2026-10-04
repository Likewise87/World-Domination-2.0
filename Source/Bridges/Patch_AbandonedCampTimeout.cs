using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Vanilla AbandonedCamp timeout is 30 days (<c>1800000</c> ticks in <see cref="Camp.Notify_MyMapRemoved"/>).
    /// WD shortens that to 5 days, or 10 hours on a bridged-water tile so bridge camps clear quickly.
    /// </summary>
    [HarmonyPatch(typeof(TimeoutComp), nameof(TimeoutComp.StartTimeout))]
    public static class Patch_TimeoutComp_StartTimeout_AbandonedCamp
    {
        public const int AbandonedCampDays = 5;
        public const int AbandonedCampBridgeHours = 10;

        public static void Prefix(TimeoutComp __instance, ref int ticks)
        {
            WorldObject parent = __instance?.parent;
            if (parent == null || parent.Destroyed) return;
            if (WorldObjectDefOf.AbandonedCamp == null || parent.def != WorldObjectDefOf.AbandonedCamp)
                return;

            ticks = WorldComponent_WdBridges.IsBridgedWaterTile(parent.Tile)
                ? AbandonedCampBridgeHours * GenDate.TicksPerHour
                : AbandonedCampDays * GenDate.TicksPerDay;
        }
    }
}
