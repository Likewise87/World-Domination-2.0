using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace TSA_WorldDomination
{
    [StaticConstructorOnStartup]
    internal static class Dialog_OutpostArmoryAssets
    {
        internal static readonly Texture2D ArmoryIcon;
        internal static readonly Texture2D VirtualFoodIcon;
        internal static readonly Texture2D SendIcon;
        internal static readonly Texture2D AmmoIcon;

        static Dialog_OutpostArmoryAssets()
        {
            ArmoryIcon = ResolveReconArmorIcon();
            VirtualFoodIcon = ContentFinder<Texture2D>.Get("UI/Commands/ConvertFood", false)
                ?? TexCommand.Install;
            SendIcon = ContentFinder<Texture2D>.Get("UI/Commands/DeliveryDestination", false)
                ?? TexCommand.Attack;
            AmmoIcon = ResolveCeAmmoIcon();
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

        /// <summary>Any common CE ammo uiIcon when CE is present; otherwise a generic attack icon.</summary>
        private static Texture2D ResolveCeAmmoIcon()
        {
            if (OutpostCeAmmoCompat.IsCeActive)
            {
                string[] prefer =
                {
                    "Ammo_556x45mmNATO_FMJ",
                    "Ammo_762x51mmNATO_FMJ",
                    "Ammo_9x19mmParabellum_FMJ",
                    "Ammo_12Gauge_Buck",
                    "Ammo_4430NATO_FMJ"
                };
                for (int i = 0; i < prefer.Length; i++)
                {
                    ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(prefer[i]);
                    if (def?.uiIcon != null && def.uiIcon != BaseContent.BadTex)
                        return def.uiIcon;
                }

                List<ThingDef> all = DefDatabase<ThingDef>.AllDefsListForReading;
                for (int i = 0; i < all.Count; i++)
                {
                    ThingDef def = all[i];
                    if (def == null || !OutpostCeAmmoCompat.IsCeAmmoDef(def)) continue;
                    if (def.uiIcon != null && def.uiIcon != BaseContent.BadTex)
                        return def.uiIcon;
                }
            }

            return ContentFinder<Texture2D>.Get("UI/Commands/LaunchReport", false)
                ?? TexCommand.Attack;
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
        /// <summary>RimWorld scroll-view gutter; keep content out of this strip so drag hits do not steal the bars.</summary>
        private const float ScrollbarSize = 16f;
        private const float ToolbarBtnHeight = 30f;
        private const float ToolbarBtnGap = 10f;
        private const float ActionStackTop = 4f;
        /// <summary>Tall enough for two gear-icon rows (90% of store IconSize) plus padding.</summary>
        private const float PawnRowH = 76f;
        private const float PawnRowHTall = 90f;
        private const float StoreRowH = 36f;
        private const float IconSize = 30.8f;
        /// <summary>Apparel / weapons / food icons: 10% smaller than store icons, two rows per pawn.</summary>
        private const float GearIconSize = IconSize * 0.9f;
        private const float GearIconGap = 4f;
        private const float ColPortrait = 35f;
        /// <summary>Match <see cref="WITab_Outpost_Pawns"/> name floor (fixed here; spare width grows gear sections).</summary>
        private const float ColPawnName = 90f;
        private const float ColPawnType = 96f;
        private const float ColStar = 56f;
        private const float ColNew = 56f;
        private const float ColAge = 44f;
        private const float ColSkill = 54f;
        private const float ColHealth = 52f;
        private const float ColTraits = 128f;
        private const float ColXenotype = 100f;
        private const float ColPsycasts = 110f;
        private const float ColIdeology = 110f;
        private const float ColMass = 57f;
        private const float ColBulk = 57f;
        private const float GearSectionGap = 6f;
        /// <summary>Preferred gear section widths; grow to fill spare viewport, never shrink below these (scroll instead).</summary>
        private const float GearApparelPref = 200f;
        private const float GearWeaponsPref = 84f;
        private const float GearConsumablesPref = 170f;
        private const float GearOtherPref = 170f;
        /// <summary>Inset icons from the section hairline so elongated weapon art does not sit on the border.</summary>
        private const float GearSectionInnerPad = 5f;
        /// <summary>Width moved from the store column to the occupants column.</summary>
        private const float LeftColumnExtra = 300f;
        private const float ColItem = 220f;
        private const float ColType = 90f;
        private const float ColQuality = 90f;
        private const float ColCount = 56f;
        private const float InventoryBtnWidth = 200f;
        /// <summary>Left edge stays at the old 220-wide slot; button is 20px narrower toward the right.</summary>
        private const float InventoryBtnLeftInset = 220f;
        private const float FoodMenuBtnWidth = 110f;
        private const float AmmoMenuBtnWidth = 110f;

        private const PawnRosterColumnWindow ColWindow = PawnRosterColumnWindow.OutpostArmory;

        private static readonly Color GearSectionBorderColor = new Color(0.35f, 0.35f, 0.35f, 0.9f);
        private static readonly Vector2 PortraitSize = new Vector2(31f, 31f);

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
        private readonly Dictionary<Pawn, CarryLoadCache> pawnCarryLoad =
            new Dictionary<Pawn, CarryLoadCache>();

        private struct CarryLoadCache
        {
            public float MassCur;
            public float MassCap;
            public float BulkCur;
            public float BulkCap;
            public bool HasBulk;
        }

        private bool rowsDirty = true;
        private string pendingMessage;

        // Session filters (same static pattern as Window_AllPlayerPawns).
        private static string pawnSearchTerm = "";
        private static string itemSearchTerm = "";
        private static ArmoryTypeFilter typeFilter = ArmoryTypeFilter.All;
        private static int qualityFilter = QualityFilterAll;
        private static PlayerPawnTypeFilter pawnTypeFilter = PlayerPawnTypeFilter.All;
        private static OutpostPawnStarFilter starFilter = OutpostPawnStarFilter.All;
        private static PawnRosterJoinedFilter joinedFilter = PawnRosterJoinedFilter.All;
        private static string xenotypeFilter = "";
        private static string psycastFilter = "";
        private static string ideoFilter = "";
        private static string storeSortColumn = "Item";
        private static bool storeSortAscending = true;
        private static string pawnSortColumn = "Name";
        private static bool pawnSortAscending = true;

        private static string _starHeaderTip;
        private static string _joinStampHeaderTip;

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
            WdItemDragDrop.ClearInputBlockers();

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
            Rect inventoryBtn = new Rect(width - InventoryBtnLeftInset, ActionStackTop, InventoryBtnWidth, ToolbarBtnHeight);
            float nextX = inventoryBtn.x;

            if (OutpostCeAmmoCompat.IsCeActive)
            {
                Rect ammoBtn = new Rect(nextX - ToolbarBtnGap - AmmoMenuBtnWidth, ActionStackTop, AmmoMenuBtnWidth, ToolbarBtnHeight);
                nextX = ammoBtn.x;
                if (WorldDomination_UIUtils.ButtonTextWithIcon(
                        ammoBtn,
                        Dialog_OutpostArmoryAssets.AmmoIcon,
                        "TSA_WD_Armory_MenuAmmo".Translate()))
                {
                    OpenAmmoMenu();
                }
                TooltipHandler.TipRegion(ammoBtn, "TSA_WD_Armory_MenuAmmoTip".Translate());
            }

            Rect foodBtn = new Rect(nextX - ToolbarBtnGap - FoodMenuBtnWidth, ActionStackTop, FoodMenuBtnWidth, ToolbarBtnHeight);
            if (WorldDomination_UIUtils.ButtonTextWithIcon(
                    foodBtn,
                    Dialog_OutpostArmoryAssets.VirtualFoodIcon,
                    "TSA_WD_Armory_MenuFood".Translate()))
            {
                OpenFoodMenu();
            }
            TooltipHandler.TipRegion(foodBtn, "TSA_WD_Armory_MenuFoodTip".Translate());

            PlayerPawnRosterUtility.DrawRosterViewControls(
                ActionStackTop,
                ToolbarBtnHeight,
                foodBtn.x - ToolbarBtnGap,
                ColWindow,
                RestoreDefaultView,
                () => Find.WindowStack.Add(new Dialog_PawnRosterColumns(ColWindow, OnColumnsChanged)));

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

        private static bool ColOn(string id) => PlayerPawnRosterUtility.ColVisible(ColWindow, id);

        private void OnColumnsChanged()
        {
            if (!IsPawnSortColumnVisible(pawnSortColumn))
                ClearPawnSortToDefault();
            rowsDirty = true;
        }

        private static bool IsPawnSortColumnVisible(string tag)
        {
            switch (tag)
            {
                case "Name": return true;
                case "PawnType": return ColOn(PawnRosterColumnIds.Type);
                case "Starred": return ColOn(PawnRosterColumnIds.Star);
                case "New": return ColOn(PawnRosterColumnIds.New);
                case "Age": return ColOn(PawnRosterColumnIds.Age);
                case "Health": return ColOn(PawnRosterColumnIds.Health);
                case "Traits": return ColOn(PawnRosterColumnIds.Traits);
                case "Xenotype": return ColOn(PawnRosterColumnIds.Xenotype);
                case "Psycasts": return ColOn(PawnRosterColumnIds.Psycasts);
                case "Ideology": return ColOn(PawnRosterColumnIds.Ideology);
                case "Mass": return ColOn(PawnRosterColumnIds.Mass);
                case "Bulk": return ColOn(PawnRosterColumnIds.Bulk);
                default:
                    SkillDef skill = DefDatabase<SkillDef>.GetNamedSilentFail(tag);
                    return skill != null && ColOn(PawnRosterColumnIds.Skill(skill));
            }
        }

        private void ClearPawnSortToDefault()
        {
            pawnSortColumn = "Name";
            pawnSortAscending = true;
        }

        private void RestoreDefaultView()
        {
            ClearPawnSortToDefault();
            storeSortColumn = "Item";
            storeSortAscending = true;
            pawnSearchTerm = "";
            itemSearchTerm = "";
            typeFilter = ArmoryTypeFilter.All;
            qualityFilter = QualityFilterAll;
            pawnTypeFilter = PlayerPawnTypeFilter.All;
            starFilter = OutpostPawnStarFilter.All;
            joinedFilter = PawnRosterJoinedFilter.All;
            xenotypeFilter = "";
            psycastFilter = "";
            ideoFilter = "";
            pawnScroll = Vector2.zero;
            storeScroll = Vector2.zero;
            rowsDirty = true;
            PlayerPawnRosterUtility.ResetSkillDisplayOptions(ColWindow);
            WorldComponent_PawnRosterColumnPrefs.Get()?.ResetToDefaults(ColWindow);
            PawnRosterTraitFilter.Clear();
            PawnRosterHeaderFilter.CloseDropdown();
        }

        private static void EnsureStarHeaderTip()
        {
            if (_starHeaderTip == null)
                _starHeaderTip = "TSA_WD_AllPlayerPawns_StarTip".Translate();
        }

        private static void EnsureJoinStampHeaderTip()
        {
            if (_joinStampHeaderTip == null)
                _joinStampHeaderTip = "TSA_WD_PawnRoster_ColNewTip".Translate();
        }

        private float EffectivePawnRowH() =>
            ColOn(PawnRosterColumnIds.Traits) || ColOn(PawnRosterColumnIds.Psycasts)
                ? PawnRowHTall
                : PawnRowH;

        private float ComputeMetaColumnsWidth()
        {
            float w = 0f;
            if (ColOn(PawnRosterColumnIds.Type)) w += ColPawnType;
            if (ColOn(PawnRosterColumnIds.Star)) w += ColStar;
            if (ColOn(PawnRosterColumnIds.New)) w += ColNew;
            if (ColOn(PawnRosterColumnIds.Age)) w += ColAge;
            if (ColOn(PawnRosterColumnIds.Health)) w += ColHealth;
            if (ColOn(PawnRosterColumnIds.Traits)) w += ColTraits;
            if (ColOn(PawnRosterColumnIds.Xenotype)) w += ColXenotype;
            if (ColOn(PawnRosterColumnIds.Psycasts)) w += ColPsycasts;
            if (ColOn(PawnRosterColumnIds.Ideology)) w += ColIdeology;
            if (ColOn(PawnRosterColumnIds.Mass)) w += ColMass;
            if (ColOn(PawnRosterColumnIds.Bulk)) w += ColBulk;
            SkillDef[] skills = PlayerPawnRosterUtility.AllSkillColumns;
            for (int i = 0; i < skills.Length; i++)
            {
                if (ColOn(PawnRosterColumnIds.Skill(skills[i])))
                    w += ColSkill;
            }
            return w;
        }

        /// <summary>
        /// Preferred column widths; leftover viewport is shared across gear sections. When the natural
        /// total exceeds the viewport, keep preferred sizes and scroll horizontally.
        /// </summary>
        private void ComputePawnLayoutWidths(float viewportW, out float contentW,
            out float apparelW, out float weaponsW, out float consumablesW, out float otherInvW, out bool showOtherInv)
        {
            showOtherInv = ColOn(PawnRosterColumnIds.OtherInventory);
            apparelW = GearApparelPref;
            weaponsW = GearWeaponsPref;
            consumablesW = GearConsumablesPref;
            otherInvW = showOtherInv ? GearOtherPref : 0f;

            float natural = ColPortrait + ColPawnName + ComputeMetaColumnsWidth()
                + apparelW + weaponsW + consumablesW + otherInvW;
            float view = Mathf.Max(0f, viewportW);
            if (natural <= view + 0.5f)
            {
                float spare = view - natural;
                int gearN = showOtherInv ? 4 : 3;
                float share = spare / gearN;
                apparelW += share;
                weaponsW += share;
                consumablesW += share;
                if (showOtherInv) otherInvW += share;
                contentW = view;
            }
            else
            {
                contentW = natural;
            }
        }

        private void OpenFoodMenu()
        {
            bool canMutate = OutpostArmoryUtility.CanMutate(outpost);
            bool canConvert = GetStoredFoodNutrition() > 0.01f && canMutate;
            var opts = new List<FloatMenuOption>
            {
                new FloatMenuOption(
                    "TSA_WD_Armory_Menu_RemoveFood".Translate(),
                    canMutate ? StripAllFood : null)
                {
                    tooltip = "TSA_WD_Armory_Menu_RemoveFoodTip".Translate()
                },
                new FloatMenuOption(
                    "TSA_WD_Armory_Menu_AssignFood".Translate(),
                    canMutate ? AssignFoodToPawns : null)
                {
                    tooltip = "TSA_WD_Armory_Menu_AssignFoodTip".Translate()
                },
                new FloatMenuOption(
                    "TSA_WD_Armory_Menu_ConvertFood".Translate(),
                    canConvert ? ConvertStoredFood : null)
                {
                    tooltip = canConvert
                        ? "TSA_WD_Armory_ConvertFoodTip".Translate()
                        : "TSA_WD_Armory_ConvertFoodNone".Translate()
                }
            };
            Find.WindowStack.Add(new FloatMenu(opts));
        }

        private void OpenAmmoMenu()
        {
            bool canMutate = OutpostArmoryUtility.CanMutate(outpost);
            var opts = new List<FloatMenuOption>
            {
                new FloatMenuOption(
                    "TSA_WD_Armory_Menu_RemoveAmmo".Translate(),
                    canMutate ? StripAllAmmo : null)
                {
                    tooltip = "TSA_WD_Armory_Menu_RemoveAmmoTip".Translate()
                },
                new FloatMenuOption(
                    "TSA_WD_Armory_Menu_AddAmmo".Translate(),
                    canMutate ? AutoAssignAmmo : null)
                {
                    tooltip = "TSA_WD_Armory_Menu_AddAmmoTip".Translate()
                }
            };
            Find.WindowStack.Add(new FloatMenu(opts));
        }

        private void DrawPawnColumn(Rect rect, float headerOffset, float listH)
        {
            EnsureStarHeaderTip();
            EnsureJoinStampHeaderTip();

            float viewportW = Mathf.Max(0f, rect.width - 16f);
            ComputePawnLayoutWidths(viewportW, out float contentW,
                out float apparelW, out float weaponsW, out float consumablesW, out float otherInvW, out bool showOtherInv);
            float rowH = EffectivePawnRowH();

            Rect headerClip = new Rect(rect.x, rect.y, rect.width, HeaderHeight);
            // Keep header columns aligned with horizontally scrolled body content.
            Rect headerRect = new Rect(0f, 0f, contentW, HeaderHeight);
            Widgets.BeginGroup(headerClip);
            float curX = ColPortrait - pawnScroll.x;

            // Match roster hubs: Tiny headers in a short HeaderHeight (UI_WINDOWS.md).
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;

            if (ColOn(PawnRosterColumnIds.Type))
            {
                PawnRosterHeaderFilter.DrawFilterableHeader(
                    ref curX, headerRect.y, ColPawnType, HeaderHeight,
                    "TSA_WD_AllPlayerPawns_ColPawnType".Translate(),
                    pawnSortColumn == "PawnType", pawnSortAscending,
                    TextAnchor.MiddleCenter,
                    pawnTypeFilter != PlayerPawnTypeFilter.All,
                    "TSA_WD_FilterByType".Translate(),
                    icon => PawnRosterHeaderFilter.OpenChoiceDropdown(
                        icon,
                        "TSA_WD_FilterByType".Translate(),
                        PawnRosterHeaderFilter.TypeChoices(pawnTypeFilter, f => pawnTypeFilter = f,
                            TypePopulationForFilter())),
                    () => SetPawnSort("PawnType"));
            }

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

            if (ColOn(PawnRosterColumnIds.Star))
            {
                PawnRosterHeaderFilter.DrawFilterableHeader(
                    ref curX, headerRect.y, ColStar, HeaderHeight,
                    "",
                    pawnSortColumn == "Starred", pawnSortAscending,
                    TextAnchor.MiddleCenter,
                    starFilter != OutpostPawnStarFilter.All,
                    _starHeaderTip,
                    icon => PawnRosterHeaderFilter.OpenChoiceDropdown(
                        icon,
                        "TSA_WD_FilterByStar".Translate(),
                        PawnRosterHeaderFilter.OutpostStarChoices(starFilter, f => starFilter = f,
                            StarPopulationForFilter())),
                    () => SetPawnSort("Starred"));
            }

            if (ColOn(PawnRosterColumnIds.New))
            {
                PawnRosterHeaderFilter.DrawFilterableHeader(
                    ref curX, headerRect.y, ColNew, HeaderHeight,
                    null,
                    pawnSortColumn == "New", pawnSortAscending,
                    TextAnchor.MiddleCenter,
                    joinedFilter != PawnRosterJoinedFilter.All,
                    "TSA_WD_FilterByNew".Translate(),
                    icon => PawnRosterHeaderFilter.OpenChoiceDropdown(
                        icon,
                        "TSA_WD_FilterByNew".Translate(),
                        PawnRosterHeaderFilter.JoinedFilterChoices(joinedFilter, f => joinedFilter = f,
                            JoinDaysPopulationForFilter()),
                        width: 300f),
                    () => SetPawnSort("New"),
                    PawnRosterHeaderFilter.JoinStampHeaderIcon,
                    _joinStampHeaderTip);
            }

            if (ColOn(PawnRosterColumnIds.Age))
            {
                Rect ageHdr = new Rect(curX, headerRect.y, ColAge, HeaderHeight);
                DrawPawnSortHeader(ref curX, headerRect, ColAge, "TSA_WD_PawnRoster_ColAge".Translate(), "Age");
                TooltipHandler.TipRegion(ageHdr, "TSA_WD_PawnRoster_ColAgeTip".Translate());
            }

            if (ColOn(PawnRosterColumnIds.Traits))
            {
                PawnRosterTraitFilter.DrawTraitsHeader(
                    ref curX, headerRect.y, ColTraits, HeaderHeight,
                    "TSA_WD_Prisoners_ColTraits".Translate(),
                    pawnSortColumn == "Traits", pawnSortAscending,
                    TextAnchor.MiddleCenter,
                    () => SetPawnSort("Traits"));
            }

            if (ColOn(PawnRosterColumnIds.Xenotype))
            {
                PawnRosterHeaderFilter.DrawFilterableHeader(
                    ref curX, headerRect.y, ColXenotype, HeaderHeight,
                    "TSA_WD_PawnRoster_ColXenotype".Translate(),
                    pawnSortColumn == "Xenotype", pawnSortAscending,
                    TextAnchor.MiddleCenter,
                    !xenotypeFilter.NullOrEmpty(),
                    "TSA_WD_FilterByXenotype".Translate(),
                    icon => PawnRosterHeaderFilter.OpenChoiceDropdown(
                        icon,
                        "TSA_WD_FilterByXenotype".Translate(),
                        PawnRosterHeaderFilter.XenotypeChoices(xenotypeFilter, v => xenotypeFilter = v ?? "",
                            XenotypePopulationForFilter())),
                    () => SetPawnSort("Xenotype"));
            }

            if (ColOn(PawnRosterColumnIds.Psycasts))
            {
                PawnRosterHeaderFilter.DrawFilterableHeader(
                    ref curX, headerRect.y, ColPsycasts, HeaderHeight,
                    "TSA_WD_PawnRoster_ColPsycasts".Translate(),
                    pawnSortColumn == "Psycasts", pawnSortAscending,
                    TextAnchor.MiddleCenter,
                    !psycastFilter.NullOrEmpty(),
                    "TSA_WD_FilterByPsycast".Translate(),
                    icon => PawnRosterHeaderFilter.OpenChoiceDropdown(
                        icon,
                        "TSA_WD_FilterByPsycast".Translate(),
                        PawnRosterHeaderFilter.PsycastChoices(psycastFilter, v => psycastFilter = v ?? "",
                            PsycastPopulationForFilter())),
                    () => SetPawnSort("Psycasts"));
            }

            if (ColOn(PawnRosterColumnIds.Ideology))
            {
                PawnRosterHeaderFilter.DrawFilterableHeader(
                    ref curX, headerRect.y, ColIdeology, HeaderHeight,
                    "TSA_WD_PawnRoster_ColIdeology".Translate(),
                    pawnSortColumn == "Ideology", pawnSortAscending,
                    TextAnchor.MiddleCenter,
                    !ideoFilter.NullOrEmpty(),
                    "TSA_WD_FilterByIdeology".Translate(),
                    icon => PawnRosterHeaderFilter.OpenChoiceDropdown(
                        icon,
                        "TSA_WD_FilterByIdeology".Translate(),
                        PawnRosterHeaderFilter.IdeoChoices(ideoFilter, v =>
                        {
                            ideoFilter = v ?? "";
                            rowsDirty = true;
                        }, IdeoPopulationForFilter())),
                    () => SetPawnSort("Ideology"));
            }

            SkillDef[] skills = PlayerPawnRosterUtility.AllSkillColumns;
            for (int i = 0; i < skills.Length; i++)
            {
                if (!ColOn(PawnRosterColumnIds.Skill(skills[i]))) continue;
                DrawPawnSortHeader(ref curX, headerRect, ColSkill, skills[i].LabelCap, skills[i].defName);
            }

            // Health after skills so the default Shooting/Melee/Health set matches the prior Armory order.
            if (ColOn(PawnRosterColumnIds.Health))
            {
                Rect healthHdr = new Rect(curX, headerRect.y, ColHealth, HeaderHeight);
                DrawPawnSortHeader(ref curX, headerRect, ColHealth, "TSA_WD_PawnRoster_ColHealth".Translate(), "Health");
                TooltipHandler.TipRegion(healthHdr, "TSA_WD_PawnRoster_ColHealthTip".Translate());
            }

            if (ColOn(PawnRosterColumnIds.Mass))
            {
                Rect massHdr = new Rect(curX, headerRect.y, ColMass, HeaderHeight);
                DrawPawnSortHeader(ref curX, headerRect, ColMass, "TSA_WD_Armory_ColMass".Translate(), "Mass");
                TooltipHandler.TipRegion(massHdr, "TSA_WD_Armory_ColMassTip".Translate());
            }

            if (ColOn(PawnRosterColumnIds.Bulk))
            {
                Rect bulkHdr = new Rect(curX, headerRect.y, ColBulk, HeaderHeight);
                DrawPawnSortHeader(ref curX, headerRect, ColBulk, "TSA_WD_Armory_ColBulk".Translate(), "Bulk");
                TooltipHandler.TipRegion(bulkHdr, "TSA_WD_Armory_ColBulkTip".Translate());
            }

            GUI.color = Color.white;
            DrawGearSectionBorder(curX, headerRect.y, HeaderHeight);
            DrawStaticSectionHeader(ref curX, headerRect, apparelW, "TSA_WD_Armory_SecApparel".Translate());
            DrawGearSectionBorder(curX, headerRect.y, HeaderHeight);
            DrawStaticSectionHeader(ref curX, headerRect, weaponsW, "TSA_WD_Armory_SecWeapons".Translate());
            DrawGearSectionBorder(curX, headerRect.y, HeaderHeight);
            DrawStaticSectionHeader(ref curX, headerRect, consumablesW, "TSA_WD_Armory_SecFoodDrugsAmmo".Translate());
            if (showOtherInv)
            {
                DrawGearSectionBorder(curX, headerRect.y, HeaderHeight);
                DrawStaticSectionHeader(ref curX, headerRect, otherInvW, "TSA_WD_Armory_SecOtherInventory".Translate());
            }
            Widgets.EndGroup();

            Text.Font = GameFont.Tiny;
            Widgets.DrawLineHorizontal(rect.x, headerClip.yMax, rect.width);

            Rect listRect = new Rect(rect.x, rect.y + headerOffset, rect.width, listH);

            if (visiblePawns.Count == 0)
            {
                string empty = outpost.Occupants == null || outpost.Occupants.Count == 0
                    ? "TSA_WD_Armory_NoOccupants".Translate()
                    : "TSA_WD_Armory_NoPawnMatch".Translate();
                Widgets.NoneLabelCenteredVertically(listRect, empty);
                return;
            }

            float viewH = visiblePawns.Count * rowH + 8f;
            BeginArmoryScrollView(listRect, ref pawnScroll, contentW, viewH, out Rect viewRect);

            float y = 0f;
            for (int i = 0; i < visiblePawns.Count; i++)
            {
                DrawPawnRow(new Rect(0f, y, viewRect.width, rowH), visiblePawns[i], i % 2 == 0,
                    apparelW, weaponsW, consumablesW, otherInvW, showOtherInv);
                y += rowH;
            }

            EndArmoryScrollView(listRect, ref pawnScroll, contentW, viewH);
        }

        /// <summary>
        /// Scroll content lives in a rect that excludes scrollbar gutters; bars are drawn in those
        /// gutters afterward so row drag hits cannot steal the scrollbar gesture.
        /// </summary>
        private static void BeginArmoryScrollView(
            Rect listRect, ref Vector2 scroll, float contentW, float contentH, out Rect viewRect)
        {
            ResolveScrollBars(listRect, contentW, contentH, out bool hBar, out bool vBar, out Rect scrollOuter);
            viewRect = new Rect(0f, 0f,
                Mathf.Max(contentW, scrollOuter.width),
                Mathf.Max(contentH, scrollOuter.height));

            // Block gutters in window space before any nested scroll-group mouse mapping.
            if (hBar)
            {
                WdItemDragDrop.BlockInput(new Rect(
                    listRect.x, listRect.yMax - ScrollbarSize, scrollOuter.width, ScrollbarSize));
            }
            if (vBar)
            {
                WdItemDragDrop.BlockInput(new Rect(
                    listRect.xMax - ScrollbarSize, listRect.y, ScrollbarSize, scrollOuter.height));
            }

            // GUI false/false still draws bars when content overflows; style.none hides them entirely.
            scroll = GUI.BeginScrollView(scrollOuter, scroll, viewRect, GUIStyle.none, GUIStyle.none);
        }

        private static void EndArmoryScrollView(
            Rect listRect, ref Vector2 scroll, float contentW, float contentH)
        {
            GUI.EndScrollView();
            ResolveScrollBars(listRect, contentW, contentH, out bool hBar, out bool vBar, out Rect scrollOuter);

            if (hBar)
            {
                Rect hRect = new Rect(listRect.x, listRect.yMax - ScrollbarSize, scrollOuter.width, ScrollbarSize);
                float viewW = Mathf.Max(contentW, scrollOuter.width);
                scroll.x = GUI.HorizontalScrollbar(hRect, scroll.x, scrollOuter.width, 0f, viewW);
            }

            if (vBar)
            {
                Rect vRect = new Rect(listRect.xMax - ScrollbarSize, listRect.y, ScrollbarSize, scrollOuter.height);
                float viewH = Mathf.Max(contentH, scrollOuter.height);
                scroll.y = GUI.VerticalScrollbar(vRect, scroll.y, scrollOuter.height, 0f, viewH);
            }
        }

        private static void ResolveScrollBars(
            Rect listRect, float contentW, float contentH,
            out bool hBar, out bool vBar, out Rect scrollOuter)
        {
            vBar = contentH > listRect.height;
            hBar = contentW > listRect.width - (vBar ? ScrollbarSize : 0f);
            vBar = contentH > listRect.height - (hBar ? ScrollbarSize : 0f);
            hBar = contentW > listRect.width - (vBar ? ScrollbarSize : 0f);

            scrollOuter = new Rect(
                listRect.x,
                listRect.y,
                listRect.width - (vBar ? ScrollbarSize : 0f),
                listRect.height - (hBar ? ScrollbarSize : 0f));
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

        private List<PlayerPawnSortCategory> TypePopulationForFilter()
        {
            var list = new List<PlayerPawnSortCategory>();
            List<Pawn> occ = outpost.Occupants;
            if (occ == null) return list;
            for (int i = 0; i < occ.Count; i++)
            {
                if (occ[i] != null)
                    list.Add(PlayerPawnRosterUtility.ClassifyPawn(occ[i]));
            }
            return list;
        }

        private List<bool> StarPopulationForFilter()
        {
            var list = new List<bool>();
            List<Pawn> occ = outpost.Occupants;
            if (occ == null) return list;
            var fav = WorldComponent_PlayerPawnFavorites.Get();
            for (int i = 0; i < occ.Count; i++)
            {
                Pawn p = occ[i];
                if (p == null) continue;
                list.Add(fav?.IsStarred(p.ThingID) == true);
            }
            return list;
        }

        private List<int> JoinDaysPopulationForFilter()
        {
            var list = new List<int>();
            List<Pawn> occ = outpost.Occupants;
            if (occ == null) return list;
            for (int i = 0; i < occ.Count; i++)
            {
                if (occ[i] != null)
                    list.Add(PlayerPawnRosterUtility.GetDaysSinceJoin(occ[i]));
            }
            return list;
        }

        private List<string> XenotypePopulationForFilter()
        {
            var list = new List<string>();
            List<Pawn> occ = outpost.Occupants;
            if (occ == null) return list;
            for (int i = 0; i < occ.Count; i++)
                list.Add(PawnRosterHeaderFilter.XenotypeKey(occ[i]));
            return list;
        }

        private List<List<string>> PsycastPopulationForFilter()
        {
            var list = new List<List<string>>();
            List<Pawn> occ = outpost.Occupants;
            if (occ == null) return list;
            for (int i = 0; i < occ.Count; i++)
                list.Add(PawnRosterHeaderFilter.PsycastKeysOnPawn(occ[i]));
            return list;
        }

        private List<string> IdeoPopulationForFilter()
        {
            var list = new List<string>();
            List<Pawn> occ = outpost.Occupants;
            if (occ == null) return list;
            for (int i = 0; i < occ.Count; i++)
                list.Add(PawnRosterHeaderFilter.IdeoKey(occ[i]));
            return list;
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

            // Size columns to the width left after a vertical scrollbar, so a phantom horizontal
            // bar does not appear under the last row when the list merely scrolls vertically.
            float contentH = visibleStoreRows.Count > 0
                ? visibleStoreRows.Count * StoreRowH + 8f
                : 0f;
            bool needVScroll = contentH > listH;
            float usableW = rect.width - (needVScroll ? ScrollbarSize : 0f);
            float itemW = Mathf.Max(100f, usableW - catW - qualityW - countW - 4f);

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

            Text.Font = GameFont.Tiny;
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

            float contentW = itemW + catW + qualityW + countW;
            BeginArmoryScrollView(listRect, ref storeScroll, contentW, contentH, out _);

            float y = 0f;
            for (int i = 0; i < visibleStoreRows.Count; i++)
            {
                DrawStoreRow(new Rect(0f, y, contentW, StoreRowH), visibleStoreRows[i], i % 2 == 0,
                    itemW, catW, qualityW, countW);
                y += StoreRowH;
            }

            EndArmoryScrollView(listRect, ref storeScroll, contentW, contentH);
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
            bool traitFilter = PawnRosterTraitFilter.FilterApplies(ColWindow);
            var fav = WorldComponent_PlayerPawnFavorites.Get();
            if (occupants != null)
            {
                for (int i = 0; i < occupants.Count; i++)
                {
                    Pawn p = occupants[i];
                    if (p == null) continue;
                    if (pawnFilter != null && (p.LabelShortCap ?? "").ToLowerInvariant().IndexOf(pawnFilter, StringComparison.Ordinal) < 0)
                        continue;
                    if (pawnTypeFilter != PlayerPawnTypeFilter.All)
                    {
                        PlayerPawnSortCategory cat = PlayerPawnRosterUtility.ClassifyPawn(p);
                        if (cat != PlayerPawnRosterUtility.ToSortCategory(pawnTypeFilter))
                            continue;
                    }
                    if (starFilter != OutpostPawnStarFilter.All)
                    {
                        bool starred = fav?.IsStarred(p.ThingID) == true;
                        if (starFilter == OutpostPawnStarFilter.Starred && !starred) continue;
                        if (starFilter == OutpostPawnStarFilter.NotStarred && starred) continue;
                    }
                    if (joinedFilter != PawnRosterJoinedFilter.All
                        && !PlayerPawnRosterUtility.PassesJoinedFilter(
                            PlayerPawnRosterUtility.GetDaysSinceJoin(p), joinedFilter))
                        continue;
                    if (traitFilter && !PawnRosterTraitFilter.Matches(p)) continue;
                    if (ColOn(PawnRosterColumnIds.Xenotype)
                        && !xenotypeFilter.NullOrEmpty()
                        && !PawnRosterTraitFilter.MatchesXenotype(p, xenotypeFilter))
                        continue;
                    if (ColOn(PawnRosterColumnIds.Psycasts)
                        && !psycastFilter.NullOrEmpty()
                        && !PawnRosterTraitFilter.MatchesPsycast(p, psycastFilter))
                        continue;
                    if (ColOn(PawnRosterColumnIds.Ideology)
                        && !ideoFilter.NullOrEmpty()
                        && !PawnRosterTraitFilter.MatchesIdeology(p, ideoFilter))
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
                int cmp = ComparePawns(a, b, pawnSortColumn);
                if (cmp == 0)
                    cmp = string.Compare(a?.LabelShortCap, b?.LabelShortCap, StringComparison.OrdinalIgnoreCase);
                return pawnSortAscending ? cmp : -cmp;
            });
        }

        private int ComparePawns(Pawn a, Pawn b, string sortColumn)
        {
            switch (sortColumn)
            {
                case "PawnType":
                    return string.Compare(
                        PlayerPawnRosterUtility.GetPawnTypeLabel(PlayerPawnRosterUtility.ClassifyPawn(a)),
                        PlayerPawnRosterUtility.GetPawnTypeLabel(PlayerPawnRosterUtility.ClassifyPawn(b)),
                        StringComparison.OrdinalIgnoreCase);
                case "Starred":
                {
                    var fav = WorldComponent_PlayerPawnFavorites.Get();
                    int as_ = fav?.IsStarred(a?.ThingID) == true ? 1 : 0;
                    int bs = fav?.IsStarred(b?.ThingID) == true ? 1 : 0;
                    return as_.CompareTo(bs);
                }
                case "New":
                    return PlayerPawnRosterUtility.GetDaysSinceJoin(a)
                        .CompareTo(PlayerPawnRosterUtility.GetDaysSinceJoin(b));
                case "Age":
                    return (a?.ageTracker?.AgeBiologicalYears ?? 0)
                        .CompareTo(b?.ageTracker?.AgeBiologicalYears ?? 0);
                case "Health":
                    return OutpostStrengthBudgetUi.GetHealthPercent(a)
                        .CompareTo(OutpostStrengthBudgetUi.GetHealthPercent(b));
                case "Traits":
                {
                    PrisonerRosterUtility.FormatTraits(a, out _, out string ta);
                    PrisonerRosterUtility.FormatTraits(b, out _, out string tb);
                    return string.Compare(ta, tb, StringComparison.OrdinalIgnoreCase);
                }
                case "Xenotype":
                    return PawnRosterTraitFilter.CompareXenotype(a, b);
                case "Psycasts":
                    return PawnRosterTraitFilter.ComparePsycasts(a, b);
                case "Ideology":
                    return PawnRosterTraitFilter.CompareIdeology(a, b);
                case "Mass":
                    return GetMassCurrent(a).CompareTo(GetMassCurrent(b));
                case "Bulk":
                    return GetBulkCurrent(a).CompareTo(GetBulkCurrent(b));
                case "Name":
                    return string.Compare(a?.LabelShortCap, b?.LabelShortCap, StringComparison.OrdinalIgnoreCase);
                default:
                {
                    SkillDef skill = DefDatabase<SkillDef>.GetNamedSilentFail(sortColumn);
                    if (skill != null)
                    {
                        return OutpostStrengthBudgetUi.GetSkillLevel(a, skill)
                            .CompareTo(OutpostStrengthBudgetUi.GetSkillLevel(b, skill));
                    }
                    return string.Compare(a?.LabelShortCap, b?.LabelShortCap, StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        private static bool MatchesQualityFilter(StoreRow r)
        {
            if (qualityFilter == QualityFilterAll) return true;
            if (qualityFilter == QualityFilterNone) return !r.HasQuality;
            return r.HasQuality && (int)r.Quality == qualityFilter;
        }

        private static int QualitySortKey(StoreRow r) =>
            r.HasQuality ? (int)r.Quality : -1;

        /// <summary>Drop zone on a pawn row: equip columns vs inventory columns.</summary>
        private sealed class ArmoryPawnDropTarget
        {
            public readonly Pawn Pawn;
            public readonly bool ToInventory;

            public ArmoryPawnDropTarget(Pawn pawn, bool toInventory)
            {
                Pawn = pawn;
                ToInventory = toInventory;
            }
        }

        private void DrawPawnRow(Rect rect, Pawn pawn, bool zebra,
            float apparelW, float weaponsW, float consumablesW, float otherInvW, bool showOtherInv)
        {
            if (zebra) Widgets.DrawHighlight(rect);
            if (Mouse.IsOver(rect)) Widgets.DrawLightHighlight(rect);
            // Default: prefer equip when dropping on portrait/name/skills; gear sections override below.
            RegisterPawnDrop(rect, pawn, toInventory: false);

            Text.Font = GameFont.Tiny;
            float rowH = rect.height;
            float x = rect.x;

            Rect portraitCell = new Rect(x, rect.y, ColPortrait, rowH);
            Texture portraitTex = PawnPortraitUIUtils.GetPortrait(pawn, PortraitSize);
            Rect portraitRect = new Rect(
                portraitCell.x + (portraitCell.width - PortraitSize.x) / 2f,
                rect.y + (rowH - PortraitSize.y) / 2f,
                PortraitSize.x,
                PortraitSize.y);
            if (portraitTex != null)
                GUI.DrawTexture(portraitRect, portraitTex, ScaleMode.ScaleToFit);
            else
                Widgets.DrawBoxSolid(portraitRect, new Color(0.3f, 0.3f, 0.35f, 1f));
            if (Widgets.ButtonInvisible(portraitCell))
                Find.WindowStack.Add(new Dialog_InfoCard(pawn));
            x += ColPortrait;

            if (ColOn(PawnRosterColumnIds.Type))
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                string typeLabel = PlayerPawnRosterUtility.GetPawnTypeLabel(
                    PlayerPawnRosterUtility.ClassifyPawn(pawn));
                Widgets.Label(new Rect(x, rect.y, ColPawnType, rowH), typeLabel.Truncate(ColPawnType - 4f));
                x += ColPawnType;
            }

            Rect nameCell = new Rect(x, rect.y, ColPawnName, rowH);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(nameCell, pawn.LabelShortCap.Truncate(ColPawnName - 4f));
            Text.Anchor = TextAnchor.UpperLeft;
            if (Widgets.ButtonInvisible(nameCell))
                Find.WindowStack.Add(new Dialog_InfoCard(pawn));
            x += ColPawnName;

            if (ColOn(PawnRosterColumnIds.Star))
            {
                EnsureStarHeaderTip();
                bool starred = WorldComponent_PlayerPawnFavorites.Get()?.IsStarred(pawn.ThingID) == true;
                Rect starCell = new Rect(x, rect.y, ColStar, rowH);
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = starred ? new Color(1f, 0.85f, 0.2f) : new Color(0.55f, 0.55f, 0.55f, 0.7f);
                Widgets.Label(starCell, starred ? "★" : "☆");
                GUI.color = Color.white;
                TooltipHandler.TipRegion(starCell, _starHeaderTip);
                if (Widgets.ButtonInvisible(starCell))
                {
                    WorldComponent_PlayerPawnFavorites.Get()?.Toggle(pawn.ThingID);
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }
                x += ColStar;
            }

            if (ColOn(PawnRosterColumnIds.New))
            {
                int days = PlayerPawnRosterUtility.GetDaysSinceJoin(pawn);
                Rect newCell = new Rect(x, rect.y, ColNew, rowH);
                Text.Anchor = TextAnchor.MiddleCenter;
                if (days >= 0)
                {
                    if (PlayerPawnRosterUtility.IsJoinStampRecent(days))
                        GUI.color = new Color(0.45f, 0.85f, 0.55f);
                    Widgets.Label(newCell, days.ToString());
                    GUI.color = Color.white;
                }
                x += ColNew;
            }

            if (ColOn(PawnRosterColumnIds.Age))
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                int age = pawn.ageTracker?.AgeBiologicalYears ?? 0;
                Widgets.Label(new Rect(x, rect.y, ColAge, rowH), age.ToString());
                x += ColAge;
            }

            if (ColOn(PawnRosterColumnIds.Traits))
            {
                PrisonerRosterUtility.FormatTraits(pawn, out string traitsDisplay, out string traitsTip);
                Rect traitsRect = new Rect(x + 2f, rect.y + 2f, ColTraits - 4f, rowH - 4f);
                PrisonerRosterUtility.DrawTraitsCell(traitsRect, traitsDisplay, traitsTip);
                x += ColTraits;
            }

            if (ColOn(PawnRosterColumnIds.Xenotype))
            {
                PawnRosterTraitFilter.FormatXenotype(pawn, out string xDisplay, out string xTip);
                Rect cell = new Rect(x + 2f, rect.y + 2f, ColXenotype - 4f, rowH - 4f);
                PrisonerRosterUtility.DrawTraitsCell(cell, xDisplay, xTip);
                x += ColXenotype;
            }

            if (ColOn(PawnRosterColumnIds.Psycasts))
            {
                PawnRosterTraitFilter.FormatPsycasts(pawn, out string pDisplay, out string pTip);
                Rect cell = new Rect(x + 2f, rect.y + 2f, ColPsycasts - 4f, rowH - 4f);
                PrisonerRosterUtility.DrawTraitsCell(cell, pDisplay, pTip);
                x += ColPsycasts;
            }

            if (ColOn(PawnRosterColumnIds.Ideology))
            {
                PawnRosterTraitFilter.FormatIdeology(pawn, out string iDisplay, out string iTip);
                Rect cell = new Rect(x + 2f, rect.y + 2f, ColIdeology - 4f, rowH - 4f);
                PrisonerRosterUtility.DrawTraitsCell(cell, iDisplay, iTip);
                x += ColIdeology;
            }

            Text.Anchor = TextAnchor.MiddleLeft;
            SkillDef[] skills = PlayerPawnRosterUtility.AllSkillColumns;
            int best = 0;
            for (int i = 0; i < skills.Length; i++)
            {
                if (!ColOn(PawnRosterColumnIds.Skill(skills[i]))) continue;
                int lvl = OutpostStrengthBudgetUi.GetSkillLevel(pawn, skills[i]);
                if (lvl > best) best = lvl;
            }
            for (int i = 0; i < skills.Length; i++)
            {
                SkillDef skill = skills[i];
                if (!ColOn(PawnRosterColumnIds.Skill(skill))) continue;
                int level = OutpostStrengthBudgetUi.GetSkillLevel(pawn, skill);
                DrawSkillCell(new Rect(x, rect.y, ColSkill, rowH), pawn, skill, level, best > 0 && level >= best);
                x += ColSkill;
            }

            if (ColOn(PawnRosterColumnIds.Health))
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                float hp = OutpostStrengthBudgetUi.GetHealthPercent(pawn);
                Widgets.Label(new Rect(x, rect.y, ColHealth, rowH), Mathf.RoundToInt(hp).ToString() + "%");
                x += ColHealth;
            }

            if (ColOn(PawnRosterColumnIds.Mass) || ColOn(PawnRosterColumnIds.Bulk))
            {
                if (!pawnCarryLoad.TryGetValue(pawn, out CarryLoadCache load))
                    load = BuildCarryLoad(pawn);
                if (ColOn(PawnRosterColumnIds.Mass))
                {
                    Text.Anchor = TextAnchor.MiddleCenter;
                    string massText = FormatCarryPair(load.MassCur, load.MassCap);
                    Rect massCell = new Rect(x, rect.y, ColMass, rowH);
                    GUI.color = CarryLoadTextColor(load.MassCur, load.MassCap);
                    Widgets.Label(massCell, massText.Truncate(ColMass - 4f));
                    GUI.color = Color.white;
                    TooltipHandler.TipRegion(massCell, "TSA_WD_Armory_ColMassTip".Translate());
                    x += ColMass;
                }
                if (ColOn(PawnRosterColumnIds.Bulk) && load.HasBulk)
                {
                    Text.Anchor = TextAnchor.MiddleCenter;
                    string bulkText = FormatCarryPair(load.BulkCur, load.BulkCap);
                    Rect bulkCell = new Rect(x, rect.y, ColBulk, rowH);
                    GUI.color = CarryLoadTextColor(load.BulkCur, load.BulkCap);
                    Widgets.Label(bulkCell, bulkText.Truncate(ColBulk - 4f));
                    GUI.color = Color.white;
                    TooltipHandler.TipRegion(bulkCell, "TSA_WD_Armory_ColBulkTip".Translate());
                    x += ColBulk;
                }
                else if (ColOn(PawnRosterColumnIds.Bulk))
                    x += ColBulk;
            }
            Text.Anchor = TextAnchor.UpperLeft;

            float gearBlockH = GearIconSize * 2f + GearIconGap;
            float gearY = rect.y + Mathf.Max(0f, (rowH - gearBlockH) * 0.5f);

            Rect apparelCol = new Rect(x, rect.y, apparelW, rowH);
            RegisterPawnDrop(apparelCol, pawn, toInventory: false);
            DrawGearSectionBorder(x, rect.y, rowH);
            DrawPawnSectionIcons(pawn, new Rect(x + GearSectionInnerPad, gearY, apparelW - GearSectionGap - GearSectionInnerPad, gearBlockH), PawnGearSection.EquippedApparel);
            x += apparelW;

            Rect weaponsCol = new Rect(x, rect.y, weaponsW, rowH);
            RegisterPawnDrop(weaponsCol, pawn, toInventory: false);
            DrawGearSectionBorder(x, rect.y, rowH);
            DrawPawnSectionIcons(pawn, new Rect(x + GearSectionInnerPad, gearY, weaponsW - GearSectionGap - GearSectionInnerPad, gearBlockH), PawnGearSection.EquippedWeapons);
            x += weaponsW;

            Rect consumablesCol = new Rect(x, rect.y, consumablesW, rowH);
            RegisterPawnDrop(consumablesCol, pawn, toInventory: true);
            DrawGearSectionBorder(x, rect.y, rowH);
            DrawPawnSectionIcons(pawn, new Rect(x + GearSectionInnerPad, gearY, consumablesW - GearSectionGap - GearSectionInnerPad, gearBlockH), PawnGearSection.FoodDrugsAmmoMed);
            x += consumablesW;

            if (showOtherInv)
            {
                Rect otherCol = new Rect(x, rect.y, otherInvW, rowH);
                RegisterPawnDrop(otherCol, pawn, toInventory: true);
                DrawGearSectionBorder(x, rect.y, rowH);
                DrawPawnSectionIcons(pawn, new Rect(x + GearSectionInnerPad, gearY, otherInvW - GearSectionGap - GearSectionInnerPad, gearBlockH), PawnGearSection.OtherInventory);
            }
        }

        private void RegisterPawnDrop(Rect rect, Pawn pawn, bool toInventory)
        {
            if (pawn == null) return;
            bool valid = true;
            if (WdItemDragDrop.DragActive && !toInventory)
            {
                ThingDef def = null;
                if (WdItemDragDrop.Payload is Thing t) def = t.def;
                else if (WdItemDragDrop.Payload is ThingDefCountClass row) def = row.thingDef;
                valid = OutpostArmoryUtility.IsEquipSlotDef(def);
            }
            WdItemDragDrop.RegisterDropTarget(rect, new ArmoryPawnDropTarget(pawn, toInventory), valid);
        }

        private static void DrawSkillCell(Rect cell, Pawn pawn, SkillDef skill, int level, bool isBest)
        {
            PlayerPawnRosterUtility.DrawSkillLevelWithPassion(cell, pawn, skill, level, isBest, ColWindow);
        }

        private enum PawnGearSection : byte
        {
            EquippedApparel,
            EquippedWeapons,
            FoodDrugsAmmoMed,
            OtherInventory
        }

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

            if (section == PawnGearSection.EquippedApparel)
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

            if (section == PawnGearSection.EquippedWeapons)
            {
                List<ThingWithComps> eq = pawn.equipment?.AllEquipmentListForReading;
                if (eq == null) return;
                for (int i = 0; i < eq.Count; i++)
                {
                    ThingWithComps t = eq[i];
                    if (t == null) continue;
                    if (x + GearIconSize > xMax + 0.01f && onSecondRow) break;
                    if (x + GearIconSize > xMax + 0.01f)
                    {
                        onSecondRow = true;
                        x = bounds.x;
                        y = row1Y;
                    }
                    DrawDraggableThing(t, pawn, x, y);
                    if (!Advance(ref x, ref y, ref onSecondRow)) break;
                }
                return;
            }

            // Carried pack slices (always shown; not column-picker prefs).
            if (!pawnInventory.TryGetValue(pawn, out List<ThingDefCountClass> pack) || pack.Count == 0)
                return;

            bool consumables = section == PawnGearSection.FoodDrugsAmmoMed;
            var ordered = new List<(Thing thing, ThingDefCountClass stack)>();
            for (int i = 0; i < pack.Count; i++)
            {
                ThingDefCountClass e = pack[i];
                if (e?.thingDef == null || e.count <= 0) continue;
                bool isConsumable = IsFoodDrugsAmmoOrMedicine(e.thingDef);
                if (consumables != isConsumable) continue;
                Thing carried = FindInventoryThing(pawn, e.thingDef);
                if (carried == null) continue;
                ordered.Add((carried, e));
            }
            if (consumables)
                ordered.Sort((a, b) => ConsumableDisplayOrder(a.thing?.def).CompareTo(ConsumableDisplayOrder(b.thing?.def)));
            else
                ordered.Sort((a, b) => OtherInventoryDisplayOrder(a.thing?.def).CompareTo(OtherInventoryDisplayOrder(b.thing?.def)));

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
                DrawDraggableInventoryIcon(entry.thing, pawn, entry.stack, x, y);
                if (!Advance(ref x, ref y, ref onSecondRow)) break;
            }
        }

        /// <summary>Food, then drugs, medicine, then ammo.</summary>
        private static int ConsumableDisplayOrder(ThingDef def)
        {
            if (def == null) return 99;
            if (def.ingestible != null && def.IsNutritionGivingIngestible && !def.IsDrug) return 0;
            if (def.IsDrug) return 1;
            if (def.IsMedicine) return 2;
            if (OutpostArmoryUtility.IsAmmoDef(def)) return 3;
            return 4;
        }

        /// <summary>Apparel, then weapons/grenades, then everything else.</summary>
        private static int OtherInventoryDisplayOrder(ThingDef def)
        {
            if (def == null) return 99;
            if (def.IsApparel) return 0;
            if (def.IsWeapon) return 10 + WeaponDisplayOrder(def);
            return 50;
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

        private static bool IsFoodDrugsAmmoOrMedicine(ThingDef def)
        {
            if (def == null) return false;
            if (OutpostArmoryUtility.IsAmmoDef(def)) return true;
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

            if (TryRightClickStore(r, pawn, carried))
                return;

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

            if (TryRightClickStore(r, owner, thing))
                return;

            if (WdItemDragDrop.DraggableIcon(r, thing, owner, tip, thing.def.uiIcon))
                OpenItemInfo(thing);
        }

        /// <summary>Right-click moves the full stack to the Armory (or asks to destroy if not storable).</summary>
        private bool TryRightClickStore(Rect r, Pawn pawn, Thing thing)
        {
            if (thing == null || thing.Destroyed || pawn == null) return false;
            if (!Mouse.IsOver(r)) return false;
            if (Event.current.type != EventType.MouseDown || Event.current.button != 1)
                return false;

            Event.current.Use();
            int count = thing.stackCount > 0 ? thing.stackCount : 1;
            if (!OutpostStorageUtility.CanStoreRow(outpost, thing.def))
                ConfirmDestroyFromPawn(pawn, thing, count);
            else
                StoreFromPawn(pawn, thing, count);
            return true;
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
        }

        /// <summary>Icon tooltip; tainted apparel also gets a yellow outline and a tainted line.</summary>
        private static string GearIconTip(Rect r, Thing thing, ThingDef def, int count)
        {
            string tip = CountedTip(OutpostArmoryUtility.DisplayLabel(thing), def, count);
            tip += "\n" + "TSA_WD_Armory_RightClickStoreTip".Translate();
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

            Text.Font = GameFont.Tiny;
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

            // Drag only the icon+name strip so Type/Count/Quality and scrollbars stay free.
            Rect dragRect = new Rect(iconRect.x, rect.y, Mathf.Max(0f, labelRect.xMax - iconRect.x), rect.height);
            object payload = (object)row.Unique ?? row.Row;
            if (WdItemDragDrop.DraggableIcon(dragRect, payload, this, row.Label, row.Def?.uiIcon))
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

            // Store -> pawn (equip vs inventory depends on which column was hovered)
            if (ReferenceEquals(source, this) && target is ArmoryPawnDropTarget storeDrop)
            {
                GiveToPawn(storeDrop.Pawn, payload, storeDrop.ToInventory);
                return;
            }

            // Pawn -> pawn (or same pawn equip ↔ inventory), column decides equip vs inventory.
            if (source is Pawn srcPawn && target is ArmoryPawnDropTarget dstDrop && payload is Thing moved)
            {
                BeginPawnThingMove(srcPawn, dstDrop.Pawn, moved, dstDrop.ToInventory);
            }
        }

        private void BeginPawnThingMove(Pawn src, Pawn dst, Thing thing, bool toInventory)
        {
            if (src == null || dst == null || thing == null || thing.Destroyed) return;

            if (!toInventory && !OutpostArmoryUtility.IsEquipSlotDef(thing.def))
            {
                pendingMessage = "TSA_WD_Armory_FailNotEquippable".Translate(thing.LabelCap);
                return;
            }

            // Same zone no-op: already equipped dropped on equip, or already in pack dropped on inventory.
            bool fromInventory = src.inventory?.innerContainer != null
                && src.inventory.innerContainer.Contains(thing);
            if (src == dst && fromInventory == toInventory)
                return;

            int available = thing.stackCount > 0 ? thing.stackCount : 1;
            void Finish(int count)
            {
                if (count <= 0) return;
                if (!OutpostArmoryUtility.TryMoveThingToPawn(
                        outpost, src, dst, thing, count, toInventory, out string fail))
                {
                    if (!string.IsNullOrEmpty(fail)) pendingMessage = fail;
                    return;
                }
                NotifyChanged();
            }

            if (!toInventory || available <= 1)
                Finish(toInventory ? available : Math.Min(1, available));
            else
                PromptCount(thing.def.LabelCap, available, Finish);
        }

        /// <summary>
        /// Ask for a count whenever more than one item can leave the pawn. Ammo and food stacks
        /// both need this; drop deferral would otherwise lose Shift. Non-storable defs ask to destroy.
        /// </summary>
        private void BeginStoreFromPawn(Pawn pawn, Thing thing)
        {
            if (pawn == null || thing == null || thing.Destroyed) return;
            int available = thing.stackCount > 0 ? thing.stackCount : 1;
            bool canStore = OutpostStorageUtility.CanStoreRow(outpost, thing.def);

            if (!canStore)
            {
                ConfirmDestroyFromPawn(pawn, thing, available);
                return;
            }

            if (available > 1)
            {
                Thing captured = thing;
                Pawn capturedPawn = pawn;
                PromptCount(thing.def.LabelCap, available, chosen => StoreFromPawn(capturedPawn, captured, chosen));
                return;
            }
            StoreFromPawn(pawn, thing, available);
        }

        private void ConfirmDestroyFromPawn(Pawn pawn, Thing thing, int count)
        {
            if (pawn == null || thing == null || thing.Destroyed || count <= 0) return;
            string label = thing.LabelNoCount.CapitalizeFirst();
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "TSA_WD_Armory_DestroyNotStorable".Translate(label),
                () => DestroyFromPawn(pawn, thing, count),
                destructive: true));
        }

        private void DestroyFromPawn(Pawn pawn, Thing thing, int count)
        {
            if (!OutpostArmoryUtility.TryDestroyFromPawn(outpost, pawn, thing, count, out string fail))
            {
                if (!string.IsNullOrEmpty(fail)) pendingMessage = fail;
                return;
            }
            NotifyChanged();
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

        private void GiveToPawn(Pawn pawn, object payload, bool forceInventory)
        {
            if (!forceInventory)
            {
                ThingDef def = payload is Thing ut ? ut.def
                    : payload is ThingDefCountClass r ? r.thingDef : null;
                if (!OutpostArmoryUtility.IsEquipSlotDef(def))
                {
                    pendingMessage = "TSA_WD_Armory_FailNotEquippable".Translate(def?.LabelCap ?? "?");
                    return;
                }
            }

            if (payload is Thing unique)
            {
                ThingOwner<Thing> uniques = Armory?.Uniques;
                if (uniques == null || !uniques.Contains(unique)) return;

                int available = unique.stackCount > 0 ? unique.stackCount : 1;
                int fit = OutpostArmoryUtility.ClampAssignCount(pawn, unique.def, available, forceInventory);
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
                            int give = OutpostArmoryUtility.ClampAssignCount(pawn, captured.def, chosen, forceInventory);
                            if (give <= 0)
                            {
                                Messages.Message(OutpostArmoryUtility.NoCapacityReason(pawn, captured.def), outpost, MessageTypeDefOf.RejectInput, false);
                                return;
                            }
                            Thing taken = uniques.Take(captured, give);
                            if (OutpostArmoryUtility.TryGiveUniqueToPawn(outpost, pawn, taken, out string fail, forceInventory))
                                NotifyChanged();
                            else if (!string.IsNullOrEmpty(fail))
                                Messages.Message(fail, outpost, MessageTypeDefOf.RejectInput, false);
                        });
                    return;
                }

                Thing takenFit = uniques.Take(unique, fit);
                if (OutpostArmoryUtility.TryGiveUniqueToPawn(outpost, pawn, takenFit, out string failU, forceInventory))
                    NotifyChanged();
                else if (!string.IsNullOrEmpty(failU))
                    pendingMessage = failU;
                return;
            }

            if (payload is ThingDefCountClass row)
            {
                TryBeginStockGive(pawn, row, forceInventory);
                return;
            }
        }

        /// <summary>
        /// Opens a count picker whenever more than one unit can be assigned. Ammo and grenades
        /// always prompt (CE stack counts), even when the inventory-fit clamp is wrong/low.
        /// </summary>
        private bool TryBeginStockGive(Pawn pawn, ThingDefCountClass row, bool forceInventory)
        {
            int stored = Armory?.GetStockCountMatching(row) ?? 0;
            if (stored <= 0) return false;

            int fit = OutpostArmoryUtility.ClampAssignCount(pawn, row.thingDef, stored, forceInventory);
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
                    if (OutpostArmoryUtility.TryGiveStockToPawn(
                            outpost, capturedPawn, capturedRow, chosen, out string f, forceInventory))
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

            if (OutpostArmoryUtility.TryGiveStockToPawn(outpost, pawn, row, fit, out string fail, forceInventory))
                NotifyChanged();
            else if (!string.IsNullOrEmpty(fail))
                pendingMessage = fail;
            return false;
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

        private void StripAllFood()
        {
            int moved = OutpostArmoryUtility.StripInventoryMatching(
                outpost, OutpostArmoryUtility.IsNutritionFoodDef, out string fail);
            if (!string.IsNullOrEmpty(fail))
            {
                pendingMessage = fail;
                return;
            }
            Messages.Message(
                moved > 0
                    ? "TSA_WD_Armory_RemovedFood".Translate(moved.ToString())
                    : "TSA_WD_Armory_RemovedFoodNone".Translate(),
                outpost,
                moved > 0 ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.RejectInput,
                false);
            if (moved > 0) NotifyChanged();
        }

        private void AssignFoodToPawns()
        {
            int moved = OutpostArmoryUtility.TryAutoAssignFoodFromStore(outpost, out string fail);
            if (!string.IsNullOrEmpty(fail))
            {
                pendingMessage = fail;
                return;
            }
            Messages.Message(
                moved > 0
                    ? "TSA_WD_Armory_AssignedFood".Translate(moved.ToString())
                    : "TSA_WD_Armory_AssignedFoodNone".Translate(),
                outpost,
                moved > 0 ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.RejectInput,
                false);
            if (moved > 0) NotifyChanged();
        }

        private void StripAllAmmo()
        {
            int moved = OutpostArmoryUtility.StripInventoryMatching(
                outpost, OutpostArmoryUtility.IsAmmoDef, out string fail);
            if (!string.IsNullOrEmpty(fail))
            {
                pendingMessage = fail;
                return;
            }
            Messages.Message(
                moved > 0
                    ? "TSA_WD_Armory_RemovedAmmo".Translate(moved.ToString())
                    : "TSA_WD_Armory_RemovedAmmoNone".Translate(),
                outpost,
                moved > 0 ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.RejectInput,
                false);
            if (moved > 0) NotifyChanged();
        }

        private void AutoAssignAmmo()
        {
            int units = OutpostArmoryUtility.TryAutoAssignAmmoFromStore(outpost, out string fail);
            if (!string.IsNullOrEmpty(fail))
            {
                pendingMessage = fail;
                return;
            }
            Messages.Message(
                units > 0
                    ? "TSA_WD_Armory_AssignedAmmo".Translate(units.ToString())
                    : "TSA_WD_Armory_AssignedAmmoNone".Translate(),
                outpost,
                units > 0 ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.RejectInput,
                false);
            if (units > 0) NotifyChanged();
        }

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
            Window_AllPlayerGear.InvalidateCache();
        }

        private void RebuildRows()
        {
            rowsDirty = false;
            storeRows.Clear();
            pawnInventory.Clear();
            pawnCarryLoad.Clear();

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
                pawnCarryLoad[p] = BuildCarryLoad(p);
            }
        }

        private CarryLoadCache BuildCarryLoad(Pawn pawn)
        {
            if (pawn == null) return default;
            if (OutpostCeAmmoCompat.TryGetCarryLoad(pawn,
                    out float massCur, out float massCap, out float bulkCur, out float bulkCap))
            {
                return new CarryLoadCache
                {
                    MassCur = massCur,
                    MassCap = massCap,
                    BulkCur = bulkCur,
                    BulkCap = bulkCap,
                    HasBulk = true
                };
            }

            return new CarryLoadCache
            {
                MassCur = MassUtility.GearAndInventoryMass(pawn),
                MassCap = MassUtility.Capacity(pawn),
                HasBulk = false
            };
        }

        private float GetMassCurrent(Pawn pawn)
        {
            if (pawn != null && pawnCarryLoad.TryGetValue(pawn, out CarryLoadCache load))
                return load.MassCur;
            return BuildCarryLoad(pawn).MassCur;
        }

        private float GetBulkCurrent(Pawn pawn)
        {
            if (pawn != null && pawnCarryLoad.TryGetValue(pawn, out CarryLoadCache load))
                return load.BulkCur;
            return BuildCarryLoad(pawn).BulkCur;
        }

        private static string FormatCarryPair(float cur, float cap) =>
            cur.ToString("F0") + "/" + cap.ToString("F0");

        /// <summary>99%+ red, 90%+ orange, 70%+ yellow, else green.</summary>
        private static Color CarryLoadTextColor(float cur, float cap)
        {
            if (cap <= 0f)
                return cur > 0f ? Color.red : new Color(0.45f, 0.85f, 0.45f);
            float ratio = cur / cap;
            if (ratio >= 0.99f) return Color.red;
            if (ratio >= 0.9f) return new Color(1f, 0.55f, 0.15f);
            if (ratio >= 0.7f) return new Color(1f, 0.9f, 0.25f);
            return new Color(0.45f, 0.85f, 0.45f);
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
