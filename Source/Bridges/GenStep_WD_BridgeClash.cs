using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Ocean map with an 8-cell-wide granite bridge. Deck axis uses the same
    /// tangent-plane world→local mapping as mortar ingress (north/east on the globe),
    /// snapped to the nearest 22.5°. Spawn ends follow each caravan's world
    /// approach tile when known. Centerline may drift ±1, every 40–70 cells.
    /// </summary>
    public class GenStep_WD_BridgeClash : GenStep
    {
        public const int BridgeWidth = 8;
        public const float AngleSnapDegrees = 22.5f;
        private const float MostlyAlignedChance = 0.92f;
        private const float MirrorNoiseChance = 0.04f;
        private const float SegmentGapChance = 0.14f;
        private const float BarricadeSegmentChance = 0.42f;
        private const int GraniteBarricadeMin = 3;
        private const int GraniteBarricadeMax = 10;
        /// <summary>Chance the centerline steps ±1 after the min spacing (stays within ±1; no corners).</summary>
        private const float LateralStepChance = 0.07f;
        /// <summary>Cells of straight deck required between lateral tilts (rolled per gap).</summary>
        private const int LateralStepMinSpacing = 40;
        private const int LateralStepMaxSpacing = 70;
        /// <summary>Spawn inset along the centerline from each map end.</summary>
        private const int SpawnInsetCells = 8;

        private const int SegGap = 0;
        private const int SegShort = -1;
        private const int SegLong = -2;

        private static int pendingPlayerFromTile = -1;
        private static int pendingHostileFromTile = -1;

        private static readonly FieldInfo CaravanPreviousTileField =
            typeof(Caravan_PathFollower).GetField(
                "previousTileForDrawingIfInDoubt",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly PropertyInfo CaravanPreviousTileProp =
            typeof(Caravan_PathFollower).GetProperty(
                "previousTileForDrawingIfInDoubt",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        public override int SeedPart => 874239117;

        public static void PushEncounterApproaches(int playerFromTile, int hostileFromTile)
        {
            pendingPlayerFromTile = playerFromTile;
            pendingHostileFromTile = hostileFromTile;
        }

        public static void ClearEncounterApproaches()
        {
            pendingPlayerFromTile = -1;
            pendingHostileFromTile = -1;
        }

        public override void Generate(Map map, GenStepParams parms)
        {
            if (map == null) return;

            TerrainDef water = TerrainDefOf.WaterOceanDeep ?? TerrainDefOf.WaterDeep;
            TerrainDef granite = ResolveGraniteFloor();

            float angleDeg = ResolveAngleFromWorldTile(map.Tile, out float rawAngle);
            GetAxisVectors(angleDeg, out Vector2 along, out Vector2 perp);

            int steps = BridgeStepCount(map);
            int[] lateral = BuildLateralOffsets(steps);

            var layout = map.GetComponent<MapComponent_WdBridgeClashLayout>();
            if (layout == null)
            {
                layout = new MapComponent_WdBridgeClashLayout(map);
                map.components.Add(layout);
            }

            layout.AngleDegrees = angleDeg;
            layout.BridgeCells = new HashSet<IntVec3>(map.Size.x * BridgeWidth);

            foreach (IntVec3 c in map.AllCells)
                map.terrainGrid.SetTerrain(c, water);

            Vector2 center = new Vector2(map.Center.x + 0.5f, map.Center.z + 0.5f);
            float halfSteps = (steps - 1) * 0.5f;
            // Inclusive half-width in cell-center space (~BridgeWidth cells solid, no float gaps).
            float halfW = BridgeWidth * 0.5f;

            PaintSolidDeck(map, layout, granite, center, along, perp, lateral, halfSteps, halfW);

            var centerline = new IntVec3[steps];
            BuildCenterline(map, layout, center, along, perp, lateral, halfSteps, centerline);
            ResolveSpawnEnds(map, layout, centerline);
            Log.Message(
                $"[TSA WD] Identified Angle from world map {angleDeg:0.#}° (raw {rawAngle:0.#}°). " +
                $"Player spawns {CompassLabel(map, layout.PlayerEnd)}. Enemy spawns {CompassLabel(map, layout.HostileEnd)}.");

            CollectLongSideEdges(layout, center, along, perp, lateral, halfSteps,
                out List<IntVec3> edgeLo, out List<IntVec3> edgeHi);
            PlaceBridgeEdgeBarriers(map, layout, edgeLo, edgeHi, perp, center, along, lateral, halfSteps);

            MapGenerator.PlayerStartSpot = layout.PlayerEnd.IsValid
                ? layout.PlayerEnd
                : WestSpawnCell(map);
        }

        public static bool IsOnBridge(Map map, IntVec3 c)
        {
            if (map == null || !c.InBounds(map)) return false;
            var layout = map.GetComponent<MapComponent_WdBridgeClashLayout>();
            if (layout != null && layout.BridgeCells != null && layout.BridgeCells.Count > 0)
                return layout.IsOnBridge(c);

            GetBridgeZRange(map, out int z0, out int z1);
            return c.z >= z0 && c.z <= z1;
        }

        public static IntVec3 WestSpawnCell(Map map) => PlayerEndSpawnCell(map);

        public static IntVec3 EastSpawnCell(Map map) => HostileEndSpawnCell(map);

        public static IntVec3 PlayerEndSpawnCell(Map map)
        {
            var layout = map?.GetComponent<MapComponent_WdBridgeClashLayout>();
            if (layout != null && layout.PlayerEnd.IsValid)
                return layout.PlayerEnd;
            GetBridgeZRange(map, out int z0, out int z1);
            int z = (z0 + z1) / 2;
            IntVec3 cell = new IntVec3(4, 0, z);
            if (map != null && !cell.InBounds(map)) cell = new IntVec3(1, 0, z);
            return cell;
        }

        public static IntVec3 HostileEndSpawnCell(Map map)
        {
            var layout = map?.GetComponent<MapComponent_WdBridgeClashLayout>();
            if (layout != null && layout.HostileEnd.IsValid)
                return layout.HostileEnd;
            GetBridgeZRange(map, out int z0, out int z1);
            int z = (z0 + z1) / 2;
            IntVec3 cell = new IntVec3(map.Size.x - 5, 0, z);
            if (!cell.InBounds(map)) cell = new IntVec3(map.Size.x - 2, 0, z);
            return cell;
        }

        public static bool TryFindStandableNear(Map map, IntVec3 root, out IntVec3 cell)
        {
            cell = root;
            if (map == null) return false;
            if (root.InBounds(map) && root.Standable(map) && IsOnBridge(map, root))
                return true;

            var layout = map.GetComponent<MapComponent_WdBridgeClashLayout>();
            if (layout?.BridgeCells != null && layout.BridgeCells.Count > 0)
            {
                IntVec3 best = IntVec3.Invalid;
                int bestDist = int.MaxValue;
                foreach (IntVec3 c in layout.BridgeCells)
                {
                    if (!c.Standable(map)) continue;
                    int d = c.DistanceToSquared(root);
                    if (d >= bestDist) continue;
                    bestDist = d;
                    best = c;
                }
                if (best.IsValid)
                {
                    cell = best;
                    return true;
                }
                return false;
            }

            GetBridgeZRange(map, out int z0, out int z1);
            for (int r = 0; r < 8; r++)
            {
                for (int dz = -r; dz <= r; dz++)
                {
                    IntVec3 c = new IntVec3(root.x + r, 0, root.z + dz);
                    if (c.z < z0 || c.z > z1) continue;
                    if (c.InBounds(map) && c.Standable(map))
                    {
                        cell = c;
                        return true;
                    }
                    c = new IntVec3(root.x - r, 0, root.z + dz);
                    if (c.z < z0 || c.z > z1) continue;
                    if (c.InBounds(map) && c.Standable(map))
                    {
                        cell = c;
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>Legacy centered E–W band (fallback).</summary>
        public static void GetBridgeZRange(Map map, out int z0, out int z1)
        {
            int midZ = map.Center.z;
            int half = BridgeWidth / 2;
            z0 = midZ - half;
            z1 = midZ + half - 1;
        }

        /// <summary>Undirected local-map axis in [0, 180) (0 = east, 90 = north).</summary>
        public static float ResolveAngleFromWorldTile(PlanetTile tile) =>
            ResolveAngleFromWorldTile(tile, out _);

        public static float ResolveAngleFromWorldTile(PlanetTile tile, out float rawUndirected)
        {
            rawUndirected = 0f;
            if (!tile.Valid || Find.WorldGrid == null)
                return 0f;

            if (!TryGetBridgeLocalAxis(tile, out float lx, out float lz))
                return 0f;

            if (Mathf.Abs(lx) < 0.01f && Mathf.Abs(lz) < 0.01f)
                return 0f;

            float a = Mathf.Atan2(lz, lx) * Mathf.Rad2Deg;
            if (a < 0f) a += 360f;
            if (a >= 180f) a -= 180f;
            rawUndirected = a;
            return SnapAngle(a);
        }

        /// <summary>Nearest undirected multiple of 22.5° in [0, 180).</summary>
        public static float SnapAngle(float degreesUndirected)
        {
            float a = degreesUndirected % 180f;
            if (a < 0f) a += 180f;
            float snapped = Mathf.Round(a / AngleSnapDegrees) * AngleSnapDegrees;
            if (snapped >= 180f) snapped = 0f;
            return snapped;
        }

        /// <summary>
        /// True for E–W / N–S decks (0°/90°). 2×1 barriers only spawn on these.
        /// </summary>
        public static bool IsCardinalDeckAngle(float angleDeg)
        {
            float a = angleDeg % 90f;
            if (a < 0f) a += 90f;
            return a < 1f || a > 89f;
        }

        /// <summary>
        /// Through-road axis on the same tangent plane as mortar / Force Raid Direction
        /// (local +X = map east, +Z = map north).
        /// </summary>
        private static bool TryGetBridgeLocalAxis(PlanetTile tile, out float localX, out float localZ)
        {
            localX = 1f;
            localZ = 0f;
            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return false;

            var neighbors = new List<PlanetTile>(8);
            grid.GetTileNeighbors(tile, neighbors);
            var linked = new List<PlanetTile>(4);
            for (int i = 0; i < neighbors.Count; i++)
            {
                PlanetTile n = neighbors[i];
                if (grid.GetRoadDef(tile, n, visibleOnly: false) == null) continue;
                linked.Add(n);
            }
            if (linked.Count == 0) return false;

            if (linked.Count == 1)
                return AssaultArtilleryIngressDirection.TryWorldToLocalMapAxes(
                    tile.tileId, linked[0].tileId, out localX, out localZ);

            float bestOpp = -2f;
            bool any = false;
            float bestX = 1f;
            float bestZ = 0f;
            for (int i = 0; i < linked.Count; i++)
            {
                if (!AssaultArtilleryIngressDirection.TryWorldToLocalMapAxes(
                        tile.tileId, linked[i].tileId, out float xi, out float zi))
                    continue;
                for (int j = i + 1; j < linked.Count; j++)
                {
                    if (!AssaultArtilleryIngressDirection.TryWorldToLocalMapAxes(
                            tile.tileId, linked[j].tileId, out float xj, out float zj))
                        continue;
                    float opp = -(xi * xj + zi * zj);
                    if (opp <= bestOpp) continue;
                    bestOpp = opp;
                    bestX = xi - xj;
                    bestZ = zi - zj;
                    any = true;
                }
            }
            if (!any) return false;
            localX = bestX;
            localZ = bestZ;
            return true;
        }

        private static void GetAxisVectors(float angleDeg, out Vector2 along, out Vector2 perp)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            along = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            if (along.sqrMagnitude < 1e-6f) along = Vector2.right;
            along.Normalize();
            // Prefer +X as the "forward" end for player/hostile polarity when nearly E–W.
            if (along.x < -0.01f || (Mathf.Abs(along.x) < 0.01f && along.y < 0f))
                along = -along;
            perp = new Vector2(-along.y, along.x);
        }

        private static int BridgeStepCount(Map map)
        {
            // Long enough to cover map corners at any snap angle.
            return Mathf.CeilToInt(Mathf.Sqrt(map.Size.x * map.Size.x + map.Size.z * map.Size.z)) + BridgeWidth;
        }

        private static int[] BuildLateralOffsets(int length)
        {
            var offsets = new int[Mathf.Max(1, length)];
            int cur = 0;
            int nextAllowedAt = Rand.RangeInclusive(LateralStepMinSpacing, LateralStepMaxSpacing);
            for (int i = 0; i < offsets.Length; i++)
            {
                offsets[i] = cur;
                if (i < nextAllowedAt) continue;
                if (!Rand.Chance(LateralStepChance)) continue;
                int next = cur + (Rand.Bool ? 1 : -1);
                if (next < -1 || next > 1) continue;
                cur = next;
                nextAllowedAt = i + Rand.RangeInclusive(LateralStepMinSpacing, LateralStepMaxSpacing);
            }
            return offsets;
        }

        /// <summary>
        /// Paint every cell within half-width of the (possibly tilted) centerline.
        /// Distance fill avoids 1×1 ocean holes from float→cell sampling on shallow angles.
        /// </summary>
        private static void PaintSolidDeck(
            Map map,
            MapComponent_WdBridgeClashLayout layout,
            TerrainDef granite,
            Vector2 center,
            Vector2 along,
            Vector2 perp,
            int[] lateral,
            float halfSteps,
            float halfW)
        {
            float tMin = -halfSteps - 1f;
            float tMax = halfSteps + 1f;
            foreach (IntVec3 c in map.AllCells)
            {
                Vector2 p = new Vector2(c.x + 0.5f, c.z + 0.5f);
                float t = Vector2.Dot(p - center, along);
                if (t < tMin || t > tMax) continue;

                float lat = SampleLateral(lateral, t, halfSteps);
                Vector2 mid = center + along * t + perp * lat;
                float perpDist = Vector2.Dot(p - mid, perp);
                if (Mathf.Abs(perpDist) > halfW) continue;

                map.terrainGrid.SetTerrain(c, granite);
                layout.BridgeCells.Add(c);
            }

            // Safety: close any rare 4-connected pinholes still left after tilts.
            FillBridgePinholes(map, layout, granite);
        }

        private static float SampleLateral(int[] lateral, float t, float halfSteps)
        {
            if (lateral == null || lateral.Length == 0) return 0f;
            float fi = t + halfSteps;
            int i0 = Mathf.Clamp(Mathf.FloorToInt(fi), 0, lateral.Length - 1);
            int i1 = Mathf.Clamp(Mathf.CeilToInt(fi), 0, lateral.Length - 1);
            if (i0 == i1) return lateral[i0];
            float frac = Mathf.Clamp01(fi - i0);
            return Mathf.Lerp(lateral[i0], lateral[i1], frac);
        }

        private static void FillBridgePinholes(
            Map map, MapComponent_WdBridgeClashLayout layout, TerrainDef granite)
        {
            // Two passes: fill cells with ≥3 orthogonal bridge neighbors.
            for (int pass = 0; pass < 2; pass++)
            {
                var fill = new List<IntVec3>(32);
                foreach (IntVec3 c in map.AllCells)
                {
                    if (layout.IsOnBridge(c)) continue;
                    int n = 0;
                    if (layout.IsOnBridge(c + IntVec3.North)) n++;
                    if (layout.IsOnBridge(c + IntVec3.South)) n++;
                    if (layout.IsOnBridge(c + IntVec3.East)) n++;
                    if (layout.IsOnBridge(c + IntVec3.West)) n++;
                    if (n >= 3) fill.Add(c);
                }
                for (int i = 0; i < fill.Count; i++)
                {
                    map.terrainGrid.SetTerrain(fill[i], granite);
                    layout.BridgeCells.Add(fill[i]);
                }
                if (fill.Count == 0) break;
            }
        }

        private static void BuildCenterline(
            Map map,
            MapComponent_WdBridgeClashLayout layout,
            Vector2 center,
            Vector2 along,
            Vector2 perp,
            int[] lateral,
            float halfSteps,
            IntVec3[] centerline)
        {
            for (int i = 0; i < centerline.Length; i++)
            {
                float t = i - halfSteps;
                float lat = SampleLateral(lateral, t, halfSteps);
                Vector2 mid = center + along * t + perp * lat;
                IntVec3 midCell = new IntVec3(Mathf.FloorToInt(mid.x), 0, Mathf.FloorToInt(mid.y));
                centerline[i] = (midCell.InBounds(map) && layout.IsOnBridge(midCell))
                    ? midCell
                    : NearestBridgeCell(layout, midCell);
            }
        }

        /// <summary>
        /// Long-side rails including map ends. Entry/exit faces (outward along the deck) stay open.
        /// Cardinals keep one cell per along-step; diagonals keep every stair-step perimeter cell.
        /// </summary>
        private static void CollectLongSideEdges(
            MapComponent_WdBridgeClashLayout layout,
            Vector2 center,
            Vector2 along,
            Vector2 perp,
            int[] lateral,
            float halfSteps,
            out List<IntVec3> edgeLo,
            out List<IntVec3> edgeHi)
        {
            edgeLo = new List<IntVec3>(128);
            edgeHi = new List<IntVec3>(128);
            if (layout?.BridgeCells == null || layout.BridgeCells.Count == 0) return;

            if (!IsCardinalDeckAngle(layout.AngleDegrees))
            {
                CollectDiagonalLongSideRails(layout, center, along, perp, lateral, halfSteps, edgeLo, edgeHi);
                return;
            }

            float tMin = float.MaxValue;
            float tMax = float.MinValue;
            foreach (IntVec3 c in layout.BridgeCells)
            {
                float t = Vector2.Dot(new Vector2(c.x + 0.5f, c.z + 0.5f) - center, along);
                if (t < tMin) tMin = t;
                if (t > tMax) tMax = t;
            }

            int buckets = Mathf.Max(8, Mathf.CeilToInt(tMax - tMin) + 1);
            var loBest = new IntVec3[buckets];
            var hiBest = new IntVec3[buckets];
            var loSide = new float[buckets];
            var hiSide = new float[buckets];
            for (int i = 0; i < buckets; i++)
            {
                loBest[i] = IntVec3.Invalid;
                hiBest[i] = IntVec3.Invalid;
                loSide[i] = 0f;
                hiSide[i] = 0f;
            }

            foreach (IntVec3 c in layout.BridgeCells)
            {
                if (!IsBridgePerimeterCell(layout, c)) continue;
                if (!IsLongSidePerimeter(layout, c, along, perp)) continue;

                Vector2 p = new Vector2(c.x + 0.5f, c.z + 0.5f);
                float t = Vector2.Dot(p - center, along);
                float lat = SampleLateral(lateral, t, halfSteps);
                Vector2 mid = center + along * t + perp * lat;
                float side = Vector2.Dot(p - mid, perp);

                int b = Mathf.Clamp(Mathf.FloorToInt(t - tMin), 0, buckets - 1);
                if (side < 0f)
                {
                    if (!loBest[b].IsValid || side < loSide[b])
                    {
                        loBest[b] = c;
                        loSide[b] = side;
                    }
                }
                else
                {
                    if (!hiBest[b].IsValid || side > hiSide[b])
                    {
                        hiBest[b] = c;
                        hiSide[b] = side;
                    }
                }
            }

            IntVec3 prevLo = IntVec3.Invalid;
            IntVec3 prevHi = IntVec3.Invalid;
            for (int b = 0; b < buckets; b++)
            {
                if (loBest[b].IsValid && loBest[b] != prevLo)
                {
                    edgeLo.Add(loBest[b]);
                    prevLo = loBest[b];
                }
                if (hiBest[b].IsValid && hiBest[b] != prevHi)
                {
                    edgeHi.Add(hiBest[b]);
                    prevHi = hiBest[b];
                }
            }
        }

        private static void CollectDiagonalLongSideRails(
            MapComponent_WdBridgeClashLayout layout,
            Vector2 center,
            Vector2 along,
            Vector2 perp,
            int[] lateral,
            float halfSteps,
            List<IntVec3> edgeLo,
            List<IntVec3> edgeHi)
        {
            var lo = new List<KeyValuePair<float, IntVec3>>(128);
            var hi = new List<KeyValuePair<float, IntVec3>>(128);
            foreach (IntVec3 c in layout.BridgeCells)
            {
                if (!IsBridgePerimeterCell(layout, c)) continue;
                if (!IsLongSidePerimeter(layout, c, along, perp)) continue;

                Vector2 p = new Vector2(c.x + 0.5f, c.z + 0.5f);
                float t = Vector2.Dot(p - center, along);
                float lat = SampleLateral(lateral, t, halfSteps);
                Vector2 mid = center + along * t + perp * lat;
                float side = Vector2.Dot(p - mid, perp);
                if (side < 0f) lo.Add(new KeyValuePair<float, IntVec3>(t, c));
                else hi.Add(new KeyValuePair<float, IntVec3>(t, c));
            }
            AppendSortedUnique(lo, edgeLo);
            AppendSortedUnique(hi, edgeHi);
        }

        private static void AppendSortedUnique(List<KeyValuePair<float, IntVec3>> raw, List<IntVec3> dest)
        {
            raw.Sort((a, b) => a.Key.CompareTo(b.Key));
            IntVec3 prev = IntVec3.Invalid;
            for (int i = 0; i < raw.Count; i++)
            {
                IntVec3 c = raw[i].Value;
                if (c == prev) continue;
                dest.Add(c);
                prev = c;
            }
        }

        /// <summary>
        /// True when the open (non-bridge) neighbor directions are mostly across the bridge,
        /// not along it — i.e. a long-side railing cell, not an entry/exit face.
        /// </summary>
        private static bool IsLongSidePerimeter(
            MapComponent_WdBridgeClashLayout layout, IntVec3 c, Vector2 along, Vector2 perp)
        {
            if (!TryGetCellOutward(layout, c, out Vector2 outward)) return false;
            float alongAmt = Mathf.Abs(Vector2.Dot(outward, along));
            float perpAmt = Mathf.Abs(Vector2.Dot(outward, perp));
            return perpAmt >= alongAmt;
        }

        private static bool TryGetCellOutward(MapComponent_WdBridgeClashLayout layout, IntVec3 c, out Vector2 outward)
        {
            Vector2 acc = Vector2.zero;
            if (!layout.IsOnBridge(c + IntVec3.East)) acc += new Vector2(1f, 0f);
            if (!layout.IsOnBridge(c + IntVec3.West)) acc += new Vector2(-1f, 0f);
            if (!layout.IsOnBridge(c + IntVec3.North)) acc += new Vector2(0f, 1f);
            if (!layout.IsOnBridge(c + IntVec3.South)) acc += new Vector2(0f, -1f);
            if (acc.sqrMagnitude < 1e-6f)
            {
                outward = Vector2.zero;
                return false;
            }
            outward = acc.normalized;
            return true;
        }

        private static bool IsBridgePerimeterCell(MapComponent_WdBridgeClashLayout layout, IntVec3 c)
        {
            if (!layout.IsOnBridge(c)) return false;
            return !layout.IsOnBridge(c + IntVec3.North)
                || !layout.IsOnBridge(c + IntVec3.South)
                || !layout.IsOnBridge(c + IntVec3.East)
                || !layout.IsOnBridge(c + IntVec3.West);
        }

        private static IntVec3 NearestBridgeCell(MapComponent_WdBridgeClashLayout layout, IntVec3 root)
        {
            if (layout?.BridgeCells == null) return IntVec3.Invalid;
            if (root.IsValid && layout.IsOnBridge(root)) return root;
            IntVec3 best = IntVec3.Invalid;
            int bestDist = int.MaxValue;
            foreach (IntVec3 c in layout.BridgeCells)
            {
                int d = c.DistanceToSquared(root);
                if (d >= bestDist) continue;
                bestDist = d;
                best = c;
            }
            return best;
        }

        private static void ResolveSpawnEnds(Map map, MapComponent_WdBridgeClashLayout layout, IntVec3[] centerline)
        {
            if (layout?.BridgeCells == null || centerline == null || centerline.Length == 0)
                return;

            IntVec3 fallbackA = IntVec3.Invalid;
            IntVec3 fallbackB = IntVec3.Invalid;
            for (int i = 0; i < centerline.Length; i++)
            {
                IntVec3 c = centerline[i];
                if (!c.IsValid || !layout.BridgeCells.Contains(c)) continue;
                if (!fallbackA.IsValid) fallbackA = c;
                fallbackB = c;
            }
            if (!fallbackA.IsValid)
            {
                foreach (IntVec3 c in layout.BridgeCells)
                {
                    fallbackA = c;
                    break;
                }
            }
            if (!fallbackB.IsValid) fallbackB = fallbackA;

            IntVec3 endA = CenterlineNear(layout, centerline, fromStart: true, inset: SpawnInsetCells);
            IntVec3 endB = CenterlineNear(layout, centerline, fromStart: false, inset: SpawnInsetCells);
            if (!endA.IsValid) endA = fallbackA;
            if (!endB.IsValid) endB = fallbackB;

            layout.AxisEndA = endA;
            layout.AxisEndB = endB;
            AssignPolarity(map, layout, endA, endB, pendingPlayerFromTile, pendingHostileFromTile);
        }

        /// <summary>Re-assign player/hostile ends from world approach tiles (existing maps / camp enter).</summary>
        public static void ApplyEncounterApproaches(Map map, int playerFromTile, int hostileFromTile)
        {
            if (map == null) return;
            var layout = map.GetComponent<MapComponent_WdBridgeClashLayout>();
            if (layout == null) return;
            IntVec3 endA = layout.AxisEndA.IsValid ? layout.AxisEndA : layout.PlayerEnd;
            IntVec3 endB = layout.AxisEndB.IsValid ? layout.AxisEndB : layout.HostileEnd;
            if (!endA.IsValid || !endB.IsValid) return;
            AssignPolarity(map, layout, endA, endB, playerFromTile, hostileFromTile);
            if (layout.PlayerEnd.IsValid)
                MapGenerator.PlayerStartSpot = layout.PlayerEnd;
        }

        public static int CameFromWorldTile(WorldObject wo)
        {
            if (wo == null || wo.Destroyed) return -1;
            int here = wo.Tile.tileId;

            if (wo is WorldObject_Traveler traveler && traveler.pather != null)
            {
                int prev = traveler.pather.previousTileId;
                if (prev >= 0 && prev != here) return prev;
                if (traveler.originObject != null && !traveler.originObject.Destroyed
                    && traveler.originObject.Tile.tileId != here)
                    return traveler.originObject.Tile.tileId;
                return -1;
            }

            if (wo is Caravan caravan && caravan.pather != null)
            {
                object v = CaravanPreviousTileField != null
                    ? CaravanPreviousTileField.GetValue(caravan.pather)
                    : CaravanPreviousTileProp?.GetValue(caravan.pather);
                int id = PlanetTileId(v);
                if (id >= 0 && id != here) return id;
            }

            return -1;
        }

        /// <summary>Player caravan currently sitting on this world tile (camp / clash map gen).</summary>
        public static Caravan FindPlayerCaravanOnTile(PlanetTile tile)
        {
            if (!tile.Valid || Find.WorldObjects == null) return null;
            List<Caravan> caravans = Find.WorldObjects.Caravans;
            if (caravans == null) return null;
            for (int i = 0; i < caravans.Count; i++)
            {
                Caravan c = caravans[i];
                if (c == null || c.Destroyed) continue;
                if (c.Faction == null || !c.Faction.IsPlayer) continue;
                if (c.Tile == tile) return c;
            }
            return null;
        }

        private static int PlanetTileId(object value)
        {
            if (value is PlanetTile pt && pt.tileId >= 0) return pt.tileId;
            if (value is int i) return i;
            return -1;
        }

        private static void AssignPolarity(
            Map map,
            MapComponent_WdBridgeClashLayout layout,
            IntVec3 endA,
            IntVec3 endB,
            int playerFrom,
            int hostileFrom)
        {
            int clash = map.Tile.tileId;
            Vector2 a = new Vector2(endA.x - map.Center.x, endA.z - map.Center.z);
            Vector2 b = new Vector2(endB.x - map.Center.x, endB.z - map.Center.z);

            float px = 0f, pz = 0f, hx = 0f, hz = 0f;
            bool haveP = playerFrom >= 0 && playerFrom != clash
                && AssaultArtilleryIngressDirection.TryWorldToLocalMapAxes(clash, playerFrom, out px, out pz);
            bool haveH = hostileFrom >= 0 && hostileFrom != clash
                && AssaultArtilleryIngressDirection.TryWorldToLocalMapAxes(clash, hostileFrom, out hx, out hz);

            if (haveP)
            {
                float da = px * a.x + pz * a.y;
                float db = px * b.x + pz * b.y;
                if (da >= db)
                {
                    layout.PlayerEnd = endA;
                    layout.HostileEnd = endB;
                }
                else
                {
                    layout.PlayerEnd = endB;
                    layout.HostileEnd = endA;
                }
                return;
            }

            if (haveH)
            {
                float da = hx * a.x + hz * a.y;
                float db = hx * b.x + hz * b.y;
                if (da >= db)
                {
                    layout.HostileEnd = endA;
                    layout.PlayerEnd = endB;
                }
                else
                {
                    layout.HostileEnd = endB;
                    layout.PlayerEnd = endA;
                }
                return;
            }

            layout.PlayerEnd = endA;
            layout.HostileEnd = endB;
        }

        private static string CompassLabel(Map map, IntVec3 cell)
        {
            if (map == null || !cell.IsValid) return "unknown";
            int dx = cell.x - map.Center.x;
            int dz = cell.z - map.Center.z;
            int ax = Mathf.Abs(dx);
            int az = Mathf.Abs(dz);
            const float includeOther = 0.4f;
            bool useX = ax > 2 && ax >= az * includeOther;
            bool useZ = az > 2 && az >= ax * includeOther;
            string ew = dx >= 0 ? "East" : "West";
            string ns = dz >= 0 ? "North" : "South";
            if (useZ && useX) return ns + " " + ew;
            if (useZ) return ns;
            if (useX) return ew;
            return "Center";
        }

        private static IntVec3 CenterlineNear(
            MapComponent_WdBridgeClashLayout layout, IntVec3[] centerline, bool fromStart, int inset)
        {
            int n = centerline.Length;
            if (fromStart)
            {
                int seen = 0;
                for (int i = 0; i < n; i++)
                {
                    if (!centerline[i].IsValid || !layout.BridgeCells.Contains(centerline[i])) continue;
                    if (seen++ >= inset) return centerline[i];
                }
            }
            else
            {
                int seen = 0;
                for (int i = n - 1; i >= 0; i--)
                {
                    if (!centerline[i].IsValid || !layout.BridgeCells.Contains(centerline[i])) continue;
                    if (seen++ >= inset) return centerline[i];
                }
            }
            return IntVec3.Invalid;
        }

        private static void PlaceBridgeEdgeBarriers(
            Map map,
            MapComponent_WdBridgeClashLayout layout,
            List<IntVec3> edgeLo,
            List<IntVec3> edgeHi,
            Vector2 perp,
            Vector2 center,
            Vector2 along,
            int[] lateral,
            float halfSteps)
        {
            ThingDef shortDef = DefDatabase<ThingDef>.GetNamedSilentFail("AncientConcreteBarrier");
            ThingDef longDef = ModsConfig.OdysseyActive
                ? DefDatabase<ThingDef>.GetNamedSilentFail("AncientBarrierLong")
                : null;
            ThingDef barricadeDef = ThingDefOf.Barricade
                ?? DefDatabase<ThingDef>.GetNamedSilentFail("Barricade");
            ThingDef graniteStuff = ThingDefOf.BlocksGranite
                ?? DefDatabase<ThingDef>.GetNamedSilentFail("BlocksGranite");
            bool allowBarricade = barricadeDef != null && graniteStuff != null && barricadeDef.MadeFromStuff;
            if (shortDef == null && longDef == null && !allowBarricade) return;

            // 2×1 only on cardinal decks; diagonals use 1×1 so they hug stair-step rails.
            bool allowLong = longDef != null && IsCardinalDeckAngle(layout.AngleDegrees);

            // size (2,1): North/South → long axis E–W; East/West → long axis N–S.
            Rot4 longAlongDeck = LongBarrierRotAlongDeck(layout.AngleDegrees);

            int patternLen = Mathf.Max(edgeLo?.Count ?? 0, edgeHi?.Count ?? 0);
            List<int> shared = patternLen > 0
                ? BuildEdgePattern(patternLen, allowLong, allowBarricade)
                : null;

            if (edgeLo != null && edgeLo.Count > 0 && shared != null)
            {
                SpawnEdgePatternOnCells(map, layout, edgeLo, shared, shortDef, longDef, barricadeDef, graniteStuff,
                    EdgeRotFromPerp(-perp), longAlongDeck);
            }
            if (edgeHi != null && edgeHi.Count > 0 && shared != null)
            {
                SpawnEdgePatternOnCells(map, layout, edgeHi, shared, shortDef, longDef, barricadeDef, graniteStuff,
                    EdgeRotFromPerp(perp), longAlongDeck);
            }

            if (allowBarricade)
                PlaceGraniteLCornersAtTilts(map, layout, edgeLo, edgeHi, center, along, lateral, halfSteps,
                    barricadeDef, graniteStuff);
        }

        /// <summary>
        /// 0°/180° decks → horizontal 2×1 (Rot North/South). 90°/270° → vertical (Rot East/West).
        /// </summary>
        private static Rot4 LongBarrierRotAlongDeck(float angleDeg)
        {
            float a = angleDeg % 180f;
            if (a < 0f) a += 180f;
            // Near north–south: long axis along Z.
            if (Mathf.Abs(a - 90f) < 1f)
                return Rot4.East;
            return Rot4.North;
        }

        private static Rot4 EdgeRotFromPerp(Vector2 outward)
        {
            if (outward.sqrMagnitude < 1e-6f) return Rot4.South;
            return Rot4.FromAngleFlat(new Vector3(outward.x, 0f, outward.y).AngleFlat());
        }

        private static Rot4 RotTowardWater(MapComponent_WdBridgeClashLayout layout, IntVec3 cell, Rot4 fallback)
        {
            if (TryGetCellOutward(layout, cell, out Vector2 outward))
                return EdgeRotFromPerp(outward);
            return fallback;
        }

        private static void SpawnEdgePatternOnCells(
            Map map,
            MapComponent_WdBridgeClashLayout layout,
            List<IntVec3> edgeCells,
            List<int> pattern,
            ThingDef shortDef,
            ThingDef longDef,
            ThingDef barricadeDef,
            ThingDef graniteStuff,
            Rot4 shortRot,
            Rot4 longRot)
        {
            if (edgeCells == null || edgeCells.Count == 0) return;
            int major = 0;
            for (int i = 0; i < pattern.Count && major < edgeCells.Count; i++)
            {
                int seg = pattern[i];
                if (seg == SegGap)
                {
                    major++;
                    continue;
                }

                if (IsBarricadeRun(seg))
                {
                    IntVec3 prev = IntVec3.Invalid;
                    for (int k = 0; k < seg && major + k < edgeCells.Count; k++)
                    {
                        IntVec3 cell = edgeCells[major + k];
                        if (!CanPlaceEdgeProp(layout, cell)) continue;
                        // Keep granite barricade runs contiguous on the perimeter (no water jumps).
                        if (prev.IsValid
                            && !AreOrthogonallyAdjacent(prev, cell)
                            && !AreOrthogonallyOrDiagAdjacent(prev, cell))
                            break;
                        if (barricadeDef == null || graniteStuff == null) continue;
                        Thing barricade = ThingMaker.MakeThing(barricadeDef, graniteStuff);
                        GenSpawn.Spawn(barricade, cell, map, WipeMode.Vanish);
                        prev = cell;
                    }
                    major += seg;
                    continue;
                }

                IntVec3 at = edgeCells[major];
                if (!CanPlaceEdgeProp(layout, at))
                {
                    major += SegmentWidth(seg);
                    continue;
                }

                if (seg == SegLong && longDef != null)
                {
                    // Never use outward/perp rot for 2×1 — that stands them across the deck.
                    if (TrySpawnLongBarrierOnRail(map, layout, longDef, at, longRot)
                        || TrySpawnLongBarrierOnRail(map, layout, longDef, at, longRot.Opposite))
                    {
                        major += 2;
                        continue;
                    }
                    if (shortDef != null)
                        TrySpawnBarrierFullyOnDeck(map, layout, shortDef, at, RotTowardWater(layout, at, shortRot));
                    if (major + 1 < edgeCells.Count && shortDef != null)
                    {
                        IntVec3 next = edgeCells[major + 1];
                        if (CanPlaceEdgeProp(layout, next) && AreOrthogonallyOrDiagAdjacent(at, next))
                            TrySpawnBarrierFullyOnDeck(map, layout, shortDef, next, RotTowardWater(layout, next, shortRot));
                    }
                    major += 2;
                    continue;
                }

                if (shortDef != null)
                    TrySpawnBarrierFullyOnDeck(map, layout, shortDef, at, RotTowardWater(layout, at, shortRot));
                major += 1;
            }
        }

        private static bool CanPlaceEdgeProp(MapComponent_WdBridgeClashLayout layout, IntVec3 cell) =>
            cell.IsValid && layout.IsOnBridge(cell) && IsBridgePerimeterCell(layout, cell);

        /// <summary>
        /// At each ±1 centerline tilt, drop granite barricades forming an L on both rails
        /// (the two rail cells plus the on-deck ortho connector).
        /// </summary>
        private static void PlaceGraniteLCornersAtTilts(
            Map map,
            MapComponent_WdBridgeClashLayout layout,
            List<IntVec3> edgeLo,
            List<IntVec3> edgeHi,
            Vector2 center,
            Vector2 along,
            int[] lateral,
            float halfSteps,
            ThingDef barricadeDef,
            ThingDef graniteStuff)
        {
            if (lateral == null || barricadeDef == null || graniteStuff == null) return;
            for (int i = 1; i < lateral.Length; i++)
            {
                if (lateral[i] == lateral[i - 1]) continue;
                float tCorner = i - halfSteps - 0.5f;
                PlaceGraniteLOnRail(map, layout, edgeLo, center, along, tCorner, barricadeDef, graniteStuff);
                PlaceGraniteLOnRail(map, layout, edgeHi, center, along, tCorner, barricadeDef, graniteStuff);
            }
        }

        private static void PlaceGraniteLOnRail(
            Map map,
            MapComponent_WdBridgeClashLayout layout,
            List<IntVec3> edge,
            Vector2 center,
            Vector2 along,
            float tCorner,
            ThingDef barricadeDef,
            ThingDef graniteStuff)
        {
            if (edge == null || edge.Count < 2) return;
            int best = -1;
            float bestScore = float.MaxValue;
            for (int i = 0; i < edge.Count - 1; i++)
            {
                float t0 = Vector2.Dot(new Vector2(edge[i].x + 0.5f, edge[i].z + 0.5f) - center, along);
                float t1 = Vector2.Dot(new Vector2(edge[i + 1].x + 0.5f, edge[i + 1].z + 0.5f) - center, along);
                float mid = (t0 + t1) * 0.5f;
                float score = Mathf.Abs(mid - tCorner);
                if (score >= bestScore) continue;
                bestScore = score;
                best = i;
            }
            if (best < 0 || bestScore > 3f) return;

            IntVec3 a = edge[best];
            IntVec3 b = edge[best + 1];
            SpawnGraniteBarricade(map, layout, a, barricadeDef, graniteStuff, requirePerimeter: true);
            SpawnGraniteBarricade(map, layout, b, barricadeDef, graniteStuff, requirePerimeter: true);

            IntVec3 c1 = new IntVec3(a.x, 0, b.z);
            IntVec3 c2 = new IntVec3(b.x, 0, a.z);
            bool c1Ok = c1 != a && c1 != b && layout.IsOnBridge(c1);
            bool c2Ok = c2 != a && c2 != b && layout.IsOnBridge(c2);
            if (c1Ok && !c2Ok)
                SpawnGraniteBarricade(map, layout, c1, barricadeDef, graniteStuff, requirePerimeter: false);
            else if (c2Ok && !c1Ok)
                SpawnGraniteBarricade(map, layout, c2, barricadeDef, graniteStuff, requirePerimeter: false);
            else if (c1Ok && c2Ok)
            {
                // Prefer the connector that stays on the outline if only one is perimeter.
                bool p1 = IsBridgePerimeterCell(layout, c1);
                bool p2 = IsBridgePerimeterCell(layout, c2);
                if (p1 && !p2)
                    SpawnGraniteBarricade(map, layout, c1, barricadeDef, graniteStuff, requirePerimeter: false);
                else if (p2 && !p1)
                    SpawnGraniteBarricade(map, layout, c2, barricadeDef, graniteStuff, requirePerimeter: false);
                else
                    SpawnGraniteBarricade(map, layout, c1, barricadeDef, graniteStuff, requirePerimeter: false);
            }
        }

        private static void SpawnGraniteBarricade(
            Map map,
            MapComponent_WdBridgeClashLayout layout,
            IntVec3 cell,
            ThingDef barricadeDef,
            ThingDef graniteStuff,
            bool requirePerimeter)
        {
            if (map == null || !cell.InBounds(map) || !layout.IsOnBridge(cell)) return;
            if (requirePerimeter && !IsBridgePerimeterCell(layout, cell)) return;
            Thing barricade = ThingMaker.MakeThing(barricadeDef, graniteStuff);
            GenSpawn.Spawn(barricade, cell, map, WipeMode.Vanish);
        }

        private static bool AreOrthogonallyAdjacent(IntVec3 a, IntVec3 b) =>
            (Mathf.Abs(a.x - b.x) + Mathf.Abs(a.z - b.z)) == 1;

        private static bool AreOrthogonallyOrDiagAdjacent(IntVec3 a, IntVec3 b) =>
            Mathf.Abs(a.x - b.x) <= 1 && Mathf.Abs(a.z - b.z) <= 1 && a != b;

        /// <summary>
        /// Spawn only if every cell of the thing's footprint is on the granite deck (and root is perimeter).
        /// </summary>
        private static bool TrySpawnBarrierFullyOnDeck(
            Map map,
            MapComponent_WdBridgeClashLayout layout,
            ThingDef def,
            IntVec3 cell,
            Rot4 rot)
        {
            if (def == null || !cell.InBounds(map) || !CanPlaceEdgeProp(layout, cell)) return false;

            CellRect occupied = GenAdj.OccupiedRect(cell, rot, def.size);
            foreach (IntVec3 c in occupied)
            {
                if (!c.InBounds(map) || !layout.IsOnBridge(c))
                    return false;
            }

            GenSpawn.Spawn(ThingMaker.MakeThing(def), cell, map, rot, WipeMode.Vanish);
            return true;
        }

        /// <summary>
        /// 2×1 must lie on the rail: every footprint cell on deck and on the perimeter
        /// (rejects placements that poke one cell inward across the deck).
        /// </summary>
        private static bool TrySpawnLongBarrierOnRail(
            Map map,
            MapComponent_WdBridgeClashLayout layout,
            ThingDef def,
            IntVec3 cell,
            Rot4 rot)
        {
            if (def == null || !cell.InBounds(map) || !CanPlaceEdgeProp(layout, cell)) return false;

            CellRect occupied = GenAdj.OccupiedRect(cell, rot, def.size);
            foreach (IntVec3 c in occupied)
            {
                if (!c.InBounds(map) || !layout.IsOnBridge(c) || !IsBridgePerimeterCell(layout, c))
                    return false;
            }

            GenSpawn.Spawn(ThingMaker.MakeThing(def), cell, map, rot, WipeMode.Vanish);
            return true;
        }

        private static List<int> BuildEdgePattern(int length, bool allowLong, bool allowBarricade)
        {
            var pattern = new List<int>(length / 2 + 8);
            int filled = 0;

            while (filled < length)
            {
                if (pattern.Count > 0 && Rand.Chance(SegmentGapChance))
                {
                    pattern.Add(SegGap);
                    filled++;
                    if (filled < length && Rand.Chance(0.22f))
                    {
                        pattern.Add(SegGap);
                        filled++;
                    }
                    continue;
                }

                int seg = PickNextSegment(length - filled, allowLong, allowBarricade);
                if (seg == int.MinValue) break;

                pattern.Add(seg);
                filled += SegmentWidth(seg);
            }

            return pattern;
        }

        private static List<int> BuildMostlyAlignedPattern(
            List<int> source, int length, bool allowLong, bool allowBarricade)
        {
            if (source == null || source.Count == 0)
                return BuildEdgePattern(length, allowLong, allowBarricade);

            var pattern = new List<int>(source.Count + 4);
            int filled = 0;

            for (int i = 0; i < source.Count && filled < length; i++)
            {
                int seg = source[i];

                if (Rand.Chance(MirrorNoiseChance))
                {
                    if (seg == SegLong && allowLong)
                    {
                        if (filled + 2 <= length)
                        {
                            pattern.Add(SegShort);
                            pattern.Add(SegShort);
                            filled += 2;
                            continue;
                        }
                    }
                    else if (seg == SegShort && allowLong && i + 1 < source.Count && source[i + 1] == SegShort
                             && filled + 2 <= length && Rand.Bool)
                    {
                        pattern.Add(SegLong);
                        filled += 2;
                        i++;
                        continue;
                    }
                    else if (IsBarricadeRun(seg) && allowBarricade && seg >= GraniteBarricadeMin + 1
                             && Rand.Bool)
                    {
                        pattern.Add(seg - 1);
                        pattern.Add(SegGap);
                        filled += seg;
                        continue;
                    }
                }

                if (seg == SegLong && !allowLong)
                    seg = SegShort;

                int width = SegmentWidth(seg);
                if (width > length - filled)
                {
                    int rem = length - filled;
                    if (IsBarricadeRun(seg) && rem >= 1)
                        seg = rem;
                    else if (allowLong && rem >= 2)
                        seg = SegLong;
                    else if (rem >= 1)
                        seg = SegShort;
                    else
                        break;
                    width = SegmentWidth(seg);
                }

                pattern.Add(seg);
                filled += width;
            }

            while (filled < length)
            {
                int seg = PickNextSegment(length - filled, allowLong, allowBarricade);
                if (seg == int.MinValue) break;
                pattern.Add(seg);
                filled += SegmentWidth(seg);
            }

            return pattern;
        }

        private static int PickNextSegment(int remaining, bool allowLong, bool allowBarricade)
        {
            if (remaining < 1) return int.MinValue;

            if (allowBarricade && remaining >= GraniteBarricadeMin && Rand.Chance(BarricadeSegmentChance))
            {
                int run = Rand.RangeInclusive(GraniteBarricadeMin, GraniteBarricadeMax);
                return run > remaining ? remaining : run;
            }

            if (allowLong && remaining >= 2 && Rand.Chance(0.55f))
                return SegLong;

            return SegShort;
        }

        private static bool IsBarricadeRun(int seg) => seg > 0;

        private static int SegmentWidth(int seg)
        {
            if (seg == SegGap) return 1;
            if (seg == SegShort) return 1;
            if (seg == SegLong) return 2;
            return seg;
        }

        private static TerrainDef ResolveGraniteFloor()
        {
            return DefDatabase<TerrainDef>.GetNamedSilentFail("TileGranite")
                ?? DefDatabase<TerrainDef>.GetNamedSilentFail("FlagstoneGranite")
                ?? DefDatabase<TerrainDef>.GetNamedSilentFail("Granite_Smooth")
                ?? TerrainDefOf.Bridge
                ?? TerrainDefOf.Soil;
        }
    }
}
