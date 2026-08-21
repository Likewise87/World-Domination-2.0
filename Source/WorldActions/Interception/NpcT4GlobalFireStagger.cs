using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Keeps NPC tier-4 settlement mortar / AA from volleying in lockstep.
    /// Two ints + a pending counter; no lists, no per-settlement state.
    /// Player mortar outposts are unaffected.
    /// </summary>
    internal static class NpcT4GlobalFireStagger
    {
        /// <summary>3 seconds at normal speed (60 ticks/s).</summary>
        public const int IntervalTicks = 180;

        private static int nextMortarFireTick;
        private static int nextAaFireTick;
        private static int pendingNpcAaEngages;

        public static bool IsMortarSlotOpen()
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            return now >= nextMortarFireTick;
        }

        /// <summary>Claim the global mortar slot. Call only when about to fire.</summary>
        public static bool TryClaimMortarFire()
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            if (now < nextMortarFireTick) return false;
            nextMortarFireTick = now + IntervalTicks;
            return true;
        }

        /// <summary>True when no NPC AA engage is pending and the global AA slot is open.</summary>
        public static bool CanQueueNpcAa()
        {
            if (pendingNpcAaEngages > 0) return false;
            int now = Find.TickManager?.TicksGame ?? 0;
            return now >= nextAaFireTick;
        }

        public static void NotifyNpcAaQueued() => pendingNpcAaEngages++;

        /// <summary>
        /// Drop the pending count; if the engage actually fired, start the global AA cooldown.
        /// </summary>
        public static void NotifyNpcAaEngageEnded(bool fired)
        {
            if (pendingNpcAaEngages > 0)
                pendingNpcAaEngages--;
            if (!fired) return;
            int now = Find.TickManager?.TicksGame ?? 0;
            nextAaFireTick = now + IntervalTicks;
        }
    }
}
