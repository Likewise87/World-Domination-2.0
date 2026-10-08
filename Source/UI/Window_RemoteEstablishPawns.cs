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
    /// Same-origin pawn picker for tile-first remote establish (colony or one outpost).
    /// Confirm launches via <see cref="RemoteOutpostEstablishUtility.TryLaunch"/>.
    /// Roster chrome matches All Player Pawns (filters, columns, highlight, restore).
    /// </summary>
    [StaticConstructorOnStartup]
    public class Window_RemoteEstablishPawns : Window
    {
        private const float RowHeightCompact = 40f;
        private const float RowHeightTall = 58f;
        private const float HeaderHeight = 28f;
        private const int UpdateIntervalTicks = 300;
        private const int PortraitCacheMax = 80;

        private const float LocIconPad = 4f;
        private const float LocIconDrawSize = 40f;
        private const float ColIcon = LocIconDrawSize + LocIconPad * 2f;
        private const float ColLocType = 108f;
        private const float ColLocName = 140f;
        private const float ColSelect = 36f;
        private const float ColPawnType = 96f;
        private const float ColPortrait = 40f;
        private const float ColName = 140f;
        private const float ColStar = 56f;
        private const float ColNew = 56f;
        private const float ColSkill = 74f;
        private const float ColPadding = 12f;
        private const float ColAge = 44f;
        private const float ColHealth = 56f;
        private const float ColTraits = 128f;
        private const float ColXenotype = 100f;
        private const float ColPsycasts = 110f;
        private const float ColIdeology = 110f;
        private const float ConfirmBtnWidth = 140f;
        private const float ViewControlsGap = 10f;
        private const PawnRosterColumnWindow ColWindow = PawnRosterColumnWindow.RemoteEstablish;

        private static readonly Vector2 PortraitSize = new Vector2(36f, 36f);
        private static readonly Dictionary<string, Texture> PortraitCache = new Dictionary<string, Texture>();

        private readonly int tile;
        private readonly WorldObjectDef outpostDef;

        private Vector2 scrollPos;
        private string sortColumn = PlayerPawnRosterUtility.DefaultSortColumn;
        private bool sortAscending = true;
        private bool useDefaultGrouping = true;
        private string pawnSearchTerm = "";
        private string locationNameSearchTerm = "";
        private string locationTypeSearchTerm = "";
        private PlayerPawnTypeFilter pawnTypeFilter = PlayerPawnTypeFilter.All;
        private PlayerPawnStarFilter starFilter = PlayerPawnStarFilter.AllAnywhere;
        private PawnRosterJoinedFilter joinedFilter = PawnRosterJoinedFilter.All;
        private string xenotypeFilter = "";
        private string psycastFilter = "";
        private string ideoFilter = "";
        private int lastUpdateTick = -9999;
        private bool cacheInvalidated;
        private List<PlayerPawnRosterEntry> cachedList = new List<PlayerPawnRosterEntry>();
        private readonly HashSet<string> selectedThingIds = new HashSet<string>();
        private float lastScrollViewportHeight = 400f;
        private static string starHeaderTip;
        private static string joinStampHeaderTip;
        private static Window_RemoteEstablishPawns openInstance;

        public override Vector2 InitialSize => new Vector2(UI.screenWidth, UI.screenHeight);

        public static void InvalidateCache()
        {
            if (openInstance != null)
                openInstance.cacheInvalidated = true;
        }

        public Window_RemoteEstablishPawns(int tile, WorldObjectDef outpostDef)
        {
            this.tile = tile;
            this.outpostDef = outpostDef;
            doCloseX = true;
            closeOnCancel = true;
            absorbInputAroundWindow = true;
            forcePause = false;
            openInstance = this;
            PawnRosterTraitFilter.Clear();
            ApplyInitialSortForOutpost();
        }

        public override void PreClose()
        {
            if (openInstance == this)
                openInstance = null;
            base.PreClose();
        }

        /// <summary>
        /// Pre-sort by the outpost's primary relevant skill (highest first) so the best candidates are on top.
        /// Falls back to default grouping when the type has no skill (e.g. scavenging).
        /// </summary>
        private void ApplyInitialSortForOutpost()
        {
            var skills = WorldObject_WD_Outpost.GetRelevantSkillDefs(outpostDef);
            if (skills == null || skills.Count == 0) return;

            SkillDef primary = skills[0];
            if (primary == null) return;

            SkillDef[] columns = PlayerPawnRosterUtility.AllSkillColumns;
            for (int i = 0; i < columns.Length; i++)
            {
                if (columns[i] != primary) continue;
                sortColumn = primary.defName;
                sortAscending = false;
                useDefaultGrouping = false;
                return;
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (PawnRosterHeaderFilter.TryCloseDropdownOnCancel())
                return;
            if (WdWindowEsc.TryCloseOnCancel(this))
                return;

            PawnRosterPaintSelect.BeginFrame(this);

            float totalWidth = ComputeTotalTableWidth();
            float rowH = EffectiveRowHeight();

            if (cacheInvalidated)
            {
                lastUpdateTick = -9999;
                cacheInvalidated = false;
            }

            if (Find.TickManager.TicksGame >= lastUpdateTick + UpdateIntervalTicks || cachedList.Count == 0)
            {
                cachedList = BuildCurrentRoster();
                PlayerPawnRosterUtility.PruneSelectionToLastScan(selectedThingIds);
                lastUpdateTick = Find.TickManager.TicksGame;
            }

            Text.Font = GameFont.Medium;
            string title = "TSA_WD_TileFirstEstablish_PawnWindowTitle".Translate(
                outpostDef?.LabelCap ?? "Outpost").ToString();
            Widgets.Label(new Rect(0f, 0f, inRect.width - ConfirmBtnWidth - 16f, 32f), title);
            Text.Font = GameFont.Small;

            float modeStripY = 34f;
            var selectedForPod = PlayerPawnRosterUtility.ResolveSelectedEntriesIncludingHidden(cachedList, selectedThingIds);
            WorldObject podOrigin = null;
            if (RemoteOutpostEstablishUtility.TryValidateFoundingSelection(selectedForPod, out WorldObject origin, out _, out _))
                podOrigin = origin;
            bool allowPod = RapidResponseUtility.TransportPodsResearched()
                && podOrigin != null
                && !podOrigin.Destroyed
                && PlayerPawnDropPodUtility.InDropPodRange(podOrigin, tile);
            string podTip = !RapidResponseUtility.TransportPodsResearched()
                ? "TSA_WD_DispatchMode_NeedsResearch".Translate()
                : (!allowPod ? "TSA_WD_PawnDropPod_OutOfRange".Translate() : null);

            bool canConfirm = CanConfirm(selectedForPod, out string disabledTip);

            Rect confirmRect = new Rect(inRect.width - ConfirmBtnWidth, 2f, ConfirmBtnWidth, ToolbarBtnHeight());
            PlayerPawnRosterUtility.DrawRosterViewControls(
                2f,
                ToolbarBtnHeight(),
                confirmRect.x - ViewControlsGap,
                ColWindow,
                RestoreDefaultView,
                () => Find.WindowStack.Add(new Dialog_PawnRosterColumns(ColWindow, OnColumnsChanged)));

            if (!canConfirm)
            {
                GUI.color = Color.gray;
                Widgets.ButtonText(confirmRect, "TSA_WD_TileFirstEstablish_Confirm".Translate(), active: false);
                GUI.color = Color.white;
                if (!disabledTip.NullOrEmpty())
                    TooltipHandler.TipRegion(confirmRect, disabledTip);
            }
            else if (Widgets.ButtonText(confirmRect, "TSA_WD_TileFirstEstablish_Confirm".Translate()))
            {
                TryConfirm(selectedForPod);
            }

            float modeH = PlayerPawnDropPodUtility.DrawAdHocModeAndCostStrip(
                new Rect(0f, modeStripY, Mathf.Max(280f, inRect.width - ConfirmBtnWidth - 24f), 90f),
                launchCount: 1,
                allowDropPod: allowPod,
                disabledPodTip: podTip);

            float headerTop = modeStripY + modeH + 6f;
            float listTop = headerTop + HeaderHeight + 4f;
            float tableHeight = inRect.height - listTop - 8f;

            DrawHorizontallyScrolledSection(
                new Rect(0f, headerTop, inRect.width, HeaderHeight),
                scrollPos.x,
                totalWidth,
                x => DrawTableHeader(x, 0f, totalWidth));
            Widgets.DrawLineHorizontal(0f, headerTop + HeaderHeight, inRect.width);

            float totalHeight = cachedList.Count * rowH + 8f;
            Rect viewRect = new Rect(0f, 0f, totalWidth, Mathf.Max(totalHeight, tableHeight));
            Rect scrollOuter = new Rect(0f, listTop, inRect.width, tableHeight);
            lastScrollViewportHeight = scrollOuter.height;

            Widgets.BeginScrollView(scrollOuter, ref scrollPos, viewRect);
            for (int i = 0; i < cachedList.Count; i++)
                DrawRow(0f, i * rowH, totalWidth, cachedList[i], i % 2 == 0);
            Widgets.EndScrollView();
            Text.Anchor = TextAnchor.UpperLeft;
            PawnRosterHeaderFilter.DrawDropdownIfOpen();
        }

        private static float ToolbarBtnHeight() => 30f;

        private bool CanConfirm(List<PlayerPawnRosterEntry> selected, out string disabledTip)
        {
            disabledTip = null;
            if (!RemoteOutpostEstablishUtility.TryValidateFoundingSelection(selected, out _, out _, out string fail))
            {
                disabledTip = fail;
                return false;
            }

            Map colonyMap = Outpost_PowerPlant.GetPlayerColonyMap();
            List<Pawn> pawns = RemoteOutpostEstablishUtility.CollectPawns(selected);
            if (!RemoteOutpostEstablishUtility.CanEstablishAtRemote(tile, outpostDef, pawns, colonyMap, out string establishFail))
            {
                disabledTip = establishFail;
                return false;
            }

            return true;
        }

        private void TryConfirm(List<PlayerPawnRosterEntry> selected)
        {
            if (!RemoteOutpostEstablishUtility.TryValidateFoundingSelection(selected, out WorldObject origin, out List<PlayerPawnRosterEntry> entries, out string fail))
            {
                Messages.Message(fail ?? "TSA_WD_RemoteEstablish_InvalidSelection".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            RemoteOutpostEstablishUtility.LaunchAfterOptionalCarryConfirm(
                tile, outpostDef, origin, entries,
                onSuccess: () => Close(),
                onFail: launchFail => Messages.Message(
                    launchFail ?? "TSA_WD_RemoteEstablish_Failed".Translate(),
                    MessageTypeDefOf.RejectInput, false),
                onCancel: null,
                viaDropPod: PlayerPawnDropPodUtility.AdHocViaDropPod);
        }

        private static void DrawHorizontallyScrolledSection(Rect viewport, float scrollX, float contentWidth, Action<float> draw)
        {
            GUI.BeginGroup(viewport);
            draw(-scrollX);
            GUI.EndGroup();
        }

        private List<PlayerPawnRosterEntry> BuildCurrentRoster(
            PlayerPawnTypeFilter? typeF = null,
            PlayerPawnStarFilter? starF = null,
            PawnRosterJoinedFilter? joinedF = null,
            bool applyXenotype = true,
            bool applyLocationType = true,
            bool applyPsycast = true,
            bool applyIdeology = true)
        {
            string pawnSearchLower = string.IsNullOrEmpty(pawnSearchTerm) ? null : pawnSearchTerm.ToLowerInvariant();
            string locNameLower = string.IsNullOrEmpty(locationNameSearchTerm) ? null : locationNameSearchTerm.ToLowerInvariant();
            string locTypeLower = applyLocationType && !string.IsNullOrEmpty(locationTypeSearchTerm)
                ? locationTypeSearchTerm.ToLowerInvariant()
                : null;
            PlayerPawnTypeFilter type = typeF ?? (ColOn(PawnRosterColumnIds.Type) ? pawnTypeFilter : PlayerPawnTypeFilter.All);
            PlayerPawnStarFilter star = starF ?? (ColOn(PawnRosterColumnIds.Star) ? starFilter : PlayerPawnStarFilter.AllAnywhere);
            PawnRosterJoinedFilter joined = joinedF ?? (ColOn(PawnRosterColumnIds.New) ? joinedFilter : PawnRosterJoinedFilter.All);
            var list = PlayerPawnRosterUtility.BuildRoster(
                pawnSearchLower, locNameLower, locTypeLower, null,
                useDefaultGrouping, sortColumn, sortAscending, star, type, joined);
            PawnRosterTraitFilter.ApplyToPlayerRows(list, ColWindow);
            if (applyXenotype && ColOn(PawnRosterColumnIds.Xenotype))
                PawnRosterTraitFilter.ApplyXenotypeToPlayerRows(list, xenotypeFilter);
            if (applyPsycast && ColOn(PawnRosterColumnIds.Psycasts))
                PawnRosterTraitFilter.ApplyPsycastToPlayerRows(list, psycastFilter);
            if (applyIdeology && ColOn(PawnRosterColumnIds.Ideology))
                PawnRosterTraitFilter.ApplyIdeologyToPlayerRows(list, ideoFilter);
            return list;
        }

        private void OnColumnsChanged()
        {
            if (!ColOn(PawnRosterColumnIds.Type) && sortColumn == "PawnType")
                ClearSortToDefault();
            else if (!ColOn(PawnRosterColumnIds.Star) && sortColumn == "Starred")
                ClearSortToDefault();
            else if (!ColOn(PawnRosterColumnIds.New) && sortColumn == "New")
                ClearSortToDefault();
            else if (!ColOn(PawnRosterColumnIds.Age) && sortColumn == "Age")
                ClearSortToDefault();
            else if (!ColOn(PawnRosterColumnIds.Health) && sortColumn == "Health")
                ClearSortToDefault();
            else if (!ColOn(PawnRosterColumnIds.Traits) && sortColumn == "Traits")
                ClearSortToDefault();
            else if (!ColOn(PawnRosterColumnIds.Xenotype) && sortColumn == "Xenotype")
                ClearSortToDefault();
            else if (!ColOn(PawnRosterColumnIds.Psycasts) && sortColumn == "Psycasts")
                ClearSortToDefault();
            else if (!ColOn(PawnRosterColumnIds.Ideology) && sortColumn == "Ideology")
                ClearSortToDefault();
            else
            {
                SkillDef[] skills = PlayerPawnRosterUtility.AllSkillColumns;
                for (int i = 0; i < skills.Length; i++)
                {
                    if (sortColumn == skills[i].defName && !ColOn(PawnRosterColumnIds.Skill(skills[i])))
                    {
                        ClearSortToDefault();
                        break;
                    }
                }
            }
            lastUpdateTick = -9999;
        }

        private void ClearSortToDefault()
        {
            useDefaultGrouping = true;
            sortColumn = PlayerPawnRosterUtility.DefaultSortColumn;
            sortAscending = true;
            ApplyInitialSortForOutpost();
        }

        private static bool ColOn(string id) => PlayerPawnRosterUtility.ColVisible(ColWindow, id);

        private void RestoreDefaultView()
        {
            pawnSearchTerm = "";
            locationNameSearchTerm = "";
            locationTypeSearchTerm = "";
            pawnTypeFilter = PlayerPawnTypeFilter.All;
            starFilter = PlayerPawnStarFilter.AllAnywhere;
            joinedFilter = PawnRosterJoinedFilter.All;
            xenotypeFilter = "";
            psycastFilter = "";
            ideoFilter = "";
            scrollPos = Vector2.zero;
            lastUpdateTick = -9999;
            PlayerPawnRosterUtility.ResetSkillDisplayOptions(ColWindow);
            WorldComponent_PawnRosterColumnPrefs.Get()?.ResetToDefaults(ColWindow);
            PawnRosterTraitFilter.Clear();
            PawnRosterHeaderFilter.CloseDropdown();
            useDefaultGrouping = true;
            sortColumn = PlayerPawnRosterUtility.DefaultSortColumn;
            sortAscending = true;
            ApplyInitialSortForOutpost();
        }

        private static float EffectiveRowHeight()
        {
            if (ColOn(PawnRosterColumnIds.Traits) || ColOn(PawnRosterColumnIds.Psycasts))
                return RowHeightTall;
            return RowHeightCompact;
        }

        private float ComputeTotalTableWidth()
        {
            float w = ColIcon + ColLocType + ColLocName + ColSelect + ColPortrait + ColName;
            if (ColOn(PawnRosterColumnIds.Type)) w += ColPawnType;
            if (ColOn(PawnRosterColumnIds.Star)) w += ColStar;
            if (ColOn(PawnRosterColumnIds.New)) w += ColNew;
            w += ColPadding;
            if (ColOn(PawnRosterColumnIds.Age)) w += ColAge;
            if (ColOn(PawnRosterColumnIds.Health)) w += ColHealth;
            if (ColOn(PawnRosterColumnIds.Traits)) w += ColTraits;
            if (ColOn(PawnRosterColumnIds.Xenotype)) w += ColXenotype;
            if (ColOn(PawnRosterColumnIds.Psycasts)) w += ColPsycasts;
            if (ColOn(PawnRosterColumnIds.Ideology)) w += ColIdeology;
            SkillDef[] skills = PlayerPawnRosterUtility.AllSkillColumns;
            for (int i = 0; i < skills.Length; i++)
            {
                if (ColOn(PawnRosterColumnIds.Skill(skills[i])))
                    w += ColSkill;
            }
            return w;
        }

        private static void EnsureStarHeaderTip()
        {
            if (starHeaderTip == null)
                starHeaderTip = "TSA_WD_AllPlayerPawns_StarTip".Translate();
        }

        private static void EnsureJoinStampHeaderTip()
        {
            if (joinStampHeaderTip == null)
                joinStampHeaderTip = "TSA_WD_PawnRoster_ColNewTip".Translate();
        }

        private void DrawTableHeader(float x, float y, float width)
        {
            EnsureStarHeaderTip();
            EnsureJoinStampHeaderTip();
            float curX = x;
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Rect hRect = new Rect(x, y, width, HeaderHeight);

            curX += ColIcon;
            PawnRosterHeaderFilter.DrawFilterableHeader(
                ref curX, hRect.y, ColLocType, HeaderHeight,
                "TSA_WD_AllPlayerPawns_ColLocationType".Translate(),
                sortColumn == "LocationType", sortAscending,
                TextAnchor.MiddleLeft,
                !locationTypeSearchTerm.NullOrEmpty(),
                "TSA_WD_FilterByLocationType".Translate(),
                icon => PawnRosterHeaderFilter.OpenChoiceDropdown(
                    icon,
                    "TSA_WD_FilterByLocationType".Translate(),
                    PawnRosterHeaderFilter.LocationTypeChoices(locationTypeSearchTerm, v =>
                    {
                        locationTypeSearchTerm = v ?? "";
                        lastUpdateTick = -9999;
                    }, PawnRosterHeaderFilter.LocationKindsFrom(BuildCurrentRoster(applyLocationType: false)))),
                () => SetSort("LocationType"));
            PawnRosterHeaderFilter.DrawFilterableHeader(
                ref curX, hRect.y, ColLocName, HeaderHeight,
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
                    v => { locationNameSearchTerm = v; lastUpdateTick = -9999; },
                    () => { locationNameSearchTerm = ""; lastUpdateTick = -9999; }),
                () => SetSort("LocationName"));
            DrawSelectAllHeader(ref curX, hRect);
            if (ColOn(PawnRosterColumnIds.Type))
            {
                PawnRosterHeaderFilter.DrawFilterableHeader(
                    ref curX, hRect.y, ColPawnType, HeaderHeight,
                    "TSA_WD_AllPlayerPawns_ColPawnType".Translate(),
                    sortColumn == "PawnType", sortAscending,
                    TextAnchor.MiddleCenter,
                    pawnTypeFilter != PlayerPawnTypeFilter.All,
                    "TSA_WD_FilterByType".Translate(),
                    icon => PawnRosterHeaderFilter.OpenChoiceDropdown(
                        icon,
                        "TSA_WD_FilterByType".Translate(),
                        PawnRosterHeaderFilter.TypeChoices(pawnTypeFilter, f =>
                        {
                            pawnTypeFilter = f;
                            lastUpdateTick = -9999;
                        }, PawnRosterHeaderFilter.CategoriesFrom(BuildCurrentRoster(PlayerPawnTypeFilter.All)))),
                    () => SetSort("PawnType"));
            }
            curX += ColPortrait;
            PawnRosterHeaderFilter.DrawFilterableHeader(
                ref curX, hRect.y, ColName, HeaderHeight,
                "TSA_WD_PawnCol_PawnName".Translate(),
                sortColumn == "Name", sortAscending,
                TextAnchor.MiddleCenter,
                !pawnSearchTerm.NullOrEmpty(),
                "TSA_WD_AllPlayerPawns_SearchName".Translate(),
                icon => PawnRosterHeaderFilter.OpenTextDropdown(
                    icon,
                    "TSA_WD_FilterByPawnName".Translate(),
                    "TSA_WD_AllPlayerPawns_SearchName".Translate(),
                    () => pawnSearchTerm,
                    v => { pawnSearchTerm = v; lastUpdateTick = -9999; },
                    () => { pawnSearchTerm = ""; lastUpdateTick = -9999; }),
                () => SetSort("Name"));
            if (ColOn(PawnRosterColumnIds.Star))
            {
                PawnRosterHeaderFilter.DrawFilterableHeader(
                    ref curX, hRect.y, ColStar, HeaderHeight,
                    "",
                    sortColumn == "Starred", sortAscending,
                    TextAnchor.MiddleCenter,
                    starFilter != PlayerPawnStarFilter.AllAnywhere,
                    starHeaderTip,
                    icon => PawnRosterHeaderFilter.OpenChoiceDropdown(
                        icon,
                        "TSA_WD_FilterByStar".Translate(),
                        PawnRosterHeaderFilter.PlayerStarChoices(starFilter, f =>
                        {
                            starFilter = f;
                            lastUpdateTick = -9999;
                        }, PawnRosterHeaderFilter.StarRowsFrom(BuildCurrentRoster(starF: PlayerPawnStarFilter.AllAnywhere))),
                        width: 280f),
                    () => SetSort("Starred"));
            }
            if (ColOn(PawnRosterColumnIds.New))
            {
                PawnRosterHeaderFilter.DrawFilterableHeader(
                    ref curX, hRect.y, ColNew, HeaderHeight,
                    null,
                    sortColumn == "New", sortAscending,
                    TextAnchor.MiddleCenter,
                    joinedFilter != PawnRosterJoinedFilter.All,
                    "TSA_WD_FilterByNew".Translate(),
                    icon => PawnRosterHeaderFilter.OpenChoiceDropdown(
                        icon,
                        "TSA_WD_FilterByNew".Translate(),
                        PawnRosterHeaderFilter.JoinedFilterChoices(joinedFilter, f =>
                        {
                            joinedFilter = f;
                            lastUpdateTick = -9999;
                        }, PawnRosterHeaderFilter.JoinDaysFrom(BuildCurrentRoster(joinedF: PawnRosterJoinedFilter.All))),
                        width: 300f),
                    () => SetSort("New"),
                    PawnRosterHeaderFilter.JoinStampHeaderIcon,
                    joinStampHeaderTip);
            }
            curX += ColPadding;

            if (ColOn(PawnRosterColumnIds.Age))
            {
                Rect ageHdr = new Rect(curX, hRect.y, ColAge, hRect.height);
                DrawSortHeader(ref curX, ColAge, "TSA_WD_PawnRoster_ColAge".Translate(), "Age", hRect);
                TooltipHandler.TipRegion(ageHdr, "TSA_WD_PawnRoster_ColAgeTip".Translate());
            }

            if (ColOn(PawnRosterColumnIds.Health))
            {
                Rect healthHdr = new Rect(curX, hRect.y, ColHealth, hRect.height);
                DrawSortHeader(ref curX, ColHealth, "TSA_WD_PawnRoster_ColHealth".Translate(), "Health", hRect);
                TooltipHandler.TipRegion(healthHdr, "TSA_WD_PawnRoster_ColHealthTip".Translate());
            }

            if (ColOn(PawnRosterColumnIds.Traits))
            {
                PawnRosterTraitFilter.DrawTraitsHeader(
                    ref curX, hRect.y, ColTraits, HeaderHeight,
                    "TSA_WD_Prisoners_ColTraits".Translate(),
                    sortColumn == "Traits", sortAscending,
                    TextAnchor.MiddleCenter,
                    () => SetSort("Traits"));
            }

            if (ColOn(PawnRosterColumnIds.Xenotype))
            {
                PawnRosterHeaderFilter.DrawFilterableHeader(
                    ref curX, hRect.y, ColXenotype, HeaderHeight,
                    "TSA_WD_PawnRoster_ColXenotype".Translate(),
                    sortColumn == "Xenotype", sortAscending,
                    TextAnchor.MiddleCenter,
                    !xenotypeFilter.NullOrEmpty(),
                    "TSA_WD_FilterByXenotype".Translate(),
                    icon => PawnRosterHeaderFilter.OpenChoiceDropdown(
                        icon,
                        "TSA_WD_FilterByXenotype".Translate(),
                        PawnRosterHeaderFilter.XenotypeChoices(xenotypeFilter, v =>
                        {
                            xenotypeFilter = v ?? "";
                            lastUpdateTick = -9999;
                        }, PawnRosterHeaderFilter.XenotypeKeysFrom(BuildCurrentRoster(applyXenotype: false)))),
                    () => SetSort("Xenotype"));
            }

            if (ColOn(PawnRosterColumnIds.Psycasts))
            {
                PawnRosterHeaderFilter.DrawFilterableHeader(
                    ref curX, hRect.y, ColPsycasts, HeaderHeight,
                    "TSA_WD_PawnRoster_ColPsycasts".Translate(),
                    sortColumn == "Psycasts", sortAscending,
                    TextAnchor.MiddleCenter,
                    !psycastFilter.NullOrEmpty(),
                    "TSA_WD_FilterByPsycast".Translate(),
                    icon => PawnRosterHeaderFilter.OpenChoiceDropdown(
                        icon,
                        "TSA_WD_FilterByPsycast".Translate(),
                        PawnRosterHeaderFilter.PsycastChoices(psycastFilter, v =>
                        {
                            psycastFilter = v ?? "";
                            lastUpdateTick = -9999;
                        }, PawnRosterHeaderFilter.PsycastListsFrom(BuildCurrentRoster(applyPsycast: false)))),
                    () => SetSort("Psycasts"));
            }

            if (ColOn(PawnRosterColumnIds.Ideology))
            {
                PawnRosterHeaderFilter.DrawFilterableHeader(
                    ref curX, hRect.y, ColIdeology, HeaderHeight,
                    "TSA_WD_PawnRoster_ColIdeology".Translate(),
                    sortColumn == "Ideology", sortAscending,
                    TextAnchor.MiddleCenter,
                    !ideoFilter.NullOrEmpty(),
                    "TSA_WD_FilterByIdeology".Translate(),
                    icon => PawnRosterHeaderFilter.OpenChoiceDropdown(
                        icon,
                        "TSA_WD_FilterByIdeology".Translate(),
                        PawnRosterHeaderFilter.IdeoChoices(ideoFilter, v =>
                        {
                            ideoFilter = v ?? "";
                            lastUpdateTick = -9999;
                        }, PawnRosterHeaderFilter.IdeoKeysFrom(BuildCurrentRoster(applyIdeology: false)))),
                    () => SetSort("Ideology"));
            }

            SkillDef[] skills = PlayerPawnRosterUtility.AllSkillColumns;
            for (int i = 0; i < skills.Length; i++)
            {
                SkillDef skill = skills[i];
                if (skill == null || !ColOn(PawnRosterColumnIds.Skill(skill))) continue;
                string skillTag = skill.defName;
                PawnRosterHeaderFilter.DrawFilterableHeader(
                    ref curX, hRect.y, ColSkill, HeaderHeight,
                    skill.LabelCap,
                    sortColumn == skillTag, sortAscending,
                    TextAnchor.MiddleCenter,
                    false,
                    null,
                    null,
                    () => SetSort(skillTag));
            }

            GUI.color = Color.white;
        }

        private void DrawSortHeader(ref float curX, float width, string label, string tag, Rect hRect)
        {
            Rect headerRect = new Rect(curX, hRect.y, width, hRect.height);
            if (Mouse.IsOver(headerRect)) Widgets.DrawHighlight(headerRect);
            Text.Anchor = TextAnchor.MiddleCenter;
            string headerText = label + (sortColumn == tag ? (sortAscending ? " ▲" : " ▼") : "");
            Widgets.Label(headerRect, headerText.Truncate(width - 4f));
            if (Widgets.ButtonInvisible(headerRect)) SetSort(tag);
            curX += width;
        }

        private void DrawSelectAllHeader(ref float curX, Rect hRect)
        {
            Rect selHdr = new Rect(curX, hRect.y, ColSelect, hRect.height);
            if (Mouse.IsOver(selHdr)) Widgets.DrawHighlight(selHdr);

            bool allSelected = AreAllVisibleSelected();
            float box = 18f;
            float cx = selHdr.x + (ColSelect - box) * 0.5f;
            float cy = selHdr.y + (HeaderHeight - box) * 0.5f;
            int visibleCount = CountVisibleSelectable();
            Widgets.CheckboxDraw(cx, cy, allSelected, visibleCount == 0, box);

            TooltipHandler.TipRegion(selHdr, "TSA_WD_PawnCol_SelectColumnTip".Translate());
            if (visibleCount > 0 && Widgets.ButtonInvisible(selHdr))
            {
                ToggleSelectAllVisible();
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
            curX += ColSelect;
        }

        private int CountVisibleSelectable()
        {
            int count = 0;
            for (int i = 0; i < cachedList.Count; i++)
            {
                if (cachedList[i].isMovable && !cachedList[i].thingId.NullOrEmpty())
                    count++;
            }
            return count;
        }

        private bool AreAllVisibleSelected()
        {
            int visible = 0;
            for (int i = 0; i < cachedList.Count; i++)
            {
                if (!cachedList[i].isMovable) continue;
                string tid = cachedList[i].thingId;
                if (tid.NullOrEmpty()) continue;
                visible++;
                if (!selectedThingIds.Contains(tid))
                    return false;
            }
            return visible > 0;
        }

        private void ToggleSelectAllVisible()
        {
            if (AreAllVisibleSelected())
            {
                for (int i = 0; i < cachedList.Count; i++)
                {
                    if (!cachedList[i].isMovable) continue;
                    string tid = cachedList[i].thingId;
                    if (!tid.NullOrEmpty())
                        selectedThingIds.Remove(tid);
                }
            }
            else
            {
                for (int i = 0; i < cachedList.Count; i++)
                {
                    PlayerPawnRosterEntry e = cachedList[i];
                    if (!e.isMovable || e.thingId.NullOrEmpty()) continue;
                    if (e.sourceOutpost != null && OutpostPawnIdeologyUtil.IsSlaveHumanlike(e.pawn)) continue;
                    selectedThingIds.Add(e.thingId);
                }
                for (int i = 0; i < cachedList.Count; i++)
                {
                    PlayerPawnRosterEntry e = cachedList[i];
                    if (!e.isMovable || e.thingId.NullOrEmpty()) continue;
                    if (e.sourceOutpost == null || !OutpostPawnIdeologyUtil.IsSlaveHumanlike(e.pawn)) continue;
                    if (OutpostPawnIdeologyUtil.CanToggleOutpostRemovalSelection(e.sourceOutpost, selectedThingIds, e.pawn))
                        selectedThingIds.Add(e.thingId);
                }
                PruneAllOutpostRemovalSelections();
            }
        }

        private void PruneAllOutpostRemovalSelections()
        {
            if (cachedList == null || selectedThingIds.Count == 0) return;
            var seen = new HashSet<WorldObject_WD_Outpost>();
            for (int i = 0; i < cachedList.Count; i++)
            {
                WorldObject_WD_Outpost op = cachedList[i].sourceOutpost;
                if (op == null || !seen.Add(op)) continue;
                PruneOutpostRemovalSelection(op);
            }
        }

        private void PruneOutpostRemovalSelection(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return;
            OutpostPawnIdeologyUtil.PruneDependentRemovalSelection(outpost, selectedThingIds);
            if (OutpostPawnIdeologyUtil.SelectionIncludesNonSlaveOccupant(outpost, selectedThingIds))
                return;
            for (int i = 0; i < cachedList.Count; i++)
            {
                PlayerPawnRosterEntry e = cachedList[i];
                if (e.sourceOutpost != outpost || e.thingId.NullOrEmpty()) continue;
                if (e.pawn != null && outpost.Occupants != null && outpost.Occupants.Contains(e.pawn))
                    continue;
                selectedThingIds.Remove(e.thingId);
            }
        }

        private void SetSort(string col)
        {
            useDefaultGrouping = false;
            if (sortColumn == col)
                sortAscending = !sortAscending;
            else
            {
                sortColumn = col;
                // Skill columns: highest first; text columns: A→Z.
                sortAscending = !IsSkillSortColumn(col);
            }
            lastUpdateTick = -9999;
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        private static bool IsSkillSortColumn(string col)
        {
            if (string.IsNullOrEmpty(col)) return false;
            SkillDef[] columns = PlayerPawnRosterUtility.AllSkillColumns;
            for (int i = 0; i < columns.Length; i++)
            {
                if (columns[i] != null && columns[i].defName == col)
                    return true;
            }
            return false;
        }

        private void DrawRow(float x, float y, float width, PlayerPawnRosterEntry entry, bool zebra)
        {
            float rowH = EffectiveRowHeight();
            float visibleY = scrollPos.y - rowH;
            float visibleYMax = scrollPos.y + lastScrollViewportHeight;
            if (y < visibleY || y >= visibleYMax)
                return;

            Rect row = new Rect(x, y, width, rowH);
            if (zebra) Widgets.DrawHighlight(row);
            if (Mouse.IsOver(row)) Widgets.DrawLightHighlight(row);

            if (entry.isSlave)
            {
                Color nameTint = PawnNameColorUtility.PawnNameColorOf(entry.pawn);
                Color rowBg = new Color(
                    Mathf.Clamp01(nameTint.r * 0.28f + 0.08f),
                    Mathf.Clamp01(nameTint.g * 0.28f + 0.06f),
                    Mathf.Clamp01(nameTint.b * 0.12f + 0.02f),
                    0.21f);
                Widgets.DrawBoxSolid(row, rowBg);
            }

            float curX = x;
            Color prevGui = GUI.color;

            if (entry.locationIcon != null)
            {
                float iconY = y + (rowH - LocIconDrawSize) * 0.5f;
                Rect iconRect = new Rect(curX + LocIconPad, iconY, LocIconDrawSize, LocIconDrawSize);
                GUI.color = entry.locationIconColor;
                GUI.DrawTexture(iconRect, entry.locationIcon, ScaleMode.ScaleToFit);
                GUI.color = Color.white;
                TooltipHandler.TipRegion(iconRect, entry.locationLabel);
            }
            curX += ColIcon;

            Rect locTypeRect = new Rect(curX, y, ColLocType, rowH);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(locTypeRect, entry.locationTypeLabel.Truncate(ColLocType - 4f));
            TooltipHandler.TipRegion(locTypeRect, entry.locationTypeLabel);
            curX += ColLocType;

            Rect locNameRect = new Rect(curX, y, ColLocName, rowH);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(locNameRect, entry.locationLabel.Truncate(ColLocName - 4f));
            curX += ColLocName;

            Rect selRect = new Rect(curX, y, ColSelect, rowH);
            if (entry.isMovable && entry.sourceOutpost != null)
            {
                bool wasSelected = selectedThingIds.Contains(entry.thingId);
                bool canInteract = OutpostPawnIdeologyUtil.CanToggleOutpostRemovalSelection(
                    entry.sourceOutpost,
                    selectedThingIds,
                    entry.pawn);
                float cx = curX + (ColSelect - 24f) * 0.5f;
                float cy = y + (rowH - 24f) * 0.5f;
                if (!wasSelected && !canInteract)
                    TooltipHandler.TipRegion(selRect, "TSA_WD_Pawns_RemoveSlaveAccompanimentRequiredTip".Translate());
                bool nowSelected = PawnRosterPaintSelect.Draw(this, selRect, cx, cy, 24f, entry.thingId, selectedThingIds, canInteract);
                if (nowSelected != wasSelected)
                    PruneOutpostRemovalSelection(entry.sourceOutpost);
            }
            else if (entry.isMovable
                && entry.locationKind == PlayerPawnLocationKind.Colony
                && entry.mapParent != null)
            {
                bool canInteract = PlayerPawnTransferUtility.ColonyBulkSelectionIsAllowedWithExtra(
                    entry.mapParent,
                    selectedThingIds,
                    entry.pawn,
                    cachedList);
                float cx = curX + (ColSelect - 24f) * 0.5f;
                float cy = y + (rowH - 24f) * 0.5f;
                if (!selectedThingIds.Contains(entry.thingId) && !canInteract)
                {
                    var probe = new List<Pawn>();
                    for (int i = 0; i < cachedList.Count; i++)
                    {
                        PlayerPawnRosterEntry e = cachedList[i];
                        if (e.mapParent != entry.mapParent || e.sourceOutpost != null) continue;
                        if (e.pawn == null || e.thingId.NullOrEmpty()) continue;
                        if (selectedThingIds.Contains(e.thingId) || e.thingId == entry.thingId)
                            probe.Add(e.pawn);
                    }
                    if (!PlayerPawnTransferUtility.ValidateColonyLeavingPawns(entry.mapParent, probe, out string reject)
                        && !reject.NullOrEmpty())
                        TooltipHandler.TipRegion(selRect, reject);
                    else
                        TooltipHandler.TipRegion(selRect, "TSA_WD_Pawns_RemoveSlaveAccompanimentRequiredTip".Translate());
                }
                PawnRosterPaintSelect.Draw(this, selRect, cx, cy, 24f, entry.thingId, selectedThingIds, canInteract);
            }
            else
            {
                TooltipHandler.TipRegion(selRect, "TSA_WD_PawnTransfer_NotMovable".Translate());
            }
            curX += ColSelect;

            if (ColOn(PawnRosterColumnIds.Type))
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(curX, y, ColPawnType, rowH), entry.pawnTypeLabel.Truncate(ColPawnType - 4f));
                curX += ColPawnType;
            }

            Rect portraitCell = new Rect(curX, y, ColPortrait, rowH);
            Texture portrait = PawnPortraitUIUtils.GetPortrait(
                entry.pawn,
                PawnPortraitUIUtils.BuildCacheKey(entry.pawn, entry.summary),
                PortraitSize,
                PortraitCache,
                PortraitCacheMax);
            Rect portraitRect = new Rect(portraitCell.x + (portraitCell.width - PortraitSize.x) / 2f,
                y + (rowH - PortraitSize.y) / 2f, PortraitSize.x, PortraitSize.y);
            if (portrait != null)
                GUI.DrawTexture(portraitRect, portrait, ScaleMode.ScaleToFit);
            else
                Widgets.DrawBoxSolid(portraitRect, new Color(0.3f, 0.3f, 0.35f, 1f));
            if (Widgets.ButtonInvisible(portraitCell))
                Find.WindowStack.Add(new Dialog_InfoCard(entry.pawn));
            curX += ColPortrait;

            if (entry.isSlave) GUI.color = PawnNameColorUtility.PawnNameColorOf(entry.pawn);

            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(curX, y, ColName, rowH), entry.nameLabel.Truncate(ColName - 4f));
            if (Widgets.ButtonInvisible(new Rect(curX, y, ColName, rowH)))
                Find.WindowStack.Add(new Dialog_InfoCard(entry.pawn));
            curX += ColName;
            GUI.color = prevGui;

            if (ColOn(PawnRosterColumnIds.Star))
            {
                Rect starCell = new Rect(curX, y, ColStar, rowH);
                EnsureStarHeaderTip();
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.Font = GameFont.Medium;
                GUI.color = entry.isStarred ? new Color(1f, 0.85f, 0.2f) : new Color(0.55f, 0.55f, 0.55f, 0.7f);
                Widgets.Label(starCell, entry.isStarred ? "★" : "☆");
                GUI.color = Color.white;
                Text.Font = GameFont.Tiny;
                TooltipHandler.TipRegion(starCell, starHeaderTip);
                if (Widgets.ButtonInvisible(starCell))
                {
                    WorldComponent_PlayerPawnFavorites.Get()?.Toggle(entry.thingId);
                    entry.isStarred = !entry.isStarred;
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    lastUpdateTick = -9999;
                }
                curX += ColStar;
            }

            if (ColOn(PawnRosterColumnIds.New))
            {
                Rect newCell = new Rect(curX, y, ColNew, rowH);
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.Font = GameFont.Tiny;
                if (entry.daysSinceJoin >= 0)
                {
                    if (PlayerPawnRosterUtility.IsJoinStampRecent(entry.daysSinceJoin))
                        GUI.color = new Color(0.45f, 0.85f, 0.55f);
                    Widgets.Label(newCell, entry.daysSinceJoin.ToString());
                    GUI.color = Color.white;
                }
                curX += ColNew;
            }
            curX += ColPadding;

            if (ColOn(PawnRosterColumnIds.Age))
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(curX, y, ColAge, rowH), entry.ageYears.ToString());
                curX += ColAge;
            }

            if (ColOn(PawnRosterColumnIds.Health))
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(curX, y, ColHealth, rowH),
                    Mathf.RoundToInt(entry.healthPercent).ToString() + "%");
                curX += ColHealth;
            }

            if (ColOn(PawnRosterColumnIds.Traits))
            {
                PrisonerRosterUtility.FormatTraits(entry.pawn, out string traitsDisplay, out string traitsTip);
                Rect traitsRect = new Rect(curX + 2f, y + 2f, ColTraits - 4f, rowH - 4f);
                PrisonerRosterUtility.DrawTraitsCell(traitsRect, traitsDisplay, traitsTip);
                curX += ColTraits;
            }

            if (ColOn(PawnRosterColumnIds.Xenotype))
            {
                PawnRosterTraitFilter.FormatXenotype(entry.pawn, out string xDisplay, out string xTip);
                Rect cell = new Rect(curX + 2f, y + 2f, ColXenotype - 4f, rowH - 4f);
                PrisonerRosterUtility.DrawTraitsCell(cell, xDisplay, xTip);
                curX += ColXenotype;
            }

            if (ColOn(PawnRosterColumnIds.Psycasts))
            {
                PawnRosterTraitFilter.FormatPsycasts(entry.pawn, out string pDisplay, out string pTip);
                Rect cell = new Rect(curX + 2f, y + 2f, ColPsycasts - 4f, rowH - 4f);
                PrisonerRosterUtility.DrawTraitsCell(cell, pDisplay, pTip);
                curX += ColPsycasts;
            }

            if (ColOn(PawnRosterColumnIds.Ideology))
            {
                PawnRosterTraitFilter.FormatIdeology(entry.pawn, out string iDisplay, out string iTip);
                Rect cell = new Rect(curX + 2f, y + 2f, ColIdeology - 4f, rowH - 4f);
                PrisonerRosterUtility.DrawTraitsCell(cell, iDisplay, iTip);
                curX += ColIdeology;
            }

            Text.Anchor = TextAnchor.MiddleLeft;
            int bestLevel = PlayerPawnRosterUtility.GetBestSkillLevel(entry.skillLevels);
            for (int si = 0; si < PlayerPawnRosterUtility.AllSkillColumns.Length; si++)
            {
                SkillDef skill = PlayerPawnRosterUtility.AllSkillColumns[si];
                if (!ColOn(PawnRosterColumnIds.Skill(skill))) continue;
                int level = si < entry.skillLevels.Length ? entry.skillLevels[si] : 0;
                bool isBest = bestLevel > 0 && level == bestLevel;
                PlayerPawnRosterUtility.DrawSkillLevelWithPassion(
                    new Rect(curX, y, ColSkill, rowH), entry.pawn, skill, level, isBest, ColWindow);
                curX += ColSkill;
            }

            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = prevGui;
        }
    }
}
