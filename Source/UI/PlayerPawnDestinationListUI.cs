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
    /// Shared destination list chrome for Transfer and Smart Send Override (build/sort/draw only; no commit).
    /// </summary>
    public static class PlayerPawnDestinationListUI
    {
        public const float HeaderHeight = 28f;
        public const float RowHeight = 40f;
        public const float SectionHeaderHeight = 28f;
        public const float ScrollbarWidth = 16f;
        public const float ColIcon = 48f;
        public const float ColHumanoids = 110f;
        public const float ColCumSkill = 110f;
        public const float ColDist = 110f;
        public const float ColAction = 100f;
        public const float ColJump = 90f;

        public struct DestinationRow
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

        public static float FixedColsWidth => ColIcon + ColHumanoids + ColCumSkill + ColDist + ColAction + ColJump;

        public static void Rebuild(
            List<DestinationRow> destinations,
            List<PlayerPawnRosterEntry> selected,
            string searchTerm,
            bool offerExitHere,
            string sortColumn,
            bool sortAscending)
        {
            destinations.Clear();
            string? searchLower = string.IsNullOrEmpty(searchTerm) ? null : searchTerm.ToLowerInvariant();
            WorldObject_WD_Outpost? soleSourceOutpost = GetSoleSourceOutpost(selected);
            MapParent? soleSourceColony = GetSoleSourceColony(selected);
            int originTile = GetOriginTile(soleSourceOutpost, soleSourceColony);

            Faction player = Faction.OfPlayer;
            if (player == null) return;

            bool selectionHasPrisoner = SelectionIncludesPrisoner(selected);
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
                    string? exitTip = selectionHasPrisoner
                        ? "TSA_WD_PawnTransfer_PrisonerNoExitHere".Translate()
                        : null;
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
                        disabled = selectionHasPrisoner,
                        disabledTip = exitTip,
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
                    string? disabledTip = disabled ? "TSA_WD_PawnTransfer_SameDestination".Translate() : null;
                    if (!disabled && selectionHasPrisoner)
                    {
                        if (outpost.ManualDefenseActive)
                        {
                            disabled = true;
                            disabledTip = "TSA_WD_OutpostDefense_FrozenDuringManualDefense".Translate();
                        }
                        else if (!outpost.TakePrisoners)
                        {
                            disabled = true;
                            disabledTip = "TSA_WD_PawnTransfer_DestRefusesPrisoners".Translate();
                        }
                    }
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
                        disabledTip = disabledTip,
                        jumpTarget = outpost
                    });
                }
            }

            Sort(destinations, sortColumn, sortAscending);
        }

        public static void Sort(List<DestinationRow> destinations, string sortColumn, bool sortAscending)
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

        public static float EstimateContentHeight(int destinationCount, bool offerExitHere)
        {
            float h = 0f;
            if (offerExitHere) h += SectionHeaderHeight + 8f;
            h += SectionHeaderHeight + 8f;
            h += SectionHeaderHeight + 8f;
            h += destinationCount * RowHeight;
            h += 60f;
            return h;
        }

        public static void DrawSectionHeader(ref float y, float width, string title)
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

        public static void DrawTableHeader(
            Rect hRect,
            float colNameWidth,
            string sortColumn,
            bool sortAscending,
            Action<string> onSort,
            string actionHeaderLabel)
        {
            float curX = hRect.x;
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            DrawSortHeader(ref curX, ColIcon, "TSA_WD_Prisoners_SmartAssignColType".Translate(), "Type", hRect, TextAnchor.LowerCenter, sortColumn, sortAscending, onSort);
            DrawSortHeader(ref curX, colNameWidth, "TSA_WD_PawnCol_Name".Translate(), "Name", hRect, TextAnchor.LowerLeft, sortColumn, sortAscending, onSort);
            DrawSortHeader(ref curX, ColHumanoids, "TSA_WD_Outpost_Pawns_Humanoids".Translate(), "Humanoids", hRect, TextAnchor.LowerCenter, sortColumn, sortAscending, onSort);
            DrawSortHeader(ref curX, ColCumSkill, "TSA_WD_Prisoners_SmartAssignColCumSkill".Translate(), "CumSkill", hRect, TextAnchor.LowerCenter, sortColumn, sortAscending, onSort);
            DrawSortHeader(ref curX, ColDist, "TSA_WD_Outpost_Dist".Translate(), "Dist", hRect, TextAnchor.LowerCenter, sortColumn, sortAscending, onSort);

            Rect actionHdr = new Rect(curX, hRect.y, ColAction, hRect.height);
            Text.Anchor = TextAnchor.LowerCenter;
            Widgets.Label(actionHdr, actionHeaderLabel);
            curX += ColAction;

            Rect jumpHdr = new Rect(curX, hRect.y, ColJump, hRect.height);
            Widgets.Label(jumpHdr, "TSA_WD_ActiveTravelers_H_Jump".Translate());

            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private static void DrawSortHeader(
            ref float curX,
            float width,
            string label,
            string tag,
            Rect hRect,
            TextAnchor anchor,
            string sortColumn,
            bool sortAscending,
            Action<string> onSort)
        {
            Rect headerRect = new Rect(curX, hRect.y, width, hRect.height);
            if (Mouse.IsOver(headerRect)) Widgets.DrawHighlight(headerRect);
            Text.Anchor = anchor;
            string headerText = label + (sortColumn == tag ? (sortAscending ? " ▲" : " ▼") : "");
            Widgets.Label(headerRect, headerText.Truncate(width - 4f));
            if (Widgets.ButtonInvisible(headerRect))
            {
                onSort?.Invoke(tag);
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
            curX += width;
        }

        /// <summary>Draws one destination row. actionLabel e.g. Send Here / Select; onAction when clicked and enabled.</summary>
        public static void DrawRow(
            ref float y,
            float width,
            float colNameWidth,
            DestinationRow row,
            bool zebra,
            string actionLabel,
            Func<DestinationRow, string> getActionTip,
            Func<DestinationRow, bool> isActionEnabled,
            Action<DestinationRow> onAction)
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

            Rect actionBtn = new Rect(curX + 4f, r.y + 6f, ColAction - 8f, r.height - 12f);
            bool enabled = isActionEnabled == null || isActionEnabled(row);
            string tip = getActionTip != null ? getActionTip(row) : row.disabledTip;
            TooltipHandler.TipRegion(actionBtn, tip ?? "");
            GUI.enabled = enabled && !row.disabled;
            if (Widgets.ButtonText(actionBtn, actionLabel))
            {
                onAction?.Invoke(row);
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
            GUI.enabled = true;
            curX += ColAction;

            Rect jumpBtn = new Rect(curX + 4f, r.y + 6f, ColJump - 8f, r.height - 12f);
            GUI.enabled = row.jumpTarget != null && !row.jumpTarget.Destroyed;
            if (row.jumpTarget != null)
                TooltipHandler.TipRegion(jumpBtn, "TSA_WD_JumpToOutpost".Translate(row.label));
            if (Widgets.ButtonText(jumpBtn, "TSA_WD_ActiveTravelers_Jump".Translate()) && row.jumpTarget != null)
                WorldDomination_UIUtils.JumpToWorldObjectOnMap(row.jumpTarget);
            GUI.enabled = true;

            y += RowHeight;
        }

        public static void DrawKindSections(
            ref float y,
            float tableWidth,
            float colNameWidth,
            List<DestinationRow> destinations,
            bool offerExitHere,
            ref int zebraIndex,
            string actionLabel,
            Func<DestinationRow, string> getActionTip,
            Func<DestinationRow, bool> isActionEnabled,
            Action<DestinationRow> onAction)
        {
            if (offerExitHere)
            {
                DrawSectionHeader(ref y, tableWidth, "TSA_WD_PawnTransfer_ExitHere".Translate());
                bool drewExit = false;
                for (int i = 0; i < destinations.Count; i++)
                {
                    DestinationRow row = destinations[i];
                    if (row.dest.kind != PlayerPawnTransferDestinationKind.ExitHere) continue;
                    DrawRow(ref y, tableWidth, colNameWidth, row, zebraIndex++ % 2 == 0, actionLabel, getActionTip, isActionEnabled, onAction);
                    drewExit = true;
                }
                if (!drewExit)
                    DrawEmptyHint(ref y, tableWidth, "TSA_WD_PawnTransfer_ExitHereUnavailable".Translate());
            }

            DrawSectionHeader(ref y, tableWidth, "TSA_WD_PawnTransfer_Colonies".Translate());
            bool drewColony = false;
            for (int i = 0; i < destinations.Count; i++)
            {
                DestinationRow row = destinations[i];
                if (row.dest.kind != PlayerPawnTransferDestinationKind.Colony) continue;
                DrawRow(ref y, tableWidth, colNameWidth, row, zebraIndex++ % 2 == 0, actionLabel, getActionTip, isActionEnabled, onAction);
                drewColony = true;
            }
            if (!drewColony)
                DrawEmptyHint(ref y, tableWidth, "TSA_WD_PawnTransfer_NoColonies".Translate());

            DrawSectionHeader(ref y, tableWidth, "TSA_WD_PawnTransfer_Outposts".Translate());
            bool drewOutpost = false;
            for (int i = 0; i < destinations.Count; i++)
            {
                DestinationRow row = destinations[i];
                if (row.dest.kind != PlayerPawnTransferDestinationKind.Outpost) continue;
                DrawRow(ref y, tableWidth, colNameWidth, row, zebraIndex++ % 2 == 0, actionLabel, getActionTip, isActionEnabled, onAction);
                drewOutpost = true;
            }
            if (!drewOutpost)
                DrawEmptyHint(ref y, tableWidth, "TSA_WD_PawnTransfer_NoOutposts".Translate());
        }

        private static void DrawEmptyHint(ref float y, float tableWidth, string text)
        {
            GUI.color = Color.gray;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(8f, y, tableWidth - 16f, 22f), text);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            y += 26f;
        }

        public static bool OriginsAllInDropPodRange(List<PlayerPawnRosterEntry> selected, int destTile)
        {
            for (int i = 0; i < selected.Count; i++)
            {
                PlayerPawnRosterEntry e = selected[i];
                WorldObject origin = e.sourceOutpost != null ? (WorldObject)e.sourceOutpost : e.mapParent;
                if (origin == null) return false;
                if (!PlayerPawnDropPodUtility.InDropPodRange(origin, destTile))
                    return false;
            }
            return true;
        }

        public static WorldObject_WD_Outpost? GetSoleSourceOutpost(List<PlayerPawnRosterEntry> selected)
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

        public static MapParent? GetSoleSourceColony(List<PlayerPawnRosterEntry> selected)
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

        private static bool SelectionIncludesPrisoner(List<PlayerPawnRosterEntry> selected)
        {
            if (selected == null) return false;
            for (int i = 0; i < selected.Count; i++)
            {
                if (selected[i]?.outpostRole == PlayerPawnOutpostRole.Prisoner)
                    return true;
            }
            return false;
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
            cumSkill = hasSkill ? OutpostSkillScaling.ToEffective(cumSkillRaw, outpost.def) : 0f;
            cumSkillDisplay = hasSkill ? OutpostSkillScaling.FormatRawEffective(cumSkillRaw, outpost.def) : "";
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
    }
}
