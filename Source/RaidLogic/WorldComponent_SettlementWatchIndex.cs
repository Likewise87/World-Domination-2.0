using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Which purpose a watcher entry in <see cref="WorldComponent_SettlementWatchIndex"/> serves for a given tile.
    /// </summary>
    [Flags]
    public enum WatchCapability
    {
        None = 0,
        /// <summary>Registered ground <see cref="IDefensiveInterceptor"/> (mortar / Rapid Response outpost, turret, NPC T4 settlement) whose configured range covers this tile.</summary>
        Interceptor = 1 << 0,
        /// <summary>Any non-player-map settlement or player outpost whose ally/awareness radius covers this tile — used as the "nearby settlements" pool for target-of-opportunity and marauding.</summary>
        Nearby = 1 << 1,
        /// <summary>Plain NPC settlements (no Rapid Response/mortar) at or above min ambush tier, within their dedicated ambush-only radius.</summary>
        Ambush = 1 << 2,
        Any = Interceptor | Nearby | Ambush
    }

    /// <summary>
    /// Shared lazily-built cache: tile -> watchers (settlements / outposts / turrets) whose range covers that tile.
    /// Single BFS flood-fill per watcher, reused by target-of-opportunity retargeting, post-conquest marauding,
    /// settlement ambush, and ground mortar/Rapid Response eventification — replaces several independent O(n) scans
    /// with one O(1) dictionary lookup per traveler tile-exit.
    /// Rebuilt lazily (on first query after invalidation) and by a daily safety-net rebuild.
    /// </summary>
    public class WorldComponent_SettlementWatchIndex : WorldComponent
    {
        private readonly struct WatcherEntry
        {
            public readonly WorldObject watcher;
            public readonly WatchCapability capability;
            public WatcherEntry(WorldObject watcher, WatchCapability capability)
            {
                this.watcher = watcher;
                this.capability = capability;
            }
        }

        private readonly Dictionary<int, List<WatcherEntry>> watchersByTile = new Dictionary<int, List<WatcherEntry>>();
        private static readonly List<WorldObject> EmptyResult = new List<WorldObject>();
        private readonly List<WorldObject> resultScratch = new List<WorldObject>();
        private readonly HashSet<WorldObject> interceptorSelvesScratch = new HashSet<WorldObject>();

        private bool dirty = true;
        private int lastRebuildTick = -1;
        private long lastRebuildElapsedMs = -1;
        private int lastRebuildWatcherCount = -1;
        private const int SafetyRebuildIntervalTicks = 60000;

        /// <summary>
        /// Feature A/B/C anti-dogpile stamps, keyed by <see cref="WorldObject.ID"/>. Intentionally not
        /// scribed (short-lived throttle, not gameplay-critical state) — a fresh <see cref="World"/> load naturally
        /// resets both to empty, per the plan's explicit "safe to reset on load" simplification.
        /// candidateBecameTargetTick: a settlement/outpost that just accepted a Feature A retarget or Feature B
        /// maraud continuation (stops several independent travelers converging on the same weak target at once).
        /// </summary>
        private readonly Dictionary<int, int> candidateBecameTargetTick = new Dictionary<int, int>();
        /// <summary>Feature C target-side stamp: a traveler/caravan that was just dispatched at by an ambush (stops several settlements independently ambushing the same passing target in the same window).</summary>
        private readonly Dictionary<int, int> targetBecameAmbushTargetTick = new Dictionary<int, int>();
        private int lastStampSweepTick = -1;
        private int liveAmbushSallyCount;
        private bool liveAmbushSallyCountNeedsRecount = true;

        // BFS scratch (reused across watchers to avoid per-call allocation).
        private readonly Queue<int> bfsFrontier = new Queue<int>();
        private readonly HashSet<int> bfsVisited = new HashSet<int>();
        private readonly Dictionary<int, int> bfsDist = new Dictionary<int, int>();
        private readonly List<PlanetTile> bfsNeighborScratch = new List<PlanetTile>(8);

        public WorldComponent_SettlementWatchIndex(World world) : base(world) { }

        public static WorldComponent_SettlementWatchIndex Get() => Find.World?.GetComponent<WorldComponent_SettlementWatchIndex>();

        /// <summary>Call after anything that changes watcher membership or range (outpost built/destroyed/upgraded, settlement founded/razed, turret toggled, escalation stage changed).</summary>
        public void Invalidate() => dirty = true;

        public override void WorldComponentTick()
        {
            if (dirty) return;
            int now = Find.TickManager?.TicksGame ?? 0;
            if (now - lastRebuildTick >= SafetyRebuildIntervalTicks)
                dirty = true;
        }

        /// <summary>Watchers of <paramref name="tileId"/> matching <paramref name="capability"/>. Empty (never null) when none.</summary>
        public List<WorldObject> GetWatchers(int tileId, WatchCapability capability)
        {
            EnsureBuilt();
            resultScratch.Clear();
            if (tileId < 0 || !watchersByTile.TryGetValue(tileId, out var list))
                return resultScratch;
            for (int i = 0; i < list.Count; i++)
            {
                WatcherEntry e = list[i];
                if ((e.capability & capability) == 0) continue;
                if (e.watcher == null || e.watcher.Destroyed) continue;
                resultScratch.Add(e.watcher);
            }
            return resultScratch;
        }

        private void EnsureBuilt()
        {
            if (!dirty) return;
            RebuildNow();
        }

        private void RebuildNow()
        {
            dirty = false;
            lastRebuildTick = Find.TickManager?.TicksGame ?? 0;
            watchersByTile.Clear();

            if (Find.WorldGrid == null || Find.WorldObjects == null)
                return;

            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            var seth = WorldDominationMod.settings;
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            int watcherCount = 0;

            // --- Interceptor capability: registered ground/AA defensive interceptors (range from the interceptor itself). ---
            interceptorSelvesScratch.Clear();
            var sched = WorldComponent_InterceptionScheduler.Current;
            if (sched != null)
            {
                foreach (IDefensiveInterceptor ip in sched.AllInterceptorsSnapshot())
                {
                    WorldObject self = ip?.Self;
                    if (self == null || self.Destroyed) continue;
                    interceptorSelvesScratch.Add(self);
                    float range = SafeRange(ip);
                    if (range <= 0f) continue;
                    PlanetTile tile = self.Tile;
                    if (!tile.Valid || !IsWatchableTile(tile)) continue;
                    BfsAdd(self, tile, range, WatchCapability.Interceptor);
                    watcherCount++;
                }
            }

            // --- Nearby capability: every non-player-map settlement + player outpost (target-of-opportunity / maraud pool). ---
            // --- Ambush capability: plain (non-interceptor) NPC settlements get a separate, smaller dedicated ambush radius. ---
            bool ambushEnabled = seth != null && seth.experimentalSettlementAmbush;
            float ambushRadius = seth?.settlementAmbushWatchRangeTiles ?? WorldDominationSettings.DefSettlementAmbushWatchRangeTiles;
            foreach (Settlement s in Find.WorldObjects.Settlements)
            {
                if (s == null || s.Destroyed || s.Faction == null) continue;
                var comp = s.GetComponent<CompViralSpread>();
                if (comp == null || comp.IsPlayerMapSettlement) continue;
                PlanetTile tile = s.Tile;
                if (!tile.Valid || !IsWatchableTile(tile)) continue;
                float radius = AllyRadiusUtil.GetEffective(s, seth, manager);
                if (radius > 0f)
                {
                    BfsAdd(s, tile, radius, WatchCapability.Nearby);
                    watcherCount++;
                }
                if (ambushEnabled && ambushRadius > 0f && !IsRegisteredInterceptor(s)
                    && comp.tier >= (seth?.settlementAmbushMinTier ?? WorldDominationSettings.DefSettlementAmbushMinTier))
                    BfsAdd(s, tile, ambushRadius, WatchCapability.Ambush);
            }

            var allObjects = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < allObjects.Count; i++)
            {
                if (!(allObjects[i] is WorldObject_WD_Outpost outpost) || outpost.Destroyed) continue;
                PlanetTile tile = outpost.Tile;
                if (!tile.Valid || !IsWatchableTile(tile)) continue;
                float radius = AllyRadiusUtil.GetEffective(outpost, seth, manager);
                if (radius <= 0f) continue;
                BfsAdd(outpost, tile, radius, WatchCapability.Nearby);
                watcherCount++;
            }

            // --- Nearby: AT Turrets at fire-range magnet radius (save/restore pull; not settlement ToO). ---
            for (int i = 0; i < allObjects.Count; i++)
            {
                if (!(allObjects[i] is WorldObject_AT_Turret turret) || turret.Destroyed) continue;
                PlanetTile tile = turret.Tile;
                if (!tile.Valid || !IsWatchableTile(tile)) continue;
                BfsAdd(turret, tile, turret.EffectiveRangeTiles, WatchCapability.Nearby);
                watcherCount++;
            }

            sw.Stop();
            lastRebuildElapsedMs = sw.ElapsedMilliseconds;
            lastRebuildWatcherCount = watcherCount;
            if (Prefs.DevMode)
                Log.Message($"[TSA WD] SettlementWatchIndex rebuilt: watchers={watcherCount} tiles={watchersByTile.Count} in {lastRebuildElapsedMs}ms");
        }

        private static float SafeRange(IDefensiveInterceptor ip)
        {
            try { return ip.InterceptorRange; }
            catch { return 0f; }
        }

        /// <summary>Excludes orbit / non-surface layers and space-like tiles; permissive on unresolved layer data (matches <see cref="WorldActions_Utils.IsWdSurfaceTile"/> semantics).</summary>
        private static bool IsWatchableTile(PlanetTile tile) => WorldActions_Utils.IsWdSurfaceTile(tile);

        private bool IsRegisteredInterceptor(WorldObject wo) => wo != null && interceptorSelvesScratch.Contains(wo);

        private void BfsAdd(WorldObject watcher, PlanetTile origin, float radiusTiles, WatchCapability cap)
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || !origin.Valid || radiusTiles <= 0f) return;
            int maxRadius = Mathf.CeilToInt(radiusTiles);

            bfsFrontier.Clear();
            bfsVisited.Clear();
            bfsDist.Clear();

            int originId = origin.tileId;
            bfsFrontier.Enqueue(originId);
            bfsVisited.Add(originId);
            bfsDist[originId] = 0;
            AddWatcherEntry(originId, watcher, cap);

            while (bfsFrontier.Count > 0)
            {
                int curId = bfsFrontier.Dequeue();
                int dist = bfsDist[curId];
                if (dist >= maxRadius) continue;

                PlanetTile curTile = new PlanetTile(curId, origin.Layer);
                bfsNeighborScratch.Clear();
                grid.GetTileNeighbors(curTile, bfsNeighborScratch);
                for (int i = 0; i < bfsNeighborScratch.Count; i++)
                {
                    PlanetTile n = bfsNeighborScratch[i];
                    if (!n.Valid) continue;
                    int nId = n.tileId;
                    if (bfsVisited.Contains(nId)) continue;
                    if (!IsWatchableTile(n)) continue;
                    bfsVisited.Add(nId);
                    bfsDist[nId] = dist + 1;
                    AddWatcherEntry(nId, watcher, cap);
                    bfsFrontier.Enqueue(nId);
                }
            }
        }

        private void AddWatcherEntry(int tileId, WorldObject watcher, WatchCapability cap)
        {
            if (!watchersByTile.TryGetValue(tileId, out var list))
            {
                list = new List<WatcherEntry>(2);
                watchersByTile[tileId] = list;
            }
            list.Add(new WatcherEntry(watcher, cap));
        }

        /// <summary>Debug: current tile count, last rebuild timing, and rebuild staleness for inspection.</summary>
        public string DebugSummary()
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            return $"tiles={watchersByTile.Count} watchers={lastRebuildWatcherCount} dirty={dirty} lastRebuild={lastRebuildTick} age={(now - lastRebuildTick)}t rebuildMs={lastRebuildElapsedMs} candidateStamps={candidateBecameTargetTick.Count} ambushStamps={targetBecameAmbushTargetTick.Count}";
        }

        /// <summary>Debug-only: forces an immediate rebuild (bypassing the lazy dirty check) and returns the resulting summary, so a debug action can report fresh timings/tile counts on demand.</summary>
        public string DebugForceRebuildAndSummarize()
        {
            dirty = true;
            RebuildNow();
            return DebugSummary();
        }

        /// <summary>True when a global NPC sally cap is set and that many ambush interceptors are already in flight. 0 cap = unlimited.</summary>
        public bool IsAmbushConcurrentCapReached(int cap)
        {
            if (cap <= 0) return false;
            EnsureLiveAmbushSallyCount();
            return liveAmbushSallyCount >= cap;
        }

        public void NotifyAmbushSallySpawned()
        {
            if (liveAmbushSallyCountNeedsRecount)
                EnsureLiveAmbushSallyCount();
            else
                liveAmbushSallyCount++;
        }

        /// <summary>Call from traveler Destroy while the object is still counted as spawned. Decrement once; do not also call from AbortTraveler.</summary>
        public void NotifyAmbushSallyDestroyed()
        {
            if (liveAmbushSallyCountNeedsRecount) return;
            if (liveAmbushSallyCount > 0)
                liveAmbushSallyCount--;
        }

        private void EnsureLiveAmbushSallyCount()
        {
            if (!liveAmbushSallyCountNeedsRecount) return;
            liveAmbushSallyCountNeedsRecount = false;
            liveAmbushSallyCount = 0;
            var all = Find.WorldObjects?.AllWorldObjects;
            if (all == null) return;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is WorldObject_Traveler traveler
                    && !traveler.Destroyed
                    && traveler.isSettlementAmbushSally)
                    liveAmbushSallyCount++;
            }
        }

        private int DogpileCooldownTicks()
        {
            var seth = WorldDominationMod.settings;
            return seth?.targetOfOpportunityDogpileCooldownTicks ?? WorldDominationSettings.DefTargetOfOpportunityDogpileCooldownTicks;
        }

        /// <summary>Feature A/B: true while <paramref name="candidate"/> is still within its anti-dogpile cooldown from a recent retarget/maraud accept.</summary>
        public bool IsUnderDogpileCooldown(WorldObject candidate)
        {
            if (candidate == null) return false;
            SweepStampsIfDue();
            if (!candidateBecameTargetTick.TryGetValue(candidate.ID, out int stampTick)) return false;
            return Find.TickManager.TicksGame - stampTick < DogpileCooldownTicks();
        }

        /// <summary>Feature A/B: stamp <paramref name="candidate"/> as having just become a target-of-opportunity/maraud destination.</summary>
        public void StampDogpile(WorldObject candidate)
        {
            if (candidate == null) return;
            candidateBecameTargetTick[candidate.ID] = Find.TickManager.TicksGame;
        }

        /// <summary>Feature C: true while <paramref name="target"/> (WD traveler or real Caravan) is still within its anti-dogpile cooldown from a recent ambush dispatch.</summary>
        public bool IsUnderAmbushTargetCooldown(WorldObject target)
        {
            if (target == null) return false;
            SweepStampsIfDue();
            if (!targetBecameAmbushTargetTick.TryGetValue(target.ID, out int stampTick)) return false;
            return Find.TickManager.TicksGame - stampTick < SettlementAmbushUtility.TargetCooldownTicks;
        }

        /// <summary>Feature C: stamp <paramref name="target"/> as having just been dispatched at by a settlement ambush.</summary>
        public void StampAmbushTarget(WorldObject target)
        {
            if (target == null) return;
            targetBecameAmbushTargetTick[target.ID] = Find.TickManager.TicksGame;
        }

        /// <summary>Bounds the two stamp dictionaries: sweep stale entries once per safety-rebuild interval rather than on every lookup.</summary>
        private void SweepStampsIfDue()
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            if (now - lastStampSweepTick < SafetyRebuildIntervalTicks) return;
            lastStampSweepTick = now;
            int cutoff = Mathf.Max(DogpileCooldownTicks(), SettlementAmbushUtility.TargetCooldownTicks) * 4;
            RemoveStaleStamps(candidateBecameTargetTick, now, cutoff);
            RemoveStaleStamps(targetBecameAmbushTargetTick, now, cutoff);
        }

        private static readonly List<int> staleKeysScratch = new List<int>();
        private static void RemoveStaleStamps(Dictionary<int, int> dict, int now, int cutoff)
        {
            staleKeysScratch.Clear();
            foreach (var kvp in dict)
                if (now - kvp.Value > cutoff) staleKeysScratch.Add(kvp.Key);
            for (int i = 0; i < staleKeysScratch.Count; i++)
                dict.Remove(staleKeysScratch[i]);
        }
    }
}
