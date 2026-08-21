using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>One craftable item for production/trading outposts. XML: thingDef (defName), amountPerSkillLevel (float), minSkillLevel (int), optional requiredResearch (defName). Optional <c>MayRequire</c> / <c>MayRequireAnyOf</c> on the <c>li</c> (same semantics as vanilla XML).</summary>
    public class ProductionOption
    {
        /// <summary>ThingDef.defName of the produced item.</summary>
        public string thingDef;
        /// <summary>Amount produced per relevant skill level per cycle (e.g. 0.5 = half a component per crafting point).</summary>
        public float amountPerSkillLevel = 1f;
        /// <summary>Minimum skill level (per pawn) required to contribute; only pawns with skill >= this count.</summary>
        public int minSkillLevel = 0;
        /// <summary>ResearchProjectDef.defName required to unlock this option. Empty = no research.</summary>
        public string requiredResearch;

        /// <summary>Optional SkillDef.defName for output scaling and min-skill checks. If empty, uses the first skill from the outpost’s <c>MinCumulativeSkill</c> (see <see cref="Outpost_Production_Utils.GetSkillDefsFromMinCumulativeSkill"/>).</summary>
        public string scalingSkill;

        /// <summary>Comma-separated packageIds; all must be active (same as XML <c>MayRequire</c> on the <c>li</c>). Empty = no gate.</summary>
        public string MayRequire;
        /// <summary>Comma-separated packageIds; at least one must be active (same as <c>MayRequireAnyOf</c>). Empty = no gate.</summary>
        public string MayRequireAnyOf;
    }

    /// <summary>One set of minimum cumulative skills. XML: &lt;li&gt; with child elements named by SkillDef.defName and value = minimum, e.g. &lt;Social&gt;5&lt;/Social&gt;&lt;Plants&gt;5&lt;/Plants&gt;.</summary>
    public class MinCumulativeSkillSet
    {
        public int Animals;
        public int Artistic;
        public int Construction;
        public int Cooking;
        public int Crafting;
        public int Intellectual;
        public int Medicine;
        public int Melee;
        public int Mining;
        public int Plants;
        public int Shooting;
        public int Social;

        /// <summary>Returns (SkillDef, minimum) for each non-zero skill in this set.</summary>
        public IEnumerable<KeyValuePair<SkillDef, int>> GetRequirements()
        {
            if (Animals > 0)
            {
                var sd = DefDatabase<SkillDef>.GetNamedSilentFail("Animals");
                if (sd != null) yield return new KeyValuePair<SkillDef, int>(sd, Animals);
            }
            if (Artistic > 0)
            {
                var sd = DefDatabase<SkillDef>.GetNamedSilentFail("Artistic");
                if (sd != null) yield return new KeyValuePair<SkillDef, int>(sd, Artistic);
            }
            if (Construction > 0)
            {
                var sd = DefDatabase<SkillDef>.GetNamedSilentFail("Construction");
                if (sd != null) yield return new KeyValuePair<SkillDef, int>(sd, Construction);
            }
            if (Cooking > 0)
            {
                var sd = DefDatabase<SkillDef>.GetNamedSilentFail("Cooking");
                if (sd != null) yield return new KeyValuePair<SkillDef, int>(sd, Cooking);
            }
            if (Crafting > 0)
            {
                var sd = DefDatabase<SkillDef>.GetNamedSilentFail("Crafting");
                if (sd != null) yield return new KeyValuePair<SkillDef, int>(sd, Crafting);
            }
            if (Intellectual > 0)
            {
                var sd = DefDatabase<SkillDef>.GetNamedSilentFail("Intellectual");
                if (sd != null) yield return new KeyValuePair<SkillDef, int>(sd, Intellectual);
            }
            if (Medicine > 0)
            {
                var sd = DefDatabase<SkillDef>.GetNamedSilentFail("Medicine");
                if (sd != null) yield return new KeyValuePair<SkillDef, int>(sd, Medicine);
            }
            if (Melee > 0)
            {
                var sd = DefDatabase<SkillDef>.GetNamedSilentFail("Melee");
                if (sd != null) yield return new KeyValuePair<SkillDef, int>(sd, Melee);
            }
            if (Mining > 0)
            {
                var sd = DefDatabase<SkillDef>.GetNamedSilentFail("Mining");
                if (sd != null) yield return new KeyValuePair<SkillDef, int>(sd, Mining);
            }
            if (Plants > 0)
            {
                var sd = DefDatabase<SkillDef>.GetNamedSilentFail("Plants");
                if (sd != null) yield return new KeyValuePair<SkillDef, int>(sd, Plants);
            }
            if (Shooting > 0)
            {
                var sd = DefDatabase<SkillDef>.GetNamedSilentFail("Shooting");
                if (sd != null) yield return new KeyValuePair<SkillDef, int>(sd, Shooting);
            }
            if (Social > 0)
            {
                var sd = DefDatabase<SkillDef>.GetNamedSilentFail("Social");
                if (sd != null) yield return new KeyValuePair<SkillDef, int>(sd, Social);
            }
        }

        /// <summary>True if this set has at least one non-zero requirement.</summary>
        public bool HasAnyRequirement()
        {
            return Animals > 0 || Artistic > 0 || Construction > 0 || Cooking > 0 || Crafting > 0
                || Intellectual > 0 || Medicine > 0 || Melee > 0 || Mining > 0 || Plants > 0 || Shooting > 0 || Social > 0;
        }
    }

    /// <summary>Optional modExtension for WorldObjectDef (WD outposts). Aligned with Outposts mod: allowed/disallowed biomes, required skills, cost.</summary>
    public class OutpostDefExtension : DefModExtension
    {
        /// <summary>If non-empty, this outpost type cannot be built on these biomes (defName). Blacklist.</summary>
        public List<string> disallowedBiomes;

        /// <summary>If non-empty, this outpost type can only be built on these biomes (defName). Whitelist. Empty = all biomes allowed (subject to disallowedBiomes).</summary>
        public List<string> allowedBiomes;

        /// <summary>Required cumulative skills to found from caravan. Each li contains skill defNames as elements with minimum value, e.g. &lt;li&gt;&lt;Social&gt;5&lt;/Social&gt;&lt;Plants&gt;5&lt;/Plants&gt;&lt;/li&gt;. All skills in all sets must be met.</summary>
        public List<MinCumulativeSkillSet> MinCumulativeSkill;

        /// <summary>Minimum number of pawns in the caravan to found this outpost from caravan. Ignored when establishing after conquest.</summary>
        public int minPawnsToFound = 1;

        /// <summary>Research project defNames required to establish this outpost type (all must be completed). Empty = no research requirement.</summary>
        public List<string> requiredResearchProjectDefNames;

        /// <summary>Materials consumed when establishing this outpost from a caravan. Empty/null = use default (50 wood, scaled by settings multiplier).</summary>
        public List<ThingDefCountClass> establishmentCost;

        /// <summary>Production cycle length in days (time until next delivery). If &gt; 0, used instead of mod settings. 1 day = 60000 ticks.</summary>
        public float productionCycleDays;

        /// <summary>Min fertility (0–100) for harvest outposts. Tile fertility % must be &gt;= this. Default 30.</summary>
        public int minFertilityPercent = 30;

        /// <summary>Min animal abundance (0–100) for hunting. Tile animal score % must be &gt;= this. Default 30.</summary>
        public int minAnimalAbundancePercent = 30;

        /// <summary>Min fish abundance (0–100) for fishing. Tile fish score % must be &gt;= this. Default 30.</summary>
        public int minFishAbundancePercent = 30;

        /// <summary>Production/trading outposts: list of items that can be produced. Each option has thingDef, amountPerSkillLevel (float), minSkillLevel, optional requiredResearch, optional MayRequire / MayRequireAnyOf (packageIds, comma-separated). Empty = use legacy fallback list.</summary>
        public List<ProductionOption> productionOptions;

        /// <summary>Minimum number of settlements or outposts (non-hostile, non-player) within minNearbyRadiusTiles. 0 = no requirement.</summary>
        public int minNearbySettlementsOrOutposts;

        /// <summary>Radius in tiles to count settlements/outposts. Only used when minNearbySettlementsOrOutposts &gt; 0.</summary>
        public int minNearbyRadiusTiles;

        /// <summary>Outpost tier (1, 2, 3, or 4+ for mods). When establishing after conquest, only outpost types with tier &lt;= conquered settlement tier can be built. No upper bound in code.</summary>
        public int outpostTier = 1;

        /// <summary>Academy outposts: base skill XP granted per in-game day (scaled by cycle length and teacher skill). Must be &gt; 0 for academy behaviour.</summary>
        public float academyBaseXpPerDay;

        /// <summary>Academy: minimum skill level on at least one occupant to offer/teach a skill in the UI.</summary>
        public int academyMinTeacherSkill = 8;

        /// <summary>Academy: students stop gaining when their level reaches <c>teacherLevel - academyTeachCapOffset</c> (exclusive cap on learning: level must stay strictly below that).</summary>
        public int academyTeachCapOffset = 3;

        /// <summary>Academy: optional whitelist of <see cref="SkillDef.defName"/>; empty = all loaded skills (subject to per-pawn disabled checks in UI).</summary>
        public List<string> academyAllowedSkills;

        /// <summary>Research outposts: outpost efficiency applied to cumulative Intellectual output. Must be &gt; 0 for research behaviour.</summary>
        public float researchEfficiencyFraction;

        /// <summary>Legacy XML compatibility only. Research now uses <see cref="Outpost_Research.SimpleResearchBenchSpeedFactor"/>.</summary>
        [Obsolete("Use researchEfficiencyFraction only. Research bench speed is a hardcoded simple-bench baseline.")]
        public float researchBenchSpeedFactor = 0.75f;

        /// <summary>Power plant outposts: base remote power supplied to the player's colony in watts. Must be &gt; 0 for power-plant behaviour.</summary>
        public float remotePowerWatts;

        /// <summary>When false, this outpost does not assign cumulative skill to physical-goods production (stats UI shows N/A). Default true.</summary>
        public bool usesPhysicalGoodsProductionSkill = true;

        /// <summary>Returns null if biome is allowed; otherwise returns a reason string. Same logic as Outposts mod: disallowed list wins, then allowed list if set.</summary>
        public string CanBuildInBiome(string biomeDefName, string biomeLabel, string outpostLabel)
        {
            if (string.IsNullOrEmpty(biomeDefName)) return null;
            string bl = biomeLabel ?? biomeDefName ?? "?";
            if (disallowedBiomes is { Count: > 0 } && disallowedBiomes.Contains(biomeDefName))
                return "TSA_WD_Establish_BiomeNotAllowed".Translate(outpostLabel, bl);
            if (allowedBiomes is { Count: > 0 } && !allowedBiomes.Contains(biomeDefName))
                return "TSA_WD_Establish_BiomeNotAllowed".Translate(outpostLabel, bl);
            return null;
        }
    }
}
