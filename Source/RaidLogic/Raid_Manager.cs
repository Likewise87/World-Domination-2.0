using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using Verse;
using UnityEngine;

namespace TSA_WorldDomination
{
    /// <summary>Stores a potential raid target and optional precomputed defender data to avoid recalculating for logging.</summary>
    public struct RaidTargetCandidate
    {
        public WorldObject Target;
        public List<WorldObject> DefAllies;  // null for player colony (we compute on demand for logging)
        public float TotalDefPower;

        public RaidTargetCandidate(WorldObject target, List<WorldObject> defAllies, float totalDefPower)
        {
            Target = target;
            DefAllies = defAllies;
            TotalDefPower = totalDefPower;
        }
    }

    /// <summary>
    /// Raid targets after cheap filters are <see cref="pending"/> (equal-quarter distance bands of attack range R).
    /// We assess at most one candidate per tick; stop as soon as one passes the min relative strength check.
    /// Player map colonies use the storyteller strength gate. No further candidates once a suitable target is found.
    /// </summary>
    internal class PendingRaidEvaluation
    {
        internal enum CandidateKind : byte { PlayerColony, PlayerSimulated, NPC }

        internal struct CandidateEntry
        {
            public WorldObject target;
            public CandidateKind kind;
            /// <summary>Cached tile distance from attacker at candidate build (for band ordering).</summary>
            public float dist;
        }

        internal Settlement attacker;
        internal CompViralSpread attComp;
        internal WorldComponent_SpreadManager manager;
        internal WorldDominationSettings seth;
        internal Dictionary<Faction, List<WorldObject>> objectsWithComp;
        internal List<WorldObject> attAllies;
        internal float totalAvailableAttPower;
        /// <summary>Attacker raid range R used for candidate filter and band index.</summary>
        internal float raidRange;
        /// <summary>Preferred distance band from weighted pick (0–3), or -1.</summary>
        internal int preferredBand = -1;
        /// <summary>Dev force: skip min-ratio abort on finalize; may force-pick a candidate if none passed gate.</summary>
        internal bool debugForce;
        /// <summary>Dev force: always launch as drop-pod (ignore T4 / chance / tech gates).</summary>
        internal bool forceDropPod;

        internal readonly List<CandidateEntry> pending = new List<CandidateEntry>();
        internal int nextIdx;
        /// <summary>At most one entry: first suitable target in ordered <see cref="pending"/>.</summary>
        internal readonly List<RaidTargetCandidate> viable = new List<RaidTargetCandidate>();
        /// <summary>How many nearer candidates failed the strength gate before the chosen one (verbose tuning).</summary>
        internal int skippedTooWeakBeforeChoice;
        /// <summary>Colony required ratio locked at assess pick (so soften reset on pick does not fail the same raid at finalize).</summary>
        internal float lockedColonyRequiredRatio = -1f;

        internal bool IsComplete => nextIdx >= pending.Count || viable.Count > 0;

        /// <summary>Evaluate one candidate (at most one pathfind). Returns true when we should finalize (suitable target found, or list exhausted).</summary>
        internal bool EvaluateNext()
        {
            if (IsComplete) return true;
            if (attacker == null || attacker.Destroyed || !attacker.Spawned) return true;

            var entry = pending[nextIdx++];
            int step = nextIdx;
            int total = pending.Count;
            var target = entry.target;
            string tgtLabel = target?.LabelCap ?? "?";

            if (target == null || target.Destroyed || !target.Spawned)
            {
                WDVerbose.Msg($"Raid assess {step}/{total} attacker={attacker.LabelCap} target={tgtLabel} → skip (invalid)");
                return IsComplete;
            }

            if (!PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(target.Tile))
            {
                WDVerbose.Msg($"Raid assess {step}/{total} attacker={attacker.LabelCap} target={tgtLabel} → skip (non-surface layer)");
                return IsComplete;
            }

            if (entry.kind != CandidateKind.PlayerColony && target.GetComponent<CompViralSpread>() == null)
            {
                WDVerbose.Msg($"Raid assess {step}/{total} attacker={attacker.LabelCap} target={tgtLabel} → skip (no CompViralSpread)");
                return IsComplete;
            }

            var gate = RaidLaunchGate.Evaluate(attacker, target, (RaidLaunchTargetKind)entry.kind, attAllies, objectsWithComp, manager, seth);
            if (entry.kind == CandidateKind.PlayerColony)
            {
                if (gate.passed)
                {
                    float quietDays = RaidLaunchGate.GetColonyQuietDays(target.GetComponent<CompViralSpread>());
                    lockedColonyRequiredRatio = gate.requiredRatio;
                    viable.Add(new RaidTargetCandidate(target, null, gate.defTotal));
                    // Quiet = days since last target pick; stamp now so soften resets for later assessors.
                    target.GetComponent<CompViralSpread>()?.MarkPlayerColonyWdRaidPicked();
                    LogChoiceWithNearSkips(step, total, tgtLabel, entry.dist,
                        $"[player map colony] → chosen (ratio={gate.ratio:F2} ≥ req={gate.requiredRatio:F2}; quietDays={quietDays:F1}; storytellerDef={gate.defTotal:F0}; effAtt={gate.effectiveAtt:F0}; eval stops)");
                    return true;
                }

                skippedTooWeakBeforeChoice++;
                float quietFail = RaidLaunchGate.GetColonyQuietDays(target.GetComponent<CompViralSpread>());
                WDVerbose.Msg($"Raid assess {step}/{total} attacker={attacker.LabelCap} target={tgtLabel} dist={entry.dist:F1} [player map colony] → too weak (ratio={gate.ratio:F2} < req={gate.requiredRatio:F2}; quietDays={quietFail:F1}; storytellerDef={gate.defTotal:F0}; effAtt={gate.effectiveAtt:F0}); try next");
                return IsComplete;
            }

            if (gate.passed)
            {
                var defSnap = gate.defenders;
                var defAllies = new List<WorldObject>(defSnap?.allies.Count ?? 0);
                if (defSnap != null)
                {
                    for (int i = 0; i < defSnap.allies.Count; i++)
                        defAllies.Add(defSnap.allies[i].obj);
                }
                viable.Add(new RaidTargetCandidate(target, defAllies, defSnap?.Total ?? gate.defTotal));
                LogChoiceWithNearSkips(step, total, tgtLabel, entry.dist,
                    $"[{entry.kind}] → chosen (ratio={gate.ratio:F2} ≥ min={seth.minRaidRatio:F2}; effAtt={gate.effectiveAtt:F0} def={gate.defTotal:F0}; eval stops)");
                return true;
            }

            skippedTooWeakBeforeChoice++;
            WDVerbose.Msg($"Raid assess {step}/{total} attacker={attacker.LabelCap} target={tgtLabel} dist={entry.dist:F1} [{entry.kind}] → too weak (ratio={gate.ratio:F2} < min={seth.minRaidRatio:F2}; effAtt={gate.effectiveAtt:F0} def={gate.defTotal:F0}); try next");
            return IsComplete;
        }

        private void LogChoiceWithNearSkips(int step, int total, string tgtLabel, float chosenDist, string detail)
        {
            if (skippedTooWeakBeforeChoice > 0)
            {
                WDVerbose.Msg(
                    $"Raid assess {step}/{total} attacker={attacker.LabelCap} target={tgtLabel} dist={chosenDist:F1} {detail} " +
                    $"(after {skippedTooWeakBeforeChoice} nearer too-weak skip(s); preferred band then walked outward)");
            }
            else
            {
                WDVerbose.Msg($"Raid assess {step}/{total} attacker={attacker.LabelCap} target={tgtLabel} dist={chosenDist:F1} {detail}");
            }
        }
    }

    public static class WorldActions_Raid
    {
        /// <summary>
        /// Begins a staggered raid evaluation. Cheap pre-filtering is done immediately;
        /// pathfinding is deferred to subsequent ticks via <see cref="PendingRaidEvaluation"/>.
        /// Returns true to claim the action slot (prevents fallback to growth).
        /// </summary>
        public static bool AttemptRaid(Settlement attacker, CompViralSpread attComp, WorldComponent_SpreadManager manager)
            => AttemptRaid(attacker, attComp, manager, debugForce: false);

        /// <param name="debugForce">When true, clears any pending raid slot and skips strength / raid-power gates.</param>
        public static bool AttemptRaid(Settlement attacker, CompViralSpread attComp, WorldComponent_SpreadManager manager, bool debugForce)
        {
            var seth = WorldDominationMod.settings;
            if (seth == null || attacker.Faction == null) return false;

            if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(attacker))
            {
                manager.AddLog(new SpreadLogEntry("TSA_WD_Log_Raid_AbortedSpace".Translate(), attacker));
                return false;
            }

            if (manager.pendingRaid != null)
            {
                if (!debugForce) return false;
                manager.pendingRaid = null;
            }

            float currentRaidRange = SettlementAttackRangeUtil.GetNpcSettlementAttackRangeWithZeal(attacker, seth, manager);

            var objectsWithComp = WorldActions_Utils.GetWorldObjectsWithCompByFaction();

            // Snapshot off GetReinforcements scratch — EvaluateNext / other raids must not wipe this list.
            var attAllies = new List<WorldObject>(
                Raid_ReinforcementLogic.GetReinforcements(attacker, null, AllyRadiusUtil.GetEffective(attacker, seth, manager), objectsWithComp, manager));

            float totalAvailableAttPower = WorldActions_Utils.GetAvailableRaidStrength(attComp, seth);
            foreach (var ally in attAllies)
                totalAvailableAttPower += WorldActions_Utils.GetAvailableRaidStrength(ally.GetComponent<CompViralSpread>(), seth);

            if (!debugForce && totalAvailableAttPower < 50f)
            {
                manager.AddLog(new SpreadLogEntry("TSA_WD_Log_Raid_SkippedLowRaidPower".Translate(attacker.LabelCap, totalAvailableAttPower.ToString("F0")), attacker));
                return false;
            }

            // --- Build candidate queue using CHEAP checks only (no pathfinding) ---
            var eval = new PendingRaidEvaluation
            {
                attacker = attacker,
                attComp = attComp,
                manager = manager,
                seth = seth,
                objectsWithComp = objectsWithComp,
                attAllies = attAllies,
                totalAvailableAttPower = totalAvailableAttPower,
                raidRange = currentRaidRange,
                debugForce = debugForce
            };

            if (seth.allowPlayerRaid && WorldActions_Utils.SafeHostileTo(attacker.Faction, Faction.OfPlayer)
                && (debugForce || manager.CanAcceptPlayerWdRaid(seth)))
            {
                foreach (var po in WorldActions_Utils.GetFactionObjects(objectsWithComp, Faction.OfPlayer))
                {
                    var pComp = po.GetComponent<CompViralSpread>();
                    if (pComp == null || pComp.IsDefenseOnCooldown || pComp.IsIncidentOnCooldown) continue;
                    if (!PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(po.Tile)) continue;

                    bool isSimulatedTarget = po is WorldObject_WD_Outpost;
                    if (isSimulatedTarget && !seth.allowPlayerOutpostRaid) continue;

                    // Player targets: settlement attack range only (no influence bubble).
                    float dist = WorldActions_Utils.GetDistance(attacker.Tile, po.Tile, manager);
                    if (dist > currentRaidRange) continue;

                    if (po is Settlement s && s.HasMap)
                        eval.pending.Add(new PendingRaidEvaluation.CandidateEntry { target = po, kind = PendingRaidEvaluation.CandidateKind.PlayerColony, dist = dist });
                    else if (isSimulatedTarget)
                        eval.pending.Add(new PendingRaidEvaluation.CandidateEntry { target = po, kind = PendingRaidEvaluation.CandidateKind.PlayerSimulated, dist = dist });
                }
            }

            foreach (var f in Find.FactionManager.AllFactionsListForReading)
            {
                if (f == null || f.defeated || f.def.hidden || f == attacker.Faction || !WorldActions_Utils.SafeHostileTo(f, attacker.Faction) || f.IsPlayer) continue;

                foreach (var obj in WorldActions_Utils.GetFactionObjects(objectsWithComp, f))
                {
                    if (!PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(obj.Tile)) continue;
                    if (obj is Settlement s && WorldActions_Utils.IsSettlementProtected(s)) continue;

                    var targetComp = obj.GetComponent<CompViralSpread>();
                    if (targetComp == null || targetComp.IsDefenseOnCooldown) continue;

                    float dist = WorldActions_Utils.GetDistance(attacker.Tile, obj.Tile, manager);
                    if (dist > currentRaidRange) continue;

                    eval.pending.Add(new PendingRaidEvaluation.CandidateEntry { target = obj, kind = PendingRaidEvaluation.CandidateKind.NPC, dist = dist });
                }
            }

            if (eval.pending.Count == 0)
            {
                WDVerbose.Msg($"AttemptRaid: no candidates in range for {attacker.LabelCap}");
                manager.AddLog(new SpreadLogEntry("TSA_WD_Log_Raid_SkippedNoTarget".Translate(), attacker, null));
                return false;
            }

            OrderRaidCandidates(eval);
            WDVerbose.Msg($"AttemptRaid: candidates={eval.pending.Count} attacker={attacker.LabelCap} travelPrepExactPct={seth.travelPrepExactPercent} (distance bands of R={currentRaidRange:F1}; one strength assess/tick, stop at first suitable)");
            manager.pendingRaid = eval;
            return true;
        }

        /// <summary>Called when staggered evaluation completes. Launches against the single chosen target (first suitable in ordered list), if any.</summary>
        internal static void FinalizeRaid(PendingRaidEvaluation eval)
        {
            var manager = eval.manager;
            var attacker = eval.attacker;
            var attComp = eval.attComp;
            var seth = eval.seth;

            if (attacker == null || attacker.Destroyed || !attacker.Spawned || attComp == null)
                return;

            if (eval.viable.Count == 0)
            {
                string bandNote = WD_TargetDistanceBandOrder.FormatBandPickMessage(eval.preferredBand, -1);
                WDVerbose.Msg($"Raid finalize: attacker={attacker.LabelCap} no suitable target (exhausted band-ordered list of {eval.pending.Count} or none passed min strength) {bandNote}");
                string skipMsg = "TSA_WD_Log_Raid_SkippedNoTarget".Translate().ToString();
                if (!bandNote.NullOrEmpty())
                    skipMsg = skipMsg + " " + bandNote;
                manager.AddLog(new SpreadLogEntry(skipMsg, attacker, null));
                AttemptGrowFallback(attacker, attComp, manager);
                return;
            }

            RaidTargetCandidate chosen = eval.viable[0];
            WorldObject target = chosen.Target;
            if (target == null || target.Destroyed || !target.Spawned)
            {
                WDVerbose.Msg($"Raid finalize: attacker={attacker.LabelCap} chosen entry invalid");
                manager.AddLog(new SpreadLogEntry("TSA_WD_Log_Raid_SkippedNoTarget".Translate(), attacker, null));
                AttemptGrowFallback(attacker, attComp, manager);
                return;
            }

            float chosenDist = WorldActions_Utils.GetDistance(attacker.Tile, target.Tile, manager);
            int chosenBand = WD_TargetDistanceBandOrder.BandIndex(chosenDist, eval.raidRange);
            string bandPickMsg = WD_TargetDistanceBandOrder.FormatBandPickMessage(eval.preferredBand, chosenBand);
            WDVerbose.Msg($"Raid finalize: attacker={attacker.LabelCap} chosen={target.LabelCap} dist={chosenDist:F1} {bandPickMsg} (nearerTooWeakSkips={eval.skippedTooWeakBeforeChoice})");

            RaidLaunchTargetKind targetKind = RaidLaunchGate.ClassifyTarget(target);
            bool skipMinRatioAbort = false;
            float colonyReqOverride = (targetKind == RaidLaunchTargetKind.PlayerColony && eval.lockedColonyRequiredRatio >= 0f)
                ? eval.lockedColonyRequiredRatio
                : -1f;

            // Colony: faction intent from scheduler, launch from nearest eligible same-faction executor.
            bool colonyRaidHandedOff = false;
            if (targetKind == RaidLaunchTargetKind.PlayerColony)
            {
                Settlement executor = RaidColonyExecutor.SelectExecutor(
                    attacker,
                    target,
                    eval.objectsWithComp,
                    manager,
                    seth,
                    out List<WorldObject> executorAllies,
                    requiredRatioOverride: colonyReqOverride);
                if (executor != null && executor != attacker && executor.GetComponent<CompViralSpread>() != null)
                {
                    Settlement scheduler = attacker;
                    string delegateMsg = "TSA_WD_Log_Raid_ColonyDelegated".Translate();
                    Log.Message(
                        $"[WD] Actor A={scheduler.LabelCap}, Actor B={executor.LabelCap}. {delegateMsg}");
                    manager?.AddLog(new SpreadLogEntry(delegateMsg, scheduler, executor));
                    WDVerbose.Msg(
                        $"Raid finalize: colony executor handoff {scheduler.LabelCap} → {executor.LabelCap} " +
                        $"(faction intent, local launch)");
                    RaidColonyExecutor.ApplyExecutor(eval, executor, executorAllies);
                    attacker = eval.attacker;
                    attComp = eval.attComp;
                    colonyRaidHandedOff = true;
                }
            }

            if (attacker == null || attacker.Destroyed || !attacker.Spawned || attComp == null)
                return;

            bool targetingPlayer = targetKind == RaidLaunchTargetKind.PlayerColony || targetKind == RaidLaunchTargetKind.PlayerSimulated;
            if (targetingPlayer && !eval.debugForce && manager != null && attacker?.Faction != null
                && manager.IsPlayerBribeCeasefireActive(attacker.Faction))
            {
                WDVerbose.Msg($"Raid finalize: attacker={attacker.LabelCap} blocked by player bribe ceasefire");
                manager.AddLog(new SpreadLogEntry(
                    "TSA_WD_Log_Raid_SkippedBribeCeasefire".Translate(attacker.Faction.Name),
                    attacker,
                    target));
                AttemptGrowFallback(attacker, attComp, manager);
                return;
            }
            if (targetingPlayer && !eval.debugForce && manager != null && !manager.CanAcceptPlayerWdRaid(seth))
            {
                WDVerbose.Msg($"Raid finalize: attacker={attacker.LabelCap} blocked by global player WD raid rate caps");
                manager.AddLog(new SpreadLogEntry("TSA_WD_Log_Raid_SkippedNoTarget".Translate(), attacker, null));
                AttemptGrowFallback(attacker, attComp, manager);
                return;
            }

            List<WorldObject> fullAtkList = new List<WorldObject> { attacker };
            fullAtkList.AddRange(eval.attAllies);

            Dictionary<WorldObject, float> contributions = new Dictionary<WorldObject, float>();
            float totalInvestedPower = 0f;
            foreach (WorldObject wo in fullAtkList)
            {
                float available = WorldActions_Utils.GetAvailableRaidStrength(wo.GetComponent<CompViralSpread>(), seth);
                contributions[wo] = available;
                totalInvestedPower += available;
            }

            bool useDropPod = ShouldLaunchDropPodRaid(attComp, attacker, seth, eval.forceDropPod);
            float dropEfficiency = 1f;
            float dropSynthTicks = -1f;
            if (useDropPod)
            {
                if (!TravelUtils.TryDropPodRaidEfficiency(
                        attacker.Tile, target.Tile, seth, attacker.Faction,
                        out dropEfficiency, out dropSynthTicks))
                {
                    if (eval.forceDropPod)
                    {
                        dropEfficiency = 1f;
                        float dist = Mathf.Max(1f, Find.WorldGrid.ApproxDistanceInTiles(attacker.Tile, target.Tile));
                        dropSynthTicks = dist * WorldObject_Traveler.DefaultTicksPerMove;
                    }
                    else
                        useDropPod = false;
                }
            }

            WorldObject_Traveler traveler;
            float pathTicks;
            float finalEfficiency;
            RaidLaunchGate.GateResult finalGate;
            RaidPollutionPreCommit.Outcome pollutionOutcome = default;

            if (useDropPod)
            {
                finalGate = RaidLaunchGate.Evaluate(
                    attacker, target, targetKind, eval.attAllies, eval.objectsWithComp, manager, seth,
                    pathTravelTicks: -1f, efficiencyOverride: dropEfficiency, requiredRatioOverride: colonyReqOverride);
                if (!finalGate.passed && !skipMinRatioAbort && !eval.debugForce)
                {
                    LogRaidAbortedBelowMinRatio(manager, attacker, target, finalGate, seth);
                    AttemptGrowFallback(attacker, attComp, manager);
                    return;
                }

                traveler = (WorldObject_Traveler)WorldObjectMaker.MakeWorldObject(
                    DefDatabase<WorldObjectDef>.GetNamed("TSA_WD_Traveler_RaidDropPod"));
                traveler.Tile = attacker.Tile;
                traveler.SetFaction(attacker.Faction);
                traveler.mission = TravelerMission.RaidDropPod;
                traveler.ticksPerMove = WorldActions_Traveler.GetDropPodTicksPerMove();
                traveler.originObject = attacker;
                traveler.targetObject = target;
                float arrivalStrength = totalInvestedPower * dropEfficiency;
                traveler.travelerStrength = arrivalStrength;
                traveler.initialStrength = arrivalStrength;
                traveler.projectedArrivalStrength = arrivalStrength;
                if (traveler.contributionFactors == null) traveler.contributionFactors = new Dictionary<WorldObject, float>();

                Find.WorldObjects.Add(traveler);
                traveler.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(target.Tile, attacker));
                AntiAirFireUtils.WakeAllForDropPod(traveler);
                pathTicks = dropSynthTicks;
                finalEfficiency = dropEfficiency;
            }
            else
            {
                var preGate = RaidLaunchGate.Evaluate(attacker, target, targetKind, eval.attAllies, eval.objectsWithComp, manager, seth,
                    requiredRatioOverride: colonyReqOverride);
                if (!preGate.passed && !skipMinRatioAbort && !eval.debugForce)
                {
                    LogRaidAbortedBelowMinRatio(manager, attacker, target, preGate, seth);
                    AttemptGrowFallback(attacker, attComp, manager);
                    return;
                }

                traveler = (WorldObject_Traveler)WorldObjectMaker.MakeWorldObject(
                    DefDatabase<WorldObjectDef>.GetNamed("TSA_WD_Traveler_Raid"));
                traveler.Tile = attacker.Tile;
                traveler.SetFaction(attacker.Faction);
                traveler.mission = TravelerMission.Raid;
                traveler.originObject = attacker;
                traveler.targetObject = target;
                traveler.travelerStrength = totalInvestedPower;
                traveler.initialStrength = totalInvestedPower;
                if (traveler.contributionFactors == null) traveler.contributionFactors = new Dictionary<WorldObject, float>();

                Find.WorldObjects.Add(traveler);
                traveler.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(target.Tile, attacker));

                var pollutionCheck = RaidPollutionPreCommit.EvaluateAndMaybeCancel(traveler, attacker, target, manager, seth);
                if (pollutionCheck.cancelled)
                {
                    AttemptGrowFallback(attacker, attComp, manager);
                    return;
                }
                pollutionOutcome = pollutionCheck;

                pathTicks = traveler.CachedLaunchTotalTravelTicks;
                finalGate = RaidLaunchGate.Evaluate(attacker, target, targetKind, eval.attAllies, eval.objectsWithComp, manager, seth, pathTicks,
                    requiredRatioOverride: colonyReqOverride);
                if (!finalGate.passed && !skipMinRatioAbort && !eval.debugForce)
                {
                    if (!traveler.Destroyed)
                    {
                        traveler.suppressDestroyedWorldFx = true;
                        traveler.Destroy();
                    }
                    LogRaidAbortedBelowMinRatio(manager, attacker, target, finalGate, seth);
                    AttemptGrowFallback(attacker, attComp, manager);
                    return;
                }

                finalEfficiency = finalGate.efficiency > 0f ? finalGate.efficiency : preGate.efficiency;
                if (pathTicks >= 0f && TravelUtils.TryEfficiencyFromPathTravelTicks(pathTicks, seth, attacker.Faction, out float effFromPath))
                    finalEfficiency = effFromPath;
                else
                {
                    var est = TravelUtils.GetTravelStrengthEstimate(attacker.Tile, target.Tile, seth, attacker.Faction, WorldObject_Traveler.DefaultTicksPerMove);
                    pathTicks = est.Found ? est.TravelTicks : -1f;
                    if (est.Found) finalEfficiency = est.Efficiency;
                }
            }

            attComp.raidCooldownTick = Find.TickManager.TicksGame + Mathf.RoundToInt(seth.cooldownRaidDays * 60000f);

            List<RaidForceRow> attForceRowsLive = RaidForceRow.FromAttackerContributions(fullAtkList, contributions, seth);

            foreach (var kvp in contributions)
            {
                if (totalInvestedPower > 0)
                    traveler.contributionFactors[kvp.Key] = kvp.Value / totalInvestedPower;

                var comp = kvp.Key.GetComponent<CompViralSpread>();
                if (comp != null && kvp.Value > 0)
                {
                    comp.strength -= kvp.Value;
                    comp.CheckTierUpdate(false);
                }
            }

            if (!traveler.Destroyed)
            {
                int reservedUntilTick = Raid_DefenseCooldownReservations.ApplyRaidDefenseCooldownReservation(target);
                traveler.targetRaidDefenseCooldownReservationTick = reservedUntilTick;
                if (target is Settlement playerRaidTarget && playerRaidTarget.Faction?.IsPlayer == true && playerRaidTarget.HasMap)
                    traveler.playerColonyRaidCooldownReservationTick = reservedUntilTick;
                if (targetingPlayer)
                    manager?.RecordPlayerWdRaidLaunch();
                if (targetKind == RaidLaunchTargetKind.PlayerColony)
                    target.GetComponent<CompViralSpread>()?.MarkPlayerColonyWdRaidPicked();
            }

            SpreadLogEntry launchLog;
            if (colonyRaidHandedOff && attacker.Faction != null)
            {
                launchLog = new SpreadLogEntry(
                    "TSA_WD_Log_Raid_FactionDispatch".Translate(
                        attacker.Faction.Name,
                        attacker.Label,
                        target.Label),
                    attacker,
                    target);
            }
            else
            {
                launchLog = new SpreadLogEntry(
                    "TSA_WD_Log_Raid_ExpeditionLaunched".Translate(attacker.Label, target.Label),
                    attacker,
                    target);
            }
            if (!bandPickMsg.NullOrEmpty())
                launchLog.message = launchLog.message + " " + bandPickMsg;
            launchLog.isRaid = true;
            launchLog.isAttempt = true;
            launchLog.attStr = totalInvestedPower;

            // Same shared snapshot used by assess, preview, and arrival so the logged "before" matches what actually fights.
            var launchDefSnap = Raid_MathSnapshot.BuildDefenders(target, attacker, attacker.Faction, eval.objectsWithComp, manager, seth);
            launchLog.defStr = launchDefSnap.Total;
            launchLog.defDetails = launchDefSnap.BuildDetails(seth);
            List<RaidForceRow> defForceRowsLive = launchDefSnap.BuildForceRows(seth);
            launchLog.defForceRows = RaidForceLogRow.FromLiveRows(defForceRowsLive);
            launchLog.targetDistance = WorldActions_Utils.GetDistance(attacker.Tile, target.Tile, manager);

            launchLog.pathTravelTicks = pathTicks;
            launchLog.efficiencyFactor = finalEfficiency;
            RaidPollutionPreCommit.ApplyFlagsToLog(launchLog, pollutionOutcome);

            float forecastRatio = finalGate.ratio > 0f ? finalGate.ratio
                : (totalInvestedPower * finalEfficiency) / (launchLog.defStr > 0 ? launchLog.defStr : 1f);
            launchLog.ratio = forecastRatio;

            WDVerbose.Msg($"RaidLaunch {attacker.LabelCap}->{target.LabelCap}: drop={useDropPod} committed={totalInvestedPower:F0} def={launchDefSnap.Total:F0} eff={finalEfficiency:F2} ratio={forecastRatio:F2} req={finalGate.requiredRatio:F2} min={seth.minRaidRatio:F2} pass={finalGate.passed || finalGate.bypassedMinRatio}");
            float forecastedWinChance = RaidCasualtyModel.GetForecast(forecastRatio, seth).winChance;
            launchLog.winChance = forecastedWinChance;

            float attStrengthAtArrival = totalInvestedPower * finalEfficiency;
            // Drop-pod colony raids get a dedicated letter; avoid also sending the generic colony letter.
            bool dropPodColonyLetter = useDropPod
                && target is Settlement dropSett
                && dropSett.HasMap
                && dropSett.Faction?.IsPlayer == true;
            if (dropPodColonyLetter)
                NotifyIncomingDropPodRaidIfEnabled(target, attacker, traveler, seth, attStrengthAtArrival, colonyRaidHandedOff);
            else
                NotifyIncomingRaidIfEnabled(target, attacker, traveler, seth, attStrengthAtArrival, launchDefSnap.Total, forecastedWinChance, colonyRaidHandedOff);

            launchLog.attForceRows = RaidForceLogRow.FromLiveRows(attForceRowsLive);

            for (int i = 0; i < fullAtkList.Count; i++)
            {
                WorldObject wo = fullAtkList[i];
                float contributed = contributions[wo];
                var comp = wo.GetComponent<CompViralSpread>();
                float currentBefore = (comp != null ? comp.strength + contributed : contributed);
                bool isPrimary = (i == 0);
                bool garrison = Raid_ReinforcementLogic.HitMinGarrisonCap(currentBefore, contributed, seth);
                float retainFloor = WorldActions_Utils.GetGarrisonRetainFloor(comp, seth);
                string display = wo.LabelCap + " (" + (isPrimary ? "TSA_WD_Primary".Translate() : "TSA_WD_Ally".Translate()) + "): " + "TSA_WD_ContribStrength".Translate(contributed.ToString("F0"));
                string tip = Raid_ReinforcementLogic.BuildContribTooltip(contributed, currentBefore, garrison, retainFloor);
                launchLog.attDetails.Add(display + Raid_ReinforcementLogic.DetailTooltipDelimiter + tip);
                launchLog.contributionDNAKeys.Add(wo?.LabelCap ?? "Unknown");
                launchLog.contributionDNAValues.Add(traveler.contributionFactors[wo]);
            }

            traveler.raidAttackerList = fullAtkList;
            traveler.raidAttackerDetails = new List<string>(launchLog.attDetails);
            traveler.raidAttackerForceRows = RaidForceLogRow.CloneList(launchLog.attForceRows);
            traveler.raidDefenderForceRows = RaidForceLogRow.CloneList(launchLog.defForceRows);

            manager.AddLog(launchLog);
        }

        private static bool ShouldLaunchDropPodRaid(CompViralSpread attComp, Settlement attacker, WorldDominationSettings seth, bool forceDropPod = false)
        {
            if (forceDropPod) return true;
            if (attComp == null || attacker?.Faction?.def == null || seth == null) return false;
            if (attComp.tier != SettlementTier.T3 && attComp.tier != SettlementTier.T4) return false;
            if (attacker.Faction.def.techLevel < seth.dropPodRaidMinTechLevel) return false;
            float chance = attComp.tier == SettlementTier.T3
                ? seth.dropPodRaidChanceT3
                : seth.dropPodRaidChance;
            return Rand.Chance(Mathf.Clamp01(chance));
        }

        /// <summary>
        /// Dev: run <see cref="AttemptRaid"/> and drain staggered assessment immediately, then <see cref="FinalizeRaid"/>.
        /// When <paramref name="forceDropPod"/> is true, launches as drop-pod regardless of attacker tier / chance / tech.
        /// </summary>
        public static bool DebugForceImmediateRaid(Settlement attacker, bool forceDropPod, out string failReason)
        {
            failReason = null;
            if (attacker == null || attacker.Destroyed || !attacker.Spawned)
            {
                failReason = "invalid settlement";
                return false;
            }
            if (attacker.Faction == null || attacker.Faction.IsPlayer)
            {
                failReason = "click an NPC settlement";
                return false;
            }

            var attComp = attacker.GetComponent<CompViralSpread>();
            if (attComp == null)
            {
                failReason = "no CompViralSpread";
                return false;
            }

            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (manager == null)
            {
                failReason = "no SpreadManager";
                return false;
            }

            if (!AttemptRaid(attacker, attComp, manager, debugForce: true))
            {
                failReason = "no hostile targets in this settlement's raid range (or space layer)";
                return false;
            }

            var eval = manager.pendingRaid;
            if (eval == null)
            {
                failReason = "pendingRaid missing after AttemptRaid";
                return false;
            }

            eval.debugForce = true;
            eval.forceDropPod = forceDropPod;

            int guard = 0;
            while (!eval.IsComplete && guard++ < 10000)
                eval.EvaluateNext();

            if (eval.viable.Count == 0)
                DebugForcePickFirstPending(eval);

            if (eval.viable.Count == 0)
            {
                manager.pendingRaid = null;
                failReason = "no raid candidates in range after assess";
                return false;
            }

            float committed = WorldActions_Utils.GetAvailableRaidStrength(attComp, WorldDominationMod.settings);
            if (eval.attAllies != null)
            {
                for (int i = 0; i < eval.attAllies.Count; i++)
                    committed += WorldActions_Utils.GetAvailableRaidStrength(
                        eval.attAllies[i]?.GetComponent<CompViralSpread>(), WorldDominationMod.settings);
            }
            if (committed <= 0f)
            {
                manager.pendingRaid = null;
                failReason = "no available raid strength (attacker + allies)";
                return false;
            }

            FinalizeRaid(eval);
            manager.pendingRaid = null;
            return true;
        }

        /// <summary>
        /// Dev: force the clicked settlement to target a player map colony, then run normal finalize
        /// (experimental executor handoff applies when the toggle is on).
        /// </summary>
        public static bool DebugForceColonyRaidForExecutorHandoff(Settlement scheduler, out string failReason, out bool handedOff)
        {
            failReason = null;
            handedOff = false;
            if (scheduler == null || scheduler.Destroyed || !scheduler.Spawned)
            {
                failReason = "invalid settlement";
                return false;
            }
            if (scheduler.Faction == null || scheduler.Faction.IsPlayer)
            {
                failReason = "click an NPC settlement";
                return false;
            }
            if (!WorldActions_Utils.SafeHostileTo(scheduler.Faction, Faction.OfPlayer))
            {
                failReason = "settlement faction is not hostile to the player";
                return false;
            }

            var seth = WorldDominationMod.settings;
            if (seth == null)
            {
                failReason = "no settings";
                return false;
            }
            if (!seth.allowPlayerRaid)
            {
                failReason = "allow player raids is off in settings";
                return false;
            }

            var attComp = scheduler.GetComponent<CompViralSpread>();
            if (attComp == null)
            {
                failReason = "no CompViralSpread";
                return false;
            }

            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (manager == null)
            {
                failReason = "no SpreadManager";
                return false;
            }

            Settlement colony = FindPlayerMapColony();
            if (colony == null)
            {
                failReason = "no player map colony found";
                return false;
            }

            var lookup = WorldActions_Utils.GetWorldObjectsWithCompByFaction();
            var attAllies = new List<WorldObject>(
                Raid_ReinforcementLogic.GetReinforcements(scheduler, null, AllyRadiusUtil.GetEffective(scheduler, seth, manager), lookup, manager));

            float lockedReq = RaidLaunchGate.GetColonyRequiredRaidRatio(colony.GetComponent<CompViralSpread>(), seth);
            float defTotal = RaidLaunchGate.GetColonyStorytellerDefense(colony);

            Settlement predictedExecutor = RaidColonyExecutor.SelectExecutor(
                scheduler, colony, lookup, manager, seth, out _, requiredRatioOverride: lockedReq);
            handedOff = predictedExecutor != null && predictedExecutor != scheduler;

            var eval = new PendingRaidEvaluation
            {
                attacker = scheduler,
                attComp = attComp,
                manager = manager,
                seth = seth,
                objectsWithComp = lookup,
                attAllies = attAllies,
                totalAvailableAttPower = RaidLaunchGate.SumAvailableAttPower(scheduler, attAllies, seth),
                debugForce = true,
                lockedColonyRequiredRatio = lockedReq,
            };
            eval.pending.Add(new PendingRaidEvaluation.CandidateEntry
            {
                target = colony,
                kind = PendingRaidEvaluation.CandidateKind.PlayerColony,
                dist = WorldActions_Utils.GetDistance(scheduler.Tile, colony.Tile, manager),
            });
            eval.viable.Add(new RaidTargetCandidate(colony, null, defTotal));

            manager.pendingRaid = null;
            FinalizeRaid(eval);
            return true;
        }

        private static Settlement FindPlayerMapColony()
        {
            var list = Find.WorldObjects?.Settlements;
            if (list == null) return null;
            for (int i = 0; i < list.Count; i++)
            {
                Settlement s = list[i];
                if (s == null || s.Destroyed || !s.Spawned) continue;
                if (s.Faction?.IsPlayer != true || !s.HasMap) continue;
                if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(s)) continue;
                return s;
            }
            return null;
        }

        private static void DebugForcePickFirstPending(PendingRaidEvaluation eval)
        {
            if (eval == null || eval.pending.Count == 0) return;
            for (int i = 0; i < eval.pending.Count; i++)
            {
                WorldObject target = eval.pending[i].target;
                if (target == null || target.Destroyed || !target.Spawned) continue;
                var defSnap = Raid_MathSnapshot.BuildDefenders(
                    target, eval.attacker, eval.attacker.Faction, eval.objectsWithComp, eval.manager, eval.seth);
                var defAllies = new List<WorldObject>(defSnap.allies.Count);
                foreach (var a in defSnap.allies) defAllies.Add(a.obj);
                eval.viable.Add(new RaidTargetCandidate(target, defAllies, defSnap.Total));
                WDVerbose.Msg($"Raid debug force-pick: attacker={eval.attacker?.LabelCap} target={target.LabelCap}");
                return;
            }
        }

        private static void NotifyIncomingDropPodRaidIfEnabled(
            WorldObject target,
            WorldObject attacker,
            WorldObject_Traveler traveler,
            WorldDominationSettings seth,
            float attStrengthAtArrival,
            bool colonyFactionDispatch = false)
        {
            if (seth == null || !seth.notifyIncomingRaidColony) return;
            if (!(target is Settlement settlement) || !settlement.HasMap || settlement.Faction?.IsPlayer != true)
                return;

            float expectedRaidPoints = RaidPointsHelper.ClampRaidPointsToStorytellerBand(
                attStrengthAtArrival,
                ResolveRaidPointsMapForTarget(target));
            string eta = FormatRaidEtaDays(traveler);
            string label;
            string text;
            if (colonyFactionDispatch && attacker?.Faction != null)
            {
                label = "TSA_WD_Letter_IncomingDropRaid_Dispatch_Label".Translate(attacker.Faction.Name);
                text = "TSA_WD_Letter_IncomingDropRaid_Dispatch_Text".Translate(
                    attacker.Faction.Name,
                    attacker.Label ?? "?",
                    target.LabelCap,
                    expectedRaidPoints.ToString("F0"),
                    eta);
            }
            else
            {
                label = "TSA_WD_Letter_IncomingDropRaid_Label".Translate();
                text = "TSA_WD_Letter_IncomingDropRaid_Text".Translate(
                    attacker?.Label ?? "?",
                    target.LabelCap,
                    expectedRaidPoints.ToString("F0"),
                    eta);
            }
            ReceiveIncomingRaidLetter(label, text, attacker, target, traveler);
        }

        private static void AttemptGrowFallback(Settlement attacker, CompViralSpread attComp, WorldComponent_SpreadManager manager)
        {
            // Soft fail: no cascading Develop/Grow when a raid finds no target.
        }

        private static void LogRaidAbortedBelowMinRatio(WorldComponent_SpreadManager manager, Settlement attacker, WorldObject target, RaidLaunchGate.GateResult gate, WorldDominationSettings seth)
        {
            float required = gate.requiredRatio > 0f ? gate.requiredRatio : seth.minRaidRatio;
            var entry = new SpreadLogEntry(
                "TSA_WD_Log_Raid_Aborted_BelowMinRatio".Translate(gate.ratio.ToString("F2"), required.ToString("F2")),
                attacker, target);
            entry.isRaid = true;
            entry.isAborted = true;
            entry.attStr = gate.rawAttPower;
            entry.defStr = gate.defTotal;
            entry.ratio = gate.ratio;
            entry.efficiencyFactor = gate.efficiency;
            manager.AddLog(entry);
            WDVerbose.Msg($"RaidLaunch ABORT {attacker?.LabelCap}->{target?.LabelCap}: ratio={gate.ratio:F2} < min={seth.minRaidRatio:F2}");
        }

        private static Map ResolveRaidPointsMapForTarget(WorldObject target)
        {
            if (target is Settlement settlement && settlement.HasMap)
                return settlement.Map;
            foreach (Map map in Find.Maps)
            {
                if (map != null && map.IsPlayerHome)
                    return map;
            }
            return null;
        }

        private static void NotifyIncomingRaidIfEnabled(
            WorldObject target,
            WorldObject attacker,
            WorldObject_Traveler traveler,
            WorldDominationSettings seth,
            float attStrengthAtArrival,
            float defStrength,
            float attackerWinChance,
            bool colonyFactionDispatch = false)
        {
            if (target.Faction == null || !target.Faction.IsPlayer)
                return;

            bool isColony = target is Settlement settlement && settlement.HasMap;
            bool isOutpost = target is WorldObject_WD_Outpost;
            if (!((isColony && seth.notifyIncomingRaidColony) || (isOutpost && seth.notifyIncomingRaidOutpost)))
                return;

            float expectedRaidPoints = RaidPointsHelper.ClampRaidPointsToStorytellerBand(
                attStrengthAtArrival,
                ResolveRaidPointsMapForTarget(target));
            string raidPointsStr = expectedRaidPoints.ToString("F0");
            string targetName = target.LabelCap;
            string attFaction = attacker.Faction.Name;
            string attLabel = attacker.Label;
            string eta = FormatRaidEtaDays(traveler);

            string label;
            string text;
            if (isColony)
            {
                if (colonyFactionDispatch)
                {
                    label = "TSA_WD_Letter_IncomingRaid_Colony_Dispatch_Label".Translate(targetName, attFaction);
                    text = "TSA_WD_Letter_IncomingRaid_Colony_Dispatch_Text".Translate(
                        attFaction, attLabel, targetName, raidPointsStr, eta);
                }
                else
                {
                    label = "TSA_WD_Letter_IncomingRaid_Colony_Label".Translate(targetName, attFaction);
                    text = "TSA_WD_Letter_IncomingRaid_Colony_Text".Translate(attLabel, targetName, raidPointsStr, eta);
                }
            }
            else
            {
                float defenderWinChance = 1f - attackerWinChance;
                label = "TSA_WD_Letter_IncomingRaid_Outpost_Label".Translate(targetName, attFaction);
                text = "TSA_WD_Letter_IncomingRaid_Outpost_Text".Translate(
                    attLabel,
                    targetName,
                    raidPointsStr,
                    defStrength.ToString("F0"),
                    defenderWinChance.ToStringPercent(),
                    eta);
            }

            ReceiveIncomingRaidLetter(label, text, attacker, target, traveler);
        }

        private static string FormatRaidEtaDays(WorldObject_Traveler traveler)
        {
            if (traveler != null && traveler.TryGetTotalExpectedTravelDays(out float days) && days > 0f)
                return days.ToString("F1");
            return "?";
        }

        private static void ReceiveIncomingRaidLetter(
            string label,
            string text,
            WorldObject attacker,
            WorldObject target,
            WorldObject_Traveler traveler)
        {
            LetterDef letterDef = DefDatabase<LetterDef>.GetNamed("TSA_WD_IncomingRaid", errorOnFail: false)
                ?? LetterDefOf.ThreatBig;
            LookTargets look = traveler != null && !traveler.Destroyed
                ? new LookTargets(traveler)
                : (attacker != null ? new LookTargets(attacker) : null);
            ChoiceLetter letter = LetterMaker.MakeLetter(label, text, letterDef, look);
            if (letter is ChoiceLetter_IncomingWdRaid raidLetter)
            {
                raidLetter.attacker = attacker;
                raidLetter.target = target;
                raidLetter.traveler = traveler;
            }
            Find.LetterStack.ReceiveLetter(letter);
        }

        private static void OrderRaidCandidates(PendingRaidEvaluation eval)
        {
            var pending = eval.pending;
            if (pending == null || pending.Count <= 1)
            {
                if (pending != null && pending.Count == 1)
                    eval.preferredBand = WD_TargetDistanceBandOrder.BandIndex(pending[0].dist, eval.raidRange);
                return;
            }

            var manager = eval.manager;
            var attacker = eval.attacker;
            var seth = eval.seth;
            float maxRange = Mathf.Max(0.001f, eval.raidRange);

            if (manager == null || attacker?.Faction == null)
            {
                eval.preferredBand = OrderByDistanceTargetBands(pending, preferPlayerInBand: false, maxRange);
                return;
            }

            // Coalition keeps absolute priority: members bucket the coalition target's objects to the front,
            // then each bucket is distance-banded.
            Faction coalitionTarget = manager.GetActiveCoalitionTarget();
            bool prioritizeCoalition = coalitionTarget != null
                && manager.IsActiveCoalitionMember(attacker.Faction)
                && WorldActions_Utils.SafeHostileTo(attacker.Faction, coalitionTarget)
                && Rand.Value < manager.GetCoalitionRaidPriorityChance();

            // Escalation Mid/Late: soft boost only inside a band (player targets earlier within the same distance band).
            Faction player = Faction.OfPlayerSilentFail;
            WdEscalationStage stage = manager.cachedEscalationStage;
            float biasPct = WdEscalation.GetRaidBiasPct(seth, stage);
            bool preferPlayerInBand = WdEscalation.IsMidOrLate(manager)
                && biasPct > 0f
                && player != null
                && seth != null && seth.allowPlayerRaid
                && WorldActions_Utils.SafeHostileTo(attacker.Faction, player)
                && manager.CanReachAnyPlayerTargetPublic(attacker, seth);

            // Quest raid bias: bucket priority-target factions first (before coalition early-return).
            HashSet<int> questPriorityIds = manager.GetQuestRaidBiasPriorityTargetLoadIds(attacker.Faction);
            if (questPriorityIds != null && questPriorityIds.Count > 0)
            {
                eval.preferredBand = BucketPriorityFactionLoadIdDistanceBands(pending, questPriorityIds, preferPlayerInBand, maxRange);
                return;
            }

            if (prioritizeCoalition)
            {
                eval.preferredBand = BucketPriorityFactionDistanceBands(pending, coalitionTarget, preferPlayerInBand, maxRange);
                return;
            }

            eval.preferredBand = OrderByDistanceTargetBands(pending, preferPlayerInBand, maxRange);
        }

        private static int OrderByDistanceTargetBands(
            List<PendingRaidEvaluation.CandidateEntry> list,
            bool preferPlayerInBand,
            float maxRange)
        {
            return WD_TargetDistanceBandOrder.OrderWeightedPreferredThenCloserThenFarther(
                list,
                e => e.dist,
                maxRange,
                band =>
                {
                    if (preferPlayerInBand)
                        OrderBandPreferPlayer(band);
                    else
                        band.Shuffle();
                });
        }

        /// <summary>Within one distance band: shuffle player vs non-player separately, players first (soft late-game boost).</summary>
        private static void OrderBandPreferPlayer(List<PendingRaidEvaluation.CandidateEntry> band)
        {
            if (band == null || band.Count <= 1) return;

            var players = new List<PendingRaidEvaluation.CandidateEntry>();
            var others = new List<PendingRaidEvaluation.CandidateEntry>();
            for (int i = 0; i < band.Count; i++)
            {
                var c = band[i];
                if (c.target?.Faction != null && c.target.Faction.IsPlayer)
                    players.Add(c);
                else
                    others.Add(c);
            }

            players.Shuffle();
            others.Shuffle();
            band.Clear();
            band.AddRange(players);
            band.AddRange(others);
        }

        private static int BucketPriorityFactionDistanceBands(
            List<PendingRaidEvaluation.CandidateEntry> pending,
            Faction priorityFaction,
            bool preferPlayerInBand,
            float maxRange)
        {
            var ids = new HashSet<int>();
            if (priorityFaction != null)
                ids.Add(priorityFaction.loadID);
            return BucketPriorityFactionLoadIdDistanceBands(pending, ids, preferPlayerInBand, maxRange);
        }

        private static int BucketPriorityFactionLoadIdDistanceBands(
            List<PendingRaidEvaluation.CandidateEntry> pending,
            HashSet<int> priorityFactionLoadIds,
            bool preferPlayerInBand,
            float maxRange)
        {
            if (pending == null || priorityFactionLoadIds == null || priorityFactionLoadIds.Count == 0)
                return -1;

            var priority = new List<PendingRaidEvaluation.CandidateEntry>();
            var rest = new List<PendingRaidEvaluation.CandidateEntry>();
            for (int i = 0; i < pending.Count; i++)
            {
                var c = pending[i];
                Faction f = c.target?.Faction;
                if (f != null && priorityFactionLoadIds.Contains(f.loadID))
                    priority.Add(c);
                else
                    rest.Add(c);
            }

            int preferred = OrderByDistanceTargetBands(priority, preferPlayerInBand, maxRange);
            OrderByDistanceTargetBands(rest, preferPlayerInBand, maxRange);
            pending.Clear();
            pending.AddRange(priority);
            pending.AddRange(rest);
            return preferred;
        }
    }
}
