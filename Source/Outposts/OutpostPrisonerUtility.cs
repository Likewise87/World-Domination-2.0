using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Harvest, virtual capture, and Warden resistance ticks for outpost-held prisoners.</summary>
    public static class OutpostPrisonerUtility
    {
        private const int VirtualCaptiveSafetyCeiling = 12;
        private const float StrengthPerEstimatedAttacker = 50f;

        public static bool IsRecruitableCapturable(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead) return false;
            if (pawn.RaceProps?.Humanlike != true) return false;
            if (OutpostPawnClassificationUtil.IsMechanoidWorker(pawn)) return false;
            if (VehicleFrameworkOutpostDissolveCompat.IsVehicleFrameworkVehiclePawn(pawn)) return false;
            if (pawn.guest != null && !pawn.guest.Recruitable) return false;
            return true;
        }

        /// <summary>Downed hostiles and already-captured colony prisoners on a defense map.</summary>
        public static bool IsManualDefenseCapturable(Pawn pawn, Faction playerFaction)
        {
            if (!IsRecruitableCapturable(pawn)) return false;
            if (pawn.Faction != null && pawn.Faction.IsPlayer) return false;
            if (pawn.IsPrisonerOfColony) return true;
            if (!pawn.Downed) return false;
            return playerFaction != null && pawn.HostileTo(playerFaction);
        }

        public static int HarvestCaptivesFromDefenseMap(WorldObject_WD_Outpost outpost, Map map)
        {
            if (outpost == null || outpost.Destroyed || !outpost.TakePrisoners || map?.mapPawns == null) return 0;
            Faction player = Faction.OfPlayer;
            if (player == null) return 0;

            var toCapture = new List<Pawn>();
            IReadOnlyList<Pawn> all = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < all.Count; i++)
            {
                Pawn pawn = all[i];
                if (IsManualDefenseCapturable(pawn, player))
                    toCapture.Add(pawn);
            }

            int captured = 0;
            for (int i = 0; i < toCapture.Count; i++)
            {
                if (outpost.TryCaptureAsPrisoner(toCapture[i]))
                    captured++;
            }
            return captured;
        }

        /// <summary>
        /// Auto-resolve defender win: generate recruitable virtual captives from attacker losses.
        /// Volume is intentional; safety ceiling only.
        /// </summary>
        public static int GenerateVirtualCaptivesAfterDefense(
            WorldObject_WD_Outpost outpost,
            Faction attackerFaction,
            float attackerInitialStrength,
            float attackerLossPct)
        {
            return GenerateVirtualCaptivesFromEnemyLosses(
                outpost, attackerFaction, attackerInitialStrength, attackerLossPct);
        }

        /// <summary>
        /// Virtual captives from a defeated enemy strength pool (outpost defense or Rapid Response win).
        /// Count uses the existing strength/loss formula; pawn quality comes from a Combat pawn group
        /// generated at full <paramref name="enemyInitialStrength"/> (same affordability as a real raid).
        /// </summary>
        public static int GenerateVirtualCaptivesFromEnemyLosses(
            WorldObject_WD_Outpost outpost,
            Faction enemyFaction,
            float enemyInitialStrength,
            float enemyLossPct)
        {
            if (outpost == null || outpost.Destroyed || !outpost.TakePrisoners) return 0;
            if (enemyFaction == null || enemyFaction.IsPlayer) return 0;
            if (enemyLossPct <= 0.05f) return 0;

            int estimatedForce = Mathf.Max(1, Mathf.RoundToInt(enemyInitialStrength / StrengthPerEstimatedAttacker));
            // Roughly 25% of lost enemies become captives (volume ok for v1).
            int desired = Mathf.RoundToInt(estimatedForce * enemyLossPct * 0.25f);
            desired = Mathf.Clamp(desired, 0, VirtualCaptiveSafetyCeiling);
            if (desired <= 0 && enemyLossPct >= 0.35f && estimatedForce >= 2)
                desired = 1;
            if (desired <= 0) return 0;

            List<Pawn> group = TryGenerateCombatGroupAtStrength(enemyFaction, enemyInitialStrength);
            if (group == null || group.Count == 0) return 0;

            combatCaptiveCandidatesScratch.Clear();
            for (int i = 0; i < group.Count; i++)
            {
                Pawn p = group[i];
                if (IsRecruitableCapturable(p))
                    combatCaptiveCandidatesScratch.Add(p);
            }

            // Shuffle candidates so we do not always prefer generation order (often elites first).
            for (int i = combatCaptiveCandidatesScratch.Count - 1; i > 0; i--)
            {
                int j = Rand.RangeInclusive(0, i);
                Pawn tmp = combatCaptiveCandidatesScratch[i];
                combatCaptiveCandidatesScratch[i] = combatCaptiveCandidatesScratch[j];
                combatCaptiveCandidatesScratch[j] = tmp;
            }

            int take = Mathf.Min(desired, combatCaptiveCandidatesScratch.Count);
            int captured = 0;
            for (int i = 0; i < take; i++)
            {
                Pawn pawn = combatCaptiveCandidatesScratch[i];
                if (outpost.TryCaptureAsPrisoner(pawn))
                {
                    // Battle captives: multiple wounds (armor ignored) so they look fought, not fresh.
                    WD_OutpostDefenseSkirmishUtility.ApplySkirmishInjuries(pawn);
                    outpost.NotePrisonerMaybeNeedsHealing(pawn);
                    captured++;
                }
            }

            // Discard the rest of the synthetic raid group (and any failed captures).
            for (int i = 0; i < group.Count; i++)
            {
                Pawn p = group[i];
                if (p == null || p.Destroyed) continue;
                if (outpost.Prisoners != null && outpost.Prisoners.Contains(p)) continue;
                p.Destroy(DestroyMode.Vanish);
            }

            combatCaptiveCandidatesScratch.Clear();
            return captured;
        }

        private static readonly List<Pawn> combatCaptiveCandidatesScratch = new List<Pawn>(32);
        private static readonly List<Pawn> combatGroupScratch = new List<Pawn>(64);

        /// <summary>
        /// Combat pawn group at full enemy strength (floor: faction min Combat points), matching real raid generation.
        /// </summary>
        private static List<Pawn> TryGenerateCombatGroupAtStrength(Faction faction, float strength)
        {
            combatGroupScratch.Clear();
            if (faction?.def == null) return combatGroupScratch;

            float points = Mathf.Max(0f, strength);
            float minPoints = faction.def.MinPointsToGeneratePawnGroup(PawnGroupKindDefOf.Combat) * 1.05f;
            if (minPoints > 0f)
                points = Mathf.Max(points, minPoints);
            if (points <= 0f) return combatGroupScratch;

            try
            {
                var parms = new PawnGroupMakerParms
                {
                    groupKind = PawnGroupKindDefOf.Combat,
                    points = points,
                    faction = faction
                };
                foreach (Pawn p in PawnGroupMakerUtility.GeneratePawns(parms))
                {
                    if (p != null && !p.Destroyed)
                        combatGroupScratch.Add(p);
                }
            }
            catch
            {
                for (int i = 0; i < combatGroupScratch.Count; i++)
                {
                    Pawn p = combatGroupScratch[i];
                    if (p != null && !p.Destroyed)
                        p.Destroy(DestroyMode.Vanish);
                }
                combatGroupScratch.Clear();
            }

            return combatGroupScratch;
        }

        /// <summary>
        /// Player Rapid Response intercept win: deposit virtual captives at the origin RR outpost.
        /// Not gated by the clash letter notify setting.
        /// </summary>
        public static int TryCaptureFromRapidResponseWin(
            WorldObject_Traveler response,
            Faction enemyFaction,
            float enemyStrengthBefore,
            float enemyStrengthAfter)
        {
            if (response == null) return 0;
            if (!(response.originObject is WorldObject_WD_Outpost outpost) || outpost.Destroyed)
                return 0;
            if (!outpost.IsRapidResponseOutpost) return 0;
            if (outpost.Faction?.IsPlayer != true && response.Faction?.IsPlayer != true) return 0;
            if (!outpost.TakePrisoners) return 0;
            if (enemyFaction == null || enemyFaction.IsPlayer) return 0;

            float before = Mathf.Max(0f, enemyStrengthBefore);
            if (before <= 0f) return 0;
            float after = Mathf.Max(0f, enemyStrengthAfter);
            float lossPct = after <= 0.01f ? 1f : Mathf.Clamp01((before - after) / before);
            return GenerateVirtualCaptivesFromEnemyLosses(outpost, enemyFaction, before, lossPct);
        }

        public static bool IsEligibleRecruitCandidate(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead || pawn.guest == null) return false;
            if (!pawn.guest.Recruitable) return false;
            return pawn.guest.ExclusiveInteractionMode == PrisonerInteractionModeDefOf.AttemptRecruit;
        }

        /// <summary>True when this captive currently occupies one of the outpost's concurrent recruit slots.</summary>
        public static bool IsCurrentlyBeingRecruited(WorldObject_WD_Outpost outpost, Pawn pawn)
        {
            if (outpost == null || pawn == null || !IsEligibleRecruitCandidate(pawn)) return false;
            int slots = OutpostPrisonerResistanceScaling.GetConcurrentRecruitSlots(outpost);
            int seen = 0;
            List<Pawn> list = outpost.Prisoners;
            for (int i = 0; i < list.Count; i++)
            {
                Pawn cand = list[i];
                if (!IsEligibleRecruitCandidate(cand)) continue;
                if (cand == pawn) return seen < slots;
                seen++;
                if (seen >= slots) return false;
            }
            return false;
        }

        public static bool TryMovePrisoner(WorldObject_WD_Outpost outpost, Pawn pawn, int delta)
        {
            if (outpost == null || pawn == null || delta == 0) return false;
            List<Pawn> list = outpost.Prisoners;
            int idx = list.IndexOf(pawn);
            if (idx < 0) return false;
            int dest = idx + delta;
            if (dest < 0 || dest >= list.Count) return false;
            list.RemoveAt(idx);
            list.Insert(dest, pawn);
            return true;
        }

        public static bool TryMovePrisonerToExtreme(WorldObject_WD_Outpost outpost, Pawn pawn, bool toTop)
        {
            if (outpost == null || pawn == null) return false;
            List<Pawn> list = outpost.Prisoners;
            int idx = list.IndexOf(pawn);
            if (idx < 0) return false;
            if (toTop)
            {
                if (idx == 0) return false;
                list.RemoveAt(idx);
                list.Insert(0, pawn);
            }
            else
            {
                if (idx == list.Count - 1) return false;
                list.RemoveAt(idx);
                list.Add(pawn);
            }
            return true;
        }

        public static void TickPrisonerRecruitmentOneDay(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null || outpost.Destroyed) return;
            List<Pawn> list = outpost.Prisoners;
            if (list.Count == 0) return;

            float resistanceDrop = OutpostExpertUtility.GetRecruiterResistanceReductionPerDay(outpost);
            int slots = OutpostPrisonerResistanceScaling.GetConcurrentRecruitSlots(outpost);
            int used = 0;

            var toRecruit = new List<Pawn>();
            for (int i = 0; i < list.Count; i++)
            {
                Pawn pawn = list[i];
                if (!IsEligibleRecruitCandidate(pawn)) continue;
                if (used >= slots) break;
                used++;

                float resistance = pawn.guest.resistance;
                if (resistance > 0f && resistanceDrop > 0f)
                {
                    pawn.guest.resistance = Mathf.Max(0f, resistance - resistanceDrop);
                    resistance = pawn.guest.resistance;
                }

                if (resistance <= 0f)
                    toRecruit.Add(pawn);
            }

            if (toRecruit.Count > 0)
                outpost.RecruitPrisonersBatch(toRecruit);

            if (toRecruit.Count > 0 || (resistanceDrop > 0f && used > 0))
                Window_Prisoners.InvalidateCache();
        }
    }
}
