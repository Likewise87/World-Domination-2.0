using System.Collections.Generic;
using RimWorld;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Single intake for anything arriving at a player outpost (caravan dissolve, pod arrival, goods
    /// delivery, refunds). Gear goes through <see cref="OutpostArmoryUtility.TryDeposit"/> (uniques,
    /// Normalize, CE unload); on a warehouse, non-gear goods fall back to the warehouse list. Food is
    /// always stored as items; it only becomes virtual food through <see cref="OutpostFoodConversion"/>.
    /// </summary>
    public static class OutpostStorageUtility
    {
        /// <summary>
        /// Stores a real Thing and disposes of it when it became a row. Returns false when the outpost
        /// has nowhere to keep it; the caller still owns the Thing then.
        /// </summary>
        public static bool TryStoreThing(WorldObject_WD_Outpost outpost, Thing thing)
        {
            if (outpost == null || thing == null || thing.Destroyed) return false;
            if (OutpostArmoryUtility.TryDepositAndDispose(outpost, thing)) return true;

            CompOutpostWarehouse wh = Outpost_Warehouse_Delivery.IsWarehouseOutpost(outpost)
                ? CompOutpostWarehouse.Get(outpost)
                : null;
            if (wh == null) return false;

            Thing content = thing.GetInnerIfMinified() ?? thing;
            if (content?.def == null || CompOutpostWarehouse.IsUnusableMinifiedDef(content.def)) return false;

            wh.TryDepositThings(new List<Thing> { thing });
            if (!thing.Destroyed) thing.Destroy(DestroyMode.Vanish);
            return true;
        }

        /// <summary>Whether a delivered row has a home at <paramref name="outpost"/>.</summary>
        public static bool CanStoreRow(WorldObject_WD_Outpost outpost, ThingDef def)
        {
            if (outpost == null || def == null) return false;
            if (CompOutpostWarehouse.IsUnusableMinifiedDef(def)) return false;
            if (Outpost_Warehouse_Delivery.IsWarehouseOutpost(outpost))
                return CompOutpostWarehouse.Get(outpost) != null;
            return OutpostArmoryUtility.FeatureEnabled
                && CompOutpostArmory.Get(outpost) != null
                && OutpostArmoryUtility.IsArmoryItem(def);
        }

        /// <summary>
        /// Banks abstract rows into the outpost store. Returns the rows actually stored, for letters.
        /// Rows without a home (non-gear at a non-warehouse outpost, or the Armory switched off while
        /// the cargo was in flight) are dropped at the home colony instead of being lost.
        /// </summary>
        public static List<ThingDefCountClass> DepositRows(WorldObject_WD_Outpost outpost, List<ThingDefCountClass> rows)
        {
            var stored = new List<ThingDefCountClass>();
            if (outpost == null || rows == null) return stored;

            CompOutpostArmory armory = CompOutpostArmory.Get(outpost);
            CompOutpostWarehouse wh = Outpost_Warehouse_Delivery.IsWarehouseOutpost(outpost)
                ? CompOutpostWarehouse.Get(outpost)
                : null;
            List<ThingDefCountClass> homeless = null;

            for (int i = 0; i < rows.Count; i++)
            {
                ThingDefCountClass row = rows[i];
                if (row?.thingDef == null || row.count <= 0) continue;
                if (!CanStoreRow(outpost, row.thingDef))
                {
                    (homeless ??= new List<ThingDefCountClass>()).Add(row);
                    continue;
                }

                if (wh != null)
                    wh.TryDeposit(new List<ThingDefCountClass> { row });
                else
                    armory.DepositStockRow(row);
                stored.Add(CompOutpostWarehouse.PlainStockRow(row, row.count));
            }

            if (homeless != null)
                SettlementBuyUtility.RefundItems(null, homeless);
            return stored;
        }

        /// <summary>
        /// Moves uniques into the outpost's armory. Things it cannot hold are dropped at the home
        /// colony, and destroyed only when there is no colony map. Returns letter lines for what was stored.
        /// </summary>
        public static List<string> DepositUniques(WorldObject_WD_Outpost outpost, ThingOwner<Thing> uniques)
        {
            var lines = new List<string>();
            if (uniques == null) return lines;
            CompOutpostArmory armory = CompOutpostArmory.Get(outpost);

            while (uniques.Count > 0)
            {
                Thing t = uniques[0];
                uniques.Remove(t);
                if (t == null || t.Destroyed) continue;
                string line = "- " + t.LabelCapNoCount + " " + (t.stackCount > 0 ? t.stackCount : 1) + "x";
                if (armory != null && armory.Uniques.TryAddOrTransfer(t, canMergeWithExistingStacks: false))
                {
                    lines.Add(line);
                    continue;
                }
                Map home = Find.AnyPlayerHomeMap;
                if (home != null && !t.Destroyed && t.holdingOwner == null
                    && GenPlace.TryPlaceThing(t, WorldActions_Traveler.FindColonyDeliveryOrTradeDropCell(home), home, ThingPlaceMode.Near))
                    continue;
                if (!t.Destroyed) t.Destroy(DestroyMode.Vanish);
            }
            return lines;
        }
    }
}
