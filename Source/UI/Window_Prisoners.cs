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
    public class Window_Prisoners : Window
    {
        private const float RowHeight = 58f;
        private const float HeaderHeight = 28f;
        private const float ToolbarHeight = 40f;
        private const float GroupHeaderHeight = 28f;
        private const int UpdateIntervalTicks = 300;
        private const int PortraitCacheMax = 80;

        private const float ColLocIcon = 36f;
        private const float ColLocName = 140f;
        private const float ColSelect = 32f;
        private const float ColReorder = 28f;
        private const float ColPortrait = 40f;
        private const float ColName = 150f;
        private const float ColInteraction = 130f;
        private const float ColResistance = 69f;
        private const float ColTraits = PawnRosterTraitFilter.ColWidth;
        private const float ColXenotype = 100f;
        private const float ColPsycasts = 110f;
        private const float ColSkill = 60f;
        private const float ColDest = 230f;
        private const float LocIconDrawSize = 28f;
        private const float SetDestBtnWidth = 160f;
        private const float KickOutBtnWidth = 140f;
        private const float ClearDestBtnWidth = 160f;
        private const float SmartAssignBtnWidth = 140f;
        private const float SmartAssignConfigBtnSize = 30f;
        private const float SmartAssignConfigGap = 1f;
        private const float SelectedLabelWidth = 130f;
        private const float ToolbarBtnGap = 10f;
        private const float ColAge = 44f;
        private const PawnRosterColumnWindow ColWindow = PawnRosterColumnWindow.Prisoners;

        private static readonly Vector2 PortraitSize = new Vector2(36f, 36f);
        private static readonly Dictionary<string, Texture> PortraitCache = new Dictionary<string, Texture>();
        private static readonly Texture2D ConfigIcon =
            ContentFinder<Texture2D>.Get("UI/Commands/Config", false)
            ?? TexButton.OpenInspectSettings
            ?? TexButton.Info;
        private static readonly Texture2D CancelIcon =
            ContentFinder<Texture2D>.Get("UI/Designators/Cancel", false)
            ?? TexButton.Delete;
        private const float DestIconSize = 28f;
        private static readonly Color RecruitingRowTint = new Color(1f, 0.55f, 0.15f, 0.21f);
        private static readonly Color KickOutFill = new Color(0.38f, 0.12f, 0.14f, 0.92f);

        private Vector2 scrollPos;
        private static string sortColumn = PrisonerRosterUtility.DefaultSortColumn;
        private static bool sortAscending = true;
        private static string pawnSearchTerm = "";
        private static PrisonerRosterSourceFilter sourceFilter = PrisonerRosterSourceFilter.All;
        private static string xenotypeFilter = "";
        private static string psycastFilter = "";
        private int lastUpdateTick = -9999;
        private List<PrisonerRosterEntry> cachedList = new List<PrisonerRosterEntry>();
        private readonly HashSet<string> selectedThingIds = new HashSet<string>();
        private static bool cacheInvalidated;
        private float lastScrollViewportHeight = 400f;

        public override Vector2 InitialSize => new Vector2(UI.screenWidth, UI.screenHeight);

        public Window_Prisoners()
        {
            doCloseX = true;
            closeOnCancel = true;
            draggable = false;
            preventCameraMotion = false;
            forcePause = false;
        }

        public static void InvalidateCache()
        {
            cacheInvalidated = true;
            WITab_Outpost_Pawns.InvalidateCache();
        }

        public override void DoWindowContents(Rect inRect)
        {
            WdNavWindows.ProcessHotkeys();
            if (!IsOpen) return;
            if (PawnRosterHeaderFilter.TryCloseDropdownOnCancel())
                return;
            if (WdWindowEsc.TryCloseOnCancel(this))
                return;

            if (cacheInvalidated) { lastUpdateTick = -9999; cacheInvalidated = false; }
            PawnRosterPaintSelect.BeginFrame(this);

            if (Find.TickManager.TicksGame >= lastUpdateTick + UpdateIntervalTicks || cachedList.Count == 0)
            {
                cachedList = BuildCurrentRoster(sourceFilter, applyXenotype: true);
                PrisonerRosterUtility.PruneSelectionToLastScan(selectedThingIds);
                lastUpdateTick = Find.TickManager.TicksGame;
            }

            float totalWidth = ComputeTotalTableWidth();
            float tableRight = Mathf.Min(totalWidth, inRect.width - 12f);

            Text.Font = GameFont.Medium;
            string title = "TSA_WD_Prisoners_Title".Translate();
            Widgets.Label(new Rect(0f, 0f, inRect.width * 0.35f, 32f), title);

            int selectedCount = selectedThingIds.Count;
            bool anySelected = selectedCount > 0;

            Rect kickOutBtn = new Rect(tableRight - KickOutBtnWidth, 4f, KickOutBtnWidth, 30f);
            Rect setDestBtn = new Rect(kickOutBtn.x - ToolbarBtnGap - SetDestBtnWidth, 4f, SetDestBtnWidth, 30f);
            Rect smartConfigBtn = new Rect(setDestBtn.x - ToolbarBtnGap - SmartAssignConfigBtnSize, 4f, SmartAssignConfigBtnSize, 30f);
            Rect smartAssignBtn = new Rect(smartConfigBtn.x - SmartAssignConfigGap - SmartAssignBtnWidth, 4f, SmartAssignBtnWidth, 30f);
            Rect clearDestBtn = new Rect(smartAssignBtn.x - ToolbarBtnGap - ClearDestBtnWidth, 4f, ClearDestBtnWidth, 30f);

            string selectedLabel = "TSA_WD_AllPlayerPawns_Selected".Translate(selectedCount.ToString());
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleRight;
            Rect selectedRect = new Rect(clearDestBtn.x - ToolbarBtnGap - SelectedLabelWidth, 6f, SelectedLabelWidth, 28f);
            Widgets.Label(selectedRect, selectedLabel);
            Text.Anchor = TextAnchor.UpperLeft;

            PlayerPawnRosterUtility.DrawRosterViewControls(
                4f,
                30f,
                selectedRect.x - ToolbarBtnGap,
                ColWindow,
                RestoreDefaultView,
                () => Find.WindowStack.Add(new Dialog_PawnRosterColumns(ColWindow, OnColumnsChanged)));

            TooltipHandler.TipRegion(setDestBtn, "TSA_WD_Prisoners_SetDestinationTip".Translate());
            TooltipHandler.TipRegion(smartAssignBtn, "TSA_WD_Prisoners_SmartAssignTip".Translate());
            TooltipHandler.TipRegion(smartConfigBtn, "TSA_WD_Prisoners_SmartAssignConfigTip".Translate());
            TooltipHandler.TipRegion(clearDestBtn, "TSA_WD_Prisoners_ClearDestinationTip".Translate());
            TooltipHandler.TipRegion(kickOutBtn, "TSA_WD_Prisoners_LetGoSelectedTip".Translate());

            Texture2D configIcon = ConfigIcon;
            if (Widgets.ButtonImage(smartConfigBtn, configIcon))
            {
                Find.WindowStack.Add(new Dialog_SmartAssignOutpostFilter());
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            GUI.enabled = anySelected;
            if (WorldDomination_UIUtils.ButtonTextWithIcon(
                setDestBtn,
                WorldDomination_UIUtils.RosterTransferIcon,
                "TSA_WD_Prisoners_SetDestination".Translate()))
            {
                OpenScheduleDialog(new List<string>(selectedThingIds));
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
            if (WorldDomination_UIUtils.ButtonTextWithIcon(
                smartAssignBtn,
                WorldDomination_UIUtils.RosterSmartIcon,
                "TSA_WD_Prisoners_SmartAssign".Translate()))
            {
                SmartAssignSelected();
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
            if (WorldDomination_UIUtils.ButtonTextWithIcon(
                clearDestBtn,
                CancelIcon,
                "TSA_WD_Prisoners_ClearDestination".Translate()))
            {
                WorldComponent_PrisonerRecruitSchedule.Get()?.ClearMany(selectedThingIds);
                lastUpdateTick = -9999;
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
            if (WorldDomination_UIUtils.ButtonTextWithIcon(
                kickOutBtn,
                WorldDomination_UIUtils.RosterKickOutIcon,
                "TSA_WD_Prisoners_LetGoSelected".Translate(),
                fill: KickOutFill))
            {
                ConfirmLetGoSelectedOutpostPrisoners();
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
            GUI.enabled = true;

            float headerTop = ToolbarHeight + 4f;
            float listTop = headerTop + HeaderHeight + 4f;
            float tableHeight = inRect.height - listTop - 8f;

            DrawHorizontallyScrolledSection(
                new Rect(0f, headerTop, inRect.width - 8f, HeaderHeight),
                scrollPos.x,
                totalWidth,
                x => DrawTableHeader(x, 0f, totalWidth));
            Widgets.DrawLineHorizontal(0f, headerTop + HeaderHeight, inRect.width - 8f);

            float totalHeight = 8f;
            for (int i = 0; i < cachedList.Count; i++)
                totalHeight += cachedList[i].isGroupHeader ? GroupHeaderHeight : RowHeight;

            Rect viewRect = new Rect(0f, 0f, totalWidth, Mathf.Max(totalHeight, tableHeight));
            Rect scrollOuter = new Rect(0f, listTop, inRect.width - 8f, tableHeight);
            lastScrollViewportHeight = scrollOuter.height;

            Widgets.BeginScrollView(scrollOuter, ref scrollPos, viewRect);
            float y = 0f;
            for (int i = 0; i < cachedList.Count; i++)
            {
                PrisonerRosterEntry entry = cachedList[i];
                if (entry.isGroupHeader)
                {
                    DrawGroupHeader(0f, y, totalWidth, entry.groupHeaderLabel);
                    y += GroupHeaderHeight;
                }
                else
                {
                    DrawRow(0f, y, totalWidth, entry, i % 2 == 0);
                    y += RowHeight;
                }
            }
            Widgets.EndScrollView();
            Text.Anchor = TextAnchor.UpperLeft;
            PawnRosterHeaderFilter.DrawDropdownIfOpen();
        }

        private static void DrawHorizontallyScrolledSection(Rect viewport, float scrollX, float contentWidth, Action<float> draw)
        {
            GUI.BeginGroup(viewport);
            draw(-scrollX);
            GUI.EndGroup();
        }

        private void RestoreDefaultView()
        {
            sortColumn = PrisonerRosterUtility.DefaultSortColumn;
            sortAscending = true;
            pawnSearchTerm = "";
            sourceFilter = PrisonerRosterSourceFilter.All;
            xenotypeFilter = "";
            psycastFilter = "";
            scrollPos = Vector2.zero;
            lastUpdateTick = -9999;
            PlayerPawnRosterUtility.ResetSkillDisplayOptions(ColWindow);
            WorldComponent_PawnRosterColumnPrefs.Get()?.ResetToDefaults(ColWindow);
            PawnRosterTraitFilter.Clear();
            PawnRosterHeaderFilter.CloseDropdown();
        }

        private void OnColumnsChanged()
        {
            if (!ColOn(PawnRosterColumnIds.Interaction) && sortColumn == "Interaction")
                ClearSortToDefault();
            else if (!ColOn(PawnRosterColumnIds.Resistance) && sortColumn == "Resistance")
                ClearSortToDefault();
            else if (!ColOn(PawnRosterColumnIds.Traits) && sortColumn == "Traits")
                ClearSortToDefault();
            else if (!ColOn(PawnRosterColumnIds.Xenotype) && sortColumn == "Xenotype")
                ClearSortToDefault();
            else if (!ColOn(PawnRosterColumnIds.Psycasts) && sortColumn == "Psycasts")
                ClearSortToDefault();
            else if (!ColOn(PawnRosterColumnIds.Age) && sortColumn == "Age")
                ClearSortToDefault();
            else if (!ColOn(PawnRosterColumnIds.Destination) && sortColumn == "Destination")
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

        private static void ClearSortToDefault()
        {
            sortColumn = PrisonerRosterUtility.DefaultSortColumn;
            sortAscending = true;
        }

        private static bool ColOn(string id) => PlayerPawnRosterUtility.ColVisible(ColWindow, id);

        private static float ComputeTotalTableWidth()
        {
            float w = ColLocIcon + ColLocName + ColSelect + ColReorder + ColPortrait + ColName;
            if (ColOn(PawnRosterColumnIds.Interaction)) w += ColInteraction;
            if (ColOn(PawnRosterColumnIds.Resistance)) w += ColResistance;
            if (ColOn(PawnRosterColumnIds.Traits)) w += ColTraits;
            if (ColOn(PawnRosterColumnIds.Xenotype)) w += ColXenotype;
            if (ColOn(PawnRosterColumnIds.Psycasts)) w += ColPsycasts;
            if (ColOn(PawnRosterColumnIds.Age)) w += ColAge;
            SkillDef[] skills = PlayerPawnRosterUtility.AllSkillColumns;
            for (int i = 0; i < skills.Length; i++)
            {
                if (ColOn(PawnRosterColumnIds.Skill(skills[i])))
                    w += ColSkill;
            }
            if (ColOn(PawnRosterColumnIds.Destination)) w += ColDest;
            return w;
        }

        private List<PrisonerRosterEntry> BuildCurrentRoster(
            PrisonerRosterSourceFilter source,
            bool applyXenotype,
            bool applyPsycast = true)
        {
            string nameSearchLower = string.IsNullOrEmpty(pawnSearchTerm) ? null : pawnSearchTerm.ToLowerInvariant();
            var list = PrisonerRosterUtility.BuildRoster(nameSearchLower, sortColumn, sortAscending, source);
            PawnRosterTraitFilter.ApplyToPrisonerRows(list);
            if (applyXenotype && ColOn(PawnRosterColumnIds.Xenotype))
                PawnRosterTraitFilter.ApplyXenotypeToPrisonerRows(list, xenotypeFilter);
            if (applyPsycast && ColOn(PawnRosterColumnIds.Psycasts))
                PawnRosterTraitFilter.ApplyPsycastToPrisonerRows(list, psycastFilter);
            return list;
        }

        private static List<List<string>> PsycastListsFromPrisoners(IReadOnlyList<PrisonerRosterEntry> rows)
        {
            var list = new List<List<string>>(rows?.Count ?? 0);
            if (rows == null) return list;
            for (int i = 0; i < rows.Count; i++)
            {
                PrisonerRosterEntry e = rows[i];
                if (e == null || e.isGroupHeader) continue;
                list.Add(PawnRosterHeaderFilter.PsycastKeysOnPawn(e.pawn));
            }
            return list;
        }

        private static List<bool> SourceFlagsFrom(IReadOnlyList<PrisonerRosterEntry> rows)
        {
            var flags = new List<bool>(rows?.Count ?? 0);
            if (rows == null) return flags;
            for (int i = 0; i < rows.Count; i++)
            {
                PrisonerRosterEntry e = rows[i];
                if (e == null || e.isGroupHeader) continue;
                flags.Add(e.isOutpostPrisoner);
            }
            return flags;
        }

        private static List<string> XenotypeKeysFromPrisoners(IReadOnlyList<PrisonerRosterEntry> rows)
        {
            var keys = new List<string>(rows?.Count ?? 0);
            if (rows == null) return keys;
            for (int i = 0; i < rows.Count; i++)
            {
                PrisonerRosterEntry e = rows[i];
                if (e == null || e.isGroupHeader) continue;
                keys.Add(PawnRosterHeaderFilter.XenotypeKey(e.pawn));
            }
            return keys;
        }

        private void DrawTableHeader(float x, float y, float width)
        {
            float curX = x;
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Rect hRect = new Rect(x, y, width, HeaderHeight);

            curX += ColLocIcon;
            PawnRosterHeaderFilter.DrawFilterableHeader(
                ref curX, hRect.y, ColLocName, HeaderHeight,
                "TSA_WD_AllPlayerPawns_ColLocation".Translate(),
                sortColumn == "Location", sortAscending,
                TextAnchor.MiddleLeft,
                sourceFilter != PrisonerRosterSourceFilter.All,
                "TSA_WD_FilterBySource".Translate(),
                icon => PawnRosterHeaderFilter.OpenChoiceDropdown(
                    icon,
                    "TSA_WD_FilterBySource".Translate(),
                    PawnRosterHeaderFilter.PrisonerSourceChoices(sourceFilter, f =>
                    {
                        sourceFilter = f;
                        lastUpdateTick = -9999;
                    }, SourceFlagsFrom(BuildCurrentRoster(PrisonerRosterSourceFilter.All, applyXenotype: true)))),
                () => SetSort("Location"));
            DrawSelectAllHeader(ref curX, hRect);
            curX += ColReorder;
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
            if (ColOn(PawnRosterColumnIds.Interaction))
                DrawHeader(ref curX, ColInteraction, "TSA_WD_Prisoners_ColInteraction".Translate(), "Interaction", hRect, TextAnchor.MiddleCenter);
            if (ColOn(PawnRosterColumnIds.Resistance))
                DrawHeader(ref curX, ColResistance, "TSA_WD_Prisoners_ColResistance".Translate(), "Resistance", hRect, TextAnchor.MiddleCenter);
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
                        }, XenotypeKeysFromPrisoners(BuildCurrentRoster(sourceFilter, applyXenotype: false)))),
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
                        }, PsycastListsFromPrisoners(BuildCurrentRoster(sourceFilter, applyXenotype: true, applyPsycast: false)))),
                    () => SetSort("Psycasts"));
            }
            if (ColOn(PawnRosterColumnIds.Age))
            {
                Rect ageHdr = new Rect(curX, hRect.y, ColAge, hRect.height);
                DrawHeader(ref curX, ColAge, "TSA_WD_PawnRoster_ColAge".Translate(), "Age", hRect, TextAnchor.MiddleCenter);
                TooltipHandler.TipRegion(ageHdr, "TSA_WD_PawnRoster_ColAgeTip".Translate());
            }

            SkillDef[] skills = PlayerPawnRosterUtility.AllSkillColumns;
            for (int i = 0; i < skills.Length; i++)
            {
                if (!ColOn(PawnRosterColumnIds.Skill(skills[i]))) continue;
                Rect skillHdr = new Rect(curX, hRect.y, ColSkill, hRect.height);
                DrawHeader(ref curX, ColSkill, skills[i].LabelCap, skills[i].defName, hRect, TextAnchor.MiddleCenter);
                TooltipHandler.TipRegion(skillHdr, skills[i].LabelCap);
            }

            if (ColOn(PawnRosterColumnIds.Destination))
                DrawHeader(ref curX, ColDest, "TSA_WD_Prisoners_ColDestination".Translate(), "Destination", hRect, TextAnchor.MiddleLeft);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
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

            TooltipHandler.TipRegion(selHdr, "TSA_WD_Prisoners_SelectColumnTip".Translate());
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
                if (!cachedList[i].isGroupHeader && !cachedList[i].thingId.NullOrEmpty())
                    count++;
            }
            return count;
        }

        private bool AreAllVisibleSelected()
        {
            int visible = 0;
            for (int i = 0; i < cachedList.Count; i++)
            {
                if (cachedList[i].isGroupHeader) continue;
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
                    if (cachedList[i].isGroupHeader) continue;
                    string tid = cachedList[i].thingId;
                    if (!tid.NullOrEmpty())
                        selectedThingIds.Remove(tid);
                }
            }
            else
            {
                for (int i = 0; i < cachedList.Count; i++)
                {
                    if (cachedList[i].isGroupHeader) continue;
                    string tid = cachedList[i].thingId;
                    if (!tid.NullOrEmpty())
                        selectedThingIds.Add(tid);
                }
            }
        }

        private void DrawHeader(ref float curX, float width, string label, string tag, Rect hRect, TextAnchor anchor)
        {
            Rect headerRect = new Rect(curX, hRect.y, width, hRect.height);
            if (Mouse.IsOver(headerRect)) Widgets.DrawHighlight(headerRect);
            Text.Anchor = anchor;
            string headerText = label + (sortColumn == tag ? (sortAscending ? " ▲" : " ▼") : "");
            Widgets.Label(headerRect, headerText.Truncate(width - 4f));
            if (Widgets.ButtonInvisible(headerRect)) SetSort(tag);
            curX += width;
        }

        private void SetSort(string col)
        {
            if (sortColumn == col) sortAscending = !sortAscending;
            else { sortColumn = col; sortAscending = true; }
            lastUpdateTick = -9999;
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        private void DrawGroupHeader(float x, float y, float width, string label)
        {
            GUI.color = Color.white;
            Widgets.DrawLineHorizontal(x, y, width);
            Widgets.DrawLineHorizontal(x, y + GroupHeaderHeight - 1f, width);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.yellow;
            Rect headerRect = new Rect(x + 8f, y, width - 16f, GroupHeaderHeight);
            Widgets.Label(headerRect, label);
            TooltipHandler.TipRegion(headerRect, "TSA_WD_Prisoners_GroupOutpostTip".Translate());
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawRow(float x, float y, float width, PrisonerRosterEntry entry, bool zebra)
        {
            float visibleY = scrollPos.y - RowHeight;
            float visibleYMax = scrollPos.y + lastScrollViewportHeight;
            if (y < visibleY || y >= visibleYMax)
                return;

            Rect row = new Rect(x, y, width, RowHeight);
            if (zebra) Widgets.DrawHighlight(row);
            if (Mouse.IsOver(row)) Widgets.DrawLightHighlight(row);
            if (entry.isBeingRecruited)
                Widgets.DrawBoxSolid(row, RecruitingRowTint);

            float curX = x;

            if (entry.locationIcon != null)
            {
                float iconY = y + (RowHeight - LocIconDrawSize) * 0.5f;
                Rect iconRect = new Rect(curX + 4f, iconY, LocIconDrawSize, LocIconDrawSize);
                GUI.color = entry.locationIconColor;
                GUI.DrawTexture(iconRect, entry.locationIcon, ScaleMode.ScaleToFit);
                GUI.color = Color.white;
                TooltipHandler.TipRegion(iconRect, entry.locationLabel);
                if (Widgets.ButtonInvisible(iconRect))
                    JumpToLocation(entry);
            }
            curX += ColLocIcon;

            Rect locNameRect = new Rect(curX, y, ColLocName, RowHeight);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(locNameRect, entry.locationLabel.Truncate(ColLocName - 4f));
            TooltipHandler.TipRegion(locNameRect, "TSA_WD_AllPlayerPawns_JumpTip".Translate(entry.locationLabel));
            if (Widgets.ButtonInvisible(locNameRect))
                JumpToLocation(entry);
            curX += ColLocName;

            Rect selRect = new Rect(curX, y, ColSelect, RowHeight);
            float cx = curX + (ColSelect - 24f) * 0.5f;
            float cy = y + (RowHeight - 24f) * 0.5f;
            PawnRosterPaintSelect.Draw(this, selRect, cx, cy, 24f, entry.thingId, selectedThingIds, canInteract: true);
            curX += ColSelect;

            DrawPrisonerQueueButtons(ref curX, y, entry);

            Rect portraitCell = new Rect(curX, y, ColPortrait, RowHeight);
            Texture portrait = PawnPortraitUIUtils.GetPortrait(
                entry.pawn,
                PawnPortraitUIUtils.BuildCacheKey(entry.pawn),
                PortraitSize,
                PortraitCache,
                PortraitCacheMax);
            Rect portraitRect = new Rect(portraitCell.x + (portraitCell.width - PortraitSize.x) / 2f,
                y + (RowHeight - PortraitSize.y) / 2f, PortraitSize.x, PortraitSize.y);
            if (portrait != null)
                GUI.DrawTexture(portraitRect, portrait, ScaleMode.ScaleToFit);
            else
                Widgets.DrawBoxSolid(portraitRect, new Color(0.3f, 0.3f, 0.35f, 1f));
            if (Widgets.ButtonInvisible(portraitCell))
                Find.WindowStack.Add(new Dialog_InfoCard(entry.pawn));
            curX += ColPortrait;

            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(curX, y, ColName, RowHeight), entry.nameLabel.Truncate(ColName - 4f));
            if (Widgets.ButtonInvisible(new Rect(curX, y, ColName, RowHeight)))
                Find.WindowStack.Add(new Dialog_InfoCard(entry.pawn));
            curX += ColName;

            if (ColOn(PawnRosterColumnIds.Interaction))
            {
                Rect interactRect = new Rect(curX + 2f, y + 10f, ColInteraction - 4f, RowHeight - 20f);
                DrawInteractionButton(interactRect, entry);
                curX += ColInteraction;
            }

            if (ColOn(PawnRosterColumnIds.Resistance))
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.Font = GameFont.Tiny;
                Rect resistRect = new Rect(curX, y, ColResistance, RowHeight);
                Widgets.Label(resistRect, entry.resistanceLabel);
                if (!entry.resistanceTip.NullOrEmpty())
                    TooltipHandler.TipRegion(resistRect, entry.resistanceTip);
                curX += ColResistance;
                Text.Font = GameFont.Small;
            }

            if (ColOn(PawnRosterColumnIds.Traits))
            {
                Rect traitsRect = new Rect(curX + 2f, y + 2f, ColTraits - 4f, RowHeight - 4f);
                PrisonerRosterUtility.DrawTraitsCell(traitsRect, entry.traitsDisplay, entry.traitsTip);
                curX += ColTraits;
            }

            if (ColOn(PawnRosterColumnIds.Xenotype))
            {
                PawnRosterTraitFilter.FormatXenotype(entry.pawn, out string xDisplay, out string xTip);
                Rect cell = new Rect(curX + 2f, y + 2f, ColXenotype - 4f, RowHeight - 4f);
                PrisonerRosterUtility.DrawTraitsCell(cell, xDisplay, xTip);
                curX += ColXenotype;
            }

            if (ColOn(PawnRosterColumnIds.Psycasts))
            {
                PawnRosterTraitFilter.FormatPsycasts(entry.pawn, out string pDisplay, out string pTip);
                Rect cell = new Rect(curX + 2f, y + 2f, ColPsycasts - 4f, RowHeight - 4f);
                PrisonerRosterUtility.DrawTraitsCell(cell, pDisplay, pTip);
                curX += ColPsycasts;
            }

            if (ColOn(PawnRosterColumnIds.Age))
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(curX, y, ColAge, RowHeight), entry.ageYears.ToString());
                curX += ColAge;
            }

            Text.Font = GameFont.Small;
            int bestLevel = PlayerPawnRosterUtility.GetBestSkillLevel(entry.skillLevels);
            for (int si = 0; si < PlayerPawnRosterUtility.AllSkillColumns.Length; si++)
            {
                SkillDef skill = PlayerPawnRosterUtility.AllSkillColumns[si];
                if (!ColOn(PawnRosterColumnIds.Skill(skill))) continue;
                int level = si < entry.skillLevels.Length ? entry.skillLevels[si] : 0;
                bool isBest = bestLevel > 0 && level == bestLevel;
                PlayerPawnRosterUtility.DrawSkillLevelWithPassion(
                    new Rect(curX, y, ColSkill, RowHeight), entry.pawn, skill, level, isBest, ColWindow);
                curX += ColSkill;
            }

            if (ColOn(PawnRosterColumnIds.Destination))
            {
                Rect destRect = new Rect(curX, y, ColDest, RowHeight);
                DrawDestinationCell(destRect, entry);
                if (Widgets.ButtonInvisible(destRect))
                {
                    OpenScheduleDialog(new List<string> { entry.thingId });
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }
            }

            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        private static void JumpToLocation(PrisonerRosterEntry entry)
        {
            WorldObject target = entry.locationJumpTarget;
            if (target == null || target.Destroyed) return;
            CameraJumper.TryJumpAndSelect(target);
        }

        private void DrawPrisonerQueueButtons(ref float curX, float y, PrisonerRosterEntry entry)
        {
            Rect col = new Rect(curX, y, ColReorder, RowHeight);
            if (entry != null && entry.isOutpostPrisoner && entry.pawn != null && entry.holdingOutpost != null)
            {
                const float btn = 22f;
                const float gap = 2f;
                float gridH = btn * 2f + gap;
                float gx = col.x + (ColReorder - btn) * 0.5f;
                float gy = y + (RowHeight - gridH) * 0.5f;
                bool canUp = entry.prisonerQueueIndex > 0;
                bool canDown = entry.prisonerQueueIndex >= 0
                    && entry.prisonerQueueIndex < entry.prisonerQueueCount - 1;
                WorldObject_WD_Outpost outpost = entry.holdingOutpost;
                Pawn pawn = entry.pawn;
                DrawQueueButton(new Rect(gx, gy, btn, btn), TexButton.ReorderUp, canUp,
                    "TSA_WD_Prisoners_QueueTopTip".Translate(),
                    () =>
                    {
                        if (OutpostPrisonerUtility.TryMovePrisonerToExtreme(outpost, pawn, true))
                            AfterPrisonerQueueChanged();
                    });
                DrawQueueButton(new Rect(gx, gy + btn + gap, btn, btn), TexButton.ReorderDown, canDown,
                    "TSA_WD_Prisoners_QueueBottomTip".Translate(),
                    () =>
                    {
                        if (OutpostPrisonerUtility.TryMovePrisonerToExtreme(outpost, pawn, false))
                            AfterPrisonerQueueChanged();
                    });
            }
            curX += ColReorder;
        }

        private static void DrawQueueButton(Rect rect, Texture2D tex, bool enabled, string tip, Action action)
        {
            if (!string.IsNullOrEmpty(tip))
                TooltipHandler.TipRegion(rect, tip);
            if (Mouse.IsOver(rect))
                Widgets.DrawHighlight(rect);
            Texture2D icon = tex ?? BaseContent.BadTex;
            Color prev = GUI.color;
            GUI.color = enabled ? Color.white : new Color(1f, 1f, 1f, 0.28f);
            // Doubled chevron = move to extreme (same visual language as outpost Pawns tab).
            float h = rect.height * 0.52f;
            GUI.DrawTexture(new Rect(rect.x, rect.y + 1f, rect.width, h), icon, ScaleMode.ScaleToFit);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - h - 1f, rect.width, h), icon, ScaleMode.ScaleToFit);
            GUI.color = prev;
            if (Widgets.ButtonInvisible(rect) && enabled)
            {
                action?.Invoke();
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
        }

        private void AfterPrisonerQueueChanged()
        {
            lastUpdateTick = -9999;
            InvalidateCache();
        }

        private void DrawInteractionButton(Rect rect, PrisonerRosterEntry entry)
        {
            if (entry.isOutpostPrisoner)
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                string label = entry.interactionLabel.NullOrEmpty()
                    ? PrisonerRosterUtility.GetOutpostInteractionLabel(entry.pawn, entry.holdingOutpost)
                    : entry.interactionLabel;
                Widgets.Label(rect, label.Truncate(rect.width - 4f));
                TooltipHandler.TipRegion(rect, label + "\n" + "TSA_WD_Prisoners_OutpostInteractionTip".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
                return;
            }

            Text.Font = GameFont.Small;
            string interactionLabel = entry.interactionLabel;

            if (Widgets.ButtonText(rect, interactionLabel.Truncate(rect.width - 8f)))
            {
                OpenInteractionMenu(entry);
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            string tip = interactionLabel;
            if (!entry.recruitable)
                tip += "\n" + "NonRecruitableTip".Translate();
            TooltipHandler.TipRegion(rect, tip);
        }

        private void OpenInteractionMenu(PrisonerRosterEntry entry)
        {
            Pawn pawn = entry.pawn;
            if (pawn?.guest == null) return;
            if (entry.isOutpostPrisoner) return;

            var options = new List<FloatMenuOption>();
            OpenColonyInteractionMenu(entry, options);
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void ConfirmLetGoSelectedOutpostPrisoners()
        {
            List<PrisonerRosterEntry> selected = PrisonerRosterUtility.ResolveSelectedIncludingHidden(cachedList, selectedThingIds);
            var toRelease = new List<PrisonerRosterEntry>();
            for (int i = 0; i < selected.Count; i++)
            {
                PrisonerRosterEntry e = selected[i];
                if (e != null && e.isOutpostPrisoner)
                    toRelease.Add(e);
            }
            if (toRelease.Count == 0)
            {
                Messages.Message("TSA_WD_Prisoners_KickOutNeedOutpost".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "TSA_WD_Prisoners_LetGoConfirm".Translate(),
                () =>
                {
                    for (int i = 0; i < toRelease.Count; i++)
                    {
                        PrisonerRosterEntry e = toRelease[i];
                        e.holdingOutpost?.LetGoPrisoner(e.pawn);
                    }
                    selectedThingIds.Clear();
                    lastUpdateTick = -9999;
                },
                destructive: true));
        }

        private void OpenColonyInteractionMenu(PrisonerRosterEntry entry, List<FloatMenuOption> options)
        {
            Pawn pawn = entry.pawn;
            if (!entry.recruitable)
            {
                var unwavering = new FloatMenuOption("NonRecruitable".Translate(), null);
                unwavering.Disabled = true;
                unwavering.tooltip = new TipSignal("NonRecruitableTip".Translate());
                options.Add(unwavering);
            }

            PrisonerInteractionModeDef maintain = PrisonerInteractionModeDefOf.MaintainOnly;
            PrisonerInteractionModeDef recruit = PrisonerInteractionModeDefOf.AttemptRecruit;

            if (maintain != null)
            {
                options.Add(new FloatMenuOption("TSA_WD_Prisoners_ModeMaintain".Translate(), () =>
                {
                    PrisonerRosterUtility.SetInteractionMode(pawn, maintain);
                    lastUpdateTick = -9999;
                }));
            }

            if (recruit != null)
            {
                if (!entry.recruitable)
                {
                    var recruitLocked = new FloatMenuOption("TSA_WD_Prisoners_ModeRecruit".Translate(), null);
                    recruitLocked.Disabled = true;
                    recruitLocked.tooltip = new TipSignal("NonRecruitableTip".Translate());
                    options.Add(recruitLocked);
                }
                else
                {
                    options.Add(new FloatMenuOption("TSA_WD_Prisoners_ModeRecruit".Translate(), () =>
                    {
                        PrisonerRosterUtility.SetInteractionMode(pawn, recruit);
                        lastUpdateTick = -9999;
                    }));
                }
            }
        }

        private static void DrawDestinationCell(Rect destRect, PrisonerRosterEntry entry)
        {
            if (entry.scheduledDestId < 0 || entry.scheduledDestLabel.NullOrEmpty())
            {
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.Font = GameFont.Tiny;
                GUI.color = new Color(1f, 1f, 1f, 0.7f);
                Widgets.Label(destRect.ContractedBy(4f, 2f), "TSA_WD_Prisoners_DestinationUnset".Translate());
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                TooltipHandler.TipRegion(destRect, "TSA_WD_Prisoners_DestinationClickTip".Translate());
                return;
            }

            float iconY = destRect.y + (destRect.height - DestIconSize) * 0.5f;
            Rect iconRect = new Rect(destRect.x + 4f, iconY, DestIconSize, DestIconSize);
            if (entry.scheduledDestIcon != null)
            {
                GUI.color = entry.scheduledDestIconColor;
                GUI.DrawTexture(iconRect, entry.scheduledDestIcon, ScaleMode.ScaleToFit);
                GUI.color = Color.white;
            }

            Text.Anchor = TextAnchor.MiddleLeft;
            Text.Font = GameFont.Small;
            Rect labelRect = new Rect(iconRect.xMax + 6f, destRect.y, destRect.width - DestIconSize - 14f, destRect.height);
            Widgets.Label(labelRect, entry.scheduledDestLabel.Truncate(labelRect.width));
            string tip = "TSA_WD_Prisoners_DestinationClickTip".Translate() + "\n" + entry.scheduledDestLabel;
            if (!entry.hasExplicitSchedule)
                tip += "\n" + "TSA_WD_Prisoners_DestinationDefaultTip".Translate();
            TooltipHandler.TipRegion(destRect, tip);
        }

        private void OpenScheduleDialog(List<string> thingIds)
        {
            if (thingIds == null || thingIds.Count == 0) return;
            Find.WindowStack.Add(new Dialog_SchedulePrisonerDestination(thingIds, () =>
            {
                lastUpdateTick = -9999;
            }));
        }

        private void SmartAssignSelected()
        {
            List<PrisonerRosterEntry> selected = PrisonerRosterUtility.ResolveSelectedIncludingHidden(cachedList, selectedThingIds);
            if (selected.Count == 0) return;

            int assigned = PrisonerRosterUtility.SmartAssignDestinations(selected, out int failed);
            lastUpdateTick = -9999;

            if (assigned > 0 && failed == 0)
            {
                Messages.Message(
                    "TSA_WD_Prisoners_SmartAssignDone".Translate(assigned.ToString()),
                    MessageTypeDefOf.TaskCompletion,
                    historical: false);
            }
            else if (assigned > 0)
            {
                Messages.Message(
                    "TSA_WD_Prisoners_SmartAssignPartial".Translate(assigned.ToString(), failed.ToString()),
                    MessageTypeDefOf.NeutralEvent,
                    historical: false);
            }
            else
            {
                Messages.Message(
                    "TSA_WD_Prisoners_SmartAssignNone".Translate(),
                    MessageTypeDefOf.RejectInput,
                    historical: false);
            }
        }

        /// <summary>Pemmican per pawn when recruit-journey packing is enabled (see Experimental settings).</summary>
        internal static int RecruitJourneyPemmican => PlayerPawnTransferUtility.RecruitTravelPemmicanPerPawn;
    }
}
