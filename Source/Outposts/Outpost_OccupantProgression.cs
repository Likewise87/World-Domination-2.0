using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Skill XP, biological aging, ability CD finalize, Disappears wear-off, virtual healing, and (with Combat Extended) passive Primary ammo for outpost occupants and stored animals (real pawns, not world-ticked). Mutates pawns; occupant changes refresh via <see cref="WorldObject_WD_Outpost.NotifyVirtualPawnsChanged"/>.</summary>
    public static class Outpost_OccupantProgression
    {
        public const float EventXpResearchPerDay = 500f;
        public const float EventXpMortarPerShot = 500f;
        public const float EventXpAntiAirPerEngagement = 500f;
        public const float EventXpRapidResponsePerWin = 500f;
        public const float EventXpBuildCompleteT1 = 300f;
        public const float EventXpBuildCompleteT2 = 500f;
        public const float EventXpBuildCompleteT3 = 700f;

        private const float HediffRemoveSeverityThreshold = 0.001f;
        private const float FullImmunityThreshold = 1f - 0.0001f;

        private static readonly System.Reflection.FieldInfo ImmunizableImmunityField =
            AccessTools.Field(typeof(HediffComp_Immunizable), "immunity");

        /// <summary>Player outpost that launched this traveler, or null.</summary>
        public static WorldObject_WD_Outpost OutpostFromTravelerOrigin(WorldObject_Traveler traveler)
        {
            return traveler?.originObject as WorldObject_WD_Outpost;
        }

        /// <summary>After a successful production payout: grant settings XP to each relevant skill per occupant (skip if skill level &gt;= cap). Uses vanilla <see cref="SkillRecord.Learn"/> with <c>direct: false</c> so passions, traits, and global learning factor apply.</summary>
        public static void ApplyPayoutSkillXp(WorldObject_WD_Outpost outpost)
        {
            WorldDominationSettings settings = WorldDominationMod.settings;
            if (settings == null || outpost?.def == null) return;
            float xpAmt = settings.outpostOccupantSkillXpPerProductionCycle;
            if (xpAmt <= 0f) return;
            List<SkillDef> skills = WorldObject_WD_Outpost.GetRelevantSkillDefs(outpost.def);
            ApplySkillXp(outpost, xpAmt, skills);
        }

        /// <summary>Grant flat skill XP to each humanlike occupant (skip if skill level &gt;= settings cap). Passions / learning factor apply via <see cref="SkillRecord.Learn"/>.</summary>
        public static void ApplySkillXp(WorldObject_WD_Outpost outpost, float xpAmount, params SkillDef[] skills)
        {
            ApplySkillXp(outpost, xpAmount, (IEnumerable<SkillDef>)skills);
        }

        /// <summary>Grant flat skill XP to each humanlike occupant (skip if skill level &gt;= settings cap). Passions / learning factor apply via <see cref="SkillRecord.Learn"/>.</summary>
        public static void ApplySkillXp(WorldObject_WD_Outpost outpost, float xpAmount, IEnumerable<SkillDef> skills)
        {
            WorldDominationSettings settings = WorldDominationMod.settings;
            if (settings == null || outpost == null || outpost.Faction != Faction.OfPlayer) return;
            if (xpAmount <= 0f || skills == null) return;
            int maxLv = settings.GetEffectiveOutpostSkillLevel(settings.outpostOccupantSkillXpMaxLevel);
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
                        rec.Learn(xpAmount, direct: false);
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

        /// <summary>
        /// Split a shared XP pool equally among humanlike occupants still below the outpost XP level cap for <paramref name="skill"/>.
        /// Capped pawns receive nothing and do not dilute shares. Passions / learning factor apply via <see cref="SkillRecord.Learn"/>.
        /// </summary>
        public static void ApplySkillXpPoolSplit(WorldObject_WD_Outpost outpost, float poolXp, SkillDef skill)
        {
            WorldDominationSettings settings = WorldDominationMod.settings;
            if (settings == null || outpost == null || outpost.Faction != Faction.OfPlayer) return;
            if (poolXp <= 0f || skill == null) return;
            int maxLv = settings.GetEffectiveOutpostSkillLevel(settings.outpostOccupantSkillXpMaxLevel);
            List<Pawn> occ = outpost.Occupants;
            if (occ == null || occ.Count == 0) return;

            var recipients = new List<SkillRecord>();
            for (int i = 0; i < occ.Count; i++)
            {
                Pawn p = occ[i];
                if (p == null || p.Destroyed || p.Dead) continue;
                if (p.RaceProps == null || !p.RaceProps.Humanlike) continue;
                if (p.skills == null) continue;
                SkillRecord rec = p.skills.GetSkill(skill);
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
                recipients.Add(rec);
            }

            int n = recipients.Count;
            if (n <= 0) return;
            float share = poolXp / n;
            if (share <= 0f) return;

            bool any = false;
            for (int i = 0; i < n; i++)
            {
                try
                {
                    recipients[i].Learn(share, direct: false);
                    any = true;
                }
                catch
                {
                    // Modded skills / bad state — skip this record
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
        /// Once per in-game day (ungated): finalize ability cooldowns whose absolute end tick has elapsed,
        /// catch up charge pools, and pulse psychic entropy. Does not call <see cref="Ability.AbilityTick"/>.
        /// </summary>
        public static void TickOccupantsAbilitiesOneDay(WorldObject_WD_Outpost outpost)
        {
            if (outpost?.Occupants == null || outpost.Occupants.Count == 0) return;

            for (int i = 0; i < outpost.Occupants.Count; i++)
            {
                Pawn p = outpost.Occupants[i];
                if (p == null || p.Destroyed || p.Dead) continue;
                if (p.RaceProps == null || !p.RaceProps.Humanlike) continue;

                try
                {
                    FinalizePawnAbilitiesOneDay(p);
                }
                catch
                {
                    // Modded abilities must not break the daily pass
                }

                try
                {
                    if (p.psychicEntropy != null)
                        p.psychicEntropy.PsychicEntropyTrackerTickInterval(GenDate.TicksPerDay);
                }
                catch
                {
                    // Royalty / mod psychic trackers
                }
            }
        }

        private static void FinalizePawnAbilitiesOneDay(Pawn pawn)
        {
            Pawn_AbilityTracker tracker = pawn.abilities;
            if (tracker == null) return;
            List<Ability> abs = tracker.AllAbilitiesForReading;
            if (abs == null || abs.Count == 0) return;

            for (int i = 0; i < abs.Count; i++)
            {
                Ability ability = abs[i];
                if (ability?.def == null) continue;
                try
                {
                    FinalizeAbilityCooldownOneDay(ability);
                }
                catch
                {
                    // Per-ability isolation
                }
            }
        }

        private static void FinalizeAbilityCooldownOneDay(Ability ability)
        {
            AbilityDef def = ability.def;
            if (def == null || def.hasExternallyHandledCooldown) return;
            if (!ability.HasCooldown || !ability.OnCooldown) return;
            if (ability.CooldownTicksRemaining > 0) return;

            // Absolute end-tick already elapsed; complete CD without ready letter / AbilityTick FX.
            if (def.cooldownPerCharge && ability.UsesCharges)
            {
                ability.RemainingCharges = ability.maxCharges;
                ability.ResetCooldown();
                return;
            }

            ability.ResetCooldown();
            if (ability.UsesCharges && ability.RemainingCharges < ability.maxCharges)
                ability.RemainingCharges = ability.maxCharges;
        }

        /// <summary>
        /// Once per in-game day (ungated): advance every <see cref="HediffComp_Disappears"/> by one day
        /// (ability buffs, anesthetic, etc.). Independent of <c>occupantsNeedHealing</c>.
        /// </summary>
        public static void TickOccupantsDisappearsOneDay(WorldObject_WD_Outpost outpost)
        {
            if (outpost?.Occupants == null || outpost.Occupants.Count == 0) return;

            for (int oi = 0; oi < outpost.Occupants.Count; oi++)
            {
                Pawn p = outpost.Occupants[oi];
                if (p == null || p.Destroyed || p.Dead) continue;
                if (p.RaceProps == null || !p.RaceProps.Humanlike) continue;
                List<Hediff> hediffs = p.health?.hediffSet?.hediffs;
                if (hediffs == null || hediffs.Count == 0) continue;

                for (int i = hediffs.Count - 1; i >= 0; i--)
                {
                    Hediff hediff = hediffs[i];
                    if (hediff == null || hediff.pawn == null) continue;
                    try
                    {
                        TryAdvanceDisappearsOneDay(p, hediff);
                    }
                    catch
                    {
                        // Modded hediffs
                    }
                }
            }
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

        /// <summary>
        /// Once per in-game day: slowly repair damaged Vehicle Framework hulls in
        /// <see cref="WorldObject_WD_Outpost.StoredAnimalsAndVehicles"/> (percent of each damaged part's MaxHealth).
        /// </summary>
        public static void TickStoredVehiclesRepairOneDay(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return;
            WorldDominationSettings settings = WorldDominationMod.settings;
            if (settings == null) return;
            float pctPerDay = settings.outpostVehicleRepairHealthPercentPerDay;
            if (pctPerDay <= 0f) return;

            List<Pawn> list = outpost.StoredAnimalsAndVehicles;
            if (list == null || list.Count == 0) return;

            for (int i = 0; i < list.Count; i++)
            {
                Pawn p = list[i];
                if (p == null || p.Destroyed || p.Dead) continue;
                if (!VehicleFrameworkOutpostDissolveCompat.IsVehicleFrameworkVehiclePawn(p)) continue;
                VehicleFrameworkOutpostDissolveCompat.TryRepairVehicleOneDay(p, pctPerDay);
            }
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

        /// <summary>Once per in-game day: heal injuries / immunizable and advance natural wear-off for mothballed prisoners.</summary>
        public static void TickPrisonersVirtualHealingOneDay(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null || !outpost.PrisonersNeedHealing) return;
            WorldDominationSettings settings = WorldDominationMod.settings;
            if (settings == null) return;

            // Injury heal may be 0; natural wear-off / immunizable recovery still runs.
            float baseSeverityPerDay = Mathf.Max(0f, settings.outpostOccupantHealSeverityPerDay);
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

        /// <summary>
        /// Once per in-game day: when Combat Extended is active and the experimental setting is on,
        /// grant basic ammo for each Occupant's ranged Primary (2 magazines/day, cap 8). See <see cref="OutpostCeAmmoCompat"/>.
        /// </summary>
        public static void TickOccupantsPassiveAmmoOneDay(WorldObject_WD_Outpost outpost)
        {
            OutpostCeAmmoCompat.TickOccupantsPassiveAmmoOneDay(outpost);
        }

        /// <summary>Once per in-game day: heal injuries / immunizable and advance natural wear-off for mothballed occupants.</summary>
        public static void TickOccupantsVirtualHealingOneDay(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null || !outpost.OccupantsNeedHealing) return;
            WorldDominationSettings settings = WorldDominationMod.settings;
            if (settings == null) return;

            // Injury heal may be 0; natural wear-off / immunizable recovery still runs.
            float baseSeverityPerDay = Mathf.Max(0f, settings.outpostOccupantHealSeverityPerDay);
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
                    if (IsWearOffCandidate(hediff))
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
                    if (TryAdvanceNaturalWearOff(pawn, hediff))
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

            // Prefer natural recovery rate when present (recovery-only).
            if (TryApplyRecoverySeverityPerDay(pawn, hediff, healMult: 1f))
                return true;

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

            bool changed = false;
            float current = immComp.Immunity;

            // Phase 1: fully immune — fade severity each day until removed (vanilla post-immunity behavior).
            if (current >= FullImmunityThreshold && props.severityPerDayImmune < 0f)
            {
                if (TryFadeImmuneHediff(pawn, hediff, healMult, props))
                    changed = true;
            }
            else
            {
                // Phase 2: gain immunity while still sick.
                float gainPerDay = props.immunityPerDaySick;
                if (gainPerDay > 0f)
                {
                    float gain = gainPerDay * healMult;
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
                    if (next > current + 0.0001f && ImmunizableImmunityField != null)
                    {
                        ImmunizableImmunityField.SetValue(immComp, next);
                        changed = true;
                        current = next;
                    }

                    if (current >= FullImmunityThreshold && props.severityPerDayImmune < 0f
                        && TryFadeImmuneHediff(pawn, hediff, healMult, props))
                        changed = true;
                }
            }

            // Natural immunizable severity curve for one day (e.g. ToxicBuildup severityPerDayNotImmune).
            // Recovery-only; hospital/expert mult applies (medical care).
            if (hediff != null && hediff.pawn != null && TryApplyImmunizableSeverityRecovery(pawn, hediff, immComp, healMult))
                changed = true;

            return changed;
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

        /// <summary>One day of immunizable <see cref="HediffComp_Immunizable.SeverityChangePerDay"/> when recovering.</summary>
        private static bool TryApplyImmunizableSeverityRecovery(Pawn pawn, Hediff hediff, HediffComp_Immunizable immComp, float healMult)
        {
            if (pawn == null || hediff == null || immComp == null || hediff.pawn == null) return false;
            float delta;
            try
            {
                delta = immComp.SeverityChangePerDay();
            }
            catch
            {
                return false;
            }
            if (delta >= 0f) return false;
            float apply = delta * Mathf.Max(0f, healMult);
            if (apply >= 0f) return false;
            return ApplySeverityDelta(pawn, hediff, apply);
        }

        // --- Natural wear-off (closed candidates; Disappears / recovery SeverityPerDay; no hospital mult) ---

        /// <summary>Refeed recovery when Food is topped (Malnutrition has no SeverityPerDay / Disappears).</summary>
        private const float MalnutritionSeverityRecoveryPerDay = 0.1f;

        private static bool IsWearOffCandidate(Hediff hediff)
        {
            if (hediff?.def == null) return false;
            if (hediff is Hediff_High) return true;
            HediffDef def = hediff.def;
            if (def == HediffDefOf.FoodPoisoning) return true;
            if (def == HediffDefOf.AlcoholHigh) return true;
            if (def == HediffDefOf.Hangover) return true;
            if (def == HediffDefOf.ToxicBuildup) return true;
            if (def == HediffDefOf.Hypothermia) return true;
            if (def == HediffDefOf.Heatstroke) return true;
            if (def == HediffDefOf.Malnutrition) return true;
            return false;
        }

        /// <summary>
        /// Closed-set wear-off (not Disappears — that runs ungated in <see cref="TickOccupantsDisappearsOneDay"/>).
        /// Hypothermia/Heatstroke: immediate clear. Malnutrition: refeed when Food full. Else recovery SeverityPerDay.
        /// </summary>
        private static bool TryAdvanceNaturalWearOff(Pawn pawn, Hediff hediff)
        {
            if (pawn == null || hediff?.def == null || hediff.pawn == null) return false;
            if (!IsWearOffCandidate(hediff)) return false;

            // Sheltered at outpost: ambient temp stress cannot persist.
            if (hediff.def == HediffDefOf.Hypothermia || hediff.def == HediffDefOf.Heatstroke)
            {
                pawn.health.RemoveHediff(hediff);
                return true;
            }

            if (hediff.def == HediffDefOf.Malnutrition)
                return TryRecoverMalnutritionOneDay(pawn, hediff);

            // ToxicBuildup is immunizable; recovery severity is handled in TryAdvanceImmunity.
            if (hediff.def == HediffDefOf.ToxicBuildup)
                return false;

            // Disappears handled ungated elsewhere — avoid double −1 day.
            return TryApplyRecoverySeverityPerDay(pawn, hediff, healMult: 1f);
        }

        private static bool TryRecoverMalnutritionOneDay(Pawn pawn, Hediff hediff)
        {
            if (pawn?.needs == null || hediff == null) return false;
            Need food = pawn.needs.TryGetNeed(NeedDefOf.Food);
            if (food == null || food.CurLevelPercentage < 0.99f) return false;
            return ApplySeverityDelta(pawn, hediff, -MalnutritionSeverityRecoveryPerDay);
        }

        private static bool TryAdvanceDisappearsOneDay(Pawn pawn, Hediff hediff)
        {
            HediffComp_Disappears disappears = hediff.TryGetComp<HediffComp_Disappears>();
            if (disappears == null) return false;

            int lostPerTick = Mathf.Max(1, disappears.TicksLostPerTick);
            int dayTicks = GenDate.TicksPerDay * lostPerTick;
            disappears.ticksToDisappear -= dayTicks;
            if (disappears.ticksToDisappear > 0 && !disappears.CompShouldRemove)
                return true;

            pawn.health.RemoveHediff(hediff);
            return true;
        }

        /// <summary>Apply one day of <see cref="HediffComp_SeverityPerDay.SeverityChangePerDay"/> when recovering (delta &lt; 0).</summary>
        private static bool TryApplyRecoverySeverityPerDay(Pawn pawn, Hediff hediff, float healMult)
        {
            HediffComp_SeverityPerDay sevComp = hediff.TryGetComp<HediffComp_SeverityPerDay>();
            if (sevComp == null) return false;
            float delta;
            try
            {
                delta = sevComp.SeverityChangePerDay();
            }
            catch
            {
                return false;
            }
            if (delta >= 0f) return false;
            float apply = delta * Mathf.Max(0f, healMult);
            if (apply >= 0f) return false;
            return ApplySeverityDelta(pawn, hediff, apply);
        }

        private static bool ApplySeverityDelta(Pawn pawn, Hediff hediff, float delta)
        {
            if (pawn == null || hediff == null || hediff.pawn == null || delta == 0f) return false;
            float before = hediff.Severity;
            hediff.Severity = Mathf.Max(0f, before + delta);
            if (hediff.Severity <= HediffRemoveSeverityThreshold)
            {
                pawn.health.RemoveHediff(hediff);
                return true;
            }
            return hediff.Severity < before - 0.0001f || hediff.Severity > before + 0.0001f;
        }
    }
}
