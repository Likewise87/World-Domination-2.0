using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Feature C: settlements (and, via <see cref="WD_Outpost_RapidResponse"/>, player outposts) with ambush
    /// capability launch a Rapid-Response-style interceptor at a passing hostile WD traveler (trader/gift/bribe
    /// caravan, or a raid not already targeting this settlement) or a real vanilla player <see cref="Caravan"/>.
    /// Reuses <see cref="WorldComponent_SettlementWatchIndex"/> for O(1) tile lookups and
    /// <see cref="WorldActions_Traveler.SpawnRapidResponseInterceptTraveler"/> for the actual dispatch — no new
    /// per-tick scanning is added beyond the existing 4-tick caravan tile-change poll in <see cref="WD_SameTileTravelerClash"/>.
    /// </summary>
    public static class SettlementAmbushUtility
    {
        /// <summary>Max chase distance = ambush watch range × this, measured from the origin settlement.</summary>
        public const float PursuitRangeMult = 1.6f;
        /// <summary>Per-target anti-dogpile (90 real-time seconds at 1x).</summary>
        public const int TargetCooldownTicks = 5400;
        /// <summary>Per-origin launch cooldown (3 real-time minutes at 1x).</summary>
        public const int OriginCooldownTicks = 10800;

        /// <summary>Feature C's mid/late escalation gate, bypassable via <see cref="WorldDominationSettings.opportunityFeaturesIgnoreEscalationGate"/> so a player can opt into ambushes from the start of a game.</summary>
        private static bool PassesEscalationGate(WorldComponent_SpreadManager manager, WorldDominationSettings seth) =>
            (seth != null && seth.opportunityFeaturesIgnoreEscalationGate) || WdEscalation.IsMidOrLate(manager);

        /// <summary>WD-traveler half: called from <see cref="WD_PathFollower.PatherTick"/>'s tile-exit block.</summary>
        public static void TryCheckAmbush(WorldObject_Traveler traveler, int tileId)
        {
            var seth = WorldDominationMod.settings;
            if (seth == null || !seth.experimentalSettlementAmbush) return;
            if (traveler == null || traveler.Destroyed || traveler.Faction == null) return;
            if (traveler.isTurretDetour) return;
            if (!IsAmbushableMission(traveler.mission)) return;

            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (!PassesEscalationGate(manager, seth)) return;

            var watchIndex = WorldComponent_SettlementWatchIndex.Get();
            if (watchIndex == null) return;
            if (watchIndex.IsAmbushConcurrentCapReached(seth.settlementAmbushMaxConcurrent)) return;
            if (watchIndex.IsUnderAmbushTargetCooldown(traveler)) return;

            List<WorldObject> watchers = watchIndex.GetWatchers(tileId, WatchCapability.Ambush);
            if (watchers.Count == 0) return;

            WorldObject settlement = FindFirstEligibleAmbusher(watchers, traveler.Faction, traveler.targetObject, traveler.originObject);
            if (settlement == null) return;

            // Coin flip before any strength math, per Feature C ordering.
            if (Rand.Value > seth.settlementAmbushChancePct) return;

            TryDispatch(settlement, traveler, traveler.travelerStrength, watchIndex, seth, manager);
        }

        /// <summary>Real-Caravan half: called from <see cref="WD_SameTileTravelerClash.TickCaravanClashDetection"/> on a detected tile change.</summary>
        public static void TryCheckAmbushForCaravan(Caravan caravan, int tileId)
        {
            var seth = WorldDominationMod.settings;
            if (seth == null || !seth.experimentalSettlementAmbush) return;
            if (caravan == null || caravan.Destroyed || caravan.Faction == null) return;

            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (!PassesEscalationGate(manager, seth)) return;

            var watchIndex = WorldComponent_SettlementWatchIndex.Get();
            if (watchIndex == null) return;
            if (watchIndex.IsAmbushConcurrentCapReached(seth.settlementAmbushMaxConcurrent)) return;
            if (watchIndex.IsUnderAmbushTargetCooldown(caravan)) return;

            List<WorldObject> watchers = watchIndex.GetWatchers(tileId, WatchCapability.Ambush);
            if (watchers.Count == 0) return;

            WorldObject settlement = FindFirstEligibleAmbusher(watchers, caravan.Faction, null, null);
            if (settlement == null) return;

            if (Rand.Value > seth.settlementAmbushChancePct) return;

            float caravanStrength = WorldComponent_SpreadManager.ComputeCaravanMortarStrengthPool(caravan);
            TryDispatch(settlement, caravan, caravanStrength, watchIndex, seth, manager);
        }

        private static bool IsAmbushableMission(TravelerMission mission) =>
            mission == TravelerMission.Trader
            || mission == TravelerMission.SettlementGift
            || mission == TravelerMission.SettlementBribe
            || WorldObject_Traveler.IsRaidMission(mission);

        private static WorldObject FindFirstEligibleAmbusher(
            List<WorldObject> watchers, Faction targetFaction, WorldObject targetDestination, WorldObject targetOrigin)
        {
            for (int i = 0; i < watchers.Count; i++)
            {
                WorldObject settlement = watchers[i];
                if (settlement == null || settlement.Destroyed) continue;
                if (settlement == targetDestination || settlement == targetOrigin) continue;
                if (settlement.Faction == null) continue;
                if (!WorldActions_Utils.SafeHostileTo(settlement.Faction, targetFaction)) continue;
                var originComp = settlement.GetComponent<CompViralSpread>();
                if (originComp == null) continue;
                SettlementTier minTier = WorldDominationMod.settings?.settlementAmbushMinTier
                    ?? WorldDominationSettings.DefSettlementAmbushMinTier;
                if (originComp.tier < minTier) continue;
                if (originComp.IsAmbushOnCooldown) continue;
                return settlement;
            }
            return null;
        }

        private static void TryDispatch(
            WorldObject settlement, WorldObject target, float targetStrengthEstimate,
            WorldComponent_SettlementWatchIndex watchIndex, WorldDominationSettings seth, WorldComponent_SpreadManager manager)
        {
            var comp = settlement.GetComponent<CompViralSpread>();
            if (comp == null) return;
            float available = WorldActions_Utils.GetAvailableRaidStrength(comp, seth);
            if (available <= 0f) return;

            float minRatio = seth.settlementAmbushMinStrengthRatio;
            if (minRatio > 0f && targetStrengthEstimate > 0f && available / targetStrengthEstimate < minRatio) return;

            float strength = RapidResponseUtility.CapSentStrength(available, targetStrengthEstimate, seth.settlementAmbushMaxStrengthRatio);
            if (strength <= 0f) return;

            comp.strength -= strength;
            comp.CheckTierUpdate(false);
            WorldObject_Traveler response = WorldActions_Traveler.SpawnRapidResponseInterceptTraveler(settlement, target, strength);
            if (response == null)
            {
                comp.AddStrengthNoTierUpgrade(strength);
                return;
            }

            response.isSettlementAmbushSally = true;
            watchIndex.NotifyAmbushSallySpawned();
            comp.ambushCooldownTick = Find.TickManager.TicksGame + OriginCooldownTicks;
            watchIndex.StampAmbushTarget(target);
            manager?.AddLog(new SpreadLogEntry(
                "TSA_WD_Log_SettlementAmbush".Translate(settlement.LabelCap, DescribeTargetKind(target), target.LabelCap),
                settlement, target));
        }

        private static string DescribeTargetKind(WorldObject target)
        {
            if (target is Caravan) return "TSA_WD_TargetKind_Caravan".Translate().ToString();
            if (target is WorldObject_Traveler traveler)
            {
                switch (traveler.mission)
                {
                    case TravelerMission.Trader: return "TSA_WD_TargetKind_TraderCaravan".Translate().ToString();
                    case TravelerMission.SettlementGift: return "TSA_WD_TargetKind_GiftCaravan".Translate().ToString();
                    case TravelerMission.SettlementBribe: return "TSA_WD_TargetKind_BribeCaravan".Translate().ToString();
                    case TravelerMission.Raid:
                    case TravelerMission.RaidDropPod: return "TSA_WD_TargetKind_Raid".Translate().ToString();
                }
            }
            return target.LabelCap.ToString();
        }
    }
}
