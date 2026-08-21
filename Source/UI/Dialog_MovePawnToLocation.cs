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
    /// Destination picker for pawn transfer. Table columns and styling match
    /// <see cref="Dialog_SmartAssignOutpostFilter"/> (zebra rows, sort headers, jump).
    /// </summary>
    public class Dialog_MovePawnToLocation : Window
    {
        private const float HeaderHeight = 28f;
        private const float RowHeight = 40f;
        private const float SectionHeaderHeight = 28f;
        private const float ScrollbarWidth = 16f;
        private const float ColIcon = 48f;
        private const float ColHumanoids = 110f;
        private const float ColCumSkill = 110f;
        private const float ColDist = 110f;
        private const float ColTransfer = 100f;
        private const float ColJump = 90f;

        private readonly List<PlayerPawnRosterEntry> selected;
        private readonly Action? onTransferred;
        private readonly bool offerExitHere;

        private Vector2 scrollPos;
        private string searchTerm = "";
        private string sortColumn = "Name";
        private bool sortAscending = true;
        private float colNameWidth = 170f;
        private readonly List<DestinationRow> destinations = new List<DestinationRow>();

        private struct DestinationRow
        {
            public PlayerPawnTransferDestination dest;
            public string label;
            public string typeLabel;
            public string skillName;
            public Texture2D? icon;
            public Color iconColor;
            public int humanoidCount;
            public float cumSkill;
            public string cumSkillDisplay;
            public bool hasSkill;
            public int distance;
            public bool disabled;
            public string? disabledTip;
            public WorldObject? jumpTarget;
        }

        public override Vector2 InitialSize => new Vector2(850f, 620f);

        public Dialog_MovePawnToLocation(List<PlayerPawnRosterEntry> selected, Action? onTransferred = null, bool offerExitHere = false)
        {
            this.selected = selected ?? new List<PlayerPawnRosterEntry>();
            this.onTransferred = onTransferred;
            this.offerExitHere = offerExitHere;
            doCloseX = true;
            closeOnCancel = true;
            absorbInputAroundWindow = true;
            forcePause = false;
            RebuildDestinations();
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

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 32f), "TSA_WD_PawnTransfer_DialogTitle".Translate());

            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(0f, 34f, inRect.width, 36f),
                "TSA_WD_PawnTransfer_DialogSubtitle".Translate(selected.Count.ToString()));

            string oldSearch = searchTerm;
            Rect searchRect = new Rect(0f, 74f, inRect.width, 28f);
            searchTerm = Widgets.TextField(searchRect, searchTerm);
            if (searchTerm != oldSearch) RebuildDestinations();

            if (string.IsNullOrEmpty(searchTerm))
            {
                GUI.color = new Color(1f, 1f, 1f, 0.4f);
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(searchRect, "  " + "TSA_WD_PawnTransfer_SearchDest".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
            }

            float listY = 110f;
            float fixedCols = ColIcon + ColHumanoids + ColCumSkill + ColDist + ColTransfer + ColJump;
            float contentWidth = Mathf.Max(fixedCols + 120f, inRect.width - ScrollbarWidth);
            colNameWidth = Mathf.Max(120f, contentWidth - fixedCols);
            float tableWidth = fixedCols + colNameWidth;

            Rect headerRect = new Rect(0f, listY, tableWidth, HeaderHeight);
            DrawTableHeader(headerRect);
            Widgets.DrawLineHorizontal(0f, headerRect.yMax, tableWidth);

            listY = headerRect.yMax + 4f;
            float listH = inRect.height - listY - 10f;
            float contentH = EstimateContentHeight();
            Rect scrollOuter = new Rect(0f, listY, inRect.width, listH);
            Rect viewRect = new Rect(0f, 0f, tableWidth, Mathf.Max(contentH, listH));
            Widgets.BeginScrollView(scrollOuter, ref scrollPos, viewRect);

            float y = 0f;
            int zebraIndex = 0;

            if (offerExitHere)
            {
                DrawSectionHeader(ref y, tableWidth, "TSA_WD_PawnTransfer_ExitHere".Translate());
                bool drewExit = false;
                for (int i = 0; i < destinations.Count; i++)
                {
                    DestinationRow row = destinations[i];
                    if (row.dest.kind != PlayerPawnTransferDestinationKind.ExitHere) continue;
                    DrawRow(ref y, tableWidth, row, zebraIndex++ % 2 == 0);
                    drewExit = true;
                }
                if (!drewExit)
                {
                    GUI.color = Color.gray;
                    Text.Font = GameFont.Tiny;
                    Text.Anchor = TextAnchor.MiddleLeft;
                    Widgets.Label(new Rect(8f, y, tableWidth - 16f, 22f), "TSA_WD_PawnTransfer_ExitHereUnavailable".Translate());
                    Text.Anchor = TextAnchor.UpperLeft;
                    GUI.color = Color.white;
                    y += 26f;
                }
            }

            DrawSectionHeader(ref y, tableWidth, "TSA_WD_PawnTransfer_Colonies".Translate());
            bool drewColony = false;
            for (int i = 0; i < destinations.Count; i++)
            {
                DestinationRow row = destinations[i];
                if (row.dest.kind != PlayerPawnTransferDestinationKind.Colony) continue;
                DrawRow(ref y, tableWidth, row, zebraIndex++ % 2 == 0);
                drewColony = true;
            }
            if (!drewColony)
            {
                GUI.color = Color.gray;
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(8f, y, tableWidth - 16f, 22f), "TSA_WD_PawnTransfer_NoColonies".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                y += 26f;
            }

            DrawSectionHeader(ref y, tableWidth, "TSA_WD_PawnTransfer_Outposts".Translate());
            bool drewOutpost = false;
            for (int i = 0; i < destinations.Count; i++)
            {
                DestinationRow row = destinations[i];
                if (row.dest.kind != PlayerPawnTransferDestinationKind.Outpost) continue;
                DrawRow(ref y, tableWidth, row, zebraIndex++ % 2 == 0);
                drewOutpost = true;
            }
            if (!drewOutpost)
            {
                GUI.color = Color.gray;
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(8f, y, tableWidth - 16f, 22f), "TSA_WD_PawnTransfer_NoOutposts".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
            }

            Widgets.EndScrollView();
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        private float EstimateContentHeight()
        {
            float h = 0f;
            if (offerExitHere) h += SectionHeaderHeight + 8f;
            h += SectionHeaderHeight + 8f; // colonies
            h += SectionHeaderHeight + 8f; // outposts
            h += destinations.Count * RowHeight;
            h += 60f; // empty-section placeholders
            return h;
        }

        private void DrawSectionHeader(ref float y, float width, string title)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = Widgets.SeparatorLabelColor;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(4f, y, width - 8f, SectionHeaderHeight), title);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            y += SectionHeaderHeight;
            Widgets.DrawLineHorizontal(0f, y, width);
            y += 4f;
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

            Rect transferHdr = new Rect(curX, hRect.y, ColTransfer, hRect.height);
            Text.Anchor = TextAnchor.LowerCenter;
            Widgets.Label(transferHdr, "TSA_WD_PawnTransfer_SendHere".Translate());
            curX += ColTransfer;

            Rect jumpHdr = new Rect(curX, hRect.y, ColJump, hRect.height);
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
                SortDestinations();
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
            curX += width;
        }

        private void DrawRow(ref float y, float width, DestinationRow row, bool zebra)
        {
            Rect r = new Rect(0f, y, width, RowHeight);
            if (zebra) Widgets.DrawHighlight(r);
            if (!row.disabled && Mouse.IsOver(r)) Widgets.DrawLightHighlight(r);

            float curX = r.x;
            Color dim = new Color(1f, 1f, 1f, 0.45f);
            if (row.disabled) GUI.color = dim;

            Rect iconRect = new Rect(curX + (ColIcon - 28f) * 0.5f, r.y + (r.height - 28f) * 0.5f, 28f, 28f);
            if (row.icon != null)
            {
                Color ic = row.disabled
                    ? new Color(row.iconColor.r, row.iconColor.g, row.iconColor.b, 0.45f)
                    : row.iconColor;
                GUI.color = ic;
                GUI.DrawTexture(iconRect, row.icon, ScaleMode.ScaleToFit);
                GUI.color = row.disabled ? dim : Color.white;
            }
            TooltipHandler.TipRegion(new Rect(curX, r.y, ColIcon, r.height), row.typeLabel);
            curX += ColIcon;

            Text.Anchor = TextAnchor.MiddleLeft;
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(curX, r.y, colNameWidth - 4f, r.height), row.label.Truncate(colNameWidth - 8f));
            curX += colNameWidth;

            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(curX, r.y, ColHumanoids, r.height), row.humanoidCount.ToString());
            curX += ColHumanoids;

            string skillLabel = row.hasSkill
                ? (string.IsNullOrEmpty(row.cumSkillDisplay) ? row.cumSkill.ToString("F0") : row.cumSkillDisplay)
                : "-";
            Widgets.Label(new Rect(curX, r.y, ColCumSkill, r.height), skillLabel);
            if (row.hasSkill)
            {
                TooltipHandler.TipRegion(new Rect(curX, r.y, ColCumSkill, r.height),
                    "TSA_WD_Prisoners_SmartAssignCumSkillTip".Translate(row.skillName, skillLabel));
            }
            curX += ColCumSkill;

            Widgets.Label(new Rect(curX, r.y, ColDist, r.height), row.distance.ToString());
            curX += ColDist;

            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            Rect transferBtn = new Rect(curX + 4f, r.y + 6f, ColTransfer - 8f, r.height - 12f);
            if (!string.IsNullOrEmpty(row.disabledTip))
                TooltipHandler.TipRegion(transferBtn, row.disabledTip);
            else
                TooltipHandler.TipRegion(transferBtn, "TSA_WD_AllPlayerPawns_TransferTip".Translate());
            GUI.enabled = !row.disabled;
            if (Widgets.ButtonText(transferBtn, "TSA_WD_PawnTransfer_SendHere".Translate()))
            {
                PlayerPawnTransferUtility.TryTransfer(selected, row.dest);
                onTransferred?.Invoke();
                Close();
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
            GUI.enabled = true;
            curX += ColTransfer;

            Rect jumpBtn = new Rect(curX + 4f, r.y + 6f, ColJump - 8f, r.height - 12f);
            GUI.enabled = row.jumpTarget != null && !row.jumpTarget.Destroyed;
            if (row.jumpTarget != null)
                TooltipHandler.TipRegion(jumpBtn, "TSA_WD_JumpToOutpost".Translate(row.label));
            if (Widgets.ButtonText(jumpBtn, "TSA_WD_ActiveTravelers_Jump".Translate()) && row.jumpTarget != null)
                WorldDomination_UIUtils.JumpToWorldObjectOnMap(row.jumpTarget);
            GUI.enabled = true;

            y += RowHeight;
        }

        private void RebuildDestinations()
        {
            destinations.Clear();
            string? searchLower = string.IsNullOrEmpty(searchTerm) ? null : searchTerm.ToLowerInvariant();
            WorldObject_WD_Outpost? soleSourceOutpost = GetSoleSourceOutpost();
            MapParent? soleSourceColony = GetSoleSourceColony();
            int originTile = GetOriginTile(soleSourceOutpost, soleSourceColony);

            Faction player = Faction.OfPlayer;
            if (player == null) return;

            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();

            if (offerExitHere && soleSourceOutpost != null)
            {
                string exitLabel = soleSourceOutpost.LabelCap;
                string typeLabel = soleSourceOutpost.def?.LabelCap ?? "TSA_WD_AllPlayerPawns_LocOutpost".Translate();
                if (searchLower == null
                    || exitLabel.ToLowerInvariant().Contains(searchLower)
                    || typeLabel.ToLowerInvariant().Contains(searchLower)
                    || "TSA_WD_PawnTransfer_ExitHere".Translate().ToString().ToLowerInvariant().Contains(searchLower))
                {
                    FillOutpostSkill(soleSourceOutpost, out string skillName, out bool hasSkill, out float cumSkill, out string cumSkillDisplay);
                    destinations.Add(new DestinationRow
                    {
                        dest = new PlayerPawnTransferDestination
                        {
                            kind = PlayerPawnTransferDestinationKind.ExitHere,
                            outpost = soleSourceOutpost
                        },
                        label = exitLabel,
                        typeLabel = "TSA_WD_PawnTransfer_ExitHere".Translate() + " (" + typeLabel + ")",
                        skillName = skillName,
                        icon = soleSourceOutpost.def?.ExpandingIconTexture,
                        iconColor = soleSourceOutpost.Faction?.Color ?? Color.white,
                        humanoidCount = soleSourceOutpost.PawnCount,
                        cumSkill = cumSkill,
                        cumSkillDisplay = cumSkillDisplay,
                        hasSkill = hasSkill,
                        distance = 0,
                        jumpTarget = soleSourceOutpost
                    });
                }
            }

            var settlements = Find.WorldObjects?.Settlements;
            if (settlements != null)
            {
                for (int si = 0; si < settlements.Count; si++)
                {
                    if (settlements[si] is not MapParent mp || mp.Faction != player || !mp.HasMap) continue;
                    string label = PlayerPawnRosterUtility.FormatColonyLabelForDisplay(mp.LabelCap);
                    string typeLabel = "TSA_WD_AllPlayerPawns_LocColony".Translate();
                    if (searchLower != null
                        && !label.ToLowerInvariant().Contains(searchLower)
                        && !mp.LabelCap.ToLowerInvariant().Contains(searchLower)
                        && !typeLabel.ToLowerInvariant().Contains(searchLower))
                        continue;

                    bool disabled = soleSourceColony != null && soleSourceColony == mp;
                    destinations.Add(new DestinationRow
                    {
                        dest = new PlayerPawnTransferDestination
                        {
                            kind = PlayerPawnTransferDestinationKind.Colony,
                            colony = mp
                        },
                        label = label,
                        typeLabel = typeLabel,
                        skillName = "",
                        icon = player.def.FactionIcon,
                        iconColor = player.Color,
                        humanoidCount = CountColonyHumanoids(mp),
                        cumSkill = 0f,
                        cumSkillDisplay = "",
                        hasSkill = false,
                        distance = CalcDistance(originTile, mp.Tile.tileId, manager),
                        disabled = disabled,
                        disabledTip = disabled ? "TSA_WD_PawnTransfer_SameDestination".Translate() : null,
                        jumpTarget = mp
                    });
                }
            }

            var allWo = Find.WorldObjects?.AllWorldObjects;
            if (allWo != null)
            {
                for (int wi = 0; wi < allWo.Count; wi++)
                {
                    if (allWo[wi] is not WorldObject_WD_Outpost outpost || outpost.Faction != player) continue;
                    string label = outpost.LabelCap;
                    string typeLabel = outpost.def?.LabelCap ?? "TSA_WD_AllPlayerPawns_LocOutpost".Translate();
                    if (searchLower != null
                        && !label.ToLowerInvariant().Contains(searchLower)
                        && !typeLabel.ToLowerInvariant().Contains(searchLower))
                        continue;

                    bool disabled = soleSourceOutpost != null && soleSourceOutpost == outpost;
                    FillOutpostSkill(outpost, out string skillName, out bool hasSkill, out float cumSkill, out string cumSkillDisplay);
                    destinations.Add(new DestinationRow
                    {
                        dest = new PlayerPawnTransferDestination
                        {
                            kind = PlayerPawnTransferDestinationKind.Outpost,
                            outpost = outpost
                        },
                        label = label,
                        typeLabel = typeLabel,
                        skillName = skillName,
                        icon = outpost.def?.ExpandingIconTexture,
                        iconColor = outpost.Faction?.Color ?? Color.white,
                        humanoidCount = outpost.PawnCount,
                        cumSkill = cumSkill,
                        cumSkillDisplay = cumSkillDisplay,
                        hasSkill = hasSkill,
                        distance = CalcDistance(originTile, outpost.Tile.tileId, manager),
                        disabled = disabled,
                        disabledTip = disabled ? "TSA_WD_PawnTransfer_SameDestination".Translate() : null,
                        jumpTarget = outpost
                    });
                }
            }

            SortDestinations();
        }

        private static void FillOutpostSkill(
            WorldObject_WD_Outpost outpost,
            out string skillName,
            out bool hasSkill,
            out float cumSkill,
            out string cumSkillDisplay)
        {
            skillName = WorldObject_WD_Outpost.GetRelevantSkillName(outpost.def);
            var skillDefs = WorldObject_WD_Outpost.GetRelevantSkillDefs(outpost.def);
            hasSkill = skillDefs != null && skillDefs.Count > 0;
            float cumSkillRaw = hasSkill ? outpost.GetTotalRelevantSkillRaw() : 0f;
            cumSkill = hasSkill ? OutpostSkillScaling.ToEffective(cumSkillRaw) : 0f;
            cumSkillDisplay = hasSkill ? OutpostSkillScaling.FormatRawEffective(cumSkillRaw) : "";
            if (!hasSkill || skillName.NullOrEmpty() || skillName == "-" || skillName == "—")
            {
                hasSkill = false;
                skillName = "";
                cumSkill = 0f;
                cumSkillDisplay = "";
            }
        }

        private static int CountColonyHumanoids(MapParent mp)
        {
            Map? map = mp?.Map;
            if (map?.mapPawns == null) return 0;
            return map.mapPawns.FreeColonistsCount;
        }

        private static int CalcDistance(int originTile, int destTile, WorldComponent_SpreadManager? manager)
        {
            if (originTile < 0 || destTile < 0) return 999;
            if (originTile == destTile) return 0;
            if (manager != null)
                return WorldActions_Utils.GetDistance(originTile, destTile, manager);
            return Mathf.RoundToInt(Find.WorldGrid.ApproxDistanceInTiles(originTile, destTile));
        }

        private static int GetOriginTile(WorldObject_WD_Outpost? soleOutpost, MapParent? soleColony)
        {
            if (soleOutpost != null && soleOutpost.Tile.Valid)
                return soleOutpost.Tile.tileId;
            if (soleColony != null && soleColony.Tile.Valid)
                return soleColony.Tile.tileId;
            return -1;
        }

        private void SortDestinations()
        {
            bool asc = sortAscending;
            destinations.Sort((a, b) =>
            {
                int kindCmp = a.dest.kind.CompareTo(b.dest.kind);
                if (kindCmp != 0) return kindCmp;

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

        private WorldObject_WD_Outpost? GetSoleSourceOutpost()
        {
            WorldObject_WD_Outpost? sole = null;
            for (int i = 0; i < selected.Count; i++)
            {
                WorldObject_WD_Outpost? op = selected[i].sourceOutpost;
                if (op == null) return null;
                if (sole == null) sole = op;
                else if (sole != op) return null;
            }
            return sole;
        }

        private MapParent? GetSoleSourceColony()
        {
            MapParent? sole = null;
            for (int i = 0; i < selected.Count; i++)
            {
                PlayerPawnRosterEntry e = selected[i];
                if (e.sourceOutpost != null) return null;
                MapParent? mp = e.mapParent;
                if (mp == null) return null;
                if (sole == null) sole = mp;
                else if (sole != mp) return null;
            }
            return sole;
        }
    }
}
