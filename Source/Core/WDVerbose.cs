using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Action / branch logging when mod settings → Verbose logging is on.
    /// For timing spikes use <see cref="WD_DevPerformanceSpikeLog"/> (<c>[WD Perf]</c>); this uses <c>[WD]</c> for narrative detail.
    /// </summary>
    public static class WDVerbose
    {
        public static void Msg(string message)
        {
            try
            {
                var s = WorldDominationMod.settings;
                if (s == null || !s.verboseLogging) return;
                // Find.TickManager NREs when Current.Game is null (e.g. StaticConstructorOnStartup).
                int t = Current.Game?.tickManager != null ? Current.Game.tickManager.TicksGame : -1;
                Log.Message($"[WD] tick={t} {message}");
            }
            catch
            {
                // Never let verbose logging break static ctors / early load.
            }
        }

        public static void MsgNoTick(string message)
        {
            try
            {
                var s = WorldDominationMod.settings;
                if (s == null || !s.verboseLogging) return;
                Log.Message($"[WD] {message}");
            }
            catch
            {
            }
        }

        /// <summary>KCSG remap, ore rolls, mining shelf fill — same gate as <see cref="Msg"/>.</summary>
        public static void Remap(string message)
        {
            try
            {
                var s = WorldDominationMod.settings;
                if (s == null || !s.verboseLogging) return;
                int t = Current.Game?.tickManager != null ? Current.Game.tickManager.TicksGame : -1;
                Log.Message($"[WD Remap] tick={t} {message}");
            }
            catch
            {
            }
        }

        public static void RemapNoTick(string message)
        {
            try
            {
                var s = WorldDominationMod.settings;
                if (s == null || !s.verboseLogging) return;
                Log.Message($"[WD Remap] {message}");
            }
            catch
            {
            }
        }
    }
}
