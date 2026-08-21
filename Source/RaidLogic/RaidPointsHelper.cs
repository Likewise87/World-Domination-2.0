using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// WD raid incidents: attacker strength clamped to a band around the current storyteller threat baseline for the map.
    /// </summary>
    public static class RaidPointsHelper
    {
        /// <summary>
        /// Player home map with the highest storyteller threat baseline, for WD raid point bands when the incident
        /// map is not a colony (e.g. world interception encounter maps, where baseline would otherwise be near zero).
        /// </summary>
        private static Map BestPlayerHomeMapForRaidThreatBaseline()
        {
            Map best = null;
            float bestThreat = -1f;
            foreach (Map m in Find.Maps)
            {
                if (m == null || !m.IsPlayerHome) continue;
                float t = StorytellerUtility.DefaultThreatPointsNow(m);
                if (t > bestThreat)
                {
                    bestThreat = t;
                    best = m;
                }
            }
            return best;
        }

        /// <summary>Map used for storyteller baseline when clamping WD raid points (see <see cref="ClampRaidPointsToStorytellerBand"/>).</summary>
        public static Map ResolveStorytellerBaselineMapForWdRaidClamp(Map map)
        {
            if (map == null) return null;
            return map.IsPlayerHome ? map : (BestPlayerHomeMapForRaidThreatBaseline() ?? map);
        }

        /// <summary>False when settings demand raw strength as raid points (no storyteller band clamp).</summary>
        public static bool WdRaidPointsStorytellerBandClampActive()
        {
            var s = WorldDominationMod.settings;
            return s != null && !s.alwaysUseStrengthAsRaidPoints;
        }

        /// <summary>
        /// True when Early/Mid/Late stage pairs are the active clamp source
        /// (scale toggle on and Mid/Late Game master switch on).
        /// </summary>
        public static bool UsesEscalationStageClampBands(WorldDominationSettings s = null)
        {
            s ??= WorldDominationMod.settings;
            return s != null && s.scaleRaidClampWithEscalation && s.enableLateGameScaling;
        }

        /// <summary>
        /// Active storyteller floor/ceiling fractions for WD raid clamps.
        /// Scale off or Mid/Late Game master off → legacy pair; otherwise Early/Mid/Late from cached escalation stage.
        /// </summary>
        public static void GetActiveStorytellerClampFractions(out float minFrac, out float maxFrac)
        {
            GetActiveStorytellerClampFractions(out minFrac, out maxFrac, out _);
        }

        public static void GetActiveStorytellerClampFractions(
            out float minFrac,
            out float maxFrac,
            out WdEscalationStage bandStage)
        {
            minFrac = WorldDominationSettings.DefCaravanRaidMinStorytellerFrac;
            maxFrac = WorldDominationSettings.DefCaravanRaidMaxStorytellerFrac;
            bandStage = WdEscalationStage.None;

            var s = WorldDominationMod.settings;
            if (s == null) return;

            if (!UsesEscalationStageClampBands(s))
            {
                minFrac = Mathf.Clamp(s.caravanRaidPointsMinStorytellerFraction, 0.05f, 2f);
                maxFrac = Mathf.Clamp(s.caravanRaidPointsMaxStorytellerFraction, 0.5f, 50f);
                if (minFrac > maxFrac) maxFrac = minFrac;
                return;
            }

            WorldComponent_SpreadManager mgr = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            bandStage = WdEscalation.GetCachedStage(mgr);
            switch (bandStage)
            {
                case WdEscalationStage.Late:
                    minFrac = s.lateRaidClampMinStorytellerFraction;
                    maxFrac = s.lateRaidClampMaxStorytellerFraction;
                    break;
                case WdEscalationStage.Mid:
                    minFrac = s.midRaidClampMinStorytellerFraction;
                    maxFrac = s.midRaidClampMaxStorytellerFraction;
                    break;
                default:
                    bandStage = WdEscalationStage.None;
                    minFrac = s.earlyRaidClampMinStorytellerFraction;
                    maxFrac = s.earlyRaidClampMaxStorytellerFraction;
                    break;
            }

            minFrac = Mathf.Clamp(minFrac, 0.05f, 2f);
            maxFrac = Mathf.Clamp(maxFrac, 0.5f, 50f);
            if (minFrac > maxFrac) maxFrac = minFrac;
        }

        /// <summary>Label for the active clamp band (Early / Mid / Late), or empty when using the legacy pair.</summary>
        public static string GetActiveClampBandLabel()
        {
            if (!UsesEscalationStageClampBands())
                return string.Empty;

            GetActiveStorytellerClampFractions(out _, out _, out WdEscalationStage stage);
            return stage switch
            {
                WdEscalationStage.Late => "TSA_WD_Escalation_StageLate".Translate().ToString(),
                WdEscalationStage.Mid => "TSA_WD_Escalation_StageMid".Translate().ToString(),
                _ => "TSA_WD_Escalation_StageEarly".Translate().ToString()
            };
        }

        /// <summary>Active clamp as whole percents for tooltips/alerts.</summary>
        public static void TryGetActiveClampPercents(out int minPct, out int maxPct, out string bandLabel)
        {
            minPct = 0;
            maxPct = 0;
            bandLabel = string.Empty;
            if (!WdRaidPointsStorytellerBandClampActive()) return;
            GetActiveStorytellerClampFractions(out float minFrac, out float maxFrac, out _);
            minPct = Mathf.RoundToInt(minFrac * 100f);
            maxPct = Mathf.RoundToInt(maxFrac * 100f);
            bandLabel = GetActiveClampBandLabel();
        }

        public static void GetWdRaidPointClampBounds(
            Map incidentMap,
            out Map baselineMap,
            out float baselineThreat,
            out float floor,
            out float ceiling,
            out float minStorytellerFraction,
            out float maxStorytellerFraction)
        {
            baselineMap = null;
            baselineThreat = 0f;
            floor = 0f;
            ceiling = 0f;
            minStorytellerFraction = 0f;
            maxStorytellerFraction = 0f;
            if (incidentMap == null) return;
            var s = WorldDominationMod.settings;
            if (s == null) return;
            if (s.alwaysUseStrengthAsRaidPoints) return;
            baselineMap = ResolveStorytellerBaselineMapForWdRaidClamp(incidentMap);
            if (baselineMap == null) return;
            baselineThreat = StorytellerUtility.DefaultThreatPointsNow(baselineMap);
            GetActiveStorytellerClampFractions(out minStorytellerFraction, out maxStorytellerFraction);
            floor = baselineThreat * minStorytellerFraction;
            ceiling = baselineThreat * maxStorytellerFraction;
        }

        /// <summary>
        /// Same WYSIWYG rule as <see cref="Raid_OnPlayerColony.HandleRaidOnPlayer"/>: clamp traveler/aggressor strength to
        /// <c>[baseline × minFraction, baseline × maxFraction]</c> from mod settings. Uses <paramref name="map"/> for
        /// baseline when it is a player home map; otherwise uses the strongest player home map so interception on
        /// encounter tiles matches colony raids instead of tiny encounter-map baselines.
        /// </summary>
        public static float ClampRaidPointsToStorytellerBand(float attackerStrength, Map map)
        {
            if (map == null) return Mathf.Max(0f, attackerStrength);
            var s = WorldDominationMod.settings;
            if (s == null) return Mathf.Max(0f, attackerStrength);
            if (s.alwaysUseStrengthAsRaidPoints) return Mathf.Max(0f, attackerStrength);
            GetWdRaidPointClampBounds(map, out _, out _, out float lo, out float hi, out _, out _);
            return Mathf.Clamp(attackerStrength, lo, hi);
        }
    }
}
