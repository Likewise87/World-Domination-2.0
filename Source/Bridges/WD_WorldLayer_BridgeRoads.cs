using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Noise;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Full vanilla-style road meshes for bridged water tiles.
    /// <see cref="WorldDrawLayer_Roads"/> skips <c>WaterCovered</c>, so land banks show roads
    /// while ocean spans need this layer drawn above the water mesh.
    /// </summary>
    public class WD_WorldLayer_BridgeRoads : WorldDrawLayer_Paths
    {
        private readonly List<OutputDirection> outputs = new List<OutputDirection>(8);

        private readonly ModuleBase roadDisplacementX = new Perlin(1.0, 2.0, 0.5, 3, 74173887, QualityMode.Medium);
        private readonly ModuleBase roadDisplacementY = new Perlin(1.0, 2.0, 0.5, 3, 67515931, QualityMode.Medium);
        private readonly ModuleBase roadDisplacementZ = new Perlin(1.0, 2.0, 0.5, 3, 87116801, QualityMode.Medium);

        public override bool Visible =>
            WorldComponent_WdBridges.Get() != null
            && WorldComponent_WdBridges.Get().HasAnyBridges;

        public override bool VisibleWhenLayerNotSelected => true;
        public override bool VisibleInBackground => false;

        /// <summary>Match vanilla road wobble, then lift above ocean water so the strip stays visible.</summary>
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
            // Vanilla roads lift by 0.02; water mesh sits higher, so lift a bit more.
            return inp + inp.normalized * 0.06f;
        }

        public static void EnsureRegistered()
        {
            SurfaceLayer surface = Find.WorldGrid?.Surface;
            if (surface?.WorldDrawLayers == null) return;

            for (int i = 0; i < surface.WorldDrawLayers.Count; i++)
            {
                if (surface.WorldDrawLayers[i] is WD_WorldLayer_BridgeRoads)
                    return;
            }

            var layer = new WD_WorldLayer_BridgeRoads();
            Traverse.Create(layer).Field("planetLayer").SetValue(surface);
            surface.WorldDrawLayers.Add(layer);
        }

        public static void SetDirty()
        {
            EnsureRegistered();
            SurfaceLayer surface = Find.WorldGrid?.Surface;
            if (surface == null) return;
            Find.World?.renderer?.SetDirty<WD_WorldLayer_BridgeRoads>(surface);
        }

        public override IEnumerable Regenerate()
        {
            foreach (object step in base.Regenerate())
                yield return step;

            WorldComponent_WdBridges bridges = WorldComponent_WdBridges.Get();
            if (bridges == null || !bridges.HasAnyBridges || planetLayer == null)
            {
                FinalizeMesh(MeshParts.All);
                yield break;
            }

            List<RoadWorldLayerDef> roadLayerDefs = DefDatabase<RoadWorldLayerDef>.AllDefs
                .OrderBy(d => d.order)
                .ToList();

            LayerSubMesh subMesh = GetSubMesh(WorldMaterials.Roads);
            IReadOnlyList<int> waterTiles = bridges.BridgedWaterTiles;
            for (int ti = 0; ti < waterTiles.Count; ti++)
            {
                int tileId = waterTiles[ti];
                if (tileId < 0 || tileId >= planetLayer.TilesCount) continue;
                if (!(planetLayer[tileId] is SurfaceTile surfaceTile) || !surfaceTile.WaterCovered) continue;
                List<SurfaceTile.RoadLink> links = surfaceTile.potentialRoads;
                if (links == null || links.Count == 0) continue;

                if (subMesh.verts.Count > 60000)
                    subMesh = GetSubMesh(WorldMaterials.Roads);

                PlanetTile tile = new PlanetTile(tileId, planetLayer);
                bool allowSmoothTransition = true;
                for (int i = 0; i < links.Count - 1; i++)
                {
                    if (links[i].road.worldTransitionGroup != links[i + 1].road.worldTransitionGroup)
                        allowSmoothTransition = false;
                }

                for (int li = 0; li < roadLayerDefs.Count; li++)
                {
                    RoadWorldLayerDef layerDef = roadLayerDefs[li];
                    outputs.Clear();
                    bool any = false;
                    for (int ri = 0; ri < links.Count; ri++)
                    {
                        RoadDef road = links[ri].road;
                        float width = road.GetLayerWidth(layerDef);
                        if (width > 0f)
                            any = true;
                        // Match vanilla: include zero-width directions so joins stay correct.
                        outputs.Add(new OutputDirection
                        {
                            neighbor = links[ri].neighbor,
                            width = width,
                            distortionFrequency = road.distortionFrequency,
                            distortionIntensity = road.distortionIntensity
                        });
                    }

                    if (!any) continue;
                    // Water-only layer: charcoal deck vs land Stone (0.4). Do not scale Outline
                    // (already 0.1) — multiplying it made the whole strip vanish into the ocean.
                    Color color = layerDef.color;
                    if (layerDef.defName == "Stone")
                        color = new Color(0.26f, 0.24f, 0.22f, 1f);
                    GeneratePaths(subMesh, tile, outputs, color, allowSmoothTransition);
                }

                yield return null;
            }

            FinalizeMesh(MeshParts.All);
        }
    }
}
