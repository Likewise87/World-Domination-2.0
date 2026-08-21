using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Post-map-gen pass: reveal the full KCSG settlement rect on WD attack/defense maps.
    /// Building-footprint unfog misses interior floor cells; rect unfog exposes the whole base.
    /// </summary>
    public static class WdSettlementMapUnfog
    {
        private const int OutpostDefenseLayoutSize = 40;

        private static readonly Dictionary<int, CellRect> PendingRectsByMap = new Dictionary<int, CellRect>();

        public static void RecordPendingRect(Map map, CellRect rect)
        {
            if (map == null || rect.Area <= 0) return;
            PendingRectsByMap[map.uniqueID] = rect;
        }

        public static void ClearPendingRect(Map map)
        {
            if (map == null) return;
            PendingRectsByMap.Remove(map.uniqueID);
        }

        public static void UnfogKcsgSettlement(Map map)
        {
            if (map?.fogGrid == null) return;
            if (!ShouldApply(map)) return;

            if (!TryResolveSettlementRect(map, out CellRect rect))
            {
                UnfogArtificialBuildingsFallback(map);
                ClearPendingRect(map);
                return;
            }

            int cellCount = 0;
            foreach (IntVec3 cell in rect)
            {
                if (!cell.InBounds(map)) continue;
                map.fogGrid.Unfog(cell);
                cellCount++;
            }

            ClearPendingRect(map);

            if (Prefs.DevMode && cellCount > 0)
            {
                Log.Message($"[WorldDomination] KCSG unfog for {map.Parent?.LabelCap}: rect={rect}, {cellCount} cells.");
            }
        }

        /// <summary>Legacy entry point used by existing call sites during migration.</summary>
        public static void UnfogKcsgFactionBuildings(Map map) => UnfogKcsgSettlement(map);

        private static bool TryResolveSettlementRect(Map map, out CellRect rect)
        {
            rect = default;

            if (TryGetKcsgSettlementRect(out rect) && rect.Area > 0)
                return true;

            if (PendingRectsByMap.TryGetValue(map.uniqueID, out CellRect pending) && pending.Area > 0)
            {
                rect = pending.ClipInsideMap(map);
                if (rect.Area > 0) return true;
            }

            if (IsOutpostDefenseSite(map.Parent))
            {
                IntVec3 center = WD_OutpostDefenseMapUtility.GetSettlementCenter(map);
                if (center.InBounds(map))
                {
                    rect = CellRect.CenteredOn(center, OutpostDefenseLayoutSize, OutpostDefenseLayoutSize).ClipInsideMap(map);
                    if (rect.Area > 0) return true;
                }
            }

            return false;
        }

        private static bool TryGetKcsgSettlementRect(out CellRect rect)
        {
            rect = default;
            Type utilsType = AccessTools.TypeByName("KCSG.SettlementGenUtils");
            if (utilsType == null) return false;

            FieldInfo rectField = AccessTools.Field(utilsType, "rect");
            if (rectField?.GetValue(null) is CellRect kcsgRect && kcsgRect.Area > 0)
            {
                rect = kcsgRect;
                return true;
            }

            return false;
        }

        private static void UnfogArtificialBuildingsFallback(Map map)
        {
            var buildings = map.listerThings?.ThingsInGroup(ThingRequestGroup.BuildingArtificial);
            if (buildings == null) return;

            int cellCount = 0;
            for (int i = 0; i < buildings.Count; i++)
            {
                if (buildings[i] is not Building building || building.Destroyed) continue;

                foreach (IntVec3 cell in building.OccupiedRect())
                {
                    if (!cell.InBounds(map)) continue;
                    map.fogGrid.Unfog(cell);
                    cellCount++;
                }
            }

            if (Prefs.DevMode && cellCount > 0)
            {
                Log.Message($"[WorldDomination] KCSG unfog fallback for {map.Parent?.LabelCap}: {cellCount} building cells.");
            }
        }

        private static bool ShouldApply(Map map)
        {
            if (IsOutpostDefenseSite(map.Parent)) return true;
            return WdSettlementMapPower.ShouldForcePower(map);
        }

        private static bool IsOutpostDefenseSite(MapParent parent) =>
            parent?.def?.defName == "TSA_WD_OutpostDefenseSite";
    }
}
