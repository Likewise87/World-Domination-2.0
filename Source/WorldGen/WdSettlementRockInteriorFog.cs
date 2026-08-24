using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// After settlement rect unfog, re-fogs natural rock and ore mineables fully enclosed on all eight neighbors
    /// within the same settlement rect — pre-existing mountain or layout-spawned rock alike. Chunks do not count.
    /// </summary>
    public static class WdSettlementRockInteriorFog
    {
        private static readonly IntVec3[] NeighborOffsets =
        {
            new IntVec3(-1, 0, -1), new IntVec3(0, 0, -1), new IntVec3(1, 0, -1),
            new IntVec3(-1, 0, 0),                         new IntVec3(1, 0, 0),
            new IntVec3(-1, 0, 1),  new IntVec3(0, 0, 1),  new IntVec3(1, 0, 1)
        };

        public static void Apply(Map map, CellRect settlementRect)
        {
            if (map?.fogGrid == null) return;
            if (settlementRect.Area <= 0) return;
            if (!ShouldApply(map)) return;

            var settings = WorldDominationMod.settings;
            if (settings != null && !settings.kcsgFogInteriorMineables) return;

            CellRect rect = settlementRect.ClipInsideMap(map);
            if (rect.Area <= 0) return;

            int fogged = 0;
            foreach (IntVec3 cell in rect)
            {
                if (!cell.InBounds(map)) continue;
                if (!IsInteriorFogMineableCell(map, cell)) continue;
                if (!AllEightNeighborsAreRockOrMountain(map, cell)) continue;
                if (map.fogGrid.IsFogged(cell)) continue;

                map.fogGrid.Refog(CellRect.SingleCell(cell));
                fogged++;
            }

            if (fogged > 0)
                WDVerbose.Msg($"Interior rock fog for {map.Parent?.LabelCap}: {fogged} cells in rect {rect}.");
        }

        private static bool ShouldApply(Map map)
        {
            var s = WorldDominationMod.settings;
            if (s != null && !s.kcsgUnfogSettlementRect) return false;
            if (IsOutpostDefenseSite(map.Parent)) return true;
            return WdSettlementMapPower.ShouldForcePower(map);
        }

        private static bool IsOutpostDefenseSite(MapParent parent) =>
            parent?.def?.defName == "TSA_WD_OutpostDefenseSite";

        private static bool AllEightNeighborsAreRockOrMountain(Map map, IntVec3 cell)
        {
            for (int i = 0; i < NeighborOffsets.Length; i++)
            {
                IntVec3 neighbor = cell + NeighborOffsets[i];
                if (!IsInteriorFogMineableCell(map, neighbor))
                    return false;
            }

            return true;
        }

        /// <summary>Natural rock wall mineables and ores count; chunks do not.</summary>
        public static bool IsInteriorFogMineableCell(Map map, IntVec3 cell)
        {
            if (map == null || !cell.InBounds(map)) return false;

            Mineable mineable = cell.GetFirstMineable(map);
            if (mineable?.def == null) return false;
            if (mineable.def.defName.StartsWith("Chunk")) return false;

            BuildingProperties building = mineable.def.building;
            if (building == null) return false;
            if (building.isNaturalRock) return true;
            return building.mineableThing != null;
        }
    }
}
