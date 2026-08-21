using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Nearby vs Far threat split for the WD dashboard and world-threat alert.
    /// Per settlement: Nearby = attack-range bands 0+1 (inner 50% of that settlement's R); Far = bands 2+3.
    /// </summary>
    public static class WorldThreatDisplay
    {
        /// <summary>Heightened tier starts at ratio 1.20 (see world-threat ClassifyRaw).</summary>
        private const float HeightenedRatioMin = 1.20f;

        public static void Partition(
            IReadOnlyList<ThreatSettlementEntry> source,
            WorldComponent_SpreadManager manager,
            List<ThreatSettlementEntry> nearbyOut,
            List<ThreatSettlementEntry> farOut)
        {
            nearbyOut?.Clear();
            farOut?.Clear();
            if (source == null || nearbyOut == null || farOut == null) return;

            var seth = WorldDominationMod.settings;
            for (int i = 0; i < source.Count; i++)
            {
                ThreatSettlementEntry e = source[i];
                if (e.settlement == null) continue;
                float raidRange = SettlementAttackRangeUtil.GetNpcSettlementAttackRangeWithZeal(e.settlement, seth, manager);
                int band = WD_TargetDistanceBandOrder.BandIndex(e.tilesToColony, raidRange);
                if (WD_TargetDistanceBandOrder.IsNearbyBand(band))
                    nearbyOut.Add(e);
                else
                    farOut.Add(e);
            }

            nearbyOut.Sort(CompareNearby);
            farOut.Sort(CompareFar);
        }

        private static int CompareNearby(ThreatSettlementEntry a, ThreatSettlementEntry b)
        {
            int byTiles = a.tilesToColony.CompareTo(b.tilesToColony);
            if (byTiles != 0) return byTiles;
            return b.rawStrength.CompareTo(a.rawStrength);
        }

        private static int CompareFar(ThreatSettlementEntry a, ThreatSettlementEntry b) =>
            b.rawStrength.CompareTo(a.rawStrength);

        public static bool TryGetStrongest(List<ThreatSettlementEntry> list, out ThreatSettlementEntry entry)
        {
            entry = default;
            if (list == null || list.Count == 0) return false;
            int best = 0;
            for (int i = 1; i < list.Count; i++)
            {
                if (list[i].rawStrength > list[best].rawStrength)
                    best = i;
            }
            entry = list[best];
            return entry.settlement != null;
        }

        /// <summary>
        /// Resolves strongest Nearby and strongest Far entries for the alert blend.
        /// </summary>
        public static void ResolveAlertSides(
            WorldComponent_SpreadManager manager,
            List<ThreatSettlementEntry> nearbyScratch,
            List<ThreatSettlementEntry> farScratch,
            out ThreatSettlementEntry? near,
            out ThreatSettlementEntry? far)
        {
            near = null;
            far = null;
            if (manager?.ThreatSettlements == null) return;

            Partition(manager.ThreatSettlements, manager, nearbyScratch, farScratch);
            if (TryGetStrongest(nearbyScratch, out ThreatSettlementEntry n))
                near = n;
            if (TryGetStrongest(farScratch, out ThreatSettlementEntry f))
                far = f;
        }

        public static float LandingPoints(ThreatSettlementEntry entry, Map clampMap)
        {
            return RaidPointsHelper.ClampRaidPointsToStorytellerBand(entry.rawStrength, clampMap);
        }

        public static bool NearLandingIsHeightenedOrWorse(float nearLanding, float baseline)
        {
            if (baseline <= 0f || nearLanding <= 0f) return false;
            return nearLanding / baseline >= HeightenedRatioMin;
        }

        public static string BuildNearbySubheaderTip() =>
            "TSA_WD_Dash_NearbyThreatsTip".Translate().ToString();

        public static string BuildFarSubheaderTip() =>
            "TSA_WD_Dash_FarThreatsTip".Translate().ToString();

        /// <summary>
        /// One-row silent-alert label: near landing | far landing (0 when that side is empty).
        /// </summary>
        public static string BuildAlertLabel(float nearPts, float farPts, bool hasNear, bool hasFar)
        {
            string near = hasNear ? nearPts.ToString("F0") : "0";
            string far = hasFar ? farPts.ToString("F0") : "0";
            return "TSA_WD_Alert_WorldThreat_NearFar".Translate(near, far).ToString();
        }
    }
}
