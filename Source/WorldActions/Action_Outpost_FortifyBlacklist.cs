using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Instant Add/Remove of no-fortify tiles from the WD world-map menu (click one tile at a time).</summary>
    public static class Action_Outpost_FortifyBlacklist
    {
        private static bool paintSessionActive;

        public static bool IsPaintSessionActive
        {
            get
            {
                if (!Find.WorldTargeter.IsTargeting && paintSessionActive)
                {
                    paintSessionActive = false;
                    WorldComponent_FortifyBlacklist.NotifyOverlayDirty();
                }
                return paintSessionActive;
            }
        }

        public static bool FeatureEnabled =>
            WorldDominationMod.settings?.enableFortifyBlacklist
            ?? WorldDominationSettings.DefEnableFortifyBlacklist;

        public static void StartAddBlockedTiles() => StartTargeting(erase: false);

        public static void StartRemoveBlockedTiles() => StartTargeting(erase: true);

        private static void StartTargeting(bool erase)
        {
            paintSessionActive = true;
            // Keep marks visible after paint ends (not only while targeting).
            WorldComponent_WDVisualizerToggle.SetShowFortifyBlacklistOverlay(true);
            WorldComponent_WDVisualizerToggle.EnsureFortifyBlacklistOverlayLayerRegisteredPublic();
            WorldComponent_FortifyBlacklist.NotifyOverlayDirty();

            bool TileOk(int tileId) =>
                WorldActions_RoadBlocks.IsTileBaseEligibleForRoadBlock(tileId);

            Find.WorldTargeter.BeginTargeting(
                (target) =>
                {
                    if (target.Tile < 0) return false;
                    if (!TileOk(target.Tile))
                    {
                        Messages.Message("TSA_WD_FortifyBlacklist_BadTile".Translate(), MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    var bl = WorldComponent_FortifyBlacklist.Get();
                    if (bl == null)
                    {
                        Messages.Message("TSA_WD_FortifyBlacklist_MissingComp".Translate(), MessageTypeDefOf.RejectInput);
                        paintSessionActive = false;
                        return true;
                    }

                    int tileId = target.Tile.tileId;
                    if (erase)
                        bl.RemoveRange(new[] { tileId });
                    else
                        bl.AddRange(new[] { tileId });

                    WorldComponent_FortifyBlacklist.NotifyOverlayDirty();
                    // Stay in targeting until right-click / Esc.
                    return false;
                },
                true,
                null,
                false,
                null,
                (target) => erase
                    ? "TSA_WD_FortifyBlacklist_RemoveTargetTip".Translate()
                    : "TSA_WD_FortifyBlacklist_AddTargetTip".Translate(),
                (target) => target.Tile >= 0 && TileOk(target.Tile));
        }
    }
}
