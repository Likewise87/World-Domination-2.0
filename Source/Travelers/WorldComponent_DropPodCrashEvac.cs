using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Crash-evac ambush prime: skip SettlementAmbush coin flip once for reformed caravans.</summary>
    public class WorldComponent_DropPodCrashEvac : WorldComponent
    {
        private const int PrimeDurationTicks = 90000; // 1.5 days
        private Dictionary<int, int> primedExpireTick = new Dictionary<int, int>();

        public WorldComponent_DropPodCrashEvac(World world) : base(world) { }

        public static WorldComponent_DropPodCrashEvac Get()
            => Find.World?.GetComponent<WorldComponent_DropPodCrashEvac>();

        public void Prime(Caravan caravan)
        {
            if (caravan == null || caravan.Destroyed) return;
            primedExpireTick[caravan.ID] = Find.TickManager.TicksGame + PrimeDurationTicks;
        }

        public bool IsPrimed(Caravan caravan)
        {
            if (caravan == null || caravan.Destroyed) return false;
            if (!primedExpireTick.TryGetValue(caravan.ID, out int expire)) return false;
            if (Find.TickManager.TicksGame > expire)
            {
                primedExpireTick.Remove(caravan.ID);
                return false;
            }
            return true;
        }

        public void Clear(Caravan caravan)
        {
            if (caravan == null) return;
            primedExpireTick.Remove(caravan.ID);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref primedExpireTick, "primedExpireTick", LookMode.Value, LookMode.Value);
            if (primedExpireTick == null) primedExpireTick = new Dictionary<int, int>();
        }
    }
}
