using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    public static class Raid_DefenseCooldownReservations
    {
        public static int ApplyRaidDefenseCooldownReservation(WorldObject target)
        {
            if (target == null) return -1;
            var seth = WorldDominationMod.settings;
            if (seth == null) return -1;
            var comp = target.GetComponent<CompViralSpread>();
            if (comp == null) return -1;

            float days = CompViralSpread.GetDefenseCooldownDaysFor(target);
            comp.defenseCooldownTick = Find.TickManager.TicksGame + CompViralSpread.CooldownTicksFromDays(days);
            return comp.defenseCooldownTick;
        }

        public static void ReleaseRaidDefenseCooldownReservation(WorldObject target, int reservedUntilTick)
        {
            if (target == null || reservedUntilTick <= 0) return;
            var comp = target.GetComponent<CompViralSpread>();
            if (comp == null) return;
            if (comp.defenseCooldownTick == reservedUntilTick)
                comp.defenseCooldownTick = -1;
        }
    }
}
