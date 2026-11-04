using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Vanilla mod-settings host tweaks when showing World Domination:
    /// widen/taller the window (3-pane shell), and replace the grey window background with our flat page fill.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_ModSettings), nameof(Window.InitialSize), MethodType.Getter)]
    public static class Patch_Dialog_ModSettings_Width
    {
        public const float ExtraWidth = 320f;
        public const float ExtraHeight = 100f;

        public static void Postfix(Dialog_ModSettings __instance, ref Vector2 __result)
        {
            if (!IsWorldDominationSettings(__instance)) return;
            __result.x += ExtraWidth;
            __result.y += ExtraHeight;
        }

        public static bool IsWorldDominationSettings(Dialog_ModSettings dlg)
        {
            if (dlg == null) return false;
            // Vanilla field name has varied; probe a few without throwing.
            string[] names = { "mod", "selMod", "selectedMod" };
            for (int i = 0; i < names.Length; i++)
            {
                var field = AccessTools.Field(typeof(Dialog_ModSettings), names[i]);
                if (field == null) continue;
                object value = field.GetValue(dlg);
                if (value is WorldDominationMod) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Kill vanilla DrawWindowBackground for WD settings and paint our page color over the
    /// ImmediateWindow group (content rect expanded by Margin). Border is drawn in Postfix
    /// so later content fills cannot cover it.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_ModSettings), nameof(Window.DoWindowContents))]
    public static class Patch_Dialog_ModSettings_Background
    {
        // Window.Margin is protected; vanilla default is 18.
        private const float WindowMargin = 18f;

        public static void Prefix(Dialog_ModSettings __instance, Rect inRect)
        {
            if (!Patch_Dialog_ModSettings_Width.IsWorldDominationSettings(__instance)) return;

            __instance.doWindowBackground = false;

            Rect full = FullWindowRect(inRect);
            Widgets.DrawBoxSolid(full, WorldDominationMod.SettingsPageBg);
        }

        public static void Postfix(Dialog_ModSettings __instance, Rect inRect)
        {
            if (!Patch_Dialog_ModSettings_Width.IsWorldDominationSettings(__instance)) return;
            DrawSlateBorder(FullWindowRect(inRect));
        }

        private static Rect FullWindowRect(Rect inRect) =>
            new Rect(inRect.x - WindowMargin, inRect.y - WindowMargin, inRect.width + WindowMargin * 2f, inRect.height + WindowMargin * 2f);

        private static void DrawSlateBorder(Rect rect)
        {
            Color prev = GUI.color;
            GUI.color = WorldDominationMod.SettingsPageBorder;
            Widgets.DrawBox(rect, 1);
            GUI.color = prev;
        }
    }
}
