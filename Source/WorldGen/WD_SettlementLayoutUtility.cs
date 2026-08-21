using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Destroy and respawn NPC settlements with territory coherence, spacing, and establishment min distance.</summary>
    public static class WD_SettlementLayoutUtility
    {
        private const int SoftClusterRadiusTiles = 12;
        private const int MaxRandomAttemptsPerSettlement = 400;
        private const int MaxNearbyAttemptsPerSettlement = 80;
        /// <summary>At spacing 100%, required distance is minDist * (1 + this). Was 1.5; raised so Spread Out is visible.</summary>
        private const float MaxExtraSpacingFactor = 3f;
        private const int SettlementCountSliderCap = 1250;
        private const int MaxConsecutiveFailuresPerFaction = 25;
        private const int MaxVerboseSkipLogLines = 50;

        private enum RecreateSkipReason
        {
            NoTile,
            SpawnFailed
        }

        /// <summary>Placement distance checks during bulk recreate — snapshot blockers + this-run placements only.</summary>
        private sealed class RecreatePlacementContext
        {
            private readonly WorldGrid grid;
            private readonly List<(int tile, Faction faction)> staticBlockers = new List<(int, Faction)>();
            private readonly List<(int tile, Faction faction)> placedThisRun = new List<(int, Faction)>();
            private readonly List<PlanetTile> neighborScratch = new List<PlanetTile>(8);

            public RecreatePlacementContext(WorldGrid grid) => this.grid = grid;

            public int StaticBlockerCount => staticBlockers.Count;
            public int PlacedThisRunCount => placedThisRun.Count;

            public void SnapshotStaticBlockers()
            {
                staticBlockers.Clear();
                var allObjs = Find.WorldObjects?.AllWorldObjects;
                if (allObjs == null) return;
                for (int i = 0; i < allObjs.Count; i++)
                {
                    WorldObject o = allObjs[i];
                    if (!Outpost_EstablishmentRequirements.IsEstablishmentMinDistanceBlocker(o)) continue;
                    staticBlockers.Add((o.Tile.tileId, o.Faction));
                }
            }

            public void RecordPlacement(int tile, Faction faction) => placedThisRun.Add((tile, faction));

            public bool MeetsRequiredDistance(int tile, int requiredDist, Faction faction, int otherFactionMinDist)
            {
                if (!WD_SettlementEditUtility.IsValidSettlementTile(tile, enforceMinDistance: false))
                    return false;
                if (grid == null) return false;
                if (!CheckBlockerList(staticBlockers, tile, requiredDist, faction, otherFactionMinDist))
                    return false;
                return CheckBlockerList(placedThisRun, tile, requiredDist, faction, otherFactionMinDist);
            }

            private bool CheckBlockerList(
                List<(int tile, Faction faction)> blockers,
                int tile,
                int requiredDist,
                Faction faction,
                int otherFactionMinDist)
            {
                for (int i = 0; i < blockers.Count; i++)
                {
                    (int blockerTile, Faction blockerFaction) = blockers[i];
                    int dist = (int)grid.ApproxDistanceInTiles(tile, blockerTile);
                    int need = requiredDist;
                    if (otherFactionMinDist > need && faction != null && blockerFaction != null && blockerFaction != faction)
                        need = otherFactionMinDist;
                    if (dist < need)
                        return false;
                }
                return true;
            }

            public int TryFindRandomValidTile(int requiredDist, Faction faction, int otherFactionMinDist)
            {
                if (grid == null) return -1;
                int tiles = grid.TilesCount;
                if (tiles <= 0) return -1;

                for (int attempt = 0; attempt < MaxRandomAttemptsPerSettlement; attempt++)
                {
                    int tile = Rand.Range(0, tiles);
                    if (MeetsRequiredDistance(tile, requiredDist, faction, otherFactionMinDist))
                        return tile;
                }

                return -1;
            }

            public int TryFindRandomValidTileForNewCluster(
                int requiredDist,
                Faction faction,
                int otherFactionMinDist,
                List<List<int>> existingClusters,
                int minBetweenClusters)
            {
                if (grid == null) return -1;
                int tiles = grid.TilesCount;
                if (tiles <= 0) return -1;

                for (int attempt = 0; attempt < MaxRandomAttemptsPerSettlement; attempt++)
                {
                    int tile = Rand.Range(0, tiles);
                    if (!MeetsRequiredDistance(tile, requiredDist, faction, otherFactionMinDist))
                        continue;
                    if (minBetweenClusters > 0
                        && !IsFarEnoughFromOtherClusters(tile, existingClusters, minBetweenClusters))
                        continue;
                    return tile;
                }

                return -1;
            }

            public int TryFindNearbyValidTile(List<int> seedTiles, int requiredDist, Faction faction, int otherFactionMinDist)
            {
                if (seedTiles == null || seedTiles.Count == 0 || grid == null) return -1;

                int softRadius = Mathf.Max(
                    SoftClusterRadiusTiles,
                    requiredDist * 4,
                    otherFactionMinDist * 4,
                    Outpost_EstablishmentRequirements.MinDistanceTiles * 4);
                int start = seedTiles[Rand.Range(0, seedTiles.Count)];
                int maxAttempts = Mathf.Max(
                    MaxNearbyAttemptsPerSettlement + requiredDist * 8 + otherFactionMinDist * 4,
                    softRadius * softRadius);

                var visited = new HashSet<int>();
                var queue = new Queue<(int tile, int dist)>();
                queue.Enqueue((start, 0));
                visited.Add(start);

                int attempts = 0;
                while (queue.Count > 0 && attempts < maxAttempts)
                {
                    (int tile, int dist) = queue.Dequeue();
                    if (dist > 0 && MeetsRequiredDistance(tile, requiredDist, faction, otherFactionMinDist))
                        return tile;

                    if (dist >= softRadius) continue;

                    neighborScratch.Clear();
                    grid.GetTileNeighbors(tile, neighborScratch);
                    for (int i = 0; i < neighborScratch.Count; i++)
                    {
                        int n = neighborScratch[i].tileId;
                        if (!visited.Add(n)) continue;
                        if (!PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(n)) continue;
                        queue.Enqueue((n, dist + 1));
                    }

                    attempts++;
                }

                WDVerbose.Msg($"Recreate cluster BFS exhausted budget attempts={attempts} max={maxAttempts} softRadius={softRadius}");
                return -1;
            }

            private bool IsFarEnoughFromOtherClusters(int tile, List<List<int>> clusters, int minBetweenClusters)
            {
                if (clusters == null || clusters.Count == 0 || minBetweenClusters <= 0 || grid == null)
                    return true;
                for (int c = 0; c < clusters.Count; c++)
                {
                    List<int> cluster = clusters[c];
                    if (cluster == null) continue;
                    for (int i = 0; i < cluster.Count; i++)
                    {
                        if ((int)grid.ApproxDistanceInTiles(tile, cluster[i]) < minBetweenClusters)
                            return false;
                    }
                }
                return true;
            }
        }

        private struct ScaleEntry
        {
            public Faction faction;
            public int vanillaCount;
            public int assigned;
            public float remainder;
        }

        public static string BuildRecreateConfirmText()
        {
            EnsureVanillaSnapshot();
            Dictionary<Faction, int> counts = GetScaledNpcSettlementCounts();
            int total = SumCounts(counts);
            WorldComponent_SpreadManager mgr = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            int vanilla = mgr != null && mgr.vanillaNpcSettlementTotal > 0 ? mgr.vanillaNpcSettlementTotal : total;

            if (vanilla <= 0 && (counts == null || counts.Count == 0))
                return "TSA_WD_WorldSetup_RecreateConfirmEmpty".Translate();

            var sb = new StringBuilder();
            sb.AppendLine("TSA_WD_WorldSetup_RecreateConfirmHeader".Translate());
            if (counts != null)
            {
                foreach (var kv in counts)
                {
                    if (kv.Key == null || kv.Value <= 0) continue;
                    sb.AppendLine("TSA_WD_WorldSetup_RecreateConfirmLine".Translate(kv.Key.Name, kv.Value));
                }
            }
            sb.AppendLine("TSA_WD_WorldSetup_RecreateConfirmTotal".Translate(total, vanilla));
            sb.AppendLine();
            bool destroyForts = WorldDominationMod.settings?.worldSetupDestroyFortificationsOnRecreate
                ?? WorldDominationSettings.DefWorldSetupDestroyFortificationsOnRecreate;
            sb.Append(destroyForts
                ? "TSA_WD_WorldSetup_RecreateConfirmFooter".Translate()
                : "TSA_WD_WorldSetup_RecreateConfirmFooterKeepForts".Translate());
            return sb.ToString().TrimEnd();
        }

        public static Dictionary<Faction, int> SnapshotNpcSettlementCounts()
        {
            var counts = new Dictionary<Faction, int>();
            List<Settlement> settlements = Find.WorldObjects?.Settlements;
            if (settlements == null) return counts;

            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement s = settlements[i];
                if (!IsRecreateTargetSettlement(s)) continue;
                Faction f = s.Faction;
                if (f == null) continue;
                if (!counts.TryGetValue(f, out int n)) n = 0;
                counts[f] = n + 1;
            }

            return counts;
        }

        public static void EnsureVanillaSnapshot()
        {
            WorldComponent_SpreadManager mgr = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (mgr == null) return;
            if (mgr.vanillaNpcSettlementCountsByFactionLoadId == null)
                mgr.vanillaNpcSettlementCountsByFactionLoadId = new Dictionary<int, int>();
            if (mgr.vanillaNpcSettlementSnapshotTaken) return;
            if (Find.WorldObjects?.Settlements == null) return;

            Dictionary<Faction, int> live = SnapshotNpcSettlementCounts();
            mgr.vanillaNpcSettlementCountsByFactionLoadId.Clear();
            int total = 0;
            foreach (var kv in live)
            {
                if (kv.Key == null || kv.Value <= 0) continue;
                mgr.vanillaNpcSettlementCountsByFactionLoadId[kv.Key.loadID] = kv.Value;
                total += kv.Value;
            }

            mgr.vanillaNpcSettlementTotal = total;
            if (mgr.worldSetupTargetNpcSettlements < 0)
                mgr.worldSetupTargetNpcSettlements = total;
            mgr.vanillaNpcSettlementSnapshotTaken = true;
        }

        public static int GetVanillaNpcSettlementTotal()
        {
            EnsureVanillaSnapshot();
            WorldComponent_SpreadManager mgr = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (mgr == null) return 0;
            return Mathf.Max(0, mgr.vanillaNpcSettlementTotal);
        }

        public static int GetTargetNpcSettlementCount()
        {
            EnsureVanillaSnapshot();
            WorldComponent_SpreadManager mgr = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (mgr == null) return 0;
            if (mgr.worldSetupTargetNpcSettlements < 0)
                return Mathf.Max(0, mgr.vanillaNpcSettlementTotal);
            return mgr.worldSetupTargetNpcSettlements;
        }

        public static void SetTargetNpcSettlementCount(int count)
        {
            WorldComponent_SpreadManager mgr = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (mgr == null) return;
            mgr.worldSetupTargetNpcSettlements = Mathf.Clamp(count, 0, SettlementCountSliderCap);
        }

        public static int GetSettlementCountSliderMax()
        {
            int vanilla = GetVanillaNpcSettlementTotal();
            int settingCap = WorldDominationMod.settings?.maxSettlements ?? WorldDominationSettings.DefMaxSettlements;
            int max = Mathf.Max(vanilla * 4, settingCap, vanilla);
            return Mathf.Clamp(max, 1, SettlementCountSliderCap);
        }

        public static Dictionary<Faction, int> GetScaledNpcSettlementCounts()
        {
            EnsureVanillaSnapshot();
            EnsureFactionSharesInitialized();
            var result = new Dictionary<Faction, int>();
            WorldComponent_SpreadManager mgr = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (mgr == null)
                return result;

            int target = GetTargetNpcSettlementCount();
            if (target <= 0)
                return result;

            var entries = new List<ScaleEntry>();
            float shareSum = 0f;
            if (mgr.worldSetupFactionSharesInitialized
                && mgr.worldSetupFactionSettlementShares != null
                && mgr.worldSetupFactionSettlementShares.Count > 0)
            {
                foreach (var kv in mgr.worldSetupFactionSettlementShares)
                {
                    if (kv.Value <= 0f) continue;
                    Faction f = FactionByLoadId(kv.Key);
                    if (f == null || f.defeated || !IsRecreateEligibleFaction(f)) continue;
                    shareSum += kv.Value;
                    entries.Add(new ScaleEntry
                    {
                        faction = f,
                        vanillaCount = 0,
                        assigned = 0,
                        remainder = kv.Value
                    });
                }
            }

            if (entries.Count == 0 || shareSum <= 0f)
            {
                // Shares initialized but all zero: no settlements (avoid silent vanilla fallback while sliders read 0).
                if (mgr.worldSetupFactionSharesInitialized)
                    return result;

                entries.Clear();
                if (mgr.vanillaNpcSettlementCountsByFactionLoadId == null)
                    return result;
                int vanillaTotal = mgr.vanillaNpcSettlementTotal;
                if (vanillaTotal <= 0)
                    return result;

                foreach (var kv in mgr.vanillaNpcSettlementCountsByFactionLoadId)
                {
                    if (kv.Value <= 0) continue;
                    Faction f = FactionByLoadId(kv.Key);
                    if (f == null || f.defeated || !IsRecreateEligibleFaction(f)) continue;
                    float exact = target * (kv.Value / (float)vanillaTotal);
                    int floor = (int)exact;
                    entries.Add(new ScaleEntry
                    {
                        faction = f,
                        vanillaCount = kv.Value,
                        assigned = floor,
                        remainder = exact - floor
                    });
                }
            }
            else
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    ScaleEntry e = entries[i];
                    float exact = target * (e.remainder / shareSum);
                    e.assigned = (int)exact;
                    e.remainder = exact - e.assigned;
                    entries[i] = e;
                }
            }

            if (entries.Count == 0)
                return result;

            int assigned = 0;
            for (int i = 0; i < entries.Count; i++)
                assigned += entries[i].assigned;

            int leftover = target - assigned;
            entries.Sort((a, b) => b.remainder.CompareTo(a.remainder));
            for (int k = 0; k < leftover; k++)
            {
                int idx = k % entries.Count;
                ScaleEntry e = entries[idx];
                e.assigned++;
                entries[idx] = e;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].assigned <= 0) continue;
                result[entries[i].faction] = entries[i].assigned;
            }

            return result;
        }

        public static bool IsRecreateEligibleFaction(Faction f)
        {
            if (f == null || f.IsPlayer || f.defeated) return false;
            if (WorldActions_Utils.IsExcludedFaction(f)) return false;
            return true;
        }

        public static List<Faction> ListRecreateEligibleFactions()
        {
            var list = new List<Faction>();
            List<Faction> all = Find.FactionManager?.AllFactionsListForReading;
            if (all == null) return list;
            for (int i = 0; i < all.Count; i++)
            {
                Faction f = all[i];
                if (!IsRecreateEligibleFaction(f)) continue;
                list.Add(f);
            }
            list.Sort((a, b) => string.Compare(a.Name, b.Name, System.StringComparison.OrdinalIgnoreCase));
            return list;
        }

        public static void EnsureFactionSharesInitialized()
        {
            EnsureVanillaSnapshot();
            WorldComponent_SpreadManager mgr = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (mgr == null) return;
            if (mgr.worldSetupFactionSettlementShares == null)
                mgr.worldSetupFactionSettlementShares = new Dictionary<int, float>();
            if (mgr.worldSetupFactionSharesInitialized
                && mgr.worldSetupFactionSettlementShares.Count > 0)
            {
                PruneIneligibleFactionShares(mgr);
                return;
            }
            SeedFactionSharesFromVanillaSnapshot(mgr);
        }

        public static void ResetFactionSharesToVanillaSnapshot()
        {
            EnsureVanillaSnapshot();
            WorldComponent_SpreadManager mgr = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (mgr == null) return;
            SeedFactionSharesFromVanillaSnapshot(mgr);
        }

        private static void SeedFactionSharesFromVanillaSnapshot(WorldComponent_SpreadManager mgr)
        {
            if (mgr.worldSetupFactionSettlementShares == null)
                mgr.worldSetupFactionSettlementShares = new Dictionary<int, float>();
            mgr.worldSetupFactionSettlementShares.Clear();

            List<Faction> factions = ListRecreateEligibleFactions();
            var vanilla = mgr.vanillaNpcSettlementCountsByFactionLoadId;
            for (int i = 0; i < factions.Count; i++)
            {
                Faction f = factions[i];
                if (f == null) continue;
                float share = 0f;
                if (vanilla != null && vanilla.TryGetValue(f.loadID, out int n) && n > 0)
                    share = n;
                mgr.worldSetupFactionSettlementShares[f.loadID] = share;
            }

            mgr.worldSetupFactionSharesInitialized = true;
        }

        /// <summary>Drop share entries for factions no longer in WD scope (e.g. orbit-only).</summary>
        private static void PruneIneligibleFactionShares(WorldComponent_SpreadManager mgr)
        {
            if (mgr?.worldSetupFactionSettlementShares == null || mgr.worldSetupFactionSettlementShares.Count == 0)
                return;

            var doomed = new List<int>();
            foreach (var kv in mgr.worldSetupFactionSettlementShares)
            {
                Faction f = FactionByLoadId(kv.Key);
                if (f == null || !IsRecreateEligibleFaction(f))
                    doomed.Add(kv.Key);
            }

            for (int i = 0; i < doomed.Count; i++)
                mgr.worldSetupFactionSettlementShares.Remove(doomed[i]);
        }

        public static float GetFactionShare(Faction f)
        {
            if (f == null || !IsRecreateEligibleFaction(f)) return 0f;
            EnsureFactionSharesInitialized();
            WorldComponent_SpreadManager mgr = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (mgr?.worldSetupFactionSettlementShares == null) return 0f;
            return mgr.worldSetupFactionSettlementShares.TryGetValue(f.loadID, out float v) ? v : 0f;
        }

        public static void SetFactionShare(Faction f, float share)
        {
            if (f == null || !IsRecreateEligibleFaction(f)) return;
            EnsureFactionSharesInitialized();
            WorldComponent_SpreadManager mgr = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (mgr?.worldSetupFactionSettlementShares == null) return;
            mgr.worldSetupFactionSettlementShares[f.loadID] = Mathf.Clamp(share, 0f, 200f);
            mgr.worldSetupFactionSharesInitialized = true;
        }

        public static float GetFactionSharePool()
        {
            EnsureFactionSharesInitialized();
            WorldComponent_SpreadManager mgr = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (mgr?.worldSetupFactionSettlementShares == null) return 0f;
            float sum = 0f;
            foreach (var kv in mgr.worldSetupFactionSettlementShares)
            {
                Faction f = FactionByLoadId(kv.Key);
                if (f == null || !IsRecreateEligibleFaction(f)) continue;
                sum += Mathf.Max(0f, kv.Value);
            }
            return sum;
        }

        public static void RecreateNpcSettlements()
        {
            if (Find.World == null || Find.WorldGrid == null)
            {
                Log.Error("[TSA WD] RecreateNpcSettlements: world not ready.");
                ShowRecreateErrorDialog();
                return;
            }

            bool completed = false;
            int targetTotal = 0;
            int placed = 0;
            var factionAbortLines = new List<string>();
            var swTotal = Stopwatch.StartNew();

            try
            {
                Dictionary<Faction, int> counts = GetScaledNpcSettlementCounts();
                targetTotal = SumCounts(counts);
                bool destroyForts = WorldDominationMod.settings?.worldSetupDestroyFortificationsOnRecreate
                    ?? WorldDominationSettings.DefWorldSetupDestroyFortificationsOnRecreate;

                LogRecreateStart(counts, targetTotal, destroyForts);

                var swDestroy = Stopwatch.StartNew();
                int destroyed = DestroyNpcSettlementsAndFortifications(destroyForts);
                swDestroy.Stop();
                WD_DevPerformanceSpikeLog.Msg($"Recreate destroy settlements={destroyed} fortsCleared={destroyForts} ms={swDestroy.ElapsedMilliseconds}");

                var placementContext = new RecreatePlacementContext(Find.WorldGrid);
                placementContext.SnapshotStaticBlockers();
                WDVerbose.Msg($"Recreate placement snapshot staticBlockers={placementContext.StaticBlockerCount}");

                float coherence = Mathf.Clamp01((WorldDominationMod.settings?.settlementTerritoryCoherence
                    ?? WorldDominationSettings.DefSettlementTerritoryCoherence) / 100f);
                float spacing01 = Mathf.Clamp01((WorldDominationMod.settings?.settlementTerritorySpacing
                    ?? WorldDominationSettings.DefSettlementTerritorySpacing) / 100f);
                float otherFaction01 = Mathf.Clamp01((WorldDominationMod.settings?.settlementOtherFactionDistance
                    ?? WorldDominationSettings.DefSettlementOtherFactionDistance) / 100f);
                int maxPerCluster = Mathf.Clamp(
                    WorldDominationMod.settings?.settlementMaxPerCluster
                    ?? WorldDominationSettings.DefSettlementMaxPerCluster, 1, 20);
                int minBetweenClusters = Mathf.Clamp(
                    WorldDominationMod.settings?.settlementMinDistanceBetweenClusters
                    ?? WorldDominationSettings.DefSettlementMinDistanceBetweenClusters, 0, 50);
                int minDist = Outpost_EstablishmentRequirements.MinDistanceTiles;
                int extraMax = Mathf.RoundToInt(minDist * MaxExtraSpacingFactor * spacing01);
                int otherFactionPad = Mathf.RoundToInt(minDist * 3f * otherFaction01);

                int skippedNoTile = 0;
                int skippedSpawnFailed = 0;
                int verboseSkipLines = 0;

                var swPlace = Stopwatch.StartNew();
                foreach (var kv in counts)
                {
                    Faction faction = kv.Key;
                    int want = kv.Value;
                    if (faction == null || want <= 0) continue;

                    int factionPlaced = 0;
                    int factionSkipped = 0;
                    int consecutiveFailures = 0;
                    var clusters = new List<List<int>>();

                    for (int i = 0; i < want; i++)
                    {
                        if (consecutiveFailures >= MaxConsecutiveFailuresPerFaction)
                        {
                            factionAbortLines.Add("TSA_WD_WorldSetup_RecreateResultFactionAbort".Translate(
                                faction.Name, MaxConsecutiveFailuresPerFaction));
                            WDVerbose.Msg($"Recreate early abort faction={faction.Name} after {MaxConsecutiveFailuresPerFaction} consecutive failures placed={factionPlaced} want={want}");
                            break;
                        }

                        int extra = extraMax;
                        if (extraMax >= 2)
                            extra = Rand.RangeInclusive(extraMax - 1, extraMax);
                        int requiredDist = minDist + extra;
                        int otherFactionMinDist = requiredDist + otherFactionPad;

                        int tile = -1;
                        int joinClusterIdx = -1;
                        bool enforceClusterSeparation = false;
                        bool preferCluster = clusters.Count > 0 && Rand.Chance(coherence);
                        if (preferCluster)
                        {
                            var eligible = new List<int>();
                            for (int c = 0; c < clusters.Count; c++)
                            {
                                if (clusters[c].Count < maxPerCluster)
                                    eligible.Add(c);
                            }
                            if (eligible.Count > 0)
                            {
                                joinClusterIdx = eligible[Rand.Range(0, eligible.Count)];
                                tile = placementContext.TryFindNearbyValidTile(
                                    clusters[joinClusterIdx], requiredDist, faction, otherFactionMinDist);
                                if (tile < 0)
                                {
                                    joinClusterIdx = -1;
                                    enforceClusterSeparation = true;
                                }
                            }
                            else
                            {
                                enforceClusterSeparation = true;
                            }
                        }

                        if (tile < 0)
                        {
                            int clusterMin = enforceClusterSeparation ? minBetweenClusters : 0;
                            tile = placementContext.TryFindRandomValidTileForNewCluster(
                                requiredDist, faction, otherFactionMinDist, clusters, clusterMin);
                            if (tile < 0 && clusterMin > 0)
                            {
                                tile = placementContext.TryFindRandomValidTileForNewCluster(
                                    requiredDist, faction, otherFactionMinDist, clusters, 0);
                            }
                            joinClusterIdx = -1;
                        }

                        if (tile < 0)
                        {
                            skippedNoTile++;
                            factionSkipped++;
                            consecutiveFailures++;
                            LogRecreateSkip(faction, i, RecreateSkipReason.NoTile, requiredDist, otherFactionMinDist,
                                ref verboseSkipLines);
                            continue;
                        }

                        SettlementTier tier = PickRandomTier();
                        if (!WD_SettlementEditUtility.TrySpawnWdSettlementAt(
                                tile, tier, faction, out _, enforceMinDistance: false, deferSideEffects: true))
                        {
                            skippedSpawnFailed++;
                            factionSkipped++;
                            consecutiveFailures++;
                            LogRecreateSkip(faction, i, RecreateSkipReason.SpawnFailed, requiredDist, otherFactionMinDist,
                                ref verboseSkipLines);
                            continue;
                        }

                        placementContext.RecordPlacement(tile, faction);
                        consecutiveFailures = 0;
                        if (joinClusterIdx >= 0)
                            clusters[joinClusterIdx].Add(tile);
                        else
                            clusters.Add(new List<int> { tile });
                        factionPlaced++;
                        placed++;
                    }

                    WDVerbose.Msg($"Recreate faction={faction.Name} want={want} placed={factionPlaced} skipped={factionSkipped}");
                }

                swPlace.Stop();
                WD_DevPerformanceSpikeLog.Msg($"Recreate placement placed={placed} skippedNoTile={skippedNoTile} skippedSpawn={skippedSpawnFailed} ms={swPlace.ElapsedMilliseconds}");

                completed = true;
                ShowRecreateResultDialog(targetTotal, placed, factionAbortLines);
            }
            catch (Exception ex)
            {
                Log.Error($"[TSA WD] RecreateNpcSettlements failed target={targetTotal} placed={placed}: {ex}");
                LogRecreateSettingsSnapshot();
                ShowRecreateErrorDialog();
            }
            finally
            {
                var swFinalize = Stopwatch.StartNew();
                Outpost_EstablishmentRequirements.InvalidateNearbyCountCache();
                Find.World?.GetComponent<WorldComponent_SpreadManager>()?.Notify_WeightsChanged();
                WorldComponent_SettlementWatchIndex.Get()?.Invalidate();
                swFinalize.Stop();
                swTotal.Stop();
                WD_DevPerformanceSpikeLog.Msg($"Recreate finalize ms={swFinalize.ElapsedMilliseconds} totalMs={swTotal.ElapsedMilliseconds} completed={completed}");
            }
        }

        private static void LogRecreateStart(Dictionary<Faction, int> counts, int targetTotal, bool destroyForts)
        {
            var s = WorldDominationMod.settings;
            WDVerbose.Msg($"Recreate begin target={targetTotal} destroyForts={destroyForts} coherence={s?.settlementTerritoryCoherence ?? WorldDominationSettings.DefSettlementTerritoryCoherence} spacing={s?.settlementTerritorySpacing ?? WorldDominationSettings.DefSettlementTerritorySpacing} otherFactionDist={s?.settlementOtherFactionDistance ?? WorldDominationSettings.DefSettlementOtherFactionDistance} maxPerCluster={s?.settlementMaxPerCluster ?? WorldDominationSettings.DefSettlementMaxPerCluster} minClusterDist={s?.settlementMinDistanceBetweenClusters ?? WorldDominationSettings.DefSettlementMinDistanceBetweenClusters}");
            if (counts == null) return;
            foreach (var kv in counts)
            {
                if (kv.Key == null || kv.Value <= 0) continue;
                WDVerbose.Msg($"Recreate faction quota {kv.Key.Name}={kv.Value}");
            }
        }

        private static void LogRecreateSkip(
            Faction faction,
            int index,
            RecreateSkipReason reason,
            int requiredDist,
            int otherFactionMinDist,
            ref int verboseSkipLines)
        {
            if (verboseSkipLines >= MaxVerboseSkipLogLines) return;
            verboseSkipLines++;
            string factionName = faction?.Name ?? "?";
            WDVerbose.Msg($"Recreate skip faction={factionName} index={index} reason={reason} requiredDist={requiredDist} otherFactionMinDist={otherFactionMinDist}");
            if (verboseSkipLines == MaxVerboseSkipLogLines)
                WDVerbose.Msg("Recreate skip log capped; further skips omitted from verbose log");
        }

        private static void LogRecreateSettingsSnapshot()
        {
            var s = WorldDominationMod.settings;
            Log.Error($"[TSA WD] Recreate settings snapshot: target={GetTargetNpcSettlementCount()} coherence={s?.settlementTerritoryCoherence} spacing={s?.settlementTerritorySpacing} otherFaction={s?.settlementOtherFactionDistance} maxPerCluster={s?.settlementMaxPerCluster} minClusterDist={s?.settlementMinDistanceBetweenClusters}");
        }

        private static void ShowRecreateResultDialog(int targetTotal, int placed, List<string> factionAbortLines)
        {
            string body;
            if (placed >= targetTotal)
            {
                body = "TSA_WD_WorldSetup_RecreateResultFull".Translate(targetTotal);
            }
            else
            {
                int skipped = targetTotal - placed;
                body = "TSA_WD_WorldSetup_RecreateResultPartial".Translate(targetTotal, placed, skipped);
                if (factionAbortLines != null && factionAbortLines.Count > 0)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine(body);
                    sb.AppendLine();
                    for (int i = 0; i < factionAbortLines.Count; i++)
                        sb.AppendLine(factionAbortLines[i]);
                    body = sb.ToString().TrimEnd();
                }
            }

            Find.WindowStack.Add(new Dialog_MessageBox(
                body,
                "OK".Translate(),
                null,
                null,
                null,
                "TSA_WD_WorldSetup_RecreateResultTitle".Translate()));
        }

        private static void ShowRecreateErrorDialog()
        {
            Find.WindowStack.Add(new Dialog_MessageBox(
                "TSA_WD_WorldSetup_RecreateResultError".Translate(),
                "OK".Translate(),
                null,
                null,
                null,
                "TSA_WD_WorldSetup_RecreateResultTitle".Translate()));
        }

        public static int DestroyNpcSettlementsAndFortifications(bool clearFortifications = true)
        {
            if (clearFortifications)
                ClearNpcFortifications();

            List<Settlement> settlements = Find.WorldObjects?.Settlements;
            if (settlements == null) return 0;

            var doomed = new List<Settlement>();
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement s = settlements[i];
                if (IsRecreateTargetSettlement(s))
                    doomed.Add(s);
            }

            for (int i = 0; i < doomed.Count; i++)
            {
                if (doomed[i] != null && !doomed[i].Destroyed)
                    doomed[i].Destroy();
            }

            Outpost_EstablishmentRequirements.InvalidateNearbyCountCache();
            return doomed.Count;
        }

        public static bool IsRecreateTargetSettlement(Settlement s)
        {
            if (s == null || s.Destroyed) return false;
            if (s.Faction == null || s.Faction.IsPlayer) return false;
            if (!PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(s.Tile)) return false;
            if (WorldActions_Utils.IsExcludedFaction(s.Faction)) return false;
            return true;
        }

        private static int SumCounts(Dictionary<Faction, int> counts)
        {
            if (counts == null) return 0;
            int total = 0;
            foreach (var kv in counts)
                total += kv.Value;
            return total;
        }

        private static Faction FactionByLoadId(int loadId)
        {
            List<Faction> factions = Find.FactionManager?.AllFactionsListForReading;
            if (factions == null) return null;
            for (int i = 0; i < factions.Count; i++)
            {
                Faction f = factions[i];
                if (f != null && f.loadID == loadId)
                    return f;
            }
            return null;
        }

        private static void ClearNpcFortifications()
        {
            WorldComponent_RoadBlocks blocks = WorldComponent_RoadBlocks.Get();
            if (blocks?.Records != null)
            {
                var clearTiles = new List<int>();
                for (int i = 0; i < blocks.Records.Count; i++)
                {
                    RoadBlockRecord r = blocks.Records[i];
                    if (r == null) continue;
                    if (r.builtByFaction != null && r.builtByFaction.IsPlayer) continue;
                    clearTiles.Add(r.tileId);
                }
                for (int i = 0; i < clearTiles.Count; i++)
                    blocks.TryClear(clearTiles[i]);
            }

            WorldComponent_SpikeTraps traps = WorldComponent_SpikeTraps.Get();
            if (traps?.Records != null)
            {
                var clearTiles = new List<int>();
                for (int i = 0; i < traps.Records.Count; i++)
                {
                    SpikeTrapRecord r = traps.Records[i];
                    if (r == null) continue;
                    if (r.builtByFaction != null && r.builtByFaction.IsPlayer) continue;
                    clearTiles.Add(r.tileId);
                }
                for (int i = 0; i < clearTiles.Count; i++)
                    traps.TryClear(clearTiles[i]);
            }

            var all = Find.WorldObjects?.AllWorldObjects;
            if (all == null) return;
            var turrets = new List<WorldObject_AT_Turret>();
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is WorldObject_AT_Turret t && !t.Destroyed
                    && (t.Faction == null || !t.Faction.IsPlayer))
                    turrets.Add(t);
            }
            for (int i = 0; i < turrets.Count; i++)
                turrets[i].Destroy();
        }

        private static SettlementTier PickRandomTier()
        {
            var s = WorldDominationMod.settings;
            if (s == null) return SettlementTier.T1;
            float total = s.TotalGenWeight;
            if (total <= 0f) return SettlementTier.T1;
            float rand = Rand.Range(0f, total);
            if (rand < s.genWeightT1) return SettlementTier.T1;
            if (rand < s.genWeightT1 + s.genWeightT2) return SettlementTier.T2;
            if (rand < s.genWeightT1 + s.genWeightT2 + s.genWeightT3) return SettlementTier.T3;
            return SettlementTier.T4;
        }
    }
}
