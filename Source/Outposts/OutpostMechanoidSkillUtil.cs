using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Maps mechanoid work-speed stats to equivalent outpost skill levels (Biotech mechs have no SkillRecords).</summary>
    public static class OutpostMechanoidSkillUtil
    {
        /// <summary>Each full skill level ≈ 16% intrinsic work speed (80% plant speed → 5).</summary>
        private const float WorkSpeedPercentPerSkillLevel = 0.16f;
        /// <summary>Dedicated worker mechs with an explicit specialty speed stat (e.g. constructoid) floor here when linear mapping is lower.</summary>
        private const int DedicatedWorkMechMinSkillLevel = 5;

        private static readonly HashSet<string> excludedWorkStatDefNames = new HashSet<string>
        {
            "HuntingStealth",
            "PlantHarvestYield",
            "DrugHarvestYield",
            "MiningYield",
            "ConstructSuccessChance",
            "PruningSpeed",
            "ReadingSpeed",
            "MechFormingSpeed",
            "MechRepairSpeed",
            "SubcoreEncodingSpeed"
        };

        private static readonly Dictionary<SkillDef, List<StatDef>> skillToWorkStats = new Dictionary<SkillDef, List<StatDef>>();
        private static bool workStatCacheBuilt;

        private static void EnsureWorkStatCache()
        {
            if (workStatCacheBuilt) return;
            workStatCacheBuilt = true;
            foreach (StatDef stat in DefDatabase<StatDef>.AllDefsListForReading)
            {
                if (stat.category != StatCategoryDefOf.PawnWork) continue;
                if (!IsWorkSpeedStat(stat)) continue;
                if (stat.skillNeedFactors == null) continue;
                for (int i = 0; i < stat.skillNeedFactors.Count; i++)
                {
                    if (stat.skillNeedFactors[i] is not SkillNeed_BaseBonus bonus || bonus.skill == null) continue;
                    if (!skillToWorkStats.TryGetValue(bonus.skill, out List<StatDef> list))
                    {
                        list = new List<StatDef>();
                        skillToWorkStats[bonus.skill] = list;
                    }
                    if (!list.Contains(stat))
                        list.Add(stat);
                }
            }
        }

        /// <summary>Only true speed stats; excludes yields, stealth, success chance, mechanitor stats, etc.</summary>
        private static bool IsWorkSpeedStat(StatDef stat)
        {
            if (stat == null) return false;
            if (excludedWorkStatDefNames.Contains(stat.defName)) return false;
            string name = stat.defName;
            if (name.Contains("Yield") || name.Contains("Stealth") || name.Contains("SuccessChance") || name.Contains("Quality"))
                return false;
            return name.Contains("Speed");
        }

        public static int EquivalentSkillLevel(Pawn pawn, SkillDef skill)
        {
            if (pawn == null || skill == null) return 0;
            if (pawn.RaceProps == null || !pawn.RaceProps.IsMechanoid) return 0;

            if (skill == SkillDefOf.Shooting)
                return ApplyOutpostSkillClamp(MechanoidShootingSkill(pawn));
            if (skill == SkillDefOf.Melee)
                return ApplyOutpostSkillClamp(MechanoidMeleeSkill(pawn));

            EnsureWorkStatCache();
            if (!skillToWorkStats.TryGetValue(skill, out List<StatDef> stats) || stats.Count == 0)
                return 0;

            StatDef workTypeFallback = GetWorkTypeFallbackSpeedStat(pawn, skill);
            int best = 0;
            for (int i = 0; i < stats.Count; i++)
            {
                StatDef stat = stats[i];
                if (!MechAppliesWorkSpeedStat(pawn, stat, workTypeFallback))
                    continue;
                int level = WorkSpeedToSkillLevel(pawn, stat);
                if (level > best) best = level;
            }

            // GeneralLaborSpeed and similar stats are not skill-linked in defs but apply to work-type mechs (e.g. fabricor).
            if (workTypeFallback != null && IsWorkSpeedStat(workTypeFallback) && !stats.Contains(workTypeFallback))
            {
                int level = WorkSpeedToSkillLevel(pawn, workTypeFallback);
                if (level > best) best = level;
            }

            best = ApplyDedicatedWorkMechFloor(pawn, skill, best, workTypeFallback);
            return ApplyOutpostSkillClamp(best);
        }

        /// <summary>
        /// Vanilla specialty mechs can have lower stat bases than agrihand (constructoid 50% construction → 3 at 16%/level).
        /// When the race def sets an explicit work-speed stat for this skill, treat them as at least <see cref="DedicatedWorkMechMinSkillLevel"/>.
        /// </summary>
        private static int ApplyDedicatedWorkMechFloor(Pawn pawn, SkillDef skill, int level, StatDef primaryWorkStat)
        {
            if (level >= DedicatedWorkMechMinSkillLevel) return level;
            if (primaryWorkStat != null && MechHasExplicitWorkStat(pawn, primaryWorkStat))
                return DedicatedWorkMechMinSkillLevel;
            EnsureWorkStatCache();
            if (skillToWorkStats.TryGetValue(skill, out List<StatDef> stats))
            {
                for (int i = 0; i < stats.Count; i++)
                {
                    if (MechHasExplicitWorkStat(pawn, stats[i]))
                        return DedicatedWorkMechMinSkillLevel;
                }
            }
            return level;
        }

        /// <summary>
        /// A mech only contributes a skill when it has an explicit work-speed factor on that stat
        /// (race statBases or stat showOnPawnKind), or its enabled work types map to that stat.
        /// </summary>
        private static bool MechAppliesWorkSpeedStat(Pawn pawn, StatDef stat, StatDef workTypeFallback)
        {
            if (MechHasExplicitWorkStat(pawn, stat))
                return true;
            return workTypeFallback != null && workTypeFallback == stat;
        }

        private static bool MechHasExplicitWorkStat(Pawn pawn, StatDef stat)
        {
            if (ThingDefHasStatBase(pawn?.def, stat))
                return true;
            if (pawn?.kindDef != null && stat.showOnPawnKind != null)
            {
                for (int i = 0; i < stat.showOnPawnKind.Count; i++)
                {
                    if (stat.showOnPawnKind[i] == pawn.kindDef)
                        return true;
                }
            }
            return false;
        }

        private static bool ThingDefHasStatBase(ThingDef def, StatDef stat)
        {
            if (def?.statBases == null || stat == null) return false;
            for (int i = 0; i < def.statBases.Count; i++)
            {
                if (def.statBases[i].stat == stat)
                    return true;
            }
            return false;
        }

        /// <summary>Mechs without a dedicated speed stat (e.g. fabricor) still map enabled work types to labor speed.</summary>
        private static StatDef GetWorkTypeFallbackSpeedStat(Pawn pawn, SkillDef skill)
        {
            if (pawn?.def?.race?.mechEnabledWorkTypes == null || skill == null) return null;
            List<WorkTypeDef> workTypes = pawn.def.race.mechEnabledWorkTypes;
            for (int i = 0; i < workTypes.Count; i++)
            {
                WorkTypeDef wt = workTypes[i];
                if (wt == null) continue;
                StatDef mapped = MapWorkTypeToSpeedStat(wt, skill);
                if (mapped != null)
                    return mapped;
            }
            return null;
        }

        private static StatDef MapWorkTypeToSpeedStat(WorkTypeDef workType, SkillDef skill)
        {
            if (workType == null || skill == null) return null;
            string wt = workType.defName;
            if (skill == SkillDefOf.Plants && (wt == "Growing" || wt == "PlantCutting"))
                return StatDefOf.PlantWorkSpeed;
            if (skill == SkillDefOf.Construction && wt == "Construction")
                return StatDefOf.ConstructionSpeed;
            if (skill == SkillDefOf.Crafting && (wt == "Crafting" || wt == "Smithing" || wt == "Tailoring"))
                return StatDefOf.GeneralLaborSpeed;
            if (skill == SkillDefOf.Cooking && wt == "Cooking")
                return StatDefOf.GeneralLaborSpeed;
            if (skill == SkillDefOf.Intellectual && wt == "Research")
                return StatDefOf.ResearchSpeed;
            if (skill == SkillDefOf.Mining && wt == "Mining")
                return StatDefOf.MiningSpeed;
            if (skill == SkillDefOf.Medicine && (wt == "Doctor" || wt == "Patient"))
                return DefDatabase<StatDef>.GetNamedSilentFail("MedicalOperationSpeed");
            if (skill == SkillDefOf.Animals && (wt == "Handling" || wt == "Hunting"))
                return StatDefOf.AnimalGatherSpeed;
            return null;
        }

        /// <summary>Strip WorkSpeedGlobal so outpost rating uses the mech's specialty speed, not bandwidth penalty.</summary>
        private static float IntrinsicWorkSpeed(Pawn pawn, StatDef stat)
        {
            float speed;
            try
            {
                speed = pawn.GetStatValue(stat);
            }
            catch
            {
                return 0f;
            }
            if (float.IsNaN(speed) || float.IsInfinity(speed) || speed <= 0f) return 0f;

            if (stat.statFactors != null && stat.statFactors.Contains(StatDefOf.WorkSpeedGlobal))
            {
                float global;
                try
                {
                    global = pawn.GetStatValue(StatDefOf.WorkSpeedGlobal);
                }
                catch
                {
                    global = 1f;
                }
                if (global > 0.01f)
                    speed /= global;
            }
            return speed;
        }

        private static int WorkSpeedToSkillLevel(Pawn pawn, StatDef stat)
        {
            float intrinsic = IntrinsicWorkSpeed(pawn, stat);
            if (intrinsic <= 0f) return 0;
            return Mathf.Clamp(Mathf.RoundToInt(intrinsic / WorkSpeedPercentPerSkillLevel), 0, 20);
        }

        private static int MechanoidShootingSkill(Pawn pawn)
        {
            if (pawn.kindDef != null && !pawn.kindDef.isFighter) return 0;
            if (pawn.equipment?.Primary == null) return 0;
            return WorkSpeedToSkillLevel(pawn, StatDefOf.ShootingAccuracyPawn);
        }

        /// <summary>Blade/tool DPS: Agrihand 8 power / 2s cooldown → ~3 melee.</summary>
        private static int MechanoidMeleeSkill(Pawn pawn)
        {
            float bestDps = 0f;
            if (pawn.def?.tools != null)
            {
                for (int i = 0; i < pawn.def.tools.Count; i++)
                {
                    Tool tool = pawn.def.tools[i];
                    if (tool == null || tool.cooldownTime <= 0f) continue;
                    bestDps = Mathf.Max(bestDps, tool.power / tool.cooldownTime);
                }
            }
            if (bestDps <= 0f) return 0;
            return Mathf.Clamp(Mathf.RoundToInt(bestDps * 0.75f), 0, 20);
        }

        private static int ApplyOutpostSkillClamp(int level)
        {
            return WorldDominationMod.settings?.GetEffectiveOutpostSkillLevel(level) ?? Mathf.Max(0, level);
        }
    }
}
