using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Short post-load grace so travelers and raid arrivals do not fire during world finalization.</summary>
    public static class WdPostLoadGuard
    {
        private const int DefaultGraceTicks = 300;
        private static int suppressArrivalsUntilTick = -1;

        /// <summary>Call on each world load / new game so the static grace does not stick across sessions.</summary>
        public static void Reset(int graceTicks = DefaultGraceTicks)
        {
            int ticks = Find.TickManager?.TicksGame ?? 0;
            suppressArrivalsUntilTick = ticks + graceTicks;
        }

        public static bool ShouldDeferTravelerArrival()
        {
            if (suppressArrivalsUntilTick < 0)
                return false;
            return Find.TickManager.TicksGame < suppressArrivalsUntilTick;
        }
    }
}
