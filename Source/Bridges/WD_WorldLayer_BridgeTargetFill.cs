using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Mesh fill of precomputed valid bridge-bank tiles while the world targeter is open.
    /// Regenerates only when the tile set changes (same pattern as movement-difficulty overlay).
    /// </summary>
    [StaticConstructorOnStartup]
    public class WD_WorldLayer_BridgeTargetFill : WorldDrawLayer
    {
        private const float SurfaceOffset = 0.012f;
        private const int RenderQueue = 3586;

        private static Material fillMaterial;
        private static readonly HashSet<int> tiles = new HashSet<int>(64);
        private static bool hasTarget;
        private static bool pinned;
        private static int touchedFrame = -1;

        private readonly List<Vector3> tileVerts = new List<Vector3>(8);

        public override bool Visible => hasTarget && tiles.Count > 0;
        public override bool VisibleWhenLayerNotSelected => true;
        public override bool VisibleInBackground => false;

        public static void EnsureRegistered()
        {
            SurfaceLayer surface = Find.WorldGrid?.Surface;
            if (surface?.WorldDrawLayers == null) return;

            for (int i = 0; i < surface.WorldDrawLayers.Count; i++)
            {
                if (surface.WorldDrawLayers[i] is WD_WorldLayer_BridgeTargetFill)
                    return;
            }

            var layer = new WD_WorldLayer_BridgeTargetFill();
            Traverse.Create(layer).Field("planetLayer").SetValue(surface);
            surface.WorldDrawLayers.Add(layer);
        }

        /// <summary>Keep the fill alive this frame. Dirties the mesh only when the set changes.</summary>
        public static void Touch(HashSet<int> nextTiles) => Show(nextTiles);

        /// <summary>Pin the bank fill until <see cref="Hide"/>. No per-frame keepalive.</summary>
        public static void Show(HashSet<int> nextTiles)
        {
            EnsureRegistered();
            pinned = true;
            if (nextTiles == null || nextTiles.Count == 0)
            {
                Hide();
                return;
            }

            if (hasTarget && SetsEqual(tiles, nextTiles)) return;

            tiles.Clear();
            foreach (int id in nextTiles)
                tiles.Add(id);
            hasTarget = true;
            SetDirty();
        }

        public static void Hide()
        {
            pinned = false;
            if (!hasTarget && tiles.Count == 0) return;
            hasTarget = false;
            tiles.Clear();
            SetDirty();
        }

        /// <summary>Call once per WorldComponentOnGUI after targeting extraUpdate.</summary>
        public static void EndFrame()
        {
            if (pinned) return;
            if (touchedFrame >= Time.frameCount - 1) return;
            if (!hasTarget) return;
            hasTarget = false;
            tiles.Clear();
            SetDirty();
        }

        public static void SetDirty()
        {
            EnsureRegistered();
            SurfaceLayer surface = Find.WorldGrid?.Surface;
            if (surface == null) return;
            Find.World?.renderer?.SetDirty<WD_WorldLayer_BridgeTargetFill>(surface);
        }

        public override IEnumerable Regenerate()
        {
            foreach (object step in base.Regenerate())
                yield return step;

            if (!hasTarget || tiles.Count == 0 || planetLayer == null)
            {
                FinalizeMesh(MeshParts.All);
                yield break;
            }

            WorldGrid grid = Find.WorldGrid;
            if (grid == null)
            {
                FinalizeMesh(MeshParts.All);
                yield break;
            }

            Material mat = fillMaterial ??= MakeFillMat();
            foreach (int tileId in tiles)
            {
                if (tileId < 0 || tileId >= planetLayer.TilesCount) continue;
                AddTileToSubMesh(grid, new PlanetTile(tileId, planetLayer), mat);
            }

            FinalizeMesh(MeshParts.All);
        }

        private static bool SetsEqual(HashSet<int> a, HashSet<int> b)
        {
            if (a.Count != b.Count) return false;
            foreach (int id in a)
            {
                if (!b.Contains(id)) return false;
            }
            return true;
        }

        private static Material MakeFillMat()
        {
            Color o = ColorLibrary.Orange;
            var color = new Color(o.r, o.g, o.b, 0.22f);
            return MaterialPool.MatFrom(BaseContent.WhiteTex, ShaderDatabase.WorldOverlayTransparent, color, RenderQueue);
        }

        private void AddTileToSubMesh(WorldGrid grid, PlanetTile tile, Material material)
        {
            tileVerts.Clear();
            grid.GetTileVertices(tile, tileVerts);
            if (tileVerts.Count < 3) return;

            LayerSubMesh subMesh = GetSubMesh(material);
            int baseIndex = subMesh.verts.Count;
            for (int i = 0; i < tileVerts.Count; i++)
            {
                Vector3 v = tileVerts[i];
                subMesh.verts.Add(v + v.normalized * SurfaceOffset);
                subMesh.uvs.Add((GenGeo.RegularPolygonVertexPosition(tileVerts.Count, i) + Vector2.one) / 2f);
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
