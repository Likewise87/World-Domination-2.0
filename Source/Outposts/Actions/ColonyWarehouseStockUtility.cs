using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Colony map + all warehouse stock for payments (same pool as outpost upgrades).
    /// Used by remote establish send and can be reused by upgrade purchase.
    /// </summary>
    public static class ColonyWarehouseStockUtility
    {
        private static int warehouseCacheTick = -1;
        private static List<WorldObject_WD_Outpost> warehouseCache;

        public static Map GetColonyMap() => Find.AnyPlayerHomeMap;

        /// <summary>All player warehouse outposts with a warehouse comp.</summary>
        public static List<WorldObject_WD_Outpost> GetAllWarehouses()
        {
            int t = Find.TickManager?.TicksGame ?? 0;
            if (warehouseCache != null && t == warehouseCacheTick)
                return warehouseCache;

            var result = new List<WorldObject_WD_Outpost>();
            if (Find.WorldObjects != null)
            {
                var all = Find.WorldObjects.AllWorldObjects;
                for (int i = 0; i < all.Count; i++)
                {
                    if (!(all[i] is WorldObject_WD_Outpost wo) || !Outpost_Warehouse_Delivery.IsWarehouseOutpost(wo)) continue;
                    if (CompOutpostWarehouse.Get(wo) == null) continue;
                    result.Add(wo);
                }
            }
            warehouseCache = result;
            warehouseCacheTick = t;
            return result;
        }

        public static int CountOnMap(Map map, ThingDef def)
        {
            if (map == null || def == null) return 0;
            var things = map.listerThings.ThingsOfDef(def);
            int total = 0;
            for (int i = 0; i < things.Count; i++)
                if (things[i].Spawned) total += things[i].stackCount;
            return total;
        }

        public static int CountInWarehouses(List<WorldObject_WD_Outpost> warehouses, ThingDef def)
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

        public static int CountOnPawns(IReadOnlyList<Pawn> pawns, ThingDef def)
        {
            if (pawns == null || def == null) return 0;
            int total = 0;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p == null || p.Destroyed || p.Dead) continue;
                if (p.inventory?.innerContainer == null) continue;
                for (int j = 0; j < p.inventory.innerContainer.Count; j++)
                {
                    Thing t = p.inventory.innerContainer[j];
                    if (t?.def == def) total += t.stackCount;
                }
            }
            return total;
        }

        public static int CountAvailable(Map map, List<WorldObject_WD_Outpost> warehouses, ThingDef def, IReadOnlyList<Pawn> alsoOnPawns = null)
        {
            return CountOnMap(map, def) + CountInWarehouses(warehouses, def) + CountOnPawns(alsoOnPawns, def);
        }

        public static bool HasCosts(
            Map map,
            List<WorldObject_WD_Outpost> warehouses,
            List<ThingDefCountClass> cost,
            IReadOnlyList<Pawn> alsoOnPawns,
            out string reason,
            Dictionary<string, bool> availabilityByDefName = null)
        {
            reason = null;
            if (cost == null || cost.Count == 0) return true;
            for (int i = 0; i < cost.Count; i++)
            {
                var c = cost[i];
                if (c?.thingDef == null || c.count <= 0) continue;
                int have = CountAvailable(map, warehouses, c.thingDef, alsoOnPawns);
                if (availabilityByDefName != null)
                    availabilityByDefName[c.thingDef.defName] = have >= c.count;
                if (have < c.count)
                {
                    reason = "TSA_WD_OutpostUpgrades_NeedHave".Translate(c.count.ToString(), c.thingDef.LabelCap, have.ToString());
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Deducts only what is not already on <paramref name="alsoOnPawns"/> from map then warehouses.
        /// Returns freshly created stacks to load onto a caravan (on-pawn items stay with the pawns).
        /// </summary>
        public static bool TryDeductDeficitAsThings(
            Map map,
            List<WorldObject_WD_Outpost> warehouses,
            List<ThingDefCountClass> cost,
            IReadOnlyList<Pawn> alsoOnPawns,
            List<Thing> into,
            out string reason)
        {
            reason = null;
            into?.Clear();
            if (into == null) return false;
            if (cost == null || cost.Count == 0) return true;

            for (int i = 0; i < cost.Count; i++)
            {
                var c = cost[i];
                if (c?.thingDef == null || c.count <= 0) continue;
                int onPawns = CountOnPawns(alsoOnPawns, c.thingDef);
                int fromPool = Mathf.Max(0, c.count - onPawns);
                if (fromPool <= 0) continue;

                int remaining = fromPool;
                if (map != null)
                    remaining -= DeductFromMap(map, c.thingDef, remaining);

                if (remaining > 0 && warehouses != null)
                {
                    for (int w = 0; w < warehouses.Count && remaining > 0; w++)
                    {
                        var comp = CompOutpostWarehouse.Get(warehouses[w]);
                        if (comp == null) continue;
                        int took = comp.WithdrawUpTo(c.thingDef, remaining);
                        remaining -= took;
                    }
                }

                if (remaining > 0)
                {
                    reason = "TSA_WD_OutpostUpgrades_DeductFailed".Translate(c.thingDef.LabelCap);
                    // Best-effort: destroy already-created stacks so we do not duplicate free goods.
                    for (int t = 0; t < into.Count; t++)
                        if (into[t] != null && !into[t].Destroyed)
                            into[t].Destroy(DestroyMode.Vanish);
                    into.Clear();
                    return false;
                }

                int leftToMake = fromPool;
                int stackLimit = Mathf.Max(1, c.thingDef.stackLimit);
                while (leftToMake > 0)
                {
                    Thing thing = ThingMaker.MakeThing(c.thingDef);
                    if (thing == null) break;
                    thing.stackCount = Mathf.Min(leftToMake, stackLimit);
                    into.Add(thing);
                    leftToMake -= thing.stackCount;
                }
            }
            return true;
        }

        private static int DeductFromMap(Map map, ThingDef def, int amount)
        {
            if (map == null || def == null || amount <= 0) return 0;
            int toRemove = amount;
            var source = map.listerThings.ThingsOfDef(def);
            var pool = new List<Thing>();
            for (int i = 0; i < source.Count; i++)
            {
                if (source[i].Spawned) pool.Add(source[i]);
            }
            for (int i = 0; i < pool.Count && toRemove > 0; i++)
            {
                Thing t = pool[i];
                int take = Mathf.Min(toRemove, t.stackCount);
                if (take >= t.stackCount) t.Destroy(DestroyMode.Vanish);
                else t.SplitOff(take).Destroy(DestroyMode.Vanish);
                toRemove -= take;
            }
            return amount - toRemove;
        }
    }
}
