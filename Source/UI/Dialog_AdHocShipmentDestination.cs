using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Destination picker for warehouse Ship Now / All Player Gear. Transfer-style list plus
    /// Pick destination on map. Shows travel-mode icon and drop-pod Total Cost when pods are used.
    /// </summary>
    public class Dialog_AdHocShipmentDestination : Window
    {
        private const float HeaderHeight = 28f;
        private const float RowHeight = 40f;
        private const float SectionHeaderHeight = 28f;
        private const float ScrollbarWidth = 16f;
        private const float ColIcon = 48f;
        private const float ColDist = 90f;
        private const float ColSend = 100f;
        private const float ColJump = 90f;
        private const float MapPickBtnH = 32f;

        private readonly List<WorldObject> destinations;
        private readonly WorldObject cameraOrigin;
        private readonly Action<WorldObject> onChosen;
        private readonly Action onPickOnMap;
        private readonly string titleKey;
        private readonly bool viaDropPod;
        private readonly int launchCount;

        private Vector2 scrollPos;
        private string searchTerm = "";
        private float colNameWidth = 220f;

        public override Vector2 InitialSize => new Vector2(720f, 560f);

        public Dialog_AdHocShipmentDestination(
            List<WorldObject> destinations,
            WorldObject cameraOrigin,
            Action<WorldObject> onChosen,
            Action onPickOnMap,
            string titleKey = null,
            bool viaDropPod = false,
            int launchCount = 1)
        {
            this.destinations = destinations ?? new List<WorldObject>();
            this.cameraOrigin = cameraOrigin;
            this.onChosen = onChosen;
            this.onPickOnMap = onPickOnMap;
            this.titleKey = titleKey ?? "TSA_WD_Shipment_DestDialogTitle";
            this.viaDropPod = viaDropPod;
            this.launchCount = Mathf.Max(1, launchCount);
            doCloseX = true;
            closeOnCancel = true;
            absorbInputAroundWindow = true;
            forcePause = false;
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
            Widgets.Label(new Rect(0f, 0f, inRect.width, 32f), titleKey.Translate());
            Text.Font = GameFont.Small;

            float y = 36f;
            y += PlayerPawnDropPodUtility.DrawModeReadoutWithTotalCost(
                new Rect(0f, y, inRect.width, PlayerPawnDropPodUtility.ModeIconSize),
                viaDropPod,
                launchCount);
            y += 6f;

            Rect mapPickRect = new Rect(0f, y, inRect.width, MapPickBtnH);
            TooltipHandler.TipRegion(mapPickRect, "TSA_WD_Shipment_PickOnMapTip".Translate());
            if (Widgets.ButtonText(mapPickRect, "TSA_WD_Shipment_PickOnMap".Translate()))
            {
                onPickOnMap?.Invoke();
                Close();
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            Rect searchRect = new Rect(0f, mapPickRect.yMax + 8f, inRect.width, 28f);
            searchTerm = Widgets.TextField(searchRect, searchTerm);
            if (string.IsNullOrEmpty(searchTerm))
            {
                GUI.color = new Color(1f, 1f, 1f, 0.4f);
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(searchRect, "  " + "TSA_WD_PawnTransfer_SearchDest".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
            }

            float listY = searchRect.yMax + 8f;
            float fixedCols = ColIcon + ColDist + ColSend + ColJump;
            float contentWidth = Mathf.Max(fixedCols + 120f, inRect.width - ScrollbarWidth);
            colNameWidth = Mathf.Max(120f, contentWidth - fixedCols);
            float tableWidth = fixedCols + colNameWidth;

            Rect headerRect = new Rect(0f, listY, tableWidth, HeaderHeight);
            DrawHeader(headerRect);
            Widgets.DrawLineHorizontal(0f, headerRect.yMax, tableWidth);

            listY = headerRect.yMax + 4f;
            float listH = inRect.height - listY - 10f;
            var filtered = FilteredDestinations();
            float contentH = SectionHeaderHeight + 8f + SectionHeaderHeight + 8f
                + filtered.Count * RowHeight + 40f;
            Rect scrollOuter = new Rect(0f, listY, inRect.width, listH);
            Rect viewRect = new Rect(0f, 0f, tableWidth, Mathf.Max(contentH, listH));
            Widgets.BeginScrollView(scrollOuter, ref scrollPos, viewRect);

            float rowY = 0f;
            int zebra = 0;
            DrawSection(ref rowY, tableWidth, "TSA_WD_PawnTransfer_Colonies".Translate(), filtered, isColony: true, ref zebra);
            DrawSection(ref rowY, tableWidth, "TSA_WD_PawnTransfer_Outposts".Translate(), filtered, isColony: false, ref zebra);

            Widgets.EndScrollView();
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        private List<WorldObject> FilteredDestinations()
        {
            if (string.IsNullOrEmpty(searchTerm)) return destinations;
            string lower = searchTerm.ToLowerInvariant();
            var list = new List<WorldObject>();
            for (int i = 0; i < destinations.Count; i++)
            {
                WorldObject d = destinations[i];
                if (d == null) continue;
                string label = Outpost_Warehouse_Delivery.GetDestinationLabelWithKind(d);
                if (label != null && label.ToLowerInvariant().Contains(lower))
                    list.Add(d);
            }
            return list;
        }

        private void DrawHeader(Rect hRect)
        {
            float x = hRect.x;
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Text.Anchor = TextAnchor.LowerCenter;
            Widgets.Label(new Rect(x, hRect.y, ColIcon, hRect.height), "TSA_WD_Prisoners_SmartAssignColType".Translate());
            x += ColIcon;
            Text.Anchor = TextAnchor.LowerLeft;
            Widgets.Label(new Rect(x, hRect.y, colNameWidth, hRect.height), "TSA_WD_PawnCol_Name".Translate());
            x += colNameWidth;
            Text.Anchor = TextAnchor.LowerCenter;
            Widgets.Label(new Rect(x, hRect.y, ColDist, hRect.height), "TSA_WD_Outpost_Dist".Translate());
            x += ColDist;
            Widgets.Label(new Rect(x, hRect.y, ColSend, hRect.height), "TSA_WD_PawnTransfer_SendHere".Translate());
            x += ColSend;
            Widgets.Label(new Rect(x, hRect.y, ColJump, hRect.height), "TSA_WD_ActiveTravelers_H_Jump".Translate());
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawSection(
            ref float y,
            float width,
            string title,
            List<WorldObject> filtered,
            bool isColony,
            ref int zebra)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = Widgets.SeparatorLabelColor;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(4f, y, width - 8f, SectionHeaderHeight), title);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            y += SectionHeaderHeight;
            Widgets.DrawLineHorizontal(0f, y, width);
            y += 4f;

            bool drew = false;
            for (int i = 0; i < filtered.Count; i++)
            {
                WorldObject d = filtered[i];
                bool colony = d is MapParent mp && !(d is WorldObject_WD_Outpost) && mp.HasMap;
                if (colony != isColony) continue;
                DrawRow(ref y, width, d, zebra++ % 2 == 0);
                drew = true;
            }
            if (!drew)
            {
                GUI.color = Color.gray;
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                string empty = isColony
                    ? "TSA_WD_PawnTransfer_NoColonies".Translate()
                    : "TSA_WD_PawnTransfer_NoOutposts".Translate();
                Widgets.Label(new Rect(8f, y, width - 16f, 22f), empty);
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                y += 26f;
            }
        }

        private void DrawRow(ref float y, float width, WorldObject dest, bool highlight)
        {
            Rect r = new Rect(0f, y, width, RowHeight);
            if (highlight) Widgets.DrawHighlight(r);
            if (Mouse.IsOver(r)) Widgets.DrawHighlight(r);

            float x = r.x;
            Texture2D icon = Outpost_Warehouse_Delivery.GetDestinationIcon(dest);
            Rect iconRect = new Rect(x + 8f, r.y + 6f, 28f, 28f);
            if (icon != null)
            {
                GUI.color = WorldOverlayLineMaterials.DarkCyanColor;
                GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
                GUI.color = Color.white;
            }
            x += ColIcon;

            Text.Anchor = TextAnchor.MiddleLeft;
            Text.Font = GameFont.Small;
            string label = Outpost_Warehouse_Delivery.GetDestinationLabelWithKind(dest);
            Widgets.Label(new Rect(x, r.y, colNameWidth - 4f, r.height), label.Truncate(colNameWidth - 8f));
            x += colNameWidth;

            int dist = cameraOrigin != null
                ? Mathf.RoundToInt(Find.WorldGrid.ApproxDistanceInTiles(cameraOrigin.Tile, dest.Tile))
                : 0;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(x, r.y, ColDist, r.height), dist.ToString());
            x += ColDist;

            Rect sendBtn = new Rect(x + 4f, r.y + 6f, ColSend - 8f, r.height - 12f);
            if (Widgets.ButtonText(sendBtn, "TSA_WD_PawnTransfer_SendHere".Translate()))
            {
                onChosen?.Invoke(dest);
                Close();
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
            x += ColSend;

            Rect jumpBtn = new Rect(x + 4f, r.y + 6f, ColJump - 8f, r.height - 12f);
            GUI.enabled = dest != null && !dest.Destroyed;
            if (Widgets.ButtonText(jumpBtn, "TSA_WD_ActiveTravelers_Jump".Translate()) && dest != null)
                WorldDomination_UIUtils.JumpToWorldObjectOnMap(dest);
            GUI.enabled = true;

            Text.Anchor = TextAnchor.UpperLeft;
            y += RowHeight;
        }
    }
}
