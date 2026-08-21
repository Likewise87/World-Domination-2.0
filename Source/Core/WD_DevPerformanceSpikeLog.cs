using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Correlation logs for isolating periodic world-map spikes (timing / "how long"). Emits only when mod settings → Verbose logging is on.
    /// For action-level branch detail use <see cref="WDVerbose"/> (<c>[WD]</c>). Prefix perf lines with <see cref="Tag"/> and filter Player.log.
    /// </summary>
    public static class WD_DevPerformanceSpikeLog
    {
        public const string Tag = "[WD Perf]";

        public static void Msg(string message)
        {
            var s = WorldDominationMod.settings;
            if (s == null || !s.verboseLogging) return;
            int t = Find.TickManager?.TicksGame ?? -1;
            Log.Message($"{Tag} tick={t} {message}");
        }
    }
}
