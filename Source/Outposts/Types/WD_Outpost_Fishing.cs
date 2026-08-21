using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>One fishing option: fish ThingDef + commonality weight from biome saltwater pool.</summary>
    public struct FishingFishOption
    {
        public ThingDef Fish;
        public float ChanceWeight;
        public bool IsUncommon;
    }

    /// <summary>Fishing outpost production: saltwater fish options, delivery, skill gates, tooltips.</summary>
    public static class Outpost_Fishing
    {
        public const int MinAnimalsSkillUncommon = 8;
        public const int MinAnimalsSkillHighValue = 10;
        public const float HighValueMarketThreshold = 30f;

        /// <summary>Saltwater fish for a fishing outpost's tile.</summary>
        public static List<FishingFishOption> GetFishingFishOptions(WorldObject_WD_Outpost outpost)
        {
            if (outpost?.def == null || !Outpost_Production_Utils.IsFishingOutpost(outpost.def))
                return new List<FishingFishOption>();
            return GetFishingFishOptionsForTile(outpost.Tile);
        }

        /// <summary>Saltwater common + uncommon fish for a coastal tile biome. Empty if not coastal, water, or Odyssey fishTypes missing.</summary>
        public static List<FishingFishOption> GetFishingFishOptionsForTile(int tile)
        {
            var list = new List<FishingFishOption>();
            var grid = Find.WorldGrid;
            if (grid == null || tile < 0 || tile >= grid.TilesCount) return list;
            Tile tileInfo = grid[tile];
            if (tileInfo.WaterCovered || !tileInfo.IsCoastal) return list;

            BiomeDef biome = WorldTileInfo.GetBiome(tile);
            BiomeFishTypes types = biome?.fishTypes;
            if (types == null) return list;

            AddFishChances(list, types.saltwater_Common, uncommon: false);
            AddFishChances(list, types.saltwater_Uncommon, uncommon: true);

            list.Sort((a, b) =>
            {
                int c = b.ChanceWeight.CompareTo(a.ChanceWeight);
                if (c != 0) return c;
                return string.CompareOrdinal(a.Fish?.label, b.Fish?.label);
            });
            return list;
        }

        private static void AddFishChances(List<FishingFishOption> list, List<FishChance> chances, bool uncommon)
        {
            if (chances == null) return;
            for (int i = 0; i < chances.Count; i++)
            {
                FishChance fc = chances[i];
                if (fc?.fishDef == null) continue;
                if (list.Any(o => o.Fish == fc.fishDef)) continue;
                list.Add(new FishingFishOption
                {
                    Fish = fc.fishDef,
                    ChanceWeight = Mathf.Max(0.01f, fc.chance),
                    IsUncommon = uncommon
                });
            }
        }

        public static bool HasAnySaltwaterFish(int tile)
        {
            BiomeDef biome = WorldTileInfo.GetBiome(tile);
            BiomeFishTypes types = biome?.fishTypes;
            if (types == null) return false;
            if (types.saltwater_Common != null)
            {
                for (int i = 0; i < types.saltwater_Common.Count; i++)
                    if (types.saltwater_Common[i]?.fishDef != null) return true;
            }
            if (types.saltwater_Uncommon != null)
            {
                for (int i = 0; i < types.saltwater_Uncommon.Count; i++)
                    if (types.saltwater_Uncommon[i]?.fishDef != null) return true;
            }
            return false;
        }

        public static int GetMinAnimalsSkillForFish(ThingDef fish, WorldObject_WD_Outpost outpost)
        {
            if (fish == null) return 0;
            bool uncommon = false;
            if (outpost != null)
            {
                foreach (FishingFishOption opt in GetFishingFishOptionsForTile(outpost.Tile))
                {
                    if (opt.Fish != fish) continue;
                    uncommon = opt.IsUncommon;
                    break;
                }
            }
            float mv = fish.BaseMarketValue;
            if (mv >= HighValueMarketThreshold) return MinAnimalsSkillHighValue;
            if (uncommon) return MinAnimalsSkillUncommon;
            return 0;
        }

        /// <summary>Whether at least one pawn has Animals skill >= min required for this fish (individual, not cumulative).</summary>
        public static bool OutpostCanFish(WorldObject_WD_Outpost outpost, ThingDef fish)
        {
            if (outpost?.VirtualPawns == null || fish == null) return false;
            int required = GetMinAnimalsSkillForFish(fish, outpost);
            if (required <= 0) return true;
            int maxAnimals = 0;
            var vpAnimals = outpost.VirtualPawns;
            for (int i = 0; i < vpAnimals.Count; i++)
            {
                if (vpAnimals[i].animals > maxAnimals) maxAnimals = vpAnimals[i].animals;
            }
            return maxAnimals >= required;
        }

        /// <summary>Units of this fish per Animals skill at 100% fishing tile (silver budget ÷ market value × rarity nuance).</summary>
        public static float GetFishBaselineUnitsPerSkill(ThingDef fish, WorldObject_WD_Outpost outpost = null)
        {
            if (fish == null) return 0f;
            float vpk = Mathf.Max(0.01f, fish.BaseMarketValue);
            float core = Outpost_Baselines.GetReferenceSilverPerSkillPerCycle() / vpk;
            return core * GetFishingRarityNuance(fish, outpost);
        }

        /// <summary>Rarer saltwater fish (lower chance weight) yield slightly less; clamp 0.75–1.25.</summary>
        public static float GetFishingRarityNuance(ThingDef fish, WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return 1f;
            return GetFishingRarityNuanceForOptions(fish, GetFishingFishOptions(outpost));
        }

        /// <summary>Rarity nuance from a coastal tile's saltwater pool (no outpost required).</summary>
        public static float GetFishingRarityNuanceForTile(ThingDef fish, int tile)
            => GetFishingRarityNuanceForOptions(fish, GetFishingFishOptionsForTile(tile));

        private static float GetFishingRarityNuanceForOptions(ThingDef fish, List<FishingFishOption> opts)
        {
            if (fish == null || opts == null || opts.Count == 0) return 1f;
            float wMin = float.MaxValue;
            float wMax = 0f;
            float mine = 0f;
            bool found = false;
            for (int i = 0; i < opts.Count; i++)
            {
                float w = opts[i].ChanceWeight;
                if (w <= 0f) continue;
                if (w < wMin) wMin = w;
                if (w > wMax) wMax = w;
                if (opts[i].Fish == fish)
                {
                    mine = w;
                    found = true;
                }
            }
            if (!found || wMax <= 0f || wMin >= float.MaxValue) return 1f;
            float t = wMin >= wMax ? 0.5f : Mathf.InverseLerp(wMin, wMax, Mathf.Clamp(mine, wMin, wMax));
            return Mathf.Lerp(0.75f, 1.25f, t);
        }

        /// <summary>Fish units per 1 Animals skill at this tile factor (baseline × rarity × tile stocks).</summary>
        public static float GetFishPerSkillAtTile(ThingDef fish, int tile, float tileFactor)
        {
            if (fish == null) return 0f;
            float vpk = Mathf.Max(0.01f, fish.BaseMarketValue);
            float core = Outpost_Baselines.GetReferenceSilverPerSkillPerCycle() / vpk;
            return core * GetFishingRarityNuanceForTile(fish, tile) * tileFactor;
        }

        /// <summary>One-line yield preview: ~N Fish per skill at this tile.</summary>
        public static string GetFishPerSkillAtTileSummary(ThingDef fish, int tile, float tileFactor)
        {
            if (fish == null) return "";
            float b = GetFishPerSkillAtTile(fish, tile, tileFactor);
            string s = "TSA_WD_Production_TooltipHunting_PerProduct".Translate(b.ToString("F1"), fish.LabelCap).ToString();
            if (s.Contains("TSA_WD_"))
                s = "~" + b.ToString("F1") + " " + fish.LabelCap + " per skill at this tile";
            return s;
        }

        public static List<ThingDefCountClass> BuildFishingDeliveryItems(ThingDef fish, float capacity, WorldObject_WD_Outpost outpost = null)
        {
            var list = new List<ThingDefCountClass>();
            if (fish == null || capacity <= 0f) return list;
            float tileFactor = outpost != null ? Outpost_Production_Utils.GetFishingTileProductionFactor(outpost) : 1f;
            float baseline = GetFishBaselineUnitsPerSkill(fish, outpost);
            int qty = Mathf.Max(0, Mathf.RoundToInt(baseline * tileFactor * capacity));
            if (qty > 0)
                list.Add(new ThingDefCountClass(fish, qty));
            return list;
        }

        public static float GetOutputPerSkillPoint(WorldObject_WD_Outpost outpost)
        {
            if (outpost?.def == null || !Outpost_Production_Utils.IsFishingOutpost(outpost.def)) return 0f;
            ThingDef fish = outpost.SelectedFishDef;
            if (fish == null) return 0f;
            float tile = Outpost_Production_Utils.GetFishingTileProductionFactor(outpost);
            return GetFishBaselineUnitsPerSkill(fish, outpost) * tile;
        }

        public static string GetFishingFishRowTooltip(ThingDef fish, WorldObject_WD_Outpost outpost)
        {
            if (fish == null) return "";
            float tileFactor = outpost != null ? Outpost_Production_Utils.GetFishingTileProductionFactor(outpost) : 1f;
            float b = GetFishBaselineUnitsPerSkill(fish, outpost) * tileFactor;
            return "TSA_WD_Fishing_RowTooltip".Translate(fish.LabelCap, b.ToString("F1")).ToString();
        }

        public static string GetProductionTooltip(WorldObject_WD_Outpost outpost, ThingDef fish)
        {
            if (outpost == null || fish == null) return "";
            float refSilver = Outpost_Baselines.GetReferenceSilverPerSkillPerCycle();
            float tileFactor = Outpost_Production_Utils.GetFishingTileProductionFactor(outpost);
            float capacity = outpost.GetCapacityForYieldPreview();
            var items = BuildFishingDeliveryItems(fish, capacity, outpost) ?? new List<ThingDefCountClass>();
            var displayItems = new List<ThingDefCountClass>();
            foreach (var tc in items)
                if (tc?.thingDef != null) displayItems.Add(new ThingDefCountClass(tc.thingDef, tc.count));
            Outpost_Production_Utils.ApplyOutputMultiplierToDeliveryItems(displayItems);
            string deliveryStr = displayItems.Count > 0
                ? displayItems[0].count + " " + displayItems[0].thingDef.LabelCap
                : "TSA_WD_Text_EmDash".Translate().ToString();
            float perSkill = GetFishBaselineUnitsPerSkill(fish, outpost) * tileFactor;
            return "TSA_WD_Production_TooltipFishing".Translate(
                refSilver.ToString("F0"),
                tileFactor.ToString("F2"),
                perSkill.ToString("F1"),
                fish.LabelCap,
                deliveryStr).ToString();
        }

        public static string GetFishingEfficiencyTooltip(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return "";
            float b = outpost.GetBuiltUpgradeTileFishAbundanceBonus();
            string lines = WorldTileProductivity.BuildOutpostUpgradeProductivityLines(outpost, d => d.tileFishAbundanceBonus);
            return WorldTileProductivity.GetFishingScoreTooltipText(outpost.Tile, b, lines);
        }

        public static string FormatFishingSummaryLine(WorldObject_WD_Outpost outpost)
        {
            if (outpost?.SelectedFishDef == null) return null;
            ThingDef fish = outpost.SelectedFishDef;
            float cycleDays = Outpost_Production_Utils.GetProductionCycleDays(outpost);
            var items = Outpost_Production.GetCurrentDeliveryItems(outpost);
            var parts = new List<string>();
            foreach (var tc in items ?? new List<ThingDefCountClass>())
                if (tc?.thingDef != null) parts.Add("x" + tc.count + " " + tc.thingDef.LabelCap);
            string yieldStr = parts.Count > 0 ? string.Join("TSA_WD_Text_ListJoinAnd".Translate().ToString(), parts) : "TSA_WD_Text_Nothing".Translate().ToString();
            return "TSA_WD_Prod_FishingSummary".Translate(fish.LabelCap, yieldStr, cycleDays.ToString("F0")).ToString();
        }
    }
}
