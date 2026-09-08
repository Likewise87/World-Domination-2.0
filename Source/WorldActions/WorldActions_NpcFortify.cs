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
            return phase != WorldActions_FortifyKit.FortifyPhase.Complete;
        }

        public static bool AttemptFortify(WorldObject actorWo, CompViralSpread comp, WorldComponent_SpreadManager manager)
        {
            if (!(actorWo is Settlement actor) || comp == null || manager == null) return false;
            var seth = WorldDominationMod.settings;
            if (seth == null) return false;
            if (!IsFortifyEligible(actor, comp)) return false;

            SettlementTier kitTier = comp.tier;
            WorldActions_FortifyKit.GetKit(
                kitTier, out SpikeTrapKind trapKind, out RoadBlockKind blockKind, out _, out int maxAt);

            var phase = WorldActions_FortifyKit.ResolveNextPhase(
                actor.Tile.tileId, actor.Faction, kitTier, actor);
            if (phase == WorldActions_FortifyKit.FortifyPhase.Complete)
                return false;

            float cost = Mathf.Max(1f, seth.fortifyTravelerStrength);
            pickedFortifyTiles.Clear();

            if (phase == WorldActions_FortifyKit.FortifyPhase.Traps)
            {
                // Paint before travelers so trap dests exist; paving clears forts on paved tiles.
                WorldActions_FortifyKit.EnsureRingHasRoadExit(actor.Tile.tileId, kitTier);
                WorldActions_FortifyKit.CollectMissingTrapTiles(
                    actor.Tile.tileId, actor.Faction, trapKind, actor, pickedFortifyTiles, tempPhaseTiles);
                return LaunchTrapOrBlockCrews(
                    actor, comp, manager, cost, placeTrap: true, trapKind, blockKind, tempPhaseTiles);
            }

            if (phase == WorldActions_FortifyKit.FortifyPhase.Blocks)
            {
                WorldActions_FortifyKit.CollectMissingBlockTiles(
                    actor.Tile.tileId, actor.Faction, blockKind, actor, pickedFortifyTiles, tempPhaseTiles);
                return LaunchTrapOrBlockCrews(
                    actor, comp, manager, cost, placeTrap: false, trapKind, blockKind, tempPhaseTiles);
            }

            // AT phase: one crew.
            WorldActions_FortifyKit.CollectMissingAtTiles(
                actor.Tile.tileId, actor.Faction, maxAt, pickedFortifyTiles, tempPhaseTiles);
            if (tempPhaseTiles.Count < 1) return false;
            if (!WorldActions_Utils.CanAffordExpeditionLeavingGarrison(comp, cost, seth)) return false;

            int tile = tempPhaseTiles[0];
            if (!SpawnNpcAtTurretTraveler(actor, tile, cost))
                return false;

            manager.AddLog(new SpreadLogEntry(
                "TSA_WD_Log_Fortify_DedicatedAtTurret".Translate(actor.LabelCap, tile.ToString()),
                actor,
                tile));

            float cdDays = Mathf.Max(0f, seth.cooldownFortifyDays);
            comp.fortifyCooldownTick = Find.TickManager.TicksGame + Mathf.RoundToInt(cdDays * 60000f);
            return true;
        }

        private static bool LaunchTrapOrBlockCrews(
            Settlement actor,
            CompViralSpread comp,
            WorldComponent_SpreadManager manager,
            float cost,
            bool placeTrap,
            SpikeTrapKind trapKind,
            RoadBlockKind blockKind,
            List<int> candidates)
        {
            var seth = WorldDominationMod.settings;
            if (seth == null || candidates == null || candidates.Count == 0) return false;

            int desired = RollFortifyCaravanCount(comp.tier);
            int maxAffordable = WorldActions_Utils.MaxAffordableExpeditionsLeavingGarrison(comp, cost, seth);
            int toLaunch = Mathf.Min(desired, maxAffordable, candidates.Count);
            if (toLaunch < 1) return false;

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
                    if (!SpawnFortifyTraveler(actor, tile, cost, placeTrap, trapKind, blockKind))
                        break;
                    manager.AddLog(new SpreadLogEntry(
                        "TSA_WD_Log_Fortify".Translate(actor.LabelCap, tile.ToString()),
                        actor,
                        tile));
                }
                else
                {
                    comp.strength = Mathf.Max(0f, comp.strength - cost);
                    comp.CheckTierUpdate(false);
                    nextDue += WorldActions_NpcLaunchStagger.NextGapTicks();
                    WorldActions_NpcLaunchStagger.EnqueueFortify(
                        nextDue, actor, tile, cost, placeTrap, trapKind, blockKind);
                }

                pickedFortifyTiles.Add(tile);
                launched++;
            }

            if (launched < 1) return false;

            float cdDays = Mathf.Max(0f, seth.cooldownFortifyDays);
            comp.fortifyCooldownTick = Find.TickManager.TicksGame + Mathf.RoundToInt(cdDays * 60000f);
            return true;
        }

        /// <summary>
        /// Per-tier chance to launch extra fortify caravans (also used by NPC road multi-launch).
        /// T1–T3: chance of 2 (else 1). T4: chance of 3 (else 2).
        /// </summary>
        public static int RollFortifyCaravanCount(SettlementTier tier)
        {
            var s = WorldDominationMod.settings;
            switch (tier)
            {
                case SettlementTier.T4:
                {
                    float p3 = Mathf.Clamp01(s?.fortifyMultiT4ChanceOf3
                        ?? WorldDominationSettings.DefFortifyMultiT4ChanceOf3);
                    return Rand.Value < p3 ? 3 : 2;
                }
                case SettlementTier.T3:
                {
                    float p2 = Mathf.Clamp01(s?.fortifyMultiT3ChanceOf2
                        ?? WorldDominationSettings.DefFortifyMultiT3ChanceOf2);
                    return Rand.Value < p2 ? 2 : 1;
                }
                case SettlementTier.T2:
                {
                    float p2 = Mathf.Clamp01(s?.fortifyMultiT2ChanceOf2
                        ?? WorldDominationSettings.DefFortifyMultiT2ChanceOf2);
                    return Rand.Value < p2 ? 2 : 1;
                }
                default:
                {
                    float p2 = Mathf.Clamp01(s?.fortifyMultiT1ChanceOf2
                        ?? WorldDominationSettings.DefFortifyMultiT1ChanceOf2);
                    return Rand.Value < p2 ? 2 : 1;
                }
            }
        }

        public static void ExecuteFortifyArrival(WorldObject_Traveler traveler)
        {
            if (traveler == null) return;
            Settlement origin = traveler.originObject as Settlement;
            int tile = traveler.Tile.tileId;
            Faction faction = traveler.Faction ?? origin?.Faction;

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

            if (!placed || origin == null) return;

            string msg = traveler.fortifyIsTrap
                ? "TSA_WD_Log_Fortify_SpikeTrapComplete".Translate(origin.LabelCap, tile).ToString()
                : "TSA_WD_Log_Fortify_RoadBlockComplete".Translate(origin.LabelCap, tile).ToString();
            Find.World?.GetComponent<WorldComponent_SpreadManager>()?.AddLog(
                new SpreadLogEntry(msg, origin, tile));
        }

        public static void ExecuteNpcAtTurretArrival(WorldObject_Traveler traveler)
        {
            if (traveler == null) return;
            Settlement origin = traveler.originObject as Settlement;
            int tile = traveler.Tile.tileId;
            Faction faction = traveler.Faction ?? origin?.Faction;
            if (origin == null || faction == null) return;
            if (WorldComponent_FortifyBlacklist.BlocksNpcFortify(tile, faction))
                return;

            var originComp = origin.GetComponent<CompViralSpread>();
            SettlementTier kitTier = originComp?.tier ?? SettlementTier.T1;
            WorldActions_FortifyKit.GetKit(kitTier, out _, out _, out AtTurretTier atTier, out _);

            WorldObject_AT_Turret turret = AtTurretUtility.TrySpawn(
                tile, faction, atTier, origin,
                requirePlayerBuildSite: false,
                allowRoadTile: true,
                ignoreSettlementCap: true);
            if (turret == null) return;

            Find.World?.GetComponent<WorldComponent_SpreadManager>()?.AddLog(
                new SpreadLogEntry(
                    "TSA_WD_Log_Fortify_AT_TurretComplete".Translate(origin.LabelCap, tile.ToString()),
                    origin,
                    tile));
        }

        private static bool SpawnNpcAtTurretTraveler(Settlement origin, int destTile, float cost)
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

            Find.WorldObjects.Add(traveler);
            traveler.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(destTile, origin));
            if (traveler.Destroyed)
            {
                WorldActions_Utils.RefundExpeditionStrength(comp, cost);
                return false;
            }
            return true;
        }

        public static void NotifyBuilderLost(Settlement settlement)
        {
            var seth = WorldDominationMod.settings;
            if (seth == null || !seth.fortifyClearOnBuilderLoss || settlement == null) return;
            WorldComponent_RoadBlocks.Get()?.ClearBuiltBySettlement(settlement);
            WorldComponent_SpikeTraps.Get()?.ClearBuiltBySettlement(settlement);
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
            return WorldActions_Utils.SafeHostileTo(self, other.Faction);
        }

        private static bool SpawnFortifyTraveler(
            Settlement origin,
            int destTile,
            float cost,
            bool placeTrap,
            SpikeTrapKind trapKind,
            RoadBlockKind blockKind)
        {
            var comp = origin.GetComponent<CompViralSpread>();
            if (comp == null) return false;
            if (!WorldActions_Utils.CanAffordExpeditionLeavingGarrison(comp, cost)) return false;
            comp.strength = Mathf.Max(0f, comp.strength - cost);
            comp.CheckTierUpdate(false);
            if (SpawnFortifyTravelerPrepaid(origin, destTile, cost, placeTrap, trapKind, blockKind))
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
            RoadBlockKind blockKind)
        {
            var comp = origin.GetComponent<CompViralSpread>();
            if (comp == null) return false;

            string defName = placeTrap
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
            traveler.fortifyIsTrap = placeTrap;
            traveler.fortifySpikeTrapKind = trapKind;
            traveler.fortifyRoadBlockKind = blockKind;

            Find.WorldObjects.Add(traveler);
            traveler.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(destTile, origin));
            if (traveler.Destroyed)
                return false;
            return true;
        }
    }
}
