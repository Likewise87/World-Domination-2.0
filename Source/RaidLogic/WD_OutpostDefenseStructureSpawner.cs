#nullable disable
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Spawns player-owned defensive structures on manual outpost defense maps from built upgrades.</summary>
    public static class WD_OutpostDefenseStructureSpawner
    {
        private const int InnerClearChebyshevRadius = 6;
        /// <summary>Chebyshev distance from map center for the wall square.</summary>
        private const int WallRingRadius = 28;
        /// <summary>Chebyshev distance from map center for the trap square.</summary>
        private const int TrapRingRadius = 30;
        /// <summary>Gate opening width in cells (centered on the map center axis).</summary>
        private const int GateWidth = 4;
        /// <summary>Chebyshev distance from map center for turret emplacements (inside the wall ring).</summary>
        private const int TurretRingRadius = 25;
        /// <summary>Empty cells between the two turret guards flanking each gate opening.</summary>
        private const int GateGuardTurretCellsBetween = 3;
        /// <summary>Gate-guard turrets sit this many cells inward from the turret ring edge (toward map center).</summary>
        private const int GateGuardTurretInsetFromRing = 3;
        /// <summary>Extra outward trap rows at each gate (Chebyshev radius from center; walls required).</summary>
        private const int GateTrapCountPerRow = 5;
        private static readonly int[] GateTrapRingRadii = { 32, 34 };
        private const int TankTrapRingRadius = 38;
        private const int TankTrapScatterCount = 32;
        private const int MinTurretCount = 4;
        private const int MaxTurretCount = 6;

        private enum WallSide
        {
            North,
            South,
            East,
            West
        }

        private static readonly IntVec3[] EightNeighborOffsets =
        {
            new IntVec3(1, 0, 0),
            new IntVec3(-1, 0, 0),
            new IntVec3(0, 0, 1),
            new IntVec3(0, 0, -1),
            new IntVec3(1, 0, 1),
            new IntVec3(1, 0, -1),
            new IntVec3(-1, 0, 1),
            new IntVec3(-1, 0, -1)
        };

        private const string UpgradeLineWalls = "Line_Walls";
        private const string UpgradeSpikeTraps = "TSA_WD_Upgrade_SpikeTraps";
        private const string UpgradeIeds = "TSA_WD_Upgrade_IEDs";
        private const string UpgradeAutoTurrets = "TSA_WD_Upgrade_AutoTurrets";

        private static ThingDef cachedWall;
        private static ThingDef cachedWallStuffWood;
        private static ThingDef cachedWallStuffStone;
        private static ThingDef cachedTrapSpike;
        private static ThingDef cachedTrapIed;
        private static ThingDef cachedTankTrap;
        private static bool tankTrapLookupDone;
        private static ThingDef cachedMiniTurret;
        private static ThingDef cachedSandbags;
        private static ThingDef cachedTrapStuffWood;
        private static ThingDef cachedTurretStuffSteel;
        private static ThingDef cachedSandbagStuffCloth;

        public static void SpawnDefenses(Map map, WorldObject_WD_Outpost outpost)
        {
            if (map == null || outpost == null) return;

            IntVec3 center = WD_OutpostDefenseMapUtility.GetSettlementCenter(map);
            int wallTier = OutpostUpgradeUtility.GetHighestBuiltLineTier(outpost, UpgradeLineWalls);
            bool hasTraps = outpost.GetUpgradeLevel(UpgradeSpikeTraps) > 0;
            bool hasIeds = outpost.GetUpgradeLevel(UpgradeIeds) > 0;
            bool hasTurrets = outpost.GetUpgradeLevel(UpgradeAutoTurrets) > 0;

            if (wallTier <= 0 && !hasTraps && !hasIeds && !hasTurrets)
                return;

            bool northSouthGates = ChooseGateAxis(map, center, WallRingRadius);
            HashSet<IntVec3> gateCells = BuildGateCells(center, WallRingRadius, northSouthGates);
            HashSet<IntVec3> iedCells = BuildIedSlotCells(center, WallRingRadius, northSouthGates);

            if (wallTier > 0)
            {
                ThingDef wallDef = WallDef();
                ThingDef wallStuff = WallStuffForTier(wallTier);
                if (wallDef == null)
                    Log.Warning("[TSA WD] Outpost defense walls: Wall ThingDef missing.");
                else if (wallStuff == null)
                    Log.Warning("[TSA WD] Outpost defense walls: wall stuff ThingDef missing.");
                else
                    SpawnWallRing(map, center, WallRingRadius, wallDef, wallStuff, gateCells);
            }

            if (hasIeds)
                SpawnIeds(map, iedCells);

            if (hasTraps)
            {
                HashSet<IntVec3> placedTraps = SpawnTrapRing(map, center, TrapRingRadius, iedCells);
                if (wallTier > 0)
                    SpawnGateTrapRows(map, center, northSouthGates, placedTraps, iedCells);
            }

            if (hasTurrets)
                SpawnTurretEmplacements(map, center, northSouthGates, wallTier > 0);

            if (wallTier >= 2)
                SpawnTankTrapsScatter(map, center);
        }

        /// <summary>Cells where defenders may spawn (inside the inner clear zone).</summary>
        public static CellRect GetInnerClearRect(Map map)
        {
            int size = InnerClearChebyshevRadius * 2 + 1;
            return CellRect.CenteredOn(WD_OutpostDefenseMapUtility.GetSettlementCenter(map), size).ClipInsideMap(map);
        }

        public static bool IsInInnerClear(IntVec3 cell, Map map)
        {
            IntVec3 center = WD_OutpostDefenseMapUtility.GetSettlementCenter(map);
            return cell.InBounds(map)
                && Mathf.Max(Mathf.Abs(cell.x - center.x), Mathf.Abs(cell.z - center.z)) <= InnerClearChebyshevRadius;
        }

        private static ThingDef WallDef() =>
            cachedWall ??= ThingDefOf.Wall
                ?? DefDatabase<ThingDef>.GetNamedSilentFail("Wall");

        private static ThingDef WallStuffForTier(int wallTier) =>
            wallTier >= 2 ? WallStuffStoneDef() : WallStuffWoodDef();

        private static ThingDef WallStuffWoodDef() =>
            cachedWallStuffWood ??= ThingDefOf.WoodLog;

        private static ThingDef WallStuffStoneDef() =>
            cachedWallStuffStone ??= ThingDefOf.BlocksGranite;

        private static ThingDef TrapSpikeDef() =>
            cachedTrapSpike ??= DefDatabase<ThingDef>.GetNamedSilentFail("TrapSpike")
                ?? DefDatabase<ThingDef>.GetNamedSilentFail("TrapSpike_WoodLog");

        private static ThingDef TrapIedDef() =>
            cachedTrapIed ??= DefDatabase<ThingDef>.GetNamedSilentFail("TrapIED_HighExplosive");

        private static ThingDef TankTrapDef()
        {
            if (tankTrapLookupDone)
                return cachedTankTrap;
            tankTrapLookupDone = true;
            cachedTankTrap = DefDatabase<ThingDef>.GetNamedSilentFail("VVE_TankTrap");
            return cachedTankTrap;
        }

        private static ThingDef TrapStuffWoodDef() =>
            cachedTrapStuffWood ??= ThingDefOf.WoodLog;

        private static ThingDef MiniTurretDef() =>
            cachedMiniTurret ??= DefDatabase<ThingDef>.GetNamedSilentFail("Turret_MiniTurret");

        private static ThingDef SandbagDef() =>
            cachedSandbags ??= DefDatabase<ThingDef>.GetNamedSilentFail("Sandbags");

        private static ThingDef TurretStuffSteelDef() =>
            cachedTurretStuffSteel ??= ThingDefOf.Steel;

        private static ThingDef SandbagStuffClothDef() =>
            cachedSandbagStuffCloth ??= ThingDefOf.Cloth;

        /// <summary>Four-cell openings on opposite wall edges facing the most walkable map-edge approach.</summary>
        private static bool ChooseGateAxis(Map map, IntVec3 center, int ringRadius)
        {
            float northSouthScore = ScoreGateAxis(map, center, ringRadius, northSouth: true);
            float eastWestScore = ScoreGateAxis(map, center, ringRadius, northSouth: false);
            return northSouthScore >= eastWestScore;
        }

        private static float ScoreGateAxis(Map map, IntVec3 center, int ringRadius, bool northSouth)
        {
            if (northSouth)
            {
                return ScoreWallSideApproach(map, center, ringRadius, WallSide.North)
                    + ScoreWallSideApproach(map, center, ringRadius, WallSide.South);
            }

            return ScoreWallSideApproach(map, center, ringRadius, WallSide.East)
                + ScoreWallSideApproach(map, center, ringRadius, WallSide.West);
        }

        private static float ScoreWallSideApproach(Map map, IntVec3 center, int ringRadius, WallSide side)
        {
            int startOffset = -(GateWidth / 2);
            float worstColumn = float.MaxValue;

            for (int i = 0; i < GateWidth; i++)
            {
                IntVec3 gateCell = GateCellOnSide(center, ringRadius, side, startOffset + i);
                if (!gateCell.InBounds(map))
                {
                    worstColumn = 0f;
                    continue;
                }

                float columnScore = ScoreOutwardPath(map, gateCell, side);
                if (columnScore < worstColumn)
                    worstColumn = columnScore;
            }

            return worstColumn == float.MaxValue ? 0f : worstColumn;
        }

        private static float ScoreOutwardPath(Map map, IntVec3 gateCell, WallSide side)
        {
            IntVec3 step = OutwardStep(side);
            IntVec3 cell = gateCell + step;
            int walkable = 0;
            int blocked = 0;

            while (cell.InBounds(map))
            {
                if (IsMapEdge(cell, map))
                {
                    walkable += 4;
                    break;
                }

                if (!cell.Walkable(map) || cell.Impassable(map))
                {
                    blocked++;
                    if (blocked > 4)
                        break;
                }
                else
                {
                    walkable++;
                }

                cell += step;
            }

            return walkable - blocked * 3;
        }

        private static bool IsMapEdge(IntVec3 cell, Map map) =>
            cell.x <= 0 || cell.x >= map.Size.x - 1 || cell.z <= 0 || cell.z >= map.Size.z - 1;

        private static IntVec3 OutwardStep(WallSide side)
        {
            switch (side)
            {
                case WallSide.North: return new IntVec3(0, 0, 1);
                case WallSide.South: return new IntVec3(0, 0, -1);
                case WallSide.East: return new IntVec3(1, 0, 0);
                default: return new IntVec3(-1, 0, 0);
            }
        }

        private static HashSet<IntVec3> BuildGateCells(IntVec3 center, int ringRadius, bool northSouthGates)
        {
            var gates = new HashSet<IntVec3>();
            if (northSouthGates)
            {
                AddGateOpening(gates, center, ringRadius, WallSide.North);
                AddGateOpening(gates, center, ringRadius, WallSide.South);
            }
            else
            {
                AddGateOpening(gates, center, ringRadius, WallSide.East);
                AddGateOpening(gates, center, ringRadius, WallSide.West);
            }

            return gates;
        }

        private static void AddGateOpening(HashSet<IntVec3> gates, IntVec3 center, int ringRadius, WallSide side)
        {
            int startOffset = -(GateWidth / 2);
            for (int i = 0; i < GateWidth; i++)
                gates.Add(GateCellOnSide(center, ringRadius, side, startOffset + i));
        }

        private static IntVec3 GateCellOnSide(IntVec3 center, int ringRadius, WallSide side, int offset)
        {
            int cx = center.x;
            int cz = center.z;
            switch (side)
            {
                case WallSide.North:
                    return new IntVec3(cx + offset, 0, cz + ringRadius);
                case WallSide.South:
                    return new IntVec3(cx + offset, 0, cz - ringRadius);
                case WallSide.East:
                    return new IntVec3(cx + ringRadius, 0, cz + offset);
                default:
                    return new IntVec3(cx - ringRadius, 0, cz + offset);
            }
        }

        private static void SpawnWallRing(Map map, IntVec3 center, int radius, ThingDef wallDef, ThingDef wallStuff, HashSet<IntVec3> gateCells)
        {
            int cx = center.x;
            int cz = center.z;
            int placed = 0;

            for (int x = cx - radius; x <= cx + radius; x++)
            {
                placed += TryAddWall(map, new IntVec3(x, 0, cz + radius), wallDef, wallStuff, gateCells) ? 1 : 0;
                placed += TryAddWall(map, new IntVec3(x, 0, cz - radius), wallDef, wallStuff, gateCells) ? 1 : 0;
            }

            for (int z = cz - radius + 1; z < cz + radius; z++)
            {
                placed += TryAddWall(map, new IntVec3(cx + radius, 0, z), wallDef, wallStuff, gateCells) ? 1 : 0;
                placed += TryAddWall(map, new IntVec3(cx - radius, 0, z), wallDef, wallStuff, gateCells) ? 1 : 0;
            }

            if (placed == 0)
            {
                Log.Warning($"[TSA WD] Outpost defense walls: 0 walls placed at radius {radius} (def={wallDef.defName}, stuff={wallStuff.defName}).");
            }
        }

        private static bool TryAddWall(Map map, IntVec3 cell, ThingDef wallDef, ThingDef wallStuff, HashSet<IntVec3> gateCells)
        {
            if (!cell.InBounds(map) || gateCells.Contains(cell) || IsInInnerClear(cell, map))
                return false;
            return SpawnPlayerStructure(map, cell, wallDef, Rot4.North, wallStuff) != null;
        }

        private static HashSet<IntVec3> SpawnTrapRing(Map map, IntVec3 center, int radius, HashSet<IntVec3> reservedIedCells)
        {
            var placed = new HashSet<IntVec3>();
            ThingDef trapDef = TrapSpikeDef();
            if (trapDef == null)
            {
                Log.Warning("[TSA WD] Outpost defense traps: spike trap ThingDef missing.");
                return placed;
            }

            ThingDef stuff = TrapStuffWoodDef();
            var perimeter = CollectSquarePerimeterWalk(center, radius);

            for (int i = 0; i < perimeter.Count; i++)
            {
                IntVec3 cell = perimeter[i];
                if (!cell.InBounds(map) || IsInInnerClear(cell, map) || !CanPlaceBuilding(map, cell))
                    continue;
                if (IsIedReserved(cell, reservedIedCells))
                    continue;
                if (AdjacentToAny(cell, placed))
                    continue;

                if (SpawnPlayerTrap(map, cell, trapDef, stuff, Rot4.Random) != null)
                    placed.Add(cell);
            }

            return placed;
        }

        /// <summary>Two outward rows of 5 traps centered on each gate (radii 32 and 34; walls required).</summary>
        private static void SpawnGateTrapRows(
            Map map,
            IntVec3 center,
            bool northSouthGates,
            HashSet<IntVec3> placed,
            HashSet<IntVec3> reservedIedCells)
        {
            ThingDef trapDef = TrapSpikeDef();
            if (trapDef == null || placed == null)
                return;

            ThingDef stuff = TrapStuffWoodDef();
            if (northSouthGates)
            {
                SpawnGateTrapRowsForSide(map, center, WallSide.North, trapDef, stuff, placed, reservedIedCells);
                SpawnGateTrapRowsForSide(map, center, WallSide.South, trapDef, stuff, placed, reservedIedCells);
            }
            else
            {
                SpawnGateTrapRowsForSide(map, center, WallSide.East, trapDef, stuff, placed, reservedIedCells);
                SpawnGateTrapRowsForSide(map, center, WallSide.West, trapDef, stuff, placed, reservedIedCells);
            }
        }

        private static void SpawnGateTrapRowsForSide(
            Map map,
            IntVec3 center,
            WallSide side,
            ThingDef trapDef,
            ThingDef stuff,
            HashSet<IntVec3> placed,
            HashSet<IntVec3> reservedIedCells)
        {
            for (int row = 0; row < GateTrapRingRadii.Length; row++)
            {
                int ringRadius = GateTrapRingRadii[row];
                for (int i = 0; i < GateTrapCountPerRow; i++)
                {
                    int tangentOffset = (i - (GateTrapCountPerRow - 1) / 2) * 2;
                    IntVec3 cell = GateTrapCellOnSide(center, side, ringRadius, tangentOffset);
                    if (!cell.InBounds(map) || IsInInnerClear(cell, map) || !CanPlaceBuilding(map, cell))
                        continue;
                    if (IsIedReserved(cell, reservedIedCells))
                        continue;
                    if (AdjacentToAny(cell, placed))
                        continue;

                    if (SpawnPlayerTrap(map, cell, trapDef, stuff, Rot4.Random) != null)
                        placed.Add(cell);
                }
            }
        }

        private static IntVec3 GateTrapCellOnSide(IntVec3 center, WallSide side, int ringRadius, int tangentOffset)
        {
            int cx = center.x;
            int cz = center.z;

            switch (side)
            {
                case WallSide.North:
                    return new IntVec3(cx + tangentOffset, 0, cz + ringRadius);
                case WallSide.South:
                    return new IntVec3(cx + tangentOffset, 0, cz - ringRadius);
                case WallSide.East:
                    return new IntVec3(cx + ringRadius, 0, cz + tangentOffset);
                default:
                    return new IntVec3(cx - ringRadius, 0, cz + tangentOffset);
            }
        }

        /// <summary>Perimeter cells in clockwise order (full 360° around the square).</summary>
        private static List<IntVec3> CollectSquarePerimeterWalk(IntVec3 center, int radius)
        {
            var cells = new List<IntVec3>();
            int cx = center.x;
            int cz = center.z;

            for (int x = cx - radius; x <= cx + radius; x++)
                cells.Add(new IntVec3(x, 0, cz + radius));

            for (int z = cz + radius - 1; z >= cz - radius; z--)
                cells.Add(new IntVec3(cx + radius, 0, z));

            for (int x = cx + radius - 1; x >= cx - radius; x--)
                cells.Add(new IntVec3(x, 0, cz - radius));

            for (int z = cz - radius + 1; z <= cz + radius - 1; z++)
                cells.Add(new IntVec3(cx - radius, 0, z));

            return cells;
        }

        private static bool AdjacentToAny(IntVec3 cell, HashSet<IntVec3> placed)
        {
            for (int i = 0; i < EightNeighborOffsets.Length; i++)
            {
                if (placed.Contains(cell + EightNeighborOffsets[i]))
                    return true;
            }
            return false;
        }

        private static bool IsIedReserved(IntVec3 cell, HashSet<IntVec3> reservedIedCells)
        {
            if (reservedIedCells == null || reservedIedCells.Count == 0)
                return false;
            if (reservedIedCells.Contains(cell))
                return true;
            return AdjacentToAny(cell, reservedIedCells);
        }

        /// <summary>Eight slots: four outer corners (trap-ring radius, so spikes yield) + center pair of each gate on the wall ring.</summary>
        private static HashSet<IntVec3> BuildIedSlotCells(IntVec3 center, int wallRadius, bool northSouthGates)
        {
            var cells = new HashSet<IntVec3>();
            int cx = center.x;
            int cz = center.z;
            int cornerR = TrapRingRadius;
            cells.Add(new IntVec3(cx - cornerR, 0, cz - cornerR));
            cells.Add(new IntVec3(cx + cornerR, 0, cz - cornerR));
            cells.Add(new IntVec3(cx - cornerR, 0, cz + cornerR));
            cells.Add(new IntVec3(cx + cornerR, 0, cz + cornerR));

            if (northSouthGates)
            {
                cells.Add(GateCellOnSide(center, wallRadius, WallSide.North, -1));
                cells.Add(GateCellOnSide(center, wallRadius, WallSide.North, 0));
                cells.Add(GateCellOnSide(center, wallRadius, WallSide.South, -1));
                cells.Add(GateCellOnSide(center, wallRadius, WallSide.South, 0));
            }
            else
            {
                cells.Add(GateCellOnSide(center, wallRadius, WallSide.East, -1));
                cells.Add(GateCellOnSide(center, wallRadius, WallSide.East, 0));
                cells.Add(GateCellOnSide(center, wallRadius, WallSide.West, -1));
                cells.Add(GateCellOnSide(center, wallRadius, WallSide.West, 0));
            }

            return cells;
        }

        private static void SpawnIeds(Map map, HashSet<IntVec3> iedCells)
        {
            ThingDef iedDef = TrapIedDef();
            if (iedDef == null)
            {
                Log.Warning("[TSA WD] Outpost defense IEDs: TrapIED_HighExplosive ThingDef missing.");
                return;
            }

            foreach (IntVec3 cell in iedCells)
            {
                if (!cell.InBounds(map) || IsInInnerClear(cell, map))
                    continue;

                Building existing = cell.GetFirstBuilding(map);
                if (existing != null)
                {
                    // IED wins contested trap slots; never carve holes in walls for IEDs.
                    if (existing.def?.building != null && existing.def.building.isTrap)
                        existing.Destroy(DestroyMode.Vanish);
                    else
                        continue;
                }

                if (!CanPlaceBuilding(map, cell))
                    continue;

                SpawnPlayerTrap(map, cell, iedDef, null, Rot4.Random);
            }
        }

        /// <summary>Soft-spawn VVE tank traps outside the outer trap ring when stone walls are present. No-op if def missing.</summary>
        private static void SpawnTankTrapsScatter(Map map, IntVec3 center)
        {
            ThingDef tankDef = TankTrapDef();
            if (tankDef == null)
                return;

            ThingDef stuff = tankDef.MadeFromStuff ? TurretStuffSteelDef() : null;
            var perimeter = CollectSquarePerimeterWalk(center, TankTrapRingRadius);
            if (perimeter.Count == 0)
                return;

            var occupied = new HashSet<IntVec3>();
            int placed = 0;
            int attempts = 0;
            int maxAttempts = TankTrapScatterCount * 24;

            while (placed < TankTrapScatterCount && attempts < maxAttempts)
            {
                attempts++;
                IntVec3 anchor = perimeter[Rand.Range(0, perimeter.Count)];
                int jitter = Rand.RangeInclusive(-2, 2);
                // Tangential nudge along the side the cell sits on.
                if (anchor.z == center.z + TankTrapRingRadius || anchor.z == center.z - TankTrapRingRadius)
                    anchor = new IntVec3(anchor.x + jitter, 0, anchor.z);
                else
                    anchor = new IntVec3(anchor.x, 0, anchor.z + jitter);

                Rot4 rot = Rot4.Random;
                if (!CanPlaceMultiCell(map, tankDef, anchor, rot, occupied))
                    continue;

                if (SpawnPlayerStructureMulti(map, anchor, tankDef, rot, stuff) == null)
                    continue;

                CellRect rect = GenAdj.OccupiedRect(anchor, rot, tankDef.size);
                foreach (IntVec3 c in rect)
                    occupied.Add(c);
                placed++;
            }
        }

        private static bool CanPlaceMultiCell(Map map, ThingDef def, IntVec3 anchor, Rot4 rot, HashSet<IntVec3> occupied)
        {
            if (def == null || !anchor.InBounds(map))
                return false;

            CellRect rect = GenAdj.OccupiedRect(anchor, rot, def.size);
            foreach (IntVec3 cell in rect)
            {
                if (!cell.InBounds(map) || IsInInnerClear(cell, map) || !CanPlaceBuilding(map, cell))
                    return false;
                if (occupied != null && occupied.Contains(cell))
                    return false;
            }
            return true;
        }

        private static Thing SpawnPlayerStructureMulti(Map map, IntVec3 cell, ThingDef def, Rot4 rot, ThingDef stuff = null)
        {
            if (def == null || !CanPlaceMultiCell(map, def, cell, rot, null))
                return null;

            CellRect rect = GenAdj.OccupiedRect(cell, rot, def.size);
            foreach (IntVec3 c in rect)
                PrepareBuildingCell(map, c);

            Thing thing;
            try
            {
                if (def.MadeFromStuff && stuff != null)
                    thing = ThingMaker.MakeThing(def, stuff);
                else
                    thing = ThingMaker.MakeThing(def);
            }
            catch
            {
                return null;
            }

            if (thing == null) return null;

            try
            {
                thing.SetFactionDirect(Faction.OfPlayer);
                GenSpawn.Spawn(thing, cell, map, rot, WipeMode.Vanish);
                thing.SetFaction(Faction.OfPlayer);
                return thing;
            }
            catch
            {
                return null;
            }
        }

        private static void SpawnTurretEmplacements(Map map, IntVec3 center, bool northSouthGates, bool hasWalls)
        {
            ThingDef turretDef = MiniTurretDef();
            ThingDef sandbagDef = SandbagDef();
            if (turretDef == null || sandbagDef == null)
            {
                if (turretDef == null)
                    Log.Warning("[TSA WD] Outpost defense turrets: mini-turret ThingDef missing.");
                if (sandbagDef == null)
                    Log.Warning("[TSA WD] Outpost defense turrets: sandbag ThingDef missing.");
                return;
            }

            ThingDef turretStuff = TurretStuffSteelDef();
            ThingDef sandbagStuff = SandbagStuffClothDef();
            int targetCount = Rand.RangeInclusive(MinTurretCount, MaxTurretCount);
            List<IntVec3> slots = hasWalls
                ? BuildTurretSlotsWithWalls(center, TurretRingRadius, targetCount, northSouthGates)
                : BuildTurretSlotsWithoutWalls(center, TurretRingRadius, targetCount, northSouthGates);

            for (int i = 0; i < slots.Count; i++)
            {
                IntVec3 cell = slots[i];
                if (!CanPlaceTurretPad(map, cell))
                    continue;
                SpawnTurretEmplacement(map, cell, center, turretDef, turretStuff, sandbagDef, sandbagStuff);
            }
        }

        /// <summary>Two turrets per gate plus any extras on random corners.</summary>
        private static List<IntVec3> BuildTurretSlotsWithWalls(IntVec3 center, int radius, int count, bool northSouthGates)
        {
            var slots = BuildGateGuardTurretSlots(center, radius, northSouthGates);
            int extras = count - slots.Count;
            if (extras <= 0)
                return slots;

            slots.AddRange(PickRandomCornerSlots(center, radius, extras, slots));
            return slots;
        }

        /// <summary>Two turret guards per gate, inset toward center with a clear lane through the opening.</summary>
        private static List<IntVec3> BuildGateGuardTurretSlots(IntVec3 center, int radius, bool northSouthGates)
        {
            int cx = center.x;
            int cz = center.z;
            int depth = radius - GateGuardTurretInsetFromRing;
            int spread = GateGuardTurretHalfSpread();

            var slots = new List<IntVec3>(4);
            if (northSouthGates)
            {
                slots.Add(new IntVec3(cx - spread, 0, cz + depth));
                slots.Add(new IntVec3(cx + spread, 0, cz + depth));
                slots.Add(new IntVec3(cx - spread, 0, cz - depth));
                slots.Add(new IntVec3(cx + spread, 0, cz - depth));
            }
            else
            {
                slots.Add(new IntVec3(cx + depth, 0, cz - spread));
                slots.Add(new IntVec3(cx + depth, 0, cz + spread));
                slots.Add(new IntVec3(cx - depth, 0, cz - spread));
                slots.Add(new IntVec3(cx - depth, 0, cz + spread));
            }

            return slots;
        }

        private static int GateGuardTurretHalfSpread() =>
            (GateGuardTurretCellsBetween + 1) / 2;

        private static List<IntVec3> PickRandomCornerSlots(IntVec3 center, int radius, int count, List<IntVec3> exclude)
        {
            int cx = center.x;
            int cz = center.z;
            int d = radius;
            var corners = new List<IntVec3>
            {
                new IntVec3(cx + d, 0, cz + d),
                new IntVec3(cx + d, 0, cz - d),
                new IntVec3(cx - d, 0, cz + d),
                new IntVec3(cx - d, 0, cz - d)
            };

            corners.Shuffle();
            var picked = new List<IntVec3>(count);
            for (int i = 0; i < corners.Count && picked.Count < count; i++)
            {
                if (exclude != null && exclude.Contains(corners[i]))
                    continue;
                picked.Add(corners[i]);
            }

            return picked;
        }

        /// <summary>4 turrets on square corners; extras on side midpoints perpendicular to the gate axis.</summary>
        private static List<IntVec3> BuildTurretSlotsWithoutWalls(IntVec3 center, int radius, int count, bool northSouthGates)
        {
            int cx = center.x;
            int cz = center.z;
            int d = radius;
            var slots = new List<IntVec3>(count)
            {
                new IntVec3(cx + d, 0, cz + d),
                new IntVec3(cx + d, 0, cz - d),
                new IntVec3(cx - d, 0, cz + d),
                new IntVec3(cx - d, 0, cz - d)
            };

            if (count <= 4)
                return slots;

            // Extra turrets sit on closed sides (perpendicular to gates), centered on each edge.
            IntVec3 sideA;
            IntVec3 sideB;
            if (northSouthGates)
            {
                sideA = new IntVec3(cx + d, 0, cz);
                sideB = new IntVec3(cx - d, 0, cz);
            }
            else
            {
                sideA = new IntVec3(cx, 0, cz + d);
                sideB = new IntVec3(cx, 0, cz - d);
            }

            slots.Add(sideA);
            if (count >= 6)
                slots.Add(sideB);

            return slots;
        }

        private static bool CanPlaceTurretPad(Map map, IntVec3 centerCell)
        {
            if (!centerCell.InBounds(map) || IsInInnerClear(centerCell, map) || !CanPlaceBuilding(map, centerCell))
                return false;

            for (int i = 0; i < EightNeighborOffsets.Length; i++)
            {
                IntVec3 neighbor = centerCell + EightNeighborOffsets[i];
                if (!neighbor.InBounds(map) || IsInInnerClear(neighbor, map) || !CanPlaceBuilding(map, neighbor))
                    return false;
            }

            return true;
        }

        private static void SpawnTurretEmplacement(
            Map map,
            IntVec3 centerCell,
            IntVec3 mapCenter,
            ThingDef turretDef,
            ThingDef turretStuff,
            ThingDef sandbagDef,
            ThingDef sandbagStuff)
        {
            for (int i = 0; i < EightNeighborOffsets.Length; i++)
            {
                IntVec3 sandbagCell = centerCell + EightNeighborOffsets[i];
                SpawnPlayerStructure(map, sandbagCell, sandbagDef, Rot4.North, sandbagStuff);
            }

            Thing turret = SpawnPlayerStructure(map, centerCell, turretDef, FacingOutward(centerCell, mapCenter), turretStuff);
            FinalizeTurret(turret);
        }

        private static Rot4 FacingOutward(IntVec3 cell, IntVec3 center)
        {
            int dx = cell.x - center.x;
            int dz = cell.z - center.z;
            if (Mathf.Abs(dx) >= Mathf.Abs(dz))
                return dx > 0 ? Rot4.East : Rot4.West;
            return dz > 0 ? Rot4.North : Rot4.South;
        }

        private static bool CanPlaceBuilding(Map map, IntVec3 cell)
        {
            if (!cell.InBounds(map) || cell.Fogged(map))
                return false;
            if (cell.GetFirstBuilding(map) != null)
                return false;
            return cell.Walkable(map) || cell.GetEdifice(map) == null;
        }

        private static void PrepareBuildingCell(Map map, IntVec3 cell)
        {
            if (!cell.InBounds(map)) return;
            map.roofGrid.SetRoof(cell, null);
            List<Thing> things = cell.GetThingList(map);
            for (int i = things.Count - 1; i >= 0; i--)
            {
                Thing t = things[i];
                if (t == null || t.def == null) continue;
                if (t.def.category == ThingCategory.Plant)
                    t.Destroy(DestroyMode.Vanish);
            }
        }

        private static Thing SpawnPlayerStructure(Map map, IntVec3 cell, ThingDef def, Rot4 rot, ThingDef stuff = null)
        {
            if (def == null || !CanPlaceBuilding(map, cell))
                return null;

            PrepareBuildingCell(map, cell);

            Thing thing;
            try
            {
                if (def.MadeFromStuff && stuff != null)
                    thing = ThingMaker.MakeThing(def, stuff);
                else
                    thing = ThingMaker.MakeThing(def);
            }
            catch
            {
                return null;
            }

            if (thing == null) return null;

            try
            {
                thing.SetFactionDirect(Faction.OfPlayer);
                GenSpawn.Spawn(thing, cell, map, rot, WipeMode.Vanish);
                thing.SetFaction(Faction.OfPlayer);
                return thing;
            }
            catch
            {
                return null;
            }
        }

        private static Thing SpawnPlayerTrap(Map map, IntVec3 cell, ThingDef trapDef, ThingDef stuff, Rot4 rot)
        {
            if (trapDef == null || !CanPlaceBuilding(map, cell))
                return null;

            PrepareBuildingCell(map, cell);

            Thing thing;
            try
            {
                if (trapDef.MadeFromStuff && stuff != null)
                    thing = ThingMaker.MakeThing(trapDef, stuff);
                else
                    thing = ThingMaker.MakeThing(trapDef);
            }
            catch
            {
                return null;
            }

            if (thing == null) return null;

            try
            {
                thing.SetFactionDirect(Faction.OfPlayer);
                GenSpawn.Spawn(thing, cell, map, rot, WipeMode.Vanish);
                thing.SetFaction(Faction.OfPlayer);
                return thing;
            }
            catch
            {
                return null;
            }
        }

        private static void FinalizeTurret(Thing turret)
        {
            if (turret == null || turret.Destroyed) return;

            turret.SetFaction(Faction.OfPlayer);

            CompPowerTrader power = turret.TryGetComp<CompPowerTrader>();
            if (power != null)
                power.PowerOn = true;

            CompRefuelable refuel = turret.TryGetComp<CompRefuelable>();
            if (refuel != null && refuel.Fuel <= 0f)
                refuel.Refuel(refuel.Props.fuelCapacity);
        }
    }
}
