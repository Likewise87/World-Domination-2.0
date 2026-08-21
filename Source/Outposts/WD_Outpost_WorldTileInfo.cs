using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Tile/biome helpers. Uses BiomeDef API for terrain, plants, animals, stones; one compatibility shim for GetBiome/GetHilliness when grid returns SurfaceTile. The farming fertility percentage shown in outpost UI is computed in <see cref="WorldTileProductivity.GetFarmingFertilityScore"/>.</summary>
    public static class WorldTileInfo
    {
        /// <summary>Optional: set to resolve biome from tile when grid returns SurfaceTile (no .biome). Leave null to use grid tile.biome or reflection fallback.</summary>
        public static Func<int, BiomeDef> BiomeResolver;

        /// <summary>Optional: set to resolve hilliness from tile. Leave null to use grid tile.hilliness or reflection fallback.</summary>
        public static Func<int, Hilliness> HillinessResolver;

        private static Func<int, BiomeDef> _biomeGetter;
        private static Func<int, Hilliness> _hillinessGetter;

        private static void EnsureTileAccessors()
        {
            if (_biomeGetter != null) return;
            if (BiomeResolver != null) { _biomeGetter = BiomeResolver; _hillinessGetter = HillinessResolver ?? GetHillinessReflection; return; }
            var grid = Find.WorldGrid;
            if (grid == null) return;
            var gridType = grid.GetType();
            var indexer = gridType.GetProperty("Item", new[] { typeof(int) })?.GetMethod;
            if (indexer == null) return;
            var tileType = indexer.ReturnType;
            var biomeProp = tileType.GetProperty("biome", BindingFlags.Public | BindingFlags.Instance);
            var hillProp = tileType.GetProperty("hilliness", BindingFlags.Public | BindingFlags.Instance)
                ?? tileType.GetProperty("Hilliness", BindingFlags.Public | BindingFlags.Instance);
            if (biomeProp != null && typeof(BiomeDef).IsAssignableFrom(biomeProp.PropertyType))
                _biomeGetter = t => { var tile = indexer.Invoke(grid, new object[] { t }); return tile != null ? biomeProp.GetValue(tile) as BiomeDef : null; };
            else
                _biomeGetter = t => { var name = GetBiomeNameFromReflection(t); return string.IsNullOrEmpty(name) ? null : DefDatabase<BiomeDef>.GetNamedSilentFail(name); };
            _hillinessGetter = (hillProp != null && (hillProp.PropertyType == typeof(Hilliness) || hillProp.PropertyType.IsEnum))
                ? (Func<int, Hilliness>)(t => { var tile = indexer.Invoke(grid, new object[] { t }); if (tile == null) return Hilliness.Flat; var v = hillProp.GetValue(tile); return v is Hilliness h ? h : Hilliness.Flat; })
                : GetHillinessReflection;
        }

        private static string GetBiomeNameFromReflection(int tile)
        {
            var grid = Find.WorldGrid;
            if (grid == null || tile < 0 || tile >= grid.TilesCount) return null;
            var tileObj = grid.GetType().GetProperty("Item", new[] { typeof(int) })?.GetMethod?.Invoke(grid, new object[] { tile });
            if (tileObj == null) return null;
            var t = tileObj.GetType();
            Def biome = null;
            var prop = t.GetProperty("biome", BindingFlags.Public | BindingFlags.Instance) ?? t.GetProperty("Biome", BindingFlags.Public | BindingFlags.Instance);
            if (prop != null) biome = prop.GetValue(tileObj) as Def;
            if (biome == null) { var f = t.GetField("biome", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) ?? t.GetField("Biome", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance); if (f != null) biome = f.GetValue(tileObj) as Def; }
            return biome?.defName;
        }

        private static Hilliness GetHillinessReflection(int tile)
        {
            var grid = Find.WorldGrid;
            if (grid == null || tile < 0 || tile >= grid.TilesCount) return Hilliness.Flat;
            var tileObj = grid.GetType().GetProperty("Item", new[] { typeof(int) })?.GetMethod?.Invoke(grid, new object[] { tile });
            if (tileObj == null) return Hilliness.Flat;
            var h = GetHillinessFromTileObject(tileObj);
            return h ?? Hilliness.Flat;
        }

        /// <summary>Biome at this tile, or null if invalid. Same as Outposts: read from Find.WorldGrid[tile].biome via reflection (grid indexer returns tile, tile has biome).</summary>
        public static BiomeDef GetBiome(int tile)
        {
            if (Find.WorldGrid == null || tile < 0 || tile >= Find.WorldGrid.TilesCount)
                return null;
            // Outposts: var biome = Find.WorldGrid[tileIdx].biome; we do the same by indexing then reading .biome from the boxed tile
            try
            {
                object gridTile = Find.WorldGrid[tile];
                if (gridTile != null)
                {
                    BiomeDef b = GetBiomeFromTileObject(gridTile);
                    if (b != null) return b;
                }
            }
            catch { /* fall through */ }
            if (BiomeResolver != null) return BiomeResolver(tile);
            EnsureTileAccessors();
            var bg = _biomeGetter?.Invoke(tile);
            if (bg != null) return bg;
            var name = GetBiomeNameFromReflection(tile);
            return string.IsNullOrEmpty(name) ? null : DefDatabase<BiomeDef>.GetNamedSilentFail(name);
        }

        /// <summary>Read BiomeDef from a boxed Tile. Same as Outposts (tile.biome) and VOE (tile.PrimaryBiome). Tries biome, then PrimaryBiome.</summary>
        private static BiomeDef GetBiomeFromTileObject(object tileObj)
        {
            if (tileObj == null) return null;
            var t = tileObj.GetType();
            foreach (var memberName in new[] { "biome", "Biome", "PrimaryBiome" })
            {
                var prop = t.GetProperty(memberName, BindingFlags.Public | BindingFlags.Instance);
                if (prop != null && typeof(BiomeDef).IsAssignableFrom(prop.PropertyType))
                {
                    var v = prop.GetValue(tileObj);
                    if (v is BiomeDef b) return b;
                }
                var field = t.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null && typeof(BiomeDef).IsAssignableFrom(field.FieldType))
                {
                    var v = field.GetValue(tileObj);
                    if (v is BiomeDef b) return b;
                }
            }
            foreach (var method in t.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (method.GetParameters().Length != 0 || !typeof(BiomeDef).IsAssignableFrom(method.ReturnType)) continue;
                if (method.Name == "get_biome" || method.Name == "get_Biome" || method.Name == "get_PrimaryBiome")
                {
                    var v = method.Invoke(tileObj, null);
                    if (v is BiomeDef b) return b;
                }
            }
            return null;
        }

        /// <summary>Biome defName at this tile, or null.</summary>
        public static string GetBiomeName(int tile)
        {
            return GetBiome(tile)?.defName;
        }

        /// <summary>Biome label for display at this tile, or fallback. Uses defName when def exists but label is missing; tries reflection defName when GetBiome returns null.</summary>
        public static string GetBiomeLabel(int tile, string fallback = "?")
        {
            var b = GetBiome(tile);
            if (b != null) return b.label ?? b.defName ?? fallback;
            string name = GetBiomeNameFromReflection(tile);
            if (!string.IsNullOrEmpty(name))
            {
                var def = DefDatabase<BiomeDef>.GetNamedSilentFail(name);
                return def?.label ?? def?.defName ?? name;
            }
            return fallback;
        }

        /// <summary>Terrain for the given fertility affordance in this biome (from terrainsByFertility).</summary>
        public static TerrainDef GetTerrainForFertility(BiomeDef biome, TerrainAffordanceDef affordance)
        {
            if (biome?.terrainsByFertility == null) return null;
            foreach (var t in biome.terrainsByFertility)
            {
                if (t.terrain?.affordances != null && t.terrain.affordances.Contains(affordance))
                    return t.terrain;
            }
            return TerrainDefOf.Soil;
        }

        /// <summary>Fertility-related info: terrain at best fertility in this biome (for display/tooltips).</summary>
        public static TerrainDef GetBestFertilityTerrain(BiomeDef biome)
        {
            if (biome?.terrainsByFertility == null || biome.terrainsByFertility.Count == 0)
                return TerrainDefOf.Soil;
            return biome.terrainsByFertility[biome.terrainsByFertility.Count - 1].terrain;
        }

        /// <summary>What can grow in this biome (wild plants with commonality &gt; 0).</summary>
        public static List<ThingDef> GetWhatCanGrow(BiomeDef biome)
        {
            return biome?.AllWildPlants ?? new List<ThingDef>();
        }

        /// <summary>Wild animals that can appear in this biome (commonality &gt; 0).</summary>
        public static IEnumerable<PawnKindDef> GetWildAnimals(BiomeDef biome)
        {
            return biome?.AllWildAnimals ?? Enumerable.Empty<PawnKindDef>();
        }

        /// <summary>Rock types (stones) that can appear in this biome. Includes extraRockTypes and forceRockTypes.</summary>
        public static IEnumerable<ThingDef> GetStones(BiomeDef biome)
        {
            if (biome == null) yield break;
            if (biome.forceRockTypes != null)
            {
                foreach (var r in biome.forceRockTypes)
                    if (r != null) yield return r;
            }
            if (biome.extraRockTypes != null)
            {
                foreach (var r in biome.extraRockTypes)
                    if (r != null) yield return r;
            }
        }

        /// <summary>Natural rock types on this world tile (VOE: Find.World.NaturalRockTypesIn). Fallback: biome force/extra rock types.</summary>
        public static IEnumerable<ThingDef> GetNaturalRockTypesIn(int tile)
        {
            if (Find.World == null || tile < 0) yield break;
            var fromWorld = TryGetNaturalRockTypesFromWorld(tile);
            if (fromWorld != null)
            {
                foreach (var rock in fromWorld)
                    yield return rock;
                yield break;
            }
            var biome = GetBiome(tile);
            foreach (var rock in GetStones(biome))
                yield return rock;
        }

        private static List<ThingDef> TryGetNaturalRockTypesFromWorld(int tile)
        {
            try
            {
                var worldType = Find.World.GetType();
                var method = worldType.GetMethod("NaturalRockTypesIn", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(int) }, null);
                if (method != null && method.ReturnType != typeof(void))
                {
                    var result = method.Invoke(Find.World, new object[] { tile });
                    if (result is System.Collections.IEnumerable enumerable)
                    {
                        var list = new List<ThingDef>();
                        foreach (object o in enumerable)
                            if (o is ThingDef rock && rock != null) list.Add(rock);
                        return list;
                    }
                }
            }
            catch { /* fall through */ }
            return null;
        }

        /// <summary>Mining products for this tile: manufactured stone blocks (from natural rocks' mineableThing.butcherProducts) plus surface ores from <see cref="MiningScatterDiscovery"/> (scatter scan + vanilla union).</summary>
        public static List<ThingDef> GetMiningProductsForTile(int tile)
        {
            var list = new List<ThingDef>();
            var seen = new HashSet<ThingDef>();
            foreach (ThingDef rock in GetNaturalRockTypesIn(tile))
            {
                ThingDef chunkProduct = rock?.building?.mineableThing;
                if (chunkProduct == null) continue;
                ThingDef block = GetManufacturedStoneBlockFromChunk(chunkProduct);
                if (block != null && !seen.Contains(block)) { seen.Add(block); list.Add(block); }
                else if (!seen.Contains(chunkProduct)) { seen.Add(chunkProduct); list.Add(chunkProduct); }
            }
            int stoneCount = list.Count;
            foreach (ThingDef ore in MiningScatterDiscovery.GetEffectiveScatterOresOrdered())
                if (ore != null && !seen.Contains(ore)) { seen.Add(ore); list.Add(ore); }
            if (stoneCount == 0)
            {
                MiningScatterDiscovery.LogStaticStoneBlockFallbackOnce();
                foreach (string blockName in new[] { "BlocksGranite", "BlocksMarble", "BlocksSandstone", "BlocksLimestone", "BlocksSlate" })
                {
                    var block = DefDatabase<ThingDef>.GetNamedSilentFail(blockName);
                    if (block != null && !seen.Contains(block)) { seen.Add(block); list.Add(block); }
                }
            }
            list.Sort((a, b) => string.CompareOrdinal(a?.defName ?? "", b?.defName ?? ""));
            return list;
        }

        /// <summary>Manufactured stone block ThingDef from a chunk/mineable product. Uses butcherProducts if present (e.g. chunk yields blocks); else null.</summary>
        public static ThingDef GetManufacturedStoneBlockFromChunk(ThingDef chunkOrMineableProduct)
        {
            if (chunkOrMineableProduct == null) return null;
            var first = chunkOrMineableProduct.butcherProducts?.FirstOrDefault();
            return first?.thingDef;
        }

        /// <summary>True if this tile is mountainous or impassable (hilliness).</summary>
        public static bool IsMountainous(int tile)
        {
            var h = GetHilliness(tile);
            return h == Hilliness.Mountainous || h == Hilliness.Impassable;
        }

        /// <summary>Hilliness at this tile. Same source as VOE: Find.WorldGrid[tile].hilliness. Tries direct read then fallbacks.</summary>
        public static Hilliness GetHilliness(int tile)
        {
            if (Find.WorldGrid == null || tile < 0 || tile >= Find.WorldGrid.TilesCount)
                return Hilliness.Flat;
            try
            {
                // Mirror VOE: Find.WorldGrid[tile].hilliness (grid indexer returns tile, tile has .hilliness)
                object grid = Find.WorldGrid;
                var indexer = grid.GetType().GetProperty("Item", new[] { typeof(int) })?.GetGetMethod();
                if (indexer != null)
                {
                    object gridTile = indexer.Invoke(grid, new object[] { tile });
                    if (gridTile != null)
                    {
                        Hilliness? h = GetHillinessFromTileObject(gridTile);
                        if (h.HasValue) return h.Value;
                    }
                }
            }
            catch { /* fall through */ }
            try
            {
                object gridTile = Find.WorldGrid[tile];
                if (gridTile != null)
                {
                    Hilliness? h = GetHillinessFromTileObject(gridTile);
                    if (h.HasValue) return h.Value;
                }
            }
            catch { /* fall through */ }
            if (HillinessResolver != null) return HillinessResolver(tile);
            EnsureTileAccessors();
            return _hillinessGetter != null ? _hillinessGetter(tile) : Hilliness.Flat;
        }

        /// <summary>Read Hilliness from a boxed Tile. VOE uses Find.WorldGrid[tile].hilliness (lowercase). Tries by name then any member of type Hilliness.</summary>
        private static Hilliness? GetHillinessFromTileObject(object tileObj)
        {
            if (tileObj == null) return null;
            var t = tileObj.GetType();

            // 1) By name (VOE uses .hilliness lowercase)
            foreach (var name in new[] { "hilliness", "Hilliness" })
            {
                var prop = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (prop != null)
                {
                    var v = SafeGetValue(() => prop.GetValue(tileObj));
                    if (TryParseHilliness(v, out Hilliness h)) return h;
                }
                var field = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (field != null)
                {
                    var v = SafeGetValue(() => field.GetValue(tileObj));
                    if (TryParseHilliness(v, out Hilliness h)) return h;
                }
            }

            // 2) Any public instance property of type Hilliness (e.g. different framework/casing)
            foreach (var prop in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.PropertyType != typeof(Hilliness) && !prop.PropertyType.IsEnum) continue;
                var v = SafeGetValue(() => prop.GetValue(tileObj));
                if (TryParseHilliness(v, out Hilliness h)) return h;
            }

            // 3) Any instance field of type Hilliness (public or private)
            foreach (var field in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (field.FieldType != typeof(Hilliness) && !field.FieldType.IsEnum) continue;
                var v = SafeGetValue(() => field.GetValue(tileObj));
                if (TryParseHilliness(v, out Hilliness h)) return h;
            }

            return null;
        }

        private static object SafeGetValue(Func<object> getter)
        {
            try { return getter(); }
            catch { return null; }
        }

        private static bool TryParseHilliness(object v, out Hilliness h)
        {
            h = Hilliness.Flat;
            if (v == null) return false;
            if (v is Hilliness hv) { h = hv; return true; }
            if (v is int i && Enum.IsDefined(typeof(Hilliness), i)) { h = (Hilliness)i; return true; }
            try
            {
                if (Enum.IsDefined(typeof(Hilliness), v)) { h = (Hilliness)v; return true; }
            }
            catch { }
            return false;
        }
    }
}
