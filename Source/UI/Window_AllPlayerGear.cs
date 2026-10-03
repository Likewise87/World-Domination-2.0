using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI.Group;
using Verse.Sound;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Every player-owned armory-eligible item: Armory stock, colony map stacks, and gear
    /// equipped or carried by player pawns. Answers "which item is where" and launches a
    /// shipment from store/map selection without needing a pawn at either end.
    /// </summary>
    [StaticConstructorOnStartup]
    public class Window_AllPlayerGear : Window
    {
        private const float RowHeight = 40f;
        private const float HeaderHeight = 28f;
        private const float ToolbarHeight = 40f;
        private const float ToolbarBtnHeight = 34f;
        private const float ToolbarBtnGap = 10f;
        private const float ActionStackTop = 4f;
        private const float FooterHeight = 22f;
        private const int UpdateIntervalTicks = 300;
        private const int MaxRows = 250;

        private const float LocIconPad = 4f;
        private const float LocIconDrawSize = 40f;
        private const float ColIcon = LocIconDrawSize + LocIconPad * 2f;
        private const float ColLocType = 108f;
        private const float ColLocName = 140f;
        private const float ColSelect = 36f;
        private const float ItemIconDrawSize = 28f;
        private const float ColItemIcon = 40f;
        private const float ColItem = 180f;
        private const float ColEquippedPortrait = 32f;
        private const float ColEquippedName = 100f;
        private const float ColEquippedBy = ColEquippedPortrait + ColEquippedName;
        private const float EquippedPortraitDrawSize = 28f;
        private const float ColType = 100f;
        private const float ColQuality = 90f;
        private const float ColCount = 56f;
        private const float BtnW = 28f;
        private const float MaxBtnW = 40f;
        private const float CountColW = 56f;
        private const float BtnGap = 4f;
        private const float ColSendCount = BtnW + CountColW + BtnW + BtnGap + BtnW + BtnGap + MaxBtnW;
        private const float ColPadding = 12f;

        /// <summary>Match <c>WITab_Outpost_Warehouse.ShipNowBtnW</c> / footer row height.</summary>
        private const float SendBtnWidth = 130f;
        private const float SelectedLabelWidth = 130f;

        /// <summary>Quality filter sentinel: any quality (including none).</summary>
        private const int QualityFilterAll = -1;
        /// <summary>Quality filter sentinel: items with no CompQuality.</summary>
        private const int QualityFilterNone = -2;

        private static readonly Texture2D SendIcon =
            ContentFinder<Texture2D>.Get("UI/Commands/DeliveryDestination", false)
            ?? TexCommand.Attack;
        private static readonly Texture2D CaravanLocIcon =
            ContentFinder<Texture2D>.Get("UI/Commands/Icon_ActiveTravelers", false)
            ?? TexCommand.Install;

        private sealed class InventoryRow
        {
            public string rowId;
            public WorldObject source;
            public string sourceLabel;
            /// <summary>Cached JumpTip with <see cref="sourceLabel"/>; set at row build (no per-frame Translate(args)).</summary>
            public string jumpTip;
            public string sourceTypeLabel;
            public PlayerPawnLocationKind locationKind;
            public Texture2D locationIcon;
            public Color locationIconColor = Color.white;
            public bool sourceIsColony;
            public ThingDefCountClass stockRow;
            public Thing thing;
            public ThingDef def;
            public ThingDef stuff;
            public string itemLabel;
            public string typeLabel;
            public string qualityLabel;
            public bool hasQuality;
            public QualityCategory quality;
            public int count;
            public bool isUnique;
            /// <summary>Equipped or carried on a pawn. Shippable when not mid-fight (see <see cref="SendBlockedKey"/>).</summary>
            public bool isOnPawn;
            public Pawn holderPawn;
            public string equippedByLabel = "";
            /// <summary>Item label plus on-pawn tip when applicable; taint tip appended at draw if needed.</summary>
            public string itemTip;
        }

        private Vector2 scrollPos;
        private float lastScrollViewportHeight = 400f;
        private static string sortColumn = "Item";
        private static bool sortAscending = true;
        private static string itemSearchTerm = "";
        private static string locationNameSearchTerm = "";
        private static string locationTypeSearchTerm = "";
        private static string equippedBySearchTerm = "";
        private static ArmoryTypeFilter typeFilter = ArmoryTypeFilter.All;
        private static int qualityFilter = QualityFilterAll;
        private static bool viaDropPod;
        private static bool _cacheInvalidated;

        private int lastUpdateTick = -9999;
        private List<InventoryRow> cachedRows = new List<InventoryRow>();
        private List<InventoryRow> filterBaseRows = new List<InventoryRow>();
        private int totalMatchCount;
        private readonly HashSet<string> selectedRowIds = new HashSet<string>();
        private readonly Dictionary<string, int> sendCounts = new Dictionary<string, int>();
        private readonly Dictionary<string, string> sendCountBuffers = new Dictionary<string, string>();
        private string cachedOnPawnTip = "";
        private string cachedQuantityAdjustTip = "";
        private string cachedBlockedCaravanTip = "";
        private string cachedBlockedDefenseTip = "";
        private string cachedBlockedAssaultTip = "";
        /// <summary>Set in <see cref="RefreshDrawStringCaches"/> for static row builders during rebuild.</summary>
        private static string buildOnPawnTip = "";

        public override Vector2 InitialSize => new Vector2(UI.screenWidth, UI.screenHeight);

        public Window_AllPlayerGear()
        {
            doCloseX = true;
            closeOnCancel = true;
            draggable = false;
            preventCameraMotion = false;
            forcePause = false;
        }

        public static void InvalidateCache() => _cacheInvalidated = true;

        public override void DoWindowContents(Rect inRect)
        {
            WdNavWindows.ProcessHotkeys();
            if (!IsOpen) return;
            if (PawnRosterHeaderFilter.TryCloseDropdownOnCancel()) return;
            if (WdWindowEsc.TryCloseOnCancel(this)) return;

            if (_cacheInvalidated) { lastUpdateTick = -9999; _cacheInvalidated = false; }

            float totalWidth = ComputeTotalTableWidth();
            float tableRight = Mathf.Min(totalWidth, inRect.width - 5f);

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width * 0.4f, 32f), "TSA_WD_AllInventory_Title".Translate());
            Text.Font = GameFont.Small;

            DrawToolbar(tableRight);

            float headerTop = ToolbarHeight + 4f;
            float listTop = headerTop + HeaderHeight + 4f;
            bool showCapFooter = totalMatchCount > MaxRows;
            float footerH = showCapFooter ? FooterHeight : 0f;
            float tableHeight = inRect.height - listTop - footerH - 8f;

            GUI.BeginGroup(new Rect(0f, headerTop, inRect.width, HeaderHeight));
            DrawTableHeader(-scrollPos.x);
            GUI.EndGroup();
            Widgets.DrawLineHorizontal(0f, headerTop + HeaderHeight, inRect.width);

            if (Find.TickManager.TicksGame >= lastUpdateTick + UpdateIntervalTicks || cachedRows.Count == 0)
                RebuildRows();

            float totalHeight = cachedRows.Count * RowHeight + 8f;
            Rect viewRect = new Rect(0f, 0f, totalWidth, Mathf.Max(totalHeight, tableHeight));
            Rect scrollOuter = new Rect(0f, listTop, inRect.width, tableHeight);
            lastScrollViewportHeight = scrollOuter.height;
            Widgets.BeginScrollView(scrollOuter, ref scrollPos, viewRect);
            for (int i = 0; i < cachedRows.Count; i++)
                DrawRow(i * RowHeight, totalWidth, cachedRows[i], i % 2 == 0);
            Widgets.EndScrollView();

            if (showCapFooter)
            {
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = new Color(1f, 0.85f, 0.4f);
                Widgets.Label(
                    new Rect(4f, listTop + tableHeight + 2f, inRect.width - 8f, FooterHeight),
                    "TSA_WD_AllInventory_TooMany".Translate());
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
            }

            Text.Anchor = TextAnchor.UpperLeft;
            PawnRosterHeaderFilter.DrawDropdownIfOpen();
        }

        private void DrawToolbar(float tableRight)
        {
            Rect sendBtn = new Rect(tableRight - SendBtnWidth, ActionStackTop, SendBtnWidth, ToolbarBtnHeight);
            Rect modeBtn = new Rect(
                sendBtn.x - ToolbarBtnGap - PlayerPawnDropPodUtility.ModeIconSize,
                ActionStackTop,
                PlayerPawnDropPodUtility.ModeIconSize,
                ToolbarBtnHeight);
            Rect selectedRect = new Rect(modeBtn.x - ToolbarBtnGap - SelectedLabelWidth, ActionStackTop, SelectedLabelWidth, ToolbarBtnHeight);

            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(selectedRect, "TSA_WD_AllInventory_Selected".Translate(selectedRowIds.Count.ToString()));
            Text.Anchor = TextAnchor.UpperLeft;

            PlayerPawnDropPodUtility.DrawGoodsDispatchModeIcon(
                modeBtn,
                viaDropPod,
                () => viaDropPod = false,
                () => viaDropPod = true);

            TooltipHandler.TipRegion(sendBtn, "TSA_WD_AllInventory_SendTip".Translate());
            GUI.enabled = selectedRowIds.Count > 0;
            if (WorldDomination_UIUtils.ButtonTextWithIcon(
                    sendBtn,
                    SendIcon,
                    "TSA_WD_WarehouseTab_ShipNow".Translate()))
            {
                OpenDestinationMenu();
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
            GUI.enabled = true;
        }

        private float ComputeTotalTableWidth() =>
            ColIcon + ColLocType + ColLocName + ColSelect + ColItemIcon + ColItem
            + ColEquippedBy + ColType + ColQuality + ColCount + ColSendCount + ColPadding;

        private void DrawTableHeader(float x)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Rect hRect = new Rect(0f, 0f, ComputeTotalTableWidth(), HeaderHeight);
            float curX = x;

            curX += ColIcon;

            PawnRosterHeaderFilter.DrawFilterableHeader(
                ref curX, hRect.y, ColLocType, hRect.height,
                "TSA_WD_AllPlayerPawns_ColLocationType".Translate(),
                sortColumn == "LocationType", sortAscending,
                TextAnchor.MiddleLeft,
                !locationTypeSearchTerm.NullOrEmpty(),
                "TSA_WD_FilterByLocationType".Translate(),
                icon => PawnRosterHeaderFilter.OpenChoiceDropdown(
                    icon,
                    "TSA_WD_FilterByLocationType".Translate(),
                    PawnRosterHeaderFilter.LocationTypeChoices(
                        locationTypeSearchTerm,
                        v => { locationTypeSearchTerm = v ?? ""; lastUpdateTick = -9999; },
                        LocationKindsFrom(filterBaseRows))),
                () => SetSort("LocationType"));

            PawnRosterHeaderFilter.DrawFilterableHeader(
                ref curX, hRect.y, ColLocName, hRect.height,
                "TSA_WD_AllPlayerPawns_ColLocationName".Translate(),
                sortColumn == "LocationName", sortAscending,
                TextAnchor.MiddleLeft,
                !locationNameSearchTerm.NullOrEmpty(),
                "TSA_WD_AllPlayerPawns_SearchLocation".Translate(),
                icon => PawnRosterHeaderFilter.OpenTextDropdown(
                    icon,
                    "TSA_WD_FilterByLocationName".Translate(),
                    "TSA_WD_AllPlayerPawns_SearchLocation".Translate(),
                    () => locationNameSearchTerm,
                    v => { locationNameSearchTerm = v ?? ""; lastUpdateTick = -9999; },
                    () => { locationNameSearchTerm = ""; lastUpdateTick = -9999; }),
                () => SetSort("LocationName"));

            DrawSelectAllHeader(ref curX, hRect);
            curX += ColItemIcon;

            PawnRosterHeaderFilter.DrawFilterableHeader(
                ref curX, hRect.y, ColItem, hRect.height,
                "TSA_WD_AllInventory_HdrItem".Translate(),
                sortColumn == "Item", sortAscending,
                TextAnchor.MiddleLeft,
                !itemSearchTerm.NullOrEmpty(),
                "TSA_WD_FilterByName".Translate(),
                icon => PawnRosterHeaderFilter.OpenTextDropdown(
                    icon,
                    "TSA_WD_FilterByName".Translate(),
                    "TSA_WD_FilterByName".Translate(),
                    () => itemSearchTerm,
                    v => { itemSearchTerm = v ?? ""; lastUpdateTick = -9999; },
                    () => { itemSearchTerm = ""; lastUpdateTick = -9999; }),
                () => SetSort("Item"));

            PawnRosterHeaderFilter.DrawFilterableHeader(
                ref curX, hRect.y, ColEquippedBy, hRect.height,
                "TSA_WD_AllInventory_HdrEquippedBy".Translate(),
                sortColumn == "EquippedBy", sortAscending,
                TextAnchor.MiddleLeft,
                !equippedBySearchTerm.NullOrEmpty(),
                "TSA_WD_AllInventory_EquippedByFilterTip".Translate(),
                icon => PawnRosterHeaderFilter.OpenTextDropdown(
                    icon,
                    "TSA_WD_AllInventory_EquippedByFilterTip".Translate(),
                    "TSA_WD_FilterByName".Translate(),
                    () => equippedBySearchTerm,
                    v => { equippedBySearchTerm = v ?? ""; lastUpdateTick = -9999; },
                    () => { equippedBySearchTerm = ""; lastUpdateTick = -9999; }),
                () => SetSort("EquippedBy"));

            PawnRosterHeaderFilter.DrawFilterableHeader(
                ref curX, hRect.y, ColType, hRect.height,
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
                ref curX, hRect.y, ColQuality, hRect.height,
                "TSA_WD_AllInventory_HdrQuality".Translate(),
                sortColumn == "Quality", sortAscending,
                TextAnchor.MiddleCenter,
                qualityFilter != QualityFilterAll,
                "TSA_WD_AllInventory_QualityFilterTip".Translate(),
                icon => PawnRosterHeaderFilter.OpenChoiceDropdown(
                    icon,
                    "TSA_WD_AllInventory_HdrQuality".Translate(),
                    BuildQualityFilterChoices()),
                () => SetSort("Quality"));

            DrawHeader(ref curX, ColCount, "TSA_WD_AllInventory_HdrCount".Translate(), "Count", hRect);
            DrawHeader(ref curX, ColSendCount, "TSA_WD_AllInventory_HdrSendCount".Translate(), null, hRect);

            GUI.color = Color.white;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawHeader(ref float curX, float width, string label, string tag, Rect hRect)
        {
            Rect headerRect = new Rect(curX, hRect.y, width, hRect.height);
            if (tag != null && Mouse.IsOver(headerRect)) Widgets.DrawHighlight(headerRect);
            Text.Anchor = TextAnchor.MiddleCenter;
            string headerText = label + (tag != null && sortColumn == tag ? (sortAscending ? " ▲" : " ▼") : "");
            Widgets.Label(headerRect, headerText.Truncate(width - 4f));
            if (tag != null && Widgets.ButtonInvisible(headerRect)) SetSort(tag);
            curX += width;
        }

        private void DrawSelectAllHeader(ref float curX, Rect hRect)
        {
            Rect selHdr = new Rect(curX, hRect.y, ColSelect, hRect.height);
            if (Mouse.IsOver(selHdr)) Widgets.DrawHighlight(selHdr);

            int selectable = 0;
            int selectedSelectable = 0;
            for (int i = 0; i < cachedRows.Count; i++)
            {
                if (SendBlockedKey(cachedRows[i]) != null) continue;
                selectable++;
                if (selectedRowIds.Contains(cachedRows[i].rowId)) selectedSelectable++;
            }
            bool all = selectable > 0 && selectedSelectable == selectable;

            bool next = all;
            Widgets.Checkbox(new Vector2(selHdr.x + (ColSelect - 24f) * 0.5f, selHdr.y + (hRect.height - 24f) * 0.5f), ref next);
            if (next != all)
            {
                if (next)
                {
                    for (int i = 0; i < cachedRows.Count; i++)
                        if (SendBlockedKey(cachedRows[i]) == null)
                            selectedRowIds.Add(cachedRows[i].rowId);
                }
                else selectedRowIds.Clear();
            }
            curX += ColSelect;
        }

        private void SetSort(string tag)
        {
            if (sortColumn == tag) sortAscending = !sortAscending;
            else { sortColumn = tag; sortAscending = true; }
            SortRows(cachedRows);
        }

        private void DrawRow(float y, float width, InventoryRow row, bool alt)
        {
            float visibleY = scrollPos.y - RowHeight;
            float visibleYMax = scrollPos.y + lastScrollViewportHeight;
            if (y < visibleY || y >= visibleYMax)
                return;

            Rect rowRect = new Rect(0f, y, width, RowHeight);
            if (alt) Widgets.DrawAltRect(rowRect);
            if (Mouse.IsOver(rowRect)) Widgets.DrawHighlight(rowRect);

            // Match All Player Pawns: Tiny after the header stays Tiny for row labels.
            Text.Font = GameFont.Tiny;
            float curX = 0f;

            if (row.locationIcon != null)
            {
                float iconY = y + (RowHeight - LocIconDrawSize) * 0.5f;
                Rect locIconRect = new Rect(curX + LocIconPad, iconY, LocIconDrawSize, LocIconDrawSize);
                GUI.color = row.locationIconColor;
                GUI.DrawTexture(locIconRect, row.locationIcon, ScaleMode.ScaleToFit);
                GUI.color = Color.white;
                TooltipHandler.TipRegion(locIconRect, row.sourceLabel);
                if (Widgets.ButtonInvisible(locIconRect) && row.source != null)
                    CameraJumper.TryJumpAndSelect(row.source);
            }
            curX += ColIcon;

            Rect locTypeRect = new Rect(curX, y, ColLocType, RowHeight);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(locTypeRect, row.sourceTypeLabel.Truncate(ColLocType - 4f));
            TooltipHandler.TipRegion(locTypeRect, row.sourceTypeLabel);
            if (Widgets.ButtonInvisible(locTypeRect) && row.source != null)
                CameraJumper.TryJumpAndSelect(row.source);
            curX += ColLocType;

            Rect locNameRect = new Rect(curX, y, ColLocName, RowHeight);
            Widgets.Label(locNameRect, row.sourceLabel.Truncate(ColLocName - 4f));
            TooltipHandler.TipRegion(locNameRect, row.jumpTip);
            if (Widgets.ButtonInvisible(locNameRect) && row.source != null)
                CameraJumper.TryJumpAndSelect(row.source);
            curX += ColLocName;

            string blockedTip = SendBlockedTip(row);
            bool selected = selectedRowIds.Contains(row.rowId);
            bool next = selected;
            Widgets.Checkbox(
                new Vector2(curX + (ColSelect - 24f) * 0.5f, y + (RowHeight - 24f) * 0.5f),
                ref next,
                disabled: blockedTip != null);
            if (next != selected)
            {
                if (next) selectedRowIds.Add(row.rowId);
                else selectedRowIds.Remove(row.rowId);
            }
            if (blockedTip != null)
            {
                TooltipHandler.TipRegion(new Rect(curX, y, ColSelect, RowHeight), blockedTip);
            }
            else if (row.isOnPawn)
            {
                TooltipHandler.TipRegion(
                    new Rect(curX, y, ColSelect, RowHeight),
                    cachedOnPawnTip);
            }
            curX += ColSelect;

            Rect itemIconCell = new Rect(curX, y, ColItemIcon, RowHeight);
            Rect iconRect = new Rect(
                itemIconCell.x + (ColItemIcon - ItemIconDrawSize) * 0.5f,
                y + (RowHeight - ItemIconDrawSize) * 0.5f,
                ItemIconDrawSize,
                ItemIconDrawSize);
            if (row.thing != null) Widgets.ThingIcon(iconRect, row.thing);
            else if (row.def != null) Widgets.ThingIcon(iconRect, row.def, row.stuff);
            curX += ColItemIcon;

            Text.Anchor = TextAnchor.MiddleLeft;
            Rect itemRect = new Rect(curX, y, ColItem, RowHeight);
            string taintTip = OutpostArmoryUtility.TaintedTip(row.thing);
            if (taintTip != null) GUI.color = OutpostArmoryUtility.TaintedColor;
            else if (row.isUnique && !row.isOnPawn) GUI.color = WorldOverlayLineMaterials.DarkCyanColor;
            Widgets.Label(itemRect, row.itemLabel.Truncate(ColItem - 6f));
            GUI.color = Color.white;
            string tip = row.itemTip ?? row.itemLabel;
            if (taintTip != null) tip += "\n\n" + taintTip;
            TooltipHandler.TipRegion(itemRect, tip);
            curX += ColItem;

            DrawEquippedByCell(curX, y, row);
            curX += ColEquippedBy;

            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(curX, y, ColType, RowHeight), row.typeLabel.Truncate(ColType - 6f));
            curX += ColType;

            Widgets.Label(new Rect(curX, y, ColQuality, RowHeight),
                (row.hasQuality ? row.qualityLabel : "").Truncate(ColQuality - 6f));
            curX += ColQuality;

            Widgets.Label(new Rect(curX, y, ColCount, RowHeight), row.count.ToString());
            curX += ColCount;

            DrawSendCountControls(curX, y, row);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawEquippedByCell(float x, float y, InventoryRow row)
        {
            if (row.holderPawn == null || row.equippedByLabel.NullOrEmpty())
                return;

            Rect portraitCell = new Rect(x, y, ColEquippedPortrait, RowHeight);
            Texture portrait = PawnPortraitUIUtils.GetPortrait(
                row.holderPawn,
                new Vector2(EquippedPortraitDrawSize, EquippedPortraitDrawSize));
            Rect portraitRect = new Rect(
                portraitCell.x + (ColEquippedPortrait - EquippedPortraitDrawSize) * 0.5f,
                y + (RowHeight - EquippedPortraitDrawSize) * 0.5f,
                EquippedPortraitDrawSize,
                EquippedPortraitDrawSize);
            if (portrait != null)
                GUI.DrawTexture(portraitRect, portrait, ScaleMode.ScaleToFit);
            else
                Widgets.DrawBoxSolid(portraitRect, new Color(0.3f, 0.3f, 0.35f, 1f));
            if (Widgets.ButtonInvisible(portraitCell))
                Find.WindowStack.Add(new Dialog_InfoCard(row.holderPawn));

            Rect nameRect = new Rect(x + ColEquippedPortrait, y, ColEquippedName, RowHeight);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(nameRect, row.equippedByLabel.Truncate(ColEquippedName - 4f));
            TooltipHandler.TipRegion(nameRect, row.equippedByLabel);
            if (Widgets.ButtonInvisible(nameRect))
                Find.WindowStack.Add(new Dialog_InfoCard(row.holderPawn));
        }

        /// <summary>Same - / field / + / 0 / Max strip as <see cref="WITab_Outpost_Warehouse"/>.</summary>
        private void DrawSendCountControls(float x, float y, InventoryRow row)
        {
            int stored = row.count;
            string key = row.rowId;
            if (!sendCounts.TryGetValue(key, out int pick)) pick = stored;
            pick = Mathf.Clamp(pick, 0, stored);
            sendCounts[key] = pick;

            float btnY = y + (RowHeight - BtnW) / 2f;
            float cx = x;
            Rect minusRect = new Rect(cx, btnY, BtnW, BtnW);
            cx += BtnW;
            Rect countRect = new Rect(cx, btnY, CountColW, BtnW);
            cx += CountColW;
            Rect plusRect = new Rect(cx, btnY, BtnW, BtnW);
            cx += BtnW + BtnGap;
            Rect zeroRect = new Rect(cx, btnY, BtnW, BtnW);
            cx += BtnW + BtnGap;
            Rect maxRect = new Rect(cx, btnY, MaxBtnW, BtnW);

            int step = WdQuantityUI.AdjustmentStep();
            if (WdDragSelectButtons.ButtonText(minusRect, "-", WdDragSelectButtons.Hash(key, "minus")) && pick > 0)
                SetSendCount(key, Mathf.Max(0, pick - step));
            if (WdDragSelectButtons.ButtonText(plusRect, "+", WdDragSelectButtons.Hash(key, "plus")) && pick < stored)
                SetSendCount(key, Mathf.Min(stored, pick + step));
            if (WdDragSelectButtons.ButtonText(zeroRect, "0", WdDragSelectButtons.Hash(key, "zero")))
                SetSendCount(key, 0);
            if (WdDragSelectButtons.ButtonText(maxRect, "Max", WdDragSelectButtons.Hash(key, "max")))
                SetSendCount(key, stored);
            TooltipHandler.TipRegion(minusRect, cachedQuantityAdjustTip);
            TooltipHandler.TipRegion(plusRect, cachedQuantityAdjustTip);

            pick = sendCounts[key];
            if (!sendCountBuffers.TryGetValue(key, out string buffer) || buffer == null)
                buffer = pick.ToString();
            int edited = pick;
            TextAnchor prevAnchor = Text.Anchor;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.TextFieldNumeric(countRect, ref edited, ref buffer, 0f, stored);
            Text.Anchor = prevAnchor;
            sendCountBuffers[key] = buffer;
            if (edited != pick)
                SetSendCount(key, Mathf.Clamp(edited, 0, stored));
        }

        private void SetSendCount(string key, int value)
        {
            sendCounts[key] = value;
            sendCountBuffers[key] = value.ToString();
        }

        /// <summary>Null when the row can be shipped; otherwise the keyed reason it cannot.</summary>
        private static string SendBlockedKey(InventoryRow row)
        {
            if (row.source is Caravan) return "TSA_WD_AllInventory_CaravanNoSend";
            if (row.source is WorldObject_WD_Outpost o && o.ManualDefenseActive)
                return "TSA_WD_Armory_FailManualDefense";
            if (row.isOnPawn && PawnGearMapInCombat(row.holderPawn))
                return "TSA_WD_AllInventory_InCombatNoSend";
            return null;
        }

        /// <summary>Translated blocked tip from window-level caches (refreshed in <see cref="RebuildRows"/>).</summary>
        private string SendBlockedTip(InventoryRow row)
        {
            string key = SendBlockedKey(row);
            if (key == null) return null;
            if (key == "TSA_WD_AllInventory_CaravanNoSend") return cachedBlockedCaravanTip;
            if (key == "TSA_WD_Armory_FailManualDefense") return cachedBlockedDefenseTip;
            if (key == "TSA_WD_AllInventory_InCombatNoSend") return cachedBlockedAssaultTip;
            return key.Translate();
        }

        private void RefreshDrawStringCaches()
        {
            cachedOnPawnTip = "TSA_WD_AllInventory_OnPawnTip".Translate();
            buildOnPawnTip = cachedOnPawnTip;
            cachedQuantityAdjustTip = "TSA_WD_QuantityAdjustTip".Translate();
            cachedBlockedCaravanTip = "TSA_WD_AllInventory_CaravanNoSend".Translate();
            cachedBlockedDefenseTip = "TSA_WD_Armory_FailManualDefense".Translate();
            cachedBlockedAssaultTip = "TSA_WD_AllInventory_InCombatNoSend".Translate();
        }

        /// <summary>
        /// Worn/carried gear cannot be stripped during an active raid/siege or WD defense/clash.
        /// Map-free outpost Occupants have no map and are fine. Result cached per map+tick for draw.
        /// </summary>
        private static int combatCacheTick = -1;
        private static int combatCacheMapId = int.MinValue;
        private static bool combatCacheValue;

        private static bool PawnGearMapInCombat(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed) return false;
            Map map = pawn.MapHeld ?? pawn.Map;
            if (map == null) return false;

            int tick = Find.TickManager.TicksGame;
            if (combatCacheTick == tick && combatCacheMapId == map.uniqueID)
                return combatCacheValue;

            combatCacheTick = tick;
            combatCacheMapId = map.uniqueID;
            combatCacheValue = MapHasActiveRaidCombat(map);
            return combatCacheValue;
        }

        private static bool MapHasActiveRaidCombat(Map map)
        {
            WD_MapComponent_OutpostDefense defense = map.GetComponent<WD_MapComponent_OutpostDefense>();
            if (defense != null && defense.BlocksPlayerEdgeExit)
                return true;

            WD_MapComponent_CaravanClash clash = map.GetComponent<WD_MapComponent_CaravanClash>();
            if (clash != null && clash.BlocksPlayerEdgeExit)
                return true;

            List<Lord> lords = map.lordManager?.lords;
            if (lords == null) return false;
            for (int i = 0; i < lords.Count; i++)
            {
                Lord lord = lords[i];
                if (lord?.LordJob == null) continue;
                if (lord.faction == null || !lord.faction.HostileTo(Faction.OfPlayer)) continue;
                LordJob job = lord.LordJob;
                if (job is LordJob_AssaultColony
                    || job is LordJob_Siege
                    || job is LordJob_StageThenAttack)
                    return true;
                if (lord.CurLordToil is LordToil_AssaultColony)
                    return true;
            }
            return false;
        }

        private int ResolveSendCount(InventoryRow row)
        {
            if (!sendCounts.TryGetValue(row.rowId, out int value)) return row.count;
            return Mathf.Clamp(value, 0, row.count);
        }

        private List<HeaderFilterChoice> BuildTypeFilterChoices()
        {
            var defs = new List<ThingDef>(filterBaseRows.Count);
            for (int i = 0; i < filterBaseRows.Count; i++) defs.Add(filterBaseRows[i].def);
            return OutpostArmoryUtility.BuildTypeFilterChoices(
                defs, typeFilter, f => { typeFilter = f; lastUpdateTick = -9999; });
        }

        private List<HeaderFilterChoice> BuildQualityFilterChoices()
        {
            int total = filterBaseRows.Count;
            int none = 0;
            var counts = new int[8];
            for (int i = 0; i < filterBaseRows.Count; i++)
            {
                InventoryRow r = filterBaseRows[i];
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
                    () => { qualityFilter = QualityFilterAll; lastUpdateTick = -9999; },
                    separatorAfter: true,
                    countLabel: CountOf(total)),
                new HeaderFilterChoice(
                    "TSA_WD_AllInventory_QualityNone".Translate(),
                    qualityFilter == QualityFilterNone,
                    () => { qualityFilter = QualityFilterNone; lastUpdateTick = -9999; },
                    countLabel: CountOf(none))
            };

            foreach (QualityCategory q in Enum.GetValues(typeof(QualityCategory)))
            {
                QualityCategory captured = q;
                int idx = (int)q;
                int n = idx >= 0 && idx < counts.Length ? counts[idx] : 0;
                list.Add(new HeaderFilterChoice(
                    q.GetLabel().CapitalizeFirst(),
                    qualityFilter == idx,
                    () => { qualityFilter = (int)captured; lastUpdateTick = -9999; },
                    countLabel: CountOf(n)));
            }

            return list;
        }

        private static List<PlayerPawnLocationKind> LocationKindsFrom(IReadOnlyList<InventoryRow> rows)
        {
            var list = new List<PlayerPawnLocationKind>(rows?.Count ?? 0);
            if (rows == null) return list;
            for (int i = 0; i < rows.Count; i++)
                list.Add(rows[i].locationKind);
            return list;
        }

        private void RebuildRows()
        {
            RefreshDrawStringCaches();
            var rows = new List<InventoryRow>();

            CollectOutpostRows(rows);
            CollectColonyMapRows(rows);
            CollectCaravanPawnGear(rows);

            filterBaseRows = rows;

            string itemFilter = string.IsNullOrEmpty(itemSearchTerm) ? null : itemSearchTerm.ToLowerInvariant();
            string locNameFilter = string.IsNullOrEmpty(locationNameSearchTerm) ? null : locationNameSearchTerm.ToLowerInvariant();
            string equippedFilter = string.IsNullOrEmpty(equippedBySearchTerm) ? null : equippedBySearchTerm.ToLowerInvariant();

            var filtered = new List<InventoryRow>(rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                InventoryRow r = rows[i];
                if (!MatchesLocationType(r.locationKind, locationTypeSearchTerm)) continue;
                if (!OutpostArmoryUtility.MatchesTypeFilter(r.def, typeFilter)) continue;
                if (!MatchesQualityFilter(r)) continue;
                if (itemFilter != null && r.itemLabel.ToLowerInvariant().IndexOf(itemFilter, StringComparison.Ordinal) < 0)
                    continue;
                if (locNameFilter != null && r.sourceLabel.ToLowerInvariant().IndexOf(locNameFilter, StringComparison.Ordinal) < 0)
                    continue;
                if (equippedFilter != null
                    && (r.equippedByLabel == null
                        || r.equippedByLabel.ToLowerInvariant().IndexOf(equippedFilter, StringComparison.Ordinal) < 0))
                    continue;
                filtered.Add(r);
            }

            SortRows(filtered);
            totalMatchCount = filtered.Count;
            if (filtered.Count > MaxRows)
                filtered.RemoveRange(MaxRows, filtered.Count - MaxRows);

            cachedRows = filtered;
            PruneStaleState();
            lastUpdateTick = Find.TickManager.TicksGame;
        }

        private static bool MatchesLocationType(PlayerPawnLocationKind kind, string filter)
        {
            if (string.IsNullOrEmpty(filter)) return true;
            if (filter == PawnRosterHeaderFilter.LocationTypeColony)
                return kind == PlayerPawnLocationKind.Colony;
            if (filter == PawnRosterHeaderFilter.LocationTypeOutpost)
                return kind == PlayerPawnLocationKind.Outpost;
            if (filter == PawnRosterHeaderFilter.LocationTypeCaravan)
                return kind == PlayerPawnLocationKind.WorldCaravan;
            if (filter == PawnRosterHeaderFilter.LocationTypeCamp)
                return kind == PlayerPawnLocationKind.Camp;
            if (filter == PawnRosterHeaderFilter.LocationTypePhysicalMap)
                return kind == PlayerPawnLocationKind.PhysicalMap;
            return true;
        }

        private static bool MatchesQualityFilter(InventoryRow r)
        {
            if (qualityFilter == QualityFilterAll) return true;
            if (qualityFilter == QualityFilterNone) return !r.hasQuality;
            return r.hasQuality && (int)r.quality == qualityFilter;
        }

        private static void CollectOutpostRows(List<InventoryRow> rows)
        {
            IReadOnlyList<WorldObject_WD_Outpost> outposts = WdPlayerOutpostCache.PlayerOutposts;
            for (int i = 0; i < outposts.Count; i++)
            {
                WorldObject_WD_Outpost o = outposts[i];
                string srcLabel = o.LabelCap;
                string typeLabel = o.def?.LabelCap ?? "TSA_WD_AllPlayerPawns_LocOutpost".Translate();
                Texture2D icon = o.def?.ExpandingIconTexture;
                Color iconColor = o.Faction?.Color ?? Color.white;

                var armory = CompOutpostArmory.Get(o);
                if (armory != null)
                {
                    List<ThingDefCountClass> stock = armory.ArmoryRows();
                    for (int j = 0; j < stock.Count; j++)
                    {
                        ThingDefCountClass e = stock[j];
                        rows.Add(MakeStockRow(o, srcLabel, typeLabel, PlayerPawnLocationKind.Outpost,
                            icon, iconColor, false, e));
                    }

                    ThingOwner<Thing> uniques = armory.Uniques;
                    if (uniques != null)
                    {
                        for (int j = 0; j < uniques.Count; j++)
                        {
                            Thing t = uniques[j];
                            if (t?.def == null) continue;
                            rows.Add(MakeThingRow(o, srcLabel, typeLabel, PlayerPawnLocationKind.Outpost,
                                icon, iconColor, false, t, true, false, null, null));
                        }
                    }
                }

                List<Pawn> occupants = o.Occupants;
                for (int j = 0; j < occupants.Count; j++)
                    AppendPawnGear(rows, occupants[j], o, srcLabel, typeLabel,
                        PlayerPawnLocationKind.Outpost, icon, iconColor, false);
            }
        }

        private static void CollectColonyMapRows(List<InventoryRow> rows)
        {
            string colonyKind = "TSA_WD_AllPlayerPawns_LocColony".Translate();
            Faction player = Faction.OfPlayer;
            Texture2D colonyIcon = player?.def?.FactionIcon;
            Color colonyColor = player?.Color ?? Color.white;

            var maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                Map map = maps[i];
                if (map?.Parent == null || map.Parent.Faction != Faction.OfPlayer) continue;
                string srcLabel = map.Parent.LabelCap;
                WorldObject source = map.Parent;

                List<ColonyArmoryLaunchUtility.ColonyItemEntry> items = ColonyArmoryLaunchUtility.BuildPickableItems(map);
                for (int j = 0; j < items.Count; j++)
                {
                    Thing t = items[j].Thing;
                    rows.Add(MakeThingRow(source, srcLabel, colonyKind, PlayerPawnLocationKind.Colony,
                        colonyIcon, colonyColor, true, t, false, false));
                }

                List<Pawn> pawns = map.mapPawns?.FreeColonistsSpawned;
                if (pawns == null) continue;
                for (int j = 0; j < pawns.Count; j++)
                    AppendPawnGear(rows, pawns[j], source, srcLabel, colonyKind,
                        PlayerPawnLocationKind.Colony, colonyIcon, colonyColor, true);
            }
        }

        private static void CollectCaravanPawnGear(List<InventoryRow> rows)
        {
            string caravanKind = "TSA_WD_AllPlayerPawns_LocCaravan".Translate();
            Faction player = Faction.OfPlayer;
            Texture2D icon = CaravanLocIcon;
            Color iconColor = player?.Color ?? Color.white;

            List<Caravan> caravans = Find.WorldObjects?.Caravans;
            if (caravans == null) return;
            for (int i = 0; i < caravans.Count; i++)
            {
                Caravan c = caravans[i];
                if (c == null || c.Faction != Faction.OfPlayer) continue;
                string srcLabel = c.LabelCap;
                List<Pawn> pawns = c.PawnsListForReading;
                for (int j = 0; j < pawns.Count; j++)
                    AppendPawnGear(rows, pawns[j], c, srcLabel, caravanKind,
                        PlayerPawnLocationKind.WorldCaravan, icon, iconColor, false);
            }
        }

        private static void AppendPawnGear(
            List<InventoryRow> rows,
            Pawn pawn,
            WorldObject source,
            string sourceLabel,
            string sourceTypeLabel,
            PlayerPawnLocationKind kind,
            Texture2D icon,
            Color iconColor,
            bool isColony)
        {
            if (pawn == null || pawn.Destroyed) return;
            string holder = pawn.LabelShortCap ?? pawn.LabelCap ?? "";

            List<Apparel> worn = pawn.apparel?.WornApparel;
            if (worn != null)
            {
                for (int i = 0; i < worn.Count; i++)
                {
                    Apparel a = worn[i];
                    if (a?.def == null || !OutpostArmoryUtility.IsArmoryItem(a.def)) continue;
                    rows.Add(MakeThingRow(source, sourceLabel, sourceTypeLabel, kind,
                        icon, iconColor, isColony, a, true, true, holder, pawn));
                }
            }

            List<ThingWithComps> eq = pawn.equipment?.AllEquipmentListForReading;
            if (eq != null)
            {
                for (int i = 0; i < eq.Count; i++)
                {
                    ThingWithComps t = eq[i];
                    if (t?.def == null || !OutpostArmoryUtility.IsArmoryItem(t.def)) continue;
                    rows.Add(MakeThingRow(source, sourceLabel, sourceTypeLabel, kind,
                        icon, iconColor, isColony, t, true, true, holder, pawn));
                }
            }

            if (pawn.inventory?.innerContainer == null) return;
            List<Thing> inv = pawn.inventory.innerContainer.InnerListForReading;
            for (int i = 0; i < inv.Count; i++)
            {
                Thing t = inv[i];
                if (t?.def == null || !OutpostArmoryUtility.IsArmoryItem(t.def)) continue;
                rows.Add(MakeThingRow(source, sourceLabel, sourceTypeLabel, kind,
                    icon, iconColor, isColony, t, false, true, holder, pawn));
            }
        }

        private static InventoryRow MakeStockRow(
            WorldObject source,
            string sourceLabel,
            string sourceTypeLabel,
            PlayerPawnLocationKind kind,
            Texture2D icon,
            Color iconColor,
            bool isColony,
            ThingDefCountClass e)
        {
            bool hasQuality = e.thingDef.HasComp(typeof(CompQuality));
            string label = GenLabel.ThingLabel(e.thingDef, e.stuff, 1).CapitalizeFirst();
            ArmoryTypeFilter typeKind = OutpostArmoryUtility.TypeFilterFor(e.thingDef);
            return new InventoryRow
            {
                rowId = source.ID + "|s|" + e.thingDef.defName + "|" + (e.stuff?.defName ?? "-") + "|"
                        + ((int)e.quality).ToString(),
                source = source,
                sourceLabel = sourceLabel,
                jumpTip = "TSA_WD_AllPlayerPawns_JumpTip".Translate(sourceLabel),
                sourceTypeLabel = sourceTypeLabel,
                locationKind = kind,
                locationIcon = icon,
                locationIconColor = iconColor,
                sourceIsColony = isColony,
                stockRow = e,
                def = e.thingDef,
                stuff = e.stuff,
                itemLabel = label,
                itemTip = label,
                typeLabel = OutpostArmoryUtility.TypeFilterLabel(typeKind),
                hasQuality = hasQuality,
                quality = e.quality,
                qualityLabel = hasQuality ? e.quality.GetLabel().CapitalizeFirst() : "",
                count = e.count
            };
        }

        private static InventoryRow MakeThingRow(
            WorldObject source,
            string sourceLabel,
            string sourceTypeLabel,
            PlayerPawnLocationKind kind,
            Texture2D icon,
            Color iconColor,
            bool isColony,
            Thing t,
            bool isUnique,
            bool isOnPawn,
            string equippedByLabel = null,
            Pawn holderPawn = null)
        {
            ArmoryTypeFilter typeKind = OutpostArmoryUtility.TypeFilterFor(t.def);
            bool hasQuality = t.TryGetQuality(out QualityCategory q);
            string label = OutpostArmoryUtility.DisplayLabel(t);
            string tip = label;
            if (isOnPawn && !buildOnPawnTip.NullOrEmpty())
                tip += "\n" + buildOnPawnTip;
            return new InventoryRow
            {
                rowId = source.ID + "|t|" + t.ThingID + (isOnPawn ? "|p" : ""),
                source = source,
                sourceLabel = sourceLabel,
                jumpTip = "TSA_WD_AllPlayerPawns_JumpTip".Translate(sourceLabel),
                sourceTypeLabel = sourceTypeLabel,
                locationKind = kind,
                locationIcon = icon,
                locationIconColor = iconColor,
                sourceIsColony = isColony,
                thing = t,
                def = t.def,
                stuff = t.Stuff,
                itemLabel = label,
                itemTip = tip,
                typeLabel = OutpostArmoryUtility.TypeFilterLabel(typeKind),
                hasQuality = hasQuality,
                quality = q,
                qualityLabel = hasQuality ? q.GetLabel().CapitalizeFirst() : "",
                count = Mathf.Max(1, t.stackCount),
                isUnique = isUnique,
                isOnPawn = isOnPawn,
                holderPawn = holderPawn,
                equippedByLabel = equippedByLabel ?? ""
            };
        }

        private void SortRows(List<InventoryRow> rows)
        {
            rows.Sort((a, b) =>
            {
                int cmp;
                switch (sortColumn)
                {
                    case "LocationType":
                        cmp = string.Compare(a.sourceTypeLabel, b.sourceTypeLabel, StringComparison.OrdinalIgnoreCase);
                        break;
                    case "LocationName":
                        cmp = string.Compare(a.sourceLabel, b.sourceLabel, StringComparison.OrdinalIgnoreCase);
                        break;
                    case "EquippedBy":
                        cmp = string.Compare(a.equippedByLabel, b.equippedByLabel, StringComparison.OrdinalIgnoreCase);
                        break;
                    case "Type":
                        cmp = string.Compare(a.typeLabel, b.typeLabel, StringComparison.OrdinalIgnoreCase);
                        break;
                    case "Quality":
                        cmp = QualitySortKey(a).CompareTo(QualitySortKey(b));
                        break;
                    case "Count":
                        cmp = a.count.CompareTo(b.count);
                        break;
                    default:
                        cmp = string.Compare(a.itemLabel, b.itemLabel, StringComparison.OrdinalIgnoreCase);
                        break;
                }
                if (cmp == 0) cmp = string.Compare(a.itemLabel, b.itemLabel, StringComparison.OrdinalIgnoreCase);
                return sortAscending ? cmp : -cmp;
            });
        }

        private static int QualitySortKey(InventoryRow r) =>
            r.hasQuality ? (int)r.quality : -1;

        private void PruneStaleState()
        {
            var live = new HashSet<string>();
            for (int i = 0; i < cachedRows.Count; i++) live.Add(cachedRows[i].rowId);
            selectedRowIds.RemoveWhere(id => !live.Contains(id));

            var stale = new List<string>();
            foreach (string id in sendCounts.Keys)
                if (!live.Contains(id)) stale.Add(id);
            for (int i = 0; i < stale.Count; i++)
            {
                sendCounts.Remove(stale[i]);
                sendCountBuffers.Remove(stale[i]);
            }
        }

        private void OpenDestinationMenu()
        {
            var bySource = GroupSelectionBySource();
            if (bySource.Count == 0)
            {
                Messages.Message("TSA_WD_AllInventory_NothingPicked".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            var snapshot = SnapshotLaunch(bySource);
            if (snapshot.Count == 0)
            {
                Messages.Message("TSA_WD_AllInventory_NothingPicked".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            bool pods = viaDropPod;
            WorldObject cameraOrigin = null;
            foreach (var kv in snapshot)
            {
                cameraOrigin = kv.Key;
                break;
            }

            var dests = CollectSharedShipmentDestinations(snapshot);
            Find.WindowStack.Add(new Dialog_AdHocShipmentDestination(
                dests,
                cameraOrigin,
                dest => TryProceedLaunch(snapshot, dest, pods),
                () =>
                {
                    PlayerPawnDropPodUtility.PrepareWorldMapDestinationPick(cameraOrigin);
                    BeginGearWorldTargeting(snapshot, pods, cameraOrigin);
                },
                "TSA_WD_Shipment_DestDialogTitle",
                viaDropPod: pods,
                launchCount: snapshot.Count));
        }

        /// <summary>
        /// Destinations valid for every selected source that is not the destination itself.
        /// A selected origin may appear when other origins can still ship there (skip-same-dest confirm).
        /// </summary>
        private static List<WorldObject> CollectSharedShipmentDestinations(
            Dictionary<WorldObject, List<LaunchItem>> snapshot)
        {
            var result = new List<WorldObject>();
            if (snapshot == null || snapshot.Count == 0) return result;

            var candidates = new HashSet<WorldObject>();
            WorldObject sortOrigin = null;
            foreach (var kv in snapshot)
            {
                if (kv.Key == null) continue;
                if (sortOrigin == null) sortOrigin = kv.Key;
                candidates.Add(kv.Key);

                var preview = new List<ThingDefCountClass>();
                int uniqueCount = 0;
                FillShipmentPreview(kv.Value, preview, ref uniqueCount);
                List<WorldObject> dests = Outpost_Warehouse_Delivery.CollectValidShipmentDestinations(
                    kv.Key, preview, uniqueCount, 0f);
                for (int i = 0; i < dests.Count; i++)
                {
                    if (dests[i] != null)
                        candidates.Add(dests[i]);
                }
            }

            foreach (WorldObject dest in candidates)
            {
                if (dest == null || dest.Destroyed) continue;
                bool anyShippable = false;
                bool allOk = true;
                foreach (var kv in snapshot)
                {
                    if (kv.Key == null || kv.Key == dest) continue;
                    if (!IsValidDestinationFor(dest, kv.Key, kv.Value, out _))
                    {
                        allOk = false;
                        break;
                    }
                    anyShippable = true;
                }
                if (anyShippable && allOk)
                    result.Add(dest);
            }

            int fromTile = sortOrigin != null ? sortOrigin.Tile : 0;
            result.Sort((a, b) =>
            {
                float da = Find.WorldGrid.ApproxDistanceInTiles(fromTile, a.Tile);
                float db = Find.WorldGrid.ApproxDistanceInTiles(fromTile, b.Tile);
                int cmp = da.CompareTo(db);
                if (cmp != 0) return cmp;
                return string.CompareOrdinal(a.LabelCap, b.LabelCap);
            });
            return result;
        }

        private void BeginGearWorldTargeting(
            Dictionary<WorldObject, List<LaunchItem>> snapshot,
            bool pods,
            WorldObject cameraOrigin)
        {
            Outpost_Warehouse_Delivery.CyanDeliveryMouseOverlayActive = true;
            Find.WorldTargeter.BeginTargeting(
                target =>
                {
                    WorldObject wo = target.WorldObject;
                    if (wo == null)
                    {
                        Messages.Message("TSA_WD_AllInventory_NoSharedDest".Translate(), MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    return TryProceedLaunch(snapshot, wo, pods);
                },
                true,
                Outpost_Warehouse_Delivery.GetDeliveryTargetMouseIcon(),
                false,
                null,
                null);
        }

        /// <summary>
        /// Partition, validate remaining, launch or open skip-same-dest confirm.
        /// Returns true when targeting should end (launch started, confirm opened, or hard reject that should not keep picking).
        /// Returns false only when the player should keep the world targeter (invalid click / remaining empty at dest / remaining invalid).
        /// </summary>
        private bool TryProceedLaunch(
            Dictionary<WorldObject, List<LaunchItem>> snapshot,
            WorldObject destination,
            bool pods)
        {
            if (destination == null || snapshot == null || snapshot.Count == 0)
            {
                Messages.Message("TSA_WD_AllInventory_NothingPicked".Translate(), MessageTypeDefOf.RejectInput);
                return false;
            }

            PartitionByDestination(snapshot, destination, out List<string> blockedLines,
                out Dictionary<WorldObject, List<LaunchItem>> remaining);

            if (remaining.Count == 0)
            {
                Messages.Message("TSA_WD_AllInventory_AlreadyAtLocation".Translate(), MessageTypeDefOf.RejectInput);
                return false;
            }

            foreach (var kv in remaining)
            {
                if (!IsValidDestinationFor(destination, kv.Key, kv.Value, out string rejectKey))
                {
                    Messages.Message(rejectKey.Translate(), MessageTypeDefOf.RejectInput);
                    return false;
                }
            }

            if (blockedLines.Count == 0)
                return FinishLaunch(remaining, destination, pods);

            string destLabel = Outpost_Warehouse_Delivery.GetDestinationLabelWithKind(destination);
            Find.WindowStack.Add(new Dialog_AllInventorySkipSameDest(
                destLabel,
                blockedLines,
                () => FinishLaunch(remaining, destination, pods)));
            // End world targeting; Cancel on the dialog only skips launch.
            return true;
        }

        private bool FinishLaunch(
            Dictionary<WorldObject, List<LaunchItem>> remaining,
            WorldObject destination,
            bool pods)
        {
            if (pods && remaining != null && destination != null)
            {
                var flights = new List<(int originTile, int destTile)>();
                foreach (var kv in remaining)
                {
                    if (kv.Key == null || kv.Key.Destroyed) continue;
                    flights.Add((kv.Key.Tile.tileId, destination.Tile.tileId));
                }
                if (flights.Count > 0)
                {
                    Action launch = () => FinishLaunchCommitted(remaining, destination, pods);
                    if (PlayerPawnDropPodUtility.ConfirmHostileAaThen(flights, launch))
                        return true;
                }
            }
            return FinishLaunchCommitted(remaining, destination, pods);
        }

        private bool FinishLaunchCommitted(
            Dictionary<WorldObject, List<LaunchItem>> remaining,
            WorldObject destination,
            bool pods)
        {
            int launched = LaunchSnapshot(remaining, destination, pods);
            if (launched <= 0)
            {
                Messages.Message("TSA_WD_AllInventory_NothingPicked".Translate(), MessageTypeDefOf.RejectInput);
                return false;
            }
            selectedRowIds.Clear();
            sendCounts.Clear();
            sendCountBuffers.Clear();
            Window_OutpostOverview.InvalidateCache();
            return true;
        }

        private static void PartitionByDestination(
            Dictionary<WorldObject, List<LaunchItem>> snapshot,
            WorldObject destination,
            out List<string> blockedLines,
            out Dictionary<WorldObject, List<LaunchItem>> remaining)
        {
            remaining = new Dictionary<WorldObject, List<LaunchItem>>();
            var merged = new Dictionary<string, int>();
            foreach (var kv in snapshot)
            {
                if (kv.Key == null || kv.Value == null || kv.Value.Count == 0) continue;
                if (kv.Key == destination)
                {
                    for (int i = 0; i < kv.Value.Count; i++)
                    {
                        LaunchItem item = kv.Value[i];
                        if (item == null || item.sendCount <= 0) continue;
                        string label = LaunchItemLabel(item);
                        if (label.NullOrEmpty()) continue;
                        if (!merged.TryGetValue(label, out int n))
                            merged[label] = item.sendCount;
                        else
                            merged[label] = n + item.sendCount;
                    }
                }
                else
                {
                    remaining[kv.Key] = kv.Value;
                }
            }

            blockedLines = new List<string>(merged.Count);
            foreach (var kv in merged)
            {
                blockedLines.Add("TSA_WD_AllInventory_SkipSameDestLine".Translate(
                    kv.Value.ToString(), kv.Key).ToString());
            }
            blockedLines.Sort(StringComparer.OrdinalIgnoreCase);
        }

        private static string LaunchItemLabel(LaunchItem item)
        {
            if (item == null) return "";
            if (item.thing != null)
                return OutpostArmoryUtility.DisplayLabel(item.thing);
            if (item.stockRow?.thingDef != null)
                return GenLabel.ThingLabel(item.stockRow.thingDef, item.stockRow.stuff, 1).CapitalizeFirst();
            if (item.Def != null)
                return item.Def.LabelCap;
            return "";
        }

        private static void FillShipmentPreview(
            List<LaunchItem> items,
            List<ThingDefCountClass> preview,
            ref int uniqueCount)
        {
            if (items == null) return;
            for (int i = 0; i < items.Count; i++)
            {
                LaunchItem item = items[i];
                if (item?.Def == null) continue;
                if (item.stockRow == null && item.thing != null && OutpostArmoryUtility.IsIrreplaceable(item.thing))
                    uniqueCount++;
                else
                    preview.Add(new ThingDefCountClass(item.Def, item.sendCount));
            }
        }

        /// <summary>One picked line frozen at targeting time: a store row, a store unique, a map stack, or pawn gear.</summary>
        private sealed class LaunchItem
        {
            public ThingDefCountClass stockRow;
            public Thing thing;
            public int sendCount;
            public Pawn holderPawn;

            public ThingDef Def => stockRow?.thingDef ?? thing?.def;
        }

        private static bool IsValidDestinationFor(
            WorldObject destination,
            WorldObject source,
            List<LaunchItem> items,
            out string rejectKey)
        {
            var preview = new List<ThingDefCountClass>();
            int uniqueCount = 0;
            FillShipmentPreview(items, preview, ref uniqueCount);
            return Outpost_Warehouse_Delivery.IsValidShipmentDestination(
                destination, source, preview, uniqueCount, 0f, out rejectKey);
        }

        private Dictionary<WorldObject, List<LaunchItem>> SnapshotLaunch(
            Dictionary<WorldObject, List<InventoryRow>> bySource)
        {
            var snapshot = new Dictionary<WorldObject, List<LaunchItem>>();
            foreach (var kv in bySource)
            {
                var list = new List<LaunchItem>();
                for (int i = 0; i < kv.Value.Count; i++)
                {
                    InventoryRow r = kv.Value[i];
                    int send = ResolveSendCount(r);
                    if (send <= 0) continue;
                    list.Add(new LaunchItem
                    {
                        stockRow = r.stockRow != null
                            ? new ThingDefCountClass(r.def, send) { stuff = r.stuff, quality = r.stockRow.quality }
                            : null,
                        thing = r.thing,
                        sendCount = send,
                        holderPawn = r.isOnPawn ? r.holderPawn : null
                    });
                }
                if (list.Count > 0)
                    snapshot[kv.Key] = list;
            }
            return snapshot;
        }

        private static int LaunchSnapshot(
            Dictionary<WorldObject, List<LaunchItem>> bySource,
            WorldObject destination,
            bool viaDropPod)
        {
            int launched = 0;
            foreach (var kv in bySource)
            {
                if (kv.Key == null || kv.Key == destination) continue;

                if (kv.Key is MapParent colony)
                {
                    if (TryLaunchColonySelection(colony, kv.Value, destination, viaDropPod))
                        launched++;
                    continue;
                }

                if (!(kv.Key is WorldObject_WD_Outpost outpost)) continue;
                if (TryLaunchOutpostSelection(outpost, kv.Value, destination, viaDropPod))
                    launched++;
            }
            return launched;
        }

        private static bool TryLaunchColonySelection(
            MapParent colony,
            List<LaunchItem> items,
            WorldObject destination,
            bool viaDropPod)
        {
            var picked = new Dictionary<Thing, int>();
            var holders = new Dictionary<Thing, Pawn>();
            for (int i = 0; i < items.Count; i++)
            {
                LaunchItem item = items[i];
                if (item.sendCount <= 0 || item.thing == null || item.thing.Destroyed) continue;
                picked[item.thing] = item.sendCount;
                if (item.holderPawn != null) holders[item.thing] = item.holderPawn;
            }
            return ColonyArmoryLaunchUtility.TryLaunch(colony, picked, destination, viaDropPod, holders);
        }

        private static bool TryLaunchOutpostSelection(
            WorldObject_WD_Outpost outpost,
            List<LaunchItem> items,
            WorldObject destination,
            bool viaDropPod)
        {
            // Gate before stripping: pawn gear goes into the store first, and a later reject must
            // leave it there rather than in limbo.
            if (!OutpostStorageShipping.CanLaunchFrom(outpost, viaDropPod, out string rejectKey))
            {
                Messages.Message(rejectKey.Translate(outpost.LabelCap), outpost, MessageTypeDefOf.RejectInput);
                return false;
            }
            CompOutpostArmory armory = CompOutpostArmory.Get(outpost);
            if (armory == null) return false;

            var rows = new List<ThingDefCountClass>();
            var uniques = new List<Thing>();

            for (int i = 0; i < items.Count; i++)
            {
                LaunchItem item = items[i];
                if (item.sendCount <= 0) continue;

                if (item.holderPawn != null && item.thing != null)
                {
                    Thing worn = item.thing;
                    if (worn.Destroyed) continue;
                    int count = Mathf.Min(item.sendCount, worn.stackCount);
                    if (!OutpostArmoryUtility.TryStoreFromPawn(outpost, item.holderPawn, worn, count, out string fail))
                    {
                        if (!fail.NullOrEmpty())
                            Messages.Message(fail, outpost, MessageTypeDefOf.RejectInput);
                        continue;
                    }
                    if (!worn.Destroyed && armory.Uniques.Contains(worn))
                    {
                        uniques.Add(worn);
                        continue;
                    }
                    var row = new ThingDefCountClass(worn.def, count)
                    {
                        stuff = CompOutpostWarehouse.ResolveStuffForDeposit(worn.def, worn.Stuff)
                    };
                    if (worn.TryGetQuality(out QualityCategory q)) row.quality = q;
                    CompOutpostWarehouse.MergeCount(rows, row);
                    continue;
                }

                if (item.stockRow != null)
                    rows.Add(CompOutpostWarehouse.PlainStockRow(item.stockRow, item.sendCount));
                else if (item.thing != null && !item.thing.Destroyed)
                    uniques.Add(item.thing);
            }

            if (rows.Count == 0 && uniques.Count == 0) return false;
            return OutpostStorageShipping.TryLaunch(outpost, rows, uniques, 0f, destination, viaDropPod);
        }

        private Dictionary<WorldObject, List<InventoryRow>> GroupSelectionBySource()
        {
            var bySource = new Dictionary<WorldObject, List<InventoryRow>>();
            for (int i = 0; i < cachedRows.Count; i++)
            {
                InventoryRow r = cachedRows[i];
                if (!selectedRowIds.Contains(r.rowId)) continue;
                if (ResolveSendCount(r) <= 0) continue;
                if (r.source == null || SendBlockedKey(r) != null) continue;
                if (!bySource.TryGetValue(r.source, out List<InventoryRow> list))
                {
                    list = new List<InventoryRow>();
                    bySource[r.source] = list;
                }
                list.Add(r);
            }
            return bySource;
        }
    }
}
