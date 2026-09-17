using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace TSA_WorldDomination
{
    public enum WarehouseGoodsFilter : byte
    {
        All = 0,
        Food = 1,
        Nonfood = 2
    }

    /// <summary>Warehouse inventory + shipping controls (method, regular deliveries, ad hoc) in one inspect tab.</summary>
    public class WITab_Outpost_Warehouse : WITab
    {
        private Vector2 scrollPosition;
        private float scrollViewHeight;
        private readonly Dictionary<string, int> selectedCounts = new Dictionary<string, int>();
        private readonly Dictionary<string, string> countEditBuffers = new Dictionary<string, string>();
        private int selectedCountsSyncedWarehouseId = int.MinValue;
        private WarehouseGoodsFilter goodsFilter = WarehouseGoodsFilter.All;
        private string goodsSearch = "";
        private int selectedVirtualFood;
        private string virtualFoodEditBuffer = "0";
        private const string VirtualFoodRowKey = "__wd_virtual_food__";

        private const float TabHeaderConsumedHeight = 38f;
        private const float TableHeaderHeight = 28f;
        private const float RowIconSize = 28f;
        private const float RowHeight = 36f;
        private const float BtnW = 28f;
        private const float MaxBtnW = 40f;
        private const float CountColW = 56f;
        private const float BtnGap = 4f;
        private const float FooterRowHeight = 34f;
        private const float FooterGap = 6f;
        private const float FooterTipHeight = 36f;
        private const float FooterLabelW = 118f;
        private const float GoodsSearchMinW = 140f;
        private const float GoodsSearchMaxW = 240f;
        private const float ShipNowBtnW = 130f;
        private const float RowRightPad = 10f;
        private static float ControlsWidth => BtnW + CountColW + BtnW + BtnGap + BtnW + BtnGap + MaxBtnW;

        private static Texture2D landIcon;
        private static Texture2D dropPodIcon;
        private static Texture2D autoDeliverIconOn;
        private static Texture2D autoDeliverIconOff;
        private static Texture2D deliveryDestinationIcon;

        private static Texture2D LandIcon =>
            landIcon ??= ContentFinder<Texture2D>.Get("UI/Commands/Icon_ActiveTravelers", false)
                ?? TexCommand.Install;
        private static Texture2D DropPodIcon =>
            dropPodIcon ??= ContentFinder<Texture2D>.Get("WorldObjects/DropPod_OutpostGoods", false)
                ?? TexCommand.Install;
        private static Texture2D AutoDeliverIconOn =>
            autoDeliverIconOn ??= ContentFinder<Texture2D>.Get("UI/Commands/AutoDeliver", false)
                ?? TexCommand.Install;
        private static Texture2D AutoDeliverIconOff =>
            autoDeliverIconOff ??= ContentFinder<Texture2D>.Get("UI/Commands/AutoDeliver_Off", false)
                ?? AutoDeliverIconOn;
        private static Texture2D DeliveryDestinationIcon =>
            deliveryDestinationIcon ??= ContentFinder<Texture2D>.Get("UI/Commands/DeliveryDestination", false)
                ?? ContentFinder<Texture2D>.Get("UI/Commands/DeliveryTarget", false)
                ?? TexCommand.Attack;
        private static Texture2D virtualFoodIcon;
        private static Texture2D VirtualFoodIcon =>
            virtualFoodIcon ??= ContentFinder<Texture2D>.Get("UI/Commands/ConvertFood", false)
                ?? TexCommand.Install;

        public WITab_Outpost_Warehouse()
        {
            size = new Vector2(710f, 620f);
            labelKey = "TSA_WD_WarehouseTab_Label";
        }

        public override bool IsVisible =>
            SelObject is WorldObject_WD_Outpost o && Outpost_Production_Utils.IsWarehouseOutpost(o.def);

        private static void LabelAnchored(Rect rect, string text, TextAnchor anchor)
        {
            TextAnchor prev = Text.Anchor;
            Text.Anchor = anchor;
            Widgets.Label(rect, text);
            Text.Anchor = prev;
        }

        protected override void FillTab()
        {
            if (PawnRosterHeaderFilter.TryCloseDropdownOnCancel())
                return;

            if (!(SelObject is WorldObject_WD_Outpost outpost)) return;
            var comp = CompOutpostWarehouse.Get(outpost);
            if (comp == null) return;

            SyncSelectedCounts(outpost, comp);

            Rect body = new Rect(0f, 0f, size.x, size.y).ContractedBy(10f);
            Text.Font = GameFont.Medium;
            LabelAnchored(new Rect(body.x, body.y, body.width, 30f),
                OutpostTranslationUtil.TabHeadline(outpost, "TSA_WD_WarehouseTab_Label"), TextAnchor.MiddleLeft);
            Text.Font = GameFont.Small;
            Widgets.DrawLineHorizontal(body.x, body.y + 32f, body.width);

            float footerBlock =
                FooterRowHeight * 2f + FooterGap * 3f + FooterTipHeight + 4f;
            float listY = body.y + TabHeaderConsumedHeight;
            float headerY = listY;
            DrawStockTableHeader(body.x, headerY, body.width);

            float scrollY = headerY + TableHeaderHeight + 4f;
            Rect listRect = new Rect(body.x, scrollY, body.width, body.yMax - scrollY - footerBlock);
            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, scrollViewHeight);
            Widgets.BeginScrollView(listRect, ref scrollPosition, viewRect);

            float innerY = 0f;
            int rowIndex = 0;
            if (PassesVirtualFoodFilter() && PassesVirtualFoodSearch())
            {
                DrawVirtualFoodRow(viewRect.width, innerY, rowIndex, outpost);
                innerY += RowHeight;
                rowIndex++;
            }

            var items = comp.storedItems;
            if (items != null && items.Count > 0)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    var e = items[i];
                    if (e?.thingDef == null || e.count <= 0) continue;
                    if (!PassesGoodsFilter(e.thingDef)) continue;
                    if (!PassesGoodsSearch(e)) continue;
                    DrawShipRow(viewRect.width, innerY, rowIndex, e);
                    innerY += RowHeight;
                    rowIndex++;
                }
            }

            if (rowIndex == 0)
            {
                LabelAnchored(new Rect(0f, innerY, viewRect.width, RowHeight),
                    "TSA_WD_Warehouse_InspectEmpty".Translate(), TextAnchor.MiddleLeft);
                innerY += RowHeight;
            }

            if (Event.current.type == EventType.Layout) scrollViewHeight = innerY;
            Widgets.EndScrollView();

            float footY = body.yMax - footerBlock + FooterGap;
            Widgets.DrawLineHorizontal(body.x, footY - 4f, body.width);
            DrawFooterRows(body.x, footY, body.width, outpost, comp);

            PawnRosterHeaderFilter.DrawDropdownIfOpen();
        }

        private void DrawStockTableHeader(float x, float y, float width)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            float goodsColW = Mathf.Max(120f, width - ControlsWidth - RowRightPad - 8f);
            float filterIconW = PawnRosterHeaderFilter.FilterIconSize + 4f;
            float searchW = Mathf.Clamp(goodsColW - filterIconW - 8f, GoodsSearchMinW, GoodsSearchMaxW);
            searchW = Mathf.Min(searchW, goodsColW - filterIconW - 8f);

            Rect searchRect = new Rect(x, y + 3f, searchW, TableHeaderHeight - 6f);
            string oldSearch = goodsSearch ?? "";
            string nextSearch = Widgets.TextField(searchRect, oldSearch);
            if (nextSearch != oldSearch)
                goodsSearch = nextSearch;
            if (string.IsNullOrEmpty(goodsSearch))
            {
                GUI.color = new Color(1f, 1f, 1f, 0.4f);
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(searchRect, "  " + "TSA_WD_WarehouseTab_SearchHint".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.gray;
            }
            TooltipHandler.TipRegion(searchRect, "TSA_WD_WarehouseTab_SearchTip".Translate());

            float curX = searchRect.xMax + 4f;
            PawnRosterHeaderFilter.DrawFilterableHeader(
                ref curX,
                y,
                filterIconW,
                TableHeaderHeight,
                null,
                false,
                false,
                TextAnchor.MiddleCenter,
                goodsFilter != WarehouseGoodsFilter.All,
                "TSA_WD_WarehouseTab_FilterGoodsTip".Translate(),
                icon => PawnRosterHeaderFilter.OpenChoiceDropdown(
                    icon,
                    "TSA_WD_WarehouseTab_FilterGoodsTitle".Translate(),
                    BuildGoodsFilterChoices()),
                null);

            LabelAnchored(
                new Rect(x + width - ControlsWidth - RowRightPad, y, ControlsWidth, TableHeaderHeight),
                "TSA_WD_WarehouseTab_ColShipAmt".Translate(),
                TextAnchor.MiddleCenter);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Widgets.DrawLineHorizontal(x, y + TableHeaderHeight, width);
        }

        private List<HeaderFilterChoice> BuildGoodsFilterChoices()
        {
            return new List<HeaderFilterChoice>
            {
                new HeaderFilterChoice(
                    "TSA_WD_WarehouseTab_FilterAll".Translate(),
                    goodsFilter == WarehouseGoodsFilter.All,
                    () => goodsFilter = WarehouseGoodsFilter.All,
                    separatorAfter: true),
                new HeaderFilterChoice(
                    "TSA_WD_WarehouseTab_FilterFood".Translate(),
                    goodsFilter == WarehouseGoodsFilter.Food,
                    () => goodsFilter = WarehouseGoodsFilter.Food),
                new HeaderFilterChoice(
                    "TSA_WD_WarehouseTab_FilterNonfood".Translate(),
                    goodsFilter == WarehouseGoodsFilter.Nonfood,
                    () => goodsFilter = WarehouseGoodsFilter.Nonfood)
            };
        }

        private bool PassesGoodsFilter(ThingDef def)
        {
            if (goodsFilter == WarehouseGoodsFilter.All) return true;
            bool food = Outpost_Warehouse_Delivery.IsNutritionGivingStock(def);
            return goodsFilter == WarehouseGoodsFilter.Food ? food : !food;
        }

        private bool PassesVirtualFoodFilter() => goodsFilter != WarehouseGoodsFilter.Nonfood;

        private bool PassesVirtualFoodSearch()
        {
            if (goodsSearch.NullOrEmpty()) return true;
            string needle = goodsSearch.Trim();
            if (needle.Length == 0) return true;
            string label = "TSA_WD_WarehouseTab_VirtualFood".Translate();
            return !label.NullOrEmpty()
                && label.ToString().IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool PassesGoodsSearch(ThingDefCountClass entry)
        {
            if (goodsSearch.NullOrEmpty()) return true;
            string needle = goodsSearch.Trim();
            if (needle.Length == 0) return true;
            string label = FormatStockLabel(entry);
            if (!label.NullOrEmpty()
                && label.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            string defLabel = entry?.thingDef?.label;
            return !defLabel.NullOrEmpty()
                && defLabel.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void DrawFooterRows(float x, float y, float width, WorldObject_WD_Outpost outpost, CompOutpostWarehouse comp)
        {
            float rowY = y;
            bool viaPod = OutpostDispatchMode.GetViaDropPod(outpost);
            bool podsResearched = RapidResponseUtility.TransportPodsResearched();
            float controlsX = x + FooterLabelW + 6f;
            float controlsW = width - FooterLabelW - 6f;
            Color cyan = WorldOverlayLineMaterials.LogisticsDarkCyanColor;

            // Row 1: delivery type dropdown + Ship Now
            DrawFooterRowLabel(x, rowY, "TSA_WD_WarehouseTab_RowDeliveryType".Translate());

            float methodW = Mathf.Max(130f, controlsW - ShipNowBtnW - 8f) - 30f;
            Rect methodRect = new Rect(controlsX, rowY, methodW, FooterRowHeight);
            Texture2D methodIcon = viaPod ? DropPodIcon : LandIcon;
            string methodLabel = viaPod
                ? "TSA_WD_DispatchMode_DropPod".Translate()
                : "TSA_WD_DispatchMode_Land".Translate();
            if (WorldDomination_UIUtils.ButtonTextWithIcon(methodRect, methodIcon, methodLabel, iconTint: cyan))
                OpenDeliveryMethodMenu(outpost, viaPod, podsResearched);
            TooltipHandler.TipRegion(methodRect, viaPod
                ? ("TSA_WD_DispatchMode_DropPodDesc".Translate().ToString()
                    + "\n\n"
                    + "TSA_WD_DispatchMode_DropPodAaWarning".Translate())
                : "TSA_WD_DispatchMode_LandDesc".Translate());

            Rect shipNowRect = new Rect(methodRect.xMax + 8f, rowY, ShipNowBtnW, FooterRowHeight);
            if (WorldDomination_UIUtils.ButtonTextWithIcon(
                    shipNowRect,
                    DeliveryDestinationIcon,
                    "TSA_WD_WarehouseTab_ShipNow".Translate()))
                BeginAdHocSend(outpost, comp);
            TooltipHandler.TipRegion(shipNowRect, "TSA_WD_WarehouseTab_AdHocSendTip".Translate());

            rowY += FooterRowHeight + FooterGap;

            // Row 2: daily auto delivery as Off / To destination dropdown
            DrawFooterRowLabel(x, rowY, "TSA_WD_WarehouseTab_RowDailyAuto".Translate());

            Rect autoRect = new Rect(controlsX, rowY, methodW, FooterRowHeight);
            Texture2D autoIcon = ResolveAutoDeliveryButtonIcon(comp);
            string autoLabel = FormatAutoDeliveryButtonLabel(comp);
            Color? autoTint = comp != null && comp.autoShipEnabled ? cyan : (Color?)null;
            if (WorldDomination_UIUtils.ButtonTextWithIcon(autoRect, autoIcon, autoLabel, iconTint: autoTint))
                OpenAutoDeliveryMenu(outpost, comp);
            TooltipHandler.TipRegion(autoRect, "TSA_WD_Warehouse_AutoShipDesc".Translate());

            rowY += FooterRowHeight + FooterGap;
            Text.Font = GameFont.Tiny;
            GUI.color = ColoredText.SubtleGrayColor;
            LabelAnchored(
                new Rect(x, rowY, width, FooterTipHeight),
                "TSA_WD_WarehouseTab_DestRulesTip".Translate(),
                TextAnchor.UpperLeft);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private static string FormatAutoDeliveryButtonLabel(CompOutpostWarehouse comp)
        {
            if (comp == null || !comp.autoShipEnabled)
                return "TSA_WD_WarehouseTab_AutoOff".Translate();
            WorldObject dest = comp.ResolveShipDestination();
            return "TSA_WD_WarehouseTab_AutoTo".Translate(
                Outpost_Warehouse_Delivery.GetDestinationLabelWithKind(dest));
        }

        private static Texture2D ResolveAutoDeliveryButtonIcon(CompOutpostWarehouse comp)
        {
            if (comp == null || !comp.autoShipEnabled)
                return AutoDeliverIconOff;

            WorldObject dest = comp.ResolveShipDestination();
            if (dest is WorldObject_WD_Outpost wo && wo.def?.ExpandingIconTexture != null)
                return wo.def.ExpandingIconTexture;
            if (dest?.Faction?.def?.FactionIcon != null)
                return dest.Faction.def.FactionIcon;
            return AutoDeliverIconOn;
        }

        private static Texture2D ResolveDestinationMenuIcon(WorldObject destination)
        {
            if (destination is WorldObject_WD_Outpost outpost && outpost.def?.ExpandingIconTexture != null)
                return outpost.def.ExpandingIconTexture;
            if (destination?.Faction?.def?.FactionIcon != null)
                return destination.Faction.def.FactionIcon;
            return TexCommand.Install;
        }

        private static void OpenDeliveryMethodMenu(WorldObject_WD_Outpost outpost, bool viaPod, bool podsResearched)
        {
            var options = new List<FloatMenuOption>();
            options.Add(new FloatMenuOption(
                "TSA_WD_DispatchMode_Land".Translate(),
                () =>
                {
                    if (viaPod)
                    {
                        OutpostDispatchMode.SetViaDropPod(outpost, false);
                        SoundDefOf.Click.PlayOneShotOnCamera();
                    }
                },
                LandIcon,
                WorldOverlayLineMaterials.LogisticsDarkCyanColor));

            var podOpt = new FloatMenuOption(
                "TSA_WD_DispatchMode_DropPod".Translate(),
                () =>
                {
                    if (!viaPod)
                    {
                        OutpostDispatchMode.TrySetViaDropPod(outpost, true);
                        SoundDefOf.Click.PlayOneShotOnCamera();
                    }
                },
                DropPodIcon,
                WorldOverlayLineMaterials.LogisticsDarkCyanColor);
            if (!podsResearched)
            {
                podOpt.Disabled = true;
                podOpt.tooltip = new TipSignal("TSA_WD_DispatchMode_NeedsResearch".Translate());
            }
            options.Add(podOpt);
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static void OpenAutoDeliveryMenu(WorldObject_WD_Outpost outpost, CompOutpostWarehouse comp)
        {
            if (comp == null || outpost == null) return;

            var options = new List<FloatMenuOption>();
            options.Add(new FloatMenuOption(
                "TSA_WD_WarehouseTab_AutoOff".Translate(),
                () =>
                {
                    comp.autoShipEnabled = false;
                    SoundDefOf.Click.PlayOneShotOnCamera();
                },
                AutoDeliverIconOff,
                Color.white));

            var destinations = Outpost_Warehouse_Delivery.CollectValidItemDeliveryDestinations(outpost);
            for (int i = 0; i < destinations.Count; i++)
            {
                WorldObject dest = destinations[i];
                WorldObject captured = dest;
                string label = "TSA_WD_WarehouseTab_AutoTo".Translate(
                    Outpost_Warehouse_Delivery.GetDestinationLabelWithKind(captured));
                options.Add(new FloatMenuOption(
                    label,
                    () =>
                    {
                        comp.shipDestinationWorldObjectId = captured.ID;
                        comp.autoShipEnabled = true;
                        SoundDefOf.Click.PlayOneShotOnCamera();
                    },
                    ResolveDestinationMenuIcon(captured),
                    WorldOverlayLineMaterials.LogisticsDarkCyanColor));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static void DrawFooterRowLabel(float x, float y, string text)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = ColoredText.SubtleGrayColor;
            LabelAnchored(new Rect(x, y, FooterLabelW, FooterRowHeight), text, TextAnchor.MiddleLeft);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private void BeginAdHocSend(WorldObject_WD_Outpost outpost, CompOutpostWarehouse comp)
        {
            var request = BuildSelectedRequest(comp);
            float virtualFood = selectedVirtualFood;
            if (!Outpost_Warehouse_Delivery.RequestHasCargo(request, virtualFood))
            {
                Messages.Message("TSA_WD_Warehouse_ShipNothingSelected".Translate(), outpost, MessageTypeDefOf.RejectInput);
                return;
            }

            bool viaDropPod = OutpostDispatchMode.GetViaDropPod(outpost);
            if (viaDropPod && !RapidResponseUtility.TransportPodsResearched())
            {
                Messages.Message("TSA_WD_RapidResponse_DropPodsNeedResearch".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            Outpost_Warehouse_Delivery.BeginAdHocShipmentTargeting(
                outpost,
                comp,
                request,
                viaDropPod,
                () =>
                {
                    selectedCounts.Clear();
                    countEditBuffers.Clear();
                    selectedVirtualFood = 0;
                    virtualFoodEditBuffer = "0";
                    CloseTab();
                },
                virtualFood);
        }

        private List<ThingDefCountClass> BuildSelectedRequest(CompOutpostWarehouse comp)
        {
            var request = new List<ThingDefCountClass>();
            var items = comp.storedItems;
            if (items == null) return request;
            for (int i = 0; i < items.Count; i++)
            {
                var e = items[i];
                if (e?.thingDef == null || e.count <= 0) continue;
                string key = CompOutpostWarehouse.StockKey(e);
                if (!selectedCounts.TryGetValue(key, out int pick) || pick <= 0) continue;
                request.Add(new ThingDefCountClass(e.thingDef, pick)
                {
                    stuff = e.stuff,
                    quality = e.quality
                });
            }
            return request;
        }

        private void SyncSelectedCounts(WorldObject_WD_Outpost outpost, CompOutpostWarehouse comp)
        {
            if (selectedCountsSyncedWarehouseId != outpost.ID)
            {
                selectedCounts.Clear();
                countEditBuffers.Clear();
                selectedVirtualFood = 0;
                virtualFoodEditBuffer = "0";
                selectedCountsSyncedWarehouseId = outpost.ID;
            }

            int shippableVf = Outpost_Warehouse_Delivery.GetShippableVirtualFoodInt(outpost);
            selectedVirtualFood = Mathf.Clamp(selectedVirtualFood, 0, shippableVf);

            var items = comp.storedItems;
            if (items == null) return;

            var stillPresent = new HashSet<string>();
            for (int i = 0; i < items.Count; i++)
            {
                var e = items[i];
                if (e?.thingDef == null || e.count <= 0) continue;
                string key = CompOutpostWarehouse.StockKey(e);
                stillPresent.Add(key);
                if (!selectedCounts.ContainsKey(key))
                    selectedCounts[key] = 0;
                else
                    selectedCounts[key] = Mathf.Clamp(selectedCounts[key], 0, e.count);
            }

            if (selectedCounts.Count == 0) return;
            var toRemove = new List<string>();
            foreach (var kv in selectedCounts)
            {
                if (!stillPresent.Contains(kv.Key))
                    toRemove.Add(kv.Key);
            }
            for (int i = 0; i < toRemove.Count; i++)
            {
                selectedCounts.Remove(toRemove[i]);
                countEditBuffers.Remove(toRemove[i]);
            }
        }

        private void DrawVirtualFoodRow(float width, float y, int rowIndex, WorldObject_WD_Outpost outpost)
        {
            int shippable = Outpost_Warehouse_Delivery.GetShippableVirtualFoodInt(outpost);
            var logi = outpost.GetComponent<CompOutpostLogistics>();
            float pool = logi?.currentFood ?? 0f;
            selectedVirtualFood = Mathf.Clamp(selectedVirtualFood, 0, shippable);

            Rect row = new Rect(0f, y, width, RowHeight);
            if (rowIndex % 2 == 0) Widgets.DrawHighlight(row);

            float contentY = y + RowHeight / 2f;
            Rect iconRect = new Rect(row.x + 4f, contentY - RowIconSize / 2f, RowIconSize, RowIconSize);
            if (VirtualFoodIcon != null)
                Widgets.DrawTextureFitted(iconRect, VirtualFoodIcon, 1f);

            float controlsX = width - ControlsWidth - RowRightPad;
            Rect labelRect = new Rect(iconRect.xMax + 8f, y, controlsX - iconRect.xMax - 12f, RowHeight);
            string label = "TSA_WD_WarehouseTab_VirtualFood".Translate();
            string detail = "TSA_WD_WarehouseTab_VirtualFoodDetail".Translate(
                pool.ToString("F0"),
                Outpost_Warehouse_Delivery.VirtualFoodShipReserve.ToString("F0"),
                shippable.ToString());
            LabelAnchored(labelRect, label + " (" + shippable + ")", TextAnchor.MiddleLeft);
            TooltipHandler.TipRegion(labelRect, detail);

            float btnY = y + (RowHeight - BtnW) / 2f;
            float cx = controlsX;
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
            if (WdDragSelectButtons.ButtonText(minusRect, "-", WdDragSelectButtons.Hash(VirtualFoodRowKey, "minus"))
                && selectedVirtualFood > 0)
                SetVirtualFoodPick(Mathf.Max(0, selectedVirtualFood - step));
            if (WdDragSelectButtons.ButtonText(plusRect, "+", WdDragSelectButtons.Hash(VirtualFoodRowKey, "plus"))
                && selectedVirtualFood < shippable)
                SetVirtualFoodPick(Mathf.Min(shippable, selectedVirtualFood + step));
            if (WdDragSelectButtons.ButtonText(zeroRect, "0", WdDragSelectButtons.Hash(VirtualFoodRowKey, "zero")))
                SetVirtualFoodPick(0);
            if (WdDragSelectButtons.ButtonText(maxRect, "Max", WdDragSelectButtons.Hash(VirtualFoodRowKey, "max")))
                SetVirtualFoodPick(shippable);
            TooltipHandler.TipRegion(minusRect, "TSA_WD_QuantityAdjustTip".Translate());
            TooltipHandler.TipRegion(plusRect, "TSA_WD_QuantityAdjustTip".Translate());

            if (virtualFoodEditBuffer == null)
                virtualFoodEditBuffer = selectedVirtualFood.ToString();
            int edited = selectedVirtualFood;
            TextAnchor prevAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.TextFieldNumeric(countRect, ref edited, ref virtualFoodEditBuffer, 0f, shippable);
            Text.Anchor = prevAnchor;
            if (edited != selectedVirtualFood)
                SetVirtualFoodPick(Mathf.Clamp(edited, 0, shippable));
        }

        private void SetVirtualFoodPick(int value)
        {
            selectedVirtualFood = value;
            virtualFoodEditBuffer = value.ToString();
        }

        private void DrawShipRow(float width, float y, int rowIndex, ThingDefCountClass entry)
        {
            ThingDef def = entry.thingDef;
            int stored = entry.count;
            string key = CompOutpostWarehouse.StockKey(entry);

            if (!selectedCounts.TryGetValue(key, out int pick)) pick = 0;
            pick = Mathf.Clamp(pick, 0, stored);
            selectedCounts[key] = pick;

            Rect row = new Rect(0f, y, width, RowHeight);
            if (rowIndex % 2 == 0) Widgets.DrawHighlight(row);

            float contentY = y + RowHeight / 2f;
            Rect iconRect = new Rect(row.x + 4f, contentY - RowIconSize / 2f, RowIconSize, RowIconSize);
            if (def.uiIcon != null)
            {
                if (entry.stuff != null)
                    Widgets.ThingIcon(iconRect, def, entry.stuff);
                else
                    Widgets.ThingIcon(iconRect, def);
            }

            float controlsX = width - ControlsWidth - RowRightPad;
            Rect labelRect = new Rect(iconRect.xMax + 8f, y, controlsX - iconRect.xMax - 12f, RowHeight);
            string label = FormatStockLabel(entry);
            LabelAnchored(labelRect, label + " (" + stored + ")", TextAnchor.MiddleLeft);

            Rect infoClickRect = new Rect(iconRect.x, y, labelRect.xMax - iconRect.x, RowHeight);
            if (Mouse.IsOver(infoClickRect))
                Widgets.DrawHighlight(infoClickRect);
            TooltipHandler.TipRegion(infoClickRect, label);
            if (Widgets.ButtonInvisible(infoClickRect))
                OpenStockInfoCard(entry);

            float btnY = y + (RowHeight - BtnW) / 2f;
            float cx = controlsX;
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
                SetPick(key, Mathf.Max(0, pick - step));
            if (WdDragSelectButtons.ButtonText(plusRect, "+", WdDragSelectButtons.Hash(key, "plus")) && pick < stored)
                SetPick(key, Mathf.Min(stored, pick + step));
            if (WdDragSelectButtons.ButtonText(zeroRect, "0", WdDragSelectButtons.Hash(key, "zero")))
                SetPick(key, 0);
            if (WdDragSelectButtons.ButtonText(maxRect, "Max", WdDragSelectButtons.Hash(key, "max")))
                SetPick(key, stored);
            TooltipHandler.TipRegion(minusRect, "TSA_WD_QuantityAdjustTip".Translate());
            TooltipHandler.TipRegion(plusRect, "TSA_WD_QuantityAdjustTip".Translate());

            pick = selectedCounts[key];
            if (!countEditBuffers.TryGetValue(key, out string buffer) || buffer == null)
                buffer = pick.ToString();
            int edited = pick;
            TextAnchor prevAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.TextFieldNumeric(countRect, ref edited, ref buffer, 0f, stored);
            Text.Anchor = prevAnchor;
            countEditBuffers[key] = buffer;
            if (edited != pick)
                SetPick(key, Mathf.Clamp(edited, 0, stored));
        }

        private static string FormatStockLabel(ThingDefCountClass entry)
        {
            if (entry?.thingDef == null) return "";
            ThingDef def = entry.thingDef;
            if (entry.stuff != null)
            {
                string stuffAdj = entry.stuff.LabelAsStuff;
                if (!string.IsNullOrEmpty(stuffAdj))
                    return stuffAdj.CapitalizeFirst() + " " + def.LabelCap;
            }
            string label = entry.LabelCap;
            return string.IsNullOrEmpty(label) ? def.LabelCap : label;
        }

        private static void OpenStockInfoCard(ThingDefCountClass entry)
        {
            if (entry?.thingDef == null) return;
            if (entry.stuff != null)
                Find.WindowStack.Add(new Dialog_InfoCard(entry.thingDef, entry.stuff));
            else
                Find.WindowStack.Add(new Dialog_InfoCard(entry.thingDef));
        }

        private void SetPick(string key, int value)
        {
            selectedCounts[key] = value;
            countEditBuffers[key] = value.ToString();
        }
    }
}
