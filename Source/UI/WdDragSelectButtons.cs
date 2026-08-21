using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Count-adjust buttons (+ / - / Max / 0) with optional <c>telardo.DragSelect</c> support via
    /// <see cref="WdDragSelectAccess"/>. Draw with <see cref="Widgets.ButtonText"/>; when DragSelect
    /// is available, click/drag comes from its invisible draggable API (no double-fire).
    /// </summary>
    internal static class WdDragSelectButtons
    {
        /// <param name="stableHash">Stable per-control id (not Rect.GetHashCode).</param>
        public static bool ButtonText(Rect rect, string label, int stableHash, bool active = true)
        {
            if (!WdDragSelectAccess.IsAvailable)
                return Widgets.ButtonText(rect, label, drawBackground: true, doMouseoverSound: true, active: active);

            // Match DragSelect.HarmonyPatch_TransferableUIUtility.DraggableButtonText:
            // draw via ButtonText, but only use DragSelect for the click/drag result
            // (OR-ing both would double-fire on a normal click).
            Widgets.ButtonText(rect, label, drawBackground: true, doMouseoverSound: true, active: active);
            if (!active)
                return false;
            return WdDragSelectAccess.TryLeftPressed(rect, stableHash);
        }

        public static int Hash(string rowKey, string controlTag)
            => Gen.HashCombineInt((rowKey ?? "").GetHashCode(), (controlTag ?? "").GetHashCode());
    }
}
