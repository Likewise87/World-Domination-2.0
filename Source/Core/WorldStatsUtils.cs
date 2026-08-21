using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>World power and faction stats for the World Stats window and coalitions.</summary>
    public static class WorldStatsUtils
    {
        /// <summary>Maps total strength to stats tier 1-4 using canonical bands from <see cref="CompViralSpread.GetStrengthRange"/>.</summary>
        public static int TierIndexFromWorldStrengthTotal(float totalStrength)
        {
            if (totalStrength >= CompViralSpread.GetStrengthRange(SettlementTier.T4).min) return 4;
            if (totalStrength >= CompViralSpread.GetStrengthRange(SettlementTier.T3).min) return 3;
            if (totalStrength >= CompViralSpread.GetStrengthRange(SettlementTier.T2).min) return 2;
            return 1;
        }

        public static float GetOutpostStatsStrength(CompViralSpread comp) =>
            comp != null ? comp.GetTotalLocalDefensePower() : 0f;

        /// <summary>
        /// Compute world power stats from pre-enumerated settlements and player outposts (e.g. from DailyWorldSnapshot).
        /// Settlement and outpost strength both use local defense power (offense + defense) so faction ranking is comparable.
        /// </summary>
        public static SpreadLogEntry.GlobalWorldStats ComputeWorldPowerStatsFromSnapshot(
            Dictionary<Faction, List<Settlement>> settlementsByFaction,
            List<WorldObject_WD_Outpost> playerOutposts)
        {
            var stats = new SpreadLogEntry.GlobalWorldStats();

            foreach (var kv in settlementsByFaction)
            {
                var f = kv.Key;
                // Player map colonies are not ranked here; player power comes only from WD outposts below.
                if (f == null || f.def.hidden || f.IsPlayer) continue;

                var fs = new SpreadLogEntry.FactionStat { faction = f };
                foreach (var s in kv.Value)
                {
                    var comp = s.GetComponent<CompViralSpread>();
                    if (comp == null) continue;

                    float str = GetOutpostStatsStrength(comp);
                    int t = Mathf.Clamp((int)comp.tier + 1, 1, 4);
                    fs.counts[t]++;
                    fs.strength[t] += str;
                    stats.GlobalTierStr[t] += str;
                    stats.GlobalTotalStr += str;
                }

                if (fs.TotalCount > 0)
                    stats.FactionStats.Add(fs);
            }

            AppendDefeatedFactionsWithNoSettlements(stats);

            var playerFaction = Faction.OfPlayerSilentFail;
            if (playerFaction != null)
            {
                // Always include the player (0 outposts = 0 strength) so dashboard / World Stats ranking works on new games.
                var playerStats = GetPlayerOutpostStatsFromList(stats, playerOutposts, playerFaction);
                if (playerStats != null)
                    stats.FactionStats.Add(playerStats);
            }

            stats.FactionStats.Sort((a, b) => b.TotalStr.CompareTo(a.TotalStr));
            return stats;
        }

        /// <summary>
        /// Factions with no settlements left are marked defeated and otherwise vanish from strength tallies.
        /// Keep them on World Stats / dashboard ranking at 0 strength so they remain visible until a comeback.
        /// </summary>
        private static void AppendDefeatedFactionsWithNoSettlements(SpreadLogEntry.GlobalWorldStats stats)
        {
            if (stats == null || Find.FactionManager == null) return;

            HashSet<Faction> present = null;
            foreach (Faction f in Find.FactionManager.AllFactionsVisible)
            {
                if (f == null || f.IsPlayer || f.def == null || f.def.hidden || !f.defeated) continue;
                if (WorldActions_Utils.IsExcludedFaction(f)) continue;

                if (present == null)
                {
                    present = new HashSet<Faction>();
                    for (int i = 0; i < stats.FactionStats.Count; i++)
                    {
                        Faction listed = stats.FactionStats[i]?.faction;
                        if (listed != null) present.Add(listed);
                    }
                }
                if (present.Contains(f)) continue;

                stats.FactionStats.Add(new SpreadLogEntry.FactionStat { faction = f });
                present.Add(f);
            }
        }

        /// <summary>Collect player outposts from the world objects list with a for-loop (no LINQ).</summary>
        public static List<WorldObject_WD_Outpost> CollectPlayerOutposts()
        {
            var playerFaction = Faction.OfPlayerSilentFail;
            var result = new List<WorldObject_WD_Outpost>();
            if (playerFaction == null || Find.WorldObjects == null) return result;
            var all = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is WorldObject_WD_Outpost wd && wd.Faction == playerFaction
                    && WorldActions_Utils.IsWdSurfaceWorldObject(wd))
                    result.Add(wd);
            }
            return result;
        }

        /// <summary>Return pre-built outpost list when available (from callers that already collected them during a settlement pass).</summary>
        public static List<WorldObject_WD_Outpost> CollectPlayerOutposts(List<WorldObject_WD_Outpost> preBuilt)
        {
            return preBuilt ?? CollectPlayerOutposts();
        }

        public static SpreadLogEntry.GlobalWorldStats GetWorldPowerStats()
        {
            var settlementsByFaction = new Dictionary<Faction, List<Settlement>>();
            var allSettlements = Find.WorldObjects.Settlements;
            for (int i = 0; i < allSettlements.Count; i++)
            {
                Settlement s = allSettlements[i];
                if (s.Faction == null || s.Faction.IsPlayer) continue;
                if (!WorldActions_Utils.IsWdSurfaceWorldObject(s)) continue;
                if (!settlementsByFaction.TryGetValue(s.Faction, out var list))
                {
                    list = new List<Settlement>();
                    settlementsByFaction[s.Faction] = list;
                }
                list.Add(s);
            }

            var playerOutposts = CollectPlayerOutposts();
            return ComputeWorldPowerStatsFromSnapshot(settlementsByFaction, playerOutposts);
        }

        private static SpreadLogEntry.FactionStat GetPlayerOutpostStatsFromList(
            SpreadLogEntry.GlobalWorldStats globalStats,
            List<WorldObject_WD_Outpost> playerOutposts,
            Faction playerFaction)
        {
            if (playerFaction == null) return null;

            var fs = new SpreadLogEntry.FactionStat { faction = playerFaction };
            if (playerOutposts == null || playerOutposts.Count == 0) return fs;

            foreach (var outpost in playerOutposts)
            {
                if (outpost.Faction != playerFaction) continue;

                var comp = outpost.GetComponent<CompViralSpread>();
                float str = GetOutpostStatsStrength(comp);
                int t = TierIndexFromWorldStrengthTotal(str);

                fs.counts[t]++;
                fs.strength[t] += str;
                globalStats.GlobalTierStr[t] += str;
                globalStats.GlobalTotalStr += str;
            }
            return fs;
        }

    }
}
