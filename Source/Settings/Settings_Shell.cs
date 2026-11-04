using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>3-pane mod settings shell: slate nav | page | hover help + global search.</summary>
    public static class WdSettingsShell
    {
        private static readonly Color NavSelectedFill = new Color(0.20f, 0.35f, 0.48f, 0.96f);

        private static WdSettingsPage selectedPage = WdSettingsPage.Overview;
        private static Vector2 navScroll;
        private static Vector2 searchScroll;
        private static Vector2 overviewScroll;
        private static string searchQuery = "";
        private static string lastCollectedQuery;
        private static List<WdSettingsSearchHit> searchHits;
        private static WdSettingsHelpState helpState;
        private static bool registryDirty = true;
        private static WdSettingsPage? pendingJumpPage;
        private static string pendingHighlightLabel;

        // Overview UI state (moved from hub)
        private static bool presetUiSeeded;
        private static WDSettingsPerformancePreset pendingPerformancePreset = WDSettingsPerformancePreset.Medium;
        private static WDSettingsDifficultyPreset pendingDifficultyPreset = WDSettingsDifficultyPreset.Medium;

        /// <summary>Threat-red tint (dashboard raid header icons).</summary>
        private static readonly Color PresetDifficultyIconTint = new Color(1f, 0.35f, 0.35f);
        /// <summary>WD cyan for performance preset icons.</summary>
        private static readonly Color PresetPerformanceIconTint = Color.cyan;

        public static WdSettingsHelpState Help => helpState;

        public static void FocusPage(WdSettingsPage page)
        {
            selectedPage = page;
            searchQuery = "";
            searchHits = null;
        }

        public static void Draw(Rect inRect)
        {
            if (helpState == null)
                helpState = new WdSettingsHelpState();

            // FloatMenu draws after this shell; drop stale float-hover once the menu is gone.
            if (!Find.WindowStack.IsOpen<FloatMenu>())
                helpState.ClearFloat();

            SettingsUI.ActiveHelp = helpState;

            float navW = Mathf.Clamp(inRect.width * 0.22f, 150f, 192f);
            float helpW = Mathf.Clamp(inRect.width * 0.28f, 200f, 290f);
            float gap = 8f;
            float midW = Mathf.Max(180f, inRect.width - navW - helpW - gap * 2f);

            Rect navCol = new Rect(inRect.x, inRect.y, navW, inRect.height);
            Rect midCol = new Rect(navCol.xMax + gap, inRect.y, midW, inRect.height);
            Rect helpCol = new Rect(midCol.xMax + gap, inRect.y, helpW, inRect.height);

            DrawNavColumn(navCol);
            DrawMiddleColumn(midCol);
            helpState.Draw(helpCol);

            if (pendingJumpPage.HasValue)
            {
                selectedPage = pendingJumpPage.Value;
                if (!pendingHighlightLabel.NullOrEmpty())
                    SettingsUI.SetHighlight(pendingHighlightLabel);
                pendingJumpPage = null;
                pendingHighlightLabel = null;
                searchQuery = "";
                searchHits = null;
            }

            SettingsUI.ActiveHelp = null;
        }

        public static void OnSettingsWritten()
        {
            foreach (WdSettingsPageDef page in WdSettingsPages.All)
                page.ApplyOnClose?.Invoke();
            registryDirty = true;
        }

        private static void DrawNavColumn(Rect rect)
        {
            float searchH = 28f;
            Rect searchRect = new Rect(rect.x, rect.y, rect.width, searchH);
            string tip = "TSA_WD_Settings_SearchTipGlobal".Translate();
            if (!tip.NullOrEmpty())
                SettingsUI.TipRegion(searchRect, tip);

            string placeholder = "TSA_WD_Settings_SearchPlaceholder".Translate();
            GUI.SetNextControlName("WdSettingsGlobalSearch");
            string newQuery = Widgets.TextField(searchRect, searchQuery ?? "");
            if ((searchQuery ?? "").Length == 0 && !GUI.GetNameOfFocusedControl().Equals("WdSettingsGlobalSearch"))
            {
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = new Color(1f, 1f, 1f, 0.35f);
                Widgets.Label(searchRect.ContractedBy(6f, 0f), placeholder);
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
            }
            if (newQuery != searchQuery)
            {
                searchQuery = newQuery;
                RefreshSearchIfNeeded(force: true);
            }

            float y = searchRect.yMax + 6f;
            Rect listRect = new Rect(rect.x, y, rect.width, rect.yMax - y);
            bool advanced = WorldDominationMod.settings?.showAdvancedSettings ?? false;

            float rowH = 32f;
            float rowGap = 3f;
            float sectionH = 22f;
            float sectionGapTop = 8f;
            float contentH = 0f;
            string measureSection = null;
            foreach (WdSettingsPageDef page in WdSettingsPages.All)
            {
                if (page.Advanced && !advanced) continue;
                string sec = page.SectionKey ?? string.Empty;
                if (sec != measureSection)
                {
                    if (!sec.NullOrEmpty())
                        contentH += sectionGapTop + sectionH;
                    measureSection = sec;
                }
                contentH += rowH + rowGap;
            }

            Rect view = new Rect(0f, 0f, listRect.width - 16f, Mathf.Max(listRect.height, contentH + 4f));
            Widgets.BeginScrollView(listRect, ref navScroll, view);
            float rowY = 0f;
            string lastSection = null;
            foreach (WdSettingsPageDef page in WdSettingsPages.All)
            {
                if (page.Advanced && !advanced) continue;

                string section = page.SectionKey ?? string.Empty;
                if (section != lastSection)
                {
                    if (!section.NullOrEmpty())
                    {
                        rowY += sectionGapTop;
                        Rect sectionRect = new Rect(0f, rowY, view.width, sectionH);
                        DrawNavSectionHeader(sectionRect, section.Translate());
                        rowY += sectionH;
                    }
                    lastSection = section;
                }

                Rect row = new Rect(0f, rowY, view.width, rowH);
                bool selected = page.Id == selectedPage && searchQuery.NullOrEmpty();
                Texture2D icon = WdSettingsPages.LoadIcon(page.IconPath);
                string label = page.LabelKey.Translate();
                Color? fill = selected ? NavSelectedFill : (Color?)null;
                if (WorldDomination_UIUtils.ButtonTextWithIcon(
                        row, icon, label, iconSize: 20f, iconTint: page.IconTint, fill: fill, font: GameFont.Tiny))
                {
                    selectedPage = page.Id;
                    searchQuery = "";
                    searchHits = null;
                    SettingsUI.ClearHighlight();
                    if (!page.DescKey.NullOrEmpty())
                        helpState.Offer(label, page.DescKey.Translate());
                }
                else if (Mouse.IsOver(row) && !page.DescKey.NullOrEmpty())
                {
                    helpState.Offer(label, page.DescKey.Translate());
                }
                rowY += rowH + rowGap;
            }
            Widgets.EndScrollView();
        }

        private static void DrawNavSectionHeader(Rect rect, string label)
        {
            Text.Font = GameFont.Tiny;
            Color prev = GUI.color;
            GUI.color = SettingsUI.SectionHeaderColor;
            TextAnchor prevAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(rect.x + 2f, rect.y, rect.width - 4f, rect.height - 4f), label);
            Text.Anchor = prevAnchor;
            GUI.color = new Color(1f, 1f, 1f, 0.22f);
            Widgets.DrawLineHorizontal(rect.x, rect.yMax - 2f, rect.width);
            GUI.color = prev;
            Text.Font = GameFont.Small;
        }

        private static void DrawMiddleColumn(Rect rect)
        {
            // Flat HD-style column: no panel fill/outline — page bg already covers the window.
            // Top = 0 so content aligns with nav search / help column; keep side 12 and bottom 6.
            Rect inner = new Rect(rect.x + 12f, rect.y, Mathf.Max(0f, rect.width - 24f), Mathf.Max(0f, rect.height - 6f));

            if (!searchQuery.NullOrEmpty() && searchQuery.Trim().Length > 0)
            {
                RefreshSearchIfNeeded(force: false);
                WdSettingsSearch.DrawResults(inner, searchHits, ref searchScroll, hit =>
                {
                    pendingJumpPage = hit.Page;
                    pendingHighlightLabel = hit.Label;
                });
                return;
            }

            WdSettingsPageDef page = WdSettingsPages.Get(selectedPage);
            if (page.Id == WdSettingsPage.Overview)
            {
                DrawOverviewPage(inner);
                return;
            }

            page.Draw?.Invoke(inner);
        }

        private static void RefreshSearchIfNeeded(bool force)
        {
            string q = (searchQuery ?? "").Trim();
            if (!force && lastCollectedQuery == q && searchHits != null)
                return;

            if (registryDirty || WdSettingsSearch.Registry.Count == 0)
            {
                WdSettingsSearch.RebuildAll();
                registryDirty = false;
            }
            searchHits = WdSettingsSearch.Query(q);
            lastCollectedQuery = q;
        }

        /// <summary>Overview page: presets, medieval, top toggles, reset, update notes.</summary>
        public static void DrawOverviewPage(Rect inRect)
        {
            var s = WorldDominationMod.settings;
            if (s == null) return;

            if (!presetUiSeeded)
            {
                pendingPerformancePreset = s.performancePreset;
                pendingDifficultyPreset = s.difficultyPreset;
                presetUiSeeded = true;
            }

            const float scrollContentH = 520f;
            Rect view = new Rect(0f, 0f, inRect.width - 16f, scrollContentH);
            Widgets.BeginScrollView(inRect, ref overviewScroll, view);

            Listing_Standard l = new Listing_Standard();
            l.Begin(view);

            // Top toggles
            {
                const float topToggleRowH = 28f;
                const float topToggleGap = 12f;
                const float checkboxSize = 24f;
                const float checkboxLabelGap = 6f;
                Rect row = l.GetRect(topToggleRowH);
                float cellW = (row.width - topToggleGap * 2f) / 3f;

                void DrawTopToggle(Rect cell, string label, ref bool value, string tip)
                {
                    WdSettingsSearch.Register(label, tip);
                    if (Mouse.IsOver(cell))
                        SettingsUI.OfferHelp(label, tip);
                    SettingsUI.TipRegion(cell, tip);
                    float boxY = cell.y + (cell.height - checkboxSize) * 0.5f;
                    Widgets.Checkbox(new Vector2(cell.x, boxY), ref value);
                    TextAnchor prev = Text.Anchor;
                    Text.Anchor = TextAnchor.MiddleLeft;
                    Widgets.Label(new Rect(
                        cell.x + checkboxSize + checkboxLabelGap,
                        cell.y,
                        Mathf.Max(0f, cell.width - checkboxSize - checkboxLabelGap),
                        cell.height), label);
                    Text.Anchor = prev;
                }

                DrawTopToggle(
                    new Rect(row.x, row.y, cellW, row.height),
                    "TSA_WD_ShowAdvanced".Translate(),
                    ref s.showAdvancedSettings,
                    SettingsUI.TooltipWithDefault(
                        "TSA_WD_ShowAdvanced_Tooltip".Translate(), WorldDominationSettings.DefShowAdvancedSettings));
                DrawTopToggle(
                    new Rect(row.x + cellW + topToggleGap, row.y, cellW, row.height),
                    "TSA_WD_ShowUpdatePopups".Translate(),
                    ref s.showUpdatePopups,
                    SettingsUI.TooltipWithDefault(
                        "TSA_WD_ShowUpdatePopups_Tooltip".Translate(), WorldDominationSettings.DefShowUpdatePopups));
                DrawTopToggle(
                    new Rect(row.x + (cellW + topToggleGap) * 2f, row.y, cellW, row.height),
                    "TSA_WD_VerboseLogging".Translate(),
                    ref s.verboseLogging,
                    SettingsUI.TooltipWithDefault(
                        "TSA_WD_VerboseLogging_Tooltip".Translate(), WorldDominationSettings.DefVerboseLogging));
            }
            l.Gap(4f);

            SettingsUI.DrawMenuTopBar(l, "TSA_WD_BtnResetAll".Translate(),
                () =>
                {
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        "TSA_WD_ConfirmResetAll".Translate(),
                        () =>
                        {
                            WorldDominationMod.settings.InitializeDefaults();
                            pendingPerformancePreset = WorldDominationMod.settings.performancePreset;
                            pendingDifficultyPreset = WorldDominationMod.settings.difficultyPreset;
                            registryDirty = true;
                        },
                        destructive: true));
                },
                expandAll: null,
                collapseAll: null,
                "TSA_WD_BtnUpdateNotes".Translate(),
                () => Find.WindowStack.Add(new Dialog_WD_UpdateLog()),
                "TSA_WD_DescUpdateNotes".Translate());

            SettingsUI.DrawStaticSectionHeader(l, "TSA_WD_HeaderSettingPresets".Translate(), SettingsUI.SectionHeaderColor,
                "TSA_WD_SettingsPreset_HeaderTip".Translate(
                    ResolveAppliedPerformanceLabel(s), ResolveAppliedDifficultyLabel(s)));

            DrawOverviewPresetRow(
                l,
                "TSA_WD_Important_Performance".Translate(),
                ResolveAppliedPerformanceLabel(s),
                pendingPerformancePreset,
                PerformancePresetLabel,
                PerformancePresetDesc,
                PerformancePresetIcon,
                PresetPerformanceIconTint,
                "TSA_WD_Important_PerformanceTip".Translate(),
                preset => pendingPerformancePreset = preset,
                () =>
                {
                    s.ApplyPerformancePreset(pendingPerformancePreset);
                    Messages.Message(
                        "TSA_WD_SettingsPreset_Applied".Translate(PerformancePresetLabel(pendingPerformancePreset)),
                        MessageTypeDefOf.TaskCompletion, false);
                    registryDirty = true;
                });
            l.Gap(10f);
            DrawOverviewPresetRow(
                l,
                "TSA_WD_Important_Difficulty".Translate(),
                ResolveAppliedDifficultyLabel(s),
                pendingDifficultyPreset,
                DifficultyPresetLabel,
                DifficultyPresetDesc,
                DifficultyPresetIcon,
                PresetDifficultyIconTint,
                "TSA_WD_Important_DifficultyTip".Translate(),
                preset => pendingDifficultyPreset = preset,
                () =>
                {
                    s.ApplyDifficultyPreset(pendingDifficultyPreset);
                    Messages.Message(
                        "TSA_WD_SettingsPreset_Applied".Translate(DifficultyPresetLabel(pendingDifficultyPreset)),
                        MessageTypeDefOf.TaskCompletion, false);
                    registryDirty = true;
                });

            l.Gap(12f);
            bool prevMedieval = s.wdMedievalMode;
            SettingsUI.DrawCheckbox(
                l,
                "TSA_WD_MedievalMode".Translate(),
                ref s.wdMedievalMode,
                "TSA_WD_MedievalModeTip".Translate(),
                defaultValue: WorldDominationSettings.DefWdMedievalMode);
            if (prevMedieval != s.wdMedievalMode)
                OutpostPowerGrid.NotifyOutpostPowerDirty(null);

            l.Gap(12f);
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(1f, 1f, 1f, 0.5f);
            Widgets.Label(l.GetRect(40f), "TSA_WD_Settings_OverviewHint".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            l.End();
            Widgets.EndScrollView();
        }

        /// <summary>
        /// Overview preset block: title, currently-applied line, slate Select + Apply.
        /// Preset explanations go to the right help column on hover (not inline).
        /// </summary>
        private static void DrawOverviewPresetRow<T>(
            Listing_Standard l,
            string title,
            string currentlyAppliedLabel,
            T pendingSelected,
            Func<T, string> labelFor,
            Func<T, string> descFor,
            Func<T, Texture2D> iconFor,
            Color iconTint,
            string tip,
            Action<T> onSelect,
            Action onApply) where T : struct, Enum
        {
            Text.Font = GameFont.Small;
            Widgets.Label(l.GetRect(24f), $"<b>{title}</b>");

            Text.Font = GameFont.Tiny;
            GUI.color = new Color(1f, 1f, 1f, 0.65f);
            Widgets.Label(l.GetRect(18f), "TSA_WD_SettingsPreset_CurrentlyApplied".Translate(currentlyAppliedLabel));
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            l.Gap(4f);

            const float btnH = 30f;
            const float applyW = 100f;
            const float gap = 8f;
            // Leave ~200px air between the preset dropdown and Apply.
            const float selectExtraGap = 200f;
            Rect row = l.GetRect(btnH);
            Rect selectRect = new Rect(row.x, row.y, Mathf.Max(80f, row.width - applyW - gap - selectExtraGap), btnH);
            Rect applyRect = new Rect(row.xMax - applyW, row.y, applyW, btnH);

            Texture2D pendingIcon = iconFor != null ? iconFor(pendingSelected) : null;
            string pendingLabel = labelFor != null ? labelFor(pendingSelected) : pendingSelected.ToString();
            string desc = descFor != null ? descFor(pendingSelected) : null;
            // Right-column help: lead with the selected preset name, then its explanation.
            string selectHelpBody = desc.NullOrEmpty() ? pendingLabel : pendingLabel + "\n" + desc;
            if (!tip.NullOrEmpty())
                selectHelpBody = selectHelpBody + "\n\n" + tip;
            WdSettingsSearch.Register(title, selectHelpBody);

            string applyHelpTitle = "TSA_WD_SettingsPreset_Apply".Translate();
            string applyHelpBody = "TSA_WD_SettingsPreset_ApplyTip".Translate();

            if (Mouse.IsOver(applyRect))
                SettingsUI.OfferHelp(applyHelpTitle, applyHelpBody);
            else if (Mouse.IsOver(selectRect) || Mouse.IsOver(row))
                SettingsUI.OfferHelp(title, selectHelpBody);

            if (WorldDomination_UIUtils.ButtonTextWithIcon(
                    selectRect, pendingIcon, pendingLabel, iconSize: 20f, iconTint: iconTint, font: GameFont.Small))
            {
                var opts = new List<FloatMenuOption>();
                foreach (T value in Enum.GetValues(typeof(T)))
                {
                    T captured = value;
                    string optLabel = labelFor != null ? labelFor(captured) : captured.ToString();
                    Texture2D optIcon = iconFor != null ? iconFor(captured) : null;
                    string optDesc = descFor != null ? descFor(captured) : null;
                    string optHelpBody = optDesc.NullOrEmpty() ? optLabel : optLabel + "\n" + optDesc;
                    if (!tip.NullOrEmpty())
                        optHelpBody = optHelpBody + "\n\n" + tip;
                    string helpTitle = title;
                    string helpBody = optHelpBody;
                    opts.Add(new FloatMenuOption(optLabel, () => onSelect?.Invoke(captured), optIcon, iconTint)
                    {
                        mouseoverGuiAction = _ => helpState?.OfferFloat(helpTitle, helpBody)
                    });
                }
                Find.WindowStack.Add(new FloatMenu(opts));
            }

            if (WorldDomination_UIUtils.ButtonTextWithIcon(
                    applyRect, WdSettingsChromeAssets.ApplySave, "TSA_WD_SettingsPreset_Apply".Translate(),
                    iconSize: 18f, iconTint: PresetPerformanceIconTint, centerContents: true, font: GameFont.Small))
                onApply?.Invoke();
        }

        private static Texture2D PerformancePresetIcon(WDSettingsPerformancePreset p)
        {
            switch (p)
            {
                case WDSettingsPerformancePreset.Low:
                    return WdSettingsChromeAssets.PerfBest;
                case WDSettingsPerformancePreset.High:
                    return WdSettingsChromeAssets.PerfReduced;
                default:
                    return WdSettingsChromeAssets.PerfNormal;
            }
        }

        private static Texture2D DifficultyPresetIcon(WDSettingsDifficultyPreset d)
        {
            switch (d)
            {
                case WDSettingsDifficultyPreset.Easy:
                    return WdSettingsChromeAssets.DiffEasy;
                case WDSettingsDifficultyPreset.Hard:
                    return WdSettingsChromeAssets.DiffHard;
                default:
                    return WdSettingsChromeAssets.DiffMedium;
            }
        }

        private static string ResolveAppliedPerformanceLabel(WorldDominationSettings s)
        {
            foreach (WDSettingsPerformancePreset p in Enum.GetValues(typeof(WDSettingsPerformancePreset)))
            {
                if (s.MatchesPerformancePreset(p))
                    return PerformancePresetLabel(p);
            }
            return "TSA_WD_SettingsPreset_Custom".Translate();
        }

        private static string ResolveAppliedDifficultyLabel(WorldDominationSettings s)
        {
            foreach (WDSettingsDifficultyPreset d in Enum.GetValues(typeof(WDSettingsDifficultyPreset)))
            {
                if (s.MatchesDifficultyPreset(d))
                    return DifficultyPresetLabel(d);
            }
            return "TSA_WD_SettingsPreset_Custom".Translate();
        }

        private static string PerformancePresetLabel(WDSettingsPerformancePreset p)
        {
            switch (p)
            {
                case WDSettingsPerformancePreset.Low: return "TSA_WD_Important_Perf_Low".Translate();
                case WDSettingsPerformancePreset.High: return "TSA_WD_Important_Perf_High".Translate();
                default: return "TSA_WD_Important_Perf_Medium".Translate();
            }
        }

        private static string PerformancePresetDesc(WDSettingsPerformancePreset p)
        {
            switch (p)
            {
                case WDSettingsPerformancePreset.Low: return "TSA_WD_Important_Perf_LowDesc".Translate();
                case WDSettingsPerformancePreset.High: return "TSA_WD_Important_Perf_HighDesc".Translate();
                default: return "TSA_WD_Important_Perf_MediumDesc".Translate();
            }
        }

        private static string DifficultyPresetLabel(WDSettingsDifficultyPreset d)
        {
            switch (d)
            {
                case WDSettingsDifficultyPreset.Easy: return "TSA_WD_Important_Diff_Easy".Translate();
                case WDSettingsDifficultyPreset.Hard: return "TSA_WD_Important_Diff_Hard".Translate();
                default: return "TSA_WD_Important_Diff_Medium".Translate();
            }
        }

        private static string DifficultyPresetDesc(WDSettingsDifficultyPreset d)
        {
            switch (d)
            {
                case WDSettingsDifficultyPreset.Easy: return "TSA_WD_Important_Diff_EasyDesc".Translate();
                case WDSettingsDifficultyPreset.Hard: return "TSA_WD_Important_Diff_HardDesc".Translate();
                default: return "TSA_WD_Important_Diff_MediumDesc".Translate();
            }
        }
    }
}
