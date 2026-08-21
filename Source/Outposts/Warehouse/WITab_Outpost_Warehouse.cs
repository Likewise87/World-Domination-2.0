using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Warehouse inventory + ship-amount controls in one inspect tab (no separate ship dialog).</summary>
    public class WITab_Outpost_Warehouse : WITab
    {
        private Vector2 scrollPosition;
        private float scrollViewHeight;
        private readonly Dictionary<string, int> selectedCounts = new Dictionary<string, int>();
        private readonly Dictionary<string, string> countEditBuffers = new Dictionary<string, string>();
        private int selectedCountsSyncedWarehouseId = int.MinValue;

        private const float TabHeaderConsumedHeight = 38f;
        private const float RowIconSize = 28f;
        private const float RowHeight = 36f;
        private const float BtnW = 28f;
        private const float MaxBtnW = 40f;
        private const float CountColW = 56f;
        private const float BtnGap = 4f;
        private const float LaunchRowHeight = 35f;
        private const float LaunchSeparatorGap = 8f;
        private const float DestLineHeight = 24f;
        private static float ControlsWidth => BtnW + CountColW + BtnW + BtnGap + BtnW + BtnGap + MaxBtnW;

        public WITab_Outpost_Warehouse()
        {
            size = new Vector2(710f, 560f);
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

            WorldObject shipDest = comp.ResolveShipDestination();
            string destLine = "TSA_WD_WarehouseTab_ShipDest".Translate(
                Outpost_Warehouse_Delivery.GetDestinationLabelWithKind(shipDest));
            float destY = body.y + TabHeaderConsumedHeight - 6f;
            LabelAnchored(new Rect(body.x, destY, body.width, DestLineHeight), destLine, TextAnchor.MiddleLeft);

            float footerHeight = LaunchRowHeight + LaunchSeparatorGap + 4f;
            float listY = destY + DestLineHeight + 6f;
            Rect listRect = new Rect(body.x, listY, body.width, body.yMax - listY - footerHeight);
            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, scrollViewHeight);
            Widgets.BeginScrollView(listRect, ref scrollPosition, viewRect);

            float innerY = 0f;
            int rowIndex = 0;
            var items = comp.storedItems;
            if (items != null && items.Count > 0)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    var e = items[i];
                    if (e?.thingDef == null || e.count <= 0) continue;
                    DrawShipRow(viewRect.width, innerY, rowIndex, e);
                    innerY += RowHeight;
                    rowIndex++;
                }
            }
            else
            {
                LabelAnchored(new Rect(0f, innerY, viewRect.width, RowHeight),
                    "TSA_WD_Warehouse_InspectEmpty".Translate(), TextAnchor.MiddleLeft);
                innerY += RowHeight;
            }

            if (Event.current.type == EventType.Layout) scrollViewHeight = innerY;
            Widgets.EndScrollView();

            float launchY = body.yMax - footerHeight + LaunchSeparatorGap;
            float btnGap = 10f;
            float halfW = (body.width - btnGap) / 2f;
            Rect caravanRect = new Rect(body.x, launchY, halfW, LaunchRowHeight);
            Rect podRect = new Rect(body.x + halfW + btnGap, launchY, halfW, LaunchRowHeight);

            if (Widgets.ButtonText(caravanRect, "TSA_WD_Warehouse_ShipViaCaravanNow".Translate()))
                TryConfirmShip(outpost, comp, viaDropPod: false);

            bool podsResearched = RapidResponseUtility.TransportPodsResearched();
            if (!podsResearched)
            {
                Color prev = GUI.color;
                GUI.color = ColoredText.SubtleGrayColor;
                Widgets.ButtonText(podRect, "TSA_WD_Warehouse_ShipViaDropPodNow".Translate());
                GUI.color = prev;
            }
            else if (Widgets.ButtonText(podRect, "TSA_WD_Warehouse_ShipViaDropPodNow".Translate()))
            {
                TryConfirmShip(outpost, comp, viaDropPod: true);
            }

            TooltipHandler.TipRegion(caravanRect, "TSA_WD_Warehouse_ShipViaCaravanDesc".Translate());
            TooltipHandler.TipRegion(podRect, podsResearched
                ? "TSA_WD_Warehouse_ShipViaDropPodDesc".Translate()
                : "TSA_WD_RapidResponse_DropPodsNeedResearch".Translate());
        }

        private void SyncSelectedCounts(WorldObject_WD_Outpost outpost, CompOutpostWarehouse comp)
        {
            if (selectedCountsSyncedWarehouseId != outpost.ID)
            {
                selectedCounts.Clear();
                countEditBuffers.Clear();
                selectedCountsSyncedWarehouseId = outpost.ID;
            }

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

            float controlsX = width - ControlsWidth;
            Rect labelRect = new Rect(iconRect.xMax + 8f, y, controlsX - iconRect.xMax - 12f, RowHeight);
            string label = FormatStockLabel(entry);
            LabelAnchored(labelRect, label + " (" + stored + ")", TextAnchor.MiddleLeft);

            // Icon + name open the vanilla info card (includes stuff when present).
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

        /// <summary>Stuffable rows: "Wooden Grand Sculpture". Otherwise the def label.</summary>
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

        private void TryConfirmShip(WorldObject_WD_Outpost warehouse, CompOutpostWarehouse comp, bool viaDropPod)
        {
            if (viaDropPod && !RapidResponseUtility.TransportPodsResearched())
            {
                Messages.Message("TSA_WD_RapidResponse_DropPodsNeedResearch".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            WorldObject dest = comp.ResolveShipDestination();
            if (dest == null || !Outpost_Warehouse_Delivery.IsValidItemDeliveryDestination(dest, warehouse))
            {
                Messages.Message("TSA_WD_Warehouse_ShipNeedsDest".Translate(), warehouse, MessageTypeDefOf.RejectInput);
                return;
            }

            var request = new List<ThingDefCountClass>();
            var items = comp.storedItems;
            if (items != null)
            {
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
            }
            if (request.Count == 0)
            {
                Messages.Message("TSA_WD_Warehouse_ShipNothingSelected".Translate(), warehouse, MessageTypeDefOf.RejectInput);
                return;
            }
            if (!comp.TryWithdraw(request))
            {
                Messages.Message("TSA_WD_Warehouse_ShipInsufficient".Translate(), warehouse, MessageTypeDefOf.RejectInput);
                return;
            }
            WorldActions_Traveler.SpawnOutpostDeliveryTraveler(warehouse, request, dest, viaDropPod);
            string msgKey = viaDropPod ? "TSA_WD_Warehouse_ShipLaunchedDropPod" : "TSA_WD_Warehouse_ShipLaunched";
            Messages.Message(msgKey.Translate(dest.LabelCap), warehouse, MessageTypeDefOf.PositiveEvent);

            selectedCounts.Clear();
            countEditBuffers.Clear();
            CloseTab();
        }
    }
}
