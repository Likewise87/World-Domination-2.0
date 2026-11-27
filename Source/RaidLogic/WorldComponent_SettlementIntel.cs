using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Soft FoW: per-faction discovered sites/travelers, MaxLOS x target-ring sight, GotBurned caution.
    /// Tracking (discovery/scouts) when the gate is configured; raid-logic fog when the stage passes.
    /// </summary>
    public class WorldComponent_SettlementIntel : WorldComponent
    {
        public int Version;

        private Dictionary<int, HashSet<int>> knownSites = new Dictionary<int, HashSet<int>>();
        private HashSet<int> stickyOutpostIds = new HashSet<int>();
        private Dictionary<int, HashSet<int>> knownTravelers = new Dictionary<int, HashSet<int>>();
        private Dictionary<string, int> gotBurnedUntilTick = new Dictionary<string, int>();
        /// <summary>Faction loadID → tileId → lastSeenTick for scout dest/arrival memory.</summary>
        private Dictionary<int, Dictionary<int, int>> scoutTileLastSeen = new Dictionary<int, Dictionary<int, int>>();

        private const int MaxScoutTilesPerFaction = 512;
        private const float ScoutSoftExcludeRadius = 4f;
        private const float ScoutSoftExcludeAgeDays = 20f;

        private static int nextIntelTrackId = 1;
        private bool bootstrappedThisSession;
        private WdEscalationStage lastSeenStage = WdEscalationStage.None;
        /// <summary>Suppress Action Log / verbose rows during <see cref="BootstrapAllFactions"/> (seed noise).</summary>
        private bool suppressDiscoveryLogging;

        // Settlement ID -> cached MaxLOS for the day (filled in DailyLosPass).
        private readonly Dictionary<int, float> cachedSettlementMaxLos = new Dictionary<int, float>();
        private int cachedMaxLosDay = -1;

        // Player colony / outpost ID -> MaxLOS (pawn-count driven, so short TTL instead of the day cache).
        private readonly Dictionary<int, float> liveSightRadiusCache = new Dictionary<int, float>();
        private int liveSightRadiusCacheTick = -99999;
        private const int LiveSightRadiusCacheTtlTicks = 250;

        private WorldComponent_SpreadManager spreadManagerCached;
        /// <summary>Same-world SpreadManager, resolved once (component list scan is too slow for per-hop paths).</summary>
        private WorldComponent_SpreadManager Manager =>
            spreadManagerCached ??= world?.GetComponent<WorldComponent_SpreadManager>();

        private static WorldComponent_SettlementIntel cachedInstance;

        // FoW view draw cache (hide/dim). Rebuilt on Version / view faction / short tick interval.
        private readonly HashSet<int> fowHiddenIds = new HashSet<int>();
        private readonly HashSet<int> fowDimIds = new HashSet<int>();
        /// <summary>Observers for non-view sight queries; memoized per (faction, tick, frame).</summary>
        private readonly List<WorldObject> fowObserverScratch = new List<WorldObject>();
        private int sightObserverMemoFactionId = int.MinValue;
        private int sightObserverMemoTick = -1;
        private int sightObserverMemoFrame = -1;
        /// <summary>Faction loadIDs that already know the traveler being processed by the hop pass.</summary>
        private readonly HashSet<int> travelerKnowerScratch = new HashSet<int>();

        // Hop-pass angular cull: per settlement (same order as the world list at build time) its unit
        // direction and cos(sight angle). One dot product rejects a far settlement before any lookup.
        private readonly List<Settlement> hopCullSites = new List<Settlement>(256);
        private readonly List<Vector3> hopCullDirs = new List<Vector3>(256);
        private readonly List<float> hopCullMinDot = new List<float>(256);
        private bool hopCullDirty = true;
        private int hopCullTilesCount = -1;
        /// <summary>Arc (radians) per tile of ApproxDistanceInTiles; 0 = calibration failed, cull off.</summary>
        private float hopCullRadPerTile;
        private const float HopCullNever = -2f;
        private const float HopCullAlways = 2f;

        // Daily LOS pass, drained a few observers per tick (each observer is independent and idempotent).
        private readonly List<WorldObject> dailyLosQueue = new List<WorldObject>(512);
        private int dailyLosCursor;
        private DailyWorldSnapshot dailyLosSnap;
        private const int DailyLosObserversPerTick = 16;

        // Verbose-only timing, reported once per day when the daily LOS pass finishes.
        private long perfHopTicks;
        private int perfHopCalls;
        private long perfHopSitesSeen;
        private long perfHopSitesCulled;
        private long perfIconTicks;
        private int perfIconRebuilds;
        private long perfDailyTicks;
        private int perfDailySlices;

        /// <summary>
        /// Bumped only by knowledge changes of the current FoW view faction (plus load / bootstrap):
        /// another faction's discovery cannot change what the view's icons show.
        /// </summary>
        private int iconVersion;
        private int fowCacheVersion = -1;
        private int fowCacheTick = -999;
        private int fowCacheViewFactionId = int.MinValue;
        private const int FowCacheIntervalTicks = 90;
        public const int FowCacheIntervalTicksPublic = FowCacheIntervalTicks;

        /// <summary>Per-faction explored tiles + session visible bits for View World as tile FoW.</summary>
        public readonly WdFowTileMemory TileFow = new WdFowTileMemory();
        private bool tileFowSeeded;

        // Scribe working lists (instance fields). ExposeData runs once per mode; locals from LoadingVars
        // are gone by PostLoadInit, so rebuilding from null wiped knownSites / travelers every load.
        private List<int> scribeSiteFactionKeys;
        private List<int> scribeSiteFactionValCounts;
        private List<int> scribeSiteFactionValFlat;
        private List<int> scribeTravFactionKeys;
        private List<int> scribeTravFactionValCounts;
        private List<int> scribeTravFactionValFlat;
        private List<string> scribeBurnKeys;
        private List<int> scribeBurnVals;
        private List<int> scribeScoutFactionKeys;
        private List<int> scribeScoutFactionValCounts;
        private List<int> scribeScoutTileFlat;
        private List<int> scribeScoutTickFlat;
        private bool fowDrawPrimed;
        private readonly Dictionary<int, int> caravanLastTileById = new Dictionary<int, int>();
        /// <summary>External airborne (vanilla pods / VF aerial) last painted tile; runtime only.</summary>
        private readonly Dictionary<int, int> airborneLastTileById = new Dictionary<int, int>();
        /// <summary>Last forced mid-flight FoW paint tick for external airborne (120t interval).</summary>
        private readonly Dictionary<int, int> airborneLastForcePaintTickById = new Dictionary<int, int>();
        private const int AirborneFowForcePaintIntervalTicks = 120;

        public WorldComponent_SettlementIntel(World world) : base(world) { }

        public static WorldComponent_SettlementIntel Get()
        {
            World w = Find.World;
            if (w == null) return null;
            WorldComponent_SettlementIntel c = cachedInstance;
            if (c != null && c.world == w) return c;
            c = w.GetComponent<WorldComponent_SettlementIntel>();
            cachedInstance = c;
            return c;
        }

        private static bool PerfLogging =>
            WorldDominationMod.settings != null && WorldDominationMod.settings.verboseLogging;

        private static double PerfMs(long stopwatchTicks) =>
            stopwatchTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        /// <summary>
        /// One distance per pair for intel / FoW paths: the same rounded tile distance
        /// <see cref="WorldActions_Utils.GetDistance(int, int, WorldComponent_SpreadManager)"/> returns, without
        /// writing the saved distanceCache (mobile tiles would evict the stable settlement pairs).
        /// </summary>
        internal static int TileDistance(WorldGrid grid, int tileA, int tileB)
        {
            if (tileA == tileB) return 0;
            return Mathf.RoundToInt(grid.ApproxDistanceInTiles(tileA, tileB));
        }

        /// <summary>Discover radius from an observer Awareness: full Awareness for Awareness-spottable targets, else LOS.</summary>
        private static float SightRangeFor(float observerAwareness, float losFrac, bool targetAwarenessSpottable)
        {
            if (observerAwareness <= 0f) return 0f;
            return targetAwarenessSpottable ? observerAwareness : observerAwareness * losFrac;
        }

        private HashSet<int> GetOrCreateKnownSiteSet(Faction faction)
        {
            if (!knownSites.TryGetValue(faction.loadID, out var set) || set == null)
            {
                set = new HashSet<int>();
                knownSites[faction.loadID] = set;
            }
            return set;
        }

        private HashSet<int> GetOrCreateKnownTravelerSet(Faction faction)
        {
            if (!knownTravelers.TryGetValue(faction.loadID, out var set) || set == null)
            {
                set = new HashSet<int>();
                knownTravelers[faction.loadID] = set;
            }
            return set;
        }

        /// <summary>Raid-logic fog active (kill switch on + stage gate passed).</summary>
        public static bool IsActive(WorldComponent_SpreadManager manager = null)
        {
            if (!IsGateConfigured()) return false;
            var seth = WorldDominationMod.settings;
            manager ??= Get()?.Manager;
            return WdEscalation.PassesGate(seth.gateSettlementIntel, manager);
        }

        /// <summary>Kill switch on and stage gate not Never. Off = zero FoW/tick cost, perfect info.</summary>
        public static bool IsGateConfigured()
        {
            var seth = WorldDominationMod.settings;
            return seth != null
                && seth.enableSettlementIntel
                && seth.gateSettlementIntel != WdThreatStageGate.Never;
        }

        /// <summary>Discovery, scouts, prune, bootstrap (same as gate configured).</summary>
        public static bool IsTracking() => IsGateConfigured();

        public override void FinalizeInit(bool fromLoad)
        {
            base.FinalizeInit(fromLoad);
            knownSites ??= new Dictionary<int, HashSet<int>>();
            stickyOutpostIds ??= new HashSet<int>();
            knownTravelers ??= new Dictionary<int, HashSet<int>>();
            gotBurnedUntilTick ??= new Dictionary<string, int>();
            scoutTileLastSeen ??= new Dictionary<int, Dictionary<int, int>>();
            // Re-apply FoW bits now that WorldGrid.TilesCount is reliable; resize to world size.
            if (fromLoad)
                TileFow.ApplyLoadedExploredBits();
            TileFow.EnsureTilesCount();
            if (fromLoad)
                PruneDestroyed();
            Version++;
            iconVersion++;
        }

        public override void WorldComponentTick()
        {
            if (Current.ProgramState != ProgramState.Playing) return;
            WdFowDraw.ClearIfGateOff();
            if (!IsTracking()) return;

            var manager = Manager;
            var stage = WdEscalation.GetCachedStage(manager);
            if (!bootstrappedThisSession || stage != lastSeenStage)
            {
                BootstrapAllFactions();
                bootstrappedThisSession = true;
                lastSeenStage = stage;
            }

            if (!tileFowSeeded && bootstrappedThisSession)
            {
                SeedAllFactionTileExploration();
                tileFowSeeded = true;
            }

            if (Find.TickManager.TicksGame % 60000 == 250)
                PruneDestroyed();

            ProcessDailyLosQueue(DailyLosObserversPerTick);

            if (WdFowDraw.IsActive)
            {
                if (!fowDrawPrimed)
                {
                    fowDrawPrimed = true;
                    WD_WorldLayer_FowIncognita.EnsureRegistered();
                    WD_WorldLayer_FowShroud.EnsureRegistered();
                    WD_WorldLayer_FowBlackout.EnsureRegistered();
                    NotifyTileFowViewChanged();
                }
                // Periodic observer diff (cheap: only moved / resized / vanished observers re-flood).
                // Catches own units that left the world or changed pawn count without a notify.
                if (Find.TickManager.TicksGame % FowCacheIntervalTicks == 0)
                    TileFow.MarkVisibleStructureDirty();
                // Sight disks first: the icon hide/dim rebuild reads their tile bits.
                if (TileFow.EnsureVisibleCacheFresh())
                    InvalidateFowDrawCache();
                EnsureFowDrawCacheFresh();
                if (Find.TickManager.TicksGame % 60 == 0)
                    TickMobileCaravanAndAirborneFowReveal();
            }
            else
            {
                fowDrawPrimed = false;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref stickyOutpostIds, "siStickyOutposts", LookMode.Value);
            Scribe_Values.Look(ref nextIntelTrackId, "siNextTrackId", 1);

            if (Scribe.mode == LoadSaveMode.Saving)
            {
                FlattenIntHashSetDict(knownSites, out scribeSiteFactionKeys, out scribeSiteFactionValCounts, out scribeSiteFactionValFlat);
                FlattenIntHashSetDict(knownTravelers, out scribeTravFactionKeys, out scribeTravFactionValCounts, out scribeTravFactionValFlat);
                FlattenScoutTileDict(scoutTileLastSeen, out scribeScoutFactionKeys, out scribeScoutFactionValCounts, out scribeScoutTileFlat, out scribeScoutTickFlat);
                scribeBurnKeys = new List<string>(gotBurnedUntilTick.Keys);
                scribeBurnVals = new List<int>();
                for (int i = 0; i < scribeBurnKeys.Count; i++)
                    scribeBurnVals.Add(gotBurnedUntilTick[scribeBurnKeys[i]]);
            }

            Scribe_Collections.Look(ref scribeSiteFactionKeys, "siSiteFactionKeys", LookMode.Value);
            Scribe_Collections.Look(ref scribeSiteFactionValCounts, "siSiteFactionValCounts", LookMode.Value);
            Scribe_Collections.Look(ref scribeSiteFactionValFlat, "siSiteFactionValFlat", LookMode.Value);
            Scribe_Collections.Look(ref scribeTravFactionKeys, "siTravFactionKeys", LookMode.Value);
            Scribe_Collections.Look(ref scribeTravFactionValCounts, "siTravFactionValCounts", LookMode.Value);
            Scribe_Collections.Look(ref scribeTravFactionValFlat, "siTravFactionValFlat", LookMode.Value);
            Scribe_Collections.Look(ref scribeBurnKeys, "siBurnKeys", LookMode.Value);
            Scribe_Collections.Look(ref scribeBurnVals, "siBurnVals", LookMode.Value);
            Scribe_Collections.Look(ref scribeScoutFactionKeys, "siScoutFactionKeys", LookMode.Value);
            Scribe_Collections.Look(ref scribeScoutFactionValCounts, "siScoutFactionValCounts", LookMode.Value);
            Scribe_Collections.Look(ref scribeScoutTileFlat, "siScoutTileFlat", LookMode.Value);
            Scribe_Collections.Look(ref scribeScoutTickFlat, "siScoutTickFlat", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                knownSites = RebuildIntHashSetDict(scribeSiteFactionKeys, scribeSiteFactionValCounts, scribeSiteFactionValFlat);
                knownTravelers = RebuildIntHashSetDict(scribeTravFactionKeys, scribeTravFactionValCounts, scribeTravFactionValFlat);
                scoutTileLastSeen = RebuildScoutTileDict(scribeScoutFactionKeys, scribeScoutFactionValCounts, scribeScoutTileFlat, scribeScoutTickFlat);
                gotBurnedUntilTick = new Dictionary<string, int>();
                if (scribeBurnKeys != null && scribeBurnVals != null)
                {
                    for (int i = 0; i < scribeBurnKeys.Count && i < scribeBurnVals.Count; i++)
                        gotBurnedUntilTick[scribeBurnKeys[i]] = scribeBurnVals[i];
                }
                stickyOutpostIds ??= new HashSet<int>();
                tileFowSeeded = false;
            }

            TileFow.ExposeExplored();
        }

        private static void FlattenIntHashSetDict(
            Dictionary<int, HashSet<int>> dict,
            out List<int> keys,
            out List<int> counts,
            out List<int> flat)
        {
            keys = new List<int>();
            counts = new List<int>();
            flat = new List<int>();
            if (dict == null) return;
            foreach (var kv in dict)
            {
                keys.Add(kv.Key);
                HashSet<int> set = kv.Value;
                if (set == null || set.Count == 0)
                {
                    counts.Add(0);
                    continue;
                }
                counts.Add(set.Count);
                foreach (int id in set)
                    flat.Add(id);
            }
        }

        private static Dictionary<int, HashSet<int>> RebuildIntHashSetDict(
            List<int> keys,
            List<int> counts,
            List<int> flat)
        {
            var dict = new Dictionary<int, HashSet<int>>();
            if (keys == null || counts == null) return dict;
            int flatIdx = 0;
            for (int i = 0; i < keys.Count && i < counts.Count; i++)
            {
                int n = Mathf.Max(0, counts[i]);
                var set = new HashSet<int>();
                for (int j = 0; j < n; j++)
                {
                    if (flat == null || flatIdx >= flat.Count) break;
                    set.Add(flat[flatIdx++]);
                }
                dict[keys[i]] = set;
            }
            return dict;
        }

        public static int AllocateIntelTrackId()
        {
            if (nextIntelTrackId < 1) nextIntelTrackId = 1;
            return nextIntelTrackId++;
        }

        public void EnsureTravelerTrackId(WorldObject_Traveler t)
        {
            if (t == null) return;
            if (t.intelTrackId <= 0)
                t.intelTrackId = AllocateIntelTrackId();
        }

        public bool Knows(Faction faction, WorldObject target)
        {
            if (faction == null || target == null || target.Destroyed) return false;
            if (!IsActive()) return true;
            if (IsAlwaysKnown(target)) return true;
            if (target.Faction == faction) return true;
            return IsDiscovered(faction, target);
        }

        public bool KnowsTraveler(Faction faction, WorldObject_Traveler traveler)
        {
            if (faction == null || traveler == null || traveler.Destroyed) return false;
            if (!IsActive()) return true;
            if (traveler.Faction == faction) return true;
            return IsTravelerDiscovered(faction, traveler);
        }

        public bool IsDiscovered(Faction faction, WorldObject target)
        {
            if (faction == null || target == null || target.Destroyed) return false;
            if (IsAlwaysKnown(target)) return true;
            if (target.Faction == faction) return true;
            if (!knownSites.TryGetValue(faction.loadID, out var set) || set == null) return false;
            return set.Contains(target.ID);
        }

        public bool IsTravelerDiscovered(Faction faction, WorldObject_Traveler traveler)
        {
            if (faction == null || traveler == null || traveler.Destroyed) return false;
            if (traveler.Faction == faction) return true;
            EnsureTravelerTrackId(traveler);
            if (!knownTravelers.TryGetValue(faction.loadID, out var set) || set == null) return false;
            return set.Contains(traveler.intelTrackId);
        }

        public void Discover(Faction faction, WorldObject target, WorldObject observer = null)
        {
            if (faction == null || target == null || target.Destroyed) return;
            if (IsAlwaysKnown(target)) return;
            if (!knownSites.TryGetValue(faction.loadID, out var set))
            {
                set = new HashSet<int>();
                knownSites[faction.loadID] = set;
            }
            if (set.Add(target.ID))
            {
                if (target is WorldObject_WD_Outpost)
                    stickyOutpostIds.Add(target.ID);
                Version++;
                BumpIconVersionIfView(faction);
                LogDiscovery(faction, target, observer);
            }
        }

        public void DiscoverTraveler(Faction faction, WorldObject_Traveler traveler, WorldObject observer = null)
        {
            if (faction == null || traveler == null || traveler.Destroyed) return;
            EnsureTravelerTrackId(traveler);
            if (!knownTravelers.TryGetValue(faction.loadID, out var set))
            {
                set = new HashSet<int>();
                knownTravelers[faction.loadID] = set;
            }
            if (set.Add(traveler.intelTrackId))
            {
                Version++;
                BumpIconVersionIfView(faction);
                LogDiscovery(faction, traveler, observer);
            }
        }

        /// <summary>Icon hide/dim only depends on what the FoW view faction knows.</summary>
        private void BumpIconVersionIfView(Faction faction)
        {
            if (faction == WdFowDraw.EffectiveViewFaction)
                iconVersion++;
        }

        private void LogDiscovery(Faction faction, WorldObject target, WorldObject observer)
        {
            if (suppressDiscoveryLogging || !IsTracking()) return;
            if (target == null || target.Destroyed || faction == null) return;
            if (target.Faction == faction) return;

            WorldObject actor = observer != null && !observer.Destroyed ? observer : null;
            string actorName = actor != null
                ? WdPlayerIntel.LabelForLetter(actor)
                : (faction.Name ?? "---");
            string targetName = WdPlayerIntel.LabelForLetter(target);
            string msg = "TSA_WD_Log_Intel_Discovered".Translate(actorName, targetName).ToString();

            // Player Action Log only for player discoveries — NPC mutual Discover must not dump foreign names.
            if (faction.IsPlayer)
            {
                var manager = Manager;
                if (manager != null)
                {
                    SpreadLogEntry entry = actor != null
                        ? new SpreadLogEntry(msg, actor, target)
                        : new SpreadLogEntry(msg, null, target);
                    if (actor == null)
                        entry.labelA = faction.Name ?? "---";
                    entry.category = SpreadLogCategory.Intel;
                    manager.AddLog(entry);
                }
            }

            WDVerbose.Msg(msg);
        }

        public void ClearSiteKnowledge(WorldObject target)
        {
            if (target == null) return;
            int id = target.ID;
            foreach (var kv in knownSites)
                kv.Value?.Remove(id);
            stickyOutpostIds.Remove(id);
            Version++;
            iconVersion++;
        }

        public void Forget(Faction faction, WorldObject target)
        {
            if (faction == null || target == null) return;
            if (!knownSites.TryGetValue(faction.loadID, out var set) || set == null) return;
            if (!set.Remove(target.ID)) return;
            if (target is WorldObject_WD_Outpost && !AnyFactionKnowsSite(target.ID))
                stickyOutpostIds.Remove(target.ID);
            Version++;
            BumpIconVersionIfView(faction);
        }

        private bool AnyFactionKnowsSite(int siteId)
        {
            foreach (var kv in knownSites)
            {
                if (kv.Value != null && kv.Value.Contains(siteId))
                    return true;
            }
            return false;
        }

        public void ClearFactionIntel(Faction faction)
        {
            if (faction == null) return;
            knownSites.Remove(faction.loadID);
            knownTravelers.Remove(faction.loadID);
            scoutTileLastSeen?.Remove(faction.loadID);
            string prefix = faction.loadID + ":";
            var remove = new List<string>();
            foreach (var k in gotBurnedUntilTick.Keys)
                if (k.StartsWith(prefix)) remove.Add(k);
            for (int i = 0; i < remove.Count; i++)
                gotBurnedUntilTick.Remove(remove[i]);
            Version++;
            iconVersion++;
        }

        /// <summary>Stamp a scout destination/arrival tile for faction-shared revisit memory.</summary>
        public void RecordScoutTileSeen(Faction faction, int tileId)
        {
            if (faction == null || tileId < 0) return;
            scoutTileLastSeen ??= new Dictionary<int, Dictionary<int, int>>();
            if (!scoutTileLastSeen.TryGetValue(faction.loadID, out var map) || map == null)
            {
                map = new Dictionary<int, int>();
                scoutTileLastSeen[faction.loadID] = map;
            }
            map[tileId] = Find.TickManager.TicksGame;
            if (map.Count > MaxScoutTilesPerFaction)
                TrimOldestScoutTiles(map, MaxScoutTilesPerFaction / 2);
        }

        /// <summary>Ticks since last scout stamp, or int.MaxValue if never.</summary>
        public int GetScoutTileAgeTicks(Faction faction, int tileId)
        {
            if (faction == null || tileId < 0) return int.MaxValue;
            if (scoutTileLastSeen == null || !scoutTileLastSeen.TryGetValue(faction.loadID, out var map) || map == null)
                return int.MaxValue;
            if (!map.TryGetValue(tileId, out int tick))
                return int.MaxValue;
            return Mathf.Max(0, Find.TickManager.TicksGame - tick);
        }

        /// <summary>True when tile is within soft radius of a recently stamped scout dest.</summary>
        public bool IsNearRecentScoutStamp(Faction faction, int tileId, float softRadius = -1f, float maxAgeDays = -1f)
        {
            if (faction == null || tileId < 0) return false;
            if (scoutTileLastSeen == null || !scoutTileLastSeen.TryGetValue(faction.loadID, out var map) || map == null || map.Count == 0)
                return false;
            if (softRadius < 0f) softRadius = ScoutSoftExcludeRadius;
            if (maxAgeDays < 0f) maxAgeDays = ScoutSoftExcludeAgeDays;
            int maxAgeTicks = Mathf.RoundToInt(maxAgeDays * 60000f);
            int now = Find.TickManager.TicksGame;
            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return false;

            foreach (var kv in map)
            {
                int age = now - kv.Value;
                if (age < 0 || age > maxAgeTicks) continue;
                float d = grid.ApproxDistanceInTiles(tileId, kv.Key);
                if (d <= softRadius)
                    return true;
            }
            return false;
        }

        private static void TrimOldestScoutTiles(Dictionary<int, int> map, int keep)
        {
            if (map == null || map.Count <= keep) return;
            var entries = new List<KeyValuePair<int, int>>(map.Count);
            foreach (var kv in map)
                entries.Add(kv);
            entries.Sort((a, b) => a.Value.CompareTo(b.Value));
            int drop = map.Count - keep;
            for (int i = 0; i < drop && i < entries.Count; i++)
                map.Remove(entries[i].Key);
        }

        private static void FlattenScoutTileDict(
            Dictionary<int, Dictionary<int, int>> dict,
            out List<int> keys,
            out List<int> counts,
            out List<int> tiles,
            out List<int> ticks)
        {
            keys = new List<int>();
            counts = new List<int>();
            tiles = new List<int>();
            ticks = new List<int>();
            if (dict == null) return;
            foreach (var kv in dict)
            {
                keys.Add(kv.Key);
                Dictionary<int, int> map = kv.Value;
                if (map == null || map.Count == 0)
                {
                    counts.Add(0);
                    continue;
                }
                counts.Add(map.Count);
                foreach (var e in map)
                {
                    tiles.Add(e.Key);
                    ticks.Add(e.Value);
                }
            }
        }

        private static Dictionary<int, Dictionary<int, int>> RebuildScoutTileDict(
            List<int> keys,
            List<int> counts,
            List<int> tiles,
            List<int> ticks)
        {
            var dict = new Dictionary<int, Dictionary<int, int>>();
            if (keys == null || counts == null) return dict;
            int idx = 0;
            for (int i = 0; i < keys.Count && i < counts.Count; i++)
            {
                int n = Mathf.Max(0, counts[i]);
                var map = new Dictionary<int, int>();
                for (int j = 0; j < n; j++)
                {
                    if (tiles == null || ticks == null || idx >= tiles.Count || idx >= ticks.Count) break;
                    map[tiles[idx]] = ticks[idx];
                    idx++;
                }
                dict[keys[i]] = map;
            }
            return dict;
        }

        public void StampGotBurned(Faction attacker, WorldObject target)
        {
            if (attacker == null || target == null) return;
            var seth = WorldDominationMod.settings;
            int days = Mathf.Max(1, Mathf.RoundToInt(seth?.settlementIntelGotBurnedDays ?? WorldDominationSettings.DefSettlementIntelGotBurnedDays));
            int until = Find.TickManager.TicksGame + days * 60000;
            gotBurnedUntilTick[BurnKey(attacker, target)] = until;
            Version++;
        }

        public bool IsGotBurned(Faction attacker, WorldObject target, out int daysLeft)
        {
            daysLeft = 0;
            if (attacker == null || target == null) return false;
            if (!gotBurnedUntilTick.TryGetValue(BurnKey(attacker, target), out int until)) return false;
            int now = Find.TickManager.TicksGame;
            if (now >= until)
            {
                gotBurnedUntilTick.Remove(BurnKey(attacker, target));
                return false;
            }
            daysLeft = Mathf.CeilToInt((until - now) / 60000f);
            return true;
        }

        public void ClearGotBurned(Faction attacker, WorldObject target)
        {
            if (attacker == null || target == null) return;
            gotBurnedUntilTick.Remove(BurnKey(attacker, target));
            Version++;
        }

        public float ApplyGotBurnedInflate(Faction attacker, WorldObject target, float knownDefTotal)
        {
            if (!IsGotBurned(attacker, target, out _)) return knownDefTotal;
            var seth = WorldDominationMod.settings;
            float pct = seth?.settlementIntelBurnedHiddenStrengthPct
                ?? WorldDominationSettings.DefSettlementIntelBurnedHiddenStrengthPct;
            return knownDefTotal * (1f + Mathf.Max(0f, pct));
        }

        public static bool IsAlwaysKnown(WorldObject target)
        {
            if (target is Settlement s && s.Faction?.IsPlayer == true && s.HasMap)
            {
                var seth = WorldDominationMod.settings;
                return seth == null || seth.intelPlayerColoniesAlwaysKnown;
            }
            return false;
        }

        /// <summary>
        /// Public for mortar / combat gates: target inside the current Awareness disk of any observer of <paramref name="faction"/>.
        /// FoW view faction: O(1) tile-bit read. Other factions: observer loop with the same ApproxDistance test,
        /// observers collected once per (faction, tick).
        /// </summary>
        public bool IsInCurrentSightOfFaction(Faction faction, WorldObject target)
        {
            if (faction == null || target == null || target.Destroyed) return false;
            if (TileFow.TryGetViewAwareness(faction, target.Tile.tileId, out bool inAwareness))
                return inAwareness;

            int tick = Find.TickManager?.TicksGame ?? 0;
            int frame = Time.frameCount;
            if (sightObserverMemoFactionId != faction.loadID
                || sightObserverMemoTick != tick
                || sightObserverMemoFrame != frame)
            {
                CollectFowObservers(faction, Manager, fowObserverScratch);
                sightObserverMemoFactionId = faction.loadID;
                sightObserverMemoTick = tick;
                sightObserverMemoFrame = frame;
            }
            return IsInCurrentSightOfAnyObserver(target, fowObserverScratch);
        }

        // --- Faction FoW view (View intel as) ---

        /// <summary>
        /// Dirties icon hide/dim only. Does <b>not</b> force a full-planet visible-disk rebuild —
        /// use <see cref="WdFowTileMemory.MarkVisibleStructureDirty"/> / view switch for that.
        /// </summary>
        public void InvalidateFowDrawCache()
        {
            fowCacheVersion = -1;
            fowCacheTick = -999;
            fowCacheViewFactionId = int.MinValue;
        }

        /// <summary>O(1) tile LOS query for MutatorWorldIcons / draw gates.</summary>
        public bool IsTileCurrentlyVisibleForFow(int tileId) =>
            TileFow.IsTileCurrentlyVisibleForFow(tileId);

        /// <summary>Begin progressive tile FoW mesh rebuild for the current View World as faction.</summary>
        public void NotifyTileFowViewChanged()
        {
            TileFow.BeginViewFactionSwitch();
            // Trench mesh filters by explored tiles; rebuild when FoW view / gate changes.
            WD_WorldLayer_Trenches.SetDirty();
        }

        public void CollectFowObserversPublic(Faction view, WorldComponent_SpreadManager manager, List<WorldObject> observers) =>
            CollectFowObservers(view, manager, observers);

        /// <summary>
        /// Raw FoW overlay visibility (never uses raid Knows short-circuit).
        /// Travelers: need discovery; by default also current sight (else hidden).
        /// With <c>intelSpottedCaravansRemainVisible</c>, discovery alone is enough (dim out of sight).
        /// <see cref="knownTravelers"/> still drives KnowsTraveler / letters / Active Travelers.
        /// </summary>
        public bool CanViewForFow(Faction view, WorldObject wo)
        {
            if (view == null || wo == null || wo.Destroyed) return false;
            if (!WdFowDraw.IsFowTarget(wo)) return true;
            if (wo.Faction == view) return true;
            if (wo is WorldObject_Traveler traveler)
            {
                if (!IsTravelerDiscovered(view, traveler)) return false;
                var seth = WorldDominationMod.settings;
                if (seth != null && seth.intelSpottedCaravansRemainVisible)
                    return true;
                return IsInCurrentSightOfFaction(view, traveler);
            }
            if (IsAlwaysKnown(wo)) return true;
            return IsDiscovered(view, wo);
        }

        public bool IsFowHidden(WorldObject wo)
        {
            // Draw/GUI path: cache read only (rebuild happens on world tick).
            if (!WdFowDraw.DrawFogForCurrentView || wo == null) return false;
            return fowHiddenIds.Contains(wo.ID);
        }

        public bool IsFowDimmed(WorldObject wo)
        {
            if (!WdFowDraw.DrawFogForCurrentView || wo == null) return false;
            return fowDimIds.Contains(wo.ID);
        }

        /// <summary>Call from world tick only. Never from draw/GUI.</summary>
        public void EnsureFowDrawCacheFresh()
        {
            WdFowDraw.ClearIfGateOff();
            // Hide/dim cache is for current-view draw. Explore still uses IsActive.
            if (!WdFowDraw.DrawFogForCurrentView)
            {
                if (fowHiddenIds.Count > 0 || fowDimIds.Count > 0)
                {
                    fowHiddenIds.Clear();
                    fowDimIds.Clear();
                }
                return;
            }

            Faction view = WdFowDraw.EffectiveViewFaction;
            int tick = Find.TickManager?.TicksGame ?? 0;
            int viewId = view?.loadID ?? int.MinValue;
            if (fowCacheVersion == iconVersion
                && fowCacheViewFactionId == viewId
                && tick - fowCacheTick >= 0
                && tick - fowCacheTick < FowCacheIntervalTicks)
                return;

            fowCacheVersion = iconVersion;
            fowCacheTick = tick;
            fowCacheViewFactionId = viewId;
            RebuildFowDrawCache(view);
        }

        /// <summary>
        /// Icon hide/dim for the view faction. O(targets): each target reads the view's tile sight bits
        /// (kept fresh by <see cref="WdFowTileMemory.EnsureVisibleCacheFresh"/>); no per-target observer loops.
        /// </summary>
        private void RebuildFowDrawCache(Faction view)
        {
            fowHiddenIds.Clear();
            fowDimIds.Clear();
            if (view == null) return;

            bool perf = PerfLogging;
            long perfStart = perf ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;

            var worldObjects = Find.WorldObjects;
            if (worldObjects?.Settlements != null)
            {
                List<Settlement> settlements = worldObjects.Settlements;
                for (int i = 0; i < settlements.Count; i++)
                    ClassifyFowTarget(view, settlements[i]);
            }

            IReadOnlyList<WorldObject_WD_Outpost> outposts = WdPlayerOutpostCache.PlayerOutposts;
            for (int i = 0; i < outposts.Count; i++)
                ClassifyFowTarget(view, outposts[i]);

            IReadOnlyList<WorldObject_AT_Turret> ats = WorldObject_AT_Turret.LiveTurrets;
            if (ats != null)
            {
                for (int i = 0; i < ats.Count; i++)
                    ClassifyFowTarget(view, ats[i]);
            }

            IReadOnlyList<WorldObject_WD_PlayerWatchTower> towers = WorldObject_WD_PlayerWatchTower.LiveTowers;
            for (int i = 0; i < towers.Count; i++)
                ClassifyFowTarget(view, towers[i]);

            var adv = WorldComponent_AdvancedBuildings.Get();
            IReadOnlyList<WorldObject_WD_AdvancedBuilding> buildings = adv?.All;
            if (buildings != null)
            {
                for (int i = 0; i < buildings.Count; i++)
                    ClassifyFowTarget(view, buildings[i]);
            }

            IReadOnlyList<WorldObject_Traveler> travelers = WorldObject_Traveler.LiveTravelers;
            for (int i = 0; i < travelers.Count; i++)
                ClassifyFowTarget(view, travelers[i]);

            IReadOnlyList<WorldObject_WdSettlementRuin> ruins = WorldObject_WdSettlementRuin.LiveRuins;
            for (int i = 0; i < ruins.Count; i++)
                ClassifyFowTarget(view, ruins[i]);

            if (perf)
            {
                perfIconTicks += System.Diagnostics.Stopwatch.GetTimestamp() - perfStart;
                perfIconRebuilds++;
            }
        }

        private static void CollectFowObservers(Faction view, WorldComponent_SpreadManager manager, List<WorldObject> observers)
        {
            observers.Clear();
            if (view == null) return;
            var snap = manager?.CurrentDailySnapshot;
            if (snap?.SettlementsByFaction != null
                && snap.SettlementsByFaction.TryGetValue(view, out List<Settlement> own)
                && own != null)
            {
                for (int i = 0; i < own.Count; i++)
                {
                    Settlement s = own[i];
                    if (s != null && !s.Destroyed)
                        observers.Add(s);
                }
            }

            // The player colony (singular) is not in SettlementsByFaction (IsWdParticipant excludes player).
            if (view.IsPlayer)
            {
                Settlement colony = InfluenceUtils.GetPlayerColony();
                if (colony != null && !colony.Destroyed && colony.HasMap && colony.Faction == view)
                    observers.Add(colony);

                IReadOnlyList<WorldObject_WD_Outpost> outposts = WdPlayerOutpostCache.PlayerOutposts;
                for (int i = 0; i < outposts.Count; i++)
                {
                    WorldObject_WD_Outpost o = outposts[i];
                    if (o != null && !o.Destroyed)
                        observers.Add(o);
                }

                List<Caravan> caravans = Find.WorldObjects?.Caravans;
                if (caravans != null)
                {
                    for (int i = 0; i < caravans.Count; i++)
                    {
                        Caravan c = caravans[i];
                        if (c == null || c.Destroyed || c.Faction != view) continue;
                        observers.Add(c);
                    }
                }
            }

            IReadOnlyList<WorldObject_Traveler> travelers = WorldObject_Traveler.LiveTravelers;
            for (int i = 0; i < travelers.Count; i++)
            {
                WorldObject_Traveler t = travelers[i];
                if (t == null || t.Destroyed || t.Faction != view) continue;
                if (WorldObject_Traveler.IsShellMission(t.mission)) continue;
                observers.Add(t);
            }

            // AT turrets (fire-range observers) + Watch Towers (fixed 10). Other adv buildings are targets only.
            IReadOnlyList<WorldObject_AT_Turret> ats = WorldObject_AT_Turret.LiveTurrets;
            for (int i = 0; i < ats.Count; i++)
            {
                WorldObject_AT_Turret at = ats[i];
                if (at == null || at.Destroyed || at.Faction != view) continue;
                observers.Add(at);
            }

            IReadOnlyList<WorldObject_WD_PlayerWatchTower> playerTowers = WorldObject_WD_PlayerWatchTower.LiveTowers;
            for (int i = 0; i < playerTowers.Count; i++)
            {
                WorldObject_WD_PlayerWatchTower wt = playerTowers[i];
                if (wt == null || wt.Destroyed || wt.Faction != view) continue;
                observers.Add(wt);
            }

            var advReg = WorldComponent_AdvancedBuildings.Get();
            if (advReg != null)
            {
                IReadOnlyList<WorldObject_WD_AdvancedBuilding> buildings = advReg.All;
                for (int i = 0; i < buildings.Count; i++)
                {
                    WorldObject_WD_AdvancedBuilding b = buildings[i];
                    if (b == null || b.Destroyed || b.Faction != view) continue;
                    if (!IsWatchTowerObserver(b)) continue;
                    observers.Add(b);
                }
            }

            // Vanilla pods / Odyssey shuttle flight / VF AerialVehicleInFlight (AA registry).
            IReadOnlyCollection<WorldObject> airborne =
                WorldComponent_InterceptionScheduler.Current?.ExternalAirborneTargets;
            if (airborne == null || airborne.Count == 0) return;
            foreach (WorldObject wo in airborne)
            {
                if (wo == null || wo.Destroyed || wo.Faction != view) continue;
                if (!IsAirborneFowObserver(wo)) continue;
                observers.Add(wo);
            }
        }

        private void ClassifyFowTarget(Faction view, WorldObject wo)
        {
            if (wo == null || wo.Destroyed || !WdFowDraw.IsFowTarget(wo)) return;

            // Bright / dim come from the same tile bits the fog is drawn from (LOS = clear, Awareness = shroud).
            int tileId = wo.Tile.tileId;

            // Travelers: bright in LOS; dim in Awareness; optional remain-visible → dim outside Awareness.
            if (wo is WorldObject_Traveler traveler)
            {
                if (wo.Faction == view)
                    return;
                if (!IsTravelerDiscovered(view, traveler))
                {
                    fowHiddenIds.Add(wo.ID);
                    return;
                }
                if (TileFow.IsTileVisibleBit(tileId))
                    return;
                if (TileFow.IsTileInAwareness(tileId))
                {
                    fowDimIds.Add(wo.ID);
                    return;
                }
                var seth = WorldDominationMod.settings;
                if (seth != null && seth.intelSpottedCaravansRemainVisible)
                    fowDimIds.Add(wo.ID);
                else
                    fowHiddenIds.Add(wo.ID);
                return;
            }

            if (!CanViewForFow(view, wo))
            {
                fowHiddenIds.Add(wo.ID);
                return;
            }

            // Own holdings and always-known stay bright.
            if (wo.Faction == view || IsAlwaysKnown(wo))
                return;

            // Known sites: bright in LOS; dim in Awareness or as memory outside Awareness.
            if (!TileFow.IsTileVisibleBit(tileId))
                fowDimIds.Add(wo.ID);
        }

        /// <summary>
        /// Observer-loop sight for factions without tile bits (non-view factions, FoW draw off).
        /// Same membership test as the view's Awareness disk (ApproxDistance ≤ Awareness), so both paths agree.
        /// </summary>
        private static bool IsInCurrentSightOfAnyObserver(WorldObject target, List<WorldObject> observers)
        {
            if (target == null || observers == null || observers.Count == 0) return false;
            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return false;
            int targetTile = target.Tile.tileId;
            for (int i = 0; i < observers.Count; i++)
            {
                WorldObject obs = observers[i];
                if (obs == null || obs.Destroyed) continue;
                float range = GetAwarenessRadius(obs);
                if (range <= 0f) continue;
                int obsTile = obs.Tile.tileId;
                if (obsTile == targetTile) return true;
                if (grid.ApproxDistanceInTiles(obsTile, targetTile) <= range) return true;
            }
            return false;
        }

        // --- Sight model ---

        public static SettlementTier GetSpotClassFromStrength(float initialStrength)
        {
            var seth = WorldDominationMod.settings;
            float t4 = seth?.intelTravelerStrengthT4 ?? WorldDominationSettings.DefIntelTravelerStrengthT4;
            float t3 = seth?.intelTravelerStrengthT3 ?? WorldDominationSettings.DefIntelTravelerStrengthT3;
            float t2 = seth?.intelTravelerStrengthT2 ?? WorldDominationSettings.DefIntelTravelerStrengthT2;
            if (initialStrength >= t4) return SettlementTier.T4;
            if (initialStrength >= t3) return SettlementTier.T3;
            if (initialStrength >= t2) return SettlementTier.T2;
            return SettlementTier.T1;
        }

        public static SettlementTier GetSpotClass(WorldObject_Traveler traveler)
        {
            if (traveler == null) return SettlementTier.T1;
            return GetSpotClassFromStrength(traveler.initialStrength);
        }

        public static SettlementTier GetSightTier(WorldObject target)
        {
            if (target is WorldObject_Traveler t)
                return GetSpotClass(t);
            if (target is WorldObject_AT_Turret
                || target is WorldObject_WD_AdvancedBuilding
                || target is WorldObject_WD_PlayerWatchTower)
                return SettlementTier.T1;
            if (target is Settlement s)
            {
                var c = s.GetComponent<CompViralSpread>();
                return c?.tier ?? SettlementTier.T1;
            }
            if (target is WorldObject_WD_Outpost)
            {
                var c = target.GetComponent<CompViralSpread>();
                float str = WorldStatsUtils.GetOutpostStatsStrength(c);
                int idx = WorldStatsUtils.TierIndexFromWorldStrengthTotal(str);
                return idx switch
                {
                    4 => SettlementTier.T4,
                    3 => SettlementTier.T3,
                    2 => SettlementTier.T2,
                    _ => SettlementTier.T1
                };
            }
            return SettlementTier.T1;
        }

        /// <summary>Legacy size-ring frac (scribed unused). Prefer <see cref="GetLosFrac"/>.</summary>
        public static float GetTargetRingFrac(SettlementTier spotClass)
        {
            var seth = WorldDominationMod.settings;
            return spotClass switch
            {
                SettlementTier.T4 => seth?.intelTargetRingFracT4 ?? WorldDominationSettings.DefIntelTargetRingFracT4,
                SettlementTier.T3 => seth?.intelTargetRingFracT3 ?? WorldDominationSettings.DefIntelTargetRingFracT3,
                SettlementTier.T2 => seth?.intelTargetRingFracT2 ?? WorldDominationSettings.DefIntelTargetRingFracT2,
                _ => seth?.intelTargetRingFracT1 ?? WorldDominationSettings.DefIntelTargetRingFracT1
            };
        }

        /// <summary>Clear LOS as a fraction of Awareness (Def 0.60 = former T1 ring).</summary>
        public static float GetLosFrac()
        {
            var seth = WorldDominationMod.settings;
            seth?.MigrateIntelTwoRingSightIfNeeded();
            return Mathf.Clamp(seth?.intelLosFrac ?? WorldDominationSettings.DefIntelLosFrac, 0.10f, 1f);
        }

        public static float GetLosGrowthFactor(CompViralSpread comp, WorldDominationSettings seth, WorldComponent_SpreadManager manager)
        {
            if (seth == null) return 1f;
            float maxMult = Mathf.Max(1f, seth.intelSettlementLosMaxMult);
            float daysToMax = Mathf.Max(1f, seth.attackRangeDaysToMax);
            int founding = comp != null ? comp.EnsureAttackRangeFoundingTick() : Find.TickManager.TicksGame;
            // Step growth once per in-game day (whole days from ticks, 60000 ticks/day): the Awareness radius is
            // then constant within a day, so the Sight disc only changes at day boundaries instead of every tick.
            int ageDaysWhole = Mathf.Max(0, (Find.TickManager.TicksGame - founding) / 60000);
            float ageProgress = Mathf.Clamp01(ageDaysWhole / daysToMax);

            float stageProgress = 0f;
            if (seth.enableLateGameScaling)
            {
                var stage = WdEscalation.GetCachedStage(manager);
                if (stage == WdEscalationStage.Late) stageProgress = 1f;
                else if (stage == WdEscalationStage.Mid) stageProgress = 0.5f;
            }
            else
            {
                stageProgress = ageProgress;
            }

            float progress = 0.5f * ageProgress + 0.5f * stageProgress;
            return 1f + (maxMult - 1f) * progress;
        }

        public static float GetViewBaselineRange(Settlement observer)
        {
            var seth = WorldDominationMod.settings;
            if (observer == null || seth == null) return 0f;
            var comp = observer.GetComponent<CompViralSpread>();
            SettlementTier tier = comp?.tier ?? SettlementTier.T1;
            return seth.GetAttackRangeBaseline(tier);
        }

        public float GetCachedSettlementMaxLos(Settlement observer)
        {
            if (observer == null) return 0f;
            EnsureMaxLosCacheDay();
            if (cachedSettlementMaxLos.TryGetValue(observer.ID, out float cached))
                return cached;
            float computed = ComputeSettlementMaxLos(observer);
            cachedSettlementMaxLos[observer.ID] = computed;
            return computed;
        }

        private void EnsureMaxLosCacheDay()
        {
            int day = GenDate.DaysPassed;
            if (day == cachedMaxLosDay) return;
            cachedMaxLosDay = day;
            cachedSettlementMaxLos.Clear();
            hopCullDirty = true;
        }

        /// <summary>
        /// MaxLOS for the player colony / a player outpost. Pawn count drives it, so it is cached for
        /// <see cref="LiveSightRadiusCacheTtlTicks"/> only (and dropped by <see cref="NotifyStationaryObserverLosChanged"/>).
        /// </summary>
        private float GetLiveCachedMaxLos(WorldObject observer)
        {
            if (observer == null) return 0f;
            int tick = Find.TickManager?.TicksGame ?? 0;
            if (tick < liveSightRadiusCacheTick || tick - liveSightRadiusCacheTick >= LiveSightRadiusCacheTtlTicks)
            {
                liveSightRadiusCache.Clear();
                liveSightRadiusCacheTick = tick;
            }
            if (liveSightRadiusCache.TryGetValue(observer.ID, out float cached))
                return cached;
            float computed = observer is WorldObject_WD_Outpost outpost
                ? ComputeOutpostMaxLos(outpost)
                : ComputeSettlementMaxLos(observer as Settlement);
            liveSightRadiusCache[observer.ID] = computed;
            return computed;
        }

        /// <summary>Pawn-count bands for player colony / outpost MaxLOS (no strength proxy).</summary>
        public const int PlayerSightPawnsForT2 = 7;
        public const int PlayerSightPawnsForT3 = 13;
        public const int PlayerSightPawnsForT4 = 21;

        public static SettlementTier SightTierFromPawnCount(int pawnCount)
        {
            if (pawnCount >= PlayerSightPawnsForT4) return SettlementTier.T4;
            if (pawnCount >= PlayerSightPawnsForT3) return SettlementTier.T3;
            if (pawnCount >= PlayerSightPawnsForT2) return SettlementTier.T2;
            return SettlementTier.T1;
        }

        public static int GetPlayerColonySightPawnCount(Settlement colony)
        {
            Map map = colony?.Map;
            if (map?.mapPawns == null) return 0;
            return map.mapPawns.FreeColonistsCount;
        }

        public static SettlementTier GetPlayerColonySightTier(Settlement colony) =>
            SightTierFromPawnCount(GetPlayerColonySightPawnCount(colony));

        public static int GetPlayerOutpostSightPawnCount(WorldObject_WD_Outpost outpost) =>
            outpost != null ? outpost.PawnCount : 0;

        public static SettlementTier GetPlayerOutpostSightTier(WorldObject_WD_Outpost outpost) =>
            SightTierFromPawnCount(GetPlayerOutpostSightPawnCount(outpost));

        /// <summary>
        /// Awareness tiles added per free colonist / outpost occupant (linear).
        /// Scaled so 0 pawns ≈ T1 baseline and <see cref="PlayerSightPawnsForT4"/> pawns ≈ T4 baseline.
        /// </summary>
        public static float GetPlayerSightAwarenessPerPawn(WorldDominationSettings seth)
        {
            float t1 = seth?.GetAttackRangeBaseline(SettlementTier.T1)
                ?? WorldDominationSettings.DefTier1AttackRangeBaseline;
            float t4 = seth?.GetAttackRangeBaseline(SettlementTier.T4)
                ?? WorldDominationSettings.DefTier4AttackRangeBaseline;
            return (t4 - t1) / PlayerSightPawnsForT4;
        }

        public static float GetPlayerSightLosPerPawn(WorldDominationSettings seth) =>
            GetPlayerSightAwarenessPerPawn(seth) * GetLosFrac();

        /// <summary>Minimum departure strength for a WD traveler to count as medium (Awareness-spottable).</summary>
        public static float GetAwarenessSpotMinTravelerStrength()
        {
            var seth = WorldDominationMod.settings;
            return seth?.intelTravelerStrengthT2 ?? WorldDominationSettings.DefIntelTravelerStrengthT2;
        }

        /// <summary>
        /// True when the target is spottable in the Awareness band outside LOS:
        /// Tier 3+ settlements/outposts, or WD travelers at T2+ departure strength.
        /// T2 sites and AT/advanced need Line of sight.
        /// </summary>
        public static bool CanSpotAtAwarenessRange(WorldObject target)
        {
            if (target == null) return false;
            SettlementTier tier = GetSightTier(target);
            if (target is Settlement || target is WorldObject_WD_Outpost)
                return tier >= SettlementTier.T3;
            if (target is WorldObject_Traveler)
                return tier >= SettlementTier.T2;
            return false;
        }

        public static float ComputeSettlementMaxLos(Settlement observer)
        {
            var seth = WorldDominationMod.settings;
            if (observer == null || seth == null) return 0f;
            var comp = observer.GetComponent<CompViralSpread>();
            float baseline;
            if (IsPlayerColonyObserver(observer))
            {
                float t1 = seth.GetAttackRangeBaseline(SettlementTier.T1);
                baseline = t1 + GetPlayerColonySightPawnCount(observer) * GetPlayerSightAwarenessPerPawn(seth);
            }
            else
                baseline = GetViewBaselineRange(observer);
            var manager = Get()?.Manager;
            return baseline * GetLosGrowthFactor(comp, seth, manager);
        }

        public static float ComputeOutpostMaxLos(WorldObject_WD_Outpost outpost)
        {
            var seth = WorldDominationMod.settings;
            if (outpost == null || seth == null) return 0f;
            var comp = outpost.GetComponent<CompViralSpread>();
            float t1 = seth.GetAttackRangeBaseline(SettlementTier.T1);
            float baseline = t1 + GetPlayerOutpostSightPawnCount(outpost) * GetPlayerSightAwarenessPerPawn(seth);
            var manager = Get()?.Manager;
            float grown = baseline * GetLosGrowthFactor(comp, seth, manager);
            // Flat hunting Watch Tower upgrade LOS (after growth).
            return grown + outpost.GetBuiltUpgradeLosBonusTiles();
        }

        /// <summary>NPC Adv Watch Tower or dedicated player Watch Tower field observer.</summary>
        public static bool IsWatchTowerObserver(WorldObject wo)
        {
            if (wo is WorldObject_WD_PlayerWatchTower) return true;
            if (wo is WorldObject_WD_AdvancedBuilding ab)
                return ab.BuildingDef != null && ab.BuildingDef.effect == AdvancedBuildingEffect.AttackRangePct;
            return false;
        }

        /// <summary>Desired clear LoS at default <see cref="WorldDominationSettings.DefIntelLosFrac"/> (0.60).</summary>
        public const float WatchTowerObserverDesiredLos = 8f;

        /// <summary>
        /// Watch Tower Awareness. Sized so default LoS = DesiredLos (8 / 0.60 ≈ 13.33).
        /// Live LoS = Awareness × intelLosFrac like every other observer.
        /// </summary>
        public static float WatchTowerObserverMaxLos =>
            WatchTowerObserverDesiredLos / WorldDominationSettings.DefIntelLosFrac;

        /// <summary>
        /// In-flight FoW observers: vanilla <see cref="TravellingTransporters"/> (pods / Odyssey shuttle)
        /// and Vehicle Framework <c>AerialVehicleInFlight</c>. Parked caravans are not airborne.
        /// </summary>
        public static bool IsAirborneFowObserver(WorldObject wo)
        {
            if (wo == null || wo.Destroyed) return false;
            if (wo is TravellingTransporters) return true;
            return VehicleFrameworkAerialAaCompat.IsAerialVehicleInFlight(wo);
        }

        public static float GetMaxLos(WorldObject observer)
        {
            var seth = WorldDominationMod.settings;
            if (observer == null) return 0f;
            if (observer is Settlement settlement)
            {
                var intel = Get();
                // Pawn-count colony LOS: short TTL cache (do not day-cache like NPC settlement tier LOS).
                if (IsPlayerColonyObserver(settlement))
                    return intel != null ? intel.GetLiveCachedMaxLos(settlement) : ComputeSettlementMaxLos(settlement);
                return intel != null ? intel.GetCachedSettlementMaxLos(settlement) : ComputeSettlementMaxLos(settlement);
            }
            if (observer is WorldObject_WD_Outpost outpost)
            {
                var intel = Get();
                return intel != null ? intel.GetLiveCachedMaxLos(outpost) : ComputeOutpostMaxLos(outpost);
            }
            if (observer is WorldObject_AT_Turret at)
                return at.EffectiveRangeTiles;
            if (IsWatchTowerObserver(observer))
                return WatchTowerObserverMaxLos;
            if (observer is WorldObject_Traveler traveler)
            {
                float land = GetLandMobileMaxLos(seth, traveler);
                if (WorldObject_Traveler.IsDropPodRevealObserver(traveler))
                    return land * 2f;
                return land;
            }
            float scoutMax = seth?.intelScoutMaxLos ?? WorldDominationSettings.DefIntelScoutMaxLos;
            // Vanilla pods / Odyssey shuttle / VF aerial in flight: drop-pod-style 2× scout Awareness.
            if (IsAirborneFowObserver(observer))
                return scoutMax * 2f;
            if (observer is Caravan)
            {
                // Landed caravans (vanilla, land VF, parked aerial/shuttle) = scout radius.
                return scoutMax;
            }
            return 0f;
        }

        private static float GetLandMobileMaxLos(WorldDominationSettings seth, WorldObject_Traveler traveler)
        {
            if (traveler.mission == TravelerMission.Scout || traveler.isNimbleScout)
                return seth?.intelScoutMaxLos ?? WorldDominationSettings.DefIntelScoutMaxLos;
            SettlementTier cls = GetSpotClass(traveler);
            return cls switch
            {
                SettlementTier.T4 => seth?.intelTravelerMaxLosT4 ?? WorldDominationSettings.DefIntelTravelerMaxLosT4,
                SettlementTier.T3 => seth?.intelTravelerMaxLosT3 ?? WorldDominationSettings.DefIntelTravelerMaxLosT3,
                SettlementTier.T2 => seth?.intelTravelerMaxLosT2 ?? WorldDominationSettings.DefIntelTravelerMaxLosT2,
                _ => seth?.intelTravelerMaxLosT1 ?? WorldDominationSettings.DefIntelTravelerMaxLosT1
            };
        }

        /// <summary>Player map colony observers (HasMap). Same LOS/Awareness rings as other observers.</summary>
        public static bool IsPlayerColonyObserver(WorldObject observer) =>
            observer is Settlement s && s.Faction?.IsPlayer == true && s.HasMap;

        /// <summary>Awareness radius (= former MaxLOS). Outer band: grey shroud, dim icons, medium+ discovery.</summary>
        public static float GetAwarenessRadius(WorldObject observer) => GetMaxLos(observer);

        /// <summary>Line of sight radius (= Awareness × intelLosFrac). Clear tiles, bright icons, discover anything.</summary>
        public static float GetLosRadius(WorldObject observer)
        {
            float awareness = GetAwarenessRadius(observer);
            if (awareness <= 0f) return 0f;
            return awareness * GetLosFrac();
        }

        /// <summary>
        /// Discover distance: LOS for T1–T2 sites / AT / advanced; Awareness for T3+ sites and T2+ travelers.
        /// </summary>
        public static float GetSightRange(WorldObject observer, WorldObject target)
        {
            float awareness = GetAwarenessRadius(observer);
            if (awareness <= 0f) return 0f;
            if (CanSpotAtAwarenessRange(target))
                return awareness;
            return GetLosRadius(observer);
        }

        /// <summary>Legacy name: sight range from settlement observer to target.</summary>
        public static float GetSightRangeTo(Settlement observer, WorldObject target) =>
            GetSightRange(observer, target);

        /// <summary>Scout spot radius from a live target (T3+ sites / T2+ travelers = full Awareness).</summary>
        public static float GetScoutSpotRadius(WorldObject target)
        {
            var seth = WorldDominationMod.settings;
            float scoutMax = seth?.intelScoutMaxLos ?? WorldDominationSettings.DefIntelScoutMaxLos;
            if (CanSpotAtAwarenessRange(target))
                return scoutMax;
            return scoutMax * GetLosFrac();
        }

        /// <summary>Scout spot by tier alone (travelers / AT T1 sentinel). Sites should use the WorldObject overload.</summary>
        public static float GetScoutSpotRadius(SettlementTier targetTier)
        {
            var seth = WorldDominationMod.settings;
            float scoutMax = seth?.intelScoutMaxLos ?? WorldDominationSettings.DefIntelScoutMaxLos;
            if (targetTier >= SettlementTier.T2)
                return scoutMax;
            return scoutMax * GetLosFrac();
        }

        public void BootstrapAllFactions()
        {
            var manager = Get()?.Manager;
            if (manager == null) return;
            var snap = manager.CurrentDailySnapshot ?? DailyWorldSnapshot.Build();
            if (snap?.SettlementsByFaction == null) return;

            suppressDiscoveryLogging = true;
            try
            {
                foreach (var kv in snap.SettlementsByFaction)
                {
                    Faction f = kv.Key;
                    if (f == null) continue;
                    List<Settlement> own = kv.Value;
                    if (own == null) continue;
                    for (int i = 0; i < own.Count; i++)
                    {
                        Settlement self = own[i];
                        if (self == null || self.Destroyed) continue;
                        Discover(f, self);
                        RunLosFromObserver(self, snap);
                    }
                }

                Faction player = Faction.OfPlayerSilentFail;
                if (player != null && snap.PlayerOutposts != null)
                {
                    for (int i = 0; i < snap.PlayerOutposts.Count; i++)
                    {
                        var o = snap.PlayerOutposts[i];
                        if (o != null)
                        {
                            Discover(player, o);
                            RunLosFromOutpost(o, snap);
                        }
                    }
                }

                // Player discovery is FoW-driven (LOS / scouts / combat) — do not omniscient-fill.
            }
            finally
            {
                suppressDiscoveryLogging = false;
            }
            Version++;
            iconVersion++;
        }

        /// <summary>
        /// Debug/compat helper: mark every settlement and WD traveler known to the player.
        /// Not called in normal FoW play.
        /// </summary>
        public void EnsurePlayerKnowsAllSitesAndTravelers(Faction player = null)
        {
            player ??= Faction.OfPlayerSilentFail;
            if (player == null) return;

            bool prev = suppressDiscoveryLogging;
            suppressDiscoveryLogging = true;
            try
            {
                List<Settlement> settlements = Find.WorldObjects?.Settlements;
                if (settlements != null)
                {
                    for (int i = 0; i < settlements.Count; i++)
                    {
                        Settlement s = settlements[i];
                        if (s != null && !s.Destroyed)
                            Discover(player, s);
                    }
                }

                IReadOnlyList<WorldObject_WD_Outpost> outposts = WdPlayerOutpostCache.PlayerOutposts;
                for (int i = 0; i < outposts.Count; i++)
                {
                    if (outposts[i] != null && !outposts[i].Destroyed)
                        Discover(player, outposts[i]);
                }

                IReadOnlyList<WorldObject_AT_Turret> ats = WorldObject_AT_Turret.LiveTurrets;
                if (ats != null)
                {
                    for (int i = 0; i < ats.Count; i++)
                    {
                        if (ats[i] != null && !ats[i].Destroyed)
                            Discover(player, ats[i]);
                    }
                }

                var adv = WorldComponent_AdvancedBuildings.Get();
                IReadOnlyList<WorldObject_WD_AdvancedBuilding> buildings = adv?.All;
                if (buildings != null)
                {
                    for (int i = 0; i < buildings.Count; i++)
                    {
                        if (buildings[i] != null && !buildings[i].Destroyed)
                            Discover(player, buildings[i]);
                    }
                }

                IReadOnlyList<WorldObject_Traveler> travelers = WorldObject_Traveler.LiveTravelers;
                for (int i = 0; i < travelers.Count; i++)
                {
                    WorldObject_Traveler t = travelers[i];
                    if (t == null || t.Destroyed) continue;
                    if (WorldObject_Traveler.IsShellMission(t.mission)) continue;
                    DiscoverTraveler(player, t);
                }
            }
            finally
            {
                suppressDiscoveryLogging = prev;
            }
        }

        public void RunLosFromObserver(Settlement observer, DailyWorldSnapshot snap = null)
        {
            if (observer?.Faction == null || observer.Destroyed) return;
            Faction f = observer.Faction;
            snap ??= Manager?.CurrentDailySnapshot;
            float maxLos;
            if (IsPlayerColonyObserver(observer))
            {
                // Pawn-count colony LOS: compute fresh here and refresh the short TTL cache.
                maxLos = ComputeSettlementMaxLos(observer);
                liveSightRadiusCache[observer.ID] = maxLos;
            }
            else
            {
                maxLos = GetCachedSettlementMaxLos(observer);
                cachedSettlementMaxLos[observer.ID] = maxLos;
            }
            SpotSitesFromObserver(observer, f, maxLos, snap);
            TileFow.RevealTilesInRadius(f, observer.Tile, maxLos);
        }

        public void RunLosFromOutpost(WorldObject_WD_Outpost observer, DailyWorldSnapshot snap = null)
        {
            if (observer?.Faction == null || observer.Destroyed) return;
            snap ??= Manager?.CurrentDailySnapshot;
            float maxLos = ComputeOutpostMaxLos(observer);
            liveSightRadiusCache[observer.ID] = maxLos;
            SpotSitesFromObserver(observer, observer.Faction, maxLos, snap);
            TileFow.RevealTilesInRadius(observer.Faction, observer.Tile, maxLos);
        }

        /// <summary>AT / Watch Tower field observers: spot + explore Awareness disk.</summary>
        public void RunLosFromFieldObserver(WorldObject observer, DailyWorldSnapshot snap = null)
        {
            if (observer?.Faction == null || observer.Destroyed) return;
            float maxLos = GetMaxLos(observer);
            if (maxLos <= 0f) return;
            snap ??= Manager?.CurrentDailySnapshot;
            SpotSitesFromObserver(observer, observer.Faction, maxLos, snap);
            TileFow.RevealTilesInRadius(observer.Faction, observer.Tile, maxLos);
        }

        /// <summary>
        /// Stationary observer spotting. Known-first: every loop tests the faction's known set before any
        /// distance / component / radius work, then uses one rounded ApproxDistance per pair.
        /// <paramref name="maxLos"/> is the observer's Awareness; LOS = Awareness × intelLosFrac.
        /// </summary>
        private void SpotSitesFromObserver(WorldObject observer, Faction f, float maxLos, DailyWorldSnapshot snap)
        {
            if (maxLos <= 0f || f == null || observer == null) return;
            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return;
            int obsTile = observer.Tile.tileId;
            float los = GetLosRadius(observer);
            HashSet<int> knownSiteIds = GetOrCreateKnownSiteSet(f);

            if (snap?.SettlementsByFaction != null)
            {
                foreach (var kv in snap.SettlementsByFaction)
                {
                    if (kv.Key == f) continue;
                    var list = kv.Value;
                    if (list == null) continue;
                    for (int i = 0; i < list.Count; i++)
                    {
                        Settlement other = list[i];
                        if (other == null || other.Destroyed || other.Faction == f) continue;
                        if (knownSiteIds.Contains(other.ID)) continue;
                        int d = TileDistance(grid, obsTile, other.Tile.tileId);
                        if (d > 1 && d > maxLos) continue;
                        // Inside Awareness here: LOS spots anything, the Awareness band only Tier 3+ sites.
                        if (d <= 1 || d <= los || CanSpotAtAwarenessRange(other))
                            Discover(f, other, observer);
                    }
                }
            }

            // Player map colony is not in SettlementsByFaction — still a Discover target when alwaysKnown is off.
            TrySpotPlayerColonyFromObserver(observer, f, maxLos, los, grid, knownSiteIds);

            if (snap?.PlayerOutposts != null)
            {
                for (int i = 0; i < snap.PlayerOutposts.Count; i++)
                {
                    var o = snap.PlayerOutposts[i];
                    if (o == null || o.Destroyed || o.Faction == f) continue;
                    if (knownSiteIds.Contains(o.ID)) continue;
                    int d = TileDistance(grid, obsTile, o.Tile.tileId);
                    if (d > 1 && d > maxLos) continue;
                    if (d <= 1 || d <= los || CanSpotAtAwarenessRange(o))
                        Discover(f, o, observer);
                }
            }

            SpotFieldStructuresFromObserver(observer, f, los, grid, knownSiteIds);

            // Travelers: discover into knownTravelers when in ring (map show still requires current sight).
            HashSet<int> knownTravelerIds = GetOrCreateKnownTravelerSet(f);
            IReadOnlyList<WorldObject_Traveler> travelers = WorldObject_Traveler.LiveTravelers;
            for (int i = 0; i < travelers.Count; i++)
            {
                WorldObject_Traveler t = travelers[i];
                if (t == null || t.Destroyed || t.Faction == f) continue;
                if (WorldObject_Traveler.IsShellMission(t.mission)) continue;
                if (t.intelTrackId > 0 && knownTravelerIds.Contains(t.intelTrackId)) continue;
                int d = TileDistance(grid, obsTile, t.Tile.tileId);
                if (d > 1 && d > maxLos) continue;
                if (d <= 1 || d <= los || CanSpotAtAwarenessRange(t))
                    DiscoverTraveler(f, t, observer);
            }
        }

        /// <summary>
        /// Singular player HasMap colony as a Discover target (excluded from SettlementsByFaction).
        /// No-op when always-known, same faction, or already discovered.
        /// </summary>
        private void TrySpotPlayerColonyFromObserver(
            WorldObject observer,
            Faction f,
            float maxLos,
            float los,
            WorldGrid grid,
            HashSet<int> knownSiteIds)
        {
            Settlement colony = InfluenceUtils.GetPlayerColony();
            if (colony == null || colony.Destroyed || !colony.HasMap) return;
            if (colony.Faction == null || colony.Faction == f) return;
            if (IsAlwaysKnown(colony) || knownSiteIds.Contains(colony.ID)) return;
            if (ReferenceEquals(observer, colony)) return;

            int d = TileDistance(grid, observer.Tile.tileId, colony.Tile.tileId);
            if (d > 1 && d > maxLos) return;
            if (d <= 1 || d <= los || CanSpotAtAwarenessRange(colony))
                Discover(f, colony, observer);
        }

        /// <summary>AT / Watch Tower / advanced are small targets: Line of sight only (not Awareness).</summary>
        private void SpotFieldStructuresFromObserver(
            WorldObject observer,
            Faction f,
            float los,
            WorldGrid grid,
            HashSet<int> knownSiteIds)
        {
            int obsTile = observer.Tile.tileId;
            IReadOnlyList<WorldObject_AT_Turret> ats = WorldObject_AT_Turret.LiveTurrets;
            for (int i = 0; i < ats.Count; i++)
            {
                WorldObject_AT_Turret at = ats[i];
                if (at == null || at.Destroyed || at.Faction == f) continue;
                if (knownSiteIds.Contains(at.ID)) continue;
                int d = TileDistance(grid, obsTile, at.Tile.tileId);
                if (d <= 1 || d <= los)
                    Discover(f, at, observer);
            }

            IReadOnlyList<WorldObject_WD_PlayerWatchTower> playerTowers = WorldObject_WD_PlayerWatchTower.LiveTowers;
            for (int i = 0; i < playerTowers.Count; i++)
            {
                WorldObject_WD_PlayerWatchTower wt = playerTowers[i];
                if (wt == null || wt.Destroyed || wt.Faction == f) continue;
                if (knownSiteIds.Contains(wt.ID)) continue;
                int d = TileDistance(grid, obsTile, wt.Tile.tileId);
                if (d <= 1 || d <= los)
                    Discover(f, wt, observer);
            }

            var registry = WorldComponent_AdvancedBuildings.Get();
            if (registry != null)
            {
                IReadOnlyList<WorldObject_WD_AdvancedBuilding> buildings = registry.All;
                for (int i = 0; i < buildings.Count; i++)
                {
                    var b = buildings[i];
                    if (b == null || b.Destroyed || b.Faction == f) continue;
                    if (knownSiteIds.Contains(b.ID)) continue;
                    int d = TileDistance(grid, obsTile, b.Tile.tileId);
                    if (d <= 1 || d <= los)
                        Discover(f, b, observer);
                }
            }

            // Settlement ruins: LOS-only targets (same field pass; LiveRuins avoids AllWorldObjects scan).
            IReadOnlyList<WorldObject_WdSettlementRuin> ruins = WorldObject_WdSettlementRuin.LiveRuins;
            for (int i = 0; i < ruins.Count; i++)
            {
                WorldObject_WdSettlementRuin ruin = ruins[i];
                if (ruin == null || ruin.Destroyed) continue;
                if (ruin.Faction != null && ruin.Faction == f) continue;
                if (knownSiteIds.Contains(ruin.ID)) continue;
                int d = TileDistance(grid, obsTile, ruin.Tile.tileId);
                if (d <= 1 || d <= los)
                    Discover(f, ruin, observer);
            }
        }

        /// <summary>
        /// Session seed of explored tiles. Player only: NPC factions keep no tile history
        /// (their site / traveler knowledge lives in <see cref="knownSites"/> / <see cref="knownTravelers"/>).
        /// Also prunes NPC explored entries carried by older saves.
        /// </summary>
        private void SeedAllFactionTileExploration()
        {
            if (!IsTracking()) return;
            Faction player = Faction.OfPlayerSilentFail;
            if (player == null) return;
            TileFow.DropNonPlayerExplored();
            var observers = new List<WorldObject>(64);
            CollectFowObservers(player, Manager, observers);
            TileFow.SeedFactionFromObservers(player, observers);
        }

        /// <summary>Reveal terrain tiles around a traveler for its faction (tile FoW memory).</summary>
        public void RevealTilesFromTraveler(WorldObject_Traveler traveler)
        {
            if (traveler?.Faction == null || traveler.Destroyed) return;
            float maxLos = GetMaxLos(traveler);
            if (maxLos <= 0f) return;
            TileFow.RevealTilesInRadius(traveler.Faction, traveler.Tile, maxLos);
            // Visible disks are view-faction only — skip dirty when this mover is not the FoW view.
            if (ShouldDirtyVisibleStructureForMover(traveler.Faction))
                TileFow.MarkVisibleStructureDirty();
        }

        /// <summary>
        /// True when a mobile observer's tile paint can change the current FoW view's clear/visible disks.
        /// Hide/dim cache is still invalidated separately (travelers are FoW targets for any view).
        /// </summary>
        private static bool ShouldDirtyVisibleStructureForMover(Faction moverFaction)
        {
            if (moverFaction == null || !WdFowDraw.IsActive) return false;
            Faction view = WdFowDraw.EffectiveViewFaction;
            return view != null && view == moverFaction;
        }

        /// <summary>Merged tile-exit pass: settlements/outposts spot traveler; traveler spots sites; travelers spot each other.</summary>
        public void TrySpotTravelerFromSettlementsAndCaravans(WorldObject_Traveler traveler)
        {
            if (traveler == null || traveler.Destroyed || !IsTracking()) return;
            bool perf = PerfLogging;
            long perfStart = perf ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
            SpotTravelerOnHop(traveler);
            if (perf)
            {
                perfHopTicks += System.Diagnostics.Stopwatch.GetTimestamp() - perfStart;
                perfHopCalls++;
            }
        }

        /// <summary>cos of the angle that safely covers <paramref name="awareness"/> tiles (rounding + adjacency + 5% slack).</summary>
        private float HopCullMinDot(float awareness)
        {
            float angle = (Mathf.Max(awareness, 1f) + 1f) * 1.05f * hopCullRadPerTile;
            return angle >= Mathf.PI ? HopCullNever : Mathf.Cos(angle);
        }

        /// <summary>
        /// Rebuild the per-settlement direction / sight-angle arrays when the settlement list or a sight radius
        /// changed (dirty flag, count change, new tile count). O(settlements), a few times per day.
        /// </summary>
        private void EnsureHopCullCache(WorldGrid grid, List<Settlement> settlements)
        {
            int tiles = grid.TilesCount;
            if (!hopCullDirty && hopCullTilesCount == tiles && hopCullSites.Count == settlements.Count)
                return;

            if (hopCullTilesCount != tiles)
            {
                hopCullTilesCount = tiles;
                hopCullRadPerTile = CalibrateRadiansPerTile(grid, tiles);
            }
            hopCullDirty = false;
            hopCullSites.Clear();
            hopCullDirs.Clear();
            hopCullMinDot.Clear();

            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement s = settlements[i];
                Vector3 dir = Vector3.zero;
                float minDot = HopCullNever;
                if (s != null && !s.Destroyed && hopCullRadPerTile > 0f && s.Faction?.IsPlayer != true)
                {
                    int tileId = s.Tile.tileId;
                    if (s.GetComponent<CompViralSpread>() == null)
                    {
                        // Not a WD observer or target for the hop pass.
                        minDot = HopCullAlways;
                    }
                    else if (tileId >= 0 && tileId < tiles)
                    {
                        dir = grid.GetTileCenter(tileId).normalized;
                        minDot = HopCullMinDot(GetCachedSettlementMaxLos(s));
                    }
                }
                hopCullSites.Add(s);
                hopCullDirs.Add(dir);
                hopCullMinDot.Add(minDot);
            }
        }

        /// <summary>
        /// Measure the arc per tile of <c>ApproxDistanceInTiles</c> from sample tile pairs instead of assuming
        /// the grid's formula. Returns the largest ratio (conservative: never culls a pair in range), or 0 when
        /// the samples disagree (distance not proportional to arc), which switches the cull off.
        /// </summary>
        private static float CalibrateRadiansPerTile(WorldGrid grid, int tiles)
        {
            const int Samples = 16;
            float minRatio = float.MaxValue;
            float maxRatio = 0f;
            int used = 0;
            try
            {
                if (tiles >= 1024)
                {
                    for (int i = 0; i < Samples; i++)
                    {
                        int a = (int)((long)tiles * i / Samples);
                        int b = (int)(((long)a + tiles / 5 + (long)i * 977) % tiles);
                        if (a == b) continue;
                        Vector3 da = grid.GetTileCenter(a).normalized;
                        Vector3 db = grid.GetTileCenter(b).normalized;
                        float angle = Mathf.Acos(Mathf.Clamp(Vector3.Dot(da, db), -1f, 1f));
                        // Skip near-zero and near-antipodal arcs (acos is ill-conditioned there).
                        if (angle < 0.05f || angle > 3f) continue;
                        float tilesApart = grid.ApproxDistanceInTiles(a, b);
                        if (tilesApart <= 0f) continue;
                        float ratio = angle / tilesApart;
                        if (ratio < minRatio) minRatio = ratio;
                        if (ratio > maxRatio) maxRatio = ratio;
                        used++;
                    }
                }
            }
            catch (System.Exception e)
            {
                WD_DevPerformanceSpikeLog.Msg($"FoW hop cull off: calibration threw {e.GetType().Name}");
                return 0f;
            }

            bool ok = used >= 4 && maxRatio <= minRatio * 1.02f;
            WD_DevPerformanceSpikeLog.Msg(ok
                ? $"FoW hop cull on: radPerTile={maxRatio:G6} samples={used}"
                : $"FoW hop cull off: samples={used} min={minRatio:G6} max={maxRatio:G6}");
            return ok ? maxRatio : 0f;
        }

        /// <summary>
        /// Hop pass body. Knowledge is permanent, so every pair is tested known-first in both directions
        /// (does the site's faction still need this traveler? does the traveler's faction still need this site?)
        /// before any component, radius or distance work; then one rounded ApproxDistance per pair.
        /// </summary>
        private void SpotTravelerOnHop(WorldObject_Traveler traveler)
        {
            EnsureTravelerTrackId(traveler);
            RevealTilesFromTraveler(traveler);

            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return;

            Faction tf = traveler.Faction;
            int tTile = traveler.Tile.tileId;
            float tAwareness = GetMaxLos(traveler);
            float losFrac = GetLosFrac();
            float tLos = tAwareness > 0f ? tAwareness * losFrac : 0f;
            // How others see this traveler: T2+ travelers are spotted at full Awareness, smaller ones need LOS.
            bool travelerAwarenessSpottable = CanSpotAtAwarenessRange(traveler);
            HashSet<int> tfSites = tf != null ? GetOrCreateKnownSiteSet(tf) : null;
            HashSet<int> tfTravelers = tf != null ? GetOrCreateKnownTravelerSet(tf) : null;

            // Factions that already know this traveler (one pass over the per-faction sets, then O(1) per site).
            HashSet<int> knowers = travelerKnowerScratch;
            knowers.Clear();
            int trackId = traveler.intelTrackId;
            foreach (var kv in knownTravelers)
            {
                if (kv.Value != null && kv.Value.Contains(trackId))
                    knowers.Add(kv.Key);
            }

            List<Settlement> settlements = Find.WorldObjects?.Settlements;
            if (settlements != null)
            {
                // Angular cull first: a traveler is unknown to most distant factions, so known-first alone
                // would still pay a comp lookup + distance for nearly every settlement on the planet.
                EnsureHopCullCache(grid, settlements);
                bool cull = hopCullRadPerTile > 0f && tTile >= 0 && tTile < hopCullTilesCount;
                Vector3 tDir = cull ? grid.GetTileCenter(tTile).normalized : Vector3.zero;
                float tMinDot = cull ? HopCullMinDot(tAwareness) : HopCullNever;
                int siteCount = hopCullSites.Count;
                int culled = 0;
                for (int i = 0; i < siteCount; i++)
                {
                    if (cull)
                    {
                        float siteMinDot = hopCullMinDot[i];
                        if (siteMinDot >= HopCullAlways)
                        {
                            culled++;
                            continue;
                        }
                        // Pair range = the larger of both Awareness radii = the smaller cosine.
                        float minDot = siteMinDot < tMinDot ? siteMinDot : tMinDot;
                        if (minDot > HopCullNever && Vector3.Dot(hopCullDirs[i], tDir) < minDot)
                        {
                            culled++;
                            continue;
                        }
                    }

                    Settlement s = hopCullSites[i];
                    if (s == null || s.Destroyed) continue;
                    Faction sf = s.Faction;
                    if (sf == null || sf == tf) continue;

                    bool needSpotTraveler = !knowers.Contains(sf.loadID);
                    bool needSpotSite = tfSites != null && !IsAlwaysKnown(s) && !tfSites.Contains(s.ID);
                    if (!needSpotTraveler && !needSpotSite) continue;

                    // Player map colonies have no CompViralSpread but are FoW observers.
                    bool playerColony = sf.IsPlayer && s.HasMap;
                    if (!playerColony && s.GetComponent<CompViralSpread>() == null) continue;

                    float sAwareness = playerColony ? GetLiveCachedMaxLos(s) : GetCachedSettlementMaxLos(s);
                    int d = TileDistance(grid, s.Tile.tileId, tTile);
                    if (d > 1 && d > Mathf.Max(sAwareness, tAwareness)) continue;

                    if (needSpotTraveler
                        && (d <= 1 || d <= SightRangeFor(sAwareness, losFrac, travelerAwarenessSpottable)))
                    {
                        DiscoverTraveler(sf, traveler, s);
                        knowers.Add(sf.loadID);
                    }
                    if (needSpotSite
                        && (d <= 1 || d <= tLos || (d <= tAwareness && CanSpotAtAwarenessRange(s))))
                        Discover(tf, s, traveler);
                }
                perfHopSitesSeen += siteCount;
                perfHopSitesCulled += culled;
            }

            IReadOnlyList<WorldObject_WD_Outpost> outposts = WdPlayerOutpostCache.PlayerOutposts;
            for (int i = 0; i < outposts.Count; i++)
            {
                WorldObject_WD_Outpost o = outposts[i];
                if (o == null || o.Destroyed) continue;
                Faction of = o.Faction;
                if (of == null || of == tf) continue;

                bool needSpotTraveler = !knowers.Contains(of.loadID);
                bool needSpotSite = tfSites != null && !tfSites.Contains(o.ID);
                if (!needSpotTraveler && !needSpotSite) continue;

                float oAwareness = GetLiveCachedMaxLos(o);
                int d = TileDistance(grid, o.Tile.tileId, tTile);
                if (d > 1 && d > Mathf.Max(oAwareness, tAwareness)) continue;

                if (needSpotTraveler
                    && (d <= 1 || d <= SightRangeFor(oAwareness, losFrac, travelerAwarenessSpottable)))
                {
                    DiscoverTraveler(of, traveler, o);
                    knowers.Add(of.loadID);
                }
                if (needSpotSite
                    && (d <= 1 || d <= tLos || (d <= tAwareness && CanSpotAtAwarenessRange(o))))
                    Discover(tf, o, traveler);
            }

            // Field structures, both directions:
            // as targets (AT / Watch Tower / advanced) they are small, so the traveler needs Line of sight;
            // as observers (AT turrets at fire range, Watch Towers at fixed Awareness) they spot the traveler like a site.
            bool travelerSpotsField = tfSites != null && tAwareness > 0f;

            IReadOnlyList<WorldObject_AT_Turret> ats = WorldObject_AT_Turret.LiveTurrets;
            for (int i = 0; i < ats.Count; i++)
            {
                WorldObject_AT_Turret at = ats[i];
                if (at == null || at.Destroyed) continue;
                SpotFieldPairOnHop(at, true, traveler, tf, tTile, tLos,
                    travelerSpotsField, tfSites, knowers, grid);
            }

            IReadOnlyList<WorldObject_WD_PlayerWatchTower> towers = WorldObject_WD_PlayerWatchTower.LiveTowers;
            for (int i = 0; i < towers.Count; i++)
            {
                WorldObject_WD_PlayerWatchTower wt = towers[i];
                if (wt == null || wt.Destroyed) continue;
                SpotFieldPairOnHop(wt, true, traveler, tf, tTile, tLos,
                    travelerSpotsField, tfSites, knowers, grid);
            }

            var registry = WorldComponent_AdvancedBuildings.Get();
            if (registry != null)
            {
                IReadOnlyList<WorldObject_WD_AdvancedBuilding> buildings = registry.All;
                for (int i = 0; i < buildings.Count; i++)
                {
                    var b = buildings[i];
                    if (b == null || b.Destroyed) continue;
                    // Only NPC Adv Watch Towers observe; other advanced buildings are targets only.
                    SpotFieldPairOnHop(b, IsWatchTowerObserver(b), traveler, tf, tTile, tLos,
                        travelerSpotsField, tfSites, knowers, grid);
                }
            }

            IReadOnlyList<WorldObject_WdSettlementRuin> ruins = WorldObject_WdSettlementRuin.LiveRuins;
            for (int i = 0; i < ruins.Count; i++)
            {
                WorldObject_WdSettlementRuin ruin = ruins[i];
                if (ruin == null || ruin.Destroyed) continue;
                // Ruins are targets only (no observer role).
                SpotFieldPairOnHop(ruin, false, traveler, tf, tTile, tLos,
                    travelerSpotsField, tfSites, knowers, grid);
            }

            IReadOnlyList<WorldObject_Traveler> live = WorldObject_Traveler.LiveTravelers;
            for (int i = 0; i < live.Count; i++)
            {
                WorldObject_Traveler other = live[i];
                if (other == null || other.Destroyed || other == traveler) continue;
                Faction of = other.Faction;
                if (of == null || of == tf) continue;

                bool otherNeedsUs = !knowers.Contains(of.loadID);
                bool weNeedOther = tfTravelers != null
                    && !(other.intelTrackId > 0 && tfTravelers.Contains(other.intelTrackId));
                if (!otherNeedsUs && !weNeedOther) continue;

                float oAwareness = GetMaxLos(other);
                int d = TileDistance(grid, other.Tile.tileId, tTile);
                if (d > 1 && d > Mathf.Max(oAwareness, tAwareness)) continue;

                if (otherNeedsUs
                    && (d <= 1 || d <= SightRangeFor(oAwareness, losFrac, travelerAwarenessSpottable)))
                {
                    DiscoverTraveler(of, traveler, other);
                    knowers.Add(of.loadID);
                }
                if (weNeedOther
                    && (d <= 1 || d <= tLos || (d <= tAwareness && CanSpotAtAwarenessRange(other))))
                    DiscoverTraveler(tf, other, traveler);
            }

            // Caravan spot = scout LOS/Awareness by traveler class (legacy intelPlayerCaravanMaxLos still scribed).
            float caravanAwareness = WorldDominationMod.settings?.intelScoutMaxLos
                ?? WorldDominationSettings.DefIntelScoutMaxLos;
            List<Caravan> caravans = Find.WorldObjects?.Caravans;
            if (caravans != null && caravanAwareness > 0f)
            {
                float caravanSpot = GetScoutSpotRadius(GetSpotClass(traveler));
                for (int i = 0; i < caravans.Count; i++)
                {
                    Caravan c = caravans[i];
                    if (c == null || c.Destroyed) continue;
                    Faction cf = c.Faction;
                    if (cf == null || cf == tf) continue;
                    if (knowers.Contains(cf.loadID)) continue;
                    int d = TileDistance(grid, c.Tile.tileId, tTile);
                    if (d <= 1 || d <= caravanSpot)
                    {
                        DiscoverTraveler(cf, traveler, c);
                        knowers.Add(cf.loadID);
                    }
                }
            }

            // A hop changes this traveler's in-sight icon state without any new discovery, but only when it
            // matters to the current view: the view's own mover, or a traveler the view already knows.
            // (New discoveries bump Version; the view's own sight disks invalidate icons when they change.)
            if (WdFowDraw.IsActive)
            {
                Faction view = WdFowDraw.EffectiveViewFaction;
                if (view != null && (tf == view || knowers.Contains(view.loadID)))
                    InvalidateFowDrawCache();
            }
        }

        /// <summary>
        /// Hop pass, one field structure vs the moving traveler. Known-first in both directions, then one
        /// rounded ApproxDistance. The structure is always a Line-of-sight-only target for the traveler;
        /// when <paramref name="fieldObserves"/> it also spots the traveler at its own sight range
        /// (Awareness for T2+ travelers, LOS otherwise), same rule as settlements and outposts.
        /// </summary>
        private void SpotFieldPairOnHop(
            WorldObject field,
            bool fieldObserves,
            WorldObject_Traveler traveler,
            Faction tf,
            int tTile,
            float tLos,
            bool travelerSpotsField,
            HashSet<int> tfSites,
            HashSet<int> knowers,
            WorldGrid grid)
        {
            Faction ff = field.Faction;
            if (ff == tf) return;

            bool needSpotTraveler = fieldObserves && ff != null && !knowers.Contains(ff.loadID);
            bool needSpotSite = travelerSpotsField && !tfSites.Contains(field.ID);
            if (!needSpotTraveler && !needSpotSite) return;

            int d = TileDistance(grid, tTile, field.Tile.tileId);

            if (needSpotTraveler
                && (d <= 1 || d <= GetSightRange(field, traveler)))
            {
                DiscoverTraveler(ff, traveler, field);
                knowers.Add(ff.loadID);
            }
            if (needSpotSite && (d <= 1 || d <= tLos))
                Discover(tf, field, traveler);
        }

        public void NotifySiteDestroyed(WorldObject wo)
        {
            if (wo == null) return;
            ClearSiteKnowledge(wo);
        }

        public void NotifyTravelerFullyDestroyed(WorldObject_Traveler t)
        {
            if (t == null || t.intelTrackId <= 0) return;
            foreach (var kv in knownTravelers)
                kv.Value?.Remove(t.intelTrackId);
            Version++;
        }

        public IEnumerable<Faction> FactionsThatKnow(WorldObject target)
        {
            if (target == null) yield break;
            var factions = Find.FactionManager?.AllFactionsListForReading;
            if (factions == null) yield break;
            for (int i = 0; i < factions.Count; i++)
            {
                Faction f = factions[i];
                if (f == null || f.def?.hidden == true) continue;
                if (IsDiscovered(f, target))
                    yield return f;
            }
        }

        /// <summary>Fill <paramref name="into"/> with factions that currently know <paramref name="site"/> (no alloc beyond the list).</summary>
        public void CollectFactionsThatKnow(WorldObject site, List<Faction> into)
        {
            into?.Clear();
            if (into == null || site == null || site.Destroyed) return;
            var factions = Find.FactionManager?.AllFactionsListForReading;
            if (factions == null) return;
            for (int i = 0; i < factions.Count; i++)
            {
                Faction f = factions[i];
                if (f == null || f.def?.hidden == true) continue;
                if (IsDiscovered(f, site))
                    into.Add(f);
            }
        }

        public List<int> GetKnownSiteIds(Faction faction)
        {
            if (faction == null || !knownSites.TryGetValue(faction.loadID, out var set) || set == null)
                return new List<int>();
            return new List<int>(set);
        }

        /// <summary>
        /// Daily LOS: queues every stationary observer and drains the queue a few observers per world tick
        /// (<see cref="DailyLosObserversPerTick"/>), so the day rollover carries no single-tick spike.
        /// Each observer's pass is independent and idempotent; discoveries land within a few ticks.
        /// </summary>
        public void DailyLosPass(DailyWorldSnapshot snap)
        {
            if (!IsTracking() || snap?.SettlementsByFaction == null) return;
            bool perf = PerfLogging;
            long perfStart = perf ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
            EnsureMaxLosCacheDay();
            cachedSettlementMaxLos.Clear();
            liveSightRadiusCache.Clear();
            hopCullDirty = true;
            // Overnight age/stage growth can change a settlement's Awareness; drop the select-driven Sight
            // radius cache so the gizmo disc picks up the new radius immediately (other change paths already do).
            WD_RadiusOverlayPrefs.InvalidateResolveCache();

            // A pass still in flight is simply restarted (a day is far longer than a pass needs).
            dailyLosQueue.Clear();
            dailyLosCursor = 0;
            dailyLosSnap = snap;

            foreach (var kv in snap.SettlementsByFaction)
            {
                var list = kv.Value;
                if (list == null) continue;
                for (int i = 0; i < list.Count; i++)
                {
                    Settlement s = list[i];
                    if (s != null && !s.Destroyed)
                        dailyLosQueue.Add(s);
                }
            }

            if (snap.PlayerOutposts != null)
            {
                for (int i = 0; i < snap.PlayerOutposts.Count; i++)
                {
                    var o = snap.PlayerOutposts[i];
                    if (o != null && !o.Destroyed)
                        dailyLosQueue.Add(o);
                }
            }

            // The player map colony (singular) is not in SettlementsByFaction — run LOS for FoW seed.
            Settlement colony = InfluenceUtils.GetPlayerColony();
            if (colony != null && !colony.Destroyed && colony.HasMap && colony.Faction?.IsPlayer == true)
                dailyLosQueue.Add(colony);

            QueueDailyFieldObservers();
            // Not a duplicate of the 60-tick WorldComponentTick call: that one is gated on WdFowDraw.IsActive
            // (fog overlay shown), this one only on IsTracking(). This is the once-daily fallback that keeps
            // player-caravan scout discovery + tile exploration running when the player has the fog display off.
            TickMobileCaravanAndAirborneFowReveal();

            if (perf)
                perfDailyTicks += System.Diagnostics.Stopwatch.GetTimestamp() - perfStart;

            // World-gen / start-site selection: no world ticks will drain the queue, so run it now.
            if (Current.ProgramState != ProgramState.Playing)
                ProcessDailyLosQueue(int.MaxValue);
        }

        /// <summary>Run up to <paramref name="budget"/> queued daily observers. No-op when the queue is empty.</summary>
        private void ProcessDailyLosQueue(int budget)
        {
            int count = dailyLosQueue.Count;
            if (dailyLosCursor >= count) return;

            bool perf = PerfLogging;
            long perfStart = perf ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
            DailyWorldSnapshot snap = dailyLosSnap ?? Manager?.CurrentDailySnapshot;

            while (dailyLosCursor < count && budget > 0)
            {
                budget--;
                WorldObject observer = dailyLosQueue[dailyLosCursor++];
                if (observer == null || observer.Destroyed) continue;
                if (observer is Settlement settlement)
                    RunLosFromObserver(settlement, snap);
                else if (observer is WorldObject_WD_Outpost outpost)
                    RunLosFromOutpost(outpost, snap);
                else
                    RunLosFromFieldObserver(observer, snap);
            }

            bool done = dailyLosCursor >= count;
            if (done)
            {
                dailyLosQueue.Clear();
                dailyLosCursor = 0;
                dailyLosSnap = null;
                // Age/stage can grow MaxLOS overnight — diff the view's observer disks now.
                TileFow.MarkVisibleStructureDirty();
            }

            if (perf)
            {
                perfDailyTicks += System.Diagnostics.Stopwatch.GetTimestamp() - perfStart;
                perfDailySlices++;
                if (done)
                    LogDailyPerf();
            }
        }

        /// <summary>Verbose-only: one [WD Perf] line per day with the FoW / LOS cost since the last daily pass.</summary>
        private void LogDailyPerf()
        {
            WD_DevPerformanceSpikeLog.Msg(
                $"FoW/LOS day: dailyLos={PerfMs(perfDailyTicks):F2}ms over {perfDailySlices} ticks"
                + $" | hops={perfHopCalls} {PerfMs(perfHopTicks):F2}ms"
                + $" (settlements culled {perfHopSitesCulled}/{perfHopSitesSeen}, cull={(hopCullRadPerTile > 0f ? "on" : "off")})"
                + $" | iconRebuilds={perfIconRebuilds} {PerfMs(perfIconTicks):F2}ms"
                + $" | diskRefresh={TileFow.PerfRefreshCount} {PerfMs(TileFow.PerfRefreshTicks):F2}ms"
                + $" | fowView={(WdFowDraw.IsActive ? "on" : "off")}");
            perfDailyTicks = 0L;
            perfDailySlices = 0;
            perfHopTicks = 0L;
            perfHopCalls = 0;
            perfHopSitesSeen = 0L;
            perfHopSitesCulled = 0L;
            perfIconTicks = 0L;
            perfIconRebuilds = 0;
            TileFow.PerfRefreshTicks = 0L;
            TileFow.PerfRefreshCount = 0;
        }

        /// <summary>Field observers for the daily pass: AT turrets and Watch Towers (player + NPC).</summary>
        private void QueueDailyFieldObservers()
        {
            IReadOnlyList<WorldObject_AT_Turret> ats = WorldObject_AT_Turret.LiveTurrets;
            for (int i = 0; i < ats.Count; i++)
            {
                WorldObject_AT_Turret at = ats[i];
                if (at != null && !at.Destroyed)
                    dailyLosQueue.Add(at);
            }

            IReadOnlyList<WorldObject_WD_PlayerWatchTower> towers = WorldObject_WD_PlayerWatchTower.LiveTowers;
            for (int i = 0; i < towers.Count; i++)
            {
                WorldObject_WD_PlayerWatchTower wt = towers[i];
                if (wt != null && !wt.Destroyed)
                    dailyLosQueue.Add(wt);
            }

            var advReg = WorldComponent_AdvancedBuildings.Get();
            if (advReg == null) return;
            IReadOnlyList<WorldObject_WD_AdvancedBuilding> buildings = advReg.All;
            for (int i = 0; i < buildings.Count; i++)
            {
                WorldObject_WD_AdvancedBuilding b = buildings[i];
                if (b == null || b.Destroyed || !IsWatchTowerObserver(b)) continue;
                dailyLosQueue.Add(b);
            }
        }
        /// <summary>
        /// Stationary observer MaxLOS changed (outpost defense band, settlement tier, etc.).
        /// Reveals/spots immediately and rebuilds the visible FoW disk — do not wait for the daily pass.
        /// </summary>
        public void NotifyStationaryObserverLosChanged(WorldObject observer)
        {
            if (!IsTracking() || observer == null || observer.Destroyed) return;
            if (!tileFowSeeded) return;

            liveSightRadiusCache.Remove(observer.ID);
            if (observer is Settlement settlement)
            {
                cachedSettlementMaxLos.Remove(settlement.ID);
                hopCullDirty = true;
                RunLosFromObserver(settlement);
            }
            else if (observer is WorldObject_WD_Outpost outpost)
            {
                RunLosFromOutpost(outpost);
            }
            else if (observer is WorldObject_AT_Turret || IsWatchTowerObserver(observer))
            {
                RunLosFromFieldObserver(observer);
            }
            else
                return;

            // Sight disks exist for the FoW view faction only: another faction's observer cannot change them.
            if (ShouldDirtyVisibleStructureForMover(observer.Faction))
            {
                TileFow.MarkVisibleStructureDirty();
                // Incremental: re-floods this observer's disk only (no whole-planet rebuild).
                if (TileFow.EnsureVisibleCacheFresh())
                    InvalidateFowDrawCache();
            }
            WD_RadiusOverlayPrefs.InvalidateResolveCache();
        }

        /// <summary>
        /// New settlement (or equivalent site) appeared: run its LOS outward and let nearby observers discover it.
        /// Skips worldgen/load until tile FoW has been seeded.
        /// Forces FoW hide/dim rebuild so icons update while paused / same frame.
        /// </summary>
        public void NotifySiteAppeared(WorldObject site)
        {
            hopCullDirty = true;
            if (!IsTracking() || site == null || site.Destroyed || !tileFowSeeded) return;
            NotifyStationaryObserverLosChanged(site);
            TryDiscoverSiteFromExistingObservers(site);
            ForceRebuildFowDrawCacheNow();
        }

        /// <summary>
        /// Call after WD <see cref="CompViralSpread.SetState"/> on founding paths (SpawnSetup may have run first).
        /// Idempotent Discover; refreshes FoW icons immediately.
        /// </summary>
        public void NotifySiteAppearedAfterInit(WorldObject site)
        {
            hopCullDirty = true;
            if (!IsTracking() || site == null || site.Destroyed || !tileFowSeeded) return;
            TryDiscoverSiteFromExistingObservers(site);
            ForceRebuildFowDrawCacheNow();
        }

        /// <summary>Gameplay-path FoW icon rebuild (safe while paused; not for draw/GUI).</summary>
        public void ForceRebuildFowDrawCacheNow()
        {
            if (!WdFowDraw.IsActive) return;
            // Icons read the view's tile sight bits: bring the disks up to date first.
            TileFow.EnsureVisibleCacheFresh();
            fowCacheVersion = -1;
            fowCacheTick = -999;
            fowCacheViewFactionId = int.MinValue;
            EnsureFowDrawCacheFresh();
        }

        private void TryDiscoverSiteFromExistingObservers(WorldObject site)
        {
            if (site == null || !site.Tile.Valid) return;
            // Ruins may briefly lack a faction; still allow nearby observers to Discover.
            if (site.Faction == null && !(site is WorldObject_WdSettlementRuin)) return;
            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return;
            // A site every faction already knows needs no observer scan at all.
            if (IsAlwaysKnown(site)) return;
            int siteId = site.ID;
            Faction siteFaction = site.Faction;

            List<Settlement> settlements = Find.WorldObjects?.Settlements;
            if (settlements != null)
            {
                for (int i = 0; i < settlements.Count; i++)
                {
                    Settlement s = settlements[i];
                    if (s == null || s.Destroyed || ReferenceEquals(s, site)) continue;
                    Faction sf = s.Faction;
                    if (sf == null || (siteFaction != null && sf == siteFaction)) continue;
                    // Known-first: skip before the comp lookup and distance.
                    if (GetOrCreateKnownSiteSet(sf).Contains(siteId)) continue;
                    bool playerColony = sf.IsPlayer && s.HasMap;
                    if (!playerColony && s.GetComponent<CompViralSpread>() == null) continue;
                    TryDiscoverSiteFromObserver(s, sf, site, grid);
                }
            }

            IReadOnlyList<WorldObject_WD_Outpost> outposts = WdPlayerOutpostCache.PlayerOutposts;
            for (int i = 0; i < outposts.Count; i++)
            {
                WorldObject_WD_Outpost o = outposts[i];
                if (o == null || o.Destroyed) continue;
                if (o.Faction == null || (siteFaction != null && o.Faction == siteFaction)) continue;
                TryDiscoverSiteFromObserver(o, o.Faction, site, grid);
            }

            // Field observers: AT turrets and Watch Towers (player + NPC).
            IReadOnlyList<WorldObject_AT_Turret> ats = WorldObject_AT_Turret.LiveTurrets;
            for (int i = 0; i < ats.Count; i++)
            {
                WorldObject_AT_Turret at = ats[i];
                if (at == null || at.Destroyed || ReferenceEquals(at, site)) continue;
                if (at.Faction == null || (siteFaction != null && at.Faction == siteFaction)) continue;
                TryDiscoverSiteFromObserver(at, at.Faction, site, grid);
            }

            IReadOnlyList<WorldObject_WD_PlayerWatchTower> towers = WorldObject_WD_PlayerWatchTower.LiveTowers;
            for (int i = 0; i < towers.Count; i++)
            {
                WorldObject_WD_PlayerWatchTower wt = towers[i];
                if (wt == null || wt.Destroyed || ReferenceEquals(wt, site)) continue;
                if (wt.Faction == null || (siteFaction != null && wt.Faction == siteFaction)) continue;
                TryDiscoverSiteFromObserver(wt, wt.Faction, site, grid);
            }

            var advReg = WorldComponent_AdvancedBuildings.Get();
            if (advReg != null)
            {
                IReadOnlyList<WorldObject_WD_AdvancedBuilding> buildings = advReg.All;
                for (int i = 0; i < buildings.Count; i++)
                {
                    WorldObject_WD_AdvancedBuilding b = buildings[i];
                    if (b == null || b.Destroyed || ReferenceEquals(b, site) || !IsWatchTowerObserver(b)) continue;
                    if (b.Faction == null || (siteFaction != null && b.Faction == siteFaction)) continue;
                    TryDiscoverSiteFromObserver(b, b.Faction, site, grid);
                }
            }

            // Player caravans (scout-style Awareness) as appear observers.
            Faction player = Faction.OfPlayerSilentFail;
            if (player != null && (siteFaction == null || player != siteFaction))
            {
                List<Caravan> caravans = Find.WorldObjects?.Caravans;
                if (caravans != null)
                {
                    for (int i = 0; i < caravans.Count; i++)
                    {
                        Caravan c = caravans[i];
                        if (c == null || c.Destroyed || c.Faction != player) continue;
                        TryDiscoverSiteFromObserver(c, player, site, grid);
                    }
                }
            }

            IReadOnlyList<WorldObject_Traveler> travelers = WorldObject_Traveler.LiveTravelers;
            for (int i = 0; i < travelers.Count; i++)
            {
                WorldObject_Traveler t = travelers[i];
                if (t == null || t.Destroyed || t.Faction == null) continue;
                if (siteFaction != null && t.Faction == siteFaction) continue;
                if (WorldObject_Traveler.IsShellMission(t.mission)) continue;
                TryDiscoverSiteFromObserver(t, t.Faction, site, grid);
            }
        }

        /// <summary>
        /// Same rules as <see cref="SpotSitesFromObserver"/>: known-first, then one rounded ApproxDistance.
        /// Sites: LOS spots anything, the Awareness band only Awareness-spottable (Tier 3+) sites; field structures need LOS.
        /// </summary>
        private void TryDiscoverSiteFromObserver(
            WorldObject observer,
            Faction observerFaction,
            WorldObject site,
            WorldGrid grid)
        {
            if (observer == null || observer.Destroyed || site == null || observerFaction == null) return;
            if (GetOrCreateKnownSiteSet(observerFaction).Contains(site.ID)) return;

            float maxLos = GetMaxLos(observer);
            if (maxLos <= 0f) return;
            int d = TileDistance(grid, observer.Tile.tileId, site.Tile.tileId);
            if (d > 1 && d > maxLos) return;
            if (d <= 1 || d <= GetLosRadius(observer) || CanSpotAtAwarenessRange(site))
                Discover(observerFaction, site, observer);
        }

        /// <summary>Paint tiles + discover sites from a mobile observer (caravan / traveler).</summary>
        public void NotifyMobileObserverMoved(Faction faction, WorldObject observer)
        {
            if (!IsTracking() || faction == null || observer == null || observer.Destroyed) return;
            float maxLos = GetMaxLos(observer);
            if (maxLos <= 0f) return;
            TileFow.RevealTilesInRadius(faction, observer.Tile, maxLos);
            if (ShouldDirtyVisibleStructureForMover(faction))
                TileFow.MarkVisibleStructureDirty();
            WorldActions_Scout.DiscoverSitesInScoutBubble(faction, observer.Tile.tileId, observer);
        }

        /// <summary>
        /// Player caravans (incl. land VF <c>VehicleCaravan</c> via <see cref="Caravan.Tile"/>) on tile change;
        /// external airborne for all factions on tile change or every <see cref="AirborneFowForcePaintIntervalTicks"/>.
        /// </summary>
        private void TickMobileCaravanAndAirborneFowReveal()
        {
            if (!IsTracking()) return;

            Faction player = Faction.OfPlayerSilentFail;
            if (player != null)
            {
                List<Caravan> caravans = Find.WorldObjects?.Caravans;
                if (caravans != null)
                {
                    for (int i = 0; i < caravans.Count; i++)
                    {
                        Caravan c = caravans[i];
                        if (c == null || c.Destroyed || c.Faction != player) continue;
                        int tileId = c.Tile.tileId;
                        if (caravanLastTileById.TryGetValue(c.ID, out int prev) && prev == tileId)
                            continue;
                        caravanLastTileById[c.ID] = tileId;
                        NotifyMobileObserverMoved(player, c);
                    }
                }
            }

            IReadOnlyCollection<WorldObject> airborne =
                WorldComponent_InterceptionScheduler.Current?.ExternalAirborneTargets;
            if (airborne == null || airborne.Count == 0) return;

            int tick = Find.TickManager?.TicksGame ?? 0;
            foreach (WorldObject wo in airborne)
            {
                if (wo == null || wo.Destroyed || wo.Faction == null) continue;
                if (!IsAirborneFowObserver(wo)) continue;
                if (!wo.Tile.Valid) continue;

                int id = wo.ID;
                int tileId = wo.Tile.tileId;
                bool tileChanged = !airborneLastTileById.TryGetValue(id, out int prevTile) || prevTile != tileId;
                bool forcePaint = !airborneLastForcePaintTickById.TryGetValue(id, out int lastForce)
                    || tick - lastForce >= AirborneFowForcePaintIntervalTicks;
                if (!tileChanged && !forcePaint)
                    continue;

                airborneLastTileById[id] = tileId;
                airborneLastForcePaintTickById[id] = tick;
                NotifyMobileObserverMoved(wo.Faction, wo);
            }
        }

        // Unused public API (no callers). Kept for a possible Strategic Expand keep-out consumer; do not wire here.
        public bool IsTileOutsideKnownEnemyT1Sight(Faction expander, int tile, out float score)
        {
            score = 0f;
            if (expander == null) return true;
            var manager = Find.World.GetComponent<WorldComponent_SpreadManager>();
            float bestEdge = float.MaxValue;
            bool any = false;

            List<Settlement> settlements = Find.WorldObjects?.Settlements;
            if (settlements == null) return true;
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement s = settlements[i];
                if (s?.Faction == null || s.Faction == expander) continue;
                if (!WorldActions_Utils.SafeHostileTo(s.Faction, expander)) continue;
                if (!Knows(expander, s)) continue;
                float r = GetCachedSettlementMaxLos(s) * GetLosFrac();
                float d = WorldActions_Utils.GetDistance(s.Tile.tileId, tile, manager);
                any = true;
                if (d <= r) return false;
                bestEdge = Mathf.Min(bestEdge, d - r);
            }
            score = any ? bestEdge : 100f;
            return true;
        }

        private void PruneDestroyed()
        {
            var worldObjects = Find.WorldObjects?.AllWorldObjects;
            // Never prune against an empty world list (would wipe all discovery).
            if (worldObjects == null || worldObjects.Count == 0) return;
            var liveIds = new HashSet<int>();
            for (int i = 0; i < worldObjects.Count; i++)
            {
                var wo = worldObjects[i];
                if (wo != null && !wo.Destroyed)
                    liveIds.Add(wo.ID);
            }
            if (liveIds.Count == 0) return;
            foreach (var kv in knownSites)
            {
                if (kv.Value == null) continue;
                kv.Value.RemoveWhere(id => !liveIds.Contains(id));
            }
            stickyOutpostIds.RemoveWhere(id => !liveIds.Contains(id));
        }

        private static string BurnKey(Faction attacker, WorldObject target) =>
            attacker.loadID + ":" + target.ID;
    }
}
