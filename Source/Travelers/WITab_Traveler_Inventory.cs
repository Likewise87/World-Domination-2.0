using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Read-only Inventory tab for player-faction WD travelers.
    /// Reuses warehouse stock-row chrome (icon / type / quality / count); no shipping controls.
    /// Upgrade travelers show prepaid cargo with the same chrome as the outpost upgrades confirm dialog.
    /// </summary>
    public class WITab_Traveler_Inventory : WITab
    {
        private sealed class Row
        {
            public ThingDefCountClass stock;
            public Thing unique;
            public bool isVirtualFood;
            public ThingDef def;
            public string itemLabel;
            public string typeLabel;
            public bool hasQuality;
            public string qualityLabel;
            public int count;
            public string valueLabel;
            public bool isUnique;
        }

        private sealed class UpgradeCargoCostLine
        {
            public ThingDef iconDef;
            public string label;
        }

        private sealed class UpgradeCargoRow
        {
            public OutpostUpgradeDef def;
            public string name;
            public string impactSummary;
            public readonly List<UpgradeCargoCostLine> costs = new List<UpgradeCargoCostLine>(4);
        }

        private Vector2 scrollPosition;
        private Vector2 upgradeScrollPosition;
        private float scrollViewHeight;
        private readonly List<Row> rows = new List<Row>(16);
        private readonly List<UpgradeCargoRow> upgradeCargoRows = new List<UpgradeCargoRow>(8);

        private const int CacheRefreshInterval = 60;
        private int lastCacheTick = -1;
        private int cachedTravelerId = int.MinValue;
        private int cachedCargoFingerprint = int.MinValue;

        private const float TabHeaderConsumedHeight = 38f;
        private const float TableHeaderHeight = 28f;
        private const float RowIconSize = 28f;
        private const float RowHeight = 36f;
        private const float ColItemIcon = 40f;
        private const float ColType = 100f;
        private const float ColQuality = 90f;
        private const float ColCount = 56f;
        private const float ColValue = 72f;
        private const float RowRightPad = 10f;
        private const float FixedColsWidth =
            ColItemIcon + ColType + ColQuality + ColCount + ColValue + RowRightPad;

        // Upgrade cargo panel (matches Dialog_OutpostUpgradeBatchConfirm / upgrades tab).
        private const float UpgradeLeadIconSize = 36f;
        private const float UpgradeLeadGap = 8f;
        private const float UpgradeCostColX = UpgradeLeadIconSize + UpgradeLeadGap;
        private const float UpgradeCostIconSize = 20f;
        private const float UpgradeCostLineH = 22f;
        private const float UpgradeNameH = 26f;
        private const float UpgradeSectionHeaderH = 26f;
        private const float UpgradeSeparatorH = 8f;
        private static readonly Color UpgradeCostTextColor = Color.gray;
        private static readonly Color UpgradeSeparatorColor = new Color(1f, 1f, 1f, 0.22f);

        public WITab_Traveler_Inventory()
        {
            size = new Vector2(820f, 560f);
            labelKey = "TSA_WD_TravelerInventory_TabLabel";
        }

        public override bool IsVisible =>
            SelObject is WorldObject_Traveler t
            && t.Faction != null
            && t.Faction.IsPlayer;

        protected override void FillTab()
        {
            if (!(SelObject is WorldObject_Traveler traveler) || traveler.Destroyed) return;

            RebuildRowsIfNeeded(traveler);

            Rect body = new Rect(0f, 0f, size.x, size.y).ContractedBy(10f);
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(body.x, body.y, body.width, 30f), traveler.LabelCap);
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            Widgets.DrawLineHorizontal(body.x, body.y + 32f, body.width);

            float y = body.y + TabHeaderConsumedHeight;
            bool isUpgradeTraveler = traveler is WorldObject_Traveler_Outpost_Upgrade;

            if (isUpgradeTraveler && upgradeCargoRows.Count > 0)
            {
                // Upgrade cargo is the inventory; fill remaining height (goods table only if present).
                float goodsReserve = rows.Count > 0 ? 140f : 0f;
                float panelH = Mathf.Max(80f, body.yMax - y - goodsReserve);
                y = DrawUpgradeCargoPanel(body.x, y, body.width, panelH);
            }

            if (traveler is WorldObject_Traveler_Outpost_TypeChange typeChange)
            {
                string typeLabel = DefDatabase<WorldObjectDef>
                    .GetNamedSilentFail(typeChange.typeChangeDefName)?.LabelCap
                    ?? typeChange.typeChangeDefName
                    ?? "?";
                Widgets.Label(
                    new Rect(body.x, y, body.width, 24f),
                    "TSA_WD_TravelerInventory_TypeChangeLine".Translate(typeLabel));
                y += 28f;
            }

            float contentWidth = body.width - OutpostTabStatsUi.ScrollbarRightPadding;
            float itemW = Mathf.Max(80f, contentWidth - FixedColsWidth);

            if (rows.Count == 0)
            {
                if (isUpgradeTraveler && upgradeCargoRows.Count > 0)
                    return;
                Rect emptyRect = new Rect(body.x, y, body.width, body.yMax - y);
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = new Color(1f, 1f, 1f, 0.55f);
                Widgets.Label(emptyRect, "TSA_WD_TravelerInventory_Empty".Translate());
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
                return;
            }

            // Header pinned above the list — same chrome as Window_AllPlayerPawns / Warehouse.
            DrawTableHeader(body.x, y, contentWidth, itemW);
            Widgets.DrawLineHorizontal(body.x, y + TableHeaderHeight, contentWidth);

            float listTop = y + TableHeaderHeight + 4f;
            Rect scrollOuter = new Rect(body.x, listTop, body.width, body.yMax - listTop);
            scrollViewHeight = rows.Count * RowHeight + 8f;
            if (scrollViewHeight < scrollOuter.height)
                scrollViewHeight = scrollOuter.height;
            Rect viewRect = new Rect(0f, 0f, contentWidth, scrollViewHeight);

            Widgets.BeginScrollView(scrollOuter, ref scrollPosition, viewRect);
            float rowY = 0f;
            for (int i = 0; i < rows.Count; i++)
            {
                DrawRow(contentWidth, rowY, i, rows[i], itemW);
                rowY += RowHeight;
            }
            Widgets.EndScrollView();
        }

        private void DrawTableHeader(float x, float y, float width, float itemW)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            float curX = x + ColItemIcon;

            LabelAnchored(new Rect(curX, y, itemW, TableHeaderHeight),
                "TSA_WD_Armory_HdrItem".Translate(), TextAnchor.MiddleLeft);
            curX += itemW;
            LabelAnchored(new Rect(curX, y, ColType, TableHeaderHeight),
                "TSA_WD_Armory_HdrType".Translate(), TextAnchor.MiddleCenter);
            curX += ColType;
            LabelAnchored(new Rect(curX, y, ColQuality, TableHeaderHeight),
                "TSA_WD_Armory_HdrQuality".Translate(), TextAnchor.MiddleCenter);
            curX += ColQuality;
            LabelAnchored(new Rect(curX, y, ColCount, TableHeaderHeight),
                "TSA_WD_Armory_HdrCount".Translate(), TextAnchor.MiddleCenter);
            curX += ColCount;
            LabelAnchored(new Rect(curX, y, ColValue, TableHeaderHeight),
                "TSA_WD_TravelerInventory_HdrValue".Translate(), TextAnchor.MiddleCenter);

            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private static void LabelAnchored(Rect rect, string text, TextAnchor anchor)
        {
            TextAnchor prev = Text.Anchor;
            Text.Anchor = anchor;
            Widgets.Label(rect, text.Truncate(rect.width - 4f));
            Text.Anchor = prev;
        }

        private float DrawUpgradeCargoPanel(float x, float y, float width, float height)
        {
            float panelTop = y;
            string header = upgradeCargoRows.Count <= 1
                ? "TSA_WD_OutpostUpgrades_BatchListHeader".Translate().ToString()
                : "TSA_WD_TravelerInventory_UpgradeBatchHeader".Translate(upgradeCargoRows.Count.ToString()).ToString();
            y = DrawUpgradeSectionHeader(x, y, width, header);

            float contentH = MeasureUpgradeCargoContentHeight(width - 16f);
            float scrollH = Mathf.Max(40f, panelTop + height - y);
            Rect scrollOuter = new Rect(x, y, width, scrollH);
            Rect viewRect = new Rect(0f, 0f, width - 16f, Mathf.Max(contentH, scrollOuter.height));
            Widgets.BeginScrollView(scrollOuter, ref upgradeScrollPosition, viewRect);
            DrawUpgradeCargoScrollContent(viewRect.width);
            Widgets.EndScrollView();
            return scrollOuter.yMax;
        }

        private static float DrawUpgradeSectionHeader(float x, float y, float width, string label)
        {
            string hex = ColorUtility.ToHtmlStringRGBA(SettingsUI.SectionHeaderColor);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(x, y, width, UpgradeSectionHeaderH), $"<b><color=#{hex}>{label}</color></b>");
            Text.Anchor = TextAnchor.UpperLeft;
            return y + UpgradeSectionHeaderH + 4f;
        }

        private float MeasureUpgradeCargoContentHeight(float width)
        {
            float h = 0f;
            for (int i = 0; i < upgradeCargoRows.Count; i++)
            {
                if (i > 0) h += UpgradeSeparatorH;
                h += MeasureUpgradeCargoBlockHeight(upgradeCargoRows[i]);
            }
            return h + 4f;
        }

        private static float MeasureUpgradeCargoBlockHeight(UpgradeCargoRow row)
        {
            if (row == null) return UpgradeLeadIconSize;
            int costLines = Mathf.Max(1, row.costs.Count);
            float impactH = string.IsNullOrEmpty(row.impactSummary) ? 0f : UpgradeCostLineH;
            float textH = UpgradeNameH + costLines * UpgradeCostLineH + impactH;
            return Mathf.Max(UpgradeLeadIconSize + 4f, textH);
        }

        private void DrawUpgradeCargoScrollContent(float width)
        {
            float curY = 0f;
            for (int i = 0; i < upgradeCargoRows.Count; i++)
            {
                UpgradeCargoRow row = upgradeCargoRows[i];
                if (row == null) continue;
                if (i > 0)
                {
                    GUI.color = UpgradeSeparatorColor;
                    Widgets.DrawLineHorizontal(0f, curY + UpgradeSeparatorH * 0.5f, width);
                    GUI.color = Color.white;
                    curY += UpgradeSeparatorH;
                }
                curY = DrawUpgradeCargoBlock(0f, curY, width, row);
            }
        }

        private static float DrawUpgradeCargoBlock(float x, float y, float width, UpgradeCargoRow row)
        {
            float blockH = MeasureUpgradeCargoBlockHeight(row);
            Rect iconRect = new Rect(x, y + 2f, UpgradeLeadIconSize, UpgradeLeadIconSize);
            Texture2D icon = Outpost_Upgrade_UI.GetUpgradeIcon(row.def);
            if (icon != null)
                Outpost_Upgrade_UI.DrawTextureTopFit(iconRect.ContractedBy(1f), icon);

            float textX = x + UpgradeCostColX;
            float textW = width - UpgradeCostColX;
            float ty = y;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(textX, ty, textW, UpgradeNameH), row.name ?? "");
            Text.Anchor = TextAnchor.UpperLeft;
            ty += UpgradeNameH;

            Text.Font = GameFont.Tiny;
            if (row.costs.Count == 0)
            {
                GUI.color = UpgradeCostTextColor;
                Widgets.Label(new Rect(textX, ty, textW, UpgradeCostLineH),
                    "TSA_WD_OutpostUpgrades_NoCost".Translate());
                GUI.color = Color.white;
                ty += UpgradeCostLineH;
            }
            else
            {
                for (int i = 0; i < row.costs.Count; i++)
                {
                    UpgradeCargoCostLine c = row.costs[i];
                    if (c == null) continue;
                    Rect costIcon = new Rect(
                        textX,
                        ty + (UpgradeCostLineH - UpgradeCostIconSize) * 0.5f,
                        UpgradeCostIconSize,
                        UpgradeCostIconSize);
                    if (c.iconDef != null)
                        Widgets.ThingIcon(costIcon, c.iconDef);
                    GUI.color = UpgradeCostTextColor;
                    Text.Anchor = TextAnchor.MiddleLeft;
                    Widgets.Label(
                        new Rect(textX + UpgradeCostIconSize + 6f, ty, textW - UpgradeCostIconSize - 6f, UpgradeCostLineH),
                        c.label ?? "");
                    Text.Anchor = TextAnchor.UpperLeft;
                    GUI.color = Color.white;
                    ty += UpgradeCostLineH;
                }
            }

            if (!string.IsNullOrEmpty(row.impactSummary))
            {
                GUI.color = UpgradeCostTextColor;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(textX, ty, textW, UpgradeCostLineH), row.impactSummary);
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
            }

            Text.Font = GameFont.Small;
            return y + blockH;
        }

        private void DrawRow(float width, float y, int rowIndex, Row row, float itemW)
        {
            Rect rowRect = new Rect(0f, y, width, RowHeight);
            if (rowIndex % 2 == 0) Widgets.DrawHighlight(rowRect);
            if (Mouse.IsOver(rowRect)) Widgets.DrawLightHighlight(rowRect);

            Text.Font = GameFont.Tiny;
            float curX = 0f;

            Rect iconRect = new Rect(
                curX + (ColItemIcon - RowIconSize) * 0.5f,
                y + (RowHeight - RowIconSize) * 0.5f,
                RowIconSize,
                RowIconSize);
            if (row.isVirtualFood)
            {
                if (WITab_Outpost_WarehouseAssets.VirtualFoodIcon != null)
                    Widgets.DrawTextureFitted(iconRect, WITab_Outpost_WarehouseAssets.VirtualFoodIcon, 1f);
            }
            else if (row.unique != null)
                Widgets.ThingIcon(iconRect, row.unique);
            else if (row.def != null)
                Widgets.ThingIcon(iconRect, row.def, row.stock?.stuff);
            curX += ColItemIcon;

            Rect labelRect = new Rect(curX, y, itemW, RowHeight);
            Text.Anchor = TextAnchor.MiddleLeft;
            if (row.isUnique) GUI.color = WorldOverlayLineMaterials.DarkCyanColor;
            Widgets.Label(labelRect, row.itemLabel.Truncate(itemW - 6f));
            GUI.color = Color.white;
            TooltipHandler.TipRegion(labelRect, row.itemLabel);
            curX += itemW;

            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(curX, y, ColType, RowHeight), row.typeLabel.Truncate(ColType - 6f));
            curX += ColType;
            Widgets.Label(new Rect(curX, y, ColQuality, RowHeight),
                (row.hasQuality ? row.qualityLabel : "").Truncate(ColQuality - 6f));
            curX += ColQuality;
            Widgets.Label(new Rect(curX, y, ColCount, RowHeight), row.count.ToString());
            curX += ColCount;
            Widgets.Label(new Rect(curX, y, ColValue, RowHeight), row.valueLabel ?? "");
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        private static int ComputeCargoFingerprint(WorldObject_Traveler traveler)
        {
            unchecked
            {
                int h = 17;
                if (traveler is WorldObject_Traveler_Outpost_Delivery delivery)
                {
                    h = h * 31 + Mathf.RoundToInt(delivery.deliveryVirtualFood * 1000f);
                    h = HashStockList(h, delivery.deliveryItems);
                    ThingOwner<Thing> u = delivery.CargoUniques;
                    if (u != null)
                    {
                        h = h * 31 + u.Count;
                        for (int i = 0; i < u.Count; i++)
                        {
                            Thing t = u[i];
                            if (t == null) continue;
                            h = h * 31 + t.thingIDNumber;
                            h = h * 31 + t.stackCount;
                        }
                    }
                }
                if (traveler is WorldObject_Traveler_SettlementBuy buy)
                    h = HashStockList(h, buy.paymentItems);
                if (traveler is WorldObject_Traveler_SettlementGift gift)
                    h = HashStockList(h, gift.paymentItems);
                if (traveler is WorldObject_Traveler_SettlementBribe bribe)
                    h = HashStockList(h, bribe.paymentItems);
                if (traveler is WorldObject_Traveler_DiplomacyNegotiate negotiate)
                    h = HashStockList(h, negotiate.paymentItems);
                if (traveler is WorldObject_Traveler_RapidResponseDropPod rr)
                    h = HashStockList(h, rr.deliveryItems);
                if (traveler is WorldObject_Traveler_Outpost_Upgrade upgrade)
                {
                    IReadOnlyList<PendingOutpostUpgradeEntry> cargo = upgrade.UpgradeCargo;
                    h = h * 31 + cargo.Count;
                    for (int i = 0; i < cargo.Count; i++)
                    {
                        PendingOutpostUpgradeEntry e = cargo[i];
                        if (e == null) continue;
                        h = h * 31 + (e.defName?.GetHashCode() ?? 0);
                        h = h * 31 + e.level;
                    }
                }
                return h;
            }
        }

        private static int HashStockList(int h, List<ThingDefCountClass> items)
        {
            if (items == null) return h * 31;
            h = h * 31 + items.Count;
            for (int i = 0; i < items.Count; i++)
            {
                ThingDefCountClass e = items[i];
                if (e?.thingDef == null) continue;
                h = h * 31 + e.thingDef.shortHash;
                h = h * 31 + e.count;
            }
            return h;
        }

        private void RebuildRowsIfNeeded(WorldObject_Traveler traveler)
        {
            int tick = Find.TickManager?.TicksGame ?? 0;
            int fp = ComputeCargoFingerprint(traveler);
            if (cachedTravelerId == traveler.ID
                && cachedCargoFingerprint == fp
                && tick - lastCacheTick < CacheRefreshInterval)
                return;

            lastCacheTick = tick;
            cachedTravelerId = traveler.ID;
            cachedCargoFingerprint = fp;
            RebuildRows(traveler);
        }

        private void RebuildRows(WorldObject_Traveler traveler)
        {
            rows.Clear();
            RebuildUpgradeCargoRows(traveler);

            if (traveler is WorldObject_Traveler_Outpost_Delivery delivery)
            {
                if (delivery.deliveryVirtualFood > 0.001f)
                {
                    rows.Add(new Row
                    {
                        isVirtualFood = true,
                        itemLabel = "TSA_WD_WarehouseTab_VirtualFood".Translate(),
                        typeLabel = OutpostArmoryUtility.TypeFilterLabel(ArmoryTypeFilter.Food),
                        count = Mathf.RoundToInt(delivery.deliveryVirtualFood),
                        valueLabel = "-"
                    });
                }
                AppendStockRows(delivery.deliveryItems);
                AppendUniqueRows(delivery.CargoUniques);
            }

            if (traveler is WorldObject_Traveler_SettlementBuy buy)
                AppendStockRows(buy.paymentItems);
            if (traveler is WorldObject_Traveler_SettlementGift gift)
                AppendStockRows(gift.paymentItems);
            if (traveler is WorldObject_Traveler_SettlementBribe bribe)
                AppendStockRows(bribe.paymentItems);
            if (traveler is WorldObject_Traveler_DiplomacyNegotiate negotiate)
                AppendStockRows(negotiate.paymentItems);
            if (traveler is WorldObject_Traveler_RapidResponseDropPod rr)
                AppendStockRows(rr.deliveryItems);
        }

        private void RebuildUpgradeCargoRows(WorldObject_Traveler traveler)
        {
            upgradeCargoRows.Clear();
            if (!(traveler is WorldObject_Traveler_Outpost_Upgrade upgrade)) return;

            IReadOnlyList<PendingOutpostUpgradeEntry> cargo = upgrade.UpgradeCargo;
            for (int i = 0; i < cargo.Count; i++)
            {
                PendingOutpostUpgradeEntry e = cargo[i];
                if (e == null || string.IsNullOrEmpty(e.defName)) continue;
                OutpostUpgradeDef def = DefDatabase<OutpostUpgradeDef>.GetNamedSilentFail(e.defName);
                var row = new UpgradeCargoRow { def = def };
                if (def != null)
                {
                    string name = def.LabelCap;
                    if (def.lineTier > 1)
                        name += " (Lv " + def.lineTier + ")";
                    row.name = name;
                    row.impactSummary = Outpost_Upgrade_UI.FormatCompactImpactSummary(def);
                    List<OutpostUpgradeCostEntry> costs = def.GetEffectiveCost();
                    if (costs != null)
                    {
                        for (int c = 0; c < costs.Count; c++)
                        {
                            OutpostUpgradeCostEntry cost = costs[c];
                            if (cost == null || cost.count <= 0) continue;
                            if (!ColonyWorldBuildMaterials.IsAnyStoneBlocksCost(cost) && cost.thingDef == null)
                                continue;
                            row.costs.Add(new UpgradeCargoCostLine
                            {
                                iconDef = ColonyWorldBuildMaterials.GetCostIconThingDef(cost),
                                label = FormatUpgradeCostLabel(cost)
                            });
                        }
                    }
                }
                else
                {
                    row.name = e.defName;
                    row.impactSummary = "";
                }
                upgradeCargoRows.Add(row);
            }
        }

        private static string FormatUpgradeCostLabel(OutpostUpgradeCostEntry c)
        {
            if (c == null) return "";
            if (c.thingDef == ThingDefOf.ComponentIndustrial)
                return "TSA_WD_OutpostUpgrades_ComponentsLabel".Translate().ToString() + " x" + c.count;
            return ColonyWorldBuildMaterials.GetCostDisplayLabel(c) + " x" + c.count;
        }

        private void AppendStockRows(List<ThingDefCountClass> items)
        {
            if (items == null) return;
            for (int i = 0; i < items.Count; i++)
            {
                ThingDefCountClass e = items[i];
                if (e?.thingDef == null || e.count <= 0) continue;
                ArmoryTypeFilter kind = OutpostArmoryUtility.TypeFilterFor(e.thingDef);
                bool hasQuality = e.thingDef.HasComp(typeof(CompQuality));
                float mv = SettlementBuyUtility.MarketValueOf(e);
                rows.Add(new Row
                {
                    stock = e,
                    def = e.thingDef,
                    itemLabel = FormatItemName(e.thingDef, e.stuff),
                    typeLabel = OutpostArmoryUtility.TypeFilterLabel(kind),
                    hasQuality = hasQuality,
                    qualityLabel = hasQuality ? e.quality.GetLabel().CapitalizeFirst() : "",
                    count = e.count,
                    valueLabel = FormatMarketValue(mv)
                });
            }
        }

        private void AppendUniqueRows(ThingOwner<Thing> uniques)
        {
            if (uniques == null) return;
            for (int i = 0; i < uniques.Count; i++)
            {
                Thing t = uniques[i];
                if (t?.def == null || t.Destroyed) continue;
                ArmoryTypeFilter kind = OutpostArmoryUtility.TypeFilterFor(t.def);
                bool hasQuality = t.TryGetQuality(out QualityCategory q);
                int stack = Mathf.Max(1, t.stackCount);
                float mv = Mathf.Max(0f, t.GetStatValue(StatDefOf.MarketValue)) * stack;
                rows.Add(new Row
                {
                    unique = t,
                    def = t.def,
                    itemLabel = OutpostArmoryUtility.DisplayLabel(t),
                    typeLabel = OutpostArmoryUtility.TypeFilterLabel(kind),
                    hasQuality = hasQuality,
                    qualityLabel = hasQuality ? q.GetLabel().CapitalizeFirst() : "",
                    count = stack,
                    valueLabel = FormatMarketValue(mv),
                    isUnique = true
                });
            }
        }

        private static string FormatItemName(ThingDef def, ThingDef stuff)
        {
            if (def == null) return "";
            if (stuff != null)
            {
                string stuffAdj = stuff.LabelAsStuff;
                if (!string.IsNullOrEmpty(stuffAdj))
                    return stuffAdj.CapitalizeFirst() + " " + def.LabelCap;
            }
            return def.LabelCap;
        }

        private static string FormatMarketValue(float marketValue)
        {
            if (marketValue <= 0.001f) return "0";
            return Mathf.RoundToInt(marketValue).ToString();
        }
    }
}
