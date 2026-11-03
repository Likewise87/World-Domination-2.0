using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public static class WD_OutpostDefenseEncounterUtility
    {
        private const int FootprintClearanceCells = 2;
        private const int OutsideWallRaidDelayTicks = 600;
        /// <summary>
        /// Chebyshev standoff from trench corridor cells for VF / multi-cell footprints.
        /// Kept tight behind the trench; footprint checks still clear wire/traps.
        /// </summary>
        private const int TrenchVehicleCorridorStandoff = 3;

        public static bool StartManualDefenseEncounter(WorldObject_Traveler traveler, WorldObject_WD_Outpost outpost, int raidArrivalDelayTicks = 900)
            => StartManualDefenseEncounter(traveler, outpost, raidArrivalDelayTicks, onlyTheseOccupants: null, supportSelections: null);

        public static bool StartManualDefenseEncounter(
            WorldObject_Traveler traveler,
            WorldObject_WD_Outpost outpost,
            int raidArrivalDelayTicks,
            IReadOnlyList<Pawn> onlyTheseOccupants)
            => StartManualDefenseEncounter(traveler, outpost, raidArrivalDelayTicks, onlyTheseOccupants, supportSelections: null);

        /// <summary>
        /// Start manual defense. <paramref name="supportSelections"/> maps supporting player outposts to selected occupants
        /// (player-only; extracted without ManualDefenseActive on donors).
        /// </summary>
        public static bool StartManualDefenseEncounter(
            WorldObject_Traveler traveler,
            WorldObject_WD_Outpost outpost,
            int raidArrivalDelayTicks,
            IReadOnlyList<Pawn> onlyTheseOccupants,
            IReadOnlyList<KeyValuePair<WorldObject_WD_Outpost, List<Pawn>>> supportSelections)
            => StartManualDefenseEncounter(traveler, outpost, raidArrivalDelayTicks, onlyTheseOccupants, supportSelections, useTrenchCorridorMap: false);

        public static bool StartManualDefenseEncounter(
            WorldObject_Traveler traveler,
            WorldObject_WD_Outpost outpost,
            int raidArrivalDelayTicks,
            IReadOnlyList<Pawn> onlyTheseOccupants,
            IReadOnlyList<KeyValuePair<WorldObject_WD_Outpost, List<Pawn>>> supportSelections,
            bool useTrenchCorridorMap)
        {
            if (traveler == null || outpost == null || outpost.Destroyed || !outpost.HasLivingManualDefensePawns())
                return false;

            // null onlyThese = legacy "take everyone"; non-null = dialog selection (incl. empty = none).
            bool selectionMode = onlyTheseOccupants != null;

            List<Pawn> primaryDefenders = outpost.ExtractManualDefensePawns(onlyTheseOccupants);
            var allDefenders = new List<Pawn>();
            var defenderHomes = new List<WorldObject_WD_Outpost>();
            if (primaryDefenders != null)
            {
                for (int i = 0; i < primaryDefenders.Count; i++)
                {
                    allDefenders.Add(primaryDefenders[i]);
                    defenderHomes.Add(outpost);
                }
            }

            var storedTransport = new List<Pawn>();
            var transportHomes = new List<WorldObject_WD_Outpost>();
            var mechanoids = new List<Pawn>();
            var mechanoidHomes = new List<WorldObject_WD_Outpost>();

            List<Pawn> targetTransport = selectionMode
                ? outpost.ExtractManualDefenseStoredTransportPawns(onlyTheseOccupants)
                : outpost.ExtractManualDefenseStoredTransportPawns();
            AppendWithHome(targetTransport, outpost, storedTransport, transportHomes);

            List<Pawn> targetMechs = selectionMode
                ? outpost.ExtractManualDefenseMechanoids(onlyTheseOccupants)
                : outpost.ExtractManualDefenseMechanoids();
            AppendWithHome(targetMechs, outpost, mechanoids, mechanoidHomes);

            var supportDonors = new List<WorldObject_WD_Outpost>();
            if (supportSelections != null)
            {
                for (int s = 0; s < supportSelections.Count; s++)
                {
                    WorldObject_WD_Outpost donor = supportSelections[s].Key;
                    List<Pawn> wanted = supportSelections[s].Value;
                    if (donor == null || donor.Destroyed || donor == outpost || donor.ManualDefenseActive) continue;
                    if (wanted == null || wanted.Count == 0) continue;

                    List<Pawn> extractedOcc = donor.ExtractDefenseSupportPawns(wanted);
                    bool anyFromDonor = false;
                    if (extractedOcc != null)
                    {
                        for (int i = 0; i < extractedOcc.Count; i++)
                        {
                            allDefenders.Add(extractedOcc[i]);
                            defenderHomes.Add(donor);
                            anyFromDonor = true;
                        }
                    }

                    List<Pawn> donorTransport = donor.ExtractManualDefenseStoredTransportPawns(wanted);
                    if (donorTransport != null && donorTransport.Count > 0)
                    {
                        AppendWithHome(donorTransport, donor, storedTransport, transportHomes);
                        anyFromDonor = true;
                    }

                    List<Pawn> donorMechs = donor.ExtractManualDefenseMechanoids(wanted);
                    if (donorMechs != null && donorMechs.Count > 0)
                    {
                        AppendWithHome(donorMechs, donor, mechanoids, mechanoidHomes);
                        anyFromDonor = true;
                    }

                    if (anyFromDonor)
                        supportDonors.Add(donor);
                }
            }

            if (allDefenders.Count == 0 && storedTransport.Count == 0 && mechanoids.Count == 0)
                return false;

            // Occupant extract sets the flag; vehicle/mech-only deploy must set it too.
            if (!outpost.ManualDefenseActive)
                outpost.SetManualDefenseActive();

            int arrivalDelay = Mathf.Max(0, raidArrivalDelayTicks);
            // Trench corridor fights happen on the intercept arm tile; keep the fight site/icon there
            // (not on the owning outpost hub). Normal outpost defense stays on the outpost tile.
            PlanetTile encounterTile = outpost.Tile;
            if (useTrenchCorridorMap && traveler != null)
                encounterTile = traveler.Tile;

            LongEventHandler.QueueLongEvent(delegate
            {
                int hostileFrom = useTrenchCorridorMap && traveler != null
                    ? GenStep_WD_BridgeClash.CameFromWorldTile(traveler)
                    : -1;
                // Player approach = owning outpost hub (same role as caravan CameFrom on bridge clashes).
                int playerFrom = useTrenchCorridorMap && outpost != null && !outpost.Destroyed
                    ? outpost.Tile.tileId
                    : -1;
                Map map = GenerateDedicatedEncounterMap(
                    encounterTile,
                    useTrenchCorridorMap,
                    outpost,
                    playerFromTile: playerFrom,
                    hostileFromTile: hostileFrom);
                if (map == null)
                {
                    ReturnExtractedDefendersByHome(allDefenders, allDefenders, defenderHomes, outpost);
                    ReturnStoredByHome(storedTransport, storedTransport, transportHomes, outpost, mechanoids: false);
                    ReturnStoredByHome(mechanoids, mechanoids, mechanoidHomes, outpost, mechanoids: true);
                    outpost.ClearManualDefenseActive();
                    Messages.Message("TSA_WD_OutpostDefense_MapFailed".Translate(outpost.LabelCap), MessageTypeDefOf.NegativeEvent);
                    return;
                }

                if (!useTrenchCorridorMap)
                    WD_OutpostDefenseStructureSpawner.SpawnDefenses(map, outpost);
                else
                    GenStep_WD_TrenchClash.EnsureTrenchMapTurrets(map, outpost);

                // Vehicles first so large footprints claim clear ground before colonists fill the courtyard.
                List<Pawn> spawnedStoredTransport = SpawnDefenders(
                    map, storedTransport, out List<Pawn> failedStoredTransport, out bool anyTransportOutsideWalls);
                ReturnStoredByHome(failedStoredTransport, storedTransport, transportHomes, outpost, mechanoids: false);

                List<Pawn> spawnedDefenders = SpawnDefenders(map, allDefenders, out List<Pawn> failedDefenders, out _);
                ReturnExtractedDefendersByHome(failedDefenders, allDefenders, defenderHomes, outpost);

                // Keep homes aligned with successfully spawned defenders / stores.
                var spawnedHomes = new List<WorldObject_WD_Outpost>(spawnedDefenders.Count);
                for (int i = 0; i < spawnedDefenders.Count; i++)
                    spawnedHomes.Add(FindHomeForPawn(spawnedDefenders[i], allDefenders, defenderHomes, outpost));

                List<Pawn> spawnedMechanoids = SpawnDefenders(map, mechanoids, out List<Pawn> failedMechanoids, out _);
                ReturnStoredByHome(failedMechanoids, mechanoids, mechanoidHomes, outpost, mechanoids: true);

                var spawnedTransportHomes = new List<WorldObject_WD_Outpost>(spawnedStoredTransport.Count);
                for (int i = 0; i < spawnedStoredTransport.Count; i++)
                    spawnedTransportHomes.Add(FindHomeForPawn(spawnedStoredTransport[i], storedTransport, transportHomes, outpost));

                var spawnedMechHomes = new List<WorldObject_WD_Outpost>(spawnedMechanoids.Count);
                for (int i = 0; i < spawnedMechanoids.Count; i++)
                    spawnedMechHomes.Add(FindHomeForPawn(spawnedMechanoids[i], mechanoids, mechanoidHomes, outpost));

                if (spawnedDefenders.Count == 0 && spawnedStoredTransport.Count == 0 && spawnedMechanoids.Count == 0)
                {
                    ReturnStoredByHome(spawnedStoredTransport, storedTransport, transportHomes, outpost, mechanoids: false);
                    ReturnStoredByHome(spawnedMechanoids, mechanoids, mechanoidHomes, outpost, mechanoids: true);
                    outpost.ClearManualDefenseActive();
                    MapParent parent = map.Parent;
                    if (Current.Game.Maps.Contains(map))
                        Current.Game.DeinitAndRemoveMap(map, false);
                    if (parent != null && !parent.Destroyed)
                        parent.Destroy();
                    Messages.Message("TSA_WD_OutpostDefense_MapFailed".Translate(outpost.LabelCap), MessageTypeDefOf.NegativeEvent);
                    return;
                }

                if (anyTransportOutsideWalls)
                {
                    arrivalDelay += OutsideWallRaidDelayTicks;
                    if (Prefs.DevMode)
                    {
                        Log.Message("[TSA WD] Outpost defense: stored transport spawned outside wall ring; "
                            + $"attacker delay +{OutsideWallRaidDelayTicks / 60f:0}s (total {arrivalDelay / 60f:0.#}s).");
                    }
                }

                WD_MapComponent_OutpostDefense tracker = GetOrAddTracker(map);
                tracker.BeginEncounter(
                    traveler,
                    outpost,
                    spawnedDefenders,
                    spawnedHomes,
                    spawnedStoredTransport,
                    spawnedTransportHomes,
                    spawnedMechanoids,
                    spawnedMechHomes,
                    supportDonors);
                PlayerWdRaidDispatchUtil.NotifyPlayerContact(traveler);

                float points = ComputeRaidPoints(traveler, map);
                tracker.ScheduleRaidArrival(points, arrivalDelay);

                // Same contract as normal outpost arrival (WorldActions_Traveler destroys after Choice opens):
                // tracker holds a snapshot; leave no live world column for TravelerMotionGuard to repath.
                // Trench intercept Manual previously left StopDead travelers alive → they resumed after victory.
                if (traveler != null && !traveler.Destroyed)
                {
                    traveler.suppressDestroyedWorldFx = true;
                    traveler.Destroy();
                }

                bool notifyArrival = WorldDominationMod.settings?.notifyRaidArrivalOutpost
                    ?? WorldDominationSettings.DefNotifyRaidArrivalOutpost;
                if (notifyArrival)
                {
                    string outpostLabel = outpost.LabelCap;
                    string factionName = traveler.Faction?.Name ?? "Unknown";
                    GlobalTargetInfo lookTarget = new GlobalTargetInfo(map.Center, map);
                    int letterDelay = arrivalDelay;
                    LongEventHandler.ExecuteWhenFinished(delegate
                    {
                        if (letterDelay <= 0)
                        {
                            Find.LetterStack.ReceiveLetter(
                                "TSA_WD_OutpostDefense_LetterImmediate_Label".Translate(),
                                "TSA_WD_OutpostDefense_LetterImmediate_Text".Translate(outpostLabel, factionName),
                                LetterDefOf.ThreatBig,
                                lookTarget);
                        }
                        else
                        {
                            Find.LetterStack.ReceiveLetter(
                                "TSA_WD_OutpostDefense_Letter_Label".Translate(),
                                "TSA_WD_OutpostDefense_Letter_Text".Translate(outpostLabel, factionName),
                                LetterDefOf.ThreatBig,
                                lookTarget);
                        }
                    });
                }
            }, "GeneratingArea", true, null);

            return true;
        }

        private static Map GenerateDedicatedEncounterMap(
            PlanetTile tile,
            bool useTrenchCorridorMap = false,
            WorldObject_WD_Outpost defendingOutpost = null,
            int playerFromTile = -1,
            int hostileFromTile = -1)
        {
            WorldObjectDef siteDef = DefDatabase<WorldObjectDef>.GetNamedSilentFail("TSA_WD_OutpostDefenseSite");
            if (siteDef == null)
            {
                Log.Error("[TSA WD] Missing WorldObjectDef TSA_WD_OutpostDefenseSite; cannot start manual outpost defense.");
                return null;
            }

            MapParent site = (MapParent)WorldObjectMaker.MakeWorldObject(siteDef);
            site.Tile = tile;
            MapGeneratorDef generator = MapGeneratorDefOf.Encounter;
            if (useTrenchCorridorMap)
            {
                MapGeneratorDef trenchGen = DefDatabase<MapGeneratorDef>.GetNamedSilentFail("WD_TrenchClash");
                if (trenchGen != null)
                    generator = trenchGen;
            }
            else
            {
                MapGeneratorDef kcsgGenerator = DefDatabase<MapGeneratorDef>.GetNamedSilentFail("KCSG_Base_Faction");
                if (kcsgGenerator != null && Faction.OfPlayer?.def != null && KCSG_Integration_Patch.EnsureKcsgCustomGenOption(Faction.OfPlayer.def))
                {
                    site.SetFaction(Faction.OfPlayer);
                    generator = kcsgGenerator;
                }
                else
                {
                    site.SetFaction(null);
                }
            }
            if (site.Faction == null && useTrenchCorridorMap)
                site.SetFaction(null);
            Find.WorldObjects.Add(site);

            if (useTrenchCorridorMap)
            {
                WorldObject_WD_Outpost pending = defendingOutpost
                    ?? GenStep_WD_TrenchClash.ResolveDefendingOutpost(tile);
                GenStep_WD_TrenchClash.PushDefendingOutpost(pending);
                GenStep_WD_TrenchClash.PushEncounterApproaches(playerFromTile, hostileFromTile);
            }
            try
            {
                return MapGenerator.GenerateMap(Find.World.info.initialMapSize, site, generator, null, null);
            }
            finally
            {
                if (useTrenchCorridorMap)
                {
                    GenStep_WD_TrenchClash.ClearDefendingOutpost();
                    GenStep_WD_TrenchClash.ClearEncounterApproaches();
                }
            }
        }

        private static WD_MapComponent_OutpostDefense GetOrAddTracker(Map map)
        {
            WD_MapComponent_OutpostDefense tracker = map.GetComponent<WD_MapComponent_OutpostDefense>();
            if (tracker == null)
            {
                tracker = new WD_MapComponent_OutpostDefense(map);
                map.components.Add(tracker);
            }
            return tracker;
        }

        /// <summary>
        /// Nearby player WD outposts eligible to lend occupants (same ally radius as auto-resolve; player outposts only).
        /// Snapshot immediately — <see cref="Raid_ReinforcementLogic.GetReinforcements"/> reuses a scratch list.
        /// </summary>
        public static List<WorldObject_WD_Outpost> CollectPlayerSupportOutposts(
            WorldObject_WD_Outpost target,
            WorldObject attacker,
            WorldComponent_SpreadManager manager)
        {
            var result = new List<WorldObject_WD_Outpost>();
            if (target == null || target.Destroyed) return result;
            var seth = WorldDominationMod.settings;
            var lookup = WorldActions_Utils.GetWorldObjectsWithCompByFaction();
            Faction attackerFaction = attacker?.Faction;
            var excluded = attackerFaction != null ? new List<Faction> { attackerFaction } : null;
            float radius = AllyRadiusUtil.GetEffective(target, seth, manager);
            var raw = Raid_ReinforcementLogic.GetReinforcements(target, attacker, radius, lookup, manager, excluded);
            var allies = new List<WorldObject>(raw);
            for (int i = 0; i < allies.Count; i++)
            {
                if (allies[i] is not WorldObject_WD_Outpost donor) continue;
                if (donor.Destroyed || donor == target) continue;
                if (donor.Faction != Faction.OfPlayer) continue;
                if (donor.ManualDefenseActive) continue;
                result.Add(donor);
            }
            return result;
        }

        private static void AppendWithHome(
            List<Pawn> extracted,
            WorldObject_WD_Outpost home,
            List<Pawn> intoPawns,
            List<WorldObject_WD_Outpost> intoHomes)
        {
            if (extracted == null || extracted.Count == 0) return;
            for (int i = 0; i < extracted.Count; i++)
            {
                intoPawns.Add(extracted[i]);
                intoHomes.Add(home);
            }
        }

        private static WorldObject_WD_Outpost FindHomeForPawn(
            Pawn pawn,
            List<Pawn> allDefenders,
            List<WorldObject_WD_Outpost> defenderHomes,
            WorldObject_WD_Outpost fallback)
        {
            if (pawn == null || allDefenders == null || defenderHomes == null) return fallback;
            for (int i = 0; i < allDefenders.Count; i++)
            {
                if (allDefenders[i] != pawn) continue;
                if (i < defenderHomes.Count && defenderHomes[i] != null && !defenderHomes[i].Destroyed)
                    return defenderHomes[i];
                return fallback;
            }
            return fallback;
        }

        private static void ReturnExtractedDefendersByHome(
            List<Pawn> pawns,
            List<Pawn> allDefenders,
            List<WorldObject_WD_Outpost> defenderHomes,
            WorldObject_WD_Outpost fallbackHome)
        {
            if (pawns == null || pawns.Count == 0) return;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn == null || pawn.Destroyed || pawn.Dead) continue;
                WorldObject_WD_Outpost home = FindHomeForPawn(pawn, allDefenders, defenderHomes, fallbackHome);
                if (home == null || home.Destroyed)
                    home = fallbackHome;
                if (home == null || home.Destroyed) continue;

                pawn.GetCaravan()?.RemovePawn(pawn);
                if (pawn.Spawned) pawn.DeSpawn();
                Outpost_OccupantProgression.TryRefreshOccupantHealthState(pawn);
                if (home == fallbackHome)
                    home.AddPawn(pawn, null!);
                else
                    home.ReturnDefenseSupportPawns(new List<Pawn> { pawn });
            }
        }

        private static void ReturnStoredByHome(
            List<Pawn> pawns,
            List<Pawn> allStored,
            List<WorldObject_WD_Outpost> homes,
            WorldObject_WD_Outpost fallbackHome,
            bool mechanoids)
        {
            if (pawns == null || pawns.Count == 0) return;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn == null || pawn.Destroyed || pawn.Dead) continue;
                WorldObject_WD_Outpost home = FindHomeForPawn(pawn, allStored, homes, fallbackHome);
                if (home == null || home.Destroyed)
                    home = fallbackHome;
                if (home == null || home.Destroyed) continue;

                if (pawn.Spawned) pawn.DeSpawn();
                if (mechanoids)
                    home.ReturnManualDefenseMechanoids(new List<Pawn> { pawn });
                else
                    home.ReturnManualDefenseStoredTransportPawns(new List<Pawn> { pawn });
            }
        }

        private static List<Pawn> SpawnDefenders(
            Map map,
            List<Pawn> defenders,
            out List<Pawn> failed,
            out bool anySpawnedOutsideWallRing)
        {
            var spawned = new List<Pawn>();
            failed = new List<Pawn>();
            anySpawnedOutsideWallRing = false;
            if (defenders == null) return spawned;

            IntVec3 settlementCenter = WD_OutpostDefenseMapUtility.GetSettlementCenter(map);
            bool trenchMap = map.GetComponent<MapComponent_WdTrenchClashLayout>() != null;

            for (int i = 0; i < defenders.Count; i++)
            {
                Pawn pawn = defenders[i];
                if (pawn == null || pawn.Destroyed || pawn.Dead) continue;
                try
                {
                    if (pawn.Spawned) pawn.DeSpawn();
                    pawn.holdingOwner?.Remove(pawn);
                    if (pawn.Faction != Faction.OfPlayer)
                        pawn.SetFaction(Faction.OfPlayer);

                    IntVec3 cell;
                    Rot4 rot = Rot4.North;
                    bool footprint = NeedsFootprintSpawn(pawn);
                    if (footprint)
                    {
                        if (!TryFindFootprintSpawn(map, pawn, settlementCenter, out cell, out rot))
                        {
                            if (Prefs.DevMode)
                            {
                                IntVec2 size = pawn.def?.Size ?? IntVec2.One;
                                Log.Warning($"[TSA WD] No clear footprint (+{FootprintClearanceCells} pad) for "
                                    + $"{pawn.LabelShortCap} size={size.x}x{size.z}; returning to outpost storage.");
                            }
                            failed.Add(pawn);
                            continue;
                        }
                        GenSpawn.Spawn(pawn, cell, map, rot);
                    }
                    else
                    {
                        cell = FindDefenderSpawnCell(map);
                        GenSpawn.Spawn(pawn, cell, map);
                        // Colonists placed into the trench start drafted; vehicles stay outside undrafted.
                        if (trenchMap)
                            GenStep_WD_TrenchClash.TryDraftTrenchColonist(pawn);
                    }

                    if (footprint
                        && Chebyshev(cell, settlementCenter) > WD_OutpostDefenseStructureSpawner.WallRingRadius)
                        anySpawnedOutsideWallRing = true;

                    spawned.Add(pawn);
                }
                catch (System.Exception ex)
                {
                    Log.Warning($"[TSA WD] Could not spawn manual outpost defense pawn {pawn.LabelShortCap}: {ex.Message}");
                    failed.Add(pawn);
                }
            }
            return spawned;
        }

        private static bool NeedsFootprintSpawn(Pawn pawn)
        {
            if (pawn == null) return false;
            if (VehicleFrameworkOutpostDissolveCompat.IsVehicleFrameworkVehiclePawn(pawn))
                return true;
            IntVec2 size = pawn.def?.Size ?? IntVec2.One;
            return size.x * size.z > 1;
        }

        private static bool TryFindFootprintSpawn(
            Map map,
            Pawn pawn,
            IntVec3 settlementCenter,
            out IntVec3 cell,
            out Rot4 rot)
        {
            cell = IntVec3.Invalid;
            rot = Rot4.North;
            if (map == null || pawn?.def == null) return false;

            IntVec2 size = pawn.def.Size;
            int halfMax = Mathf.Max(size.x, size.z) / 2;
            int pad = FootprintClearanceCells;
            int wallR = WD_OutpostDefenseStructureSpawner.WallRingRadius;
            int outerR = WD_OutpostDefenseStructureSpawner.TankTrapRingRadius;

            // Trench defense: vehicles stay outside the corridor (past wire / tank-trap belt).
            var trench = map.GetComponent<MapComponent_WdTrenchClashLayout>();
            if (trench?.CorridorCells != null && trench.CorridorCells.Count > 0)
                return TryFindFootprintOutsideTrench(map, settlementCenter, size, pad, trench, out cell, out rot);

            if (TryFindFootprintInRect(
                    map, settlementCenter, size, pad,
                    WD_OutpostDefenseStructureSpawner.GetInnerClearRect(map),
                    minChebyshev: 0,
                    maxChebyshev: int.MaxValue,
                    tries: 400,
                    out cell, out rot))
                return true;

            int insideMax = Mathf.Max(0, wallR - halfMax - pad - 1);
            CellRect insideRect = CellRect.CenteredOn(settlementCenter, insideMax * 2 + 1).ClipInsideMap(map);
            if (insideMax > 0
                && TryFindFootprintInRect(
                    map, settlementCenter, size, pad, insideRect,
                    minChebyshev: 0,
                    maxChebyshev: insideMax,
                    tries: 600,
                    out cell, out rot))
                return true;

            int outsideMin = outerR + halfMax + pad + 1;
            CellRect whole = CellRect.WholeMap(map);
            if (TryFindFootprintInRect(
                    map, settlementCenter, size, pad, whole,
                    minChebyshev: outsideMin,
                    maxChebyshev: int.MaxValue,
                    tries: 900,
                    out cell, out rot))
                return true;

            return TryFindFootprintInRect(
                map, settlementCenter, size, pad, whole,
                minChebyshev: 0,
                maxChebyshev: int.MaxValue,
                tries: 1200,
                out cell, out rot);
        }

        /// <summary>
        /// Clear multi-cell spawn past trench corridor + prop belt (wire / VVE traps), preferring the
        /// map half opposite the hostile edge so vehicles sit behind the trench line.
        /// </summary>
        private static bool TryFindFootprintOutsideTrench(
            Map map,
            IntVec3 settlementCenter,
            IntVec2 size,
            int pad,
            MapComponent_WdTrenchClashLayout trench,
            out IntVec3 cell,
            out Rot4 rot)
        {
            cell = IntVec3.Invalid;
            rot = Rot4.North;
            HashSet<IntVec3> avoidStandoff = BuildChebyshevExpandedSet(
                map, trench.CorridorCells, TrenchVehicleCorridorStandoff);
            HashSet<IntVec3> avoidCorridorOnly = BuildChebyshevExpandedSet(map, trench.CorridorCells, 0);

            CellRect prefer = GetTrenchVehiclePreferRect(map, trench);
            IntVec3 preferCenter = prefer.CenterCell;
            if (TryFindFootprintInRect(
                    map, preferCenter, size, pad, prefer,
                    minChebyshev: 0,
                    maxChebyshev: int.MaxValue,
                    tries: 800,
                    out cell, out rot,
                    avoidStandoff))
                return true;

            CellRect whole = CellRect.WholeMap(map);
            if (TryFindFootprintInRect(
                    map, settlementCenter, size, pad, whole,
                    minChebyshev: 0,
                    maxChebyshev: int.MaxValue,
                    tries: 1500,
                    out cell, out rot,
                    avoidStandoff))
                return true;

            // Last resort: still never on the corridor itself; FootprintAndPadClear rejects wire/traps.
            return TryFindFootprintInRect(
                map, settlementCenter, size, pad, whole,
                minChebyshev: 0,
                maxChebyshev: int.MaxValue,
                tries: 1500,
                out cell, out rot,
                avoidCorridorOnly);
        }

        private static CellRect GetTrenchVehiclePreferRect(Map map, MapComponent_WdTrenchClashLayout trench)
        {
            CellRect whole = CellRect.WholeMap(map);
            int margin = 2;
            int w = Mathf.Max(1, whole.Width - margin * 2);
            int h = Mathf.Max(1, whole.Height - margin * 2);
            int halfW = Mathf.Max(1, w / 2);
            int halfH = Mathf.Max(1, h / 2);
            var o = (GenStep_WD_TrenchClash.WdOctant)Mathf.Clamp(trench.HostileOctant, 0, 7);

            // Prefer the half opposite the hostile approach (player rear / behind the trench).
            switch (o)
            {
                case GenStep_WD_TrenchClash.WdOctant.North:
                case GenStep_WD_TrenchClash.WdOctant.NorthEast:
                case GenStep_WD_TrenchClash.WdOctant.NorthWest:
                    return new CellRect(whole.minX + margin, whole.minZ + margin, w, halfH);
                case GenStep_WD_TrenchClash.WdOctant.South:
                case GenStep_WD_TrenchClash.WdOctant.SouthEast:
                case GenStep_WD_TrenchClash.WdOctant.SouthWest:
                    return new CellRect(whole.minX + margin, whole.maxZ - halfH - margin + 1, w, halfH);
                case GenStep_WD_TrenchClash.WdOctant.East:
                    return new CellRect(whole.minX + margin, whole.minZ + margin, halfW, h);
                case GenStep_WD_TrenchClash.WdOctant.West:
                default:
                    return new CellRect(whole.maxX - halfW - margin + 1, whole.minZ + margin, halfW, h);
            }
        }

        private static HashSet<IntVec3> BuildChebyshevExpandedSet(Map map, List<IntVec3> seeds, int buffer)
        {
            var set = new HashSet<IntVec3>();
            if (map == null || seeds == null || seeds.Count == 0) return set;
            buffer = Mathf.Max(0, buffer);
            for (int i = 0; i < seeds.Count; i++)
            {
                IntVec3 s = seeds[i];
                if (!s.InBounds(map)) continue;
                if (buffer == 0)
                {
                    set.Add(s);
                    continue;
                }
                for (int dx = -buffer; dx <= buffer; dx++)
                {
                    for (int dz = -buffer; dz <= buffer; dz++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) > buffer) continue;
                        IntVec3 c = new IntVec3(s.x + dx, 0, s.z + dz);
                        if (c.InBounds(map))
                            set.Add(c);
                    }
                }
            }
            return set;
        }

        private static bool TryFindFootprintInRect(
            Map map,
            IntVec3 settlementCenter,
            IntVec2 size,
            int pad,
            CellRect searchRect,
            int minChebyshev,
            int maxChebyshev,
            int tries,
            out IntVec3 cell,
            out Rot4 rot,
            HashSet<IntVec3> avoidCells = null)
        {
            cell = IntVec3.Invalid;
            rot = Rot4.North;
            if (searchRect.Area <= 0) return false;

            Rot4[] rotations = { Rot4.North, Rot4.East, Rot4.South, Rot4.West };
            for (int i = 0; i < tries; i++)
            {
                IntVec3 candidate = searchRect.RandomCell;
                if (!candidate.InBounds(map)) continue;
                int d = Chebyshev(candidate, settlementCenter);
                if (d < minChebyshev || d > maxChebyshev) continue;
                if (avoidCells != null && avoidCells.Contains(candidate)) continue;

                for (int r = 0; r < rotations.Length; r++)
                {
                    Rot4 tryRot = rotations[r];
                    if (!FootprintAndPadClear(map, candidate, tryRot, size, pad, avoidCells))
                        continue;
                    cell = candidate;
                    rot = tryRot;
                    return true;
                }
            }
            return false;
        }

        private static bool FootprintAndPadClear(
            Map map,
            IntVec3 cell,
            Rot4 rot,
            IntVec2 size,
            int pad,
            HashSet<IntVec3> avoidCells = null)
        {
            CellRect body = GenAdj.OccupiedRect(cell, rot, size);
            CellRect full = body.ExpandedBy(pad);
            foreach (IntVec3 c in full)
            {
                if (!c.InBounds(map))
                    return false;
                if (avoidCells != null && avoidCells.Contains(c))
                    return false;
                if (!IsClearSpawnCell(map, c))
                    return false;
            }
            return true;
        }

        private static bool IsClearSpawnCell(Map map, IntVec3 cell)
        {
            if (!cell.InBounds(map) || cell.Fogged(map))
                return false;
            if (!cell.Standable(map))
                return false;
            if (cell.GetFirstBuilding(map) != null)
                return false;
            return true;
        }

        private static int Chebyshev(IntVec3 a, IntVec3 b)
            => Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.z - b.z));

        private static IntVec3 FindDefenderSpawnCell(Map map)
        {
            var trench = map.GetComponent<MapComponent_WdTrenchClashLayout>();
            List<IntVec3> trenchPool = GenStep_WD_TrenchClash.GetPlayerSpawnPool(trench);
            if (trenchPool != null && trenchPool.Count > 0
                && GenStep_WD_TrenchClash.TryPickStandableTrenchCell(map, trenchPool, out IntVec3 trenchCell))
                return trenchCell;

            CellRect center = WD_OutpostDefenseStructureSpawner.GetInnerClearRect(map);
            for (int i = 0; i < 240; i++)
            {
                IntVec3 cell = center.RandomCell;
                if (cell.InBounds(map) && cell.Standable(map) && !cell.Fogged(map))
                    return cell;
            }

            CellRect fallback = CellRect.CenteredOn(WD_OutpostDefenseMapUtility.GetSettlementCenter(map), 14).ClipInsideMap(map);
            for (int i = 0; i < 240; i++)
            {
                IntVec3 cell = fallback.RandomCell;
                if (cell.InBounds(map) && cell.Standable(map) && !cell.Fogged(map))
                    return cell;
            }

            CellRect whole = CellRect.WholeMap(map);
            for (int i = 0; i < 720; i++)
            {
                IntVec3 cell = whole.RandomCell;
                if (cell.InBounds(map) && cell.Standable(map) && !cell.Fogged(map))
                    return cell;
            }

            return map.Center;
        }

        private static float ComputeRaidPoints(WorldObject_Traveler traveler, Map map)
        {
            float strength = Mathf.Max(0f, traveler?.travelerStrength ?? 0f);
            var seth = WorldDominationMod.settings;
            if (seth != null
                && (seth.alwaysUseStrengthAsRaidPoints || seth.alwaysUseStrengthAsOutpostDefenseRaidPoints))
                return strength;
            return RaidPointsHelper.ClampRaidPointsToStorytellerBand(strength, map);
        }

        /// <summary>
        /// Launch the outpost-defense raid incident. Returns false when neither the incident nor
        /// manual assault spawn produced hostiles (caller should abort without auto-victory).
        /// </summary>
        public static bool ExecuteRaidIncident(Map map, WorldObject_Traveler traveler, float points)
        {
            IncidentParms parms = new IncidentParms
            {
                target = map,
                points = points,
                faction = traveler.Faction,
                raidStrategy = RaidStrategyDefOf.ImmediateAttack,
                forced = true,
                silent = true,
                customLetterLabel = "TSA_WD_OutpostDefense_RaidLabel".Translate()
            };
            WdRaidParmsUtility.ApplyFightToLastRaidParms(parms);

            if (traveler != null && traveler.mission == TravelerMission.RaidGravship)
            {
                Raid_OnPlayerColony.IsWorldDominationRaid = true;
                try
                {
                    if (GravshipRaidsCompat.TryExecuteWdGravshipRaid(map, traveler.Faction, points))
                        return true;
                }
                finally
                {
                    Raid_OnPlayerColony.IsWorldDominationRaid = false;
                }
                // Fall through to drop-pod spawn if GR rejects the defense map.
                parms.raidArrivalMode = Rand.Bool ? PawnsArrivalModeDefOf.CenterDrop : PawnsArrivalModeDefOf.EdgeDrop;
            }
            else if (traveler != null && traveler.mission == TravelerMission.RaidDropPod)
                parms.raidArrivalMode = Rand.Bool ? PawnsArrivalModeDefOf.CenterDrop : PawnsArrivalModeDefOf.EdgeDrop;
            else
                parms.raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn;

            PawnsArrivalModeDef preferredArrival = parms.raidArrivalMode;
            WdRaidParmsUtility.EnsureFactionCompatibleRaidParms(parms, preferredArrival);

            // Trench clash: EdgeWalkIn hostiles from the polarity-matched map edge (bridge-style).
            if (parms.raidArrivalMode == null || parms.raidArrivalMode == PawnsArrivalModeDefOf.EdgeWalkIn)
            {
                int hostileFrom = GenStep_WD_BridgeClash.CameFromWorldTile(traveler);
                int playerFrom = -1;
                var trenchLayout = map.GetComponent<MapComponent_WdTrenchClashLayout>();
                if (trenchLayout != null)
                {
                    WorldObject_WD_Outpost owner = GenStep_WD_TrenchClash.ResolveDefendingOutpost(map.Tile);
                    if (owner != null && !owner.Destroyed)
                        playerFrom = owner.Tile.tileId;
                    GenStep_WD_TrenchClash.PreferHostileEdgeFromApproach(
                        map, trenchLayout, hostileFrom, playerFromTile: playerFrom);
                }
                if (GenStep_WD_TrenchClash.TryGetHostileEdgeSpawn(map, out IntVec3 trenchEdge, out Rot4 trenchRot))
                {
                    parms.spawnCenter = trenchEdge;
                    parms.spawnRotation = trenchRot;
                }
            }

            bool ok = false;
            Raid_OnPlayerColony.IsWorldDominationRaid = true;
            try
            {
                ok = IncidentDefOf.RaidEnemy.Worker.TryExecute(parms);
            }
            finally
            {
                Raid_OnPlayerColony.IsWorldDominationRaid = false;
            }

            if (!ok)
            {
                ok = WdRaidParmsUtility.TryManualAssaultSpawn(
                    map,
                    traveler?.Faction ?? parms.faction,
                    points,
                    parms.raidStrategy,
                    parms.customLetterLabel);
                if (Prefs.DevMode)
                {
                    Log.Warning("[TSA WD] Outpost defense raid incident failed; manual spawn fallback "
                        + (ok ? "succeeded" : "also failed")
                        + " faction=" + (parms.faction?.Name ?? "?")
                        + " strategy=" + (parms.raidStrategy?.defName ?? "(null)"));
                }
            }

            return ok;
        }
    }
}
