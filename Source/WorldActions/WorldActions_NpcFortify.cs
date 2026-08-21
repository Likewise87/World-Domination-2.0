using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// NPC Fortify: threat-gated daily action. After Fortify is chosen, roll one placement type
    /// (road block / trap / AT Turret among valid options), then pick tiles with type-specific bias.
    /// Road block and trap may launch multiple contiguous crews; AT Turret is always one crew.
    /// Interior settlements travel far (within fortifyMaxTravelTiles) or delegate beyond that.
    /// </summary>
    public static class WorldActions_NpcFortify
    {
        private enum FortifyPlacementKind : byte
        {
            RoadBlock = 0,
            Trap = 1,
            Turret = 2
        }

        private static readonly List<int> tempRoadTiles = new List<int>();
        private static readonly List<int> tempOffRoadTiles = new List<int>();
        private static readonly HashSet<int> pickedFortifyTiles = new HashSet<int>();
        private static readonly HashSet<int> tempExistingFortTiles = new HashSet<int>();
        private static readonly HashSet<int> tempAllRoadBlocks = new HashSet<int>();
        private static readonly List<WorldObject> tempHostiles = new List<WorldObject>();
        private static readonly List<Settlement> tempFactionSettlements = new List<Settlement>();

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

                tempFactionSettlements.Clear();
                for (int i = 0; i < list.Count; i++)
                {
                    Settlement s = list[i];
                    if (!DailyWorldSnapshot.IsSettlementStillValid(s)) continue;
                    if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(s)) continue;
                    var comp = s.GetComponent<CompViralSpread>();
                    if (comp == null) continue;
                    tempFactionSettlements.Add(s);

                    comp.fortifyThreatenedToday = IsThreatenedToday(s, comp, manager, seth, out WorldObject threatInRange);
                    FindNearestHostile(s, tempHostiles, out WorldObject nearestAny, out _);
                    comp.fortifyNearestHostile = threatInRange ?? nearestAny;
                    comp.fortifyIsFrontier = false;
                    comp.fortifyTerritoryId = -1;
                }

                if (tempFactionSettlements.Count == 0) continue;
                BuildTerritoryAndFrontier(tempFactionSettlements, tempHostiles, seth);
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
            if (!TryResolveAnchor(actor, threat, out Settlement anchor, out _))
                return false;
            return HasFreeRing(anchor, actor.Faction, out _, out _);
        }

        public static bool AttemptFortify(WorldObject actorWo, CompViralSpread comp, WorldComponent_SpreadManager manager)
        {
            if (!(actorWo is Settlement actor) || comp == null || manager == null) return false;
            var seth = WorldDominationMod.settings;
            if (seth == null) return false;
            if (!IsFortifyEligible(actor, comp)) return false;

            WorldObject nearestThreat = ResolveThreat(actor, comp);
            if (nearestThreat == null) return false;

            if (!TryResolveAnchor(actor, nearestThreat, out Settlement anchor, out bool travelFar))
                return false;

            if (!HasFreeRing(anchor, actor.Faction, out int ringMin, out int ringMax))
                return false;

            Settlement launcher = travelFar ? actor : anchor;
            CompViralSpread payComp = travelFar ? comp : anchor.GetComponent<CompViralSpread>();
            if (launcher == null || payComp == null) return false;

            float cost = Mathf.Max(1f, seth.fortifyTravelerStrength);
            if (!TryRollFortifyPlacementKind(
                    launcher, anchor, actor, nearestThreat, ringMin, ringMax, seth,
                    out FortifyPlacementKind kind))
                return false;

            // Multi-crew only for road blocks / traps. AT Turret is always a single crew.
            int desired = kind == FortifyPlacementKind.Turret ? 1 : RollFortifyCaravanCount(comp.tier);
            int maxAffordable = WorldActions_Utils.MaxAffordableExpeditionsLeavingGarrison(payComp, cost, seth);
            int toLaunch = Mathf.Min(desired, maxAffordable);
            if (toLaunch < 1) return false;

            GetTierKit(launcher == actor ? comp.tier : payComp.tier, out SpikeTrapKind trapKind, out RoadBlockKind blockKind);
            pickedFortifyTiles.Clear();
            int launched = 0;
            int nextDue = Find.TickManager.TicksGame;

            for (int i = 0; i < toLaunch; i++)
            {
                if (!WorldActions_Utils.CanAffordExpeditionLeavingGarrison(payComp, cost, seth))
                    break;
                if (!TryPickTile(anchor, actor, nearestThreat, ringMin, ringMax, seth, pickedFortifyTiles, kind, out int tile))
                    break;

                if (kind == FortifyPlacementKind.Turret)
                {
                    if (!SpawnNpcAtTurretTraveler(launcher, tile, cost))
                        break;
                    manager.AddLog(new SpreadLogEntry(
                        "TSA_WD_Log_Fortify_DedicatedAtTurret".Translate(launcher.LabelCap, tile.ToString()),
                        launcher,
                        tile));
                    pickedFortifyTiles.Add(tile);
                    launched++;
                    break;
                }

                bool placeTrap = kind == FortifyPlacementKind.Trap;
                if (i == 0)
                {
                    if (!SpawnFortifyTraveler(launcher, tile, cost, placeTrap, trapKind, blockKind))
                        break;
                    if (travelFar)
                    {
                        manager.AddLog(new SpreadLogEntry(
                            "TSA_WD_Log_Fortify".Translate(actor.LabelCap, tile.ToString()),
                            actor,
                            tile));
                    }
                    else
                    {
                        manager.AddLog(new SpreadLogEntry(
                            "TSA_WD_Log_Fortify_Delegated".Translate(actor.LabelCap, launcher.LabelCap, tile.ToString()),
                            actor,
                            tile));
                    }
                }
                else
                {
                    payComp.strength = Mathf.Max(0f, payComp.strength - cost);
                    payComp.CheckTierUpdate(false);
                    nextDue += WorldActions_NpcLaunchStagger.NextGapTicks();
                    WorldActions_NpcLaunchStagger.EnqueueFortify(
                        nextDue, launcher, tile, cost, placeTrap, trapKind, blockKind);
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
            if (!AtTurretUtility.CanBuildAnother(origin)) return;

            var originComp = origin.GetComponent<CompViralSpread>();
            SettlementTier settlementTier = originComp?.tier ?? SettlementTier.T1;
            AtTurretTier tier = AtTurretUtility.PreferredTierForSettlementTier(settlementTier);
            WorldObject_AT_Turret turret = AtTurretUtility.TrySpawn(tile, faction, tier, origin);
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

        private static bool TryResolveAnchor(
            Settlement actor,
            WorldObject threat,
            out Settlement anchor,
            out bool travelFar)
        {
            anchor = null;
            travelFar = true;
            if (actor == null || threat == null || actor.Faction == null) return false;

            var seth = WorldDominationMod.settings;
            int maxTravel = Mathf.Max(1, seth?.fortifyMaxTravelTiles ?? WorldDominationSettings.DefFortifyMaxTravelTiles);
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || actor.Tile < 0 || threat.Tile < 0) return false;

            var actorComp = actor.GetComponent<CompViralSpread>();
            int territoryId = actorComp?.fortifyTerritoryId ?? -1;

            Settlement bestInRange = null;
            float bestInRangeThreatDist = float.MaxValue;
            Settlement bestAny = null;
            float bestAnyThreatDist = float.MaxValue;

            var settlements = Find.WorldObjects?.Settlements;
            if (settlements == null) return false;

            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement s = settlements[i];
                if (s == null || s.Destroyed || s.Tile < 0 || s.Faction != actor.Faction) continue;
                var c = s.GetComponent<CompViralSpread>();
                if (c == null || !c.fortifyIsFrontier) continue;
                if (territoryId >= 0 && c.fortifyTerritoryId != territoryId) continue;

                float dThreat = grid.ApproxDistanceInTiles(s.Tile, threat.Tile);
                float dActor = grid.ApproxDistanceInTiles(actor.Tile, s.Tile);

                if (dThreat < bestAnyThreatDist)
                {
                    bestAnyThreatDist = dThreat;
                    bestAny = s;
                }
                if (dActor <= maxTravel + 0.01f && dThreat < bestInRangeThreatDist)
                {
                    bestInRangeThreatDist = dThreat;
                    bestInRange = s;
                }
            }

            if (bestInRange != null)
            {
                anchor = bestInRange;
                travelFar = true;
                return true;
            }
            if (bestAny != null)
            {
                anchor = bestAny;
                travelFar = false;
                return true;
            }

            // Isolated threatened settlement: fortify locally.
            anchor = actor;
            travelFar = true;
            return true;
        }

        private static void BuildTerritoryAndFrontier(
            List<Settlement> settlements,
            List<WorldObject> hostiles,
            WorldDominationSettings seth)
        {
            int n = settlements.Count;
            if (n == 0) return;

            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return;

            int linkMax = Mathf.Max(1, seth.fortifyTerritoryLinkMaxTiles);
            float eps = Mathf.Max(0f, seth.fortifyFrontierEps);
            int[] parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;

            int FindRoot(int x)
            {
                while (parent[x] != x)
                {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }
                return x;
            }

            void Union(int a, int b)
            {
                int ra = FindRoot(a);
                int rb = FindRoot(b);
                if (ra != rb) parent[rb] = ra;
            }

            for (int i = 0; i < n; i++)
            {
                Settlement a = settlements[i];
                if (a.Tile < 0) continue;
                for (int j = i + 1; j < n; j++)
                {
                    Settlement b = settlements[j];
                    if (b.Tile < 0) continue;
                    float ab = grid.ApproxDistanceInTiles(a.Tile, b.Tile);
                    if (ab > linkMax) continue;
                    if (HostileBlocksTerritoryEdge(a.Tile, b.Tile, ab, hostiles, grid)) continue;
                    Union(i, j);
                }
            }

            // Remap roots to compact territory ids.
            var rootToId = new Dictionary<int, int>();
            int nextId = 0;
            for (int i = 0; i < n; i++)
            {
                int root = FindRoot(i);
                if (!rootToId.TryGetValue(root, out int tid))
                {
                    tid = nextId++;
                    rootToId[root] = tid;
                }
                settlements[i].GetComponent<CompViralSpread>().fortifyTerritoryId = tid;
            }

            // Per settlement nearest hostile (global list), then frontier within territory.
            var nearestDist = new float[n];
            for (int i = 0; i < n; i++)
            {
                FindNearestHostile(settlements[i], hostiles, out WorldObject nearest, out float dist);
                var c = settlements[i].GetComponent<CompViralSpread>();
                if (c.fortifyNearestHostile == null)
                    c.fortifyNearestHostile = nearest;
                nearestDist[i] = dist;
            }

            for (int i = 0; i < n; i++)
            {
                var ci = settlements[i].GetComponent<CompViralSpread>();
                WorldObject hi = ci.fortifyNearestHostile;
                if (hi == null || nearestDist[i] >= float.MaxValue * 0.5f) continue;

                float di = nearestDist[i];
                bool isFrontier = true;
                int tid = ci.fortifyTerritoryId;
                for (int j = 0; j < n; j++)
                {
                    if (i == j) continue;
                    var cj = settlements[j].GetComponent<CompViralSpread>();
                    if (cj.fortifyTerritoryId != tid) continue;
                    float dj = grid.ApproxDistanceInTiles(settlements[j].Tile, hi.Tile);
                    if (dj + eps < di)
                    {
                        isFrontier = false;
                        break;
                    }
                }
                ci.fortifyIsFrontier = isFrontier;
            }
        }

        private static bool HostileBlocksTerritoryEdge(
            int tileA,
            int tileB,
            float distAB,
            List<WorldObject> hostiles,
            WorldGrid grid)
        {
            if (hostiles == null || grid == null) return false;
            for (int i = 0; i < hostiles.Count; i++)
            {
                WorldObject h = hostiles[i];
                if (h == null || h.Destroyed || h.Tile < 0) continue;
                float da = grid.ApproxDistanceInTiles(tileA, h.Tile);
                float db = grid.ApproxDistanceInTiles(tileB, h.Tile);
                if (da + db <= distAB + 2f)
                    return true;
            }
            return false;
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

        private static bool HasFreeRing(Settlement anchor, Faction faction, out int ringMin, out int ringMax)
        {
            ringMin = 0;
            ringMax = 0;
            var seth = WorldDominationMod.settings;
            if (seth == null || anchor == null || faction == null) return false;

            GetRingBands(seth, out int r1Min, out int r1Max, out int r2Min, out int r2Max);
            if (!HasFactionFortificationInBand(anchor, faction, r1Min, r1Max))
            {
                ringMin = r1Min;
                ringMax = r1Max;
                return true;
            }
            if (!HasFactionFortificationInBand(anchor, faction, r2Min, r2Max))
            {
                ringMin = r2Min;
                ringMax = r2Max;
                return true;
            }
            return false;
        }

        public static void GetRingBands(
            WorldDominationSettings seth,
            out int r1Min, out int r1Max,
            out int r2Min, out int r2Max)
        {
            int minSelf = Mathf.Max(1, seth.fortifyMinTilesFromSelf);
            int maxSelf = Mathf.Max(minSelf, seth.fortifyMaxTilesFromSelf);
            int mid = minSelf + (maxSelf - minSelf) / 2;
            r1Min = minSelf;
            r1Max = Mathf.Max(minSelf, mid);
            r2Min = Mathf.Min(maxSelf, r1Max + 1);
            r2Max = maxSelf;
            if (r2Min > r2Max)
            {
                r2Min = r2Max;
            }
        }

        private static bool HasFactionFortificationInBand(
            Settlement anchor,
            Faction faction,
            int distMin,
            int distMax)
        {
            if (anchor == null || anchor.Tile < 0 || faction == null) return false;
            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return false;

            var blocks = WorldComponent_RoadBlocks.Get();
            if (blocks?.Records != null)
            {
                for (int i = 0; i < blocks.Records.Count; i++)
                {
                    RoadBlockRecord r = blocks.Records[i];
                    if (r == null || r.tileId < 0) continue;
                    if (!SameFactionBuilder(r.builtByFaction, r.builtBySettlement, faction)) continue;
                    int dist = (int)grid.ApproxDistanceInTiles(anchor.Tile, r.tileId);
                    if (dist >= distMin && dist <= distMax) return true;
                }
            }

            var traps = WorldComponent_SpikeTraps.Get();
            if (traps?.Records != null)
            {
                for (int i = 0; i < traps.Records.Count; i++)
                {
                    SpikeTrapRecord r = traps.Records[i];
                    if (r == null || r.tileId < 0) continue;
                    if (!SameFactionBuilder(r.builtByFaction, r.builtBySettlement, faction)) continue;
                    int dist = (int)grid.ApproxDistanceInTiles(anchor.Tile, r.tileId);
                    if (dist >= distMin && dist <= distMax) return true;
                }
            }

            return false;
        }

        private static bool SameFactionBuilder(Faction builtByFaction, Settlement builtBySettlement, Faction faction)
        {
            if (builtByFaction != null && builtByFaction == faction) return true;
            if (builtBySettlement?.Faction != null && builtBySettlement.Faction == faction) return true;
            return false;
        }

        private static bool TryRollFortifyPlacementKind(
            Settlement launcher,
            Settlement anchor,
            Settlement actor,
            WorldObject threat,
            int ringMin,
            int ringMax,
            WorldDominationSettings seth,
            out FortifyPlacementKind kind)
        {
            kind = FortifyPlacementKind.RoadBlock;
            CollectRingCandidates(anchor, actor, threat, ringMin, ringMax, seth, excludeTiles: null, out int roadCount, out int offRoadCount);

            float wBlock = Mathf.Max(0f, seth.fortifyChanceRoadBlock);
            float wTrap = Mathf.Max(0f, seth.fortifyChanceTrap);
            float wTurret = Mathf.Max(0f, seth.fortifyChanceTurret);

            bool canBlock = roadCount + offRoadCount > 0 && wBlock > 0f;
            bool canTrap = roadCount > 0 && wTrap > 0f;
            bool canTurret = offRoadCount > 0
                && wTurret > 0f
                && AtTurretUtility.CanScheduleAnother(launcher);

            float total = 0f;
            if (canBlock) total += wBlock;
            if (canTrap) total += wTrap;
            if (canTurret) total += wTurret;
            if (total <= 0f)
            {
                if (roadCount + offRoadCount > 0) { kind = FortifyPlacementKind.RoadBlock; return true; }
                if (roadCount > 0) { kind = FortifyPlacementKind.Trap; return true; }
                if (offRoadCount > 0 && AtTurretUtility.CanScheduleAnother(launcher))
                {
                    kind = FortifyPlacementKind.Turret;
                    return true;
                }
                return false;
            }

            float roll = Rand.Value * total;
            if (canBlock)
            {
                if (roll < wBlock) { kind = FortifyPlacementKind.RoadBlock; return true; }
                roll -= wBlock;
            }
            if (canTrap)
            {
                if (roll < wTrap) { kind = FortifyPlacementKind.Trap; return true; }
                roll -= wTrap;
            }
            if (canTurret)
            {
                kind = FortifyPlacementKind.Turret;
                return true;
            }

            kind = FortifyPlacementKind.RoadBlock;
            return canBlock || canTrap;
        }

        private static void CollectRingCandidates(
            Settlement anchor,
            Settlement actor,
            WorldObject threat,
            int ringMin,
            int ringMax,
            WorldDominationSettings seth,
            HashSet<int> excludeTiles,
            out int roadCount,
            out int offRoadCount)
        {
            tempRoadTiles.Clear();
            tempOffRoadTiles.Clear();
            roadCount = 0;
            offRoadCount = 0;

            WorldGrid grid = Find.WorldGrid;
            if (grid == null || anchor == null || anchor.Tile < 0 || threat == null || threat.Tile < 0) return;

            int minOther = Mathf.Max(0, seth.fortifyMinTilesFromOtherSettlement);
            float anchorToThreat = grid.ApproxDistanceInTiles(anchor.Tile, threat.Tile);

            anchor.Tile.Layer.Filler.FloodFill(anchor.Tile, (PlanetTile pt) => PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(pt), (PlanetTile pt, int dist) =>
            {
                if (dist > ringMax) return true;
                if (dist < ringMin) return false;

                int tid = pt.tileId;
                if (excludeTiles != null && excludeTiles.Contains(tid)) return false;
                if (excludeTiles != null && excludeTiles.Count > 0 && !IsAdjacentToAny(tid, excludeTiles, grid))
                    return false;
                if (!IsLegalFortifyTile(tid, actor, anchor, minOther)) return false;

                float tileToThreat = grid.ApproxDistanceInTiles(tid, threat.Tile);
                if (tileToThreat > anchorToThreat + 0.5f) return false;

                if (TileHasRoad(tid))
                    tempRoadTiles.Add(tid);
                else
                    tempOffRoadTiles.Add(tid);
                return false;
            });

            roadCount = tempRoadTiles.Count;
            offRoadCount = tempOffRoadTiles.Count;
        }

        private static bool TryPickTile(
            Settlement anchor,
            Settlement actor,
            WorldObject threat,
            int ringMin,
            int ringMax,
            WorldDominationSettings seth,
            HashSet<int> excludeTiles,
            FortifyPlacementKind kind,
            out int tile)
        {
            tile = -1;
            CollectRingCandidates(anchor, actor, threat, ringMin, ringMax, seth, excludeTiles, out _, out _);

            List<int> pool;
            switch (kind)
            {
                case FortifyPlacementKind.Trap:
                    pool = tempRoadTiles;
                    break;
                case FortifyPlacementKind.Turret:
                    pool = tempOffRoadTiles;
                    break;
                default:
                    // Road blocks: anywhere in the ring (road + off-road).
                    if (tempRoadTiles.Count == 0)
                        pool = tempOffRoadTiles;
                    else if (tempOffRoadTiles.Count == 0)
                        pool = tempRoadTiles;
                    else
                    {
                        tempRoadTiles.AddRange(tempOffRoadTiles);
                        pool = tempRoadTiles;
                    }
                    break;
            }

            if (pool == null || pool.Count == 0) return false;

            WorldGrid grid = Find.WorldGrid;
            Faction faction = actor?.Faction ?? anchor.Faction;
            CollectSameFactionFortTiles(faction, tempExistingFortTiles);
            if (kind == FortifyPlacementKind.Turret)
                CollectAllRoadBlockTiles(tempAllRoadBlocks);
            else
                tempAllRoadBlocks.Clear();

            int best = pool[0];
            float bestShield = kind == FortifyPlacementKind.Turret
                ? ScoreTurretShieldBehindRoadBlocks(best, threat, tempAllRoadBlocks, grid)
                : 0f;
            int bestAdj = CountAdjacentForts(best, tempExistingFortTiles, excludeTiles, grid);
            float bestThreatDist = grid.ApproxDistanceInTiles(best, threat.Tile);
            for (int i = 1; i < pool.Count; i++)
            {
                int cand = pool[i];
                float shield = kind == FortifyPlacementKind.Turret
                    ? ScoreTurretShieldBehindRoadBlocks(cand, threat, tempAllRoadBlocks, grid)
                    : 0f;
                int adj = CountAdjacentForts(cand, tempExistingFortTiles, excludeTiles, grid);
                float d = grid.ApproxDistanceInTiles(cand, threat.Tile);
                bool better = shield > bestShield + 0.01f
                    || (Mathf.Abs(shield - bestShield) <= 0.01f && adj > bestAdj)
                    || (Mathf.Abs(shield - bestShield) <= 0.01f && adj == bestAdj && d < bestThreatDist);
                if (better)
                {
                    bestShield = shield;
                    bestAdj = adj;
                    bestThreatDist = d;
                    best = cand;
                }
            }

            tile = best;
            return true;
        }

        private static void CollectAllRoadBlockTiles(HashSet<int> into)
        {
            into.Clear();
            var blocks = WorldComponent_RoadBlocks.Get();
            if (blocks?.Records == null) return;
            for (int i = 0; i < blocks.Records.Count; i++)
            {
                RoadBlockRecord r = blocks.Records[i];
                if (r == null || r.tileId < 0) continue;
                into.Add(r.tileId);
            }
        }

        /// <summary>
        /// Prefer off-road sites adjacent to any road block that sits between the turret and the threat
        /// (block shields the gun). Higher is better; 0 means no useful adjacent block.
        /// </summary>
        private static float ScoreTurretShieldBehindRoadBlocks(
            int turretTileId,
            WorldObject threat,
            HashSet<int> roadBlockTiles,
            WorldGrid grid)
        {
            if (grid == null || turretTileId < 0 || roadBlockTiles == null || roadBlockTiles.Count == 0)
                return 0f;

            int threatTile = threat != null && !threat.Destroyed ? threat.Tile.tileId : -1;
            Vector3 turretPos = grid.GetTileCenter(turretTileId);
            Vector3 threatPos = threatTile >= 0 ? grid.GetTileCenter(threatTile) : Vector3.zero;

            float best = 0f;
            foreach (int blockTile in roadBlockTiles)
            {
                if (blockTile < 0 || !grid.IsNeighbor(turretTileId, blockTile)) continue;

                float score = 100f; // adjacent to a road block at all
                if (TileHasRoad(blockTile))
                    score += 50f;

                if (threatTile >= 0)
                {
                    float distTurret = grid.ApproxDistanceInTiles(turretTileId, threatTile);
                    float distBlock = grid.ApproxDistanceInTiles(blockTile, threatTile);
                    // Block closer to threat than the turret = sits in front as a shield.
                    if (distBlock < distTurret - 0.01f)
                        score += 500f;

                    Vector3 blockPos = grid.GetTileCenter(blockTile);
                    Vector3 toThreat = threatPos - turretPos;
                    Vector3 toBlock = blockPos - turretPos;
                    if (toThreat.sqrMagnitude > 0.0001f && toBlock.sqrMagnitude > 0.0001f)
                    {
                        float align = Vector3.Dot(toThreat.normalized, toBlock.normalized);
                        if (align > 0.25f)
                            score += 300f * align;
                    }

                    score += 100f - distTurret;
                }

                if (score > best)
                    best = score;
            }

            return best;
        }

        private static void CollectSameFactionFortTiles(Faction faction, HashSet<int> into)
        {
            into.Clear();
            if (faction == null) return;

            var blocks = WorldComponent_RoadBlocks.Get();
            if (blocks?.Records != null)
            {
                for (int i = 0; i < blocks.Records.Count; i++)
                {
                    RoadBlockRecord r = blocks.Records[i];
                    if (r == null || r.tileId < 0) continue;
                    if (!SameFactionBuilder(r.builtByFaction, r.builtBySettlement, faction)) continue;
                    into.Add(r.tileId);
                }
            }

            var traps = WorldComponent_SpikeTraps.Get();
            if (traps?.Records != null)
            {
                for (int i = 0; i < traps.Records.Count; i++)
                {
                    SpikeTrapRecord r = traps.Records[i];
                    if (r == null || r.tileId < 0) continue;
                    if (!SameFactionBuilder(r.builtByFaction, r.builtBySettlement, faction)) continue;
                    into.Add(r.tileId);
                }
            }
        }

        private static int CountAdjacentForts(
            int tileId,
            HashSet<int> existingForts,
            HashSet<int> pickedThisLaunch,
            WorldGrid grid)
        {
            if (grid == null || tileId < 0) return 0;
            int n = 0;
            if (existingForts != null)
            {
                foreach (int other in existingForts)
                {
                    if (other >= 0 && grid.IsNeighbor(tileId, other))
                        n++;
                }
            }
            if (pickedThisLaunch != null)
            {
                foreach (int other in pickedThisLaunch)
                {
                    if (other >= 0 && grid.IsNeighbor(tileId, other))
                        n++;
                }
            }
            return n;
        }

        private static bool IsAdjacentToAny(int tileId, HashSet<int> others, WorldGrid grid)
        {
            if (others == null || others.Count == 0 || grid == null) return false;
            foreach (int other in others)
            {
                if (other >= 0 && grid.IsNeighbor(tileId, other))
                    return true;
            }
            return false;
        }

        private static bool IsLegalFortifyTile(int tileId, Settlement actor, Settlement anchor, int minOther)
        {
            if (!WorldActions_RoadBlocks.IsTileBaseEligibleForRoadBlock(tileId)) return false;
            if (WorldComponent_RoadBlocks.Get()?.HasBlockAt(tileId) == true) return false;
            if (WorldComponent_SpikeTraps.Get()?.HasTrapAt(tileId) == true) return false;
            if (AtTurretUtility.TileHasAtTurret(tileId)) return false;
            if (WorldComponent_FortifyBlacklist.BlocksNpcFortify(tileId, actor?.Faction)) return false;

            if (WorldActions_RoadBlocks.TileHasSettlementOrOutpost(tileId)) return false;

            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return false;

            int minDist = Mathf.Max(1, minOther);

            var settlements = Find.WorldObjects.Settlements;
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement s = settlements[i];
                if (s == null || s.Destroyed || s.Tile < 0) continue;
                if (s == actor || s == anchor) continue;
                if (grid.ApproxDistanceInTiles(tileId, s.Tile) < minDist) return false;
            }

            var worldObjects = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < worldObjects.Count; i++)
            {
                WorldObject wo = worldObjects[i];
                if (wo == null || wo.Destroyed || wo.Tile < 0) continue;
                if (wo is WorldObject_WD_Outpost)
                {
                    if (grid.ApproxDistanceInTiles(tileId, wo.Tile) < minDist) return false;
                }
                else if (wo is MapParent && wo.Faction != null && wo.Faction.IsPlayer && !(wo is Settlement))
                {
                    if (grid.ApproxDistanceInTiles(tileId, wo.Tile) < minDist) return false;
                }
            }

            return true;
        }

        private static bool TileHasRoad(int tileId)
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || tileId < 0 || !grid.InBounds(tileId)) return false;
            if (!(grid[tileId] is SurfaceTile surface)) return false;
            var roads = surface.Roads;
            return roads != null && roads.Count > 0;
        }

        private static void GetTierKit(SettlementTier tier, out SpikeTrapKind trapKind, out RoadBlockKind blockKind)
        {
            if (tier >= SettlementTier.T4)
            {
                trapKind = SpikeTrapKind.Caltrops;
                blockKind = RoadBlockKind.Heavy;
            }
            else if (tier >= SettlementTier.T3)
            {
                trapKind = SpikeTrapKind.Caltrops;
                blockKind = RoadBlockKind.Normal;
            }
            else
            {
                trapKind = SpikeTrapKind.Spike;
                blockKind = RoadBlockKind.Light;
            }
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

