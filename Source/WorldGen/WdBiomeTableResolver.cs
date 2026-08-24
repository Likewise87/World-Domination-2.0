using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace TSA_WorldDomination
{
    public static class WdBiomeTableResolver
    {
        public static WdBiomeCropTableDef ResolveCropTable(BiomeDef biome)
        {
            var all = DefDatabase<WdBiomeCropTableDef>.AllDefsListForReading;
            if (all == null || all.Count == 0) return null;
            WdBiomeCropTableDef match = all.FirstOrDefault(d => d != null && !d.IsDefault && d.MatchesBiome(biome));
            if (match != null) return match;
            WdBiomeCropTableDef fallback = all.FirstOrDefault(d => d != null && d.IsDefault);
            if (fallback == null)
                Log.Warning("[WorldDomination] No Default WdBiomeCropTableDef found.");
            return fallback;
        }

        public static WdBiomePenLivestockTableDef ResolvePenLivestockTable(BiomeDef biome)
        {
            var all = DefDatabase<WdBiomePenLivestockTableDef>.AllDefsListForReading;
            if (all == null || all.Count == 0) return null;
            WdBiomePenLivestockTableDef match = all.FirstOrDefault(d => d != null && !d.IsDefault && d.MatchesBiome(biome));
            if (match != null) return match;
            WdBiomePenLivestockTableDef fallback = all.FirstOrDefault(d => d != null && d.IsDefault);
            if (fallback == null)
                Log.Warning("[WorldDomination] No Default WdBiomePenLivestockTableDef found.");
            return fallback;
        }

        public static WdMiningOrePoolDef ResolveMiningOrePool(BiomeDef biome)
        {
            var all = DefDatabase<WdMiningOrePoolDef>.AllDefsListForReading;
            if (all == null || all.Count == 0) return null;
            WdMiningOrePoolDef match = all.FirstOrDefault(d => d != null && !d.IsDefault && d.MatchesBiome(biome));
            if (match != null) return match;
            WdMiningOrePoolDef fallback = all.FirstOrDefault(d => d != null && d.IsDefault);
            if (fallback == null)
                Log.Warning("[WorldDomination] No Default WdMiningOrePoolDef found.");
            return fallback;
        }

        public static WdSettlementLootTableDef ResolveLootTable(string settlementType, string tier)
        {
            var all = DefDatabase<WdSettlementLootTableDef>.AllDefsListForReading;
            if (all == null || all.Count == 0) return null;

            WdSettlementLootTableDef exact = all.FirstOrDefault(d =>
                d != null && !d.IsDefaultType && !d.IsDefaultTier
                && d.settlementTypes != null && d.settlementTypes.Contains(settlementType)
                && d.tiers != null && d.tiers.Contains(tier));
            if (exact != null) return exact;

            WdSettlementLootTableDef typeDefaultTier = all.FirstOrDefault(d =>
                d != null && !d.IsDefaultType && d.IsDefaultTier
                && d.settlementTypes != null && d.settlementTypes.Contains(settlementType));
            if (typeDefaultTier != null) return typeDefaultTier;

            WdSettlementLootTableDef defaultTypeTier = all.FirstOrDefault(d =>
                d != null && d.IsDefaultType && !d.IsDefaultTier
                && d.tiers != null && d.tiers.Contains(tier));
            if (defaultTypeTier != null) return defaultTypeTier;

            WdSettlementLootTableDef fullDefault = all.FirstOrDefault(d =>
                d != null && d.IsDefaultType && d.IsDefaultTier);
            if (fullDefault == null)
                Log.Warning("[WorldDomination] No Default WdSettlementLootTableDef found.");
            return fullDefault;
        }

        public static ThingDef PickWeightedThing(List<WdWeightedThingOption> options)
        {
            if (options == null || options.Count == 0) return null;
            float total = 0f;
            for (int i = 0; i < options.Count; i++)
            {
                WdWeightedThingOption o = options[i];
                if (o?.thingDef == null || o.weight <= 0f) continue;
                total += o.weight;
            }
            if (total <= 0f) return null;
            float roll = Rand.Range(0f, total);
            float acc = 0f;
            for (int i = 0; i < options.Count; i++)
            {
                WdWeightedThingOption o = options[i];
                if (o?.thingDef == null || o.weight <= 0f) continue;
                acc += o.weight;
                if (roll <= acc) return o.thingDef;
            }
            return options.LastOrDefault(o => o?.thingDef != null)?.thingDef;
        }

        public static ThingDef PickWeightedPlant(List<WdWeightedPlantOption> options)
        {
            if (options == null || options.Count == 0) return null;
            float total = 0f;
            for (int i = 0; i < options.Count; i++)
            {
                WdWeightedPlantOption o = options[i];
                if (o?.plantDef == null || o.weight <= 0f) continue;
                total += o.weight;
            }
            if (total <= 0f) return null;
            float roll = Rand.Range(0f, total);
            float acc = 0f;
            for (int i = 0; i < options.Count; i++)
            {
                WdWeightedPlantOption o = options[i];
                if (o?.plantDef == null || o.weight <= 0f) continue;
                acc += o.weight;
                if (roll <= acc) return o.plantDef;
            }
            return options.LastOrDefault(o => o?.plantDef != null)?.plantDef;
        }

        public static PawnKindDef PickWeightedPawnKind(List<WdWeightedPawnKindOption> options)
        {
            if (options == null || options.Count == 0) return null;
            float total = 0f;
            for (int i = 0; i < options.Count; i++)
            {
                WdWeightedPawnKindOption o = options[i];
                if (o?.pawnKindDef == null || o.weight <= 0f) continue;
                total += o.weight;
            }
            if (total <= 0f) return null;
            float roll = Rand.Range(0f, total);
            float acc = 0f;
            for (int i = 0; i < options.Count; i++)
            {
                WdWeightedPawnKindOption o = options[i];
                if (o?.pawnKindDef == null || o.weight <= 0f) continue;
                acc += o.weight;
                if (roll <= acc) return o.pawnKindDef;
            }
            return options.LastOrDefault(o => o?.pawnKindDef != null)?.pawnKindDef;
        }

        public static WdRoadTierDef GetRoadTierDef(SettlementTier tier)
        {
            string key = tier == SettlementTier.T3 || tier == SettlementTier.T4 ? "T3"
                : tier == SettlementTier.T2 ? "T2" : "T1";
            return DefDatabase<WdRoadTierDef>.AllDefsListForReading
                .FirstOrDefault(d => d != null && d.tier == key);
        }
    }
}
