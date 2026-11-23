using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Unity.Collections;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Instant full-planet black cover while a View World as FoW mesh is rebuilding.
    /// </summary>
    [StaticConstructorOnStartup]
    public class WD_WorldLayer_FowBlackout : WorldDrawLayer
    {
        private Material blackMat;

        public override bool Visible =>
            base.Visible
            && WdFowDraw.IsActive
            && !(WorldComponent_SettlementIntel.Get()?.TileFow?.LayersReady ?? true);

        public override bool VisibleWhenLayerNotSelected => false;
        public override bool VisibleInBackground => false;

        public static void EnsureRegistered()
        {
            SurfaceLayer surface = Find.WorldGrid?.Surface;
            if (surface?.WorldDrawLayers == null) return;
            for (int i = 0; i < surface.WorldDrawLayers.Count; i++)
            {
                if (surface.WorldDrawLayers[i] is WD_WorldLayer_FowBlackout)
                    return;
            }
            var layer = new WD_WorldLayer_FowBlackout();
            Traverse.Create(layer).Field("planetLayer").SetValue(surface);
            surface.WorldDrawLayers.Add(layer);
        }

        public new static void SetDirty()
        {
            // Never EnsureRegistered here: mutating WorldDrawLayers mid-draw causes Collection was modified.
            SurfaceLayer surface = Find.WorldGrid?.Surface;
            if (surface == null) return;
            Find.World?.renderer?.SetDirty<WD_WorldLayer_FowBlackout>(surface);
        }

        public override IEnumerable Regenerate()
        {
            foreach (object step in base.Regenerate())
                yield return step;

            if (!Visible || planetLayer == null)
            {
                FinalizeMesh(MeshParts.All);
                yield break;
            }

            if (blackMat == null)
            {
                blackMat = new Material(ShaderDatabase.DefaultShader)
                {
                    color = Color.black,
                    renderQueue = 3595
                };
            }

            Vector3 center = Find.WorldGrid.SurfaceViewCenter;
            float angle = Find.WorldGrid.SurfaceViewAngle;
            List<Vector3> verts;
            List<int> tris;
            if (angle < 180f)
            {
                SphereGenerator.Generate(
                    4,
                    planetLayer.Radius + 0.02f,
                    -center,
                    180f - Mathf.Min(angle, 180f) + 10f,
                    out verts,
                    out tris);
            }
            else
            {
                SphereGenerator.Generate(4, planetLayer.Radius + 0.02f, Vector3.forward, 180f, out verts, out tris);
            }
            LayerSubMesh mesh = GetSubMesh(blackMat);
            mesh.verts.AddRange(verts);
            mesh.tris.AddRange(tris);

            FinalizeMesh(MeshParts.All);
        }
    }

    /// <summary>
    /// RWE-style tile FoW mesh: verts only for fogged tiles; POV switch rebuilds sync under blackout.
    /// </summary>
    public abstract class WD_WorldLayer_FowTileBase : WorldDrawLayer
    {
        private Material tileMat;
        private readonly Dictionary<int, List<int>> trackVerts = new Dictionary<int, List<int>>();
        private readonly Dictionary<int, int> tileToMesh = new Dictionary<int, int>();
        private readonly Dictionary<int, int> chunkToSubMesh = new Dictionary<int, int>();
        private readonly List<int> drainScratch = new List<int>(256);
        private bool meshBootstrapped;

        protected abstract float OverlayAltitude { get; }
        protected abstract Color OverlayColor { get; }
        protected abstract int RenderQueue { get; }
        protected abstract bool IsIncognitaLayer { get; }

        public override bool Visible =>
            base.Visible
            && WdFowDraw.IsActive
            && (WorldComponent_SettlementIntel.Get()?.TileFow?.LayersReady ?? false);

        public override bool VisibleWhenLayerNotSelected => false;
        public override bool VisibleInBackground => false;

        protected static void EnsureRegisteredTyped<T>() where T : WD_WorldLayer_FowTileBase, new()
        {
            SurfaceLayer surface = Find.WorldGrid?.Surface;
            if (surface?.WorldDrawLayers == null) return;
            for (int i = 0; i < surface.WorldDrawLayers.Count; i++)
            {
                if (surface.WorldDrawLayers[i] is T)
                    return;
            }
            var layer = new T();
            Traverse.Create(layer).Field("planetLayer").SetValue(surface);
            surface.WorldDrawLayers.Add(layer);
        }

        protected static void SetDirtyTyped<T>() where T : WD_WorldLayer_FowTileBase
        {
            SurfaceLayer surface = Find.WorldGrid?.Surface;
            if (surface == null) return;
            Find.World?.renderer?.SetDirty<T>(surface);
        }

        protected static T FindLayer<T>() where T : WD_WorldLayer_FowTileBase
        {
            SurfaceLayer surface = Find.WorldGrid?.Surface;
            if (surface?.WorldDrawLayers == null) return null;
            for (int i = 0; i < surface.WorldDrawLayers.Count; i++)
            {
                if (surface.WorldDrawLayers[i] is T typed)
                    return typed;
            }
            return null;
        }

        public virtual void ResetMeshState()
        {
            trackVerts.Clear();
            tileToMesh.Clear();
            chunkToSubMesh.Clear();
            subMeshes.Clear();
            meshBootstrapped = false;
        }

        /// <summary>Full sync rebuild for POV switch. Only meshes tiles that should show fog.</summary>
        public void RebuildAllSync()
        {
            if (planetLayer == null) return;
            ResetMeshState();
            foreach (object _ in base.Regenerate()) { }
            meshBootstrapped = true;
            EnsureMaterial();

            var intel = WorldComponent_SettlementIntel.Get();
            WdFowTileMemory mem = intel?.TileFow;
            if (mem == null) return;

            // Sight disks are refreshed by the caller (view switch) / world tick; this path only reads bits.
            Faction view = WdFowDraw.EffectiveViewFaction;
            BitArray explored = view != null ? mem.GetExplored(view.loadID) : null;
            NativeArray<int> offsets = planetLayer.UnsafeTileIDToVerts_offsets;
            NativeArray<Vector3> verts = planetLayer.UnsafeVerts;
            int n = planetLayer.TilesCount;

            for (int tileId = 0; tileId < n; tileId++)
            {
                if (!ShouldShowOverlay(mem, explored, tileId)) continue;
                EnsureTileVerts(tileId, offsets, verts);
                ApplyTileRaised(tileId, offsets, verts, raised: true);
            }

            FinalizeTouchedMeshes();
        }

        public override IEnumerable Regenerate()
        {
            if (!meshBootstrapped)
            {
                foreach (object step in base.Regenerate())
                    yield return step;
                trackVerts.Clear();
                tileToMesh.Clear();
                chunkToSubMesh.Clear();
                meshBootstrapped = true;
            }

            if (!WdFowDraw.IsActive || planetLayer == null)
                yield break;

            var intel = WorldComponent_SettlementIntel.Get();
            WdFowTileMemory mem = intel?.TileFow;
            if (mem == null || !mem.LayersReady)
                yield break;

            EnsureMaterial();
            // Draw path: never refresh sight here. The world tick keeps the disks fresh and fills the dirty queues.
            // Drain entire dirty queue in one go (incremental traveler updates only).
            if (!mem.TryDrainDirty(IsIncognitaLayer, drainScratch, maxCount: int.MaxValue))
                yield break;

            NativeArray<int> offsets = planetLayer.UnsafeTileIDToVerts_offsets;
            NativeArray<Vector3> verts = planetLayer.UnsafeVerts;
            Faction view = WdFowDraw.EffectiveViewFaction;
            BitArray explored = view != null ? mem.GetExplored(view.loadID) : null;

            for (int d = 0; d < drainScratch.Count; d++)
            {
                int tileId = drainScratch[d];
                if (tileId < 0 || tileId >= planetLayer.TilesCount) continue;

                if (ShouldShowOverlay(mem, explored, tileId))
                {
                    EnsureTileVerts(tileId, offsets, verts);
                    ApplyTileRaised(tileId, offsets, verts, raised: true);
                }
                else if (trackVerts.ContainsKey(tileId))
                {
                    ApplyTileRaised(tileId, offsets, verts, raised: false);
                }
            }

            FinalizeTouchedMeshes();
        }

        private bool ShouldShowOverlay(WdFowTileMemory mem, BitArray explored, int tileId)
        {
            bool isExplored = explored != null && tileId >= 0 && tileId < explored.Length && explored[tileId];
            if (IsIncognitaLayer)
                return !isExplored;
            return isExplored && !mem.IsTileVisibleBit(tileId);
        }

        private void EnsureMaterial()
        {
            if (tileMat != null) return;
            tileMat = new Material(IsIncognitaLayer ? ShaderDatabase.DefaultShader : ShaderDatabase.MetaOverlay)
            {
                color = OverlayColor,
                renderQueue = RenderQueue
            };
        }

        private Vector3 Raised(Vector3 vertex) =>
            vertex + vertex.normalized * OverlayAltitude;

        private void EnsureTileVerts(int tileId, NativeArray<int> offsets, NativeArray<Vector3> verts)
        {
            if (trackVerts.ContainsKey(tileId)) return;

            LayerSubMesh mesh = SubMeshForTile(tileId, offsets, verts, out int subMeshIndex);
            trackVerts[tileId] = new List<int>();
            tileToMesh[tileId] = subMeshIndex;

            int start = offsets[tileId];
            int end = GetOneAfterLastVertIndex(tileId, offsets, verts);
            int baseCount = mesh.verts.Count;
            int local = 0;
            for (int i = start; i < end; i++)
            {
                trackVerts[tileId].Add(mesh.verts.Count);
                mesh.verts.Add(Raised(verts[i]));
                mesh.uvs.Add(Vector2.zero);
                if (i < end - 2)
                {
                    mesh.tris.Add(baseCount + local + 2);
                    mesh.tris.Add(baseCount + local + 1);
                    mesh.tris.Add(baseCount);
                }
                local++;
            }
        }

        private void ApplyTileRaised(int tileId, NativeArray<int> offsets, NativeArray<Vector3> verts, bool raised)
        {
            if (!trackVerts.TryGetValue(tileId, out List<int> indices) || indices == null) return;
            if (!tileToMesh.TryGetValue(tileId, out int meshIndex)) return;
            if (meshIndex < 0 || meshIndex >= subMeshes.Count) return;

            LayerSubMesh mesh = subMeshes[meshIndex];
            if (raised)
            {
                int src = offsets[tileId];
                for (int i = 0; i < indices.Count; i++)
                {
                    mesh.verts[indices[i]] = Raised(verts[src]);
                    src++;
                }
            }
            else
            {
                for (int i = 0; i < indices.Count; i++)
                    mesh.verts[indices[i]] = Vector3.zero;
            }
            mesh.finalized = false;
        }

        private LayerSubMesh SubMeshForTile(int tileId, NativeArray<int> offsets, NativeArray<Vector3> verts, out int subMeshIndex)
        {
            if (tileToMesh.TryGetValue(tileId, out subMeshIndex))
                return subMeshes[subMeshIndex];

            int chunk = SpatialChunk(verts[offsets[tileId]]);
            if (!chunkToSubMesh.TryGetValue(chunk, out subMeshIndex)
                || subMeshes[subMeshIndex].verts.Count >= 35000)
            {
                var item = new LayerSubMesh(new Mesh(), tileMat);
                subMeshIndex = subMeshes.Count;
                subMeshes.Add(item);
                chunkToSubMesh[chunk] = subMeshIndex;
            }
            return subMeshes[subMeshIndex];
        }

        private void FinalizeTouchedMeshes()
        {
            for (int i = 0; i < subMeshes.Count; i++)
            {
                if (!subMeshes[i].finalized && subMeshes[i].verts.Count > 0)
                    subMeshes[i].FinalizeMesh(MeshParts.All);
            }
        }

        private static int GetOneAfterLastVertIndex(int index, NativeArray<int> offsets, NativeArray<Vector3> verts)
        {
            if (index + 1 >= offsets.Length) return verts.Length;
            return offsets[index + 1];
        }

        private static int SpatialChunk(Vector3 position)
        {
            float mag = position.magnitude;
            if (mag < 0.001f) return 0;
            float inv = 1f / mag;
            return Quantize(position.x * inv) | (Quantize(position.y * inv) << 2) | (Quantize(position.z * inv) << 4);
        }

        private static int Quantize(float unit)
        {
            int n = (int)((unit + 1f) * 1.999f);
            if (n < 0) return 0;
            if (n > 3) return 3;
            return n;
        }
    }

    [StaticConstructorOnStartup]
    public class WD_WorldLayer_FowIncognita : WD_WorldLayer_FowTileBase
    {
        protected override float OverlayAltitude => 0.08f;
        protected override Color OverlayColor => Color.black;
        protected override int RenderQueue => 3590;
        protected override bool IsIncognitaLayer => true;

        public static void EnsureRegistered() => EnsureRegisteredTyped<WD_WorldLayer_FowIncognita>();
        public new static void SetDirty()
        {
            // Register only from tick / BeginViewFactionSwitch — not from SetDirty mid-draw.
            SetDirtyTyped<WD_WorldLayer_FowIncognita>();
        }

        public static void RebuildSync()
        {
            EnsureRegistered();
            FindLayer<WD_WorldLayer_FowIncognita>()?.RebuildAllSync();
        }

        public static void NotifyFowInactive()
        {
            SurfaceLayer surface = Find.WorldGrid?.Surface;
            if (surface?.WorldDrawLayers == null) return;
            for (int i = 0; i < surface.WorldDrawLayers.Count; i++)
            {
                if (surface.WorldDrawLayers[i] is WD_WorldLayer_FowIncognita layer)
                    layer.ResetMeshState();
            }
        }
    }

    [StaticConstructorOnStartup]
    public class WD_WorldLayer_FowShroud : WD_WorldLayer_FowTileBase
    {
        protected override float OverlayAltitude => 0.012f;
        protected override Color OverlayColor => new Color(0f, 0f, 0f, 0.50f);
        protected override int RenderQueue => 3588;
        protected override bool IsIncognitaLayer => false;

        public static void EnsureRegistered() => EnsureRegisteredTyped<WD_WorldLayer_FowShroud>();
        public new static void SetDirty()
        {
            SetDirtyTyped<WD_WorldLayer_FowShroud>();
        }

        public static void RebuildSync()
        {
            EnsureRegistered();
            FindLayer<WD_WorldLayer_FowShroud>()?.RebuildAllSync();
        }

        public static void NotifyFowInactive()
        {
            SurfaceLayer surface = Find.WorldGrid?.Surface;
            if (surface?.WorldDrawLayers == null) return;
            for (int i = 0; i < surface.WorldDrawLayers.Count; i++)
            {
                if (surface.WorldDrawLayers[i] is WD_WorldLayer_FowShroud layer)
                    layer.ResetMeshState();
            }
        }
    }
}
