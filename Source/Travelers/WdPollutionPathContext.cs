using System;
using UnityEngine;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Scoped ambient flag for WD FindPath only. While active, the road-mult Harmony postfix
    /// adds live pollution cost on edges A* already evaluates (no map-wide tile paint).
    /// </summary>
    public static class WdPollutionPathContext
    {
        [ThreadStatic] private static int depth;
        [ThreadStatic] private static float weight;

        /// <summary>True while a WD pollution-aware FindPath (or water A*) is running.</summary>
        public static bool Active => depth > 0;

        /// <summary>Cost multiplier (1 = normal avoid; higher = High-preset repath).</summary>
        public static float Weight => weight > 0f ? weight : 1f;

        /// <summary>Scales pollution exit-damage units into road-mult additive cost.</summary>
        public const float DamageToRoadMultScale = 0.025f;

        public static Scope Activate(float weightMultiplier = 1f) => new Scope(weightMultiplier);

        public sealed class Scope : IDisposable
        {
            private readonly float previousWeight;
            private bool disposed;

            public Scope(float weightMultiplier)
            {
                previousWeight = weight;
                weight = Mathf.Max(0.01f, weightMultiplier);
                depth++;
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                depth = Math.Max(0, depth - 1);
                if (depth == 0)
                    weight = 1f;
                else
                    weight = previousWeight;
            }
        }
    }
}
