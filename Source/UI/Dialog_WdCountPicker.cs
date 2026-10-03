using System;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Small modal for splitting a stack. Uses the same TextFieldNumeric plus buffer pattern as the
    /// warehouse tab and the settlement deal dialogs so typing behaves identically everywhere.
    /// </summary>
    public class Dialog_WdCountPicker : Window
    {
        private readonly string title;
        private readonly int max;
        private readonly Action<int> onConfirm;

        private int value;
        private string buffer;

        public Dialog_WdCountPicker(string title, int max, Action<int> onConfirm)
        {
            this.title = title;
            this.max = Mathf.Max(1, max);
            this.onConfirm = onConfirm;
            value = this.max;
            buffer = value.ToString();

            forcePause = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            doCloseX = true;
            draggable = false;
            // Above the Armory dialog so OK / typing are never swallowed by it.
            layer = WindowLayer.Super;
        }

        public override Vector2 InitialSize => new Vector2(360f, 180f);

        public override void DoWindowContents(Rect inRect)
        {
            if (WdWindowEsc.TryCloseOnCancel(this))
                return;

            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 28f), title);

            Rect fieldRect = new Rect(0f, 34f, 120f, 28f);
            Widgets.TextFieldNumeric(fieldRect, ref value, ref buffer, 1f, max);

            Rect sliderRect = new Rect(0f, 68f, inRect.width, 24f);
            int slid = Mathf.RoundToInt(Widgets.HorizontalSlider(sliderRect, value, 1f, max, roundTo: 1f));
            if (slid != value)
            {
                value = slid;
                buffer = value.ToString();
            }

            Rect allBtn = new Rect(130f, 34f, 70f, 28f);
            if (Widgets.ButtonText(allBtn, "TSA_WD_CountPicker_All".Translate()))
            {
                value = max;
                buffer = value.ToString();
            }

            float btnY = inRect.height - 34f;
            float half = inRect.width / 2f - 4f;
            Rect cancelBtn = new Rect(0f, btnY, half, 30f);
            Rect okBtn = new Rect(inRect.width / 2f + 4f, btnY, half, 30f);

            GUI.color = new Color(1f, 0.3f, 0.35f);
            bool cancel = Widgets.ButtonText(cancelBtn, "CancelButton".Translate());
            GUI.color = Color.white;
            if (cancel)
                Close();

            if (Widgets.ButtonText(okBtn, "OK".Translate()))
            {
                onConfirm?.Invoke(Mathf.Clamp(value, 1, max));
                Close();
            }
        }
    }
}
