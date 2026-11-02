using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Noise;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Wooden-trench look: wide berm + timber + dark ditch (vertex-color path strips).
    /// Smooth transition is off so diagonal legs keep a readable min width.
    /// </summary>
    public class WD_WorldLayer_Trenches : WorldDrawLayer_Paths
    {
        private readonly List<OutputDirection> outputs = new List<OutputDirection>(8);

        private readonly ModuleBase roadDisplacementX = new Perlin(1.0, 2.0, 0.5, 3, 84173887, QualityMode.Medium);
        private readonly ModuleBase roadDisplacementY = new Perlin(1.0, 2.0, 0.5, 3, 77515931, QualityMode.Medium);
        private readonly ModuleBase roadDisplacementZ = new Perlin(1.0, 2.0, 0.5, 3, 97116801, QualityMode.Medium);

        public override bool Visible =>
            base.Visible
            && WorldComponent_WdTrenches.FeatureEnabled
            && WorldComponent_WdTrenches.Get() != null
            && WorldComponent_WdTrenches.Get().HasAny;

        public override bool VisibleWhenLayerNotSelected => false;
        public override bool VisibleInBackground => false;

        public override Vector3 FinalizePoint(Vector3 inp, float distortionFrequency, float distortionIntensity)
        {
            Vector3 coordinate = inp * distortionFrequency;
            float magnitude = inp.magnitude;
            Vector3 vector = new Vector3(
                roadDisplacementX.GetValue(coordinate),
                roadDisplacementY.GetValue(coordinate),
                roadDisplacementZ.GetValue(coordinate));
            if ((double)vector.magnitude > 0.0001)
            {
                float num = (1f / (1f + Mathf.Exp((0f - vector.magnitude) / 1f * 2f)) * 2f - 1f) * 1f;
                vector = vector.normalized * num;
            }
            inp = (inp + vector * distortionIntensity).normalized * magnitude;
            return inp + inp.normalized * 0.025f;
        }

        public static void EnsureRegistered()
        {
            SurfaceLayer surface = Find.WorldGrid?.Surface;
            if (surface?.WorldDrawLayers == null) return;

            for (int i = 0; i < surface.WorldDrawLayers.Count; i++)
            {
                if (surface.WorldDrawLayers[i] is WD_WorldLayer_Trenches)
                    return;
            }

            var layer = new WD_WorldLayer_Trenches();
            Traverse.Create(layer).Field("planetLayer").SetValue(surface);
            surface.WorldDrawLayers.Add(layer);
        }

        public static void SetDirty()
        {
            EnsureRegistered();
            SurfaceLayer surface = Find.WorldGrid?.Surface;
            if (surface == null) return;
            Find.World?.renderer?.SetDirty<WD_WorldLayer_Trenches>(surface);
        }

        public override IEnumerable Regenerate()
        {
            foreach (object step in base.Regenerate())
                yield return step;

            var trenches = WorldComponent_WdTrenches.Get();
            if (trenches == null || !trenches.HasAny || planetLayer == null)
            {
                FinalizeMesh(MeshParts.All);
                yield break;
            }

            var adj = new Dictionary<int, List<int>>();
            var edgeList = trenches.Edges;
            for (int i = 0; i < edgeList.Count; i++)
            {
                var e = edgeList[i];
                if (e == null) continue;
                Add(adj, e.tileA, e.tileB);
                Add(adj, e.tileB, e.tileA);
            }

            LayerSubMesh subMesh = GetSubMesh(WorldMaterials.Roads);

            // Widest berm first, then timber lip, then ditch (top). No smooth transition —
            // that was collapsing diagonal legs to a hairline at hub joins.
            DrawLayer(adj, ref subMesh, WorldComponent_WdTrenches.BermColor, WorldComponent_WdTrenches.BermWidth);
            DrawLayer(adj, ref subMesh, WorldComponent_WdTrenches.TimberColor, WorldComponent_WdTrenches.TimberWidth);
            DrawLayer(adj, ref subMesh, WorldComponent_WdTrenches.DitchColor, WorldComponent_WdTrenches.DitchWidth);

            FinalizeMesh(MeshParts.All);
            yield break;
        }

        private void DrawLayer(Dictionary<int, List<int>> adj, ref LayerSubMesh subMesh, Color color, float width)
        {
            foreach (var kv in adj)
            {
                int tileId = kv.Key;
                var nbs = kv.Value;
                if (nbs == null || nbs.Count == 0) continue;
                if (tileId < 0 || tileId >= planetLayer.TilesCount) continue;

                if (subMesh.verts.Count > 60000)
                    subMesh = GetSubMesh(WorldMaterials.Roads);

                outputs.Clear();
                for (int i = 0; i < nbs.Count; i++)
                {
                    outputs.Add(new OutputDirection
                    {
                        neighbor = nbs[i],
                        width = width,
                        distortionFrequency = WorldComponent_WdTrenches.DistortionFrequency,
                        distortionIntensity = WorldComponent_WdTrenches.DistortionIntensity
                    });
                }

                PlanetTile tile = new PlanetTile(tileId, planetLayer);
                GeneratePaths(subMesh, tile, outputs, color, allowSmoothTransition: false);
            }
        }

        private static void Add(Dictionary<int, List<int>> adj, int from, int to)
        {
            if (!adj.TryGetValue(from, out var list))
            {
                list = new List<int>(2);
                adj[from] = list;
            }
            if (!list.Contains(to))
                list.Add(to);
        }
    }
}
