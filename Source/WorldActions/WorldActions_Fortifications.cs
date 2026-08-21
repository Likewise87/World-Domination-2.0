using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Shared clear helpers for road blocks, spike traps, and AT turrets.</summary>
    public static class WorldActions_Fortifications
    {
        public static bool HasFortificationAt(int tileId) =>
            WorldComponent_RoadBlocks.Get()?.HasBlockAt(tileId) == true
            || WorldComponent_SpikeTraps.Get()?.HasTrapAt(tileId) == true
            || AtTurretUtility.TileHasAtTurret(tileId);

        public static bool TryClearAt(int tileId)
        {
            if (tileId < 0) return false;
            bool cleared = false;
            if (WorldComponent_RoadBlocks.Get()?.TryClear(tileId) == true)
                cleared = true;
            if (WorldComponent_SpikeTraps.Get()?.TryClear(tileId) == true)
                cleared = true;

            WorldObject_AT_Turret turret = AtTurretUtility.FindTurretAt(tileId);
            if (turret != null && !turret.Destroyed)
            {
                turret.suppressDestroyedLetter = true;
                turret.Destroy();
                cleared = true;
            }

            return cleared;
        }
    }
}
