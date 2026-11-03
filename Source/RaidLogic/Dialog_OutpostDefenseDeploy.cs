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
    /// Pick which outpost occupants deploy for manual defense under per-section offensive-strength budgets.
    /// Target outpost plus nearby player supporting outposts as tabs (one roster visible at a time).
    /// </summary>
    public class Dialog_OutpostDefenseDeploy : Window, IPawnRosterViewHost
    {
        private const PawnRosterColumnWindow ColWindow = PawnRosterColumnWindow.DefenseDeploy;
        private const float ViewControlsH = 30f;
        private const float SectionMeterH = 44f;
        private const float HeaderHeight = 36f;
        private const float ColSelect = 36f;
        private const float ArmoryBtnWidth = 120f;
        private const float SmartSelectBtnWidth = 160f;
        private const float DeselectBtnWidth = 130f;
        private const float ToolbarBtnGap = 8f;
        private const float TabBarH = 28f;
        private const float TabPadX = 12f;
        private const float TabGap = 4f;
        private const float MaxTabW = 220f;
        private const float MinTabW = 80f;

        private sealed class DeploySection
        {
            public WorldObject_WD_Outpost source;
            public bool isTarget;
            public float available;
            public float maxOffensive;
            public readonly List<Pawn> candidates = new List<Pawn>();
            public readonly List<Pawn> visibleRows = new List<Pawn>();
            public string cachedHeaderLabel;
            public string cachedTabLabel;
        }

        private readonly WorldObject_WD_Outpost outpost;
        private readonly WorldObject_Traveler traveler;
        private readonly WorldComponent_SpreadManager manager;
        private readonly bool isSkirmishFollowUp;
        private readonly bool enforceBudget;
        private readonly List<DeploySection> sections = new List<DeploySection>();
        private readonly HashSet<string> selectedThingIds = new HashSet<string>();
        private Vector2 scrollPosition;
        private Vector2 tabScrollPosition;
        private int selectedSectionIndex;
        private bool resolved;
        private bool armoryDirty;
        private readonly PawnRosterViewState view = PawnRosterViewStateStore.For(ColWindow);
        private readonly Dictionary<Pawn, PawnRosterRowData> rosterData = new Dictionary<Pawn, PawnRosterRowData>();
        private readonly Action<string> onSort;
        private readonly List<Pawn> leaveBehindScratch = new List<Pawn>();
        private readonly List<Pawn> smartSelectScratch = new List<Pawn>();
        private readonly List<float> tabWidths = new List<float>();
        private int builtViewRevision = -1;
        private int builtTraitRevision = -1;
        private int builtHediffRevision = -1;
        private string cachedBanner;
        private string nameSearchTerm
        {
            get => view.GetString(PawnRosterFilterKeys.Name);
            set => view.SetString(PawnRosterFilterKeys.Name, value);
        }

        private static float FixedColumnsWidth =>
            ColSelect
            + OutpostStrengthBudgetUi.ColIconDeploy
            + PawnRosterBudgetTable.ColumnsWidth(ColWindow);

        private DeploySection ActiveSection =>
            sections.Count == 0
                ? null
                : sections[Mathf.Clamp(selectedSectionIndex, 0, sections.Count - 1)];

        public override Vector2 InitialSize => new Vector2(920f, 720f);

        private readonly bool isTrenchCorridor;

        public bool IsForTraveler(WorldObject_Traveler candidate)
            => traveler != null && candidate != null && traveler == candidate;

        public Dialog_OutpostDefenseDeploy(
            WorldObject_Traveler traveler,
            WorldObject_WD_Outpost outpost,
            WorldComponent_SpreadManager manager,
            bool isSkirmishFollowUp,
            bool isTrenchCorridor = false)
        {
            this.traveler = traveler;
            this.outpost = outpost;
            this.manager = manager;
            this.isSkirmishFollowUp = isSkirmishFollowUp;
            this.isTrenchCorridor = isTrenchCorridor;
            enforceBudget = OutpostStrengthBudget.DefenseDeployBudgetEnabled;
            onSort = ToggleSort;
            view.SetDefaultSort(PawnRosterBudgetTable.DefaultSortId, false);
            cachedBanner = "TSA_WD_StrengthBudget_DeployBanner".Translate()
                + "\n" + "TSA_WD_StrengthBudget_DeployStoredTip".Translate();
            BuildSectionsOnce();
            RebuildVisibleRows();
            SelectDefaultFill();
            doCloseX = true;
            doCloseButton = false;
            absorbInputAroundWindow = true;
            forcePause = true;
            closeOnAccept = false;
            closeOnCancel = false;
        }

        private void BuildSectionsOnce()
        {
            sections.Clear();
            selectedSectionIndex = 0;
            var target = new DeploySection
            {
                source = outpost,
                isTarget = true,
                available = OutpostStrengthBudget.GetAvailableForDefense(outpost),
                maxOffensive = Mathf.Max(0f, outpost?.GetComponent<CompViralSpread>()?.GetMaxOffensiveStrength() ?? 0f),
                cachedHeaderLabel = "TSA_WD_OutpostDefense_DeploySection_Target".Translate(outpost?.LabelCap ?? "Outpost"),
                cachedTabLabel = outpost?.LabelCap ?? "Outpost"
            };
            CollectDeployCandidates(outpost, target.candidates);
            sections.Add(target);

            List<WorldObject_WD_Outpost> supports = WD_OutpostDefenseEncounterUtility.CollectPlayerSupportOutposts(
                outpost, traveler, manager);
            for (int i = 0; i < supports.Count; i++)
            {
                WorldObject_WD_Outpost donor = supports[i];
                if (donor == null || donor.Destroyed) continue;
                var section = new DeploySection
                {
                    source = donor,
                    isTarget = false,
                    available = OutpostStrengthBudget.GetAvailableForSupportDefense(donor),
                    maxOffensive = Mathf.Max(0f, donor.GetComponent<CompViralSpread>()?.GetMaxOffensiveStrength() ?? 0f),
                    cachedHeaderLabel = "TSA_WD_OutpostDefense_DeploySection_Support".Translate(donor.LabelCap),
                    cachedTabLabel = donor.LabelCap
                };
                CollectDeployCandidates(donor, section.candidates);
                if (section.candidates.Count == 0) continue;
                sections.Add(section);
            }

            RebuildTabWidths();
        }

        private void RebuildTabWidths()
        {
            tabWidths.Clear();
            Text.Font = GameFont.Small;
            for (int i = 0; i < sections.Count; i++)
            {
                string label = sections[i].cachedTabLabel ?? sections[i].cachedHeaderLabel ?? "?";
                float w = Mathf.Clamp(Text.CalcSize(label).x + TabPadX * 2f, MinTabW, MaxTabW);
                tabWidths.Add(w);
            }
        }

        private static void CollectDeployCandidates(WorldObject_WD_Outpost source, List<Pawn> into)
        {
            into.Clear();
            if (source == null) return;
            var seen = new HashSet<int>();
            AppendLiving(source.Occupants, into, seen);
            AppendLiving(source.StoredAnimalsAndVehicles, into, seen);
            AppendLiving(source.StoredMechanoids, into, seen);
        }

        private static void AppendLiving(List<Pawn> from, List<Pawn> into, HashSet<int> seen)
        {
            if (from == null) return;
            for (int i = 0; i < from.Count; i++)
            {
                Pawn p = from[i];
                if (p == null || p.Destroyed || p.Dead) continue;
                if (!seen.Add(p.thingIDNumber)) continue;
                into.Add(p);
            }
        }

        /// <summary>Occupants only — stored animals/vehicles/mechs start deselected.</summary>
        private static bool IsOccupantCandidate(WorldObject_WD_Outpost source, Pawn pawn)
        {
            if (source == null || pawn == null) return false;
            List<Pawn> occ = source.Occupants;
            if (occ == null) return false;
            for (int i = 0; i < occ.Count; i++)
            {
                if (occ[i] == pawn) return true;
            }
            return false;
        }

        private void RebuildVisibleRows()
        {
            builtViewRevision = view.Revision;
            builtTraitRevision = PawnRosterTraitFilter.Revision;
            builtHediffRevision = PawnRosterHediffFilter.Revision;
            for (int i = 0; i < sections.Count; i++)
            {
                DeploySection section = sections[i];
                PawnRosterBudgetTable.BuildRows(section.candidates, section.visibleRows, p => p, view, rosterData);
            }
        }

        private void RebuildIfViewChanged()
        {
            if (builtViewRevision != view.Revision
                || builtTraitRevision != PawnRosterTraitFilter.Revision
                || builtHediffRevision != PawnRosterHediffFilter.Revision)
                RebuildVisibleRows();
        }

        private void RefreshAfterArmoryIfNeeded()
        {
            if (!armoryDirty) return;
            if (IsOutpostArmoryOpen()) return;
            rosterData.Clear();
            armoryDirty = false;
            builtViewRevision = -1;
        }

        private static bool IsOutpostArmoryOpen()
        {
            var windows = Find.WindowStack?.Windows;
            if (windows == null) return false;
            for (int i = 0; i < windows.Count; i++)
            {
                if (windows[i] is Dialog_OutpostArmory)
                    return true;
            }
            return false;
        }

        public PawnRosterColumnWindow RosterWindow => ColWindow;
        public PawnRosterViewState RosterView => view;
        public bool RosterHostOpen => IsOpen;
        public void CollectRosterSortOptions(List<PawnRosterSortOption> into) =>
            PawnRosterBudgetTable.CollectSortOptions(ColWindow, into);
        public bool SupportsRosterFilter(string key) =>
            PawnRosterViewUtil.IsCommonFilterKey(key)
            || key == PawnRosterFilterKeys.Traits
            || key == PawnRosterFilterKeys.Hediffs;
        public string DescribeRosterFilter(string key) => PawnRosterViewUtil.DescribeFilter(view, key);
        public void OnRosterViewChanged() => builtViewRevision = -1;

        public void CollectRosterPawns(List<Pawn> into)
        {
            for (int s = 0; s < sections.Count; s++)
            {
                List<Pawn> candidates = sections[s].candidates;
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (candidates[i] != null) into.Add(candidates[i]);
                }
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
            selectedThingIds.Clear();
            DeploySection target = sections.Count > 0 ? sections[0] : null;
            if (target == null || !target.isTarget) return;

            if (!enforceBudget)
            {
                for (int i = 0; i < target.candidates.Count; i++)
                {
                    Pawn p = target.candidates[i];
                    if (p?.ThingID == null) continue;
                    if (!IsOccupantCandidate(target.source, p)) continue;
                    selectedThingIds.Add(p.ThingID);
                }
                return;
            }

            float used = 0f;
            var byStrength = new List<Pawn>();
            for (int i = 0; i < target.candidates.Count; i++)
            {
                Pawn p = target.candidates[i];
                if (p == null || !IsOccupantCandidate(target.source, p)) continue;
                byStrength.Add(p);
            }
            byStrength.Sort((a, b) => OutpostStrengthBudget.GetPawnCost(b).CompareTo(OutpostStrengthBudget.GetPawnCost(a)));
            for (int i = 0; i < byStrength.Count; i++)
            {
                Pawn p = byStrength[i];
                if (p?.ThingID == null) continue;
                float cost = OutpostStrengthBudget.GetPawnCost(p);
                if (used + cost > target.available + 0.05f) continue;
                selectedThingIds.Add(p.ThingID);
                used += cost;
            }
        }

        private void SmartSelectAllSections()
        {
            selectedThingIds.Clear();
            smartSelectScratch.Clear();
            for (int s = 0; s < sections.Count; s++)
            {
                DeploySection section = sections[s];
                List<Pawn> candidates = section.candidates;
                for (int i = 0; i < candidates.Count; i++)
                {
                    Pawn p = candidates[i];
                    if (p?.ThingID == null) continue;
                    if (!IsOccupantCandidate(section.source, p)) continue;
                    smartSelectScratch.Add(p);
                }
            }

            smartSelectScratch.Sort((a, b) => OutpostStrengthBudget.GetPawnCost(b).CompareTo(OutpostStrengthBudget.GetPawnCost(a)));

            var usedBySection = new Dictionary<DeploySection, float>();
            for (int s = 0; s < sections.Count; s++)
                usedBySection[sections[s]] = 0f;

            for (int i = 0; i < smartSelectScratch.Count; i++)
            {
                Pawn pawn = smartSelectScratch[i];
                DeploySection section = FindSectionForPawn(pawn);
                if (section == null || pawn.ThingID == null) continue;

                float cost = OutpostStrengthBudget.GetPawnCost(pawn);
                float used = usedBySection[section];
                if (enforceBudget && !OutpostStrengthBudget.IsUnderBudget(used + cost, section.available))
                    continue;

                selectedThingIds.Add(pawn.ThingID);
                if (!section.isTarget)
                {
                    FillSelectedScratch(section);
                    if (!OutpostPawnIdeologyUtil.BulkRemovalSelectionIsAllowed(section.source, leaveBehindScratch))
                    {
                        selectedThingIds.Remove(pawn.ThingID);
                        continue;
                    }
                }

                usedBySection[section] = used + cost;
            }

            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        private void DeselectAll()
        {
            selectedThingIds.Clear();
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        private DeploySection FindSectionForPawn(Pawn pawn)
        {
            if (pawn == null) return null;
            for (int s = 0; s < sections.Count; s++)
            {
                List<Pawn> candidates = sections[s].candidates;
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (candidates[i] == pawn) return sections[s];
                }
            }
            return null;
        }

        private float SectionUsed(DeploySection section)
        {
            if (section == null) return 0f;
            float used = 0f;
            for (int i = 0; i < section.candidates.Count; i++)
            {
                Pawn p = section.candidates[i];
                if (p?.ThingID == null || !selectedThingIds.Contains(p.ThingID)) continue;
                used += OutpostStrengthBudget.GetPawnCost(p);
            }
            return used;
        }

        private bool SectionUnderBudget(DeploySection section)
        {
            if (section == null) return true;
            if (!enforceBudget) return true;
            return OutpostStrengthBudget.IsUnderBudget(SectionUsed(section), section.available);
        }

        private bool AllSectionsUnderBudget()
        {
            for (int i = 0; i < sections.Count; i++)
            {
                if (!SectionUnderBudget(sections[i])) return false;
            }
            return true;
        }

        private int TargetSelectedCount()
        {
            if (sections.Count == 0 || !sections[0].isTarget) return 0;
            int n = 0;
            List<Pawn> candidates = sections[0].candidates;
            for (int i = 0; i < candidates.Count; i++)
            {
                Pawn p = candidates[i];
                if (p?.ThingID != null && selectedThingIds.Contains(p.ThingID)) n++;
            }
            return n;
        }

        private bool SupportLeaveBehindOk()
        {
            for (int s = 0; s < sections.Count; s++)
            {
                DeploySection section = sections[s];
                if (section.isTarget) continue;
                FillSelectedScratch(section);
                if (leaveBehindScratch.Count == 0) continue;
                if (!OutpostPawnIdeologyUtil.BulkRemovalSelectionIsAllowed(section.source, leaveBehindScratch))
                    return false;
            }
            return true;
        }

        private bool TryGetSupportLeaveBehindReject(out string reject)
        {
            reject = null;
            for (int s = 0; s < sections.Count; s++)
            {
                DeploySection section = sections[s];
                if (section.isTarget) continue;
                FillSelectedScratch(section);
                if (leaveBehindScratch.Count == 0) continue;
                if (OutpostPawnIdeologyUtil.TryGetBulkRemovalRejectReason(section.source, leaveBehindScratch, out reject))
                    return true;
            }
            return false;
        }

        private void FillSelectedScratch(DeploySection section)
        {
            leaveBehindScratch.Clear();
            if (section == null) return;
            for (int i = 0; i < section.candidates.Count; i++)
            {
                Pawn p = section.candidates[i];
                if (p?.ThingID != null && selectedThingIds.Contains(p.ThingID))
                    leaveBehindScratch.Add(p);
            }
        }

        private bool CanSelectPawn(DeploySection section, Pawn pawn, bool currentlySelected)
        {
            if (section == null || pawn == null) return false;
            if (currentlySelected) return true;
            if (!enforceBudget) return true;
            float used = SectionUsed(section);
            float cost = OutpostStrengthBudget.GetPawnCost(pawn);
            return OutpostStrengthBudget.IsUnderBudget(used + cost, section.available);
        }

        public override void PostClose()
        {
            PawnRosterHeaderFilter.CloseDropdown();
            base.PostClose();
            if (!resolved && traveler != null && !traveler.Destroyed && outpost != null && !outpost.Destroyed)
            {
                Find.WindowStack.Add(new Dialog_OutpostDefenseChoice(
                    traveler, outpost, manager, isSkirmishFollowUp, isTrenchIntercept: isTrenchCorridor));
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (PawnRosterHeaderFilter.TryCloseDropdownOnCancel())
                return;

            RefreshAfterArmoryIfNeeded();
            PawnRosterPaintSelect.BeginFrame(this);

            float y = 0f;
            Text.Font = GameFont.Medium;
            OutpostStrengthBudgetUi.LabelAnchored(
                new Rect(0f, y, inRect.width, Outpost_Dialog_UI.DialogTitleHeight),
                "TSA_WD_StrengthBudget_DeployTitle".Translate(),
                TextAnchor.MiddleCenter);
            y += Outpost_Dialog_UI.DialogTitleRowAdvance;
            Text.Font = GameFont.Small;

            float bannerH = Mathf.Max(OutpostStrengthBudgetUi.BoxLineH, Text.CalcHeight(cachedBanner, inRect.width));
            OutpostStrengthBudgetUi.LabelAnchored(
                new Rect(0f, y, inRect.width, bannerH), cachedBanner, TextAnchor.MiddleCenter);
            y += bannerH + 6f;

            float toolbarRight = inRect.width;
            Rect deselectBtn = new Rect(
                toolbarRight - DeselectBtnWidth,
                y,
                DeselectBtnWidth,
                ViewControlsH);
            Rect smartBtn = new Rect(
                deselectBtn.x - ToolbarBtnGap - SmartSelectBtnWidth,
                y,
                SmartSelectBtnWidth,
                ViewControlsH);

            bool showOutpostTabs = sections.Count > 1;
            if (showOutpostTabs)
            {
                string outpostsLabel = "TSA_WD_OutpostDefense_OutpostsLabel".Translate();
                float outpostsLabelW = Mathf.Max(72f, Text.CalcSize(outpostsLabel).x + 4f);
                OutpostStrengthBudgetUi.LabelAnchored(
                    new Rect(0f, y, outpostsLabelW, ViewControlsH),
                    outpostsLabel,
                    TextAnchor.MiddleLeft);
            }

            TooltipHandler.TipRegion(smartBtn, "TSA_WD_OutpostDefense_SmartSelectTip".Translate());
            TooltipHandler.TipRegion(deselectBtn, "TSA_WD_OutpostDefense_DeselectAllTip".Translate());
            if (WorldDomination_UIUtils.ButtonTextWithIcon(
                smartBtn,
                WorldDomination_UIUtils.RosterSmartIcon,
                "TSA_WD_OutpostDefense_SmartSelect".Translate()))
            {
                SmartSelectAllSections();
            }
            if (Widgets.ButtonText(deselectBtn, "TSA_WD_OutpostDefense_DeselectAll".Translate()))
                DeselectAll();

            PlayerPawnRosterUtility.DrawRosterViewControls(
                y, ViewControlsH, smartBtn.x - ToolbarBtnGap, ColWindow, RestoreDefaultView,
                () => Find.WindowStack.Add(new Dialog_PawnRosterColumns(ColWindow, OnColumnsChanged)),
                this);
            y += ViewControlsH + 4f;
            RebuildIfViewChanged();

            if (showOutpostTabs)
            {
                Widgets.DrawLineHorizontal(0f, y, inRect.width);
                y += 6f;
                y = DrawOutpostTabBar(new Rect(0f, y, inRect.width, TabBarH));
                y += 6f;
            }

            DeploySection section = ActiveSection;
            if (section == null) return;

            float rowH = OutpostStrengthBudgetUi.DeployRowHeight;
            float tableBottom = inRect.height - OutpostStrengthBudgetUi.BottomH - 8f;
            const float scrollBarW = 16f;
            float fixedW = FixedColumnsWidth;
            float tableWidth = Mathf.Max(Mathf.Max(200f, inRect.width - scrollBarW), fixedW + 80f);
            float nameW = Mathf.Max(80f, tableWidth - fixedW);

            float used = SectionUsed(section);
            bool under = SectionUnderBudget(section);
            bool showMeter = enforceBudget;
            bool showArmory = OutpostArmoryUtility.FeatureEnabled && section.source != null;
            if (showMeter || showArmory)
            {
                float curX = 0f;
                if (showArmory)
                {
                    Rect armoryRect = new Rect(curX, y, ArmoryBtnWidth, SectionMeterH);
                    DrawArmoryButton(armoryRect, section.source);
                    curX = armoryRect.xMax + ToolbarBtnGap;
                }

                if (showMeter)
                {
                    Color meterColor = under ? Color.white : ColorLibrary.RedReadable;
                    string meter = "TSA_WD_StrengthBudget_DeployingMeter".Translate(
                        used.ToString("F0"),
                        section.available.ToString("F0"),
                        section.maxOffensive.ToString("F0"));
                    float meterW = Mathf.Max(120f, inRect.width - curX);
                    Rect meterRect = new Rect(curX, y, meterW, SectionMeterH);
                    GUI.color = meterColor;
                    OutpostStrengthBudgetUi.LabelAnchored(meterRect, meter, TextAnchor.MiddleLeft);
                    GUI.color = Color.white;
                    TooltipHandler.TipRegion(
                        meterRect,
                        "TSA_WD_StrengthBudget_DeployMeterTip".Translate()
                            + "\n\n" + "TSA_WD_StrengthBudget_DeployStoredTip".Translate());
                }

                y += SectionMeterH + 2f;
            }

            DrawSortableTableHeader(new Rect(0f, y, tableWidth, HeaderHeight), nameW, section);
            Widgets.DrawLineHorizontal(0f, y + HeaderHeight, inRect.width);
            y += HeaderHeight + 4f;

            float listTop = y;
            Rect listOut = new Rect(0f, listTop, inRect.width, Mathf.Max(40f, tableBottom - listTop));
            float viewH = section.visibleRows.Count * rowH + 4f;
            Rect listView = new Rect(0f, 0f, tableWidth, Mathf.Max(listOut.height, viewH));
            Widgets.BeginScrollView(listOut, ref scrollPosition, listView);
            for (int i = 0; i < section.visibleRows.Count; i++)
                DrawRow(0f, i * rowH, tableWidth, nameW, section.visibleRows[i], section);
            Widgets.EndScrollView();

            bool canFight = TargetSelectedCount() > 0 && AllSectionsUnderBudget() && SupportLeaveBehindOk();
            Rect btnRow = new Rect(0f, inRect.height - OutpostStrengthBudgetUi.BottomH, inRect.width, 36f);
            if (Widgets.ButtonText(btnRow.LeftHalf().ContractedBy(2f), "TSA_WD_Conquest_Back".Translate()))
                Close();

            Rect fightRect = btnRow.RightHalf().ContractedBy(2f);
            if (!canFight && Mouse.IsOver(fightRect) && TryGetSupportLeaveBehindReject(out string leaveReject) && !leaveReject.NullOrEmpty())
                TooltipHandler.TipRegion(fightRect, leaveReject);
            GUI.enabled = canFight;
            if (Widgets.ButtonText(fightRect, "TSA_WD_OutpostDefense_FightManually".Translate()))
                ConfirmFight();
            GUI.enabled = true;

            PawnRosterHeaderFilter.DrawDropdownIfOpen();
        }

        /// <summary>
        /// Horizontal outpost tabs. Scrolls when labels do not fit (many nearby supports).
        /// </summary>
        private float DrawOutpostTabBar(Rect barRect)
        {
            if (tabWidths.Count != sections.Count)
                RebuildTabWidths();

            float totalW = 0f;
            for (int i = 0; i < tabWidths.Count; i++)
                totalW += tabWidths[i] + (i > 0 ? TabGap : 0f);

            bool needScroll = totalW > barRect.width + 0.5f;
            // Slightly taller bar when scrolling so the horizontal scrollbar has room.
            Rect outer = needScroll
                ? new Rect(barRect.x, barRect.y, barRect.width, barRect.height + 16f)
                : barRect;
            Rect viewRect = new Rect(0f, 0f, needScroll ? totalW : outer.width, TabBarH);

            if (needScroll)
                tabScrollPosition = GUI.BeginScrollView(outer, tabScrollPosition, viewRect, true, false);
            else
                GUI.BeginGroup(outer);

            float x = 0f;
            Text.Font = GameFont.Small;
            for (int i = 0; i < sections.Count; i++)
            {
                float w = tabWidths[i];
                Rect tabRect = new Rect(x, 0f, w, TabBarH);
                bool selected = i == selectedSectionIndex;
                if (selected)
                    Widgets.DrawHighlightSelected(tabRect);
                else
                    Widgets.DrawHighlightIfMouseover(tabRect);

                Widgets.DrawBox(tabRect, 1);
                string label = sections[i].cachedTabLabel ?? sections[i].cachedHeaderLabel ?? "?";
                OutpostStrengthBudgetUi.LabelAnchored(tabRect, label.Truncate(w - 8f), TextAnchor.MiddleCenter);
                TooltipHandler.TipRegion(tabRect, sections[i].cachedHeaderLabel);

                if (Widgets.ButtonInvisible(tabRect))
                {
                    selectedSectionIndex = i;
                    scrollPosition = Vector2.zero;
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }

                x += w + TabGap;
            }

            if (needScroll)
                GUI.EndScrollView();
            else
                GUI.EndGroup();

            return outer.yMax;
        }

        private void DrawArmoryButton(Rect rect, WorldObject_WD_Outpost source)
        {
            bool enabled = OutpostArmoryUtility.CanMutate(source);
            if (!enabled) GUI.color = new Color(1f, 1f, 1f, 0.45f);
            bool clicked = WorldDomination_UIUtils.ButtonTextWithIcon(
                rect,
                Dialog_OutpostArmoryAssets.ArmoryIcon,
                "TSA_WD_Armory_OpenButton".Translate());
            GUI.color = Color.white;

            TooltipHandler.TipRegion(rect, enabled
                ? "TSA_WD_Armory_OpenButtonTip".Translate()
                : "TSA_WD_Armory_FailManualDefense".Translate());

            if (clicked && enabled)
            {
                armoryDirty = true;
                Find.WindowStack.Add(new Dialog_OutpostArmory(source));
            }
        }

        private void DrawSortableTableHeader(Rect hRect, float nameW, DeploySection section)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            float curX = hRect.x;
            DrawSelectAllHeader(ref curX, hRect, section);
            curX += OutpostStrengthBudgetUi.ColIconDeploy;
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

        private void DrawSelectAllHeader(ref float curX, Rect hRect, DeploySection section)
        {
            Rect selHdr = new Rect(curX, hRect.y, ColSelect, hRect.height);
            if (Mouse.IsOver(selHdr)) Widgets.DrawHighlight(selHdr);

            int visible = section?.visibleRows.Count ?? 0;
            bool allSelected = visible > 0 && AreAllVisibleSelected(section);
            float box = 18f;
            float cx = selHdr.x + (ColSelect - box) * 0.5f;
            float cy = selHdr.y + (hRect.height - box) * 0.5f;
            Widgets.CheckboxDraw(cx, cy, allSelected, visible == 0, box);

            TooltipHandler.TipRegion(selHdr, "TSA_WD_PawnCol_SelectColumnTip".Translate());
            if (visible > 0 && Widgets.ButtonInvisible(selHdr))
            {
                ToggleSelectAllVisible(section);
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
            curX += ColSelect;
        }

        private bool AreAllVisibleSelected(DeploySection section)
        {
            if (section == null) return false;
            int visible = 0;
            for (int i = 0; i < section.visibleRows.Count; i++)
            {
                Pawn p = section.visibleRows[i];
                if (p?.ThingID == null) continue;
                visible++;
                if (!selectedThingIds.Contains(p.ThingID)) return false;
            }
            return visible > 0;
        }

        private void ToggleSelectAllVisible(DeploySection section)
        {
            if (section == null) return;
            if (AreAllVisibleSelected(section))
            {
                for (int i = 0; i < section.visibleRows.Count; i++)
                {
                    Pawn p = section.visibleRows[i];
                    if (p?.ThingID != null) selectedThingIds.Remove(p.ThingID);
                }
                return;
            }

            for (int i = 0; i < section.visibleRows.Count; i++)
            {
                Pawn p = section.visibleRows[i];
                if (p?.ThingID == null) continue;
                if (!CanSelectPawn(section, p, currentlySelected: false)) continue;
                selectedThingIds.Add(p.ThingID);
                if (!section.isTarget)
                {
                    FillSelectedScratch(section);
                    if (!OutpostPawnIdeologyUtil.BulkRemovalSelectionIsAllowed(section.source, leaveBehindScratch))
                        selectedThingIds.Remove(p.ThingID);
                }
            }
        }

        private void ToggleSort(string id)
        {
            view.Sort.ClickFromHeader(id, PawnRosterBudgetTable.FirstClickAscending(ColWindow, id));
            RebuildVisibleRows();
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        private void DrawRow(float x, float y, float width, float nameW, Pawn pawn, DeploySection section)
        {
            if (pawn == null || section == null || pawn.ThingID == null) return;
            bool selected = selectedThingIds.Contains(pawn.ThingID);
            bool canSelect = CanSelectPawn(section, pawn, selected);
            float rowH = OutpostStrengthBudgetUi.DeployRowHeight;
            float cost = OutpostStrengthBudget.GetPawnCost(pawn);

            Rect rowRect = new Rect(x, y, width, rowH);
            Outpost_Dialog_UI.DrawSelectedRowTint(rowRect, selected);

            float curX = x;
            Rect selRect = new Rect(curX, y, ColSelect, rowH);
            float box = 24f;
            float cx = curX + (ColSelect - box) * 0.5f;
            float cy = y + (rowH - box) * 0.5f;
            bool nowSelected = PawnRosterPaintSelect.Draw(
                this, selRect, cx, cy, box, pawn.ThingID, selectedThingIds, canSelect);
            if (nowSelected != selected)
            {
                if (nowSelected && !section.isTarget)
                {
                    FillSelectedScratch(section);
                    if (!OutpostPawnIdeologyUtil.BulkRemovalSelectionIsAllowed(section.source, leaveBehindScratch))
                        selectedThingIds.Remove(pawn.ThingID);
                }
                selected = selectedThingIds.Contains(pawn.ThingID);
            }
            curX += ColSelect;

            float contentY = y + rowH / 2f;
            float portrait = OutpostStrengthBudgetUi.DeployPortraitSize;
            Rect portraitRect = new Rect(
                curX + (OutpostStrengthBudgetUi.ColIconDeploy - portrait) / 2f,
                contentY - portrait / 2f,
                portrait,
                portrait);
            OutpostStrengthBudgetUi.DrawPawnPortrait(portraitRect, pawn);
            float portraitColX = curX;
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

            OutpostStrengthBudgetUi.FinishDeploySelectedRow(rowRect, selected);

            // Info-card hit starts at the portrait so checkboxes stay usable.
            Rect infoHit = new Rect(portraitColX, y, width - (portraitColX - x), rowH);
            if (!gearClicked && Widgets.ButtonInvisible(infoHit))
                Find.WindowStack.Add(new Dialog_InfoCard(pawn));
        }

        private void ConfirmFight()
        {
            if (resolved) return;
            if (!SupportLeaveBehindOk()) return;

            DeploySection target = sections.Count > 0 ? sections[0] : null;
            if (target == null || !target.isTarget) return;

            var deploy = new List<Pawn>();
            for (int i = 0; i < target.candidates.Count; i++)
            {
                Pawn p = target.candidates[i];
                if (p?.ThingID == null || !selectedThingIds.Contains(p.ThingID)) continue;
                deploy.Add(p);
            }
            if (deploy.Count == 0) return;

            var supports = new List<KeyValuePair<WorldObject_WD_Outpost, List<Pawn>>>();
            for (int s = 1; s < sections.Count; s++)
            {
                DeploySection section = sections[s];
                if (section.isTarget || section.source == null) continue;
                var list = new List<Pawn>();
                for (int i = 0; i < section.candidates.Count; i++)
                {
                    Pawn p = section.candidates[i];
                    if (p?.ThingID == null || !selectedThingIds.Contains(p.ThingID)) continue;
                    list.Add(p);
                }
                if (list.Count == 0) continue;
                supports.Add(new KeyValuePair<WorldObject_WD_Outpost, List<Pawn>>(section.source, list));
            }

            if (!AllSectionsUnderBudget()) return;

            resolved = true;
            int raidDelay = isSkirmishFollowUp ? 0 : 900;
            outpost?.ClearPendingSkirmishDefense();
            if (isTrenchCorridor)
                WdTrenchInterceptUtility.MarkResolved(traveler, outpost);
            Close(doCloseSound: false);
            if (!WD_OutpostDefenseEncounterUtility.StartManualDefenseEncounter(
                    traveler, outpost, raidDelay, deploy, supports.Count > 0 ? supports : null, isTrenchCorridor))
                Raid_Simulated.ResolvePlayerOutpostRaidArrival(traveler, manager, allowSkirmishRetry: false);
        }
    }
}
