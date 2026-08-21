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
    /// When transferring from an outpost costs more offensive strength than available:
    /// Form Caravan, Leave at Outpost, or Mark as Lost until under budget.
    /// </summary>
    public class Dialog_OutpostStrengthWithdraw : Window
    {
        private enum WithdrawSortColumn
        {
            Name,
            Gear,
            Shooting,
            Melee,
            Health,
            Strength
        }

        private enum PawnFate
        {
            FormCaravan,
            LeaveAtOutpost,
            MarkLost
        }

        private readonly WorldObject_WD_Outpost outpost;
        private readonly List<PlayerPawnRosterEntry> entries;
        private readonly List<PlayerPawnRosterEntry> visibleRows = new List<PlayerPawnRosterEntry>();
        private readonly float available;
        /// <summary>take, stay, lost, willEmptyOutpost (already confirmed by the player in this dialog).</summary>
        private readonly Action<List<PlayerPawnRosterEntry>, List<Pawn>, List<Pawn>, bool> onResolved;
        private readonly Dictionary<string, PawnFate> fateByThingId = new Dictionary<string, PawnFate>();
        private Vector2 scrollPosition;
        private bool resolved;
        private string nameSearchTerm = "";
        private WithdrawSortColumn sortColumn = WithdrawSortColumn.Strength;
        private bool sortAscending;
        private const float HeaderHeight = 36f;
        private const float FateBtnW = 74f;
        private const float FateBtnH = 28f;
        private const float FateBtnGap = 4f;
        private const float ActionColW = FateBtnW * 3f + FateBtnGap * 2f + 8f;
        private static readonly Color LeaveTint = new Color(0.95f, 0.82f, 0.35f);
        private static readonly Color NavSlateFill = new Color(0.16f, 0.18f, 0.22f, 0.92f);
        private static readonly Color NavBtnBgHover = new Color(0.22f, 0.26f, 0.32f, 0.96f);
        private static readonly Color NavBtnBgPress = new Color(0.12f, 0.14f, 0.17f, 0.96f);
        private static readonly Color NavBtnBgSelected = new Color(0.22f, 0.26f, 0.32f, 0.96f);
        private static readonly Color NavBtnOutline = new Color(0.55f, 0.62f, 0.72f, 0.42f);
        private static readonly Color NavBtnOutlineHover = new Color(0.78f, 0.84f, 0.92f, 0.72f);
        private static readonly Color NavBtnOutlineSelected = new Color(0.70f, 0.76f, 0.86f, 0.55f);

        public override Vector2 InitialSize => new Vector2(980f, 740f);

        private static float FixedColumnsWidth =>
            OutpostStrengthBudgetUi.ColIconDeploy
            + OutpostStrengthBudgetUi.ColGear
            + OutpostStrengthBudgetUi.ColShoot
            + OutpostStrengthBudgetUi.ColMelee
            + OutpostStrengthBudgetUi.ColHealth
            + OutpostStrengthBudgetUi.ColStrengthDeploy
            + ActionColW;

        public Dialog_OutpostStrengthWithdraw(
            WorldObject_WD_Outpost outpost,
            List<PlayerPawnRosterEntry> entries,
            float available,
            Action<List<PlayerPawnRosterEntry>, List<Pawn>, List<Pawn>, bool> onResolved)
        {
            this.outpost = outpost;
            this.entries = entries ?? new List<PlayerPawnRosterEntry>();
            this.available = available;
            this.onResolved = onResolved;
            for (int i = 0; i < this.entries.Count; i++)
            {
                PlayerPawnRosterEntry e = this.entries[i];
                if (e?.thingId == null) continue;
                fateByThingId[e.thingId] = PawnFate.FormCaravan;
            }
            RebuildVisibleRows();
            doCloseX = true;
            doCloseButton = false;
            absorbInputAroundWindow = true;
            forcePause = true;
            closeOnAccept = false;
            closeOnCancel = false;
        }

        private PawnFate GetFate(string thingId)
        {
            if (thingId.NullOrEmpty()) return PawnFate.FormCaravan;
            return fateByThingId.TryGetValue(thingId, out PawnFate f) ? f : PawnFate.FormCaravan;
        }

        private float CurrentTransferCost()
        {
            float cost = 0f;
            for (int i = 0; i < entries.Count; i++)
            {
                PlayerPawnRosterEntry e = entries[i];
                if (e?.pawn == null) continue;
                if (GetFate(e.thingId) != PawnFate.FormCaravan) continue;
                cost += OutpostStrengthBudget.GetPawnCost(e.pawn);
            }
            return cost;
        }

        private void RebuildVisibleRows()
        {
            visibleRows.Clear();
            string search = string.IsNullOrEmpty(nameSearchTerm) ? null : nameSearchTerm.Trim().ToLowerInvariant();
            for (int i = 0; i < entries.Count; i++)
            {
                PlayerPawnRosterEntry e = entries[i];
                if (e?.pawn == null) continue;
                if (search != null)
                {
                    string label = e.pawn.LabelShortCap ?? "";
                    if (!label.ToLowerInvariant().Contains(search))
                        continue;
                }
                visibleRows.Add(e);
            }
            SortVisibleRows();
        }

        private void SortVisibleRows()
        {
            visibleRows.Sort((a, b) =>
            {
                int cmp = CompareWithdraw(a?.pawn, b?.pawn, sortColumn);
                if (cmp == 0)
                    cmp = string.Compare(a?.pawn?.LabelShortCap, b?.pawn?.LabelShortCap, StringComparison.OrdinalIgnoreCase);
                return sortAscending ? cmp : -cmp;
            });
        }

        private static int CompareWithdraw(Pawn a, Pawn b, WithdrawSortColumn col)
        {
            switch (col)
            {
                case WithdrawSortColumn.Name:
                    return string.Compare(a?.LabelShortCap, b?.LabelShortCap, StringComparison.OrdinalIgnoreCase);
                case WithdrawSortColumn.Gear:
                    return CountGear(a).CompareTo(CountGear(b));
                case WithdrawSortColumn.Shooting:
                    return OutpostStrengthBudgetUi.GetSkillLevel(a, SkillDefOf.Shooting)
                        .CompareTo(OutpostStrengthBudgetUi.GetSkillLevel(b, SkillDefOf.Shooting));
                case WithdrawSortColumn.Melee:
                    return OutpostStrengthBudgetUi.GetSkillLevel(a, SkillDefOf.Melee)
                        .CompareTo(OutpostStrengthBudgetUi.GetSkillLevel(b, SkillDefOf.Melee));
                case WithdrawSortColumn.Health:
                    return OutpostStrengthBudgetUi.GetHealthPercent(a)
                        .CompareTo(OutpostStrengthBudgetUi.GetHealthPercent(b));
                default:
                    return OutpostStrengthBudget.GetPawnCost(a).CompareTo(OutpostStrengthBudget.GetPawnCost(b));
            }
        }

        private static int CountGear(Pawn pawn)
        {
            int n = 0;
            if (pawn?.equipment?.AllEquipmentListForReading != null)
                n += pawn.equipment.AllEquipmentListForReading.Count;
            if (pawn?.apparel?.WornApparel != null)
                n += pawn.apparel.WornApparel.Count;
            return n;
        }

        public override void PostClose()
        {
            PawnRosterHeaderFilter.CloseDropdown();
            base.PostClose();
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (PawnRosterHeaderFilter.TryCloseDropdownOnCancel())
                return;

            float y = 0f;
            Text.Font = GameFont.Medium;
            string outpostName = outpost?.LabelCap.ToString() ?? "";
            string outpostType = outpost?.def?.LabelCap.ToString() ?? "";
            string title = "TSA_WD_StrengthBudget_WithdrawTitle".Translate(outpostName, outpostType);
            Rect titleRect = new Rect(0f, y, inRect.width, Outpost_Dialog_UI.DialogTitleHeight);
            OutpostStrengthBudgetUi.LabelAnchored(titleRect, title.Truncate(inRect.width - 8f), TextAnchor.MiddleCenter);
            TooltipHandler.TipRegion(titleRect, title);
            y += Outpost_Dialog_UI.DialogTitleRowAdvance;
            Text.Font = GameFont.Small;

            float transferCost = CurrentTransferCost();
            bool under = OutpostStrengthBudget.IsUnderWithdrawBudget(transferCost, available);
            float excess = OutpostStrengthBudget.WithdrawExcess(transferCost, available);
            y += 10f;
            string caption = "TSA_WD_StrengthBudget_WithdrawCaption".Translate(
                transferCost.ToString("F0"),
                available.ToString("F0"));
            float captionH = Mathf.Max(OutpostStrengthBudgetUi.BoxLineH, Text.CalcHeight(caption, inRect.width));
            Rect captionRect = new Rect(0f, y, inRect.width, captionH);
            OutpostStrengthBudgetUi.LabelAnchored(captionRect, caption, TextAnchor.MiddleCenter);
            TooltipHandler.TipRegion(captionRect, "TSA_WD_StrengthBudget_WithdrawCaptionTip".Translate());
            y += captionH + 2f;
            string tip = under
                ? null
                : "TSA_WD_StrengthBudget_NeedStayBehind".Translate(excess.ToString("F0")).ToString();
            y = OutpostStrengthBudgetUi.DrawWithdrawMeterBox(0f, y, inRect.width, transferCost, available, tip);
            y += 10f;

            float rowH = OutpostStrengthBudgetUi.DeployRowHeight;
            float tableBottom = inRect.height - OutpostStrengthBudgetUi.BottomH - 8f;
            const float scrollBarW = 16f;
            float tableWidth = Mathf.Max(200f, inRect.width - scrollBarW);
            float nameW = Mathf.Max(80f, tableWidth - FixedColumnsWidth);

            float listTop = y + HeaderHeight + 4f;
            Rect headerRect = new Rect(0f, y, tableWidth, HeaderHeight);
            DrawSortableTableHeader(headerRect, nameW);
            Widgets.DrawLineHorizontal(0f, y + HeaderHeight, inRect.width);

            Rect listOut = new Rect(0f, listTop, inRect.width, Mathf.Max(40f, tableBottom - listTop));
            float viewH = visibleRows.Count * rowH + 4f;
            Rect listView = new Rect(0f, 0f, tableWidth, Mathf.Max(listOut.height, viewH));
            Widgets.BeginScrollView(listOut, ref scrollPosition, listView);
            for (int i = 0; i < visibleRows.Count; i++)
                DrawRow(0f, i * rowH, tableWidth, visibleRows[i]);
            Widgets.EndScrollView();

            transferCost = CurrentTransferCost();
            under = OutpostStrengthBudget.IsUnderWithdrawBudget(transferCost, available);
            excess = OutpostStrengthBudget.WithdrawExcess(transferCost, available);

            Rect btnRow = new Rect(0f, inRect.height - OutpostStrengthBudgetUi.BottomH, inRect.width, 36f);
            if (Widgets.ButtonText(btnRow.LeftHalf().ContractedBy(2f), "CancelButton".Translate()))
                Close();

            Rect confirmRect = btnRow.RightHalf().ContractedBy(2f);
            GUI.enabled = under;
            if (Widgets.ButtonText(confirmRect, "TSA_WD_StrengthBudget_ConfirmLeave".Translate()))
                TryConfirm(under);
            GUI.enabled = true;
            if (!under)
            {
                TooltipHandler.TipRegion(confirmRect, "TSA_WD_StrengthBudget_NeedStayBehind".Translate(excess.ToString("F0")));
            }
            else
            {
                TooltipHandler.TipRegion(confirmRect, "TSA_WD_StrengthBudget_ConfirmLeaveTip".Translate());
            }

            PawnRosterHeaderFilter.DrawDropdownIfOpen();
        }

        private void DrawSortableTableHeader(Rect hRect, float nameW)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            float curX = hRect.x + OutpostStrengthBudgetUi.ColIconDeploy;
            PawnRosterHeaderFilter.DrawFilterableHeader(
                ref curX, hRect.y, nameW, hRect.height,
                "TSA_WD_StrengthBudget_ColName".Translate(),
                sortColumn == WithdrawSortColumn.Name, sortAscending,
                TextAnchor.MiddleCenter,
                !nameSearchTerm.NullOrEmpty(),
                "TSA_WD_AllPlayerPawns_SearchName".Translate(),
                icon => PawnRosterHeaderFilter.OpenTextDropdown(
                    icon,
                    "TSA_WD_FilterByPawnName".Translate(),
                    "TSA_WD_AllPlayerPawns_SearchName".Translate(),
                    () => nameSearchTerm,
                    v => { nameSearchTerm = v ?? ""; RebuildVisibleRows(); },
                    () => { nameSearchTerm = ""; RebuildVisibleRows(); }),
                () => ToggleSort(WithdrawSortColumn.Name));
            DrawSortableHeader(ref curX, hRect, OutpostStrengthBudgetUi.ColGear, "TSA_WD_StrengthBudget_ColGear".Translate(), WithdrawSortColumn.Gear, TextAnchor.MiddleLeft, "TSA_WD_StrengthBudget_ColGearTip".Translate());
            DrawSortableHeader(ref curX, hRect, OutpostStrengthBudgetUi.ColShoot, "TSA_WD_StrengthBudget_ColShooting".Translate(), WithdrawSortColumn.Shooting, TextAnchor.MiddleCenter, "TSA_WD_StrengthBudget_ColShootingTip".Translate());
            DrawSortableHeader(ref curX, hRect, OutpostStrengthBudgetUi.ColMelee, "TSA_WD_StrengthBudget_ColMelee".Translate(), WithdrawSortColumn.Melee, TextAnchor.MiddleCenter, "TSA_WD_StrengthBudget_ColMeleeTip".Translate());
            DrawSortableHeader(ref curX, hRect, OutpostStrengthBudgetUi.ColHealth, "TSA_WD_StrengthBudget_ColHealth".Translate(), WithdrawSortColumn.Health, TextAnchor.MiddleCenter, "TSA_WD_StrengthBudget_ColHealthTip".Translate());
            DrawSortableHeader(ref curX, hRect, OutpostStrengthBudgetUi.ColStrengthDeploy, "TSA_WD_StrengthBudget_ColStrength".Translate(), WithdrawSortColumn.Strength, TextAnchor.MiddleCenter, "TSA_WD_StrengthBudget_ColStrengthTip".Translate());
            DrawFateHeader(new Rect(curX, hRect.y, ActionColW, hRect.height));
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private void DrawFateHeader(Rect actionHdr)
        {
            const float bulkW = 20f;
            const float bulkH = 22f;
            const float bulkGap = 2f;
            const float labelToButtonsGap = 4f;
            float clusterW = bulkW * 3f + bulkGap * 2f;
            string fateLabel = "TSA_WD_StrengthBudget_ColFate".Translate();
            float labelW = Mathf.Max(24f, Text.CalcSize(fateLabel).x + 4f);
            float totalW = labelW + labelToButtonsGap + clusterW;
            float startX = actionHdr.x + Mathf.Max(0f, (actionHdr.width - totalW) / 2f);
            Rect fateLabelRect = new Rect(startX, actionHdr.y, labelW, actionHdr.height);
            OutpostStrengthBudgetUi.LabelAnchored(fateLabelRect, fateLabel, TextAnchor.MiddleRight);
            TooltipHandler.TipRegion(fateLabelRect, "TSA_WD_StrengthBudget_ColFateTip".Translate());

            float bulkX = fateLabelRect.xMax + labelToButtonsGap;
            float bulkY = actionHdr.y + (actionHdr.height - bulkH) / 2f;
            Color prev = GUI.color;
            GUI.color = Color.white;
            DrawFateBulkButton(new Rect(bulkX, bulkY, bulkW, bulkH), "C", PawnFate.FormCaravan, "TSA_WD_StrengthBudget_FateAllFormTip".Translate());
            DrawFateBulkButton(new Rect(bulkX + bulkW + bulkGap, bulkY, bulkW, bulkH), "O", PawnFate.LeaveAtOutpost, "TSA_WD_StrengthBudget_FateAllLeaveTip".Translate());
            DrawFateBulkButton(new Rect(bulkX + (bulkW + bulkGap) * 2f, bulkY, bulkW, bulkH), "A", PawnFate.MarkLost, "TSA_WD_StrengthBudget_FateAllLostTip".Translate());
            GUI.color = prev;
        }

        private void DrawFateBulkButton(Rect rect, string letter, PawnFate fate, string tip)
        {
            Text.Font = GameFont.Tiny;
            if (Widgets.ButtonText(rect, letter))
                SetAllFate(fate);
            TooltipHandler.TipRegion(rect, tip);
        }

        private void DrawSortableHeader(ref float curX, Rect hRect, float width, string label, WithdrawSortColumn col, TextAnchor anchor, string tip)
        {
            Rect headerRect = new Rect(curX, hRect.y, width, hRect.height);
            if (Mouse.IsOver(headerRect)) Widgets.DrawHighlight(headerRect);
            string text = label + (sortColumn == col ? (sortAscending ? " ▲" : " ▼") : "");
            OutpostStrengthBudgetUi.LabelAnchored(headerRect, text.Truncate(width - 4f), anchor);
            if (!tip.NullOrEmpty())
                TooltipHandler.TipRegion(headerRect, tip);
            if (Widgets.ButtonInvisible(headerRect))
                ToggleSort(col);
            curX += width;
        }

        private void ToggleSort(WithdrawSortColumn col)
        {
            if (sortColumn == col) sortAscending = !sortAscending;
            else
            {
                sortColumn = col;
                sortAscending = col == WithdrawSortColumn.Name || col == WithdrawSortColumn.Gear;
            }
            SortVisibleRows();
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        private void DrawRow(float x, float y, float width, PlayerPawnRosterEntry entry)
        {
            if (entry?.pawn == null) return;
            Pawn pawn = entry.pawn;
            PawnFate fate = GetFate(entry.thingId);
            float cost = OutpostStrengthBudget.GetPawnCost(pawn);
            float rowH = OutpostStrengthBudgetUi.DeployRowHeight;

            Rect rowRect = new Rect(x, y, width, rowH);
            if (fate == PawnFate.MarkLost)
                Outpost_Dialog_UI.DrawUnmetRequirementsRowTint(rowRect, true);
            else if (fate == PawnFate.FormCaravan)
                Outpost_Dialog_UI.DrawSelectedRowTint(rowRect, true);

            float curX = x;
            float contentY = y + rowH / 2f;
            float portrait = OutpostStrengthBudgetUi.DeployPortraitSize;
            Rect portraitRect = new Rect(
                curX + (OutpostStrengthBudgetUi.ColIconDeploy - portrait) / 2f,
                contentY - portrait / 2f,
                portrait,
                portrait);
            OutpostStrengthBudgetUi.DrawPawnPortrait(portraitRect, pawn);
            TooltipHandler.TipRegion(portraitRect, "TSA_WD_StrengthBudget_PortraitTip".Translate(pawn.LabelShortCap));
            curX += OutpostStrengthBudgetUi.ColIconDeploy;

            float nameW = Mathf.Max(80f, width - FixedColumnsWidth);
            Color prev = GUI.color;
            GUI.color = FateTextColor(fate);
            Rect nameRect = new Rect(curX, y, nameW, rowH);
            OutpostStrengthBudgetUi.LabelAnchored(nameRect, pawn.LabelShortCap.Truncate(nameW - 4f), TextAnchor.MiddleLeft);
            TooltipHandler.TipRegion(nameRect, FateNameTip(fate, pawn.LabelShortCap));
            GUI.color = prev;
            curX += nameW;

            Rect gearRect = new Rect(curX, y + 4f, OutpostStrengthBudgetUi.ColGear - 4f, rowH - 8f);
            bool gearClicked = OutpostStrengthBudgetUi.DrawEquippedItemIcons(gearRect, pawn);
            curX += OutpostStrengthBudgetUi.ColGear;

            GUI.color = FateTextColor(fate);

            Rect shootRect = new Rect(curX, y, OutpostStrengthBudgetUi.ColShoot, rowH);
            OutpostStrengthBudgetUi.LabelAnchored(shootRect, OutpostStrengthBudgetUi.GetSkillLevel(pawn, SkillDefOf.Shooting).ToString(), TextAnchor.MiddleCenter);
            TooltipHandler.TipRegion(shootRect, SkillDefOf.Shooting.LabelCap);
            curX += OutpostStrengthBudgetUi.ColShoot;

            Rect meleeRect = new Rect(curX, y, OutpostStrengthBudgetUi.ColMelee, rowH);
            OutpostStrengthBudgetUi.LabelAnchored(meleeRect, OutpostStrengthBudgetUi.GetSkillLevel(pawn, SkillDefOf.Melee).ToString(), TextAnchor.MiddleCenter);
            TooltipHandler.TipRegion(meleeRect, SkillDefOf.Melee.LabelCap);
            curX += OutpostStrengthBudgetUi.ColMelee;

            Rect healthRect = new Rect(curX, y, OutpostStrengthBudgetUi.ColHealth, rowH);
            float healthPct = OutpostStrengthBudgetUi.GetHealthPercent(pawn);
            OutpostStrengthBudgetUi.LabelAnchored(healthRect, healthPct.ToString("F0") + "%", TextAnchor.MiddleCenter);
            TooltipHandler.TipRegion(healthRect, "TSA_WD_StrengthBudget_HealthTip".Translate(healthPct.ToString("F0")));
            curX += OutpostStrengthBudgetUi.ColHealth;

            Rect strengthRect = new Rect(curX, y, OutpostStrengthBudgetUi.ColStrengthDeploy, rowH);
            OutpostStrengthBudgetUi.LabelAnchored(strengthRect, cost.ToString("F0"), TextAnchor.MiddleCenter);
            TooltipHandler.TipRegion(strengthRect, FateStrengthTip(fate, cost));
            GUI.color = prev;
            curX += OutpostStrengthBudgetUi.ColStrengthDeploy;

            Rect actionCol = new Rect(curX, y, ActionColW, rowH);
            float clusterW = FateBtnW * 3f + FateBtnGap * 2f;
            float btnX = actionCol.x + (ActionColW - clusterW) / 2f;
            float btnY = y + (rowH - FateBtnH) / 2f;
            bool btnClicked = false;
            if (DrawFateToggle(new Rect(btnX, btnY, FateBtnW, FateBtnH),
                    "TSA_WD_StrengthBudget_FateCaravan".Translate(), PawnFate.FormCaravan, fate,
                    "TSA_WD_StrengthBudget_FormCaravanTip".Translate()))
            {
                SetFate(entry.thingId, PawnFate.FormCaravan);
                btnClicked = true;
            }
            if (DrawFateToggle(new Rect(btnX + FateBtnW + FateBtnGap, btnY, FateBtnW, FateBtnH),
                    "TSA_WD_StrengthBudget_FateOutpost".Translate(), PawnFate.LeaveAtOutpost, fate,
                    "TSA_WD_StrengthBudget_LeaveAtOutpostTip".Translate()))
            {
                SetFate(entry.thingId, PawnFate.LeaveAtOutpost);
                btnClicked = true;
            }
            if (DrawFateToggle(new Rect(btnX + (FateBtnW + FateBtnGap) * 2f, btnY, FateBtnW, FateBtnH),
                    "TSA_WD_StrengthBudget_FateAbandon".Translate(), PawnFate.MarkLost, fate,
                    "TSA_WD_StrengthBudget_MarkLostTip".Translate()))
            {
                SetFate(entry.thingId, PawnFate.MarkLost);
                btnClicked = true;
            }

            OutpostStrengthBudgetUi.FinishDeploySelectedRow(rowRect, fate == PawnFate.FormCaravan);

            if (!gearClicked && !btnClicked && Widgets.ButtonInvisible(rowRect))
                Find.WindowStack.Add(new Dialog_InfoCard(pawn));
        }

        private static Color FateTextColor(PawnFate fate)
        {
            switch (fate)
            {
                case PawnFate.MarkLost: return OutpostStrengthBudgetUi.LostTint;
                case PawnFate.LeaveAtOutpost: return LeaveTint;
                default: return OutpostStrengthBudgetUi.SelectedTint;
            }
        }

        private bool DrawFateToggle(Rect r, string label, PawnFate forFate, PawnFate current, string tip)
        {
            bool selected = forFate == current;
            bool mouseOver = Mouse.IsOver(r);
            bool pressed = mouseOver && Input.GetMouseButton(0);
            Color bg = selected ? NavBtnBgSelected : pressed ? NavBtnBgPress : mouseOver ? NavBtnBgHover : NavSlateFill;
            Widgets.DrawBoxSolid(r, bg);
            GUI.color = selected ? NavBtnOutlineSelected : mouseOver ? NavBtnOutlineHover : NavBtnOutline;
            Widgets.DrawBox(r, 1);
            GUI.color = Color.white;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(r, label.Colorize(FateTextColor(forFate)));
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            TooltipHandler.TipRegion(r, tip);
            return Widgets.ButtonInvisible(r);
        }

        private static string FateNameTip(PawnFate fate, string name)
        {
            switch (fate)
            {
                case PawnFate.MarkLost: return "TSA_WD_StrengthBudget_NameLostTip".Translate(name);
                case PawnFate.LeaveAtOutpost: return "TSA_WD_StrengthBudget_NameStayTip".Translate(name);
                default: return "TSA_WD_StrengthBudget_NameCaravanTip".Translate(name);
            }
        }

        private static string FateStrengthTip(PawnFate fate, float cost)
        {
            string c = cost.ToString("F0");
            switch (fate)
            {
                case PawnFate.MarkLost: return "TSA_WD_StrengthBudget_StrengthLostTip".Translate(c);
                case PawnFate.LeaveAtOutpost: return "TSA_WD_StrengthBudget_StrengthStayTip".Translate(c);
                default: return "TSA_WD_StrengthBudget_StrengthCaravanTip".Translate(c);
            }
        }

        private void SetFate(string thingId, PawnFate fate)
        {
            if (thingId.NullOrEmpty()) return;
            if (GetFate(thingId) == fate) return;
            fateByThingId[thingId] = fate;
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        private void SetAllFate(PawnFate fate)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                PlayerPawnRosterEntry e = entries[i];
                if (e?.thingId == null) continue;
                fateByThingId[e.thingId] = fate;
            }
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        private void TryConfirm(bool under)
        {
            if (resolved || !under) return;

            var take = new List<PlayerPawnRosterEntry>();
            var stay = new List<Pawn>();
            var lost = new List<Pawn>();
            for (int i = 0; i < entries.Count; i++)
            {
                PlayerPawnRosterEntry e = entries[i];
                if (e?.pawn == null) continue;
                switch (GetFate(e.thingId))
                {
                    case PawnFate.MarkLost:
                        lost.Add(e.pawn);
                        break;
                    case PawnFate.LeaveAtOutpost:
                        stay.Add(e.pawn);
                        break;
                    default:
                        take.Add(e);
                        break;
                }
            }

            bool willEmptyOutpost = WillEmptyOutpost();

            if (lost.Count > 0 && willEmptyOutpost)
            {
                string names = BuildLostNames(lost);
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "TSA_WD_StrengthBudget_LostAndAbandonConfirm".Translate(names, outpost.Label),
                    () => Finish(take, stay, lost, true),
                    destructive: true));
                return;
            }

            if (lost.Count > 0)
            {
                string names = BuildLostNames(lost);
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "TSA_WD_StrengthBudget_LostConfirm".Translate(names),
                    () => Finish(take, stay, lost, false),
                    destructive: true));
                return;
            }

            if (willEmptyOutpost)
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "TSA_WD_RemoveLastPawnWarning".Translate(outpost.Label),
                    () => Finish(take, stay, lost, true),
                    destructive: true));
                return;
            }

            Finish(take, stay, lost, false);
        }

        /// <summary>
        /// True when every current occupant of the outpost is covered by this dialog's entries and none of them
        /// chose Leave at Outpost — i.e. resolving fates as chosen will leave the outpost with zero occupants.
        /// Mirrors <c>PlayerPawnTransferUtility.GroupEmptiesOutpost</c>, but fate-aware and evaluated up front
        /// (before any pawn is destroyed) so the player is warned before the irreversible action, not after.
        /// </summary>
        private bool WillEmptyOutpost()
        {
            if (outpost == null || outpost.Occupants == null) return false;
            int occupantEntries = 0;
            int occupantsStaying = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                PlayerPawnRosterEntry e = entries[i];
                if (e?.pawn == null || e.outpostRole != PlayerPawnOutpostRole.Occupant) continue;
                if (!outpost.Occupants.Contains(e.pawn)) continue;
                occupantEntries++;
                if (GetFate(e.thingId) == PawnFate.LeaveAtOutpost) occupantsStaying++;
            }
            return occupantEntries > 0
                && occupantEntries == outpost.Occupants.Count
                && occupantsStaying == 0;
        }

        private static string BuildLostNames(List<Pawn> lost)
        {
            if (lost == null || lost.Count == 0) return "";
            if (lost.Count == 1) return lost[0].LabelShortCap;
            if (lost.Count == 2) return lost[0].LabelShortCap + ", " + lost[1].LabelShortCap;
            return "TSA_WD_StrengthBudget_LostCount".Translate(lost.Count).ToString();
        }

        private void Finish(List<PlayerPawnRosterEntry> take, List<Pawn> stay, List<Pawn> lost, bool willEmptyOutpost)
        {
            if (resolved) return;
            resolved = true;
            onResolved?.Invoke(take, stay, lost, willEmptyOutpost);
            Close(doCloseSound: false);
        }
    }
}
