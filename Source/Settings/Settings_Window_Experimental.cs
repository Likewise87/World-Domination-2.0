using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public class Dialog_ExperimentalSettings : Window
    {
        private Vector2 scrollPosition;
        private readonly string windowTitle;
        private bool worldActionsExpanded = true;
        private bool iconsExpanded = true;
        private bool controlsExpanded = true;
        private bool pollutionExpanded = true;

        public override Vector2 InitialSize => new Vector2(850f, 700f);

        public Dialog_ExperimentalSettings()
        {
            doCloseButton = true;
            forcePause = true;
            closeOnClickedOutside = true;
            windowTitle = "TSA_WD_BtnExperimental".Translate();
            optionalTitle = null;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Rect contentRect = SettingsUI.DrawWindowTitle(inRect, windowTitle);
            float contentWidth = contentRect.width - 24f;
            Rect scrollViewRect = new Rect(0f, 0f, contentWidth, 2200f);

            Widgets.BeginScrollView(contentRect, ref scrollPosition, scrollViewRect);
            Listing_Standard l = new Listing_Standard();
            l.Begin(scrollViewRect);

            var s = WorldDominationMod.settings;
            SettingsUI.DrawMenuTopBar(l, SettingsUI.ResetPageToDefaultsLabel, () => s.ResetExperimental(),
                () => { worldActionsExpanded = iconsExpanded = controlsExpanded = pollutionExpanded = true; },
                () => { worldActionsExpanded = iconsExpanded = controlsExpanded = pollutionExpanded = false; });
            SettingsUI.DrawSettingsSearchBar(l);

            SettingsUI.DrawCheckbox(l, "TSA_WD_Experimental_OutpostWithdrawStrengthBudget".Translate(),
                ref s.experimentalOutpostWithdrawStrengthBudget,
                "TSA_WD_Experimental_OutpostWithdrawStrengthBudgetTip".Translate(),
                defaultValue: WorldDominationSettings.DefExperimentalOutpostWithdrawStrengthBudget);
            SettingsUI.DrawCheckbox(l, "TSA_WD_Experimental_OutpostDefenseDeployBudget".Translate(),
                ref s.experimentalOutpostDefenseDeployBudget,
                "TSA_WD_Experimental_OutpostDefenseDeployBudgetTip".Translate(),
                defaultValue: WorldDominationSettings.DefExperimentalOutpostDefenseDeployBudget);

            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Experimental_HeaderWorldActionsRaidLogic".Translate(), ref worldActionsExpanded, SettingsUI.SectionHeaderColor))
            {
                SettingsUI.DrawCheckbox(l, "TSA_WD_Experimental_ColonyWorldBuild".Translate(),
                    ref s.experimentalColonyWorldBuild,
                    "TSA_WD_Experimental_ColonyWorldBuildTip".Translate(),
                    defaultValue: WorldDominationSettings.DefExperimentalColonyWorldBuild);
                SettingsUI.DrawCheckbox(l, "TSA_WD_Experimental_PlayerConquestRaze".Translate(),
                    ref s.experimentalPlayerConquestRaze,
                    "TSA_WD_Experimental_PlayerConquestRazeTip".Translate(),
                    defaultValue: WorldDominationSettings.DefExperimentalPlayerConquestRaze);
                SettingsUI.DrawCheckbox(l, "TSA_WD_Quest_EnableFirstOutpost".Translate(),
                    ref s.enableFirstOutpostQuest,
                    "TSA_WD_Quest_EnableFirstOutpostTip".Translate(),
                    defaultValue: WorldDominationSettings.DefEnableFirstOutpostQuest);
                SettingsUI.DrawCheckbox(l, "TSA_WD_Quest_EnableCommonEnemySettlement".Translate(),
                    ref s.enableCommonEnemySettlementQuest,
                    "TSA_WD_Quest_EnableCommonEnemySettlementTip".Translate(),
                    defaultValue: WorldDominationSettings.DefEnableCommonEnemySettlementQuest);
                SettingsUI.DrawCheckbox(l, "TSA_WD_Quest_EnableColonyRoadLink".Translate(),
                    ref s.enableColonyRoadLinkQuest,
                    "TSA_WD_Quest_EnableColonyRoadLinkTip".Translate(),
                    defaultValue: WorldDominationSettings.DefEnableColonyRoadLinkQuest);
                bool victoryQuestWas = s.enableWorldDominationVictoryQuest;
                SettingsUI.DrawCheckbox(l, "TSA_WD_Quest_EnableWorldDominationVictory".Translate(),
                    ref s.enableWorldDominationVictoryQuest,
                    "TSA_WD_Quest_EnableWorldDominationVictoryTip".Translate(),
                    defaultValue: WorldDominationSettings.DefEnableWorldDominationVictoryQuest);
                if (Current.ProgramState == ProgramState.Playing
                    && victoryQuestWas != s.enableWorldDominationVictoryQuest)
                {
                    if (s.enableWorldDominationVictoryQuest)
                        WdWorldDominationVictoryQuestHelper.TryLaunchNowIfEligible();
                    else
                        WdWorldDominationVictoryQuestHelper.RemoveActiveIfAny();
                }

                SettingsUI.DrawCheckbox(l, "TSA_WD_Experimental_EnableWorldMapSounds".Translate(),
                    ref s.enableWorldMapSounds,
                    "TSA_WD_Experimental_EnableWorldMapSoundsTip".Translate(),
                    defaultValue: WorldDominationSettings.DefEnableWorldMapSounds);

                SettingsUI.DrawCheckbox(l, "TSA_WD_Experimental_UnlimitedAssaultMortarSupport".Translate(),
                    ref s.experimentalUnlimitedAssaultMortarSupport,
                    "TSA_WD_Experimental_UnlimitedAssaultMortarSupportTip".Translate(),
                    defaultValue: WorldDominationSettings.DefExperimentalUnlimitedAssaultMortarSupport);
            }

            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Notify_HeaderWorldMapIcons".Translate(), ref iconsExpanded, SettingsUI.SectionHeaderColor))
            {
                bool prevOutpostTravelerIcons = s.alwaysShowOutpostTravelerIconsRegardlessOfZoom;
                bool prevSettlementIcons = s.alwaysShowSettlementIconsRegardlessOfZoom;

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_AlwaysShowOutpostTravelerIcons".Translate(),
                    ref s.alwaysShowOutpostTravelerIconsRegardlessOfZoom,
                    "TSA_WD_Notify_AlwaysShowOutpostTravelerIconsTip".Translate(),
                    defaultValue: WorldDominationSettings.DefAlwaysShowOutpostTravelerIconsRegardlessOfZoom);

                SettingsUI.DrawCheckbox(l, "TSA_WD_Notify_AlwaysShowSettlementIcons".Translate(),
                    ref s.alwaysShowSettlementIconsRegardlessOfZoom,
                    "TSA_WD_Notify_AlwaysShowSettlementIconsTip".Translate(),
                    defaultValue: WorldDominationSettings.DefAlwaysShowSettlementIconsRegardlessOfZoom);

                if (prevOutpostTravelerIcons != s.alwaysShowOutpostTravelerIconsRegardlessOfZoom
                    || prevSettlementIcons != s.alwaysShowSettlementIconsRegardlessOfZoom)
                {
                    Patch_WdWorldObjectNoExpandingIcon.NotifyIconModeChanged();
                }
            }

            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Experimental_HeaderControls".Translate(), ref controlsExpanded, SettingsUI.SectionHeaderColor))
            {
                DrawWorldMapOverlayHoldKeyRow(l, s);
                SettingsUI.DrawCheckbox(l, "TSA_WD_AutoAdd_DefaultOn".Translate(),
                    ref s.autoAddPawnsOnArrivalDefault,
                    "TSA_WD_AutoAdd_DefaultOnTip".Translate(),
                    defaultValue: WorldDominationSettings.DefAutoAddPawnsOnArrivalDefault);
                SettingsUI.DrawCheckbox(l, "TSA_WD_TravelFood_PrisonerRecruit".Translate(),
                    ref s.giveFoodOnPrisonerRecruitTransfer,
                    "TSA_WD_TravelFood_PrisonerRecruitTip".Translate(),
                    defaultValue: WorldDominationSettings.DefGiveFoodOnPrisonerRecruitTransfer);
                SettingsUI.DrawCheckbox(l, "TSA_WD_TravelFood_AllPlayerPawns".Translate(),
                    ref s.giveFoodOnAllPlayerPawnsTransfer,
                    "TSA_WD_TravelFood_AllPlayerPawnsTip".Translate(),
                    defaultValue: WorldDominationSettings.DefGiveFoodOnAllPlayerPawnsTransfer);
                SettingsUI.DrawCheckbox(l, "TSA_WD_OutpostSim_ShowInWdMenu".Translate(),
                    ref s.showOutpostRequirementsPreviewInWdMenu,
                    "TSA_WD_OutpostSim_ShowInWdMenuTip".Translate(),
                    defaultValue: WorldDominationSettings.DefShowOutpostRequirementsPreviewInWdMenu);
            }

            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Experimental_HeaderPollution".Translate(), ref pollutionExpanded, SettingsUI.SectionHeaderColor))
            {
                SettingsUI.DrawCheckbox(l, "TSA_WD_Pollution_EnableTravelerDamage".Translate(),
                    ref s.travelerPollutionDamageEnabled,
                    "TSA_WD_Pollution_EnableTravelerDamageTip".Translate(),
                    defaultValue: WorldDominationSettings.DefTravelerPollutionDamageEnabled);

                SettingsUI.DrawCheckbox(l, "TSA_WD_Pollution_WasterImmunity".Translate(),
                    ref s.wasterPollutionImmunityEnabled,
                    "TSA_WD_Pollution_WasterImmunityTip".Translate(),
                    defaultValue: WorldDominationSettings.DefWasterPollutionImmunityEnabled);

                if (s.travelerPollutionDamageEnabled)
                {
                    SettingsUI.DrawCheckbox(l, "TSA_WD_Pollution_DamageRaiders".Translate(),
                        ref s.pollutionDamageRaiders,
                        "TSA_WD_Pollution_DamageRaidersTip".Translate(),
                        defaultValue: WorldDominationSettings.DefPollutionDamageRaiders);
                    SettingsUI.DrawCheckbox(l, "TSA_WD_Pollution_DamageExpansion".Translate(),
                        ref s.pollutionDamageExpansion,
                        "TSA_WD_Pollution_DamageExpansionTip".Translate(),
                        defaultValue: WorldDominationSettings.DefPollutionDamageExpansion);
                    SettingsUI.DrawCheckbox(l, "TSA_WD_Pollution_DamageConstruction".Translate(),
                        ref s.pollutionDamageConstruction,
                        "TSA_WD_Pollution_DamageConstructionTip".Translate(),
                        defaultValue: WorldDominationSettings.DefPollutionDamageConstruction);
                    SettingsUI.DrawCheckbox(l, "TSA_WD_Pollution_DamageTraders".Translate(),
                        ref s.pollutionDamageTraders,
                        "TSA_WD_Pollution_DamageTradersTip".Translate(),
                        defaultValue: WorldDominationSettings.DefPollutionDamageTraders);
                    SettingsUI.DrawCheckbox(l, "TSA_WD_Pollution_DamagePlayer".Translate(),
                        ref s.pollutionDamagePlayerTravelers,
                        "TSA_WD_Pollution_DamagePlayerTip".Translate(),
                        defaultValue: WorldDominationSettings.DefPollutionDamagePlayerTravelers);

                    s.pollutionDamageIgnoreBelow = SettingsUI.LabeledSlider(l,
                        "TSA_WD_Pollution_IgnoreBelow".Translate(),
                        s.pollutionDamageIgnoreBelow, 0f, 0.5f,
                        "TSA_WD_Pollution_IgnoreBelowTip".Translate(),
                        0.01f, SliderFormat.Percent, WorldDominationSettings.DefPollutionDamageIgnoreBelow);
                    s.pollutionDamageAtThreshold = SettingsUI.LabeledSlider(l,
                        "TSA_WD_Pollution_DamageAtThreshold".Translate(),
                        s.pollutionDamageAtThreshold, 0f, 100f,
                        "TSA_WD_Pollution_DamageAtThresholdTip".Translate(),
                        1f, SliderFormat.Fixed0, WorldDominationSettings.DefPollutionDamageAtThreshold);
                    s.pollutionDamageAtFull = SettingsUI.LabeledSlider(l,
                        "TSA_WD_Pollution_DamageAtFull".Translate(),
                        s.pollutionDamageAtFull, 0f, 1000f,
                        "TSA_WD_Pollution_DamageAtFullTip".Translate(),
                        5f, SliderFormat.Fixed0, WorldDominationSettings.DefPollutionDamageAtFull);
                    s.pollutionDamageRadius = Mathf.RoundToInt(SettingsUI.LabeledSlider(l,
                        "TSA_WD_Pollution_DamageRadius".Translate(),
                        s.pollutionDamageRadius, 0f, 10f,
                        "TSA_WD_Pollution_DamageRadiusTip".Translate(),
                        1f, SliderFormat.Fixed0, WorldDominationSettings.DefPollutionDamageRadius));
                    s.npcSettlementDecontaminationStrengthCost = SettingsUI.LabeledSlider(l,
                        "TSA_WD_Pollution_NpcDecontamCost".Translate(),
                        s.npcSettlementDecontaminationStrengthCost, 0f, 100f,
                        "TSA_WD_Pollution_NpcDecontamCostTip".Translate(),
                        1f, SliderFormat.Fixed0, WorldDominationSettings.DefNpcSettlementDecontaminationStrengthCost);
                }

                SettingsUI.DrawCheckbox(l, "TSA_WD_Pollution_PathCost".Translate(),
                    ref s.pollutionPathCostEnabled,
                    "TSA_WD_Pollution_PathCostTip".Translate(),
                    defaultValue: WorldDominationSettings.DefPollutionPathCostEnabled);
                SettingsUI.DrawCheckbox(l, "TSA_WD_Pollution_PathRepath".Translate(),
                    ref s.pollutionPathRepathEnabled,
                    "TSA_WD_Pollution_PathRepathTip".Translate(),
                    defaultValue: WorldDominationSettings.DefPollutionPathRepathEnabled);
                SettingsUI.DrawCheckbox(l, "TSA_WD_Pollution_PathPreCommitCancel".Translate(),
                    ref s.pollutionPathPreCommitCancelEnabled,
                    "TSA_WD_Pollution_PathPreCommitCancelTip".Translate(),
                    defaultValue: WorldDominationSettings.DefPollutionPathPreCommitCancelEnabled);
            }

            l.End();
            Widgets.EndScrollView();
        }

        private static void DrawWorldMapOverlayHoldKeyRow(Listing_Standard l, WorldDominationSettings s)
        {
            l.Gap(2f);
            Rect row = l.GetRect(24f);
            string tip = SettingsUI.TooltipWithDefault(
                "TSA_WD_WorldMapOverlayHoldKey_Tooltip".Translate(),
                FormatOverlayHoldKey(WorldDominationSettings.DefWorldMapOverlayHoldKey));
            TooltipHandler.TipRegion(row, tip);

            Rect labelRect = new Rect(row.x, row.y, row.width - 110f, row.height);
            Rect btnRect = new Rect(row.xMax - 106f, row.y, 106f, row.height);
            Widgets.Label(labelRect, "TSA_WD_WorldMapOverlayHoldKey".Translate());
            if (Widgets.ButtonText(btnRect, FormatOverlayHoldKey(s.worldMapOverlayHoldKey)))
            {
                var options = new List<FloatMenuOption>();
                foreach (KeyCode key in OverlayHoldKeyChoices)
                {
                    KeyCode captured = key;
                    options.Add(new FloatMenuOption(FormatOverlayHoldKey(captured), () => s.worldMapOverlayHoldKey = captured));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }

        private static readonly KeyCode[] OverlayHoldKeyChoices =
        {
            KeyCode.LeftAlt, KeyCode.RightAlt,
            KeyCode.LeftControl, KeyCode.RightControl,
            KeyCode.LeftShift, KeyCode.RightShift,
            KeyCode.A, KeyCode.B, KeyCode.C, KeyCode.D, KeyCode.E, KeyCode.F, KeyCode.G, KeyCode.H,
            KeyCode.I, KeyCode.J, KeyCode.K, KeyCode.L, KeyCode.M, KeyCode.N, KeyCode.O, KeyCode.P,
            KeyCode.Q, KeyCode.R, KeyCode.S, KeyCode.T, KeyCode.U, KeyCode.V, KeyCode.W, KeyCode.X,
            KeyCode.Y, KeyCode.Z
        };

        private static string FormatOverlayHoldKey(KeyCode key)
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
    }
}
