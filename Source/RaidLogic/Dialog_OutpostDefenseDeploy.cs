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
    public class Dialog_OutpostDefenseDeploy : Window, IPawnRosterViewHost
    {
        private const PawnRosterColumnWindow ColWindow = PawnRosterColumnWindow.DefenseDeploy;
        private const float ViewControlsH = 30f;

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
        private readonly PawnRosterViewState view = PawnRosterViewStateStore.For(ColWindow);
        private readonly Dictionary<Pawn, PawnRosterRowData> rosterData = new Dictionary<Pawn, PawnRosterRowData>();
        private readonly Action<string> onSort;
        private int builtViewRevision = -1;
        private int builtTraitRevision = -1;
        private string nameSearchTerm
        {
            get => view.GetString(PawnRosterFilterKeys.Name);
            set => view.SetString(PawnRosterFilterKeys.Name, value);
        }
        private const float HeaderHeight = 36f;

        private static float FixedColumnsWidth =>
            OutpostStrengthBudgetUi.ColIconDeploy
            + PawnRosterBudgetTable.ColumnsWidth(ColWindow)
            + OutpostStrengthBudgetUi.ColAction;

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
            onSort = ToggleSort;
            view.SetDefaultSort(PawnRosterBudgetTable.DefaultSortId, false);
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
            builtViewRevision = view.Revision;
            builtTraitRevision = PawnRosterTraitFilter.Revision;
            PawnRosterBudgetTable.BuildRows(candidates, visibleRows, p => p, view, rosterData);
        }

        private void RebuildIfViewChanged()
        {
            if (builtViewRevision != view.Revision || builtTraitRevision != PawnRosterTraitFilter.Revision)
                RebuildVisibleRows();
        }

        public PawnRosterColumnWindow RosterWindow => ColWindow;
        public PawnRosterViewState RosterView => view;
        public bool RosterHostOpen => IsOpen;
        public void CollectRosterSortOptions(List<PawnRosterSortOption> into) =>
            PawnRosterBudgetTable.CollectSortOptions(ColWindow, into);
        public bool SupportsRosterFilter(string key) =>
            PawnRosterViewUtil.IsCommonFilterKey(key) || key == PawnRosterFilterKeys.Traits;
        public string DescribeRosterFilter(string key) => PawnRosterViewUtil.DescribeFilter(view, key);
        public void OnRosterViewChanged() => builtViewRevision = -1;

        public void CollectRosterPawns(List<Pawn> into)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i] != null) into.Add(candidates[i]);
            }
        }

        private void OnColumnsChanged()
        {
            view.Sort.RemoveWhere(id => PawnRosterColumnCatalog.HasOption(ColWindow, id) && !PlayerPawnRosterUtility.ColVisible(ColWindow, id));
            builtViewRevision = -1;
        }

        private void RestoreDefaultView()
        {
            view.Reset();
            scrollPosition = Vector2.zero;
            PlayerPawnRosterUtility.ResetSkillDisplayOptions(ColWindow);
            WorldComponent_PawnRosterColumnPrefs.Get()?.ResetToDefaults(ColWindow);
            PawnRosterTraitFilter.Clear();
            PawnRosterHeaderFilter.CloseDropdown();
            builtViewRevision = -1;
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

            PlayerPawnRosterUtility.DrawRosterViewControls(
                y, ViewControlsH, inRect.width, ColWindow, RestoreDefaultView,
                () => Find.WindowStack.Add(new Dialog_PawnRosterColumns(ColWindow, OnColumnsChanged)),
                this);
            y += ViewControlsH + 4f;
            RebuildIfViewChanged();

            float rowH = OutpostStrengthBudgetUi.DeployRowHeight;
            float tableBottom = inRect.height - OutpostStrengthBudgetUi.BottomH - 8f;
            // Match scroll-view content width so header and rows share the same column X.
            const float scrollBarW = 16f;
            float fixedW = FixedColumnsWidth;
            float tableWidth = Mathf.Max(Mathf.Max(200f, inRect.width - scrollBarW), fixedW + 80f);
            float nameW = Mathf.Max(80f, tableWidth - fixedW);

            float listTop = y + HeaderHeight + 4f;
            GUI.BeginGroup(new Rect(0f, y, inRect.width, HeaderHeight));
            DrawSortableTableHeader(new Rect(-scrollPosition.x, 0f, tableWidth, HeaderHeight), nameW);
            GUI.EndGroup();
            Widgets.DrawLineHorizontal(0f, y + HeaderHeight, inRect.width);

            Rect listOut = new Rect(0f, listTop, inRect.width, Mathf.Max(40f, tableBottom - listTop));
            float viewH = visibleRows.Count * rowH + 4f;
            Rect listView = new Rect(0f, 0f, tableWidth, Mathf.Max(listOut.height, viewH));
            Widgets.BeginScrollView(listOut, ref scrollPosition, listView);
            used = CurrentUsed();
            for (int i = 0; i < visibleRows.Count; i++)
                DrawRow(0f, i * rowH, tableWidth, nameW, visibleRows[i], used);
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
                view.Sort.HeaderSorted(PawnRosterColumnIds.Name), view.Sort.IsAscending(PawnRosterColumnIds.Name),
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
                () => ToggleSort(PawnRosterColumnIds.Name));
            PawnRosterBudgetTable.DrawHeaders(ref curX, hRect, ColWindow, view.Sort, onSort, withTips: false);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private void ToggleSort(string id)
        {
            view.Sort.ClickFromHeader(id, PawnRosterBudgetTable.FirstClickAscending(ColWindow, id));
            RebuildVisibleRows();
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        private void DrawRow(float x, float y, float width, float nameW, Pawn pawn, float currentUsed)
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

            Color prev = GUI.color;
            if (selected) GUI.color = OutpostStrengthBudgetUi.SelectedTint;
            OutpostStrengthBudgetUi.LabelAnchored(
                new Rect(curX, y, nameW, rowH),
                pawn.LabelShortCap.Truncate(nameW - 4f),
                TextAnchor.MiddleLeft);
            GUI.color = prev;
            curX += nameW;

            bool gearClicked = PawnRosterBudgetTable.DrawCells(
                ref curX, y, rowH, ColWindow, pawn, cost,
                selected ? OutpostStrengthBudgetUi.SelectedTint : prev,
                withTips: false, strengthTip: null,
                PawnRosterRowData.ForPawn(rosterData, pawn));

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
