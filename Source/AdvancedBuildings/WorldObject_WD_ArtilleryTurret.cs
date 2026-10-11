using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// NPC artillery turret owned by a settlement (Mortar Turret / AA Turret). Has strength like an AT turret,
    /// fights caravans that enter its tile, takes mortar chips, and never regenerates.
    /// Settlements themselves never fire; these turrets are the only NPC mortar/AA shooters.
    /// </summary>
    public abstract class WorldObject_WD_ArtilleryTurret : WorldObject_WD_AdvancedBuilding, IDefensiveInterceptor
    {
        public float strength = -1f;
        public float maxStrength = -1f;
        public int cooldownTick = -99999;

        public static float ConfiguredStrength =>
            Mathf.Max(1f, WorldDominationMod.settings?.artilleryTurretStrength ?? WorldDominationSettings.DefArtilleryTurretStrength);

        public float MaxStrength => maxStrength > 0f ? maxStrength : ConfiguredStrength;

        public bool IsOnCooldown => (Find.TickManager?.TicksGame ?? 0) < cooldownTick;

        public int CooldownTicksLeft => Mathf.Max(0, cooldownTick - (Find.TickManager?.TicksGame ?? 0));

        protected abstract bool MasterToggleOn { get; }
        protected abstract int CooldownDurationTicks { get; }
        public abstract float RangeTiles { get; }

        /// <summary>Owner alive and non-player, master toggle on, owner faction meets the NPC artillery tech level. No tier requirement.</summary>
        public bool IsFiringEnabled
        {
            get
            {
                if (Destroyed || !Spawned) return false;
                if (Faction == null || Faction.IsPlayer) return false;
                if (IsOrphan) return false;
                if (!MasterToggleOn) return false;
                TechLevel minTech = WorldDominationMod.settings?.npcT4MortarMinTechLevel ?? WorldDominationSettings.DefNpcT4MortarMinTechLevel;
                if (Faction.def != null && Faction.def.techLevel < minTech) return false;
                return true;
            }
        }

        public void InitArtillery()
        {
            maxStrength = ConfiguredStrength;
            strength = maxStrength;
        }

        /// <summary>Start (or restart) the fire cooldown. Call before <c>WorldObjects.Add</c> on spawn so new turrets never snap-fire.</summary>
        public void ApplyCooldown()
        {
            cooldownTick = (Find.TickManager?.TicksGame ?? 0) + Mathf.Max(1, CooldownDurationTicks);
        }

        /// <summary>Flat strength chip; destroys at zero.</summary>
        public bool ApplyDamage(float amount)
        {
            if (amount <= 0f || Destroyed) return false;
            strength = Mathf.Max(0f, strength - amount);
            if (strength > 0.01f) return false;
            suppressDestroyedLetter = true;
            Destroy();
            return true;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref strength, "artStrength", -1f);
            Scribe_Values.Look(ref maxStrength, "artMaxStrength", -1f);
            Scribe_Values.Look(ref cooldownTick, "artCooldownTick", -99999);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (maxStrength <= 0f) maxStrength = ConfiguredStrength;
                if (strength < 0f) strength = maxStrength;
                RefreshInterceptorRegistration();
            }
        }

        public override void SpawnSetup()
        {
            base.SpawnSetup();
            RefreshInterceptorRegistration();
        }

        public override void PostAdd()
        {
            base.PostAdd();
            if (maxStrength <= 0f) maxStrength = ConfiguredStrength;
            if (strength < 0f) strength = maxStrength;
            RefreshInterceptorRegistration();
        }

        public override void PostRemove()
        {
            WorldComponent_InterceptionScheduler.Current?.UnregisterInterceptor(this);
            base.PostRemove();
        }

        public override void Destroy()
        {
            WorldComponent_InterceptionScheduler.Current?.UnregisterInterceptor(this);
            base.Destroy();
        }

        public void RefreshInterceptorRegistration()
        {
            var sched = WorldComponent_InterceptionScheduler.Current;
            if (sched == null) return;
            if (!Destroyed)
                sched.RegisterInterceptor(this);
            else
                sched.UnregisterInterceptor(this);
            WorldComponent_SettlementWatchIndex.Get()?.Invalidate();
        }

        protected static WorldComponent_SpreadManager LiveSpreadManager =>
            Find.World?.GetComponent<WorldComponent_SpreadManager>();

        // --- IDefensiveInterceptor ---
        WorldObject IDefensiveInterceptor.Self => this;
        PlanetTile IDefensiveInterceptor.InterceptorTile => Tile;
        Faction IDefensiveInterceptor.InterceptorFaction => Faction;
        float IDefensiveInterceptor.InterceptorRange => IsFiringEnabled ? RangeTiles : 0f;
        MissionMask IDefensiveInterceptor.InterceptorMissionMask => IsFiringEnabled ? FireMask : MissionMask.None;
        bool IDefensiveInterceptor.InterceptorCanFireNow() => IsFiringEnabled && !IsOnCooldown && GlobalSlotOpen;
        bool IDefensiveInterceptor.InterceptorCanTargetPlayer => CanTargetPlayerNow();
        void IDefensiveInterceptor.InterceptorFire(WorldObject_Traveler target, float approxTileDist) => FireAtTraveler(target, approxTileDist);
        void IDefensiveInterceptor.InterceptorNoTargetFire() => FireIdle();

        protected abstract MissionMask FireMask { get; }
        protected abstract bool GlobalSlotOpen { get; }
        protected abstract bool CanTargetPlayerNow();
        protected abstract void FireAtTraveler(WorldObject_Traveler target, float approxTileDist);
        protected virtual void FireIdle() { }

        protected override void AppendInspectExtraLines(StringBuilder sb)
        {
            sb.AppendLine("TSA_WD_AdvBuild_Inspect_Strength".Translate(strength.ToString("F0"), MaxStrength.ToString("F0")).ToString());
            if (!IsOnCooldown) return;
            float days = CooldownTicksLeft / (float)GenDate.TicksPerDay;
            sb.AppendLine("TSA_WD_AdvBuild_Inspect_ReloadingDays".Translate(days.ToString("F1")).ToString());
        }

        public override System.Collections.Generic.IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos())
                yield return g;
            foreach (Gizmo g in RadiusHoverGizmos.GetForArtilleryTurret(this))
                yield return g;
        }
    }

    /// <summary>NPC Mortar Turret: auto-fires at hostile ground travelers / player caravans, idle-shells static targets.</summary>
    public class WorldObject_WD_MortarTurret : WorldObject_WD_ArtilleryTurret
    {
        protected override bool MasterToggleOn =>
            WorldDominationMod.settings?.enableNpcT4Mortar ?? WorldDominationSettings.DefEnableNpcT4Mortar;

        protected override int CooldownDurationTicks
        {
            get
            {
                float days = Mathf.Max(0.1f, WorldDominationMod.settings?.npcMortarCooldownDays ?? WorldDominationSettings.DefNpcMortarCooldownDays);
                return Mathf.RoundToInt(days * GenDate.TicksPerDay);
            }
        }

        public override float RangeTiles =>
            Mathf.Max(1f, WorldDominationMod.settings?.npcMortarRange ?? WorldDominationSettings.DefNpcMortarRange);

        protected override MissionMask FireMask => MissionMask.All;
        protected override bool GlobalSlotOpen => NpcT4GlobalFireStagger.IsMortarSlotOpen();

        protected override bool CanTargetPlayerNow() =>
            WdEscalation.CanTargetPlayerWithT4Mortar(WorldDominationMod.settings, WdEscalation.GetCachedStage(LiveSpreadManager));

        protected override void FireAtTraveler(WorldObject_Traveler target, float approxTileDist)
        {
            if (target == null || target.Destroyed) return;
            if (AntiAirFireUtils.IsAirborneAaTarget(target)) return;
            if (!WorldObject_Traveler.IsRaidMission(target.mission) && MortarTurretTargeting.ShouldReserveShell(this))
                return;
            MortarFireUtils.FireFromNpcMortarTurret(this, target, approxTileDist);
        }

        /// <summary>Scheduler caravan path (player caravans; escalation gate checked by the scheduler).</summary>
        public void InterceptorFireAtCaravan(Caravan target, float approxTileDist)
        {
            if (!IsFiringEnabled || IsOnCooldown) return;
            if (!CanTargetPlayerNow()) return;
            if (MortarTurretTargeting.ShouldReserveShell(this)) return;
            MortarFireUtils.FireFromNpcMortarTurret(this, target, approxTileDist);
        }

        protected override void FireIdle()
        {
            if (!IsFiringEnabled || IsOnCooldown || !NpcT4GlobalFireStagger.IsMortarSlotOpen()) return;
            WorldObject target = MortarTurretTargeting.FindIdleTarget(this, CanTargetPlayerNow(), out float dist);
            if (target == null) return;
            if (!(target is WorldObject_Traveler t && WorldObject_Traveler.IsRaidMission(t.mission))
                && MortarTurretTargeting.ShouldReserveShell(this))
                return;
            MortarFireUtils.FireFromNpcMortarTurret(this, target, dist);
        }
    }

    /// <summary>NPC AA Turret: engages hostile drop pods, gravships, mortar shells, and transport pods in AA range.</summary>
    public class WorldObject_WD_AaTurret : WorldObject_WD_ArtilleryTurret
    {
        protected override bool MasterToggleOn =>
            WorldDominationMod.settings?.enableNpcT4AntiAir ?? WorldDominationSettings.DefEnableNpcT4AntiAir;

        protected override int CooldownDurationTicks =>
            Mathf.RoundToInt(AntiAirFireUtils.GetNpcAntiAirCooldownSeconds() * 60f);

        public override float RangeTiles => AntiAirFireUtils.GetNpcAntiAirMaxRangeTiles();

        protected override MissionMask FireMask => MissionMask.Raider;
        protected override bool GlobalSlotOpen => NpcT4GlobalFireStagger.CanQueueNpcAa();

        protected override bool CanTargetPlayerNow() =>
            WdEscalation.CanTargetPlayerWithT4AntiAir(WorldDominationMod.settings, WdEscalation.GetCachedStage(LiveSpreadManager));

        protected override void FireAtTraveler(WorldObject_Traveler target, float approxTileDist)
        {
            if (target == null || target.Destroyed) return;
            if (!AntiAirFireUtils.IsAirborneAaTarget(target)) return;
            AntiAirFireUtils.TryQueueEngage(this, target);
        }
    }
}
