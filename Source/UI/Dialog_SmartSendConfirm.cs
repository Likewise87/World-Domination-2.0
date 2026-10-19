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
    /// Confirm Smart Send. Table chrome matches All Player Pawns; Override uses Transfer list helper.
    /// </summary>
    [StaticConstructorOnStartup]
    public class Dialog_SmartSendConfirm : Window
    {
        private const float RowHeight = 56f;
        private const float HeaderHeight = 28f;
        private const float PortraitSize = 36f;
        private const float LocIconDrawSize = 40f;
        private const float LocIconPad = 4f;
        private const float ColRemove = 28f;
        private const float ColOriginIcon = LocIconDrawSize + LocIconPad * 2f;
        private const float ColOriginName = 140f;
        private const float ColPortrait = 40f;
        private const float ColPawnName = 120f;
        private const float ColDestIcon = LocIconDrawSize + LocIconPad * 2f;
        private const float ColDest = 200f;
        private const float ColOverrideGap = 12f;
        private const float ColReasonMin = 300f;
        private static readonly Color OverrideChangedFill = new Color(0.42f, 0.36f, 0.10f, 0.92f);
        private const float TablePad = 4f;
        private const float ButtonHeight = 32f;
        private const float TitleHeight = 32f;
        private const float SubtitleHeight = 24f;
        private const float ResetBtnWidth = 180f;
        private const float ToolbarBtnGap = 10f;

        private static readonly Texture2D RemoveIcon = TexButton.CloseXSmall ?? TexButton.Delete;
        private static readonly Color RemoveTint = new Color(0.9f, 0.25f, 0.25f);

        private readonly List<PlayerPawnTransferUtility.PlayerPawnTransferAssignment> assignments;
        private readonly Action onConfirm;
        private readonly List<RowSnapshot> originalSnapshot = new List<RowSnapshot>();
        private readonly List<RowVm> rows = new List<RowVm>();
        private readonly List<RowVm> filteredRows = new List<RowVm>();
        private Vector2 scrollPos;
        private float lastScrollViewportHeight = 200f;
        private string cachedSubtitle = "";
        private string cachedResetLabel;
        private string cachedOverrideTip;
        private string cachedRemoveTip;
        private string cachedStayReason;
        private string cachedColOrigin;
        private string cachedColPawn;
        private string cachedColDest;
        private string cachedColReason;

        private static string sortColumn = "Pawn";
        private static bool sortAscending = true;
        private static string originFilter = "";
        private static string pawnFilter = "";
        private static string destFilter = "";
        private static string reasonFilter = "";

        private struct RowSnapshot
        {
            public PlayerPawnRosterEntry entry;
            public PlayerPawnTransferDestination dest;
            public bool isStay;
            public SkillDef matchedSkill;
        }

        private class RowVm
        {
            public PlayerPawnRosterEntry entry;
            public Pawn pawn;
            public string pawnName;
            public string portraitKey;
            public PlanetTile fromTile;
            public SkillDef matchedSkill;
            public PlayerPawnTransferDestination dest;
            public Texture originIcon;
            public Color originIconColor;
            public string originLabel;
            public Texture destIcon;
            public Color destIconColor;
            public string destLabel;
            public string overrideLabel;
            public string reasonText;
            public string reasonTip;
            public PlayerPawnTransferDestination smartDest;
            public bool isOverridden;
            public bool isStay;
            public bool willLaunch;
        }

        public override Vector2 InitialSize =>
            new Vector2(
                Mathf.Min(UI.screenWidth - 20f, 1180f),
                Mathf.Min(UI.screenHeight - 40f, 720f));

        public Dialog_SmartSendConfirm(
            List<PlayerPawnTransferUtility.PlayerPawnTransferAssignment> moveAssignments,
            List<PlayerPawnTransferUtility.PlayerPawnTransferAssignment> stayAssignments,
            Action onConfirm)
        {
            assignments = moveAssignments ?? new List<PlayerPawnTransferUtility.PlayerPawnTransferAssignment>();
            this.onConfirm = onConfirm;
            doCloseX = true;
            closeOnCancel = true;
            absorbInputAroundWindow = false;
            draggable = true;
            forcePause = false;
            resizeable = true;

            cachedResetLabel = "TSA_WD_SmartSend_Reset".Translate();
            cachedOverrideTip = "TSA_WD_SmartSend_OverrideTip".Translate();
            cachedRemoveTip = "TSA_WD_SmartSend_RemoveTip".Translate();
            cachedStayReason = "TSA_WD_SmartSend_ReasonStay".Translate();
            cachedColOrigin = "TSA_WD_SmartSend_ColOrigin".Translate();
            cachedColPawn = "TSA_WD_SmartSend_ColPawn".Translate();
            cachedColDest = "TSA_WD_SmartSend_ColDestination".Translate();
            cachedColReason = "TSA_WD_SmartSend_ColReason".Translate();

            CaptureSnapshot(assignments, stayAssignments);
            RestoreFromSnapshot();
        }

        public Dialog_SmartSendConfirm(
            List<PlayerPawnTransferUtility.PlayerPawnTransferAssignment> assignments,
            Action onConfirm)
            : this(assignments, null, onConfirm)
        {
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (PawnRosterHeaderFilter.TryCloseDropdownOnCancel())
                return;
            if (WdWindowEsc.TryCloseOnCancel(this))
                return;

            float bottomBlockH = ButtonHeight + 8f;
            float topBlockH = TitleHeight + 4f + SubtitleHeight + 6f;

            Text.Font = GameFont.Medium;
            float modeW = PlayerPawnDropPodUtility.ModeIconSize;
            float titleRight = inRect.width - ResetBtnWidth - ToolbarBtnGap - modeW - ToolbarBtnGap;
            Widgets.Label(new Rect(0f, 0f, Mathf.Max(80f, titleRight), TitleHeight),
                "TSA_WD_PawnDropPod_SmartSendTitle".Translate());

            Rect resetRect = new Rect(inRect.width - ResetBtnWidth, 1f, ResetBtnWidth, 30f);
            Rect modeRect = new Rect(resetRect.x - ToolbarBtnGap - modeW, 1f, modeW, 30f);
            PlayerPawnDropPodUtility.DrawAdHocModeIcon(modeRect);
            if (WorldDomination_UIUtils.ButtonTextWithIcon(resetRect, null, cachedResetLabel, centerContents: true))
            {
                RestoreFromSnapshot();
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(0f, TitleHeight + 4f, inRect.width, SubtitleHeight), cachedSubtitle);

            float tableTop = topBlockH;
            float tableAreaH = Mathf.Max(80f, inRect.height - bottomBlockH - tableTop - 8f);

            float fixedLeft = ColRemove + ColOriginIcon + ColOriginName + ColPortrait + ColPawnName
                + ColDestIcon + ColDest + ColOverrideGap + TablePad;
            float reasonWidth = Mathf.Max(ColReasonMin, inRect.width - fixedLeft);

            Rect headerRect = new Rect(0f, tableTop, inRect.width, HeaderHeight);
            Text.Font = GameFont.Tiny;
            DrawTableHeader(headerRect, reasonWidth);

            Rect scrollOuter = new Rect(0f, tableTop + HeaderHeight, inRect.width, tableAreaH - HeaderHeight);
            lastScrollViewportHeight = scrollOuter.height;
            float contentW = fixedLeft + reasonWidth;
            Rect viewRect = new Rect(0f, 0f, contentW, filteredRows.Count * RowHeight + 2f);
            Widgets.BeginScrollView(scrollOuter, ref scrollPos, viewRect);
            for (int i = 0; i < filteredRows.Count; i++)
            {
                float rowY = i * RowHeight;
                if (rowY + RowHeight < scrollPos.y - RowHeight) continue;
                if (rowY > scrollPos.y + lastScrollViewportHeight + RowHeight) continue;
                int sourceIndex = rows.IndexOf(filteredRows[i]);
                DrawRow(0f, rowY, contentW, reasonWidth, sourceIndex, filteredRows[i], i % 2 == 0);
            }
            Widgets.EndScrollView();

            PawnRosterHeaderFilter.DrawDropdownIfOpen();

            Text.Font = GameFont.Small;
            float btnY = inRect.height - ButtonHeight;
            if (Widgets.ButtonText(new Rect(0f, btnY, inRect.width * 0.48f, ButtonHeight), "Cancel".Translate()))
            {
                Close();
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            int launchCount = CountLaunchRows();
            bool canConfirm = launchCount > 0;
            if (!canConfirm) GUI.color = Color.gray;
            if (Widgets.ButtonText(new Rect(inRect.width * 0.52f, btnY, inRect.width * 0.48f, ButtonHeight),
                    "TSA_WD_PawnDropPod_SmartSendConfirm".Translate())
                && canConfirm)
            {
                SyncAssignmentsFromRows();
                onConfirm?.Invoke();
                Close();
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
            GUI.color = Color.white;
        }

        private int CountLaunchRows()
        {
            int n = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].willLaunch) n++;
            }
            return n;
        }

        private void CaptureSnapshot(
            List<PlayerPawnTransferUtility.PlayerPawnTransferAssignment> moves,
            List<PlayerPawnTransferUtility.PlayerPawnTransferAssignment> stays)
        {
            originalSnapshot.Clear();
            if (moves != null)
            {
                for (int i = 0; i < moves.Count; i++)
                    AddSnapshot(moves[i], isStay: false);
            }
            if (stays != null)
            {
                for (int i = 0; i < stays.Count; i++)
                    AddSnapshot(stays[i], isStay: true);
            }
        }

        private void AddSnapshot(PlayerPawnTransferUtility.PlayerPawnTransferAssignment a, bool isStay)
        {
            PlayerPawnRosterEntry entry = a.entry;
            if (entry?.pawn == null) return;
            PlayerPawnTransferDestination dest = a.destination;
            if (dest.kind == PlayerPawnTransferDestinationKind.Outpost
                && (dest.outpost == null || dest.outpost.Destroyed))
                return;
            if (dest.kind == PlayerPawnTransferDestinationKind.Colony
                && (dest.colony == null || dest.colony.Destroyed))
                return;

            SkillDef matched = dest.kind == PlayerPawnTransferDestinationKind.Outpost
                ? SmartAssignOutpostUtility.FindMatchedSkillForDest(entry.pawn, dest.outpost)
                : null;

            originalSnapshot.Add(new RowSnapshot
            {
                entry = entry,
                dest = dest,
                isStay = isStay,
                matchedSkill = matched
            });
        }

        private void RestoreFromSnapshot()
        {
            rows.Clear();
            for (int i = 0; i < originalSnapshot.Count; i++)
            {
                RowSnapshot s = originalSnapshot[i];
                if (s.entry?.pawn == null) continue;
                PlanetTile fromTile = ResolveFromTile(s.entry);
                rows.Add(BuildRowVm(
                    s.entry, s.entry.pawn, fromTile, s.dest, s.matchedSkill, s.isStay, smartDest: s.dest));
            }
            RebuildFiltered();
            RefreshSubtitle();
        }

        private void SyncAssignmentsFromRows()
        {
            assignments.Clear();
            for (int i = 0; i < rows.Count; i++)
            {
                RowVm row = rows[i];
                if (!row.willLaunch) continue;
                if (row.dest.kind == PlayerPawnTransferDestinationKind.Outpost
                    && (row.dest.outpost == null || row.dest.outpost.Destroyed))
                    continue;
                if (row.dest.kind == PlayerPawnTransferDestinationKind.Colony
                    && (row.dest.colony == null || row.dest.colony.Destroyed))
                    continue;
                if (row.dest.kind == PlayerPawnTransferDestinationKind.ExitHere)
                    continue;

                assignments.Add(new PlayerPawnTransferUtility.PlayerPawnTransferAssignment
                {
                    entry = row.entry,
                    destination = row.dest
                });
            }
        }

        private void RefreshSubtitle()
        {
            int launch = CountLaunchRows();
            int stay = rows.Count - launch;
            if (stay > 0)
                cachedSubtitle = "TSA_WD_SmartSend_SubtitleMixed".Translate(launch.ToString(), stay.ToString());
            else
                cachedSubtitle = "TSA_WD_PawnTransfer_DialogSubtitle".Translate(launch.ToString());
        }

        private void RebuildFiltered()
        {
            filteredRows.Clear();
            for (int i = 0; i < rows.Count; i++)
            {
                RowVm r = rows[i];
                if (!originFilter.NullOrEmpty()
                    && (r.originLabel == null || r.originLabel.IndexOf(originFilter, StringComparison.OrdinalIgnoreCase) < 0))
                    continue;
                if (!pawnFilter.NullOrEmpty()
                    && (r.pawnName == null || r.pawnName.IndexOf(pawnFilter, StringComparison.OrdinalIgnoreCase) < 0))
                    continue;
                if (!destFilter.NullOrEmpty()
                    && (r.destLabel == null || r.destLabel.IndexOf(destFilter, StringComparison.OrdinalIgnoreCase) < 0))
                    continue;
                if (!reasonFilter.NullOrEmpty()
                    && (r.reasonText == null || r.reasonText.IndexOf(reasonFilter, StringComparison.OrdinalIgnoreCase) < 0))
                    continue;
                filteredRows.Add(r);
            }

            bool asc = sortAscending;
            filteredRows.Sort((a, b) =>
            {
                int cmp = sortColumn switch
                {
                    "Origin" => string.Compare(a.originLabel, b.originLabel, StringComparison.OrdinalIgnoreCase),
                    "Dest" => string.Compare(a.destLabel, b.destLabel, StringComparison.OrdinalIgnoreCase),
                    "Reason" => string.Compare(a.reasonText, b.reasonText, StringComparison.OrdinalIgnoreCase),
                    _ => string.Compare(a.pawnName, b.pawnName, StringComparison.OrdinalIgnoreCase)
                };
                return asc ? cmp : -cmp;
            });
        }

        private static PlanetTile ResolveFromTile(PlayerPawnRosterEntry entry)
        {
            if (entry?.sourceOutpost != null && entry.sourceOutpost.Tile.Valid)
                return entry.sourceOutpost.Tile;
            if (entry?.mapParent != null && entry.mapParent.Tile.Valid)
                return entry.mapParent.Tile;
            return default;
        }

        private static void FillDestVisuals(
            PlayerPawnTransferDestination dest,
            out Texture icon,
            out Color iconColor,
            out string label)
        {
            icon = null;
            iconColor = Color.white;
            label = dest.Label;
            if (dest.kind == PlayerPawnTransferDestinationKind.Outpost && dest.outpost != null)
            {
                icon = dest.outpost.ExpandingIcon ?? dest.outpost.def?.ExpandingIconTexture;
                iconColor = dest.outpost.Faction?.Color ?? Color.white;
                label = !string.IsNullOrEmpty(dest.outpost.Name) ? dest.outpost.Name : dest.outpost.LabelCap;
            }
            else if (dest.kind == PlayerPawnTransferDestinationKind.Colony && dest.colony != null)
            {
                Faction player = Faction.OfPlayer;
                icon = player?.def?.FactionIcon;
                iconColor = player?.Color ?? Color.white;
                label = PlayerPawnRosterUtility.FormatColonyLabelForDisplay(dest.colony.LabelCap);
            }
        }

        private RowVm BuildRowVm(
            PlayerPawnRosterEntry entry,
            Pawn pawn,
            PlanetTile fromTile,
            PlayerPawnTransferDestination dest,
            SkillDef matchedSkill,
            bool isStay,
            PlayerPawnTransferDestination smartDest)
        {
            FillDestVisuals(dest, out Texture destIcon, out Color destColor, out string destLabel);

            string reason;
            string reasonTip = "";
            if (dest.kind == PlayerPawnTransferDestinationKind.Outpost && dest.outpost != null)
            {
                string baseReason = SmartAssignOutpostUtility.BuildAssignmentReasonText(
                    pawn, fromTile, dest.outpost, matchedSkill);
                reasonTip = SmartAssignOutpostUtility.BuildAssignmentReasonTip(
                    pawn, fromTile, dest.outpost, matchedSkill);
                reason = isStay ? cachedStayReason + "\n" + baseReason : baseReason;
            }
            else
            {
                reason = isStay
                    ? cachedStayReason
                    : "TSA_WD_SmartSend_ReasonOverrideDest".Translate(destLabel);
            }

            bool willLaunch = !isStay && !IsSameAsOrigin(entry, dest);
            bool overridden = !DestEquals(dest, smartDest);

            return new RowVm
            {
                entry = entry,
                pawn = pawn,
                pawnName = pawn.LabelShortCap ?? pawn.LabelCap ?? "?",
                portraitKey = PawnPortraitUIUtils.BuildCacheKey(pawn, entry?.summary),
                fromTile = fromTile,
                matchedSkill = matchedSkill,
                dest = dest,
                originIcon = entry?.locationIcon,
                originIconColor = entry != null ? entry.locationIconColor : Color.white,
                originLabel = entry?.locationLabel ?? "?",
                destIcon = destIcon,
                destIconColor = destColor,
                destLabel = destLabel,
                overrideLabel = destLabel,
                reasonText = reason,
                reasonTip = reasonTip,
                smartDest = smartDest,
                isOverridden = overridden,
                isStay = isStay,
                willLaunch = willLaunch
            };
        }

        private static bool IsSameAsOrigin(PlayerPawnRosterEntry entry, PlayerPawnTransferDestination dest)
        {
            if (dest.kind == PlayerPawnTransferDestinationKind.Outpost
                && entry?.sourceOutpost != null
                && dest.outpost == entry.sourceOutpost)
                return true;
            if (dest.kind == PlayerPawnTransferDestinationKind.Colony
                && entry?.sourceOutpost == null
                && entry?.mapParent != null
                && dest.colony == entry.mapParent)
                return true;
            return false;
        }

        private static bool DestEquals(PlayerPawnTransferDestination a, PlayerPawnTransferDestination b)
        {
            if (a.kind != b.kind) return false;
            if (a.kind == PlayerPawnTransferDestinationKind.Outpost)
                return a.outpost == b.outpost;
            if (a.kind == PlayerPawnTransferDestinationKind.Colony)
                return a.colony == b.colony;
            return true;
        }

        private void ApplyOverride(int rowIndex, PlayerPawnTransferDestination newDest)
        {
            if (rowIndex < 0 || rowIndex >= rows.Count) return;
            RowVm old = rows[rowIndex];
            bool stay = IsSameAsOrigin(old.entry, newDest);
            SkillDef matched = newDest.kind == PlayerPawnTransferDestinationKind.Outpost
                ? SmartAssignOutpostUtility.FindMatchedSkillForDest(old.pawn, newDest.outpost)
                : null;
            rows[rowIndex] = BuildRowVm(
                old.entry, old.pawn, old.fromTile, newDest, matched, stay, old.smartDest);
            RebuildFiltered();
            RefreshSubtitle();
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        private static void JumpToOrigin(PlayerPawnRosterEntry entry)
        {
            if (entry == null) return;
            if (entry.sourceOutpost != null)
            {
                WorldDomination_UIUtils.JumpToWorldObjectOnMap(entry.sourceOutpost);
                return;
            }
            if (entry.mapParent != null)
                WorldDomination_UIUtils.JumpToWorldObjectOnMap(entry.mapParent);
        }

        private static void JumpToDest(PlayerPawnTransferDestination dest)
        {
            if (dest.kind == PlayerPawnTransferDestinationKind.Outpost && dest.outpost != null)
                WorldDomination_UIUtils.JumpToWorldObjectOnMap(dest.outpost);
            else if (dest.kind == PlayerPawnTransferDestinationKind.Colony && dest.colony != null)
                WorldDomination_UIUtils.JumpToWorldObjectOnMap(dest.colony);
        }

        private static void OpenPawnInfo(Pawn pawn)
        {
            if (pawn == null) return;
            Find.WindowStack.Add(new Dialog_InfoCard(pawn));
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        private void RemoveRow(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= rows.Count) return;
            rows.RemoveAt(rowIndex);
            RebuildFiltered();
            RefreshSubtitle();
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        private void OpenOverridePicker(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= rows.Count) return;
            RowVm row = rows[rowIndex];
            int captured = rowIndex;
            var one = new List<PlayerPawnRosterEntry> { row.entry };
            Find.WindowStack.Add(new Dialog_PickPlayerOutpost(
                one,
                dest => ApplyOverride(captured, dest)));
        }

        private void DrawTableHeader(Rect rect, float reasonWidth)
        {
            Widgets.DrawMenuSection(rect);
            float curX = rect.x + TablePad;
            float y = rect.y;

            curX += ColRemove;

            PawnRosterHeaderFilter.DrawFilterableHeader(
                ref curX, y, ColOriginIcon + ColOriginName, HeaderHeight,
                cachedColOrigin,
                sortColumn == "Origin", sortAscending,
                TextAnchor.MiddleLeft,
                !originFilter.NullOrEmpty(),
                "TSA_WD_AllPlayerPawns_SearchLocation".Translate(),
                icon => PawnRosterHeaderFilter.OpenTextDropdown(
                    icon,
                    cachedColOrigin,
                    "TSA_WD_AllPlayerPawns_SearchLocation".Translate(),
                    () => originFilter,
                    v => { originFilter = v ?? ""; RebuildFiltered(); },
                    () => { originFilter = ""; RebuildFiltered(); }),
                () => ToggleSort("Origin"));

            PawnRosterHeaderFilter.DrawFilterableHeader(
                ref curX, y, ColPortrait + ColPawnName, HeaderHeight,
                cachedColPawn,
                sortColumn == "Pawn", sortAscending,
                TextAnchor.MiddleLeft,
                !pawnFilter.NullOrEmpty(),
                "TSA_WD_AllPlayerPawns_SearchName".Translate(),
                icon => PawnRosterHeaderFilter.OpenTextDropdown(
                    icon,
                    cachedColPawn,
                    "TSA_WD_AllPlayerPawns_SearchName".Translate(),
                    () => pawnFilter,
                    v => { pawnFilter = v ?? ""; RebuildFiltered(); },
                    () => { pawnFilter = ""; RebuildFiltered(); }),
                () => ToggleSort("Pawn"));

            PawnRosterHeaderFilter.DrawFilterableHeader(
                ref curX, y, ColDestIcon + ColDest, HeaderHeight,
                cachedColDest,
                sortColumn == "Dest", sortAscending,
                TextAnchor.MiddleLeft,
                !destFilter.NullOrEmpty(),
                "TSA_WD_AllPlayerPawns_SearchLocation".Translate(),
                icon => PawnRosterHeaderFilter.OpenTextDropdown(
                    icon,
                    cachedColDest,
                    "TSA_WD_AllPlayerPawns_SearchLocation".Translate(),
                    () => destFilter,
                    v => { destFilter = v ?? ""; RebuildFiltered(); },
                    () => { destFilter = ""; RebuildFiltered(); }),
                () => ToggleSort("Dest"));

            curX += ColOverrideGap;

            PawnRosterHeaderFilter.DrawFilterableHeader(
                ref curX, y, reasonWidth, HeaderHeight,
                cachedColReason,
                sortColumn == "Reason", sortAscending,
                TextAnchor.MiddleLeft,
                !reasonFilter.NullOrEmpty(),
                "TSA_WD_AllPlayerPawns_SearchName".Translate(),
                icon => PawnRosterHeaderFilter.OpenTextDropdown(
                    icon,
                    cachedColReason,
                    "TSA_WD_AllPlayerPawns_SearchName".Translate(),
                    () => reasonFilter,
                    v => { reasonFilter = v ?? ""; RebuildFiltered(); },
                    () => { reasonFilter = ""; RebuildFiltered(); }),
                () => ToggleSort("Reason"));
        }

        private void ToggleSort(string col)
        {
            if (sortColumn == col) sortAscending = !sortAscending;
            else { sortColumn = col; sortAscending = true; }
            RebuildFiltered();
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        private void DrawRow(float x, float y, float width, float reasonWidth, int rowIndex, RowVm row, bool zebra)
        {
            Rect r = new Rect(x, y, width, RowHeight);
            if (row.isStay)
                Widgets.DrawBoxSolid(r, new Color(0.35f, 0.35f, 0.35f, 0.18f));
            else if (zebra)
                Widgets.DrawHighlight(r);
            if (Mouse.IsOver(r)) Widgets.DrawLightHighlight(r);

            GameFont prevFont = Text.Font;
            Text.Font = GameFont.Tiny;
            float curX = x + TablePad;

            Rect removeRect = new Rect(curX + 2f, y + (RowHeight - 18f) * 0.5f, 18f, 18f);
            TooltipHandler.TipRegion(removeRect, cachedRemoveTip);
            GUI.color = RemoveTint;
            if (Widgets.ButtonImage(removeRect, RemoveIcon))
            {
                GUI.color = Color.white;
                Text.Font = prevFont;
                RemoveRow(rowIndex);
                return;
            }
            GUI.color = Color.white;
            curX += ColRemove;

            // Origin icon (jump) + name
            Rect originIconRect = new Rect(
                curX + LocIconPad,
                y + (RowHeight - LocIconDrawSize) * 0.5f,
                LocIconDrawSize,
                LocIconDrawSize);
            if (row.originIcon != null)
            {
                GUI.color = row.originIconColor;
                GUI.DrawTexture(originIconRect, row.originIcon, ScaleMode.ScaleToFit);
                GUI.color = Color.white;
                TooltipHandler.TipRegion(originIconRect, "TSA_WD_AllPlayerPawns_JumpTip".Translate(row.originLabel));
                if (Widgets.ButtonInvisible(originIconRect))
                    JumpToOrigin(row.entry);
            }
            curX += ColOriginIcon;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(curX, y, ColOriginName - 4f, RowHeight), row.originLabel.Truncate(ColOriginName - 8f));
            curX += ColOriginName;

            Rect portraitCell = new Rect(curX, y, ColPortrait, RowHeight);
            Texture portrait = PawnPortraitUIUtils.GetPortrait(
                row.pawn, new Vector2(PortraitSize, PortraitSize), row.portraitKey);
            Rect portraitRect = new Rect(
                portraitCell.x + (portraitCell.width - PortraitSize) * 0.5f,
                portraitCell.y + (portraitCell.height - PortraitSize) * 0.5f,
                PortraitSize, PortraitSize);
            if (portrait != null)
                GUI.DrawTexture(portraitRect, portrait, ScaleMode.ScaleToFit);
            else
                Widgets.DrawBoxSolid(portraitRect, new Color(0.2f, 0.2f, 0.2f, 0.5f));
            if (Widgets.ButtonInvisible(portraitCell))
                OpenPawnInfo(row.pawn);
            curX += ColPortrait;

            Rect pawnNameRect = new Rect(curX, y, ColPawnName - 4f, RowHeight);
            Widgets.Label(pawnNameRect, row.pawnName.Truncate(ColPawnName - 8f));
            if (Widgets.ButtonInvisible(pawnNameRect))
                OpenPawnInfo(row.pawn);
            curX += ColPawnName;

            // Dest icon (jump) + Destination chrome button
            Rect destIconRect = new Rect(
                curX + LocIconPad,
                y + (RowHeight - LocIconDrawSize) * 0.5f,
                LocIconDrawSize,
                LocIconDrawSize);
            if (row.destIcon != null)
            {
                GUI.color = row.destIconColor;
                GUI.DrawTexture(destIconRect, row.destIcon, ScaleMode.ScaleToFit);
                GUI.color = Color.white;
                TooltipHandler.TipRegion(destIconRect, "TSA_WD_AllPlayerPawns_JumpTip".Translate(row.destLabel));
                if (Widgets.ButtonInvisible(destIconRect))
                    JumpToDest(row.dest);
            }
            curX += ColDestIcon;

            Rect destBtnRect = new Rect(curX + 2f, y + 6f, ColDest - 6f, RowHeight - 12f);
            TooltipHandler.TipRegion(destBtnRect, cachedOverrideTip);
            Color? destFill = row.isOverridden ? OverrideChangedFill : (Color?)null;
            if (WorldDomination_UIUtils.ButtonTextWithIcon(
                    destBtnRect,
                    null,
                    row.overrideLabel.Truncate(ColDest - 16f),
                    centerContents: true,
                    fill: destFill,
                    font: GameFont.Tiny))
            {
                OpenOverridePicker(rowIndex);
            }
            curX += ColDest + ColOverrideGap;

            Rect reasonRect = new Rect(curX, y, reasonWidth - 4f, RowHeight);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(reasonRect, row.reasonText);
            if (!row.reasonTip.NullOrEmpty())
                TooltipHandler.TipRegion(reasonRect, row.reasonTip);
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = prevFont;
        }
    }
}
