using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Offensive-strength budget for outpost withdraw and manual defense deploy.</summary>
    public static class OutpostStrengthBudget
    {
        private const float Epsilon = 0.05f;
        private const float WithdrawBudgetToleranceFraction = 0.05f;

        /// <summary>Budget for leaving the outpost: same capped current offense used by Launch Attack.</summary>
        public static float GetAvailable(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null || outpost.Destroyed) return 0f;
            return WorldActions_Utils.GetAvailableRaidStrength(
                outpost.GetComponent<CompViralSpread>(),
                WorldDominationMod.settings);
        }

        /// <summary>
        /// Withdraw-only budget: current outpost offensive strength in uncapped space.
        /// Keeps current damage/loss state, but removes the 1500 offensive cap and garrison retain floor.
        /// </summary>
        public static float GetAvailableForWithdraw(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null || outpost.Destroyed) return 0f;
            CompViralSpread comp = outpost.GetComponent<CompViralSpread>();
            float currentOffensiveCapped = Mathf.Max(0f, comp?.strength ?? 0f);
            float cappedMax = Mathf.Max(0f, outpost.GetTargetStrength());
            float uncappedMax = Mathf.Max(0f, outpost.GetTargetStrengthUncapped());
            if (currentOffensiveCapped <= 0f || uncappedMax <= 0f) return 0f;
            if (cappedMax <= Epsilon) return uncappedMax;
            float remainingRatio = Mathf.Clamp01(currentOffensiveCapped / cappedMax);
            return remainingRatio * uncappedMax;
        }

        /// <summary>Budget for manual defense deploy: full current offensive strength (no retain floor).</summary>
        public static float GetAvailableForDefense(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null || outpost.Destroyed) return 0f;
            CompViralSpread comp = outpost.GetComponent<CompViralSpread>();
            return Mathf.Max(0f, comp?.strength ?? 0f);
        }

        /// <summary>Same contribution as composition max (<see cref="WorldObject_WD_Outpost.GetTargetStrength"/>).</summary>
        public static float GetPawnCost(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead) return 0f;
            if (pawn.RaceProps?.Humanlike == true || OutpostPawnClassificationUtil.IsMechanoidWorker(pawn))
            {
                VirtualPawnSummary summary = VirtualPawnSummary.FromPawn(pawn);
                return summary != null ? summary.CombatStrength : 0f;
            }
            return WorldObject_WD_Outpost.GetStoredTransportCombatStrength(pawn);
        }

        public static float SumCost(IReadOnlyList<Pawn> pawns)
        {
            if (pawns == null || pawns.Count == 0) return 0f;
            float sum = 0f;
            for (int i = 0; i < pawns.Count; i++)
                sum += GetPawnCost(pawns[i]);
            return sum;
        }

        public static float SumCost(IReadOnlyList<PlayerPawnRosterEntry> entries)
        {
            if (entries == null || entries.Count == 0) return 0f;
            float sum = 0f;
            for (int i = 0; i < entries.Count; i++)
            {
                PlayerPawnRosterEntry e = entries[i];
                if (e?.pawn == null) continue;
                sum += GetPawnCost(e.pawn);
            }
            return sum;
        }

        public static bool IsUnderBudget(float used, float available)
            => used <= available + Epsilon;

        public static float Excess(float used, float available)
            => Mathf.Max(0f, used - available);

        public static float GetWithdrawEffectiveLimit(float available)
            => Mathf.Max(0f, available * (1f + WithdrawBudgetToleranceFraction));

        public static bool IsUnderWithdrawBudget(float used, float available)
            => used <= GetWithdrawEffectiveLimit(available) + Epsilon;

        public static float WithdrawExcess(float used, float available)
            => Mathf.Max(0f, used - GetWithdrawEffectiveLimit(available));

        /// <summary>Experimental flag: over-budget outpost withdrawals open the fate picker.</summary>
        public static bool WithdrawBudgetEnabled
            => WorldDominationMod.settings?.experimentalOutpostWithdrawStrengthBudget == true;

        /// <summary>Experimental flag: manual defense deploy enforces a selection strength budget.</summary>
        public static bool DefenseDeployBudgetEnabled
            => WorldDominationMod.settings?.experimentalOutpostDefenseDeployBudget == true;

        public static bool NeedsWithdrawBudgetGate(WorldObject_WD_Outpost outpost, IReadOnlyList<PlayerPawnRosterEntry> entries)
        {
            if (!WithdrawBudgetEnabled) return false;
            if (outpost == null || entries == null || entries.Count == 0) return false;
            float available = GetAvailableForWithdraw(outpost);
            float used = SumCost(entries);
            return !IsUnderWithdrawBudget(used, available);
        }

        /// <summary>Remove from outpost and destroy. Not left working, not on a caravan.</summary>
        public static void DestroyLostPawns(WorldObject_WD_Outpost outpost, IReadOnlyList<Pawn> lost)
        {
            if (outpost == null || lost == null || lost.Count == 0) return;
            for (int i = 0; i < lost.Count; i++)
            {
                Pawn p = lost[i];
                if (p == null || p.Destroyed) continue;
                try
                {
                    Pawn removed = outpost.RemovePawn(p);
                    if (removed == null)
                        removed = outpost.RemoveStoredAnimalOrVehicle(p);
                    if (removed == null)
                        removed = outpost.RemoveStoredMechanoid(p);
                    if (removed != null && !removed.Destroyed)
                        removed.Destroy(DestroyMode.Vanish);
                    else if (!p.Destroyed)
                        p.Destroy(DestroyMode.Vanish);
                }
                catch (System.Exception ex)
                {
                    Log.Warning($"[TSA WD] Failed to destroy lost extract pawn {p.LabelShortCap}: {ex.Message}");
                }
            }
            outpost.NotifyVirtualPawnsChanged();
            // Mirrors every other removal helper (RemovePawnsAsCaravan, Outpost_RemovePawn.DoRemove, etc.):
            // an outpost that loses its last occupant here would otherwise never self-destroy.
            if (outpost.Occupants != null && outpost.Occupants.Count == 0 && !outpost.Destroyed)
                outpost.Destroy();
        }
    }
}
