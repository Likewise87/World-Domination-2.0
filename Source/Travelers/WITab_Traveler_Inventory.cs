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
            public bool isUnique;
        }

        private Vector2 scrollPosition;
        private float scrollViewHeight;
        private readonly List<Row> rows = new List<Row>(16);

        private const float TabHeaderConsumedHeight = 38f;
        private const float TableHeaderHeight = 28f;
        private const float RowIconSize = 28f;
        private const float RowHeight = 36f;
        private const float ColItemIcon = 40f;
        private const float ColType = 100f;
        private const float ColQuality = 90f;
        private const float ColCount = 56f;
        private const float RowRightPad = 10f;

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

            RebuildRows(traveler);

            Rect body = new Rect(0f, 0f, size.x, size.y).ContractedBy(10f);
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(body.x, body.y, body.width, 30f), traveler.LabelCap);
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            Widgets.DrawLineHorizontal(body.x, body.y + 32f, body.width);

            float y = body.y + TabHeaderConsumedHeight;
            if (traveler is WorldObject_Traveler_Outpost_Upgrade upgrade)
            {
                string upgradeLabel = DefDatabase<OutpostUpgradeDef>
                    .GetNamedSilentFail(upgrade.upgradeDefName)?.LabelCap
                    ?? upgrade.upgradeDefName
                    ?? "?";
                string line = "TSA_WD_TravelerInventory_UpgradeLine".Translate(
                    upgradeLabel, upgrade.upgradeLevel.ToString()).ToString();
                Widgets.Label(new Rect(body.x, y, body.width, 24f), line);
                y += 28f;
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

            Rect scrollOuter = new Rect(body.x, y, body.width, body.yMax - y);
            float contentWidth = scrollOuter.width - OutpostTabStatsUi.ScrollbarRightPadding;
            float itemW = Mathf.Max(80f, contentWidth - ColItemIcon - ColType - ColQuality - ColCount - RowRightPad);

            if (rows.Count == 0)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = new Color(1f, 1f, 1f, 0.55f);
                Widgets.Label(scrollOuter, "TSA_WD_TravelerInventory_Empty".Translate());
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
                return;
            }

            scrollViewHeight = TableHeaderHeight + rows.Count * RowHeight + 8f;
            if (scrollViewHeight < scrollOuter.height)
                scrollViewHeight = scrollOuter.height;
            Rect viewRect = new Rect(0f, 0f, contentWidth, scrollViewHeight);

            Widgets.BeginScrollView(scrollOuter, ref scrollPosition, viewRect);
            DrawHeader(0f, itemW, contentWidth);
            float rowY = TableHeaderHeight;
            for (int i = 0; i < rows.Count; i++)
            {
                DrawRow(contentWidth, rowY, i, rows[i], itemW);
                rowY += RowHeight;
            }
            Widgets.EndScrollView();
        }

        private void DrawHeader(float y, float itemW, float width)
        {
            Rect headerRect = new Rect(0f, y, width, TableHeaderHeight);
            Widgets.DrawHighlight(headerRect);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            float x = ColItemIcon;
            Widgets.Label(new Rect(x, y, itemW, TableHeaderHeight), "TSA_WD_Armory_HdrItem".Translate());
            x += itemW;
            Widgets.Label(new Rect(x, y, ColType, TableHeaderHeight), "TSA_WD_Armory_HdrType".Translate());
            x += ColType;
            Widgets.Label(new Rect(x, y, ColQuality, TableHeaderHeight), "TSA_WD_Armory_HdrQuality".Translate());
            x += ColQuality;
            Widgets.Label(new Rect(x, y, ColCount, TableHeaderHeight), "TSA_WD_Armory_HdrCount".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
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
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        private void RebuildRows(WorldObject_Traveler traveler)
        {
            rows.Clear();

            if (traveler is WorldObject_Traveler_Outpost_Delivery delivery)
            {
                if (delivery.deliveryVirtualFood > 0.001f)
                {
                    rows.Add(new Row
                    {
                        isVirtualFood = true,
                        itemLabel = "TSA_WD_WarehouseTab_VirtualFood".Translate(),
                        typeLabel = OutpostArmoryUtility.TypeFilterLabel(ArmoryTypeFilter.Food),
                        count = Mathf.RoundToInt(delivery.deliveryVirtualFood)
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

        private void AppendStockRows(List<ThingDefCountClass> items)
        {
            if (items == null) return;
            for (int i = 0; i < items.Count; i++)
            {
                ThingDefCountClass e = items[i];
                if (e?.thingDef == null || e.count <= 0) continue;
                ArmoryTypeFilter kind = OutpostArmoryUtility.TypeFilterFor(e.thingDef);
                bool hasQuality = e.thingDef.HasComp(typeof(CompQuality));
                rows.Add(new Row
                {
                    stock = e,
                    def = e.thingDef,
                    itemLabel = FormatItemName(e.thingDef, e.stuff),
                    typeLabel = OutpostArmoryUtility.TypeFilterLabel(kind),
                    hasQuality = hasQuality,
                    qualityLabel = hasQuality ? e.quality.GetLabel().CapitalizeFirst() : "",
                    count = e.count
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
                rows.Add(new Row
                {
                    unique = t,
                    def = t.def,
                    itemLabel = OutpostArmoryUtility.DisplayLabel(t),
                    typeLabel = OutpostArmoryUtility.TypeFilterLabel(kind),
                    hasQuality = hasQuality,
                    qualityLabel = hasQuality ? q.GetLabel().CapitalizeFirst() : "",
                    count = Mathf.Max(1, t.stackCount),
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
    }
}
