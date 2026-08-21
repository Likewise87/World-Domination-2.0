using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// World-targeter session for remote establish from AllPlayerPawns.
    /// Isolated from requirements-preview targeting so the two never share state.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class RemoteOutpostEstablishSession
    {
        private static Texture2D mouseIcon;
        private static List<PlayerPawnRosterEntry> pendingEntries;
        private static MapParent pendingSource;
        private static bool targetingActive;
        private static bool suppressStopClear;

        public static bool IsTargetingActive => targetingActive;

        public static void BeginFromSelection(IReadOnlyList<PlayerPawnRosterEntry> selected)
        {
            if (!RemoteOutpostEstablishUtility.TryValidateColonySelection(selected, out MapParent source, out List<PlayerPawnRosterEntry> entries, out string fail))
            {
                Messages.Message(fail ?? "TSA_WD_RemoteEstablish_InvalidSelection".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            pendingEntries = entries;
            pendingSource = source;
            BeginOrRestartTargeting(showHint: true);
        }

        public static void RestartTargetingWithPending()
        {
            if (pendingEntries == null || pendingEntries.Count == 0 || pendingSource == null)
            {
                Clear();
                return;
            }
            BeginOrRestartTargeting(showHint: false);
        }

        public static void Clear()
        {
            pendingEntries = null;
            pendingSource = null;
            targetingActive = false;
            suppressStopClear = false;
        }

        /// <summary>Called when WorldTargeter stops; clears only if this session owns targeting.</summary>
        public static void NotifyWorldTargeterStopped()
        {
            if (!targetingActive || suppressStopClear) return;
            Clear();
        }

        private static void BeginOrRestartTargeting(bool showHint)
        {
            // Do not share WorldTargeter state with the requirements-preview session.
            suppressStopClear = true;
            if (Dialog_OutpostSelection.IsEstablishmentPreviewOverlayActive)
                Dialog_OutpostSelection.SetEstablishmentPreviewOverlayActive(false);
            if (WorldComponent_WDVisualizerToggle.IsWorldTargeterActive())
                Find.WorldTargeter.StopTargeting();
            suppressStopClear = false;

            PrepareWorldMapForPicking();

            if (showHint)
                Messages.Message("TSA_WD_RemoteEstablish_ClickTileHint".Translate(), MessageTypeDefOf.NeutralEvent);

            // Same mouse cursor as caravan Establish Outpost / requirements-preview targeting.
            mouseIcon ??= ContentFinder<Texture2D>.Get("UI/Commands/EstablishOutpost", false)
                ?? ContentFinder<Texture2D>.Get("UI/Commands/Settle", false)
                ?? TexCommand.Replant;

            targetingActive = true;
            Find.WorldTargeter.BeginTargeting(
                target =>
                {
                    if (!TryResolveTile(target, out int tile))
                        return false;

                    suppressStopClear = true;
                    var entries = pendingEntries;
                    var source = pendingSource;
                    targetingActive = false;
                    Find.WindowStack.Add(new Dialog_OutpostSelection(
                        tile,
                        "",
                        -1,
                        SettlementTier.T1,
                        null,
                        fromCaravan: null,
                        requirementsPreviewOnly: false,
                        remoteEstablishEntries: entries,
                        remoteEstablishSource: source));
                    suppressStopClear = false;
                    return true;
                },
                true,
                mouseIcon,
                false,
                null,
                null,
                IsValidTile);
        }

        /// <summary>Close covering UI and ensure the world map is visible for tile picking.</summary>
        private static void PrepareWorldMapForPicking()
        {
            WorldDomination_UIUtils.DismissWorldDominationUiForWorldMap();
            Find.WindowStack.WindowOfType<Window_AllPlayerPawns>()?.Close();

            // Close any other lingering layered windows so the targeter is usable.
            var stack = Find.WindowStack;
            if (stack?.Windows != null)
            {
                var copy = new List<Window>(stack.Windows);
                for (int i = 0; i < copy.Count; i++)
                {
                    Window w = copy[i];
                    if (w == null || w is ImmediateWindow) continue;
                    string name = w.GetType().Name;
                    if (name.Contains("Letter") || name.Contains("Messages")) continue;
                    stack.TryRemove(w, doCloseSound: false);
                }
            }

            if (Find.MainTabsRoot?.OpenTab != null)
                Find.MainTabsRoot.EscapeCurrentTab();

            PlanetTile jumpTile = pendingSource != null ? pendingSource.Tile : PlanetTile.Invalid;
            if (!jumpTile.Valid && Find.AnyPlayerHomeMap?.Parent != null)
                jumpTile = Find.AnyPlayerHomeMap.Parent.Tile;
            if (jumpTile.Valid)
                CameraJumper.TryJump(jumpTile);
        }

        private static bool TryResolveTile(GlobalTargetInfo target, out int tile)
        {
            tile = -1;
            if (!target.IsValid || target.Tile < 0) return false;
            if (!PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(target.Tile)) return false;
            tile = target.Tile;
            return true;
        }

        private static bool IsValidTile(GlobalTargetInfo target) => TryResolveTile(target, out _);
    }
}
