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
    /// Shared roster header filter icon, slate choice tiles, and one-at-a-time anchored dropdowns.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class PawnRosterHeaderFilter
    {
        public const float FilterIconSize = 22f;
        public const float TileH = 28f;
        public const float FieldH = 28f;
        public const float Pad = 10f;
        public const float TitleH = 24f;
        public const float ClearBtnH = 28f;
        public const float DefaultDropdownW = 260f;
        public const string LocationTypeAll = "";
        public const string LocationTypeColony = "Colony";
        public const string LocationTypeOutpost = "Outpost";
        public const string LocationTypeCaravan = "Caravan";
        public const string LocationTypeCamp = "Camp";
        public const string LocationTypePhysicalMap = "PhysicalMap";
        public const string PsycastFilterNone = "__none__";

        private static readonly Texture2D FilterIconTex =
            ContentFinder<Texture2D>.Get("UI/Buttons/OpenSpecificTab", false)
            ?? TexButton.Search
            ?? TexButton.Info;

        private static readonly Color ActiveFilterTint = new Color(0.45f, 0.85f, 1f);
        private static readonly Color IdleFilterTint = new Color(0.85f, 0.85f, 0.85f);

        private static readonly Color NavSlateFill = new Color(0.16f, 0.18f, 0.22f, 0.92f);
        private static readonly Color NavBtnBgHover = new Color(0.22f, 0.26f, 0.32f, 0.96f);
        private static readonly Color NavBtnBgPress = new Color(0.12f, 0.14f, 0.17f, 0.96f);
        private static readonly Color NavBtnBgSelected = new Color(0.22f, 0.26f, 0.32f, 0.96f);
        private static readonly Color NavBtnOutline = new Color(0.55f, 0.62f, 0.72f, 0.42f);
        private static readonly Color NavBtnOutlineHover = new Color(0.78f, 0.84f, 0.92f, 0.72f);
        private static readonly Color NavBtnOutlineSelected = new Color(0.70f, 0.76f, 0.86f, 0.55f);

        private const int DropdownWindowId = 918273645;
        private const string TextControlName = "PawnRosterHeaderFilter_Text";

        private static bool dropdownOpen;
        private static Rect dropdownScreenRect;
        private static Action<Rect> dropdownDraw;
        private static bool focusTextNext;
        private static int openedOnFrame = -1;

        public static bool IsDropdownOpen => dropdownOpen;

        private static Vector2 dropdownListScroll;

        public static void CloseDropdown()
        {
            dropdownOpen = false;
            dropdownDraw = null;
            focusTextNext = false;
            dropdownListScroll = Vector2.zero;
        }

        public static bool TryCloseDropdownOnCancel()
        {
            if (!dropdownOpen || !WdWindowEsc.CancelPressed())
                return false;

            if (WdWindowEsc.HasTextFocus)
            {
                WdWindowEsc.ClearTextFocus();
                WdWindowEsc.ConsumeCancel();
                return true;
            }

            CloseDropdown();
            WdWindowEsc.ConsumeCancel();
            return true;
        }

        /// <summary>Call once per frame from the parent window so the ImmediateWindow stays alive.</summary>
        public static void DrawDropdownIfOpen()
        {
            if (!dropdownOpen || dropdownDraw == null) return;
            Rect r = dropdownScreenRect;
            Find.WindowStack.ImmediateWindow(
                DropdownWindowId,
                r,
                WindowLayer.Super,
                () => dropdownDraw?.Invoke(r.AtZero()),
                doBackground: true,
                absorbInputAroundWindow: true,
                shadowAlpha: 1f,
                doClickOutsideFunc: () =>
                {
                    if (Time.frameCount <= openedOnFrame) return;
                    CloseDropdown();
                });
        }

        public static bool DrawSlateChoice(Rect r, string label, bool selected, string tip = null, string countLabel = null)
        {
            bool mouseOver = Mouse.IsOver(r);
            bool pressed = mouseOver && Input.GetMouseButton(0);
            Color bg = selected ? NavBtnBgSelected : pressed ? NavBtnBgPress : mouseOver ? NavBtnBgHover : NavSlateFill;
            Widgets.DrawBoxSolid(r, bg);
            GUI.color = selected ? NavBtnOutlineSelected : mouseOver ? NavBtnOutlineHover : NavBtnOutline;
            Widgets.DrawBox(r, 1);
            GUI.color = Color.white;

            Text.Font = GameFont.Tiny;
            Rect textRect = r.ContractedBy(6f, 0f);
            if (!countLabel.NullOrEmpty())
            {
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(textRect, label ?? "");
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(textRect, countLabel);
            }
            else
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(r, label ?? "");
            }
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            if (!tip.NullOrEmpty())
                TooltipHandler.TipRegion(r, tip);
            return Widgets.ButtonInvisible(r);
        }

        public static bool DrawFilterableHeader(
            ref float curX,
            float y,
            float width,
            float height,
            string label,
            bool isSorted,
            bool sortAscending,
            TextAnchor labelAnchor,
            bool filterActive,
            string filterTip,
            Action<Rect> onFilterClick,
            Action onSort)
        {
            Rect headerRect = new Rect(curX, y, width, height);
            const float gap = 2f;
            const float edgePad = 2f;
            bool showFilter = onFilterClick != null;

            if (!showFilter)
            {
                if (!label.NullOrEmpty())
                {
                    string arrow = isSorted ? (sortAscending ? " ▲" : " ▼") : "";
                    string headerText = label + arrow;
                    float maxTextW = Mathf.Max(8f, width - edgePad * 2f);
                    if (Text.CalcSize(headerText).x > maxTextW)
                        headerText = label.Truncate(Mathf.Max(8f, maxTextW - 16f)) + arrow;
                    Rect labelRect = IsLeftAnchor(labelAnchor)
                        ? new Rect(headerRect.x + edgePad, y, headerRect.width - edgePad, height)
                        : headerRect;
                    Text.Anchor = labelAnchor;
                    Widgets.Label(labelRect, headerText);
                    Text.Anchor = TextAnchor.UpperLeft;
                }

                if (onSort != null)
                {
                    if (Mouse.IsOver(headerRect)) Widgets.DrawHighlight(headerRect);
                    if (Widgets.ButtonInvisible(headerRect))
                        onSort.Invoke();
                }

                curX += width;
                return false;
            }

            float icon = Mathf.Min(FilterIconSize, height - 2f);
            float iconY = y + (height - icon) * 0.5f;
            Rect iconRect;

            if (label.NullOrEmpty())
            {
                iconRect = new Rect(headerRect.x + (width - icon) * 0.5f, iconY, icon, icon);
            }
            else
            {
                string arrow = isSorted ? (sortAscending ? " ▲" : " ▼") : "";
                string headerText = label + arrow;
                float maxTextW = Mathf.Max(8f, width - icon - gap - edgePad * 2f);
                float textW = Text.CalcSize(headerText).x;
                if (textW > maxTextW)
                {
                    headerText = label.Truncate(Mathf.Max(8f, maxTextW - 16f)) + arrow;
                    textW = Mathf.Min(maxTextW, Text.CalcSize(headerText).x);
                }

                float clusterW = textW + gap + icon;
                float clusterX;
                if (IsRightAnchor(labelAnchor))
                    clusterX = headerRect.xMax - clusterW - edgePad;
                else if (IsLeftAnchor(labelAnchor))
                    clusterX = headerRect.x + edgePad;
                else
                    clusterX = headerRect.x + (width - clusterW) * 0.5f;
                clusterX = Mathf.Clamp(clusterX, headerRect.x + edgePad, Mathf.Max(headerRect.x + edgePad, headerRect.xMax - clusterW - edgePad));

                Rect labelRect = new Rect(clusterX, y, textW, height);
                iconRect = new Rect(clusterX + textW + gap, iconY, icon, icon);
                Text.Anchor = LeftAlignedVertical(labelAnchor);
                Widgets.Label(labelRect, headerText);
                Text.Anchor = TextAnchor.UpperLeft;
            }

            bool overIcon = Mouse.IsOver(iconRect);
            bool overSort = onSort != null && Mouse.IsOver(headerRect) && !overIcon;
            if (overSort) Widgets.DrawHighlight(headerRect);
            if (onSort != null && !overIcon && Widgets.ButtonInvisible(headerRect))
                onSort.Invoke();

            if (Mouse.IsOver(iconRect)) Widgets.DrawHighlight(iconRect);
            Color prev = GUI.color;
            GUI.color = filterActive ? ActiveFilterTint : IdleFilterTint;
            if (FilterIconTex != null)
                GUI.DrawTexture(iconRect, FilterIconTex, ScaleMode.ScaleToFit);
            GUI.color = prev;
            if (!filterTip.NullOrEmpty())
                TooltipHandler.TipRegion(iconRect, filterTip);
            bool filterClicked = Widgets.ButtonInvisible(iconRect);
            if (filterClicked)
            {
                onFilterClick.Invoke(iconRect);
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            curX += width;
            return filterClicked;
        }

        private static bool IsLeftAnchor(TextAnchor a) =>
            a == TextAnchor.UpperLeft || a == TextAnchor.MiddleLeft || a == TextAnchor.LowerLeft;

        private static bool IsRightAnchor(TextAnchor a) =>
            a == TextAnchor.UpperRight || a == TextAnchor.MiddleRight || a == TextAnchor.LowerRight;

        private static TextAnchor LeftAlignedVertical(TextAnchor a)
        {
            switch (a)
            {
                case TextAnchor.UpperLeft:
                case TextAnchor.UpperCenter:
                case TextAnchor.UpperRight:
                    return TextAnchor.UpperLeft;
                case TextAnchor.LowerLeft:
                case TextAnchor.LowerCenter:
                case TextAnchor.LowerRight:
                    return TextAnchor.LowerLeft;
                default:
                    return TextAnchor.MiddleLeft;
            }
        }

        public static void OpenTextDropdown(
            Rect guiAnchor,
            string title,
            string hint,
            Func<string> get,
            Action<string> onChanged,
            Action onCleared,
            float width = DefaultDropdownW)
        {
            float h = Pad * 2f + TitleH + 12f + FieldH + 6f + ClearBtnH;
            Open(guiAnchor, width, h, inner =>
            {
                float y = DrawDropdownTitle(inner, title);
                Rect field = new Rect(inner.x, y, inner.width, FieldH);
                DrawSearchField(field, hint, get?.Invoke() ?? "", onChanged, controlName: TextControlName, requestFocus: true);
                Rect clear = new Rect(inner.x, field.yMax + 6f, inner.width, ClearBtnH);
                if (Widgets.ButtonText(clear, "TSA_WD_TraitFilter_Clear".Translate()))
                {
                    onCleared?.Invoke();
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }
            });
        }

        public static void OpenTwoTextDropdown(
            Rect guiAnchor,
            string title,
            string hintA,
            Func<string> getA,
            Action<string> onChangedA,
            string hintB,
            Func<string> getB,
            Action<string> onChangedB,
            Action onCleared,
            float width = DefaultDropdownW)
        {
            float h = Pad * 2f + TitleH + 12f + FieldH + 6f + FieldH + 6f + ClearBtnH;
            Open(guiAnchor, width, h, inner =>
            {
                float y = DrawDropdownTitle(inner, title);
                Rect a = new Rect(inner.x, y, inner.width, FieldH);
                DrawSearchField(a, hintA, getA?.Invoke() ?? "", onChangedA, controlName: TextControlName, requestFocus: true);
                Rect b = new Rect(inner.x, a.yMax + 6f, inner.width, FieldH);
                DrawSearchField(b, hintB, getB?.Invoke() ?? "", onChangedB, controlName: TextControlName + "_B", requestFocus: false);
                Rect clear = new Rect(inner.x, b.yMax + 6f, inner.width, ClearBtnH);
                if (Widgets.ButtonText(clear, "TSA_WD_TraitFilter_Clear".Translate()))
                {
                    onCleared?.Invoke();
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }
            });
        }

        public static void OpenChoiceDropdown(
            Rect guiAnchor,
            string title,
            IReadOnlyList<HeaderFilterChoice> choices,
            float width = DefaultDropdownW)
        {
            if (choices == null || choices.Count == 0) return;
            int separators = 0;
            for (int i = 0; i < choices.Count; i++)
            {
                if (choices[i].SeparatorAfter) separators++;
            }
            float listH = choices.Count * (TileH + 4f) + separators * 10f;
            float maxListH = 12f * (TileH + 4f);
            bool needScroll = listH > maxListH + 1f;
            float shownListH = needScroll ? maxListH : listH;
            float h = Pad * 2f + TitleH + 12f + shownListH;
            Open(guiAnchor, width, h, inner =>
            {
                float y = DrawDropdownTitle(inner, title);
                float drawW = inner.width;
                if (needScroll)
                {
                    Rect outer = new Rect(inner.x, y, inner.width, shownListH);
                    Rect view = new Rect(0f, 0f, inner.width - 16f, listH);
                    Widgets.BeginScrollView(outer, ref dropdownListScroll, view);
                    DrawChoiceTiles(new Rect(0f, 0f, view.width, listH), choices);
                    Widgets.EndScrollView();
                }
                else
                {
                    DrawChoiceTiles(new Rect(inner.x, y, drawW, listH), choices);
                }
            });
        }

        private static void DrawChoiceTiles(Rect area, IReadOnlyList<HeaderFilterChoice> choices)
        {
            float y = area.y;
            for (int i = 0; i < choices.Count; i++)
            {
                HeaderFilterChoice c = choices[i];
                Rect tile = new Rect(area.x, y, area.width, TileH);
                if (DrawSlateChoice(tile, c.Label, c.Selected, c.Tip, c.CountLabel))
                {
                    c.OnPick?.Invoke();
                    CloseDropdown();
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }
                y += TileH + 4f;
                if (c.SeparatorAfter)
                {
                    Color prev = GUI.color;
                    GUI.color = Color.white;
                    Widgets.DrawLineHorizontal(area.x, y + 2f, area.width);
                    GUI.color = prev;
                    y += 10f;
                }
            }
        }

        public static List<PlayerPawnSortCategory> CategoriesFrom(IReadOnlyList<PlayerPawnRosterEntry> rows)
        {
            var list = new List<PlayerPawnSortCategory>(rows?.Count ?? 0);
            if (rows == null) return list;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i] == null) continue;
                list.Add(rows[i].pawnSortCategory);
            }
            return list;
        }

        public static List<HeaderFilterChoice> TypeChoices(
            PlayerPawnTypeFilter current,
            Action<PlayerPawnTypeFilter> onPick,
            IReadOnlyList<PlayerPawnSortCategory> population = null)
        {
            int total = 0, human = 0, animal = 0, mech = 0, vehicle = 0;
            if (population != null)
            {
                for (int i = 0; i < population.Count; i++)
                {
                    total++;
                    switch (population[i])
                    {
                        case PlayerPawnSortCategory.Human: human++; break;
                        case PlayerPawnSortCategory.Animal: animal++; break;
                        case PlayerPawnSortCategory.Mechanoid: mech++; break;
                        case PlayerPawnSortCategory.Vehicle: vehicle++; break;
                    }
                }
            }

            var list = new List<HeaderFilterChoice>();
            string allCountLabel = null;
            string allTip = null;
            if (population != null)
                FormatFilterCount(total, total, out allCountLabel, out allTip);
            list.Add(new HeaderFilterChoice(
                PlayerPawnRosterUtility.TypeFilterLabel(PlayerPawnTypeFilter.All),
                current == PlayerPawnTypeFilter.All,
                () => onPick?.Invoke(PlayerPawnTypeFilter.All),
                tip: allTip,
                separatorAfter: true,
                countLabel: allCountLabel));

            var typed = new List<(PlayerPawnTypeFilter filter, int count, string label)>();
            foreach (PlayerPawnTypeFilter f in Enum.GetValues(typeof(PlayerPawnTypeFilter)))
            {
                if (f == PlayerPawnTypeFilter.All) continue;
                int n = f switch
                {
                    PlayerPawnTypeFilter.Humanoid => human,
                    PlayerPawnTypeFilter.Animal => animal,
                    PlayerPawnTypeFilter.Mechanoid => mech,
                    PlayerPawnTypeFilter.Vehicle => vehicle,
                    _ => 0
                };
                typed.Add((f, n, PlayerPawnRosterUtility.TypeFilterLabel(f)));
            }
            typed.Sort((a, b) => CompareCountThenLabel(a.count, a.label, b.count, b.label));
            for (int i = 0; i < typed.Count; i++)
            {
                PlayerPawnTypeFilter captured = typed[i].filter;
                string countLabel = null;
                string tip = null;
                if (population != null)
                    FormatFilterCount(typed[i].count, total, out countLabel, out tip);
                list.Add(new HeaderFilterChoice(
                    typed[i].label,
                    current == captured,
                    () => onPick?.Invoke(captured),
                    tip: tip,
                    countLabel: countLabel));
            }
            return list;
        }

        public static List<HeaderFilterChoice> PlayerStarChoices(
            PlayerPawnStarFilter current,
            Action<PlayerPawnStarFilter> onPick,
            IReadOnlyList<StarCountRow> population = null)
        {
            bool show = population != null;
            int total = 0, starred = 0, notStarred = 0, colony = 0, colonyStarred = 0, colonyNot = 0;
            if (show)
            {
                for (int i = 0; i < population.Count; i++)
                {
                    StarCountRow r = population[i];
                    total++;
                    if (r.Starred) starred++;
                    else notStarred++;
                    if (!r.OnColonyMap) continue;
                    colony++;
                    if (r.Starred) colonyStarred++;
                    else colonyNot++;
                }
            }

            var list = new List<HeaderFilterChoice>();
            foreach (PlayerPawnStarFilter f in Enum.GetValues(typeof(PlayerPawnStarFilter)))
            {
                PlayerPawnStarFilter captured = f;
                int n = captured switch
                {
                    PlayerPawnStarFilter.StarredAnywhere => starred,
                    PlayerPawnStarFilter.NotStarredAnywhere => notStarred,
                    PlayerPawnStarFilter.AllColony => colony,
                    PlayerPawnStarFilter.StarredColony => colonyStarred,
                    PlayerPawnStarFilter.NotStarredColony => colonyNot,
                    _ => total
                };
                string tip = PlayerPawnRosterUtility.StarFilterTip(captured);
                AttachCount(n, total, show, ref tip, out string countLabel);
                list.Add(new HeaderFilterChoice(
                    PlayerPawnRosterUtility.StarFilterLabel(captured),
                    current == captured,
                    () => onPick?.Invoke(captured),
                    tip: tip,
                    separatorAfter: captured == PlayerPawnStarFilter.AllAnywhere,
                    countLabel: countLabel));
            }
            return list;
        }

        public static List<StarCountRow> StarRowsFrom(IReadOnlyList<PlayerPawnRosterEntry> rows)
        {
            var list = new List<StarCountRow>(rows?.Count ?? 0);
            if (rows == null) return list;
            for (int i = 0; i < rows.Count; i++)
            {
                PlayerPawnRosterEntry e = rows[i];
                if (e == null) continue;
                list.Add(new StarCountRow(e.isStarred, e.locationKind == PlayerPawnLocationKind.Colony));
            }
            return list;
        }

        public static List<HeaderFilterChoice> OutpostStarChoices(
            OutpostPawnStarFilter current,
            Action<OutpostPawnStarFilter> onPick,
            IReadOnlyList<bool> starred = null)
        {
            bool show = starred != null;
            int total = 0, yes = 0, no = 0;
            if (show)
            {
                for (int i = 0; i < starred.Count; i++)
                {
                    total++;
                    if (starred[i]) yes++;
                    else no++;
                }
            }

            var list = new List<HeaderFilterChoice>();
            foreach (OutpostPawnStarFilter f in Enum.GetValues(typeof(OutpostPawnStarFilter)))
            {
                OutpostPawnStarFilter captured = f;
                int n = captured switch
                {
                    OutpostPawnStarFilter.Starred => yes,
                    OutpostPawnStarFilter.NotStarred => no,
                    _ => total
                };
                string tip = PlayerPawnRosterUtility.OutpostStarFilterTip(captured);
                AttachCount(n, total, show, ref tip, out string countLabel);
                list.Add(new HeaderFilterChoice(
                    PlayerPawnRosterUtility.OutpostStarFilterLabel(captured),
                    current == captured,
                    () => onPick?.Invoke(captured),
                    tip: tip,
                    separatorAfter: captured == OutpostPawnStarFilter.All,
                    countLabel: countLabel));
            }
            return list;
        }

        public static List<HeaderFilterChoice> PrisonerSourceChoices(
            PrisonerRosterSourceFilter current,
            Action<PrisonerRosterSourceFilter> onPick,
            IReadOnlyList<bool> isOutpostPrisoner = null)
        {
            bool show = isOutpostPrisoner != null;
            int total = 0, colony = 0, outpost = 0;
            if (show)
            {
                for (int i = 0; i < isOutpostPrisoner.Count; i++)
                {
                    total++;
                    if (isOutpostPrisoner[i]) outpost++;
                    else colony++;
                }
            }

            return new List<HeaderFilterChoice>
            {
                MakeCountedChoice(
                    PrisonerRosterUtility.SourceFilterLabel(PrisonerRosterSourceFilter.All),
                    current == PrisonerRosterSourceFilter.All,
                    () => onPick?.Invoke(PrisonerRosterSourceFilter.All),
                    total, total, show, separatorAfter: true),
                MakeCountedChoice(
                    PrisonerRosterUtility.SourceFilterLabel(PrisonerRosterSourceFilter.Colony),
                    current == PrisonerRosterSourceFilter.Colony,
                    () => onPick?.Invoke(PrisonerRosterSourceFilter.Colony),
                    colony, total, show),
                MakeCountedChoice(
                    PrisonerRosterUtility.SourceFilterLabel(PrisonerRosterSourceFilter.Outpost),
                    current == PrisonerRosterSourceFilter.Outpost,
                    () => onPick?.Invoke(PrisonerRosterSourceFilter.Outpost),
                    outpost, total, show)
            };
        }

        public static List<HeaderFilterChoice> LocationTypeChoices(
            string current,
            Action<string> onPick,
            IReadOnlyList<PlayerPawnLocationKind> kinds = null)
        {
            string cur = current ?? "";
            bool show = kinds != null;
            int total = 0, colony = 0, outpost = 0, caravan = 0, camp = 0, physical = 0;
            if (show)
            {
                for (int i = 0; i < kinds.Count; i++)
                {
                    total++;
                    switch (kinds[i])
                    {
                        case PlayerPawnLocationKind.Colony: colony++; break;
                        case PlayerPawnLocationKind.Outpost: outpost++; break;
                        case PlayerPawnLocationKind.WorldCaravan: caravan++; break;
                        case PlayerPawnLocationKind.Camp: camp++; break;
                        case PlayerPawnLocationKind.PhysicalMap: physical++; break;
                    }
                }
            }

            return new List<HeaderFilterChoice>
            {
                MakeCountedChoice(
                    "TSA_WD_AllPlayerPawns_Filter_AllTypes".Translate(),
                    cur.Length == 0,
                    () => onPick?.Invoke(LocationTypeAll),
                    total, total, show, separatorAfter: true),
                MakeCountedChoice(
                    "TSA_WD_AllPlayerPawns_LocColony".Translate(),
                    cur == LocationTypeColony,
                    () => onPick?.Invoke(LocationTypeColony),
                    colony, total, show),
                MakeCountedChoice(
                    "TSA_WD_AllPlayerPawns_LocOutpost".Translate(),
                    cur == LocationTypeOutpost,
                    () => onPick?.Invoke(LocationTypeOutpost),
                    outpost, total, show),
                MakeCountedChoice(
                    "TSA_WD_AllPlayerPawns_LocCamp".Translate(),
                    cur == LocationTypeCamp,
                    () => onPick?.Invoke(LocationTypeCamp),
                    camp, total, show),
                MakeCountedChoice(
                    "TSA_WD_AllPlayerPawns_LocCaravan".Translate(),
                    cur == LocationTypeCaravan,
                    () => onPick?.Invoke(LocationTypeCaravan),
                    caravan, total, show),
                MakeCountedChoice(
                    "TSA_WD_AllPlayerPawns_LocPhysicalMap".Translate(),
                    cur == LocationTypePhysicalMap,
                    () => onPick?.Invoke(LocationTypePhysicalMap),
                    physical, total, show)
            };
        }

        public static List<string> XenotypeKeysFrom(IReadOnlyList<PlayerPawnRosterEntry> rows)
        {
            var list = new List<string>(rows?.Count ?? 0);
            if (rows == null) return list;
            for (int i = 0; i < rows.Count; i++)
                list.Add(XenotypeKey(rows[i]?.pawn));
            return list;
        }

        public static List<PlayerPawnLocationKind> LocationKindsFrom(IReadOnlyList<PlayerPawnRosterEntry> rows)
        {
            var list = new List<PlayerPawnLocationKind>(rows?.Count ?? 0);
            if (rows == null) return list;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i] == null) continue;
                list.Add(rows[i].locationKind);
            }
            return list;
        }

        public static List<HeaderFilterChoice> XenotypeChoices(
            string current,
            Action<string> onPick,
            IReadOnlyList<string> xenotypeKeys = null)
        {
            string cur = current ?? "";
            bool show = xenotypeKeys != null;
            int total = 0;
            Dictionary<string, int> byDef = null;
            if (show)
            {
                byDef = new Dictionary<string, int>();
                for (int i = 0; i < xenotypeKeys.Count; i++)
                {
                    total++;
                    string key = xenotypeKeys[i] ?? "";
                    if (key.Length == 0) continue;
                    byDef.TryGetValue(key, out int n);
                    byDef[key] = n + 1;
                }
            }

            var list = new List<HeaderFilterChoice>
            {
                MakeCountedChoice(
                    "TSA_WD_Filter_AllXenotypes".Translate(),
                    cur.Length == 0,
                    () => onPick?.Invoke(""),
                    total, total, show, separatorAfter: true)
            };
            if (!ModsConfig.BiotechActive) return list;

            List<XenotypeDef> defs = DefDatabase<XenotypeDef>.AllDefsListForReading;
            var named = new List<XenotypeDef>();
            for (int i = 0; i < defs.Count; i++)
            {
                XenotypeDef def = defs[i];
                if (def == null || def.LabelCap.NullOrEmpty()) continue;
                named.Add(def);
            }
            named.Sort((a, b) =>
            {
                int na = 0;
                int nb = 0;
                if (byDef != null)
                {
                    byDef.TryGetValue(a.defName, out na);
                    byDef.TryGetValue(b.defName, out nb);
                }
                return CompareCountThenLabel(na, a.LabelCap, nb, b.LabelCap);
            });
            for (int i = 0; i < named.Count; i++)
            {
                XenotypeDef captured = named[i];
                string defName = captured.defName;
                int n = 0;
                if (byDef != null)
                    byDef.TryGetValue(defName, out n);
                list.Add(MakeCountedChoice(
                    captured.LabelCap,
                    cur == defName,
                    () => onPick?.Invoke(defName),
                    n, total, show));
            }
            return list;
        }

        public static string XenotypeKey(Pawn pawn) => pawn?.genes?.Xenotype?.defName ?? "";

        public static List<HeaderFilterChoice> OutpostTypeChoices(
            string current,
            Action<string> onPick,
            IReadOnlyList<string> typeDefNames = null)
        {
            string cur = current ?? "";
            bool show = typeDefNames != null;
            int total = 0;
            Dictionary<string, int> byDef = null;
            if (show)
            {
                byDef = new Dictionary<string, int>();
                for (int i = 0; i < typeDefNames.Count; i++)
                {
                    total++;
                    string key = typeDefNames[i] ?? "";
                    if (key.Length == 0) continue;
                    byDef.TryGetValue(key, out int n);
                    byDef[key] = n + 1;
                }
            }

            var list = new List<HeaderFilterChoice>
            {
                MakeCountedChoice(
                    "TSA_WD_AllPlayerPawns_Filter_AllTypes".Translate(),
                    cur.Length == 0,
                    () => onPick?.Invoke(""),
                    total, total, show, separatorAfter: true)
            };

            var named = new List<WorldObjectDef>();
            if (byDef != null)
            {
                foreach (var kv in byDef)
                {
                    WorldObjectDef def = DefDatabase<WorldObjectDef>.GetNamedSilentFail(kv.Key);
                    if (def == null || def.LabelCap.NullOrEmpty()) continue;
                    named.Add(def);
                }
            }
            named.Sort((a, b) =>
            {
                int na = 0;
                int nb = 0;
                if (byDef != null)
                {
                    byDef.TryGetValue(a.defName, out na);
                    byDef.TryGetValue(b.defName, out nb);
                }
                return CompareCountThenLabel(na, a.LabelCap, nb, b.LabelCap);
            });
            for (int i = 0; i < named.Count; i++)
            {
                WorldObjectDef captured = named[i];
                string defName = captured.defName;
                int n = 0;
                if (byDef != null)
                    byDef.TryGetValue(defName, out n);
                list.Add(MakeCountedChoice(
                    captured.LabelCap,
                    cur == defName,
                    () => onPick?.Invoke(defName),
                    n, total, show));
            }
            return list;
        }

        public static List<string> PsycastKeysOnPawn(Pawn pawn)
        {
            var keys = new List<string>();
            if (pawn?.abilities?.abilities == null) return keys;
            List<Ability> abs = pawn.abilities.abilities;
            for (int i = 0; i < abs.Count; i++)
            {
                Ability a = abs[i];
                if (a?.def == null || !a.def.IsPsycast || a.def.defName.NullOrEmpty()) continue;
                if (!keys.Contains(a.def.defName))
                    keys.Add(a.def.defName);
            }
            return keys;
        }

        public static List<List<string>> PsycastListsFrom(IReadOnlyList<PlayerPawnRosterEntry> rows)
        {
            var list = new List<List<string>>(rows?.Count ?? 0);
            if (rows == null) return list;
            for (int i = 0; i < rows.Count; i++)
                list.Add(PsycastKeysOnPawn(rows[i]?.pawn));
            return list;
        }

        public static List<HeaderFilterChoice> PsycastChoices(
            string current,
            Action<string> onPick,
            IReadOnlyList<List<string>> pawnPsycasts = null)
        {
            string cur = current ?? "";
            bool show = pawnPsycasts != null;
            int total = 0;
            int none = 0;
            var byDef = new Dictionary<string, int>();
            if (show)
            {
                for (int i = 0; i < pawnPsycasts.Count; i++)
                {
                    total++;
                    List<string> keys = pawnPsycasts[i];
                    if (keys == null || keys.Count == 0)
                    {
                        none++;
                        continue;
                    }
                    for (int k = 0; k < keys.Count; k++)
                    {
                        string key = keys[k];
                        if (key.NullOrEmpty()) continue;
                        byDef.TryGetValue(key, out int n);
                        byDef[key] = n + 1;
                    }
                }
            }

            var list = new List<HeaderFilterChoice>
            {
                MakeCountedChoice(
                    "TSA_WD_Filter_AllPsycasts".Translate(),
                    cur.Length == 0,
                    () => onPick?.Invoke(""),
                    total, total, show, separatorAfter: true),
                MakeCountedChoice(
                    "TSA_WD_Filter_NoPsycasts".Translate(),
                    cur == PsycastFilterNone,
                    () => onPick?.Invoke(PsycastFilterNone),
                    none, total, show, separatorAfter: true)
            };

            var named = new List<string>(byDef.Keys);
            named.Sort((a, b) => string.Compare(PsycastLabel(a), PsycastLabel(b), StringComparison.OrdinalIgnoreCase));
            for (int i = 0; i < named.Count; i++)
            {
                string defName = named[i];
                byDef.TryGetValue(defName, out int n);
                list.Add(MakeCountedChoice(
                    PsycastLabel(defName),
                    cur == defName,
                    () => onPick?.Invoke(defName),
                    n, total, show));
            }
            return list;
        }

        private static string PsycastLabel(string defName)
        {
            AbilityDef def = DefDatabase<AbilityDef>.GetNamedSilentFail(defName);
            if (def != null && !def.LabelCap.NullOrEmpty())
                return def.LabelCap;
            return defName ?? "";
        }

        private static float DrawDropdownTitle(Rect inner, string title)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, TitleH), title ?? "");
            Text.Anchor = TextAnchor.UpperLeft;
            float lineY = inner.y + TitleH + 2f;
            Color prev = GUI.color;
            GUI.color = Color.white;
            Widgets.DrawLineHorizontal(inner.x, lineY, inner.width);
            GUI.color = prev;
            return lineY + 8f;
        }

        private static Rect GuiRectToScreen(Rect gui)
        {
            Vector2 topLeft = UI.GUIToScreenPoint(new Vector2(gui.x, gui.y));
            Vector2 bottomRight = UI.GUIToScreenPoint(new Vector2(gui.xMax, gui.yMax));
            return Rect.MinMaxRect(topLeft.x, topLeft.y, bottomRight.x, bottomRight.y);
        }

        private static void Open(Rect guiAnchor, float width, float height, Action<Rect> draw)
        {
            Rect iconScreen = GuiRectToScreen(guiAnchor);
            float winW = width;
            float winH = height;
            // Right-align to the filter icon and drop just below the header cell.
            float x = iconScreen.xMax - winW;
            float y = iconScreen.yMax;
            if (x < 8f) x = 8f;
            if (x + winW > UI.screenWidth - 8f)
                x = UI.screenWidth - winW - 8f;
            if (y + winH > UI.screenHeight - 8f)
                y = iconScreen.y - winH;
            if (y < 8f) y = 8f;

            dropdownScreenRect = new Rect(x, y, winW, winH);
            dropdownDraw = win =>
            {
                Rect inner = win.ContractedBy(Pad);
                draw?.Invoke(inner);
            };
            dropdownOpen = true;
            focusTextNext = true;
            dropdownListScroll = Vector2.zero;
            openedOnFrame = Time.frameCount;
        }

        private static void DrawSearchField(
            Rect rect,
            string hint,
            string current,
            Action<string> onChanged,
            string controlName,
            bool requestFocus)
        {
            string old = current ?? "";
            if (requestFocus && focusTextNext)
            {
                GUI.FocusControl(controlName);
                if (Event.current.type == EventType.Repaint)
                    focusTextNext = false;
            }
            GUI.SetNextControlName(controlName);
            Text.Font = GameFont.Small;
            string next = Widgets.TextField(rect, old);
            if (string.IsNullOrEmpty(next) && !hint.NullOrEmpty())
            {
                GUI.color = new Color(1f, 1f, 1f, 0.4f);
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.Font = GameFont.Tiny;
                Widgets.Label(rect, "  " + hint);
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
            }
            if (next != old)
                onChanged?.Invoke(next);
        }

        private static int CompareCountThenLabel(int countA, string labelA, int countB, string labelB)
        {
            int cmp = countB.CompareTo(countA);
            if (cmp != 0) return cmp;
            return string.Compare(labelA, labelB, StringComparison.OrdinalIgnoreCase);
        }

        private static HeaderFilterChoice MakeCountedChoice(
            string label,
            bool selected,
            Action onPick,
            int count,
            int total,
            bool showCounts,
            bool separatorAfter = false)
        {
            string tip = null;
            AttachCount(count, total, showCounts, ref tip, out string countLabel);
            return new HeaderFilterChoice(label, selected, onPick, tip: tip, separatorAfter: separatorAfter, countLabel: countLabel);
        }

        private static void AttachCount(int count, int total, bool show, ref string tip, out string countLabel)
        {
            countLabel = null;
            if (!show) return;
            FormatFilterCount(count, total, out countLabel, out string countTip);
            if (tip.NullOrEmpty())
                tip = countTip;
            else if (!countTip.NullOrEmpty())
                tip = tip + "\n" + countTip;
        }

        private static void FormatFilterCount(int count, int total, out string countLabel, out string tip)
        {
            int pct = total <= 0 ? 0 : Mathf.RoundToInt(100f * count / total);
            countLabel = "TSA_WD_TraitFilter_CountPct".Translate(count.ToString(), pct.ToString());
            tip = "TSA_WD_TypeFilter_CountTip".Translate(count.ToString(), pct.ToString());
        }
    }

    public struct StarCountRow
    {
        public bool Starred;
        public bool OnColonyMap;

        public StarCountRow(bool starred, bool onColonyMap)
        {
            Starred = starred;
            OnColonyMap = onColonyMap;
        }
    }

    public struct HeaderFilterChoice
    {
        public string Label;
        public bool Selected;
        public string Tip;
        public string CountLabel;
        public Action OnPick;
        public bool SeparatorAfter;

        public HeaderFilterChoice(string label, bool selected, Action onPick, string tip = null, bool separatorAfter = false, string countLabel = null)
        {
            Label = label;
            Selected = selected;
            OnPick = onPick;
            Tip = tip;
            CountLabel = countLabel;
            SeparatorAfter = separatorAfter;
        }
    }
}
