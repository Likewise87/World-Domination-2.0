using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Scoped FindPath bias: known hostile settlements (and for NPC movers, player outposts) get a
    /// ring-lerped flat difficulty so A* prefers detours. Pathfinding only; dispose before hop timing.
    /// Destination site disk is omitted so road-block funnels into a target still work.
    /// </summary>
    public static class WdHostileSiteAvoidPathContext
    {
        [ThreadStatic] private static int depth;
        [ThreadStatic] private static Dictionary<int, float> tilePenalties;
        [ThreadStatic] private static Dictionary<int, float> reusableMap;

        private static readonly List<PlanetTile> neighborScratch = new List<PlanetTile>(8);
        private static readonly List<int> bfsQueue = new List<int>(256);
        private static readonly Dictionary<int, int> bfsDepth = new Dictionary<int, int>(256);

        public static bool Active => depth > 0;

        public static bool TryGetPenalty(int tileId, out float penalty)
        {
            penalty = 0f;
            if (depth <= 0 || tilePenalties == null || tilePenalties.Count == 0) return false;
            return tilePenalties.TryGetValue(tileId, out penalty) && penalty > 0f;
        }

        /// <summary>WD traveler FindPath: gated by player / NPC WD pathfinding toggles.</summary>
        public static Scope TryActivateForTraveler(Faction mover, int destTileId)
        {
            if (mover == null) return null;
            var s = WorldDominationMod.settings;
            if (s == null) return null;
            if (mover.IsPlayer)
            {
                if (!s.playerCaravansAvoidHostileSettlements) return null;
            }
            else if (!s.npcCaravansAvoidHostileSettlements)
                return null;

            return new Scope(mover, destTileId);
        }

        /// <summary>Vanilla player pawn Caravan FindPath: gated by the real-caravan pathfinding toggle.</summary>
        public static Scope TryActivateForPlayerPawnCaravan(Faction mover, int destTileId)
        {
            if (mover == null || !mover.IsPlayer) return null;
            var s = WorldDominationMod.settings;
            if (s == null || !s.playerPawnCaravanPathfindingAdjustments) return null;
            return new Scope(mover, destTileId);
        }

        public sealed class Scope : IDisposable
        {
            private readonly Dictionary<int, float> previousPenalties;
            private bool disposed;

            public Scope(Faction mover, int destTileId)
            {
                previousPenalties = tilePenalties;
                Dictionary<int, float> map;
                if (depth == 0)
                {
                    if (reusableMap == null)
                        reusableMap = new Dictionary<int, float>(256);
                    else
                        reusableMap.Clear();
                    map = reusableMap;
                }
                else
                    map = new Dictionary<int, float>(256);

                BuildPenalties(map, mover, destTileId);
                tilePenalties = map;
                depth++;
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                depth = Math.Max(0, depth - 1);
                if (depth == 0)
                {
                    tilePenalties?.Clear();
                    tilePenalties = null;
                }
                else
                    tilePenalties = previousPenalties;
            }
        }

        private static void BuildPenalties(Dictionary<int, float> map, Faction mover, int destTileId)
        {
            var s = WorldDominationMod.settings;
            if (s == null || map == null || mover == null) return;

            int radius = Mathf.Clamp(s.hostileSettlementPathAvoidRadius, 0, 8);
            float inner = Mathf.Clamp(s.hostileSettlementPathAvoidPenaltyInner, 0f, 10f);
            float outer = Mathf.Clamp(s.hostileSettlementPathAvoidPenaltyOuter, 0f, 10f);
            if (inner < outer) inner = outer;
            if (inner <= 0f && outer <= 0f) return;

            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return;

            WorldObject exemptSite = FindSiteAtTile(destTileId);
            bool fowTracking = WorldComponent_SettlementIntel.IsTracking();
            var intel = fowTracking ? WorldComponent_SettlementIntel.Get() : null;
            bool includeOutposts = !mover.IsPlayer;

            List<Settlement> settlements = Find.WorldObjects?.Settlements;
            if (settlements != null)
            {
                for (int i = 0; i < settlements.Count; i++)
                {
                    Settlement site = settlements[i];
                    if (!ShouldSeedSite(site, mover, exemptSite, fowTracking, intel)) continue;
                    FloodSiteDisk(map, grid, site.Tile.tileId, radius, inner, outer);
                }
            }

            if (includeOutposts)
            {
                IReadOnlyList<WorldObject_WD_Outpost> outposts = WdPlayerOutpostCache.PlayerOutposts;
                for (int i = 0; i < outposts.Count; i++)
                {
                    WorldObject_WD_Outpost o = outposts[i];
                    if (!ShouldSeedSite(o, mover, exemptSite, fowTracking, intel)) continue;
                    FloodSiteDisk(map, grid, o.Tile.tileId, radius, inner, outer);
                }
            }
        }

        private static bool ShouldSeedSite(
            WorldObject site,
            Faction mover,
            WorldObject exemptSite,
            bool fowTracking,
            WorldComponent_SettlementIntel intel)
        {
            if (site == null || site.Destroyed) return false;
            if (ReferenceEquals(site, exemptSite)) return false;
            Faction sf = site.Faction;
            if (sf == null || sf == mover) return false;
            if (!WorldActions_Utils.SafeHostileTo(mover, sf)) return false;
            if (fowTracking)
            {
                if (intel == null) return false;
                if (!intel.IsDiscovered(mover, site)) return false;
            }
            return true;
        }

        private static WorldObject FindSiteAtTile(int tileId)
        {
            if (tileId < 0 || Find.WorldObjects == null) return null;
            PlanetLayer layer = PlanetSurfaceWorldActions.WdSurfaceLayer ?? Find.WorldGrid?.Surface;
            if (layer != null)
            {
                PlanetTile pt = new PlanetTile(tileId, layer);
                Settlement s = Find.WorldObjects.SettlementAt(pt);
                if (s != null && !s.Destroyed) return s;
                foreach (WorldObject wo in Find.WorldObjects.ObjectsAt(pt))
                {
                    if (wo is WorldObject_WD_Outpost o && !o.Destroyed) return o;
                }
            }

            Settlement s2 = Find.WorldObjects.SettlementAt(tileId);
            if (s2 != null && !s2.Destroyed) return s2;
            foreach (WorldObject wo in Find.WorldObjects.ObjectsAt(tileId))
            {
                if (wo is WorldObject_WD_Outpost o && !o.Destroyed) return o;
            }
            return null;
        }

        private static void FloodSiteDisk(
            Dictionary<int, float> map,
            WorldGrid grid,
            int centerTile,
            int radius,
            float inner,
            float outer)
        {
            if (centerTile < 0) return;
            if (radius <= 0)
            {
                Accrue(map, centerTile, inner);
                return;
            }

            bfsQueue.Clear();
            bfsDepth.Clear();
            bfsQueue.Add(centerTile);
            bfsDepth[centerTile] = 0;
            Accrue(map, centerTile, inner);

            for (int qi = 0; qi < bfsQueue.Count; qi++)
            {
                int cur = bfsQueue[qi];
                int d = bfsDepth[cur];
                if (d >= radius) continue;

                neighborScratch.Clear();
                PlanetLayer layer = grid[cur].Layer;
                grid.GetTileNeighbors(new PlanetTile(cur, layer), neighborScratch);

                int nextDepth = d + 1;
                float t = nextDepth / (float)radius;
                float penalty = Mathf.Lerp(inner, outer, t);
                for (int n = 0; n < neighborScratch.Count; n++)
                {
                    PlanetTile npt = neighborScratch[n];
                    if (!npt.Valid) continue;
                    int nb = npt.tileId;
                    if (bfsDepth.ContainsKey(nb)) continue;
                    bfsDepth[nb] = nextDepth;
                    bfsQueue.Add(nb);
                    Accrue(map, nb, penalty);
                }
            }
        }

        private static void Accrue(Dictionary<int, float> map, int tileId, float penalty)
        {
            if (penalty <= 0f) return;
            if (map.TryGetValue(tileId, out float existing))
            {
                if (penalty > existing)
                    map[tileId] = penalty;
            }
            else
                map[tileId] = penalty;
        }
    }
}
