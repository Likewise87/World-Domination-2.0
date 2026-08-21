using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    public class WD_MapComponent_CaravanClash : MapComponent
    {
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
        private bool lootResolved = false;
        private bool lootDialogOpen = false;
        /// <summary>True when this clash map was opened on a tile that still has a player AT Turret.</summary>
        private bool foughtOnPlayerAtTurret;

        /// <summary>True only while the interception raid incident is executing; not saved.</summary>
        public bool InterceptionRaidPending { get; set; }

        /// <summary>For ForceRaidDirection / other mods: do not steer this raid; WD sets spawn or encounter is active.</summary>
        public bool ShouldSkipExternalRaidSteering => InterceptionRaidPending || encounterActive;

        public WD_MapComponent_CaravanClash(Map map) : base(map) { }

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
            this.lootDialogOpen = false;
            this.playerHasWon = false;
            // Hostile AT already cleared at clash start; any remaining gun here is the player's.
            this.foughtOnPlayerAtTurret = AtTurretUtility.TileHasPlayerAtTurret(map.Tile.tileId);

            Log.Message($"[TSA WD] Data saved for {travelerLabel}. Original destroyed.");
            traveler.Destroy();
        }

        public override void MapComponentTick()
        {
            if (playerHasWon)
            {
                if (!lootResolved && Find.TickManager.TicksGame % 60 == 0)
                    TryResumeVictoryLootPath();
                return;
            }

            if (!encounterActive) return;

            if (Find.TickManager.TicksGame % 60 == 0)
                CheckEncounterState();
        }

        private void CheckEncounterState()
        {
            bool threatExists = savedMission == TravelerMission.Trader
                ? AnyLivingCaravanFactionPawnThreat()
                : GenHostility.AnyHostileActiveThreatToPlayer(map, true);
            bool playerStanding = false;
            var allPawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < allPawns.Count; i++)
            {
                var p = allPawns[i];
                if (p.Faction != null && p.Faction.IsPlayer && !p.Downed && !p.Dead) { playerStanding = true; break; }
            }

            // 1. Victory Logic
            if (!threatExists && playerStanding)
            {
                if (Find.TickManager.TicksGame > startTick + 600)
                {
                    Log.Message($"[TSA WD] Victory detected for {travelerLabel}.");
                    playerHasWon = true;
                    Messages.Message("TSA_WD_InterceptionVictory".Translate(), MessageTypeDefOf.PositiveEvent);
                    SendPlayerCaravanClashResultLetter(victory: true);
                    BeginVictoryLootPath();
                }
                return;
            }

            // 2. Defeat Logic
            if (!playerStanding && threatExists)
            {
                // SURGICAL FIX: We respawn BEFORE killing the pawns.
                // This ensures the WorldObject is back on the map before the map is wiped.
                RespawnNewTraveler();
                ExecuteAllDownedPlayerPawns();
                DiscardEncounterLeftovers();
            }
        }

        /// <summary>Vanilla trader caravans are not hostile; treat any living pawn of the encounter faction as an active threat until defeated.</summary>
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
            {
                lootResolved = true;
                lootDialogOpen = false;
            }

            // Fallback for unexpected closure (Dev mode, fleeing, etc)
            if (encounterActive && !playerHasWon)
            {
                bool stillThreat = savedMission == TravelerMission.Trader
                    ? AnyLivingCaravanFactionPawnThreat()
                    : GenHostility.AnyHostileActiveThreatToPlayer(map, true);
                if (!stillThreat)
                {
                    encounterActive = false;
                    // Threat gone without a formal victory tick: keep player AT (same as win).
                }
                else
                    RespawnNewTraveler();
            }

            // After traveler respawn decision: destroy NPC leftovers (esp. VF vehicles) so they
            // never remain as ticking WorldPawns after the temporary clash map is gone.
            DiscardEncounterLeftovers();

            // VF may PassToWorld vehicles after MapComponent.MapRemoved; sweep once teardown finishes.
            Faction fac = enemyFaction;
            if (fac != null)
            {
                LongEventHandler.ExecuteWhenFinished(delegate
                {
                    DiscardOrphanEnemyFactionVehiclesFromWorldPawns(fac, mapStillAllowed: null);
                });
            }
        }

        /// <summary>On player defeat or map closure: recreate the traveler on the world map so the mission can continue.</summary>
        private void RespawnNewTraveler()
        {
            // The !encounterActive check prevents double-spawning if Tick and MapRemoved fire together
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

            if (destinationTileId >= 0 && newTraveler.pather != null)
                newTraveler.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(destinationTileId, map.Parent));

            Log.Message($"[TSA WD] Defeat/Closure: {travelerLabel} has been recreated on the world map.");
            Messages.Message("TSA_WD_InterceptionFailed".Translate(travelerLabel), MessageTypeDefOf.NegativeEvent);
            SendPlayerCaravanClashResultLetter(victory: false);

            if (foughtOnPlayerAtTurret)
            {
                AtTurretUtility.DestroyPlayerAtTurretOnTileAfterClashDefeat(map.Tile.tileId, savedOrigin);
                foughtOnPlayerAtTurret = false;
            }

            encounterActive = false; // Mark as handled
        }

        private void BeginVictoryLootPath()
        {
            if (lootResolved) return;

            if (WD_CaravanClashLootUtility.SettingEnabled
                && WD_CaravanClashLootUtility.TryBuildCandidates(map, out var prisoners, out var items)
                && (prisoners.Count > 0 || items.Count > 0))
            {
                OpenLootDialog(prisoners, items);
                return;
            }

            CompleteVictoryLoot(null, null);
        }

        private void TryResumeVictoryLootPath()
        {
            if (lootResolved || lootDialogOpen) return;
            if (Find.WindowStack != null && Find.WindowStack.IsOpen<Dialog_WdCaravanClashLoot>())
            {
                lootDialogOpen = true;
                return;
            }

            BeginVictoryLootPath();
        }

        private void OpenLootDialog(
            List<WD_CaravanClashLootUtility.LootPrisonerRow> prisoners,
            List<WD_CaravanClashLootUtility.LootItemRow> items)
        {
            if (lootDialogOpen) return;
            lootDialogOpen = true;
            Find.WindowStack.Add(new Dialog_WdCaravanClashLoot(this, map, prisoners, items));
        }

        /// <summary>Called by the loot dialog (confirm, take nothing, or close).</summary>
        public void CompleteVictoryLoot(
            List<WD_CaravanClashLootUtility.LootPrisonerRow> prisoners,
            List<WD_CaravanClashLootUtility.LootItemRow> items)
        {
            if (lootResolved) return;
            lootResolved = true;
            lootDialogOpen = false;
            encounterActive = false;
            WD_CaravanClashLootUtility.ApplyAndLeave(map, this, prisoners, items);
        }

        public void DiscardEncounterLeftoversPublic() => DiscardEncounterLeftovers();

        /// <summary>
        /// Destroy non-player encounter pawns (and VF vehicles) still on this map, plus orphan
        /// encounter-faction vehicles already parked in WorldPawns without a caravan.
        /// </summary>
        private void DiscardEncounterLeftovers()
        {
            // Only when this map actually hosted a clash (or still has encounter state).
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

            // Always sweep: VF may PassToWorld vehicles during teardown after map destroy.
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
            // Captured for the leaving caravan: still enemy Faction, but HostFaction is the player.
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

        /// <summary>
        /// Belt-and-suspenders: VF may PassToWorld vehicles during map teardown; remove orphan
        /// encounter-faction vehicles that are not part of any caravan.
        /// </summary>
        private static void DiscardOrphanEnemyFactionVehiclesFromWorldPawns(Faction enemyFaction, Map mapStillAllowed)
        {
            if (enemyFaction == null || Find.WorldPawns == null) return;

            List<Pawn> orphans = null;
            foreach (Pawn p in Find.WorldPawns.AllPawnsAlive)
            {
                if (p == null || p.Destroyed) continue;
                if (p.Faction != enemyFaction) continue;
                if (!VehicleFrameworkOutpostDissolveCompat.IsVehicleFrameworkVehiclePawn(p)) continue;
                if (p.GetCaravan() != null) continue;
                // Still on another live map — leave alone.
                if (p.Spawned && p.Map != null && p.Map != mapStillAllowed) continue;
                orphans ??= new List<Pawn>();
                orphans.Add(p);
            }

            if (orphans == null) return;
            for (int i = 0; i < orphans.Count; i++)
                VehicleFrameworkOutpostDissolveCompat.DestroyVehiclePawnForCleanup(orphans[i]);
        }

        private void SendPlayerCaravanClashResultLetter(bool victory)
        {
            if (!(WorldDominationMod.settings?.notifyPlayerCaravanClash ?? WorldDominationSettings.DefNotifyPlayerCaravanClash))
                return;

            Find.LetterStack.ReceiveLetter(
                victory ? "TSA_WD_Letter_PlayerCaravanClashWon_Label".Translate() : "TSA_WD_Letter_PlayerCaravanClashDestroyed_Label".Translate(),
                victory
                    ? "TSA_WD_Letter_PlayerCaravanClashWon_Text".Translate(travelerLabel ?? "TSA_WD_Traveller_Unknown".Translate())
                    : "TSA_WD_Letter_PlayerCaravanClashDestroyed_Text".Translate(travelerLabel ?? "TSA_WD_Traveller_Unknown".Translate()),
                victory ? LetterDefOf.PositiveEvent : LetterDefOf.NegativeEvent,
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
        }
    }
}
