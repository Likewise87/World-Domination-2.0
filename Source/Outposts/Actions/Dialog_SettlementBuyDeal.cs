using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace TSA_WorldDomination
{
    /// <summary>Deal meter UI: fill tier ask with colony/warehouse goods + optional goodwill.</summary>
    public class Dialog_SettlementBuyDeal : Window
    {
        private readonly Settlement settlement;
        private readonly float askSilver;
        private readonly List<ThingDefCountClass> allRows = new List<ThingDefCountClass>();
        private readonly Dictionary<string, int> offered = new Dictionary<string, int>();
        private readonly Dictionary<string, string> countEditBuffers = new Dictionary<string, string>();
        private readonly Dictionary<string, float> unitValueCache = new Dictionary<string, float>();
        private Vector2 scrollPosition;
        private int goodwillOffer;
        private string filter = "";
        private string sortColumn = "SilverPer";
        private bool sortAscending;

        private const float RowIconSize = 28f;
        private const float RowHeight = 36f;
        private const float HeaderHeight = 30f;
        private const float BtnW = 28f;
        private const float MaxBtnW = 40f;
        private const float CountColW = 56f;
        private const float BtnGap = 4f;
        private const float ColIcon = 40f;
        private const float ColStar = SettlementCaravanDealUi.ColStar;
        private const float ColName = 228f;
        private const float ColQty = 90f;
        private const float ColSilverPer = 100f;
        private const float ColSilverTotal = 100f;
        private static float ControlsWidth => BtnW + CountColW + BtnW + BtnGap + BtnW + BtnGap + MaxBtnW;
        private static float TableContentWidth =>
            ColIcon + ColStar + ColName + ColQty + ColSilverPer + ControlsWidth + ColSilverTotal;

        public override Vector2 InitialSize => new Vector2(1040f, 760f);

        public Dialog_SettlementBuyDeal(Settlement settlement)
        {
            this.settlement = settlement;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            doCloseX = true;
            doCloseButton = false;

            var viral = settlement?.GetComponent<CompViralSpread>();
            askSilver = SettlementBuyUtility.GetAskSilver(viral?.tier ?? SettlementTier.T1);
            allRows.AddRange(SettlementBuyUtility.BuildAvailablePool(settlement.Tile));
            SortRows();
        }

        private float GoodsMarketValue
        {
            get
            {
                float total = 0f;
                for (int i = 0; i < allRows.Count; i++)
                {
                    var row = allRows[i];
                    string key = CompOutpostWarehouse.StockKey(row);
                    int pick = GetOffer(key);
                    if (pick <= 0) continue;
                    total += UnitValue(row) * pick;
                }
                return total;
            }
        }

        private float GoodwillMarketValue => goodwillOffer * SettlementBuyUtility.SilverPerGoodwill;
        private float TotalOffer => GoodsMarketValue + GoodwillMarketValue;
        private bool HasOrigin => SettlementBuyUtility.HasPlayerPaymentOrigin(settlement?.Tile ?? -1, out _);
        private bool CanConfirm =>
            settlement != null
            && !settlement.Destroyed
            && SettlementBuyUtility.MeetsAsk(TotalOffer, askSilver)
            && HasOrigin;

        private string ConfirmDisableReason
        {
            get
            {
                if (settlement == null || settlement.Destroyed)
                    return "TSA_WD_BuySettlement_TargetGone".Translate();
                if (!HasOrigin)
                {
                    SettlementBuyUtility.HasPlayerPaymentOrigin(settlement.Tile, out string r);
                    return r ?? "TSA_WD_BuySettlement_NoOrigin".Translate();
                }
                if (!SettlementBuyUtility.MeetsAsk(TotalOffer, askSilver))
                    return "TSA_WD_BuySettlement_UnderAsk".Translate();
                return null;
            }
        }

        private static readonly Color BarUnder = new Color(0.85f, 0.25f, 0.22f);
        private static readonly Color BarOk = new Color(0.35f, 0.75f, 0.4f);
        private static readonly Color BarHeavyOver = new Color(0.95f, 0.55f, 0.15f);
        private const float MildOverspendShare = 0.05f;

        private Color MeterFillColor
        {
            get
            {
                if (!SettlementBuyUtility.MeetsAsk(TotalOffer, askSilver))
                    return BarUnder;
                float overRatio = askSilver > 0f
                    ? SettlementBuyUtility.RoundedOverspend(TotalOffer, askSilver) / askSilver
                    : 0f;
                return overRatio > MildOverspendShare ? BarHeavyOver : BarOk;
            }
        }

        private const float BoxLineH = 24f;
        private const float MeterH = 18f;
        private const float MeterGap = 8f;
        private const float GoodwillSliderH = 28f;
        private const float GoodsCaptionGap = 12f;
        /// <summary>Gap after the left status label before Remaining / Min-retain (shared by both top boxes).</summary>
        private const float StatusSecondColGap = 100f;

        private float UnitValue(ThingDefCountClass row)
        {
            string key = CompOutpostWarehouse.StockKey(row);
            if (unitValueCache.TryGetValue(key, out float cached))
                return cached;
            float v = SettlementBuyUtility.UnitMarketValue(row);
            unitValueCache[key] = v;
            return v;
        }

        private static void LabelAnchored(Rect rect, string text, TextAnchor anchor)
        {
            TextAnchor prev = Text.Anchor;
            Text.Anchor = anchor;
            Widgets.Label(rect, text);
            Text.Anchor = prev;
        }

        /// <summary>X where Remaining (price box) and Min-retain (goodwill box) start, under the same column.</summary>
        private float StatusSecondColumnX(float innerLeftX)
        {
            string offeredText = "TSA_WD_BuySettlement_Offered".Translate(
                SettlementBuyUtility.RoundSilver(TotalOffer).ToString("F2"));
            return innerLeftX + Text.CalcSize(offeredText).x + StatusSecondColGap;
        }

        private static string DealPriceTooltip()
        {
            return "TSA_WD_BuySettlement_DealPriceTip".Translate() + "\n\n" +
                "TSA_WD_BuySettlement_DealPriceTipTiers".Translate(
                    SettlementBuyUtility.GetAskSilver(SettlementTier.T1).ToString("F0"),
                    SettlementBuyUtility.GetAskSilver(SettlementTier.T2).ToString("F0"),
                    SettlementBuyUtility.GetAskSilver(SettlementTier.T3).ToString("F0"),
                    SettlementBuyUtility.GetAskSilver(SettlementTier.T4).ToString("F0"));
        }

        private static void DrawTopBoxFrame(Rect rect)
        {
            Widgets.DrawBox(rect);
        }

        private float DrawPriceBox(float x, float y, float width)
        {
            const float boxPad = Outpost_Dialog_UI.OutcomeBoxPad;
            float innerH = BoxLineH + MeterGap + MeterH + MeterGap + BoxLineH;
            float boxH = boxPad * 2f + innerH;
            DrawTopBoxFrame(new Rect(x, y, width, boxH));

            float cy = y + boxPad;
            float ix = x + boxPad;
            float iw = width - boxPad * 2f;

            string priceText = "TSA_WD_BuySettlement_DealPrice".Translate(askSilver.ToString("F0"));
            Rect priceRect = new Rect(ix, cy, iw, BoxLineH);
            GUI.color = Outpost_Dialog_UI.CycleTimerColor;
            LabelAnchored(priceRect, priceText, TextAnchor.MiddleLeft);
            GUI.color = Color.white;
            TooltipHandler.TipRegion(priceRect, DealPriceTooltip());
            cy += BoxLineH + MeterGap;

            float barMax = askSilver * 2f;
            Rect meterBg = new Rect(ix, cy, iw, MeterH);
            Widgets.DrawBoxSolid(meterBg, new Color(0.15f, 0.15f, 0.15f));
            float fill = barMax > 0f ? Mathf.Clamp01(TotalOffer / barMax) : 0f;
            Widgets.DrawBoxSolid(new Rect(meterBg.x, meterBg.y, meterBg.width * fill, meterBg.height), MeterFillColor);
            float markerX = meterBg.x + meterBg.width * 0.5f;
            Widgets.DrawBoxSolid(new Rect(markerX - 1f, meterBg.y - 2f, 2f, meterBg.height + 4f), Color.white);
            cy += MeterH + MeterGap;

            float overspend = SettlementBuyUtility.RoundedOverspend(TotalOffer, askSilver);
            float remaining = SettlementBuyUtility.RoundedRemaining(TotalOffer, askSilver);
            string offeredText = "TSA_WD_BuySettlement_Offered".Translate(SettlementBuyUtility.RoundSilver(TotalOffer).ToString("F2"));
            string rightText = overspend > 0f
                ? "TSA_WD_BuySettlement_Overspending".Translate(overspend.ToString("F2"))
                : "TSA_WD_BuySettlement_Remaining".Translate(remaining.ToString("F2"));
            float secondColX = StatusSecondColumnX(ix);
            LabelAnchored(new Rect(ix, cy, Mathf.Max(1f, secondColX - ix - 8f), BoxLineH), offeredText, TextAnchor.MiddleLeft);
            Color prev = GUI.color;
            if (overspend > 0f && askSilver > 0f && overspend / askSilver > MildOverspendShare)
                GUI.color = BarHeavyOver;
            LabelAnchored(new Rect(secondColX, cy, Mathf.Max(1f, ix + iw - secondColX), BoxLineH), rightText, TextAnchor.MiddleLeft);
            GUI.color = prev;

            return y + boxH;
        }

        private float DrawGoodwillBox(float x, float y, float width, int currentGw)
        {
            const float boxPad = Outpost_Dialog_UI.OutcomeBoxPad;
            int minGw = SettlementBuyUtility.GoodwillFloor;
            int maxGwByFloor = Mathf.Max(0, currentGw - minGw);
            int sliderMax = Mathf.Min(200, maxGwByFloor);
            goodwillOffer = Mathf.Clamp(goodwillOffer, 0, sliderMax);

            float innerH = BoxLineH;
            if (sliderMax > 0)
                innerH += MeterGap + GoodwillSliderH;
            else
                innerH += MeterGap + BoxLineH;

            float boxH = boxPad * 2f + innerH;
            DrawTopBoxFrame(new Rect(x, y, width, boxH));

            float cy = y + boxPad;
            float ix = x + boxPad;
            float iw = width - boxPad * 2f;

            if (sliderMax > 0)
            {
                Rect gwRow = new Rect(ix, cy, iw, GoodwillSliderH);
                string payLabel = "Pay with Goodwill: " + goodwillOffer.ToString() + " (" + GoodwillMarketValue.ToString("F2") + ")";
                float labelW = Text.CalcSize(payLabel).x + 8f;
                LabelAnchored(new Rect(gwRow.x, gwRow.y, labelW, gwRow.height), payLabel, TextAnchor.MiddleLeft);
                Rect sliderRect = new Rect(gwRow.x + labelW, gwRow.y, Mathf.Max(1f, gwRow.width - labelW), gwRow.height);
                goodwillOffer = Mathf.RoundToInt(Widgets.HorizontalSlider(sliderRect, goodwillOffer, 0f, sliderMax, true));
                TooltipHandler.TipRegion(gwRow,
                    "TSA_WD_BuySettlement_GoodwillRateTip".Translate(
                        SettlementBuyUtility.SilverPerGoodwill.ToString("F0")));
                cy += GoodwillSliderH;
            }
            else
            {
                GUI.color = Color.gray;
                LabelAnchored(new Rect(ix, cy, iw, BoxLineH),
                    "TSA_WD_BuySettlement_GoodwillNone".Translate(minGw.ToString(), currentGw.ToString()),
                    TextAnchor.MiddleLeft);
                GUI.color = Color.white;
                cy += BoxLineH;
            }

            cy += MeterGap;
            float secondColX = StatusSecondColumnX(ix);
            string currentText = "TSA_WD_BuySettlement_GoodwillCurrent".Translate(currentGw.ToString());
            string minText = "TSA_WD_BuySettlement_GoodwillMinRetain".Translate(minGw.ToString());
            // Current goodwill lines up under Remaining; min-retain continues on the same row to its right.
            float currentW = Text.CalcSize(currentText).x;
            LabelAnchored(new Rect(secondColX, cy, currentW, BoxLineH), currentText, TextAnchor.MiddleLeft);
            float minX = secondColX + currentW + StatusSecondColGap;
            LabelAnchored(new Rect(minX, cy, Mathf.Max(1f, ix + iw - minX), BoxLineH), minText, TextAnchor.MiddleLeft);
            cy += BoxLineH;

            if (sliderMax > 0)
            {
                TooltipHandler.TipRegion(new Rect(ix, y + boxPad, iw, GoodwillSliderH),
                    "TSA_WD_BuySettlement_GoodwillOffer".Translate(
                        goodwillOffer.ToString(),
                        GoodwillMarketValue.ToString("F2"),
                        sliderMax.ToString()));
            }

            return y + boxH;
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (settlement == null || settlement.Destroyed)
            {
                Close();
                return;
            }

            float y = 0f;
            var viral = settlement.GetComponent<CompViralSpread>();
            SettlementTier tier = viral?.tier ?? SettlementTier.T1;
            int tierNum = (int)tier + 1;

            Text.Font = GameFont.Medium;
            LabelAnchored(new Rect(0f, y, inRect.width, 32f),
                "TSA_WD_BuySettlement_DealTitle".Translate(tierNum.ToString(), settlement.LabelCap),
                TextAnchor.MiddleLeft);
            y += 36f;
            Text.Font = GameFont.Small;

            y = DrawPriceBox(0f, y, inRect.width);
            y += Outpost_Dialog_UI.OutcomeBoxGap;

            int currentGw = settlement.Faction != null
                ? GoodwillChangeNotifier.GetPlayerGoodwill(settlement.Faction)
                : 0;
            y = DrawGoodwillBox(0f, y, inRect.width, currentGw);
            y += Outpost_Dialog_UI.OutcomeBoxGap + GoodsCaptionGap;

            LabelAnchored(new Rect(0f, y, inRect.width, BoxLineH),
                "TSA_WD_BuySettlement_PayWithGoods".Translate(),
                TextAnchor.MiddleLeft);
            y += BoxLineH + 4f;

            LabelAnchored(new Rect(0f, y, 70f, 24f), "TSA_WD_BuySettlement_Filter".Translate(), TextAnchor.MiddleLeft);
            filter = Widgets.TextField(new Rect(74f, y, 220f, 24f), filter ?? "");
            SettlementCaravanDealUi.DrawCategoryDropdown(
                new Rect(74f + 220f + 8f, y, SettlementCaravanDealUi.CategoryFilterWidth, 24f));

            float btnH = 28f;
            float resetW = 110f;
            float fillW = 180f;
            float btnTop = y + (24f - btnH) / 2f;
            Rect resetRect = new Rect(inRect.width - resetW - fillW - 8f, btnTop, resetW, btnH);
            Rect fillRect = new Rect(inRect.width - fillW, btnTop, fillW, btnH);
            if (Widgets.ButtonText(resetRect, "TSA_WD_BuySettlement_ResetAll".Translate()))
                ResetAllOffers();
            if (Widgets.ButtonText(fillRect, "TSA_WD_BuySettlement_AssignExpensiveFirst".Translate()))
                AssignExpensiveFirst();
            TooltipHandler.TipRegion(fillRect, "TSA_WD_BuySettlement_AssignExpensiveFirstTip".Translate());
            y += 30f;

            float bottomH = 48f;
            float tableTop = y;
            float tableBottom = inRect.height - bottomH - 8f;
            float listTop = tableTop + HeaderHeight + 4f;
            Rect headerRect = new Rect(0f, tableTop, inRect.width, HeaderHeight);
            DrawTableHeader(headerRect);
            Widgets.DrawLineHorizontal(0f, tableTop + HeaderHeight, inRect.width);

            Rect listOut = new Rect(0f, listTop, inRect.width, tableBottom - listTop);
            var visible = BuildVisibleList();
            float tableW = Mathf.Max(listOut.width - 16f, TableContentWidth);
            Rect listView = new Rect(0f, 0f, tableW, Mathf.Max(listOut.height, visible.Count * RowHeight + 4f));
            Widgets.BeginScrollView(listOut, ref scrollPosition, listView);
            for (int i = 0; i < visible.Count; i++)
                DrawItemRow(0f, i * RowHeight, tableW, visible[i], i);
            Widgets.EndScrollView();

            Rect btnRow = new Rect(0f, inRect.height - bottomH, inRect.width, 36f);
            if (Widgets.ButtonText(btnRow.LeftHalf().ContractedBy(2f), "CancelButton".Translate()))
                Close();

            Rect confirmRect = btnRow.RightHalf().ContractedBy(2f);
            GUI.enabled = CanConfirm;
            if (Widgets.ButtonText(confirmRect, "TSA_WD_BuySettlement_Confirm".Translate()))
            {
                var list = new List<ThingDefCountClass>();
                for (int i = 0; i < allRows.Count; i++)
                {
                    var row = allRows[i];
                    string key = CompOutpostWarehouse.StockKey(row);
                    int pick = GetOffer(key);
                    if (pick <= 0) continue;
                    list.Add(SettlementBuyUtility.CloneStockRow(row, pick));
                }
                if (SettlementBuyUtility.TryLaunchBuy(settlement, list, goodwillOffer, out string fail))
                {
                    SoundDefOf.ExecuteTrade.PlayOneShotOnCamera();
                    Close();
                }
                else if (!fail.NullOrEmpty())
                    Messages.Message(fail, MessageTypeDefOf.RejectInput, false);
            }
            GUI.enabled = true;
            TooltipHandler.TipRegion(confirmRect, SettlementCaravanDealUi.BuildConfirmTooltip(ConfirmDisableReason));
        }

        private List<ThingDefCountClass> BuildVisibleList()
        {
            var visible = new List<ThingDefCountClass>();
            for (int i = 0; i < allRows.Count; i++)
            {
                ThingDefCountClass row = allRows[i];
                if (!SettlementCaravanDealUi.PassesListFilter(row, filter, SettlementCaravanDealUi.SessionCategory))
                    continue;
                visible.Add(row);
            }
            return visible;
        }

        private void DrawTableHeader(Rect hRect)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            float curX = hRect.x;
            curX += ColIcon;
            Rect starHdr = new Rect(curX, hRect.y, ColStar, hRect.height);
            SettlementCaravanDealUi.DrawStarHeader(starHdr);
            curX += ColStar;
            DrawHeader(ref curX, ColName, "TSA_WD_BuySettlement_ColName".Translate(), "Name", hRect);
            DrawHeader(ref curX, ColQty, "TSA_WD_BuySettlement_ColQty".Translate(), "Qty", hRect);
            float silverPerX = curX;
            DrawHeader(ref curX, ColSilverPer, "TSA_WD_BuySettlement_ColSilverPer".Translate(), "SilverPer", hRect);
            TooltipHandler.TipRegion(new Rect(silverPerX, hRect.y, ColSilverPer, hRect.height),
                "TSA_WD_BuySettlement_ColSilverPerTip".Translate());
            curX += ControlsWidth;
            DrawHeader(ref curX, ColSilverTotal, "TSA_WD_BuySettlement_ColSilver".Translate(), "SilverTotal", hRect);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private void DrawHeader(ref float curX, float width, string label, string tag, Rect hRect)
        {
            PawnRosterHeaderFilter.DrawFilterableHeader(
                ref curX, hRect.y, width, hRect.height,
                label, sortColumn == tag, sortAscending,
                TextAnchor.MiddleCenter, false, null, null,
                () => SetSort(tag));
        }

        private void SetSort(string col)
        {
            if (sortColumn == col) sortAscending = !sortAscending;
            else
            {
                sortColumn = col;
                sortAscending = true;
            }
            SortRows();
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        private void SortRows()
        {
            int dir = sortAscending ? 1 : -1;
            allRows.Sort((a, b) =>
            {
                bool aSilver = a.thingDef == ThingDefOf.Silver;
                bool bSilver = b.thingDef == ThingDefOf.Silver;
                if (aSilver != bSilver)
                    return aSilver ? -1 : 1;

                string keyA = CompOutpostWarehouse.StockKey(a);
                string keyB = CompOutpostWarehouse.StockKey(b);
                int cmp;
                switch (sortColumn)
                {
                    case "Qty":
                        cmp = a.count.CompareTo(b.count);
                        break;
                    case "SilverPer":
                        cmp = UnitValue(a).CompareTo(UnitValue(b));
                        break;
                    case "SilverTotal":
                        cmp = (UnitValue(a) * GetOffer(keyA)).CompareTo(UnitValue(b) * GetOffer(keyB));
                        break;
                    default:
                        cmp = string.CompareOrdinal(
                            SettlementBuyUtility.FormatStockLabel(a),
                            SettlementBuyUtility.FormatStockLabel(b));
                        break;
                }
                if (cmp == 0)
                    cmp = string.CompareOrdinal(keyA, keyB);
                return cmp * dir;
            });
        }

        private void ResetAllOffers()
        {
            offered.Clear();
            countEditBuffers.Clear();
            goodwillOffer = 0;
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        private void AssignExpensiveFirst()
        {
            float goodsTarget = Mathf.Max(0f, askSilver - GoodwillMarketValue);
            bool met = SettlementCaravanDealUi.AssignGoodsToTarget(
                allRows, offered, countEditBuffers, goodsTarget, UnitValue);
            SoundDefOf.Click.PlayOneShotOnCamera();
            if (!met && goodsTarget > 0.0001f)
                SettlementCaravanDealUi.NotifyAssignUnderfill();
        }

        private static Color OfferRowColor(int pick, int have)
        {
            if (pick <= 0) return Color.white;
            if (pick >= have) return new Color(0.45f, 0.85f, 0.5f);
            return new Color(0.95f, 0.85f, 0.35f);
        }

        private int GetOffer(string key) => offered.TryGetValue(key, out int o) ? o : 0;

        private void DrawItemRow(float x, float y, float width, ThingDefCountClass row, int rowIndex)
        {
            string key = CompOutpostWarehouse.StockKey(row);
            int have = row.count;
            int pick = Mathf.Clamp(GetOffer(key), 0, have);
            if (pick > 0) offered[key] = pick;
            else offered.Remove(key);

            Rect rowRect = new Rect(x, y, width, RowHeight);
            if (rowIndex % 2 == 0) Widgets.DrawHighlight(rowRect);
            if (Mouse.IsOver(rowRect)) Widgets.DrawLightHighlight(rowRect);

            Color prevColor = GUI.color;
            Color rowTint = OfferRowColor(pick, have);

            float curX = x;
            float contentY = y + RowHeight / 2f;
            Rect iconRect = new Rect(curX + (ColIcon - RowIconSize) / 2f, contentY - RowIconSize / 2f, RowIconSize, RowIconSize);
            if (row.stuff != null)
                Widgets.ThingIcon(iconRect, row.thingDef, row.stuff);
            else if (row.thingDef.uiIcon != null)
                Widgets.ThingIcon(iconRect, row.thingDef);
            curX += ColIcon;

            SettlementCaravanDealUi.DrawStarToggle(new Rect(curX, y, ColStar, RowHeight), row.thingDef);
            curX += ColStar;

            string label = SettlementBuyUtility.FormatStockLabel(row);
            GUI.color = rowTint;
            LabelAnchored(new Rect(curX, y, ColName, RowHeight), label.Truncate(ColName - 4f), TextAnchor.MiddleLeft);
            curX += ColName;

            LabelAnchored(new Rect(curX, y, ColQty, RowHeight), have.ToString(), TextAnchor.MiddleCenter);
            curX += ColQty;

            float per = UnitValue(row);
            LabelAnchored(new Rect(curX, y, ColSilverPer, RowHeight), per.ToString("F2"), TextAnchor.MiddleCenter);
            curX += ColSilverPer;
            GUI.color = prevColor;

            float btnY = y + (RowHeight - BtnW) / 2f;
            float cx = curX;
            Rect minusRect = new Rect(cx, btnY, BtnW, BtnW);
            cx += BtnW;
            Rect countRect = new Rect(cx, y + 4f, CountColW, RowHeight - 8f);
            cx += CountColW;
            Rect plusRect = new Rect(cx, btnY, BtnW, BtnW);
            cx += BtnW + BtnGap;
            Rect zeroRect = new Rect(cx, btnY, BtnW, BtnW);
            cx += BtnW + BtnGap;
            Rect maxRect = new Rect(cx, btnY, MaxBtnW, BtnW);

            int step = WdQuantityUI.AdjustmentStep();
            if (WdDragSelectButtons.ButtonText(minusRect, "-", WdDragSelectButtons.Hash(key, "minus")) && pick > 0)
                SetOffer(key, Mathf.Max(0, pick - step));
            if (WdDragSelectButtons.ButtonText(plusRect, "+", WdDragSelectButtons.Hash(key, "plus")) && pick < have)
                SetOffer(key, Mathf.Min(have, pick + step));
            if (WdDragSelectButtons.ButtonText(zeroRect, "0", WdDragSelectButtons.Hash(key, "zero")))
                SetOffer(key, 0);
            if (WdDragSelectButtons.ButtonText(maxRect, "Max", WdDragSelectButtons.Hash(key, "max")))
                SetOffer(key, have);
            TooltipHandler.TipRegion(minusRect, "TSA_WD_QuantityAdjustTip".Translate());
            TooltipHandler.TipRegion(plusRect, "TSA_WD_QuantityAdjustTip".Translate());

            pick = GetOffer(key);
            if (!countEditBuffers.TryGetValue(key, out string buffer) || buffer == null)
                buffer = pick.ToString();
            int edited = pick;
            TextAnchor prevAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.TextFieldNumeric(countRect, ref edited, ref buffer, 0f, have);
            Text.Anchor = prevAnchor;
            countEditBuffers[key] = buffer;
            if (edited != pick)
                SetOffer(key, Mathf.Clamp(edited, 0, have));

            curX += ControlsWidth;
            float lineSilver = UnitValue(row) * GetOffer(key);
            GUI.color = rowTint;
            LabelAnchored(new Rect(curX, y, ColSilverTotal, RowHeight), lineSilver.ToString("F2"), TextAnchor.MiddleCenter);
            GUI.color = prevColor;
        }

        private void SetOffer(string key, int value)
        {
            if (value <= 0)
            {
                offered.Remove(key);
                countEditBuffers[key] = "0";
                return;
            }
            offered[key] = value;
            countEditBuffers[key] = value.ToString();
        }
    }
}
