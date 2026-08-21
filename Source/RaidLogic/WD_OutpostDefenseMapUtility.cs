#nullable disable
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Resolves the KCSG settlement anchor for outpost defense maps (not always map.Center).</summary>
    public static class WD_OutpostDefenseMapUtility
    {
        private static readonly Dictionary<int, IntVec3> RecordedCenters = new Dictionary<int, IntVec3>();

        public static void RecordSettlementCenter(Map map, IntVec3 center)
        {
            if (map == null || !center.InBounds(map))
                return;
            RecordedCenters[map.uniqueID] = center;
        }

        public static void ClearSettlementCenter(Map map)
        {
            if (map == null)
                return;
            RecordedCenters.Remove(map.uniqueID);
        }

        public static IntVec3 GetSettlementCenter(Map map)
        {
            if (map == null)
                return IntVec3.Invalid;

            if (RecordedCenters.TryGetValue(map.uniqueID, out IntVec3 recorded) && recorded.InBounds(map))
                return recorded;

            IntVec3 fromBuildings = ComputeColonistBuildingCentroid(map);
            if (fromBuildings.InBounds(map))
                return fromBuildings;

            return map.Center;
        }

        public static IntVec3 ResolveKcsgSettlementCenter(IntVec3 generateLoc)
        {
            Type utilsType = AccessTools.TypeByName("KCSG.SettlementGenUtils");
            if (utilsType != null)
            {
                FieldInfo rectField = AccessTools.Field(utilsType, "rect");
                if (rectField?.GetValue(null) is CellRect rect && rect.Area > 0)
                    return rect.CenterCell;
            }

            return generateLoc;
        }

        private static IntVec3 ComputeColonistBuildingCentroid(Map map)
        {
            var buildings = map.listerBuildings?.allBuildingsColonist;
            if (buildings == null || buildings.Count == 0)
                return IntVec3.Invalid;

            long sumX = 0;
            long sumZ = 0;
            int count = 0;
            for (int i = 0; i < buildings.Count; i++)
            {
                Building building = buildings[i];
                if (building == null || building.Destroyed)
                    continue;
                IntVec3 pos = building.Position;
                if (!pos.InBounds(map))
                    continue;
                sumX += pos.x;
                sumZ += pos.z;
                count++;
            }

            if (count <= 0)
                return IntVec3.Invalid;

            return new IntVec3((int)(sumX / count), 0, (int)(sumZ / count));
        }
    }
}
