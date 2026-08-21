using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using UnityEngine;

namespace TSA_WorldDomination
{
    public static partial class WorldActions_Traveler
    {


        /// <summary>RR / warehouse ballistic drop pods (higher = slower). Default for settings.</summary>
        public const int DropPodTicksPerMove = 15;



        public static int GetDropPodTicksPerMove()
        {
            float v = WorldDominationMod.settings?.dropPodTicksPerMove ?? DropPodTicksPerMove;
            return Mathf.Max(1, Mathf.RoundToInt(v));
        }

        public static bool ValidateMission(WorldObject_Traveler traveler, PlanetTile destTile)
        {
            switch (traveler.mission)
            {
                case TravelerMission.Expansion:
                    int dest = destTile.tileId;
                    var seth = WorldDominationMod.settings;

                    bool isOccupied = false;
                    if (Find.WorldObjects.AnyWorldObjectAt(dest))
                    {
                        foreach (var wo in Find.WorldObjects.ObjectsAt(dest))
                        {
                            if (wo != traveler) { isOccupied = true; break; }
                        }
                    }

                    if (isOccupied) return false;
                    if (!TileFinder.IsValidTileForNewSettlement(dest)) return false;
                    if (Outpost_EstablishmentRequirements.IsTileBlockedByMinDistanceCached(dest)) return false;
                    var spreadMgr = Find.World.GetComponent<WorldComponent_SpreadManager>();
                    if (WorldActions_GrowthExpand.IsTargetSaturated(dest, traveler.Faction, seth, spreadMgr)) return false;
                    return true;

                case TravelerMission.Raid:
                case TravelerMission.RaidDropPod:
                    if (traveler.isTurretDetour)
                    {
                        if (traveler.targetObject is WorldObject_AT_Turret liveTurret && !liveTurret.Destroyed)
                            return true;
                        // Turret gone mid-route: resume original target instead of aborting the whole raid.
                        return AtTurretRetaliationUtility.TryResumeAfterTurretDetour(traveler);
                    }
                    return TravelerEndpointUtility.IsLiveEndpoint(traveler.targetObject);

                case TravelerMission.RoadBuilding:
                    if (traveler.originObject == null || traveler.originObject.Destroyed) return false;
                    RoadDef plannedRoad = WorldActions_Roads.GetRoadDefForActor(traveler.originObject);
                    if (plannedRoad == null) return true;
                    // Must validate only the edge we just crossed. Allow crossing segments already at the planned
                    // tier so the pather can reuse them toward the next gap (ShouldUpgradeRoad is false there).
                    int prev = traveler.pather.previousTileId;
                    if (prev < 0) return true;
                    return WorldActions_Roads.RoadBuilderMayCrossEdge(prev, traveler.Tile.tileId, traveler.Tile.Layer, plannedRoad);

                case TravelerMission.RoadBlock:
                    return traveler.originObject != null && !traveler.originObject.Destroyed;

                case TravelerMission.SpikeTrap:
                    return traveler.originObject != null && !traveler.originObject.Destroyed;

                case TravelerMission.AtTurret:
                    return traveler.originObject != null && !traveler.originObject.Destroyed;

                case TravelerMission.NpcAtTurret:
                    if (traveler.originObject == null || traveler.originObject.Destroyed) return false;
                    return AtTurretUtility.IsEmptyOffRoadTurretSite(destTile.tileId);

                case TravelerMission.NpcFortify:
                    if (traveler.originObject == null || traveler.originObject.Destroyed) return false;
                    // Abort if the destination became a settlement / player colony / outpost tile.
                    return !WorldActions_RoadBlocks.TileHasSettlementOrOutpost(destTile.tileId);

                case TravelerMission.Decontamination:
                    return traveler.originObject != null && !traveler.originObject.Destroyed;

                case TravelerMission.OutpostDelivery:
                    return TravelerEndpointUtility.IsLiveEndpoint(traveler.targetObject)
                        && TravelerEndpointUtility.IsLiveEndpoint(traveler.originObject);
                case TravelerMission.Trader:
                    if (traveler.originObject == null || traveler.originObject.Destroyed ||
                        traveler.targetObject == null || traveler.targetObject.Destroyed)
                        return false;
                    // NPC settlements, player colonies, and player WD outposts are all valid destinations.
                    return true;
                case TravelerMission.OutpostUpgrade:
                    return traveler.targetObject is WorldObject_WD_Outpost targetOutpost && !targetOutpost.Destroyed;
                case TravelerMission.SettlementBuy:
                {
                    if (!(traveler is WorldObject_Traveler_SettlementBuy buyTraveler))
                        return false;
                    return SettlementBuyUtility.IsDealStillValid(buyTraveler);
                }
                case TravelerMission.SettlementGift:
                {
                    if (!(traveler is WorldObject_Traveler_SettlementGift giftTraveler))
                        return false;
                    return SettlementGiftUtility.IsGiftStillValid(giftTraveler);
                }
                case TravelerMission.SettlementBribe:
                case TravelerMission.RaidBribe:
                {
                    if (!(traveler is WorldObject_Traveler_SettlementBribe bribeTraveler))
                        return false;
                    return SettlementBribeUtility.IsBribeStillValid(bribeTraveler);
                }
                case TravelerMission.DiplomacyNegotiate:
                {
                    if (!(traveler is WorldObject_Traveler_DiplomacyNegotiate negotiateTraveler))
                        return false;
                    return DiplomacyNegotiateUtility.IsDealStillValid(negotiateTraveler);
                }
                case TravelerMission.RapidResponseIntercept:
                    if (traveler.targetObject is WorldObject_Traveler rapidTarget)
                        return !rapidTarget.Destroyed;
                    if (traveler.targetObject is Caravan rapidCaravanTarget)
                        return !rapidCaravanTarget.Destroyed && rapidCaravanTarget.Spawned;
                    return false;
                case TravelerMission.RapidResponseDropPod:
                {
                    // Prefer a live world-object target; otherwise land at the path destination tile.
                    if (TravelerEndpointUtility.IsLiveEndpoint(traveler.targetObject))
                        return true;
                    int id = destTile.tileId;
                    if (id < 0 || !Find.WorldGrid.InBounds(id)) return false;
                    return PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(destTile);
                }

                case TravelerMission.DebugRaidTransit:
                {
                    int id = destTile.tileId;
                    if (id < 0 || !Find.WorldGrid.InBounds(id)) return false;
                    return PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(destTile);
                }

                default:
                    return true;
            }
        }

        public static void ExecuteArrival(WorldObject_Traveler traveler, int previousTileId)
        {
            if (WdPostLoadGuard.ShouldDeferTravelerArrival()
                && (WorldObject_Traveler.IsRaidMission(traveler.mission)
                    || traveler.mission == TravelerMission.MortarStrike
                    || traveler.mission == TravelerMission.AntiAirStrike))
                return;

            if (WorldObject_Traveler.IsRaidMission(traveler.mission)
                && traveler.targetObject is WorldObject_WD_Outpost raidOutpost
                && raidOutpost.BlocksAutoRaidResolution())
                return;

            switch (traveler.mission)
            {
                case TravelerMission.Expansion:
                    ExecuteExpansion(traveler);
                    traveler.Destroy();
                    break;
                case TravelerMission.Raid:
                case TravelerMission.RaidDropPod:
                    var manager = Find.World.GetComponent<WorldComponent_SpreadManager>();

                    traveler.targetRaidDefenseCooldownReservationTick = -1;
                    traveler.playerColonyRaidCooldownReservationTick = -1;

                    // AT turret arrival: open-field clash. Detours resume the original target; primary turret raids refund remnant and end.
                    if (AtTurretRetaliationUtility.TryResolveTurretDetourArrival(traveler, manager))
                        break;

                    // Routes to the new Efficiency/Proportional math. Feature B: a true return means marauding
                    // accepted a new target and re-pathed the traveler; it must not be destroyed here.
                    bool marauding = Raid_Simulated.ExecuteTravelerRaid(traveler, manager);

                    if (!marauding && traveler != null && !traveler.Destroyed)
                    {
                        traveler.Destroy();
                    }
                    break;
                case TravelerMission.RoadBuilding:
                    ExecuteRoadPaving(traveler, previousTileId);
                    if (traveler != null && !traveler.Destroyed)
                        traveler.Destroy();
                    break;
                case TravelerMission.RoadBlock:
                    WorldActions_RoadBlocks.ExecuteRoadBlockArrival(traveler);
                    if (traveler != null && !traveler.Destroyed)
                        traveler.Destroy();
                    break;
                case TravelerMission.SpikeTrap:
                    WorldActions_SpikeTraps.ExecuteSpikeTrapArrival(traveler);
                    if (traveler != null && !traveler.Destroyed)
                        traveler.Destroy();
                    break;
                case TravelerMission.AtTurret:
                    WorldActions_AtTurrets.ExecuteAtTurretArrival(traveler);
                    if (traveler != null && !traveler.Destroyed)
                        traveler.Destroy();
                    break;
                case TravelerMission.NpcAtTurret:
                    WorldActions_NpcFortify.ExecuteNpcAtTurretArrival(traveler);
                    if (traveler != null && !traveler.Destroyed)
                        traveler.Destroy();
                    break;
                case TravelerMission.NpcFortify:
                    WorldActions_NpcFortify.ExecuteFortifyArrival(traveler);
                    if (traveler != null && !traveler.Destroyed)
                        traveler.Destroy();
                    break;
                case TravelerMission.Decontamination:
                    WorldActions_Decontamination.ExecuteDecontaminationArrival(traveler);
                    if (traveler != null && !traveler.Destroyed)
                        traveler.Destroy();
                    break;
                case TravelerMission.OutpostDelivery:
                    if (traveler is WorldObject_Traveler_Outpost_Delivery delivery)
                        ExecuteOutpostDelivery(delivery);
                    if (traveler != null && !traveler.Destroyed)
                        traveler.Destroy();
                    break;
                case TravelerMission.Trader:
                    ExecuteTraderArrival(traveler);
                    if (traveler != null && !traveler.Destroyed)
                        traveler.Destroy();
                    break;
                case TravelerMission.OutpostUpgrade:
                    if (traveler is WorldObject_Traveler_Outpost_Upgrade upgradeTraveler)
                        ExecuteOutpostUpgradeArrival(upgradeTraveler);
                    if (traveler != null && !traveler.Destroyed)
                        traveler.Destroy();
                    break;
                case TravelerMission.SettlementBuy:
                    if (traveler is WorldObject_Traveler_SettlementBuy buyTraveler)
                        ExecuteSettlementBuyArrival(buyTraveler);
                    if (traveler != null && !traveler.Destroyed)
                        traveler.Destroy();
                    break;
                case TravelerMission.SettlementGift:
                    if (traveler is WorldObject_Traveler_SettlementGift giftTraveler)
                        ExecuteSettlementGiftArrival(giftTraveler);
                    if (traveler != null && !traveler.Destroyed)
                        traveler.Destroy();
                    break;
                case TravelerMission.DiplomacyNegotiate:
                    if (traveler is WorldObject_Traveler_DiplomacyNegotiate negotiateTraveler)
                        DiplomacyNegotiateUtility.CompleteArrival(negotiateTraveler);
                    if (traveler != null && !traveler.Destroyed)
                        traveler.Destroy();
                    break;
                case TravelerMission.SettlementBribe:
                    if (traveler is WorldObject_Traveler_SettlementBribe settleBribe)
                        SettlementBribeUtility.ExecuteSettlementBribeArrival(settleBribe);
                    if (traveler != null && !traveler.Destroyed)
                        traveler.Destroy();
                    break;
                case TravelerMission.RaidBribe:
                    bool raidBribeDone = ExecuteRaidBribeArrival(traveler);
                    if (raidBribeDone && traveler != null && !traveler.Destroyed)
                        traveler.Destroy();
                    break;
                case TravelerMission.MortarStrike:
                    ExecuteMortarStrike(traveler);
                    if (traveler != null && !traveler.Destroyed)
                        traveler.Destroy();
                    break;
                case TravelerMission.AntiAirStrike:
                    FinishAntiAirShell(traveler);
                    break;
                case TravelerMission.RapidResponseIntercept:
                    bool rapidResponseDone = ExecuteRapidResponseIntercept(traveler);
                    if (rapidResponseDone && traveler != null && !traveler.Destroyed)
                        traveler.Destroy();
                    break;
                case TravelerMission.RapidResponseDropPod:
                    if (traveler is WorldObject_Traveler_RapidResponseDropPod dropPod)
                        ExecuteRapidResponseDropPodArrival(dropPod);
                    if (traveler != null && !traveler.Destroyed)
                        traveler.Destroy();
                    break;
                case TravelerMission.DebugRaidTransit:
                    // Dev-only pathing test: no raid simulation, no strength refunds (no contributionFactors).
                    if (Prefs.DevMode)
                        Log.Message($"[TSA WD] Debug raid traveler arrived at tile {traveler.Tile.tileId}; despawning.");
                    if (traveler != null && !traveler.Destroyed)
                        traveler.Destroy();
                    break;
            }
        }

        private static bool ExecuteRapidResponseIntercept(WorldObject_Traveler traveler)
        {
            if (traveler == null) return true;
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();

            if (traveler.targetObject is Caravan caravanTarget)
                return ExecuteRapidResponseInterceptVsCaravan(traveler, caravanTarget, manager);

            WorldObject_Traveler target = traveler.targetObject as WorldObject_Traveler;
            if (target == null || target.Destroyed || target.Faction == null || traveler.Faction == null || !WorldActions_Utils.SafeHostileTo(traveler.Faction, target.Faction))
            {
                // Same-tile clash may have already resolved and refunded; just despawn quietly.
                if (traveler.rapidResponseStrengthRefunded)
                {
                    traveler.suppressDestroyedWorldFx = true;
                    return true;
                }
                TravelerEndpointUtility.RefundRapidResponseStrength(traveler, traveler.travelerStrength);
                manager?.AddLog(new SpreadLogEntry(
                    "TSA_WD_Log_RapidResponseAborted".Translate(traveler.originObject?.LabelCap ?? "?"),
                    traveler.originObject,
                    traveler.targetObject));
                traveler.suppressDestroyedWorldFx = true;
                return true;
            }

            if (target.Tile != traveler.Tile)
            {
                traveler.pather?.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(target.Tile, traveler));
                return false;
            }

            float defBefore = Mathf.Max(0f, target.travelerStrength);
            if (traveler.travelerStrength <= 0f || defBefore <= 0f)
                return true;

            Faction enemyFaction = target.Faction;
            OpenFieldClashResult clash = OpenFieldClashUtility.ResolveTravelerClash(traveler, target, traveler, manager);
            if (!clash.ok)
                return true;

            bool won = OpenFieldClashUtility.SideWon(clash, traveler);
            float defAfter = target.Destroyed ? 0f : Mathf.Max(0f, target.travelerStrength);
            int captivesTaken = 0;
            if (won)
            {
                captivesTaken = OutpostPrisonerUtility.TryCaptureFromRapidResponseWin(
                    traveler, enemyFaction, defBefore, defAfter);
            }
            float refund = OpenFieldClashUtility.SurvivorStrengthFor(clash, traveler);
            TravelerEndpointUtility.RefundRapidResponseStrength(traveler, refund);
            SendRapidResponseClashLetter(traveler, target, won, defBefore, defAfter, captivesTaken);
            // Survivors despawn after intercept; do not show wipe art when strength returns home.
            if (!traveler.Destroyed && refund > 0.01f)
                traveler.suppressDestroyedWorldFx = true;
            return true;
        }

        /// <summary>
        /// Feature C (real-caravan ambush/RR): the actual combat is already resolved by the tile-exit landing hook
        /// (<see cref="WD_SameTileTravelerClash.AfterTravelerLanded_TravelerVsCaravan"/>), which runs unconditionally
        /// before mission dispatch on every hop this interceptor lands. This only handles the fallback where the
        /// caravan moved away between the landing check and mission dispatch, or is no longer a valid target.
        /// </summary>
        private static bool ExecuteRapidResponseInterceptVsCaravan(WorldObject_Traveler traveler, Caravan target, WorldComponent_SpreadManager manager)
        {
            if (target == null || target.Destroyed || !target.Spawned || target.Faction == null
                || traveler.Faction == null || !WorldActions_Utils.SafeHostileTo(traveler.Faction, target.Faction))
            {
                if (traveler.rapidResponseStrengthRefunded)
                {
                    traveler.suppressDestroyedWorldFx = true;
                    return true;
                }
                TravelerEndpointUtility.RefundRapidResponseStrength(traveler, traveler.travelerStrength);
                manager?.AddLog(new SpreadLogEntry(
                    "TSA_WD_Log_RapidResponseAborted".Translate(traveler.originObject?.LabelCap ?? "?"),
                    traveler.originObject,
                    traveler.targetObject));
                traveler.suppressDestroyedWorldFx = true;
                return true;
            }

            if (target.Tile == traveler.Tile)
            {
                // The landing hook already handled (or is about to handle, from the same tile-exit block) the
                // actual interception encounter for a hostile caravan on this tile; do not destroy this traveler
                // out from under a queued encounter.
                return false;
            }

            traveler.RefreshRapidResponseInterceptPath(true);
            return false;
        }

        /// <summary>Resolve an RR intercept that is already on the target's tile (waiting / chase catch-up) without requiring a path arrival.</summary>
        public static void TryCompleteRapidResponseSameTile(WorldObject_Traveler traveler)
        {
            if (traveler == null || traveler.Destroyed) return;
            if (traveler.mission != TravelerMission.RapidResponseIntercept) return;
            if (!(traveler.targetObject is WorldObject_Traveler target) || target.Destroyed) return;
            if (target.Tile != traveler.Tile) return;

            if (ExecuteRapidResponseIntercept(traveler) && traveler != null && !traveler.Destroyed)
                traveler.Destroy();
        }

        /// <summary>Deliver a raid bribe when already on the raid's tile (waiting lead intercept / catch-up).</summary>
        public static void TryCompleteRaidBribeSameTile(WorldObject_Traveler traveler)
        {
            if (traveler == null || traveler.Destroyed) return;
            if (traveler.mission != TravelerMission.RaidBribe) return;
            if (!(traveler.targetObject is WorldObject_Traveler target) || target.Destroyed) return;
            if (target.Tile != traveler.Tile) return;

            if (ExecuteRaidBribeArrival(traveler) && traveler != null && !traveler.Destroyed)
                traveler.Destroy();
        }

        private static bool ExecuteRaidBribeArrival(WorldObject_Traveler traveler)
        {
            if (!(traveler is WorldObject_Traveler_SettlementBribe bribe))
                return true;

            if (!SettlementBribeUtility.IsBribeStillValid(bribe, out var failReason))
            {
                SettlementBribeUtility.RefundPayment(bribe, failReason);
                return true;
            }

            var target = bribe.targetObject as WorldObject_Traveler;
            if (target == null || target.Destroyed)
            {
                SettlementBribeUtility.RefundPayment(bribe, SettlementBribeUtility.BribeFailReason.TargetGone);
                return true;
            }

            if (target.Tile != bribe.Tile)
            {
                bribe.RefreshRapidResponseInterceptPath(true);
                return false;
            }

            SettlementBribeUtility.ExecuteRaidBribeArrival(bribe);
            return true;
        }

        /// <summary>Auto-resolve hostile travelers with World Raids open-field math. <paramref name="incoming"/> is the initiator for role fallback.</summary>
        public static OpenFieldClashResult ResolveTravelerClashByRaidMath(
            WorldObject_Traveler a,
            WorldObject_Traveler b,
            WorldObject_Traveler incoming,
            WorldComponent_SpreadManager manager)
            => OpenFieldClashUtility.ResolveTravelerClash(a, b, incoming, manager);

        /// <summary>Legacy name â€” routes to raid-math resolve with <paramref name="attacker"/> as incoming for role fallback.</summary>
        public static void ResolveTravelerClashBySubtraction(
            WorldObject_Traveler attacker,
            WorldObject_Traveler defender,
            WorldComponent_SpreadManager manager)
            => ResolveTravelerClashByRaidMath(attacker, defender, attacker, manager);

        /// <summary>Feature E: stamp the sending settlement's <see cref="CompViralSpread.lastCaravanInterceptedTick"/> when a Trader-mission traveler is actually destroyed via interception (ambush/Rapid Response/mortar), never on ordinary arrival.</summary>
        internal static void StampTraderInterceptedIfApplicable(WorldObject_Traveler destroyed)
        {
            if (destroyed == null || destroyed.mission != TravelerMission.Trader) return;
            var senderComp = destroyed.originObject?.GetComponent<CompViralSpread>();
            senderComp?.MarkCaravanIntercepted();
        }

        /// <summary>
        /// NPC WD traveler that destroys a trader: origin settlement gets the receiver trade reward (strength + possible tier-up).
        /// </summary>
        internal static void TryAwardTraderInterceptLoot(WorldObject_Traveler winner, WorldObject_Traveler loser)
        {
            if (winner == null || loser == null) return;
            if (loser.mission != TravelerMission.Trader) return;
            if (winner.Faction == null || winner.Faction.IsPlayer) return;

            WorldObject origin = winner.originObject;
            CompViralSpread originComp = origin?.GetComponent<CompViralSpread>();
            if (originComp == null) return;

            var seth = WorldDominationMod.settings;
            if (seth == null) return;

            TraderArrivalRewardOutcome outcome = originComp.ApplyTraderArrivalReward(
                seth.traderCaravanReceiverRewardStrength,
                seth.traderTierUpgradeChanceT1ToT2,
                seth.traderTierUpgradeChanceT2ToT3,
                seth.traderTierUpgradeChanceT3ToT4);

            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (manager == null) return;

            manager.AddLog(new SpreadLogEntry(
                "TSA_WD_Log_TraderTrade_HeaderIntercept".Translate(
                    origin?.LabelCap ?? "?",
                    loser.LabelCap),
                origin,
                loser));
            LogTraderTradeParty(
                manager,
                "TSA_WD_Log_TraderTrade_RoleInterceptor".Translate(origin?.LabelCap ?? "?"),
                outcome,
                origin,
                loser);
        }

        private static void SendRapidResponseClashLetter(
            WorldObject_Traveler response,
            WorldObject_Traveler target,
            bool won,
            float targetStrengthBefore,
            float targetStrengthAfter,
            int captivesTaken = 0)
        {
            SendRapidResponseClashLetter(
                response,
                target?.LabelCap ?? "?",
                won,
                targetStrengthBefore,
                targetStrengthAfter,
                captivesTaken,
                target != null && !target.Destroyed
                    ? new LookTargets(target)
                    : (response?.originObject != null ? new LookTargets(response.originObject) : null));
        }

        internal static void SendRapidResponseClashLetter(
            WorldObject_Traveler response,
            string targetLabel,
            bool won,
            float targetStrengthBefore,
            float targetStrengthAfter,
            int captivesTaken,
            LookTargets look)
        {
            if (!ShouldNotifyPlayerRapidResponseClash(response))
                return;

            WorldObject origin = response?.originObject;
            string originLabel = origin?.LabelCap ?? "?";
            Find.LetterStack.ReceiveLetter(
                won ? "TSA_WD_Letter_RapidResponseClashWon_Label".Translate() : "TSA_WD_Letter_RapidResponseClashLost_Label".Translate(),
                won
                    ? FormatRapidResponseClashWonText(originLabel, targetLabel, captivesTaken)
                    : FormatRapidResponseClashLostText(originLabel, targetLabel, targetStrengthBefore, targetStrengthAfter),
                won ? LetterDefOf.PositiveEvent : LetterDefOf.NegativeEvent,
                look);
        }

        /// <summary>
        /// Player-facing Rapid Response clash letters only: real Rapid Response outpost owned by the player.
        /// NPC settlement intercepts (and any other RapidResponseIntercept origins) stay silent.
        /// </summary>
        internal static bool ShouldNotifyPlayerRapidResponseClash(WorldObject_Traveler response)
        {
            if (!(WorldDominationMod.settings?.notifyRapidResponseCaravanClash ?? WorldDominationSettings.DefNotifyRapidResponseCaravanClash))
                return false;
            if (response == null) return false;
            if (!(response.originObject is WorldObject_WD_Outpost outpost) || !outpost.IsRapidResponseOutpost)
                return false;
            if (outpost.Faction?.IsPlayer != true && response.Faction?.IsPlayer != true)
                return false;
            return true;
        }

        internal static TaggedString FormatRapidResponseClashWonText(
            string originLabel,
            string targetLabel,
            int captivesTaken)
        {
            TaggedString text = "TSA_WD_Letter_RapidResponseClashWon_Text".Translate(originLabel, targetLabel);
            if (captivesTaken > 0)
                text += "\n\n" + "TSA_WD_Letter_OutpostDefended_Captives".Translate(captivesTaken.ToString());
            return text;
        }

        internal static TaggedString FormatRapidResponseClashLostText(
            string originLabel,
            string targetLabel,
            float targetStrengthBefore,
            float targetStrengthAfter)
        {
            int before = Mathf.RoundToInt(Mathf.Max(0f, targetStrengthBefore));
            int after = Mathf.RoundToInt(Mathf.Max(0f, targetStrengthAfter));
            return "TSA_WD_Letter_RapidResponseClashLost_Text".Translate(
                originLabel.Named("OUTPOST"),
                targetLabel.Named("TARGET"),
                before.Named("BEFORE"),
                after.Named("AFTER"));
        }


        /// <summary>
        /// Dispatches a Rapid-Response-style interceptor. <paramref name="origin"/> is any live <see cref="WorldObject"/>
        /// with a <see cref="CompViralSpread"/> (player outpost or, since Feature C, a plain NPC settlement).
        /// <paramref name="target"/> is either a WD <see cref="WorldObject_Traveler"/> (trader/gift/bribe/raid) or a
        /// real vanilla <see cref="Caravan"/> — the caravan case relies on <see cref="WD_SameTileTravelerClash.AfterTravelerLanded_TravelerVsCaravan"/>,
        /// already fired unconditionally at every tile-exit, to resolve the actual encounter once this interceptor lands on the caravan's tile.
        /// </summary>
        public static WorldObject_Traveler SpawnRapidResponseInterceptTraveler(WorldObject origin, WorldObject target, float strength)
        {
            if (origin == null || target == null || target.Destroyed) return null;
            if (!(target is WorldObject_Traveler) && !(target is Caravan)) return null;
            var def = DefDatabase<WorldObjectDef>.GetNamedSilentFail("TSA_WD_Traveler_RapidResponseIntercept");
            if (def == null)
            {
                Log.Error("[TSA World Domination] Missing WorldObjectDef TSA_WD_Traveler_RapidResponseIntercept.");
                return null;
            }

            var traveler = (WorldObject_Traveler)WorldObjectMaker.MakeWorldObject(def);
            traveler.Tile = origin.Tile;
            traveler.SetFaction(origin.Faction);
            traveler.originObject = origin;
            traveler.targetObject = target;
            traveler.mission = TravelerMission.RapidResponseIntercept;
            traveler.ticksPerMove = RapidResponseUtility.GetTicksPerMove();
            traveler.travelerStrength = Mathf.Max(0f, strength);
            traveler.initialStrength = traveler.travelerStrength;
            traveler.contributionFactors[origin] = 1f;
            Find.WorldObjects.Add(traveler);
            traveler.RefreshRapidResponseInterceptPath(true);
            return traveler;
        }

        /// <summary>Ballistic RR drop-pod traveler carrying real pawns (warehouse drop-pod speed, RR caravan icon).</summary>
        public static WorldObject_Traveler_RapidResponseDropPod SpawnRapidResponseDropPodTraveler(
            WorldObject_WD_Outpost origin,
            WorldObject target,
            List<Pawn> pawns)
        {
            if (origin == null || target == null || target.Destroyed || pawns == null || pawns.Count == 0)
                return null;
            return SpawnRapidResponseDropPodTraveler(origin, target.Tile.tileId, pawns, target);
        }

        /// <summary>Ballistic RR drop-pod to a destination tile. Optional <paramref name="target"/> enables special arrival (clash, colony map, outpost join).</summary>
        public static WorldObject_Traveler_RapidResponseDropPod SpawnRapidResponseDropPodTraveler(
            WorldObject_WD_Outpost origin,
            int destTileId,
            List<Pawn> pawns,
            WorldObject target = null)
        {
            if (origin == null || pawns == null || pawns.Count == 0)
                return null;
            if (destTileId < 0 || !Find.WorldGrid.InBounds(destTileId))
                return null;
            var def = DefDatabase<WorldObjectDef>.GetNamedSilentFail("TSA_WD_Traveler_RapidResponseDropPod");
            if (def == null)
            {
                Log.Error("[TSA World Domination] Missing WorldObjectDef TSA_WD_Traveler_RapidResponseDropPod.");
                return null;
            }

            var traveler = (WorldObject_Traveler_RapidResponseDropPod)WorldObjectMaker.MakeWorldObject(def);
            traveler.Tile = origin.Tile;
            traveler.SetFaction(origin.Faction);
            traveler.originObject = origin;
            traveler.targetObject = TravelerEndpointUtility.IsLiveEndpoint(target) ? target : null;
            traveler.mission = TravelerMission.RapidResponseDropPod;
            traveler.ticksPerMove = GetDropPodTicksPerMove();
            traveler.travelerStrength = 1f;
            traveler.initialStrength = 1f;
            traveler.carriedPawns = new List<Pawn>(pawns);
            Find.WorldObjects.Add(traveler);
            traveler.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(destTileId, origin));
            // SpawnSetup wakes AA before StartPath (no dest/arc yet). Re-wake so nearby T4 AA sees the flyby.
            AntiAirFireUtils.WakeAllForDropPod(traveler);
            return traveler;
        }

        private static void ExecuteRapidResponseDropPodArrival(WorldObject_Traveler_RapidResponseDropPod traveler)
        {
            if (traveler == null) return;
            List<Pawn> removed = traveler.TakeCarriedPawns();
            if (removed == null || removed.Count == 0) return;

            WorldObject target = traveler.targetObject;
            WorldObject_WD_Outpost origin = traveler.originObject as WorldObject_WD_Outpost;
            int landTile = traveler.Tile.tileId;

            void ReturnHome()
            {
                if (origin == null || origin.Destroyed)
                {
                    for (int i = 0; i < removed.Count; i++)
                    {
                        Pawn p = removed[i];
                        if (p != null && !p.Destroyed) p.Destroy();
                    }
                    return;
                }
                for (int i = 0; i < removed.Count; i++)
                {
                    Pawn p = removed[i];
                    if (p == null || p.Destroyed || p.Dead) continue;
                    origin.AddPawn(p, null!);
                }
            }

            if (TravelerEndpointUtility.IsLiveEndpoint(target))
            {
                if (target is WorldObject_Traveler hostileTraveler)
                {
                    WD_CaravanClashUtility.StartInterceptionEncounterDropPods(removed, hostileTraveler);
                    return;
                }

                if (target is WorldObject_WD_Outpost targetOutpost && RapidResponseUtility.MapAtTile(targetOutpost.Tile) == null)
                {
                    int added = 0;
                    for (int i = 0; i < removed.Count; i++)
                    {
                        Pawn pawn = removed[i];
                        if (pawn == null || pawn.Destroyed || pawn.Dead) continue;
                        if (targetOutpost.AddPawn(pawn, null!))
                            added++;
                        else if (origin != null && !origin.Destroyed)
                            origin.AddPawn(pawn, null!);
                    }
                    Messages.Message("TSA_WD_RapidResponse_DropPodsArrived".Translate(added.ToString(), targetOutpost.LabelCap), MessageTypeDefOf.NeutralEvent, false);
                    return;
                }

                Map map = null;
                if (target is MapParent mapParent && mapParent.HasMap)
                    map = mapParent.Map;
                else
                    map = RapidResponseUtility.MapAtTile(target.Tile);

                if (map != null)
                {
                    if (target is Caravan targetCaravan && !targetCaravan.Destroyed)
                        CaravanEnterMapUtility.Enter(targetCaravan, map, CaravanEnterMode.Edge);
                    RapidResponseUtility.DropPawnsViaDropPods(removed, map);
                    Messages.Message("TSA_WD_RapidResponse_DropPodsArrived".Translate(removed.Count.ToString(), target.LabelCap), MessageTypeDefOf.NeutralEvent, false);
                    return;
                }
            }

            // Tile destination (or WO lost mid-flight / no map): drop on map if present, else form a caravan.
            if (landTile < 0 || !Find.WorldGrid.InBounds(landTile))
            {
                ReturnHome();
                Messages.Message("TSA_WD_RapidResponse_DropPodsAborted".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            Map landMap = RapidResponseUtility.MapAtTile(landTile);
            if (landMap != null)
            {
                RapidResponseUtility.DropPawnsViaDropPods(removed, landMap);
                string mapLabel = landMap.Parent?.LabelCap ?? ("#" + landTile);
                Messages.Message("TSA_WD_RapidResponse_DropPodsArrived".Translate(removed.Count.ToString(), mapLabel), MessageTypeDefOf.NeutralEvent, false);
                return;
            }

            Faction faction = traveler.Faction ?? Faction.OfPlayer;
            Caravan caravan = CaravanMaker.MakeCaravan(removed, faction, landTile, true);
            string destLabel = caravan != null ? caravan.LabelCap : ("#" + landTile);
            Messages.Message("TSA_WD_RapidResponse_DropPodsArrived".Translate(removed.Count.ToString(), destLabel), MessageTypeDefOf.NeutralEvent, false);
        }

        private static void ExecuteOutpostUpgradeArrival(WorldObject_Traveler_Outpost_Upgrade traveler)
        {
            if (!(traveler.targetObject is WorldObject_WD_Outpost outpost)) return;
            bool applied = outpost.ApplyPendingUpgrade();
            var manager = Find.World.GetComponent<WorldComponent_SpreadManager>();
            var upDef = DefDatabase<OutpostUpgradeDef>.GetNamedSilentFail(traveler.upgradeDefName);
            string upgradeLabel = upDef?.LabelCap ?? traveler.upgradeDefName;
            if (applied)
                manager?.AddLog(new SpreadLogEntry("TSA_WD_Log_OutpostUpgradeApplied".Translate(outpost.LabelCap, upgradeLabel, traveler.upgradeLevel.ToString()), traveler.originObject, outpost));
            else
                manager?.AddLog(new SpreadLogEntry("TSA_WD_Log_OutpostUpgradeApplyFailed".Translate(outpost.LabelCap), traveler.originObject, outpost));
        }

        private static void ExecuteTraderArrival(WorldObject_Traveler traveler)
        {
            var seth = WorldDominationMod.settings;
            if (seth == null) return;

            WorldObject origin = traveler.originObject;
            WorldObject target = traveler.targetObject;

            CompViralSpread senderComp = origin?.GetComponent<CompViralSpread>();
            TraderArrivalRewardOutcome senderOutcome = senderComp?.ApplyTraderArrivalReward(
                seth.traderCaravanSenderRewardStrength,
                seth.traderTierUpgradeChanceT1ToT2,
                seth.traderTierUpgradeChanceT2ToT3,
                seth.traderTierUpgradeChanceT3ToT4) ?? TraderArrivalRewardOutcome.NoEffect;

            CompViralSpread receiverComp = target?.GetComponent<CompViralSpread>();
            TraderArrivalRewardOutcome receiverOutcome = receiverComp?.ApplyTraderArrivalReward(
                seth.traderCaravanReceiverRewardStrength,
                seth.traderTierUpgradeChanceT1ToT2,
                seth.traderTierUpgradeChanceT2ToT3,
                seth.traderTierUpgradeChanceT3ToT4) ?? TraderArrivalRewardOutcome.NoEffect;

            Faction senderFaction = origin?.Faction;
            Faction receiverFaction = target?.Faction;
            bool playerOrdered = traveler.playerOrderedTrader;
            if (!playerOrdered)
            {
                int goodwill = Mathf.RoundToInt(seth.traderCaravanGoodwillGain);
                if (goodwill > 0 && senderFaction != null && receiverFaction != null && senderFaction != receiverFaction)
                {
                    if (senderFaction.IsPlayer || receiverFaction.IsPlayer)
                    {
                        senderFaction.TryAffectGoodwillWith(receiverFaction, goodwill);
                        GoodwillChangeNotifier.NotifyTraderCaravanArrival(senderFaction, receiverFaction, goodwill);
                    }
                    else
                    {
                        WorldActions_DiplomacyBuffsNerfs.ApplyNpcTraderGoodwill(senderFaction, receiverFaction, goodwill);
                    }
                }
            }

            if (target is Settlement colonyTarget && colonyTarget.Faction == Faction.OfPlayer)
            {
                FactionRelationKind rel = WorldActions_Utils.SafeRelationKindWith(senderFaction, Faction.OfPlayerSilentFail);
                bool hostile = rel == FactionRelationKind.Hostile;
                Faction blockingTraderFaction = null;
                bool blockedByHostileTrader = !hostile
                    && colonyTarget.HasMap
                    && Trader_OnPlayerColony.TryFindHostileTraderAlreadyOnMap(
                        colonyTarget.Map, senderFaction, out blockingTraderFaction);

                if (hostile || !colonyTarget.HasMap)
                {
                    if (playerOrdered)
                    {
                        Messages.Message(
                            hostile
                                ? "TSA_WD_OrderedTrader_SpawnSkippedHostile".Translate(senderFaction?.Name ?? "?", colonyTarget.LabelCap)
                                : "TSA_WD_OrderedTrader_SpawnSkippedNoMap".Translate(colonyTarget.LabelCap),
                            colonyTarget,
                            MessageTypeDefOf.NeutralEvent);
                    }
                }
                else if (blockedByHostileTrader)
                {
                    Messages.Message(
                        "TSA_WD_Trader_SpawnSkippedHostileVisitor".Translate(
                            senderFaction?.Name ?? "?",
                            blockingTraderFaction?.Name ?? "?",
                            colonyTarget.LabelCap),
                        colonyTarget,
                        MessageTypeDefOf.NeutralEvent);
                }
                else
                {
                    TraderKindDef kind = playerOrdered ? traveler.orderedTraderKind : null;
                    if (!Trader_OnPlayerColony.TrySpawnTraderOnPlayerColony(colonyTarget, senderFaction, kind) && playerOrdered)
                    {
                        Messages.Message(
                            "TSA_WD_OrderedTrader_SpawnFailed".Translate(colonyTarget.LabelCap),
                            colonyTarget,
                            MessageTypeDefOf.NegativeEvent);
                    }
                }
            }

            var manager = Find.World.GetComponent<WorldComponent_SpreadManager>();
            if (manager != null)
            {
                manager.AddLog(new SpreadLogEntry(
                    "TSA_WD_Log_TraderTrade_Header".Translate(origin?.LabelCap ?? "?", target?.LabelCap ?? "?"),
                    origin,
                    target));

                string senderLabel = origin?.LabelCap ?? "?";
                string receiverLabel = target?.LabelCap ?? "?";
                LogTraderTradeParty(manager, "TSA_WD_Log_TraderTrade_RoleSender".Translate(senderLabel), senderOutcome, origin, target);
                LogTraderTradeParty(manager, "TSA_WD_Log_TraderTrade_RoleReceiver".Translate(receiverLabel), receiverOutcome, origin, target);
            }
        }

        private static void LogTraderTradeParty(WorldComponent_SpreadManager manager, string roleTagged, TraderArrivalRewardOutcome outcome, WorldObject a, WorldObject b)
        {
            if (outcome == TraderArrivalRewardOutcome.NoEffect)
                manager.AddLog(new SpreadLogEntry("TSA_WD_Log_TraderTrade_NoStrengthLine".Translate(roleTagged), a, b));
            else
            {
                string detail = outcome == TraderArrivalRewardOutcome.StrengthAndTierUp
                    ? "TSA_WD_Log_TraderTrade_OutcomeUpgrade".Translate()
                    : "TSA_WD_Log_TraderTrade_OutcomeStrength".Translate();
                manager.AddLog(new SpreadLogEntry("TSA_WD_Log_TraderTrade_Line".Translate(roleTagged, detail), a, b));
            }
        }

        private static void ExecuteOutpostDelivery(WorldObject_Traveler_Outpost_Delivery delivery)
        {
            if (delivery?.deliveryItems == null || delivery.deliveryItems.Count == 0) return;

            if (delivery.targetObject is WorldObject_WD_Outpost whOutpost && Outpost_Warehouse_Delivery.IsWarehouseOutpost(whOutpost))
            {
                var whComp = CompOutpostWarehouse.Get(whOutpost);
                if (whComp == null) return;
                whComp.TryDeposit(delivery.deliveryItems);
                string originLabel = delivery.originObject?.Label ?? "?";
                string text = "TSA_WD_Warehouse_Deposit_Letter".Translate(originLabel, whOutpost.LabelCap) + "\n";
                foreach (var tc in delivery.deliveryItems)
                {
                    if (tc?.thingDef == null || tc.count <= 0) continue;
                    text += "  - " + tc.thingDef.LabelCap + " x" + tc.count + "\n";
                }
                string letterLabel = "TSA_WD_Warehouse_Deposit_LetterLabel".Translate(whOutpost.LabelCap);
                if (WorldDominationMod.settings?.notifyWarehouseGoodsArrived ?? WorldDominationSettings.DefNotifyWarehouseGoodsArrived)
                    Find.LetterStack.ReceiveLetter(letterLabel, text.TrimEnd(), LetterDefOf.PositiveEvent, whOutpost);
                return;
            }

            var mapParent = delivery.targetObject as MapParent;
            if (mapParent == null || !mapParent.HasMap) return;

            if (delivery.deliveryViaDropPod)
            {
                ExecuteOutpostDeliveryColonyDropPods(delivery, mapParent);
                return;
            }

            ExecuteOutpostDeliveryColonyCaravan(delivery, mapParent);
        }

        private static void ExecuteOutpostDeliveryColonyCaravan(WorldObject_Traveler_Outpost_Delivery delivery, MapParent mapParent)
        {
            var map = mapParent.Map;
            IntVec3 dropCell;
            Building deliverySpot = FindDeliverySpot(map);
            if (deliverySpot != null)
            {
                dropCell = deliverySpot.Position;
            }
            else
            {
            var dir = Find.WorldGrid.GetRotFromTo(mapParent.Tile, delivery.Tile);
                dropCell = FindDeliveryDropCell(map, dir);
            }

            // When a delivery spot is placed, cluster goods as tightly as possible around it
            // (Near spirals outward to the closest free cell and merges into existing stacks).
            // Map-edge arrivals keep the looser drifting scatter for flavor.
            bool clusterTight = deliverySpot != null;

            var lookAt = new List<Thing>();
            var things = new List<Thing>();
            IntVec3 nextCell = dropCell;
            foreach (var tc in delivery.deliveryItems)
            {
                if (tc?.thingDef == null || tc.count <= 0) continue;
                int remaining = tc.count;
                while (remaining > 0)
                {
                    int chunk = GetDeliveryChunkSize(tc.thingDef, remaining);
                    if (chunk <= 0) break;
                    remaining -= chunk;
                    Thing t = MakeDeliveryThing(tc.thingDef, chunk, tc.stuff, tc.quality);
                    if (t == null) continue;
                    things.Add(t);
                    if (clusterTight)
                        TryPlaceDeliveryThing(t, ref nextCell, map, dropCell, lookAt, clusterTight: true);
                    else
                        TryPlaceDeliveryThing(t, ref nextCell, map, dropCell, lookAt, clusterTight: false);
                }
            }

            if (things.Count > 0 && delivery.originObject != null)
            {
                string text = "TSA_WD_OutpostDelivery_Letter".Translate(delivery.originObject.Label) + "\n";
                foreach (var tc in delivery.deliveryItems)
                {
                    if (tc?.thingDef == null || tc.count <= 0) continue;
                    text += "  - " + tc.thingDef.LabelCap + " x" + tc.count + "\n";
                }
                string letterLabel = "TSA_WD_OutpostDelivery_LetterLabel".Translate(delivery.originObject.Label);
                if (WorldDominationMod.settings?.notifyOutpostDeliveryToColonyArrived ?? WorldDominationSettings.DefNotifyOutpostDeliveryToColonyArrived)
                Find.LetterStack.ReceiveLetter(letterLabel, text, LetterDefOf.PositiveEvent, new LookTargets(lookAt));
            }
        }

        private static void ExecuteOutpostDeliveryColonyDropPods(WorldObject_Traveler_Outpost_Delivery delivery, MapParent mapParent)
        {
            var map = mapParent.Map;
            Building deliverySpot = FindDeliverySpot(map);
            IntVec3 dropCell;
            if (deliverySpot != null)
                dropCell = deliverySpot.Position;
            else
            {
                var dir = Find.WorldGrid.GetRotFromTo(mapParent.Tile, delivery.Tile);
                dropCell = FindDeliveryDropCell(map, dir);
            }

            var things = new List<Thing>();
            foreach (var tc in delivery.deliveryItems)
            {
                if (tc?.thingDef == null || tc.count <= 0) continue;
                int remaining = tc.count;
                while (remaining > 0)
                {
                    int chunk = GetDeliveryChunkSize(tc.thingDef, remaining);
                    if (chunk <= 0) break;
                    remaining -= chunk;
                    Thing t = MakeDeliveryThing(tc.thingDef, chunk, tc.stuff, tc.quality);
                    if (t != null) things.Add(t);
                }
            }

            if (things.Count == 0) return;

            DropPodUtility.DropThingsNear(dropCell, map, things);

            if (delivery.originObject != null)
            {
                string text = "TSA_WD_OutpostDelivery_Letter".Translate(delivery.originObject.Label) + "\n";
                foreach (var tc in delivery.deliveryItems)
                {
                    if (tc?.thingDef == null || tc.count <= 0) continue;
                    text += "  - " + tc.thingDef.LabelCap + " x" + tc.count + "\n";
                }
                string letterLabel = "TSA_WD_OutpostDelivery_LetterLabel".Translate(delivery.originObject.Label);
                if (WorldDominationMod.settings?.notifyOutpostDeliveryToColonyArrived ?? WorldDominationSettings.DefNotifyOutpostDeliveryToColonyArrived)
                    Find.LetterStack.ReceiveLetter(letterLabel, text, LetterDefOf.PositiveEvent, new LookTargets(dropCell, map));
            }
        }

        private const int DeliveryBorderInset = 4;
        private const int DeliveryScatterRadius = 14;

        private static Thing MakeDeliveryThing(ThingDef def, int stackCount) =>
            MakeDeliveryThing(def, stackCount, preferredStuff: null, preferredQuality: null);

        /// <summary>
        /// Spawns a delivery item. Never passes stuff unless <see cref="BuildableDef.MadeFromStuff"/>;
        /// strips leftover Stuff that would trip CostListAdjusted (e.g. CE-stuffed FlakVest after a mod
        /// removes stuffCategories).
        /// </summary>
        private static Thing MakeDeliveryThing(ThingDef def, int stackCount, ThingDef preferredStuff, QualityCategory? preferredQuality)
        {
            if (def == null || def.category == ThingCategory.Ethereal || def.category == ThingCategory.Pawn) return null;
            // Never spawn the abstract minified-crate root (or other unusable junk) as a delivery.
            if (def.thingClass != null && typeof(MinifiedThing).IsAssignableFrom(def.thingClass)) return null;
            if (!def.PlayerAcquirable || def.IsBlueprint || def.IsFrame || def.destroyOnDrop) return null;
            if (def.category == ThingCategory.Building && !def.Minifiable) return null;

            ThingDef stuff = null;
            if (def.MadeFromStuff)
            {
                if (preferredStuff != null && preferredStuff.IsStuff && preferredStuff.stuffProps != null
                    && preferredStuff.stuffProps.CanMake(def))
                    stuff = preferredStuff;
                if (stuff == null)
                    stuff = GenStuff.RandomStuffByCommonalityFor(def, TechLevel.Undefined);
                if (stuff == null)
                    stuff = GenStuff.RandomStuffInexpensiveFor(def, Faction.OfPlayer);
            }

            Thing t = stuff != null ? ThingMaker.MakeThing(def, stuff) : ThingMaker.MakeThing(def);
            // Defensive: CostListAdjusted(def, stuff) errors when Stuff is set on a non-stuffable def
            // (common after CE adds stuffCategories and another mod strips them).
            if (t != null && t.Stuff != null && !t.def.MadeFromStuff)
            {
                t.SetStuffDirect(null);
                // If a Harmony postfix re-applies Stuff, do not place the broken instance into player inventory
                // (quests call CostListAdjusted on accessible things and will red-error otherwise).
                if (t.Stuff != null)
                {
                    try { t.Destroy(DestroyMode.Vanish); } catch { /* ignore */ }
                    return null;
                }
            }

            if (preferredQuality.HasValue && t != null)
                t.TryGetComp<CompQuality>()?.SetQuality(preferredQuality.Value, ArtGenerationContext.Outsider);

            if (ShouldMinifyForDelivery(def))
            {
                t.stackCount = 1;
                return MinifyUtility.MakeMinified(t);
            }

            int maxStack = def.stackLimit > 0 ? def.stackLimit : stackCount;
            t.stackCount = Mathf.Min(stackCount, maxStack);
            return t;
        }

        private static bool ShouldMinifyForDelivery(ThingDef def) =>
            def != null && def.Minifiable;

        private static int GetDeliveryChunkSize(ThingDef def, int remaining)
        {
            if (def == null || remaining <= 0) return 0;
            if (ShouldMinifyForDelivery(def)) return 1;
            int maxStack = def.stackLimit > 0 ? def.stackLimit : remaining;
            return Mathf.Min(remaining, maxStack);
        }

        private static bool TryPlaceDeliveryThing(Thing t, ref IntVec3 nextCell, Map map, IntVec3 anchor, List<Thing> lookAt, bool clusterTight)
        {
            if (t == null || map == null) return false;

            if (clusterTight)
            {
                if (GenPlace.TryPlaceThing(t, anchor, map, ThingPlaceMode.Near, (placed, _) => lookAt.Add(placed)))
                    return true;
            }
            else if (GenPlace.TryPlaceThing(t, nextCell, map, ThingPlaceMode.Direct, (placed, _) => lookAt.Add(placed)))
            {
                IntVec3 nearCell = CellFinder.RandomClosewalkCellNear(nextCell, map, DeliveryScatterRadius);
                if (nearCell.Standable(map))
                    nextCell = nearCell;
                return true;
            }

            IntVec3 placedAt = anchor;
            if (GenPlace.TryPlaceThing(t, anchor, map, ThingPlaceMode.Near, (placed, _) =>
                {
                    lookAt.Add(placed);
                    placedAt = placed.Position;
                }))
            {
                nextCell = placedAt;
                return true;
            }

            bool Reachable(IntVec3 c) =>
                c.InBounds(map) && c.Standable(map) && !c.Fogged(map) && map.reachability.CanReachColony(c);

            placedAt = anchor;
            if (CellFinderLoose.TryGetRandomCellWith(Reachable, map, 1000, out IntVec3 fallback)
                && GenPlace.TryPlaceThing(t, fallback, map, ThingPlaceMode.Near, (placed, _) =>
                {
                    lookAt.Add(placed);
                    placedAt = placed.Position;
                }))
            {
                nextCell = placedAt;
                return true;
            }

            Log.Warning($"[TSA WD] Could not place outpost delivery item {t.Label} on {map}. Destroying.");
            t.Destroy(DestroyMode.Vanish);
            return false;
        }

        /// <summary>
        /// Pick a cell on the colony map for delivered outpost goods that is guaranteed to be
        /// standable and reachable by the colony, so items never spawn under a mountain or behind
        /// deep water. Prefers an open, unroofed edge cell on the side facing the sending outpost,
        /// inset at least <see cref="DeliveryBorderInset"/> cells from the map border, then degrades
        /// gracefully through several reachable fallbacks.
        /// </summary>
        private static IntVec3 FindDeliveryDropCell(Map map, Rot4 dir)
        {
            // Reachable + standable + visible. CanReachColony excludes cells walled off by
            // mountain or separated from the base by deep water.
            bool Reachable(IntVec3 c) =>
                c.InBounds(map) && c.Standable(map) && !c.Fogged(map)
                && map.reachability.CanReachColony(c);

            // Ideal cells additionally avoid natural mountain (thick) roofs so goods land in the open.
            bool Ideal(IntVec3 c)
            {
                if (!Reachable(c)) return false;
                RoofDef roof = c.GetRoof(map);
                return roof == null || !roof.isThickRoof;
            }

            IntVec3 InsetFromBorder(IntVec3 edgeCell)
            {
                if (!dir.IsValid || DeliveryBorderInset <= 0) return edgeCell;
                IntVec3 inward = dir.Opposite.FacingCell;
                for (int steps = DeliveryBorderInset; steps >= 1; steps--)
                {
                    IntVec3 candidate = edgeCell;
                    for (int i = 0; i < steps; i++)
                        candidate += inward;
                    if (Ideal(candidate)) return candidate;
                    if (Reachable(candidate)) return candidate;
                }
                return edgeCell;
            }

            IntVec3 cell;

            // 1. Open edge cell on the side facing the outpost (flavor: goods arrive from that direction).
            if (CellFinder.TryFindRandomEdgeCellWith(Ideal, map, dir, CellFinder.EdgeRoadChance_Always, out cell))
                return InsetFromBorder(cell);

            // 2. Any open, reachable edge cell.
            if (CellFinder.TryFindRandomEdgeCellWith(Ideal, map, CellFinder.EdgeRoadChance_Always, out cell))
                return InsetFromBorder(cell);

            // 3. Vanilla trade drop spot: an open cell near the colony that pawns can reach.
            cell = DropCellFinder.TradeDropSpot(map);
            if (Ideal(cell)) return cell;

            // 4. Any reachable standable cell, allowing roofed/cave cells if nothing else is open.
            if (CellFinderLoose.TryGetRandomCellWith(Reachable, map, 1000, out cell))
                return cell;

            // 5. Last resort: trade drop spot regardless (always returns a colony-side cell).
            return DropCellFinder.TradeDropSpot(map);
        }

        /// <summary>The single player-placed outpost delivery spot on this map, or null if none. When present, outpost goods land around it instead of the map edge.</summary>
        public static Building FindDeliverySpot(Map map)
        {
            if (map == null) return null;
            var list = map.listerThings.ThingsOfDef(WD_BuildingDefOf.TSA_WD_OutpostDeliverySpot);
            if (list == null || list.Count == 0) return null;
            Building spot = list[0] as Building;
            return spot != null && spot.Spawned ? spot : null;
        }

        /// <summary>Prefer the player Outpost Delivery Spot; otherwise vanilla trade drop spot.</summary>
        public static IntVec3 FindColonyDeliveryOrTradeDropCell(Map map)
        {
            Building spot = FindDeliverySpot(map);
            if (spot != null)
                return spot.Position;
            return DropCellFinder.TradeDropSpot(map);
        }

        /// <summary>Spawn an outpost delivery traveler to a resolved colony or warehouse target.</summary>
        public static void SpawnOutpostDeliveryTraveler(
            WorldObject_WD_Outpost outpost,
            List<ThingDefCountClass> items,
            WorldObject explicitDestination = null,
            bool viaDropPod = false)
        {
            if (outpost == null || items == null || items.Count == 0) return;
            if (viaDropPod && !RapidResponseUtility.TransportPodsResearched()) return;

            WorldObject destination = explicitDestination;
            if (destination == null && !Outpost_Warehouse_Delivery.TryResolveDeliveryTarget(outpost, out destination))
                return;
            if (!Outpost_Warehouse_Delivery.IsValidItemDeliveryDestination(destination, outpost))
                return;

            var def = DefDatabase<WorldObjectDef>.GetNamedSilentFail("TSA_WD_Traveler_Outpost_Delivery");
            if (def == null) return;

            var traveler = (WorldObject_Traveler_Outpost_Delivery)WorldObjectMaker.MakeWorldObject(def);
            traveler.Tile = outpost.Tile;
            traveler.SetFaction(outpost.Faction);
            traveler.originObject = outpost;
            traveler.targetObject = destination;
            traveler.mission = TravelerMission.OutpostDelivery;
            traveler.deliveryViaDropPod = viaDropPod;
            if (viaDropPod)
                traveler.InvalidateTravelerMaterialCache();
            float cost = WorldDominationMod.settings?.outpostDeliveryStrengthCost ?? 50f;
            traveler.deliveryItems = new List<ThingDefCountClass>(items);
            traveler.ticksPerMove = viaDropPod ? GetDropPodTicksPerMove() : WorldObject_Traveler.DefaultTicksPerMove;
            traveler.travelerStrength = cost;
            Find.WorldObjects.Add(traveler);

            var comp = outpost.GetComponent<CompViralSpread>();
            if (comp != null)
                comp.strength = Mathf.Max(0, comp.strength - cost);

            traveler.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(destination.Tile, outpost));
            if (viaDropPod)
                AntiAirFireUtils.WakeAllForDropPod(traveler);
        }

        /// <param name="origin">Visual dispatch origin (the colony map parent or a contributing warehouse outpost). The traveler is symbolic and carries no cargo.</param>
        public static bool SpawnOutpostUpgradeTraveler(WorldObject_WD_Outpost outpost, WorldObject origin, string upgradeDefName, int level)
        {
            if (outpost == null || origin == null || string.IsNullOrEmpty(upgradeDefName) || level <= 0) return false;
            if (ReferenceEquals(outpost, origin) || outpost.Tile == origin.Tile) return false;
            var def = DefDatabase<WorldObjectDef>.GetNamedSilentFail("TSA_WD_Traveler_Outpost_Upgrade");
            if (def == null) return false;

            bool viaDropPod = OutpostDispatchMode.GetViaDropPod(origin)
                && RapidResponseUtility.TransportPodsResearched();

            var traveler = (WorldObject_Traveler_Outpost_Upgrade)WorldObjectMaker.MakeWorldObject(def);
            traveler.Tile = origin.Tile;
            traveler.SetFaction(Faction.OfPlayer);
            traveler.originObject = origin;
            traveler.targetObject = outpost;
            traveler.mission = TravelerMission.OutpostUpgrade;
            traveler.upgradeViaDropPod = viaDropPod;
            if (viaDropPod)
                traveler.InvalidateTravelerMaterialCache();
            traveler.ticksPerMove = viaDropPod ? GetDropPodTicksPerMove() : WorldObject_Traveler.DefaultTicksPerMove;
            traveler.travelerStrength = 100f;
            traveler.initialStrength = 100f;
            traveler.upgradeDefName = upgradeDefName;
            traveler.upgradeLevel = level;
            Find.WorldObjects.Add(traveler);
            traveler.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(outpost.Tile, origin));
            if (traveler.Destroyed)
                return false;
            if (viaDropPod)
                AntiAirFireUtils.WakeAllForDropPod(traveler);
            string upgradeLabel = DefDatabase<OutpostUpgradeDef>.GetNamedSilentFail(upgradeDefName)?.LabelCap ?? upgradeDefName;
            Find.World.GetComponent<WorldComponent_SpreadManager>()?.AddLog(new SpreadLogEntry("TSA_WD_Log_OutpostUpgradeLaunched".Translate(origin.LabelCap, outpost.LabelCap, upgradeLabel, level.ToString()), origin, outpost));
            return true;
        }

        private static bool OriginWantsTradeDropPod(WorldObject origin) =>
            origin != null
            && OutpostDispatchMode.GetViaDropPod(origin)
            && RapidResponseUtility.TransportPodsResearched();

        private static void ApplyTradeDropPodLaunch(WorldObject_Traveler_TradePayment traveler, WorldObject origin)
        {
            bool viaDropPod = OriginWantsTradeDropPod(origin);
            traveler.tradeViaDropPod = viaDropPod;
            traveler.ticksPerMove = viaDropPod ? GetDropPodTicksPerMove() : WorldObject_Traveler.DefaultTicksPerMove;
            if (viaDropPod)
                traveler.InvalidateTravelerMaterialCache();
        }

        private static void WakeTradeDropPodIfNeeded(WorldObject_Traveler_TradePayment traveler)
        {
            if (traveler != null && traveler.tradeViaDropPod && !traveler.Destroyed)
                AntiAirFireUtils.WakeAllForDropPod(traveler);
        }

        public static bool SpawnSettlementBuyTraveler(
            Settlement settlement,
            WorldObject origin,
            List<ThingDefCountClass> paymentItems,
            int pendingGoodwill,
            Faction sellerFaction)
        {
            if (settlement == null || origin == null || settlement.Destroyed) return false;
            if (origin.Tile == settlement.Tile) return false;
            var def = DefDatabase<WorldObjectDef>.GetNamedSilentFail("TSA_WD_Traveler_SettlementBuy");
            if (def == null) return false;

            var traveler = (WorldObject_Traveler_SettlementBuy)WorldObjectMaker.MakeWorldObject(def);
            traveler.Tile = origin.Tile;
            traveler.SetFaction(Faction.OfPlayer);
            traveler.originObject = origin;
            traveler.targetObject = settlement;
            traveler.mission = TravelerMission.SettlementBuy;
            ApplyTradeDropPodLaunch(traveler, origin);
            traveler.travelerStrength = 100f;
            traveler.initialStrength = 100f;
            traveler.pendingGoodwill = Mathf.Max(0, pendingGoodwill);
            traveler.sellerFaction = sellerFaction;
            traveler.dealTier = SettlementBuyUtility.GetCurrentSettlementTier(settlement);
            traveler.paymentItems = paymentItems != null
                ? new List<ThingDefCountClass>(paymentItems)
                : new List<ThingDefCountClass>();
            Find.WorldObjects.Add(traveler);
            traveler.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(settlement.Tile, origin));
            if (traveler.Destroyed)
                return false;
            WakeTradeDropPodIfNeeded(traveler);

            Find.World.GetComponent<WorldComponent_SpreadManager>()?.AddLog(new SpreadLogEntry(
                "TSA_WD_Log_SettlementBuyLaunched".Translate(origin.LabelCap, settlement.LabelCap, sellerFaction?.Name ?? "?"),
                origin,
                settlement));
            return true;
        }

        public static bool SpawnSettlementGiftTraveler(
            Settlement settlement,
            WorldObject origin,
            List<ThingDefCountClass> paymentItems,
            Faction recipientFaction)
        {
            if (settlement == null || origin == null || settlement.Destroyed) return false;
            if (origin.Tile == settlement.Tile) return false;
            var def = DefDatabase<WorldObjectDef>.GetNamedSilentFail("TSA_WD_Traveler_SettlementGift");
            if (def == null) return false;

            var traveler = (WorldObject_Traveler_SettlementGift)WorldObjectMaker.MakeWorldObject(def);
            traveler.Tile = origin.Tile;
            traveler.SetFaction(Faction.OfPlayer);
            traveler.originObject = origin;
            traveler.targetObject = settlement;
            traveler.mission = TravelerMission.SettlementGift;
            ApplyTradeDropPodLaunch(traveler, origin);
            traveler.travelerStrength = 100f;
            traveler.initialStrength = 100f;
            traveler.recipientFaction = recipientFaction;
            traveler.paymentItems = paymentItems != null
                ? new List<ThingDefCountClass>(paymentItems)
                : new List<ThingDefCountClass>();
            Find.WorldObjects.Add(traveler);
            traveler.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(settlement.Tile, origin));
            if (traveler.Destroyed)
                return false;
            WakeTradeDropPodIfNeeded(traveler);

            Find.World.GetComponent<WorldComponent_SpreadManager>()?.AddLog(new SpreadLogEntry(
                "TSA_WD_Log_SettlementGiftLaunched".Translate(origin.LabelCap, settlement.LabelCap, recipientFaction?.Name ?? "?"),
                origin,
                settlement));
            return true;
        }

        public static bool SpawnDiplomacyNegotiateTraveler(
            Settlement destination,
            WorldObject origin,
            List<ThingDefCountClass> paymentItems,
            Faction negotiatorFaction,
            Faction targetFaction,
            DiplomacyNegotiateAction action,
            float askSilver)
        {
            if (destination == null || origin == null || destination.Destroyed) return false;
            if (origin.Tile == destination.Tile) return false;
            var def = DefDatabase<WorldObjectDef>.GetNamedSilentFail("TSA_WD_Traveler_DiplomacyNegotiate");
            if (def == null) return false;

            var traveler = (WorldObject_Traveler_DiplomacyNegotiate)WorldObjectMaker.MakeWorldObject(def);
            traveler.Tile = origin.Tile;
            traveler.SetFaction(Faction.OfPlayer);
            traveler.originObject = origin;
            traveler.targetObject = destination;
            traveler.mission = TravelerMission.DiplomacyNegotiate;
            ApplyTradeDropPodLaunch(traveler, origin);
            traveler.travelerStrength = 100f;
            traveler.initialStrength = 100f;
            traveler.negotiatorFaction = negotiatorFaction;
            traveler.targetFaction = targetFaction;
            traveler.action = action;
            traveler.desiredKind = DiplomacyNegotiateUtility.DesiredKind(action);
            traveler.askSilver = askSilver;
            traveler.paymentItems = paymentItems != null
                ? new List<ThingDefCountClass>(paymentItems)
                : new List<ThingDefCountClass>();
            Find.WorldObjects.Add(traveler);
            traveler.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(destination.Tile, origin));
            if (traveler.Destroyed)
                return false;
            WakeTradeDropPodIfNeeded(traveler);

            string actionLabel = DiplomacyNegotiateUtility.ActionVerbLabel(action);
            Find.World.GetComponent<WorldComponent_SpreadManager>()?.AddLog(new SpreadLogEntry(
                "TSA_WD_Log_NegotiateLaunched".Translate(
                    origin.LabelCap,
                    negotiatorFaction?.Name ?? "?",
                    actionLabel,
                    targetFaction?.Name ?? "?"),
                origin,
                destination)
            {
                highlightKind = SpreadLogHighlightKind.Diplomacy
            });

            if (WorldDominationMod.settings?.notifyDiplomacyNegotiateStarted ?? false)
            {
                Find.LetterStack.ReceiveLetter(
                    "TSA_WD_Negotiate_StartLetterLabel".Translate(),
                    "TSA_WD_Negotiate_StartLetterText".Translate(
                        negotiatorFaction?.Name ?? "?",
                        actionLabel,
                        targetFaction?.Name ?? "?"),
                    LetterDefOf.NeutralEvent,
                    traveler);
            }

            return true;
        }

        public static bool SpawnSettlementBribeTraveler(
            Settlement settlement,
            WorldObject origin,
            List<ThingDefCountClass> paymentItems,
            Faction targetFaction,
            int ceasefireDays,
            float askSilver)
        {
            if (settlement == null || origin == null || settlement.Destroyed) return false;
            if (origin.Tile == settlement.Tile) return false;
            var def = DefDatabase<WorldObjectDef>.GetNamedSilentFail("TSA_WD_Traveler_SettlementBribe");
            if (def == null) return false;

            var traveler = (WorldObject_Traveler_SettlementBribe)WorldObjectMaker.MakeWorldObject(def);
            traveler.Tile = origin.Tile;
            traveler.SetFaction(Faction.OfPlayer);
            traveler.originObject = origin;
            traveler.targetObject = settlement;
            traveler.mission = TravelerMission.SettlementBribe;
            traveler.bribeKind = WorldObject_Traveler_SettlementBribe.BribeKind.Settlement;
            traveler.ticksPerMove = WorldObject_Traveler.DefaultTicksPerMove;
            traveler.travelerStrength = 100f;
            traveler.initialStrength = 100f;
            traveler.targetFaction = targetFaction;
            traveler.ceasefireDays = Mathf.Max(1, ceasefireDays);
            traveler.askSilver = Mathf.Max(0f, askSilver);
            traveler.paymentItems = paymentItems != null
                ? new List<ThingDefCountClass>(paymentItems)
                : new List<ThingDefCountClass>();
            Find.WorldObjects.Add(traveler);
            traveler.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(settlement.Tile, origin));
            if (traveler.Destroyed)
                return false;

            Find.World.GetComponent<WorldComponent_SpreadManager>()?.AddLog(new SpreadLogEntry(
                "TSA_WD_Log_BribeSettlementLaunched".Translate(origin.LabelCap, settlement.LabelCap, targetFaction?.Name ?? "?"),
                origin,
                settlement));
            return true;
        }

        public static bool SpawnRaidBribeTraveler(
            WorldObject_Traveler raid,
            WorldObject origin,
            List<ThingDefCountClass> paymentItems,
            Faction targetFaction,
            float askSilver)
        {
            if (raid == null || origin == null || raid.Destroyed) return false;
            var def = DefDatabase<WorldObjectDef>.GetNamedSilentFail("TSA_WD_Traveler_SettlementBribe");
            if (def == null) return false;

            var traveler = (WorldObject_Traveler_SettlementBribe)WorldObjectMaker.MakeWorldObject(def);
            traveler.Tile = origin.Tile;
            traveler.SetFaction(Faction.OfPlayer);
            traveler.originObject = origin;
            traveler.targetObject = raid;
            traveler.mission = TravelerMission.RaidBribe;
            traveler.bribeKind = WorldObject_Traveler_SettlementBribe.BribeKind.Raid;
            traveler.ticksPerMove = WorldObject_Traveler.DefaultTicksPerMove;
            traveler.travelerStrength = 100f;
            traveler.initialStrength = 100f;
            traveler.targetFaction = targetFaction;
            traveler.askSilver = Mathf.Max(0f, askSilver);
            traveler.paymentItems = paymentItems != null
                ? new List<ThingDefCountClass>(paymentItems)
                : new List<ThingDefCountClass>();
            Find.WorldObjects.Add(traveler);
            traveler.RefreshRapidResponseInterceptPath(true);
            if (traveler.Destroyed)
                return false;

            Find.World.GetComponent<WorldComponent_SpreadManager>()?.AddLog(new SpreadLogEntry(
                "TSA_WD_Log_BribeRaidLaunched".Translate(origin.LabelCap, raid.LabelCap, targetFaction?.Name ?? "?"),
                origin,
                raid));
            return true;
        }

        private static void ExecuteSettlementGiftArrival(WorldObject_Traveler_SettlementGift gift)
        {
            if (gift == null) return;

            if (!SettlementGiftUtility.IsGiftStillValid(gift, out var failReason))
            {
                SettlementGiftUtility.RefundPayment(gift, failReason);
                return;
            }

            var settlement = (Settlement)gift.targetObject;
            float silverBudget = SettlementBuyUtility.MarketValueOf(gift.paymentItems);
            var itemsCopy = gift.paymentItems != null
                ? new List<ThingDefCountClass>(gift.paymentItems)
                : new List<ThingDefCountClass>();

            gift.completed = true;
            gift.paymentRefunded = true;
            gift.paymentItems?.Clear();

            SettlementGiftUtility.ApplyVanillaGiftGoodwill(settlement, itemsCopy);

            FactionSettlementInvestment.AwardFromSilverBudget(
                settlement.Faction,
                settlement.Tile,
                silverBudget,
                preferFirst: settlement,
                notify: FactionSettlementInvestment.NotifyKind.Gift);

            if (WorldDominationMod.settings?.notifySettlementBuyCompleted ?? WorldDominationSettings.DefNotifySettlementBuyCompleted)
            {
                Find.LetterStack.ReceiveLetter(
                    "TSA_WD_GiftSettlement_CompletedLetterLabel".Translate(),
                    "TSA_WD_GiftSettlement_CompletedLetterText".Translate(
                        settlement.LabelCap,
                        settlement.Faction?.Name ?? "?",
                        silverBudget.ToString("F0")),
                    LetterDefOf.PositiveEvent,
                    settlement);
            }

            Find.World.GetComponent<WorldComponent_SpreadManager>()?.AddLog(new SpreadLogEntry(
                "TSA_WD_Log_SettlementGiftCompleted".Translate(settlement.LabelCap, settlement.Faction?.Name ?? "?"),
                settlement));
        }

        private static void ExecuteSettlementBuyArrival(WorldObject_Traveler_SettlementBuy buy)
        {
            if (buy == null) return;

            if (!SettlementBuyUtility.IsDealStillValid(buy, out var failReason))
            {
                SettlementBuyUtility.RefundPayment(buy, failReason);
                return;
            }

            var settlement = (Settlement)buy.targetObject;
            Faction seller = settlement.Faction;
            string name = settlement.LabelCap;
            int tile = settlement.Tile;
            SettlementTier tier = buy.dealTier;

            float goodsMv = SettlementBuyUtility.MarketValueOf(buy.paymentItems);
            int gwSpent = Mathf.Max(0, buy.pendingGoodwill);
            float silverBudget = goodsMv + gwSpent * SettlementBuyUtility.SilverPerGoodwill;

            // Goodwill was spent at launch; goods travel with the caravan. Consume both on success.
            buy.completed = true;
            buy.paymentRefunded = true;
            buy.paymentItems?.Clear();
            buy.pendingGoodwill = 0;

            // Buying ends any player-ordered road here: refund unbuilt segments as if the order was cancelled.
            CompViralSpread viral = settlement.GetComponent<CompViralSpread>();
            if (viral != null && viral.HasActivePlayerOrderedRoadProject)
                WorldActions_Roads.ClearRoadProject(viral, RoadProjectClearReason.PlayerCancel, seller);

            settlement.Destroy();
            Outpost_EstablishmentRequirements.InvalidateNearbyCountCache();

            FactionSettlementInvestment.AwardFromSilverBudget(
                seller,
                tile,
                silverBudget,
                preferFirst: null,
                notify: FactionSettlementInvestment.NotifyKind.Buy);

            ConquestOpportunityUtility.RegisterSimulatedConquest(tile, name, tier);
            var context = new ConquestOpportunityContext(tile, name, -1, tier, seller, -1, fromSettlementBuy: true);
            Find.WindowStack.Add(new Dialog_OutpostOpportunityChoices(context, allowLeave: false));

            if (WorldDominationMod.settings?.notifySettlementBuyCompleted ?? WorldDominationSettings.DefNotifySettlementBuyCompleted)
            {
                Find.LetterStack.ReceiveLetter(
                    "TSA_WD_BuySettlement_CompleteLetterLabel".Translate(),
                    "TSA_WD_BuySettlement_CompleteLetterText".Translate(name, seller.Name),
                    LetterDefOf.PositiveEvent,
                    new GlobalTargetInfo(tile));
            }

            Find.World.GetComponent<WorldComponent_SpreadManager>()?.AddLog(new SpreadLogEntry(
                "TSA_WD_Log_SettlementBuyCompleted".Translate(name, seller.Name),
                null));
        }

        private static void ExecuteExpansion(WorldObject_Traveler traveler)
        {
            var spreadMgr = Find.World.GetComponent<WorldComponent_SpreadManager>();
            string originLabel = traveler.originObject?.LabelCap ?? traveler.Faction?.Name ?? "?";

            if (Find.WorldObjects.AnySettlementAt(traveler.Tile)
                || Outpost_EstablishmentRequirements.TileHasActiveCamp(traveler.Tile))
            {
                spreadMgr?.AddLog(new SpreadLogEntry(
                    "TSA_WD_Log_Expand_SkippedNoValidTile".Translate(originLabel),
                    traveler.originObject));
                return;
            }

            // Final gate: destination may have become blocked (new outpost/settlement) while the caravan was en route.
            var seth = WorldDominationMod.settings;
            if (!TileFinder.IsValidTileForNewSettlement(traveler.Tile)
                || Outpost_EstablishmentRequirements.IsTileBlockedByMinDistanceCached(traveler.Tile)
                || WorldActions_GrowthExpand.IsTargetSaturated(traveler.Tile, traveler.Faction, seth, spreadMgr))
            {
                spreadMgr?.AddLog(new SpreadLogEntry(
                    "TSA_WD_Log_Expand_SkippedNoValidTile".Translate(originLabel),
                    traveler.originObject));
                return;
            }

            Settlement newS = (Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
            newS.Tile = traveler.Tile;
            newS.SetFaction(traveler.Faction);
            newS.Name = SettlementNameGenerator.GenerateSettlementName(newS);
            Find.WorldObjects.Add(newS);
            Outpost_EstablishmentRequirements.InvalidateNearbyCountCache();

            var comp = newS.GetComponent<CompViralSpread>();
            if (comp != null)
            {
                comp.SetState(SettlementTier.T1);
                comp.strength = traveler.travelerStrength;
                if (comp.strength < 10f) comp.strength = 10f;
                comp.CheckTierUpdate();
            }

            // Always log success when a settlement was placed (comp setup is independent of visibility).
            if (spreadMgr != null)
            {
                var expandOk = new SpreadLogEntry(
                    "TSA_WD_Log_ExpandSuccess".Translate(originLabel, newS.LabelCap),
                    traveler.originObject,
                    newS);
                expandOk.highlightKind = SpreadLogHighlightKind.ExpansionSuccess;
                spreadMgr.AddLog(expandOk);
            }

            TryNotifyNewSettlementFounded(newS);
        }

        private static void TryNotifyNewSettlementFounded(Settlement settlement)
        {
            if (settlement == null || settlement.Tile < 0) return;
            var seth = WorldDominationMod.settings;
            if (seth == null || !seth.notifyNewSettlement) return;
            if (!WD_NotifyProximity.IsWithinPlayerNotificationRadius(settlement.Tile)) return;
            string factionName = settlement.Faction?.Name ?? "Unknown";
            Find.LetterStack.ReceiveLetter(
                "TSA_WD_Letter_NewSettlement_Label".Translate(),
                "TSA_WD_Letter_NewSettlement_Text".Translate(settlement.LabelCap, factionName),
                LetterDefOf.NeutralEvent,
                new GlobalTargetInfo(settlement));
        }

        private static void ExecuteRoadPaving(WorldObject_Traveler traveler, int previousTileId)
        {
            if (traveler.originObject == null) return;

            var originComp = traveler.originObject.GetComponent<CompViralSpread>();
            PlanetLayer layer = traveler.Tile.Layer;

            if (originComp != null && originComp.roadIsClearing)
            {
                ExecuteRoadRemoval(traveler, previousTileId, originComp, layer);
                return;
            }

            RoadDef plannedRoad = WorldActions_Roads.GetRoadDefForActor(traveler.originObject);
            if (plannedRoad == null) return;

            SettlementTier tier = originComp != null ? originComp.selectedRoadTier : SettlementTier.T1;

            int paveFrom = -1;
            int paveTo = -1;
            bool haveCorridorEdge = originComp?.cachedRoadPathTiles != null
                && WorldActions_Roads.TryGetFirstUnfinishedRoadEdge(originComp.cachedRoadPathTiles, tier, out paveFrom, out paveTo);

            // Prefer the planned corridor edge so shortcuts / waypoint junctions cannot leave a permanent gap.
            if (!haveCorridorEdge)
            {
                if (previousTileId == -1) return;
                paveFrom = previousTileId;
                paveTo = traveler.Tile.tileId;
            }

            bool stillNeedsWork = WorldActions_Roads.ShouldUpgradeRoad(
                new PlanetTile(paveFrom, layer),
                new PlanetTile(paveTo, layer),
                plannedRoad
            );

            var manager = Find.World.GetComponent<WorldComponent_SpreadManager>();

            if (stillNeedsWork)
            {
                WorldActions_Roads.ApplyRoadLink(
                    new PlanetTile(paveTo, layer),
                    new PlanetTile(paveFrom, layer),
                    traveler.originObject
                );

                if (manager != null)
                {
                    int remaining = WorldActions_Utils.GetDistance(traveler.Tile, traveler.pather.destTile.tileId, manager);

                    int workTile = traveler.pather.destTile.tileId;
                    manager.AddLog(new SpreadLogEntry(
                        "TSA_WD_Log_Road_Progress".Translate(
                            traveler.originObject.LabelCap,
                            workTile,
                            remaining
                        ),
                        traveler.originObject,
                        workTile
                    ));
                }

                bool playerRoadProject = traveler.originObject is WorldObject_WD_Outpost
                    || (traveler.originObject is Settlement
                        && (originComp?.playerOrderedRoad == true
                            || ColonyWorldBuildUtility.IsPlayerColonyBuildActor(traveler.originObject)));

                if (playerRoadProject)
                {
                    if (traveler.originObject is WorldObject_WD_Outpost
                        || ColonyWorldBuildUtility.IsPlayerColonyBuildActor(traveler.originObject))
                        Messages.Message("TSA_WD_RoadSegmentComplete".Translate(traveler.originObject.LabelCap), MessageTypeDefOf.PositiveEvent);
                    WorldActions_Roads.RefreshOutpostRoadProjectVisualsAfterSegment(traveler.originObject);
                }
            }
            else
            {
                originComp?.AddStrength(traveler.travelerStrength);
                // Still advance worksite if corridor says this edge is done but marker was stale.
                if (haveCorridorEdge
                    && (traveler.originObject is WorldObject_WD_Outpost
                        || (traveler.originObject is Settlement
                            && (originComp?.playerOrderedRoad == true
                                || ColonyWorldBuildUtility.IsPlayerColonyBuildActor(traveler.originObject)))))
                {
                    WorldActions_Roads.RefreshOutpostRoadProjectVisualsAfterSegment(traveler.originObject);
                }
            }
        }

        private static void ExecuteRoadRemoval(WorldObject_Traveler traveler, int previousTileId, CompViralSpread originComp, PlanetLayer layer)
        {
            int from = -1;
            int to = -1;
            bool haveCorridorEdge = originComp.cachedRoadPathTiles != null
                && WorldActions_Roads.TryGetFirstRemovableRoadEdge(originComp.cachedRoadPathTiles, out from, out to);

            if (!haveCorridorEdge)
            {
                if (previousTileId == -1) return;
                from = previousTileId;
                to = traveler.Tile.tileId;
            }

            bool stillHasRoad = WorldActions_Roads.HasRoadLink(from, to);
            bool playerRoadProject = traveler.originObject is WorldObject_WD_Outpost
                || ColonyWorldBuildUtility.IsPlayerColonyBuildActor(traveler.originObject);

            if (stillHasRoad)
            {
                WorldActions_Roads.RemoveRoadLink(
                    new PlanetTile(from, layer),
                    new PlanetTile(to, layer));

                if (playerRoadProject)
                {
                    Messages.Message("TSA_WD_RoadRemovalSegmentComplete".Translate(traveler.originObject.LabelCap), MessageTypeDefOf.PositiveEvent);
                    WorldActions_Roads.RefreshOutpostRoadProjectVisualsAfterSegment(traveler.originObject);
                }
            }
            else
            {
                originComp.AddStrength(traveler.travelerStrength);
                if (haveCorridorEdge && playerRoadProject)
                    WorldActions_Roads.RefreshOutpostRoadProjectVisualsAfterSegment(traveler.originObject);
            }
        }

    }
}