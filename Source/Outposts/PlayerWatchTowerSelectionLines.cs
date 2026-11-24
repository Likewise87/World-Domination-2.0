using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Cyan ownership lines for player Watch Towers.
    /// Tower select always draws to builder; builder fan-out only when Highlight Watch Towers is on.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class PlayerWatchTowerSelectionLines
    {
        private static readonly HashSet<int> highlightBuilderIds = new HashSet<int>();

        public static bool IsHighlightActive(WorldObject builder) =>
            builder != null && !builder.Destroyed && highlightBuilderIds.Contains(builder.ID);

        public static void SetHighlight(WorldObject builder, bool on)
        {
            if (builder == null || builder.Destroyed) return;
            if (on) highlightBuilderIds.Add(builder.ID);
            else highlightBuilderIds.Remove(builder.ID);
        }

        public static void ClearHighlight(WorldObject builder)
        {
            if (builder == null) return;
            highlightBuilderIds.Remove(builder.ID);
        }

        public static void DrawToBuilder(WorldObject_WD_PlayerWatchTower tower)
        {
            if (tower == null || tower.Destroyed || !tower.Spawned) return;
            WorldObject owner = tower.builtBySite;
            if (owner == null || owner.Destroyed || !owner.Spawned) return;
            if (!CanDraw()) return;
            DrawChord(tower.Tile.tileId, owner.Tile.tileId);
        }

        public static void DrawForBuilderIfHighlight(WorldObject builder)
        {
            if (builder == null || builder.Destroyed || !builder.Spawned) return;
            if (!IsHighlightActive(builder)) return;
            if (!CanDraw()) return;
            if (!Find.WorldSelector.IsSelected(builder)) return;

            int ownerTile = builder.Tile.tileId;
            IReadOnlyList<WorldObject_WD_PlayerWatchTower> live = WorldObject_WD_PlayerWatchTower.LiveTowers;
            for (int i = 0; i < live.Count; i++)
            {
                WorldObject_WD_PlayerWatchTower t = live[i];
                if (t == null || t.Destroyed || !t.Spawned) continue;
                if (!ReferenceEquals(t.builtBySite, builder)) continue;
                DrawChord(ownerTile, t.Tile.tileId);
            }
        }

        private static bool CanDraw()
        {
            if (!PlanetSurfaceWorldActions.IsPlanetSurfaceLayerSelected()) return false;
            if (WD_WorldMapZoomUtil.IsZoomedTooFarOut(WD_WorldMapZoomUtil.TravelerPathHideAltitudePercent))
                return false;
            return true;
        }

        private static void DrawChord(int fromTile, int toTile)
        {
            if (fromTile < 0 || toTile < 0 || fromTile == toTile) return;
            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return;
            GenDraw_WorldLineSmooth.DrawSmoothWorldLine(
                fromTile,
                toTile,
                grid,
                WorldOverlayLineMaterials.RadiusCyan,
                1f,
                GenDraw_WorldLineSmooth.GetPathLineLift());
        }
    }
}
