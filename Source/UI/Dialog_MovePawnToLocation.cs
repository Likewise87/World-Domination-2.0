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
    /// Destination picker for pawn transfer. List chrome shared via <see cref="PlayerPawnDestinationListUI"/>.
    /// </summary>
    public class Dialog_MovePawnToLocation : Window
    {
        private readonly List<PlayerPawnRosterEntry> selected;
        private readonly Action? onTransferred;
        private readonly bool offerExitHere;

        private Vector2 scrollPos;
        private string searchTerm = "";
        private string sortColumn = "Name";
        private bool sortAscending = true;
        private float colNameWidth = 170f;
        private readonly List<PlayerPawnDestinationListUI.DestinationRow> destinations =
            new List<PlayerPawnDestinationListUI.DestinationRow>();

        public override Vector2 InitialSize => new Vector2(850f, 620f);

        public Dialog_MovePawnToLocation(List<PlayerPawnRosterEntry> selected, Action? onTransferred = null, bool offerExitHere = false)
        {
            this.selected = selected ?? new List<PlayerPawnRosterEntry>();
            this.onTransferred = onTransferred;
            this.offerExitHere = offerExitHere;
            doCloseX = true;
            closeOnCancel = true;
            absorbInputAroundWindow = true;
            forcePause = false;
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
            Widgets.Label(new Rect(0f, 0f, inRect.width, 32f), "TSA_WD_PawnTransfer_DialogTitle".Translate());

            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(0f, 34f, inRect.width, 28f),
                "TSA_WD_PawnTransfer_DialogSubtitle".Translate(selected.Count.ToString()));

            int launchCount = selected.Count;
            float modeH = PlayerPawnDropPodUtility.DrawAdHocModeAndCostStrip(
                new Rect(0f, 64f, inRect.width, 80f),
                launchCount);

            float mapPickY = 64f + modeH + 4f;
            Rect mapPickRect = new Rect(0f, mapPickY, inRect.width, 32f);
            TooltipHandler.TipRegion(mapPickRect, "TSA_WD_Shipment_PickOnMapTip".Translate());
            if (Widgets.ButtonText(mapPickRect, "TSA_WD_Shipment_PickOnMap".Translate()))
            {
                StartPickDestinationOnMap();
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            string oldSearch = searchTerm;
            float searchY = mapPickRect.yMax + 8f;
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
                "TSA_WD_PawnTransfer_SendHere".Translate());
            Widgets.DrawLineHorizontal(0f, headerRect.yMax, tableWidth);

            listY = headerRect.yMax + 4f;
            float listH = inRect.height - listY - 10f;
            float contentH = PlayerPawnDestinationListUI.EstimateContentHeight(destinations.Count, offerExitHere);
            Rect scrollOuter = new Rect(0f, listY, inRect.width, listH);
            Rect viewRect = new Rect(0f, 0f, tableWidth, Mathf.Max(contentH, listH));
            Widgets.BeginScrollView(scrollOuter, ref scrollPos, viewRect);

            float y = 0f;
            int zebraIndex = 0;
            PlayerPawnDestinationListUI.DrawKindSections(
                ref y,
                tableWidth,
                colNameWidth,
                destinations,
                offerExitHere,
                ref zebraIndex,
                "TSA_WD_PawnTransfer_SendHere".Translate(),
                GetSendTip,
                IsSendEnabled,
                OnSendHere);

            Widgets.EndScrollView();
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        private void OnSort(string tag)
        {
            if (sortColumn == tag) sortAscending = !sortAscending;
            else { sortColumn = tag; sortAscending = true; }
            PlayerPawnDestinationListUI.Sort(destinations, sortColumn, sortAscending);
        }

        private string GetSendTip(PlayerPawnDestinationListUI.DestinationRow row)
        {
            if (!string.IsNullOrEmpty(row.disabledTip)) return row.disabledTip;
            bool viaDropPod = PlayerPawnDropPodUtility.AdHocViaDropPod;
            if (viaDropPod && row.dest.kind == PlayerPawnTransferDestinationKind.ExitHere)
                return "TSA_WD_PawnDropPod_NoExitHere".Translate();
            if (viaDropPod
                && row.dest.kind != PlayerPawnTransferDestinationKind.ExitHere
                && !PlayerPawnDestinationListUI.OriginsAllInDropPodRange(selected, row.dest.Tile))
                return "TSA_WD_PawnDropPod_OutOfRange".Translate();
            return "TSA_WD_AllPlayerPawns_TransferTip".Translate();
        }

        private bool IsSendEnabled(PlayerPawnDestinationListUI.DestinationRow row)
        {
            if (row.disabled) return false;
            bool viaDropPod = PlayerPawnDropPodUtility.AdHocViaDropPod;
            if (viaDropPod && row.dest.kind == PlayerPawnTransferDestinationKind.ExitHere)
                return false;
            if (viaDropPod
                && row.dest.kind != PlayerPawnTransferDestinationKind.ExitHere
                && !PlayerPawnDestinationListUI.OriginsAllInDropPodRange(selected, row.dest.Tile))
                return false;
            return true;
        }

        private void OnSendHere(PlayerPawnDestinationListUI.DestinationRow row)
        {
            bool viaDropPod = PlayerPawnDropPodUtility.AdHocViaDropPod;
            PlayerPawnTransferUtility.TryTransfer(selected, row.dest, viaDropPod: viaDropPod);
            onTransferred?.Invoke();
            Close();
        }

        private void RebuildDestinations()
        {
            PlayerPawnDestinationListUI.Rebuild(
                destinations, selected, searchTerm, offerExitHere, sortColumn, sortAscending);
        }

        private void StartPickDestinationOnMap()
        {
            var pending = new List<PlayerPawnRosterEntry>(selected);
            Action? transferred = onTransferred;
            bool viaDropPod = PlayerPawnDropPodUtility.AdHocViaDropPod;
            WorldObject cameraOrigin = null;
            for (int i = 0; i < pending.Count; i++)
            {
                PlayerPawnRosterEntry e = pending[i];
                if (e?.sourceOutpost != null && !e.sourceOutpost.Destroyed)
                {
                    cameraOrigin = e.sourceOutpost;
                    break;
                }
                if (e?.mapParent != null && !e.mapParent.Destroyed)
                {
                    cameraOrigin = e.mapParent;
                    break;
                }
            }

            Close();
            PlayerPawnDropPodUtility.PrepareWorldMapDestinationPick(cameraOrigin);
            Outpost_Warehouse_Delivery.CyanDeliveryMouseOverlayActive = true;
            Find.WorldTargeter.BeginTargeting(
                target =>
                {
                    if (!TryResolveTransferDestination(target.WorldObject, out PlayerPawnTransferDestination dest, out string rejectKey))
                    {
                        Messages.Message(
                            rejectKey.NullOrEmpty()
                                ? "TSA_WD_Warehouse_InvalidDestination".Translate()
                                : rejectKey.Translate(),
                            MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    if (viaDropPod && dest.kind == PlayerPawnTransferDestinationKind.ExitHere)
                    {
                        Messages.Message("TSA_WD_PawnDropPod_NoExitHere".Translate(), MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    PlayerPawnTransferUtility.TryTransfer(pending, dest, viaDropPod: viaDropPod);
                    transferred?.Invoke();
                    return true;
                },
                true,
                Outpost_Warehouse_Delivery.GetDeliveryTargetMouseIcon(),
                false,
                null,
                null);
        }

        private static bool TryResolveTransferDestination(
            WorldObject wo,
            out PlayerPawnTransferDestination dest,
            out string rejectKey)
        {
            dest = default;
            rejectKey = "TSA_WD_Warehouse_InvalidDestination";
            if (wo == null || wo.Destroyed || !wo.Spawned) return false;
            if (WorldActions_Utils.IsSpace(wo)) return false;
            if (!PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(wo.Tile)) return false;

            if (wo is WorldObject_WD_Outpost outpost && outpost.Faction == Faction.OfPlayer)
            {
                dest = new PlayerPawnTransferDestination
                {
                    kind = PlayerPawnTransferDestinationKind.Outpost,
                    outpost = outpost
                };
                rejectKey = null;
                return true;
            }

            if (wo is MapParent mp && mp.Faction == Faction.OfPlayer && mp.HasMap && wo is not WorldObject_WD_Outpost)
            {
                dest = new PlayerPawnTransferDestination
                {
                    kind = PlayerPawnTransferDestinationKind.Colony,
                    colony = mp
                };
                rejectKey = null;
                return true;
            }

            return false;
        }
    }
}
