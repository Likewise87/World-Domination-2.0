using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// NPC Fortify: threat-gated daily action. Builds the same local turtle kit layout
    /// (r=1 AT, r=2 traps on roads / blocks off-road) one phase per successful Fortify:
    /// traps (ensure road exit) → road blocks → AT turrets.
    /// </summary>
    public static class WorldActions_NpcFortify
    {
        private static readonly List<int> tempPhaseTiles = new List<int>();
        private static readonly HashSet<int> pickedFortifyTiles = new HashSet<int>();
        private static readonly HashSet<int> tempOpenRing = new HashSet<int>();
        private static readonly List<WorldObject> tempHostiles = new List<WorldObject>();

        public static void UpdateDailyThreatBits(
            DailyWorldSnapshot snapshot,
            WorldComponent_SpreadManager manager,
            WorldDominationSettings seth)
        {
            if (snapshot?.SettlementsByFaction == null || seth == null) return;

            foreach (var kv in snapshot.SettlementsByFaction)
            {
                Faction faction = kv.Key;
                List<Settlement> list = kv.Value;
                if (faction == null || faction.IsPlayer || list == null) continue;

                CollectHostilesForFaction(faction, tempHostiles);

                for (int i = 0; i < list.Count; i++)
                {
                    Settlement s = list[i];
                    if (!DailyWorldSnapshot.IsSettlementStillValid(s)) continue;
                    if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(s)) continue;
                    var comp = s.GetComponent<CompViralSpread>();
                    if (comp == null) continue;

                    comp.fortifyThreatenedToday = IsThreatenedToday(s, comp, manager, seth, out WorldObject threatInRange);
                    FindNearestHostile(s, tempHostiles, out WorldObject nearestAny, out _);
                    comp.fortifyNearestHostile = threatInRange ?? nearestAny;
                    // Legacy territory/frontier fields kept for save-compat; unused by sequential kit fortify.
                    comp.fortifyIsFrontier = false;
                    comp.fortifyTerritoryId = -1;
                }
            }
        }

        public static bool IsFortifyEligible(Settlement actor, CompViralSpread comp)
        {
            if (actor == null || comp == null) return false;
            if (!comp.IsSettlement || comp.IsOutpost || actor.Faction == null || actor.Faction.IsPlayer) return false;
            if (comp.IsFortifyOnCooldown) return false;
            if (!comp.fortifyThreatenedToday) return false;

            WorldObject threat = ResolveThreat(actor, comp);
            if (threat == null) return false;

            var phase = WorldActions_FortifyKit.ResolveNextPhase(
                actor.Tile.tileId, actor.Faction, comp.tier, actor);
            if (phase != WorldActions_FortifyKit.FortifyPhase.Complete)
                return true;
            return HasOwnTownClearWork(actor);
        }

        private static bool HasOwnTownClearWork(Settlement actor)
        {
            var seth = WorldDominationMod.settings;
            if (seth == null || !seth.npcFortifyKeepOwnPathsOpen || !seth.npcFortifyClearOwnPathBlocks)
                return false;
            WorldActions_FortifyKit.CollectOwnTownClearBlockTiles(actor, actor.Faction, tempPhaseTiles);
            return tempPhaseTiles.Count > 0;
        }

        public static bool AttemptFortify(WorldObject actorWo, CompViralSpread comp, WorldComponent_SpreadManager manager)
        {
            if (!(actorWo is Settlement actor) || comp == null || manager == null) return false;
            var seth = WorldDominationMod.settings;
            if (seth == null) return false;
            if (!IsFortifyEligible(actor, comp)) return false;

            SettlementTier kitTier = comp.tier;
            WorldActions_FortifyKit.GetKit(
                kitTier, out SpikeTrapKind trapKind, out RoadBlockKind blockKind, out _, out int maxAt, actor.Faction);

            bool ownTownsOpen = seth.npcFortifyKeepOwnPathsOpen
                && WorldActions_FortifyKit.TryFillOwnTownOpenRingTiles(actor, actor.Faction, tempOpenRing);

            var phase = WorldActions_FortifyKit.ResolveNextPhase(
                actor.Tile.tileId, actor.Faction, kitTier, actor);

            float placeCost = Mathf.Max(1f, seth.fortifyTravelerStrength);
            float clearCost = Mathf.Max(1f, WorldDominationSettings.DefFortifyClearPathTravelerStrength);
            pickedFortifyTiles.Clear();

            int crewBudget = RollFortifyCaravanCount(comp.tier, actor.Faction);
            int launched = 0;

            if (seth.npcFortifyKeepOwnPathsOpen && seth.npcFortifyClearOwnPathBlocks)
            {
                WorldActions_FortifyKit.CollectOwnTownClearBlockTiles(actor, actor.Faction, tempPhaseTiles);
                if (tempPhaseTiles.Count > 0)
                {
                    int clearCap = Mathf.Clamp(seth.npcFortifyClearPathCrewsMax, 1, 3);
                    int clearLaunched = LaunchClearCrews(
                        actor, comp, manager, clearCost, tempPhaseTiles, clearCap, crewBudget, ownTownsOpen: true);
                    launched += clearLaunched;
                    crewBudget = Mathf.Max(0, crewBudget - clearLaunched);
                }
            }

            if (crewBudget > 0 && phase != WorldActions_FortifyKit.FortifyPhase.Complete)
            {
                if (phase == WorldActions_FortifyKit.FortifyPhase.Traps)
                {
                    WorldActions_FortifyKit.EnsureRingHasRoadExit(actor.Tile.tileId, kitTier);
                    WorldActions_FortifyKit.CollectMissingTrapTiles(
                        actor.Tile.tileId, actor.Faction, trapKind, actor, pickedFortifyTiles, tempPhaseTiles);
                    launched += LaunchTrapOrBlockCrews(
                        actor, comp, manager, placeCost, placeTrap: true, trapKind, blockKind,
                        tempPhaseTiles, crewBudget, ownTownsOpen);
                }
                else if (phase == WorldActions_FortifyKit.FortifyPhase.Blocks)
                {
                    WorldActions_FortifyKit.CollectMissingBlockTiles(
                        actor.Tile.tileId, actor.Faction, blockKind, actor, pickedFortifyTiles, tempPhaseTiles);
                    launched += LaunchTrapOrBlockCrews(
                        actor, comp, manager, placeCost, placeTrap: false, trapKind, blockKind,
                        tempPhaseTiles, crewBudget, ownTownsOpen);
                }
                else if (phase == WorldActions_FortifyKit.FortifyPhase.AtTurrets)
                {
                    WorldActions_FortifyKit.CollectMissingAtTiles(
                        actor.Tile.tileId, actor.Faction, maxAt, actor, pickedFortifyTiles, tempPhaseTiles);
                    if (tempPhaseTiles.Count >= 1
                        && WorldActions_Utils.CanAffordExpeditionLeavingGarrison(comp, placeCost, seth))
                    {
                        int tile = tempPhaseTiles[0];
                        if (SpawnNpcAtTurretTraveler(actor, tile, placeCost, ownTownsOpen))
                        {
                            manager.AddLog(new SpreadLogEntry(
                                ownTownsOpen
                                    ? "TSA_WD_Log_Fortify_OwnTowns_DedicatedAtTurret".Translate(actor.LabelCap, tile.ToString())
                                    : "TSA_WD_Log_Fortify_DedicatedAtTurret".Translate(actor.LabelCap, tile.ToString()),
                                actor,
                                tile)
                            {
                                category = ownTownsOpen
                                    ? SpreadLogCategory.FortifyOwnTownsOpen
                                    : SpreadLogCategory.BuiltFortification
                            });
                            launched++;
                        }
                    }
                }
            }

            if (launched < 1) return false;

            float cdDays = Mathf.Max(0f, seth.cooldownFortifyDays);
            comp.fortifyCooldownTick = Find.TickManager.TicksGame + Mathf.RoundToInt(cdDays * 60000f);
            return true;
        }

        private static int LaunchClearCrews(
            Settlement actor,
            CompViralSpread comp,
            WorldComponent_SpreadManager manager,
            float cost,
            List<int> candidates,
            int clearCap,
            int crewBudget,
            bool ownTownsOpen)
        {
            var seth = WorldDominationMod.settings;
            if (seth == null || candidates == null || candidates.Count == 0) return 0;

            int maxAffordable = WorldActions_Utils.MaxAffordableExpeditionsLeavingGarrison(comp, cost, seth);
            int toLaunch = Mathf.Min(clearCap, crewBudget, maxAffordable, candidates.Count);
            if (toLaunch < 1) return 0;

            int launched = 0;
            int nextDue = Find.TickManager.TicksGame;

            for (int i = 0; i < toLaunch; i++)
            {
                if (!WorldActions_Utils.CanAffordExpeditionLeavingGarrison(comp, cost, seth))
                    break;

                int tile = -1;
                for (int c = 0; c < candidates.Count; c++)
                {
                    int cand = candidates[c];
                    if (pickedFortifyTiles.Contains(cand)) continue;
                    tile = cand;
                    break;
                }
                if (tile < 0) break;

                if (i == 0)
                {
                    if (!SpawnFortifyTraveler(actor, tile, cost, placeTrap: false, default, default,
                            clearing: true, ownTownsOpen: ownTownsOpen))
                        break;
                    manager.AddLog(new SpreadLogEntry(
                        "TSA_WD_Log_Fortify_RemoveBlock".Translate(actor.LabelCap, tile.ToString()),
                        actor,
                        tile) { category = SpreadLogCategory.ClearedFortification });
                }
                else
                {
                    comp.strength = Mathf.Max(0f, comp.strength - cost);
                    comp.CheckTierUpdate(false);
                    nextDue += WorldActions_NpcLaunchStagger.NextGapTicks();
                    WorldActions_NpcLaunchStagger.EnqueueFortify(
                        nextDue, actor, tile, cost, placeTrap: false, default, default,
                        clearing: true, ownTownsOpen: ownTownsOpen);
                }

                pickedFortifyTiles.Add(tile);
                launched++;
            }
            return launched;
        }

        private static int LaunchTrapOrBlockCrews(
            Settlement actor,
            CompViralSpread comp,
            WorldComponent_SpreadManager manager,
            float cost,
            bool placeTrap,
            SpikeTrapKind trapKind,
            RoadBlockKind blockKind,
            List<int> candidates,
            int crewBudget,
            bool ownTownsOpen)
        {
            var seth = WorldDominationMod.settings;
            if (seth == null || candidates == null || candidates.Count == 0 || crewBudget < 1) return 0;

            int maxAffordable = WorldActions_Utils.MaxAffordableExpeditionsLeavingGarrison(comp, cost, seth);
            int toLaunch = Mathf.Min(crewBudget, maxAffordable, candidates.Count);
            if (toLaunch < 1) return 0;

            int launched = 0;
            int nextDue = Find.TickManager.TicksGame;

            for (int i = 0; i < toLaunch; i++)
            {
                if (!WorldActions_Utils.CanAffordExpeditionLeavingGarrison(comp, cost, seth))
                    break;

                int tile = -1;
                for (int c = 0; c < candidates.Count; c++)
                {
                    int cand = candidates[c];
                    if (pickedFortifyTiles.Contains(cand)) continue;
                    tile = cand;
                    break;
                }
                if (tile < 0) break;

                if (i == 0)
                {
                    if (!SpawnFortifyTraveler(actor, tile, cost, placeTrap, trapKind, blockKind,
                            clearing: false, ownTownsOpen: ownTownsOpen))
                        break;
                    manager.AddLog(MakePlaceLaunchLog(actor, tile, placeTrap, ownTownsOpen));
                }
                else
                {
                    comp.strength = Mathf.Max(0f, comp.strength - cost);
                    comp.CheckTierUpdate(false);
                    nextDue += WorldActions_NpcLaunchStagger.NextGapTicks();
                    WorldActions_NpcLaunchStagger.EnqueueFortify(
                        nextDue, actor, tile, cost, placeTrap, trapKind, blockKind,
                        clearing: false, ownTownsOpen: ownTownsOpen);
                }

                pickedFortifyTiles.Add(tile);
                launched++;
            }
            return launched;
        }

        private static SpreadLogEntry MakePlaceLaunchLog(Settlement actor, int tile, bool placeTrap, bool ownTownsOpen)
        {
            string key = ownTownsOpen
                ? (placeTrap ? "TSA_WD_Log_Fortify_OwnTowns_Trap" : "TSA_WD_Log_Fortify_OwnTowns")
                : "TSA_WD_Log_Fortify";
            return new SpreadLogEntry(key.Translate(actor.LabelCap, tile.ToString()), actor, tile)
            {
                category = ownTownsOpen
                    ? SpreadLogCategory.FortifyOwnTownsOpen
                    : SpreadLogCategory.BuiltFortification
            };
        }

        /// <summary>
        /// Per-tier chance to launch extra fortify caravans (also used by NPC road multi-launch).
        /// T1–T3: chance of 2 (else 1). T4: chance of 3 (else 2).
        /// </summary>
        public static int RollFortifyCaravanCount(SettlementTier tier, Faction faction = null)
        {
            var s = WorldDominationMod.settings;
            int count;
            switch (tier)
            {
                case SettlementTier.T4:
                {
                    float p3 = Mathf.Clamp01(s?.fortifyMultiT4ChanceOf3
                        ?? WorldDominationSettings.DefFortifyMultiT4ChanceOf3);
                    count = Rand.Value < p3 ? 3 : 2;
                    break;
                }
                case SettlementTier.T3:
                {
                    float p2 = Mathf.Clamp01(s?.fortifyMultiT3ChanceOf2
                        ?? WorldDominationSettings.DefFortifyMultiT3ChanceOf2);
                    count = Rand.Value < p2 ? 2 : 1;
                    break;
                }
                case SettlementTier.T2:
                {
                    float p2 = Mathf.Clamp01(s?.fortifyMultiT2ChanceOf2
                        ?? WorldDominationSettings.DefFortifyMultiT2ChanceOf2);
                    count = Rand.Value < p2 ? 2 : 1;
                    break;
                }
                default:
                {
                    float p2 = Mathf.Clamp01(s?.fortifyMultiT1ChanceOf2
                        ?? WorldDominationSettings.DefFortifyMultiT1ChanceOf2);
                    count = Rand.Value < p2 ? 2 : 1;
                    break;
                }
            }

            float dens = WorldActions_FortifyKit.FortifyDensityMult(faction);
            if (dens > 1.01f && Rand.Chance(dens - 1f))
                count++;
            else if (dens < 0.99f && count > 1 && Rand.Chance(1f - dens))
                count--;
            return Mathf.Max(1, count);
        }

        public static void ExecuteFortifyArrival(WorldObject_Traveler traveler)
        {
            if (traveler == null) return;
            Settlement origin = traveler.originObject as Settlement;
            // Origin razed mid-flight: crew should already be aborted; never orphan-place under a dead builder.
            if (origin == null || origin.Destroyed) return;
            int tile = traveler.Tile.tileId;
            Faction faction = traveler.Faction ?? origin.Faction;

            if (traveler.fortifyIsClearing)
            {
                ExecuteFortifyClearArrival(traveler, origin, faction, tile);
                return;
            }

            if (WorldComponent_FortifyBlacklist.BlocksNpcFortify(tile, faction))
                return;

            bool placed;
            if (traveler.fortifyIsTrap)
            {
                placed = WorldComponent_SpikeTraps.Get()?.TryPlaceOrUpgrade(
                    tile, faction, traveler.fortifySpikeTrapKind, origin) == true;
            }
            else
            {
                placed = WorldComponent_RoadBlocks.Get()?.TryPlaceOrUpgrade(
                    tile, faction, traveler.fortifyRoadBlockKind, origin) == true;
            }

            if (!placed) return;

            bool ownTownsOpen = traveler.fortifyOwnTownsOpen;
            string msg;
            SpreadLogCategory cat;
            if (ownTownsOpen)
            {
                cat = SpreadLogCategory.FortifyOwnTownsOpen;
                msg = traveler.fortifyIsTrap
                    ? "TSA_WD_Log_Fortify_OwnTowns_SpikeTrapComplete".Translate(origin.LabelCap, tile).ToString()
                    : "TSA_WD_Log_Fortify_OwnTowns_RoadBlockComplete".Translate(origin.LabelCap, tile).ToString();
            }
            else
            {
                cat = SpreadLogCategory.BuiltFortification;
                msg = traveler.fortifyIsTrap
                    ? "TSA_WD_Log_Fortify_SpikeTrapComplete".Translate(origin.LabelCap, tile).ToString()
                    : "TSA_WD_Log_Fortify_RoadBlockComplete".Translate(origin.LabelCap, tile).ToString();
            }
            Find.World?.GetComponent<WorldComponent_SpreadManager>()?.AddLog(
                new SpreadLogEntry(msg, origin, tile) { category = cat });
        }

        private static void ExecuteFortifyClearArrival(
            WorldObject_Traveler traveler,
            Settlement origin,
            Faction faction,
            int tile)
        {
            // Re-validate gate: still an own-town open tile with a same-faction block.
            if (!WorldActions_FortifyKit.TryFillOwnTownOpenRingTiles(origin, faction, tempOpenRing)
                || !tempOpenRing.Contains(tile))
                return;
            var blocks = WorldComponent_RoadBlocks.Get();
            if (blocks == null || !blocks.TryGet(tile, out RoadBlockRecord rec) || rec == null)
                return;
            if (rec.builtByFaction != null && rec.builtByFaction != faction)
                return;
            if (blocks.TryClear(tile) != true)
                return;

            Find.World?.GetComponent<WorldComponent_SpreadManager>()?.AddLog(
                new SpreadLogEntry(
                    "TSA_WD_Log_Fortify_RemoveBlockComplete".Translate(origin.LabelCap, tile.ToString()),
                    origin,
                    tile) { category = SpreadLogCategory.ClearedFortification });
        }

        public static void ExecuteNpcAtTurretArrival(WorldObject_Traveler traveler)
        {
            if (traveler == null) return;
            Settlement origin = traveler.originObject as Settlement;
            if (origin == null || origin.Destroyed) return;
            int tile = traveler.Tile.tileId;
            Faction faction = traveler.Faction ?? origin.Faction;
            if (faction == null) return;
            if (WorldComponent_FortifyBlacklist.BlocksNpcFortify(tile, faction))
                return;

            var originComp = origin.GetComponent<CompViralSpread>();
            SettlementTier kitTier = originComp?.tier ?? SettlementTier.T1;
            WorldActions_FortifyKit.GetKit(kitTier, out _, out _, out AtTurretTier atTier, out _);

            WorldObject_AT_Turret turret = AtTurretUtility.TrySpawn(
                tile, faction, atTier, origin,
                requirePlayerBuildSite: false,
                allowRoadTile: true,
                ignoreSettlementCap: false);
            if (turret == null) return;

            bool ownTownsOpen = traveler.fortifyOwnTownsOpen;
            Find.World?.GetComponent<WorldComponent_SpreadManager>()?.AddLog(
                new SpreadLogEntry(
                    ownTownsOpen
                        ? "TSA_WD_Log_Fortify_OwnTowns_AT_TurretComplete".Translate(origin.LabelCap, tile.ToString())
                        : "TSA_WD_Log_Fortify_AT_TurretComplete".Translate(origin.LabelCap, tile.ToString()),
                    origin,
                    tile)
                {
                    category = ownTownsOpen
                        ? SpreadLogCategory.FortifyOwnTownsOpen
                        : SpreadLogCategory.BuiltFortification
                });
        }

        private static bool SpawnNpcAtTurretTraveler(Settlement origin, int destTile, float cost, bool ownTownsOpen = false)
        {
            var comp = origin.GetComponent<CompViralSpread>();
            if (comp == null) return false;
            if (!WorldActions_Utils.CanAffordExpeditionLeavingGarrison(comp, cost)) return false;
            if (!WorldActions_Utils.TryConsumeExpeditionStrength(comp, cost)) return false;

            WorldObjectDef def = DefDatabase<WorldObjectDef>.GetNamedSilentFail("TSA_WD_Traveler_Outpost_RoadBlock")
                ?? DefDatabase<WorldObjectDef>.GetNamedSilentFail("TSA_WD_Traveler_Outpost_RoadBuilder");
            if (def == null)
            {
                WorldActions_Utils.RefundExpeditionStrength(comp, cost);
                return false;
            }

            WorldObject_Traveler traveler = (WorldObject_Traveler)WorldObjectMaker.MakeWorldObject(def);
            traveler.Tile = origin.Tile;
            traveler.SetFaction(origin.Faction);
            traveler.mission = TravelerMission.NpcAtTurret;
            traveler.originObject = origin;
            traveler.travelerStrength = cost;
            traveler.initialStrength = cost;
            traveler.fortifyOwnTownsOpen = ownTownsOpen;
            AdvancedBuildingEffects.ApplyNpcLandSpeed(traveler, origin);

            Find.WorldObjects.Add(traveler);
            traveler.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(destTile, origin));
            if (traveler.Destroyed)
            {
                WorldActions_Utils.RefundExpeditionStrength(comp, cost);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Builder settlement razed, captured, or removed: nuke its advanced buildings, AT turrets,
        /// road blocks, traps, trenches, and abort in-flight fortify crews.
        /// Always on; legacy <c>fortifyClearOnBuilderLoss</c> is still scribed but no longer read.
        /// </summary>
        public static void NotifyBuilderLost(Settlement settlement)
        {
            if (settlement == null) return;
            DestroyActiveNpcFortifyCrewsFrom(settlement);
            AtTurretUtility.DestroyTurretsBuiltBy(settlement);
            WorldComponent_RoadBlocks.Get()?.ClearBuiltBySettlement(settlement);
            WorldComponent_SpikeTraps.Get()?.ClearBuiltBySettlement(settlement);
            WorldComponent_AdvancedBuildings.Get()?.DestroyAllOwnedBy(settlement);
            WorldActions_Trenches.WipeForOwner(settlement);
        }

        /// <summary>Kill live NPC fortify / AT crews whose origin is this settlement (no orphan placements after razing).</summary>
        public static void DestroyActiveNpcFortifyCrewsFrom(WorldObject origin)
        {
            if (origin == null) return;
            IReadOnlyList<WorldObject_Traveler> live = WorldObject_Traveler.LiveTravelers;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                WorldObject_Traveler t = live[i];
                if (t == null || t.Destroyed) continue;
                if (t.originObject != origin) continue;
                if (t.mission != TravelerMission.NpcFortify && t.mission != TravelerMission.NpcAtTurret)
                    continue;
                t.suppressDestroyedWorldFx = true;
                t.Destroy();
            }
        }

        private static WorldObject ResolveThreat(Settlement actor, CompViralSpread comp)
        {
            if (comp?.fortifyNearestHostile != null
                && !comp.fortifyNearestHostile.Destroyed
                && comp.fortifyNearestHostile.Tile >= 0)
                return comp.fortifyNearestHostile;

            var seth = WorldDominationMod.settings;
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (IsThreatenedToday(actor, comp, manager, seth, out WorldObject threat))
                return threat;
            return null;
        }

        private static void CollectHostilesForFaction(Faction self, List<WorldObject> into)
        {
            into.Clear();
            if (self == null) return;

            var settlements = Find.WorldObjects?.Settlements;
            if (settlements != null)
            {
                for (int i = 0; i < settlements.Count; i++)
                {
                    Settlement s = settlements[i];
                    if (s == null || s.Destroyed || s.Tile < 0 || s.Faction == null) continue;
                    if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(s)) continue;
                    if (!IsHostileThreat(self, s)) continue;
                    into.Add(s);
                }
            }

            var worldObjects = Find.WorldObjects?.AllWorldObjects;
            if (worldObjects == null) return;
            for (int i = 0; i < worldObjects.Count; i++)
            {
                if (!(worldObjects[i] is WorldObject_WD_Outpost outpost)) continue;
                if (outpost.Destroyed || outpost.Tile < 0 || outpost.Faction == null) continue;
                if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(outpost)) continue;
                if (!IsHostileThreat(self, outpost)) continue;
                into.Add(outpost);
            }
        }

        private static void FindNearestHostile(
            Settlement actor,
            List<WorldObject> candidates,
            out WorldObject nearest,
            out float bestDist)
        {
            nearest = null;
            bestDist = float.MaxValue;
            if (actor?.Faction == null || candidates == null) return;
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || actor.Tile < 0) return;

            for (int i = 0; i < candidates.Count; i++)
            {
                WorldObject other = candidates[i];
                if (other == null || other == actor || other.Destroyed || other.Tile < 0) continue;
                if (!IsHostileThreat(actor.Faction, other)) continue;
                float dist = grid.ApproxDistanceInTiles(actor.Tile, other.Tile);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    nearest = other;
                }
            }
        }

        private static bool IsThreatenedToday(
            Settlement actor,
            CompViralSpread comp,
            WorldComponent_SpreadManager manager,
            WorldDominationSettings seth,
            out WorldObject nearestThreat)
        {
            nearestThreat = null;
            if (actor == null || actor.Tile < 0 || actor.Faction == null) return false;

            float range = SettlementAttackRangeUtil.GetNpcSettlementAttackRangeWithZeal(actor, seth, manager);
            float bestDist = float.MaxValue;

            var settlements = Find.WorldObjects?.Settlements;
            if (settlements != null)
            {
                for (int i = 0; i < settlements.Count; i++)
                {
                    Settlement other = settlements[i];
                    if (other == null || other == actor || other.Destroyed || other.Tile < 0) continue;
                    if (other.Faction == null) continue;
                    if (!IsHostileThreat(actor.Faction, other)) continue;
                    if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(other)) continue;
                    float dist = Find.WorldGrid.ApproxDistanceInTiles(actor.Tile, other.Tile);
                    if (dist > range) continue;
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        nearestThreat = other;
                    }
                }
            }

            var worldObjects = Find.WorldObjects?.AllWorldObjects;
            if (worldObjects != null)
            {
                for (int i = 0; i < worldObjects.Count; i++)
                {
                    if (!(worldObjects[i] is WorldObject_WD_Outpost outpost)) continue;
                    if (outpost.Destroyed || outpost.Tile < 0 || outpost.Faction == null) continue;
                    if (!IsHostileThreat(actor.Faction, outpost)) continue;
                    if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(outpost)) continue;
                    float dist = Find.WorldGrid.ApproxDistanceInTiles(actor.Tile, outpost.Tile);
                    if (dist > range) continue;
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        nearestThreat = outpost;
                    }
                }
            }

            return nearestThreat != null;
        }

        private static bool IsHostileThreat(Faction self, WorldObject other)
        {
            if (self == null || other?.Faction == null) return false;
            if (other.Faction == self) return false;
            if (other.Faction.IsPlayer) return true;
            if (!WorldActions_Utils.IsWdHostileTargetFaction(other.Faction)) return false;
            return WorldActions_Utils.SafeHostileTo(self, other.Faction);
        }

        private static bool SpawnFortifyTraveler(
            Settlement origin,
            int destTile,
            float cost,
            bool placeTrap,
            SpikeTrapKind trapKind,
            RoadBlockKind blockKind,
            bool clearing = false,
            bool ownTownsOpen = false)
        {
            var comp = origin.GetComponent<CompViralSpread>();
            if (comp == null) return false;
            if (!WorldActions_Utils.CanAffordExpeditionLeavingGarrison(comp, cost)) return false;
            comp.strength = Mathf.Max(0f, comp.strength - cost);
            comp.CheckTierUpdate(false);
            if (SpawnFortifyTravelerPrepaid(
                    origin, destTile, cost, placeTrap, trapKind, blockKind, clearing, ownTownsOpen))
                return true;
            comp.AddStrength(cost);
            return false;
        }

        /// <summary>Spawn after strength was already reserved (staggered multi-launch).</summary>
        internal static bool SpawnFortifyTravelerPrepaid(
            Settlement origin,
            int destTile,
            float cost,
            bool placeTrap,
            SpikeTrapKind trapKind,
            RoadBlockKind blockKind,
            bool clearing = false,
            bool ownTownsOpen = false)
        {
            var comp = origin.GetComponent<CompViralSpread>();
            if (comp == null) return false;

            string defName = placeTrap && !clearing
                ? "TSA_WD_Traveler_Outpost_SpikeTrap"
                : "TSA_WD_Traveler_Outpost_RoadBlock";
            WorldObjectDef def = DefDatabase<WorldObjectDef>.GetNamed(defName, false)
                ?? DefDatabase<WorldObjectDef>.GetNamed("TSA_WD_Traveler_Outpost_RoadBuilder", false);
            if (def == null) return false;

            WorldObject_Traveler traveler = (WorldObject_Traveler)WorldObjectMaker.MakeWorldObject(def);
            traveler.Tile = origin.Tile;
            traveler.SetFaction(origin.Faction);
            traveler.mission = TravelerMission.NpcFortify;
            traveler.originObject = origin;
            traveler.travelerStrength = cost;
            traveler.initialStrength = cost;
            traveler.fortifyIsTrap = placeTrap && !clearing;
            traveler.fortifySpikeTrapKind = trapKind;
            traveler.fortifyRoadBlockKind = blockKind;
            traveler.fortifyIsClearing = clearing;
            traveler.fortifyOwnTownsOpen = ownTownsOpen;
            AdvancedBuildingEffects.ApplyNpcLandSpeed(traveler, origin);

            Find.WorldObjects.Add(traveler);
            traveler.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(destTile, origin));
            if (traveler.Destroyed)
                return false;
            return true;
        }
    }
}
