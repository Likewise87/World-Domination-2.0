using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// SSoT for trench arms: exclusive undirected edges owned by a WorldObject ID.
    /// Player owners are WD outposts only. Storage stays WorldObject-typed so NPC Settlements can own later.
    /// Parallel to forts/roads — not stored as <see cref="RoadDef"/> links.
    /// Mid-tile claims (outpost establish / NPC settle) wipe incident foreign edges then
    /// <see cref="PruneDisconnectedFromHub"/> so arms severed from the hub disappear; tip-remove does the same.
    /// </summary>
    public class WorldComponent_WdTrenches : WorldComponent
    {
        public const int MaxBuildRadiusTiles = 6;
        public const int WoodPerSegment = 30;
        public const float DefMovementFlatPenalty = 0.5f;

        /// <summary>Outer berm / lip (widest; ~50% darker than the first timber berm).</summary>
        public static readonly Color BermColor = new Color(0.19f, 0.12f, 0.06f, 1f);
        /// <summary>Warm timber underlayer (also darkened with the berm stack).</summary>
        public static readonly Color TimberColor = new Color(0.26f, 0.18f, 0.09f, 1f);
        /// <summary>Dark ditch center (narrower, drawn on top).</summary>
        public static readonly Color DitchColor = new Color(0.12f, 0.10f, 0.08f, 1f);
        // Cumulative narrow passes; another −20% on all layers + low distortion.
        public const float BermWidth = 0.254f;
        public const float TimberWidth = 0.208f;
        public const float DitchWidth = 0.147f;
        public const float DistortionFrequency = 0.35f;
        public const float DistortionIntensity = 0.04f;

        private List<WdTrenchEdge> edges = new List<WdTrenchEdge>();
        private List<int> edgeOwnerIds = new List<int>();

        private Dictionary<int, int> tileToOwnerId = new Dictionary<int, int>();
        private Dictionary<int, List<int>> neighborsByTile = new Dictionary<int, List<int>>();
        private Dictionary<int, WorldObject> ownerById = new Dictionary<int, WorldObject>();
        private HashSet<int> trenchTiles = new HashSet<int>();
        private bool indexDirty = true;
        /// <summary>Scratch for wire-quad spacing (avoids corner overlaps across edges).</summary>
        private readonly List<Vector3> wirePlaceScratch = new List<Vector3>(64);

        private static WorldComponent_WdTrenches cached;
        private static World cachedWorld;

        public WorldComponent_WdTrenches(World world) : base(world)
        {
            cached = this;
            cachedWorld = world;
        }

        public static WorldComponent_WdTrenches Get()
        {
            World w = Find.World;
            if (w == null) return null;
            if (cached != null && cachedWorld == w) return cached;
            cached = w.GetComponent<WorldComponent_WdTrenches>();
            cachedWorld = w;
            return cached;
        }

        public bool HasAny => edges != null && edges.Count > 0;

        public IReadOnlyList<WdTrenchEdge> Edges
        {
            get
            {
                EnsureIndex();
                return edges;
            }
        }

        public override void FinalizeInit(bool fromLoad)
        {
            base.FinalizeInit(fromLoad);
            RebuildIndex();
            WD_WorldLayer_Trenches.EnsureRegistered();
            if (HasAny)
                WD_WorldLayer_Trenches.SetDirty();
        }

        public override void WorldComponentUpdate()
        {
            if (!FeatureEnabled || !HasAny) return;
            if (!WorldComponent_WDVisualizerToggle.ShowRoadBlocksAndTraps) return;
            if (!WorldRendererUtility.WorldRendered) return;
            if (!PlanetSurfaceWorldActions.IsPlanetSurfaceLayerSelected()) return;
            // Keep decorations visible a bit farther out than fort overlays; hairline Xs vanish otherwise.
            if (WD_WorldMapZoomUtil.IsZoomedTooFarOut(WD_WorldMapZoomUtil.TravelerPathHideAltitudePercent))
                return;
            DrawTrenchDecorations();
        }

        /// <summary>
        /// Bridge.png sleepers across the ditch (rotated per edge), plus irregular barbed-wire quads outside the berms.
        /// Wire positions are deterministic-random per edge and globally spaced so corner turns do not stack.
        /// </summary>
        private void DrawTrenchDecorations()
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || edges == null || edges.Count == 0) return;

            Material bridgeMat = WorldOverlayLineMaterials.TrenchBridge;
            if (bridgeMat == null) return;

            // Modern barbed-wire texture — suppressed in medieval (clash maps use MO cavalry spikes).
            Material wireMat = WdMedievalMode.Active
                ? null
                : WorldOverlayLineMaterials.TrenchBarbedWire;

            float tile = grid.AverageTileSize;
            float bermHalf = tile * (BermWidth * 0.5f);
            // Sit clearly outside the berm lip (was +0.028 tile — hugged the fill).
            float outsideBerm = bermHalf + tile * 0.085f;
            float lift = WD_WorldMapZoomUtil.GetSurfaceOverlayDrawAltitude();
            // AT baseline ≈ 1.0 tile-fraction; wire at 30%.
            float wireSize = WD_WorldMapZoomUtil.GetSurfaceOverlayQuadSize(0.27f);
            // Bridge spans the berm strip (same role as the old sleeper line).
            float bridgeSize = tile * BermWidth * 1.05f;
            // Min gap +20% vs prior 0.9× wireSize (keeps max spacing feel, kills bend crowding).
            float minSepSqr = wireSize * 1.08f;
            minSepSqr *= minSepSqr;
            // Keep wire clear of the bridge along the edge (t-space exclusion).
            const float boardAvoidHalf = 0.16f;
            const float uprightRotation = -90f;

            wirePlaceScratch.Clear();

            for (int i = 0; i < edges.Count; i++)
            {
                WdTrenchEdge e = edges[i];
                if (e == null || e.tileA < 0 || e.tileB < 0) continue;
                if (!grid.InBounds(e.tileA) || !grid.InBounds(e.tileB)) continue;

                Vector3 a = grid.GetTileCenter(e.tileA);
                Vector3 b = grid.GetTileCenter(e.tileB);
                Vector3 along = b - a;
                if (along.sqrMagnitude < 0.0001f) continue;
                along.Normalize();

                Vector3 midRaw = Vector3.Lerp(a, b, 0.5f);
                Vector3 mid = midRaw.normalized * (midRaw.magnitude + lift);
                Vector3 side = Vector3.Cross(mid.normalized, along).normalized;
                if (side.sqrMagnitude < 0.0001f) continue;

                // One bridge near mid; rotated so the texture spans across the trench (follows edge turns).
                float boardT = 0.42f + Hash01(e.tileA, e.tileB, 11) * 0.16f;
                Vector3 bridgePos = Vector3.Lerp(a, b, boardT);
                float bridgeRot = TangentialRotationDeg(bridgePos, side) + 90f;
                WorldRendererUtility.DrawQuadTangentialToPlanet(
                    bridgePos, bridgeSize, lift, bridgeMat, bridgeRot);

                if (wireMat == null) continue;

                // Irregular wire: usually 1 per berm side (2 is rarer); stay off endpoints/bridge/peers.
                for (int sideSign = -1; sideSign <= 1; sideSign += 2)
                {
                    int count = Hash01(e.tileA, e.tileB, 20 + sideSign) < 0.72f ? 1 : 2;
                    for (int k = 0; k < count; k++)
                    {
                        float t;
                        if (count == 1)
                            t = 0.36f + Hash01(e.tileA, e.tileB, 30 + sideSign * 3 + k) * 0.28f; // 0.36–0.64
                        else if (k == 0)
                            t = 0.32f + Hash01(e.tileA, e.tileB, 40 + sideSign) * 0.10f; // 0.32–0.42
                        else
                            t = 0.58f + Hash01(e.tileA, e.tileB, 50 + sideSign) * 0.10f; // 0.58–0.68

                        // Nudge off the bridge if the roll landed near it.
                        if (Mathf.Abs(t - boardT) < boardAvoidHalf)
                        {
                            t = t < boardT
                                ? Mathf.Max(0.30f, boardT - boardAvoidHalf)
                                : Mathf.Min(0.70f, boardT + boardAvoidHalf);
                        }
                        if (Mathf.Abs(t - boardT) < boardAvoidHalf * 0.85f)
                            continue;

                        float bermJitter = (Hash01(e.tileA, e.tileB, 60 + sideSign * 5 + k) - 0.5f) * tile * 0.05f;
                        Vector3 raw = Vector3.Lerp(a, b, t);
                        Vector3 pos = raw + side * (sideSign * (outsideBerm + bermJitter));
                        if (IsTooCloseToPlaced(wirePlaceScratch, pos, minSepSqr))
                            continue;
                        wirePlaceScratch.Add(pos);
                        WorldRendererUtility.DrawQuadTangentialToPlanet(
                            pos, wireSize, lift, wireMat, uprightRotation);
                    }
                }
            }
        }

        /// <summary>
        /// Degrees around the planet radial so a tangential quad's reference axis aligns with
        /// <paramref name="desiredForward"/> (projected onto the tangent plane).
        /// </summary>
        private static float TangentialRotationDeg(Vector3 pos, Vector3 desiredForward)
        {
            Vector3 normal = pos.normalized;
            Vector3 desired = Vector3.ProjectOnPlane(desiredForward, normal);
            if (desired.sqrMagnitude < 1e-8f) return -90f;
            desired.Normalize();

            Vector3 north = Vector3.ProjectOnPlane(Vector3.up, normal);
            if (north.sqrMagnitude < 1e-8f)
                north = Vector3.ProjectOnPlane(Vector3.forward, normal);
            if (north.sqrMagnitude < 1e-8f) return -90f;
            north.Normalize();
            return Vector3.SignedAngle(north, desired, normal);
        }

        private static bool IsTooCloseToPlaced(List<Vector3> placed, Vector3 pos, float minSepSqr)
        {
            for (int i = 0; i < placed.Count; i++)
            {
                if ((placed[i] - pos).sqrMagnitude < minSepSqr)
                    return true;
            }
            return false;
        }

        /// <summary>Stable 0–1 hash from edge endpoints + salt (no per-frame flicker).</summary>
        private static float Hash01(int a, int b, int salt)
        {
            unchecked
            {
                uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663) ^ (uint)(salt * 83492791);
                h ^= h >> 13;
                h *= 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFu) / 65535f;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref edges, "wdTrenchEdges", LookMode.Deep);
            Scribe_Collections.Look(ref edgeOwnerIds, "wdTrenchEdgeOwnerIds", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (edges == null) edges = new List<WdTrenchEdge>();
                if (edgeOwnerIds == null) edgeOwnerIds = new List<int>();
                while (edgeOwnerIds.Count < edges.Count)
                    edgeOwnerIds.Add(-1);
                if (edgeOwnerIds.Count > edges.Count)
                    edgeOwnerIds.RemoveRange(edges.Count, edgeOwnerIds.Count - edges.Count);
                RebuildIndex();
            }
        }

        public static bool FeatureEnabled =>
            WorldDominationMod.settings?.enableTrenches
            ?? WorldDominationSettings.DefEnableTrenches;

        public static bool HasTrenchAt(int tileId)
        {
            var c = Get();
            if (c == null) return false;
            c.EnsureIndex();
            return c.trenchTiles.Contains(tileId);
        }

        public static bool HasTrenchAt(PlanetTile tile) =>
            tile.Valid && HasTrenchAt(tile.tileId);

        /// <summary>
        /// True when a trench occupies a tile for exclusivity vs roads/forts/etc.
        /// Owner hub tiles may host trench roots visually while still allowing roads in other directions.
        /// </summary>
        public static bool HasExclusiveTrenchAt(int tileId)
        {
            if (!HasTrenchAt(tileId)) return false;
            if (TryGetOwnerAt(tileId, out WorldObject owner) && owner != null && !owner.Destroyed
                && owner.Tile.tileId == tileId)
                return false;
            return true;
        }

        public static bool IsOwnerHubTile(int tileId)
        {
            return TryGetOwnerAt(tileId, out WorldObject owner)
                && owner != null
                && !owner.Destroyed
                && owner.Tile.tileId == tileId;
        }

        public static float GetFlatPenalty(int tileId)
        {
            if (!HasTrenchAt(tileId)) return 0f;
            // Hub roots are drawn but do not apply the trench movement tax on the settlement/outpost tile.
            if (IsOwnerHubTile(tileId)) return 0f;
            return DefMovementFlatPenalty;
        }

        public bool TryGetOwner(int tileId, out WorldObject owner)
        {
            owner = null;
            EnsureIndex();
            if (!tileToOwnerId.TryGetValue(tileId, out int id) || id < 0) return false;
            if (!ownerById.TryGetValue(id, out owner) || owner == null || owner.Destroyed)
            {
                ResolveOwners();
                return ownerById.TryGetValue(id, out owner) && owner != null && !owner.Destroyed;
            }
            return true;
        }

        public static bool TryGetOwnerAt(int tileId, out WorldObject owner)
        {
            owner = null;
            var c = Get();
            return c != null && c.TryGetOwner(tileId, out owner);
        }

        public bool TryGetNeighbors(int tileId, List<int> into)
        {
            into?.Clear();
            EnsureIndex();
            if (!neighborsByTile.TryGetValue(tileId, out var list) || list == null) return false;
            if (into != null)
                into.AddRange(list);
            return list.Count > 0;
        }

        public int Degree(int tileId)
        {
            EnsureIndex();
            return neighborsByTile.TryGetValue(tileId, out var list) && list != null ? list.Count : 0;
        }

        public bool HasEdge(int a, int b)
        {
            if (a < 0 || b < 0 || a == b) return false;
            EnsureIndex();
            return neighborsByTile.TryGetValue(a, out var list) && list != null && list.Contains(b);
        }

        /// <summary>Refuse if tile is unsuitable for a new trench segment endpoint (exclusive + passable land).</summary>
        public static AcceptanceReport CanUseTileForTrench(int tileId, WorldObject hub, bool allowHubNeighborOnlyCheck = false)
        {
            if (!FeatureEnabled)
                return "TSA_WD_Trench_Disabled".Translate();
            if (tileId < 0 || Find.WorldGrid == null)
                return false;
            var tile = Find.WorldGrid[tileId];
            if (tile == null)
                return false;
            if (tile.WaterCovered)
                return "TSA_WD_Trench_NeedPassableLand".Translate();
            if (Find.World.Impassable(tileId))
                return "TSA_WD_Trench_NeedPassableLand".Translate();

            // Origin cell is a valid trench root (first leg starts here), like road paint from a settlement.
            bool isHub = hub != null && hub.Tile.tileId == tileId;
            if (isHub)
                return AcceptanceReport.WasAccepted;

            if (HasTrenchAt(tileId))
                return "TSA_WD_Trench_AlreadyTrench".Translate();

            // Exclusive off-hub: no roads, forts, WOs (except travelers / own hub).
            if (TileHasRoadLink(tileId))
                return "TSA_WD_Trench_BlockedByRoad".Translate();
            if (WorldActions_Fortifications.HasFortificationAt(tileId))
                return "TSA_WD_Trench_BlockedByFort".Translate();
            if (WorldComponent_WdBridges.IsBridgedWaterTile(tileId))
                return "TSA_WD_Trench_BlockedByBridge".Translate();

            if (Find.WorldObjects != null)
            {
                foreach (WorldObject o in Find.WorldObjects.ObjectsAt(tileId))
                {
                    if (o == null || o.Destroyed) continue;
                    if (o is WorldObject_Traveler) continue;
                    if (hub != null && o == hub) continue;
                    if (o is Settlement || o is WorldObject_WD_Outpost || o is MapParent)
                        return "TSA_WD_Trench_BlockedByWorldObject".Translate(o.LabelCap);
                    if (o is WorldObject_AT_Turret || o.def?.defName != null && o.def.defName.Contains("Advanced"))
                        return "TSA_WD_Trench_BlockedByWorldObject".Translate(o.LabelCap);
                }
            }

            if (hub != null)
            {
                float dist = Find.WorldGrid.ApproxDistanceInTiles(hub.Tile, new PlanetTile(tileId));
                if (dist > MaxBuildRadiusTiles + 0.01f)
                    return "TSA_WD_Trench_OutOfRange".Translate(MaxBuildRadiusTiles.ToString());
            }

            return AcceptanceReport.WasAccepted;
        }

        public static bool TileHasRoadLink(int tileId)
        {
            if (Find.WorldGrid == null || tileId < 0 || tileId >= Find.WorldGrid.TilesCount) return false;
            if (Find.WorldGrid[tileId] is not SurfaceTile st) return false;
            return st.potentialRoads != null && st.potentialRoads.Count > 0;
        }

        /// <summary>True if any other build system must refuse this tile because a trench occupies it.</summary>
        public static AcceptanceReport RefuseIfTrench(int tileId)
        {
            if (!HasExclusiveTrenchAt(tileId)) return AcceptanceReport.WasAccepted;
            return "TSA_WD_Trench_TileReserved".Translate();
        }

        public bool TryAddEdge(int tileA, int tileB, WorldObject owner, out string failReason)
        {
            failReason = null;
            if (owner == null || owner.Destroyed)
            {
                failReason = "TSA_WD_Trench_NoOwner".Translate();
                return false;
            }
            // Player trenches are outpost-only. NPC Settlements may own later; refuse player Settlement hubs.
            Faction player = Faction.OfPlayerSilentFail;
            if (player != null && owner.Faction == player && owner is not WorldObject_WD_Outpost)
            {
                failReason = "TSA_WD_Trench_OutpostOnly".Translate();
                return false;
            }
            if (!FeatureEnabled)
            {
                failReason = "TSA_WD_Trench_Disabled".Translate();
                return false;
            }
            if (tileA < 0 || tileB < 0 || tileA == tileB)
            {
                failReason = "TSA_WD_Trench_BadEdge".Translate();
                return false;
            }
            if (!Find.WorldGrid.IsNeighbor(tileA, tileB))
            {
                failReason = "TSA_WD_Trench_NotNeighbors".Translate();
                return false;
            }

            int hub = owner.Tile.tileId;
            EnsureIndex();
            if (HasEdge(tileA, tileB))
            {
                failReason = "TSA_WD_Trench_AlreadyTrench".Translate();
                return false;
            }

            var checkA = CanUseTileForTrench(tileA, owner);
            // Existing trench tile is OK when extending the tip / hub.
            if (!checkA.Accepted && !HasTrenchAt(tileA))
            {
                failReason = checkA.Reason;
                return false;
            }
            var checkB = CanUseTileForTrench(tileB, owner);
            if (!checkB.Accepted && !HasTrenchAt(tileB))
            {
                failReason = checkB.Reason;
                return false;
            }

            int ownerId = owner.ID;
            // Foreign owner refuse (hub of this owner is fine).
            if (HasTrenchAt(tileA) && tileToOwnerId.TryGetValue(tileA, out int oa) && oa != ownerId)
            {
                failReason = "TSA_WD_Trench_ForeignNetwork".Translate();
                return false;
            }
            if (HasTrenchAt(tileB) && tileToOwnerId.TryGetValue(tileB, out int ob) && ob != ownerId)
            {
                failReason = "TSA_WD_Trench_ForeignNetwork".Translate();
                return false;
            }

            // Polyline arms rooted at hub (first leg must include hub). Hub may host multiple arms.
            if (!IsLegalPolylineExtension(tileA, tileB, hub, ownerId, out failReason))
                return false;

            edges.Add(new WdTrenchEdge(tileA, tileB));
            edgeOwnerIds.Add(ownerId);
            ownerById[ownerId] = owner;
            indexDirty = true;
            EnsureIndex();
            WD_WorldLayer_Trenches.SetDirty();
            return true;
        }

        /// <summary>Max incident edges at a non-hub trench tile (T-junction = 3; X = 4 forbidden).</summary>
        public const int MaxArmDegree = 3;

        private bool IsLegalPolylineExtension(int a, int b, int hub, int ownerId, out string failReason)
        {
            failReason = null;
            EnsureIndex();
            int degA = Degree(a);
            int degB = Degree(b);
            bool aExists = HasTrenchAt(a);
            bool bExists = HasTrenchAt(b);

            // First segment of a new arm: must be hub ↔ empty neighbor.
            if (!aExists && !bExists)
            {
                if (a == hub && Find.WorldGrid.IsNeighbor(a, b))
                    return TryAcceptNewEmptyTile(b, a, ownerId, out failReason);
                if (b == hub && Find.WorldGrid.IsNeighbor(a, b))
                    return TryAcceptNewEmptyTile(a, b, ownerId, out failReason);
                failReason = "TSA_WD_Trench_MustTouchHub".Translate();
                return false;
            }

            // New arm from hub that already has trenches: hub → empty neighbor.
            if (aExists && !bExists && a == hub)
                return TryAcceptNewEmptyTile(b, a, ownerId, out failReason);
            if (bExists && !aExists && b == hub)
                return TryAcceptNewEmptyTile(a, b, ownerId, out failReason);

            // Extend or T-branch from owned arm tile (not hub): degree 1 (tip) or 2 (mid → T).
            if (aExists && !bExists)
                return TryAcceptBranchOrExtend(a, b, hub, ownerId, degA, out failReason);
            if (bExists && !aExists)
                return TryAcceptBranchOrExtend(b, a, hub, ownerId, degB, out failReason);

            // Both exist: would merge/loop.
            failReason = "TSA_WD_Trench_NoMerge".Translate();
            return false;
        }

        private bool TryAcceptBranchOrExtend(
            int fromExisting, int toEmpty, int hub, int ownerId, int fromDeg, out string failReason)
        {
            failReason = null;
            if (fromExisting == hub)
            {
                failReason = "TSA_WD_Trench_NoBranch".Translate();
                return false;
            }
            if (tileToOwnerId.TryGetValue(fromExisting, out int oa) && oa != ownerId)
            {
                failReason = "TSA_WD_Trench_ForeignNetwork".Translate();
                return false;
            }
            // After this edge, fromExisting degree becomes fromDeg+1; must stay ≤ MaxArmDegree.
            if (fromDeg < 1 || fromDeg >= MaxArmDegree)
            {
                failReason = fromDeg >= MaxArmDegree
                    ? "TSA_WD_Trench_MaxDegree".Translate()
                    : "TSA_WD_Trench_NoBranch".Translate();
                return false;
            }
            return TryAcceptNewEmptyTile(toEmpty, fromExisting, ownerId, out failReason);
        }

        /// <summary>
        /// New empty trench tile must keep ≥1 empty tile between non-incident arms.
        /// Planet-neighbors that are already graph-neighbors of <paramref name="fromTile"/> are allowed
        /// (same junction on a hex grid); other owned trench neighbors are not.
        /// </summary>
        private bool TryAcceptNewEmptyTile(int newTile, int fromTile, int ownerId, out string failReason)
        {
            failReason = null;
            if (!ArmSeparationOk(newTile, fromTile, ownerId))
            {
                failReason = "TSA_WD_Trench_ArmTooClose".Translate();
                return false;
            }
            return true;
        }

        /// <summary>Public for path planning so UI rejects illegal T digs before dispatch.</summary>
        public bool WouldViolateArmSeparation(int newTile, int fromTile, int ownerId) =>
            !ArmSeparationOk(newTile, fromTile, ownerId);

        private bool ArmSeparationOk(int newTile, int fromTile, int ownerId)
        {
            EnsureIndex();
            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return true;

            // Tiles already linked to the dig point are the same junction, not a parallel arm.
            var incident = new HashSet<int> { fromTile };
            if (neighborsByTile.TryGetValue(fromTile, out List<int> fromNbs) && fromNbs != null)
            {
                for (int i = 0; i < fromNbs.Count; i++)
                    incident.Add(fromNbs[i]);
            }

            var neighbors = new List<PlanetTile>(8);
            grid.GetTileNeighbors(new PlanetTile(newTile), neighbors);
            for (int i = 0; i < neighbors.Count; i++)
            {
                int n = neighbors[i].tileId;
                if (incident.Contains(n)) continue;
                if (!HasTrenchAt(n)) continue;
                if (tileToOwnerId.TryGetValue(n, out int oid) && oid == ownerId)
                    return false;
            }
            return true;
        }

        /// <summary>Remove tip edge of an arm that includes tipTile (degree-1 trench tile; never the hub).</summary>
        public bool TryRemoveTipAt(int tipTile, out string failReason)
        {
            failReason = null;
            EnsureIndex();
            if (!HasTrenchAt(tipTile))
            {
                failReason = "TSA_WD_Trench_NoTrench".Translate();
                return false;
            }
            if (IsOwnerHubTile(tipTile))
            {
                failReason = "TSA_WD_Trench_RemoveTipOnly".Translate();
                return false;
            }
            if (Degree(tipTile) != 1)
            {
                failReason = "TSA_WD_Trench_RemoveTipOnly".Translate();
                return false;
            }
            if (!neighborsByTile.TryGetValue(tipTile, out var nbs) || nbs == null || nbs.Count != 1)
            {
                failReason = "TSA_WD_Trench_RemoveTipOnly".Translate();
                return false;
            }
            TryGetOwnerAt(tipTile, out WorldObject owner);
            int other = nbs[0];
            if (!TryRemoveEdge(tipTile, other, out failReason))
                return false;
            // Tip-clearing must not leave distal orphans (and cleans legacy disconnected arms).
            if (owner != null && !owner.Destroyed)
                PruneDisconnectedFromHub(owner);
            return true;
        }

        /// <summary>
        /// True when <paramref name="tileId"/> is the owner hub or reachable from the hub along that owner's edges.
        /// </summary>
        public bool IsConnectedToOwnerHub(int tileId, WorldObject owner)
        {
            if (owner == null || owner.Destroyed || tileId < 0) return false;
            int hub = owner.Tile.tileId;
            if (hub < 0) return false;
            if (tileId == hub) return true;
            EnsureIndex();
            CollectOwnerReachableFromHub(owner.ID, hub, reachableScratch);
            return reachableScratch.Contains(tileId);
        }

        public static bool IsConnectedToOwnerHubStatic(int tileId, WorldObject owner)
        {
            var c = Get();
            return c != null && c.IsConnectedToOwnerHub(tileId, owner);
        }

        /// <summary>
        /// Deletes this owner's edges that are not in the undirected component connected to the hub tile.
        /// </summary>
        public void PruneDisconnectedFromHub(WorldObject owner)
        {
            if (owner == null || owner.Destroyed) return;
            PruneDisconnectedFromHub(owner.ID, owner.Tile.tileId);
            WorldActions_Trenches.NotifyOwnerNetworkPruned(owner);
        }

        public void PruneDisconnectedFromHub(int ownerId, int hubTile)
        {
            if (ownerId < 0 || hubTile < 0 || edges == null || edges.Count == 0) return;
            EnsureIndex();
            CollectOwnerReachableFromHub(ownerId, hubTile, reachableScratch);

            bool any = false;
            for (int i = edges.Count - 1; i >= 0; i--)
            {
                if (i >= edgeOwnerIds.Count || edgeOwnerIds[i] != ownerId) continue;
                WdTrenchEdge e = edges[i];
                if (e == null)
                {
                    edges.RemoveAt(i);
                    edgeOwnerIds.RemoveAt(i);
                    any = true;
                    continue;
                }
                if (reachableScratch.Contains(e.tileA) && reachableScratch.Contains(e.tileB))
                    continue;
                edges.RemoveAt(i);
                edgeOwnerIds.RemoveAt(i);
                any = true;
            }
            if (!any) return;
            indexDirty = true;
            EnsureIndex();
            WD_WorldLayer_Trenches.SetDirty();
        }

        private readonly HashSet<int> reachableScratch = new HashSet<int>();
        private readonly Dictionary<int, List<int>> ownerAdjScratch = new Dictionary<int, List<int>>();
        private readonly Stack<int> bfsScratch = new Stack<int>();

        private void CollectOwnerReachableFromHub(int ownerId, int hubTile, HashSet<int> into)
        {
            into.Clear();
            ownerAdjScratch.Clear();
            for (int i = 0; i < edges.Count; i++)
            {
                if (i >= edgeOwnerIds.Count || edgeOwnerIds[i] != ownerId) continue;
                WdTrenchEdge e = edges[i];
                if (e == null || e.tileA < 0 || e.tileB < 0) continue;
                AddOwnerAdj(e.tileA, e.tileB);
                AddOwnerAdj(e.tileB, e.tileA);
            }

            bfsScratch.Clear();
            into.Add(hubTile);
            bfsScratch.Push(hubTile);
            while (bfsScratch.Count > 0)
            {
                int t = bfsScratch.Pop();
                if (!ownerAdjScratch.TryGetValue(t, out List<int> ns) || ns == null) continue;
                for (int i = 0; i < ns.Count; i++)
                {
                    int n = ns[i];
                    if (into.Add(n))
                        bfsScratch.Push(n);
                }
            }
        }

        private void AddOwnerAdj(int from, int to)
        {
            if (!ownerAdjScratch.TryGetValue(from, out List<int> list))
            {
                list = new List<int>(2);
                ownerAdjScratch[from] = list;
            }
            if (!list.Contains(to))
                list.Add(to);
        }

        public bool TryRemoveEdge(int a, int b, out string failReason)
        {
            failReason = null;
            var key = new WdTrenchEdge(a, b);
            for (int i = 0; i < edges.Count; i++)
            {
                if (edges[i] == null) continue;
                if (edges[i].tileA == key.tileA && edges[i].tileB == key.tileB)
                {
                    edges.RemoveAt(i);
                    if (i < edgeOwnerIds.Count)
                        edgeOwnerIds.RemoveAt(i);
                    indexDirty = true;
                    EnsureIndex();
                    WD_WorldLayer_Trenches.SetDirty();
                    return true;
                }
            }
            failReason = "TSA_WD_Trench_NoTrench".Translate();
            return false;
        }

        public void WipeOwnedBy(WorldObject owner)
        {
            if (owner == null) return;
            WipeOwnedById(owner.ID);
        }

        /// <summary>
        /// Path B type-change: keep trench edges, retarget owner id from old shell to the replacement outpost.
        /// </summary>
        public void RemapOwnerId(int oldOwnerId, WorldObject newOwner)
        {
            if (oldOwnerId < 0 || newOwner == null || newOwner.Destroyed) return;
            int newId = newOwner.ID;
            if (newId < 0 || oldOwnerId == newId) return;
            if (edgeOwnerIds == null || edgeOwnerIds.Count == 0) return;

            bool any = false;
            for (int i = 0; i < edgeOwnerIds.Count; i++)
            {
                if (edgeOwnerIds[i] == oldOwnerId)
                {
                    edgeOwnerIds[i] = newId;
                    any = true;
                }
            }
            if (!any) return;

            ownerById.Remove(oldOwnerId);
            ownerById[newId] = newOwner;
            indexDirty = true;
            EnsureIndex();
            WD_WorldLayer_Trenches.SetDirty();
        }

        public void WipeOwnedById(int ownerId)
        {
            if (ownerId < 0 || edges == null) return;
            bool any = false;
            for (int i = edges.Count - 1; i >= 0; i--)
            {
                if (i < edgeOwnerIds.Count && edgeOwnerIds[i] == ownerId)
                {
                    edges.RemoveAt(i);
                    edgeOwnerIds.RemoveAt(i);
                    any = true;
                }
            }
            if (any)
            {
                indexDirty = true;
                EnsureIndex();
                WD_WorldLayer_Trenches.SetDirty();
            }
        }

        public void WipeAll()
        {
            edges.Clear();
            edgeOwnerIds.Clear();
            indexDirty = true;
            EnsureIndex();
            WD_WorldLayer_Trenches.SetDirty();
        }

        /// <summary>Remove every trench edge that touches <paramref name="tileId"/> (MapParent claims tile).</summary>
        public void WipeEdgesTouchingTile(int tileId)
        {
            WipeEdgesTouchingTileExceptOwner(tileId, null);
        }

        /// <summary>
        /// Removes trench edges touching <paramref name="tileId"/>. When <paramref name="claimant"/> is set,
        /// edges owned by that world object are kept (so colony/outpost SpawnSetup does not erase hub roots).
        /// Then prunes each foreign owner so arms severed from their hub are wiped.
        /// </summary>
        public void WipeEdgesTouchingTileExceptOwner(int tileId, WorldObject claimant)
        {
            if (tileId < 0 || edges == null || edges.Count == 0) return;
            int keepOwnerId = claimant != null ? claimant.ID : int.MinValue;
            bool any = false;
            HashSet<int> affectedOwners = null;
            for (int i = edges.Count - 1; i >= 0; i--)
            {
                WdTrenchEdge e = edges[i];
                if (e == null) continue;
                if (e.tileA != tileId && e.tileB != tileId) continue;
                if (claimant != null && i < edgeOwnerIds.Count && edgeOwnerIds[i] == keepOwnerId)
                    continue;
                if (i < edgeOwnerIds.Count)
                {
                    int oid = edgeOwnerIds[i];
                    if (oid != keepOwnerId)
                    {
                        affectedOwners ??= new HashSet<int>();
                        affectedOwners.Add(oid);
                    }
                    edgeOwnerIds.RemoveAt(i);
                }
                edges.RemoveAt(i);
                any = true;
            }
            if (!any) return;
            indexDirty = true;
            EnsureIndex();
            WD_WorldLayer_Trenches.SetDirty();

            if (affectedOwners == null) return;
            foreach (int oid in affectedOwners)
            {
                if (ownerById.TryGetValue(oid, out WorldObject o) && o != null && !o.Destroyed)
                    PruneDisconnectedFromHub(o);
                else
                    WipeOwnedById(oid);
            }
        }

        /// <summary>Ordered tip→hub trench tiles for the arm containing <paramref name="tileId"/> (excludes hub).</summary>
        public bool TryGetArmTipToRoot(int tileId, int hubTile, List<int> into)
        {
            into?.Clear();
            EnsureIndex();
            if (!HasTrenchAt(tileId) || into == null) return false;

            // Walk from tip if degree 1, else from tile toward a degree-1 tip then reverse... For remove we start at tip.
            int start = tileId;
            if (Degree(tileId) != 1)
            {
                // Find a tip in this component.
                var component = new HashSet<int>();
                var stack = new Stack<int>();
                stack.Push(tileId);
                while (stack.Count > 0)
                {
                    int t = stack.Pop();
                    if (!component.Add(t)) continue;
                    if (neighborsByTile.TryGetValue(t, out var ns))
                    {
                        for (int i = 0; i < ns.Count; i++)
                            stack.Push(ns[i]);
                    }
                }
                start = -1;
                foreach (int t in component)
                {
                    if (Degree(t) == 1)
                    {
                        // Prefer tip farther from hub.
                        if (start < 0
                            || Find.WorldGrid.ApproxDistanceInTiles(new PlanetTile(t), new PlanetTile(hubTile))
                                > Find.WorldGrid.ApproxDistanceInTiles(new PlanetTile(start), new PlanetTile(hubTile)))
                            start = t;
                    }
                }
                if (start < 0) return false;
            }

            int prev = -1;
            int cur = start;
            into.Add(cur);
            while (true)
            {
                if (!neighborsByTile.TryGetValue(cur, out var ns) || ns == null || ns.Count == 0)
                    break;
                int next = -1;
                for (int i = 0; i < ns.Count; i++)
                {
                    if (ns[i] != prev)
                    {
                        next = ns[i];
                        break;
                    }
                }
                if (next < 0) break;
                // Stop before we'd need hub — root trench tile is hub-adjacent.
                prev = cur;
                cur = next;
                into.Add(cur);
                if (Find.WorldGrid.IsNeighbor(cur, hubTile) && Degree(cur) == 1 && into.Count > 1)
                {
                    // Root of arm (degree 1 only when single-tile arm). Multi-tile root has degree 1 only at tip.
                }
                if (Degree(cur) <= 1 && cur != start && Find.WorldGrid.IsNeighbor(cur, hubTile))
                    break;
                if (into.Count > 64) break;
            }
            return into.Count > 0;
        }

        private void EnsureIndex()
        {
            if (indexDirty)
                RebuildIndex();
        }

        private void RebuildIndex()
        {
            tileToOwnerId.Clear();
            neighborsByTile.Clear();
            trenchTiles.Clear();
            if (edges == null) edges = new List<WdTrenchEdge>();
            if (edgeOwnerIds == null) edgeOwnerIds = new List<int>();

            for (int i = 0; i < edges.Count; i++)
            {
                var e = edges[i];
                if (e == null || e.tileA < 0 || e.tileB < 0) continue;
                int oid = i < edgeOwnerIds.Count ? edgeOwnerIds[i] : -1;
                AddAdj(e.tileA, e.tileB);
                AddAdj(e.tileB, e.tileA);
                trenchTiles.Add(e.tileA);
                trenchTiles.Add(e.tileB);
                tileToOwnerId[e.tileA] = oid;
                tileToOwnerId[e.tileB] = oid;
            }
            ResolveOwners();
            indexDirty = false;
        }

        private void AddAdj(int from, int to)
        {
            if (!neighborsByTile.TryGetValue(from, out var list))
            {
                list = new List<int>(2);
                neighborsByTile[from] = list;
            }
            if (!list.Contains(to))
                list.Add(to);
        }

        private void ResolveOwners()
        {
            ownerById.Clear();
            if (Find.WorldObjects == null) return;
            // FinalizeInit can run before a player faction exists (world gen / early load).
            Faction player = Faction.OfPlayerSilentFail;
            var objs = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < objs.Count; i++)
            {
                var o = objs[i];
                if (o == null || o.Destroyed) continue;
                // Player trenches are outpost-owned; keep Settlement resolve only for future NPC owners / legacy IDs.
                if (o is WorldObject_WD_Outpost)
                    ownerById[o.ID] = o;
                else if (o is Settlement s && player != null && s.Faction == player)
                    ownerById[o.ID] = o;
            }
        }
    }
}
