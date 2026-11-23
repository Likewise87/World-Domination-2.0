using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Shared clear helpers for road blocks, spike traps, AT turrets, advanced buildings, and player Watch Towers.</summary>
    public static class WorldActions_Fortifications
    {
        public static bool HasFortificationAt(int tileId) =>
            WorldComponent_RoadBlocks.Get()?.HasBlockAt(tileId) == true
            || WorldComponent_SpikeTraps.Get()?.HasTrapAt(tileId) == true
            || AtTurretUtility.TileHasAtTurret(tileId)
            || WorldComponent_AdvancedBuildings.Get()?.HasAt(tileId) == true
            || WorldObject_WD_PlayerWatchTower.FindAt(tileId) != null;

        /// <summary>
        /// AT turret, advanced building, or player Watch Tower on the tile. Road blocks / traps must refuse these tiles
        /// (same mutual exclusion AT eligibility already applies the other way).
        /// </summary>
        public static bool HasWorldObjectAssetAt(int tileId) =>
            AtTurretUtility.TileHasAtTurret(tileId)
            || WorldComponent_AdvancedBuildings.Get()?.HasAt(tileId) == true
            || WorldObject_WD_PlayerWatchTower.FindAt(tileId) != null;

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

            WorldObject_WD_AdvancedBuilding building = WorldComponent_AdvancedBuildings.Get()?.At(tileId);
            if (building != null && !building.Destroyed)
            {
                building.suppressDestroyedLetter = true;
                building.Destroy();
                cleared = true;
            }

            // Quiet Destroy — do not call DestroyTower (combat BuiltFortification log).
            WorldObject_WD_PlayerWatchTower tower = WorldObject_WD_PlayerWatchTower.FindAt(tileId);
            if (tower != null && !tower.Destroyed)
            {
                tower.Destroy();
                cleared = true;
            }

            return cleared;
        }
    }
}
