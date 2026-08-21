using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Mineable products from defs with surface scatter (VOE-style scan) and from vein-only resource rocks
    /// (e.g. Odyssey <c>MineableObsidian</c>: no scatter fields), plus a vanilla ore safety union.
    /// Logs when falling back to or supplementing with static lists.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class MiningScatterDiscovery
    {
        public const string DevLogPrefix = "[TSA_WD Mining]";

        private static List<ThingDef> effectiveOresOrdered = new List<ThingDef>();
        private static bool rebuilt;
        private static bool loggedStoneBlockFallback;

        static MiningScatterDiscovery()
        {
            LongEventHandler.ExecuteWhenFinished(RebuildEffectiveList);
        }

        /// <summary>Ordered surface ores for mining UI and tile product lists (scatter + vanilla union).</summary>
        public static IReadOnlyList<ThingDef> GetEffectiveScatterOresOrdered()
        {
            if (!rebuilt) RebuildEffectiveList();
            return effectiveOresOrdered;
        }

        public static void RebuildEffectiveList()
        {
            rebuilt = true;
            var scatterRaw = BuildScatterMineableProductsRaw();
            if (scatterRaw.Count == 0)
            {
                Log.Warning(
                    $"{DevLogPrefix} Scatter mineable scan found 0 products; falling back to static vanilla ore list (Silver, Gold, Steel, Plasteel, Uranium, Jade).");
                effectiveOresOrdered = BuildVanillaOreListOnly();
                return;
            }

            var merged = new List<ThingDef>(scatterRaw);
            var seen = new HashSet<ThingDef>(merged);
            var addedFromStatic = new List<string>();
            foreach (ThingDef v in VanillaOreThingDefs())
            {
                if (v == null) continue;
                if (seen.Add(v))
                {
                    merged.Add(v);
                    addedFromStatic.Add(v.defName);
                }
            }

            if (addedFromStatic.Count > 0)
                Log.Warning(
                    $"{DevLogPrefix} Scatter scan did not include vanilla ore(s) {string.Join(", ", addedFromStatic)}; merged from static list.");

            merged.Sort((a, b) => string.CompareOrdinal(a?.defName ?? "", b?.defName ?? ""));
            effectiveOresOrdered = merged;
        }

        private static List<ThingDef> BuildVanillaOreListOnly()
        {
            var list = new List<ThingDef>();
            foreach (ThingDef t in VanillaOreThingDefs())
                if (t != null && !list.Contains(t)) list.Add(t);
            list.Sort((a, b) => string.CompareOrdinal(a?.defName ?? "", b?.defName ?? ""));
            return list;
        }

        private static IEnumerable<ThingDef> VanillaOreThingDefs()
        {
            yield return ThingDefOf.Silver;
            yield return ThingDefOf.Gold;
            yield return ThingDefOf.Steel;
            yield return ThingDefOf.Plasteel;
            yield return DefDatabase<ThingDef>.GetNamedSilentFail("Uranium");
            yield return DefDatabase<ThingDef>.GetNamedSilentFail("Jade");
        }

        private static bool HasScatterMineableProps(BuildingProperties b)
        {
            return b != null
                && b.mineableScatterCommonality > 0f
                && b.mineableScatterLumpSizeRange.max > 0;
        }

        /// <summary>
        /// Odyssey (and similar): resource rock mined from veins / mutators with no map-gen scatter commonality.
        /// Vanilla ores use both scatter and <see cref="BuildingProperties.veinMineable"/>; this branch only adds when scatter is absent.
        /// </summary>
        private static bool IsVeinOnlyResourceRockMineable(BuildingProperties b)
        {
            return b != null
                && b.veinMineable
                && b.isResourceRock
                && b.mineableThing != null
                && !HasScatterMineableProps(b);
        }

        private static List<ThingDef> BuildScatterMineableProductsRaw()
        {
            var result = new List<ThingDef>();
            var seen = new HashSet<ThingDef>();
            foreach (ThingDef thing in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                BuildingProperties b = thing?.building;
                if (b?.mineableThing == null) continue;
                if (!HasScatterMineableProps(b) && !IsVeinOnlyResourceRockMineable(b)) continue;
                ThingDef product = b.mineableThing;
                if (IsChunk(product)) continue;
                if (product != null && seen.Add(product)) result.Add(product);
            }

            return result;
        }

        private static bool IsChunk(ThingDef td)
        {
            if (td == null) return true;
            if (td.thingClass != null && typeof(Chunks).IsAssignableFrom(td.thingClass)) return true;
            if (!string.IsNullOrEmpty(td.defName) &&
                td.defName.IndexOf("chunk", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return td.thingCategories != null && td.thingCategories.Contains(ThingCategoryDefOf.StoneChunks);
        }

        /// <summary>Logs once when tile mining uses static vanilla block defs because no rock types were resolved.</summary>
        public static void LogStaticStoneBlockFallbackOnce()
        {
            if (loggedStoneBlockFallback) return;
            loggedStoneBlockFallback = true;
            Log.Warning(
                $"{DevLogPrefix} No natural rock types for at least one tile; using static vanilla stone blocks (BlocksGranite/Marble/Sandstone/Limestone/Slate). Further occurrences are not logged.");
        }
    }
}
