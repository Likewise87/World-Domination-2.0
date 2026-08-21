using UnityEngine;
using UnityEngine.Rendering;
using Verse;

namespace TSA_WorldDomination
{
    [StaticConstructorOnStartup]
    public static class WorldOverlayLineMaterials
    {
        /// <summary>Vanilla world lines use 3590; draw logistics above dynamic world objects so supply lines stay visible.</summary>
        private const int RenderQueue = 3590;
        private const int LogisticsRenderQueue = 3620;
        /// <summary>Above food logistics cyan/green so hover delivery/redirect lines stay visible.</summary>
        private const int DeliveryOverlayRenderQueue = 3630;
        /// <summary>Above orange construction corridors so selected-crew white routes stay readable.</summary>
        private const int TravelerPathRenderQueue = 3640;

        public static readonly Material PathLineWhite = MakeAlwaysOnTopLineMat(Color.white, TravelerPathRenderQueue);
        public static readonly Material LogisticsGreen = MakeAlwaysOnTopLineMat(Color.green, LogisticsRenderQueue);

        private static readonly Color LogisticsDarkCyanColor = new Color(0.2f, 0.8f, 0.8f, 1f);
        public static readonly Material LogisticsDarkCyan = MakeAlwaysOnTopLineMat(LogisticsDarkCyanColor, LogisticsRenderQueue);

        public static readonly Color RecruitPurple = new Color(0.5f, 0f, 0.6f, 1f);
        public static readonly Material RecruitRedirectLine = MakeAlwaysOnTopLineMat(RecruitPurple, DeliveryOverlayRenderQueue);
        public static readonly Material RecruitTradingRadiusRing = MaterialPool.MatFrom("UI/Overlays/ThingLine", ShaderDatabase.WorldOverlayTransparent, RecruitPurple, RenderQueue);

        public static readonly Material RoadOrange = MaterialPool.MatFrom("UI/Overlays/ThingLine", ShaderDatabase.WorldOverlayTransparent, ColorLibrary.Orange, RenderQueue);
        /// <summary>Waypoint X / destination star for build projects; more opaque than corridor path lines.</summary>
        public static readonly Material RoadOrangeMarker = MakeAlwaysOnTopLineMat(
            new Color(ColorLibrary.Orange.r, ColorLibrary.Orange.g, ColorLibrary.Orange.b, 1f),
            RenderQueue);

        public static readonly Material RadiusRed = MaterialPool.MatFrom("UI/Overlays/ThingLine", ShaderDatabase.WorldOverlayTransparent, Color.red, RenderQueue);
        public static readonly Material RadiusCyan = MaterialPool.MatFrom("UI/Overlays/ThingLine", ShaderDatabase.WorldOverlayTransparent, Color.cyan, RenderQueue);

        /// <summary>
        /// Own material instance with ZTest Always so great-circle segments stay visible over hills/tiles
        /// (depth buffer from terrain otherwise hides mid-chord segments despite radial lift).
        /// </summary>
        private static Material MakeAlwaysOnTopLineMat(Color color, int renderQueue)
        {
            Material shared = MaterialPool.MatFrom("UI/Overlays/ThingLine", ShaderDatabase.WorldOverlayTransparent, color, renderQueue);
            Material mat = new Material(shared)
            {
                renderQueue = renderQueue,
                name = shared.name + "_AlwaysOnTop"
            };
            mat.SetInt(Shader.PropertyToID("_ZTest"), (int)CompareFunction.Always);
            return mat;
        }
    }
}
