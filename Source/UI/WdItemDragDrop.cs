using System;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Frame-scoped drag state for item icons. Vanilla <see cref="Widgets.ButtonInvisibleDraggable"/>
    /// supplies the click-versus-drag threshold; this owns the payload, the hovered drop target and
    /// the mouse-up commit, because Widgets' active control is keyed on draw order and scroll
    /// culling would silently drop the drag mid-gesture.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class WdItemDragDrop
    {
        private static object dragPayload;
        private static object dragSource;
        private static string dragLabel;
        private static Texture dragIcon;

        private static object hoverTarget;
        private static Rect hoverRect;
        private static bool hoverValid;

        public static bool DragActive => dragPayload != null;
        public static object Payload => dragPayload;
        public static object Source => dragSource;

        /// <summary>
        /// Call once per draggable icon. Returns true on a plain click (inspect / info card);
        /// drag-and-drop is handled by <see cref="ResolveFrame"/>.
        /// </summary>
        public static bool DraggableIcon(Rect rect, object payload, object source, string label, Texture icon)
        {
            Widgets.DraggableResult result = Widgets.ButtonInvisibleDraggable(rect, false);

            if (result == Widgets.DraggableResult.Dragged)
            {
                dragPayload = payload;
                dragSource = source;
                dragLabel = label;
                dragIcon = icon;
                // Consume the event so nothing else (window chrome, scroll) steals the gesture.
                if (Event.current.type != EventType.Repaint && Event.current.type != EventType.Layout)
                    Event.current.Use();
                return false;
            }

            return result == Widgets.DraggableResult.Pressed
                || result == Widgets.DraggableResult.DraggedThenPressed;
        }

        /// <summary>Call while drawing each drop zone. Highlights the zone when a drag hovers it.</summary>
        public static void RegisterDropTarget(Rect rect, object target, bool valid = true)
        {
            if (!DragActive || target == null) return;
            if (!Mouse.IsOver(rect)) return;

            hoverTarget = target;
            hoverRect = rect;
            hoverValid = valid;

            Color prev = GUI.color;
            GUI.color = valid ? new Color(0.4f, 0.9f, 0.4f, 0.28f) : new Color(0.9f, 0.3f, 0.3f, 0.28f);
            GUI.DrawTexture(rect, BaseContent.WhiteTex);
            GUI.color = prev;
        }

        /// <summary>
        /// Call at the very end of DoWindowContents, outside every group and scroll view. Draws the
        /// ghost and resolves the drop when the button is released.
        /// </summary>
        public static void ResolveFrame(Action<object, object, object> onDrop)
        {
            if (!DragActive)
            {
                ClearHover();
                return;
            }

            DrawGhost();

            if (Event.current.type == EventType.Repaint)
            {
                // Mouse-up can land on a frame where Widgets never reports it, so poll directly.
                if (!Input.GetMouseButton(0))
                {
                    object payload = dragPayload;
                    object source = dragSource;
                    object target = hoverValid ? hoverTarget : null;
                    Cancel();
                    // Defer the drop callback: opening a count picker (or any Window) during
                    // Repaint leaves the new dialog unable to take input / confirm.
                    if (payload != null && target != null && onDrop != null)
                    {
                        Action<object, object, object> cb = onDrop;
                        LongEventHandler.ExecuteWhenFinished(() => cb(payload, source, target));
                    }
                    return;
                }
            }

            ClearHover();
        }

        /// <summary>Escape cancels the drag before the window gets a chance to close.</summary>
        public static bool TryCancelOnEscape()
        {
            if (!DragActive) return false;
            if (Event.current.type != EventType.KeyDown || Event.current.keyCode != KeyCode.Escape)
                return false;
            Event.current.Use();
            Cancel();
            return true;
        }

        public static void Cancel()
        {
            dragPayload = null;
            dragSource = null;
            dragLabel = null;
            dragIcon = null;
            ClearHover();
        }

        private static void ClearHover()
        {
            hoverTarget = null;
            hoverRect = default;
            hoverValid = false;
        }

        private static void DrawGhost()
        {
            Vector2 mouse = Event.current.mousePosition;
            const float iconSize = 28f;
            Rect iconRect = new Rect(mouse.x + 10f, mouse.y + 6f, iconSize, iconSize);

            Color prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.75f);
            if (dragIcon != null)
                GUI.DrawTexture(iconRect, dragIcon);
            GUI.color = prev;

            if (string.IsNullOrEmpty(dragLabel)) return;

            Text.Font = GameFont.Tiny;
            float w = Text.CalcSize(dragLabel).x + 8f;
            Rect labelRect = new Rect(iconRect.xMax + 4f, mouse.y + 6f, w, 20f);
            Widgets.DrawBoxSolid(labelRect, new Color(0f, 0f, 0f, 0.55f));
            Widgets.Label(labelRect.ContractedBy(2f), dragLabel);
            Text.Font = GameFont.Small;
        }
    }
}
