using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Trading outpost: recruiting-style two-column dialog (settlement breakdown left, silver/gold picker right).</summary>
    public class Dialog_OutpostTrading : Window
    {
        private readonly WorldObject_WD_Outpost outpost;
        private Vector2 leftPartnerScrollPosition;
        private Vector2 rightScrollPosition;
        private readonly string windowTitleText;

        private readonly List<CachedPartnerRow> partnerRows = new List<CachedPartnerRow>();
        private readonly OutpostDialogNearbyMonitor nearbyMonitor = new OutpostDialogNearbyMonitor();
        private readonly string nearbyHeaderTooltip;

        private string nearbyHeaderLabel;

        private struct CachedPartnerRow
        {
            public Faction Faction;
            public WorldObject WorldObject;
            public string LabelLine;
            public string Tooltip;
            public bool Contributes;
        }

        private struct CachedCommodityRow
        {
            public ThingDef Def;
            public string Label;
            public string Formula;
            public string Tooltip;
        }

        private const float IconColW = 56f;
        private const float IconPadding = 8f;
        private const float ListRightMargin = 8f;
        private const float PartnerIconSize = 28f;
        private const float RowPadding = 6f;
        private const float LineH = 26f;
        private const float NameLabelHeight = Outpost_Dialog_UI.ListRowNameHeight;
        private const float FormulaLineHeight = Outpost_Dialog_UI.ListRowFormulaLineHeight;
        private const float FormulaTopPadding = Outpost_Dialog_UI.ListRowFormulaTopPadding;
        private const float FormulaBlockHeight = Outpost_Dialog_UI.ListRowFormulaBlockHeight;
        private const float CommodityRowHeight = NameLabelHeight + FormulaBlockHeight;

        public override Vector2 InitialSize => new Vector2(960f, 728f);

        public Dialog_OutpostTrading(WorldObject_WD_Outpost outpost)
        {
            this.outpost = outpost;
            doCloseButton = true;
            doCloseX = true;
            absorbInputAroundWindow = true;
            forcePause = false;
            optionalTitle = null;

            nearbyHeaderTooltip = OutpostTranslationUtil.Key(
                "TSA_WD_Trading_NearbyHeaderTip",
                Outpost_Trading.GetNearbyRadiusTiles(outpost).ToString());

            nearbyMonitor.ForceRefresh(outpost, RebuildPartnerRows);

            windowTitleText = OutpostTranslationUtil.Key("TSA_WD_Trading_WindowTitle");
        }

        public override void PreClose()
        {
            base.PreClose();
            Window_OutpostOverview.InvalidateCache();
        }

        private void RebuildPartnerRows()
        {
            partnerRows.Clear();
            if (outpost == null) return;
            Outpost_Trading.InvalidateTradingRadiusProbeCache(outpost);
            var partners = new List<Outpost_Trading.NearbyPartnerInfo>();
            Outpost_Trading.CollectSortedNearbyPartners(outpost, partners);
            for (int i = 0; i < partners.Count; i++)
            {
                var p = partners[i];
                partnerRows.Add(new CachedPartnerRow
                {
                    Faction = p.Faction,
                    WorldObject = p.WorldObject,
                    LabelLine = Outpost_Trading.FormatPartnerRowLabel(p),
                    Tooltip = Outpost_Trading.BuildPartnerRowTooltip(p),
                    Contributes = p.ContributesToFaction
                });
            }
            nearbyHeaderLabel = Outpost_Dialog_UI.FormatNearbyHeaderLabel(nearbyMonitor.NearbyCount);
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (outpost == null) return;

            nearbyMonitor.TryRefresh(outpost, RebuildPartnerRows);

            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            const float listRightMargin = 20f;
            float contentWidth = inRect.width - listRightMargin;
            const float closeXLeftInset = 22f;
            const float rightScrollbarW = 16f;
            float rightContentRight = inRect.width - closeXLeftInset;
            float headerSlotWidth = 165f;
            float slotX = rightContentRight - headerSlotWidth;

            float y = 0f;
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(0f, y, slotX - 8f, Outpost_Dialog_UI.DialogTitleHeight), windowTitleText);
            Text.Anchor = TextAnchor.MiddleRight;
            Text.Font = GameFont.Small;
            GUI.color = Outpost_Dialog_UI.NearbyCountColor(nearbyMonitor.NearbyCount);
            Rect slotRect = new Rect(slotX, y + Outpost_Dialog_UI.DialogHeaderSlotTopInset, headerSlotWidth, Outpost_Dialog_UI.DialogHeaderSlotHeight);
            Widgets.Label(slotRect, nearbyHeaderLabel);
            TooltipHandler.TipRegion(slotRect, nearbyHeaderTooltip);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            y += Outpost_Dialog_UI.DialogTitleRowAdvance;

            string outpostName = outpost.Name ?? outpost.Label;
            string typeLabel = outpost.def?.label ?? OutpostTranslationUtil.Key("TSA_WD_Outpost_GenericLabel");
            Widgets.Label(new Rect(0f, y, contentWidth, 24f), (outpostName + " (" + typeLabel + ")").Truncate(contentWidth));
            y += 28f;

            y = Outpost_Dialog_UI.DrawProductionPauseBanner(0f, y, contentWidth, outpost);
            y += Outpost_Dialog_UI.AfterPauseBannerGap;
            y = Outpost_Dialog_UI.DrawSkillDiminishingReturnsBanner(0f, y, contentWidth, outpost);

            const float bottomReserve = 48f;
            const float colGap = 18f;
            float columnsTop = y;
            float columnsBottom = inRect.height - bottomReserve;
            float leftW = Mathf.Max(260f, contentWidth * 0.42f);
            Rect leftArea = new Rect(0f, columnsTop, leftW, columnsBottom - columnsTop);
            float rightColRight = rightContentRight + rightScrollbarW;
            Rect rightArea = new Rect(leftW + colGap, columnsTop, rightColRight - (leftW + colGap), columnsBottom - columnsTop);
            Widgets.DrawLineVertical(leftW + colGap * 0.5f, columnsTop, columnsBottom - columnsTop);

            DrawLeftColumn(leftArea);
            DrawRightColumn(rightArea);

            const float clearBtnW = 120f;
            const float closeBtnHeight = 40f;
            float bottomY = inRect.height - closeBtnHeight - 4f;
            string clearBtnLabel = OutpostTranslationUtil.Key("TSA_WD_Production_Clear");
            if (Widgets.ButtonText(new Rect(inRect.width - listRightMargin - clearBtnW, bottomY, clearBtnW, closeBtnHeight), clearBtnLabel))
            {
                string confirmMsg = OutpostTranslationUtil.Key("TSA_WD_Production_ClearConfirm");
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(confirmMsg, () => outpost.SetSelectedProduction(null)));
            }
        }

        private void DrawLeftColumn(Rect leftArea)
        {
            float lx = leftArea.x;
            float lw = leftArea.width;
            float ly = leftArea.y;
            Text.Anchor = TextAnchor.MiddleLeft;

            float snapshotSocialRaw = outpost.GetTotalRelevantSkillRaw();
            float snapshotSocial = OutpostSkillScaling.ToEffective(snapshotSocialRaw);
            float avgSocial = outpost.GetCapacityForYieldPreview();
            ThingDef previewDef = outpost.GetProducingDefForCurrentCycle() ?? outpost.SelectedProductionDef ?? ThingDefOf.Silver;
            string detailedMathAvg = Outpost_Trading.GetDetailedMathTooltip(outpost, avgSocial);
            string detailedMathSnapshot = Outpost_Trading.GetDetailedMathTooltip(outpost, snapshotSocial);
            string skillLabel = SkillDefOf.Social.label;
            string snapshotSocialDisplay = OutpostSkillScaling.FormatRawEffective(snapshotSocialRaw);

            const float lineH = Outpost_Dialog_UI.OutcomeLineH;
            const float boxPad = Outpost_Dialog_UI.OutcomeBoxPad;
            float cycleDaysLeft = outpost.ProductionTicksLeftForDisplay / 60000f;
            string expectedValueText = Outpost_Trading.FormatTradingAmountLine(outpost, avgSocial, previewDef);
            float valueH = Outpost_Dialog_UI.MeasureTextLinesHeight(expectedValueText);
            float boxH = boxPad * 2f + (lineH + 2f) + lineH + lineH + valueH;
            Outpost_Dialog_UI.DrawOutcomeBox(new Rect(lx, ly, lw, boxH));
            float cy = ly + boxPad;
            float ix = lx + boxPad;
            float iw = lw - boxPad * 2f;

            GUI.color = Outpost_Dialog_UI.CycleTimerColor;
            Rect cycleRect = new Rect(ix, cy, iw, lineH);
            Widgets.Label(cycleRect, OutpostTranslationUtil.Key("TSA_WD_Production_Info_CycleEndsIn", cycleDaysLeft.ToString("F1")));
            TooltipHandler.TipRegion(cycleRect, OutpostTranslationUtil.Key("TSA_WD_Production_Info_CycleEndsInTip"));
            GUI.color = Color.white;
            cy += lineH + 2f;

            Rect avgRect = new Rect(ix, cy, iw, lineH);
            Widgets.Label(avgRect, OutpostTranslationUtil.Key("TSA_WD_Production_Info_AvgSkill", skillLabel, avgSocial.ToString("F0")));
            TooltipHandler.TipRegion(avgRect, OutpostTranslationUtil.Key("TSA_WD_Trading_Info_AvgSocialTip", avgSocial.ToString("F0")));
            cy += lineH;

            float outBlockTop = cy;
            Widgets.Label(new Rect(ix, cy, iw, lineH), OutpostTranslationUtil.Key("TSA_WD_Production_Info_OutputCycleEnd"));
            cy += lineH;
            cy = Outpost_Dialog_UI.DrawTextOutcomeLines(
                ix + Outpost_Dialog_UI.OutcomeValueIndent,
                cy,
                iw - Outpost_Dialog_UI.OutcomeValueIndent,
                expectedValueText,
                Outpost_Dialog_UI.OutcomeValueColor);
            TooltipHandler.TipRegion(new Rect(ix, outBlockTop, iw, cy - outBlockTop), detailedMathAvg);
            ly += boxH + Outpost_Dialog_UI.OutcomeBoxGap;

            Rect curRect = new Rect(lx, ly, lw, lineH);
            Widgets.Label(curRect, OutpostTranslationUtil.Key("TSA_WD_Production_Info_CurrentSkill", skillLabel, snapshotSocialDisplay));
            string curTip = OutpostTranslationUtil.Key("TSA_WD_Trading_Info_CurrentSocialTip", snapshotSocialRaw.ToString("F0"));
            if (OutpostSkillScaling.IsDiminished(snapshotSocialRaw))
                curTip = curTip + "\n\n" + OutpostSkillScaling.BuildBandBreakdownTip(snapshotSocialRaw);
            TooltipHandler.TipRegion(curRect, curTip);
            ly += lineH;

            float snapBlockTop = ly;
            GUI.color = Outpost_Dialog_UI.TheoreticalLabelColor;
            Widgets.Label(new Rect(lx, ly, lw, lineH), OutpostTranslationUtil.Key("TSA_WD_Production_Info_OutputNow"));
            GUI.color = Color.white;
            ly += lineH;
            string snapshotValueText = Outpost_Trading.FormatTradingAmountLine(outpost, snapshotSocial, previewDef);
            ly = Outpost_Dialog_UI.DrawTextOutcomeLines(
                lx + Outpost_Dialog_UI.OutcomeValueIndent,
                ly,
                lw - Outpost_Dialog_UI.OutcomeValueIndent,
                snapshotValueText,
                Color.white);
            TooltipHandler.TipRegion(new Rect(lx, snapBlockTop, lw, ly - snapBlockTop), detailedMathSnapshot);
            ly += Outpost_Dialog_UI.AfterSnapshotGap;

            Widgets.DrawLineHorizontal(lx, ly, lw);
            ly += 6f;

            ThingDef cycleDef = outpost.GetProducingDefForCurrentCycle();
            const float selIconSize = 24f;
            const float selRowH = 28f;
            Texture2D cycleIcon = cycleDef?.uiIcon;
            if (cycleIcon != null)
            {
                Rect selIconRect = new Rect(lx, ly, selIconSize, selIconSize);
                GUI.color = cycleDef.graphicData?.color ?? Color.white;
                Widgets.DrawTextureFitted(selIconRect, cycleIcon, 1f);
                GUI.color = Color.white;
            }
            string cycleName = cycleDef?.LabelCap ?? OutpostTranslationUtil.Key("TSA_WD_Production_NoneLabel");
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(new Rect(lx + selIconSize + 6f, ly, lw - selIconSize - 6f, selRowH),
                OutpostTranslationUtil.Key("TSA_WD_Production_SelectedForThisCycle", cycleName));
            Text.Anchor = TextAnchor.MiddleLeft;
            ly += selRowH + 2f;

            if (outpost.IsSelectionLockedForThisCycle)
            {
                ThingDef selDef = outpost.SelectedProductionDef;
                if (selDef != null && selDef != cycleDef)
                {
                    GUI.color = new Color(1f, 0.85f, 0.35f);
                    if (selDef.uiIcon != null)
                    {
                        Rect nextIconRect = new Rect(lx, ly, selIconSize, selIconSize);
                        GUI.color = selDef.graphicData?.color ?? Color.white;
                        Widgets.DrawTextureFitted(nextIconRect, selDef.uiIcon, 1f);
                        GUI.color = new Color(1f, 0.85f, 0.35f);
                    }
                    Text.Anchor = TextAnchor.UpperLeft;
                    Rect nextRect = new Rect(lx + selIconSize + 6f, ly, lw - selIconSize - 6f, selRowH);
                    Widgets.Label(nextRect, OutpostTranslationUtil.Key("TSA_WD_Production_SelectedForNextCycle", selDef.LabelCap));
                    TooltipHandler.TipRegion(nextRect, OutpostTranslationUtil.Key("TSA_WD_Production_SelectedForNextCycleTip"));
                    Text.Anchor = TextAnchor.MiddleLeft;
                    GUI.color = Color.white;
                    ly += selRowH + 2f;
                }
            }

            int interval = outpost.ProductionTicksIntervalPublic;
            int lockThreshold = (int)(interval * 0.75f);
            bool changeable = outpost.ProductionTicksLeft > lockThreshold;
            float changeableWindowDays = Mathf.Max(0, interval - lockThreshold) / 60000f;
            string timerLine = changeable
                ? OutpostTranslationUtil.Key("TSA_WD_Production_SelectionChangeableFor", (Mathf.Max(0, outpost.ProductionTicksLeft - lockThreshold) / 60000f).ToString("F1"))
                : OutpostTranslationUtil.Key("TSA_WD_Production_SelectionLocked");
            GUI.color = changeable ? Color.green : Color.gray;
            Rect timerRect = new Rect(lx, ly, lw, lineH);
            Widgets.Label(timerRect, timerLine);
            TooltipHandler.TipRegion(timerRect, OutpostTranslationUtil.Key("TSA_WD_Production_SelectionChangeWindowTip", changeableWindowDays.ToString("F1")));
            GUI.color = Color.white;
            ly += lineH + 8f;

            GUI.color = new Color(0.72f, 0.72f, 0.72f);
            Rect srcHdr = new Rect(lx, ly, lw, LineH);
            Widgets.Label(srcHdr, OutpostTranslationUtil.Key("TSA_WD_Trading_NearbySettlementsHeader"));
            TooltipHandler.TipRegion(srcHdr, OutpostTranslationUtil.Key("TSA_WD_Trading_NearbySettlementsTip"));
            GUI.color = Color.white;
            ly += LineH + 2f;

            string footerRuleText = Outpost_Trading.GetFooterRuleText();
            Text.Font = GameFont.Tiny;
            float footerRuleH = Mathf.Max(36f, Text.CalcHeight(footerRuleText, lw));
            const float footerSummaryLineH = 22f;
            float neighborFooterH = footerRuleH + footerSummaryLineH * 2f + 14f;
            float partnersTop = ly;
            float partnersBottom = leftArea.yMax - 8f - neighborFooterH;
            float partnersH = Mathf.Max(60f, partnersBottom - partnersTop);
            float partnerContentH = partnerRows.Count == 0 ? LineH : partnerRows.Count * (LineH + RowPadding);
            Rect partnerScrollOuter = new Rect(lx, partnersTop, lw, partnersH);
            Rect partnerView = new Rect(0f, 0f, lw - 16f, partnerContentH);
            Widgets.BeginScrollView(partnerScrollOuter, ref leftPartnerScrollPosition, partnerView);

            float ply = 0f;
            if (partnerRows.Count == 0)
            {
                Widgets.Label(new Rect(0f, ply, partnerView.width, LineH), OutpostTranslationUtil.Key("TSA_WD_Trading_Math_None"));
            }
            else
            {
                int bestCount = 0;
                for (int i = 0; i < partnerRows.Count; i++)
                {
                    if (!partnerRows[i].Contributes) break;
                    bestCount++;
                }

                float rowH = LineH + RowPadding;
                Rect bestGroupRect = default;
                if (bestCount > 0)
                    bestGroupRect = new Rect(0f, 0f, partnerView.width, bestCount * rowH);

                for (int i = 0; i < partnerRows.Count; i++)
                {
                    Rect rowRect = new Rect(0f, i * rowH, partnerView.width, rowH);
                    if (i % 2 == 0) Widgets.DrawHighlight(rowRect);
                }
                if (bestCount > 0)
                    Outpost_Dialog_UI.DrawSelectedRowTint(bestGroupRect, true);

                for (int i = 0; i < partnerRows.Count; i++)
                    ply = DrawPartnerRowInScroll(ply, partnerView.width, partnerRows[i]);

                if (bestCount > 0)
                {
                    GUI.color = Color.white;
                    Widgets.DrawBox(bestGroupRect, 1);
                    GUI.color = Color.white;
                }
            }
            Widgets.EndScrollView();

            float fy = partnersTop + partnersH + 6f;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = new Color(0.72f, 0.72f, 0.72f);
            Rect ruleRect = new Rect(lx, fy, lw, footerRuleH);
            Widgets.Label(ruleRect, footerRuleText);
            TooltipHandler.TipRegion(ruleRect, Outpost_Trading.GetTierSilverDetailText());
            fy += footerRuleH + 4f;
            Widgets.Label(new Rect(lx, fy, lw, footerSummaryLineH), Outpost_Trading.GetFooterTierSumLine(outpost));
            fy += footerSummaryLineH;
            GUI.color = new Color(0.4f, 0.8f, 1f);
            Widgets.Label(new Rect(lx, fy, lw, footerSummaryLineH), Outpost_Trading.GetFooterExpectedLine(outpost, avgSocial));
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawRightColumn(Rect rightArea)
        {
            float x = rightArea.x;
            float y = rightArea.y;
            float w = rightArea.width;

            GUI.color = new Color(0.75f, 0.82f, 1f);
            Widgets.Label(new Rect(x, y, w, 22f), OutpostTranslationUtil.Key("TSA_WD_Production_ChooseHeader"));
            GUI.color = Color.white;
            y += 24f;

            float avgSocial = outpost.GetCapacityForYieldPreview();
            var ordered = BuildOrderedCommodityRows(avgSocial);
            float scrollHeight = 8f;
            foreach (var row in ordered)
                scrollHeight += CommodityRowHeight + RowPadding;

            Rect scrollOuter = new Rect(x, y, w, rightArea.height - (y - rightArea.y));
            Rect viewRect = new Rect(0f, 0f, w - 16f, scrollHeight);
            Widgets.BeginScrollView(scrollOuter, ref rightScrollPosition, viewRect);

            float curY = 0f;
            int visibleRow = 0;
            foreach (var row in ordered)
            {
                Rect rowRect = new Rect(0f, curY, viewRect.width, CommodityRowHeight + RowPadding);
                if (visibleRow % 2 == 0) Widgets.DrawHighlight(rowRect);
                bool isSelected = CommodityRowIsSelected(row);
                Outpost_Dialog_UI.DrawSelectedRowTint(rowRect, isSelected);

                float rowContentY = curY + (rowRect.height - CommodityRowHeight) / 2f;
                Rect iconRect = new Rect(IconPadding, rowContentY + (CommodityRowHeight - 32f) / 2f, 32f, 32f);
                Color? rowColor = row.Def.graphicData?.color;
                if (rowColor.HasValue) GUI.color = rowColor.Value;
                if (row.Def.uiIcon != null) Widgets.DrawTextureFitted(iconRect, row.Def.uiIcon, 1f);
                if (rowColor.HasValue) GUI.color = Color.white;

                Rect labelRect = new Rect(IconColW, rowContentY, viewRect.width - IconColW - ListRightMargin, NameLabelHeight);
                Widgets.Label(labelRect, row.Label);

                if (!string.IsNullOrEmpty(row.Formula))
                {
                    Text.Font = GameFont.Tiny;
                    GUI.color = Color.gray;
                    float formulaY = rowContentY + NameLabelHeight + FormulaTopPadding;
                    Rect formulaRect = new Rect(IconColW, formulaY, viewRect.width - IconColW - ListRightMargin, FormulaLineHeight);
                    Widgets.Label(formulaRect, row.Formula);
                    if (!string.IsNullOrEmpty(row.Tooltip))
                        TooltipHandler.TipRegion(formulaRect, row.Tooltip);
                    GUI.color = Color.white;
                    Text.Font = GameFont.Small;
                }

                Outpost_Dialog_UI.FinishSelectableListRow(rowRect, isSelected);
                if (Widgets.ButtonInvisible(rowRect))
                {
                    bool deferred = outpost.IsSelectionLockedForThisCycle && outpost.GetProducingDefForCurrentCycle() != row.Def;
                    outpost.SetSelectedProduction(row.Def);
                    if (deferred)
                        Messages.Message(OutpostTranslationUtil.Key("TSA_WD_Production_NextCycle"), outpost, MessageTypeDefOf.NeutralEvent);
                }

                curY += CommodityRowHeight + RowPadding;
                visibleRow++;
            }

            Widgets.EndScrollView();
        }

        private List<CachedCommodityRow> BuildOrderedCommodityRows(float avgSocial)
        {
            var rows = new List<CachedCommodityRow>();
            foreach (ThingDef def in Outpost_Trading.GetTradingCommodityOptions())
                rows.Add(BuildCommodityRow(def, avgSocial));

            ThingDef selected = outpost.SelectedProductionDef;
            if (selected == null) return rows;

            var ordered = new List<CachedCommodityRow>(rows.Count);
            foreach (var row in rows) if (row.Def == selected) ordered.Add(row);
            foreach (var row in rows) if (row.Def != selected) ordered.Add(row);
            return ordered;
        }

        private CachedCommodityRow BuildCommodityRow(ThingDef def, float avgSocial)
        {
            int tierSum = Outpost_Trading.GetSilverFromNearbyTiers(outpost);
            int socialInt = Mathf.RoundToInt(avgSocial);
            int multPct = Mathf.RoundToInt(Outpost_Trading.GetTradingSocialYieldMultiplier(outpost.def, socialInt) * 100f);
            int silverEq = Outpost_Trading.ComputeTradingSilverForOutpost(outpost, avgSocial);
            int amount = Outpost_Trading.ComputeTradingAmountForOutpost(outpost, avgSocial, def);

            string formula;
            if (Outpost_Trading.IsGoldTradingProduct(def))
            {
                formula = OutpostTranslationUtil.Key(
                    "TSA_WD_Trading_RowFormula_Gold",
                    amount.ToString(),
                    silverEq.ToString(),
                    Outpost_Trading.GoldPerSilverAmount.ToString());
            }
            else
            {
                formula = OutpostTranslationUtil.Key(
                    "TSA_WD_Trading_RowFormula_Silver",
                    amount.ToString(),
                    tierSum.ToString(),
                    multPct.ToString());
            }

            return new CachedCommodityRow
            {
                Def = def,
                Label = def.LabelCap,
                Formula = formula,
                Tooltip = Outpost_Trading.GetDetailedMathTooltip(outpost, avgSocial)
            };
        }

        private bool CommodityRowIsSelected(CachedCommodityRow row)
            => row.Def != null && outpost.SelectedProductionDef == row.Def;

        private float DrawPartnerRowInScroll(float ly, float lw, CachedPartnerRow row)
        {
            float rowH = LineH + RowPadding;
            Rect rowRect = new Rect(0f, ly, lw, rowH);
            if (Mouse.IsOver(rowRect)) Widgets.DrawHighlight(rowRect);

            float contentY = ly + RowPadding * 0.5f;
            Rect iconRect = new Rect(4f, contentY + (LineH - PartnerIconSize) * 0.5f, PartnerIconSize, PartnerIconSize);
            if (!row.Contributes)
                GUI.color = new Color(1f, 1f, 1f, 0.55f);
            WorldDomination_UIUtils.DrawFactionIconWithColor(InsetIconDrawRect(iconRect), row.Faction);

            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = row.Contributes ? Color.white : new Color(1f, 1f, 1f, 0.55f);
            Widgets.Label(new Rect(PartnerIconSize + 10f, contentY, lw - PartnerIconSize - 14f, LineH), row.LabelLine);
            GUI.color = Color.white;
            if (!string.IsNullOrEmpty(row.Tooltip))
                TooltipHandler.TipRegion(rowRect, row.Tooltip);
            if (Widgets.ButtonInvisible(rowRect) && row.WorldObject != null)
                WorldDomination_UIUtils.JumpToWorldObjectOnMap(row.WorldObject);

            Text.Anchor = TextAnchor.UpperLeft;
            return ly + rowH;
        }

        private static Rect InsetIconDrawRect(Rect outer, float pad = 2f)
        {
            return new Rect(outer.x + pad, outer.y + pad, outer.width - pad * 2f, outer.height - pad * 2f);
        }
    }
}
