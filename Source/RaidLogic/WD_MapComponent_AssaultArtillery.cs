using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace TSA_WorldDomination
{
    /// <summary>One concurrent assault-artillery rain on a settlement map.</summary>
    public class AssaultArtilleryVolley : IExposable
    {
        public IntVec3 aimCell = IntVec3.Invalid;
        public ThingDef shellDef;
        public int ticksUntilNextShell;
        public int shellsRemaining;
        public int scatterRadius = WD_AssaultArtillerySupport.ScatterMin;
        public int originTile = -1;
        public CellRect ingressBand = CellRect.Empty;
        public bool hasIngressBand;

        public bool HasLockedIngressBand => hasIngressBand && ingressBand.Area > 0;

        public void ExposeData()
        {
            Scribe_Values.Look(ref aimCell, "aimCell", default(IntVec3));
            Scribe_Defs.Look(ref shellDef, "shellDef");
            Scribe_Values.Look(ref ticksUntilNextShell, "ticksUntilNextShell", 0);
            Scribe_Values.Look(ref shellsRemaining, "shellsRemaining", 0);
            Scribe_Values.Look(ref scatterRadius, "scatterRadius", WD_AssaultArtillerySupport.ScatterMin);
            Scribe_Values.Look(ref originTile, "originTile", -1);
            Scribe_Values.Look(ref hasIngressBand, "hasIngressBand", false);
            int minX = ingressBand.minX;
            int minZ = ingressBand.minZ;
            int maxX = ingressBand.maxX;
            int maxZ = ingressBand.maxZ;
            Scribe_Values.Look(ref minX, "ingressMinX", 0);
            Scribe_Values.Look(ref minZ, "ingressMinZ", 0);
            Scribe_Values.Look(ref maxX, "ingressMaxX", 0);
            Scribe_Values.Look(ref maxZ, "ingressMaxZ", 0);
            if (Scribe.mode == LoadSaveMode.LoadingVars && hasIngressBand)
                ingressBand = new CellRect(minX, minZ, maxX - minX + 1, maxZ - minZ + 1);
        }
    }

    /// <summary>Pending assault artillery rain on a settlement map (supports multiple concurrent volleys).</summary>
    public class WD_MapComponent_AssaultArtillery : MapComponent
    {
        private List<AssaultArtilleryVolley> activeVolleys = new List<AssaultArtilleryVolley>();
        private List<int> inboundShellTravelerIds = new List<int>();

        // Legacy single-volley / single-inbound fields kept for save compatibility.
        private bool pending;
        private IntVec3 aimCell;
        private ThingDef shellDef;
        private int ticksUntilNextShell;
        private int shellsRemaining;
        private int scatterRadius;
        private int originTile = -1;
        private int inboundShellTravelerId = -1;
        private CellRect ingressBand = CellRect.Empty;
        private bool hasIngressBand;

        public bool HasPendingStrike => activeVolleys.Count > 0 || HasValidInboundShell();

        public bool HasLockedIngressBand
        {
            get
            {
                for (int i = 0; i < activeVolleys.Count; i++)
                {
                    if (activeVolleys[i] != null && activeVolleys[i].HasLockedIngressBand)
                        return true;
                }
                return false;
            }
        }

        public CellRect LockedIngressBand
        {
            get
            {
                for (int i = 0; i < activeVolleys.Count; i++)
                {
                    AssaultArtilleryVolley volley = activeVolleys[i];
                    if (volley != null && volley.HasLockedIngressBand)
                        return volley.ingressBand;
                }
                return CellRect.Empty;
            }
        }

        public WD_MapComponent_AssaultArtillery(Map map) : base(map) { }

        public static WD_MapComponent_AssaultArtillery GetOrAdd(Map map)
        {
            if (map == null) return null;
            WD_MapComponent_AssaultArtillery tracker = map.GetComponent<WD_MapComponent_AssaultArtillery>();
            if (tracker != null) return tracker;
            tracker = new WD_MapComponent_AssaultArtillery(map);
            map.components.Add(tracker);
            return tracker;
        }

        public void RegisterInboundShell(WorldObject_Traveler shell)
        {
            if (shell == null) return;
            if (inboundShellTravelerIds.Contains(shell.ID)) return;
            inboundShellTravelerIds.Add(shell.ID);
        }

        public bool ClearInboundShell(int travelerId)
        {
            return inboundShellTravelerIds.Remove(travelerId);
        }

        public bool HasValidInboundShell()
        {
            PruneDeadInboundShells();
            return inboundShellTravelerIds.Count > 0;
        }

        private void PruneDeadInboundShells()
        {
            for (int i = inboundShellTravelerIds.Count - 1; i >= 0; i--)
            {
                WorldObject_Traveler shell = FindInboundShell(inboundShellTravelerIds[i]);
                if (shell == null || shell.Destroyed || !shell.assaultArtillerySupport)
                    inboundShellTravelerIds.RemoveAt(i);
            }
        }

        private static WorldObject_Traveler FindInboundShell(int travelerId)
        {
            if (travelerId < 0) return null;
            IReadOnlyList<WorldObject_Traveler> live = WorldObject_Traveler.LiveTravelers;
            for (int i = 0; i < live.Count; i++)
            {
                WorldObject_Traveler t = live[i];
                if (t != null && t.ID == travelerId)
                    return t;
            }
            return null;
        }

        public void EnqueueStrike(
            IntVec3 aim,
            ThingDef shell,
            int inboundDelayTicks,
            int shellCount,
            int scatter,
            int shellOriginTile = -1)
        {
            var volley = new AssaultArtilleryVolley
            {
                aimCell = aim,
                shellDef = shell,
                ticksUntilNextShell = Mathf.Max(0, inboundDelayTicks),
                shellsRemaining = Mathf.Max(1, shellCount),
                scatterRadius = Mathf.Clamp(scatter, WD_AssaultArtillerySupport.ScatterMin, WD_AssaultArtillerySupport.ScatterMax),
                originTile = shellOriginTile
            };
            volley.ingressBand = AssaultArtilleryIngressDirection.LockIngressBand(map, shellOriginTile);
            volley.hasIngressBand = volley.ingressBand.Area > 0;
            activeVolleys.Add(volley);
        }

        public override void MapComponentTick()
        {
            if (activeVolleys.Count == 0) return;

            for (int i = activeVolleys.Count - 1; i >= 0; i--)
            {
                AssaultArtilleryVolley volley = activeVolleys[i];
                if (volley == null || volley.shellDef == null || volley.shellsRemaining <= 0)
                {
                    activeVolleys.RemoveAt(i);
                    continue;
                }

                volley.ticksUntilNextShell--;
                if (volley.ticksUntilNextShell > 0) continue;

                FireOneShell(volley);
                volley.shellsRemaining--;
                if (volley.shellsRemaining <= 0)
                {
                    activeVolleys.RemoveAt(i);
                    continue;
                }

                volley.ticksUntilNextShell = Rand.RangeInclusive(
                    WD_AssaultArtillerySupport.ShellIntervalMinTicks,
                    WD_AssaultArtillerySupport.ShellIntervalMaxTicks);
            }
        }

        private void FireOneShell(AssaultArtilleryVolley volley)
        {
            IntVec3 impact = volley.aimCell;
            if (volley.scatterRadius > 0
                && CellFinder.TryFindRandomCellNear(volley.aimCell, map, volley.scatterRadius,
                    c => c.InBounds(map) && !c.Fogged(map), out IntVec3 near))
                impact = near;

            if (volley.shellDef != null
                && AssaultArtilleryProjectileLaunch.TryLaunchAssaultShell(
                    map,
                    volley.originTile,
                    impact,
                    volley.shellDef,
                    volley.HasLockedIngressBand ? volley.ingressBand : (CellRect?)null))
                return;

            ExplodeAt(impact, AssaultArtilleryProjectileLaunch.ResolveProjectileDef(volley.shellDef));
        }

        private void ExplodeAt(IntVec3 cell, ThingDef projectileDef)
        {
            if (projectileDef?.projectile == null)
            {
                GenExplosion.DoExplosion(cell, map, 2.9f, DamageDefOf.Bomb, null);
                return;
            }

            ProjectileProperties props = projectileDef.projectile;
            float radius = props.explosionRadius > 0.01f ? props.explosionRadius : 2.9f;
            DamageDef dmgDef = props.damageDef ?? DamageDefOf.Bomb;
            int damAmount = props.GetDamageAmount(null);
            float armorPen = props.GetArmorPenetration(null);

            GenExplosion.DoExplosion(
                cell,
                map,
                radius,
                dmgDef,
                null,
                damAmount,
                armorPen,
                props.soundExplode,
                null,
                projectileDef,
                null,
                props.postExplosionSpawnThingDef,
                props.postExplosionSpawnChance,
                props.postExplosionSpawnThingCount);

            if (props.soundImpactAnticipate != null)
                props.soundImpactAnticipate.PlayOneShot(new TargetInfo(cell, map));
        }

        public override void ExposeData()
        {
            base.ExposeData();

            if (Scribe.mode == LoadSaveMode.Saving)
            {
                // Legacy keys stay writable for save compat; clear single-slot state so it cannot
                // overwrite the multi-volley lists on the next load.
                pending = false;
                shellDef = null;
                shellsRemaining = 0;
                ticksUntilNextShell = 0;
                aimCell = IntVec3.Invalid;
                scatterRadius = WD_AssaultArtillerySupport.ScatterMin;
                originTile = -1;
                hasIngressBand = false;
                ingressBand = CellRect.Empty;
                inboundShellTravelerId = inboundShellTravelerIds != null && inboundShellTravelerIds.Count > 0
                    ? inboundShellTravelerIds[0]
                    : -1;
            }

            // Legacy single-volley fields (always Look for old saves).
            Scribe_Values.Look(ref pending, "assaultArtilleryPending", false);
            Scribe_Values.Look(ref aimCell, "assaultArtilleryAim", default(IntVec3));
            Scribe_Defs.Look(ref shellDef, "assaultArtilleryShell");
            Scribe_Values.Look(ref ticksUntilNextShell, "assaultArtilleryTicksNext", 0);
            Scribe_Values.Look(ref shellsRemaining, "assaultArtilleryShellsLeft", 0);
            Scribe_Values.Look(ref scatterRadius, "assaultArtilleryScatter", WD_AssaultArtillerySupport.ScatterMin);
            Scribe_Values.Look(ref originTile, "assaultArtilleryOriginTile", -1);
            Scribe_Values.Look(ref inboundShellTravelerId, "assaultArtilleryInboundShellId", -1);
            Scribe_Values.Look(ref hasIngressBand, "assaultArtilleryHasIngressBand", false);
            int minX = ingressBand.minX;
            int minZ = ingressBand.minZ;
            int maxX = ingressBand.maxX;
            int maxZ = ingressBand.maxZ;
            Scribe_Values.Look(ref minX, "assaultArtilleryIngressMinX", 0);
            Scribe_Values.Look(ref minZ, "assaultArtilleryIngressMinZ", 0);
            Scribe_Values.Look(ref maxX, "assaultArtilleryIngressMaxX", 0);
            Scribe_Values.Look(ref maxZ, "assaultArtilleryIngressMaxZ", 0);
            if (Scribe.mode == LoadSaveMode.LoadingVars && hasIngressBand)
                ingressBand = new CellRect(minX, minZ, maxX - minX + 1, maxZ - minZ + 1);

            Scribe_Collections.Look(ref activeVolleys, "assaultArtilleryVolleys", LookMode.Deep);
            Scribe_Collections.Look(ref inboundShellTravelerIds, "assaultArtilleryInboundShellIds", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (activeVolleys == null)
                    activeVolleys = new List<AssaultArtilleryVolley>();
                if (inboundShellTravelerIds == null)
                    inboundShellTravelerIds = new List<int>();

                MigrateLegacyPendingVolley();
                MigrateLegacyInboundShell();
            }
        }

        private void MigrateLegacyPendingVolley()
        {
            if (!pending || activeVolleys.Count > 0) return;
            if (shellDef == null || shellsRemaining <= 0)
            {
                pending = false;
                return;
            }

            activeVolleys.Add(new AssaultArtilleryVolley
            {
                aimCell = aimCell,
                shellDef = shellDef,
                ticksUntilNextShell = ticksUntilNextShell,
                shellsRemaining = shellsRemaining,
                scatterRadius = scatterRadius > 0 ? scatterRadius : WD_AssaultArtillerySupport.ScatterMin,
                originTile = originTile,
                ingressBand = ingressBand,
                hasIngressBand = hasIngressBand
            });
            pending = false;
            shellDef = null;
            shellsRemaining = 0;
            ticksUntilNextShell = 0;
            aimCell = IntVec3.Invalid;
            originTile = -1;
            hasIngressBand = false;
            ingressBand = CellRect.Empty;
        }

        private void MigrateLegacyInboundShell()
        {
            if (inboundShellTravelerId < 0) return;
            if (!inboundShellTravelerIds.Contains(inboundShellTravelerId))
                inboundShellTravelerIds.Add(inboundShellTravelerId);
            inboundShellTravelerId = -1;
        }
    }
}
