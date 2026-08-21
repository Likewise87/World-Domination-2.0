using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Roster checkbox selection. When <c>telardo.DragSelect</c> is available via
    /// <see cref="WdDragSelectAccess"/>, hold-and-drag multi-select matches trade/caravan UI.
    /// Otherwise uses vanilla <see cref="Widgets.Checkbox"/>.
    /// </summary>
    internal static class PawnRosterPaintSelect
    {
        private static bool paintSession;
        private static bool paintSelectToTrue;

        /// <summary>Call once at the start of each window/tab GUI pass.</summary>
        public static void BeginFrame(object owner)
        {
            _ = owner;
            if (!Input.GetMouseButton(0))
                paintSession = false;
        }

        /// <summary>
        /// Draws the checkbox and updates <paramref name="selected"/> on click / drag-paint.
        /// </summary>
        public static bool Draw(
            object owner,
            Rect cell,
            float checkboxX,
            float checkboxY,
            float size,
            string thingId,
            HashSet<string> selected,
            bool canInteract)
        {
            _ = owner;
            bool isSelected = !thingId.NullOrEmpty() && selected != null && selected.Contains(thingId);

            if (WdDragSelectAccess.IsAvailable)
                return DrawWithDragSelect(cell, checkboxX, checkboxY, size, thingId, selected, canInteract, isSelected);

            return DrawVanillaCheckbox(checkboxX, checkboxY, size, thingId, selected, canInteract, isSelected);
        }

        private static bool DrawVanillaCheckbox(
            float checkboxX,
            float checkboxY,
            float size,
            string thingId,
            HashSet<string> selected,
            bool canInteract,
            bool isSelected)
        {
            bool want = isSelected;
            Color prev = GUI.color;
            GUI.enabled = canInteract;
            if (!canInteract)
                GUI.color = new Color(0.45f, 0.45f, 0.45f, 1f);
            Widgets.Checkbox(new Vector2(checkboxX, checkboxY), ref want, size);
            GUI.enabled = true;
            GUI.color = prev;

            if (!canInteract || selected == null || thingId.NullOrEmpty())
                return isSelected;

            if (want != isSelected)
                Apply(selected, thingId, want);

            return selected.Contains(thingId);
        }

        private static bool DrawWithDragSelect(
            Rect cell,
            float checkboxX,
            float checkboxY,
            float size,
            string thingId,
            HashSet<string> selected,
            bool canInteract,
            bool isSelected)
        {
            Color prev = GUI.color;
            if (!canInteract)
                GUI.color = new Color(0.45f, 0.45f, 0.45f, 1f);
            Widgets.CheckboxDraw(checkboxX, checkboxY, isSelected, !canInteract, size);
            GUI.color = prev;

            if (!canInteract || selected == null || thingId.NullOrEmpty())
                return isSelected;

            // Stable per-row hash (Rect.GetHashCode is a poor drag key across layout passes).
            int hash = Gen.HashCombineInt(thingId.GetHashCode(), 391847);
            if (WdDragSelectAccess.TryLeftPressed(cell, hash))
            {
                if (!paintSession)
                {
                    paintSession = true;
                    paintSelectToTrue = !isSelected;
                }
                Apply(selected, thingId, paintSelectToTrue);
            }

            return selected.Contains(thingId);
        }

        private static void Apply(HashSet<string> selected, string thingId, bool select)
        {
            if (select) selected.Add(thingId);
            else selected.Remove(thingId);
        }
    }
}
