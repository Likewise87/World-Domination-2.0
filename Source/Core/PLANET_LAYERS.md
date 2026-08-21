# Planet layers, tiles, and WD "surface" guards

Reference for how RimWorld 1.6 (Odyssey) models the planet across multiple layers, why our
old space/surface guards were unreliable, and the correct pattern to use going forward.

All API facts below were verified by reflecting and decoding the IL of `Assembly-CSharp.dll`
(`RimWorldWin64_Data/Managed/Assembly-CSharp.dll`).

## The layer model

Since Odyssey, the world is not a single sphere of tiles. `WorldGrid` holds several
`PlanetLayer` instances:

- `WorldGrid.Surface` (type `SurfaceLayer`) — the root planet surface. (`PlanetLayer.isRootSurface` is `true`
  for it, but that field is `internal` and not accessible from mod code — use reference-equality to
  `WorldGrid.Surface` instead.)
- `WorldGrid.Orbit` (type `PlanetLayer`) — the orbital ring.
- `WorldGrid.PlanetLayers` — an `IReadOnlyDictionary<int, PlanetLayer>` keyed by `layerId` containing
  every layer (surface, orbit, and any others added by content/mods). `layerId` is an arbitrary
  integer (the surface is typically `0`, but do NOT rely on a specific numeric value; use
  reference-equality to `WorldGrid.Surface`).

Each `PlanetLayer` owns its own tile set and its own `WorldPathing pather`. **You cannot path
between layers.** `WorldPathing.FindPath(from, to, ...)` logs an error and bails when
`from.Layer != to.Layer` ("Tried to FindPath to a different layer A -> B").

### `PlanetTile`

`PlanetTile` is a value type:

```
struct PlanetTile { int tileId; int layerId; }
```

- `PlanetTile.Layer` (property) resolves the real `PlanetLayer` from `layerId` via the grid
  (returns `WorldGrid.Surface` for the root, else `WorldGrid.PlanetLayers[layerId]`). This is the
  ONLY correct way to know a tile's layer.
- `PlanetTile.Valid`, `PlanetTile.LayerDef` — convenience accessors.
- `WorldObject.Tile` and `GlobalTargetInfo.Tile` both return a full `PlanetTile` (layer included).

## The trap (what bit us)

Two implicit/overloaded conversions silently discard the layer:

1. `PlanetTile` has an implicit conversion to `int` that returns **only `tileId`** (IL: `ldfld tileId; ret`).
   So `SomeMethod(int)` called with a `PlanetTile` throws away the layer.

2. `WorldGrid` has two indexers:
   - `this[int] -> SurfaceTile` — IL is literally `return this.surface[index]`, i.e. it **always reads
     the surface layer**, regardless of which layer the id "belongs" to.
   - `this[PlanetTile] -> Tile` — layer-correct.

Combined effect of the old guards (which took `int tile` and did `Find.World.grid[tile].Layer`):

```
WorldObject.Tile (PlanetTile 6472, layer 6)
  --> implicit int = 6472                 (layer dropped)
  --> grid[6472] = surface[6472]          (surface tile!)
  --> .Layer == Surface                   (WRONG)
  --> "is surface" == true                (ALWAYS)
```

So `IsPlanetSurfaceTileForWorldActions(int)` was effectively always `true`, and
`IsConfirmedOrbitTile(int)` always `false`. On a single-layer (pre-Odyssey) world this was harmless
because everything really is surface. On a multi-layer world, a settlement on a non-surface layer
passed every "surface" check, WD then built a traveler at that off-surface tile and called
`FindPath(offSurface, surface)` -> the cross-layer error in the player's log.

## The correct pattern

Always resolve the layer from the `PlanetTile` itself, never from `WorldGrid[int]`, and never funnel
a tile through `int`:

```csharp
// surface check
PlanetLayer layer = tile.Layer;                 // layer-aware (uses layerId)
bool isSurface = layer != null &&
                 ReferenceEquals(layer, Find.WorldGrid.Surface);

// layer-correct tile data
Tile t = Find.World.grid[tile];                 // PlanetTile indexer, NOT grid[tile.tileId]
```

Guidelines:

- Guard/scope methods take `PlanetTile`, not `int`. `WorldObject.Tile` flows in unchanged.
- If you only have a bare `tileId`, you must also know its layer; build `new PlanetTile(tileId, layer)`
  explicitly. Do not rely on the implicit `int -> PlanetTile` (it assumes the surface layer).
- When you need tile data (`WaterCovered`, biome, movement difficulty, etc.), index with the
  `PlanetTile` (`grid[pt]`) so you read the right layer.
- WD travel is surface-only. Destinations are built on the destination's own layer (or the origin's
  layer) via `PlanetSurfaceWorldActions.PlanetTileForWdTravel`, never via `grid[int]`.

## WD scope semantics

- `WorldActions_Utils.IsWdSurfaceTile(PlanetTile)` — in WD simulation scope. Returns `true` if the
  tile is on the root surface, OR if layer data is not yet available (unready grid / invalid tile /
  null layer) so world-gen and comp init stay permissive and do not strip every settlement. Non-surface
  layers (orbit and anything else) are out of scope.
- `WorldActions_Utils.IsConfirmedOrbitTile(PlanetTile)` — true only when the layer positively resolves
  to the orbit layer. Used to strip `CompViralSpread`; deliberately conservative so we never strip on
  uncertain/early state.
- `PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(PlanetTile)` — strict: true only for the
  root surface. Used for travel/pathing/targeting validation, where a wrong answer causes cross-layer
  `FindPath`.
- `SpaceMapGuard` — secondary def-name/biome heuristics for space-like maps (SOS2 etc.); a fallback,
  not the primary layer test.
