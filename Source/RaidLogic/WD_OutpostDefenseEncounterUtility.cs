using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public static class WD_OutpostDefenseEncounterUtility
    {
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

                List<Pawn> spawnedDefenders = SpawnDefenders(map, defenders, out List<Pawn> failedDefenders);
                for (int i = 0; i < failedDefenders.Count; i++)
                    outpost.AddPawn(failedDefenders[i], null!);
                List<Pawn> spawnedStoredTransport = SpawnDefenders(map, storedTransport, out List<Pawn> failedStoredTransport);
                outpost.ReturnManualDefenseStoredTransportPawns(failedStoredTransport);
                List<Pawn> spawnedMechanoids = SpawnDefenders(map, mechanoids, out List<Pawn> failedMechanoids);
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

                WD_MapComponent_OutpostDefense tracker = GetOrAddTracker(map);
                tracker.BeginEncounter(traveler, outpost, spawnedDefenders, spawnedStoredTransport, spawnedMechanoids);

                float points = ComputeRaidPoints(traveler, map);
                tracker.ScheduleRaidArrival(points, arrivalDelay);

                string outpostLabel = outpost.LabelCap;
                string factionName = traveler.Faction?.Name ?? "Unknown";
                GlobalTargetInfo lookTarget = new GlobalTargetInfo(map.Center, map);
                LongEventHandler.ExecuteWhenFinished(delegate
                {
                    if (arrivalDelay <= 0)
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

        private static List<Pawn> SpawnDefenders(Map map, List<Pawn> defenders, out List<Pawn> failed)
        {
            var spawned = new List<Pawn>();
            failed = new List<Pawn>();
            if (defenders == null) return spawned;
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

                    IntVec3 cell = FindDefenderSpawnCell(map);
                    GenSpawn.Spawn(pawn, cell, map);
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

        public static void ExecuteRaidIncident(Map map, WorldObject_Traveler traveler, float points)
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

            if (traveler != null && traveler.mission == TravelerMission.RaidDropPod)
                parms.raidArrivalMode = Rand.Bool ? PawnsArrivalModeDefOf.CenterDrop : PawnsArrivalModeDefOf.EdgeDrop;
            else
                parms.raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn;

            Raid_OnPlayerColony.IsWorldDominationRaid = true;
            try
            {
                IncidentDefOf.RaidEnemy.Worker.TryExecute(parms);
            }
            finally
            {
                Raid_OnPlayerColony.IsWorldDominationRaid = false;
            }
        }
    }
}
