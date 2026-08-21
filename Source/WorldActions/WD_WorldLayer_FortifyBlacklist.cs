using System.Collections;
using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Fills player no-fortify marks. Visible when WD toggle is on or Mark/Erase targeting is active.</summary>
    [StaticConstructorOnStartup]
    public class WD_WorldLayer_FortifyBlacklist : WorldDrawLayer
    {
        private const float OverlayAlpha = 0.45f;
        private const float SurfaceOffset = 0.012f;
        private const int RenderQueue = 3580;

        private static Material fillMat;
        private readonly List<Vector3> tileVerts = new List<Vector3>(8);

        public override bool Visible =>
            WorldComponent_WDVisualizerToggle.ShowFortifyBlacklistOverlay
            || Action_Outpost_FortifyBlacklist.IsPaintSessionActive;

        public override bool VisibleWhenLayerNotSelected => true;
        public override bool VisibleInBackground => false;

        private static Material FillMat
        {
            get
            {
                if (fillMat != null) return fillMat;
                Color c = new Color(0.15f, 0.55f, 0.95f, OverlayAlpha);
                fillMat = MaterialPool.MatFrom(BaseContent.WhiteTex, ShaderDatabase.WorldOverlayTransparent, c, RenderQueue);
                return fillMat;
            }
        }

        public override IEnumerable Regenerate()
        {
            foreach (object step in base.Regenerate())
                yield return step;

            if (!Visible)
            {
                FinalizeMesh(MeshParts.All);
                yield break;
            }

            WorldGrid grid = Find.WorldGrid;
            var bl = WorldComponent_FortifyBlacklist.Get();
            if (grid == null || bl == null || bl.Count == 0)
            {
                FinalizeMesh(MeshParts.All);
                yield break;
            }

            Material mat = FillMat;
            PlanetLayer layer = Find.World?.grid?.Surface;
            foreach (int tileId in bl.EnumerateTiles())
            {
                if (tileId < 0 || !grid.InBounds(tileId)) continue;
                PlanetTile tile = layer != null
                    ? new PlanetTile(tileId, layer)
                    : new PlanetTile(tileId);
                AddTileToSubMesh(grid, tile, mat);
            }

            FinalizeMesh(MeshParts.All);
        }

        private void AddTileToSubMesh(WorldGrid grid, PlanetTile tile, Material material)
        {
            tileVerts.Clear();
            grid.GetTileVertices(tile, tileVerts);
            if (tileVerts.Count < 3) return;

            LayerSubMesh subMesh = GetSubMesh(material);
            int baseIndex = subMesh.verts.Count;
            for (int i = 0; i < tileVerts.Count; i++)
                subMesh.verts.Add(tileVerts[i] + tileVerts[i].normalized * SurfaceOffset);

            for (int i = 0; i < tileVerts.Count - 2; i++)
            {
                subMesh.tris.Add(baseIndex);
                subMesh.tris.Add(baseIndex + i + 1);
                subMesh.tris.Add(baseIndex + i + 2);
            }
        }
    }
}
