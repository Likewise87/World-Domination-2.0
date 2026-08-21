using System;
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Event = UnityEngine.Event;
using EventType = UnityEngine.EventType;

namespace TSA_WorldDomination
{
    /// <summary>World threat tiers: single strongest incoming raid (attacker + attacker-radius allies) as a fraction of the threatened colony's current storyteller raid points.</summary>
    public enum WorldThreatTier
    {
        None = 0,
        Low = 1,
        Moderate = 2,
        Heightened = 3,
        High = 4,
        Critical = 5
    }

    /// <summary>One hostile settlement that can reach a player colony, with its potential raid strength and supporting allies. Cached; rebuilt only on change (see WorldComponent_SpreadManager threat model).</summary>
    public struct ThreatSettlementEntry
    {
        public Settlement settlement;
        public Faction faction;
        public float rawStrength;     // self available raid strength + attacker-radius allies (pre-clamp)
        public float clampedPoints;   // expected raid points after the storyteller-band clamp (what would actually land)
        public float storytellerPct;  // rawStrength as a percentage of the nearest reachable colony's storyteller raid points
        public float travelDays;      // heuristic crow-flies raid ETA to the nearest reachable colony
        public int nearestColonyTile;
        /// <summary>Same <see cref="WorldActions_Utils.GetDistance"/> used when picking nearestColony (Nearby/Far partition).</summary>
        public float tilesToColony;
        public string allyTooltip;    // supporting-allies breakdown lines (may be empty)
    }

    public class WorldComponent_SpreadManager : WorldComponent
    {
        private Queue<Faction> dailyActionQueue = new Queue<Faction>();
        private int ticksPerAction = 0;
        private int ticksUntilNextAction = 0;
        private List<SpreadLogEntry> ActionLog = new List<SpreadLogEntry>();
        private const int MaxLogEntries = 500;
        private bool firstTickRun = false;
        private bool dirtyPowerStats = false; // Flag for immediate recalculation
        private bool lateGameMetricsBootstrapped;
        private bool lateGameMetricsBootstrapScheduled;
        private bool lateGameMetricsDirty;
        private int lateGameMetricsDirtyTick = -1;
        /// <summary>Coalescing window for player-strength changes (several outposts can change in the same operation).</summary>
        private const int LateGameMetricsCoalesceTicks = 600;
        private int nextWorldThreatRefreshTick = -1;
        /// <summary>~1 in-game hour. Cheap O(1) reclassify of the cached max-raid against a freshly sampled storyteller baseline (no world scan); the full recompute stays daily/on settings change.</summary>
        private const int WorldThreatRefreshIntervalTicks = 2500;

        private Dictionary<int, float> factionThreats = new Dictionary<int, float>();
        private Dictionary<int, string> factionBreakdowns = new Dictionary<int, string>();

        // --- World threat level (single strongest incoming raid vs storyteller baseline; refreshed with UpdateThreatScores) ---
        private WorldThreatTier cachedWorldThreatTier = WorldThreatTier.None;
        private int cachedWorldThreatPercent;          // maxRaid / baseline, rounded to whole percent
        private float cachedWorldThreatMaxRaid;        // strongest single raid strength (attacker + attacker-radius allies)
        private float cachedWorldThreatBaseline;       // threatened colony's storyteller raid points
        private GlobalTargetInfo cachedWorldThreatScariest = GlobalTargetInfo.Invalid;
        private string cachedWorldThreatScariestName;
        private string cachedWorldThreatBreakdown;
        private Settlement cachedWorldThreatColony; // threatened colony from the last full recompute; used for cheap intra-day baseline reclassify (not persisted)
        private WorldThreatTier lastWorldThreatTier = WorldThreatTier.None; // persisted; drives hysteresis across recomputes

        /// <summary>Boundary hysteresis (fraction of ratio) so the tier does not flip as storyteller points drift with wealth.</summary>
        private const float WorldThreatHysteresis = 0.03f;

        public WorldThreatTier CurrentWorldThreatTier => cachedWorldThreatTier;
        public int WorldThreatPercent => cachedWorldThreatPercent;
        public float WorldThreatMaxRaid => cachedWorldThreatMaxRaid;
        public float WorldThreatBaseline => cachedWorldThreatBaseline;
        public GlobalTargetInfo WorldThreatScariest => cachedWorldThreatScariest;
        public string WorldThreatScariestName => cachedWorldThreatScariestName;
        public string WorldThreatBreakdown => cachedWorldThreatBreakdown;
        /// <summary>Colony whose storyteller baseline drives the world-threat ratio (not persisted).</summary>
        public Settlement WorldThreatColony => cachedWorldThreatColony;

        // --- Ranked list of hostile settlements that can reach a player colony (rebuilt only on change). ---
        private readonly List<ThreatSettlementEntry> cachedThreatSettlements = new List<ThreatSettlementEntry>();
        public IReadOnlyList<ThreatSettlementEntry> ThreatSettlements => cachedThreatSettlements;
        private int lastThreatFingerprint = int.MinValue;
        private int nextThreatFingerprintTick = -1;
        /// <summary>~8s at 60 t/s. Cheap poll over near-colony hostile settlements; triggers the expensive recompute only when the fingerprint changes.</summary>
        private const int ThreatFingerprintIntervalTicks = 15000;
        private const float ThreatFingerprintStrengthBucket = 200f;

        /// <summary>Remaining abstract strength pool for vanilla <see cref="Caravan"/> under mortar fire (same basis as WD clash vs traveler).</summary>
        private Dictionary<int, float> caravanMortarVitalityRemaining = new Dictionary<int, float>();

        // Diplomacy & Coalition Fields
        public Dictionary<long, int> diplomacyFreezeTicks = new Dictionary<long, int>();
        /// <summary>Faction loadID → tick when player bribe ceasefire (blocks new WD raids vs player) expires.</summary>
        public Dictionary<int, int> playerBribeCeasefireTicksExpiry = new Dictionary<int, int>();

        /// <summary>Quest raid-bias entries: attacker prefers priorityTarget in WD raid candidate order until expiry.</summary>
        public List<QuestRaidBiasEntry> questRaidBiasEntries = new List<QuestRaidBiasEntry>();

        public Faction currentWorldLeader;
        public int leaderHandicapExpiryTick = -1;
        public int leaderHandicapCooldownTick = -1;

        public Faction currentWeakestUnderdog;
        public int underdogBuffExpiryTick = -1;
        public int underdogBuffCooldownTick = -1;

        public Faction expansionistZealFaction;
        public int expansionistZealExpiryTick = -1;
        public int expansionistZealCooldownTick = -1;

        public int antiLeaderCoalitionCooldownTick = -1;
        public Faction antiLeaderCoalitionTarget;
        public List<Faction> antiLeaderCoalitionMembers = new List<Faction>();
        public int antiLeaderCoalitionExpiryTick = -1;
        public List<AntiLeaderCoalitionPriorRelation> antiLeaderCoalitionPriorRelations = new List<AntiLeaderCoalitionPriorRelation>();

        // Late-game player metrics. Full recompute after load and once per day in CalculateDailyBudget;
        // in between, only a cheap player-outpost re-sum when something actually changed.
        public float cachedPlayerOutpostStrength;
        public float cachedPlayerGlobalShare;
        public bool cachedLateGameModifierActive;
        public bool cachedMidGameModifierActive;
        public WdEscalationStage cachedEscalationStage;
        /// <summary>Next Mid/Late outpost silver-upkeep deadline tick; -1 when inactive/cancelled.</summary>
        public int outpostUpkeepNextTick = -1;
        /// <summary>World total strength (includes player outposts) from the last full recompute; denominator for cheap refreshes.</summary>
        private float cachedWorldTotalStrength;
        public Dictionary<long, int> distanceCache = new Dictionary<long, int>();
        public HashSet<int> spaceTileCache = new HashSet<int>();

        /// <summary>Built once per day in CalculateDailyBudget. Not persisted; rebuilt each day. Validate existence when using intra-day.</summary>
        private DailyWorldSnapshot dailySnapshot;

        /// <summary>Last daily snapshot from <see cref="CalculateDailyBudget"/>; may be null before the first budget tick.</summary>
        public DailyWorldSnapshot CurrentDailySnapshot => dailySnapshot;

        /// <summary>Same-faction settlements from the daily snapshot when available; returns false if no snapshot or faction missing (caller may fall back).</summary>
        public bool TryGetFactionSettlements(Faction faction, out List<Settlement> settlements)
        {
            settlements = null;
            if (faction == null) return false;
            return dailySnapshot?.SettlementsByFaction != null
                && dailySnapshot.SettlementsByFaction.TryGetValue(faction, out settlements)
                && settlements != null;
        }
        private readonly List<Settlement> tempCandidateList = new List<Settlement>();

        /// <summary>Staggered raid evaluation — one pathfind per tick. Set by <see cref="WorldActions_Raid.AttemptRaid"/>, ticked here, finalized when complete.</summary>
        internal PendingRaidEvaluation pendingRaid;

        /// <summary>Staggered trader destination evaluation — same pattern as <see cref="pendingRaid"/>.</summary>
        internal PendingTraderEvaluation pendingTrader;

        /// <summary>Game ticks when a WD world raid launched at a player colony or player outpost (global rate caps).</summary>
        private List<int> playerWdRaidLaunchTicks = new List<int>();

        private const int PlayerWdRaidDayTicks = 60000;
        private const int PlayerWdRaidFourDayTicks = 240000;
        private const int PlayerWdRaidSevenDayTicks = 420000;

        /// <summary>
        /// True if another WD raid may target the player (colonies + outposts) under the global per-day / per-4-day / per-7-day caps.
        /// </summary>
        public bool CanAcceptPlayerWdRaid(WorldDominationSettings seth)
        {
            if (seth == null) return true;
            seth.ClampPlayerWdRaidRateCaps();
            int perDay = seth.maxPlayerWdRaidsPerDay;
            int per4Days = Mathf.Max(seth.maxPlayerWdRaidsPer4Days, perDay);
            int per7Days = Mathf.Max(seth.maxPlayerWdRaidsPer7Days, per4Days);

            if (playerWdRaidLaunchTicks == null)
                playerWdRaidLaunchTicks = new List<int>();

            int now = Find.TickManager.TicksGame;
            PrunePlayerWdRaidLaunchTicks(now);

            int countDay = 0;
            int count4 = 0;
            for (int i = 0; i < playerWdRaidLaunchTicks.Count; i++)
            {
                int age = now - playerWdRaidLaunchTicks[i];
                if (age < PlayerWdRaidDayTicks)
                    countDay++;
                if (age < PlayerWdRaidFourDayTicks)
                    count4++;
            }

            if (countDay >= perDay) return false;
            if (count4 >= per4Days) return false;
            if (playerWdRaidLaunchTicks.Count >= per7Days) return false;
            return true;
        }

        /// <summary>Record a committed WD raid launch against a player colony or outpost.</summary>
        public void RecordPlayerWdRaidLaunch()
        {
            if (playerWdRaidLaunchTicks == null)
                playerWdRaidLaunchTicks = new List<int>();
            int now = Find.TickManager.TicksGame;
            PrunePlayerWdRaidLaunchTicks(now);
            playerWdRaidLaunchTicks.Add(now);
        }

        /// <summary>
        /// Returns WD world raid launch counts against the player for the dashboard windows (1-day, 4-day, 7-day),
        /// plus the corresponding global caps from <paramref name="seth"/>.
        /// </summary>
        public void GetPlayerWdRaidLaunchCounts(
            WorldDominationSettings seth,
            out int countDay,
            out int count4Days,
            out int count7Days,
            out int capDay,
            out int cap4Days,
            out int cap7Days)
        {
            countDay = 0;
            count4Days = 0;
            count7Days = 0;
            capDay = 0;
            cap4Days = 0;
            cap7Days = 0;

            if (seth != null)
            {
                seth.ClampPlayerWdRaidRateCaps();
                capDay = seth.maxPlayerWdRaidsPerDay;
                cap4Days = Mathf.Max(seth.maxPlayerWdRaidsPer4Days, capDay);
                cap7Days = Mathf.Max(seth.maxPlayerWdRaidsPer7Days, cap4Days);
            }
            else
            {
                capDay = WorldDominationSettings.DefMaxPlayerWdRaidsPerDay;
                cap4Days = WorldDominationSettings.DefMaxPlayerWdRaidsPer4Days;
                cap7Days = WorldDominationSettings.DefMaxPlayerWdRaidsPer7Days;
            }

            if (playerWdRaidLaunchTicks == null || playerWdRaidLaunchTicks.Count == 0)
                return;

            int now = Find.TickManager.TicksGame;
            PrunePlayerWdRaidLaunchTicks(now);
            if (playerWdRaidLaunchTicks == null || playerWdRaidLaunchTicks.Count == 0)
                return;

            for (int i = 0; i < playerWdRaidLaunchTicks.Count; i++)
            {
                int age = now - playerWdRaidLaunchTicks[i];
                if (age < 0) continue;
                if (age < PlayerWdRaidDayTicks) countDay++;
                if (age < PlayerWdRaidFourDayTicks) count4Days++;
                if (age < PlayerWdRaidSevenDayTicks) count7Days++;
            }
        }

        private void PrunePlayerWdRaidLaunchTicks(int now)
        {
            if (playerWdRaidLaunchTicks == null) return;
            for (int i = playerWdRaidLaunchTicks.Count - 1; i >= 0; i--)
            {
                if (now - playerWdRaidLaunchTicks[i] >= PlayerWdRaidSevenDayTicks)
                    playerWdRaidLaunchTicks.RemoveAt(i);
            }
        }

        /// <summary>
        /// Single world calendar day id for <see cref="CompViralSpread"/> daily action cap (updated every 2500 ticks).
        /// Uses reference longitude <c>0f</c> with <see cref="GenDate.DayOfYear"/> — not per-settlement local solar day.
        /// </summary>
        public int ActionDayOfYearId { get; private set; }

        /// <summary>Vanilla world-gen NPC settlement counts by faction loadID. Captured once after gen.</summary>
        public Dictionary<int, int> vanillaNpcSettlementCountsByFactionLoadId = new Dictionary<int, int>();
        public int vanillaNpcSettlementTotal = -1;
        public bool vanillaNpcSettlementSnapshotTaken;
        /// <summary>World Setup recreate target. -1 means use <see cref="vanillaNpcSettlementTotal"/>.</summary>
        public int worldSetupTargetNpcSettlements = -1;
        /// <summary>Per-faction recreate shares (faction loadID → raw share 0–200). Empty until initialized from world gen.</summary>
        public Dictionary<int, float> worldSetupFactionSettlementShares = new Dictionary<int, float>();
        public bool worldSetupFactionSharesInitialized;

        public WorldComponent_SpreadManager(World world) : base(world) { }

        /// <summary>Migrates hysteresis tier from removed <see cref="WorldThreatManager"/> on old saves.</summary>
        internal void ImportLegacyWorldThreatTier(int legacyCategory)
        {
            if (legacyCategory <= (int)WorldThreatTier.None || legacyCategory > (int)WorldThreatTier.Critical)
                return;
            if (lastWorldThreatTier != WorldThreatTier.None)
                return;
            lastWorldThreatTier = (WorldThreatTier)legacyCategory;
        }

        /// <summary>For logging: get an anchor settlement for a faction. Uses daily snapshot if available and valid.</summary>
        public Settlement GetAnchorSettlementForFaction(Faction faction)
        {
            if (faction == null) return null;
            if (dailySnapshot != null && dailySnapshot.TryGetAnchor(faction, out var s)) return s;
            var all = Find.WorldObjects.Settlements;
            Settlement fallback = null;
            int seen = 0;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Faction != faction) continue;
                if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(all[i])) continue;
                seen++;
                if (Rand.Chance(1f / seen)) fallback = all[i];
            }
            return fallback;
        }

        public void Notify_WeightsChanged()
        {
            dirtyPowerStats = true;
            RefreshLateGameMetricsNow();
        }

        /// <summary>
        /// Re-run outpost strength sync so late-game metrics are not computed against empty-occupant floors
        /// still present briefly after load (PostLoadInit may not have finished settling every outpost).
        /// Safe: <see cref="CompViralSpread.UpdateOutpostStrengthLogically"/> refuses empty-floor crush.
        /// </summary>
        private void SyncPlayerOutpostStrengthForLateGame()
        {
            List<WorldObject_WD_Outpost> outposts = WorldStatsUtils.CollectPlayerOutposts();
            for (int i = 0; i < outposts.Count; i++)
            {
                WorldObject_WD_Outpost o = outposts[i];
                if (o == null || o.Destroyed) continue;
                o.GetComponent<CompViralSpread>()?.UpdateOutpostStrengthLogically();
            }
        }

        /// <summary>
        /// Full recompute: live outpost scan plus a full world power scan. Expensive, so it only runs on the
        /// post-load bootstrap and when settings change. Prefer this over snapshot-only paths on load: snapshot
        /// build requires <c>Spawned</c> and can miss outposts or bake empty-floor strength.
        /// </summary>
        public void RefreshLateGameMetricsNow()
        {
            var seth = WorldDominationMod.settings;
            if (seth == null || !seth.enableLateGameScaling) { ClearLateGameMetrics(); return; }

            float playerStrength = SumPlayerOutpostStrength(WorldStatsUtils.CollectPlayerOutposts());
            var stats = WorldStatsUtils.GetWorldPowerStats();
            ApplyLateGameMetrics(playerStrength, stats?.GlobalTotalStr ?? 0f, seth);
        }

        /// <summary>Snapshot-backed update used by the daily budget (same OR-gate as <see cref="RefreshLateGameMetricsNow"/>).</summary>
        public void UpdatePlayerPowerMetrics(DailyWorldSnapshot snapshot)
        {
            var seth = WorldDominationMod.settings;
            if (seth == null || !seth.enableLateGameScaling) { ClearLateGameMetrics(); return; }

            float playerStrength = PlayerPowerIndex.GetPlayerOutpostStrength(snapshot);
            ApplyLateGameMetrics(playerStrength, snapshot?.WorldPowerStats?.GlobalTotalStr ?? 0f, seth);
        }

        /// <summary>
        /// Player outpost strength changed. Coalesced into one cheap re-sum instead of recomputing on a timer;
        /// the world total keeps its value from the last full recompute.
        /// </summary>
        public void Notify_PlayerOutpostStrengthChanged()
        {
            if (lateGameMetricsDirty || !lateGameMetricsBootstrapped) return;
            lateGameMetricsDirty = true;
            lateGameMetricsDirtyTick = Find.TickManager.TicksGame + LateGameMetricsCoalesceTicks;
        }

        /// <summary>
        /// Cheap refresh: re-sum player outposts only. The world total already includes them, so it is moved by
        /// the same delta and stays consistent until the next daily full recompute.
        /// </summary>
        private void RefreshLateGameMetricsFromPlayerOutposts()
        {
            lateGameMetricsDirty = false;

            var seth = WorldDominationMod.settings;
            if (seth == null || !seth.enableLateGameScaling) { ClearLateGameMetrics(); return; }

            // Without a known denominator the share would read as 100%; pay for the full scan once instead.
            if (cachedWorldTotalStrength <= 0f) { RefreshLateGameMetricsNow(); return; }

            float playerStrength = SumPlayerOutpostStrength(WorldStatsUtils.CollectPlayerOutposts());
            float worldTotal = Mathf.Max(playerStrength, cachedWorldTotalStrength - cachedPlayerOutpostStrength + playerStrength);
            ApplyLateGameMetrics(playerStrength, worldTotal, seth);
        }

        private static float SumPlayerOutpostStrength(List<WorldObject_WD_Outpost> outposts)
        {
            float total = 0f;
            if (outposts == null) return 0f;
            for (int i = 0; i < outposts.Count; i++)
            {
                WorldObject_WD_Outpost o = outposts[i];
                if (o == null || o.Destroyed) continue;
                var comp = o.GetComponent<CompViralSpread>();
                if (comp != null) total += comp.GetTotalLocalDefensePower();
            }
            return Mathf.Max(0f, total);
        }

        private void ApplyLateGameMetrics(float playerStrength, float worldTotalStrength, WorldDominationSettings seth)
        {
            // A missing world total means the caller had no snapshot, not that the world is empty: keep the last one.
            if (worldTotalStrength > 0f) cachedWorldTotalStrength = worldTotalStrength;
            cachedPlayerOutpostStrength = Mathf.Max(0f, playerStrength);
            cachedPlayerGlobalShare = PlayerPowerIndex.ComputeGlobalShare(cachedPlayerOutpostStrength, cachedWorldTotalStrength);
            cachedEscalationStage = WdEscalation.GetStage(cachedPlayerOutpostStrength, cachedPlayerGlobalShare, seth);
            cachedLateGameModifierActive = cachedEscalationStage == WdEscalationStage.Late;
            cachedMidGameModifierActive = cachedEscalationStage == WdEscalationStage.Mid;
            lateGameMetricsDirty = false;
        }

        private void ClearLateGameMetrics()
        {
            cachedPlayerOutpostStrength = 0f;
            cachedPlayerGlobalShare = 0f;
            cachedEscalationStage = WdEscalationStage.None;
            cachedLateGameModifierActive = false;
            cachedMidGameModifierActive = false;
            lateGameMetricsDirty = false;
        }

        /// <summary>
        /// Queue the authoritative recompute for after the load long event finishes. FinalizeInit is too early:
        /// outpost occupants and virtual pawn lists can still be settling, so strength reads the empty floor.
        /// </summary>
        private void ScheduleLateGameMetricsBootstrap()
        {
            lateGameMetricsBootstrapped = false;
            if (lateGameMetricsBootstrapScheduled) return;
            lateGameMetricsBootstrapScheduled = true;
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                lateGameMetricsBootstrapScheduled = false;
                if (Current.ProgramState != ProgramState.Playing) return;
                if (Find.World?.GetComponent<WorldComponent_SpreadManager>() != this) return;
                BootstrapLateGameMetrics();
            });
        }

        private void BootstrapLateGameMetrics()
        {
            SyncPlayerOutpostStrengthForLateGame();
            // Rebuild after occupants settle so WorldPowerStats / global share use real outpost strength.
            dailySnapshot = DailyWorldSnapshot.Build();
            RefreshLateGameMetricsNow();
            lateGameMetricsBootstrapped = true;
            lateGameMetricsBootstrapScheduled = false;
        }

        public bool IsCoalitionActive(int tick = -1)
        {
            if (tick < 0) tick = Find.TickManager.TicksGame;
            return antiLeaderCoalitionTarget != null
                && !antiLeaderCoalitionTarget.defeated
                && tick < antiLeaderCoalitionExpiryTick
                && antiLeaderCoalitionMembers != null
                && antiLeaderCoalitionMembers.Count > 0;
        }

        public Faction GetActiveCoalitionTarget()
        {
            return IsCoalitionActive() ? antiLeaderCoalitionTarget : null;
        }

        public bool IsActiveCoalitionMember(Faction faction)
        {
            if (faction == null || !IsCoalitionActive()) return false;
            var members = antiLeaderCoalitionMembers;
            if (members == null) return false;
            for (int i = 0; i < members.Count; i++)
                if (members[i] == faction) return true;
            return false;
        }

        public void ClearExpiredCoalition()
        {
            if (IsCoalitionActive()) return;
            bool hasState = antiLeaderCoalitionTarget != null
                || antiLeaderCoalitionExpiryTick >= 0
                || (antiLeaderCoalitionMembers != null && antiLeaderCoalitionMembers.Count > 0)
                || (antiLeaderCoalitionPriorRelations != null && antiLeaderCoalitionPriorRelations.Count > 0);
            if (!hasState) return;
            WorldActions_DiplomacyBuffsNerfs.DissolveAntiLeaderCoalition(this);
        }

        /// <summary>Flat growth multiplier for hostile settlements when Mid or Late is active; 1 otherwise.</summary>
        public float GetLateGameGrowthMultiplier()
        {
            var seth = WorldDominationMod.settings;
            return WdEscalation.GetGrowthMult(seth, cachedEscalationStage);
        }

        public float GetCoalitionRaidPriorityChance()
        {
            var seth = WorldDominationMod.settings;
            if (seth == null || !IsCoalitionActive()) return 0f;
            return seth.coalitionRaidPriorityBias;
        }

        public override void FinalizeInit(bool fromLoad)
        {
            base.FinalizeInit(fromLoad);
            WdPostLoadGuard.Reset();
            WorldDomination_UIUtils.ResetPlanetLayerCache();
            spaceTileCache?.Clear();
            ReinforcementNeighborCache.BumpGeneration();
            WorldActions_Utils.EnsureAllSettlementsInitialized();
            WD_SettlementLayoutUtility.EnsureVanillaSnapshot();

            // Static live-traveler registry + player-outpost cache are not per-World and are not reset
            // between save loads in one session. Rebuild/invalidate now (after world objects are spawned)
            // so stale entries from a prior game cannot linger. Safe on new games (finds zero travelers).
            WorldObject_Traveler.RebuildLiveRegistry();
            WdPlayerOutpostCache.Invalidate();

            if (fromLoad)
            {
                WorldActions_Utils.MarkExistingPlayerColoniesShieldHandled();
                int removed = TravelerRemnantCleanup.RemoveOrphanedTravelers();
                if (removed > 0 && Prefs.DevMode)
                    Log.Message($"[WD] Removed {removed} orphaned traveler(s) from previous mod version (namespace change).");
                PurgeLegacyWorldThreatManager();
            }

            // Snapshot + threat caches are not persisted; rebuild now so alerts/dashboard rank are ready
            // before the first tick (and while the game is still paused after load).
            if (fromLoad)
                SyncPlayerOutpostStrengthForLateGame();

            bool budgetRan = false;
            if (dailyActionQueue.Count == 0)
            {
                CalculateDailyBudget();
                budgetRan = true;
            }
            else if (dailySnapshot == null)
            {
                dailySnapshot = DailyWorldSnapshot.Build();
                UpdatePlayerPowerMetrics(dailySnapshot);
            }

            if (!fromLoad)
            {
                WorldActions_Utils.ApplyStartingPlayerColonyRaidShields();
                WorldActions_DiplomacyBuffsNerfs.InitializeNewGameBuffCooldowns(this);
            }

            // CalculateDailyBudget already recomputes threat; otherwise do it here for load/resume.
            if (!budgetRan)
                UpdateThreatScores(dailySnapshot, fromLoad ? "FinalizeInitLoad" : "FinalizeInit");
            firstTickRun = true;

            ScheduleLateGameMetricsBootstrap();

            if (Current.ProgramState == ProgramState.Playing && Find.TickManager.TicksGame > 0)
                ActionDayOfYearId = GenDate.DayOfYear(Find.TickManager.TicksAbs, 0f);
        }

        public override void WorldComponentOnGUI()
        {
            base.WorldComponentOnGUI();
            if (Current.ProgramState != ProgramState.Playing) return;
            if (!WorldRendererUtility.WorldRendered) return;
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            MortarWorldFx.DrawWorldMapGuiOverlay();
        }

        public override void WorldComponentTick()
        {
            int currentTick = Find.TickManager.TicksGame;

            if (currentTick % 2500 == 0)
                ActionDayOfYearId = GenDate.DayOfYear(Find.TickManager.TicksAbs, 0f);

            if (!firstTickRun) { UpdateThreatScores(null, "firstTick"); firstTickRun = true; }

            if (dirtyPowerStats)
            {
                UpdateThreatScores(null, "dirtyPowerStats");
                dirtyPowerStats = false;
            }

            if (currentTick % 60000 == 0) CalculateDailyBudget();

            // Safety net: never run the heavy bootstrap every tick; re-schedule at most every 2500 ticks.
            if (!lateGameMetricsBootstrapped)
            {
                if (!lateGameMetricsBootstrapScheduled && currentTick % 2500 == 0)
                    ScheduleLateGameMetricsBootstrap();
            }
            else if (lateGameMetricsDirty && currentTick >= lateGameMetricsDirtyTick)
                RefreshLateGameMetricsFromPlayerOutposts();
            // Cheap change-detection: hash near-colony hostile settlements; only a change triggers the expensive recompute.
            if (currentTick >= nextThreatFingerprintTick)
            {
                int fp = ComputeThreatFingerprint();
                if (fp != lastThreatFingerprint)
                {
                    lastThreatFingerprint = fp;
                    dirtyPowerStats = true; // full recompute next tick (list + faction sums + world threat)
                }
                nextThreatFingerprintTick = currentTick + ThreatFingerprintIntervalTicks;
            }
            // Cheap intra-day drift: re-sample baseline and reclassify the cached max-raid (tracks wealth growth).
            if (currentTick >= nextWorldThreatRefreshTick)
            {
                ReclassifyWorldThreatAgainstFreshBaseline();
                nextWorldThreatRefreshTick = currentTick + WorldThreatRefreshIntervalTicks;
            }

            if (pendingRaid != null)
            {
                if (pendingRaid.EvaluateNext())
                {
                    WDVerbose.Msg($"Raid staggered eval finished: attacker={pendingRaid.attacker?.LabelCap ?? "?"} (see Raid assess / Raid finalize lines)");
                    WorldActions_Raid.FinalizeRaid(pendingRaid);
                    pendingRaid = null;
                }
            }

            if (pendingTrader != null)
            {
                if (pendingTrader.EvaluateNext())
                {
                    WDVerbose.Msg($"Staggered trader eval complete: sender={pendingTrader.sender?.LabelCap ?? "?"} viable={pendingTrader.viable.Count}");
                    WorldActions_TraderCaravan.FinalizeTrader(pendingTrader);
                    pendingTrader = null;
                }
            }

            WorldActions_NpcLaunchStagger.Tick();

            WD_SameTileTravelerClash.TickCaravanClashDetection();

            if (currentTick % 2500 == 0)
            {
                TickPlayerBribeCeasefireExpiry(currentTick);
                TickQuestRaidBiasExpiry(currentTick);
            }

            if (dailyActionQueue.Count > 0)
            {
                ticksUntilNextAction--;
                if (ticksUntilNextAction <= 0)
                {
                    ExecuteNextAction();
                    ticksUntilNextAction = ticksPerAction;
                }
            }
        }

        private void CalculateDailyBudget()
        {
            WD_DevPerformanceSpikeLog.Msg("CalculateDailyBudget start");
            dailyActionQueue.Clear();
            var seth = WorldDominationMod.settings;

            // Single daily enumeration: all settlements and player outposts in scope. Reused for revolt, diplomacy, and action queue.
            dailySnapshot = DailyWorldSnapshot.Build();
            var settlementsByFaction = dailySnapshot.SettlementsByFaction;
            var worldPowerStats = dailySnapshot.WorldPowerStats;

            WorldActions_Revolt.TryTriggerRevolt(this, dailySnapshot);

            // Dissolve/restore before form or random diplomacy so expiry-day pairs are not mutated then overwritten.
            ClearExpiredCoalition();
            WorldActions_DiplomacyBuffsNerfs.ApplyLeaderHandicap(this, worldPowerStats);
            WorldActions_DiplomacyBuffsNerfs.ApplyUnderdogBuff(this, worldPowerStats);
            WorldActions_DiplomacyBuffsNerfs.FormAntiLeaderCoalition(this, worldPowerStats);
            WorldActions_DiplomacyBuffsNerfs.ApplyExpansionistZeal(this);
            WorldActions_DiplomacyBuffsNerfs.TryChangeAllegiances(this);
            WorldActions_DiplomacyBuffsNerfs.TryStrongFactionWar(this, worldPowerStats);

            UpdatePlayerPowerMetrics(dailySnapshot);
            EscalationGoodwillDrain.TryPulse(this);
            EscalationOutpostUpkeep.TryDaily(this);
            WorldActions_OutpostIncidents.TryDailyOutpostIncident(this);

            UpdateThreatScores(dailySnapshot, "CalculateDailyBudget");
            WorldActions_NpcFortify.UpdateDailyThreatBits(dailySnapshot, this, seth);

            List<Faction> tempActionList = new List<Faction>();
            foreach (var kv in settlementsByFaction)
            {
                Faction f = kv.Key;
                if (f == null || f.def.hidden || f.defeated) continue;

                float shares = 0;
                foreach (var s in kv.Value)
                {
                    var comp = s.GetComponent<CompViralSpread>();
                    if (comp == null) continue;

                    float baseShare = (comp.tier == SettlementTier.T4) ? seth.tier4Share :
                                      (comp.tier == SettlementTier.T3) ? seth.tier3Share :
                                      (comp.tier == SettlementTier.T2) ? seth.tier2Share : seth.tier1Share;

                    if (f == currentWeakestUnderdog && Find.TickManager.TicksGame < underdogBuffExpiryTick)
                    {
                        baseShare *= seth.underdogActionShareMult;
                    }

                    shares += baseShare;
                }

                int actions = Math.Max(1, Mathf.RoundToInt(shares));
                for (int i = 0; i < actions; i++) tempActionList.Add(f);
            }

            tempActionList.Shuffle();
            foreach (Faction f in tempActionList) dailyActionQueue.Enqueue(f);

            int actionsCount = tempActionList.Count;
            ticksPerAction = actionsCount > 0 ? 60000 / actionsCount : 60000;

            WDVerbose.Msg($"CalculateDailyBudget: factionActionSlots={actionsCount} snapshotFactions={dailySnapshot?.SettlementsByFaction?.Count ?? 0} travelPrepExactPct={seth.travelPrepExactPercent}");
        }

        private void ExecuteNextAction()
        {
            if (dailyActionQueue.Count == 0) return;
            Faction f = dailyActionQueue.Dequeue();
            int currentTick = Find.TickManager.TicksGame;
            int currentDay = GenDate.DayOfYear(Find.TickManager.TicksAbs, 0f);

            Settlement actor = null;
            if (dailySnapshot?.SettlementsByFaction != null && dailySnapshot.SettlementsByFaction.TryGetValue(f, out var list) && list != null)
            {
                tempCandidateList.Clear();
                for (int i = 0; i < list.Count; i++)
                {
                    Settlement s = list[i];
                    if (!DailyWorldSnapshot.IsSettlementStillValid(s) || WorldActions_Utils.IsSettlementProtected(s)) continue;
                    var c = s.GetComponent<CompViralSpread>();
                    if (c == null) continue;
                    if (c.lastActionDay != currentDay) { c.actionsTakenToday = 0; c.lastActionDay = currentDay; }
                    int cap = GetTierActionCap(c.tier, WorldDominationMod.settings);
                    if (c.actionsTakenToday < cap) tempCandidateList.Add(s);
                }
                actor = tempCandidateList.Count > 0 ? tempCandidateList.RandomElement() : null;
            }

            if (actor == null)
            {
                AddLog(new SpreadLogEntry("TSA_WD_Log_ActionSkip".Translate().ToString(), null)
                {
                    labelA = "TS_WD_Log_FactionLabel".Translate(f.Name).ToString()
                });
                return;
            }

            if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(actor))
            {
                WDVerbose.Msg($"ExecuteNextAction: skip actor not on planet surface layer tile={actor.Tile} {actor.LabelCap}");
                AddLog(new SpreadLogEntry("TSA_WD_Log_ActionSkip".Translate().ToString(), actor)
                {
                    labelA = "TS_WD_Log_FactionLabel".Translate(f.Name).ToString()
                });
                return;
            }

            var comp = actor.GetComponent<CompViralSpread>();
            var seth = WorldDominationMod.settings;

            // Below tier band floor: prefer develop over raid/road/trader. Garrison floor already limits deployable strength.
            float growthThreshold = CompViralSpread.GetStrengthRange(comp.tier).min;
            bool isRecovering = comp.strength < growthThreshold;

            float incidentMult = 1f;
            if (f == currentWorldLeader && currentTick < leaderHandicapExpiryTick) incidentMult = seth.leaderIncidentWeightMult;
            if (f == currentWeakestUnderdog && currentTick < underdogBuffExpiryTick) incidentMult = seth.underdogIncidentWeightMult;

            bool developEligible = WorldActions_GrowthExpand.IsDevelopEligible(comp);
            bool fortifyEligible = WorldActions_NpcFortify.IsFortifyEligible(actor, comp);

            float wRaid = (!comp.IsRaidOnCooldown && !isRecovering) ? seth.weightRaid : 0f;
            float wMinor = (!comp.IsIncidentOnCooldown) ? seth.weightMinorIncident * incidentMult : 0f;
            float wMajor = (!comp.IsIncidentOnCooldown) ? seth.weightMajorIncident * incidentMult : 0f;
            float wDevelop = developEligible ? seth.weightGrow : 0f;
            float wBuildRoad = (!comp.IsRoadOnCooldown && !isRecovering) ? seth.weightBuildRoad : 0f;
            float wTrader = (!comp.IsTraderOnCooldown && !isRecovering) ? seth.weightTrader : 0f;
            float wFortify = fortifyEligible ? seth.weightFortify : 0f;

            float totalWeight = wRaid + wMinor + wMajor + wDevelop + wBuildRoad + wTrader + wFortify;
            if (totalWeight <= 0f)
            {
                // Soft fail: no eligible action for this slot (silent).
                return;
            }

            float rand = Rand.Range(0f, totalWeight);
            float cursor = 0f;

            if (rand < (cursor += wRaid))
            {
                WDVerbose.Msg($"DailyAction pick=raid faction={f.Name} actor={actor.LabelCap}");
                WorldActions_Raid.AttemptRaid(actor, comp, this);
            }
            else if (rand < (cursor += wMinor))
            {
                WDVerbose.Msg($"DailyAction pick=minorIncident faction={f.Name} actor={actor.LabelCap}");
                WorldActions_Incidents.AttemptMinorIncident(actor, comp, this);
            }
            else if (rand < (cursor += wMajor))
            {
                WDVerbose.Msg($"DailyAction pick=majorIncident faction={f.Name} actor={actor.LabelCap}");
                WorldActions_Incidents.AttemptMajorIncident(actor, comp, this);
            }
            else if (rand < (cursor += wDevelop))
            {
                WDVerbose.Msg($"DailyAction pick=develop faction={f.Name} actor={actor.LabelCap}");
                WorldActions_GrowthExpand.AttemptDevelop(actor, comp, this);
            }
            else if (rand < (cursor += wBuildRoad))
            {
                WDVerbose.Msg($"DailyAction pick=road faction={f.Name} actor={actor.LabelCap}");
                WorldActions_Roads.AttemptBuildRoad(actor, comp, this);
            }
            else if (rand < (cursor += wTrader))
            {
                WDVerbose.Msg($"DailyAction pick=trader faction={f.Name} actor={actor.LabelCap}");
                WorldActions_TraderCaravan.AttemptTraderCaravan(actor, comp, this);
            }
            else
            {
                WDVerbose.Msg($"DailyAction pick=fortify faction={f.Name} actor={actor.LabelCap}");
                WorldActions_NpcFortify.AttemptFortify(actor, comp, this);
            }

            comp.actionsTakenToday++;
        }

        private static int GetTierActionCap(SettlementTier tier, WorldDominationSettings seth)
        {
            if (seth == null)
            {
                return (tier == SettlementTier.T3 || tier == SettlementTier.T4) ? 1 : 1;
            }
            switch (tier)
            {
                case SettlementTier.T4: return Mathf.Max(1, seth.tier4MaxActions);
                case SettlementTier.T3: return Mathf.Max(1, seth.tier3MaxActions);
                case SettlementTier.T2: return Mathf.Max(1, seth.tier2MaxActions);
                default: return Mathf.Max(1, seth.tier1MaxActions);
            }
        }

        public void AddLog(SpreadLogEntry entry)
        {
            ActionLog.Add(entry);
            if (ActionLog.Count > MaxLogEntries) ActionLog.RemoveAt(0);
        }

        /// <summary>Sum of deployable offensive strength from this faction's settlements that can reach player holdings (hostile factions only).</summary>
        public float GetThreatScoreFor(Faction f) => (f != null && factionThreats.TryGetValue(f.loadID, out float s)) ? s : 0f;

        public string GetThreatBreakdown(Faction f) => (f != null && factionBreakdowns.TryGetValue(f.loadID, out string b)) ? b : "No data.";

        private readonly List<WorldObject> tempThreatPlayerColonyTargets = new List<WorldObject>();
        private readonly Dictionary<int, float> tempFactionSum = new Dictionary<int, float>();
        private readonly Dictionary<int, StringBuilder> tempFactionSb = new Dictionary<int, StringBuilder>();
        private readonly Dictionary<int, Faction> tempFactionRef = new Dictionary<int, Faction>();
        private static readonly StringBuilder s_allyTooltipSb = new StringBuilder();

        /// <param name="snapshot">Unused by the spatial threat model (kept for existing callers); the pass enumerates settlements once with cached distances.</param>
        private void UpdateThreatScores(DailyWorldSnapshot snapshot, string perfReason)
        {
            WD_DevPerformanceSpikeLog.Msg($"UpdateThreatScores reason={perfReason}");
            RecomputeThreatModel(WorldDominationMod.settings);
        }

        /// <summary>
        /// Single spatially-limited pass over hostile settlements that can reach a player colony. Produces the ranked
        /// <see cref="cachedThreatSettlements"/> list (raw raid strength + allies + clamped points + travel ETA), the
        /// per-faction sums/breakdowns used by the diplomacy window, and the world-threat level cache used by the alert.
        /// Runs only when the threat model changed (fingerprint), on settings/diplomacy events, and daily as a catch-all.
        /// </summary>
        private void RecomputeThreatModel(WorldDominationSettings seth)
        {
            factionThreats.Clear();
            factionBreakdowns.Clear();
            cachedThreatSettlements.Clear();
            tempFactionSum.Clear();
            tempFactionRef.Clear();
            foreach (var kv in tempFactionSb) kv.Value.Clear();

            Faction player = Faction.OfPlayerSilentFail;
            if (player == null || seth == null)
            {
                SetWorldThreatNone();
                return;
            }

            BuildThreatPlayerColonyTargets(player, seth, tempThreatPlayerColonyTargets);
            if (tempThreatPlayerColonyTargets.Count == 0)
            {
                SetWorldThreatNone();
                return;
            }

            // World-threat colony rows: baseline storyteller points per player colony (for the alert ratio).
            tempThreatColonyRows.Clear();
            for (int i = 0; i < tempThreatPlayerColonyTargets.Count; i++)
            {
                if (!(tempThreatPlayerColonyTargets[i] is Settlement colony) || !colony.HasMap) continue;
                float baseline = StorytellerUtility.DefaultThreatPointsNow(colony.Map);
                tempThreatColonyRows.Add((colony, baseline, 0f, null));
            }

            var lookup = WorldActions_Utils.GetWorldObjectsWithCompByFaction();
            var allSettlements = Find.WorldObjects.Settlements;
            bool zealActive = expansionistZealFaction != null && Find.TickManager.TicksGame < expansionistZealExpiryTick;

            for (int si = 0; si < allSettlements.Count; si++)
            {
                Settlement s = allSettlements[si];
                if (s == null || s.Tile < 0 || s.Faction == null || s.Faction.IsPlayer) continue;
                if (s.Faction.def.hidden || s.Faction.defeated) continue;
                if (!WorldActions_Utils.SafeHostileTo(s.Faction, player)) continue;
                if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(s)) continue;
                if (!CanReachAnyPlayerTarget(s, tempThreatPlayerColonyTargets, seth)) continue;

                float rawStrength = ComputeRaidStrengthWithAllies(s, seth, lookup, s_allyTooltipSb);
                if (rawStrength <= 0f) continue;

                float raidRange = SettlementAttackRangeUtil.GetNpcSettlementAttackRangeWithZeal(s, seth, this);

                // Nearest reachable colony (for travel ETA + clamp map + storyteller %) and per-colony max (for the alert ratio).
                Settlement nearestColony = null;
                float nearestDist = float.MaxValue;
                float nearestBaseline = 0f;
                for (int ci = 0; ci < tempThreatColonyRows.Count; ci++)
                {
                    var row = tempThreatColonyRows[ci];
                    float effectiveRange = raidRange;
                    float dist = WorldActions_Utils.GetDistance(s.Tile, row.colony.Tile, this);
                    if (dist > effectiveRange) continue;
                    if (rawStrength > row.maxRaid)
                        tempThreatColonyRows[ci] = (row.colony, row.baseline, rawStrength, s);
                    if (dist < nearestDist) { nearestDist = dist; nearestColony = row.colony; nearestBaseline = row.baseline; }
                }
                if (nearestColony == null) continue;

                float travelDays = TravelUtils.GetHeuristicTravelDays(s.Tile, nearestColony.Tile);
                float clamped = RaidPointsHelper.ClampRaidPointsToStorytellerBand(rawStrength, nearestColony.Map);
                float storytellerPct = nearestBaseline > 0f ? rawStrength / nearestBaseline * 100f : 0f;

                cachedThreatSettlements.Add(new ThreatSettlementEntry
                {
                    settlement = s,
                    faction = s.Faction,
                    rawStrength = rawStrength,
                    clampedPoints = clamped,
                    storytellerPct = storytellerPct,
                    travelDays = travelDays,
                    nearestColonyTile = nearestColony.Tile,
                    tilesToColony = nearestDist,
                    allyTooltip = s_allyTooltipSb.ToString()
                });

                int fid = s.Faction.loadID;
                tempFactionSum.TryGetValue(fid, out float prev);
                tempFactionSum[fid] = prev + rawStrength;
                tempFactionRef[fid] = s.Faction;
                if (!tempFactionSb.TryGetValue(fid, out var fsb)) { fsb = new StringBuilder(); tempFactionSb[fid] = fsb; }
                fsb.AppendLine("TSA_WD_NotificationStrength_Line".Translate(s.LabelCap, rawStrength.ToString("F0")).ToString());
            }

            cachedThreatSettlements.Sort((a, b) => b.rawStrength.CompareTo(a.rawStrength));

            // Per-faction sums + breakdowns for the diplomacy window.
            foreach (var kv in tempFactionSum)
            {
                string name = tempFactionRef.TryGetValue(kv.Key, out var f) && f != null ? f.Name : "";
                factionThreats[kv.Key] = kv.Value;
                string lines = tempFactionSb.TryGetValue(kv.Key, out var fsb) && fsb.Length > 0 ? fsb.ToString() : "\n" + "TSA_WD_None".Translate().ToString();
                factionBreakdowns[kv.Key] = "TSA_WD_NotificationStrength_BreakdownHeader".Translate(name, kv.Value.ToString("F0")).ToString() + lines;
            }

            ApplyWorldThreatFromColonyRows(seth);
        }

        /// <summary>
        /// Cheap change-detection hash over near-colony hostile settlements (no ally scans). Captures founding/destruction
        /// (ID set), growth (strength bucket), diplomacy (hostile set), and radius terms (raid/zeal/mid-late + colony tiles).
        /// A changed value flags a full <see cref="RecomputeThreatModel"/> next tick.
        /// </summary>
        private int ComputeThreatFingerprint()
        {
            Faction player = Faction.OfPlayerSilentFail;
            WorldDominationSettings seth = WorldDominationMod.settings;
            if (player == null || seth == null) return 0;

            BuildThreatPlayerColonyTargets(player, seth, tempThreatPlayerColonyTargets);
            if (tempThreatPlayerColonyTargets.Count == 0) return 0;

            bool zealActive = expansionistZealFaction != null && Find.TickManager.TicksGame < expansionistZealExpiryTick;
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (int)cachedEscalationStage;
                hash = hash * 31 + Mathf.RoundToInt(seth.midGameAttackRangeBonusPct * 100f);
                hash = hash * 31 + Mathf.RoundToInt(seth.lateGameAttackRangeBonusPct * 100f);
                hash = hash * 31 + Mathf.RoundToInt(seth.tier1AttackRangeBaseline);
                hash = hash * 31 + Mathf.RoundToInt(seth.tier2AttackRangeBaseline);
                hash = hash * 31 + Mathf.RoundToInt(seth.tier3AttackRangeBaseline);
                hash = hash * 31 + Mathf.RoundToInt(seth.tier4AttackRangeBaseline);
                hash = hash * 31 + Mathf.RoundToInt(seth.attackRangeTimeMaxBonusPct * 100f);
                hash = hash * 31 + Mathf.RoundToInt(seth.attackRangeDaysToMax);
                // Day rollover omitted: CalculateDailyBudget already full-recomputes threat.
                hash = hash * 31 + (zealActive ? (expansionistZealFaction?.loadID ?? 0) : 0);
                for (int i = 0; i < tempThreatPlayerColonyTargets.Count; i++)
                    hash = hash * 31 + tempThreatPlayerColonyTargets[i].Tile;

                var allSettlements = Find.WorldObjects.Settlements;
                for (int si = 0; si < allSettlements.Count; si++)
                {
                    Settlement s = allSettlements[si];
                    if (s == null || s.Tile < 0 || s.Faction == null || s.Faction.IsPlayer) continue;
                    if (s.Faction.def.hidden || s.Faction.defeated) continue;
                    if (!WorldActions_Utils.SafeHostileTo(s.Faction, player)) continue;
                    if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(s)) continue;
                    if (!CanReachAnyPlayerTarget(s, tempThreatPlayerColonyTargets, seth)) continue;

                    var comp = s.GetComponent<CompViralSpread>();
                    if (comp == null) continue;
                    int strengthBucket = Mathf.RoundToInt(WorldActions_Utils.GetAvailableRaidStrength(comp, seth) / ThreatFingerprintStrengthBucket);
                    hash = hash * 31 + s.ID;
                    hash = hash * 31 + strengthBucket;
                    hash = hash * 31 + comp.attackRangeFoundingTick;
                }
                return hash;
            }
        }

        /// <summary>
        /// Full threat recompute (ranked settlement list + per-faction sums + world-threat level). Heavier than
        /// <see cref="ReclassifyWorldThreatAgainstFreshBaseline"/>; call only when reachability/enemy strength may have
        /// changed (fingerprint change, daily, on settings change), not on a frequent timer.
        /// </summary>
        public void RefreshWorldThreatLevelNow()
        {
            RecomputeThreatModel(WorldDominationMod.settings);
        }

        /// <summary>
        /// Cheap intra-day refresh: re-samples the threatened colony's storyteller baseline and reclassifies the cached max-raid.
        /// O(1) (one <see cref="StorytellerUtility.DefaultThreatPointsNow"/> call); no world scan. Tracks wealth drift between full recomputes.
        /// </summary>
        private void ReclassifyWorldThreatAgainstFreshBaseline()
        {
            if (cachedWorldThreatColony == null || !cachedWorldThreatColony.HasMap || cachedWorldThreatMaxRaid <= 0f)
                return; // nothing cached (or colony gone); leave state until the next full recompute
            float baseline = StorytellerUtility.DefaultThreatPointsNow(cachedWorldThreatColony.Map);
            if (baseline <= 0f) return;
            float ratio = cachedWorldThreatMaxRaid / baseline;
            WorldThreatTier tier = ClassifyThreat(ratio, lastWorldThreatTier);
            cachedWorldThreatTier = tier;
            lastWorldThreatTier = tier;
            cachedWorldThreatBaseline = baseline;
            cachedWorldThreatPercent = Mathf.RoundToInt(ratio * 100f);
            cachedWorldThreatBreakdown = BuildWorldThreatBreakdown(baseline, WorldDominationMod.settings);
        }

        /// <summary>Called when mod settings change (e.g. attack range / escalation): full recompute immediately and flag a rescan for next tick.</summary>
        public void NotifyInfluenceSettingsChanged()
        {
            dirtyPowerStats = true;
            RefreshWorldThreatLevelNow();
        }

        private readonly List<(Settlement colony, float baseline, float maxRaid, Settlement scariest)> tempThreatColonyRows = new List<(Settlement, float, float, Settlement)>();

        /// <summary>
        /// Derive the world-threat level from the per-colony rows already populated by <see cref="RecomputeThreatModel"/>
        /// (each row holds its strongest reachable raid). Worst raid/baseline ratio wins. Raw pre-clamp strength on purpose
        /// (full tier spread); the storyteller band may still cap the raid that lands.
        /// </summary>
        private void ApplyWorldThreatFromColonyRows(WorldDominationSettings seth)
        {
            float worstRatio = 0f;
            int worstIdx = -1;
            for (int ci = 0; ci < tempThreatColonyRows.Count; ci++)
            {
                var row = tempThreatColonyRows[ci];
                if (row.maxRaid <= 0f || row.baseline <= 0f) continue;
                float ratio = row.maxRaid / row.baseline;
                if (ratio > worstRatio) { worstRatio = ratio; worstIdx = ci; }
            }

            if (worstIdx < 0)
            {
                SetWorldThreatNone();
                return;
            }

            var worst = tempThreatColonyRows[worstIdx];
            WorldThreatTier tier = ClassifyThreat(worstRatio, lastWorldThreatTier);
            cachedWorldThreatTier = tier;
            lastWorldThreatTier = tier;
            cachedWorldThreatMaxRaid = worst.maxRaid;
            cachedWorldThreatBaseline = worst.baseline;
            cachedWorldThreatPercent = Mathf.RoundToInt(worstRatio * 100f);
            cachedWorldThreatScariest = worst.scariest != null ? new GlobalTargetInfo(worst.scariest) : GlobalTargetInfo.Invalid;
            cachedWorldThreatScariestName = worst.scariest?.LabelCap;
            cachedWorldThreatColony = worst.colony;
            cachedWorldThreatBreakdown = BuildWorldThreatBreakdown(worst.baseline, seth);
        }

        private void SetWorldThreatNone()
        {
            cachedWorldThreatTier = WorldThreatTier.None;
            lastWorldThreatTier = WorldThreatTier.None;
            cachedWorldThreatPercent = 0;
            cachedWorldThreatMaxRaid = 0f;
            cachedWorldThreatBaseline = 0f;
            cachedWorldThreatScariest = GlobalTargetInfo.Invalid;
            cachedWorldThreatScariestName = null;
            cachedWorldThreatColony = null;
            cachedWorldThreatBreakdown = null;
        }

        /// <summary>
        /// Attacker available raid strength plus its attacker-radius allies — identical basis to a real raid (see Raid_Manager).
        /// Also fills <paramref name="allySb"/> with per-ally breakdown lines for the tooltip (cleared first).
        /// </summary>
        private float ComputeRaidStrengthWithAllies(Settlement attacker, WorldDominationSettings seth, Dictionary<Faction, List<WorldObject>> lookup, StringBuilder allySb)
        {
            allySb.Length = 0;
            var comp = attacker.GetComponent<CompViralSpread>();
            if (comp == null) return 0f;
            float total = WorldActions_Utils.GetAvailableRaidStrength(comp, seth);
            // GetReinforcements reuses a shared scratch list; consume it immediately, do not retain the list.
            var allies = Raid_ReinforcementLogic.GetReinforcements(attacker, null, AllyRadiusUtil.GetEffective(attacker, seth, this), lookup, this);
            for (int i = 0; i < allies.Count; i++)
            {
                var ally = allies[i];
                float allyStr = WorldActions_Utils.GetAvailableRaidStrength(ally?.GetComponent<CompViralSpread>(), seth);
                if (allyStr <= 0f) continue;
                total += allyStr;
                allySb.AppendLine("TSA_WD_NotificationStrength_Line".Translate(ally.LabelCap, allyStr.ToString("F0")).ToString());
            }
            return total;
        }

        private static WorldThreatTier ClassifyThreat(float ratio, WorldThreatTier prev)
        {
            // Apply hysteresis: nudge the ratio toward the previous tier so small drift near a boundary does not flip the tier.
            float adjusted = ratio;
            if (prev >= WorldThreatTier.Low)
            {
                WorldThreatTier raw = ClassifyRaw(ratio);
                if (raw > prev) adjusted = ratio - WorldThreatHysteresis * ratio;      // resist stepping up
                else if (raw < prev) adjusted = ratio + WorldThreatHysteresis * ratio; // resist stepping down
            }
            return ClassifyRaw(adjusted);
        }

        private static WorldThreatTier ClassifyRaw(float ratio)
        {
            if (ratio <= 0f) return WorldThreatTier.None;
            if (ratio < 0.80f) return WorldThreatTier.Low;
            if (ratio < 1.20f) return WorldThreatTier.Moderate;
            if (ratio < 1.50f) return WorldThreatTier.Heightened;
            if (ratio < 2.00f) return WorldThreatTier.High;
            return WorldThreatTier.Critical;
        }

        /// <summary>Concise clamp note for the threat tooltip. Null when the storyteller-band clamp is off (raw strength is used).</summary>
        private static string BuildWorldThreatBreakdown(float baseline, WorldDominationSettings seth)
        {
            if (seth == null || seth.alwaysUseStrengthAsRaidPoints) return null;
            RaidPointsHelper.TryGetActiveClampPercents(out int minPct, out int maxPct, out string bandLabel);
            string lower = minPct + "%";
            string upper = maxPct + "%";
            if (!string.IsNullOrEmpty(bandLabel))
                return "TSA_WD_WorldThreat_ClampTooltip_Staged".Translate(lower, upper, baseline.ToString("F0"), bandLabel).ToString();
            return "TSA_WD_WorldThreat_ClampTooltip".Translate(lower, upper, baseline.ToString("F0")).ToString();
        }

        private static void BuildThreatPlayerColonyTargets(Faction player, WorldDominationSettings seth, List<WorldObject> targets)
        {
            targets.Clear();
            if (player == null || seth == null || !seth.allowPlayerRaid) return;

            var settlements = Find.WorldObjects?.Settlements;
            if (settlements == null) return;

            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement settlement = settlements[i];
                if (settlement == null || settlement.Faction != player || settlement.Tile < 0 || !settlement.HasMap) continue;
                if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(settlement)) continue;
                if (settlement.GetComponent<CompViralSpread>() == null) continue;

                targets.Add(settlement);
            }
        }

        /// <summary>Public wrapper for raid bias / UI: can this settlement reach any player colony or outpost target.</summary>
        public bool CanReachAnyPlayerTargetPublic(Settlement attacker, WorldDominationSettings seth)
        {
            Faction player = Faction.OfPlayerSilentFail;
            if (player == null || seth == null) return false;
            BuildThreatPlayerColonyTargets(player, seth, tempThreatPlayerColonyTargets);
            return CanReachAnyPlayerTarget(attacker, tempThreatPlayerColonyTargets, seth);
        }

        private bool CanReachAnyPlayerTarget(Settlement attacker, List<WorldObject> playerTargets, WorldDominationSettings seth)
        {
            if (attacker == null || attacker.Tile < 0 || seth == null) return false;

            float raidRange = SettlementAttackRangeUtil.GetNpcSettlementAttackRangeWithZeal(attacker, seth, this);

            for (int i = 0; i < playerTargets.Count; i++)
            {
                WorldObject target = playerTargets[i];
                if (target == null || target.Tile < 0 || target.Destroyed) continue;

                float dist = WorldActions_Utils.GetDistance(attacker.Tile, target.Tile, this);
                if (dist <= raidRange)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Launch/clash snapshot of a real caravan. Humans and mechs use the Pawns-tab formula
        /// (50 + max(Shooting, Melee) * 7.5). Animals and vehicles use kindDef.combatPower. Floor 50.
        /// Inline skill reads only; no FromPawn, inventory, or hediff walks.
        /// </summary>
        public static float ComputeCaravanMortarStrengthPool(Caravan caravan)
        {
            float sum = 0f;
            var list = caravan?.PawnsListForReading;
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    Pawn p = list[i];
                    if (p == null || p.Dead) continue;
                    PlayerPawnSortCategory cat = PlayerPawnRosterUtility.ClassifyPawn(p);
                    if (cat == PlayerPawnSortCategory.Human)
                    {
                        SkillRecord shoot = p.skills?.GetSkill(SkillDefOf.Shooting);
                        SkillRecord melee = p.skills?.GetSkill(SkillDefOf.Melee);
                        int shootLv = shoot != null ? shoot.levelInt : 0;
                        int meleeLv = melee != null ? melee.levelInt : 0;
                        sum += 50f + Mathf.Max(shootLv, meleeLv) * 7.5f;
                    }
                    else if (cat == PlayerPawnSortCategory.Mechanoid)
                    {
                        int shootLv = OutpostMechanoidSkillUtil.EquivalentSkillLevel(p, SkillDefOf.Shooting);
                        int meleeLv = OutpostMechanoidSkillUtil.EquivalentSkillLevel(p, SkillDefOf.Melee);
                        sum += 50f + Mathf.Max(shootLv, meleeLv) * 7.5f;
                    }
                    else
                    {
                        sum += p.kindDef?.combatPower ?? 0f;
                    }
                }
            }
            return Mathf.Max(sum, 50f);
        }

        /// <summary>Apply shell potency to the caravan's tracked pool; <paramref name="depleted"/> means strength reached zero and the caravan should be destroyed.</summary>
        public void ApplyMortarShellToCaravanVitality(Caravan caravan, float shellPotency, out float beforePool, out float afterPool, out bool depleted)
        {
            depleted = false;
            beforePool = 0f;
            afterPool = 0f;
            if (caravan == null || caravan.Destroyed) return;
            int id = caravan.ID;
            if (!caravanMortarVitalityRemaining.TryGetValue(id, out float pool))
                pool = ComputeCaravanMortarStrengthPool(caravan);
            beforePool = pool;
            afterPool = Mathf.Max(0f, pool - Mathf.Max(0f, shellPotency));
            depleted = afterPool <= 0.001f;
            if (depleted)
                caravanMortarVitalityRemaining.Remove(id);
            else
                caravanMortarVitalityRemaining[id] = afterPool;
        }

        public void ClearCaravanMortarVitality(int caravanId) => caravanMortarVitalityRemaining?.Remove(caravanId);

        /// <summary>Restacks by refreshing expiry from now (no multiplicative duration stack).</summary>
        public void SetPlayerBribeCeasefire(Faction faction, int days)
        {
            if (faction == null || faction.IsPlayer || days <= 0) return;
            if (playerBribeCeasefireTicksExpiry == null)
                playerBribeCeasefireTicksExpiry = new Dictionary<int, int>();
            int expiry = Find.TickManager.TicksGame + Mathf.Max(1, days) * 60000;
            playerBribeCeasefireTicksExpiry[faction.loadID] = expiry;
        }

        public bool IsPlayerBribeCeasefireActive(Faction faction)
        {
            if (faction == null || playerBribeCeasefireTicksExpiry == null) return false;
            if (!playerBribeCeasefireTicksExpiry.TryGetValue(faction.loadID, out int expiry)) return false;
            return Find.TickManager.TicksGame < expiry;
        }

        public bool TryGetPlayerBribeCeasefireDaysRemaining(Faction faction, out float daysRemaining)
        {
            daysRemaining = 0f;
            if (faction == null || playerBribeCeasefireTicksExpiry == null) return false;
            if (!playerBribeCeasefireTicksExpiry.TryGetValue(faction.loadID, out int expiry)) return false;
            int remaining = expiry - Find.TickManager.TicksGame;
            if (remaining <= 0) return false;
            daysRemaining = remaining / 60000f;
            return true;
        }

        private void TickPlayerBribeCeasefireExpiry(int currentTick)
        {
            if (playerBribeCeasefireTicksExpiry == null || playerBribeCeasefireTicksExpiry.Count == 0) return;
            List<int> expired = null;
            foreach (var kv in playerBribeCeasefireTicksExpiry)
            {
                if (currentTick < kv.Value) continue;
                expired ??= new List<int>();
                expired.Add(kv.Key);
            }
            if (expired == null) return;

            bool notify = WorldDominationMod.settings?.notifyBribeCeasefireExpired
                ?? WorldDominationSettings.DefNotifyBribeCeasefireExpired;
            for (int i = 0; i < expired.Count; i++)
            {
                int loadId = expired[i];
                playerBribeCeasefireTicksExpiry.Remove(loadId);
                if (!notify) continue;
                Faction fac = null;
                var all = Find.FactionManager?.AllFactionsListForReading;
                if (all != null)
                {
                    for (int f = 0; f < all.Count; f++)
                    {
                        if (all[f] != null && all[f].loadID == loadId)
                        {
                            fac = all[f];
                            break;
                        }
                    }
                }
                string name = fac?.Name ?? "?";
                Find.LetterStack.ReceiveLetter(
                    "TSA_WD_Bribe_CeasefireExpiredLabel".Translate(),
                    "TSA_WD_Bribe_CeasefireExpiredText".Translate(name),
                    LetterDefOf.NeutralEvent);
            }
        }

        /// <summary>Restacks expiry for the (attacker, priorityTarget) pair from now.</summary>
        public void SetQuestRaidBias(Faction attacker, Faction priorityTarget, int days)
        {
            if (attacker == null || priorityTarget == null || days <= 0) return;
            if (questRaidBiasEntries == null)
                questRaidBiasEntries = new List<QuestRaidBiasEntry>();

            int now = Find.TickManager.TicksGame;
            int expiry = now + Mathf.Max(1, days) * 60000;
            int aId = attacker.loadID;
            int tId = priorityTarget.loadID;

            for (int i = 0; i < questRaidBiasEntries.Count; i++)
            {
                QuestRaidBiasEntry e = questRaidBiasEntries[i];
                if (e == null) continue;
                if (e.attackerLoadId == aId && e.priorityTargetLoadId == tId)
                {
                    e.expiryTick = expiry;
                    return;
                }
            }

            questRaidBiasEntries.Add(new QuestRaidBiasEntry
            {
                attackerLoadId = aId,
                priorityTargetLoadId = tId,
                expiryTick = expiry
            });
        }

        public bool IsQuestRaidBiasActive(Faction attacker, Faction priorityTarget = null)
        {
            if (attacker == null || questRaidBiasEntries == null || questRaidBiasEntries.Count == 0)
                return false;
            int now = Find.TickManager.TicksGame;
            int aId = attacker.loadID;
            int tId = priorityTarget?.loadID ?? int.MinValue;
            for (int i = 0; i < questRaidBiasEntries.Count; i++)
            {
                QuestRaidBiasEntry e = questRaidBiasEntries[i];
                if (e == null || e.IsExpired(now)) continue;
                if (e.attackerLoadId != aId) continue;
                if (priorityTarget == null || e.priorityTargetLoadId == tId)
                    return true;
            }
            return false;
        }

        public HashSet<int> GetQuestRaidBiasPriorityTargetLoadIds(Faction attacker)
        {
            var result = new HashSet<int>();
            if (attacker == null || questRaidBiasEntries == null) return result;
            int now = Find.TickManager.TicksGame;
            int aId = attacker.loadID;
            for (int i = 0; i < questRaidBiasEntries.Count; i++)
            {
                QuestRaidBiasEntry e = questRaidBiasEntries[i];
                if (e == null || e.IsExpired(now)) continue;
                if (e.attackerLoadId == aId)
                    result.Add(e.priorityTargetLoadId);
            }
            return result;
        }

        public List<QuestRaidBiasEntry> GetActiveQuestRaidBiasEntries()
        {
            var result = new List<QuestRaidBiasEntry>();
            if (questRaidBiasEntries == null) return result;
            int now = Find.TickManager.TicksGame;
            for (int i = 0; i < questRaidBiasEntries.Count; i++)
            {
                QuestRaidBiasEntry e = questRaidBiasEntries[i];
                if (e != null && !e.IsExpired(now))
                    result.Add(e);
            }
            return result;
        }

        public void ClearQuestRaidBias(Faction attacker, Faction priorityTarget = null)
        {
            if (attacker == null || questRaidBiasEntries == null || questRaidBiasEntries.Count == 0)
                return;
            int aId = attacker.loadID;
            int tId = priorityTarget?.loadID ?? int.MinValue;
            for (int i = questRaidBiasEntries.Count - 1; i >= 0; i--)
            {
                QuestRaidBiasEntry e = questRaidBiasEntries[i];
                if (e == null || e.attackerLoadId != aId) continue;
                if (priorityTarget == null || e.priorityTargetLoadId == tId)
                    questRaidBiasEntries.RemoveAt(i);
            }
        }

        private void TickQuestRaidBiasExpiry(int currentTick)
        {
            if (questRaidBiasEntries == null || questRaidBiasEntries.Count == 0) return;
            for (int i = questRaidBiasEntries.Count - 1; i >= 0; i--)
            {
                QuestRaidBiasEntry e = questRaidBiasEntries[i];
                if (e == null || e.IsExpired(currentTick))
                    questRaidBiasEntries.RemoveAt(i);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref ActionLog, "ActionLog", LookMode.Deep);
            Scribe_Collections.Look(ref factionThreats, "factionThreats", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref factionBreakdowns, "factionBreakdowns", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref diplomacyFreezeTicks, "diplomacyFreezeTicks", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref playerBribeCeasefireTicksExpiry, "playerBribeCeasefireTicksExpiry", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref questRaidBiasEntries, "questRaidBiasEntries", LookMode.Deep);
            Scribe_Collections.Look(ref distanceCache, "distanceCache", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref spaceTileCache, "spaceTileCache", LookMode.Value);
            Scribe_Collections.Look(ref caravanMortarVitalityRemaining, "caravanMortarVitalityRemaining", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref playerWdRaidLaunchTicks, "playerWdRaidLaunchTicks", LookMode.Value);

            Scribe_References.Look(ref currentWorldLeader, "currentWorldLeader");
            Scribe_References.Look(ref currentWeakestUnderdog, "currentWeakestUnderdog");

            Scribe_References.Look(ref expansionistZealFaction, "expansionistZealFaction");
            Scribe_Values.Look(ref expansionistZealExpiryTick, "expansionistZealExpiryTick", -1);
            Scribe_Values.Look(ref expansionistZealCooldownTick, "expansionistZealCooldownTick", -1);

            Scribe_Values.Look(ref leaderHandicapExpiryTick, "leaderHandicapExpiryTick", -1);
            Scribe_Values.Look(ref leaderHandicapCooldownTick, "leaderHandicapCooldownTick", -1);

            Scribe_Values.Look(ref underdogBuffExpiryTick, "underdogBuffExpiryTick", -1);
            Scribe_Values.Look(ref underdogBuffCooldownTick, "underdogBuffCooldownTick", -1);

            Scribe_Values.Look(ref antiLeaderCoalitionCooldownTick, "antiLeaderCoalitionCooldownTick", -1);
            Scribe_References.Look(ref antiLeaderCoalitionTarget, "antiLeaderCoalitionTarget");
            Scribe_Collections.Look(ref antiLeaderCoalitionMembers, "antiLeaderCoalitionMembers", LookMode.Reference);
            Scribe_Values.Look(ref antiLeaderCoalitionExpiryTick, "antiLeaderCoalitionExpiryTick", -1);
            Scribe_Collections.Look(ref antiLeaderCoalitionPriorRelations, "antiLeaderCoalitionPriorRelations", LookMode.Deep);

            Scribe_Values.Look(ref cachedPlayerOutpostStrength, "cachedPlayerOutpostStrength", 0f);
            Scribe_Values.Look(ref cachedPlayerGlobalShare, "cachedPlayerGlobalShare", 0f);
            Scribe_Values.Look(ref cachedLateGameModifierActive, "cachedLateGameModifierActive", false);
            Scribe_Values.Look(ref cachedMidGameModifierActive, "cachedMidGameModifierActive", false);
            Scribe_Values.Look(ref cachedEscalationStage, "cachedEscalationStage", WdEscalationStage.None);
            Scribe_Values.Look(ref outpostUpkeepNextTick, "outpostUpkeepNextTick", -1);
            Scribe_Values.Look(ref lastWorldThreatTier, "lastWorldThreatTier", WorldThreatTier.None);

            Scribe_Values.Look(ref ticksUntilNextAction, "ticksUntilNextAction", 0);
            Scribe_Values.Look(ref ticksPerAction, "ticksPerAction", 0);
            Scribe_Values.Look(ref firstTickRun, "firstTickRun", false);

            Scribe_Collections.Look(ref vanillaNpcSettlementCountsByFactionLoadId, "wdVanillaNpcSettlementCounts", LookMode.Value, LookMode.Value);
            Scribe_Values.Look(ref vanillaNpcSettlementTotal, "wdVanillaNpcSettlementTotal", -1);
            Scribe_Values.Look(ref vanillaNpcSettlementSnapshotTaken, "wdVanillaNpcSettlementSnapshotTaken", false);
            Scribe_Values.Look(ref worldSetupTargetNpcSettlements, "wdWorldSetupTargetNpcSettlements", -1);
            Scribe_Collections.Look(ref worldSetupFactionSettlementShares, "wdWorldSetupFactionSettlementShares", LookMode.Value, LookMode.Value);
            Scribe_Values.Look(ref worldSetupFactionSharesInitialized, "wdWorldSetupFactionSharesInitialized", false);

            if (diplomacyFreezeTicks == null) diplomacyFreezeTicks = new Dictionary<long, int>();
            if (playerBribeCeasefireTicksExpiry == null) playerBribeCeasefireTicksExpiry = new Dictionary<int, int>();
            if (questRaidBiasEntries == null) questRaidBiasEntries = new List<QuestRaidBiasEntry>();
            if (distanceCache == null) distanceCache = new Dictionary<long, int>();
            if (spaceTileCache == null) spaceTileCache = new HashSet<int>();
            if (factionThreats == null) factionThreats = new Dictionary<int, float>();
            if (factionBreakdowns == null) factionBreakdowns = new Dictionary<int, string>();
            if (caravanMortarVitalityRemaining == null) caravanMortarVitalityRemaining = new Dictionary<int, float>();
            if (antiLeaderCoalitionMembers == null) antiLeaderCoalitionMembers = new List<Faction>();
            if (antiLeaderCoalitionPriorRelations == null) antiLeaderCoalitionPriorRelations = new List<AntiLeaderCoalitionPriorRelation>();
            if (playerWdRaidLaunchTicks == null) playerWdRaidLaunchTicks = new List<int>();
            if (vanillaNpcSettlementCountsByFactionLoadId == null)
                vanillaNpcSettlementCountsByFactionLoadId = new Dictionary<int, int>();
            if (worldSetupFactionSettlementShares == null)
                worldSetupFactionSettlementShares = new Dictionary<int, float>();
        }

        public List<SpreadLogEntry> GetLog() => ActionLog;

        private void PurgeLegacyWorldThreatManager()
        {
            if (world?.components == null) return;
            for (int i = world.components.Count - 1; i >= 0; i--)
            {
                if (world.components[i] is WorldThreatManager legacy)
                {
                    ImportLegacyWorldThreatTier(legacy.LegacyThreatCategory);
                    world.components.RemoveAt(i);
                }
            }
        }
    }
}