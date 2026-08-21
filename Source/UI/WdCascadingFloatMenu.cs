using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Float menu that supports a visible parent + child panel, and Escape to close only the child.
    /// </summary>
    public class WdCascadingFloatMenu : FloatMenu
    {
        private readonly Vector2? forcedPos;
        private readonly WdCascadingFloatMenu parentMenu;

        public WdCascadingFloatMenu(List<FloatMenuOption> options, Vector2? forcedPos = null, WdCascadingFloatMenu parent = null)
            : base(options)
        {
            this.forcedPos = forcedPos;
            this.parentMenu = parent;
            // Exact-type onlyOne check — allow parent + child of this class together.
            onlyOneOfTypeAllowed = false;
            vanishIfMouseDistant = false;
            // Both close on true outside clicks; clicking the parent closes the child via WindowStack.
            closeOnClickedOutside = true;
            closeOnCancel = true;
            // Esc must hit us even if mouse-focus promotion left the parent focused above the child.
            forceCatchAcceptAndCancelEventEvenIfUnfocused = true;
        }

        protected override void SetInitialSizeAndPosition()
        {
            if (!forcedPos.HasValue)
            {
                base.SetInitialSizeAndPosition();
                return;
            }

            Vector2 vector = forcedPos.Value;
            if (vector.x + InitialSize.x > UI.screenWidth)
                vector.x = UI.screenWidth - InitialSize.x;
            if (vector.y + InitialSize.y > UI.screenHeight)
                vector.y = UI.screenHeight - InitialSize.y;
            if (vector.x < 0f) vector.x = 0f;
            if (vector.y < 0f) vector.y = 0f;
            windowRect = new Rect(vector.x, vector.y, InitialSize.x, InitialSize.y);
        }

        /// <summary>
        /// Unity only delivers mouse events to the focused GUI.Window. With two cascade panels open,
        /// the child holds focus — so without this, clicks on the visible parent fall through to gizmos.
        /// ExtraOnGUI runs before WindowOnGUI, so focusing here makes the same-frame click land correctly.
        /// </summary>
        public override void ExtraOnGUI()
        {
            base.ExtraOnGUI();
            PromoteCascadePanelUnderMouse();
        }

        private static void PromoteCascadePanelUnderMouse()
        {
            if (Find.WindowStack == null) return;

            Vector2 mouse = UI.MousePositionOnUIInverted;
            WdCascadingFloatMenu underMouse = null;
            IList<Window> windows = Find.WindowStack.Windows;
            for (int i = windows.Count - 1; i >= 0; i--)
            {
                if (i >= windows.Count) continue;
                if (windows[i] is WdCascadingFloatMenu m && m.windowRect.Contains(mouse))
                {
                    underMouse = m;
                    break;
                }
            }

            if (underMouse == null) return;

            Find.WindowStack.Notify_ManuallySetFocus(underMouse);
            GUI.FocusWindow(underMouse.ID);
            GUI.BringWindowToFront(underMouse.ID);
        }

        public override void OnCancelKeyPressed()
        {
            // Consume Esc so the world map / main tabs do not also handle it.
            Event.current.Use();
            SoundDefOf.FloatMenu_Cancel.PlayOneShotOnCamera();

            // Always peel the deepest level first (child, then root), regardless of which
            // cascade panel currently holds WindowStack focus.
            WdCascadingFloatMenu toClose = FindDeepestOpen();
            if (toClose == null) return;

            WdCascadingFloatMenu stayFocused = toClose.parentMenu;
            Find.WindowStack.TryRemove(toClose);
            if (stayFocused != null && stayFocused.IsOpen)
                Find.WindowStack.Notify_ManuallySetFocus(stayFocused);
        }

        private static WdCascadingFloatMenu FindDeepestOpen()
        {
            if (Find.WindowStack == null) return null;
            WdCascadingFloatMenu root = null;
            WdCascadingFloatMenu child = null;
            foreach (Window w in Find.WindowStack.Windows)
            {
                if (w is not WdCascadingFloatMenu m) continue;
                if (m.parentMenu != null)
                    child = m;
                else
                    root = m;
            }
            return child ?? root;
        }

        public override void PostClose()
        {
            // Closing the parent must also dismiss its child (e.g. click-outside on root).
            if (parentMenu == null && Find.WindowStack != null)
            {
                IList<Window> windows = Find.WindowStack.Windows;
                for (int i = windows.Count - 1; i >= 0; i--)
                {
                    if (i >= windows.Count) continue;
                    if (windows[i] is WdCascadingFloatMenu child && child.parentMenu == this)
                        child.Close(doCloseSound: false);
                }
            }
            base.PostClose();
        }

        /// <summary>Close every cascading build menu (root + children).</summary>
        public static void CloseAll()
        {
            if (Find.WindowStack == null) return;
            IList<Window> windows = Find.WindowStack.Windows;
            for (int i = windows.Count - 1; i >= 0; i--)
            {
                if (i >= windows.Count) continue;
                if (windows[i] is WdCascadingFloatMenu m)
                    m.Close(doCloseSound: false);
            }
        }

        private static WdCascadingFloatMenu FindOpenRoot()
        {
            if (Find.WindowStack == null) return null;
            WdCascadingFloatMenu fallback = null;
            foreach (Window w in Find.WindowStack.Windows)
            {
                if (w is not WdCascadingFloatMenu m) continue;
                if (m.parentMenu == null)
                    return m;
                fallback ??= m.parentMenu;
            }
            return fallback;
        }

        private static void CloseChildrenOf(WdCascadingFloatMenu root)
        {
            if (root == null || Find.WindowStack == null) return;
            IList<Window> windows = Find.WindowStack.Windows;
            for (int i = windows.Count - 1; i >= 0; i--)
            {
                if (i >= windows.Count) continue;
                if (windows[i] is WdCascadingFloatMenu child && child.parentMenu == root)
                    child.Close(doCloseSound: false);
            }
        }

        /// <summary>
        /// Show <paramref name="childOptions"/> to the right of the open root menu.
        /// Keeps the parent in place (does not recreate/reposition it). Safe to call again to swap the child.
        /// <paramref name="rebuildParent"/> is unused (kept so call sites stay unchanged).
        /// </summary>
        public static void OpenAsChild(List<FloatMenuOption> childOptions, Func<List<FloatMenuOption>> rebuildParent)
        {
            if (childOptions == null || childOptions.Count == 0) return;

            WdCascadingFloatMenu root = FindOpenRoot();
            if (root == null)
            {
                // No cascade root (should not happen from Build gizmo) — fall back to a fresh pair.
                Vector2 mouse = UI.MousePositionOnUIInverted;
                List<FloatMenuOption> parentOpts = rebuildParent?.Invoke();
                if (parentOpts == null || parentOpts.Count == 0) return;
                root = new WdCascadingFloatMenu(parentOpts, mouse);
                Find.WindowStack.Add(root);
            }

            CloseChildrenOf(root);

            Rect parentRect = root.windowRect;
            var child = new WdCascadingFloatMenu(
                childOptions,
                new Vector2(parentRect.xMax + 4f, parentRect.y),
                root);
            Find.WindowStack.Add(child);
        }

        /// <summary>
        /// Parent-row option that opens a child panel. Returns false from DoGUI so vanilla
        /// FloatMenu does not close the parent after the click (unlike normal options).
        /// </summary>
        public static FloatMenuOption MakeBranchOption(string label, Action action, Texture2D icon, Color iconColor)
        {
            return new WdCascadeBranchOption(label, action, icon, iconColor);
        }

        /// <summary>Wrap a leaf action so choosing it dismisses the whole cascade.</summary>
        public static Action WrapLeaf(Action action)
        {
            return () =>
            {
                CloseAll();
                action?.Invoke();
            };
        }

        /// <summary>
        /// Same as <see cref="FloatMenuOption"/>, but never asks the owning menu to close —
        /// used for rows that only open/replace a child panel.
        /// </summary>
        private sealed class WdCascadeBranchOption : FloatMenuOption
        {
            public WdCascadeBranchOption(string label, Action action, Texture2D icon, Color iconColor)
                : base(label, action, icon, iconColor)
            {
            }

            public override bool DoGUI(Rect rect, bool colonistOrdering, FloatMenu floatMenu)
            {
                base.DoGUI(rect, colonistOrdering, floatMenu);
                return false;
            }
        }
    }
}
