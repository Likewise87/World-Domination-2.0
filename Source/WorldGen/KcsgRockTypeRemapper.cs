using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// During WD KCSG settlement generation:
    /// - remaps hardcoded layout stone to the map's dominant rock
    /// - remaps Soil/SoilRich (and crop cells) to the map's dominant fertile terrain
    /// - remaps Plant_Corn / ranked trees / MineableSteel|Silver from biome + XML pools (per layout)
    /// - paints RoughHewn halos under layout rock
    /// - spawns wild livestock around PenMarker symbols from biome XML
    /// - wipes non-layout filth/chunks/plants/items on layout floor and constructed-roof cells
    ///   (early on SetTerrain/SetRoof, per-structure catch-up, plus a final settlement pass for re-drops)
    /// </summary>
    public static class KcsgRockTypeRemapper
    {
        private static string SettlementLabel(Map map) => map?.Parent?.LabelCap ?? map?.Parent?.def?.defName ?? "unknown-settlement";
        public static readonly string[] RockKinds =
        {
            "Marble",
            "Granite",
            "Slate",
            "Sandstone",
            "Limestone"
        };

        private static readonly string[] LayoutFloorExclusions =
        {
            "WoodPlankFloor",
            "StrawMatting",
            "PackedDirt",
            "MetalTile",
            "PavedTile",
            "Concrete",
            "SterileTile",
            "Carpet",
            "Bridge",
            "BrokenAsphalt",
            "Asphalt"
        };

        private const string CropPlaceholder = "Plant_Corn";
        private const float MinCropGrowth = 0.60f;
        private const float MaxCropGrowth = 1.00f;
        private const string TreeRank1 = "Plant_TreeOak";
        private const string TreeRank2 = "Plant_TreeBirch";
        private const string TreeRank3 = "Plant_TreeMaple";
        private const string OreCheapPlaceholder = "MineableSteel";
        private const string OreExpensivePlaceholder = "MineableSilver";
        private const string FarmLayoutTag = "TSA_Tribal_Farm";
        private const int MaxCropFallbackTries = 2;

        private enum LayoutSymbolKind { None, OreCheap, OreExpensive, CropPlaceholder }

        private struct PendingSymbolVerify
        {
            public IntVec3 cell;
            public LayoutSymbolKind kind;
            public ThingDef expectedDef;
        }

        [System.ThreadStatic] private static Map sessionMap;
        [System.ThreadStatic] private static IntVec3 sessionSampleCell;
        [System.ThreadStatic] private static ThingDef dominantRock;
        [System.ThreadStatic] private static TerrainDef dominantFertile;
        [System.ThreadStatic] private static ThingDef chosenCrop;
        [System.ThreadStatic] private static ThingDef chosenTree1;
        [System.ThreadStatic] private static ThingDef chosenTree2;
        [System.ThreadStatic] private static ThingDef chosenTree3;
        [System.ThreadStatic] private static ThingDef chosenCheapOre;
        [System.ThreadStatic] private static ThingDef chosenExpensiveOre;
        [System.ThreadStatic] private static BiomeDef sessionBiome;
        [System.ThreadStatic] private static bool sessionActive;
        [System.ThreadStatic] private static bool suppressFarmTerrainRemap;
        [System.ThreadStatic] private static Stack<(object symbol, ThingDef thing, ThingDef stuff)> symbolRestoreStack;
        [System.ThreadStatic] private static PendingSymbolVerify? pendingSymbolVerify;
        [System.ThreadStatic] private static HashSet<ThingDef> layoutSpawnedCheap;
        [System.ThreadStatic] private static HashSet<ThingDef> layoutSpawnedExpensive;
        [System.ThreadStatic] private static HashSet<ThingDef> sessionSpawnedCheap;
        [System.ThreadStatic] private static HashSet<ThingDef> sessionSpawnedExpensive;
        [System.ThreadStatic] private static List<WdWeightedThingOption> sessionCropProducts;
        [System.ThreadStatic] private static List<CellRect> sessionCropProductLayoutRects;
        [System.ThreadStatic] private static List<CellRect> sessionOreProductLayoutRects;
        [System.ThreadStatic] private static string currentLayoutName;
        [System.ThreadStatic] private static bool currentLayoutIsFarmTagged;
        [System.ThreadStatic] private static int layoutCropAttempted;
        [System.ThreadStatic] private static int layoutCropPlaced;
        [System.ThreadStatic] private static int layoutOreAttempted;
        [System.ThreadStatic] private static int layoutOrePlaced;
        [System.ThreadStatic] private static List<IntVec3> layoutCropFallbackCells;
        [System.ThreadStatic] private static HashSet<ThingDef> layoutFailedCrops;
        [System.ThreadStatic] private static HashSet<Thing> layoutProtectedDebris;

        private static FieldInfo thingDefField;
        private static FieldInfo stuffDefField;

        public static bool SessionActive => sessionActive;

        public static bool RockRemapEnabled =>
            WorldDominationMod.settings?.kcsgRemapRockToMapStone ?? WorldDominationSettings.DefKcsgRemapRockToMapStone;

        public static bool FarmSoilRemapEnabled =>
            WorldDominationMod.settings?.kcsgRemapFarmSoil ?? WorldDominationSettings.DefKcsgRemapFarmSoil;

        public static bool FertileUnderCropsEnabled =>
            WorldDominationMod.settings?.kcsgFertileUnderCrops ?? WorldDominationSettings.DefKcsgFertileUnderCrops;

        public static bool PenLivestockEnabled =>
            WorldDominationMod.settings?.kcsgSpawnPenLivestock ?? WorldDominationSettings.DefKcsgSpawnPenLivestock;

        public static bool TreeRemapEnabled =>
            WorldDominationMod.settings?.kcsgRemapTreesToBiome ?? WorldDominationSettings.DefKcsgRemapTreesToBiome;

        public static bool Active => sessionActive && dominantRock != null && RockRemapEnabled;

        public static ThingDef DominantRock => dominantRock;
        public static TerrainDef DominantFertile => dominantFertile;
        public static ThingDef ChosenCheapOre => chosenCheapOre;
        public static ThingDef ChosenExpensiveOre => chosenExpensiveOre;
        public static ThingDef ChosenCrop => chosenCrop;

        public static IReadOnlyCollection<ThingDef> GetLayoutSpawnedCheap() =>
            layoutSpawnedCheap ?? (IReadOnlyCollection<ThingDef>)System.Array.Empty<ThingDef>();

        public static IReadOnlyCollection<ThingDef> GetLayoutSpawnedExpensive() =>
            layoutSpawnedExpensive ?? (IReadOnlyCollection<ThingDef>)System.Array.Empty<ThingDef>();

        public static IReadOnlyCollection<ThingDef> GetSessionSpawnedCheap() =>
            sessionSpawnedCheap ?? (IReadOnlyCollection<ThingDef>)System.Array.Empty<ThingDef>();

        public static IReadOnlyCollection<ThingDef> GetSessionSpawnedExpensive() =>
            sessionSpawnedExpensive ?? (IReadOnlyCollection<ThingDef>)System.Array.Empty<ThingDef>();

        public static List<WdWeightedThingOption> GetSessionCropProducts() =>
            sessionCropProducts != null ? new List<WdWeightedThingOption>(sessionCropProducts) : new List<WdWeightedThingOption>();

        /// <summary>Layout rects that successfully placed crops this session (for shelf product preference).</summary>
        public static IReadOnlyList<CellRect> GetSessionCropProductLayoutRects() =>
            sessionCropProductLayoutRects ?? (IReadOnlyList<CellRect>)System.Array.Empty<CellRect>();

        /// <summary>Layout rects that successfully placed ores this session (for shelf product preference).</summary>
        public static IReadOnlyList<CellRect> GetSessionOreProductLayoutRects() =>
            sessionOreProductLayoutRects ?? (IReadOnlyList<CellRect>)System.Array.Empty<CellRect>();

        /// <summary>Mined item stack def produced when the mineable building is mined.</summary>
        public static ThingDef MinedProductForMineable(ThingDef mineableDef) =>
            mineableDef?.building?.mineableThing;

        /// <summary>Harvest product def for a sowable crop plant.</summary>
        public static ThingDef HarvestProductForPlant(ThingDef plantDef) =>
            plantDef?.plant?.harvestedThingDef;

        public static void Begin(Map map)
        {
            sessionMap = map;
            sessionSampleCell = FindBestCropSampleCell(map);
            dominantRock = FindDominantNaturalRock(map);
            dominantFertile = FindDominantFertileTerrain(map) ?? TerrainDefOf.Soil;
            sessionBiome = map?.Biome;
            sessionActive = true;
            suppressFarmTerrainRemap = false;

            sessionSpawnedCheap = new HashSet<ThingDef>();
            sessionSpawnedExpensive = new HashSet<ThingDef>();
            sessionCropProducts = new List<WdWeightedThingOption>();
            sessionCropProductLayoutRects = new List<CellRect>();
            sessionOreProductLayoutRects = new List<CellRect>();
            layoutFailedCrops = new HashSet<ThingDef>();
            // Session-wide: layout chunk/item symbols must survive the final settlement wipe.
            layoutProtectedDebris = new HashSet<Thing>();

            chosenCrop = ResolveSessionCrop(map);
            ResolveSessionTrees(map);
            ResolveSessionOres(map);

            WDVerbose.RemapNoTick("KCSG remap BEGIN settlement=" + SettlementLabel(map)
                + " biome=" + (map?.Biome?.defName ?? "?")
                + " rock=" + (dominantRock != null ? dominantRock.defName : "none")
                + " fertile=" + (dominantFertile != null ? dominantFertile.defName : "none")
                + " crop=" + (chosenCrop != null ? chosenCrop.defName : "none")
                + " trees=" + (chosenTree1 != null ? chosenTree1.defName : "-")
                + "/" + (chosenTree2 != null ? chosenTree2.defName : "-")
                + "/" + (chosenTree3 != null ? chosenTree3.defName : "-")
                + " ores=" + (chosenCheapOre != null ? chosenCheapOre.defName : "-")
                + "/" + (chosenExpensiveOre != null ? chosenExpensiveOre.defName : "-")
                + " sample=" + (sessionSampleCell.IsValid ? sessionSampleCell.ToString() : "invalid")
                + " products="
                + FormatOreProduct(chosenCheapOre) + "/" + FormatOreProduct(chosenExpensiveOre));
        }

        /// <summary>Call at the start of each KCSG structure layout.</summary>
        public static void BeginLayout(string layoutName, IEnumerable<string> tags)
        {
            currentLayoutName = layoutName ?? "?";
            currentLayoutIsFarmTagged = tags != null && tags.Contains(FarmLayoutTag);
            layoutCropAttempted = 0;
            layoutCropPlaced = 0;
            layoutOreAttempted = 0;
            layoutOrePlaced = 0;
            pendingSymbolVerify = null;
            layoutCropFallbackCells = layoutCropFallbackCells ?? new List<IntVec3>();
            layoutCropFallbackCells.Clear();
            // Do not clear layoutProtectedDebris here — protection is session-scoped so a final
            // settlement wipe can keep decorative chunks from earlier structures.
        }

        /// <summary>Call at the end of each KCSG structure layout (before shelf fill).</summary>
        public static void EndLayout(Map map, CellRect rect = default)
        {
            if (layoutCropAttempted > 0 || currentLayoutIsFarmTagged)
            {
                WDVerbose.RemapNoTick(
                    $"KCSG remap layout settlement={SettlementLabel(map)} layout={currentLayoutName} crop={chosenCrop?.defName ?? "none"} "
                    + $"placed={layoutCropPlaced}/{layoutCropAttempted} "
                    + $"failed={layoutCropAttempted - layoutCropPlaced}");
            }

            if (currentLayoutIsFarmTagged && layoutCropPlaced == 0 && layoutCropAttempted > 0 && map != null)
                TryCropFallback(map);

            if (layoutOreAttempted > 0)
            {
                WDVerbose.RemapNoTick(
                    $"KCSG remap layout settlement={SettlementLabel(map)} layout={currentLayoutName} ore rolled cheap={chosenCheapOre?.defName ?? "-"} "
                    + $"expensive={chosenExpensiveOre?.defName ?? "-"} "
                    + $"attempted={layoutOreAttempted} placed={layoutOrePlaced} "
                    + $"spawned cheap=[{FormatDefSet(layoutSpawnedCheap)}] "
                    + $"expensive=[{FormatDefSet(layoutSpawnedExpensive)}]");
            }

            // Catch-up: wipe pre-layout debris on every floor/roof cell in this structure.
            if (sessionActive && map != null && rect.Area > 0)
                WipeDebrisOnLayoutFloorOrRoof(map, rect);

            if (rect.Area <= 0) return;

            if (layoutCropPlaced > 0)
            {
                sessionCropProductLayoutRects = sessionCropProductLayoutRects ?? new List<CellRect>();
                sessionCropProductLayoutRects.Add(rect);
            }

            if (layoutOrePlaced > 0)
            {
                sessionOreProductLayoutRects = sessionOreProductLayoutRects ?? new List<CellRect>();
                sessionOreProductLayoutRects.Add(rect);
            }
        }

        /// <summary>Stash expected spawn before KCSG places the symbol (read in postfix verification).</summary>
        public static void PrepareSymbolVerification(ThingDef originalDef, IntVec3 cell)
        {
            if (!sessionActive || originalDef == null) return;

            if (originalDef.defName == CropPlaceholder && chosenCrop != null)
            {
                layoutCropAttempted++;
                pendingSymbolVerify = new PendingSymbolVerify
                {
                    cell = cell,
                    kind = LayoutSymbolKind.CropPlaceholder,
                    expectedDef = chosenCrop
                };
                return;
            }

            if (originalDef.defName == OreCheapPlaceholder && chosenCheapOre != null)
            {
                layoutOreAttempted++;
                pendingSymbolVerify = new PendingSymbolVerify
                {
                    cell = cell,
                    kind = LayoutSymbolKind.OreCheap,
                    expectedDef = chosenCheapOre
                };
                return;
            }

            if (originalDef.defName == OreExpensivePlaceholder && chosenExpensiveOre != null)
            {
                layoutOreAttempted++;
                pendingSymbolVerify = new PendingSymbolVerify
                {
                    cell = cell,
                    kind = LayoutSymbolKind.OreExpensive,
                    expectedDef = chosenExpensiveOre
                };
            }
        }

        /// <summary>Verify what KCSG actually spawned at the cell; update layout/session pools.</summary>
        public static void CompleteSymbolVerification(Map map, IntVec3 cell)
        {
            if (!sessionActive || map == null || !pendingSymbolVerify.HasValue) return;
            PendingSymbolVerify pending = pendingSymbolVerify.Value;
            pendingSymbolVerify = null;
            if (pending.cell != cell) return;

            switch (pending.kind)
            {
                case LayoutSymbolKind.CropPlaceholder:
                    ThingDef plantDef = GetPlantDefAt(map, cell);
                    if (plantDef == pending.expectedDef)
                    {
                        layoutCropPlaced++;
                        AddVerifiedCropProduct(pending.expectedDef);
                    }
                    else
                    {
                        layoutCropFallbackCells.Add(cell);
                        if (chosenCrop != null)
                            layoutFailedCrops.Add(chosenCrop);
                    }
                    break;

                case LayoutSymbolKind.OreCheap:
                case LayoutSymbolKind.OreExpensive:
                    ThingDef mineable = cell.GetFirstMineable(map)?.def;
                    if (mineable != null && mineable == pending.expectedDef)
                    {
                        layoutOrePlaced++;
                        RecordSpawnedMineable(mineable);
                    }
                    break;
            }
        }

        /// <summary>Call at the start of each KCSG structure layout so farms are not one crop for the whole map.</summary>
        public static void RerollCropForLayout()
        {
            if (!sessionActive) return;
            chosenCrop = ResolveSessionCrop(sessionMap);
            // Included in the per-layout summary at EndLayout; no separate reroll line.
        }

        /// <summary>Call at the start of each KCSG structure layout so mines are not one ore pair for the whole map.</summary>
        public static void RerollOresForLayout()
        {
            if (!sessionActive) return;
            ClearLayoutOreSpawned();
            ResolveSessionOres(null);
            // Included in the per-layout summary at EndLayout; no separate reroll line.
        }

        /// <summary>Farm placeholder plants spawn at 60–100% growth so fields are not bare seedlings.</summary>
        public static void ApplyRandomCropGrowth(Map map, IntVec3 cell)
        {
            if (!sessionActive || map == null || !cell.InBounds(map)) return;
            Plant plant = null;
            List<Thing> things = cell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i] is Plant p && p.def == chosenCrop)
                {
                    plant = p;
                    break;
                }
            }
            if (plant == null) return;
            plant.Growth = Rand.Range(MinCropGrowth, MaxCropGrowth);
        }

        public static void End()
        {
            WDVerbose.RemapNoTick("KCSG remap END");
            sessionActive = false;
            sessionMap = null;
            sessionSampleCell = IntVec3.Invalid;
            dominantRock = null;
            dominantFertile = null;
            chosenCrop = null;
            chosenTree1 = chosenTree2 = chosenTree3 = null;
            chosenCheapOre = chosenExpensiveOre = null;
            sessionBiome = null;
            suppressFarmTerrainRemap = false;
            pendingSymbolVerify = null;
            layoutSpawnedCheap = null;
            layoutSpawnedExpensive = null;
            sessionSpawnedCheap = null;
            sessionSpawnedExpensive = null;
            sessionCropProducts = null;
            sessionCropProductLayoutRects = null;
            sessionOreProductLayoutRects = null;
            currentLayoutName = null;
            currentLayoutIsFarmTagged = false;
            layoutCropAttempted = layoutCropPlaced = 0;
            layoutOreAttempted = layoutOrePlaced = 0;
            layoutCropFallbackCells = null;
            layoutFailedCrops = null;
            layoutProtectedDebris = null;
            while (symbolRestoreStack != null && symbolRestoreStack.Count > 0)
                RestoreSymbol(symbolRestoreStack.Pop());
            symbolRestoreStack = null;
        }

        public static ThingDef FindDominantNaturalRock(Map map)
        {
            if (map == null) return null;

            ThingDef best = null;
            int bestN = 0;
            foreach (string kind in RockKinds)
            {
                ThingDef rock = DefDatabase<ThingDef>.GetNamedSilentFail(kind);
                if (rock?.building == null || !rock.building.isNaturalRock) continue;
                int n = map.listerThings.ThingsOfDef(rock).Count;
                if (n > bestN)
                {
                    bestN = n;
                    best = rock;
                }
            }

            if (best != null) return best;

            foreach (ThingDef rock in Find.World.NaturalRockTypesIn(map.Tile))
            {
                if (rock?.building != null && rock.building.isNaturalRock && TryGetRockKind(rock, out _))
                    return rock;
            }

            return null;
        }

        public static TerrainDef FindDominantFertileTerrain(Map map)
        {
            if (map == null) return null;

            var counts = new Dictionary<TerrainDef, int>();
            TerrainDef best = null;
            int bestN = 0;

            foreach (IntVec3 c in map.AllCells)
            {
                if (!c.InBounds(map) || !c.Walkable(map)) continue;
                TerrainDef terrain = c.GetTerrain(map);
                if (!IsFertileNaturalGroundCandidate(terrain)) continue;

                counts.TryGetValue(terrain, out int n);
                n++;
                counts[terrain] = n;
                if (n > bestN)
                {
                    bestN = n;
                    best = terrain;
                }
            }

            return best;
        }

        /// <summary>
        /// Farm / crop cells need real growing ground. RimWorld UI % is fertility*100
        /// (Soil ≈ 100%, Sand ≈ 10%). Anything below this is rejected so deserts fall
        /// back to TerrainDefOf.Soil via FindDominantFertileTerrain callers.
        /// </summary>
        public const float MinFertilityForFarmRemap = 0.70f;

        public static bool IsFertileNaturalGroundCandidate(TerrainDef terrain)
        {
            if (terrain == null || terrain.fertility < MinFertilityForFarmRemap) return false;
            if (terrain.IsWater) return false;
            if (terrain.affordances != null && terrain.affordances.Contains(TerrainAffordanceDefOf.Bridgeable))
                return false;
            if (IsExcludedLayoutFloor(terrain)) return false;
            return true;
        }

        /// <summary>Layout terrainGrid floors (wood, stone tile, packed dirt, asphalt, etc.) — not natural soil/grass.</summary>
        public static bool IsLayoutPlacedFloor(TerrainDef terrain)
        {
            if (terrain == null) return false;
            if (IsExcludedLayoutFloor(terrain)) return true;
            // Any constructible floor (has a cost list) counts — catches mod/vanilla floors not in the exclusion list.
            if (terrain.costList != null && terrain.costList.Count > 0) return true;
            return false;
        }

        /// <summary>Constructed layout roofs — not natural rock/mountain roof.</summary>
        public static bool IsLayoutPlacedRoof(RoofDef roof) => roof != null && !roof.isNatural;

        /// <summary>True when this cell currently has layout floor and/or constructed roof.</summary>
        public static bool CellHasLayoutFloorOrRoof(Map map, IntVec3 cell)
        {
            if (map == null || !cell.InBounds(map)) return false;
            if (IsLayoutPlacedFloor(cell.GetTerrain(map))) return true;
            return IsLayoutPlacedRoof(map.roofGrid.RoofAt(cell));
        }

        private static bool IsExcludedLayoutFloor(TerrainDef terrain)
        {
            string name = terrain.defName;
            for (int i = 0; i < LayoutFloorExclusions.Length; i++)
            {
                if (name == LayoutFloorExclusions[i] || name.StartsWith(LayoutFloorExclusions[i]))
                    return true;
            }
            if (name.StartsWith("Flagstone") || name.StartsWith("Tile") || name.Contains("Carpet"))
                return true;
            if (TryGetRockKind(terrain, out _)) return true;
            return false;
        }

        /// <summary>
        /// After KCSG spawns a layout symbol, keep those Things when wiping indoor debris.
        /// Item symbols (esp. Shell_* / mortar shells under CE) register every Item on the cell —
        /// exact def match alone can miss CE AmmoThing / def-identity quirks.
        /// </summary>
        public static void ProtectLayoutDebrisAfterSymbolSpawn(object symbol, Map map, IntVec3 cell)
        {
            if (!sessionActive || map == null || !cell.InBounds(map)) return;
            ThingDef def = GetSymbolThingDef(symbol);
            if (def == null) return;

            bool protectAllItemsOnCell = def.category == ThingCategory.Item
                || IsLayoutMortarShellDef(def);

            List<Thing> things = cell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                Thing t = things[i];
                if (t == null || t.Destroyed || t is Pawn) continue;
                if (protectAllItemsOnCell)
                {
                    if (t.def != null && t.def.category == ThingCategory.Item)
                        RegisterLayoutSpawnedDebris(t);
                    continue;
                }
                // Match spawned def, or any chunk when the symbol was a chunk (rock remap may change kind).
                if (t.def == def || (IsChunkThingDef(def) && IsChunkThingDef(t.def)))
                    RegisterLayoutSpawnedDebris(t);
            }
        }

        private static bool IsLayoutMortarShellDef(ThingDef def)
        {
            if (def?.defName != null && def.defName.StartsWith("Shell_"))
                return true;
            ThingCategoryDef mortarCat = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("MortarShells");
            if (mortarCat != null && def.IsWithinCategory(mortarCat))
                return true;
            return false;
        }

        public static void RegisterLayoutSpawnedDebris(Thing thing)
        {
            if (thing == null || thing.Destroyed) return;
            if (layoutProtectedDebris == null)
                layoutProtectedDebris = new HashSet<Thing>();
            layoutProtectedDebris.Add(thing);
        }

        /// <summary>
        /// Remove pre-existing debris when a layout floor/roof is placed.
        /// Layout-spawned Things (registered via ProtectLayoutDebrisAfterSymbolSpawn) are kept.
        /// </summary>
        public static void WipeIndoorCellDebris(IntVec3 cell)
        {
            Map map = sessionMap;
            if (!sessionActive || map == null || !cell.InBounds(map)) return;
            WipeIndoorCellDebris(map, cell, chunksAndFilthOnly: false);
        }

        /// <summary>End-of-structure catch-up wipe for every cell with layout floor or constructed roof.</summary>
        public static void WipeDebrisOnLayoutFloorOrRoof(Map map, CellRect rect)
        {
            if (!sessionActive || map == null || rect.Area <= 0) return;
            foreach (IntVec3 cell in rect)
            {
                if (!CellHasLayoutFloorOrRoof(map, cell)) continue;
                WipeIndoorCellDebris(map, cell, chunksAndFilthOnly: false);
            }
        }

        /// <summary>
        /// Final settlement-wide pass after all KCSG structures/roads/scatter.
        /// Only removes filth + stone chunks so stockpile/shelf loot placed after structures survives.
        /// Layout-spawned chunks stay via session-scoped <see cref="layoutProtectedDebris"/>.
        /// </summary>
        public static void WipeDebrisOnAllTrackedIndoorCells(Map map)
        {
            if (!sessionActive || map == null) return;

            int wipedCells = 0;
            if (WdLayoutSpawnCellTracker.TryGetCells(map, out HashSet<IntVec3> cells))
            {
                foreach (IntVec3 cell in cells)
                {
                    if (!CellHasLayoutFloorOrRoof(map, cell)) continue;
                    WipeIndoorCellDebris(map, cell, chunksAndFilthOnly: true);
                    wipedCells++;
                }
            }
            else if (WdSettlementMapUnfog.TryResolveSettlementRect(map, out CellRect rect) && rect.Area > 0)
            {
                foreach (IntVec3 cell in rect)
                {
                    if (!cell.InBounds(map) || !CellHasLayoutFloorOrRoof(map, cell)) continue;
                    WipeIndoorCellDebris(map, cell, chunksAndFilthOnly: true);
                    wipedCells++;
                }
            }

            if (wipedCells > 0)
                WDVerbose.RemapNoTick($"KCSG indoor debris final wipe settlement={SettlementLabel(map)} cells={wipedCells}");
        }

        private static void WipeIndoorCellDebris(Map map, IntVec3 cell, bool chunksAndFilthOnly)
        {
            List<Thing> things = cell.GetThingList(map);
            for (int i = things.Count - 1; i >= 0; i--)
            {
                Thing t = things[i];
                if (t == null || t.Destroyed) continue;
                if (t is Pawn) continue;
                if (layoutProtectedDebris != null && layoutProtectedDebris.Contains(t)) continue;
                // Layout rock/ore symbols are not "debris".
                if (t is Mineable) continue;
                if (t.def.building != null && t.def.building.isNaturalRock) continue;
                // Pre-map / non-layout debris we always want gone on floor/roof cells.
                bool filth = t.def.category == ThingCategory.Filth;
                bool chunk = IsChunkThingDef(t.def);
                if (filth || chunk)
                {
                    t.Destroy(DestroyMode.Vanish);
                    continue;
                }
                if (chunksAndFilthOnly) continue;
                bool plant = t.def.category == ThingCategory.Plant;
                bool looseItem = t.def.category == ThingCategory.Item;
                if (plant || looseItem)
                    t.Destroy(DestroyMode.Vanish);
            }
        }

        private static bool IsChunkThingDef(ThingDef def)
        {
            if (def == null) return false;
            if (def.thingCategories != null && def.thingCategories.Contains(ThingCategoryDefOf.Chunks))
                return true;
            // Fallback for odd/modded chunk defs that skip the Chunks category.
            return def.defName != null && def.defName.StartsWith("Chunk");
        }

        public static bool TryGetRockKind(ThingDef def, out string kind)
        {
            kind = null;
            if (def == null) return false;
            string name = def.defName;
            foreach (string k in RockKinds)
            {
                if (name == k || name == "Chunk" + k || name == "Blocks" + k || name == "Wall_Blocks" + k)
                {
                    kind = k;
                    return true;
                }
            }
            return false;
        }

        public static bool TryGetRockKind(TerrainDef def, out string kind)
        {
            kind = null;
            if (def == null) return false;
            string name = def.defName;
            foreach (string k in RockKinds)
            {
                if (name == k + "_RoughHewn" || name == k + "_Rough" ||
                    name == "Flagstone" + k || name == "Tile" + k)
                {
                    kind = k;
                    return true;
                }
            }
            return false;
        }

        public static ThingDef RemapThing(ThingDef def)
        {
            if (def == null) return def;

            ThingDef plantOrOre = RemapPlantOrOre(def);
            if (plantOrOre != def) return plantOrOre;

            if (!Active) return def;
            if (!TryGetRockKind(def, out string kind)) return def;
            if (kind == dominantRock.defName) return def;

            string targetName;
            if (def.defName == kind)
                targetName = dominantRock.defName;
            else if (def.defName.StartsWith("Chunk"))
                targetName = "Chunk" + dominantRock.defName;
            else if (def.defName.StartsWith("Wall_Blocks"))
                targetName = "Wall_Blocks" + dominantRock.defName;
            else if (def.defName.StartsWith("Blocks"))
                targetName = "Blocks" + dominantRock.defName;
            else
                return def;

            return DefDatabase<ThingDef>.GetNamedSilentFail(targetName) ?? def;
        }

        public static ThingDef RemapStuff(ThingDef stuff)
        {
            if (!Active || stuff == null) return stuff;
            if (!TryGetRockKind(stuff, out string kind)) return stuff;
            if (kind == dominantRock.defName) return stuff;
            if (!stuff.defName.StartsWith("Blocks")) return stuff;
            return DefDatabase<ThingDef>.GetNamedSilentFail("Blocks" + dominantRock.defName) ?? stuff;
        }

        public static TerrainDef RemapTerrain(TerrainDef terrain)
        {
            if (!Active || terrain == null) return terrain;
            if (!TryGetRockKind(terrain, out string kind)) return terrain;
            if (kind == dominantRock.defName) return terrain;

            string name = terrain.defName;
            string targetName;
            if (name.EndsWith("_RoughHewn"))
                targetName = dominantRock.defName + "_RoughHewn";
            else if (name.EndsWith("_Rough"))
                targetName = dominantRock.defName + "_Rough";
            else if (name.StartsWith("Flagstone"))
                targetName = "Flagstone" + dominantRock.defName;
            else if (name.StartsWith("Tile"))
                targetName = "Tile" + dominantRock.defName;
            else
                return terrain;

            return DefDatabase<TerrainDef>.GetNamedSilentFail(targetName) ?? terrain;
        }

        public static TerrainDef RemapFarmTerrain(TerrainDef terrain)
        {
            if (!sessionActive || !FarmSoilRemapEnabled || suppressFarmTerrainRemap || terrain == null) return terrain;
            if (terrain.defName != "Soil" && terrain.defName != "SoilRich") return terrain;
            return dominantFertile ?? TerrainDefOf.Soil;
        }

        public static bool IsTerrainBuildableForSettlement(TerrainDef terrain)
        {
            if (terrain?.affordances == null) return false;
            if (terrain.affordances.Contains(TerrainAffordanceDefOf.Bridgeable)) return false;
            return terrain.affordances.Contains(TerrainAffordanceDefOf.Medium);
        }

        public static TerrainDef FindMostCommonBuildableTerrain(Map map)
        {
            if (map == null) return TerrainDefOf.Soil;

            var counts = new Dictionary<TerrainDef, int>();
            TerrainDef best = null;
            int bestN = 0;
            foreach (IntVec3 c in map.AllCells)
            {
                if (!c.InBounds(map) || !c.Walkable(map)) continue;
                TerrainDef terrain = c.GetTerrain(map);
                if (!IsTerrainBuildableForSettlement(terrain)) continue;
                counts.TryGetValue(terrain, out int n);
                n++;
                counts[terrain] = n;
                if (n > bestN)
                {
                    bestN = n;
                    best = terrain;
                }
            }
            return best ?? TerrainDefOf.Soil;
        }

        /// <summary>
        /// After any structure layout: replace unbuildable underfoot terrain with the map's most common buildable soil.
        /// Does not replace constructed layout floors (wood, tile, packed dirt, etc.).
        /// </summary>
        public static void EnsureBuildableFloorsUnderLayout(Map map, CellRect rect)
        {
            if (map == null || rect.Area <= 0) return;

            TerrainDef floor = FindMostCommonBuildableTerrain(map);
            if (floor == null) return;

            int fixedCells = 0;
            foreach (IntVec3 c in rect)
            {
                if (!c.InBounds(map)) continue;
                TerrainDef current = c.GetTerrain(map);
                if (current == null) continue;
                if (IsLayoutPlacedFloor(current)) continue;
                if (IsTerrainBuildableForSettlement(current)) continue;

                suppressFarmTerrainRemap = true;
                try
                {
                    map.terrainGrid.SetTerrain(c, floor);
                    fixedCells++;
                }
                finally
                {
                    suppressFarmTerrainRemap = false;
                }
            }

            if (fixedCells > 0)
                WDVerbose.RemapNoTick($"KCSG buildable floor fill settlement={SettlementLabel(map)} cells={fixedCells} floor={floor.defName}");
        }

        public static bool IsLayoutCropPlant(ThingDef def)
        {
            if (def?.plant == null) return false;
            if (!def.plant.Sowable) return false;
            if (def.plant.IsTree) return false;
            return true;
        }

        public static bool IsPenMarker(ThingDef def) =>
            def != null && def.defName != null && def.defName.StartsWith("PenMarker");

        public static bool IsLayoutNaturalRockOrMineable(ThingDef def)
        {
            if (def?.building == null) return false;
            if (def.building.isNaturalRock) return true;
            if (def.building.mineableThing != null) return true;
            return false;
        }

        public static void EnsureFertileUnderCrop(Map map, IntVec3 cell)
        {
            if (!sessionActive || !FertileUnderCropsEnabled || map == null || !cell.InBounds(map)) return;
            TerrainDef target = dominantFertile ?? TerrainDefOf.Soil;
            if (target == null) return;

            TerrainDef current = cell.GetTerrain(map);
            if (current == target) return;

            suppressFarmTerrainRemap = true;
            try
            {
                map.terrainGrid.SetTerrain(cell, target);
            }
            finally
            {
                suppressFarmTerrainRemap = false;
            }
        }

        public static void ApplyRoughHewnHalo(Map map, IntVec3 center)
        {
            if (!sessionActive || !RockRemapEnabled || map == null || !center.InBounds(map)) return;
            if (dominantRock == null) return;

            TerrainDef hewn = DefDatabase<TerrainDef>.GetNamedSilentFail(dominantRock.defName + "_RoughHewn")
                ?? DefDatabase<TerrainDef>.GetNamedSilentFail(dominantRock.defName + "_Rough");
            if (hewn == null) return;

            PaintRoughHewnIfFree(map, center, hewn);
            for (int i = 0; i < 8; i++)
                PaintRoughHewnIfFree(map, center + GenAdj.AdjacentCellsAround[i], hewn);
        }

        private static void PaintRoughHewnIfFree(Map map, IntVec3 cell, TerrainDef hewn)
        {
            if (!cell.InBounds(map)) return;
            TerrainDef current = cell.GetTerrain(map);
            if (current == null || current.IsWater) return;
            if (current.layerable) return;
            if (IsExcludedLayoutFloor(current) && !TryGetRockKind(current, out _)) return;
            if (current != hewn)
                map.terrainGrid.SetTerrain(cell, hewn);
        }

        public static void TrySpawnPenAnimals(Map map, IntVec3 markerCell)
        {
            if (!sessionActive || !PenLivestockEnabled || map == null || !markerCell.InBounds(map)) return;

            WdBiomePenLivestockTableDef table = WdBiomeTableResolver.ResolvePenLivestockTable(sessionBiome ?? map.Biome);
            PawnKindDef chosen = null;
            if (table?.animals != null)
                chosen = WdBiomeTableResolver.PickWeightedPawnKind(table.animals);

            if (chosen?.race?.race == null || !chosen.race.race.Animal)
            {
                chosen = DefDatabase<PawnKindDef>.GetNamedSilentFail("Cow")
                    ?? DefDatabase<PawnKindDef>.GetNamedSilentFail("Muffalo");
            }
            if (chosen == null) return;

            int count = Rand.RangeInclusive(3, 5);
            for (int i = 0; i < count; i++)
            {
                if (!CellFinder.TryFindRandomCellNear(markerCell, map, 5,
                        c => c.InBounds(map) && c.Standable(map) && c.GetFirstBuilding(map) == null,
                        out IntVec3 spawnCell))
                    continue;

                try
                {
                    Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                        chosen,
                        faction: null,
                        context: PawnGenerationContext.NonPlayer,
                        forceGenerateNewPawn: true,
                        canGeneratePawnRelations: false,
                        allowFood: true));
                    if (pawn == null) continue;
                    GenSpawn.Spawn(pawn, spawnCell, map);
                }
                catch
                {
                    // Soft-fail individual animals.
                }
            }
        }

        public static ThingDef GetSymbolThingDef(object symbol)
        {
            if (symbol == null) return null;
            EnsureFields();
            return thingDefField?.GetValue(symbol) as ThingDef;
        }

        public static bool PushAndRemapSymbol(object symbol)
        {
            if (!sessionActive || symbol == null) return false;
            EnsureFields();
            if (thingDefField == null) return false;

            var origThing = thingDefField.GetValue(symbol) as ThingDef;
            var origStuff = stuffDefField != null ? stuffDefField.GetValue(symbol) as ThingDef : null;
            var newThing = RemapThing(origThing);
            var newStuff = RemapStuff(origStuff);
            // Stuffable layout symbols without stuff (e.g. WallLamp_* when ReBuild makes
            // WallLamp MadeFromStuff) must get a material before KCSG MakeThing — WallLamp
            // hits KCSG's "wall" path which has no furniture-style null-stuff fallback.
            if (newStuff == null && newThing != null && newThing.MadeFromStuff)
                newStuff = DefaultStuffForLayoutSymbol(newThing);
            if (newThing == origThing && newStuff == origStuff) return false;

            if (symbolRestoreStack == null)
                symbolRestoreStack = new Stack<(object, ThingDef, ThingDef)>();
            symbolRestoreStack.Push((symbol, origThing, origStuff));
            if (newThing != origThing)
                thingDefField.SetValue(symbol, newThing);
            if (stuffDefField != null && newStuff != origStuff)
                stuffDefField.SetValue(symbol, newStuff);
            return true;
        }

        /// <summary>
        /// Session-independent: fill null stuff on MadeFromStuff KCSG symbols.
        /// Needed for Crashlanded / non-WD KCSG spawns where remapper Begin never runs.
        /// Does not restore — leaving Steel (or default) on the symbol is correct.
        /// </summary>
        public static void EnsureDefaultStuffIfNeeded(object symbol)
        {
            if (symbol == null) return;
            EnsureFields();
            if (thingDefField == null || stuffDefField == null) return;

            var thing = thingDefField.GetValue(symbol) as ThingDef;
            if (thing == null || !thing.MadeFromStuff) return;
            if (stuffDefField.GetValue(symbol) is ThingDef) return;

            ThingDef fill = DefaultStuffForLayoutSymbol(thing);
            if (fill != null)
                stuffDefField.SetValue(symbol, fill);
        }

        /// <summary>
        /// KCSG passes a room-wide wallStuff into every "*wall*" defName (including WallLamp).
        /// If that material is not allowed for this thing, clear it so symbol.stuffDef is used.
        /// </summary>
        public static void ClearIncompatibleWallStuff(object symbol, ref ThingDef wallStuff)
        {
            if (wallStuff == null || symbol == null) return;
            EnsureFields();
            var thing = thingDefField?.GetValue(symbol) as ThingDef;
            if (thing == null || !thing.MadeFromStuff) return;
            if (!StuffAllowedFor(thing, wallStuff))
                wallStuff = null;
        }

        /// <summary>
        /// Prefer Steel when the thing accepts Metallic (matches prior WallLamp_Steel_* layouts);
        /// otherwise GenStuff.DefaultStuffFor.
        /// </summary>
        private static ThingDef DefaultStuffForLayoutSymbol(ThingDef thing)
        {
            if (thing == null || !thing.MadeFromStuff) return null;
            if (ThingDefOf.Steel != null && StuffAllowedFor(thing, ThingDefOf.Steel))
                return ThingDefOf.Steel;
            return GenStuff.DefaultStuffFor(thing);
        }

        private static bool StuffAllowedFor(ThingDef thing, ThingDef stuff)
        {
            if (thing?.stuffCategories == null || stuff?.stuffProps?.categories == null)
                return false;
            for (int i = 0; i < thing.stuffCategories.Count; i++)
            {
                if (stuff.stuffProps.categories.Contains(thing.stuffCategories[i]))
                    return true;
            }
            return false;
        }

        public static void PopSymbolRestore()
        {
            if (symbolRestoreStack == null || symbolRestoreStack.Count == 0) return;
            RestoreSymbol(symbolRestoreStack.Pop());
        }

        private static void RestoreSymbol((object symbol, ThingDef thing, ThingDef stuff) entry)
        {
            if (entry.symbol == null) return;
            EnsureFields();
            thingDefField?.SetValue(entry.symbol, entry.thing);
            stuffDefField?.SetValue(entry.symbol, entry.stuff);
        }

        private static void EnsureFields()
        {
            if (thingDefField != null) return;
            var symbolType = AccessTools.TypeByName("KCSG.SymbolDef");
            if (symbolType == null) return;
            thingDefField = AccessTools.Field(symbolType, "thingDef");
            stuffDefField = AccessTools.Field(symbolType, "stuffDef");
        }

        private static ThingDef RemapPlantOrOre(ThingDef def)
        {
            if (!sessionActive || def == null) return def;

            if (def.defName == CropPlaceholder && chosenCrop != null)
                return chosenCrop;

            if (TreeRemapEnabled)
            {
                if (def.defName == TreeRank1 && chosenTree1 != null) return chosenTree1;
                if (def.defName == TreeRank2 && chosenTree2 != null) return chosenTree2;
                if (def.defName == TreeRank3 && chosenTree3 != null) return chosenTree3;
            }

            if (def.defName == OreCheapPlaceholder && chosenCheapOre != null)
            {
                return chosenCheapOre;
            }
            if (def.defName == OreExpensivePlaceholder && chosenExpensiveOre != null)
            {
                return chosenExpensiveOre;
            }

            return def;
        }

        private static ThingDef ResolveSessionCrop(Map map)
        {
            BiomeDef biome = map != null ? map.Biome : sessionBiome;
            Map sampleMap = map ?? sessionMap;
            var all = DefDatabase<WdBiomeCropTableDef>.AllDefsListForReading;
            WdBiomeCropTableDef exact = all?.FirstOrDefault(d => d != null && !d.IsDefault && d.MatchesBiome(biome));

            if (exact?.plants != null)
            {
                ThingDef picked = PickViableWeightedPlant(exact, sampleMap, layoutFailedCrops);
                if (picked != null) return picked;
            }

            WdBiomeCropTableDef fallback = WdBiomeTableResolver.ResolveCropTable(biome);
            ThingDef fromTable = PickViableWeightedPlant(fallback, sampleMap, layoutFailedCrops);
            if (fromTable != null) return fromTable;

            return PickSowableCropFromBiome(biome)
                ?? DefDatabase<ThingDef>.GetNamedSilentFail(CropPlaceholder);
        }

        private static ThingDef PickViableWeightedPlant(
            WdBiomeCropTableDef table,
            Map map,
            HashSet<ThingDef> exclude)
        {
            if (table?.plants == null) return null;
            var viable = GetViableCropOptions(table, map, exclude);
            return WdBiomeTableResolver.PickWeightedPlant(viable);
        }

        private static List<WdWeightedPlantOption> GetViableCropOptions(
            WdBiomeCropTableDef table,
            Map map,
            HashSet<ThingDef> exclude)
        {
            var viable = new List<WdWeightedPlantOption>();
            if (table?.plants == null) return viable;

            foreach (WdWeightedPlantOption opt in table.plants)
            {
                if (opt?.plantDef == null) continue;
                if (exclude != null && exclude.Contains(opt.plantDef)) continue;
                if (map != null && !IsCropViableAt(opt.plantDef, map)) continue;
                viable.Add(opt);
            }

            return viable;
        }

        private static bool IsCropViableAt(ThingDef plantDef, Map map)
        {
            if (plantDef?.plant == null || map == null) return false;
            IntVec3 sample = sessionSampleCell.IsValid ? sessionSampleCell : map.Center;
            if (!sample.InBounds(map)) sample = map.Center;
            return PlantUtility.CanEverPlantAt(plantDef, sample, map, canWipePlantsExceptTree: false);
        }

        private static IntVec3 FindBestCropSampleCell(Map map)
        {
            if (map == null) return IntVec3.Invalid;

            IntVec3 bestClean = IntVec3.Invalid;
            float bestCleanFertility = float.MinValue;
            IntVec3 bestAny = IntVec3.Invalid;
            float bestAnyFertility = float.MinValue;

            foreach (IntVec3 c in map.AllCells)
            {
                if (!c.InBounds(map) || !c.Walkable(map)) continue;

                TerrainDef terrain = c.GetTerrain(map);
                if (terrain == null || terrain.IsWater) continue;

                float fertility = terrain.fertility;
                if (fertility <= 0f) continue;

                float pollution = CellPollution01(map, c);
                if (pollution <= 0f)
                {
                    if (fertility > bestCleanFertility)
                    {
                        bestCleanFertility = fertility;
                        bestClean = c;
                    }
                }

                if (fertility > bestAnyFertility)
                {
                    bestAnyFertility = fertility;
                    bestAny = c;
                }
            }

            if (bestClean.IsValid) return bestClean;
            if (bestAny.IsValid) return bestAny;
            return map.Center;
        }

        private static float CellPollution01(Map map, IntVec3 cell)
        {
            if (map == null || !cell.InBounds(map) || !ModsConfig.BiotechActive) return 0f;
            return cell.IsPolluted(map) ? 1f : 0f;
        }

        private static void ClearLayoutOreSpawned()
        {
            layoutSpawnedCheap = layoutSpawnedCheap ?? new HashSet<ThingDef>();
            layoutSpawnedExpensive = layoutSpawnedExpensive ?? new HashSet<ThingDef>();
            layoutSpawnedCheap.Clear();
            layoutSpawnedExpensive.Clear();
        }

        private static void RecordSpawnedMineable(ThingDef mineable)
        {
            if (mineable == null) return;
            WdMiningOrePoolDef pool = WdBiomeTableResolver.ResolveMiningOrePool(sessionBiome);
            bool isCheap = pool?.cheapOres != null && pool.cheapOres.Any(o => o?.thingDef == mineable);
            bool isExpensive = pool?.expensiveOres != null && pool.expensiveOres.Any(o => o?.thingDef == mineable);

            layoutSpawnedCheap = layoutSpawnedCheap ?? new HashSet<ThingDef>();
            layoutSpawnedExpensive = layoutSpawnedExpensive ?? new HashSet<ThingDef>();
            sessionSpawnedCheap = sessionSpawnedCheap ?? new HashSet<ThingDef>();
            sessionSpawnedExpensive = sessionSpawnedExpensive ?? new HashSet<ThingDef>();

            if (isCheap)
            {
                layoutSpawnedCheap.Add(mineable);
                sessionSpawnedCheap.Add(mineable);
            }
            else if (isExpensive)
            {
                layoutSpawnedExpensive.Add(mineable);
                sessionSpawnedExpensive.Add(mineable);
            }
            else
            {
            }
        }

        private static void AddVerifiedCropProduct(ThingDef plantDef)
        {
            ThingDef product = HarvestProductForPlant(plantDef);
            if (product == null) return;

            sessionCropProducts = sessionCropProducts ?? new List<WdWeightedThingOption>();
            if (sessionCropProducts.Any(o => o.thingDef == product)) return;

            float weight = 1f;
            WdBiomeCropTableDef table = WdBiomeTableResolver.ResolveCropTable(sessionBiome);
            WdWeightedPlantOption match = table?.plants?.FirstOrDefault(p => p?.plantDef == plantDef);
            if (match != null) weight = Mathf.Max(0.01f, match.weight);

            sessionCropProducts.Add(new WdWeightedThingOption { thingDef = product, weight = weight });
        }

        private static void TryCropFallback(Map map)
        {
            if (layoutCropFallbackCells == null || layoutCropFallbackCells.Count == 0) return;

            WdBiomeCropTableDef table = WdBiomeTableResolver.ResolveCropTable(sessionBiome);
            ThingDef fallbackCrop = null;

            for (int attempt = 0; attempt < MaxCropFallbackTries && fallbackCrop == null; attempt++)
            {
                fallbackCrop = PickViableWeightedPlant(table, map, layoutFailedCrops);
                if (fallbackCrop == null) break;
                if (fallbackCrop == chosenCrop)
                {
                    layoutFailedCrops.Add(fallbackCrop);
                    fallbackCrop = null;
                }
            }

            if (fallbackCrop == null)
            {
                WDVerbose.RemapNoTick($"KCSG remap layout={currentLayoutName} crop fallback failed after {MaxCropFallbackTries} tries");
                return;
            }

            chosenCrop = fallbackCrop;
            int placed = 0;
            foreach (IntVec3 cell in layoutCropFallbackCells)
            {
                if (!cell.InBounds(map)) continue;
                if (GetPlantDefAt(map, cell) != null) continue;
                EnsureFertileUnderCrop(map, cell);
                try
                {
                    Plant plant = (Plant)ThingMaker.MakeThing(fallbackCrop);
                    plant.Growth = Rand.Range(MinCropGrowth, MaxCropGrowth);
                    GenSpawn.Spawn(plant, cell, map);
                    placed++;
                    AddVerifiedCropProduct(fallbackCrop);
                }
                catch
                {
                    // Soft-fail individual cells.
                }
            }

            layoutCropPlaced += placed;
            WDVerbose.RemapNoTick(
                $"KCSG remap crop fallback layout={currentLayoutName} crop={fallbackCrop.defName} placed={placed}/{layoutCropFallbackCells.Count}");
        }

        private static ThingDef GetPlantDefAt(Map map, IntVec3 cell)
        {
            if (map == null || !cell.InBounds(map)) return null;
            List<Thing> things = cell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i] is Plant plant)
                    return plant.def;
            }
            return null;
        }

        private static string FormatDefSet(HashSet<ThingDef> defs)
        {
            if (defs == null || defs.Count == 0) return "-";
            return string.Join(",", defs.Select(d => d.defName));
        }

        private static ThingDef PickSowableCropFromBiome(BiomeDef biome)
        {
            if (biome?.wildPlants == null || biome.wildPlants.Count == 0) return null;
            var options = new List<WdWeightedPlantOption>();
            foreach (BiomePlantRecord record in biome.wildPlants)
            {
                ThingDef plant = record?.plant;
                if (plant?.plant == null) continue;
                if (!plant.plant.Sowable || plant.plant.IsTree) continue;
                options.Add(new WdWeightedPlantOption { plantDef = plant, weight = Mathf.Max(0.01f, record.commonality) });
            }
            return WdBiomeTableResolver.PickWeightedPlant(options);
        }

        private static void ResolveSessionTrees(Map map)
        {
            chosenTree1 = DefDatabase<ThingDef>.GetNamedSilentFail(TreeRank1);
            chosenTree2 = DefDatabase<ThingDef>.GetNamedSilentFail(TreeRank2);
            chosenTree3 = DefDatabase<ThingDef>.GetNamedSilentFail(TreeRank3);

            if (!TreeRemapEnabled) return;

            List<ThingDef> ranked = RankBiomeTrees(map != null ? map.Biome : sessionBiome);
            if (ranked.Count >= 1) chosenTree1 = ranked[0];
            if (ranked.Count >= 2) chosenTree2 = ranked[1];
            else chosenTree2 = chosenTree1;
            if (ranked.Count >= 3) chosenTree3 = ranked[2];
            else chosenTree3 = chosenTree2 ?? chosenTree1;
        }

        private static List<ThingDef> RankBiomeTrees(BiomeDef biome)
        {
            var result = new List<ThingDef>();
            if (biome?.wildPlants == null) return result;

            foreach (BiomePlantRecord record in biome.wildPlants.OrderByDescending(p => p.commonality))
            {
                ThingDef plant = record?.plant;
                if (plant?.plant == null || !plant.plant.IsTree) continue;
                if (result.Contains(plant)) continue;
                result.Add(plant);
                if (result.Count >= 3) break;
            }
            return result;
        }

        private static void ResolveSessionOres(Map map)
        {
            BiomeDef biome = map != null ? map.Biome : sessionBiome;
            WdMiningOrePoolDef pool = WdBiomeTableResolver.ResolveMiningOrePool(biome);
            chosenCheapOre = WdBiomeTableResolver.PickWeightedThing(pool?.cheapOres)
                ?? DefDatabase<ThingDef>.GetNamedSilentFail(OreCheapPlaceholder);
            chosenExpensiveOre = WdBiomeTableResolver.PickWeightedThing(pool?.expensiveOres)
                ?? DefDatabase<ThingDef>.GetNamedSilentFail(OreExpensivePlaceholder);
            // Included in session/layout summaries; no standalone ore-roll line.
        }

        private static string FormatOreProduct(ThingDef mineable)
        {
            ThingDef product = MinedProductForMineable(mineable);
            return product != null ? product.defName : "?";
        }
    }
}
