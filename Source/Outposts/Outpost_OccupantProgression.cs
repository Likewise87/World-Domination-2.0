using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Skill XP, biological aging, and virtual healing for outpost occupants and stored animals (real pawns, not world-ticked). Mutates pawns; occupant changes refresh via <see cref="WorldObject_WD_Outpost.NotifyVirtualPawnsChanged"/>.</summary>
    public static class Outpost_OccupantProgression
    {
        private const float HediffRemoveSeverityThreshold = 0.001f;
        private const float FullImmunityThreshold = 1f - 0.0001f;

        private static readonly System.Reflection.FieldInfo ImmunizableImmunityField =
            AccessTools.Field(typeof(HediffComp_Immunizable), "immunity");

        /// <summary>After a successful production payout: grant settings XP to each relevant skill per occupant (skip if skill level &gt;= cap). Uses vanilla <see cref="SkillRecord.Learn"/> with <c>direct: false</c> so passions, traits, and global learning factor apply.</summary>
        public static void ApplyPayoutSkillXp(WorldObject_WD_Outpost outpost)
        {
            WorldDominationSettings settings = WorldDominationMod.settings;
            if (settings == null || outpost?.def == null || outpost.Faction != Faction.OfPlayer) return;
            float xpAmt = settings.outpostOccupantSkillXpPerProductionCycle;
            if (xpAmt <= 0f) return;
            int maxLv = settings.GetEffectiveOutpostSkillLevel(settings.outpostOccupantSkillXpMaxLevel);
            List<SkillDef> skills = WorldObject_WD_Outpost.GetRelevantSkillDefs(outpost.def);
            if (skills == null || skills.Count == 0) return;
            List<Pawn> occ = outpost.Occupants;
            if (occ == null || occ.Count == 0) return;

            bool any = false;
            foreach (Pawn p in occ)
            {
                if (p == null || p.Destroyed || p.Dead) continue;
                if (p.RaceProps == null || !p.RaceProps.Humanlike) continue;
                if (p.skills == null) continue;
                foreach (SkillDef sd in skills)
                {
                    if (sd == null) continue;
                    SkillRecord rec = p.skills.GetSkill(sd);
                    if (rec == null) continue;
                    int level;
                    try
                    {
                        level = settings.GetEffectiveOutpostSkillLevel(rec.Level);
                    }
                    catch
                    {
                        level = settings.GetEffectiveOutpostSkillLevel(rec.levelInt);
                    }
                    if (level >= maxLv) continue;
                    try
                    {
                        rec.Learn(xpAmt, direct: false);
                        any = true;
                    }
                    catch
                    {
                        // Modded skills / bad state — skip this record
                    }
                }
            }
            if (any)
                outpost.NotifyVirtualPawnsChanged();
        }

        /// <summary>Advance biological age for frozen occupants like vanilla mothballed world pawns: one in-game day per call.</summary>
        public static void TickOccupantsBiologicalAgeOneDay(WorldObject_WD_Outpost outpost)
        {
            if (outpost?.Occupants == null || outpost.Occupants.Count == 0) return;
            bool any = false;
            foreach (Pawn p in outpost.Occupants)
            {
                if (p == null || p.Destroyed || p.Dead) continue;
                if (p.RaceProps == null || !p.RaceProps.Humanlike) continue;
                if (p.ageTracker == null) continue;
                try
                {
                    p.ageTracker.AgeTickMothballed(GenDate.TicksPerDay);
                    any = true;
                }
                catch
                {
                    // Rare mod conflicts on AgeTracker
                }
            }
            if (any)
                outpost.NotifyVirtualPawnsChanged();
        }

        /// <summary>
        /// Advance biological age for stored animals (not vehicles/mechs): one in-game day per call.
        /// Refreshes outpost strength only when a life stage changes or an animal dies of age.
        /// </summary>
        public static void TickStoredAnimalsBiologicalAgeOneDay(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return;
            List<Pawn> list = outpost.StoredAnimalsAndVehicles;
            if (list == null || list.Count == 0) return;

            bool strengthDirty = false;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                Pawn p = list[i];
                if (p == null || p.Destroyed || p.Dead)
                {
                    list.RemoveAt(i);
                    strengthDirty = true;
                    continue;
                }
                if (!IsAgeableStoredAnimal(p) || p.ageTracker == null) continue;

                int stageBefore = p.ageTracker.CurLifeStageIndex;
                try
                {
                    p.ageTracker.AgeTickMothballed(GenDate.TicksPerDay);
                }
                catch
                {
                    continue;
                }

                if (p.Destroyed || p.Dead)
                {
                    list.RemoveAt(i);
                    strengthDirty = true;
                    continue;
                }
                if (p.ageTracker.CurLifeStageIndex != stageBefore)
                    strengthDirty = true;
            }

            if (strengthDirty)
                outpost.GetComponent<CompViralSpread>()?.UpdateOutpostStrengthLogically();
        }

        private static bool IsAgeableStoredAnimal(Pawn pawn)
        {
            if (pawn?.RaceProps == null) return false;
            if (VehicleFrameworkOutpostDissolveCompat.IsVehicleFrameworkVehiclePawn(pawn)) return false;
            if (OutpostPawnClassificationUtil.IsMechanoidWorker(pawn)) return false;
            if (pawn.RaceProps.IsMechanoid) return false;
            if (pawn.RaceProps.Humanlike) return false;
            return pawn.RaceProps.Animal;
        }

        /// <summary>Once per in-game day: heal injuries, blood loss, and immunizable conditions for mothballed outpost prisoners (Doctor bonus applies).</summary>
        public static void TickPrisonersVirtualHealingOneDay(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null || !outpost.PrisonersNeedHealing) return;
            WorldDominationSettings settings = WorldDominationMod.settings;
            if (settings == null) return;
            float baseSeverityPerDay = settings.outpostOccupantHealSeverityPerDay;
            if (baseSeverityPerDay <= 0f) return;

            float healMult = 1f + outpost.GetHospitalOccupantHealMultiplierBonus() + outpost.GetOutpostExpertOccupantHealMultiplierBonus();
            float dailySeverityHeal = baseSeverityPerDay * healMult;

            List<Pawn> list = outpost.Prisoners;
            if (list == null || list.Count == 0)
            {
                outpost.SetPrisonersNeedHealing(false);
                return;
            }

            bool anyHealed = false;
            bool anyStillNeedsHealing = false;

            for (int i = 0; i < list.Count; i++)
            {
                Pawn p = list[i];
                if (p == null || p.Destroyed || p.Dead) continue;
                if (p.RaceProps == null || !p.RaceProps.Humanlike) continue;
                if (!OccupantNeedsHealing(p)) continue;

                if (ApplyVirtualHealPass(p, dailySeverityHeal, healMult))
                    anyHealed = true;

                if (OccupantNeedsHealing(p))
                    anyStillNeedsHealing = true;
            }

            outpost.SetPrisonersNeedHealing(anyStillNeedsHealing);

            if (anyHealed)
                Window_Prisoners.InvalidateCache();
        }

        /// <summary>Once per in-game day: heal injuries, blood loss, and immunizable conditions for mothballed occupants.</summary>
        public static void TickOccupantsVirtualHealingOneDay(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null || !outpost.OccupantsNeedHealing) return;
            WorldDominationSettings settings = WorldDominationMod.settings;
            if (settings == null) return;
            float baseSeverityPerDay = settings.outpostOccupantHealSeverityPerDay;
            if (baseSeverityPerDay <= 0f) return;

            float healMult = 1f + outpost.GetHospitalOccupantHealMultiplierBonus() + outpost.GetOutpostExpertOccupantHealMultiplierBonus();
            float dailySeverityHeal = baseSeverityPerDay * healMult;

            List<Pawn> occ = outpost.Occupants;
            if (occ == null || occ.Count == 0)
            {
                outpost.SetOccupantsNeedHealing(false);
                return;
            }

            bool anyHealed = false;
            bool anyStillNeedsHealing = false;

            foreach (Pawn p in occ)
            {
                if (p == null || p.Destroyed || p.Dead) continue;
                if (p.RaceProps == null || !p.RaceProps.Humanlike) continue;
                if (!OccupantNeedsHealing(p)) continue;

                if (ApplyVirtualHealPass(p, dailySeverityHeal, healMult))
                    anyHealed = true;

                if (OccupantNeedsHealing(p))
                    anyStillNeedsHealing = true;
            }

            outpost.SetOccupantsNeedHealing(anyStillNeedsHealing);

            if (anyHealed)
            {
                try
                {
                    outpost.NotifyVirtualPawnsChanged();
                }
                catch
                {
                    // Summary refresh must not break the daily pass
                }
            }
        }

        /// <summary>True when pawn has treatable injuries/infections or summary health is below full.</summary>
        public static bool OccupantNeedsHealing(Pawn pawn)
        {
            if (pawn?.health == null) return false;
            if (HasTreatableHediffs(pawn)) return true;
            if (pawn.health.summaryHealth == null) return false;
            try
            {
                float p = pawn.health.summaryHealth.SummaryHealthPercent;
                if (float.IsNaN(p) || float.IsInfinity(p)) return false;
                return p < 0.99f;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>True when the bleeding icon should show: an actively bleeding wound only.</summary>
        public static bool OccupantShowsHurtIcon(Pawn pawn)
        {
            List<Hediff> hediffs = pawn?.health?.hediffSet?.hediffs;
            if (hediffs == null) return false;
            for (int i = 0; i < hediffs.Count; i++)
            {
                if (HediffShowsBleedingIcon(hediffs[i])) return true;
            }
            return false;
        }

        public static int CountOccupantsShowingHurtIcon(IReadOnlyList<Pawn> occupants)
        {
            if (occupants == null) return 0;
            int count = 0;
            for (int i = 0; i < occupants.Count; i++)
            {
                Pawn p = occupants[i];
                if (p == null || p.Destroyed || p.Dead) continue;
                if (p.RaceProps == null || !p.RaceProps.Humanlike) continue;
                if (OccupantShowsHurtIcon(p)) count++;
            }
            return count;
        }

        private static bool HediffShowsBleedingIcon(Hediff hediff)
        {
            if (hediff == null) return false;
            try
            {
                // Severity can NRE on Hediff_ChemicalDependency when LinkedGene is not ready
                if (hediff.Severity <= HediffRemoveSeverityThreshold) return false;
                return hediff is Hediff_Injury injury && InjuryIsBleeding(injury);
            }
            catch
            {
                // Modded hediffs must not break the UI gate
            }
            return false;
        }

        private static bool InjuryIsBleeding(Hediff_Injury injury)
        {
            if (injury == null || injury.IsPermanent()) return false;
            return injury.BleedRate > 1E-05f;
        }

        public static int CountOccupantsNeedingHealing(IReadOnlyList<Pawn> occupants)
        {
            if (occupants == null) return 0;
            int count = 0;
            for (int i = 0; i < occupants.Count; i++)
            {
                Pawn p = occupants[i];
                if (p == null || p.Destroyed || p.Dead) continue;
                if (p.RaceProps == null || !p.RaceProps.Humanlike) continue;
                if (OccupantNeedsHealing(p)) count++;
            }
            return count;
        }

        private static bool HasTreatableHediffs(Pawn pawn)
        {
            List<Hediff> hediffs = pawn?.health?.hediffSet?.hediffs;
            if (hediffs == null) return false;
            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff hediff = hediffs[i];
                if (hediff == null) continue;
                try
                {
                    // Severity can NRE on Hediff_ChemicalDependency when LinkedGene is not ready
                    if (hediff.Severity <= HediffRemoveSeverityThreshold) continue;
                    if (hediff is Hediff_Injury injury && !injury.IsPermanent())
                        return true;
                    if (IsBloodLoss(hediff))
                        return true;
                    if (hediff.TryGetComp<HediffComp_Immunizable>() != null)
                        return true;
                }
                catch
                {
                    // ChemicalDependency / modded hediffs must not break the gate
                }
            }
            return false;
        }

            private static bool ApplyVirtualHealPass(Pawn pawn, float dailySeverityHeal, float healMult)
        {
            if (pawn?.health?.hediffSet?.hediffs == null) return false;
            bool changed = false;
            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
            for (int i = hediffs.Count - 1; i >= 0; i--)
            {
                Hediff hediff = hediffs[i];
                if (hediff == null) continue;
                try
                {
                    if (TryHealInjury(pawn, hediff, dailySeverityHeal))
                    {
                        changed = true;
                        continue;
                    }
                    if (TryHealBloodLoss(pawn, hediff, dailySeverityHeal))
                    {
                        changed = true;
                        continue;
                    }
                    if (TryAdvanceImmunity(pawn, hediff, healMult))
                        changed = true;
                }
                catch
                {
                    // CE / modded hediffs must not break the daily pass
                }
            }

            if (changed)
            {
                TryRefreshOccupantHealthState(pawn);
                try
                {
                    pawn.health.Notify_HediffChanged(null);
                }
                catch
                {
                    // Stat refresh is best-effort
                }
            }

            return changed;
        }

        /// <summary>Recalculate downed/incapacitated state after virtual healing on mothballed occupants.</summary>
        public static void TryRefreshOccupantHealthState(Pawn pawn)
        {
            if (pawn?.health == null) return;
            if (OccupantNeedsHealing(pawn)) return;
            try
            {
                pawn.health.CheckForStateChange(null, null);
            }
            catch
            {
                // Modded health trackers must not break the daily pass
            }
        }

        private static bool TryHealInjury(Pawn pawn, Hediff hediff, float dailySeverityHeal)
        {
            if (hediff is not Hediff_Injury injury || injury.IsPermanent()) return false;
            float severity = injury.Severity;
            if (severity <= 0f) return false;

            if (dailySeverityHeal <= 0f) return false;

            injury.Severity = severity - dailySeverityHeal;
            if (injury.Severity <= HediffRemoveSeverityThreshold)
                pawn.health.RemoveHediff(injury);
            return true;
        }

        private static bool IsBloodLoss(Hediff hediff)
        {
            return hediff?.def != null && hediff.def == HediffDefOf.BloodLoss;
        }

        private static bool TryHealBloodLoss(Pawn pawn, Hediff hediff, float dailySeverityHeal)
        {
            if (!IsBloodLoss(hediff)) return false;
            float severity = hediff.Severity;
            if (severity <= 0f) return false;
            if (dailySeverityHeal <= 0f) return false;

            hediff.Severity = severity - dailySeverityHeal;
            if (hediff.Severity <= HediffRemoveSeverityThreshold)
                pawn.health.RemoveHediff(hediff);
            return true;
        }

        private static bool TryAdvanceImmunity(Pawn pawn, Hediff hediff, float healMult)
        {
            HediffComp_Immunizable immComp = hediff.TryGetComp<HediffComp_Immunizable>();
            if (immComp?.Props is not HediffCompProperties_Immunizable props) return false;

            float current = immComp.Immunity;

            // Phase 1: fully immune — fade severity each day until removed (vanilla post-immunity behavior).
            if (current >= FullImmunityThreshold && props.severityPerDayImmune < 0f)
                return TryFadeImmuneHediff(pawn, hediff, healMult, props);

            // Phase 2: gain immunity while still sick.
            float gainPerDay = props.immunityPerDaySick;
            if (gainPerDay <= 0f) return false;

            float gain = gainPerDay * healMult;
            if (gain <= 0f) return false;

            float factor = 1f;
            try
            {
                if (pawn?.health != null)
                {
                    float stat = pawn.GetStatValue(StatDefOf.ImmunityGainSpeed);
                    if (stat > 0f)
                        factor = stat / 100f;
                }
            }
            catch
            {
                factor = 1f;
            }

            gain *= factor;

            float next = Mathf.Min(1f, current + gain);
            if (next <= current + 0.0001f) return false;

            if (ImmunizableImmunityField != null)
                ImmunizableImmunityField.SetValue(immComp, next);
            else
                return false;

            if (next >= FullImmunityThreshold && props.severityPerDayImmune < 0f)
                return TryFadeImmuneHediff(pawn, hediff, healMult, props);

            return true;
        }

        private static bool TryFadeImmuneHediff(Pawn pawn, Hediff hediff, float healMult, HediffCompProperties_Immunizable props)
        {
            float immuneReduction = -props.severityPerDayImmune * healMult;
            if (immuneReduction <= 0f || hediff.Severity <= 0f) return false;

            hediff.Severity = Mathf.Max(0f, hediff.Severity - immuneReduction);
            if (hediff.Severity <= HediffRemoveSeverityThreshold)
            {
                pawn.health.RemoveHediff(hediff);
                return true;
            }
            return true;
        }
    }
}
