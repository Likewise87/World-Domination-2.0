using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Shared colony-map + warehouse stock check/deduct for outpost upgrades and player world builds.
    /// </summary>
    public static class ColonyWorldBuildMaterials
    {
        private static readonly List<OutpostUpgradeCostEntry> EmptyCosts = new List<OutpostUpgradeCostEntry>();
        private static int warehouseCacheTick = -1;
        private static List<WorldObject_WD_Outpost> warehouseCache;

        public static List<OutpostUpgradeCostEntry> EmptyCostList => EmptyCosts;

        public static Map GetColonyMap() => Find.AnyPlayerHomeMap;

        /// <summary>All player warehouse outposts (cached per tick).</summary>
        public static List<WorldObject_WD_Outpost> GetContributingWarehouses()
        {
            int t = Find.TickManager?.TicksGame ?? 0;
            if (warehouseCache != null && t == warehouseCacheTick)
                return warehouseCache;

            var result = new List<WorldObject_WD_Outpost>();
            IReadOnlyList<WorldObject_WD_Outpost> outposts = WdPlayerOutpostCache.PlayerOutposts;
            for (int i = 0; i < outposts.Count; i++)
            {
                WorldObject_WD_Outpost wo = outposts[i];
                if (wo == null || wo.Destroyed || !Outpost_Warehouse_Delivery.IsWarehouseOutpost(wo)) continue;
                if (CompOutpostWarehouse.Get(wo) == null) continue;
                result.Add(wo);
            }
            warehouseCache = result;
            warehouseCacheTick = t;
            return result;
        }

        public static bool IsAnyStoneBlocksCost(OutpostUpgradeCostEntry c)
        {
            if (c == null) return false;
            if (c.costMode == OutpostUpgradeCostMode.AnyStoneBlocks) return true;
            return c.thingDef != null && c.thingDef.defName != null && c.thingDef.defName.StartsWith("Blocks");
        }

        public static string GetCostDisplayLabel(OutpostUpgradeCostEntry c)
        {
            if (c == null) return "";
            if (IsAnyStoneBlocksCost(c))
                return "TSA_WD_OutpostUpgrades_AnyStoneBlocks".Translate().ToString();
            return c.thingDef?.LabelCap ?? c.thingDef?.defName ?? "";
        }

        /// <summary>Icon ThingDef for float-menu / UI (AnyStoneBlocks uses a representative blocks def).</summary>
        public static ThingDef GetCostIconThingDef(OutpostUpgradeCostEntry c)
        {
            if (c == null) return null;
            if (IsAnyStoneBlocksCost(c))
            {
                return DefDatabase<ThingDef>.GetNamedSilentFail("BlocksGranite")
                    ?? DefDatabase<ThingDef>.GetNamedSilentFail("BlocksSandstone");
            }
            return c.thingDef;
        }

        /// <summary>Colony map + warehouse stock for one cost line.</summary>
        public static int CountHaveForCost(OutpostUpgradeCostEntry c)
        {
            if (c == null || c.count <= 0) return 0;
            Map map = GetColonyMap();
            List<WorldObject_WD_Outpost> warehouses = GetContributingWarehouses();
            if (IsAnyStoneBlocksCost(c))
                return CountAnyStoneBlocks(map) + CountWarehouseStoneBlocks(warehouses);
            if (c.thingDef == null) return 0;
            return CountAvailable(map, c.thingDef) + CountWarehouseStored(warehouses, c.thingDef);
        }

        public static bool HasMaterialCosts(List<OutpostUpgradeCostEntry> cost, out string reason)
        {
            Map colonyMap = GetColonyMap();
            var warehouses = GetContributingWarehouses();
            if ((cost == null || cost.Count == 0))
            {
                reason = null;
                return true;
            }
            if (colonyMap == null && (warehouses == null || warehouses.Count == 0))
            {
                reason = "TSA_WD_OutpostUpgrades_NoColonyMap".Translate();
                return false;
            }
            return HasCost(colonyMap, warehouses, cost, out reason);
        }

        public static bool HasMaterialCosts(List<OutpostUpgradeCostEntry> cost) =>
            HasMaterialCosts(cost, out _);

        public static bool TryDeductMaterialCosts(List<OutpostUpgradeCostEntry> cost, out string reason)
        {
            Map colonyMap = GetColonyMap();
            var warehouses = GetContributingWarehouses();
            if (cost == null || cost.Count == 0)
            {
                reason = null;
                return true;
            }
            if (colonyMap == null && (warehouses == null || warehouses.Count == 0))
            {
                reason = "TSA_WD_OutpostUpgrades_NoColonyMap".Translate();
                return false;
            }
            return DeductCost(colonyMap, warehouses, cost, out reason, out _);
        }

        /// <summary>Clone cost lines for traveler abort-refund bookkeeping.</summary>
        public static List<OutpostUpgradeCostEntry> CloneCosts(List<OutpostUpgradeCostEntry> cost)
        {
            if (cost == null || cost.Count == 0) return null;
            var clone = new List<OutpostUpgradeCostEntry>(cost.Count);
            for (int i = 0; i < cost.Count; i++)
            {
                OutpostUpgradeCostEntry e = cost[i];
                if (e == null || e.count <= 0) continue;
                if (e.thingDef == null && !IsAnyStoneBlocksCost(e)) continue;
                clone.Add(new OutpostUpgradeCostEntry
                {
                    thingDef = e.thingDef,
                    count = e.count,
                    costMode = e.costMode
                });
            }
            return clone.Count > 0 ? clone : null;
        }

        /// <summary>Return materials to colony map (preferred) or first warehouse. Best-effort.</summary>
        public static void TryRefundMaterialCosts(List<OutpostUpgradeCostEntry> cost)
        {
            if (cost == null || cost.Count == 0) return;
            Map map = GetColonyMap();
            List<WorldObject_WD_Outpost> warehouses = GetContributingWarehouses();
            for (int i = 0; i < cost.Count; i++)
            {
                OutpostUpgradeCostEntry c = cost[i];
                if (c == null || c.count <= 0) continue;
                bool isStone = IsAnyStoneBlocksCost(c);
                ThingDef def = isStone
                    ? (GetCostIconThingDef(c) ?? DefDatabase<ThingDef>.GetNamedSilentFail("BlocksGranite"))
                    : c.thingDef;
                if (def == null) continue;
                int remaining = c.count;
                if (map != null)
                    remaining -= PlaceRefundOnMap(map, def, remaining);
                if (remaining > 0 && warehouses != null && warehouses.Count > 0)
                {
                    var deposit = new List<ThingDefCountClass>
                    {
                        new ThingDefCountClass(def, remaining)
                    };
                    CompOutpostWarehouse.Get(warehouses[0])?.TryDeposit(deposit);
                }
            }
        }

        private static int PlaceRefundOnMap(Map map, ThingDef def, int amount)
        {
            if (map == null || def == null || amount <= 0) return 0;
            IntVec3 cell = WorldActions_Traveler.FindColonyDeliveryOrTradeDropCell(map);
            if (!cell.IsValid) cell = map.Center;
            int placed = 0;
            int left = amount;
            while (left > 0)
            {
                int stack = Mathf.Min(left, def.stackLimit > 0 ? def.stackLimit : left);
                Thing t = ThingMaker.MakeThing(def);
                t.stackCount = stack;
                if (!GenPlace.TryPlaceThing(t, cell, map, ThingPlaceMode.Near))
                {
                    if (!t.Destroyed) t.Destroy(DestroyMode.Vanish);
                    break;
                }
                placed += stack;
                left -= stack;
            }
            return placed;
        }

        public static bool HasCost(
            Map map,
            List<WorldObject_WD_Outpost> warehouses,
            List<OutpostUpgradeCostEntry> cost,
            out string reason,
            Dictionary<string, bool> availabilityByDefName = null)
        {
            reason = null;
            if (cost == null || cost.Count == 0) return true;
            foreach (var c in cost)
            {
                if (c == null || c.count <= 0) continue;
                if (c.thingDef == null && !IsAnyStoneBlocksCost(c)) continue;
                bool isStone = IsAnyStoneBlocksCost(c);
                int have = isStone
                    ? CountAnyStoneBlocks(map) + CountWarehouseStoneBlocks(warehouses)
                    : CountAvailable(map, c.thingDef) + CountWarehouseStored(warehouses, c.thingDef);
                if (availabilityByDefName != null && c.thingDef != null)
                    availabilityByDefName[c.thingDef.defName] = have >= c.count;
                if (have < c.count)
                {
                    reason = "TSA_WD_OutpostUpgrades_NeedHave".Translate(
                        c.count.ToString(), GetCostDisplayLabel(c), have.ToString());
                    return false;
                }
            }
            return true;
        }

        public static bool DeductCost(
            Map map,
            List<WorldObject_WD_Outpost> warehouses,
            List<OutpostUpgradeCostEntry> cost,
            out string reason,
            out HashSet<WorldObject_WD_Outpost> warehousesThatContributed)
        {
            reason = null;
            warehousesThatContributed = new HashSet<WorldObject_WD_Outpost>();
            if (cost == null || cost.Count == 0) return true;
            foreach (var c in cost)
            {
                if (c == null || c.count <= 0) continue;
                if (c.thingDef == null && !IsAnyStoneBlocksCost(c)) continue;
                int toRemove = c.count;
                bool isStone = IsAnyStoneBlocksCost(c);

                if (map != null)
                    toRemove -= DeductFromMap(map, c.thingDef, isStone, toRemove);

                if (toRemove > 0 && warehouses != null)
                {
                    for (int i = 0; i < warehouses.Count && toRemove > 0; i++)
                    {
                        var comp = CompOutpostWarehouse.Get(warehouses[i]);
                        if (comp == null) continue;
                        int took = isStone ? comp.WithdrawStoneBlocksUpTo(toRemove) : comp.WithdrawUpTo(c.thingDef, toRemove);
                        if (took > 0)
                            warehousesThatContributed.Add(warehouses[i]);
                        toRemove -= took;
                    }
                }

                if (toRemove > 0)
                {
                    reason = "TSA_WD_OutpostUpgrades_DeductFailed".Translate(GetCostDisplayLabel(c));
                    return false;
                }
            }
            return true;
        }

        private static int DeductFromMap(Map map, ThingDef def, bool isStone, int amount)
        {
            if (map == null || amount <= 0) return 0;
            int toRemove = amount;
            var source = isStone ? map.listerThings.AllThings : map.listerThings.ThingsOfDef(def);
            var pool = new List<Thing>();
            for (int pi = 0; pi < source.Count; pi++)
            {
                var item = source[pi];
                if (!item.Spawned) continue;
                if (isStone && (item.def?.defName == null || !item.def.defName.StartsWith("Blocks"))) continue;
                pool.Add(item);
            }
            foreach (var t in pool)
            {
                if (toRemove <= 0) break;
                int take = Mathf.Min(toRemove, t.stackCount);
                if (take == t.stackCount) t.Destroy(DestroyMode.Vanish);
                else t.SplitOff(take).Destroy(DestroyMode.Vanish);
                toRemove -= take;
            }
            return amount - toRemove;
        }

        private static int CountAvailable(Map map, ThingDef def)
        {
            if (map == null || def == null) return 0;
            var things = map.listerThings.ThingsOfDef(def);
            int total = 0;
            for (int i = 0; i < things.Count; i++)
                if (things[i].Spawned) total += things[i].stackCount;
            return total;
        }

        private static int CountAnyStoneBlocks(Map map)
        {
            if (map == null) return 0;
            var things = map.listerThings.AllThings;
            int total = 0;
            for (int i = 0; i < things.Count; i++)
            {
                var t = things[i];
                if (t.Spawned && t.def?.defName != null && t.def.defName.StartsWith("Blocks"))
                    total += t.stackCount;
            }
            return total;
        }

        private static int CountWarehouseStored(List<WorldObject_WD_Outpost> warehouses, ThingDef def)
        {
            if (warehouses == null || def == null) return 0;
            int total = 0;
            for (int i = 0; i < warehouses.Count; i++)
            {
                var comp = CompOutpostWarehouse.Get(warehouses[i]);
                if (comp != null) total += comp.GetStoredCount(def);
            }
            return total;
        }

        private static int CountWarehouseStoneBlocks(List<WorldObject_WD_Outpost> warehouses)
        {
            if (warehouses == null) return 0;
            int total = 0;
            for (int i = 0; i < warehouses.Count; i++)
            {
                var comp = CompOutpostWarehouse.Get(warehouses[i]);
                if (comp != null) total += comp.GetStoredStoneBlocksCount();
            }
            return total;
        }
    }
}
