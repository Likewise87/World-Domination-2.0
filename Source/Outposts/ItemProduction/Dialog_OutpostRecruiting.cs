using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Recruiting outpost: production-style two-column dialog (stats left, skill picker right).</summary>
    [StaticConstructorOnStartup]
    public class Dialog_OutpostRecruiting : Window
    {
        private static Texture2D cachedRecruitPawnIcon;
        private static Texture2D cachedAnyColonistIcon;

        private readonly WorldObject_WD_Outpost outpost;
        private Vector2 rightScrollPosition;
        private Vector2 leftPartnerScrollPosition;
        private readonly string windowTitleText;

        private readonly List<CachedPartnerRow> partnerRows = new List<CachedPartnerRow>();
        private readonly List<string> xenotypeLines = new List<string>();
        private readonly List<string> pawnKindLines = new List<string>();
        private bool xenotypeSectionExpanded;
        private bool pawnKindSectionExpanded;
        private readonly List<SkillDef> skillCandidates = new List<SkillDef>();
        private readonly bool showSkillSearchBar;
        private readonly OutpostDialogNearbyMonitor nearbyMonitor = new OutpostDialogNearbyMonitor();
        private readonly string nearbyHeaderTooltip;

        private string nearbyHeaderLabel;

        private string skillSearchFilter = "";

        private struct CachedPartnerRow
        {
            public Faction Faction;
            public WorldObject WorldObject;
            public string LabelLine;
            public string Tooltip;
            public bool Contributes;
        }

        private struct CachedSkillRow
        {
            public SkillDef Skill;
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
        private const float SkillRowHeight = NameLabelHeight + FormulaBlockHeight;

        public override Vector2 InitialSize => new Vector2(960f, 758f);

        public Dialog_OutpostRecruiting(WorldObject_WD_Outpost outpost)
        {
            this.outpost = outpost;
            doCloseButton = true;
            doCloseX = true;
            absorbInputAroundWindow = true;
            forcePause = false;
            optionalTitle = null;

            nearbyHeaderTooltip = OutpostTranslationUtil.Key(
                "TSA_WD_Recruiting_NearbyHeaderTip",
                Outpost_Trading.GetNearbyRadiusTiles(outpost).ToString());

            nearbyMonitor.ForceRefresh(outpost, OnNearbyPartnersChanged);

            windowTitleText = OutpostTranslationUtil.Key("TSA_WD_Recruiting_WindowTitle");

            skillCandidates.AddRange(Outpost_Recruiting.GetPrioritySkillCandidates());
            showSkillSearchBar = skillCandidates.Count + 1 > 5;
        }

        private void OnNearbyPartnersChanged()
        {
            RebuildPartnerRows();
            xenotypeLines.Clear();
            pawnKindLines.Clear();
            if (outpost != null)
            {
                xenotypeLines.AddRange(Outpost_Recruiting.GetXenotypePoolDisplayLines(outpost));
                pawnKindLines.AddRange(Outpost_Recruiting.GetPawnKindPoolDisplayLines(outpost));
            }
            nearbyHeaderLabel = Outpost_Dialog_UI.FormatNearbyHeaderLabel(nearbyMonitor.NearbyCount);
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
            var partners = new List<Outpost_Trading.NearbyPartnerInfo>();
            Outpost_Recruiting.CollectSortedNearbyPartners(outpost, partners);
            for (int i = 0; i < partners.Count; i++)
            {
                var p = partners[i];
                partnerRows.Add(new CachedPartnerRow
                {
                    Faction = p.Faction,
                    WorldObject = p.WorldObject,
                    LabelLine = Outpost_Recruiting.FormatPartnerRowLabel(p),
                    Tooltip = Outpost_Recruiting.BuildPartnerRowTooltip(p),
                    Contributes = p.ContributesToFaction
                });
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (outpost == null) return;

            nearbyMonitor.TryRefresh(outpost, OnNearbyPartnersChanged);

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
            string typeLabel = outpost.def?.label ?? "";
            Widgets.Label(new Rect(0f, y, contentWidth, 24f), (outpostName + " (" + typeLabel + ")").Truncate(contentWidth));
            y += 28f;

            y = Outpost_Dialog_UI.DrawProductionPauseBanner(0f, y, contentWidth, outpost);
            y += Outpost_Dialog_UI.AfterPauseBannerGap;
            y = Outpost_Dialog_UI.DrawSkillDiminishingReturnsBanner(0f, y, contentWidth, outpost);

            const float bottomReserve = 44f;
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
            int expectedRecruits = Outpost_Recruiting.ComputeRecruitCount(outpost, avgSocial);
            int snapshotRecruits = Outpost_Recruiting.ComputeRecruitCount(outpost, snapshotSocial);
            string detailedMathAvg = Outpost_Recruiting.GetDetailedMathTooltip(outpost, avgSocial);
            string detailedMathSnapshot = Outpost_Recruiting.GetDetailedMathTooltip(outpost, snapshotSocial);
            string skillLabel = SkillDefOf.Social.label;
            string snapshotSocialDisplay = OutpostSkillScaling.FormatRawEffective(snapshotSocialRaw);

            const float lineH = Outpost_Dialog_UI.OutcomeLineH;
            const float boxPad = Outpost_Dialog_UI.OutcomeBoxPad;
            float cycleDaysLeft = outpost.ProductionTicksLeftForDisplay / 60000f;
            string expectedValueText = OutpostTranslationUtil.Key(
                "TSA_WD_Recruiting_Info_ExpectedCount",
                expectedRecruits.ToString(),
                OutpostTranslationUtil.Key("TSA_WD_Production_Recruits"));
            float valueH = Outpost_Dialog_UI.MeasureTextLinesHeight(expectedValueText);
            float boxH = boxPad * 2f + (lineH + 2f) + lineH + lineH + valueH;
            Rect boxRect = new Rect(lx, ly, lw, boxH);
            Outpost_Dialog_UI.DrawOutcomeBox(boxRect);
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
            TooltipHandler.TipRegion(avgRect, OutpostTranslationUtil.Key("TSA_WD_Recruiting_Info_AvgSocialTip", avgSocial.ToString("F0")));
            cy += lineH;

            float outBlockTop = cy;
            Widgets.Label(new Rect(ix, cy, iw, lineH), OutpostTranslationUtil.Key("TSA_WD_Recruiting_Info_ExpectedOutput"));
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
            string curTip = OutpostTranslationUtil.Key("TSA_WD_Recruiting_Info_CurrentSocialTip", snapshotSocialRaw.ToString("F0"));
            if (OutpostSkillScaling.IsDiminished(snapshotSocialRaw))
                curTip = curTip + "\n\n" + OutpostSkillScaling.BuildBandBreakdownTip(snapshotSocialRaw);
            TooltipHandler.TipRegion(curRect, curTip);
            ly += lineH;

            float snapBlockTop = ly;
            GUI.color = Outpost_Dialog_UI.TheoreticalLabelColor;
            Widgets.Label(new Rect(lx, ly, lw, lineH), OutpostTranslationUtil.Key("TSA_WD_Production_Info_OutputNow"));
            GUI.color = Color.white;
            ly += lineH;
            string snapshotValueText = OutpostTranslationUtil.Key(
                "TSA_WD_Recruiting_Info_ExpectedCount",
                snapshotRecruits.ToString(),
                OutpostTranslationUtil.Key("TSA_WD_Production_Recruits"));
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
            const float selIconSize = 24f;
            Texture2D selIcon = GetSkillRowIcon(outpost.SelectedRecruitPrioritySkill);
            string selName = Outpost_Recruiting.GetPrioritySkillDisplayLine(outpost);
            if (selIcon != null)
            {
                Rect selIconRect = new Rect(lx, ly, selIconSize, selIconSize);
                Widgets.DrawTextureFitted(selIconRect, selIcon, 1f);
            }
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(new Rect(lx + selIconSize + 6f, ly, lw - selIconSize - 6f, 28f),
                OutpostTranslationUtil.Key("TSA_WD_Recruiting_SelectedTraining", selName));
            Text.Anchor = TextAnchor.MiddleLeft;
            ly += 32f + 8f;

            ly = DrawCollapsiblePoolSection(
                lx, ly, lw,
                OutpostTranslationUtil.Key("TSA_WD_Recruiting_XenotypePoolHeader"),
                OutpostTranslationUtil.Key("TSA_WD_Recruiting_XenotypePoolTip"),
                OutpostTranslationUtil.Key("TSA_WD_Recruiting_XenotypeNone"),
                xenotypeLines,
                ref xenotypeSectionExpanded);

            ly = DrawCollapsiblePoolSection(
                lx, ly, lw,
                OutpostTranslationUtil.Key("TSA_WD_Recruiting_PawnKindPoolHeader"),
                OutpostTranslationUtil.Key("TSA_WD_Recruiting_PawnKindPoolTip"),
                OutpostTranslationUtil.Key("TSA_WD_Recruiting_PawnKindNone"),
                pawnKindLines,
                ref pawnKindSectionExpanded);

            Rect srcHdr = new Rect(lx, ly, lw, LineH);
            Widgets.Label(srcHdr, OutpostTranslationUtil.Key("TSA_WD_Recruiting_XenotypeSourcesHeader"));
            TooltipHandler.TipRegion(srcHdr, OutpostTranslationUtil.Key("TSA_WD_Recruiting_XenotypeSourcesTip"));
            ly += LineH + 2f;

            string footerRuleText = Outpost_Recruiting.GetNeighborBonusFooterRuleText();
            Text.Font = GameFont.Tiny;
            float footerRuleH = Mathf.Max(36f, Text.CalcHeight(footerRuleText, lw));
            const float footerSummaryLineH = 22f;
            float neighborFooterH = footerRuleH + footerSummaryLineH * 2f + 14f;
            float partnersTop = ly;
            float partnersBottom = leftArea.yMax - 8f - neighborFooterH;
            // Prefer room for at least three settlement rows without scrolling.
            float partnersH = Mathf.Max(3f * (LineH + RowPadding), partnersBottom - partnersTop);
            float partnerContentH = partnerRows.Count == 0 ? LineH : partnerRows.Count * (LineH + RowPadding);
            Rect partnerScrollOuter = new Rect(lx, partnersTop, lw, partnersH);
            Rect partnerView = new Rect(0f, 0f, lw - 16f, partnerContentH);
            Widgets.BeginScrollView(partnerScrollOuter, ref leftPartnerScrollPosition, partnerView);

            float ply = 0f;
            if (partnerRows.Count == 0)
            {
                Widgets.Label(new Rect(0f, ply, partnerView.width, LineH), OutpostTranslationUtil.Key("TSA_WD_Recruiting_Math_None"));
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
            TooltipHandler.TipRegion(ruleRect, Outpost_Recruiting.GetNeighborBonusTierPointsDetailText());
            fy += footerRuleH + 4f;
            Widgets.Label(new Rect(lx, fy, lw, footerSummaryLineH), Outpost_Recruiting.GetNeighborBonusFooterTotalLine(outpost));
            fy += footerSummaryLineH;
            GUI.color = new Color(0.4f, 0.8f, 1f);
            Widgets.Label(new Rect(lx, fy, lw, footerSummaryLineH), Outpost_Recruiting.GetNeighborBonusFooterResultLine(outpost));
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }

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

        /// <summary>Clickable ▶/▼ header in white; breakdown lines only when expanded.</summary>
        private static float DrawCollapsiblePoolSection(
            float x,
            float y,
            float width,
            string header,
            string tooltip,
            string emptyFallback,
            List<string> lines,
            ref bool expanded)
        {
            string arrow = expanded ? "▼ " : "▶ ";
            string headerText = arrow + header;

            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Rect hdrRect = new Rect(x, y, width, LineH);
            if (Mouse.IsOver(hdrRect)) Widgets.DrawHighlight(hdrRect);
            Widgets.Label(hdrRect, headerText);
            if (!string.IsNullOrEmpty(tooltip))
                TooltipHandler.TipRegion(hdrRect, tooltip);
            if (Widgets.ButtonInvisible(hdrRect))
                expanded = !expanded;
            y += LineH;

            if (expanded)
            {
                if (lines == null || lines.Count == 0)
                {
                    Widgets.Label(new Rect(x + 8f, y, width - 16f, LineH), emptyFallback);
                    y += LineH;
                }
                else
                {
                    for (int i = 0; i < lines.Count; i++)
                    {
                        Widgets.Label(new Rect(x + 8f, y, width - 16f, LineH), lines[i]);
                        y += LineH;
                    }
                }
            }

            return y + 6f;
        }

        private void DrawRightColumn(Rect rightArea)
        {
            float y = rightArea.y;
            const float itemSearchBarH = 28f;
            const float itemSearchGap = 6f;

            GUI.color = new Color(0.75f, 0.82f, 1f);
            Widgets.Label(new Rect(rightArea.x, y, rightArea.width, 22f), OutpostTranslationUtil.Key("TSA_WD_Recruiting_ChooseTraining"));
            GUI.color = Color.white;
            y += 24f;

            if (showSkillSearchBar)
            {
                string oldFilter = skillSearchFilter;
                Rect searchRect = new Rect(rightArea.x, y, rightArea.width - 16f, itemSearchBarH);
                skillSearchFilter = Widgets.TextField(searchRect, skillSearchFilter);
                if (skillSearchFilter != oldFilter)
                    rightScrollPosition = Vector2.zero;
                if (string.IsNullOrEmpty(skillSearchFilter))
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.4f);
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Text.Font = GameFont.Tiny;
                    Widgets.Label(searchRect, OutpostTranslationUtil.Key("TSA_WD_Production_SearchPlaceholder"));
                    Text.Font = GameFont.Small;
                    Text.Anchor = TextAnchor.UpperLeft;
                    GUI.color = Color.white;
                }
                y += itemSearchBarH + itemSearchGap;
            }

            var ordered = BuildOrderedSkillRows();
            float scrollHeight = 8f;
            foreach (var row in ordered)
            {
                if (SkillRowMatchesSearch(row)) scrollHeight += SkillRowHeight + RowPadding;
            }

            Rect scrollOuter = new Rect(rightArea.x, y, rightArea.width, rightArea.height - (y - rightArea.y));
            Rect viewRect = new Rect(0f, 0f, rightArea.width - 16f, scrollHeight);
            Widgets.BeginScrollView(scrollOuter, ref rightScrollPosition, viewRect);

            float curY = 0f;
            int visibleRow = 0;
            foreach (var row in ordered)
            {
                if (!SkillRowMatchesSearch(row)) continue;

                Rect rowRect = new Rect(0f, curY, viewRect.width, SkillRowHeight + RowPadding);
                if (visibleRow % 2 == 0) Widgets.DrawHighlight(rowRect);
                bool isSelected = SkillRowIsSelected(row);
                Outpost_Dialog_UI.DrawSelectedRowTint(rowRect, isSelected);
                float rowContentY = curY + (rowRect.height - SkillRowHeight) / 2f;

                Texture2D icon = GetSkillRowIcon(row.Skill);
                Rect iconRect = new Rect(IconPadding, rowContentY + (SkillRowHeight - 32f) / 2f, 32f, 32f);
                if (icon != null) Widgets.DrawTextureFitted(iconRect, icon, 1f);

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
                    outpost.SetSelectedRecruitPriority(row.Skill);

                curY += SkillRowHeight + RowPadding;
                visibleRow++;
            }

            Widgets.EndScrollView();
        }

        private List<CachedSkillRow> BuildOrderedSkillRows()
        {
            var rows = new List<CachedSkillRow>(skillCandidates.Count + 1);
            var selected = outpost.SelectedRecruitPrioritySkill;

            void AddRow(SkillDef skill, string label)
            {
                rows.Add(new CachedSkillRow
                {
                    Skill = skill,
                    Label = label,
                    Formula = Outpost_Recruiting.GetPrioritySkillRowFormula(skill),
                    Tooltip = Outpost_Recruiting.GetPrioritySkillRowTooltip(skill)
                });
            }

            if (selected == null)
                AddRow(null, OutpostTranslationUtil.Key("TSA_WD_Recruiting_PriorityAny"));
            else
                AddRow(selected, selected.LabelCap);

            if (selected != null)
                AddRow(null, OutpostTranslationUtil.Key("TSA_WD_Recruiting_PriorityAny"));

            for (int i = 0; i < skillCandidates.Count; i++)
            {
                var sk = skillCandidates[i];
                if (sk == selected) continue;
                AddRow(sk, sk.LabelCap);
            }

            return rows;
        }

        private bool SkillRowIsSelected(CachedSkillRow row)
        {
            if (row.Skill == null) return outpost.SelectedRecruitPrioritySkill == null;
            return outpost.SelectedRecruitPrioritySkill == row.Skill;
        }

        private bool SkillRowMatchesSearch(CachedSkillRow row)
        {
            if (string.IsNullOrEmpty(skillSearchFilter)) return true;
            return row.Label.IndexOf(skillSearchFilter, StringComparison.OrdinalIgnoreCase) >= 0
                || (row.Formula != null && row.Formula.IndexOf(skillSearchFilter, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static Rect InsetIconDrawRect(Rect outer, float pad = 2f)
        {
            return new Rect(outer.x + pad, outer.y + pad, outer.width - pad * 2f, outer.height - pad * 2f);
        }

        private static Texture2D GetRecruitPawnIcon()
        {
            if (cachedRecruitPawnIcon == null)
                cachedRecruitPawnIcon = ContentFinder<Texture2D>.Get("UI/Commands/RecruitPawn", false) ?? TexCommand.Replant;
            return cachedRecruitPawnIcon;
        }

        private static Texture2D GetAnyColonistIcon()
        {
            if (cachedAnyColonistIcon == null)
                cachedAnyColonistIcon = TexCommand.Replant;
            return cachedAnyColonistIcon;
        }

        private static Texture2D GetSkillRowIcon(SkillDef skill)
        {
            if (skill == null) return GetAnyColonistIcon();
            return GetRecruitPawnIcon();
        }
    }
}
