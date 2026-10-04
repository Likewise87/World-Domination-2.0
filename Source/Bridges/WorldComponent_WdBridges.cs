using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// O(1) registry of water tiles that carry WD road bridges (passable land travel + clash map).
    /// </summary>
    public class WorldComponent_WdBridges : WorldComponent
    {
        private List<int> bridgedWaterTiles = new List<int>();
        private HashSet<int> bridgedSet = new HashSet<int>();
        private HashSet<int> exclusiveCompleteBanks;
        private bool exclusiveBanksDirty = true;
        private bool pathCostsHealedThisSession;
        private static readonly HashSet<int> ExclusiveBankScratch = new HashSet<int>(32);

        private static WorldComponent_WdBridges cached;
        private static World cachedWorld;

        public const int MaxWaterTiles = 10;
        /// <summary>End bank is at most this many hops from the start bank (water span + landing tile).</summary>
        public const int MaxBridgeTargetRange = MaxWaterTiles + 1;

        public bool HasAnyBridges => bridgedSet != null && bridgedSet.Count > 0;

        public IReadOnlyList<int> BridgedWaterTiles => bridgedWaterTiles;

        public WorldComponent_WdBridges(World world) : base(world)
        {
            cached = this;
            cachedWorld = world;
        }

        public static WorldComponent_WdBridges Get()
        {
            World w = Find.World;
            if (w == null) return null;
            if (cached != null && cachedWorld == w) return cached;
            cached = w.GetComponent<WorldComponent_WdBridges>();
            cachedWorld = w;
            return cached;
        }

        public static bool IsBridgedWaterTile(int tileId)
        {
            WorldComponent_WdBridges c = Get();
            return c != null && c.bridgedSet != null && c.bridgedSet.Contains(tileId);
        }

        public static bool IsBridgedWaterTile(PlanetTile tile) =>
            tile.Valid && IsBridgedWaterTile(tile.tileId);

        public override void FinalizeInit(bool fromLoad)
        {
            base.FinalizeInit(fromLoad);
            RebuildSetFromList();
            RelabelLandStoneBridgeLinksToStoneRoad();
            if (fromLoad)
                TryRebuildFromPotentialRoads();
            PruneWatersWithoutStoneBridge();
            WD_WorldLayer_BridgeRoads.EnsureRegistered();
            WD_WorldLayer_BridgeTargetFill.EnsureRegistered();
            if (HasAnyBridges)
            {
                // Path-grid / reachability may have been built before bridge membership was restored.
                RecalculatePathCostsForTiles(bridgedWaterTiles);
                pathCostsHealedThisSession = true;
                WD_WorldLayer_BridgeRoads.SetDirty();
            }
        }

        /// <summary>
        /// One-shot heal for sessions that already had bridges registered before path costs
        /// / reachability were refreshed (e.g. after a hot-reload of the mod DLL).
        /// </summary>
        public override void WorldComponentUpdate()
        {
            if (pathCostsHealedThisSession || !HasAnyBridges) return;
            pathCostsHealedThisSession = true;
            RecalculatePathCostsForTiles(bridgedWaterTiles);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref bridgedWaterTiles, "wdBridgedWaterTiles", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (bridgedWaterTiles == null) bridgedWaterTiles = new List<int>();
                bridgedWaterTiles.RemoveAll(id => id < 0);
                RebuildSetFromList();
                RelabelLandStoneBridgeLinksToStoneRoad();
                TryRebuildFromPotentialRoads();
                PruneWatersWithoutStoneBridge();
            }
        }

        public void RegisterBridgeWaterTiles(IReadOnlyList<int> waterTileIds)
        {
            if (waterTileIds == null || waterTileIds.Count == 0) return;
            bool changed = false;
            for (int i = 0; i < waterTileIds.Count; i++)
            {
                int id = waterTileIds[i];
                if (id < 0 || bridgedSet.Contains(id)) continue;
                bridgedSet.Add(id);
                bridgedWaterTiles.Add(id);
                changed = true;
            }
            if (changed)
            {
                InvalidateExclusiveBanks();
                WD_WorldLayer_BridgeRoads.SetDirty();
            }
        }

        public void UnregisterTile(int tileId)
        {
            if (tileId < 0 || !bridgedSet.Remove(tileId)) return;
            bridgedWaterTiles.Remove(tileId);
            InvalidateExclusiveBanks();
            WD_WorldLayer_BridgeRoads.SetDirty();
        }

        /// <summary>After road links change on a water tile: drop bridge membership when no potentialRoads remain.</summary>
        public void RefreshTileAfterRoadChange(int tileId)
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || tileId < 0 || !grid.InBounds(tileId)) return;
            if (!(grid[tileId] is SurfaceTile surface) || !surface.WaterCovered)
            {
                UnregisterTile(tileId);
                return;
            }

            if (TileHasStoneBridgeLink(surface))
            {
                if (!bridgedSet.Contains(tileId))
                {
                    bridgedSet.Add(tileId);
                    bridgedWaterTiles.Add(tileId);
                    InvalidateExclusiveBanks();
                    WD_WorldLayer_BridgeRoads.SetDirty();
                }
            }
            else
            {
                UnregisterTile(tileId);
            }
        }

        public void InvalidateExclusiveBanks() => exclusiveBanksDirty = true;

        /// <summary>Land banks of fully built bridges (stubs omitted so resume stays clickable).</summary>
        public void CopyExclusiveCompleteBanksInto(HashSet<int> into)
        {
            if (into == null) return;
            EnsureExclusiveCompleteBanks();
            if (exclusiveCompleteBanks == null) return;
            foreach (int bank in exclusiveCompleteBanks)
                into.Add(bank);
        }

        private void EnsureExclusiveCompleteBanks()
        {
            if (!exclusiveBanksDirty && exclusiveCompleteBanks != null) return;
            exclusiveCompleteBanks ??= new HashSet<int>(32);
            exclusiveCompleteBanks.Clear();
            if (!HasAnyBridges)
            {
                exclusiveBanksDirty = false;
                return;
            }

            WdBridgeGeometry.CollectAnchoredBanksFromBridgedWaters(ExclusiveBankScratch);
            foreach (int bank in ExclusiveBankScratch)
            {
                if (exclusiveCompleteBanks.Contains(bank)) continue;
                if (!WdBridgeGeometry.TryResolveExistingBridgeFromBank(bank, out List<int> chain, out _))
                    continue;
                if (!WdBridgeGeometry.SpanIsFullyBridged(chain)) continue;
                exclusiveCompleteBanks.Add(chain[0]);
                int end = chain[chain.Count - 1];
                if (end >= 0) exclusiveCompleteBanks.Add(end);
            }
            exclusiveBanksDirty = false;
        }

        public void RecalculatePathCostsForTiles(IReadOnlyList<int> tileIds)
        {
            if (tileIds == null || Find.World?.pathGrid == null) return;
            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return;
            PlanetLayer layer = PlanetSurfaceWorldActions.WdSurfaceLayer ?? grid.Surface;
            if (layer == null) return;

            WorldPathGrid pathGrid = Find.World.pathGrid;
            if (!pathGrid.layerMovementDifficulty.TryGetValue(layer, out float[] costs) || costs == null)
                return;

            bool touchedBridge = false;
            for (int i = 0; i < tileIds.Count; i++)
            {
                int id = tileIds[i];
                if (id < 0 || id >= costs.Length || !grid.InBounds(id)) continue;

                PlanetTile pt = new PlanetTile(id, layer);
                pathGrid.RecalculatePerceivedMovementDifficultyAt(pt, out _);

                // FindPath reads this array directly (not PerceivedMovementDifficultyAt).
                // Force land-like cost for bridged water if a recalc race left ocean at 1000.
                if (bridgedSet.Contains(id))
                {
                    touchedBridge = true;
                    if (costs[id] >= 1000f)
                        costs[id] = 1f;
                }
            }

            // Without this, CanReach still treats opposite banks as disconnected islands
            // even after path costs are correct.
            if (touchedBridge)
                Find.WorldReachability?.ClearCache();
        }

        /// <summary>
        /// TSA_WD_StoneBridge used the same world-gen segment length as StoneRoad, so vanilla
        /// painted it on inland stone routes. Rewrite land-land links to StoneRoad.
        /// Water links stay as stone bridge.
        /// </summary>
        public static void RelabelLandStoneBridgeLinksToStoneRoad()
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return;
            RoadDef bridge = DefDatabase<RoadDef>.GetNamedSilentFail("TSA_WD_StoneBridge");
            RoadDef stone = DefDatabase<RoadDef>.GetNamedSilentFail("StoneRoad");
            if (bridge == null || stone == null || bridge == stone) return;

            PlanetLayer layer = PlanetSurfaceWorldActions.WdSurfaceLayer ?? grid.Surface;
            for (int i = 0; i < grid.TilesCount; i++)
            {
                if (!(grid[i] is SurfaceTile surface) || surface.WaterCovered) continue;
                List<SurfaceTile.RoadLink> links = surface.potentialRoads;
                if (links == null) continue;
                for (int li = 0; li < links.Count; li++)
                {
                    SurfaceTile.RoadLink link = links[li];
                    if (link.road != bridge) continue;
                    int n = link.neighbor.tileId;
                    if (n < 0 || !grid.InBounds(n) || grid[n].WaterCovered) continue;
                    links[li] = new SurfaceTile.RoadLink { neighbor = link.neighbor, road = stone };
                    RelabelReverseLink(grid, n, i, layer, stone);
                }
            }
        }

        private static void RelabelReverseLink(
            WorldGrid grid, int tileId, int towardTileId, PlanetLayer layer, RoadDef stone)
        {
            if (!(grid[tileId] is SurfaceTile surface) || surface.potentialRoads == null) return;
            List<SurfaceTile.RoadLink> links = surface.potentialRoads;
            PlanetTile toward = new PlanetTile(towardTileId, layer);
            for (int i = 0; i < links.Count; i++)
            {
                SurfaceTile.RoadLink link = links[i];
                if (link.neighbor.tileId != towardTileId && link.neighbor != toward) continue;
                links[i] = new SurfaceTile.RoadLink { neighbor = link.neighbor, road = stone };
                return;
            }
        }

        private void RebuildSetFromList()
        {
            if (bridgedWaterTiles == null) bridgedWaterTiles = new List<int>();
            bridgedSet = new HashSet<int>(bridgedWaterTiles.Count);
            for (int i = 0; i < bridgedWaterTiles.Count; i++)
            {
                int id = bridgedWaterTiles[i];
                if (id >= 0) bridgedSet.Add(id);
            }
        }

        private static bool TileHasStoneBridgeLink(SurfaceTile surface)
        {
            List<SurfaceTile.RoadLink> links = surface?.potentialRoads;
            if (links == null || links.Count == 0) return false;
            RoadDef stoneBridge = DefDatabase<RoadDef>.GetNamedSilentFail("TSA_WD_StoneBridge");
            if (stoneBridge == null) return links.Count > 0;
            for (int i = 0; i < links.Count; i++)
            {
                if (links[i].road == stoneBridge)
                    return true;
            }
            return false;
        }

        private void PruneWatersWithoutStoneBridge()
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || bridgedWaterTiles == null || bridgedWaterTiles.Count == 0) return;
            bool changed = false;
            for (int i = bridgedWaterTiles.Count - 1; i >= 0; i--)
            {
                int id = bridgedWaterTiles[i];
                if (id < 0 || !grid.InBounds(id) || !(grid[id] is SurfaceTile surface)
                    || !surface.WaterCovered || !TileHasStoneBridgeLink(surface))
                {
                    bridgedWaterTiles.RemoveAt(i);
                    bridgedSet.Remove(id);
                    changed = true;
                }
            }
            if (changed)
            {
                InvalidateExclusiveBanks();
                WD_WorldLayer_BridgeRoads.SetDirty();
            }
        }

        private void TryRebuildFromPotentialRoads()
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return;
            bool changed = false;
            for (int i = 0; i < grid.TilesCount; i++)
            {
                if (!(grid[i] is SurfaceTile surface) || !surface.WaterCovered) continue;
                if (bridgedSet.Contains(i)) continue;
                if (!TileHasStoneBridgeLink(surface)) continue;
                bridgedSet.Add(i);
                bridgedWaterTiles.Add(i);
                changed = true;
            }
            if (changed)
                InvalidateExclusiveBanks();
        }
    }
}
