using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>After a bridge span is removed: repath or abort travelers that depended on those water tiles.</summary>
    public static class WdBridgeTravelerImpact
    {
        public static void NotifySpanRemoved(IReadOnlyList<int> waterTiles, WorldObject_Traveler except = null)
        {
            if (waterTiles == null || waterTiles.Count == 0) return;
            var removed = new HashSet<int>(waterTiles.Count);
            for (int i = 0; i < waterTiles.Count; i++)
            {
                if (waterTiles[i] >= 0) removed.Add(waterTiles[i]);
            }
            if (removed.Count == 0) return;

            int rerouted = 0;
            int cancelled = 0;
            IReadOnlyList<WorldObject_Traveler> live = WorldObject_Traveler.LiveTravelers;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                WorldObject_Traveler t = live[i];
                if (t == null || t.Destroyed || t.pather == null) continue;
                if (except != null && t == except) continue;
                if (!IsAffected(t, removed)) continue;

                if (IsConstructionTiedToSpan(t, removed))
                {
                    t.pather.CancelMission("TSA_WD_Log_BridgePathInvalidated".Translate(t.Label));
                    cancelled++;
                    continue;
                }

                PlanetTile dest = t.pather.destTile;
                if (!dest.Valid)
                {
                    t.pather.CancelMission("TSA_WD_Log_BridgePathInvalidated".Translate(t.Label));
                    cancelled++;
                    continue;
                }

                t.pather.StopDead();
                t.pather.StartPath(dest, skipLaunchTravelCache: true);
                if (t.Destroyed)
                {
                    cancelled++;
                    continue;
                }
                if (t.pather.moving)
                    rerouted++;
                else
                {
                    t.pather.CancelMission("TSA_WD_Log_BridgePathInvalidated".Translate(t.Label));
                    cancelled++;
                }
            }

            if (rerouted > 0 || cancelled > 0)
            {
                Messages.Message(
                    "TSA_WD_BridgeRemovedTravelerImpact".Translate(rerouted, cancelled),
                    MessageTypeDefOf.NeutralEvent);
            }
        }

        private static bool IsAffected(WorldObject_Traveler t, HashSet<int> removed)
        {
            if (removed.Contains(t.Tile.tileId)) return true;
            if (t.pather.nextTile.Valid && removed.Contains(t.pather.nextTile.tileId)) return true;
            List<int> pathTiles = t.pather.CollectPathExitTileIds();
            if (pathTiles == null) return false;
            for (int i = 0; i < pathTiles.Count; i++)
            {
                if (removed.Contains(pathTiles[i])) return true;
            }
            if (t.cachedPathTiles != null)
            {
                for (int i = 0; i < t.cachedPathTiles.Count; i++)
                {
                    if (removed.Contains(t.cachedPathTiles[i])) return true;
                }
            }
            return false;
        }

        private static bool IsConstructionTiedToSpan(WorldObject_Traveler t, HashSet<int> removed)
        {
            if (!ColonyWorldBuildRequirements.IsWorldBuildConstructionMission(t.mission)
                && t.mission != TravelerMission.BridgeDeconstruct)
                return false;

            // Job site or locked corridor includes removed water — cancel, do not invent a land detour.
            if (t.pather.destTile.Valid && removed.Contains(t.pather.destTile.tileId))
                return true;
            if (t.cachedPathTiles != null)
            {
                for (int i = 0; i < t.cachedPathTiles.Count; i++)
                {
                    if (removed.Contains(t.cachedPathTiles[i]))
                        return true;
                }
            }
            return false;
        }
    }
}
