using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public enum RaidLaunchTargetKind : byte
    {
        PlayerColony,
        PlayerSimulated,
        NPC,
    }

    /// <summary>Single source of truth for minRaidRatio launch eligibility. Used at assess and finalize.</summary>
    public static class RaidLaunchGate
    {
        /// <summary>Colony vs storyteller points: starting required ratio.</summary>
        public const float ColonyRaidRequiredRatioFresh = 0.7f;
        /// <summary>Subtracted from fresh ratio per quiet day since the colony was last picked as a WD raid target.</summary>
        public const float ColonyRaidQuietRatioStepPerDay = 0.2f;

        public struct GateResult
        {
            public bool passed;
            public float rawAttPower;
            public float effectiveAtt;
            public float defTotal;
            public float ratio;
            public float efficiency;
            public bool bypassedMinRatio;
            /// <summary>Ratio threshold used for this evaluate (outpost/NPC: minRaidRatio; colony: 0.7 then −0.2/quiet day).</summary>
            public float requiredRatio;
            /// <summary>Non-colony only: defender snapshot from this Evaluate (null for colony / early returns).</summary>
            public RaidDefenderSnapshot defenders;
        }

        public static RaidLaunchTargetKind ClassifyTarget(WorldObject target)
        {
            // Surface landed Odyssey gravships are normal player Settlements (HasMap + GravEngine on map).
            // They already get CompViralSpread via Settlement_Patch and route here as PlayerColony — no special case.
            if (target is Settlement s && s.HasMap && target.Faction?.IsPlayer == true)
                return RaidLaunchTargetKind.PlayerColony;
            if (target is WorldObject_WD_Outpost)
                return RaidLaunchTargetKind.PlayerSimulated;
            return RaidLaunchTargetKind.NPC;
        }

        public static float SumAvailableAttPower(Settlement attacker, List<WorldObject> attAllies, WorldDominationSettings seth)
        {
            float total = WorldActions_Utils.GetAvailableRaidStrength(attacker?.GetComponent<CompViralSpread>(), seth);
            if (attAllies == null) return total;
            for (int i = 0; i < attAllies.Count; i++)
                total += WorldActions_Utils.GetAvailableRaidStrength(attAllies[i]?.GetComponent<CompViralSpread>(), seth);
            return total;
        }

        /// <summary>Storyteller threat points as colony defense proxy; denom never below 1.</summary>
        public static float GetColonyStorytellerDefense(WorldObject target)
        {
            if (target is Settlement settlement && settlement.Map != null)
            {
                float baseline = StorytellerUtility.DefaultThreatPointsNow(settlement.Map);
                return baseline > 0f ? baseline : 1f;
            }
            return 1f;
        }

        /// <summary>
        /// Colony-only required attacker / storyteller-points ratio.
        /// Separate from <see cref="WorldDominationSettings.minRaidRatio"/> (outposts / NPC).
        /// Starts at <see cref="ColonyRaidRequiredRatioFresh"/>, then −<see cref="ColonyRaidQuietRatioStepPerDay"/>
        /// per quiet day (floor 0). Quiet = days since <see cref="CompViralSpread.lastPlayerColonyWdRaidPickTick"/>
        /// (last successful WD raid launch / founding shield, or since game start if never stamped).
        /// </summary>
        public static float GetColonyRequiredRaidRatio(CompViralSpread colonyComp, WorldDominationSettings seth)
        {
            float quietDays = GetColonyQuietDays(colonyComp);
            return Mathf.Max(0f, ColonyRaidRequiredRatioFresh - ColonyRaidQuietRatioStepPerDay * quietDays);
        }

        public static float GetColonyQuietDays(CompViralSpread colonyComp)
        {
            if (Find.TickManager == null) return 999f;
            if (colonyComp == null || colonyComp.lastPlayerColonyWdRaidPickTick < 0)
                return Find.TickManager.TicksGame / 60000f;
            return Mathf.Max(0f, (Find.TickManager.TicksGame - colonyComp.lastPlayerColonyWdRaidPickTick) / 60000f);
        }

        /// <summary>Assess-time gate. Uses estimated path efficiency unless <paramref name="pathTravelTicks"/> is supplied.
        /// When <paramref name="efficiencyOverride"/> is ≥ 0, that value is used instead (drop-pod crow-flies attrition).
        /// When <paramref name="requiredRatioOverride"/> is ≥ 0 for colony gate, that threshold is used (locked at assess pick).</summary>
        public static GateResult Evaluate(
            Settlement attacker,
            WorldObject target,
            RaidLaunchTargetKind kind,
            List<WorldObject> attAllies,
            Dictionary<Faction, List<WorldObject>> lookup,
            WorldComponent_SpreadManager manager,
            WorldDominationSettings seth,
            float pathTravelTicks = -1f,
            float efficiencyOverride = -1f,
            float requiredRatioOverride = -1f)
        {
            var result = new GateResult();
            if (attacker == null || target == null || seth == null)
                return result;

            if (kind == RaidLaunchTargetKind.PlayerColony)
            {
                result.defTotal = GetColonyStorytellerDefense(target);
                result.rawAttPower = SumAvailableAttPower(attacker, attAllies, seth);
                if (efficiencyOverride >= 0f)
                    result.efficiency = efficiencyOverride;
                else if (pathTravelTicks >= 0f)
                    result.efficiency = ResolveEfficiency(attacker.Tile, target.Tile, seth, attacker.Faction, pathTravelTicks);
                else
                    result.efficiency = ResolveEfficiencyForRaidAssess(attacker, target, seth);
                result.effectiveAtt = result.rawAttPower * result.efficiency;
                result.requiredRatio = requiredRatioOverride >= 0f
                    ? requiredRatioOverride
                    : GetColonyRequiredRaidRatio(target.GetComponent<CompViralSpread>(), seth);
                result.ratio = result.effectiveAtt / Mathf.Max(1f, result.defTotal);
                result.passed = result.ratio >= result.requiredRatio;
                return result;
            }

            result.rawAttPower = SumAvailableAttPower(attacker, attAllies, seth);
            var defSnap = Raid_MathSnapshot.BuildDefenders(target, attacker, attacker.Faction, lookup, manager, seth);
            result.defenders = defSnap;
            result.defTotal = defSnap.Total;
            float outpostDenom = result.defTotal > 0f ? result.defTotal : 1f;

            result.efficiency = efficiencyOverride >= 0f
                ? efficiencyOverride
                : pathTravelTicks >= 0f
                    ? ResolveEfficiency(attacker.Tile, target.Tile, seth, attacker.Faction, pathTravelTicks)
                    : ResolveEfficiencyForRaidAssess(attacker, target, seth);
            result.effectiveAtt = result.rawAttPower * result.efficiency;
            result.ratio = result.effectiveAtt / outpostDenom;
            result.requiredRatio = seth.minRaidRatio;
            result.passed = result.ratio >= result.requiredRatio;
            return result;
        }

        /// <summary>
        /// Assess-time efficiency: T3/T4 over-water → drop-pod attrition (skip water A*); otherwise prep path/heuristic.
        /// </summary>
        private static float ResolveEfficiencyForRaidAssess(Settlement attacker, WorldObject target, WorldDominationSettings seth)
        {
            CompViralSpread attComp = attacker?.GetComponent<CompViralSpread>();
            if (TravelUtils.ShouldRaidUseDropPodsOverWater(attacker, attComp, target, seth)
                && TravelUtils.TryDropPodRaidEfficiency(
                    attacker.Tile, target.Tile, seth, attacker.Faction, out float podEff, out _))
            {
                return podEff;
            }
            return ResolveEfficiency(attacker.Tile, target.Tile, seth, attacker.Faction, -1f);
        }

        /// <summary>
        /// Feature A/B (target-of-opportunity / marauding) ratio: a single already-dispatched traveler's current
        /// strength vs a candidate's defense (no attacker-side ally pooling — the traveler is not re-launching a
        /// fresh coalition raid). Colony candidates use storyteller defense like <see cref="Evaluate"/>; everyone
        /// else reuses <see cref="Raid_MathSnapshot.BuildDefenders"/> so ally-aware NPC/outpost defense stays consistent
        /// with normal raid-launch math.
        /// </summary>
        public static float EvaluateTravelerVsCandidateRatio(
            WorldObject_Traveler traveler,
            WorldObject candidate,
            RaidLaunchTargetKind kind,
            Dictionary<Faction, List<WorldObject>> lookup,
            WorldComponent_SpreadManager manager,
            WorldDominationSettings seth,
            out float defTotal)
        {
            defTotal = 1f;
            if (traveler == null || candidate == null || seth == null) return 0f;
            float attPower = Mathf.Max(0f, traveler.travelerStrength);
            if (attPower <= 0f) return 0f;

            if (kind == RaidLaunchTargetKind.PlayerColony)
            {
                defTotal = Mathf.Max(1f, GetColonyStorytellerDefense(candidate));
                return attPower / defTotal;
            }

            var snap = Raid_MathSnapshot.BuildDefenders(candidate, traveler, traveler.Faction, lookup, manager, seth);
            defTotal = snap.Total > 0f ? snap.Total : 1f;
            return attPower / defTotal;
        }

        public static float ResolveEfficiency(int startTile, int destTile, WorldDominationSettings seth, Faction faction, float pathTravelTicks)
        {
            if (pathTravelTicks >= 0f && TravelUtils.TryEfficiencyFromPathTravelTicks(pathTravelTicks, seth, faction, out float effFromPath))
                return effFromPath;
            // Assess-time: honor travelPrepExactPercent (launch still uses real path ticks when supplied).
            return TravelUtils.ResolvePrepEfficiency(startTile, destTile, seth, faction, WorldObject_Traveler.DefaultTicksPerMove);
        }
    }
}
