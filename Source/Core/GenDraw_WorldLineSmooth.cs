using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    [StaticConstructorOnStartup]
    public static class GenDraw_WorldLineSmooth
    {
        public const int AdaptiveSegments = -1;

        private const float DefaultLift = 0.08f;
        /// <summary>Extra radial offset for food logistics chords so mid-segments clear hill meshes.</summary>
        public const float LogisticsLift = 0.22f;
        private const float DegreesPerSegment = 6f;
        private const int SegmentsMin = 1;
        private const int SegmentsMax = 32;

        public static Material DefaultPathLineMat => WorldOverlayLineMaterials.PathLineWhite;

        /// <summary>
        /// Path/logistics line lift. Base matches <see cref="LogisticsLift"/>; on tiny planets
        /// (<see cref="WD_WorldMapZoomUtil"/>) we raise it so surface relief does not bury chords
        /// at normal overview zoom (same small-world scale idea as world-map labels).
        /// </summary>
        public static float GetPathLineLift()
        {
            float scale = Mathf.Clamp(WD_WorldMapZoomUtil.GetSmallWorldAltitudeScale(), 1f, 2.5f);
            return LogisticsLift * scale;
        }

        public static int SegmentsForChord(Vector3 start, Vector3 end)
        {
            float angle = Vector3.Angle(start, end);
            if (angle < 0.001f)
                return 1;
            return Mathf.Clamp(Mathf.RoundToInt(angle / DegreesPerSegment), SegmentsMin, SegmentsMax);
        }

        private static void DrawSmoothWorldLineCore(Vector3 start, Vector3 end, Material mat, float width, float lift, int segments)
        {
            if (segments < 1)
                segments = 1;
            for (int i = 0; i < segments; i++)
            {
                float t0 = (float)i / segments;
                float t1 = (float)(i + 1) / segments;
                Vector3 a = Vector3.Slerp(start, end, t0);
                Vector3 b = Vector3.Slerp(start, end, t1);
                a += a.normalized * lift;
                b += b.normalized * lift;
                GenDraw.DrawWorldLineBetween(a, b, mat, width);
            }
        }

        public static void DrawSmoothWorldLine(Vector3 start, Vector3 end, Material mat, float width, float lift = DefaultLift, int segments = -1)
        {
            if (mat == null)
                return;
            int seg = (segments == -1) ? SegmentsForChord(start, end) : Mathf.Max(1, segments);
            DrawSmoothWorldLineCore(start, end, mat, width, lift, seg);
        }

        public static void DrawSmoothWorldLine(Vector3 start, Vector3 end, float width = 1f, float lift = DefaultLift, int segments = -1)
        {
            DrawSmoothWorldLine(start, end, DefaultPathLineMat, width, lift, segments);
        }

        public static void DrawSmoothWorldLine(int fromTile, int toTile, WorldGrid grid, Material mat, float width, float lift = DefaultLift, int segments = -1)
        {
            if (grid == null || mat == null)
                return;
            DrawSmoothWorldLine(grid.GetTileCenter(fromTile), grid.GetTileCenter(toTile), mat, width, lift, segments);
        }

        public static void DrawSmoothWorldPolyline(IReadOnlyList<Vector3> points, Material mat, float width, float lift = DefaultLift, int segmentsOverride = -1)
        {
            if (mat == null || points == null || points.Count < 2)
                return;
            for (int i = 0; i < points.Count - 1; i++)
            {
                int seg = (segmentsOverride == -1) ? SegmentsForChord(points[i], points[i + 1]) : Mathf.Max(1, segmentsOverride);
                DrawSmoothWorldLineCore(points[i], points[i + 1], mat, width, lift, seg);
            }
        }
    }
}
