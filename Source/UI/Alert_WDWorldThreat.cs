using System.Collections.Generic;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Silent alert for strongest Near and Far reachable raids (raw + storyteller-band clamp).
    /// </summary>
    public class Alert_WDWorldThreat : Alert
    {
        private readonly StringBuilder explanationScratch = new StringBuilder();
        private readonly List<ThreatSettlementEntry> nearbyScratch = new List<ThreatSettlementEntry>();
        private readonly List<ThreatSettlementEntry> farScratch = new List<ThreatSettlementEntry>();
        private int resolvedFrame = -1;
        private ThreatSettlementEntry? cachedNear;
        private ThreatSettlementEntry? cachedFar;

        public Alert_WDWorldThreat()
        {
            defaultPriority = AlertPriority.Medium;
        }

        private static WorldComponent_SpreadManager Manager =>
            Current.ProgramState == ProgramState.Playing ? Find.World?.GetComponent<WorldComponent_SpreadManager>() : null;

        public static string TierLabel(WorldThreatTier tier)
        {
            switch (tier)
            {
                case WorldThreatTier.Low: return "TSA_WD_WorldThreatTier_Low".Translate();
                case WorldThreatTier.Moderate: return "TSA_WD_WorldThreatTier_Moderate".Translate();
                case WorldThreatTier.Heightened: return "TSA_WD_WorldThreatTier_Heightened".Translate();
                case WorldThreatTier.High: return "TSA_WD_WorldThreatTier_High".Translate();
                case WorldThreatTier.Critical: return "TSA_WD_WorldThreatTier_Critical".Translate();
                default: return "TSA_WD_WorldThreatTier_None".Translate();
            }
        }

        public override AlertPriority Priority
        {
            get
            {
                WorldComponent_SpreadManager m = Manager;
                if (m == null) return AlertPriority.Medium;
                if (m.CurrentWorldThreatTier == WorldThreatTier.High || m.CurrentWorldThreatTier == WorldThreatTier.Critical)
                    return AlertPriority.High;

                ResolveSidesCached(m, out ThreatSettlementEntry? near, out _);
                if (near.HasValue)
                {
                    Map clampMap = m.WorldThreatColony?.Map ?? Find.CurrentMap;
                    float landing = WorldThreatDisplay.LandingPoints(near.Value, clampMap);
                    float baseline = m.WorldThreatBaseline > 0f ? m.WorldThreatBaseline : 0f;
                    if (WorldThreatDisplay.NearLandingIsHeightenedOrWorse(landing, baseline))
                        return AlertPriority.High;
                }
                return AlertPriority.Medium;
            }
        }

        public override string GetLabel()
        {
            WorldComponent_SpreadManager m = Manager;
            if (m == null) return "";
            if (!TryResolveBlend(m, out float nearPts, out float farPts, out bool hasNear, out bool hasFar))
                return "";

            return WorldThreatDisplay.BuildAlertLabel(nearPts, farPts, hasNear, hasFar);
        }

        public override TaggedString GetExplanation()
        {
            WorldComponent_SpreadManager m = Manager;
            if (m == null) return "";

            ResolveSidesCached(m, out ThreatSettlementEntry? near, out ThreatSettlementEntry? far);
            if (!near.HasValue && !far.HasValue) return "";

            Map clampMap = m.WorldThreatColony?.Map ?? Find.CurrentMap;
            float baseline = m.WorldThreatBaseline > 0f ? m.WorldThreatBaseline : 0f;

            RaidPointsHelper.TryGetActiveClampPercents(out int minPct, out int maxPct, out string bandLabel);

            float nearRaw = near.HasValue ? near.Value.rawStrength : 0f;
            float nearClamped = near.HasValue ? WorldThreatDisplay.LandingPoints(near.Value, clampMap) : 0f;
            float farRaw = far.HasValue ? far.Value.rawStrength : 0f;
            float farClamped = far.HasValue ? WorldThreatDisplay.LandingPoints(far.Value, clampMap) : 0f;

            explanationScratch.Clear();
            explanationScratch.AppendLine("TSA_WD_Alert_WorldThreatDesc_Storyteller".Translate(baseline.ToString("F0")));
            if (!string.IsNullOrEmpty(bandLabel))
                explanationScratch.AppendLine("TSA_WD_Alert_WorldThreatDesc_Clamp_Staged".Translate(minPct, maxPct, bandLabel));
            else
                explanationScratch.AppendLine("TSA_WD_Alert_WorldThreatDesc_Clamp".Translate(minPct, maxPct));
            explanationScratch.AppendLine();
            explanationScratch.AppendLine("TSA_WD_Alert_WorldThreatDesc_NearRaid".Translate(
                nearRaw.ToString("F0"), nearClamped.ToString("F0")));
            explanationScratch.AppendLine("TSA_WD_Alert_WorldThreatDesc_FarRaid".Translate(
                farRaw.ToString("F0"), farClamped.ToString("F0")));
            explanationScratch.AppendLine();
            explanationScratch.AppendLine("TSA_WD_Alert_WorldThreatDesc_NearFarDefs".Translate());
            explanationScratch.AppendLine();
            explanationScratch.Append("TSA_WD_Alert_WorldThreatDesc_NearPreferred".Translate());
            return explanationScratch.ToString();
        }

        public override AlertReport GetReport()
        {
            if (Current.ProgramState != ProgramState.Playing) return false;
            if (WorldDominationMod.settings == null || !WorldDominationMod.settings.notifyThreatLevel) return false;
            WorldComponent_SpreadManager m = Manager;
            if (m == null || m.CurrentWorldThreatTier == WorldThreatTier.None) return false;

            ResolveSidesCached(m, out ThreatSettlementEntry? near, out ThreatSettlementEntry? far);
            if (!near.HasValue && !far.HasValue) return false;

            Settlement jump = near.HasValue ? near.Value.settlement : far.Value.settlement;
            if (jump != null)
                return AlertReport.CulpritIs(jump);
            GlobalTargetInfo culprit = m.WorldThreatScariest;
            return culprit.IsValid ? AlertReport.CulpritIs(culprit) : AlertReport.Active;
        }

        /// <summary>
        /// ResolveAlertSides is called from Priority, GetLabel, GetExplanation and GetReport within the same
        /// AlertsReadout pass. Memoize per frame so it runs once instead of up to four times per check.
        /// </summary>
        private void ResolveSidesCached(WorldComponent_SpreadManager m, out ThreatSettlementEntry? near, out ThreatSettlementEntry? far)
        {
            int frame = Time.frameCount;
            if (frame == resolvedFrame)
            {
                near = cachedNear;
                far = cachedFar;
                return;
            }
            WorldThreatDisplay.ResolveAlertSides(m, nearbyScratch, farScratch, out near, out far);
            resolvedFrame = frame;
            cachedNear = near;
            cachedFar = far;
        }

        private bool TryResolveBlend(
            WorldComponent_SpreadManager m,
            out float nearPts,
            out float farPts,
            out bool hasNear,
            out bool hasFar)
        {
            nearPts = 0f;
            farPts = 0f;
            hasNear = false;
            hasFar = false;
            ResolveSidesCached(m, out ThreatSettlementEntry? near, out ThreatSettlementEntry? far);
            Map clampMap = m.WorldThreatColony?.Map ?? Find.CurrentMap;
            if (near.HasValue)
            {
                hasNear = true;
                nearPts = WorldThreatDisplay.LandingPoints(near.Value, clampMap);
            }
            if (far.HasValue)
            {
                hasFar = true;
                farPts = WorldThreatDisplay.LandingPoints(far.Value, clampMap);
            }
            return hasNear || hasFar;
        }
    }
}
