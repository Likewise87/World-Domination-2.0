using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KCSG;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Places facing-correct Generic_MG layouts on settlement outer borders after KCSG gen.
    /// Counts come from <see cref="WdMgNestSpawnTableDef"/>; placement prefers mid-edge then slides off roads.
    /// Nest sits one cell past the settlement outer rim (away from center). Footprint is nuked first
    /// (including prior KCSG buildings). Layouts are sandbags/wire only; turret comes from
    /// <see cref="WdMgTurretResolver"/> by settlement tier. Mannable picks get a Settlement-group gunner
    /// and sticky no-lord manning keeper (vanilla ManTurrets fails on CE M240).
    /// </summary>
    public static class WdMgNestSpawner
    {
        private const string TableDefName = "TSA_WdMgNestSpawns";
        /// <summary>Place the nest this many cells past the settlement outer rim (away from center).</summary>
        private const int OuterPushCells = 1;
        /// <summary>Extra gap between trench rails and nest footprint on clash maps.</summary>
        private const int TrenchNestExtraStandoff = 1;
        private const string CePackageId = "CETeam.CombatExtended";
        private const string CeMgTurretDefName = "Turret_M240B";
        private const string AutoTurretDefName = "Turret_MiniTurret";
        private const string CeAmmoSet762 = "AmmoSet_762x51mmNATO";
        private const string CeAmmoFmJ = "Ammo_762x51mmNATO_FMJ";
        private const int CeAmmoStackCount = 200;

        private static readonly Rot4[] Sides =
        {
            Rot4.North,
            Rot4.East,
            Rot4.South,
            Rot4.West
        };

        private static bool ceMgResolveDone;
        private static ThingDef cachedCeMgTurretDef;
        private static ThingDef cachedAutoTurretDef;

        /// <summary>
        /// True when Combat Extended content is available.
        /// Uses cached M240 / CE type presence — trench map gen can run on a LongEvent worker
        /// where <see cref="ModsConfig.IsActive"/> (and sometimes name lookup alone) is unreliable.
        /// </summary>
        public static bool IsCombatExtendedActive() =>
            ResolveCeMgTurretDef() != null || CeAssemblyPresent();

        private static bool CeAssemblyPresent()
        {
            try
            {
                if (GenTypes.GetTypeInAnyAssembly("CombatExtended.CompAmmoUser", "CombatExtended") != null)
                    return true;
            }
            catch
            {
                // ignore
            }

            try
            {
                return ModsConfig.IsActive(CePackageId) || ModsConfig.IsActive(CePackageId + "_steam");
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Live CE M240 ThingDef for trench MG slots. Prefer pool / AllDefs references over
        /// repeated GetNamedSilentFail during map gen (that was returning null with CE loaded).
        /// </summary>
        private static ThingDef ResolveCeMgTurretDef()
        {
            if (ceMgResolveDone)
                return cachedCeMgTurretDef;
            ceMgResolveDone = true;

            cachedCeMgTurretDef = DefDatabase<ThingDef>.GetNamedSilentFail(CeMgTurretDefName);
            if (cachedCeMgTurretDef != null)
                return cachedCeMgTurretDef;

            // Pool XML already cross-resolved thingDef at load — stronger than a re-lookup mid-gen.
            WdMgTurretPoolDef pool = DefDatabase<WdMgTurretPoolDef>.GetNamedSilentFail(WdMgTurretResolver.PoolDefName);
            if (pool?.tiers != null)
            {
                for (int t = 0; t < pool.tiers.Count; t++)
                {
                    WdMgTurretTier tier = pool.tiers[t];
                    if (tier?.options == null) continue;
                    for (int i = 0; i < tier.options.Count; i++)
                    {
                        ThingDef def = tier.options[i]?.thingDef;
                        if (def == null) continue;
                        if (def.defName == CeMgTurretDefName
                            || (WdMgTurretResolver.IsMannableDef(def)
                                && def.defName != null
                                && def.defName.IndexOf("M240", StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            cachedCeMgTurretDef = def;
                            return cachedCeMgTurretDef;
                        }
                    }
                }
            }

            List<ThingDef> all = DefDatabase<ThingDef>.AllDefsListForReading;
            if (all != null)
            {
                for (int i = 0; i < all.Count; i++)
                {
                    ThingDef d = all[i];
                    if (d?.defName == CeMgTurretDefName)
                    {
                        cachedCeMgTurretDef = d;
                        return cachedCeMgTurretDef;
                    }
                }
            }

            return null;
        }

        private static ThingDef ResolveAutoTurretDef() =>
            cachedAutoTurretDef ??= DefDatabase<ThingDef>.GetNamedSilentFail(AutoTurretDefName)
                ?? DefDatabase<ThingDef>.GetNamedSilentFail(WdMgTurretResolver.FallbackTurretDefName);

        /// <summary>
        /// Trench defense nests on the defender flank along a cardinal E–W / N–S corridor.
        /// Prefer <see cref="TrySpawnAlongTrenchAxis"/> for 22.5° / diagonal trenches.
        /// </summary>
        public static HashSet<IntVec3> TrySpawnAlongTrench(
            Map map,
            bool trenchIsHorizontal,
            int baseMid,
            int[] lateralOffsets,
            int alongLen,
            int corridorWidth,
            Faction faction,
            bool hostileFromNorthOrEast,
            int countMin = 4,
            int countMax = 4,
            int poolLevel = 2)
        {
            Rot4 facing = trenchIsHorizontal
                ? (hostileFromNorthOrEast ? Rot4.North : Rot4.South)
                : (hostileFromNorthOrEast ? Rot4.East : Rot4.West);
            Vector2 along = trenchIsHorizontal ? Vector2.right : Vector2.up;
            Vector2 perp = trenchIsHorizontal ? Vector2.up : Vector2.right;
            IntVec3 center = map != null
                ? new IntVec3(
                    trenchIsHorizontal ? alongLen / 2 : baseMid,
                    0,
                    trenchIsHorizontal ? baseMid : alongLen / 2)
                : IntVec3.Zero;
            if (map != null)
                center = map.Center;
            return TrySpawnAlongTrenchAxis(
                map,
                center,
                along,
                perp,
                lateralOffsets,
                corridorWidth,
                faction,
                facing,
                countMin,
                countMax,
                poolLevel);
        }

        /// <summary>
        /// Trench defense nests along an arbitrary trench axis (bridge-style along/perp).
        /// MG layouts stay cardinal-facing (<paramref name="facing"/>); footprint centers follow the angled corridor.
        /// Fixed slots: CE → Auto/MG/Auto/MG; else four Autos.
        /// </summary>
        public static HashSet<IntVec3> TrySpawnAlongTrenchAxis(
            Map map,
            IntVec3 mapCenter,
            Vector2 along,
            Vector2 perp,
            int[] lateralOffsets,
            int corridorWidth,
            Faction faction,
            Rot4 facing,
            int countMin = 4,
            int countMax = 4,
            int poolLevel = 2,
            HashSet<IntVec3> corridorCells = null)
        {
            var reserved = new HashSet<IntVec3>();
            if (map == null || corridorWidth < 3) return reserved;
            if (along.sqrMagnitude < 1e-6f) along = Vector2.right;
            else along.Normalize();
            if (perp.sqrMagnitude < 1e-6f) perp = new Vector2(-along.y, along.x);
            else perp.Normalize();

            _ = countMin;
            _ = countMax;
            int target = 4;
            ThingDef ceMg = ResolveCeMgTurretDef();
            bool ceMgAvailable = ceMg != null;
            if (!ceMgAvailable && CeAssemblyPresent())
            {
                Log.Warning(
                    "[TSA WD] CE assembly present but Turret_M240B could not be resolved for trench nests "
                    + "(GetNamed + pool + AllDefs). Odd slots fall back to mini turrets.");
            }

            float[] fracs = { 0.18f, 0.38f, 0.62f, 0.82f };
            IntVec2 size = NestSizeForSide(facing);
            Vector2 centerV = new Vector2(mapCenter.x + 0.5f, mapCenter.z + 0.5f);
            float halfW = corridorWidth * 0.5f;
            int depthAlongPerp = (facing == Rot4.North || facing == Rot4.South) ? size.z : size.x;
            // Nest AABB must fit; outer fracs used to lerp over the map diagonal, which puts slots
            // off-map on near-cardinal axes (e.g. axis≈(-0.92,0.38) → only mid two survive).
            float alongMargin = Mathf.Max(size.x, size.z) * 0.5f + TrenchNestExtraStandoff + 2f;
            if (!TryGetNestAlongSpan(map, centerV, along, corridorCells, alongMargin, out float tLo, out float tHi))
            {
                Log.Warning(
                    $"[TSA WD] Trench defense nests: no in-map along-span "
                    + $"(map={map.Size.x}x{map.Size.z} along=({along.x:0.##},{along.y:0.##})).");
                return reserved;
            }

            // Hostile is the direction guns face; nest sits on the opposite flank of the trench.
            Vector2 faceDir = facing == Rot4.North ? Vector2.up
                : facing == Rot4.South ? -Vector2.up
                : facing == Rot4.East ? Vector2.right
                : -Vector2.right;
            float nestSide = -Mathf.Sign(Vector2.Dot(faceDir, perp));
            if (Mathf.Abs(nestSide) < 0.01f) nestSide = -1f;

            int placed = 0;
            int failRect = 0;
            int failPaint = 0;
            var placedDefs = new List<string>(4);
            for (int i = 0; i < fracs.Length && placed < target; i++)
            {
                float t = Mathf.Lerp(tLo, tHi, fracs[i]);
                float lat = SampleLateralOffsetFloat(lateralOffsets, t);
                Vector2 trenchMid = centerV + along * t + perp * lat;

                if (!TryBuildTrenchNestRectOnAxis(
                        map,
                        trenchMid,
                        perp,
                        nestSide,
                        halfW,
                        depthAlongPerp,
                        size,
                        corridorCells,
                        reserved,
                        out CellRect nestRect))
                {
                    failRect++;
                    continue;
                }

                ThingDef slotTurret = ResolveTrenchSlotTurret(placed, ceMg);
                if (slotTurret == null)
                {
                    Log.Warning($"[TSA WD] Trench nest slot {placed}: no turret def resolved (CE-MG={ceMgAvailable}).");
                    continue;
                }

                if (!TryPlaceNestInRectManual(map, nestRect, facing, faction, poolLevel, slotTurret, tryManGunner: false))
                {
                    failPaint++;
                    continue;
                }

                foreach (IntVec3 c in nestRect)
                    reserved.Add(c);
                foreach (IntVec3 c in nestRect.ExpandedBy(1).ClipInsideMap(map))
                    reserved.Add(c);

                placedDefs.Add(slotTurret.defName);
                placed++;
            }

            if (placed < target)
            {
                Log.Warning(
                    $"[TSA WD] Trench defense nests placed={placed}/{target} guns=[{string.Join(", ", placedDefs)}] "
                    + $"axis=({along.x:0.##},{along.y:0.##}) face={facing} ceMg={ceMgAvailable} "
                    + $"t=[{tLo:0.#}..{tHi:0.#}] failRect={failRect} failPaint={failPaint}");
            }
            else
            {
                WDVerbose.MsgNoTick(
                    $"Trench defense nests placed={placed}/{target} guns=[{string.Join(", ", placedDefs)}] "
                    + $"axis=({along.x:0.##},{along.y:0.##}) face={facing} ceMg={ceMgAvailable}");
            }
            return reserved;
        }

        /// <summary>
        /// Along-axis span where nest centers can sit on-map. Prefers corridor coverage; falls back to a map ray.
        /// </summary>
        private static bool TryGetNestAlongSpan(
            Map map,
            Vector2 centerV,
            Vector2 along,
            HashSet<IntVec3> corridorCells,
            float margin,
            out float tLo,
            out float tHi)
        {
            tLo = float.MaxValue;
            tHi = float.MinValue;
            if (corridorCells != null && corridorCells.Count > 0)
            {
                foreach (IntVec3 c in corridorCells)
                {
                    float t = Vector2.Dot(new Vector2(c.x + 0.5f, c.z + 0.5f) - centerV, along);
                    if (t < tLo) tLo = t;
                    if (t > tHi) tHi = t;
                }
            }
            else
            {
                // Sample the axis for cells still inside the map.
                float diag = Mathf.Sqrt(map.Size.x * map.Size.x + map.Size.z * map.Size.z) + 8f;
                for (float t = -diag; t <= diag; t += 1f)
                {
                    Vector2 p = centerV + along * t;
                    var cell = new IntVec3(Mathf.FloorToInt(p.x), 0, Mathf.FloorToInt(p.y));
                    if (!cell.InBounds(map)) continue;
                    if (t < tLo) tLo = t;
                    if (t > tHi) tHi = t;
                }
            }

            if (tHi < tLo) return false;
            tLo += margin;
            tHi -= margin;
            return tHi - tLo >= 8f;
        }

        /// <summary>
        /// Axis-aligned nest rect behind the trench on the defender flank; nudges out along perp until clear of corridor.
        /// </summary>
        private static bool TryBuildTrenchNestRectOnAxis(
            Map map,
            Vector2 trenchMid,
            Vector2 perp,
            float nestSide,
            float halfW,
            int depthAlongPerp,
            IntVec2 size,
            HashSet<IntVec3> corridorCells,
            HashSet<IntVec3> reserved,
            out CellRect nestRect)
        {
            nestRect = default;
            for (int nudge = 0; nudge <= 8; nudge++)
            {
                float dist = halfW + TrenchNestExtraStandoff + depthAlongPerp * 0.5f + nudge;
                Vector2 nestCenter = trenchMid + perp * (nestSide * dist);
                int minX = Mathf.RoundToInt(nestCenter.x - size.x * 0.5f);
                int minZ = Mathf.RoundToInt(nestCenter.y - size.z * 0.5f);
                var candidate = new CellRect(minX, minZ, size.x, size.z);
                CellRect clipped = candidate.ClipInsideMap(map);
                if (clipped.Width != candidate.Width || clipped.Height != candidate.Height)
                    continue;

                bool blocked = false;
                foreach (IntVec3 c in candidate)
                {
                    if (reserved != null && reserved.Contains(c))
                    {
                        blocked = true;
                        break;
                    }
                    if (corridorCells != null && corridorCells.Contains(c))
                    {
                        blocked = true;
                        break;
                    }
                }
                if (blocked) continue;

                nestRect = candidate;
                return true;
            }
            return false;
        }

        private static float SampleLateralOffsetFloat(int[] lateral, float t)
        {
            if (lateral == null || lateral.Length == 0) return 0f;
            float fi = Mathf.Abs(t);
            int i0 = Mathf.Clamp(Mathf.FloorToInt(fi), 0, lateral.Length - 1);
            int i1 = Mathf.Clamp(Mathf.CeilToInt(fi), 0, lateral.Length - 1);
            if (i0 == i1) return lateral[i0];
            float frac = Mathf.Clamp01(fi - i0);
            return Mathf.Lerp(lateral[i0], lateral[i1], frac);
        }

        /// <summary>Slot order when CE MG def exists: Auto, MG, Auto, MG. Otherwise four Autos.</summary>
        private static ThingDef ResolveTrenchSlotTurret(int slotIndex, ThingDef ceMg)
        {
            if (ceMg != null && (slotIndex % 2) == 1)
                return ceMg;

            return ResolveAutoTurretDef();
        }

        /// <summary>Hardcoded Generic_MG_* footprint sizes (KCSG Sizes can be 0 outside settlement resolve).</summary>
        private static IntVec2 NestSizeForSide(Rot4 side)
        {
            if (side == Rot4.North) return new IntVec2(5, 3); // Generic_MG_1
            if (side == Rot4.West) return new IntVec2(4, 5);  // Generic_MG_2
            if (side == Rot4.East) return new IntVec2(4, 5);  // Generic_MG_3
            return new IntVec2(5, 4);                         // Generic_MG_4
        }

        private static int SampleLateralOffset(int[] lateral, int t)
        {
            if (lateral == null || lateral.Length == 0) return 0;
            return lateral[Mathf.Clamp(t, 0, lateral.Length - 1)];
        }

        /// <summary>
        /// Place nest on the defender flank (behind the trench), guns <paramref name="facing"/> toward hostiles.
        /// Facing North/East → nest on the South/West side of the rails (and vice versa).
        /// </summary>
        private static bool TryBuildTrenchNestRectBehind(
            bool trenchIsHorizontal,
            Rot4 facing,
            int along,
            int railLo,
            int railHi,
            IntVec2 size,
            out CellRect rect)
        {
            rect = default;
            int minX;
            int minZ;
            if (trenchIsHorizontal)
            {
                minX = along - size.x / 2;
                // Face north → sit south of trench; face south → sit north of trench.
                // +TrenchNestExtraStandoff keeps a one-cell gap off the rails.
                if (facing == Rot4.North)
                    minZ = railLo - size.z - TrenchNestExtraStandoff;
                else if (facing == Rot4.South)
                    minZ = railHi + 1 + TrenchNestExtraStandoff;
                else
                    return false;
            }
            else
            {
                minZ = along - size.z / 2;
                if (facing == Rot4.East)
                    minX = railLo - size.x - TrenchNestExtraStandoff;
                else if (facing == Rot4.West)
                    minX = railHi + 1 + TrenchNestExtraStandoff;
                else
                    return false;
            }

            rect = new CellRect(minX, minZ, size.x, size.z);
            return rect.Width == size.x && rect.Height == size.z;
        }

        /// <summary>Generate a Generic_MG layout + pool turret into an already-chosen rect (KCSG LayoutUtils).</summary>
        public static bool TryPlaceNestInRect(
            Map map,
            CellRect nestRect,
            Rot4 facing,
            Faction faction,
            int poolLevel,
            bool tryManGunner = true)
        {
            if (map == null) return false;
            string layoutDefName = LayoutDefNameForSide(facing);
            KCSG.StructureLayoutDef nestLayout = DefDatabase<KCSG.StructureLayoutDef>.GetNamedSilentFail(layoutDefName);
            if (nestLayout == null) return false;

            try
            {
                NukeNestFootprint(map, nestRect);
                LayoutUtils.Generate(nestLayout, nestRect, map, faction);
                TrySpawnNestTurret(map, nestRect, facing, faction, poolLevel, tryManGunner);
                return true;
            }
            catch (Exception ex)
            {
                WDVerbose.MsgNoTick($"MG nest generate error layout={layoutDefName} rect={nestRect}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Trench-safe nest place: paints sandbags/wire from the layout symbols without KCSG LayoutUtils
        /// (settlement GenOption / resolve context is absent on clash maps).
        /// </summary>
        public static bool TryPlaceNestInRectManual(
            Map map,
            CellRect nestRect,
            Rot4 facing,
            Faction faction,
            int poolLevel,
            ThingDef forceTurretDef = null,
            bool tryManGunner = false)
        {
            if (map == null) return false;
            try
            {
                NukeNestFootprint(map, nestRect);
                // Trench nests: Generic_MG_* opens toward the facing (enemy). Flip bags/wire 180°
                // so the parapet sits toward the attack and the gunner pocket stays behind the gun.
                if (!TryPaintMgNestSymbols(map, nestRect, facing, faction, flipBagsAndWire180: true))
                    return false;
                TrySpawnNestTurret(
                    map,
                    nestRect,
                    facing,
                    faction,
                    poolLevel,
                    tryManGunner: tryManGunner,
                    flipBagsAndWire180: true,
                    forceTurretDef: forceTurretDef);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning($"[TSA WD] Trench MG nest manual place failed facing={facing} rect={nestRect}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Paint Generic_MG_* symbols. Rows are north→south (maxZ first), matching ResolveTurretCell.
        /// Symbols: W=AncientRazorWire, S=Sandbags+Cloth, .=empty.
        /// </summary>
        private static bool TryPaintMgNestSymbols(
            Map map,
            CellRect nestRect,
            Rot4 facing,
            Faction faction,
            bool flipBagsAndWire180 = false)
        {
            string[] rows = MgNestSymbolRows(facing);
            if (flipBagsAndWire180)
                rows = FlipSymbolRows180(rows);
            if (rows == null || rows.Length == 0) return false;
            if (rows.Length != nestRect.Height) return false;

            ThingDef wire = DefDatabase<ThingDef>.GetNamedSilentFail("AncientRazorWire")
                ?? DefDatabase<ThingDef>.GetNamedSilentFail("Tr_BarbedWire");
            ThingDef sandbags = ThingDefOf.Sandbags
                ?? DefDatabase<ThingDef>.GetNamedSilentFail("Sandbags");
            ThingDef cloth = ThingDefOf.Cloth;

            for (int r = 0; r < rows.Length; r++)
            {
                string[] cells = rows[r].Split(',');
                if (cells.Length != nestRect.Width) return false;
                int z = nestRect.maxZ - r;
                for (int c = 0; c < cells.Length; c++)
                {
                    string sym = cells[c].Trim();
                    if (sym == "." || sym.Length == 0) continue;
                    IntVec3 cell = new IntVec3(nestRect.minX + c, 0, z);
                    if (!cell.InBounds(map)) continue;

                    if (sym == "W" || sym == "AncientRazorWire" || sym == "Tr_BarbedWire")
                    {
                        if (wire == null) continue;
                        TrySpawnNestBuilding(wire, null, cell, map, faction);
                    }
                    else if (sym == "S" || sym.StartsWith("Sandbags", StringComparison.Ordinal))
                    {
                        if (sandbags == null) continue;
                        ThingDef stuff = sandbags.MadeFromStuff ? cloth : null;
                        if (stuff != null && (stuff.stuffProps == null || !stuff.stuffProps.CanMake(sandbags)))
                            stuff = GenStuff.DefaultStuffFor(sandbags);
                        TrySpawnNestBuilding(sandbags, stuff, cell, map, faction);
                    }
                }
            }
            return true;
        }

        private static string[] MgNestSymbolRows(Rot4 facing)
        {
            // Compact codes matching Generic_MG_1..4 XML (north row first).
            if (facing == Rot4.North)
            {
                return new[]
                {
                    "W,S,.,S,W",
                    "W,S,S,S,W",
                    "W,W,W,W,W"
                };
            }
            if (facing == Rot4.West)
            {
                return new[]
                {
                    "W,W,W,W",
                    "W,S,S,S",
                    "W,S,.,.",
                    "W,S,S,S",
                    "W,W,W,W"
                };
            }
            if (facing == Rot4.East)
            {
                return new[]
                {
                    "W,W,W,W",
                    "S,S,S,W",
                    ".,.,S,W",
                    "S,S,S,W",
                    "W,W,W,W"
                };
            }
            // South
            return new[]
            {
                "W,W,W,W,W",
                "W,S,S,S,W",
                "W,S,.,S,W",
                "W,S,.,S,W"
            };
        }

        /// <summary>180° rotate symbol grid (reverse rows and each row) — parapet toward facing/enemy.</summary>
        private static string[] FlipSymbolRows180(string[] rows)
        {
            if (rows == null || rows.Length == 0) return rows;
            var flipped = new string[rows.Length];
            for (int r = 0; r < rows.Length; r++)
            {
                string[] cells = rows[rows.Length - 1 - r].Split(',');
                Array.Reverse(cells);
                flipped[r] = string.Join(",", cells);
            }
            return flipped;
        }

        private static void TrySpawnNestBuilding(ThingDef def, ThingDef stuff, IntVec3 cell, Map map, Faction faction)
        {
            if (def == null || !cell.InBounds(map)) return;
            try
            {
                Thing thing = def.MadeFromStuff && stuff != null
                    ? ThingMaker.MakeThing(def, stuff)
                    : ThingMaker.MakeThing(def);
                if (faction != null && thing.def.CanHaveFaction)
                    thing.SetFactionDirect(faction);
                GenSpawn.Spawn(thing, cell, map, WipeMode.Vanish);
            }
            catch
            {
                // ignore single-cell failures
            }
        }

        public static void TrySpawn(Map map)
        {
            if (map == null) return;

            string? layoutKey = ResolveSettlementLayoutKey(map);
            if (string.IsNullOrEmpty(layoutKey))
            {
                WDVerbose.MsgNoTick($"MG nests skipped settlement={SettlementLabel(map)} reason=no-layout-key");
                return;
            }

            WdMgNestSpawnTableDef table = DefDatabase<WdMgNestSpawnTableDef>.GetNamedSilentFail(TableDefName);
            if (table == null || !table.TryGetCounts(layoutKey!, out int countMin, out int countMax) || countMax <= 0)
            {
                WDVerbose.MsgNoTick($"MG nests skipped settlement={SettlementLabel(map)} layout={layoutKey} reason=count-max-zero-or-missing");
                return;
            }

            if (!WdSettlementMapUnfog.TryResolveSettlementRect(map, out CellRect settlementRect) || settlementRect.Area <= 0)
            {
                WDVerbose.MsgNoTick($"MG nests skipped settlement={SettlementLabel(map)} layout={layoutKey} reason=no-settlement-rect");
                return;
            }

            if (countMin < 0) countMin = 0;
            if (countMax < countMin) countMax = countMin;
            int n = Rand.RangeInclusive(countMin, countMax);
            if (n > 4) n = 4;
            if (n <= 0) return;

            int poolLevel = ResolvePoolLevel(map, layoutKey!);
            if (poolLevel <= 0)
            {
                WDVerbose.MsgNoTick($"MG nests skipped settlement={SettlementLabel(map)} layout={layoutKey} reason=no-pool-level");
                return;
            }

            List<Rot4> chosenSides = Sides.InRandomOrder().Take(n).ToList();
            Faction faction = map.ParentFaction;
            int placed = 0;

            for (int i = 0; i < chosenSides.Count; i++)
            {
                Rot4 side = chosenSides[i];
                if (!TryPlaceNest(map, settlementRect, side, faction, poolLevel))
                {
                    WDVerbose.MsgNoTick($"MG nest failed settlement={SettlementLabel(map)} layout={layoutKey} side={side}");
                    continue;
                }
                placed++;
            }

            WDVerbose.MsgNoTick(
                $"MG nests placed settlement={SettlementLabel(map)} layout={layoutKey} poolLevel={poolLevel} requested={n} placed={placed} rect={settlementRect}");
        }

        private static int ResolvePoolLevel(Map map, string layoutKey)
        {
            if (map.Parent is Settlement settlement)
            {
                CompViralSpread spread = settlement.GetComponent<CompViralSpread>();
                if (spread != null)
                {
                    int fromTier = WdMgTurretResolver.PoolLevelFromTier(spread.tier);
                    if (fromTier > 0) return fromTier;
                }
            }

            if (layoutKey.IndexOf("_T4_", StringComparison.Ordinal) >= 0) return 3;
            if (layoutKey.IndexOf("_T3_", StringComparison.Ordinal) >= 0) return 2;
            if (layoutKey.IndexOf("_T2_", StringComparison.Ordinal) >= 0) return 1;
            return 0;
        }

        private static bool TryPlaceNest(Map map, CellRect settlementRect, Rot4 side, Faction faction, int poolLevel)
        {
            string layoutDefName = LayoutDefNameForSide(side);
            KCSG.StructureLayoutDef nestLayout = DefDatabase<KCSG.StructureLayoutDef>.GetNamedSilentFail(layoutDefName);
            if (nestLayout == null)
            {
                WDVerbose.MsgNoTick($"MG nest layout missing defName={layoutDefName}");
                return false;
            }

            IntVec2 size = nestLayout.Sizes;
            if (size.x <= 0 || size.z <= 0) return false;

            if (!TryPickNestRect(map, settlementRect, side, size, out CellRect nestRect))
                return false;

            return TryPlaceNestInRect(map, nestRect, side, faction, poolLevel, tryManGunner: true);
        }

        /// <summary>
        /// Destroy everything already in the nest rect (including prior KCSG buildings) so the layout can place cleanly.
        /// Pawns are left alone.
        /// </summary>
        private static void NukeNestFootprint(Map map, CellRect nestRect)
        {
            foreach (IntVec3 cell in nestRect)
            {
                if (!cell.InBounds(map)) continue;

                RoofDef roof = map.roofGrid.RoofAt(cell);
                if (roof != null)
                    map.roofGrid.SetRoof(cell, null);

                List<Thing> things = cell.GetThingList(map);
                for (int i = things.Count - 1; i >= 0; i--)
                {
                    Thing t = things[i];
                    if (t == null || t.Destroyed) continue;
                    if (t is Pawn) continue;
                    // SteamGeyser etc.: Destroy logs a red error even when catchable — skip non-destroyables.
                    if (t.def == null || !t.def.destroyable) continue;
                    try
                    {
                        t.Destroy(DestroyMode.Vanish);
                    }
                    catch (Exception ex)
                    {
                        WDVerbose.MsgNoTick($"MG nest nuke skip thing={t.LabelCap} cell={cell}: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Empty pocket cell for the MG. Matches a <c>.</c> slot in Generic_MG_1..4
        /// (or the same slot after <see cref="FlipSymbolRows180"/>).
        /// </summary>
        private static IntVec3 ResolveTurretCell(CellRect nestRect, Rot4 side, bool flipBagsAndWire180 = false)
        {
            if (!flipBagsAndWire180)
            {
                if (side == Rot4.North)
                    return new IntVec3(nestRect.minX + 2, 0, nestRect.maxZ);
                if (side == Rot4.West)
                    return new IntVec3(nestRect.minX + 2, 0, nestRect.maxZ - 2);
                if (side == Rot4.East)
                    return new IntVec3(nestRect.minX + 1, 0, nestRect.maxZ - 2);
                // South
                return new IntVec3(nestRect.minX + 2, 0, nestRect.maxZ - 2);
            }

            // After 180° bag/wire flip, the empty pocket sits behind the gun (away from facing).
            if (side == Rot4.North)
                return new IntVec3(nestRect.minX + 2, 0, nestRect.minZ);
            if (side == Rot4.South)
                return new IntVec3(nestRect.minX + 2, 0, nestRect.maxZ - 1);
            if (side == Rot4.West)
                return new IntVec3(nestRect.minX + 1, 0, nestRect.maxZ - 2);
            // East
            return new IntVec3(nestRect.maxX - 1, 0, nestRect.maxZ - 2);
        }

        private static void TrySpawnNestTurret(
            Map map,
            CellRect nestRect,
            Rot4 side,
            Faction faction,
            int poolLevel,
            bool tryManGunner = true,
            bool flipBagsAndWire180 = false,
            ThingDef forceTurretDef = null)
        {
            ThingDef turretDef;
            bool manned;
            if (forceTurretDef != null)
            {
                turretDef = forceTurretDef;
                manned = WdMgTurretResolver.IsMannableDef(forceTurretDef);
            }
            else if (!WdMgTurretResolver.TryPick(poolLevel, out turretDef, out manned) || turretDef == null)
            {
                WDVerbose.MsgNoTick(
                    $"MG nest turret skip settlement={SettlementLabel(map)} side={side} reason=pool-pick-fail level={poolLevel}");
                return;
            }

            IntVec3 cell = ResolveTurretCell(nestRect, side, flipBagsAndWire180);
            if (!cell.InBounds(map))
            {
                WDVerbose.MsgNoTick(
                    $"MG nest turret skip settlement={SettlementLabel(map)} side={side} reason=cell-oob cell={cell}");
                return;
            }

            Building_Turret turret = SpawnFactionTurret(map, cell, side, turretDef, faction);
            if (turret == null)
            {
                Log.Warning(
                    $"[TSA WD] Nest turret spawn failed def={turretDef.defName} cell={cell} side={side} "
                    + $"(MakeThing type may not be Building_Turret, or GenSpawn failed).");
                return;
            }

            WDVerbose.MsgNoTick(
                $"MG nest turret spawned settlement={SettlementLabel(map)} side={side} def={turretDef.defName} manned={manned} cell={cell}");

            if (tryManGunner && manned)
                TryManNestTurret(map, nestRect, side, faction, turret);
        }

        private static Building_Turret SpawnFactionTurret(Map map, IntVec3 cell, Rot4 facing, ThingDef turretDef, Faction faction)
        {
            try
            {
                ThingDef stuff = turretDef.MadeFromStuff ? ThingDefOf.Steel : null;
                Thing made = ThingMaker.MakeThing(turretDef, stuff);
                if (made is not Building_Turret turret)
                {
                    if (made != null && !made.Destroyed)
                        made.Destroy(DestroyMode.Vanish);
                    return null;
                }

                if (faction != null)
                    turret.SetFactionDirect(faction);

                GenSpawn.Spawn(turret, cell, map, facing, WipeMode.Vanish);

                if (faction != null)
                    turret.SetFaction(faction);

                CompPowerTrader power = turret.TryGetComp<CompPowerTrader>();
                if (power != null)
                    power.PowerOn = true;

                CompRefuelable refuel = turret.TryGetComp<CompRefuelable>();
                if (refuel != null && refuel.Fuel <= 0f)
                    refuel.Refuel(refuel.Props.fuelCapacity);

                // CE MG / CE-patched turrets: fill the loaded magazine (settlement nests + trench nests).
                OutpostCeAmmoCompat.TryFillMagazineFull(turret);

                return turret;
            }
            catch (Exception ex)
            {
                WDVerbose.MsgNoTick($"MG nest turret spawn exception def={turretDef?.defName}: {ex.Message}");
                return null;
            }
        }

        private static void TryManNestTurret(Map map, CellRect nestRect, Rot4 side, Faction faction, Building_Turret turret)
        {
            if (turret == null || turret.Destroyed || turret.GetComp<CompMannable>() == null)
            {
                WDVerbose.MsgNoTick(
                    $"MG nest unmanned settlement={SettlementLabel(map)} side={side} reason=not-mannable");
                return;
            }

            Pawn gunner = TryGenerateSettlementGunner(map, faction);
            if (gunner == null)
            {
                WDVerbose.MsgNoTick(
                    $"MG nest unmanned settlement={SettlementLabel(map)} side={side} reason=gunner-gen-fail turret={turret.Position}");
                return;
            }

            IntVec3 spawnCell = ResolveGunnerSpawnCell(map, nestRect, turret);
            GenSpawn.Spawn(gunner, spawnCell, map);
            TryGiveCeMgAmmo(gunner, turret, map, spawnCell);
            // Keep chamber full even if manning/CE init cleared CurMagCount.
            OutpostCeAmmoCompat.TryFillMagazineFull(turret);

            // No LordJob_ManTurrets — that duty requires vanilla shell ammo + Building_TurretGun.
            WdMgNestGunnerKeeper.Register(map, gunner, turret);

            WDVerbose.MsgNoTick(
                $"MG nest manned settlement={SettlementLabel(map)} side={side} kind={gunner.kindDef?.defName} turret={turret.Position} spawn={spawnCell}");
        }

        private static Pawn TryGenerateSettlementGunner(Map map, Faction faction)
        {
            if (faction?.def == null) return null;

            List<Pawn> generated = null;
            try
            {
                float min = faction.def.MinPointsToGeneratePawnGroup(PawnGroupKindDefOf.Settlement);
                var parms = new PawnGroupMakerParms
                {
                    groupKind = PawnGroupKindDefOf.Settlement,
                    faction = faction,
                    tile = map.Tile,
                    points = Mathf.Max(min * 1.05f, 1f),
                    inhabitants = true
                };
                generated = PawnGroupMakerUtility.GeneratePawns(parms, warnOnZeroResults: false).ToList();
                Pawn gunner = generated.FirstOrDefault(p =>
                    p != null && !p.Destroyed && p.RaceProps.Humanlike && !p.WorkTagIsDisabled(WorkTags.Violent));

                for (int i = 0; i < generated.Count; i++)
                {
                    Pawn extra = generated[i];
                    if (extra == null || extra.Destroyed || ReferenceEquals(extra, gunner)) continue;
                    extra.Destroy(DestroyMode.Vanish);
                }

                return gunner;
            }
            catch (Exception ex)
            {
                WDVerbose.MsgNoTick($"MG nest gunner gen exception: {ex.Message}");
                if (generated != null)
                {
                    for (int i = 0; i < generated.Count; i++)
                    {
                        Pawn p = generated[i];
                        if (p != null && !p.Destroyed)
                            p.Destroy(DestroyMode.Vanish);
                    }
                }
                return null;
            }
        }

        private static IntVec3 ResolveGunnerSpawnCell(Map map, CellRect nestRect, Building_Turret turret)
        {
            IntVec3 interaction = turret.InteractionCell;
            if (interaction.InBounds(map) && interaction.Standable(map))
                return interaction;

            if (CellFinder.TryFindRandomCellInsideWith(nestRect, c => c.InBounds(map) && c.Standable(map), out IntVec3 inNest))
                return inNest;

            if (CellFinder.TryFindRandomCellNear(turret.Position, map, 4, c => c.Standable(map), out IntVec3 near))
                return near;

            return turret.Position;
        }

        /// <summary>
        /// Soft-fail CE ammo: inventory first, else drop near spawn. Manning proceeds either way.
        /// </summary>
        private static void TryGiveCeMgAmmo(Pawn gunner, Building_Turret turret, Map map, IntVec3 nearCell)
        {
            if (gunner == null || map == null) return;
            if (!ModsConfig.IsActive(CePackageId)) return;

            try
            {
                ThingDef ammoDef = ResolveCeMgAmmoDef(turret);
                if (ammoDef == null) return;

                Thing ammo = ThingMaker.MakeThing(ammoDef);
                ammo.stackCount = Math.Min(CeAmmoStackCount, ammoDef.stackLimit > 0 ? ammoDef.stackLimit : CeAmmoStackCount);

                if (gunner.inventory != null && gunner.inventory.innerContainer.TryAdd(ammo))
                    return;

                if (!ammo.Destroyed)
                {
                    IntVec3 drop = nearCell.InBounds(map) ? nearCell : turret.Position;
                    GenPlace.TryPlaceThing(ammo, drop, map, ThingPlaceMode.Near);
                }
            }
            catch (Exception ex)
            {
                WDVerbose.MsgNoTick($"MG nest CE ammo soft-fail: {ex.Message}");
            }
        }

        private static ThingDef ResolveCeMgAmmoDef(Building_Turret turret)
        {
            ThingDef fromTurret = TryAmmoFromTurretComp(turret);
            if (fromTurret != null) return fromTurret;

            ThingDef fmj = DefDatabase<ThingDef>.GetNamedSilentFail(CeAmmoFmJ);
            if (fmj != null) return fmj;

            return TryFirstAmmoFromSet(CeAmmoSet762);
        }

        private static ThingDef TryAmmoFromTurretComp(Building_Turret turret)
        {
            ThingDef fromBuilding = TryAmmoFromThingComps(turret);
            if (fromBuilding != null) return fromBuilding;
            return TryAmmoFromThingComps(TryGetTurretGunThing(turret));
        }

        private static ThingWithComps TryGetTurretGunThing(Building_Turret turret)
        {
            if (turret == null) return null;
            if (turret is Building_TurretGun vanillaGun)
                return vanillaGun.gun as ThingWithComps;

            // CE Building_TurretGunCE exposes Gun / gun without inheriting Building_TurretGun.
            PropertyInfo prop = turret.GetType().GetProperty("Gun", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop?.GetValue(turret) is ThingWithComps fromProp)
                return fromProp;
            FieldInfo field = turret.GetType().GetField("gun", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return field?.GetValue(turret) as ThingWithComps;
        }

        private static ThingDef TryAmmoFromThingComps(ThingWithComps twc)
        {
            if (twc?.AllComps == null) return null;
            for (int i = 0; i < twc.AllComps.Count; i++)
            {
                ThingComp comp = twc.AllComps[i];
                if (comp == null || comp.GetType().Name != "CompAmmoUser") continue;

                object props = comp.props;
                object ammoSet = props != null
                    ? props.GetType().GetField("ammoSet", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(props)
                    : null;
                if (ammoSet == null)
                {
                    PropertyInfo setProp = comp.GetType().GetProperty("AmmoSet", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    ammoSet = setProp?.GetValue(comp);
                }
                if (ammoSet == null) continue;

                string setName = ammoSet.GetType().GetField("defName", BindingFlags.Instance | BindingFlags.Public)?.GetValue(ammoSet) as string
                    ?? (ammoSet as Def)?.defName;
                if (!string.IsNullOrEmpty(setName))
                {
                    ThingDef fromSet = TryFirstAmmoFromSet(setName);
                    if (fromSet != null) return fromSet;
                }
            }
            return null;
        }

        private static ThingDef TryFirstAmmoFromSet(string ammoSetDefName)
        {
            if (string.IsNullOrEmpty(ammoSetDefName)) return null;

            Type ammoSetType = GenTypes.GetTypeInAnyAssembly("CombatExtended.AmmoSetDef", "CombatExtended");
            if (ammoSetType == null) return null;

            MethodInfo getNamed = typeof(DefDatabase<>).MakeGenericType(ammoSetType)
                .GetMethod("GetNamedSilentFail", BindingFlags.Public | BindingFlags.Static);
            object set = getNamed?.Invoke(null, new object[] { ammoSetDefName });
            if (set == null) return null;

            object ammoTypes = set.GetType().GetField("ammoTypes", BindingFlags.Instance | BindingFlags.Public)?.GetValue(set);
            if (ammoTypes is IDictionary dict)
            {
                foreach (DictionaryEntry entry in dict)
                {
                    if (entry.Key is ThingDef td) return td;
                    if (entry.Key is string key)
                    {
                        ThingDef named = DefDatabase<ThingDef>.GetNamedSilentFail(key);
                        if (named != null) return named;
                    }
                }
            }

            return null;
        }

        private static string LayoutDefNameForSide(Rot4 side)
        {
            if (side == Rot4.North) return "Generic_MG_1";
            if (side == Rot4.West) return "Generic_MG_2";
            if (side == Rot4.East) return "Generic_MG_3";
            return "Generic_MG_4";
        }

        private static bool TryPickNestRect(Map map, CellRect settlement, Rot4 side, IntVec2 size, out CellRect best)
        {
            best = default;
            int room = AlongRoom(settlement, side, size);
            if (room < 0) return false;

            int step = Math.Max(1, AlongSize(side, size));
            int bestScore = int.MaxValue;
            int bestAbsOffset = int.MaxValue;
            bool found = false;

            int maxK = room / step;
            for (int k = 0; k <= maxK; k++)
            {
                int[] offsets = k == 0 ? new[] { 0 } : new[] { -k * step, k * step };
                for (int o = 0; o < offsets.Length; o++)
                {
                    int offset = offsets[o];
                    if (!TryBuildNestRect(settlement, side, size, offset, out CellRect candidate))
                        continue;

                    CellRect clipped = candidate.ClipInsideMap(map);
                    if (clipped.Width != candidate.Width || clipped.Height != candidate.Height)
                        continue;

                    int score = ScoreCandidate(map, candidate);
                    int absOffset = Math.Abs(offset);
                    if (!found || score < bestScore || (score == bestScore && absOffset < bestAbsOffset))
                    {
                        best = candidate;
                        bestScore = score;
                        bestAbsOffset = absOffset;
                        found = true;
                        if (score == 0 && offset == 0)
                            return true;
                    }
                }

                if (found && bestScore == 0)
                    return true;
            }

            return found;
        }

        private static int AlongRoom(CellRect settlement, Rot4 side, IntVec2 size)
        {
            if (side == Rot4.North || side == Rot4.South)
                return settlement.Width - size.x;
            return settlement.Height - size.z;
        }

        private static int AlongSize(Rot4 side, IntVec2 size) =>
            (side == Rot4.North || side == Rot4.South) ? size.x : size.z;

        /// <summary>
        /// Centered along the side, outer edge <see cref="OuterPushCells"/> past the settlement rim
        /// (entire footprint shifted away from the center). Layout contents unchanged.
        /// </summary>
        private static bool TryBuildNestRect(CellRect settlement, Rot4 side, IntVec2 size, int alongOffset, out CellRect rect)
        {
            rect = default;
            int room = AlongRoom(settlement, side, size);
            if (room < 0) return false;

            int centerAlong = room / 2;
            int along = centerAlong + alongOffset;
            if (along < 0 || along > room) return false;

            int minX;
            int minZ;
            if (side == Rot4.North)
            {
                minX = settlement.minX + along;
                minZ = settlement.maxZ + OuterPushCells - size.z + 1;
            }
            else if (side == Rot4.South)
            {
                minX = settlement.minX + along;
                minZ = settlement.minZ - OuterPushCells;
            }
            else if (side == Rot4.East)
            {
                minX = settlement.maxX + OuterPushCells - size.x + 1;
                minZ = settlement.minZ + along;
            }
            else // West
            {
                minX = settlement.minX - OuterPushCells;
                minZ = settlement.minZ + along;
            }

            rect = new CellRect(minX, minZ, size.x, size.z);
            return rect.Width == size.x && rect.Height == size.z;
        }

        private static int ScoreCandidate(Map map, CellRect rect)
        {
            int score = 0;
            foreach (IntVec3 cell in rect)
            {
                if (!cell.InBounds(map))
                {
                    score += 100;
                    continue;
                }

                TerrainDef terrain = cell.GetTerrain(map);
                if (IsLinkRoadTerrain(terrain))
                    score += 10;

                List<Thing> things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    Thing t = things[i];
                    if (t is Filth || t is Plant) continue;
                    if (t is not Building b) continue;
                    if (IsWipeablePerimeter(b)) continue;
                    score += 5;
                }
            }
            return score;
        }

        private static bool IsLinkRoadTerrain(TerrainDef terrain)
        {
            if (terrain == null) return false;
            string n = terrain.defName;
            return n == "Concrete" || n == "PackedDirt" || n == "AncientAsphaltRoad" || n == "AncientAsphaltPlatform";
        }

        private static bool IsWipeablePerimeter(Building b)
        {
            if (b?.def == null) return true;
            string n = b.def.defName ?? string.Empty;
            if (n.IndexOf("Sandbag", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (n.IndexOf("Barricade", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (n.IndexOf("RazorWire", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (n.IndexOf("Fence", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (n.IndexOf("Conduit", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static string? ResolveSettlementLayoutKey(Map map)
        {
            try
            {
                if (GenOption.settlementLayout != null && !string.IsNullOrEmpty(GenOption.settlementLayout.defName))
                    return GenOption.settlementLayout.defName;
            }
            catch
            {
                // GenOption may be unavailable outside KCSG gen; fall through.
            }

            if (map.Parent is not Settlement settlement) return null;
            var spread = settlement.GetComponent<CompViralSpread>();
            if (spread == null || settlement.Faction?.def == null) return null;

            bool isTribal = settlement.Faction.def.techLevel <= TechLevel.Medieval;
            string techPrefix = isTribal ? "Tribal" : "Generic";
            string tier = spread.tier.ToString();
            string baseType = string.Equals(spread.subType, "Vanguard", System.StringComparison.Ordinal)
                ? "Vanguard"
                : (spread.tier == SettlementTier.T4
                    ? "Citadel"
                    : NpcSettlementSubtypeUtil.LayoutTokenForSubtype(spread.subType));
            if (string.IsNullOrEmpty(baseType)) return null;
            return $"TSA_{techPrefix}_{tier}_{baseType}";
        }

        private static string SettlementLabel(Map map) =>
            map?.Parent?.LabelCap ?? map?.Parent?.def?.defName ?? "unknown-settlement";
    }
}
