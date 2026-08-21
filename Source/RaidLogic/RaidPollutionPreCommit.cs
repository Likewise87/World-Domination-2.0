using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Pre-commit B cancel / High repath for ground raid launches. Strength must not be spent yet.</summary>
    public static class RaidPollutionPreCommit
    {
        public struct Outcome
        {
            public bool cancelled;
            public bool damageExpected;
            public bool routeAltered;
            public float expectedLoss;
        }

        /// <summary>
        /// Call after traveler exists and StartPath has run, before deducting strength.
        /// May repath once (High). On cancel, destroys the traveler (suppress wipe FX).
        /// </summary>
        public static Outcome EvaluateAndMaybeCancel(
            WorldObject_Traveler traveler,
            WorldObject attacker,
            WorldObject target,
            WorldComponent_SpreadManager manager,
            WorldDominationSettings seth)
        {
            var outcome = new Outcome();
            if (traveler == null || traveler.Destroyed || seth == null) return outcome;
            if (!seth.travelerPollutionDamageEnabled) return outcome;
            if (!TravelerPollutionDamage.TakesPollutionDamage(traveler)) return outcome;

            PlanetTile start = traveler.Tile;
            PlanetTile dest = traveler.pather?.destTile ?? PlanetTile.Invalid;
            bool routeAltered = false;
            if (seth.pollutionPathCostEnabled && start.Valid && dest.Valid)
                routeAltered = PollutionPathMath.DetectRouteAltered(start, dest, traveler.Faction);

            PollutionPathMath.Result poll = PollutionPathMath.EvaluateAfterStartPath(traveler, seth, routeAltered);
            outcome.damageExpected = poll.damageExpected;
            outcome.expectedLoss = poll.expectedLoss;
            outcome.routeAltered = poll.routeAltered;

            if (!seth.pollutionPathPreCommitCancelEnabled || !poll.wouldGut)
                return outcome;

            if (seth.pollutionPathRepathEnabled && traveler.pather != null && dest.Valid)
            {
                traveler.pather.StartPath(dest, skipLaunchTravelCache: false, pollutionWeightMultiplier: PollutionPathMath.HeavyRepathWeight);
                if (traveler.Destroyed) return outcome;

                routeAltered = true;
                poll = PollutionPathMath.EvaluateAfterStartPath(traveler, seth, routeAlteredHint: true);
                outcome.damageExpected = poll.damageExpected;
                outcome.expectedLoss = poll.expectedLoss;
                outcome.routeAltered = true;
                if (!poll.wouldGut)
                    return outcome;
            }

            outcome.cancelled = true;
            LogPollutionCancel(manager, attacker, target, traveler, poll.expectedLoss, seth);
            if (!traveler.Destroyed)
            {
                traveler.suppressDestroyedWorldFx = true;
                traveler.Destroy();
            }
            return outcome;
        }

        private static void LogPollutionCancel(
            WorldComponent_SpreadManager manager,
            WorldObject attacker,
            WorldObject target,
            WorldObject_Traveler traveler,
            float expectedLoss,
            WorldDominationSettings seth)
        {
            string msg = "TSA_WD_Log_Raid_Aborted_Pollution".Translate(
                attacker?.LabelCap ?? "?",
                target?.LabelCap ?? "?",
                expectedLoss.ToString("F0"));
            var entry = new SpreadLogEntry(msg, attacker, target);
            entry.isRaid = true;
            entry.isAttempt = true;
            entry.isAborted = true;
            entry.attStr = traveler?.initialStrength ?? traveler?.travelerStrength ?? 0f;
            entry.efficiencyFactor = 1f;
            entry.defStr = 0f;
            entry.pollutionDamageExpected = expectedLoss > 0.01f;
            entry.pollutionRouteAltered = false;
            entry.pollutionExpectedLoss = expectedLoss;
            manager?.AddLog(entry);
        }

        public static void ApplyFlagsToLog(SpreadLogEntry log, Outcome outcome)
        {
            if (log == null) return;
            log.pollutionDamageExpected = outcome.damageExpected;
            log.pollutionRouteAltered = outcome.routeAltered;
            log.pollutionExpectedLoss = outcome.expectedLoss;
        }
    }
}
