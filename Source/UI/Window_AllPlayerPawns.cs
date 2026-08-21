using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;
using RimWorld.Planet;
using Verse.Sound;

namespace TSA_WorldDomination
{
    [StaticConstructorOnStartup]
    public class Window_AllPlayerPawns : Window
    {
        private const float RowHeightCompact = 40f;
        private const float RowHeightTall = 58f;
        private const float HeaderHeight = 28f;
        private const float ToolbarHeight = 40f;
        private const int UpdateIntervalTicks = 300;
        private const int PortraitCacheMax = 80;

        private const float LocIconPad = 4f;
        private const float LocIconDrawSize = 40f;
        private const float ColIcon = LocIconDrawSize + LocIconPad * 2f;
        private const float ColLocType = 128f;
        private const float ColLocName = 160f;
        private const float ColSelect = 36f;
        private const float ColPawnType = 96f;
        private const float ColPortrait = 40f;
        private const float ColName = 140f;
        private const float ColStar = 56f;
        private const float ColSkill = 74f;
        private const float ColPadding = 12f;
        private const float TransferBtnWidth = 220f;
        private const float EstablishBtnWidth = 185f;
        private const float SmartSendBtnWidth = 180f;
        private const float SmartAssignConfigBtnSize = 30f;
        private const float SmartAssignConfigGap = 2f;
        private const float SelectedLabelWidth = 130f;
        private const float ToolbarBtnGap = 10f;
        private const float ColAge = 44f;
        private const float ColTraits = 128f;
        private const float ColXenotype = 100f;
        private const float ColPsycasts = 110f;
        private const PawnRosterColumnWindow ColWindow = PawnRosterColumnWindow.AllPlayerPawns;
        private const float ToolbarBtnHeight = 30f;
        private const float SelectedGroupBraceGap = 6f;
        private const float ActionStackTop = 4f;

        private static readonly Vector2 PortraitSize = new Vector2(36f, 36f);
        private static readonly Dictionary<string, Texture> PortraitCache = new Dictionary<string, Texture>();
        private static readonly Texture2D ConfigIcon =
            ContentFinder<Texture2D>.Get("UI/Commands/Config", false)
            ?? TexButton.OpenInspectSettings
            ?? TexButton.Info;

        private Vector2 scrollPos;
        private static bool useDefaultGrouping = true;
        private static string sortColumn = PlayerPawnRosterUtility.DefaultSortColumn;
        private static bool sortAscending = true;
        private static string pawnSearchTerm = "";
        private static string locationNameSearchTerm = "";
        private static string locationTypeSearchTerm = "";
        private static PlayerPawnTypeFilter pawnTypeFilter = PlayerPawnTypeFilter.All;
        private static PlayerPawnStarFilter starFilter = PlayerPawnStarFilter.AllAnywhere;
        private static string xenotypeFilter = "";
        private static string psycastFilter = "";
        private int lastUpdateTick = -9999;
        private List<PlayerPawnRosterEntry> cachedList = new List<PlayerPawnRosterEntry>();
        private readonly HashSet<string> selectedThingIds = new HashSet<string>();
        private static bool _cacheInvalidated;
        private float lastScrollViewportHeight = 400f;
        private static string? _starHeaderTip;

        public override Vector2 InitialSize => new Vector2(UI.screenWidth, UI.screenHeight);

        public Window_AllPlayerPawns()
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
            if (PawnRosterHeaderFilter.TryCloseDropdownOnCancel())
                return;
            if (WdWindowEsc.TryCloseOnCancel(this))
                return;

            if (_cacheInvalidated) { lastUpdateTick = -9999; _cacheInvalidated = false; }
            PawnRosterPaintSelect.BeginFrame(this);

            float totalWidth = ComputeTotalTableWidth();
            float tableRight = Mathf.Min(totalWidth, inRect.width - 5f);

            Text.Font = GameFont.Medium;
            string title = "TSA_WD_AllPlayerPawns_Title".Translate();
            Widgets.Label(new Rect(0f, 0f, inRect.width * 0.35f, 32f), title);

            int selectedCount = selectedThingIds.Count;
            bool anyMovableSelected = false;
            int visibleSelected = 0;
            for (int i = 0; i < cachedList.Count; i++)
            {
                PlayerPawnRosterEntry e = cachedList[i];
                if (e.thingId == null || !selectedThingIds.Contains(e.thingId)) continue;
                visibleSelected++;
                if (e.isMovable) anyMovableSelected = true;
            }
            // Selected pawns hidden by filters still count toward enabling Transfer.
            if (!anyMovableSelected && selectedCount > visibleSelected)
                anyMovableSelected = true;

            Rect transferBtn = new Rect(tableRight - TransferBtnWidth, ActionStackTop, TransferBtnWidth, ToolbarBtnHeight);
            Rect smartConfigBtn = new Rect(
                transferBtn.x - ToolbarBtnGap - SmartAssignConfigBtnSize,
                ActionStackTop,
                SmartAssignConfigBtnSize,
                ToolbarBtnHeight);
            Rect smartSendBtn = new Rect(
                smartConfigBtn.x - SmartAssignConfigGap - SmartSendBtnWidth,
                ActionStackTop,
                SmartSendBtnWidth,
                ToolbarBtnHeight);
            Rect establishBtn = new Rect(
                smartSendBtn.x - ToolbarBtnGap - EstablishBtnWidth,
                ActionStackTop,
                EstablishBtnWidth,
                ToolbarBtnHeight);
            float braceX = establishBtn.x - SelectedGroupBraceGap;
            Color prevLine = GUI.color;
            GUI.color = Color.white;
            Widgets.DrawLineVertical(braceX, ActionStackTop, ToolbarBtnHeight);
            GUI.color = prevLine;
            Rect selectedRect = new Rect(
                braceX - ToolbarBtnGap - SelectedLabelWidth,
                ActionStackTop,
                SelectedLabelWidth,
                ToolbarBtnHeight);

            string selectedLabel = "TSA_WD_AllPlayerPawns_Selected".Translate(selectedCount.ToString());
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(selectedRect, selectedLabel);
            Text.Anchor = TextAnchor.UpperLeft;
            TooltipHandler.TipRegion(selectedRect, "TSA_WD_AllPlayerPawns_SelectedTip".Translate());

            PlayerPawnRosterUtility.DrawRosterViewControls(
                ActionStackTop,
                ToolbarBtnHeight,
                selectedRect.x - ToolbarBtnGap,
                ColWindow,
                RestoreDefaultView,
                () => Find.WindowStack.Add(new Dialog_PawnRosterColumns(ColWindow, OnColumnsChanged)));

            TooltipHandler.TipRegion(smartSendBtn, "TSA_WD_AllPlayerPawns_SmartSendTip".Translate());
            TooltipHandler.TipRegion(smartConfigBtn, "TSA_WD_Prisoners_SmartAssignConfigTip".Translate());
            if (Widgets.ButtonImage(smartConfigBtn, ConfigIcon))
            {
                Find.WindowStack.Add(new Dialog_SmartAssignOutpostFilter());
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            GUI.enabled = anyMovableSelected;
            if (WorldDomination_UIUtils.ButtonTextWithIcon(
                smartSendBtn,
                WorldDomination_UIUtils.RosterSmartIcon,
                "TSA_WD_AllPlayerPawns_SmartSendNow".Translate()))
            {
                SmartAssignSelected();
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            string transferTip = CountDistinctTransferSources() > 1
                ? "TSA_WD_PawnTransfer_MultiSourceTip".Translate()
                : "TSA_WD_AllPlayerPawns_TransferTip".Translate();
            TooltipHandler.TipRegion(transferBtn, transferTip);
            if (WorldDomination_UIUtils.ButtonTextWithIcon(
                transferBtn,
                WorldDomination_UIUtils.RosterTransferIcon,
                "TSA_WD_AllPlayerPawns_Transfer".Translate()))
            {
                var selected = PlayerPawnRosterUtility.ResolveSelectedEntriesIncludingHidden(cachedList, selectedThingIds);
                Find.WindowStack.Add(new Dialog_MovePawnToLocation(selected, () =>
                {
                    selectedThingIds.Clear();
                    lastUpdateTick = -9999;
                }));
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            bool canRemoteEstablish = CanRemoteEstablishSelection(out string establishDisabledTip);
            TooltipHandler.TipRegion(establishBtn,
                canRemoteEstablish
                    ? "TSA_WD_AllPlayerPawns_EstablishOutpostTip".Translate()
                    : (establishDisabledTip ?? "TSA_WD_AllPlayerPawns_EstablishOutpostTip".Translate()));
            GUI.enabled = canRemoteEstablish;
            if (WorldDomination_UIUtils.ButtonTextWithIcon(
                establishBtn,
                WorldDomination_UIUtils.RosterEstablishOutpostIcon,
                "TSA_WD_AllPlayerPawns_EstablishOutpost".Translate()))
            {
                var selected = PlayerPawnRosterUtility.ResolveSelectedEntriesIncludingHidden(cachedList, selectedThingIds);
                Close();
                RemoteOutpostEstablishSession.BeginFromSelection(selected);
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
            GUI.enabled = true;

            float headerTop = ToolbarHeight + 4f;
            float listTop = headerTop + HeaderHeight + 4f;
            float tableHeight = inRect.height - listTop - 8f;

            DrawHorizontallyScrolledSection(
                new Rect(0f, headerTop, inRect.width, HeaderHeight),
                scrollPos.x,
                totalWidth,
                x => DrawTableHeader(x, 0f, totalWidth));
            Widgets.DrawLineHorizontal(0f, headerTop + HeaderHeight, inRect.width);

            if (Find.TickManager.TicksGame >= lastUpdateTick + UpdateIntervalTicks || cachedList.Count == 0)
            {
                cachedList = BuildCurrentRoster(
                    ColOn(PawnRosterColumnIds.Type) ? pawnTypeFilter : PlayerPawnTypeFilter.All);
                PlayerPawnRosterUtility.PruneSelectionToLastScan(selectedThingIds);
                lastUpdateTick = Find.TickManager.TicksGame;
            }

            float totalHeight = cachedList.Count * EffectiveRowHeight() + 8f;
            Rect viewRect = new Rect(0f, 0f, totalWidth, Mathf.Max(totalHeight, tableHeight));
            Rect scrollOuter = new Rect(0f, listTop, inRect.width, tableHeight);
            lastScrollViewportHeight = scrollOuter.height;

            Widgets.BeginScrollView(scrollOuter, ref scrollPos, viewRect);

            for (int i = 0; i < cachedList.Count; i++)
                DrawRow(0f, i * EffectiveRowHeight(), totalWidth, cachedList[i], i % 2 == 0);

            Widgets.EndScrollView();
            Text.Anchor = TextAnchor.UpperLeft;
            PawnRosterHeaderFilter.DrawDropdownIfOpen();
        }

        private void SmartAssignSelected()
        {
            var selected = PlayerPawnRosterUtility.ResolveSelectedEntriesIncludingHidden(cachedList, selectedThingIds);
            var movable = new List<PlayerPawnRosterEntry>();
            for (int i = 0; i < selected.Count; i++)
            {
                if (PlayerPawnTransferUtility.IsMovableTransferEntry(selected[i]))
                    movable.Add(selected[i]);
            }

            var assignments = PlayerPawnRosterUtility.SmartAssignDestinations(movable, out int failed);
            int assigned = assignments.Count;
            if (assigned == 0)
            {
                Messages.Message("TSA_WD_AllPlayerPawns_SmartAssignNone".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (!PlayerPawnTransferUtility.TryTransferWithPerPawnDestinations(assignments))
                return;

            selectedThingIds.Clear();
            lastUpdateTick = -9999;

            if (failed == 0)
            {
                Messages.Message(
                    "TSA_WD_AllPlayerPawns_SmartAssignDone".Translate(assigned.ToString()),
                    MessageTypeDefOf.TaskCompletion,
                    false);
            }
            else
            {
                Messages.Message(
                    "TSA_WD_AllPlayerPawns_SmartAssignPartial".Translate(assigned.ToString(), failed.ToString()),
                    MessageTypeDefOf.NeutralEvent,
                    false);
            }
        }

        private List<PlayerPawnRosterEntry> BuildCurrentRoster(
            PlayerPawnTypeFilter? typeF = null,
            PlayerPawnStarFilter? starF = null,
            bool applyXenotype = true,
            bool applyLocationType = true,
            bool applyPsycast = true)
        {
            string? pawnSearchLower = string.IsNullOrEmpty(pawnSearchTerm) ? null : pawnSearchTerm.ToLowerInvariant();
            string? locNameLower = string.IsNullOrEmpty(locationNameSearchTerm) ? null : locationNameSearchTerm.ToLowerInvariant();
            string? locTypeLower = applyLocationType && !string.IsNullOrEmpty(locationTypeSearchTerm)
                ? locationTypeSearchTerm.ToLowerInvariant()
                : null;
            PlayerPawnTypeFilter type = typeF ?? (ColOn(PawnRosterColumnIds.Type) ? pawnTypeFilter : PlayerPawnTypeFilter.All);
            PlayerPawnStarFilter star = starF ?? (ColOn(PawnRosterColumnIds.Star) ? starFilter : PlayerPawnStarFilter.AllAnywhere);
            var list = PlayerPawnRosterUtility.BuildRoster(
                pawnSearchLower, locNameLower, locTypeLower, null,
                useDefaultGrouping, sortColumn, sortAscending, star, type);
            PawnRosterTraitFilter.ApplyToPlayerRows(list, ColWindow);
            if (applyXenotype && ColOn(PawnRosterColumnIds.Xenotype))
                PawnRosterTraitFilter.ApplyXenotypeToPlayerRows(list, xenotypeFilter);
            if (applyPsycast && ColOn(PawnRosterColumnIds.Psycasts))
                PawnRosterTraitFilter.ApplyPsycastToPlayerRows(list, psycastFilter);
            return list;
        }

        private static void DrawHorizontallyScrolledSection(Rect viewport, float scrollX, float contentWidth, Action<float> draw)
        {
            GUI.BeginGroup(viewport);
            draw(-scrollX);
            GUI.EndGroup();
        }

        private void OnColumnsChanged()
        {
            if (!ColOn(PawnRosterColumnIds.Type) && sortColumn == "PawnType")
                ClearSortToDefault();
            else if (!ColOn(PawnRosterColumnIds.Star) && sortColumn == "Starred")
                ClearSortToDefault();
            else if (!ColOn(PawnRosterColumnIds.Age) && sortColumn == "Age")
                ClearSortToDefault();
            else if (!ColOn(PawnRosterColumnIds.Traits) && sortColumn == "Traits")
                ClearSortToDefault();
            else if (!ColOn(PawnRosterColumnIds.Xenotype) && sortColumn == "Xenotype")
                ClearSortToDefault();
            else if (!ColOn(PawnRosterColumnIds.Psycasts) && sortColumn == "Psycasts")
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
        }

        private static bool ColOn(string id) => PlayerPawnRosterUtility.ColVisible(ColWindow, id);

        private void RestoreDefaultView()
        {
            useDefaultGrouping = true;
            sortColumn = PlayerPawnRosterUtility.DefaultSortColumn;
            sortAscending = true;
            pawnSearchTerm = "";
            locationNameSearchTerm = "";
            locationTypeSearchTerm = "";
            pawnTypeFilter = PlayerPawnTypeFilter.All;
            starFilter = PlayerPawnStarFilter.AllAnywhere;
            xenotypeFilter = "";
            psycastFilter = "";
            scrollPos = Vector2.zero;
            lastUpdateTick = -9999;
            PlayerPawnRosterUtility.ResetSkillDisplayOptions(ColWindow);
            WorldComponent_PawnRosterColumnPrefs.Get()?.ResetToDefaults(ColWindow);
            PawnRosterTraitFilter.Clear();
            PawnRosterHeaderFilter.CloseDropdown();
        }

        private static float EffectiveRowHeight()
        {
            if (ColOn(PawnRosterColumnIds.Traits) || ColOn(PawnRosterColumnIds.Psycasts))
                return RowHeightTall;
            return RowHeightCompact;
        }

        private bool CanRemoteEstablishSelection(out string disabledTip)
        {
            disabledTip = null;
            if (selectedThingIds.Count == 0)
            {
                RemoteOutpostEstablishUtility.TryValidateColonySelection(
                    new List<PlayerPawnRosterEntry>(), out _, out _, out disabledTip);
                return false;
            }

            // Avoid rebuilding the full roster every frame when filters hide part of the selection.
            var visible = PlayerPawnRosterUtility.ResolveSelectedEntries(cachedList, selectedThingIds);
            if (visible.Count < selectedThingIds.Count)
            {
                if (visible.Count == 0) return true;
                return RemoteOutpostEstablishUtility.TryValidateColonySelection(visible, out _, out _, out disabledTip);
            }
            return RemoteOutpostEstablishUtility.TryValidateColonySelection(visible, out _, out _, out disabledTip);
        }

        private int CountDistinctTransferSources()
        {
            var outposts = new HashSet<WorldObject_WD_Outpost>();
            var colonies = new HashSet<MapParent>();
            for (int i = 0; i < cachedList.Count; i++)
            {
                PlayerPawnRosterEntry e = cachedList[i];
                if (e.thingId == null || !selectedThingIds.Contains(e.thingId) || !e.isMovable) continue;
                if (e.sourceOutpost != null)
                    outposts.Add(e.sourceOutpost);
                else if (e.mapParent != null)
                    colonies.Add(e.mapParent);
            }
            return outposts.Count + colonies.Count;
        }

        private float ComputeTotalTableWidth()
        {
            float w = ColIcon + ColLocType + ColLocName + ColSelect + ColPortrait + ColName;
            if (ColOn(PawnRosterColumnIds.Type)) w += ColPawnType;
            if (ColOn(PawnRosterColumnIds.Star)) w += ColStar;
            w += ColPadding;
            if (ColOn(PawnRosterColumnIds.Age)) w += ColAge;
            if (ColOn(PawnRosterColumnIds.Traits)) w += ColTraits;
            if (ColOn(PawnRosterColumnIds.Xenotype)) w += ColXenotype;
            if (ColOn(PawnRosterColumnIds.Psycasts)) w += ColPsycasts;
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
            if (_starHeaderTip == null)
                _starHeaderTip = "TSA_WD_AllPlayerPawns_StarTip".Translate();
        }

        private void DrawTableHeader(float x, float y, float width)
        {
            EnsureStarHeaderTip();
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
                    _starHeaderTip,
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
            curX += ColPadding;

            if (ColOn(PawnRosterColumnIds.Age))
            {
                Rect ageHdr = new Rect(curX, hRect.y, ColAge, hRect.height);
                DrawHeader(ref curX, ColAge, "TSA_WD_PawnRoster_ColAge".Translate(), "Age", hRect);
                TooltipHandler.TipRegion(ageHdr, "TSA_WD_PawnRoster_ColAgeTip".Translate());
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

            SkillDef[] skills = PlayerPawnRosterUtility.AllSkillColumns;
            for (int i = 0; i < skills.Length; i++)
            {
                if (!ColOn(PawnRosterColumnIds.Skill(skills[i]))) continue;
                DrawHeader(ref curX, ColSkill, skills[i].LabelCap, skills[i].defName, hRect);
            }

            GUI.color = Color.white;
        }

        private void DrawHeader(ref float curX, float width, string label, string tag, Rect hRect)
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
                    if (!cachedList[i].isMovable) continue;
                    string tid = cachedList[i].thingId;
                    if (!tid.NullOrEmpty())
                        selectedThingIds.Add(tid);
                }
            }
        }

        private void SetSort(string col)
        {
            useDefaultGrouping = false;
            if (sortColumn == col) sortAscending = !sortAscending;
            else { sortColumn = col; sortAscending = true; }
            lastUpdateTick = -9999;
            SoundDefOf.Click.PlayOneShotOnCamera();
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
                if (Widgets.ButtonInvisible(iconRect))
                    JumpToLocation(entry);
            }
            curX += ColIcon;

            Rect locTypeRect = new Rect(curX, y, ColLocType, rowH);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(locTypeRect, entry.locationTypeLabel.Truncate(ColLocType - 4f));
            TooltipHandler.TipRegion(locTypeRect, entry.locationTypeLabel);
            if (Widgets.ButtonInvisible(locTypeRect))
                JumpToLocation(entry);
            curX += ColLocType;

            Rect locNameRect = new Rect(curX, y, ColLocName, rowH);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(locNameRect, entry.locationLabel.Truncate(ColLocName - 4f));
            TooltipHandler.TipRegion(locNameRect, "TSA_WD_AllPlayerPawns_JumpTip".Translate(entry.locationLabel));
            if (Widgets.ButtonInvisible(locNameRect))
                JumpToLocation(entry);
            curX += ColLocName;

            Rect selRect = new Rect(curX, y, ColSelect, rowH);
            if (entry.isMovable && entry.sourceOutpost != null)
            {
                bool canInteract = OutpostPawnIdeologyUtil.BulkRemovalSelectionIsAllowedWithExtra(
                    entry.sourceOutpost,
                    selectedThingIds,
                    entry.pawn);
                float cx = curX + (ColSelect - 24f) * 0.5f;
                float cy = y + (rowH - 24f) * 0.5f;
                if (!selectedThingIds.Contains(entry.thingId) && !canInteract)
                    TooltipHandler.TipRegion(selRect, "TSA_WD_Pawns_RemoveSlaveAccompanimentRequiredTip".Translate());
                PawnRosterPaintSelect.Draw(this, selRect, cx, cy, 24f, entry.thingId, selectedThingIds, canInteract);
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
            Texture? portrait = PawnPortraitUIUtils.GetPortrait(
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
                TooltipHandler.TipRegion(starCell, _starHeaderTip);
                if (Widgets.ButtonInvisible(starCell))
                {
                    WorldComponent_PlayerPawnFavorites.Get()?.Toggle(entry.thingId);
                    entry.isStarred = !entry.isStarred;
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    lastUpdateTick = -9999;
                }
                curX += ColStar;
            }
            curX += ColPadding;

            if (ColOn(PawnRosterColumnIds.Age))
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(curX, y, ColAge, rowH), entry.ageYears.ToString());
                curX += ColAge;
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

        private void JumpToLocation(PlayerPawnRosterEntry entry)
        {
            if (!entry.jumpTarget.IsValid) return;
            CameraJumper.TryJump(entry.jumpTarget);
            Find.WorldSelector.ClearSelection();
            if (entry.jumpTarget.WorldObject != null)
                Find.WorldSelector.Select(entry.jumpTarget.WorldObject);
            if (Find.MainTabsRoot.OpenTab != null)
                Find.MainTabsRoot.EscapeCurrentTab();
            SoundDefOf.Click.PlayOneShotOnCamera();
            Close();
        }
    }
}
