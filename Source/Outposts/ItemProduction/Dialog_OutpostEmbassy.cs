using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Embassy outpost: single-column dialog (cycle outcome, Social, nearby settlements).</summary>
    public class Dialog_OutpostEmbassy : Window
    {
        private readonly WorldObject_WD_Outpost outpost;
        private Vector2 partnerScrollPosition;
        private readonly string windowTitleText;
        private readonly string nearbyHeaderTooltip;

        private readonly List<CachedPartnerRow> partnerRows = new List<CachedPartnerRow>();
        private readonly OutpostDialogNearbyMonitor nearbyMonitor = new OutpostDialogNearbyMonitor();

        private string nearbyHeaderLabel;
        private int embassyNearbyCount;

        private struct CachedPartnerRow
        {
            public Faction Faction;
            public WorldObject WorldObject;
            public string LabelLine;
            public string Tooltip;
            public bool Contributes;
        }

        private const float PartnerIconSize = 28f;
        private const float RowPadding = 6f;
        private const float LineH = 26f;

        public override Vector2 InitialSize => new Vector2(540f, 780f);

        public Dialog_OutpostEmbassy(WorldObject_WD_Outpost outpost)
        {
            this.outpost = outpost;
            doCloseButton = true;
            doCloseX = true;
            absorbInputAroundWindow = true;
            forcePause = false;
            optionalTitle = null;

            nearbyHeaderTooltip = OutpostTranslationUtil.Key(
                "TSA_WD_Embassy_NearbyHeaderTip",
                Outpost_Embassy.GetNearbyRadiusTiles(outpost).ToString());

            nearbyMonitor.ForceRefresh(outpost, RebuildRows);
            windowTitleText = OutpostTranslationUtil.Key("TSA_WD_Embassy_WindowTitle");
        }

        public override void PreClose()
        {
            base.PreClose();
            Window_OutpostOverview.InvalidateCache();
        }

        private void RebuildRows()
        {
            partnerRows.Clear();
            if (outpost == null) return;

            Outpost_Embassy.InvalidateProbeCache(outpost);
            var partners = new List<Outpost_Embassy.NearbySettlementInfo>();
            Outpost_Embassy.CollectSortedNearbySettlements(outpost, partners);
            embassyNearbyCount = partners.Count;

            for (int i = 0; i < partners.Count; i++)
            {
                var p = partners[i];
                partnerRows.Add(new CachedPartnerRow
                {
                    Faction = p.Faction,
                    WorldObject = p.Settlement,
                    LabelLine = Outpost_Embassy.FormatPartnerRowLabel(p),
                    Tooltip = Outpost_Embassy.BuildPartnerRowTooltip(p),
                    Contributes = p.ContributesToFaction
                });
            }

            nearbyHeaderLabel = Outpost_Dialog_UI.FormatNearbyHeaderLabel(embassyNearbyCount);
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (outpost == null) return;

            nearbyMonitor.TryRefresh(outpost, RebuildRows);

            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            const float sidePad = 4f;
            float contentWidth = inRect.width - sidePad * 2f;
            const float closeXLeftInset = 22f;
            float rightContentRight = inRect.width - closeXLeftInset;
            float headerSlotWidth = 165f;
            float slotX = rightContentRight - headerSlotWidth;

            float y = 0f;
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(sidePad, y, slotX - sidePad - 8f, Outpost_Dialog_UI.DialogTitleHeight), windowTitleText);
            Text.Anchor = TextAnchor.MiddleRight;
            Text.Font = GameFont.Small;
            GUI.color = Outpost_Dialog_UI.NearbyCountColor(embassyNearbyCount);
            Rect slotRect = new Rect(slotX, y + Outpost_Dialog_UI.DialogHeaderSlotTopInset, headerSlotWidth, Outpost_Dialog_UI.DialogHeaderSlotHeight);
            Widgets.Label(slotRect, nearbyHeaderLabel);
            TooltipHandler.TipRegion(slotRect, nearbyHeaderTooltip);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            y += Outpost_Dialog_UI.DialogTitleRowAdvance;

            string outpostName = outpost.Name ?? outpost.Label;
            string typeLabel = outpost.def?.label ?? OutpostTranslationUtil.Key("TSA_WD_Outpost_GenericLabel");
            Widgets.Label(new Rect(sidePad, y, contentWidth, 24f), (outpostName + " (" + typeLabel + ")").Truncate(contentWidth));
            y += 28f;

            y = Outpost_Dialog_UI.DrawProductionPauseBanner(sidePad, y, contentWidth, outpost);
            y += Outpost_Dialog_UI.AfterPauseBannerGap;
            y = Outpost_Dialog_UI.DrawSkillDiminishingReturnsBanner(sidePad, y, contentWidth, outpost);

            const float bottomReserve = 48f;
            Rect body = new Rect(sidePad, y, contentWidth, inRect.height - bottomReserve - y);
            DrawBody(body);
        }

        private void DrawBody(Rect area)
        {
            float lx = area.x;
            float lw = area.width;
            float ly = area.y;
            Text.Anchor = TextAnchor.MiddleLeft;

            float snapshotSocialRaw = Outpost_Embassy.GetDeliveryDrivingCapacityRaw(outpost);
            float snapshotSocial = OutpostSkillScaling.ToEffective(snapshotSocialRaw);
            float avgSocial = outpost.GetCapacityForYieldPreview();
            string detailedMathAvg = Outpost_Embassy.GetDetailedMathTooltip(outpost, avgSocial);
            string detailedMathSnapshot = Outpost_Embassy.GetDetailedMathTooltip(outpost, snapshotSocial);
            string skillLabel = SkillDefOf.Social.label;
            string snapshotSocialDisplay = OutpostSkillScaling.FormatRawEffective(snapshotSocialRaw);

            const float lineH = Outpost_Dialog_UI.OutcomeLineH;
            const float boxPad = Outpost_Dialog_UI.OutcomeBoxPad;
            float cycleDaysLeft = outpost.ProductionTicksLeftForDisplay / 60000f;
            string expectedValueText = Outpost_Embassy.FormatDialogExpectedOutput(outpost, avgSocial, 3);
            string snapshotValueText = Outpost_Embassy.FormatDialogExpectedOutput(outpost, snapshotSocial, 3);
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
            TooltipHandler.TipRegion(avgRect, OutpostTranslationUtil.Key("TSA_WD_Embassy_Info_AvgSocialTip", avgSocial.ToString("F0")));
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
            string curTip = OutpostTranslationUtil.Key("TSA_WD_Embassy_Info_CurrentSocialTip", snapshotSocialRaw.ToString("F0"));
            if (OutpostSkillScaling.IsDiminished(snapshotSocialRaw))
                curTip = curTip + "\n\n" + OutpostSkillScaling.BuildBandBreakdownTip(snapshotSocialRaw);
            TooltipHandler.TipRegion(curRect, curTip);
            ly += lineH;

            float snapBlockTop = ly;
            GUI.color = Outpost_Dialog_UI.TheoreticalLabelColor;
            Widgets.Label(new Rect(lx, ly, lw, lineH), OutpostTranslationUtil.Key("TSA_WD_Production_Info_OutputNow"));
            GUI.color = Color.white;
            ly += lineH;
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

            GUI.color = new Color(0.72f, 0.72f, 0.72f);
            Rect srcHdr = new Rect(lx, ly, lw, LineH);
            Widgets.Label(srcHdr, OutpostTranslationUtil.Key("TSA_WD_Embassy_NearbySettlementsHeader"));
            TooltipHandler.TipRegion(srcHdr, OutpostTranslationUtil.Key("TSA_WD_Embassy_NearbySettlementsTip"));
            GUI.color = Color.white;
            ly += LineH + 2f;

            string footerRuleText = OutpostTranslationUtil.Key("TSA_WD_Embassy_FooterRule");
            Text.Font = GameFont.Tiny;
            float footerRuleH = Mathf.Max(48f, Text.CalcHeight(footerRuleText, lw));
            float neighborFooterH = footerRuleH + 10f;

            float partnersTop = ly;
            float maxPartnersH = Mathf.Max(LineH, area.yMax - partnersTop - neighborFooterH);
            float partnerContentH = partnerRows.Count == 0 ? LineH : partnerRows.Count * (LineH + RowPadding);
            // Fit to content so footer sits under the list (no empty zebra void).
            float partnersH = Mathf.Min(partnerContentH + 4f, maxPartnersH);
            bool needsScroll = partnerContentH > partnersH + 0.5f;
            float viewW = needsScroll ? lw - 16f : lw;
            Rect partnerScrollOuter = new Rect(lx, partnersTop, lw, partnersH);
            Rect partnerView = new Rect(0f, 0f, viewW, partnerContentH);
            Widgets.BeginScrollView(partnerScrollOuter, ref partnerScrollPosition, partnerView);

            float ply = 0f;
            if (partnerRows.Count == 0)
            {
                Widgets.Label(new Rect(0f, ply, partnerView.width, LineH), OutpostTranslationUtil.Key("TSA_WD_Embassy_Math_None"));
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

                // Zebra under the group, then one selected tint over the best block (academy style).
                for (int i = 0; i < partnerRows.Count; i++)
                {
                    Rect rowRect = new Rect(0f, i * rowH, partnerView.width, rowH);
                    if (i % 2 == 0) Widgets.DrawHighlight(rowRect);
                }
                if (bestCount > 0)
                    Outpost_Dialog_UI.DrawSelectedRowTint(bestGroupRect, true);

                for (int i = 0; i < partnerRows.Count; i++)
                    ply = DrawPartnerRowInScroll(ply, partnerView.width, partnerRows[i]);

                // One frame around all best-per-faction rows (they sit together at the top).
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
            TooltipHandler.TipRegion(ruleRect, OutpostTranslationUtil.Key("TSA_WD_Embassy_TierPointsTip"));
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
            // Best (contributing) rows match academy selected text; covered rows stay dimmed.
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
