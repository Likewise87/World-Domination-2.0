using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace TSA_WorldDomination
{
    /// <summary>Toggle which player outposts Smart Assign may pick as recruit destinations.</summary>
    public class Dialog_SmartAssignOutpostFilter : Window
    {
        private const float HeaderHeight = 28f;
        private const float RowHeight = 40f;
        private const float ScrollbarWidth = 16f;
        private const float ColIcon = 48f;
        private const float ColHumanoids = 110f;
        private const float ColCumSkill = 110f;
        private const float ColDist = 110f;
        private const float ColSelect = 40f;
        private const float ColJump = 90f;

        private Vector2 scrollPos;
        private string searchTerm = "";
        private string sortColumn = "Name";
        private bool sortAscending = true;
        private float colNameWidth = 170f;
        private List<OutpostRow> rows = new List<OutpostRow>();
        private readonly HashSet<string> allowedThingIds = new HashSet<string>();

        private struct OutpostRow
        {
            public WorldObject_WD_Outpost outpost;
            public string label;
            public string typeLabel;
            public string skillName;
            public Texture2D icon;
            public Color iconColor;
            public int humanoidCount;
            public float cumSkill;
            public string cumSkillDisplay;
            public bool hasSkill;
            public int distance;
        }

        public override Vector2 InitialSize => new Vector2(850f, 620f);

        public Dialog_SmartAssignOutpostFilter()
        {
            doCloseX = true;
            closeOnCancel = true;
            absorbInputAroundWindow = true;
            forcePause = false;
            RebuildRows();
        }

        public override void PostClose()
        {
            WdWindowEsc.ClearTextFocusOnClose();
            base.PostClose();
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (WdWindowEsc.TryCloseOnCancel(this))
                return;

            PawnRosterPaintSelect.BeginFrame(this);
            SyncAllowedThingIdsFromSchedule();

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 32f), "TSA_WD_Prisoners_SmartAssignConfigTitle".Translate());

            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(0f, 34f, inRect.width, 36f), "TSA_WD_Prisoners_SmartAssignConfigSubtitle".Translate());

            string oldSearch = searchTerm;
            Rect searchRect = new Rect(0f, 74f, inRect.width, 28f);
            searchTerm = Widgets.TextField(searchRect, searchTerm);
            if (searchTerm != oldSearch) RebuildRows();

            if (string.IsNullOrEmpty(searchTerm))
            {
                GUI.color = new Color(1f, 1f, 1f, 0.4f);
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(searchRect, "  " + "TSA_WD_PawnTransfer_SearchDest".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
            }

            float listY = 110f;
            float fixedCols = ColIcon + ColHumanoids + ColCumSkill + ColDist + ColSelect + ColJump;
            float contentWidth = Mathf.Max(fixedCols + 120f, inRect.width - ScrollbarWidth);
            colNameWidth = Mathf.Max(120f, contentWidth - fixedCols);
            float tableWidth = fixedCols + colNameWidth;

            Rect headerRect = new Rect(0f, listY, tableWidth, HeaderHeight);
            DrawTableHeader(headerRect);
            Widgets.DrawLineHorizontal(0f, headerRect.yMax, tableWidth);

            listY = headerRect.yMax + 4f;
            float listH = inRect.height - listY - 10f;
            float contentH = rows.Count * RowHeight + 8f;
            Rect scrollOuter = new Rect(0f, listY, inRect.width, listH);
            Rect viewRect = new Rect(0f, 0f, tableWidth, Mathf.Max(contentH, listH));
            Widgets.BeginScrollView(scrollOuter, ref scrollPos, viewRect);

            float y = 0f;
            if (rows.Count == 0)
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(8f, y, tableWidth - 16f, 22f), "TSA_WD_PawnTransfer_NoOutposts".Translate());
                GUI.color = Color.white;
            }
            else
            {
                for (int i = 0; i < rows.Count; i++)
                    DrawRow(ref y, tableWidth, rows[i], i % 2 == 0);
            }

            Widgets.EndScrollView();
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        private void DrawTableHeader(Rect hRect)
        {
            float curX = hRect.x;
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            DrawSortHeader(ref curX, ColIcon, "TSA_WD_Prisoners_SmartAssignColType".Translate(), "Type", hRect, TextAnchor.LowerCenter);
            DrawSortHeader(ref curX, colNameWidth, "TSA_WD_PawnCol_Name".Translate(), "Name", hRect, TextAnchor.LowerLeft);
            DrawSortHeader(ref curX, ColHumanoids, "TSA_WD_Outpost_Pawns_Humanoids".Translate(), "Humanoids", hRect, TextAnchor.LowerCenter);
            DrawSortHeader(ref curX, ColCumSkill, "TSA_WD_Prisoners_SmartAssignColCumSkill".Translate(), "CumSkill", hRect, TextAnchor.LowerCenter);
            DrawSortHeader(ref curX, ColDist, "TSA_WD_Outpost_Dist".Translate(), "Dist", hRect, TextAnchor.LowerCenter);
            DrawSelectAllHeader(ref curX, hRect);

            Rect jumpHdr = new Rect(curX, hRect.y, ColJump, hRect.height);
            Text.Anchor = TextAnchor.LowerCenter;
            Widgets.Label(jumpHdr, "TSA_WD_ActiveTravelers_H_Jump".Translate());

            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawSortHeader(ref float curX, float width, string label, string tag, Rect hRect, TextAnchor anchor)
        {
            Rect headerRect = new Rect(curX, hRect.y, width, hRect.height);
            if (Mouse.IsOver(headerRect)) Widgets.DrawHighlight(headerRect);
            Text.Anchor = anchor;
            string headerText = label + (sortColumn == tag ? (sortAscending ? " ▲" : " ▼") : "");
            Widgets.Label(headerRect, headerText.Truncate(width - 4f));
            if (Widgets.ButtonInvisible(headerRect))
            {
                if (sortColumn == tag) sortAscending = !sortAscending;
                else { sortColumn = tag; sortAscending = true; }
                SortRows();
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
            curX += width;
        }

        private void DrawSelectAllHeader(ref float curX, Rect hRect)
        {
            Rect selHdr = new Rect(curX, hRect.y, ColSelect, hRect.height);
            if (Mouse.IsOver(selHdr)) Widgets.DrawHighlight(selHdr);

            bool allAllowed = AreAllVisibleAllowed();
            float box = 18f;
            float cx = selHdr.x + (ColSelect - box) * 0.5f;
            float cy = selHdr.y + (HeaderHeight - box) * 0.5f;
            Widgets.CheckboxDraw(cx, cy, allAllowed, rows.Count == 0, box);
            TooltipHandler.TipRegion(selHdr, "TSA_WD_Prisoners_SmartAssignSelectColumnTip".Translate());
            if (rows.Count > 0 && Widgets.ButtonInvisible(selHdr))
            {
                SetAllVisibleExcluded(allAllowed);
                SyncAllowedThingIdsFromSchedule();
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
            curX += ColSelect;
        }

        private void DrawRow(ref float y, float width, OutpostRow row, bool zebra)
        {
            Rect r = new Rect(0f, y, width, RowHeight);
            if (zebra) Widgets.DrawHighlight(r);
            if (Mouse.IsOver(r)) Widgets.DrawLightHighlight(r);

            var schedule = WorldComponent_PrisonerRecruitSchedule.Get();
            bool allowed = schedule == null || !schedule.IsSmartAssignExcluded(row.outpost);

            float curX = r.x;
            Color dim = new Color(1f, 1f, 1f, 0.45f);

            Rect iconRect = new Rect(curX + (ColIcon - 28f) * 0.5f, r.y + (r.height - 28f) * 0.5f, 28f, 28f);
            if (row.icon != null)
            {
                GUI.color = allowed ? row.iconColor : new Color(row.iconColor.r, row.iconColor.g, row.iconColor.b, 0.45f);
                GUI.DrawTexture(iconRect, row.icon, ScaleMode.ScaleToFit);
                GUI.color = Color.white;
            }
            TooltipHandler.TipRegion(new Rect(curX, r.y, ColIcon, r.height), row.typeLabel);
            curX += ColIcon;

            if (!allowed) GUI.color = dim;
            Text.Anchor = TextAnchor.MiddleLeft;
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(curX, r.y, colNameWidth - 4f, r.height), row.label.Truncate(colNameWidth - 8f));
            curX += colNameWidth;

            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(curX, r.y, ColHumanoids, r.height), row.humanoidCount.ToString());
            curX += ColHumanoids;

            string skillLabel = row.hasSkill ? (string.IsNullOrEmpty(row.cumSkillDisplay) ? row.cumSkill.ToString("F0") : row.cumSkillDisplay) : "—";
            Widgets.Label(new Rect(curX, r.y, ColCumSkill, r.height), skillLabel);
            if (row.hasSkill)
                TooltipHandler.TipRegion(new Rect(curX, r.y, ColCumSkill, r.height),
                    "TSA_WD_Prisoners_SmartAssignCumSkillTip".Translate(row.skillName, skillLabel));
            curX += ColCumSkill;

            Widgets.Label(new Rect(curX, r.y, ColDist, r.height), row.distance.ToString());
            curX += ColDist;
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;

            string tid = row.outpost != null ? row.outpost.ID.ToString() : null;
            float checkSize = 24f;
            float cx = curX + (ColSelect - checkSize) * 0.5f;
            float cy = r.y + (r.height - checkSize) * 0.5f;
            Rect selRect = new Rect(curX, r.y, ColSelect, r.height);
            bool wasAllowed = !tid.NullOrEmpty() && allowedThingIds.Contains(tid);
            PawnRosterPaintSelect.Draw(this, selRect, cx, cy, checkSize, tid, allowedThingIds, canInteract: true);
            bool nowAllowed = !tid.NullOrEmpty() && allowedThingIds.Contains(tid);
            if (wasAllowed != nowAllowed && schedule != null && row.outpost != null)
            {
                schedule.SetSmartAssignExcluded(row.outpost, excluded: !nowAllowed);
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
            curX += ColSelect;

            Rect jumpBtn = new Rect(curX + 4f, r.y + 6f, ColJump - 8f, r.height - 12f);
            TooltipHandler.TipRegion(jumpBtn, "TSA_WD_JumpToOutpost".Translate(row.label));
            if (Widgets.ButtonText(jumpBtn, "TSA_WD_ActiveTravelers_Jump".Translate()))
                WorldDomination_UIUtils.JumpToWorldObjectOnMap(row.outpost);

            y += RowHeight;
        }

        private void SyncAllowedThingIdsFromSchedule()
        {
            allowedThingIds.Clear();
            var schedule = WorldComponent_PrisonerRecruitSchedule.Get();
            for (int i = 0; i < rows.Count; i++)
            {
                WorldObject_WD_Outpost o = rows[i].outpost;
                if (o == null) continue;
                if (schedule == null || !schedule.IsSmartAssignExcluded(o))
                    allowedThingIds.Add(o.ID.ToString());
            }
        }

        private bool AreAllVisibleAllowed()
        {
            if (rows.Count == 0) return false;
            var schedule = WorldComponent_PrisonerRecruitSchedule.Get();
            if (schedule == null) return true;
            for (int i = 0; i < rows.Count; i++)
            {
                if (schedule.IsSmartAssignExcluded(rows[i].outpost))
                    return false;
            }
            return true;
        }

        private void SetAllVisibleExcluded(bool excluded)
        {
            var schedule = WorldComponent_PrisonerRecruitSchedule.Get();
            if (schedule == null) return;
            for (int i = 0; i < rows.Count; i++)
                schedule.SetSmartAssignExcluded(rows[i].outpost, excluded);
        }

        private void RebuildRows()
        {
            rows.Clear();
            string searchLower = string.IsNullOrEmpty(searchTerm) ? null : searchTerm.ToLowerInvariant();
            Faction player = Faction.OfPlayer;
            if (player == null) return;

            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            int playerTile = -1;
            var settlements = Find.WorldObjects?.Settlements;
            if (settlements != null)
            {
                for (int si = 0; si < settlements.Count; si++)
                {
                    if (settlements[si]?.Faction == player && settlements[si].Tile.Valid)
                    {
                        playerTile = settlements[si].Tile.tileId;
                        break;
                    }
                }
            }

            var allWo = Find.WorldObjects?.AllWorldObjects;
            if (allWo == null) return;

            for (int wi = 0; wi < allWo.Count; wi++)
            {
                if (allWo[wi] is not WorldObject_WD_Outpost outpost || outpost.Faction != player) continue;
                string label = outpost.LabelCap;
                string typeLabel = outpost.def?.LabelCap ?? "TSA_WD_AllPlayerPawns_LocOutpost".Translate();
                if (searchLower != null
                    && !label.ToLowerInvariant().Contains(searchLower)
                    && !typeLabel.ToLowerInvariant().Contains(searchLower))
                    continue;

                string skillName = WorldObject_WD_Outpost.GetRelevantSkillName(outpost.def);
                var skillDefs = WorldObject_WD_Outpost.GetRelevantSkillDefs(outpost.def);
                bool hasSkill = skillDefs != null && skillDefs.Count > 0;
                float cumSkillRaw = hasSkill ? outpost.GetTotalRelevantSkillRaw() : 0f;
                float cumSkill = hasSkill ? OutpostSkillScaling.ToEffective(cumSkillRaw) : 0f;
                string cumSkillLabel = hasSkill ? OutpostSkillScaling.FormatRawEffective(cumSkillRaw) : "";
                if (!hasSkill || skillName.NullOrEmpty() || skillName == "—")
                {
                    hasSkill = false;
                    skillName = "";
                }

                int dist = 999;
                if (playerTile >= 0 && outpost.Tile.Valid && manager != null)
                    dist = WorldActions_Utils.GetDistance(outpost.Tile.tileId, playerTile, manager);
                else if (playerTile >= 0 && outpost.Tile.Valid)
                    dist = Mathf.RoundToInt(Find.WorldGrid.ApproxDistanceInTiles(outpost.Tile.tileId, playerTile));

                rows.Add(new OutpostRow
                {
                    outpost = outpost,
                    label = label,
                    typeLabel = typeLabel,
                    skillName = skillName,
                    icon = outpost.def?.ExpandingIconTexture,
                    iconColor = outpost.Faction?.Color ?? Color.white,
                    humanoidCount = outpost.PawnCount,
                    cumSkill = cumSkill,
                    cumSkillDisplay = cumSkillLabel,
                    hasSkill = hasSkill,
                    distance = dist
                });
            }

            SortRows();
        }

        private void SortRows()
        {
            bool asc = sortAscending;
            rows.Sort((a, b) =>
            {
                int cmp = sortColumn switch
                {
                    "Type" => string.Compare(a.typeLabel, b.typeLabel, StringComparison.OrdinalIgnoreCase),
                    "Humanoids" => a.humanoidCount.CompareTo(b.humanoidCount),
                    "CumSkill" => a.cumSkill.CompareTo(b.cumSkill),
                    "Dist" => a.distance.CompareTo(b.distance),
                    _ => string.Compare(a.label, b.label, StringComparison.OrdinalIgnoreCase)
                };
                if (cmp == 0)
                    cmp = string.Compare(a.label, b.label, StringComparison.OrdinalIgnoreCase);
                return asc ? cmp : -cmp;
            });
        }
    }
}
