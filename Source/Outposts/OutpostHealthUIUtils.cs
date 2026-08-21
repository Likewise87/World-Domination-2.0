using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Shared outpost pawn health UI helpers (vanilla bleeding icon, etc.).</summary>
    [StaticConstructorOnStartup]
    public static class OutpostHealthUIUtils
    {
        public const float BleedingIconSize = 22f;

        private static Texture2D cachedBleedingIcon;

        static OutpostHealthUIUtils()
        {
            FieldInfo field = typeof(HealthCardUtility).GetField("BleedingIcon", BindingFlags.NonPublic | BindingFlags.Static);
            cachedBleedingIcon = field?.GetValue(null) as Texture2D;
        }

        /// <summary>Vanilla health-tab bleeding icon (same as pawn health UI).</summary>
        public static Texture2D GetBleedingIcon() => cachedBleedingIcon;

        public static void DrawBleedingIconCentered(Rect cell, float rowHeight, string tooltip = null)
        {
            Texture2D icon = GetBleedingIcon();
            if (icon == null) return;
            float sz = BleedingIconSize;
            Rect ir = new Rect(cell.x + (cell.width - sz) * 0.5f, cell.y + (rowHeight - sz) * 0.5f, sz, sz);
            GUI.DrawTexture(ir, icon, ScaleMode.ScaleToFit);
            if (!tooltip.NullOrEmpty())
                TooltipHandler.TipRegion(cell, tooltip);
        }
    }
}
