using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Keeps MG-nest gunners on their assigned mannable turret.
    /// Vanilla <see cref="LordJob_ManTurrets"/> / ManClosestTurret requires shell ammo and
    /// <see cref="Building_TurretGun"/>, so it never sticks on CE M240 (<c>Building_TurretGunCE</c>).
    /// These pawns intentionally have no lord so settlement defense AI cannot walk them off.
    /// </summary>
    public class WdMgNestGunnerKeeper : MapComponent
    {
        private const int RecheckInterval = 15;

        private List<Entry> entries = new List<Entry>();

        public WdMgNestGunnerKeeper(Map map) : base(map) { }

        public static void Register(Map map, Pawn gunner, Building_Turret turret)
        {
            if (map == null || gunner == null || turret == null) return;
            WdMgNestGunnerKeeper keeper = Ensure(map);
            for (int i = 0; i < keeper.entries.Count; i++)
            {
                if (keeper.entries[i].pawn == gunner)
                {
                    keeper.entries[i].turret = turret;
                    ForceStickyMan(gunner, turret);
                    return;
                }
            }
            keeper.entries.Add(new Entry { pawn = gunner, turret = turret });
            ForceStickyMan(gunner, turret);
        }

        private static WdMgNestGunnerKeeper Ensure(Map map)
        {
            WdMgNestGunnerKeeper existing = map.GetComponent<WdMgNestGunnerKeeper>();
            if (existing != null) return existing;
            var created = new WdMgNestGunnerKeeper(map);
            map.components.Add(created);
            return created;
        }

        public override void MapComponentTick()
        {
            if (!map.IsHashIntervalTick(RecheckInterval)) return;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                Entry e = entries[i];
                if (e.pawn == null || e.pawn.Destroyed || e.pawn.Dead || !e.pawn.Spawned || e.pawn.Map != map
                    || e.turret == null || e.turret.Destroyed || !e.turret.Spawned || e.turret.Map != map)
                {
                    entries.RemoveAt(i);
                    continue;
                }
                if (e.pawn.Downed || e.pawn.InMentalState) continue;
                ForceStickyMan(e.pawn, e.turret);
            }
        }

        private static void ForceStickyMan(Pawn gunner, Building_Turret turret)
        {
            // Settlement / raid lords will walk them off the nest — stay lordless.
            Lord lord = gunner.GetLord();
            if (lord != null)
                lord.RemovePawn(gunner);
            gunner.mindState.duty = null;

            if (gunner.Downed || gunner.Dead || !gunner.Spawned) return;
            if (turret.Destroyed || !turret.Spawned) return;

            Job cur = gunner.CurJob;
            if (cur != null && cur.def == JobDefOf.ManTurret && cur.targetA.Thing == turret)
                return;

            Job job = JobMaker.MakeJob(JobDefOf.ManTurret, turret);
            job.expiryInterval = 999999;
            job.checkOverrideOnExpire = false;
            gunner.jobs.StartJob(job, JobCondition.InterruptForced);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref entries, "entries", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && entries == null)
                entries = new List<Entry>();
        }

        private class Entry : IExposable
        {
            public Pawn? pawn;
            public Building_Turret? turret;

            public void ExposeData()
            {
                Scribe_References.Look(ref pawn, "pawn");
                Scribe_References.Look(ref turret, "turret");
            }
        }
    }
}
