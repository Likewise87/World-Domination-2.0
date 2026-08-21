using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Draggable floating WD World Setup icon on Select Starting Site (ExtraOnGUI).
    /// Also runs overlay hotkeys while that page shows the world.
    /// </summary>
    [StaticConstructorOnStartup]
    [HarmonyPatch(typeof(Page_SelectStartingSite), nameof(Page_SelectStartingSite.ExtraOnGUI))]
    public static class Patch_Page_SelectStartingSite_WdWorldSetup
    {
        private static readonly Texture2D WdIcon =
            ContentFinder<Texture2D>.Get("UI/Tab/WD", false) ?? TexCommand.Replant;

        public const float ButtonSize = 72f;
        public const float ScreenPad = 16f;
        private const float DragThreshold = 4f;

        private static Vector2 iconPos;
        private static bool iconPosSeeded;
        private static bool dragging;
        private static bool draggedThisPress;
        private static Vector2 dragGrabOffset;

        public static Vector2 DefaultIconPos =>
            new Vector2(ScreenPad, ScreenPad);

        public static void Postfix()
        {
            if (Find.World == null || WdIcon == null) return;

            WorldComponent_WDVisualizerToggle.ProcessWindowAndOverlayHotkeys();

            if (!iconPosSeeded)
            {
                iconPos = DefaultIconPos;
                iconPosSeeded = true;
            }

            ClampIconToScreen();
            Rect btn = new Rect(iconPos.x, iconPos.y, ButtonSize, ButtonSize);
            Event e = Event.current;

            if (dragging)
            {
                if (e.type == EventType.MouseDrag || e.type == EventType.MouseMove || e.rawType == EventType.MouseDrag)
                {
                    Vector2 next = e.mousePosition - dragGrabOffset;
                    if ((next - iconPos).sqrMagnitude > DragThreshold * DragThreshold)
                        draggedThisPress = true;
                    iconPos = next;
                    ClampIconToScreen();
                    e.Use();
                }

                if (e.type == EventType.MouseUp || e.rawType == EventType.MouseUp)
                {
                    dragging = false;
                    bool open = !draggedThisPress && e.button == 0;
                    e.Use();
                    if (open)
                        ToggleWorldSetupWindow();
                }
            }
            else if (e.type == EventType.MouseDown && e.button == 0 && Mouse.IsOver(btn))
            {
                dragging = true;
                draggedThisPress = false;
                dragGrabOffset = e.mousePosition - iconPos;
                e.Use();
            }

            Widgets.DrawWindowBackground(btn);
            if (Mouse.IsOver(btn))
                Widgets.DrawHighlight(btn);
            Widgets.DrawTextureFitted(btn.ContractedBy(8f), WdIcon, 1f);
            TooltipHandler.TipRegion(btn, "TSA_WD_WorldSetup_ButtonTooltip".Translate());
        }

        private static void ClampIconToScreen()
        {
            iconPos.x = Mathf.Clamp(iconPos.x, 0f, UI.screenWidth - ButtonSize);
            iconPos.y = Mathf.Clamp(iconPos.y, 0f, UI.screenHeight - ButtonSize);
        }

        private static void ToggleWorldSetupWindow()
        {
            Window existing = Find.WindowStack?.WindowOfType<Dialog_WdWorldSetup>();
            if (existing != null)
            {
                existing.Close();
                return;
            }
            Find.WindowStack.Add(new Dialog_WdWorldSetup());
        }

        /// <summary>
        /// Leaving Select Starting Site (back to factions/scenario, or forward into the game).
        /// World Setup windows and targeters must not outlive this page.
        /// </summary>
        public static void CleanupOnLeaveSelectStartingSite()
        {
            dragging = false;
            draggedThisPress = false;

            WD_WorldSetupTools.CancelActive();

            WindowStack stack = Find.WindowStack;
            if (stack != null)
            {
                CloseIfOpen<Dialog_WdWorldSetup>(stack);
                CloseIfOpen<Dialog_WdWorldGenAllegiances>(stack);
                CloseIfOpen<Dialog_WorldGenSettings>(stack);
            }

            // Overlays toggled via Alt+1–7 on this page keep drawing via WorldComponentOnGUI
            // even after the page is gone; clear them so Entry UI / next pages stay safe.
            WorldComponent_WDVisualizerToggle.SetShowEstablishmentBlockedOverlay(false);
            WorldComponent_WDVisualizerToggle.SetShowFortifyBlacklistOverlay(false);
            WorldComponent_WDVisualizerToggle.SetProductivityOverlayMode(WD_ProductivityOverlayMode.Off);
        }

        private static void CloseIfOpen<T>(WindowStack stack) where T : Window
        {
            T win = stack.WindowOfType<T>();
            if (win != null)
                win.Close(doCloseSound: false);
        }
    }

    [HarmonyPatch(typeof(Window), nameof(Window.PreClose))]
    public static class Patch_Page_SelectStartingSite_WdWorldSetup_PreClose
    {
        public static void Prefix(Window __instance)
        {
            if (__instance is Page_SelectStartingSite)
                Patch_Page_SelectStartingSite_WdWorldSetup.CleanupOnLeaveSelectStartingSite();
        }
    }
}
