using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Sortable columns in <see cref="WITab_Outpost_Pawns"/>.</summary>
    public enum OutpostPawnTableSortColumn
    {
        /// <summary>Default: type category (Human→Animal→Mechanoid→Vehicle) then name.</summary>
        Default,
        PawnType,
        Name,
        Starred,
        Resistance,
        Traits,
        Xenotype,
        Psycasts,
        Age,
        Shooting,
        Melee,
        RelevantCombined,
        Construction,
        Strength,
        DailyFood,
        Hurt,
        RelevantXp,
        Plants,
        Animals,
        Social,
        Mining,
        Crafting,
        Intellectual,
        Cooking,
        Medicine,
        Artistic
    }

    /// <summary>Star filter on the outpost Pawns tab (subset of the global roster filters).</summary>
    public enum OutpostPawnStarFilter
    {
        All = 0,
        Starred = 1,
        NotStarred = 2
    }

    /// <summary>Serializable snapshot of a pawn used for outpost strength and logistics. No ticking, no needs on map; biological age can advance via outpost progression. Inventory is frozen and restored when pawn is removed.</summary>
    public class VirtualPawnSummary : IExposable
    {
        // Direct reference to the real pawn when frozen. Preferred source of truth for removal/UI.
        public Pawn pawn;
        public string name = "";
        public int shooting = 0;
        public int melee = 0;
        public int plants = 0;
        public int animals = 0;
        public int construction = 0;
        public int social = 0;
        public int mining = 0;
        public int crafting = 0;
        public int intellectual = 0;
        public int cooking = 0;
        public int medicine = 0;
        public int artistic = 0;
        public float healthFactor = 1f;
        /// <summary>Biological age in years (for table display/sort). From <see cref="Pawn_AgeTracker.AgeBiologicalYearsFloat"/> when available.</summary>
        public float biologicalAgeYears;
        /// <summary>Frozen snapshot of pawn inventory (def + count). Restored when pawn is removed as caravan.</summary>
        public List<ThingDefCountClass> inventory;

        public VirtualPawnSummary() { }

        /// <summary>
        /// Effective skill for outpost math/UI: prefers <see cref="SkillRecord.Level"/> (includes gene offsets, etc.);
        /// falls back to <see cref="SkillRecord.levelInt"/> if <c>Level</c> throws (rare incomplete load / bad state).
        /// Persisted fields are still ints — we do not call this during <see cref="ExposeData"/>.
        /// </summary>
        private static int SkillLevelSnapshot(SkillRecord sr)
        {
            if (sr == null) return 0;
            try
            {
                return WorldDominationMod.settings?.GetEffectiveOutpostSkillLevel(sr.Level) ?? Mathf.Max(0, sr.Level);
            }
            catch
            {
                return WorldDominationMod.settings?.GetEffectiveOutpostSkillLevel(sr.levelInt) ?? Mathf.Max(0, sr.levelInt);
            }
        }

        /// <summary>
        /// Vanilla <see cref="SummaryHealthHandler.SummaryHealthPercent"/> walks all hediffs; some mods (e.g. Combat Extended on
        /// <see cref="Hediff_MissingPart"/>) can throw or NRE while the pawn graph is still resolving during save load. Outposts must not fail to load.
        /// </summary>
        private static float HealthFactorSnapshot(Pawn pawn)
        {
            if (pawn?.health?.summaryHealth == null) return 1f;
            try
            {
                float p = pawn.health.summaryHealth.SummaryHealthPercent;
                if (float.IsNaN(p) || float.IsInfinity(p)) return 1f;
                return p;
            }
            catch
            {
                return 1f;
            }
        }

        private static float BiologicalAgeYearsSnapshot(Pawn pawn)
        {
            if (pawn?.ageTracker == null) return 0f;
            try
            {
                float y = pawn.ageTracker.AgeBiologicalYearsFloat;
                if (float.IsNaN(y) || float.IsInfinity(y)) return 0f;
                return y;
            }
            catch
            {
                return 0f;
            }
        }

        public static VirtualPawnSummary FromPawn(Pawn pawn)
        {
            if (pawn == null) return null;
            bool isMechanoid = pawn.RaceProps != null && pawn.RaceProps.IsMechanoid;
            var s = new VirtualPawnSummary();
            s.pawn = pawn;
            s.name = pawn.LabelShort ?? "Pawn";
            if (isMechanoid)
            {
                s.shooting = OutpostMechanoidSkillUtil.EquivalentSkillLevel(pawn, SkillDefOf.Shooting);
                s.melee = OutpostMechanoidSkillUtil.EquivalentSkillLevel(pawn, SkillDefOf.Melee);
                s.plants = OutpostMechanoidSkillUtil.EquivalentSkillLevel(pawn, SkillDefOf.Plants);
                s.animals = OutpostMechanoidSkillUtil.EquivalentSkillLevel(pawn, SkillDefOf.Animals);
                s.construction = OutpostMechanoidSkillUtil.EquivalentSkillLevel(pawn, SkillDefOf.Construction);
                s.social = OutpostMechanoidSkillUtil.EquivalentSkillLevel(pawn, SkillDefOf.Social);
                s.mining = OutpostMechanoidSkillUtil.EquivalentSkillLevel(pawn, SkillDefOf.Mining);
                s.crafting = OutpostMechanoidSkillUtil.EquivalentSkillLevel(pawn, SkillDefOf.Crafting);
                s.intellectual = OutpostMechanoidSkillUtil.EquivalentSkillLevel(pawn, SkillDefOf.Intellectual);
                s.cooking = OutpostMechanoidSkillUtil.EquivalentSkillLevel(pawn, SkillDefOf.Cooking);
                s.medicine = OutpostMechanoidSkillUtil.EquivalentSkillLevel(pawn, SkillDefOf.Medicine);
                s.artistic = OutpostMechanoidSkillUtil.EquivalentSkillLevel(pawn, SkillDefOf.Artistic);
            }
            else
            {
                s.shooting = SkillLevelSnapshot(pawn.skills?.GetSkill(SkillDefOf.Shooting));
                s.melee = SkillLevelSnapshot(pawn.skills?.GetSkill(SkillDefOf.Melee));
                s.plants = SkillLevelSnapshot(pawn.skills?.GetSkill(SkillDefOf.Plants));
                s.animals = SkillLevelSnapshot(pawn.skills?.GetSkill(SkillDefOf.Animals));
                s.construction = SkillLevelSnapshot(pawn.skills?.GetSkill(SkillDefOf.Construction));
                s.social = SkillLevelSnapshot(pawn.skills?.GetSkill(SkillDefOf.Social));
                s.mining = SkillLevelSnapshot(pawn.skills?.GetSkill(SkillDefOf.Mining));
                s.crafting = SkillLevelSnapshot(pawn.skills?.GetSkill(SkillDefOf.Crafting));
                s.intellectual = SkillLevelSnapshot(pawn.skills?.GetSkill(SkillDefOf.Intellectual));
                s.cooking = SkillLevelSnapshot(pawn.skills?.GetSkill(SkillDefOf.Cooking));
                s.medicine = SkillLevelSnapshot(pawn.skills?.GetSkill(SkillDefOf.Medicine));
                s.artistic = SkillLevelSnapshot(pawn.skills?.GetSkill(SkillDefOf.Artistic));
            }
            s.healthFactor = HealthFactorSnapshot(pawn);
            s.biologicalAgeYears = BiologicalAgeYearsSnapshot(pawn);
            try
            {
                s.inventory = SnapshotPawnInventory(pawn);
            }
            catch
            {
                s.inventory = new List<ThingDefCountClass>();
            }
            return s;
        }

        /// <summary>Snapshot pawn inventory as list of def + count (merged by def). No tick-based logic; just current state.</summary>
        public static List<ThingDefCountClass> SnapshotPawnInventory(Pawn pawn)
        {
            var list = new List<ThingDefCountClass>();
            if (pawn?.inventory?.innerContainer == null) return list;
            var byDef = new Dictionary<ThingDef, int>();
            foreach (Thing t in pawn.inventory.innerContainer)
            {
                if (t?.def == null) continue;
                int count = t.stackCount;
                if (count <= 0) count = 1;
                if (!byDef.ContainsKey(t.def)) byDef[t.def] = 0;
                byDef[t.def] += count;
            }
            foreach (var kv in byDef)
                list.Add(new ThingDefCountClass(kv.Key, kv.Value));
            return list;
        }

        /// <summary>Strength contribution for combat (same formula as legacy GetTargetOutpostStrength).</summary>
        public float CombatStrength
        {
            get
            {
                int maxSkill = Math.Max(shooting, melee);
                float baseStr = 50f + (maxSkill * 7.5f);
                return baseStr * healthFactor;
            }
        }

        /// <summary>For logistics production: plants (farming) or animals (hunting).</summary>
        public float ProductionSkill(bool forFarming)
        {
            return forFarming ? plants : animals;
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_Values.Look(ref name, "name", "");
            Scribe_Values.Look(ref shooting, "shooting", 0);
            Scribe_Values.Look(ref melee, "melee", 0);
            Scribe_Values.Look(ref plants, "plants", 0);
            Scribe_Values.Look(ref animals, "animals", 0);
            Scribe_Values.Look(ref construction, "construction", 0);
            Scribe_Values.Look(ref social, "social", 0);
            Scribe_Values.Look(ref mining, "mining", 0);
            Scribe_Values.Look(ref crafting, "crafting", 0);
            Scribe_Values.Look(ref intellectual, "intellectual", 0);
            Scribe_Values.Look(ref cooking, "cooking", 0);
            Scribe_Values.Look(ref medicine, "medicine", 0);
            Scribe_Values.Look(ref artistic, "artistic", 0);
            Scribe_Values.Look(ref healthFactor, "healthFactor", 1f);
            Scribe_Values.Look(ref biologicalAgeYears, "biologicalAgeYears", 0f);
            Scribe_Collections.Look(ref inventory, "inventory", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.LoadingVars && inventory == null)
                inventory = new List<ThingDefCountClass>();
        }

        /// <summary>Sum of levels for each skill in <see cref="WorldObject_WD_Outpost.GetRelevantSkillDefs"/> (matches one column in the pawns tab).</summary>
        public float GetRelevantSkillValue(WorldObjectDef outpostDef)
        {
            if (outpostDef == null) return 0f;
            var skills = Outpost_Production_Utils.GetCachedRelevantSkillDefs(outpostDef);
            if (skills == null || skills.Count == 0) return 0f;
            float sum = 0f;
            foreach (var sd in skills)
                sum += GetSkill(sd);
            return sum;
        }

        /// <inheritdoc cref="GetRelevantSkillValue(WorldObjectDef)"/>
        public float GetRelevantSkillValue(string outpostDefName)
        {
            if (string.IsNullOrEmpty(outpostDefName)) return 0f;
            string d = outpostDefName.ToLowerInvariant();
            if (d.Contains("farming")) return plants;
            if (d.Contains("hunting")) return animals;
            if (d.Contains("recruiting") || d.Contains("trading") || d.Contains("embassy") || d.Contains("town")) return social;
            if (d.Contains("mining")) return mining;
            if (d.Contains("fabrication") || d.Contains("production") || d.Contains("factory")) return crafting;
            var def = DefDatabase<WorldObjectDef>.GetNamedSilentFail(outpostDefName);
            return def != null ? GetRelevantSkillValue(def) : 0f;
        }

        public int GetSkill(SkillDef def)
        {
            if (def == null) return 0;
            if (def == SkillDefOf.Shooting) return shooting;
            if (def == SkillDefOf.Melee) return melee;
            if (def == SkillDefOf.Plants) return plants;
            if (def == SkillDefOf.Animals) return animals;
            if (def == SkillDefOf.Construction) return construction;
            if (def == SkillDefOf.Social) return social;
            if (def == SkillDefOf.Mining) return mining;
            if (def == SkillDefOf.Crafting) return crafting;
            if (def == SkillDefOf.Intellectual) return intellectual;
            if (def == SkillDefOf.Cooking) return cooking;
            if (def == SkillDefOf.Medicine) return medicine;
            if (def == SkillDefOf.Artistic) return artistic;
            return 0;
        }

        /// <summary>Vanilla-style progress to next skill level: current XP / XP required (live read from <paramref name="pawn"/>). One line per relevant skill, newline-separated.</summary>
        public static string FormatRelevantSkillsXpProgress(Pawn pawn, List<SkillDef> relevantDefs)
        {
            if (pawn?.skills == null || relevantDefs == null || relevantDefs.Count == 0) return "—";
            try
            {
                string a = FormatOneSkillXp(pawn.skills.GetSkill(relevantDefs[0]));
                if (relevantDefs.Count == 1) return a;
                for (int i = 1; i < relevantDefs.Count; i++)
                    a = a + "\n" + FormatOneSkillXp(pawn.skills.GetSkill(relevantDefs[i]));
                return a;
            }
            catch
            {
                return "—";
            }
        }

        private static string FormatOneSkillXp(SkillRecord rec)
        {
            if (rec == null || rec.TotallyDisabled) return "—";
            int lv;
            try
            {
                lv = WorldDominationMod.settings?.GetEffectiveOutpostSkillLevel(rec.Level) ?? Mathf.Max(0, rec.Level);
            }
            catch
            {
                lv = WorldDominationMod.settings?.GetEffectiveOutpostSkillLevel(rec.levelInt) ?? Mathf.Max(0, rec.levelInt);
            }
            if ((WorldDominationMod.settings?.clampOutpostSkillsAtLevel20 ?? WorldDominationSettings.DefClampOutpostSkillsAtLevel20) && lv >= 20) return "MAX";
            float need;
            try
            {
                need = rec.XpRequiredForLevelUp;
            }
            catch
            {
                return "—";
            }
            if (need <= 0f) return "—";
            int cur = Mathf.FloorToInt(rec.xpSinceLastLevel);
            int req = Mathf.RoundToInt(need);
            return cur + "/" + req;
        }

        /// <summary>Compare two rows for the outpost pawns table. Tie-breaker: name A→Z. Returns negative if <paramref name="a"/> should sort before <paramref name="b"/> when <paramref name="ascending"/> is true.</summary>
        public static int CompareForOutpostTableSort(VirtualPawnSummary a, VirtualPawnSummary b, OutpostPawnTableSortColumn column, WorldObject_WD_Outpost outpost, bool ascending)
        {
            int primary = ComparePrimaryForOutpostTable(a, b, column, outpost);
            if (primary != 0)
                return ascending ? primary : -primary;
            return string.Compare(a?.name ?? "", b?.name ?? "", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Skill column matching a full-skill / dedicated combat header, or null if the sort is not a skill level.</summary>
        public static SkillDef SkillDefForSortColumn(OutpostPawnTableSortColumn column)
        {
            switch (column)
            {
                case OutpostPawnTableSortColumn.Shooting: return SkillDefOf.Shooting;
                case OutpostPawnTableSortColumn.Melee: return SkillDefOf.Melee;
                case OutpostPawnTableSortColumn.Plants: return SkillDefOf.Plants;
                case OutpostPawnTableSortColumn.Animals: return SkillDefOf.Animals;
                case OutpostPawnTableSortColumn.Construction: return SkillDefOf.Construction;
                case OutpostPawnTableSortColumn.Social: return SkillDefOf.Social;
                case OutpostPawnTableSortColumn.Mining: return SkillDefOf.Mining;
                case OutpostPawnTableSortColumn.Crafting: return SkillDefOf.Crafting;
                case OutpostPawnTableSortColumn.Intellectual: return SkillDefOf.Intellectual;
                case OutpostPawnTableSortColumn.Cooking: return SkillDefOf.Cooking;
                case OutpostPawnTableSortColumn.Medicine: return SkillDefOf.Medicine;
                case OutpostPawnTableSortColumn.Artistic: return SkillDefOf.Artistic;
                default: return null;
            }
        }

        public static OutpostPawnTableSortColumn SortColumnForSkillDef(SkillDef skill)
        {
            if (skill == SkillDefOf.Shooting) return OutpostPawnTableSortColumn.Shooting;
            if (skill == SkillDefOf.Melee) return OutpostPawnTableSortColumn.Melee;
            if (skill == SkillDefOf.Plants) return OutpostPawnTableSortColumn.Plants;
            if (skill == SkillDefOf.Animals) return OutpostPawnTableSortColumn.Animals;
            if (skill == SkillDefOf.Construction) return OutpostPawnTableSortColumn.Construction;
            if (skill == SkillDefOf.Social) return OutpostPawnTableSortColumn.Social;
            if (skill == SkillDefOf.Mining) return OutpostPawnTableSortColumn.Mining;
            if (skill == SkillDefOf.Crafting) return OutpostPawnTableSortColumn.Crafting;
            if (skill == SkillDefOf.Intellectual) return OutpostPawnTableSortColumn.Intellectual;
            if (skill == SkillDefOf.Cooking) return OutpostPawnTableSortColumn.Cooking;
            if (skill == SkillDefOf.Medicine) return OutpostPawnTableSortColumn.Medicine;
            if (skill == SkillDefOf.Artistic) return OutpostPawnTableSortColumn.Artistic;
            return OutpostPawnTableSortColumn.Default;
        }

        /// <summary>Average relevant-skill progress (level + fraction to next). Missing skills sort as -1. MAX counts as level+1.</summary>
        public static float RelevantXpSortKey(Pawn pawn, List<SkillDef> relevantDefs)
        {
            if (pawn?.skills == null || relevantDefs == null || relevantDefs.Count == 0)
                return -1f;
            float sum = 0f;
            int counted = 0;
            bool clamp20 = WorldDominationMod.settings?.clampOutpostSkillsAtLevel20 ?? WorldDominationSettings.DefClampOutpostSkillsAtLevel20;
            for (int i = 0; i < relevantDefs.Count; i++)
            {
                SkillDef def = relevantDefs[i];
                if (def == null) continue;
                SkillRecord rec;
                try
                {
                    rec = pawn.skills.GetSkill(def);
                }
                catch
                {
                    continue;
                }
                if (rec == null || rec.TotallyDisabled) continue;
                int lv;
                try
                {
                    lv = WorldDominationMod.settings?.GetEffectiveOutpostSkillLevel(rec.Level) ?? Mathf.Max(0, rec.Level);
                }
                catch
                {
                    lv = WorldDominationMod.settings?.GetEffectiveOutpostSkillLevel(rec.levelInt) ?? Mathf.Max(0, rec.levelInt);
                }
                if (clamp20 && lv >= 20)
                {
                    sum += lv + 1f;
                    counted++;
                    continue;
                }
                float need;
                try
                {
                    need = rec.XpRequiredForLevelUp;
                }
                catch
                {
                    sum += lv;
                    counted++;
                    continue;
                }
                if (need <= 0f)
                    sum += lv;
                else
                    sum += lv + Mathf.Clamp01(rec.xpSinceLastLevel / need);
                counted++;
            }
            return counted == 0 ? -1f : sum / counted;
        }

        private static int ComparePrimaryForOutpostTable(VirtualPawnSummary a, VirtualPawnSummary b, OutpostPawnTableSortColumn column, WorldObject_WD_Outpost outpost)
        {
            if (a == null && b == null) return 0;
            if (a == null) return -1;
            if (b == null) return 1;
            SkillDef skill = SkillDefForSortColumn(column);
            if (skill != null)
                return a.GetSkill(skill).CompareTo(b.GetSkill(skill));
            switch (column)
            {
                case OutpostPawnTableSortColumn.Name:
                    return string.Compare(a.name ?? "", b.name ?? "", StringComparison.OrdinalIgnoreCase);
                case OutpostPawnTableSortColumn.Age:
                    return a.biologicalAgeYears.CompareTo(b.biologicalAgeYears);
                case OutpostPawnTableSortColumn.RelevantCombined:
                    return RelevantSkillSumForPawnsTab(a, outpost).CompareTo(RelevantSkillSumForPawnsTab(b, outpost));
                case OutpostPawnTableSortColumn.Strength:
                    return a.CombatStrength.CompareTo(b.CombatStrength);
                case OutpostPawnTableSortColumn.DailyFood:
                    return DailyFoodDemandForSort(a.pawn).CompareTo(DailyFoodDemandForSort(b.pawn));
                default:
                    return 0;
            }
        }

        private static float DailyFoodDemandForSort(Pawn pawn)
        {
            if (!OutpostPawnClassificationUtil.ConsumesVirtualFood(pawn)) return 0f;
            return WorldDominationMod.settings?.foodConsumptionPerPawn ?? WorldDominationSettings.DefFoodConsumptionPerPawn;
        }

        private static float RelevantSkillSumForPawnsTab(VirtualPawnSummary v, WorldObject_WD_Outpost outpost)
        {
            if (v == null || outpost == null) return 0f;
            var skills = WorldObject_WD_Outpost.GetRelevantSkillDefsForPawnsTab(outpost);
            if (skills == null || skills.Count == 0) return 0f;
            float sum = 0f;
            for (int i = 0; i < skills.Count; i++)
                sum += v.GetSkill(skills[i]);
            return sum;
        }
    }
}
