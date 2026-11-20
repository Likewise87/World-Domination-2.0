using System.Collections;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Per-faction explored tiles + session current-visible bits for View World as tile FoW.
    /// Draw layers read dirty queues only; no sight math on the draw path.
    /// </summary>
    public class WdFowTileMemory
    {
        private const int MaxRevealHopRadius = 64;

        private readonly Dictionary<int, BitArray> exploredByFaction = new Dictionary<int, BitArray>();
        /// <summary>Faction loadID → world feature uniqueIDs seen on explored tiles (for landform name FoW).</summary>
        private readonly Dictionary<int, HashSet<int>> knownFeaturesByFaction = new Dictionary<int, HashSet<int>>();
        private BitArray visibleBits;
        private BitArray previousVisibleBits;
        private int visibleFactionId = int.MinValue;
        private int visibleCacheVersion = -1;
        private int visibleCacheTick = -999;
        private int tilesCount;

        private readonly List<int> dirtyIncognita = new List<int>(256);
        private readonly List<int> dirtyShroud = new List<int>(256);
        private readonly HashSet<int> dirtyIncognitaSet = new HashSet<int>();
        private readonly HashSet<int> dirtyShroudSet = new HashSet<int>();

        private readonly Queue<PlanetTile> openTiles = new Queue<PlanetTile>();
        private readonly Dictionary<int, int> distancesByTileId = new Dictionary<int, int>(256);
        private readonly List<PlanetTile> neighborTiles = new List<PlanetTile>(8);

        /// <summary>False during POV switch blackout; true when FoW meshes are ready to show.</summary>
        public bool LayersReady { get; private set; } = true;

        public bool HasPendingMeshWork => dirtyIncognita.Count > 0 || dirtyShroud.Count > 0;

        public void ClearSession()
        {
            visibleBits = null;
            previousVisibleBits = null;
            visibleFactionId = int.MinValue;
            visibleCacheVersion = -1;
            visibleCacheTick = -999;
            ClearDirtyQueues();
            LayersReady = true;
        }

        public void ExposeExplored()
        {
            List<int> keys = null;
            List<byte[]> blobs = null;
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                keys = new List<int>();
                blobs = new List<byte[]>();
                foreach (var kv in exploredByFaction)
                {
                    if (kv.Value == null || kv.Value.Length == 0) continue;
                    keys.Add(kv.Key);
                    blobs.Add(BitArrayToBytes(kv.Value));
                }
            }

            Scribe_Collections.Look(ref keys, "siFowExploredFactionKeys", LookMode.Value);
            Scribe_Collections.Look(ref blobs, "siFowExploredFactionBits", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                exploredByFaction.Clear();
                knownFeaturesByFaction.Clear();
                if (keys == null || blobs == null) return;
                int n = Mathf.Min(keys.Count, blobs.Count);
                int expect = Find.WorldGrid?.TilesCount ?? 0;
                for (int i = 0; i < n; i++)
                {
                    BitArray bits = BytesToBitArray(blobs[i], expect);
                    if (bits != null)
                        exploredByFaction[keys[i]] = bits;
                }
                // Feature name sets are derived (not scribed); rebuild after tiles exist.
                foreach (int factionId in exploredByFaction.Keys)
                    RebuildKnownFeaturesFromExplored(factionId);
            }
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
            visibleBits = null;
            previousVisibleBits = null;
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
        /// When FoW active but mesh not ready: false. Else: current MaxLOS visible bit.
        /// </summary>
        public bool IsTileCurrentlyVisibleForFow(int tileId)
        {
            if (!WdFowDraw.IsActive) return true;
            if (!LayersReady) return false;
            return IsTileVisibleBit(tileId);
        }

        /// <summary>Raw current-LOS bit for mesh raise/collapse (ignores LayersReady).</summary>
        public bool IsTileVisibleBit(int tileId)
        {
            EnsureVisibleCacheFresh();
            if (visibleBits == null || tileId < 0 || tileId >= visibleBits.Length) return false;
            return visibleBits[tileId];
        }

        public bool IsTileExploredForView(int tileId)
        {
            Faction view = WdFowDraw.ViewFaction;
            if (view == null) return true;
            return IsExplored(view.loadID, tileId);
        }

        /// <summary>Landform / feature name visible for current FoW view (any explored tile of that feature).</summary>
        public bool IsFeatureKnownForView(WorldFeature feature)
        {
            if (feature == null || !WdFowDraw.IsActive) return true;
            Faction view = WdFowDraw.ViewFaction;
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

        public void InvalidateVisibleCache()
        {
            visibleCacheVersion = -1;
            visibleCacheTick = -999;
        }

        public void BeginViewFactionSwitch()
        {
            EnsureTilesCount();
            InvalidateVisibleCache();
            ClearDirtyQueues();

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

            EnsureVisibleCacheFresh(force: true);
            ClearDirtyQueues();

            WD_WorldLayer_FowIncognita.RebuildSync();
            WD_WorldLayer_FowShroud.RebuildSync();

            LayersReady = true;
            WD_WorldLayer_FowBlackout.SetDirty();
            WD_WorldLayer_FowIncognita.SetDirty();
            WD_WorldLayer_FowShroud.SetDirty();
        }

        public void EnsureVisibleCacheFresh(bool force = false)
        {
            if (!WdFowDraw.IsActive)
            {
                visibleBits = null;
                return;
            }

            EnsureTilesCount();
            Faction view = WdFowDraw.ViewFaction;
            if (view == null || tilesCount <= 0) return;

            var intel = WorldComponent_SettlementIntel.Get();
            int version = intel?.Version ?? 0;
            int tick = Find.TickManager?.TicksGame ?? 0;
            int viewId = view.loadID;
            if (!force
                && visibleCacheVersion == version
                && visibleFactionId == viewId
                && visibleBits != null
                && visibleBits.Length == tilesCount
                && tick - visibleCacheTick >= 0
                && tick - visibleCacheTick < WorldComponent_SettlementIntel.FowCacheIntervalTicksPublic)
                return;

            if (previousVisibleBits == null || previousVisibleBits.Length != tilesCount)
                previousVisibleBits = new BitArray(tilesCount);
            else
                previousVisibleBits.SetAll(false);

            if (visibleBits != null && visibleBits.Length == tilesCount)
            {
                for (int i = 0; i < tilesCount; i++)
                    previousVisibleBits[i] = visibleBits[i];
            }

            if (visibleBits == null || visibleBits.Length != tilesCount)
                visibleBits = new BitArray(tilesCount);
            else
                visibleBits.SetAll(false);

            RebuildVisibleBits(view);
            visibleCacheVersion = version;
            visibleCacheTick = tick;
            visibleFactionId = viewId;

            // Fog dirty for visibility transitions (explored tiles only matter for shroud).
            BitArray explored = GetExplored(viewId);
            for (int i = 0; i < tilesCount; i++)
            {
                bool was = previousVisibleBits[i];
                bool now = visibleBits[i];
                if (was == now) continue;
                if (explored != null && explored[i])
                    QueueShroud(i);
            }
            RequestIncrementalMeshUpdate();
        }

        private void RebuildVisibleBits(Faction view)
        {
            var intel = WorldComponent_SettlementIntel.Get();
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (intel == null) return;

            var observers = new List<WorldObject>(64);
            intel.CollectFowObserversPublic(view, manager, observers);
            BitArray explored = GetExplored(view.loadID);

            for (int i = 0; i < observers.Count; i++)
            {
                WorldObject obs = observers[i];
                if (obs == null || obs.Destroyed) continue;
                float maxLos = WorldComponent_SettlementIntel.GetMaxLos(obs);
                if (maxLos <= 0f) continue;
                ApplyDisk(obs.Tile, maxLos, visibleBits, explored, view.loadID, queueIncognitaOnExplore: true);
            }
        }

        /// <summary>Reveal MaxLOS disk into explored for a faction (gameplay tracking).</summary>
        public void RevealTilesInRadius(Faction faction, PlanetTile center, float maxLos)
        {
            if (faction == null || !center.Valid || maxLos <= 0f) return;
            if (!WorldComponent_SettlementIntel.IsTracking()) return;
            EnsureTilesCount();
            BitArray explored = GetExplored(faction.loadID);
            if (explored == null) return;
            ApplyDisk(center, maxLos, visibleOverlay: null, explored, faction.loadID, queueIncognitaOnExplore: true);
            RequestIncrementalMeshUpdate();
        }

        public void RevealTilesInRadius(Faction faction, int centerTileId, float maxLos)
        {
            PlanetLayer surface = Find.WorldGrid?.Surface;
            if (surface == null || centerTileId < 0) return;
            RevealTilesInRadius(faction, new PlanetTile(centerTileId, surface), maxLos);
        }

        private void ApplyDisk(
            PlanetTile center,
            float maxLos,
            BitArray visibleOverlay,
            BitArray explored,
            int factionLoadId,
            bool queueIncognitaOnExplore)
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || !center.Valid) return;

            int hopWindow = Mathf.Min(
                Mathf.Max(WorldMapRadiusVisual.GetHopDrawRadius(maxLos), Mathf.CeilToInt(maxLos) + 2),
                MaxRevealHopRadius);

            openTiles.Clear();
            distancesByTileId.Clear();
            openTiles.Enqueue(center);
            distancesByTileId[center.tileId] = 0;

            Faction view = WdFowDraw.ViewFaction;
            bool viewMatches = view != null && view.loadID == factionLoadId;

            while (openTiles.Count > 0)
            {
                PlanetTile tile = openTiles.Dequeue();
                int hop = distancesByTileId[tile.tileId];
                float approx = grid.ApproxDistanceInTiles(center, tile);
                if (approx <= maxLos)
                {
                    int id = tile.tileId;
                    if (id >= 0 && id < tilesCount)
                    {
                        if (visibleOverlay != null)
                            visibleOverlay[id] = true;
                        if (explored != null && !explored[id])
                        {
                            explored[id] = true;
                            NoteFeatureOnExploredTile(factionLoadId, id);
                            if (queueIncognitaOnExplore && viewMatches)
                            {
                                QueueIncognita(id);
                                QueueShroud(id);
                            }
                        }
                        else if (explored != null && explored[id])
                        {
                            NoteFeatureOnExploredTile(factionLoadId, id);
                        }
                    }
                }

                if (hop >= hopWindow) continue;
                neighborTiles.Clear();
                grid.GetTileNeighbors(tile, neighborTiles);
                for (int i = 0; i < neighborTiles.Count; i++)
                {
                    PlanetTile n = neighborTiles[i];
                    if (!n.Valid || distancesByTileId.ContainsKey(n.tileId)) continue;
                    distancesByTileId[n.tileId] = hop + 1;
                    openTiles.Enqueue(n);
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
                float maxLos = WorldComponent_SettlementIntel.GetMaxLos(obs);
                if (maxLos <= 0f) continue;
                RevealTilesInRadius(faction, obs.Tile, maxLos);
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
