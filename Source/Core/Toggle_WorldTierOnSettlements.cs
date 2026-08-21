using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public enum WD_ProductivityOverlayMode
    {
        Off,
        Fertility,
        AnimalAbundance,
        FishAbundance,
        MiningRichness,
        MovementDifficulty,
        Pollution
    }

    public enum WD_OutpostWorldMapLabelMode
    {
        Off,
        Food,
        Strength,
        RaidCooldown,
        Name
    }

    /// <summary>
    /// Texture assets and Harmony registration for the world play-settings row must live on a type marked
    /// <see cref="StaticConstructorOnStartupAttribute"/> so RimWorld loads them on the main thread (not on a
    /// <see cref="WorldComponent"/> instance type).
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class WD_PlaySettingsWorldRowAssets
    {
        internal static readonly Texture2D WdMenuIcon;

        static WD_PlaySettingsWorldRowAssets()
        {
            WdMenuIcon = ContentFinder<Texture2D>.Get("UI/Tab/WD", false) ?? TexCommand.Replant;

            var harmony = new Harmony("TSA.WorldDomination.PlaySettingsPatch");
            harmony.Patch(
                AccessTools.Method(typeof(PlaySettings), nameof(PlaySettings.DoPlaySettingsGlobalControls)),
                postfix: new HarmonyMethod(typeof(WorldComponent_WDVisualizerToggle), nameof(WorldComponent_WDVisualizerToggle.Postfix_DoPlaySettingsGlobalControls))
            );
            harmony.Patch(
                AccessTools.Method(typeof(WorldTargeter), nameof(WorldTargeter.StopTargeting)),
                prefix: new HarmonyMethod(typeof(WorldComponent_WDVisualizerToggle), nameof(WorldComponent_WDVisualizerToggle.Prefix_WorldTargeter_StopEstablishmentPreview)));
        }
    }

    /// <summary>
    /// World-map visualizer toggles and play-settings menu. Marked
    /// <see cref="StaticConstructorOnStartupAttribute"/> because this type caches <see cref="Texture2D"/> icons
    /// (must load on the main thread). Prefer putting new shared icons on <see cref="WD_PlaySettingsWorldRowAssets"/> when practical.
    /// </summary>
    [StaticConstructorOnStartup]
    public class WorldComponent_WDVisualizerToggle : WorldComponent
    {
        public static bool ShowSettlementTierTexts = true;
        public static bool ShowEstablishmentBlockedOverlay = false;
        public static bool ShowRoadBlocksAndTraps = true;
        public static bool ShowFortifyBlacklistOverlay = false;
        // RELATION_UNDERLAY begin
        public static bool ShowRelationUnderlays = true;
        public static bool RelationUnderlaysBasedOnSelection = false;
        // RELATION_UNDERLAY end
        // PLAYER_UNDERLAY begin
        public static bool ShowPlayerUnderlays = false;
        // PLAYER_UNDERLAY end
        public static WD_OutpostWorldMapLabelMode OutpostWorldMapLabelMode = WD_OutpostWorldMapLabelMode.Food;
        public static WD_ProductivityOverlayMode ProductivityOverlayMode = WD_ProductivityOverlayMode.Off;
        private bool productivityOverlayLayerChecked;
        private bool movementDifficultyOverlayLayerChecked;
        private bool pollutionOverlayLayerChecked;
        private bool establishmentBlockedOverlayLayerChecked;
        private bool fortifyBlacklistOverlayLayerChecked;
        private bool coverageFillLayerChecked;
        private const int OverlayCenterMoveThresholdTiles = 4;

        private static PlanetTile lastProductivityOverlayMouseTile = PlanetTile.Invalid;
        private static PlanetTile lastMovementDifficultyOverlayMouseTile = PlanetTile.Invalid;
        private static PlanetTile lastPollutionOverlayMouseTile = PlanetTile.Invalid;
        private static PlanetTile lastEstablishmentBlockedOverlayMouseTile = PlanetTile.Invalid;
        private static bool outpostCoverageFillLayerRegistered;
        private static WD_WorldLayer_OutpostCoverageFill cachedOutpostCoverageFillLayer;
        private static IEnumerator coverageFillProgressiveRegen;

        public WorldComponent_WDVisualizerToggle(World world) : base(world)
        {
            outpostCoverageFillLayerRegistered = false;
            cachedOutpostCoverageFillLayer = null;
            CancelCoverageFillProgressiveRegen();
        }

        public override void WorldComponentUpdate()
        {
            base.WorldComponentUpdate();
            AdvanceCoverageFillProgressiveRegen();
        }

        public static void Postfix_DoPlaySettingsGlobalControls(WidgetRow row, bool worldView)
        {
            if (!worldView) return;

            if (row.ButtonIcon(WD_PlaySettingsWorldRowAssets.WdMenuIcon, GetWdWorldMapMenuTooltip()))
                OpenWdWorldMapMenu();
        }

        public static void OpenWdWorldMapMenu()
        {
            var options = new List<FloatMenuOption>();

            if (Action_Outpost_FortifyBlacklist.FeatureEnabled)
            {
                options.Add(new FloatMenuOption(
                    "TSA_WD_WorldMap_AddBlockedTiles".Translate(),
                    Action_Outpost_FortifyBlacklist.StartAddBlockedTiles)
                {
                    tooltip = "TSA_WD_WorldMap_AddBlockedTilesTip".Translate()
                });
                options.Add(new FloatMenuOption(
                    "TSA_WD_WorldMap_RemoveBlockedTiles".Translate(),
                    Action_Outpost_FortifyBlacklist.StartRemoveBlockedTiles)
                {
                    tooltip = "TSA_WD_WorldMap_RemoveBlockedTilesTip".Translate()
                });
            }

            if (WorldDominationMod.settings?.showOutpostRequirementsPreviewInWdMenu
                ?? WorldDominationSettings.DefShowOutpostRequirementsPreviewInWdMenu)
            {
                var simulationOption = new FloatMenuOption(
                    "TSA_WD_WorldMap_OutpostSimulation".Translate(),
                    OpenOutpostRequirementsPreview,
                    GetEstablishOutpostMenuIcon(),
                    Color.white);
                simulationOption.tooltip = "TSA_WD_WorldMap_OutpostSimulationTip".Translate();
                options.Add(simulationOption);
            }

            AddToggleMenuOption(options, "TSA_WD_WorldMap_ToggleTierTexts", ShowSettlementTierTexts,
                v => ShowSettlementTierTexts = v, tipKey: "TSA_WD_WorldMap_ToggleTierTextsTip", hotkey: "E");
            AddToggleMenuOption(options, "TSA_WD_WorldMap_ToggleRoadBlocksAndTraps", ShowRoadBlocksAndTraps,
                v => ShowRoadBlocksAndTraps = v, tipKey: "TSA_WD_WorldMap_ToggleRoadBlocksAndTrapsTip", hotkey: "R");
            // RELATION_UNDERLAY begin
            AddToggleMenuOption(options, "TSA_WD_WorldMap_ToggleRelationUnderlays", ShowRelationUnderlays,
                v => ShowRelationUnderlays = v, tipKey: "TSA_WD_WorldMap_ToggleRelationUnderlaysTip", hotkey: "Q");
            AddToggleMenuOption(options, "TSA_WD_WorldMap_ToggleRelationUnderlaysRelative", RelationUnderlaysBasedOnSelection,
                v => RelationUnderlaysBasedOnSelection = v, tipKey: "TSA_WD_WorldMap_ToggleRelationUnderlaysRelativeTip");
            // RELATION_UNDERLAY end
            // PLAYER_UNDERLAY begin
            AddToggleMenuOption(options, "TSA_WD_WorldMap_TogglePlayerUnderlays", ShowPlayerUnderlays,
                v => ShowPlayerUnderlays = v, tipKey: "TSA_WD_WorldMap_TogglePlayerUnderlaysTip", hotkey: "W");
            // PLAYER_UNDERLAY end

            AddMenuSectionHeader(options, "TSA_WD_WorldMap_SectionProductivityOverlays");
            options.Add(MakeCheckboxMenuOption(
                "TSA_WD_WorldMap_ToggleCaravanBlockedTiles".Translate(),
                ShowEstablishmentBlockedOverlay,
                () =>
                {
                    SetShowEstablishmentBlockedOverlay(!ShowEstablishmentBlockedOverlay);
                    OpenWdWorldMapMenu();
                },
                "TSA_WD_WorldMap_ToggleCaravanBlockedTilesTip",
                hotkey: "1"));
            if (Action_Outpost_FortifyBlacklist.FeatureEnabled)
            {
                options.Add(MakeCheckboxMenuOption(
                    "TSA_WD_WorldMap_ToggleFortifyBlacklist".Translate(),
                    ShowFortifyBlacklistOverlay,
                    () =>
                    {
                        SetShowFortifyBlacklistOverlay(!ShowFortifyBlacklistOverlay);
                        OpenWdWorldMapMenu();
                    },
                    "TSA_WD_WorldMap_ToggleFortifyBlacklistTip"));
            }
            AddProductivityOverlayToggle(options, WD_ProductivityOverlayMode.Fertility,
                "TSA_WD_WorldMap_OverlayFertility", "TSA_WD_WorldMap_OverlayFertilityTip", hotkey: "2");
            AddProductivityOverlayToggle(options, WD_ProductivityOverlayMode.AnimalAbundance,
                "TSA_WD_WorldMap_OverlayAnimalAbundance", "TSA_WD_WorldMap_OverlayAnimalAbundanceTip", hotkey: "3");
            AddProductivityOverlayToggle(options, WD_ProductivityOverlayMode.FishAbundance,
                "TSA_WD_WorldMap_OverlayFishAbundance", "TSA_WD_WorldMap_OverlayFishAbundanceTip", hotkey: "4");
            AddProductivityOverlayToggle(options, WD_ProductivityOverlayMode.MiningRichness,
                "TSA_WD_WorldMap_OverlayMiningEfficiency", "TSA_WD_WorldMap_OverlayMiningEfficiencyTip", hotkey: "5");
            AddProductivityOverlayToggle(options, WD_ProductivityOverlayMode.MovementDifficulty,
                "TSA_WD_WorldMap_OverlayMovementDifficulty", "TSA_WD_WorldMap_OverlayMovementDifficultyTip", hotkey: "6");
            if (ModsConfig.BiotechActive)
            {
                AddProductivityOverlayToggle(options, WD_ProductivityOverlayMode.Pollution,
                    "TSA_WD_WorldMap_OverlayPollution", "TSA_WD_WorldMap_OverlayPollutionTip", hotkey: "7");
            }

            AddMenuSectionHeader(options, "TSA_WD_WorldMap_SectionOutpostLabels");
            AddOutpostLabelModeOption(options, WD_OutpostWorldMapLabelMode.Name);
            AddOutpostLabelModeOption(options, WD_OutpostWorldMapLabelMode.Food, FoodLabelModeEnabled());
            AddOutpostLabelModeOption(options, WD_OutpostWorldMapLabelMode.Strength);
            AddOutpostLabelModeOption(options, WD_OutpostWorldMapLabelMode.RaidCooldown);

            options.Add(MakeIconMenuOption(
                "TSA_WD_WorldMap_ChangeNotes".Translate(),
                () => Find.WindowStack.Add(new Dialog_WD_UpdateLog()),
                GetChangeNotesMenuIcon(),
                "TSA_WD_WorldMap_ChangeNotesTip"));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static Texture2D cachedEstablishOutpostMenuIcon;
        private static Texture2D cachedChangeNotesMenuIcon;

        private static Texture2D GetEstablishOutpostMenuIcon()
        {
            return cachedEstablishOutpostMenuIcon ??= ContentFinder<Texture2D>.Get("UI/Commands/EstablishOutpost", false)
                ?? ContentFinder<Texture2D>.Get("UI/Commands/Settle", false)
                ?? TexCommand.Replant;
        }

        private static Texture2D GetChangeNotesMenuIcon()
        {
            // Same stats-report / list icon used on the world play-settings row before the float menu.
            return cachedChangeNotesMenuIcon ??= TexButton.OpenStatsReport
                ?? ContentFinder<Texture2D>.Get("UI/Buttons/OpenStatsReport", false)
                ?? TexButton.Info;
        }

        /// <summary>Shrink FloatMenu item icons (27→16) without changing option order.</summary>
        private static void SetMenuOptionTinyIcons(FloatMenuOption option)
        {
            if (option == null) return;
            Traverse.Create(option).Field("sizeMode").SetValue(FloatMenuSizeMode.Tiny);
        }

        private static FloatMenuOption MakeIconMenuOption(string label, Action action, Texture2D icon, string tipKey = null, bool tinyIcon = false, string hotkey = null)
        {
            var option = new FloatMenuOption(label, action, icon, Color.white);
            if (tinyIcon) SetMenuOptionTinyIcons(option);
            if (!string.IsNullOrEmpty(tipKey))
                option.tooltip = AppendHotkeyTip(tipKey.Translate().ToString(), hotkey);
            else if (!string.IsNullOrEmpty(hotkey))
                option.tooltip = AppendHotkeyTip(null, hotkey);
            return option;
        }

        private static FloatMenuOption MakeCheckboxMenuOption(string label, bool active, Action action, string tipKey = null, string hotkey = null)
        {
            var option = MakeIconMenuOption(
                label,
                action,
                active ? Widgets.CheckboxOnTex : Widgets.CheckboxOffTex,
                tipKey,
                tinyIcon: true,
                hotkey: hotkey);
            return option;
        }

        private static void AddMenuSectionHeader(List<FloatMenuOption> options, string labelKey)
        {
            // Must have a non-null action: FloatMenu sorts null-action (disabled) rows to the bottom.
            options.Add(new FloatMenuOption(labelKey.Translate(), () => OpenWdWorldMapMenu()));
        }

        private static void AddProductivityOverlayToggle(List<FloatMenuOption> options, WD_ProductivityOverlayMode mode, string labelKey, string tipKey, string hotkey = null)
        {
            bool active = ProductivityOverlayMode == mode;
            FloatMenuOption option = MakeCheckboxMenuOption(labelKey.Translate(), active, () =>
            {
                SetProductivityOverlayMode(active ? WD_ProductivityOverlayMode.Off : mode);
                OpenWdWorldMapMenu();
            }, tipKey, hotkey);
            if (IsScoreProductivityOverlayMode(mode) && !string.IsNullOrEmpty(tipKey))
            {
                string tip = tipKey.Translate().ToString()
                    + "\n\n"
                    + "TSA_WD_WorldMap_OverlayIgnoresOutpostUpgrades".Translate().ToString();
                option.tooltip = AppendHotkeyTip(tip, hotkey);
            }
            options.Add(option);
        }

        private static bool IsScoreProductivityOverlayMode(WD_ProductivityOverlayMode mode)
        {
            return mode == WD_ProductivityOverlayMode.Fertility
                || mode == WD_ProductivityOverlayMode.AnimalAbundance
                || mode == WD_ProductivityOverlayMode.FishAbundance
                || mode == WD_ProductivityOverlayMode.MiningRichness;
        }

        private static void AddToggleMenuOption(List<FloatMenuOption> options, string labelKey, bool currentValue,
            Action<bool> setter, bool enabled = true, string tipKey = null, string hotkey = null)
        {
            string label = labelKey.Translate().ToString();
            if (!enabled)
            {
                var disabled = new FloatMenuOption(label, null);
                if (!string.IsNullOrEmpty(tipKey) || !string.IsNullOrEmpty(hotkey))
                    disabled.tooltip = AppendHotkeyTip(
                        string.IsNullOrEmpty(tipKey) ? null : tipKey.Translate().ToString(),
                        hotkey);
                options.Add(disabled);
                return;
            }

            options.Add(MakeCheckboxMenuOption(label, currentValue, () =>
            {
                bool next = !currentValue;
                setter(next);
                NotifyWorldMapToggle(label, next);
                OpenWdWorldMapMenu();
            }, tipKey, hotkey));
        }

        /// <summary>Appends "Hotkey: Hold+X" using the Experimental world-map hold key.</summary>
        private static string AppendHotkeyTip(string tip, string hotkeyLetter)
        {
            if (string.IsNullOrEmpty(hotkeyLetter))
                return tip ?? string.Empty;
            string line = "TSA_WD_WorldMap_HotkeyLine".Translate(FormatWorldMapHoldHotkey(hotkeyLetter)).ToString();
            if (string.IsNullOrEmpty(tip))
                return line;
            return tip + "\n\n" + line;
        }

        private static string FormatWorldMapHoldHotkey(string letterOrDigit)
        {
            KeyCode hold = WorldDominationMod.settings?.worldMapOverlayHoldKey
                ?? WorldDominationSettings.DefWorldMapOverlayHoldKey;
            return FormatOverlayHoldKeyLabel(hold) + "+" + letterOrDigit;
        }

        private static string FormatOverlayHoldKeyLabel(KeyCode key)
        {
            switch (key)
            {
                case KeyCode.LeftAlt: return "Left Alt";
                case KeyCode.RightAlt: return "Right Alt";
                case KeyCode.LeftControl: return "Left Ctrl";
                case KeyCode.RightControl: return "Right Ctrl";
                case KeyCode.LeftShift: return "Left Shift";
                case KeyCode.RightShift: return "Right Shift";
                default: return key.ToString();
            }
        }

        public static void SetOutpostWorldMapLabelMode(WD_OutpostWorldMapLabelMode mode)
        {
            if (OutpostWorldMapLabelMode == mode) return;
            OutpostWorldMapLabelMode = mode;
            if (mode == WD_OutpostWorldMapLabelMode.Off)
            {
                NotifyWorldMapToggle("TSA_WD_WorldMap_OutpostLabels".Translate(), false);
                return;
            }
            NotifyWorldMapToggle(
                "TSA_WD_WorldMap_OutpostLabelsNotify".Translate(GetOutpostLabelModeLabel(mode)),
                true);
        }

        public static void SetShowEstablishmentBlockedOverlay(bool show)
        {
            if (ShowEstablishmentBlockedOverlay == show) return;
            ShowEstablishmentBlockedOverlay = show;
            EnsureEstablishmentBlockedOverlayLayerRegistered();
            lastEstablishmentBlockedOverlayMouseTile = PlanetTile.Invalid;
            if (show)
            {
                Outpost_EstablishmentRequirements.RebuildEstablishmentBlockedCacheForOverlay();
                WD_WorldLayer_EstablishmentBlockedOverlay.SetCenterTile(GenWorld.MouseTile());
            }
            else
                Outpost_EstablishmentRequirements.InvalidateEstablishmentBlockedCache();
            MarkEstablishmentBlockedOverlayDirty();
            NotifyWorldMapToggle("TSA_WD_WorldMap_ToggleCaravanBlockedTiles".Translate(), show);
        }

        public static void SetShowFortifyBlacklistOverlay(bool show)
        {
            if (ShowFortifyBlacklistOverlay == show) return;
            ShowFortifyBlacklistOverlay = show;
            EnsureFortifyBlacklistOverlayLayerRegistered();
            MarkFortifyBlacklistOverlayDirty();
            NotifyWorldMapToggle("TSA_WD_WorldMap_ToggleFortifyBlacklist".Translate(), show);
        }

        public static void EnsureFortifyBlacklistOverlayLayerRegisteredPublic() =>
            EnsureFortifyBlacklistOverlayLayerRegistered();

        public static void MarkFortifyBlacklistOverlayDirtyPublic() =>
            MarkFortifyBlacklistOverlayDirty();

        private static void NotifyWorldMapToggle(string featureLabel, bool enabled)
        {
            if (string.IsNullOrEmpty(featureLabel)) return;
            if (Current.ProgramState != ProgramState.Playing) return;
            string key = enabled ? "TSA_WD_WorldMap_ToggleActivated" : "TSA_WD_WorldMap_ToggleDeactivated";
            Messages.Message(key.Translate(featureLabel), MessageTypeDefOf.TaskCompletion, false);
        }

        private static string ProductivityOverlayNotifyLabel(WD_ProductivityOverlayMode mode)
        {
            string name;
            switch (mode)
            {
                case WD_ProductivityOverlayMode.Fertility:
                    name = "TSA_WD_WorldMap_OverlayFertility".Translate();
                    break;
                case WD_ProductivityOverlayMode.AnimalAbundance:
                    name = "TSA_WD_WorldMap_OverlayAnimalAbundance".Translate();
                    break;
                case WD_ProductivityOverlayMode.FishAbundance:
                    name = "TSA_WD_WorldMap_OverlayFishAbundance".Translate();
                    break;
                case WD_ProductivityOverlayMode.MiningRichness:
                    name = "TSA_WD_WorldMap_OverlayMiningEfficiency".Translate();
                    break;
                case WD_ProductivityOverlayMode.MovementDifficulty:
                    name = "TSA_WD_WorldMap_OverlayMovementDifficulty".Translate();
                    break;
                case WD_ProductivityOverlayMode.Pollution:
                    name = "TSA_WD_WorldMap_OverlayPollution".Translate();
                    break;
                default:
                    return null;
            }
            return "TSA_WD_WorldMap_ProductivityOverlayNotifyLabel".Translate(name);
        }

        private static void AddOutpostLabelModeOption(List<FloatMenuOption> options, WD_OutpostWorldMapLabelMode mode, bool enabled = true)
        {
            bool selected = OutpostWorldMapLabelMode == mode;
            string label = GetOutpostLabelModeLabel(mode);
            string tipKey = GetOutpostLabelModeTipKey(mode);
            // Hold+T cycles outpost label modes.
            const string hotkey = "T";
            if (!enabled)
            {
                var disabled = new FloatMenuOption(label, null);
                if (!string.IsNullOrEmpty(tipKey) || !string.IsNullOrEmpty(hotkey))
                    disabled.tooltip = AppendHotkeyTip(
                        string.IsNullOrEmpty(tipKey) ? null : tipKey.Translate().ToString(),
                        hotkey);
                options.Add(disabled);
                return;
            }

            options.Add(MakeCheckboxMenuOption(label, selected, () =>
            {
                SetOutpostWorldMapLabelMode(selected ? WD_OutpostWorldMapLabelMode.Off : mode);
                OpenWdWorldMapMenu();
            }, tipKey, hotkey));
        }

        private static string GetOutpostLabelModeTipKey(WD_OutpostWorldMapLabelMode mode)
        {
            switch (mode)
            {
                case WD_OutpostWorldMapLabelMode.Name:
                    return "TSA_WD_OutpostWorldMapLabel_NameTip";
                case WD_OutpostWorldMapLabelMode.Food:
                    return "TSA_WD_OutpostWorldMapLabel_FoodTip";
                case WD_OutpostWorldMapLabelMode.Strength:
                    return "TSA_WD_OutpostWorldMapLabel_StrengthTip";
                case WD_OutpostWorldMapLabelMode.RaidCooldown:
                    return "TSA_WD_OutpostWorldMapLabel_RaidCooldownTip";
                default:
                    return null;
            }
        }

        private static bool FoodLabelModeEnabled()
            => WorldDominationMod.settings == null || WorldDominationMod.settings.foodLogisticsActive;

        public static string GetOutpostLabelModeLabel(WD_OutpostWorldMapLabelMode mode)
        {
            switch (mode)
            {
                case WD_OutpostWorldMapLabelMode.Name:
                    return "TSA_WD_OutpostWorldMapLabel_Name".Translate().ToString();
                case WD_OutpostWorldMapLabelMode.Food:
                    return "TSA_WD_OutpostWorldMapLabel_Food".Translate().ToString();
                case WD_OutpostWorldMapLabelMode.Strength:
                    return "TSA_WD_OutpostWorldMapLabel_Strength".Translate().ToString();
                case WD_OutpostWorldMapLabelMode.RaidCooldown:
                    return "TSA_WD_OutpostWorldMapLabel_RaidCooldown".Translate().ToString();
                default:
                    return "TSA_WD_OutpostWorldMapLabel_Off".Translate().ToString();
            }
        }

        private static string GetWdWorldMapMenuTooltip()
            => "TSA_WD_WorldMap_MenuTooltip".Translate().ToString();

        private static void OpenOutpostRequirementsPreview()
        {
            Dialog_OutpostSelection.BeginRequirementsPreviewTileSelection();
        }

        public static void SetProductivityOverlayMode(WD_ProductivityOverlayMode mode)
        {
            if (ProductivityOverlayMode == mode) return;
            WD_ProductivityOverlayMode previous = ProductivityOverlayMode;
            ProductivityOverlayMode = mode;
            EnsureProductivityOverlayLayerRegistered();
            EnsureMovementDifficultyOverlayLayerRegistered();
            EnsurePollutionOverlayLayerRegistered();
            PlanetTile mouse = GenWorld.MouseTile();
            WD_WorldLayer_ProductivityOverlay.SetCenterTile(mouse);
            WD_WorldLayer_MovementDifficultyOverlay.SetCenterTile(mouse);
            WD_WorldLayer_PollutionOverlay.SetCenterTile(mouse);
            lastProductivityOverlayMouseTile = PlanetTile.Invalid;
            lastMovementDifficultyOverlayMouseTile = PlanetTile.Invalid;
            lastPollutionOverlayMouseTile = PlanetTile.Invalid;
            MarkProductivityOverlayDirty();
            MarkMovementDifficultyOverlayDirty();
            MarkPollutionOverlayDirty();
            // Blocked overlay swaps solid/pie vs grey hatch when productivity mode changes.
            if (ShowEstablishmentBlockedOverlay)
                MarkEstablishmentBlockedOverlayDirty();
            if (mode == WD_ProductivityOverlayMode.Off)
                NotifyWorldMapToggle(ProductivityOverlayNotifyLabel(previous), false);
            else
                NotifyWorldMapToggle(ProductivityOverlayNotifyLabel(mode), true);
        }

        public static bool IsProductivityScoreOverlayActive()
        {
            return ProductivityOverlayMode == WD_ProductivityOverlayMode.Fertility
                || ProductivityOverlayMode == WD_ProductivityOverlayMode.AnimalAbundance
                || ProductivityOverlayMode == WD_ProductivityOverlayMode.FishAbundance
                || ProductivityOverlayMode == WD_ProductivityOverlayMode.MiningRichness;
        }

        public static bool IsMovementDifficultyOverlayActive()
            => ProductivityOverlayMode == WD_ProductivityOverlayMode.MovementDifficulty;

        public static bool IsPollutionOverlayActive()
            => ProductivityOverlayMode == WD_ProductivityOverlayMode.Pollution;

        private static void EnsureProductivityOverlayLayerRegistered()
        {
            SurfaceLayer surface = Find.WorldGrid?.Surface;
            if (surface?.WorldDrawLayers == null) return;

            for (int i = 0; i < surface.WorldDrawLayers.Count; i++)
            {
                if (surface.WorldDrawLayers[i] is WD_WorldLayer_ProductivityOverlay)
                    return;
            }

            var layer = new WD_WorldLayer_ProductivityOverlay();
            Traverse.Create(layer).Field("planetLayer").SetValue(surface);
            surface.WorldDrawLayers.Add(layer);
        }

        private static void MarkProductivityOverlayDirty()
        {
            SurfaceLayer surface = Find.WorldGrid?.Surface;
            if (surface == null) return;
            Find.World?.renderer?.SetDirty<WD_WorldLayer_ProductivityOverlay>(surface);
        }

        private static void EnsureMovementDifficultyOverlayLayerRegistered()
        {
            SurfaceLayer surface = Find.WorldGrid?.Surface;
            if (surface?.WorldDrawLayers == null) return;

            for (int i = 0; i < surface.WorldDrawLayers.Count; i++)
            {
                if (surface.WorldDrawLayers[i] is WD_WorldLayer_MovementDifficultyOverlay)
                    return;
            }

            var layer = new WD_WorldLayer_MovementDifficultyOverlay();
            Traverse.Create(layer).Field("planetLayer").SetValue(surface);
            surface.WorldDrawLayers.Add(layer);
        }

        internal static void MarkMovementDifficultyOverlayDirty()
        {
            SurfaceLayer surface = Find.WorldGrid?.Surface;
            if (surface == null) return;
            Find.World?.renderer?.SetDirty<WD_WorldLayer_MovementDifficultyOverlay>(surface);
        }

        private static void EnsurePollutionOverlayLayerRegistered()
        {
            SurfaceLayer surface = Find.WorldGrid?.Surface;
            if (surface?.WorldDrawLayers == null) return;

            for (int i = 0; i < surface.WorldDrawLayers.Count; i++)
            {
                if (surface.WorldDrawLayers[i] is WD_WorldLayer_PollutionOverlay)
                    return;
            }

            var layer = new WD_WorldLayer_PollutionOverlay();
            Traverse.Create(layer).Field("planetLayer").SetValue(surface);
            surface.WorldDrawLayers.Add(layer);
        }

        internal static void MarkPollutionOverlayDirty()
        {
            SurfaceLayer surface = Find.WorldGrid?.Surface;
            if (surface == null) return;
            Find.World?.renderer?.SetDirty<WD_WorldLayer_PollutionOverlay>(surface);
        }

        private static void EnsureEstablishmentBlockedOverlayLayerRegistered()
        {
            SurfaceLayer surface = Find.WorldGrid?.Surface;
            if (surface?.WorldDrawLayers == null) return;

            for (int i = 0; i < surface.WorldDrawLayers.Count; i++)
            {
                if (surface.WorldDrawLayers[i] is WD_WorldLayer_EstablishmentBlockedOverlay)
                    return;
            }

            var layer = new WD_WorldLayer_EstablishmentBlockedOverlay();
            Traverse.Create(layer).Field("planetLayer").SetValue(surface);
            surface.WorldDrawLayers.Add(layer);
        }

        private static void EnsureFortifyBlacklistOverlayLayerRegistered()
        {
            SurfaceLayer surface = Find.WorldGrid?.Surface;
            if (surface?.WorldDrawLayers == null) return;

            for (int i = 0; i < surface.WorldDrawLayers.Count; i++)
            {
                if (surface.WorldDrawLayers[i] is WD_WorldLayer_FortifyBlacklist)
                    return;
            }

            var layer = new WD_WorldLayer_FortifyBlacklist();
            Traverse.Create(layer).Field("planetLayer").SetValue(surface);
            surface.WorldDrawLayers.Add(layer);
        }

        private static void MarkFortifyBlacklistOverlayDirty()
        {
            SurfaceLayer surface = Find.WorldGrid?.Surface;
            if (surface == null) return;
            Find.World?.renderer?.SetDirty<WD_WorldLayer_FortifyBlacklist>(surface);
        }

        private static void MarkEstablishmentBlockedOverlayDirty()
        {
            SurfaceLayer surface = Find.WorldGrid?.Surface;
            if (surface == null) return;
            Find.World?.renderer?.SetDirty<WD_WorldLayer_EstablishmentBlockedOverlay>(surface);
        }

        internal static void EnsureOutpostCoverageFillLayerRegisteredPublic() => EnsureOutpostCoverageFillLayerRegistered();

        internal static void MarkOutpostCoverageFillDirtyPublic() => MarkOutpostCoverageFillDirty();

        internal static bool IsOutpostCoverageFillLayerRegisteredPublic() => outpostCoverageFillLayerRegistered;

        private static void EnsureOutpostCoverageFillLayerRegistered()
        {
            if (outpostCoverageFillLayerRegistered && cachedOutpostCoverageFillLayer != null) return;

            SurfaceLayer surface = Find.WorldGrid?.Surface;
            if (surface?.WorldDrawLayers == null) return;

            for (int i = 0; i < surface.WorldDrawLayers.Count; i++)
            {
                if (surface.WorldDrawLayers[i] is WD_WorldLayer_OutpostCoverageFill existing)
                {
                    cachedOutpostCoverageFillLayer = existing;
                    outpostCoverageFillLayerRegistered = true;
                    return;
                }
            }

            var layer = new WD_WorldLayer_OutpostCoverageFill();
            Traverse.Create(layer).Field("planetLayer").SetValue(surface);
            surface.WorldDrawLayers.Add(layer);
            cachedOutpostCoverageFillLayer = layer;
            outpostCoverageFillLayerRegistered = true;
        }

        private static void MarkOutpostCoverageFillDirty()
        {
            EnsureOutpostCoverageFillLayerRegistered();
            WD_WorldLayer_OutpostCoverageFill layer = FindOutpostCoverageFillLayer();
            if (layer == null)
            {
                SurfaceLayer surface = Find.WorldGrid?.Surface;
                if (surface == null) return;
                Find.World?.renderer?.SetDirty<WD_WorldLayer_OutpostCoverageFill>(surface);
                return;
            }

            StartCoverageFillProgressiveRegen(layer);
        }

        private static WD_WorldLayer_OutpostCoverageFill FindOutpostCoverageFillLayer()
        {
            if (cachedOutpostCoverageFillLayer != null)
                return cachedOutpostCoverageFillLayer;

            SurfaceLayer surface = Find.WorldGrid?.Surface;
            if (surface?.WorldDrawLayers == null) return null;
            for (int i = 0; i < surface.WorldDrawLayers.Count; i++)
            {
                if (surface.WorldDrawLayers[i] is WD_WorldLayer_OutpostCoverageFill fill)
                {
                    cachedOutpostCoverageFillLayer = fill;
                    return fill;
                }
            }
            return null;
        }

        private static void StartCoverageFillProgressiveRegen(WD_WorldLayer_OutpostCoverageFill layer)
        {
            CancelCoverageFillProgressiveRegen();
            coverageFillProgressiveRegen = layer.Regenerate().GetEnumerator();
            // Kick first chunk this frame so small fills appear immediately.
            AdvanceCoverageFillProgressiveRegen();
        }

        private static void CancelCoverageFillProgressiveRegen()
        {
            if (coverageFillProgressiveRegen is IDisposable disposable)
                disposable.Dispose();
            coverageFillProgressiveRegen = null;
        }

        /// <summary>
        /// Advances coverage-fill Regenerate across frames. Vanilla SetDirty→RegenerateNow drains
        /// all yields in one frame; stopping on null checkpoints spreads large fill mesh builds.
        /// </summary>
        private static void AdvanceCoverageFillProgressiveRegen()
        {
            if (coverageFillProgressiveRegen == null) return;

            try
            {
                while (true)
                {
                    if (!coverageFillProgressiveRegen.MoveNext())
                    {
                        CancelCoverageFillProgressiveRegen();
                        return;
                    }

                    // Checkpoint from WD_WorldLayer_OutpostCoverageFill (every ~200 land tiles).
                    if (coverageFillProgressiveRegen.Current == null)
                        return;
                    // Keep draining base.Regenerate / other non-null steps in the same frame.
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[WorldDomination] Coverage fill progressive regen failed: {ex.Message}");
                CancelCoverageFillProgressiveRegen();
                SurfaceLayer surface = Find.WorldGrid?.Surface;
                if (surface != null)
                    Find.World?.renderer?.SetDirty<WD_WorldLayer_OutpostCoverageFill>(surface);
            }
        }

        private void EndEstablishmentPreviewOverlayIfTargetingEnded()
        {
            Dialog_OutpostSelection.EnsureRequirementsPreviewTargetingAfterDialogClosed();
        }

        public static void Prefix_WorldTargeter_StopEstablishmentPreview()
        {
            RemoteOutpostEstablishSession.NotifyWorldTargeterStopped();
            if (!Dialog_OutpostSelection.IsEstablishmentPreviewOverlayActive) return;
            if (Dialog_OutpostSelection.IsSuppressingEstablishmentPreviewEnd()) return;
            Dialog_OutpostSelection.SetEstablishmentPreviewOverlayActive(false);
        }

        internal static bool IsWorldTargeterActive()
        {
            WorldTargeter targeter = Find.WorldTargeter;
            if (targeter == null) return false;

            Traverse t = Traverse.Create(targeter);
            if (t.Field("targeting").FieldExists())
                return t.Field("targeting").GetValue<bool>();
            if (t.Property("IsTargeting").PropertyExists())
                return t.Property("IsTargeting").GetValue<bool>();
            return false;
        }

        public static string GetProductivityModeLabel(WD_ProductivityOverlayMode mode)
        {
            switch (mode)
            {
                case WD_ProductivityOverlayMode.Fertility:
                    return "TSA_WD_WorldMap_OverlayFertility".Translate().ToString();
                case WD_ProductivityOverlayMode.AnimalAbundance:
                    return "TSA_WD_WorldMap_OverlayAnimalAbundance".Translate().ToString();
                case WD_ProductivityOverlayMode.FishAbundance:
                    return "TSA_WD_WorldMap_OverlayFishAbundance".Translate().ToString();
                case WD_ProductivityOverlayMode.MiningRichness:
                    return "TSA_WD_WorldMap_OverlayMiningEfficiency".Translate().ToString();
                case WD_ProductivityOverlayMode.MovementDifficulty:
                    return "TSA_WD_WorldMap_OverlayMovementDifficulty".Translate().ToString();
                case WD_ProductivityOverlayMode.Pollution:
                    return "TSA_WD_WorldMap_OverlayPollution".Translate().ToString();
                default:
                    return "TSA_WD_ProductivityOverlay_Off".Translate().ToString();
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            bool legacyShowVisuals = true;
            Scribe_Values.Look(ref legacyShowVisuals, "ShowWDVisuals", true);
            Scribe_Values.Look(ref ShowSettlementTierTexts, "ShowWDTierTexts", legacyShowVisuals);
            bool legacyShowFoodTexts = legacyShowVisuals;
            Scribe_Values.Look(ref legacyShowFoodTexts, "ShowWDFoodTexts", legacyShowVisuals);
            Scribe_Values.Look(ref OutpostWorldMapLabelMode, "WD_OutpostWorldMapLabelMode",
                legacyShowFoodTexts ? WD_OutpostWorldMapLabelMode.Food : WD_OutpostWorldMapLabelMode.Off);
            Scribe_Values.Look(ref ProductivityOverlayMode, "WD_ProductivityOverlayMode", WD_ProductivityOverlayMode.Off);
            Scribe_Values.Look(ref ShowEstablishmentBlockedOverlay, "WD_ShowCaravanEstablishmentBlockedOverlay", false);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                bool legacyShowRoadBlocks = true;
                bool legacyShowSpikeTraps = true;
                Scribe_Values.Look(ref legacyShowRoadBlocks, "WD_ShowRoadBlocks", true);
                Scribe_Values.Look(ref legacyShowSpikeTraps, "WD_ShowSpikeTraps", true);
                Scribe_Values.Look(ref ShowRoadBlocksAndTraps, "WD_ShowRoadBlocksAndTraps",
                    legacyShowRoadBlocks || legacyShowSpikeTraps);
            }
            else
            {
                Scribe_Values.Look(ref ShowRoadBlocksAndTraps, "WD_ShowRoadBlocksAndTraps", true);
            }
            Scribe_Values.Look(ref ShowFortifyBlacklistOverlay, "WD_ShowFortifyBlacklistOverlay", false);
            // RELATION_UNDERLAY begin
            Scribe_Values.Look(ref ShowRelationUnderlays, "WD_ShowRelationUnderlays", true);
            Scribe_Values.Look(ref RelationUnderlaysBasedOnSelection, "WD_RelationUnderlaysBasedOnSelection", false);
            // RELATION_UNDERLAY end
            // PLAYER_UNDERLAY begin
            Scribe_Values.Look(ref ShowPlayerUnderlays, "WD_ShowPlayerUnderlays", false);
            // PLAYER_UNDERLAY end
        }

        public override void WorldComponentOnGUI()
        {
            ProcessWindowAndOverlayHotkeys();

            if (!productivityOverlayLayerChecked && Find.WorldGrid?.Surface != null)
            {
                productivityOverlayLayerChecked = true;
                EnsureProductivityOverlayLayerRegistered();
                MarkProductivityOverlayDirty();
            }

            if (!movementDifficultyOverlayLayerChecked && Find.WorldGrid?.Surface != null)
            {
                movementDifficultyOverlayLayerChecked = true;
                EnsureMovementDifficultyOverlayLayerRegistered();
                MarkMovementDifficultyOverlayDirty();
            }

            if (!pollutionOverlayLayerChecked && Find.WorldGrid?.Surface != null)
            {
                pollutionOverlayLayerChecked = true;
                EnsurePollutionOverlayLayerRegistered();
                MarkPollutionOverlayDirty();
            }

            if (!establishmentBlockedOverlayLayerChecked && Find.WorldGrid?.Surface != null)
            {
                establishmentBlockedOverlayLayerChecked = true;
                EnsureEstablishmentBlockedOverlayLayerRegistered();
                MarkEstablishmentBlockedOverlayDirty();
            }

            if (!fortifyBlacklistOverlayLayerChecked && Find.WorldGrid?.Surface != null)
            {
                fortifyBlacklistOverlayLayerChecked = true;
                EnsureFortifyBlacklistOverlayLayerRegistered();
                MarkFortifyBlacklistOverlayDirty();
            }

            if (!coverageFillLayerChecked && Find.WorldGrid?.Surface != null)
            {
                coverageFillLayerChecked = true;
                EnsureOutpostCoverageFillLayerRegistered();
                // Empty until RadiusFillHoverController.Begin — skip dirty regen.
            }

            EndEstablishmentPreviewOverlayIfTargetingEnded();

            RefreshProductivityOverlayForMouseTile();
            RefreshMovementDifficultyOverlayForMouseTile();
            RefreshPollutionOverlayForMouseTile();
            RefreshEstablishmentBlockedOverlayForMouseTile();
            RadiusFillHoverController.EndFrame();
            DrawOverlayHoverLabel();
        }

        /// <summary>
        /// OnGUI can run multiple times per frame; GetKeyDown stays true for the whole frame.
        /// </summary>
        private static int lastOverlayHotkeyProcessedFrame = -1;

        /// <summary>
        /// Hold configured key (default Left Alt): on the world map, 1–7 / Q–T toggle overlays;
        /// anywhere in play, A/X/S/D/Y/F/G/C open WD windows / world map (pawns, main tab, world stats, diplomacy, prisoners, outposts, travelers, world map).
        /// Uses Unity Input (not Event.current) so a leftover TextField focus — common after
        /// search boxes / rename fields — cannot permanently kill the chord mid-game.
        /// Safe to call from WorldComponentOnGUI and from hub DoWindowContents (frame-debounced).
        /// </summary>
        public static void ProcessWindowAndOverlayHotkeys()
        {
            HandleWorldMapOverlayHotkeys();
        }

        private static void HandleWorldMapOverlayHotkeys()
        {
            bool playing = Current.ProgramState == ProgramState.Playing;
            bool entryWorld = Current.ProgramState == ProgramState.Entry && Find.World != null;
            if (!playing && !entryWorld) return;

            KeyCode holdKey = WorldDominationMod.settings?.worldMapOverlayHoldKey
                ?? WorldDominationSettings.DefWorldMapOverlayHoldKey;
            if (holdKey == KeyCode.None || !IsOverlayHoldKeyPressed(holdKey)) return;

            int frame = Time.frameCount;
            if (frame == lastOverlayHotkeyProcessedFrame) return;

            // Window chords must work even while a force-paused hub (Diplomacy / World Stats) is open.
            int windowSlot = OverlayWindowHotkeyFromInput();
            int digitSlot = -1;
            int letterSlot = -1;
            bool forcePaused = Find.WindowStack != null && Find.WindowStack.WindowsForcePause;
            bool worldVisible = WorldRendererUtility.WorldRendered || entryWorld;
            if (worldVisible && (!forcePaused || entryWorld))
            {
                digitSlot = OverlayHotkeySlotFromInput();
                letterSlot = OverlayToggleHotkeyLetterFromInput();
            }
            if (windowSlot < 0 && digitSlot < 0 && letterSlot < 0) return;

            lastOverlayHotkeyProcessedFrame = frame;

            // Stale IMGUI focus (closed window / inspect search) otherwise eats digit keys forever.
            if (GUIUtility.keyboardControl != 0)
            {
                GUIUtility.keyboardControl = 0;
                GUI.FocusControl(null);
            }

            Event e = Event.current;
            if (e != null && e.type == EventType.KeyDown)
                e.Use();

            if (windowSlot >= 0)
                ApplyOverlayWindowHotkey(windowSlot);
            else if (digitSlot >= 0)
                ApplyWorldMapOverlayHotkeySlot(digitSlot);
            else
                ApplyWorldMapToggleHotkeyLetter(letterSlot);
        }

        /// <summary>
        /// Left/Right Alt (or Ctrl/Shift) accept either side of the pair so "Alt" feels like a single modifier.
        /// </summary>
        private static bool IsOverlayHoldKeyPressed(KeyCode holdKey)
        {
            if (holdKey == KeyCode.LeftAlt || holdKey == KeyCode.RightAlt)
                return Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            if (holdKey == KeyCode.LeftControl || holdKey == KeyCode.RightControl)
                return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (holdKey == KeyCode.LeftShift || holdKey == KeyCode.RightShift)
                return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            return Input.GetKey(holdKey);
        }

        private static int OverlayHotkeySlotFromInput()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) return 1;
            if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) return 2;
            if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) return 3;
            if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4)) return 4;
            if (Input.GetKeyDown(KeyCode.Alpha5) || Input.GetKeyDown(KeyCode.Keypad5)) return 5;
            if (Input.GetKeyDown(KeyCode.Alpha6) || Input.GetKeyDown(KeyCode.Keypad6)) return 6;
            if (Input.GetKeyDown(KeyCode.Alpha7) || Input.GetKeyDown(KeyCode.Keypad7)) return 7;
            return -1;
        }

        /// <summary>A / X / S / D / Y / F / G / C → window / world-map shortcuts.</summary>
        private static int OverlayWindowHotkeyFromInput()
        {
            if (Input.GetKeyDown(KeyCode.A)) return 0;
            if (Input.GetKeyDown(KeyCode.X)) return 1;
            if (Input.GetKeyDown(KeyCode.S)) return 2;
            if (Input.GetKeyDown(KeyCode.D)) return 3;
            if (Input.GetKeyDown(KeyCode.Y)) return 4;
            if (Input.GetKeyDown(KeyCode.F)) return 5;
            if (Input.GetKeyDown(KeyCode.G)) return 6;
            if (Input.GetKeyDown(KeyCode.C)) return 7;
            return -1;
        }

        private static void ApplyOverlayWindowHotkey(int slot)
        {
            switch (slot)
            {
                case 0: // A — All Player Pawns
                    WdNavWindows.ToggleExclusive(() => new Window_AllPlayerPawns());
                    return;
                case 1: // X — WD main tab
                    WdNavWindows.ToggleMainTabExclusive();
                    return;
                case 2: // S — World Stats
                    WdNavWindows.ToggleExclusive(() => new Window_WorldStats());
                    return;
                case 3: // D — Diplomacy
                    WdNavWindows.ToggleExclusive(() => new Window_DiplomacyMatrix());
                    return;
                case 4: // Y — Prisoners
                    WdNavWindows.ToggleExclusive(() => new Window_Prisoners());
                    return;
                case 5: // F — Outpost Overview
                    WdNavWindows.ToggleExclusive(() => new Window_OutpostOverview());
                    return;
                case 6: // G — Active Travelers
                    WdNavWindows.ToggleExclusive(() => new Window_ActiveTravelers());
                    return;
                case 7: // C — World map
                    WdNavWindows.ToggleWorldMap();
                    return;
            }
        }

        /// <summary>Q/W/E/R/T → 0..4 for non-productivity WD float-menu toggles.</summary>
        private static int OverlayToggleHotkeyLetterFromInput()
        {
            if (Input.GetKeyDown(KeyCode.Q)) return 0;
            if (Input.GetKeyDown(KeyCode.W)) return 1;
            if (Input.GetKeyDown(KeyCode.E)) return 2;
            if (Input.GetKeyDown(KeyCode.R)) return 3;
            if (Input.GetKeyDown(KeyCode.T)) return 4;
            return -1;
        }

        private static void ApplyWorldMapOverlayHotkeySlot(int slot)
        {
            switch (slot)
            {
                case 1:
                    SetShowEstablishmentBlockedOverlay(!ShowEstablishmentBlockedOverlay);
                    return;
                case 2:
                    ToggleProductivityOverlayMode(WD_ProductivityOverlayMode.Fertility);
                    return;
                case 3:
                    ToggleProductivityOverlayMode(WD_ProductivityOverlayMode.AnimalAbundance);
                    return;
                case 4:
                    ToggleProductivityOverlayMode(WD_ProductivityOverlayMode.FishAbundance);
                    return;
                case 5:
                    ToggleProductivityOverlayMode(WD_ProductivityOverlayMode.MiningRichness);
                    return;
                case 6:
                    ToggleProductivityOverlayMode(WD_ProductivityOverlayMode.MovementDifficulty);
                    return;
                case 7:
                    if (!ModsConfig.BiotechActive) return;
                    ToggleProductivityOverlayMode(WD_ProductivityOverlayMode.Pollution);
                    return;
            }
        }

        private static void ApplyWorldMapToggleHotkeyLetter(int slot)
        {
            switch (slot)
            {
                case 0: // Q - Highlight Relationships
                    ShowRelationUnderlays = !ShowRelationUnderlays;
                    NotifyWorldMapToggle("TSA_WD_WorldMap_ToggleRelationUnderlays".Translate(), ShowRelationUnderlays);
                    return;
                case 1: // W - Highlight Player
                    ShowPlayerUnderlays = !ShowPlayerUnderlays;
                    NotifyWorldMapToggle("TSA_WD_WorldMap_TogglePlayerUnderlays".Translate(), ShowPlayerUnderlays);
                    return;
                case 2: // E - Settlement tier labels
                    ShowSettlementTierTexts = !ShowSettlementTierTexts;
                    NotifyWorldMapToggle("TSA_WD_WorldMap_ToggleTierTexts".Translate(), ShowSettlementTierTexts);
                    return;
                case 3: // R - Road blocks and traps
                    ShowRoadBlocksAndTraps = !ShowRoadBlocksAndTraps;
                    NotifyWorldMapToggle("TSA_WD_WorldMap_ToggleRoadBlocksAndTraps".Translate(), ShowRoadBlocksAndTraps);
                    return;
                case 4: // T - cycle outpost world-map label mode
                    CycleOutpostWorldMapLabelMode();
                    return;
            }
        }

        private static void CycleOutpostWorldMapLabelMode()
        {
            WD_OutpostWorldMapLabelMode[] order =
            {
                WD_OutpostWorldMapLabelMode.Off,
                WD_OutpostWorldMapLabelMode.Name,
                WD_OutpostWorldMapLabelMode.Food,
                WD_OutpostWorldMapLabelMode.Strength,
                WD_OutpostWorldMapLabelMode.RaidCooldown,
            };
            int idx = 0;
            for (int i = 0; i < order.Length; i++)
            {
                if (order[i] == OutpostWorldMapLabelMode)
                {
                    idx = i;
                    break;
                }
            }
            for (int step = 1; step <= order.Length; step++)
            {
                WD_OutpostWorldMapLabelMode next = order[(idx + step) % order.Length];
                if (next == WD_OutpostWorldMapLabelMode.Food && !FoodLabelModeEnabled())
                    continue;
                SetOutpostWorldMapLabelMode(next);
                return;
            }
        }

        private static void ToggleProductivityOverlayMode(WD_ProductivityOverlayMode mode)
        {
            SetProductivityOverlayMode(ProductivityOverlayMode == mode
                ? WD_ProductivityOverlayMode.Off
                : mode);
        }

        private void RefreshProductivityOverlayForMouseTile()
        {
            if (!IsProductivityScoreOverlayActive()) return;
            if (!WorldRendererUtility.WorldRendered) return;
            if (Event.current == null || Event.current.type != EventType.Repaint) return;

            PlanetTile mouseTile = GenWorld.MouseTile();
            if (!ShouldMoveOverlayCenter(lastProductivityOverlayMouseTile, mouseTile)) return;

            lastProductivityOverlayMouseTile = mouseTile;
            if (WD_WorldLayer_ProductivityOverlay.SetCenterTile(mouseTile))
                MarkProductivityOverlayDirty();
        }

        private void RefreshMovementDifficultyOverlayForMouseTile()
        {
            if (!IsMovementDifficultyOverlayActive()) return;
            if (!WorldRendererUtility.WorldRendered) return;
            if (Event.current == null || Event.current.type != EventType.Repaint) return;

            PlanetTile mouseTile = GenWorld.MouseTile();
            if (!ShouldMoveOverlayCenter(lastMovementDifficultyOverlayMouseTile, mouseTile)) return;

            lastMovementDifficultyOverlayMouseTile = mouseTile;
            if (WD_WorldLayer_MovementDifficultyOverlay.SetCenterTile(mouseTile))
                MarkMovementDifficultyOverlayDirty();
        }

        private void RefreshPollutionOverlayForMouseTile()
        {
            if (!IsPollutionOverlayActive()) return;
            if (!WorldRendererUtility.WorldRendered) return;
            if (Event.current == null || Event.current.type != EventType.Repaint) return;

            PlanetTile mouseTile = GenWorld.MouseTile();
            if (!ShouldMoveOverlayCenter(lastPollutionOverlayMouseTile, mouseTile)) return;

            lastPollutionOverlayMouseTile = mouseTile;
            if (WD_WorldLayer_PollutionOverlay.SetCenterTile(mouseTile))
                MarkPollutionOverlayDirty();
        }

        private void RefreshEstablishmentBlockedOverlayForMouseTile()
        {
            if (!ShowEstablishmentBlockedOverlay) return;
            if (!WorldRendererUtility.WorldRendered) return;
            if (Event.current == null || Event.current.type != EventType.Repaint) return;

            PlanetTile mouseTile = GenWorld.MouseTile();
            if (!ShouldMoveOverlayCenter(lastEstablishmentBlockedOverlayMouseTile, mouseTile)) return;

            lastEstablishmentBlockedOverlayMouseTile = mouseTile;
            // Cache is rebuilt when the overlay is toggled on — not on every mouse move.
            if (WD_WorldLayer_EstablishmentBlockedOverlay.SetCenterTile(mouseTile))
                MarkEstablishmentBlockedOverlayDirty();
        }

        private static bool ShouldMoveOverlayCenter(PlanetTile lastCenter, PlanetTile newCenter)
        {
            if (!newCenter.Valid) return false;
            if (!lastCenter.Valid) return true;
            if (lastCenter == newCenter) return false;
            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return true;
            return grid.ApproxDistanceInTiles(lastCenter.tileId, newCenter.tileId) >= OverlayCenterMoveThresholdTiles;
        }

        private void DrawOverlayHoverLabel()
        {
            bool showMovement = IsMovementDifficultyOverlayActive();
            bool showPollution = IsPollutionOverlayActive();
            bool showForProductivity = IsProductivityScoreOverlayActive();
            bool showForSimulation = Dialog_OutpostSelection.IsEstablishmentPreviewOverlayActive;
            if (!showMovement && !showPollution && !showForProductivity && !showForSimulation) return;
            if (!WorldRendererUtility.WorldRendered) return;
            if (Event.current == null || Event.current.type != EventType.Repaint) return;

            PlanetTile mouseTile = GenWorld.MouseTile();
            if (!mouseTile.Valid || Find.WorldGrid == null) return;

            Tile tileInfo = Find.WorldGrid[mouseTile];
            if (tileInfo == null || tileInfo.WaterCovered) return;

            string label;
            float boxH;
            if (showMovement)
            {
                label = BuildMovementDifficultyHoverLabel(mouseTile);
                boxH = 36f;
            }
            else if (showPollution)
            {
                label = BuildPollutionHoverLabel(mouseTile);
                boxH = 36f;
            }
            else
            {
                label = BuildProductivityHoverMultiline(mouseTile);
                boxH = 72f;
            }

            Vector2 screenPos = GenWorldUI.WorldToUIPosition(Find.WorldGrid.GetTileCenter(mouseTile));
            const float boxW = 210f;
            Rect rect = new Rect(screenPos.x - boxW * 0.5f, screenPos.y - 70f, boxW, boxH);

            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;
            Color oldColor = GUI.color;

            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.65f));
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.MiddleLeft;
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(rect.x + 8f, rect.y + 4f, rect.width - 16f, rect.height - 8f), label);

            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
            GUI.color = oldColor;
        }

        private static string BuildMovementDifficultyHoverLabel(PlanetTile mouseTile)
        {
            if (!WD_WorldLayer_MovementDifficultyOverlay.TryGetDisplayDifficulty(mouseTile, out float difficulty))
                return "TSA_WD_WorldMap_OverlayMovementDifficulty_Impassable".Translate().ToString();
            return "TSA_WD_WorldMap_OverlayMovementDifficulty_Hover".Translate(difficulty.ToString("F2")).ToString();
        }

        private static string BuildPollutionHoverLabel(PlanetTile mouseTile)
        {
            int pp = WD_WorldLayer_PollutionOverlay.GetDisplayPollutionPercent(mouseTile);
            return "TSA_WD_WorldMap_OverlayPollution_Hover".Translate(pp).ToString();
        }

        private static string BuildProductivityHoverMultiline(PlanetTile mouseTile)
        {
            var ordered = new List<WD_ProductivityOverlayMode>(4);
            if (ProductivityOverlayMode == WD_ProductivityOverlayMode.Fertility
                || ProductivityOverlayMode == WD_ProductivityOverlayMode.AnimalAbundance
                || ProductivityOverlayMode == WD_ProductivityOverlayMode.FishAbundance
                || ProductivityOverlayMode == WD_ProductivityOverlayMode.MiningRichness)
            {
                ordered.Add(ProductivityOverlayMode);
            }

            void AddIfMissing(WD_ProductivityOverlayMode mode)
            {
                if (!ordered.Contains(mode))
                    ordered.Add(mode);
            }

            AddIfMissing(WD_ProductivityOverlayMode.Fertility);
            AddIfMissing(WD_ProductivityOverlayMode.AnimalAbundance);
            AddIfMissing(WD_ProductivityOverlayMode.FishAbundance);
            AddIfMissing(WD_ProductivityOverlayMode.MiningRichness);

            var lines = new List<string>(4);
            for (int i = 0; i < ordered.Count; i++)
            {
                WD_ProductivityOverlayMode mode = ordered[i];
                int percent = Mathf.RoundToInt(WD_WorldLayer_ProductivityOverlay.GetCachedScore(mode, mouseTile) * 100f);
                lines.Add(GetProductivityHoverMetricLabel(mode) + ": " + percent + " %");
            }
            return string.Join("\n", lines);
        }

        private static string GetProductivityHoverMetricLabel(WD_ProductivityOverlayMode mode)
        {
            switch (mode)
            {
                case WD_ProductivityOverlayMode.Fertility:
                    return "Fertility";
                case WD_ProductivityOverlayMode.AnimalAbundance:
                    return "Animal Abundance";
                case WD_ProductivityOverlayMode.FishAbundance:
                    return "Fish Stocks";
                case WD_ProductivityOverlayMode.MiningRichness:
                    return "Mining efficiency";
                default:
                    return "";
            }
        }
    }
}
