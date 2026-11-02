using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// SSoT for player trench arms: exclusive undirected edges owned by an outpost or colony Settlement.
    /// Parallel to forts/roads — not stored as <see cref="RoadDef"/> links.
    /// </summary>
    public class WorldComponent_WdTrenches : WorldComponent
    {
        public const int MaxBuildRadiusTiles = 8;
        public const int WoodPerSegment = 30;
        public const float DefMovementFlatPenalty = 0.5f;

        /// <summary>Outer berm / lip (widest; ~50% darker than the first timber berm).</summary>
        public static readonly Color BermColor = new Color(0.19f, 0.12f, 0.06f, 1f);
        /// <summary>Warm timber underlayer (also darkened with the berm stack).</summary>
        public static readonly Color TimberColor = new Color(0.26f, 0.18f, 0.09f, 1f);
        /// <summary>Dark ditch center (narrower, drawn on top).</summary>
        public static readonly Color DitchColor = new Color(0.12f, 0.10f, 0.08f, 1f);
        // Another ~30% narrower pass.
        public const float BermWidth = 0.466f;
        public const float TimberWidth = 0.382f;
        public const float DitchWidth = 0.270f;
        public const float DistortionFrequency = 0.75f;
        public const float DistortionIntensity = 0.12f;

        private List<WdTrenchEdge> edges = new List<WdTrenchEdge>();
        private List<int> edgeOwnerIds = new List<int>();

        private Dictionary<int, int> tileToOwnerId = new Dictionary<int, int>();
        private Dictionary<int, List<int>> neighborsByTile = new Dictionary<int, List<int>>();
        private Dictionary<int, WorldObject> ownerById = new Dictionary<int, WorldObject>();
        private HashSet<int> trenchTiles = new HashSet<int>();
        private bool indexDirty = true;

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
            if (!WorldRendererUtility.WorldRendered) return;
            if (!PlanetSurfaceWorldActions.IsPlanetSurfaceLayerSelected()) return;
            // Keep decorations visible a bit farther out than fort overlays; hairline Xs vanish otherwise.
            if (WD_WorldMapZoomUtil.IsZoomedTooFarOut(WD_WorldMapZoomUtil.TravelerPathHideAltitudePercent))
                return;
            DrawTrenchDecorations();
        }

        /// <summary>
        /// Hard wood sleepers spanning berm-to-berm, plus barbed Xs just outside the lips.
        /// Boards and Xs are staggered along the edge so they do not sit on the same stations.
        /// </summary>
        private void DrawTrenchDecorations()
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || edges == null || edges.Count == 0) return;

            Material wireMat = WorldOverlayLineMaterials.TrenchWireBrown;
            Material woodMat = WorldOverlayLineMaterials.TrenchWoodPlank;
            if (wireMat == null || woodMat == null) return;

            float tile = grid.AverageTileSize;
            // Path widths are fractions of AverageTileSize; bridge nearly full berm lip-to-lip.
            float bermHalf = tile * (BermWidth * 0.5f);
            float plankHalf = bermHalf * 0.98f;
            // Just outside the berm strip (was 0.30 — floated in open terrain).
            float outsideBerm = bermHalf + tile * 0.028f;
            float wireArm = tile * 0.058f;
            float lift = WD_WorldMapZoomUtil.GetSurfaceOverlayDrawAltitude();
            const float hardWidth = 0.165f;
            // Boards at thirds; Xs at ends + mid so they are horizontally offset from the planks.
            float[] boardTs = { 0.35f, 0.65f };
            float[] xTs = { 0.18f, 0.50f, 0.82f };

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

                for (int bi = 0; bi < boardTs.Length; bi++)
                {
                    Vector3 p = LiftedLerp(a, b, boardTs[bi], lift);
                    GenDraw.DrawWorldLineBetween(
                        p - side * plankHalf,
                        p + side * plankHalf,
                        woodMat,
                        hardWidth);
                }

                for (int xi = 0; xi < xTs.Length; xi++)
                {
                    Vector3 p = LiftedLerp(a, b, xTs[xi], lift);
                    DrawHardX(p + side * outsideBerm, along, side, wireArm, wireMat, hardWidth);
                    DrawHardX(p - side * outsideBerm, along, side, wireArm, wireMat, hardWidth);
                }
            }
        }

        private static Vector3 LiftedLerp(Vector3 a, Vector3 b, float t, float lift)
        {
            Vector3 raw = Vector3.Lerp(a, b, t);
            return raw.normalized * (raw.magnitude + lift);
        }

        private static void DrawHardX(Vector3 c, Vector3 along, Vector3 side, float arm, Material mat, float width)
        {
            Vector3 d1 = (along + side).normalized * arm;
            Vector3 d2 = (along - side).normalized * arm;
            GenDraw.DrawWorldLineBetween(c + d1, c - d1, mat, width);
            GenDraw.DrawWorldLineBetween(c + d2, c - d2, mat, width);
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
                if ((a == hub && Find.WorldGrid.IsNeighbor(a, b))
                    || (b == hub && Find.WorldGrid.IsNeighbor(a, b)))
                    return true;
                failReason = "TSA_WD_Trench_MustTouchHub".Translate();
                return false;
            }

            // New arm from hub that already has trenches: hub → empty neighbor.
            if (aExists && !bExists && a == hub)
                return true;
            if (bExists && !aExists && b == hub)
                return true;

            // Extend existing arm tip (not hub): tip degree must be 1.
            if (aExists && !bExists)
            {
                if (degA != 1 || a == hub)
                {
                    failReason = "TSA_WD_Trench_NoBranch".Translate();
                    return false;
                }
                if (tileToOwnerId.TryGetValue(a, out int oa) && oa != ownerId)
                {
                    failReason = "TSA_WD_Trench_ForeignNetwork".Translate();
                    return false;
                }
                return true;
            }
            if (bExists && !aExists)
            {
                if (degB != 1 || b == hub)
                {
                    failReason = "TSA_WD_Trench_NoBranch".Translate();
                    return false;
                }
                if (tileToOwnerId.TryGetValue(b, out int ob) && ob != ownerId)
                {
                    failReason = "TSA_WD_Trench_ForeignNetwork".Translate();
                    return false;
                }
                return true;
            }

            // Both exist: would merge/loop.
            failReason = "TSA_WD_Trench_NoMerge".Translate();
            return false;
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
            int other = nbs[0];
            return TryRemoveEdge(tipTile, other, out failReason);
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
        /// </summary>
        public void WipeEdgesTouchingTileExceptOwner(int tileId, WorldObject claimant)
        {
            if (tileId < 0 || edges == null || edges.Count == 0) return;
            int keepOwnerId = claimant != null ? claimant.ID : int.MinValue;
            bool any = false;
            for (int i = edges.Count - 1; i >= 0; i--)
            {
                WdTrenchEdge e = edges[i];
                if (e == null) continue;
                if (e.tileA != tileId && e.tileB != tileId) continue;
                if (claimant != null && i < edgeOwnerIds.Count && edgeOwnerIds[i] == keepOwnerId)
                    continue;
                edges.RemoveAt(i);
                if (i < edgeOwnerIds.Count)
                    edgeOwnerIds.RemoveAt(i);
                any = true;
            }
            if (!any) return;
            indexDirty = true;
            EnsureIndex();
            WD_WorldLayer_Trenches.SetDirty();
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
            var objs = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < objs.Count; i++)
            {
                var o = objs[i];
                if (o == null || o.Destroyed) continue;
                if (o is WorldObject_WD_Outpost || (o is Settlement s && s.Faction == Faction.OfPlayer))
                    ownerById[o.ID] = o;
            }
        }
    }
}
