using System;
using System.Collections.Generic;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Raid/trader target ordering by equal-quarter distance bands of max range R.
    /// Weighted preferred-band pick (closer heavier), shuffle inside band, then closer bands, then farther.
    /// </summary>
    public static class WD_TargetDistanceBandOrder
    {
        public const int BandCount = 4;

        /// <summary>Preferred-band weights for bands 0..3 (closer higher). Sum = 7.5.</summary>
        public static readonly float[] PreferredBandWeights = { 3f, 2f, 1.5f, 1f };

        /// <summary>
        /// Equal quarters of <paramref name="maxRange"/>: 0–25%, 25–50%, 50–75%, 75–100%.
        /// Dist beyond maxRange clamps to band 3.
        /// </summary>
        public static int BandIndex(float dist, float maxRange)
        {
            if (maxRange <= 0.001f) return 0;
            float t = dist / maxRange;
            if (t <= 0.25f) return 0;
            if (t <= 0.50f) return 1;
            if (t <= 0.75f) return 2;
            return 3;
        }

        /// <summary>True when band is Nearby for dashboard (inner 50% of R).</summary>
        public static bool IsNearbyBand(int bandIndex) => bandIndex <= 1;

        /// <summary>
        /// Reorders <paramref name="list"/> in place. Returns preferred band index (-1 if list empty/skipped).
        /// </summary>
        public static int OrderWeightedPreferredThenCloserThenFarther<T>(
            List<T> list,
            Func<T, float> getDist,
            float maxRange,
            Action<List<T>> shuffleBand)
        {
            if (list == null || list.Count == 0 || getDist == null || shuffleBand == null)
                return -1;
            if (list.Count == 1)
                return BandIndex(getDist(list[0]), maxRange);

            var bands = new List<T>[BandCount];
            for (int b = 0; b < BandCount; b++)
                bands[b] = new List<T>(4);

            for (int i = 0; i < list.Count; i++)
            {
                T item = list[i];
                int bi = BandIndex(getDist(item), maxRange);
                bands[bi].Add(item);
            }

            var nonEmpty = new List<int>(BandCount);
            for (int b = 0; b < BandCount; b++)
            {
                if (bands[b].Count > 0)
                    nonEmpty.Add(b);
            }
            if (nonEmpty.Count == 0) return -1;

            int preferred = PickWeightedPreferredBand(nonEmpty);
            var ordered = new List<T>(list.Count);
            AppendShuffledBand(ordered, bands[preferred], shuffleBand);

            for (int b = preferred - 1; b >= 0; b--)
                AppendShuffledBand(ordered, bands[b], shuffleBand);
            for (int b = preferred + 1; b < BandCount; b++)
                AppendShuffledBand(ordered, bands[b], shuffleBand);

            list.Clear();
            list.AddRange(ordered);
            return preferred;
        }

        private static int PickWeightedPreferredBand(List<int> nonEmpty)
        {
            float sum = 0f;
            for (int i = 0; i < nonEmpty.Count; i++)
                sum += PreferredBandWeights[nonEmpty[i]];
            float roll = Rand.Value * sum;
            float acc = 0f;
            for (int i = 0; i < nonEmpty.Count; i++)
            {
                acc += PreferredBandWeights[nonEmpty[i]];
                if (roll <= acc)
                    return nonEmpty[i];
            }
            return nonEmpty[nonEmpty.Count - 1];
        }

        private static void AppendShuffledBand<T>(List<T> dest, List<T> band, Action<List<T>> shuffleBand)
        {
            if (band == null || band.Count == 0) return;
            shuffleBand(band);
            dest.AddRange(band);
        }

        /// <summary>Player-facing band suffix for logs. preferred/chosen are 0-based indices.</summary>
        public static string FormatBandPickMessage(int preferredBand, int chosenBand)
        {
            if (preferredBand < 0) return "";
            // Display as Band 1..4 for players
            int prefDisp = preferredBand + 1;
            if (chosenBand < 0 || chosenBand == preferredBand)
                return "TSA_WD_Log_Raid_BandPicked".Translate(prefDisp).ToString();
            int chosenDisp = chosenBand + 1;
            return "TSA_WD_Log_Raid_BandFallback".Translate(prefDisp, chosenDisp).ToString();
        }
    }
}
