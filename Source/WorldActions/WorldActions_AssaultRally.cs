using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Shared pack → rally → absorb → Raid bus for Desperation (and legacy in-flight Invasion assemblies).
    /// New Invasions use coordinated multi-raid without packing (see <see cref="WorldActions_Invasion"/>).
    /// Mission remains <see cref="TravelerMission.DesperationRally"/> for save compat; flavor via <c>isDesperationRaid</c>.
    /// </summary>
    public static class WorldActions_AssaultRally
    {
        public const float RallyPathCapDays = 1.5f;
        public const float RallyWaitDays = 1.5f;

        private static readonly List<PlanetTile> tmpNeighbors = new List<PlanetTile>();
        private static readonly List<Settlement> tmpPartitionRemaining = new List<Settlement>();
        private static readonly List<Settlement> tmpAssemblyScratch = new List<Settlement>();

        public sealed class AssaultRallyOptions
        {
            public bool isDesperationRaid;
            public int aggressorFactionLoadId = -1;
            public string packRallyLogKey;
            public string packSoloRaidLogKey;
            public string launchRaidLogKey;
        }

        public static int NextGroupId()
        {
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (manager == null)
                return 1;
            if (manager.assaultRallyNextGroupId < 1)
                manager.assaultRallyNextGroupId = 1;
            return manager.assaultRallyNextGroupId++;
        }

        /// <summary>After load: ensure next id is above any in-flight traveler group ids.</summary>
        public static void EnsureNextGroupIdAfterLoad(WorldComponent_SpreadManager manager)
        {
            if (manager == null) return;
            int max = Mathf.Max(0, manager.assaultRallyNextGroupId - 1);
            var objs = Find.WorldObjects?.AllWorldObjects;
            if (objs != null)
            {
                for (int i = 0; i < objs.Count; i++)
                {
                    if (objs[i] is WorldObject_Traveler t && t.desperationGroupId > max)
                        max = t.desperationGroupId;
                }
            }
            manager.assaultRallyNextGroupId = max + 1;
        }

        public static bool CanReachRallyInCap(int fromTile, int rallyTile)
        {
            if (fromTile < 0 || rallyTile < 0) return false;
            float dist = Find.WorldGrid.ApproxDistanceInTiles(fromTile, rallyTile);
            float ticks = dist * WorldObject_Traveler.DefaultTicksPerMove;
            return ticks <= RallyPathCapDays * 60000f;
        }

        public static int ComputeCentroidTile(IList<Settlement> cluster)
        {
            if (cluster == null || cluster.Count == 0) return -1;
            Settlement first = null;
            for (int i = 0; i < cluster.Count; i++)
            {
                if (cluster[i] != null && !cluster[i].Destroyed)
                {
                    first = cluster[i];
                    break;
                }
            }
            if (first == null) return -1;

            int best = first.Tile.tileId;
            float bestSum = float.MaxValue;
            for (int i = 0; i < cluster.Count; i++)
            {
                Settlement candS = cluster[i];
                if (candS == null || candS.Destroyed) continue;
                int cand = candS.Tile.tileId;
                float sum = 0f;
                for (int j = 0; j < cluster.Count; j++)
                {
                    Settlement other = cluster[j];
                    if (other == null || other.Destroyed) continue;
                    sum += Find.WorldGrid.ApproxDistanceInTiles(cand, other.Tile.tileId);
                }
                if (sum < bestSum)
                {
                    bestSum = sum;
                    best = cand;
                }
            }
            return best;
        }

        /// <summary>
        /// Free tile near the cluster centroid — never a live cluster settlement tile.
        /// </summary>
        public static int PickRallyTile(IList<Settlement> cluster, int centroid)
        {
            int seed = centroid >= 0
                ? centroid
                : (cluster != null && cluster.Count > 0 && cluster[0] != null ? cluster[0].Tile.tileId : -1);
            if (seed < 0) return -1;

            var occupiedCluster = new HashSet<int>();
            if (cluster != null)
            {
                for (int i = 0; i < cluster.Count; i++)
                {
                    Settlement s = cluster[i];
                    if (s != null && !s.Destroyed)
                        occupiedCluster.Add(s.Tile.tileId);
                }
            }

            int found = FindNearbyFreeRallyTile(seed, occupiedCluster);
            if (found >= 0) return found;

            if (!occupiedCluster.Contains(seed)
                && TileFinder.IsValidTileForNewSettlement(seed)
                && !Find.WorldObjects.AnySettlementAt(seed))
                return seed;

            return seed;
        }

        private static int FindNearbyFreeRallyTile(int seedTile, HashSet<int> occupiedCluster)
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || seedTile < 0 || !grid.InBounds(seedTile)) return -1;

            var visited = new HashSet<int>();
            var queue = new Queue<(int tile, int dist)>();
            queue.Enqueue((seedTile, 0));
            visited.Add(seedTile);
            const int maxDist = 12;
            const int maxAttempts = 256;
            int attempts = 0;

            while (queue.Count > 0 && attempts < maxAttempts)
            {
                (int tile, int dist) = queue.Dequeue();
                attempts++;

                bool blockedHome = occupiedCluster != null && occupiedCluster.Contains(tile);
                if (!blockedHome
                    && !Find.WorldObjects.AnySettlementAt(tile)
                    && TileFinder.IsValidTileForNewSettlement(tile))
                    return tile;

                if (dist >= maxDist) continue;
                tmpNeighbors.Clear();
                grid.GetTileNeighbors(tile, tmpNeighbors);
                for (int i = 0; i < tmpNeighbors.Count; i++)
                {
                    int n = tmpNeighbors[i].tileId;
                    if (!visited.Add(n)) continue;
                    queue.Enqueue((n, dist + 1));
                }
            }

            return -1;
        }

        /// <summary>
        /// Greedy: strongest remaining seed → take all that can reach a shared rally under path cap → repeat.
        /// </summary>
        public static List<List<Settlement>> PartitionAssemblies(IList<Settlement> sources)
        {
            var result = new List<List<Settlement>>();
            tmpPartitionRemaining.Clear();
            if (sources != null)
            {
                for (int i = 0; i < sources.Count; i++)
                {
                    Settlement s = sources[i];
                    if (s != null && !s.Destroyed)
                        tmpPartitionRemaining.Add(s);
                }
            }

            while (tmpPartitionRemaining.Count > 0)
            {
                tmpPartitionRemaining.Sort((a, b) =>
                    WorldActions_PackUp.SourcePickWeight(b).CompareTo(WorldActions_PackUp.SourcePickWeight(a)));
                Settlement seed = tmpPartitionRemaining[0];

                tmpAssemblyScratch.Clear();
                for (int i = 0; i < tmpPartitionRemaining.Count; i++)
                {
                    Settlement s = tmpPartitionRemaining[i];
                    if (s == seed || CanReachRallyInCap(s.Tile.tileId, seed.Tile.tileId))
                        tmpAssemblyScratch.Add(s);
                }

                int centroid = ComputeCentroidTile(tmpAssemblyScratch);
                int rallyTile = PickRallyTile(tmpAssemblyScratch, centroid);
                var assembly = new List<Settlement>();
                for (int i = 0; i < tmpAssemblyScratch.Count; i++)
                {
                    Settlement s = tmpAssemblyScratch[i];
                    if (CanReachRallyInCap(s.Tile.tileId, rallyTile))
                        assembly.Add(s);
                }

                if (assembly.Count < 1)
                    assembly.Add(seed);

                result.Add(assembly);
                for (int i = 0; i < assembly.Count; i++)
                    tmpPartitionRemaining.Remove(assembly[i]);
            }

            return result;
        }

        public struct LaunchAssembliesResult
        {
            public int travelersLaunched;
            public int columnsLaunched;
            public int settlementsPacked;
            public List<WorldObject> lookTargets;
        }

        /// <summary>
        /// Partition sources and launch one column per assembly. <paramref name="pickTarget"/> receives the assembly.
        /// </summary>
        public static LaunchAssembliesResult LaunchAssemblies(
            IList<Settlement> sources,
            Func<IList<Settlement>, WorldObject> pickTarget,
            AssaultRallyOptions opts,
            WorldComponent_SpreadManager manager)
        {
            var result = new LaunchAssembliesResult
            {
                lookTargets = new List<WorldObject>()
            };
            if (sources == null || sources.Count < 1 || pickTarget == null || opts == null)
                return result;

            List<List<Settlement>> assemblies = PartitionAssemblies(sources);
            for (int i = 0; i < assemblies.Count; i++)
            {
                List<Settlement> assembly = assemblies[i];
                WorldObject target = pickTarget(assembly);
                if (!TravelerEndpointUtility.IsLiveEndpoint(target))
                {
                    WDVerbose.Msg("AssaultRally LaunchAssemblies skip assembly reason=no-target");
                    continue;
                }

                List<WorldObject_Traveler> launched = LaunchColumn(assembly, target, opts, manager);
                if (launched == null || launched.Count < 1) continue;

                result.columnsLaunched++;
                result.travelersLaunched += launched.Count;
                result.settlementsPacked += launched.Count;
                for (int t = 0; t < launched.Count; t++)
                {
                    if (launched[t] != null && !launched[t].Destroyed)
                        result.lookTargets.Add(launched[t]);
                }
                if (!target.Destroyed)
                    result.lookTargets.Add(target);
            }

            return result;
        }

        public static List<WorldObject_Traveler> LaunchColumn(
            IList<Settlement> assembly,
            WorldObject finalTarget,
            AssaultRallyOptions opts,
            WorldComponent_SpreadManager manager)
        {
            var launched = new List<WorldObject_Traveler>();
            if (assembly == null || assembly.Count < 1 || finalTarget == null || opts == null)
                return launched;

            var live = new List<Settlement>();
            for (int i = 0; i < assembly.Count; i++)
            {
                Settlement s = assembly[i];
                if (s != null && !s.Destroyed)
                    live.Add(s);
            }
            if (live.Count < 1) return launched;

            int groupId = NextGroupId();

            if (live.Count == 1)
            {
                WorldObject_Traveler solo = LaunchSoloRaid(live[0], finalTarget, groupId, opts, manager);
                if (solo != null)
                    launched.Add(solo);
                return launched;
            }

            int centroid = ComputeCentroidTile(live);
            int rallyTile = PickRallyTile(live, centroid);
            if (rallyTile < 0)
            {
                WDVerbose.Msg("AssaultRally LaunchColumn abort reason=no-rally-tile");
                return launched;
            }

            for (int i = 0; i < live.Count; i++)
            {
                Settlement src = live[i];
                if (!CanReachRallyInCap(src.Tile.tileId, rallyTile))
                    continue;
                WorldObject_Traveler t = LaunchRallyTraveler(src, rallyTile, finalTarget, groupId, opts, manager);
                if (t != null)
                    launched.Add(t);
            }

            int expected = launched.Count;
            for (int i = 0; i < launched.Count; i++)
                launched[i].desperationExpectedCount = expected;

            return launched;
        }

        private static WorldObject_Traveler LaunchRallyTraveler(
            Settlement source,
            int rallyTile,
            WorldObject target,
            int groupId,
            AssaultRallyOptions opts,
            WorldComponent_SpreadManager manager)
        {
            if (source == null || source.Destroyed || target == null) return null;
            var comp = source.GetComponent<CompViralSpread>();
            if (comp == null) return null;

            SettlementTier tier = comp.tier;
            float off = comp.offensiveStrength;
            float def = comp.defensiveStrength;
            Faction faction = source.Faction;
            int originTile = source.Tile.tileId;
            string label = source.LabelCap;

            WorldActions_DesperationRaid.SuppressLossNotify(source);
            source.Destroy();
            Outpost_EstablishmentRequirements.InvalidateNearbyCountCache();

            WorldObject_Traveler traveler = (WorldObject_Traveler)WorldObjectMaker.MakeWorldObject(
                DefDatabase<WorldObjectDef>.GetNamed("TSA_WD_Traveler_Raid"));
            traveler.Tile = originTile;
            traveler.SetFaction(faction);
            traveler.mission = TravelerMission.DesperationRally;
            traveler.travelerStrength = Mathf.Max(10f, off);
            traveler.initialStrength = traveler.travelerStrength;
            traveler.projectedArrivalStrength = traveler.travelerStrength;
            traveler.originObject = null;
            traveler.packUpOriginLabel = label;
            traveler.targetObject = target;
            traveler.packUpRequiresRefound = true;
            traveler.massRelocationTier = tier;
            traveler.massRelocationDefensiveStrength = def;
            traveler.massRelocationDestTile = rallyTile;
            traveler.isDesperationRaid = opts.isDesperationRaid;
            traveler.desperationGroupId = groupId;
            traveler.desperationAggressorFactionId = opts.aggressorFactionLoadId;
            traveler.contributionFactors = new Dictionary<WorldObject, float>();

            Find.WorldObjects.Add(traveler);
            traveler.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(rallyTile, traveler));

            if (!string.IsNullOrEmpty(opts.packRallyLogKey))
            {
                manager?.AddLog(new SpreadLogEntry(
                    opts.packRallyLogKey.Translate(label, rallyTile),
                    traveler, target));
            }
            return traveler;
        }

        private static WorldObject_Traveler LaunchSoloRaid(
            Settlement source,
            WorldObject target,
            int groupId,
            AssaultRallyOptions opts,
            WorldComponent_SpreadManager manager)
        {
            if (source == null || source.Destroyed || target == null) return null;
            var comp = source.GetComponent<CompViralSpread>();
            if (comp == null) return null;

            SettlementTier tier = comp.tier;
            float off = comp.offensiveStrength;
            float def = comp.defensiveStrength;
            Faction faction = source.Faction;
            int originTile = source.Tile.tileId;
            string label = source.LabelCap;

            WorldActions_DesperationRaid.SuppressLossNotify(source);
            source.Destroy();
            Outpost_EstablishmentRequirements.InvalidateNearbyCountCache();

            WorldObject_Traveler traveler = (WorldObject_Traveler)WorldObjectMaker.MakeWorldObject(
                DefDatabase<WorldObjectDef>.GetNamed("TSA_WD_Traveler_Raid"));
            traveler.Tile = originTile;
            traveler.SetFaction(faction);
            traveler.mission = TravelerMission.Raid;
            traveler.travelerStrength = Mathf.Max(10f, off);
            traveler.initialStrength = traveler.travelerStrength;
            traveler.projectedArrivalStrength = traveler.travelerStrength;
            traveler.originObject = null;
            traveler.packUpOriginLabel = label;
            traveler.targetObject = target;
            traveler.cachedTargetKind = RaidLaunchGate.ClassifyTarget(target);
            traveler.packUpRequiresRefound = true;
            traveler.massRelocationTier = tier;
            traveler.massRelocationDefensiveStrength = def;
            traveler.isDesperationRaid = opts.isDesperationRaid;
            traveler.desperationGroupId = groupId;
            traveler.desperationAggressorFactionId = opts.aggressorFactionLoadId;
            traveler.desperationExpectedCount = 1;
            traveler.desperationArrivedCount = 1;
            traveler.contributionFactors = new Dictionary<WorldObject, float>();

            Find.WorldObjects.Add(traveler);
            traveler.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(target.Tile, traveler));

            ApplyRaidReservation(traveler, target, manager);

            if (!string.IsNullOrEmpty(opts.packSoloRaidLogKey))
            {
                manager?.AddLog(new SpreadLogEntry(
                    opts.packSoloRaidLogKey.Translate(label, target.Label),
                    traveler, target));
            }
            return traveler;
        }

        public static void ExecuteRallyArrival(WorldObject_Traveler traveler)
        {
            if (traveler == null || traveler.Destroyed) return;
            var manager = Find.World.GetComponent<WorldComponent_SpreadManager>();

            WorldObject_Traveler host = FindHostForGroup(traveler.desperationGroupId, exclude: traveler);
            if (host == null)
            {
                traveler.desperationIsHost = true;
                traveler.desperationArrivedCount = 1;
                traveler.desperationWaitUntilTick = Find.TickManager.TicksGame
                    + CompViralSpread.CooldownTicksFromDays(RallyWaitDays);
                traveler.pather?.StopDead();
                WDVerbose.Msg(
                    $"AssaultRally host wait group={traveler.desperationGroupId} expected={traveler.desperationExpectedCount} until={traveler.desperationWaitUntilTick} desperation={traveler.isDesperationRaid}");
                AbsorbSameTileGroupOrphans(traveler);
                TryFinalizeRallyIfReady(traveler, manager);
                return;
            }

            AbsorbIntoHost(host, traveler);
            AbsorbSameTileGroupOrphans(host);
            TryFinalizeRallyIfReady(host, manager);
        }

        public static void TickRallyHost(WorldObject_Traveler traveler)
        {
            if (traveler == null || traveler.Destroyed) return;
            if (traveler.mission != TravelerMission.DesperationRally) return;
            if (!traveler.desperationIsHost) return;
            if (traveler.pather != null && traveler.pather.moving) return;

            AbsorbSameTileGroupOrphans(traveler);
            TryFinalizeRallyIfReady(traveler, Find.World.GetComponent<WorldComponent_SpreadManager>());
        }

        private static void AbsorbSameTileGroupOrphans(WorldObject_Traveler host)
        {
            if (host == null || host.Destroyed || !host.desperationIsHost) return;
            if (host.desperationGroupId <= 0) return;

            int tileId = host.Tile.tileId;
            var objs = Find.WorldObjects.AllWorldObjects;
            for (int i = objs.Count - 1; i >= 0; i--)
            {
                if (objs[i] is not WorldObject_Traveler t || t.Destroyed || t == host) continue;
                if (t.desperationGroupId != host.desperationGroupId) continue;
                if (t.mission != TravelerMission.DesperationRally) continue;
                if (t.desperationIsHost) continue;
                if (t.Tile.tileId != tileId) continue;
                AbsorbIntoHost(host, t);
            }
        }

        private static void AbsorbIntoHost(WorldObject_Traveler host, WorldObject_Traveler joiner)
        {
            if (host == null || joiner == null || host.Destroyed || joiner.Destroyed) return;
            host.travelerStrength += Mathf.Max(0f, joiner.travelerStrength);
            host.initialStrength += Mathf.Max(0f, joiner.initialStrength);
            host.projectedArrivalStrength = host.travelerStrength;
            host.massRelocationDefensiveStrength += Mathf.Max(0f, joiner.massRelocationDefensiveStrength);
            if (joiner.massRelocationTier > host.massRelocationTier)
                host.massRelocationTier = joiner.massRelocationTier;
            host.desperationArrivedCount = Mathf.Max(1, host.desperationArrivedCount) + 1;

            SettlementTier mergedTier = WorldActions_DesperationRaid.TierFromHostStrength(host.travelerStrength);
            if (mergedTier > host.massRelocationTier)
                host.massRelocationTier = mergedTier;

            joiner.suppressDestroyedWorldFx = true;
            joiner.Destroy();
            WDVerbose.Msg(
                $"AssaultRally absorb group={host.desperationGroupId} arrived={host.desperationArrivedCount}/{host.desperationExpectedCount} str={host.travelerStrength:F0}");
        }

        private static void TryFinalizeRallyIfReady(WorldObject_Traveler host, WorldComponent_SpreadManager manager)
        {
            if (host == null || host.Destroyed || !host.desperationIsHost) return;
            if (host.mission != TravelerMission.DesperationRally) return;

            int now = Find.TickManager.TicksGame;
            bool allIn = host.desperationExpectedCount > 0
                && host.desperationArrivedCount >= host.desperationExpectedCount;
            bool timedOut = host.desperationWaitUntilTick > 0 && now >= host.desperationWaitUntilTick;
            if (!allIn && !timedOut) return;

            ConvertHostToRaid(host, manager);
        }

        private static void ConvertHostToRaid(WorldObject_Traveler host, WorldComponent_SpreadManager manager)
        {
            WorldObject target = host.targetObject;
            if (!TravelerEndpointUtility.IsLiveEndpoint(target))
                target = RetargetDeadEndpoint(host);

            if (!TravelerEndpointUtility.IsLiveEndpoint(target))
            {
                WDVerbose.Msg($"AssaultRally raid abort no-target group={host.desperationGroupId}");
                CancelGroupStragglers(host, manager);
                WorldActions_PackUp.TryRefoundFromTraveler(host);
                host.suppressDestroyedWorldFx = true;
                host.Destroy();
                return;
            }

            host.targetObject = target;
            host.mission = TravelerMission.Raid;
            host.cachedTargetKind = RaidLaunchGate.ClassifyTarget(target);
            host.desperationIsHost = false;
            host.contributionFactors ??= new Dictionary<WorldObject, float>();
            host.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(target.Tile, host));

            ApplyRaidReservation(host, target, manager);

            string logKey = host.isDesperationRaid
                ? "TSA_WD_Log_DesperationRaid_LaunchRaid"
                : "TSA_WD_Log_ForwardAssault_InvasionRallyRaid";
            manager?.AddLog(new SpreadLogEntry(
                logKey.Translate(host.Label, target.Label),
                host, target));
            WDVerbose.Msg(
                $"AssaultRally → Raid group={host.desperationGroupId} tgt={target.Label} str={host.travelerStrength:F0} desperation={host.isDesperationRaid}");

            PromoteGroupStragglersToRaid(host, target, manager);
        }

        private static WorldObject RetargetDeadEndpoint(WorldObject_Traveler host)
        {
            if (host == null) return null;
            if (host.isDesperationRaid)
                return WorldActions_DesperationRaid.RetargetAggressorSitePublic(
                    host.Faction, host.Tile.tileId, host.desperationAggressorFactionId);
            return WorldActions_PackUp.PickNearestPlayerTarget(host.Tile.tileId, WorldActions_PackUp.CollectDistinctPlayerTargets());
        }

        private static void PromoteGroupStragglersToRaid(
            WorldObject_Traveler host,
            WorldObject target,
            WorldComponent_SpreadManager manager)
        {
            if (host == null || host.desperationGroupId <= 0) return;
            if (!TravelerEndpointUtility.IsLiveEndpoint(target)) return;

            int groupId = host.desperationGroupId;
            var objs = Find.WorldObjects.AllWorldObjects;
            for (int i = objs.Count - 1; i >= 0; i--)
            {
                if (objs[i] is not WorldObject_Traveler t || t.Destroyed || t == host) continue;
                if (t.desperationGroupId != groupId) continue;
                if (t.mission != TravelerMission.DesperationRally) continue;

                t.targetObject = target;
                t.mission = TravelerMission.Raid;
                t.cachedTargetKind = RaidLaunchGate.ClassifyTarget(target);
                t.desperationIsHost = false;
                t.contributionFactors ??= new Dictionary<WorldObject, float>();
                t.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(target.Tile, t));
                ApplyRaidReservation(t, target, manager);
                WDVerbose.Msg($"AssaultRally straggler→Raid group={groupId} label={t.Label}");
            }
        }

        private static void CancelGroupStragglers(WorldObject_Traveler host, WorldComponent_SpreadManager manager)
        {
            if (host == null || host.desperationGroupId <= 0) return;
            int groupId = host.desperationGroupId;
            var objs = Find.WorldObjects.AllWorldObjects;
            for (int i = objs.Count - 1; i >= 0; i--)
            {
                if (objs[i] is not WorldObject_Traveler t || t.Destroyed || t == host) continue;
                if (t.desperationGroupId != groupId) continue;
                if (t.mission != TravelerMission.DesperationRally) continue;
                WorldActions_PackUp.TryRefoundFromTraveler(t);
                t.suppressDestroyedWorldFx = true;
                t.Destroy();
            }
        }

        private static void ApplyRaidReservation(
            WorldObject_Traveler traveler,
            WorldObject target,
            WorldComponent_SpreadManager manager)
        {
            if (traveler == null || target == null) return;
            int reservedUntilTick = Raid_DefenseCooldownReservations.ApplyRaidDefenseCooldownReservation(target);
            traveler.targetRaidDefenseCooldownReservationTick = reservedUntilTick;
            if (target is Settlement ps && ps.Faction?.IsPlayer == true && ps.HasMap)
            {
                traveler.playerColonyRaidCooldownReservationTick = reservedUntilTick;
                manager?.RecordPlayerWdRaidLaunch();
                ps.GetComponent<CompViralSpread>()?.MarkPlayerColonyWdRaidPicked();
            }
        }

        private static WorldObject_Traveler FindHostForGroup(int groupId, WorldObject_Traveler exclude)
        {
            if (groupId <= 0) return null;
            var objs = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < objs.Count; i++)
            {
                if (objs[i] is not WorldObject_Traveler t || t.Destroyed) continue;
                if (t == exclude) continue;
                if (t.desperationGroupId != groupId) continue;
                if (t.mission != TravelerMission.DesperationRally) continue;
                if (!t.desperationIsHost) continue;
                return t;
            }
            return null;
        }
    }
}
