using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>One hunting option: animal kind + its products (meat, leather, wool).</summary>
    public struct HuntingAnimalOption
    {
        public PawnKindDef Kind;
        public List<ThingDef> Products;
    }

    /// <summary>Hunting outpost production: animal options, delivery items, tooltips.</summary>
    public static class Outpost_Hunting
    {
        /// <summary>Hunting options for a hunting outpost's tile biome.</summary>
        public static List<HuntingAnimalOption> GetHuntingAnimalOptions(WorldObject_WD_Outpost outpost)
        {
            if (outpost?.def == null || !Outpost_Production_Utils.IsHuntingOutpost(outpost.def))
                return new List<HuntingAnimalOption>();
            return GetHuntingAnimalOptionsForTile(outpost.Tile);
        }

        /// <summary>Huntable animals for a world tile biome (no outpost required). Useful for tile inspect before founding.</summary>
        public static List<HuntingAnimalOption> GetHuntingAnimalOptionsForTile(int tile)
        {
            var list = new List<HuntingAnimalOption>();
            var biome = WorldTileInfo.GetBiome(tile);
            IEnumerable<PawnKindDef> kinds = WorldTileInfo.GetWildAnimals(biome);
            var kindList = kinds?.ToList() ?? new List<PawnKindDef>();
            if (biome != null && kindList.Count > 0)
                kindList.Sort((a, b) => biome.CommonalityOfAnimal(b).CompareTo(biome.CommonalityOfAnimal(a)));

            foreach (var kind in kindList)
            {
                if (kind?.RaceProps == null) continue;
                var products = new List<ThingDef>();
                if (kind.RaceProps.meatDef != null) products.Add(kind.RaceProps.meatDef);
                if (kind.RaceProps.leatherDef != null && !products.Contains(kind.RaceProps.leatherDef)) products.Add(kind.RaceProps.leatherDef);
                ThingDef woolDef = GetWoolDefFromKind(kind);
                if (woolDef != null && !products.Contains(woolDef)) products.Add(woolDef);
                if (products.Count > 0)
                    list.Add(new HuntingAnimalOption { Kind = kind, Products = products });
            }
            return list;
        }

        /// <summary>Public for Outpost_Baselines. Wool def from race comps if any.</summary>
        public static ThingDef GetWoolDefFromKindPublic(PawnKindDef kind) => GetWoolDefFromKind(kind);

        public static ThingDef GetWoolDefFromKind(PawnKindDef kind)
        {
            if (kind?.race == null) return null;
            var comps = kind.race.comps;
            if (comps == null) return null;
            foreach (var c in comps)
            {
                if (c == null) continue;
                var t = c.GetType();
                var woolProp = t.GetProperty("woolDef", BindingFlags.Public | BindingFlags.Instance);
                if (woolProp != null && typeof(ThingDef).IsAssignableFrom(woolProp.PropertyType))
                {
                    var wool = woolProp.GetValue(c) as ThingDef;
                    if (wool != null) return wool;
                }
            }
            return null;
        }

        /// <summary>Meat per Animals skill at current tile (before output slider). Selected animal only.</summary>
        public static float GetOutputPerSkillPoint(WorldObject_WD_Outpost outpost)
        {
            if (outpost?.def == null) return 0f;
            if (!Outpost_Production_Utils.IsHuntingOutpost(outpost.def)) return 0f;
            var kind = outpost.SelectedPawnKindForHunting;
            if (kind?.RaceProps == null) return 0f;
            var y = Outpost_Baselines.GetAnimalYieldPerKill(kind);
            float tile = Outpost_Production_Utils.GetHuntingTileProductionFactor(outpost);
            BiomeDef biome = WorldTileInfo.GetBiome(outpost.Tile);
            return Outpost_Baselines.GetAnimalBaselineUnitsPerSkillForProduct(kind, y.MeatCount, biome) * tile;
        }

        /// <summary>Static baseline units/skill at 100% hunting tile (includes biome rarity + danger nuance when outpost set).</summary>
        public static float GetHuntingBaselineUnitsPerSkillForProduct(PawnKindDef kind, ThingDef product, WorldObject_WD_Outpost outpost = null)
        {
            if (kind?.RaceProps == null || product == null) return 0f;
            BiomeDef biome = outpost != null ? WorldTileInfo.GetBiome(outpost.Tile) : null;
            var y = Outpost_Baselines.GetAnimalYieldPerKill(kind);
            if (kind.RaceProps.meatDef == product)
                return Outpost_Baselines.GetAnimalBaselineUnitsPerSkillForProduct(kind, y.MeatCount, biome);
            if (kind.RaceProps.leatherDef == product)
                return Outpost_Baselines.GetAnimalBaselineUnitsPerSkillForProduct(kind, y.LeatherCount, biome);
            ThingDef wool = GetWoolDefFromKind(kind);
            if (wool == product)
                return Outpost_Baselines.GetAnimalBaselineUnitsPerSkillForProduct(kind, y.WoolCount, biome);
            return 0f;
        }

        /// <summary>Build delivery: per product, Round(baselineUnits/skill × tile × Animals skill). Output multiplier applied later.</summary>
        public static List<ThingDefCountClass> BuildHuntingDeliveryItems(PawnKindDef kind, float capacity, WorldObject_WD_Outpost outpost = null)
        {
            var list = new List<ThingDefCountClass>();
            if (kind?.RaceProps == null || capacity <= 0) return list;
            float tileFactor = outpost != null ? Outpost_Production_Utils.GetHuntingTileProductionFactor(outpost) : 1f;
            var y = Outpost_Baselines.GetAnimalYieldPerKill(kind);
            float scale = tileFactor * capacity;
            BiomeDef biome = outpost != null ? WorldTileInfo.GetBiome(outpost.Tile) : null;
            int meatQty = kind.RaceProps.meatDef != null ? Mathf.Max(0, Mathf.RoundToInt(Outpost_Baselines.GetAnimalBaselineUnitsPerSkillForProduct(kind, y.MeatCount, biome) * scale)) : 0;
            int leatherQty = kind.RaceProps.leatherDef != null ? Mathf.Max(0, Mathf.RoundToInt(Outpost_Baselines.GetAnimalBaselineUnitsPerSkillForProduct(kind, y.LeatherCount, biome) * scale)) : 0;
            int woolQty = GetWoolDefFromKind(kind) != null ? Mathf.Max(0, Mathf.RoundToInt(Outpost_Baselines.GetAnimalBaselineUnitsPerSkillForProduct(kind, y.WoolCount, biome) * scale)) : 0;
            if (meatQty > 0 && kind.RaceProps.meatDef != null)
                list.Add(new ThingDefCountClass(kind.RaceProps.meatDef, meatQty));
            if (leatherQty > 0 && kind.RaceProps.leatherDef != null)
                list.Add(new ThingDefCountClass(kind.RaceProps.leatherDef, leatherQty));
            ThingDef woolDef = GetWoolDefFromKind(kind);
            if (woolQty > 0 && woolDef != null)
                list.Add(new ThingDefCountClass(woolDef, woolQty));
            return list;
        }

        /// <summary>One line per product: baseline units per Animals skill after tile efficiency (meat, leather, wool when applicable).</summary>
        public static string GetHuntingPerSkillAtTileSummary(PawnKindDef kind, float tileFactor, BiomeDef biome)
        {
            if (kind?.RaceProps == null) return "";
            var yk = Outpost_Baselines.GetAnimalYieldPerKill(kind);
            var lines = new List<string>();
            void AddLine(ThingDef product, int unitsPerKill)
            {
                if (product == null || unitsPerKill <= 0) return;
                float b = Outpost_Baselines.GetAnimalBaselineUnitsPerSkillForProduct(kind, unitsPerKill, biome) * tileFactor;
                string perKey = "TSA_WD_Production_TooltipHunting_PerProduct";
                string s = perKey.Translate(b.ToString("F1"), product.LabelCap).ToString();
                if (s == perKey)
                    s = "~" + b.ToString("F1") + " " + product.LabelCap + " per skill at this tile";
                lines.Add(s);
            }

            AddLine(kind.RaceProps.meatDef, yk.MeatCount);
            AddLine(kind.RaceProps.leatherDef, yk.LeatherCount);
            ThingDef woolDef = GetWoolDefFromKind(kind);
            AddLine(woolDef, yk.WoolCount);
            return lines.Count > 0 ? string.Join("\n", lines) : "TSA_WD_Text_EmDash".Translate().ToString();
        }

        /// <summary>Tooltip when clicking the animal name: bundle + per-tile baselines.</summary>
        public static string GetHuntingAnimalRowTooltip(PawnKindDef kind, WorldObject_WD_Outpost outpost)
        {
            if (kind == null) return "";
            float tileFactor = outpost != null ? Outpost_Production_Utils.GetHuntingTileProductionFactor(outpost) : 1f;
            BiomeDef biome = outpost != null ? WorldTileInfo.GetBiome(outpost.Tile) : null;
            var products = new List<string>();
            if (kind.RaceProps?.meatDef != null) products.Add(kind.RaceProps.meatDef.LabelCap);
            if (kind.RaceProps?.leatherDef != null) products.Add(kind.RaceProps.leatherDef.LabelCap);
            ThingDef wool = GetWoolDefFromKind(kind);
            if (wool != null) products.Add(wool.LabelCap);
            string productList = products.Count > 0 ? string.Join(", ", products) : kind.LabelCap;
            string summary = GetHuntingPerSkillAtTileSummary(kind, tileFactor, biome);
            string rowKey = "TSA_WD_Hunting_RowBundleTooltip";
            string t = rowKey.Translate(kind.LabelCap, productList, summary).ToString();
            if (t == rowKey)
                t = kind.LabelCap + " — each delivery includes: " + productList + ".\n\n" + summary;
            return t;
        }

        /// <summary>Tooltip for hunting: baseline, tile factor, effective, delivery summary.</summary>
        public static string GetProductionTooltip(WorldObject_WD_Outpost outpost, PawnKindDef kind)
        {
            if (outpost == null || kind == null) return "";
            float refSilver = Outpost_Baselines.GetReferenceSilverPerSkillPerCycle();
            float tileFactor = Outpost_Production_Utils.GetHuntingTileProductionFactor(outpost);
            BiomeDef biome = WorldTileInfo.GetBiome(outpost.Tile);
            string perSkillBlock = GetHuntingPerSkillAtTileSummary(kind, tileFactor, biome);
            float capacity = outpost.GetCapacityForYieldPreview();
            var products = new List<string>();
            if (kind.RaceProps?.meatDef != null) products.Add(kind.RaceProps.meatDef.LabelCap);
            if (kind.RaceProps?.leatherDef != null) products.Add(kind.RaceProps.leatherDef.LabelCap);
            ThingDef wool = GetWoolDefFromKind(kind);
            if (wool != null) products.Add(wool.LabelCap);
            string productsStr = products.Count > 0 ? string.Join(", ", products) : kind.RaceProps?.meatDef?.LabelCap ?? "TSA_WD_Text_EmDash".Translate().ToString();
            var items = BuildHuntingDeliveryItems(kind, capacity, outpost) ?? new List<ThingDefCountClass>();
            var displayItems = new List<ThingDefCountClass>();
            foreach (var tc in items)
                if (tc?.thingDef != null) displayItems.Add(new ThingDefCountClass(tc.thingDef, tc.count));
            Outpost_Production_Utils.ApplyOutputMultiplierToDeliveryItems(displayItems);
            var deliveryParts = new List<string>();
            foreach (var tc in displayItems)
                if (tc?.thingDef != null) deliveryParts.Add(tc.count + " " + tc.thingDef.LabelCap);
            string deliveryStr = deliveryParts.Count > 0 ? string.Join(" + ", deliveryParts) : "TSA_WD_Text_EmDash".Translate().ToString();
            string huntTipKey = "TSA_WD_Production_TooltipHunting";
            string t = huntTipKey.Translate(refSilver.ToString("F0"), tileFactor.ToString("F2"), perSkillBlock, productsStr, deliveryStr).ToString();
            if (t == huntTipKey)
                t = "Budget " + refSilver.ToString("F0") + " silver per Animals skill per delivery. Tile hunting: " + tileFactor.ToString("F2") + ".\n" + perSkillBlock + "\nProducts: " + productsStr + ".\nThis delivery: " + deliveryStr;
            return t;
        }

        /// <summary>Multi-product baseline tooltip at 100% tile; uses biome nuance when outpost given.</summary>
        public static string GetAnimalBaselineTooltip(PawnKindDef kind, WorldObject_WD_Outpost outpost = null)
        {
            if (kind?.RaceProps == null) return "";
            float refSilver = Outpost_Baselines.GetReferenceSilverPerSkillPerCycle();
            float vpk = Outpost_Baselines.GetAnimalValuePerKill(kind);
            BiomeDef biome = outpost != null ? WorldTileInfo.GetBiome(outpost.Tile) : null;
            string lines100 = GetHuntingPerSkillAtTileSummary(kind, 1f, biome);
            string introKey = "TSA_WD_Hunting_AnimalBaseline_Intro";
            string intro = introKey.Translate().ToString();
            if (intro == introKey)
                intro = "Per skill at 100% tile efficiency:";
            string block = intro + "\n" + lines100;
            string baseKey = "TSA_WD_Hunting_AnimalBaselineTooltip";
            string t = baseKey.Translate(refSilver.ToString("F0"), vpk.ToString("F0"), block).ToString();
            if (t == baseKey)
                t = "Budget " + refSilver.ToString("F0") + " silver per Animals skill per delivery. One kill ≈ " + vpk.ToString("F0") + " silver (market value of meat, leather, wool).\n" + block + "\nDelivery = baseline × tile × assigned Animals skill × output multiplier.";
            return t;
        }

        /// <summary>Tooltip for hunting: biome rank + mutator breakdown (matches column %).</summary>
        public static string GetHuntingEfficiencyTooltip(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return "";
            float b = outpost.GetBuiltUpgradeTileAnimalAbundanceBonus();
            string lines = WorldTileProductivity.BuildOutpostUpgradeProductivityLines(outpost, d => d.tileAnimalAbundanceBonus);
            return WorldTileProductivity.GetHuntingScoreTooltipText(outpost.Tile, b, lines);
        }

        /// <summary>Summary line for hunting.</summary>
        public static string FormatHuntingSummaryLine(WorldObject_WD_Outpost outpost)
        {
            if (outpost?.SelectedPawnKindForHunting == null) return null;
            PawnKindDef kind = outpost.SelectedPawnKindForHunting;
            float cycleDays = Outpost_Production_Utils.GetProductionCycleDays(outpost);
            var items = Outpost_Production.GetCurrentDeliveryItems(outpost);
            var parts = new List<string>();
            foreach (var tc in items ?? new List<ThingDefCountClass>())
                if (tc?.thingDef != null) parts.Add("x" + tc.count + " " + tc.thingDef.LabelCap);
            string yieldStr = parts.Count > 0 ? string.Join("TSA_WD_Text_ListJoinAnd".Translate().ToString(), parts) : "TSA_WD_Text_Nothing".Translate().ToString();
            string cycleStr = cycleDays.ToString("F0");
            return "TSA_WD_Prod_HuntingSummary".Translate(kind.LabelCap, yieldStr, cycleStr).ToString();
        }

        public static string GetHuntingProductFormulaTooltip(int count, string productLabel, float baselineDisplay, int efficiencyPct, float animalSkill)
        {
            return "TSA_WD_Hunting_ProductFormulaTooltip".Translate(
                count,
                productLabel,
                baselineDisplay.ToString("F1"),
                "TSA_WD_Hunting_FormulaLabel_BaselinePerKill".Translate(),
                efficiencyPct,
                "TSA_WD_Hunting_FormulaLabel_TileEfficiency".Translate(),
                animalSkill.ToString("F0"),
                "TSA_WD_Hunting_FormulaLabel_OutpostAnimals".Translate()).ToString();
        }
    }
}
