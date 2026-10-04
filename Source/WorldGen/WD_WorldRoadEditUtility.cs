using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Instant world-road / bridge paint / strip for World Setup and debug tools.</summary>
    public static class WD_WorldRoadEditUtility
    {
        private static readonly List<int> BridgeWaterScratch = new List<int>(16);

        public static RoadDef ResolveRoadDef(SettlementTier tier) =>
            WorldActions_Roads.GetRoadDefByTier(tier);

        public static bool TryPlaceRoadAlongPath(int fromTile, int toTile, RoadDef road, out string failReason)
        {
            failReason = null;
            if (road == null)
            {
                failReason = "TSA_WD_WorldSetup_RoadDefMissing".Translate();
                return false;
            }

            if (fromTile < 0 || toTile < 0 || fromTile == toTile)
            {
                failReason = "TSA_WD_WorldSetup_RoadNeedTwoTiles".Translate();
                return false;
            }

            PlanetLayer layer = PlanetSurfaceWorldActions.WdSurfaceLayer;
            if (layer == null || Find.WorldGrid == null)
            {
                failReason = "TSA_WD_WorldSetup_WorldMissing".Translate();
                return false;
            }

            using (WorldPath path = layer.Pather.FindPath(
                new PlanetTile(fromTile, layer),
                new PlanetTile(toTile, layer),
                null))
            {
                if (path == null || !path.Found)
                {
                    failReason = "TSA_WD_WorldSetup_RoadNoPath".Translate();
                    return false;
                }

                List<PlanetTile> nodes = path.NodesReversed;
                if (nodes == null || nodes.Count < 2)
                {
                    failReason = "TSA_WD_WorldSetup_RoadNoPath".Translate();
                    return false;
                }

                // NodesReversed is dest-first; walk adjacent hops and pave each land edge only.
                int links = 0;
                for (int i = nodes.Count - 1; i > 0; i--)
                {
                    PlanetTile a = nodes[i];
                    PlanetTile b = nodes[i - 1];
                    if (WorldActions_Roads.IsBridgeOrWaterRoadEdge(a.tileId, b.tileId))
                        continue;
                    WorldActions_Roads.ApplyRoadLink(a, b, road);
                    links++;
                }

                if (links <= 0)
                {
                    failReason = "TSA_WD_WorldSetup_RoadNoPath".Translate();
                    return false;
                }

                return true;
            }
        }

        public static bool TryRemoveRoadsAtTile(int tile, out int removedLinks)
        {
            removedLinks = 0;
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || tile < 0 || !grid.InBounds(tile)) return false;
            if (!(grid[tile] is SurfaceTile surface)) return false;

            List<SurfaceTile.RoadLink> links = surface.potentialRoads;
            if (links == null || links.Count == 0)
                links = surface.Roads;
            if (links == null || links.Count == 0) return false;

            var neighbors = new List<PlanetTile>(links.Count);
            for (int i = 0; i < links.Count; i++)
                neighbors.Add(links[i].neighbor);

            PlanetLayer layer = PlanetSurfaceWorldActions.WdSurfaceLayer;
            for (int i = 0; i < neighbors.Count; i++)
            {
                PlanetTile a = layer != null ? new PlanetTile(tile, layer) : new PlanetTile(tile);
                PlanetTile b = neighbors[i];
                // Bridge deck links are destroyed only via TryDestroyBridgeAtTile.
                if (WorldActions_Roads.IsBridgeOrWaterRoadEdge(a.tileId, b.tileId))
                    continue;
                WorldActions_Roads.RemoveRoadLink(a, b);
                removedLinks++;
            }

            return removedLinks > 0;
        }

        /// <summary>Instant stone-bridge paint between two land banks (World Setup / debug).</summary>
        public static bool TryPlaceBridgeBetweenBanks(int startBank, int endBank, out string failReason)
        {
            failReason = null;
            if (startBank < 0 || endBank < 0 || startBank == endBank)
            {
                failReason = "TSA_WD_WorldSetup_RoadNeedTwoTiles".Translate();
                return false;
            }

            if (!WdBridgeGeometry.TryResolveSpan(startBank, endBank, out List<int> chain, out WdBridgeGeometry.RejectReason reason)
                || chain == null)
            {
                failReason = WorldActions_BuildBridge.RejectMessage(reason);
                return false;
            }

            RoadDef road = WorldActions_BuildBridge.GetBridgeRoadDef();
            if (road == null)
            {
                failReason = "TSA_WD_WorldSetup_RoadDefMissing".Translate();
                return false;
            }

            WorldActions_BuildBridge.PaintEntireSpan(chain, road);
            Find.WorldReachability?.ClearCache();
            return true;
        }

        /// <summary>Destroy the painted WD bridge that owns this bank or water tile.</summary>
        public static bool TryDestroyBridgeAtTile(int tile, out string failReason)
        {
            failReason = null;
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || tile < 0 || !grid.InBounds(tile))
            {
                failReason = "TSA_WD_WorldSetup_InvalidTile".Translate();
                return false;
            }

            List<int> chain = null;
            if (grid[tile].WaterCovered)
            {
                if (!WdBridgeGeometry.TryGetBridgedSpanBanks(tile, out int bankA, out _)
                    || !WdBridgeGeometry.TryResolveExistingBridgeFromBank(bankA, out chain, out _))
                {
                    failReason = "TSA_WD_WorldSetup_DestroyBridgeNone".Translate();
                    return false;
                }
            }
            else if (!WdBridgeGeometry.TryResolveExistingBridgeFromBank(tile, out chain, out _))
            {
                failReason = "TSA_WD_WorldSetup_DestroyBridgeNone".Translate();
                return false;
            }

            if (chain == null || chain.Count < 2)
            {
                failReason = "TSA_WD_WorldSetup_DestroyBridgeNone".Translate();
                return false;
            }

            BridgeWaterScratch.Clear();
            WdBridgeGeometry.CollectWaterTiles(chain, BridgeWaterScratch);
            for (int i = 0; i < BridgeWaterScratch.Count; i++)
                WorldActions_Fortifications.TryClearAt(BridgeWaterScratch[i]);

            WorldActions_BuildBridge.UnlinkEntireSpan(chain);

            WorldComponent_WdBridges bridges = WorldComponent_WdBridges.Get();
            for (int i = 0; i < BridgeWaterScratch.Count; i++)
                bridges?.RefreshTileAfterRoadChange(BridgeWaterScratch[i]);
            bridges?.RecalculatePathCostsForTiles(BridgeWaterScratch);
            Find.WorldReachability?.ClearCache();
            WD_WorldLayer_MovementDifficultyOverlay.InvalidateAndDirtyIfActive();
            WdBridgeTravelerImpact.NotifySpanRemoved(BridgeWaterScratch, except: null);
            return true;
        }

        public static bool TileHasRemovableLandRoad(int tile)
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || tile < 0 || !grid.InBounds(tile)) return false;
            if (!(grid[tile] is SurfaceTile surface)) return false;
            List<SurfaceTile.RoadLink> links = surface.potentialRoads;
            if (links == null || links.Count == 0)
                links = surface.Roads;
            if (links == null || links.Count == 0) return false;
            for (int i = 0; i < links.Count; i++)
            {
                if (!WorldActions_Roads.IsBridgeOrWaterRoadEdge(tile, links[i].neighbor.tileId))
                    return true;
            }
            return false;
        }

        public static bool TileHasDestroyableBridge(int tile)
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || tile < 0 || !grid.InBounds(tile)) return false;
            if (grid[tile].WaterCovered)
                return WorldComponent_WdBridges.IsBridgedWaterTile(tile);
            return WdBridgeGeometry.TryResolveExistingBridgeFromBank(tile, out _, out _);
        }
    }
}
