using System;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// RimWorld 1.6 + Odyssey: primary overworld ground layer is <see cref="WorldGrid.Surface"/> (<see cref="SurfaceLayer"/>).
    /// Vanilla uses it for surface-only logic (e.g. <see cref="TravelUtils.GetHopDifficultyUnits"/> with <c>from.Layer == surface</c>).
    /// WD treats a tile as <b>planet surface</b> for orchestrator + travel when its
    /// <see cref="PlanetTile.Layer"/> is <b>reference-equal</b> to <see cref="WorldGrid.Surface"/>.
    /// Orbit and other layers use different <see cref="PlanetLayer"/> instances.
    /// <para>
    /// IMPORTANT: detection MUST resolve the layer from the <see cref="PlanetTile"/> (its <c>layerId</c>),
    /// never from <c>WorldGrid[int]</c>. The <c>int</c> indexer (<see cref="WorldGrid.get_Item(int)"/>)
    /// is hardcoded to the surface layer (<c>this.surface[index]</c>), and <see cref="PlanetTile"/> has an
    /// implicit <c>-&gt; int</c> conversion that yields only <c>tileId</c>. Funnelling a tile through <c>int</c>
    /// therefore silently discards the layer and makes every tile look like surface. See <c>PLANET_LAYERS.md</c>.
    /// </para>
    /// </summary>
    public static class PlanetSurfaceWorldActions
    {
        public static bool TryGetPlanetSurfaceLayer(out PlanetLayer surface)
        {
            surface = Find.WorldGrid?.Surface;
            return surface != null;
        }

        /// <summary>
        /// The layer WD surface operations (travel, roads, food logistics, trade) run on. WD is surface-only, so this
        /// is the root surface. Use this instead of <c>WorldGrid[int].Layer</c> when only a bare surface tile id is
        /// available: the <c>int</c> indexer returns the surface tile regardless of layer, so it reads as if it were
        /// layer-aware when it is not. When a <see cref="PlanetTile"/> or <see cref="WorldObject"/> is in scope, prefer
        /// <see cref="LayerOf(PlanetTile)"/> / <see cref="LayerOf(WorldObject)"/>.
        /// </summary>
        public static PlanetLayer WdSurfaceLayer => Find.WorldGrid?.Surface;

        /// <summary>Layer a world object actually sits on (resolved via its <see cref="PlanetTile"/>). Null-safe; falls back to the surface layer.</summary>
        public static PlanetLayer LayerOf(WorldObject o) => LayerOf(o != null ? o.Tile : PlanetTile.Invalid);

        /// <summary>Layer a <see cref="PlanetTile"/> sits on (via its <c>layerId</c>), never the surface-only <c>WorldGrid[int]</c>. Falls back to the surface layer.</summary>
        public static PlanetLayer LayerOf(PlanetTile tile)
        {
            if (tile.Valid)
            {
                PlanetLayer l = tile.Layer;
                if (l != null) return l;
            }
            return Find.WorldGrid?.Surface;
        }

        /// <summary>
        /// True only when <paramref name="tile"/> is on the root planet surface layer. Layer-aware: resolves
        /// <see cref="PlanetTile.Layer"/> (via the tile's <c>layerId</c>), not the surface-only <c>WorldGrid[int]</c>.
        /// Returns false for orbit and any other non-surface layer, and for invalid/unready tiles.
        /// </summary>
        public static bool IsPlanetSurfaceTileForWorldActions(PlanetTile tile)
        {
            if (Find.World?.grid == null || Find.WorldGrid == null) return false;
            if (!tile.Valid) return false;
            PlanetLayer gridSurface = Find.WorldGrid.Surface;
            if (gridSurface == null) return false;
            try
            {
                PlanetLayer atTile = tile.Layer;
                return atTile != null && ReferenceEquals(atTile, gridSurface);
            }
            catch (Exception ex)
            {
                Log.Warning($"[WD] IsPlanetSurfaceTileForWorldActions: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        public static bool IsPlanetSurfaceWorldObjectForWorldActions(WorldObject o)
        {
            return o != null && o.Spawned && !o.Destroyed && IsPlanetSurfaceTileForWorldActions(o.Tile);
        }

        /// <summary>
        /// Destination <see cref="PlanetTile"/> for WD travelers, preserving the destination's own layer.
        /// The layer is taken from <paramref name="destTile"/> directly (its <c>layerId</c>), then the origin's
        /// layer, then <see cref="WorldGrid.Surface"/>. This never routes the layer lookup through the
        /// surface-only <c>WorldGrid[int]</c> indexer.
        /// </summary>
        public static PlanetTile PlanetTileForWdTravel(PlanetTile destTile, WorldObject originForLayerFallback)
        {
            PlanetLayer destLayer = destTile.Valid ? destTile.Layer : null;
            destLayer ??= LayerFromOriginOrSurface(originForLayerFallback);
            return new PlanetTile(destTile.tileId, destLayer);
        }

        /// <summary>
        /// Destination <see cref="PlanetTile"/> for WD travelers when only a bare tile id is known. WD travel is
        /// surface-only, so the layer is taken from the origin object (the sender/outpost, already on the surface),
        /// then <see cref="WorldGrid.Surface"/>. Never uses <c>WorldGrid[int]</c> for layer detection.
        /// </summary>
        public static PlanetTile PlanetTileForWdTravel(int destTileId, WorldObject originForLayerFallback)
        {
            return new PlanetTile(destTileId, LayerFromOriginOrSurface(originForLayerFallback));
        }

        private static PlanetLayer LayerFromOriginOrSurface(WorldObject originForLayerFallback)
        {
            if (originForLayerFallback != null && originForLayerFallback.Tile.Valid)
            {
                PlanetLayer originLayer = originForLayerFallback.Tile.Layer;
                if (originLayer != null) return originLayer;
            }
            return Find.WorldGrid?.Surface ?? WorldDomination_UIUtils.GetDefaultPlanetLayer();
        }
    }
}
