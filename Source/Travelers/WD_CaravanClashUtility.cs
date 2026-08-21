using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using UnityEngine;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Caravan vs WD traveler map clashes. Long events run synchronously (doAsynchronously false) so
    /// raid/vehicle graphic loads (Vehicle Framework / VVERaid) stay on the main thread.
    /// </summary>
    public static class WD_CaravanClashUtility
    {
        private const int EntryBandWidth = 12;

        private enum CardinalEdge
        {
            West, East, North, South
        }

        public static void StartInterceptionEncounter(Caravan playerCaravan, WorldObject_Traveler traveler)
        {
            if (OdysseyGravshipCaravanClashCompat.ShouldSkipPlayerCaravanClash(playerCaravan))
                return;

            if (traveler != null && traveler.mission == TravelerMission.Trader)
            {
                StartTraderCaravanClashEncounter(playerCaravan, traveler);
                return;
            }

            if (traveler != null && WorldObject_Traveler.IsRaidMission(traveler.mission)
                && playerCaravan?.Faction != null
                && Raid_Simulated.TryAbortIfNoLongerHostile(
                    traveler,
                    traveler.originObject,
                    traveler.targetObject,
                    Find.World.GetComponent<WorldComponent_SpreadManager>(),
                    playerCaravan.Faction))
            {
                traveler.Destroy();
                return;
            }

            LongEventHandler.QueueLongEvent(delegate
            {
                if (playerCaravan == null || playerCaravan.Destroyed || traveler == null || traveler.Destroyed)
                    return;

                // Temporary overrun before Ambush map gen (player AT fate is marked when the traveler is stored).
                AtTurretUtility.TryOverrunHostileAtTurret(playerCaravan);

                Map map = GetOrGenerateMapForTile(playerCaravan.Tile);
                if (map == null)
                {
                    ResumeTravelerAfterFailedClashEncounter(traveler);
                    return;
                }

                if (WorldObject_Traveler.IsRaidMission(traveler.mission)
                    && playerCaravan.Faction != null
                    && Raid_Simulated.TryAbortIfNoLongerHostile(
                        traveler,
                        traveler.originObject,
                        traveler.targetObject,
                        Find.World.GetComponent<WorldComponent_SpreadManager>(),
                        playerCaravan.Faction))
                {
                    traveler.Destroy();
                    return;
                }

                var tracker = GetOrAddClashTracker(map);
                float points = ComputeInterceptionRaidPoints(traveler, map);
                LogInterceptionRaidPoints(traveler, map, points);

                tracker.InterceptionRaidPending = true;
                try
                {
                    Raid_OnPlayerColony.IsCaravanClashInterception = true;

                    StopBothForEncounter(playerCaravan, traveler);
                    CaravanEnterMapUtility.Enter(playerCaravan, map, CaravanEnterMode.Edge);

                    ExecuteInterceptionRaidIncident(map, traveler, points);
                }
                finally
                {
                    Raid_OnPlayerColony.IsCaravanClashInterception = false;
                    tracker.InterceptionRaidPending = false;
                }

                tracker.StoreAndDestroyTraveler(traveler);

                SendPlayerCaravanClashStartedLetter(map, traveler.Label, traveler.Faction?.Name ?? "");
            }, "GeneratingArea", false, null);
        }

        public static void StartInterceptionEncounterDropPods(IReadOnlyList<Pawn> playerPawns, WorldObject_Traveler traveler)
        {
            if (playerPawns == null || playerPawns.Count == 0 || traveler == null || traveler.Destroyed) return;

            string letterTravelerLabel = traveler.Label;
            string letterFactionName = traveler.Faction?.Name ?? "";

            LongEventHandler.QueueLongEvent(delegate
            {
                if (traveler == null || traveler.Destroyed) return;

                int fightTile = traveler.Tile.tileId;
                AtTurretUtility.TryOverrunHostileAtTurretOnTile(fightTile, Faction.OfPlayer, traveler);

                Map map = GetOrGenerateMapForTile(traveler.Tile);
                if (map == null)
                {
                    ResumeTravelerAfterFailedClashEncounter(traveler);
                    return;
                }
                Faction encounterFaction = traveler.Faction;

                if (WorldObject_Traveler.IsRaidMission(traveler.mission)
                    && Raid_Simulated.TryAbortIfNoLongerHostile(
                        traveler,
                        traveler.originObject,
                        traveler.targetObject,
                        Find.World.GetComponent<WorldComponent_SpreadManager>(),
                        Faction.OfPlayer))
                {
                    traveler.Destroy();
                    return;
                }

                var tracker = GetOrAddClashTracker(map);
                RapidResponseUtility.DropPawnsViaDropPods(playerPawns, map);

                if (traveler.mission == TravelerMission.Trader)
                {
                    bool spawnedTraderForces = WD_TraderCaravanClash.SpawnTraderClashForces(map, traveler);
                    tracker.StoreAndDestroyTraveler(traveler);
                    if (!spawnedTraderForces)
                    {
                        float traderFallbackPoints = ComputeInterceptionRaidPoints(traveler, map);
                        LogInterceptionRaidPoints(traveler, map, traderFallbackPoints);
                        tracker.InterceptionRaidPending = true;
                        try
                        {
                            Raid_OnPlayerColony.IsCaravanClashInterception = true;
                            ExecuteInterceptionRaidIncident(map, encounterFaction, traderFallbackPoints);
                        }
                        finally
                        {
                            Raid_OnPlayerColony.IsCaravanClashInterception = false;
                            tracker.InterceptionRaidPending = false;
                        }
                    }
                }
                else
                {
                    float points = ComputeInterceptionRaidPoints(traveler, map);
                    LogInterceptionRaidPoints(traveler, map, points);
                    tracker.StoreAndDestroyTraveler(traveler);
                    tracker.InterceptionRaidPending = true;
                    try
                    {
                        Raid_OnPlayerColony.IsCaravanClashInterception = true;
                        ExecuteInterceptionRaidIncident(map, encounterFaction, points);
                    }
                    finally
                    {
                        Raid_OnPlayerColony.IsCaravanClashInterception = false;
                        tracker.InterceptionRaidPending = false;
                    }
                }

                SendPlayerCaravanClashStartedLetter(map, letterTravelerLabel, letterFactionName);
            }, "GeneratingArea", false, null);
        }

        private static float ComputeInterceptionRaidPoints(WorldObject_Traveler traveler, Map map)
        {
            return RaidPointsHelper.ClampRaidPointsToStorytellerBand(traveler?.travelerStrength ?? 0f, map);
        }

        private static void LogInterceptionRaidPoints(WorldObject_Traveler traveler, Map map, float points)
        {
            var seth = WorldDominationMod.settings;
            if (seth == null) return;

            float raw = traveler?.travelerStrength ?? 0f;

            if (!RaidPointsHelper.WdRaidPointsStorytellerBandClampActive())
            {
                Log.Message(
                    "[TSA WD] Interception raid points:" + "\n"
                    + $"  Traveler strength: {raw:F0}" + "\n"
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
                baselineWhere += $" (WD uses this player-home baseline instead of encounter map tile {map.Tile})";

            string verdict;
            if (raw < floor - 0.01f)
                verdict = "Clamped to floor (traveler strength was below the floor).";
            else if (raw > ceiling + 0.01f)
                verdict = "Clamped to ceiling (traveler strength was above the ceiling).";
            else
                verdict = "No clamping needed. Using traveler strength as raid points.";

            Log.Message(
                "[TSA WD] Interception raid points:" + "\n"
                + $"  Traveler strength: {raw:F0}" + "\n"
                + $"  Storyteller threat baseline: {baseline:F0} ({baselineWhere})" + "\n"
                + $"  Floor = baseline × {minFrac:0.###} = {floor:F0}" + "\n"
                + $"  Ceiling = baseline × {maxFrac:0.###} = {ceiling:F0}" + "\n"
                + $"  {verdict}" + "\n"
                + $"  Raid points used for incident: {points:F0}");
        }

        private static WD_MapComponent_CaravanClash GetOrAddClashTracker(Map map)
        {
            var tracker = map.GetComponent<WD_MapComponent_CaravanClash>();
            if (tracker == null)
            {
                tracker = new WD_MapComponent_CaravanClash(map);
                map.components.Add(tracker);
            }
            return tracker;
        }

        private static void ExecuteInterceptionRaidIncident(Map map, WorldObject_Traveler traveler, float points)
        {
            ExecuteInterceptionRaidIncident(map, traveler?.Faction, points);
        }

        private static void ExecuteInterceptionRaidIncident(Map map, Faction faction, float points)
        {
            IncidentParms parms = new IncidentParms
            {
                target = map,
                points = points,
                faction = faction,
                raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn,
                raidStrategy = RaidStrategyDefOf.ImmediateAttack,
                silent = true,
                customLetterLabel = "TSA_WD_Interception_Label".Translate(),
                canKidnap = false,
                canSteal = false
            };

            TryAssignInterceptionSpawn(map, parms);

            IncidentDefOf.RaidEnemy.Worker.TryExecute(parms);
        }

        private static void StartTraderCaravanClashEncounter(Caravan playerCaravan, WorldObject_Traveler traveler)
        {
            string letterTravelerLabel = traveler?.Label ?? "";
            string letterFactionName = traveler?.Faction?.Name ?? "";

            LongEventHandler.QueueLongEvent(delegate
            {
                if (playerCaravan == null || playerCaravan.Destroyed || traveler == null || traveler.Destroyed)
                    return;

                AtTurretUtility.TryOverrunHostileAtTurret(playerCaravan);

                Map map = GetOrGenerateMapForTile(playerCaravan.Tile);
                if (map == null)
                {
                    ResumeTravelerAfterFailedClashEncounter(traveler);
                    return;
                }

                var tracker = GetOrAddClashTracker(map);

                StopBothForEncounter(playerCaravan, traveler);
                CaravanEnterMapUtility.Enter(playerCaravan, map, CaravanEnterMode.Edge);
                if (!WD_TraderCaravanClash.SpawnTraderClashForces(map, traveler))
                {
                    float raidPoints = ComputeInterceptionRaidPoints(traveler, map);
                    LogInterceptionRaidPoints(traveler, map, raidPoints);
                    Log.Message("[TSA WD] Trader interception: using RaidEnemy fallback (no trader pawn group for this faction).");
                    tracker.InterceptionRaidPending = true;
                    try
                    {
                        Raid_OnPlayerColony.IsCaravanClashInterception = true;
                        ExecuteInterceptionRaidIncident(map, traveler, raidPoints);
                    }
                    finally
                    {
                        Raid_OnPlayerColony.IsCaravanClashInterception = false;
                        tracker.InterceptionRaidPending = false;
                    }
                }

                tracker.StoreAndDestroyTraveler(traveler);

                SendPlayerCaravanClashStartedLetter(map, letterTravelerLabel, letterFactionName);
            }, "GeneratingArea", false, null);
        }

        private static void SendPlayerCaravanClashStartedLetter(Map map, string travelerLabel, string factionName)
        {
            if (!(WorldDominationMod.settings?.notifyPlayerCaravanClash ?? WorldDominationSettings.DefNotifyPlayerCaravanClash))
                return;
            Find.LetterStack.ReceiveLetter(
                "TSA_WD_Letter_Interception_Label".Translate(),
                "TSA_WD_Letter_Interception_Text".Translate(travelerLabel, factionName),
                LetterDefOf.ThreatBig,
                new GlobalTargetInfo(map.Center, map)
            );
        }

        private static void TryAssignInterceptionSpawn(Map map, IncidentParms parms)
        {
            var playerCells = map.mapPawns.AllPawnsSpawned
                .Where(p => p.Faction != null && p.Faction.IsPlayer)
                .Select(p => p.Position)
                .ToList();

            if (playerCells.Count == 0)
                return;

            int sx = 0, sz = 0;
            foreach (var c in playerCells)
            {
                sx += c.x;
                sz += c.z;
            }

            var centroid = new IntVec3(sx / playerCells.Count, 0, sz / playerCells.Count);
            CardinalEdge playerEdge = DominantCardinalEdge(centroid, map);
            CardinalEdge opposite = OppositeCardinal(playerEdge);

            var tryOrder = new List<CardinalEdge> { opposite };
            foreach (var e in new[] { CardinalEdge.West, CardinalEdge.East, CardinalEdge.North, CardinalEdge.South }
                         .Where(e => e != playerEdge && e != opposite)
                         .OrderBy(_ => Rand.Value))
            {
                tryOrder.Add(e);
            }

            foreach (var edge in tryOrder)
            {
                if (TryFindEntryInBand(map, edge, EntryBandWidth, out IntVec3 cell) && cell.IsValid)
                {
                    parms.spawnCenter = cell;
                    parms.spawnRotation = DesiredRotFor(edge);
                    return;
                }
            }

            if (RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 edgeCell, map, 0.35f, true,
                    c => DominantCardinalEdge(c, map) != playerEdge))
            {
                parms.spawnCenter = edgeCell;
                parms.spawnRotation = Rot4.FromAngleFlat((map.Center - edgeCell).ToVector3Shifted().AngleFlat());
            }
        }

        private static CardinalEdge DominantCardinalEdge(IntVec3 c, Map map)
        {
            var rect = CellRect.WholeMap(map);
            int dW = c.x - rect.minX;
            int dE = rect.maxX - c.x;
            int dS = c.z - rect.minZ;
            int dN = rect.maxZ - c.z;
            int min = Mathf.Min(Mathf.Min(dW, dE), Mathf.Min(dS, dN));
            if (min == dW) return CardinalEdge.West;
            if (min == dE) return CardinalEdge.East;
            if (min == dS) return CardinalEdge.South;
            return CardinalEdge.North;
        }

        private static CardinalEdge OppositeCardinal(CardinalEdge e)
        {
            switch (e)
            {
                case CardinalEdge.West: return CardinalEdge.East;
                case CardinalEdge.East: return CardinalEdge.West;
                case CardinalEdge.North: return CardinalEdge.South;
                default: return CardinalEdge.North;
            }
        }

        private static CellRect CardinalBand(Map map, CardinalEdge edge, int band)
        {
            var rect = CellRect.WholeMap(map);
            if (rect.Area <= 0) return CellRect.Empty;
            int w = Mathf.Clamp(band, 1, Mathf.Max(rect.Width, rect.Height));
            switch (edge)
            {
                case CardinalEdge.West: return new CellRect(rect.minX, rect.minZ, w, rect.Height);
                case CardinalEdge.East: return new CellRect(rect.maxX - (w - 1), rect.minZ, w, rect.Height);
                case CardinalEdge.North: return new CellRect(rect.minX, rect.maxZ - (w - 1), rect.Width, w);
                default: return new CellRect(rect.minX, rect.minZ, rect.Width, w);
            }
        }

        private static bool TryFindEntryInBand(Map map, CardinalEdge edge, int band, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            var bandRect = CardinalBand(map, edge, band);
            if (bandRect.Area <= 0) return false;

            bool InBand(IntVec3 c) => bandRect.Contains(c);
            if (RCellFinder.TryFindRandomPawnEntryCell(out cell, map, 0.35f, true, InBand))
                return true;

            for (int i = 0; i < 240; i++)
            {
                var c = bandRect.RandomCell;
                if (c.InBounds(map) && !c.Fogged(map) && c.Standable(map))
                {
                    cell = c;
                    return true;
                }
            }

            for (int i = 0; i < 480; i++)
            {
                var c = bandRect.RandomCell;
                if (c.InBounds(map) && !c.Fogged(map) && c.Walkable(map))
                {
                    cell = c;
                    return true;
                }
            }

            return false;
        }

        private static Rot4 DesiredRotFor(CardinalEdge e)
        {
            switch (e)
            {
                case CardinalEdge.West: return Rot4.East;
                case CardinalEdge.East: return Rot4.West;
                case CardinalEdge.North: return Rot4.South;
                default: return Rot4.North;
            }
        }

        private static Map GetOrGenerateMapForTile(PlanetTile tile)
        {
            if (!tile.Valid) return null;

            // Belt-and-suspenders: never generate/reuse clash maps on colony / landed gravship tiles
            // (even if MapParentAt is unexpectedly null). Ambush sites are excluded from this check.
            if (OdysseyGravshipCaravanClashCompat.TileBlocksPlayerCaravanClash(tile))
            {
                if (Prefs.DevMode)
                    Log.Message($"[TSA WD] Aborting caravan clash map gen: tile blocks clash ({tile})");
                return null;
            }

            // Only reuse temporary Ambush encounter maps — never a player home / gravship landing map.
            Map map = Current.Game.Maps.FirstOrDefault(m => m.Tile == tile && IsTemporaryClashAmbushMap(m));
            if (map != null) return map;

            MapParent existing = Find.WorldObjects.MapParentAt(tile);
            if (existing != null)
            {
                if (existing.def == WorldObjectDefOf.Ambush)
                {
                    return MapGenerator.GenerateMap(
                        Find.World.info.initialMapSize, existing, MapGeneratorDefOf.Encounter, null, null);
                }

                // Non-Ambush MapParent on tile: do not GenerateMap onto it (would risk wiping a home).
                if (Prefs.DevMode)
                    Log.Message($"[TSA WD] Aborting caravan clash map gen: non-Ambush MapParent ({existing.def?.defName}) on tile {tile}");
                return null;
            }

            MapParent site = CreateEncounterSite(tile);
            return MapGenerator.GenerateMap(Find.World.info.initialMapSize, site, MapGeneratorDefOf.Encounter, null, null);
        }

        private static bool IsTemporaryClashAmbushMap(Map map) =>
            map?.Parent != null && !map.Parent.Destroyed && map.Parent.def == WorldObjectDefOf.Ambush;

        private static MapParent CreateEncounterSite(PlanetTile tile)
        {
            MapParent site = (MapParent)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Ambush);
            site.Tile = tile;
            site.SetFaction(null);
            Find.WorldObjects.Add(site);
            return site;
        }

        private static void StopBothForEncounter(Caravan caravan, WorldObject_Traveler traveler)
        {
            traveler?.pather?.StopDead();
            caravan?.pather?.StopDead();
        }

        /// <summary>Encounter map gen failed or was cancelled — resume route instead of leaving a frozen traveler.</summary>
        internal static void ResumeTravelerAfterFailedClashEncounter(WorldObject_Traveler traveler)
        {
            if (traveler == null || traveler.Destroyed) return;
            WD_PathFollower pather = traveler.pather;
            if (pather == null || !pather.destTile.Valid) return;
            if (pather.moving) return;

            pather.StartPath(pather.destTile, skipLaunchTravelCache: true);
            if (!pather.moving && !traveler.Destroyed)
            {
                var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
                TravelerEndpointUtility.AbortTraveler(
                    traveler,
                    "TSA_WD_Log_TravelerAborted_Stranded".Translate(traveler.Label),
                    manager);
            }
        }
    }
}
