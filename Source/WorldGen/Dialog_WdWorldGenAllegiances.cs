using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Unified allegiance editor: faction WD participation, diplomacy/goodwill, and NPC×NPC allegiance locks.
    /// </summary>
    public class Dialog_WdWorldGenAllegiances : Window
    {
        private static readonly Color NavSlateFill = new Color(0.16f, 0.18f, 0.22f, 0.92f);
        private static readonly Color NavBtnBgHover = new Color(0.22f, 0.26f, 0.32f, 0.96f);
        private static readonly Color NavBtnBgPress = new Color(0.12f, 0.14f, 0.17f, 0.96f);
        private static readonly Color NavBtnBgSelected = new Color(0.22f, 0.26f, 0.32f, 0.96f);
        private static readonly Color NavBtnOutline = new Color(0.55f, 0.62f, 0.72f, 0.42f);
        private static readonly Color NavBtnOutlineHover = new Color(0.78f, 0.84f, 0.92f, 0.72f);
        private static readonly Color NavBtnOutlineSelected = new Color(0.70f, 0.76f, 0.86f, 0.55f);

        private const float FactionChipW = 150f;
        private const float RelBtnW = 78f;
        private const float GoodwillW = 70f;
        private const float LockBtnW = 140f;
        private const float RowPadLeft = 8f;
        private const float ChipGap = 6f;
        private const float ArrowSlotW = 28f;
        private const float AfterSecondChipGap = 16f;
        private const float RelBtnGap = 4f;
        private const float AfterRelBtnsGap = 10f;
        private const float AfterGoodwillGap = 10f;
        private const float ScrollBarReserve = 24f;
        private const float ScrollViewInset = 4f;
        private const float WindowMarginX = 18f;
        private const float SectionHeaderH = 30f;
        private const float SectionToTableGap = 1f;
        private const float TableHeaderH = 28f;
        private const float RosterRowH = 36f;
        private const float RosterBodyMinH = 240f;
        private const float RosterBlockPad = 8f;
        private const float RosterColWd = 68f;
        private const float RosterColMapGen = 58f;
        private const float RosterColStoryteller = 72f;
        private const float RosterSeparatorW = 1f;
        private const int RosterColumnsPerRow = 2;
        private const float PageBtnH = 24f;
        private const float PageBtnGap = 4f;
        private const float ResetSectionBtnW = 96f;
        private const int MinGoodwill = -100;
        private const GameFont PageBtnFont = GameFont.Tiny;

        private static float RowContentWidth =>
            RowPadLeft
            + FactionChipW + ChipGap
            + ArrowSlotW
            + FactionChipW + AfterSecondChipGap
            + RelBtnW * 3f + RelBtnGap * 2f + AfterRelBtnsGap
            + GoodwillW + AfterGoodwillGap
            + LockBtnW;

        private static float PreferredInRectWidth =>
            RowContentWidth + ScrollViewInset * 2f + ScrollBarReserve;

        private Vector2 rosterScrollPosition = Vector2.zero;
        private Vector2 diplomacyScrollPosition = Vector2.zero;
        private string searchTerm = "";
        private string lastAppliedFilter;
        private readonly List<Faction> factionRoster = new List<Faction>();
        private readonly List<Pair<Faction, Faction>> factionPairs = new List<Pair<Faction, Faction>>();
        private readonly List<Pair<Faction, Faction>> filteredPairsCache = new List<Pair<Faction, Faction>>();
        private readonly Dictionary<string, string> goodwillEditBuffers = new Dictionary<string, string>();
        private static bool factionsControlledExpanded = true;
        private static bool diplomaticPairsExpanded = true;
        private static string s_filterPlaceholder;

        private static bool IsLiveEdit => Current.ProgramState == ProgramState.Playing;

        private static float ContentRightEdge(Rect area) =>
            area.xMax - ScrollBarReserve - ScrollViewInset;

        private static float ContentWidth(Rect area) =>
            ContentRightEdge(area) - area.x - ScrollViewInset;

        private static Rect ResetSectionButtonRect(Rect row) =>
            new Rect(
                ContentRightEdge(row) - ResetSectionBtnW,
                row.y + (row.height - PageBtnH) * 0.5f,
                ResetSectionBtnW,
                PageBtnH);

        private readonly struct RosterLayout
        {
            public readonly float ContentWidth;
            public readonly float BlockWidth;
            public readonly float ColFaction;

            public RosterLayout(float contentWidth)
            {
                ContentWidth = contentWidth;
                float sepTotal = (RosterColumnsPerRow - 1) * RosterSeparatorW;
                BlockWidth = (contentWidth - sepTotal) / RosterColumnsPerRow;
                ColFaction = Mathf.Max(72f, BlockWidth - RosterBlockPad - RosterColWd - RosterColMapGen - RosterColStoryteller);
            }

            public float BlockX(int column) =>
                column * (BlockWidth + RosterSeparatorW);

            public float SeparatorX(int column) =>
                BlockX(column);
        }

        public override Vector2 InitialSize =>
            new Vector2(PreferredInRectWidth + WindowMarginX * 2f, 820f);

        public Dialog_WdWorldGenAllegiances()
        {
            doCloseButton = true;
            doCloseX = true;
            absorbInputAroundWindow = true;
            forcePause = IsLiveEdit;
            closeOnClickedOutside = true;
            optionalTitle = null;
            if (s_filterPlaceholder == null)
                s_filterPlaceholder = "TSA_WD_FilterByName".Translate();

            WorldDominationMod.settings?.EnsureInitialLaunchDefaults();
            RefreshFactionData();
        }

        private void RefreshFactionData()
        {
            factionRoster.Clear();
            factionPairs.Clear();

            var allFactions = Find.FactionManager.AllFactionsVisible
                .Where(f => f != null)
                .OrderBy(f => f.IsPlayer ? 0 : 1)
                .ThenBy(f => f.def.LabelCap.Resolve())
                .ToList();

            for (int i = 0; i < allFactions.Count; i++)
            {
                Faction f = allFactions[i];
                if (!f.IsPlayer)
                    factionRoster.Add(f);
            }

            var pairPool = allFactions
                .Where(f => f.IsPlayer || !WorldActions_Utils.IsAutoExcludedFromWd(f))
                .ToList();

            for (int i = 0; i < pairPool.Count; i++)
            {
                for (int j = i + 1; j < pairPool.Count; j++)
                    factionPairs.Add(new Pair<Faction, Faction>(pairPool[i], pairPool[j]));
            }

            factionPairs.Sort((a, b) =>
            {
                int scoreA = InvolvesPlayer(a) ? 0 : 1;
                int scoreB = InvolvesPlayer(b) ? 0 : 1;
                if (scoreA != scoreB) return scoreA.CompareTo(scoreB);
                return 0;
            });

            lastAppliedFilter = null;
            goodwillEditBuffers.Clear();
        }

        private static bool InvolvesPlayer(Pair<Faction, Faction> p) =>
            p.First != null && p.Second != null && (p.First.IsPlayer || p.Second.IsPlayer);

        private static bool IsNpcNpcPair(Pair<Faction, Faction> p) =>
            p.First != null && p.Second != null && !p.First.IsPlayer && !p.Second.IsPlayer;

        private static bool IsPermanentHostileVsPlayer(Pair<Faction, Faction> p)
        {
            if (!InvolvesPlayer(p)) return false;
            Faction npc = p.First.IsPlayer ? p.Second : p.First;
            return WorldActions_Utils.IsPermanentEnemyOfPlayer(npc);
        }

        private static bool FactionMatchesFilter(Faction f, string searchLower)
        {
            if (f == null) return false;
            if (!string.IsNullOrEmpty(f.Name) && f.Name.ToLowerInvariant().Contains(searchLower))
                return true;
            string label = f.def?.LabelCap.Resolve();
            return !string.IsNullOrEmpty(label) && label.ToLowerInvariant().Contains(searchLower);
        }

        public override void DoWindowContents(Rect inRect)
        {
            float y = inRect.y;
            var s = WorldDominationMod.settings;
            if (s == null) return;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, Outpost_Dialog_UI.DialogTitleHeight),
                "TSA_WD_WorldSetup_AllegiancesTitle".Translate());
            y += Outpost_Dialog_UI.DialogTitleRowAdvance;

            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.75f, 0.75f, 0.75f);
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 24f),
                "TSA_WD_WorldSetup_AllegiancesSubtitleNoFreeze".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            y += 28f;

            if (IsLiveEdit)
                y = Outpost_Dialog_UI.DrawWarningBanner(inRect.x, y, inRect.width,
                    "TSA_WD_Allegiances_LiveEditWarning".Translate(), severe: true);

            y = DrawFilterRow(inRect, y) + 8f;
            RebuildFilteredPairsIfNeeded();

            float bottomReserve = 44f;
            float remainingH = inRect.yMax - y - bottomReserve;

            if (DrawFactionsSectionHeader(new Rect(inRect.x, y, inRect.width, SectionHeaderH), s))
            {
                y += SectionHeaderH + SectionToTableGap;
                float rosterH = Mathf.Min(RosterBodyMinH, Mathf.Max(RosterBodyMinH, remainingH * 0.42f));
                Rect rosterOut = new Rect(inRect.x, y, inRect.width, rosterH);
                DrawFactionRosterTable(rosterOut, s);
                y += rosterH + 6f;
                remainingH = inRect.yMax - y - bottomReserve;
            }
            else
                y += SectionHeaderH + SectionToTableGap;

            if (DrawDiplomaticPairsSectionHeader(new Rect(inRect.x, y, inRect.width, SectionHeaderH), s))
            {
                y += SectionHeaderH + SectionToTableGap;
                float diploH = Mathf.Max(120f, remainingH - SectionHeaderH);
                Rect diploOut = new Rect(inRect.x, y, inRect.width, diploH);
                DrawDiplomacyTable(diploOut, s);
            }
            else
                y += SectionHeaderH + SectionToTableGap;
        }

        private float DrawFilterRow(Rect inRect, float y)
        {
            var s = WorldDominationMod.settings;
            Rect searchRect = new Rect(inRect.x, y, Mathf.Min(320f, inRect.width - 72f), 28f);
            searchTerm = Widgets.TextField(searchRect, searchTerm);
            if (string.IsNullOrEmpty(searchTerm))
            {
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = new Color(1f, 1f, 1f, 0.4f);
                Widgets.Label(searchRect.ContractedBy(4f, 0f), s_filterPlaceholder);
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
            }

            Rect clearBtnRect = new Rect(searchRect.xMax + 5f, y, 60f, PageBtnH);
            Text.Font = PageBtnFont;
            if (Widgets.ButtonText(clearBtnRect, "TSA_WD_BtnClear".Translate()))
                searchTerm = "";
            Text.Font = GameFont.Small;

            if (s != null)
            {
                float modeBtnW = Mathf.Min(360f, inRect.xMax - clearBtnRect.xMax - 12f);
                if (modeBtnW > 120f)
                {
                    Rect modeRect = new Rect(clearBtnRect.xMax + 8f, y, modeBtnW, PageBtnH);
                    string modeLabel = Patch_RaidEnemy_AdjustPoints.StorytellerBlockedRaidModeLabel(s.storytellerBlockedRaidMode);
                    TooltipHandler.TipRegion(modeRect, SettingsUI.TooltipWithDefault(
                        "TSA_WD_StorytellerBlockedRaid_Tooltip".Translate(),
                        Patch_RaidEnemy_AdjustPoints.StorytellerBlockedRaidModeLabel(
                            WorldDominationSettings.DefStorytellerBlockedRaidMode)));
                    if (Widgets.ButtonText(modeRect, modeLabel))
                    {
                        var opts = new List<FloatMenuOption>
                        {
                            new FloatMenuOption(
                                Patch_RaidEnemy_AdjustPoints.StorytellerBlockedRaidModeLabel(WdStorytellerBlockedRaidMode.DropInvalid),
                                () => s.storytellerBlockedRaidMode = WdStorytellerBlockedRaidMode.DropInvalid),
                            new FloatMenuOption(
                                Patch_RaidEnemy_AdjustPoints.StorytellerBlockedRaidModeLabel(WdStorytellerBlockedRaidMode.SwapToValid),
                                () => s.storytellerBlockedRaidMode = WdStorytellerBlockedRaidMode.SwapToValid)
                        };
                        Find.WindowStack.Add(new FloatMenu(opts));
                    }
                }
            }

            return y + 32f;
        }

        private void RebuildFilteredPairsIfNeeded()
        {
            if (lastAppliedFilter == searchTerm) return;
            lastAppliedFilter = searchTerm;
            filteredPairsCache.Clear();
            string searchLower = string.IsNullOrEmpty(searchTerm) ? null : searchTerm.ToLowerInvariant();
            for (int i = 0; i < factionPairs.Count; i++)
            {
                var p = factionPairs[i];
                if (searchLower == null
                    || FactionMatchesFilter(p.First, searchLower)
                    || FactionMatchesFilter(p.Second, searchLower))
                    filteredPairsCache.Add(p);
            }
        }

        private bool DrawFactionsSectionHeader(Rect row, WorldDominationSettings s)
        {
            Rect resetRect = ResetSectionButtonRect(row);
            Rect titleRect = new Rect(row.x, row.y, resetRect.x - row.x - 6f, row.height);

            DrawCollapsibleHeader(titleRect, ref factionsControlledExpanded,
                "TSA_WD_Allegiances_SectionParticipation",
                "TSA_WD_Allegiances_SectionParticipation_Tooltip");

            if (DrawHeaderButton(resetRect, "TSA_WD_BtnResetSection".Translate(),
                "TSA_WD_BtnResetFactionParticipation_Tooltip".Translate(),
                () => s.ResetManualFactionParticipation()))
                SoundDefOf.Tick_High.PlayOneShotOnCamera();

            return factionsControlledExpanded;
        }

        private bool DrawDiplomaticPairsSectionHeader(Rect row, WorldDominationSettings s)
        {
            Rect resetRect = ResetSectionButtonRect(row);
            float buttonsLeft = resetRect.x - DiplomacyHeaderButtonsWidth();
            Rect titleRect = new Rect(row.x, row.y, buttonsLeft - row.x - 6f, row.height);

            DrawCollapsibleHeader(titleRect, ref diplomaticPairsExpanded,
                "TSA_WD_Allegiances_SectionDiplomacy",
                "TSA_WD_Allegiances_SectionDiplomacy_Tooltip");

            DrawDiplomacyHeaderButtons(s, resetRect);
            return diplomaticPairsExpanded;
        }

        private static float DiplomacyHeaderButtonsWidth()
        {
            float w = ResetSectionBtnW;
            w += PageBtnGap + MeasureHeaderBtnW("TSA_WD_BtnResetGlobal".Translate());
            w += PageBtnGap + MeasureHeaderBtnW("TSA_WD_BtnReset".Translate());
            w += PageBtnGap + MeasureHeaderBtnW("TSA_WD_BtnAllowAll".Translate());
            w += PageBtnGap + MeasureHeaderBtnW("TSA_WD_BtnLockAll".Translate());
            return w;
        }

        private void DrawDiplomacyHeaderButtons(WorldDominationSettings s, Rect resetRect)
        {
            Text.Font = PageBtnFont;
            float btnH = PageBtnH;
            float y = resetRect.y;
            float x = resetRect.x;

            if (DrawHeaderButton(resetRect, "TSA_WD_BtnResetSection".Translate(),
                "TSA_WD_WorldSetup_ResetAllegiancesDefaultTooltip".Translate(),
                () =>
                {
                    if (IsLiveEdit)
                    {
                        Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                            "TSA_WD_Allegiances_ResetConfirm".Translate(),
                            () =>
                            {
                                ResetAllRelationsToDefaults();
                                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                            }));
                    }
                    else
                    {
                        ResetAllRelationsToDefaults();
                        SoundDefOf.Tick_High.PlayOneShotOnCamera();
                    }
                })) { }

            x -= PageBtnGap;
            x -= DrawPackedHeaderButton(ref x, y, btnH, "TSA_WD_BtnResetGlobal".Translate(),
                "TSA_WD_BtnResetGlobal_Tooltip".Translate(), () => s.ResetAllegianceLocks(true),
                SoundDefOf.Tick_High);
            x -= PageBtnGap;
            x -= DrawPackedHeaderButton(ref x, y, btnH, "TSA_WD_BtnReset".Translate(),
                "TSA_WD_BtnReset_Tooltip".Translate(), () => s.ResetAllegianceLocks(false),
                SoundDefOf.Tick_High);
            x -= PageBtnGap;
            x -= DrawPackedHeaderButton(ref x, y, btnH, "TSA_WD_BtnAllowAll".Translate(),
                "TSA_WD_BtnAllowAll_Tooltip".Translate(), () => s.lockedAllegiancePairs.Clear(),
                SoundDefOf.Checkbox_TurnedOff);
            x -= PageBtnGap;
            DrawPackedHeaderButton(ref x, y, btnH, "TSA_WD_BtnLockAll".Translate(),
                "TSA_WD_BtnLockAll_Tooltip".Translate(), LockAllNpcPairs,
                SoundDefOf.Checkbox_TurnedOn);

            Text.Font = GameFont.Small;
        }

        private static float DrawPackedHeaderButton(
            ref float rightEdge,
            float y,
            float btnH,
            string label,
            string tip,
            Action onClick,
            SoundDef sound)
        {
            float btnW = MeasureHeaderBtnW(label);
            rightEdge -= btnW;
            Rect rect = new Rect(rightEdge, y, btnW, btnH);
            if (DrawHeaderButton(rect, label, tip, onClick))
                sound?.PlayOneShotOnCamera();
            return btnW;
        }

        private static float MeasureHeaderBtnW(string label)
        {
            Text.Font = PageBtnFont;
            float w = Text.CalcSize(label).x + 14f;
            return Mathf.Max(52f, w);
        }

        private static bool DrawHeaderButton(Rect rect, string label, string tip, Action onClick)
        {
            TooltipHandler.TipRegion(rect, tip);
            Text.Font = PageBtnFont;
            if (!Widgets.ButtonText(rect, label)) return false;
            onClick();
            return true;
        }

        private void LockAllNpcPairs()
        {
            var s = WorldDominationMod.settings;
            if (s == null) return;
            for (int i = 0; i < factionPairs.Count; i++)
            {
                if (!IsNpcNpcPair(factionPairs[i])) continue;
                s.lockedAllegiancePairs.Add(s.GetFactionPairKey(factionPairs[i].First, factionPairs[i].Second));
            }
        }

        private static void DrawCollapsibleHeader(Rect rect, ref bool expanded, string labelKey, string tipKey)
        {
            Widgets.DrawHighlightIfMouseover(rect);
            TooltipHandler.TipRegion(rect, tipKey.Translate());
            if (Widgets.ButtonInvisible(rect))
                expanded = !expanded;

            Color c = SettingsUI.SectionHeaderColor;
            string colorHex = ColorUtility.ToHtmlStringRGBA(c);
            string arrow = expanded ? "▼" : "▶";
            Text.Font = GameFont.Small;
            Widgets.Label(rect, $"<b><color=#{colorHex}>{arrow}  {labelKey.Translate()}</color></b>");
            Text.Font = GameFont.Small;
        }

        private void DrawFactionRosterTable(Rect outRect, WorldDominationSettings s)
        {
            string searchLower = string.IsNullOrEmpty(searchTerm) ? null : searchTerm.ToLowerInvariant();
            var visible = new List<Faction>();
            for (int i = 0; i < factionRoster.Count; i++)
            {
                Faction f = factionRoster[i];
                if (searchLower != null && !FactionMatchesFilter(f, searchLower)) continue;
                visible.Add(f);
            }

            var layout = new RosterLayout(ContentWidth(outRect));
            int rowCount = (visible.Count + RosterColumnsPerRow - 1) / RosterColumnsPerRow;
            float viewW = layout.ContentWidth;
            float viewH = TableHeaderH + 4f + rowCount * RosterRowH + 8f;
            Rect viewRect = new Rect(0f, 0f, viewW, viewH);
            Rect scrollOut = outRect.ContractedBy(ScrollViewInset);
            Widgets.BeginScrollView(scrollOut, ref rosterScrollPosition, viewRect);

            float headerY = 0f;
            for (int col = 0; col < RosterColumnsPerRow; col++)
                DrawRosterBlockHeader(layout, col, headerY);
            Widgets.DrawLineHorizontal(0f, headerY + TableHeaderH, viewW);
            DrawRosterColumnSeparators(layout, headerY, viewH);

            for (int row = 0; row < rowCount; row++)
            {
                float rowY = TableHeaderH + 4f + row * RosterRowH;
                Rect rowRect = new Rect(0f, rowY, viewW, RosterRowH);
                if (row % 2 == 0) Widgets.DrawHighlight(rowRect);
                if (Mouse.IsOver(rowRect)) Widgets.DrawLightHighlight(rowRect);

                for (int col = 0; col < RosterColumnsPerRow; col++)
                {
                    int index = row * RosterColumnsPerRow + col;
                    if (index >= visible.Count) break;
                    DrawRosterFactionCell(s, visible, index, layout, col, rowY);
                }
            }

            Widgets.EndScrollView();
        }

        private static void DrawRosterColumnSeparators(RosterLayout layout, float yStart, float yEnd)
        {
            GUI.color = Color.white;
            for (int col = 1; col < RosterColumnsPerRow; col++)
                Widgets.DrawLineVertical(layout.SeparatorX(col), yStart, yEnd - yStart);
            GUI.color = Color.white;
        }

        private static void DrawRosterBlockHeader(RosterLayout layout, int column, float y)
        {
            float blockX = layout.BlockX(column);
            float x = blockX + RosterBlockPad;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            Rect nameRect = new Rect(x, y, layout.ColFaction, TableHeaderH);
            Widgets.Label(nameRect, "TSA_WD_FactionCol_Name".Translate());
            x += layout.ColFaction;
            Text.Anchor = TextAnchor.MiddleCenter;
            Rect wdRect = new Rect(x, y, RosterColWd, TableHeaderH);
            Widgets.Label(wdRect, "TSA_WD_FactionCol_WdActions".Translate());
            TooltipHandler.TipRegion(wdRect, "TSA_WD_FactionCol_WdActions_Tooltip".Translate());
            x += RosterColWd;
            Rect mapRect = new Rect(x, y, RosterColMapGen, TableHeaderH);
            Widgets.Label(mapRect, "TSA_WD_FactionCol_BaseGen".Translate());
            TooltipHandler.TipRegion(mapRect, "TSA_WD_FactionCol_BaseGen_Tooltip".Translate());
            x += RosterColMapGen;
            Rect stRect = new Rect(x, y, RosterColStoryteller, TableHeaderH);
            Widgets.Label(stRect, "TSA_WD_FactionCol_StorytellerRaids".Translate());
            TooltipHandler.TipRegion(stRect, "TSA_WD_FactionCol_StorytellerRaids_Tooltip".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        private static void DrawNotApplicableCell(Rect cellRect, string tip)
        {
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = Color.gray;
            Widgets.Label(cellRect, "TSA_WD_FactionCol_NA".Translate());
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            TooltipHandler.TipRegion(cellRect, tip);
        }

        private static void DrawRosterFactionCell(
            WorldDominationSettings s,
            List<Faction> visible,
            int index,
            RosterLayout layout,
            int column,
            float rowY)
        {
            Faction f = visible[index];
            bool hardExcluded = WorldActions_Utils.IsHardExcludedFromWd(f);
            bool softDefaultExcluded = WorldActions_Utils.IsSoftDefaultExcludedFromWd(f);
            bool defeated = f.defeated;
            string defName = f.def.defName;
            bool wdOn = WorldActions_Utils.IsWdParticipant(f);
            bool mapGenOn = wdOn && !s.IsManualWdBaseGenExclude(defName);
            bool storytellerOn = WorldActions_Utils.IsStorytellerRaidAllowed(f);

            float blockX = layout.BlockX(column);
            float x = blockX + RosterBlockPad;
            DrawFactionChip(new Rect(x, rowY + 4f, layout.ColFaction - 4f, RosterRowH - 8f), f, defeated);
            x += layout.ColFaction;

            Rect wdCell = new Rect(x, rowY, RosterColWd, RosterRowH);
            if (hardExcluded)
            {
                string hardTip = WorldActions_Utils.TryGetHardExcludedReasonKey(f, out string hardKey)
                    ? hardKey.Translate()
                    : "TSA_WD_FactionCol_WdActions_Tooltip".Translate();
                DrawNotApplicableCell(wdCell, hardTip);
            }
            else
            {
                bool wdEditable = !defeated;
                bool nextWd = wdOn;
                GUI.enabled = wdEditable;
                Widgets.Checkbox(new Vector2(x + (RosterColWd - 24f) * 0.5f, rowY + 8f), ref nextWd);
                GUI.enabled = true;
                if (wdEditable && nextWd != wdOn)
                {
                    s.ClearManualStorytellerOverride(defName);
                    if (softDefaultExcluded)
                    {
                        s.SetManualIncludeInWd(defName, nextWd);
                        if (!nextWd)
                        {
                            s.SetManualWdExclude(defName, true);
                            s.SetManualWdBaseGenExclude(defName, true);
                        }
                        else
                        {
                            s.SetManualWdExclude(defName, false);
                            s.SetManualWdBaseGenExclude(defName, false);
                        }
                    }
                    else
                    {
                        s.SetManualIncludeInWd(defName, false);
                        s.SetManualWdExclude(defName, !nextWd);
                        if (!nextWd)
                            s.SetManualWdBaseGenExclude(defName, true);
                        else
                            s.SetManualWdBaseGenExclude(defName, false);
                    }
                }

                string wdTip = softDefaultExcluded
                    && WorldActions_Utils.TryGetSoftDefaultExcludedReasonKey(f, out string softKey)
                    ? softKey.Translate()
                    : "TSA_WD_FactionCol_WdActions_Tooltip".Translate();
                TooltipHandler.TipRegion(wdCell, wdTip);
            }
            x += RosterColWd;

            Rect mapCell = new Rect(x, rowY, RosterColMapGen, RosterRowH);
            if (hardExcluded)
            {
                string hardTip = WorldActions_Utils.TryGetHardExcludedReasonKey(f, out string hardKey)
                    ? hardKey.Translate()
                    : "TSA_WD_FactionCol_BaseGen_Tooltip".Translate();
                DrawNotApplicableCell(mapCell, hardTip);
            }
            else
            {
                bool mapEditable = !defeated && wdOn;
                bool nextMap = mapGenOn;
                GUI.enabled = mapEditable;
                Widgets.Checkbox(new Vector2(x + (RosterColMapGen - 24f) * 0.5f, rowY + 8f), ref nextMap);
                GUI.enabled = true;
                if (mapEditable && nextMap != mapGenOn)
                    s.SetManualWdBaseGenExclude(defName, !nextMap);

                string mapTip = !wdOn
                    ? "TSA_WD_FactionCol_BaseGen_RequiresWdActions".Translate()
                    : "TSA_WD_FactionCol_BaseGen_Tooltip".Translate();
                TooltipHandler.TipRegion(mapCell, mapTip);
            }
            x += RosterColMapGen;

            Rect stCell = new Rect(x, rowY, RosterColStoryteller, RosterRowH);
            if (hardExcluded)
            {
                string hardTip = WorldActions_Utils.TryGetHardExcludedReasonKey(f, out string hardKey)
                    ? hardKey.Translate()
                    : "TSA_WD_FactionCol_StorytellerRaids_Tooltip".Translate();
                DrawNotApplicableCell(stCell, hardTip);
            }
            else
            {
                bool stEditable = !defeated;
                bool nextSt = storytellerOn;
                GUI.enabled = stEditable;
                Widgets.Checkbox(new Vector2(x + (RosterColStoryteller - 24f) * 0.5f, rowY + 8f), ref nextSt);
                GUI.enabled = true;
                if (stEditable && nextSt != storytellerOn)
                    s.SetManualStorytellerRaidsAllow(defName, nextSt, WorldActions_Utils.IsWdParticipant(f));

                TooltipHandler.TipRegion(stCell, "TSA_WD_FactionCol_StorytellerRaids_Tooltip".Translate());
            }
        }

        private void DrawDiplomacyTable(Rect outRect, WorldDominationSettings s)
        {
            int lastPlayerPairIndex = -1;
            for (int i = 0; i < filteredPairsCache.Count; i++)
            {
                if (InvolvesPlayer(filteredPairsCache[i]))
                    lastPlayerPairIndex = i;
            }

            float viewWidth = RowContentWidth;
            const float rowStep = 40f;
            float separatorExtra = lastPlayerPairIndex >= 0 ? 8f : 0f;
            float viewH = filteredPairsCache.Count * rowStep + separatorExtra;
            Rect viewRect = new Rect(0f, 0f, viewWidth, viewH);
            Rect scrollOut = outRect.ContractedBy(ScrollViewInset);
            Widgets.BeginScrollView(scrollOut, ref diplomacyScrollPosition, viewRect);

            float drawY = 0f;
            for (int i = 0; i < filteredPairsCache.Count; i++)
            {
                var pair = filteredPairsCache[i];
                Rect rowRect = new Rect(0f, drawY, viewWidth, 36f);
                if (i % 2 == 0) Widgets.DrawHighlight(rowRect);
                if (Mouse.IsOver(rowRect)) Widgets.DrawLightHighlight(rowRect);

                string key = s.GetFactionPairKey(pair.First, pair.Second);
                FactionRelationKind kind = GetRelationKind(pair.First, pair.Second);
                int goodwill = GetGoodwill(pair.First, pair.Second);
                bool permVsPlayer = IsPermanentHostileVsPlayer(pair);
                bool npcNpc = IsNpcNpcPair(pair);

                float x = RowPadLeft;
                DrawFactionChip(new Rect(x, rowRect.y + 4f, FactionChipW, 28f), pair.First, false);
                x += FactionChipW + ChipGap;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(x, rowRect.y, ArrowSlotW - 4f, 36f), "↔");
                Text.Anchor = TextAnchor.UpperLeft;
                x += ArrowSlotW;
                DrawFactionChip(new Rect(x, rowRect.y + 4f, FactionChipW, 28f), pair.Second, false);
                x += FactionChipW + AfterSecondChipGap;

                float relBtnH = PageBtnH;
                float relY = rowRect.y + (36f - PageBtnH) * 0.5f;

                if (permVsPlayer)
                {
                    float labelW = RelBtnW * 3f + RelBtnGap * 2f;
                    Rect permRect = new Rect(x, relY, labelW, relBtnH);
                    Text.Font = GameFont.Tiny;
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(permRect, "TSA_WD_WorldSetup_PermanentlyHostile".Translate().Colorize(ColorLibrary.RedReadable));
                    Text.Anchor = TextAnchor.UpperLeft;
                    Text.Font = GameFont.Small;
                    x += labelW + AfterRelBtnsGap;

                    Rect gwReadOnly = new Rect(x, relY, GoodwillW, relBtnH);
                    Text.Font = GameFont.Tiny;
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(gwReadOnly, FormatGoodwill(goodwill).Colorize(FactionRelationKind.Hostile.GetColor()));
                    Text.Anchor = TextAnchor.UpperLeft;
                    Text.Font = GameFont.Small;
                    TooltipHandler.TipRegion(gwReadOnly, GoodwillFieldTooltip());
                }
                else
                {
                    if (DrawRelationToggle(new Rect(x, relY, RelBtnW, relBtnH),
                        "TSA_WD_WorldSetup_RelNeutral".Translate(), FactionRelationKind.Neutral, kind))
                        ApplyKind(pair.First, pair.Second, FactionRelationKind.Neutral);
                    x += RelBtnW + RelBtnGap;
                    if (DrawRelationToggle(new Rect(x, relY, RelBtnW, relBtnH),
                        "TSA_WD_WorldSetup_RelAllied".Translate(), FactionRelationKind.Ally, kind))
                        ApplyKind(pair.First, pair.Second, FactionRelationKind.Ally);
                    x += RelBtnW + RelBtnGap;
                    if (DrawRelationToggle(new Rect(x, relY, RelBtnW, relBtnH),
                        "TSA_WD_WorldSetup_RelHostile".Translate(), FactionRelationKind.Hostile, kind))
                        ApplyKind(pair.First, pair.Second, FactionRelationKind.Hostile);
                    x += RelBtnW + AfterRelBtnsGap;

                    Rect gwRect = new Rect(x, relY, GoodwillW, relBtnH);
                    DrawGoodwillField(gwRect, key, pair.First, pair.Second, goodwill);
                    x += GoodwillW + AfterGoodwillGap;

                    if (npcNpc)
                    {
                        bool isLocked = s.lockedAllegiancePairs.Contains(key);
                        Rect lockRect = new Rect(x, relY, LockBtnW, relBtnH);
                        Text.Font = PageBtnFont;
                        if (Widgets.ButtonText(lockRect,
                            isLocked
                                ? "TSA_WD_StatusLocked".Translate().Colorize(Color.red)
                                : "TSA_WD_StatusPossible".Translate()))
                        {
                            if (isLocked) s.lockedAllegiancePairs.Remove(key);
                            else s.lockedAllegiancePairs.Add(key);
                            if (isLocked) SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
                            else SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
                        }
                        Text.Font = GameFont.Small;
                    }
                }

                drawY += rowStep;

                if (i == lastPlayerPairIndex)
                {
                    float sepY = drawY + 2f;
                    Widgets.DrawLineHorizontal(8f, sepY, viewWidth - 16f);
                    GUI.color = Color.white;
                    drawY += 8f;
                }
            }

            Widgets.EndScrollView();
        }

        private bool DrawRelationToggle(Rect r, string label, FactionRelationKind forKind, FactionRelationKind current)
        {
            bool selected = forKind == current;
            bool mouseOver = Mouse.IsOver(r);
            bool pressed = mouseOver && Input.GetMouseButton(0);
            Color bg = selected ? NavBtnBgSelected : pressed ? NavBtnBgPress : mouseOver ? NavBtnBgHover : NavSlateFill;
            Widgets.DrawBoxSolid(r, bg);
            GUI.color = selected ? NavBtnOutlineSelected : mouseOver ? NavBtnOutlineHover : NavBtnOutline;
            Widgets.DrawBox(r, 1);
            GUI.color = Color.white;

            Text.Font = PageBtnFont;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(r, label.Colorize(forKind.GetColor()));
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            return Widgets.ButtonInvisible(r);
        }

        private static int MaxGoodwill => WorldActions_DiplomacyBuffsNerfs.MaxGoodwillAbs;

        private static string GoodwillFieldTooltip() =>
            "TSA_WD_WorldSetup_GoodwillFieldTooltip".Translate(MaxGoodwill);

        private static int ClampGoodwill(int goodwill) =>
            Mathf.Clamp(goodwill, MinGoodwill, MaxGoodwill);

        private void DrawGoodwillField(Rect rect, string key, Faction a, Faction b, int currentGoodwill)
        {
            string controlName = "wd_gw_" + key;
            bool focused = GUI.GetNameOfFocusedControl() == controlName;
            if (!focused)
                goodwillEditBuffers[key] = FormatGoodwill(currentGoodwill);

            if (!goodwillEditBuffers.TryGetValue(key, out string buffer) || buffer == null)
                buffer = FormatGoodwill(currentGoodwill);

            GUI.SetNextControlName(controlName);
            string next = Widgets.TextField(rect, buffer);
            goodwillEditBuffers[key] = next;

            bool returnPressed = focused
                && Event.current.type == EventType.KeyDown
                && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
            bool clickOutsideCommit = focused
                && Event.current.type == EventType.MouseDown
                && !Mouse.IsOver(rect);

            if ((returnPressed || clickOutsideCommit) && TryParseGoodwill(next, out int parsed))
            {
                int clamped = ClampGoodwill(parsed);
                if (clamped != currentGoodwill)
                    ApplyGoodwill(a, b, clamped);
                else
                    goodwillEditBuffers[key] = FormatGoodwill(currentGoodwill);

                if (returnPressed)
                {
                    Event.current.Use();
                    GUI.FocusControl(null);
                }
            }

            TooltipHandler.TipRegion(rect, GoodwillFieldTooltip());
        }

        private static bool TryParseGoodwill(string text, out int value)
        {
            value = 0;
            if (string.IsNullOrEmpty(text)) return false;
            text = text.Trim();
            if (text.StartsWith("+")) text = text.Substring(1);
            return int.TryParse(text, out value);
        }

        private static string FormatGoodwill(int goodwill)
        {
            if (goodwill > 0) return "+" + goodwill;
            return goodwill.ToString();
        }

        private static FactionRelationKind GetRelationKind(Faction a, Faction b) =>
            WorldActions_Utils.SafeRelationKindWith(a, b);

        private static int GetGoodwill(Faction a, Faction b)
        {
            FactionRelation rel = a?.RelationWith(b, true);
            return rel?.baseGoodwill ?? 0;
        }

        private void ApplyKind(Faction a, Faction b, FactionRelationKind next)
        {
            int gw = WorldActions_DiplomacyBuffsNerfs.GoodwillForKind(next);
            ApplyGoodwill(a, b, gw);
        }

        private void ApplyGoodwill(Faction a, Faction b, int goodwill)
        {
            goodwill = ClampGoodwill(goodwill);
            int expiry = Find.TickManager?.TicksGame ?? 0;
            if (!WorldActions_DiplomacyBuffsNerfs.TrySetDiplomacyGoodwill(a, b, goodwill, expiry, out _))
            {
                Messages.Message("TSA_WD_WorldSetup_RelationFailed".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            string key = WorldDominationMod.settings.GetFactionPairKey(a, b);
            goodwillEditBuffers[key] = FormatGoodwill(goodwill);
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
        }

        private void ResetAllRelationsToDefaults()
        {
            int expiry = Find.TickManager?.TicksGame ?? 0;

            for (int i = 0; i < factionPairs.Count; i++)
            {
                Faction a = factionPairs[i].First;
                Faction b = factionPairs[i].Second;
                if (IsPermanentHostileVsPlayer(factionPairs[i])) continue;
                int goodwill = DefaultGoodwillBetween(a, b);
                WorldActions_DiplomacyBuffsNerfs.TrySetDiplomacyGoodwill(a, b, goodwill, expiry, out _);
            }

            goodwillEditBuffers.Clear();
            Messages.Message("TSA_WD_WorldSetup_ResetAllegiancesDone".Translate(), MessageTypeDefOf.PositiveEvent, false);
        }

        private static int DefaultGoodwillBetween(Faction a, Faction b)
        {
            int goodwillA = GetNaturalGoodwill(a, b);
            int goodwillB = GetNaturalGoodwill(b, a);
            return Mathf.Min(goodwillA, goodwillB);
        }

        private static int GetNaturalGoodwill(Faction a, Faction b)
        {
            if (a?.def == null || b?.def == null) return 0;
            if (a.def.permanentEnemy) return -100;
            if (a.def.permanentEnemyToEveryoneExceptPlayer && !b.IsPlayer) return -100;
            if (a.def.permanentEnemyToEveryoneExcept != null && !a.def.permanentEnemyToEveryoneExcept.Contains(b.def))
                return -100;
            if (WorldActions_Utils.IsPermanentEnemyOfPlayer(a) && b.IsPlayer) return -100;
            if (a.def.naturalEnemy) return -80;
            return 0;
        }

        private static void DrawFactionChip(Rect rect, Faction faction, bool grayed)
        {
            float iconSize = 22f;
            Rect iconRect = new Rect(rect.x, rect.y + (rect.height - iconSize) / 2f, iconSize, iconSize);
            Rect textRect = new Rect(iconRect.xMax + 6f, rect.y, rect.width - (iconSize + 6f), rect.height);

            GUI.color = grayed ? Color.gray : faction.Color;
            Widgets.DrawTextureFitted(iconRect, faction.def.FactionIcon, 1f);
            GUI.color = Color.white;

            Text.Anchor = TextAnchor.MiddleLeft;
            Text.Font = GameFont.Tiny;
            string name = faction.IsPlayer
                ? (faction.Name + " (" + "TSA_WD_Faction_Player".Translate() + ")")
                : faction.Name;
            if (grayed)
                name += " (" + "TSA_WD_Faction_Defeated".Translate() + ")";
            Widgets.Label(textRect, name.Truncate(textRect.width));
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }
    }
}
