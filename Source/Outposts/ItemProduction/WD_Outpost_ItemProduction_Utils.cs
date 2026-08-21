using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Shared production utilities: cycle days, tile factors, production options, formatting. Relevant skills: <see cref="WorldObject_WD_Outpost.GetRelevantSkillDefs"/> (MinCumulativeSkill in XML first).</summary>
    public static class Outpost_Production_Utils
    {
        [System.Flags]
        private enum OutpostTypeFlags
        {
            None        = 0,
            Farming     = 1,
            Hunting     = 2,
            Mining      = 4,
            Recruiting  = 8,
            Trading     = 16,
            Fabrication = 32,
            Production  = 64,
            SimpleProduction = 128,
            Scavenging  = 256,
            Mortar      = 512,
            Warehouse   = 1024,
            RapidResponse = 2048,
            Ranch       = 4096,
            Embassy     = 8192,
            Fishing     = 16384,
        }

        private static readonly Dictionary<WorldObjectDef, OutpostTypeFlags> outpostTypeFlagsCache = new Dictionary<WorldObjectDef, OutpostTypeFlags>();
        private static readonly Dictionary<WorldObjectDef, List<SkillDef>> relevantSkillDefsCache = new Dictionary<WorldObjectDef, List<SkillDef>>();

        private static OutpostTypeFlags GetTypeFlags(WorldObjectDef def)
        {
            if (def == null) return OutpostTypeFlags.None;
            if (outpostTypeFlagsCache.TryGetValue(def, out var flags)) return flags;
            flags = OutpostTypeFlags.None;
            string d = def.defName?.ToLowerInvariant() ?? "";
            if (d.Contains("farming"))          flags |= OutpostTypeFlags.Farming;
            if (d.Contains("hunting"))          flags |= OutpostTypeFlags.Hunting;
            if (d.Contains("mining"))           flags |= OutpostTypeFlags.Mining;
            if (d.Contains("recruiting"))       flags |= OutpostTypeFlags.Recruiting;
            if (d.Contains("trading"))          flags |= OutpostTypeFlags.Trading;
            if (d.Contains("scavenging"))       flags |= OutpostTypeFlags.Scavenging;
            if (d.Contains("fabrication"))      flags |= OutpostTypeFlags.Fabrication;
            if (d.Contains("production"))       flags |= OutpostTypeFlags.Production;
            if (d.Contains("simpleproduction")) flags |= OutpostTypeFlags.SimpleProduction;
            if (d.Contains("mortar"))           flags |= OutpostTypeFlags.Mortar;
            if (d.Contains("warehouse"))       flags |= OutpostTypeFlags.Warehouse;
            if (d.Contains("rapidresponse"))    flags |= OutpostTypeFlags.RapidResponse;
            if (d.Contains("ranch"))            flags |= OutpostTypeFlags.Ranch;
            if (d.Contains("embassy"))          flags |= OutpostTypeFlags.Embassy;
            if (d.Contains("fishing"))          flags |= OutpostTypeFlags.Fishing;
            outpostTypeFlagsCache[def] = flags;
            return flags;
        }

        /// <summary>Cached relevant skill defs per WorldObjectDef (avoids new List + XML walk per call).</summary>
        public static List<SkillDef> GetCachedRelevantSkillDefs(WorldObjectDef def)
        {
            if (def == null) return null;
            if (relevantSkillDefsCache.TryGetValue(def, out var cached)) return cached;
            cached = WorldObject_WD_Outpost.GetRelevantSkillDefs(def);
            relevantSkillDefsCache[def] = cached;
            return cached;
        }

        /// <summary>Avoids repeating fertility lookups many times per tick (e.g. inspect string every GUI frame).</summary>
        private struct FarmingTileFactorCacheEntry
        {
            public int tick;
            public int tile;
            public float fertBonus;
            public float value;
        }

        private static readonly Dictionary<int, FarmingTileFactorCacheEntry> farmingTileFactorCache = new Dictionary<int, FarmingTileFactorCacheEntry>();

        /// <summary>Localized skill name for UI (RimWorld <see cref="SkillDef.LabelCap"/>).</summary>
        public static string SkillLabelCap(SkillDef skillDef) => skillDef == null ? "" : skillDef.LabelCap;

        /// <summary>Unique skill defs from <c>MinCumulativeSkill</c> (all sets, in encounter order). Null if none declared.</summary>
        public static List<SkillDef> GetSkillDefsFromMinCumulativeSkill(WorldObjectDef def)
        {
            var ext = def?.GetModExtension<OutpostDefExtension>();
            if (ext?.MinCumulativeSkill == null) return null;
            var list = new List<SkillDef>();
            foreach (var set in ext.MinCumulativeSkill)
            {
                if (set == null) continue;
                foreach (var kv in set.GetRequirements())
                    if (kv.Key != null && !list.Contains(kv.Key))
                        list.Add(kv.Key);
            }
            return list.Count > 0 ? list : null;
        }

        /// <summary>Skill that scales this production option: <c>option.scalingSkill</c> if set, else first skill from <see cref="GetSkillDefsFromMinCumulativeSkill"/> / <see cref="WorldObject_WD_Outpost.GetRelevantSkillDefs"/>.</summary>
        public static SkillDef GetScalingSkillDefForProduction(WorldObject_WD_Outpost outpost, ProductionOption option)
        {
            if (option != null && !string.IsNullOrEmpty(option.scalingSkill))
            {
                var sd = DefDatabase<SkillDef>.GetNamedSilentFail(option.scalingSkill);
                if (sd != null) return sd;
            }
            var defs = GetCachedRelevantSkillDefs(outpost?.def);
            return defs != null && defs.Count > 0 ? defs[0] : null;
        }

        /// <summary>True if this outpost def is farming (crops).</summary>
        public static bool IsFarmingOutpost(WorldObjectDef def) =>
            (GetTypeFlags(def) & OutpostTypeFlags.Farming) != 0;

        /// <summary>True if this outpost def is hunting.</summary>
        public static bool IsHuntingOutpost(WorldObjectDef def) =>
            (GetTypeFlags(def) & OutpostTypeFlags.Hunting) != 0;

        /// <summary>True if this outpost def is fishing.</summary>
        public static bool IsFishingOutpost(WorldObjectDef def) =>
            (GetTypeFlags(def) & OutpostTypeFlags.Fishing) != 0;

        /// <summary>True if this outpost def is ranching. Ranches use Animals skill and farming fertility scaling.</summary>
        public static bool IsRanchOutpost(WorldObjectDef def) =>
            (GetTypeFlags(def) & OutpostTypeFlags.Ranch) != 0;

        /// <summary>True if this outpost def is mining.</summary>
        public static bool IsMiningOutpost(WorldObjectDef def) =>
            (GetTypeFlags(def) & OutpostTypeFlags.Mining) != 0;

        /// <summary>True if this outpost def is recruiting (special type: pawns per 10 Social).</summary>
        public static bool IsRecruitingOutpost(WorldObjectDef def) =>
            (GetTypeFlags(def) & OutpostTypeFlags.Recruiting) != 0;

        /// <summary>True if this outpost def is trading (special type: silver from nearby tiers).</summary>
        public static bool IsTradingOutpost(WorldObjectDef def) =>
            (GetTypeFlags(def) & OutpostTypeFlags.Trading) != 0;

        /// <summary>True if this outpost def is an embassy (goodwill from nearby eligible settlements).</summary>
        public static bool IsEmbassyOutpost(WorldObjectDef def) =>
            (GetTypeFlags(def) & OutpostTypeFlags.Embassy) != 0;

        /// <summary>True if this outpost def is scavenging (random reward bundle; VOE-style).</summary>
        public static bool IsScavengingOutpost(WorldObjectDef def) =>
            (GetTypeFlags(def) & OutpostTypeFlags.Scavenging) != 0;

        /// <summary>True if this outpost def is a mortar outpost (long-range strikes; no production).</summary>
        public static bool IsMortarOutpost(WorldObjectDef def) =>
            (GetTypeFlags(def) & OutpostTypeFlags.Mortar) != 0;

        public static bool IsWarehouseOutpost(WorldObjectDef def) =>
            (GetTypeFlags(def) & OutpostTypeFlags.Warehouse) != 0;

        /// <summary>True when this outpost assigns cumulative skill to physical-goods production. Controlled by <see cref="OutpostDefExtension.usesPhysicalGoodsProductionSkill"/> (default true).</summary>
        public static bool UsesPhysicalGoodsProductionSkill(WorldObjectDef def)
        {
            var ext = def?.GetModExtension<OutpostDefExtension>();
            return ext == null || ext.usesPhysicalGoodsProductionSkill;
        }

        /// <summary>True if this outpost def is a rapid response outpost (virtual counter-caravans and drop-pod dispatch).</summary>
        public static bool IsRapidResponseOutpost(WorldObjectDef def) =>
            (GetTypeFlags(def) & OutpostTypeFlags.RapidResponse) != 0;

        /// <summary>True when the def extension configures academy XP (&gt; 0 base XP/day). Not keyed off defName.</summary>
        public static bool IsAcademyOutpost(WorldObjectDef def)
        {
            var ext = def?.GetModExtension<OutpostDefExtension>();
            return ext != null && ext.academyBaseXpPerDay > 0f;
        }

        /// <summary>Academy extension values; returns false if not an academy outpost.</summary>
        public static bool TryGetAcademyExtension(WorldObjectDef def, out OutpostDefExtension ext)
        {
            ext = def?.GetModExtension<OutpostDefExtension>();
            if (ext == null || ext.academyBaseXpPerDay <= 0f)
            {
                ext = null;
                return false;
            }
            return true;
        }

        /// <summary>True when the def extension configures research efficiency (&gt; 0). Not keyed off defName.</summary>
        public static bool IsResearchOutpost(WorldObjectDef def)
        {
            var ext = def?.GetModExtension<OutpostDefExtension>();
            return ext != null && ext.researchEfficiencyFraction > 0f;
        }

        /// <summary>True when the def extension configures remote colony power (&gt; 0). Not keyed off defName.</summary>
        public static bool IsPowerPlantOutpost(WorldObjectDef def)
        {
            var ext = def?.GetModExtension<OutpostDefExtension>();
            return ext != null && ext.remotePowerWatts > 0f;
        }

        /// <summary>Power plant extension values; returns false if not a power plant outpost.</summary>
        public static bool TryGetPowerPlantExtension(WorldObjectDef def, out OutpostDefExtension ext)
        {
            ext = def?.GetModExtension<OutpostDefExtension>();
            if (ext == null || ext.remotePowerWatts <= 0f)
            {
                ext = null;
                return false;
            }
            return true;
        }

        /// <summary>Research extension values; returns false if not a research outpost.</summary>
        public static bool TryGetResearchExtension(WorldObjectDef def, out OutpostDefExtension ext)
        {
            ext = def?.GetModExtension<OutpostDefExtension>();
            if (ext == null || ext.researchEfficiencyFraction <= 0f)
            {
                ext = null;
                return false;
            }
            return true;
        }

        /// <summary>True if this outpost def produces food for logistics (farming, hunting, or ranching).</summary>
        public static bool IsFoodProducerOutpost(WorldObjectDef def) =>
            (GetTypeFlags(def) & (OutpostTypeFlags.Farming | OutpostTypeFlags.Hunting | OutpostTypeFlags.Ranch | OutpostTypeFlags.Fishing)) != 0;

        /// <summary>True when the def XML lists <c>productionOptions</c> (fabrication-style output without relying on defName keywords).</summary>
        public static bool HasXmlProductionOptions(WorldObjectDef def)
        {
            var ext = def?.GetModExtension<OutpostDefExtension>();
            return ext?.productionOptions != null && ext.productionOptions.Count > 0;
        }

        /// <summary>True if this outpost def is fabrication/simple production (fixed item, baseline x skill). Excludes recruiting and trading (they are special types).</summary>
        public static bool IsProductionOrTradingOutpost(WorldObjectDef def)
        {
            if (IsAcademyOutpost(def)) return false;
            if (IsResearchOutpost(def)) return false;
            if (IsPowerPlantOutpost(def)) return false;
            if (IsRapidResponseOutpost(def)) return false;
            var f = GetTypeFlags(def);
            if ((f & (OutpostTypeFlags.Recruiting | OutpostTypeFlags.Trading | OutpostTypeFlags.Embassy)) != 0) return false;
            if (HasXmlProductionOptions(def)) return true;
            return (f & (OutpostTypeFlags.Fabrication | OutpostTypeFlags.Production | OutpostTypeFlags.SimpleProduction)) != 0;
        }

        public static float ClampedProductionTimeMultiplier()
        {
            return Mathf.Clamp(WorldDominationMod.settings?.outpostProductionTimeMultiplier ?? WorldDominationSettings.DefOutpostProductionTimeMultiplier, 0.01f, 4f);
        }

        public static float ClampedProductionOutputMultiplier()
        {
            return Mathf.Clamp(WorldDominationMod.settings?.outpostProductionOutputMultiplier ?? WorldDominationSettings.DefOutpostProductionOutputMultiplier, 0.01f, 4f);
        }

        /// <summary>Global production output multiplier plus flat upgrade / expert / warehouse percentage points for this outpost.</summary>
        public static float GetEffectiveProductionOutputMultiplier(WorldObject_WD_Outpost outpost)
        {
            float m = ClampedProductionOutputMultiplier();
            if (outpost != null)
            {
                m += outpost.GetProductionUpgradeEfficiencyBonus();
                if (OutpostExpertUtility.OutpostHasProductionBonusPath(outpost))
                    m += OutpostExpertUtility.GetCombinedProductionBonus(outpost);
                m += OutpostWarehouseAuraUtility.GetBestWarehouseAuraBonus(outpost);
            }
            return Mathf.Clamp(m, 0.01f, 4f);
        }

        private static string ProductionFormulaTag(string key, string fallback)
        {
            string s = key.Translate().ToString();
            return s == key || s.Contains("TSA_WD_") ? fallback : s;
        }

        /// <summary>Suffix for production preview formulas, e.g. " × 1.26 (Expert Bonus)". Entertainer and Cook share one additive factor.</summary>
        public static string BuildProductionOutputFactorSuffix(WorldObject_WD_Outpost outpost)
        {
            var sb = new System.Text.StringBuilder();
            AppendProductionOutputFactorSuffix(sb, outpost);
            return sb.ToString();
        }

        public static void AppendProductionOutputFactorSuffix(System.Text.StringBuilder sb, WorldObject_WD_Outpost outpost)
        {
            float global = ClampedProductionOutputMultiplier();
            if (Mathf.Abs(global - 1f) > 0.02f)
                sb.Append(" × ").Append(global.ToString("F2")).Append(" ")
                    .Append(ProductionFormulaTag("TSA_WD_Production_Formula_GlobalOutput", "(Global Output)"));

            if (outpost == null) return;

            float upgrade = outpost.GetProductionUpgradeEfficiencyBonus();
            if (upgrade > 0.001f)
                sb.Append(" × ").Append((1f + upgrade).ToString("F2")).Append(" ")
                    .Append(ProductionFormulaTag("TSA_WD_Production_Formula_UpgradeBonus", "(Upgrade Bonus)"));

            if (OutpostExpertUtility.OutpostHasProductionBonusPath(outpost))
            {
                // Entertainer + Cook share one additive production multiplier (same as GetEffectiveProductionOutputMultiplier).
                float experts = OutpostExpertUtility.GetCombinedProductionBonus(outpost);
                if (experts > 0.001f)
                    sb.Append(" × ").Append((1f + experts).ToString("F2")).Append(" ")
                        .Append(ProductionFormulaTag("TSA_WD_Production_Formula_ExpertBonus", "(Expert Bonus)"));
            }

            float warehouse = OutpostWarehouseAuraUtility.GetBestWarehouseAuraBonus(outpost);
            if (warehouse > 0.001f)
                sb.Append(" × ").Append((1f + warehouse).ToString("F2")).Append(" ")
                    .Append(ProductionFormulaTag("TSA_WD_Production_Formula_WarehouseBonus", "(Warehouse Bonus)"));
        }

        /// <summary>Suffix for soft bonuses (virtual food / academy XP): experts + warehouse only.</summary>
        public static string BuildSoftProductionBonusSuffix(WorldObject_WD_Outpost outpost)
        {
            var sb = new System.Text.StringBuilder();
            AppendSoftProductionBonusSuffix(sb, outpost);
            return sb.ToString();
        }

        public static void AppendSoftProductionBonusSuffix(System.Text.StringBuilder sb, WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return;
            if (OutpostExpertUtility.OutpostHasProductionBonusPath(outpost))
            {
                float experts = OutpostExpertUtility.GetCombinedProductionBonus(outpost);
                if (experts > 0.001f)
                    sb.Append(" × ").Append((1f + experts).ToString("F2")).Append(" ")
                        .Append(ProductionFormulaTag("TSA_WD_Production_Formula_ExpertBonus", "(Expert Bonus)"));
            }

            float warehouse = OutpostWarehouseAuraUtility.GetBestWarehouseAuraBonus(outpost);
            if (warehouse > 0.001f)
                sb.Append(" × ").Append((1f + warehouse).ToString("F2")).Append(" ")
                    .Append(ProductionFormulaTag("TSA_WD_Production_Formula_WarehouseBonus", "(Warehouse Bonus)"));
        }

        /// <summary>Tooltip explaining global, upgrade, expert, and warehouse output factors on production formulas.</summary>
        public static string BuildProductionOutputFactorTooltip(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return "";

            float global = ClampedProductionOutputMultiplier();
            float upgrade = outpost.GetProductionUpgradeEfficiencyBonus();
            float entertainer = 0f;
            float cook = 0f;
            if (OutpostExpertUtility.OutpostHasProductionBonusPath(outpost))
            {
                entertainer = OutpostExpertUtility.GetEntertainerProductionBonus(outpost);
                cook = OutpostExpertUtility.GetCookProductionBonus(outpost);
            }
            float experts = entertainer + cook;
            float warehouse = OutpostWarehouseAuraUtility.GetBestWarehouseAuraBonus(outpost);
            string warehouseName = "";
            if (OutpostWarehouseAuraUtility.TryGetBestWarehouseAura(outpost, out var whSrc, out _) && whSrc != null)
                warehouseName = whSrc.Name ?? whSrc.LabelCap ?? "";
            bool hasGlobal = Mathf.Abs(global - 1f) > 0.02f;
            bool hasUpgrade = upgrade > 0.001f;
            bool hasExperts = experts > 0.001f;
            bool hasWarehouse = warehouse > 0.001f;
            if (!hasGlobal && !hasUpgrade && !hasExperts && !hasWarehouse) return "";

            var sb = new System.Text.StringBuilder();
            int factorCount = (hasGlobal ? 1 : 0) + (hasUpgrade ? 1 : 0) + (hasExperts ? 1 : 0) + (hasWarehouse ? 1 : 0);
            if (factorCount > 1)
            {
                float effective = GetEffectiveProductionOutputMultiplier(outpost);
                sb.AppendLine("TSA_WD_Production_Formula_OutputBonusCombinedTip".Translate(
                    effective.ToString("F2"),
                    global.ToString("F2"),
                    Mathf.RoundToInt(upgrade * 100f).ToString(),
                    Mathf.RoundToInt(experts * 100f).ToString(),
                    Mathf.RoundToInt(warehouse * 100f).ToString()));
            }
            else if (hasGlobal)
            {
                sb.AppendLine("TSA_WD_Production_Formula_GlobalOutputTip".Translate(global.ToString("F2")));
            }
            else if (hasUpgrade)
            {
                sb.AppendLine("TSA_WD_Production_Formula_UpgradeBonusTip".Translate(Mathf.RoundToInt(upgrade * 100f).ToString()));
            }
            else if (hasWarehouse)
            {
                sb.AppendLine(BuildWarehouseAuraBonusTip(warehouse, warehouseName));
            }

            if (entertainer > 0.001f)
            {
                string expertTip = OutpostExpertUtility.BuildExpertContributionTooltip(
                    outpost, OutpostExpertRole.Entertainer, entertainer);
                if (!string.IsNullOrEmpty(expertTip))
                {
                    if (sb.Length > 0) sb.AppendLine();
                    sb.Append(expertTip);
                }
            }

            if (cook > 0.001f)
            {
                string expertTip = OutpostExpertUtility.BuildExpertContributionTooltip(
                    outpost, OutpostExpertRole.Cook, cook);
                if (!string.IsNullOrEmpty(expertTip))
                {
                    if (sb.Length > 0) sb.AppendLine();
                    sb.Append(expertTip);
                }
            }

            if (hasWarehouse && factorCount > 1)
            {
                if (sb.Length > 0) sb.AppendLine();
                sb.Append(BuildWarehouseAuraBonusTip(warehouse, warehouseName));
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>Expert + warehouse tip for soft multipliers (virtual food / academy XP).</summary>
        public static string BuildSoftProductionBonusTooltip(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return "";

            float entertainer = 0f;
            float cook = 0f;
            if (OutpostExpertUtility.OutpostHasProductionBonusPath(outpost))
            {
                entertainer = OutpostExpertUtility.GetEntertainerProductionBonus(outpost);
                cook = OutpostExpertUtility.GetCookProductionBonus(outpost);
            }
            float experts = entertainer + cook;
            float warehouse = OutpostWarehouseAuraUtility.GetBestWarehouseAuraBonus(outpost);
            string warehouseName = "";
            if (OutpostWarehouseAuraUtility.TryGetBestWarehouseAura(outpost, out var whSrc, out _) && whSrc != null)
                warehouseName = whSrc.Name ?? whSrc.LabelCap ?? "";
            if (experts <= 0.001f && warehouse <= 0.001f) return "";

            var sb = new System.Text.StringBuilder();
            if (experts > 0.001f && warehouse > 0.001f)
            {
                float soft = OutpostWarehouseAuraUtility.GetSoftProductionBonusMultiplier(outpost);
                sb.AppendLine("TSA_WD_Production_Formula_SoftBonusCombinedTip".Translate(
                    soft.ToString("F2"),
                    Mathf.RoundToInt(experts * 100f).ToString(),
                    Mathf.RoundToInt(warehouse * 100f).ToString()));
            }

            if (entertainer > 0.001f)
            {
                string expertTip = OutpostExpertUtility.BuildExpertContributionTooltip(
                    outpost, OutpostExpertRole.Entertainer, entertainer);
                if (!string.IsNullOrEmpty(expertTip))
                {
                    if (sb.Length > 0) sb.AppendLine();
                    sb.Append(expertTip);
                }
            }

            if (cook > 0.001f)
            {
                string expertTip = OutpostExpertUtility.BuildExpertContributionTooltip(
                    outpost, OutpostExpertRole.Cook, cook);
                if (!string.IsNullOrEmpty(expertTip))
                {
                    if (sb.Length > 0) sb.AppendLine();
                    sb.Append(expertTip);
                }
            }

            if (warehouse > 0.001f)
            {
                if (sb.Length > 0) sb.AppendLine();
                sb.Append(BuildWarehouseAuraBonusTip(warehouse, warehouseName));
            }

            return sb.ToString().TrimEnd();
        }

        private static string BuildWarehouseAuraBonusTip(float warehouseFraction, string warehouseName)
        {
            string pct = Mathf.RoundToInt(warehouseFraction * 100f).ToString();
            if (string.IsNullOrEmpty(warehouseName))
            {
                string key = "TSA_WD_Production_Formula_WarehouseBonusTip";
                string t = key.Translate(pct).ToString();
                if (t == key || t.Contains("TSA_WD_"))
                    t = "Warehouse productivity aura: +" + pct + "%.";
                return t;
            }

            string namedKey = "TSA_WD_Production_Formula_WarehouseBonusNamedTip";
            string named = namedKey.Translate(pct, warehouseName).ToString();
            if (named == namedKey || named.Contains("TSA_WD_"))
                named = "Warehouse productivity aura from " + warehouseName + ": +" + pct + "%.";
            return named;
        }

        public static int ScaleOutputStackCount(int baseCount)
        {
            return ScaleOutputStackCount(baseCount, null);
        }

        public static int ScaleOutputStackCount(int baseCount, WorldObject_WD_Outpost outpost)
        {
            if (baseCount <= 0) return 0;
            return Mathf.Max(0, Mathf.RoundToInt(baseCount * GetEffectiveProductionOutputMultiplier(outpost)));
        }

        public static void ApplyOutputMultiplierToDeliveryItems(List<ThingDefCountClass> items)
        {
            ApplyOutputMultiplierToDeliveryItems(items, null);
        }

        public static void ApplyOutputMultiplierToDeliveryItems(List<ThingDefCountClass> items, WorldObject_WD_Outpost outpost)
        {
            if (items == null || items.Count == 0) return;
            float m = GetEffectiveProductionOutputMultiplier(outpost);
            for (int i = 0; i < items.Count; i++)
            {
                var tc = items[i];
                if (tc.thingDef == null) continue;
                tc.count = Mathf.Max(0, Mathf.RoundToInt(tc.count * m));
                items[i] = tc;
            }
        }

        /// <summary>Cycle days from outpost def XML or default (before production time multiplier). Used for timers only—silver budget per skill is not scaled by cycle length.</summary>
        public static float GetProductionCycleDaysBase(WorldObject_WD_Outpost outpost)
        {
            if (outpost?.def == null)
                return Outpost_Baselines.ReferenceCycleDays;
            var ext = outpost.def.GetModExtension<OutpostDefExtension>();
            if (ext != null && ext.productionCycleDays > 0f)
                return ext.productionCycleDays;
            return WorldDominationSettings.DefOutpostProductionTicksInterval / 60000f;
        }

        /// <summary>Actual calendar length until next delivery: def/default cycle × production time multiplier (matches world timer).</summary>
        public static float GetProductionCycleDays(WorldObject_WD_Outpost outpost)
        {
            if (outpost?.def != null && IsAcademyOutpost(outpost.def))
                return GetProductionCycleDaysBase(outpost);
            return GetProductionCycleDaysBase(outpost) * ClampedProductionTimeMultiplier();
        }

        /// <summary>Production cycle ticks (matches WorldObject_WD_Outpost timer).</summary>
        public static int GetProductionTicksInterval(WorldObjectDef def)
        {
            float tm = (def != null && IsAcademyOutpost(def)) ? 1f : ClampedProductionTimeMultiplier();
            var ext = def?.GetModExtension<OutpostDefExtension>();
            if (ext != null && ext.productionCycleDays > 0f)
                return Mathf.Max(1, (int)(ext.productionCycleDays * 60000f * tm));
            return Mathf.Max(1, (int)(WorldDominationSettings.DefOutpostProductionTicksInterval * tm));
        }

        /// <summary>Baseline silver value per skill per cycle (from mod settings). Used in tooltips; actual quantities are derived from market value in Outpost_Baselines.</summary>
        public static float GetBaselineOutputPerSkill()
        {
            return Outpost_Baselines.GetReferenceSilverPerSkillPerCycle();
        }

        /// <summary>Production/trading: options from def productionOptions.</summary>
        public static List<ProductionOption> GetProductionOptions(WorldObject_WD_Outpost outpost)
        {
            return outpost?.def?.GetModExtension<OutpostDefExtension>()?.productionOptions;
        }

        /// <summary>Vanilla-style <c>MayRequire</c> / <c>MayRequireAnyOf</c> for <see cref="ProductionOption"/> (comma-separated packageIds). Used because def-extension list entries may not always be stripped by the XML pipeline.</summary>
        public static bool ProductionOptionPassesMayRequire(ProductionOption option)
        {
            if (option == null) return false;
            if (!string.IsNullOrWhiteSpace(option.MayRequire))
            {
                foreach (var raw in option.MayRequire.Split(','))
                {
                    string id = raw.Trim();
                    if (string.IsNullOrEmpty(id)) continue;
                    if (!ModPackageActive(id)) return false;
                }
            }

            if (!string.IsNullOrWhiteSpace(option.MayRequireAnyOf))
            {
                bool any = false;
                foreach (var raw in option.MayRequireAnyOf.Split(','))
                {
                    string id = raw.Trim();
                    if (string.IsNullOrEmpty(id)) continue;
                    if (ModPackageActive(id))
                    {
                        any = true;
                        break;
                    }
                }
                if (!any) return false;
            }

            return true;
        }

        private static bool ModPackageActive(string packageId)
        {
            if (ModsConfig.IsActive(packageId)) return true;
            // Local vs Steam duplicate packageId quirk (see RimWorld wiki / MayRequire).
            if (!packageId.EndsWith("_steam", StringComparison.OrdinalIgnoreCase) && ModsConfig.IsActive(packageId + "_steam"))
                return true;
            return false;
        }

        /// <summary>Production option for this item (by thingDef.defName). Null if not in def's productionOptions.</summary>
        public static ProductionOption GetProductionOption(WorldObject_WD_Outpost outpost, ThingDef product)
        {
            if (outpost?.def == null || product == null) return null;
            var opts = GetProductionOptions(outpost);
            if (opts == null) return null;
            string name = product.defName;
            foreach (var opt in opts)
            {
                if (!ProductionOptionPassesMayRequire(opt)) continue;
                if (opt?.thingDef == name) return opt;
            }
            return null;
        }

        /// <summary>True if research required by this option is completed (or no research required).</summary>
        public static bool IsResearchDoneForOption(ProductionOption option)
        {
            if (option == null || string.IsNullOrEmpty(option.requiredResearch)) return true;
            var project = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(option.requiredResearch);
            if (project == null) return true;
            if (Find.ResearchManager == null) return false;
            return Find.ResearchManager.GetProgress(project) >= project.baseCost;
        }

        /// <summary>Sum of <paramref name="skillDef"/> across virtual pawns + mechs (raw, for min-skill checks).</summary>
        private static float SumVirtualPawnSkillRaw(WorldObject_WD_Outpost outpost, SkillDef skillDef)
        {
            if (outpost == null || skillDef == null) return 0f;
            return outpost.SumVirtualPawnSkillRaw(skillDef);
        }

        /// <summary>Effective cumulative scaling skill for production math.</summary>
        private static float SumVirtualPawnSkill(WorldObject_WD_Outpost outpost, SkillDef skillDef)
        {
            if (outpost == null || skillDef == null) return 0f;
            return outpost.SumVirtualPawnSkill(skillDef);
        }

        /// <summary>Total scaling skill for UI “what-if” formulas: always the outpost cumulative skill for this option’s scaling skill. Ignores research and per-option min skill (Select stays disabled; tooltips state requirements).</summary>
        public static float GetScalingSkillTotalForProductionPreview(WorldObject_WD_Outpost outpost, ProductionOption option)
        {
            if (outpost == null || option == null) return 0f;
            SkillDef skillDef = GetScalingSkillDefForProduction(outpost, option);
            return SumVirtualPawnSkill(outpost, skillDef);
        }

        /// <summary>If research is done and at least one pawn has skill &gt;= option.minSkillLevel, returns effective total scaling skill; otherwise 0. Used for actual delivery capacity.</summary>
        public static float GetEligibleSkillForProduction(WorldObject_WD_Outpost outpost, ProductionOption option)
        {
            if (outpost?.VirtualPawns == null || option == null) return 0f;
            if (!IsResearchDoneForOption(option)) return 0f;
            SkillDef skillDef = GetScalingSkillDefForProduction(outpost, option);
            if (skillDef == null) return 0f;
            if (option.minSkillLevel > 0)
            {
                bool anyMeetsMin = false;
                var vp = outpost.VirtualPawns;
                for (int i = 0; i < vp.Count; i++)
                {
                    if (vp[i].GetSkill(skillDef) >= option.minSkillLevel)
                    { anyMeetsMin = true; break; }
                }
                if (!anyMeetsMin) return 0f;
            }
            return SumVirtualPawnSkill(outpost, skillDef);
        }

        /// <summary>Whether at least one pawn meets minSkillLevel and research is done (for production/trading option).</summary>
        public static bool OutpostCanProduceItem(WorldObject_WD_Outpost outpost, ThingDef product)
        {
            var option = GetProductionOption(outpost, product);
            if (option == null) return false;
            if (!IsResearchDoneForOption(option)) return false;
            SkillDef skillDef = GetScalingSkillDefForProduction(outpost, option);
            if (skillDef == null) return false;
            if (option.minSkillLevel <= 0) return true;
            if (outpost.VirtualPawns == null) return false;
            var vpList = outpost.VirtualPawns;
            for (int i = 0; i < vpList.Count; i++)
            {
                if (vpList[i].GetSkill(skillDef) >= option.minSkillLevel)
                    return true;
            }
            return false;
        }

        /// <summary>Format delivery items as "120 bird meat + 120 bird skin".</summary>
        public static string FormatDeliveryProductLine(List<ThingDefCountClass> items)
        {
            if (items == null || items.Count == 0) return null;
            var parts = new List<string>();
            foreach (var tc in items)
                if (tc?.thingDef != null) parts.Add(tc.count + " " + tc.thingDef.LabelCap);
            return parts.Count > 0 ? string.Join(" + ", parts) : null;
        }

        /// <summary>Format math line: "10 Plants Skill * 30 Corn = 300 Corn".</summary>
        public static string FormatSkillOutputLine(float capacity, string skillName, float outputPerSkill, string productLabel, float totalOutput)
        {
            string key = "TSA_WD_Production_SkillOutput";
            string result = key.Translate(capacity.ToString("F0"), skillName, outputPerSkill.ToString("F0"), productLabel, totalOutput.ToString("F0")).ToString();
            if (result == key || result.Contains("TSA_WD_"))
                return capacity.ToString("F0") + " " + skillName + " Skill * " + outputPerSkill.ToString("F0") + " " + productLabel + " = " + totalOutput.ToString("F0") + " " + productLabel;
            return result;
        }

        /// <summary>Tooltip for the skill factor: cumulative skill at the outpost.</summary>
        public static string GetSkillFactorTooltip(string skillName)
        {
            return "TSA_WD_Production_SkillFactorTooltip".Translate(skillName);
        }

        public static SettlementTier GuessTierFromPawnCount(int pawnCount)
        {
            if (pawnCount >= 20) return SettlementTier.T4;
            if (pawnCount >= 12) return SettlementTier.T3;
            if (pawnCount >= 7) return SettlementTier.T2;
            return SettlementTier.T1;
        }

        /// <summary>World-tile farming fertility for UI (0–150%+ by mutators), same as outpost selection / establishment dialogs.</summary>
        public static int GetFarmingFertilityPercentInt(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return 0;
            float b = outpost.GetBuiltUpgradeTileFertilityBonus();
            return Mathf.RoundToInt(Mathf.Clamp(WorldTileProductivity.GetFarmingFertilityScore(outpost.Tile, b), 0f, WorldTileProductivity.ProductivityScoreCap) * 100f);
        }

        /// <summary>
        /// World-tile fertility multiplier for farming output (0–<see cref="WorldTileProductivity.ProductivityScoreCap"/>).
        /// Fertility and upgrade bonuses only — not average skill or settlement tier.
        /// </summary>
        public static float GetFarmingTileProductionFactor(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return 1f;
            int tick = Find.TickManager?.TicksGame ?? 0;
            int id = outpost.ID;
            int tile = outpost.Tile;
            float fertBonus = outpost.GetBuiltUpgradeTileFertilityBonus();
            if (farmingTileFactorCache.TryGetValue(id, out FarmingTileFactorCacheEntry c)
                && c.tick == tick
                && c.tile == tile
                && Mathf.Approximately(c.fertBonus, fertBonus))
                return c.value;

            float factor = Mathf.Clamp(
                WorldTileProductivity.GetFarmingFertilityScore(tile, fertBonus),
                0f,
                WorldTileProductivity.ProductivityScoreCap);
            farmingTileFactorCache[id] = new FarmingTileFactorCacheEntry
            {
                tick = tick,
                tile = tile,
                fertBonus = fertBonus,
                value = factor
            };
            return factor;
        }

        /// <summary>
        /// Ranches use the same fertility tile multiplier as farming (Animals skill is the capacity axis, not the tile).
        /// </summary>
        public static float GetRanchTileProductionFactor(WorldObject_WD_Outpost outpost)
            => GetFarmingTileProductionFactor(outpost);

        /// <summary>Hunting efficiency = animal abundance for this tile (0–<see cref="WorldTileProductivity.ProductivityScoreCap"/>). Display as percent. Same value as shown in the dialog header.</summary>
        public static float GetHuntingTileProductionFactor(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return 0f;
            return Mathf.Clamp(
                WorldTileProductivity.GetHuntingScore(outpost.Tile, outpost.GetBuiltUpgradeTileAnimalAbundanceBonus()),
                0f,
                WorldTileProductivity.ProductivityScoreCap);
        }

        /// <summary>Fishing efficiency = fish abundance for this coastal tile (0–<see cref="WorldTileProductivity.ProductivityScoreCap"/>).</summary>
        public static float GetFishingTileProductionFactor(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return 0f;
            return Mathf.Clamp(
                WorldTileProductivity.GetFishingScore(outpost.Tile, outpost.GetBuiltUpgradeTileFishAbundanceBonus()),
                0f,
                WorldTileProductivity.ProductivityScoreCap);
        }

        /// <summary>Tile efficiency multiplier for mining (0–<see cref="WorldTileProductivity.ProductivityScoreCap"/> by mutators). Hilliness baseline plus flat offsets.</summary>
        public static float GetMiningTileProductionFactor(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return 0f;
            return WorldTileProductivity.GetMiningOutputMultiplier(outpost.Tile, outpost.GetBuiltUpgradeTileMiningBonus());
        }

        public static void AddIfExists(List<ThingDef> list, string defName)
        {
            ThingDef t = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (t != null) list.Add(t);
        }

        /// <summary>Player settlement tiles (MapParent with map, no logistics outpost comp). Matches food logistics tab.</summary>
        public static HashSet<int> GetColonyTiles()
        {
            var mgr = Find.World?.GetComponent<WorldComponent_LogisticsManager>();
            if (mgr != null)
                return mgr.GetColonyTilesCached();
            var set = new HashSet<int>();
            if (Find.WorldObjects == null) return set;
            foreach (var o in Find.WorldObjects.AllWorldObjects)
            {
                if (o is MapParent mp && mp.HasMap && o.Faction == Faction.OfPlayer && o.GetComponent<CompOutpostLogistics>() == null)
                    set.Add(o.Tile);
            }
            return set;
        }

        /// <summary>
        /// Skill available for physical-goods production: the outpost's full cumulative relevant skill.
        /// Decoupled from virtual-food logistics — routing surplus food to other outposts no longer reduces this.
        /// </summary>
        public static float GetSkillAssignedToPhysicalProduction(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return 0f;
            return outpost.GetTotalRelevantSkill();
        }
    }
}
