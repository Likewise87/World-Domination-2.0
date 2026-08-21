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
            var s = WorldDominationMod.settings;
            if (s == null || !s.verboseLogging) return;
            int t = Find.TickManager?.TicksGame ?? -1;
            Log.Message($"[WD] tick={t} {message}");
        }
    }
}
