using System;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace TSA_WorldDomination
{
    /// <summary>Draggable single-column World Setup window. Opens on the far left of Select Starting Site.</summary>
    [StaticConstructorOnStartup]
    public class Dialog_WdWorldSetup : Window
    {
        private Vector2 scrollPosition;
        private static bool generalExpanded = true;
        private static bool distributionExpanded = true;
        private static bool layoutExpanded = true;
        private static bool roadsExpanded = true;

        private const float ToolButtonHeight = 36f;
        private const float NavButtonIconSize = 26f;
        private const float NavButtonIconPad = 8f;
        private const float NavButtonIconTextGap = 6f;
        private const float PlusMinusIconInnerPad = 3f;
        private const float ScrollbarWidth = 16f;
        private const float ScrollContentRightPad = 20f;

        private static readonly Color NavSlateFill = new Color(0.16f, 0.18f, 0.22f, 0.92f);
        private static readonly Color NavBtnBgHover = new Color(0.22f, 0.26f, 0.32f, 0.96f);
        private static readonly Color NavBtnBgPress = new Color(0.12f, 0.14f, 0.17f, 0.96f);
        private static readonly Color NavBtnBgSelected = new Color(0.26f, 0.32f, 0.40f, 0.98f);
        private static readonly Color NavBtnOutline = new Color(0.55f, 0.62f, 0.72f, 0.42f);
        private static readonly Color NavBtnOutlineHover = new Color(0.78f, 0.84f, 0.92f, 0.72f);
        private static readonly Color NavBtnOutlineSelected = new Color(0.55f, 0.85f, 1f, 0.70f);

        private static readonly Texture2D IconPlaceSettlementFallback =
            ContentFinder<Texture2D>.Get("UI/Commands/Settle", false)
            ?? ContentFinder<Texture2D>.Get("UI/Commands/EstablishOutpost", false);
        private static readonly Texture2D IconRemoveSettlement = TexButton.Delete;
        private static readonly Texture2D IconStrengthPlus = TexButton.Plus;
        private static readonly Texture2D IconStrengthMinus = TexButton.Minus;
        private static readonly Texture2D IconTurret =
            ContentFinder<Texture2D>.Get(AtTurretUtility.TexturePathForTier(AtTurretTier.Medium), false);
        private static readonly Texture2D IconRoadBlock =
            ContentFinder<Texture2D>.Get("UI/Commands/Build_RoadBlock", false);
        private static readonly Texture2D IconTrap =
            ContentFinder<Texture2D>.Get("WorldObjects/WorldSpikeTrap", false);
        private static readonly Texture2D IconPlaceRoad =
            ContentFinder<Texture2D>.Get("UI/Commands/BuildRoad", false);
        private static readonly Texture2D IconRemoveFortify =
            ContentFinder<Texture2D>.Get("UI/Commands/Remove_WorldSpikeTrap", false)
            ?? ContentFinder<Texture2D>.Get("UI/Commands/Remove_RoadBlock", false);
        private static readonly Texture2D IconRemoveRoad =
            ContentFinder<Texture2D>.Get("UI/Commands/RemoveRoad", false)
            ?? ContentFinder<Texture2D>.Get("UI/Commands/Remove_RoadBlock", false);
        private static readonly Texture2D IconDiplomacy =
            ContentFinder<Texture2D>.Get("UI/Commands/Icon_Diplomacy", false);
        private static readonly Texture2D IconConfig =
            ContentFinder<Texture2D>.Get("UI/Commands/Config", false);

        public override Vector2 InitialSize
        {
            get
            {
                float h = Mathf.Min(720f, UI.screenHeight - 48f);
                return new Vector2(380f, h);
            }
        }

        public Dialog_WdWorldSetup()
        {
            doCloseButton = true;
            doCloseX = true;
            draggable = true;
            absorbInputAroundWindow = false;
            forcePause = false;
            closeOnClickedOutside = false;
            preventCameraMotion = false;
            optionalTitle = null;
        }

        protected override void SetInitialSizeAndPosition()
        {
            Vector2 size = InitialSize;
            float x = Patch_Page_SelectStartingSite_WdWorldSetup.ScreenPad;
            float y = Patch_Page_SelectStartingSite_WdWorldSetup.ScreenPad
                + Patch_Page_SelectStartingSite_WdWorldSetup.ButtonSize
                + 8f;
            if (y + size.y > UI.screenHeight - 8f)
                y = Mathf.Max(8f, UI.screenHeight - size.y - 8f);
            windowRect = new Rect(x, y, size.x, size.y).Rounded();
        }

        public override void PreClose()
        {
            WD_WorldSetupTools.CancelActive();
            base.PreClose();
        }

        public override void DoWindowContents(Rect inRect)
        {
            WD_WorldSetupTools.TickClearIfIdle();
            WD_SettlementLayoutUtility.EnsureVanillaSnapshot();
            WorldComponent_WDVisualizerToggle.ProcessWindowAndOverlayHotkeys();

            float y = inRect.y;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, Outpost_Dialog_UI.DialogTitleHeight),
                "TSA_WD_WorldSetup_Title".Translate());
            y += Outpost_Dialog_UI.DialogTitleRowAdvance;
            Text.Font = GameFont.Small;

            float bottomReserve = 44f;
            Rect scrollOut = new Rect(inRect.x, y, inRect.width, inRect.yMax - y - bottomReserve);
            Rect view = new Rect(0f, 0f, scrollOut.width - ScrollbarWidth - ScrollContentRightPad, EstimateViewHeight());
            Widgets.BeginScrollView(scrollOut, ref scrollPosition, view);

            var listing = new Listing_Standard();
            listing.Begin(view);

            if (SettingsUI.DrawCollapsibleHeader(listing, "TSA_WD_WorldSetup_SectionGeneral".Translate(),
                ref generalExpanded, SettingsUI.SectionHeaderColor))
            {
                DrawDashButton(listing, "TSA_WD_WorldSetup_Allegiances".Translate(), IconDiplomacy, false,
                    () => Find.WindowStack.Add(new Dialog_WdWorldGenAllegiances()),
                    "TSA_WD_WorldSetup_AllegiancesTooltip".Translate());
            }

            if (SettingsUI.DrawCollapsibleHeader(listing, "TSA_WD_WorldSetup_SectionDistribution".Translate(),
                ref distributionExpanded, SettingsUI.SectionHeaderColor))
            {
                var s = WorldDominationMod.settings;
                if (s != null)
                {
                    s.settlementTerritoryCoherence = SettingsUI.StackedSlider(
                        listing,
                        "TSA_WD_WorldSetup_Coherence".Translate(),
                        s.settlementTerritoryCoherence,
                        0f,
                        100f,
                        "TSA_WD_WorldSetup_CoherenceTooltip".Translate(),
                        step: 1f,
                        format: SliderFormat.Fixed0,
                        defaultValue: WorldDominationSettings.DefSettlementTerritoryCoherence);

                    listing.Gap(4f);
                    s.settlementTerritorySpacing = SettingsUI.StackedSlider(
                        listing,
                        "TSA_WD_WorldSetup_Spacing".Translate(),
                        s.settlementTerritorySpacing,
                        0f,
                        100f,
                        "TSA_WD_WorldSetup_SpacingTooltip".Translate(),
                        step: 1f,
                        format: SliderFormat.Fixed0,
                        defaultValue: WorldDominationSettings.DefSettlementTerritorySpacing);

                    listing.Gap(4f);
                    s.settlementOtherFactionDistance = SettingsUI.StackedSlider(
                        listing,
                        "TSA_WD_WorldSetup_OtherFactionDistance".Translate(),
                        s.settlementOtherFactionDistance,
                        0f,
                        100f,
                        "TSA_WD_WorldSetup_OtherFactionDistanceTooltip".Translate(),
                        step: 1f,
                        format: SliderFormat.Fixed0,
                        defaultValue: WorldDominationSettings.DefSettlementOtherFactionDistance);

                    listing.Gap(4f);
                    s.settlementMaxPerCluster = Mathf.RoundToInt(SettingsUI.StackedSlider(
                        listing,
                        "TSA_WD_WorldSetup_MaxPerCluster".Translate(),
                        s.settlementMaxPerCluster,
                        1f,
                        20f,
                        "TSA_WD_WorldSetup_MaxPerClusterTooltip".Translate(),
                        step: 1f,
                        format: SliderFormat.Fixed0,
                        defaultValue: WorldDominationSettings.DefSettlementMaxPerCluster));

                    listing.Gap(4f);
                    s.settlementMinDistanceBetweenClusters = Mathf.RoundToInt(SettingsUI.StackedSlider(
                        listing,
                        "TSA_WD_WorldSetup_MinClusterDistance".Translate(),
                        s.settlementMinDistanceBetweenClusters,
                        0f,
                        50f,
                        "TSA_WD_WorldSetup_MinClusterDistanceTooltip".Translate(),
                        step: 1f,
                        format: SliderFormat.Fixed0,
                        defaultValue: WorldDominationSettings.DefSettlementMinDistanceBetweenClusters));
                }

                listing.Gap(4f);
                int vanillaCount = WD_SettlementLayoutUtility.GetVanillaNpcSettlementTotal();
                int countMax = WD_SettlementLayoutUtility.GetSettlementCountSliderMax();
                int countMin = 1;
                if (countMax < countMin) countMax = countMin;
                int targetCount = Mathf.Clamp(WD_SettlementLayoutUtility.GetTargetNpcSettlementCount(), countMin, countMax);
                int nextCount = Mathf.RoundToInt(SettingsUI.StackedSlider(
                    listing,
                    "TSA_WD_WorldSetup_SettlementCount".Translate(),
                    targetCount,
                    countMin,
                    countMax,
                    "TSA_WD_WorldSetup_SettlementCountTooltip".Translate(),
                    step: 1f,
                    format: SliderFormat.Fixed0,
                    defaultValue: vanillaCount));
                if (nextCount != targetCount)
                    WD_SettlementLayoutUtility.SetTargetNpcSettlementCount(nextCount);

                listing.Gap(6f);
                DrawDashButton(listing, "TSA_WD_WorldSetup_FactionShares".Translate(), IconConfig, false,
                    () => Find.WindowStack.Add(new Dialog_WdWorldSetupFactionDistribution()),
                    "TSA_WD_WorldSetup_FactionSharesTooltip".Translate());
                listing.Gap(4f);
                DrawDashButton(listing, "TSA_WD_WorldSetup_TierLikelihood".Translate(), IconConfig, false,
                    () => Find.WindowStack.Add(new Dialog_WorldGenSettings()),
                    "TSA_WD_WorldSetup_TierLikelihoodTooltip".Translate());
                listing.Gap(4f);
                Rect recreateRect = listing.GetRect(30f);
                TooltipHandler.TipRegion(recreateRect, "TSA_WD_WorldSetup_RecreateTooltip".Translate());
                if (Widgets.ButtonText(recreateRect, "TSA_WD_WorldSetup_Recreate".Translate()))
                {
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        WD_SettlementLayoutUtility.BuildRecreateConfirmText(),
                        WD_SettlementLayoutUtility.RecreateNpcSettlements,
                        destructive: true));
                }

                if (s != null)
                {
                    listing.Gap(4f);
                    SettingsUI.DrawCheckbox(listing, "TSA_WD_WorldSetup_DestroyFortsOnRecreate".Translate(),
                        ref s.worldSetupDestroyFortificationsOnRecreate,
                        "TSA_WD_WorldSetup_DestroyFortsOnRecreateTip".Translate(),
                        defaultValue: WorldDominationSettings.DefWorldSetupDestroyFortificationsOnRecreate);
                }
            }

            if (SettingsUI.DrawCollapsibleHeader(listing, "TSA_WD_WorldSetup_SectionLayout".Translate(),
                ref layoutExpanded, SettingsUI.SectionHeaderColor))
            {
                ResolveOutlanderCivilIcon(out Texture2D placeIcon, out Color placeTint);
                DrawToolButton(listing, "TSA_WD_WorldSetup_ToolPlaceSettlement".Translate(),
                    placeIcon, WdWorldSetupTool.PlaceSettlement, WD_WorldSetupTools.BeginPlaceSettlement,
                    iconTint: placeTint);
                DrawToolButton(listing, "TSA_WD_WorldSetup_ToolRemoveSettlement".Translate(),
                    IconRemoveSettlement, WdWorldSetupTool.RemoveSettlement, WD_WorldSetupTools.BeginRemoveSettlement);
                DrawToolButton(listing, "TSA_WD_WorldSetup_ToolTierUp".Translate(),
                    IconStrengthPlus, WdWorldSetupTool.TierUp, () => WD_WorldSetupTools.BeginAdjustTier(1),
                    iconInnerPad: PlusMinusIconInnerPad);
                DrawToolButton(listing, "TSA_WD_WorldSetup_ToolTierDown".Translate(),
                    IconStrengthMinus, WdWorldSetupTool.TierDown, () => WD_WorldSetupTools.BeginAdjustTier(-1),
                    iconInnerPad: PlusMinusIconInnerPad);
                DrawToolButton(listing, "TSA_WD_WorldSetup_ToolStrengthPlus".Translate(),
                    IconStrengthPlus, WdWorldSetupTool.StrengthPlus, () => WD_WorldSetupTools.BeginAdjustStrength(100f),
                    iconInnerPad: PlusMinusIconInnerPad);
                DrawToolButton(listing, "TSA_WD_WorldSetup_ToolStrengthMinus".Translate(),
                    IconStrengthMinus, WdWorldSetupTool.StrengthMinus, () => WD_WorldSetupTools.BeginAdjustStrength(-100f),
                    iconInnerPad: PlusMinusIconInnerPad);
            }

            if (SettingsUI.DrawCollapsibleHeader(listing, "TSA_WD_WorldSetup_SectionRoadsFortify".Translate(),
                ref roadsExpanded, SettingsUI.SectionHeaderColor))
            {
                DrawToolButton(listing, "TSA_WD_WorldSetup_ToolTurret".Translate(),
                    IconTurret, WdWorldSetupTool.Turret, WD_WorldSetupTools.BeginPlaceAtTurret);
                DrawToolButton(listing, "TSA_WD_WorldSetup_ToolRoadBlock".Translate(),
                    IconRoadBlock, WdWorldSetupTool.RoadBlock, WD_WorldSetupTools.BeginPlaceRoadBlock);
                DrawToolButton(listing, "TSA_WD_WorldSetup_ToolTrap".Translate(),
                    IconTrap, WdWorldSetupTool.Trap, WD_WorldSetupTools.BeginPlaceSpikeTrap);
                DrawToolButton(listing, "TSA_WD_WorldSetup_ToolPlaceRoad".Translate(),
                    IconPlaceRoad, WdWorldSetupTool.PlaceRoad, WD_WorldSetupTools.BeginPlaceRoad);
                DrawToolButton(listing, "TSA_WD_WorldSetup_ToolRemoveFortify".Translate(),
                    IconRemoveFortify, WdWorldSetupTool.RemoveFortify, WD_WorldSetupTools.BeginRemoveFortification);
                DrawToolButton(listing, "TSA_WD_WorldSetup_ToolRemoveRoad".Translate(),
                    IconRemoveRoad, WdWorldSetupTool.RemoveRoad, WD_WorldSetupTools.BeginRemoveRoad);
            }

            listing.End();
            Widgets.EndScrollView();
        }

        private static float EstimateViewHeight()
        {
            float h = 16f;
            h += 40f;
            if (generalExpanded)
                h += 40f + 12f;
            h += 40f;
            if (distributionExpanded)
                h += 6f * 58f + 6f + 40f + 4f + 40f + 4f + 34f + 4f + 28f + 12f;
            h += 40f;
            if (layoutExpanded)
                h += 6f * 40f + 12f;
            h += 40f;
            if (roadsExpanded)
                h += 6f * 40f + 12f;
            return Mathf.Max(h, 200f);
        }

        private static void ResolveOutlanderCivilIcon(out Texture2D icon, out Color tint)
        {
            icon = IconPlaceSettlementFallback;
            tint = Color.white;
            FactionDef def = DefDatabase<FactionDef>.GetNamedSilentFail("OutlanderCivil");
            if (def == null) return;
            if (def.FactionIcon != null)
                icon = def.FactionIcon;

            Faction live = Find.FactionManager?.FirstFactionOfDef(def);
            if (live != null)
            {
                if (live.def?.FactionIcon != null)
                    icon = live.def.FactionIcon;
                tint = live.Color;
                return;
            }

            if (def.colorSpectrum != null && def.colorSpectrum.Count > 0)
                tint = def.colorSpectrum[0];
        }

        private static void DrawToolButton(Listing_Standard listing, string label, Texture2D icon,
            WdWorldSetupTool tool, Action begin, float iconInnerPad = 0f, Color? iconTint = null)
        {
            bool selected = WD_WorldSetupTools.ActiveTool == tool;
            DrawDashButton(listing, label, icon, selected, () =>
            {
                if (WD_WorldSetupTools.TryToggleOff(tool))
                    return;
                begin();
            }, null, iconInnerPad, iconTint);
            listing.Gap(4f);
        }

        private static void DrawDashButton(Listing_Standard listing, string label, Texture2D icon,
            bool selected, Action onClick, string tip, float iconInnerPad = 0f, Color? iconTint = null)
        {
            Rect r = listing.GetRect(ToolButtonHeight);
            bool mouseOver = Mouse.IsOver(r);
            bool pressed = mouseOver && Input.GetMouseButton(0);
            Color bg = selected ? NavBtnBgSelected : pressed ? NavBtnBgPress : mouseOver ? NavBtnBgHover : NavSlateFill;
            Widgets.DrawBoxSolid(r, bg);
            GUI.color = selected ? NavBtnOutlineSelected : mouseOver ? NavBtnOutlineHover : NavBtnOutline;
            Widgets.DrawBox(r, 1);
            GUI.color = Color.white;

            float textLeft = r.x + NavButtonIconPad;
            if (icon != null)
            {
                float pad = Mathf.Max(0f, iconInnerPad);
                Rect iconRect = new Rect(
                    r.x + NavButtonIconPad + pad,
                    r.y + (r.height - NavButtonIconSize) * 0.5f + pad,
                    NavButtonIconSize - pad * 2f,
                    NavButtonIconSize - pad * 2f);
                if (iconTint.HasValue)
                    GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, true, 0f, iconTint.Value, 0f, 0f);
                else
                    Widgets.DrawTextureFitted(iconRect, icon, 1f);
                textLeft = r.x + NavButtonIconPad + NavButtonIconSize + NavButtonIconTextGap;
            }

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            float labelW = Mathf.Max(0f, r.xMax - textLeft - 4f);
            Widgets.Label(new Rect(textLeft, r.y, labelW, r.height), label.Truncate(labelW));
            Text.Anchor = TextAnchor.UpperLeft;

            if (!tip.NullOrEmpty())
                TooltipHandler.TipRegion(r, tip);
            if (Widgets.ButtonInvisible(r))
            {
                SoundDefOf.Click.PlayOneShotOnCamera();
                onClick();
            }
        }
    }
}
