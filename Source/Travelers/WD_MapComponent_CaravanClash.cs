using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    public class WD_MapComponent_CaravanClash : MapComponent
    {
        private static readonly List<Pawn> aboardScratch = new List<Pawn>();
        private static readonly HashSet<Pawn> aboardSeen = new HashSet<Pawn>();
        private static readonly FieldInfo TravellingTransportersInitialTileField =
            AccessTools.Field(typeof(TravellingTransporters), "initialTile");

        private WorldObjectDef travelerDef;
        private Faction enemyFaction;
        private float travelerStrength;
        private int destinationTileId = -1;
        private string travelerLabel;

        private TravelerMission savedMission = TravelerMission.Expansion;
        private WorldObject savedOrigin;
        private WorldObject savedTarget;
        private float savedInitialStrength;
        private bool savedPackUpRequiresRefound;
        private string savedPackUpOriginLabel;
        private int savedMassRelocationDestTile = -1;
        private SettlementTier savedMassRelocationTier = SettlementTier.T1;
        private float savedMassRelocationDefensiveStrength;
        private bool savedIsDesperationRaid;
        private bool savedIsInvasionRaid;
        private int savedDesperationGroupId;
        private int savedDesperationExpectedCount;
        private int savedDesperationArrivedCount;
        private int savedDesperationWaitUntilTick = -1;
        private bool savedDesperationIsHost;
        private int savedDesperationAggressorFactionId = -1;

        private bool encounterActive = false;
        private bool playerHasWon = false;
        /// <summary>
        /// Latched when no effective enemy threats remain (dead/downed/PanicFlee). Survives reform before
        /// <see cref="playerHasWon"/> is set so MapRemoved does not respawn the traveler.
        /// </summary>
        private bool enemiesBroken = false;
        /// <summary>
        /// Player fled via aerial/shuttle. Traveler already respawned;
        /// Ambush teardown waits until escape craft leaves this map.
        /// </summary>
        private bool playerFled = false;
        /// <summary>
        /// Player shuttle/VF launch started from this Ambush but may still be mid-skyfaller.
        /// Suppresses wipe/teardown if the last ground fighter dies before the craft leaves.
        /// </summary>
        private bool aerialLeaveInProgress = false;
        private int startTick = -1;
        private bool leftoversDiscarded = false;
        // Legacy; scribe only — old loot dialog path removed; player uses vanilla reform caravan.
        private bool lootResolved = false;
        /// <summary>Prevents stacking multiple Ambush teardown long-events (flee Phase B retries).</summary>
        private bool fleeTeardownQueued = false;
        /// <summary>True when this clash map was opened on a tile that still has a player AT Turret.</summary>
        private bool foughtOnPlayerAtTurret;

        /// <summary>True only while the interception raid incident is executing; not saved.</summary>
        public bool InterceptionRaidPending { get; set; }

        /// <summary>For ForceRaidDirection / other mods: do not steer this raid; WD sets spawn or encounter is active.</summary>
        public bool ShouldSkipExternalRaidSteering => InterceptionRaidPending || encounterActive;

        /// <summary>
        /// Mid-fight: block drafted map-edge auto-caravan exit. Cleared after WD victory so vanilla
        /// <see cref="FormCaravanComp"/> reform caravan remains available. Stays blocked after aerial flee
        /// until Ambush teardown (no edge reform while escape craft is still on the map).
        /// </summary>
        public bool BlocksPlayerEdgeExit => (encounterActive || playerFled || aerialLeaveInProgress) && !playerHasWon;

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
            this.savedPackUpRequiresRefound = traveler.packUpRequiresRefound;
            this.savedPackUpOriginLabel = traveler.packUpOriginLabel;
            this.savedMassRelocationDestTile = traveler.massRelocationDestTile;
            this.savedMassRelocationTier = traveler.massRelocationTier;
            this.savedMassRelocationDefensiveStrength = traveler.massRelocationDefensiveStrength;
            this.savedIsDesperationRaid = traveler.isDesperationRaid;
            this.savedIsInvasionRaid = traveler.isInvasionRaid;
            this.savedDesperationGroupId = traveler.desperationGroupId;
            this.savedDesperationExpectedCount = traveler.desperationExpectedCount;
            this.savedDesperationArrivedCount = traveler.desperationArrivedCount;
            this.savedDesperationWaitUntilTick = traveler.desperationWaitUntilTick;
            this.savedDesperationIsHost = traveler.desperationIsHost;
            this.savedDesperationAggressorFactionId = traveler.desperationAggressorFactionId;

            this.encounterActive = true;
            this.startTick = Find.TickManager.TicksGame;
            this.leftoversDiscarded = false;
            this.lootResolved = false;
            this.playerHasWon = false;
            this.enemiesBroken = false;
            this.playerFled = false;
            this.aerialLeaveInProgress = false;
            this.fleeTeardownQueued = false;
            this.foughtOnPlayerAtTurret = AtTurretUtility.TileHasPlayerAtTurret(map.Tile.tileId);

            WDVerbose.Msg($"[TSA WD] Data saved for {travelerLabel}. Original destroyed.");
            traveler.Destroy();
        }

        public override void MapComponentTick()
        {
            if (playerHasWon)
                return;

            if (playerFled)
            {
                if (Find.TickManager.TicksGame % 60 == 0)
                    TryFinishFleeTeardownIfSafe();
                return;
            }

            if (!encounterActive) return;

            if (Find.TickManager.TicksGame % 60 == 0)
            {
                // Launch started; ground force wiped mid-skyfaller — latch flee once craft is off-map.
                if (aerialLeaveInProgress
                    && !AnyPlayerClashForceStanding()
                    && !AnyEscapedSurvivorsStillOnThisMap()
                    && ThreatExists())
                {
                    ResolvePlayerFledClashPhaseA();
                    TryFinishFleeTeardownIfSafe();
                    return;
                }

                CheckEncounterState();
            }
        }

        private void CheckEncounterState()
        {
            bool threatExists = ThreatExists();
            if (!threatExists)
                enemiesBroken = true;

            bool playerStanding = AnyPlayerClashForceStanding();

            if (!threatExists && playerStanding)
            {
                if (Find.TickManager.TicksGame > startTick + 600)
                {
                    WDVerbose.Msg($"[TSA WD] Victory detected for {travelerLabel}.");
                    playerHasWon = true;
                    encounterActive = false;
                    aerialLeaveInProgress = false;
                    Messages.Message("TSA_WD_InterceptionVictory".Translate(), MessageTypeDefOf.PositiveEvent);
                }
                return;
            }

            if (!playerStanding && threatExists)
            {
                // Boarded / launching: suppress wipe. Lose only when craft has left (or leave hooks fire).
                if (AnyPlayerClashSurvivorsEscaped() || aerialLeaveInProgress)
                    return;

                RespawnNewTraveler(fled: false);
                ExecuteAllDownedPlayerPawns();
                DiscardEncounterLeftovers();
                QueueAmbushEncounterMapTeardown();
            }
        }

        private bool ThreatExists()
        {
            return savedMission == TravelerMission.Trader
                ? AnyLivingCaravanFactionPawnThreat()
                : GenHostility.AnyHostileActiveThreatToPlayer(map, true);
        }

        /// <summary>
        /// Successful launch from this Ambush map. Marks leave-in-progress immediately so a mid-launch
        /// ground wipe cannot tear down the map under the skyfaller. Full flee only if no standing force.
        /// </summary>
        public void NotifyPossibleAerialLeave()
        {
            if (playerHasWon) return;
            if (playerFled) return;
            if (!encounterActive) return;

            aerialLeaveInProgress = true;

            if (!ThreatExists()) return;
            if (AnyPlayerClashForceStanding()) return;

            ResolvePlayerFledClashPhaseA();
        }

        /// <summary>
        /// World airborne spawned from this clash tile (VF aerial / TravellingTransporters).
        /// No-op if a standing player force remains on the Ambush (partial shuttle evacuate).
        /// </summary>
        public void NotifyAirborneSurvivorsLeftThisClash()
        {
            if (playerHasWon)
            {
                aerialLeaveInProgress = false;
                if (!AnyPlayerClashForceStanding())
                    TryFinishAmbushCleanupIfCraftGone();
                return;
            }
            if (encounterActive)
            {
                // Some left by air, some still fighting — keep the clash map alive.
                if (AnyPlayerClashForceStanding())
                {
                    aerialLeaveInProgress = false;
                    return;
                }

                aerialLeaveInProgress = false;

                // Enemies already cleared while boarding/leaving: win, do not respawn traveler.
                if (enemiesBroken || !ThreatExists())
                {
                    playerHasWon = true;
                    encounterActive = false;
                }
                else
                    ResolvePlayerFledClashPhaseA();
            }
            else
                aerialLeaveInProgress = false;

            if (playerFled)
                TryFinishFleeTeardownIfSafe();
            else if (playerHasWon)
                TryFinishAmbushCleanupIfCraftGone();
        }

        public static WD_MapComponent_CaravanClash FindClashNeedingAerialFleeOnTile(int tileId)
        {
            if (tileId < 0) return null;
            var maps = Current.Game?.Maps;
            if (maps == null) return null;
            for (int i = 0; i < maps.Count; i++)
            {
                Map m = maps[i];
                if (m == null || !m.Tile.Valid || m.Tile.tileId != tileId) continue;
                MapParent parent = m.Parent;
                if (parent == null || parent.Destroyed || parent.def != WorldObjectDefOf.Ambush) continue;
                WD_MapComponent_CaravanClash clash = m.GetComponent<WD_MapComponent_CaravanClash>();
                if (clash == null) continue;
                if (clash.playerHasWon) continue;
                if (!clash.encounterActive && !clash.playerFled) continue;
                return clash;
            }
            return null;
        }

        public static void NotifyWorldAirborneFromStartTile(int startTileId)
        {
            FindClashNeedingAerialFleeOnTile(startTileId)?.NotifyAirborneSurvivorsLeftThisClash();
        }

        public static void NotifyPossibleAerialLeaveFromMap(Map map)
        {
            map?.GetComponent<WD_MapComponent_CaravanClash>()?.NotifyPossibleAerialLeave();
        }

        public static int TryGetTravellingTransportersStartTileId(TravellingTransporters pods)
        {
            if (pods == null) return -1;
            try
            {
                if (TravellingTransportersInitialTileField != null)
                {
                    object v = TravellingTransportersInitialTileField.GetValue(pods);
                    if (v is PlanetTile pt && pt.Valid)
                        return pt.tileId;
                }
            }
            catch
            {
                // fall through
            }
            return pods.Tile.Valid ? pods.Tile.tileId : -1;
        }

        private void ResolvePlayerFledClashPhaseA()
        {
            if (playerFled || playerHasWon) return;
            if (!encounterActive) return;

            WDVerbose.Msg($"[TSA WD] Aerial/shuttle flee Phase A for {travelerLabel}.");
            RespawnNewTraveler(fled: true);
            playerFled = true;
            ExecuteAllDownedPlayerPawns();
        }

        private void TryFinishFleeTeardownIfSafe()
        {
            if (!playerFled || playerHasWon) return;
            if (AnyPlayerClashForceStanding()) return;
            if (aerialLeaveInProgress || AnyEscapedSurvivorsStillOnThisMap()) return;
            ResolvePlayerFledClashPhaseB();
        }

        /// <summary>Ambush cleanup after aerial leave when the clash already resolved (win or flee).</summary>
        private void TryFinishAmbushCleanupIfCraftGone()
        {
            if (AnyPlayerClashForceStanding()) return;
            if (aerialLeaveInProgress || AnyEscapedSurvivorsStillOnThisMap()) return;
            DiscardEncounterLeftovers();
            QueueAmbushEncounterMapTeardown();
        }

        private void ResolvePlayerFledClashPhaseB()
        {
            if (!playerFled || playerHasWon) return;
            if (AnyPlayerClashForceStanding()) return;
            if (aerialLeaveInProgress || AnyEscapedSurvivorsStillOnThisMap()) return;

            WDVerbose.Msg($"[TSA WD] Aerial/shuttle flee Phase B teardown for {travelerLabel}.");
            aerialLeaveInProgress = false;
            DiscardEncounterLeftovers();
            QueueAmbushEncounterMapTeardown();
        }

        /// <summary>
        /// Living player humanlikes in Odyssey shuttle / off-map aerial escape — not counting as “standing”.
        /// </summary>
        private bool AnyPlayerClashSurvivorsEscaped()
        {
            if (AnyEscapedSurvivorsStillOnThisMap())
                return true;

            int clashTile = map != null && map.Tile.Valid ? map.Tile.tileId : -1;
            if (clashTile < 0 || Find.WorldObjects == null) return false;

            List<WorldObject> all = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < all.Count; i++)
            {
                WorldObject wo = all[i];
                if (wo is not TravellingTransporters pods || pods.Destroyed) continue;
                if (TryGetTravellingTransportersStartTileId(pods) != clashTile) continue;
                if (PodsHaveLivingPlayerHumanlike(pods))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Player escape craft still on the Ambush map: boarded shuttle, leaving skyfaller, or VF crew.
        /// Must stay true while leave skyfallers tick — tearing down the Ambush mid-<c>LeaveMap</c> NREs / deletes them.
        /// </summary>
        private bool AnyEscapedSurvivorsStillOnThisMap()
        {
            if (map?.listerThings?.AllThings != null)
            {
                List<Thing> all = map.listerThings.AllThings;
                for (int i = 0; i < all.Count; i++)
                {
                    Thing t = all[i];
                    if (t == null || t.Destroyed) continue;

                    // Odyssey shuttle leave skyfaller still owns the map until TravellingTransporters spawns.
                    if (t is FlyShipLeaving)
                        return true;

                    if (t is Skyfaller skyfaller)
                    {
                        if (IsPassengerShuttleLeaveSkyfaller(skyfaller))
                            return true;
                        if (SkyfallerHasLivingPlayerHumanlike(skyfaller))
                            return true;
                    }

                    if (ModsConfig.OdysseyActive
                        && t is Building_PassengerShuttle shuttle
                        && (shuttle.Faction == null || shuttle.Faction.IsPlayer)
                        && ShuttleHasLivingPlayerHumanlike(shuttle))
                        return true;
                }
            }

            var spawned = map?.mapPawns?.AllPawnsSpawned;
            if (spawned == null) return false;
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
                    if (IsLivingPlayerHumanlike(aboardScratch[j]))
                        return true;
                }
            }

            return false;
        }

        private static bool IsPassengerShuttleLeaveSkyfaller(Skyfaller skyfaller)
        {
            string defName = skyfaller?.def?.defName;
            if (string.IsNullOrEmpty(defName)) return false;
            return defName.IndexOf("PassengerShuttleLeaving", StringComparison.OrdinalIgnoreCase) >= 0
                || defName.IndexOf("PassengerShuttleSkyfaller", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool SkyfallerHasLivingPlayerHumanlike(Skyfaller skyfaller)
        {
            ThingOwner container = skyfaller?.innerContainer;
            if (container == null) return false;
            for (int i = 0; i < container.Count; i++)
            {
                if (container[i] is Pawn p && IsLivingPlayerHumanlike(p))
                    return true;
                if (container[i] is Building_PassengerShuttle shuttle
                    && ShuttleHasLivingPlayerHumanlike(shuttle))
                    return true;
            }
            return false;
        }

        private static bool ShuttleHasLivingPlayerHumanlike(Building_PassengerShuttle shuttle)
        {
            CompTransporter transporter = shuttle?.TransporterComp;
            ThingOwner container = transporter?.innerContainer;
            if (container == null) return false;
            for (int i = 0; i < container.Count; i++)
            {
                if (container[i] is Pawn p && IsLivingPlayerHumanlike(p))
                    return true;
            }
            return false;
        }

        private static bool PodsHaveLivingPlayerHumanlike(TravellingTransporters pods)
        {
            if (pods == null) return false;
            foreach (Pawn p in pods.Pawns)
            {
                if (IsLivingPlayerHumanlike(p))
                    return true;
            }
            return false;
        }

        private static bool IsLivingPlayerHumanlike(Pawn p)
        {
            if (p == null || p.Destroyed || p.Dead) return false;
            if (VehicleFrameworkOutpostDissolveCompat.IsVehicleFrameworkVehiclePawn(p)) return false;
            if (p.RaceProps == null || !p.RaceProps.Humanlike) return false;
            if (p.Faction == null || !p.Faction.IsPlayer) return false;
            return true;
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
            if (IsCombatIneffective(p)) return false;
            if (VehicleFrameworkOutpostDissolveCompat.IsVehicleFrameworkVehiclePawn(p)) return false;
            if (p.RaceProps == null || !p.RaceProps.Humanlike) return false;
            if (p.Faction == null || !p.Faction.IsPlayer) return false;
            return true;
        }

        /// <summary>
        /// Dead, downed, or PanicFlee — same bar for enemy threats and player “still standing”
        /// (matches vanilla Reform Caravan “no active hostiles” and <see cref="WdSettlementTurretSilence"/>).
        /// </summary>
        private static bool IsCombatIneffective(Pawn p)
        {
            if (p == null || p.Destroyed || p.Dead || p.Downed) return true;
            if (p.MentalStateDef == MentalStateDefOf.PanicFlee) return true;
            return false;
        }

        private bool AnyLivingCaravanFactionPawnThreat()
        {
            if (enemyFaction == null) return false;
            foreach (Pawn p in map.mapPawns.AllPawnsSpawned)
            {
                if (IsCombatIneffective(p)) continue;
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
            // enemiesBroken was latched while the map was live (fleeers count as clear); skip respawn.
            // playerFled already respawned the traveler in Phase A.
            if (encounterActive && !playerHasWon && !playerFled)
            {
                if (enemiesBroken)
                {
                    playerHasWon = true;
                    encounterActive = false;
                    WDVerbose.Msg($"[TSA WD] Victory on map exit (enemies broken) for {travelerLabel}.");
                }
                else
                    RespawnNewTraveler(fled: false);
            }

            playerFled = false;

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

        private void RespawnNewTraveler(bool fled = false)
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
            newTraveler.packUpRequiresRefound = savedPackUpRequiresRefound;
            newTraveler.packUpOriginLabel = savedPackUpOriginLabel;
            newTraveler.massRelocationDestTile = savedMassRelocationDestTile;
            newTraveler.massRelocationTier = savedMassRelocationTier;
            newTraveler.massRelocationDefensiveStrength = savedMassRelocationDefensiveStrength;
            newTraveler.isDesperationRaid = savedIsDesperationRaid;
            newTraveler.isInvasionRaid = savedIsInvasionRaid;
            newTraveler.desperationGroupId = savedDesperationGroupId;
            newTraveler.desperationExpectedCount = savedDesperationExpectedCount;
            newTraveler.desperationArrivedCount = savedDesperationArrivedCount;
            newTraveler.desperationWaitUntilTick = savedDesperationWaitUntilTick;
            newTraveler.desperationIsHost = savedDesperationIsHost;
            newTraveler.desperationAggressorFactionId = savedDesperationAggressorFactionId;
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

            WDVerbose.Msg($"[TSA WD] {(fled ? "Flee" : "Defeat")}/Closure: {travelerLabel} recreated on world map dest={destId} moving={moving}.");
            Messages.Message(
                (fled ? "TSA_WD_InterceptionFled" : "TSA_WD_InterceptionFailed").Translate(travelerLabel),
                MessageTypeDefOf.NegativeEvent);

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
            if (fleeTeardownQueued) return;
            fleeTeardownQueued = true;
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
            Scribe_Values.Look(ref enemiesBroken, "enemiesBroken", false);
            Scribe_Values.Look(ref playerFled, "playerFled", false);
            Scribe_Values.Look(ref aerialLeaveInProgress, "aerialLeaveInProgress", false);
            Scribe_Values.Look(ref startTick, "startTick");
            Scribe_Values.Look(ref savedMission, "savedMission", TravelerMission.Expansion);
            Scribe_References.Look(ref savedOrigin, "savedOrigin");
            Scribe_References.Look(ref savedTarget, "savedTarget");
            Scribe_Values.Look(ref savedInitialStrength, "savedInitialStrength", 0f);
            Scribe_Values.Look(ref savedPackUpRequiresRefound, "savedPackUpRequiresRefound", false);
            Scribe_Values.Look(ref savedPackUpOriginLabel, "savedPackUpOriginLabel");
            Scribe_Values.Look(ref savedMassRelocationDestTile, "savedMassRelocationDestTile", -1);
            Scribe_Values.Look(ref savedMassRelocationTier, "savedMassRelocationTier", SettlementTier.T1);
            Scribe_Values.Look(ref savedMassRelocationDefensiveStrength, "savedMassRelocationDefensiveStrength", 0f);
            Scribe_Values.Look(ref savedIsDesperationRaid, "savedIsDesperationRaid", false);
            Scribe_Values.Look(ref savedIsInvasionRaid, "savedIsInvasionRaid", false);
            Scribe_Values.Look(ref savedDesperationGroupId, "savedDesperationGroupId", 0);
            Scribe_Values.Look(ref savedDesperationExpectedCount, "savedDesperationExpectedCount", 0);
            Scribe_Values.Look(ref savedDesperationArrivedCount, "savedDesperationArrivedCount", 0);
            Scribe_Values.Look(ref savedDesperationWaitUntilTick, "savedDesperationWaitUntilTick", -1);
            Scribe_Values.Look(ref savedDesperationIsHost, "savedDesperationIsHost", false);
            Scribe_Values.Look(ref savedDesperationAggressorFactionId, "savedDesperationAggressorFactionId", -1);
            Scribe_Values.Look(ref leftoversDiscarded, "leftoversDiscarded", false);
            Scribe_Values.Look(ref foughtOnPlayerAtTurret, "foughtOnPlayerAtTurret", false);
            Scribe_Values.Look(ref lootResolved, "lootResolved", false);

            if (Scribe.mode == LoadSaveMode.PostLoadInit && playerHasWon)
                encounterActive = false;
        }
    }
}
