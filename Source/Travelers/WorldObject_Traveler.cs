using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public enum TravelerMission { Expansion, Raid, RoadBuilding, RoadBlock, SpikeTrap, Decontamination, OutpostDelivery, Trader, OutpostUpgrade, MortarStrike, AntiAirStrike, RapidResponseIntercept, RapidResponseDropPod, DebugRaidTransit, RaidDropPod, SettlementBuy, SettlementGift, SettlementBribe, RaidBribe, NpcFortify, DiplomacyNegotiate, AtTurret, NpcAtTurret }
    public enum RaidOrderOutcome { PlayerOutpostConquestMenu, AllyClaimsTarget, AllyAwardsToPlayer }

    public class WorldObject_Traveler : WorldObject
    {
        /// <summary>≈ lightly loaded walking human caravan (~2200 TPM). Lower = faster. Vanilla default fallback is 3300.</summary>
        public const int DefaultTicksPerMove = 2200;

        /// <summary>True for walking raids and T4 ballistic drop-pod raids.</summary>
        public static bool IsRaidMission(TravelerMission mission) =>
            mission == TravelerMission.Raid || mission == TravelerMission.RaidDropPod;

        public WD_PathFollower pather;
        public TravelerMission mission;
        public int ticksPerMove = DefaultTicksPerMove;
        public float travelerStrength;
        public float initialStrength;
        public float projectedArrivalStrength;
        /// <summary>Hostile spike traps this traveler has already triggered (anti-cheese cap).</summary>
        public int spikeTrapsTriggered;
        /// <summary>
        /// When true, <see cref="Destroy"/> skips the world-map destroyed caravan fade
        /// (abort / failed launch / peaceful despawn).
        /// </summary>
        public bool suppressDestroyedWorldFx;

        /// <summary>True after the first pollution-exit warning for this traveler (one yellow letter max).</summary>
        public bool pollutionDamageWarned;

        public Dictionary<WorldObject, float> contributionFactors;
        public int spawnTick;

        // Mortar / flak shell payload (MortarStrike / AntiAirStrike)
        public float mortarDamage;
        public bool mortarHit = true;
        public int mortarTargetTravelerId = -1;
        /// <summary>When true, this flak shell is the engagement resolver (hit/damage/letter); cosmetics are false.</summary>
        public bool antiAirIsResolver;
        /// <summary><see cref="AntiAirFireUtils.AntiAirTargetKind"/> stored as byte for scribing.</summary>
        public byte antiAirTargetKind;
        /// <summary>Flak lead flight: Slerp from AA to predicted meeting point (no tile path).</summary>
        public bool antiAirLeadFlight;
        public Vector3 antiAirLeadFrom;
        public Vector3 antiAirLeadTo;
        public float antiAirLeadTicksTotal;
        public float antiAirLeadTicksLeft;
        public bool rapidResponseStrengthRefunded;
        /// <summary>True when this intercept was launched by Feature C settlement ambush (sally, not player Rapid Response).</summary>
        public bool isSettlementAmbushSally;
        private int lastRapidResponseTargetTile = -1;
        private int lastRapidResponseInterceptTile = -1;

        /// <summary>Full-route travel ticks set once when the path starts (inspect UI; scribed). -1 = legacy / unset.</summary>
        private float cachedTotalTravelTicksAtLaunch = -1f;

        public float CachedLaunchTotalTravelTicks => cachedTotalTravelTicksAtLaunch;

        /// <summary>Called from <see cref="WD_PathFollower.StartPath"/> from the fresh <see cref="WorldPath"/> before any node is consumed.</summary>
        public void SetLaunchTotalTravelTicks(float ticks) => cachedTotalTravelTicksAtLaunch = ticks;

        /// <summary>UI + inspect: full-route days at path start (<see cref="cachedTotalTravelTicksAtLaunch"/>).</summary>
        public bool TryGetTotalExpectedTravelDays(out float days)
        {
            if (cachedTotalTravelTicksAtLaunch >= 0f)
            {
                days = cachedTotalTravelTicksAtLaunch / 60000f;
                return true;
            }
            days = 0f;
            return false;
        }

        private Vector3 tweenedPos = Vector3.zero;
        /// <summary>Cached tile centers for the current hop — avoid GetTileCenter twice per tick.</summary>
        private int tweenFromTileId = int.MinValue;
        private int tweenToTileId = int.MinValue;
        private Vector3 tweenFromCenter;
        private Vector3 tweenToCenter;

        /// <summary>Cached <see cref="ExpandingIcon"/> for <see cref="ResolveIconTexturePath"/> (avoids per-frame ContentFinder).</summary>
        private Texture2D? cachedExpandingIcon;
        private string? cachedExpandingIconPath;

        /// <summary>Throttle shell <see cref="ExpandingIconRotation"/> WorldToScreenPoint work to every N frames.</summary>
        private const int ShellFacingCacheFrames = 3;
        private float cachedShellFacingAngle;
        private int cachedShellFacingFrame = -999;
        private bool cachedShellFacingValid;

        public WorldObject originObject;
        public WorldObject targetObject;
        /// <summary>Player-ordered trader caravan (goodwill paid); skips arrival goodwill gain and uses <see cref="orderedTraderKind"/>.</summary>
        public bool playerOrderedTrader;
        public TraderKindDef orderedTraderKind;
        public RaidOrderOutcome raidOrderOutcome = RaidOrderOutcome.PlayerOutpostConquestMenu;
        public int alliedRaidOrderGoodwillPaid;
        public bool alliedRaidOrderGoodwillRefunded;
        public int playerColonyRaidCooldownReservationTick = -1;
        public int targetRaidDefenseCooldownReservationTick = -1;

        /// <summary>Cached <see cref="RaidLaunchTargetKind"/> classification of the current <see cref="targetObject"/>. Must be re-run and stored on every Feature A retarget / Feature B maraud accept so arrival/combat resolution never reads stale metadata from before a swap.</summary>
        public RaidLaunchTargetKind cachedTargetKind = RaidLaunchTargetKind.NPC;
        /// <summary>Feature A: number of target-of-opportunity retargets accepted so far (per-traveler cap).</summary>
        public int targetOfOpportunityRetargets;
        /// <summary>Feature B: number of post-victory marauding continuations accepted so far (per-traveler chain cap).</summary>
        public int maraudingChainCount;
        /// <summary>Combined Feature A + B counter, capped against <see cref="WorldDominationSettings.targetChangesMaxLifetime"/> in addition to each feature's own per-feature cap.</summary>
        public int totalTargetChanges;
        /// <summary>Feature A: tick of the last full (expensive) target-of-opportunity evaluation, throttling how often a raid weaving past a dense watcher cluster can re-evaluate.</summary>
        public int lastOpportunityEvalTick = -99999;
        /// <summary>True while this raid has diverted to assault an AT Turret and will resume its original target afterward.</summary>
        public bool isTurretDetour;
        /// <summary>True after this raid has used its one proximity AT Turret pull.</summary>
        public bool atTurretProximityDetourConsumed;
        /// <summary>How many post-shell-hit AT Turret detours this raid has already begun (cap <see cref="AtTurretRetaliationUtility.MaxHitDetours"/>).</summary>
        public int atTurretHitDetourCount;
        /// <summary>Original <see cref="targetObject"/> saved for the duration of an AT Turret detour.</summary>
        public WorldObject preTurretDetourTarget;
        /// <summary>Original path destination tile id saved with the AT Turret detour (-1 if unknown).</summary>
        public int preTurretDetourDestTileId = -1;
        /// <summary>Original <see cref="cachedTargetKind"/> saved with the AT Turret detour.</summary>
        public RaidLaunchTargetKind preTurretDetourCachedKind = RaidLaunchTargetKind.NPC;
        public List<WorldObject> raidAttackerList = new List<WorldObject>();
        public List<string> raidAttackerDetails = new List<string>();
        /// <summary>Structured attacker breakdown for resolution Details (icon rows). Parallel to <see cref="raidAttackerDetails"/>.</summary>
        public List<RaidForceLogRow> raidAttackerForceRows = new List<RaidForceLogRow>();
        /// <summary>Structured defender breakdown snapshot at launch for resolution Details.</summary>
        public List<RaidForceLogRow> raidDefenderForceRows = new List<RaidForceLogRow>();

        /// <summary>Npc Fortify payload: place trap vs road block on arrival.</summary>
        public bool fortifyIsTrap;
        public SpikeTrapKind fortifySpikeTrapKind = SpikeTrapKind.Spike;
        public RoadBlockKind fortifyRoadBlockKind = RoadBlockKind.Light;

        // --- SURGICAL: Road Path Caching for Travelers ---
        public List<int> cachedPathTiles = new List<int>();

        /// <summary>Paired lists avoid RimWorld dict scribe errors when keys/values counts mismatch (corrupt saves).</summary>
        private List<WorldObject> scribeContribKeys;
        private List<float> scribeContribVals;

        /// <summary>Single world-map icon material per traveler (faction-tinted), same pattern as WorldObject_WD_Outpost.</summary>
        private Material travelerExpandingMaterial;


        private int cachedInspectTick = -1;
        private string cachedInspectString;

        // --- SURGICAL: Target Identification ---
        public bool IsTargetingPlayer => TravelerEndpointUtility.IsLiveEndpoint(targetObject) && targetObject.Faction != null && targetObject.Faction.IsPlayer;

        /// <summary>Hostile raids only—not outpost supply caravans.</summary>
        public bool ShowTargetingPlayerWarning => IsRaidMission(mission) && IsTargetingPlayer;

        /// <summary>Dashboard warning banner: NPC raids, traders, and mortar shells heading to a player endpoint—not player deliveries or upgrades.</summary>
        public bool IsHostileNpcTravelerTargetingPlayer =>
            IsTargetingPlayer
            && Faction != null
            && !Faction.IsPlayer
            && (IsRaidMission(mission)
                || mission == TravelerMission.Trader
                || mission == TravelerMission.MortarStrike
                || mission == TravelerMission.AntiAirStrike
                || mission == TravelerMission.DebugRaidTransit);

        /// <summary>Dashboard sort: raid→colony, raid→outpost, delivery→colony, then everything else.</summary>
        public static int DashboardListTier(WorldObject_Traveler t)
        {
            if (t == null) return 99;
            if (IsRaidTargetingPlayerColony(t)) return 0;
            if (IsRaidTargetingPlayerOutpost(t)) return 1;
            if (IsOutpostDeliveryToPlayerColony(t)) return 2;
            return 3;
        }

        private static bool IsPlayerColonyTarget(WorldObject wo)
        {
            if (!TravelerEndpointUtility.IsLiveEndpoint(wo) || wo.Faction?.IsPlayer != true) return false;
            return wo is Settlement && wo is MapParent mp && mp.HasMap;
        }

        private static bool IsPlayerOutpostTarget(WorldObject wo)
        {
            if (!TravelerEndpointUtility.IsLiveEndpoint(wo)) return false;
            return wo is WorldObject_WD_Outpost o && o.Faction?.IsPlayer == true;
        }

        private static bool IsRaidTargetingPlayerColony(WorldObject_Traveler t) =>
            IsRaidMission(t.mission) && IsPlayerColonyTarget(t.targetObject);

        private static bool IsRaidTargetingPlayerOutpost(WorldObject_Traveler t) =>
            IsRaidMission(t.mission) && IsPlayerOutpostTarget(t.targetObject);

        private static bool IsOutpostDeliveryToPlayerColony(WorldObject_Traveler t) =>
            t.mission == TravelerMission.OutpostDelivery && IsPlayerColonyTarget(t.targetObject);

        private static readonly List<WorldObject_Traveler> sortedTravelersBuffer = new List<WorldObject_Traveler>();

        public static List<WorldObject_Traveler> SortTravelersForUi(IEnumerable<WorldObject_Traveler> all, int maxCount)
        {
            sortedTravelersBuffer.Clear();
            sortedTravelersBuffer.AddRange(all);
            sortedTravelersBuffer.Sort((a, b) =>
            {
                int cmp = DashboardListTier(a).CompareTo(DashboardListTier(b));
                if (cmp != 0) return cmp;
                return a.spawnTick.CompareTo(b.spawnTick);
            });
            if (sortedTravelersBuffer.Count > maxCount)
                sortedTravelersBuffer.RemoveRange(maxCount, sortedTravelersBuffer.Count - maxCount);
            return sortedTravelersBuffer;
        }

        private static readonly Dictionary<TravelerMission, string> missionLabelCache = new Dictionary<TravelerMission, string>();
        private static string targetingYouCache;

        /// <summary>Short type label for world objects, dashboard, and travelers list tooltips.</summary>
        public static string GetMissionTypeLabel(TravelerMission mission)
        {
            if (!missionLabelCache.TryGetValue(mission, out string label))
            {
                label = mission switch
                {
                    TravelerMission.Raid => "TSA_WD_RaiderCaravan".Translate(),
                    TravelerMission.RaidDropPod => "TSA_WD_RaiderDropPods".Translate(),
                    TravelerMission.Expansion => "TSA_WD_ExpansionCaravan".Translate(),
                    TravelerMission.RoadBuilding => "TSA_WD_RoadBuilderCaravan".Translate(),
                    TravelerMission.RoadBlock => "TSA_WD_Traveler_Outpost_RoadBlock".Translate(),
                    TravelerMission.SpikeTrap => "TSA_WD_Traveler_Outpost_SpikeTrap".Translate(),
                    TravelerMission.Decontamination => "TSA_WD_Traveler_Outpost_Decontamination".Translate(),
                    TravelerMission.OutpostDelivery => "TSA_WD_Traveler_Outpost_Delivery".Translate(),
                    TravelerMission.Trader => "TSA_WD_TraderCaravan".Translate(),
                    TravelerMission.OutpostUpgrade => "TSA_WD_Traveler_Outpost_Upgrade".Translate(),
                    TravelerMission.MortarStrike => "TSA_WD_Traveler_MortarStrike".Translate(),
                    TravelerMission.AntiAirStrike => "TSA_WD_Traveler_FlakShell".Translate(),
                    TravelerMission.RapidResponseIntercept => "TSA_WD_Traveler_RapidResponseIntercept".Translate(),
                    TravelerMission.RapidResponseDropPod => "TSA_WD_Traveler_RapidResponseDropPod".Translate(),
                    TravelerMission.DebugRaidTransit => "TSA_WD_DebugRaidTravelerLabel".Translate(),
                    TravelerMission.SettlementBuy => "TSA_WD_SettlementBuyerCaravan".Translate(),
                    TravelerMission.SettlementGift => "TSA_WD_SettlementGifterCaravan".Translate(),
                    TravelerMission.SettlementBribe => "TSA_WD_BribeSettlementCaravan".Translate(),
                    TravelerMission.RaidBribe => "TSA_WD_BribeRaidCaravan".Translate(),
                    TravelerMission.NpcFortify => "TSA_WD_Traveler_NpcFortify".Translate(),
                    TravelerMission.NpcAtTurret => "TSA_WD_Traveler_NpcAtTurret".Translate(),
                    TravelerMission.AtTurret => "TSA_WD_Traveler_AtTurret".Translate(),
                    _ => "TSA_WD_TravelerCaravan".Translate()
                };
                missionLabelCache[mission] = label;
            }
            return label;
        }

        public override string Label
        {
            get
            {
                string baseLabel = mission == TravelerMission.MortarStrike && originObject is WorldObject_AT_Turret
                    ? (string)"TSA_WD_Traveler_AT_Shell".Translate()
                    : GetMissionTypeLabel(mission);
                if (!ShowTargetingPlayerWarning) return baseLabel;
                targetingYouCache ??= "TSA_WD_TargetingYou".Translate();
                return $"{baseLabel} ({targetingYouCache})";
            }
        }

        public WorldObject_Traveler()
        {
            pather = new WD_PathFollower(this);
            contributionFactors = new Dictionary<WorldObject, float>();
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos())
                yield return g;
            foreach (Gizmo g in WD_Outpost_RapidResponse.GetTravelerGizmos(this))
                yield return g;
            foreach (Gizmo g in Action_Settlement_Bribe.GetRaidGizmos(this))
                yield return g;
        }

        /// <summary>Texture path from def XML: <c>expandingIconTexture</c> if set, else <c>texture</c> (same as vanilla <see cref="WorldObjectDef"/>).</summary>
        public static string? GetIconTexturePathFromDef(WorldObjectDef? d)
        {
            if (d == null) return null;
            if (!d.expandingIconTexture.NullOrEmpty()) return d.expandingIconTexture;
            if (!d.texture.NullOrEmpty()) return d.texture;
            return null;
        }

        /// <summary>World-map / UI icon path for this traveler instance (defs may share a class; subclasses can override for mode-specific art).</summary>
        public virtual string? ResolveIconTexturePath() => GetIconTexturePathFromDef(def);

        /// <summary>
        /// Vanilla prefers <see cref="WorldObjectDef.ExpandingIconTexture"/> and never consults <see cref="Material"/>.
        /// Warehouse drop-pod deliveries share the goods traveler def, so we must resolve per-instance here.
        /// </summary>
        public override Texture2D ExpandingIcon
        {
            get
            {
                string path = ResolveIconTexturePath();
                if (!path.NullOrEmpty())
                {
                    if (cachedExpandingIcon != null && cachedExpandingIconPath == path)
                        return cachedExpandingIcon;
                    Texture2D tex = ContentFinder<Texture2D>.Get(path, false);
                    if (tex != null)
                    {
                        cachedExpandingIcon = tex;
                        cachedExpandingIconPath = path;
                        return tex;
                    }
                }
                return base.ExpandingIcon;
            }
        }

        /// <summary>
        /// Far/zoomed-out icon only. Mortar/flak art faces east; <c>AT_Shell</c> faces north (see offset in
        /// <see cref="TryGetShellFacingScreenAngle"/>). Rotate so the nose matches screen travel direction.
        /// </summary>
        public override float ExpandingIconRotation
        {
            get
            {
                if (!IsShellMission(mission))
                    return base.ExpandingIconRotation;
                return TryGetShellFacingScreenAngle(out float angle) ? angle : 0f;
            }
        }

        /// <summary>
        /// Draw airborne travelers above settlements (10), sites, and ground caravans (100).
        /// Vanilla <see cref="TravellingTransporters"/> use 60; we go above caravan so flight reads as "over" the tile.
        /// </summary>
        public override float ExpandingIconPriority
        {
            get
            {
                if (WD_PathFollower.IsBallisticWorldFlight(this))
                    return 110f;
                return base.ExpandingIconPriority;
            }
        }

        /// <summary>AT Shell art points north; east-relative facing math needs +90° (same as <c>AT_Gun</c>).</summary>
        private const float NorthFacingShellTextureOffsetDeg = 90f;

        /// <summary>True for shells fired by <see cref="WorldObject_AT_Turret"/> (not mortar shells). Anti-air ignores these.</summary>
        public bool IsAtTurretShell()
        {
            if (mission != TravelerMission.MortarStrike) return false;
            if (originObject is WorldObject_AT_Turret) return true;
            return def?.defName == "TSA_WD_Traveler_AT_Shell";
        }

        private bool UsesNorthFacingShellArt() => IsAtTurretShell();

        /// <summary>
        /// Screen-space facing for mortar/flak icons. Default texture forward = +X (east). Uses WorldToScreenPoint Y-up so
        /// <see cref="Widgets.DrawTextureRotated"/> (CCW positive) points the nose along the shot.
        /// Cached for a few frames — angle is stable within a hop.
        /// </summary>
        private bool TryGetShellFacingScreenAngle(out float angleDeg)
        {
            int frame = Time.frameCount;
            if (cachedShellFacingValid && frame - cachedShellFacingFrame < ShellFacingCacheFrames)
            {
                angleDeg = cachedShellFacingAngle;
                return true;
            }

            angleDeg = 0f;
            Vector3 from;
            Vector3 to;
            if (antiAirLeadFlight && mission == TravelerMission.AntiAirStrike)
            {
                from = antiAirLeadFrom;
                to = antiAirLeadTo;
            }
            else if (pather != null && pather.moving
                && (tweenFromCenter - tweenToCenter).sqrMagnitude > 1e-8f)
            {
                from = tweenFromCenter;
                to = tweenToCenter;
            }
            else if (TravelerEndpointUtility.IsLiveEndpoint(originObject)
                && TravelerEndpointUtility.IsLiveEndpoint(targetObject))
            {
                from = originObject.DrawPos;
                to = targetObject.DrawPos;
            }
            else
                return false;

            Camera cam = Find.WorldCamera;
            if (cam == null) return false;

            Vector3 s0 = cam.WorldToScreenPoint(from);
            Vector3 s1 = cam.WorldToScreenPoint(to);
            if (s0.z <= 0f || s1.z <= 0f) return false;

            Vector2 d = new Vector2(s1.x - s0.x, s1.y - s0.y);
            if (d.sqrMagnitude < 0.25f) return false;

            // WorldToScreenPoint is Y-up; expanding icons draw in Y-down UI space — flip Y so up/down match.
            angleDeg = Mathf.Atan2(-d.y, d.x) * Mathf.Rad2Deg;
            if (UsesNorthFacingShellArt())
                angleDeg += NorthFacingShellTextureOffsetDeg;
            cachedShellFacingAngle = angleDeg;
            cachedShellFacingFrame = frame;
            cachedShellFacingValid = true;
            return true;
        }

        public override Material Material
        {
            get
            {
                if (travelerExpandingMaterial == null && def != null)
                {
                    string path = ResolveIconTexturePath();
                    if (!path.NullOrEmpty())
                    {
                        Color c = Faction?.Color ?? Color.white;
                        travelerExpandingMaterial = MaterialPool.MatFrom(path, ShaderDatabase.WorldOverlayTransparentLit, c, WorldMaterials.WorldObjectRenderQueue);
                    }
                }
                return travelerExpandingMaterial ?? base.Material;
            }
        }

        /// <summary>Clears cached zoomed-in material, expanding icon, and shell facing so the next draw resolves again.</summary>
        public void InvalidateTravelerMaterialCache()
        {
            travelerExpandingMaterial = null;
            cachedExpandingIcon = null;
            cachedExpandingIconPath = null;
            cachedShellFacingValid = false;
        }

        /// <summary>Live count of spawned travelers; used by CaravansCount patch to prevent false "path leak" warnings.</summary>
        public static int ActiveCount { get; private set; }

        private static readonly List<WorldObject_Traveler> liveTravelers = new List<WorldObject_Traveler>();

        /// <summary>Read-only view of spawned travelers for per-frame GUI consumers (underlays). Do not mutate.</summary>
        public static IReadOnlyList<WorldObject_Traveler> LiveTravelers => liveTravelers;

        /// <summary>
        /// Rebuild the static live-traveler registry from the current world. Static state is not reset
        /// between save loads in one session, so orphaned travelers from a prior game can linger and
        /// inflate <see cref="ActiveCount"/>. Called from <see cref="WorldComponent_SpreadManager.FinalizeInit"/>,
        /// which runs after world objects are spawned on load, so it is order-independent and authoritative.
        /// </summary>
        public static void RebuildLiveRegistry()
        {
            liveTravelers.Clear();
            ActiveCount = 0;
            WorldObjectsHolder worldObjects = Find.WorldObjects;
            if (worldObjects == null) return;
            List<WorldObject> all = worldObjects.AllWorldObjects;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is WorldObject_Traveler t && !t.Destroyed)
                {
                    liveTravelers.Add(t);
                    ActiveCount++;
                }
            }
        }

        public override void Destroy()
        {
            if (!Destroyed)
            {
                MortarWorldFx.TryNotifyGroundCombatCaravanDestroyed(this);
                if (playerColonyRaidCooldownReservationTick > 0
                    && targetObject is Settlement playerSettlement
                    && playerSettlement.Faction?.IsPlayer == true)
                    Raid_OnPlayerColony.ReleaseRaidDefenseCooldownReservation(playerSettlement, playerColonyRaidCooldownReservationTick);
                if (targetRaidDefenseCooldownReservationTick > 0)
                    Raid_DefenseCooldownReservations.ReleaseRaidDefenseCooldownReservation(targetObject, targetRaidDefenseCooldownReservationTick);
                if (this is WorldObject_Traveler_Outpost_Upgrade upgradeTraveler
                    && upgradeTraveler.targetObject is WorldObject_WD_Outpost outpost
                    && !outpost.Destroyed)
                    outpost.ClearPendingUpgradeIfMatches(upgradeTraveler.upgradeDefName, upgradeTraveler.upgradeLevel);
                // Rapid-response strength is deducted at dispatch; refunds only via TravelerEndpointUtility (abort / surviving clash strength).
                ActiveCount--;
                liveTravelers.Remove(this);
                pather?.StopDead();
                if (mission == TravelerMission.RoadBuilding && originObject != null && !originObject.Destroyed)
                    originObject.GetComponent<CompViralSpread>()?.NotifyRoadBuilderReturned();
                if (mission == TravelerMission.RoadBlock && originObject != null && !originObject.Destroyed)
                    originObject.GetComponent<CompViralSpread>()?.NotifyRoadBlockCrewReturned();
                if (mission == TravelerMission.SpikeTrap && originObject != null && !originObject.Destroyed)
                    originObject.GetComponent<CompViralSpread>()?.NotifySpikeTrapCrewReturned();
                if (mission == TravelerMission.AtTurret && originObject != null && !originObject.Destroyed)
                    originObject.GetComponent<CompViralSpread>()?.NotifyAtTurretCrewReturned();
                if (mission == TravelerMission.Decontamination && originObject != null && !originObject.Destroyed)
                    originObject.GetComponent<CompViralSpread>()?.NotifyDecontaminationCrewReturned();
                WorldComponent_InterceptionScheduler.Current?.UnregisterTraveler(this);
                if (isSettlementAmbushSally)
                    WorldComponent_SettlementWatchIndex.Get()?.NotifyAmbushSallyDestroyed();
            }
            base.Destroy();
        }

        public override void SpawnSetup()
        {
            base.SpawnSetup();
            ActiveCount++;
            liveTravelers.Add(this);
            tweenedPos = Find.WorldGrid.GetTileCenter(Tile);
            tweenFromCenter = tweenToCenter = tweenedPos;
            tweenFromTileId = tweenToTileId = Tile.tileId;
            if (spawnTick == 0) spawnTick = Find.TickManager.TicksGame;
            WorldComponent_InterceptionScheduler.Current?.RegisterTraveler(this);

            // --- SURGICAL: Capture Initial Strength if not set yet ---
            if (initialStrength <= 0) initialStrength = travelerStrength;

            // projectedArrivalStrength is locked in exactly once by WD_PathFollower.StartPath,
            // using initialStrength × efficiency derived from the real launch-time path ticks.
            // Do NOT recompute it here: SpawnSetup runs before StartPath on fresh spawns
            // (destTile is still -1), and runs again on load where it would overwrite the
            // scribed value with a drifting "current tile" estimate.
        }

        /// <summary>World ticks remaining to <see cref="WD_PathFollower.destTile"/>: partial current hop + edges along the unconsumed <see cref="WD_PathFollower.curPath"/> (matches path follower).</summary>
        public float GetRemainingTravelTicks()
        {
            if (pather == null || !pather.moving) return 0f;
            return pather.GetRemainingTravelTicks();
        }

        public override string GetInspectString()
        {
            int tick = Find.TickManager.TicksGame;
            if (tick == cachedInspectTick && cachedInspectString != null)
                return cachedInspectString;
            cachedInspectTick = tick;
            cachedInspectString = BuildInspectString();
            return cachedInspectString;
        }

        private string BuildInspectString()
        {
            StringBuilder sb = new StringBuilder();
            // Base implementation usually ends with \n; AppendLine would add another and create a blank inspect row.
            string baseInspect = base.GetInspectString();
            if (!baseInspect.NullOrEmpty())
                sb.AppendLine(baseInspect.TrimEnd('\r', '\n'));

            string relationLabel = WorldActions_Utils.GetRelationshipLabel(Faction).CapitalizeFirst();
            sb.AppendLine($"{"TSA_WD_Traveller_DiploStatus".Translate()}: {relationLabel}");

            // --- STRENGTH BREAKDOWN ---
            float currentEfficiency = (initialStrength > 0) ? (travelerStrength / initialStrength) : 1f;
            sb.AppendLine($"{"TSA_WD_StrengthAtDeparture".Translate()}: {travelerStrength:F0} / {initialStrength:F0} ({currentEfficiency.ToStringPercent()})");

            float ticksSinceDeparture = Find.TickManager.TicksGame - spawnTick;
            float daysSinceDeparture = ticksSinceDeparture / 60000f;

            string originLabel = TravelerEndpointUtility.IsLiveEndpoint(originObject)
                ? originObject.LabelCap
                : "TSA_WD_Traveller_Unknown".Translate();

            string destLabel = TravelerEndpointUtility.IsLiveEndpoint(targetObject)
                ? targetObject.LabelCap
                : (pather != null ? pather.destTile.tileId.ToString() : "TSA_WD_Traveller_Unknown".Translate());
            sb.AppendLine($"{"TSA_WD_Traveller_Origin".Translate()}: {originLabel}");

            sb.AppendLine($"{"TSA_WD_Traveller_Destination".Translate()}: {destLabel}");

            sb.AppendLine($"{"TSA_WD_TimeSinceDeparture".Translate()}: {daysSinceDeparture:F1} {"TSA_WD_Days".Translate()}");
            if (cachedTotalTravelTicksAtLaunch >= 0f)
                sb.AppendLine($"{"TSA_WD_Traveller_TotalExpectedTravelTime".Translate()}: {(cachedTotalTravelTicksAtLaunch / 60000f):F1} {"TSA_WD_Days".Translate()}");
            else
                sb.AppendLine($"{"TSA_WD_Traveller_TotalExpectedTravelTime".Translate()}: {"TSA_WD_Traveller_Unknown".Translate()}");

            return sb.ToString().TrimEnd('\r', '\n');
        }

        public override void DrawExtraSelectionOverlays()
        {
            base.DrawExtraSelectionOverlays();
            // Path must live here — not in Draw(). When always-show-icon is on,
            // Patch_WdWorldObjectNoExpandingIcon skips both world-object draw layers,
            // so Draw() never runs for travelers (expanding OnGUI icon still does).
            if (WD_WorldMapZoomUtil.IsZoomedTooFarOut(WD_WorldMapZoomUtil.TravelerPathHideAltitudePercent))
                return;
            // Always draw this caravan's route to its current destination (white), like other travelers.
            // The orange full-project corridor is drawn from the outpost/colony selection overlay only.
            pather?.DrawPathHelper();
        }

        /// <summary>Outpost/colony construction and fortify crews that travel to a worksite tile (no <see cref="targetObject"/>).</summary>
        public static bool IsConstructionMission(TravelerMission m) =>
            m == TravelerMission.RoadBuilding
            || m == TravelerMission.RoadBlock
            || m == TravelerMission.SpikeTrap
            || m == TravelerMission.AtTurret
            || m == TravelerMission.Decontamination
            || m == TravelerMission.NpcFortify
            || m == TravelerMission.NpcAtTurret;

        /// <summary>
        /// When an outpost/colony is selected, draw white routes for its active construction crews.
        /// Orange project corridors alone do not show which caravan is going where.
        /// </summary>
        public static void DrawConstructionTravelerPathsForOrigin(WorldObject origin)
        {
            if (origin == null || origin.Destroyed) return;
            if (!Find.WorldSelector.IsSelected(origin)) return;
            if (WD_WorldMapZoomUtil.IsZoomedTooFarOut(WD_WorldMapZoomUtil.TravelerPathHideAltitudePercent))
                return;

            for (int i = 0; i < liveTravelers.Count; i++)
            {
                WorldObject_Traveler t = liveTravelers[i];
                if (t == null || t.Destroyed) continue;
                if (t.originObject != origin) continue;
                if (!IsConstructionMission(t.mission)) continue;
                t.pather?.DrawPathHelper();
            }
        }

        public void BeginAntiAirLeadFlight(Vector3 from, Vector3 to, float ticks)
        {
            antiAirLeadFlight = true;
            antiAirLeadFrom = from;
            antiAirLeadTo = to;
            antiAirLeadTicksTotal = Mathf.Max(1f, ticks);
            antiAirLeadTicksLeft = antiAirLeadTicksTotal;
            tweenedPos = from;
            pather?.StopDead();
        }

        /// <summary>True if the shell’s strike target no longer exists (destroyed or despawned). Manual and auto shells both use this to despawn mid-flight.</summary>
        private bool MortarStrikePrimaryTargetGone()
            => targetObject == null || targetObject.Destroyed;

        /// <summary>
        /// AT shells that never reach <see cref="WorldActions_Traveler.ExecuteMortarStrike"/> (target died mid-flight,
        /// leaked stationary shell) still need an action-log miss line.
        /// </summary>
        private void TryLogUnresolvedAtShellMiss()
        {
            if (!IsAtTurretShell()) return;
            if (!(originObject is WorldObject_AT_Turret atGun) || atGun.Destroyed) return;
            AtTurretNotifyUtility.NotifyShellMiss(atGun, targetObject);
        }

        internal static bool IsShellMission(TravelerMission m) =>
            m == TravelerMission.MortarStrike || m == TravelerMission.AntiAirStrike;

        /// <summary>
        /// Same cadence as vanilla <see cref="Caravan"/>: path + tween live in <see cref="TickInterval"/>,
        /// so updates batch when the world map is not selected (<see cref="WorldObject.UpdateRateTicks"/>).
        /// </summary>
        protected override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            if (Destroyed || delta <= 0)
                return;

            if (antiAirLeadFlight && mission == TravelerMission.AntiAirStrike)
            {
                TickAntiAirLeadFlight(delta);
                return;
            }

            pather?.PatherTick(delta);
            if (Destroyed)
                return;

            TryRetryDeferredOutpostRaidArrival(delta);

            if ((mission == TravelerMission.RapidResponseIntercept || mission == TravelerMission.RaidBribe)
                && this.IsHashIntervalTick(180, delta))
                RefreshRapidResponseInterceptPath(false);

            // Ballistic AA targets keep Tile at launch; NPC T4 scan is sparse (3× interval, round-robin).
            // Re-wake periodically so flak engages when the hop progress enters AA range mid-flight.
            if (pather != null && pather.moving
                && AntiAirFireUtils.IsAirborneAaTarget(this)
                && this.IsHashIntervalTick(60, delta)
                && spawnTick != Find.TickManager.TicksGame)
            {
                AntiAirFireUtils.WakeAllForDropPod(this);
            }

            if (IsShellMission(mission) && pather != null && pather.moving
                && this.IsHashIntervalTick(60, delta) && MortarStrikePrimaryTargetGone())
            {
                TryLogUnresolvedAtShellMiss();
                Destroy();
                return;
            }

            // Safety net: a mortar/flak shell should always be either in flight or already destroyed on arrival.
            // If one ends up stationary (moving == false) on a later tick, it has leaked; clean it up.
            // O(1), mission-gated; excludes the spawn tick (StartPath runs synchronously that frame).
            if (IsShellMission(mission)
                && !antiAirLeadFlight
                && (pather == null || !pather.moving)
                && spawnTick != Find.TickManager.TicksGame)
            {
                if (Prefs.DevMode)
                    Log.Warning($"[TSA WD] Cleaning up stationary shell \"{Label}\" (leaked stuck on world map).");
                TryLogUnresolvedAtShellMiss();
                Destroy();
                return;
            }

            // Mortar shells and drop-pod warehouse / RR / raid drop pods ignore attrition (paid at launch or in-flight for seconds).
            bool skipAttrition = IsShellMission(mission)
                || mission == TravelerMission.RapidResponseDropPod
                || mission == TravelerMission.RaidDropPod
                || WD_PathFollower.IsBallisticWorldFlight(this);
            if (!skipAttrition && this.IsHashIntervalTick(180, delta))
            {
                var seth = WorldDominationMod.settings;
                float intervalRate = (seth.strengthLossPerHour / 2500f) * 180f;
                float attritionMult = 1f;
                var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
                if (manager != null && Faction == manager.expansionistZealFaction && Find.TickManager.TicksGame < manager.expansionistZealExpiryTick)
                    attritionMult = seth.zealAttritionMult;
                intervalRate *= attritionMult;
                travelerStrength = Mathf.Max(0f, travelerStrength * (1f - intervalRate));
                float strengthFloor = initialStrength * (1f - Mathf.Clamp01(seth.maxTravelPercentageStrengthLoss));
                travelerStrength = Mathf.Max(travelerStrength, strengthFloor);

                bool shouldExpire = mission != TravelerMission.OutpostDelivery && travelerStrength <= 0.01f;
                if (shouldExpire)
                {
                    TravelerEndpointUtility.AbortTraveler(this, "TSA_WD_Log_TravelerExpired".Translate(Label), manager);
                    return;
                }
            }

            Vector3 targetRoot;
            if (pather != null && pather.moving && pather.nextTile.Valid)
            {
                // Off-camera LOD: advance pather above; snap DrawPos to current tile only when the tile changes.
                // Avoids per-tick hop blend / Slerp for travelers the player cannot see. Ballistic single-hop
                // flights (mortar/AT shells, drop pods) are excluded: their Tile stays pinned at the launch tile
                // for the whole flight, so this shortcut would freeze DrawPos at the origin the entire time and
                // misplace impact/flak FX. Ballistic flights are short, so always Slerping them is cheap.
                if (!WorldObjectSelectionUtility.VisibleToCameraNow(this) && !WD_PathFollower.IsBallisticWorldFlight(this))
                {
                    int tileId = Tile.tileId;
                    if (tweenFromTileId != tileId || tweenToTileId != tileId)
                    {
                        tweenedPos = Find.WorldGrid.GetTileCenter(Tile);
                        tweenFromCenter = tweenToCenter = tweenedPos;
                        tweenFromTileId = tweenToTileId = tileId;
                        cachedShellFacingValid = false;
                    }
                    return;
                }

                EnsureTweenHopCenters(Tile.tileId, pather.nextTile.tileId);
                // Direct hop lerp (vanilla CaravanTweenerUtility style). Ballistic must Slerp so DrawPos
                // stays on the planet surface — a linear chord goes through the globe.
                float pct = 1f - (pather.nextTileCostLeft / Mathf.Max(0.001f, pather.nextTileCostTotal));
                pct = Mathf.Clamp01(pct);
                if (WD_PathFollower.IsBallisticWorldFlight(this))
                    targetRoot = Vector3.Slerp(tweenFromCenter, tweenToCenter, pct);
                else
                    targetRoot = tweenToCenter * pct + tweenFromCenter * (1f - pct);
            }
            else
            {
                int tileId = Tile.tileId;
                if (tweenFromTileId != tileId || tweenToTileId != tileId)
                {
                    tweenFromCenter = tweenToCenter = Find.WorldGrid.GetTileCenter(Tile);
                    tweenFromTileId = tweenToTileId = tileId;
                    cachedShellFacingValid = false;
                }
                targetRoot = tweenFromCenter;
            }
            // Direct assignment — no spring follow (cheaper every tick; matches caravan hop blend).
            tweenedPos = targetRoot;
        }

        private void TickAntiAirLeadFlight(int delta)
        {
            if (this.IsHashIntervalTick(60, delta) && MortarStrikePrimaryTargetGone())
            {
                antiAirLeadFlight = false;
                Destroy();
                return;
            }

            antiAirLeadTicksLeft -= delta;
            float total = Mathf.Max(0.001f, antiAirLeadTicksTotal);
            float pct = Mathf.Clamp01(1f - antiAirLeadTicksLeft / total);
            tweenedPos = Vector3.Slerp(antiAirLeadFrom, antiAirLeadTo, pct);

            if (antiAirLeadTicksLeft > 0f)
                return;

            antiAirLeadFlight = false;
            WorldActions_Traveler.FinishAntiAirShell(this);
        }

        private void EnsureTweenHopCenters(int fromTileId, int toTileId)
        {
            if (tweenFromTileId == fromTileId && tweenToTileId == toTileId)
                return;
            WorldGrid grid = Find.WorldGrid;
            tweenFromCenter = grid.GetTileCenter(fromTileId);
            tweenToCenter = grid.GetTileCenter(toTileId);
            tweenFromTileId = fromTileId;
            tweenToTileId = toTileId;
            cachedShellFacingValid = false;
        }

        /// <summary>
        /// After a ballistic <see cref="WD_PathFollower.StartPath"/>, nudge the hop start along the great-circle
        /// toward the aim tile. <paramref name="fractionOfTileRadius"/> 1 = roughly the tile edge (halfway to the
        /// neighbor center); 0.8 keeps the muzzle flash inside the origin tile but clearly off-center.
        /// </summary>
        public void ApplyBallisticSpawnOffsetTowardAim(float fractionOfTileRadius = 0.8f)
        {
            if (pather == null || !pather.moving || !pather.nextTile.Valid) return;
            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return;
            int fromId = Tile.tileId;
            int toId = pather.nextTile.tileId;
            if (fromId < 0 || toId < 0 || fromId == toId) return;

            EnsureTweenHopCenters(fromId, toId);
            float approxDist = Mathf.Max(1f, grid.ApproxDistanceInTiles(fromId, toId));
            // Half a tile toward dest ≈ 0.5 / hopDistance along the full hop slerp.
            float t = Mathf.Clamp01((0.5f * Mathf.Clamp01(fractionOfTileRadius)) / approxDist);
            tweenFromCenter = Vector3.Slerp(tweenFromCenter, tweenToCenter, t);
            tweenedPos = tweenFromCenter;
            cachedShellFacingValid = false;
        }

        private void TryRetryDeferredOutpostRaidArrival(int delta)
        {
            if (mission != TravelerMission.Raid || Destroyed)
                return;
            if (pather == null || pather.moving)
                return;
            if (!(targetObject is WorldObject_WD_Outpost outpost) || outpost.Destroyed)
                return;
            if (outpost.Tile != Tile)
                return;
            if (WdPostLoadGuard.ShouldDeferTravelerArrival())
                return;
            if (outpost.BlocksAutoRaidResolution())
                return;
            if (!this.IsHashIntervalTick(60, delta))
                return;

            var manager = Find.World.GetComponent<WorldComponent_SpreadManager>();
            targetRaidDefenseCooldownReservationTick = -1;
            playerColonyRaidCooldownReservationTick = -1;
            Raid_Simulated.ExecuteTravelerRaid(this, manager);
            if (!Destroyed)
                Destroy();
        }

        /// <summary>
        /// Lead-intercept pathing for Rapid Response and raid bribes: cut off ahead on the target's route when possible,
        /// wait on that tile, or close directly if already between the target and its destination (never flee toward dest).
        /// </summary>
        public void RefreshRapidResponseInterceptPath(bool force)
        {
            if (pather == null) return;
            if (mission != TravelerMission.RapidResponseIntercept && mission != TravelerMission.RaidBribe) return;

            // Feature C (real-caravan ambush/RR): no lead-intercept math (real Caravan_PathFollower, not WD_PathFollower) —
            // simply keep re-aiming at the caravan's current tile. WD_SameTileTravelerClash.AfterTravelerLanded_TravelerVsCaravan
            // resolves the actual encounter for free once this traveler lands on the caravan's tile.
            if (targetObject is Caravan caravanTarget)
            {
                if (caravanTarget.Destroyed || !caravanTarget.Spawned)
                {
                    AbortRapidResponseIfTargetLost();
                    return;
                }
                if (caravanTarget.Tile != Tile && TryAbortSettlementAmbushIfTargetEscaped(caravanTarget.Tile))
                    return;
                RefreshRapidResponseInterceptPathForCaravan(caravanTarget, force);
                return;
            }

            if (!(targetObject is WorldObject_Traveler target) || target.Destroyed)
            {
                AbortRapidResponseIfTargetLost();
                return;
            }
            int targetTile = target.Tile;
            if (targetTile < 0)
            {
                AbortRapidResponseIfTargetLost();
                return;
            }

            if (targetTile != Tile && TryAbortSettlementAmbushIfTargetEscaped(targetTile))
                return;

            // Same tile: RR clashes / bribe delivers.
            if (targetTile == Tile)
            {
                if (pather.moving)
                    pather.StopDead();
                CompleteMovingTargetMeetupIfPossible();
                return;
            }

            int interceptTile = ResolveRapidResponseInterceptTile(target);
            if (interceptTile < 0) interceptTile = targetTile;
            interceptTile = ClampRapidResponseAimNotAwayFromHostile(interceptTile, target);

            if (!force && interceptTile == lastRapidResponseInterceptTile && targetTile == lastRapidResponseTargetTile) return;
            lastRapidResponseTargetTile = targetTile;
            lastRapidResponseInterceptTile = interceptTile;

            // Ahead on the hostile's route: wait here; do not path further toward their destination.
            if (interceptTile == Tile)
            {
                if (pather.moving)
                    pather.StopDead();
                return;
            }

            if (pather.destTile.Valid && pather.destTile.tileId == interceptTile) return;
            PlanetTile interceptPlanetTile = PlanetSurfaceWorldActions.PlanetTileForWdTravel(interceptTile, this);
            if (!pather.RetargetDestinationAfterCurrentHop(interceptPlanetTile))
                pather.StartPath(interceptPlanetTile, skipLaunchTravelCache: true);
        }

        /// <summary>
        /// Settlement ambush and Rapid Response both use this mission. If the quarry is destroyed or despawns
        /// mid-chase, refund strength to the origin (same as arrival abort / cancel gizmo).
        /// </summary>
        private void AbortRapidResponseIfTargetLost()
        {
            if (mission != TravelerMission.RapidResponseIntercept) return;
            if (Destroyed || rapidResponseStrengthRefunded) return;
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            TravelerEndpointUtility.AbortTraveler(
                this,
                "TSA_WD_Log_RapidResponseAborted".Translate(originObject?.LabelCap ?? "?").ToString(),
                manager);
        }

        /// <summary>Feature C sally: give up if the quarry is farther than 1.6× ambush watch range from the origin settlement.</summary>
        private bool TryAbortSettlementAmbushIfTargetEscaped(int targetTile)
        {
            if (!isSettlementAmbushSally || mission != TravelerMission.RapidResponseIntercept) return false;
            if (Destroyed || rapidResponseStrengthRefunded) return false;
            if (targetTile < 0) return false;
            if (!TravelerEndpointUtility.IsLiveEndpoint(originObject)) return false;
            int originTile = originObject.Tile;
            if (originTile < 0) return false;

            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (manager == null) return false;
            int dist = WorldActions_Utils.GetDistance(originTile, targetTile, manager);
            float watch = WorldDominationMod.settings?.settlementAmbushWatchRangeTiles
                ?? WorldDominationSettings.DefSettlementAmbushWatchRangeTiles;
            if (dist <= watch * SettlementAmbushUtility.PursuitRangeMult) return false;

            bool notifyPlayer = targetObject?.Faction != null && targetObject.Faction.IsPlayer;
            TravelerEndpointUtility.AbortTraveler(
                this,
                "TSA_WD_Log_SettlementAmbushPursuitStopped".Translate(originObject.LabelCap).ToString(),
                manager);
            if (notifyPlayer)
                Messages.Message("TSA_WD_Message_SettlementAmbushPursuitStopped".Translate(), MessageTypeDefOf.NeutralEvent);
            return true;
        }

        private void RefreshRapidResponseInterceptPathForCaravan(Caravan target, bool force)
        {
            if (target == null || target.Destroyed || !target.Spawned) return;
            int targetTile = target.Tile;
            if (targetTile < 0) return;

            if (targetTile == Tile)
            {
                if (pather.moving)
                    pather.StopDead();
                return;
            }

            if (!force && targetTile == lastRapidResponseTargetTile) return;
            lastRapidResponseTargetTile = targetTile;
            lastRapidResponseInterceptTile = targetTile;

            if (pather.destTile.Valid && pather.destTile.tileId == targetTile) return;
            PlanetTile targetPlanetTile = PlanetSurfaceWorldActions.PlanetTileForWdTravel(targetTile, this);
            if (!pather.RetargetDestinationAfterCurrentHop(targetPlanetTile))
                pather.StartPath(targetPlanetTile, skipLaunchTravelCache: true);
        }

        private void CompleteMovingTargetMeetupIfPossible()
        {
            if (mission == TravelerMission.RapidResponseIntercept)
                WorldActions_Traveler.TryCompleteRapidResponseSameTile(this);
            else if (mission == TravelerMission.RaidBribe)
                WorldActions_Traveler.TryCompleteRaidBribeSameTile(this);
        }

        private int ResolveRapidResponseInterceptTile(WorldObject_Traveler target)
        {
            if (target == null || target.Destroyed) return -1;
            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return target.Tile;

            // Already ahead on the hostile's route: meet them (or wait). Do not cut further toward their destination.
            if (IsBetweenHostileAndItsDestination(target))
                return ResolveChaseHostileTile(target);

            int bestLateTile = target.Tile;
            float bestLateTicks = float.MaxValue;
            if (TryUseRapidResponseInterceptCandidate(target.Tile, 0f, ref bestLateTile, ref bestLateTicks, out int reachableTile))
                return reachableTile;

            WD_PathFollower tp = target.pather;
            if (tp == null || !tp.moving)
                return bestLateTile;

            float targetEta = Mathf.Max(0f, tp.nextTileCostLeft) * Mathf.Max(1, target.ticksPerMove);
            if (tp.nextTile.Valid)
            {
                if (TryUseRapidResponseInterceptCandidate(tp.nextTile.tileId, targetEta, ref bestLateTile, ref bestLateTicks, out reachableTile))
                {
                    // Prefer aiming further along the path when we already know the path.
                    WorldPath pathEarly = tp.curPath;
                    if (pathEarly != null && pathEarly.Found && pathEarly.NodesLeftCount > 0)
                        return AdvanceRapidResponseAimAlongPath(pathEarly, -1, reachableTile);
                    return reachableTile;
                }
            }

            WorldPath path = tp.curPath;
            if (path != null && path.Found)
            {
                int fromTile = tp.nextTile.Valid ? tp.nextTile.tileId : target.Tile;
                int maxLookahead = Mathf.Min(path.NodesLeftCount, 20);
                for (int i = 0; i < maxLookahead; i++)
                {
                    PlanetTile node = path.Peek(i);
                    if (!node.Valid) continue;
                    targetEta += TravelUtils.GetTravelerHopDifficultyUnits(fromTile, node) * Mathf.Max(1, target.ticksPerMove);
                    fromTile = node.tileId;
                    if (TryUseRapidResponseInterceptCandidate(node.tileId, targetEta, ref bestLateTile, ref bestLateTicks, out reachableTile))
                        return AdvanceRapidResponseAimAlongPath(path, i, reachableTile);
                }
            }

            return bestLateTile;
        }

        /// <summary>True when we are closer to the hostile's destination than they are, and roughly on their corridor.</summary>
        private bool IsBetweenHostileAndItsDestination(WorldObject_Traveler target)
        {
            if (target == null) return false;
            WD_PathFollower tp = target.pather;
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || tp == null || !tp.moving || !tp.destTile.Valid) return false;

            int us = Tile;
            int hostile = target.Tile;
            int dest = tp.destTile.tileId;
            if (us < 0 || hostile < 0 || dest < 0) return false;

            float hostileToDest = grid.ApproxDistanceInTiles(hostile, dest);
            float usToDest = grid.ApproxDistanceInTiles(us, dest);
            float usToHostile = grid.ApproxDistanceInTiles(us, hostile);
            if (usToDest >= hostileToDest - 0.5f) return false;

            // Corridor check: us lies near the hostile→dest geodesic.
            float slack = usToHostile + usToDest - hostileToDest;
            return slack <= 2.5f;
        }

        /// <summary>Aim at the hostile (next hop if moving). Prefer meeting them over sitting on a cut-off tile behind us.</summary>
        private static int ResolveChaseHostileTile(WorldObject_Traveler target)
        {
            if (target == null) return -1;
            int chaseTile = target.Tile;
            WD_PathFollower tp = target.pather;
            if (tp != null && tp.moving && tp.nextTile.Valid)
                chaseTile = tp.nextTile.tileId;
            return chaseTile;
        }

        /// <summary>Never path to a tile farther from the hostile than we already are (that reads as fleeing).</summary>
        private int ClampRapidResponseAimNotAwayFromHostile(int aimTile, WorldObject_Traveler target)
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || target == null || aimTile < 0) return aimTile;

            int us = Tile;
            int hostile = target.Tile;
            if (us < 0 || hostile < 0) return aimTile;

            float distUs = grid.ApproxDistanceInTiles(us, hostile);
            float distAim = grid.ApproxDistanceInTiles(aimTile, hostile);
            if (distAim <= distUs + 0.5f)
                return aimTile;

            int chase = ResolveChaseHostileTile(target);
            // If chase would also be farther (rare), wait in place.
            if (chase < 0) return us;
            float distChase = grid.ApproxDistanceInTiles(chase, hostile);
            return distChase <= distUs + 0.5f ? chase : us;
        }

        /// <summary>After the first viable intercept tile on the target path, aim +2 nodes further (clamp to path end).</summary>
        private static int AdvanceRapidResponseAimAlongPath(WorldPath path, int foundIndex, int fallbackTile)
        {
            if (path == null || !path.Found) return fallbackTile;
            int nodes = path.NodesLeftCount;
            if (nodes <= 0) return fallbackTile;
            int aimIndex = foundIndex < 0
                ? Mathf.Min(1, nodes - 1) // nextTile was viable → aim ~2 into path (index 1)
                : Mathf.Min(foundIndex + 2, nodes - 1);
            if (aimIndex < 0) return fallbackTile;
            PlanetTile node = path.Peek(aimIndex);
            return node.Valid ? node.tileId : fallbackTile;
        }

        private bool TryUseRapidResponseInterceptCandidate(int candidateTile, float targetEtaTicks, ref int bestLateTile, ref float bestLateTicks, out int reachableTile)
        {
            reachableTile = -1;
            if (candidateTile < 0) return false;
            float responseEta = EstimateRapidResponseTicksTo(candidateTile);
            if (responseEta <= targetEtaTicks)
            {
                reachableTile = candidateTile;
                return true;
            }

            float lateTicks = responseEta - targetEtaTicks;
            if (lateTicks < bestLateTicks)
            {
                bestLateTicks = lateTicks;
                bestLateTile = candidateTile;
            }
            return false;
        }

        private float EstimateRapidResponseTicksTo(int tile)
        {
            if (tile < 0 || Find.WorldGrid == null) return 0f;
            if (tile == Tile) return 0f;
            float ticksPerTile = Mathf.Max(1, ticksPerMove);
            if (pather != null && pather.moving && pather.nextTile.Valid)
            {
                if (pather.nextTile.tileId == tile)
                    return Mathf.Max(0f, pather.nextTileCostLeft) * ticksPerTile;
                return Mathf.Max(0f, pather.nextTileCostLeft) * ticksPerTile
                    + Mathf.Max(1f, Find.WorldGrid.ApproxDistanceInTiles(pather.nextTile.tileId, tile)) * ticksPerTile;
            }
            return Mathf.Max(1f, Find.WorldGrid.ApproxDistanceInTiles(Tile, tile)) * ticksPerTile;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref pather, "pather", this);
            Scribe_Values.Look(ref mission, "mission");
            Scribe_Values.Look(ref travelerStrength, "travelerStrength");
            Scribe_Values.Look(ref initialStrength, "initialStrength");
            Scribe_Values.Look(ref projectedArrivalStrength, "projectedArrivalStrength");
            Scribe_Values.Look(ref spikeTrapsTriggered, "spikeTrapsTriggered", 0);
            Scribe_Values.Look(ref pollutionDamageWarned, "pollutionDamageWarned", false);
            Scribe_Values.Look(ref ticksPerMove, "ticksPerMove", DefaultTicksPerMove);
            Scribe_Values.Look(ref cachedTotalTravelTicksAtLaunch, "cachedTotalTravelTicksAtLaunch", -1f);
            Scribe_References.Look(ref originObject, "originObject");
            Scribe_References.Look(ref targetObject, "targetObject");
            Scribe_Values.Look(ref playerOrderedTrader, "playerOrderedTrader", false);
            Scribe_Defs.Look(ref orderedTraderKind, "orderedTraderKind");
            Scribe_Values.Look(ref raidOrderOutcome, "raidOrderOutcome", RaidOrderOutcome.PlayerOutpostConquestMenu);
            Scribe_Values.Look(ref alliedRaidOrderGoodwillPaid, "alliedRaidOrderGoodwillPaid", 0);
            Scribe_Values.Look(ref alliedRaidOrderGoodwillRefunded, "alliedRaidOrderGoodwillRefunded", false);
            Scribe_Values.Look(ref playerColonyRaidCooldownReservationTick, "playerColonyRaidCooldownReservationTick", -1);
            Scribe_Values.Look(ref targetRaidDefenseCooldownReservationTick, "targetRaidDefenseCooldownReservationTick", -1);
            Scribe_Values.Look(ref cachedTargetKind, "cachedTargetKind", RaidLaunchTargetKind.NPC);
            Scribe_Values.Look(ref targetOfOpportunityRetargets, "targetOfOpportunityRetargets", 0);
            Scribe_Values.Look(ref maraudingChainCount, "maraudingChainCount", 0);
            Scribe_Values.Look(ref totalTargetChanges, "totalTargetChanges", 0);
            Scribe_Values.Look(ref lastOpportunityEvalTick, "lastOpportunityEvalTick", -99999);
            Scribe_Values.Look(ref isTurretDetour, "isTurretDetour", false);
            // Legacy single-flag saves: treat as proximity + hit budget fully spent.
            bool legacyAtTurretDetourConsumed = false;
            Scribe_Values.Look(ref legacyAtTurretDetourConsumed, "atTurretDetourConsumed", false);
            Scribe_Values.Look(ref atTurretProximityDetourConsumed, "atTurretProximityDetourConsumed", false);
            Scribe_Values.Look(ref atTurretHitDetourCount, "atTurretHitDetourCount", 0);
            if (Scribe.mode == LoadSaveMode.LoadingVars && legacyAtTurretDetourConsumed)
            {
                atTurretProximityDetourConsumed = true;
                if (atTurretHitDetourCount < AtTurretRetaliationUtility.MaxHitDetours)
                    atTurretHitDetourCount = AtTurretRetaliationUtility.MaxHitDetours;
            }
            Scribe_References.Look(ref preTurretDetourTarget, "preTurretDetourTarget");
            Scribe_Values.Look(ref preTurretDetourDestTileId, "preTurretDetourDestTileId", -1);
            Scribe_Values.Look(ref preTurretDetourCachedKind, "preTurretDetourCachedKind", RaidLaunchTargetKind.NPC);
            Scribe_Collections.Look(ref raidAttackerList, "raidAttackerList", LookMode.Reference);
            Scribe_Collections.Look(ref raidAttackerDetails, "raidAttackerDetails", LookMode.Value);
            Scribe_Collections.Look(ref raidAttackerForceRows, "raidAttackerForceRows", LookMode.Deep);
            Scribe_Collections.Look(ref raidDefenderForceRows, "raidDefenderForceRows", LookMode.Deep);
            Scribe_Values.Look(ref fortifyIsTrap, "fortifyIsTrap", false);
            Scribe_Values.Look(ref fortifySpikeTrapKind, "fortifySpikeTrapKind", SpikeTrapKind.Spike);
            Scribe_Values.Look(ref fortifyRoadBlockKind, "fortifyRoadBlockKind", RoadBlockKind.Light);
            Scribe_Values.Look(ref spawnTick, "spawnTick");
            Scribe_Values.Look(ref mortarDamage, "mortarDamage", 0f);
            Scribe_Values.Look(ref mortarHit, "mortarHit", true);
            Scribe_Values.Look(ref mortarTargetTravelerId, "mortarTargetTravelerId", -1);
            Scribe_Values.Look(ref antiAirIsResolver, "antiAirIsResolver", false);
            Scribe_Values.Look(ref antiAirTargetKind, "antiAirTargetKind", (byte)0);
            Scribe_Values.Look(ref antiAirLeadFlight, "antiAirLeadFlight", false);
            Scribe_Values.Look(ref antiAirLeadFrom, "antiAirLeadFrom", Vector3.zero);
            Scribe_Values.Look(ref antiAirLeadTo, "antiAirLeadTo", Vector3.zero);
            Scribe_Values.Look(ref antiAirLeadTicksTotal, "antiAirLeadTicksTotal", 0f);
            Scribe_Values.Look(ref antiAirLeadTicksLeft, "antiAirLeadTicksLeft", 0f);
            Scribe_Values.Look(ref rapidResponseStrengthRefunded, "rapidResponseStrengthRefunded", false);
            Scribe_Values.Look(ref isSettlementAmbushSally, "isSettlementAmbushSally", false);
            Scribe_Values.Look(ref lastRapidResponseTargetTile, "lastRapidResponseTargetTile", -1);
            Scribe_Values.Look(ref lastRapidResponseInterceptTile, "lastRapidResponseInterceptTile", -1);

            if (Scribe.mode == LoadSaveMode.Saving)
            {
                scribeContribKeys = new List<WorldObject>();
                scribeContribVals = new List<float>();
                if (contributionFactors != null)
                    foreach (var kv in contributionFactors)
                        if (kv.Key != null)
                        {
                            scribeContribKeys.Add(kv.Key);
                            scribeContribVals.Add(kv.Value);
                        }
            }
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                scribeContribKeys = null;
                scribeContribVals = null;
            }
            Scribe_Collections.Look(ref scribeContribKeys, "contribFactorKeys", LookMode.Reference);
            Scribe_Collections.Look(ref scribeContribVals, "contribFactorVals", LookMode.Value);

            // --- SURGICAL: Expose cached path ---
            Scribe_Collections.Look(ref cachedPathTiles, "cachedPathTiles", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (raidAttackerForceRows == null) raidAttackerForceRows = new List<RaidForceLogRow>();
                if (raidDefenderForceRows == null) raidDefenderForceRows = new List<RaidForceLogRow>();
                contributionFactors = new Dictionary<WorldObject, float>();
                var keys = scribeContribKeys ?? new List<WorldObject>();
                var vals = scribeContribVals ?? new List<float>();
                int n = Mathf.Min(keys.Count, vals.Count);
                if (keys.Count != vals.Count)
                    Log.Warning("[TSA World Domination] Traveler (tile " + Tile + "): contribution factor key/value count mismatch (" + keys.Count + " vs " + vals.Count + "); using first " + n + " pairs.");
                for (int i = 0; i < n; i++)
                    if (keys[i] != null)
                        contributionFactors[keys[i]] = vals[i];
                scribeContribKeys = null;
                scribeContribVals = null;
            }
        }

        public override Vector3 DrawPos => (tweenedPos == Vector3.zero) ? base.DrawPos : tweenedPos;

        /// <summary>Float menu when player has a caravan selected and right-clicks this traveler: Move here (tile only) or Attack Caravan (chase until in range).</summary>
        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Caravan caravan)
        {
            foreach (var opt in base.GetFloatMenuOptions(caravan))
                yield return opt;

            if (caravan == null || caravan.Faction != Faction.OfPlayer) yield break;

            int targetTile = Tile;

            yield return new FloatMenuOption("TSA_WD_Traveler_MoveHere".Translate(), () =>
            {
                if (caravan.Destroyed) return;
                caravan.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(targetTile, caravan), null, false, false);
            });

            if (!OdysseyGravshipCaravanClashCompat.ShouldSkipPlayerCaravanClash(caravan))
            {
                yield return new FloatMenuOption("TSA_WD_Traveler_AttackCaravan".Translate(), () =>
                {
                    if (caravan.Destroyed || this.Destroyed) return;
                    var comp = Find.World.GetComponent<WorldComponent_CaravanChaseTraveler>();
                    if (comp == null)
                    {
                        comp = new WorldComponent_CaravanChaseTraveler(Find.World);
                        Find.World.components.Add(comp);
                    }
                    comp.AddChase(caravan, this);
                    caravan.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(targetTile, caravan), null, false, false);
                });
            }
        }
    }

    public class WorldObject_Traveler_Outpost_Raid : WorldObject_Traveler { }
    public class WorldObject_Traveler_Outpost_RoadBuilder : WorldObject_Traveler { }

    /// <summary>Traveler that carries produced items from an outpost to the player colony and delivers them at the map edge (teleport-style).</summary>
    public class WorldObject_Traveler_Outpost_Delivery : WorldObject_Traveler
    {
        public const string DropPodIconTexturePath = "WorldObjects/DropPod_OutpostGoods";

        public List<ThingDefCountClass> deliveryItems = new List<ThingDefCountClass>();
        /// <summary>When true, flies straight to the destination tile (mortar-style) and delivers via drop pods on arrival.</summary>
        public bool deliveryViaDropPod;

        public bool UsesBallisticWorldFlight => deliveryViaDropPod;

        /// <summary>
        /// Warehouse drop-pod deliveries share the goods traveler def; invalidate the cached material when the icon path changes.
        /// </summary>
        public override string? ResolveIconTexturePath()
        {
            if (deliveryViaDropPod)
                return DropPodIconTexturePath;
            return base.ResolveIconTexturePath();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref deliveryItems, "deliveryItems", LookMode.Deep);
            Scribe_Values.Look(ref deliveryViaDropPod, "deliveryViaDropPod", false);
            if (deliveryItems == null) deliveryItems = new List<ThingDefCountClass>();
            if (Scribe.mode == LoadSaveMode.PostLoadInit && deliveryViaDropPod)
                InvalidateTravelerMaterialCache();
        }
    }

    public class WorldObject_Traveler_Outpost_Upgrade : WorldObject_Traveler
    {
        public const string DropPodIconTexturePath = "WorldObjects/DropPod_OutpostUpgrade";

        public string upgradeDefName;
        public int upgradeLevel;
        /// <summary>When true, flies ballistic like goods drop pods and can be engaged by anti-air.</summary>
        public bool upgradeViaDropPod;

        public bool UsesBallisticWorldFlight => upgradeViaDropPod;

        public override string? ResolveIconTexturePath()
        {
            if (upgradeViaDropPod)
                return DropPodIconTexturePath;
            return base.ResolveIconTexturePath();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref upgradeDefName, "upgradeDefName");
            Scribe_Values.Look(ref upgradeLevel, "upgradeLevel", 0);
            Scribe_Values.Look(ref upgradeViaDropPod, "upgradeViaDropPod", false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && upgradeViaDropPod)
                InvalidateTravelerMaterialCache();
        }
    }

    /// <summary>Buy / gift / negotiate payment travelers. Share Caravan_Trader / DropPod_Trader art; bribes stay land-only.</summary>
    public abstract class WorldObject_Traveler_TradePayment : WorldObject_Traveler
    {
        public const string DropPodTraderTexturePath = "WorldObjects/DropPod_Trader";

        public bool tradeViaDropPod;

        public bool UsesBallisticWorldFlight => tradeViaDropPod;

        public override string? ResolveIconTexturePath()
        {
            if (tradeViaDropPod)
                return DropPodTraderTexturePath;
            return base.ResolveIconTexturePath();
        }

        protected void ExposeTradeDropPod()
        {
            Scribe_Values.Look(ref tradeViaDropPod, "tradeViaDropPod", false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && tradeViaDropPod)
                InvalidateTravelerMaterialCache();
        }
    }

    /// <summary>Virtual buy caravan: carries reserved payment goods to an ally/neutral NPC settlement.</summary>
    public class WorldObject_Traveler_SettlementBuy : WorldObject_Traveler_TradePayment
    {
        public List<ThingDefCountClass> paymentItems = new List<ThingDefCountClass>();
        /// <summary>Goodwill already deducted at launch; refunded on abort unless lost in transit.</summary>
        public int pendingGoodwill;
        public Faction sellerFaction;
        /// <summary>Tier locked at launch; mismatch invalidates the deal.</summary>
        public SettlementTier dealTier = SettlementTier.T1;
        public bool completed;
        public bool paymentRefunded;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref paymentItems, "paymentItems", LookMode.Deep);
            Scribe_Values.Look(ref pendingGoodwill, "pendingGoodwill", 0);
            Scribe_References.Look(ref sellerFaction, "sellerFaction");
            Scribe_Values.Look(ref dealTier, "dealTier", SettlementTier.T1);
            Scribe_Values.Look(ref completed, "completed", false);
            Scribe_Values.Look(ref paymentRefunded, "paymentRefunded", false);
            ExposeTradeDropPod();
            if (paymentItems == null) paymentItems = new List<ThingDefCountClass>();
        }

        public override void Destroy()
        {
            // Invalid-deal / arrival abort refunds first. Mid-route combat Destroy = payment lost (or seized if clash set looter first).
            if (!completed && !paymentRefunded)
                SettlementBuyUtility.MarkPaymentLostInTransit(this);
            base.Destroy();
        }
    }

    /// <summary>Virtual gift caravan: carries reserved goods to an ally/neutral NPC settlement.</summary>
    public class WorldObject_Traveler_SettlementGift : WorldObject_Traveler_TradePayment
    {
        public List<ThingDefCountClass> paymentItems = new List<ThingDefCountClass>();
        public Faction recipientFaction;
        public bool completed;
        public bool paymentRefunded;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref paymentItems, "paymentItems", LookMode.Deep);
            Scribe_References.Look(ref recipientFaction, "recipientFaction");
            Scribe_Values.Look(ref completed, "completed", false);
            Scribe_Values.Look(ref paymentRefunded, "paymentRefunded", false);
            ExposeTradeDropPod();
            if (paymentItems == null) paymentItems = new List<ThingDefCountClass>();
        }

        public override void Destroy()
        {
            if (!completed && !paymentRefunded)
                SettlementGiftUtility.MarkPaymentLostInTransit(this);
            base.Destroy();
        }
    }

    /// <summary>Virtual bribe caravan: settlement ceasefire delivery, or chase-dissolve of a ground raid.</summary>
    public class WorldObject_Traveler_SettlementBribe : WorldObject_Traveler
    {
        public enum BribeKind : byte { Settlement = 0, Raid = 1 }

        public List<ThingDefCountClass> paymentItems = new List<ThingDefCountClass>();
        public Faction targetFaction;
        public BribeKind bribeKind = BribeKind.Settlement;
        public int ceasefireDays;
        public float askSilver;
        public bool completed;
        public bool paymentRefunded;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref paymentItems, "paymentItems", LookMode.Deep);
            Scribe_References.Look(ref targetFaction, "targetFaction");
            Scribe_Values.Look(ref bribeKind, "bribeKind", BribeKind.Settlement);
            Scribe_Values.Look(ref ceasefireDays, "ceasefireDays", 0);
            Scribe_Values.Look(ref askSilver, "askSilver", 0f);
            Scribe_Values.Look(ref completed, "completed", false);
            Scribe_Values.Look(ref paymentRefunded, "paymentRefunded", false);
            if (paymentItems == null) paymentItems = new List<ThingDefCountClass>();
        }

        public override void Destroy()
        {
            if (!completed && !paymentRefunded)
                SettlementBribeUtility.MarkPaymentLostInTransit(this);
            base.Destroy();
        }
    }

    /// <summary>Virtual negotiate caravan: pays a faction to change relation with another faction.</summary>
    public class WorldObject_Traveler_DiplomacyNegotiate : WorldObject_Traveler_TradePayment
    {
        public List<ThingDefCountClass> paymentItems = new List<ThingDefCountClass>();
        public Faction negotiatorFaction;
        public Faction targetFaction;
        public DiplomacyNegotiateAction action;
        public FactionRelationKind desiredKind = FactionRelationKind.Neutral;
        public float askSilver;
        public bool completed;
        public bool paymentRefunded;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref paymentItems, "paymentItems", LookMode.Deep);
            Scribe_References.Look(ref negotiatorFaction, "negotiatorFaction");
            Scribe_References.Look(ref targetFaction, "targetFaction");
            Scribe_Values.Look(ref action, "action", DiplomacyNegotiateAction.DeclareWar);
            Scribe_Values.Look(ref desiredKind, "desiredKind", FactionRelationKind.Neutral);
            Scribe_Values.Look(ref askSilver, "askSilver", 0f);
            Scribe_Values.Look(ref completed, "completed", false);
            Scribe_Values.Look(ref paymentRefunded, "paymentRefunded", false);
            ExposeTradeDropPod();
            if (paymentItems == null) paymentItems = new List<ThingDefCountClass>();
        }

        public override void Destroy()
        {
            if (!completed && !paymentRefunded)
                DiplomacyNegotiateUtility.MarkPaymentLostInTransit(this);
            base.Destroy();
        }
    }

    /// <summary>Rapid Response drop-pod dispatch: ballistic flight (warehouse drop-pod speed) carrying real pawns.</summary>
    public class WorldObject_Traveler_RapidResponseDropPod : WorldObject_Traveler
    {
        public List<Pawn> carriedPawns = new List<Pawn>();

        public bool UsesBallisticWorldFlight => true;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref carriedPawns, "carriedPawns", LookMode.Deep);
            if (carriedPawns == null) carriedPawns = new List<Pawn>();
        }

        public override void Destroy()
        {
            ReturnCarriedPawnsToOrigin();
            base.Destroy();
        }

        /// <summary>If destroyed or aborted before arrival, put surviving pawns back into the origin outpost.</summary>
        public void ReturnCarriedPawnsToOrigin()
        {
            if (carriedPawns == null || carriedPawns.Count == 0) return;
            var origin = originObject as WorldObject_WD_Outpost;
            for (int i = 0; i < carriedPawns.Count; i++)
            {
                Pawn p = carriedPawns[i];
                if (p == null || p.Destroyed || p.Dead) continue;
                if (origin != null && !origin.Destroyed)
                    origin.AddPawn(p, null!);
                else
                    p.Destroy();
            }
            carriedPawns.Clear();
        }

        /// <summary>Hand off carried pawns for arrival delivery without returning them to origin on Destroy.</summary>
        public List<Pawn> TakeCarriedPawns()
        {
            var list = carriedPawns ?? new List<Pawn>();
            carriedPawns = new List<Pawn>();
            return list;
        }
    }

    /// <summary>Removes world objects that are traveler defs but not the current WorldObject_Traveler type (e.g. from old TSA_WorldDomination.Experimental namespace).</summary>
    public static class TravelerRemnantCleanup
    {
        public static int RemoveOrphanedTravelers()
        {
            if (Find.WorldObjects == null) return 0;
            var toRemove = Find.WorldObjects.AllWorldObjects
                .Where(wo => wo != null && IsOrphanedTraveler(wo))
                .ToList();
            foreach (var wo in toRemove)
                wo.Destroy();
            return toRemove.Count;
        }

        private static bool IsOrphanedTraveler(WorldObject wo)
        {
            if (wo.def?.defName == null) return false;
            if (!wo.def.defName.StartsWith("TSA_WD_Traveler_")) return false;
            if (wo is WorldObject_Traveler) return false;
            return true;
        }
    }
}
