using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Shared Smart Assign geography: skill rank + closest eligible outpost.</summary>
    public static class SmartAssignOutpostUtility
    {
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

        public static WorldObject_WD_Outpost? FindClosestOutpost(
            PlanetTile fromTile,
            List<WorldObject_WD_Outpost> candidates,
            WorldObject_WD_Outpost? excludeOutpost = null,
            bool excludeSameTile = false)
        {
            WorldObject_WD_Outpost? best = null;
            float bestDist = float.MaxValue;
            var grid = Find.WorldGrid;
            if (grid == null) return null;

            for (int i = 0; i < candidates.Count; i++)
            {
                WorldObject_WD_Outpost o = candidates[i];
                if (o == null || o.Destroyed || !o.Tile.Valid) continue;
                if (excludeOutpost != null && o == excludeOutpost) continue;
                if (excludeSameTile && fromTile.Valid && o.Tile == fromTile) continue;
                float dist = grid.ApproxDistanceInTiles(fromTile, o.Tile);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = o;
                }
            }
            return best;
        }

        /// <summary>
        /// Pick closest eligible outpost for this pawn from fromTile, walking skills best-first.
        /// </summary>
        public static bool TryFindSmartAssignOutpost(
            Pawn pawn,
            PlanetTile fromTile,
            Dictionary<SkillDef, List<WorldObject_WD_Outpost>> bySkill,
            WorldObject_WD_Outpost? currentOutpost,
            out WorldObject_WD_Outpost? dest)
        {
            dest = null;
            if (pawn?.skills?.skills == null || !fromTile.Valid || bySkill == null || bySkill.Count == 0)
                return false;

            bool atOutpost = currentOutpost != null && !currentOutpost.Destroyed;
            if (atOutpost)
            {
                SkillDef? winning = FindWinningSkillDef(pawn, bySkill);
                if (winning != null && currentOutpost != null && OutpostUsesSkill(currentOutpost, winning))
                {
                    dest = currentOutpost;
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

                WorldObject_WD_Outpost? best = FindClosestOutpost(
                    fromTile,
                    candidates,
                    excludeOutpost: currentOutpost,
                    excludeSameTile: atOutpost);
                if (best == null) continue;

                dest = best;
                return true;
            }
            return false;
        }
    }
}
