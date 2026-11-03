using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Overlay on a real Encounter biome map: sandbag/barricade rails with a shifting centerline
    /// (bridge-style 22.5° snap + distance fill), Oomby <c>Tr_TrenchDirt</c> fill, optional VVE
    /// tank traps / barbed wire, or MO cavalry spikes under medieval mode.
    /// </summary>
    public class GenStep_WD_TrenchClash : GenStep
    {
        /// <summary>Total cells across rails inclusive (dirt fill is the interior between rails).</summary>
        public const int CorridorWidth = 5;

        private struct TrenchArm
        {
            public float RawDirectedDeg;
            public float SnappedDirectedDeg;
            public Vector2 Along;
            public Vector2 Perp;
        }
        private const int DuckboardSectionMin = 8;
        private const int DuckboardSectionMax = 22;
        private const int DuckboardGapMin = 4;
        private const int DuckboardGapMax = 12;
        private const int DuckboardFuzzCells = 2;
        private const int LateralStepMinSpacing = 20;
        private const int LateralStepMaxSpacing = 40;
        private const int TankTrapTargetMin = 30;
        private const int TankTrapTargetMax = 60;
        private const int TankTrapDistMin = 5;
        private const int TankTrapDistMax = 10;
        private const int WireRunMin = 14;
        private const int WireRunMax = 36;
        private const int WireGapMin = 6;
        private const int WireGapMax = 16;
        private const int WireCurveMinSpacing = 5;
        private const int WireCurveMaxSpacing = 10;
        private const int RailOpeningMin = 3;
        private const int RailOpeningMax = 5;
        private const int RailSolidMin = 12;
        private const int RailSolidMax = 28;

        public override int SeedPart => 913847221;

        /// <summary>
        /// Set by outpost-defense / clash map gen so GenStep can read AutoTurrets without relying only on tile ownership.
        /// </summary>
        private static WorldObject_WD_Outpost pendingDefendingOutpost;

        /// <summary>World approach tiles pushed before GenerateMap (mirror bridge clash).</summary>
        private static int pendingPlayerFromTile = -1;
        private static int pendingHostileFromTile = -1;

        public static void PushDefendingOutpost(WorldObject_WD_Outpost outpost) =>
            pendingDefendingOutpost = outpost;

        public static void ClearDefendingOutpost() => pendingDefendingOutpost = null;

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

            IntVec3 center = map.Center;
            var layout = map.GetComponent<MapComponent_WdTrenchClashLayout>()
                ?? new MapComponent_WdTrenchClashLayout(map);
            if (map.GetComponent<MapComponent_WdTrenchClashLayout>() == null)
                map.components.Add(layout);

            List<TrenchArm> arms = ResolveSnappedArmsFromWorld(map.Tile);
            if (arms.Count == 0)
            {
                bool horiz = ResolveIsHorizontal(map.Tile);
                float axis = horiz ? 0f : 90f;
                arms.Add(MakeArmFromDirectedAngle(axis, axis));
                arms.Add(MakeArmFromDirectedAngle(axis + 180f, axis + 180f));
            }

            bool anyEW = false;
            bool anyNS = false;
            var armRots = new List<Rot4>(arms.Count);
            for (int i = 0; i < arms.Count; i++)
            {
                Rot4 r = DominantCardinalFromDirectedAngle(arms[i].SnappedDirectedDeg);
                armRots.Add(r);
                if (r == Rot4.East || r == Rot4.West) anyEW = true;
                if (r == Rot4.North || r == Rot4.South) anyNS = true;
            }
            layout.IsHorizontal = anyEW && !anyNS;
            layout.ArmDirections = armRots;

            int maxAlong = Mathf.CeilToInt(Mathf.Sqrt(map.Size.x * map.Size.x + map.Size.z * map.Size.z))
                + CorridorWidth;
            int[] lateral = BuildLateralOffsets(maxAlong);

            var corridor = new List<IntVec3>();
            var trenchFill = new List<IntVec3>();
            var painted = new HashSet<IntVec3>();
            var allRails = new List<HashSet<IntVec3>>(arms.Count);

            for (int i = 0; i < arms.Count; i++)
            {
                var railCells = new HashSet<IntVec3>();
                PaintCorridorArmDistanceFill(
                    map,
                    center,
                    arms[i],
                    lateral,
                    corridor,
                    trenchFill,
                    railCells,
                    painted);
                CollectArmRailEdges(map, center, arms[i], painted, railCells);
                allRails.Add(railCells);
            }

            layout.CorridorCells = corridor;
            layout.TrenchFillCells = trenchFill;

            ThingDef cloth = ThingDefOf.Cloth;
            ThingDef barricadeStuff = ResolveMostCommonStoneBlocks(map);

            for (int i = 0; i < arms.Count; i++)
                PlaceRailFortificationsForArm(map, center, arms[i], allRails[i], cloth, barricadeStuff);

            FillTrenchCells(map, trenchFill, center);

            // Hostile approach from world push (bridge-style); Rand only when unknown.
            layout.HostileOctant = ResolveHostileOctantForGen(map.Tile);
            layout.HostileFromNorthOrEast = OctantPrefersHigh(layout.HostileOctant);

            HashSet<IntVec3> nestReserved = new HashSet<IntVec3>();
            WorldObject_WD_Outpost defending = ResolveDefendingOutpost(map.Tile);
            SpawnTrenchMapTurrets(map, layout, defending, lateral, center, arms, nestReserved);

            ThingDef approachDef = ResolveApproachStrandDef(out bool approachIsTrap);
            var approachCells = new HashSet<IntVec3>();
            int approachPlaced = 0;
            // One strand/trap pass per undirected axis, but t runs the full line through the map
            // (not a half-ray). Opposed arms (e.g. 22.5° and 202.5°) share a key — skip the duplicate.
            var undirectedDone = new HashSet<int>();
            for (int i = 0; i < arms.Count; i++)
            {
                int undirectedKey = Mathf.RoundToInt(
                    (arms[i].SnappedDirectedDeg % 180f) / GenStep_WD_BridgeClash.AngleSnapDegrees);
                if (!undirectedDone.Add(undirectedKey)) continue;

                approachPlaced += SpawnApproachStrandsOnArm(
                    map, center, arms[i], lateral, nestReserved, painted, approachDef, approachIsTrap, approachCells);
            }

            if (!approachIsTrap && approachDef != null && approachCells.Count > 0)
                FillWireCornerPattern(map, approachDef, approachCells, painted, nestReserved);

            bool skipTankTraps = approachIsTrap && approachPlaced > 0;
            if (!skipTankTraps)
            {
                undirectedDone.Clear();
                for (int i = 0; i < arms.Count; i++)
                {
                    int undirectedKey = Mathf.RoundToInt(
                        (arms[i].SnappedDirectedDeg % 180f) / GenStep_WD_BridgeClash.AngleSnapDegrees);
                    if (!undirectedDone.Add(undirectedKey)) continue;
                    SpawnTankTrapsNearArm(map, center, arms[i], lateral, nestReserved, painted);
                }
            }

            layout.HostileSpawnCell = ResolveHostileSpawnCell(map, layout);
            layout.PlayerStartCell = ResolvePlayerStartNearCenter(map, trenchFill, center);

            if (layout.PlayerStartCell.IsValid)
                MapGenerator.PlayerStartSpot = layout.PlayerStartCell;

            LogTrenchGenVerbose(map, layout, arms);
        }

        private static void LogTrenchGenVerbose(Map map, MapComponent_WdTrenchClashLayout layout, List<TrenchArm> arms)
        {
            if (arms == null || arms.Count == 0) return;
            var sb = new System.Text.StringBuilder();
            sb.Append("[TSA WD] Trench clash arms=").Append(arms.Count);
            for (int i = 0; i < arms.Count; i++)
            {
                sb.Append(" | arm").Append(i)
                    .Append(" snapped=").Append(arms[i].SnappedDirectedDeg.ToString("0.#")).Append('°')
                    .Append(" raw=").Append(arms[i].RawDirectedDeg.ToString("0.#")).Append('°');
            }
            sb.Append(" | Player spawns ")
                .Append(CompassLabel(map, layout.PlayerStartCell))
                .Append(". Enemy spawns ")
                .Append(CompassLabel(map, layout.HostileSpawnCell))
                .Append(" (octant=").Append(layout.HostileOctant).Append(')');
            WDVerbose.MsgNoTick(sb.ToString());
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

        /// <summary>CE MG / auto turrets on trench maps: enablesTrenchDefenseTurrets upgrade + not medieval.</summary>
        public static bool AllowTrenchMapTurrets(WorldObject_WD_Outpost outpost)
        {
            if (WdMedievalMode.Active) return false;
            if (outpost == null || outpost.Destroyed) return false;
            if (outpost.HasBuiltTrenchDefenseTurrets()) return true;
            // Fallbacks for saves / defs that predate enablesTrenchDefenseTurrets.
            if (OutpostUpgradeUtility.GetHighestBuiltLineTier(outpost, "AutoTurrets") > 0)
                return true;
            return outpost.GetUpgradeLevel("TSA_WD_Upgrade_AutoTurrets") > 0
                || outpost.GetUpgradeLevel("BOR_TSA_WD_Upgrade_AutoTurrets") > 0
                || outpost.GetUpgradeLevel("BOR_TSA_WD_Upgrade_Adv_AutoTurrets") > 0;
        }

        public static WorldObject_WD_Outpost ResolveDefendingOutpost(PlanetTile tile)
        {
            if (pendingDefendingOutpost != null && !pendingDefendingOutpost.Destroyed)
                return pendingDefendingOutpost;
            if (!tile.Valid) return null;
            if (WorldComponent_WdTrenches.TryGetOwnerAt(tile.tileId, out WorldObject owner)
                && owner is WorldObject_WD_Outpost op && !op.Destroyed)
                return op;
            return null;
        }

        /// <summary>Post-gen backup when outpost is known (map gen may have missed pending owner).</summary>
        public static void EnsureTrenchMapTurrets(Map map, WorldObject_WD_Outpost outpost)
        {
            if (map == null || !AllowTrenchMapTurrets(outpost)) return;
            if (CountTrenchMapTurrets(map) > 0) return;

            var layout = map.GetComponent<MapComponent_WdTrenchClashLayout>();
            if (layout == null) return;

            PushDefendingOutpost(outpost);
            try
            {
                bool anyEW = layout.ArmDirections != null
                    && layout.ArmDirections.Exists(a => a == Rot4.East || a == Rot4.West);
                bool anyNS = layout.ArmDirections != null
                    && layout.ArmDirections.Exists(a => a == Rot4.North || a == Rot4.South);
                if (!anyEW && !anyNS)
                {
                    anyEW = layout.IsHorizontal;
                    anyNS = !layout.IsHorizontal;
                }

                int maxAlong = Mathf.CeilToInt(Mathf.Sqrt(map.Size.x * map.Size.x + map.Size.z * map.Size.z))
                    + CorridorWidth;
                int[] lateral = BuildLateralOffsets(maxAlong);
                List<TrenchArm> arms = ResolveSnappedArmsFromWorld(map.Tile);
                if (arms.Count == 0)
                {
                    bool horiz = layout.IsHorizontal || anyEW && !anyNS;
                    float axis = horiz ? 0f : 90f;
                    arms.Add(MakeArmFromDirectedAngle(axis, axis));
                    arms.Add(MakeArmFromDirectedAngle(axis + 180f, axis + 180f));
                }
                var reserved = new HashSet<IntVec3>();
                SpawnTrenchMapTurrets(map, layout, outpost, lateral, map.Center, arms, reserved);
            }
            finally
            {
                ClearDefendingOutpost();
            }

            if (CountTrenchMapTurrets(map) == 0)
            {
                Log.Warning(
                    $"[TSA WD] Trench turrets: AutoTurrets OK on {outpost.LabelCap} but placed 0 guns on clash map.");
            }
        }

        private static int CountTrenchMapTurrets(Map map)
        {
            int n = 0;
            var list = map?.listerBuildings?.allBuildingsColonist;
            if (list == null) return 0;
            for (int i = 0; i < list.Count; i++)
            {
                Thing b = list[i];
                if (b == null || b.Destroyed) continue;
                if (b is Building_Turret) n++;
            }
            return n;
        }

        private static void SpawnTrenchMapTurrets(
            Map map,
            MapComponent_WdTrenchClashLayout layout,
            WorldObject_WD_Outpost defending,
            int[] lateral,
            IntVec3 center,
            List<TrenchArm> arms,
            HashSet<IntVec3> nestReserved)
        {
            if (!AllowTrenchMapTurrets(defending)) return;

            // Guns face hostile approach (cardinal MG layouts); nest centers follow trench along/perp.
            Rot4 facing = DominantCardinalFromOctant(layout.HostileOctant);
            TrenchArm nestArm = PickNestAxisArm(arms, layout.HostileOctant);
            Faction nestFaction = Faction.OfPlayer ?? map.ParentFaction ?? defending.Faction;

            HashSet<IntVec3> corridorSet = null;
            if (layout.CorridorCells != null && layout.CorridorCells.Count > 0)
                corridorSet = new HashSet<IntVec3>(layout.CorridorCells);

            HashSet<IntVec3> nests = WdMgNestSpawner.TrySpawnAlongTrenchAxis(
                map,
                center,
                nestArm.Along,
                nestArm.Perp,
                lateral,
                CorridorWidth,
                nestFaction,
                facing,
                countMin: 4,
                countMax: 4,
                poolLevel: 2,
                corridorCells: corridorSet);
            if (nests != null)
            {
                foreach (IntVec3 c in nests)
                    nestReserved.Add(c);
            }
            layout.HostileEdgeLockedByMgNests = nestReserved.Count > 0;
        }

        /// <summary>
        /// Undirected trench axis for nests: prefer the arm most perpendicular to the hostile approach
        /// (guns fire across the ditch). Falls back to the first arm.
        /// </summary>
        private static TrenchArm PickNestAxisArm(List<TrenchArm> arms, int hostileOctant)
        {
            if (arms == null || arms.Count == 0)
                return MakeArmFromDirectedAngle(0f, 0f);

            Vector2 hostileDir = OctantToVector2(hostileOctant);
            TrenchArm best = arms[0];
            float bestScore = -1f;
            var seen = new HashSet<int>();
            for (int i = 0; i < arms.Count; i++)
            {
                int key = Mathf.RoundToInt(
                    (arms[i].SnappedDirectedDeg % 180f) / GenStep_WD_BridgeClash.AngleSnapDegrees);
                if (!seen.Add(key)) continue;
                // Prefer trench axis across the hostile bearing (|dot| near 0).
                float across = 1f - Mathf.Abs(Vector2.Dot(arms[i].Along.normalized, hostileDir));
                if (across <= bestScore) continue;
                bestScore = across;
                best = arms[i];
            }
            return best;
        }

        private static Vector2 OctantToVector2(int octant)
        {
            // WdOctant: 0=N … clockwise; local +X east, +Y north (maps to map Z).
            float a = Mathf.Clamp(octant, 0, 7) * 45f * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(a), Mathf.Cos(a));
        }

        /// <summary>
        /// True when the primary trench axis is east-west.
        /// Uses trench-neighbor world bearings (not road / bridge angle).
        /// </summary>
        public static bool ResolveIsHorizontal(PlanetTile tile)
        {
            if (!TryGetTrenchLocalAxis(tile, out float lx, out float lz))
                return true; // default E–W corridor
            return Mathf.Abs(lx) >= Mathf.Abs(lz);
        }

        /// <summary>
        /// Hostile octant for gen — same priority as bridge <c>AssignPolarity</c>:
        /// player approach known → hostile = opposite side; else hostile approach; else Rand.
        /// </summary>
        private static int ResolveHostileOctantForGen(PlanetTile fightTile) =>
            ResolveHostileOctantFromApproaches(
                fightTile.Valid ? fightTile.tileId : -1,
                pendingPlayerFromTile,
                pendingHostileFromTile);

        /// <summary>
        /// Bridge-style polarity: if the player came from a world tile, hostiles enter from the opposite
        /// map octant. Otherwise use the hostile traveler's previous tile. Same axes as FRD/bridge.
        /// </summary>
        public static int ResolveHostileOctantFromApproaches(int clashTile, int playerFromTile, int hostileFromTile)
        {
            if (clashTile >= 0
                && playerFromTile >= 0
                && playerFromTile != clashTile
                && AssaultArtilleryIngressDirection.TryWorldToLocalMapAxes(
                    clashTile, playerFromTile, out float px, out float pz))
                return ((int)OctantFromLocalAxes(px, pz) + 4) % 8;

            if (clashTile >= 0
                && hostileFromTile >= 0
                && hostileFromTile != clashTile
                && AssaultArtilleryIngressDirection.TryWorldToLocalMapAxes(
                    clashTile, hostileFromTile, out float hx, out float hz))
                return (int)OctantFromLocalAxes(hx, hz);

            return (int)WdOctant.North + Rand.Range(0, 8);
        }

        /// <summary>
        /// Best trench axis on the fight tile (opposed neighbor pair, else single neighbor).
        /// local +X = map east, +Z = map north (same as bridge / FRD).
        /// </summary>
        private static bool TryGetTrenchLocalAxis(PlanetTile tile, out float localX, out float localZ)
        {
            localX = 1f;
            localZ = 0f;
            var trenches = WorldComponent_WdTrenches.Get();
            if (trenches == null || !tile.Valid) return false;

            var nbs = new List<int>();
            if (!trenches.TryGetNeighbors(tile.tileId, nbs) || nbs.Count == 0)
                return false;

            if (nbs.Count == 1)
                return AssaultArtilleryIngressDirection.TryWorldToLocalMapAxes(
                    tile.tileId, nbs[0], out localX, out localZ);

            float bestOpp = -2f;
            bool any = false;
            float bestX = 1f;
            float bestZ = 0f;
            for (int i = 0; i < nbs.Count; i++)
            {
                if (!AssaultArtilleryIngressDirection.TryWorldToLocalMapAxes(
                        tile.tileId, nbs[i], out float xi, out float zi))
                    continue;
                for (int j = i + 1; j < nbs.Count; j++)
                {
                    if (!AssaultArtilleryIngressDirection.TryWorldToLocalMapAxes(
                            tile.tileId, nbs[j], out float xj, out float zj))
                        continue;
                    float opp = -(xi * xj + zi * zj);
                    if (opp <= bestOpp) continue;
                    bestOpp = opp;
                    bestX = xi - xj;
                    bestZ = zi - zj;
                    any = true;
                }
            }
            if (!any)
                return AssaultArtilleryIngressDirection.TryWorldToLocalMapAxes(
                    tile.tileId, nbs[0], out localX, out localZ);

            localX = bestX;
            localZ = bestZ;
            return true;
        }

        private static List<TrenchArm> ResolveSnappedArmsFromWorld(PlanetTile fightTile)
        {
            var arms = new List<TrenchArm>(4);
            var trenches = WorldComponent_WdTrenches.Get();
            if (trenches == null || !fightTile.Valid) return arms;

            var nbs = new List<int>();
            if (!trenches.TryGetNeighbors(fightTile.tileId, nbs) || nbs.Count == 0)
                return arms;

            var seen = new HashSet<int>();
            for (int i = 0; i < nbs.Count; i++)
            {
                if (!AssaultArtilleryIngressDirection.TryWorldToLocalMapAxes(
                        fightTile.tileId, nbs[i], out float lx, out float lz))
                    continue;
                if (Mathf.Abs(lx) < 0.01f && Mathf.Abs(lz) < 0.01f) continue;

                float raw = Mathf.Atan2(lz, lx) * Mathf.Rad2Deg;
                if (raw < 0f) raw += 360f;
                float undirected = raw % 180f;
                if (undirected < 0f) undirected += 180f;
                float snappedU = GenStep_WD_BridgeClash.SnapAngle(undirected);
                GetAxisVectors(snappedU, out Vector2 along, out Vector2 perp);
                Vector2 toward = new Vector2(lx, lz);
                if (toward.sqrMagnitude > 1e-6f) toward.Normalize();
                if (Vector2.Dot(toward, along) < 0f)
                {
                    along = -along;
                    perp = new Vector2(-along.y, along.x);
                }

                float directed = Mathf.Atan2(along.y, along.x) * Mathf.Rad2Deg;
                if (directed < 0f) directed += 360f;
                int key = Mathf.RoundToInt(directed / GenStep_WD_BridgeClash.AngleSnapDegrees) % 16;
                if (!seen.Add(key)) continue;

                arms.Add(new TrenchArm
                {
                    RawDirectedDeg = raw,
                    SnappedDirectedDeg = directed,
                    Along = along,
                    Perp = perp
                });
                if (arms.Count >= WorldComponent_WdTrenches.MaxArmDegree) break;
            }
            return arms;
        }

        private static TrenchArm MakeArmFromDirectedAngle(float rawDirectedDeg, float snappedDirectedDeg)
        {
            float a = snappedDirectedDeg % 360f;
            if (a < 0f) a += 360f;
            float undirected = a % 180f;
            GetAxisVectors(undirected, out Vector2 along, out Vector2 perp);
            Vector2 want = new Vector2(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad));
            if (Vector2.Dot(want, along) < 0f)
            {
                along = -along;
                perp = new Vector2(-along.y, along.x);
            }
            float directed = Mathf.Atan2(along.y, along.x) * Mathf.Rad2Deg;
            if (directed < 0f) directed += 360f;
            return new TrenchArm
            {
                RawDirectedDeg = rawDirectedDeg,
                SnappedDirectedDeg = directed,
                Along = along,
                Perp = perp
            };
        }

        /// <summary>Same as bridge: continuous along / perp from undirected angle degrees.</summary>
        private static void GetAxisVectors(float angleDeg, out Vector2 along, out Vector2 perp)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            along = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            if (along.sqrMagnitude < 1e-6f) along = Vector2.right;
            along.Normalize();
            if (along.x < -0.01f || (Mathf.Abs(along.x) < 0.01f && along.y < 0f))
                along = -along;
            perp = new Vector2(-along.y, along.x);
        }

        private static Rot4 DominantCardinalFromDirectedAngle(float directedDeg)
        {
            float a = directedDeg % 360f;
            if (a < 0f) a += 360f;
            // Map east=0 → prefer E/N/W/S buckets with diagonals favoring N/S like octant helper.
            if (a >= 315f || a < 45f) return Rot4.East;
            if (a < 135f) return Rot4.North;
            if (a < 225f) return Rot4.West;
            return Rot4.South;
        }

        /// <summary>
        /// Bridge-style distance fill for one outward half-ray: every cell with t≥0 and |perpDist|≤halfW.
        /// </summary>
        private static void PaintCorridorArmDistanceFill(
            Map map,
            IntVec3 center,
            TrenchArm arm,
            int[] lateral,
            List<IntVec3> corridor,
            List<IntVec3> trenchFill,
            HashSet<IntVec3> railCells,
            HashSet<IntVec3> painted)
        {
            Vector2 centerV = new Vector2(center.x + 0.5f, center.z + 0.5f);
            float halfW = CorridorWidth * 0.5f;
            float tMax = Mathf.Sqrt(map.Size.x * map.Size.x + map.Size.z * map.Size.z) + CorridorWidth;

            foreach (IntVec3 c in map.AllCells)
            {
                Vector2 p = new Vector2(c.x + 0.5f, c.z + 0.5f);
                float t = Vector2.Dot(p - centerV, arm.Along);
                if (t < -0.75f || t > tMax) continue;

                float lat = SampleLateralFloat(lateral, t);
                Vector2 mid = centerV + arm.Along * t + arm.Perp * lat;
                float perpDist = Vector2.Dot(p - mid, arm.Perp);
                if (Mathf.Abs(perpDist) > halfW) continue;

                if (!painted.Add(c)) continue;
                ClearCellForTrenchWork(c, map);
                corridor.Add(c);
                if (Mathf.Abs(perpDist) >= halfW - 1f)
                    railCells.Add(c);
                else
                    trenchFill.Add(c);
            }
        }

        /// <summary>
        /// Prefer bridge perimeter rails on long sides (cardinal buckets or diagonal stair cells).
        /// Refines the outer band tagged during distance fill.
        /// </summary>
        private static void CollectArmRailEdges(
            Map map,
            IntVec3 center,
            TrenchArm arm,
            HashSet<IntVec3> painted,
            HashSet<IntVec3> railCells)
        {
            if (painted == null || painted.Count == 0 || railCells == null) return;
            Vector2 centerV = new Vector2(center.x + 0.5f, center.z + 0.5f);
            bool cardinal = GenStep_WD_BridgeClash.IsCardinalDeckAngle(arm.SnappedDirectedDeg);
            var lo = new List<KeyValuePair<float, IntVec3>>(64);
            var hi = new List<KeyValuePair<float, IntVec3>>(64);
            float halfW = CorridorWidth * 0.5f;

            // Seed from cells already marked as rails for this arm during paint.
            var seed = new List<IntVec3>(railCells);
            railCells.Clear();
            for (int i = 0; i < seed.Count; i++)
            {
                IntVec3 c = seed[i];
                if (!c.InBounds(map) || !IsCorridorPerimeterCell(painted, c)) continue;
                Vector2 p = new Vector2(c.x + 0.5f, c.z + 0.5f);
                float t = Vector2.Dot(p - centerV, arm.Along);
                if (t < -0.75f) continue;
                float side = Vector2.Dot(p - centerV, arm.Perp);
                if (Mathf.Abs(side) > halfW + 1.5f) continue;
                if (side < 0f) lo.Add(new KeyValuePair<float, IntVec3>(t, c));
                else hi.Add(new KeyValuePair<float, IntVec3>(t, c));
            }

            AppendSortedUniqueRails(lo, railCells, cardinal);
            AppendSortedUniqueRails(hi, railCells, cardinal);
            if (railCells.Count == 0)
            {
                for (int i = 0; i < seed.Count; i++)
                    railCells.Add(seed[i]);
            }
        }

        private static bool IsCorridorPerimeterCell(HashSet<IntVec3> painted, IntVec3 c)
        {
            if (!painted.Contains(c + IntVec3.North)
                || !painted.Contains(c + IntVec3.South)
                || !painted.Contains(c + IntVec3.East)
                || !painted.Contains(c + IntVec3.West))
                return true;
            return false;
        }

        private static void AppendSortedUniqueRails(
            List<KeyValuePair<float, IntVec3>> raw, HashSet<IntVec3> dest, bool cardinalBuckets)
        {
            if (raw == null || raw.Count == 0) return;
            raw.Sort((a, b) => a.Key.CompareTo(b.Key));
            if (!cardinalBuckets)
            {
                IntVec3 prev = IntVec3.Invalid;
                for (int i = 0; i < raw.Count; i++)
                {
                    IntVec3 c = raw[i].Value;
                    if (c == prev) continue;
                    dest.Add(c);
                    prev = c;
                }
                return;
            }

            // One cell per unit-t bucket (bridge cardinal style).
            float tMin = raw[0].Key;
            float tMax = raw[raw.Count - 1].Key;
            int buckets = Mathf.Max(8, Mathf.CeilToInt(tMax - tMin) + 1);
            var best = new IntVec3[buckets];
            for (int i = 0; i < buckets; i++)
                best[i] = IntVec3.Invalid;
            for (int i = 0; i < raw.Count; i++)
            {
                int b = Mathf.Clamp(Mathf.FloorToInt(raw[i].Key - tMin), 0, buckets - 1);
                // Prefer extremes already in list order — keep first extreme per bucket by |t| side magnitude unused.
                if (!best[b].IsValid)
                    best[b] = raw[i].Value;
            }
            IntVec3 prevB = IntVec3.Invalid;
            for (int b = 0; b < buckets; b++)
            {
                if (!best[b].IsValid || best[b] == prevB) continue;
                dest.Add(best[b]);
                prevB = best[b];
            }
        }

        private static float SampleLateralFloat(int[] lateral, float t)
        {
            if (lateral == null || lateral.Length == 0) return 0f;
            // Distance from hub — same wiggle on both half-rays of an undirected axis.
            float fi = Mathf.Abs(t);
            int i0 = Mathf.Clamp(Mathf.FloorToInt(fi), 0, lateral.Length - 1);
            int i1 = Mathf.Clamp(Mathf.CeilToInt(fi), 0, lateral.Length - 1);
            if (i0 == i1) return lateral[i0];
            float frac = Mathf.Clamp01(fi - i0);
            return Mathf.Lerp(lateral[i0], lateral[i1], frac);
        }

        public enum WdOctant
        {
            North = 0, NorthEast = 1, East = 2, SouthEast = 3,
            South = 4, SouthWest = 5, West = 6, NorthWest = 7
        }

        public static WdOctant OctantFromLocalAxes(float localX, float localZ)
        {
            // 0° = east, 90° = north → shift so 0 = north.
            float deg = Mathf.Atan2(localX, localZ) * Mathf.Rad2Deg; // 0 = north, + = east
            if (deg < 0f) deg += 360f;
            int bucket = Mathf.RoundToInt(deg / 45f) % 8;
            return (WdOctant)bucket;
        }

        private static bool OctantPrefersHigh(int octant)
        {
            var o = (WdOctant)Mathf.Clamp(octant, 0, 7);
            return o == WdOctant.North || o == WdOctant.NorthEast || o == WdOctant.East || o == WdOctant.NorthWest;
        }

        private static Rot4 DominantCardinalFromOctant(int octant)
        {
            switch ((WdOctant)Mathf.Clamp(octant, 0, 7))
            {
                case WdOctant.North:
                case WdOctant.NorthEast:
                case WdOctant.NorthWest:
                    return Rot4.North;
                case WdOctant.South:
                case WdOctant.SouthEast:
                case WdOctant.SouthWest:
                    return Rot4.South;
                case WdOctant.East:
                    return Rot4.East;
                default:
                    return Rot4.West;
            }
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
                if (!Rand.Chance(0.55f)) continue;
                int next = cur + (Rand.Bool ? 1 : -1);
                if (next < -1 || next > 1) continue;
                cur = next;
                nextAllowedAt = i + Rand.RangeInclusive(LateralStepMinSpacing, LateralStepMaxSpacing);
            }
            return offsets;
        }

        private static int SampleLateral(int[] lateral, int t)
        {
            if (lateral == null || lateral.Length == 0) return 0;
            return lateral[Mathf.Clamp(t, 0, lateral.Length - 1)];
        }

        private static void ClearCellForTrenchWork(IntVec3 c, Map map)
        {
            List<Thing> things = c.GetThingList(map);
            for (int i = things.Count - 1; i >= 0; i--)
            {
                Thing t = things[i];
                if (t == null || t.Destroyed) continue;
                if (t is Pawn) continue;
                // SteamGeyser and other non-destroyables log red errors if Destroy is called.
                if (t.def == null || !t.def.destroyable) continue;
                if (t.def.category == ThingCategory.Plant
                    || t.def.category == ThingCategory.Item
                    || t.def.category == ThingCategory.Building
                    || t.def.category == ThingCategory.Filth
                    || t is Mineable
                    || (t.def.building != null && t.def.building.isNaturalRock))
                {
                    t.Destroy(DestroyMode.Vanish);
                }
            }
        }

        private static ThingDef ResolveMostCommonStoneBlocks(Map map)
        {
            ThingDef rock = KcsgRockTypeRemapper.FindDominantNaturalRock(map);
            if (rock != null)
            {
                ThingDef blocks = DefDatabase<ThingDef>.GetNamedSilentFail("Blocks" + rock.defName);
                if (blocks != null) return blocks;
            }

            if (map != null && Find.World != null)
            {
                foreach (ThingDef worldRock in Find.World.NaturalRockTypesIn(map.Tile))
                {
                    if (worldRock?.building == null || !worldRock.building.isNaturalRock) continue;
                    ThingDef blocks = DefDatabase<ThingDef>.GetNamedSilentFail("Blocks" + worldRock.defName);
                    if (blocks != null) return blocks;
                }
            }

            return ThingDefOf.BlocksGranite
                ?? DefDatabase<ThingDef>.GetNamedSilentFail("BlocksGranite");
        }

        private static void PlaceRailFortificationsForArm(
            Map map,
            IntVec3 center,
            TrenchArm arm,
            HashSet<IntVec3> railCells,
            ThingDef clothStuff,
            ThingDef stoneStuff)
        {
            ThingDef sandbags = ThingDefOf.Sandbags
                ?? DefDatabase<ThingDef>.GetNamedSilentFail("Sandbags");
            ThingDef barricade = ThingDefOf.Barricade
                ?? DefDatabase<ThingDef>.GetNamedSilentFail("Barricade");
            if (sandbags == null && barricade == null) return;
            if (railCells == null || railCells.Count == 0) return;

            Vector2 centerV = new Vector2(center.x + 0.5f, center.z + 0.5f);
            float tMin = float.MaxValue;
            float tMax = float.MinValue;
            float midCross = 0f;
            int n = 0;
            foreach (IntVec3 c in railCells)
            {
                float t = Vector2.Dot(new Vector2(c.x + 0.5f, c.z + 0.5f) - centerV, arm.Along);
                if (t < tMin) tMin = t;
                if (t > tMax) tMax = t;
                midCross += Vector2.Dot(new Vector2(c.x + 0.5f, c.z + 0.5f) - centerV, arm.Perp);
                n++;
            }
            midCross /= Mathf.Max(1, n);

            int maskLen = Mathf.Max(8, Mathf.CeilToInt(tMax - tMin) + 4);
            bool[] openings = BuildRailOpeningMask(maskLen);

            var sideLo = new List<IntVec3>();
            var sideHi = new List<IntVec3>();
            foreach (IntVec3 c in railCells)
            {
                float side = Vector2.Dot(new Vector2(c.x + 0.5f, c.z + 0.5f) - centerV, arm.Perp);
                if (side <= midCross) sideLo.Add(c);
                else sideHi.Add(c);
            }

            sideLo.Sort((a, b) =>
                Vector2.Dot(new Vector2(a.x + 0.5f, a.z + 0.5f) - centerV, arm.Along)
                    .CompareTo(Vector2.Dot(new Vector2(b.x + 0.5f, b.z + 0.5f) - centerV, arm.Along)));
            sideHi.Sort((a, b) =>
                Vector2.Dot(new Vector2(a.x + 0.5f, a.z + 0.5f) - centerV, arm.Along)
                    .CompareTo(Vector2.Dot(new Vector2(b.x + 0.5f, b.z + 0.5f) - centerV, arm.Along)));

            PlaceRailSidePatternAlongArm(map, sideLo, openings, centerV, arm.Along, tMin, sandbags, barricade, clothStuff, stoneStuff);
            PlaceRailSidePatternAlongArm(map, sideHi, openings, centerV, arm.Along, tMin, sandbags, barricade, clothStuff, stoneStuff);
        }

        /// <summary>
        /// Per-side pattern: sandbag runs avg ~3 (hard cap 6) with forced breaks so segments
        /// do not merge; barricades appear often but never as a contiguous run longer than 2.
        /// Openings indexed by t along the arm axis.
        /// </summary>
        private static void PlaceRailSidePatternAlongArm(
            Map map,
            List<IntVec3> sorted,
            bool[] openings,
            Vector2 centerV,
            Vector2 along,
            float tMin,
            ThingDef sandbags,
            ThingDef barricade,
            ThingDef clothStuff,
            ThingDef stoneStuff)
        {
            if (sorted == null || sorted.Count == 0) return;
            int inset = 2;
            int i = 0;
            while (i < sorted.Count)
            {
                IntVec3 c = sorted[i];
                if (!c.InBounds(map)
                    || c.x < inset || c.z < inset
                    || c.x >= map.Size.x - inset || c.z >= map.Size.z - inset)
                {
                    i++;
                    continue;
                }

                int alongIdx = Mathf.Clamp(
                    Mathf.FloorToInt(Vector2.Dot(new Vector2(c.x + 0.5f, c.z + 0.5f) - centerV, along) - tMin),
                    0,
                    openings.Length - 1);
                if (openings[alongIdx])
                {
                    i++;
                    continue;
                }
                if (c.GetFirstBuilding(map) != null)
                {
                    i++;
                    continue;
                }

                float roll = Rand.Value;
                if (roll < 0.12f)
                {
                    i += Rand.RangeInclusive(1, 2);
                    continue;
                }

                if (roll < 0.48f && barricade != null
                    && !RailCellIsBarricade(map, sorted, i - 1, barricade))
                {
                    int bRun = Rand.Chance(0.40f) ? 2 : 1;
                    for (int k = 0; k < bRun && i < sorted.Count; k++, i++)
                    {
                        IntVec3 cell = sorted[i];
                        int a = Mathf.Clamp(
                            Mathf.FloorToInt(Vector2.Dot(new Vector2(cell.x + 0.5f, cell.z + 0.5f) - centerV, along) - tMin),
                            0,
                            openings.Length - 1);
                        if (openings[a])
                            break;
                        if (!cell.InBounds(map) || cell.GetFirstBuilding(map) != null)
                            continue;
                        TryPlaceRailPiece(barricade, stoneStuff, cell, map);
                    }
                    if (i < sorted.Count)
                        i++;
                    continue;
                }

                int run = SampleSandbagRunLength();
                for (int k = 0; k < run && i < sorted.Count; k++, i++)
                {
                    IntVec3 cell = sorted[i];
                    int a = Mathf.Clamp(
                        Mathf.FloorToInt(Vector2.Dot(new Vector2(cell.x + 0.5f, cell.z + 0.5f) - centerV, along) - tMin),
                        0,
                        openings.Length - 1);
                    if (openings[a])
                        break;
                    if (!cell.InBounds(map) || cell.GetFirstBuilding(map) != null)
                        continue;
                    if (sandbags != null)
                        TryPlaceRailPiece(sandbags, clothStuff, cell, map);
                }

                if (i < sorted.Count)
                {
                    IntVec3 sep = sorted[i];
                    int a = Mathf.Clamp(
                        Mathf.FloorToInt(Vector2.Dot(new Vector2(sep.x + 0.5f, sep.z + 0.5f) - centerV, along) - tMin),
                        0,
                        openings.Length - 1);
                    bool placedSepBarricade = false;
                    if (!openings[a]
                        && sep.InBounds(map) && sep.GetFirstBuilding(map) == null
                        && barricade != null && Rand.Chance(0.55f)
                        && !RailCellIsBarricade(map, sorted, i - 1, barricade))
                    {
                        TryPlaceRailPiece(barricade, stoneStuff, sep, map);
                        placedSepBarricade = true;
                    }
                    i++;
                    if (placedSepBarricade && i < sorted.Count)
                        i++;
                }
            }
        }

        private static bool RailCellIsBarricade(Map map, List<IntVec3> sorted, int index, ThingDef barricade)
        {
            if (map == null || barricade == null || sorted == null || index < 0 || index >= sorted.Count)
                return false;
            IntVec3 cell = sorted[index];
            if (!cell.InBounds(map)) return false;
            Thing b = cell.GetFirstBuilding(map);
            return b != null && !b.Destroyed && b.def == barricade;
        }

        /// <summary>Sandbag segment length: hard max 6, mean ≈ 3.</summary>
        private static int SampleSandbagRunLength()
        {
            int len = Mathf.RoundToInt(Rand.Gaussian(3f, 1.2f));
            return Mathf.Clamp(len, 1, 6);
        }

        private static void TryPlaceRailPiece(ThingDef def, ThingDef preferredStuff, IntVec3 c, Map map)
        {
            if (def == null) return;
            ThingDef stuff = null;
            if (def.MadeFromStuff)
            {
                stuff = preferredStuff;
                if (stuff == null || stuff.stuffProps == null || !stuff.stuffProps.CanMake(def))
                    stuff = GenStuff.DefaultStuffFor(def);
            }
            TrySpawnBuilding(def, stuff, c, map);
        }

        /// <summary>True = leave rail empty (3–5 tile openings between solid stretches).</summary>
        private static bool[] BuildRailOpeningMask(int alongLen)
        {
            var open = new bool[Mathf.Max(1, alongLen)];
            int t = Rand.RangeInclusive(RailSolidMin / 2, RailSolidMax);
            while (t < alongLen - RailOpeningMin)
            {
                int gap = Rand.RangeInclusive(RailOpeningMin, RailOpeningMax);
                int end = Mathf.Min(alongLen, t + gap);
                for (int i = t; i < end; i++)
                    open[i] = true;
                t = end + Rand.RangeInclusive(RailSolidMin, RailSolidMax);
            }
            return open;
        }

        private static void FillTrenchCells(Map map, List<IntVec3> trenchFill, IntVec3 center)
        {
            ThingDef trench = DefDatabase<ThingDef>.GetNamedSilentFail("Tr_TrenchDirt");
            if (trench == null)
            {
                Log.WarningOnce(
                    "[TSA WD] Tr_TrenchDirt missing (Oomby Trenches); trench clash corridor has rails only.",
                    "TSA_WD_TrenchClash_MissingTrTrenchDirt".GetHashCode());
                return;
            }

            int maskLen = Mathf.Max(map.Size.x, map.Size.z);
            TerrainDef duckboards = DefDatabase<TerrainDef>.GetNamedSilentFail("Tr_Duckboards");
            float[] boardStrength = duckboards != null
                ? BuildDuckboardStrengthMask(maskLen)
                : null;

            for (int i = 0; i < trenchFill.Count; i++)
            {
                IntVec3 c = trenchFill[i];
                if (!c.InBounds(map) || c.GetFirstBuilding(map) != null) continue;
                ThingDef stuff = trench.MadeFromStuff ? GenStuff.DefaultStuffFor(trench) : null;
                if (!TrySpawnBuilding(trench, stuff, c, map)) continue;
                if (boardStrength == null) continue;

                int along = Mathf.Abs(c.x - center.x) + Mathf.Abs(c.z - center.z);
                if (along < 0 || along >= boardStrength.Length) continue;
                float strength = boardStrength[along];
                if (strength <= 0f) continue;
                if (strength >= 1f || Rand.Chance(strength))
                    map.terrainGrid.SetTerrain(c, duckboards);
            }
        }

        /// <summary>
        /// Per-along-cell board strength: 1 = solid section, 0 = dirt gap, (0,1) = fuzzy section ends.
        /// </summary>
        private static float[] BuildDuckboardStrengthMask(int alongLen)
        {
            var mask = new float[Mathf.Max(1, alongLen)];
            int t = Rand.RangeInclusive(0, Mathf.Max(0, DuckboardGapMax));
            while (t < alongLen)
            {
                int sectionLen = Rand.RangeInclusive(DuckboardSectionMin, DuckboardSectionMax);
                int end = Mathf.Min(alongLen, t + sectionLen);
                int fuzz = Mathf.Min(DuckboardFuzzCells, Mathf.Max(0, (end - t - 1) / 2));
                for (int i = t; i < end; i++)
                {
                    int fromStart = i - t;
                    int fromEnd = end - 1 - i;
                    if (fromStart < fuzz)
                        mask[i] = Mathf.Lerp(0.35f, 1f, (fromStart + 1f) / (fuzz + 1f));
                    else if (fromEnd < fuzz)
                        mask[i] = Mathf.Lerp(0.35f, 1f, (fromEnd + 1f) / (fuzz + 1f));
                    else
                        mask[i] = 1f;
                }
                t = end + Rand.RangeInclusive(DuckboardGapMin, DuckboardGapMax);
            }
            return mask;
        }

        private static ThingDef ResolveApproachStrandDef(out bool isTrap)
        {
            isTrap = false;
            if (WdMedievalMode.Active)
            {
                ThingDef spike = DefDatabase<ThingDef>.GetNamedSilentFail("DankPyon_CavalrySpike");
                if (spike == null) return null;
                isTrap = spike.building != null && spike.building.isTrap;
                return spike;
            }

            ThingDef wire = DefDatabase<ThingDef>.GetNamedSilentFail("Tr_BarbedWire")
                ?? DefDatabase<ThingDef>.GetNamedSilentFail("AncientRazorWire");
            return wire;
        }

        private static ThingDef ResolveApproachStuff(ThingDef def)
        {
            if (def == null || !def.MadeFromStuff) return null;
            return GenStuff.DefaultStuffFor(def);
        }

        /// <summary>
        /// One approach strand per flank, parallel to the arm (fixed standoff; WireRun/WireGap cadence).
        /// Returns how many buildings were placed.
        /// </summary>
        private static int SpawnApproachStrandsOnArm(
            Map map,
            IntVec3 center,
            TrenchArm arm,
            int[] lateral,
            HashSet<IntVec3> nestReserved,
            HashSet<IntVec3> corridorPainted,
            ThingDef approachDef,
            bool approachIsTrap,
            HashSet<IntVec3> placedCells)
        {
            if (approachDef == null) return 0;
            ThingDef stuff = ResolveApproachStuff(approachDef);
            int minBeyond = CorridorWidth / 2 + 1;
            int placed = 0;
            placed += SpawnApproachStrandOnFlank(
                map, center, arm, lateral, nestReserved, corridorPainted,
                approachDef, stuff, +1, minBeyond, placedCells);
            placed += SpawnApproachStrandOnFlank(
                map, center, arm, lateral, nestReserved, corridorPainted,
                approachDef, stuff, -1, minBeyond, placedCells);
            return placed;
        }

        private static int SpawnApproachStrandOnFlank(
            Map map,
            IntVec3 center,
            TrenchArm arm,
            int[] lateral,
            HashSet<IntVec3> nestReserved,
            HashSet<IntVec3> corridorPainted,
            ThingDef def,
            ThingDef stuff,
            int flankSign,
            int minBeyond,
            HashSet<IntVec3> placedCells)
        {
            Vector2 centerV = new Vector2(center.x + 0.5f, center.z + 0.5f);
            // Full undirected axis through the map (both half-rays), not center→edge only.
            int halfLen = Mathf.CeilToInt(Mathf.Sqrt(map.Size.x * map.Size.x + map.Size.z * map.Size.z));
            int tLo = -(halfLen - 2);
            int tHi = halfLen - 2;
            int beyondRail = minBeyond + 1;
            int wireLatOff = 0;
            int nextCurveAt = tLo + Rand.RangeInclusive(WireCurveMinSpacing, WireCurveMaxSpacing);
            int step = Mathf.Max(1, def.Size.x, def.Size.z);
            int placed = 0;

            int t = tLo + Rand.RangeInclusive(0, WireGapMax);
            while (t < tHi)
            {
                int runLen = Rand.RangeInclusive(WireRunMin, WireRunMax);
                int runEnd = Mathf.Min(tHi, t + runLen);
                for (; t < runEnd; t += step)
                {
                    float latWiggle = SampleLateralFloat(lateral, t);
                    if (t >= nextCurveAt)
                    {
                        int next = wireLatOff + (Rand.Bool ? 1 : -1);
                        if (next >= -1 && next <= 1 && beyondRail + next >= minBeyond)
                            wireLatOff = next;
                        nextCurveAt = t + Rand.RangeInclusive(WireCurveMinSpacing, WireCurveMaxSpacing);
                    }

                    float lat = latWiggle + flankSign * (beyondRail + wireLatOff);
                    Vector2 pos = centerV + arm.Along * t + arm.Perp * lat;
                    IntVec3 c = new IntVec3(Mathf.FloorToInt(pos.x), 0, Mathf.FloorToInt(pos.y));
                    if (TryPlaceApproachCell(def, stuff, map, c, arm, nestReserved, corridorPainted, placedCells))
                        placed++;
                }
                t += Rand.RangeInclusive(WireGapMin, WireGapMax);
            }
            return placed;
        }

        private static bool TryPlaceApproachCell(
            ThingDef def,
            ThingDef stuff,
            Map map,
            IntVec3 c,
            TrenchArm arm,
            HashSet<IntVec3> nestReserved,
            HashSet<IntVec3> corridorPainted,
            HashSet<IntVec3> placedCells)
        {
            if (!c.InBounds(map)) return false;
            if (corridorPainted != null && corridorPainted.Contains(c)) return false;
            if (nestReserved != null && nestReserved.Contains(c)) return false;
            if (placedCells != null && placedCells.Contains(c)) return false;

            Rot4 rot = Rot4.North;
            if (def.rotatable)
                rot = Rot4.FromAngleFlat(arm.SnappedDirectedDeg);

            CellRect rect = GenAdj.OccupiedRect(c, rot, def.Size);
            foreach (IntVec3 p in rect)
            {
                if (!p.InBounds(map)
                    || (corridorPainted != null && corridorPainted.Contains(p))
                    || (nestReserved != null && nestReserved.Contains(p))
                    || (placedCells != null && placedCells.Contains(p))
                    || p.GetFirstBuilding(map) != null)
                    return false;
            }

            foreach (IntVec3 p in rect)
                ClearCellForTrenchWork(p, map);

            if (!TrySpawnBuilding(def, stuff, c, map, def.rotatable ? rot : (Rot4?)null))
                return false;

            foreach (IntVec3 p in rect)
                placedCells?.Add(p);
            return true;
        }

        /// <summary>
        /// Wire-only corner thicken: when diagonal wire neighbors form a stair, fill both orthogonal
        /// sides of the 2×2 (W E / W W / E W family). Trap approach defs skip this.
        /// </summary>
        private static void FillWireCornerPattern(
            Map map,
            ThingDef wire,
            HashSet<IntVec3> wireCells,
            HashSet<IntVec3> corridorPainted,
            HashSet<IntVec3> nestReserved)
        {
            if (wire == null || wireCells == null || wireCells.Count == 0) return;
            if (wire.building != null && wire.building.isTrap) return;

            ThingDef stuff = ResolveApproachStuff(wire);
            var toAdd = new List<IntVec3>();

            foreach (IntVec3 c in wireCells)
            {
                // Diagonal pair (c) — (c+E+N): fill both sides.
                IntVec3 ne = c + IntVec3.East + IntVec3.North;
                if (wireCells.Contains(ne))
                {
                    TryQueueCornerFill(c + IntVec3.East, wireCells, toAdd);
                    TryQueueCornerFill(c + IntVec3.North, wireCells, toAdd);
                }
                IntVec3 nw = c + IntVec3.West + IntVec3.North;
                if (wireCells.Contains(nw))
                {
                    TryQueueCornerFill(c + IntVec3.West, wireCells, toAdd);
                    TryQueueCornerFill(c + IntVec3.North, wireCells, toAdd);
                }
            }

            for (int i = 0; i < toAdd.Count; i++)
            {
                IntVec3 c = toAdd[i];
                if (!c.InBounds(map)) continue;
                if (corridorPainted != null && corridorPainted.Contains(c)) continue;
                if (nestReserved != null && nestReserved.Contains(c)) continue;
                if (wireCells.Contains(c)) continue;
                if (c.GetFirstBuilding(map) != null) continue;
                ClearCellForTrenchWork(c, map);
                if (TrySpawnBuilding(wire, stuff, c, map))
                    wireCells.Add(c);
            }
        }

        private static void TryQueueCornerFill(IntVec3 cell, HashSet<IntVec3> existing, List<IntVec3> toAdd)
        {
            if (existing.Contains(cell)) return;
            if (!toAdd.Contains(cell))
                toAdd.Add(cell);
        }

        private static void SpawnTankTrapsNearArm(
            Map map,
            IntVec3 center,
            TrenchArm arm,
            int[] lateral,
            HashSet<IntVec3> nestReserved,
            HashSet<IntVec3> corridorPainted)
        {
            ThingDef tankDef = DefDatabase<ThingDef>.GetNamedSilentFail("VVE_TankTrap");
            if (tankDef == null) return;

            ThingDef steel = ThingDefOf.Steel;
            ThingDef stuff = tankDef.MadeFromStuff ? steel : null;
            if (stuff != null && (stuff.stuffProps == null || !stuff.stuffProps.CanMake(tankDef)))
                stuff = GenStuff.DefaultStuffFor(tankDef);

            Vector2 centerV = new Vector2(center.x + 0.5f, center.z + 0.5f);
            int halfLen = Mathf.CeilToInt(Mathf.Sqrt(map.Size.x * map.Size.x + map.Size.z * map.Size.z));
            int tLo = -(halfLen - 5);
            int tHi = halfLen - 5;
            if (tHi <= tLo) tHi = tLo + 1;
            int target = Rand.RangeInclusive(TankTrapTargetMin, TankTrapTargetMax);
            int placed = 0;
            int attempts = 0;
            int maxAttempts = target * 30;
            var occupied = new HashSet<IntVec3>();

            while (placed < target && attempts < maxAttempts)
            {
                attempts++;
                int t = Rand.RangeInclusive(tLo, tHi);
                float latWiggle = SampleLateralFloat(lateral, t);
                int flankSign = Rand.Bool ? 1 : -1;
                int dist = Rand.RangeInclusive(TankTrapDistMin, TankTrapDistMax);
                Vector2 pos = centerV + arm.Along * t + arm.Perp * (latWiggle + flankSign * dist);
                IntVec3 anchor = new IntVec3(Mathf.FloorToInt(pos.x), 0, Mathf.FloorToInt(pos.y));
                if (!anchor.InBounds(map)) continue;
                if (corridorPainted != null && corridorPainted.Contains(anchor)) continue;

                Rot4 rot = Rot4.Random;
                CellRect rect = GenAdj.OccupiedRect(anchor, rot, tankDef.size);
                bool ok = true;
                foreach (IntVec3 c in rect)
                {
                    if (!c.InBounds(map)
                        || occupied.Contains(c)
                        || (corridorPainted != null && corridorPainted.Contains(c))
                        || (nestReserved != null && nestReserved.Contains(c))
                        || c.GetFirstBuilding(map) != null)
                    {
                        ok = false;
                        break;
                    }
                }
                if (!ok) continue;

                foreach (IntVec3 c in rect)
                    ClearCellForTrenchWork(c, map);

                if (!TrySpawnBuilding(tankDef, stuff, anchor, map, rot))
                    continue;

                foreach (IntVec3 c in rect)
                    occupied.Add(c);
                placed++;
            }
        }

        private static bool TrySpawnBuilding(ThingDef def, ThingDef stuff, IntVec3 c, Map map, Rot4? rot = null)
        {
            if (def == null || !c.InBounds(map)) return false;
            try
            {
                Thing thing = def.MadeFromStuff && stuff != null
                    ? ThingMaker.MakeThing(def, stuff)
                    : ThingMaker.MakeThing(def);
                GenSpawn.Spawn(thing, c, map, rot ?? Rot4.North, WipeMode.Vanish);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static IntVec3 ResolveHostileSpawnCell(Map map, MapComponent_WdTrenchClashLayout layout)
        {
            CellRect whole = CellRect.WholeMap(map);
            int margin = 2;
            int band = 6;
            WdOctant o = (WdOctant)Mathf.Clamp(layout.HostileOctant, 0, 7);

            int highZ = whole.maxZ - (band - 1);
            int highX = whole.maxX - (band - 1);
            int lowZ = whole.minZ + 1;
            int lowX = whole.minX + 1;
            int midW = Mathf.Max(1, whole.Width - margin * 2);
            int midH = Mathf.Max(1, whole.Height - margin * 2);

            switch (o)
            {
                case WdOctant.North:
                    return FindStandableInRect(map, new CellRect(whole.minX + margin, highZ, midW, band));
                case WdOctant.South:
                    return FindStandableInRect(map, new CellRect(whole.minX + margin, lowZ, midW, band));
                case WdOctant.East:
                    return FindStandableInRect(map, new CellRect(highX, whole.minZ + margin, band, midH));
                case WdOctant.West:
                    return FindStandableInRect(map, new CellRect(lowX, whole.minZ + margin, band, midH));
                case WdOctant.NorthEast:
                    return FindStandableInRect(map, new CellRect(highX, highZ, band, band));
                case WdOctant.SouthEast:
                    return FindStandableInRect(map, new CellRect(highX, lowZ, band, band));
                case WdOctant.SouthWest:
                    return FindStandableInRect(map, new CellRect(lowX, lowZ, band, band));
                case WdOctant.NorthWest:
                default:
                    return FindStandableInRect(map, new CellRect(lowX, highZ, band, band));
            }
        }

        private static IntVec3 FindStandableInRect(Map map, CellRect rect)
        {
            rect = rect.ClipInsideMap(map);
            for (int n = 0; n < 100; n++)
            {
                IntVec3 c = rect.RandomCell;
                if (c.InBounds(map) && c.Standable(map))
                    return c;
            }
            foreach (IntVec3 c in rect)
            {
                if (c.InBounds(map) && c.Standable(map))
                    return c;
            }
            return rect.CenterCell;
        }

        private static IntVec3 ResolvePlayerStartNearCenter(Map map, List<IntVec3> trenchFill, IntVec3 center)
        {
            if (trenchFill != null && trenchFill.Count > 0)
            {
                IntVec3 best = IntVec3.Invalid;
                int bestDist = int.MaxValue;
                for (int i = 0; i < trenchFill.Count; i++)
                {
                    IntVec3 c = trenchFill[i];
                    if (!c.InBounds(map) || c.Fogged(map) || !c.Standable(map)) continue;
                    int d = (c - center).LengthHorizontalSquared;
                    if (d >= bestDist) continue;
                    bestDist = d;
                    best = c;
                }
                if (best.IsValid) return best;
            }
            if (TryPickStandableTrenchCell(map, trenchFill, out IntVec3 cell))
                return cell;
            return center;
        }

        public static bool TryPickStandableTrenchCell(Map map, List<IntVec3> trenchFill, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            if (map == null || trenchFill == null || trenchFill.Count == 0) return false;

            for (int n = 0; n < Mathf.Min(120, trenchFill.Count * 2); n++)
            {
                IntVec3 c = trenchFill[Rand.Range(0, trenchFill.Count)];
                if (!c.InBounds(map) || c.Fogged(map)) continue;
                if (!c.Standable(map)) continue;
                cell = c;
                return true;
            }

            for (int i = 0; i < trenchFill.Count; i++)
            {
                IntVec3 c = trenchFill[i];
                if (c.InBounds(map) && !c.Fogged(map) && c.Standable(map))
                {
                    cell = c;
                    return true;
                }
            }
            return false;
        }

        public static bool TryFindStandableNear(Map map, IntVec3 root, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            if (map == null || !root.IsValid) return false;
            if (root.InBounds(map) && root.Standable(map))
            {
                cell = root;
                return true;
            }
            return CellFinder.TryFindRandomCellNear(root, map, 8, c => c.Standable(map), out cell);
        }

        /// <summary>Interior trench cells when present; otherwise full corridor band.</summary>
        public static List<IntVec3> GetPlayerSpawnPool(MapComponent_WdTrenchClashLayout layout)
        {
            if (layout == null) return null;
            if (layout.TrenchFillCells != null && layout.TrenchFillCells.Count > 0)
                return layout.TrenchFillCells;
            return layout.CorridorCells;
        }

        /// <summary>
        /// Apply bridge-style approach polarity to an existing trench layout (post-gen / Ambush reuse).
        /// Prefer <paramref name="playerFromTile"/> (hostile = opposite), else <paramref name="hostileFromTile"/>.
        /// </summary>
        public static void PreferHostileEdgeFromApproach(
            Map map,
            MapComponent_WdTrenchClashLayout layout,
            int hostileFromTile,
            int playerFromTile = -1)
        {
            if (map == null || layout == null || !map.Tile.Valid) return;

            int octant = ResolveHostileOctantFromApproaches(map.Tile.tileId, playerFromTile, hostileFromTile);
            // Rand fallback inside Resolve when both unknown — skip rewriting a painted octant with noise.
            if (playerFromTile < 0 && hostileFromTile < 0) return;

            layout.HostileOctant = octant;
            layout.HostileFromNorthOrEast = OctantPrefersHigh(layout.HostileOctant);
            layout.HostileSpawnCell = ResolveHostileSpawnCell(map, layout);
        }

        public static bool TryGetHostileEdgeSpawn(Map map, out IntVec3 cell, out Rot4 rot)
        {
            cell = IntVec3.Invalid;
            rot = Rot4.North;
            var layout = map?.GetComponent<MapComponent_WdTrenchClashLayout>();
            if (layout == null) return false;

            IntVec3 root = layout.HostileSpawnCell;
            if (!root.IsValid)
                root = ResolveHostileSpawnCell(map, layout);
            if (!TryFindStandableNear(map, root, out cell))
                return false;

            rot = Rot4.FromAngleFlat((map.Center - cell).ToVector3Shifted().AngleFlat());
            return cell.IsValid;
        }

        /// <summary>Spawn pawns directly onto standable trench cells (no drop pods).</summary>
        public static void SpawnPawnsSpreadInTrench(IReadOnlyList<Pawn> pawns, Map map)
        {
            if (pawns == null || pawns.Count == 0 || map == null) return;

            var layout = map.GetComponent<MapComponent_WdTrenchClashLayout>();
            List<IntVec3> pool = GetPlayerSpawnPool(layout) ?? new List<IntVec3>();
            var standable = new List<IntVec3>(pool.Count);
            for (int i = 0; i < pool.Count; i++)
            {
                IntVec3 c = pool[i];
                if (c.InBounds(map) && c.Standable(map) && !c.Fogged(map))
                    standable.Add(c);
            }
            standable.Shuffle();

            IntVec3 jump = IntVec3.Invalid;
            int idx = 0;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn == null || pawn.Destroyed || pawn.Dead) continue;
                if (pawn.Spawned) pawn.DeSpawn();
                pawn.holdingOwner?.Remove(pawn);

                IntVec3 cell;
                if (idx < standable.Count)
                    cell = standable[idx++];
                else if (!CellFinder.TryFindRandomCellNear(
                             layout != null && layout.PlayerStartCell.IsValid ? layout.PlayerStartCell : map.Center,
                             map,
                             12,
                             c => c.Standable(map) && !c.Fogged(map),
                             out cell))
                    cell = map.Center;

                try
                {
                    GenSpawn.Spawn(pawn, cell, map);
                    TryDraftTrenchColonist(pawn);
                    jump = cell;
                }
                catch (System.Exception ex)
                {
                    Log.Warning($"[TSA WD] Trench clash spawn failed for {pawn.LabelShortCap}: {ex.Message}");
                }
            }

            if (jump.IsValid)
                CameraJumper.TryJump(new GlobalTargetInfo(jump, map));
        }

        /// <summary>Draft player humanlikes placed into the trench (combat-ready on map open).</summary>
        public static void TryDraftTrenchColonist(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead || pawn.Downed || !pawn.Spawned) return;
            if (pawn.drafter == null) return;
            if (pawn.Faction != Faction.OfPlayer) return;
            if (pawn.RaceProps?.Humanlike != true) return;
            if (VehicleFrameworkOutpostDissolveCompat.IsVehicleFrameworkVehiclePawn(pawn)) return;
            pawn.drafter.Drafted = true;
        }
    }

    public class MapComponent_WdTrenchClashLayout : MapComponent
    {
        public List<IntVec3> CorridorCells = new List<IntVec3>();
        /// <summary>Interior cells between rails (player spawn pool).</summary>
        public List<IntVec3> TrenchFillCells = new List<IntVec3>();
        public List<Rot4> ArmDirections = new List<Rot4>();
        public bool IsHorizontal;
        /// <summary>When horizontal: true = north edge; when vertical: true = east edge (legacy).</summary>
        public bool HostileFromNorthOrEast;
        /// <summary>0–7 = N,NE,E,SE,S,SW,W,NW for EdgeWalkIn bands.</summary>
        public int HostileOctant;
        /// <summary>When true, hostile edge was fixed at gen to match MG nest facing — do not flip from traveler approach.</summary>
        public bool HostileEdgeLockedByMgNests;
        public IntVec3 PlayerStartCell;
        public IntVec3 HostileSpawnCell;

        // Legacy fields kept for save compatibility with earlier trench-clash layouts.
        public IntVec3 PlayerEnd;
        public IntVec3 HostileEnd;

        public MapComponent_WdTrenchClashLayout(Map map) : base(map) { }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref CorridorCells, "corridorCells", LookMode.Value);
            Scribe_Collections.Look(ref TrenchFillCells, "trenchFillCells", LookMode.Value);
            Scribe_Values.Look(ref IsHorizontal, "isHorizontal", false);
            Scribe_Values.Look(ref HostileFromNorthOrEast, "hostileFromNorthOrEast", false);
            Scribe_Values.Look(ref HostileOctant, "hostileOctant", 0);
            Scribe_Values.Look(ref HostileEdgeLockedByMgNests, "hostileEdgeLockedByMgNests", false);
            Scribe_Values.Look(ref PlayerStartCell, "playerStartCell");
            Scribe_Values.Look(ref HostileSpawnCell, "hostileSpawnCell");
            Scribe_Values.Look(ref PlayerEnd, "playerEnd");
            Scribe_Values.Look(ref HostileEnd, "hostileEnd");
            if (CorridorCells == null) CorridorCells = new List<IntVec3>();
            if (ArmDirections == null) ArmDirections = new List<Rot4>();
            if (TrenchFillCells == null) TrenchFillCells = new List<IntVec3>();
            if (Scribe.mode == LoadSaveMode.PostLoadInit
                && TrenchFillCells.Count == 0
                && CorridorCells.Count > 0)
            {
                // Old saves only had CorridorCells; treat them as the spawn pool.
                TrenchFillCells.AddRange(CorridorCells);
            }
        }
    }
}
