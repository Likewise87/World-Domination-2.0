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

        public static bool StartManualDefenseEncounter(WorldObject_Traveler traveler, WorldObject_WD_Outpost outpost, int raidArrivalDelayTicks = 900)
            => StartManualDefenseEncounter(traveler, outpost, raidArrivalDelayTicks, onlyTheseOccupants: null);

        public static bool StartManualDefenseEncounter(
            WorldObject_Traveler traveler,
            WorldObject_WD_Outpost outpost,
            int raidArrivalDelayTicks,
            IReadOnlyList<Pawn> onlyTheseOccupants)
        {
            if (traveler == null || outpost == null || outpost.Destroyed || !outpost.HasLivingManualDefensePawns())
                return false;

            List<Pawn> defenders = outpost.ExtractManualDefensePawns(onlyTheseOccupants);
            if (defenders == null || defenders.Count == 0)
                return false;
            List<Pawn> storedTransport = outpost.ExtractManualDefenseStoredTransportPawns();
            List<Pawn> mechanoids = outpost.ExtractManualDefenseMechanoids();
            int arrivalDelay = Mathf.Max(0, raidArrivalDelayTicks);

            LongEventHandler.QueueLongEvent(delegate
            {
                Map map = GenerateDedicatedEncounterMap(outpost.Tile);
                if (map == null)
                {
                    outpost.ReturnManualDefensePawns(defenders);
                    outpost.ReturnManualDefenseStoredTransportPawns(storedTransport);
                    outpost.ReturnManualDefenseMechanoids(mechanoids);
                    Messages.Message("TSA_WD_OutpostDefense_MapFailed".Translate(outpost.LabelCap), MessageTypeDefOf.NegativeEvent);
                    return;
                }

                WD_OutpostDefenseStructureSpawner.SpawnDefenses(map, outpost);

                // Vehicles first so large footprints claim clear ground before colonists fill the courtyard.
                List<Pawn> spawnedStoredTransport = SpawnDefenders(
                    map, storedTransport, out List<Pawn> failedStoredTransport, out bool anyTransportOutsideWalls);
                outpost.ReturnManualDefenseStoredTransportPawns(failedStoredTransport);

                List<Pawn> spawnedDefenders = SpawnDefenders(map, defenders, out List<Pawn> failedDefenders, out _);
                for (int i = 0; i < failedDefenders.Count; i++)
                    outpost.AddPawn(failedDefenders[i], null!);

                List<Pawn> spawnedMechanoids = SpawnDefenders(map, mechanoids, out List<Pawn> failedMechanoids, out _);
                outpost.ReturnManualDefenseMechanoids(failedMechanoids);

                if (spawnedDefenders.Count == 0)
                {
                    outpost.ReturnManualDefenseStoredTransportPawns(spawnedStoredTransport);
                    outpost.ReturnManualDefenseMechanoids(spawnedMechanoids);
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
                tracker.BeginEncounter(traveler, outpost, spawnedDefenders, spawnedStoredTransport, spawnedMechanoids);

                float points = ComputeRaidPoints(traveler, map);
                tracker.ScheduleRaidArrival(points, arrivalDelay);

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

        private static Map GenerateDedicatedEncounterMap(int tile)
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
            Find.WorldObjects.Add(site);
            return MapGenerator.GenerateMap(Find.World.info.initialMapSize, site, generator, null, null);
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
                    if (NeedsFootprintSpawn(pawn))
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
                    }

                    if (NeedsFootprintSpawn(pawn)
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
            out Rot4 rot)
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

                for (int r = 0; r < rotations.Length; r++)
                {
                    Rot4 tryRot = rotations[r];
                    if (!FootprintAndPadClear(map, candidate, tryRot, size, pad))
                        continue;
                    cell = candidate;
                    rot = tryRot;
                    return true;
                }
            }
            return false;
        }

        private static bool FootprintAndPadClear(Map map, IntVec3 cell, Rot4 rot, IntVec2 size, int pad)
        {
            CellRect body = GenAdj.OccupiedRect(cell, rot, size);
            CellRect full = body.ExpandedBy(pad);
            foreach (IntVec3 c in full)
            {
                if (!c.InBounds(map))
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
                customLetterLabel = "TSA_WD_OutpostDefense_RaidLabel".Translate(),
                canKidnap = false,
                canSteal = false
            };

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
