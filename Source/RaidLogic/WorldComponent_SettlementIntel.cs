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

        // FoW view draw cache (hide/dim). Rebuilt on Version / view faction / short tick interval.
        private readonly HashSet<int> fowHiddenIds = new HashSet<int>();
        private readonly HashSet<int> fowDimIds = new HashSet<int>();
        private readonly List<WorldObject> fowObserverScratch = new List<WorldObject>();
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

        public WorldComponent_SettlementIntel(World world) : base(world) { }

        public static WorldComponent_SettlementIntel Get() =>
            Find.World?.GetComponent<WorldComponent_SettlementIntel>();

        /// <summary>Raid-logic fog active (kill switch on + stage gate passed).</summary>
        public static bool IsActive(WorldComponent_SpreadManager manager = null)
        {
            if (!IsGateConfigured()) return false;
            var seth = WorldDominationMod.settings;
            manager ??= Find.World?.GetComponent<WorldComponent_SpreadManager>();
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
        }

        public override void WorldComponentTick()
        {
            if (Current.ProgramState != ProgramState.Playing) return;
            WdFowDraw.ClearIfGateOff();
            if (!IsTracking()) return;

            var manager = Find.World.GetComponent<WorldComponent_SpreadManager>();
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
                EnsureFowDrawCacheFresh();
                TileFow.EnsureVisibleCacheFresh();
                if (Find.TickManager.TicksGame % 60 == 0)
                    TickPlayerCaravanFowReveal();
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
                LogDiscovery(faction, traveler, observer);
            }
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
                var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
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
        }

        public void Forget(Faction faction, WorldObject target)
        {
            if (faction == null || target == null) return;
            if (!knownSites.TryGetValue(faction.loadID, out var set) || set == null) return;
            if (!set.Remove(target.ID)) return;
            if (target is WorldObject_WD_Outpost && !AnyFactionKnowsSite(target.ID))
                stickyOutpostIds.Remove(target.ID);
            Version++;
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

        /// <summary>Public for mortar / combat gates: target in current LOS of any observer of <paramref name="faction"/>.</summary>
        public bool IsInCurrentSightOfFaction(Faction faction, WorldObject target)
        {
            if (faction == null || target == null || target.Destroyed) return false;
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            CollectFowObservers(faction, manager, fowObserverScratch);
            return IsInCurrentSightOfAnyObserver(target, fowObserverScratch, manager);
        }

        // --- Faction FoW view (View intel as) ---

        public void InvalidateFowDrawCache()
        {
            fowCacheVersion = -1;
            fowCacheTick = -999;
            fowCacheViewFactionId = int.MinValue;
            TileFow.InvalidateVisibleCache();
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
            if (!WdFowDraw.IsActive || wo == null) return false;
            return fowHiddenIds.Contains(wo.ID);
        }

        public bool IsFowDimmed(WorldObject wo)
        {
            if (!WdFowDraw.IsActive || wo == null) return false;
            return fowDimIds.Contains(wo.ID);
        }

        /// <summary>Call from world tick only. Never from draw/GUI.</summary>
        public void EnsureFowDrawCacheFresh()
        {
            WdFowDraw.ClearIfGateOff();
            if (!WdFowDraw.IsActive)
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
            if (fowCacheVersion == Version
                && fowCacheViewFactionId == viewId
                && tick - fowCacheTick >= 0
                && tick - fowCacheTick < FowCacheIntervalTicks)
                return;

            fowCacheVersion = Version;
            fowCacheTick = tick;
            fowCacheViewFactionId = viewId;
            RebuildFowDrawCache(view);
        }

        private void RebuildFowDrawCache(Faction view)
        {
            fowHiddenIds.Clear();
            fowDimIds.Clear();
            if (view == null) return;

            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            var observers = new List<WorldObject>(64);
            CollectFowObservers(view, manager, observers);

            var worldObjects = Find.WorldObjects;
            if (worldObjects?.Settlements != null)
            {
                List<Settlement> settlements = worldObjects.Settlements;
                for (int i = 0; i < settlements.Count; i++)
                    ClassifyFowTarget(view, settlements[i], observers, manager);
            }

            IReadOnlyList<WorldObject_WD_Outpost> outposts = WdPlayerOutpostCache.PlayerOutposts;
            for (int i = 0; i < outposts.Count; i++)
                ClassifyFowTarget(view, outposts[i], observers, manager);

            IReadOnlyList<WorldObject_AT_Turret> ats = WorldObject_AT_Turret.LiveTurrets;
            if (ats != null)
            {
                for (int i = 0; i < ats.Count; i++)
                    ClassifyFowTarget(view, ats[i], observers, manager);
            }

            var adv = WorldComponent_AdvancedBuildings.Get();
            IReadOnlyList<WorldObject_WD_AdvancedBuilding> buildings = adv?.All;
            if (buildings != null)
            {
                for (int i = 0; i < buildings.Count; i++)
                    ClassifyFowTarget(view, buildings[i], observers, manager);
            }

            IReadOnlyList<WorldObject_Traveler> travelers = WorldObject_Traveler.LiveTravelers;
            for (int i = 0; i < travelers.Count; i++)
                ClassifyFowTarget(view, travelers[i], observers, manager);
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

            // Player colonies are not in SettlementsByFaction (IsWdParticipant excludes player).
            if (view.IsPlayer)
            {
                List<Settlement> settlements = Find.WorldObjects?.Settlements;
                if (settlements != null)
                {
                    for (int i = 0; i < settlements.Count; i++)
                    {
                        Settlement s = settlements[i];
                        if (s == null || s.Destroyed || s.Faction != view || !s.HasMap) continue;
                        if (!observers.Contains(s))
                            observers.Add(s);
                    }
                }

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
        }

        private void ClassifyFowTarget(
            Faction view,
            WorldObject wo,
            List<WorldObject> observers,
            WorldComponent_SpreadManager manager)
        {
            if (wo == null || wo.Destroyed || !WdFowDraw.IsFowTarget(wo)) return;

            // Travelers: bright in LOS; dim in Hunch; optional remain-visible → dim outside Hunch.
            // Reuses observers already collected for this rebuild (avoids CanViewForFow re-collect).
            if (wo is WorldObject_Traveler traveler)
            {
                if (wo.Faction == view)
                    return;
                if (!IsTravelerDiscovered(view, traveler))
                {
                    fowHiddenIds.Add(wo.ID);
                    return;
                }
                if (IsInCurrentLosOfAnyObserver(wo, observers, manager))
                    return;
                if (IsInCurrentSightOfAnyObserver(wo, observers, manager))
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

            // Known sites: bright in LOS; dim in Hunch or as memory outside Hunch.
            if (!IsInCurrentLosOfAnyObserver(wo, observers, manager))
                fowDimIds.Add(wo.ID);
        }

        private static bool IsInCurrentSightOfAnyObserver(
            WorldObject target,
            List<WorldObject> observers,
            WorldComponent_SpreadManager manager)
        {
            if (target == null || observers == null || observers.Count == 0) return false;
            int targetTile = target.Tile.tileId;
            for (int i = 0; i < observers.Count; i++)
            {
                WorldObject obs = observers[i];
                if (obs == null || obs.Destroyed) continue;
                float d = WorldActions_Utils.GetDistance(obs.Tile.tileId, targetTile, manager);
                if (d <= 1f) return true;
                float range = GetHunchRadius(obs);
                if (range > 0f && d <= range) return true;
            }
            return false;
        }

        /// <summary>True when any observer has clear line of sight (inner ring) to the target.</summary>
        private static bool IsInCurrentLosOfAnyObserver(
            WorldObject target,
            List<WorldObject> observers,
            WorldComponent_SpreadManager manager)
        {
            if (target == null || observers == null || observers.Count == 0) return false;
            int targetTile = target.Tile.tileId;
            for (int i = 0; i < observers.Count; i++)
            {
                WorldObject obs = observers[i];
                if (obs == null || obs.Destroyed) continue;
                float d = WorldActions_Utils.GetDistance(obs.Tile.tileId, targetTile, manager);
                if (d <= 1f) return true;
                float range = GetLosRadius(obs);
                if (range > 0f && d <= range) return true;
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
            if (target is WorldObject_AT_Turret || target is WorldObject_WD_AdvancedBuilding)
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

        /// <summary>Clear LOS as a fraction of Hunch (Def 0.60 = former T1 ring).</summary>
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
            float ageDays = Mathf.Max(0f, (Find.TickManager.TicksGame - founding) / 60000f);
            float ageProgress = Mathf.Clamp01(ageDays / daysToMax);

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
        /// Hunch tiles added per free colonist / outpost occupant (linear).
        /// Scaled so 0 pawns ≈ T1 baseline and <see cref="PlayerSightPawnsForT4"/> pawns ≈ T4 baseline.
        /// </summary>
        public static float GetPlayerSightHunchPerPawn(WorldDominationSettings seth)
        {
            float t1 = seth?.GetAttackRangeBaseline(SettlementTier.T1)
                ?? WorldDominationSettings.DefTier1AttackRangeBaseline;
            float t4 = seth?.GetAttackRangeBaseline(SettlementTier.T4)
                ?? WorldDominationSettings.DefTier4AttackRangeBaseline;
            return (t4 - t1) / PlayerSightPawnsForT4;
        }

        public static float GetPlayerSightLosPerPawn(WorldDominationSettings seth) =>
            GetPlayerSightHunchPerPawn(seth) * GetLosFrac();

        /// <summary>Minimum departure strength for a WD traveler to count as medium (Hunch-spottable).</summary>
        public static float GetHunchSpotMinTravelerStrength()
        {
            var seth = WorldDominationMod.settings;
            return seth?.intelTravelerStrengthT2 ?? WorldDominationSettings.DefIntelTravelerStrengthT2;
        }

        /// <summary>
        /// True when the target is spottable in the Hunch band outside LOS:
        /// Tier 3+ settlements/outposts, or WD travelers at T2+ departure strength.
        /// T2 sites and AT/advanced need Line of sight.
        /// </summary>
        public static bool CanSpotAtHunchRange(WorldObject target)
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
                baseline = t1 + GetPlayerColonySightPawnCount(observer) * GetPlayerSightHunchPerPawn(seth);
            }
            else
                baseline = GetViewBaselineRange(observer);
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            return baseline * GetLosGrowthFactor(comp, seth, manager);
        }

        public static float ComputeOutpostMaxLos(WorldObject_WD_Outpost outpost)
        {
            var seth = WorldDominationMod.settings;
            if (outpost == null || seth == null) return 0f;
            var comp = outpost.GetComponent<CompViralSpread>();
            float t1 = seth.GetAttackRangeBaseline(SettlementTier.T1);
            float baseline = t1 + GetPlayerOutpostSightPawnCount(outpost) * GetPlayerSightHunchPerPawn(seth);
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            return baseline * GetLosGrowthFactor(comp, seth, manager);
        }

        public static float GetMaxLos(WorldObject observer)
        {
            var seth = WorldDominationMod.settings;
            if (observer == null) return 0f;
            if (observer is Settlement settlement)
            {
                // Live pawn count for colony LOS (do not day-cache like NPC settlement tier LOS).
                if (IsPlayerColonyObserver(settlement))
                    return ComputeSettlementMaxLos(settlement);
                var intel = Get();
                return intel != null ? intel.GetCachedSettlementMaxLos(settlement) : ComputeSettlementMaxLos(settlement);
            }
            if (observer is WorldObject_WD_Outpost outpost)
                return ComputeOutpostMaxLos(outpost);
            if (observer is WorldObject_Traveler traveler)
            {
                float land = GetLandMobileMaxLos(seth, traveler);
                if (WorldObject_Traveler.IsDropPodRevealObserver(traveler))
                    return land * 2f;
                return land;
            }
            if (observer is Caravan)
            {
                // Unified with scout LOS (vanilla caravan = scout radius).
                return seth?.intelScoutMaxLos ?? WorldDominationSettings.DefIntelScoutMaxLos;
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

        /// <summary>Player map colony observers (HasMap). Same LOS/Hunch rings as other observers.</summary>
        public static bool IsPlayerColonyObserver(WorldObject observer) =>
            observer is Settlement s && s.Faction?.IsPlayer == true && s.HasMap;

        /// <summary>Hunch radius (= former MaxLOS). Outer band: grey shroud, dim icons, medium+ discovery.</summary>
        public static float GetHunchRadius(WorldObject observer) => GetMaxLos(observer);

        /// <summary>Line of sight radius (= Hunch × intelLosFrac). Clear tiles, bright icons, discover anything.</summary>
        public static float GetLosRadius(WorldObject observer)
        {
            float hunch = GetHunchRadius(observer);
            if (hunch <= 0f) return 0f;
            return hunch * GetLosFrac();
        }

        /// <summary>
        /// Discover distance: LOS for T1–T2 sites / AT / advanced; Hunch for T3+ sites and T2+ travelers.
        /// </summary>
        public static float GetSightRange(WorldObject observer, WorldObject target)
        {
            float hunch = GetHunchRadius(observer);
            if (hunch <= 0f) return 0f;
            if (CanSpotAtHunchRange(target))
                return hunch;
            return GetLosRadius(observer);
        }

        /// <summary>Legacy name: sight range from settlement observer to target.</summary>
        public static float GetSightRangeTo(Settlement observer, WorldObject target) =>
            GetSightRange(observer, target);

        /// <summary>Scout spot radius from a live target (T3+ sites / T2+ travelers = full Hunch).</summary>
        public static float GetScoutSpotRadius(WorldObject target)
        {
            var seth = WorldDominationMod.settings;
            float scoutMax = seth?.intelScoutMaxLos ?? WorldDominationSettings.DefIntelScoutMaxLos;
            if (CanSpotAtHunchRange(target))
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
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
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
            snap ??= Find.World.GetComponent<WorldComponent_SpreadManager>()?.CurrentDailySnapshot;
            float maxLos = GetCachedSettlementMaxLos(observer);
            cachedSettlementMaxLos[observer.ID] = maxLos;
            SpotSitesFromObserver(observer, f, maxLos, snap);
            TileFow.RevealTilesInRadius(f, observer.Tile, maxLos);
        }

        public void RunLosFromOutpost(WorldObject_WD_Outpost observer, DailyWorldSnapshot snap = null)
        {
            if (observer?.Faction == null || observer.Destroyed) return;
            snap ??= Find.World.GetComponent<WorldComponent_SpreadManager>()?.CurrentDailySnapshot;
            float maxLos = ComputeOutpostMaxLos(observer);
            SpotSitesFromObserver(observer, observer.Faction, maxLos, snap);
            TileFow.RevealTilesInRadius(observer.Faction, observer.Tile, maxLos);
        }

        private void SpotSitesFromObserver(WorldObject observer, Faction f, float maxLos, DailyWorldSnapshot snap)
        {
            if (maxLos <= 0f) return;
            var manager = Find.World.GetComponent<WorldComponent_SpreadManager>();

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
                        if (other == null || other.Destroyed) continue;
                        if (IsDiscovered(f, other)) continue;
                        float approx = Find.WorldGrid.ApproxDistanceInTiles(observer.Tile.tileId, other.Tile.tileId);
                        if (approx > maxLos + 1f) continue;
                        float d = WorldActions_Utils.GetDistance(observer.Tile, other.Tile, manager);
                        if (d > maxLos && d > 1f) continue;
                        if (d <= 1f || d <= GetSightRange(observer, other))
                            Discover(f, other, observer);
                    }
                }
            }

            if (snap?.PlayerOutposts != null)
            {
                for (int i = 0; i < snap.PlayerOutposts.Count; i++)
                {
                    var o = snap.PlayerOutposts[i];
                    if (o == null || o.Destroyed || o.Faction == f) continue;
                    if (IsDiscovered(f, o)) continue;
                    float approx = Find.WorldGrid.ApproxDistanceInTiles(observer.Tile.tileId, o.Tile.tileId);
                    if (approx > maxLos + 1f) continue;
                    float d = WorldActions_Utils.GetDistance(observer.Tile, o.Tile, manager);
                    if (d > maxLos && d > 1f) continue;
                    if (d <= 1f || d <= GetSightRange(observer, o))
                        Discover(f, o, observer);
                }
            }

            SpotFieldStructuresFromObserver(observer, f, maxLos, manager);

            // Travelers: discover into knownTravelers when in ring (map show still requires current sight).
            IReadOnlyList<WorldObject_Traveler> travelers = WorldObject_Traveler.LiveTravelers;
            for (int i = 0; i < travelers.Count; i++)
            {
                WorldObject_Traveler t = travelers[i];
                if (t == null || t.Destroyed || t.Faction == f) continue;
                if (WorldObject_Traveler.IsShellMission(t.mission)) continue;
                if (IsTravelerDiscovered(f, t)) continue;
                float approx = Find.WorldGrid.ApproxDistanceInTiles(observer.Tile.tileId, t.Tile.tileId);
                if (approx > maxLos + 1f) continue;
                float d = WorldActions_Utils.GetDistance(observer.Tile, t.Tile, manager);
                if (d > maxLos && d > 1f) continue;
                if (d <= 1f || d <= GetSightRange(observer, t))
                    DiscoverTraveler(f, t, observer);
            }
        }

        private void SpotFieldStructuresFromObserver(WorldObject observer, Faction f, float maxLos, WorldComponent_SpreadManager manager)
        {
            // AT / advanced are small targets: Line of sight only (not Hunch).
            float los = maxLos * GetLosFrac();
            IReadOnlyList<WorldObject_AT_Turret> ats = WorldObject_AT_Turret.LiveTurrets;
            for (int i = 0; i < ats.Count; i++)
            {
                WorldObject_AT_Turret at = ats[i];
                if (at == null || at.Destroyed || at.Faction == f) continue;
                if (IsDiscovered(f, at)) continue;
                float d = WorldActions_Utils.GetDistance(observer.Tile, at.Tile, manager);
                if (d <= 1f || d <= los)
                    Discover(f, at, observer);
            }

            var registry = WorldComponent_AdvancedBuildings.Get();
            if (registry == null) return;
            IReadOnlyList<WorldObject_WD_AdvancedBuilding> buildings = registry.All;
            for (int i = 0; i < buildings.Count; i++)
            {
                var b = buildings[i];
                if (b == null || b.Destroyed || b.Faction == f) continue;
                if (IsDiscovered(f, b)) continue;
                float d = WorldActions_Utils.GetDistance(observer.Tile, b.Tile, manager);
                if (d <= 1f || d <= los)
                    Discover(f, b, observer);
            }
        }

        private void SeedAllFactionTileExploration()
        {
            if (!IsTracking()) return;
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            var observers = new List<WorldObject>(64);
            List<Faction> factions = Find.FactionManager?.AllFactionsListForReading;
            if (factions == null) return;
            for (int i = 0; i < factions.Count; i++)
            {
                Faction f = factions[i];
                if (f == null || f.defeated || f.Hidden == true) continue;
                CollectFowObservers(f, manager, observers);
                TileFow.SeedFactionFromObservers(f, observers);
            }
        }

        /// <summary>Reveal terrain tiles around a traveler for its faction (tile FoW memory).</summary>
        public void RevealTilesFromTraveler(WorldObject_Traveler traveler)
        {
            if (traveler?.Faction == null || traveler.Destroyed) return;
            float maxLos = GetMaxLos(traveler);
            if (maxLos <= 0f) return;
            TileFow.RevealTilesInRadius(traveler.Faction, traveler.Tile, maxLos);
            TileFow.MarkVisibleStructureDirty();
        }

        /// <summary>Merged tile-exit pass: settlements/outposts spot traveler; traveler spots sites; travelers spot each other.</summary>
        public void TrySpotTravelerFromSettlementsAndCaravans(WorldObject_Traveler traveler)
        {
            if (traveler == null || traveler.Destroyed || !IsTracking()) return;
            EnsureTravelerTrackId(traveler);
            RevealTilesFromTraveler(traveler);
            var manager = Find.World.GetComponent<WorldComponent_SpreadManager>();
            float travelerMaxLos = GetMaxLos(traveler);

            List<Settlement> settlements = Find.WorldObjects?.Settlements;
            if (settlements != null)
            {
                for (int i = 0; i < settlements.Count; i++)
                {
                    Settlement s = settlements[i];
                    if (s?.Faction == null || s.Faction == traveler.Faction) continue;
                    // Player map colonies have no CompViralSpread but are FoW observers.
                    bool playerColony = s.Faction.IsPlayer && s.HasMap;
                    if (!playerColony && s.GetComponent<CompViralSpread>() == null) continue;

                    float approx = Find.WorldGrid.ApproxDistanceInTiles(s.Tile.tileId, traveler.Tile.tileId);
                    float maxSpot = Mathf.Max(
                        GetCachedSettlementMaxLos(s),
                        travelerMaxLos) + 1f;
                    if (approx > maxSpot) continue;
                    float d = WorldActions_Utils.GetDistance(s.Tile, traveler.Tile, manager);
                    if (!IsTravelerDiscovered(s.Faction, traveler))
                    {
                        float spotR = GetSightRange(s, traveler);
                        if (d <= 1f || d <= spotR)
                            DiscoverTraveler(s.Faction, traveler, s);
                    }
                    if (traveler.Faction != null && !IsDiscovered(traveler.Faction, s))
                    {
                        if (d <= 1f || d <= GetSightRange(traveler, s))
                            Discover(traveler.Faction, s, traveler);
                    }
                }
            }

            IReadOnlyList<WorldObject_WD_Outpost> outposts = WdPlayerOutpostCache.PlayerOutposts;
            for (int i = 0; i < outposts.Count; i++)
            {
                var o = outposts[i];
                if (o == null || o.Destroyed || o.Faction == null || o.Faction == traveler.Faction) continue;
                float approx = Find.WorldGrid.ApproxDistanceInTiles(o.Tile.tileId, traveler.Tile.tileId);
                float maxSpot = Mathf.Max(ComputeOutpostMaxLos(o), travelerMaxLos) + 1f;
                if (approx > maxSpot) continue;
                float d = WorldActions_Utils.GetDistance(o.Tile, traveler.Tile, manager);
                if (!IsTravelerDiscovered(o.Faction, traveler))
                {
                    float spotR = GetSightRange(o, traveler);
                    if (d <= 1f || d <= spotR)
                        DiscoverTraveler(o.Faction, traveler, o);
                }
                if (traveler.Faction != null && !IsDiscovered(traveler.Faction, o))
                {
                    if (d <= 1f || d <= GetSightRange(traveler, o))
                        Discover(traveler.Faction, o, traveler);
                }
            }

            if (traveler.Faction != null && travelerMaxLos > 0f)
            {
                float los = travelerMaxLos * GetLosFrac();
                IReadOnlyList<WorldObject_AT_Turret> ats = WorldObject_AT_Turret.LiveTurrets;
                for (int i = 0; i < ats.Count; i++)
                {
                    WorldObject_AT_Turret at = ats[i];
                    if (at == null || at.Destroyed || at.Faction == traveler.Faction) continue;
                    if (IsDiscovered(traveler.Faction, at)) continue;
                    float d = WorldActions_Utils.GetDistance(traveler.Tile, at.Tile, manager);
                    if (d <= 1f || d <= los)
                        Discover(traveler.Faction, at, traveler);
                }

                var registry = WorldComponent_AdvancedBuildings.Get();
                if (registry != null)
                {
                    IReadOnlyList<WorldObject_WD_AdvancedBuilding> buildings = registry.All;
                    for (int i = 0; i < buildings.Count; i++)
                    {
                        var b = buildings[i];
                        if (b == null || b.Destroyed || b.Faction == traveler.Faction) continue;
                        if (IsDiscovered(traveler.Faction, b)) continue;
                        float d = WorldActions_Utils.GetDistance(traveler.Tile, b.Tile, manager);
                        if (d <= 1f || d <= los)
                            Discover(traveler.Faction, b, traveler);
                    }
                }
            }

            IReadOnlyList<WorldObject_Traveler> live = WorldObject_Traveler.LiveTravelers;
            for (int i = 0; i < live.Count; i++)
            {
                var other = live[i];
                if (other == null || other.Destroyed || other == traveler) continue;
                if (other.Faction == null || other.Faction == traveler.Faction) continue;
                float d = WorldActions_Utils.GetDistance(other.Tile, traveler.Tile, manager);
                if (!IsTravelerDiscovered(other.Faction, traveler))
                {
                    float spotR = GetSightRange(other, traveler);
                    if (d <= 1f || d <= spotR)
                        DiscoverTraveler(other.Faction, traveler, other);
                }
                if (traveler.Faction != null && !IsTravelerDiscovered(traveler.Faction, other))
                {
                    float spotR = GetSightRange(traveler, other);
                    if (d <= 1f || d <= spotR)
                        DiscoverTraveler(traveler.Faction, other, traveler);
                }
            }

            // Caravan spot = scout LOS/Hunch by traveler class (legacy intelPlayerCaravanMaxLos still scribed).
            float caravanHunch = WorldDominationMod.settings?.intelScoutMaxLos
                ?? WorldDominationSettings.DefIntelScoutMaxLos;
            List<Caravan> caravans = Find.WorldObjects?.Caravans;
            if (caravans != null && caravanHunch > 0f)
            {
                float caravanSpot = GetScoutSpotRadius(GetSpotClass(traveler));
                for (int i = 0; i < caravans.Count; i++)
                {
                    Caravan c = caravans[i];
                    if (c?.Faction == null || c.Faction == traveler.Faction) continue;
                    float approx = Find.WorldGrid.ApproxDistanceInTiles(c.Tile.tileId, traveler.Tile.tileId);
                    if (approx > caravanHunch + 1f) continue;
                    float d = WorldActions_Utils.GetDistance(c.Tile, traveler.Tile, manager);
                    if (!IsTravelerDiscovered(c.Faction, traveler))
                    {
                        if (d <= 1f || d <= caravanSpot)
                            DiscoverTraveler(c.Faction, traveler, c);
                    }
                }
            }

            // In-sight FoW for travelers changes every hop even without a new discovery.
            InvalidateFowDrawCache();
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

        public List<int> GetKnownSiteIds(Faction faction)
        {
            if (faction == null || !knownSites.TryGetValue(faction.loadID, out var set) || set == null)
                return new List<int>();
            return new List<int>(set);
        }

        public void DailyLosPass(DailyWorldSnapshot snap)
        {
            if (!IsTracking() || snap?.SettlementsByFaction == null) return;
            EnsureMaxLosCacheDay();
            cachedSettlementMaxLos.Clear();

            foreach (var kv in snap.SettlementsByFaction)
            {
                var list = kv.Value;
                if (list == null) continue;
                for (int i = 0; i < list.Count; i++)
                {
                    Settlement s = list[i];
                    if (s != null && !s.Destroyed)
                        RunLosFromObserver(s, snap);
                }
            }

            if (snap.PlayerOutposts != null)
            {
                for (int i = 0; i < snap.PlayerOutposts.Count; i++)
                {
                    var o = snap.PlayerOutposts[i];
                    if (o != null && !o.Destroyed)
                        RunLosFromOutpost(o, snap);
                }
            }

            // Player map colonies are not in SettlementsByFaction — run LOS for FoW seed.
            Faction player = Faction.OfPlayerSilentFail;
            if (player != null)
            {
                List<Settlement> settlements = Find.WorldObjects?.Settlements;
                if (settlements != null)
                {
                    for (int i = 0; i < settlements.Count; i++)
                    {
                        Settlement s = settlements[i];
                        if (s == null || s.Destroyed || s.Faction != player || !s.HasMap) continue;
                        RunLosFromObserver(s, snap);
                    }
                }
            }

            TickPlayerCaravanFowReveal();
            // Age/stage can grow MaxLOS overnight — refresh the visible disk, don't wait for the 90-tick interval alone.
            TileFow.MarkVisibleStructureDirty();
        }

        /// <summary>
        /// Stationary observer MaxLOS changed (outpost defense band, settlement tier, etc.).
        /// Reveals/spots immediately and rebuilds the visible FoW disk — do not wait for the daily pass.
        /// </summary>
        public void NotifyStationaryObserverLosChanged(WorldObject observer)
        {
            if (!IsTracking() || observer == null || observer.Destroyed) return;
            if (!tileFowSeeded) return;

            if (observer is Settlement settlement)
            {
                cachedSettlementMaxLos.Remove(settlement.ID);
                RunLosFromObserver(settlement);
            }
            else if (observer is WorldObject_WD_Outpost outpost)
            {
                RunLosFromOutpost(outpost);
            }
            else
                return;

            TileFow.MarkVisibleStructureDirty();
            TileFow.EnsureVisibleCacheFresh(force: true);
            WD_RadiusOverlayPrefs.InvalidateResolveCache();
        }

        /// <summary>
        /// New settlement (or equivalent site) appeared: run its LOS outward and let nearby observers discover it.
        /// Skips worldgen/load until tile FoW has been seeded.
        /// </summary>
        public void NotifySiteAppeared(WorldObject site)
        {
            if (!IsTracking() || site == null || site.Destroyed || !tileFowSeeded) return;
            NotifyStationaryObserverLosChanged(site);
            TryDiscoverSiteFromExistingObservers(site);
        }

        private void TryDiscoverSiteFromExistingObservers(WorldObject site)
        {
            if (site?.Faction == null || !site.Tile.Valid) return;
            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return;

            List<Settlement> settlements = Find.WorldObjects?.Settlements;
            if (settlements != null)
            {
                for (int i = 0; i < settlements.Count; i++)
                {
                    Settlement s = settlements[i];
                    if (s == null || s.Destroyed || ReferenceEquals(s, site)) continue;
                    if (s.Faction == null || s.Faction == site.Faction) continue;
                    bool playerColony = s.Faction.IsPlayer && s.HasMap;
                    if (!playerColony && s.GetComponent<CompViralSpread>() == null) continue;
                    float range = GetSightRange(s, site);
                    if (range <= 0f) continue;
                    float d = grid.ApproxDistanceInTiles(s.Tile, site.Tile);
                    if (d <= 1f || d <= range)
                        Discover(s.Faction, site, s);
                }
            }

            List<WorldObject> all = Find.WorldObjects?.AllWorldObjects;
            if (all == null) return;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is not WorldObject_WD_Outpost o || o.Destroyed) continue;
                if (o.Faction == null || o.Faction == site.Faction) continue;
                float range = GetSightRange(o, site);
                if (range <= 0f) continue;
                float d = grid.ApproxDistanceInTiles(o.Tile, site.Tile);
                if (d <= 1f || d <= range)
                    Discover(o.Faction, site, o);
            }
        }

        /// <summary>Paint tiles + discover sites from a mobile observer (caravan / traveler).</summary>
        public void NotifyMobileObserverMoved(Faction faction, WorldObject observer)
        {
            if (!IsTracking() || faction == null || observer == null || observer.Destroyed) return;
            float maxLos = GetMaxLos(observer);
            if (maxLos <= 0f) return;
            TileFow.RevealTilesInRadius(faction, observer.Tile, maxLos);
            TileFow.MarkVisibleStructureDirty();
            WorldActions_Scout.DiscoverSitesInScoutBubble(faction, observer.Tile.tileId, observer);
        }

        private void TickPlayerCaravanFowReveal()
        {
            if (!IsTracking()) return;
            Faction player = Faction.OfPlayerSilentFail;
            if (player == null) return;
            List<Caravan> caravans = Find.WorldObjects?.Caravans;
            if (caravans == null) return;
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
