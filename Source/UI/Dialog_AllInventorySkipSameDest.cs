using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Confirm skipping goods already at the chosen destination, then shipping the rest from other origins.
    /// </summary>
    public class Dialog_AllInventorySkipSameDest : Window
    {
        private const float TitleH = 28f;
        private const float IntroH = 44f;
        private const float QuestionH = 28f;
        private const float BtnH = 32f;
        private const float LineH = 22f;
        private const float Pad = 6f;
        private const float MaxListH = 320f;
        private const float ExtraWindowH = 80f;

        private readonly string destinationLabel;
        private readonly List<string> skippedLines;
        private readonly Action onConfirm;
        private Vector2 scrollPos;

        public Dialog_AllInventorySkipSameDest(
            string destinationLabel,
            List<string> skippedLines,
            Action onConfirm)
        {
            this.destinationLabel = destinationLabel ?? "";
            this.skippedLines = skippedLines ?? new List<string>();
            this.onConfirm = onConfirm;
            doCloseX = true;
            closeOnCancel = true;
            absorbInputAroundWindow = true;
            forcePause = false;
            layer = WindowLayer.Super;
        }

        private float NeededListContentH =>
            Mathf.Max(LineH, skippedLines.Count * LineH + 4f);

        public override Vector2 InitialSize
        {
            get
            {
                float listH = Mathf.Min(NeededListContentH, MaxListH);
                // Inner content + RimWorld window chrome (Margin on each side) + requested extra height.
                float innerH = TitleH + Pad + IntroH + Pad + listH + Pad + QuestionH + Pad + BtnH;
                float chrome = Margin * 2f;
                return new Vector2(520f, innerH + chrome + ExtraWindowH);
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (WdWindowEsc.TryCloseOnCancel(this))
                return;

            // Pin chrome to the bottom so buttons never clip if size/margins disagree.
            float btnY = inRect.height - BtnH;
            float questionY = btnY - Pad - QuestionH;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, TitleH),
                "TSA_WD_AllInventory_SkipSameDestTitle".Translate());
            Text.Font = GameFont.Small;

            float y = TitleH + Pad;
            Widgets.Label(new Rect(0f, y, inRect.width, IntroH),
                "TSA_WD_AllInventory_SkipSameDestIntro".Translate(destinationLabel));
            y += IntroH + Pad;

            float listBudget = Mathf.Max(LineH, questionY - Pad - y);
            float contentH = NeededListContentH;
            // Use only as much height as the lines need, until the budget / cap; scroll beyond that.
            float listH = Mathf.Min(contentH, Mathf.Min(listBudget, MaxListH));
            bool scroll = contentH > listH + 0.5f;

            Rect scrollOuter = new Rect(0f, y, inRect.width, listH);
            if (scroll)
            {
                Rect viewRect = new Rect(0f, 0f, inRect.width - 16f, contentH);
                Widgets.BeginScrollView(scrollOuter, ref scrollPos, viewRect);
                DrawLines(0f, viewRect.width);
                Widgets.EndScrollView();
            }
            else
            {
                DrawLines(y, inRect.width);
            }

            Widgets.Label(new Rect(0f, questionY, inRect.width, QuestionH),
                "TSA_WD_AllInventory_SkipSameDestQuestion".Translate());

            float half = inRect.width * 0.48f;
            GUI.color = new Color(1f, 0.3f, 0.35f);
            bool cancel = Widgets.ButtonText(new Rect(0f, btnY, half, BtnH), "Cancel".Translate());
            GUI.color = Color.white;
            if (cancel)
            {
                Close();
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
            if (Widgets.ButtonText(new Rect(inRect.width - half, btnY, half, BtnH),
                    "TSA_WD_AllInventory_SkipSameDestConfirm".Translate()))
            {
                onConfirm?.Invoke();
                Close();
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
        }

        private void DrawLines(float topY, float width)
        {
            float ly = topY + 2f;
            for (int i = 0; i < skippedLines.Count; i++)
            {
                Widgets.Label(new Rect(4f, ly, width - 8f, LineH), skippedLines[i]);
                ly += LineH;
            }
        }
    }
}
