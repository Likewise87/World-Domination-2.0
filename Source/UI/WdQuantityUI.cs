using RimWorld;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Shared quantity step for +/- controls (warehouse, buy/gift deals, etc.).
    /// Vanilla <see cref="GenUI.CurrentAdjustmentMultiplier"/> uses KeyBindingDef.IsDownEvent,
    /// which often returns 1 when modifiers were already held before the click (common in windows
    /// with text fields). Prefer that when it sees a multiplier; otherwise fall back to held keys.
    /// </summary>
    internal static class WdQuantityUI
    {
        /// <summary>1, or ×10 / ×100 / ×1000 (Ctrl, Shift, both) like vanilla trade adjusters.</summary>
        public static int AdjustmentStep()
        {
            int fromGen = GenUI.CurrentAdjustmentMultiplier();
            if (fromGen > 1)
                return fromGen;

            bool x10 = KeyBindingDefOf.ModifierIncrement_10x.IsDown
                || Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
                || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);
            bool x100 = KeyBindingDefOf.ModifierIncrement_100x.IsDown
                || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            if (x10 && x100) return 1000;
            if (x10) return 10;
            if (x100) return 100;
            return 1;
        }
    }
}
