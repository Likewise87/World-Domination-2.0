using LudeonTK;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Single Dev Tools entry for the Playing-time WD world debug overlay.</summary>
    public static class DebugActions_WorldDebug
    {
        /// <summary>Absolute pollution step per click (0..1 tile pollution scale = 25 percentage points).</summary>
        public const float PollutionDebugStep = 0.25f;

        [DebugAction("World Domination", "WD Debug Tools",
            allowedGameStates = AllowedGameStates.PlayingOnWorld)]
        public static void OpenWdWorldDebug()
        {
            if (Find.WindowStack == null) return;
            var existing = Find.WindowStack.WindowOfType<Dialog_WdWorldDebug>();
            if (existing != null)
            {
                Find.WindowStack.TryRemove(existing);
                return;
            }
            Find.WindowStack.Add(new Dialog_WdWorldDebug());
        }

        public static void PollutionPlus25AtMouse() => AdjustPollutionAtMouse(+PollutionDebugStep);

        public static void PollutionMinus25AtMouse() => AdjustPollutionAtMouse(-PollutionDebugStep);

        private static void AdjustPollutionAtMouse(float delta01)
        {
            if (!ModsConfig.BiotechActive)
            {
                Messages.Message("WD debug: Biotech required for tile pollution.", MessageTypeDefOf.RejectInput);
                return;
            }

            int tileId = GenWorld.MouseTile();
            var grid = Find.WorldGrid;
            if (grid == null || tileId < 0 || tileId >= grid.TilesCount)
            {
                Messages.Message("WD debug: click a world tile.", MessageTypeDefOf.RejectInput);
                return;
            }

            Tile t = grid[tileId];
            float before = Mathf.Clamp01(t.pollution);
            float after = Mathf.Clamp01(before + delta01);
            if (Mathf.Approximately(before, after))
            {
                Messages.Message(
                    $"WD debug: tile {tileId} pollution already {(before * 100f):F0}% (no change).",
                    MessageTypeDefOf.NeutralEvent);
                return;
            }

            t.pollution = after;
            Find.World?.renderer?.Notify_TilePollutionChanged(new PlanetTile(tileId));
            WD_WorldLayer_PollutionOverlay.InvalidateAndDirtyIfActive();

            string sign = delta01 >= 0f ? "+" : "-";
            Messages.Message(
                $"WD debug: tile {tileId} pollution {(before * 100f):F0}% → {(after * 100f):F0}% ({sign}25%).",
                MessageTypeDefOf.CautionInput);
        }
    }
}
