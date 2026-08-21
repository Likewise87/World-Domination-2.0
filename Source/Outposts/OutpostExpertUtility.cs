using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public enum OutpostExpertRole
    {
        Strategist,
        Entertainer,
        Cook,
        Doctor,
        Engineer,
        /// <summary>Player-facing label: Warden. Social skill drives outpost prisoner resistance reduction.</summary>
        Recruiter
    }

    [Flags]
    public enum ExpertEffect
    {
        None = 0,
        AttackRange = 1 << 0,
        Production = 1 << 1,
        OccupantHeal = 1 << 2,
        OffensiveRecovery = 1 << 3,
        DefensiveRecovery = 1 << 4,
        RoadSpeed = 1 << 5,
        ConstructionRadius = 1 << 6,
        /// <summary>Strategist: player mortar + AA max range (mortar outposts only).</summary>
        MortarAntiAirRange = 1 << 7,
        /// <summary>Warden (Recruiter): outpost prisoner resistance reduction.</summary>
        PrisonerResistance = 1 << 8
    }

    public static class OutpostExpertUtility
    {
        public const int PawnsPerExpertSlot = 4;

        public static bool IsHumanoidOccupant(Pawn p)
        {
            if (p == null || p.Destroyed || p.Dead) return false;
            if (p.RaceProps == null || !p.RaceProps.Humanlike) return false;
            if (OutpostPawnClassificationUtil.IsMechanoidWorker(p)) return false;
            if (VehicleFrameworkOutpostDissolveCompat.IsVehicleFrameworkVehiclePawn(p)) return false;
            return true;
        }

        public static int GetReferenceSkillLevel()
        {
            int v = WorldDominationMod.settings?.expertReferenceSkillLevel ?? WorldDominationSettings.DefExpertReferenceSkillLevel;
            return Mathf.Clamp(v, 1, 40);
        }

        public static float GetMaxBonusForRole(OutpostExpertRole role)
        {
            var s = WorldDominationMod.settings;
            return role switch
            {
                OutpostExpertRole.Strategist => s?.expertStrategistMaxBonusPct ?? WorldDominationSettings.DefExpertStrategistMaxBonusPct,
                OutpostExpertRole.Entertainer => s?.expertEntertainerMaxBonusPct ?? WorldDominationSettings.DefExpertEntertainerMaxBonusPct,
                OutpostExpertRole.Cook => s?.expertCookMaxBonusPct ?? WorldDominationSettings.DefExpertCookMaxBonusPct,
                OutpostExpertRole.Doctor => s?.expertDoctorMaxBonusPct ?? WorldDominationSettings.DefExpertDoctorMaxBonusPct,
                OutpostExpertRole.Engineer => s?.expertEngineerMaxBonusPct ?? WorldDominationSettings.DefExpertEngineerMaxBonusPct,
                OutpostExpertRole.Recruiter => s?.expertRecruiterMaxBonusPct
                    ?? WorldDominationSettings.DefExpertRecruiterMaxBonusPct,
                _ => 0f
            };
        }

        /// <summary>Max bonus fraction for a specific effect (Engineer construction radius uses its own 30% cap).</summary>
        public static float GetMaxBonusForRoleEffect(OutpostExpertRole role, ExpertEffect effect)
        {
            if (role == OutpostExpertRole.Engineer && effect == ExpertEffect.ConstructionRadius)
            {
                return WorldDominationMod.settings?.expertEngineerConstructionRadiusMaxBonusPct
                    ?? WorldDominationSettings.DefExpertEngineerConstructionRadiusMaxBonusPct;
            }
            return GetMaxBonusForRole(role);
        }

        public static int GetRoleSkillLevel(VirtualPawnSummary summary, OutpostExpertRole role)
        {
            if (summary == null) return 0;
            return role switch
            {
                OutpostExpertRole.Strategist => summary.intellectual,
                OutpostExpertRole.Entertainer => GetEntertainerSkill(summary),
                OutpostExpertRole.Cook => summary.cooking,
                OutpostExpertRole.Doctor => summary.medicine,
                OutpostExpertRole.Engineer => Mathf.Max(summary.construction, summary.crafting),
                OutpostExpertRole.Recruiter => summary.social,
                _ => 0
            };
        }

        public static int GetRoleSkillLevel(Pawn pawn, OutpostExpertRole role)
        {
            if (!IsHumanoidOccupant(pawn)) return 0;
            VirtualPawnSummary s = VirtualPawnSummary.FromPawn(pawn);
            return GetRoleSkillLevel(s, role);
        }

        private static int GetEntertainerSkill(VirtualPawnSummary summary)
        {
            int skill = Mathf.Max(summary.artistic, summary.social);
            return skill <= 0 ? 0 : skill;
        }

        public static float ComputeBonusFraction(int skillLevel, float maxBonusAtReference)
        {
            if (skillLevel <= 0 || maxBonusAtReference <= 0f) return 0f;
            int refSkill = GetReferenceSkillLevel();
            int effective = Mathf.Min(skillLevel, refSkill);
            return (effective / (float)refSkill) * maxBonusAtReference;
        }

        public static float GetExpertBonusFraction(WorldObject_WD_Outpost outpost, OutpostExpertRole role)
        {
            if (outpost == null) return 0f;
            Pawn pawn = outpost.GetAssignedExpert(role);
            if (pawn == null) return 0f;
            int skill = GetRoleSkillLevel(pawn, role);
            return ComputeBonusFraction(skill, GetMaxBonusForRole(role));
        }

        public static float GetExpertBonusFractionForEffect(WorldObject_WD_Outpost outpost, OutpostExpertRole role, ExpertEffect effect)
        {
            if (outpost == null) return 0f;
            Pawn pawn = outpost.GetAssignedExpert(role);
            if (pawn == null) return 0f;
            int skill = GetRoleSkillLevel(pawn, role);
            return ComputeBonusFraction(skill, GetMaxBonusForRoleEffect(role, effect));
        }

        public static float GetStrategistAttackRangeBonusFraction(WorldObject_WD_Outpost outpost) =>
            GetExpertBonusFraction(outpost, OutpostExpertRole.Strategist);

        public static float GetEntertainerProductionBonus(WorldObject_WD_Outpost outpost) =>
            GetExpertBonusFraction(outpost, OutpostExpertRole.Entertainer);

        public static float GetCookProductionBonus(WorldObject_WD_Outpost outpost) =>
            GetExpertBonusFraction(outpost, OutpostExpertRole.Cook);

        public static float GetCookOffensiveRecoveryBonus(WorldObject_WD_Outpost outpost) =>
            GetExpertBonusFraction(outpost, OutpostExpertRole.Cook);

        /// <summary>Entertainer + Cook production bonuses (additive).</summary>
        public static float GetCombinedProductionBonus(WorldObject_WD_Outpost outpost) =>
            GetEntertainerProductionBonus(outpost) + GetCookProductionBonus(outpost);

        public static float GetDoctorHealBonus(WorldObject_WD_Outpost outpost) =>
            GetExpertBonusFraction(outpost, OutpostExpertRole.Doctor);

        public static float GetDoctorOffensiveRecoveryBonus(WorldObject_WD_Outpost outpost) =>
            GetExpertBonusFraction(outpost, OutpostExpertRole.Doctor);

        /// <summary>
        /// Prisoner resistance removed per in-game day at this outpost: Cum. Social base (early DR curve)
        /// times optional Warden % bonus. No Warden still allows base drop from Cum. Social.
        /// </summary>
        public static float GetRecruiterResistanceReductionPerDay(WorldObject_WD_Outpost outpost) =>
            OutpostPrisonerResistanceScaling.GetDailyDrop(outpost);

        public static float GetEngineerRoadSpeedBonus(WorldObject_WD_Outpost outpost) =>
            GetExpertBonusFraction(outpost, OutpostExpertRole.Engineer);

        public static float GetEngineerDefensiveRecoveryBonus(WorldObject_WD_Outpost outpost) =>
            GetExpertBonusFraction(outpost, OutpostExpertRole.Engineer);

        /// <summary>Bonus to construction project planning radius (roads, road blocks, clear). Caps at +30% by default.</summary>
        public static float GetEngineerConstructionRadiusBonus(WorldObject_WD_Outpost outpost) =>
            GetExpertBonusFractionForEffect(outpost, OutpostExpertRole.Engineer, ExpertEffect.ConstructionRadius);

        public static float GetCombinedExpertOccupantHealBonus(WorldObject_WD_Outpost outpost) =>
            GetDoctorHealBonus(outpost);

        /// <summary>Doctor + Cook offensive recovery bonuses (additive).</summary>
        public static float GetCombinedExpertOffensiveRecoveryBonus(WorldObject_WD_Outpost outpost) =>
            GetDoctorOffensiveRecoveryBonus(outpost) + GetCookOffensiveRecoveryBonus(outpost);

        /// <summary>True when an Entertainer expert can affect this outpost (research points or physical-goods output).</summary>
        public static bool OutpostHasProductionBonusPath(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return false;
            if (outpost.IsResearchOutpost) return true;

            WorldObjectDef def = outpost.def;
            if (def == null) return false;

            // Academy uses XP (not physical goods skill) but still receives Entertainer/Cook + warehouse aura.
            if (Outpost_Production_Utils.IsAcademyOutpost(def)) return true;

            if (Outpost_Production_Utils.IsRecruitingOutpost(def)
                || Outpost_Production_Utils.IsTradingOutpost(def)
                || Outpost_Production_Utils.IsEmbassyOutpost(def)
                || Outpost_Production_Utils.IsScavengingOutpost(def)
                || Outpost_Production_Utils.IsMortarOutpost(def)
                || Outpost_Production_Utils.IsRapidResponseOutpost(def)
                || Outpost_Production_Utils.IsPowerPlantOutpost(def)
                || Outpost_Production_Utils.IsWarehouseOutpost(def))
                return false;

            return Outpost_Production_Utils.UsesPhysicalGoodsProductionSkill(def);
        }

        public static bool IsProductionBonusRole(OutpostExpertRole role) =>
            role == OutpostExpertRole.Entertainer || role == OutpostExpertRole.Cook;

        public static bool IsRoleAvailableForOutpost(WorldObject_WD_Outpost outpost, OutpostExpertRole role)
        {
            // Entertainer is production-only. Cook also boosts offensive recovery, so it stays available on all outposts.
            if (role == OutpostExpertRole.Entertainer)
                return OutpostHasProductionBonusPath(outpost);
            return true;
        }

        public static ExpertEffect GetRoleEffects(OutpostExpertRole role) => role switch
        {
            OutpostExpertRole.Strategist => ExpertEffect.AttackRange | ExpertEffect.MortarAntiAirRange,
            OutpostExpertRole.Entertainer => ExpertEffect.Production,
            OutpostExpertRole.Cook => ExpertEffect.Production | ExpertEffect.OffensiveRecovery,
            OutpostExpertRole.Doctor => ExpertEffect.OccupantHeal | ExpertEffect.OffensiveRecovery,
            OutpostExpertRole.Engineer => ExpertEffect.RoadSpeed | ExpertEffect.DefensiveRecovery | ExpertEffect.ConstructionRadius,
            OutpostExpertRole.Recruiter => ExpertEffect.PrisonerResistance,
            _ => ExpertEffect.None
        };

        /// <summary>Effects that currently apply at this outpost (e.g. Cook production is skipped when there is no production path).</summary>
        public static ExpertEffect GetApplicableRoleEffects(WorldObject_WD_Outpost outpost, OutpostExpertRole role)
        {
            ExpertEffect effects = GetRoleEffects(role);
            if ((effects & ExpertEffect.Production) != ExpertEffect.None
                && !OutpostHasProductionBonusPath(outpost))
                effects &= ~ExpertEffect.Production;
            if ((effects & ExpertEffect.MortarAntiAirRange) != ExpertEffect.None
                && (outpost == null || !outpost.IsMortarOutpost))
                effects &= ~ExpertEffect.MortarAntiAirRange;
            return effects;
        }

        public static SkillDef GetPrimarySkillDef(OutpostExpertRole role) => role switch
        {
            OutpostExpertRole.Strategist => SkillDefOf.Intellectual,
            OutpostExpertRole.Entertainer => SkillDefOf.Social,
            OutpostExpertRole.Cook => SkillDefOf.Cooking,
            OutpostExpertRole.Doctor => SkillDefOf.Medicine,
            OutpostExpertRole.Engineer => SkillDefOf.Construction,
            OutpostExpertRole.Recruiter => SkillDefOf.Social,
            _ => null
        };

        public static string GetRoleLabel(OutpostExpertRole role) => role switch
        {
            OutpostExpertRole.Strategist => "TSA_WD_Experts_Role_Strategist".Translate().ToString(),
            OutpostExpertRole.Entertainer => "TSA_WD_Experts_Role_Entertainer".Translate().ToString(),
            OutpostExpertRole.Cook => "TSA_WD_Experts_Role_Cook".Translate().ToString(),
            OutpostExpertRole.Doctor => "TSA_WD_Experts_Role_Doctor".Translate().ToString(),
            OutpostExpertRole.Engineer => "TSA_WD_Experts_Role_Engineer".Translate().ToString(),
            OutpostExpertRole.Recruiter => "TSA_WD_Experts_Role_Warden".Translate().ToString(),
            _ => role.ToString()
        };

        /// <summary>Overview / inspect tip: pawn + skill, blank line, then row benefit text.</summary>
        public static string BuildAssignedExpertIconTooltip(WorldObject_WD_Outpost outpost, OutpostExpertRole role, Pawn pawn)
        {
            if (pawn == null) return "";
            int skill = GetRoleSkillLevel(pawn, role);
            string skillName = GetRoleSkillNameForDisplay(role, pawn);
            string head = pawn.LabelShortCap + ", " + skillName + ": " + skill;
            float bonus = GetExpertBonusFraction(outpost, role);
            string effect = GetRoleRowBenefitText(outpost, role, bonus);
            return head + "\n\n" + effect;
        }

        public static string GetRoleRowBenefitText(WorldObject_WD_Outpost outpost, OutpostExpertRole role, float bonusFraction)
        {
            if (role == OutpostExpertRole.Entertainer && !OutpostHasProductionBonusPath(outpost))
                return "TSA_WD_Experts_NoBonus".Translate().ToString();

            if (role == OutpostExpertRole.Engineer)
            {
                int speedPct = Mathf.RoundToInt(GetEngineerRoadSpeedBonus(outpost) * 100f);
                int radiusPct = Mathf.RoundToInt(GetEngineerConstructionRadiusBonus(outpost) * 100f);
                if (speedPct <= 0 && radiusPct <= 0)
                    return "TSA_WD_Experts_NoBonus".Translate().ToString();
                return "TSA_WD_Experts_RowBenefit_Engineer".Translate(speedPct, radiusPct).ToString();
            }

            if (role == OutpostExpertRole.Recruiter)
            {
                float bonus = GetExpertBonusFraction(outpost, OutpostExpertRole.Recruiter);
                if (bonus <= 0f)
                    return "TSA_WD_Experts_NoBonus".Translate().ToString();
                int wardenPct = Mathf.RoundToInt(bonus * 100f);
                return "TSA_WD_Experts_RowBenefit_Warden".Translate(wardenPct).ToString();
            }

            if (bonusFraction <= 0f)
                return "TSA_WD_Experts_NoBonus".Translate().ToString();

            int pct = Mathf.RoundToInt(bonusFraction * 100f);
            return role switch
            {
                OutpostExpertRole.Strategist =>
                    outpost != null && outpost.IsMortarOutpost
                        ? "TSA_WD_Experts_RowBenefit_StrategistMortar".Translate(pct).ToString()
                        : "TSA_WD_Experts_RowBenefit_AttackRange".Translate(pct).ToString(),
                OutpostExpertRole.Entertainer =>
                    "TSA_WD_Experts_RowBenefit_Production".Translate(pct).ToString(),
                OutpostExpertRole.Cook =>
                    OutpostHasProductionBonusPath(outpost)
                        ? "TSA_WD_Experts_RowBenefit_Cook".Translate(pct).ToString()
                        : "TSA_WD_Experts_RowBenefit_OffRecovery".Translate(pct).ToString(),
                OutpostExpertRole.Doctor =>
                    "TSA_WD_Experts_RowBenefit_Doctor".Translate(pct).ToString(),
                _ => "+" + pct + "%"
            };
        }

        public static string BuildRoleRowBenefitTooltip(WorldObject_WD_Outpost outpost, OutpostExpertRole role, float bonusFraction)
        {
            if (outpost == null) return "";
            ExpertEffect effects = GetApplicableRoleEffects(outpost, role);
            if (effects == ExpertEffect.None) return "";

            if (IsSingleExpertEffect(effects))
            {
                float effectBonus = GetExpertBonusFractionForEffect(outpost, role, effects);
                if (effectBonus <= 0f) return "";
                return BuildExpertContributionTooltip(outpost, role, effectBonus, effects);
            }

            var sb = new StringBuilder();
            bool wroteAny = false;
            foreach (ExpertEffect effect in AllExpertEffects())
            {
                if ((effects & effect) == ExpertEffect.None) continue;
                float effectBonus = GetExpertBonusFractionForEffect(outpost, role, effect);
                if (effectBonus <= 0f) continue;
                string block = BuildExpertContributionTooltip(outpost, role, effectBonus, effect);
                if (string.IsNullOrEmpty(block)) continue;
                if (wroteAny) sb.AppendLine();
                sb.Append(block);
                wroteAny = true;
            }
            return sb.ToString().TrimEnd();
        }

        private static bool IsSingleExpertEffect(ExpertEffect effects)
        {
            if (effects == ExpertEffect.None) return true;
            return (effects & (effects - 1)) == 0;
        }

        private static IEnumerable<ExpertEffect> AllExpertEffects()
        {
            yield return ExpertEffect.AttackRange;
            yield return ExpertEffect.MortarAntiAirRange;
            yield return ExpertEffect.Production;
            yield return ExpertEffect.OccupantHeal;
            yield return ExpertEffect.OffensiveRecovery;
            yield return ExpertEffect.DefensiveRecovery;
            yield return ExpertEffect.RoadSpeed;
            yield return ExpertEffect.ConstructionRadius;
            yield return ExpertEffect.PrisonerResistance;
        }

        public static ExpertEffect GetRolePrimaryBenefitEffect(OutpostExpertRole role) => role switch
        {
            OutpostExpertRole.Strategist => ExpertEffect.AttackRange,
            OutpostExpertRole.Entertainer => ExpertEffect.Production,
            OutpostExpertRole.Cook => ExpertEffect.Production,
            OutpostExpertRole.Doctor => ExpertEffect.OccupantHeal,
            OutpostExpertRole.Engineer => ExpertEffect.RoadSpeed,
            OutpostExpertRole.Recruiter => ExpertEffect.PrisonerResistance,
            _ => ExpertEffect.None
        };

        public static string GetBenefitLabel(ExpertEffect effect) => effect switch
        {
            ExpertEffect.AttackRange => "TSA_WD_Experts_BenefitLabel_AttackRange".Translate().ToString(),
            ExpertEffect.MortarAntiAirRange => "TSA_WD_Experts_BenefitLabel_MortarAntiAirRange".Translate().ToString(),
            ExpertEffect.Production => "TSA_WD_Experts_BenefitLabel_Production".Translate().ToString(),
            ExpertEffect.OccupantHeal => "TSA_WD_Experts_BenefitLabel_Heal".Translate().ToString(),
            ExpertEffect.OffensiveRecovery => "TSA_WD_Experts_BenefitLabel_OffRecovery".Translate().ToString(),
            ExpertEffect.DefensiveRecovery => "TSA_WD_Experts_BenefitLabel_DefRecovery".Translate().ToString(),
            ExpertEffect.RoadSpeed => "TSA_WD_Experts_BenefitLabel_RoadSpeed".Translate().ToString(),
            ExpertEffect.ConstructionRadius => "TSA_WD_Experts_BenefitLabel_ConstructionRadius".Translate().ToString(),
            ExpertEffect.PrisonerResistance => "TSA_WD_Experts_BenefitLabel_PrisonerResistance".Translate().ToString(),
            _ => ""
        };

        public static string BuildRoleExpandedPanelText(WorldObject_WD_Outpost outpost, OutpostExpertRole role)
        {
            if (role == OutpostExpertRole.Entertainer && !OutpostHasProductionBonusPath(outpost))
                return "TSA_WD_Experts_Expanded_NoProductionPath".Translate().ToString();
            return GetRoleDescription(role);
        }

        /// <summary>Skill-scaling detail for the expanded role panel; shown on mouseover only.</summary>
        public static string BuildRoleExpandedSkillTooltip(WorldObject_WD_Outpost outpost, OutpostExpertRole role)
        {
            if (role == OutpostExpertRole.Entertainer && outpost != null && !OutpostHasProductionBonusPath(outpost))
                return "";

            Pawn assigned = outpost?.GetAssignedExpert(role);
            string skillName = GetRoleSkillNameForDisplay(role, assigned);
            int refSkill = GetReferenceSkillLevel();

            var sb = new StringBuilder();
            sb.AppendLine("TSA_WD_Experts_Expanded_NoBonusAtZero".Translate(skillName).ToString());
            ExpertEffect effects = GetApplicableRoleEffects(outpost, role);
            foreach (ExpertEffect effect in AllExpertEffects())
            {
                if ((effects & effect) == ExpertEffect.None) continue;
                string benefitLabel = GetBenefitLabel(effect);
                int maxPct = Mathf.RoundToInt(GetMaxBonusForRoleEffect(role, effect) * 100f);
                sb.AppendLine("TSA_WD_Experts_Expanded_MaxAtRef".Translate(maxPct.ToString(), benefitLabel, skillName, refSkill.ToString()).ToString());
            }
            return sb.ToString().TrimEnd();
        }

        public static float MeasureRoleExpandedPanelHeight(WorldObject_WD_Outpost outpost, OutpostExpertRole role, float width)
        {
            GameFont prev = Text.Font;
            Text.Font = GameFont.Tiny;
            float h = Text.CalcHeight(BuildRoleExpandedPanelText(outpost, role), width);
            Text.Font = prev;
            return h + 2f;
        }

        public static string BuildExpertContributionTooltip(
            WorldObject_WD_Outpost outpost,
            OutpostExpertRole role,
            float bonusFraction)
            => BuildExpertContributionTooltip(outpost, role, bonusFraction, GetRolePrimaryBenefitEffect(role));

        public static string BuildExpertContributionTooltip(
            WorldObject_WD_Outpost outpost,
            OutpostExpertRole role,
            float bonusFraction,
            ExpertEffect benefitEffect)
        {
            if (outpost == null) return "";
            Pawn pawn = outpost.GetAssignedExpert(role);
            if (pawn == null) return "";

            int skill = GetRoleSkillLevel(pawn, role);
            string skillName = GetRoleSkillNameForDisplay(role, pawn);
            string roleLabel = GetRoleLabel(role);
            string benefitLabel = GetBenefitLabel(benefitEffect);
            int refSkill = GetReferenceSkillLevel();

            var sb = new StringBuilder();
            sb.AppendLine("TSA_WD_Experts_BenefitTooltip_PawnAssigned".Translate(roleLabel, pawn.LabelShortCap));
            sb.AppendLine("TSA_WD_Experts_BenefitTooltip_RoleSkill".Translate(roleLabel, skillName, skill.ToString()));
            int pct = Mathf.RoundToInt(bonusFraction * 100f);
            int maxPct = Mathf.RoundToInt(GetMaxBonusForRoleEffect(role, benefitEffect) * 100f);
            if (benefitEffect == ExpertEffect.PrisonerResistance)
            {
                sb.AppendLine("TSA_WD_Experts_BenefitTooltip_Result".Translate(pct.ToString(), benefitLabel));
                if (skill < refSkill)
                    sb.Append("TSA_WD_Experts_BenefitTooltip_MaxAtRef".Translate(skillName, refSkill.ToString(), maxPct.ToString(), benefitLabel));
                float total = GetRecruiterResistanceReductionPerDay(outpost);
                if (total > 0f)
                    sb.AppendLine("TSA_WD_Experts_BenefitTooltip_TotalResistanceDrop".Translate(total.ToString("F1")));
            }
            else
            {
                sb.AppendLine("TSA_WD_Experts_BenefitTooltip_Result".Translate(pct.ToString(), benefitLabel));
                if (skill < refSkill)
                    sb.Append("TSA_WD_Experts_BenefitTooltip_MaxAtRef".Translate(skillName, refSkill.ToString(), maxPct.ToString(), benefitLabel));
            }
            return sb.ToString().TrimEnd();
        }

        public static string BuildRoleFormulaTooltip(OutpostExpertRole role, int skill, float bonusFraction)
        {
            int refSkill = GetReferenceSkillLevel();
            float maxBonus = GetMaxBonusForRole(role);
            int pct = Mathf.RoundToInt(bonusFraction * 100f);
            int maxPct = Mathf.RoundToInt(maxBonus * 100f);
            return "TSA_WD_Experts_BenefitTooltip_Formula".Translate(
                skill.ToString(),
                refSkill.ToString(),
                maxPct.ToString(),
                pct.ToString()).ToString();
        }

        public static string BuildCombinedContributionTooltip(
            WorldObject_WD_Outpost outpost,
            params (OutpostExpertRole role, Func<WorldObject_WD_Outpost, float> getBonus)[] contributors)
        {
            if (outpost == null || contributors == null || contributors.Length == 0) return "";

            var sb = new StringBuilder();
            bool wroteAny = false;
            for (int i = 0; i < contributors.Length; i++)
            {
                (OutpostExpertRole role, Func<WorldObject_WD_Outpost, float> getBonus) = contributors[i];
                float bonus = getBonus(outpost);
                if (bonus <= 0f) continue;

                Pawn pawn = outpost.GetAssignedExpert(role);
                if (pawn == null) continue;

                if (wroteAny) sb.AppendLine();
                wroteAny = true;

                int skill = GetRoleSkillLevel(pawn, role);
                string skillName = GetRoleSkillNameForDisplay(role, pawn);
                int pct = Mathf.RoundToInt(bonus * 100f);
                sb.AppendLine("TSA_WD_Experts_BenefitTooltip_RoleLine".Translate(GetRoleLabel(role), pawn.LabelShortCap));
                sb.Append("TSA_WD_Experts_BenefitTooltip_SkillLine".Translate(skillName, skill.ToString(), pct.ToString()));
            }

            return sb.ToString().TrimEnd();
        }

        public static string BuildExpertMutatorLines(WorldObject_WD_Outpost outpost, ExpertEffect filter)
        {
            if (outpost == null || filter == ExpertEffect.None) return "";
            var sb = new StringBuilder();
            foreach (OutpostExpertRole role in Enum.GetValues(typeof(OutpostExpertRole)))
            {
                if ((GetRoleEffects(role) & filter) == ExpertEffect.None) continue;
                if (filter == ExpertEffect.Production && !OutpostHasProductionBonusPath(outpost)) continue;

                float bonus = GetExpertBonusFractionForEffect(outpost, role, filter);
                if (Mathf.Abs(bonus) < 1e-6f) continue;

                Pawn pawn = outpost.GetAssignedExpert(role);
                string name = pawn?.LabelShortCap ?? GetRoleLabel(role);
                int pp = Mathf.RoundToInt(bonus * 100f);
                string signed = (pp >= 0 ? "+" : "") + pp.ToString() + "%";
                sb.AppendLine("TSA_WD_ProductivityTooltip_MutatorLine".Translate(name, signed).ToString());
            }
            return sb.ToString().TrimEnd();
        }

        public static string AppendExpertBlock(string baseTip, string expertLines)
        {
            if (string.IsNullOrEmpty(expertLines)) return baseTip ?? "";
            var sb = new StringBuilder(baseTip ?? "");
            if (sb.Length > 0) sb.AppendLine().AppendLine();
            sb.AppendLine("TSA_WD_Experts_MutatorHeader".Translate());
            sb.Append(expertLines);
            return sb.ToString();
        }

        public static string GetRoleDescription(OutpostExpertRole role) => role switch
        {
            OutpostExpertRole.Strategist => "TSA_WD_Experts_RoleDesc_Strategist".Translate().ToString(),
            OutpostExpertRole.Entertainer => "TSA_WD_Experts_RoleDesc_Entertainer".Translate().ToString(),
            OutpostExpertRole.Cook => "TSA_WD_Experts_RoleDesc_Cook".Translate().ToString(),
            OutpostExpertRole.Doctor => "TSA_WD_Experts_RoleDesc_Doctor".Translate().ToString(),
            OutpostExpertRole.Engineer => "TSA_WD_Experts_RoleDesc_Engineer".Translate().ToString(),
            OutpostExpertRole.Recruiter => "TSA_WD_Experts_RoleDesc_Warden".Translate().ToString(),
            _ => ""
        };

        public static string GetRoleSkillNameForDisplay(OutpostExpertRole role, Pawn pawn = null)
        {
            switch (role)
            {
                case OutpostExpertRole.Strategist:
                    return SkillDefOf.Intellectual.LabelCap;
                case OutpostExpertRole.Cook:
                    return SkillDefOf.Cooking.LabelCap;
                case OutpostExpertRole.Doctor:
                    return SkillDefOf.Medicine.LabelCap;
                case OutpostExpertRole.Engineer:
                    if (pawn != null)
                    {
                        VirtualPawnSummary s = VirtualPawnSummary.FromPawn(pawn);
                        return s.construction >= s.crafting
                            ? SkillDefOf.Construction.LabelCap
                            : SkillDefOf.Crafting.LabelCap;
                    }
                    return "TSA_WD_Experts_Skill_ConOrCraft".Translate().ToString();
                case OutpostExpertRole.Entertainer:
                    if (pawn != null)
                    {
                        VirtualPawnSummary s = VirtualPawnSummary.FromPawn(pawn);
                        return s.artistic >= s.social
                            ? SkillDefOf.Artistic.LabelCap
                            : SkillDefOf.Social.LabelCap;
                    }
                    return "TSA_WD_Experts_Skill_ArtOrSocial".Translate().ToString();
                case OutpostExpertRole.Recruiter:
                    return SkillDefOf.Social.LabelCap;
                default:
                    return "";
            }
        }

        public static OutpostExpertRole? GetAssignedRoleForPawn(WorldObject_WD_Outpost outpost, Pawn pawn, OutpostExpertRole? exceptRole = null)
        {
            if (outpost == null || pawn == null || pawn.ThingID.NullOrEmpty()) return null;
            foreach (OutpostExpertRole role in Enum.GetValues(typeof(OutpostExpertRole)))
            {
                if (exceptRole.HasValue && role == exceptRole.Value) continue;
                if (outpost.GetExpertThingId(role) == pawn.ThingID) return role;
            }
            return null;
        }

        public static IEnumerable<Pawn> GetAllHumanoidOccupants(WorldObject_WD_Outpost outpost)
        {
            if (outpost?.Occupants == null) yield break;
            for (int i = 0; i < outpost.Occupants.Count; i++)
            {
                Pawn p = outpost.Occupants[i];
                if (IsHumanoidOccupant(p)) yield return p;
            }
        }

        public struct ExpertBenefitLine
        {
            public string DisplayText;
            public string Tooltip;
        }

        public static List<ExpertBenefitLine> BuildAggregateBenefitLines(WorldObject_WD_Outpost outpost)
        {
            var lines = new List<ExpertBenefitLine>();
            if (outpost == null) return lines;

            float attack = GetStrategistAttackRangeBonusFraction(outpost);
            if (attack > 0f)
            {
                lines.Add(new ExpertBenefitLine
                {
                    DisplayText = "TSA_WD_Experts_Benefit_AttackRange".Translate(Mathf.RoundToInt(attack * 100f)).ToString(),
                    Tooltip = BuildExpertContributionTooltip(outpost, OutpostExpertRole.Strategist, attack, ExpertEffect.AttackRange)
                });
                if (outpost.IsMortarOutpost)
                {
                    lines.Add(new ExpertBenefitLine
                    {
                        DisplayText = "TSA_WD_Experts_Benefit_MortarAntiAirRange".Translate(Mathf.RoundToInt(attack * 100f)).ToString(),
                        Tooltip = BuildExpertContributionTooltip(outpost, OutpostExpertRole.Strategist, attack, ExpertEffect.MortarAntiAirRange)
                    });
                }
            }

            if (OutpostHasProductionBonusPath(outpost))
            {
                float production = GetCombinedProductionBonus(outpost);
                if (production > 0f)
                {
                    lines.Add(new ExpertBenefitLine
                    {
                        DisplayText = "TSA_WD_Experts_Benefit_Production".Translate(Mathf.RoundToInt(production * 100f)).ToString(),
                        Tooltip = BuildCombinedContributionTooltip(
                            outpost,
                            (OutpostExpertRole.Entertainer, GetEntertainerProductionBonus),
                            (OutpostExpertRole.Cook, GetCookProductionBonus))
                    });
                }
            }

            float heal = GetCombinedExpertOccupantHealBonus(outpost);
            if (heal > 0f)
                lines.Add(new ExpertBenefitLine
                {
                    DisplayText = "TSA_WD_Experts_Benefit_Heal".Translate(Mathf.RoundToInt(heal * 100f)).ToString(),
                    Tooltip = BuildExpertContributionTooltip(outpost, OutpostExpertRole.Doctor, heal, ExpertEffect.OccupantHeal)
                });

            float offRec = GetCombinedExpertOffensiveRecoveryBonus(outpost);
            if (offRec > 0f)
                lines.Add(new ExpertBenefitLine
                {
                    DisplayText = "TSA_WD_Experts_Benefit_OffRecovery".Translate(Mathf.RoundToInt(offRec * 100f)).ToString(),
                    Tooltip = BuildCombinedContributionTooltip(
                        outpost,
                        (OutpostExpertRole.Doctor, GetDoctorOffensiveRecoveryBonus),
                        (OutpostExpertRole.Cook, GetCookOffensiveRecoveryBonus))
                });

            float defRec = GetEngineerDefensiveRecoveryBonus(outpost);
            if (defRec > 0f)
            {
                lines.Add(new ExpertBenefitLine
                {
                    DisplayText = "TSA_WD_Experts_Benefit_DefRecovery".Translate(Mathf.RoundToInt(defRec * 100f)).ToString(),
                    Tooltip = BuildExpertContributionTooltip(outpost, OutpostExpertRole.Engineer, defRec, ExpertEffect.DefensiveRecovery)
                });
            }

            float road = GetEngineerRoadSpeedBonus(outpost);
            if (road > 0f)
            {
                lines.Add(new ExpertBenefitLine
                {
                    DisplayText = "TSA_WD_Experts_Benefit_RoadSpeed".Translate(Mathf.RoundToInt(road * 100f)).ToString(),
                    Tooltip = BuildExpertContributionTooltip(outpost, OutpostExpertRole.Engineer, road, ExpertEffect.RoadSpeed)
                });
            }

            float radius = GetEngineerConstructionRadiusBonus(outpost);
            if (radius > 0f)
            {
                lines.Add(new ExpertBenefitLine
                {
                    DisplayText = "TSA_WD_Experts_Benefit_ConstructionRadius".Translate(Mathf.RoundToInt(radius * 100f)).ToString(),
                    Tooltip = BuildExpertContributionTooltip(outpost, OutpostExpertRole.Engineer, radius, ExpertEffect.ConstructionRadius)
                });
            }

            float resistance = GetRecruiterResistanceReductionPerDay(outpost);
            if (resistance > 0f && outpost.GetAssignedExpert(OutpostExpertRole.Recruiter) != null)
            {
                float wardenBonus = GetExpertBonusFraction(outpost, OutpostExpertRole.Recruiter);
                lines.Add(new ExpertBenefitLine
                {
                    DisplayText = "TSA_WD_Experts_Benefit_PrisonerResistance".Translate(resistance.ToString("F1")).ToString(),
                    Tooltip = wardenBonus > 0f
                        ? BuildExpertContributionTooltip(outpost, OutpostExpertRole.Recruiter, wardenBonus, ExpertEffect.PrisonerResistance)
                        : OutpostPrisonerResistanceScaling.BuildTooltip(outpost)
                });
            }

            return lines;
        }

        public static IEnumerable<Pawn> GetEligibleOccupants(WorldObject_WD_Outpost outpost, OutpostExpertRole role)
        {
            if (outpost?.Occupants == null) yield break;
            string currentId = outpost.GetExpertThingId(role);
            for (int i = 0; i < outpost.Occupants.Count; i++)
            {
                Pawn p = outpost.Occupants[i];
                if (!IsHumanoidOccupant(p)) continue;
                if (p.ThingID.NullOrEmpty()) continue;
                if (p.ThingID != currentId && outpost.IsExpertAssignedElsewhere(p.ThingID, role))
                    continue;
                yield return p;
            }
        }

        public static int GetHumanoidOccupantCount(WorldObject_WD_Outpost outpost)
        {
            if (outpost?.Occupants == null) return 0;
            int count = 0;
            for (int i = 0; i < outpost.Occupants.Count; i++)
            {
                if (IsHumanoidOccupant(outpost.Occupants[i]))
                    count++;
            }
            return count;
        }

        /// <summary>How many expert roles this outpost can actually use (e.g. no Entertainer on non-production types).</summary>
        public static int GetAvailableExpertRoleCount(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return 0;
            int count = 0;
            foreach (OutpostExpertRole role in Enum.GetValues(typeof(OutpostExpertRole)))
            {
                if (IsRoleAvailableForOutpost(outpost, role))
                    count++;
            }
            return count;
        }

        public static int GetMaxExpertSlots(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return 0;
            int roleCap = GetAvailableExpertRoleCount(outpost);
            if (roleCap <= 0) return 0;
            return Mathf.Min(GetHumanoidOccupantCount(outpost) / PawnsPerExpertSlot, roleCap);
        }

        public static int GetAssignedExpertCount(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return 0;
            int count = 0;
            foreach (OutpostExpertRole role in Enum.GetValues(typeof(OutpostExpertRole)))
            {
                if (!IsRoleAvailableForOutpost(outpost, role)) continue;
                if (!outpost.GetExpertThingId(role).NullOrEmpty())
                    count++;
            }
            return count;
        }

        public static bool IsRoleBlockedByCapacity(WorldObject_WD_Outpost outpost, OutpostExpertRole role)
        {
            if (outpost == null) return true;
            if (outpost.GetAssignedExpert(role) != null) return false;
            return GetAssignedExpertCount(outpost) >= GetMaxExpertSlots(outpost);
        }

        public static bool CanAssignExpertToRole(WorldObject_WD_Outpost outpost, OutpostExpertRole role, Pawn pawn)
        {
            if (outpost == null || pawn == null) return false;
            if (outpost.GetAssignedExpert(role) != null) return true;
            if (GetAssignedRoleForPawn(outpost, pawn).HasValue) return true;
            return GetAssignedExpertCount(outpost) < GetMaxExpertSlots(outpost);
        }

        public static void EnforceExpertCapacity(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return;
            // Occupants are temporarily empty while borrowed for a manual defense map.
            // Do not strip experts for capacity; ValidateAssignments will re-check after return.
            if (outpost.ManualDefenseActive)
                return;
            bool changed = false;
            while (GetAssignedExpertCount(outpost) > GetMaxExpertSlots(outpost))
            {
                var assignedRoles = new List<OutpostExpertRole>();
                foreach (OutpostExpertRole role in Enum.GetValues(typeof(OutpostExpertRole)))
                {
                    if (!outpost.GetExpertThingId(role).NullOrEmpty())
                        assignedRoles.Add(role);
                }
                if (assignedRoles.Count == 0) break;

                OutpostExpertRole roleToClear = assignedRoles.RandomElement();
                Pawn pawn = outpost.GetAssignedExpert(roleToClear);
                outpost.SetExpertThingId(roleToClear, null);
                changed = true;

                if (pawn != null && outpost.Faction == Faction.OfPlayer)
                {
                    Messages.Message(
                        "TSA_WD_Experts_RemovedTooFewPawns".Translate(
                            pawn.LabelShortCap,
                            GetRoleLabel(roleToClear),
                            outpost.LabelCap),
                        outpost,
                        MessageTypeDefOf.NegativeEvent,
                        false);
                }
            }

            if (changed)
                outpost.InvalidateInspectCachePublic();
        }

        public static void ValidateAssignments(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return;
            // Manual defense extracts occupants onto a temp map. Expert roles are stored by ThingID;
            // clearing them here would lose assignments for survivors (including injured) when they return.
            // After ReturnManualDefensePawns / ClearManualDefenseActive, this runs again and drops dead experts.
            if (outpost.ManualDefenseActive)
                return;
            var seen = new HashSet<string>();
            foreach (OutpostExpertRole role in Enum.GetValues(typeof(OutpostExpertRole)))
            {
                if (!IsRoleAvailableForOutpost(outpost, role))
                {
                    if (!outpost.GetExpertThingId(role).NullOrEmpty())
                        outpost.SetExpertThingId(role, null);
                    continue;
                }

                string id = outpost.GetExpertThingId(role);
                if (id.NullOrEmpty()) continue;
                Pawn pawn = outpost.FindOccupantByThingId(id);
                if (pawn == null || !IsHumanoidOccupant(pawn))
                {
                    outpost.SetExpertThingId(role, null);
                    continue;
                }
                if (!seen.Add(id))
                    outpost.SetExpertThingId(role, null);
            }

            EnforceExpertCapacity(outpost);
        }

        public static bool TryAssignExpert(WorldObject_WD_Outpost outpost, OutpostExpertRole role, Pawn pawn)
        {
            if (outpost == null || pawn == null || !IsHumanoidOccupant(pawn) || pawn.ThingID.NullOrEmpty())
                return false;
            if (!IsRoleAvailableForOutpost(outpost, role)) return false;
            if (!outpost.Occupants.Contains(pawn)) return false;
            if (!CanAssignExpertToRole(outpost, role, pawn)) return false;

            outpost.ClearExpertFromAllRoles(pawn.ThingID);
            outpost.SetExpertThingId(role, pawn.ThingID);
            outpost.InvalidateInspectCachePublic();
            return true;
        }

        public static void ClearExpert(WorldObject_WD_Outpost outpost, OutpostExpertRole role)
        {
            if (outpost == null) return;
            outpost.SetExpertThingId(role, null);
            outpost.InvalidateInspectCachePublic();
        }
    }
}
