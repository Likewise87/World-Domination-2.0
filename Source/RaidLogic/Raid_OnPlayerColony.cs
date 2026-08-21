using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI.Group;
using UnityEngine;

namespace TSA_WorldDomination
{
    public static class Raid_OnPlayerColony
    {
        /// <summary>Set to true only while we are invoking the raid incident (so Patch_BlockStoryTellerRaids can allow it when blockStorytellerRaidsOnlyWD is on).</summary>
        public static bool IsWorldDominationRaid;

        /// <summary>True while a traveler interception raid runs; threat scaling and FRD must not alter these points/spawns.</summary>
        public static bool IsCaravanClashInterception;

        /// <summary>
        /// World raid targeting window: while active, <see cref="WorldActions_Raid.AttemptRaid"/> skips this settlement.
        /// Uses <see cref="WorldDominationSettings.cooldownPlayerRaidDays"/> (Raid Point Multiplier: "Player Raid Cooldown"), same as new-colony shield in <see cref="CompViralSpread"/>.
        /// Does not require a loaded colony map (must run before any <c>target.Map == null</c> early exit).
        /// </summary>
        public static int ApplyRaidDefenseCooldownToPlayerSettlement(Settlement settlement)
        {
            int reservedUntilTick = Raid_DefenseCooldownReservations.ApplyRaidDefenseCooldownReservation(settlement);
            if (reservedUntilTick < 0 && settlement != null)
                Log.Warning("[TSA WD] Player settlement \"" + settlement.Label + "\" has no CompViralSpread; raid defense cooldown not applied. Ensure Settlement comps patch is loaded.");
            return reservedUntilTick;
        }

        public static void ReleaseRaidDefenseCooldownReservation(Settlement settlement, int reservedUntilTick)
        {
            Raid_DefenseCooldownReservations.ReleaseRaidDefenseCooldownReservation(settlement, reservedUntilTick);
        }

        public static void HandleRaidOnPlayer(
            Settlement attacker,
            Settlement target,
            List<WorldObject> attList,
            float attAgg,
            List<string> attDetails,
            WorldComponent_SpreadManager manager,
            WorldObject_Traveler traveler = null)
        {
            if (attacker?.Faction != null && target?.Faction != null
                && !WorldActions_Utils.SafeHostileTo(attacker.Faction, target.Faction))
                return;

            ApplyRaidDefenseCooldownToPlayerSettlement(target);

            Map map = target.Map;
            if (map == null) return;
            if (attacker == null) return;

            float raidPoints = RaidPointsHelper.ClampRaidPointsToStorytellerBand(attAgg, map);
            LogColonyRaidPoints(attacker, target, map, attAgg, raidPoints);
            ExecuteWdColonyRaidIncident(attacker, target, map, attAgg, raidPoints, manager, traveler);
        }

        /// <summary>Debug helper: execute a WD colony raid with fixed points through the same pipeline/flags as real WD raids.</summary>
        public static bool TriggerDebugRaidOnPlayer(Settlement attacker, Settlement target, float fixedRaidPoints, WorldComponent_SpreadManager manager)
        {
            ApplyRaidDefenseCooldownToPlayerSettlement(target);
            Map map = target?.Map;
            if (map == null || attacker?.Faction == null) return false;
            float raidPoints = fixedRaidPoints < 1f ? 1f : fixedRaidPoints;
            if (Prefs.DevMode)
            {
                Log.Message(
                    "[TSA WD] Debug colony raid points (" + (attacker?.LabelCap ?? "?") + " -> " + (target?.LabelCap ?? "?") + "):" + "\n"
                    + "  Fixed debug raid points requested: " + raidPoints.ToString("F0"));
            }
            return ExecuteWdColonyRaidIncident(attacker, target, map, raidPoints, raidPoints, manager, null);
        }

        private static bool ExecuteWdColonyRaidIncident(
            Settlement attacker,
            Settlement target,
            Map map,
            float attackerStrengthForLog,
            float raidPoints,
            WorldComponent_SpreadManager manager,
            WorldObject_Traveler traveler)
        {
            IncidentParms parms = new IncidentParms();
            parms.target = map;
            parms.faction = attacker.Faction;
            // Keep this scripted so storyteller-blocking logic can still allow WD-triggered incidents.
            parms.forced = true;
            parms.points = raidPoints;

            parms.customLetterLabel = "TSA_WD_Letter_RaidPlayer_Colony_Label".Translate(target.LabelCap, attacker.Faction.Name);
            parms.customLetterText = "TSA_WD_Letter_RaidPlayer_Colony_Text".Translate(attacker.Label, target.LabelCap);

            bool dropPod = traveler != null && traveler.mission == TravelerMission.RaidDropPod;
            bool forcedSiege = false;
            if (dropPod)
            {
                parms.raidArrivalMode = Rand.Bool ? PawnsArrivalModeDefOf.CenterDrop : PawnsArrivalModeDefOf.EdgeDrop;
            }
            else
            {
                // Pin walk-in + ImmediateAttack (same as outpost defense / reinforcements). Leaving strategy
                // null lets ForceRaidDirection's "convert StageThenAttack" option also rewrite nulls to
                // ImmediateAttackSmart, which can then fail pawn generation for some factions.
                parms.raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn;
                parms.raidStrategy = RaidStrategyDefOf.ImmediateAttack;

                var seth = WorldDominationMod.settings;
                var tier = attacker.GetComponent<CompViralSpread>()?.tier;
                float siegeChance = seth != null ? seth.colonySiegeRaidChance : WorldDominationSettings.DefColonySiegeRaidChance;
                if ((tier == SettlementTier.T3 || tier == SettlementTier.T4) && Rand.Chance(Mathf.Clamp01(siegeChance)))
                {
                    RaidStrategyDef siege = DefDatabase<RaidStrategyDef>.GetNamedSilentFail("Siege");
                    if (siege != null)
                    {
                        parms.raidStrategy = siege;
                        forcedSiege = true;
                    }
                }
            }

            bool ok = false;
            try
            {
                IsWorldDominationRaid = true;
                ok = IncidentDefOf.RaidEnemy.Worker.TryExecute(parms);
                if (!ok && forcedSiege)
                {
                    ResetRaidParmsForRetry(parms, raidPoints, preferDropPod: dropPod);
                    ok = IncidentDefOf.RaidEnemy.Worker.TryExecute(parms);
                }
                if (!ok)
                {
                    // Clear FRD-forced spawnCenter / age restriction and retry a plain ImmediateAttack walk-in.
                    ResetRaidParmsForRetry(parms, raidPoints, preferDropPod: false);
                    ok = IncidentDefOf.RaidEnemy.Worker.TryExecute(parms);
                }
                if (!ok)
                {
                    ok = TryManualColonyRaidSpawn(map, attacker.Faction, raidPoints, parms.customLetterLabel, parms.customLetterText);
                    if (Prefs.DevMode)
                    {
                        Log.Warning("[TSA WD] WD colony raid incident failed; manual spawn fallback "
                            + (ok ? "succeeded" : "also failed")
                            + " faction=" + (attacker.Faction?.Name ?? "?")
                            + " points=" + raidPoints.ToString("F0")
                            + " spawnCenterWas=" + parms.spawnCenter);
                    }
                }
                if (ok)
                {
                    SpreadLogEntry entry = new SpreadLogEntry("TSA_WD_Log_Raid_PlayerAttack".Translate(), attacker, target);
                    entry.isRaid = true;
                    entry.isAttempt = false;
                    entry.victory = true;
                    entry.attStr = attackerStrengthForLog;
                    entry.defStr = raidPoints;
                    manager?.AddLog(entry);
                }
            }
            finally
            {
                IsWorldDominationRaid = false;
            }
            return ok;
        }

        /// <summary>
        /// Clears arrival/strategy/spawn steering so a retry is not stuck on a bad FRD cell or invalid strategy.
        /// </summary>
        private static void ResetRaidParmsForRetry(IncidentParms parms, float raidPoints, bool preferDropPod)
        {
            parms.points = raidPoints;
            parms.spawnCenter = IntVec3.Invalid;
            parms.raidAgeRestriction = null;
            parms.pawnKind = null;
            parms.pawnCount = 0;
            parms.raidStrategy = RaidStrategyDefOf.ImmediateAttack;
            if (preferDropPod)
                parms.raidArrivalMode = PawnsArrivalModeDefOf.EdgeDrop;
            else
                parms.raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn;
        }

        /// <summary>
        /// Last-resort spawn when <see cref="IncidentWorker_RaidEnemy.TryExecuteWorker"/> returns false
        /// (empty pawn group / bad spawn center). Mirrors reinforcement fallback.
        /// </summary>
        private static bool TryManualColonyRaidSpawn(Map map, Faction faction, float points, string letterLabel, string letterText)
        {
            if (map == null || faction == null) return false;

            PawnGroupMakerParms pgmParms = new PawnGroupMakerParms
            {
                groupKind = PawnGroupKindDefOf.Combat,
                points = Mathf.Max(points, faction.def.MinPointsToGeneratePawnGroup(PawnGroupKindDefOf.Combat) * 1.05f),
                faction = faction
            };
            List<Pawn> pawns = PawnGroupMakerUtility.GeneratePawns(pgmParms).ToList();
            if (pawns.Count == 0) return false;

            if (!CellFinder.TryFindRandomEdgeCellWith(c => c.Standable(map) && !c.Fogged(map), map, CellFinder.EdgeRoadChance_Hostile, out IntVec3 spawnCell))
            {
                if (!CellFinder.TryFindRandomEdgeCellWith(c => c.Standable(map), map, CellFinder.EdgeRoadChance_Hostile, out spawnCell))
                    return false;
            }

            foreach (Pawn p in pawns)
                GenSpawn.Spawn(p, spawnCell, map);
            LordMaker.MakeNewLord(faction, new LordJob_AssaultColony(faction), map, pawns);

            if (!letterLabel.NullOrEmpty() || !letterText.NullOrEmpty())
            {
                Find.LetterStack.ReceiveLetter(
                    letterLabel.NullOrEmpty() ? "Raid".Translate() : (TaggedString)letterLabel,
                    letterText.NullOrEmpty() ? faction.Name : (TaggedString)letterText,
                    LetterDefOf.ThreatBig,
                    new TargetInfo(spawnCell, map));
            }
            return true;
        }

        /// <summary>
        /// Mirrors <c>WD_CaravanClashUtility.LogInterceptionRaidPoints</c> so dev logs for colony
        /// raids and caravan interceptions share the same structure: attacker aggregate strength
        /// → storyteller band (floor/ceiling from min/max fractions) → clamp verdict → final
        /// raid points actually fed to the incident. Unlike interception, no travel-efficiency
        /// step is printed — by the time we reach this call <paramref name="attAgg"/> is already
        /// <c>traveler.travelerStrength</c> at arrival (post travel-decay, set in
        /// <see cref="Raid_Simulated.ExecuteTravelerRaid"/>).
        /// </summary>
        private static void LogColonyRaidPoints(Settlement attacker, Settlement target, Map map, float attAgg, float points)
        {
            if (WorldDominationMod.settings == null) return;

            string attackerLabel = attacker != null ? attacker.LabelCap.ToString() : "?";
            string targetLabel = target != null ? target.LabelCap.ToString() : "?";

            if (!RaidPointsHelper.WdRaidPointsStorytellerBandClampActive())
            {
                Log.Message(
                    $"[TSA WD] Colony raid points ({attackerLabel} → {targetLabel}):" + "\n"
                    + $"  Attacker aggregate strength (arrival, post-decay): {attAgg:F0}" + "\n"
                    + "  Always use Strength as Raid points is ON: storyteller floor/ceiling are not applied." + "\n"
                    + $"  Raid points used for incident: {points:F0}");
                return;
            }

            RaidPointsHelper.GetWdRaidPointClampBounds(
                map,
                out Map baselineMap,
                out float baseline,
                out float floor,
                out float ceiling,
                out float minFrac,
                out float maxFrac);

            string baselineWhere = baselineMap != null ? $"map tile {baselineMap.Tile}" : "unknown map";
            if (baselineMap != null && baselineMap != map)
                baselineWhere += $" (WD uses this player-home baseline instead of target map tile {map.Tile})";

            string verdict;
            if (attAgg < floor - 0.01f)
                verdict = "Clamped to floor (attacker aggregate was below the floor).";
            else if (attAgg > ceiling + 0.01f)
                verdict = "Clamped to ceiling (attacker aggregate was above the ceiling).";
            else
                verdict = "No clamping needed. Using attacker aggregate strength as raid points.";

            Log.Message(
                $"[TSA WD] Colony raid points ({attackerLabel} → {targetLabel}):" + "\n"
                + $"  Attacker aggregate strength (arrival, post-decay): {attAgg:F0}" + "\n"
                + $"  Storyteller threat baseline: {baseline:F0} ({baselineWhere})" + "\n"
                + $"  Floor = baseline × {minFrac:0.###} = {floor:F0}" + "\n"
                + $"  Ceiling = baseline × {maxFrac:0.###} = {ceiling:F0}" + "\n"
                + $"  {verdict}" + "\n"
                + $"  Raid points used for incident: {points:F0}");
        }
    }
}