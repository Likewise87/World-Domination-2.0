using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Experimental colony raids: when a settlement notices the colony, pick the nearest same-faction
    /// settlement that still passes the colony strength gate to actually launch (faction intent, local execution).
    /// </summary>
    public static class RaidColonyExecutor
    {
        private const float DistTieEpsilon = 0.05f;

        /// <summary>
        /// Returns the best executor for a player-colony raid, or <paramref name="scheduler"/> when none better / none eligible.
        /// Caller must only invoke when experimental colony gate is active.
        /// <paramref name="executorAllies"/> is set for the returned settlement (including scheduler when it wins).
        /// </summary>
        public static Settlement SelectExecutor(
            Settlement scheduler,
            WorldObject colony,
            Dictionary<Faction, List<WorldObject>> lookup,
            WorldComponent_SpreadManager manager,
            WorldDominationSettings seth,
            out List<WorldObject> executorAllies,
            float requiredRatioOverride = -1f)
        {
            executorAllies = null;
            if (scheduler == null || colony == null || seth == null || scheduler.Faction == null)
                return scheduler;

            Settlement best = null;
            float bestDist = float.MaxValue;
            float bestMargin = float.MinValue;
            List<WorldObject> bestAllies = null;

            Faction faction = scheduler.Faction;
            foreach (WorldObject wo in WorldActions_Utils.GetFactionObjects(lookup, faction))
            {
                if (!(wo is Settlement candidate) || candidate.Destroyed || !candidate.Spawned)
                    continue;
                if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(candidate))
                    continue;

                var candComp = candidate.GetComponent<CompViralSpread>();
                if (candComp == null) continue;
                // Scheduler already committed to this attempt; other bases must be off raid CD.
                if (candidate != scheduler && candComp.IsRaidOnCooldown) continue;

                float range = SettlementAttackRangeUtil.GetNpcSettlementAttackRangeWithZeal(candidate, seth, manager);

                float dist = WorldActions_Utils.GetDistance(candidate.Tile, colony.Tile, manager);
                if (dist > range) continue;

                var allies = new List<WorldObject>(
                    Raid_ReinforcementLogic.GetReinforcements(candidate, null, AllyRadiusUtil.GetEffective(candidate, seth, manager), lookup, manager));
                var gate = RaidLaunchGate.Evaluate(
                    candidate,
                    colony,
                    RaidLaunchTargetKind.PlayerColony,
                    allies,
                    lookup,
                    manager,
                    seth,
                    requiredRatioOverride: requiredRatioOverride);
                if (!gate.passed) continue;

                float margin = gate.ratio - gate.requiredRatio;
                bool better = best == null
                    || dist < bestDist - DistTieEpsilon
                    || (Mathf.Abs(dist - bestDist) <= DistTieEpsilon && margin > bestMargin);
                if (!better) continue;

                best = candidate;
                bestDist = dist;
                bestMargin = margin;
                bestAllies = allies;
            }

            if (best == null)
                return scheduler;

            executorAllies = bestAllies;
            return best;
        }

        /// <summary>
        /// Rebinds <paramref name="eval"/> to launch from <paramref name="executor"/> (strength, allies, CD ownership).
        /// </summary>
        internal static void ApplyExecutor(PendingRaidEvaluation eval, Settlement executor, List<WorldObject> alliesOrNull)
        {
            if (eval == null || executor == null) return;
            eval.attacker = executor;
            eval.attComp = executor.GetComponent<CompViralSpread>();
            if (alliesOrNull != null)
                eval.attAllies = alliesOrNull;
            else if (eval.seth != null)
            {
                eval.attAllies = new List<WorldObject>(
                    Raid_ReinforcementLogic.GetReinforcements(
                        executor, null, AllyRadiusUtil.GetEffective(executor, eval.seth, eval.manager), eval.objectsWithComp, eval.manager));
            }
            else
                eval.attAllies = new List<WorldObject>();

            float total = WorldActions_Utils.GetAvailableRaidStrength(eval.attComp, eval.seth);
            if (eval.attAllies != null)
            {
                for (int i = 0; i < eval.attAllies.Count; i++)
                    total += WorldActions_Utils.GetAvailableRaidStrength(
                        eval.attAllies[i]?.GetComponent<CompViralSpread>(), eval.seth);
            }
            eval.totalAvailableAttPower = total;
        }
    }
}
