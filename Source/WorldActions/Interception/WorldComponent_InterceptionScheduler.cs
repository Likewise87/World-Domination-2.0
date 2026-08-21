using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Central staggered scanner for all <see cref="IDefensiveInterceptor"/> implementations.
    /// Player mortar / Rapid Response keep <c>interceptionScanIntervalTicks</c>.
    /// NPC T4 settlements use 3× that interval and at most 3 scans per NPC cycle (round-robin).
    /// Out-of-range traveler pairs use skip-until so we do not re-distance every cycle.
    /// Hostile drop pods also wake AA-capable mortar outposts immediately on register.
    /// Vanilla transport pods and Vehicle Framework aerials: arm AA along the flight arc at launch,
    /// recheck DrawPos every second, fire once when in range, then sleep that shooter for that target.
    /// </summary>
    public class WorldComponent_InterceptionScheduler : WorldComponent
    {
        private readonly List<IDefensiveInterceptor> interceptors = new List<IDefensiveInterceptor>();
        private readonly HashSet<WorldObject_Traveler> travelers = new HashSet<WorldObject_Traveler>();
        private readonly HashSet<WorldObject> externalAirborne = new HashSet<WorldObject>();
        private readonly HashSet<int> inboundMortarTargetIdsScratch = new HashSet<int>(16);
        /// <summary>Active Rapid Response intercepts keyed by origin outpost ID × target traveler ID.
        /// Same outpost cannot stack two responses on one target; different outposts may.</summary>
        private readonly HashSet<long> inboundRapidResponseOriginTargetPairsScratch = new HashSet<long>(16);
        /// <summary>Persistent one-attempt lock: once an outpost dispatches at a target, no second launch even if the
        /// interceptor is destroyed. Cleaned when the target traveler no longer exists.</summary>
        private HashSet<long> dispatchedRapidResponsePairs = new HashSet<long>(16);

        private readonly List<IDefensiveInterceptor> playerScratch = new List<IDefensiveInterceptor>(32);
        private readonly List<IDefensiveInterceptor> npcT4Scratch = new List<IDefensiveInterceptor>(64);
        private readonly Dictionary<int, int> pendingExternalAirborneWakeTickById = new Dictionary<int, int>(8);
        /// <summary>AT turrets waiting for cooldown to elapse before <see cref="TryEngageAtTurretTargets"/>.</summary>
        private readonly Dictionary<int, int> pendingAtCooldownWakeTickById = new Dictionary<int, int>(16);
        /// <summary>AA shooters armed for an external airborne flight (interceptor ID × target ID). Checked every second.</summary>
        private readonly HashSet<long> armedExternalAirborneAaPairs = new HashSet<long>();
        /// <summary>O(1) lookup for mortar shells in <see cref="armedExternalAirborneAaPairs"/> (do not scan <see cref="travelers"/>).</summary>
        private readonly Dictionary<int, WorldObject_Traveler> armedMortarShellsById = new Dictionary<int, WorldObject_Traveler>(8);
        private int npcT4Cursor;

        /// <summary>Per interceptor→traveler pair: earliest tick to recheck distance after out-of-range.</summary>
        private readonly Dictionary<long, int> skipUntilByPair = new Dictionary<long, int>(128);

        private int cachedIntervalTicks = -1;
        private int cachedIntervalTickLastRefresh = -99999;
        private const int IntervalRefreshCadence = 600;
        private const int NpcT4PerCycle = 3;
        private const int ExternalAirborneInitialWakeDelayTicks = 2;
        /// <summary>Armed external-airborne AA recheck cadence (1 second).</summary>
        private const int ExternalAirborneArmedCheckIntervalTicks = 60;
        /// <summary>Conservative max close rate (tiles/tick): fastest Rapid Response clamp is ticksPerMove ≥ 60.</summary>
        private const float MaxCloseTilesPerTickGround = 1f / 60f;
        /// <summary>Ballistic drop pods / shells use ticksPerMove ≈ 10–13.</summary>
        private const float MaxCloseTilesPerTickBallistic = 1f / 10f;
        private const float SkipUntilSafetyMargin = 1.15f;

        private WorldComponent_SpreadManager spreadManagerScratch;

        public WorldComponent_InterceptionScheduler(World world) : base(world) { }

        public static WorldComponent_InterceptionScheduler Of(World world)
        {
            if (world == null) return null;
            var c = world.GetComponent<WorldComponent_InterceptionScheduler>();
            if (c == null)
            {
                c = new WorldComponent_InterceptionScheduler(world);
                world.components.Add(c);
            }
            return c;
        }

        public static WorldComponent_InterceptionScheduler Current =>
            Find.World == null ? null : Of(Find.World);

        public void RegisterInterceptor(IDefensiveInterceptor ip)
        {
            if (ip == null) return;
            if (!interceptors.Contains(ip)) interceptors.Add(ip);
        }

        public void UnregisterInterceptor(IDefensiveInterceptor ip)
        {
            if (ip == null) return;
            interceptors.Remove(ip);
            ClearSkipUntilForInterceptor(ip);
            ClearArmedExternalAirborneAaForInterceptor(ip);
            if (ip.Self != null)
                pendingAtCooldownWakeTickById.Remove(ip.Self.ID);
        }

        public void RegisterTraveler(WorldObject_Traveler t)
        {
            if (t == null) return;
            travelers.Add(t);
            // Include player-faction shells/pods: hostility + late-game / inbound gates in Notify
            // (hostile mortar shells skip the escalation gate).
            if (t.Faction != null && AntiAirFireUtils.IsAirborneAaTarget(t))
                NotifyHostileAirborneTarget(t);
        }

        public void UnregisterTraveler(WorldObject_Traveler t)
        {
            if (t == null) return;
            travelers.Remove(t);
            ClearSkipUntilForTraveler(t);
            ClearArmedExternalAirborneAaForTarget(t);
            armedMortarShellsById.Remove(t.ID);
            AntiAirFireUtils.NotifyTargetDestroyed(t);
        }

        public void RegisterVanillaPods(TravellingTransporters pods)
            => RegisterExternalAirborne(pods);

        /// <summary>Vanilla transport pods or Vehicle Framework aerials: track + delayed arm along flight arc.</summary>
        public void RegisterExternalAirborne(WorldObject target)
        {
            if (target == null || target.Destroyed) return;
            if (!(target is TravellingTransporters) && !VehicleFrameworkAerialAaCompat.IsAerialVehicleInFlight(target))
                return;
            externalAirborne.Add(target);
            pendingExternalAirborneWakeTickById[target.ID] =
                (Find.TickManager?.TicksGame ?? 0) + ExternalAirborneInitialWakeDelayTicks;
        }

        /// <summary>Immediate re-arm (e.g. VF <c>OrderFlyToTiles</c> after path is set).</summary>
        public void ArmExternalAirborneAaNow(WorldObject target)
        {
            if (target == null || target.Destroyed) return;
            externalAirborne.Add(target);
            pendingExternalAirborneWakeTickById.Remove(target.ID);
            ArmExternalAirborneAaAlongFlight(target);
        }

        /// <summary>
        /// True while this Rapid Response outpost already has an intercept traveler chasing <paramref name="target"/>.
        /// Different outposts may still engage the same target independently.
        /// </summary>
        public bool HasActiveRapidResponseFrom(WorldObject_WD_Outpost origin, WorldObject_Traveler target)
        {
            if (origin == null || target == null || target.Destroyed) return false;
            long key = MakePairKey(origin.ID, target.ID);
            if (dispatchedRapidResponsePairs.Contains(key))
                return true;
            if (inboundRapidResponseOriginTargetPairsScratch.Contains(key))
                return true;

            foreach (WorldObject_Traveler t in travelers)
            {
                if (t == null || t.Destroyed || !t.Spawned) continue;
                if (t.mission != TravelerMission.RapidResponseIntercept) continue;
                if (t.originObject != origin) continue;
                if (t.targetObject != target) continue;
                return true;
            }
            return false;
        }

        /// <summary>Register an origin→target pair immediately after dispatch so same-tick re-scans cannot stack.</summary>
        public void NotifyRapidResponseDispatched(WorldObject_WD_Outpost origin, WorldObject_Traveler target)
        {
            if (origin == null || target == null) return;
            long key = MakePairKey(origin.ID, target.ID);
            inboundRapidResponseOriginTargetPairsScratch.Add(key);
            dispatchedRapidResponsePairs.Add(key);
        }

        public int ActiveInterceptorCount => interceptors.Count;
        public int TrackedTravelerCount => travelers.Count;

        /// <summary>Read-only snapshot of registered interceptors, for <see cref="WorldComponent_SettlementWatchIndex"/> rebuilds.</summary>
        public IReadOnlyList<IDefensiveInterceptor> AllInterceptorsSnapshot() => interceptors;

        /// <summary>One-shot wake when an AT turret's cooldown reaches <see cref="WorldObject_AT_Turret.cooldownTick"/>.</summary>
        public void ScheduleAtTurretCooldownWake(WorldObject_AT_Turret gun)
        {
            if (gun == null || gun.Destroyed) return;
            pendingAtCooldownWakeTickById[gun.ID] = gun.cooldownTick;
        }

        /// <summary>
        /// Built / defense-on / range change: this gun tries to engage, then hostile ATs covering its tile wake
        /// (new target appeared in their bubble).
        /// </summary>
        public void NotifyAtTurretEngagementOpportunity(WorldObject_AT_Turret gun)
        {
            if (gun == null || gun.Destroyed) return;
            TryEngageAtTurretTargets(gun);
            WakeHostileAtTurretsCoveringTile(gun.Tile.tileId, exclude: gun);
        }

        /// <summary>
        /// A world object that ATs may have been aiming at was destroyed. Guns still aiming it cancel and
        /// retarget if not on CD; other ready hostile ATs covering its tile also re-evaluate.
        /// Already-fired guns (on CD) stay quiet until their CD wake.
        /// </summary>
        public void NotifyPotentialAtTargetDestroyed(WorldObject destroyed)
        {
            if (destroyed == null) return;

            for (int i = 0; i < interceptors.Count; i++)
            {
                if (!(interceptors[i]?.Self is WorldObject_AT_Turret gun) || gun.Destroyed)
                    continue;
                if (gun == destroyed) continue;
                if (!gun.IsAimingAt(destroyed)) continue;
                // Still aiming = has not fired yet (CD applies on fire). Retarget or return to idle.
                gun.NotifyPendingTargetLostAndRetarget();
            }

            int tileId = destroyed.Tile.tileId;
            if (tileId >= 0)
                WakeHostileAtTurretsCoveringTile(tileId, exclude: destroyed as WorldObject_AT_Turret);
        }

        /// <summary>Mod settings AT range changed: rebuild watch bubbles and re-evaluate every registered AT.</summary>
        public void NotifyAtTurretRangeSettingsChanged()
        {
            WorldComponent_SettlementWatchIndex.Get()?.Invalidate();
            WD_RadiusOverlayPrefs.InvalidateResolveCache();
            for (int i = 0; i < interceptors.Count; i++)
            {
                if (interceptors[i]?.Self is WorldObject_AT_Turret gun && !gun.Destroyed)
                    TryEngageAtTurretTargets(gun);
            }
        }

        /// <summary>
        /// Event path for AT idle fire: nearest hostile ground traveler in range first, else nearest hostile AT
        /// via interceptor registry + distance (not GetWatchers on own tile).
        /// </summary>
        public void TryEngageAtTurretTargets(WorldObject_AT_Turret gun)
        {
            if (gun == null || gun.Destroyed || !gun.DefenseActive) return;
            if (!((IDefensiveInterceptor)gun).InterceptorCanFireNow()) return;
            Faction iFaction = gun.Faction;
            if (iFaction == null) return;

            PlanetTile iTile = gun.Tile;
            if (iTile.tileId < 0) return;
            float range = gun.EffectiveRangeTiles;
            if (range <= 0f) return;

            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();

            WorldObject_Traveler bestTraveler = null;
            float bestTravelerDist = float.MaxValue;
            foreach (WorldObject_Traveler t in travelers)
            {
                if (!IsEligibleAtTurretTravelerTarget(gun, t, iFaction, out _)) continue;
                int tTileId = t.Tile.tileId;
                if (tTileId < 0) continue;
                float dist = manager != null
                    ? (float)WorldActions_Utils.GetDistance(iTile.tileId, tTileId, manager)
                    : Find.WorldGrid.ApproxDistanceInTiles(iTile.tileId, tTileId);
                if (dist > range) continue;
                if (dist < bestTravelerDist)
                {
                    bestTravelerDist = dist;
                    bestTraveler = t;
                }
            }

            if (bestTraveler != null)
            {
                ((IDefensiveInterceptor)gun).InterceptorFire(bestTraveler, bestTravelerDist);
                return;
            }

            WorldObject_AT_Turret bestAt = null;
            float bestAtDist = float.MaxValue;
            for (int i = 0; i < interceptors.Count; i++)
            {
                if (!(interceptors[i]?.Self is WorldObject_AT_Turret other) || other == gun || other.Destroyed)
                    continue;
                if (!other.DefenseActive) continue;
                Faction of = other.Faction;
                if (of == null || of == iFaction || !WorldActions_Utils.SafeHostileTo(of, iFaction))
                    continue;
                int oTileId = other.Tile.tileId;
                if (oTileId < 0) continue;
                float dist = manager != null
                    ? (float)WorldActions_Utils.GetDistance(iTile.tileId, oTileId, manager)
                    : Find.WorldGrid.ApproxDistanceInTiles(iTile.tileId, oTileId);
                if (dist > range) continue;
                if (dist < bestAtDist)
                {
                    bestAtDist = dist;
                    bestAt = other;
                }
            }

            if (bestAt != null)
                gun.TryFireAtWorldObject(bestAt, bestAtDist);
        }

        private static bool IsEligibleAtTurretTravelerTarget(
            WorldObject_AT_Turret gun, WorldObject_Traveler t, Faction iFaction, out Faction tf)
        {
            tf = null;
            if (t == null || t.Destroyed) return false;
            if (!AtTurretUtility.IsGroundAtTurretTravelerTarget(t)) return false;
            tf = t.Faction;
            if (tf == null || tf == iFaction || !WorldActions_Utils.SafeHostileTo(tf, iFaction))
                return false;
            if (tf.IsPlayer)
                return AtTurretUtility.CanAutoTargetPlayerTraveler(gun, t);
            return RapidResponseUtility.IsEligibleAutoInterceptTarget(t, gun.DefenseRaidTargetMask)
                && InterceptionMissionMaskUtils.Matches(t.mission, gun.DefenseMask);
        }

        private void WakeHostileAtTurretsCoveringTile(int tileId, WorldObject_AT_Turret exclude)
        {
            if (tileId < 0) return;
            var watchIndex = WorldComponent_SettlementWatchIndex.Get();
            if (watchIndex == null) return;
            // GetWatchers reuses a scratch list; copy before nested TryEngage can call GetWatchers again.
            List<WorldObject> watchers = watchIndex.GetWatchers(tileId, WatchCapability.Interceptor);
            if (watchers.Count == 0) return;
            List<WorldObject_AT_Turret> toWake = new List<WorldObject_AT_Turret>(watchers.Count);
            Faction excludeFaction = exclude?.Faction;
            for (int i = 0; i < watchers.Count; i++)
            {
                if (!(watchers[i] is WorldObject_AT_Turret other) || other == exclude || other.Destroyed)
                    continue;
                if (!other.DefenseActive) continue;
                Faction of = other.Faction;
                if (of == null) continue;
                if (excludeFaction != null && (of == excludeFaction || !WorldActions_Utils.SafeHostileTo(of, excludeFaction)))
                    continue;
                toWake.Add(other);
            }

            for (int i = 0; i < toWake.Count; i++)
                TryEngageAtTurretTargets(toWake[i]);
        }

        private void ProcessPendingAtCooldownWakes()
        {
            if (pendingAtCooldownWakeTickById.Count == 0) return;
            int now = Find.TickManager?.TicksGame ?? 0;
            skipUntilRemoveScratch.Clear();
            foreach (var kv in pendingAtCooldownWakeTickById)
            {
                if (now < kv.Value) continue;
                skipUntilRemoveScratch.Add(kv.Key);
            }

            for (int i = 0; i < skipUntilRemoveScratch.Count; i++)
            {
                int id = (int)skipUntilRemoveScratch[i];
                pendingAtCooldownWakeTickById.Remove(id);
                WorldObject_AT_Turret gun = FindAtTurretInterceptorById(id);
                if (gun != null && !gun.Destroyed)
                    TryEngageAtTurretTargets(gun);
            }
        }

        private WorldObject_AT_Turret FindAtTurretInterceptorById(int id)
        {
            for (int i = 0; i < interceptors.Count; i++)
            {
                if (interceptors[i]?.Self is WorldObject_AT_Turret gun && gun.ID == id)
                    return gun;
            }
            return null;
        }

        /// <summary>Legacy name — routes to <see cref="NotifyHostileAirborneTarget"/>.</summary>
        public void NotifyHostileDropPodAirborne(WorldObject_Traveler pod)
            => NotifyHostileAirborneTarget(pod);

        /// <summary>Queue AA engages from ready player outposts and T4 settlements in AA range.</summary>
        /// <param name="logSkips">When false (mid-flight pulse), only log successful queues to avoid spam.</param>
        public void NotifyHostileAirborneTarget(WorldObject target, bool logSkips = true)
        {
            if (target == null || target.Destroyed) return;
            Faction tf = target.Faction;
            if (tf == null)
            {
                if (logSkips) WDVerbose.Msg($"AA wake skip: {LabelOf(target)} has no faction");
                return;
            }

            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            int considered = 0;
            int queued = 0;
            for (int i = 0; i < interceptors.Count; i++)
            {
                var ip = interceptors[i];
                if (ip?.Self == null || ip.Self.Destroyed) continue;

                Faction iFaction = ip.InterceptorFaction;
                if (iFaction == null || !WorldActions_Utils.SafeHostileTo(tf, iFaction)) continue;

                bool inbound = AntiAirFireUtils.IsInboundThreatTo(ip.Self, target);
                // Player airborne: late-game / AA-vs-player gate, except inbound (self-defense) and hostile mortar shells.
                if (tf.IsPlayer
                    && !ip.InterceptorCanTargetPlayer
                    && !inbound
                    && !AntiAirFireUtils.IsHostileMortarShell(target))
                {
                    if (logSkips)
                        WDVerbose.Msg($"AA wake skip {ip.Self.LabelCap} vs {LabelOf(target)}: player gate (not inbound) — {DescribePlayerGate(ip)}");
                    continue;
                }

                if (ip.Self is WorldObject_WD_Outpost outpost)
                {
                    if (!outpost.IsMortarOutpost) continue;
                    if (!outpost.AntiAirDefenseActive) continue;
                    if (!AntiAirFireUtils.HasAntiAirUpgrade(outpost)) continue;
                    considered++;
                    float aaRange = AntiAirFireUtils.GetPlayerAntiAirMaxRangeTiles(outpost);
                    if (!AntiAirFireUtils.IsAirborneInAaRange(outpost, target, aaRange, manager))
                    {
                        if (logSkips)
                            WDVerbose.Msg($"AA wake skip {outpost.LabelCap} vs {LabelOf(target)}: out of AA range ({aaRange:F0})");
                        continue;
                    }
                    if (AntiAirFireUtils.TryQueueEngage(outpost, target))
                        queued++;
                    else
                        TryArmMortarShellAa(outpost, target);
                    continue;
                }

                if (ip.Self is Settlement settlement)
                {
                    var comp = settlement.GetComponent<CompViralSpread>();
                    if (comp == null || !comp.IsSettlementAntiAirAutoActive) continue;
                    if (!CompViralSpread.IsSettlementAntiAirEligible(settlement)) continue;
                    considered++;
                    float aaRange = AntiAirFireUtils.GetNpcAntiAirMaxRangeTiles();
                    if (!AntiAirFireUtils.IsAirborneInAaRange(settlement, target, aaRange, manager))
                    {
                        if (logSkips)
                        {
                            float cur = target.Tile.tileId >= 0
                                ? (manager != null
                                    ? WorldActions_Utils.GetDistance(settlement.Tile.tileId, target.Tile.tileId, manager)
                                    : Find.WorldGrid.ApproxDistanceInTiles(settlement.Tile.tileId, target.Tile.tileId))
                                : -1f;
                            int destId = -1;
                            float progDist = -1f;
                            bool ballistic = false;
                            if (target is WorldObject_Traveler tt)
                            {
                                ballistic = WD_PathFollower.IsBallisticWorldFlight(tt);
                                if (tt.pather != null && tt.pather.destTile.Valid)
                                    destId = tt.pather.destTile.tileId;
                                // Live hop progress (Tile stays at launch for ballistic).
                                if (ballistic && tt.pather != null && tt.pather.moving && tt.pather.nextTile.Valid)
                                {
                                    WorldGrid grid = Find.WorldGrid;
                                    Vector3 aaPos = grid.GetTileCenter(settlement.Tile.tileId);
                                    Vector3 from = grid.GetTileCenter(tt.Tile.tileId);
                                    Vector3 to = grid.GetTileCenter(tt.pather.nextTile.tileId);
                                    float total = Mathf.Max(0.001f, tt.pather.nextTileCostTotal);
                                    float u = Mathf.Clamp01(1f - Mathf.Max(0f, tt.pather.nextTileCostLeft) / total);
                                    Vector3 p = Vector3.Slerp(from, to, u);
                                    float cos = Mathf.Clamp(Vector3.Dot(aaPos.normalized, p.normalized), -1f, 1f);
                                    progDist = grid.ApproxDistanceInTiles(Mathf.Acos(cos));
                                }
                            }
                            else if (target is TravellingTransporters pods)
                            {
                                ballistic = true;
                                destId = pods.destinationTile.Valid ? pods.destinationTile.tileId : -1;
                                Vector3 draw = pods.DrawPos;
                                if (draw.sqrMagnitude > 0.0001f)
                                {
                                    WorldGrid grid = Find.WorldGrid;
                                    Vector3 aaPos = grid.GetTileCenter(settlement.Tile.tileId);
                                    float cos = Mathf.Clamp(Vector3.Dot(aaPos.normalized, draw.normalized), -1f, 1f);
                                    progDist = grid.ApproxDistanceInTiles(Mathf.Acos(cos));
                                }
                            }
                            float destDist = destId >= 0
                                ? (manager != null
                                    ? WorldActions_Utils.GetDistance(settlement.Tile.tileId, destId, manager)
                                    : Find.WorldGrid.ApproxDistanceInTiles(settlement.Tile.tileId, destId))
                                : -1f;
                            WDVerbose.Msg($"AA wake skip {settlement.LabelCap} vs {LabelOf(target)}: out of AA range ({aaRange:F0}); tileDist={cur:F1} (launch tile) progDist={progDist:F1} destDist={destDist:F1} destTile={destId} ballistic={ballistic} inbound={inbound} canTargetPlayer={ip.InterceptorCanTargetPlayer}");
                        }
                        continue;
                    }
                    if (AntiAirFireUtils.TryEngageFromSettlement(settlement, target))
                        queued++;
                    else
                    {
                        TryArmMortarShellAa(settlement, target);
                        if (logSkips)
                            WDVerbose.Msg($"AA wake {settlement.LabelCap} vs {LabelOf(target)}: in range but TryQueueEngage failed (cooldown/classify/ready)");
                    }
                }
            }

            if (logSkips || queued > 0)
                WDVerbose.Msg($"AA {(logSkips ? "wake" : "pulse")} {LabelOf(target)} fac={tf.Name} interceptors={interceptors.Count} aaCandidates={considered} queued={queued}");
        }

        /// <summary>Breaks the AA-vs-player gate into its sub-conditions so a false result names its own cause.</summary>
        private static string DescribePlayerGate(IDefensiveInterceptor ip)
        {
            if (ip is CompViralSpread comp)
            {
                // An interceptor left over from a previous world would answer the gate while the live settlement
                // of the same name has a different comp; both facts are needed to tell those cases apart.
                var liveComp = (ip.Self as Settlement)?.GetComponent<CompViralSpread>();
                bool ipIsLiveComp = ReferenceEquals(comp, liveComp);
                bool selfInWorld = ip.Self != null && Find.WorldObjects != null
                    && Find.WorldObjects.AllWorldObjects.Contains(ip.Self);
                return comp.DebugDescribePlayerGate()
                    + $" ipIsLiveComp={ipIsLiveComp} selfInWorld={selfInWorld}";
            }
            if (ip?.Self is Settlement settlement)
                return $"interceptor is not CompViralSpread (type {ip.GetType().Name}); settlement {settlement.LabelCap}";
            if (ip?.Self is WorldObject_WD_Outpost)
                return "player outpost interceptor (never targets player)";
            return $"interceptor type {ip?.Self?.GetType().Name ?? "null"}";
        }

        private static string LabelOf(WorldObject wo)
        {
            if (wo == null) return "?";
            if (wo is WorldObject_Traveler t)
                return $"{t.LabelCap}[{t.mission}]#{t.ID}";
            return $"{wo.LabelCap}#{wo.ID}";
        }

        private int GetScanIntervalTicks()
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            if (cachedIntervalTicks > 0 && now - cachedIntervalTickLastRefresh < IntervalRefreshCadence)
                return cachedIntervalTicks;
            var seth = WorldDominationMod.settings;
            int v = seth != null ? seth.interceptionScanIntervalTicks : WorldDominationSettings.DefInterceptionScanIntervalTicks;
            if (v < 60) v = 60;
            cachedIntervalTicks = v;
            cachedIntervalTickLastRefresh = now;
            return v;
        }

        private static bool IsNpcT4Interceptor(IDefensiveInterceptor ip)
        {
            if (!(ip?.Self is Settlement settlement)) return false;
            if (settlement.Faction == null || settlement.Faction.IsPlayer) return false;
            var comp = settlement.GetComponent<CompViralSpread>();
            return comp != null && comp.tier == SettlementTier.T4;
        }

        private void PartitionInterceptors()
        {
            playerScratch.Clear();
            npcT4Scratch.Clear();
            for (int i = 0; i < interceptors.Count; i++)
            {
                var ip = interceptors[i];
                if (ip == null) continue;
                if (IsNpcT4Interceptor(ip)) npcT4Scratch.Add(ip);
                else playerScratch.Add(ip);
            }
        }

        public override void WorldComponentTick()
        {
            AntiAirFireUtils.TickPending();
            ProcessPendingExternalAirborneWakes();
            ProcessPendingAtCooldownWakes();
            ProcessArmedExternalAirborneAa();

            // Stripe still needed for AT caravan scans and T4 InterceptorNoTargetFire when no WD travelers exist.
            if (interceptors.Count == 0) return;
            int interval = GetScanIntervalTicks();
            int npcInterval = interval * 3;
            int tick = Find.TickManager.TicksGame;
            int playerSlot = tick % interval;
            bool npcCycle = (tick % npcInterval) == 0;

            PartitionInterceptors();

            bool anyPlayerWork = playerSlot < playerScratch.Count;
            bool anyNpcWork = npcCycle && npcT4Scratch.Count > 0;
            if (!anyPlayerWork && !anyNpcWork)
            {
                if ((tick & 1023) == 0) PurgeDestroyed();
                return;
            }

            RebuildInboundTargetIds();
            spreadManagerScratch = Find.World?.GetComponent<WorldComponent_SpreadManager>();

            if (anyPlayerWork)
            {
                for (int i = playerSlot; i < playerScratch.Count; i += interval)
                {
                    var ip = playerScratch[i];
                    if (ip == null) continue;
                    try { ScanOne(ip); }
                    catch (System.Exception ex)
                    {
                        Log.Error("[TSA World Domination] Interception scan failed: " + ex);
                    }
                }
            }

            if (anyNpcWork)
            {
                int n = Mathf.Min(NpcT4PerCycle, npcT4Scratch.Count);
                for (int k = 0; k < n; k++)
                {
                    if (npcT4Cursor >= npcT4Scratch.Count) npcT4Cursor = 0;
                    var ip = npcT4Scratch[npcT4Cursor++];
                    if (ip == null) continue;
                    try { ScanOne(ip); }
                    catch (System.Exception ex)
                    {
                        Log.Error("[TSA World Domination] NPC T4 interception scan failed: " + ex);
                    }
                }
            }

            spreadManagerScratch = null;

            if ((tick & 1023) == 0)
                PurgeDestroyed();
        }

        private void RebuildInboundTargetIds()
        {
            inboundMortarTargetIdsScratch.Clear();
            inboundRapidResponseOriginTargetPairsScratch.Clear();
            foreach (WorldObject_Traveler shell in travelers)
            {
                if (shell == null || shell.Destroyed || !shell.Spawned) continue;
                WorldObject tgt = shell.targetObject;
                if (tgt == null || tgt.Destroyed) continue;
                if (shell.mission == TravelerMission.MortarStrike)
                    inboundMortarTargetIdsScratch.Add(tgt.ID);
                else if (shell.mission == TravelerMission.RapidResponseIntercept
                    && shell.originObject != null && !shell.originObject.Destroyed)
                {
                    inboundRapidResponseOriginTargetPairsScratch.Add(
                        MakePairKey(shell.originObject.ID, tgt.ID));
                }
            }
        }

        private void PurgeDestroyed()
        {
            if (travelers.Count > 0)
                travelers.RemoveWhere(t => t == null || t.Destroyed);
            PurgeDispatchedRapidResponsePairs();
            if (externalAirborne.Count > 0)
                externalAirborne.RemoveWhere(IsExternalAirborneStale);
            if (pendingExternalAirborneWakeTickById.Count > 0)
            {
                skipUntilRemoveScratch.Clear();
                foreach (var kv in pendingExternalAirborneWakeTickById)
                {
                    bool alive = false;
                    foreach (var pods in externalAirborne)
                    {
                        if (pods != null && !pods.Destroyed && pods.ID == kv.Key)
                        {
                            alive = true;
                            break;
                        }
                    }
                    if (!alive)
                        skipUntilRemoveScratch.Add(kv.Key);
                }
                for (int i = 0; i < skipUntilRemoveScratch.Count; i++)
                    pendingExternalAirborneWakeTickById.Remove((int)skipUntilRemoveScratch[i]);
            }
            if (armedExternalAirborneAaPairs.Count > 0)
                PurgeArmedExternalAirborneAaPairs();
            for (int i = interceptors.Count - 1; i >= 0; i--)
            {
                var ip = interceptors[i];
                if (ip == null || ip.Self == null || ip.Self.Destroyed)
                {
                    if (ip?.Self != null)
                        pendingAtCooldownWakeTickById.Remove(ip.Self.ID);
                    ClearSkipUntilForInterceptor(ip);
                    ClearArmedExternalAirborneAaForInterceptor(ip);
                    interceptors.RemoveAt(i);
                }
            }
        }

        private void PurgeDispatchedRapidResponsePairs()
        {
            if (dispatchedRapidResponsePairs.Count == 0) return;
            skipUntilRemoveScratch.Clear();
            foreach (long key in dispatchedRapidResponsePairs)
            {
                int targetId = (int)(key & 0xFFFFFFFF);
                bool alive = false;
                foreach (var t in travelers)
                {
                    if (t != null && !t.Destroyed && t.ID == targetId)
                    { alive = true; break; }
                }
                if (!alive) skipUntilRemoveScratch.Add(key);
            }
            for (int i = 0; i < skipUntilRemoveScratch.Count; i++)
                dispatchedRapidResponsePairs.Remove(skipUntilRemoveScratch[i]);
        }

        private static long MakePairKey(int interceptorId, int travelerId)
        {
            unchecked
            {
                return ((long)interceptorId << 32) ^ (uint)travelerId;
            }
        }

        private static bool IsExternalAirborneStale(WorldObject wo)
        {
            if (wo == null || wo.Destroyed) return true;
            if (wo is TravellingTransporters pods) return !pods.Spawned;
            if (VehicleFrameworkAerialAaCompat.IsAerialVehicleInFlight(wo))
                return !VehicleFrameworkAerialAaCompat.IsFlying(wo);
            if (wo is WorldObject_Traveler t && AntiAirFireUtils.IsHostileMortarShell(t))
                return !t.Spawned;
            return true;
        }

        private void ProcessPendingExternalAirborneWakes()
        {
            if (pendingExternalAirborneWakeTickById.Count == 0) return;
            int now = Find.TickManager?.TicksGame ?? 0;
            skipUntilRemoveScratch.Clear();
            foreach (var kv in pendingExternalAirborneWakeTickById)
            {
                if (now < kv.Value) continue;
                skipUntilRemoveScratch.Add(kv.Key);
            }

            for (int i = 0; i < skipUntilRemoveScratch.Count; i++)
            {
                int id = (int)skipUntilRemoveScratch[i];
                pendingExternalAirborneWakeTickById.Remove(id);
                WorldObject target = FindExternalAirborneById(id);
                if (target != null && !IsExternalAirborneStale(target))
                    ArmExternalAirborneAaAlongFlight(target);
            }
        }

        /// <summary>
        /// At launch: arm every AA-capable hostile shooter whose bubble the flight will touch.
        /// If already in DrawPos range, engage immediately and leave that shooter asleep for this target.
        /// </summary>
        private void ArmExternalAirborneAaAlongFlight(WorldObject target)
        {
            if (target == null || IsExternalAirborneStale(target)) return;
            Faction tf = target.Faction;
            if (tf == null)
            {
                WDVerbose.Msg($"AA arm skip: {LabelOf(target)} has no faction");
                return;
            }

            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            int armed = 0;
            int firedNow = 0;
            for (int i = 0; i < interceptors.Count; i++)
            {
                var ip = interceptors[i];
                if (ip?.Self == null || ip.Self.Destroyed) continue;
                if (!TryGetAaShooter(ip, out WorldObject shooter, out float aaRange)) continue;

                Faction iFaction = ip.InterceptorFaction;
                if (iFaction == null || !WorldActions_Utils.SafeHostileTo(tf, iFaction)) continue;

                bool inbound = AntiAirFireUtils.IsInboundThreatTo(shooter, target);
                if (tf.IsPlayer && !ip.InterceptorCanTargetPlayer && !inbound
                    && !AntiAirFireUtils.IsHostileMortarShell(target))
                    continue;

                bool shouldWatch;
                if (target is TravellingTransporters pods)
                    shouldWatch = AntiAirFireUtils.WillVanillaPodFlightEnterAaRange(shooter, pods, aaRange, manager);
                else if (VehicleFrameworkAerialAaCompat.IsAerialVehicleInFlight(target))
                {
                    shouldWatch = AntiAirFireUtils.WillExternalAirborneFlightEnterAaRange(shooter, target, aaRange, manager);
                    // No VF path / arc unavailable: still arm for per-second DrawPos polling.
                    if (!shouldWatch)
                        shouldWatch = true;
                }
                else
                    continue;

                if (!shouldWatch) continue;

                // Already inside the bubble: fire now, do not keep armed.
                if (AntiAirFireUtils.IsAirborneInAaRange(shooter, target, aaRange, manager))
                {
                    if (AntiAirFireUtils.TryQueueEngage(shooter, target))
                        firedNow++;
                    continue;
                }

                long key = MakePairKey(shooter.ID, target.ID);
                if (armedExternalAirborneAaPairs.Add(key))
                    armed++;
            }

            WDVerbose.Msg($"AA arm {LabelOf(target)} fac={tf.Name} armed={armed} firedNow={firedNow} interceptors={interceptors.Count}");
        }

        /// <summary>Every second: DrawPos check for armed pairs; queue once when in range, then sleep.</summary>
        private void ProcessArmedExternalAirborneAa()
        {
            if (armedExternalAirborneAaPairs.Count == 0) return;
            int tick = Find.TickManager?.TicksGame ?? 0;
            if ((tick % ExternalAirborneArmedCheckIntervalTicks) != 0) return;

            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            skipUntilRemoveScratch.Clear();

            foreach (long key in armedExternalAirborneAaPairs)
            {
                int shooterId = (int)(key >> 32);
                int targetId = (int)(key & 0xFFFFFFFF);

                WorldObject target = FindExternalAirborneById(targetId);
                if (target == null || IsExternalAirborneStale(target))
                {
                    skipUntilRemoveScratch.Add(key);
                    continue;
                }

                WorldObject shooter = FindAaShooterById(shooterId, out float aaRange);
                if (shooter == null || aaRange <= 0f)
                {
                    skipUntilRemoveScratch.Add(key);
                    continue;
                }

                Faction tf = target.Faction;
                Faction sf = shooter.Faction;
                if (tf == null || sf == null || !WorldActions_Utils.SafeHostileTo(tf, sf))
                {
                    skipUntilRemoveScratch.Add(key);
                    continue;
                }

                if (!AntiAirFireUtils.IsAirborneInAaRange(shooter, target, aaRange, manager))
                    continue;

                // In range: fire once, then sleep. Stay armed if cooldown/ready blocked so the next second can retry.
                if (AntiAirFireUtils.TryQueueEngage(shooter, target))
                {
                    skipUntilRemoveScratch.Add(key);
                    WDVerbose.Msg($"AA armed-fire {shooter.LabelCap} -> {LabelOf(target)} (then sleep)");
                }
            }

            for (int i = 0; i < skipUntilRemoveScratch.Count; i++)
                armedExternalAirborneAaPairs.Remove(skipUntilRemoveScratch[i]);
        }

        private WorldObject FindExternalAirborneById(int id)
        {
            foreach (var pods in externalAirborne)
            {
                if (pods != null && !pods.Destroyed && pods.ID == id)
                    return pods;
            }
            if (armedMortarShellsById.TryGetValue(id, out WorldObject_Traveler shell)
                && shell != null && !shell.Destroyed)
                return shell;
            return null;
        }

        private WorldObject FindAaShooterById(int id, out float aaRange)
        {
            aaRange = 0f;
            for (int i = 0; i < interceptors.Count; i++)
            {
                var ip = interceptors[i];
                if (ip?.Self == null || ip.Self.Destroyed || ip.Self.ID != id) continue;
                if (!TryGetAaShooter(ip, out WorldObject shooter, out aaRange)) return null;
                return shooter;
            }
            return null;
        }

        private static bool TryGetAaShooter(IDefensiveInterceptor ip, out WorldObject shooter, out float aaRange)
        {
            shooter = null;
            aaRange = 0f;
            if (ip?.Self == null) return false;

            if (ip.Self is WorldObject_WD_Outpost outpost)
            {
                if (!outpost.IsMortarOutpost || !outpost.AntiAirDefenseActive) return false;
                if (!AntiAirFireUtils.HasAntiAirUpgrade(outpost)) return false;
                shooter = outpost;
                aaRange = AntiAirFireUtils.GetPlayerAntiAirMaxRangeTiles(outpost);
                return aaRange > 0f;
            }

            if (ip.Self is Settlement settlement)
            {
                var comp = settlement.GetComponent<CompViralSpread>();
                if (comp == null || !comp.IsSettlementAntiAirAutoActive) return false;
                if (!CompViralSpread.IsSettlementAntiAirEligible(settlement)) return false;
                shooter = settlement;
                aaRange = AntiAirFireUtils.GetNpcAntiAirMaxRangeTiles();
                return aaRange > 0f;
            }

            // CompViralSpread may be the interceptor while Self is the settlement.
            if (ip is CompViralSpread viral && viral.parent is Settlement s2)
            {
                if (!viral.IsSettlementAntiAirAutoActive) return false;
                if (!CompViralSpread.IsSettlementAntiAirEligible(s2)) return false;
                shooter = s2;
                aaRange = AntiAirFireUtils.GetNpcAntiAirMaxRangeTiles();
                return aaRange > 0f;
            }

            return false;
        }

        private void ClearArmedExternalAirborneAaForInterceptor(IDefensiveInterceptor ip)
        {
            if (ip?.Self == null || armedExternalAirborneAaPairs.Count == 0) return;
            int id = ip.Self.ID;
            skipUntilRemoveScratch.Clear();
            foreach (long key in armedExternalAirborneAaPairs)
            {
                if ((int)(key >> 32) == id)
                    skipUntilRemoveScratch.Add(key);
            }
            for (int i = 0; i < skipUntilRemoveScratch.Count; i++)
                armedExternalAirborneAaPairs.Remove(skipUntilRemoveScratch[i]);
        }

        private void ClearArmedExternalAirborneAaForTarget(WorldObject target)
        {
            if (target == null || armedExternalAirborneAaPairs.Count == 0) return;
            int id = target.ID;
            skipUntilRemoveScratch.Clear();
            foreach (long key in armedExternalAirborneAaPairs)
            {
                if ((int)(key & 0xFFFFFFFF) == id)
                    skipUntilRemoveScratch.Add(key);
            }
            for (int i = 0; i < skipUntilRemoveScratch.Count; i++)
                armedExternalAirborneAaPairs.Remove(skipUntilRemoveScratch[i]);
        }

        /// <summary>
        /// 1Hz retry when spawn-wake <see cref="AntiAirFireUtils.TryQueueEngage"/> failed (cooldown / NPC stagger).
        /// Outpost and settlement AA branches only; AT guns never call this.
        /// </summary>
        private void TryArmMortarShellAa(WorldObject shooter, WorldObject target)
        {
            if (shooter == null || shooter.Destroyed) return;
            if (target is not WorldObject_Traveler shell || shell.Destroyed) return;
            if (!AntiAirFireUtils.IsHostileMortarShell(shell)) return;
            armedMortarShellsById[shell.ID] = shell;
            armedExternalAirborneAaPairs.Add(MakePairKey(shooter.ID, shell.ID));
        }

        private void PurgeArmedExternalAirborneAaPairs()
        {
            skipUntilRemoveScratch.Clear();
            foreach (long key in armedExternalAirborneAaPairs)
            {
                int podId = (int)(key & 0xFFFFFFFF);
                if (FindExternalAirborneById(podId) == null)
                    skipUntilRemoveScratch.Add(key);
            }
            for (int i = 0; i < skipUntilRemoveScratch.Count; i++)
            {
                long key = skipUntilRemoveScratch[i];
                armedExternalAirborneAaPairs.Remove(key);
                armedMortarShellsById.Remove((int)(key & 0xFFFFFFFF));
            }
        }

        private readonly List<long> skipUntilRemoveScratch = new List<long>(16);

        private void ClearSkipUntilForInterceptor(IDefensiveInterceptor ip)
        {
            if (ip?.Self == null || skipUntilByPair.Count == 0) return;
            int id = ip.Self.ID;
            skipUntilRemoveScratch.Clear();
            foreach (var kv in skipUntilByPair)
            {
                if ((int)(kv.Key >> 32) == id)
                    skipUntilRemoveScratch.Add(kv.Key);
            }
            for (int i = 0; i < skipUntilRemoveScratch.Count; i++)
                skipUntilByPair.Remove(skipUntilRemoveScratch[i]);
        }

        private void ClearSkipUntilForTraveler(WorldObject_Traveler t)
        {
            if (t == null || skipUntilByPair.Count == 0) return;
            int id = t.ID;
            skipUntilRemoveScratch.Clear();
            foreach (var kv in skipUntilByPair)
            {
                if ((int)(kv.Key & 0xFFFFFFFF) == id)
                    skipUntilRemoveScratch.Add(kv.Key);
            }
            for (int i = 0; i < skipUntilRemoveScratch.Count; i++)
                skipUntilByPair.Remove(skipUntilRemoveScratch[i]);
        }

        private static int ComputeSkipUntilTick(int now, float dist, float range, WorldObject_Traveler t)
        {
            float overshoot = dist - range;
            if (overshoot <= 0f) return now;
            float maxClose = WD_PathFollower.IsBallisticWorldFlight(t)
                ? MaxCloseTilesPerTickBallistic
                : MaxCloseTilesPerTickGround;
            int delay = Mathf.Max(1, Mathf.CeilToInt((overshoot / maxClose) * SkipUntilSafetyMargin));
            return now + delay;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            // Legacy key: cleared on load so old exclusive RR locks do not persist.
            List<int> dispatchedList = null;
            Scribe_Collections.Look(ref dispatchedList, "dispatchedRapidResponseTargetIds", LookMode.Value);

            List<long> pairsList = null;
            if (Scribe.mode == LoadSaveMode.Saving && dispatchedRapidResponsePairs.Count > 0)
            {
                pairsList = new List<long>(dispatchedRapidResponsePairs);
            }
            Scribe_Collections.Look(ref pairsList, "dispatchedRapidResponsePairs", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                dispatchedRapidResponsePairs.Clear();
                if (pairsList != null)
                {
                    for (int i = 0; i < pairsList.Count; i++)
                        dispatchedRapidResponsePairs.Add(pairsList[i]);
                }
            }
        }

        /// <summary>
        /// Feature D: sole auto-fire path for ground (non-ballistic) WD travelers. Called from
        /// <see cref="WD_PathFollower"/> tile-exit via <see cref="WorldComponent_SettlementWatchIndex"/>
        /// <see cref="WatchCapability.Interceptor"/> lookup. Re-validates range / dispatch-lock / mission-mask
        /// before <see cref="IDefensiveInterceptor.InterceptorFire"/>. Does not handle airborne/ballistic AA
        /// (<see cref="WD_PathFollower"/> <c>!ballistic</c> guard). Mid-hop gun spawn / CD expiry retry on the
        /// next tile exit. AT idle vs other ATs uses <see cref="TryEngageAtTurretTargets"/> event wakes instead.
        /// </summary>
        public void TryEventDrivenGroundIntercept(WorldObject_Traveler traveler, int tileId)
        {
            if (traveler == null || traveler.Destroyed || tileId < 0) return;
            if (traveler.mission == TravelerMission.MortarStrike || traveler.mission == TravelerMission.AntiAirStrike) return;
            if (AntiAirFireUtils.IsAirborneAaTarget(traveler)) return;
            Faction tf = traveler.Faction;
            if (tf == null || interceptors.Count == 0) return;

            var watchIndex = WorldComponent_SettlementWatchIndex.Get();
            if (watchIndex == null) return;
            List<WorldObject> watchers = watchIndex.GetWatchers(tileId, WatchCapability.Interceptor);
            if (watchers.Count == 0) return;

            RebuildInboundTargetIds();
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            for (int i = 0; i < watchers.Count; i++)
            {
                IDefensiveInterceptor ip = FindInterceptorForSelf(watchers[i]);
                if (ip == null) continue;
                TryFireGroundInterceptAt(ip, traveler, tf, manager);
                if (traveler.Destroyed) return;
            }
        }

        private IDefensiveInterceptor FindInterceptorForSelf(WorldObject self)
        {
            if (self == null) return null;
            for (int i = 0; i < interceptors.Count; i++)
            {
                var ip = interceptors[i];
                if (ip?.Self == self) return ip;
            }
            return null;
        }

        /// <summary>Single-target ground-fire evaluation for the tile-exit path (same gates as the former ScanOne ground-raider branch).</summary>
        private void TryFireGroundInterceptAt(IDefensiveInterceptor ip, WorldObject_Traveler t, Faction tf, WorldComponent_SpreadManager manager)
        {
            var self = ip.Self;
            if (self == null || self.Destroyed) return;
            if (!ip.InterceptorCanFireNow()) return;

            Faction iFaction = ip.InterceptorFaction;
            if (iFaction == null || tf == iFaction || !WorldActions_Utils.SafeHostileTo(tf, iFaction)) return;

            PlanetTile iTile = ip.InterceptorTile;
            if (iTile.tileId < 0) return;
            float range = ip.InterceptorRange;
            if (range <= 0f) return;

            MissionMask mask = ip.InterceptorMissionMask;
            if (mask == MissionMask.None) return;

            if (tf.IsPlayer)
            {
                if (self is WorldObject_AT_Turret atTurret)
                {
                    if (!AtTurretUtility.CanAutoTargetPlayerTraveler(atTurret, t)) return;
                }
                else
                {
                    if (!ip.InterceptorCanTargetPlayer) return;
                    if (!InterceptionMissionMaskUtils.Matches(t.mission, mask)) return;
                }
            }
            else if (!InterceptionMissionMaskUtils.Matches(t.mission, mask))
            {
                return;
            }

            bool isRapidResponse = false;
            WorldObject_WD_Outpost defenseOutpost = null;
            if (self is WorldObject_WD_Outpost wdOutpost)
            {
                if (wdOutpost.IsRapidResponseOutpost) { isRapidResponse = true; defenseOutpost = wdOutpost; }
                else if (wdOutpost.IsMortarOutpost) defenseOutpost = wdOutpost;
            }
            if (defenseOutpost != null)
            {
                RaidTargetMask raidMask = isRapidResponse
                    ? defenseOutpost.RapidResponseRaidTargetMask
                    : defenseOutpost.MortarRaidTargetMask;
                if (!RapidResponseUtility.IsEligibleAutoInterceptTarget(t, raidMask)) return;
            }
            if (isRapidResponse && defenseOutpost != null)
            {
                long rrKey = MakePairKey(defenseOutpost.ID, t.ID);
                if (dispatchedRapidResponsePairs.Contains(rrKey) || inboundRapidResponseOriginTargetPairsScratch.Contains(rrKey))
                    return;
            }
            if (!isRapidResponse && inboundMortarTargetIdsScratch.Contains(t.ID)) return;

            int tTileId = t.Tile.tileId;
            if (tTileId < 0) return;
            float dist = manager != null
                ? (float)WorldActions_Utils.GetDistance(iTile.tileId, tTileId, manager)
                : Find.WorldGrid.ApproxDistanceInTiles(iTile.tileId, tTileId);
            if (dist > range) return;

            long pairKey = MakePairKey(self.ID, t.ID);
            skipUntilByPair.Remove(pairKey);
            ip.InterceptorFire(t, dist);
        }

        private void ScanOne(IDefensiveInterceptor ip)
        {
            var self = ip.Self;
            if (self == null || self.Destroyed) return;
            if (!ip.InterceptorCanFireNow()) return;

            Faction iFaction = ip.InterceptorFaction;
            if (iFaction == null) return;

            PlanetTile iTile = ip.InterceptorTile;
            if (iTile.tileId < 0) return;

            float range = ip.InterceptorRange;
            if (range <= 0f) return;

            MissionMask mask = ip.InterceptorMissionMask;
            if (mask == MissionMask.None) return;

            var manager = spreadManagerScratch ?? Find.World?.GetComponent<WorldComponent_SpreadManager>();
            int now = Find.TickManager.TicksGame;
            int interceptorId = self.ID;

            // Ground WD travelers: tile-exit only (TryEventDrivenGroundIntercept). Stripe keeps AA airborne
            // travelers, external airborne, AT caravans, and InterceptorNoTargetFire.
            foreach (var t in travelers)
            {
                if (t == null || t.Destroyed) continue;
                if (t.mission == TravelerMission.MortarStrike || t.mission == TravelerMission.AntiAirStrike) continue;

                Faction tf = t.Faction;
                if (tf == null) continue;
                if (tf == iFaction) continue;
                if (!WorldActions_Utils.SafeHostileTo(tf, iFaction)) continue;

                // Airborne AA targets: event wake + mid-flight re-scan via dest/arc range (Tile stays at origin).
                if (!AntiAirFireUtils.IsAirborneAaTarget(t)) continue;

                bool inbound = AntiAirFireUtils.IsInboundThreatTo(self, t);
                if (tf.IsPlayer && !ip.InterceptorCanTargetPlayer && !inbound
                    && !AntiAirFireUtils.IsHostileMortarShell(t))
                    continue;
                float aaRange = AntiAirFireUtils.GetAntiAirMaxRangeForOrigin(self);
                if (aaRange <= 0f || !AntiAirFireUtils.IsAirborneInAaRange(self, t, aaRange, manager))
                    continue;
                if (AntiAirFireUtils.TryQueueEngage(self, t))
                    WDVerbose.Msg($"AA scan-queue {self.LabelCap} -> {t.LabelCap}[{t.mission}] inbound={inbound}");
            }

            // Vanilla transport pods and VF aerials tracked as external airborne AA targets.
            foreach (var target in externalAirborne)
            {
                if (target == null || IsExternalAirborneStale(target)) continue;

                Faction tf = target.Faction;
                if (tf == null) continue;
                if (tf == iFaction) continue;
                if (!WorldActions_Utils.SafeHostileTo(tf, iFaction)) continue;

                bool inbound = AntiAirFireUtils.IsInboundThreatTo(self, target);
                if (tf.IsPlayer && !ip.InterceptorCanTargetPlayer && !inbound
                    && !AntiAirFireUtils.IsHostileMortarShell(target))
                    continue;

                float aaRange = AntiAirFireUtils.GetAntiAirMaxRangeForOrigin(self);
                if (aaRange <= 0f || !AntiAirFireUtils.IsAirborneInAaRange(self, target, aaRange, manager))
                    continue;

                if (AntiAirFireUtils.TryQueueEngage(self, target))
                    WDVerbose.Msg($"AA scan-queue {self.LabelCap} -> {LabelOf(target)} inbound={inbound}");
            }

            // AT caravan path: independent of InterceptorCanTargetPlayer / traveler flag.
            if (self is WorldObject_AT_Turret atGun
                && AtTurretUtility.IsPlayerCaravanTargetingEnabled())
            {
                Caravan bestCaravan = null;
                float bestCaravanDist = float.MaxValue;
                List<Caravan> caravans = Find.WorldObjects?.Caravans;
                if (caravans != null)
                {
                    for (int i = 0; i < caravans.Count; i++)
                    {
                        Caravan c = caravans[i];
                        if (!AtTurretUtility.CanAutoTargetPlayerCaravan(atGun, c)) continue;
                        if (inboundMortarTargetIdsScratch.Contains(c.ID)) continue;

                        long pairKey = MakePairKey(interceptorId, c.ID);
                        if (skipUntilByPair.TryGetValue(pairKey, out int until) && now < until)
                            continue;

                        int cTileId = c.Tile.tileId;
                        if (cTileId < 0) continue;

                        float dist = manager != null
                            ? (float)WorldActions_Utils.GetDistance(iTile.tileId, cTileId, manager)
                            : Find.WorldGrid.ApproxDistanceInTiles(iTile.tileId, cTileId);
                        if (dist > range)
                        {
                            skipUntilByPair[pairKey] = now + GenDate.TicksPerHour;
                            continue;
                        }

                        skipUntilByPair.Remove(pairKey);
                        if (dist < bestCaravanDist)
                        {
                            bestCaravanDist = dist;
                            bestCaravan = c;
                        }
                    }
                }

                if (bestCaravan != null)
                {
                    atGun.InterceptorFireAtCaravan(bestCaravan, bestCaravanDist);
                    return;
                }
            }

            ip.InterceptorNoTargetFire();
        }
    }
}
