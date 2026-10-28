#nullable disable
using System;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace TSA_WorldDomination
{
    /// <summary>Small name prompt for saving / renaming a roster filter preset.</summary>
    public class Dialog_PawnRosterPresetName : Window
    {
        private const string FieldControlName = "WD_PawnRosterPresetName";
        private const float BtnH = 32f;

        private readonly string title;
        private readonly Func<string, string> validate;
        private readonly Action<string> onSave;
        private string name;
        private bool focusPending = true;

        public override Vector2 InitialSize => new Vector2(360f, 170f);

        /// <param name="validate">Returns a reject reason for the trimmed name, or null when it can be saved.</param>
        public Dialog_PawnRosterPresetName(string title, string initialName, Func<string, string> validate, Action<string> onSave)
        {
            this.title = title;
            this.validate = validate;
            this.onSave = onSave;
            name = initialName ?? "";
            doCloseX = false;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = true;
            forcePause = false;
            preventCameraMotion = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 32f), title);
            Text.Font = GameFont.Small;

            Rect field = new Rect(0f, 40f, inRect.width, 30f);
            GUI.SetNextControlName(FieldControlName);
            name = Widgets.TextField(field, name ?? "");
            if (focusPending)
            {
                GUI.FocusControl(FieldControlName);
                focusPending = false;
            }

            string trimmed = (name ?? "").Trim();
            string reject = trimmed.Length == 0 ? "" : validate?.Invoke(trimmed);
            if (!reject.NullOrEmpty())
            {
                GUI.color = ColorLibrary.RedReadable;
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(0f, field.yMax + 2f, inRect.width, 18f), reject);
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
            }

            bool canSave = trimmed.Length > 0 && reject.NullOrEmpty();
            float btnW = (inRect.width - 10f) * 0.5f;
            Rect cancelRect = new Rect(0f, inRect.height - BtnH, btnW, BtnH);
            Rect saveRect = new Rect(inRect.width - btnW, inRect.height - BtnH, btnW, BtnH);
            if (Widgets.ButtonText(cancelRect, "CancelButton".Translate()))
            {
                SoundDefOf.Click.PlayOneShotOnCamera();
                Close();
            }
            if (!canSave) GUI.color = new Color(1f, 1f, 1f, 0.45f);
            if (Widgets.ButtonText(saveRect, "TSA_WD_AdvFilter_SaveBtn".Translate(), active: canSave) && canSave)
                Accept(trimmed);
            GUI.color = Color.white;
        }

        public override void OnAcceptKeyPressed()
        {
            string trimmed = (name ?? "").Trim();
            if (trimmed.Length > 0 && validate?.Invoke(trimmed).NullOrEmpty() != false)
                Accept(trimmed);
            Event.current?.Use();
        }

        private void Accept(string trimmed)
        {
            SoundDefOf.Click.PlayOneShotOnCamera();
            onSave?.Invoke(trimmed);
            Close();
        }
    }
}
