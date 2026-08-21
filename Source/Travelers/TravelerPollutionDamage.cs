using RimWorld;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Flat strength damage when a ground traveler leaves a polluted world tile.</summary>
    public static class TravelerPollutionDamage
    {
        /// <summary>Master + waster + per-type + player gates for strength damage (B) and cancel.</summary>
        public static bool TakesPollutionDamage(WorldObject_Traveler traveler)
        {
            if (traveler == null || traveler.Destroyed) return false;
            var s = WorldDominationMod.settings;
            if (s == null || !s.travelerPollutionDamageEnabled) return false;
            if (PollutionImmunity.IsImmune(traveler)) return false;
            return MissionTakesPollutionDamage(traveler.mission, traveler.Faction, s, traveler);
        }

        /// <summary>
        /// Path cost (A) only when pollution can actually hurt this traveler
        /// (<see cref="TakesPollutionDamage"/>) and the path-cost setting is on.
        /// </summary>
        public static bool UsesPollutionPathCost(WorldObject_Traveler traveler)
        {
            var s = WorldDominationMod.settings;
            if (s == null || !s.pollutionPathCostEnabled) return false;
            return TakesPollutionDamage(traveler);
        }

        public static bool MissionTakesPollutionDamage(
            TravelerMission mission,
            Faction faction,
            WorldDominationSettings s,
            WorldObject_Traveler traveler = null)
        {
            if (s == null || !s.travelerPollutionDamageEnabled) return false;
            if (PollutionImmunity.IsImmune(faction)) return false;

            if (mission == TravelerMission.Decontamination
                || mission == TravelerMission.MortarStrike
                || mission == TravelerMission.AntiAirStrike
                || mission == TravelerMission.RapidResponseDropPod
                || mission == TravelerMission.RaidDropPod)
                return false;
            if (traveler is WorldObject_Traveler_Outpost_Delivery delivery && delivery.deliveryViaDropPod)
                return false;
            if (traveler != null && WD_PathFollower.IsBallisticWorldFlight(traveler))
                return false;

            if (faction != null && faction.IsPlayer && !s.pollutionDamagePlayerTravelers)
                return false;

            switch (mission)
            {
                case TravelerMission.Raid:
                case TravelerMission.RapidResponseIntercept:
                case TravelerMission.DebugRaidTransit:
                    return s.pollutionDamageRaiders;
                case TravelerMission.Expansion:
                    return s.pollutionDamageExpansion;
                case TravelerMission.RoadBuilding:
                case TravelerMission.RoadBlock:
                case TravelerMission.SpikeTrap:
                case TravelerMission.NpcFortify:
                case TravelerMission.NpcAtTurret:
                case TravelerMission.AtTurret:
                    return s.pollutionDamageConstruction;
                case TravelerMission.Trader:
                case TravelerMission.OutpostDelivery:
                case TravelerMission.OutpostUpgrade:
                case TravelerMission.SettlementBuy:
                case TravelerMission.SettlementGift:
                case TravelerMission.SettlementBribe:
                case TravelerMission.RaidBribe:
                    return s.pollutionDamageTraders;
                default:
                    return false;
            }
        }

        public static void ApplyOnTileExit(int leftTileId, WorldObject_Traveler traveler)
        {
            if (traveler == null || traveler.Destroyed || leftTileId < 0) return;
            if (!TakesPollutionDamage(traveler)) return;

            var s = WorldDominationMod.settings;
            if (s == null) return;

            float pollution01 = WorldTileProductivity.GetTilePollution01(leftTileId);
            float damage = s.GetPollutionExitDamage(pollution01);
            if (damage <= 0f) return;

            traveler.travelerStrength = Mathf.Max(0f, traveler.travelerStrength - damage);

            TrySendPlayerWarning(traveler, damage);

            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            string damageText = "TSA_WD_Log_Pollution_DamagedTraveler".Translate(
                traveler.LabelCap,
                damage.ToString("F0"),
                Mathf.RoundToInt(pollution01 * 100f));
            // Actor A = caravan/traveler; no Actor B.
            manager?.AddLog(new SpreadLogEntry(damageText, traveler));
            Log.Message($"[WD] Pollution exit dmg tile={leftTileId} traveler={traveler.LabelCap} dmg={damage:F0} p={pollution01:F2}");

            if (traveler.travelerStrength <= 0.01f && !traveler.Destroyed)
            {
                string destroyText = "TSA_WD_Log_Pollution_DestroyedTraveler".Translate(traveler.LabelCap);
                manager?.AddLog(new SpreadLogEntry(destroyText, traveler));
                Log.Message($"[WD] Pollution destroyed traveler={traveler.LabelCap} tile={leftTileId}");
                // Empty reason: we already logged with Actor A only (AbortTraveler would pair origin as B).
                TravelerEndpointUtility.AbortTraveler(traveler, null, manager);
            }
        }

        private static void TrySendPlayerWarning(WorldObject_Traveler traveler, float damage)
        {
            if (traveler.Faction == null || !traveler.Faction.IsPlayer) return;
            if (traveler.pollutionDamageWarned) return;

            traveler.pollutionDamageWarned = true;

            var s = WorldDominationMod.settings;
            if (s == null || !s.notifyTravelerPollutionDamage) return;

            Find.LetterStack.ReceiveLetter(
                "TSA_WD_Letter_PollutionDamage_Label".Translate(),
                "TSA_WD_Letter_PollutionDamage_Text".Translate(
                    traveler.LabelCap,
                    damage.ToString("F0")),
                LetterDefOf.NegativeEvent,
                traveler);
        }
    }
}
