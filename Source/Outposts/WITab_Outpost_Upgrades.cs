using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace TSA_WorldDomination
{
    [StaticConstructorOnStartup]
    public class WITab_Outpost_Upgrades : WITab
    {
        private const float HeaderOffsetY = 0f;
        private const float ColGap = 18f;
        private const float SearchRowHeight = 28f;
        private const float SearchRowGap = 6f;
        /// <summary>Match the free-text search field height (UI_WINDOWS: Small ≥ 24).</summary>
        private const float SlateButtonHeight = SearchRowHeight;
        private const float StatusFilterButtonWidth = 175f;
        private const float ImpactFilterButtonWidth = 175f;
        private const float FilterButtonGap = 8f;
        private const float IconColW = 56f;
        private const float IconPadding = 8f;

        private static readonly Texture2D IconStatusFilter =
            ContentFinder<Texture2D>.Get("UI/Commands/Build", false)
            ?? ContentFinder<Texture2D>.Get("UI/Commands/BuildRoad", false);
        private static readonly Texture2D IconImpactFilter =
            ContentFinder<Texture2D>.Get("UI/Commands/AdvancedFilter", false)
            ?? ContentFinder<Texture2D>.Get("UI/Commands/ColumnPicker", false);
        /// <summary>Funnel texture is greyscale; Build status icon is already cyan in the asset.</summary>
        private static readonly Color ImpactFilterIconCyan = WorldOverlayLineMaterials.DarkCyanColor;
        private const float GroupSeparatorHeight = 8f;
        private static readonly Color GroupSeparatorColor = new Color(1f, 1f, 1f, 0.22f);
        private const float BatchConfirmButtonHeight = 32f;
        private static readonly Color BatchConfirmIdleFill = new Color(0.32f, 0.32f, 0.34f);
        private static readonly Color BatchConfirmReadyFill = new Color(0.22f, 0.55f, 0.28f);

        private Vector2 rightScrollPos;
        private Vector2 leftDetailScrollPos;
        private string upgradeSearchFilter = "";
        private UpgradeTabFilter upgradeFilter = UpgradeTabFilter.All;
        private UpgradeBenefitKind? upgradeImpactFilter;
        private string selectedUpgradeDefName;
        private WorldObject_WD_Outpost selectionOutpost;
        private readonly HashSet<string> batchSelectedDefNames = new HashSet<string>();
        /// <summary>Unchecked defName → aggregate fail reason when adding it would exceed stock/pods.</summary>
        private readonly Dictionary<string, string> batchCostBlockReasons = new Dictionary<string, string>();
        private readonly List<string> batchCostScratchNames = new List<string>(16);
        private readonly List<OutpostUpgradeDef> batchCostScratchDefs = new List<OutpostUpgradeDef>(16);
        private int batchCostCacheSelectionHash = int.MinValue;
        private int batchCostCacheRowsFingerprint = int.MinValue;
        private int batchCostCacheRowsTick = int.MinValue;

        private List<List<OutpostUpgradeDef>> cachedUpgradeGroups;
        private string cachedUpgradeGroupsDefName;

        private List<CachedUpgradeRow> cachedRows = new List<CachedUpgradeRow>();
        private int cachedRowsTick = -1;
        private int cachedRowsFingerprint;
        private static readonly List<UpgradeBenefitKind> ScratchBenefitKinds = new List<UpgradeBenefitKind>(8);
        private const float BatchCheckboxSize = 24f;

        private static readonly Dictionary<string, List<List<OutpostUpgradeDef>>> UpgradeGroupsByOutpostDef =
            new Dictionary<string, List<List<OutpostUpgradeDef>>>();

        private struct CachedUpgradeRow
        {
            public OutpostUpgradeDef Def;
            public int GroupIndex;
            public RowState State;
            public bool CanBuy;
            public bool IsPending;
            public OutpostUpgradeUtility.PurchaseCheck Check;
            public string RowTooltip;
            public string ImpactSummary;
            public UpgradeBenefitKind[] ImpactKinds;
        }

        private readonly struct RowState
        {
            public readonly bool builtHere;
            public readonly bool superseded;
            public readonly bool showBuy;
            public readonly bool deployed;
            public readonly bool futureTier;
            public readonly bool sequentialBlocked;

            public RowState(bool builtHere, bool superseded, bool showBuy, bool deployed, bool futureTier, bool sequentialBlocked)
            {
                this.builtHere = builtHere;
                this.superseded = superseded;
                this.showBuy = showBuy;
                this.deployed = deployed;
                this.futureTier = futureTier;
                this.sequentialBlocked = sequentialBlocked;
            }
        }

        private enum UpgradeTabFilter
        {
            All,
            Built,
            NotBuilt,
            Buildable
        }

        public WITab_Outpost_Upgrades()
        {
            size = new Vector2(960f, 580f);
            labelKey = "TSA_WD_OutpostUpgrades_TabLabel";
        }

        private WorldObject_WD_Outpost SelOutpost => SelObject as WorldObject_WD_Outpost;
        public override bool IsVisible => SelOutpost != null && SelOutpost.Faction == Faction.OfPlayer;

        protected override void FillTab()
        {
            WorldObject_WD_Outpost outpost = SelOutpost;
            if (outpost == null) return;

            Rect body = new Rect(0f, HeaderOffsetY, size.x, size.y - HeaderOffsetY).ContractedBy(10f);
            Text.Font = GameFont.Medium;
            string headline = OutpostTranslationUtil.TabHeadline(outpost, "TSA_WD_OutpostUpgrades_TabLabel");
            LabelAnchored(new Rect(body.x, body.y, body.width, 30f), headline, TextAnchor.MiddleLeft);
            Text.Font = GameFont.Small;
            Widgets.DrawLineHorizontal(body.x, body.y + 32f, body.width);

            List<List<OutpostUpgradeDef>> groups = GetUpgradeGroupsCached(outpost);
            EnsureCachedRows(outpost, groups);

            float columnsTop = body.y + 38f;
            float columnsBottom = body.yMax;
            float leftW = Mathf.Max(260f, body.width * 0.42f);
            Rect leftArea = new Rect(body.x, columnsTop, leftW, columnsBottom - columnsTop);
            Rect rightArea = new Rect(body.x + leftW + ColGap, columnsTop, body.xMax - (body.x + leftW + ColGap), columnsBottom - columnsTop);
            Widgets.DrawLineVertical(body.x + leftW + ColGap * 0.5f, columnsTop, columnsBottom - columnsTop);

            EnsureDefaultSelection(outpost);
            DrawLeftColumn(leftArea, outpost);
            DrawRightColumn(rightArea, outpost);
        }

        private void DrawLeftColumn(Rect leftArea, WorldObject_WD_Outpost outpost)
        {
            float lx = leftArea.x;
            float lw = leftArea.width;
            float ly = leftArea.y;

            ly = Outpost_Upgrade_UI.DrawAggregateBenefitsBox(lx, ly, lw, outpost);
            ly += Outpost_Dialog_UI.OutcomeBoxGap;

            if (outpost.HasPendingUpgrade)
            {
                GUI.color = new Color(0.95f, 0.75f, 0.35f);
                string pendingLine;
                if (outpost.PendingUpgradeCount == 1)
                {
                    string defName = outpost.PendingUpgradeDefName;
                    var pendingDef = DefDatabase<OutpostUpgradeDef>.GetNamedSilentFail(defName);
                    string pendingLabel = pendingDef != null ? pendingDef.LabelCap : defName;
                    pendingLine = OutpostTranslationUtil.Key("TSA_WD_OutpostUpgrades_PendingCaravan", pendingLabel);
                }
                else
                {
                    pendingLine = OutpostTranslationUtil.Key("TSA_WD_OutpostUpgrades_PendingCount", outpost.PendingUpgradeCount.ToString());
                }
                Rect pendingRect = new Rect(lx, ly, lw, Outpost_Dialog_UI.OutcomeLineH);
                Widgets.Label(pendingRect, pendingLine);
                GUI.color = Color.white;
                ly += Outpost_Dialog_UI.OutcomeLineH + 6f;
            }

            Widgets.DrawLineHorizontal(lx, ly, lw);
            ly += 8f;

            GUI.color = Outpost_Dialog_UI.TheoreticalLabelColor;
            Widgets.Label(new Rect(lx, ly, lw, Outpost_Dialog_UI.OutcomeLineH), OutpostTranslationUtil.Key("TSA_WD_OutpostUpgrades_SelectedDetail"));
            GUI.color = Color.white;
            ly += Outpost_Dialog_UI.OutcomeLineH + 4f;

            CachedUpgradeRow? selectedRow = FindCachedRow(selectedUpgradeDefName);
            float detailContentH = selectedRow.HasValue
                ? Outpost_Upgrade_UI.MeasureSelectedUpgradeDetailHeight(selectedRow.Value.Def, !string.IsNullOrEmpty(selectedRow.Value.Def?.description))
                : Outpost_Dialog_UI.OutcomeLineH;

            float detailScrollH = leftArea.yMax - ly - 4f;
            Rect detailOuter = new Rect(lx, ly, lw, detailScrollH);
            Rect detailView = new Rect(0f, 0f, lw - 16f, Mathf.Max(detailContentH, detailScrollH));
            Widgets.BeginScrollView(detailOuter, ref leftDetailScrollPos, detailView);

            if (selectedRow.HasValue)
            {
                var row = selectedRow.Value;
                Outpost_Upgrade_UI.DrawSelectedUpgradeDetail(
                    0f, 0f, detailView.width, outpost, row.Def,
                    row.State.deployed, row.State.superseded, row.State.sequentialBlocked,
                    row.State.futureTier, row.State.showBuy, row.CanBuy, row.IsPending,
                    row.Check, onBuild: null);
            }
            else
            {
                Widgets.Label(new Rect(0f, 0f, detailView.width, Outpost_Dialog_UI.OutcomeLineH),
                    OutpostTranslationUtil.Key("TSA_WD_OutpostUpgrades_NoSelection"));
            }

            Widgets.EndScrollView();
        }

        private void DrawRightColumn(Rect rightArea, WorldObject_WD_Outpost outpost)
        {
            float y = rightArea.y;

            GUI.color = new Color(0.75f, 0.82f, 1f);
            Widgets.Label(new Rect(rightArea.x, y, rightArea.width, Outpost_Upgrade_UI.RightColHeaderH), OutpostTranslationUtil.Key("TSA_WD_OutpostUpgrades_ChooseHeader"));
            GUI.color = Color.white;
            y += Outpost_Upgrade_UI.RightColHeaderH + 2f;

            float filterRowW = StatusFilterButtonWidth + FilterButtonGap + ImpactFilterButtonWidth;
            Rect impactFilterRect = new Rect(rightArea.xMax - ImpactFilterButtonWidth, y, ImpactFilterButtonWidth, SlateButtonHeight);
            Rect filterRect = new Rect(impactFilterRect.x - FilterButtonGap - StatusFilterButtonWidth, y, StatusFilterButtonWidth, SlateButtonHeight);
            Rect searchRect = new Rect(rightArea.x, y, rightArea.width - filterRowW - FilterButtonGap, SearchRowHeight);

            string oldSearch = upgradeSearchFilter;
            upgradeSearchFilter = Widgets.TextField(searchRect, upgradeSearchFilter);
            if (upgradeSearchFilter != oldSearch)
                rightScrollPos = Vector2.zero;

            if (string.IsNullOrEmpty(upgradeSearchFilter))
            {
                GUI.color = new Color(1f, 1f, 1f, 0.4f);
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.Font = GameFont.Tiny;
                Widgets.Label(searchRect, OutpostTranslationUtil.Key("TSA_WD_OutpostUpgrades_SearchPlaceholder"));
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
            }

            if (WorldDomination_UIUtils.ButtonTextWithIcon(
                    filterRect, IconStatusFilter, GetFilterLabel(upgradeFilter)))
            {
                var opts = new List<FloatMenuOption>
                {
                    new FloatMenuOption(OutpostTranslationUtil.Key("TSA_WD_OutpostUpgrades_FilterAll"), () => SetFilter(UpgradeTabFilter.All)),
                    new FloatMenuOption(OutpostTranslationUtil.Key("TSA_WD_OutpostUpgrades_FilterBuilt"), () => SetFilter(UpgradeTabFilter.Built)),
                    new FloatMenuOption(OutpostTranslationUtil.Key("TSA_WD_OutpostUpgrades_FilterNotBuilt"), () => SetFilter(UpgradeTabFilter.NotBuilt)),
                    new FloatMenuOption(OutpostTranslationUtil.Key("TSA_WD_OutpostUpgrades_FilterBuildable"), () => SetFilter(UpgradeTabFilter.Buildable)),
                };
                Find.WindowStack.Add(new FloatMenu(opts));
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            ClearImpactFilterIfUnavailable();
            if (WorldDomination_UIUtils.ButtonTextWithIcon(
                    impactFilterRect, IconImpactFilter, GetImpactFilterButtonLabel(upgradeImpactFilter),
                    iconTint: ImpactFilterIconCyan))
            {
                var opts = new List<FloatMenuOption>
                {
                    new FloatMenuOption(OutpostTranslationUtil.Key("TSA_WD_OutpostUpgrades_FilterAllBonuses"), () => SetImpactFilter(null)),
                };
                var present = CollectPresentImpactKinds();
                present.Sort((a, b) => string.Compare(
                    Outpost_Upgrade_UI.GetImpactFilterLabel(a),
                    Outpost_Upgrade_UI.GetImpactFilterLabel(b),
                    StringComparison.CurrentCultureIgnoreCase));
                for (int i = 0; i < present.Count; i++)
                {
                    UpgradeBenefitKind kind = present[i];
                    opts.Add(new FloatMenuOption(Outpost_Upgrade_UI.GetImpactFilterLabel(kind), () => SetImpactFilter(kind)));
                }
                Find.WindowStack.Add(new FloatMenu(opts));
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            y += SearchRowHeight + SearchRowGap;

            EnsureBatchCostBlockCache(outpost);
            int batchCount = CountValidBatchSelection(outpost);
            bool anyPending = outpost.HasPendingUpgrade;
            string batchLabel = OutpostTranslationUtil.Key("TSA_WD_OutpostUpgrades_BuildSelected", batchCount.ToString());
            float batchBtnW = MeasureBatchConfirmButtonWidth(batchLabel) * 1.35f;
            batchBtnW = Mathf.Clamp(batchBtnW, 260f, rightArea.width);
            Rect batchRect = new Rect(
                rightArea.x + (rightArea.width - batchBtnW) * 0.5f,
                rightArea.yMax - BatchConfirmButtonHeight,
                batchBtnW,
                BatchConfirmButtonHeight);
            bool canOpenBatch = !anyPending && batchCount > 0;
            bool prevEnabled = GUI.enabled;
            GUI.enabled = prevEnabled && canOpenBatch;
            Color batchFill = canOpenBatch ? BatchConfirmReadyFill : BatchConfirmIdleFill;
            if (WorldDomination_UIUtils.ButtonTextWithIcon(
                    batchRect, null, batchLabel, centerContents: true, fill: batchFill, font: GameFont.Small))
            {
                TryOpenBatchConfirm(outpost);
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
            GUI.enabled = prevEnabled;
            if (anyPending)
                TooltipHandler.TipRegion(batchRect, OutpostTranslationUtil.Key("TSA_WD_OutpostUpgrades_InTransit"));
            else if (batchCount <= 0)
                TooltipHandler.TipRegion(batchRect, OutpostTranslationUtil.Key("TSA_WD_OutpostUpgrades_BatchNeedSelection"));
            else
                TooltipHandler.TipRegion(batchRect, OutpostTranslationUtil.Key("TSA_WD_OutpostUpgrades_BuildSelectedTip", batchCount.ToString()));

            List<CachedUpgradeRow> ordered = BuildOrderedVisibleRows(outpost);
            float scrollHeight = 8f;
            int prevGroupForHeight = -1;
            for (int oi = 0; oi < ordered.Count; oi++)
            {
                if (prevGroupForHeight >= 0 && ordered[oi].GroupIndex != prevGroupForHeight)
                    scrollHeight += GroupSeparatorHeight;
                scrollHeight += Outpost_Upgrade_UI.CompactRowHeight + Outpost_Upgrade_UI.CompactRowPadding;
                prevGroupForHeight = ordered[oi].GroupIndex;
            }

            float scrollBottom = batchRect.y - SearchRowGap;
            Rect scrollOuter = new Rect(rightArea.x, y, rightArea.width, Mathf.Max(40f, scrollBottom - y));
            Rect viewRect = new Rect(0f, 0f, rightArea.width - 16f, Mathf.Max(scrollHeight, 1f));
            Widgets.BeginScrollView(scrollOuter, ref rightScrollPos, viewRect);

            float curY = 0f;
            int prevGroupIndex = -1;
            float checkColW = BatchCheckboxSize + 8f;

            foreach (var row in ordered)
            {
                // Rebuild after paint-drag selection mutations so later rows see updated aggregate blocks.
                EnsureBatchCostBlockCache(outpost);

                if (prevGroupIndex >= 0 && row.GroupIndex != prevGroupIndex)
                {
                    float sepY = curY + GroupSeparatorHeight * 0.5f;
                    GUI.color = GroupSeparatorColor;
                    Widgets.DrawLineHorizontal(IconPadding, sepY, viewRect.width - IconPadding * 2f);
                    GUI.color = Color.white;
                    curY += GroupSeparatorHeight;
                }
                prevGroupIndex = row.GroupIndex;

                float rowH = Outpost_Upgrade_UI.CompactRowHeight + Outpost_Upgrade_UI.CompactRowPadding;
                Rect rowRect = new Rect(0f, curY, viewRect.width, rowH);

                Color? bg = Outpost_Upgrade_UI.GetRowBackground(
                    row.State.deployed, row.State.superseded, row.State.sequentialBlocked,
                    row.State.futureTier, row.IsPending);
                if (bg.HasValue)
                    Widgets.DrawBoxSolid(rowRect, bg.Value);

                // Same light-red tint as production options that cannot be selected (materials / research /
                // missing previous chain tier / etc.).
                Outpost_Dialog_UI.DrawUnmetRequirementsRowTint(
                    rowRect,
                    (row.State.showBuy && !row.CanBuy) || row.State.sequentialBlocked);

                bool isSelected = row.Def != null && row.Def.defName == selectedUpgradeDefName;
                if (isSelected)
                {
                    GUI.color = Outpost_Upgrade_UI.RowBgSelected;
                    GUI.DrawTexture(rowRect, BaseContent.WhiteTex);
                    GUI.color = Color.white;
                }

                float rowContentY = curY + (rowRect.height - Outpost_Upgrade_UI.CompactRowHeight) * 0.5f;
                Texture2D icon = Outpost_Upgrade_UI.GetUpgradeIcon(row.Def);
                float iconY = rowContentY + (Outpost_Upgrade_UI.CompactRowHeight - Outpost_Upgrade_UI.CompactRowIconSize) * 0.5f;
                Rect iconRect = new Rect(IconPadding, iconY, Outpost_Upgrade_UI.CompactRowIconSize, Outpost_Upgrade_UI.CompactRowIconSize);
                if (row.State.futureTier && !row.State.sequentialBlocked)
                    GUI.color = new Color(1f, 1f, 1f, 0.45f);
                if (icon != null) Outpost_Upgrade_UI.DrawTextureTopFit(iconRect.ContractedBy(2f), icon);
                GUI.color = Color.white;

                string label = row.Def?.LabelCap ?? "";
                if (row.Def != null && row.Def.lineTier > 1)
                    label += " (Lv " + row.Def.lineTier + ")";

                bool checkedNow = row.Def != null && batchSelectedDefNames.Contains(row.Def.defName);
                bool sameLineBlocked = row.Def != null && !checkedNow && IsSameLineAlreadySelected(row.Def);
                string batchCostBlockReason = null;
                bool batchCostBlocked = row.Def != null
                    && !checkedNow
                    && batchCostBlockReasons.TryGetValue(row.Def.defName, out batchCostBlockReason);
                bool canCheck = row.Def != null
                    && row.State.showBuy
                    && row.CanBuy
                    && !anyPending
                    && !row.IsPending
                    && !sameLineBlocked
                    && !batchCostBlocked;
                Rect checkRect = new Rect(
                    viewRect.width - checkColW,
                    rowContentY + (Outpost_Upgrade_UI.CompactRowHeight - BatchCheckboxSize) * 0.5f,
                    BatchCheckboxSize,
                    BatchCheckboxSize);
                if (!canCheck && checkedNow && row.Def != null)
                {
                    batchSelectedDefNames.Remove(row.Def.defName);
                    InvalidateBatchCostBlockCache();
                }
                bool checkDraw = canCheck && checkedNow;
                GUI.enabled = canCheck;
                Widgets.Checkbox(checkRect.position, ref checkDraw, BatchCheckboxSize, disabled: !canCheck, paintable: true);
                GUI.enabled = true;
                if (canCheck && row.Def != null)
                {
                    if (checkDraw)
                    {
                        if (batchSelectedDefNames.Add(row.Def.defName))
                            InvalidateBatchCostBlockCache();
                    }
                    else if (batchSelectedDefNames.Remove(row.Def.defName))
                    {
                        InvalidateBatchCostBlockCache();
                    }
                }
                TooltipHandler.TipRegion(
                    checkRect,
                    GetBatchCheckboxTip(row, canCheck, checkDraw, anyPending, sameLineBlocked, batchCostBlocked, batchCostBlockReason));

                float textW = viewRect.width - IconColW - checkColW - 4f;
                Rect labelRect = new Rect(IconColW, rowContentY, textW, 22f);
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(labelRect, label);
                Text.Anchor = TextAnchor.UpperLeft;

                if (!string.IsNullOrEmpty(row.ImpactSummary))
                {
                    Rect impactRect = new Rect(IconColW, rowContentY + 22f, textW, 24f);
                    Text.Font = GameFont.Tiny;
                    GUI.color = new Color(0.65f, 0.65f, 0.65f);
                    Text.Anchor = TextAnchor.UpperLeft;
                    Widgets.Label(impactRect, row.ImpactSummary);
                    Text.Anchor = TextAnchor.UpperLeft;
                    GUI.color = Color.white;
                    Text.Font = GameFont.Small;
                }

                if (Mouse.IsOver(rowRect) && !Mouse.IsOver(checkRect)) Widgets.DrawHighlight(rowRect);
                if (isSelected)
                {
                    GUI.color = Color.white;
                    Widgets.DrawBox(rowRect, 1);
                    GUI.color = Color.white;
                }
                Rect clickRect = new Rect(0f, curY, viewRect.width - checkColW, rowH);
                if (Widgets.ButtonInvisible(clickRect) && row.Def != null)
                    selectedUpgradeDefName = row.Def.defName;
                if (!string.IsNullOrEmpty(row.RowTooltip))
                    TooltipHandler.TipRegion(clickRect, row.RowTooltip);

                curY += rowH;
            }

            Widgets.EndScrollView();
        }

        private int CountValidBatchSelection(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null || batchSelectedDefNames.Count == 0 || outpost.HasPendingUpgrade) return 0;
            int n = 0;
            foreach (string defName in batchSelectedDefNames)
            {
                CachedUpgradeRow? row = FindCachedRow(defName);
                if (row.HasValue && row.Value.State.showBuy && row.Value.CanBuy && !row.Value.IsPending)
                    n++;
            }
            return n;
        }

        private void InvalidateBatchCostBlockCache()
        {
            batchCostCacheSelectionHash = int.MinValue;
        }

        private int ComputeBatchSelectionHash()
        {
            // Order-independent: HashSet iteration order is not stable across add/remove sequences.
            unchecked
            {
                int h = batchSelectedDefNames.Count;
                foreach (string name in batchSelectedDefNames)
                    h ^= (name?.GetHashCode() ?? 0) * 397;
                return h;
            }
        }

        private void CollectBatchSelectionDefs(ICollection<string> defNames, List<OutpostUpgradeDef> into)
        {
            into.Clear();
            if (defNames == null) return;
            foreach (string defName in defNames)
            {
                CachedUpgradeRow? row = FindCachedRow(defName);
                if (!row.HasValue || row.Value.Def == null) continue;
                if (!row.Value.State.showBuy || !row.Value.CanBuy || row.Value.IsPending) continue;
                into.Add(row.Value.Def);
            }
        }

        private OutpostUpgradeUtility.BatchPurchaseCheck EvaluateBatchSelection(
            WorldObject_WD_Outpost outpost, ICollection<string> defNames)
        {
            CollectBatchSelectionDefs(defNames, batchCostScratchDefs);
            return OutpostUpgradeUtility.EvaluateBatchPurchase(outpost, batchCostScratchDefs);
        }

        /// <summary>
        /// Preemptive aggregate affordability for multi-select: block unchecked rows that would tip
        /// the current selection over stock/pods. Prunes the selection if live stock no longer covers it.
        /// </summary>
        private void EnsureBatchCostBlockCache(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null)
            {
                batchCostBlockReasons.Clear();
                return;
            }

            int selHash = ComputeBatchSelectionHash();
            if (batchCostCacheSelectionHash == selHash
                && batchCostCacheRowsFingerprint == cachedRowsFingerprint
                && batchCostCacheRowsTick == cachedRowsTick)
                return;

            batchCostBlockReasons.Clear();
            batchCostCacheSelectionHash = selHash;
            batchCostCacheRowsFingerprint = cachedRowsFingerprint;
            batchCostCacheRowsTick = cachedRowsTick;

            if (outpost.HasPendingUpgrade || batchSelectedDefNames.Count == 0)
                return;

            OutpostUpgradeUtility.BatchPurchaseCheck current = EvaluateBatchSelection(outpost, batchSelectedDefNames);
            if (!current.canBuy)
            {
                batchSelectedDefNames.Clear();
                batchCostCacheSelectionHash = ComputeBatchSelectionHash();
                return;
            }

            for (int i = 0; i < cachedRows.Count; i++)
            {
                CachedUpgradeRow row = cachedRows[i];
                if (row.Def == null) continue;
                if (batchSelectedDefNames.Contains(row.Def.defName)) continue;
                if (!row.State.showBuy || !row.CanBuy || row.IsPending) continue;
                if (IsSameLineAlreadySelected(row.Def)) continue;

                batchCostScratchNames.Clear();
                foreach (string selectedName in batchSelectedDefNames)
                    batchCostScratchNames.Add(selectedName);
                batchCostScratchNames.Add(row.Def.defName);

                OutpostUpgradeUtility.BatchPurchaseCheck tentative = EvaluateBatchSelection(outpost, batchCostScratchNames);
                if (!tentative.canBuy)
                {
                    batchCostBlockReasons[row.Def.defName] = tentative.reason
                        ?? OutpostTranslationUtil.Key("TSA_WD_OutpostUpgrades_UnableBuy");
                }
            }
        }

        private void TryOpenBatchConfirm(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null || outpost.HasPendingUpgrade) return;
            EnsureBatchCostBlockCache(outpost);
            CollectBatchSelectionDefs(batchSelectedDefNames, batchCostScratchDefs);
            if (batchCostScratchDefs.Count == 0) return;

            OutpostUpgradeUtility.BatchPurchaseCheck check =
                OutpostUpgradeUtility.EvaluateBatchPurchase(outpost, batchCostScratchDefs);
            if (!check.canBuy)
            {
                Messages.Message(check.reason ?? OutpostTranslationUtil.Key("TSA_WD_OutpostUpgrades_UnableBuy"), MessageTypeDefOf.RejectInput);
                return;
            }

            Find.WindowStack.Add(new Dialog_OutpostUpgradeBatchConfirm(outpost, check, () =>
            {
                batchSelectedDefNames.Clear();
                InvalidateBatchCostBlockCache();
                cachedRowsTick = -1;
            }));
        }

        private static float MeasureSlateButtonWidth(string label)
        {
            Text.Font = GameFont.Small;
            float labelW = Text.CalcSize(label ?? string.Empty).x;
            return WorldDomination_UIUtils.SlateNavIconPad
                + WorldDomination_UIUtils.SlateNavIconSize
                + WorldDomination_UIUtils.SlateNavIconTextGap
                + labelW
                + WorldDomination_UIUtils.SlateNavIconPad;
        }

        private static float MeasureBatchConfirmButtonWidth(string label)
        {
            Text.Font = GameFont.Small;
            float labelW = Text.CalcSize(label ?? string.Empty).x;
            return WorldDomination_UIUtils.SlateNavIconPad * 2f + labelW;
        }

        private void EnsureCachedRows(WorldObject_WD_Outpost outpost, List<List<OutpostUpgradeDef>> groups)
        {
            int fp = UpgradeRowsFingerprint(outpost);
            int tick = Find.TickManager.TicksGame;
            if (cachedRows.Count > 0
                && cachedRowsFingerprint == fp
                && cachedRowsTick >= 0
                && tick - cachedRowsTick < Outpost_Dialog_UI.LiveRefreshIntervalTicks)
                return;

            cachedRows.Clear();
            cachedRowsFingerprint = fp;
            cachedRowsTick = tick;
            const int purchaseLevel = 1;

            for (int gi = 0; gi < groups.Count; gi++)
            {
                var group = groups[gi];
                for (int i = 0; i < group.Count; i++)
                {
                    OutpostUpgradeDef def = group[i];
                    if (def == null) continue;
                    RowState rs = ComputeRowState(outpost, def);
                    bool isPending = outpost.IsUpgradePending(def.defName);

                    OutpostUpgradeUtility.PurchaseCheck check = default;
                    bool canBuy = false;
                    if (rs.showBuy || rs.sequentialBlocked || rs.futureTier || rs.superseded)
                    {
                        check = OutpostUpgradeUtility.EvaluatePurchase(outpost, def, purchaseLevel, ignoreLineTierForPreview: rs.sequentialBlocked || rs.futureTier || rs.superseded);
                        canBuy = check.canBuy;
                    }

                    ScratchBenefitKinds.Clear();
                    Outpost_Upgrade_UI.CollectBenefitKinds(def, ScratchBenefitKinds);
                    cachedRows.Add(new CachedUpgradeRow
                    {
                        Def = def,
                        GroupIndex = gi,
                        State = rs,
                        CanBuy = canBuy,
                        IsPending = isPending,
                        Check = check,
                        RowTooltip = BuildRowTooltip(rs, isPending, canBuy, check),
                        ImpactSummary = Outpost_Upgrade_UI.FormatCompactImpactSummary(def),
                        ImpactKinds = ScratchBenefitKinds.Count > 0
                            ? ScratchBenefitKinds.ToArray()
                            : Array.Empty<UpgradeBenefitKind>()
                    });
                }
            }
        }

        private static string BuildRowTooltip(RowState rs, bool isPending, bool canBuy, OutpostUpgradeUtility.PurchaseCheck check)
        {
            if (isPending)
                return OutpostTranslationUtil.Key("TSA_WD_OutpostUpgrades_AwaitingDelivery");
            if (rs.showBuy && !canBuy && !string.IsNullOrEmpty(check.reason))
                return check.reason;
            if (rs.sequentialBlocked)
                return OutpostTranslationUtil.Key("TSA_WD_OutpostUpgrades_PreviousUpgradeNeeded");
            if (rs.superseded)
                return OutpostTranslationUtil.Key("TSA_WD_OutpostUpgrades_InferiorOption");
            return null;
        }

        private string GetBatchCheckboxTip(
            CachedUpgradeRow row,
            bool canCheck,
            bool checkedNow,
            bool anyPending,
            bool sameLineBlocked,
            bool batchCostBlocked,
            string batchCostBlockReason)
        {
            if (canCheck)
            {
                return checkedNow
                    ? OutpostTranslationUtil.Key("TSA_WD_OutpostUpgrades_DeselectTip")
                    : OutpostTranslationUtil.Key("TSA_WD_OutpostUpgrades_SelectTip");
            }
            if (batchCostBlocked && !string.IsNullOrEmpty(batchCostBlockReason))
                return batchCostBlockReason;
            if (sameLineBlocked)
                return OutpostTranslationUtil.Key("TSA_WD_OutpostUpgrades_SelectDisabledSameLine");
            if (anyPending || row.IsPending)
                return OutpostTranslationUtil.Key("TSA_WD_OutpostUpgrades_SelectDisabledPending");
            if (!string.IsNullOrEmpty(row.RowTooltip))
                return row.RowTooltip;
            return OutpostTranslationUtil.Key("TSA_WD_OutpostUpgrades_SelectDisabled");
        }

        /// <summary>True when another selected upgrade shares this def's <see cref="OutpostUpgradeDef.upgradeLineId"/>.</summary>
        private bool IsSameLineAlreadySelected(OutpostUpgradeDef def)
        {
            if (def == null || batchSelectedDefNames.Count == 0) return false;
            string lineId = string.IsNullOrEmpty(def.upgradeLineId) ? def.defName : def.upgradeLineId;
            if (string.IsNullOrEmpty(lineId)) return false;
            foreach (string selectedName in batchSelectedDefNames)
            {
                if (string.IsNullOrEmpty(selectedName) || selectedName == def.defName) continue;
                OutpostUpgradeDef other = DefDatabase<OutpostUpgradeDef>.GetNamedSilentFail(selectedName);
                if (other == null) continue;
                string otherLine = string.IsNullOrEmpty(other.upgradeLineId) ? other.defName : other.upgradeLineId;
                if (string.Equals(lineId, otherLine, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private List<CachedUpgradeRow> BuildOrderedVisibleRows(WorldObject_WD_Outpost outpost)
        {
            var visible = new List<CachedUpgradeRow>();
            for (int i = 0; i < cachedRows.Count; i++)
            {
                if (RowMatchesFilters(outpost, cachedRows[i]))
                    visible.Add(cachedRows[i]);
            }
            return visible;
        }

        private void EnsureDefaultSelection(WorldObject_WD_Outpost outpost)
        {
            if (!ReferenceEquals(selectionOutpost, outpost))
            {
                selectionOutpost = outpost;
                selectedUpgradeDefName = null;
                batchSelectedDefNames.Clear();
                InvalidateBatchCostBlockCache();
            }

            if (!string.IsNullOrEmpty(selectedUpgradeDefName))
            {
                bool stillVisible = false;
                for (int i = 0; i < cachedRows.Count; i++)
                {
                    if (cachedRows[i].Def?.defName != selectedUpgradeDefName) continue;
                    if (!RowMatchesFilters(outpost, cachedRows[i])) break;
                    stillVisible = true;
                    break;
                }
                if (stillVisible) return;
            }

            if (outpost.HasPendingUpgrade && !string.IsNullOrEmpty(outpost.PendingUpgradeDefName))
            {
                selectedUpgradeDefName = outpost.PendingUpgradeDefName;
                return;
            }

            for (int i = 0; i < cachedRows.Count; i++)
            {
                var row = cachedRows[i];
                if (!RowMatchesFilters(outpost, row)) continue;
                if (row.State.showBuy && row.CanBuy)
                {
                    selectedUpgradeDefName = row.Def.defName;
                    return;
                }
            }

            for (int i = 0; i < cachedRows.Count; i++)
            {
                var row = cachedRows[i];
                if (RowMatchesFilters(outpost, row) && row.Def != null)
                {
                    selectedUpgradeDefName = row.Def.defName;
                    return;
                }
            }

            selectedUpgradeDefName = null;
        }

        private CachedUpgradeRow? FindCachedRow(string defName)
        {
            if (string.IsNullOrEmpty(defName)) return null;
            for (int i = 0; i < cachedRows.Count; i++)
            {
                if (cachedRows[i].Def?.defName == defName)
                    return cachedRows[i];
            }
            return null;
        }

        private bool RowMatchesFilters(WorldObject_WD_Outpost outpost, CachedUpgradeRow row)
        {
            if (!UpgradeMatchesSearch(row, upgradeSearchFilter)) return false;
            if (row.State.superseded) return false;
            if (upgradeImpactFilter.HasValue && !RowAffectsImpact(row, upgradeImpactFilter.Value))
                return false;

            switch (upgradeFilter)
            {
                case UpgradeTabFilter.Built:
                    return row.State.builtHere;
                case UpgradeTabFilter.NotBuilt:
                    return !row.State.builtHere;
                case UpgradeTabFilter.Buildable:
                    return OutpostUpgradeUtility.IsBuildableUpgrade(outpost, row.Def);
                default:
                    return true;
            }
        }

        private void SetFilter(UpgradeTabFilter filter)
        {
            upgradeFilter = filter;
            rightScrollPos = Vector2.zero;
            cachedRowsTick = -1;
        }

        private void SetImpactFilter(UpgradeBenefitKind? kind)
        {
            upgradeImpactFilter = kind;
            rightScrollPos = Vector2.zero;
        }

        private void ClearImpactFilterIfUnavailable()
        {
            if (!upgradeImpactFilter.HasValue) return;
            UpgradeBenefitKind kind = upgradeImpactFilter.Value;
            for (int i = 0; i < cachedRows.Count; i++)
            {
                if (RowAffectsImpact(cachedRows[i], kind))
                    return;
            }
            upgradeImpactFilter = null;
        }

        private List<UpgradeBenefitKind> CollectPresentImpactKinds()
        {
            var present = new HashSet<UpgradeBenefitKind>();
            for (int i = 0; i < cachedRows.Count; i++)
            {
                UpgradeBenefitKind[] kinds = cachedRows[i].ImpactKinds;
                if (kinds == null) continue;
                for (int k = 0; k < kinds.Length; k++)
                    present.Add(kinds[k]);
            }
            return new List<UpgradeBenefitKind>(present);
        }

        private static bool RowAffectsImpact(CachedUpgradeRow row, UpgradeBenefitKind kind)
        {
            UpgradeBenefitKind[] kinds = row.ImpactKinds;
            if (kinds == null) return false;
            for (int i = 0; i < kinds.Length; i++)
            {
                if (kinds[i] == kind) return true;
            }
            return false;
        }

        private static string GetFilterLabel(UpgradeTabFilter filter)
        {
            switch (filter)
            {
                case UpgradeTabFilter.Built:
                    return OutpostTranslationUtil.Key("TSA_WD_OutpostUpgrades_FilterBuilt");
                case UpgradeTabFilter.NotBuilt:
                    return OutpostTranslationUtil.Key("TSA_WD_OutpostUpgrades_FilterNotBuilt");
                case UpgradeTabFilter.Buildable:
                    return OutpostTranslationUtil.Key("TSA_WD_OutpostUpgrades_FilterBuildable");
                default:
                    return OutpostTranslationUtil.Key("TSA_WD_OutpostUpgrades_FilterAll");
            }
        }

        private static string GetImpactFilterButtonLabel(UpgradeBenefitKind? kind)
        {
            string label = !kind.HasValue
                ? OutpostTranslationUtil.Key("TSA_WD_OutpostUpgrades_FilterAllBonuses")
                : Outpost_Upgrade_UI.GetImpactFilterLabel(kind.Value);
            if (label != null && label.Length > 22)
                return label.Substring(0, 19) + "...";
            return label;
        }

        private static int UpgradeRowsFingerprint(WorldObject_WD_Outpost o)
        {
            if (o == null) return 0;
            unchecked
            {
                int h = RuntimeHelpers.GetHashCode(o);
                h = h * 31 + (o.def?.defName?.GetHashCode() ?? 0);
                h = h * 31 + o.Tile;
                if (o.BuiltUpgradeLevels != null)
                    foreach (var kv in o.BuiltUpgradeLevels)
                    {
                        h = h * 31 + (kv.Key?.GetHashCode() ?? 0);
                        h = h * 31 + kv.Value;
                    }
                h = h * 31 + o.PendingUpgradeCount;
                IReadOnlyList<PendingOutpostUpgradeEntry> pending = o.PendingUpgrades;
                if (pending != null)
                {
                    for (int i = 0; i < pending.Count; i++)
                    {
                        PendingOutpostUpgradeEntry e = pending[i];
                        h = h * 31 + (e?.defName?.GetHashCode() ?? 0);
                        h = h * 31 + (e?.level ?? 0);
                    }
                }
                return h;
            }
        }

        private static List<List<OutpostUpgradeDef>> GetOrBuildUpgradeGroupsForDef(string wdef)
        {
            if (string.IsNullOrEmpty(wdef))
                return new List<List<OutpostUpgradeDef>>();

            if (UpgradeGroupsByOutpostDef.TryGetValue(wdef, out List<List<OutpostUpgradeDef>> cached))
                return cached;

            cached = BuildUpgradeGroupsForDef(wdef);
            UpgradeGroupsByOutpostDef[wdef] = cached;
            return cached;
        }

        private List<List<OutpostUpgradeDef>> GetUpgradeGroupsCached(WorldObject_WD_Outpost outpost)
        {
            string wdef = outpost?.def?.defName;
            if (string.IsNullOrEmpty(wdef))
                return new List<List<OutpostUpgradeDef>>();

            if (cachedUpgradeGroups != null && cachedUpgradeGroupsDefName == wdef)
                return cachedUpgradeGroups;

            cachedUpgradeGroups = GetOrBuildUpgradeGroupsForDef(wdef);
            cachedUpgradeGroupsDefName = wdef;
            cachedRowsTick = -1;
            return cachedUpgradeGroups;
        }

        private static List<List<OutpostUpgradeDef>> BuildUpgradeGroupsForDef(string wdef)
        {
            var groups = new List<List<OutpostUpgradeDef>>();
            if (string.IsNullOrEmpty(wdef)) return groups;

            var allDefs = DefDatabase<OutpostUpgradeDef>.AllDefsListForReading;
            var byLine = new Dictionary<string, List<OutpostUpgradeDef>>();
            var lineOrder = new List<string>();
            for (int i = 0; i < allDefs.Count; i++)
            {
                OutpostUpgradeDef d = allDefs[i];
                if (d == null || !d.AppliesToOutpost(wdef)) continue;
                if (!WdMedievalMode.IsUpgradeAllowed(d)) continue;
                string key = d.upgradeLineId ?? d.defName;
                if (!byLine.TryGetValue(key, out List<OutpostUpgradeDef> list))
                {
                    list = new List<OutpostUpgradeDef>();
                    byLine[key] = list;
                    lineOrder.Add(key);
                }
                list.Add(d);
            }

            lineOrder.Sort((a, b) =>
            {
                List<OutpostUpgradeDef> la = byLine[a];
                List<OutpostUpgradeDef> lb = byLine[b];
                int cmp = ((int)la[0].category).CompareTo((int)lb[0].category);
                if (cmp != 0) return cmp;
                int minA = int.MaxValue, minB = int.MaxValue;
                for (int i = 0; i < la.Count; i++) if (la[i].lineTier < minA) minA = la[i].lineTier;
                for (int i = 0; i < lb.Count; i++) if (lb[i].lineTier < minB) minB = lb[i].lineTier;
                cmp = minA.CompareTo(minB);
                if (cmp != 0) return cmp;
                return string.Compare(la[0].LabelCap.Resolve(), lb[0].LabelCap.Resolve(), StringComparison.OrdinalIgnoreCase);
            });

            for (int i = 0; i < lineOrder.Count; i++)
            {
                List<OutpostUpgradeDef> list = byLine[lineOrder[i]];
                list.Sort((a, b) =>
                {
                    int cmp = a.lineTier.CompareTo(b.lineTier);
                    return cmp != 0 ? cmp : string.Compare(a.LabelCap.Resolve(), b.LabelCap.Resolve(), StringComparison.OrdinalIgnoreCase);
                });
                groups.Add(list);
            }
            return groups;
        }

        private static RowState ComputeRowState(WorldObject_WD_Outpost outpost, OutpostUpgradeDef def)
        {
            int hi = OutpostUpgradeUtility.GetHighestBuiltLineTier(outpost, def.upgradeLineId);
            int lvl = outpost.GetUpgradeLevel(def.defName);
            bool builtHere = lvl > 0;
            bool superseded = hi > def.lineTier;
            bool showBuy = !superseded && !builtHere && OutpostUpgradeUtility.TierEligibleForPurchase(def, hi);
            bool deployed = builtHere && hi == def.lineTier;
            bool futureTier = !superseded && !builtHere && !showBuy && def.lineTier > hi;
            bool sequentialBlocked = def.requiresPreviousLineTier && !superseded && !builtHere && hi < def.lineTier - 1;
            return new RowState(builtHere, superseded, showBuy, deployed, futureTier, sequentialBlocked);
        }

        private static bool UpgradeMatchesSearch(CachedUpgradeRow row, string filter)
        {
            string q = filter?.Trim();
            if (string.IsNullOrEmpty(q)) return true;
            OutpostUpgradeDef def = row.Def;
            if (TokenMatches(def?.LabelCap.Resolve(), q)
                || TokenMatches(def?.label, q)
                || TokenMatches(def?.defName, q)
                || TokenMatches(row.ImpactSummary, q))
                return true;
            UpgradeBenefitKind[] kinds = row.ImpactKinds;
            if (kinds == null) return false;
            for (int i = 0; i < kinds.Length; i++)
            {
                if (TokenMatches(Outpost_Upgrade_UI.GetImpactFilterLabel(kinds[i]), q))
                    return true;
            }
            return false;
        }

        private static bool TokenMatches(string haystack, string needle)
        {
            return !haystack.NullOrEmpty()
                && !needle.NullOrEmpty()
                && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void LabelAnchored(Rect rect, string text, TextAnchor anchor)
        {
            TextAnchor prev = Text.Anchor;
            Text.Anchor = anchor;
            Widgets.Label(rect, text);
            Text.Anchor = prev;
        }
    }

    public static class OutpostUpgradeUtility
    {
        /// <summary>The single player colony map (we assume one player home map).</summary>
        private static Map GetColonyMap() => ColonyWorldBuildMaterials.GetColonyMap();

        /// <summary>All player warehouse outposts. Colony map plus every warehouse count toward upgrade payment.</summary>
        private static List<WorldObject_WD_Outpost> GetContributingWarehouses(int outpostTile)
        {
            _ = outpostTile;
            return ColonyWorldBuildMaterials.GetContributingWarehouses();
        }

        /// <summary>Origin for the symbolic upgrade caravan: nearest warehouse or colony, whichever is closer to the upgrading outpost.</summary>
        private static WorldObject ResolveUpgradeOrigin(
            WorldObject_WD_Outpost outpost,
            Map colonyMap,
            List<WorldObject_WD_Outpost> warehouses)
        {
            if (outpost == null || Find.WorldGrid == null)
                return colonyMap?.Parent;

            WorldObject nearestWh = FindNearestWarehouse(outpost.Tile, warehouses);
            WorldObject colony = colonyMap?.Parent;
            if (nearestWh == null) return colony;
            if (colony == null) return nearestWh;

            float dWh = Find.WorldGrid.ApproxDistanceInTiles(outpost.Tile, nearestWh.Tile);
            float dCol = Find.WorldGrid.ApproxDistanceInTiles(outpost.Tile, colony.Tile);
            return dWh <= dCol ? nearestWh : colony;
        }

        private static WorldObject_WD_Outpost FindNearestWarehouse(int outpostTile, List<WorldObject_WD_Outpost> warehouses)
        {
            if (warehouses == null || warehouses.Count == 0 || Find.WorldGrid == null) return null;
            WorldObject_WD_Outpost nearest = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < warehouses.Count; i++)
            {
                if (warehouses[i] == null) continue;
                // Do not ship from the outpost that is itself being upgraded.
                if (warehouses[i].Tile == outpostTile) continue;
                float d = Find.WorldGrid.ApproxDistanceInTiles(outpostTile, warehouses[i].Tile);
                if (d < bestDist) { bestDist = d; nearest = warehouses[i]; }
            }
            return nearest;
        }

        public struct PurchaseCheck
        {
            public bool canBuy;
            public string reason;
            public Dictionary<string, bool> costAvailableByDefName;
        }

        public static int GetHighestBuiltLineTier(WorldObject_WD_Outpost outpost, string upgradeLineId)
        {
            if (outpost == null || string.IsNullOrEmpty(upgradeLineId)) return 0;
            int best = 0;
            foreach (var kv in outpost.BuiltUpgradeLevels)
            {
                if (kv.Value <= 0) continue;
                var d = DefDatabase<OutpostUpgradeDef>.GetNamedSilentFail(kv.Key);
                if (d == null || d.upgradeLineId != upgradeLineId) continue;
                if (d.lineTier > best)
                    best = d.lineTier;
            }
            return best;
        }

        public static bool TierEligibleForPurchase(OutpostUpgradeDef def, int highestBuiltLineTier)
        {
            if (def == null) return false;
            if (def.requiresPreviousLineTier)
                return def.lineTier == highestBuiltLineTier + 1;
            return def.lineTier > highestBuiltLineTier;
        }

        public static IEnumerable<string> GetResearchRequirements(OutpostUpgradeDef upgrade)
        {
            if (upgrade == null) yield break;
            var seen = new HashSet<string>();
            foreach (var r in upgrade.GetEffectiveResearchRequirements())
            {
                if (string.IsNullOrEmpty(r) || seen.Contains(r)) continue;
                seen.Add(r);
                yield return r;
            }
        }

        public static int CountResearchRequirements(OutpostUpgradeDef upgrade)
        {
            if (upgrade == null) return 0;
            var seen = new HashSet<string>();
            int n = 0;
            foreach (var r in upgrade.GetEffectiveResearchRequirements())
            {
                if (string.IsNullOrEmpty(r) || seen.Contains(r)) continue;
                seen.Add(r);
                n++;
            }
            return n;
        }

        public static PurchaseCheck EvaluatePurchase(WorldObject_WD_Outpost outpost, OutpostUpgradeDef upgrade, int level, bool ignoreLineTierForPreview = false)
        {
            var result = new PurchaseCheck
            {
                canBuy = false,
                reason = null,
                costAvailableByDefName = new Dictionary<string, bool>()
            };

            if (outpost == null || upgrade == null) { result.reason = "TSA_WD_OutpostUpgrades_Invalid".Translate(); return result; }
            if (level <= 0) { result.reason = "TSA_WD_OutpostUpgrades_Invalid".Translate(); return result; }
            if (!WdMedievalMode.IsUpgradeAllowed(upgrade))
            {
                result.reason = WdMedievalMode.MedievalBlockedReason;
                return result;
            }
            if (outpost.HasPendingUpgrade) { result.reason = "TSA_WD_OutpostUpgrades_InTransit".Translate(); return result; }
            if (!upgrade.AppliesToOutpost(outpost.def.defName)) { result.reason = "TSA_WD_OutpostUpgrades_Invalid".Translate(); return result; }
            if (outpost.GetUpgradeLevel(upgrade.defName) > 0)
            {
                result.reason = "TSA_WD_OutpostUpgrades_AlreadyOwned".Translate();
                return result;
            }
            if (!ignoreLineTierForPreview)
            {
                int hi = GetHighestBuiltLineTier(outpost, upgrade.upgradeLineId);
                if (upgrade.requiresPreviousLineTier)
                {
                    if (upgrade.lineTier != hi + 1)
                    {
                        result.reason = "TSA_WD_OutpostUpgrades_NotNextTier".Translate();
                        return result;
                    }
                }
                else if (upgrade.lineTier <= hi)
                {
                    result.reason = "TSA_WD_OutpostUpgrades_AlreadyHaveLineTier".Translate();
                    return result;
                }
            }

            foreach (var req in GetResearchRequirements(upgrade))
            {
                var rp = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(req);
                if (rp != null && (Find.ResearchManager == null || Find.ResearchManager.GetProgress(rp) < rp.baseCost))
                {
                    result.reason = "TSA_WD_OutpostUpgrades_MissingResearch".Translate(rp.LabelCap);
                    return result;
                }
            }

            Map colonyMap = GetColonyMap();
            var warehouses = GetContributingWarehouses(outpost.Tile);
            if (colonyMap == null && warehouses.Count == 0) { result.reason = "TSA_WD_OutpostUpgrades_NoColonyMap".Translate(); return result; }
            List<OutpostUpgradeCostEntry> effectiveCost = upgrade.GetEffectiveCost();
            if (!HasCost(colonyMap, warehouses, effectiveCost, out result.reason, result.costAvailableByDefName)) return result;

            WorldObject origin = ResolveUpgradeOrigin(outpost, colonyMap, warehouses);
            if (!ColonyWorldBuildMaterials.WouldHavePodComponentsAfterCost(
                    origin,
                    ColonyWorldBuildMaterials.CountIndustrialComponentsInUpgradeCost(effectiveCost),
                    "TSA_WD_OutpostUpgrades_DropPodShortAfterBuy",
                    out result.reason))
                return result;

            result.canBuy = !ignoreLineTierForPreview;
            return result;
        }

        public class BatchPurchaseCheck
        {
            public bool canBuy;
            public string reason;
            public List<OutpostUpgradeDef> accepted = new List<OutpostUpgradeDef>();
            public List<string> droppedSameLineLabels = new List<string>();
            public string sameLineDroppedNote;
            public List<OutpostUpgradeCostEntry> aggregatedCost = new List<OutpostUpgradeCostEntry>();
            public Dictionary<string, bool> costAvailableByDefName = new Dictionary<string, bool>();
            public WorldObject origin;
            public int launchCount;
            public bool originUsesDropPod;
        }

        /// <summary>
        /// Prepaid multi-select validation: same-line keeps highest tier only, then sequential sim from built hi,
        /// one cumulative stock check, and pods for N launches.
        /// </summary>
        public static BatchPurchaseCheck EvaluateBatchPurchase(
            WorldObject_WD_Outpost outpost,
            IList<OutpostUpgradeDef> selectedDefs)
        {
            var result = new BatchPurchaseCheck();
            if (outpost == null || selectedDefs == null || selectedDefs.Count == 0)
            {
                result.reason = "TSA_WD_OutpostUpgrades_Invalid".Translate();
                return result;
            }
            if (outpost.HasPendingUpgrade)
            {
                result.reason = "TSA_WD_OutpostUpgrades_InTransit".Translate();
                return result;
            }

            // Same upgradeLineId: keep highest lineTier only.
            var byLine = new Dictionary<string, OutpostUpgradeDef>();
            var noLine = new List<OutpostUpgradeDef>();
            for (int i = 0; i < selectedDefs.Count; i++)
            {
                OutpostUpgradeDef def = selectedDefs[i];
                if (def == null) continue;
                if (string.IsNullOrEmpty(def.upgradeLineId))
                {
                    if (!noLine.Exists(d => d != null && d.defName == def.defName))
                        noLine.Add(def);
                    continue;
                }
                if (!byLine.TryGetValue(def.upgradeLineId, out OutpostUpgradeDef existing) || def.lineTier > existing.lineTier)
                {
                    if (existing != null && existing.defName != def.defName)
                        result.droppedSameLineLabels.Add(existing.LabelCap);
                    byLine[def.upgradeLineId] = def;
                }
                else if (existing != null && existing.defName != def.defName)
                {
                    result.droppedSameLineLabels.Add(def.LabelCap);
                }
            }

            var candidates = new List<OutpostUpgradeDef>(byLine.Count + noLine.Count);
            foreach (var kv in byLine)
                candidates.Add(kv.Value);
            candidates.AddRange(noLine);
            candidates.Sort((a, b) =>
            {
                int c = string.CompareOrdinal(a.upgradeLineId ?? "", b.upgradeLineId ?? "");
                if (c != 0) return c;
                return a.lineTier.CompareTo(b.lineTier);
            });

            if (result.droppedSameLineLabels.Count > 0)
            {
                result.sameLineDroppedNote = "TSA_WD_OutpostUpgrades_BatchSameLineDropped".Translate(
                    string.Join(", ", result.droppedSameLineLabels)).ToString();
            }

            var simHi = new Dictionary<string, int>();
            for (int i = 0; i < candidates.Count; i++)
            {
                OutpostUpgradeDef upgrade = candidates[i];
                if (!WdMedievalMode.IsUpgradeAllowed(upgrade))
                {
                    result.reason = WdMedievalMode.MedievalBlockedReason;
                    return result;
                }
                if (!upgrade.AppliesToOutpost(outpost.def.defName))
                {
                    result.reason = "TSA_WD_OutpostUpgrades_Invalid".Translate();
                    return result;
                }
                if (outpost.GetUpgradeLevel(upgrade.defName) > 0)
                {
                    result.reason = "TSA_WD_OutpostUpgrades_AlreadyOwned".Translate();
                    return result;
                }

                int hi = 0;
                if (!string.IsNullOrEmpty(upgrade.upgradeLineId))
                {
                    if (!simHi.TryGetValue(upgrade.upgradeLineId, out hi))
                    {
                        hi = GetHighestBuiltLineTier(outpost, upgrade.upgradeLineId);
                        simHi[upgrade.upgradeLineId] = hi;
                    }
                    if (!TierEligibleForPurchase(upgrade, hi))
                    {
                        result.reason = upgrade.requiresPreviousLineTier
                            ? "TSA_WD_OutpostUpgrades_NotNextTier".Translate()
                            : "TSA_WD_OutpostUpgrades_AlreadyHaveLineTier".Translate();
                        return result;
                    }
                }

                foreach (var req in GetResearchRequirements(upgrade))
                {
                    var rp = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(req);
                    if (rp != null && (Find.ResearchManager == null || Find.ResearchManager.GetProgress(rp) < rp.baseCost))
                    {
                        result.reason = "TSA_WD_OutpostUpgrades_MissingResearch".Translate(rp.LabelCap);
                        return result;
                    }
                }

                result.accepted.Add(upgrade);
                if (!string.IsNullOrEmpty(upgrade.upgradeLineId))
                    simHi[upgrade.upgradeLineId] = upgrade.lineTier;
                MergeCostsInto(result.aggregatedCost, upgrade.GetEffectiveCost());
            }

            if (result.accepted.Count == 0)
            {
                result.reason = "TSA_WD_OutpostUpgrades_Invalid".Translate();
                return result;
            }

            Map colonyMap = GetColonyMap();
            var warehouses = GetContributingWarehouses(outpost.Tile);
            if (colonyMap == null && warehouses.Count == 0)
            {
                result.reason = "TSA_WD_OutpostUpgrades_NoColonyMap".Translate();
                return result;
            }
            if (!HasCost(colonyMap, warehouses, result.aggregatedCost, out result.reason, result.costAvailableByDefName))
                return result;

            result.origin = ResolveUpgradeOrigin(outpost, colonyMap, warehouses);
            // One traveler carries the whole batch: one strength pool, one drop-pod launch.
            result.launchCount = 1;
            result.originUsesDropPod = result.origin != null
                && OutpostDispatchMode.GetViaDropPod(result.origin)
                && RapidResponseUtility.TransportPodsResearched();
            if (!ColonyWorldBuildMaterials.WouldHavePodComponentsAfterCost(
                    result.origin,
                    ColonyWorldBuildMaterials.CountIndustrialComponentsInUpgradeCost(result.aggregatedCost),
                    "TSA_WD_OutpostUpgrades_DropPodShortAfterBuy",
                    out result.reason,
                    result.originUsesDropPod ? result.launchCount : 0))
                return result;

            result.canBuy = true;
            return result;
        }

        private static void MergeCostsInto(List<OutpostUpgradeCostEntry> dest, List<OutpostUpgradeCostEntry> src)
        {
            if (dest == null || src == null) return;
            for (int i = 0; i < src.Count; i++)
            {
                OutpostUpgradeCostEntry c = src[i];
                if (c == null || c.count <= 0) continue;
                bool isStone = ColonyWorldBuildMaterials.IsAnyStoneBlocksCost(c);
                if (!isStone && c.thingDef == null) continue;

                OutpostUpgradeCostEntry match = null;
                for (int j = 0; j < dest.Count; j++)
                {
                    OutpostUpgradeCostEntry d = dest[j];
                    if (d == null) continue;
                    bool dStone = ColonyWorldBuildMaterials.IsAnyStoneBlocksCost(d);
                    if (isStone && dStone) { match = d; break; }
                    if (!isStone && !dStone && d.thingDef == c.thingDef) { match = d; break; }
                }
                if (match != null)
                    match.count += c.count;
                else
                {
                    dest.Add(new OutpostUpgradeCostEntry
                    {
                        thingDef = c.thingDef,
                        count = c.count,
                        costMode = c.costMode
                    });
                }
            }
        }

        public static bool TryPurchaseBatchAndLaunch(
            WorldObject_WD_Outpost outpost,
            BatchPurchaseCheck batch,
            out string reason)
        {
            reason = null;
            if (outpost == null || batch == null || !batch.canBuy || batch.accepted == null || batch.accepted.Count == 0)
            {
                reason = batch?.reason ?? "TSA_WD_OutpostUpgrades_Invalid".Translate();
                return false;
            }
            // Re-validate against live state (window can stay open).
            var live = EvaluateBatchPurchase(outpost, batch.accepted);
            if (!live.canBuy)
            {
                reason = live.reason;
                return false;
            }

            Map colonyMap = GetColonyMap();
            var warehouses = GetContributingWarehouses(outpost.Tile);
            if (!DeductCost(colonyMap, warehouses, live.aggregatedCost, out reason, out _))
                return false;

            WorldObject origin = live.origin;
            var queued = new List<OutpostUpgradeDef>();
            const int level = 1;
            for (int i = 0; i < live.accepted.Count; i++)
            {
                OutpostUpgradeDef upgrade = live.accepted[i];
                if (upgrade == null) continue;
                if (!outpost.TryQueuePendingUpgrade(upgrade.defName, level))
                {
                    ClearQueuedPendingAndRefund(outpost, queued, live.aggregatedCost);
                    reason = "TSA_WD_OutpostUpgrades_QueueFailed".Translate();
                    return false;
                }
                queued.Add(upgrade);
            }

            if (queued.Count == 0)
            {
                ColonyWorldBuildMaterials.TryRefundMaterialCosts(live.aggregatedCost);
                reason = "TSA_WD_OutpostUpgrades_Invalid".Translate();
                return false;
            }

            if (origin == null || !WorldActions_Traveler.SpawnOutpostUpgradeTraveler(outpost, origin, queued, level))
            {
                ClearQueuedPendingAndRefund(outpost, queued, live.aggregatedCost);
                reason = "TSA_WD_OutpostUpgrades_LaunchFailed".Translate();
                return false;
            }
            return true;
        }

        private static void ClearQueuedPendingAndRefund(
            WorldObject_WD_Outpost outpost,
            List<OutpostUpgradeDef> queued,
            List<OutpostUpgradeCostEntry> fullCost)
        {
            if (queued != null)
            {
                for (int i = 0; i < queued.Count; i++)
                {
                    OutpostUpgradeDef def = queued[i];
                    if (def == null) continue;
                    outpost.ClearPendingUpgradeIfMatches(def.defName, 1);
                }
            }
            ColonyWorldBuildMaterials.TryRefundMaterialCosts(fullCost);
        }

        public static bool CanPurchaseUpgrade(WorldObject_WD_Outpost outpost, OutpostUpgradeDef upgrade, int level, out string reason)
        {
            var check = EvaluatePurchase(outpost, upgrade, level);
            reason = check.reason;
            return check.canBuy;
        }

        /// <summary>True when tier, research, materials, and queue state all allow purchase right now.</summary>
        public static bool IsBuildableUpgrade(WorldObject_WD_Outpost outpost, OutpostUpgradeDef upgrade)
            => CanPurchaseUpgrade(outpost, upgrade, 1, out _);

        public static bool MaterialsAvailableForUpgrade(WorldObject_WD_Outpost outpost, OutpostUpgradeDef upgrade)
        {
            if (outpost == null || upgrade == null) return false;
            List<OutpostUpgradeCostEntry> effectiveCost = upgrade.GetEffectiveCost();
            if (effectiveCost == null || effectiveCost.Count == 0) return true;
            Map colonyMap = GetColonyMap();
            var warehouses = GetContributingWarehouses(outpost.Tile);
            if (colonyMap == null && warehouses.Count == 0) return false;
            var availability = new Dictionary<string, bool>();
            return HasCost(colonyMap, warehouses, effectiveCost, out _, availability);
        }

        public static bool TryPurchaseAndLaunch(WorldObject_WD_Outpost outpost, OutpostUpgradeDef upgrade, int level, out string reason)
        {
            reason = null;
            if (!CanPurchaseUpgrade(outpost, upgrade, level, out reason)) return false;
            Map colonyMap = GetColonyMap();
            var warehouses = GetContributingWarehouses(outpost.Tile);
            if (colonyMap == null && warehouses.Count == 0) { reason = "TSA_WD_OutpostUpgrades_NoColonyMap".Translate(); return false; }

            List<OutpostUpgradeCostEntry> effectiveCost = upgrade.GetEffectiveCost();
            WorldObject origin = ResolveUpgradeOrigin(outpost, colonyMap, warehouses);
            if (!ColonyWorldBuildMaterials.WouldHavePodComponentsAfterCost(
                    origin,
                    ColonyWorldBuildMaterials.CountIndustrialComponentsInUpgradeCost(effectiveCost),
                    "TSA_WD_OutpostUpgrades_DropPodShortAfterBuy",
                    out reason))
                return false;

            if (!DeductCost(colonyMap, warehouses, effectiveCost, out reason, out _)) return false;
            if (!outpost.TryQueuePendingUpgrade(upgrade.defName, level))
            {
                ColonyWorldBuildMaterials.TryRefundMaterialCosts(effectiveCost);
                reason = "TSA_WD_OutpostUpgrades_QueueFailed".Translate();
                return false;
            }
            if (origin == null || !WorldActions_Traveler.SpawnOutpostUpgradeTraveler(outpost, origin, upgrade.defName, level))
            {
                outpost.ClearPendingUpgradeIfMatches(upgrade.defName, level);
                ColonyWorldBuildMaterials.TryRefundMaterialCosts(effectiveCost);
                reason = "TSA_WD_OutpostUpgrades_LaunchFailed".Translate();
                return false;
            }
            return true;
        }

        public static bool IsAnyStoneBlocksCost(OutpostUpgradeCostEntry c) =>
            ColonyWorldBuildMaterials.IsAnyStoneBlocksCost(c);

        public static string GetCostDisplayLabel(OutpostUpgradeCostEntry c) =>
            ColonyWorldBuildMaterials.GetCostDisplayLabel(c);

        private static bool HasCost(
            Map map,
            List<WorldObject_WD_Outpost> warehouses,
            List<OutpostUpgradeCostEntry> cost,
            out string reason,
            Dictionary<string, bool> availabilityByDefName = null) =>
            ColonyWorldBuildMaterials.HasCost(map, warehouses, cost, out reason, availabilityByDefName);

        private static bool DeductCost(
            Map map,
            List<WorldObject_WD_Outpost> warehouses,
            List<OutpostUpgradeCostEntry> cost,
            out string reason,
            out HashSet<WorldObject_WD_Outpost> warehousesThatContributed) =>
            ColonyWorldBuildMaterials.DeductCost(map, warehouses, cost, out reason, out warehousesThatContributed);
    }
}
