using System;
using System.Collections;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Explored tiles (player, plus the active debug "View World as" faction) and session sight disks for tile FoW.
    /// Sight disks are per-observer and incremental: one observer change touches only that observer's tiles.
    /// Draw layers read dirty queues and bits only; no sight math on the draw path.
    /// </summary>
    public class WdFowTileMemory
    {
        private const int MaxRevealHopRadius = 64;
        private const int MaxRevealMemoEntries = 8192;
        /// <summary>Radius drift (tiles) tolerated before an observer disk is re-flooded (growth creeps per tick).</summary>
        private const float DiskRadiusEpsilon = 0.05f;

        private sealed class ObserverDisk
        {
            public int tileId = -1;
            public float awareness;
            public float los;
            public int seenStamp;
            public readonly List<int> awarenessTiles = new List<int>(256);
            public readonly List<int> losTiles = new List<int>(128);
        }

        private readonly Dictionary<int, BitArray> exploredByFaction = new Dictionary<int, BitArray>();
        /// <summary>Faction loadID → world feature uniqueIDs seen on explored tiles (for landform name FoW).</summary>
        private readonly Dictionary<int, HashSet<int>> knownFeaturesByFaction = new Dictionary<int, HashSet<int>>();

        // Session sight state for the current FoW view faction: per-tile observer counts.
        private ushort[] losCount;
        private ushort[] awarenessCount;
        private readonly Dictionary<int, ObserverDisk> disksByObserverId = new Dictionary<int, ObserverDisk>();
        private readonly Stack<ObserverDisk> diskPool = new Stack<ObserverDisk>();
        private readonly List<int> staleDiskIds = new List<int>();
        private int diskRefreshStamp;
        private int visibleFactionId = int.MinValue;
        /// <summary>Bumped when LOS disks must refresh (observer move / radius change). Not tied to discovery Version.</summary>
        private int visibleStructureEpoch;
        private int visibleCacheEpoch = -1;
        private int tilesCount;
        private bool visibleForceFullReconcile;

        /// <summary>(faction, centre tile) → largest Awareness radius already flooded into explored this session.</summary>
        private readonly Dictionary<long, float> revealMemo = new Dictionary<long, float>();

        private readonly List<int> dirtyIncognita = new List<int>(256);
        private readonly List<int> dirtyShroud = new List<int>(256);
        private readonly HashSet<int> dirtyIncognitaSet = new HashSet<int>();
        private readonly HashSet<int> dirtyShroudSet = new HashSet<int>();
        /// <summary>Coalesce trench mesh rebuild when newly explored tiles may reveal edges.</summary>
        private bool trenchOverlayDirty;

        // Flood scratch: stamped visited array + list used as a queue (no per-flood allocations).
        private int[] floodStamp;
        private int floodStampCounter;
        private readonly List<PlanetTile> floodQueue = new List<PlanetTile>(512);
        private readonly List<PlanetTile> neighborTiles = new List<PlanetTile>(8);
        private readonly List<WorldObject> visibleObserverScratch = new List<WorldObject>(64);

        /// <summary>Verbose-only timing (Stopwatch ticks / refresh count) read and reset by the daily FoW perf line.</summary>
        public long PerfRefreshTicks;
        public int PerfRefreshCount;

        /// <summary>False during POV switch blackout; true when FoW meshes are ready to show.</summary>
        public bool LayersReady { get; private set; } = true;

        public bool HasPendingMeshWork => dirtyIncognita.Count > 0 || dirtyShroud.Count > 0;

        public void ClearSession()
        {
            ResetVisibleState();
            visibleFactionId = int.MinValue;
            visibleCacheEpoch = -1;
            visibleStructureEpoch = 0;
            visibleForceFullReconcile = true;
            ClearDirtyQueues();
            LayersReady = true;
        }

        private void ResetVisibleState()
        {
            losCount = null;
            awarenessCount = null;
            foreach (var kv in disksByObserverId)
                RecycleDisk(kv.Value);
            disksByObserverId.Clear();
        }

        private void RecycleDisk(ObserverDisk disk)
        {
            if (disk == null) return;
            disk.awarenessTiles.Clear();
            disk.losTiles.Clear();
            disk.tileId = -1;
            diskPool.Push(disk);
        }

        // Instance fields: ExposeData is invoked per Scribe mode; locals filled in LoadingVars are discarded
        // before PostLoadInit, so Look no-ops would rebuild from null and wipe explored tiles on every load.
        private List<int> scribeExploredKeys;
        private List<string> scribeExploredBlobsB64;

        public void ExposeExplored()
        {
            // Byte[] cannot use LookMode.Value (saves as "System.Byte[]" and fails parse on load).
            // Persist explored bitsets as Base64 strings. Legacy label siFowExploredFactionBits is dead/corrupt.
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                scribeExploredKeys = new List<int>();
                scribeExploredBlobsB64 = new List<string>();
                // Only the player's exploration is persistent. If the player faction cannot be resolved,
                // write everything rather than risk dropping the player's map.
                Faction player = Faction.OfPlayerSilentFail;
                foreach (var kv in exploredByFaction)
                {
                    if (kv.Value == null || kv.Value.Length == 0) continue;
                    if (player != null && kv.Key != player.loadID) continue;
                    scribeExploredKeys.Add(kv.Key);
                    scribeExploredBlobsB64.Add(Convert.ToBase64String(BitArrayToBytes(kv.Value)));
                }
            }

            Scribe_Collections.Look(ref scribeExploredKeys, "siFowExploredFactionKeys", LookMode.Value);
            Scribe_Collections.Look(ref scribeExploredBlobsB64, "siFowExploredFactionBitsB64", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                ApplyLoadedExploredBits();
        }

        /// <summary>
        /// Rebuild explored bitsets from scribed Base64. Safe to call from PostLoadInit or FinalizeInit.
        /// NPC entries from older saves are loaded here and pruned later by <see cref="DropNonPlayerExplored"/>
        /// (the player faction is not reliably resolvable this early).
        /// </summary>
        public void ApplyLoadedExploredBits()
        {
            if (scribeExploredKeys == null || scribeExploredBlobsB64 == null) return;

            exploredByFaction.Clear();
            knownFeaturesByFaction.Clear();
            revealMemo.Clear();
            int n = Mathf.Min(scribeExploredKeys.Count, scribeExploredBlobsB64.Count);
            int expect = Find.WorldGrid?.TilesCount ?? 0;
            for (int i = 0; i < n; i++)
            {
                string b64 = scribeExploredBlobsB64[i];
                if (string.IsNullOrEmpty(b64) || b64 == "System.Byte[]")
                    continue;
                byte[] bytes;
                try
                {
                    bytes = Convert.FromBase64String(b64);
                }
                catch (FormatException)
                {
                    continue;
                }
                // If grid tile count is not ready yet, keep byte-derived length; EnsureTilesCount resizes later.
                int len = expect > 0 ? expect : bytes.Length * 8;
                BitArray bits = BytesToBitArray(bytes, len);
                if (bits != null)
                    exploredByFaction[scribeExploredKeys[i]] = bits;
            }
            foreach (int factionId in exploredByFaction.Keys)
                RebuildKnownFeaturesFromExplored(factionId);
        }

        /// <summary>
        /// Explored tiles are tracked for the player and for the active debug "View World as" faction only.
        /// NPC factions keep no tile history (their site / traveler knowledge is separate and unaffected).
        /// </summary>
        public static bool TracksExploredFor(Faction faction)
        {
            if (faction == null) return false;
            return faction.IsPlayer || faction == WdFowDraw.ViewFaction;
        }

        /// <summary>
        /// Drop explored memory of every faction except the player and the active debug view.
        /// No-op when the player faction cannot be resolved (never risk the player's map).
        /// </summary>
        public void DropNonPlayerExplored()
        {
            Faction player = Faction.OfPlayerSilentFail;
            if (player == null) return;
            int playerId = player.loadID;
            Faction debugView = WdFowDraw.ViewFaction;
            int keepId = debugView != null && !debugView.IsPlayer ? debugView.loadID : playerId;

            List<int> drop = null;
            foreach (var kv in exploredByFaction)
            {
                if (kv.Key == playerId || kv.Key == keepId) continue;
                drop ??= new List<int>();
                drop.Add(kv.Key);
            }
            if (drop == null) return;
            for (int i = 0; i < drop.Count; i++)
            {
                exploredByFaction.Remove(drop[i]);
                knownFeaturesByFaction.Remove(drop[i]);
            }
            revealMemo.Clear();
        }

        public void EnsureTilesCount()
        {
            int n = Find.WorldGrid?.TilesCount ?? 0;
            if (n <= 0) return;
            if (tilesCount == n) return;
            tilesCount = n;
            // Resize existing bit arrays if world size changed mid-session (rare).
            var keys = new List<int>(exploredByFaction.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                BitArray old = exploredByFaction[keys[i]];
                if (old != null && old.Length == n) continue;
                var next = new BitArray(n);
                if (old != null)
                {
                    int copy = Mathf.Min(old.Length, n);
                    for (int t = 0; t < copy; t++)
                        next[t] = old[t];
                }
                exploredByFaction[keys[i]] = next;
            }
            ResetVisibleState();
            revealMemo.Clear();
            floodStamp = null;
        }

        public BitArray GetExplored(int factionLoadId)
        {
            EnsureTilesCount();
            if (tilesCount <= 0) return null;
            if (!exploredByFaction.TryGetValue(factionLoadId, out BitArray bits) || bits == null || bits.Length != tilesCount)
            {
                bits = new BitArray(tilesCount);
                exploredByFaction[factionLoadId] = bits;
            }
            return bits;
        }

        public bool IsExplored(int factionLoadId, int tileId)
        {
            BitArray bits = GetExplored(factionLoadId);
            if (bits == null || tileId < 0 || tileId >= bits.Length) return false;
            return bits[tileId];
        }

        /// <summary>
        /// Mutator / icon gate. When FoW inactive: true (show everything).
        /// When FoW active but mesh not ready: false. Else: current LOS bit.
        /// </summary>
        public bool IsTileCurrentlyVisibleForFow(int tileId)
        {
            if (!WdFowDraw.DrawFogForCurrentView) return true;
            if (!LayersReady) return false;
            return IsTileVisibleBit(tileId);
        }

        /// <summary>
        /// Current clear-LOS bit for the FoW view faction (ignores LayersReady). Pure read:
        /// the world tick keeps the disks fresh, draw / GUI callers never trigger sight math.
        /// </summary>
        public bool IsTileVisibleBit(int tileId)
        {
            ushort[] counts = losCount;
            if (counts == null || tileId < 0 || tileId >= counts.Length) return false;
            return counts[tileId] > 0;
        }

        /// <summary>Current Awareness bit for the FoW view faction (LOS tiles are always inside Awareness). Pure read.</summary>
        public bool IsTileInAwareness(int tileId)
        {
            ushort[] counts = awarenessCount;
            if (counts == null || tileId < 0 || tileId >= counts.Length) return false;
            return counts[tileId] > 0;
        }

        /// <summary>
        /// O(1) "is this tile in current sight" for the FoW view faction. False when the disks are not built
        /// for <paramref name="faction"/> (FoW draw off, other view, tile out of range): caller falls back to an observer loop.
        /// </summary>
        public bool TryGetViewAwareness(Faction faction, int tileId, out bool inAwareness)
        {
            inAwareness = false;
            ushort[] counts = awarenessCount;
            if (faction == null || counts == null || visibleForceFullReconcile) return false;
            if (visibleFactionId != faction.loadID || !WdFowDraw.IsActive) return false;
            if (WdFowDraw.EffectiveViewFaction != faction) return false;
            if (tileId < 0 || tileId >= counts.Length) return false;
            inAwareness = counts[tileId] > 0;
            return true;
        }

        public bool IsTileExploredForView(int tileId)
        {
            Faction view = WdFowDraw.EffectiveViewFaction;
            if (view == null) return true;
            return IsExplored(view.loadID, tileId);
        }

        /// <summary>Landform / feature name visible for current FoW view (any explored tile of that feature).</summary>
        public bool IsFeatureKnownForView(WorldFeature feature)
        {
            if (feature == null || !WdFowDraw.DrawFogForCurrentView) return true;
            Faction view = WdFowDraw.EffectiveViewFaction;
            if (view == null) return true;
            if (!knownFeaturesByFaction.TryGetValue(view.loadID, out HashSet<int> set) || set == null)
                return false;
            return set.Contains(feature.uniqueID);
        }

        public void NoteFeatureOnExploredTile(int factionLoadId, int tileId)
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || tileId < 0 || tileId >= grid.TilesCount) return;
            WorldFeature feature = grid[tileId].feature;
            if (feature == null) return;
            if (!knownFeaturesByFaction.TryGetValue(factionLoadId, out HashSet<int> set) || set == null)
            {
                set = new HashSet<int>();
                knownFeaturesByFaction[factionLoadId] = set;
            }
            set.Add(feature.uniqueID);
        }

        /// <summary>Rebuild feature name knowledge from explored bits (load / first seed).</summary>
        public void RebuildKnownFeaturesFromExplored(int factionLoadId)
        {
            BitArray bits = GetExplored(factionLoadId);
            if (bits == null) return;
            if (!knownFeaturesByFaction.TryGetValue(factionLoadId, out HashSet<int> set) || set == null)
            {
                set = new HashSet<int>();
                knownFeaturesByFaction[factionLoadId] = set;
            }
            else
                set.Clear();

            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return;
            int n = Mathf.Min(bits.Length, grid.TilesCount);
            for (int i = 0; i < n; i++)
            {
                if (!bits[i]) continue;
                WorldFeature feature = grid[i].feature;
                if (feature != null)
                    set.Add(feature.uniqueID);
            }
        }

        /// <summary>Force a full disk rebuild on the next refresh (view switch only; never from hop / discovery paths).</summary>
        public void InvalidateVisibleCache()
        {
            visibleCacheEpoch = -1;
            visibleForceFullReconcile = true;
        }

        /// <summary>Observer move / MaxLOS change: refresh changed observer disks soon (not on every discovery).</summary>
        public void MarkVisibleStructureDirty()
        {
            visibleStructureEpoch++;
        }

        public void BeginViewFactionSwitch()
        {
            EnsureTilesCount();
            InvalidateVisibleCache();
            ClearDirtyQueues();
            // NPC tile history is not kept: only the player and the faction being debug-viewed hold explored bits.
            DropNonPlayerExplored();

            if (!WdFowDraw.IsActive)
            {
                LayersReady = true;
                ClearSession();
                WD_WorldLayer_FowIncognita.NotifyFowInactive();
                WD_WorldLayer_FowShroud.NotifyFowInactive();
                WD_WorldLayer_FowBlackout.SetDirty();
                return;
            }

            // Instant blackout, then sync rebuild, then show finished FoW.
            LayersReady = false;
            WD_WorldLayer_FowBlackout.EnsureRegistered();
            WD_WorldLayer_FowBlackout.SetDirty();

            if (tilesCount <= 0)
            {
                LayersReady = true;
                WD_WorldLayer_FowBlackout.SetDirty();
                return;
            }

            // Debug view of an NPC faction starts from what it sees right now (the forced refresh below
            // explores its current Awareness disks); it accumulates only while it stays the view.
            Faction view = WdFowDraw.EffectiveViewFaction;
            if (view != null && !view.IsPlayer)
            {
                exploredByFaction.Remove(view.loadID);
                knownFeaturesByFaction.Remove(view.loadID);
                revealMemo.Clear();
            }

            EnsureVisibleCacheFresh(force: true);
            ClearDirtyQueues();

            WD_WorldLayer_FowIncognita.RebuildSync();
            WD_WorldLayer_FowShroud.RebuildSync();

            LayersReady = true;
            WD_WorldLayer_FowBlackout.SetDirty();
            WD_WorldLayer_FowIncognita.SetDirty();
            WD_WorldLayer_FowShroud.SetDirty();
        }

        /// <summary>
        /// Bring the view faction's sight disks up to date. Call from world tick / gameplay paths only, never from draw.
        /// O(1) when nothing is dirty; otherwise re-floods only observers whose tile or radius changed.
        /// Returns true when any tile's sight state may have changed (icon hide/dim cache should rebuild).
        /// </summary>
        public bool EnsureVisibleCacheFresh(bool force = false)
        {
            if (!WdFowDraw.IsActive)
            {
                if (losCount != null || awarenessCount != null || disksByObserverId.Count > 0)
                    ResetVisibleState();
                return false;
            }

            EnsureTilesCount();
            Faction view = WdFowDraw.EffectiveViewFaction;
            if (view == null || tilesCount <= 0) return false;

            int viewId = view.loadID;
            bool arraysOk = losCount != null && losCount.Length == tilesCount
                && awarenessCount != null && awarenessCount.Length == tilesCount;
            bool full = force || visibleForceFullReconcile || visibleFactionId != viewId || !arraysOk;
            // Discovery Version and the icon timer never reach here: only a structure epoch bump does.
            if (!full && visibleCacheEpoch == visibleStructureEpoch)
                return false;

            var intel = WorldComponent_SettlementIntel.Get();
            if (intel == null) return false;

            bool perf = WorldDominationMod.settings != null && WorldDominationMod.settings.verboseLogging;
            long perfStart = perf ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;

            int epoch = visibleStructureEpoch;
            BitArray explored = GetExplored(viewId);
            bool changed = false;

            if (!arraysOk)
            {
                ResetVisibleState();
                losCount = new ushort[tilesCount];
                awarenessCount = new ushort[tilesCount];
                changed = true;
            }
            else if (full)
            {
                changed |= RemoveAllDisks(explored);
            }

            visibleFactionId = viewId;
            visibleForceFullReconcile = false;
            changed |= SyncObserverDisks(intel, view, explored);
            visibleCacheEpoch = epoch;

            if (changed)
                RequestIncrementalMeshUpdate();

            if (perf)
            {
                PerfRefreshTicks += System.Diagnostics.Stopwatch.GetTimestamp() - perfStart;
                PerfRefreshCount++;
            }
            return changed || full;
        }

        /// <summary>Diff current view observers against their recorded disks; re-flood only what moved or resized.</summary>
        private bool SyncObserverDisks(WorldComponent_SettlementIntel intel, Faction view, BitArray explored)
        {
            bool changed = false;
            int stamp = ++diskRefreshStamp;
            int viewId = view.loadID;
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            intel.CollectFowObserversPublic(view, manager, visibleObserverScratch);

            for (int i = 0; i < visibleObserverScratch.Count; i++)
            {
                WorldObject obs = visibleObserverScratch[i];
                if (obs == null || obs.Destroyed) continue;
                PlanetTile center = obs.Tile;
                if (!center.Valid) continue;
                float awareness = WorldComponent_SettlementIntel.GetAwarenessRadius(obs);
                if (awareness <= 0f) continue;
                // Explore out to Awareness; clear tiles only to LOS (Watch Tower uses fixed LoS, not frac).
                float los = WorldComponent_SettlementIntel.GetLosRadius(obs);

                int id = obs.ID;
                if (disksByObserverId.TryGetValue(id, out ObserverDisk disk))
                {
                    if (disk.seenStamp == stamp) continue;
                    disk.seenStamp = stamp;
                    if (disk.tileId == center.tileId
                        && Mathf.Abs(disk.awareness - awareness) < DiskRadiusEpsilon
                        && Mathf.Abs(disk.los - los) < DiskRadiusEpsilon)
                        continue;
                    RemoveDiskCounts(disk, explored);
                }
                else
                {
                    disk = diskPool.Count > 0 ? diskPool.Pop() : new ObserverDisk();
                    disk.seenStamp = stamp;
                    disksByObserverId[id] = disk;
                }

                disk.tileId = center.tileId;
                disk.awareness = awareness;
                disk.los = los;
                FloodDisk(center, awareness, los, disk, explored, viewId);
                changed = true;
            }

            // Observers that are gone (destroyed, arrived, changed faction) release their tiles.
            staleDiskIds.Clear();
            foreach (var kv in disksByObserverId)
            {
                if (kv.Value.seenStamp != stamp)
                    staleDiskIds.Add(kv.Key);
            }
            for (int i = 0; i < staleDiskIds.Count; i++)
            {
                int id = staleDiskIds[i];
                ObserverDisk disk = disksByObserverId[id];
                RemoveDiskCounts(disk, explored);
                disksByObserverId.Remove(id);
                RecycleDisk(disk);
                changed = true;
            }
            staleDiskIds.Clear();
            return changed;
        }

        private bool RemoveAllDisks(BitArray explored)
        {
            if (disksByObserverId.Count == 0) return false;
            foreach (var kv in disksByObserverId)
            {
                RemoveDiskCounts(kv.Value, explored);
                RecycleDisk(kv.Value);
            }
            disksByObserverId.Clear();
            return true;
        }

        /// <summary>Release one observer's tiles. Queues shroud only where the last clear-LOS observer left.</summary>
        private void RemoveDiskCounts(ObserverDisk disk, BitArray explored)
        {
            ushort[] los = losCount;
            ushort[] awareness = awarenessCount;
            if (los != null)
            {
                List<int> tiles = disk.losTiles;
                for (int i = 0; i < tiles.Count; i++)
                {
                    int id = tiles[i];
                    if (id < 0 || id >= los.Length || los[id] == 0) continue;
                    if (--los[id] == 0 && explored != null && id < explored.Length && explored[id])
                        QueueShroud(id);
                }
            }
            if (awareness != null)
            {
                List<int> tiles = disk.awarenessTiles;
                for (int i = 0; i < tiles.Count; i++)
                {
                    int id = tiles[i];
                    if (id < 0 || id >= awareness.Length || awareness[id] == 0) continue;
                    awareness[id]--;
                }
            }
            disk.losTiles.Clear();
            disk.awarenessTiles.Clear();
        }

        /// <summary>
        /// Reveal Awareness disk into explored for a faction (gameplay tracking). Does not clear tiles.
        /// Only the player and the active debug view keep tile memory, and a (centre, radius) already flooded
        /// this session is skipped: stationary observers only flood again when their radius grows.
        /// </summary>
        public void RevealTilesInRadius(Faction faction, PlanetTile center, float awarenessRadius)
        {
            if (faction == null || !center.Valid || awarenessRadius <= 0f) return;
            if (!TracksExploredFor(faction)) return;
            if (!WorldComponent_SettlementIntel.IsTracking()) return;
            EnsureTilesCount();
            BitArray explored = GetExplored(faction.loadID);
            if (explored == null) return;

            long key = ((long)faction.loadID << 32) | (uint)center.tileId;
            if (revealMemo.TryGetValue(key, out float done) && done >= awarenessRadius)
                return;
            if (revealMemo.Count >= MaxRevealMemoEntries)
                revealMemo.Clear();
            revealMemo[key] = awarenessRadius;

            FloodDisk(center, awarenessRadius, 0f, null, explored, faction.loadID);
            RequestIncrementalMeshUpdate();
        }

        public void RevealTilesInRadius(Faction faction, int centerTileId, float awarenessRadius)
        {
            PlanetLayer surface = Find.WorldGrid?.Surface;
            if (surface == null || centerTileId < 0) return;
            RevealTilesInRadius(faction, new PlanetTile(centerTileId, surface), awarenessRadius);
        }

        /// <summary>
        /// Flood from center: mark explored within exploreRadius. With a <paramref name="disk"/>, also count the
        /// observer into Awareness (exploreRadius) and clear LOS (clearRadius ≤ exploreRadius) and record its tiles.
        /// Disk membership is the same ApproxDistance test as before; only the bookkeeping is allocation-free.
        /// </summary>
        private void FloodDisk(
            PlanetTile center,
            float exploreRadius,
            float clearRadius,
            ObserverDisk disk,
            BitArray explored,
            int factionLoadId)
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || !center.Valid) return;
            int centerId = center.tileId;
            if (centerId < 0 || centerId >= tilesCount) return;

            if (floodStamp == null || floodStamp.Length != tilesCount)
            {
                floodStamp = new int[tilesCount];
                floodStampCounter = 0;
            }
            if (floodStampCounter == int.MaxValue)
            {
                Array.Clear(floodStamp, 0, floodStamp.Length);
                floodStampCounter = 0;
            }
            int stamp = ++floodStampCounter;

            float hopBasis = Mathf.Max(exploreRadius, clearRadius);
            int hopWindow = Mathf.Min(
                Mathf.Max(WorldMapRadiusVisual.GetHopDrawRadius(hopBasis), Mathf.CeilToInt(hopBasis) + 2),
                MaxRevealHopRadius);

            Faction view = WdFowDraw.EffectiveViewFaction;
            bool viewMatches = view != null && view.loadID == factionLoadId;
            ushort[] los = losCount;
            ushort[] awareness = awarenessCount;
            bool countDisk = disk != null && los != null && awareness != null;

            floodQueue.Clear();
            floodQueue.Add(center);
            floodStamp[centerId] = stamp;
            int head = 0;
            int hop = 0;
            int levelEnd = 1;

            while (head < floodQueue.Count)
            {
                if (head == levelEnd)
                {
                    hop++;
                    levelEnd = floodQueue.Count;
                }
                PlanetTile tile = floodQueue[head++];
                int id = tile.tileId;
                float approx = id == centerId ? 0f : grid.ApproxDistanceInTiles(center, tile);
                bool inExplore = approx <= exploreRadius;

                // Only note features on newly explored tiles (skip re-note every disk paint).
                if (explored != null && inExplore && !explored[id])
                {
                    explored[id] = true;
                    NoteFeatureOnExploredTile(factionLoadId, id);
                    if (viewMatches)
                    {
                        QueueIncognita(id);
                        QueueShroud(id);
                        trenchOverlayDirty = true;
                    }
                }

                if (countDisk)
                {
                    if (inExplore)
                    {
                        awareness[id]++;
                        disk.awarenessTiles.Add(id);
                    }
                    if (clearRadius > 0f && approx <= clearRadius)
                    {
                        disk.losTiles.Add(id);
                        // Shroud changes only where the first clear-LOS observer arrives.
                        if (los[id]++ == 0 && explored != null && explored[id])
                            QueueShroud(id);
                    }
                }

                if (hop >= hopWindow) continue;
                neighborTiles.Clear();
                grid.GetTileNeighbors(tile, neighborTiles);
                for (int i = 0; i < neighborTiles.Count; i++)
                {
                    PlanetTile n = neighborTiles[i];
                    if (!n.Valid) continue;
                    int nid = n.tileId;
                    if (nid < 0 || nid >= tilesCount || floodStamp[nid] == stamp) continue;
                    floodStamp[nid] = stamp;
                    floodQueue.Add(n);
                }
            }
        }

        public void SeedFactionFromObservers(Faction faction, List<WorldObject> observers)
        {
            if (faction == null || observers == null) return;
            for (int i = 0; i < observers.Count; i++)
            {
                WorldObject obs = observers[i];
                if (obs == null || obs.Destroyed) continue;
                float awareness = WorldComponent_SettlementIntel.GetAwarenessRadius(obs);
                if (awareness <= 0f) continue;
                RevealTilesInRadius(faction, obs.Tile, awareness);
            }
        }

        public void QueueIncognita(int tileId)
        {
            if (dirtyIncognitaSet.Add(tileId))
                dirtyIncognita.Add(tileId);
        }

        public void QueueShroud(int tileId)
        {
            if (dirtyShroudSet.Add(tileId))
                dirtyShroud.Add(tileId);
        }

        public void ClearDirtyQueues()
        {
            dirtyIncognita.Clear();
            dirtyShroud.Clear();
            dirtyIncognitaSet.Clear();
            dirtyShroudSet.Clear();
        }

        /// <summary>Drain dirty tile ids for a layer (full drain when maxCount is large).</summary>
        public bool TryDrainDirty(bool incognita, List<int> into, int maxCount = int.MaxValue)
        {
            into.Clear();
            List<int> src = incognita ? dirtyIncognita : dirtyShroud;
            HashSet<int> set = incognita ? dirtyIncognitaSet : dirtyShroudSet;
            int n = Mathf.Min(maxCount, src.Count);
            for (int i = 0; i < n; i++)
            {
                int id = src[src.Count - 1];
                src.RemoveAt(src.Count - 1);
                set.Remove(id);
                into.Add(id);
            }
            return into.Count > 0;
        }

        /// <summary>After incremental explore/visibility changes while FoW is showing, dirty both layers.</summary>
        public void RequestIncrementalMeshUpdate()
        {
            if (!WdFowDraw.IsActive || !LayersReady || !HasPendingMeshWork) return;
            WD_WorldLayer_FowIncognita.SetDirty();
            WD_WorldLayer_FowShroud.SetDirty();
            if (trenchOverlayDirty)
            {
                trenchOverlayDirty = false;
                WD_WorldLayer_Trenches.SetDirty();
            }
        }

        private static byte[] BitArrayToBytes(BitArray bits)
        {
            int byteLen = (bits.Length + 7) / 8;
            var bytes = new byte[byteLen];
            bits.CopyTo(bytes, 0);
            return bytes;
        }

        private static BitArray BytesToBitArray(byte[] bytes, int length)
        {
            if (bytes == null || length <= 0) return new BitArray(Mathf.Max(0, length));
            var bits = new BitArray(bytes);
            if (bits.Length == length) return bits;
            var sized = new BitArray(length);
            int copy = Mathf.Min(bits.Length, length);
            for (int i = 0; i < copy; i++)
                sized[i] = bits[i];
            return sized;
        }
    }
}
