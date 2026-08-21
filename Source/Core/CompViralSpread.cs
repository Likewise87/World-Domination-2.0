using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;
using UnityEngine;
using System.Reflection;
using System.Text;
namespace TSA_WorldDomination
{
    public enum SettlementTier { T1, T2, T3, T4 }

    public enum TraderArrivalRewardOutcome
    {
        NoEffect,
        StrengthOnly,
        StrengthAndTierUp
    }

    public class CompProperties_ViralSpread : WorldObjectCompProperties
    {
        public CompProperties_ViralSpread() => this.compClass = typeof(CompViralSpread);
    }

    public class CompViralSpread : WorldObjectComp, IDefensiveInterceptor
    {
        public static readonly Color RaidVulnerableColor = new Color(1f, 0.85f, 0.2f);

        public static string GetRaidVulnerableLabel()
        {
            return "TSA_WD_Status_DefVulnerable".Translate().ToString().Colorize(RaidVulnerableColor);
        }

        public static string GetColonyRaidVulnerableLabel()
        {
            return "TSA_WD_Status_ColonyDefVulnerable".Translate().ToString().Colorize(RaidVulnerableColor);
        }

        /// <summary>Colored raid-protection line for player colony or outpost inspect / dashboard.</summary>
        public static string FormatRaidProtectionStatusLine(CompViralSpread comp, bool playerColonySettlement)
        {
            if (comp == null) return "";
            if (comp.IsDefenseOnCooldown)
            {
                float daysLeft = Mathf.Max(0f, (comp.defenseCooldownTick - Find.TickManager.TicksGame) / 60000f);
                return "TSA_WD_Inspect_DefenseCD".Translate(daysLeft.ToString("F1")).ToString().Colorize(Color.green);
            }
            return playerColonySettlement ? GetColonyRaidVulnerableLabel() : GetRaidVulnerableLabel();
        }

        public static string NormalizeRaidVulnerableLabelColor(string inspect)
        {
            if (string.IsNullOrEmpty(inspect)) return inspect;
            string label = "TSA_WD_Status_DefVulnerable".Translate().ToString();
            if (string.IsNullOrEmpty(label) || !inspect.Contains(label)) return inspect;
            if (inspect.Contains(GetRaidVulnerableLabel())) return inspect;
            return inspect.Replace(label, GetRaidVulnerableLabel());
        }

        /// <summary>Re-applies raid-protection colors on player colony inspect strings (comps + settlement postfix).</summary>
        public static string ApplyPlayerSettlementInspectColors(string inspect)
        {
            if (string.IsNullOrEmpty(inspect)) return inspect;
            inspect = NormalizeRaidVulnerableLabelColor(inspect);

            string colonyVuln = "TSA_WD_Status_ColonyDefVulnerable".Translate().ToString();
            if (!string.IsNullOrEmpty(colonyVuln) && inspect.Contains(colonyVuln) && !inspect.Contains(GetColonyRaidVulnerableLabel()))
                inspect = inspect.Replace(colonyVuln, GetColonyRaidVulnerableLabel());

            string defVuln = "TSA_WD_Status_DefVulnerable".Translate().ToString();
            if (!string.IsNullOrEmpty(defVuln) && inspect.Contains(defVuln) && !inspect.Contains(GetRaidVulnerableLabel()))
                inspect = inspect.Replace(defVuln, GetRaidVulnerableLabel());

            string cdPrefix = RaidProtectionCooldownPrefix();
            if (!string.IsNullOrEmpty(cdPrefix))
            {
                string[] lines = inspect.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (!line.Contains(cdPrefix) || line.Contains("<color")) continue;
                    lines[i] = line.TrimEnd().Colorize(Color.green);
                }
                inspect = string.Join("\n", lines);
            }
            return inspect;
        }

        private static string RaidProtectionCooldownPrefix()
        {
            string sample = "TSA_WD_Inspect_DefenseCD".Translate("0").ToString();
            int brace = sample.IndexOf('{');
            if (brace < 0) brace = sample.Length;
            return sample.Substring(0, brace).TrimEnd();
        }

        /// <summary>True while this comp is registered with <see cref="WorldComponent_InterceptionScheduler"/>.
        /// Avoids reregistration per tick; updated when tier/faction becomes (or stops being) eligible for auto-fire.</summary>
        private bool registeredAsInterceptor;

        // --- IDefensiveInterceptor (T4 settlements auto-fire mortars at passing hostile travelers; static fallback to nearest hostile settlement) ---
        WorldObject IDefensiveInterceptor.Self => parent;
        PlanetTile IDefensiveInterceptor.InterceptorTile => parent != null ? parent.Tile : default(PlanetTile);
        Faction IDefensiveInterceptor.InterceptorFaction => parent?.Faction;
        float IDefensiveInterceptor.InterceptorRange
        {
            get
            {
                float mortar = WorldDominationMod.settings?.npcMortarRange ?? WorldDominationSettings.DefNpcMortarRange;
                float aa = AntiAirFireUtils.GetNpcAntiAirMaxRangeTiles();
                bool mortarOn = IsSettlementMortarAutoActive && IsSettlementMortarInterceptorEligible();
                bool aaOn = IsSettlementAntiAirAutoActive && IsSettlementAntiAirEligible();
                if (mortarOn && aaOn) return Mathf.Max(mortar, aa);
                if (aaOn) return aa;
                return mortar;
            }
        }
        MissionMask IDefensiveInterceptor.InterceptorMissionMask
        {
            get
            {
                if (IsSettlementMortarInterceptorEligible() && IsSettlementMortarAutoActive)
                    return MissionMask.All;
                // AA-only T4s still need a mask so the scheduler re-checks airborne pods mid-flight.
                if (IsSettlementAntiAirEligible() && IsSettlementAntiAirAutoActive)
                    return MissionMask.Raider;
                return MissionMask.None;
            }
        }
        bool IDefensiveInterceptor.InterceptorCanFireNow()
        {
            if (IsSettlementMortarInterceptorEligible() && IsSettlementMortarAutoActive && !IsMortarOnCooldown
                && NpcT4GlobalFireStagger.IsMortarSlotOpen())
                return true;
            if (IsSettlementAntiAirEligible() && IsSettlementAntiAirAutoActive && !IsAntiAirOnCooldown
                && NpcT4GlobalFireStagger.CanQueueNpcAa())
                return true;
            return false;
        }
        bool IDefensiveInterceptor.InterceptorCanTargetPlayer => CanTargetPlayerNow() || CanTargetPlayerWithAntiAirNow();

        /// <summary>
        /// Live lookup, deliberately uncached. A wrong manager instance here silently disables T4 mortar/anti-air
        /// against the player for an entire session, which is far worse than one small component-list scan on a
        /// path that only runs when an airborne target wakes the interceptor.
        /// </summary>
        private static WorldComponent_SpreadManager LiveSpreadManager =>
            Find.World?.GetComponent<WorldComponent_SpreadManager>();

        /// <summary>T4 settlement mortars may target the player when the stage flag is on (Mid and/or Late).</summary>
        private bool CanTargetPlayerNow()
        {
            var manager = LiveSpreadManager;
            return WdEscalation.CanTargetPlayerWithT4Mortar(WorldDominationMod.settings, WdEscalation.GetCachedStage(manager));
        }

        private bool CanTargetPlayerWithAntiAirNow()
        {
            var manager = LiveSpreadManager;
            return WdEscalation.CanTargetPlayerWithT4AntiAir(WorldDominationMod.settings, WdEscalation.GetCachedStage(manager));
        }

        /// <summary>
        /// Verbose diagnostics for the AA-vs-player gate. Reports every sub-condition plus object identities, so a
        /// failure names its own cause instead of needing a second guess: which comp instance answered, whether its
        /// parent is still spawned, and whether the cached / getter / live manager lookups agree.
        /// </summary>
        public string DebugDescribePlayerGate()
        {
            var seth = WorldDominationMod.settings;
            bool aaFlag = seth?.enableT4SettlementAntiAir ?? WorldDominationSettings.DefEnableT4SettlementAntiAir;
            bool mortarFlag = seth?.enableT4SettlementMortar ?? WorldDominationSettings.DefEnableT4SettlementMortar;

            World world = Find.World;
            var live = world?.GetComponent<WorldComponent_SpreadManager>();
            var viaGetter = SpreadManager;

            int mgrCount = 0;
            if (world?.components != null)
            {
                for (int i = 0; i < world.components.Count; i++)
                    if (world.components[i] is WorldComponent_SpreadManager) mgrCount++;
            }

            string Id(object o) => o == null ? "null" : "#" + o.GetHashCode().ToString("X");

            return $"aaFlag={aaFlag} mortarFlag={mortarFlag} "
                + $"lateGameLive={(live?.cachedLateGameModifierActive ?? false)} "
                + $"lateGameGetter={(viaGetter?.cachedLateGameModifierActive ?? false)} "
                + $"mgrLive={Id(live)} mgrGetter={Id(viaGetter)} mgrCount={mgrCount} "
                + $"world={Id(world)} "
                + $"comp={Id(this)} parent={Id(parent)} parentSpawned={parent?.Spawned} parentDestroyed={parent?.Destroyed} "
                + $"tier={tier} aaEligible={IsSettlementAntiAirEligible()} aaAuto={IsSettlementAntiAirAutoActive}";
        }

        void IDefensiveInterceptor.InterceptorFire(WorldObject_Traveler target, float approxTileDist)
        {
            if (parent is not Settlement s) return;
            if (target != null && AntiAirFireUtils.IsAirborneAaTarget(target))
            {
                AntiAirFireUtils.TryEngageFromSettlement(s, target);
                return;
            }
            if (!IsSettlementMortarAutoActive) return;
            MortarFireUtils.FireNpcSettlementAtTraveler(s, target, approxTileDist);
        }
        void IDefensiveInterceptor.InterceptorNoTargetFire()
        {
            // No hostile caravan in range: fall back to shelling the nearest hostile settlement / AT Turret
            // immediately (subject only to the mortar cooldown). Player outposts and player AT Turrets are only
            // valid static targets while the late-game modifier is active.
            if (!(parent is Settlement origin)) return;
            if (!IsSettlementMortarAutoActive || !IsSettlementMortarInterceptorEligible()) return;
            if (IsMortarOnCooldown || !NpcT4GlobalFireStagger.IsMortarSlotOpen()) return;
            bool canTargetPlayer = CanTargetPlayerNow();
            WorldObject staticTarget = FindNearestStaticMortarTarget(origin, canTargetPlayer, out float dist);
            if (staticTarget == null) return;
            MortarFireUtils.FireNpcSettlementAtStaticTarget(origin, staticTarget, dist);
        }

        /// <summary>
        /// Nearest hostile non-player settlement or AT Turret (always), or player outpost / player AT Turret
        /// (only when <paramref name="canTargetPlayer"/>) within mortar range.
        /// </summary>
        private WorldObject FindNearestStaticMortarTarget(Settlement origin, bool canTargetPlayer, out float bestDist)
        {
            bestDist = float.MaxValue;
            WorldObject best = null;
            Faction iFaction = origin.Faction;
            if (iFaction == null) return null;
            var seth = WorldDominationMod.settings;
            float range = seth?.npcMortarRange ?? WorldDominationSettings.DefNpcMortarRange;
            var manager = SpreadManager;
            Faction player = Faction.OfPlayerSilentFail;

            var settlements = Find.WorldObjects.Settlements;
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement s = settlements[i];
                if (s == origin || s.Faction == null || s.Faction.IsPlayer) continue;
                if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(s)) continue;
                if (!WorldActions_Utils.SafeHostileTo(s.Faction, iFaction)) continue;
                float d = manager != null
                    ? (float)WorldActions_Utils.GetDistance(origin.Tile, s.Tile, manager)
                    : Find.WorldGrid.ApproxDistanceInTiles(origin.Tile, s.Tile);
                if (d > range || d >= bestDist) continue;
                bestDist = d;
                best = s;
            }

            var all = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < all.Count; i++)
            {
                WorldObject wo = all[i];
                if (wo is WorldObject_AT_Turret at)
                {
                    if (at.Destroyed || at.Faction == null) continue;
                    if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(at)) continue;
                    if (!WorldActions_Utils.SafeHostileTo(at.Faction, iFaction)) continue;
                    if (at.Faction.IsPlayer && (!canTargetPlayer || player == null)) continue;
                    float d = manager != null
                        ? (float)WorldActions_Utils.GetDistance(origin.Tile, at.Tile, manager)
                        : Find.WorldGrid.ApproxDistanceInTiles(origin.Tile, at.Tile);
                    if (d > range || d >= bestDist) continue;
                    bestDist = d;
                    best = at;
                    continue;
                }

                if (!canTargetPlayer || player == null || !WorldActions_Utils.SafeHostileTo(player, iFaction))
                    continue;
                if (!(wo is WorldObject_WD_Outpost o) || o.Faction != player) continue;
                if (!WorldActions_Utils.IsWdSurfaceWorldObject(o)) continue;
                float dOutpost = manager != null
                    ? (float)WorldActions_Utils.GetDistance(origin.Tile, o.Tile, manager)
                    : Find.WorldGrid.ApproxDistanceInTiles(origin.Tile, o.Tile);
                if (dOutpost > range || dOutpost >= bestDist) continue;
                bestDist = dOutpost;
                best = o;
            }

            return best;
        }

        /// <summary>Cheap eligibility gate for T4 settlement mortar interception. Excludes player and non-settlements; hostility is checked per-target during the scan.</summary>
        private bool IsSettlementMortarInterceptorEligible()
        {
            if (!(WorldDominationMod.settings?.enableNpcT4Mortar ?? WorldDominationSettings.DefEnableNpcT4Mortar)) return false;
            return IsSettlementT4TurretBaseEligible();
        }

        public static bool IsSettlementAntiAirEligible(Settlement settlement)
        {
            if (settlement == null || settlement.Destroyed) return false;
            var comp = settlement.GetComponent<CompViralSpread>();
            return comp != null && comp.IsSettlementAntiAirEligible();
        }

        public bool IsSettlementAntiAirEligible()
        {
            if (!(WorldDominationMod.settings?.enableNpcT4AntiAir ?? WorldDominationSettings.DefEnableNpcT4AntiAir)) return false;
            return IsSettlementT4TurretBaseEligible();
        }

        private bool IsSettlementT4TurretBaseEligible()
        {
            if (parent == null || parent.Destroyed) return false;
            if (!(parent is Settlement)) return false;
            if (tier != SettlementTier.T4) return false;
            Faction f = parent.Faction;
            if (f == null || f.IsPlayer) return false;
            TechLevel minTech = WorldDominationMod.settings?.npcT4MortarMinTechLevel ?? WorldDominationSettings.DefNpcT4MortarMinTechLevel;
            if (f.def.techLevel < minTech) return false;
            return true;
        }

        public bool IsSettlementMortarAutoActive => t4MortarAutoActive;
        public bool IsSettlementAntiAirAutoActive => t4AntiAirAutoActive;

        public void SetT4MortarAutoActive(bool on)
        {
            t4MortarAutoActive = on;
            UpdateInterceptorRegistration();
        }

        public void SetT4AntiAirAutoActive(bool on)
        {
            t4AntiAirAutoActive = on;
            UpdateInterceptorRegistration();
        }

        public static bool IsSettlementAntiAirReady(Settlement settlement, out CompViralSpread comp, out float skillEquiv)
        {
            comp = null;
            skillEquiv = 0f;
            if (settlement == null || settlement.Destroyed) return false;
            comp = settlement.GetComponent<CompViralSpread>();
            if (comp == null) return false;
            if (!comp.IsSettlementAntiAirEligible()) return false;
            if (!comp.IsSettlementAntiAirAutoActive) return false;
            if (comp.IsAntiAirOnCooldown) return false;
            if (settlement.Faction != null && settlement.Faction.IsPlayer) return false;
            skillEquiv = WorldDominationMod.settings?.npcAntiAirSkillEquivalent ?? WorldDominationSettings.DefNpcAntiAirSkillEquivalent;
            return skillEquiv > 0f;
        }

        /// <summary>Keep scheduler registration in sync with tier/faction eligibility (event-driven; not polled every CompTick).</summary>
        private void UpdateInterceptorRegistration()
        {
            bool eligible = (IsSettlementMortarInterceptorEligible() && IsSettlementMortarAutoActive)
                || (IsSettlementAntiAirEligible() && IsSettlementAntiAirAutoActive);
            if (eligible == registeredAsInterceptor) return;
            var sched = WorldComponent_InterceptionScheduler.Current;
            if (sched == null) return;
            if (eligible) sched.RegisterInterceptor(this);
            else sched.UnregisterInterceptor(this);
            registeredAsInterceptor = eligible;
        }

        /// <summary>
        /// Always resolved live. Comps are constructed during world generation / load, before the world's final
        /// component list exists, so any instance captured at that point can differ from the one the game actually
        /// ticks. Caching it silently pinned a dead manager and made late-game reads (and zeal, action-day and
        /// distance-cache reads) wrong for the whole session.
        /// </summary>
        private WorldComponent_SpreadManager SpreadManager => LiveSpreadManager;

        public SettlementTier tier = SettlementTier.T1;
        public string subType = "";
        public float offensiveStrength;
        public float defensiveStrength;
        /// <summary>Backward-compatible alias used by legacy callers; maps to offensive strength.</summary>
        public float strength
        {
            get => offensiveStrength;
            set
            {
                if (IsPlayerMapSettlement) return;
                offensiveStrength = value;
            }
        }

        // --- INDEPENDENT COOLDOWN CHANNELS ---
        public int raidCooldownTick = -1;       // Actor: Cannot launch raids
        public int expansionCooldownTick = -1;  // Actor: Cannot expand
        public int roadCooldownTick = -1;       // Actor: Cannot build roads
        public int fortifyCooldownTick = -1;  // Actor: Cannot fortify
        /// <summary>Set once per day by orchestrator: hostile in attack range (Fortify eligibility).</summary>
        public bool fortifyThreatenedToday;
        /// <summary>Daily: nearest hostile used for frontier facing (not scribed).</summary>
        public WorldObject fortifyNearestHostile;
        /// <summary>Daily: true if this settlement is on its territory component's frontier toward its nearest hostile.</summary>
        public bool fortifyIsFrontier;
        /// <summary>Daily: contiguous same-faction territory id for fortify (not scribed).</summary>
        public int fortifyTerritoryId = -1;
        /// <summary>Faction loadID when fortifications were attributed; used to clear on faction change.</summary>
        private int fortifyBuilderFactionId = -1;
        /// <summary>NPC Fortify wave: try to place at most one AT Turret beside a road block this action.</summary>
        /// <summary>Legacy save field; unused. Piggyback AT-on-road-block was removed.</summary>
        public bool fortifyTurretPending;
        public int traderCooldownTick = -1;     // Actor: Cannot launch trader caravans
        /// <summary>Actor: Feature C origin ambush cooldown. This settlement cannot launch another ambush until Ready. Abort/refund of a chase does not clear it.</summary>
        public int ambushCooldownTick = -1;
        /// <summary>Target (player map colony): Earliest tick this settlement can be chosen again as a WD trader destination. Set when a trader is dispatched here.</summary>
        public int playerColonyWdTraderCooldownTick = -1;
        /// <summary>TicksGame this settlement last had one of its outgoing Trader travelers destroyed via ambush/Rapid Response/mortar interception (not ordinary arrival). Drives Feature E's temporary full-force escort strength. -1 = never.</summary>
        public int lastCaravanInterceptedTick = -99999;

        public int defenseCooldownTick = -1;    // Target: Protected from Raids/Caravans
        /// <summary>TicksGame when this player colony was last picked as a WD raid target (experimental soften clock). Not raid arrival. -1 = never stamped.</summary>
        public int lastPlayerColonyWdRaidPickTick = -1;
        public int incidentCooldownTick = -1;   // Target: Protected from Minor/Major incidents
        public int mortarCooldownTick = -1;     // Actor: Shared cooldown for manual mortar strikes and defensive auto-fire
        public int antiAirCooldownTick = -1;    // Actor: Separate short cooldown for AA flak engagements
        private bool t4MortarAutoActive = true;
        private bool t4AntiAirAutoActive = true;
        /// <summary>Player map colony: convert map food into travel pemmican when transferring pawns away. Default on.</summary>
        public bool autoFeedTransferredPawns = true;

        public int espionageCooldownUntilTick = -1;
        public int aidCooldownUntilTick = -1;

        // --- DAILY CAPACITY TRACKING ---
        public int actionsTakenToday = 0;
        public int lastActionDay = -1;

        public float cachedRadius = -1f;
        private int lastRadiusUpdateTick = -9999;

        private bool outpostInitialized = false;
        /// <summary>One-time fix: older versions set player comps to subType Excluded via Initialize + IsExcludedFaction(player).</summary>
        private bool repairedMisclassifiedPlayerSubType;
        /// <summary>True after the one-time starting raid shield for this player map colony has been applied (or skipped on load).</summary>
        private bool appliedInitialPlayerColonyShield;
        /// <summary>After <see cref="WorldObject.Tile"/> is valid, strip comp if orbit (init can run before tile/layer exist).</summary>
        private bool evaluatedDeferredOrbitStrip;
        private int ticksExisted = 0;
        /// <summary>Game tick when this settlement began accruing attack-range age. -1 until first ensure.</summary>
        public int attackRangeFoundingTick = -1;
        /// <summary>Last target (max) strength from occupants; used to apply delta when pawns are added/removed.</summary>
        private float lastTargetOutpostStrength = -1f;

        public int roadTargetTile = -1;
        public float roadProgress = 0f;
        public string roadTargetName = string.Empty;
        public SettlementTier selectedRoadTier = SettlementTier.T1;
        /// <summary>True when the active road project removes existing road links (dirt-segment effort) instead of paving.</summary>
        public bool roadIsClearing;

        /// <summary>Settlement building a road on behalf of the player (paid upfront; independent of daily WorldActions).</summary>
        public bool playerOrderedRoad;
        public int playerOrderedRoadGoodwillPaid;
        public bool playerOrderedRoadGoodwillRefunded;
        public int playerOrderedRoadInitialSegments;
        public int playerOrderedRoadBaseCost;
        public float playerOrderedRoadPerSegmentRate;
        private int legacyPlayerOrderedRoadPerSegmentCost;

        public bool HasActivePlayerOrderedRoadProject => playerOrderedRoad && roadTargetTile != -1;

        public void ClearPlayerOrderedRoadBilling()
        {
            playerOrderedRoad = false;
            playerOrderedRoadGoodwillPaid = 0;
            playerOrderedRoadGoodwillRefunded = false;
            playerOrderedRoadInitialSegments = 0;
            playerOrderedRoadBaseCost = 0;
            playerOrderedRoadPerSegmentRate = 0f;
            roadTargetUsesDetachedStart = false;
        }

        // --- SURGICAL: Road Path & Work Caching to prevent stuttering ---
        public List<int> cachedRoadPathTiles = new List<int>();
        /// <summary>Saved clicked nodes before destination. For detached-start projects this includes the chosen start node.</summary>
        public List<int> roadWaypointTiles = new List<int>();
        /// <summary>True when the player picked an explicit first node instead of starting at the builder tile.</summary>
        public bool roadTargetUsesDetachedStart;
        public int lastPathSourceTile = -1;
        public int cachedWorkTile = -1;
        /// <summary>True while a road builder traveler from this outpost is en route. Set when we launch, cleared when traveler is destroyed (event-based).</summary>
        private bool builderInField;

        // --- Road block project (mutex with roadTargetTile) ---
        public List<int> roadBlockPlannedTiles = new List<int>();
        /// <summary>Player-clicked polyline nodes (waypoints + final). Used for X/star overlays.</summary>
        public List<int> roadBlockClickedNodes = new List<int>();
        /// <summary>Full contiguous path along clicked nodes (dest-first), for selection overlay lines.</summary>
        public List<int> roadBlockCachedPathTiles = new List<int>();
        public int roadBlockWorkIndex;
        public float roadBlockProgress;
        public bool roadBlockIsClearing;
        /// <summary>When clearing, planned tiles may hold road blocks or spike traps (Remove fortifications).</summary>
        public bool roadBlockClearAnyFortification;
        public int roadBlockCachedWorkTile = -1;
        public string roadBlockTargetName = string.Empty;
        public RoadBlockKind selectedRoadBlockKind = RoadBlockKind.Normal;
        private bool roadBlockBuilderInField;
        private int lastRoadBlockProgressTick = -1;

        // --- Spike trap project (mutex with road / road-block projects) ---
        public List<int> spikeTrapPlannedTiles = new List<int>();
        public List<int> spikeTrapClickedNodes = new List<int>();
        public List<int> spikeTrapCachedPathTiles = new List<int>();
        public int spikeTrapWorkIndex;
        public float spikeTrapProgress;
        public bool spikeTrapIsClearing;
        public int spikeTrapCachedWorkTile = -1;
        public string spikeTrapTargetName = string.Empty;
        public SpikeTrapKind selectedSpikeTrapKind = SpikeTrapKind.Spike;
        private bool spikeTrapBuilderInField;
        private int lastSpikeTrapProgressTick = -1;

        // --- AT Turret project (multi-tile queue; mutex with road / block / trap / decontam) ---
        public List<int> atTurretPlannedTiles = new List<int>();
        public int atTurretWorkIndex;
        public int atTurretCachedWorkTile = -1;
        public float atTurretProgress;
        public string atTurretTargetName = string.Empty;
        public AtTurretTier selectedAtTurretTier = AtTurretTier.Medium;
        public bool atTurretBuilderInField;
        public int lastAtTurretProgressTick = -1;

        // --- Decontamination project (mutex with road / road-block / spike-trap) ---
        public List<int> decontamPlannedTiles = new List<int>();
        public List<int> decontamClickedNodes = new List<int>();
        public List<int> decontamCachedPathTiles = new List<int>();
        public int decontamWorkIndex;
        public float decontamProgress;
        public int decontamCachedWorkTile = -1;
        public string decontamTargetName = string.Empty;
        private bool decontamBuilderInField;
        private int lastDecontamProgressTick = -1;

        public bool DecontamBuilderInField => decontamBuilderInField;

        /// <summary>Last <see cref="TickManager.TicksGame"/> when road progress was applied in batches; -1 = init on next batch.</summary>
        private int lastRoadProgressTick = -1;
        /// <summary>Last <see cref="TickManager.TicksGame"/> when daily strength regen fired; elapsed-tick gating replaces modulo alignment.</summary>
        private int lastStrengthRegenTick = -99999;
        /// <summary>Last tick when site pollution damage was assessed (daily). -1 = seed stagger on first check.</summary>
        private int lastPollutionSiteDamageTick = -1;
        /// <summary>Last tick when NPC settlement auto-decontam was assessed (12h). -1 = seed stagger on first check.</summary>
        private int lastNpcDecontamAssessTick = -1;

        private const int RoadProgressUpdateIntervalTicks = 180;
        private const int NpcAutoDecontamIntervalTicks = 30000;

        /// <summary>Nominal days between NPC settlement auto-decontamination assessments (12 hours).</summary>
        public static float NpcAutoDecontamIntervalDays => NpcAutoDecontamIntervalTicks / (float)GenDate.TicksPerDay;

        /// <summary>Tick when the next NPC auto-decontam assessment is due, or -1 if not seeded yet.</summary>
        public int NpcDecontamAssessCooldownEndTick
        {
            get
            {
                if (lastNpcDecontamAssessTick < 0) return -1;
                return lastNpcDecontamAssessTick + NpcAutoDecontamIntervalTicks;
            }
        }
        private const int MaxRoadProgressCatchUpTicks = 3600;

        public int redirectionTargetTile = -1;

        private string cachedInspectString;
        private int cachedInspectTick = -999;
        private WD_RadiusOverlayKind cachedInspectRadiusKind = WD_RadiusOverlayKind.Off;

        /// <summary>Called when a road builder from this outpost is destroyed (arrived or expired). Allows progress to accumulate for the next builder.</summary>
        public void NotifyRoadBuilderReturned()
        {
            builderInField = false;
        }

        /// <summary>Called when a road-block crew from this outpost is destroyed (arrived or cancelled).</summary>
        public void NotifyRoadBlockCrewReturned()
        {
            roadBlockBuilderInField = false;
        }

        /// <summary>Called when a spike-trap crew from this outpost is destroyed (arrived or cancelled).</summary>
        public void NotifySpikeTrapCrewReturned()
        {
            spikeTrapBuilderInField = false;
        }

        /// <summary>Called when an AT Turret crew from this outpost is destroyed (arrived or cancelled).</summary>
        public void NotifyAtTurretCrewReturned()
        {
            atTurretBuilderInField = false;
        }

        /// <summary>Called when a decontamination crew from this outpost is destroyed (arrived or cancelled).</summary>
        public void NotifyDecontaminationCrewReturned()
        {
            decontamBuilderInField = false;
        }

        /// <summary>Called when a decontamination crew has been successfully dispatched from this site.</summary>
        public void NotifyDecontaminationCrewDispatched()
        {
            decontamBuilderInField = true;
        }

        /// <summary>
        /// Ready to dispatch a construction crew (progress at 100%) but cannot afford the expedition
        /// (raw cost for decontamination; garrison-retain gate for roads / blocks / traps).
        /// <paramref name="projectLabel"/> is the kind/action noun for UI.
        /// </summary>
        public bool IsConstructionWaitingOnStrength(out string projectLabel, out bool clearing)
        {
            projectLabel = null;
            clearing = false;

            if (roadTargetTile != -1 && !builderInField)
            {
                float cost = WorldActions_Roads.GetExpeditionStrengthCost(selectedRoadTier);
                if (roadProgress >= 1f && !WorldActions_Utils.CanAffordExpeditionLeavingGarrison(this, cost))
                {
                    clearing = roadIsClearing;
                    projectLabel = GetActiveRoadProjectLabel();
                    return true;
                }
            }

            if (WorldActions_RoadBlocks.HasActiveRoadBlockProject(this) && !roadBlockBuilderInField)
            {
                float cost = WorldActions_RoadBlocks.GetExpeditionStrengthCost(selectedRoadBlockKind);
                if (roadBlockProgress >= 1f && !WorldActions_Utils.CanAffordExpeditionLeavingGarrison(this, cost))
                {
                    clearing = roadBlockIsClearing;
                    projectLabel = clearing
                        ? (roadBlockClearAnyFortification
                            ? "TSA_WD_Inspect_FortificationClear".Translate().ToString()
                            : "TSA_WD_Inspect_RoadBlockClear".Translate().ToString())
                        : RoadBlockKindUtil.LabelKey(selectedRoadBlockKind).Translate().ToString();
                    return true;
                }
            }

            if (WorldActions_SpikeTraps.HasActiveSpikeTrapProject(this) && !spikeTrapBuilderInField)
            {
                float cost = WorldActions_SpikeTraps.GetExpeditionStrengthCost(selectedSpikeTrapKind);
                if (spikeTrapProgress >= 1f && !WorldActions_Utils.CanAffordExpeditionLeavingGarrison(this, cost))
                {
                    clearing = spikeTrapIsClearing;
                    projectLabel = clearing
                        ? "TSA_WD_Inspect_SpikeTrapClear".Translate().ToString()
                        : SpikeTrapKindUtil.LabelKey(selectedSpikeTrapKind).Translate().ToString();
                    return true;
                }
            }

            if (WorldActions_AtTurrets.HasActiveAtTurretProject(this) && !atTurretBuilderInField)
            {
                float cost = WorldActions_AtTurrets.GetExpeditionStrengthCost(selectedAtTurretTier);
                if (atTurretProgress >= 1f && !WorldActions_Utils.CanAffordExpeditionLeavingGarrison(this, cost))
                {
                    projectLabel = AtTurretUtility.LabelKey(selectedAtTurretTier).Translate().ToString();
                    return true;
                }
            }

            // Decontamination intentionally ignores garrison retain — only the raw expedition cost matters.
            if (WorldActions_Decontamination.HasActiveDecontaminationProject(this) && !decontamBuilderInField)
            {
                float cost = WorldActions_Decontamination.GetExpeditionStrengthCost();
                if (decontamProgress >= 1f && strength < cost)
                {
                    projectLabel = "TSA_WD_Inspect_DecontaminationBuild".Translate().ToString();
                    return true;
                }
            }

            return false;
        }

        public string GetInsufficientStrengthConstructionMessage()
        {
            if (!IsConstructionWaitingOnStrength(out string projectLabel, out bool clearing))
                return null;
            return clearing
                ? "TSA_WD_InsufficientStrengthToClear".Translate(projectLabel).ToString()
                : "TSA_WD_InsufficientStrengthToBuild".Translate(projectLabel).ToString();
        }

        /// <summary>Inspect/overview label for the active road project (tier when building, clear label when removing).</summary>
        public string GetActiveRoadProjectLabel()
        {
            if (roadIsClearing)
                return "TSA_WD_Inspect_RoadClear".Translate().ToString();
            return WorldActions_Roads.GetRoadTierLabel(selectedRoadTier);
        }

        /// <summary>Inspect/overview label for the active road-block project (kind when building, clear label when clearing).</summary>
        public string GetActiveRoadBlockProjectLabel()
        {
            if (roadBlockIsClearing)
                return roadBlockClearAnyFortification
                    ? "TSA_WD_Inspect_FortificationClear".Translate().ToString()
                    : "TSA_WD_Inspect_RoadBlockClear".Translate().ToString();
            return RoadBlockKindUtil.LabelKey(selectedRoadBlockKind).Translate().ToString();
        }

        /// <summary>Inspect/overview label for the active spike-trap project (kind when building, clear label when clearing).</summary>
        public string GetActiveSpikeTrapProjectLabel()
        {
            if (spikeTrapIsClearing)
                return "TSA_WD_Inspect_SpikeTrapClear".Translate().ToString();
            return SpikeTrapKindUtil.LabelKey(selectedSpikeTrapKind).Translate().ToString();
        }

        public override void PostDestroy()
        {
            Faction faction = parent?.Faction;
            if (playerOrderedRoad && roadTargetTile != -1 && !playerOrderedRoadGoodwillRefunded)
                WorldActions_Roads.ClearRoadProject(this, RoadProjectClearReason.SettlementDestroyed, faction);
            if (WorldActions_RoadBlocks.HasActiveRoadBlockProject(this))
                WorldActions_RoadBlocks.ClearRoadBlockProject(this);
            if (WorldActions_SpikeTraps.HasActiveSpikeTrapProject(this))
                WorldActions_SpikeTraps.ClearSpikeTrapProject(this);
            if (WorldActions_AtTurrets.HasActiveAtTurretProject(this))
                WorldActions_AtTurrets.ClearAtTurretProject(this);
            if (WorldActions_Decontamination.HasActiveDecontaminationProject(this))
                WorldActions_Decontamination.ClearDecontaminationProject(this);
            if (parent is Settlement settlement)
            {
                AtTurretUtility.DestroyTurretsBuiltBy(settlement);
                WorldActions_NpcFortify.NotifyBuilderLost(settlement);
            }
            ReinforcementNeighborCache.BumpGeneration();
            base.PostDestroy();
        }

        private static float GetOutpostDefensiveRecoveryMinFlat()
        {
            WorldDominationSettings settings = WorldDominationMod.settings;
            float v = settings != null ? settings.outpostDefensiveRecoveryMinFlatPerDay : 25f;
            return Mathf.Max(0f, v);
        }

        private static float GetOutpostDefensiveRecoveryFraction()
        {
            WorldDominationSettings settings = WorldDominationMod.settings;
            return Mathf.Clamp(settings != null ? settings.outpostDefensiveRecoveryFractionPerDay : 0.1f, 0f, 1f);
        }

        private static float GetOutpostOffensiveRecoveryMinFlat()
        {
            WorldDominationSettings settings = WorldDominationMod.settings;
            float v = settings != null ? settings.outpostOffensiveRecoveryMinFlatPerDay : 80f;
            return Mathf.Max(0f, v);
        }

        private static float GetOutpostOffensiveRecoveryFraction()
        {
            WorldDominationSettings settings = WorldDominationMod.settings;
            return Mathf.Clamp(settings != null ? settings.outpostOffensiveRecoveryFractionPerDay : 0.15f, 0f, 1f);
        }

        /// <summary>Daily offensive regen toward cap (matches tick logic; used by outpost overview).</summary>
        public float GetInspectDailyOffensiveRecovery()
        {
            if (!IsOutpost) return 0f;
            if (parent is WorldObject_WD_Outpost wd && wd.ManualDefenseActive)
                return 0f;
            float targetStr = GetOutpostOffensiveRegenTarget();
            float mult = 1f;
            if (parent is WorldObject_WD_Outpost wo)
                mult += wo.GetOutpostOffensiveRecoveryMultiplierBonus();
            return Mathf.Max(GetOutpostOffensiveRecoveryMinFlat(), targetStr * GetOutpostOffensiveRecoveryFraction()) * mult;
        }

        /// <summary>
        /// Regen/clamp target for outpost offensive pool. Prefer the last known composition max when
        /// <see cref="GetTargetOutpostStrength"/> dips (e.g. garrison extracted for manual defense, stale empty cache)
        /// so CompTick cannot crush current down to the empty-outpost floor (~100).
        /// </summary>
        private float GetOutpostOffensiveRegenTarget()
        {
            float live = GetTargetOutpostStrength();
            if (lastTargetOutpostStrength > live)
                return lastTargetOutpostStrength;
            return live;
        }

        /// <summary>Daily defensive regen toward structural max (matches tick logic; used by outpost overview).</summary>
        public float GetInspectDailyDefensiveRecovery()
        {
            if (IsPlayerMapSettlement)
                return 0f;
            float defMax = GetBaseDefensiveStrength();
            float gain = Mathf.Max(GetOutpostDefensiveRecoveryMinFlat(), defMax * GetOutpostDefensiveRecoveryFraction());
            if (parent is WorldObject_WD_Outpost wo)
                gain *= 1f + wo.GetOutpostDefensiveRecoveryMultiplierBonus();
            return gain;
        }

        // Updated Helpers for UI and Logic
        public bool IsRaidOnCooldown => Find.TickManager.TicksGame < raidCooldownTick;
        public bool IsExpansionOnCooldown => Find.TickManager.TicksGame < expansionCooldownTick;
        public bool IsRoadOnCooldown => Find.TickManager.TicksGame < roadCooldownTick;
        public bool IsFortifyOnCooldown => Find.TickManager.TicksGame < fortifyCooldownTick;
        public bool IsTraderOnCooldown => Find.TickManager.TicksGame < traderCooldownTick;
        public bool IsAmbushOnCooldown => Find.TickManager.TicksGame < ambushCooldownTick;
        public bool IsPlayerColonyWdTraderTargetOnCooldown => Find.TickManager.TicksGame < playerColonyWdTraderCooldownTick;
        public bool IsDefenseOnCooldown => Find.TickManager.TicksGame < defenseCooldownTick;
        public bool IsIncidentOnCooldown => Find.TickManager.TicksGame < incidentCooldownTick;
        public bool IsMortarOnCooldown => Find.TickManager.TicksGame < mortarCooldownTick;
        public bool IsAntiAirOnCooldown => Find.TickManager.TicksGame < antiAirCooldownTick;

        public bool IsEspionageOnCooldown => Find.TickManager.TicksGame < espionageCooldownUntilTick;
        public bool IsAidOnCooldown => Find.TickManager.TicksGame < aidCooldownUntilTick;

        public bool IsSettlement => parent is Settlement;
        public bool IsOutpost => parent is WorldObject_WD_Outpost;
        /// <summary>Player map colony only. WD offensive/defensive pools are not used (always zero); strength applies to NPC settlements and player outposts.</summary>
        public bool IsPlayerMapSettlement => IsSettlement && parent?.Faction?.IsPlayer == true;

        public float GetDeployableOffense() => IsPlayerMapSettlement ? 0f : Mathf.Max(0f, offensiveStrength);
        public float GetTotalLocalDefensePower() => IsPlayerMapSettlement ? 0f : Mathf.Max(0f, offensiveStrength) + Mathf.Max(0f, defensiveStrength);

        public float GetBaseDefensiveStrength()
        {
            var seth = WorldDominationMod.settings;
            if (IsOutpost && parent?.Faction?.IsPlayer == true)
            {
                float baseVal = seth != null ? seth.playerOutpostBaseDefensiveStrength : 100f;
                if (parent is WorldObject_WD_Outpost outpost)
                    baseVal += outpost.GetOutpostUpgradeDefensiveBonus();
                return baseVal;
            }

            if (IsPlayerMapSettlement) return 0f;

            switch (tier)
            {
                case SettlementTier.T4: return seth != null ? seth.tier4BaseDefensiveStrength : 500f;
                case SettlementTier.T3: return seth != null ? seth.tier3BaseDefensiveStrength : 350f;
                case SettlementTier.T2: return seth != null ? seth.tier2BaseDefensiveStrength : 200f;
                default: return seth != null ? seth.tier1BaseDefensiveStrength : 100f;
            }
        }

        /// <summary>Cooldown end offset from current tick; at least 1 tick so <c>TicksGame &lt; defenseCooldownTick</c> is ever true.</summary>
        public static int CooldownTicksFromDays(float days) => Mathf.Max(1, Mathf.RoundToInt(days * 60000f));

        /// <summary>Defense-shield duration after a raid targets this world object (player colony, player outpost, or NPC settlement).</summary>
        public static float GetDefenseCooldownDaysFor(WorldObject target)
        {
            WorldDominationSettings seth = WorldDominationMod.settings;
            if (target is Settlement settlement && settlement.Faction?.IsPlayer == true && settlement.HasMap)
                return seth?.cooldownPlayerRaidDays ?? WorldDominationSettings.DefCdPlayerRaidDays;
            if (target is WorldObject_WD_Outpost outpost && outpost.Faction?.IsPlayer == true)
                return seth?.cooldownPlayerOutpostRaidDays ?? WorldDominationSettings.DefCooldownPlayerOutpostRaidDays;
            return seth?.cooldownBeingRaidedDays ?? WorldDominationSettings.DefCdBeingRaidedDays;
        }

        /// <summary>
        /// One-time raid protection for player map colonies. Initialize can run before faction/settings exist
        /// (misclassifying the settlement as NPC), so this is retried from CompTick and world FinalizeInit.
        /// </summary>
        public void EnsureInitialPlayerColonyShield()
        {
            if (appliedInitialPlayerColonyShield || !IsPlayerMapSettlement) return;

            var seth = WorldDominationMod.settings;
            if (seth == null) return;

            subType = "Colony";
            offensiveStrength = 0f;
            defensiveStrength = 0f;

            if (!IsDefenseOnCooldown)
                defenseCooldownTick = Find.TickManager.TicksGame + CooldownTicksFromDays(seth.cooldownPlayerRaidDays);

            MarkPlayerColonyWdRaidPicked();
            appliedInitialPlayerColonyShield = true;
        }

        /// <summary>
        /// Call after a new player outpost is created and faction is set (dialog and remote caravan founding).
        /// Defense shield is half of <see cref="GetDefenseCooldownDaysFor"/>; outgoing raid CD stays full <c>cooldownRaidDays</c>.
        /// </summary>
        public static void ApplyPlayerOutpostFoundingShields(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null || outpost.Destroyed) return;
            CompViralSpread spread = outpost.GetComponent<CompViralSpread>();
            if (spread == null) return;

            spread.UpdateOutpostStrengthLogically();

            var seth = WorldDominationMod.settings;
            if (seth == null) return;

            int ticks = Find.TickManager.TicksGame;
            spread.raidCooldownTick = ticks + CooldownTicksFromDays(seth.cooldownRaidDays);
            float defenseDays = GetDefenseCooldownDaysFor(outpost) * 0.5f;
            spread.defenseCooldownTick = ticks + CooldownTicksFromDays(defenseDays);
        }

        /// <summary>Resets experimental colony raid-ratio soften clock when the colony is picked as a WD raid target (or initial shield).</summary>
        public void MarkPlayerColonyWdRaidPicked()
        {
            lastPlayerColonyWdRaidPickTick = Find.TickManager.TicksGame;
        }

        /// <summary>Loaded saves: do not grant a retroactive starting shield mid-campaign.</summary>
        public void MarkInitialPlayerColonyShieldHandled() => appliedInitialPlayerColonyShield = true;

        public override void Initialize(WorldObjectCompProperties props)
        {
            base.Initialize(props);
            ReinforcementNeighborCache.BumpGeneration();

            // Only strip when orbit is confirmed — Tile/Layer are often unset during early world-object init (race with world gen).
            if (parent != null && WorldActions_Utils.IsConfirmedOrbitWorldObject(parent))
            {
                parent.AllComps.Remove(this);
                return;
            }

            // IsExcludedFaction includes the player faction (they don't get AI world-sim tiers). Do not treat player bases as "Excluded" here
            // or we skip Colony/Outpost setup and never apply initial raid-protection ticks.
            if (parent.Faction != null && WorldActions_Utils.IsExcludedFaction(parent.Faction) && !parent.Faction.IsPlayer)
            {
                this.subType = "Excluded";
                this.outpostInitialized = true;
                return;
            }

            if (string.IsNullOrEmpty(subType))
            {
                if (IsOutpost)
                {
                    subType = "Outpost";
                    offensiveStrength = 0f;
                    defensiveStrength = GetBaseDefensiveStrength();
                    // Founding raid shields are applied after SetFaction via ApplyPlayerOutpostFoundingShields
                    // (Initialize runs before faction is set, so a CD write here would usually no-op).
                }
                else if (IsSettlement)
                {
                    bool isPlayerSettlement = parent.Faction != null && parent.Faction.IsPlayer;
                    if (isPlayerSettlement)
                        EnsureInitialPlayerColonyShield();
                    else
                    {
                        if (SpreadManager != null) WorldActions_Utils.ApplyRandomTier(this);
                        else SetState(SettlementTier.T1);
                    }
                }
            }

            UpdateInterceptorRegistration();
        }

        /// <summary>
        /// Syncs outpost strength with current occupants. New outposts get current = max (full strength).
        /// When pawns are added/removed, preserve fill ratio (current/oldMax) against the new max so a
        /// depleted outpost is not wiped by absolute pawn-worth deltas, and re-adding is symmetric
        /// (no free full-strength boost). Skipped while manual defense has extracted the garrison —
        /// otherwise max collapses to the empty-outpost floor and CompTick would crush/stick strength.
        /// </summary>
        public void UpdateOutpostStrengthLogically()
        {
            if (!IsOutpost) return;
            if (parent is WorldObject_WD_Outpost wdOutpost && wdOutpost.ManualDefenseActive)
                return;

            float newMax = GetTargetOutpostStrength();

            // Occupant → VirtualPawns can still be empty right after load; GetTargetStrength then returns the
            // empty-outpost floor (~100). Rematching against that would crush scribed strength and
            // leave late-game metrics false until CompTick regenerates over ~1 in-game hour.
            if (outpostInitialized
                && parent is WorldObject_WD_Outpost occupied
                && occupied.Occupants != null
                && occupied.Occupants.Count > 0
                && lastTargetOutpostStrength > 0f
                && newMax + 0.5f < lastTargetOutpostStrength
                && newMax <= 100.5f)
            {
                return;
            }

            if (!outpostInitialized && newMax > 0)
            {
                // New outpost (e.g. founded after raid): start at full strength
                offensiveStrength = newMax;
                lastTargetOutpostStrength = newMax;
                defensiveStrength = GetBaseDefensiveStrength();
                outpostInitialized = true;
            }
            else
            {
                // Pawns added/removed: keep the same readiness fraction of the new cap.
                float oldMax = lastTargetOutpostStrength >= 0f ? lastTargetOutpostStrength : newMax;
                if (oldMax > 0.01f && newMax >= 0f)
                {
                    float ratio = Mathf.Clamp01(offensiveStrength / oldMax);
                    offensiveStrength = ratio * newMax;
                }
                else
                {
                    offensiveStrength = Mathf.Min(offensiveStrength, newMax);
                }
                offensiveStrength = Mathf.Clamp(offensiveStrength, 0f, newMax);
                lastTargetOutpostStrength = newMax;
                ClampDefensiveStrengthToStructuralMax();
            }

            lastRadiusUpdateTick = -9999;
            if (parent?.Faction != null && parent.Faction.IsPlayer)
                SpreadManager?.Notify_PlayerOutpostStrengthChanged();
        }

        public override void CompTick()
        {
            // parent.Tile.Valid is layer-aware (checks tileId + layer); TilesCount is the surface-only count and
            // would mis-gate a non-surface parent. Valid + the orbit check below handle every layer correctly.
            if (!evaluatedDeferredOrbitStrip && parent != null && parent.Spawned
                && Find.WorldGrid != null && parent.Tile.Valid)
            {
                evaluatedDeferredOrbitStrip = true;
                if (WorldActions_Utils.IsConfirmedOrbitWorldObject(parent))
                {
                    parent.AllComps.Remove(this);
                    return;
                }
            }

            if (!repairedMisclassifiedPlayerSubType && subType == "Excluded" && parent.Faction != null && parent.Faction.IsPlayer)
            {
                repairedMisclassifiedPlayerSubType = true;
                if (IsOutpost)
                {
                    subType = "Outpost";
                    offensiveStrength = 0f;
                    defensiveStrength = GetBaseDefensiveStrength();
                    UpdateOutpostStrengthLogically();
                }
                else if (IsSettlement)
                {
                    subType = "Colony";
                    offensiveStrength = 0f;
                    defensiveStrength = 0f;
                    EnsureInitialPlayerColonyShield();
                }
            }

            EnsureInitialPlayerColonyShield();

            if ((Find.TickManager.TicksGame + parent.ID) % 250 != 0) return;

            ColonyWorldBuildUtility.ClearProjectsIfFeatureDisabled(this);

            var spreadMgr = SpreadManager;
            if (spreadMgr != null && lastActionDay != spreadMgr.ActionDayOfYearId)
            {
                actionsTakenToday = 0;
                lastActionDay = spreadMgr.ActionDayOfYearId;
            }

            bool colonyWorldBuild = ColonyWorldBuildUtility.IsPlayerColonyBuildActor(parent);

            // Prep accrues while a crew is in transit; only one traveler launches at a time (bank at 100% until return).
            if (roadTargetTile != -1 && (IsOutpost || (IsSettlement && playerOrderedRoad) || colonyWorldBuild))
            {
                if (playerOrderedRoad && parent?.Faction != null
                    && WorldActions_Utils.SafeRelationKindWith(parent.Faction, Faction.OfPlayerSilentFail) == FactionRelationKind.Hostile)
                {
                    WorldActions_Roads.ClearRoadProject(this, RoadProjectClearReason.FactionHostile);
                }
                else
                {
                int nowTick = Find.TickManager.TicksGame;
                if (lastRoadProgressTick < 0)
                    lastRoadProgressTick = nowTick;
                else if (nowTick - lastRoadProgressTick >= RoadProgressUpdateIntervalTicks)
                {
                    int dt = nowTick - lastRoadProgressTick;
                    dt = Mathf.Min(dt, MaxRoadProgressCatchUpTicks);
                    lastRoadProgressTick = nowTick;
                    float workSpeed = WorldActions_Roads.GetRoadProgressWorkSpeed(parent);
                    if (workSpeed > 0f)
                    {
                        float rate = workSpeed / WorldActions_Roads.GetRoadProgressRequiredTicks(selectedRoadTier);
                        roadProgress += rate * dt;
                        while (roadProgress >= 1f && !builderInField)
                        {
                            // Same gate as road blocks / traps: do not consume the ready segment while too weak to dispatch
                            // (includes min garrison retain — builders must not empty the outpost).
                            if (!WorldActions_Utils.CanAffordExpeditionLeavingGarrison(this, WorldActions_Roads.GetExpeditionStrengthCost(selectedRoadTier)))
                                break;
                            roadProgress -= 1f;
                            if (WorldActions_Roads.LaunchRoadBuilderFromOutpost(parent))
                                builderInField = true;
                        }
                        if (roadProgress > 1f)
                            roadProgress = 1f;
                    }
                }
                }
            }

            if (WorldActions_RoadBlocks.HasActiveRoadBlockProject(this) && (IsOutpost || colonyWorldBuild))
            {
                int nowTickRb = Find.TickManager.TicksGame;
                if (lastRoadBlockProgressTick < 0)
                    lastRoadBlockProgressTick = nowTickRb;
                else if (nowTickRb - lastRoadBlockProgressTick >= RoadProgressUpdateIntervalTicks)
                {
                    int dtRb = nowTickRb - lastRoadBlockProgressTick;
                    dtRb = Mathf.Min(dtRb, MaxRoadProgressCatchUpTicks);
                    lastRoadBlockProgressTick = nowTickRb;
                    float workSpeedRb = WorldActions_Roads.GetRoadProgressWorkSpeed(parent);
                    if (workSpeedRb > 0f)
                    {
                        float rateRb = workSpeedRb / WorldActions_RoadBlocks.GetRoadBlockProgressRequiredTicks(selectedRoadBlockKind);
                        roadBlockProgress += rateRb * dtRb;
                        while (roadBlockProgress >= 1f && !roadBlockBuilderInField)
                        {
                            if (!WorldActions_Utils.CanAffordExpeditionLeavingGarrison(this, WorldActions_RoadBlocks.GetExpeditionStrengthCost(selectedRoadBlockKind)))
                                break;
                            roadBlockProgress -= 1f;
                            if (WorldActions_RoadBlocks.LaunchRoadBlockCrewFromOutpost(parent))
                                roadBlockBuilderInField = true;
                        }
                        // Cap at 100% while waiting on strength, in transit, or any failed dispatch.
                        if (roadBlockProgress > 1f)
                            roadBlockProgress = 1f;
                    }
                }
            }

            if (WorldActions_SpikeTraps.HasActiveSpikeTrapProject(this) && (IsOutpost || colonyWorldBuild))
            {
                int nowTickSt = Find.TickManager.TicksGame;
                if (lastSpikeTrapProgressTick < 0)
                    lastSpikeTrapProgressTick = nowTickSt;
                else if (nowTickSt - lastSpikeTrapProgressTick >= RoadProgressUpdateIntervalTicks)
                {
                    int dtSt = nowTickSt - lastSpikeTrapProgressTick;
                    dtSt = Mathf.Min(dtSt, MaxRoadProgressCatchUpTicks);
                    lastSpikeTrapProgressTick = nowTickSt;
                    float workSpeedSt = WorldActions_Roads.GetRoadProgressWorkSpeed(parent);
                    if (workSpeedSt > 0f)
                    {
                        float rateSt = workSpeedSt / WorldActions_SpikeTraps.GetSpikeTrapProgressRequiredTicks(selectedSpikeTrapKind);
                        spikeTrapProgress += rateSt * dtSt;
                        while (spikeTrapProgress >= 1f && !spikeTrapBuilderInField)
                        {
                            if (!WorldActions_Utils.CanAffordExpeditionLeavingGarrison(this, WorldActions_SpikeTraps.GetExpeditionStrengthCost(selectedSpikeTrapKind)))
                                break;
                            spikeTrapProgress -= 1f;
                            if (WorldActions_SpikeTraps.LaunchSpikeTrapCrewFromOutpost(parent))
                                spikeTrapBuilderInField = true;
                        }
                        if (spikeTrapProgress > 1f)
                            spikeTrapProgress = 1f;
                    }
                }
            }

            if (WorldActions_AtTurrets.HasActiveAtTurretProject(this) && (IsOutpost || colonyWorldBuild))
            {
                int nowTickAt = Find.TickManager.TicksGame;
                if (lastAtTurretProgressTick < 0)
                    lastAtTurretProgressTick = nowTickAt;
                else if (nowTickAt - lastAtTurretProgressTick >= RoadProgressUpdateIntervalTicks)
                {
                    int dtAt = nowTickAt - lastAtTurretProgressTick;
                    dtAt = Mathf.Min(dtAt, MaxRoadProgressCatchUpTicks);
                    lastAtTurretProgressTick = nowTickAt;
                    float workSpeedAt = WorldActions_Roads.GetRoadProgressWorkSpeed(parent);
                    if (workSpeedAt > 0f)
                    {
                        float rateAt = workSpeedAt / WorldActions_AtTurrets.GetAtTurretProgressRequiredTicks(selectedAtTurretTier);
                        atTurretProgress += rateAt * dtAt;
                        while (atTurretProgress >= 1f && !atTurretBuilderInField)
                        {
                            if (!WorldActions_Utils.CanAffordExpeditionLeavingGarrison(this, WorldActions_AtTurrets.GetExpeditionStrengthCost(selectedAtTurretTier)))
                                break;
                            atTurretProgress -= 1f;
                            if (WorldActions_AtTurrets.LaunchAtTurretCrewFromOutpost(parent))
                                atTurretBuilderInField = true;
                        }
                        if (atTurretProgress > 1f)
                            atTurretProgress = 1f;
                    }
                }
            }

            if (WorldActions_Decontamination.HasActiveDecontaminationProject(this) && IsOutpost)
            {
                int nowTickDc = Find.TickManager.TicksGame;
                if (lastDecontamProgressTick < 0)
                    lastDecontamProgressTick = nowTickDc;
                else if (nowTickDc - lastDecontamProgressTick >= RoadProgressUpdateIntervalTicks)
                {
                    int dtDc = nowTickDc - lastDecontamProgressTick;
                    dtDc = Mathf.Min(dtDc, MaxRoadProgressCatchUpTicks);
                    lastDecontamProgressTick = nowTickDc;
                    float workSpeedDc = WorldActions_Roads.GetRoadProgressWorkSpeed(parent);
                    if (workSpeedDc > 0f)
                    {
                        float rateDc = workSpeedDc / WorldActions_Decontamination.GetDecontaminationProgressRequiredTicks();
                        decontamProgress += rateDc * dtDc;
                        while (decontamProgress >= 1f && !decontamBuilderInField)
                        {
                            if (strength < WorldActions_Decontamination.GetExpeditionStrengthCost())
                                break;
                            decontamProgress -= 1f;
                            if (WorldActions_Decontamination.LaunchDecontaminationCrewFromOutpost(parent))
                                decontamBuilderInField = true;
                        }
                        if (decontamProgress > 1f)
                            decontamProgress = 1f;
                    }
                }
            }

            int ticksNow = Find.TickManager.TicksGame;
            if (ticksNow - lastStrengthRegenTick >= 60000)
            {
                lastStrengthRegenTick = ticksNow;
                TickOutpostOffensiveStrengthRecoveryDaily();
                TickSettlementPassiveOffensiveGrowthDaily();
                TickDefensiveStrengthRecoveryDaily();
                TickFortifyBuilderFactionChange();
            }

            if (lastPollutionSiteDamageTick < 0)
                lastPollutionSiteDamageTick = ticksNow - (parent.ID % GenDate.TicksPerDay);
            if (ticksNow - lastPollutionSiteDamageTick >= GenDate.TicksPerDay)
            {
                lastPollutionSiteDamageTick = ticksNow;
                SitePollutionDamage.TryApplyDaily(this);
            }

            if (IsSettlement && parent.Faction != null && !parent.Faction.IsPlayer)
            {
                if (lastNpcDecontamAssessTick < 0)
                    lastNpcDecontamAssessTick = ticksNow - (parent.ID % NpcAutoDecontamIntervalTicks);
                if (ticksNow - lastNpcDecontamAssessTick >= NpcAutoDecontamIntervalTicks)
                {
                    lastNpcDecontamAssessTick = ticksNow;
                    WorldActions_Decontamination.TryNpcSettlementAutoDecontaminate(parent);
                }
            }
        }

        /// <summary>Lazily set founding tick for per-settlement attack-range age (new settlements start at 0 age).</summary>
        public int EnsureAttackRangeFoundingTick()
        {
            if (attackRangeFoundingTick < 0)
                attackRangeFoundingTick = Find.TickManager?.TicksGame ?? 0;
            return attackRangeFoundingTick;
        }

        /// <summary>Daily offensive regen toward composition max. Skipped while manual defense has extracted the garrison.</summary>
        private void TickOutpostOffensiveStrengthRecoveryDaily()
        {
            if (!IsOutpost) return;
            if (parent is WorldObject_WD_Outpost wd && wd.ManualDefenseActive)
                return;

            float targetStr = GetOutpostOffensiveRegenTarget();
            if (offensiveStrength > targetStr) offensiveStrength = targetStr;
            else if (offensiveStrength < targetStr)
            {
                float mult = 1f;
                if (parent is WorldObject_WD_Outpost outpost)
                    mult += outpost.GetOutpostOffensiveRecoveryMultiplierBonus();
                float gain = Mathf.Max(GetOutpostOffensiveRecoveryMinFlat(), targetStr * GetOutpostOffensiveRecoveryFraction()) * mult;
                offensiveStrength = Mathf.Min(offensiveStrength + gain, targetStr);
            }
        }

        /// <summary>NPC settlements: silent daily offensive climb toward tier soft cap (action-free).</summary>
        private void TickSettlementPassiveOffensiveGrowthDaily()
        {
            WorldActions_GrowthExpand.ApplyPassiveOffensiveGrowth(this);
        }

        private void TickFortifyBuilderFactionChange()
        {
            if (!(parent is Settlement settlement) || IsOutpost) return;
            int fid = parent.Faction?.loadID ?? -1;
            if (fortifyBuilderFactionId < 0)
            {
                fortifyBuilderFactionId = fid;
                return;
            }
            if (fid != fortifyBuilderFactionId)
            {
                AtTurretUtility.DestroyTurretsBuiltBy(settlement);
                WorldActions_NpcFortify.NotifyBuilderLost(settlement);
                fortifyBuilderFactionId = fid;
            }
        }

        /// <summary>Raise defensive toward <see cref="GetBaseDefensiveStrength"/> by max(flat, fraction of max) per day; cap at max.</summary>
        private void TickDefensiveStrengthRecoveryDaily()
        {
            if (IsPlayerMapSettlement) return;
            float defMax = GetBaseDefensiveStrength();
            if (defensiveStrength > defMax)
                defensiveStrength = defMax;
            else if (defensiveStrength < defMax)
            {
                float gain = Mathf.Max(GetOutpostDefensiveRecoveryMinFlat(), defMax * GetOutpostDefensiveRecoveryFraction());
                if (parent is WorldObject_WD_Outpost woDef)
                    gain *= 1f + woDef.GetOutpostDefensiveRecoveryMultiplierBonus();
                defensiveStrength = Mathf.Min(defensiveStrength + gain, defMax);
            }
        }

        /// <summary>If structural max dropped (e.g. upgrade removed), clamp current defensive. Does not raise toward max (daily regen does).</summary>
        public void ClampDefensiveStrengthToStructuralMax()
        {
            float defMax = GetBaseDefensiveStrength();
            if (defensiveStrength > defMax)
                defensiveStrength = defMax;
        }

        /// <summary>
        /// WD outpost: when structural defensive max changes (built upgrades), move <see cref="defensiveStrength"/> by the same delta immediately.
        /// Daily regen would eventually fill the gap; this matches player expectation that defensive upgrades apply at once.
        /// </summary>
        public void ApplyDefensiveCurrentForStructuralCapDelta(float delta)
        {
            if (!IsOutpost || subType != "Outpost") return;
            if (Mathf.Approximately(delta, 0f)) return;
            defensiveStrength = Mathf.Max(0f, defensiveStrength + delta);
            ClampDefensiveStrengthToStructuralMax();
        }

        public float GetTargetOutpostStrength()
        {
            if (parent is WorldObject_WD_Outpost wdOutpost)
                return wdOutpost.GetTargetStrength();

            return 100f;
        }

        /// <summary>Maximum offensive strength pool for UI and clamping (outpost cap or settlement tier max).</summary>
        public float GetMaxOffensiveStrength()
        {
            if (IsPlayerMapSettlement) return 0f;
            if (IsOutpost) return GetOutpostOffensiveRegenTarget();
            if (IsSettlement) return GetStrengthRange(tier).max;
            return float.MaxValue;
        }

        /// <summary>Feature E: flat tier-indexed trader-caravan escort strength floor for this settlement (interception/combat math only, not the resource cost deducted at launch). Non-settlements fall back to <see cref="GetMaxOffensiveStrength"/>.</summary>
        public float GetTraderEscortFloor()
        {
            var seth = WorldDominationMod.settings;
            if (seth == null || !IsSettlement) return GetMaxOffensiveStrength();
            switch (tier)
            {
                case SettlementTier.T4: return seth.traderEscortFloorT4;
                case SettlementTier.T3: return seth.traderEscortFloorT3;
                case SettlementTier.T2: return seth.traderEscortFloorT2;
                default: return seth.traderEscortFloorT1;
            }
        }

        /// <summary>Feature E: true if this settlement lost a trader caravan to interception recently enough that its next caravans should go out at full offensive strength instead of the tier floor.</summary>
        public bool IsCaravanEscortRecentlyIntercepted()
        {
            var seth = WorldDominationMod.settings;
            if (seth == null || seth.traderEscortRecentInterceptWindowDays <= 0f) return false;
            int windowTicks = CooldownTicksFromDays(seth.traderEscortRecentInterceptWindowDays);
            return Find.TickManager.TicksGame - lastCaravanInterceptedTick <= windowTicks;
        }

        /// <summary>Feature E: stamp this settlement as having just lost a Trader caravan to an actual interception kill (ambush/Rapid Response/mortar) — never on ordinary arrival or unrelated despawn.</summary>
        public void MarkCaravanIntercepted()
        {
            lastCaravanInterceptedTick = Find.TickManager.TicksGame;
        }

        public void SetState(SettlementTier newTier)
        {
            if (IsOutpost || IsPlayerMapSettlement) return;
            tier = newTier;
            subType = GetRandomSubType(newTier);
            var range = GetStrengthRange(newTier);
            if (offensiveStrength <= 0) offensiveStrength = range.RandomInRange;
            defensiveStrength = GetBaseDefensiveStrength();
            lastRadiusUpdateTick = -9999;
            UpdateInterceptorRegistration();
        }

        public void AddStrength(float amount)
        {
            if (IsPlayerMapSettlement) return;
            offensiveStrength += amount;
            if (IsSettlement) CheckTierUpdate();
        }

        public void AddStrengthNoTierUpgrade(float amount)
        {
            if (amount <= 0f) return;
            if (IsPlayerMapSettlement) return;
            if (IsOutpost)
            {
                float maxStr = GetOutpostOffensiveRegenTarget();
                offensiveStrength = Mathf.Clamp(offensiveStrength + amount, 0f, maxStr);
                return;
            }
            if (IsSettlement)
            {
                float currentTierMax = GetStrengthRange(tier).max;
                offensiveStrength = Mathf.Clamp(offensiveStrength + amount, 0f, currentTierMax);
            }
        }

        public void AddStrengthNoTierUpgradeSplitEvenlyWithOverflow(float amount)
        {
            if (amount <= 0f) return;
            if (IsPlayerMapSettlement) return;

            float offensiveMax = IsOutpost
                ? GetTargetOutpostStrength()
                : (IsSettlement ? GetStrengthRange(tier).max : float.MaxValue);
            float defensiveMax = GetBaseDefensiveStrength();
            float offensiveRoom = Mathf.Max(0f, offensiveMax - offensiveStrength);
            float defensiveRoom = Mathf.Max(0f, defensiveMax - defensiveStrength);
            float share = amount * 0.5f;
            float offensiveGain = Mathf.Min(share, offensiveRoom);
            float defensiveGain = Mathf.Min(share, defensiveRoom);
            float overflow = Mathf.Max(0f, amount - offensiveGain - defensiveGain);

            float extraOffensive = Mathf.Min(overflow, offensiveRoom - offensiveGain);
            offensiveGain += extraOffensive;
            overflow -= extraOffensive;

            float extraDefensive = Mathf.Min(overflow, defensiveRoom - defensiveGain);
            defensiveGain += extraDefensive;

            offensiveStrength += offensiveGain;
            defensiveStrength += defensiveGain;
        }

        /// <summary>WD trader arrival: flat strength reward, optional tier promotion at cap (bypasses growth neighbor gates).</summary>
        public TraderArrivalRewardOutcome ApplyTraderArrivalReward(float amount, float chanceT1ToT2, float chanceT2ToT3, float chanceT3ToT4)
        {
            if (amount <= 0f) return TraderArrivalRewardOutcome.NoEffect;
            if (IsPlayerMapSettlement) return TraderArrivalRewardOutcome.NoEffect;

            if (IsOutpost)
            {
                AddStrengthNoTierUpgrade(amount);
                return TraderArrivalRewardOutcome.StrengthOnly;
            }

            if (!IsSettlement)
            {
                AddStrengthNoTierUpgrade(amount);
                return TraderArrivalRewardOutcome.StrengthOnly;
            }

            if (tier == SettlementTier.T4)
            {
                AddStrengthNoTierUpgrade(amount);
                return TraderArrivalRewardOutcome.StrengthOnly;
            }

            FloatRange r = GetStrengthRange(tier);
            float tierMax = r.max;
            const float eps = 0.01f;
            bool atCap = offensiveStrength >= tierMax - eps;
            bool wouldExceed = offensiveStrength + amount > tierMax + eps;

            if (!atCap && !wouldExceed)
            {
                AddStrengthNoTierUpgrade(amount);
                return TraderArrivalRewardOutcome.StrengthOnly;
            }

            float chance = tier == SettlementTier.T1 ? chanceT1ToT2 : (tier == SettlementTier.T2 ? chanceT2ToT3 : chanceT3ToT4);
            chance = Mathf.Clamp01(chance);
            if (Rand.Value < chance && TryGetNextTraderTier(tier, out SettlementTier nextTier))
            {
                PromoteSettlementTierForTraderArrival(nextTier, amount);
                return TraderArrivalRewardOutcome.StrengthAndTierUp;
            }

            AddStrengthNoTierUpgrade(amount);
            return TraderArrivalRewardOutcome.StrengthOnly;
        }

        private static bool TryGetNextTraderTier(SettlementTier current, out SettlementTier next)
        {
            switch (current)
            {
                case SettlementTier.T1:
                    next = SettlementTier.T2;
                    return true;
                case SettlementTier.T2:
                    next = SettlementTier.T3;
                    return true;
                case SettlementTier.T3:
                    next = SettlementTier.T4;
                    return true;
                default:
                    next = current;
                    return false;
            }
        }

        private void PromoteSettlementTierForTraderArrival(SettlementTier newTier, float rewardAmount)
        {
            if (IsOutpost || IsPlayerMapSettlement) return;
            tier = newTier;
            subType = GetRandomSubType(newTier);
            FloatRange nr = GetStrengthRange(newTier);
            offensiveStrength = Mathf.Clamp(offensiveStrength + rewardAmount, nr.min, nr.max);
            defensiveStrength = GetBaseDefensiveStrength();
            lastRadiusUpdateTick = -9999;
            UpdateInterceptorRegistration();
        }

        /// <summary>
        /// Gift/buy investment tier-up: pay already deducted by caller. Rolls investment upgrade success chance.
        /// </summary>
        public enum InvestmentPromoteResult : byte { Ineligible, FailedRoll, Promoted }

        public InvestmentPromoteResult TryPromoteTierFromInvestment()
        {
            if (IsOutpost || IsPlayerMapSettlement || !IsSettlement) return InvestmentPromoteResult.Ineligible;
            if (!TryGetNextTraderTier(tier, out SettlementTier nextTier)) return InvestmentPromoteResult.Ineligible;

            var s = WorldDominationMod.settings;
            float chance = s?.factionInvestmentUpgradeSuccessChance
                ?? WorldDominationSettings.DefFactionInvestmentUpgradeSuccessChance;
            if (Rand.Value >= Mathf.Clamp01(chance))
                return InvestmentPromoteResult.FailedRoll;

            tier = nextTier;
            subType = GetRandomSubType(nextTier);
            FloatRange nr = GetStrengthRange(nextTier);
            offensiveStrength = Mathf.Clamp(offensiveStrength, nr.min, nr.max);
            defensiveStrength = GetBaseDefensiveStrength();
            lastRadiusUpdateTick = -9999;
            UpdateInterceptorRegistration();
            return InvestmentPromoteResult.Promoted;
        }

        /// <summary>Debug / cheats: shift strength without changing tier. Settlements: tier band. Outposts: cap at occupant-based max (GetTargetOutpostStrength). Does not call CheckTierUpdate.</summary>
        public void AdjustStrengthWithinTier(float delta)
        {
            if (IsPlayerMapSettlement) return;
            if (IsOutpost)
            {
                float maxStr = GetOutpostOffensiveRegenTarget();
                offensiveStrength = Mathf.Clamp(offensiveStrength + delta, 0f, maxStr);
                return;
            }
            FloatRange r = GetStrengthRange(tier);
            offensiveStrength = Mathf.Clamp(offensiveStrength + delta, r.min, r.max);
        }

        /// <summary>Raid (or similar) loss: same fraction removed from offensive and defensive so total drops by that fraction of prior total (e.g. 50% loss halves both pools).</summary>
        public void ReduceStrength(float percentage, bool allowDemotion = false)
        {
            if (IsPlayerMapSettlement) return;
            float f = 1f - Mathf.Clamp01(percentage);
            offensiveStrength *= f;
            defensiveStrength *= f;
            offensiveStrength = Mathf.Max(0f, offensiveStrength);
            defensiveStrength = Mathf.Max(0f, defensiveStrength);
            if (IsSettlement && parent?.Faction != null && !parent.Faction.IsPlayer && offensiveStrength < 100f) offensiveStrength = 100f;
            if (IsSettlement) CheckTierUpdate(allowDemotion);
        }

        /// <summary>
        /// Defender-ally raid loss: removes an absolute amount from OFFENSIVE strength only (the detachment the ally lent),
        /// not a percentage of the whole garrison. Mirrors the non-player min-100 floor and tier recheck of <see cref="ReduceStrength"/>.
        /// Used so an ally that only committed its available raid strength does not lose a percentage of its entire pool.
        /// </summary>
        public void ReduceOffensiveByAmount(float amount, bool allowDemotion = false)
        {
            if (IsPlayerMapSettlement) return;
            if (amount <= 0f) return;
            offensiveStrength = Mathf.Max(0f, offensiveStrength - amount);
            if (IsSettlement && parent?.Faction != null && !parent.Faction.IsPlayer && offensiveStrength < 100f) offensiveStrength = 100f;
            if (IsSettlement) CheckTierUpdate(allowDemotion);
        }

        public void CheckTierUpdate(bool allowDemotion = false)
        {
            if (IsOutpost || IsPlayerMapSettlement) return;

            SettlementTier oldTier = tier;

            float currentMax = (tier == SettlementTier.T1) ? 500f :
                               (tier == SettlementTier.T2) ? 1000f :
                               (tier == SettlementTier.T3) ? 1600f : 2250f;

            if (offensiveStrength > currentMax) offensiveStrength = currentMax;

            if (offensiveStrength > 1600f) tier = SettlementTier.T4;
            else if (offensiveStrength > 1000f) tier = SettlementTier.T3;
            else if (offensiveStrength > 500f) tier = SettlementTier.T2;

            if (allowDemotion)
            {
                if (offensiveStrength <= 500f) tier = SettlementTier.T1;
                else if (offensiveStrength <= 1000f) tier = SettlementTier.T2;
                else if (offensiveStrength <= 1600f) tier = SettlementTier.T3;
            }
            else
            {
                if (tier < oldTier) tier = oldTier;
            }

            if (oldTier != tier)
            {
                subType = GetRandomSubType(tier);
                defensiveStrength = GetBaseDefensiveStrength();
                lastRadiusUpdateTick = -9999;
                UpdateInterceptorRegistration();
            }
        }

        /// <summary>
        /// Like <see cref="CheckTierUpdate"/>(allowDemotion: true), but demotion is capped to at most
        /// <paramref name="maxDemotionSteps"/> tiers. Settlement incidents use this so one hit cannot
        /// collapse e.g. T4 straight to T1; destruction when strength goes negative stays the caller's job.
        /// </summary>
        public void CheckTierUpdateLimitedDemotion(int maxDemotionSteps = 1)
        {
            if (IsOutpost || IsPlayerMapSettlement) return;
            if (maxDemotionSteps < 0) maxDemotionSteps = 0;

            SettlementTier oldTier = tier;
            int minOrdinal = Mathf.Max((int)SettlementTier.T1, (int)oldTier - maxDemotionSteps);
            SettlementTier minTier = (SettlementTier)minOrdinal;

            CheckTierUpdate(true);

            if (tier >= minTier) return;

            SettlementTier overDemoted = tier;
            tier = minTier;
            if (overDemoted != tier)
            {
                subType = GetRandomSubType(tier);
                defensiveStrength = GetBaseDefensiveStrength();
                lastRadiusUpdateTick = -9999;
                UpdateInterceptorRegistration();
            }
        }

        public static FloatRange GetStrengthRange(SettlementTier t)
        {
            if (t == SettlementTier.T4) return new FloatRange(1601f, 2250f);
            if (t == SettlementTier.T3) return new FloatRange(1001f, 1600f);
            if (t == SettlementTier.T2) return new FloatRange(501f, 1000f);
            return new FloatRange(100f, 500f);
        }

        private static readonly string[] SubTypesT1 = { "Logging", "Mining", "Farming" };
        private static readonly string[] SubTypesT2 = { "Production", "Slavery" };

        private string GetRandomSubType(SettlementTier forTier)
        {
            switch (forTier)
            {
                case SettlementTier.T1:
                    return SubTypesT1.RandomElement();
                case SettlementTier.T2:
                    return SubTypesT2.RandomElement();
                case SettlementTier.T3:
                    return "Fortress";
                case SettlementTier.T4:
                    return "Citadel";
                default:
                    return "Generic";
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref tier, "tier", SettlementTier.T1);
            Scribe_Values.Look(ref subType, "subType", "");
            float legacyStrength = offensiveStrength;
            Scribe_Values.Look(ref offensiveStrength, "offensiveStrength", -1f);
            Scribe_Values.Look(ref defensiveStrength, "defensiveStrength", -1f);
            Scribe_Values.Look(ref legacyStrength, "strength", -1f);

            // --- INDEPENDENT COOLDOWNS ---
            Scribe_Values.Look(ref raidCooldownTick, "raidCooldownTick", -1);
            Scribe_Values.Look(ref expansionCooldownTick, "expansionCooldownTick", -1);
            Scribe_Values.Look(ref roadCooldownTick, "roadCooldownTick", -1);
            Scribe_Values.Look(ref fortifyCooldownTick, "fortifyCooldownTick", -1);
            Scribe_Values.Look(ref fortifyBuilderFactionId, "fortifyBuilderFactionId", -1);
            Scribe_Values.Look(ref fortifyTurretPending, "fortifyTurretPending", false);
            Scribe_Values.Look(ref traderCooldownTick, "traderCooldownTick", -1);
            Scribe_Values.Look(ref ambushCooldownTick, "ambushCooldownTick", -1);
            Scribe_Values.Look(ref playerColonyWdTraderCooldownTick, "playerColonyWdTraderCooldownTick", -1);
            Scribe_Values.Look(ref lastCaravanInterceptedTick, "lastCaravanInterceptedTick", -99999);
            Scribe_Values.Look(ref defenseCooldownTick, "defenseCooldownTick", -1);
            Scribe_Values.Look(ref lastPlayerColonyWdRaidPickTick, "lastPlayerColonyWdRaidPickTick", -1);
            Scribe_Values.Look(ref incidentCooldownTick, "incidentCooldownTick", -1);
            Scribe_Values.Look(ref mortarCooldownTick, "mortarCooldownTick", -1);
            Scribe_Values.Look(ref antiAirCooldownTick, "antiAirCooldownTick", -1);
            Scribe_Values.Look(ref t4MortarAutoActive, "t4MortarAutoActive", true);
            Scribe_Values.Look(ref t4AntiAirAutoActive, "t4AntiAirAutoActive", true);
            Scribe_Values.Look(ref autoFeedTransferredPawns, "wdAutoFeedTransferredPawns", true);

            Scribe_Values.Look(ref espionageCooldownUntilTick, "espionageCooldownUntilTick", -1);
            Scribe_Values.Look(ref aidCooldownUntilTick, "aidCooldownUntilTick", -1);

            // --- CAPACITY TRACKING ---
            Scribe_Values.Look(ref actionsTakenToday, "actionsTakenToday", 0);
            Scribe_Values.Look(ref lastActionDay, "lastActionDay", -1);

            Scribe_Values.Look(ref outpostInitialized, "outpostInitialized", false);
            Scribe_Values.Look(ref appliedInitialPlayerColonyShield, "appliedInitialPlayerColonyShield", false);
            Scribe_Values.Look(ref ticksExisted, "ticksExisted", 0);
            Scribe_Values.Look(ref attackRangeFoundingTick, "attackRangeFoundingTick", -1);
            // Migrate: old saves used global day ramp; seed founding so age ≈ current global progress.
            if (Scribe.mode == LoadSaveMode.PostLoadInit && attackRangeFoundingTick < 0 && IsSettlement
                && parent?.Faction != null && !parent.Faction.IsPlayer)
            {
                int now = Find.TickManager?.TicksGame ?? 0;
                float daysToMax = WorldDominationMod.settings?.attackRangeDaysToMax
                    ?? WorldDominationSettings.DefAttackRangeDaysToMax;
                int maxAgeTicks = Mathf.RoundToInt(Mathf.Max(1f, daysToMax) * 60000f);
                attackRangeFoundingTick = Mathf.Max(0, now - maxAgeTicks);
            }
            Scribe_Values.Look(ref lastTargetOutpostStrength, "lastTargetOutpostStrength", -1f);
            Scribe_Values.Look(ref roadTargetTile, "roadTargetTile", -1);
            Scribe_Values.Look(ref roadProgress, "roadProgress", 0f);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && roadProgress > 1f)
                roadProgress = 1f;
            Scribe_Values.Look(ref roadTargetName, "roadTargetName", string.Empty);
            Scribe_Values.Look(ref selectedRoadTier, "selectedRoadTier", SettlementTier.T1);
            Scribe_Values.Look(ref roadIsClearing, "roadIsClearing", false);
            Scribe_Values.Look(ref playerOrderedRoad, "playerOrderedRoad", false);
            Scribe_Values.Look(ref playerOrderedRoadGoodwillPaid, "playerOrderedRoadGoodwillPaid", 0);
            Scribe_Values.Look(ref playerOrderedRoadGoodwillRefunded, "playerOrderedRoadGoodwillRefunded", false);
            Scribe_Values.Look(ref playerOrderedRoadInitialSegments, "playerOrderedRoadInitialSegments", 0);
            Scribe_Values.Look(ref playerOrderedRoadBaseCost, "playerOrderedRoadBaseCost", 0);
            Scribe_Values.Look(ref playerOrderedRoadPerSegmentRate, "playerOrderedRoadPerSegmentRate", 0f);
            Scribe_Values.Look(ref roadTargetUsesDetachedStart, "roadTargetUsesDetachedStart", false);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
                Scribe_Values.Look(ref legacyPlayerOrderedRoadPerSegmentCost, "playerOrderedRoadPerSegmentCost", 0);

            // --- ROAD CACHE PERSISTENCE ---
            Scribe_Collections.Look(ref cachedRoadPathTiles, "cachedRoadPathTiles", LookMode.Value);
            Scribe_Collections.Look(ref roadWaypointTiles, "roadWaypointTiles", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && roadWaypointTiles == null)
                roadWaypointTiles = new List<int>();
            Scribe_Values.Look(ref lastPathSourceTile, "lastPathSourceTile", -1);
            Scribe_Values.Look(ref cachedWorkTile, "cachedWorkTile", -1);
            Scribe_Values.Look(ref builderInField, "builderInField", false);
            Scribe_Values.Look(ref lastRoadProgressTick, "lastRoadProgressTick", -1);
            Scribe_Values.Look(ref lastStrengthRegenTick, "lastStrengthRegenTick", -99999);
            Scribe_Values.Look(ref lastPollutionSiteDamageTick, "lastPollutionSiteDamageTick", -1);
            Scribe_Values.Look(ref lastNpcDecontamAssessTick, "lastNpcDecontamAssessTick", -1);

            Scribe_Collections.Look(ref roadBlockPlannedTiles, "roadBlockPlannedTiles", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && roadBlockPlannedTiles == null)
                roadBlockPlannedTiles = new List<int>();
            Scribe_Collections.Look(ref roadBlockClickedNodes, "roadBlockClickedNodes", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && roadBlockClickedNodes == null)
                roadBlockClickedNodes = new List<int>();
            Scribe_Collections.Look(ref roadBlockCachedPathTiles, "roadBlockCachedPathTiles", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && roadBlockCachedPathTiles == null)
                roadBlockCachedPathTiles = new List<int>();
            Scribe_Values.Look(ref roadBlockWorkIndex, "roadBlockWorkIndex", 0);
            Scribe_Values.Look(ref roadBlockProgress, "roadBlockProgress", 0f);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && roadBlockProgress > 1f)
                roadBlockProgress = 1f;
            Scribe_Values.Look(ref roadBlockIsClearing, "roadBlockIsClearing", false);
            Scribe_Values.Look(ref roadBlockClearAnyFortification, "roadBlockClearAnyFortification", false);
            Scribe_Values.Look(ref roadBlockCachedWorkTile, "roadBlockCachedWorkTile", -1);
            Scribe_Values.Look(ref roadBlockTargetName, "roadBlockTargetName", string.Empty);
            Scribe_Values.Look(ref selectedRoadBlockKind, "selectedRoadBlockKind", RoadBlockKind.Normal);
            Scribe_Values.Look(ref roadBlockBuilderInField, "roadBlockBuilderInField", false);
            Scribe_Values.Look(ref lastRoadBlockProgressTick, "lastRoadBlockProgressTick", -1);

            Scribe_Collections.Look(ref spikeTrapPlannedTiles, "spikeTrapPlannedTiles", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && spikeTrapPlannedTiles == null)
                spikeTrapPlannedTiles = new List<int>();
            Scribe_Collections.Look(ref spikeTrapClickedNodes, "spikeTrapClickedNodes", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && spikeTrapClickedNodes == null)
                spikeTrapClickedNodes = new List<int>();
            Scribe_Collections.Look(ref spikeTrapCachedPathTiles, "spikeTrapCachedPathTiles", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && spikeTrapCachedPathTiles == null)
                spikeTrapCachedPathTiles = new List<int>();
            Scribe_Values.Look(ref spikeTrapWorkIndex, "spikeTrapWorkIndex", 0);
            Scribe_Values.Look(ref spikeTrapProgress, "spikeTrapProgress", 0f);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && spikeTrapProgress > 1f)
                spikeTrapProgress = 1f;
            Scribe_Values.Look(ref spikeTrapIsClearing, "spikeTrapIsClearing", false);
            Scribe_Values.Look(ref spikeTrapCachedWorkTile, "spikeTrapCachedWorkTile", -1);
            Scribe_Values.Look(ref spikeTrapTargetName, "spikeTrapTargetName", string.Empty);
            Scribe_Values.Look(ref selectedSpikeTrapKind, "selectedSpikeTrapKind", SpikeTrapKind.Spike);
            Scribe_Values.Look(ref spikeTrapBuilderInField, "spikeTrapBuilderInField", false);
            Scribe_Values.Look(ref lastSpikeTrapProgressTick, "lastSpikeTrapProgressTick", -1);
            Scribe_Collections.Look(ref atTurretPlannedTiles, "atTurretPlannedTiles", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && atTurretPlannedTiles == null)
                atTurretPlannedTiles = new List<int>();
            int legacyAtTurretPlannedTile = -1;
            Scribe_Values.Look(ref legacyAtTurretPlannedTile, "atTurretPlannedTile", -1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit
                && legacyAtTurretPlannedTile >= 0
                && atTurretPlannedTiles.Count == 0)
            {
                atTurretPlannedTiles.Add(legacyAtTurretPlannedTile);
            }
            Scribe_Values.Look(ref atTurretWorkIndex, "atTurretWorkIndex", 0);
            Scribe_Values.Look(ref atTurretCachedWorkTile, "atTurretCachedWorkTile", -1);
            Scribe_Values.Look(ref atTurretProgress, "atTurretProgress", 0f);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && atTurretProgress > 1f)
                atTurretProgress = 1f;
            Scribe_Values.Look(ref atTurretTargetName, "atTurretTargetName", string.Empty);
            Scribe_Values.Look(ref selectedAtTurretTier, "selectedAtTurretTier", AtTurretTier.Medium);
            Scribe_Values.Look(ref atTurretBuilderInField, "atTurretBuilderInField", false);
            Scribe_Values.Look(ref lastAtTurretProgressTick, "lastAtTurretProgressTick", -1);

            Scribe_Collections.Look(ref decontamPlannedTiles, "decontamPlannedTiles", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && decontamPlannedTiles == null)
                decontamPlannedTiles = new List<int>();
            Scribe_Collections.Look(ref decontamClickedNodes, "decontamClickedNodes", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && decontamClickedNodes == null)
                decontamClickedNodes = new List<int>();
            Scribe_Collections.Look(ref decontamCachedPathTiles, "decontamCachedPathTiles", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && decontamCachedPathTiles == null)
                decontamCachedPathTiles = new List<int>();
            Scribe_Values.Look(ref decontamWorkIndex, "decontamWorkIndex", 0);
            Scribe_Values.Look(ref decontamProgress, "decontamProgress", 0f);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && decontamProgress > 1f)
                decontamProgress = 1f;
            Scribe_Values.Look(ref decontamCachedWorkTile, "decontamCachedWorkTile", -1);
            Scribe_Values.Look(ref decontamTargetName, "decontamTargetName", string.Empty);
            Scribe_Values.Look(ref decontamBuilderInField, "decontamBuilderInField", false);
            Scribe_Values.Look(ref lastDecontamProgressTick, "lastDecontamProgressTick", -1);

            Scribe_Values.Look(ref redirectionTargetTile, "redirectionTargetTile", -1);

            if (Scribe.mode == LoadSaveMode.PostLoadInit && roadTargetTile != -1 && builderInField && !WorldActions_Roads.HasActiveRoadBuilderFrom(parent))
                builderInField = false;
            if (Scribe.mode == LoadSaveMode.PostLoadInit
                && WorldActions_RoadBlocks.HasActiveRoadBlockProject(this)
                && roadBlockBuilderInField
                && !WorldActions_RoadBlocks.HasActiveRoadBlockCrewFrom(parent))
                roadBlockBuilderInField = false;
            if (Scribe.mode == LoadSaveMode.PostLoadInit
                && WorldActions_SpikeTraps.HasActiveSpikeTrapProject(this)
                && spikeTrapBuilderInField
                && !WorldActions_SpikeTraps.HasActiveSpikeTrapCrewFrom(parent))
                spikeTrapBuilderInField = false;
            if (Scribe.mode == LoadSaveMode.PostLoadInit
                && WorldActions_AtTurrets.HasActiveAtTurretProject(this)
                && atTurretBuilderInField
                && !WorldActions_AtTurrets.HasActiveAtTurretCrewFrom(parent))
                atTurretBuilderInField = false;
            if (Scribe.mode == LoadSaveMode.PostLoadInit
                && WorldActions_Decontamination.HasActiveDecontaminationProject(this)
                && decontamBuilderInField
                && !WorldActions_Decontamination.HasActiveDecontaminationCrewFrom(parent))
                decontamBuilderInField = false;

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (parent != null && WorldActions_Utils.IsConfirmedOrbitWorldObject(parent))
                {
                    parent.AllComps.Remove(this);
                    return;
                }

                lastRoadProgressTick = -1;
                lastRoadBlockProgressTick = -1;
                lastSpikeTrapProgressTick = -1;
                lastDecontamProgressTick = -1;
                if (playerOrderedRoadPerSegmentRate <= 0f && legacyPlayerOrderedRoadPerSegmentCost > 0)
                    playerOrderedRoadPerSegmentRate = legacyPlayerOrderedRoadPerSegmentCost;
                if (offensiveStrength < 0f)
                    offensiveStrength = legacyStrength >= 0f ? legacyStrength : 100f;
                if (defensiveStrength < 0f)
                    defensiveStrength = GetBaseDefensiveStrength();
                if (IsPlayerMapSettlement)
                {
                    offensiveStrength = 0f;
                    defensiveStrength = 0f;
                }
                else if (IsOutpost)
                {
                    // Occupant virtual-pawn caches may still be empty here; clamping against GetTargetOutpostStrength()
                    // would cap everyone at the empty-outpost floor (~100). WorldObject_WD_Outpost PostLoadInit calls
                    // UpdateOutpostStrengthLogically once occupants are ready.
                    if (string.IsNullOrEmpty(subType) || subType == "Excluded")
                        subType = "Outpost";
                }

                UpdateInterceptorRegistration();
            }
        }

        public override string CompInspectStringExtra()
        {
            int tickNow = Find.TickManager.TicksGame;
            WD_RadiusOverlayKind radiusKindNow = WD_RadiusOverlayKind.Off;
            if (WD_RadiusOverlayPrefs.TryGetCategory(parent, out WD_RadiusOverlayCategory radiusCatNow))
                radiusKindNow = WD_RadiusOverlayPrefs.Get(radiusCatNow);
            if (cachedInspectString != null
                && tickNow - cachedInspectTick < 60
                && radiusKindNow == cachedInspectRadiusKind)
                return cachedInspectString;

            StringBuilder sb = new StringBuilder();
            var seth = WorldDominationMod.settings;
            float offensiveMax = GetMaxOffensiveStrength();
            float defensiveMax = GetBaseDefensiveStrength();
            float totalCurrent = offensiveStrength + defensiveStrength;
            float totalMax = offensiveMax + defensiveMax;

            if (IsSettlement)
            {
                if (parent.Faction != null && parent.Faction.IsPlayer)
                {
                    sb.Append("TSA_WD_Inspect_PlayerColony".Translate());
                    if (ColonyWorldBuildUtility.IsPlayerColonyBuildActor(parent))
                        AppendActiveConstructionInspectLines(sb);
                }
                else
                {
                    string tierKey = (tier == SettlementTier.T4) ? "TSA_WD_Tier4" :
                                     (tier == SettlementTier.T3) ? "TSA_WD_Tier3" :
                                     (tier == SettlementTier.T2) ? "TSA_WD_Tier2" : "TSA_WD_Tier1";
                    string subTypeKey = "TSA_WD_SubType_" + subType;
                    sb.Append("TSA_WD_Inspect_Settlement".Translate(tierKey.Translate(), subTypeKey.Translate()));
                    sb.AppendLine();
                    sb.Append("TSA_WD_Inspect_StrengthSimpleLine".Translate(
                        totalCurrent.ToString("F0"), totalMax.ToString("F0")));
                }
            }
            else
            {
                if (parent is WorldObject_WD_Outpost wdOutpost)
                {
                    sb.Append("TSA_WD_Inspect_StrengthWithHumanoidsLine".Translate(
                        totalCurrent.ToString("F0"),
                        totalMax.ToString("F0"),
                        wdOutpost.PawnCount.ToString()));
                }
                else
                {
                    sb.Append("TSA_WD_Inspect_StrengthSimpleLine".Translate(
                        totalCurrent.ToString("F0"), totalMax.ToString("F0")));
                }

                AppendActiveConstructionInspectLines(sb);
            }

            bool isPlayerColonySettlement = parent.Faction != null && parent.Faction.IsPlayer && IsSettlement;
            bool isPlayerWdOutpost = parent.Faction != null && parent.Faction.IsPlayer && IsOutpost;
            bool shouldShowRaidProtectionState = isPlayerColonySettlement || isPlayerWdOutpost;

            if (isPlayerWdOutpost && parent is WorldObject_WD_Outpost playerOutpost)
            {
                int prisonerCount = playerOutpost.Prisoners.Count;
                if (prisonerCount > 0)
                {
                    sb.AppendLine();
                    sb.Append("TSA_WD_Inspect_HoldsPrisoners".Translate(prisonerCount).ToString().Colorize(Color.yellow));
                }
            }

            // --- REFACTORED COOLDOWNS ---
            int ticksNow = Find.TickManager.TicksGame;

            if (IsEspionageOnCooldown)
            {
                float daysLeft = (espionageCooldownUntilTick - ticksNow) / 60000f;
                sb.AppendLine();
                sb.Append("TSA_WD_Inspect_EspionageCD".Translate(daysLeft.ToString("F1")).Colorize(Color.red));
            }
            if (IsDefenseOnCooldown)
            {
                float daysLeft = (defenseCooldownTick - ticksNow) / 60000f;
                sb.AppendLine();
                sb.Append("TSA_WD_Inspect_DefenseCD".Translate(daysLeft.ToString("F1")).Colorize(Color.green));
            }
            else if (shouldShowRaidProtectionState)
            {
                sb.AppendLine();
                sb.Append(isPlayerColonySettlement ? GetColonyRaidVulnerableLabel() : GetRaidVulnerableLabel());
            }
            if (IsIncidentOnCooldown)
            {
                float daysLeft = (incidentCooldownTick - ticksNow) / 60000f;
                sb.AppendLine();
                sb.Append("TSA_WD_Inspect_IncidentCD".Translate(daysLeft.ToString("F1")).Colorize(Color.magenta));
            }
            if (IsAidOnCooldown)
            {
                float daysLeft = (aidCooldownUntilTick - ticksNow) / 60000f;
                sb.AppendLine();
                sb.Append("TSA_WD_Inspect_AidCD".Translate(daysLeft.ToString("F1")).Colorize(Color.gray));
            }
            // T4 mortar / AA status — same ready/CD pattern as player mortar outposts.
            if (IsSettlementMortarInterceptorEligible())
            {
                sb.AppendLine();
                if (IsMortarOnCooldown)
                {
                    float daysLeft = (mortarCooldownTick - ticksNow) / 60000f;
                    sb.Append("TSA_WD_Inspect_MortarCD".Translate(daysLeft.ToString("F1")).Colorize(Color.cyan));
                }
                else
                    sb.Append("TSA_WD_Inspect_MortarReady".Translate().Colorize(Color.cyan));
            }
            if (IsSettlementAntiAirEligible())
            {
                sb.AppendLine();
                if (!IsSettlementAntiAirAutoActive)
                    sb.Append("TSA_WD_AntiAir_Auto_Off".Translate().Colorize(Color.gray));
                else if (IsAntiAirOnCooldown)
                {
                    float secLeft = (antiAirCooldownTick - ticksNow) / 60f;
                    sb.Append("TSA_WD_Inspect_AntiAirCD".Translate(secLeft.ToString("F0")).Colorize(Color.cyan));
                }
                else
                    sb.Append("TSA_WD_Inspect_AntiAirReady".Translate().Colorize(Color.cyan));
            }
            if (parent.Faction != null && !parent.Faction.IsPlayer && IsSettlement)
            {
                AppendActiveRadiusInspectLine(sb);
            }

            cachedInspectString = sb.ToString().Trim();
            if (isPlayerColonySettlement)
                cachedInspectString = ApplyPlayerSettlementInspectColors(cachedInspectString);
            cachedInspectTick = tickNow;
            cachedInspectRadiusKind = WD_RadiusOverlayPrefs.TryGetCategory(parent, out WD_RadiusOverlayCategory cat)
                ? WD_RadiusOverlayPrefs.Get(cat)
                : WD_RadiusOverlayKind.Off;
            return cachedInspectString;
        }

        /// <summary>Road / road-block / spike-trap / decontam progress lines shared by outposts and colony world-build.</summary>
        private void AppendActiveConstructionInspectLines(StringBuilder sb)
        {
            if (roadTargetTile != -1)
            {
                sb.AppendLine();
                string insufficient = GetInsufficientStrengthConstructionMessage();
                if (insufficient != null)
                {
                    sb.Append(insufficient.Colorize(Color.red));
                }
                else if (roadIsClearing)
                {
                    string roadPct = (Mathf.Min(1f, roadProgress) * 100f).ToString("F0");
                    if (builderInField)
                        sb.Append("TSA_WD_Inspect_RoadClearStatus_InTransit".Translate(roadTargetName, roadPct));
                    else
                        sb.Append("TSA_WD_Inspect_RoadClearStatus".Translate(roadTargetName, roadPct));
                }
                else
                {
                    string roadTypeLabel = GetActiveRoadProjectLabel();
                    string roadPct = (Mathf.Min(1f, roadProgress) * 100f).ToString("F0");

                    if (builderInField)
                        sb.Append("TSA_WD_Inspect_RoadStatus_InTransit".Translate(roadTypeLabel, roadTargetName, roadPct));
                    else
                        sb.Append("TSA_WD_Inspect_RoadStatus".Translate(roadTypeLabel, roadPct, roadTargetName));
                }
            }
            else if (WorldActions_RoadBlocks.HasActiveRoadBlockProject(this))
            {
                sb.AppendLine();
                string dest = roadBlockTargetName.NullOrEmpty() ? "…" : roadBlockTargetName;
                string insufficient = GetInsufficientStrengthConstructionMessage();
                if (insufficient != null)
                {
                    sb.Append(insufficient.Colorize(Color.red));
                }
                else
                {
                    string blockLabel = GetActiveRoadBlockProjectLabel();
                    string blockPct = (Mathf.Min(1f, roadBlockProgress) * 100f).ToString("F0");
                    if (roadBlockBuilderInField)
                        sb.Append("TSA_WD_Inspect_RoadBlockStatus_InTransit".Translate(blockLabel, dest, blockPct));
                    else
                        sb.Append("TSA_WD_Inspect_RoadBlockStatus".Translate(blockLabel, blockPct, dest));
                }
            }
            else if (WorldActions_SpikeTraps.HasActiveSpikeTrapProject(this))
            {
                sb.AppendLine();
                string dest = spikeTrapTargetName.NullOrEmpty() ? "…" : spikeTrapTargetName;
                string insufficient = GetInsufficientStrengthConstructionMessage();
                if (insufficient != null)
                {
                    sb.Append(insufficient.Colorize(Color.red));
                }
                else
                {
                    string trapLabel = GetActiveSpikeTrapProjectLabel();
                    string trapPct = (Mathf.Min(1f, spikeTrapProgress) * 100f).ToString("F0");
                    if (spikeTrapBuilderInField)
                        sb.Append("TSA_WD_Inspect_SpikeTrapStatus_InTransit".Translate(trapLabel, dest, trapPct));
                    else
                        sb.Append("TSA_WD_Inspect_SpikeTrapStatus".Translate(trapLabel, trapPct, dest));
                }
            }
            else if (WorldActions_AtTurrets.HasActiveAtTurretProject(this))
            {
                sb.AppendLine();
                string dest = atTurretTargetName.NullOrEmpty() ? "…" : atTurretTargetName;
                string insufficient = GetInsufficientStrengthConstructionMessage();
                if (insufficient != null)
                {
                    sb.Append(insufficient.Colorize(Color.red));
                }
                else
                {
                    string turretLabel = AtTurretUtility.LabelKey(selectedAtTurretTier).Translate();
                    string turretPct = (Mathf.Min(1f, atTurretProgress) * 100f).ToString("F0");
                    if (atTurretBuilderInField)
                        sb.Append("TSA_WD_Inspect_AT_TurretStatus_InTransit".Translate(turretLabel, dest, turretPct));
                    else
                        sb.Append("TSA_WD_Inspect_AT_TurretStatus".Translate(turretLabel, turretPct, dest));
                }
            }
            else if (WorldActions_Decontamination.HasActiveDecontaminationProject(this))
            {
                sb.AppendLine();
                string dest = decontamTargetName.NullOrEmpty() ? "…" : decontamTargetName;
                string insufficient = GetInsufficientStrengthConstructionMessage();
                if (insufficient != null)
                {
                    sb.Append(insufficient.Colorize(Color.red));
                }
                else
                {
                    string scrubLabel = "TSA_WD_Inspect_DecontaminationBuild".Translate();
                    string scrubPct = (Mathf.Min(1f, decontamProgress) * 100f).ToString("F0");
                    if (decontamBuilderInField)
                        sb.Append("TSA_WD_Inspect_DecontaminationStatus_InTransit".Translate(scrubLabel, dest, scrubPct));
                    else
                        sb.Append("TSA_WD_Inspect_DecontaminationStatus".Translate(scrubLabel, scrubPct, dest));
                }
            }
        }

        private void AppendActiveRadiusInspectLine(StringBuilder sb)
        {
            if (!WD_RadiusOverlayPrefs.TryGetCategory(parent, out WD_RadiusOverlayCategory category))
                return;

            WD_RadiusOverlayKind kind = WD_RadiusOverlayPrefs.Get(category);
            if (kind == WD_RadiusOverlayKind.Off)
                kind = WD_RadiusOverlayPrefs.DefaultKind(category);

            if (!WD_RadiusOverlayPrefs.TryResolve(parent, kind, out float radius, out _, out _))
                return;

            sb.AppendLine();
            switch (kind)
            {
                case WD_RadiusOverlayKind.Ally:
                    sb.Append("TSA_WD_Inspect_AllyRadius".Translate(radius.ToString("F0")).Colorize(Color.cyan));
                    break;
                case WD_RadiusOverlayKind.Mortar:
                    sb.Append("TSA_WD_Inspect_MortarRadius".Translate(radius.ToString("F0")).Colorize(Color.white));
                    break;
                case WD_RadiusOverlayKind.AA:
                    sb.Append("TSA_WD_Inspect_AARadius".Translate(radius.ToString("F0")).Colorize(Color.white));
                    break;
                default:
                {
                    var manager = SpreadManager;
                    bool hasZeal = manager != null
                        && parent.Faction == manager.expansionistZealFaction
                        && Find.TickManager.TicksGame < manager.expansionistZealExpiryTick;
                    string rangeLabel = hasZeal ? "TSA_WD_Inspect_AttackRange_Zeal" : "TSA_WD_Inspect_AttackRange";
                    sb.Append(rangeLabel.Translate(radius.ToString("F0")).Colorize(hasZeal ? Color.cyan : Color.white));
                    break;
                }
            }
        }
    }
}

