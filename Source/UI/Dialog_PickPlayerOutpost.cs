using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Select a transfer destination for Smart Send Override. Same list chrome as Transfer
    /// (<see cref="PlayerPawnDestinationListUI"/>); select-only, no TryTransfer.
    /// </summary>
    public class Dialog_PickPlayerOutpost : Window
    {
        private readonly List<PlayerPawnRosterEntry> selected;
        private readonly Action<PlayerPawnTransferDestination> onPicked;

        private Vector2 scrollPos;
        private string searchTerm = "";
        private string sortColumn = "Name";
        private bool sortAscending = true;
        private float colNameWidth = 170f;
        private readonly List<PlayerPawnDestinationListUI.DestinationRow> destinations =
            new List<PlayerPawnDestinationListUI.DestinationRow>();
        private string cachedSelectLabel;
        private string cachedSelectTip;

        public override Vector2 InitialSize => new Vector2(850f, 620f);

        public Dialog_PickPlayerOutpost(
            List<PlayerPawnRosterEntry> selected,
            Action<PlayerPawnTransferDestination> onPicked)
        {
            this.selected = selected ?? new List<PlayerPawnRosterEntry>();
            this.onPicked = onPicked;
            doCloseX = true;
            closeOnCancel = true;
            absorbInputAroundWindow = true;
            forcePause = false;
            cachedSelectLabel = "TSA_WD_SmartSend_SelectDest".Translate();
            cachedSelectTip = "TSA_WD_SmartSend_OverrideTip".Translate();
            RebuildDestinations();
        }

        public override void PostClose()
        {
            WdWindowEsc.ClearTextFocusOnClose();
            base.PostClose();
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (WdWindowEsc.TryCloseOnCancel(this))
                return;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 32f), "TSA_WD_SmartSend_OverrideTitle".Translate());
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(0f, 34f, inRect.width, 24f),
                "TSA_WD_PawnTransfer_DialogSubtitle".Translate(selected.Count.ToString()));

            string oldSearch = searchTerm;
            float searchY = 62f;
            Rect searchRect = new Rect(0f, searchY, inRect.width, 28f);
            searchTerm = Widgets.TextField(searchRect, searchTerm);
            if (searchTerm != oldSearch) RebuildDestinations();

            if (string.IsNullOrEmpty(searchTerm))
            {
                GUI.color = new Color(1f, 1f, 1f, 0.4f);
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(searchRect, "  " + "TSA_WD_PawnTransfer_SearchDest".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
            }

            float listY = searchRect.yMax + 8f;
            float fixedCols = PlayerPawnDestinationListUI.FixedColsWidth;
            float contentWidth = Mathf.Max(fixedCols + 120f, inRect.width - PlayerPawnDestinationListUI.ScrollbarWidth);
            colNameWidth = Mathf.Max(120f, contentWidth - fixedCols);
            float tableWidth = fixedCols + colNameWidth;

            Rect headerRect = new Rect(0f, listY, tableWidth, PlayerPawnDestinationListUI.HeaderHeight);
            PlayerPawnDestinationListUI.DrawTableHeader(
                headerRect,
                colNameWidth,
                sortColumn,
                sortAscending,
                OnSort,
                cachedSelectLabel);
            Widgets.DrawLineHorizontal(0f, headerRect.yMax, tableWidth);

            listY = headerRect.yMax + 4f;
            float listH = inRect.height - listY - 10f;
            float contentH = PlayerPawnDestinationListUI.EstimateContentHeight(destinations.Count, offerExitHere: false);
            Widgets.BeginScrollView(new Rect(0f, listY, inRect.width, listH), ref scrollPos,
                new Rect(0f, 0f, tableWidth, Mathf.Max(contentH, listH)));

            float y = 0f;
            int zebraIndex = 0;
            PlayerPawnDestinationListUI.DrawKindSections(
                ref y,
                tableWidth,
                colNameWidth,
                destinations,
                offerExitHere: false,
                ref zebraIndex,
                cachedSelectLabel,
                GetSelectTip,
                row => !row.disabled,
                OnSelect);

            Widgets.EndScrollView();
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        private string GetSelectTip(PlayerPawnDestinationListUI.DestinationRow row) =>
            !string.IsNullOrEmpty(row.disabledTip) ? row.disabledTip : cachedSelectTip;

        private void OnSort(string tag)
        {
            if (sortColumn == tag) sortAscending = !sortAscending;
            else { sortColumn = tag; sortAscending = true; }
            PlayerPawnDestinationListUI.Sort(destinations, sortColumn, sortAscending);
        }

        private void OnSelect(PlayerPawnDestinationListUI.DestinationRow row)
        {
            onPicked?.Invoke(row.dest);
            Close();
        }

        private void RebuildDestinations()
        {
            PlayerPawnDestinationListUI.Rebuild(
                destinations, selected, searchTerm, offerExitHere: false, sortColumn, sortAscending);
        }
    }
}
