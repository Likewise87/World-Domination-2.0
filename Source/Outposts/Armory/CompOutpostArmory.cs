using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    public class CompProperties_OutpostArmory : WorldObjectCompProperties
    {
        public CompProperties_OutpostArmory() => compClass = typeof(CompOutpostArmory);
    }

    /// <summary>
    /// Loose gear stored at any outpost. Two halves: <see cref="Stock"/> holds ordinary items as
    /// abstract def+stuff+quality rows, and <see cref="uniques"/> holds real Things whose state cannot
    /// survive that reduction (persona weapons, art, relics, quest items).
    /// On a warehouse outpost <see cref="Stock"/> IS <see cref="CompOutpostWarehouse.storedItems"/>, so
    /// the Warehouse tab and the Armory read and write one list. Use <see cref="ArmoryRows"/> for gear UI:
    /// the shared list also holds non-gear goods (steel, components).
    /// </summary>
    public class CompOutpostArmory : WorldObjectComp, IThingHolder
    {
        private List<ThingDefCountClass> stock = new List<ThingDefCountClass>();
        private ThingOwner<Thing> uniques;
        private readonly List<ThingDefCountClass> armoryRowsBuffer = new List<ThingDefCountClass>();

        public CompOutpostArmory()
        {
            // Built here rather than lazily: Scribe_Deep replaces the instance on load.
            uniques = new ThingOwner<Thing>(this, LookMode.Deep, removeContentsIfDestroyed: true)
            {
                dontTickContents = true
            };
        }

        private CompOutpostWarehouse SharedWarehouse =>
            parent is WorldObject_WD_Outpost o && Outpost_Production_Utils.IsWarehouseOutpost(o.def)
                ? CompOutpostWarehouse.Get(o)
                : null;

        /// <summary>True when rows live in the warehouse list instead of this comp.</summary>
        public bool UsesWarehouseStock => SharedWarehouse != null;

        /// <summary>Every row in this outpost's store, gear or not. Gear UI must use <see cref="ArmoryRows"/>.</summary>
        public List<ThingDefCountClass> Stock
        {
            get
            {
                CompOutpostWarehouse wh = SharedWarehouse;
                if (wh != null)
                    return wh.storedItems ?? (wh.storedItems = new List<ThingDefCountClass>());
                return stock ?? (stock = new List<ThingDefCountClass>());
            }
        }

        public ThingOwner<Thing> Uniques => uniques;

        /// <summary>
        /// Rows passing <see cref="OutpostArmoryUtility.IsArmoryItem"/>. Shared buffer: refilled on every
        /// call, so copy it before mutating the store mid-iteration.
        /// </summary>
        public List<ThingDefCountClass> ArmoryRows()
        {
            armoryRowsBuffer.Clear();
            List<ThingDefCountClass> rows = Stock;
            for (int i = 0; i < rows.Count; i++)
            {
                ThingDefCountClass e = rows[i];
                if (e?.thingDef == null || e.count <= 0) continue;
                if (!OutpostArmoryUtility.IsArmoryItem(e.thingDef)) continue;
                armoryRowsBuffer.Add(e);
            }
            return armoryRowsBuffer;
        }

        /// <summary>
        /// Deliberately hides <see cref="WorldObjectComp.ParentHolder"/>, which would chain up to the
        /// world. Null keeps the store out of ThingOwnerUtility map/holder walks, so no vanilla
        /// system reaches in and tries to resolve a map for these Things.
        /// </summary>
        public new IThingHolder ParentHolder => null;

        public ThingOwner GetDirectlyHeldThings() => uniques;

        public void GetChildHolders(List<IThingHolder> outChildren) =>
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, uniques);

        public static CompOutpostArmory Get(WorldObject_WD_Outpost outpost) =>
            outpost?.GetComponent<CompOutpostArmory>();

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Collections.Look(ref stock, "armoryStock", LookMode.Deep);
            Scribe_Deep.Look(ref uniques, "armoryUniques", this);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (stock == null)
                    stock = new List<ThingDefCountClass>();
                else
                    CompOutpostWarehouse.PruneUnusableMinifiedStock(stock);

                // Saves from before the shared store kept warehouse gear in this list, where nothing reads it now.
                CompOutpostWarehouse wh = SharedWarehouse;
                if (wh != null && stock.Count > 0)
                {
                    List<ThingDefCountClass> shared = Stock;
                    for (int i = 0; i < stock.Count; i++)
                    {
                        ThingDefCountClass row = stock[i];
                        if (row?.thingDef == null || row.count <= 0) continue;
                        CompOutpostWarehouse.MergeCount(shared, CompOutpostWarehouse.PlainStockRow(row, row.count));
                    }
                    stock.Clear();
                }

                if (uniques == null)
                {
                    uniques = new ThingOwner<Thing>(this, LookMode.Deep, removeContentsIfDestroyed: true)
                    {
                        dontTickContents = true
                    };
                }
            }
        }

        public bool IsEmpty => GetTotalItemCount() <= 0 && (uniques == null || uniques.Count == 0);

        /// <summary>Gear count only; on a warehouse the shared list also holds non-gear goods.</summary>
        public int GetTotalItemCount()
        {
            List<ThingDefCountClass> rows = Stock;
            int sum = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                var e = rows[i];
                if (e?.thingDef == null || e.count <= 0) continue;
                if (!OutpostArmoryUtility.IsArmoryItem(e.thingDef)) continue;
                sum += e.count;
            }
            return sum;
        }

        public int GetStockCountMatching(ThingDefCountClass match)
        {
            if (match?.thingDef == null) return 0;
            List<ThingDefCountClass> rows = Stock;
            int total = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                var e = rows[i];
                if (e == null || e.count <= 0) continue;
                if (CompOutpostWarehouse.SameStockIdentity(e, match))
                    total += e.count;
            }
            return total;
        }

        /// <summary>Total across all rows for <paramref name="def"/>, any stuff or quality.</summary>
        public int GetStockCountOfDef(ThingDef def)
        {
            if (def == null) return 0;
            List<ThingDefCountClass> rows = Stock;
            int total = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                var e = rows[i];
                if (e?.thingDef == def && e.count > 0)
                    total += e.count;
            }
            return total;
        }

        /// <summary>Merges an abstract row into stock. Caller is responsible for category filtering.</summary>
        public void DepositStockRow(ThingDefCountClass row)
        {
            if (row?.thingDef == null || row.count <= 0) return;
            if (CompOutpostWarehouse.IsUnusableMinifiedDef(row.thingDef)) return;
            CompOutpostWarehouse.MergeCount(Stock, CompOutpostWarehouse.PlainStockRow(row, row.count));
        }

        /// <summary>Removes up to <paramref name="amount"/> matching def+stuff+quality; returns how many came out.</summary>
        public int WithdrawUpToMatching(ThingDefCountClass match, int amount)
        {
            if (match?.thingDef == null || amount <= 0) return 0;
            List<ThingDefCountClass> rows = Stock;
            int remaining = amount;
            for (int i = 0; i < rows.Count && remaining > 0; i++)
            {
                var e = rows[i];
                if (e == null || e.count <= 0 || !CompOutpostWarehouse.SameStockIdentity(e, match)) continue;
                int take = e.count < remaining ? e.count : remaining;
                e.count -= take;
                remaining -= take;
            }
            CompOutpostWarehouse.PruneEmpty(rows);
            return amount - remaining;
        }

        /// <summary>
        /// Wipes this comp's own rows and the uniques. Never touches the shared warehouse list, which
        /// belongs to <see cref="CompOutpostWarehouse"/>. Safe to call more than once.
        /// </summary>
        public void ClearAndDestroyAll()
        {
            stock?.Clear();
            uniques?.ClearAndDestroyContents();
        }
    }
}
