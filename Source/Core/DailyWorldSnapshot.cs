using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Built once per day in CalculateDailyBudget. Holds all settlements and player outposts
    /// in scope for the mod. Strength/existence may change intra-day; we use this snapshot
    /// for the day and optionally validate when using (e.g. settlement still exists).
    /// </summary>
    public class DailyWorldSnapshot
    {
        public Dictionary<Faction, List<Settlement>> SettlementsByFaction { get; }
        public List<WorldObject_WD_Outpost> PlayerOutposts { get; }
        public SpreadLogEntry.GlobalWorldStats WorldPowerStats { get; }
        public Dictionary<Faction, Settlement> AnchorPerFaction { get; }

        public DailyWorldSnapshot(
            Dictionary<Faction, List<Settlement>> settlementsByFaction,
            List<WorldObject_WD_Outpost> playerOutposts,
            SpreadLogEntry.GlobalWorldStats worldPowerStats,
            Dictionary<Faction, Settlement> anchorPerFaction)
        {
            SettlementsByFaction = settlementsByFaction;
            PlayerOutposts = playerOutposts;
            WorldPowerStats = worldPowerStats;
            AnchorPerFaction = anchorPerFaction;
        }

        public bool TryGetAnchor(Faction faction, out Settlement anchor)
        {
            anchor = null;
            if (faction == null || AnchorPerFaction == null) return false;
            if (!AnchorPerFaction.TryGetValue(faction, out var s)) return false;
            if (!IsSettlementStillValid(s)) return false;
            anchor = s;
            return true;
        }

        public static bool IsSettlementStillValid(Settlement s)
        {
            return s != null && !s.Destroyed && s.Spawned;
        }

        /// <summary>Single daily enumeration: all settlements and player outposts in scope. Build once per day in CalculateDailyBudget.</summary>
        public static DailyWorldSnapshot Build()
        {
            var settlementsByFaction = new Dictionary<Faction, List<Settlement>>();
            var playerOutposts = new List<WorldObject_WD_Outpost>();
            var playerFaction = Faction.OfPlayerSilentFail;

            var allObjects = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < allObjects.Count; i++)
            {
                var wo = allObjects[i];
                if (wo is Settlement s)
                {
                    if (s.Faction == null || s.Faction.def.hidden || s.Faction.defeated) continue;
                    if (WorldActions_Utils.IsExcludedFaction(s.Faction)) continue;
                    if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(s)) continue;
                    if (s.GetComponent<CompViralSpread>() == null) continue;
                    if (!settlementsByFaction.TryGetValue(s.Faction, out var list))
                    {
                        list = new List<Settlement>();
                        settlementsByFaction[s.Faction] = list;
                    }
                    list.Add(s);
                }
                else if (wo is WorldObject_WD_Outpost wd && playerFaction != null && wd.Faction == playerFaction
                    && WorldActions_Utils.IsWdSurfaceWorldObject(wd))
                {
                    playerOutposts.Add(wd);
                }
            }

            var worldPowerStats = WorldStatsUtils.ComputeWorldPowerStatsFromSnapshot(settlementsByFaction, playerOutposts);

            var anchorPerFaction = new Dictionary<Faction, Settlement>();
            foreach (var kv in settlementsByFaction)
            {
                if (kv.Value.Count > 0)
                    anchorPerFaction[kv.Key] = kv.Value.RandomElementWithFallback();
            }

            return new DailyWorldSnapshot(settlementsByFaction, playerOutposts, worldPowerStats, anchorPerFaction);
        }
    }
}
