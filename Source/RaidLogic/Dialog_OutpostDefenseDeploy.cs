using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace TSA_WorldDomination
{
    /// <summary>Pick which outpost occupants deploy for manual defense under an offensive-strength budget. Shows gear and combat stats.</summary>
    public class Dialog_OutpostDefenseDeploy : Window
    {
        private enum DeploySortColumn
        {
            Name,
            Gear,
            Shooting,
            Melee,
            Health,
            Strength
        }

        private readonly WorldObject_WD_Outpost outpost;
        private readonly WorldObject_Traveler traveler;
        private readonly WorldComponent_SpreadManager manager;
        private readonly bool isSkirmishFollowUp;
        private readonly bool enforceBudget;
        private readonly float available;
        private readonly List<Pawn> candidates = new List<Pawn>();
        private readonly List<Pawn> visibleRows = new List<Pawn>();
        private readonly HashSet<int> selectedIds = new HashSet<int>();
        private Vector2 scrollPosition;
        private bool resolved;
        private string nameSearchTerm = "";
        private DeploySortColumn sortColumn = DeploySortColumn.Strength;
        private bool sortAscending;
        private const float HeaderHeight = 36f;

        public override Vector2 InitialSize => new Vector2(890f, 660f);

        public Dialog_OutpostDefenseDeploy(
            WorldObject_Traveler traveler,
            WorldObject_WD_Outpost outpost,
            WorldComponent_SpreadManager manager,
            bool isSkirmishFollowUp)
        {
            this.traveler = traveler;
            this.outpost = outpost;
            this.manager = manager;
            this.isSkirmishFollowUp = isSkirmishFollowUp;
            enforceBudget = OutpostStrengthBudget.DefenseDeployBudgetEnabled;
            available = OutpostStrengthBudget.GetAvailableForDefense(outpost);
            CollectCandidates();
            RebuildVisibleRows();
            SelectDefaultFill();
            doCloseX = true;
            doCloseButton = false;
            absorbInputAroundWindow = true;
            forcePause = true;
            closeOnAccept = false;
            closeOnCancel = false;
        }

        private void CollectCandidates()
        {
            candidates.Clear();
            List<Pawn> occ = outpost?.Occupants;
            if (occ == null) return;
            for (int i = 0; i < occ.Count; i++)
            {
                Pawn p = occ[i];
                if (p == null || p.Destroyed || p.Dead) continue;
                candidates.Add(p);
            }
        }

        private void RebuildVisibleRows()
        {
            visibleRows.Clear();
            string search = string.IsNullOrEmpty(nameSearchTerm) ? null : nameSearchTerm.Trim().ToLowerInvariant();
            for (int i = 0; i < candidates.Count; i++)
            {
                Pawn p = candidates[i];
                if (p == null) continue;
                if (search != null)
                {
                    string label = p.LabelShortCap ?? "";
                    if (!label.ToLowerInvariant().Contains(search))
                        continue;
                }
                visibleRows.Add(p);
            }
            SortVisibleRows();
        }

        private void SortVisibleRows()
        {
            visibleRows.Sort((a, b) =>
            {
                int cmp = CompareDeploy(a, b, sortColumn);
                if (cmp == 0)
                    cmp = string.Compare(a?.LabelShortCap, b?.LabelShortCap, StringComparison.OrdinalIgnoreCase);
                return sortAscending ? cmp : -cmp;
            });
        }

        private static int CompareDeploy(Pawn a, Pawn b, DeploySortColumn col)
        {
            switch (col)
            {
                case DeploySortColumn.Name:
                    return string.Compare(a?.LabelShortCap, b?.LabelShortCap, StringComparison.OrdinalIgnoreCase);
                case DeploySortColumn.Gear:
                    return CountGear(a).CompareTo(CountGear(b));
                case DeploySortColumn.Shooting:
                    return OutpostStrengthBudgetUi.GetSkillLevel(a, SkillDefOf.Shooting)
                        .CompareTo(OutpostStrengthBudgetUi.GetSkillLevel(b, SkillDefOf.Shooting));
                case DeploySortColumn.Melee:
                    return OutpostStrengthBudgetUi.GetSkillLevel(a, SkillDefOf.Melee)
                        .CompareTo(OutpostStrengthBudgetUi.GetSkillLevel(b, SkillDefOf.Melee));
                case DeploySortColumn.Health:
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

        private void SelectDefaultFill()
        {
            selectedIds.Clear();
            if (!enforceBudget)
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    Pawn p = candidates[i];
                    if (p == null) continue;
                    selectedIds.Add(p.thingIDNumber);
                }
                return;
            }

            float used = 0f;
            // Fill by strength descending regardless of current UI sort.
            var byStrength = new List<Pawn>(candidates);
            byStrength.Sort((a, b) => OutpostStrengthBudget.GetPawnCost(b).CompareTo(OutpostStrengthBudget.GetPawnCost(a)));
            for (int i = 0; i < byStrength.Count; i++)
            {
                Pawn p = byStrength[i];
                float cost = OutpostStrengthBudget.GetPawnCost(p);
                if (used + cost > available + 0.05f) continue;
                selectedIds.Add(p.thingIDNumber);
                used += cost;
            }
        }

        private float CurrentUsed()
        {
            float used = 0f;
            for (int i = 0; i < candidates.Count; i++)
            {
                Pawn p = candidates[i];
                if (p == null || !selectedIds.Contains(p.thingIDNumber)) continue;
                used += OutpostStrengthBudget.GetPawnCost(p);
            }
            return used;
        }

        public override void PostClose()
        {
            PawnRosterHeaderFilter.CloseDropdown();
            base.PostClose();
            if (!resolved && traveler != null && outpost != null && !outpost.Destroyed)
                Find.WindowStack.Add(new Dialog_OutpostDefenseChoice(traveler, outpost, manager, isSkirmishFollowUp));
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (PawnRosterHeaderFilter.TryCloseDropdownOnCancel())
                return;

            float y = 0f;
            Text.Font = GameFont.Medium;
            OutpostStrengthBudgetUi.LabelAnchored(
                new Rect(0f, y, inRect.width, Outpost_Dialog_UI.DialogTitleHeight),
                "TSA_WD_StrengthBudget_DeployTitle".Translate(),
                TextAnchor.MiddleCenter);
            y += Outpost_Dialog_UI.DialogTitleRowAdvance;
            Text.Font = GameFont.Small;

            string banner = "TSA_WD_StrengthBudget_DeployBanner".Translate();
            float bannerH = Mathf.Max(OutpostStrengthBudgetUi.BoxLineH, Text.CalcHeight(banner, inRect.width));
            OutpostStrengthBudgetUi.LabelAnchored(new Rect(0f, y, inRect.width, bannerH), banner, TextAnchor.MiddleCenter);
            y += bannerH + 8f;

            float used = CurrentUsed();
            bool under = !enforceBudget || OutpostStrengthBudget.IsUnderBudget(used, available);
            Color meterColor = under ? Color.white : ColorLibrary.RedReadable;
            string meter = "TSA_WD_StrengthBudget_DeployingMeter".Translate(used.ToString("F0"), available.ToString("F0"));
            y = OutpostStrengthBudgetUi.DrawMeterBox(0f, y, inRect.width, meter, meterColor,
                tipKey: "TSA_WD_StrengthBudget_DeployMeterTip");
            y += Outpost_Dialog_UI.OutcomeBoxGap;

            float rowH = OutpostStrengthBudgetUi.DeployRowHeight;
            float tableBottom = inRect.height - OutpostStrengthBudgetUi.BottomH - 8f;
            // Match scroll-view content width so header and rows share the same column X.
            const float scrollBarW = 16f;
            float tableWidth = Mathf.Max(200f, inRect.width - scrollBarW);
            float nameW = Mathf.Max(80f, tableWidth - OutpostStrengthBudgetUi.DeployFixedColumnsWidth);

            float listTop = y + HeaderHeight + 4f;
            Rect headerRect = new Rect(0f, y, tableWidth, HeaderHeight);
            DrawSortableTableHeader(headerRect, nameW);
            Widgets.DrawLineHorizontal(0f, y + HeaderHeight, inRect.width);

            Rect listOut = new Rect(0f, listTop, inRect.width, Mathf.Max(40f, tableBottom - listTop));
            float viewH = visibleRows.Count * rowH + 4f;
            Rect listView = new Rect(0f, 0f, tableWidth, Mathf.Max(listOut.height, viewH));
            Widgets.BeginScrollView(listOut, ref scrollPosition, listView);
            used = CurrentUsed();
            for (int i = 0; i < visibleRows.Count; i++)
                DrawRow(0f, i * rowH, tableWidth, visibleRows[i], used);
            Widgets.EndScrollView();

            used = CurrentUsed();
            under = (!enforceBudget || OutpostStrengthBudget.IsUnderBudget(used, available)) && selectedIds.Count > 0;

            Rect btnRow = new Rect(0f, inRect.height - OutpostStrengthBudgetUi.BottomH, inRect.width, 36f);
            if (Widgets.ButtonText(btnRow.LeftHalf().ContractedBy(2f), "TSA_WD_Conquest_Back".Translate()))
                Close();

            Rect fightRect = btnRow.RightHalf().ContractedBy(2f);
            GUI.enabled = under;
            if (Widgets.ButtonText(fightRect, "TSA_WD_OutpostDefense_FightManually".Translate()))
                ConfirmFight();
            GUI.enabled = true;

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
                sortColumn == DeploySortColumn.Name, sortAscending,
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
                () => ToggleSort(DeploySortColumn.Name));
            DrawSortableHeader(ref curX, hRect, OutpostStrengthBudgetUi.ColGear, "TSA_WD_StrengthBudget_ColGear".Translate(), DeploySortColumn.Gear, TextAnchor.MiddleLeft);
            DrawSortableHeader(ref curX, hRect, OutpostStrengthBudgetUi.ColShoot, "TSA_WD_StrengthBudget_ColShooting".Translate(), DeploySortColumn.Shooting, TextAnchor.MiddleCenter);
            DrawSortableHeader(ref curX, hRect, OutpostStrengthBudgetUi.ColMelee, "TSA_WD_StrengthBudget_ColMelee".Translate(), DeploySortColumn.Melee, TextAnchor.MiddleCenter);
            DrawSortableHeader(ref curX, hRect, OutpostStrengthBudgetUi.ColHealth, "TSA_WD_StrengthBudget_ColHealth".Translate(), DeploySortColumn.Health, TextAnchor.MiddleCenter);
            DrawSortableHeader(ref curX, hRect, OutpostStrengthBudgetUi.ColStrengthDeploy, "TSA_WD_StrengthBudget_ColStrength".Translate(), DeploySortColumn.Strength, TextAnchor.MiddleCenter);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private void DrawSortableHeader(ref float curX, Rect hRect, float width, string label, DeploySortColumn col, TextAnchor anchor)
        {
            Rect headerRect = new Rect(curX, hRect.y, width, hRect.height);
            if (Mouse.IsOver(headerRect)) Widgets.DrawHighlight(headerRect);
            string text = label + (sortColumn == col ? (sortAscending ? " ▲" : " ▼") : "");
            OutpostStrengthBudgetUi.LabelAnchored(headerRect, text.Truncate(width - 4f), anchor);
            if (Widgets.ButtonInvisible(headerRect))
                ToggleSort(col);
            curX += width;
        }

        private void ToggleSort(DeploySortColumn col)
        {
            if (sortColumn == col) sortAscending = !sortAscending;
            else
            {
                sortColumn = col;
                sortAscending = col == DeploySortColumn.Name || col == DeploySortColumn.Gear;
            }
            SortVisibleRows();
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        private void DrawRow(float x, float y, float width, Pawn pawn, float currentUsed)
        {
            if (pawn == null) return;
            bool selected = selectedIds.Contains(pawn.thingIDNumber);
            float cost = OutpostStrengthBudget.GetPawnCost(pawn);
            bool canSelect = selected || !enforceBudget || OutpostStrengthBudget.IsUnderBudget(currentUsed + cost, available);
            float rowH = OutpostStrengthBudgetUi.DeployRowHeight;

            Rect rowRect = new Rect(x, y, width, rowH);
            Outpost_Dialog_UI.DrawSelectedRowTint(rowRect, selected);

            float curX = x;
            float contentY = y + rowH / 2f;
            float portrait = OutpostStrengthBudgetUi.DeployPortraitSize;
            Rect portraitRect = new Rect(
                curX + (OutpostStrengthBudgetUi.ColIconDeploy - portrait) / 2f,
                contentY - portrait / 2f,
                portrait,
                portrait);
            OutpostStrengthBudgetUi.DrawPawnPortrait(portraitRect, pawn);
            curX += OutpostStrengthBudgetUi.ColIconDeploy;

            float nameW = Mathf.Max(80f, width - OutpostStrengthBudgetUi.DeployFixedColumnsWidth);
            Color prev = GUI.color;
            if (selected) GUI.color = OutpostStrengthBudgetUi.SelectedTint;
            OutpostStrengthBudgetUi.LabelAnchored(
                new Rect(curX, y, nameW, rowH),
                pawn.LabelShortCap.Truncate(nameW - 4f),
                TextAnchor.MiddleLeft);
            GUI.color = prev;
            curX += nameW;

            Rect gearRect = new Rect(curX, y + 4f, OutpostStrengthBudgetUi.ColGear - 4f, rowH - 8f);
            bool gearClicked = OutpostStrengthBudgetUi.DrawEquippedItemIcons(gearRect, pawn);
            curX += OutpostStrengthBudgetUi.ColGear;

            if (selected) GUI.color = OutpostStrengthBudgetUi.SelectedTint;
            OutpostStrengthBudgetUi.LabelAnchored(
                new Rect(curX, y, OutpostStrengthBudgetUi.ColShoot, rowH),
                OutpostStrengthBudgetUi.GetSkillLevel(pawn, SkillDefOf.Shooting).ToString(),
                TextAnchor.MiddleCenter);
            curX += OutpostStrengthBudgetUi.ColShoot;

            OutpostStrengthBudgetUi.LabelAnchored(
                new Rect(curX, y, OutpostStrengthBudgetUi.ColMelee, rowH),
                OutpostStrengthBudgetUi.GetSkillLevel(pawn, SkillDefOf.Melee).ToString(),
                TextAnchor.MiddleCenter);
            curX += OutpostStrengthBudgetUi.ColMelee;

            OutpostStrengthBudgetUi.LabelAnchored(
                new Rect(curX, y, OutpostStrengthBudgetUi.ColHealth, rowH),
                OutpostStrengthBudgetUi.GetHealthPercent(pawn).ToString("F0") + "%",
                TextAnchor.MiddleCenter);
            curX += OutpostStrengthBudgetUi.ColHealth;

            OutpostStrengthBudgetUi.LabelAnchored(
                new Rect(curX, y, OutpostStrengthBudgetUi.ColStrengthDeploy, rowH),
                cost.ToString("F0"),
                TextAnchor.MiddleCenter);
            GUI.color = prev;
            curX += OutpostStrengthBudgetUi.ColStrengthDeploy;

            Rect actionCol = new Rect(curX, y, OutpostStrengthBudgetUi.ColAction, rowH);
            Rect btn = new Rect(actionCol.x + 4f, y + (rowH - 28f) / 2f, OutpostStrengthBudgetUi.ColAction - 8f, 28f);
            string label = selected
                ? "TSA_WD_StrengthBudget_Deploying".Translate()
                : "TSA_WD_StrengthBudget_Deploy".Translate();
            if (!canSelect && !selected) GUI.enabled = false;
            bool btnClicked = Widgets.ButtonText(btn, label);
            GUI.enabled = true;
            if (btnClicked)
                Toggle(pawn, selected, canSelect);

            OutpostStrengthBudgetUi.FinishDeploySelectedRow(rowRect, selected);

            if (!gearClicked && !btnClicked && Widgets.ButtonInvisible(rowRect))
                Find.WindowStack.Add(new Dialog_InfoCard(pawn));
        }

        private void Toggle(Pawn pawn, bool selected, bool canSelect)
        {
            if (pawn == null) return;
            if (selected)
                selectedIds.Remove(pawn.thingIDNumber);
            else if (canSelect)
                selectedIds.Add(pawn.thingIDNumber);
        }

        private void ConfirmFight()
        {
            if (resolved) return;
            var deploy = new List<Pawn>();
            for (int i = 0; i < candidates.Count; i++)
            {
                Pawn p = candidates[i];
                if (p == null || !selectedIds.Contains(p.thingIDNumber)) continue;
                deploy.Add(p);
            }
            if (deploy.Count == 0) return;

            resolved = true;
            int raidDelay = isSkirmishFollowUp ? 0 : 900;
            outpost?.ClearPendingSkirmishDefense();
            Close(doCloseSound: false);
            if (!WD_OutpostDefenseEncounterUtility.StartManualDefenseEncounter(traveler, outpost, raidDelay, deploy))
                Raid_Simulated.ResolvePlayerOutpostRaidArrival(traveler, manager, allowSkirmishRetry: false);
        }
    }
}
