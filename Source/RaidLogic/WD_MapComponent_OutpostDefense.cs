using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public class WD_MapComponent_OutpostDefense : MapComponent
    {
        private WorldObjectDef travelerDef;
        private Faction enemyFaction;
        private float travelerStrength;
        private float initialStrength;
        private int spawnTick;
        private TravelerMission savedMission = TravelerMission.Raid;
        private RaidOrderOutcome raidOrderOutcome = RaidOrderOutcome.PlayerOutpostConquestMenu;
        private int alliedRaidOrderGoodwillPaid;
        private bool alliedRaidOrderGoodwillRefunded;
        private WorldObject savedOrigin;
        private WorldObject_WD_Outpost outpost;
        private List<WorldObject> raidAttackerList = new List<WorldObject>();
        private List<string> raidAttackerDetails = new List<string>();
        private List<RaidForceLogRow> raidAttackerForceRows = new List<RaidForceLogRow>();
        private List<RaidForceLogRow> raidDefenderForceRows = new List<RaidForceLogRow>();
        private List<WorldObject> contributionKeys = new List<WorldObject>();
        private List<float> contributionValues = new List<float>();
        private List<Pawn> borrowedPawns = new List<Pawn>();
        private List<Pawn> borrowedStoredTransportPawns = new List<Pawn>();
        private List<Pawn> borrowedMechanoids = new List<Pawn>();
        private bool encounterActive;
        private bool resolved;
        private bool mapRemovalQueued;
        private int startTick = -1;
        private bool raidLaunched;
        private int raidLaunchedTick = -1;
        /// <summary>True once living hostile pawns were observed (not merely inbound drop pods).</summary>
        private bool raidThreatSeen;
        /// <summary>True once inbound drop pods / skyfallers were observed this raid.</summary>
        private bool raidInboundSeen;
        private int raidArrivalTick = -1;
        private float raidArrivalRealtime = -1f;
        private float pendingRaidPoints;
        private int postLoadGraceUntilTick = -1;

        public WD_MapComponent_OutpostDefense(Map map) : base(map) { }

        public bool IsActiveEncounterFor(WorldObject_WD_Outpost target)
            => encounterActive && !resolved && outpost != null && outpost == target;

        public static bool HasActiveEncounterFor(WorldObject_WD_Outpost target)
            => FindActiveMapFor(target) != null;

        /// <summary>Active temporary defense map for this outpost, or null if none.</summary>
        public static Map FindActiveMapFor(WorldObject_WD_Outpost target)
        {
            if (target == null || target.Destroyed)
                return null;

            var maps = Current.Game?.Maps;
            if (maps == null)
                return null;

            for (int i = 0; i < maps.Count; i++)
            {
                WD_MapComponent_OutpostDefense tracker = maps[i].GetComponent<WD_MapComponent_OutpostDefense>();
                if (tracker != null && tracker.IsActiveEncounterFor(target))
                    return maps[i];
            }

            return null;
        }

        private bool IsWithinPostLoadGrace()
            => postLoadGraceUntilTick >= 0 && Find.TickManager.TicksGame < postLoadGraceUntilTick;

        public void BeginEncounter(WorldObject_Traveler traveler, WorldObject_WD_Outpost targetOutpost, List<Pawn> defenders, List<Pawn> storedTransportPawns, List<Pawn> mechanoidPawns = null)
        {
            travelerDef = traveler.def;
            enemyFaction = traveler.Faction;
            travelerStrength = traveler.travelerStrength;
            initialStrength = traveler.initialStrength > 0f ? traveler.initialStrength : traveler.travelerStrength;
            spawnTick = traveler.spawnTick;
            savedMission = traveler.mission;
            raidOrderOutcome = traveler.raidOrderOutcome;
            alliedRaidOrderGoodwillPaid = traveler.alliedRaidOrderGoodwillPaid;
            alliedRaidOrderGoodwillRefunded = traveler.alliedRaidOrderGoodwillRefunded;
            savedOrigin = traveler.originObject;
            outpost = targetOutpost;

            raidAttackerList = traveler.raidAttackerList != null ? new List<WorldObject>(traveler.raidAttackerList) : new List<WorldObject>();
            raidAttackerDetails = traveler.raidAttackerDetails != null ? new List<string>(traveler.raidAttackerDetails) : new List<string>();
            raidAttackerForceRows = RaidForceLogRow.CloneList(traveler.raidAttackerForceRows);
            raidDefenderForceRows = RaidForceLogRow.CloneList(traveler.raidDefenderForceRows);

            contributionKeys = new List<WorldObject>();
            contributionValues = new List<float>();
            if (traveler.contributionFactors != null)
            {
                foreach (var kv in traveler.contributionFactors)
                {
                    contributionKeys.Add(kv.Key);
                    contributionValues.Add(kv.Value);
                }
            }

            borrowedPawns = defenders != null ? new List<Pawn>(defenders) : new List<Pawn>();
            borrowedStoredTransportPawns = storedTransportPawns != null ? new List<Pawn>(storedTransportPawns) : new List<Pawn>();
            borrowedMechanoids = mechanoidPawns != null ? new List<Pawn>(mechanoidPawns) : new List<Pawn>();
            encounterActive = true;
            resolved = false;
            raidLaunched = false;
            raidLaunchedTick = -1;
            raidThreatSeen = false;
            raidInboundSeen = false;
            raidArrivalTick = -1;
            raidArrivalRealtime = -1f;
            pendingRaidPoints = 0f;
            startTick = Find.TickManager.TicksGame;
        }

        public void ScheduleRaidArrival(float raidPoints, int delayTicks)
        {
            pendingRaidPoints = raidPoints;
            raidArrivalTick = Find.TickManager.TicksGame + System.Math.Max(0, delayTicks);
            raidArrivalRealtime = Time.realtimeSinceStartup + (System.Math.Max(0, delayTicks) / 60f);
            raidLaunched = false;
            if (delayTicks <= 0)
                LaunchPendingRaid();
        }

        public override void MapComponentTick()
        {
            if (!encounterActive || resolved) return;
            LaunchPendingRaidIfDue();
            if (Find.TickManager.TicksGame % 60 == 0)
                CheckEncounterState();
        }

        private void LaunchPendingRaidIfDue()
        {
            if (raidLaunched || raidArrivalTick < 0) return;
            bool realtimeDue = raidArrivalRealtime > 0f && Time.realtimeSinceStartup >= raidArrivalRealtime;
            bool tickFallbackDue = Find.TickManager.TicksGame >= raidArrivalTick + 1800;
            if (!realtimeDue && !tickFallbackDue) return;
            LaunchPendingRaid();
        }

        private void LaunchPendingRaid()
        {
            if (raidLaunched) return;
            raidLaunched = true;
            raidLaunchedTick = Find.TickManager.TicksGame;
            raidThreatSeen = false;
            raidInboundSeen = false;
            WorldObject_Traveler traveler = RecreateTransientTraveler();
            if (traveler != null)
                WD_OutpostDefenseEncounterUtility.ExecuteRaidIncident(map, traveler, pendingRaidPoints);
        }

        private void CheckEncounterState()
        {
            if (!raidLaunched)
                return;

            if (IsWithinPostLoadGrace())
                return;

            // Brief settle after the raid incident fires (covers walk-in spawn delay too).
            if (raidLaunchedTick >= 0 && Find.TickManager.TicksGame < raidLaunchedTick + 90)
                return;

            if (Find.TickManager.TicksGame <= startTick + 600)
                return;

            bool defenderStanding = AnyBorrowedDefenderStanding();

            if (!defenderStanding)
            {
                if (!AnyBorrowedDefenderExists())
                {
                    AbortEncounterRestoreDefenders("no valid borrowed defenders after load");
                    return;
                }

                ResolveManualDefeat();
                return;
            }

            bool inbound = AnyInboundRaidThreat();
            bool hostiles = GenHostility.AnyHostileActiveThreatToPlayer(map, true);

            // Only living hostiles arm victory. Inbound pods must NOT — otherwise the gap between
            // skyfaller despawn and pawn open counts as "threat gone" and auto-wins.
            if (inbound)
                raidInboundSeen = true;
            if (hostiles)
                raidThreatSeen = true;

            if (inbound)
                return;

            if (!raidThreatSeen)
            {
                // Longer grace if pods were seen (open delay); shorter if the incident never spawned anything.
                int graceTicks = raidInboundSeen ? 5000 : 1800;
                if (raidLaunchedTick >= 0
                    && Find.TickManager.TicksGame >= raidLaunchedTick + graceTicks)
                {
                    Log.Warning($"[TSA WD] Outpost defense raid produced no hostile pawns for {outpost?.Label ?? "unknown"}; resolving as victory.");
                    ResolveManualVictory();
                }
                return;
            }

            if (!hostiles && defenderStanding)
            {
                ResolveManualVictory();
                return;
            }
        }

        /// <summary>
        /// Incoming / opening drop pods still count as an unresolved raid (not a victory condition).
        /// Includes skyfallers and post-landing DropPod things before pawns exit.
        /// </summary>
        private bool AnyInboundRaidThreat()
        {
            if (map?.listerThings?.AllThings == null) return false;
            List<Thing> all = map.listerThings.AllThings;
            for (int i = 0; i < all.Count; i++)
            {
                Thing t = all[i];
                if (t == null || t.Destroyed) continue;
                if (t is Skyfaller)
                    return true;
                string defName = t.def?.defName;
                if (defName != null
                    && defName.IndexOf("DropPod", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private bool AnyBorrowedDefenderStanding()
        {
            if (borrowedPawns == null) return false;
            for (int i = 0; i < borrowedPawns.Count; i++)
            {
                Pawn pawn = borrowedPawns[i];
                if (pawn != null && !pawn.Destroyed && !pawn.Dead && !pawn.Downed)
                    return true;
            }
            return false;
        }

        private bool AnyBorrowedDefenderExists()
        {
            return AnyBorrowedPawnReferenceExists(borrowedPawns)
                || AnyBorrowedPawnReferenceExists(borrowedStoredTransportPawns)
                || AnyBorrowedPawnReferenceExists(borrowedMechanoids);
        }

        private static bool AnyBorrowedPawnReferenceExists(List<Pawn> list)
        {
            if (list == null) return false;
            for (int i = 0; i < list.Count; i++)
            {
                Pawn pawn = list[i];
                if (pawn != null && !pawn.Destroyed)
                    return true;
            }
            return false;
        }

        private void AbortEncounterRestoreDefenders(string reason)
        {
            if (resolved) return;
            resolved = true;
            encounterActive = false;

            Log.Warning($"[TSA WD] Aborting outpost defense encounter ({reason}) for {outpost?.Label ?? "unknown"} — restoring defenders without destroying outpost.");

            outpost?.ReturnManualDefenseStoredTransportPawns(SurvivingBorrowedStoredTransportPawns());
            outpost?.ReturnManualDefenseMechanoids(SurvivingBorrowedMechanoids());
            outpost?.ReturnManualDefensePawns(SurvivingBorrowedPawns());
            outpost?.ClearManualDefenseActive();
        }

        private List<Pawn> SurvivingBorrowedPawns()
        {
            var list = new List<Pawn>();
            if (borrowedPawns == null) return list;
            for (int i = 0; i < borrowedPawns.Count; i++)
            {
                Pawn pawn = borrowedPawns[i];
                if (pawn == null || pawn.Destroyed || pawn.Dead) continue;
                if (pawn.Faction != null && pawn.Faction.IsPlayer)
                    list.Add(pawn);
            }
            return list;
        }

        private List<Pawn> SurvivingBorrowedStoredTransportPawns()
        {
            var list = new List<Pawn>();
            if (borrowedStoredTransportPawns == null) return list;
            for (int i = 0; i < borrowedStoredTransportPawns.Count; i++)
            {
                Pawn pawn = borrowedStoredTransportPawns[i];
                if (pawn == null || pawn.Destroyed || pawn.Dead) continue;
                if (pawn.Faction != null && pawn.Faction.IsPlayer)
                    list.Add(pawn);
            }
            return list;
        }

        private List<Pawn> SurvivingBorrowedMechanoids()
        {
            var list = new List<Pawn>();
            if (borrowedMechanoids == null) return list;
            for (int i = 0; i < borrowedMechanoids.Count; i++)
            {
                Pawn pawn = borrowedMechanoids[i];
                if (pawn == null || pawn.Destroyed || pawn.Dead) continue;
                if (pawn.Faction != null && pawn.Faction.IsPlayer)
                    list.Add(pawn);
            }
            return list;
        }

        private void ResolveManualVictory()
        {
            if (resolved) return;
            resolved = true;
            encounterActive = false;

            string outpostLabel = outpost?.LabelCap ?? "Outpost";
            // Return borrowed defenders first so they are off the map before we scan for extras.
            outpost?.ReturnManualDefenseStoredTransportPawns(SurvivingBorrowedStoredTransportPawns());
            outpost?.ReturnManualDefenseMechanoids(SurvivingBorrowedMechanoids());
            int returned = outpost?.ReturnManualDefensePawns(SurvivingBorrowedPawns()) ?? 0;
            ReturnExtraPlayerPawnsAsCaravan(includeDowned: true);
            int captivesTaken = 0;
            if (outpost != null && !outpost.Destroyed)
                captivesTaken = OutpostPrisonerUtility.HarvestCaptivesFromDefenseMap(outpost, map);
            ResolveSharedOutpostRaid(attackerWon: false);

            string letterText = "TSA_WD_OutpostDefense_VictoryLetter_Text".Translate(outpostLabel, enemyFaction?.Name ?? "Unknown", returned);
            if (captivesTaken > 0)
                letterText += "\n\n" + "TSA_WD_Letter_OutpostDefended_Captives".Translate(captivesTaken.ToString());
            Find.LetterStack.ReceiveLetter(
                "TSA_WD_OutpostDefense_VictoryLetter_Label".Translate(),
                letterText,
                LetterDefOf.PositiveEvent,
                outpost ?? (LookTargets)new GlobalTargetInfo(map.Center, map));
            QueueTemporaryMapRemoval();
        }

        private void ResolveManualDefeat()
        {
            if (resolved) return;
            resolved = true;
            encounterActive = false;

            string outpostLabel = outpost?.LabelCap ?? "Outpost";
            ReturnExtraPlayerPawnsAsCaravan(includeDowned: false);
            KillRemainingBorrowedPawns();
            KillRemainingBorrowedStoredTransportPawns();
            KillRemainingBorrowedMechanoids();
            outpost?.ClearManualDefenseActive();
            ResolveSharedOutpostRaid(attackerWon: true);

            if (WorldDominationMod.settings?.notifyOutpostDestroyed ?? true)
            {
                Find.LetterStack.ReceiveLetter(
                    "TSA_WD_OutpostDefense_DefeatLetter_Label".Translate(),
                    "TSA_WD_OutpostDefense_DefeatLetter_Text".Translate(outpostLabel, enemyFaction?.Name ?? "Unknown"),
                    LetterDefOf.NegativeEvent,
                    new GlobalTargetInfo(map.Center, map));
            }
            QueueTemporaryMapRemoval();
        }

        private void KillRemainingBorrowedPawns()
        {
            if (borrowedPawns == null) return;
            for (int i = 0; i < borrowedPawns.Count; i++)
            {
                Pawn pawn = borrowedPawns[i];
                if (pawn != null && !pawn.Destroyed && !pawn.Dead)
                    pawn.Kill(null);
            }
        }

        private void KillRemainingBorrowedMechanoids()
        {
            if (borrowedMechanoids == null) return;
            for (int i = 0; i < borrowedMechanoids.Count; i++)
            {
                Pawn pawn = borrowedMechanoids[i];
                if (pawn != null && !pawn.Destroyed && !pawn.Dead)
                    pawn.Kill(null);
            }
        }

        private void KillRemainingBorrowedStoredTransportPawns()
        {
            if (borrowedStoredTransportPawns == null) return;
            for (int i = 0; i < borrowedStoredTransportPawns.Count; i++)
            {
                Pawn pawn = borrowedStoredTransportPawns[i];
                if (pawn != null && !pawn.Destroyed && !pawn.Dead)
                    pawn.Kill(null);
            }
        }

        private int ReturnExtraPlayerPawnsAsCaravan(bool includeDowned)
        {
            if (outpost == null || outpost.Destroyed || map == null || map.mapPawns == null)
                return 0;

            var borrowed = new HashSet<Pawn>();
            if (borrowedPawns != null)
            {
                for (int i = 0; i < borrowedPawns.Count; i++)
                {
                    Pawn pawn = borrowedPawns[i];
                    if (pawn != null) borrowed.Add(pawn);
                }
            }
            if (borrowedStoredTransportPawns != null)
            {
                for (int i = 0; i < borrowedStoredTransportPawns.Count; i++)
                {
                    Pawn pawn = borrowedStoredTransportPawns[i];
                    if (pawn != null) borrowed.Add(pawn);
                }
            }
            if (borrowedMechanoids != null)
            {
                for (int i = 0; i < borrowedMechanoids.Count; i++)
                {
                    Pawn pawn = borrowedMechanoids[i];
                    if (pawn != null) borrowed.Add(pawn);
                }
            }

            var toReturn = new List<Pawn>();
            IReadOnlyList<Pawn> allPawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < allPawns.Count; i++)
            {
                Pawn pawn = allPawns[i];
                if (pawn == null || pawn.Destroyed || pawn.Dead) continue;
                if (pawn.Faction != Faction.OfPlayer) continue;
                if (borrowed.Contains(pawn)) continue;
                if (!includeDowned && pawn.Downed) continue;
                toReturn.Add(pawn);
            }

            for (int i = 0; i < toReturn.Count; i++)
            {
                Pawn pawn = toReturn[i];
                if (pawn == null || pawn.Destroyed) continue;
                if (pawn.Spawned) pawn.DeSpawn();
                pawn.holdingOwner?.Remove(pawn);
                if (pawn.Faction != Faction.OfPlayer)
                    pawn.SetFaction(Faction.OfPlayer);
            }

            if (toReturn.Count == 0)
                return 0;

            Caravan caravan = CaravanMaker.MakeCaravan(toReturn, Faction.OfPlayer, outpost.Tile, true);
            if (caravan != null)
            {
                if (caravan.PawnsListForReading.Count == 0)
                {
                    caravan.Destroy();
                    return 0;
                }
                Messages.Message("TSA_WD_OutpostDefense_ExtraPawnsReturned".Translate(toReturn.Count, outpost.LabelCap), caravan, MessageTypeDefOf.PositiveEvent, false);
                return toReturn.Count;
            }

            return 0;
        }

        private void ResolveSharedOutpostRaid(bool attackerWon)
        {
            WorldObject_Traveler traveler = RecreateTransientTraveler();
            if (traveler == null) return;
            Raid_Simulated.ResolvePlayerOutpostRaidArrival(traveler, Find.World.GetComponent<WorldComponent_SpreadManager>(), attackerWon, suppressOutpostLetter: true);
        }

        private WorldObject_Traveler RecreateTransientTraveler()
        {
            WorldObjectDef def = travelerDef ?? DefDatabase<WorldObjectDef>.GetNamedSilentFail("TSA_WD_Traveler_Outpost_Raid");
            if (def == null) return null;
            var traveler = (WorldObject_Traveler)WorldObjectMaker.MakeWorldObject(def);
            traveler.SetFaction(enemyFaction);
            traveler.travelerStrength = travelerStrength;
            traveler.initialStrength = initialStrength > 0f ? initialStrength : travelerStrength;
            traveler.spawnTick = spawnTick > 0 ? spawnTick : Find.TickManager.TicksGame;
            traveler.mission = savedMission;
            traveler.originObject = savedOrigin;
            traveler.targetObject = outpost;
            traveler.raidOrderOutcome = raidOrderOutcome;
            traveler.alliedRaidOrderGoodwillPaid = alliedRaidOrderGoodwillPaid;
            traveler.alliedRaidOrderGoodwillRefunded = alliedRaidOrderGoodwillRefunded;
            traveler.raidAttackerList = raidAttackerList != null ? new List<WorldObject>(raidAttackerList) : new List<WorldObject>();
            traveler.raidAttackerDetails = raidAttackerDetails != null ? new List<string>(raidAttackerDetails) : new List<string>();
            traveler.raidAttackerForceRows = RaidForceLogRow.CloneList(raidAttackerForceRows);
            traveler.raidDefenderForceRows = RaidForceLogRow.CloneList(raidDefenderForceRows);
            traveler.contributionFactors = new Dictionary<WorldObject, float>();
            if (contributionKeys != null && contributionValues != null)
            {
                int count = System.Math.Min(contributionKeys.Count, contributionValues.Count);
                for (int i = 0; i < count; i++)
                {
                    if (contributionKeys[i] != null)
                        traveler.contributionFactors[contributionKeys[i]] = contributionValues[i];
                }
            }
            return traveler;
        }

        public override void MapRemoved()
        {
            base.MapRemoved();
            if (!encounterActive || resolved) return;
            if (IsWithinPostLoadGrace()) return;

            AbortEncounterRestoreDefenders("map removed before encounter resolved");
        }

        private void QueueTemporaryMapRemoval()
        {
            if (mapRemovalQueued || map == null) return;
            mapRemovalQueued = true;
            Map mapToRemove = map;
            MapParent parent = mapToRemove.Parent;

            LongEventHandler.QueueLongEvent(delegate
            {
                WD_OutpostDefenseMapUtility.ClearSettlementCenter(mapToRemove);
                if (mapToRemove != null && Current.Game.Maps.Contains(mapToRemove))
                    Current.Game.DeinitAndRemoveMap(mapToRemove, false);
                if (parent != null && !parent.Destroyed)
                    parent.Destroy();
            }, "GeneratingArea", false, null);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref travelerDef, "travelerDef");
            Scribe_References.Look(ref enemyFaction, "enemyFaction");
            Scribe_Values.Look(ref travelerStrength, "travelerStrength", 0f);
            Scribe_Values.Look(ref initialStrength, "initialStrength", 0f);
            Scribe_Values.Look(ref spawnTick, "spawnTick", 0);
            Scribe_Values.Look(ref savedMission, "savedMission", TravelerMission.Raid);
            Scribe_Values.Look(ref raidOrderOutcome, "raidOrderOutcome", RaidOrderOutcome.PlayerOutpostConquestMenu);
            Scribe_Values.Look(ref alliedRaidOrderGoodwillPaid, "alliedRaidOrderGoodwillPaid", 0);
            Scribe_Values.Look(ref alliedRaidOrderGoodwillRefunded, "alliedRaidOrderGoodwillRefunded", false);
            Scribe_References.Look(ref savedOrigin, "savedOrigin");
            Scribe_References.Look(ref outpost, "outpost");
            Scribe_Collections.Look(ref raidAttackerList, "raidAttackerList", LookMode.Reference);
            Scribe_Collections.Look(ref raidAttackerDetails, "raidAttackerDetails", LookMode.Value);
            Scribe_Collections.Look(ref raidAttackerForceRows, "raidAttackerForceRows", LookMode.Deep);
            Scribe_Collections.Look(ref raidDefenderForceRows, "raidDefenderForceRows", LookMode.Deep);
            Scribe_Collections.Look(ref contributionKeys, "contributionKeys", LookMode.Reference);
            Scribe_Collections.Look(ref contributionValues, "contributionValues", LookMode.Value);
            Scribe_Collections.Look(ref borrowedPawns, "borrowedPawns", LookMode.Reference);
            Scribe_Collections.Look(ref borrowedStoredTransportPawns, "borrowedStoredTransportPawns", LookMode.Reference);
            Scribe_Collections.Look(ref borrowedMechanoids, "borrowedMechanoids", LookMode.Reference);
            Scribe_Values.Look(ref encounterActive, "encounterActive", false);
            Scribe_Values.Look(ref resolved, "resolved", false);
            Scribe_Values.Look(ref mapRemovalQueued, "mapRemovalQueued", false);
            Scribe_Values.Look(ref startTick, "startTick", -1);
            Scribe_Values.Look(ref raidLaunched, "raidLaunched", false);
            Scribe_Values.Look(ref raidLaunchedTick, "raidLaunchedTick", -1);
            Scribe_Values.Look(ref raidThreatSeen, "raidThreatSeen", false);
            Scribe_Values.Look(ref raidInboundSeen, "raidInboundSeen", false);
            Scribe_Values.Look(ref raidArrivalTick, "raidArrivalTick", -1);
            Scribe_Values.Look(ref pendingRaidPoints, "pendingRaidPoints", 0f);

            if (raidAttackerList == null) raidAttackerList = new List<WorldObject>();
            if (raidAttackerDetails == null) raidAttackerDetails = new List<string>();
            if (raidAttackerForceRows == null) raidAttackerForceRows = new List<RaidForceLogRow>();
            if (raidDefenderForceRows == null) raidDefenderForceRows = new List<RaidForceLogRow>();
            if (contributionKeys == null) contributionKeys = new List<WorldObject>();
            if (contributionValues == null) contributionValues = new List<float>();
            if (borrowedPawns == null) borrowedPawns = new List<Pawn>();
            if (borrowedStoredTransportPawns == null) borrowedStoredTransportPawns = new List<Pawn>();
            if (borrowedMechanoids == null) borrowedMechanoids = new List<Pawn>();
            if (Scribe.mode == LoadSaveMode.PostLoadInit && encounterActive && !resolved)
            {
                postLoadGraceUntilTick = Find.TickManager.TicksGame + 300;
                startTick = Find.TickManager.TicksGame;
                if (!raidLaunched && raidArrivalTick >= 0)
                    raidArrivalRealtime = Time.realtimeSinceStartup + 15f;
                if (raidLaunched)
                {
                    if (AnyInboundRaidThreat())
                        raidInboundSeen = true;
                    // Only living hostiles arm victory — never inbound pods alone.
                    if (GenHostility.AnyHostileActiveThreatToPlayer(map, true))
                        raidThreatSeen = true;
                }
            }
        }
    }
}
