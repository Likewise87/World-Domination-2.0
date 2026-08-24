using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Draggable WD float button on hostile settlement assault maps → artillery support dialog.</summary>
    [StaticConstructorOnStartup]
    [HarmonyPatch(typeof(MapInterface), nameof(MapInterface.MapInterfaceOnGUI_BeforeMainTabs))]
    public static class Patch_AssaultArtilleryFloatButton
    {
        private static readonly Texture2D WdIcon =
            ContentFinder<Texture2D>.Get("UI/Tab/WD", false) ?? TexCommand.Attack;

        private const float ButtonSize = 72f;
        private const float ScreenPad = 16f;
        private const float DragThreshold = 4f;

        private static Vector2 iconPos;
        private static bool iconPosSeeded;
        private static bool dragging;
        private static bool draggedThisPress;
        private static Vector2 dragGrabOffset;

        public static void Postfix()
        {
            if (Current.ProgramState != ProgramState.Playing) return;
            if (Find.CurrentMap == null || WdIcon == null) return;
            if (WorldRendererUtility.WorldRendered) return;
            if (!WD_AssaultArtillerySupport.IsEligibleAssaultMap(Find.CurrentMap)) return;
            if (!WD_AssaultArtillerySupport.CanOpenDialog(Find.CurrentMap)) return;

            if (!iconPosSeeded)
            {
                iconPos = new Vector2(ScreenPad, ScreenPad);
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
                        WD_AssaultArtillerySupport.OpenDialog(Find.CurrentMap);
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
            TooltipHandler.TipRegion(btn, "TSA_WD_AssaultArtillery_ButtonTip".Translate());
        }

        private static void ClampIconToScreen()
        {
            iconPos.x = Mathf.Clamp(iconPos.x, 0f, UI.screenWidth - ButtonSize);
            iconPos.y = Mathf.Clamp(iconPos.y, 0f, UI.screenHeight - ButtonSize);
        }
    }
}
