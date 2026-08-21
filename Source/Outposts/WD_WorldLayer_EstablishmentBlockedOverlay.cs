using System.Collections;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Highlights tiles too close to settlements, WD outposts, or WD ruins for establishment (min-distance rule).
    /// Default: solid faction fill (1 blocker) or contested Voronoi spots (2–4 factions).
    /// With any productivity overlay on: dark grey single hatch so score colors stay readable.
    /// Shown when the blocked-tiles world-map toggle is on, always centered on the mouse cursor
    /// (same pattern as <see cref="WD_WorldLayer_ProductivityOverlay"/>).
    /// </summary>
    [StaticConstructorOnStartup]
    public class WD_WorldLayer_EstablishmentBlockedOverlay : WorldDrawLayer
    {
        public const int OverlayRadius = WD_WorldLayer_ProductivityOverlay.OverlayRadius;
        private const float FillSurfaceOffset = 0.013f;
        private const float HatchSurfaceOffset = 0.014f;
        private const float StripesPerTile = 6.0f;
        private const int RenderQueue = 3581;
        private const int StripeTextureSize = 64;
        private const int StripePeriod = 16;
        private const int StripeWidth = 5;
        private const float FillAlpha = 0.50f;
        private const float HatchAlpha = 0.72f;
        private const int BlockerScanDistancePad = 3;
        private const int MaxBlockerFactions = WD_ContestedVoronoiMaterials.MaxFactions;

        private static readonly Color NoFactionFillColor = new Color(0.12f, 0.12f, 0.12f, FillAlpha);
        private static readonly Color ProductivityHatchColor = new Color(0.12f, 0.12f, 0.12f, HatchAlpha);
        private static readonly Dictionary<int, Material> fillMatsByColorKey = new Dictionary<int, Material>();
        private static readonly Dictionary<int, Material> hatchMatsByColorKey = new Dictionary<int, Material>();
        private static Texture2D blockedStripeTexture;
        private static PlanetTile centerTile = PlanetTile.Invalid;

        private readonly List<Vector3> tileVerts = new List<Vector3>(8);
        private readonly List<PlanetTile> neighborTiles = new List<PlanetTile>(8);
        private readonly Queue<PlanetTile> openTiles = new Queue<PlanetTile>();
        private readonly Dictionary<int, int> distancesByTileId = new Dictionary<int, int>();
        private readonly Dictionary<int, TileBlockerSlots> tileBlockerSlots = new Dictionary<int, TileBlockerSlots>();
        private readonly List<WorldObject> nearbyBlockers = new List<WorldObject>(32);
        private readonly HashSet<int> overlayTileIds = new HashSet<int>();
        private readonly List<PlanetTile> overlayTiles = new List<PlanetTile>(128);
        private readonly List<PlanetTile> blockedOverlayTiles = new List<PlanetTile>(64);
        private readonly List<int> blockedOverlayTileIds = new List<int>(64);
        private readonly List<Faction> drawFactions = new List<Faction>(MaxBlockerFactions);

        private struct TileBlockerSlots
        {
            public bool hasPrimary;
            public Faction primary;
            public float primaryDist;
            public bool hasSecondary;
            public Faction secondary;
            public float secondaryDist;
            public bool hasTertiary;
            public Faction tertiary;
            public float tertiaryDist;
            public bool hasQuaternary;
            public Faction quaternary;
            public float quaternaryDist;
        }

        public override bool Visible => WorldComponent_WDVisualizerToggle.ShowEstablishmentBlockedOverlay;
        public override bool VisibleWhenLayerNotSelected => true;
        public override bool VisibleInBackground => false;

        public static PlanetTile CenterTile => centerTile;

        public static bool SetCenterTile(PlanetTile tile)
        {
            if (centerTile == tile) return false;
            centerTile = tile;
            return true;
        }

        private static bool UseProductivityGreyHatch =>
            WorldComponent_WDVisualizerToggle.ProductivityOverlayMode != WD_ProductivityOverlayMode.Off;

        public override IEnumerable Regenerate()
        {
            foreach (object step in base.Regenerate())
                yield return step;

            if (!WorldComponent_WDVisualizerToggle.ShowEstablishmentBlockedOverlay)
            {
                FinalizeMesh(MeshParts.All);
                yield break;
            }

            WorldGrid grid = Find.WorldGrid;
            if (grid == null || !centerTile.Valid)
            {
                FinalizeMesh(MeshParts.All);
                yield break;
            }

            CollectOverlayTiles(grid, centerTile, OverlayRadius);
            CollectBlockedOverlayTiles(grid);

            if (UseProductivityGreyHatch)
            {
                Material hatchMat = GetBlockedHatchMaterial(ProductivityHatchColor);
                for (int i = 0; i < blockedOverlayTiles.Count; i++)
                    AddHatchedTileToSubMesh(grid, blockedOverlayTiles[i], hatchMat);
            }
            else
            {
                BuildTileBlockerSlots(grid, centerTile, OverlayRadius);
                for (int i = 0; i < blockedOverlayTiles.Count; i++)
                    DrawFactionFillOrSpots(grid, blockedOverlayTiles[i]);
            }

            FinalizeMesh(MeshParts.All);
        }

        private void CollectOverlayTiles(WorldGrid grid, PlanetTile root, int radius)
        {
            overlayTileIds.Clear();
            overlayTiles.Clear();
            foreach (PlanetTile tile in EnumerateTilesInRadius(grid, root, radius))
            {
                overlayTileIds.Add(tile.tileId);
                overlayTiles.Add(tile);
            }
        }

        private void CollectBlockedOverlayTiles(WorldGrid grid)
        {
            blockedOverlayTiles.Clear();
            blockedOverlayTileIds.Clear();
            for (int i = 0; i < overlayTiles.Count; i++)
            {
                PlanetTile tile = overlayTiles[i];
                Tile tileInfo = grid[tile];
                if (tileInfo == null || tileInfo.WaterCovered)
                    continue;
                if (!Outpost_EstablishmentRequirements.IsTileBlockedByMinDistanceCached(tile.tileId))
                    continue;
                blockedOverlayTiles.Add(tile);
                blockedOverlayTileIds.Add(tile.tileId);
            }
        }

        /// <summary>
        /// Closest up to four distinct blocking factions (null faction = grey slot for ruins).
        /// Uses the same ApproxDistance &lt; minDist rule as
        /// <see cref="Outpost_EstablishmentRequirements.MeetsMinDistanceOnly"/>.
        /// </summary>
        private void BuildTileBlockerSlots(WorldGrid grid, PlanetTile root, int overlayRadius)
        {
            tileBlockerSlots.Clear();
            nearbyBlockers.Clear();

            int minDist = Outpost_EstablishmentRequirements.MinDistanceTiles;
            if (minDist <= 0 || blockedOverlayTileIds.Count == 0) return;

            int scanRadius = overlayRadius + minDist + BlockerScanDistancePad;
            var allObjs = Find.WorldObjects?.AllWorldObjects;
            if (allObjs == null) return;

            for (int i = 0; i < allObjs.Count; i++)
            {
                WorldObject o = allObjs[i];
                if (!Outpost_EstablishmentRequirements.IsEstablishmentMinDistanceBlocker(o)) continue;
                if (!o.Tile.Valid) continue;
                // Layer-correct distance: never compare bare tileIds (orbit ids collide with surface).
                if ((int)grid.ApproxDistanceInTiles(root, o.Tile) > scanRadius)
                    continue;
                nearbyBlockers.Add(o);
            }

            if (nearbyBlockers.Count == 0) return;

            for (int t = 0; t < blockedOverlayTileIds.Count; t++)
            {
                PlanetTile tile = blockedOverlayTiles[t];
                int tileId = tile.tileId;
                TileBlockerSlots slots = default;
                for (int i = 0; i < nearbyBlockers.Count; i++)
                {
                    WorldObject o = nearbyBlockers[i];
                    float dist = grid.ApproxDistanceInTiles(tile, o.Tile);
                    if ((int)dist >= minDist)
                        continue;
                    ConsiderBlocker(ref slots, o.Faction, dist);
                }
                if (slots.hasPrimary)
                    tileBlockerSlots[tileId] = slots;
            }
        }

        private static void ConsiderBlocker(ref TileBlockerSlots slots, Faction faction, float dist)
        {
            if (!slots.hasPrimary)
            {
                SetPrimary(ref slots, faction, dist);
                return;
            }

            if (SameFactionSlot(slots.primary, faction))
            {
                if (IsCloserOrBetterTie(dist, faction, slots.primaryDist, slots.primary))
                    slots.primaryDist = dist;
                return;
            }

            if (slots.hasSecondary && SameFactionSlot(slots.secondary, faction))
            {
                if (IsCloserOrBetterTie(dist, faction, slots.secondaryDist, slots.secondary))
                    slots.secondaryDist = dist;
                return;
            }

            if (slots.hasTertiary && SameFactionSlot(slots.tertiary, faction))
            {
                if (IsCloserOrBetterTie(dist, faction, slots.tertiaryDist, slots.tertiary))
                    slots.tertiaryDist = dist;
                return;
            }

            if (slots.hasQuaternary && SameFactionSlot(slots.quaternary, faction))
            {
                if (IsCloserOrBetterTie(dist, faction, slots.quaternaryDist, slots.quaternary))
                    slots.quaternaryDist = dist;
                return;
            }

            if (IsCloserOrBetterTie(dist, faction, slots.primaryDist, slots.primary))
            {
                Faction oldFaction = slots.primary;
                float oldDist = slots.primaryDist;
                SetPrimary(ref slots, faction, dist);
                InsertAsSecondary(ref slots, oldFaction, oldDist);
                return;
            }

            InsertAsSecondary(ref slots, faction, dist);
        }

        private static void InsertAsSecondary(ref TileBlockerSlots slots, Faction faction, float dist)
        {
            if (!slots.hasSecondary)
            {
                SetSecondary(ref slots, faction, dist);
                return;
            }

            if (SameFactionSlot(slots.secondary, faction))
            {
                if (IsCloserOrBetterTie(dist, faction, slots.secondaryDist, slots.secondary))
                    slots.secondaryDist = dist;
                return;
            }

            if (IsCloserOrBetterTie(dist, faction, slots.secondaryDist, slots.secondary))
            {
                Faction oldFaction = slots.secondary;
                float oldDist = slots.secondaryDist;
                SetSecondary(ref slots, faction, dist);
                InsertAsTertiary(ref slots, oldFaction, oldDist);
                return;
            }

            InsertAsTertiary(ref slots, faction, dist);
        }

        private static void InsertAsTertiary(ref TileBlockerSlots slots, Faction faction, float dist)
        {
            if (!slots.hasTertiary)
            {
                SetTertiary(ref slots, faction, dist);
                return;
            }

            if (SameFactionSlot(slots.tertiary, faction))
            {
                if (IsCloserOrBetterTie(dist, faction, slots.tertiaryDist, slots.tertiary))
                    slots.tertiaryDist = dist;
                return;
            }

            if (IsCloserOrBetterTie(dist, faction, slots.tertiaryDist, slots.tertiary))
            {
                Faction oldFaction = slots.tertiary;
                float oldDist = slots.tertiaryDist;
                SetTertiary(ref slots, faction, dist);
                InsertAsQuaternary(ref slots, oldFaction, oldDist);
                return;
            }

            InsertAsQuaternary(ref slots, faction, dist);
        }

        private static void InsertAsQuaternary(ref TileBlockerSlots slots, Faction faction, float dist)
        {
            if (!slots.hasQuaternary)
            {
                SetQuaternary(ref slots, faction, dist);
                return;
            }

            if (SameFactionSlot(slots.quaternary, faction))
            {
                if (IsCloserOrBetterTie(dist, faction, slots.quaternaryDist, slots.quaternary))
                    slots.quaternaryDist = dist;
                return;
            }

            if (IsCloserOrBetterTie(dist, faction, slots.quaternaryDist, slots.quaternary))
                SetQuaternary(ref slots, faction, dist);
        }

        private static void SetPrimary(ref TileBlockerSlots slots, Faction faction, float dist)
        {
            slots.hasPrimary = true;
            slots.primary = faction;
            slots.primaryDist = dist;
        }

        private static void SetSecondary(ref TileBlockerSlots slots, Faction faction, float dist)
        {
            slots.hasSecondary = true;
            slots.secondary = faction;
            slots.secondaryDist = dist;
        }

        private static void SetTertiary(ref TileBlockerSlots slots, Faction faction, float dist)
        {
            slots.hasTertiary = true;
            slots.tertiary = faction;
            slots.tertiaryDist = dist;
        }

        private static void SetQuaternary(ref TileBlockerSlots slots, Faction faction, float dist)
        {
            slots.hasQuaternary = true;
            slots.quaternary = faction;
            slots.quaternaryDist = dist;
        }

        private static bool SameFactionSlot(Faction a, Faction b) =>
            a == b || (a == null && b == null);

        private static bool IsCloserOrBetterTie(float dist, Faction faction, float otherDist, Faction otherFaction)
        {
            if (dist < otherDist) return true;
            if (dist > otherDist) return false;
            return FactionLoadId(faction) < FactionLoadId(otherFaction);
        }

        private static int FactionLoadId(Faction faction) => faction?.loadID ?? int.MinValue;

        private void DrawFactionFillOrSpots(WorldGrid grid, PlanetTile tile)
        {
            if (!tileBlockerSlots.TryGetValue(tile.tileId, out TileBlockerSlots slots) || !slots.hasPrimary)
            {
                AddSolidTileToSubMesh(grid, tile, GetFillMaterial(NoFactionFillColor));
                return;
            }

            drawFactions.Clear();
            drawFactions.Add(slots.primary);
            if (slots.hasSecondary && !SameFactionSlot(slots.primary, slots.secondary))
                drawFactions.Add(slots.secondary);
            if (slots.hasTertiary
                && !SameFactionSlot(slots.primary, slots.tertiary)
                && !(slots.hasSecondary && SameFactionSlot(slots.secondary, slots.tertiary)))
            {
                drawFactions.Add(slots.tertiary);
            }
            if (slots.hasQuaternary
                && !SameFactionSlot(slots.primary, slots.quaternary)
                && !(slots.hasSecondary && SameFactionSlot(slots.secondary, slots.quaternary))
                && !(slots.hasTertiary && SameFactionSlot(slots.tertiary, slots.quaternary)))
            {
                drawFactions.Add(slots.quaternary);
            }

            if (drawFactions.Count <= 1)
            {
                AddSolidTileToSubMesh(grid, tile, GetFillMaterial(ColorForFactionSlot(drawFactions[0])));
                return;
            }

            Material contested = WD_ContestedVoronoiMaterials.GetMaterial(drawFactions);
            if (contested == null)
            {
                AddSolidTileToSubMesh(grid, tile, GetFillMaterial(ColorForFactionSlot(drawFactions[0])));
                return;
            }

            AddSolidTileToSubMesh(grid, tile, contested);
        }

        private static Color ColorForFactionSlot(Faction faction)
        {
            if (faction == null)
                return NoFactionFillColor;
            Color c = faction.Color;
            c.a = FillAlpha;
            return c;
        }

        private IEnumerable<PlanetTile> EnumerateTilesInRadius(WorldGrid grid, PlanetTile root, int radius)
        {
            openTiles.Clear();
            distancesByTileId.Clear();

            openTiles.Enqueue(root);
            distancesByTileId[root.tileId] = 0;

            while (openTiles.Count > 0)
            {
                PlanetTile tile = openTiles.Dequeue();
                int distance = distancesByTileId[tile.tileId];
                yield return tile;

                if (distance >= radius) continue;

                neighborTiles.Clear();
                grid.GetTileNeighbors(tile, neighborTiles);
                for (int i = 0; i < neighborTiles.Count; i++)
                {
                    PlanetTile neighbor = neighborTiles[i];
                    if (!neighbor.Valid || distancesByTileId.ContainsKey(neighbor.tileId)) continue;
                    distancesByTileId[neighbor.tileId] = distance + 1;
                    openTiles.Enqueue(neighbor);
                }
            }
        }

        private static Material GetFillMaterial(Color color)
        {
            int key = QuantizeColorKey(color);
            if (fillMatsByColorKey.TryGetValue(key, out Material mat) && mat != null)
                return mat;
            mat = MaterialPool.MatFrom(BaseContent.WhiteTex, ShaderDatabase.MetaOverlay, color, RenderQueue);
            fillMatsByColorKey[key] = mat;
            return mat;
        }

        private static Material GetBlockedHatchMaterial(Color color)
        {
            int key = QuantizeColorKey(color);
            if (hatchMatsByColorKey.TryGetValue(key, out Material mat) && mat != null)
                return mat;
            mat = MaterialPool.MatFrom(GetBlockedStripeTexture(), ShaderDatabase.WorldOverlayTransparent, color, RenderQueue);
            hatchMatsByColorKey[key] = mat;
            return mat;
        }

        private static int QuantizeColorKey(Color color)
        {
            int r = Mathf.Clamp(Mathf.RoundToInt(color.r * 255f), 0, 255);
            int g = Mathf.Clamp(Mathf.RoundToInt(color.g * 255f), 0, 255);
            int b = Mathf.Clamp(Mathf.RoundToInt(color.b * 255f), 0, 255);
            int a = Mathf.Clamp(Mathf.RoundToInt(color.a * 255f), 0, 255);
            return (r << 24) | (g << 16) | (b << 8) | a;
        }

        private static Texture2D GetBlockedStripeTexture()
        {
            if (blockedStripeTexture != null) return blockedStripeTexture;
            int size = StripeTextureSize;
            int period = StripePeriod;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                name = "WD_EstablishmentBlockedStripe"
            };

            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int mod = (x - y) % period;
                    if (mod < 0) mod += period;
                    pixels[(y * size) + x] = mod < StripeWidth ? Color.white : Color.clear;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(false, true);
            blockedStripeTexture = tex;
            return blockedStripeTexture;
        }

        private static float GetWorldStripePhase(Vector3 unitSpherePos)
        {
            Vector3 p = unitSpherePos.normalized;
            float lon = Mathf.Atan2(p.z, p.x);
            float lat = Mathf.Asin(Mathf.Clamp(p.y, -1f, 1f));
            return (lon - lat) * GetStripeWorldScale();
        }

        private static float GetStripeWorldScale()
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return 120f;

            float uvPeriod = StripePeriod / (float)StripeTextureSize;
            float planetRadius = grid.GetTileCenter(new PlanetTile(0, grid.Surface)).magnitude;
            if (planetRadius <= 0.001f) return 120f;

            float tileArc = grid.AverageTileSize / planetRadius;
            if (tileArc <= 0.0001f) return 120f;

            float tilePhaseSpan = tileArc * 1.35f;
            return (StripesPerTile * uvPeriod) / tilePhaseSpan;
        }

        private void AddSolidTileToSubMesh(WorldGrid grid, PlanetTile tile, Material material)
        {
            tileVerts.Clear();
            grid.GetTileVertices(tile, tileVerts);
            if (tileVerts.Count < 3) return;

            LayerSubMesh subMesh = GetSubMesh(material);
            int baseIndex = subMesh.verts.Count;
            for (int i = 0; i < tileVerts.Count; i++)
            {
                Vector3 v = tileVerts[i];
                subMesh.verts.Add(v + v.normalized * FillSurfaceOffset);
                subMesh.uvs.Add((GenGeo.RegularPolygonVertexPosition(tileVerts.Count, i) + Vector2.one) / 2f);
            }

            for (int i = 1; i < tileVerts.Count - 1; i++)
            {
                subMesh.tris.Add(baseIndex + i + 1);
                subMesh.tris.Add(baseIndex + i);
                subMesh.tris.Add(baseIndex);
            }
        }

        private void AddHatchedTileToSubMesh(WorldGrid grid, PlanetTile tile, Material material)
        {
            tileVerts.Clear();
            grid.GetTileVertices(tile, tileVerts);
            if (tileVerts.Count < 3) return;

            LayerSubMesh subMesh = GetSubMesh(material);
            int baseIndex = subMesh.verts.Count;
            for (int i = 0; i < tileVerts.Count; i++)
            {
                Vector3 v = tileVerts[i];
                subMesh.verts.Add(v + v.normalized * HatchSurfaceOffset);
                subMesh.uvs.Add(new Vector2(GetWorldStripePhase(v), 0.5f));
            }

            for (int i = 1; i < tileVerts.Count - 1; i++)
            {
                subMesh.tris.Add(baseIndex + i + 1);
                subMesh.tris.Add(baseIndex + i);
                subMesh.tris.Add(baseIndex);
            }
        }
    }
}
