using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace TSA_WorldDomination
{
    public class WdWeightedThingOption
    {
        public ThingDef thingDef;
        public float weight = 1f;
    }

    public class WdWeightedPawnKindOption
    {
        public PawnKindDef pawnKindDef;
        public float weight = 1f;
    }

    public class WdWeightedPlantOption
    {
        public ThingDef plantDef;
        public float weight = 1f;
    }

    /// <summary>Multi-biome crop override/fallback table. Must include a Default row.</summary>
    public class WdBiomeCropTableDef : Def
    {
        /// <summary>
        /// Share of Farming settlement shelf valueBudget spent on harvest products of crops
        /// verified spawned during layout gen. Remainder uses Farming loot table.
        /// </summary>
        public float shelfCropBudgetFraction = 0.5f;

        public List<string> biomes = new List<string>();
        public List<WdWeightedPlantOption> plants = new List<WdWeightedPlantOption>();

        public bool IsDefault => biomes != null && biomes.Any(b => b == "Default");
        public bool MatchesBiome(BiomeDef biome)
        {
            if (biome == null || biomes == null) return false;
            return biomes.Contains(biome.defName);
        }
    }

    /// <summary>Multi-biome pen livestock table. Must include a Default row.</summary>
    public class WdBiomePenLivestockTableDef : Def
    {
        public List<string> biomes = new List<string>();
        public List<WdWeightedPawnKindOption> animals = new List<WdWeightedPawnKindOption>();

        public bool IsDefault => biomes != null && biomes.Any(b => b == "Default");
        public bool MatchesBiome(BiomeDef biome)
        {
            if (biome == null || biomes == null) return false;
            return biomes.Contains(biome.defName);
        }
    }

    /// <summary>Multi-biome mining ore pools. Must include a Default row.</summary>
    public class WdMiningOrePoolDef : Def
    {
        /// <summary>
        /// Share of Mining settlement shelf valueBudget spent on mined products from verified spawned
        /// mineables (classified by cheapOres/expensiveOres lists). Remainder uses Mining loot table.
        /// </summary>
        public float shelfOreBudgetFraction = 0.5f;

        public List<string> biomes = new List<string>();
        public List<WdWeightedThingOption> cheapOres = new List<WdWeightedThingOption>();
        public List<WdWeightedThingOption> expensiveOres = new List<WdWeightedThingOption>();

        public bool IsDefault => biomes != null && biomes.Any(b => b == "Default");
        public bool MatchesBiome(BiomeDef biome)
        {
            if (biome == null || biomes == null) return false;
            return biomes.Contains(biome.defName);
        }
    }

    /// <summary>Settlement shelf loot by type × tier. Must include a Default row.</summary>
    public class WdSettlementLootTableDef : Def
    {
        public List<string> settlementTypes = new List<string>();
        public List<string> tiers = new List<string>();
        public float valueBudget = 1000f;
        public List<WdWeightedThingOption> items = new List<WdWeightedThingOption>();

        public bool IsDefaultType => settlementTypes != null && settlementTypes.Any(t => t == "Default");
        public bool IsDefaultTier => tiers != null && tiers.Any(t => t == "Default");
        public bool MatchesType(string type) =>
            settlementTypes != null && (settlementTypes.Contains(type) || IsDefaultType);
        public bool MatchesTier(string tier) =>
            tiers != null && (tiers.Contains(tier) || IsDefaultTier);
    }

    /// <summary>WD world-road tier. Research + min Construction are XML-authored; economy metrics are settings-overridable defaults.</summary>
    public class WdRoadTierDef : Def
    {
        public string tier;
        public RoadDef roadDef;
        public float movementCostMultiplier = 0.5f;
        public float workPerSegment = 250f;
        public float expeditionStrengthCost = 50f;
        public float winterPenaltyReduction = 0.15f;
        public int minCumulativeConstructionSkill = 5;
        public List<ResearchProjectDef> researchPrerequisites = new List<ResearchProjectDef>();
    }

    public class WdMgNestSpawnEntry
    {
        public string settlementLayout;
        public int countMin;
        public int countMax;
    }

    /// <summary>Post-gen MG nest counts by KCSG SettlementLayoutDef defName.</summary>
    public class WdMgNestSpawnTableDef : Def
    {
        public List<WdMgNestSpawnEntry> entries = new List<WdMgNestSpawnEntry>();

        public bool TryGetCounts(string settlementLayoutDefName, out int countMin, out int countMax)
        {
            countMin = 0;
            countMax = 0;
            if (entries == null || string.IsNullOrEmpty(settlementLayoutDefName)) return false;

            for (int i = 0; i < entries.Count; i++)
            {
                WdMgNestSpawnEntry e = entries[i];
                if (e == null || e.settlementLayout != settlementLayoutDefName) continue;
                countMin = e.countMin;
                countMax = e.countMax;
                return true;
            }
            return false;
        }
    }
}
