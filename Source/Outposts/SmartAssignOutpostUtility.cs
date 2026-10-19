using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Shared Smart Assign geography: skill rank + soft-scored eligible outpost.</summary>
    public static class SmartAssignOutpostUtility
    {
        /// <summary>1 tile ≈ this much banner-skill of unevenness.</summary>
        public const float DistanceSkillWeight = 2f;

        public static Dictionary<SkillDef, List<WorldObject_WD_Outpost>> BuildOutpostsByRelevantSkill()
        {
            var bySkill = new Dictionary<SkillDef, List<WorldObject_WD_Outpost>>();
            Faction player = Faction.OfPlayer;
            if (player == null) return bySkill;

            var allWo = Find.WorldObjects?.AllWorldObjects;
            if (allWo == null) return bySkill;

            var schedule = WorldComponent_PrisonerRecruitSchedule.Get();

            for (int i = 0; i < allWo.Count; i++)
            {
                if (allWo[i] is not WorldObject_WD_Outpost outpost || outpost.Destroyed) continue;
                if (outpost.Faction != player) continue;
                if (schedule != null && schedule.IsSmartAssignExcluded(outpost))
                    continue;

                List<SkillDef> skills = WorldObject_WD_Outpost.GetRelevantSkillDefs(outpost.def);
                if (skills == null || skills.Count == 0) continue;

                for (int s = 0; s < skills.Count; s++)
                {
                    SkillDef sd = skills[s];
                    if (sd == null) continue;
                    if (!bySkill.TryGetValue(sd, out List<WorldObject_WD_Outpost> list))
                    {
                        list = new List<WorldObject_WD_Outpost>();
                        bySkill[sd] = list;
                    }
                    if (!list.Contains(outpost))
                        list.Add(outpost);
                }
            }
            return bySkill;
        }

        public static int CompareSkillRecordBestFirst(SkillRecord a, SkillRecord b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return 1;
            if (b == null) return -1;
            int cmp = b.Level.CompareTo(a.Level);
            if (cmp != 0) return cmp;
            cmp = ((int)b.passion).CompareTo((int)a.passion);
            if (cmp != 0) return cmp;
            string an = a.def?.defName ?? "";
            string bn = b.def?.defName ?? "";
            return string.CompareOrdinal(an, bn);
        }

        public static List<SkillRecord> RankSkillsBestFirst(Pawn pawn)
        {
            var ranked = new List<SkillRecord>();
            if (pawn?.skills?.skills == null) return ranked;
            ranked.AddRange(pawn.skills.skills);
            ranked.Sort(CompareSkillRecordBestFirst);
            return ranked;
        }

        /// <summary>
        /// Best skill that has at least one eligible outpost (any distance). Used for "already well-placed".
        /// </summary>
        public static SkillDef? FindWinningSkillDef(Pawn pawn, Dictionary<SkillDef, List<WorldObject_WD_Outpost>> bySkill)
        {
            if (pawn == null || bySkill == null || bySkill.Count == 0) return null;
            List<SkillRecord> ranked = RankSkillsBestFirst(pawn);
            for (int i = 0; i < ranked.Count; i++)
            {
                SkillDef skill = ranked[i]?.def;
                if (skill == null) continue;
                if (bySkill.TryGetValue(skill, out List<WorldObject_WD_Outpost> list) && list != null && list.Count > 0)
                    return skill;
            }
            return null;
        }

        public static bool OutpostUsesSkill(WorldObject_WD_Outpost outpost, SkillDef skill)
        {
            if (outpost == null || skill == null) return false;
            List<SkillDef> skills = WorldObject_WD_Outpost.GetRelevantSkillDefs(outpost.def);
            if (skills == null) return false;
            for (int i = 0; i < skills.Count; i++)
            {
                if (skills[i] == skill) return true;
            }
            return false;
        }

        /// <summary>Per-pawn banner raw contribution mirroring <see cref="OutpostSkillScaling.GetBannerRawSkill"/>.</summary>
        public static float GetPawnBannerContribution(WorldObject_WD_Outpost outpost, Pawn pawn)
        {
            if (outpost?.def == null || pawn == null) return 0f;
            WorldObjectDef def = outpost.def;
            if (Outpost_Production_Utils.IsScavengingOutpost(def)) return 0f;

            if (Outpost_Production_Utils.IsFoodProducerOutpost(def))
            {
                if (Outpost_Production_Utils.IsFarmingOutpost(def))
                    return Outpost_Academy.GetPawnSkillLevel(pawn, SkillDefOf.Plants);
                return Outpost_Academy.GetPawnSkillLevel(pawn, SkillDefOf.Animals);
            }

            if (Outpost_Production_Utils.IsMiningOutpost(def))
                return Outpost_Academy.GetPawnSkillLevel(pawn, SkillDefOf.Mining);

            if (Outpost_Production_Utils.IsResearchOutpost(def))
            {
                int intel = Outpost_Research.GetPawnIntellectualLevel(pawn);
                if (intel <= 0) return 0f;
                return intel * Outpost_Research.GetIntelContributionMultiplier(pawn);
            }

            if (OutpostSkillScaling.UsesSocialProductionScaling(def))
                return Outpost_Academy.GetPawnSkillLevel(pawn, SkillDefOf.Social);

            List<SkillDef> skills = WorldObject_WD_Outpost.GetRelevantSkillDefs(def);
            if (skills == null || skills.Count == 0) return 0f;
            float total = 0f;
            for (int i = 0; i < skills.Count; i++)
            {
                SkillDef sd = skills[i];
                if (sd == null) continue;
                total += Outpost_Academy.GetPawnSkillLevel(pawn, sd);
            }
            return total;
        }

        public static float GetProvisionalBannerRaw(
            WorldObject_WD_Outpost outpost,
            Dictionary<WorldObject_WD_Outpost, float>? provisionalBannerRaw)
        {
            if (outpost == null) return 0f;
            if (provisionalBannerRaw != null && provisionalBannerRaw.TryGetValue(outpost, out float raw))
                return raw;
            return OutpostSkillScaling.GetBannerRawSkill(outpost);
        }

        public static void EnsureProvisionalBannerRaw(
            WorldObject_WD_Outpost outpost,
            Dictionary<WorldObject_WD_Outpost, float> provisionalBannerRaw)
        {
            if (outpost == null || provisionalBannerRaw == null) return;
            if (provisionalBannerRaw.ContainsKey(outpost)) return;
            provisionalBannerRaw[outpost] = OutpostSkillScaling.GetBannerRawSkill(outpost);
        }

        public static void BumpProvisionalBannerRaw(
            WorldObject_WD_Outpost outpost,
            Pawn pawn,
            Dictionary<WorldObject_WD_Outpost, float> provisionalBannerRaw)
        {
            if (outpost == null || provisionalBannerRaw == null) return;
            EnsureProvisionalBannerRaw(outpost, provisionalBannerRaw);
            provisionalBannerRaw[outpost] = provisionalBannerRaw[outpost] + GetPawnBannerContribution(outpost, pawn);
        }

        /// <summary>Soft assignment cost. Lower is better. When DR off, distance only.</summary>
        public static float ScoreDestinationCost(
            WorldObject_WD_Outpost outpost,
            PlanetTile fromTile,
            float provisionalRaw)
        {
            var grid = Find.WorldGrid;
            float dist = 0f;
            if (grid != null && fromTile.Valid && outpost != null && outpost.Tile.Valid)
                dist = grid.ApproxDistanceInTiles(fromTile, outpost.Tile);

            if (!OutpostSkillScaling.IsEnabled)
                return dist;

            float cost = provisionalRaw + dist * DistanceSkillWeight;
            if (OutpostSkillScaling.IsDiminished(provisionalRaw, outpost?.def))
                cost += OutpostSkillScaling.FirstFullBandEnd(outpost?.def);
            return cost;
        }

        /// <summary>
        /// Soft-scored best outpost among candidates (load + DR bias + soft distance).
        /// </summary>
        public static WorldObject_WD_Outpost? FindBestOutpost(
            PlanetTile fromTile,
            List<WorldObject_WD_Outpost> candidates,
            WorldObject_WD_Outpost? excludeOutpost = null,
            bool excludeSameTile = false,
            Dictionary<WorldObject_WD_Outpost, float>? provisionalBannerRaw = null)
        {
            WorldObject_WD_Outpost? best = null;
            float bestCost = float.MaxValue;
            var grid = Find.WorldGrid;
            if (grid == null) return null;

            for (int i = 0; i < candidates.Count; i++)
            {
                WorldObject_WD_Outpost o = candidates[i];
                if (o == null || o.Destroyed || !o.Tile.Valid) continue;
                if (excludeOutpost != null && o == excludeOutpost) continue;
                if (excludeSameTile && fromTile.Valid && o.Tile == fromTile) continue;

                float raw = GetProvisionalBannerRaw(o, provisionalBannerRaw);
                float cost = ScoreDestinationCost(o, fromTile, raw);
                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = o;
                }
            }
            return best;
        }

        /// <summary>Legacy name: soft-scored pick (same as <see cref="FindBestOutpost"/> without provisional overlay).</summary>
        public static WorldObject_WD_Outpost? FindClosestOutpost(
            PlanetTile fromTile,
            List<WorldObject_WD_Outpost> candidates,
            WorldObject_WD_Outpost? excludeOutpost = null,
            bool excludeSameTile = false)
        {
            return FindBestOutpost(fromTile, candidates, excludeOutpost, excludeSameTile, provisionalBannerRaw: null);
        }

        /// <summary>Best skill on the pawn that this outpost uses (for reason text / overrides).</summary>
        public static SkillDef? FindMatchedSkillForDest(Pawn pawn, WorldObject_WD_Outpost dest)
        {
            if (pawn == null || dest == null) return null;
            List<SkillRecord> ranked = RankSkillsBestFirst(pawn);
            for (int i = 0; i < ranked.Count; i++)
            {
                SkillDef skill = ranked[i]?.def;
                if (skill != null && OutpostUsesSkill(dest, skill))
                    return skill;
            }
            return null;
        }

        /// <summary>
        /// Short multi-line Smart Send reason. Built once per row, not per frame.
        /// </summary>
        public static string BuildAssignmentReasonText(
            Pawn pawn,
            PlanetTile fromTile,
            WorldObject_WD_Outpost dest,
            SkillDef? matchedSkill = null)
        {
            if (!TryGetReasonParts(pawn, fromTile, dest, matchedSkill,
                    out string skillLabel, out int skillLevel, out int dist, out string cumSkillLabel,
                    out float raw, out bool diminished, out _))
                return "";

            string line1 = "TSA_WD_SmartSend_ReasonSkill".Translate(skillLabel, skillLevel.ToString());
            string line2 = "TSA_WD_SmartSend_ReasonDistance".Translate(dist.ToString());
            string line3 = diminished
                ? "TSA_WD_SmartSend_ReasonCumSkillDr".Translate(cumSkillLabel, raw.ToString("F0"))
                : "TSA_WD_SmartSend_ReasonCumSkillFull".Translate(cumSkillLabel, raw.ToString("F0"));
            return line1 + "\n" + line2 + "\n" + line3;
        }

        /// <summary>
        /// Tooltip: chosen outpost score, up to 4 alternatives (max 5 lines), then crisp formula.
        /// </summary>
        public static string BuildAssignmentReasonTip(
            Pawn pawn,
            PlanetTile fromTile,
            WorldObject_WD_Outpost dest,
            SkillDef? matchedSkill = null)
        {
            if (pawn == null || dest == null) return "";

            SkillDef skill = matchedSkill ?? FindMatchedSkillForDest(pawn, dest);
            if (skill == null) return "";

            string skillLabel = Outpost_Production_Utils.SkillLabelCap(skill);
            Dictionary<SkillDef, List<WorldObject_WD_Outpost>> bySkill = BuildOutpostsByRelevantSkill();
            if (!bySkill.TryGetValue(skill, out List<WorldObject_WD_Outpost> candidates)
                || candidates == null
                || candidates.Count == 0)
                return "";

            var ranked = new List<(WorldObject_WD_Outpost outpost, float cost, string name)>();
            for (int i = 0; i < candidates.Count; i++)
            {
                WorldObject_WD_Outpost o = candidates[i];
                if (o == null || o.Destroyed || !o.Tile.Valid) continue;
                float raw = OutpostSkillScaling.IsEnabled
                    ? OutpostSkillScaling.GetBannerRawSkill(o)
                    : o.GetTotalRelevantSkillRaw();
                float cost = ScoreDestinationCost(o, fromTile, raw);
                string name = !string.IsNullOrEmpty(o.Name) ? o.Name : o.LabelCap;
                ranked.Add((o, cost, name));
            }

            if (ranked.Count == 0) return "";

            ranked.Sort((a, b) =>
            {
                int cmp = a.cost.CompareTo(b.cost);
                if (cmp != 0) return cmp;
                return string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase);
            });

            string destName = !string.IsNullOrEmpty(dest.Name) ? dest.Name : dest.LabelCap;
            float destCost = 0f;
            bool foundDest = false;
            for (int i = 0; i < ranked.Count; i++)
            {
                if (ranked[i].outpost != dest) continue;
                destCost = ranked[i].cost;
                foundDest = true;
                break;
            }
            if (!foundDest)
            {
                float raw = OutpostSkillScaling.IsEnabled
                    ? OutpostSkillScaling.GetBannerRawSkill(dest)
                    : dest.GetTotalRelevantSkillRaw();
                destCost = ScoreDestinationCost(dest, fromTile, raw);
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("TSA_WD_SmartSend_ReasonTipHeader".Translate());
            sb.AppendLine("TSA_WD_SmartSend_ReasonTipRow".Translate(destCost.ToString("F0"), destName));

            if (ranked.Count <= 1)
            {
                sb.AppendLine("TSA_WD_SmartSend_ReasonTipNoOther".Translate(skillLabel));
            }
            else
            {
                int shown = 1;
                for (int i = 0; i < ranked.Count && shown < 5; i++)
                {
                    if (ranked[i].outpost == dest) continue;
                    sb.AppendLine("TSA_WD_SmartSend_ReasonTipRow".Translate(
                        ranked[i].cost.ToString("F0"), ranked[i].name));
                    shown++;
                }
            }

            sb.AppendLine();
            if (OutpostSkillScaling.IsEnabled)
                sb.AppendLine("TSA_WD_SmartSend_ReasonTipFormula".Translate(DistanceSkillWeight.ToString("F0")));
            else
                sb.AppendLine("TSA_WD_SmartSend_ReasonTipFormulaDistOnly".Translate());
            sb.Append("TSA_WD_SmartSend_ReasonTipLowerWins".Translate());
            return sb.ToString();
        }

        private static bool TryGetReasonParts(
            Pawn pawn,
            PlanetTile fromTile,
            WorldObject_WD_Outpost dest,
            SkillDef? matchedSkill,
            out string skillLabel,
            out int skillLevel,
            out int dist,
            out string cumSkillLabel,
            out float raw,
            out bool diminished,
            out float cost)
        {
            skillLabel = "?";
            skillLevel = 0;
            dist = 0;
            cumSkillLabel = "?";
            raw = 0f;
            diminished = false;
            cost = 0f;
            if (pawn == null || dest == null) return false;

            SkillDef skill = matchedSkill ?? FindMatchedSkillForDest(pawn, dest);
            skillLabel = skill != null ? Outpost_Production_Utils.SkillLabelCap(skill) : "?";
            skillLevel = skill != null ? Outpost_Academy.GetPawnSkillLevel(pawn, skill) : 0;

            var grid = Find.WorldGrid;
            if (grid != null && fromTile.Valid && dest.Tile.Valid)
                dist = (int)grid.ApproxDistanceInTiles(fromTile, dest.Tile);

            raw = OutpostSkillScaling.GetBannerRawSkill(dest);
            if (!OutpostSkillScaling.IsEnabled)
                raw = dest.GetTotalRelevantSkillRaw();

            cumSkillLabel = skillLabel;
            List<SkillDef> relevant = WorldObject_WD_Outpost.GetRelevantSkillDefs(dest.def);
            if (relevant != null && relevant.Count == 1 && relevant[0] != null)
                cumSkillLabel = Outpost_Production_Utils.SkillLabelCap(relevant[0]);

            diminished = OutpostSkillScaling.IsEnabled && OutpostSkillScaling.IsDiminished(raw, dest.def);
            cost = ScoreDestinationCost(dest, fromTile, raw);
            return true;
        }

        /// <summary>
        /// Pick best eligible outpost for this pawn from fromTile, walking skills best-first.
        /// </summary>
        public static bool TryFindSmartAssignOutpost(
            Pawn pawn,
            PlanetTile fromTile,
            Dictionary<SkillDef, List<WorldObject_WD_Outpost>> bySkill,
            WorldObject_WD_Outpost? currentOutpost,
            out WorldObject_WD_Outpost? dest,
            Dictionary<WorldObject_WD_Outpost, float>? provisionalBannerRaw = null)
        {
            return TryFindSmartAssignOutpost(
                pawn, fromTile, bySkill, currentOutpost, out dest, out _, provisionalBannerRaw);
        }

        /// <summary>
        /// Pick best eligible outpost for this pawn from fromTile, walking skills best-first.
        /// </summary>
        public static bool TryFindSmartAssignOutpost(
            Pawn pawn,
            PlanetTile fromTile,
            Dictionary<SkillDef, List<WorldObject_WD_Outpost>> bySkill,
            WorldObject_WD_Outpost? currentOutpost,
            out WorldObject_WD_Outpost? dest,
            out SkillDef? matchedSkill,
            Dictionary<WorldObject_WD_Outpost, float>? provisionalBannerRaw = null)
        {
            dest = null;
            matchedSkill = null;
            if (pawn?.skills?.skills == null || !fromTile.Valid || bySkill == null || bySkill.Count == 0)
                return false;

            bool atOutpost = currentOutpost != null && !currentOutpost.Destroyed;
            if (atOutpost)
            {
                SkillDef? winning = FindWinningSkillDef(pawn, bySkill);
                if (winning != null
                    && currentOutpost != null
                    && OutpostUsesSkill(currentOutpost, winning)
                    && bySkill.TryGetValue(winning, out List<WorldObject_WD_Outpost> winList)
                    && winList != null
                    && winList.Count > 0)
                {
                    float stayRaw = GetProvisionalBannerRaw(currentOutpost, provisionalBannerRaw);
                    float stayCost = ScoreDestinationCost(currentOutpost, fromTile, stayRaw);

                    WorldObject_WD_Outpost? better = FindBestOutpost(
                        fromTile,
                        winList,
                        excludeOutpost: currentOutpost,
                        excludeSameTile: true,
                        provisionalBannerRaw: provisionalBannerRaw);

                    if (better == null)
                    {
                        dest = currentOutpost;
                        matchedSkill = winning;
                        return true;
                    }

                    float betterRaw = GetProvisionalBannerRaw(better, provisionalBannerRaw);
                    float betterCost = ScoreDestinationCost(better, fromTile, betterRaw);
                    if (betterCost < stayCost)
                    {
                        dest = better;
                        matchedSkill = winning;
                        return true;
                    }

                    dest = currentOutpost;
                    matchedSkill = winning;
                    return true;
                }
            }

            List<SkillRecord> ranked = RankSkillsBestFirst(pawn);
            for (int i = 0; i < ranked.Count; i++)
            {
                SkillDef skill = ranked[i]?.def;
                if (skill == null) continue;
                if (!bySkill.TryGetValue(skill, out List<WorldObject_WD_Outpost> candidates) || candidates == null || candidates.Count == 0)
                    continue;

                WorldObject_WD_Outpost? best = FindBestOutpost(
                    fromTile,
                    candidates,
                    excludeOutpost: currentOutpost,
                    excludeSameTile: atOutpost,
                    provisionalBannerRaw: provisionalBannerRaw);
                if (best == null) continue;

                dest = best;
                matchedSkill = skill;
                return true;
            }
            return false;
        }
    }
}
