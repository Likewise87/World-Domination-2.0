using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// AT Turret: lightweight defensive world object (not derived from <see cref="WorldObject_WD_Outpost"/>).
    /// Own strength / range / cooldown so it can be a valid target-of-opportunity/ambush/raid <c>targetObject</c>.
    /// Fires via <see cref="MortarFireUtils"/> so shells reuse shared combat visuals and letters.
    /// Expanding icon is Light/Medium/Heavy <c>AT_Gun_*</c> art (barrel faces north);
    /// globe <see cref="Material"/> stays settlement-style + faction tint. After each shot the barrel returns to a
    /// chosen default aim tile at the same turn rate.
    /// </summary>
    [StaticConstructorOnStartup]
    public class WorldObject_AT_Turret : WorldObject, IDefensiveInterceptor
    {
        public const float DefaultStrength = WorldDominationSettings.DefAtTurretMediumMaxStrength;
        public const float DefaultRangeTiles = WorldDominationSettings.DefAtTurretMediumRange;
        public const float DefaultDamage = WorldDominationSettings.DefAtTurretDamage;
        public const float DefaultSkillEquivalent = 8f;
        public const float DefaultCooldownDays = WorldDominationSettings.DefAtTurretCooldownDays;
        /// <summary>Lowest tiles players may set for AT Turret auto-fire range.</summary>
        public const float MinRangeTiles = 1f;

        /// <summary>~36°/s; a 180° swing finishes in about five seconds.</summary>
        private const float TurnDegreesPerTick = 0.6f;
        private const float FacingSnapEpsilonDeg = 1.5f;
        /// <summary>Hard cap so a moving target can never leave the turret stuck in <see cref="isAiming"/> forever.</summary>
        private const int MaxAimTicks = 300;
        /// <summary>Hold on the shot bearing before starting the return-to-idle turn (2 seconds).</summary>
        private const int PostShotHoldTicks = 120;
        /// <summary>
        /// Shell facing math assumes texture forward = east. AT_Gun barrel faces north in the PNG.
        /// +90 converts that east-relative angle so the barrel points along the shot (was -90, which aimed 180° off).
        /// </summary>
        private const float NorthFacingTextureOffsetDeg = 90f;

        public float strength = DefaultStrength;
        /// <summary>Legacy scribed field; combat uses <see cref="EffectiveRangeTiles"/> from settings / override.</summary>
        public float rangeTiles = DefaultRangeTiles;
        public int cooldownTick = -99999;
        /// <summary>Build/combat tier (Light / Medium / Heavy).</summary>
        public AtTurretTier tier = AtTurretTier.Medium;
        /// <summary>Settlement that built this turret; wiped when that settlement is destroyed or changes ownership.</summary>
        public Settlement builtBySettlement;
        /// <summary>
        /// Player site that ordered the build (colony settlement or WD outpost). Used for per-site caps.
        /// Null on older saves: fall back to <see cref="builtBySettlement"/>.
        /// </summary>
        public WorldObject builtBySite;

        private bool defenseActive = true;
        private int defenseMaskRaw = (int)MissionMask.Raider;
        private int raidTargetMaskRaw = (int)(RaidTargetMask.Player | RaidTargetMask.Allies | RaidTargetMask.OtherNpcs);
        /// <summary>-1 = use configured max. Otherwise absolute tiles, clamped to [min..max] at read time.</summary>
        private float rangeOverride = -1f;

        /// <summary>Current upright expanding-icon rotation (degrees, already includes north-art offset).</summary>
        private float currentFacingAngleDeg;
        private float desiredFacingAngleDeg;
        private bool isAiming;
        /// <summary>After a shot (or when the player sets a new default), lerp back toward the default aim tile.</summary>
        private bool isReturningToDefault;
        /// <summary>TicksGame until the post-shot hold ends and return-to-idle may start. -99999 = no hold.</summary>
        private int returnHoldUntilTick = -99999;
        private WorldObject pendingTarget;
        private float pendingApproxTileDist;
        private int aimStartedTick = -99999;
        /// <summary>World tile the barrel rests toward when idle. -1 = art default (no rotation / north).</summary>
        private int defaultAimTileId = -1;
        private Material cachedMaterial;
        private string cachedInspectString;
        private int cachedInspectTick = -999;
        private static Texture2D cachedSetFacingIcon;
        private static Texture2D cachedConfigureIcon;
        private static Texture2D cachedIconLight;
        private static Texture2D cachedIconMedium;
        private static Texture2D cachedIconHeavy;

        public bool IsOnCooldown => (Find.TickManager?.TicksGame ?? 0) < cooldownTick;
        public bool IsAiming => isAiming;

        public bool DefenseActive => defenseActive;
        public MissionMask DefenseMask => (MissionMask)defenseMaskRaw;
        public RaidTargetMask DefenseRaidTargetMask => (RaidTargetMask)raidTargetMaskRaw;
        public float RangeOverride => rangeOverride;

        /// <summary>Settings tier max range (absolute ceiling for the configure slider).</summary>
        public float GetConfiguredMaxRangeTiles()
        {
            var s = WorldDominationMod.settings;
            return s != null ? s.GetAtTurretRange(tier) : DefaultRangeTiles;
        }

        public float EffectiveRangeTiles
        {
            get
            {
                float max = GetConfiguredMaxRangeTiles();
                float min = Mathf.Min(MinRangeTiles, max);
                if (rangeOverride < 0f)
                    return max;
                return Mathf.Clamp(rangeOverride, min, max);
            }
        }

        public void SetDefenseActive(bool on)
        {
            defenseActive = on;
            RefreshInterceptorRegistration();
            if (on)
                WorldComponent_InterceptionScheduler.Current?.NotifyAtTurretEngagementOpportunity(this);
        }

        public void SetDefenseMask(MissionMask mask) => defenseMaskRaw = (int)mask;

        public void SetRaidTargetMask(RaidTargetMask mask) => raidTargetMaskRaw = (int)mask;

        /// <summary>Set absolute auto-fire tiles, or pass negative / at-max to clear override.</summary>
        public void SetRangeOverride(float tilesOrClear)
        {
            float max = GetConfiguredMaxRangeTiles();
            float min = Mathf.Min(MinRangeTiles, max);
            if (tilesOrClear < 0f || Mathf.Approximately(tilesOrClear, max))
                rangeOverride = -1f;
            else
                rangeOverride = Mathf.Clamp(tilesOrClear, min, max);
            WD_RadiusOverlayPrefs.InvalidateResolveCache();
            WorldComponent_SettlementWatchIndex.Get()?.Invalidate();
            WorldComponent_InterceptionScheduler.Current?.NotifyAtTurretEngagementOpportunity(this);
        }

        private void RefreshInterceptorRegistration()
        {
            var sched = WorldComponent_InterceptionScheduler.Current;
            if (sched == null) return;
            if (defenseActive && !Destroyed)
                sched.RegisterInterceptor(this);
            else
                sched.UnregisterInterceptor(this);
            WorldComponent_SettlementWatchIndex.Get()?.Invalidate();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                Scribe_Values.Look(ref strength, "strength", DefaultStrength);
            }
            else if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                // Prefer "strength"; fall back to legacy "health" from older saves.
                float loaded = -99999f;
                Scribe_Values.Look(ref loaded, "strength", -99999f);
                if (loaded < -99998f)
                {
                    loaded = DefaultStrength;
                    Scribe_Values.Look(ref loaded, "health", DefaultStrength);
                }
                strength = loaded;
            }
            Scribe_Values.Look(ref rangeTiles, "rangeTiles", DefaultRangeTiles);
            Scribe_Values.Look(ref cooldownTick, "cooldownTick", -99999);
            Scribe_Values.Look(ref tier, "atTurretTier", AtTurretTier.Medium);
            Scribe_References.Look(ref builtBySettlement, "builtBySettlement");
            Scribe_References.Look(ref builtBySite, "builtBySite");
            Scribe_Values.Look(ref currentFacingAngleDeg, "currentFacingAngleDeg", 0f);
            Scribe_Values.Look(ref defaultAimTileId, "defaultAimTileId", -1);
            Scribe_Values.Look(ref defenseActive, "atTurretDefenseActive", true);
            Scribe_Values.Look(ref defenseMaskRaw, "atTurretDefenseMask", (int)MissionMask.Raider);
            Scribe_Values.Look(ref raidTargetMaskRaw, "atTurretRaidTargetMask",
                (int)(RaidTargetMask.Player | RaidTargetMask.Allies | RaidTargetMask.OtherNpcs));
            Scribe_Values.Look(ref rangeOverride, "atTurretRangeOverride", -1f);
            // Pending engagement / return-in-progress are short-lived; drop on load.
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                ClearPendingEngagement();
                isReturningToDefault = false;
                returnHoldUntilTick = -99999;
                RefreshInterceptorRegistration();
                if (IsOnCooldown)
                    WorldComponent_InterceptionScheduler.Current?.ScheduleAtTurretCooldownWake(this);
            }
        }

        public override void PostAdd()
        {
            base.PostAdd();
            RefreshInterceptorRegistration();
            WorldComponent_InterceptionScheduler.Current?.NotifyAtTurretEngagementOpportunity(this);
        }

        public override void PostRemove()
        {
            ClearPendingEngagement();
            isReturningToDefault = false;
            returnHoldUntilTick = -99999;
            // Before unregister: peers aiming here (or covering this tile) may retarget if not on CD.
            WorldComponent_InterceptionScheduler.Current?.NotifyPotentialAtTargetDestroyed(this);
            WorldComponent_InterceptionScheduler.Current?.UnregisterInterceptor(this);
            WorldComponent_SettlementWatchIndex.Get()?.Invalidate();
            base.PostRemove();
        }

        public override void SetFaction(Faction newFaction)
        {
            base.SetFaction(newFaction);
            InvalidateWorldMapIconCache();
        }

        /// <summary>When true, combat destroyed-letter is skipped (ownership wipe / already notified).</summary>
        public bool suppressDestroyedLetter;

        /// <summary>Plain flat strength chip; no ruin/replacement object for this prototype pass.</summary>
        public void ApplyDamage(float amount)
        {
            if (amount <= 0f || Destroyed) return;
            strength -= amount;
            if (strength <= 0f)
            {
                AtTurretNotifyUtility.NotifyPlayerTurretDestroyed(this);
                suppressDestroyedLetter = true;
                Destroy();
            }
        }

        public void ApplyCooldown()
        {
            var s = WorldDominationMod.settings;
            float days = s != null ? s.GetAtTurretCooldownDays(tier) : DefaultCooldownDays;
            cooldownTick = (Find.TickManager?.TicksGame ?? 0) + Mathf.RoundToInt(Mathf.Max(0f, days) * 60000f);
            WorldComponent_InterceptionScheduler.Current?.ScheduleAtTurretCooldownWake(this);
        }

        public void InvalidateWorldMapIconCache()
        {
            cachedMaterial = null;
            Patch_WdWorldObjectNoExpandingIcon.NotifyIconModeChanged();
        }

        public override string Label => AtTurretUtility.LabelKey(tier).Translate();

        /// <summary>
        /// Globe mesh stays settlement-style + faction-colored (never AT_Gun), matching outposts.
        /// Type identity comes only from <see cref="ExpandingIcon"/>.
        /// </summary>
        public override Material Material
        {
            get
            {
                if (cachedMaterial == null && Faction != null)
                {
                    string path = Faction.def?.settlementTexturePath
                        ?? "World/WorldObjects/Settlements/Settlement";
                    cachedMaterial = MaterialPool.MatFrom(
                        path,
                        ShaderDatabase.WorldOverlayTransparentLit,
                        Faction.Color,
                        WorldMaterials.WorldObjectRenderQueue);
                }
                return cachedMaterial ?? base.Material;
            }
        }

        public override Texture2D ExpandingIcon
        {
            get
            {
                Texture2D tex = IconForTier(tier);
                return tex ?? base.ExpandingIcon;
            }
        }

        public static Texture2D IconForTier(AtTurretTier turretTier)
        {
            switch (turretTier)
            {
                case AtTurretTier.Light:
                    return cachedIconLight ??= ContentFinder<Texture2D>.Get(AtTurretUtility.TexturePathForTier(AtTurretTier.Light), false);
                case AtTurretTier.Heavy:
                    return cachedIconHeavy ??= ContentFinder<Texture2D>.Get(AtTurretUtility.TexturePathForTier(AtTurretTier.Heavy), false);
                default:
                    return cachedIconMedium ??= ContentFinder<Texture2D>.Get(AtTurretUtility.TexturePathForTier(AtTurretTier.Medium), false);
            }
        }

        public override float ExpandingIconRotation => currentFacingAngleDeg;

        public override string GetInspectString()
        {
            int tick = Find.TickManager?.TicksGame ?? 0;
            if (tick - cachedInspectTick < 60 && cachedInspectString != null)
                return cachedInspectString;
            cachedInspectTick = tick;
            cachedInspectString = BuildInspectString();
            return cachedInspectString;
        }

        private string BuildInspectString()
        {
            string baseStr = base.GetInspectString();
            float dmg = WorldDominationMod.settings != null
                ? WorldDominationMod.settings.GetAtTurretDamage(tier)
                : WorldDominationSettings.GetAtTurretDamageDefault(tier);
            string turretStr = "TSA_WD_Inspect_AT_TurretStrength".Translate(strength.ToString("F0"))
                + "\n" + "TSA_WD_Inspect_AT_TurretDamage".Translate(dmg.ToString("F0"))
                + "\n" + "TSA_WD_Inspect_AT_TurretRange".Translate(EffectiveRangeTiles.ToString("F0"));
            if (TravelerEndpointUtility.IsLiveEndpoint(builtBySite))
                turretStr += "\n" + "TSA_WD_Inspect_AT_TurretBuilder".Translate(builtBySite.LabelCap);
            else if (TravelerEndpointUtility.IsLiveEndpoint(builtBySettlement))
                turretStr += "\n" + "TSA_WD_Inspect_AT_TurretBuilder".Translate(builtBySettlement.LabelCap);
            if (IsOnCooldown)
            {
                float daysLeft = (cooldownTick - Find.TickManager.TicksGame) / 60000f;
                turretStr += "\n" + "TSA_WD_Inspect_AT_TurretCD".Translate(daysLeft.ToString("F1")).Colorize(Color.cyan);
            }
            else
                turretStr += "\n" + "TSA_WD_Inspect_AT_TurretReady".Translate().Colorize(Color.cyan);
            return string.IsNullOrEmpty(baseStr) ? turretStr : baseStr + "\n" + turretStr;
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos())
                yield return g;

            if (Faction == Faction.OfPlayer)
            {
                yield return new Command_Action
                {
                    defaultLabel = "TSA_WD_AT_Turret_ConfigureLabel".Translate(),
                    defaultDesc = "TSA_WD_AT_Turret_ConfigureDesc".Translate(),
                    icon = GetConfigureIcon(),
                    action = () => Dialog_AtTurretConfigure.Open(this)
                };

                yield return new Command_Action
                {
                    defaultLabel = "TSA_WD_AT_Turret_SetFacingLabel".Translate(),
                    defaultDesc = "TSA_WD_AT_Turret_SetFacingDesc".Translate(),
                    icon = GetSetFacingIcon(),
                    action = BeginSetDefaultFacingTargeting
                };
            }

            foreach (Gizmo g in RadiusHoverGizmos.GetForAtTurret(this))
                yield return g;
        }

        private static Texture2D GetConfigureIcon()
        {
            if (cachedConfigureIcon != null) return cachedConfigureIcon;
            Texture2D tex = ContentFinder<Texture2D>.Get("UI/Commands/AT_Radius", false);
            cachedConfigureIcon = tex ?? TexCommand.Attack;
            return cachedConfigureIcon;
        }

        private static Texture2D GetSetFacingIcon()
        {
            if (cachedSetFacingIcon != null) return cachedSetFacingIcon;
            Texture2D tex = ContentFinder<Texture2D>.Get("UI/Commands/AT_Angle", true);
            cachedSetFacingIcon = tex ?? TexCommand.Attack;
            return cachedSetFacingIcon;
        }

        public override void DrawExtraSelectionOverlays()
        {
            base.DrawExtraSelectionOverlays();
            if (!Find.WorldSelector.IsSelected(this)) return;

            if (Dialog_AtTurretConfigure.TryGetPreview(this, out float previewRadius) && previewRadius > 0f)
            {
                WD_RadiusOverlayMode.DrawOrFill(
                    this,
                    previewRadius,
                    OutpostCoverageFillKind.Red,
                    WorldOverlayLineMaterials.RadiusRed,
                    accuracyBands: true);
            }
        }

        protected override void Tick()
        {
            base.Tick();

            if (isAiming)
            {
                TickAimThenFire();
                return;
            }

            if (isReturningToDefault)
            {
                int now = Find.TickManager?.TicksGame ?? 0;
                if (now < returnHoldUntilTick)
                    return;
                TickReturnToDefault();
            }
        }

        private void TickAimThenFire()
        {
            if (pendingTarget == null || pendingTarget.Destroyed)
            {
                // Target gone before we fired: still ready (not on CD) — pick another traveler/AT.
                NotifyPendingTargetLostAndRetarget();
                return;
            }

            int now = Find.TickManager?.TicksGame ?? 0;
            // Lock bearing at engage time (do not chase a moving traveler each tick). At 0.6°/tick a
            // moving target's screen bearing can outrun the turn forever and leave isAiming stuck,
            // which blocks InterceptorCanFireNow for every later passer.
            bool timedOut = now - aimStartedTick >= MaxAimTicks;
            if (!timedOut && !AdvanceFacingToward(desiredFacingAngleDeg))
                return;

            WorldObject target = pendingTarget;
            float dist = pendingApproxTileDist;
            ClearPendingEngagement();
            MortarFireUtils.FireFromAtTurret(this, target, dist);
            BeginReturnToDefault(holdAfterShot: true);
        }

        /// <summary>
        /// Drop a lost aim target and try another engagement if still ready (not on cooldown).
        /// Guns that already fired stay on CD and do not retarget until the CD wake.
        /// </summary>
        public void NotifyPendingTargetLostAndRetarget()
        {
            ClearPendingEngagement();
            if (IsOnCooldown)
            {
                BeginReturnToDefault();
                return;
            }
            WorldComponent_InterceptionScheduler.Current?.TryEngageAtTurretTargets(this);
            if (!isAiming)
                BeginReturnToDefault();
        }

        public bool IsAimingAt(WorldObject target)
            => isAiming && target != null && pendingTarget == target;

        private void TickReturnToDefault()
        {
            if (!TryGetDefaultDesiredFacing(out float desired))
            {
                currentFacingAngleDeg = 0f;
                isReturningToDefault = false;
                returnHoldUntilTick = -99999;
                return;
            }

            desiredFacingAngleDeg = desired;
            if (AdvanceFacingToward(desiredFacingAngleDeg))
            {
                isReturningToDefault = false;
                returnHoldUntilTick = -99999;
            }
        }

        /// <summary>Returns true when facing has reached <paramref name="desired"/>.</summary>
        private bool AdvanceFacingToward(float desired)
        {
            currentFacingAngleDeg = Mathf.MoveTowardsAngle(currentFacingAngleDeg, desired, TurnDegreesPerTick);
            if (Mathf.Abs(Mathf.DeltaAngle(currentFacingAngleDeg, desired)) > FacingSnapEpsilonDeg)
                return false;
            currentFacingAngleDeg = desired;
            return true;
        }

        // --- IDefensiveInterceptor ---
        WorldObject IDefensiveInterceptor.Self => this;
        PlanetTile IDefensiveInterceptor.InterceptorTile => Tile;
        Faction IDefensiveInterceptor.InterceptorFaction => Faction;
        float IDefensiveInterceptor.InterceptorRange => EffectiveRangeTiles;
        MissionMask IDefensiveInterceptor.InterceptorMissionMask =>
            defenseActive ? DefenseMask : MissionMask.None;
        bool IDefensiveInterceptor.InterceptorCanFireNow() =>
            defenseActive && !Destroyed && !IsOnCooldown && !isAiming;
        /// <summary>Traveler flag only (caravan scan uses <see cref="AtTurretUtility.CanAutoTargetPlayerCaravan"/> separately).</summary>
        bool IDefensiveInterceptor.InterceptorCanTargetPlayer => AtTurretUtility.IsPlayerTravelerTargetingEnabled();

        void IDefensiveInterceptor.InterceptorFire(WorldObject_Traveler target, float approxTileDist)
        {
            if (!defenseActive) return;
            if (target == null || target.Destroyed) return;
            if (!AtTurretUtility.IsGroundAtTurretTravelerTarget(target)) return;
            if (target.Faction?.IsPlayer == true)
            {
                if (!AtTurretUtility.CanAutoTargetPlayerTraveler(this, target)) return;
            }
            else if (!RapidResponseUtility.IsEligibleAutoInterceptTarget(target, DefenseRaidTargetMask))
            {
                return;
            }
            if (IsOnCooldown || isAiming) return;

            BeginAimThenFire(target, approxTileDist);
        }

        /// <summary>Scheduler caravan path (independent of <see cref="IDefensiveInterceptor.InterceptorCanTargetPlayer"/>).</summary>
        public void InterceptorFireAtCaravan(Caravan target, float approxTileDist)
        {
            if (!defenseActive) return;
            if (!AtTurretUtility.CanAutoTargetPlayerCaravan(this, target)) return;
            if (IsOnCooldown || isAiming) return;
            BeginAimThenFire(target, approxTileDist);
        }

        /// <summary>AT-vs-AT (and other static world objects) via event wakes; travelers use <see cref="IDefensiveInterceptor.InterceptorFire"/>.</summary>
        public void TryFireAtWorldObject(WorldObject target, float approxTileDist)
        {
            if (!defenseActive) return;
            if (target == null || target.Destroyed) return;
            if (IsOnCooldown || isAiming) return;
            BeginAimThenFire(target, approxTileDist);
        }

        void IDefensiveInterceptor.InterceptorNoTargetFire() { }

        private void BeginAimThenFire(WorldObject target, float approxTileDist)
        {
            if (target == null || target.Destroyed) return;
            isReturningToDefault = false;
            returnHoldUntilTick = -99999;
            pendingTarget = target;
            pendingApproxTileDist = approxTileDist;
            aimStartedTick = Find.TickManager?.TicksGame ?? 0;
            if (!TryComputeDesiredFacingToWorldPos(target.DrawPos, out float desired))
            {
                // Cannot resolve screen facing (camera/off-world): fire immediately rather than stall forever.
                ClearPendingEngagement();
                MortarFireUtils.FireFromAtTurret(this, target, approxTileDist);
                BeginReturnToDefault(holdAfterShot: true);
                return;
            }

            desiredFacingAngleDeg = desired;
            isAiming = true;

            if (Mathf.Abs(Mathf.DeltaAngle(currentFacingAngleDeg, desiredFacingAngleDeg)) <= FacingSnapEpsilonDeg)
            {
                currentFacingAngleDeg = desiredFacingAngleDeg;
                ClearPendingEngagement();
                MortarFireUtils.FireFromAtTurret(this, target, approxTileDist);
                BeginReturnToDefault(holdAfterShot: true);
            }
        }

        /// <param name="holdAfterShot">When true, keep the shot bearing for <see cref="PostShotHoldTicks"/> before turning idle.</param>
        private void BeginReturnToDefault(bool holdAfterShot = false)
        {
            isReturningToDefault = true;
            returnHoldUntilTick = holdAfterShot
                ? (Find.TickManager?.TicksGame ?? 0) + PostShotHoldTicks
                : -99999;
            if (TryGetDefaultDesiredFacing(out float desired))
                desiredFacingAngleDeg = desired;
            else
                desiredFacingAngleDeg = 0f;
        }

        private void ClearPendingEngagement()
        {
            isAiming = false;
            pendingTarget = null;
            pendingApproxTileDist = 0f;
            aimStartedTick = -99999;
        }

        private void BeginSetDefaultFacingTargeting()
        {
            Messages.Message("TSA_WD_AT_Turret_SetFacingPrompt".Translate(), MessageTypeDefOf.NeutralEvent);
            Find.WorldTargeter.BeginTargeting(
                (GlobalTargetInfo target) =>
                {
                    if (!TryGetValidFacingTile(target, out int tileId))
                        return false;
                    SetDefaultAimTile(tileId);
                    Messages.Message("TSA_WD_AT_Turret_SetFacingDone".Translate(), MessageTypeDefOf.PositiveEvent);
                    return true;
                },
                true,
                null,
                false,
                null,
                null,
                t => TryGetValidFacingTile(t, out _));
        }

        private bool TryGetValidFacingTile(GlobalTargetInfo target, out int tileId)
        {
            tileId = -1;
            if (!target.IsValid) return false;
            tileId = target.Tile;
            if (tileId < 0) return false;
            if (tileId == Tile.tileId) return false;
            if (!PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(target.Tile))
                return false;
            return true;
        }

        private void SetDefaultAimTile(int tileId)
        {
            defaultAimTileId = tileId;
            // If mid-shot aim, only update the rest facing; otherwise turn to the new default now.
            if (!isAiming)
                BeginReturnToDefault();
        }

        private bool TryGetDefaultDesiredFacing(out float angleDeg)
        {
            if (defaultAimTileId < 0)
            {
                angleDeg = 0f;
                return true;
            }

            WorldGrid grid = Find.WorldGrid;
            if (grid == null)
            {
                angleDeg = currentFacingAngleDeg;
                return false;
            }

            PlanetTile aimTile = new PlanetTile(defaultAimTileId, Tile.Layer);
            if (!aimTile.Valid)
            {
                angleDeg = 0f;
                return true;
            }

            return TryComputeDesiredFacingToWorldPos(grid.GetTileCenter(aimTile), out angleDeg);
        }

        /// <summary>
        /// Screen-space facing for the upright expanding icon. Same WorldToScreenPoint / Y-flip math as mortar/flak
        /// shells, then <see cref="NorthFacingTextureOffsetDeg"/> so north-facing AT_Gun art points along the shot.
        /// </summary>
        private bool TryComputeDesiredFacingToWorldPos(Vector3 to, out float angleDeg)
        {
            angleDeg = currentFacingAngleDeg;
            Camera cam = Find.WorldCamera;
            if (cam == null) return false;

            Vector3 from = DrawPos;
            Vector3 s0 = cam.WorldToScreenPoint(from);
            Vector3 s1 = cam.WorldToScreenPoint(to);
            if (s0.z <= 0f || s1.z <= 0f) return false;

            Vector2 d = new Vector2(s1.x - s0.x, s1.y - s0.y);
            if (d.sqrMagnitude < 0.25f) return false;

            // WorldToScreenPoint is Y-up; expanding icons draw in Y-down UI space — flip Y so up/down match.
            float eastFacingDeg = Mathf.Atan2(-d.y, d.x) * Mathf.Rad2Deg;
            angleDeg = eastFacingDeg + NorthFacingTextureOffsetDeg;
            return true;
        }
    }
}
