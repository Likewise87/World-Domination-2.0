using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace TSA_WorldDomination
{
    public enum OutpostUpgradeCostMode
    {
        SpecificThingDef = 0,
        AnyStoneBlocks = 1
    }

    public enum OutpostUpgradeCategory
    {
        None = 0,
        Hospital = 1,
        Walls = 2,
        Traps = 3,
        AutoTurrets = 4,
        Mining = 5,
        Farming = 6,
        Hunting = 7,
        Mortar = 8,
        Research = 9,
        Production = 10,
        PowerPlant = 11,
        RapidResponse = 12,
        Ranch = 13,
        Fishing = 14,
        Warehouse = 15
    }

    /// <summary>Cost line for upgrade XML (&lt;thingDef&gt; + &lt;count&gt;). Plain fields so nested defs do not hit ThingDefCountClass shorthand parsing.</summary>
    public class OutpostUpgradeCostEntry
    {
        public ThingDef thingDef;
        public int count = 1;
        public OutpostUpgradeCostMode costMode = OutpostUpgradeCostMode.SpecificThingDef;
    }

    public class OutpostUpgradeDef : Def
    {
        public string imagePath;

        /// <summary>Logical group: Basic/Advanced Hospital share the same id. Defaults to defName in ResolveReferences if empty.</summary>
        public string upgradeLineId;

        /// <summary>Order within <see cref="upgradeLineId"/>; higher replaces lower when built.</summary>
        public int lineTier = 1;

        /// <summary>If true, this tier is only offered after the previous line tier is built (e.g. reinforced walls after palisades). If false (default), any higher tier can be built whenever resources and research allow.</summary>
        public bool requiresPreviousLineTier;

        public OutpostUpgradeCategory category;

        /// <summary>Use All (default) or Whitelist with <see cref="allowedOutpostDefNames"/>.</summary>
        public string outpostApplicability = "All";

        public List<string> allowedOutpostDefNames = new List<string>();

        public float defensiveStrengthBonus;
        /// <summary>Additional offensive recovery multiplier. 0.5 = +50% recovery.</summary>
        public float offensiveRecoveryBonus = 0f;

        /// <summary>Additive farming fertility score when built (0.15 = +15 percentage points before cap). Stacks with tier: total bonus × built level.</summary>
        public float tileFertilityBonus;
        /// <summary>Additive mining efficiency score when built (0.15 = +15pp).</summary>
        public float tileMiningBonus;
        /// <summary>Additive hunting animal-abundance score when built (0.15 = +15pp).</summary>
        public float tileAnimalAbundanceBonus;
        /// <summary>Additive fishing fish-abundance score when built (0.15 = +15pp).</summary>
        public float tileFishAbundanceBonus;

        // --- Mortar outpost (TSA_WD_Outpost_Mortar); bonuses × built level when stacked on same def line ---
        /// <summary>Additive shell strength damage per built level (only source of damage scaling beyond <see cref="WorldDominationSettings.mortarBaseShellDamage"/>).</summary>
        public float mortarShellDamageBonus;
        /// <summary>Additive hit chance per built level at 0–1 scale (0.05 = +5 percentage points to final hit chance).</summary>
        public float mortarHitChanceBonus;
        /// <summary>Cooldown multiplier reduction per built level, same units as cumulative Shooting skill (0.02 = −2 percentage points from duration multiplier; stacks with garrison sum).</summary>
        public float mortarCooldownReduction;
        /// <summary>Additive world max mortar range in tiles per built level (player mortar outpost only; stacks with <see cref="WorldDominationSettings.mortarRange"/>).</summary>
        public float mortarRangeBonus;
        /// <summary>When true and built, unlocks AA flak auto-fire vs hostile <see cref="TravelerMission.RaidDropPod"/> (stats come from shared mortar upgrades).</summary>
        public bool enablesAntiAir;

        /// <summary>When true and built, unlocks Decontamination Crew missions on the Build menu (Biotech pollution scrub).</summary>
        public bool enablesDecontaminationCrew;

        /// <summary>Flat research efficiency bonus per built level, 0–1 scale (0.10 = +10 percentage points on outpost research efficiency).</summary>
        public float researchEfficiencyBonus;

        /// <summary>Flat production output bonus per built level, 0–1 scale (0.10 = +10 percentage points on delivery output multiplier).</summary>
        public float productionEfficiencyBonus;

        /// <summary>Additive warehouse aura production bonus per built level (0.05 = +5pp to that warehouse's aura %).</summary>
        public float warehouseAuraBonus;

        /// <summary>Additive warehouse aura radius in world tiles per built level.</summary>
        public float warehouseAuraRadiusBonus;

        /// <summary>Flat remote colony power bonus per built level, in watts.</summary>
        public float remotePowerWattsBonus;

        /// <summary>Flat percentage-point bonus per built level to Rapid Response offensive strength cap (0.10 = +10%).</summary>
        public float rapidResponseOffensiveStrengthBonus;

        /// <summary>Flat ally pull radius tiles per built level (WD outposts only; added after mid/late scaling).</summary>
        public float allyPullRadiusBonus;

        /// <summary>Flat max virtual food storage added per built level for this outpost.</summary>
        public float foodStorageMaxBonus;

        /// <summary>Flat daily food production added per built level for this outpost.</summary>
        public float foodProductionFlatBonus;

        public List<OutpostUpgradeCostEntry> cost = new List<OutpostUpgradeCostEntry>();
        public string requiredResearch;
        public List<string> requiredResearches = new List<string>();

        private static readonly List<OutpostUpgradeCostEntry> EmptyCostList = new List<OutpostUpgradeCostEntry>();

        /// <summary>When false, upgrade purchases skip material costs (default true).</summary>
        public static bool UpgradesCostMaterialsEnabled =>
            WorldDominationMod.settings?.outpostUpgradesCostMaterials ?? true;

        /// <summary>When false, upgrade purchases skip research requirements (default true).</summary>
        public static bool UpgradesRequireResearchEnabled =>
            WorldDominationMod.settings?.outpostUpgradesRequireResearch ?? true;

        public IEnumerable<string> GetAllResearchRequirements()
        {
            if (!string.IsNullOrEmpty(requiredResearch))
                yield return requiredResearch;
            if (requiredResearches == null) yield break;
            for (int i = 0; i < requiredResearches.Count; i++)
            {
                string v = requiredResearches[i];
                if (!string.IsNullOrEmpty(v))
                    yield return v;
            }
        }

        /// <summary>Research requirements respecting the settings gate.</summary>
        public IEnumerable<string> GetEffectiveResearchRequirements()
        {
            if (!UpgradesRequireResearchEnabled) yield break;
            foreach (string r in GetAllResearchRequirements())
                yield return r;
        }

        /// <summary>Material cost respecting the settings gate.</summary>
        public List<OutpostUpgradeCostEntry> GetEffectiveCost()
        {
            if (!UpgradesCostMaterialsEnabled) return EmptyCostList;
            return cost ?? EmptyCostList;
        }

        public bool AppliesToOutpost(string worldObjectDefName)
        {
            if (string.IsNullOrEmpty(worldObjectDefName)) return false;

            // Typed bonuses bind by outpost behaviour, not only XML whitelist (research/drug-lab upgrades).
            if (researchEfficiencyBonus > 0f)
            {
                var wod = DefDatabase<WorldObjectDef>.GetNamedSilentFail(worldObjectDefName);
                return Outpost_Production_Utils.IsResearchOutpost(wod);
            }
            if (productionEfficiencyBonus > 0f)
            {
                string prodMode = outpostApplicability?.Trim() ?? "All";
                if (prodMode.Equals("Whitelist", StringComparison.OrdinalIgnoreCase))
                {
                    if (allowedOutpostDefNames == null) return false;
                    for (int i = 0; i < allowedOutpostDefNames.Count; i++)
                    {
                        if (allowedOutpostDefNames[i] == worldObjectDefName)
                            return true;
                    }
                    return false;
                }
                return worldObjectDefName == "TSA_WD_Outpost_DrugLab";
            }
            if (warehouseAuraBonus > 0f || warehouseAuraRadiusBonus > 0f)
            {
                var wod = DefDatabase<WorldObjectDef>.GetNamedSilentFail(worldObjectDefName);
                return Outpost_Production_Utils.IsWarehouseOutpost(wod);
            }
            if (remotePowerWattsBonus > 0f)
            {
                var wod = DefDatabase<WorldObjectDef>.GetNamedSilentFail(worldObjectDefName);
                return Outpost_Production_Utils.IsPowerPlantOutpost(wod);
            }
            if (rapidResponseOffensiveStrengthBonus > 0f)
            {
                var wod = DefDatabase<WorldObjectDef>.GetNamedSilentFail(worldObjectDefName);
                return Outpost_Production_Utils.IsRapidResponseOutpost(wod);
            }

            string mode = outpostApplicability?.Trim() ?? "All";
            if (mode.Equals("All", StringComparison.OrdinalIgnoreCase))
                return true;
            if (mode.Equals("Whitelist", StringComparison.OrdinalIgnoreCase))
            {
                if (allowedOutpostDefNames == null) return false;
                for (int i = 0; i < allowedOutpostDefNames.Count; i++)
                {
                    if (allowedOutpostDefNames[i] == worldObjectDefName)
                        return true;
                }
                return false;
            }
            Log.WarningOnce(
                $"OutpostUpgradeDef {defName}: unknown outpostApplicability '{outpostApplicability}', treating as All.",
                0x5E371903 ^ (defName?.GetHashCode() ?? 0));
            return true;
        }

        public override void ResolveReferences()
        {
            base.ResolveReferences();
            if (string.IsNullOrEmpty(upgradeLineId))
                upgradeLineId = defName;
            if (lineTier <= 0)
                lineTier = 1;
            if (cost == null)
                cost = new List<OutpostUpgradeCostEntry>();
            if (requiredResearches == null)
                requiredResearches = new List<string>();
            if (allowedOutpostDefNames == null)
                allowedOutpostDefNames = new List<string>();

            string mode = outpostApplicability?.Trim() ?? "All";
            if (mode.Equals("Whitelist", StringComparison.OrdinalIgnoreCase) && allowedOutpostDefNames.Count == 0)
                Log.Error($"OutpostUpgradeDef {defName}: outpostApplicability is Whitelist but allowedOutpostDefNames is empty.");
        }
    }
}
