using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;
using UnityEngine;

namespace TSA_WorldDomination
{
    public static class Raid_Simulated
    {
        /// <summary>
        /// At raid resolution (settlement arrival or caravan interception), abort if diplomacy changed
        /// so the attacker is no longer hostile to the defender. Refunds committed strength via contribution DNA.
        /// </summary>
        /// <param name="defenderFactionOverride">When set (e.g. player caravan interception), used instead of <paramref name="target"/>.Faction.</param>
        /// <returns>True if the raid was aborted (caller should stop processing).</returns>
        public static bool TryAbortIfNoLongerHostile(
            WorldObject_Traveler traveler,
            WorldObject attacker,
            WorldObject target,
            WorldComponent_SpreadManager manager,
            Faction defenderFactionOverride = null)
        {
            if (traveler == null) return false;

            Faction attFac = attacker?.Faction ?? TravelerEndpointUtility.GetRaidAttackerFaction(traveler);
            Faction defFac = defenderFactionOverride ?? target?.Faction;
            if (attFac == null || defFac == null) return false;
            if (WorldActions_Utils.SafeHostileTo(attFac, defFac)) return false;

            float efficiency = (traveler.initialStrength > 0) ? (traveler.travelerStrength / traveler.initialStrength) : 1f;
            WorldObject attWorld = attacker ?? traveler.originObject ?? traveler;

            var abortEntry = new SpreadLogEntry(
                "TSA_WD_Log_Raid_Aborted_NoLongerHostile".Translate(attFac.Name, defFac.Name),
                attWorld, target);
            abortEntry.isRaid = true;
            abortEntry.isAttempt = true;
            abortEntry.isAborted = true;
            abortEntry.attStr = traveler.initialStrength;
            abortEntry.efficiencyFactor = efficiency;
            abortEntry.defStr = 0f;
            manager?.AddLog(abortEntry);
            RefundStrength(traveler, 1.0f, efficiency);
            RefundAlliedRaidOrderGoodwill(traveler);
            traveler.suppressDestroyedWorldFx = true;
            return true;
        }

        /// <summary>Returns true when Feature B marauding accepted a new target and the traveler should be kept alive by the caller instead of destroyed.</summary>
        public static bool ExecuteTravelerRaid(WorldObject_Traveler traveler, WorldComponent_SpreadManager manager)
        {
            if (traveler?.targetObject is WorldObject_WD_Outpost outpost && outpost.Faction == Faction.OfPlayer)
            {
                HandlePlayerOutpostRaidArrival(traveler, manager);
                return false;
            }

            var seth = WorldDominationMod.settings;
            WorldObject attacker = TravelerEndpointUtility.GetRaidAttackerContext(traveler);
            Faction attackerFaction = TravelerEndpointUtility.GetRaidAttackerFaction(traveler);
            WorldObject target = traveler.targetObject;

            float efficiency = (traveler.initialStrength > 0) ? (traveler.travelerStrength / traveler.initialStrength) : 1.0f;

            if (!TravelerEndpointUtility.IsLiveEndpoint(target) || target.Faction == attackerFaction)
            {
                string abortKey = !TravelerEndpointUtility.IsLiveEndpoint(target) ? "TSA_WD_Log_Raid_Aborted_TargetNull" :
                    "TSA_WD_Log_Raid_Aborted_SameFaction";
                var abortEntry = new SpreadLogEntry(abortKey.Translate(), attacker, target);
                abortEntry.isRaid = true;
                abortEntry.isAttempt = true;
                abortEntry.isAborted = true;
                abortEntry.attStr = traveler.initialStrength;
                abortEntry.efficiencyFactor = efficiency;
                abortEntry.defStr = 0f;
                manager?.AddLog(abortEntry);
                RefundStrength(traveler, 1.0f);
                RefundAlliedRaidOrderGoodwill(traveler);
                traveler.suppressDestroyedWorldFx = true;
                return false;
            }

            if (TryAbortIfNoLongerHostile(traveler, attacker, target, manager))
                return false;

            var targetComp = target.GetComponent<CompViralSpread>();

            if (target is Settlement settlement && target.Faction != null && target.Faction.IsPlayer && !(target is WorldObject_WD_Outpost))
            {
                if (attacker is Settlement attSettlement)
                {
                    // World caravan becomes a map raid; not a combat wipe overlay.
                    traveler.suppressDestroyedWorldFx = true;
                    Raid_OnPlayerColony.HandleRaidOnPlayer(attSettlement, settlement, traveler.raidAttackerList, traveler.travelerStrength, traveler.raidAttackerDetails, manager, traveler);
                }
                else
                {
                    var abortEntry = new SpreadLogEntry("TSA_WD_Log_Raid_Aborted_TargetNull".Translate(), attacker, target);
                    abortEntry.isRaid = true;
                    abortEntry.isAttempt = true;
                    abortEntry.isAborted = true;
                    abortEntry.attStr = traveler.initialStrength;
                    abortEntry.efficiencyFactor = efficiency;
                    abortEntry.defStr = 0f;
                    manager?.AddLog(abortEntry);
                    RefundStrength(traveler, 1.0f);
                    RefundAlliedRaidOrderGoodwill(traveler);
                    traveler.suppressDestroyedWorldFx = true;
                }
                return false;
            }

            // --- COMBAT DATA (shared snapshot: same defender discovery + totals as preview/launch; primary counted once) ---
            var objectsWithComp = WorldActions_Utils.GetWorldObjectsWithCompByFaction();
            float attAgg = traveler.travelerStrength;

            var defSnap = Raid_MathSnapshot.BuildDefenders(target, attacker, attackerFaction, objectsWithComp, manager, seth);
            float defAgg = defSnap.Total;

            List<WorldObject> fullDefList = new List<WorldObject> { target };
            foreach (var a in defSnap.allies) fullDefList.Add(a.obj);

            float ratio = attAgg / (defAgg > 0 ? defAgg : 1f);
            RaidResolvedOutcome resolved = RaidCasualtyModel.Resolve(ratio, seth);
            bool won = resolved.attackerWon;
            float winChance = resolved.winChance;
            float attLossPct = resolved.attLossPct;
            float defLossPct = resolved.defLossPct;
            BattleMarginTier attSeverity = resolved.attSeverity;
            BattleMarginTier defCoalitionSeverity = resolved.defCoalitionSeverity;

            // Capture "before" with the SAME metric as "after" (total local defense power).
            var defStrengthsBefore = new Dictionary<WorldObject, float>();
            foreach (var wo in fullDefList)
                defStrengthsBefore[wo] = wo.GetComponent<CompViralSpread>()?.GetTotalLocalDefensePower() ?? 0f;

            // Primary loses a fraction of its whole battlefield (unless conquered); allies risk only their committed detachment.
            ApplyDefenderLosses(defSnap, target, won, defLossPct, seth);

            WDVerbose.Msg($"RaidArrival(NPC) {attacker?.LabelCap}->{target.LabelCap}: att={attAgg:F0} def={defAgg:F0} (primary={defSnap.primary.totalLocalDefense:F0}, allies={defSnap.allies.Count}) ratio={ratio:F2} win={winChance:P0} -> {(won ? "WON" : "LOST")} attSev={attSeverity} defCoal={defCoalitionSeverity} attLoss={attLossPct:P0} defLoss={defLossPct:P0}");

            // --- SURGICAL: APPLY DEFENSE COOLDOWN (Triggered Post-Arrival) ---
            if (!won && targetComp != null)
            {
                targetComp.defenseCooldownTick = Find.TickManager.TicksGame + CompViralSpread.CooldownTicksFromDays(seth.cooldownBeingRaidedDays);
            }

            HandleNotifications(target, attacker, won, attackerFaction);

            float survivalPct = 1.0f - attLossPct;
            List<string> finalAttDetails = BuildFinalAttDetails(traveler.raidAttackerList, traveler.raidAttackerDetails, survivalPct);
            List<string> finalDefDetails = BuildFinalDefDetails(fullDefList, defStrengthsBefore, won, target);
            List<RaidForceLogRow> finalAttForceRows = RaidForceLogRow.CloneList(traveler.raidAttackerForceRows);
            List<RaidForceLogRow> finalDefForceRows = RaidForceRow.BuildResolutionDefenderLogRows(fullDefList, defStrengthsBefore, won, target);

            WorldObject victoryOrigin = TravelerEndpointUtility.IsLiveEndpoint(traveler.originObject) ? traveler.originObject : null;

            if (won)
            {
                RefundStrength(traveler, survivalPct);
                traveler.suppressDestroyedWorldFx = true;
                if (traveler.raidOrderOutcome == RaidOrderOutcome.AllyClaimsTarget)
                {
                    return ResolveVictory(target, victoryOrigin, manager, traveler.initialStrength, defAgg, ratio, winChance, finalAttDetails, finalDefDetails, attLossPct, defLossPct, efficiency, traveler.contributionFactors, traveler.CachedLaunchTotalTravelTicks, attackerFaction, attSeverity, defCoalitionSeverity, finalAttForceRows, finalDefForceRows, traveler);
                }
                else if (traveler.raidOrderOutcome == RaidOrderOutcome.AllyAwardsToPlayer)
                {
                    ResolveVictoryAwardedToPlayer(target, traveler, manager, defAgg, ratio, winChance, finalAttDetails, finalDefDetails, attLossPct, defLossPct, efficiency, attSeverity, defCoalitionSeverity, finalAttForceRows, finalDefForceRows);
                    return false;
                }
                else if ((victoryOrigin?.Faction != null && victoryOrigin.Faction.IsPlayer) || (attackerFaction != null && attackerFaction.IsPlayer))
                {
                    ResolveVictoryWithPlayerConquestChoices(target, traveler, manager, defAgg, ratio, winChance, finalAttDetails, finalDefDetails, attLossPct, defLossPct, efficiency, attSeverity, defCoalitionSeverity, finalAttForceRows, finalDefForceRows);
                    return false;
                }
                else
                {
                    return ResolveVictory(target, victoryOrigin, manager, traveler.initialStrength, defAgg, ratio, winChance, finalAttDetails, finalDefDetails, attLossPct, defLossPct, efficiency, traveler.contributionFactors, traveler.CachedLaunchTotalTravelTicks, attackerFaction, attSeverity, defCoalitionSeverity, finalAttForceRows, finalDefForceRows, traveler);
                }
            }
            else
            {
                RefundStrength(traveler, 1.0f - attLossPct);
                ApplyLossesAndLog(attacker, target, traveler.raidAttackerList, fullDefList, attLossPct, defLossPct, "TSA_WD_Log_Raid_Failed".Translate(), null, manager, traveler.initialStrength, defAgg, ratio, false, winChance, finalAttDetails, finalDefDetails, efficiency, traveler.contributionFactors, traveler.CachedLaunchTotalTravelTicks, attSeverity, defCoalitionSeverity, finalAttForceRows, finalDefForceRows);
                return false;
            }
        }

        public static void RefundStrength(WorldObject_Traveler traveler, float survivalMultiplier)
        {
            if (traveler == null || survivalMultiplier <= 0f || traveler.contributionFactors == null) return;
            float totalRefundPool = traveler.travelerStrength * survivalMultiplier;

            foreach (var entry in traveler.contributionFactors)
            {
                if (TravelerEndpointUtility.IsLiveEndpoint(entry.Key))
                {
                    var comp = entry.Key.GetComponent<CompViralSpread>();
                    if (comp != null)
                    {
                        comp.strength += totalRefundPool * entry.Value;
                        comp.CheckTierUpdate();
                    }
                }
            }
        }

        private static void RefundStrength(WorldObject_Traveler traveler, float survivalMultiplier, float efficiency)
        {
            RefundStrength(traveler, survivalMultiplier);
        }

        public static void RefundAlliedRaidOrderGoodwill(WorldObject_Traveler traveler)
        {
            if (traveler == null || traveler.alliedRaidOrderGoodwillRefunded) return;
            if (traveler.raidOrderOutcome == RaidOrderOutcome.PlayerOutpostConquestMenu) return;
            int paid = traveler.alliedRaidOrderGoodwillPaid;
            if (paid <= 0) return;

            Faction ally = traveler.originObject?.Faction ?? traveler.Faction;
            Faction player = Faction.OfPlayerSilentFail;
            if (ally == null || player == null) return;

            GoodwillChangeNotifier.RefundAlliedRaidOrder(ally, traveler.targetObject, paid);
            traveler.alliedRaidOrderGoodwillRefunded = true;
        }

        private static void HandlePlayerOutpostRaidArrival(WorldObject_Traveler traveler, WorldComponent_SpreadManager manager)
        {
            if (traveler?.targetObject is WorldObject_WD_Outpost outpost
                && outpost.Faction == Faction.OfPlayer)
            {
                if (outpost.BlocksAutoRaidResolution())
                    return;

                if (outpost.HasLivingManualDefensePawns())
                {
                    // Dialog holds traveler data; world object despawn is not a combat wipe.
                    traveler.suppressDestroyedWorldFx = true;
                    Find.WindowStack.Add(new Dialog_OutpostDefenseChoice(traveler, outpost, manager));
                    return;
                }
            }

            ResolvePlayerOutpostRaidArrival(traveler, manager);
        }

        /// <summary>
        /// Hostile ground raid caravans that step onto a player mortar or rapid-response outpost tile are diverted
        /// to that fortress (choke-point defense), even when their original target was elsewhere.
        /// Drop-pod raids do not walk tiles and are not intercepted here.
        /// Returns true when the traveler was stopped / consumed for this hop.
        /// </summary>
        public static bool TryInterceptRaidAtFortressOutpost(WorldObject_Traveler traveler)
        {
            if (traveler == null || traveler.Destroyed) return false;
            if (traveler.mission != TravelerMission.Raid) return false;

            Faction player = Faction.OfPlayerSilentFail;
            if (player == null || traveler.Faction == null) return false;
            if (!WorldActions_Utils.SafeHostileTo(traveler.Faction, player)) return false;

            WorldObject_WD_Outpost fortress = FindPlayerFortressOutpostAt(traveler.Tile.tileId);
            if (fortress == null || fortress.Destroyed) return false;

            // Intended destination is already this outpost: normal arrival resolves it.
            if (ReferenceEquals(traveler.targetObject, fortress))
                return false;

            // Already fighting here: do not consume a second caravan into a stuck/destroyed state.
            if (fortress.BlocksAutoRaidResolution())
                return false;

            WorldObject previousTarget = traveler.targetObject;
            traveler.pather?.StopDead();
            traveler.targetObject = fortress;

            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            string prevLabel = previousTarget?.LabelCap ?? "?";
            WDVerbose.Msg($"Raid choke intercept: {traveler.LabelCap} diverted from {prevLabel} to fortress {fortress.LabelCap} tile={fortress.Tile.tileId}");
            manager?.AddLog(new SpreadLogEntry(
                "TSA_WD_Log_Raid_ChokeIntercept".Translate(traveler.LabelCap, fortress.LabelCap, prevLabel),
                traveler, fortress));

            HandlePlayerOutpostRaidArrival(traveler, manager);

            // Same cleanup as ExecuteArrival raid path (dialog may hold a destroyed traveler ref; fields remain readable).
            if (traveler != null && !traveler.Destroyed)
                traveler.Destroy();

            return true;
        }

        private static WorldObject_WD_Outpost FindPlayerFortressOutpostAt(int tileId)
        {
            if (tileId < 0 || Find.WorldObjects == null) return null;

            WorldObject_WD_Outpost best = null;
            foreach (WorldObject wo in Find.WorldObjects.ObjectsAt(tileId))
            {
                if (wo is not WorldObject_WD_Outpost op || op.Destroyed) continue;
                if (op.Faction == null || !op.Faction.IsPlayer) continue;
                if (!op.IsMortarOutpost && !op.IsRapidResponseOutpost) continue;

                if (op.BlocksAutoRaidResolution())
                    return op;

                if (best == null || (op.HasLivingManualDefensePawns() && !best.HasLivingManualDefensePawns()))
                    best = op;
            }
            return best;
        }

        public static void ResolvePlayerOutpostRaidArrival(
            WorldObject_Traveler traveler,
            WorldComponent_SpreadManager manager,
            bool? forcedAttackerWon = null,
            bool suppressOutpostLetter = false,
            bool allowSkirmishRetry = true)
        {
            var seth = WorldDominationMod.settings;
            WorldObject attacker = TravelerEndpointUtility.GetRaidAttackerContext(traveler);
            Faction attackerFaction = TravelerEndpointUtility.GetRaidAttackerFaction(traveler);
            WorldObject target = traveler.targetObject;
            float efficiency = (traveler.initialStrength > 0) ? (traveler.travelerStrength / traveler.initialStrength) : 1.0f;

            if (!TravelerEndpointUtility.IsLiveEndpoint(target) || target.Faction == attackerFaction)
            {
                string abortKey = !TravelerEndpointUtility.IsLiveEndpoint(target) ? "TSA_WD_Log_Raid_Aborted_TargetNull" :
                    "TSA_WD_Log_Raid_Aborted_SameFaction";
                var abortEntry = new SpreadLogEntry(abortKey.Translate(), attacker, target);
                abortEntry.isRaid = true;
                abortEntry.isAttempt = true;
                abortEntry.isAborted = true;
                abortEntry.attStr = traveler.initialStrength;
                abortEntry.efficiencyFactor = efficiency;
                abortEntry.defStr = 0f;
                manager?.AddLog(abortEntry);
                RefundStrength(traveler, 1.0f);
                RefundAlliedRaidOrderGoodwill(traveler);
                if (target is WorldObject_WD_Outpost abortOutpost)
                    abortOutpost.ClearPendingSkirmishDefense();
                traveler.suppressDestroyedWorldFx = true;
                return;
            }

            if (TryAbortIfNoLongerHostile(traveler, attacker, target, manager))
            {
                if (target is WorldObject_WD_Outpost hostileAbortOutpost)
                    hostileAbortOutpost.ClearPendingSkirmishDefense();
                return;
            }

            // Shared snapshot: same defender discovery + totals as preview/launch/NPC arrival; primary counted once.
            var objectsWithComp = WorldActions_Utils.GetWorldObjectsWithCompByFaction();
            var targetComp = target.GetComponent<CompViralSpread>();
            float effectiveAttAgg = traveler.travelerStrength;

            var defSnap = Raid_MathSnapshot.BuildDefenders(target, attacker, attackerFaction, objectsWithComp, manager, seth);
            float finalDefAgg = defSnap.Total;

            var fullDefListLocal = new List<WorldObject> { target };
            foreach (var a in defSnap.allies) fullDefListLocal.Add(a.obj);

            float ratio = effectiveAttAgg / (finalDefAgg > 0 ? finalDefAgg : 1f);
            RaidResolvedOutcome resolved = forcedAttackerWon.HasValue
                ? RaidCasualtyModel.Resolve(ratio, seth, forcedAttackerWon)
                : RaidCasualtyModel.Resolve(ratio, seth);
            bool won = resolved.attackerWon;
            float winChance = resolved.winChance;
            float attLossPct = resolved.attLossPct;
            float defLossPct = resolved.defLossPct;
            BattleMarginTier attSeverity = resolved.attSeverity;
            BattleMarginTier defCoalitionSeverity = resolved.defCoalitionSeverity;

            // Soft second chance for simulated auto-resolve only (not map-forced outcomes).
            if (won
                && allowSkirmishRetry
                && !forcedAttackerWon.HasValue
                && target is WorldObject_WD_Outpost skirmishOutpost
                && skirmishOutpost.Faction == Faction.OfPlayer
                && skirmishOutpost.HasLivingManualDefensePawns())
            {
                WDVerbose.Msg($"RaidArrival(Outpost) skirmish follow-up at {skirmishOutpost.LabelCap}: att={effectiveAttAgg:F0} def={finalDefAgg:F0} ratio={ratio:F2} winChance={winChance:P0}");
                traveler.suppressDestroyedWorldFx = true;
                WD_OutpostDefenseSkirmishUtility.BeginSkirmishFollowUp(traveler, skirmishOutpost, manager);
                return;
            }

            if (target is WorldObject_WD_Outpost resolvedOutpost)
                resolvedOutpost.ClearPendingSkirmishDefense();

            // Capture "before" with the SAME metric as "after" (total local defense power), BEFORE applying losses.
            var defStrengthsBefore = new Dictionary<WorldObject, float>();
            foreach (var wo in fullDefListLocal)
                defStrengthsBefore[wo] = wo.GetComponent<CompViralSpread>()?.GetTotalLocalDefensePower() ?? 0f;

            // Primary loses a fraction of its whole battlefield (unless conquered); allies risk only their committed detachment.
            ApplyDefenderLosses(defSnap, target, won, defLossPct, seth);

            if (!won && targetComp != null)
            {
                targetComp.defenseCooldownTick = Find.TickManager.TicksGame + CompViralSpread.CooldownTicksFromDays(CompViralSpread.GetDefenseCooldownDaysFor(target));
            }

            WDVerbose.Msg($"RaidArrival(Outpost) {attacker?.LabelCap}->{target.LabelCap}: att={effectiveAttAgg:F0} def={finalDefAgg:F0} (primary={defSnap.primary.totalLocalDefense:F0}, allies={defSnap.allies.Count}) ratio={ratio:F2} win={winChance:P0} -> {(won ? "WON" : "LOST")} attSev={attSeverity} defCoal={defCoalitionSeverity} attLoss={attLossPct:P0} defLoss={defLossPct:P0}");

            float survivalPct = 1.0f - attLossPct;
            List<string> finalAttDetails = BuildFinalAttDetails(traveler.raidAttackerList, traveler.raidAttackerDetails, survivalPct);
            List<string> finalDefDetails = BuildFinalDefDetails(fullDefListLocal, defStrengthsBefore, won, target);
            List<RaidForceLogRow> finalAttForceRows = RaidForceLogRow.CloneList(traveler.raidAttackerForceRows);
            List<RaidForceLogRow> finalDefForceRows = RaidForceRow.BuildResolutionDefenderLogRows(fullDefListLocal, defStrengthsBefore, won, target);

            WorldObject victoryOrigin = TravelerEndpointUtility.IsLiveEndpoint(traveler.originObject) ? traveler.originObject : null;

            if (won)
            {
                if (!suppressOutpostLetter)
                    HandleNotifications(target, attacker, won, attackerFaction, captivesTaken: 0);

                int tile = target.Tile;
                string originalName = (target is Settlement s) ? (s.Name ?? s.LabelCap) : target.LabelCap;
                Faction targetFaction = target.Faction;
                bool targetWasPlayerOwned = targetFaction != null && targetFaction.IsPlayer;
                SettlementTier tier = targetComp?.tier ?? SettlementTier.T1;

                RefundStrength(traveler, survivalPct);
                traveler.suppressDestroyedWorldFx = true;
                if (traveler.raidOrderOutcome == RaidOrderOutcome.AllyClaimsTarget)
                {
                    ResolveVictory(target, victoryOrigin, manager, traveler.initialStrength, finalDefAgg, ratio, winChance, finalAttDetails, finalDefDetails, attLossPct, defLossPct, efficiency, traveler.contributionFactors, traveler.CachedLaunchTotalTravelTicks, attackerFaction, attSeverity, defCoalitionSeverity, finalAttForceRows, finalDefForceRows);
                }
                else
                {
                    if (targetWasPlayerOwned && target is WorldObject_WD_Outpost playerOutpost
                        && (attSeverity == BattleMarginTier.Close || attSeverity == BattleMarginTier.Normal))
                    {
                        playerOutpost.SpawnRetreatCaravan(1f - defLossPct);
                    }

                    ApplyLossesAndLog(attacker, target, traveler.raidAttackerList, null, attLossPct, defLossPct, "TSA_WD_Log_Raid_Successful".Translate(originalName), originalName, manager, traveler.initialStrength, finalDefAgg, ratio, true, winChance, finalAttDetails, finalDefDetails, efficiency, traveler.contributionFactors, traveler.CachedLaunchTotalTravelTicks, attSeverity, defCoalitionSeverity, finalAttForceRows, finalDefForceRows);

                    NotifyCommonEnemyQuestBeforeDestroy(target);
                    target.Destroy();
                    if (targetWasPlayerOwned)
                    {
                        ApplyNpcRaidVictoryTileOutcome(tile, originalName, tier, attackerFaction, traveler.initialStrength, attLossPct, traveler.contributionFactors, targetFaction);
                    }
                    else if (traveler.raidOrderOutcome == RaidOrderOutcome.AllyAwardsToPlayer)
                    {
                        ConquestOpportunityUtility.RegisterSimulatedConquest(tile, originalName, tier);
                        Find.WindowStack.Add(new Dialog_OutpostSelection(tile, originalName, -1, tier, conquestContext: null));
                    }
                    else
                    {
                        ConquestOpportunityUtility.RegisterSimulatedConquestAndOpenMenu(tile, originalName, tier, targetFaction);
                    }
                }
            }
            else
            {
                RefundStrength(traveler, 1.0f - attLossPct);
                ApplyLossesAndLog(attacker, target, traveler.raidAttackerList, null, attLossPct, defLossPct, "TSA_WD_Log_Raid_Failed".Translate(), null, manager, traveler.initialStrength, finalDefAgg, ratio, false, winChance, finalAttDetails, finalDefDetails, efficiency, traveler.contributionFactors, traveler.CachedLaunchTotalTravelTicks, attSeverity, defCoalitionSeverity, finalAttForceRows, finalDefForceRows);

                int captivesTaken = 0;
                // Auto-resolve defender win: virtual captives (manual map path harvests before wipe instead).
                if (!forcedAttackerWon.HasValue
                    && target is WorldObject_WD_Outpost defendedOutpost
                    && defendedOutpost.Faction == Faction.OfPlayer
                    && !defendedOutpost.Destroyed)
                {
                    captivesTaken = OutpostPrisonerUtility.GenerateVirtualCaptivesAfterDefense(
                        defendedOutpost,
                        attackerFaction,
                        traveler.initialStrength,
                        attLossPct);
                }

                if (!suppressOutpostLetter)
                    HandleNotifications(target, attacker, won, attackerFaction, captivesTaken);
            }
        }

        private static List<string> BuildFinalAttDetails(List<WorldObject> attList, List<string> attDetails, float survivalPct)
        {
            List<string> list = new List<string>();
            if (attList == null) return list;
            char delim = Raid_ReinforcementLogic.DetailTooltipDelimiter;
            for (int i = 0; i < attList.Count; i++)
            {
                string baseInfo = (attDetails != null && i < attDetails.Count) ? attDetails[i] : (attList[i]?.LabelCap ?? "Unknown");
                string survivedSuffix = " (Survived: " + (survivalPct * 100f).ToString("F0") + "%)";
                if (baseInfo.IndexOf(delim) >= 0)
                {
                    int idx = baseInfo.IndexOf(delim);
                    list.Add(baseInfo.Substring(0, idx) + survivedSuffix + delim + baseInfo.Substring(idx + 1));
                }
                else
                    list.Add(baseInfo + survivedSuffix);
            }
            return list;
        }

        /// <summary>
        /// Applies defender losses consistently for all simulated raids: the primary target loses a fraction of its whole
        /// battlefield (offensive + defensive) unless it was conquered (won == true, handled by the caller's destroy/replace),
        /// while each ally only loses the detachment it committed (<c>defLossPct * committed</c>) instead of a fraction of its
        /// entire garrison.
        /// </summary>
        private static void ApplyDefenderLosses(RaidDefenderSnapshot defSnap, WorldObject target, bool won, float defLossPct, WorldDominationSettings seth)
        {
            if (defSnap == null) return;
            if (!won)
                target?.GetComponent<CompViralSpread>()?.ReduceStrength(defLossPct, true);
            foreach (var a in defSnap.allies)
                a.obj?.GetComponent<CompViralSpread>()?.ReduceOffensiveByAmount(defLossPct * a.committed, true);
        }

        /// <summary>Before/after rows for the final raid log. Both sides use the SAME metric (total local defense power) so deltas are honest; a conquered target reports 0 after.</summary>
        private static List<string> BuildFinalDefDetails(List<WorldObject> fullDefList, Dictionary<WorldObject, float> before, bool won, WorldObject target)
        {
            List<string> list = new List<string>();
            char delim = Raid_ReinforcementLogic.DetailTooltipDelimiter;
            for (int i = 0; i < fullDefList.Count; i++)
            {
                var wo = fullDefList[i];
                float b = before.ContainsKey(wo) ? before[wo] : 0f;
                var woComp = wo.GetComponent<CompViralSpread>();
                float a = (won && wo == target) ? 0f : (woComp?.GetTotalLocalDefensePower() ?? 0f);
                string display = $"{wo.LabelCap}: {b:F0} -> {a:F0}";
                string tip = "TSA_WD_OfCurrentStrength".Translate(b.ToString("F0")) + " → " + "TSA_WD_StrengthAfter".Translate(a.ToString("F0"));
                list.Add(display + delim + tip);
            }
            return list;
        }

        private static void ResolveVictoryWithPlayerConquestChoices(WorldObject target, WorldObject_Traveler traveler, WorldComponent_SpreadManager manager, float defAgg, float ratio, float winChance, List<string> attDet, List<string> defDet, float attLoss, float defLoss, float efficiency, BattleMarginTier attSeverity = BattleMarginTier.Normal, BattleMarginTier defCoalitionSeverity = BattleMarginTier.Normal, List<RaidForceLogRow> attForceRows = null, List<RaidForceLogRow> defForceRows = null)
        {
            int tile = target.Tile;
            string originalName = (target is Settlement sOld) ? (sOld.Name ?? sOld.LabelCap) : target.LabelCap;
            Faction targetFaction = target.Faction;
            SettlementTier tier = target.GetComponent<CompViralSpread>()?.tier ?? SettlementTier.T1;
            WorldObject attacker = TravelerEndpointUtility.GetRaidAttackerContext(traveler);

            ApplyLossesAndLog(attacker, target, traveler.raidAttackerList, null!, attLoss, defLoss, "TSA_WD_Log_Raid_Successful".Translate(originalName), originalName, manager, traveler.initialStrength, defAgg, ratio, true, winChance, attDet, defDet, efficiency, traveler.contributionFactors, traveler.CachedLaunchTotalTravelTicks, attSeverity, defCoalitionSeverity, attForceRows, defForceRows);

            NotifyCommonEnemyQuestBeforeDestroy(target);
            target.Destroy();
            ConquestOpportunityUtility.RegisterSimulatedConquestAndOpenMenu(tile, originalName, tier, targetFaction);
        }

        private static void ResolveVictoryAwardedToPlayer(WorldObject target, WorldObject_Traveler traveler, WorldComponent_SpreadManager manager, float defAgg, float ratio, float winChance, List<string> attDet, List<string> defDet, float attLoss, float defLoss, float efficiency, BattleMarginTier attSeverity = BattleMarginTier.Normal, BattleMarginTier defCoalitionSeverity = BattleMarginTier.Normal, List<RaidForceLogRow> attForceRows = null, List<RaidForceLogRow> defForceRows = null)
        {
            int tile = target.Tile;
            string originalName = (target is Settlement sOld) ? (sOld.Name ?? sOld.LabelCap) : target.LabelCap;
            Faction targetFaction = target.Faction;
            SettlementTier tier = target.GetComponent<CompViralSpread>()?.tier ?? SettlementTier.T1;
            WorldObject attacker = TravelerEndpointUtility.GetRaidAttackerContext(traveler);

            ApplyLossesAndLog(attacker, target, traveler.raidAttackerList, null!, attLoss, defLoss, "TSA_WD_Log_Raid_Successful".Translate(originalName), originalName, manager, traveler.initialStrength, defAgg, ratio, true, winChance, attDet, defDet, efficiency, traveler.contributionFactors, traveler.CachedLaunchTotalTravelTicks, attSeverity, defCoalitionSeverity, attForceRows, defForceRows);

            NotifyCommonEnemyQuestBeforeDestroy(target);
            target.Destroy();
            ConquestOpportunityUtility.RegisterSimulatedConquest(tile, originalName, tier);
            Find.WindowStack.Add(new Dialog_OutpostSelection(tile, originalName, -1, tier, conquestContext: null!));
        }

        private static int CountNpcSurfaceSettlements()
        {
            int total = 0;
            var globalSettlements = Find.WorldObjects?.Settlements;
            if (globalSettlements == null) return 0;
            for (int i = 0; i < globalSettlements.Count; i++)
            {
                var gs = globalSettlements[i];
                if (gs.Faction != null && !gs.Faction.IsPlayer && !gs.Faction.def.hidden
                    && PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(gs))
                    total++;
            }
            return total;
        }

        private static void CreateConquerorSettlement(int tile, string name, SettlementTier tier, Faction attackerFaction, float attAgg, float attLoss, Dictionary<WorldObject, float> dna, Faction previousOwnerFaction = null)
        {
            var seth = WorldDominationMod.settings;
            int maxSettlements = seth?.maxSettlements ?? WorldDominationSettings.DefMaxSettlements;
            if (CountNpcSurfaceSettlements() >= maxSettlements)
            {
                WorldObject_WdSettlementRuin.Spawn(tile, name, attackerFaction);
                TryNotifySettlementRazed(tile, name, attackerFaction);
                return;
            }

            Settlement newS = (Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
            newS.SetFaction(attackerFaction);
            newS.Tile = tile;
            newS.Name = name;

            var nc = newS.GetComponent<CompViralSpread>();
            if (nc != null)
            {
                nc.tier = tier;
                nc.defenseCooldownTick = Find.TickManager.TicksGame + CompViralSpread.CooldownTicksFromDays(WorldDominationMod.settings.cooldownBeingRaidedDays);
                float tierMax = (tier == SettlementTier.T1) ? 500f :
                                (tier == SettlementTier.T2) ? 1000f :
                                (tier == SettlementTier.T3) ? 1600f : 2250f;
                float survivingStrength = attAgg * (1f - attLoss);
                float amountToStay = Mathf.Min(survivingStrength, tierMax);
                float tierMin = CompViralSpread.GetStrengthRange(tier).min;
                if (amountToStay < tierMin) amountToStay = tierMin;
                nc.strength = amountToStay;
                nc.defensiveStrength = nc.GetBaseDefensiveStrength();

                float overflow = survivingStrength - amountToStay;
                if (overflow > 1f && dna != null)
                {
                    foreach (var entry in dna)
                    {
                        if (TravelerEndpointUtility.IsLiveEndpoint(entry.Key))
                            entry.Key.GetComponent<CompViralSpread>()?.AddStrength(overflow * entry.Value);
                    }
                }
            }

            Find.WorldObjects.Add(newS);
            Find.World?.GetComponent<Text_WorldTierOnSettlements>()?.NotifyTierLabelCacheDirty();

            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (manager != null)
            {
                string factionName = attackerFaction?.Name ?? "?";
                manager.AddLog(new SpreadLogEntry(
                    "TSA_WD_Log_ConquestSettlementFounded".Translate(factionName, newS.LabelCap, tier.ToString()),
                    newS));
            }

            TryNotifySettlementCaptured(newS, attackerFaction, previousOwnerFaction);
        }

        /// <summary>After a successful NPC raid: roll raze chance (timed WD ruins) or spawn attacker settlement.</summary>
        private static void ApplyNpcRaidVictoryTileOutcome(int tile, string originalName, SettlementTier tier, Faction attackerFaction, float attAgg, float attLoss, Dictionary<WorldObject, float> dna, Faction previousOwnerFaction = null)
        {
            if (attackerFaction == null) return;
            var seth = WorldDominationMod.settings;
            float razeChance = Mathf.Clamp01(seth?.razeChance ?? WorldDominationSettings.DefRazeChance);
            if (Rand.Value < razeChance)
            {
                WorldObject_WdSettlementRuin.Spawn(tile, originalName, attackerFaction);
                TryNotifySettlementRazed(tile, originalName, attackerFaction);
                return;
            }
            CreateConquerorSettlement(tile, originalName, tier, attackerFaction, attAgg, attLoss, dna, previousOwnerFaction);
        }

        private static void TryNotifySettlementCaptured(Settlement settlement, Faction attackerFaction, Faction previousOwnerFaction)
        {
            if (settlement == null || settlement.Tile < 0) return;
            var seth = WorldDominationMod.settings;
            if (seth == null || !seth.notifyNpcConquestSettlement) return;
            if (!WD_NotifyProximity.IsWithinPlayerNotificationRadius(settlement.Tile)) return;
            string attackerName = attackerFaction?.Name ?? settlement.Faction?.Name ?? "Unknown";
            string settlementName = settlement.LabelCap;
            string previousName = previousOwnerFaction?.Name ?? "Unknown";
            Find.LetterStack.ReceiveLetter(
                "TSA_WD_Letter_SettlementCaptured_Label".Translate(),
                "TSA_WD_Letter_SettlementCaptured_Text".Translate(attackerName, settlementName, previousName),
                LetterDefOf.NeutralEvent,
                new GlobalTargetInfo(settlement));
        }

        private static void TryNotifySettlementRazed(int tile, string originalName, Faction attackerFaction)
        {
            if (tile < 0) return;
            var seth = WorldDominationMod.settings;
            if (seth == null || !seth.notifySettlementRazed) return;
            if (!WD_NotifyProximity.IsWithinPlayerNotificationRadius(tile)) return;
            string attackerName = attackerFaction?.Name ?? "Unknown";
            Find.LetterStack.ReceiveLetter(
                "TSA_WD_Letter_SettlementRazed_Label".Translate(),
                "TSA_WD_Letter_SettlementRazed_Text".Translate(originalName ?? "Settlement", attackerName),
                LetterDefOf.NeutralEvent,
                new GlobalTargetInfo(tile));
        }

        /// <summary>Returns true when Feature B marauding accepted a new target for <paramref name="continuationTraveler"/> after this conquest (caller must not destroy it).</summary>
        private static bool ResolveVictory(WorldObject target, WorldObject originObject, WorldComponent_SpreadManager manager, float attAgg, float defAgg, float ratio, float winChance, List<string> attDet, List<string> defDet, float attLoss, float defLoss, float efficiency, Dictionary<WorldObject, float> dna, float pathTravelTicks = -1f, Faction attackerFactionOverride = null, BattleMarginTier attSeverity = BattleMarginTier.Normal, BattleMarginTier defCoalitionSeverity = BattleMarginTier.Normal, List<RaidForceLogRow> attForceRows = null, List<RaidForceLogRow> defForceRows = null, WorldObject_Traveler continuationTraveler = null)
        {
            int tile = target.Tile;
            string originalName = (target is Settlement sOld) ? (sOld.Name ?? sOld.LabelCap) : target.LabelCap;
            Faction attackerFaction = originObject?.Faction ?? attackerFactionOverride;
            Faction previousOwnerFaction = target.Faction;

            SettlementTier tier = target.GetComponent<CompViralSpread>()?.tier ?? SettlementTier.T1;
            NotifyCommonEnemyQuestBeforeDestroy(target);
            target.Destroy();
            ApplyNpcRaidVictoryTileOutcome(tile, originalName, tier, attackerFaction, attAgg, attLoss, dna, previousOwnerFaction);

            ApplyLossesAndLog(originObject, target, null, null, attLoss, defLoss, "TSA_WD_Log_Raid_Successful".Translate(originalName), originalName, manager, attAgg, defAgg, ratio, true, winChance, attDet, defDet, efficiency, dna, pathTravelTicks, attSeverity, defCoalitionSeverity, attForceRows, defForceRows);

            return TargetOfOpportunityUtility.TryContinueMarauding(continuationTraveler, tile, originalName, manager);
        }

        private static void NotifyCommonEnemyQuestBeforeDestroy(WorldObject target)
        {
            if (target is Settlement settlement)
                WdCommonEnemySettlementQuestHelper.NotifySettlementRemoved(settlement);
        }

        private static void HandleNotifications(WorldObject target, WorldObject attacker, bool won, Faction attackerFactionOverride = null, int captivesTaken = 0)
        {
            if (target.Faction == null || !target.Faction.IsPlayer || target is not WorldObject_WD_Outpost) return;

            // won = attacker won = player outpost lost. Global flag; ignore notification radius.
            if (won && !(WorldDominationMod.settings?.notifyOutpostDestroyed ?? true)) return;

            string attackerName = attacker?.Faction?.Name ?? attackerFactionOverride?.Name ?? "Unknown";
            string label = won ? "TSA_WD_Letter_OutpostRaided_Label".Translate() : "TSA_WD_Letter_OutpostDefended_Label".Translate();
            string text = won
                ? "TSA_WD_Letter_OutpostRaided_Text".Translate(target.LabelCap, attackerName)
                : "TSA_WD_Letter_OutpostDefended_Text".Translate(target.LabelCap, attackerName);
            if (!won && captivesTaken > 0)
                text += "\n\n" + "TSA_WD_Letter_OutpostDefended_Captives".Translate(captivesTaken.ToString());
            Find.LetterStack.ReceiveLetter(label, text, won ? LetterDefOf.NegativeEvent : LetterDefOf.PositiveEvent, target);
        }

        private static void ApplyLossesAndLog(WorldObject attacker, WorldObject target, List<WorldObject> attList, List<WorldObject> defList, float attLossPct, float defLossPct, string message, string customOldLabel, WorldComponent_SpreadManager manager, float attAgg, float defAgg, float ratio, bool victory, float winChance, List<string> attDetails, List<string> defDetails, float efficiency = 1f, Dictionary<WorldObject, float> dna = null, float pathTravelTicks = -1f, BattleMarginTier attSeverity = BattleMarginTier.Normal, BattleMarginTier defCoalitionSeverity = BattleMarginTier.Normal, List<RaidForceLogRow> attForceRows = null, List<RaidForceLogRow> defForceRows = null)
        {
            SpreadLogEntry entry = new SpreadLogEntry(message, attacker, target);
            if (customOldLabel != null) entry.labelB = customOldLabel;
            entry.isRaid = true;
            entry.isAttempt = false;
            entry.attStr = attAgg;
            entry.defStr = defAgg;
            entry.ratio = ratio;
            entry.victory = victory;
            entry.winChance = winChance;
            entry.attLossPct = attLossPct;
            entry.defLossPct = defLossPct;
            entry.attSeverityTier = attSeverity;
            entry.defCoalitionSeverityTier = defCoalitionSeverity;
            entry.marginTier = attSeverity;
            entry.attDetails = attDetails;
            entry.defDetails = defDetails;
            entry.attForceRows = attForceRows ?? new List<RaidForceLogRow>();
            entry.defForceRows = defForceRows ?? new List<RaidForceLogRow>();
            entry.efficiencyFactor = efficiency;
            entry.pathTravelTicks = pathTravelTicks;
            entry.highlightKind = victory ? SpreadLogHighlightKind.RaidSuccess : SpreadLogHighlightKind.None;

            if (dna != null)
            {
                foreach (var kvp in dna)
                {
                    entry.contributionDNAKeys.Add(kvp.Key?.LabelCap ?? "Unknown");
                    entry.contributionDNAValues.Add(kvp.Value);
                }
            }
            manager.AddLog(entry);
        }
    }
}