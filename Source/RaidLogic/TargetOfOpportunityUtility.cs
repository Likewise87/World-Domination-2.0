using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Feature A (target-of-opportunity retargeting) and Feature B (post-victory marauding) share a single
    /// "retarget this in-flight raid traveler onto a different static settlement/outpost" primitive, driven by
    /// <see cref="WorldComponent_SettlementWatchIndex"/> and <see cref="RaidLaunchGate.EvaluateTravelerVsCandidateRatio"/>.
    /// Both features never redirect a raid onto another traveler/caravan — candidates are always watch-index
    /// settlement/outpost entries.
    /// </summary>
    public static class TargetOfOpportunityUtility
    {
        /// <summary>Per-traveler minimum interval between full (expensive) Feature A evaluations, regardless of roll outcome.</summary>
        private const int EvalCooldownTicks = 300;

        /// <summary>Features A/B/C's shared mid/late escalation gate, bypassable via <see cref="WorldDominationSettings.opportunityFeaturesIgnoreEscalationGate"/> so a player can opt into these behaviors from the start of a game.</summary>
        private static bool PassesEscalationGate(WorldComponent_SpreadManager manager, WorldDominationSettings seth) =>
            (seth != null && seth.opportunityFeaturesIgnoreEscalationGate) || WdEscalation.IsMidOrLate(manager);

        /// <summary>Feature A entry point: called from <see cref="WD_PathFollower.PatherTick"/>'s tile-exit block for every walking Raid traveler.</summary>
        public static void TryCheckTargetOfOpportunity(WorldObject_Traveler traveler, int exitedTileId)
        {
            var seth = WorldDominationMod.settings;
            if (seth == null || !seth.experimentalTargetOfOpportunity) return;
            if (traveler == null || traveler.Destroyed || traveler.pather == null) return;
            if (traveler.isTurretDetour) return;
            if (traveler.targetObject is WorldObject_AT_Turret) return;
            if (!WorldObject_Traveler.IsRaidMission(traveler.mission)) return;
            if (!TravelerEndpointUtility.IsLiveEndpoint(traveler.targetObject)) return;
            if (traveler.Faction == null) return;

            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (!PassesEscalationGate(manager, seth)) return;

            if (traveler.totalTargetChanges >= seth.targetChangesMaxLifetime) return;
            if (traveler.targetOfOpportunityRetargets >= seth.targetOfOpportunityMaxRetargets) return;

            // Cheap coin flip first: most tile-exit-near-a-watcher events die here for free.
            if (Rand.Value > seth.targetOfOpportunityEligibilityRollPct) return;

            int now = Find.TickManager.TicksGame;
            if (now - traveler.lastOpportunityEvalTick < EvalCooldownTicks) return;

            var watchIndex = WorldComponent_SettlementWatchIndex.Get();
            if (watchIndex == null) return;
            List<WorldObject> watchers = watchIndex.GetWatchers(exitedTileId, WatchCapability.Nearby);
            if (watchers.Count == 0) return;

            WorldObject best = FindClosestEligibleCandidate(traveler, watchers, exitedTileId, watchIndex);
            if (best == null) return;

            // From here on we are doing the expensive ally-aware strength math; consume the per-traveler cooldown.
            traveler.lastOpportunityEvalTick = now;

            var lookup = WorldActions_Utils.GetWorldObjectsWithCompByFaction();
            float currentRatio = RaidLaunchGate.EvaluateTravelerVsCandidateRatio(
                traveler, traveler.targetObject, traveler.cachedTargetKind, lookup, manager, seth, out _);
            RaidLaunchTargetKind candidateKind = RaidLaunchGate.ClassifyTarget(best);
            float candidateRatio = RaidLaunchGate.EvaluateTravelerVsCandidateRatio(
                traveler, best, candidateKind, lookup, manager, seth, out _);

            if (candidateRatio < currentRatio + seth.targetOfOpportunityMinRatioAdvantage) return;

            bool originalTargetWasPlayer = traveler.cachedTargetKind == RaidLaunchTargetKind.PlayerColony
                || traveler.cachedTargetKind == RaidLaunchTargetKind.PlayerSimulated;
            WorldObject oldTarget = traveler.targetObject;

            ApplyRetarget(traveler, best, watchIndex);
            traveler.targetOfOpportunityRetargets++;

            manager?.AddLog(new SpreadLogEntry(
                "TSA_WD_Log_TargetOfOpportunity".Translate(traveler.LabelCap, oldTarget?.LabelCap ?? "?", best.LabelCap),
                traveler, best));

            bool newTargetIsPlayer = candidateKind == RaidLaunchTargetKind.PlayerColony || candidateKind == RaidLaunchTargetKind.PlayerSimulated;
            if (originalTargetWasPlayer && !newTargetIsPlayer
                && (seth.notifyRaidDivertedFromPlayer))
            {
                Find.LetterStack.ReceiveLetter(
                    "TSA_WD_Letter_RaidDivertedFromPlayer_Label".Translate(),
                    "TSA_WD_Letter_RaidDivertedFromPlayer_Text".Translate(traveler.LabelCap, best.LabelCap),
                    LetterDefOf.PositiveEvent,
                    new LookTargets(traveler));
            }
        }

        /// <summary>
        /// Feature B entry point: called right before <c>Raid_Simulated</c> would otherwise let <see cref="WorldActions_Traveler.ExecuteArrival"/>
        /// destroy the victorious traveler. Only ever called from the synchronous NPC-vs-NPC/NPC-vs-player auto-resolve win branch
        /// (never the real player-map manual-defense dialog flow). Returns true when the traveler should be kept alive and re-pathed.
        /// <paramref name="defeatedTileId"/>/<paramref name="defeatedLabel"/> are captured by the caller BEFORE the defeated target's
        /// <c>Destroy()</c> call, since a destroyed <see cref="WorldObject"/> should not be read from afterward.
        /// </summary>
        public static bool TryContinueMarauding(WorldObject_Traveler traveler, int defeatedTileId, string defeatedLabel, WorldComponent_SpreadManager manager)
        {
            var seth = WorldDominationMod.settings;
            if (seth == null || !seth.experimentalContinueAfterConquest) return false;
            if (traveler == null || traveler.Destroyed || traveler.pather == null) return false;
            if (traveler.isTurretDetour) return false;
            if (traveler.Faction == null) return false;
            if (!PassesEscalationGate(manager, seth)) return false;

            if (traveler.maraudingChainCount >= seth.maraudingMaxChainedTargets) return false;
            if (traveler.totalTargetChanges >= seth.targetChangesMaxLifetime) return false;
            if (traveler.travelerStrength < seth.maraudingMinSurvivingStrengthAbsolute) return false;
            if (Rand.Value > seth.maraudingChanceToOccurPct) return false;

            var watchIndex = WorldComponent_SettlementWatchIndex.Get();
            if (watchIndex == null) return false;
            List<WorldObject> watchers = watchIndex.GetWatchers(defeatedTileId, WatchCapability.Nearby);
            if (watchers.Count == 0) return false;

            WorldObject best = FindClosestEligibleCandidate(traveler, watchers, defeatedTileId, watchIndex);
            if (best == null) return false;

            var lookup = WorldActions_Utils.GetWorldObjectsWithCompByFaction();
            RaidLaunchTargetKind candidateKind = RaidLaunchGate.ClassifyTarget(best);
            float candidateRatio = RaidLaunchGate.EvaluateTravelerVsCandidateRatio(
                traveler, best, candidateKind, lookup, manager, seth, out _);
            if (candidateRatio < seth.minRaidRatio) return false;

            ApplyRetarget(traveler, best, watchIndex);
            traveler.maraudingChainCount++;

            manager?.AddLog(new SpreadLogEntry(
                "TSA_WD_Log_Marauding".Translate(traveler.LabelCap, defeatedLabel ?? "?", best.LabelCap),
                traveler, best));

            return true;
        }

        /// <summary>Closest (BFS-tile-exit-cheap) watcher that passes the shared hostile/live/anti-dogpile filters. Never another traveler/caravan.</summary>
        private static WorldObject FindClosestEligibleCandidate(
            WorldObject_Traveler traveler, List<WorldObject> watchers, int fromTileId, WorldComponent_SettlementWatchIndex watchIndex)
        {
            WorldObject best = null;
            float bestDist = float.MaxValue;
            WorldGrid grid = Find.WorldGrid;
            for (int i = 0; i < watchers.Count; i++)
            {
                WorldObject candidate = watchers[i];
                if (!IsEligibleCandidate(traveler, candidate, watchIndex)) continue;
                float dist = grid != null ? grid.ApproxDistanceInTiles(fromTileId, candidate.Tile.tileId) : 0f;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = candidate;
                }
            }
            return best;
        }

        private static bool IsEligibleCandidate(WorldObject_Traveler traveler, WorldObject candidate, WorldComponent_SettlementWatchIndex watchIndex)
        {
            if (!TravelerEndpointUtility.IsLiveEndpoint(candidate)) return false;
            // AT Turrets use save/restore detours via AtTurretRetaliationUtility, never permanent ToO/maraud swaps.
            if (candidate is WorldObject_AT_Turret) return false;
            if (candidate == traveler.targetObject) return false;
            if (WorldActions_Utils.IsSpace(candidate)) return false;
            if (candidate.Faction == null) return false;
            if (!WorldActions_Utils.SafeHostileTo(traveler.Faction, candidate.Faction)) return false;
            if (watchIndex != null && watchIndex.IsUnderDogpileCooldown(candidate)) return false;
            var comp = candidate.GetComponent<CompViralSpread>();
            if (comp != null && comp.defenseCooldownTick > Find.TickManager.TicksGame) return false;
            return true;
        }

        /// <summary>
        /// Shared swap: releases any existing defense-cooldown reservation on the old target, reserves one on the new
        /// target, re-runs <see cref="RaidLaunchGate.ClassifyTarget"/> and stores it, retargets the pather, stamps the
        /// anti-dogpile cooldown, and increments the combined <see cref="WorldObject_Traveler.totalTargetChanges"/> counter.
        /// </summary>
        private static void ApplyRetarget(WorldObject_Traveler traveler, WorldObject candidate, WorldComponent_SettlementWatchIndex watchIndex)
        {
            WorldObject oldTarget = traveler.targetObject;

            if (traveler.playerColonyRaidCooldownReservationTick > 0
                && oldTarget is Settlement oldPlayerSettlement
                && oldPlayerSettlement.Faction?.IsPlayer == true)
                Raid_OnPlayerColony.ReleaseRaidDefenseCooldownReservation(oldPlayerSettlement, traveler.playerColonyRaidCooldownReservationTick);
            if (traveler.targetRaidDefenseCooldownReservationTick > 0)
                Raid_DefenseCooldownReservations.ReleaseRaidDefenseCooldownReservation(oldTarget, traveler.targetRaidDefenseCooldownReservationTick);
            traveler.targetRaidDefenseCooldownReservationTick = -1;
            traveler.playerColonyRaidCooldownReservationTick = -1;

            traveler.targetObject = candidate;
            traveler.cachedTargetKind = RaidLaunchGate.ClassifyTarget(candidate);

            int reservedUntilTick = Raid_DefenseCooldownReservations.ApplyRaidDefenseCooldownReservation(candidate);
            traveler.targetRaidDefenseCooldownReservationTick = reservedUntilTick;
            if (candidate is Settlement newPlayerSettlement && newPlayerSettlement.Faction?.IsPlayer == true && newPlayerSettlement.HasMap)
                traveler.playerColonyRaidCooldownReservationTick = reservedUntilTick;

            PlanetTile candidateTile = PlanetSurfaceWorldActions.PlanetTileForWdTravel(candidate.Tile, traveler);
            if (!traveler.pather.RetargetDestinationAfterCurrentHop(candidateTile))
                traveler.pather.StartPath(candidateTile, skipLaunchTravelCache: true);

            traveler.projectedArrivalStrength = traveler.travelerStrength;
            traveler.totalTargetChanges++;

            watchIndex?.StampDogpile(candidate);
        }
    }
}
