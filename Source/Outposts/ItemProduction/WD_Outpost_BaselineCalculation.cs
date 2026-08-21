using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Yield from one "kill" of an animal: meat, leather/fur, wool counts. Used for both value-per-kill and delivery quantities.</summary>
    public struct AnimalYieldPerKill
    {
        public int MeatCount;
        public int LeatherCount;
        public int WoolCount;
    }

    /// <summary>
    /// Silver budget per skill per delivery (settings) is fixed—no scaling with outpost cycle length.
    /// Hunting: baseline × biome nuance (wild commonality band + combat power), then × hunting tile factor × Animals skill.
    /// Crops: (budget ÷ harvest value) ÷ harvest-difficulty vs potato (clamped 0.75–1.25), then × farming tile factor.
    /// Mining: budget/value rules + overrides as before.
    /// </summary>
    public static class Outpost_Baselines
    {
        private static Dictionary<ThingDef, ThingDef> harvestToPlantMap;
        private static Dictionary<ThingDef, ThingDef> productToOreMap;
        private static readonly Dictionary<PawnKindDef, int> minAnimalSkillCache = new Dictionary<PawnKindDef, int>();
        private static ThingDef cachedReferencePotatoPlant;
        private static float cachedPotatoHarvestStrain = -1f;

        private static void EnsurePlantOreMaps()
        {
            if (harvestToPlantMap != null) return;
            harvestToPlantMap = new Dictionary<ThingDef, ThingDef>();
            productToOreMap = new Dictionary<ThingDef, ThingDef>();
            foreach (ThingDef t in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (t?.plant?.harvestedThingDef != null)
                    harvestToPlantMap[t.plant.harvestedThingDef] = t;
                if (t?.building?.mineableThing != null)
                    productToOreMap[t.building.mineableThing] = t;
            }
        }

        /// <summary>Reference cycle length in days (e.g. 30 days = one delivery).</summary>
        public const float ReferenceCycleDays = 30f;

        /// <summary>Silver budget per skill point per delivery (mod setting). Not scaled by cycle length.</summary>
        public static float GetReferenceSilverPerSkillPerCycle()
        {
            return WorldDominationMod.settings?.outpostSilverValuePerSkillPerCycle ?? WorldDominationSettings.DefOutpostSilverValuePerSkillPerCycle;
        }

        /// <summary>Tooltip: baseline = spending the silver budget on this product at market rates (per skill).</summary>
        public static string GetBaselineTooltipForProduct(ThingDef product)
        {
            if (product == null) return "";
            return "TSA_WD_Production_BaselineTooltip".Translate(product.LabelCap, GetReferenceSilverPerSkillPerCycle().ToString("F0")).ToString();
        }

        // ---- Crops ----

        /// <summary>Plant ThingDef that produces this harvest (harvestedThingDef). Null if not a crop from a plant.</summary>
        public static ThingDef GetPlantDefForHarvest(ThingDef harvest)
        {
            if (harvest == null) return null;
            EnsurePlantOreMaps();
            return harvestToPlantMap != null && harvestToPlantMap.TryGetValue(harvest, out ThingDef plant) ? plant : null;
        }

        /// <summary>Minimum Plants skill required to grow this crop (from plant.sowMinSkill). Same gating as local map cultivation.</summary>
        public static int GetMinPlantsSkillForCrop(ThingDef harvest)
        {
            ThingDef plant = GetPlantDefForHarvest(harvest);
            if (plant?.plant == null) return 0;
            return plant.plant.sowMinSkill;
        }

        /// <summary>Crop units per Plants skill: (silver budget ÷ harvest value) ÷ harvest-difficulty factor. Difficulty 1 = potato; higher = harder = less yield; clamped 0.75–1.25.</summary>
        public static float GetCropBaselinePerSkill(ThingDef harvest)
        {
            if (harvest == null) return 0f;
            ThingDef plant = GetPlantDefForHarvest(harvest);
            if (plant?.plant == null) return 0f;
            float valuePerUnit = Mathf.Max(0.01f, harvest.BaseMarketValue);
            float silverBaseline = GetReferenceSilverPerSkillPerCycle() / valuePerUnit;
            float difficulty = GetCropHarvestDifficultyFactor(harvest);
            return Mathf.Max(0.1f, silverBaseline / Mathf.Max(0.01f, difficulty));
        }

        /// <summary>Strain from plant defs: grow-days and work per unit of yield plus skill gate (not normalized).</summary>
        private static float GetPlantHarvestStrain(ThingDef plantDef)
        {
            if (plantDef?.plant == null) return 1f;
            PlantProperties p = plantDef.plant;
            float yield = Mathf.Max(0.1f, p.harvestYield);
            float growDays = Mathf.Max(0.1f, p.growDays);
            float work = Mathf.Max(0f, p.sowWork + p.harvestWork);
            int skill = Mathf.Max(0, p.sowMinSkill);
            return growDays / yield + work * 0.01f / yield + skill * 0.08f;
        }

        private static float GetPotatoReferenceStrain()
        {
            if (cachedPotatoHarvestStrain >= 0f) return cachedPotatoHarvestStrain;
            if (cachedReferencePotatoPlant == null)
                cachedReferencePotatoPlant = DefDatabase<ThingDef>.GetNamedSilentFail("Plant_Potato");
            cachedPotatoHarvestStrain = cachedReferencePotatoPlant != null
                ? GetPlantHarvestStrain(cachedReferencePotatoPlant)
                : 1f;
            return cachedPotatoHarvestStrain;
        }

        /// <summary>Relative harvest difficulty vs vanilla potato (1.0). Higher = harder crop = lower yield. Clamped 0.75–1.25. Missing potato def → 1.</summary>
        public static float GetCropHarvestDifficultyFactor(ThingDef harvest)
        {
            ThingDef plant = GetPlantDefForHarvest(harvest);
            if (plant?.plant == null) return 1f;
            float refStrain = GetPotatoReferenceStrain();
            if (refStrain < 1e-4f) return 1f;
            float ratio = GetPlantHarvestStrain(plant) / refStrain;
            return Mathf.Clamp(ratio, 0.75f, 1.25f);
        }

        /// <summary>Whether at least one pawn at the outpost has Plants >= min required for this crop (individual, not cumulative).</summary>
        public static bool OutpostCanProduceCrop(WorldObject_WD_Outpost outpost, ThingDef harvest)
        {
            if (outpost?.VirtualPawns == null || harvest == null) return false;
            int required = GetMinPlantsSkillForCrop(harvest);
            if (required <= 0) return true;
            int maxPlants = 0;
            var vpPlants = outpost.VirtualPawns;
            for (int i = 0; i < vpPlants.Count; i++)
            {
                if (vpPlants[i].plants > maxPlants) maxPlants = vpPlants[i].plants;
            }
            return maxPlants >= required;
        }

        // ---- Animals ----

        /// <summary>Single source of truth: yield (meat, leather/fur, wool) from one kill. Uses game MeatAmount/LeatherAmount stats on the race when available; else body-size formula. Same bundle is used for value-per-kill and for delivery quantities.</summary>
        public static AnimalYieldPerKill GetAnimalYieldPerKill(PawnKindDef kind)
        {
            var y = new AnimalYieldPerKill();
            if (kind?.RaceProps == null) return y;
            float bodySize = Mathf.Max(0.1f, kind.RaceProps.baseBodySize);
            int meat = 0, leather = 0, wool = 0;
            TryGetRaceStat(kind.race, "MeatAmount", 140f, bodySize, out meat);
            TryGetRaceStat(kind.race, "LeatherAmount", 40f, bodySize, out leather);
            if (Outpost_Hunting.GetWoolDefFromKindPublic(kind) != null)
                wool = Mathf.Max(0, Mathf.RoundToInt(10f * bodySize));
            y.MeatCount = kind.RaceProps.meatDef != null ? Mathf.Max(1, meat) : 0;
            y.LeatherCount = kind.RaceProps.leatherDef != null ? Mathf.Max(0, leather) : 0;
            y.WoolCount = Mathf.Max(0, wool);
            return y;
        }

        private static void TryGetRaceStat(ThingDef race, string statDefName, float defaultBase, float bodySize, out int result)
        {
            result = Mathf.Max(0, Mathf.RoundToInt(defaultBase * bodySize));
            if (race == null) return;
            try
            {
                var statDef = DefDatabase<StatDef>.GetNamedSilentFail(statDefName);
                if (statDef == null) return;
                var method = race.GetType().GetMethod("GetStatValueAbstract", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(StatDef) }, null);
                if (method == null) return;
                var val = method.Invoke(race, new object[] { statDef });
                if (val is float f && f > 0f)
                    result = Mathf.Max(0, Mathf.RoundToInt(f));
            }
            catch { /* use body-size fallback */ }
        }

        /// <summary>Market value of one "kill": sum of (yield count × BaseMarketValue) for meat, leather, wool. Uses GetAnimalYieldPerKill so value and delivery stay consistent.</summary>
        public static float GetAnimalValuePerKill(PawnKindDef kind)
        {
            if (kind?.RaceProps == null) return 0f;
            var y = GetAnimalYieldPerKill(kind);
            float value = 0f;
            if (y.MeatCount > 0 && kind.RaceProps.meatDef != null)
                value += y.MeatCount * Mathf.Max(0f, kind.RaceProps.meatDef.BaseMarketValue);
            if (y.LeatherCount > 0 && kind.RaceProps.leatherDef != null)
                value += y.LeatherCount * Mathf.Max(0f, kind.RaceProps.leatherDef.BaseMarketValue);
            ThingDef woolDef = Outpost_Hunting.GetWoolDefFromKindPublic(kind);
            if (y.WoolCount > 0 && woolDef != null)
                value += y.WoolCount * Mathf.Max(0f, woolDef.BaseMarketValue);
            return Mathf.Max(0.01f, value);
        }

        /// <summary>Effective “kills worth” per Animals skill: budget ÷ value per kill × optional biome nuance.</summary>
        public static float GetAnimalBaselineYield(PawnKindDef kind, BiomeDef biome = null)
        {
            if (kind?.RaceProps == null) return 0f;
            float nuance = GetHuntingBiomeNuanceMultiplier(biome, kind);
            return Mathf.Max(0.01f, GetReferenceSilverPerSkillPerCycle() / Mathf.Max(0.01f, GetAnimalValuePerKill(kind)) * nuance);
        }

        /// <summary>Units per Animals skill at 100% hunting tile: budget × (units/kill ÷ kill value) × biome nuance (commonality + combat power).</summary>
        public static float GetAnimalBaselineUnitsPerSkillForProduct(PawnKindDef kind, int unitsPerKill, BiomeDef biome = null)
        {
            if (kind?.RaceProps == null || unitsPerKill <= 0) return 0f;
            float vpk = GetAnimalValuePerKill(kind);
            float core = GetReferenceSilverPerSkillPerCycle() * unitsPerKill / Mathf.Max(0.01f, vpk);
            return core * GetHuntingBiomeNuanceMultiplier(biome, kind);
        }

        /// <summary>
        /// Abundance: BiomeDef.CommonalityOfAnimal vs min/max in biome (rarer → less yield). Danger: combatPower 0–200 (harder prey → less yield).
        /// Average of the two, clamped 0.75–1.25. No biome → still applies danger only if kind valid (rarity defaults neutral).
        /// </summary>
        public static float GetHuntingBiomeNuanceMultiplier(BiomeDef biome, PawnKindDef kind)
        {
            if (kind?.RaceProps == null) return 1f;
            float rarityMult = 1f;
            if (biome?.AllWildAnimals != null)
            {
                float cMin = float.MaxValue;
                float cMax = 0f;
                foreach (PawnKindDef k in biome.AllWildAnimals)
                {
                    if (k == null) continue;
                    float c = biome.CommonalityOfAnimal(k);
                    if (c <= 0f) continue;
                    if (c < cMin) cMin = c;
                    if (c > cMax) cMax = c;
                }

                if (cMax > 0f && cMin < float.MaxValue)
                {
                    float c = Mathf.Clamp(biome.CommonalityOfAnimal(kind), cMin, cMax);
                    float t = cMin >= cMax ? 0.5f : Mathf.InverseLerp(cMin, cMax, c);
                    rarityMult = Mathf.Lerp(0.75f, 1.25f, t);
                }
            }

            float cp = Mathf.Min(kind.combatPower, 200f);
            float dangerMult = Mathf.Lerp(1.25f, 0.75f, Mathf.InverseLerp(0f, 200f, cp));
            return Mathf.Clamp((rarityMult + dangerMult) * 0.5f, 0.75f, 1.25f);
        }

        /// <summary>Minimum Animal (handling) skill required to tame/train this animal. Uses vanilla TrainableUtility.MinimumHandlingSkill(pawn); fallback from body size and predator if unavailable.</summary>
        public static int GetMinAnimalSkillForAnimal(PawnKindDef kind)
        {
            if (kind?.RaceProps == null || !kind.RaceProps.Animal) return 0;
            if (minAnimalSkillCache.TryGetValue(kind, out int cached))
                return cached;
            int result;
            try
            {
                Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, null, PawnGenerationContext.NonPlayer));
                if (pawn != null)
                {
                    result = TrainableUtility.MinimumHandlingSkill(pawn);
                    pawn.Destroy(DestroyMode.Vanish);
                    minAnimalSkillCache[kind] = result;
                    return result;
                }
            }
            catch { /* fallback */ }
            float bodySize = Mathf.Max(0.1f, kind.RaceProps.baseBodySize);
            int baseSkill = bodySize >= 1f ? 2 : (bodySize >= 0.5f ? 1 : 0);
            if (kind.RaceProps.predator)
                baseSkill = Mathf.Min(10, baseSkill + 3);
            result = baseSkill;
            minAnimalSkillCache[kind] = result;
            return result;
        }

        /// <summary>Whether at least one pawn has Animals skill >= min required for this animal.</summary>
        public static bool OutpostCanHuntAnimal(WorldObject_WD_Outpost outpost, PawnKindDef kind)
        {
            if (outpost?.VirtualPawns == null || kind == null) return false;
            int required = GetMinAnimalSkillForAnimal(kind);
            if (required <= 0) return true;
            int maxAnimals = 0;
            var vpAnimals = outpost.VirtualPawns;
            for (int i = 0; i < vpAnimals.Count; i++)
            {
                if (vpAnimals[i].animals > maxAnimals) maxAnimals = vpAnimals[i].animals;
            }
            return maxAnimals >= required;
        }

        // ---- Mining ----

        /// <summary>Ore/building def that yields this product when mined (building.mineableThing == product).</summary>
        public static ThingDef GetOreDefForMinedProduct(ThingDef product)
        {
            if (product == null) return null;
            EnsurePlantOreMaps();
            return productToOreMap != null && productToOreMap.TryGetValue(product, out ThingDef ore) ? ore : null;
        }

        /// <summary>Minimum Mining skill required for this product. Silver=8, Gold=10, Uranium=9, Plasteel=12, Jade=6, ComponentIndustrial=8, ComponentSpacer=12, Obsidian=12, else 0.</summary>
        public static int GetMinMiningSkillForProduct(ThingDef product)
        {
            if (product == null) return 0;
            string n = product.defName ?? "";
            if (n == "Silver") return 8;
            if (n == "Gold") return 10;
            if (n == "Uranium") return 9;
            if (n == "Plasteel") return 12;
            if (n == "Jade") return 6;
            if (n == "ComponentIndustrial") return 8;
            if (n == "ComponentSpacer") return 12;
            if (n == "Obsidian") return 12;
            return 0;
        }

        /// <summary>Vanilla stone block defNames (for slider UI and fallback detection).</summary>
        public static readonly string[] VanillaStoneDefNames = { "BlocksGranite", "BlocksMarble", "BlocksSandstone", "BlocksLimestone", "BlocksSlate" };

        /// <summary>Vanilla ore defNames for baseline rules (Silver/Gold divisors, IsVanillaOre). Production/settings ore rows use <see cref="MiningScatterDiscovery.GetEffectiveScatterOresOrdered"/>.</summary>
        public static readonly string[] VanillaOreDefNames = { "Silver", "Gold", "Steel", "Plasteel", "Uranium", "Jade" };

        private static float GetMiningBaselineOverride(string defName)
        {
            var dict = WorldDominationMod.settings?.miningBaselineMultiplierByDefName;
            if (dict != null && dict.TryGetValue(defName, out float baseline)) return Mathf.Max(0f, baseline);
            return -1f;
        }

        private static float GetConfiguredDefaultMiningBaseline(string defName)
        {
            if (string.IsNullOrEmpty(defName)) return -1f;
            if (WorldDominationSettings.DefMiningBaselineByDefName.TryGetValue(defName, out float configured)) return configured;
            if (defName.StartsWith("Blocks", System.StringComparison.Ordinal)) return 25f;
            return -1f;
        }

        private static bool IsVanillaStone(ThingDef product)
        {
            if (product?.defName == null) return false;
            foreach (string s in VanillaStoneDefNames) if (product.defName == s) return true;
            return product.defName.StartsWith("Blocks");
        }

        private static bool IsVanillaOre(ThingDef product)
        {
            if (product?.defName == null) return false;
            foreach (string s in VanillaOreDefNames) if (product.defName == s) return true;
            return false;
        }

        /// <summary>True if product is mined as ore (raw resource), not stone/chunk.</summary>
        private static bool IsOreProduct(ThingDef product)
        {
            if (product == null) return false;
            if (IsVanillaOre(product)) return true;
            if (IsVanillaStone(product) || (product.defName ?? "").StartsWith("Blocks")) return false;
            return GetOreDefForMinedProduct(product) != null;
        }

        /// <summary>Baseline units per Mining skill per delivery. Slider override = absolute; else budget ÷ market value (ore/stone rules unchanged).</summary>
        public static float GetMiningBaselinePerSkill(ThingDef product)
        {
            if (product == null) return 0f;
            string n = product.defName ?? "";
            float overrideBaseline = GetMiningBaselineOverride(n);
            if (overrideBaseline >= 0f) return overrideBaseline;

            float configuredBaseline = GetConfiguredDefaultMiningBaseline(n);
            if (configuredBaseline >= 0f) return configuredBaseline;

            float refSilver = GetReferenceSilverPerSkillPerCycle();
            float baseline;
            if (IsVanillaStone(product) || IsVanillaOre(product))
            {
                float valuePerUnit = Mathf.Max(0.01f, product.BaseMarketValue);
                baseline = Mathf.Max(0.5f, refSilver / valuePerUnit);
                if (n == "Silver") baseline /= 8f;
                else if (n == "Gold") baseline /= 2f;
            }
            else
            {
                float valuePerUnit = Mathf.Max(0.01f, product.BaseMarketValue);
                float refBaseline = Mathf.Max(0.5f, refSilver / valuePerUnit);
                if (IsOreProduct(product))
                    baseline = Mathf.Max(0.1f, 0.25f * refBaseline);
                else
                    baseline = Mathf.Max(0.1f, 0.1f * refBaseline);
            }
            return baseline;
        }

        /// <summary>Whether at least one pawn has Mining >= min required for this product.</summary>
        public static bool OutpostCanProduceMiningItem(WorldObject_WD_Outpost outpost, ThingDef product)
        {
            if (outpost?.VirtualPawns == null || product == null) return true;
            int required = GetMinMiningSkillForProduct(product);
            if (required <= 0) return true;
            int maxMining = 0;
            var vpMining = outpost.VirtualPawns;
            for (int i = 0; i < vpMining.Count; i++)
            {
                if (vpMining[i].mining > maxMining) maxMining = vpMining[i].mining;
            }
            return maxMining >= required;
        }
    }
}
