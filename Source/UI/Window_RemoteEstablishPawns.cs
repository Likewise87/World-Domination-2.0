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
    /// Colony-only pawn picker for tile-first remote establish.
    /// Confirm launches via <see cref="RemoteOutpostEstablishUtility.TryLaunch"/>.
    /// </summary>
    [StaticConstructorOnStartup]
    public class Window_RemoteEstablishPawns : Window
    {
        private const float RowHeight = 40f;
        private const float HeaderHeight = 28f;
        private const float ToolbarHeight = 40f;
        private const int UpdateIntervalTicks = 300;
        private const int PortraitCacheMax = 80;

        private const float ColIcon = 80f;
        private const float ColLocName = 160f;
        private const float ColSelect = 36f;
        private const float ColPawnType = 96f;
        private const float ColPortrait = 40f;
        private const float ColName = 140f;
        private const float ColStar = 56f;
        private const float ColSkill = 74f;
        private const float ColPadding = 12f;
        private const float LocIconDrawSize = 40f;
        private const float ConfirmBtnWidth = 140f;

        private static readonly Vector2 PortraitSize = new Vector2(36f, 36f);
        private static readonly Dictionary<string, Texture> PortraitCache = new Dictionary<string, Texture>();

        private readonly int tile;
        private readonly WorldObjectDef outpostDef;

        private Vector2 scrollPos;
        private string sortColumn = PlayerPawnRosterUtility.DefaultSortColumn;
        private bool sortAscending = true;
        private bool useDefaultGrouping = true;
        private string pawnSearchTerm = "";
        private PlayerPawnTypeFilter pawnTypeFilter = PlayerPawnTypeFilter.All;
        private PlayerPawnStarFilter starFilter = PlayerPawnStarFilter.AllAnywhere;
        private int lastUpdateTick = -9999;
        private List<PlayerPawnRosterEntry> cachedList = new List<PlayerPawnRosterEntry>();
        private readonly HashSet<string> selectedThingIds = new HashSet<string>();
        private float lastScrollViewportHeight = 400f;
        private static string starHeaderTip;

        public override Vector2 InitialSize => new Vector2(UI.screenWidth * 0.92f, UI.screenHeight * 0.88f);

        public Window_RemoteEstablishPawns(int tile, WorldObjectDef outpostDef)
        {
            this.tile = tile;
            this.outpostDef = outpostDef;
            doCloseX = true;
            closeOnCancel = true;
            absorbInputAroundWindow = true;
            forcePause = false;
            ApplyInitialSortForOutpost();
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

            if (Find.TickManager.TicksGame >= lastUpdateTick + UpdateIntervalTicks || cachedList.Count == 0)
            {
                cachedList = BuildCurrentRoster(pawnTypeFilter);
                PlayerPawnRosterUtility.PruneSelectionToLastScan(selectedThingIds);
                lastUpdateTick = Find.TickManager.TicksGame;
            }

            Text.Font = GameFont.Medium;
            string title = "TSA_WD_TileFirstEstablish_PawnWindowTitle".Translate(
                outpostDef?.LabelCap ?? "Outpost").ToString();
            Widgets.Label(new Rect(0f, 0f, inRect.width - ConfirmBtnWidth - 16f, 32f), title);
            Text.Font = GameFont.Small;

            var selected = PlayerPawnRosterUtility.ResolveSelectedEntries(cachedList, selectedThingIds);
            bool hasHiddenSelection = selected.Count < selectedThingIds.Count;
            bool canConfirm;
            string disabledTip;
            if (hasHiddenSelection)
            {
                // Keep Confirm enabled while filters hide part of the selection; validate fully on click.
                if (selected.Count == 0)
                {
                    canConfirm = selectedThingIds.Count > 0;
                    disabledTip = null;
                }
                else
                    canConfirm = CanConfirm(selected, out disabledTip);
            }
            else
                canConfirm = CanConfirm(selected, out disabledTip);

            Rect confirmRect = new Rect(inRect.width - ConfirmBtnWidth, 2f, ConfirmBtnWidth, ToolbarBtnHeight());
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
                var full = PlayerPawnRosterUtility.ResolveSelectedEntriesIncludingHidden(cachedList, selectedThingIds);
                full.RemoveAll(e => e.locationKind != PlayerPawnLocationKind.Colony);
                TryConfirm(full);
            }

            float headerTop = ToolbarHeight;
            float listTop = headerTop + HeaderHeight + 4f;
            float tableHeight = inRect.height - listTop - 8f;

            DrawHorizontallyScrolledSection(
                new Rect(0f, headerTop, inRect.width, HeaderHeight),
                scrollPos.x,
                totalWidth,
                x => DrawTableHeader(x, 0f, totalWidth));
            Widgets.DrawLineHorizontal(0f, headerTop + HeaderHeight, inRect.width);

            float totalHeight = cachedList.Count * RowHeight + 8f;
            Rect viewRect = new Rect(0f, 0f, totalWidth, Mathf.Max(totalHeight, tableHeight));
            Rect scrollOuter = new Rect(0f, listTop, inRect.width, tableHeight);
            lastScrollViewportHeight = scrollOuter.height;

            Widgets.BeginScrollView(scrollOuter, ref scrollPos, viewRect);
            for (int i = 0; i < cachedList.Count; i++)
                DrawRow(0f, i * RowHeight, totalWidth, cachedList[i], i % 2 == 0);
            Widgets.EndScrollView();
            Text.Anchor = TextAnchor.UpperLeft;
            PawnRosterHeaderFilter.DrawDropdownIfOpen();
        }

        private static float ToolbarBtnHeight() => 30f;

        private bool CanConfirm(List<PlayerPawnRosterEntry> selected, out string disabledTip)
        {
            disabledTip = null;
            if (!RemoteOutpostEstablishUtility.TryValidateColonySelection(selected, out MapParent source, out _, out string fail, colonyOnlyRoster: true))
            {
                disabledTip = fail;
                return false;
            }

            List<Pawn> pawns = RemoteOutpostEstablishUtility.CollectPawns(selected);
            if (!RemoteOutpostEstablishUtility.CanEstablishAtRemote(tile, outpostDef, pawns, source?.Map, out string establishFail))
            {
                disabledTip = establishFail;
                return false;
            }

            return true;
        }

        private void TryConfirm(List<PlayerPawnRosterEntry> selected)
        {
            if (!RemoteOutpostEstablishUtility.TryValidateColonySelection(selected, out MapParent source, out List<PlayerPawnRosterEntry> entries, out string fail, colonyOnlyRoster: true))
            {
                Messages.Message(fail ?? "TSA_WD_RemoteEstablish_InvalidSelection".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            RemoteOutpostEstablishUtility.LaunchAfterOptionalCarryConfirm(
                tile, outpostDef, source, entries,
                onSuccess: () => Close(),
                onFail: launchFail => Messages.Message(
                    launchFail ?? "TSA_WD_RemoteEstablish_Failed".Translate(),
                    MessageTypeDefOf.RejectInput, false),
                onCancel: null);
        }

        private static void DrawHorizontallyScrolledSection(Rect viewport, float scrollX, float contentWidth, Action<float> draw)
        {
            GUI.BeginGroup(viewport);
            draw(-scrollX);
            GUI.EndGroup();
        }

        private List<PlayerPawnRosterEntry> BuildCurrentRoster(
            PlayerPawnTypeFilter typeF,
            PlayerPawnStarFilter? starF = null)
        {
            string pawnSearchLower = string.IsNullOrEmpty(pawnSearchTerm) ? null : pawnSearchTerm.ToLowerInvariant();
            var list = PlayerPawnRosterUtility.BuildRoster(
                pawnSearchLower, null, null, null,
                useDefaultGrouping, sortColumn, sortAscending, starF ?? starFilter, typeF);
            list.RemoveAll(e => e.locationKind != PlayerPawnLocationKind.Colony);
            return list;
        }

        private static float ComputeTotalTableWidth()
        {
            return ColIcon + ColLocName + ColSelect + ColPawnType + ColPortrait + ColName + ColStar + ColPadding
                + PlayerPawnRosterUtility.AllSkillColumns.Length * ColSkill;
        }

        private static void EnsureStarHeaderTip()
        {
            if (starHeaderTip == null)
                starHeaderTip = "TSA_WD_AllPlayerPawns_StarTip".Translate();
        }

        private void DrawTableHeader(float x, float y, float width)
        {
            EnsureStarHeaderTip();
            float curX = x;
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Rect hRect = new Rect(x, y, width, HeaderHeight);

            curX += ColIcon;
            DrawHeader(ref curX, ColLocName, "TSA_WD_AllPlayerPawns_ColLocation".Translate(), "LocationName", hRect);
            DrawSelectAllHeader(ref curX, hRect);
            PawnRosterHeaderFilter.DrawFilterableHeader(
                ref curX, hRect.y, ColPawnType, HeaderHeight,
                "TSA_WD_AllPlayerPawns_ColPawnType".Translate(),
                sortColumn == "PawnType", sortAscending,
                TextAnchor.LowerCenter,
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
            curX += ColPortrait;
            PawnRosterHeaderFilter.DrawFilterableHeader(
                ref curX, hRect.y, ColName, HeaderHeight,
                "TSA_WD_PawnCol_PawnName".Translate(),
                sortColumn == "Name", sortAscending,
                TextAnchor.LowerCenter,
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
            PawnRosterHeaderFilter.DrawFilterableHeader(
                ref curX, hRect.y, ColStar, HeaderHeight,
                "",
                sortColumn == "Starred", sortAscending,
                TextAnchor.LowerCenter,
                starFilter != PlayerPawnStarFilter.AllAnywhere,
                starHeaderTip,
                icon => PawnRosterHeaderFilter.OpenChoiceDropdown(
                    icon,
                    "TSA_WD_FilterByStar".Translate(),
                    PawnRosterHeaderFilter.PlayerStarChoices(starFilter, f =>
                    {
                        starFilter = f;
                        lastUpdateTick = -9999;
                    }, PawnRosterHeaderFilter.StarRowsFrom(BuildCurrentRoster(pawnTypeFilter, PlayerPawnStarFilter.AllAnywhere))),
                    width: 280f),
                () => SetSort("Starred"));
            curX += ColPadding;

            SkillDef[] skills = PlayerPawnRosterUtility.AllSkillColumns;
            for (int i = 0; i < skills.Length; i++)
                DrawHeader(ref curX, ColSkill, skills[i].LabelCap, skills[i].defName, hRect);

            GUI.color = Color.white;
        }

        private void DrawHeader(ref float curX, float width, string label, string tag, Rect hRect)
        {
            Rect headerRect = new Rect(curX, hRect.y, width, hRect.height);
            if (Mouse.IsOver(headerRect)) Widgets.DrawHighlight(headerRect);
            Text.Anchor = TextAnchor.LowerCenter;
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
            float visibleY = scrollPos.y - RowHeight;
            float visibleYMax = scrollPos.y + lastScrollViewportHeight;
            if (y < visibleY || y >= visibleYMax)
                return;

            Rect row = new Rect(x, y, width, RowHeight);
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
                float iconY = y + (RowHeight - LocIconDrawSize) * 0.5f;
                Rect iconRect = new Rect(curX + 10f, iconY, LocIconDrawSize, LocIconDrawSize);
                GUI.color = entry.locationIconColor;
                GUI.DrawTexture(iconRect, entry.locationIcon, ScaleMode.ScaleToFit);
                GUI.color = Color.white;
                TooltipHandler.TipRegion(iconRect, entry.locationLabel);
            }
            curX += ColIcon;

            Rect locNameRect = new Rect(curX, y, ColLocName, RowHeight);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(locNameRect, entry.locationLabel.Truncate(ColLocName - 4f));
            curX += ColLocName;

            Rect selRect = new Rect(curX, y, ColSelect, RowHeight);
            if (entry.isMovable && entry.mapParent != null)
            {
                bool canInteract = PlayerPawnTransferUtility.ColonyBulkSelectionIsAllowedWithExtra(
                    entry.mapParent,
                    selectedThingIds,
                    entry.pawn,
                    cachedList);
                float cx = curX + (ColSelect - 24f) * 0.5f;
                float cy = y + (RowHeight - 24f) * 0.5f;
                if (!selectedThingIds.Contains(entry.thingId) && !canInteract)
                {
                    var probe = new List<Pawn>();
                    for (int i = 0; i < cachedList.Count; i++)
                    {
                        PlayerPawnRosterEntry e = cachedList[i];
                        if (e.mapParent != entry.mapParent) continue;
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

            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(curX, y, ColPawnType, RowHeight), entry.pawnTypeLabel.Truncate(ColPawnType - 4f));
            curX += ColPawnType;

            Rect portraitCell = new Rect(curX, y, ColPortrait, RowHeight);
            Texture portrait = PawnPortraitUIUtils.GetPortrait(
                entry.pawn,
                PawnPortraitUIUtils.BuildCacheKey(entry.pawn, entry.summary),
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

            if (entry.isSlave) GUI.color = PawnNameColorUtility.PawnNameColorOf(entry.pawn);

            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(curX, y, ColName, RowHeight), entry.nameLabel.Truncate(ColName - 4f));
            if (Widgets.ButtonInvisible(new Rect(curX, y, ColName, RowHeight)))
                Find.WindowStack.Add(new Dialog_InfoCard(entry.pawn));
            curX += ColName;
            GUI.color = prevGui;

            Rect starCell = new Rect(curX, y, ColStar, RowHeight);
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
            curX += ColPadding;

            Text.Anchor = TextAnchor.MiddleLeft;
            int bestLevel = PlayerPawnRosterUtility.GetBestSkillLevel(entry.skillLevels);
            for (int si = 0; si < PlayerPawnRosterUtility.AllSkillColumns.Length; si++)
            {
                SkillDef skill = PlayerPawnRosterUtility.AllSkillColumns[si];
                int level = si < entry.skillLevels.Length ? entry.skillLevels[si] : 0;
                bool isBest = bestLevel > 0 && level == bestLevel;
                PlayerPawnRosterUtility.DrawSkillLevelWithPassion(
                    new Rect(curX, y, ColSkill, RowHeight), entry.pawn, skill, level, isBest);
                curX += ColSkill;
            }

            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = prevGui;
        }
    }
}
