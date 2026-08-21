using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Vanilla <see cref="WorldInspectPane"/> sets <c>closeOnCancel = false</c>, and the open WITab's
    /// <see cref="ImmediateWindow"/> does the same. ESC therefore falls through to
    /// <see cref="MainButtonsRoot.HandleLowPriorityShortcuts"/>, which leaves the world while
    /// <see cref="WorldInspectPane.OpenTabType"/> stays set — tabs reopen when returning to the world.
    /// Two-step: if a tab TextField has focus, clear it first; otherwise close the open inspect tab.
    /// Only leave the world when no tab is open.
    /// Freely closable dialogs already default to <c>closeOnCancel = true</c> and are handled earlier
    /// by <see cref="WindowStack.Notify_PressedCancel"/> (except intentional blockers like defense choice).
    /// </summary>
    [HarmonyPatch(typeof(MainButtonsRoot), nameof(MainButtonsRoot.HandleLowPriorityShortcuts))]
    public static class Patch_MainButtonsRoot_EscClosesWorldInspectTab
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            if (!WorldRendererUtility.WorldSelected
                || Current.ProgramState != ProgramState.Playing
                || Find.CurrentMap == null)
            {
                return true;
            }

            // First ESC: leave filter / search focus on the open WITab (e.g. outpost Pawns).
            if (WdWindowEsc.TryDefocusOnCancel())
                return false;

            if (!WdWindowEsc.CancelPressed())
                return true;

            WorldInspectPane? pane = Find.World?.UI?.inspectPane;
            if (pane?.OpenTabType == null)
                return true;

            pane.CloseOpenTab();
            SoundDefOf.TabClose.PlayOneShotOnCamera();
            WdWindowEsc.ConsumeCancel();
            return false;
        }
    }

    /// <summary>Retint warehouse delivery / ship-destination mouse icon cyan (WorldTargeter draws it white).</summary>
    [HarmonyPatch(typeof(WorldTargeter), nameof(WorldTargeter.TargeterOnGUI))]
    public static class Patch_WorldTargeter_CyanDeliveryMouseIcon
    {
        [HarmonyPostfix]
        public static void Postfix() => Outpost_Warehouse_Delivery.DrawCyanDeliveryMouseOverlayIfActive();
    }
}
