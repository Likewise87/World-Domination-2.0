using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Result of an open-field clash resolved with World Raids math (raw strengths, no settlement defense pool).</summary>
    public struct OpenFieldClashResult
    {
        public bool ok;
        public bool attackerWon;
        public float attBefore;
        public float defBefore;
        public float attAfter;
        public float defAfter;
        /// <summary>Strength that would return home from the attacker side (win remnant or loss retreat). Used for RR refunds.</summary>
        public float attackerSurvivorStrength;
        /// <summary>Strength remaining on the defender side after a held fight; 0 if conquered.</summary>
        public float defenderSurvivorStrength;
        public float ratio;
        public float winChance;
        public float attLossPct;
        public float defLossPct;
        public BattleMarginTier attSeverity;
        public BattleMarginTier defSeverity;
        public string attackerLabel;
        public string defenderLabel;
        public WorldObject attackerObj;
        public WorldObject defenderObj;
    }

    /// <summary>
    /// Open-field traveler/caravan/AT melee clashes: World Raids outcome table as-is, with explicit attacker/defender roles.
    /// </summary>
    public static class OpenFieldClashUtility
    {
        public static bool IsInterceptorMission(TravelerMission mission) =>
            mission == TravelerMission.RapidResponseIntercept;

        /// <summary>
        /// Designate attacker/defender for two travelers.
        /// <paramref name="incoming"/> is the initiator used when neither (or both non-asymmetric) need a fallback.
        /// </summary>
        public static void DesignateTravelerRoles(
            WorldObject_Traveler a,
            WorldObject_Traveler b,
            WorldObject_Traveler incoming,
            out WorldObject_Traveler attacker,
            out WorldObject_Traveler defender)
        {
            attacker = a;
            defender = b;
            if (a == null || b == null) return;

            bool aIntercept = IsInterceptorMission(a.mission);
            bool bIntercept = IsInterceptorMission(b.mission);
            if (aIntercept != bIntercept)
            {
                attacker = aIntercept ? a : b;
                defender = attacker == a ? b : a;
                return;
            }

            bool aRaid = WorldObject_Traveler.IsRaidMission(a.mission);
            bool bRaid = WorldObject_Traveler.IsRaidMission(b.mission);
            if (aRaid != bRaid)
            {
                attacker = aRaid ? a : b;
                defender = attacker == a ? b : a;
                return;
            }

            if (aRaid && bRaid)
            {
                attacker = Rand.Bool ? a : b;
                defender = attacker == a ? b : a;
                return;
            }

            // Neither raid (or both interceptors): incoming / initiating traveler is attacker.
            WorldObject_Traveler init = incoming != null && (incoming == a || incoming == b) ? incoming : a;
            attacker = init;
            defender = init == a ? b : a;
        }

        /// <summary>Traveler vs traveler: role rules, raid math, winner keeps remnant and continues; loser wiped.</summary>
        public static OpenFieldClashResult ResolveTravelerClash(
            WorldObject_Traveler a,
            WorldObject_Traveler b,
            WorldObject_Traveler incoming,
            WorldComponent_SpreadManager manager)
        {
            var empty = default(OpenFieldClashResult);
            if (a == null || b == null || a.Destroyed || b.Destroyed) return empty;

            DesignateTravelerRoles(a, b, incoming, out WorldObject_Traveler attacker, out WorldObject_Traveler defender);

            float attBefore = Mathf.Max(0f, attacker.travelerStrength);
            float defBefore = Mathf.Max(0f, defender.travelerStrength);
            if (attBefore <= 0f || defBefore <= 0f) return empty;

            OpenFieldClashResult result = RollAndFill(attacker, defender, attBefore, defBefore);
            ApplyTravelerOutcome(attacker, defender, result);
            LogTravelerClash(result, attacker, defender, manager);
            return result;
        }

        /// <summary>Traveler (always attacker) vs AT turret (always defender).</summary>
        public static OpenFieldClashResult ResolveTravelerVsAtTurret(
            WorldObject_Traveler traveler,
            WorldObject_AT_Turret turret,
            WorldComponent_SpreadManager manager)
        {
            var empty = default(OpenFieldClashResult);
            if (traveler == null || turret == null || traveler.Destroyed || turret.Destroyed) return empty;

            float attBefore = Mathf.Max(0f, traveler.travelerStrength);
            float defBefore = Mathf.Max(0f, turret.strength);
            if (defBefore <= 0f)
            {
                DestroyAtTurretIfLive(turret, traveler);
                return empty;
            }
            if (attBefore <= 0f) return empty;

            OpenFieldClashResult result = RollAndFill(traveler, turret, attBefore, defBefore);
            ApplyTravelerVsTurretOutcome(traveler, turret, result, manager);
            LogAtTurretClash(result, traveler, turret, manager);
            return result;
        }

        /// <summary>
        /// NPC caravan vs traveler. Interceptor/raid traveler is attacker; otherwise the initiator is attacker.
        /// </summary>
        public static OpenFieldClashResult ResolveNpcCaravanVsTraveler(
            Caravan caravan,
            WorldObject_Traveler traveler,
            bool travelerIsInitiator,
            WorldComponent_SpreadManager manager)
        {
            var empty = default(OpenFieldClashResult);
            if (caravan == null || traveler == null || caravan.Destroyed || traveler.Destroyed) return empty;

            float caravanStr = WorldComponent_SpreadManager.ComputeCaravanMortarStrengthPool(caravan);
            float tStr = Mathf.Max(0f, traveler.travelerStrength);
            if (caravanStr <= 0f || tStr <= 0f) return empty;

            bool travelerAttacks = IsInterceptorMission(traveler.mission)
                || WorldObject_Traveler.IsRaidMission(traveler.mission)
                || travelerIsInitiator;

            WorldObject attackerObj = travelerAttacks ? (WorldObject)traveler : caravan;
            WorldObject defenderObj = travelerAttacks ? (WorldObject)caravan : traveler;
            float attBefore = travelerAttacks ? tStr : caravanStr;
            float defBefore = travelerAttacks ? caravanStr : tStr;

            OpenFieldClashResult result = RollAndFill(attackerObj, defenderObj, attBefore, defBefore);
            ApplyNpcCaravanOutcome(caravan, traveler, travelerAttacks, result);
            LogNpcCaravanClash(result, caravan, traveler, manager);
            return result;
        }

        public static bool SideWon(OpenFieldClashResult result, WorldObject side)
        {
            if (!result.ok || side == null) return false;
            if (side == result.attackerObj) return result.attackerWon;
            if (side == result.defenderObj) return !result.attackerWon;
            return false;
        }

        public static float SurvivorStrengthFor(OpenFieldClashResult result, WorldObject side)
        {
            if (!result.ok || side == null) return 0f;
            if (side == result.attackerObj) return result.attackerSurvivorStrength;
            if (side == result.defenderObj) return result.defenderSurvivorStrength;
            return 0f;
        }

        private static OpenFieldClashResult RollAndFill(WorldObject attacker, WorldObject defender, float attBefore, float defBefore)
        {
            var seth = WorldDominationMod.settings;
            float ratio = attBefore / Mathf.Max(defBefore, 0.0001f);
            RaidResolvedOutcome resolved = RaidCasualtyModel.Resolve(ratio, seth);

            float attSurvivors = attBefore * (1f - resolved.attLossPct);
            float defSurvivors = defBefore * (1f - resolved.defLossPct);

            return new OpenFieldClashResult
            {
                ok = true,
                attackerWon = resolved.attackerWon,
                attBefore = attBefore,
                defBefore = defBefore,
                attAfter = resolved.attackerWon ? attSurvivors : 0f,
                defAfter = resolved.attackerWon ? 0f : defSurvivors,
                attackerSurvivorStrength = attSurvivors,
                defenderSurvivorStrength = resolved.attackerWon ? 0f : defSurvivors,
                ratio = ratio,
                winChance = resolved.winChance,
                attLossPct = resolved.attLossPct,
                defLossPct = resolved.defLossPct,
                attSeverity = resolved.attSeverity,
                defSeverity = resolved.defCoalitionSeverity,
                attackerLabel = attacker?.LabelCap ?? "?",
                defenderLabel = defender?.LabelCap ?? "?",
                attackerObj = attacker,
                defenderObj = defender
            };
        }

        private static void ApplyTravelerOutcome(
            WorldObject_Traveler attacker,
            WorldObject_Traveler defender,
            OpenFieldClashResult result)
        {
            if (result.attackerWon)
            {
                attacker.travelerStrength = result.attAfter;
                SettlementCaravanLootUtility.TrySeizeBeforeDestroy(defender, attacker.Faction);
                WorldActions_Traveler.TryAwardTraderInterceptLoot(attacker, defender);
                WorldActions_Traveler.StampTraderInterceptedIfApplicable(defender);
                if (!defender.Destroyed)
                    defender.Destroy();
                if (attacker.travelerStrength <= 0.01f && !attacker.Destroyed)
                {
                    WorldActions_Traveler.StampTraderInterceptedIfApplicable(attacker);
                    attacker.Destroy();
                }
            }
            else
            {
                defender.travelerStrength = result.defAfter;
                SettlementCaravanLootUtility.TrySeizeBeforeDestroy(attacker, defender.Faction);
                WorldActions_Traveler.TryAwardTraderInterceptLoot(defender, attacker);
                WorldActions_Traveler.StampTraderInterceptedIfApplicable(attacker);
                if (!attacker.Destroyed)
                    attacker.Destroy();
                if (defender.travelerStrength <= 0.01f && !defender.Destroyed)
                {
                    WorldActions_Traveler.StampTraderInterceptedIfApplicable(defender);
                    defender.Destroy();
                }
            }
        }

        private static void ApplyTravelerVsTurretOutcome(
            WorldObject_Traveler traveler,
            WorldObject_AT_Turret turret,
            OpenFieldClashResult result,
            WorldComponent_SpreadManager manager)
        {
            if (result.attackerWon)
            {
                traveler.travelerStrength = result.attAfter;
                DestroyAtTurretIfLive(turret, traveler);
                if (traveler.travelerStrength <= 0.01f && !traveler.Destroyed)
                {
                    WorldActions_Traveler.StampTraderInterceptedIfApplicable(traveler);
                    traveler.Destroy();
                }
            }
            else
            {
                turret.strength = result.defAfter;
                WorldActions_Traveler.StampTraderInterceptedIfApplicable(traveler);
                AtTurretNotifyUtility.NotifyShellOrStrengthHit(manager, turret, traveler, result.attBefore, 0f, wiped: true);
                if (!traveler.Destroyed)
                    traveler.Destroy();
                if (turret.strength <= 0.01f)
                    DestroyAtTurretIfLive(turret, traveler);
            }
        }

        /// <summary>Live turret at 0 strength is not a valid fight; destroy it with the same letter/suppress as a clash win.</summary>
        private static void DestroyAtTurretIfLive(WorldObject_AT_Turret turret, WorldObject attacker)
        {
            if (turret == null || turret.Destroyed) return;
            AtTurretNotifyUtility.NotifyPlayerTurretDestroyed(turret, attacker);
            turret.suppressDestroyedLetter = true;
            turret.Destroy();
        }

        private static void ApplyNpcCaravanOutcome(
            Caravan caravan,
            WorldObject_Traveler traveler,
            bool travelerWasAttacker,
            OpenFieldClashResult result)
        {
            bool travelerWon = travelerWasAttacker ? result.attackerWon : !result.attackerWon;
            if (travelerWon)
            {
                float remnant = travelerWasAttacker ? result.attAfter : result.defAfter;
                traveler.travelerStrength = remnant;
                if (!caravan.Destroyed)
                    caravan.Destroy();
                if (traveler.travelerStrength <= 0.01f && !traveler.Destroyed)
                {
                    WorldActions_Traveler.StampTraderInterceptedIfApplicable(traveler);
                    traveler.Destroy();
                }
            }
            else
            {
                SettlementCaravanLootUtility.TrySeizeBeforeDestroy(traveler, caravan.Faction);
                WorldActions_Traveler.StampTraderInterceptedIfApplicable(traveler);
                if (!traveler.Destroyed)
                    traveler.Destroy();
                // NPC caravans have no vitality pool to chip; leave intact on win.
            }
        }

        private static void LogTravelerClash(
            OpenFieldClashResult result,
            WorldObject_Traveler attacker,
            WorldObject_Traveler defender,
            WorldComponent_SpreadManager manager)
        {
            if (manager == null || !result.ok) return;

            var entry = new SpreadLogEntry("TSA_WD_Log_TravelerClash".Translate(), attacker, defender);
            FillClashEntry(entry, result, attacker, defender);
            manager.AddLog(entry);
        }

        private static void LogNpcCaravanClash(
            OpenFieldClashResult result,
            Caravan caravan,
            WorldObject_Traveler traveler,
            WorldComponent_SpreadManager manager)
        {
            if (manager == null || !result.ok) return;

            var entry = new SpreadLogEntry(
                "TSA_WD_Log_NpcCaravanClash".Translate(
                    caravan.LabelCap,
                    traveler.LabelCap,
                    result.attBefore.ToString("F0"),
                    result.defBefore.ToString("F0")),
                traveler,
                caravan);
            FillClashEntry(entry, result, result.attackerObj, result.defenderObj);
            manager.AddLog(entry);
        }

        private static void LogAtTurretClash(
            OpenFieldClashResult result,
            WorldObject_Traveler traveler,
            WorldObject_AT_Turret turret,
            WorldComponent_SpreadManager manager)
        {
            if (manager == null || !result.ok) return;

            string outcome;
            if (result.attackerWon && !traveler.Destroyed)
                outcome = "TSA_WD_Log_AT_TurretClash_TravelerWon".Translate(
                    traveler.LabelCap, turret.LabelCap, result.attAfter.ToString("F0"));
            else if (!result.attackerWon && !turret.Destroyed)
                outcome = "TSA_WD_Log_AT_TurretClash_TurretWon".Translate(
                    turret.LabelCap, traveler.LabelCap, result.defAfter.ToString("F0"));
            else
                outcome = "TSA_WD_Log_AT_TurretClash_Mutual".Translate(traveler.LabelCap, turret.LabelCap);

            var entry = new SpreadLogEntry(outcome, traveler, turret);
            FillClashEntry(entry, result, traveler, turret);
            manager.AddLog(entry);
        }

        private static void FillClashEntry(
            SpreadLogEntry entry,
            OpenFieldClashResult result,
            WorldObject attacker,
            WorldObject defender)
        {
            entry.isCaravanClash = true;
            entry.attStr = result.attBefore;
            entry.defStr = result.defBefore;
            entry.ratio = result.ratio;
            entry.winChance = result.winChance;
            entry.victory = result.attackerWon;
            entry.attLossPct = result.attLossPct;
            entry.defLossPct = result.defLossPct;
            entry.attSeverityTier = result.attSeverity;
            entry.defCoalitionSeverityTier = result.defSeverity;
            entry.marginTier = result.attSeverity;
            entry.labelA = result.attackerLabel;
            entry.labelB = result.defenderLabel;

            string attMission = attacker is WorldObject_Traveler at ? at.mission.ToString() : "AT";
            string defMission = defender is WorldObject_Traveler dt ? dt.mission.ToString()
                : defender is WorldObject_AT_Turret ? "ATTurret"
                : defender is Caravan ? "Caravan" : "?";

            entry.attDetails = new System.Collections.Generic.List<string>
            {
                "TSA_WD_Log_Clash_Detail".Translate(result.attackerLabel, result.attBefore.ToString("F0"), attMission)
            };
            entry.defDetails = new System.Collections.Generic.List<string>
            {
                "TSA_WD_Log_Clash_Detail".Translate(result.defenderLabel, result.defBefore.ToString("F0"), defMission)
            };
        }
    }
}
