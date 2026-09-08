using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Daily special: when fewer than 2 hostile factions threaten the player, force one eligible
    /// faction to found 1–2 settlements near the player (Isolation Pressure).
    /// </summary>
    public static class WorldActions_IsolationPressure
    {
        private static readonly List<Settlement> tmpFactionSites = new List<Settlement>();
        private static readonly HashSet<int> tmpExcludeDest = new HashSet<int>();
        private static readonly HashSet<int> tmpSeenFactionIds = new HashSet<int>();

        public static void TryTrigger(WorldComponent_SpreadManager manager, DailyWorldSnapshot snapshot = null)
        {
            if (manager == null || Current.ProgramState != ProgramState.Playing) return;
            var seth = WorldDominationMod.settings;
            if (seth == null) return;
            if (seth.gateThreatIsolationPressure == WdThreatStageGate.Never) return;
            if (!WdEscalation.PassesGate(seth.gateThreatIsolationPressure, manager)) return;

            if (CountThreateningHostileFactions(manager) >= 2) return;

            float chance = Mathf.Clamp01(seth.isolationPressureChance);
            if (chance <= 0f || Rand.Value >= chance) return;

            if (!TryGetPlayerAnchorTile(out int playerAnchor)) return;

            if (!TryPickFactionAndParents(manager, playerAnchor, out Faction faction, out List<Settlement> parents))
                return;

            int want = Rand.RangeInclusive(
                Mathf.Max(1, seth.isolationPressureCountMin),
                Mathf.Max(seth.isolationPressureCountMin, seth.isolationPressureCountMax));
            int minTiles = Mathf.Max(1, seth.isolationPressureMinTiles);
            int maxTiles = Mathf.Max(minTiles, seth.isolationPressureMaxTiles);

            tmpExcludeDest.Clear();
            int launched = 0;
            for (int i = 0; i < parents.Count && launched < want; i++)
            {
                Settlement parent = parents[i];
                if (parent == null || parent.Destroyed) continue;
                var comp = parent.GetComponent<CompViralSpread>();
                if (comp == null) continue;
                if (WorldActions_GrowthExpand.TryLaunchForcedExpansionNearPlayer(
                        parent, comp, manager, playerAnchor, minTiles, maxTiles,
                        markIsolationPressure: true, tmpExcludeDest))
                    launched++;
            }

            if (launched < 1) return;

            StampFactionCooldown(manager, faction, seth);
            manager.AddLog(new SpreadLogEntry(
                "TSA_WD_Log_IsolationPressure".Translate(faction.Name, launched.ToString()),
                parents[0]));
            WDVerbose.Msg($"IsolationPressure faction={faction.Name} launched={launched} anchor={playerAnchor}");
        }

        private static int CountThreateningHostileFactions(WorldComponent_SpreadManager manager)
        {
            tmpSeenFactionIds.Clear();
            var threats = manager.ThreatSettlements;
            if (threats == null) return 0;
            for (int i = 0; i < threats.Count; i++)
            {
                Faction f = threats[i].faction;
                if (f == null || f.IsPlayer || f.def.hidden || f.defeated) continue;
                tmpSeenFactionIds.Add(f.loadID);
            }
            return tmpSeenFactionIds.Count;
        }

        private static bool TryGetPlayerAnchorTile(out int tileId)
        {
            tileId = -1;
            Settlement colony = InfluenceUtils.GetPlayerColony();
            if (colony != null && colony.Tile >= 0)
            {
                tileId = colony.Tile.tileId;
                return true;
            }
            Faction player = Faction.OfPlayerSilentFail;
            if (player == null || Find.WorldObjects == null) return false;
            var all = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is MapParent mp && mp.Faction == player && mp.HasMap && mp.Tile >= 0
                    && PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(mp))
                {
                    tileId = mp.Tile.tileId;
                    return true;
                }
            }
            return false;
        }

        private static bool TryPickFactionAndParents(
            WorldComponent_SpreadManager manager,
            int playerAnchor,
            out Faction faction,
            out List<Settlement> parents)
        {
            faction = null;
            parents = tmpFactionSites;
            parents.Clear();

            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return false;

            // Rank all hostile WD settlements by distance to player; first faction not on CD wins.
            Settlement bestSite = null;
            float bestDist = float.MaxValue;
            Faction bestFaction = null;

            var settlements = Find.WorldObjects?.Settlements;
            if (settlements == null) return false;

            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement s = settlements[i];
                if (!DailyWorldSnapshot.IsSettlementStillValid(s)) continue;
                if (s.Faction == null || s.Faction.IsPlayer || s.Faction.def.hidden || s.Faction.defeated) continue;
                if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(s)) continue;
                if (!WorldActions_Utils.IsWdParticipant(s.Faction)) continue;
                if (!WorldActions_Utils.SafeHostileTo(s.Faction, Faction.OfPlayer)) continue;
                if (IsFactionOnCooldown(manager, s.Faction)) continue;

                float d = grid.ApproxDistanceInTiles(playerAnchor, s.Tile);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestSite = s;
                    bestFaction = s.Faction;
                }
            }

            if (bestFaction == null || bestSite == null) return false;
            faction = bestFaction;

            // Collect same-faction sites sorted by distance (nearest first) for multi-launch.
            parents.Clear();
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement s = settlements[i];
                if (!DailyWorldSnapshot.IsSettlementStillValid(s)) continue;
                if (s.Faction != faction) continue;
                if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(s)) continue;
                if (s.GetComponent<CompViralSpread>() == null) continue;
                parents.Add(s);
            }

            parents.Sort((a, b) =>
                grid.ApproxDistanceInTiles(playerAnchor, a.Tile)
                    .CompareTo(grid.ApproxDistanceInTiles(playerAnchor, b.Tile)));

            return parents.Count > 0;
        }

        private static bool IsFactionOnCooldown(WorldComponent_SpreadManager manager, Faction faction)
        {
            if (manager?.isolationPressureCooldownByFaction == null || faction == null) return false;
            if (!manager.isolationPressureCooldownByFaction.TryGetValue(faction.loadID, out int until)) return false;
            return Find.TickManager.TicksGame < until;
        }

        private static void StampFactionCooldown(
            WorldComponent_SpreadManager manager,
            Faction faction,
            WorldDominationSettings seth)
        {
            if (manager == null || faction == null || seth == null) return;
            if (manager.isolationPressureCooldownByFaction == null)
                manager.isolationPressureCooldownByFaction = new Dictionary<int, int>();
            float days = Mathf.Max(0.1f, seth.isolationPressureCooldownDays);
            manager.isolationPressureCooldownByFaction[faction.loadID] =
                Find.TickManager.TicksGame + CompViralSpread.CooldownTicksFromDays(days);
        }
    }
}
