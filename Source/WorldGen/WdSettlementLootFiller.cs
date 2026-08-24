using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using KCSG;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public static class WdSettlementLootFiller
    {
        private static readonly Regex LayoutNameRx = new Regex(
            @"TSA_(Tribal|Generic)_T([1-4])_(\w+)",
            RegexOptions.Compiled);
        private static string SettlementLabel(Map map) => map?.Parent?.LabelCap ?? map?.Parent?.def?.defName ?? "unknown-settlement";

        public static void FillShelves(Map map)
        {
            if (map?.Parent == null)
            {
                WDVerbose.RemapNoTick("Shelf fill skipped settlement=unknown-settlement reason=map-or-parent-null");
                return;
            }
            if (!TryResolveSettlementTypeAndTier(map, out string settlementType, out string tier))
            {
                WDVerbose.RemapNoTick($"Shelf fill skipped settlement={SettlementLabel(map)} reason=type-tier-unresolved");
                return;
            }

            // Mining settlements use one map-wide shelf pass after full KCSG generation.
            if (settlementType == "Mining")
            {
                FillMiningSettlementShelvesRemaining(map, reason: "map-wide mining sweep");
                return;
            }

            if (settlementType == "Farming")
            {
                TryFillFarmingShelves(map, tier, context: "map-wide");
                return;
            }

            WdSettlementLootTableDef table = WdBiomeTableResolver.ResolveLootTable(settlementType, tier);
            if (table == null || table.items == null || table.items.Count == 0)
            {
                WDVerbose.RemapNoTick($"Shelf fill skipped settlement={SettlementLabel(map)} type={settlementType}/{tier} reason=no-loot-table");
                return;
            }

            FillShelvesInRect(map, null, table, null, 0f, null, 0f, context: "map-wide");
        }

        /// <summary>
        /// Mining shelf loot runs once after full KCSG gen, using ore products verified anywhere on the map.
        /// </summary>
        public static void FillMiningSettlementShelvesRemaining(Map map, string reason)
        {
            if (map == null) return;
            if (!TryResolveSettlementTypeAndTier(map, out string settlementType, out string tier)) return;
            if (settlementType != "Mining") return;

            TryFillMiningShelves(map, null, tier, reason, onlyEmptyShelves: false);
        }

        private static void TryFillMiningShelves(
            Map map,
            CellRect? rect,
            string tier,
            string context,
            bool onlyEmptyShelves = false)
        {
            WdSettlementLootTableDef table = WdBiomeTableResolver.ResolveLootTable("Mining", tier);
            if (table == null || table.items == null || table.items.Count == 0)
            {
                WDVerbose.RemapNoTick($"Shelf fill skipped settlement={SettlementLabel(map)} type=Mining/{tier} context={context} reason=no-loot-table");
                return;
            }

            WdMiningOrePoolDef pool = WdBiomeTableResolver.ResolveMiningOrePool(map.Biome);
            float oreFraction = pool?.shelfOreBudgetFraction ?? 0.5f;
            if (oreFraction < 0f) oreFraction = 0f;
            else if (oreFraction > 1f) oreFraction = 1f;

            var spawnedCheap = KcsgRockTypeRemapper.GetSessionSpawnedCheap();
            var spawnedExpensive = KcsgRockTypeRemapper.GetSessionSpawnedExpensive();

            BuildSpawnedOreProductBuckets(spawnedCheap, spawnedExpensive,
                out List<WdWeightedThingOption> cheapProducts,
                out List<WdWeightedThingOption> expensiveProducts);

            bool hasCheap = cheapProducts != null && cheapProducts.Count > 0;
            bool hasExpensive = expensiveProducts != null && expensiveProducts.Count > 0;

            float oreBudget = table.valueBudget * oreFraction;
            float cheapBudget = 0f;
            float expensiveBudget = 0f;

            if (hasCheap)
            {
                // Prefer cheap/common ore first so mining shelves do not skew into rare-only loot.
                cheapBudget = hasExpensive ? oreBudget * 0.60f : oreBudget;
                expensiveBudget = hasExpensive ? oreBudget - cheapBudget : 0f;
            }
            else if (hasExpensive)
                expensiveBudget = oreBudget;
            else
                oreBudget = 0f;

            if (!hasCheap && !hasExpensive)
            {
                WDVerbose.RemapNoTick(
                    $"Shelf fill settlement={SettlementLabel(map)} type=Mining/{tier} context={context} reason=no-verified-ore-products "
                    + $"(rolled cheap={KcsgRockTypeRemapper.ChosenCheapOre?.defName ?? "null"} "
                    + $"expensive={KcsgRockTypeRemapper.ChosenExpensiveOre?.defName ?? "null"})");
            }

            FillShelvesInRect(map, rect, table, cheapProducts, cheapBudget, expensiveProducts, expensiveBudget,
                context, onlyEmptyShelves, BuildMiningGenericExclusionSet(cheapProducts, expensiveProducts));
        }

        private static void TryFillFarmingShelves(Map map, string tier, string context)
        {
            WdSettlementLootTableDef table = WdBiomeTableResolver.ResolveLootTable("Farming", tier);
            if (table == null || table.items == null || table.items.Count == 0)
            {
                WDVerbose.RemapNoTick($"Shelf fill skipped settlement={SettlementLabel(map)} type=Farming/{tier} context={context} reason=no-loot-table");
                return;
            }

            WdBiomeCropTableDef cropTable = WdBiomeTableResolver.ResolveCropTable(map.Biome);
            float cropFraction = cropTable?.shelfCropBudgetFraction ?? 0.5f;
            if (cropFraction < 0f) cropFraction = 0f;
            else if (cropFraction > 1f) cropFraction = 1f;

            List<WdWeightedThingOption> cropProducts = KcsgRockTypeRemapper.GetSessionCropProducts();
            float cropBudget = 0f;
            if (cropProducts != null && cropProducts.Count > 0)
                cropBudget = table.valueBudget * cropFraction;
            else
            {
                WDVerbose.RemapNoTick(
                    $"Shelf fill settlement={SettlementLabel(map)} type=Farming/{tier} context={context} reason=no-verified-crop-products "
                    + $"(crop={KcsgRockTypeRemapper.ChosenCrop?.defName ?? "null"})");
            }

            FillShelvesInRect(map, null, table, cropProducts, cropBudget, null, 0f, context, onlyEmptyShelves: false);
        }

        private static void BuildSpawnedOreProductBuckets(
            IEnumerable<ThingDef> spawnedCheap,
            IEnumerable<ThingDef> spawnedExpensive,
            out List<WdWeightedThingOption> cheapProducts,
            out List<WdWeightedThingOption> expensiveProducts)
        {
            cheapProducts = new List<WdWeightedThingOption>();
            expensiveProducts = new List<WdWeightedThingOption>();

            if (spawnedCheap != null)
            {
                foreach (ThingDef mineable in spawnedCheap)
                    AddOreProductOption(cheapProducts, mineable, "cheap-spawned");
            }

            if (spawnedExpensive != null)
            {
                foreach (ThingDef mineable in spawnedExpensive)
                    AddOreProductOption(expensiveProducts, mineable, "expensive-spawned");
            }

            if (cheapProducts.Count == 0) cheapProducts = null;
            if (expensiveProducts.Count == 0) expensiveProducts = null;
        }

        private static string FormatSpawnedMineables(IEnumerable<ThingDef> mineables)
        {
            if (mineables == null) return "-";
            var list = mineables.ToList();
            if (list.Count == 0) return "-";
            return string.Join(",", list.Select(m => m.defName));
        }

        private static void AddOreProductOption(List<WdWeightedThingOption> options, ThingDef mineable, string slot)
        {
            ThingDef product = KcsgRockTypeRemapper.MinedProductForMineable(mineable);
            if (mineable == null)
            {
                return;
            }
            if (product == null)
            {
                return;
            }
            if (options.Any(o => o.thingDef == product)) return;
            options.Add(new WdWeightedThingOption { thingDef = product, weight = 1f });
        }

        private static string FormatOreProducts(List<WdWeightedThingOption> options)
        {
            if (options == null || options.Count == 0) return "-";
            return string.Join("/", options.Select(o => o.thingDef?.defName ?? "?"));
        }

        private static HashSet<ThingDef> BuildMiningGenericExclusionSet(
            List<WdWeightedThingOption> cheapProducts,
            List<WdWeightedThingOption> expensiveProducts)
        {
            var excluded = new HashSet<ThingDef>();
            if (cheapProducts != null)
            {
                for (int i = 0; i < cheapProducts.Count; i++)
                {
                    ThingDef def = cheapProducts[i]?.thingDef;
                    if (def != null) excluded.Add(def);
                }
            }
            if (expensiveProducts != null)
            {
                for (int i = 0; i < expensiveProducts.Count; i++)
                {
                    ThingDef def = expensiveProducts[i]?.thingDef;
                    if (def != null) excluded.Add(def);
                }
            }
            return excluded;
        }

        private static void FillShelvesInRect(
            Map map,
            CellRect? rect,
            WdSettlementLootTableDef table,
            List<WdWeightedThingOption> primaryProducts,
            float primaryBudget,
            List<WdWeightedThingOption> secondaryProducts,
            float secondaryBudget,
            string context,
            bool onlyEmptyShelves = false,
            HashSet<ThingDef> genericExclusions = null)
        {
            List<Building> shelves = (map.listerThings?.ThingsInGroup(ThingRequestGroup.BuildingArtificial) ?? new List<Thing>())
                .OfType<Building>()
                .Where(b => IsStorageBuilding(b) && ShelfInRect(b, rect))
                .Distinct()
                .ToList();

            if (onlyEmptyShelves)
                shelves = shelves.Where(s => ShelfHasNoItems(s, map)).ToList();

            float genericBudget = table.valueBudget - primaryBudget - secondaryBudget;
            if (genericBudget < 0f) genericBudget = 0f;

            float primarySpent = 0f;
            float secondarySpent = 0f;
            float genericSpent = 0f;
            int spawned = 0;
            int touches = 0;
            int rejectedFilter = 0;
            int placeFailed = 0;
            int skippedNoStore = 0;
            int skippedNoSlots = 0;

            var tableItems = table.items
                .Where(o => o?.thingDef != null && (genericExclusions == null || !genericExclusions.Contains(o.thingDef)))
                .ToList();
            bool hasPrimary = primaryProducts != null && primaryProducts.Count > 0 && primaryBudget > 0f;
            bool hasSecondary = secondaryProducts != null && secondaryProducts.Count > 0 && secondaryBudget > 0f;

            foreach (Building shelf in shelves)
            {
                if (primarySpent >= primaryBudget && secondarySpent >= secondaryBudget && genericSpent >= genericBudget)
                    break;
                ISlotGroupParent slotParent = shelf as ISlotGroupParent;
                StorageSettings store = slotParent?.GetStoreSettings();
                if (store == null)
                {
                    skippedNoStore++;
                    continue;
                }
                touches++;

                List<IntVec3> slotCells = GetStorageSlotCells(shelf);
                if (slotCells.Count == 0)
                {
                    skippedNoSlots++;
                    continue;
                }

                foreach (IntVec3 cell in slotCells)
                {
                    if (primarySpent >= primaryBudget && secondarySpent >= secondaryBudget && genericSpent >= genericBudget)
                        break;
                    if (!cell.InBounds(map)) continue;
                    if (onlyEmptyShelves && cell.GetFirstItem(map) != null) continue;

                    bool usePrimary = hasPrimary && primarySpent < primaryBudget
                        && (!hasSecondary || secondarySpent >= secondaryBudget || Rand.Value < 0.5f);
                    bool useSecondary = !usePrimary && hasSecondary && secondarySpent < secondaryBudget;
                    bool useOverride = usePrimary || useSecondary;

                    List<WdWeightedThingOption> bucketProducts = usePrimary ? primaryProducts
                        : useSecondary ? secondaryProducts
                        : null;
                    float bucketBudget = usePrimary ? primaryBudget
                        : useSecondary ? secondaryBudget
                        : genericBudget;
                    float bucketSpent = usePrimary ? primarySpent
                        : useSecondary ? secondarySpent
                        : genericSpent;

                    if (useOverride && bucketSpent >= bucketBudget)
                    {
                        if (usePrimary && hasSecondary && secondarySpent < secondaryBudget)
                        {
                            usePrimary = false;
                            useSecondary = true;
                            bucketProducts = secondaryProducts;
                            bucketBudget = secondaryBudget;
                            bucketSpent = secondarySpent;
                        }
                        else if (!useOverride || bucketSpent >= bucketBudget)
                        {
                            useOverride = false;
                            bucketProducts = null;
                            bucketBudget = genericBudget;
                            bucketSpent = genericSpent;
                        }
                    }

                    if (!useOverride && genericSpent >= genericBudget) continue;

                    ThingDef stuff = useOverride
                        ? WdBiomeTableResolver.PickWeightedThing(bucketProducts)
                        : WdBiomeTableResolver.PickWeightedThing(tableItems);
                    if (stuff == null) continue;

                    bool isOverrideItem = useOverride && bucketProducts != null
                        && bucketProducts.Any(o => o.thingDef == stuff);

                    if (!store.AllowedToAccept(stuff))
                    {
                        ThingDef fallback = isOverrideItem
                            ? WdBiomeTableResolver.PickWeightedThing(tableItems)
                            : (hasPrimary ? WdBiomeTableResolver.PickWeightedThing(primaryProducts)
                                : hasSecondary ? WdBiomeTableResolver.PickWeightedThing(secondaryProducts)
                                : null);
                        if (fallback == null || !store.AllowedToAccept(fallback))
                        {
                            rejectedFilter++;
                            continue;
                        }
                        stuff = fallback;
                        isOverrideItem = (hasPrimary && primaryProducts.Any(o => o.thingDef == stuff))
                            || (hasSecondary && secondaryProducts.Any(o => o.thingDef == stuff));
                    }

                    bucketBudget = isOverrideItem && primaryProducts != null && primaryProducts.Any(o => o.thingDef == stuff)
                        ? primaryBudget
                        : isOverrideItem && secondaryProducts != null && secondaryProducts.Any(o => o.thingDef == stuff)
                            ? secondaryBudget
                            : genericBudget;
                    bucketSpent = isOverrideItem && primaryProducts != null && primaryProducts.Any(o => o.thingDef == stuff)
                        ? primarySpent
                        : isOverrideItem && secondaryProducts != null && secondaryProducts.Any(o => o.thingDef == stuff)
                            ? secondarySpent
                            : genericSpent;

                    if (bucketSpent >= bucketBudget) continue;

                    int maxStack = stuff.stackLimit > 0 ? stuff.stackLimit : 1;
                    float unitValue = stuff.BaseMarketValue > 0.01f ? stuff.BaseMarketValue : 1f;
                    int want = Rand.RangeInclusive(1, maxStack);
                    int affordable = (int)((bucketBudget - bucketSpent) / unitValue);
                    if (affordable <= 0) continue;
                    want = Mathf.Min(want, affordable);
                    if (want <= 0) continue;

                    try
                    {
                        Thing thing = ThingMaker.MakeThing(stuff);
                        thing.stackCount = want;
                        if (!GenPlace.TryPlaceThing(thing, cell, map, ThingPlaceMode.Direct))
                        {
                            thing.Destroy();
                            placeFailed++;
                            continue;
                        }

                        float cost = unitValue * want;
                        if (isOverrideItem && primaryProducts != null && primaryProducts.Any(o => o.thingDef == stuff))
                            primarySpent += cost;
                        else if (isOverrideItem && secondaryProducts != null && secondaryProducts.Any(o => o.thingDef == stuff))
                            secondarySpent += cost;
                        else
                            genericSpent += cost;
                        spawned++;
                    }
                    catch (System.Exception ex)
                    {
                        placeFailed++;
                    }
                }
            }

            string primaryLabel = hasSecondary ? "cheap/crop" : "override";
            string secondaryLabel = hasSecondary ? "expensive" : "";

            WDVerbose.RemapNoTick(
                $"Shelf fill settlement={SettlementLabel(map)} context={context} biome={map.Biome?.defName ?? "?"} "
                + $"rect={(rect.HasValue ? rect.Value.ToString() : "all")} shelves={touches}/{shelves.Count} stacks={spawned} "
                + $"products primary=[{FormatOreProducts(primaryProducts)}] secondary=[{FormatOreProducts(secondaryProducts)}] "
                + $"{primaryLabel}={primarySpent:F0}/{primaryBudget:F0} "
                + (hasSecondary ? $"{secondaryLabel}={secondarySpent:F0}/{secondaryBudget:F0} " : "")
                + $"generic={genericSpent:F0}/{genericBudget:F0} "
                + $"genericExcl={(genericExclusions == null || genericExclusions.Count == 0 ? "-" : string.Join("/", genericExclusions.Select(d => d.defName)))} "
                + $"noStore={skippedNoStore} noSlots={skippedNoSlots} filterReject={rejectedFilter} placeFail={placeFailed}");
        }

        private static List<IntVec3> GetStorageSlotCells(Building shelf)
        {
            var cells = new List<IntVec3>();
            if (shelf is Building_Storage storage)
            {
                foreach (IntVec3 cell in storage.AllSlotCells())
                    cells.Add(cell);
                return cells;
            }

            if (shelf is ISlotGroupParent parent)
            {
                SlotGroup group = parent.GetSlotGroup();
                if (group?.CellsList != null)
                {
                    for (int i = 0; i < group.CellsList.Count; i++)
                        cells.Add(group.CellsList[i]);
                }
            }

            if (cells.Count == 0)
            {
                foreach (IntVec3 cell in shelf.OccupiedRect().Cells)
                    cells.Add(cell);
            }

            return cells;
        }

        private static bool ShelfHasNoItems(Building shelf, Map map)
        {
            foreach (IntVec3 cell in GetStorageSlotCells(shelf))
            {
                if (cell.InBounds(map) && cell.GetFirstItem(map) != null)
                    return false;
            }
            return true;
        }

        private static bool ShelfInRect(Building shelf, CellRect? rect)
        {
            if (shelf == null) return false;
            if (rect == null) return true;
            CellRect r = rect.Value;
            if (r.Contains(shelf.Position)) return true;
            foreach (IntVec3 cell in shelf.OccupiedRect().Cells)
            {
                if (r.Contains(cell)) return true;
            }
            return false;
        }

        private static bool IsStorageBuilding(Building b)
        {
            if (b == null) return false;
            if (b is Building_Storage) return true;
            return b is ISlotGroupParent;
        }

        public static bool TryResolveSettlementTypeAndTier(Map map, out string settlementType, out string tier)
        {
            settlementType = "Default";
            tier = "Default";

            var settlement = map.Parent as Settlement;
            if (settlement != null)
            {
                var spread = settlement.GetComponent<CompViralSpread>();
                if (spread != null)
                {
                    tier = spread.tier.ToString();
                    if (!string.IsNullOrEmpty(spread.subType))
                        settlementType = MapSubtypeToLootType(spread.subType, spread.tier);
                    return true;
                }
            }

            string layoutName = map.Parent?.def?.defName;
            if (!string.IsNullOrEmpty(layoutName))
            {
                Match m = LayoutNameRx.Match(layoutName);
                if (m.Success)
                {
                    tier = "T" + m.Groups[2].Value;
                    settlementType = MapSubtypeToLootType(m.Groups[3].Value, ParseTier(tier));
                    return true;
                }
            }
            return true;
        }

        private static SettlementTier ParseTier(string t) =>
            t == "T4" ? SettlementTier.T4
            : t == "T3" ? SettlementTier.T3
            : t == "T2" ? SettlementTier.T2
            : SettlementTier.T1;

        private static string MapSubtypeToLootType(string subType, SettlementTier tier)
        {
            if (string.IsNullOrEmpty(subType)) return "Default";
            if (subType == "Farming" || subType == "Mining" || subType == "Logging"
                || subType == "Production" || subType == "Slavery" || subType == "Fortress"
                || subType == "Citadel")
                return subType;
            string s = subType.ToLowerInvariant();
            if (s.Contains("farm")) return "Farming";
            if (s.Contains("mine")) return "Mining";
            if (s.Contains("log") || s.Contains("lumber")) return "Logging";
            if (s.Contains("product") || s.Contains("industr")) return "Production";
            if (s.Contains("slav")) return "Slavery";
            if (s.Contains("fort")) return "Fortress";
            if (s.Contains("citadel") || tier == SettlementTier.T4) return "Citadel";
            return "Default";
        }
    }
}
