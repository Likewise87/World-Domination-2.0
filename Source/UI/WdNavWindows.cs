using System;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Alt+key hub windows: close the current WD nav UI, then open the target (or toggle off if already open).
    /// Keeps shortcuts usable while a force-paused hub is up.
    /// </summary>
    internal static class WdNavWindows
    {
        private const string MainTabDefName = "TSA_WD_WorldDomination";

        /// <summary>
        /// Run Alt+window hotkeys from hub DoWindowContents so chords work even when this window
        /// is open (and from WorldComponentOnGUI). Frame-debounced inside the visualizer component.
        /// </summary>
        public static void ProcessHotkeys()
        {
            WorldComponent_WDVisualizerToggle.ProcessWindowAndOverlayHotkeys();
        }

        /// <summary>Close all WD nav hubs and related detail overlays (not modal gameplay dialogs).</summary>
        public static void CloseAllNavWindows(bool escapeMainTab = true)
        {
            WindowStack stack = Find.WindowStack;
            if (stack == null) return;

            stack.WindowOfType<Window_DiplomacyMatrix>()?.Close();
            stack.WindowOfType<Window_OutpostOverview>()?.Close();
            stack.WindowOfType<Window_WorldStats>()?.Close();
            stack.WindowOfType<Window_ActionLog>()?.Close();
            stack.WindowOfType<Window_ActiveTravelers>()?.Close();
            stack.WindowOfType<Window_AllPlayerPawns>()?.Close();
            stack.WindowOfType<Window_Prisoners>()?.Close();
            stack.WindowOfType<Window_FactionDetails>()?.Close();
            stack.WindowOfType<Window_RaidResolutionDetails>()?.Close();
            stack.WindowOfType<Window_RaidAttemptDetails>()?.Close();
            stack.WindowOfType<Window_CaravanClashDetails>()?.Close();
            stack.WindowOfType<Dialog_MovePawnToLocation>()?.Close();
            stack.WindowOfType<Dialog_SmartAssignOutpostFilter>()?.Close();
            stack.WindowOfType<Dialog_SchedulePrisonerDestination>()?.Close();

            if (escapeMainTab && Find.MainTabsRoot?.OpenTab != null)
                Find.MainTabsRoot.EscapeCurrentTab();
        }

        /// <summary>
        /// Hotkey toggle: if <typeparamref name="T"/> is open, close it; otherwise close other WD hubs and open it.
        /// </summary>
        public static void ToggleExclusive<T>(Func<T> create) where T : Window
        {
            if (Find.WindowStack == null || create == null) return;

            T open = Find.WindowStack.WindowOfType<T>();
            if (open != null)
            {
                open.Close();
                return;
            }

            CloseAllNavWindows(escapeMainTab: true);
            WdWindowEsc.ClearTextFocus();
            Find.WindowStack.Add(create());
        }

        /// <summary>Dashboard/nav click: always land on a fresh <typeparamref name="T"/>, closing other WD hubs.</summary>
        public static void OpenExclusive<T>(Func<T> create) where T : Window
        {
            if (Find.WindowStack == null || create == null) return;
            CloseAllNavWindows(escapeMainTab: true);
            WdWindowEsc.ClearTextFocus();
            Find.WindowStack.Add(create());
        }

        public static void ToggleMainTabExclusive()
        {
            MainButtonDef def = DefDatabase<MainButtonDef>.GetNamedSilentFail(MainTabDefName);
            if (def == null || Find.MainTabsRoot == null) return;

            if (Find.MainTabsRoot.OpenTab == def)
            {
                Find.MainTabsRoot.EscapeCurrentTab();
                return;
            }

            CloseAllNavWindows(escapeMainTab: false);
            WdWindowEsc.ClearTextFocus();
            Find.MainTabsRoot.ToggleTab(def);
        }

        /// <summary>Hold+C: show world map, or hide it if already open.</summary>
        public static void ToggleWorldMap()
        {
            if (Current.ProgramState != ProgramState.Playing) return;

            if (WorldRendererUtility.WorldRendered)
            {
                CameraJumper.TryHideWorld();
                return;
            }

            CloseAllNavWindows(escapeMainTab: true);
            WdWindowEsc.ClearTextFocus();
            CameraJumper.TryShowWorld();
        }
    }
}
