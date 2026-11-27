using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Settlement-intel nimble scouts: bear-and-snap hops (0.5–2× attack range from current),
    /// faction dest memory, alive caps, continuous spot bubble; anti-suicide bearing avoid after a kill.
    /// </summary>
    public static class WorldActions_Scout
    {
        private const float UnsetKillBearing = -999f;
        private const int ExtraWaypointsMin = 3;
        private const int ExtraWaypointsMax = 5;
        private const float HopDistMinMult = 0.5f;
        private const float HopDistMaxMult = 2f;
        private const int PickRetries = 8;
        private const int PathRepicks = 2;
        private static readonly List<PlanetTile> neighborScratch = new List<PlanetTile>(8);

        public static int MaxAliveScouts(SettlementTier tier)
        {
            if (tier >= SettlementTier.T3) return 2;
            return 1;
        }

        public static int CountAliveScouts(Settlement settlement)
        {
            if (settlement == null) return 0;
            int n = 0;
            IReadOnlyList<WorldObject_Traveler> live = WorldObject_Traveler.LiveTravelers;
            for (int i = 0; i < live.Count; i++)
            {
                WorldObject_Traveler t = live[i];
                if (t == null || t.Destroyed) continue;
                if (t.mission != TravelerMission.Scout) continue;
                if (t.originObject != settlement) continue;
                n++;
            }
            return n;
        }

        public static int ScoutSlotsRemaining(Settlement settlement, CompViralSpread comp)
        {
            if (settlement == null || comp == null) return 0;
            return Mathf.Max(0, MaxAliveScouts(comp.tier) - CountAliveScouts(settlement));
        }

        public static bool AttemptScout(Settlement settlement, CompViralSpread comp, WorldComponent_SpreadManager manager)
        {
            if (settlement == null || comp == null || manager == null) return false;
            if (comp.tier < SettlementTier.T1) return false;
            if (comp.IsScoutOnCooldown) return false;
            if (ScoutSlotsRemaining(settlement, comp) <= 0) return false;
            return LaunchScoutsInternal(settlement, comp, manager, stampCooldown: true, ignoreAliveCap: false);
        }

        /// <summary>Dev/debug: ignore tier, cooldown, and alive caps; still stamps CD on success.</summary>
        public static bool ForceScoutDebug(Settlement settlement)
        {
            if (settlement == null) return false;
            var comp = settlement.GetComponent<CompViralSpread>();
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (comp == null || manager == null) return false;
            return LaunchScoutsInternal(settlement, comp, manager, stampCooldown: true, ignoreAliveCap: true);
        }

        private static bool LaunchScoutsInternal(
            Settlement settlement,
            CompViralSpread comp,
            WorldComponent_SpreadManager manager,
            bool stampCooldown,
            bool ignoreAliveCap)
        {
            if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(settlement)) return false;

            var seth = WorldDominationMod.settings;
            if (seth == null) return false;

            var def = DefDatabase<WorldObjectDef>.GetNamedSilentFail("TSA_WD_Traveler_Scout");
            if (def == null)
            {
                WDVerbose.Msg($"AttemptScout: missing TSA_WD_Traveler_Scout def for {settlement.LabelCap}");
                return false;
            }

            float attackRange = SettlementAttackRangeUtil.GetNpcSettlementAttackRangeWithZeal(settlement, seth, manager);
            float avoidBearing = comp.lastScoutKillBearingDeg;
            float avoidDeg = seth.settlementIntelScoutBearingAvoidDeg;
            float firstBearing = PickInitialBearing(avoidBearing, avoidDeg);

            int slots = ignoreAliveCap ? 99 : ScoutSlotsRemaining(settlement, comp);
            if (slots <= 0) return false;

            int batch = comp.tier >= SettlementTier.T4 ? 2 : 1;
            int launches = Mathf.Min(batch, slots);
            int launched = 0;
            for (int i = 0; i < launches; i++)
            {
                float bearing = firstBearing + (i * 180f);
                float strMin = seth.settlementIntelScoutStrengthMin;
                float strMax = Mathf.Max(strMin, seth.settlementIntelScoutStrengthMax);
                float strength = Rand.Range(strMin, strMax);

                if (!LaunchOneScout(settlement, comp, manager, def, strength, attackRange, bearing, avoidBearing, avoidDeg))
                    continue;
                launched++;
            }

            if (launched <= 0) return false;

            if (stampCooldown)
                comp.StampScoutCooldown();
            manager.AddLog(new SpreadLogEntry(
                "TSA_WD_Log_ScoutLaunched".Translate(settlement.LabelCap, launched),
                settlement));
            return true;
        }

        private static bool LaunchOneScout(
            Settlement settlement,
            CompViralSpread comp,
            WorldComponent_SpreadManager manager,
            WorldObjectDef def,
            float strength,
            float attackRange,
            float preferredBearing,
            float avoidBearing,
            float avoidDeg)
        {
            for (int repick = 0; repick < PathRepicks; repick++)
            {
                if (!TryPickScoutDestination(
                        settlement.Tile.tileId,
                        settlement,
                        settlement.Faction,
                        attackRange,
                        preferredBearing,
                        avoidBearing,
                        avoidDeg,
                        out int destTile))
                {
                    WDVerbose.Msg($"AttemptScout: no land hop dest for {settlement.LabelCap}");
                    return false;
                }

                var traveler = (WorldObject_Traveler)WorldObjectMaker.MakeWorldObject(def);
                traveler.Tile = settlement.Tile;
                traveler.SetFaction(settlement.Faction);
                traveler.mission = TravelerMission.Scout;
                traveler.isNimbleScout = true;
                traveler.scoutExtraWaypointsLeft = Rand.RangeInclusive(ExtraWaypointsMin, ExtraWaypointsMax);
                traveler.travelerStrength = strength;
                traveler.initialStrength = strength;
                traveler.originObject = settlement;
                traveler.ticksPerMove = WorldActions_Traveler.GetScoutTicksPerMove();
                AdvancedBuildingEffects.ApplyNpcLandSpeed(traveler, settlement);
                WorldComponent_SettlementIntel.Get()?.EnsureTravelerTrackId(traveler);

                Find.WorldObjects.Add(traveler);
                traveler.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(destTile, settlement));
                if (traveler.Destroyed)
                {
                    WDVerbose.Msg($"AttemptScout: path failed dest={destTile} from {settlement.LabelCap}; re-pick.");
                    continue;
                }

                WorldComponent_SettlementIntel.Get()?.RecordScoutTileSeen(settlement.Faction, destTile);
                WDVerbose.Msg(
                    $"AttemptScout: launched str={strength:F0} dest={destTile} extras={traveler.scoutExtraWaypointsLeft} from {settlement.LabelCap}");
                return true;
            }
            return false;
        }

        /// <summary>
        /// After an arrival discover pulse: if extras remain, path to next hop and return true.
        /// </summary>
        public static bool TryContinueScoutMission(WorldObject_Traveler traveler)
        {
            if (traveler == null || traveler.Destroyed) return false;
            // Player queued waypoints take priority over NPC chaos hops.
            if (traveler.Faction?.IsPlayer == true
                && traveler.scoutWaypointQueue != null
                && traveler.scoutWaypointQueue.Count > 0)
                return WorldActions_PlayerScout.TryContinuePlayerScoutQueue(traveler);

            if (traveler.scoutExtraWaypointsLeft <= 0) return false;
            if (!(traveler.originObject is Settlement origin) || origin.Destroyed) return false;
            if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(origin)) return false;

            var seth = WorldDominationMod.settings;
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (seth == null) return false;

            float attackRange = SettlementAttackRangeUtil.GetNpcSettlementAttackRangeWithZeal(origin, seth, manager);
            var originComp = origin.GetComponent<CompViralSpread>();
            float avoidBearing = originComp != null ? originComp.lastScoutKillBearingDeg : UnsetKillBearing;
            float avoidDeg = seth.settlementIntelScoutBearingAvoidDeg;
            float twist = Rand.Range(70f, 170f) * (Rand.Bool ? 1f : -1f);
            float bearing = Mathf.Repeat(BearingDeg(origin.Tile.tileId, traveler.Tile.tileId) + twist, 360f);

            for (int repick = 0; repick < PathRepicks; repick++)
            {
                if (!TryPickScoutDestination(
                        traveler.Tile.tileId,
                        origin,
                        traveler.Faction,
                        attackRange,
                        bearing,
                        avoidBearing,
                        avoidDeg,
                        out int destTile))
                {
                    WDVerbose.Msg($"AttemptScout: no chaos hop left for {origin.LabelCap}; ending scout.");
                    return false;
                }

                traveler.scoutExtraWaypointsLeft--;
                traveler.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(destTile, origin));
                if (traveler.Destroyed)
                {
                    traveler.scoutExtraWaypointsLeft++;
                    continue;
                }

                WorldComponent_SettlementIntel.Get()?.RecordScoutTileSeen(traveler.Faction, destTile);
                WDVerbose.Msg(
                    $"AttemptScout: continue dest={destTile} extrasLeft={traveler.scoutExtraWaypointsLeft} from {origin.LabelCap}");
                return true;
            }
            return false;
        }

        /// <summary>
        /// Bear-and-snap: sample shorter-weighted dist in [0.5R, 2R], walk neighbors toward bearing,
        /// validate land / band / MaxLOS / recent stamps. Best-of retries by stamp age.
        /// </summary>
        private static bool TryPickScoutDestination(
            int fromTileId,
            Settlement origin,
            Faction faction,
            float attackRange,
            float preferredBearing,
            float avoidBearing,
            float avoidDeg,
            out int chosenTile)
        {
            chosenTile = -1;
            if (fromTileId < 0 || origin == null || attackRange <= 0f) return false;

            float minDist = Mathf.Max(1f, attackRange * HopDistMinMult);
            float maxDist = Mathf.Max(minDist, attackRange * HopDistMaxMult);
            float originLos = WorldComponent_SettlementIntel.ComputeSettlementMaxLos(origin);

            int bestTile = -1;
            int bestAge = -1;

            for (int attempt = 0; attempt < PickRetries; attempt++)
            {
                float bearing = preferredBearing;
                if (attempt > 0)
                    bearing = Mathf.Repeat(preferredBearing + Rand.Range(-90f, 90f), 360f);
                if (HasKillBearing(avoidBearing) && Mathf.Abs(Mathf.DeltaAngle(bearing, avoidBearing)) < avoidDeg)
                    bearing = Mathf.Repeat(avoidBearing + 180f + Rand.Range(-40f, 40f), 360f);

                // Shorter-weighted: u^2 biases toward minDist.
                float u = Rand.Value;
                int steps = Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(minDist, maxDist, u * u)));

                int snapped = WalkBearingSteps(fromTileId, bearing, steps, faction);
                if (snapped < 0 || snapped == fromTileId) continue;
                if (!IsValidScoutLandTile(snapped, origin.Tile.Layer)) continue;
                // Do not hop onto a hostile spike trap (scouts die to trap damage easily).
                if (WorldActions_SpikeTraps.IsHostileTrapTo(snapped, faction))
                    continue;

                WorldGrid grid = Find.WorldGrid;
                if (grid == null) continue;
                float approx = grid.ApproxDistanceInTiles(fromTileId, snapped);
                if (approx < minDist * 0.75f || approx > maxDist * 1.25f) continue;

                if (IsTileInSameFactionLos(snapped, origin, originLos, fromTileId, attackRange, maxDist))
                    continue;

                // Must be pathable from the hop start (rejects impassable and landlocked passable pockets).
                if (!CanScoutPathLand(fromTileId, snapped, origin.Tile.Layer, faction))
                    continue;

                var intel = WorldComponent_SettlementIntel.Get();
                if (intel != null && faction != null && intel.IsNearRecentScoutStamp(faction, snapped))
                    continue;

                int age = intel != null && faction != null
                    ? intel.GetScoutTileAgeTicks(faction, snapped)
                    : int.MaxValue;
                if (age > bestAge)
                {
                    bestAge = age;
                    bestTile = snapped;
                }
            }

            if (bestTile < 0) return false;
            chosenTile = bestTile;
            return true;
        }

        private static bool AllowScoutWaterTravel()
        {
            var seth = WorldDominationMod.settings;
            return seth?.allowCaravansTravelOverWater ?? WorldDominationSettings.DefAllowCaravansTravelOverWater;
        }

        private static bool IsValidScoutLandTile(int tileId, PlanetLayer layer)
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || tileId < 0 || tileId >= grid.TilesCount) return false;
            PlanetLayer L = layer ?? PlanetSurfaceWorldActions.WdSurfaceLayer ?? grid.Surface;
            var pt = new PlanetTile(tileId, L);
            if (!PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(pt)) return false;
            // Water is a valid scout dest when traveler water routing is on (oceans are Impassable to vanilla).
            if (grid[tileId].WaterCovered)
                return AllowScoutWaterTravel();
            if (Find.World.Impassable(pt)) return false;
            return true;
        }

        /// <summary>True when a scout traveler can reach <paramref name="toTileId"/> (vanilla land path and/or water A*).</summary>
        private static bool CanScoutPathLand(int fromTileId, int toTileId, PlanetLayer layer, Faction faction = null)
        {
            if (fromTileId < 0 || toTileId < 0 || fromTileId == toTileId) return false;
            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return false;
            PlanetLayer L = layer ?? PlanetSurfaceWorldActions.WdSurfaceLayer ?? grid.Surface;
            if (!IsValidScoutLandTile(toTileId, L)) return false;
            var from = new PlanetTile(fromTileId, L);
            var to = new PlanetTile(toTileId, L);
            WdScoutTrapAvoidPathContext.Scope trapScope = faction != null
                ? WdScoutTrapAvoidPathContext.Activate(faction)
                : null;
            try
            {
                using WorldPath path = L.Pather.FindPath(from, to, null);
                if (path != null && path.Found) return true;
            }
            finally
            {
                trapScope?.Dispose();
            }
            if (!AllowScoutWaterTravel()) return false;
            return TravelerWaterPathing.TryBuildFallbackPath(from, to, out List<PlanetTile> waterPath)
                && waterPath != null
                && waterPath.Count > 1;
        }

        private static bool IsTileInSameFactionLos(
            int tileId,
            Settlement origin,
            float originLos,
            int fromTileId,
            float attackRange,
            float maxHopDist)
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || origin?.Faction == null) return false;

            if (grid.ApproxDistanceInTiles(origin.Tile.tileId, tileId) <= originLos)
                return true;

            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            DailyWorldSnapshot snap = manager?.CurrentDailySnapshot;
            List<Settlement> list = null;
            if (snap?.SettlementsByFaction != null)
                snap.SettlementsByFaction.TryGetValue(origin.Faction, out list);
            if (list == null) return false;

            float nearBudget = maxHopDist + originLos;
            for (int i = 0; i < list.Count; i++)
            {
                Settlement s = list[i];
                if (s == null || s.Destroyed || s == origin) continue;
                if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(s)) continue;
                float toScout = grid.ApproxDistanceInTiles(fromTileId, s.Tile.tileId);
                float sLos = WorldComponent_SettlementIntel.ComputeSettlementMaxLos(s);
                if (toScout > attackRange * HopDistMaxMult + sLos) continue;
                if (toScout > nearBudget + sLos) continue;
                if (grid.ApproxDistanceInTiles(s.Tile.tileId, tileId) <= sLos)
                    return true;
            }
            return false;
        }

        private static int WalkBearingSteps(int fromTileId, float bearingDeg, int steps, Faction faction = null)
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || fromTileId < 0 || steps < 1) return -1;

            float rad = bearingDeg * Mathf.Deg2Rad;
            Vector3 want = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad));
            int cur = fromTileId;
            PlanetLayer layer = grid[fromTileId].Layer ?? PlanetSurfaceWorldActions.WdSurfaceLayer ?? grid.Surface;

            for (int step = 0; step < steps; step++)
            {
                neighborScratch.Clear();
                grid.GetTileNeighbors(new PlanetTile(cur, layer), neighborScratch);
                if (neighborScratch.Count == 0) break;

                Vector3 curCenter = grid.GetTileCenter(cur);
                int best = -1;
                float bestDot = float.MinValue;
                for (int i = 0; i < neighborScratch.Count; i++)
                {
                    int n = neighborScratch[i].tileId;
                    if (n < 0) continue;
                    // Skip mountains / invalid surface; water ok when traveler water routing is on.
                    if (!IsValidScoutLandTile(n, layer)) continue;
                    if (WorldActions_SpikeTraps.IsHostileTrapTo(n, faction)) continue;
                    Vector3 delta = grid.GetTileCenter(n) - curCenter;
                    delta.y = 0f;
                    if (delta.sqrMagnitude < 0.0001f) continue;
                    float dot = Vector3.Dot(delta.normalized, want);
                    if (dot > bestDot)
                    {
                        bestDot = dot;
                        best = n;
                    }
                }
                if (best < 0) break;
                cur = best;
            }
            return cur;
        }

        /// <summary>
        /// Discover settlements/outposts/ATs/advanced buildings within scout MaxLOS x target ring of
        /// <paramref name="tile"/> for <paramref name="faction"/>.
        /// Called on every scout tile exit and again on arrival.
        /// </summary>
        public static void DiscoverSitesInScoutBubble(Faction faction, int tile, WorldObject observer = null)
        {
            if (faction == null || tile < 0) return;
            var intel = WorldComponent_SettlementIntel.Get();
            if (intel == null || !WorldComponent_SettlementIntel.IsTracking()) return;
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || Find.WorldObjects == null) return;

            float scoutMax = WorldDominationMod.settings?.intelScoutMaxLos
                ?? WorldDominationSettings.DefIntelScoutMaxLos;

            // Known-first, then one rounded ApproxDistance per pair (no saved distanceCache writes: the
            // scout tile changes every hop). Outer cull at scout Awareness before the per-target radius.
            List<Settlement> settlements = Find.WorldObjects.Settlements;
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement s = settlements[i];
                if (s == null || s.Destroyed) continue;
                if (intel.IsDiscovered(faction, s)) continue;
                int d = WorldComponent_SettlementIntel.TileDistance(grid, tile, s.Tile.tileId);
                if (d > 1 && d > scoutMax) continue;
                if (d <= 1 || d <= WorldComponent_SettlementIntel.GetScoutSpotRadius(s))
                    intel.Discover(faction, s, observer);
            }

            IReadOnlyList<WorldObject_WD_Outpost> outposts = WdPlayerOutpostCache.PlayerOutposts;
            for (int i = 0; i < outposts.Count; i++)
            {
                WorldObject_WD_Outpost o = outposts[i];
                if (o == null || o.Destroyed) continue;
                if (intel.IsDiscovered(faction, o)) continue;
                int d = WorldComponent_SettlementIntel.TileDistance(grid, tile, o.Tile.tileId);
                if (d > 1 && d > scoutMax) continue;
                if (d <= 1 || d <= WorldComponent_SettlementIntel.GetScoutSpotRadius(o))
                    intel.Discover(faction, o, observer);
            }

            // AT / advanced are small targets: scout Line of sight only.
            float scoutLos = WorldComponent_SettlementIntel.GetScoutSpotRadius(SettlementTier.T1);
            IReadOnlyList<WorldObject_AT_Turret> ats = WorldObject_AT_Turret.LiveTurrets;
            for (int i = 0; i < ats.Count; i++)
            {
                WorldObject_AT_Turret at = ats[i];
                if (at == null || at.Destroyed) continue;
                if (intel.IsDiscovered(faction, at)) continue;
                int d = WorldComponent_SettlementIntel.TileDistance(grid, tile, at.Tile.tileId);
                if (d <= 1 || d <= scoutLos)
                    intel.Discover(faction, at, observer);
            }

            IReadOnlyList<WorldObject_WD_PlayerWatchTower> towers = WorldObject_WD_PlayerWatchTower.LiveTowers;
            for (int i = 0; i < towers.Count; i++)
            {
                WorldObject_WD_PlayerWatchTower wt = towers[i];
                if (wt == null || wt.Destroyed) continue;
                if (intel.IsDiscovered(faction, wt)) continue;
                int d = WorldComponent_SettlementIntel.TileDistance(grid, tile, wt.Tile.tileId);
                if (d <= 1 || d <= scoutLos)
                    intel.Discover(faction, wt, observer);
            }

            var registry = WorldComponent_AdvancedBuildings.Get();
            if (registry != null)
            {
                IReadOnlyList<WorldObject_WD_AdvancedBuilding> buildings = registry.All;
                for (int i = 0; i < buildings.Count; i++)
                {
                    var b = buildings[i];
                    if (b == null || b.Destroyed) continue;
                    if (intel.IsDiscovered(faction, b)) continue;
                    int d = WorldComponent_SettlementIntel.TileDistance(grid, tile, b.Tile.tileId);
                    if (d <= 1 || d <= scoutLos)
                        intel.Discover(faction, b, observer);
                }
            }

            IReadOnlyList<WorldObject_WdSettlementRuin> ruins = WorldObject_WdSettlementRuin.LiveRuins;
            for (int i = 0; i < ruins.Count; i++)
            {
                WorldObject_WdSettlementRuin ruin = ruins[i];
                if (ruin == null || ruin.Destroyed) continue;
                if (intel.IsDiscovered(faction, ruin)) continue;
                int d = WorldComponent_SettlementIntel.TileDistance(grid, tile, ruin.Tile.tileId);
                if (d <= 1 || d <= scoutLos)
                    intel.Discover(faction, ruin, observer);
            }

            intel.TileFow.RevealTilesInRadius(faction, tile, scoutMax);
        }

        /// <summary>True when this settlement has no discovered hostiles inside its combat attack range.</summary>
        public static bool IsScoutClearOfKnownHostiles(Settlement settlement, CompViralSpread _, WorldComponent_SpreadManager manager)
        {
            if (settlement?.Faction == null) return true;
            var intel = WorldComponent_SettlementIntel.Get();
            if (intel == null || !WorldComponent_SettlementIntel.IsTracking()) return true;

            var seth = WorldDominationMod.settings;
            float range = SettlementAttackRangeUtil.GetNpcSettlementAttackRangeWithZeal(settlement, seth, manager);
            if (Find.WorldObjects == null) return true;
            List<Settlement> settlements = Find.WorldObjects.Settlements;
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement other = settlements[i];
                if (other == null || other.Destroyed || other.Faction == null) continue;
                if (other.Faction == settlement.Faction) continue;
                if (!WorldActions_Utils.SafeHostileTo(other.Faction, settlement.Faction)) continue;
                bool known = WorldComponent_SettlementIntel.IsActive(manager)
                    ? intel.Knows(settlement.Faction, other)
                    : intel.IsDiscovered(settlement.Faction, other);
                if (!known) continue;
                float d = WorldActions_Utils.GetDistance(settlement.Tile, other.Tile, manager);
                if (d <= range) return false;
            }
            return true;
        }

        public static void RecordScoutKillBearing(WorldObject_Traveler scout)
        {
            if (scout == null || !scout.isNimbleScout) return;
            if (!(scout.originObject is Settlement origin) || origin.Destroyed) return;
            var originComp = origin.GetComponent<CompViralSpread>();
            if (originComp == null) return;
            originComp.lastScoutKillBearingDeg = BearingDeg(origin.Tile.tileId, scout.Tile.tileId);
        }

        public static float BearingDeg(int fromTileId, int toTileId)
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || fromTileId < 0 || toTileId < 0) return 0f;
            Vector3 from = grid.GetTileCenter(fromTileId);
            Vector3 to = grid.GetTileCenter(toTileId);
            Vector3 d = to - from;
            return Mathf.Atan2(d.z, d.x) * Mathf.Rad2Deg;
        }

        private static float PickInitialBearing(float avoidBearing, float avoidDeg)
        {
            if (!HasKillBearing(avoidBearing))
                return Rand.Range(0f, 360f);

            for (int attempt = 0; attempt < 12; attempt++)
            {
                float b = Rand.Range(0f, 360f);
                if (Mathf.Abs(Mathf.DeltaAngle(b, avoidBearing)) >= avoidDeg)
                    return b;
            }
            return Mathf.Repeat(avoidBearing + 180f, 360f);
        }

        private static bool HasKillBearing(float bearing) =>
            !float.IsNaN(bearing) && bearing > UnsetKillBearing + 1f;
    }
}
