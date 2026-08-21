using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Academy outpost: one <see cref="SkillDef"/> per cycle, XP on eligible students via vanilla <see cref="SkillRecord.Learn"/>.</summary>
    [StaticConstructorOnStartup]
    public static class Outpost_Academy
    {
        private static Texture2D cachedGizmoIcon;

        public static float GetConfiguredBaseXpPerDay(OutpostDefExtension ext)
        {
            float xml = ext?.academyBaseXpPerDay ?? 0f;
            float fromSettings = WorldDominationMod.settings?.academyBaseXpPerDay ?? WorldDominationSettings.DefAcademyBaseXpPerDay;
            return Mathf.Max(0f, fromSettings > 0f ? fromSettings : xml);
        }

        public static int GetConfiguredMinTeacherSkill(OutpostDefExtension ext)
        {
            int xml = ext?.academyMinTeacherSkill ?? WorldDominationSettings.DefAcademyMinTeacherSkill;
            int fromSettings = WorldDominationMod.settings?.academyMinTeacherSkill ?? WorldDominationSettings.DefAcademyMinTeacherSkill;
            return Mathf.Max(0, fromSettings > 0 ? fromSettings : xml);
        }

        public static int GetConfiguredTeachCapOffset(OutpostDefExtension ext)
        {
            int xml = ext?.academyTeachCapOffset ?? WorldDominationSettings.DefAcademyTeachCapOffset;
            int fromSettings = WorldDominationMod.settings?.academyTeachCapOffset ?? WorldDominationSettings.DefAcademyTeachCapOffset;
            return Mathf.Max(0, fromSettings >= 0 ? fromSettings : xml);
        }

        public static bool UseFlatDirectXp() =>
            WorldDominationMod.settings?.academyUseFlatDirectXp ?? WorldDominationSettings.DefAcademyUseFlatDirectXp;

        /// <summary>Academy production gizmo icon.</summary>
        public static Texture2D GetGizmoIcon() =>
            cachedGizmoIcon ??= ContentFinder<Texture2D>.Get("UI/Commands/AcademyTeach", false)
                ?? GetSkillIcon(SkillDefOf.Intellectual)
                ?? TexCommand.Replant;

        /// <summary>Vanilla skill row icon path pattern: <c>UI/Icons/Skills/&lt;defName&gt;</c>.</summary>
        public static Texture2D GetSkillIcon(SkillDef sd)
        {
            if (sd == null) return null;
            return ContentFinder<Texture2D>.Get("UI/Icons/Skills/" + sd.defName, false);
        }

        /// <summary>Effective skill for this cycle (locked mid-cycle when applicable).</summary>
        public static SkillDef GetSkillForCurrentCycle(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return null;
            string name = outpost.IsSelectionLockedForThisCycle && !string.IsNullOrEmpty(outpost.LockedAcademySkillDefName)
                ? outpost.LockedAcademySkillDefName
                : outpost.SelectedAcademySkillDefName;
            return string.IsNullOrEmpty(name) ? null : DefDatabase<SkillDef>.GetNamedSilentFail(name);
        }

        /// <summary>Delivery-driving capacity track: best teacher level for the active skill, or 1 when a skill is selected but no teacher yet (keeps averages non-zero).</summary>
        public static float GetDeliveryDrivingCapacity(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null || !Outpost_Production_Utils.TryGetAcademyExtension(outpost.def, out var ext)) return 0f;
            var skill = GetSkillForCurrentCycle(outpost);
            if (skill == null) return 0f;
            int best = GetBestTeacherLevel(outpost, skill, GetConfiguredMinTeacherSkill(ext));
            return best > 0 ? best : 1f;
        }

        /// <summary>Skills shown in the academy dialog, in load order.</summary>
        public static List<SkillDef> GetCandidateSkills(WorldObjectDef def)
        {
            var list = new List<SkillDef>();
            if (!Outpost_Production_Utils.TryGetAcademyExtension(def, out var ext)) return list;
            var all = DefDatabase<SkillDef>.AllDefsListForReading;
            if (all == null) return list;
            bool useWhitelist = ext.academyAllowedSkills != null && ext.academyAllowedSkills.Count > 0;
            for (int i = 0; i < all.Count; i++)
            {
                var sd = all[i];
                if (sd == null) continue;
                if (useWhitelist)
                {
                    bool ok = false;
                    for (int w = 0; w < ext.academyAllowedSkills.Count; w++)
                    {
                        if (ext.academyAllowedSkills[w] == sd.defName) { ok = true; break; }
                    }
                    if (!ok) continue;
                }
                list.Add(sd);
            }
            return list;
        }

        /// <summary>True if at least one occupant can teach this skill at the configured minimum.</summary>
        public static bool OutpostCanTeachSkill(WorldObject_WD_Outpost outpost, SkillDef skill, int minTeacher)
        {
            if (outpost == null || skill == null) return false;
            return GetBestTeacherLevel(outpost, skill, minTeacher) >= minTeacher;
        }

        /// <summary>Highest skill level among occupants who are not totally disabled in <paramref name="skill"/>.</summary>
        public static int GetBestTeacherLevel(WorldObject_WD_Outpost outpost, SkillDef skill, int minTeacherIgnored)
        {
            if (outpost?.Occupants == null || skill == null) return 0;
            int max = 0;
            for (int i = 0; i < outpost.Occupants.Count; i++)
            {
                var p = outpost.Occupants[i];
                int lv = GetPawnSkillLevel(p, skill);
                if (lv > max) max = lv;
            }
            return max;
        }

        /// <summary>One pawn at <paramref name="bestLevel"/> for UI portrait (deterministic tie-break: <see cref="Pawn.LabelShortCap"/>).</summary>
        public static Pawn GetPrimaryTeacherPawn(WorldObject_WD_Outpost outpost, SkillDef skill, int bestLevel)
        {
            if (outpost?.Occupants == null || skill == null || bestLevel <= 0) return null;
            Pawn pick = null;
            string pickLabel = null;
            for (int i = 0; i < outpost.Occupants.Count; i++)
            {
                var p = outpost.Occupants[i];
                if (p == null || p.Destroyed) continue;
                if (GetPawnSkillLevel(p, skill) != bestLevel) continue;
                string lab = p.LabelShortCap ?? "";
                if (pick == null || string.CompareOrdinal(lab, pickLabel) < 0)
                {
                    pick = p;
                    pickLabel = lab;
                }
            }
            return pick;
        }

        /// <summary>XP multiplier from teacher level: 1× at 8, 2× at 20, linear between.</summary>
        public static float GetTeacherXpMultiplier(float teacherLevel)
        {
            if (teacherLevel <= 8f) return 1f;
            if (teacherLevel >= 20f) return 2f;
            return 1f + (teacherLevel - 8f) / 12f;
        }

        /// <summary>Approx. XP per day each eligible student receives at this teacher level (before cycle length).</summary>
        public static float GetDisplayXpPerDayPool(OutpostDefExtension ext, int teacherLevel, WorldObject_WD_Outpost outpost = null)
        {
            if (ext == null) return 0f;
            float pool = GetConfiguredBaseXpPerDay(ext) * GetTeacherXpMultiplier(teacherLevel);
            if (outpost != null)
                pool *= OutpostWarehouseAuraUtility.GetSoftProductionBonusMultiplier(outpost);
            return Mathf.Max(0f, pool);
        }

        /// <summary>After roster changes: clear academy skill if nobody meets <see cref="OutpostDefExtension.academyMinTeacherSkill"/> for the selected skill.</summary>
        public static void ValidateTeachingStateAfterOccupantsChanged(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null || !Outpost_Production_Utils.IsAcademyOutpost(outpost.def)) return;
            if (string.IsNullOrEmpty(outpost.SelectedAcademySkillDefName)) return;
            if (!Outpost_Production_Utils.TryGetAcademyExtension(outpost.def, out var ext)) return;
            var skill = outpost.SelectedAcademySkill;
            if (skill == null)
            {
                outpost.SetSelectedAcademySkill(null);
                return;
            }
            int minTeacher = GetConfiguredMinTeacherSkill(ext);
            if (GetBestTeacherLevel(outpost, skill, minTeacher) >= minTeacher) return;
            string skillLabel = skill.LabelCap;
            outpost.SetSelectedAcademySkill(null);
            string key = "TSA_WD_Academy_TeachingStoppedNoTeacher";
            string msg = key.Translate(outpost.Name ?? outpost.Label, skillLabel).ToString();
            if (msg == key || msg.Contains("TSA_WD_Academy_TeachingStoppedNoTeacher"))
                msg = "Academy at " + (outpost.Name ?? outpost.Label) + ": no teacher for " + skillLabel + " (min " + minTeacher + "). Teaching stopped.";
            Messages.Message(msg, outpost, MessageTypeDefOf.NegativeEvent);
        }

        /// <summary>Level for this skill on the pawn, or 0 if missing/disabled.</summary>
        public static int GetPawnSkillLevel(Pawn pawn, SkillDef skill)
        {
            if (pawn?.skills == null || skill == null) return 0;
            var rec = pawn.skills.GetSkill(skill);
            if (rec == null) return 0;
            try
            {
                // TotallyDisabled can NRE via CombinedDisabledWorkTags when addiction Need is not ready
                if (rec.TotallyDisabled) return 0;
                return WorldDominationMod.settings?.GetEffectiveOutpostSkillLevel(rec.Level) ?? Mathf.Max(0, rec.Level);
            }
            catch
            {
                try
                {
                    return WorldDominationMod.settings?.GetEffectiveOutpostSkillLevel(rec.levelInt) ?? Mathf.Max(0, rec.levelInt);
                }
                catch
                {
                    return 0;
                }
            }
        }

        /// <summary>When timer completes: apply XP if strength allows; never spawn items. True when the cycle is consumed (strength OK), like trading <see cref="Outpost_Trading.Produce"/>.</summary>
        public static bool TryCompleteProductionCycle(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null || !Outpost_Production_Utils.TryGetAcademyExtension(outpost.def, out var ext)) return false;
            float minStrength = WorldDominationMod.settings?.outpostDeliveryMinStrength ?? 100f;
            var comp = outpost.GetComponent<CompViralSpread>();
            if (comp != null && comp.strength < minStrength) return false;

            var skill = GetSkillForCurrentCycle(outpost);
            if (skill == null) return false;

            int minTeacher = GetConfiguredMinTeacherSkill(ext);
            int capOffset = GetConfiguredTeachCapOffset(ext);
            int teacherLevel = GetBestTeacherLevel(outpost, skill, minTeacher);
            if (teacherLevel < minTeacher)
            {
                string skillLabel = skill.LabelCap;
                outpost.SetSelectedAcademySkill(null);
                string key = "TSA_WD_Academy_TeachingStoppedNoTeacher";
                string msg = key.Translate(outpost.Name ?? outpost.Label, skillLabel).ToString();
                if (msg == key || msg.Contains("TSA_WD_Academy_TeachingStoppedNoTeacher"))
                    msg = "Academy at " + (outpost.Name ?? outpost.Label) + ": no teacher for " + skillLabel + " (min " + minTeacher + "). Teaching stopped.";
                Messages.Message(msg, outpost, MessageTypeDefOf.NegativeEvent);
                return false;
            }

            int capExclusive = teacherLevel - capOffset;
            var students = new List<Pawn>();
            CollectEligibleStudents(outpost, skill, teacherLevel, capExclusive, students);
            float pool = ComputeXpPoolForCycle(outpost, ext, teacherLevel);
            bool anyLearn = false;
            if (students.Count > 0 && pool > 0.0001f)
            {
                for (int i = 0; i < students.Count; i++)
                {
                    if (ApplyXpToPawn(students[i], skill, pool))
                        anyLearn = true;
                }
            }
            if (anyLearn)
                outpost.NotifyVirtualPawnsChanged();
            return true;
        }

        private static void CollectEligibleStudents(WorldObject_WD_Outpost outpost, SkillDef skill, int teacherLevel, int capExclusive, List<Pawn> into)
        {
            into.Clear();
            if (outpost?.Occupants == null) return;
            for (int i = 0; i < outpost.Occupants.Count; i++)
            {
                var p = outpost.Occupants[i];
                if (!IsHumanoidOccupant(p)) continue;
                int lv = GetPawnSkillLevel(p, skill);
                if (lv >= teacherLevel) continue;
                if (lv >= capExclusive) continue;
                into.Add(p);
            }
        }

        /// <summary>Humanlike occupants only (excludes animals, vehicles, mechanoids).</summary>
        public static int CountHumanoidOccupants(WorldObject_WD_Outpost outpost)
        {
            if (outpost?.Occupants == null) return 0;
            int n = 0;
            for (int i = 0; i < outpost.Occupants.Count; i++)
            {
                if (IsHumanoidOccupant(outpost.Occupants[i])) n++;
            }
            return n;
        }

        public struct AcademyRosterStats
        {
            public int HumanoidOccupants;
            public int StudentsTaught;
            public float AvgStudentSkill;
            public int TooSkilled;
        }

        /// <summary>Roster breakdown for academy dialog stats (current cycle skill).</summary>
        public static AcademyRosterStats GetRosterStats(WorldObject_WD_Outpost outpost, SkillDef skill)
        {
            var stats = new AcademyRosterStats
            {
                HumanoidOccupants = CountHumanoidOccupants(outpost)
            };
            if (outpost == null || skill == null || !Outpost_Production_Utils.TryGetAcademyExtension(outpost.def, out var ext))
                return stats;

            int minTeacher = GetConfiguredMinTeacherSkill(ext);
            int teacherLevel = GetBestTeacherLevel(outpost, skill, minTeacher);
            if (teacherLevel < minTeacher) return stats;

            int capExclusive = teacherLevel - GetConfiguredTeachCapOffset(ext);
            var students = new List<Pawn>();
            CollectEligibleStudents(outpost, skill, teacherLevel, capExclusive, students);
            stats.StudentsTaught = students.Count;

            float sum = 0f;
            for (int i = 0; i < students.Count; i++)
                sum += GetPawnSkillLevel(students[i], skill);
            stats.AvgStudentSkill = students.Count > 0 ? sum / students.Count : 0f;
            stats.TooSkilled = CountTooSkilledHumanoids(outpost, skill, capExclusive);
            return stats;
        }

        private static int CountTooSkilledHumanoids(WorldObject_WD_Outpost outpost, SkillDef skill, int capExclusive)
        {
            if (outpost?.Occupants == null) return 0;
            int n = 0;
            for (int i = 0; i < outpost.Occupants.Count; i++)
            {
                var p = outpost.Occupants[i];
                if (!IsHumanoidOccupant(p)) continue;
                if (GetPawnSkillLevel(p, skill) >= capExclusive) n++;
            }
            return n;
        }

        private static bool IsHumanoidOccupant(Pawn p)
        {
            if (p == null || p.Destroyed || p.Dead) return false;
            if (p.RaceProps == null || !p.RaceProps.Humanlike) return false;
            if (OutpostPawnClassificationUtil.IsMechanoidWorker(p)) return false;
            if (VehicleFrameworkOutpostDissolveCompat.IsVehicleFrameworkVehiclePawn(p)) return false;
            return true;
        }

        /// <summary>XP each eligible student receives when the production cycle completes (not divided by student count). Uses best teacher level for multiplier so runtime matches UI/dialog.</summary>
        public static float ComputeXpPoolForCycle(WorldObject_WD_Outpost outpost, OutpostDefExtension ext, int teacherLevel)
        {
            if (outpost == null || ext == null) return 0f;
            float cycleDays = Outpost_Production_Utils.GetProductionCycleDays(outpost);
            float basePool = GetConfiguredBaseXpPerDay(ext) * cycleDays;
            float pool = basePool * GetTeacherXpMultiplier(teacherLevel);
            pool *= OutpostWarehouseAuraUtility.GetSoftProductionBonusMultiplier(outpost);
            return Mathf.Max(0f, pool);
        }

        /// <summary>Preview XP per student per cycle using current capacity preview (UI).</summary>
        public static float GetPreviewXpPoolForCycle(WorldObject_WD_Outpost outpost)
        {
            if (!Outpost_Production_Utils.TryGetAcademyExtension(outpost?.def, out var ext)) return 0f;
            var skill = GetActiveSkillForPreview(outpost);
            if (skill == null) return 0f;
            int teacherLevel = GetPreviewTeacherLevelSnapshot(outpost, skill);
            return GetPreviewXpPoolForCycle(outpost, ext, skill, teacherLevel);
        }

        /// <summary>Preview XP per student using an explicit teacher level (cycle average or snapshot).</summary>
        public static float GetPreviewXpPoolForCycle(WorldObject_WD_Outpost outpost, int teacherLevel)
        {
            if (!Outpost_Production_Utils.TryGetAcademyExtension(outpost?.def, out var ext)) return 0f;
            var skill = GetActiveSkillForPreview(outpost);
            if (skill == null) return 0f;
            return GetPreviewXpPoolForCycle(outpost, ext, skill, teacherLevel);
        }

        private static float GetPreviewXpPoolForCycle(WorldObject_WD_Outpost outpost, OutpostDefExtension ext, SkillDef skill, int teacherLevel)
        {
            if (outpost == null || ext == null || skill == null) return 0f;
            int minTeacher = GetConfiguredMinTeacherSkill(ext);
            if (teacherLevel < minTeacher) return 0f;
            return ComputeXpPoolForCycle(outpost, ext, teacherLevel);
        }

        /// <summary>Skill shown in academy dialog previews (locked cycle skill or current selection).</summary>
        public static SkillDef GetActiveSkillForPreview(WorldObject_WD_Outpost outpost)
            => GetSkillForCurrentCycle(outpost) ?? outpost?.SelectedAcademySkill;

        /// <summary>Best teacher level now for the preview skill.</summary>
        public static int GetPreviewTeacherLevelSnapshot(WorldObject_WD_Outpost outpost, SkillDef skill)
        {
            if (outpost == null || skill == null || !Outpost_Production_Utils.TryGetAcademyExtension(outpost.def, out var ext)) return 0;
            return GetBestTeacherLevel(outpost, skill, GetConfiguredMinTeacherSkill(ext));
        }

        /// <summary>Time-weighted teacher level for cycle-end preview (matches delivery capacity track).</summary>
        public static int GetPreviewTeacherLevelAverage(WorldObject_WD_Outpost outpost)
            => outpost == null ? 0 : Mathf.RoundToInt(outpost.GetCapacityForYieldPreview());

        /// <summary>Students who would receive XP this cycle for the preview skill.</summary>
        public static int CountEligibleStudents(WorldObject_WD_Outpost outpost, SkillDef skill)
        {
            if (outpost == null || skill == null || !Outpost_Production_Utils.TryGetAcademyExtension(outpost.def, out var ext)) return 0;
            int teacher = GetBestTeacherLevel(outpost, skill, GetConfiguredMinTeacherSkill(ext));
            if (teacher < GetConfiguredMinTeacherSkill(ext)) return 0;
            int capExclusive = teacher - GetConfiguredTeachCapOffset(ext);
            var scratch = new List<Pawn>();
            CollectEligibleStudents(outpost, skill, teacher, capExclusive, scratch);
            return scratch.Count;
        }

        public static string FormatExpectedXpLine(float xpPerStudent)
            => OutpostTranslationUtil.Key("TSA_WD_Academy_Info_ExpectedXp", Mathf.RoundToInt(xpPerStudent).ToString());

        /// <summary>Dialog left-column math tooltip (full academy formula).</summary>
        public static string GetDetailedMathTooltip(WorldObject_WD_Outpost outpost)
            => GetProductionTooltip(outpost);

        /// <summary>Grant lesson XP via vanilla learning pipeline (<c>direct: false</c>) so passions, traits, and global learning factor apply.</summary>
        private static bool ApplyXpToPawn(Pawn pawn, SkillDef skill, float xp)
        {
            if (pawn?.skills == null || skill == null || xp <= 0f) return false;
            var rec = pawn.skills.GetSkill(skill);
            if (rec == null || rec.TotallyDisabled) return false;
            try
            {
                rec.Learn(xp, direct: UseFlatDirectXp());
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Inspect / summary: one line describing teaching and XP.</summary>
        public static string GetInspectProductLine(WorldObject_WD_Outpost outpost)
        {
            if (!Outpost_Production_Utils.TryGetAcademyExtension(outpost?.def, out var ext)) return "";
            var skill = GetSkillForCurrentCycle(outpost);
            if (skill == null)
                return "TSA_WD_Academy_InspectNoSkill".Translate().ToString();
            int teacher = GetBestTeacherLevel(outpost, skill, GetConfiguredMinTeacherSkill(ext));
            int capEx = teacher - GetConfiguredTeachCapOffset(ext);
            int xpPerDay = Mathf.RoundToInt(GetDisplayXpPerDayPool(ext, teacher, outpost));
            float daysLeft = outpost.ProductionTicksLeftForDisplay / 60000f;
            string daysStr = daysLeft.ToString("F1");
            string key = "TSA_WD_Academy_InspectTeaching";
            string t = key.Translate(skill.LabelCap, xpPerDay.ToString(), capEx.ToString(), daysStr).ToString();
            if (t == key || t.Contains("TSA_WD_Academy_InspectTeaching"))
                t = "Teaching " + skill.LabelCap + ". Each student gets " + xpPerDay + " XP per day up to lvl " + capEx + " (" + daysStr + " days)";
            return t;
        }

        /// <summary>Overview summary line.</summary>
        public static string GetProductionSummaryLine(WorldObject_WD_Outpost outpost)
        {
            if (!Outpost_Production_Utils.TryGetAcademyExtension(outpost?.def, out var ext)) return null;
            var skill = GetSkillForCurrentCycle(outpost) ?? outpost?.SelectedAcademySkill;
            if (skill == null) return "TSA_WD_Academy_SummaryNoSkill".Translate().ToString();
            float pool = GetPreviewXpPoolForCycle(outpost);
            string key = "TSA_WD_Academy_Summary";
            string t = key.Translate(skill.LabelCap, pool.ToString("F0")).ToString();
            if (t == key || t.Contains("TSA_WD_Academy_Summary"))
                t = "Academy: " + skill.LabelCap + " (~" + pool.ToString("F0") + " XP/cycle per student)";
            return t;
        }

        /// <summary>Gizmo / dialog tooltip body.</summary>
        public static string GetProductionTooltip(WorldObject_WD_Outpost outpost)
        {
            if (!Outpost_Production_Utils.TryGetAcademyExtension(outpost?.def, out var ext)) return "";
            var skill = GetSkillForCurrentCycle(outpost) ?? outpost?.SelectedAcademySkill;
            if (skill == null)
                return "TSA_WD_Academy_TooltipNoSkill".Translate(GetConfiguredMinTeacherSkill(ext)).ToString();
            int teacher = GetBestTeacherLevel(outpost, skill, GetConfiguredMinTeacherSkill(ext));
            int capEx = teacher - GetConfiguredTeachCapOffset(ext);
            float pool = GetPreviewXpPoolForCycle(outpost);
            float cycleDays = Outpost_Production_Utils.GetProductionCycleDays(outpost);
            float mult = GetTeacherXpMultiplier(teacher);
            float baseXp = GetConfiguredBaseXpPerDay(ext);
            string key = "TSA_WD_Academy_Tooltip";
            string t = key.Translate(
                skill.LabelCap,
                baseXp.ToString("F0"),
                cycleDays.ToString("F1"),
                teacher.ToString(),
                capEx.ToString(),
                pool.ToString("F0"),
                GetConfiguredMinTeacherSkill(ext).ToString(),
                mult.ToString("F2")).ToString();
            if (t == key || t.Contains("TSA_WD_Academy_Tooltip"))
            {
                t = "Teaching " + skill.LabelCap + ". Base " + baseXp.ToString("F0") + " XP/day × " + mult.ToString("F2")
                    + "× (1× at teacher lvl 8 → 2× at 20) × " + cycleDays.ToString("F1") + " day cycle → ~" + pool.ToString("F0")
                    + " XP per cycle for each eligible student (skill below " + capEx + "). Best teacher now: " + teacher
                    + ". Minimum teacher level: " + GetConfiguredMinTeacherSkill(ext) + ".";
            }

            string softTip = Outpost_Production_Utils.BuildSoftProductionBonusTooltip(outpost);
            if (!string.IsNullOrEmpty(softTip))
                t = t + "\n\n" + softTip;
            return t;
        }
    }
}
