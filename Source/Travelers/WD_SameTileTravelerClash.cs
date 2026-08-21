using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Hostile encounters when travelers share a tile with another hostile traveler or caravan.
    /// No distance scans — only ObjectsAt(tile). Caravan tile changes detected via
    /// <see cref="TickCaravanClashDetection"/> called from WorldComponentTick.
    /// </summary>
    public static class WD_SameTileTravelerClash
    {
        private static readonly Dictionary<int, int> caravanLastTile = new Dictionary<int, int>();
        private static readonly List<int> cleanupTemp = new List<int>();
        private static readonly HashSet<int> cleanupKeep = new HashSet<int>();
        private static int lastCleanupTick = -1;

        public static void TickCaravanClashDetection()
        {
            if (Find.TickManager.TicksGame % 4 != 0) return;

            var caravans = Find.WorldObjects.Caravans;
            if (caravans == null || caravans.Count == 0) return;

            for (int i = 0; i < caravans.Count; i++)
            {
                Caravan c = caravans[i];
                if (c == null || c.Destroyed) continue;
                int tile = c.Tile;
                if (tile < 0) continue;

                bool tileChanged;
                if (caravanLastTile.TryGetValue(c.ID, out int prev))
                {
                    tileChanged = prev != tile;
                    caravanLastTile[c.ID] = tile;
                }
                else
                {
                    tileChanged = true;
                    caravanLastTile[c.ID] = tile;
                }
                if (!tileChanged) continue;

                // Temporary: player caravans overrun hostile AT Turrets by walking onto their tile.
                if (c.Faction != null && c.Faction.IsPlayer)
                    AtTurretUtility.TryOverrunHostileAtTurret(c);

                TryResolveCaravanVsHostileTravelersOnTile(c, tile);
                if (c.Destroyed) continue;
                // Feature C (real-Caravan half): only the player's own caravans can be ambushed by a hostile NPC settlement.
                if (c.Faction != null && c.Faction.IsPlayer)
                    SettlementAmbushUtility.TryCheckAmbushForCaravan(c, tile);
            }

            int tickNow = Find.TickManager.TicksGame;
            if (tickNow - lastCleanupTick >= 15000)
            {
                lastCleanupTick = tickNow;
                if (caravanLastTile.Count > caravans.Count * 2 + 10)
                {
                    cleanupKeep.Clear();
                    for (int j = 0; j < caravans.Count; j++)
                        if (caravans[j] != null) cleanupKeep.Add(caravans[j].ID);
                    cleanupTemp.Clear();
                    foreach (var kvp in caravanLastTile)
                        if (!cleanupKeep.Contains(kvp.Key)) cleanupTemp.Add(kvp.Key);
                    for (int j = 0; j < cleanupTemp.Count; j++)
                        caravanLastTile.Remove(cleanupTemp[j]);
                }
            }
        }

        public static bool TryBeforeTravelerEntersTile_TravelerVsTraveler(WorldObject_Traveler incoming, int destTile)
        {
            if (incoming == null || incoming.Destroyed || destTile < 0) return false;
            if (incoming.mission == TravelerMission.OutpostDelivery) return false;
            if (incoming.Faction == null) return false;

            // Pass 1: abort before any clash if tile has a home / MapParent / gravship (order-independent).
            if (OdysseyGravshipCaravanClashCompat.TileBlocksPlayerCaravanClash(
                    PlanetSurfaceWorldActions.PlanetTileForWdTravel(destTile, incoming)))
                return false;

            foreach (WorldObject wo in Find.WorldObjects.ObjectsAt(destTile))
            {
                if (wo is WorldObject_Traveler other && other != incoming && !other.Destroyed
                    && other.mission != TravelerMission.OutpostDelivery
                    && other.Faction != null && WorldActions_Utils.SafeHostileTo(incoming.Faction, other.Faction))
                {
                    if (IsDeferredRapidResponseInterceptClash(incoming, other)
                        || IsDeferredRaidBribeDeliveryClash(incoming, other))
                        continue;

                    if (Prefs.DevMode)
                        Log.Message($"[TSA WD] Traveler vs traveler same tile {destTile}: {incoming.Label} enters vs {other.Label}");
                    ResolveTravelerVsTraveler(incoming, other);
                    return true;
                }
            }
            return false;
        }

        public static void AfterTravelerLanded_TravelerVsCaravan(WorldObject_Traveler traveler, int tile)
        {
            if (traveler == null || traveler.Destroyed || tile < 0) return;
            if (traveler.mission == TravelerMission.OutpostDelivery) return;
            if (traveler.Faction == null) return;

            // Pass 1: colony / landed gravship / launch site — never start clash (ObjectsAt order is unreliable).
            if (OdysseyGravshipCaravanClashCompat.TileBlocksPlayerCaravanClash(traveler.Tile))
                return;

            foreach (WorldObject wo in Find.WorldObjects.ObjectsAt(tile))
            {
                if (wo is Caravan caravan && !caravan.Destroyed && caravan.Faction != null
                    && WorldActions_Utils.SafeHostileTo(traveler.Faction, caravan.Faction))
                {
                    if (Prefs.DevMode)
                        Log.Message($"[TSA WD] Traveler landed tile {tile} with hostile caravan: {traveler.Label} vs {caravan.Label}");

                    if (caravan.Faction.IsPlayer)
                    {
                        WD_CaravanClashUtility.StartInterceptionEncounter(caravan, traveler);
                        return;
                    }

                    ResolveNpcCaravanVsTraveler(caravan, traveler, travelerIsInitiator: true);
                    return;
                }
            }
        }

        /// <summary>
        /// Raid-bribe delivery is deferred from strength clash; complete the meet-up once both share a tile after a hop.
        /// </summary>
        public static void AfterTravelerLanded_DeferredMeetups(WorldObject_Traveler traveler, int tile)
        {
            if (traveler == null || traveler.Destroyed || tile < 0) return;

            if (traveler.mission == TravelerMission.RaidBribe)
            {
                WorldActions_Traveler.TryCompleteRaidBribeSameTile(traveler);
                return;
            }

            foreach (WorldObject wo in Find.WorldObjects.ObjectsAt(tile))
            {
                if (!(wo is WorldObject_Traveler other) || other == traveler || other.Destroyed) continue;
                if (other.mission == TravelerMission.RaidBribe && other.targetObject == traveler)
                {
                    WorldActions_Traveler.TryCompleteRaidBribeSameTile(other);
                    return;
                }
            }
        }

        private static void TryResolveCaravanVsHostileTravelersOnTile(Caravan caravan, int tile)
        {
            if (caravan == null || caravan.Destroyed || caravan.Faction == null) return;
            if (tile < 0) return;

            // Pass 1: blockers before travelers (same-tile ObjectsAt order is not guaranteed).
            if (OdysseyGravshipCaravanClashCompat.TileBlocksPlayerCaravanClash(caravan.Tile))
                return;

            foreach (WorldObject wo in Find.WorldObjects.ObjectsAt(tile))
            {
                if (wo is WorldObject_Traveler tr && !tr.Destroyed && tr.mission != TravelerMission.OutpostDelivery
                    && tr.Faction != null && WorldActions_Utils.SafeHostileTo(caravan.Faction, tr.Faction))
                {
                    if (Prefs.DevMode)
                        Log.Message($"[TSA WD] Caravan moved to tile {tile} vs traveler: {caravan.Label} vs {tr.Label}");

                    if (caravan.Faction.IsPlayer)
                    {
                        WD_CaravanClashUtility.StartInterceptionEncounter(caravan, tr);
                        return;
                    }

                    ResolveNpcCaravanVsTraveler(caravan, tr, travelerIsInitiator: false);
                    return;
                }
            }
        }

        private static bool IsDeferredRapidResponseInterceptClash(WorldObject_Traveler incoming, WorldObject_Traveler other)
        {
            // Only when the RR is entering its target's tile: ArrivalAction runs ExecuteRapidResponseIntercept.
            // Do NOT defer the reverse (target walking onto a waiting RR) — that would never arrive and never clash.
            return incoming != null
                && incoming.mission == TravelerMission.RapidResponseIntercept
                && incoming.targetObject == other;
        }

        /// <summary>Raid bribe delivery vs its designated raid target is not a strength clash.</summary>
        private static bool IsDeferredRaidBribeDeliveryClash(WorldObject_Traveler a, WorldObject_Traveler b)
        {
            if (a.mission == TravelerMission.RaidBribe && a.targetObject == b) return true;
            if (b.mission == TravelerMission.RaidBribe && b.targetObject == a) return true;
            return false;
        }

        private static void ResolveTravelerVsTraveler(WorldObject_Traveler incoming, WorldObject_Traveler defender)
        {
            var manager = Find.World.GetComponent<WorldComponent_SpreadManager>();
            float sa = incoming.travelerStrength;
            float sb = defender.travelerStrength;
            if (sb <= 0f || sa <= 0f) return;

            WorldObject_Traveler rapidResponse = incoming.mission == TravelerMission.RapidResponseIntercept ? incoming :
                defender.mission == TravelerMission.RapidResponseIntercept ? defender : null;
            WorldObject_Traveler rapidTarget = rapidResponse == incoming ? defender :
                rapidResponse == defender ? incoming : null;
            float rapidTargetStrengthBefore = rapidTarget == incoming ? sa :
                rapidTarget == defender ? sb : 0f;
            Faction rapidTargetFaction = rapidTarget?.Faction;

            OpenFieldClashResult clash = OpenFieldClashUtility.ResolveTravelerClash(incoming, defender, incoming, manager);
            if (!clash.ok) return;

            if (rapidResponse != null)
            {
                bool rapidResponseWon = OpenFieldClashUtility.SideWon(clash, rapidResponse);
                float surviving = OpenFieldClashUtility.SurvivorStrengthFor(clash, rapidResponse);
                float rapidTargetStrengthAfter = rapidTarget == null || rapidTarget.Destroyed
                    ? 0f
                    : Mathf.Max(0f, rapidTarget.travelerStrength);
                int captivesTaken = 0;
                if (rapidResponseWon)
                {
                    captivesTaken = OutpostPrisonerUtility.TryCaptureFromRapidResponseWin(
                        rapidResponse, rapidTargetFaction, rapidTargetStrengthBefore, rapidTargetStrengthAfter);
                }
                TravelerEndpointUtility.RefundRapidResponseStrength(rapidResponse, surviving);
                SendRapidResponseAutoClashLetter(
                    rapidResponse, rapidTarget, rapidResponseWon,
                    rapidTargetStrengthBefore, rapidTargetStrengthAfter, captivesTaken);
                // Intercept caravans always despawn after the fight (survivors return as refunded strength).
                if (!rapidResponse.Destroyed)
                {
                    if (surviving > 0.01f)
                        rapidResponse.suppressDestroyedWorldFx = true;
                    rapidResponse.Destroy();
                }
            }
        }

        private static void SendRapidResponseAutoClashLetter(
            WorldObject_Traveler rapidResponse,
            WorldObject_Traveler target,
            bool won,
            float targetStrengthBefore,
            float targetStrengthAfter,
            int captivesTaken)
        {
            WorldObject origin = rapidResponse?.originObject;
            LookTargets look = null;
            if (target != null && !target.Destroyed)
                look = new LookTargets(target);
            else if (origin != null && !origin.Destroyed)
                look = new LookTargets(origin);

            WorldActions_Traveler.SendRapidResponseClashLetter(
                rapidResponse,
                target?.LabelCap ?? "?",
                won,
                targetStrengthBefore,
                targetStrengthAfter,
                captivesTaken,
                look);
        }

        private static void ResolveNpcCaravanVsTraveler(Caravan caravan, WorldObject_Traveler traveler, bool travelerIsInitiator)
        {
            Faction caravanFaction = caravan.Faction;
            string caravanLabel = caravan.LabelCap;
            float caravanStrengthBefore = WorldComponent_SpreadManager.ComputeCaravanMortarStrengthPool(caravan);
            var manager = Find.World.GetComponent<WorldComponent_SpreadManager>();

            OpenFieldClashResult clash = OpenFieldClashUtility.ResolveNpcCaravanVsTraveler(
                caravan, traveler, travelerIsInitiator, manager);
            if (!clash.ok) return;

            if (traveler.mission == TravelerMission.RapidResponseIntercept)
            {
                bool travelerWon = OpenFieldClashUtility.SideWon(clash, traveler);
                int captivesTaken = 0;
                if (travelerWon)
                {
                    captivesTaken = OutpostPrisonerUtility.TryCaptureFromRapidResponseWin(
                        traveler, caravanFaction, caravanStrengthBefore, 0f);
                }
                float surviving = OpenFieldClashUtility.SurvivorStrengthFor(clash, traveler);
                TravelerEndpointUtility.RefundRapidResponseStrength(traveler, surviving);
                if (travelerWon)
                {
                    WorldObject origin = traveler.originObject;
                    LookTargets look = origin != null && !origin.Destroyed ? new LookTargets(origin) : null;
                    WorldActions_Traveler.SendRapidResponseClashLetter(
                        traveler,
                        caravanLabel,
                        true,
                        caravanStrengthBefore,
                        0f,
                        captivesTaken,
                        look);
                }
                if (!traveler.Destroyed)
                {
                    if (surviving > 0.01f)
                        traveler.suppressDestroyedWorldFx = true;
                    traveler.Destroy();
                }
            }
        }
    }
}
