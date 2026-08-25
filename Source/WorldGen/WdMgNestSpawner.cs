using System;
using System.Collections.Generic;
using System.Linq;
using KCSG;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Places facing-correct Generic_MG layouts on settlement outer borders after KCSG gen.
    /// Counts come from <see cref="WdMgNestSpawnTableDef"/>; placement prefers mid-edge then slides off roads.
    /// Nest sits one cell past the settlement outer rim (away from center). Layout contents unchanged.
    /// Turret symbols are skipped when Combat Extended is inactive (sandbags/wire still spawn).
    /// </summary>
    public static class WdMgNestSpawner
    {
        private const string TableDefName = "TSA_WdMgNestSpawns";
        /// <summary>Place the nest this many cells past the settlement outer rim (away from center).</summary>
        private const int OuterPushCells = 1;

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

            List<Rot4> chosenSides = Sides.InRandomOrder().Take(n).ToList();
            Faction faction = map.ParentFaction;
            int placed = 0;

            for (int i = 0; i < chosenSides.Count; i++)
            {
                Rot4 side = chosenSides[i];
                if (!TryPlaceNest(map, settlementRect, side, faction))
                {
                    WDVerbose.MsgNoTick($"MG nest failed settlement={SettlementLabel(map)} layout={layoutKey} side={side}");
                    continue;
                }
                placed++;
            }

            WDVerbose.MsgNoTick(
                $"MG nests placed settlement={SettlementLabel(map)} layout={layoutKey} requested={n} placed={placed} rect={settlementRect}");
        }

        private static bool TryPlaceNest(Map map, CellRect settlementRect, Rot4 side, Faction faction)
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
                LayoutUtils.Generate(nestLayout, nestRect, map, faction);
                WDVerbose.MsgNoTick(
                    $"MG nest generated settlement={SettlementLabel(map)} side={side} layout={layoutDefName} rect={nestRect}");
                return true;
            }
            catch (Exception ex)
            {
                WDVerbose.MsgNoTick($"MG nest generate error layout={layoutDefName} rect={nestRect}: {ex.Message}");
                return false;
            }
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
            string baseType = spread.tier == SettlementTier.T4 ? "Citadel" : spread.subType;
            if (string.IsNullOrEmpty(baseType)) return null;
            return $"TSA_{techPrefix}_{tier}_{baseType}";
        }

        private static string SettlementLabel(Map map) =>
            map?.Parent?.LabelCap ?? map?.Parent?.def?.defName ?? "unknown-settlement";
    }
}
