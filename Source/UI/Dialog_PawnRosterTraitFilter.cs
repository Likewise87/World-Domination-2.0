using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace TSA_WorldDomination
{
    /// <summary>Searchable And/Or trait picker for roster tables.</summary>
    public class Dialog_PawnRosterTraitFilter : Window
    {
        private string searchTerm = "";
        private Vector2 scroll;
        private bool foundExpanded = true;
        private bool allExpanded = true;
        private const float RowH = 24f;
        private const float RowPadX = 10f;
        private const float SearchH = 28f;
        private const float ModeBtnW = 70f;
        private const float CountColW = 90f;
        private const float TitleH = 30f;
        private const float SubtitleH = 18f;

        public override Vector2 InitialSize => new Vector2(520f, 640f);

        public Dialog_PawnRosterTraitFilter()
        {
            doCloseX = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = false;
            draggable = true;
            forcePause = false;
            PawnRosterTraitFilter.EnsureSnapshot(force: true);
        }

        public override void PreClose()
        {
            base.PreClose();
            PawnRosterTraitFilter.InvalidateRosterCaches();
        }

        public override void DoWindowContents(Rect inRect)
        {
            PawnRosterTraitFilter.EnsureSnapshot();

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, TitleH), "TSA_WD_TraitFilter_Title".Translate());
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(0f, TitleH, inRect.width, SubtitleH), "TSA_WD_TraitFilter_Subtitle".Translate());

            float y = TitleH + SubtitleH + 4f;
            Rect searchRect = new Rect(0f, y, inRect.width, SearchH);
            string oldSearch = searchTerm;
            searchTerm = Widgets.TextField(searchRect, searchTerm);
            if (string.IsNullOrEmpty(searchTerm))
            {
                GUI.color = new Color(1f, 1f, 1f, 0.4f);
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.Font = GameFont.Tiny;
                Widgets.Label(searchRect, "  " + "TSA_WD_TraitFilter_Search".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
            }
            if (searchTerm != oldSearch)
                scroll = Vector2.zero;
            y += SearchH + 6f;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            string logic = "TSA_WD_TraitFilter_Logic".Translate();
            float logicW = Text.CalcSize(logic).x + 8f;
            Widgets.Label(new Rect(0f, y, logicW, 28f), logic);
            Text.Anchor = TextAnchor.UpperLeft;

            Rect andRect = new Rect(logicW, y, ModeBtnW, 28f);
            Rect orRect = new Rect(andRect.xMax + 8f, y, ModeBtnW, 28f);
            if (PawnRosterHeaderFilter.DrawSlateChoice(
                andRect,
                "TSA_WD_TraitFilter_And".Translate(),
                PawnRosterTraitFilter.Mode == PawnRosterTraitFilterMode.And,
                "TSA_WD_TraitFilter_AndTip".Translate()))
            {
                PawnRosterTraitFilter.Mode = PawnRosterTraitFilterMode.And;
                PawnRosterTraitFilter.InvalidateRosterCaches();
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
            if (PawnRosterHeaderFilter.DrawSlateChoice(
                orRect,
                "TSA_WD_TraitFilter_Or".Translate(),
                PawnRosterTraitFilter.Mode == PawnRosterTraitFilterMode.Or,
                "TSA_WD_TraitFilter_OrTip".Translate()))
            {
                PawnRosterTraitFilter.Mode = PawnRosterTraitFilterMode.Or;
                PawnRosterTraitFilter.InvalidateRosterCaches();
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            Rect clearRect = new Rect(inRect.width - 140f, y, 140f, 28f);
            if (Widgets.ButtonText(clearRect, "TSA_WD_TraitFilter_Clear".Translate()))
            {
                PawnRosterTraitFilter.Clear();
                PawnRosterTraitFilter.InvalidateRosterCaches();
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
            y += 34f;

            IReadOnlyList<PawnRosterTraitDegreeRow> all = PawnRosterTraitFilter.GetSnapshotRows(out int total);
            string searchLower = string.IsNullOrEmpty(searchTerm) ? null : searchTerm.Trim().ToLowerInvariant();
            var found = new List<PawnRosterTraitDegreeRow>();
            var rest = new List<PawnRosterTraitDegreeRow>();
            for (int i = 0; i < all.Count; i++)
            {
                PawnRosterTraitDegreeRow row = all[i];
                if (searchLower != null && (row.Label == null || !row.Label.ToLowerInvariant().Contains(searchLower)))
                    continue;
                if (row.Count > 0) found.Add(row);
                rest.Add(row);
            }

            float foundBody = foundExpanded ? found.Count * RowH : 0f;
            float allBody = allExpanded ? rest.Count * RowH : 0f;
            float viewH = 80f + foundBody + allBody + 48f;
            Rect scrollOut = new Rect(0f, y, inRect.width, inRect.height - y);
            Rect view = new Rect(0f, 0f, inRect.width - 16f, Mathf.Max(scrollOut.height, viewH));
            Widgets.BeginScrollView(scrollOut, ref scroll, view);

            var listing = new Listing_Standard();
            listing.Begin(view);
            if (SettingsUI.DrawCollapsibleHeader(
                listing,
                "TSA_WD_TraitFilter_FoundHeader".Translate(found.Count.ToString(), total.ToString()),
                ref foundExpanded,
                SettingsUI.SectionHeaderColor,
                "TSA_WD_TraitFilter_FoundTip".Translate()))
            {
                DrawRows(listing, found, view.width);
            }
            if (SettingsUI.DrawCollapsibleHeader(
                listing,
                "TSA_WD_TraitFilter_AllHeader".Translate(rest.Count.ToString()),
                ref allExpanded,
                SettingsUI.SectionHeaderColor,
                "TSA_WD_TraitFilter_AllTip".Translate()))
            {
                DrawRows(listing, rest, view.width);
            }
            listing.End();
            Widgets.EndScrollView();
        }

        private static void DrawRows(Listing_Standard listing, List<PawnRosterTraitDegreeRow> rows, float width)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                PawnRosterTraitDegreeRow row = rows[i];
                Rect r = listing.GetRect(RowH);
                if (i % 2 == 0) Widgets.DrawHighlight(r);
                if (Mouse.IsOver(r)) Widgets.DrawLightHighlight(r);

                bool on = PawnRosterTraitFilter.IsSelected(row.Key);
                bool prev = on;
                Rect check = new Rect(r.x + RowPadX, r.y, r.width - CountColW - RowPadX * 2f, r.height);
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.CheckboxLabeled(check, row.Label ?? "", ref on);
                if (on != prev)
                {
                    PawnRosterTraitFilter.SetSelected(row.Key, on);
                    PawnRosterTraitFilter.InvalidateRosterCaches();
                }

                Rect countRect = new Rect(r.xMax - CountColW - RowPadX, r.y, CountColW, r.height);
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(countRect, "TSA_WD_TraitFilter_CountPct".Translate(
                    row.Count.ToString(),
                    row.Percent.ToString("F0")));
                TooltipHandler.TipRegion(countRect, "TSA_WD_TraitFilter_CountTip".Translate(
                    row.Count.ToString(),
                    row.Percent.ToString("F0")));
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
            }
            listing.Gap(4f);
        }
    }
}
