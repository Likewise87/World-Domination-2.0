using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public class WorldDominationMod : Mod
    {
        public static WorldDominationSettings settings;
        private static Vector2 mainSettingsScrollPosition;
        private static WorldDominationMod instance;
        private static bool presetsExpanded = true;
        private static bool generalExpanded = true;
        private static bool outpostsExpanded = true;
        private static bool playerInteractionsExpanded = true;
        private static bool miscExpanded = true;

        /// <summary>UI-only: preset chosen in dropdown before Apply (Performance).</summary>
        private static WDSettingsPerformancePreset pendingPerformancePreset = WDSettingsPerformancePreset.Medium;
        /// <summary>UI-only: preset chosen in dropdown before Apply (Difficulty).</summary>
        private static WDSettingsDifficultyPreset pendingDifficultyPreset = WDSettingsDifficultyPreset.Medium;
        private static bool presetUiSeeded;

        public WorldDominationMod(ModContentPack content) : base(content)
        {
            instance = this;
            settings = GetSettings<WorldDominationSettings>();
        }

        /// <summary>Writes mod settings to disk (Config folder). Use after changing lastSeenReleaseNotesVersion so dismiss persists across saves/worlds.</summary>
        public static void SaveSettingsToDisk()
        {
            instance?.WriteSettings();
        }

        /// <summary>Opens this mod's settings hub (same UI as Options → Mod settings).</summary>
        public static void OpenModSettingsWindow()
        {
            Mod mod = instance ?? LoadedModManager.GetMod<WorldDominationMod>();
            if (mod == null) return;
            Find.WindowStack.Add(new Dialog_ModSettings(mod));
        }

        /// <summary>Unified allegiance editor needs a loaded game (faction list). Block from main menu mod settings.</summary>
        public static void TryOpenAllegianceLockWindow()
        {
            if (Current.ProgramState != ProgramState.Playing)
            {
                Messages.Message("TSA_WD_AllegianceMatrixInGameOnly".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }
            Find.WindowStack.Add(new Dialog_WdWorldGenAllegiances());
        }

        public override string SettingsCategory() => "TSA_WD_Category".Translate();

        /// <summary>After settings are saved (window closed), invalidate cached derived values so changes like attack range and escalation take effect immediately in-game.</summary>
        public override void WriteSettings()
        {
            base.WriteSettings();
            if (Current.ProgramState != ProgramState.Playing) return;
            Find.World?.GetComponent<WorldComponent_SpreadManager>()?.NotifyInfluenceSettingsChanged();
            WorldComponent_InterceptionScheduler.Current?.NotifyAtTurretRangeSettingsChanged();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            float contentWidth = inRect.width - 24f;
            float reserveTop = 0f;
            float reserveBottom = SettingsUI.ReserveBottomForCloseButton;
            Rect scrollOutRect = new Rect(inRect.x, inRect.y + reserveTop, inRect.width, inRect.height - reserveTop - reserveBottom);
            var s = WorldDominationMod.settings;
            // Fixed scroll content height: enough for all expanded hub sections (advanced + in-game).
            // Do not bind this to Listing.CurHeight on Layout; that collapses to the viewport and clips Misc.
            const float scrollContentH = 2500f;
            Rect scrollViewRect = new Rect(0f, 0f, contentWidth, scrollContentH);

            Widgets.BeginScrollView(scrollOutRect, ref mainSettingsScrollPosition, scrollViewRect);

            Listing_Standard l = new Listing_Standard();
            l.Begin(scrollViewRect);
            bool advanced = s.showAdvancedSettings;
            int rowIndex = 0;

            if (!presetUiSeeded)
            {
                pendingPerformancePreset = s.performancePreset;
                pendingDifficultyPreset = s.difficultyPreset;
                presetUiSeeded = true;
            }

            // --- Top toggles (single row): checkbox left, label right (avoids box-right ambiguity across cells) ---
            {
                const float topToggleRowH = 28f;
                const float topToggleGap = 18f;
                const float checkboxSize = 24f;
                const float checkboxLabelGap = 6f;
                Rect row = l.GetRect(topToggleRowH);
                float cellW = (row.width - topToggleGap * 2f) / 3f;

                void DrawTopToggle(Rect cell, string label, ref bool value, string tip)
                {
                    TooltipHandler.TipRegion(cell, tip);
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
                        },
                        destructive: true));
                },
                () =>
                {
                    presetsExpanded = generalExpanded = outpostsExpanded = playerInteractionsExpanded = miscExpanded = true;
                },
                () =>
                {
                    presetsExpanded = generalExpanded = outpostsExpanded = playerInteractionsExpanded = miscExpanded = false;
                },
                "TSA_WD_BtnUpdateNotes".Translate(),
                () => Find.WindowStack.Add(new Dialog_WD_UpdateLog()),
                "TSA_WD_DescUpdateNotes".Translate());

            // --- Setting presets (independent packs; Apply only) ---
            string perfApplied = ResolveAppliedPerformanceLabel(s);
            string diffApplied = ResolveAppliedDifficultyLabel(s);
            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_HeaderSettingPresets".Translate(), ref presetsExpanded, SettingsUI.SectionHeaderColor,
                "TSA_WD_SettingsPreset_HeaderTip".Translate(perfApplied, diffApplied)))
            {
                // Blue selected tint + white outline (same as embassy best-partner / academy selected rows).
                const float presetsBoxPadX = 10f;
                const float presetsBoxPadY = 6f;
                float presetsInnerW = Mathf.Max(1f, l.ColumnWidth - presetsBoxPadX * 2f);
                float perfRowH = SettingsUI.EstimateSettingPresetRowHeight(PerformancePresetDesc(pendingPerformancePreset), presetsInnerW);
                float diffRowH = SettingsUI.EstimateSettingPresetRowHeight(DifficultyPresetDesc(pendingDifficultyPreset), presetsInnerW);
                float presetsContentH = perfRowH + 6f + diffRowH;
                Rect presetsBox = l.GetRect(presetsContentH + presetsBoxPadY * 2f);
                Outpost_Dialog_UI.DrawSelectedRowTint(presetsBox, true);
                GUI.color = Color.white;
                Widgets.DrawBox(presetsBox, 1);

                Listing_Standard presetsListing = new Listing_Standard();
                presetsListing.Begin(presetsBox.ContractedBy(presetsBoxPadX, presetsBoxPadY));

                SettingsUI.DrawSettingPresetRow(
                    presetsListing,
                    "",
                    pendingPerformancePreset,
                    "",
                    PerformancePresetLabel,
                    PerformancePresetDesc,
                    preset => pendingPerformancePreset = preset,
                    () =>
                    {
                        s.ApplyPerformancePreset(pendingPerformancePreset);
                        Messages.Message("TSA_WD_SettingsPreset_Applied".Translate(PerformancePresetLabel(pendingPerformancePreset)), MessageTypeDefOf.TaskCompletion, false);
                    },
                    "TSA_WD_Important_PerformanceTip".Translate());
                presetsListing.Gap(6f);

                SettingsUI.DrawSettingPresetRow(
                    presetsListing,
                    "",
                    pendingDifficultyPreset,
                    "",
                    DifficultyPresetLabel,
                    DifficultyPresetDesc,
                    preset => pendingDifficultyPreset = preset,
                    () =>
                    {
                        s.ApplyDifficultyPreset(pendingDifficultyPreset);
                        Messages.Message("TSA_WD_SettingsPreset_Applied".Translate(DifficultyPresetLabel(pendingDifficultyPreset)), MessageTypeDefOf.TaskCompletion, false);
                    },
                    "TSA_WD_Important_DifficultyTip".Translate());
                presetsListing.End();
            }
            l.Gap(10f);

            // --- 1. General ---
            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_HeaderGeneral".Translate(), ref generalExpanded, SettingsUI.SectionHeaderColor))
            {
                SettingsUI.DrawMenuRow(l, rowIndex++, "TSA_WD_BtnNotifications".Translate(), "TSA_WD_DescNotifications".Translate(), () => Find.WindowStack.Add(new Dialog_NotificationSettings()));
                SettingsUI.DrawMenuRow(l, rowIndex++, "TSA_WD_BtnCombatDifficulty".Translate(), "TSA_WD_DescCombatDifficulty".Translate(), () => Find.WindowStack.Add(new Dialog_CombatDifficultySettings()));
                SettingsUI.DrawMenuRow(l, rowIndex++, "TSA_WD_BtnEconomicDifficulty".Translate(), "TSA_WD_DescEconomicDifficulty".Translate(), () => Find.WindowStack.Add(new Dialog_EconomicDifficultySettings()));
                SettingsUI.DrawMenuRow(l, rowIndex++, "TSA_WD_BtnDailyActions".Translate(), "TSA_WD_DescDailyActions".Translate(), () => Find.WindowStack.Add(new Dialog_DailyActionsSettings()));
                SettingsUI.DrawMenuRow(l, rowIndex++, "TSA_WD_BtnWorldRaids".Translate(), "TSA_WD_DescWorldRaids".Translate(), () => Find.WindowStack.Add(new Dialog_RaidSettings()));
                SettingsUI.DrawMenuRow(l, rowIndex++, "TSA_WD_BtnGrowthExpand".Translate(), "TSA_WD_DescGrowthExpand".Translate(), () => Find.WindowStack.Add(new Dialog_GrowthSettings()));
                SettingsUI.DrawMenuRow(l, rowIndex++, "TSA_WD_BtnDiplomacy".Translate(), "TSA_WD_DescDiplomacy".Translate(), () => Find.WindowStack.Add(new Dialog_DiplomacySettings()));
            }

            // --- 2. WD Outposts and Caravans/Travelers ---
            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_HeaderOutposts".Translate(), ref outpostsExpanded, SettingsUI.SectionHeaderColor))
            {
                SettingsUI.DrawMenuRow(l, rowIndex++, "TSA_WD_BtnOutpostSettings".Translate(), "TSA_WD_DescOutpostSettings".Translate(), () => Find.WindowStack.Add(new Dialog_FoodSettings()));
                SettingsUI.DrawMenuRow(l, rowIndex++, "TSA_WD_BtnPlayerArtillery".Translate(), "TSA_WD_DescPlayerArtillery".Translate(), () => Find.WindowStack.Add(new Dialog_PlayerArtillerySettings()));
                SettingsUI.DrawMenuRow(l, rowIndex++, "TSA_WD_BtnCaravans".Translate(), "TSA_WD_DescCaravans".Translate(), () => Find.WindowStack.Add(new Dialog_CaravansSettings()));
                if (advanced)
                {
                    SettingsUI.DrawMenuRow(l, rowIndex++, "TSA_WD_BtnRoadBuilding".Translate(), "TSA_WD_DescRoadBuilding".Translate(), () => Find.WindowStack.Add(new Dialog_RoadBuildingSettings()));
                    SettingsUI.DrawMenuRow(l, rowIndex++, "TSA_WD_BtnMiningBaselines".Translate(), "TSA_WD_DescMiningBaselines".Translate(), () => Find.WindowStack.Add(new Dialog_MiningBaselineSettings()));
                }
            }

            // --- 3. Manual Player Interactions ---
            if (advanced)
            {
                if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_HeaderPlayerInteractions".Translate(), ref playerInteractionsExpanded, SettingsUI.SectionHeaderColor))
                {
                    SettingsUI.DrawMenuRow(l, rowIndex++, "TSA_WD_BtnSabotage".Translate(), "TSA_WD_DescSabotage".Translate(), () => Find.WindowStack.Add(new Dialog_SabotageSettings()));
                    SettingsUI.DrawMenuRow(l, rowIndex++, "TSA_WD_BtnDisinformation".Translate(), "TSA_WD_DescDisinformation".Translate(), () => Find.WindowStack.Add(new Dialog_DisinformationSettings()));
                }
            }

            // --- 4. Misc ---
            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_HeaderMisc".Translate(), ref miscExpanded, SettingsUI.SectionHeaderColor))
            {
                if (advanced)
                    SettingsUI.DrawMenuRow(l, rowIndex++, "TSA_WD_BtnWorldGen".Translate(), "TSA_WD_DescWorldGen".Translate(), () => Find.WindowStack.Add(new Dialog_WorldGenSettings()));
                if (advanced)
                    SettingsUI.DrawMenuRow(l, rowIndex++, "TSA_WD_BtnBaseGeneration".Translate(), "TSA_WD_DescBaseGeneration".Translate(),
                        () => Find.WindowStack.Add(new Dialog_BaseGenerationSettings()));

                SettingsUI.DrawMenuRow(l, rowIndex++, "TSA_WD_BtnExperimental".Translate(), "TSA_WD_DescExperimental".Translate(),
                    () => Find.WindowStack.Add(new Dialog_ExperimentalSettings()));
            }

            l.End();
            Widgets.EndScrollView();
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
