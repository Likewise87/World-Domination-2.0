using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    public class WD_MapComponent_CaravanClash : MapComponent
    {
        private static readonly List<Pawn> aboardScratch = new List<Pawn>();
        private static readonly HashSet<Pawn> aboardSeen = new HashSet<Pawn>();

        private WorldObjectDef travelerDef;
        private Faction enemyFaction;
        private float travelerStrength;
        private int destinationTileId = -1;
        private string travelerLabel;

        private TravelerMission savedMission = TravelerMission.Expansion;
        private WorldObject savedOrigin;
        private WorldObject savedTarget;
        private float savedInitialStrength;

        private bool encounterActive = false;
        private bool playerHasWon = false;
        private int startTick = -1;
        private bool leftoversDiscarded = false;
        // Legacy; scribe only — old loot dialog path removed; player uses vanilla reform caravan.
        private bool lootResolved = false;
        /// <summary>True when this clash map was opened on a tile that still has a player AT Turret.</summary>
        private bool foughtOnPlayerAtTurret;

        /// <summary>True only while the interception raid incident is executing; not saved.</summary>
        public bool InterceptionRaidPending { get; set; }

        /// <summary>For ForceRaidDirection / other mods: do not steer this raid; WD sets spawn or encounter is active.</summary>
        public bool ShouldSkipExternalRaidSteering => InterceptionRaidPending || encounterActive;

        public WD_MapComponent_CaravanClash(Map map) : base(map) { }

        /// <summary>True when this tile already has a loaded temporary Ambush map hosting a WD caravan clash.</summary>
        public static bool TileHasBusyCaravanClashAmbush(PlanetTile tile)
        {
            if (!tile.Valid) return false;
            var maps = Current.Game?.Maps;
            if (maps == null) return false;
            for (int i = 0; i < maps.Count; i++)
            {
                Map m = maps[i];
                if (m == null || m.Tile != tile) continue;
                MapParent parent = m.Parent;
                if (parent == null || parent.Destroyed || parent.def != WorldObjectDefOf.Ambush) continue;
                if (m.GetComponent<WD_MapComponent_CaravanClash>() != null)
                    return true;
            }
            return false;
        }

        public void StoreAndDestroyTraveler(WorldObject_Traveler traveler)
        {
            this.travelerDef = traveler.def;
            this.enemyFaction = traveler.Faction;
            this.travelerStrength = traveler.travelerStrength;
            this.destinationTileId = (traveler.pather != null && traveler.pather.destTile != PlanetTile.Invalid)
                ? traveler.pather.destTile.tileId
                : -1;
            this.travelerLabel = traveler.Label;
            this.savedMission = traveler.mission;
            this.savedOrigin = traveler.originObject;
            this.savedTarget = traveler.targetObject;
            this.savedInitialStrength = traveler.initialStrength;

            this.encounterActive = true;
            this.startTick = Find.TickManager.TicksGame;
            this.leftoversDiscarded = false;
            this.lootResolved = false;
            this.playerHasWon = false;
            this.foughtOnPlayerAtTurret = AtTurretUtility.TileHasPlayerAtTurret(map.Tile.tileId);

            Log.Message($"[TSA WD] Data saved for {travelerLabel}. Original destroyed.");
            traveler.Destroy();
        }

        public override void MapComponentTick()
        {
            if (playerHasWon)
                return;

            if (!encounterActive) return;

            if (Find.TickManager.TicksGame % 60 == 0)
                CheckEncounterState();
        }

        private void CheckEncounterState()
        {
            bool threatExists = savedMission == TravelerMission.Trader
                ? AnyLivingCaravanFactionPawnThreat()
                : GenHostility.AnyHostileActiveThreatToPlayer(map, true);
            bool playerStanding = AnyPlayerClashForceStanding();

            if (!threatExists && playerStanding)
            {
                if (Find.TickManager.TicksGame > startTick + 600)
                {
                    Log.Message($"[TSA WD] Victory detected for {travelerLabel}.");
                    playerHasWon = true;
                    encounterActive = false;
                    Messages.Message("TSA_WD_InterceptionVictory".Translate(), MessageTypeDefOf.PositiveEvent);
                }
                return;
            }

            if (!playerStanding && threatExists)
            {
                RespawnNewTraveler();
                ExecuteAllDownedPlayerPawns();
                DiscardEncounterLeftovers();
                QueueAmbushEncounterMapTeardown();
            }
        }

        /// <summary>
        /// Humanlike player fighters still in the fight: spawned colonists and VF crew aboard vehicles.
        /// Vehicle shells and animals do not count (avoids soft-lock defeat / false standing).
        /// </summary>
        private bool AnyPlayerClashForceStanding()
        {
            var spawned = map?.mapPawns?.AllPawnsSpawned;
            if (spawned == null) return false;

            for (int i = 0; i < spawned.Count; i++)
            {
                Pawn p = spawned[i];
                if (IsStandingPlayerHumanlike(p))
                    return true;
            }

            for (int i = 0; i < spawned.Count; i++)
            {
                Pawn vehicle = spawned[i];
                if (!VehicleFrameworkOutpostDissolveCompat.IsVehicleFrameworkVehiclePawn(vehicle))
                    continue;
                if (vehicle.Faction == null || !vehicle.Faction.IsPlayer)
                    continue;

                aboardScratch.Clear();
                aboardSeen.Clear();
                VehicleFrameworkOutpostDissolveCompat.CollectPawnsAboardVehicleForRoster(
                    vehicle, aboardScratch, aboardSeen);
                for (int j = 0; j < aboardScratch.Count; j++)
                {
                    if (IsStandingPlayerHumanlike(aboardScratch[j]))
                        return true;
                }
            }

            return false;
        }

        private static bool IsStandingPlayerHumanlike(Pawn p)
        {
            if (p == null || p.Destroyed || p.Dead || p.Downed) return false;
            if (VehicleFrameworkOutpostDissolveCompat.IsVehicleFrameworkVehiclePawn(p)) return false;
            if (p.RaceProps == null || !p.RaceProps.Humanlike) return false;
            if (p.Faction == null || !p.Faction.IsPlayer) return false;
            return true;
        }

        private bool AnyLivingCaravanFactionPawnThreat()
        {
            if (enemyFaction == null) return false;
            foreach (Pawn p in map.mapPawns.AllPawnsSpawned)
            {
                if (p.Dead || p.Downed) continue;
                if (p.Faction != enemyFaction) continue;
                return true;
            }

            return false;
        }

        private void ExecuteAllDownedPlayerPawns()
        {
            var allPawns = map.mapPawns.AllPawnsSpawned;
            for (int i = allPawns.Count - 1; i >= 0; i--)
            {
                var p = allPawns[i];
                if (p.Faction != null && p.Faction.IsPlayer && p.Downed && !p.Dead)
                    p.Kill(null);
            }
        }

        public override void MapRemoved()
        {
            base.MapRemoved();

            if (playerHasWon && !lootResolved)
                lootResolved = true;

            // Do not trust hostile pawn lists on a dying/empty map — wipe teardown often clears
            // pawns before MapRemoved, which previously abandoned the stored traveler.
            if (encounterActive && !playerHasWon)
                RespawnNewTraveler();

            DiscardEncounterLeftovers();
            DestroyAmbushParentIfPresent();

            Faction fac = enemyFaction;
            if (fac != null)
            {
                LongEventHandler.ExecuteWhenFinished(delegate
                {
                    DiscardOrphanEnemyFactionVehiclesFromWorldPawns(fac, mapStillAllowed: null);
                });
            }
        }

        private void RespawnNewTraveler()
        {
            if (!encounterActive || travelerDef == null) return;

            WorldObject_Traveler newTraveler = (WorldObject_Traveler)WorldObjectMaker.MakeWorldObject(travelerDef);
            newTraveler.Tile = map.Tile;
            newTraveler.SetFaction(enemyFaction);
            newTraveler.travelerStrength = this.travelerStrength;
            newTraveler.initialStrength = this.savedInitialStrength > 0f ? this.savedInitialStrength : this.travelerStrength;
            newTraveler.mission = savedMission;
            newTraveler.originObject = savedOrigin;
            newTraveler.targetObject = savedTarget;
            if (newTraveler.spawnTick == 0)
                newTraveler.spawnTick = Find.TickManager.TicksGame;

            Find.WorldObjects.Add(newTraveler);

            int destId = destinationTileId;
            if (destId < 0 && savedTarget != null && !savedTarget.Destroyed)
                destId = savedTarget.Tile.tileId;

            bool moving = false;
            if (destId >= 0 && newTraveler.pather != null)
            {
                newTraveler.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(destId, newTraveler));
                moving = newTraveler.pather.moving;
            }

            Log.Message($"[TSA WD] Defeat/Closure: {travelerLabel} recreated on world map dest={destId} moving={moving}.");
            Messages.Message("TSA_WD_InterceptionFailed".Translate(travelerLabel), MessageTypeDefOf.NegativeEvent);
            SendPlayerCaravanClashResultLetter(victory: false);

            if (foughtOnPlayerAtTurret)
            {
                AtTurretUtility.DestroyPlayerAtTurretOnTileAfterClashDefeat(map.Tile.tileId, savedOrigin);
                foughtOnPlayerAtTurret = false;
            }

            encounterActive = false;
        }

        private void DiscardEncounterLeftovers()
        {
            if (travelerDef == null && !encounterActive && !playerHasWon && enemyFaction == null)
                return;

            if (!leftoversDiscarded)
            {
                leftoversDiscarded = true;

                var nonVehicles = new List<Pawn>();
                var vehicles = new List<Pawn>();
                CollectMapLeftovers(nonVehicles, vehicles);

                for (int i = 0; i < nonVehicles.Count; i++)
                    TryDestroyLeftoverPawn(nonVehicles[i]);
                for (int i = 0; i < vehicles.Count; i++)
                    VehicleFrameworkOutpostDissolveCompat.DestroyVehiclePawnForCleanup(vehicles[i]);
            }

            DiscardOrphanEnemyFactionVehiclesFromWorldPawns(enemyFaction, map);
        }

        private void CollectMapLeftovers(List<Pawn> nonVehicles, List<Pawn> vehicles)
        {
            var spawned = map?.mapPawns?.AllPawnsSpawned;
            if (spawned == null) return;

            for (int i = 0; i < spawned.Count; i++)
            {
                Pawn p = spawned[i];
                if (p == null || p.Destroyed) continue;
                if (!IsEncounterLeftover(p)) continue;
                if (VehicleFrameworkOutpostDissolveCompat.IsVehicleFrameworkVehiclePawn(p))
                    vehicles.Add(p);
                else
                    nonVehicles.Add(p);
            }
        }

        private bool IsEncounterLeftover(Pawn p)
        {
            if (p.Faction != null && p.Faction.IsPlayer) return false;
            if (VehicleFrameworkOutpostDissolveCompat.IsVehicleFrameworkVehiclePawn(p)
                && p.Faction != null && p.Faction.IsPlayer)
                return false;
            if (p.IsPrisonerOfColony || p.IsSlaveOfColony) return false;
            if (enemyFaction != null && p.Faction == enemyFaction) return true;
            if (savedMission != TravelerMission.Trader && p.HostileTo(Faction.OfPlayer)) return true;
            return false;
        }

        private static void TryDestroyLeftoverPawn(Pawn p)
        {
            if (p == null || p.Destroyed) return;
            try
            {
                if (!p.Destroyed)
                    p.Destroy(DestroyMode.Vanish);
                if (Find.WorldPawns != null && Find.WorldPawns.Contains(p))
                    Find.WorldPawns.RemovePawn(p);
            }
            catch (Exception ex)
            {
                Log.Warning($"[TSA WD] Caravan clash leftover cleanup failed for {p.LabelShortCap}: {ex.Message}");
            }
        }

        private static void DiscardOrphanEnemyFactionVehiclesFromWorldPawns(Faction enemyFac, Map mapStillAllowed)
        {
            if (enemyFac == null || Find.WorldPawns == null) return;

            List<Pawn> orphans = null;
            foreach (Pawn p in Find.WorldPawns.AllPawnsAlive)
            {
                if (p == null || p.Destroyed) continue;
                if (p.Faction != enemyFac) continue;
                if (!VehicleFrameworkOutpostDissolveCompat.IsVehicleFrameworkVehiclePawn(p)) continue;
                if (p.GetCaravan() != null) continue;
                if (p.Spawned && p.Map != null && p.Map != mapStillAllowed) continue;
                orphans ??= new List<Pawn>();
                orphans.Add(p);
            }

            if (orphans == null) return;
            for (int i = 0; i < orphans.Count; i++)
                VehicleFrameworkOutpostDissolveCompat.DestroyVehiclePawnForCleanup(orphans[i]);
        }

        private void QueueAmbushEncounterMapTeardown()
        {
            MapParent parent = map?.Parent;
            if (parent == null || parent.Destroyed || parent.def != WorldObjectDefOf.Ambush) return;
            LongEventHandler.ExecuteWhenFinished(TeardownAmbushEncounterMapNow);
        }

        private void TeardownAmbushEncounterMapNow()
        {
            MapParent parent = map?.Parent;
            if (parent == null || parent.Destroyed || parent.def != WorldObjectDefOf.Ambush) return;
            try
            {
                if (parent.HasMap && map != null && Current.Game.Maps.Contains(map))
                    Current.Game.DeinitAndRemoveMap(map, false);
                else if (!parent.Destroyed)
                    parent.Destroy();
            }
            catch (Exception ex)
            {
                Log.Warning($"[TSA WD] Clash encounter map destroy failed: {ex.Message}");
            }
        }

        private void DestroyAmbushParentIfPresent()
        {
            MapParent parent = map?.Parent;
            if (parent == null || parent.Destroyed || parent.def != WorldObjectDefOf.Ambush) return;
            try
            {
                if (!parent.Destroyed)
                    parent.Destroy();
            }
            catch (Exception ex)
            {
                Log.Warning($"[TSA WD] Clash Ambush parent destroy failed: {ex.Message}");
            }
        }

        private void SendPlayerCaravanClashResultLetter(bool victory)
        {
            if (victory) return;
            if (!(WorldDominationMod.settings?.notifyPlayerCaravanClash ?? WorldDominationSettings.DefNotifyPlayerCaravanClash))
                return;

            Find.LetterStack.ReceiveLetter(
                "TSA_WD_Letter_PlayerCaravanClashDestroyed_Label".Translate(),
                "TSA_WD_Letter_PlayerCaravanClashDestroyed_Text".Translate(travelerLabel ?? "TSA_WD_Traveller_Unknown".Translate()),
                LetterDefOf.NegativeEvent,
                new GlobalTargetInfo(map.Center, map));
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref travelerDef, "travelerDef");
            Scribe_References.Look(ref enemyFaction, "enemyFaction");
            Scribe_Values.Look(ref travelerStrength, "travelerStrength");
            Scribe_Values.Look(ref destinationTileId, "destinationTile", -1);
            Scribe_Values.Look(ref travelerLabel, "travelerLabel");
            Scribe_Values.Look(ref encounterActive, "encounterActive");
            Scribe_Values.Look(ref playerHasWon, "playerHasWon");
            Scribe_Values.Look(ref startTick, "startTick");
            Scribe_Values.Look(ref savedMission, "savedMission", TravelerMission.Expansion);
            Scribe_References.Look(ref savedOrigin, "savedOrigin");
            Scribe_References.Look(ref savedTarget, "savedTarget");
            Scribe_Values.Look(ref savedInitialStrength, "savedInitialStrength", 0f);
            Scribe_Values.Look(ref leftoversDiscarded, "leftoversDiscarded", false);
            Scribe_Values.Look(ref foughtOnPlayerAtTurret, "foughtOnPlayerAtTurret", false);
            Scribe_Values.Look(ref lootResolved, "lootResolved", false);

            if (Scribe.mode == LoadSaveMode.PostLoadInit && playerHasWon)
                encounterActive = false;
        }
    }
}
