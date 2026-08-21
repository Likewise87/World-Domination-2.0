using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Instant world-road paint / strip for World Setup (Select Starting Site).</summary>
    public static class WD_WorldRoadEditUtility
    {
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

                // NodesReversed is dest-first; walk adjacent hops and pave each edge.
                int links = 0;
                for (int i = nodes.Count - 1; i > 0; i--)
                {
                    PlanetTile a = nodes[i];
                    PlanetTile b = nodes[i - 1];
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
                WorldActions_Roads.RemoveRoadLink(a, b);
                removedLinks++;
            }

            return removedLinks > 0;
        }
    }
}
