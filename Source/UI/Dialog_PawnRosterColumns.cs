using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace TSA_WorldDomination
{
    /// <summary>Sticky checkbox dialog for roster column visibility (stays open while toggling).</summary>
    public class Dialog_PawnRosterColumns : Window
    {
        private readonly PawnRosterColumnWindow windowKind;
        private readonly Action onChanged;
        private Vector2 scroll;

        public override Vector2 InitialSize => new Vector2(320f, 480f);

        public Dialog_PawnRosterColumns(PawnRosterColumnWindow windowKind, Action onChanged = null)
        {
            this.windowKind = windowKind;
            this.onChanged = onChanged;
            doCloseX = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = false;
            draggable = true;
            forcePause = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 28f), "TSA_WD_PawnRoster_ColumnsToShow".Translate());
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(0f, 28f, inRect.width, 18f), "TSA_WD_PawnRoster_ColumnsToShowTip".Translate());

            float y = 52f;
            Rect resetRect = new Rect(0f, y, inRect.width, 28f);
            if (Widgets.ButtonText(resetRect, "TSA_WD_PawnRoster_ColumnsReset".Translate()))
            {
                WorldComponent_PawnRosterColumnPrefs.Get()?.ResetToDefaults(windowKind);
                onChanged?.Invoke();
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
            y += 34f;

            IReadOnlyList<PawnRosterColumnOption> opts = PawnRosterColumnCatalog.OptionsFor(windowKind);
            float rowH = 24f;
            float viewH = opts.Count * rowH + 8f;
            Rect scrollOut = new Rect(0f, y, inRect.width, inRect.height - y);
            Rect view = new Rect(0f, 0f, inRect.width - 16f, viewH);
            Widgets.BeginScrollView(scrollOut, ref scroll, view);

            WorldComponent_PawnRosterColumnPrefs prefs = WorldComponent_PawnRosterColumnPrefs.Get();
            float rowY = 0f;
            for (int i = 0; i < opts.Count; i++)
            {
                PawnRosterColumnOption opt = opts[i];
                Rect row = new Rect(0f, rowY, view.width, rowH);
                bool on = prefs == null || prefs.IsVisible(windowKind, opt.Id);
                bool prev = on;
                Widgets.CheckboxLabeled(row, PawnRosterColumnCatalog.ResolveLabel(opt), ref on);
                if (on != prev && prefs != null)
                {
                    prefs.SetVisible(windowKind, opt.Id, on);
                    onChanged?.Invoke();
                }
                rowY += rowH;
            }

            Widgets.EndScrollView();
        }
    }
}
