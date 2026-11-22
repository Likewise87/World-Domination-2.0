using System;
using RimWorld;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Scoped ambient flag for scout FindPath only. While active, hostile spike-trap tiles get a large
    /// road-mult penalty so A* prefers detours. Does not make traps impassable (still reachable if boxed in).
    /// </summary>
    public static class WdScoutTrapAvoidPathContext
    {
        [ThreadStatic] private static int depth;
        [ThreadStatic] private static Faction faction;

        public static bool Active => depth > 0;

        public static Faction MoverFaction => faction;

        /// <summary>Flat difficulty units added for entering a hostile trap tile (lethal vs typical scout strength).</summary>
        public const float HostileTrapFlatPenalty = 80f;

        public static bool ShouldAvoidFor(WorldObject_Traveler traveler) =>
            traveler != null
            && (traveler.mission == TravelerMission.Scout || traveler.isNimbleScout);

        public static Scope Activate(Faction moverFaction) => new Scope(moverFaction);

        public sealed class Scope : IDisposable
        {
            private readonly Faction previousFaction;
            private bool disposed;

            public Scope(Faction moverFaction)
            {
                previousFaction = faction;
                faction = moverFaction;
                depth++;
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                depth = Math.Max(0, depth - 1);
                faction = depth == 0 ? null : previousFaction;
            }
        }
    }
}
