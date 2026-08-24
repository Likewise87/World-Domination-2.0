using System.Collections.Generic;
using RimWorld;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Tracks world cells touched by KCSG <see cref="KCSG.StructureLayoutDef"/> spawns during WD settlement generation.
    /// </summary>
    public static class WdLayoutSpawnCellTracker
    {
        private static readonly Dictionary<int, HashSet<IntVec3>> CellsByMapId = new Dictionary<int, HashSet<IntVec3>>();
        [System.ThreadStatic] private static bool sessionActive;

        public static bool SessionActive => sessionActive;

        public static void Begin(Map map)
        {
            sessionActive = true;
            if (map == null) return;
            CellsByMapId[map.uniqueID] = new HashSet<IntVec3>();
        }

        public static void End()
        {
            sessionActive = false;
        }

        public static void Clear(Map map)
        {
            if (map == null) return;
            CellsByMapId.Remove(map.uniqueID);
        }

        public static void RecordGenerateRect(Map map, CellRect rect)
        {
            if (!sessionActive || map == null || rect.Area <= 0) return;

            if (!CellsByMapId.TryGetValue(map.uniqueID, out HashSet<IntVec3> cells))
            {
                cells = new HashSet<IntVec3>();
                CellsByMapId[map.uniqueID] = cells;
            }

            foreach (IntVec3 cell in rect)
            {
                if (cell.InBounds(map))
                    cells.Add(cell);
            }
        }

        public static bool TryGetCells(Map map, out HashSet<IntVec3> cells)
        {
            cells = null!;
            if (map == null) return false;
            return CellsByMapId.TryGetValue(map.uniqueID, out cells!) && cells != null && cells.Count > 0;
        }
    }
}
