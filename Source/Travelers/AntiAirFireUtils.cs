using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Player / T4 anti-air: event-driven engage vs hostile drop pods, mortar shells, and vanilla transport pods.
    /// Reaction delay before first shell; player letter groups A–D claim targets; T4 may stack.
    /// </summary>
    public static class AntiAirFireUtils
    {
        public const string AntiAirUpgradeDefName = "TSA_WD_Upgrade_AntiAirGun";

        public const int VolleyMin = 3;
        public const int VolleyMax = 5;
        public const int ReactionDelayTicks = 30;
        /// <summary>When a WD ballistic target has no path hop yet, re-check every N ticks.</summary>
        private const int HopDeferStepTicks = 5;
        /// <summary>Max extra wait after the original engage time before aborting (never fire at launch origin).</summary>
        private const int HopWaitCapTicks = 45;
        private const int ClaimFailsafeTicks = 3600;

        private const float MeetJitterTilesMin = 0.2f;
        private const float MeetJitterTilesMax = 0.55f;

        private struct PendingFlak
        {
            public int spawnAtTick;
            public WorldObject origin;
            public WorldObject target;
            public float damage;
            public bool hit;
            public bool isResolver;
            public AntiAirTargetKind kind;
        }

        private struct PendingEngage
        {
            public int engageAtTick;
            /// <summary>First scheduled engage tick + <see cref="HopWaitCapTicks"/>; hop waits abort after this.</summary>
            public int hopWaitDeadlineTick;
            public WorldObject origin;
            public WorldObject target;
            public AntiAirTargetKind kind;
            public bool isPlayerOutpost;
        }

        public enum AntiAirTargetKind : byte
        {
            RaidDropPod = 0,
            MortarStrike = 1,
            VanillaTransportPods = 2,
            VehicleFrameworkAerial = 3,
        }

        private static readonly List<PendingFlak> pendingFlak = new List<PendingFlak>(16);
        private static readonly List<PendingEngage> pendingEngage = new List<PendingEngage>(16);
        private static readonly Dictionary<int, AntiAirGroupLetter> claimLetterByTargetId = new Dictionary<int, AntiAirGroupLetter>(32);
        private static readonly Dictionary<int, int> claimExpireTickByTargetId = new Dictionary<int, int>(32);
        private static readonly HashSet<long> pendingEngageKeys = new HashSet<long>();

        public static bool HasAntiAirUpgrade(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return false;
            if (outpost.GetUpgradeLevel(AntiAirUpgradeDefName) > 0) return true;
            return outpost.HasBuiltAntiAirUnlock();
        }

        public static bool AllowsAntiAirKind(WorldObject_WD_Outpost outpost, AntiAirTargetKind kind)
        {
            if (outpost == null) return true;
            AntiAirKindMask mask = outpost.AntiAirTargetKinds;
            switch (kind)
            {
                case AntiAirTargetKind.MortarStrike:
                    return (mask & AntiAirKindMask.MortarShells) != 0;
                case AntiAirTargetKind.RaidDropPod:
                case AntiAirTargetKind.VanillaTransportPods:
                case AntiAirTargetKind.VehicleFrameworkAerial:
                    return (mask & AntiAirKindMask.DropPods) != 0;
                default:
                    return false;
            }
        }

        public static float GetPlayerAntiAirConfiguredMaxRangeTiles(WorldObject_WD_Outpost origin = null)
        {
            var seth = WorldDominationMod.settings;
            float r = Mathf.Max(1f, seth?.antiAirRange ?? WorldDominationSettings.DefAntiAirRange);
            float expert = OutpostExpertUtility.GetStrategistAttackRangeBonusFraction(origin);
            if (expert > 0f)
                r *= 1f + expert;
            return r;
        }

        /// <summary>Effective AA range (respects per-outpost shrink override).</summary>
        public static float GetPlayerAntiAirMaxRangeTiles(WorldObject_WD_Outpost origin)
        {
            float max = GetPlayerAntiAirConfiguredMaxRangeTiles(origin);
            if (origin == null) return max;
            float ov = origin.AntiAirRangeOverride;
            if (ov < 0f) return max;
            float min = Mathf.Min(Dialog_OutpostRangeAdjust.MinTiles, max);
            return Mathf.Clamp(ov, min, max);
        }

        public static float GetNpcAntiAirMaxRangeTiles()
        {
            var seth = WorldDominationMod.settings;
            return Mathf.Max(1f, seth?.npcAntiAirRange ?? WorldDominationSettings.DefNpcAntiAirRange);
        }

        /// <summary>
        /// Hostile T4 settlements with flak that threaten a player drop-pod flight (origin, destination,
        /// or ballistic arc within NPC AA range). Nearest to <paramref name="originTile"/> first.
        /// </summary>
        public static bool TryGetHostileSettlementAaThreatsForDropPodFlight(
            int originTile,
            int destTile,
            List<Settlement> into)
        {
            into?.Clear();
            if (into == null || originTile < 0 || destTile < 0) return false;
            if (!(WorldDominationMod.settings?.enableNpcT4AntiAir ?? WorldDominationSettings.DefEnableNpcT4AntiAir))
                return false;

            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (!WdEscalation.CanTargetPlayerWithT4AntiAir(
                    WorldDominationMod.settings,
                    WdEscalation.GetCachedStage(manager)))
                return false;

            float aaRange = GetNpcAntiAirMaxRangeTiles();
            var settlements = Find.WorldObjects?.Settlements;
            if (settlements == null) return false;

            var scored = new List<(Settlement s, float dist)>();
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement settlement = settlements[i];
                if (settlement == null || settlement.Destroyed || settlement.Tile < 0) continue;
                if (settlement.Faction == null || settlement.Faction.IsPlayer) continue;
                if (!WorldActions_Utils.SafeHostileTo(settlement.Faction, Faction.OfPlayer)) continue;

                CompViralSpread comp = settlement.GetComponent<CompViralSpread>();
                if (comp == null || !comp.IsSettlementAntiAirEligible() || !comp.IsSettlementAntiAirAutoActive)
                    continue;

                int aaTile = settlement.Tile.tileId;
                bool threatens = TileWithin(manager, aaTile, originTile, aaRange)
                    || TileWithin(manager, aaTile, destTile, aaRange)
                    || BallisticArcComesWithinRange(aaTile, originTile, destTile, aaRange);
                if (!threatens) continue;

                float dist = manager != null
                    ? WorldActions_Utils.GetDistance(originTile, aaTile, manager)
                    : Find.WorldGrid.ApproxDistanceInTiles(originTile, aaTile);
                scored.Add((settlement, dist));
            }

            if (scored.Count == 0) return false;
            scored.Sort((a, b) => a.dist.CompareTo(b.dist));
            for (int i = 0; i < scored.Count; i++)
                into.Add(scored[i].s);
            return true;
        }

        public static float GetAntiAirMaxRangeForOrigin(WorldObject origin)
        {
            if (origin is WorldObject_WD_Outpost op && op.IsMortarOutpost)
                return GetPlayerAntiAirMaxRangeTiles(op);
            return GetNpcAntiAirMaxRangeTiles();
        }

        public static float GetAntiAirDamage(WorldObject_WD_Outpost origin)
        {
            var seth = WorldDominationMod.settings;
            float d = seth?.antiAirBaseDamage ?? WorldDominationSettings.DefAntiAirBaseDamage;
            if (origin != null)
                d += origin.GetBuiltUpgradeMortarShellDamageBonus();
            return Mathf.Max(0f, d);
        }

        public static float GetNpcAntiAirDamage()
        {
            var seth = WorldDominationMod.settings;
            return Mathf.Max(0f, seth?.npcAntiAirDamage ?? WorldDominationSettings.DefNpcAntiAirDamage);
        }

        public static float GetAntiAirEffectiveCooldownSeconds(WorldObject_WD_Outpost origin, out float fromUpgradeReduction)
        {
            fromUpgradeReduction = 0f;
            var seth = WorldDominationMod.settings;
            float baseSec = Mathf.Max(1f, seth?.cooldownAntiAirSeconds ?? WorldDominationSettings.DefCooldownAntiAirSeconds);
            float floorSec = Mathf.Max(1f, seth?.antiAirCooldownFloorSeconds ?? WorldDominationSettings.DefAntiAirCooldownFloorSeconds);
            if (origin == null)
                return Mathf.Max(floorSec, baseSec);

            float fromSkill = WorldDominationSettings.MortarCooldownReductionPerCumulativeShootingSkill * origin.GetSkillSum(SkillDefOf.Shooting);
            fromUpgradeReduction = origin.GetBuiltUpgradeMortarCooldownReduction();
            float mult = Mathf.Max(WorldDominationSettings.MortarCooldownMultiplierFloor, 1f - fromSkill - fromUpgradeReduction);
            return Mathf.Max(floorSec, baseSec * mult);
        }

        public static float GetNpcAntiAirCooldownSeconds()
        {
            var seth = WorldDominationMod.settings;
            return Mathf.Max(1f, seth?.npcAntiAirCooldownSeconds ?? WorldDominationSettings.DefNpcAntiAirCooldownSeconds);
        }

        public static void ApplyAntiAirCooldown(CompViralSpread comp, WorldObject origin)
        {
            if (comp == null || origin == null) return;
            float sec = origin is WorldObject_WD_Outpost op
                ? GetAntiAirEffectiveCooldownSeconds(op, out _)
                : GetNpcAntiAirCooldownSeconds();
            comp.antiAirCooldownTick = Find.TickManager.TicksGame + Mathf.RoundToInt(sec * 60f);
        }

        public static float GetAntiAirVsMortarHitChance(WorldObject origin)
        {
            var seth = WorldDominationMod.settings;
            if (origin is WorldObject_WD_Outpost)
                return Mathf.Clamp01(seth?.antiAirVsMortarHitChance ?? WorldDominationSettings.DefAntiAirVsMortarHitChance);
            return Mathf.Clamp01(seth?.npcAntiAirVsMortarHitChance ?? WorldDominationSettings.DefNpcAntiAirVsMortarHitChance);
        }

        /// <summary>Base Anti-Air hit chance for pods/aerials from distance band (fraction of AA max range). Mortar shells use <see cref="GetAntiAirVsMortarHitChance"/> instead.</summary>
        public static float BandBaseAntiAirHitChance(float distance, float maxRange, WorldDominationSettings seth, bool useNpcBands)
        {
            var s = seth ?? WorldDominationMod.settings;
            int band = MortarFireUtils.GetAccuracyBandIndex(distance, maxRange);
            if (useNpcBands)
            {
                switch (band)
                {
                    case 0:
                        return Mathf.Clamp01(s?.npcAntiAirHitChance0To50PctRange ?? WorldDominationSettings.DefNpcAntiAirHitChance0To50PctRange);
                    case 1:
                        return Mathf.Clamp01(s?.npcAntiAirHitChance51To75PctRange ?? WorldDominationSettings.DefNpcAntiAirHitChance51To75PctRange);
                    default:
                        return Mathf.Clamp01(s?.npcAntiAirHitChance76To100PctRange ?? WorldDominationSettings.DefNpcAntiAirHitChance76To100PctRange);
                }
            }
            switch (band)
            {
                case 0:
                    return Mathf.Clamp01(s?.antiAirHitChance0To50PctRange ?? WorldDominationSettings.DefAntiAirHitChance0To50PctRange);
                case 1:
                    return Mathf.Clamp01(s?.antiAirHitChance51To75PctRange ?? WorldDominationSettings.DefAntiAirHitChance51To75PctRange);
                default:
                    return Mathf.Clamp01(s?.antiAirHitChance76To100PctRange ?? WorldDominationSettings.DefAntiAirHitChance76To100PctRange);
            }
        }

        /// <summary>Anti-Air hit vs pods/aerials: AA band base + best shooter flat (+1 pp per Shooting level). No mortar hit upgrades.</summary>
        public static bool RollAntiAirHit(float distance, float maxRange, float bestShootingSkill, WorldDominationSettings seth, bool useNpcBands)
        {
            float baseHit = BandBaseAntiAirHitChance(distance, maxRange, seth, useNpcBands);
            float fromBest = Mathf.Max(0f, bestShootingSkill) * WorldDominationSettings.MortarHitFlatBonusPerBestShootingLevel;
            return Rand.Value < Mathf.Clamp01(baseHit + fromBest);
        }

        /// <summary>Queue an AA engage after the global reaction delay. Returns true if queued or already pending.</summary>
        public static bool TryQueueEngage(WorldObject origin, WorldObject target)
        {
            if (!TryClassifyTarget(origin, target, out AntiAirTargetKind kind))
            {
                WDVerbose.Msg($"AA queue fail {origin?.LabelCap} -> {target?.LabelCap}: classify (faction/hostile/mission)");
                return false;
            }
            if (origin is WorldObject_WD_Outpost aaOutpost && !AllowsAntiAirKind(aaOutpost, kind))
            {
                WDVerbose.Msg($"AA queue fail {origin?.LabelCap} -> {target?.LabelCap}: kind filter ({kind})");
                return false;
            }
            if (!IsShooterReady(origin, out _, out _))
            {
                WDVerbose.Msg($"AA queue fail {origin?.LabelCap} -> {target?.LabelCap}: shooter not ready (auto/upgrade/cooldown/skill)");
                return false;
            }
            // NPC T4: one global AA engage at a time, then 3s before the next settlement may queue.
            bool npcSettlement = origin is Settlement;
            if (npcSettlement && !NpcT4GlobalFireStagger.CanQueueNpcAa())
            {
                WDVerbose.Msg($"AA queue fail {origin?.LabelCap} -> {target?.LabelCap}: global NPC AA stagger");
                return false;
            }
            if (!IsInAaRange(origin, target))
            {
                WDVerbose.Msg($"AA queue fail {origin?.LabelCap} -> {target?.LabelCap}: range");
                return false;
            }
            if (!PassesGroupLock(origin, target, claimNow: false))
            {
                WDVerbose.Msg($"AA queue fail {origin?.LabelCap} -> {target?.LabelCap}: group lock");
                return false;
            }

            long key = MakeEngageKey(origin.ID, target.ID);
            if (pendingEngageKeys.Contains(key))
            {
                WDVerbose.Msg($"AA queue already-pending {origin.LabelCap} -> {target.LabelCap} kind={kind}");
                return true;
            }

            int now = Find.TickManager.TicksGame;
            int readyAt = now + ReactionDelayTicks;
            pendingEngage.Add(new PendingEngage
            {
                engageAtTick = readyAt,
                hopWaitDeadlineTick = readyAt + HopWaitCapTicks,
                origin = origin,
                target = target,
                kind = kind,
                isPlayerOutpost = origin is WorldObject_WD_Outpost
            });
            pendingEngageKeys.Add(key);
            if (npcSettlement)
                NpcT4GlobalFireStagger.NotifyNpcAaQueued();
            WDVerbose.Msg($"AA queue OK {origin.LabelCap} -> {target.LabelCap} kind={kind} engageIn={ReactionDelayTicks}");
            return true;
        }

        /// <summary>Legacy entry used by interceptor fire / wake paths.</summary>
        public static bool TryEngageDropPod(WorldObject_WD_Outpost origin, WorldObject_Traveler pod)
            => TryQueueEngage(origin, pod);

        public static bool TryEngageFromSettlement(Settlement origin, WorldObject target)
            => TryQueueEngage(origin, target);

        public static void TickPending()
        {
            TickPendingEngage();
            TickPendingFlak();
            PruneExpiredClaims();
        }

        /// <summary>Called from the interception scheduler each tick.</summary>
        public static void TickPendingFlak()
        {
            if (pendingFlak.Count == 0) return;
            int now = Find.TickManager.TicksGame;
            for (int i = pendingFlak.Count - 1; i >= 0; i--)
            {
                PendingFlak p = pendingFlak[i];
                if (now < p.spawnAtTick) continue;
                pendingFlak.RemoveAt(i);
                if (p.origin == null || p.origin.Destroyed) continue;
                if (p.target == null || p.target.Destroyed) continue;
                WorldActions_Traveler.SpawnFlakTraveler(p.origin, p.target, p.damage, p.hit, p.isResolver, p.kind);
            }
        }

        public static void WakeAllForDropPod(WorldObject_Traveler pod)
        {
            if (pod == null || pod.Destroyed) return;
            WDVerbose.Msg($"AA WakeAllForDropPod {pod.LabelCap}[{pod.mission}] tile={pod.Tile.tileId} moving={pod.pather?.moving} dest={(pod.pather != null && pod.pather.destTile.Valid ? pod.pather.destTile.tileId.ToString() : "-")} next={(pod.pather != null && pod.pather.nextTile.Valid ? pod.pather.nextTile.tileId.ToString() : "-")} fac={pod.Faction?.Name ?? "null"}");
            WorldComponent_InterceptionScheduler.Current?.NotifyHostileAirborneTarget(pod);
        }

        public static void WakeAllForMortarShell(WorldObject_Traveler shell)
        {
            if (shell == null || shell.Destroyed || shell.mission != TravelerMission.MortarStrike) return;
            // AT Turret shells are ground-hugging fire, not AA targets (mortars / drop pods only).
            if (shell.IsAtTurretShell()) return;
            WorldComponent_InterceptionScheduler.Current?.NotifyHostileAirborneTarget(shell);
        }

        public static void WakeAllForVanillaPods(TravellingTransporters pods)
        {
            if (pods == null || pods.Destroyed) return;
            WorldComponent_InterceptionScheduler.Current?.NotifyHostileAirborneTarget(pods);
        }

        /// <summary>
        /// When Auto AA is turned on, engage any hostile drop pods / shells / transport pods already in range
        /// (they only wake AA at spawn, so mid-flight targets would otherwise be ignored).
        /// </summary>
        public static void EngageExistingAirborneTargets(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null || outpost.Destroyed) return;
            if (!outpost.IsMortarOutpost || !outpost.AntiAirDefenseActive) return;
            if (!HasAntiAirUpgrade(outpost)) return;

            var all = Find.WorldObjects?.AllWorldObjects;
            if (all == null) return;

            for (int i = 0; i < all.Count; i++)
            {
                WorldObject wo = all[i];
                if (wo == null || wo.Destroyed) continue;
                TryQueueEngage(outpost, wo);
            }
        }

        public static void ClearClaimForTarget(int targetId)
        {
            claimLetterByTargetId.Remove(targetId);
            claimExpireTickByTargetId.Remove(targetId);
        }

        public static void NotifyTargetDestroyed(WorldObject target)
        {
            if (target == null) return;
            ClearClaimForTarget(target.ID);
        }

        public static Vector3 JitterMeet(Vector3 meet, float tiles)
        {
            if (meet.sqrMagnitude < 0.0001f || tiles <= 0.01f) return meet;
            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return meet;

            Vector3 n = meet.normalized;
            Vector3 axis = Vector3.Cross(n, new Vector3(Rand.Gaussian(), Rand.Gaussian(), Rand.Gaussian()));
            if (axis.sqrMagnitude < 0.0001f)
                axis = Vector3.Cross(n, Vector3.up);
            if (axis.sqrMagnitude < 0.0001f)
                return meet;
            axis.Normalize();

            float lo = 0f;
            float hi = 0.08f;
            for (int i = 0; i < 12; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (grid.ApproxDistanceInTiles(mid) < tiles)
                    lo = mid;
                else
                    hi = mid;
            }
            float angleRad = (lo + hi) * 0.5f;
            return Quaternion.AngleAxis(angleRad * Mathf.Rad2Deg, axis) * meet;
        }

        public static Vector3 JitterMeetRandom(Vector3 meet)
            => JitterMeet(meet, Rand.Range(MeetJitterTilesMin, MeetJitterTilesMax));

        public static bool IsAirborneAaTargetMission(TravelerMission m)
            => m == TravelerMission.RaidDropPod
            || m == TravelerMission.MortarStrike
            || m == TravelerMission.RapidResponseDropPod;

        /// <summary>True for any traveler / vanilla pods Anti-Air may engage (includes warehouse drop-pod deliveries).</summary>
        public static bool IsAirborneAaTarget(WorldObject target)
        {
            if (target is WorldObject_Traveler t)
            {
                // AT shells reuse MortarStrike mission but must never be flak targets.
                if (t.IsAtTurretShell()) return false;
                if (IsAirborneAaTargetMission(t.mission)) return true;
                return OutpostDispatchMode.IsPlayerCargoDropPod(t);
            }
            if (target is TravellingTransporters) return true;
            return VehicleFrameworkAerialAaCompat.IsAerialVehicleInFlight(target);
        }

        /// <summary>
        /// Mortar shell AA may engage (not AT turret shells, not flak). Hostility is checked by the caller.
        /// T4 AA vs these shells does not require Mid/Late escalation.
        /// </summary>
        public static bool IsHostileMortarShell(WorldObject target)
        {
            if (target is not WorldObject_Traveler t || t.Destroyed) return false;
            if (t.mission != TravelerMission.MortarStrike) return false;
            return !t.IsAtTurretShell();
        }

        private static void TickPendingEngage()
        {
            if (pendingEngage.Count == 0) return;
            int now = Find.TickManager.TicksGame;
            for (int i = pendingEngage.Count - 1; i >= 0; i--)
            {
                PendingEngage p = pendingEngage[i];
                if (now < p.engageAtTick) continue;

                // WD ballistic targets: do not fire until a real path hop exists (lead otherwise collapses to launch tile).
                if (RequiresBallisticHopBeforeFire(p.target) && !HasReadyBallisticHop(p.target))
                {
                    if (now >= p.hopWaitDeadlineTick)
                    {
                        pendingEngage.RemoveAt(i);
                        if (p.origin != null)
                            pendingEngageKeys.Remove(MakeEngageKey(p.origin.ID, p.target != null ? p.target.ID : 0));
                        WDVerbose.Msg(
                            $"AA hop-wait abort {p.origin?.LabelCap} -> {p.target?.LabelCap}: no path hop by deadline (no origin shot)");
                        continue;
                    }

                    p.engageAtTick = now + HopDeferStepTicks;
                    pendingEngage[i] = p;
                    WDVerbose.Msg(
                        $"AA hop-wait defer {p.origin?.LabelCap} -> {p.target?.LabelCap}: retry in {HopDeferStepTicks}t");
                    continue;
                }

                pendingEngage.RemoveAt(i);
                if (p.origin != null)
                    pendingEngageKeys.Remove(MakeEngageKey(p.origin.ID, p.target != null ? p.target.ID : 0));
                ExecuteEngage(p);
            }
        }

        /// <summary>Drop pods / mortar shells need a moving hop before lead is meaningful. Vanilla/VF do not.</summary>
        private static bool RequiresBallisticHopBeforeFire(WorldObject target)
        {
            if (!(target is WorldObject_Traveler t) || t.Destroyed) return false;
            if (t.mission == TravelerMission.AntiAirStrike) return false;
            return WD_PathFollower.IsBallisticWorldFlight(t);
        }

        private static bool HasReadyBallisticHop(WorldObject target)
        {
            if (!(target is WorldObject_Traveler t) || t.Destroyed) return false;
            WD_PathFollower tp = t.pather;
            return tp != null && tp.moving && tp.nextTile.Valid;
        }

        private static void ExecuteEngage(PendingEngage p)
        {
            bool npcSettlement = p.origin is Settlement;
            bool fired = false;
            try
            {
                if (p.origin == null || p.origin.Destroyed || p.target == null || p.target.Destroyed)
                {
                    WDVerbose.Msg("AA execute abort: origin/target gone");
                    return;
                }
                if (!TryClassifyTarget(p.origin, p.target, out AntiAirTargetKind kind) || kind != p.kind)
                {
                    WDVerbose.Msg($"AA execute abort {p.origin.LabelCap} -> {p.target.LabelCap}: classify/kind mismatch");
                    return;
                }
                if (!IsShooterReady(p.origin, out CompViralSpread comp, out float bestShooting))
                {
                    WDVerbose.Msg($"AA execute abort {p.origin.LabelCap} -> {p.target.LabelCap}: shooter not ready");
                    return;
                }
                if (!IsInAaRange(p.origin, p.target))
                {
                    WDVerbose.Msg($"AA execute abort {p.origin.LabelCap} -> {p.target.LabelCap}: range");
                    return;
                }
                if (!PassesGroupLock(p.origin, p.target, claimNow: true))
                {
                    WDVerbose.Msg($"AA execute abort {p.origin.LabelCap} -> {p.target.LabelCap}: group lock");
                    return;
                }

                float maxRange = GetAntiAirMaxRangeForOrigin(p.origin);
                bool hit;
                float damage;
                float rangeTiles;

                if (kind == AntiAirTargetKind.MortarStrike)
                {
                    if (!(p.target is WorldObject_Traveler shell)) return;
                    if (!AntiAirIntercept.TryResolveLeadFlight(p.origin, shell, maxRange, WorldActions_Traveler.GetFlakShellTicksPerMove(),
                            out _, out _, out rangeTiles))
                    {
                        WDVerbose.Msg($"AA execute abort {p.origin.LabelCap} -> {p.target.LabelCap}: lead flight (mortar)");
                        return;
                    }
                    hit = Rand.Chance(GetAntiAirVsMortarHitChance(p.origin));
                    damage = 0f;
                }
                else if (kind == AntiAirTargetKind.RaidDropPod)
                {
                    if (!(p.target is WorldObject_Traveler pod)) return;
                    if (!AntiAirIntercept.TryResolveLeadFlight(p.origin, pod, maxRange, WorldActions_Traveler.GetFlakShellTicksPerMove(),
                            out _, out _, out rangeTiles))
                    {
                        WDVerbose.Msg($"AA execute abort {p.origin.LabelCap} -> {p.target.LabelCap}: lead flight (drop pod mission={pod.mission})");
                        return;
                    }
                    hit = RollBandedHit(p.origin, rangeTiles, maxRange, bestShooting);
                    damage = p.origin is WorldObject_WD_Outpost op ? GetAntiAirDamage(op) : GetNpcAntiAirDamage();
                }
                else
                {
                    // Vanilla transport pods and Vehicle Framework aerials: aim at DrawPos / lead.
                    if (!(p.target is TravellingTransporters)
                        && !VehicleFrameworkAerialAaCompat.IsAerialVehicleInFlight(p.target))
                        return;
                    if (!AntiAirIntercept.TryResolveLeadFlightForWorldObject(p.origin, p.target, maxRange, WorldActions_Traveler.GetFlakShellTicksPerMove(),
                            out _, out _, out rangeTiles))
                    {
                        WDVerbose.Msg($"AA execute abort {p.origin.LabelCap} -> {p.target.LabelCap}: lead flight (external airborne kind={kind})");
                        return;
                    }
                    hit = RollBandedHit(p.origin, rangeTiles, maxRange, bestShooting);
                    damage = p.origin is WorldObject_WD_Outpost op2 ? GetAntiAirDamage(op2) : GetNpcAntiAirDamage();
                }

                ApplyAntiAirCooldown(comp, p.origin);
                if (p.origin is WorldObject_WD_Outpost outpost)
                    WD_Outpost_Mortar.InvalidateFireGizmoCache(outpost);

                int volley = Rand.RangeInclusive(VolleyMin, VolleyMax);
                int resolverIndex = Rand.Range(0, volley);
                int now = Find.TickManager.TicksGame;

                for (int i = 0; i < volley; i++)
                {
                    int delay = i == 0 ? 0 : 6 + (i - 1) * 10 + Rand.RangeInclusive(0, 5);
                    bool isResolver = i == resolverIndex;
                    EnqueueOrSpawn(now + delay, p.origin, p.target,
                        isResolver ? damage : 0f,
                        isResolver && hit,
                        isResolver,
                        kind);
                }

                fired = true;
                WDVerbose.Msg($"AA execute FIRE {p.origin.LabelCap} -> {p.target.LabelCap} kind={kind} range={rangeTiles:F1}/{maxRange:F1} hit={hit} dmg={damage:F0} volley={volley}");

                string targetLabel = p.target.LabelCap;
                Find.World?.GetComponent<WorldComponent_SpreadManager>()?.AddLog(new SpreadLogEntry(
                    "TSA_WD_Log_AntiAirLaunched".Translate(p.origin.LabelCap, targetLabel, damage.ToString("F0")),
                    p.origin, p.target));
            }
            finally
            {
                if (npcSettlement)
                    NpcT4GlobalFireStagger.NotifyNpcAaEngageEnded(fired);
            }
        }

        private static bool RollBandedHit(WorldObject origin, float rangeTiles, float maxRange, float bestShooting)
        {
            var seth = WorldDominationMod.settings;
            bool useNpc = !(origin is WorldObject_WD_Outpost);
            return RollAntiAirHit(rangeTiles, maxRange, bestShooting, seth, useNpc);
        }

        private static bool IsShooterReady(WorldObject origin, out CompViralSpread comp, out float bestShooting)
        {
            comp = null;
            bestShooting = 0f;
            if (origin == null || origin.Destroyed) return false;

            if (origin is WorldObject_WD_Outpost op)
            {
                if (!op.IsMortarOutpost || !op.AntiAirDefenseActive) return false;
                if (!HasAntiAirUpgrade(op)) return false;
                if (op.Faction == null) return false;
                comp = op.GetComponent<CompViralSpread>();
                if (comp == null || comp.IsAntiAirOnCooldown) return false;
                bestShooting = op.GetHighestVirtualPawnSkill(SkillDefOf.Shooting);
                return bestShooting > 0f;
            }

            if (origin is Settlement settlement)
            {
                if (!CompViralSpread.IsSettlementAntiAirReady(settlement, out comp, out bestShooting))
                    return false;
                return true;
            }

            return false;
        }

        private static bool IsInAaRange(WorldObject origin, WorldObject target)
        {
            if (origin == null || target == null) return false;
            float maxRange = GetAntiAirMaxRangeForOrigin(origin);
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            return IsAirborneInAaRange(origin, target, maxRange, manager);
        }

        /// <summary>
        /// True if the airborne target is in AA range now, or (for ballistic shells/pods) its destination /
        /// aimed world object / great-circle flight arc comes within range — so flybys and inbound shells wake AA.
        /// Ballistic travelers keep <see cref="WorldObject.Tile"/> at the launch tile until arrival; range must use
        /// dest / arc / current hop progress — never “spawn tile only”.
        /// </summary>
        public static bool IsAirborneInAaRange(
            WorldObject aa,
            WorldObject target,
            float aaRange,
            WorldComponent_SpreadManager manager)
        {
            if (aa == null || target == null || aaRange <= 0f) return false;
            int aaTile = aa.Tile.tileId;
            if (aaTile < 0) return false;

            // Vanilla pods / VF aerials keep Tile at launch or last waypoint; DrawPos is the live flight position.
            // Fire gate: current position only. Use WillExternalAirborneFlightEnterAaRange to arm shooters.
            if (target is TravellingTransporters || VehicleFrameworkAerialAaCompat.IsAerialVehicleInFlight(target))
            {
                WorldGrid grid = Find.WorldGrid;
                if (grid == null) return false;
                if (target.Tile.tileId < 0) return false;

                Vector3 meet = target.DrawPos;
                if (meet.sqrMagnitude < 0.0001f) return false;

                Vector3 aaPos = grid.GetTileCenter(aa.Tile);
                return WorldDistTiles(aaPos, meet, grid) <= aaRange;
            }

            if (TileWithin(manager, aaTile, target.Tile.tileId, aaRange))
                return true;

            if (target is WorldObject_Traveler t)
            {
                if (t.targetObject != null && !t.targetObject.Destroyed
                    && TileWithin(manager, aaTile, t.targetObject.Tile.tileId, aaRange))
                    return true;

                WD_PathFollower path = t.pather;
                if (path != null && path.moving)
                {
                    if (path.destTile.Valid && TileWithin(manager, aaTile, path.destTile.tileId, aaRange))
                        return true;
                    if (path.nextTile.Valid && TileWithin(manager, aaTile, path.nextTile.tileId, aaRange))
                        return true;

                    // Ballistic hop: Tile stays at origin until arrival. Check live progress + full arc for flybys
                    // that start and end outside AA range but pass through it.
                    if (WD_PathFollower.IsBallisticWorldFlight(t) && path.nextTile.Valid)
                    {
                        if (CurrentBallisticPosWithinRange(aaTile, t, aaRange))
                            return true;
                        if (BallisticArcComesWithinRange(aaTile, t.Tile.tileId, path.nextTile.tileId, aaRange))
                            return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// True if this AA site should watch an external airborne target (vanilla pods / VF aerial):
        /// already in DrawPos range, destination in range, or flight arc clips the AA bubble.
        /// Fire still requires <see cref="IsAirborneInAaRange"/> (DrawPos).
        /// </summary>
        public static bool WillExternalAirborneFlightEnterAaRange(
            WorldObject aa,
            WorldObject target,
            float aaRange,
            WorldComponent_SpreadManager manager)
        {
            if (aa == null || target == null || aaRange <= 0f) return false;
            if (IsAirborneInAaRange(aa, target, aaRange, manager))
                return true;

            if (target is TravellingTransporters pods)
                return WillVanillaPodFlightEnterAaRange(aa, pods, aaRange, manager);

            if (VehicleFrameworkAerialAaCompat.IsAerialVehicleInFlight(target))
                return WillVfAerialFlightEnterAaRange(aa, target, aaRange, manager);

            return false;
        }

        /// <summary>
        /// True if this AA site should watch a vanilla transport pod: already in DrawPos range, destination in range,
        /// or the launch→dest great-circle clips the AA bubble. Used to arm shooters at launch; fire still requires
        /// <see cref="IsAirborneInAaRange"/> (DrawPos).
        /// </summary>
        public static bool WillVanillaPodFlightEnterAaRange(
            WorldObject aa,
            TravellingTransporters pods,
            float aaRange,
            WorldComponent_SpreadManager manager)
        {
            if (aa == null || pods == null || aaRange <= 0f) return false;
            if (IsAirborneInAaRange(aa, pods, aaRange, manager))
                return true;

            int aaTile = aa.Tile.tileId;
            int fromTile = pods.Tile.tileId;
            if (aaTile < 0 || fromTile < 0) return false;

            int destTile = pods.destinationTile.Valid ? pods.destinationTile.tileId : -1;
            if (destTile < 0) return false;

            if (TileWithin(manager, aaTile, destTile, aaRange))
                return true;

            return BallisticArcComesWithinRange(aaTile, fromTile, destTile, aaRange);
        }

        private static bool WillVfAerialFlightEnterAaRange(
            WorldObject aa,
            WorldObject aerial,
            float aaRange,
            WorldComponent_SpreadManager manager)
        {
            int aaTile = aa.Tile.tileId;
            int fromTile = aerial.Tile.tileId;
            if (aaTile < 0 || fromTile < 0) return false;

            if (!VehicleFrameworkAerialAaCompat.TryGetFlightNodes(aerial, out List<int> nodes) || nodes.Count == 0)
                return false;

            int destTile = nodes[nodes.Count - 1];
            if (destTile >= 0 && TileWithin(manager, aaTile, destTile, aaRange))
                return true;

            // Current position → first waypoint, then each consecutive path leg.
            if (BallisticArcComesWithinRange(aaTile, fromTile, nodes[0], aaRange))
                return true;
            for (int i = 0; i < nodes.Count - 1; i++)
            {
                if (BallisticArcComesWithinRange(aaTile, nodes[i], nodes[i + 1], aaRange))
                    return true;
            }
            return false;
        }

        /// <summary>True when the pod/shell’s current great-circle progress is within AA range (Tile may still be at launch).</summary>
        private static bool CurrentBallisticPosWithinRange(int aaTile, WorldObject_Traveler t, float aaRange)
        {
            WD_PathFollower path = t?.pather;
            if (path == null || !path.moving || !path.nextTile.Valid) return false;
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || aaTile < 0 || t.Tile.tileId < 0) return false;

            Vector3 aaPos = grid.GetTileCenter(aaTile);
            Vector3 from = grid.GetTileCenter(t.Tile.tileId);
            Vector3 to = grid.GetTileCenter(path.nextTile.tileId);
            float total = Mathf.Max(0.001f, path.nextTileCostTotal);
            float progress = Mathf.Clamp01(1f - Mathf.Max(0f, path.nextTileCostLeft) / total);
            Vector3 p = Vector3.Slerp(from, to, progress);
            return WorldDistTiles(aaPos, p, grid) <= aaRange;
        }

        private static bool BallisticArcComesWithinRange(int aaTile, int fromTile, int toTile, float aaRange)
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || aaTile < 0 || fromTile < 0 || toTile < 0) return false;
            Vector3 aaPos = grid.GetTileCenter(aaTile);
            Vector3 from = grid.GetTileCenter(fromTile);
            Vector3 to = grid.GetTileCenter(toTile);
            float hopTiles = Mathf.Max(1f, grid.ApproxDistanceInTiles(fromTile, toTile));
            // ~1 sample per tile so short clips of the AA bubble are not missed on long flybys.
            int samples = Mathf.Clamp(Mathf.CeilToInt(hopTiles), 12, 64);
            for (int i = 0; i <= samples; i++)
            {
                float u = i / (float)samples;
                Vector3 p = Vector3.Slerp(from, to, u);
                if (WorldDistTiles(aaPos, p, grid) <= aaRange) return true;
            }
            return false;
        }

        private static float WorldDistTiles(Vector3 a, Vector3 b, WorldGrid grid)
        {
            float cos = Mathf.Clamp(Vector3.Dot(a.normalized, b.normalized), -1f, 1f);
            return Mathf.Max(0.05f, grid.ApproxDistanceInTiles(Mathf.Acos(cos)));
        }

        /// <summary>True when a mortar shell, drop pod, or transport pod is aimed at this shooter — self-defense, no late-game gate.</summary>
        public static bool IsInboundThreatTo(WorldObject shooter, WorldObject airborne)
        {
            if (shooter == null || airborne == null) return false;

            if (airborne is TravellingTransporters pods)
            {
                return pods.destinationTile.Valid && pods.destinationTile.tileId == shooter.Tile.tileId;
            }

            if (VehicleFrameworkAerialAaCompat.IsAerialVehicleInFlight(airborne))
            {
                int dest = VehicleFrameworkAerialAaCompat.TryGetDestinationTileId(airborne);
                return dest >= 0 && dest == shooter.Tile.tileId;
            }

            if (airborne is not WorldObject_Traveler t) return false;
            if (t.IsAtTurretShell()) return false;
            if (t.mission != TravelerMission.MortarStrike
                && t.mission != TravelerMission.RaidDropPod
                && t.mission != TravelerMission.RapidResponseDropPod
                && !OutpostDispatchMode.IsPlayerCargoDropPod(t))
                return false;

            if (t.targetObject != null && !t.targetObject.Destroyed && t.targetObject.ID == shooter.ID)
                return true;

            WD_PathFollower path = t.pather;
            if (path != null && path.moving && path.destTile.Valid && path.destTile.tileId == shooter.Tile.tileId)
                return true;

            return false;
        }

        private static bool TileWithin(WorldComponent_SpreadManager manager, int fromTile, int toTile, float range)
        {
            if (fromTile < 0 || toTile < 0) return false;
            float dist = manager != null
                ? WorldActions_Utils.GetDistance(fromTile, toTile, manager)
                : Find.WorldGrid.ApproxDistanceInTiles(fromTile, toTile);
            return dist <= range;
        }

        private static bool TryClassifyTarget(WorldObject origin, WorldObject target, out AntiAirTargetKind kind)
        {
            kind = AntiAirTargetKind.RaidDropPod;
            if (origin == null || target == null || target.Destroyed) return false;
            Faction of = origin.Faction;
            Faction tf = target.Faction;
            if (of == null || tf == null) return false;
            if (tf == of) return false;
            if (!WorldActions_Utils.SafeHostileTo(of, tf)) return false;

            if (target is WorldObject_Traveler t)
            {
                if (t.mission == TravelerMission.RaidDropPod
                    || t.mission == TravelerMission.RapidResponseDropPod
                    || OutpostDispatchMode.IsPlayerCargoDropPod(t))
                {
                    kind = AntiAirTargetKind.RaidDropPod;
                    return true;
                }
                if (t.mission == TravelerMission.MortarStrike)
                {
                    if (t.IsAtTurretShell()) return false;
                    kind = AntiAirTargetKind.MortarStrike;
                    return true;
                }
                return false;
            }

            if (target is TravellingTransporters)
            {
                kind = AntiAirTargetKind.VanillaTransportPods;
                return true;
            }

            if (VehicleFrameworkAerialAaCompat.IsAerialVehicleInFlight(target))
            {
                kind = AntiAirTargetKind.VehicleFrameworkAerial;
                return true;
            }

            return false;
        }

        private static bool PassesGroupLock(WorldObject origin, WorldObject target, bool claimNow)
        {
            if (!(origin is WorldObject_WD_Outpost op)) return true;
            AntiAirGroupLetter letter = op.AntiAirGroup;
            if (letter == AntiAirGroupLetter.Off) return true;

            int id = target.ID;
            PruneExpiredClaims();
            if (claimLetterByTargetId.TryGetValue(id, out AntiAirGroupLetter claimed))
            {
                if (claimed != letter) return false;
                if (claimNow)
                    claimExpireTickByTargetId[id] = Find.TickManager.TicksGame + ClaimFailsafeTicks;
                return true;
            }

            if (claimNow)
            {
                claimLetterByTargetId[id] = letter;
                claimExpireTickByTargetId[id] = Find.TickManager.TicksGame + ClaimFailsafeTicks;
            }
            return true;
        }

        private static void PruneExpiredClaims()
        {
            if (claimExpireTickByTargetId.Count == 0) return;
            int now = Find.TickManager.TicksGame;
            List<int> remove = null;
            foreach (var kv in claimExpireTickByTargetId)
            {
                if (now < kv.Value) continue;
                remove ??= new List<int>(4);
                remove.Add(kv.Key);
            }
            if (remove == null) return;
            for (int i = 0; i < remove.Count; i++)
                ClearClaimForTarget(remove[i]);
        }

        private static void EnqueueOrSpawn(
            int spawnAtTick,
            WorldObject origin,
            WorldObject target,
            float damage,
            bool hit,
            bool isResolver,
            AntiAirTargetKind kind)
        {
            int now = Find.TickManager.TicksGame;
            if (spawnAtTick <= now)
            {
                WorldActions_Traveler.SpawnFlakTraveler(origin, target, damage, hit, isResolver, kind);
                return;
            }
            pendingFlak.Add(new PendingFlak
            {
                spawnAtTick = spawnAtTick,
                origin = origin,
                target = target,
                damage = damage,
                hit = hit,
                isResolver = isResolver,
                kind = kind
            });
        }

        private static long MakeEngageKey(int originId, int targetId)
        {
            unchecked
            {
                return ((long)originId << 32) ^ (uint)targetId;
            }
        }
    }
}
