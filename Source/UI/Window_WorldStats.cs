using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Verse;
using RimWorld;
using RimWorld.Planet;
using static TSA_WorldDomination.SpreadLogEntry;
using Verse.Sound;

namespace TSA_WorldDomination
{
    [StaticConstructorOnStartup]
    public class Window_WorldStats : Window
    {
        private const float ColIcon = 60f;
        private const float ColFaction = 140f;
        private const float ColStatus = 180f;
        private const float ColT = 170f;
        private const float FactionIconSize = 40f;
        private const float DetailsBtnW = 68f;
        private const float DetailsBtnH = 22f;
        private const float StatusIconSize = 32f;
        private const float StatusIconGap = 4f;
        private const int StatusIconCount = 4;
        private static readonly Texture2D StatusIconLeader =
            ContentFinder<Texture2D>.Get("UI/Commands/Leader", false);
        private static readonly Texture2D StatusIconNimble =
            ContentFinder<Texture2D>.Get("UI/Commands/Nimble", false);
        private static readonly Texture2D StatusIconExpansionist =
            ContentFinder<Texture2D>.Get("UI/Commands/Expansionist", false);
        private static readonly Texture2D StatusIconCoalition =
            ContentFinder<Texture2D>.Get("UI/Commands/Coalition", false);
        private static readonly Color StatusIconInactiveTint = new Color(0.65f, 0.65f, 0.65f);
        private static readonly Color StatusIconActiveTint = new Color(0.92f, 0.18f, 0.16f);

        private Vector2 scrollPos;
        private static string nameFilter = "";
        private List<FactionStat> visibleList;
        public override Vector2 InitialSize => new Vector2(UI.screenWidth, UI.screenHeight);

        private GlobalWorldStats statsData;
        private List<FactionStat> displayList;

        private const int DefaultSortColumn = 6;
        private const bool DefaultSortAscending = false;
        private static int sortColumn = DefaultSortColumn;
        private static bool sortAscending = DefaultSortAscending;

        private struct RowDisplayCache
        {
            public string nameColorized;
            public string tooltipName;
            public string[] tierCountStr;
            public string[] tierStrStr;
            public string[] tierShareStr;
            public string[] tierTooltip;
            public string totalCountColorized;
            public string totalStrColorized;
            public string totalShareColorized;
            public string totalTooltip;
        }

        private Dictionary<Faction, RowDisplayCache> rowCacheMap;
        private string cachedWorldTotalsLabel;
        private string cachedEscalationLabel;
        private string cachedEscalationTip;
        private string[] cachedFooterTierCount;
        private string[] cachedFooterTierStr;
        private string cachedFooterTotalCount;
        private string cachedFooterTotalStr;
        private string[] cachedFooterTierTips;
        private string cachedFooterTotalsTip;
        private string[] cachedTierHeaderTips;
        private bool displayCacheBuilt;
        private int displayCacheBuiltTick;

        private static readonly StringBuilder tierHeaderTipScratch = new StringBuilder();

        public Window_WorldStats()
        {
            this.doCloseX = true;
            this.draggable = false;
            this.absorbInputAroundWindow = true;
            this.forcePause = true; // full-screen stats: pause so the sim does not advance under the window
            this.closeOnCancel = true;

            this.statsData = WorldStatsUtils.GetWorldPowerStats();
            displayList = new List<FactionStat>(statsData.FactionStats);

            Faction pFact = Find.FactionManager.OfPlayer;
            bool hasPlayerRow = false;
            if (pFact != null)
            {
                for (int i = 0; i < displayList.Count; i++)
                {
                    if (displayList[i].faction == pFact) { hasPlayerRow = true; break; }
                }
            }
            if (pFact != null && !hasPlayerRow)
            {
                var playerStats = GetPlayerOutpostStats();
                displayList.Add(playerStats);
                for (int t = 1; t <= 4; t++) statsData.GlobalTierStr[t] += playerStats.strength[t];
                statsData.GlobalTotalStr += playerStats.TotalStr;
            }

            SortList();
        }

        public override void PostClose()
        {
            base.PostClose();
            PawnRosterHeaderFilter.CloseDropdown();
            WdWindowEsc.ClearTextFocusOnClose();
        }

        private void SortList()
        {
            int col = sortColumn;
            displayList.Sort((a, b) =>
            {
                int cmp;
                switch (col)
                {
                    case 0:
                        cmp = string.Compare(a.faction.Name, b.faction.Name, StringComparison.OrdinalIgnoreCase);
                        break;
                    case 2: case 3: case 4: case 5:
                        int tierIdx = col - 1;
                        cmp = a.strength[tierIdx].CompareTo(b.strength[tierIdx]);
                        break;
                    case 6:
                        cmp = a.TotalStr.CompareTo(b.TotalStr);
                        break;
                    default:
                        cmp = 0;
                        break;
                }
                return sortAscending ? cmp : -cmp;
            });
            RebuildVisibleList();
        }

        private static bool MatchesFactionNameFilter(Faction f, string filter)
        {
            if (filter.NullOrEmpty() || f == null) return true;
            if (!f.Name.NullOrEmpty() && f.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return f.def != null && f.def.LabelCap.ToString().IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private List<FactionStat> RebuildVisibleList()
        {
            if (visibleList == null)
                visibleList = new List<FactionStat>();
            else
                visibleList.Clear();
            if (displayList == null) return visibleList;
            for (int i = 0; i < displayList.Count; i++)
            {
                FactionStat fs = displayList[i];
                if (MatchesFactionNameFilter(fs.faction, nameFilter))
                    visibleList.Add(fs);
            }
            return visibleList;
        }

        private void BuildDisplayCache()
        {
            if (cachedManager == null)
                cachedManager = Find.World.GetComponent<WorldComponent_SpreadManager>();
            var manager = cachedManager;
            cachedWorldTotalsLabel = "TSA_WD_Stats_WorldTotals".Translate().Colorize(Color.cyan);

            WdEscalationStage stage = manager.cachedEscalationStage;
            string stageLabel = stage == WdEscalationStage.None
                ? "TSA_WD_Escalation_StageEarly".Translate().ToString()
                : WdEscalation.StageLabel(stage);
            cachedEscalationLabel = "TSA_WD_Stats_EscalationStage".Translate(stageLabel);
            cachedEscalationTip = "TSA_WD_Stats_EscalationTip".Translate(
                stageLabel,
                manager.cachedPlayerOutpostStrength.ToString("F0"),
                manager.cachedPlayerGlobalShare.ToString("P1")).ToString();

            float nameWidth = ColFaction;
            Text.Font = GameFont.Small;

            rowCacheMap = new Dictionary<Faction, RowDisplayCache>(displayList.Count);
            for (int i = 0; i < displayList.Count; i++)
            {
                var fs = displayList[i];
                var rc = new RowDisplayCache();

                rc.nameColorized = fs.faction.Name.Truncate(nameWidth)
                    .Colorize(WorldDomination_UIUtils.ColorForRelationWithPlayer(fs.faction));
                rc.tooltipName = fs.faction.IsPlayer ? (string)fs.faction.def.LabelCap : fs.faction.def.LabelCap + "\n\n" + fs.faction.GetInfoText();

                rc.tierCountStr = new string[5];
                rc.tierStrStr = new string[5];
                rc.tierShareStr = new string[5];
                rc.tierTooltip = new string[5];
                for (int t = 1; t <= 4; t++)
                {
                    float share = statsData.GlobalTierStr[t] > 0 ? (fs.strength[t] / statsData.GlobalTierStr[t]) : 0;
                    rc.tierCountStr[t] = fs.counts[t].ToString();
                    rc.tierStrStr[t] = fs.strength[t].ToString("F0");
                    rc.tierShareStr[t] = share.ToString("P0");
                    rc.tierTooltip[t] = "TSA_WD_Stats_RowTierTip".Translate(
                        t.ToString(),
                        fs.counts[t].ToString(),
                        fs.strength[t].ToString("F0"),
                        share.ToString("P1")).ToString();
                }

                float totalShare = statsData.GlobalTotalStr > 0 ? (fs.TotalStr / statsData.GlobalTotalStr) : 0;
                rc.totalCountColorized = fs.TotalCount.ToString().Colorize(Color.cyan);
                rc.totalStrColorized = fs.TotalStr.ToString("F0").Colorize(Color.cyan);
                rc.totalShareColorized = totalShare.ToString("P1").Colorize(Color.cyan);
                rc.totalTooltip = "TSA_WD_Stats_RowTotalsTip".Translate(
                    fs.TotalCount.ToString(),
                    fs.TotalStr.ToString("F0"),
                    totalShare.ToString("P1")).ToString();

                rowCacheMap[fs.faction] = rc;
            }

            int sumT1 = 0, sumT2 = 0, sumT3 = 0, sumT4 = 0, sumTotalCount = 0;
            for (int i = 0; i < displayList.Count; i++)
            {
                FactionStat x = displayList[i];
                sumT1 += x.counts[1];
                sumT2 += x.counts[2];
                sumT3 += x.counts[3];
                sumT4 += x.counts[4];
                sumTotalCount += x.TotalCount;
            }
            cachedFooterTierCount = new string[5];
            cachedFooterTierStr = new string[5];
            cachedFooterTierTips = new string[5];
            int[] tierSums = { 0, sumT1, sumT2, sumT3, sumT4 };
            for (int t = 1; t <= 4; t++)
            {
                cachedFooterTierCount[t] = tierSums[t].ToString();
                cachedFooterTierStr[t] = statsData.GlobalTierStr[t].ToString("F0");
                cachedFooterTierTips[t] = "TSA_WD_Stats_FooterTierTip".Translate(
                    t.ToString(),
                    cachedFooterTierCount[t],
                    cachedFooterTierStr[t]).ToString();
            }
            cachedFooterTotalCount = sumTotalCount.ToString();
            cachedFooterTotalStr = statsData.GlobalTotalStr.ToString("F0");
            cachedFooterTotalsTip = "TSA_WD_Stats_FooterTotalsTip".Translate(
                cachedFooterTotalCount,
                cachedFooterTotalStr).ToString();

            cachedTierHeaderTips = new string[5];
            for (int t = 1; t <= 4; t++)
                cachedTierHeaderTips[t] = BuildTierHeaderTip(t);

            displayCacheBuilt = true;
            displayCacheBuiltTick = Find.TickManager.TicksGame;
        }

        /// <summary>
        /// Mouseover for Tier 1–4 column headers: NPC settlements use real tier;
        /// player outposts are bucketed by local defense strength via <see cref="WorldStatsUtils.TierIndexFromWorldStrengthTotal"/>.
        /// </summary>
        private static string BuildTierHeaderTip(int tierIndex)
        {
            FloatRange r2 = CompViralSpread.GetStrengthRange(SettlementTier.T2);
            FloatRange r3 = CompViralSpread.GetStrengthRange(SettlementTier.T3);
            FloatRange r4 = CompViralSpread.GetStrengthRange(SettlementTier.T4);

            string t2Min = r2.min.ToString("F0");
            string t3Min = r3.min.ToString("F0");
            string t4Min = r4.min.ToString("F0");
            string t2Max = (r3.min - 1f).ToString("F0");
            string t3Max = (r4.min - 1f).ToString("F0");

            string thisBand;
            switch (tierIndex)
            {
                case 1:
                    thisBand = "TSA_WD_Stats_TierHeader_BandUnder".Translate(t2Min);
                    break;
                case 2:
                    thisBand = "TSA_WD_Stats_TierHeader_BandRange".Translate(t2Min, t2Max);
                    break;
                case 3:
                    thisBand = "TSA_WD_Stats_TierHeader_BandRange".Translate(t3Min, t3Max);
                    break;
                default:
                    thisBand = "TSA_WD_Stats_TierHeader_BandAtLeast".Translate(t4Min);
                    break;
            }

            tierHeaderTipScratch.Clear();
            tierHeaderTipScratch.AppendLine("TSA_WD_Stats_TierHeaderTip_Npc".Translate(tierIndex));
            tierHeaderTipScratch.AppendLine();
            tierHeaderTipScratch.AppendLine("TSA_WD_Stats_TierHeaderTip_Player".Translate());
            tierHeaderTipScratch.AppendLine();
            tierHeaderTipScratch.AppendLine("TSA_WD_Stats_TierHeaderTip_Bands".Translate(
                t2Min, t2Min, t2Max, t3Min, t3Max, t4Min));
            tierHeaderTipScratch.AppendLine();
            tierHeaderTipScratch.Append("TSA_WD_Stats_TierHeaderTip_ThisColumn".Translate(tierIndex, thisBand));
            return tierHeaderTipScratch.ToString();
        }

        private void SortByColumn(int columnId)
        {
            if (sortColumn == columnId) sortAscending = !sortAscending;
            else { sortColumn = columnId; sortAscending = true; }
            SoundDefOf.Click.PlayOneShotOnCamera();
            SortList();
        }

        private WorldComponent_SpreadManager cachedManager;

        public override void DoWindowContents(Rect inRect)
        {
            WdNavWindows.ProcessHotkeys();
            if (!IsOpen) return;
            if (PawnRosterHeaderFilter.TryCloseDropdownOnCancel())
                return;
            if (WdWindowEsc.TryCloseOnCancel(this))
                return;

            if (cachedManager == null)
                cachedManager = Find.World.GetComponent<WorldComponent_SpreadManager>();
            var manager = cachedManager;
            int ticksNow = Find.TickManager.TicksGame;

            if (displayCacheBuilt && Find.TickManager.TicksGame - displayCacheBuiltTick >= 600)
                displayCacheBuilt = false;
            if (!displayCacheBuilt)
                BuildDisplayCache();

            // Layout: Medium title, Tiny gray headers, scroll below header.
            float colIcon = ColIcon;
            float colFaction = ColFaction;
            float colStatus = ColStatus;
            float colT = ColT;
            float identityW = colIcon + colFaction;
            float smallH = Mathf.Max(24f, Text.LineHeightOf(GameFont.Small));
            float tinyH = Mathf.Max(15f, Text.LineHeightOf(GameFont.Tiny));
            // Identity: Small name + Tiny type + optional Tiny Defeated + Details; tier cols: 3× Small.
            float rowHeight = Mathf.Max(16f + smallH + tinyH + tinyH + 2f + DetailsBtnH + 8f, 10f + smallH * 3f);
            float footerHeight = 8f + smallH * 2f + 8f;
            float headerH = Mathf.Max(30f, tinyH);

            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.UpperLeft;
            float titleH = 35f;
            float btn = WorldDomination_UIUtils.RosterIconBtnSize;
            float t2X = identityW + colStatus + colT;
            float restoreX = t2X + (colT - btn) * 0.5f;
            Widgets.Label(new Rect(0, 0, Mathf.Max(80f, restoreX - 8f), titleH), "TSA_WD_Stats_Title".Translate());
            WorldDomination_UIUtils.DrawTitleRestoreDefaultViewAt(restoreX, titleH, RestoreDefaultView);

            Rect hRect = new Rect(0, 40f, inRect.width, headerH);
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            float iconHdrX = 0f;
            PawnRosterHeaderFilter.DrawFilterableHeader(
                ref iconHdrX, hRect.y, colIcon, hRect.height,
                "", false, false, TextAnchor.MiddleCenter, false, null, null, null);
            float factionHdrX = colIcon;
            PawnRosterHeaderFilter.DrawFilterableHeader(
                ref factionHdrX, hRect.y, colFaction, hRect.height,
                "TSA_WD_Stats_H_Faction".Translate(),
                sortColumn == 0, sortAscending,
                TextAnchor.MiddleLeft,
                !nameFilter.NullOrEmpty(),
                "TSA_WD_FilterByName".Translate(),
                icon => PawnRosterHeaderFilter.OpenTextDropdown(
                    icon,
                    "TSA_WD_FilterByName".Translate(),
                    "TSA_WD_FilterByName".Translate(),
                    () => nameFilter,
                    v => { nameFilter = v ?? ""; RebuildVisibleList(); },
                    () => { nameFilter = ""; RebuildVisibleList(); }),
                () => SortByColumn(0));
            float statusHdrX = identityW;
            PawnRosterHeaderFilter.DrawFilterableHeader(
                ref statusHdrX, hRect.y, colStatus, hRect.height,
                "TSA_WD_Stats_H_Status".Translate(),
                false, false, TextAnchor.MiddleCenter, false, null, null, null);

            float curX = identityW + colStatus;
            Rect t1Header = new Rect(curX, hRect.y, colT, hRect.height);
            Rect t2Header = new Rect(curX + colT, hRect.y, colT, hRect.height);
            Rect t3Header = new Rect(curX + (colT * 2), hRect.y, colT, hRect.height);
            Rect t4Header = new Rect(curX + (colT * 3), hRect.y, colT, hRect.height);
            float hx = curX;
            PawnRosterHeaderFilter.DrawFilterableHeader(
                ref hx, hRect.y, colT, hRect.height,
                "TSA_WD_Stats_H_T1".Translate(), sortColumn == 2, sortAscending,
                TextAnchor.MiddleCenter, false, null, null, () => SortByColumn(2));
            PawnRosterHeaderFilter.DrawFilterableHeader(
                ref hx, hRect.y, colT, hRect.height,
                "TSA_WD_Stats_H_T2".Translate(), sortColumn == 3, sortAscending,
                TextAnchor.MiddleCenter, false, null, null, () => SortByColumn(3));
            PawnRosterHeaderFilter.DrawFilterableHeader(
                ref hx, hRect.y, colT, hRect.height,
                "TSA_WD_Stats_H_T3".Translate(), sortColumn == 4, sortAscending,
                TextAnchor.MiddleCenter, false, null, null, () => SortByColumn(4));
            PawnRosterHeaderFilter.DrawFilterableHeader(
                ref hx, hRect.y, colT, hRect.height,
                "TSA_WD_Stats_H_T4".Translate(), sortColumn == 5, sortAscending,
                TextAnchor.MiddleCenter, false, null, null, () => SortByColumn(5));
            PawnRosterHeaderFilter.DrawFilterableHeader(
                ref hx, hRect.y, colT, hRect.height,
                "TSA_WD_Stats_H_Influence".Translate(), sortColumn == 6, sortAscending,
                TextAnchor.MiddleCenter, false, null, null, () => SortByColumn(6));
            if (cachedTierHeaderTips != null)
            {
                TooltipHandler.TipRegion(t1Header, cachedTierHeaderTips[1]);
                TooltipHandler.TipRegion(t2Header, cachedTierHeaderTips[2]);
                TooltipHandler.TipRegion(t3Header, cachedTierHeaderTips[3]);
                TooltipHandler.TipRegion(t4Header, cachedTierHeaderTips[4]);
            }
            GUI.color = Color.white;
            Widgets.DrawLineHorizontal(0, hRect.yMax, inRect.width);

            List<FactionStat> rows = RebuildVisibleList();
            Rect viewRect = new Rect(0, 0, inRect.width - 25f, rows.Count * rowHeight);
            float scrollTop = hRect.yMax + 5f;
            Rect scrollRect = new Rect(0, scrollTop, inRect.width, inRect.height - scrollTop - footerHeight);
            Widgets.BeginScrollView(scrollRect, ref scrollPos, viewRect);

            for (int i = 0; i < rows.Count; i++)
            {
                var fs = rows[i];
                var rc = rowCacheMap[fs.faction];
                Rect row = new Rect(0, i * rowHeight, viewRect.width, rowHeight);

                if (fs.faction == Faction.OfPlayer)
                    Widgets.DrawBoxSolid(row, new Color(0.2f, 0.5f, 0.8f, 0.15f));
                else if (i % 2 == 0)
                    Widgets.DrawHighlight(row);
                if (Mouse.IsOver(row))
                    Widgets.DrawLightHighlight(row);

                // 1. IDENTITY (Left Aligned): name, type, optional Defeated, Details under type
                Text.Anchor = TextAnchor.MiddleLeft;
                float iconY = row.y + Mathf.Max(7f, (rowHeight - FactionIconSize) * 0.5f);
                Rect iconRect = new Rect(row.x + 10f, iconY, FactionIconSize, FactionIconSize);
                WorldDomination_UIUtils.DrawFactionIconWithColor(iconRect, fs.faction);

                bool showDefeated = fs.faction.defeated;
                float identityH = smallH + tinyH + (showDefeated ? tinyH : 0f) + 2f + DetailsBtnH;
                float leftTop = row.y + Mathf.Max(0f, (rowHeight - identityH) * 0.5f);

                Rect nameArea = new Rect(colIcon, leftTop, colFaction, smallH);
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(nameArea, rc.nameColorized);
                TooltipHandler.TipRegion(nameArea, rc.tooltipName);

                Rect typeArea = new Rect(nameArea.x, nameArea.yMax, nameArea.width, tinyH);
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = Color.gray;
                Widgets.Label(typeArea, fs.faction.def.LabelCap.Truncate(typeArea.width));
                GUI.color = Color.white;

                Rect afterTypeRect = typeArea;
                if (showDefeated)
                {
                    Rect defeatedArea = new Rect(typeArea.x, typeArea.yMax, typeArea.width, tinyH);
                    Text.Font = GameFont.Tiny;
                    Text.Anchor = TextAnchor.MiddleLeft;
                    GUI.color = Color.red;
                    Widgets.Label(defeatedArea, "TSA_WD_Stats_FactionDefeated".Translate());
                    GUI.color = Color.white;
                    TooltipHandler.TipRegion(defeatedArea, "TSA_WD_Stats_FactionDefeatedTip".Translate());
                    afterTypeRect = defeatedArea;
                }

                Text.Font = GameFont.Tiny;
                Rect detailsBtn = new Rect(nameArea.x, afterTypeRect.yMax + 2f, DetailsBtnW, DetailsBtnH);
                if (Widgets.ButtonText(detailsBtn, "TSA_WD_Log_BtnDetails".Translate()))
                {
                    Find.WindowStack.Add(new Window_FactionDetails(fs.faction));
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }

                // 2. STATUS (NPC rows only: grey inactive, red active)
                if (!fs.faction.IsPlayer)
                    DrawFactionStatusIcons(new Rect(identityW + 5f, row.y, colStatus - 5f, rowHeight), fs.faction, manager, ticksNow);

                // 3. TIER DATA (Middle Centered)
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                float tierPad = Mathf.Max(0f, (rowHeight - smallH * 3f) * 0.5f);
                for (int t = 1; t <= 4; t++)
                {
                    float dX = identityW + colStatus + (colT * (t - 1));
                    Rect tRect = new Rect(dX, row.y, colT, rowHeight);
                    Widgets.Label(new Rect(tRect.x, tRect.y + tierPad, tRect.width, smallH), rc.tierCountStr[t]);
                    Widgets.Label(new Rect(tRect.x, tRect.y + tierPad + smallH, tRect.width, smallH), rc.tierStrStr[t]);
                    Widgets.Label(new Rect(tRect.x, tRect.y + tierPad + smallH * 2f, tRect.width, smallH), rc.tierShareStr[t]);
                    TooltipHandler.TipRegion(tRect, rc.tierTooltip[t]);
                }

                // 4. TOTALS (Middle Centered)
                float totX = identityW + colStatus + (colT * 4);
                Rect totRect = new Rect(totX, row.y, colT, rowHeight);
                Widgets.Label(new Rect(totRect.x, totRect.y + tierPad, totRect.width, smallH), rc.totalCountColorized);
                Widgets.Label(new Rect(totRect.x, totRect.y + tierPad + smallH, totRect.width, smallH), rc.totalStrColorized);
                Widgets.Label(new Rect(totRect.x, totRect.y + tierPad + smallH * 2f, totRect.width, smallH), rc.totalShareColorized);

                TooltipHandler.TipRegion(totRect, rc.totalTooltip);

                Text.Anchor = TextAnchor.UpperLeft;
            }
            Widgets.EndScrollView();

            // --- 6. FOOTER (Middle Centered for Data) ---
            Rect footerRect = new Rect(0, inRect.height - footerHeight, inRect.width, footerHeight);
            Widgets.DrawBoxSolid(footerRect, new Color(1f, 1f, 1f, 0.05f));
            GUI.color = Color.cyan;
            Widgets.DrawLineHorizontal(footerRect.x, footerRect.y, footerRect.width);
            GUI.color = Color.white;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            float totalsColW = identityW + colStatus;
            float footLine0 = footerRect.y + 8f;
            Rect totalsLabelRect = new Rect(15f, footLine0, totalsColW - 20f, smallH);
            Widgets.Label(totalsLabelRect, cachedWorldTotalsLabel);

            GUI.color = Color.white;
            Rect escalationRect = new Rect(15f, footLine0 + smallH, totalsColW - 20f, smallH);
            Widgets.Label(escalationRect, cachedEscalationLabel.Truncate(escalationRect.width));
            TooltipHandler.TipRegion(escalationRect, cachedEscalationTip);

            Text.Anchor = TextAnchor.MiddleCenter;
            float footX = identityW + colStatus;
            float footDataH = smallH * 2f;
            for (int t = 1; t <= 4; t++)
            {
                float x = footX + (colT * (t - 1));
                Rect footCol = new Rect(x, footLine0, colT, footDataH);
                Widgets.Label(new Rect(x, footLine0, colT, smallH), cachedFooterTierCount[t]);
                Widgets.Label(new Rect(x, footLine0 + smallH, colT, smallH), cachedFooterTierStr[t]);
                if (cachedFooterTierTips != null)
                    TooltipHandler.TipRegion(footCol, cachedFooterTierTips[t]);
            }

            float totFootX = footX + (colT * 4);
            Rect totFootCol = new Rect(totFootX, footLine0, colT, footDataH);
            Widgets.Label(new Rect(totFootX, footLine0, colT, smallH), cachedFooterTotalCount);
            Widgets.Label(new Rect(totFootX, footLine0 + smallH, colT, smallH), cachedFooterTotalStr);
            if (cachedFooterTotalsTip != null)
                TooltipHandler.TipRegion(totFootCol, cachedFooterTotalsTip);

            Text.Anchor = TextAnchor.UpperLeft;
            PawnRosterHeaderFilter.DrawDropdownIfOpen();
        }

        private void RestoreDefaultView()
        {
            sortColumn = DefaultSortColumn;
            sortAscending = DefaultSortAscending;
            nameFilter = "";
            scrollPos = Vector2.zero;
            PawnRosterHeaderFilter.CloseDropdown();
            SortList();
        }

        private static void DrawFactionStatusIcons(Rect area, Faction faction, WorldComponent_SpreadManager manager, int ticksNow)
        {
            float icon = Mathf.Min(StatusIconSize, area.height - 4f);
            float rowW = icon * StatusIconCount + StatusIconGap * (StatusIconCount - 1);
            float x = area.x + Mathf.Max(0f, (area.width - rowW) * 0.5f);
            float y = area.y + (area.height - icon) * 0.5f;
            float step = icon + StatusIconGap;

            int leaderExpiry = manager != null ? manager.leaderHandicapExpiryTick : 0;
            int nimbleExpiry = manager != null ? manager.underdogBuffExpiryTick : 0;
            int zealExpiry = manager != null ? manager.expansionistZealExpiryTick : 0;
            int coalitionExpiry = manager != null ? manager.antiLeaderCoalitionExpiryTick : 0;
            bool leaderActive = manager != null
                && faction == manager.currentWorldLeader
                && ticksNow < leaderExpiry;
            bool nimbleActive = manager != null
                && faction == manager.currentWeakestUnderdog
                && ticksNow < nimbleExpiry;
            bool zealActive = manager != null
                && faction == manager.expansionistZealFaction
                && ticksNow < zealExpiry;
            bool coalitionMember = false;
            bool coalitionTarget = false;
            if (manager != null && manager.IsCoalitionActive(ticksNow))
            {
                coalitionMember = manager.IsActiveCoalitionMember(faction);
                coalitionTarget = faction == manager.antiLeaderCoalitionTarget;
            }

            WorldDominationSettings seth = WorldDominationMod.settings;
            TaggedString inactive = "TSA_WD_Status_IconInactive".Translate();
            Color allyGreen = FactionRelationKind.Ally.GetColor();
            DrawStatusIcon(
                new Rect(x, y, icon, icon),
                StatusIconLeader,
                leaderActive ? StatusIconActiveTint : StatusIconInactiveTint,
                ComposeStatusTip(
                    "TSA_WD_Status_LeaderTitle",
                    "TSA_WD_Status_LeaderBlurb",
                    leaderActive
                        ? "TSA_WD_Status_LeaderActive".Translate(
                            FormatStatusMult(seth?.leaderIncidentWeightMult ?? WorldDominationSettings.DefLeaderIncidentWeightMult),
                            FormatStatusDays(leaderExpiry, ticksNow))
                        : inactive));
            DrawStatusIcon(
                new Rect(x + step, y, icon, icon),
                StatusIconNimble,
                nimbleActive ? allyGreen : StatusIconInactiveTint,
                ComposeStatusTip(
                    "TSA_WD_Status_NimbleTitle",
                    "TSA_WD_Status_NimbleBlurb",
                    nimbleActive
                        ? "TSA_WD_Status_NimbleActive".Translate(
                            FormatStatusMult(seth?.underdogGrowthGainMult ?? WorldDominationSettings.DefUnderdogGrowthGainMult),
                            FormatStatusMult(seth?.underdogIncidentWeightMult ?? WorldDominationSettings.DefUnderdogIncidentWeightMult),
                            FormatStatusMult(seth?.underdogIncidentSeverityMult ?? WorldDominationSettings.DefUnderdogIncidentSeverityMult),
                            FormatStatusDays(nimbleExpiry, ticksNow))
                        : inactive));
            DrawStatusIcon(
                new Rect(x + step * 2f, y, icon, icon),
                StatusIconExpansionist,
                zealActive ? Color.cyan : StatusIconInactiveTint,
                ComposeStatusTip(
                    "TSA_WD_Status_ExpansionistTitle",
                    "TSA_WD_Status_ExpansionistBlurb",
                    zealActive
                        ? "TSA_WD_Status_ExpansionistActive".Translate(
                            FormatStatusMult(seth?.zealRaidRangeMult ?? WorldDominationSettings.DefZealRaidRangeMult),
                            FormatStatusDays(zealExpiry, ticksNow))
                        : inactive));
            TaggedString coalitionStatus = inactive;
            Color coalitionTint = StatusIconInactiveTint;
            if (coalitionTarget)
            {
                coalitionTint = StatusIconActiveTint;
                coalitionStatus = "TSA_WD_Status_CoalitionTargetActive".Translate(
                    FormatCoalitionPartnerNames(manager),
                    FormatStatusDays(coalitionExpiry, ticksNow));
            }
            else if (coalitionMember)
            {
                coalitionTint = allyGreen;
                string targetName = manager.antiLeaderCoalitionTarget?.Name ?? "?";
                coalitionStatus = "TSA_WD_Status_CoalitionMemberActive".Translate(
                    targetName,
                    FormatCoalitionPartnerNames(manager, faction),
                    FormatStatusDays(coalitionExpiry, ticksNow));
            }
            DrawStatusIcon(
                new Rect(x + step * 3f, y, icon, icon),
                StatusIconCoalition,
                coalitionTint,
                ComposeStatusTip(
                    "TSA_WD_Status_CoalitionTitle",
                    "TSA_WD_Status_CoalitionBlurb",
                    coalitionStatus));
        }

        private static TaggedString ComposeStatusTip(string titleKey, string blurbKey, TaggedString status)
            => titleKey.Translate() + "\n\n" + blurbKey.Translate() + "\n\n" + status;

        private static string FormatStatusMult(float mult) => mult.ToString("0.#") + "×";

        private static string FormatStatusDays(int expiryTick, int ticksNow)
            => Mathf.Max(0f, (expiryTick - ticksNow) / 60000f).ToString("0.#");

        private static string FormatCoalitionPartnerNames(WorldComponent_SpreadManager manager, Faction exclude = null)
        {
            if (manager?.antiLeaderCoalitionMembers == null || manager.antiLeaderCoalitionMembers.Count == 0)
                return "—";
            var names = new List<string>();
            for (int i = 0; i < manager.antiLeaderCoalitionMembers.Count; i++)
            {
                Faction f = manager.antiLeaderCoalitionMembers[i];
                if (f == null || f.defeated || f == exclude) continue;
                if (!f.Name.NullOrEmpty())
                    names.Add(f.Name);
            }
            return names.Count == 0 ? "—" : string.Join(", ", names);
        }

        private static void DrawStatusIcon(Rect rect, Texture2D tex, Color tint, TaggedString tip)
        {
            if (tex != null)
            {
                GUI.DrawTexture(
                    rect,
                    tex,
                    ScaleMode.ScaleToFit,
                    true,
                    0f,
                    tint,
                    0f,
                    0f);
            }
            TooltipHandler.TipRegion(rect, tip);
        }

        private FactionStat GetPlayerOutpostStats()
        {
            var stats = new FactionStat { faction = Faction.OfPlayer };
            if (stats.faction == null) return stats;
            List<WorldObject_WD_Outpost> outposts = WorldStatsUtils.CollectPlayerOutposts();
            for (int i = 0; i < outposts.Count; i++)
            {
                var comp = outposts[i].GetComponent<CompViralSpread>();
                float str = WorldStatsUtils.GetOutpostStatsStrength(comp);
                int tier = WorldStatsUtils.TierIndexFromWorldStrengthTotal(str);
                stats.counts[tier]++;
                stats.strength[tier] += str;
            }
            return stats;
        }
    }
}