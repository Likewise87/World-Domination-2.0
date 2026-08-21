using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Shared, throttled snapshot of player WD outposts for per-frame GUI consumers (right-side alerts,
    /// world-map underlays). Rebuilt at most once per <see cref="RefreshIntervalTicks"/> with a single
    /// AllWorldObjects scan, so consumers never walk the world every frame.
    ///
    /// Self-healing by design: staleness is bounded to the interval and a missed change corrects on the
    /// next rebuild. Player outposts change on the order of minutes, so ~30s lag is invisible in practice.
    /// The backwards-clock guard also forces a rebuild after loading a different save (smaller TicksGame),
    /// so no explicit load hook is required.
    /// </summary>
    public static class WdPlayerOutpostCache
    {
        /// <summary>~30s at 60 tps. Alerts tolerate this lag; outposts change far slower than this.</summary>
        private const int RefreshIntervalTicks = 1800;

        private static readonly List<WorldObject_WD_Outpost> cache = new List<WorldObject_WD_Outpost>(32);
        private static int lastRefreshTick = int.MinValue;

        /// <summary>
        /// Throttled read-only view of live player WD outposts. Consumers must not mutate the list and
        /// should still check <c>Destroyed</c> per element (a since-destroyed outpost can linger up to one interval).
        /// </summary>
        public static IReadOnlyList<WorldObject_WD_Outpost> PlayerOutposts
        {
            get
            {
                RefreshIfStale();
                return cache;
            }
        }

        /// <summary>Force a rebuild on the next read (e.g. after a known bulk topology change).</summary>
        public static void Invalidate() => lastRefreshTick = int.MinValue;

        private static void RefreshIfStale()
        {
            if (Current.ProgramState != ProgramState.Playing)
            {
                // No player outposts exist outside a running game; keep the (empty) cache and avoid a
                // frameCount/TicksGame basis mismatch.
                if (cache.Count > 0) cache.Clear();
                lastRefreshTick = int.MinValue;
                return;
            }

            int tick = Find.TickManager?.TicksGame ?? 0;
            int delta = tick - lastRefreshTick;
            // delta < 0 => clock went backwards (new save loaded) -> rebuild.
            if (delta >= 0 && delta < RefreshIntervalTicks) return;

            lastRefreshTick = tick;
            Rebuild();
        }

        private static void Rebuild()
        {
            cache.Clear();
            Faction playerFaction = Faction.OfPlayerSilentFail;
            WorldObjectsHolder worldObjects = Find.WorldObjects;
            if (playerFaction == null || worldObjects == null) return;

            List<WorldObject> all = worldObjects.AllWorldObjects;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is WorldObject_WD_Outpost wd
                    && wd.Faction == playerFaction
                    && WorldActions_Utils.IsWdSurfaceWorldObject(wd))
                    cache.Add(wd);
            }
        }
    }
}
