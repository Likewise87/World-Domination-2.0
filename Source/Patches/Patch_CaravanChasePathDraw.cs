using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>When a caravan is selected and chasing a traveler (Attack Caravan), draw the path to the target like normal caravan travel.</summary>
    [StaticConstructorOnStartup]
    [HarmonyPatch(typeof(Caravan), nameof(Caravan.DrawExtraSelectionOverlays))]
    public static class Patch_CaravanChasePathDraw
    {
        private static WorldComponent_CaravanChaseTraveler cachedComp;
        private static int cachedCompWorldId = -1;
        private static readonly List<Vector3> drawPathScratch = new List<Vector3>();

        private static readonly FieldInfo CurPathField =
            AccessTools.Field(typeof(Caravan_PathFollower), "curPath");
        private static readonly FieldInfo NextTileField =
            AccessTools.Field(typeof(Caravan_PathFollower), "nextTile");
        private static readonly FieldInfo MovingField =
            AccessTools.Field(typeof(Caravan_PathFollower), "moving");

        [HarmonyPostfix]
        public static void Postfix(Caravan __instance)
        {
            if (__instance == null || __instance.Destroyed) return;
            if (!Find.WorldSelector.IsSelected(__instance)) return;

            int worldId = Find.World?.info?.Seed ?? -1;
            if (cachedComp == null || cachedCompWorldId != worldId)
            {
                cachedComp = Find.World?.GetComponent<WorldComponent_CaravanChaseTraveler>();
                cachedCompWorldId = worldId;
            }
            var comp = cachedComp;
            if (comp == null) return;

            WorldObject_Traveler target = comp.GetChaseTarget(__instance);
            if (target == null || target.Destroyed) return;

            DrawChasePath(__instance, target);
        }

        private static void DrawChasePath(Caravan caravan, WorldObject_Traveler traveler)
        {
            WorldGrid grid = Find.WorldGrid;

            try
            {
                Caravan_PathFollower pather = caravan.pather;
                if (pather != null && CurPathField != null && NextTileField != null && MovingField != null)
                {
                    var curPath = CurPathField.GetValue(pather) as WorldPath;
                    object nextTileObj = NextTileField.GetValue(pather);
                    bool moving = (bool)MovingField.GetValue(pather);

                    if (moving && curPath != null && curPath.Found && curPath.NodesLeftCount > 0)
                    {
                        int count = curPath.NodesLeftCount;
                        drawPathScratch.Clear();
                        if (nextTileObj is PlanetTile nextTile && nextTile.Valid)
                        {
                            drawPathScratch.Add(caravan.DrawPos);
                            drawPathScratch.Add(grid.GetTileCenter(nextTile));
                        }
                        else
                            drawPathScratch.Add(grid.GetTileCenter(caravan.Tile));
                        for (int i = 0; i < count; i++)
                            drawPathScratch.Add(grid.GetTileCenter(curPath.Peek(i)));
                        GenDraw_WorldLineSmooth.DrawSmoothWorldPolyline(
                            drawPathScratch,
                            GenDraw_WorldLineSmooth.DefaultPathLineMat,
                            1f,
                            GenDraw_WorldLineSmooth.GetPathLineLift(),
                            segmentsOverride: 1);
                        return;
                    }
                }
            }
            catch
            {
                // Reflection may fail if vanilla API differs; fall through to fallback
            }

            // Fallback: single line from caravan to traveler
            GenDraw_WorldLineSmooth.DrawSmoothWorldLine(
                caravan.DrawPos,
                traveler.DrawPos,
                GenDraw_WorldLineSmooth.DefaultPathLineMat,
                1f,
                GenDraw_WorldLineSmooth.GetPathLineLift(),
                segments: 1);
        }
    }
}
