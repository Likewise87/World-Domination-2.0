using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Research outpost: continuous progress on the active research project using cumulative Intellectual (trait-adjusted).</summary>
    public static class Outpost_Research
    {
        /// <summary>Vanilla research points per tick factor ([RimWorld wiki](https://rimworldwiki.com/wiki/Research)).</summary>
        public const float VanillaResearchPointsPerTick = 0.00825f;

        /// <summary>Matches in-game hour batch cadence used by <see cref="WorldObject_WD_Outpost"/> production timer.</summary>
        public const int ResearchTickBatch = 2500;

        /// <summary>Simple research bench baseline. Kept hardcoded so XML has one balancing lever: outpost efficiency.</summary>
        public const float SimpleResearchBenchSpeedFactor = 0.75f;

        /// <summary>SkillNeed bonus per Intellectual level. Research outposts use this linearly for cumulative skill.</summary>
        private const float SkillBonusPerLevel = 0.115f;
        private const float MinResearchSpeed = 0.1f;

        /// <summary>Too Smart: that pawn's Intellectual counts at this multiplier toward effective cumulative Intellectual.</summary>
        public const float TooSmartIntelMultiplier = 1.30f;

        private static TraitDef cachedTooSmartTrait;

        private static TraitDef TooSmartTrait =>
            cachedTooSmartTrait ?? (cachedTooSmartTrait = DefDatabase<TraitDef>.GetNamedSilentFail("TooSmart"));

        /// <summary>Player's active main research project (null if none).</summary>
        public static ResearchProjectDef GetActiveResearchProject() =>
            Find.ResearchManager?.GetProject(null);

        public static float GetConfiguredEfficiencyFraction(OutpostDefExtension ext)
        {
            if (ext == null) return 0f;
            return Mathf.Max(0f, ext.researchEfficiencyFraction);
        }

        /// <summary>Intellectual contribution multiplier for this pawn (Too Smart → 1.30×).</summary>
        public static float GetIntelContributionMultiplier(Pawn pawn)
        {
            if (pawn?.story?.traits == null) return 1f;
            var tooSmart = TooSmartTrait;
            if (tooSmart != null && pawn.story.traits.HasTrait(tooSmart))
                return TooSmartIntelMultiplier;
            return 1f;
        }

        public static int GetPawnIntellectualLevel(Pawn pawn)
        {
            return Outpost_Academy.GetPawnSkillLevel(pawn, SkillDefOf.Intellectual);
        }

        /// <summary>Sum of each occupant's Intellectual × trait multiplier, then skill diminishing returns (founding still uses raw cumulative skill).</summary>
        public static float GetEffectiveCumulativeIntellectual(WorldObject_WD_Outpost outpost)
        {
            if (outpost?.Occupants == null) return 0f;
            float sum = 0f;
            for (int i = 0; i < outpost.Occupants.Count; i++)
            {
                var p = outpost.Occupants[i];
                if (p == null || p.Destroyed || p.Dead) continue;
                int intel = GetPawnIntellectualLevel(p);
                if (intel <= 0) continue;
                sum += intel * GetIntelContributionMultiplier(p);
            }
            return OutpostSkillScaling.ToEffective(sum);
        }

        public static float GetEffectiveCumulativeIntellectualRaw(WorldObject_WD_Outpost outpost)
        {
            if (outpost?.Occupants == null) return 0f;
            float sum = 0f;
            for (int i = 0; i < outpost.Occupants.Count; i++)
            {
                var p = outpost.Occupants[i];
                if (p == null || p.Destroyed || p.Dead) continue;
                int intel = GetPawnIntellectualLevel(p);
                if (intel <= 0) continue;
                sum += intel * GetIntelContributionMultiplier(p);
            }
            return sum;
        }

        public static float GetResearchSpeedFromEffectiveIntel(float effectiveIntel)
        {
            if (effectiveIntel <= 0f) return MinResearchSpeed;
            return Mathf.Max(MinResearchSpeed, effectiveIntel * SkillBonusPerLevel);
        }

        public static float GetDifficultyResearchFactor()
        {
            var diff = Find.Storyteller?.difficulty;
            return diff != null ? diff.researchSpeedFactor : 1f;
        }

        /// <summary>Base efficiency + flat upgrade percentage points, clamped 0–1.</summary>
        public static float GetTotalEfficiency(WorldObject_WD_Outpost outpost, OutpostDefExtension ext)
        {
            if (outpost == null || ext == null) return 0f;
            float total = GetConfiguredEfficiencyFraction(ext) + outpost.GetResearchUpgradeEfficiencyBonus();
            return Mathf.Clamp(total, 0f, 1f);
        }

        public static float GetPointsPerTick(WorldObject_WD_Outpost outpost)
        {
            if (!Outpost_Production_Utils.TryGetResearchExtension(outpost?.def, out var ext)) return 0f;
            float effectiveIntel = GetEffectiveCumulativeIntellectual(outpost);
            if (effectiveIntel <= 0f) return 0f;

            float speed = GetResearchSpeedFromEffectiveIntel(effectiveIntel);
            float diff = GetDifficultyResearchFactor();
            float efficiency = GetTotalEfficiency(outpost, ext);
            float experts = OutpostExpertUtility.GetCombinedProductionBonus(outpost);
            return VanillaResearchPointsPerTick * speed * SimpleResearchBenchSpeedFactor * diff * efficiency * (1f + experts);
        }

        public static float GetPointsPerDay(WorldObject_WD_Outpost outpost)
        {
            return GetPointsPerTick(outpost) * GenDate.TicksPerDay;
        }

        public static float GetBasePointsPerDayForDisplay(WorldObject_WD_Outpost outpost, OutpostDefExtension ext, float effectiveIntel, float totalEfficiency)
        {
            if (outpost == null || ext == null) return 0f;
            return VanillaResearchPointsPerTick * SkillBonusPerLevel * SimpleResearchBenchSpeedFactor * GetDifficultyResearchFactor() * GenDate.TicksPerDay;
        }

        public static int GetInspectFingerprint(WorldObject_WD_Outpost outpost)
        {
            if (!Outpost_Production_Utils.TryGetResearchExtension(outpost?.def, out var ext)) return 0;
            unchecked
            {
                int h = 17;
                h = h * 31 + Mathf.RoundToInt(GetEffectiveCumulativeIntellectual(outpost) * 100f);
                h = h * 31 + Mathf.RoundToInt(GetTotalEfficiency(outpost, ext) * 10000f);
                h = h * 31 + Mathf.RoundToInt(GetDifficultyResearchFactor() * 10000f);
                h = h * 31 + (GetActiveResearchProject()?.shortHash ?? 0);
                h = h * 31 + (CanResearchNow(outpost, out _) ? 1 : 0);
                return h;
            }
        }

        public static Pawn GetRepresentativeResearcher(WorldObject_WD_Outpost outpost)
        {
            if (outpost?.Occupants == null) return null;
            Pawn best = null;
            int bestIntel = -1;
            for (int i = 0; i < outpost.Occupants.Count; i++)
            {
                var p = outpost.Occupants[i];
                if (p == null || p.Destroyed || p.Dead) continue;
                int intel = GetPawnIntellectualLevel(p);
                if (intel <= bestIntel) continue;
                bestIntel = intel;
                best = p;
            }
            return best;
        }

        public static bool CanResearchNow(WorldObject_WD_Outpost outpost, out string pauseReason)
        {
            pauseReason = null;
            if (outpost == null || !Outpost_Production_Utils.IsResearchOutpost(outpost.def))
                return false;
            if (GetActiveResearchProject() == null)
            {
                pauseReason = "TSA_WD_Research_NoActiveProject".Translate().ToString();
                return false;
            }
            if (GetEffectiveCumulativeIntellectual(outpost) <= 0f)
            {
                pauseReason = "TSA_WD_Research_NoResearchers".Translate().ToString();
                return false;
            }
            return true;
        }

        /// <summary>Hourly batch; call from <see cref="WorldObject_WD_Outpost.Tick"/>.</summary>
        public static void TickResearch(WorldObject_WD_Outpost outpost, int ticksGame, int staggerMask)
        {
            if (outpost == null || outpost.Faction != Faction.OfPlayer) return;
            if (!Outpost_Production_Utils.IsResearchOutpost(outpost.def)) return;
            if ((ticksGame + staggerMask) % ResearchTickBatch != 0) return;
            if (!CanResearchNow(outpost, out _)) return;

            var rm = Find.ResearchManager;
            var proj = GetActiveResearchProject();
            if (rm == null || proj == null) return;

            float points = GetPointsPerTick(outpost) * ResearchTickBatch;
            if (points <= 0f) return;

            Pawn rep = GetRepresentativeResearcher(outpost);
            rm.AddProgress(proj, points, rep);
        }

        public static string GetEfficiencyBreakdown(WorldObject_WD_Outpost outpost, OutpostDefExtension ext)
        {
            if (outpost == null || ext == null) return "";
            float baseEff = GetConfiguredEfficiencyFraction(ext);
            float upgrade = outpost.GetResearchUpgradeEfficiencyBonus();
            float total = GetTotalEfficiency(outpost, ext);
            int basePct = Mathf.RoundToInt(baseEff * 100f);
            int totalPct = Mathf.RoundToInt(total * 100f);
            if (upgrade <= 0.0001f)
            {
                string key = "TSA_WD_Research_EfficiencyBaseOnly";
                string t = key.Translate(totalPct.ToString()).ToString();
                if (t == key || t.Contains("TSA_WD_Research_EfficiencyBaseOnly"))
                    t = totalPct + "% efficiency";
                return t;
            }
            int upgradePp = Mathf.RoundToInt(upgrade * 100f);
            string key2 = "TSA_WD_Research_EfficiencyWithUpgrades";
            string t2 = key2.Translate(totalPct.ToString(), basePct.ToString(), upgradePp.ToString()).ToString();
            if (t2 == key2 || t2.Contains("TSA_WD_Research_EfficiencyWithUpgrades"))
                t2 = totalPct + "% efficiency (" + basePct + "% base + " + upgradePp + "pp upgrades)";
            return t2;
        }

        public static string GetInspectProductLine(WorldObject_WD_Outpost outpost)
        {
            // Inspect pane shows the same crisp one-liner as the Stats tab ("Contributes 576 research per day");
            // the full skill × efficiency × base math lives in the Stats-tab tooltip instead.
            return GetStatsSummaryLine(outpost);
        }

        /// <summary>Short one-line summary for the Stats tab, e.g. "Contributes 576 research per day". Detailed math lives in <see cref="GetStatsSummaryTooltip"/>.</summary>
        public static string GetStatsSummaryLine(WorldObject_WD_Outpost outpost)
        {
            if (!Outpost_Production_Utils.TryGetResearchExtension(outpost?.def, out _)) return "";
            if (!CanResearchNow(outpost, out string pause))
            {
                string pk = "TSA_WD_Research_InspectPaused";
                string pt = pk.Translate(pause ?? "").ToString();
                if (pt == pk || pt.Contains("TSA_WD_Research_InspectPaused"))
                    pt = "Research paused: " + (pause ?? "?");
                return pt;
            }

            float ptsDay = GetPointsPerDay(outpost);
            string key = "TSA_WD_Research_StatsSummary";
            string t = key.Translate(ptsDay.ToString("F0")).ToString();
            if (t == key || t.Contains("TSA_WD_Research_StatsSummary"))
                t = "Contributes " + ptsDay.ToString("F0") + " research per day";
            return t;
        }

        /// <summary>Detailed research math for the Stats-tab summary mouseover.</summary>
        public static string GetStatsSummaryTooltip(WorldObject_WD_Outpost outpost)
        {
            if (!Outpost_Production_Utils.TryGetResearchExtension(outpost?.def, out var ext)) return "";
            if (!CanResearchNow(outpost, out string pause))
                return pause ?? "TSA_WD_Research_NoResearchers".Translate().ToString();

            float effIntel = GetEffectiveCumulativeIntellectual(outpost);
            float ptsDay = GetPointsPerDay(outpost);
            float efficiency = GetTotalEfficiency(outpost, ext);
            float baseValue = GetBasePointsPerDayForDisplay(outpost, ext, effIntel, efficiency);
            float experts = OutpostExpertUtility.GetCombinedProductionBonus(outpost);

            var sb = new StringBuilder();
            string key = "TSA_WD_Research_StatsSummaryTip";
            string main = key.Translate(
                ptsDay.ToString("F0"),
                effIntel.ToString("F0"),
                (efficiency * 100f).ToString("F0") + "%",
                baseValue.ToString("F1")).ToString();
            if (main == key || main.Contains("TSA_WD_Research_StatsSummaryTip"))
            {
                main = ptsDay.ToString("F0") + " research/day = " + effIntel.ToString("F0")
                    + " cumulative Intellectual skill × " + (efficiency * 100f).ToString("F0")
                    + "% outpost efficiency × " + baseValue.ToString("F1") + " base value per skill point.";
            }
            sb.AppendLine(main);
            sb.AppendLine();
            sb.AppendLine(GetEfficiencyBreakdown(outpost, ext));
            if (experts > 0.001f)
            {
                int expertPct = Mathf.RoundToInt(experts * 100f);
                string expertKey = "TSA_WD_Research_StatsSummaryTip_Experts";
                string expertLine = expertKey.Translate((1f + experts).ToString("F2"), expertPct.ToString()).ToString();
                if (expertLine == expertKey || expertLine.Contains("TSA_WD_Research_StatsSummaryTip_Experts"))
                    expertLine = "Expert production bonus: ×" + (1f + experts).ToString("F2") + " (+" + expertPct + "%).";
                sb.AppendLine(expertLine);

                string detail = OutpostExpertUtility.BuildCombinedContributionTooltip(
                    outpost,
                    (OutpostExpertRole.Entertainer, OutpostExpertUtility.GetEntertainerProductionBonus),
                    (OutpostExpertRole.Cook, OutpostExpertUtility.GetCookProductionBonus));
                if (!string.IsNullOrEmpty(detail))
                {
                    sb.AppendLine();
                    sb.Append(detail);
                }
            }
            return sb.ToString().TrimEnd();
        }

        public static string GetProductionSummaryLine(WorldObject_WD_Outpost outpost)
        {
            if (!Outpost_Production_Utils.TryGetResearchExtension(outpost?.def, out _)) return null;
            if (!CanResearchNow(outpost, out string pause))
            {
                string pk = "TSA_WD_Research_SummaryPaused";
                string pt = pk.Translate(pause ?? "").ToString();
                if (pt == pk || pt.Contains("TSA_WD_Research_SummaryPaused"))
                    pt = "Research paused: " + (pause ?? "?");
                return pt;
            }
            var proj = GetActiveResearchProject();
            string projLabel = proj?.LabelCap ?? "?";
            float ptsDay = GetPointsPerDay(outpost);
            string key = "TSA_WD_Research_Summary";
            string t = key.Translate(projLabel, ptsDay.ToString("F0")).ToString();
            if (t == key || t.Contains("TSA_WD_Research_Summary"))
                t = "Research: " + projLabel + " (~" + ptsDay.ToString("F0") + " pts/day)";
            return t;
        }
    }
}
