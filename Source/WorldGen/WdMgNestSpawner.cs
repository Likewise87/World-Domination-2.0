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
        private const string CePackageId = "CETeam.CombatExtended";
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

            try
            {
                NukeNestFootprint(map, nestRect);
                LayoutUtils.Generate(nestLayout, nestRect, map, faction);
                WDVerbose.MsgNoTick(
                    $"MG nest generated settlement={SettlementLabel(map)} side={side} layout={layoutDefName} rect={nestRect}");
                TrySpawnNestTurret(map, nestRect, side, faction, poolLevel);
                return true;
            }
            catch (Exception ex)
            {
                WDVerbose.MsgNoTick($"MG nest generate error layout={layoutDefName} rect={nestRect}: {ex.Message}");
                return false;
            }
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
        /// Empty pocket cell against the sandbag back wall (fully surrounded on three sides).
        /// Matches the <c>.</c> slot in Generic_MG_1..4 (not the outer mouth cell).
        /// </summary>
        private static IntVec3 ResolveTurretCell(CellRect nestRect, Rot4 side)
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

        private static void TrySpawnNestTurret(Map map, CellRect nestRect, Rot4 side, Faction faction, int poolLevel)
        {
            if (!WdMgTurretResolver.TryPick(poolLevel, out ThingDef turretDef, out bool manned) || turretDef == null)
            {
                WDVerbose.MsgNoTick(
                    $"MG nest turret skip settlement={SettlementLabel(map)} side={side} reason=pool-pick-fail level={poolLevel}");
                return;
            }

            IntVec3 cell = ResolveTurretCell(nestRect, side);
            if (!cell.InBounds(map))
            {
                WDVerbose.MsgNoTick(
                    $"MG nest turret skip settlement={SettlementLabel(map)} side={side} reason=cell-oob cell={cell}");
                return;
            }

            Building_Turret turret = SpawnFactionTurret(map, cell, side, turretDef, faction);
            if (turret == null)
            {
                WDVerbose.MsgNoTick(
                    $"MG nest turret skip settlement={SettlementLabel(map)} side={side} reason=spawn-fail def={turretDef.defName} cell={cell}");
                return;
            }

            WDVerbose.MsgNoTick(
                $"MG nest turret spawned settlement={SettlementLabel(map)} side={side} def={turretDef.defName} manned={manned} cell={cell}");

            if (manned)
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
                : (spread.tier == SettlementTier.T4 ? "Citadel" : spread.subType);
            if (string.IsNullOrEmpty(baseType)) return null;
            return $"TSA_{techPrefix}_{tier}_{baseType}";
        }

        private static string SettlementLabel(Map map) =>
            map?.Parent?.LabelCap ?? map?.Parent?.def?.defName ?? "unknown-settlement";
    }
}
