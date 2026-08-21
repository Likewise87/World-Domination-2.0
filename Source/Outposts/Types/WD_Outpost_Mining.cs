using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Mining outpost production: options (stone chunks + ores including Uranium/Jade), output per skill, delivery items, tooltips.</summary>
    public static class Outpost_Mining
    {
        /// <summary>Mining options from WorldTileInfo.GetMiningProductsForTile (tile stone/blocks + scatter mineable ores). Empty list falls back to Silver/Gold/Plasteel with dev log.</summary>
        public static List<ThingDef> GetProducibleOptions(WorldObject_WD_Outpost outpost)
        {
            var list = new List<ThingDef>();
            if (outpost?.def == null || !Outpost_Production_Utils.IsMiningOutpost(outpost.def)) return list;
            var tileProducts = WorldTileInfo.GetMiningProductsForTile(outpost.Tile);
            foreach (var t in tileProducts)
                if (t != null && !list.Contains(t)) list.Add(t);
            if (list.Count == 0)
            {
                Log.Warning(
                    $"{MiningScatterDiscovery.DevLogPrefix} GetProducibleOptions: no mining products for outpost tile; falling back to Silver, Gold, Plasteel.");
                list.Add(ThingDefOf.Silver);
                list.Add(ThingDefOf.Gold);
                list.Add(ThingDefOf.Plasteel);
            }
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i] == null) list.RemoveAt(i);
            }
            list.Sort((a, b) => string.Compare(a.defName ?? "", b.defName ?? "", System.StringComparison.Ordinal));
            return list;
        }

        /// <summary>Output per skill point for a mined product: mining baseline × mining tile factor.</summary>
        public static float GetOutputPerSkillPoint(WorldObject_WD_Outpost outpost, ThingDef product)
        {
            if (outpost?.def == null || product == null) return 0f;
            if (!Outpost_Production_Utils.IsMiningOutpost(outpost.def)) return 0f;
            float baseline = Outpost_Baselines.GetMiningBaselinePerSkill(product);
            float tileFactor = Outpost_Production_Utils.GetMiningTileProductionFactor(outpost);
            return baseline * tileFactor;
        }

        /// <summary>Delivery items for mining: single selected product, qty = TotalMiningSkill × output per skill. When overrideCapacity is set (spawn path), uses that value. Baselines (incl. stone) come from settings/XML.</summary>
        public static List<ThingDefCountClass> GetDeliveryItems(WorldObject_WD_Outpost outpost, ThingDef producing, float? overrideCapacity = null)
        {
            if (outpost == null || producing == null) return null;
            float capacity = overrideCapacity ?? outpost.TotalMiningSkill();
            float outputPer = GetOutputPerSkillPoint(outpost, producing);
            int qty = Mathf.Max(0, Mathf.RoundToInt(capacity * outputPer));
            if (qty <= 0) return null;
            return new List<ThingDefCountClass> { new ThingDefCountClass(producing, qty) };
        }

        /// <summary>Tooltip for mining: baseline, tile factor, effective, total.</summary>
        public static string GetProductionTooltip(WorldObject_WD_Outpost outpost, ThingDef ore)
        {
            if (outpost == null || ore == null) return "";
            float miningBaseline = Outpost_Baselines.GetMiningBaselinePerSkill(ore);
            float tileFactor = Outpost_Production_Utils.GetMiningTileProductionFactor(outpost);
            float effective = GetOutputPerSkillPoint(outpost, ore);
            float capacity = outpost.GetCapacityForYieldPreview();
            int totalItems = Outpost_Production_Utils.ScaleOutputStackCount(Mathf.Max(0, Mathf.RoundToInt(capacity * effective)));
            string key = "TSA_WD_Production_TooltipMining";
            string t = key.Translate(miningBaseline.ToString("F1"), tileFactor.ToString("F2"), effective.ToString("F2"), capacity.ToString("F0"), SkillDefOf.Mining.label, totalItems.ToString(), ore.LabelCap).ToString();
            if (t == key || t.Contains("TSA_WD_")) t = "Base output: " + miningBaseline.ToString("F1") + " per skill (from ore).\nMining efficiency uses hilliness baseline on the tile plus world tile mutators (ore-rich, junkyard, VEE mining modifiers, etc.), capped at " + WorldTileProductivity.ProductivityScoreCap.ToString("F1") + ".\nEffective: " + effective.ToString("F2") + " per skill.\nTotal (incl. global output multiplier): " + capacity.ToString("F0") + " Mining Skill × " + effective.ToString("F2") + " → " + totalItems + " " + ore.LabelCap;
            return t;
        }

        /// <summary>Summary line for mining.</summary>
        public static string FormatMiningSummaryLine(WorldObject_WD_Outpost outpost)
        {
            if (outpost?.SelectedProductionDef == null) return null;
            ThingDef ore = outpost.SelectedProductionDef;
            float cycleDays = Outpost_Production_Utils.GetProductionCycleDays(outpost);
            var items = Outpost_Production.GetCurrentDeliveryItems(outpost);
            string cycleStr = cycleDays.ToString("F0");
            if (items == null || items.Count == 0)
                return "TSA_WD_Prod_MiningSummary_None".Translate(ore.LabelCap, cycleStr).ToString();
            var parts = new List<string>();
            foreach (var tc in items)
                if (tc?.thingDef != null) parts.Add("x" + tc.count + " " + tc.thingDef.LabelCap);
            string yieldStr = string.Join(" and ", parts);
            return "TSA_WD_Prod_MiningSummary".Translate(ore.LabelCap, yieldStr, cycleStr).ToString();
        }

        /// <summary>Tooltip for mining tile factor: hilliness baseline plus mutators (same breakdown as farming/hunting).</summary>
        public static string GetMiningEfficiencyTooltip(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return "";
            float b = outpost.GetBuiltUpgradeTileMiningBonus();
            string lines = WorldTileProductivity.BuildOutpostUpgradeProductivityLines(outpost, d => d.tileMiningBonus);
            return WorldTileProductivity.GetMiningEfficiencyTooltipText(outpost.Tile, b, lines);
        }

        /// <summary>Tooltip for mining baseline: how much item can be bought for silver from settings (mining has hard coded modifications).</summary>
        /// <summary>Mining baseline copy: mod settings (per-ore sliders); same text for every row.</summary>
        public static string GetMiningBaselineTooltip(ThingDef _)
        {
            return "TSA_WD_Production_BaselineTooltip_Mining".Translate().ToString();
        }

        /// <summary>Vanilla-like color for stone chunks and stone blocks in the production list. Returns null if not a stone type (use default label color).</summary>
        public static Color? GetChunkColor(ThingDef def)
        {
            if (def == null) return null;
            string n = def.defName ?? "";
            if (n.StartsWith("Chunk") || n.StartsWith("Blocks"))
            {
                if (n.Contains("Granite")) return new Color(0.55f, 0.55f, 0.52f);
                if (n.Contains("Marble")) return new Color(0.92f, 0.9f, 0.85f);
                if (n.Contains("Sandstone")) return new Color(0.76f, 0.69f, 0.5f);
                if (n.Contains("Limestone")) return new Color(0.72f, 0.7f, 0.65f);
                if (n.Contains("Slate")) return new Color(0.45f, 0.48f, 0.52f);
            }
            var ore = Outpost_Baselines.GetOreDefForMinedProduct(def);
            if (ore?.building?.isNaturalRock == true)
            {
                if (n.Contains("Granite")) return new Color(0.55f, 0.55f, 0.52f);
                if (n.Contains("Marble")) return new Color(0.92f, 0.9f, 0.85f);
                if (n.Contains("Sandstone")) return new Color(0.76f, 0.69f, 0.5f);
                if (n.Contains("Limestone")) return new Color(0.72f, 0.7f, 0.65f);
                if (n.Contains("Slate")) return new Color(0.45f, 0.48f, 0.52f);
            }
            return null;
        }
    }
}
