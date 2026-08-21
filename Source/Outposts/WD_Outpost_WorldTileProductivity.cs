using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Numeric productivity indices and coarse yield estimates for world tiles.
    /// Base: farming = growing season × biome plant rank × hills; hunting = biome animal rank; mining = hilliness ladder.
    /// <para />
    /// <b>Landmarks / Odyssey mutators:</b> many VEE animal mutators only change per-species weights (<c>AnimalCommonalityFactorFor</c>) and do not bump <c>Tile.AnimalDensity</c>.
    /// Mutators that add a <c>GameConditionDef</c> (e.g. colossal/micro fauna, no wind) still use the <b>tile mutator</b> <c>defName</c> on <see cref="Tile.Mutators"/>—often the same string as the game condition.
    /// For predictable, auditable UX we add <b>flat additive bonuses</b> per <see cref="TileMutatorDef.defName"/> (values are fractions: 0.2 = +20 percentage points on display).
    /// Keys must match real <c>TileMutatorDef</c> <c>defName</c> (Vanilla Odyssey <c>Data/Odyssey/Defs/TileMutators</c>, VEE, other mods — not <c>LandmarkDef</c> names).
    /// The world tile “Features” line can show <b>dynamic labels</b> from workers (e.g. <c>WildPlants</c> displays as “wild psychoid plant” when that plant is rolled); lookup still uses the mutator <c>defName</c>.
    /// VEE <c>GameConditionDef</c>s in <c>GameConditions_Odyssey.xml</c> usually share <c>defName</c> with the <c>TileMutatorDef</c> on <see cref="Tile.Mutators"/>; penalties for “unpleasant to live there” use that same key (exception: <c>VEE_TidalFlooding</c> is applied via mutator <c>VEE_RisingWaters</c>).
    /// Final scores clamp to <see cref="ProductivityScoreCap"/> so UI can exceed 100% without runaway stacking.
    /// Tune the public dictionaries; duplicate keys are not used.
    /// </summary>
    public static class WorldTileProductivity
    {
        /// <summary>Max score after mutators (3.0 = up to 300% in UI) for fertility, animal abundance, and mining efficiency.</summary>
        public const float ProductivityScoreCap = 3.0f;

        /// <summary>RAG for 0–100%; purple above 100% (matches <see cref="WD_WorldLayer_ProductivityOverlay"/>).</summary>
        public static Color GetProductivityPercentDisplayColor(int percent)
        {
            float score = percent / 100f;
            if (score > 1f)
                return Color.Lerp(new Color(0.52f, 0.32f, 0.72f), new Color(0.32f, 0.04f, 0.42f),
                    Mathf.InverseLerp(1f, ProductivityScoreCap, score));
            if (percent <= 30) return Color.red;
            if (percent <= 60) return Color.yellow;
            return Color.green;
        }

        /// <summary>Lower bound for counting twelfths as farm-viable (matches prior VOE-style floor).</summary>
        private const float FarmingMinTemp = 6f;
        /// <summary>Upper bound for growing-season twelfths. 42°C was too strict for hot tropics (never 100% on rainforest). ~58°C matches typical vanilla plant max-growth temperature.</summary>
        private const float FarmingMaxTemp = 58f;

        private static bool baselinesInitialized;
        private static float plantDensityMin = 0f;
        private static float plantDensityMax = 1f;
        private static float animalDensityMin = 0f;
        private static float animalDensityMax = 1f;
        private static float fishPopulationMin = 0f;
        private static float fishPopulationMax = 1f;

        /// <summary>Additive hunting score per mutator <c>defName</c> (0.2f = +20pp). Odyssey modifiers + VEE <c>TileMutators_Animals.xml</c>.</summary>
        public static readonly Dictionary<string, float> MutatorHuntingScoreOffsets = new Dictionary<string, float>
        {
            { "AnimalHabitat", 0.20f },
            { "AnimalLife_Decreased", -0.35f },
            { "AnimalLife_Increased", 0.35f },
            { "VEE_AggressiveHerds", -0.05f },
            { "VEE_Alphabeavers", 0.15f },
            { "VEE_AbundantPredators", 0.2f },
            { "VEE_AbundantPrey", 0.4f },
            { "VEE_AnimaFauna", 0.20f },
            { "VEE_ColossalFauna", 0.4f },
            { "VEE_DistressedWildlife", -0.08f },
            { "VEE_DomesticatedEscapees", 0.15f },
            { "VEE_FeralKinship", 0.08f },
            { "VEE_GeomagneticStorm", -0.15f },
            { "VEE_IncreasedInfestations", 0.1f },
            { "VEE_MarineSanctuary", 0.2f },
            { "VEE_Megafauna", 0.25f },
            { "VEE_Microfauna", -0.15f },
            { "VEE_MigratoryHerds", 0.25f },
            { "VEE_NaturalAerie", 0.2f },
            { "VEE_NobleSteeds", 0.15f },
            { "VEE_NocturnalFauna", 0.15f },
            { "VEE_ReducedPredators", -0.10f },
            { "VEE_RagingWind", -0.10f },
            { "VEE_ReducedPrey", -0.15f },
            { "VEE_RisingWaters", -0.05f },
            { "VEE_RodentPlagues", 0.15f },
            { "VEE_RottenStench", -0.15f },
            { "VEE_VenomousEcosystem", 0.2f },
            { "VEE_WanderingCompanions", 0.06f },
            { "VEE_WastelandFauna", 0.2f },
            { "Pollution_Increased", -0.10f },
        };

        /// <summary>Additive fishing score per mutator <c>defName</c> (0.1f = +10pp). Odyssey fish population mutators.</summary>
        public static readonly Dictionary<string, float> MutatorFishingScoreOffsets = new Dictionary<string, float>
        {
            { "Fish_Decreased", -0.10f },
            { "Fish_Increased", 0.10f },
            { "Pollution_Increased", -0.10f },
            { "VEE_RisingWaters", 0.05f },
            { "VEE_MarineSanctuary", 0.15f },
        };

        /// <summary>Additive farming fertility score per mutator <c>defName</c>. Odyssey + VEE <c>TileMutators_Plants.xml</c> / weather.</summary>
        public static readonly Dictionary<string, float> MutatorFarmingScoreOffsets = new Dictionary<string, float>
        {
            { "ArcheanTrees", 0.20f },
            { "DryGround", -0.25f },
            { "Fertile", 0.50f },
            { "PlantGrove", 0.20f },
            { "PlantLife_Decreased", -0.35f },
            { "PlantLife_Increased", 0.35f },
            { "Pollution_Increased", -0.25f },
            { "VEE_RodentPlagues", -0.25f },
            { "WetClimate", 0.20f },
            { "WildPlants", 0.20f },
            { "WildTropicalPlants", 0.20f },
            { "VEE_AnimaFlora", 0.25f },
            { "VEE_AnimaSoils", 0.25f },
            { "VEE_AnimaSoils_Coast", 0.25f },
            { "VEE_AuburnTree_Birches", 0.10f },
            { "VEE_AuburnTree_Maples", 0.10f },
            { "VEE_AuburnTree_Oaks", 0.10f },
            { "VEE_AuburnTree_Poplars", 0.10f },
            { "VEE_MangroveTrees", 0.10f },
            { "VEE_Blooming_Buttercup", 0.10f },
            { "VEE_Blooming_ForgetMeNot", 0.10f },
            { "VEE_Blooming_Knapweed", 0.10f },
            { "VEE_Blooming_Loosestrife", 0.10f },
            { "VEE_Cactus_Barrel", 0.10f },
            { "VEE_Cactus_Beavertail", 0.10f },
            { "VEE_Cactus_Hedgehog", 0.10f },
            { "VEE_Cactus_OrganPipe", 0.10f },
            { "VEE_FertileRains", 0.40f },
            { "VEE_Fertility_Reduced", -0.30f },
            { "VEE_GeomagneticStorm", -0.15f },
            { "VEE_Mycelium", 0.10f },
            { "VEE_NoTrees", -0.10f },
            { "VEE_PlentifulGrass", 0.15f },
            { "VEE_PlantLife_Decimated", -0.60f },
            { "VEE_PlantLife_Overgrown", 0.60f },
            { "VEE_PoisonousFlora", -0.20f },
            { "VEE_RagingWind", -0.15f },
            { "VEE_RisingWaters", -0.15f },
            { "VEE_RottenStench", -0.20f },
            { "VEE_VolcanicRichSoil", 0.60f },
            { "VEE_WildCereals", 0.15f },
            { "VEE_WildFruitTrees", 0.20f },
            { "VEE_WildRice", 0.20f },
            { "VEE_WildSucculents", 0.20f },
            { "VEE_WildWheat", 0.20f },
        };

        /// <summary>Additive mining score (applied after hill baseline), same cap as other scores.</summary>
        public static readonly Dictionary<string, float> MutatorMiningScoreOffsets = new Dictionary<string, float>
        {
            { "VEE_GeomagneticStorm", -0.15f },
            { "VEE_RagingWind", -0.10f },
            { "VEE_RisingWaters", -0.25f },
            { "VEE_RottenStench", -0.25f },
            { "Junkyard", 0.15f },
            { "MineralRich", 0.60f },
            { "ObsidianDeposits", 0.30f },
            { "SteamGeysers_Increased", 0.10f },
            { "VEE_DeepOreDevoid", -0.60f },
            { "VEE_DeepOrePoor", -0.25f },
            { "VEE_DeepOreRich", 0.60f },
            { "VEE_JadeChunks", 0.25f },
            { "VEE_JadeiteMountains", 0.35f },
            { "VEE_MineableComponentSpacer", 0.50f },
            { "VEE_MineralDevoid", -0.60f },
            { "VEE_Sinkholes", 0.35f },
        };

        public static float SumMutatorScoreOffsets(Tile tile, Dictionary<string, float> byDefName)
        {
            if (byDefName.Count == 0 || tile.Mutators == null || tile.Mutators.Count == 0)
                return 0f;
            float sum = 0f;
            foreach (TileMutatorDef m in tile.Mutators)
            {
                if (m == null || m.defName == null) continue;
                if (byDefName.TryGetValue(m.defName, out float d))
                    sum += d;
            }
            return sum;
        }

        /// <summary>Aggregates mutator deltas by <c>defName</c> (duplicate entries stack).</summary>
        private static Dictionary<string, float> AggregateMutatorDeltas(Tile tile, Dictionary<string, float> byDefName)
        {
            var agg = new Dictionary<string, float>();
            if (tile?.Mutators == null || tile.Mutators.Count == 0 || byDefName.Count == 0)
                return agg;
            foreach (TileMutatorDef m in tile.Mutators)
            {
                if (m == null || m.defName == null) continue;
                if (!byDefName.TryGetValue(m.defName, out float d)) continue;
                if (agg.TryGetValue(m.defName, out float cur))
                    agg[m.defName] = cur + d;
                else
                    agg[m.defName] = d;
            }
            return agg;
        }

        private static string BuildMutatorDetailLines(Tile tile, Dictionary<string, float> byDefName)
        {
            var agg = AggregateMutatorDeltas(tile, byDefName);
            if (agg.Count == 0) return "";
            var sb = new StringBuilder();
            foreach (var kv in agg.OrderBy(x => x.Key))
            {
                if (Mathf.Abs(kv.Value) < 1e-6f) continue;
                TileMutatorDef mutDef = DefDatabase<TileMutatorDef>.GetNamedSilentFail(kv.Key);
                string label = mutDef?.LabelCap ?? kv.Key;
                int pp = Mathf.RoundToInt(kv.Value * 100f);
                string signed = (pp >= 0 ? "+" : "") + pp.ToString() + "%";
                sb.AppendLine("TSA_WD_ProductivityTooltip_MutatorLine".Translate(label, signed).ToString());
            }
            return sb.ToString();
        }

        /// <summary>Bullet lines for built upgrades that affect tile productivity (same format as mutator lines).</summary>
        public static string BuildOutpostUpgradeProductivityLines(WorldObject_WD_Outpost o, Func<OutpostUpgradeDef, float> bonusPerLevel)
        {
            if (o?.BuiltUpgradeLevels == null || o.BuiltUpgradeLevels.Count == 0) return "";
            var sb = new StringBuilder();
            foreach (var kv in o.BuiltUpgradeLevels.OrderBy(x => x.Key))
            {
                if (kv.Value <= 0) continue;
                var def = DefDatabase<OutpostUpgradeDef>.GetNamedSilentFail(kv.Key);
                if (def == null) continue;
                float b = bonusPerLevel(def) * kv.Value;
                if (Mathf.Abs(b) < 1e-6f) continue;
                int pp = Mathf.RoundToInt(b * 100f);
                string signed = (pp >= 0 ? "+" : "") + pp.ToString() + "%";
                sb.AppendLine("TSA_WD_ProductivityTooltip_MutatorLine".Translate(def.LabelCap, signed).ToString());
            }
            return sb.ToString().TrimEnd();
        }

        private static void AppendClampNote(StringBuilder sb, float rawTotal)
        {
            float clamped = Mathf.Clamp(rawTotal, 0f, ProductivityScoreCap);
            if (Mathf.Abs(rawTotal - clamped) < 1e-4f) return;
            if (rawTotal > ProductivityScoreCap)
                sb.AppendLine("TSA_WD_ProductivityTooltip_CappedHigh".Translate(
                    Mathf.RoundToInt(rawTotal * 100f),
                    Mathf.RoundToInt(ProductivityScoreCap * 100f)).ToString());
            else if (rawTotal < 0f)
                sb.AppendLine("TSA_WD_ProductivityTooltip_CappedLow".Translate(Mathf.RoundToInt(rawTotal * 100f)).ToString());
        }

        /// <summary>Biotech tile pollution in 0..1. Invalid / missing grid => 0.</summary>
        public static float GetTilePollution01(int tile)
        {
            var grid = Find.WorldGrid;
            if (grid == null || tile < 0 || tile >= grid.TilesCount)
                return 0f;
            return Mathf.Clamp01(grid[tile].pollution);
        }

        /// <summary>
        /// Multiplies ecology scores by (1 - pollution). Disabled via settings or clean tiles leave the score unchanged.
        /// </summary>
        public static float ApplyPollutionEcologyMultiplier(float score, int tile)
        {
            var seth = WorldDominationMod.settings;
            if (seth == null || !seth.pollutionEcologyPenaltyEnabled)
                return score;
            float p = GetTilePollution01(tile);
            if (p <= 0f)
                return score;
            return score * (1f - p);
        }

        /// <summary>
        /// True when ecology pollution penalty applies on this tile.
        /// <paramref name="pollution01"/> is tile pollution; <paramref name="multiplier01"/> is remaining factor (e.g. 0.8 at 20% pollution).
        /// </summary>
        public static bool TryGetPollutionEcologyPenalty(int tile, out float pollution01, out float multiplier01)
        {
            pollution01 = 0f;
            multiplier01 = 1f;
            var seth = WorldDominationMod.settings;
            if (seth == null || !seth.pollutionEcologyPenaltyEnabled)
                return false;
            float p = GetTilePollution01(tile);
            if (p <= 0f)
                return false;
            pollution01 = p;
            multiplier01 = 1f - p;
            return true;
        }

        /// <summary>One mutator-style bullet for WITab tile modifiers, or empty if no penalty.</summary>
        public static string GetPollutionEcologyModifierLine(int tile)
        {
            if (!TryGetPollutionEcologyPenalty(tile, out float pollution01, out float mult))
                return "";
            return "TSA_WD_ProductivityTooltip_PollutionLine".Translate(
                Mathf.RoundToInt(pollution01 * 100f),
                Mathf.RoundToInt(mult * 100f)).ToString();
        }

        private static void AppendPollutionLine(StringBuilder sb, int tile)
        {
            if (!TryGetPollutionEcologyPenalty(tile, out float pollution01, out float mult))
                return;
            sb.AppendLine("TSA_WD_ProductivityTooltip_PollutionLine".Translate(
                Mathf.RoundToInt(pollution01 * 100f),
                Mathf.RoundToInt(mult * 100f)).ToString());
        }

        /// <summary>Hover text: base score, per-mutator percentage points, total (matches column %).</summary>
        /// <param name="outpostUpgradeAdditive">Built outpost upgrades: additive fraction (e.g. 0.15 = +15pp) before cap.</param>
        /// <param name="outpostUpgradeDetailLines">Preformatted bullet lines from <see cref="BuildOutpostUpgradeProductivityLines"/> (optional).</param>
        public static string GetFarmingFertilityTooltipText(int tile, float outpostUpgradeAdditive = 0f, string outpostUpgradeDetailLines = null)
        {
            var grid = Find.WorldGrid;
            if (grid == null || tile < 0 || tile >= grid.TilesCount)
                return "TSA_WD_ProductivityTooltip_InvalidTile".Translate().ToString();
            Tile tileInfo = grid[tile];
            if (tileInfo.WaterCovered)
                return "TSA_WD_ProductivityTooltip_WaterTile".Translate().ToString();
            float baseScore = GetFarmingBaseScore(tile);
            float mutSum = SumMutatorScoreOffsets(tileInfo, MutatorFarmingScoreOffsets);
            float raw = ApplyPollutionEcologyMultiplier(baseScore + mutSum + outpostUpgradeAdditive, tile);
            float final = GetFarmingFertilityScore(tile, outpostUpgradeAdditive);
            var sb = new StringBuilder();
            sb.AppendLine("TSA_WD_ProductivityTooltip_FertilityIntro".Translate().ToString());
            sb.AppendLine();
            sb.AppendLine("TSA_WD_ProductivityTooltip_Base".Translate(Mathf.RoundToInt(baseScore * 100f)).ToString());
            sb.AppendLine("TSA_WD_ProductivityTooltip_MutatorsHeader".Translate().ToString());
            string mutBlock = BuildMutatorDetailLines(tileInfo, MutatorFarmingScoreOffsets);
            if (string.IsNullOrEmpty(mutBlock))
                sb.AppendLine("TSA_WD_ProductivityTooltip_NoMutators".Translate().ToString());
            else
                sb.Append(mutBlock);
            if (!string.IsNullOrEmpty(outpostUpgradeDetailLines))
            {
                sb.AppendLine();
                sb.AppendLine("TSA_WD_ProductivityTooltip_OutpostUpgradesHeader".Translate());
                sb.AppendLine(outpostUpgradeDetailLines);
            }
            AppendPollutionLine(sb, tile);
            sb.AppendLine();
            sb.AppendLine("TSA_WD_ProductivityTooltip_Total".Translate(
                Mathf.RoundToInt(final * 100f),
                Mathf.RoundToInt(ProductivityScoreCap * 100f)).ToString());
            AppendClampNote(sb, raw);
            return sb.ToString();
        }

        /// <summary>Hover text for hunting score: biome rank + mutator lines + total.</summary>
        public static string GetHuntingScoreTooltipText(int tile, float outpostUpgradeAdditive = 0f, string outpostUpgradeDetailLines = null)
        {
            var grid = Find.WorldGrid;
            if (grid == null || tile < 0 || tile >= grid.TilesCount)
                return "TSA_WD_ProductivityTooltip_InvalidTile".Translate().ToString();
            Tile tileInfo = grid[tile];
            if (tileInfo.WaterCovered)
                return "TSA_WD_ProductivityTooltip_WaterTile".Translate().ToString();
            float baseScore = GetHuntingBaseScore(tile);
            float mutSum = SumMutatorScoreOffsets(tileInfo, MutatorHuntingScoreOffsets);
            float raw = ApplyPollutionEcologyMultiplier(baseScore + mutSum + outpostUpgradeAdditive, tile);
            float final = GetHuntingScore(tile, outpostUpgradeAdditive);
            var sb = new StringBuilder();
            sb.AppendLine("TSA_WD_ProductivityTooltip_HuntingIntro".Translate().ToString());
            sb.AppendLine();
            sb.AppendLine("TSA_WD_ProductivityTooltip_Base".Translate(Mathf.RoundToInt(baseScore * 100f)).ToString());
            sb.AppendLine("TSA_WD_ProductivityTooltip_MutatorsHeader".Translate().ToString());
            string mutBlock = BuildMutatorDetailLines(tileInfo, MutatorHuntingScoreOffsets);
            if (string.IsNullOrEmpty(mutBlock))
                sb.AppendLine("TSA_WD_ProductivityTooltip_NoMutators".Translate().ToString());
            else
                sb.Append(mutBlock);
            if (!string.IsNullOrEmpty(outpostUpgradeDetailLines))
            {
                sb.AppendLine();
                sb.AppendLine("TSA_WD_ProductivityTooltip_OutpostUpgradesHeader".Translate());
                sb.AppendLine(outpostUpgradeDetailLines);
            }
            AppendPollutionLine(sb, tile);
            sb.AppendLine();
            sb.AppendLine("TSA_WD_ProductivityTooltip_Total".Translate(
                Mathf.RoundToInt(final * 100f),
                Mathf.RoundToInt(ProductivityScoreCap * 100f)).ToString());
            AppendClampNote(sb, raw);
            return sb.ToString();
        }

        /// <summary>Hover text: hilliness baseline, tile mutators, total (matches mining % column).</summary>
        public static string GetMiningEfficiencyTooltipText(int tile, float outpostUpgradeAdditive = 0f, string outpostUpgradeDetailLines = null)
        {
            var grid = Find.WorldGrid;
            if (grid == null || tile < 0 || tile >= grid.TilesCount)
                return "TSA_WD_ProductivityTooltip_InvalidTile".Translate().ToString();
            Tile tileInfo = grid[tile];
            if (tileInfo.WaterCovered)
                return "TSA_WD_ProductivityTooltip_WaterTile".Translate().ToString();
            float baseScore = GetMiningBaseScore(tile);
            float mutSum = SumMutatorScoreOffsets(tileInfo, MutatorMiningScoreOffsets);
            float raw = baseScore + mutSum + outpostUpgradeAdditive;
            float final = GetMiningOutputMultiplier(tile, outpostUpgradeAdditive);
            var sb = new StringBuilder();
            sb.AppendLine("TSA_WD_ProductivityTooltip_MiningIntro".Translate().ToString());
            sb.AppendLine();
            sb.AppendLine("TSA_WD_ProductivityTooltip_Base".Translate(Mathf.RoundToInt(baseScore * 100f)).ToString());
            sb.AppendLine("TSA_WD_ProductivityTooltip_MutatorsHeader".Translate().ToString());
            string mutBlock = BuildMutatorDetailLines(tileInfo, MutatorMiningScoreOffsets);
            if (string.IsNullOrEmpty(mutBlock))
                sb.AppendLine("TSA_WD_ProductivityTooltip_NoMutators".Translate().ToString());
            else
                sb.Append(mutBlock);
            if (!string.IsNullOrEmpty(outpostUpgradeDetailLines))
            {
                sb.AppendLine();
                sb.AppendLine("TSA_WD_ProductivityTooltip_OutpostUpgradesHeader".Translate());
                sb.AppendLine(outpostUpgradeDetailLines);
            }
            sb.AppendLine();
            sb.AppendLine("TSA_WD_ProductivityTooltip_Total".Translate(
                Mathf.RoundToInt(final * 100f),
                Mathf.RoundToInt(ProductivityScoreCap * 100f)).ToString());
            AppendClampNote(sb, raw);
            return sb.ToString();
        }

        /// <summary>Mutator bullet lines for UI columns (same format as productivity tooltips).</summary>
        public static string GetMutatorLinesForProductivity(Tile tile, Dictionary<string, float> byDefName)
        {
            return BuildMutatorDetailLines(tile, byDefName);
        }

        private static float ClampProductivityScore(float score)
        {
            return Mathf.Clamp(score, 0f, ProductivityScoreCap);
        }

        public struct FarmingYieldEstimate
        {
            public float fertilityScore;
            /// <summary>Fraction of year in farming temp band; for UI. Already folded into <see cref="fertilityScore"/>.</summary>
            public float growingSeasonFraction;
            /// <summary>Relative annual food production vs. a standard mid-biome tile at tier 2 and mid skill (~1 = baseline).</summary>
            public float relativeFoodPerYear;
        }

        public struct HuntingYieldEstimate
        {
            public float huntingScore;
            /// <summary>Relative annual meat production vs. a standard mid-biome tile at tier 2 and mid skill (~1 = baseline).</summary>
            public float relativeMeatPerYear;
        }

        private static void EnsureBaselines()
        {
            if (baselinesInitialized) return;
            baselinesInitialized = true;

            var biomes = DefDatabase<BiomeDef>.AllDefsListForReading;
            if (biomes == null || biomes.Count == 0) return;

            var plantDensities = new List<float>();
            var animalDensities = new List<float>();
            var fishPops = new List<float>();

            foreach (var b in biomes)
            {
                if (b == null) continue;
                if (b.plantDensity > 0f)
                    plantDensities.Add(b.plantDensity);
                if (b.animalDensity > 0f)
                    animalDensities.Add(b.animalDensity);
                if (b.maxFishPopulation > 0f)
                    fishPops.Add(b.maxFishPopulation);
            }

            if (plantDensities.Count > 0)
            {
                plantDensityMin = plantDensities.Min();
                plantDensityMax = plantDensities.Max();
                if (plantDensityMax <= plantDensityMin)
                    plantDensityMax = plantDensityMin + 0.01f;
            }
            else
            {
                plantDensityMin = 0f;
                plantDensityMax = 1f;
            }

            if (animalDensities.Count > 0)
            {
                animalDensityMin = animalDensities.Min();
                animalDensityMax = animalDensities.Max();
                if (animalDensityMax <= animalDensityMin) animalDensityMax = animalDensityMin + 0.01f;
            }

            if (fishPops.Count > 0)
            {
                fishPopulationMin = fishPops.Min();
                fishPopulationMax = fishPops.Max();
                if (fishPopulationMax <= fishPopulationMin) fishPopulationMax = fishPopulationMin + 0.01f;
            }
            else
            {
                fishPopulationMin = 0f;
                fishPopulationMax = 1f;
            }
        }

        private static float GetGrowingSeasonFraction(int tile, float minTemp, float maxTemp)
        {
            List<Twelfth> twelfths = GenTemperature.TwelfthsInAverageTemperatureRange(tile, minTemp, maxTemp);
            if (twelfths == null || twelfths.Count == 0) return 0f;
            return Mathf.Clamp01(twelfths.Count / 12f);
        }

        /// <summary>Climate × biome plant rank × hills, without mutator offsets.</summary>
        public static float GetFarmingBaseScore(int tile)
        {
            var grid = Find.WorldGrid;
            if (grid == null || tile < 0 || tile >= grid.TilesCount)
                return 0f;

            Tile tileInfo = grid[tile];
            if (tileInfo.WaterCovered)
                return 0f;

            BiomeDef biome = WorldTileInfo.GetBiome(tile);
            if (biome == null)
                return 0f;

            EnsureBaselines();

            float growingDaysFactor = GetGrowingSeasonFraction(tile, FarmingMinTemp, FarmingMaxTemp);

            float biomePlantFactor;
            if (biome.plantDensity <= 0f)
                biomePlantFactor = 0f;
            else if (plantDensityMax <= plantDensityMin + 1e-5f)
                biomePlantFactor = 1f;
            else
                biomePlantFactor = Mathf.Clamp01(Mathf.InverseLerp(plantDensityMin, plantDensityMax, biome.plantDensity));

            Hilliness hill = WorldTileInfo.GetHilliness(tile);
            float hillPenalty;
            switch (hill)
            {
                case Hilliness.Flat:
                case Hilliness.SmallHills:
                    hillPenalty = 1f;
                    break;
                case Hilliness.LargeHills:
                    hillPenalty = 0.8f;
                    break;
                case Hilliness.Mountainous:
                    hillPenalty = 0.5f;
                    break;
                case Hilliness.Impassable:
                    hillPenalty = 0f;
                    break;
                default:
                    hillPenalty = 0.8f;
                    break;
            }

            return growingDaysFactor * biomePlantFactor * hillPenalty;
        }

        /// <summary>Biome animal density rank only (0–1), without mutator offsets.</summary>
        public static float GetHuntingBaseScore(int tile)
        {
            var grid = Find.WorldGrid;
            if (grid == null || tile < 0 || tile >= grid.TilesCount)
                return 0f;
            if (grid[tile].WaterCovered)
                return 0f;

            BiomeDef biome = WorldTileInfo.GetBiome(tile);
            if (biome == null)
                return 0f;

            EnsureBaselines();

            if (biome.animalDensity <= 0f)
                return 0f;

            float range = animalDensityMax - animalDensityMin;
            if (range <= 0f)
                return 0.5f;

            return (biome.animalDensity - animalDensityMin) / range;
        }

        /// <summary>Hilliness mining baseline only (Flat=0.1, SmallHills=0.5, LargeHills=0.8, Mountainous=1, Impassable=0). Water tiles return 0.</summary>
        public static float GetMiningBaseScore(int tile)
        {
            var grid = Find.WorldGrid;
            if (grid == null || tile < 0 || tile >= grid.TilesCount)
                return 0f;
            if (grid[tile].WaterCovered)
                return 0f;
            Hilliness hill = WorldTileInfo.GetHilliness(tile);
            switch (hill)
            {
                case Hilliness.Flat: return 0.1f;
                case Hilliness.SmallHills: return 0.5f;
                case Hilliness.LargeHills: return 0.8f;
                case Hilliness.Mountainous: return 1f;
                case Hilliness.Impassable: return 0f;
                default: return 0.5f;
            }
        }

        /// <summary>
        /// Farming fertility score (base roughly 0–1; mutators can push up to <see cref="ProductivityScoreCap"/>).
        /// Water tiles always return 0.
        /// </summary>
        /// <param name="outpostUpgradeAdditive">Additive bonus from built <see cref="OutpostUpgradeDef.tileFertilityBonus"/> (fraction; 0.15 = +15pp).</param>
        public static float GetFarmingFertilityScore(int tile, float outpostUpgradeAdditive = 0f)
        {
            var grid = Find.WorldGrid;
            if (grid == null || tile < 0 || tile >= grid.TilesCount)
                return 0f;
            if (grid[tile].WaterCovered)
                return 0f;

            float score = GetFarmingBaseScore(tile);
            score += SumMutatorScoreOffsets(grid[tile], MutatorFarmingScoreOffsets);
            score += outpostUpgradeAdditive;
            score = ApplyPollutionEcologyMultiplier(score, tile);
            return ClampProductivityScore(score);
        }

        /// <summary>Mining efficiency by hilliness plus flat mutator offsets (same cap as <see cref="ProductivityScoreCap"/>). Water tiles return 0.</summary>
        /// <param name="outpostUpgradeAdditive">From built <see cref="OutpostUpgradeDef.tileMiningBonus"/>.</param>
        public static float GetMiningOutputMultiplier(int tile, float outpostUpgradeAdditive = 0f)
        {
            var grid = Find.WorldGrid;
            if (grid == null || tile < 0 || tile >= grid.TilesCount)
                return 0f;
            if (grid[tile].WaterCovered)
                return 0f;
            float baseline = GetMiningBaseScore(tile);
            baseline += SumMutatorScoreOffsets(grid[tile], MutatorMiningScoreOffsets);
            baseline += outpostUpgradeAdditive;
            return ClampProductivityScore(baseline);
        }

        /// <summary>
        /// Short label for farming fertility rating based on the numeric score.
        /// </summary>
        public static string GetFarmingFertilityLabel(int tile, float outpostUpgradeAdditive = 0f)
        {
            float score = GetFarmingFertilityScore(tile, outpostUpgradeAdditive);
            if (score <= 0.05f) return "None";
            if (score < 0.25f) return "Poor";
            if (score < 0.6f) return "Normal";
            if (score < 0.85f) return "Good";
            if (score < 1f) return "Excellent";
            return "Exceptional";
        }

        /// <summary>
        /// Hunting productivity: biome animal density rank plus flat mutator offsets (up to <see cref="ProductivityScoreCap"/>). Water tiles return 0.
        /// </summary>
        /// <param name="outpostUpgradeAdditive">From built <see cref="OutpostUpgradeDef.tileAnimalAbundanceBonus"/>.</param>
        public static float GetHuntingScore(int tile, float outpostUpgradeAdditive = 0f)
        {
            var grid = Find.WorldGrid;
            if (grid == null || tile < 0 || tile >= grid.TilesCount)
                return 0f;
            if (grid[tile].WaterCovered)
                return 0f;

            float score = GetHuntingBaseScore(tile);
            score += SumMutatorScoreOffsets(grid[tile], MutatorHuntingScoreOffsets);
            score += outpostUpgradeAdditive;
            score = ApplyPollutionEcologyMultiplier(score, tile);
            return ClampProductivityScore(score);
        }

        /// <summary>Biome maxFishPopulation rank only (0–1). Non-coastal / water / zero population → 0.</summary>
        public static float GetFishingBaseScore(int tile)
        {
            var grid = Find.WorldGrid;
            if (grid == null || tile < 0 || tile >= grid.TilesCount)
                return 0f;
            Tile tileInfo = grid[tile];
            if (tileInfo.WaterCovered || !tileInfo.IsCoastal)
                return 0f;

            float maxPop = tileInfo.MaxFishPopulation;
            if (maxPop <= 0f)
                return 0f;

            EnsureBaselines();
            float range = fishPopulationMax - fishPopulationMin;
            if (range <= 0f)
                return 0.5f;
            return Mathf.Clamp01((maxPop - fishPopulationMin) / range);
        }

        /// <summary>
        /// Fishing productivity for ocean-coast land tiles: MaxFishPopulation rank plus fish mutators (up to <see cref="ProductivityScoreCap"/>).
        /// Non-coastal and water tiles return 0.
        /// </summary>
        /// <param name="outpostUpgradeAdditive">From built <see cref="OutpostUpgradeDef.tileFishAbundanceBonus"/>.</param>
        public static float GetFishingScore(int tile, float outpostUpgradeAdditive = 0f)
        {
            var grid = Find.WorldGrid;
            if (grid == null || tile < 0 || tile >= grid.TilesCount)
                return 0f;
            Tile tileInfo = grid[tile];
            if (tileInfo.WaterCovered || !tileInfo.IsCoastal)
                return 0f;

            float score = GetFishingBaseScore(tile);
            score += SumMutatorScoreOffsets(tileInfo, MutatorFishingScoreOffsets);
            score += outpostUpgradeAdditive;
            score = ApplyPollutionEcologyMultiplier(score, tile);
            return ClampProductivityScore(score);
        }

        /// <summary>Hover text for fishing score: coast gate, biome rank, mutators, total.</summary>
        public static string GetFishingScoreTooltipText(int tile, float outpostUpgradeAdditive = 0f, string outpostUpgradeDetailLines = null)
        {
            var grid = Find.WorldGrid;
            if (grid == null || tile < 0 || tile >= grid.TilesCount)
                return "TSA_WD_ProductivityTooltip_InvalidTile".Translate().ToString();
            Tile tileInfo = grid[tile];
            if (tileInfo.WaterCovered)
                return "TSA_WD_ProductivityTooltip_WaterTile".Translate().ToString();
            if (!tileInfo.IsCoastal)
                return "TSA_WD_ProductivityTooltip_NotCoastal".Translate().ToString();
            float baseScore = GetFishingBaseScore(tile);
            float mutSum = SumMutatorScoreOffsets(tileInfo, MutatorFishingScoreOffsets);
            float raw = ApplyPollutionEcologyMultiplier(baseScore + mutSum + outpostUpgradeAdditive, tile);
            float final = GetFishingScore(tile, outpostUpgradeAdditive);
            var sb = new StringBuilder();
            sb.AppendLine("TSA_WD_ProductivityTooltip_FishingIntro".Translate().ToString());
            sb.AppendLine();
            sb.AppendLine("TSA_WD_ProductivityTooltip_Base".Translate(Mathf.RoundToInt(baseScore * 100f)).ToString());
            sb.AppendLine("TSA_WD_ProductivityTooltip_MutatorsHeader".Translate().ToString());
            string mutBlock = BuildMutatorDetailLines(tileInfo, MutatorFishingScoreOffsets);
            if (string.IsNullOrEmpty(mutBlock))
                sb.AppendLine("TSA_WD_ProductivityTooltip_NoMutators".Translate().ToString());
            else
                sb.Append(mutBlock);
            if (!string.IsNullOrEmpty(outpostUpgradeDetailLines))
            {
                sb.AppendLine();
                sb.AppendLine("TSA_WD_ProductivityTooltip_OutpostUpgradesHeader".Translate());
                sb.AppendLine(outpostUpgradeDetailLines);
            }
            AppendPollutionLine(sb, tile);
            sb.AppendLine();
            sb.AppendLine("TSA_WD_ProductivityTooltip_Total".Translate(
                Mathf.RoundToInt(final * 100f),
                Mathf.RoundToInt(ProductivityScoreCap * 100f)).ToString());
            AppendClampNote(sb, raw);
            return sb.ToString();
        }

        private static float GetSkillFactor(float totalRelevantSkill, int workerCount)
        {
            if (workerCount <= 0 || totalRelevantSkill <= 0f) return 0.5f;
            float avgSkill = totalRelevantSkill / workerCount;
            // 0 skill -> 0.5; 12 skill -> 1.0; 20+ skill -> ~1.33, clamped below.
            float factor = 0.5f + 0.5f * (avgSkill / 12f);
            return Mathf.Clamp(factor, 0.5f, 1.5f);
        }

        private static float GetTierFactor(SettlementTier tier)
        {
            switch (tier)
            {
                case SettlementTier.T1: return 1f;
                case SettlementTier.T2: return 1.3f;
                case SettlementTier.T3: return 1.6f;
                case SettlementTier.T4: return 2f;
                default: return 1f;
            }
        }

        /// <summary>
        /// Coarse farming yield estimate for UI and balance decisions. Fertility score already includes growing season (see <see cref="GetFarmingFertilityScore"/>); skill and tier scale the result. <see cref="FarmingYieldEstimate.growingSeasonFraction"/> is informational only.
        /// relativeFoodPerYear is a unitless multiplier where ~1 represents a "standard" farming tile.
        /// </summary>
        public static FarmingYieldEstimate GetFarmingYields(int tile, SettlementTier tier, float totalPlantsSkill, int workerCount, float tileUpgradeFertilityBonus = 0f)
        {
            float fertility = GetFarmingFertilityScore(tile, tileUpgradeFertilityBonus);
            float seasonFraction = GetGrowingSeasonFraction(tile, FarmingMinTemp, FarmingMaxTemp);
            float skillFactor = GetSkillFactor(totalPlantsSkill, workerCount);
            float tierFactor = GetTierFactor(tier);

            float relative = fertility * skillFactor * tierFactor;
            // Soft clamp so very strong combos don't explode.
            relative = Mathf.Clamp(relative, 0f, 3f);

            return new FarmingYieldEstimate
            {
                fertilityScore = fertility,
                growingSeasonFraction = seasonFraction,
                relativeFoodPerYear = relative
            };
        }

        /// <summary>
        /// Coarse hunting yield estimate for UI and balance decisions. Relies on hunting score, tier and skill.
        /// relativeMeatPerYear is a unitless multiplier where ~1 represents a "standard" hunting tile.
        /// </summary>
        public static HuntingYieldEstimate GetHuntingYields(int tile, SettlementTier tier, float totalHuntingSkill, int workerCount, float tileUpgradeAnimalBonus = 0f)
        {
            float huntingScore = GetHuntingScore(tile, tileUpgradeAnimalBonus);
            float skillFactor = GetSkillFactor(totalHuntingSkill, workerCount);
            float tierFactor = GetTierFactor(tier);

            float relative = huntingScore * skillFactor * tierFactor;
            relative = Mathf.Clamp(relative, 0f, 3f);

            return new HuntingYieldEstimate
            {
                huntingScore = huntingScore,
                relativeMeatPerYear = relative
            };
        }
    }
}

