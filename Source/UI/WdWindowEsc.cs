using UnityEngine;
using RimWorld;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Roster / search UIs draw TextFields before WindowStack checks cancel, and leftover IMGUI
    /// focus can make <see cref="KeyBindingDef.KeyDownEvent"/> unreliable.
    /// Two-step Escape: first clears text focus, second closes the window (or inspect tab).
    /// </summary>
    internal static class WdWindowEsc
    {
        private static int lastHandledFrame = -1;

        public static bool HasTextFocus => GUIUtility.keyboardControl != 0;

        /// <summary>
        /// True once per frame when cancel / Escape is pressed.
        /// Uses Event KeyDown (works even when KeyDownEvent is unreliable under TextField focus).
        /// </summary>
        public static bool CancelPressed()
        {
            int frame = Time.frameCount;
            if (frame == lastHandledFrame)
                return false;

            if (KeyBindingDefOf.Cancel.KeyDownEvent)
                return true;

            Event e = Event.current;
            return e != null && e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape;
        }

        public static void ClearTextFocus()
        {
            GUIUtility.keyboardControl = 0;
            GUI.FocusControl(null);
        }

        private static void MarkHandledAndUse()
        {
            lastHandledFrame = Time.frameCount;
            if (Event.current != null)
                Event.current.Use();
        }

        /// <summary>Mark cancel as handled this frame (e.g. after closing a world inspect tab).</summary>
        public static void ConsumeCancel() => MarkHandledAndUse();

        /// <summary>
        /// If cancel is pressed while a TextField has focus, clear focus and consume the event.
        /// Does not close any window. Call from WITab FillTab before TextFields.
        /// </summary>
        public static bool TryDefocusOnCancel()
        {
            if (!CancelPressed() || !HasTextFocus)
                return false;
            ClearTextFocus();
            MarkHandledAndUse();
            return true;
        }

        /// <summary>
        /// Two-step cancel for closable windows: defocus TextField first, close on the next cancel.
        /// Call at the start of DoWindowContents (before any TextField).
        /// </summary>
        public static bool TryCloseOnCancel(Window window)
        {
            if (window == null || !window.closeOnCancel)
                return false;
            if (!CancelPressed())
                return false;

            if (HasTextFocus)
            {
                ClearTextFocus();
                MarkHandledAndUse();
                return true;
            }

            ClearTextFocus();
            MarkHandledAndUse();
            window.Close();
            return true;
        }

        /// <summary>Clear focus when a search dialog closes so the parent roster keeps working ESC.</summary>
        public static void ClearTextFocusOnClose() => ClearTextFocus();
    }
}
