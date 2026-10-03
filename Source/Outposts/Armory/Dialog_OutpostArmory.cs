using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    [StaticConstructorOnStartup]
    internal static class Dialog_OutpostArmoryAssets
    {
        internal static readonly Texture2D ArmoryIcon;
        internal static readonly Texture2D VirtualFoodIcon;
        internal static readonly Texture2D SendIcon;

        static Dialog_OutpostArmoryAssets()
        {
            ArmoryIcon = ResolveReconArmorIcon();
            VirtualFoodIcon = ContentFinder<Texture2D>.Get("UI/Commands/ConvertFood", false)
                ?? TexCommand.Install;
            SendIcon = ContentFinder<Texture2D>.Get("UI/Commands/DeliveryDestination", false)
                ?? TexCommand.Attack;
        }

        /// <summary>Vanilla recon armor inventory icon (Core). Prefer def uiIcon, then known tex paths.</summary>
        private static Texture2D ResolveReconArmorIcon()
        {
            ThingDef recon = DefDatabase<ThingDef>.GetNamedSilentFail("Apparel_ArmorRecon")
                ?? DefDatabase<ThingDef>.GetNamedSilentFail("Apparel_ReconArmor");
            if (recon?.uiIcon != null && recon.uiIcon != BaseContent.BadTex)
                return recon.uiIcon;

            return ContentFinder<Texture2D>.Get("Things/Pawn/Humanlike/Apparel/ArmorRecon/Apparel_ArmorRecon", false)
                ?? ContentFinder<Texture2D>.Get("Things/Pawn/Humanlike/Apparel/ArmorRecon/ReconArmor", false)
                ?? ContentFinder<Texture2D>.Get("UI/Commands/Armory", false)
                ?? TexCommand.Install;
        }
    }

    /// <summary>
    /// Two-column gear manager for one outpost: occupants with their gear on the left, loose
    /// armory contents on the right, drag-and-drop or click-to-move between them. Chrome matches
    /// the All Player Pawns roster (filterable headers, zebra rows, shared dropdown).
    /// </summary>
    public class Dialog_OutpostArmory : Window
    {
        private const float ToolbarHeight = 40f;
        private const float HeaderHeight = 28f;
        private const float FooterH = 28f;
        private const float ColGap = 12f;
        private const float ToolbarBtnHeight = 30f;
        private const float ToolbarBtnGap = 10f;
        private const float ActionStackTop = 4f;
        /// <summary>Tall enough for two gear-icon rows (90% of store IconSize) plus padding.</summary>
        private const float PawnRowH = 76f;
        private const float StoreRowH = 36f;
        private const float IconSize = 30.8f;
        /// <summary>Apparel / weapons / food icons: 10% smaller than store icons, two rows per pawn.</summary>
        private const float GearIconSize = IconSize * 0.9f;
        private const float GearIconGap = 4f;
        private const float ColPortrait = 48f;
        private const float ColPawnName = 120f;
        private const float ColSkill = 54f;
        private const float ColHealth = 52f;
        private const float GearSectionGap = 6f;
        /// <summary>Inset icons from the section hairline so elongated weapon art does not sit on the border.</summary>
        private const float GearSectionInnerPad = 5f;
        /// <summary>Width moved from the store column to the occupants column.</summary>
        private const float LeftColumnExtra = 180f;
        private const float ColItem = 220f;
        private const float ColType = 110f;
        private const float ColQuality = 90f;
        private const float ColCount = 56f;
        private const float ConvertBtnWidth = 200f;
        private const float InventoryBtnWidth = 220f;

        private static readonly Color GearSectionBorderColor = new Color(0.35f, 0.35f, 0.35f, 0.9f);

        private const int QualityFilterAll = -1;
        private const int QualityFilterNone = -2;

        private readonly WorldObject_WD_Outpost outpost;
        private Vector2 pawnScroll;
        private Vector2 storeScroll;

        private readonly List<StoreRow> storeRows = new List<StoreRow>();
        private readonly List<StoreRow> visibleStoreRows = new List<StoreRow>();
        private readonly List<Pawn> visiblePawns = new List<Pawn>();
        private readonly Dictionary<Pawn, List<ThingDefCountClass>> pawnInventory =
            new Dictionary<Pawn, List<ThingDefCountClass>>();

        private bool rowsDirty = true;
        private string pendingMessage;

        // Session filters (same static pattern as Window_AllPlayerPawns).
        private static string pawnSearchTerm = "";
        private static string itemSearchTerm = "";
        private static ArmoryTypeFilter typeFilter = ArmoryTypeFilter.All;
        private static int qualityFilter = QualityFilterAll;
        private static string storeSortColumn = "Item";
        private static bool storeSortAscending = true;
        private static string pawnSortColumn = "Name";
        private static bool pawnSortAscending = true;

        /// <summary>One display line: either an abstract stock row or a single unique Thing.</summary>
        private class StoreRow
        {
            public ThingDefCountClass Row;
            public Thing Unique;
            public string Label;
            public string TypeLabel;
            public string QualityLabel = "";
            public bool HasQuality;
            public QualityCategory Quality;
            public ArmoryTypeFilter TypeKind;
            public ThingDef Def => Unique?.def ?? Row?.thingDef;
            public int Count => Unique != null
                ? (Unique.stackCount > 0 ? Unique.stackCount : 1)
                : (Row?.count ?? 0);
        }

        public Dialog_OutpostArmory(WorldObject_WD_Outpost outpost)
        {
            this.outpost = outpost;
            forcePause = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            doCloseX = true;
            // Must stay false: a draggable window steals click-hold for window move and
            // kills item drag-and-drop before ButtonInvisibleDraggable can start a gesture.
            draggable = false;
            resizeable = true;
        }

        public override Vector2 InitialSize => new Vector2(1480f, 760f);

        private CompOutpostArmory Armory => CompOutpostArmory.Get(outpost);

        public override void DoWindowContents(Rect inRect)
        {
            if (outpost == null || outpost.Destroyed)
            {
                Close();
                return;
            }

            if (PawnRosterHeaderFilter.TryCloseDropdownOnCancel())
                return;
            if (WdItemDragDrop.TryCancelOnEscape())
                return;
            if (!WdItemDragDrop.DragActive && WdWindowEsc.TryCloseOnCancel(this))
                return;

            WdNavWindows.ProcessHotkeys();

            if (rowsDirty) RebuildRows();
            RebuildVisibleLists();

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width * 0.45f, 32f),
                OutpostTranslationUtil.TabHeadline(outpost, "TSA_WD_Armory_OpenButton"));
            Text.Font = GameFont.Small;

            DrawToolbar(inRect.width);

            float headerTop = ToolbarHeight + 4f;
            float listTop = headerTop + HeaderHeight + 4f;
            float bodyH = inRect.height - listTop - FooterH;
            float half = (inRect.width - ColGap) / 2f;
            float leftW = half + LeftColumnExtra;
            float rightW = half - LeftColumnExtra;

            DrawPawnColumn(new Rect(0f, headerTop, leftW, HeaderHeight + 4f + bodyH), listTop - headerTop, bodyH);
            DrawStoreColumn(new Rect(leftW + ColGap, headerTop, rightW, HeaderHeight + 4f + bodyH), listTop - headerTop, bodyH);

            DrawFooterNote(new Rect(0f, inRect.height - FooterH + 4f, inRect.width, FooterH - 4f));

            // Ghost and commit must happen outside every group or the ghost is clipped.
            WdItemDragDrop.ResolveFrame(OnDrop);
            PawnRosterHeaderFilter.DrawDropdownIfOpen();

            if (!string.IsNullOrEmpty(pendingMessage))
            {
                Messages.Message(pendingMessage, outpost, MessageTypeDefOf.RejectInput, false);
                pendingMessage = null;
            }
        }

        private void DrawToolbar(float width)
        {
            Rect inventoryBtn = new Rect(width - InventoryBtnWidth, ActionStackTop, InventoryBtnWidth, ToolbarBtnHeight);
            Rect convertBtn = new Rect(inventoryBtn.x - ToolbarBtnGap - ConvertBtnWidth, ActionStackTop, ConvertBtnWidth, ToolbarBtnHeight);

            float storedNutrition = GetStoredFoodNutrition();
            bool canConvert = storedNutrition > 0.01f && OutpostArmoryUtility.CanMutate(outpost);
            if (!canConvert) GUI.color = new Color(1f, 1f, 1f, 0.45f);
            if (WorldDomination_UIUtils.ButtonTextWithIcon(
                    convertBtn,
                    Dialog_OutpostArmoryAssets.VirtualFoodIcon,
                    "TSA_WD_Armory_ConvertFood".Translate())
                && canConvert)
            {
                ConvertStoredFood();
            }
            GUI.color = Color.white;
            TooltipHandler.TipRegion(convertBtn, canConvert
                ? "TSA_WD_Armory_ConvertFoodTip".Translate()
                : "TSA_WD_Armory_ConvertFoodNone".Translate());

            if (WorldDomination_UIUtils.ButtonTextWithIcon(
                    inventoryBtn,
                    Dialog_OutpostArmoryAssets.SendIcon,
                    "TSA_WD_Armory_OpenInventory".Translate()))
            {
                Close();
                WdNavWindows.OpenExclusive(() => new Window_AllPlayerGear());
            }
            TooltipHandler.TipRegion(inventoryBtn, "TSA_WD_Armory_OpenInventoryTip".Translate());
        }

        private void DrawPawnColumn(Rect rect, float headerOffset, float listH)
        {
            Rect headerRect = new Rect(rect.x, rect.y, rect.width, HeaderHeight);
            float curX = rect.x + ColPortrait;
            float gearW = Mathf.Max(180f, rect.width - ColPortrait - ColPawnName - ColSkill * 2f - ColHealth - 4f);
            float apparelW = gearW * 0.38f + 25f;
            float weaponsW = gearW * 0.31f;
            float foodW = gearW - apparelW - weaponsW;

            // Match roster hubs: Tiny headers in a short HeaderHeight (UI_WINDOWS.md).
            Text.Font = GameFont.Tiny;

            PawnRosterHeaderFilter.DrawFilterableHeader(
                ref curX, headerRect.y, ColPawnName, HeaderHeight,
                "TSA_WD_Armory_ColumnPawns".Translate(),
                pawnSortColumn == "Name", pawnSortAscending,
                TextAnchor.MiddleLeft,
                !pawnSearchTerm.NullOrEmpty(),
                "TSA_WD_FilterByName".Translate(),
                icon => PawnRosterHeaderFilter.OpenTextDropdown(
                    icon,
                    "TSA_WD_FilterByName".Translate(),
                    "TSA_WD_FilterByName".Translate(),
                    () => pawnSearchTerm,
                    v => { pawnSearchTerm = v ?? ""; },
                    () => { pawnSearchTerm = ""; }),
                () => SetPawnSort("Name"));

            DrawPawnSortHeader(ref curX, headerRect, ColSkill, SkillDefOf.Shooting.LabelCap, "Shooting");
            DrawPawnSortHeader(ref curX, headerRect, ColSkill, SkillDefOf.Melee.LabelCap, "Melee");
            DrawPawnSortHeader(ref curX, headerRect, ColHealth, "TSA_WD_PawnRoster_ColHealth".Translate(), "Health");

            DrawGearSectionBorder(curX, headerRect.y, HeaderHeight);
            DrawStaticSectionHeader(ref curX, headerRect, apparelW, "TSA_WD_Armory_SecApparel".Translate());
            DrawGearSectionBorder(curX, headerRect.y, HeaderHeight);
            DrawStaticSectionHeader(ref curX, headerRect, weaponsW, "TSA_WD_Armory_SecWeapons".Translate());
            DrawGearSectionBorder(curX, headerRect.y, HeaderHeight);
            DrawStaticSectionHeader(ref curX, headerRect, foodW, "TSA_WD_Armory_SecFoodDrugs".Translate());

            Text.Font = GameFont.Small;
            Widgets.DrawLineHorizontal(rect.x, headerRect.yMax, rect.width);

            Rect listRect = new Rect(rect.x, rect.y + headerOffset, rect.width, listH);

            if (visiblePawns.Count == 0)
            {
                string empty = outpost.Occupants == null || outpost.Occupants.Count == 0
                    ? "TSA_WD_Armory_NoOccupants".Translate()
                    : "TSA_WD_Armory_NoPawnMatch".Translate();
                Widgets.NoneLabelCenteredVertically(listRect, empty);
                return;
            }

            float viewH = visiblePawns.Count * PawnRowH + 8f;
            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, Mathf.Max(viewH, listH));
            Widgets.BeginScrollView(listRect, ref pawnScroll, viewRect);

            float y = 0f;
            for (int i = 0; i < visiblePawns.Count; i++)
            {
                DrawPawnRow(new Rect(0f, y, viewRect.width, PawnRowH), visiblePawns[i], i % 2 == 0,
                    apparelW, weaponsW, foodW);
                y += PawnRowH;
            }

            Widgets.EndScrollView();
        }

        private void DrawPawnSortHeader(ref float curX, Rect headerRect, float width, string label, string tag)
        {
            Rect r = new Rect(curX, headerRect.y, width, HeaderHeight);
            if (Mouse.IsOver(r)) Widgets.DrawHighlight(r);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            string text = label + (pawnSortColumn == tag ? (pawnSortAscending ? " ▲" : " ▼") : "");
            Widgets.Label(r, text.Truncate(width - 4f));
            if (Widgets.ButtonInvisible(r)) SetPawnSort(tag);
            Text.Anchor = TextAnchor.UpperLeft;
            curX += width;
        }

        private static void DrawStaticSectionHeader(ref float curX, Rect headerRect, float width, string label)
        {
            Rect r = new Rect(curX + GearSectionInnerPad, headerRect.y, width - GearSectionInnerPad, HeaderHeight);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(r, label.Truncate(Mathf.Max(8f, width - GearSectionInnerPad - 4f)));
            Text.Anchor = TextAnchor.UpperLeft;
            curX += width;
        }

        /// <summary>Dark grey hairline left of Apparel / Weapons / Food &amp; Drugs (same approach as roster skill separators).</summary>
        private static void DrawGearSectionBorder(float x, float y, float height)
        {
            Color prev = GUI.color;
            GUI.color = GearSectionBorderColor;
            Widgets.DrawLineVertical(x, y + 2f, height - 4f);
            GUI.color = prev;
        }

        private void DrawStoreColumn(Rect rect, float headerOffset, float listH)
        {
            Rect headerRect = new Rect(rect.x, rect.y, rect.width, HeaderHeight);
            float curX = rect.x;
            float catW = ColType;
            float qualityW = ColQuality;
            float countW = ColCount;
            float itemW = Mathf.Max(100f, rect.width - catW - qualityW - countW - 4f);

            Text.Font = GameFont.Tiny;

            PawnRosterHeaderFilter.DrawFilterableHeader(
                ref curX, headerRect.y, catW, HeaderHeight,
                "TSA_WD_Armory_HdrType".Translate(),
                storeSortColumn == "Type", storeSortAscending,
                TextAnchor.MiddleCenter,
                typeFilter != ArmoryTypeFilter.All,
                "TSA_WD_Armory_WeaponFilterTip".Translate(),
                icon => PawnRosterHeaderFilter.OpenChoiceDropdown(
                    icon,
                    "TSA_WD_Armory_HdrType".Translate(),
                    BuildTypeFilterChoices()),
                () => SetStoreSort("Type"));

            Rect countHdr = new Rect(curX, headerRect.y, countW, HeaderHeight);
            if (Mouse.IsOver(countHdr)) Widgets.DrawHighlight(countHdr);
            Text.Anchor = TextAnchor.MiddleCenter;
            string countText = "TSA_WD_Armory_HdrCount".Translate()
                + (storeSortColumn == "Count" ? (storeSortAscending ? " ▲" : " ▼") : "");
            Widgets.Label(countHdr, countText.Truncate(countW - 4f));
            if (Widgets.ButtonInvisible(countHdr)) SetStoreSort("Count");
            Text.Anchor = TextAnchor.UpperLeft;
            curX += countW;

            PawnRosterHeaderFilter.DrawFilterableHeader(
                ref curX, headerRect.y, itemW, HeaderHeight,
                "TSA_WD_Armory_HdrItem".Translate(),
                storeSortColumn == "Item", storeSortAscending,
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
                () => SetStoreSort("Item"));

            PawnRosterHeaderFilter.DrawFilterableHeader(
                ref curX, headerRect.y, qualityW, HeaderHeight,
                "TSA_WD_Armory_HdrQuality".Translate(),
                storeSortColumn == "Quality", storeSortAscending,
                TextAnchor.MiddleCenter,
                qualityFilter != QualityFilterAll,
                "TSA_WD_AllInventory_QualityFilterTip".Translate(),
                icon => PawnRosterHeaderFilter.OpenChoiceDropdown(
                    icon,
                    "TSA_WD_Armory_HdrQuality".Translate(),
                    BuildQualityFilterChoices()),
                () => SetStoreSort("Quality"));

            Text.Font = GameFont.Small;
            Widgets.DrawLineHorizontal(rect.x, headerRect.yMax, rect.width);

            Rect listRect = new Rect(rect.x, rect.y + headerOffset, rect.width, listH);
            WdItemDragDrop.RegisterDropTarget(listRect, this);

            if (storeRows.Count == 0)
            {
                Widgets.NoneLabelCenteredVertically(listRect, "TSA_WD_Armory_StoreEmpty".Translate());
                return;
            }
            if (visibleStoreRows.Count == 0)
            {
                Widgets.NoneLabelCenteredVertically(listRect, "TSA_WD_Armory_NoItemMatch".Translate());
                return;
            }

            float viewH = visibleStoreRows.Count * StoreRowH + 8f;
            float contentW = itemW + catW + qualityW + countW;
            Rect viewRect = new Rect(0f, 0f, Mathf.Max(listRect.width - 16f, contentW), Mathf.Max(viewH, listH));
            Widgets.BeginScrollView(listRect, ref storeScroll, viewRect);

            float y = 0f;
            for (int i = 0; i < visibleStoreRows.Count; i++)
            {
                DrawStoreRow(new Rect(0f, y, contentW, StoreRowH), visibleStoreRows[i], i % 2 == 0,
                    itemW, catW, qualityW, countW);
                y += StoreRowH;
            }

            Widgets.EndScrollView();
        }

        private void SetStoreSort(string tag)
        {
            if (storeSortColumn == tag) storeSortAscending = !storeSortAscending;
            else { storeSortColumn = tag; storeSortAscending = true; }
        }

        private void SetPawnSort(string tag)
        {
            if (pawnSortColumn == tag) pawnSortAscending = !pawnSortAscending;
            else { pawnSortColumn = tag; pawnSortAscending = true; }
        }

        private List<HeaderFilterChoice> BuildTypeFilterChoices()
        {
            var defs = new List<ThingDef>(storeRows.Count);
            for (int i = 0; i < storeRows.Count; i++) defs.Add(storeRows[i].Def);
            return OutpostArmoryUtility.BuildTypeFilterChoices(defs, typeFilter, f => typeFilter = f);
        }

        private List<HeaderFilterChoice> BuildQualityFilterChoices()
        {
            int total = storeRows.Count;
            int none = 0;
            var counts = new int[8];
            for (int i = 0; i < storeRows.Count; i++)
            {
                StoreRow r = storeRows[i];
                if (!r.HasQuality) { none++; continue; }
                int idx = (int)r.Quality;
                if (idx >= 0 && idx < counts.Length) counts[idx]++;
            }

            string CountOf(int n) => total > 0 ? n + "/" + total : null;

            var list = new List<HeaderFilterChoice>
            {
                new HeaderFilterChoice(
                    OutpostArmoryUtility.TypeFilterLabel(ArmoryTypeFilter.All),
                    qualityFilter == QualityFilterAll,
                    () => qualityFilter = QualityFilterAll,
                    separatorAfter: true,
                    countLabel: CountOf(total)),
                new HeaderFilterChoice(
                    "TSA_WD_AllInventory_QualityNone".Translate(),
                    qualityFilter == QualityFilterNone,
                    () => qualityFilter = QualityFilterNone,
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
                    () => qualityFilter = (int)captured,
                    countLabel: CountOf(n)));
            }

            return list;
        }

        private void RebuildVisibleLists()
        {
            visiblePawns.Clear();
            List<Pawn> occupants = outpost.Occupants;
            string pawnFilter = string.IsNullOrEmpty(pawnSearchTerm) ? null : pawnSearchTerm.ToLowerInvariant();
            if (occupants != null)
            {
                for (int i = 0; i < occupants.Count; i++)
                {
                    Pawn p = occupants[i];
                    if (p == null) continue;
                    if (pawnFilter != null && (p.LabelShortCap ?? "").ToLowerInvariant().IndexOf(pawnFilter, StringComparison.Ordinal) < 0)
                        continue;
                    visiblePawns.Add(p);
                }
            }

            visibleStoreRows.Clear();
            string itemFilter = string.IsNullOrEmpty(itemSearchTerm) ? null : itemSearchTerm.ToLowerInvariant();
            for (int i = 0; i < storeRows.Count; i++)
            {
                StoreRow row = storeRows[i];
                if (!OutpostArmoryUtility.MatchesTypeFilter(row.Def, typeFilter)) continue;
                if (!MatchesQualityFilter(row)) continue;
                if (itemFilter != null && (row.Label ?? "").ToLowerInvariant().IndexOf(itemFilter, StringComparison.Ordinal) < 0)
                    continue;
                visibleStoreRows.Add(row);
            }

            visibleStoreRows.Sort((a, b) =>
            {
                int cmp;
                switch (storeSortColumn)
                {
                    case "Type":
                        cmp = string.Compare(a.TypeLabel, b.TypeLabel, StringComparison.OrdinalIgnoreCase);
                        break;
                    case "Quality":
                        cmp = QualitySortKey(a).CompareTo(QualitySortKey(b));
                        break;
                    case "Count":
                        cmp = a.Count.CompareTo(b.Count);
                        break;
                    default:
                        cmp = string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase);
                        break;
                }
                if (cmp == 0) cmp = string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase);
                return storeSortAscending ? cmp : -cmp;
            });

            visiblePawns.Sort((a, b) =>
            {
                int cmp;
                switch (pawnSortColumn)
                {
                    case "Shooting":
                        cmp = OutpostStrengthBudgetUi.GetSkillLevel(a, SkillDefOf.Shooting)
                            .CompareTo(OutpostStrengthBudgetUi.GetSkillLevel(b, SkillDefOf.Shooting));
                        break;
                    case "Melee":
                        cmp = OutpostStrengthBudgetUi.GetSkillLevel(a, SkillDefOf.Melee)
                            .CompareTo(OutpostStrengthBudgetUi.GetSkillLevel(b, SkillDefOf.Melee));
                        break;
                    case "Health":
                        cmp = OutpostStrengthBudgetUi.GetHealthPercent(a)
                            .CompareTo(OutpostStrengthBudgetUi.GetHealthPercent(b));
                        break;
                    default:
                        cmp = string.Compare(a?.LabelShortCap, b?.LabelShortCap, StringComparison.OrdinalIgnoreCase);
                        break;
                }
                if (cmp == 0)
                    cmp = string.Compare(a?.LabelShortCap, b?.LabelShortCap, StringComparison.OrdinalIgnoreCase);
                return pawnSortAscending ? cmp : -cmp;
            });
        }

        private static bool MatchesQualityFilter(StoreRow r)
        {
            if (qualityFilter == QualityFilterAll) return true;
            if (qualityFilter == QualityFilterNone) return !r.HasQuality;
            return r.HasQuality && (int)r.Quality == qualityFilter;
        }

        private static int QualitySortKey(StoreRow r) =>
            r.HasQuality ? (int)r.Quality : -1;

        private void DrawPawnRow(Rect rect, Pawn pawn, bool zebra, float apparelW, float weaponsW, float foodW)
        {
            if (zebra) Widgets.DrawHighlight(rect);
            if (Mouse.IsOver(rect)) Widgets.DrawLightHighlight(rect);
            WdItemDragDrop.RegisterDropTarget(rect, pawn);

            float x = rect.x + 2f;
            Rect portrait = new Rect(x, rect.y + 4f, ColPortrait - 6f, PawnRowH - 8f);
            OutpostStrengthBudgetUi.DrawPawnPortrait(portrait, pawn);
            if (Widgets.ButtonInvisible(portrait))
                Find.WindowStack.Add(new Dialog_InfoCard(pawn));
            x = rect.x + ColPortrait;

            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(x, rect.y, ColPawnName, rect.height), pawn.LabelShortCap.Truncate(ColPawnName - 4f));
            Text.Anchor = TextAnchor.UpperLeft;
            x += ColPawnName;

            DrawSkillCell(new Rect(x, rect.y, ColSkill, rect.height), pawn, SkillDefOf.Shooting);
            x += ColSkill;
            DrawSkillCell(new Rect(x, rect.y, ColSkill, rect.height), pawn, SkillDefOf.Melee);
            x += ColSkill;

            Text.Anchor = TextAnchor.MiddleCenter;
            float hp = OutpostStrengthBudgetUi.GetHealthPercent(pawn);
            Widgets.Label(new Rect(x, rect.y, ColHealth, rect.height), Mathf.RoundToInt(hp).ToString() + "%");
            Text.Anchor = TextAnchor.UpperLeft;
            x += ColHealth;

            float gearBlockH = GearIconSize * 2f + GearIconGap;
            float gearY = rect.y + Mathf.Max(0f, (rect.height - gearBlockH) * 0.5f);
            DrawGearSectionBorder(x, rect.y, rect.height);
            DrawPawnSectionIcons(pawn, new Rect(x + GearSectionInnerPad, gearY, apparelW - GearSectionGap - GearSectionInnerPad, gearBlockH), PawnGearSection.Apparel);
            x += apparelW;
            DrawGearSectionBorder(x, rect.y, rect.height);
            DrawPawnSectionIcons(pawn, new Rect(x + GearSectionInnerPad, gearY, weaponsW - GearSectionGap - GearSectionInnerPad, gearBlockH), PawnGearSection.Weapons);
            x += weaponsW;
            DrawGearSectionBorder(x, rect.y, rect.height);
            DrawPawnSectionIcons(pawn, new Rect(x + GearSectionInnerPad, gearY, foodW - GearSectionGap - GearSectionInnerPad, gearBlockH), PawnGearSection.FoodDrugs);
        }

        private static void DrawSkillCell(Rect cell, Pawn pawn, SkillDef skill)
        {
            int level = OutpostStrengthBudgetUi.GetSkillLevel(pawn, skill);
            PlayerPawnRosterUtility.DrawSkillLevelWithPassion(cell, pawn, skill, level, false, null);
        }

        private enum PawnGearSection : byte { Apparel, Weapons, FoodDrugs }

        private void DrawPawnSectionIcons(Pawn pawn, Rect bounds, PawnGearSection section)
        {
            float x = bounds.x;
            float y = bounds.y;
            float xMax = bounds.xMax;
            float row1Y = bounds.y + GearIconSize + GearIconGap;
            bool onSecondRow = false;

            bool Advance(ref float cx, ref float cy, ref bool secondRow)
            {
                cx += GearIconSize + GearIconGap;
                if (cx + GearIconSize <= xMax + 0.01f) return true;
                if (secondRow) return false;
                secondRow = true;
                cx = bounds.x;
                cy = row1Y;
                return true;
            }

            if (section == PawnGearSection.Apparel)
            {
                List<Apparel> worn = pawn.apparel?.WornApparel;
                if (worn == null) return;
                for (int i = 0; i < worn.Count; i++)
                {
                    if (x + GearIconSize > xMax + 0.01f && onSecondRow) break;
                    if (x + GearIconSize > xMax + 0.01f)
                    {
                        onSecondRow = true;
                        x = bounds.x;
                        y = row1Y;
                    }
                    DrawDraggableThing(worn[i], pawn, x, y);
                    if (!Advance(ref x, ref y, ref onSecondRow)) break;
                }
                return;
            }

            if (section == PawnGearSection.Weapons)
            {
                var ordered = new List<(Thing thing, ThingDefCountClass stack)>();

                List<ThingWithComps> eq = pawn.equipment?.AllEquipmentListForReading;
                if (eq != null)
                {
                    for (int i = 0; i < eq.Count; i++)
                    {
                        ThingWithComps t = eq[i];
                        if (t != null) ordered.Add((t, null));
                    }
                }

                if (pawnInventory.TryGetValue(pawn, out List<ThingDefCountClass> inv))
                {
                    for (int i = 0; i < inv.Count; i++)
                    {
                        ThingDefCountClass e = inv[i];
                        if (e?.thingDef == null || e.count <= 0) continue;
                        if (!IsWeaponOrAmmo(e.thingDef)) continue;
                        Thing carried = FindInventoryThing(pawn, e.thingDef);
                        if (carried == null) continue;
                        ordered.Add((carried, e));
                    }
                }

                ordered.Sort((a, b) => WeaponDisplayOrder(a.thing?.def).CompareTo(WeaponDisplayOrder(b.thing?.def)));

                for (int i = 0; i < ordered.Count; i++)
                {
                    if (x + GearIconSize > xMax + 0.01f && onSecondRow) break;
                    if (x + GearIconSize > xMax + 0.01f)
                    {
                        onSecondRow = true;
                        x = bounds.x;
                        y = row1Y;
                    }

                    var entry = ordered[i];
                    if (entry.stack != null)
                        DrawDraggableInventoryIcon(entry.thing, pawn, entry.stack, x, y);
                    else
                        DrawDraggableThing(entry.thing, pawn, x, y);
                    if (!Advance(ref x, ref y, ref onSecondRow)) break;
                }
                return;
            }

            // Food, drugs, medicine (inventory ingestibles that are not weapons/ammo).
            if (!pawnInventory.TryGetValue(pawn, out List<ThingDefCountClass> pack) || pack.Count == 0)
                return;
            for (int i = 0; i < pack.Count; i++)
            {
                ThingDefCountClass e = pack[i];
                if (e?.thingDef == null || e.count <= 0) continue;
                if (!IsFoodDrugsOrMedicine(e.thingDef)) continue;
                if (x + GearIconSize > xMax + 0.01f && onSecondRow) break;
                if (x + GearIconSize > xMax + 0.01f)
                {
                    onSecondRow = true;
                    x = bounds.x;
                    y = row1Y;
                }
                Thing carried = FindInventoryThing(pawn, e.thingDef);
                if (carried == null) continue;
                DrawDraggableInventoryIcon(carried, pawn, e, x, y);
                if (!Advance(ref x, ref y, ref onSecondRow)) break;
            }
        }

        /// <summary>Melee, then ranged, then grenades, then loose ammo.</summary>
        private static int WeaponDisplayOrder(ThingDef def)
        {
            if (def == null) return 99;
            if (OutpostArmoryUtility.IsGrenadeWeapon(def)) return 2;
            if (def.IsMeleeWeapon) return 0;
            if (def.IsRangedWeapon) return 1;
            if (OutpostArmoryUtility.IsAmmoDef(def)) return 3;
            if (def.IsWeapon) return 1;
            return 4;
        }

        private static bool IsWeaponOrAmmo(ThingDef def)
        {
            if (def == null) return false;
            if (OutpostArmoryUtility.IsAmmoDef(def)) return true;
            return def.IsWeapon;
        }

        private static bool IsFoodDrugsOrMedicine(ThingDef def)
        {
            if (def == null) return false;
            if (def.IsMedicine || def.IsDrug) return true;
            return def.ingestible != null && def.IsNutritionGivingIngestible;
        }

        private void DrawDraggableInventoryIcon(Thing carried, Pawn pawn, ThingDefCountClass e, float x, float y)
        {
            Rect r = new Rect(x, y, GearIconSize, GearIconSize);
            Widgets.ThingIcon(r, carried);

            string tip = GearIconTip(r, carried, e.thingDef, e.count);
            TooltipHandler.TipRegion(r, tip);

            bool showCount = e.count > 1
                || OutpostArmoryUtility.IsAmmoDef(e.thingDef)
                || OutpostArmoryUtility.IsGrenadeWeapon(e.thingDef);
            if (showCount && e.count > 0)
                DrawCountBadge(r, e.count);

            if (WdItemDragDrop.DraggableIcon(r, carried, pawn, tip, e.thingDef.uiIcon))
                OpenItemInfo(carried);
        }

        private void DrawDraggableThing(Thing thing, Pawn owner, float x, float y)
        {
            if (thing == null) return;
            Rect r = new Rect(x, y, GearIconSize, GearIconSize);
            Widgets.ThingIcon(r, thing);
            string tip = GearIconTip(r, thing, thing.def, thing.stackCount);
            TooltipHandler.TipRegion(r, tip);

            if (thing.stackCount > 1
                || OutpostArmoryUtility.IsAmmoDef(thing.def)
                || OutpostArmoryUtility.IsGrenadeWeapon(thing.def))
                DrawCountBadge(r, thing.stackCount > 0 ? thing.stackCount : 1);

            if (WdItemDragDrop.DraggableIcon(r, thing, owner, tip, thing.def.uiIcon))
                OpenItemInfo(thing);
        }

        private static void OpenItemInfo(Thing thing)
        {
            if (thing == null || thing.Destroyed) return;
            Find.WindowStack.Add(new Dialog_InfoCard(thing));
        }

        private static void OpenItemInfo(ThingDef def)
        {
            if (def == null) return;
            Find.WindowStack.Add(new Dialog_InfoCard(def));
        }

        /// <summary>Tiny stack count on the icon corner: black plate (+2px each side), white digits.</summary>
        private static void DrawCountBadge(Rect iconRect, int count)
        {
            Text.Font = GameFont.Tiny;
            string countLabel = count.ToString();
            Vector2 size = Text.CalcSize(countLabel);
            // Prior plate was size.x+2; widen by 2px on each side → +6 total.
            const float padX = 3f;
            Rect countRect = new Rect(
                iconRect.xMax - size.x - padX * 2f - 1f,
                iconRect.yMax - size.y,
                size.x + padX * 2f,
                size.y);

            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            GUI.DrawTexture(countRect, BaseContent.WhiteTex);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.MiddleCenter;
            // Force readable white digits (Widgets.Label alone can inherit a washed-out style color).
            var prevContent = GUI.contentColor;
            GUI.contentColor = Color.white;
            Widgets.Label(countRect, countLabel);
            GUI.contentColor = prevContent;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = prev;
            Text.Font = GameFont.Small;
        }

        /// <summary>Icon tooltip; tainted apparel also gets a yellow outline and a tainted line.</summary>
        private static string GearIconTip(Rect r, Thing thing, ThingDef def, int count)
        {
            string tip = CountedTip(OutpostArmoryUtility.DisplayLabel(thing), def, count);
            string taintTip = OutpostArmoryUtility.TaintedTip(thing);
            if (taintTip == null) return tip;
            GUI.color = OutpostArmoryUtility.TaintedColor;
            Widgets.DrawBox(r, 1);
            GUI.color = Color.white;
            return tip + "\n\n" + taintTip;
        }

        private static string WithTag(string label, string tag) =>
            string.IsNullOrEmpty(tag) ? label : label + "  " + tag;

        /// <summary>Item name plus the exact carried count for stacks, ammo and grenades.</summary>
        private static string CountedTip(string label, ThingDef def, int count)
        {
            bool counted = count > 1
                || OutpostArmoryUtility.IsAmmoDef(def)
                || OutpostArmoryUtility.IsGrenadeWeapon(def);
            return counted ? label + " x" + Math.Max(1, count) : label;
        }

        private static Thing FindInventoryThing(Pawn pawn, ThingDef def)
        {
            if (pawn?.inventory?.innerContainer == null || def == null) return null;
            List<Thing> list = pawn.inventory.innerContainer.InnerListForReading;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i]?.def == def && !list[i].Destroyed) return list[i];
            }
            return null;
        }

        private void DrawStoreRow(Rect rect, StoreRow row, bool zebra, float itemW, float catW, float qualityW, float countW)
        {
            // Stripe only through the Count column (matches header span), not the scrollbar gutter.
            if (zebra) Widgets.DrawHighlight(rect);
            if (Mouse.IsOver(rect)) Widgets.DrawLightHighlight(rect);

            // Column order: Type | Count | Name (icon + label) | Quality
            float x = rect.x;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(x, rect.y, catW, rect.height), row.TypeLabel.Truncate(catW - 4f));
            x += catW;

            Widgets.Label(new Rect(x, rect.y, countW, rect.height), row.Count.ToString());
            x += countW;

            Rect iconRect = new Rect(x + 4f, rect.y + (rect.height - IconSize) * 0.5f, IconSize, IconSize);
            if (row.Unique != null) Widgets.ThingIcon(iconRect, row.Unique);
            else if (row.Def != null) Widgets.ThingIcon(iconRect, row.Def, row.Row?.stuff);

            Text.Anchor = TextAnchor.MiddleLeft;
            Rect labelRect = new Rect(iconRect.xMax + 8f, rect.y, itemW - IconSize - 14f, rect.height);
            string taintTip = OutpostArmoryUtility.TaintedTip(row.Unique);
            if (taintTip != null) GUI.color = OutpostArmoryUtility.TaintedColor;
            Widgets.Label(labelRect, row.Label.Truncate(labelRect.width - 4f));
            GUI.color = Color.white;
            TooltipHandler.TipRegion(labelRect, taintTip != null ? row.Label + "\n\n" + taintTip : row.Label);
            x = rect.x + catW + countW + itemW;

            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(x, rect.y, qualityW, rect.height),
                (row.HasQuality ? row.QualityLabel : "").Truncate(qualityW - 4f));
            Text.Anchor = TextAnchor.UpperLeft;

            object payload = (object)row.Unique ?? row.Row;
            if (WdItemDragDrop.DraggableIcon(rect, payload, this, row.Label, row.Def?.uiIcon))
            {
                if (row.Unique != null)
                    OpenItemInfo(row.Unique);
                else
                    OpenItemInfo(row.Def);
            }
        }

        private void DrawFooterNote(Rect rect)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(1f, 1f, 1f, 0.6f);
            Widgets.Label(rect, "TSA_WD_Armory_NormalizeNote".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private void OnDrop(object payload, object source, object target)
        {
            if (payload == null || target == null) return;

            // Pawn -> store
            if (source is Pawn fromPawn && ReferenceEquals(target, this))
            {
                if (payload is Thing t) BeginStoreFromPawn(fromPawn, t);
                return;
            }

            // Store -> pawn
            if (ReferenceEquals(source, this) && target is Pawn toPawn)
            {
                GiveToPawn(toPawn, payload);
                return;
            }

            // Pawn -> pawn routes through the store so every validation runs once.
            if (source is Pawn srcPawn && target is Pawn dstPawn && srcPawn != dstPawn && payload is Thing moved)
            {
                // Fail before detaching: a filled primary slot must leave the weapon on the source.
                if (!OutpostArmoryUtility.CanReceiveThing(dstPawn, moved, out string blockReason))
                {
                    pendingMessage = blockReason;
                    return;
                }

                ThingDef def = moved.def;
                ThingDef stuff = moved.Stuff;
                moved.TryGetQuality(out QualityCategory q);
                bool wasUnique = OutpostArmoryUtility.IsIrreplaceable(moved);
                int available = moved.stackCount > 0 ? moved.stackCount : 1;

                void FinishMove(int count)
                {
                    if (count <= 0) return;
                    if (!OutpostArmoryUtility.CanReceiveThing(dstPawn, moved, out string againBlocked))
                    {
                        pendingMessage = againBlocked;
                        return;
                    }
                    if (!StoreFromPawn(srcPawn, moved, count)) return;

                    if (wasUnique)
                    {
                        Thing found = Armory?.Uniques != null && Armory.Uniques.Contains(moved) ? moved : FindUnique(def);
                        if (found != null) GiveUniqueImmediate(dstPawn, found);
                    }
                    else
                    {
                        // Count already chosen: do not open a second picker.
                        GiveStockImmediate(dstPawn, new ThingDefCountClass(def, count) { stuff = stuff, quality = q }, count);
                    }
                }

                if (available > 1)
                    PromptCount(def.LabelCap, available, FinishMove);
                else
                    FinishMove(available);
            }
        }

        private Thing FindUnique(ThingDef def)
        {
            ThingOwner<Thing> uniques = Armory?.Uniques;
            if (uniques == null) return null;
            for (int i = 0; i < uniques.Count; i++)
            {
                if (uniques[i]?.def == def) return uniques[i];
            }
            return null;
        }

        /// <summary>
        /// Ask for a count whenever more than one item can leave the pawn. Ammo and food stacks
        /// both need this; drop deferral would otherwise lose Shift.
        /// </summary>
        private void BeginStoreFromPawn(Pawn pawn, Thing thing)
        {
            if (pawn == null || thing == null || thing.Destroyed) return;
            int available = thing.stackCount > 0 ? thing.stackCount : 1;
            if (available > 1)
            {
                Thing captured = thing;
                Pawn capturedPawn = pawn;
                PromptCount(thing.def.LabelCap, available, chosen => StoreFromPawn(capturedPawn, captured, chosen));
                return;
            }
            StoreFromPawn(pawn, thing, available);
        }

        private bool StoreFromPawn(Pawn pawn, Thing thing, int count)
        {
            if (!OutpostArmoryUtility.TryStoreFromPawn(outpost, pawn, thing, count, out string fail))
            {
                if (!string.IsNullOrEmpty(fail)) pendingMessage = fail;
                return false;
            }
            NotifyChanged();
            return true;
        }

        private void GiveToPawn(Pawn pawn, object payload)
        {
            if (payload is Thing unique)
            {
                ThingOwner<Thing> uniques = Armory?.Uniques;
                if (uniques == null || !uniques.Contains(unique)) return;

                int available = unique.stackCount > 0 ? unique.stackCount : 1;
                int fit = OutpostArmoryUtility.ClampAssignCount(pawn, unique.def, available);
                if (fit <= 0)
                {
                    pendingMessage = OutpostArmoryUtility.NoCapacityReason(pawn, unique.def);
                    return;
                }
                if (available > 1 && (fit > 1 || OutpostArmoryUtility.NeedsAssignCountPrompt(unique.def)))
                {
                    Thing captured = unique;
                    // Picker max is what fits; ammo/grenades always prompt when more than one unit is available to take.
                    int pickerMax = Math.Max(1, fit);
                    if (pickerMax <= 1 && OutpostArmoryUtility.NeedsAssignCountPrompt(unique.def) && available > 1)
                        pickerMax = available; // let the player pick; TryGive clamps on confirm
                    PromptCount(unique.def.LabelCap, pickerMax,
                        chosen =>
                        {
                            if (chosen <= 0) return;
                            if (!uniques.Contains(captured)) return;
                            int give = OutpostArmoryUtility.ClampAssignCount(pawn, captured.def, chosen);
                            if (give <= 0)
                            {
                                Messages.Message(OutpostArmoryUtility.NoCapacityReason(pawn, captured.def), outpost, MessageTypeDefOf.RejectInput, false);
                                return;
                            }
                            Thing taken = uniques.Take(captured, give);
                            if (OutpostArmoryUtility.TryGiveUniqueToPawn(outpost, pawn, taken, out string fail))
                                NotifyChanged();
                            else if (!string.IsNullOrEmpty(fail))
                                Messages.Message(fail, outpost, MessageTypeDefOf.RejectInput, false);
                        });
                    return;
                }

                Thing takenAll = uniques.Take(unique, available);
                if (OutpostArmoryUtility.TryGiveUniqueToPawn(outpost, pawn, takenAll, out string failU))
                    NotifyChanged();
                else if (!string.IsNullOrEmpty(failU))
                    pendingMessage = failU;
                return;
            }

            if (payload is ThingDefCountClass row)
            {
                TryBeginStockGive(pawn, row);
                return;
            }
        }

        /// <summary>
        /// Opens a count picker whenever more than one unit can be assigned. Ammo and grenades
        /// always prompt (CE stack counts), even when the inventory-fit clamp is wrong/low.
        /// </summary>
        private bool TryBeginStockGive(Pawn pawn, ThingDefCountClass row)
        {
            int stored = Armory?.GetStockCountMatching(row) ?? 0;
            if (stored <= 0) return false;

            int fit = OutpostArmoryUtility.ClampAssignCount(pawn, row.thingDef, stored);
            bool forcePrompt = OutpostArmoryUtility.NeedsAssignCountPrompt(row.thingDef);

            if (fit <= 0 && !forcePrompt)
            {
                pendingMessage = OutpostArmoryUtility.NoCapacityReason(pawn, row.thingDef);
                return false;
            }

            // Prefer inventory-fit as the picker max. For ammo/grenades, if CE reports fit <= 1
            // while the store holds more, still open the picker on the stored count so the player
            // can choose; TryGiveStockToPawn clamps again on confirm.
            int pickerMax = fit > 0 ? fit : stored;
            if (forcePrompt && stored > 1 && pickerMax <= 1)
                pickerMax = stored;

            ThingDefCountClass capturedRow = CompOutpostWarehouse.CloneStockRowPreservingExtras(row, row.count);
            Pawn capturedPawn = pawn;

            if (pickerMax > 1)
            {
                PromptCount(row.thingDef.LabelCap, pickerMax, chosen =>
                {
                    if (chosen <= 0) return;
                    if (OutpostArmoryUtility.TryGiveStockToPawn(outpost, capturedPawn, capturedRow, chosen, out string f))
                        NotifyChanged();
                    else if (!string.IsNullOrEmpty(f))
                        Messages.Message(f, outpost, MessageTypeDefOf.RejectInput, false);
                });
                return true;
            }

            if (fit <= 0)
            {
                pendingMessage = OutpostArmoryUtility.NoCapacityReason(pawn, row.thingDef);
                return false;
            }

            if (OutpostArmoryUtility.TryGiveStockToPawn(outpost, pawn, row, fit, out string fail))
                NotifyChanged();
            else if (!string.IsNullOrEmpty(fail))
                pendingMessage = fail;
            return false;
        }

        private void GiveStockImmediate(Pawn pawn, ThingDefCountClass row, int count)
        {
            if (OutpostArmoryUtility.TryGiveStockToPawn(outpost, pawn, row, count, out string fail))
                NotifyChanged();
            else if (!string.IsNullOrEmpty(fail))
                Messages.Message(fail, outpost, MessageTypeDefOf.RejectInput, false);
        }

        private void GiveUniqueImmediate(Pawn pawn, Thing unique)
        {
            ThingOwner<Thing> uniques = Armory?.Uniques;
            if (uniques == null || unique == null || !uniques.Contains(unique)) return;
            Thing taken = uniques.Take(unique, unique.stackCount > 0 ? unique.stackCount : 1);
            if (OutpostArmoryUtility.TryGiveUniqueToPawn(outpost, pawn, taken, out string fail))
                NotifyChanged();
            else if (!string.IsNullOrEmpty(fail))
                Messages.Message(fail, outpost, MessageTypeDefOf.RejectInput, false);
        }

        private void PromptCount(string label, int max, Action<int> onConfirm)
        {
            if (max <= 0) return;
            if (max == 1)
            {
                onConfirm?.Invoke(1);
                return;
            }

            void Open()
            {
                Find.WindowStack.Add(new Dialog_WdCountPicker(
                    "TSA_WD_Armory_SplitTitle".Translate(label),
                    max,
                    onConfirm));
            }

            // Drop callbacks already run via ExecuteWhenFinished (past Repaint). Opening another
            // deferred window from inside that callback is unreliable; open immediately then.
            // Only defer when still inside Layout/Repaint (plain click path).
            Event e = Event.current;
            if (e != null && (e.type == EventType.Repaint || e.type == EventType.Layout))
                LongEventHandler.ExecuteWhenFinished(Open);
            else
                Open();
        }

        private float GetStoredFoodNutrition() => OutpostFoodConversion.StoredNutrition(outpost);

        private void ConvertStoredFood()
        {
            float applied = OutpostFoodConversion.ConvertRows(
                outpost, OutpostFoodConversion.AllFoodRows(outpost), out _);

            Messages.Message(
                applied > 0.01f
                    ? "TSA_WD_Armory_ConvertedFood".Translate(applied.ToString("F1"), outpost.LabelCap)
                    : "TSA_WD_Armory_ConvertFoodNone".Translate(),
                outpost,
                applied > 0.01f ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.RejectInput,
                false);

            NotifyChanged();
        }

        private static string TypeLabelFor(ThingDef def, ArmoryTypeFilter kind) =>
            OutpostArmoryUtility.TypeFilterLabel(kind);

        private void NotifyChanged()
        {
            rowsDirty = true;
            outpost.NotifyVirtualPawnsChanged();
            Window_OutpostOverview.InvalidateCache();
        }

        private void RebuildRows()
        {
            rowsDirty = false;
            storeRows.Clear();
            pawnInventory.Clear();

            var armory = Armory;
            if (armory != null)
            {
                List<ThingDefCountClass> stock = armory.ArmoryRows();
                for (int i = 0; i < stock.Count; i++)
                {
                    ThingDefCountClass e = stock[i];
                    ArmoryTypeFilter kind = OutpostArmoryUtility.TypeFilterFor(e.thingDef);
                    bool hasQuality = e.thingDef.HasComp(typeof(CompQuality));
                    storeRows.Add(new StoreRow
                    {
                        Row = e,
                        Label = FormatStoreItemName(e.thingDef, e.stuff),
                        TypeKind = kind,
                        TypeLabel = TypeLabelFor(e.thingDef, kind),
                        HasQuality = hasQuality,
                        Quality = e.quality,
                        QualityLabel = hasQuality ? e.quality.GetLabel().CapitalizeFirst() : ""
                    });
                }

                ThingOwner<Thing> uniques = armory.Uniques;
                if (uniques != null)
                {
                    for (int i = 0; i < uniques.Count; i++)
                    {
                        Thing t = uniques[i];
                        if (t == null) continue;
                        ArmoryTypeFilter kind = OutpostArmoryUtility.TypeFilterFor(t.def);
                        bool hasQuality = t.TryGetQuality(out QualityCategory q);
                        storeRows.Add(new StoreRow
                        {
                            Unique = t,
                            Label = WithTag(FormatStoreItemName(t.def, t.Stuff), OutpostArmoryUtility.StoredThingTag(t)),
                            TypeKind = kind,
                            TypeLabel = TypeLabelFor(t.def, kind),
                            HasQuality = hasQuality,
                            Quality = q,
                            QualityLabel = hasQuality ? q.GetLabel().CapitalizeFirst() : ""
                        });
                    }
                }
            }

            List<Pawn> occupants = outpost.Occupants;
            if (occupants == null) return;
            for (int i = 0; i < occupants.Count; i++)
            {
                Pawn p = occupants[i];
                if (p == null) continue;
                pawnInventory[p] = VirtualPawnSummary.SnapshotPawnInventory(p);
            }
        }

        /// <summary>Item name without quality (quality lives in its own column).</summary>
        private static string FormatStoreItemName(ThingDef def, ThingDef stuff)
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

        public override void PreClose()
        {
            base.PreClose();
            WdItemDragDrop.Cancel();
        }
    }
}
