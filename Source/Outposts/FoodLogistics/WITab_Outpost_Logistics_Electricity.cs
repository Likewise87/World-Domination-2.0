using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace TSA_WorldDomination
{
    /// <summary>Electricity pane for <see cref="WITab_Outpost_Logistics"/> (mirrors food layout/controls).</summary>
    public partial class WITab_Outpost_Logistics
    {
        private enum LogisticsPane : byte { Food = 0, Electricity = 1 }

        private LogisticsPane logisticsPane;
        private readonly List<TabRecord> logisticsPaneTabs = new List<TabRecord>(2);
        private Vector2 powerScrollPosition;
        private float powerScrollViewHeight;
        private Vector2 powerConsumerScrollPosition;
        private float powerConsumerScrollViewHeight;
        private readonly List<WorldObject_WD_Outpost> powerDestCache = new List<WorldObject_WD_Outpost>(16);
        private int powerDestCacheTick = -999999;
        private WorldObject_WD_Outpost powerDestCacheSel;
        private int powerDestCacheGen = int.MinValue;
        private readonly Dictionary<int, RowStrings> powerRowStrCache = new Dictionary<int, RowStrings>();
        private readonly Dictionary<int, RowStrings> powerConsumerRowStrCache = new Dictionary<int, RowStrings>();

        private int powerSectionCacheTick = -999999;
        private int powerSectionCachedGen = int.MinValue;
        private WorldObject_WD_Outpost powerSectionCachedSel;
        private OutpostStatsSection cachedPowerSection;

        private static string s_incomingPowerSupplyLines;
        private static string s_keepHereColony;
        private static string s_keepHereColonyTip;
        private static string s_paneFood;
        private static string s_paneElectricity;

        private static void EnsurePowerStaticStrings()
        {
            if (s_incomingPowerSupplyLines != null) return;
            s_incomingPowerSupplyLines = "TSA_IncomingPowerSupplyLines".Translate().ToString();
            if (s_incomingPowerSupplyLines.Contains("TSA_IncomingPowerSupplyLines"))
                s_incomingPowerSupplyLines = "Receiving Electricity From:";
            s_keepHereColony = "TSA_Logistics_KeepHereColony".Translate().ToString();
            if (s_keepHereColony.Contains("TSA_Logistics_KeepHereColony"))
                s_keepHereColony = "Keep (colony)";
            s_keepHereColonyTip = "TSA_Logistics_KeepHereColonyTip".Translate().ToString();
            if (s_keepHereColonyTip.Contains("TSA_Logistics_KeepHereColonyTip"))
                s_keepHereColonyTip = "Leftover after local demand and delivered links feed the colony power grid.";
            s_paneFood = "TSA_LogisticsPane_Food".Translate().ToString();
            if (s_paneFood.Contains("TSA_LogisticsPane_Food")) s_paneFood = "Food";
            s_paneElectricity = "TSA_LogisticsPane_Electricity".Translate().ToString();
            if (s_paneElectricity.Contains("TSA_LogisticsPane_Electricity")) s_paneElectricity = "Electricity";
        }

        /// <summary>Vanilla Form-Caravan style tabs along the top edge of <paramref name="contentRect"/>.</summary>
        private void DrawLogisticsPaneTabs(Rect contentRect, bool foodOn, bool powerOn)
        {
            EnsurePowerStaticStrings();
            logisticsPaneTabs.Clear();
            if (foodOn)
            {
                logisticsPaneTabs.Add(new TabRecord(
                    s_paneFood,
                    () => logisticsPane = LogisticsPane.Food,
                    logisticsPane == LogisticsPane.Food));
            }
            if (powerOn)
            {
                logisticsPaneTabs.Add(new TabRecord(
                    s_paneElectricity,
                    () => logisticsPane = LogisticsPane.Electricity,
                    logisticsPane == LogisticsPane.Electricity));
            }
            if (logisticsPaneTabs.Count > 1)
                TabDrawer.DrawTabs(contentRect, logisticsPaneTabs);
        }

        private void FillTabElectricity(Rect body)
        {
            var grid = Find.World?.GetComponent<WorldComponent_OutpostPowerGrid>();
            if (grid == null || SelOutpost == null) return;
            EnsurePowerStaticStrings();
            grid.EnsureAllocationFresh();

            bool isPlant = SelOutpost.IsPowerPlantOutpost;

            // Same column geometry as food FillTab (RightColumnBoost shrinks left, widens right for producer tables).
            float leftColW = Mathf.Max(200f, body.width * LeftColumnFraction - RightColumnBoost);
            float rightX = body.x + leftColW + ColumnGap;
            float rightW = body.width - leftColW - ColumnGap;
            Text.Font = GameFont.Medium;
            string headline = OutpostTranslationUtil.TabHeadline(SelOutpost, "TSA_LogisticsPane_Electricity");
            if (headline.Contains("TSA_LogisticsPane_Electricity"))
                headline = OutpostTranslationUtil.TabHeadline(SelOutpost, "TSA_LogisticsTab");
            LabelAnchored(new Rect(body.x, body.y, body.width, 30f), headline, TextAnchor.MiddleLeft);
            Text.Font = GameFont.Small;
            Widgets.DrawLineHorizontal(body.x, body.y + 32f, body.width);

            float contentY = body.y + TabHeaderConsumedHeight;
            float contentH = body.height - TabHeaderConsumedHeight;

            var powerSection = EnsurePowerSectionFresh();
            OutpostTabStatsUi.DrawSectionColumn(body.x, contentY, leftColW, powerSection);

            float dividerX = body.x + leftColW + ColumnGap * 0.5f;
            Widgets.DrawLineVertical(dividerX, contentY, contentH);

            Rect rightRect = new Rect(rightX, contentY, rightW, contentH);
            if (isPlant)
                DrawPowerProducerColumn(rightRect, grid);
            else
                DrawPowerReceiverColumn(rightRect, grid);
        }

        private OutpostStatsSection EnsurePowerSectionFresh()
        {
            int now = Find.TickManager.TicksGame;
            int gen = OutpostPowerGrid.PowerStateGeneration;
            bool dirty = cachedPowerSection == null
                || powerSectionCachedSel != SelOutpost
                || powerSectionCachedGen != gen
                || now - powerSectionCacheTick >= LogisticsRefreshThrottleTicks;
            if (!dirty)
                return cachedPowerSection;

            powerSectionCacheTick = now;
            powerSectionCachedSel = SelOutpost;
            powerSectionCachedGen = gen;
            cachedPowerSection = OutpostStatsSnapshot.BuildPowerSectionForTab(SelOutpost);
            return cachedPowerSection;
        }

        private void EnsurePowerDestCache()
        {
            int now = Find.TickManager.TicksGame;
            int gen = OutpostPowerGrid.PowerStateGeneration;
            if (powerDestCacheSel == SelOutpost
                && powerDestCacheGen == gen
                && now - powerDestCacheTick < LogisticsTabWorldScanIntervalTicks)
                return;

            powerDestCacheTick = now;
            powerDestCacheSel = SelOutpost;
            powerDestCacheGen = gen;
            powerDestCache.Clear();
            powerRowStrCache.Clear();
            powerConsumerRowStrCache.Clear();
            if (SelOutpost == null) return;
            float maxRange = OutpostPowerReach.GetMaxRangeTiles();
            IReadOnlyList<WorldObject_WD_Outpost> list = WdPlayerOutpostCache.PlayerOutposts;
            for (int i = 0; i < list.Count; i++)
            {
                WorldObject_WD_Outpost op = list[i];
                if (op == null || op.Destroyed || op == SelOutpost) continue;
                if (op.Faction != Faction.OfPlayer) continue;
                // Plants cover themselves from generation; never a power destination for other plants.
                if (op.IsPowerPlantOutpost) continue;
                if (OutpostPowerGrid.GetPowerDemandWatts(op) <= OutpostPowerGrid.DemandEpsilon) continue;
                if (Find.WorldGrid.ApproxDistanceInTiles(SelOutpost.Tile, op.Tile) > maxRange) continue;
                powerDestCache.Add(op);
            }
            powerDestCache.Sort((a, b) =>
                Find.WorldGrid.ApproxDistanceInTiles(SelOutpost.Tile, a.Tile)
                    .CompareTo(Find.WorldGrid.ApproxDistanceInTiles(SelOutpost.Tile, b.Tile)));
        }

        private void DrawPowerProducerColumn(Rect rightRect, WorldComponent_OutpostPowerGrid grid)
        {
            EnsurePowerDestCache();
            // Only surplus after local demand is distributable (food Keep-local parity).
            float budget = OutpostPowerGrid.GetDistributableWatts(SelOutpost);
            // ACTUAL colony Keep (matches left stats / PowerNet). +/- still gated on INTENT residual below.
            float keepHere = grid.GetCachedColonyKeepWatts(SelOutpost);

            Text.Font = GameFont.Small;
            GUI.color = Widgets.SeparatorLabelColor;
            Widgets.Label(new Rect(rightRect.x, rightRect.y, rightRect.width, 22f), s_allocationStrategy);
            GUI.color = Color.white;
            float y = rightRect.y + 24f;

            DrawPowerSmartToggles(new Rect(rightRect.x, y, rightRect.width, 28f), grid);
            y += 32f;

            GUI.color = Widgets.SeparatorLabelColor;
            Rect keepHeaderRect = new Rect(rightRect.x, y, rightRect.width, 22f);
            Widgets.Label(keepHeaderRect, s_keepHereColony);
            GUI.color = Color.white;
            TooltipHandler.TipRegion(keepHeaderRect, s_keepHereColonyTip);
            y += 24f;

            float rowContentW = rightRect.width - 16f;
            Rect keepRow = new Rect(rightRect.x, y, rowContentW, ProducerRowHeight);
            DrawPowerKeepHereRow(keepRow, keepHere);
            y += ProducerRowStep;

            GUI.color = Widgets.SeparatorLabelColor;
            Widgets.Label(new Rect(rightRect.x, y, rightRect.width, 22f), s_distributeTo);
            GUI.color = Color.white;
            y += 24f;

            Rect listRect = new Rect(rightRect.x, y, rightRect.width, rightRect.yMax - y);
            DrawPowerProducerInterface(listRect, grid, budget);
        }

        private void DrawPowerKeepHereRow(Rect row, float keepAmount)
        {
            Widgets.DrawHighlight(row);
            Widgets.DrawHighlightIfMouseover(row);
            GetProducerRowLayout(row.width, out float nameX, out float nameW, out float netX, out _, out _);

            float contentY = row.y + row.height / 2f;
            Rect iconRect = new Rect(row.x, contentY - RowIconSize / 2f, RowIconSize, RowIconSize);
            WorldDomination_UIUtils.DrawFactionIconWithColor(iconRect, Faction.OfPlayer);

            string colonyLabel = s_colonyPrefix;
            Widgets.Label(new Rect(row.x + nameX, contentY - 21f, nameW, 24f), colonyLabel.Truncate(nameW - 4f));
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(row.x + nameX, contentY + 2f, nameW, 18f), s_keepHereColonyTip.Truncate(nameW - 4f));
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            Rect netRect = new Rect(row.x + netX, row.y, ProducerNetW, row.height);
            // Same keep-row treatment as food: signed amount, green when keeping power for the colony.
            LabelAnchored(netRect, FormatNetColored(keepAmount), TextAnchor.MiddleCenter);
            TooltipHandler.TipRegion(netRect, s_keepHereColonyTip);
        }

        private void DrawPowerSmartToggles(Rect rect, WorldComponent_OutpostPowerGrid grid)
        {
            const float btnSize = 24f;
            const float labelGap = 4f;
            const float groupGap = 16f;
            const float resetBtnW = 140f;

            bool smart = LogisticsModeUtil.IsSmartMode(SelOutpost.PowerLogisticsMode);
            float smartLabelW = Text.CalcSize(s_toggleLblSmart).x;
            float smartGroupW = btnSize + labelGap + smartLabelW;
            float smartX = rect.x;
            Rect smartHit = new Rect(smartX, rect.y, smartGroupW, btnSize);
            if (Widgets.RadioButton(smartX, rect.y, smart))
            {
                if (!smart)
                {
                    SelOutpost.SetPowerLogisticsMode(LogisticsMode.Smart);
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }
            }
            LabelAnchored(new Rect(smartX + btnSize + labelGap, rect.y, smartLabelW, btnSize), s_toggleLblSmart, TextAnchor.MiddleLeft);
            TooltipHandler.TipRegion(smartHit, s_toggleTipSmart);

            float manualLabelW = Text.CalcSize(s_toggleLblManual).x;
            float manualGroupW = btnSize + labelGap + manualLabelW;
            float manualX = smartX + smartGroupW + groupGap;
            Rect manualHit = new Rect(manualX, rect.y, manualGroupW, btnSize);
            if (Widgets.RadioButton(manualX, rect.y, !smart))
            {
                if (smart)
                {
                    SelOutpost.SetPowerLogisticsMode(LogisticsMode.Manual);
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }
            }
            LabelAnchored(new Rect(manualX + btnSize + labelGap, rect.y, manualLabelW, btnSize), s_toggleLblManual, TextAnchor.MiddleLeft);
            TooltipHandler.TipRegion(manualHit, s_toggleTipManual);

            float tableContentW = rect.width - 16f;
            float resetRight = rect.x + tableContentW - ProducerScrollbarPad;
            Rect resetRect = new Rect(resetRight - resetBtnW, rect.y, resetBtnW, btnSize);
            if (Widgets.ButtonText(resetRect, s_resetAllocation))
            {
                SelOutpost.SetPowerLogisticsMode(LogisticsMode.Manual);
                grid.ClearOutgoingFromTile(SelOutpost.Tile.tileId);
                powerDestCacheTick = -999999;
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
            TooltipHandler.TipRegion(resetRect, s_resetAllocationTip);
        }

        private void DrawPowerProducerInterface(
            Rect listRect,
            WorldComponent_OutpostPowerGrid grid,
            float budget)
        {
            if (powerDestCache.Count == 0)
            {
                DrawIsolatedMessage(listRect, producer: true, power: true);
                return;
            }

            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, powerScrollViewHeight);
            Widgets.BeginScrollView(listRect, ref powerScrollPosition, viewRect);

            float vw = viewRect.width;
            GetProducerRowLayout(vw, out float nameX, out float nameW, out float netX, out float ctrlX, out float ctrlW);

            float innerY = 0f;
            int rowIndex = 0;
            for (int i = 0; i < powerDestCache.Count; i++)
            {
                WorldObject_WD_Outpost target = powerDestCache[i];
                int destTile = target.Tile.tileId;
                if (!powerRowStrCache.TryGetValue(destTile, out RowStrings rs))
                {
                    rs = new RowStrings
                    {
                        jump = "TSA_WD_JumpToOutpost".Translate(target.LabelCap),
                        name = (target.Name ?? target.LabelCap) + " (" + (target.def?.label ?? "") + ")"
                    };
                    powerRowStrCache[destTile] = rs;
                }

                float val = grid.GetAssignment(SelOutpost.Tile.tileId, destTile);
                float demand = OutpostPowerGrid.GetPowerDemandWatts(target);
                float supplied = OutpostPowerGrid.GetSuppliedWatts(target);

                Rect row = new Rect(0f, innerY, vw, ProducerRowHeight);
                if (rowIndex % 2 == 0) Widgets.DrawHighlight(row);
                Widgets.DrawHighlightIfMouseover(row);

                Rect jumpRect = new Rect(row.x, row.y, netX, row.height);
                if (Widgets.ButtonInvisible(jumpRect))
                {
                    CameraJumper.TryJump(target);
                    Find.WorldSelector.ClearSelection();
                    Find.WorldSelector.Select(target);
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    if (Find.MainTabsRoot.OpenTab != null) Find.MainTabsRoot.EscapeCurrentTab();
                }
                TooltipHandler.TipRegion(jumpRect, rs.jump);

                float contentY = innerY + row.height / 2f;
                Rect iconRect = new Rect(row.x, contentY - RowIconSize / 2f, RowIconSize, RowIconSize);
                Texture2D iconTex = target.def?.ExpandingIconTexture;
                if (iconTex != null)
                {
                    GUI.color = target.Faction?.Color ?? Color.white;
                    GUI.DrawTexture(iconRect, iconTex, ScaleMode.ScaleToFit);
                    GUI.color = Color.white;
                }

                Widgets.Label(new Rect(nameX, contentY - 21f, nameW, 24f), rs.name.Truncate(nameW - 4f));
                Text.Font = GameFont.Tiny;
                GUI.color = Color.gray;
                Widgets.Label(new Rect(nameX, contentY + 2f, nameW, 18f),
                    Outpost_PowerPlant.FormatWatts(supplied) + " / " + Outpost_PowerPlant.FormatWatts(demand));
                GUI.color = Color.white;
                Text.Font = GameFont.Small;

                // Left of +/-: destination balance (like food net), colored by % of demand met.
                Rect netRect = new Rect(netX, innerY, ProducerNetW, ProducerRowHeight);
                LabelAnchored(netRect, FormatPowerBalanceColored(supplied, demand), TextAnchor.MiddleCenter);

                // Same as food: +/- always shown; clicking while Smart auto-switches to Manual.
                float btnY = innerY + (row.height / 2f) - 12f;
                string rowKey = destTile.ToString();
                Rect minusR = new Rect(ctrlX, btnY, AssignmentBtnW, 24f);
                if (WdDragSelectButtons.ButtonText(minusR, "-", WdDragSelectButtons.Hash(rowKey, "minus")))
                    AdjustPowerLink(grid, destTile, budget, isMinus: true);

                val = grid.GetAssignment(SelOutpost.Tile.tileId, destTile);
                LabelAnchored(new Rect(ctrlX + AssignmentBtnW, innerY, AssignmentValueW, row.height),
                    val.ToString("F0"), TextAnchor.MiddleCenter);

                bool canAdd = grid.GetOutgoingSumFromTile(SelOutpost.Tile.tileId) + 1f < budget;
                if (!canAdd) GUI.color = Color.gray;
                Rect plusR = new Rect(ctrlX + ctrlW - AssignmentBtnW, btnY, AssignmentBtnW, 24f);
                if (WdDragSelectButtons.ButtonText(plusR, "+", WdDragSelectButtons.Hash(rowKey, "plus"), active: canAdd) && canAdd)
                    AdjustPowerLink(grid, destTile, budget, isMinus: false);
                GUI.color = Color.white;

                innerY += ProducerRowStep;
                rowIndex++;
            }

            if (Event.current.type == EventType.Layout)
                powerScrollViewHeight = innerY;
            Widgets.EndScrollView();
        }

        /// <summary>Sets this hub→target watts and switches to Manual (food AdjustLink / SetLinkAssignment parity).</summary>
        private void AdjustPowerLink(
            WorldComponent_OutpostPowerGrid grid,
            int destTile,
            float budget,
            bool isMinus)
        {
            if (SelOutpost == null || grid == null) return;
            int srcTile = SelOutpost.Tile.tileId;
            float current = grid.GetAssignment(srcTile, destTile);
            float step = WdQuantityUI.AdjustmentStep() * 50f;

            float next;
            if (isMinus)
            {
                if (current <= 0f) return;
                next = Mathf.Max(0f, current - step);
            }
            else
            {
                // Food AdjustLink parity: re-read live outgoing so rapid +/- cannot overshoot.
                float remaining = budget - grid.GetOutgoingSumFromTile(srcTile);
                if (remaining <= 0.001f) return;
                next = current + Mathf.Min(step, remaining);
            }

            if (LogisticsModeUtil.IsSmartMode(SelOutpost.PowerLogisticsMode))
                SelOutpost.SetPowerLogisticsMode(LogisticsMode.Manual);
            grid.SetAssignment(srcTile, destTile, next);
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        private void DrawPowerReceiverColumn(Rect rightRect, WorldComponent_OutpostPowerGrid grid)
        {
            float y = OutpostTabStatsUi.DrawSectionHeader(rightRect.x, rightRect.y, rightRect.width, s_incomingPowerSupplyLines);
            Rect listRect = new Rect(rightRect.x, y, rightRect.width, rightRect.yMax - y);
            DrawPowerConsumerInterface(listRect, grid);
        }

        private void DrawPowerConsumerInterface(Rect listRect, WorldComponent_OutpostPowerGrid grid)
        {
            var lines = new List<(WorldObject_WD_Outpost Source, float Watts)>(8);
            foreach (var line in grid.EnumerateIncomingAssignments(SelOutpost))
                lines.Add(line);

            if (lines.Count == 0)
            {
                DrawIsolatedMessage(listRect, producer: false, power: true);
                return;
            }

            const float rowH = 40f;
            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, powerConsumerScrollViewHeight);
            Widgets.BeginScrollView(listRect, ref powerConsumerScrollPosition, viewRect);

            float vw = viewRect.width;
            const float iconColW = 40f;
            float amountColW = 70f;
            float nameColW = vw - iconColW - amountColW - 8f;

            float curY = 0f;
            int rowIndex = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                WorldObject_WD_Outpost srcOp = lines[i].Source;
                float watts = lines[i].Watts;
                if (srcOp == null) continue;

                Rect rowRect = new Rect(0f, curY, vw, rowH);
                if (rowIndex % 2 == 0) Widgets.DrawHighlight(rowRect);
                Widgets.DrawHighlightIfMouseover(rowRect);

                if (Widgets.ButtonInvisible(rowRect))
                {
                    CameraJumper.TryJump(srcOp);
                    Find.WorldSelector.ClearSelection();
                    Find.WorldSelector.Select(srcOp);
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    if (Find.MainTabsRoot.OpenTab != null) Find.MainTabsRoot.EscapeCurrentTab();
                }

                int tid = srcOp.Tile.tileId;
                if (!powerConsumerRowStrCache.TryGetValue(tid, out RowStrings crs))
                {
                    crs = new RowStrings
                    {
                        jump = "TSA_WD_JumpToOutpost".Translate(srcOp.LabelCap),
                        name = (srcOp.Name ?? srcOp.LabelCap) + " (" + (srcOp.def?.label ?? "") + ")"
                    };
                    powerConsumerRowStrCache[tid] = crs;
                }
                TooltipHandler.TipRegion(rowRect, crs.jump);

                float contentY = curY + rowH / 2f;
                Rect iconRect = new Rect(4f, contentY - RowIconSize / 2f, RowIconSize, RowIconSize);
                Texture2D iconTex = srcOp.def?.ExpandingIconTexture;
                if (iconTex != null)
                {
                    GUI.color = srcOp.Faction?.Color ?? Color.white;
                    GUI.DrawTexture(iconRect, iconTex, ScaleMode.ScaleToFit);
                    GUI.color = Color.white;
                }

                Rect nameRect = new Rect(iconColW + 4f, curY, nameColW - 8f, rowH);
                Rect amountRect = new Rect(iconColW + nameColW + 4f, curY, amountColW - 8f, rowH);
                LabelAnchored(nameRect, crs.name.Truncate(nameColW - 12f), TextAnchor.MiddleLeft);
                LabelAnchored(amountRect, Outpost_PowerPlant.FormatWatts(watts).Colorize(Color.cyan), TextAnchor.MiddleLeft);

                curY += rowH;
                rowIndex++;
            }

            if (Event.current.type == EventType.Layout)
                powerConsumerScrollViewHeight = curY;
            Widgets.EndScrollView();
        }
    }
}
