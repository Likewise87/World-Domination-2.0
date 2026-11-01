using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace TSA_WorldDomination
{
    /// <summary>Searchable And/Or hediff picker for roster tables (pawn-driven snapshot only).</summary>
    public class Dialog_PawnRosterHediffFilter : Window
    {
        private string searchTerm = "";
        private Vector2 scroll;
        private bool foundExpanded = true;
        private const float RowH = 24f;
        private const float RowPadX = 10f;
        private const float SearchH = 28f;
        private const float ModeBtnW = 70f;
        private const float CountColW = 90f;
        private const float TitleH = 30f;
        private const float SubtitleH = 18f;

        public override Vector2 InitialSize => new Vector2(520f, 640f);

        public Dialog_PawnRosterHediffFilter()
        {
            doCloseX = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = false;
            draggable = true;
            forcePause = false;
            PawnRosterHediffFilter.EnsureSnapshot(force: true);
        }

        public override void PreClose()
        {
            base.PreClose();
            PawnRosterHediffFilter.InvalidateRosterCaches();
        }

        public override void DoWindowContents(Rect inRect)
        {
            PawnRosterHediffFilter.EnsureSnapshot();

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, TitleH), "TSA_WD_HediffFilter_Title".Translate());
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(0f, TitleH, inRect.width, SubtitleH), "TSA_WD_HediffFilter_Subtitle".Translate());

            float y = TitleH + SubtitleH + 4f;
            Rect searchRect = new Rect(0f, y, inRect.width, SearchH);
            string oldSearch = searchTerm;
            searchTerm = Widgets.TextField(searchRect, searchTerm);
            if (string.IsNullOrEmpty(searchTerm))
            {
                GUI.color = new Color(1f, 1f, 1f, 0.4f);
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.Font = GameFont.Tiny;
                Widgets.Label(searchRect, "  " + "TSA_WD_HediffFilter_Search".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
            }
            if (searchTerm != oldSearch)
                scroll = Vector2.zero;
            y += SearchH + 6f;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            string logic = "TSA_WD_HediffFilter_Logic".Translate();
            float logicW = Text.CalcSize(logic).x + 8f;
            Widgets.Label(new Rect(0f, y, logicW, 28f), logic);
            Text.Anchor = TextAnchor.UpperLeft;

            Rect andRect = new Rect(logicW, y, ModeBtnW, 28f);
            Rect orRect = new Rect(andRect.xMax + 8f, y, ModeBtnW, 28f);
            if (PawnRosterHeaderFilter.DrawSlateChoice(
                andRect,
                "TSA_WD_HediffFilter_And".Translate(),
                PawnRosterHediffFilter.Mode == PawnRosterHediffFilterMode.And,
                "TSA_WD_HediffFilter_AndTip".Translate()))
            {
                PawnRosterHediffFilter.Mode = PawnRosterHediffFilterMode.And;
                PawnRosterHediffFilter.InvalidateRosterCaches();
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
            if (PawnRosterHeaderFilter.DrawSlateChoice(
                orRect,
                "TSA_WD_HediffFilter_Or".Translate(),
                PawnRosterHediffFilter.Mode == PawnRosterHediffFilterMode.Or,
                "TSA_WD_HediffFilter_OrTip".Translate()))
            {
                PawnRosterHediffFilter.Mode = PawnRosterHediffFilterMode.Or;
                PawnRosterHediffFilter.InvalidateRosterCaches();
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            Rect clearRect = new Rect(inRect.width - 140f, y, 140f, 28f);
            if (Widgets.ButtonText(clearRect, "TSA_WD_HediffFilter_Clear".Translate()))
            {
                PawnRosterHediffFilter.Clear();
                PawnRosterHediffFilter.InvalidateRosterCaches();
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
            y += 34f;

            IReadOnlyList<PawnRosterHediffRow> all = PawnRosterHediffFilter.GetSnapshotRows(out int total);
            string searchLower = string.IsNullOrEmpty(searchTerm) ? null : searchTerm.Trim().ToLowerInvariant();
            var found = new List<PawnRosterHediffRow>();
            for (int i = 0; i < all.Count; i++)
            {
                PawnRosterHediffRow row = all[i];
                if (searchLower != null && (row.Label == null || !row.Label.ToLowerInvariant().Contains(searchLower)))
                    continue;
                found.Add(row);
            }

            float foundBody = foundExpanded ? found.Count * RowH : 0f;
            float viewH = 48f + foundBody + 24f;
            Rect scrollOut = new Rect(0f, y, inRect.width, inRect.height - y);
            Rect view = new Rect(0f, 0f, inRect.width - 16f, Mathf.Max(scrollOut.height, viewH));
            Widgets.BeginScrollView(scrollOut, ref scroll, view);

            var listing = new Listing_Standard();
            listing.Begin(view);
            if (SettingsUI.DrawCollapsibleHeader(
                listing,
                "TSA_WD_HediffFilter_FoundHeader".Translate(found.Count.ToString(), total.ToString()),
                ref foundExpanded,
                SettingsUI.SectionHeaderColor,
                "TSA_WD_HediffFilter_FoundTip".Translate()))
            {
                DrawRows(listing, found);
            }
            listing.End();
            Widgets.EndScrollView();
        }

        private static void DrawRows(Listing_Standard listing, List<PawnRosterHediffRow> rows)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                PawnRosterHediffRow row = rows[i];
                Rect r = listing.GetRect(RowH);
                if (i % 2 == 0) Widgets.DrawHighlight(r);
                if (Mouse.IsOver(r)) Widgets.DrawLightHighlight(r);

                bool on = PawnRosterHediffFilter.IsSelected(row.Key);
                bool prev = on;
                Rect check = new Rect(r.x + RowPadX, r.y, r.width - CountColW - RowPadX * 2f, r.height);
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.CheckboxLabeled(check, row.Label ?? "", ref on);
                if (on != prev)
                {
                    PawnRosterHediffFilter.SetSelected(row.Key, on);
                    PawnRosterHediffFilter.InvalidateRosterCaches();
                }

                Rect countRect = new Rect(r.xMax - CountColW - RowPadX, r.y, CountColW, r.height);
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(countRect, "TSA_WD_HediffFilter_CountPct".Translate(
                    row.Count.ToString(),
                    row.Percent.ToString("F0")));
                TooltipHandler.TipRegion(countRect, "TSA_WD_HediffFilter_CountTip".Translate(
                    row.Count.ToString(),
                    row.Percent.ToString("F0")));
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
            }
            listing.Gap(4f);
        }
    }
}
