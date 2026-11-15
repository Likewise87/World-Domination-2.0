using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Settlement-intel nimble scouts: ring path at combat attack range, then 3–5 chaotic extra
    /// waypoints; continuous spot bubble on each tile exit (plus arrival); anti-suicide bearing
    /// avoid after a kill.
    /// </summary>
    public static class WorldActions_Scout
    {
        private const float UnsetKillBearing = -999f;
        private const int ExtraWaypointsMin = 3;
        private const int ExtraWaypointsMax = 5;
        private static readonly List<int> ringScratch = new List<int>(128);

        public static bool AttemptScout(Settlement settlement, CompViralSpread comp, WorldComponent_SpreadManager manager)
        {
            if (settlement == null || comp == null || manager == null) return false;
            if (comp.tier < SettlementTier.T2) return false;
            if (comp.IsScoutOnCooldown) return false;
            return LaunchScoutsInternal(settlement, comp, manager, stampCooldown: true);
        }

        /// <summary>Dev/debug: ignore T2+ and cooldown gates, still stamps CD on success.</summary>
        public static bool ForceScoutDebug(Settlement settlement)
        {
            if (settlement == null) return false;
            var comp = settlement.GetComponent<CompViralSpread>();
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (comp == null || manager == null) return false;
            return LaunchScoutsInternal(settlement, comp, manager, stampCooldown: true);
        }

        private static bool LaunchScoutsInternal(
            Settlement settlement, CompViralSpread comp, WorldComponent_SpreadManager manager, bool stampCooldown)
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

            float combatRange = SettlementAttackRangeUtil.GetNpcSettlementAttackRangeWithZeal(settlement, seth, manager);
            float maxLos = WorldComponent_SettlementIntel.ComputeSettlementMaxLos(settlement);
            float ringLo = Mathf.Max(1f, maxLos);
            float ringHi = Mathf.Max(ringLo, combatRange);
            int ringDist = Mathf.Max(1, Mathf.RoundToInt(Rand.Range(ringLo, ringHi)));

            float avoidBearing = comp.lastScoutKillBearingDeg;
            float avoidDeg = seth.settlementIntelScoutBearingAvoidDeg;
            float firstBearing = PickInitialBearing(avoidBearing, avoidDeg);

            int launches = comp.tier >= SettlementTier.T4 ? 2 : 1;
            int launched = 0;
            for (int i = 0; i < launches; i++)
            {
                float bearing = firstBearing + (i * 180f);
                ringDist = Mathf.Max(1, Mathf.RoundToInt(Rand.Range(ringLo, ringHi)));
                if (!TryPickRingTile(settlement, ringDist, bearing, avoidBearing, avoidDeg, out int destTile))
                {
                    WDVerbose.Msg($"AttemptScout: no ring tile bearing={bearing:F0} range={ringDist} {settlement.LabelCap}");
                    continue;
                }

                float strMin = seth.settlementIntelScoutStrengthMin;
                float strMax = Mathf.Max(strMin, seth.settlementIntelScoutStrengthMax);
                float strength = Rand.Range(strMin, strMax);

                if (!LaunchOneScout(settlement, comp, manager, def, destTile, strength))
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
            int destTile,
            float strength)
        {
            var traveler = (WorldObject_Traveler)WorldObjectMaker.MakeWorldObject(def);
            traveler.Tile = settlement.Tile;
            traveler.SetFaction(settlement.Faction);
            traveler.mission = TravelerMission.Scout;
            traveler.isNimbleScout = true;
            traveler.scoutExtraWaypointsLeft = Rand.RangeInclusive(ExtraWaypointsMin, ExtraWaypointsMax);
            traveler.travelerStrength = strength;
            traveler.initialStrength = strength;
            traveler.originObject = settlement;
            AdvancedBuildingEffects.ApplyNpcLandSpeed(traveler, settlement);
            WorldComponent_SettlementIntel.Get()?.EnsureTravelerTrackId(traveler);

            Find.WorldObjects.Add(traveler);
            traveler.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(destTile, settlement));
            if (traveler.Destroyed)
                return false;

            WDVerbose.Msg(
                $"AttemptScout: launched str={strength:F0} dest={destTile} extras={traveler.scoutExtraWaypointsLeft} from {settlement.LabelCap}");
            return true;
        }

        /// <summary>
        /// After an arrival discover pulse: if extras remain, path to a chaotic next waypoint and
        /// return true (caller must not Destroy). Returns false when the mission should end.
        /// </summary>
        public static bool TryContinueScoutMission(WorldObject_Traveler traveler)
        {
            if (traveler == null || traveler.Destroyed) return false;
            if (traveler.scoutExtraWaypointsLeft <= 0) return false;
            if (!(traveler.originObject is Settlement origin) || origin.Destroyed) return false;
            if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(origin)) return false;

            if (!TryPickChaosWaypoint(traveler, origin, out int destTile))
            {
                WDVerbose.Msg($"AttemptScout: no chaos waypoint left for {origin.LabelCap}; ending scout.");
                return false;
            }

            traveler.scoutExtraWaypointsLeft--;
            traveler.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(destTile, origin));
            if (traveler.Destroyed) return false;

            WDVerbose.Msg(
                $"AttemptScout: continue dest={destTile} extrasLeft={traveler.scoutExtraWaypointsLeft} from {origin.LabelCap}");
            return true;
        }

        /// <summary>
        /// Clever chaos: twist bearing around the origin ring, sometimes hop from the scout's
        /// current tile, keep away from the kill-bearing cone when possible.
        /// </summary>
        private static bool TryPickChaosWaypoint(WorldObject_Traveler scout, Settlement origin, out int destTile)
        {
            destTile = -1;
            var seth = WorldDominationMod.settings;
            if (seth == null) return false;
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();

            float combatRange = SettlementAttackRangeUtil.GetNpcSettlementAttackRangeWithZeal(origin, seth, manager);
            float maxLos = WorldComponent_SettlementIntel.ComputeSettlementMaxLos(origin);
            float ringLo = Mathf.Max(1f, maxLos);
            float ringHi = Mathf.Max(ringLo, combatRange);
            int ringDist = Mathf.Max(1, Mathf.RoundToInt(Rand.Range(ringLo, ringHi)));
            var originComp = origin.GetComponent<CompViralSpread>();
            float avoidBearing = originComp != null ? originComp.lastScoutKillBearingDeg : UnsetKillBearing;
            float avoidDeg = seth.settlementIntelScoutBearingAvoidDeg;

            int fromId = origin.Tile.tileId;
            int curId = scout.Tile.tileId;
            float curBearing = BearingDeg(fromId, curId);

            for (int attempt = 0; attempt < 10; attempt++)
            {
                float twist = Rand.Range(70f, 170f) * (Rand.Bool ? 1f : -1f);
                float bearing = Mathf.Repeat(curBearing + twist, 360f);

                // Mix ring revisits (coverage around the settlement) with local hops (fill gaps).
                if (Rand.Chance(0.4f))
                {
                    int hop = Rand.RangeInclusive(Mathf.Max(2, ringDist / 3), Mathf.Max(3, ringDist));
                    if (TryPickRingTileAround(scout.Tile, hop, bearing, avoidBearing, avoidDeg, out destTile)
                        && destTile != curId)
                        return true;
                }
                else
                {
                    ringDist = Mathf.Max(1, Mathf.RoundToInt(Rand.Range(ringLo, ringHi)));
                    int dist = Mathf.Max(1, ringDist + Rand.RangeInclusive(-2, 2));
                    if (TryPickRingTileAround(origin.Tile, dist, bearing, avoidBearing, avoidDeg, out destTile)
                        && destTile != curId)
                        return true;
                }
            }

            // Last resort: any other ring tile around the origin.
            CollectRingTiles(origin.Tile, ringDist, ringScratch);
            for (int i = 0; i < ringScratch.Count; i++)
            {
                int j = Rand.Range(i, ringScratch.Count);
                int tmp = ringScratch[i];
                ringScratch[i] = ringScratch[j];
                ringScratch[j] = tmp;

                int tile = ringScratch[i];
                if (tile == curId) continue;
                if (!PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(new PlanetTile(tile, origin.Tile.Layer)))
                    continue;
                destTile = tile;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Discover settlements/outposts/ATs/advanced buildings within scout MaxLOS x target ring of
        /// <paramref name="tile"/> for <paramref name="faction"/>.
        /// Called on every scout tile exit and again on arrival.
        /// </summary>
        public static void DiscoverSitesInScoutBubble(Faction faction, int tile)
        {
            if (faction == null || tile < 0) return;
            var intel = WorldComponent_SettlementIntel.Get();
            if (intel == null || !WorldComponent_SettlementIntel.IsTracking()) return;
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (manager == null || Find.WorldObjects == null) return;

            float scoutMax = WorldDominationMod.settings?.intelScoutMaxLos
                ?? WorldDominationSettings.DefIntelScoutMaxLos;

            List<Settlement> settlements = Find.WorldObjects.Settlements;
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement s = settlements[i];
                if (s == null || s.Destroyed) continue;
                if (intel.IsDiscovered(faction, s)) continue;
                float r = WorldComponent_SettlementIntel.GetScoutSpotRadius(
                    WorldComponent_SettlementIntel.GetSightTier(s));
                float d = WorldActions_Utils.GetDistance(tile, s.Tile.tileId, manager);
                if (d <= 1f || d <= r)
                    intel.Discover(faction, s);
            }

            IReadOnlyList<WorldObject_WD_Outpost> outposts = WdPlayerOutpostCache.PlayerOutposts;
            for (int i = 0; i < outposts.Count; i++)
            {
                WorldObject_WD_Outpost o = outposts[i];
                if (o == null || o.Destroyed) continue;
                if (intel.IsDiscovered(faction, o)) continue;
                float r = WorldComponent_SettlementIntel.GetScoutSpotRadius(
                    WorldComponent_SettlementIntel.GetSightTier(o));
                float d = WorldActions_Utils.GetDistance(tile, o.Tile.tileId, manager);
                if (d <= 1f || d <= r)
                    intel.Discover(faction, o);
            }

            float t1R = scoutMax * WorldComponent_SettlementIntel.GetTargetRingFrac(SettlementTier.T1);
            IReadOnlyList<WorldObject_AT_Turret> ats = WorldObject_AT_Turret.LiveTurrets;
            for (int i = 0; i < ats.Count; i++)
            {
                WorldObject_AT_Turret at = ats[i];
                if (at == null || at.Destroyed) continue;
                if (intel.IsDiscovered(faction, at)) continue;
                float d = WorldActions_Utils.GetDistance(tile, at.Tile.tileId, manager);
                if (d <= 1f || d <= t1R)
                    intel.Discover(faction, at);
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
                    float d = WorldActions_Utils.GetDistance(tile, b.Tile.tileId, manager);
                    if (d <= 1f || d <= t1R)
                        intel.Discover(faction, b);
                }
            }
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

        private static bool TryPickRingTile(
            Settlement settlement,
            int ringDist,
            float preferredBearing,
            float avoidBearing,
            float avoidDeg,
            out int chosenTile)
        {
            return TryPickRingTileAround(settlement.Tile, ringDist, preferredBearing, avoidBearing, avoidDeg, out chosenTile);
        }

        private static bool TryPickRingTileAround(
            PlanetTile center,
            int ringDist,
            float preferredBearing,
            float avoidBearing,
            float avoidDeg,
            out int chosenTile)
        {
            chosenTile = -1;
            if (!center.Valid) return false;
            CollectRingTiles(center, ringDist, ringScratch);
            if (ringScratch.Count == 0 && ringDist > 1)
                CollectRingTiles(center, ringDist - 1, ringScratch);
            if (ringScratch.Count == 0) return false;

            int fromId = center.tileId;
            int best = -1;
            float bestScore = float.MinValue;
            int samples = Mathf.Min(ringScratch.Count, 24);
            for (int i = 0; i < samples; i++)
            {
                int j = Rand.Range(i, ringScratch.Count);
                int tmp = ringScratch[i];
                ringScratch[i] = ringScratch[j];
                ringScratch[j] = tmp;

                int tile = ringScratch[i];
                if (!PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(new PlanetTile(tile, center.Layer)))
                    continue;

                float bearing = BearingDeg(fromId, tile);
                if (HasKillBearing(avoidBearing) && Mathf.Abs(Mathf.DeltaAngle(bearing, avoidBearing)) < avoidDeg)
                    continue;

                float score = -Mathf.Abs(Mathf.DeltaAngle(bearing, preferredBearing));
                if (score > bestScore)
                {
                    bestScore = score;
                    best = tile;
                }
            }

            if (best < 0)
            {
                // Fall back: any ring tile, even if near kill bearing.
                for (int i = 0; i < ringScratch.Count; i++)
                {
                    int tile = ringScratch[i];
                    if (PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(new PlanetTile(tile, center.Layer)))
                    {
                        chosenTile = tile;
                        return true;
                    }
                }
                return false;
            }

            chosenTile = best;
            return true;
        }

        private static void CollectRingTiles(PlanetTile center, int dist, List<int> into)
        {
            into.Clear();
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || !center.Valid || dist < 1) return;
            PlanetLayer layer = center.Layer ?? PlanetSurfaceWorldActions.WdSurfaceLayer ?? grid.Surface;
            if (layer == null) return;

            layer.Filler.FloodFill(
                center,
                pt => PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(pt),
                (PlanetTile pt, int d) =>
                {
                    if (d > dist) return true;
                    if (d == dist)
                        into.Add(pt.tileId);
                    return false;
                });
        }
    }
}
