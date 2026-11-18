using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Colony map + all outpost Stock for payments (same pool as outpost upgrades).
    /// Used by remote establish send and can be reused by upgrade purchase.
    /// </summary>
    public static class ColonyWarehouseStockUtility
    {
        private static int storeCacheTick = -1;
        private static List<WorldObject_WD_Outpost> storeCache;

        public static Map GetColonyMap() => Find.AnyPlayerHomeMap;

        /// <summary>All player outposts with <see cref="CompOutpostArmory"/> (cached per tick).</summary>
        public static List<WorldObject_WD_Outpost> GetAllWarehouses()
        {
            int t = Find.TickManager?.TicksGame ?? 0;
            if (storeCache != null && t == storeCacheTick)
                return storeCache;

            // Same pool as upgrades / world build.
            storeCache = ColonyWorldBuildMaterials.GetContributingWarehouses();
            storeCacheTick = t;
            return storeCache;
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
                CompOutpostArmory armory = CompOutpostArmory.Get(warehouses[i]);
                if (armory != null) total += armory.GetStockCountOfDef(def);
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
                        CompOutpostArmory armory = CompOutpostArmory.Get(warehouses[w]);
                        if (armory == null) continue;
                        remaining -= armory.WithdrawUpTo(c.thingDef, remaining);
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

        public static bool TryConsume(
            Map map,
            List<WorldObject_WD_Outpost> warehouses,
            ThingDef def,
            int count,
            out string reason)
        {
            reason = null;
            if (def == null || count <= 0) return true;
            int have = CountAvailable(map, warehouses, def);
            if (have < count)
            {
                reason = "TSA_WD_OutpostUpgrades_NeedHave".Translate(count.ToString(), def.LabelCap, have.ToString());
                return false;
            }

            int remaining = count;
            if (map != null)
                remaining -= DeductFromMap(map, def, remaining);
            if (remaining > 0 && warehouses != null)
            {
                for (int w = 0; w < warehouses.Count && remaining > 0; w++)
                {
                    CompOutpostArmory armory = CompOutpostArmory.Get(warehouses[w]);
                    if (armory == null) continue;
                    remaining -= armory.WithdrawUpTo(def, remaining);
                }
            }

            if (remaining > 0)
            {
                reason = "TSA_WD_OutpostUpgrades_DeductFailed".Translate(def.LabelCap);
                return false;
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
