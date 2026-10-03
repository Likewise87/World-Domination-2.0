using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Warehouse-tab icons. Must live on a dedicated <see cref="StaticConstructorOnStartup"/> type —
    /// not on <see cref="WITab_Outpost_Warehouse"/> — because inspect-tab types are constructed during
    /// def resolve and that can run their static ctor off the main thread.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class WITab_Outpost_WarehouseAssets
    {
        internal static readonly Texture2D LandIcon;
        internal static readonly Texture2D DropPodIcon;
        internal static readonly Texture2D AutoDeliverIconOn;
        internal static readonly Texture2D AutoDeliverIconOff;
        internal static readonly Texture2D DeliveryDestinationIcon;
        internal static readonly Texture2D VirtualFoodIcon;

        static WITab_Outpost_WarehouseAssets()
        {
            LandIcon = ContentFinder<Texture2D>.Get("UI/Commands/Icon_ActiveTravelers", false)
                ?? TexCommand.Install;
            DropPodIcon = ContentFinder<Texture2D>.Get("WorldObjects/DropPod_OutpostGoods", false)
                ?? TexCommand.Install;
            AutoDeliverIconOn = ContentFinder<Texture2D>.Get("UI/Commands/AutoDeliver", false)
                ?? TexCommand.Install;
            AutoDeliverIconOff = ContentFinder<Texture2D>.Get("UI/Commands/AutoDeliver_Off", false)
                ?? AutoDeliverIconOn;
            DeliveryDestinationIcon = ContentFinder<Texture2D>.Get("UI/Commands/DeliveryDestination", false)
                ?? ContentFinder<Texture2D>.Get("UI/Commands/DeliveryTarget", false)
                ?? TexCommand.Attack;
            VirtualFoodIcon = ContentFinder<Texture2D>.Get("UI/Commands/ConvertFood", false)
                ?? TexCommand.Install;
        }
    }

    /// <summary>
    /// Warehouse inventory + shipping controls. Stock table chrome matches
    /// <see cref="Window_AllPlayerGear"/>: checkbox selection, Type / Quality / Count columns,
    /// filterable headers, clean item labels, and send amounts defaulting to max.
    /// </summary>
    public class WITab_Outpost_Warehouse : WITab
    {
        private sealed class StockRowView
        {
            public string key;
            public ThingDefCountClass stock;
            public Thing unique;
            public bool isVirtualFood;
            public ThingDef def;
            public string itemLabel;
            public string typeLabel;
            public ArmoryTypeFilter typeKind;
            public bool hasQuality;
            public QualityCategory quality;
            public string qualityLabel;
            public int count;
            public bool isUnique;
        }

        private Vector2 scrollPosition;
        private float scrollViewHeight;
        private readonly Dictionary<string, int> selectedCounts = new Dictionary<string, int>();
        private readonly Dictionary<string, string> countEditBuffers = new Dictionary<string, string>();
        private readonly HashSet<string> selectedRowIds = new HashSet<string>();
        private int selectedCountsSyncedWarehouseId = int.MinValue;
        private int selectedVirtualFood;
        private string virtualFoodEditBuffer = "0";
        private const string VirtualFoodRowKey = "__wd_virtual_food__";

        private static string sortColumn = "Item";
        private static bool sortAscending = true;
        private static string itemSearchTerm = "";
        private static ArmoryTypeFilter typeFilter = ArmoryTypeFilter.All;
        private static int qualityFilter = QualityFilterAll;

        private List<StockRowView> filterBaseRows = new List<StockRowView>();
        private List<StockRowView> visibleRows = new List<StockRowView>();

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
        private const float ShipNowBtnW = 130f;
        private const float ConvertBtnW = 190f;
        private const float RowRightPad = 10f;
        private const float ColSelect = 36f;
        private const float ColItemIcon = 40f;
        private const float ColType = 100f;
        private const float ColQuality = 90f;
        private const float ColCount = 56f;
        private static float ColSend => BtnW + CountColW + BtnW + BtnGap + BtnW + BtnGap + MaxBtnW;
        private static float FixedColsWidth =>
            ColSelect + ColItemIcon + ColType + ColQuality + ColCount + ColSend + RowRightPad;

        private const int QualityFilterAll = -1;
        private const int QualityFilterNone = -2;

        public WITab_Outpost_Warehouse()
        {
            size = new Vector2(820f, 620f);
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
            RebuildVisibleRows(outpost, comp);

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
            float tableInnerW = body.width;
            float itemW = Mathf.Max(120f, tableInnerW - FixedColsWidth);
            DrawStockTableHeader(body.x, headerY, tableInnerW, itemW);

            float scrollY = headerY + TableHeaderHeight + 4f;
            Rect listRect = new Rect(body.x, scrollY, body.width, body.yMax - scrollY - footerBlock);
            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, scrollViewHeight);
            Widgets.BeginScrollView(listRect, ref scrollPosition, viewRect);

            float innerY = 0f;
            if (visibleRows.Count == 0)
            {
                LabelAnchored(new Rect(0f, innerY, viewRect.width, RowHeight),
                    "TSA_WD_Warehouse_InspectEmpty".Translate(), TextAnchor.MiddleLeft);
                innerY += RowHeight;
            }
            else
            {
                for (int i = 0; i < visibleRows.Count; i++)
                {
                    DrawStockRow(viewRect.width, innerY, i, visibleRows[i], itemW);
                    innerY += RowHeight;
                }
            }

            if (Event.current.type == EventType.Layout) scrollViewHeight = innerY;
            Widgets.EndScrollView();

            float footY = body.yMax - footerBlock + FooterGap;
            Widgets.DrawLineHorizontal(body.x, footY - 4f, body.width);
            DrawFooterRows(body.x, footY, body.width, outpost, comp);

            PawnRosterHeaderFilter.DrawDropdownIfOpen();
        }

        private void RebuildVisibleRows(WorldObject_WD_Outpost outpost, CompOutpostWarehouse comp)
        {
            var rows = new List<StockRowView>();

            int shippableVf = Outpost_Warehouse_Delivery.GetShippableVirtualFoodInt(outpost);
            rows.Add(new StockRowView
            {
                key = VirtualFoodRowKey,
                isVirtualFood = true,
                def = null,
                itemLabel = "TSA_WD_WarehouseTab_VirtualFood".Translate(),
                typeKind = ArmoryTypeFilter.Food,
                typeLabel = OutpostArmoryUtility.TypeFilterLabel(ArmoryTypeFilter.Food),
                hasQuality = false,
                qualityLabel = "",
                count = shippableVf
            });

            var items = comp.storedItems;
            if (items != null)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    var e = items[i];
                    if (e?.thingDef == null || e.count <= 0) continue;
                    ArmoryTypeFilter kind = OutpostArmoryUtility.TypeFilterFor(e.thingDef);
                    bool hasQuality = e.thingDef.HasComp(typeof(CompQuality));
                    rows.Add(new StockRowView
                    {
                        key = CompOutpostWarehouse.StockKey(e),
                        stock = e,
                        def = e.thingDef,
                        itemLabel = FormatItemName(e.thingDef, e.stuff),
                        typeKind = kind,
                        typeLabel = OutpostArmoryUtility.TypeFilterLabel(kind),
                        hasQuality = hasQuality,
                        quality = e.quality,
                        qualityLabel = hasQuality ? e.quality.GetLabel().CapitalizeFirst() : "",
                        count = e.count
                    });
                }
            }

            ThingOwner<Thing> uniques = CompOutpostArmory.Get(outpost)?.Uniques;
            if (uniques != null)
            {
                for (int i = 0; i < uniques.Count; i++)
                {
                    Thing t = uniques[i];
                    if (t?.def == null || t.Destroyed) continue;
                    ArmoryTypeFilter kind = OutpostArmoryUtility.TypeFilterFor(t.def);
                    bool hasQuality = t.TryGetQuality(out QualityCategory q);
                    rows.Add(new StockRowView
                    {
                        key = UniqueKey(t),
                        unique = t,
                        def = t.def,
                        itemLabel = OutpostArmoryUtility.DisplayLabel(t),
                        typeKind = kind,
                        typeLabel = OutpostArmoryUtility.TypeFilterLabel(kind),
                        hasQuality = hasQuality,
                        quality = q,
                        qualityLabel = hasQuality ? q.GetLabel().CapitalizeFirst() : "",
                        count = Mathf.Max(1, t.stackCount),
                        isUnique = true
                    });
                }
            }

            filterBaseRows = rows;
            visibleRows = new List<StockRowView>(rows.Count);
            string itemFilter = string.IsNullOrEmpty(itemSearchTerm) ? null : itemSearchTerm.ToLowerInvariant();
            for (int i = 0; i < rows.Count; i++)
            {
                StockRowView r = rows[i];
                bool typeOk = typeFilter == ArmoryTypeFilter.All
                    || (r.isVirtualFood
                        ? typeFilter == ArmoryTypeFilter.Food
                        : OutpostArmoryUtility.MatchesTypeFilter(r.def, typeFilter));
                if (!typeOk) continue;
                if (qualityFilter == QualityFilterNone && r.hasQuality) continue;
                if (qualityFilter >= 0 && (!r.hasQuality || (int)r.quality != qualityFilter)) continue;
                if (itemFilter != null
                    && (r.itemLabel ?? "").ToLowerInvariant().IndexOf(itemFilter, System.StringComparison.Ordinal) < 0)
                    continue;
                visibleRows.Add(r);
            }

            SortVisibleRows();
        }

        private void SortVisibleRows()
        {
            visibleRows.Sort((a, b) =>
            {
                int cmp;
                switch (sortColumn)
                {
                    case "Type":
                        cmp = string.Compare(a.typeLabel, b.typeLabel, System.StringComparison.OrdinalIgnoreCase);
                        break;
                    case "Quality":
                        cmp = a.hasQuality.CompareTo(b.hasQuality);
                        if (cmp == 0 && a.hasQuality) cmp = a.quality.CompareTo(b.quality);
                        break;
                    case "Count":
                        cmp = a.count.CompareTo(b.count);
                        break;
                    default:
                        cmp = string.Compare(a.itemLabel, b.itemLabel, System.StringComparison.OrdinalIgnoreCase);
                        break;
                }
                if (cmp == 0)
                    cmp = string.Compare(a.itemLabel, b.itemLabel, System.StringComparison.OrdinalIgnoreCase);
                return sortAscending ? cmp : -cmp;
            });
        }

        private void DrawStockTableHeader(float x, float y, float width, float itemW)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            float curX = x;

            DrawSelectAllHeader(ref curX, y);
            curX += ColItemIcon;

            PawnRosterHeaderFilter.DrawFilterableHeader(
                ref curX, y, itemW, TableHeaderHeight,
                "TSA_WD_Armory_HdrItem".Translate(),
                sortColumn == "Item", sortAscending,
                TextAnchor.MiddleLeft,
                !itemSearchTerm.NullOrEmpty(),
                "TSA_WD_FilterByName".Translate(),
                icon => PawnRosterHeaderFilter.OpenTextDropdown(
                    icon,
                    "TSA_WD_FilterByName".Translate(),
                    "TSA_WD_FilterByName".Translate(),
                    () => itemSearchTerm,
                    v => { itemSearchTerm = v ?? ""; },
                    () => { itemSearchTerm = ""; }),
                () => SetSort("Item"));

            PawnRosterHeaderFilter.DrawFilterableHeader(
                ref curX, y, ColType, TableHeaderHeight,
                "TSA_WD_Armory_HdrType".Translate(),
                sortColumn == "Type", sortAscending,
                TextAnchor.MiddleCenter,
                typeFilter != ArmoryTypeFilter.All,
                "TSA_WD_Armory_WeaponFilterTip".Translate(),
                icon => PawnRosterHeaderFilter.OpenChoiceDropdown(
                    icon,
                    "TSA_WD_Armory_HdrType".Translate(),
                    BuildTypeFilterChoices()),
                () => SetSort("Type"));

            PawnRosterHeaderFilter.DrawFilterableHeader(
                ref curX, y, ColQuality, TableHeaderHeight,
                "TSA_WD_Armory_HdrQuality".Translate(),
                sortColumn == "Quality", sortAscending,
                TextAnchor.MiddleCenter,
                qualityFilter != QualityFilterAll,
                "TSA_WD_AllInventory_QualityFilterTip".Translate(),
                icon => PawnRosterHeaderFilter.OpenChoiceDropdown(
                    icon,
                    "TSA_WD_Armory_HdrQuality".Translate(),
                    BuildQualityFilterChoices()),
                () => SetSort("Quality"));

            DrawSortHeader(ref curX, y, ColCount, "TSA_WD_Armory_HdrCount".Translate(), "Count");
            LabelAnchored(
                new Rect(curX, y, ColSend, TableHeaderHeight),
                "TSA_WD_WarehouseTab_ColShipAmt".Translate(),
                TextAnchor.MiddleCenter);

            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Widgets.DrawLineHorizontal(x, y + TableHeaderHeight, width);
        }

        private void DrawSelectAllHeader(ref float curX, float y)
        {
            Rect selHdr = new Rect(curX, y, ColSelect, TableHeaderHeight);
            if (Mouse.IsOver(selHdr)) Widgets.DrawHighlight(selHdr);

            int selectable = visibleRows.Count;
            int selectedSelectable = 0;
            for (int i = 0; i < visibleRows.Count; i++)
            {
                if (selectedRowIds.Contains(visibleRows[i].key))
                    selectedSelectable++;
            }
            bool all = selectable > 0 && selectedSelectable == selectable;
            bool next = all;
            Widgets.Checkbox(
                new Vector2(selHdr.x + (ColSelect - 24f) * 0.5f, selHdr.y + (TableHeaderHeight - 24f) * 0.5f),
                ref next);
            if (next != all)
            {
                if (next)
                {
                    for (int i = 0; i < visibleRows.Count; i++)
                        selectedRowIds.Add(visibleRows[i].key);
                }
                else
                {
                    for (int i = 0; i < visibleRows.Count; i++)
                        selectedRowIds.Remove(visibleRows[i].key);
                }
            }
            curX += ColSelect;
        }

        private void DrawSortHeader(ref float curX, float y, float width, string label, string tag)
        {
            Rect headerRect = new Rect(curX, y, width, TableHeaderHeight);
            if (Mouse.IsOver(headerRect)) Widgets.DrawHighlight(headerRect);
            Text.Anchor = TextAnchor.MiddleCenter;
            string headerText = label + (sortColumn == tag ? (sortAscending ? " ▲" : " ▼") : "");
            Widgets.Label(headerRect, headerText.Truncate(width - 4f));
            if (Widgets.ButtonInvisible(headerRect)) SetSort(tag);
            Text.Anchor = TextAnchor.UpperLeft;
            curX += width;
        }

        private void SetSort(string tag)
        {
            if (sortColumn == tag) sortAscending = !sortAscending;
            else { sortColumn = tag; sortAscending = true; }
        }

        private List<HeaderFilterChoice> BuildTypeFilterChoices()
        {
            var defs = new List<ThingDef>(filterBaseRows.Count);
            for (int i = 0; i < filterBaseRows.Count; i++)
            {
                if (filterBaseRows[i].def != null)
                    defs.Add(filterBaseRows[i].def);
            }
            return OutpostArmoryUtility.BuildTypeFilterChoices(
                defs, typeFilter, f => { typeFilter = f; });
        }

        private List<HeaderFilterChoice> BuildQualityFilterChoices()
        {
            int total = filterBaseRows.Count;
            int none = 0;
            var counts = new int[8];
            for (int i = 0; i < filterBaseRows.Count; i++)
            {
                StockRowView r = filterBaseRows[i];
                if (!r.hasQuality) { none++; continue; }
                int idx = (int)r.quality;
                if (idx >= 0 && idx < counts.Length) counts[idx]++;
            }

            string CountOf(int n) => total > 0 ? n + "/" + total : null;

            var list = new List<HeaderFilterChoice>
            {
                new HeaderFilterChoice(
                    "TSA_WD_Armory_WeaponFilter_All".Translate(),
                    qualityFilter == QualityFilterAll,
                    () => { qualityFilter = QualityFilterAll; },
                    separatorAfter: true,
                    countLabel: CountOf(total)),
                new HeaderFilterChoice(
                    "TSA_WD_AllInventory_QualityNone".Translate(),
                    qualityFilter == QualityFilterNone,
                    () => { qualityFilter = QualityFilterNone; },
                    countLabel: CountOf(none))
            };

            foreach (QualityCategory q in System.Enum.GetValues(typeof(QualityCategory)))
            {
                QualityCategory captured = q;
                int idx = (int)q;
                int n = idx >= 0 && idx < counts.Length ? counts[idx] : 0;
                list.Add(new HeaderFilterChoice(
                    q.GetLabel().CapitalizeFirst(),
                    qualityFilter == idx,
                    () => { qualityFilter = (int)captured; },
                    countLabel: CountOf(n)));
            }

            return list;
        }

        private static string UniqueKey(Thing t) => "__wd_unique__" + t.ThingID;

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

        private void DrawFooterRows(float x, float y, float width, WorldObject_WD_Outpost outpost, CompOutpostWarehouse comp)
        {
            float rowY = y;
            bool viaPod = OutpostDispatchMode.GetViaDropPod(outpost);
            float controlsX = x + FooterLabelW + 6f;
            float controlsW = width - FooterLabelW - 6f;
            Color cyan = WorldOverlayLineMaterials.DarkCyanColor;

            // Row 1: delivery type icon + Ship Now + Convert (Ship Now / Convert flush right)
            DrawFooterRowLabel(x, rowY, "TSA_WD_WarehouseTab_RowDeliveryType".Translate());

            float methodW = PlayerPawnDropPodUtility.ModeIconSize;
            Rect methodRect = new Rect(controlsX, rowY, methodW, FooterRowHeight);
            PlayerPawnDropPodUtility.DrawGoodsDispatchModeIcon(
                methodRect,
                viaPod,
                () => OutpostDispatchMode.SetViaDropPod(outpost, false),
                () => OutpostDispatchMode.TrySetViaDropPod(outpost, true));

            Rect shipNowRect = new Rect(methodRect.xMax + 8f, rowY, ShipNowBtnW, FooterRowHeight);
            bool canShip = HasAnythingToShip(outpost, comp);
            bool prevEnabled = GUI.enabled;
            GUI.enabled = prevEnabled && canShip;
            if (WorldDomination_UIUtils.ButtonTextWithIcon(
                    shipNowRect,
                    WITab_Outpost_WarehouseAssets.DeliveryDestinationIcon,
                    "TSA_WD_WarehouseTab_ShipNow".Translate())
                && canShip)
                BeginAdHocSend(outpost, comp);
            GUI.enabled = prevEnabled;
            TooltipHandler.TipRegion(shipNowRect, canShip
                ? "TSA_WD_WarehouseTab_AdHocSendTip".Translate()
                : "TSA_WD_Warehouse_ShipNothingSelected".Translate());

            Rect convertRect = new Rect(shipNowRect.xMax + 8f, rowY, ConvertBtnW, FooterRowHeight);
            bool canConvert = OutpostFoodConversion.CanConvert(outpost)
                && OutpostFoodConversion.PoolHeadroom(outpost) > 0.01f
                && HasPickedFood(comp);
            GUI.enabled = prevEnabled && canConvert;
            if (WorldDomination_UIUtils.ButtonTextWithIcon(
                    convertRect,
                    WITab_Outpost_WarehouseAssets.VirtualFoodIcon,
                    "TSA_WD_WarehouseTab_ConvertFood".Translate())
                && canConvert)
                ConvertPickedFood(outpost, comp);
            GUI.enabled = prevEnabled;
            GUI.color = Color.white;
            TooltipHandler.TipRegion(convertRect, canConvert
                ? "TSA_WD_WarehouseTab_ConvertFoodTip".Translate()
                : "TSA_WD_WarehouseTab_ConvertFoodNone".Translate());

            rowY += FooterRowHeight + FooterGap;

            // Row 2: daily auto delivery ends under Ship Now (same right edge).
            DrawFooterRowLabel(x, rowY, "TSA_WD_WarehouseTab_RowDailyAuto".Translate());

            float autoW = Mathf.Max(130f, shipNowRect.xMax - controlsX);
            Rect autoRect = new Rect(controlsX, rowY, autoW, FooterRowHeight);
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
                return WITab_Outpost_WarehouseAssets.AutoDeliverIconOff;

            WorldObject dest = comp.ResolveShipDestination();
            if (dest is WorldObject_WD_Outpost wo && wo.def?.ExpandingIconTexture != null)
                return wo.def.ExpandingIconTexture;
            if (dest?.Faction?.def?.FactionIcon != null)
                return dest.Faction.def.FactionIcon;
            return WITab_Outpost_WarehouseAssets.AutoDeliverIconOn;
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
                WITab_Outpost_WarehouseAssets.LandIcon,
                WorldOverlayLineMaterials.DarkCyanColor));

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
                WITab_Outpost_WarehouseAssets.DropPodIcon,
                WorldOverlayLineMaterials.DarkCyanColor);
            if (!podsResearched)
            {
                podOpt.Disabled = true;
                podOpt.tooltip = new TipSignal("TSA_WD_DispatchMode_NeedsResearch".Translate());
            }
            options.Add(podOpt);
            Find.WindowStack.Add(new FloatMenu(options));
        }

        /// <summary>Where the auto delivery menu sits, so a checkbox toggle reopens it in place.</summary>
        private static Vector2? autoDeliveryMenuPos;

        private static void OpenAutoDeliveryMenu(
            WorldObject_WD_Outpost outpost,
            CompOutpostWarehouse comp,
            Vector2? atPosition = null)
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
                WITab_Outpost_WarehouseAssets.AutoDeliverIconOff,
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
                    WorldOverlayLineMaterials.DarkCyanColor));
            }

            AddAutoIncludeOption(options, outpost, comp, "TSA_WD_WarehouseTab_AutoInclude_Apparel",
                () => comp.autoShipApparel, v => comp.autoShipApparel = v);
            AddAutoIncludeOption(options, outpost, comp, "TSA_WD_WarehouseTab_AutoInclude_Weapons",
                () => comp.autoShipWeapons, v => comp.autoShipWeapons = v);
            AddAutoIncludeOption(options, outpost, comp, "TSA_WD_WarehouseTab_AutoInclude_Food",
                () => comp.autoShipFood, v => comp.autoShipFood = v);
            AddAutoIncludeOption(options, outpost, comp, "TSA_WD_WarehouseTab_AutoInclude_Drugs",
                () => comp.autoShipDrugs, v => comp.autoShipDrugs = v);
            AddAutoIncludeOption(options, outpost, comp, "TSA_WD_WarehouseTab_AutoInclude_Medicine",
                () => comp.autoShipMedicine, v => comp.autoShipMedicine = v);

            var menu = new WdFixedPosFloatMenu(options, atPosition);
            Find.WindowStack.Add(menu);
            autoDeliveryMenuPos = menu.windowRect.position;
        }

        /// <summary>
        /// Checkbox entry; toggling reopens the menu so several categories can be set in one go.
        /// The reopened menu is pinned to the old one's position so the rows do not move under the cursor.
        /// </summary>
        private static void AddAutoIncludeOption(
            List<FloatMenuOption> options,
            WorldObject_WD_Outpost outpost,
            CompOutpostWarehouse comp,
            string labelKey,
            System.Func<bool> get,
            System.Action<bool> set)
        {
            bool on = get();
            options.Add(new FloatMenuOption(
                labelKey.Translate(),
                () =>
                {
                    set(!get());
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    OpenAutoDeliveryMenu(outpost, comp, autoDeliveryMenuPos);
                },
                on ? Widgets.CheckboxOnTex : Widgets.CheckboxOffTex,
                Color.white));
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
            List<Thing> uniques = BuildSelectedUniques(outpost);
            float virtualFood = selectedRowIds.Contains(VirtualFoodRowKey) ? selectedVirtualFood : 0;
            if (!Outpost_Warehouse_Delivery.RequestHasCargo(request, virtualFood) && uniques.Count == 0)
            {
                Messages.Message("TSA_WD_Warehouse_ShipNothingSelected".Translate(), outpost, MessageTypeDefOf.RejectInput);
                return;
            }

            if (outpost.ManualDefenseActive)
            {
                Messages.Message("TSA_WD_Armory_FailManualDefense".Translate(), outpost, MessageTypeDefOf.RejectInput);
                return;
            }

            bool viaDropPod = OutpostDispatchMode.GetViaDropPod(outpost);
            if (viaDropPod && !RapidResponseUtility.TransportPodsResearched())
            {
                Messages.Message("TSA_WD_RapidResponse_DropPodsNeedResearch".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            OpenAdHocDestinationDialog(outpost, request, uniques, virtualFood, viaDropPod);
        }

        private void OpenAdHocDestinationDialog(
            WorldObject_WD_Outpost outpost,
            List<ThingDefCountClass> request,
            List<Thing> uniques,
            float virtualFood,
            bool viaDropPod)
        {
            var dests = Outpost_Warehouse_Delivery.CollectValidShipmentDestinations(
                outpost, request, uniques?.Count ?? 0, virtualFood);
            Find.WindowStack.Add(new Dialog_AdHocShipmentDestination(
                dests,
                outpost,
                dest =>
                {
                    Action launch = () =>
                    {
                        if (OutpostStorageShipping.TryLaunch(outpost, request, uniques, virtualFood, dest, viaDropPod))
                        {
                            selectedCounts.Clear();
                            countEditBuffers.Clear();
                            selectedRowIds.Clear();
                            selectedVirtualFood = 0;
                            virtualFoodEditBuffer = "0";
                        }
                    };
                    if (viaDropPod
                        && dest != null
                        && PlayerPawnDropPodUtility.ConfirmHostileAaThen(
                            outpost.Tile.tileId, dest.Tile.tileId, launch))
                        return;
                    launch();
                },
                () =>
                {
                    PlayerPawnDropPodUtility.PrepareWorldMapDestinationPick(outpost);
                    Outpost_Warehouse_Delivery.BeginAdHocShipmentTargeting(
                        outpost,
                        request,
                        uniques,
                        viaDropPod,
                        () =>
                        {
                            selectedCounts.Clear();
                            countEditBuffers.Clear();
                            selectedRowIds.Clear();
                            selectedVirtualFood = 0;
                            virtualFoodEditBuffer = "0";
                            CloseTab();
                        },
                        virtualFood,
                        skipCameraJump: true);
                },
                viaDropPod: viaDropPod,
                launchCount: 1));
        }

        /// <summary>True when at least one checked row has a positive ship amount (stock, unique, or virtual food).</summary>
        private bool HasAnythingToShip(WorldObject_WD_Outpost outpost, CompOutpostWarehouse comp)
        {
            float virtualFood = selectedRowIds.Contains(VirtualFoodRowKey) ? selectedVirtualFood : 0;
            if (Outpost_Warehouse_Delivery.RequestHasCargo(BuildSelectedRequest(comp), virtualFood))
                return true;
            return BuildSelectedUniques(outpost).Count > 0;
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
                if (!selectedRowIds.Contains(key)) continue;
                if (!selectedCounts.TryGetValue(key, out int pick) || pick <= 0) continue;
                request.Add(CompOutpostWarehouse.PlainStockRow(e, Mathf.Min(pick, e.count)));
            }
            return request;
        }

        private List<Thing> BuildSelectedUniques(WorldObject_WD_Outpost outpost)
        {
            var result = new List<Thing>();
            ThingOwner<Thing> uniques = CompOutpostArmory.Get(outpost)?.Uniques;
            if (uniques == null) return result;
            for (int i = 0; i < uniques.Count; i++)
            {
                Thing t = uniques[i];
                if (t == null || t.Destroyed) continue;
                string key = UniqueKey(t);
                if (!selectedRowIds.Contains(key)) continue;
                if (selectedCounts.TryGetValue(key, out int pick) && pick > 0)
                    result.Add(t);
            }
            return result;
        }

        private bool HasPickedFood(CompOutpostWarehouse comp)
        {
            var items = comp?.storedItems;
            if (items == null) return false;
            for (int i = 0; i < items.Count; i++)
            {
                var e = items[i];
                if (e?.thingDef == null || e.count <= 0) continue;
                if (!Outpost_Warehouse_Delivery.IsNutritionGivingStock(e.thingDef)) continue;
                string key = CompOutpostWarehouse.StockKey(e);
                if (!selectedRowIds.Contains(key)) continue;
                if (selectedCounts.TryGetValue(key, out int pick) && pick > 0)
                    return true;
            }
            return false;
        }

        /// <summary>Converts only checked food rows at their send amounts; other selections stay.</summary>
        private void ConvertPickedFood(WorldObject_WD_Outpost outpost, CompOutpostWarehouse comp)
        {
            var picks = new List<ThingDefCountClass>();
            var items = comp.storedItems;
            for (int i = 0; i < items.Count; i++)
            {
                var e = items[i];
                if (e?.thingDef == null || e.count <= 0) continue;
                if (!Outpost_Warehouse_Delivery.IsNutritionGivingStock(e.thingDef)) continue;
                string key = CompOutpostWarehouse.StockKey(e);
                if (!selectedRowIds.Contains(key)) continue;
                if (!selectedCounts.TryGetValue(key, out int pick) || pick <= 0) continue;
                picks.Add(CompOutpostWarehouse.PlainStockRow(e, Mathf.Min(pick, e.count)));
            }

            float applied = OutpostFoodConversion.ConvertRows(outpost, picks, out List<ThingDefCountClass> converted);
            for (int i = 0; i < converted.Count; i++)
            {
                string key = CompOutpostWarehouse.StockKey(converted[i]);
                selectedRowIds.Remove(key);
                if (selectedCounts.TryGetValue(key, out int pick))
                    SetPick(key, Mathf.Max(0, pick - converted[i].count));
            }

            Messages.Message(
                applied > 0.01f
                    ? "TSA_WD_WarehouseTab_ConvertedFood".Translate(applied.ToString("F1"), outpost.LabelCap)
                    : "TSA_WD_WarehouseTab_ConvertFoodNone".Translate(),
                outpost,
                applied > 0.01f ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.RejectInput,
                false);
            if (applied > 0.01f) Window_OutpostOverview.InvalidateCache();
        }

        private void SyncSelectedCounts(WorldObject_WD_Outpost outpost, CompOutpostWarehouse comp)
        {
            if (selectedCountsSyncedWarehouseId != outpost.ID)
            {
                selectedCounts.Clear();
                countEditBuffers.Clear();
                selectedRowIds.Clear();
                selectedVirtualFood = 0;
                virtualFoodEditBuffer = "0";
                selectedCountsSyncedWarehouseId = outpost.ID;
            }

            int shippableVf = Outpost_Warehouse_Delivery.GetShippableVirtualFoodInt(outpost);
            // First visit to this warehouse: preselect full shippable virtual food.
            if (!countEditBuffers.ContainsKey(VirtualFoodRowKey) && selectedVirtualFood == 0 && shippableVf > 0)
            {
                selectedVirtualFood = shippableVf;
                virtualFoodEditBuffer = selectedVirtualFood.ToString();
                countEditBuffers[VirtualFoodRowKey] = virtualFoodEditBuffer;
            }
            selectedVirtualFood = Mathf.Clamp(selectedVirtualFood, 0, shippableVf);

            var items = comp.storedItems;
            if (items == null) return;

            var stillPresent = new HashSet<string> { VirtualFoodRowKey };
            for (int i = 0; i < items.Count; i++)
            {
                var e = items[i];
                if (e?.thingDef == null || e.count <= 0) continue;
                string key = CompOutpostWarehouse.StockKey(e);
                stillPresent.Add(key);
                if (!selectedCounts.ContainsKey(key))
                    selectedCounts[key] = e.count;
                else
                    selectedCounts[key] = Mathf.Clamp(selectedCounts[key], 0, e.count);
            }

            ThingOwner<Thing> uniques = CompOutpostArmory.Get(outpost)?.Uniques;
            if (uniques != null)
            {
                for (int i = 0; i < uniques.Count; i++)
                {
                    Thing t = uniques[i];
                    if (t == null || t.Destroyed) continue;
                    string key = UniqueKey(t);
                    stillPresent.Add(key);
                    if (!selectedCounts.ContainsKey(key))
                        selectedCounts[key] = 1;
                    else
                        selectedCounts[key] = Mathf.Clamp(selectedCounts[key], 0, 1);
                }
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
                selectedRowIds.Remove(toRemove[i]);
            }
        }

        private void DrawStockRow(float width, float y, int rowIndex, StockRowView row, float itemW)
        {
            Rect rowRect = new Rect(0f, y, width, RowHeight);
            if (rowIndex % 2 == 0) Widgets.DrawHighlight(rowRect);
            if (Mouse.IsOver(rowRect)) Widgets.DrawLightHighlight(rowRect);

            Text.Font = GameFont.Tiny;
            float curX = 0f;

            bool selected = selectedRowIds.Contains(row.key);
            bool next = selected;
            Widgets.Checkbox(
                new Vector2(curX + (ColSelect - 24f) * 0.5f, y + (RowHeight - 24f) * 0.5f),
                ref next);
            if (next != selected)
            {
                if (next) selectedRowIds.Add(row.key);
                else selectedRowIds.Remove(row.key);
            }
            curX += ColSelect;

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
            string taintTip = OutpostArmoryUtility.TaintedTip(row.unique);
            if (taintTip != null) GUI.color = OutpostArmoryUtility.TaintedColor;
            else if (row.isUnique) GUI.color = WorldOverlayLineMaterials.DarkCyanColor;
            Widgets.Label(labelRect, row.itemLabel.Truncate(itemW - 6f));
            GUI.color = Color.white;
            string tip = row.itemLabel;
            if (row.isVirtualFood)
            {
                var logi = (SelObject as WorldObject_WD_Outpost)?.GetComponent<CompOutpostLogistics>();
                float pool = logi?.currentFood ?? 0f;
                tip = "TSA_WD_WarehouseTab_VirtualFoodDetail".Translate(
                    pool.ToString("F0"),
                    Outpost_Warehouse_Delivery.VirtualFoodShipReserve.ToString("F0"),
                    row.count.ToString());
            }
            else if (taintTip != null)
                tip += "\n\n" + taintTip;
            TooltipHandler.TipRegion(labelRect, tip);
            if (!row.isVirtualFood && Widgets.ButtonInvisible(labelRect))
            {
                if (row.unique != null)
                    Find.WindowStack.Add(new Dialog_InfoCard(row.unique));
                else if (row.stock != null)
                    OpenStockInfoCard(row.stock);
            }
            curX += itemW;

            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(curX, y, ColType, RowHeight), row.typeLabel.Truncate(ColType - 6f));
            curX += ColType;

            Widgets.Label(new Rect(curX, y, ColQuality, RowHeight),
                (row.hasQuality ? row.qualityLabel : "").Truncate(ColQuality - 6f));
            curX += ColQuality;

            Widgets.Label(new Rect(curX, y, ColCount, RowHeight), row.count.ToString());
            curX += ColCount;

            if (row.isVirtualFood)
                DrawVirtualFoodPickControls(curX, y, row.count);
            else
                DrawPickControls(curX, y, row.key, row.count);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawVirtualFoodPickControls(float controlsX, float y, int shippable)
        {
            selectedVirtualFood = Mathf.Clamp(selectedVirtualFood, 0, shippable);
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

        private void DrawPickControls(float controlsX, float y, string key, int stored)
        {
            if (!selectedCounts.TryGetValue(key, out int pick)) pick = stored;
            pick = Mathf.Clamp(pick, 0, stored);
            selectedCounts[key] = pick;

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
