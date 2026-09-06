using System.Runtime.CompilerServices;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Instance MarketValue state that vanilla sealed <see cref="ThingDefCountClass"/> cannot carry
    /// (taint, HP, biocode). Attached to payment-pool rows via <see cref="WdStockExtrasTable"/>.
    /// Warehouse persistence of these fields is out of scope.
    /// </summary>
    public class WdStockExtras
    {
        public bool wornByCorpse;
        /// <summary>-1 = unset (warehouse-style / unspecified).</summary>
        public int hitPoints = -1;
        public bool biocoded;
    }

    public static class WdStockExtrasTable
    {
        private static readonly ConditionalWeakTable<ThingDefCountClass, WdStockExtras> Table =
            new ConditionalWeakTable<ThingDefCountClass, WdStockExtras>();

        public static bool TryGet(ThingDefCountClass row, out WdStockExtras extras)
        {
            if (row == null)
            {
                extras = null;
                return false;
            }
            return Table.TryGetValue(row, out extras);
        }

        public static WdStockExtras GetOrCreate(ThingDefCountClass row)
        {
            if (row == null) return null;
            if (Table.TryGetValue(row, out WdStockExtras existing))
                return existing;
            var created = new WdStockExtras();
            Table.Add(row, created);
            return created;
        }

        public static void CopyTo(ThingDefCountClass from, ThingDefCountClass to)
        {
            if (from == null || to == null) return;
            if (!TryGet(from, out WdStockExtras src)) return;
            WdStockExtras dst = GetOrCreate(to);
            dst.wornByCorpse = src.wornByCorpse;
            dst.hitPoints = src.hitPoints;
            dst.biocoded = src.biocoded;
        }

        public static bool GetWornByCorpse(ThingDefCountClass row) =>
            TryGet(row, out WdStockExtras e) && e.wornByCorpse;

        public static bool GetBiocoded(ThingDefCountClass row) =>
            TryGet(row, out WdStockExtras e) && e.biocoded;

        public static int GetHitPoints(ThingDefCountClass row) =>
            TryGet(row, out WdStockExtras e) ? e.hitPoints : -1;
    }
}
