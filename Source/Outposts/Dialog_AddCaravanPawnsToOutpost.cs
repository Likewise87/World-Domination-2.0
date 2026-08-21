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
    /// Multi-select add from a parked caravan into a WD outpost.
    /// Layout and styling match <see cref="WITab_Outpost_Pawns"/> (toolbar, zebra table, select column).
    /// </summary>
    public class Dialog_AddCaravanPawnsToOutpost : Window
    {
        private const float ToolbarHeight = 40f;
        private const float ToolbarBtnGap = 10f;
        private const float TransferBtnWidth = 180f;
        private const float TransferBtnRightInset = 8f;
        private const float SelectedLabelWidth = 130f;
        private const float DeselectBtnWidth = 170f;
        private const float SelectAllBtnWidth = 120f;
        private const float ColPortrait = 40f;
        private const float ColPawnType = 96f;
        private const float ColName = 240f;
        private const float ColSelect = 36f;
        private const float ColAge = 44f;
        private const float ColSkill = 56f;
        private const float ColStrength = 72f;
        private const float RowHeight = 40f;
        private const float HeaderHeight = 36f;
        private const float BottomBarHeight = 38f;
        private static readonly Vector2 PortraitSize = new Vector2(36f, 36f);

        private readonly WorldObject_WD_Outpost outpost;
        private readonly Caravan caravan;
        private readonly HashSet<string> selectedThingIds = new HashSet<string>();
        private readonly List<Row> rows = new List<Row>();
        private Vector2 scrollPos;
        private float scrollViewHeight = 120f;
        private float lastScrollViewportHeight = 400f;
        private float colNameWidth = ColName;
        private string? lastRejectTip;

        private sealed class Row
        {
            public Pawn pawn = null!;
            public string thingId = "";
            public string nameLabel = "";
            public string typeLabel = "";
            public string ageLabel = "-";
            public string shootingLabel = "-";
            public string meleeLabel = "-";
            public string strengthLabel = "-";
            public string portraitKey = "";
            public bool isSlave;
            public bool sparseSkills;
            public VirtualPawnSummary? summary;
        }

        public override Vector2 InitialSize => new Vector2(920f, 640f);

        public Dialog_AddCaravanPawnsToOutpost(WorldObject_WD_Outpost outpost, Caravan caravan)
        {
            this.outpost = outpost;
            this.caravan = caravan;
            doCloseX = true;
            absorbInputAroundWindow = true;
            forcePause = false;
            RebuildRows(selectAll: true);
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (outpost == null || outpost.Destroyed || caravan == null || caravan.Destroyed)
            {
                Close();
                return;
            }

            PawnRosterPaintSelect.BeginFrame(this);
            RebuildRows(selectAll: false);
            PruneStaleSelection();

            if (rows.Count == 0)
            {
                Messages.Message("TSA_WD_AddToOutpost_NoValidPawns".Translate(), MessageTypeDefOf.RejectInput, false);
                Close();
                return;
            }

            Rect body = inRect;
            Rect bottomBar = new Rect(body.x, body.yMax - BottomBarHeight, body.width, BottomBarHeight);
            Rect content = new Rect(body.x, body.y, body.width, body.height - BottomBarHeight - 6f);

            GUI.BeginGroup(content);

            float tableInnerWidth = content.width - 16f;
            if (tableInnerWidth < 50f) tableInnerWidth = content.width;
            UpdateFlexibleNameWidth(tableInnerWidth);
            float totalTableWidth = ComputeTotalTableWidth();

            // Match WITab_Outpost_Pawns toolbar: Medium title, Selected + primary action on the right.
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, content.width * 0.35f, 32f), "TSA_WD_AddToOutpost_DialogTitle".Translate());

            bool parked = Outpost_EstablishmentRequirements.CaravanParkedOnTileForAddToOutpost(
                caravan, outpost.Tile, out string parkedReason);
            string selectionReject = "";
            bool selectionOk = selectedThingIds.Count > 0
                && PlayerPawnTransferUtility.ValidateCaravanAddToOutpostSelection(
                    outpost, caravan, selectedThingIds, out selectionReject);

            Rect confirmBtn = new Rect(content.width - TransferBtnWidth - TransferBtnRightInset, 4f, TransferBtnWidth, 30f);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleRight;
            Rect selectedRect = new Rect(confirmBtn.x - ToolbarBtnGap - SelectedLabelWidth, 6f, SelectedLabelWidth, 28f);
            Widgets.Label(selectedRect, "TSA_WD_AllPlayerPawns_Selected".Translate(selectedThingIds.Count.ToString()));
            Text.Anchor = TextAnchor.UpperLeft;
            TooltipHandler.TipRegion(selectedRect, "TSA_WD_AllPlayerPawns_SelectedTip".Translate());

            Rect deselectBtn = new Rect(selectedRect.x - ToolbarBtnGap - DeselectBtnWidth, 4f, DeselectBtnWidth, 30f);
            TooltipHandler.TipRegion(deselectBtn, "TSA_WD_AllPlayerPawns_DeselectAllTip".Translate());
            GUI.enabled = selectedThingIds.Count > 0;
            if (Widgets.ButtonText(deselectBtn, "TSA_WD_AllPlayerPawns_DeselectAll".Translate()))
            {
                selectedThingIds.Clear();
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
            GUI.enabled = true;

            Rect selectAllBtn = new Rect(deselectBtn.x - ToolbarBtnGap - SelectAllBtnWidth, 4f, SelectAllBtnWidth, 30f);
            if (Widgets.ButtonText(selectAllBtn, "TSA_WD_AddToOutpost_SelectAll".Translate()))
            {
                SelectAll();
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            string confirmTip = !parked
                ? (parkedReason ?? "")
                : (!selectionOk ? selectionReject : "TSA_WD_AddToOutpost_ConfirmTip".Translate());
            TooltipHandler.TipRegion(confirmBtn, confirmTip);
            GUI.enabled = parked && selectionOk;
            if (Widgets.ButtonText(confirmBtn, "TSA_WD_AddToOutpost_Confirm".Translate()))
                TryConfirm();
            GUI.enabled = true;

            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(0f, 34f, Mathf.Min(420f, selectAllBtn.x - 8f), 18f),
                "TSA_WD_AddToOutpost_DialogSubtitle".Translate(outpost.LabelCap, caravan.LabelCap));
            GUI.color = Color.white;

            float headerTop = ToolbarHeight + 4f + 14f;
            float headerCurY = headerTop;
            DoTableHeader(ref headerCurY);
            Widgets.DrawLineHorizontal(0f, headerTop + HeaderHeight, content.width);

            float listTopY = headerTop + HeaderHeight + 4f;
            Rect listScrollArea = new Rect(0f, listTopY, content.width, content.height - listTopY);
            lastScrollViewportHeight = listScrollArea.height;

            float viewHeight = Mathf.Max(scrollViewHeight, 80f);
            if (viewHeight < listScrollArea.height)
                viewHeight = listScrollArea.height;
            Rect rowViewRect = new Rect(0f, 0f, Mathf.Max(totalTableWidth, tableInnerWidth), viewHeight);

            Widgets.BeginScrollView(listScrollArea, ref scrollPos, rowViewRect);

            float curY = 0f;
            Text.Font = GameFont.Tiny;
            for (int i = 0; i < rows.Count; i++)
                DoPawnRow(ref curY, rowViewRect, rows[i], i % 2 == 0);

            if (Event.current.type == EventType.Layout)
                scrollViewHeight = curY + 8f;

            Widgets.EndScrollView();
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.EndGroup();

            DrawBottomBar(bottomBar, parked, selectionOk, parkedReason ?? "", selectionReject);
        }

        private void UpdateFlexibleNameWidth(float availableInnerWidth)
        {
            float fixedW = ColPortrait + ColPawnType + ColSelect + ColAge + ColSkill + ColSkill + ColStrength;
            colNameWidth = Mathf.Max(ColName, availableInnerWidth - fixedW);
        }

        private float ComputeTotalTableWidth() =>
            ColPortrait + ColPawnType + colNameWidth + ColSelect + ColAge + ColSkill + ColSkill + ColStrength;

        private void DoTableHeader(ref float curY)
        {
            float x = 0f;
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;

            x += ColPortrait;
            DrawHeaderCell(ref x, curY, ColPawnType, "TSA_WD_AllPlayerPawns_ColPawnType".Translate(), centered: true);
            DrawHeaderCell(ref x, curY, colNameWidth, "TSA_WD_PawnCol_Name".Translate(), centered: false);
            DrawSelectHeader(ref x, curY);
            DrawHeaderCell(ref x, curY, ColAge, "TSA_WD_PawnCol_Age".Translate(), centered: true);
            DrawHeaderCell(ref x, curY, ColSkill, SkillDefOf.Shooting.LabelCap, centered: true);
            DrawHeaderCell(ref x, curY, ColSkill, "TSA_WD_PawnCol_Melee".Translate(), centered: true);
            DrawHeaderCell(ref x, curY, ColStrength, "TSA_WD_PawnCol_ResultingStrength".Translate(), centered: true);

            GUI.color = Color.white;
            curY += HeaderHeight;
        }

        private static void DrawHeaderCell(ref float x, float curY, float width, string label, bool centered)
        {
            Rect r = new Rect(x, curY, width, HeaderHeight);
            if (Mouse.IsOver(r)) Widgets.DrawHighlight(r);
            Text.Anchor = centered ? TextAnchor.LowerCenter : TextAnchor.LowerLeft;
            Widgets.Label(r, (label ?? "").Truncate(width - 4f));
            Text.Anchor = TextAnchor.UpperLeft;
            x += width;
        }

        private void DrawSelectHeader(ref float x, float curY)
        {
            Rect selHdr = new Rect(x, curY, ColSelect, HeaderHeight);
            if (Mouse.IsOver(selHdr)) Widgets.DrawHighlight(selHdr);
            TooltipHandler.TipRegion(selHdr, "TSA_WD_PawnCol_SelectColumnTip".Translate());
            x += ColSelect;
        }

        private void DoPawnRow(ref float curY, Rect viewRect, Row row, bool zebra)
        {
            float visibleY = scrollPos.y - RowHeight;
            float visibleYMax = scrollPos.y + lastScrollViewportHeight;
            bool visible = curY >= visibleY && curY < visibleYMax;

            if (visible && row?.pawn != null)
            {
                Rect rowRect = new Rect(0f, curY, viewRect.width, RowHeight);
                if (zebra) Widgets.DrawHighlight(rowRect);
                if (Mouse.IsOver(rowRect)) Widgets.DrawLightHighlight(rowRect);
                if (row.isSlave)
                {
                    Color nameTint = PawnNameColorUtility.PawnNameColorOf(row.pawn);
                    Color rowBg = new Color(
                        Mathf.Clamp01(nameTint.r * 0.28f + 0.08f),
                        Mathf.Clamp01(nameTint.g * 0.28f + 0.06f),
                        Mathf.Clamp01(nameTint.b * 0.12f + 0.02f),
                        0.21f);
                    Widgets.DrawBoxSolid(rowRect, rowBg);
                }

                Color prevGui = GUI.color;
                if (row.isSlave)
                    GUI.color = PawnNameColorUtility.PawnNameColorOf(row.pawn);

                Text.Font = GameFont.Tiny;
                float x = 0f;

                Rect cell = new Rect(x, curY, ColPortrait, RowHeight);
                Texture? portrait = GetRowPortrait(row);
                Rect portraitRect = new Rect(
                    cell.x + (cell.width - PortraitSize.x) / 2f,
                    curY + (RowHeight - PortraitSize.y) / 2f,
                    PortraitSize.x,
                    PortraitSize.y);
                if (portrait != null)
                    GUI.DrawTexture(portraitRect, portrait, ScaleMode.ScaleToFit);
                else
                    Widgets.DrawBoxSolid(portraitRect, new Color(0.3f, 0.3f, 0.35f, 1f));
                if (Widgets.ButtonInvisible(cell))
                    Find.WindowStack.Add(new Dialog_InfoCard(row.pawn));
                x += ColPortrait;

                Text.Anchor = TextAnchor.MiddleCenter;
                cell = new Rect(x, curY, ColPawnType, RowHeight);
                Widgets.Label(cell, (row.typeLabel ?? "-").Truncate(ColPawnType - 4f));
                x += ColPawnType;

                cell = new Rect(x, curY, colNameWidth, RowHeight);
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(cell, (row.nameLabel ?? "-").Truncate(colNameWidth - 4f));
                Text.Anchor = TextAnchor.UpperLeft;
                if (Widgets.ButtonInvisible(cell))
                    Find.WindowStack.Add(new Dialog_InfoCard(row.pawn));
                x += colNameWidth;

                DrawRowSelectCheckbox(ref x, curY, row);

                Text.Anchor = TextAnchor.MiddleCenter;
                cell = new Rect(x, curY, ColAge, RowHeight);
                Widgets.Label(cell, (row.ageLabel ?? "-").Truncate(ColAge - 2f));
                x += ColAge;

                cell = new Rect(x, curY, ColSkill, RowHeight);
                Widgets.Label(cell, row.shootingLabel ?? "-");
                x += ColSkill;

                cell = new Rect(x, curY, ColSkill, RowHeight);
                Widgets.Label(cell, row.meleeLabel ?? "-");
                x += ColSkill;

                cell = new Rect(x, curY, ColStrength, RowHeight);
                Widgets.Label(cell, row.strengthLabel ?? "-");
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = prevGui;

                if (row.isSlave)
                    TooltipHandler.TipRegion(new Rect(0f, curY, x + ColStrength, RowHeight), "TSA_WD_PawnRow_SlaveRowTip".Translate());
            }

            curY += RowHeight;
        }

        private void DrawRowSelectCheckbox(ref float x, float curY, Row row)
        {
            Rect selectColRect = new Rect(x, curY, ColSelect, RowHeight);
            bool canInteract = PlayerPawnTransferUtility.CaravanAddBulkSelectionIsAllowedWithToggle(
                outpost, caravan, selectedThingIds, row.thingId, out string reject);
            if (!canInteract && !reject.NullOrEmpty())
            {
                lastRejectTip = reject;
                TooltipHandler.TipRegion(selectColRect, reject);
            }
            else if (!canInteract)
            {
                TooltipHandler.TipRegion(selectColRect,
                    lastRejectTip ?? "TSA_WD_Pawns_RemoveSlaveAccompanimentRequiredTip".Translate());
            }

            float cx = x + (ColSelect - 24f) * 0.5f;
            float cy = curY + (RowHeight - 24f) * 0.5f;
            PawnRosterPaintSelect.Draw(this, selectColRect, cx, cy, 24f, row.thingId, selectedThingIds, canInteract);
            x += ColSelect;
        }

        private Texture? GetRowPortrait(Row row)
        {
            if (row.pawn == null) return null;
            if (row.pawn.RaceProps?.Humanlike == true)
                return PawnPortraitUIUtils.GetPortrait(row.pawn, PortraitSize, row.portraitKey);
            return row.pawn.def?.uiIcon ?? row.pawn.kindDef?.race?.uiIcon;
        }

        private void DrawBottomBar(Rect rect, bool parked, bool selectionOk, string parkedReason, string selectionReject)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            string footer;
            if (!parked)
                footer = parkedReason;
            else if (!selectionOk && selectedThingIds.Count > 0)
                footer = selectionReject;
            else if (selectedThingIds.Count == 0)
                footer = "TSA_WD_AddToOutpost_FooterPick".Translate();
            else if (IsFullDissolveSelection())
                footer = "TSA_WD_AddToOutpost_FooterDissolve".Translate();
            else
                footer = "TSA_WD_AddToOutpost_FooterLeftover".Translate();

            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(rect.x + 4f, rect.y, rect.width - 8f, rect.height), footer);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private bool IsFullDissolveSelection()
        {
            if (caravan?.PawnsListForReading == null || selectedThingIds.Count == 0) return false;
            var list = caravan.PawnsListForReading;
            for (int i = 0; i < list.Count; i++)
            {
                Pawn p = list[i];
                if (p == null || p.Destroyed || p.Dead || p.ThingID.NullOrEmpty()) continue;
                if (!selectedThingIds.Contains(p.ThingID))
                    return false;
            }
            return true;
        }

        private void TryConfirm()
        {
            if (!Outpost_EstablishmentRequirements.CaravanParkedOnTileForAddToOutpost(
                    caravan, outpost.Tile, out string parkedReason))
            {
                Messages.Message(parkedReason ?? "", MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (!PlayerPawnTransferUtility.TryAddSelectedCaravanPawnsToOutpost(
                    outpost, caravan, selectedThingIds, out string reject))
            {
                Messages.Message(reject ?? "TSA_WD_PawnTransfer_NoSelection".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            SoundDefOf.Click.PlayOneShotOnCamera();
            Close();
        }

        private void RebuildRows(bool selectAll)
        {
            rows.Clear();
            if (selectAll)
                selectedThingIds.Clear();

            var list = caravan?.PawnsListForReading;
            if (list == null) return;

            for (int i = 0; i < list.Count; i++)
            {
                Pawn p = list[i];
                if (p == null || p.Destroyed || p.Dead || p.ThingID.NullOrEmpty()) continue;

                var cat = PlayerPawnRosterUtility.ClassifyPawn(p);
                var v = VirtualPawnSummary.FromPawn(p);
                bool sparse = cat != PlayerPawnSortCategory.Human;
                var row = new Row
                {
                    pawn = p,
                    thingId = p.ThingID,
                    nameLabel = p.Name?.ToStringFull ?? p.Label ?? "-",
                    typeLabel = PlayerPawnRosterUtility.GetPawnTypeLabel(cat),
                    isSlave = OutpostPawnIdeologyUtil.IsSlaveHumanlike(p),
                    sparseSkills = sparse,
                    summary = v,
                    portraitKey = PawnPortraitUIUtils.BuildCacheKey(p, v)
                };

                if (sparse || v == null)
                {
                    row.ageLabel = "-";
                    row.shootingLabel = "-";
                    row.meleeLabel = "-";
                    row.strengthLabel = "-";
                }
                else
                {
                    try
                    {
                        row.ageLabel = p.ageTracker != null
                            ? p.ageTracker.AgeBiologicalYears.ToString()
                            : v.biologicalAgeYears.ToString("F0");
                    }
                    catch
                    {
                        row.ageLabel = v.biologicalAgeYears.ToString("F0");
                    }
                    row.shootingLabel = v.shooting.ToString();
                    row.meleeLabel = v.melee.ToString();
                    row.strengthLabel = v.CombatStrength.ToString("F0");
                }

                rows.Add(row);
                if (selectAll)
                    selectedThingIds.Add(row.thingId);
            }

            rows.Sort((a, b) =>
            {
                int c = a.sparseSkills.CompareTo(b.sparseSkills);
                if (c != 0) return c;
                return string.Compare(a.nameLabel, b.nameLabel, StringComparison.OrdinalIgnoreCase);
            });
        }

        private void SelectAll()
        {
            selectedThingIds.Clear();
            for (int i = 0; i < rows.Count; i++)
            {
                if (!rows[i].thingId.NullOrEmpty())
                    selectedThingIds.Add(rows[i].thingId);
            }
        }

        private void PruneStaleSelection()
        {
            if (selectedThingIds.Count == 0) return;
            var live = new HashSet<string>();
            for (int i = 0; i < rows.Count; i++)
            {
                if (!rows[i].thingId.NullOrEmpty())
                    live.Add(rows[i].thingId);
            }

            List<string>? drop = null;
            foreach (string id in selectedThingIds)
            {
                if (!live.Contains(id))
                {
                    drop ??= new List<string>();
                    drop.Add(id);
                }
            }
            if (drop == null) return;
            for (int i = 0; i < drop.Count; i++)
                selectedThingIds.Remove(drop[i]);
        }
    }
}
