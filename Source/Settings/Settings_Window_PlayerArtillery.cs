using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Player mortar, anti-air, and Rapid Response (extracted from Outpost Settings).</summary>
    public class Dialog_PlayerArtillerySettings : Window
    {
        private Vector2 scrollPosition;
        private readonly string windowTitle;
        private bool mortarExpanded = true;
        private bool antiAirExpanded = false;
        private bool rapidResponseExpanded = false;

        public override Vector2 InitialSize => new Vector2(850f, 750f);

        public Dialog_PlayerArtillerySettings()
        {
            doCloseButton = true;
            forcePause = true;
            closeOnClickedOutside = true;
            windowTitle = "TSA_WD_BtnPlayerArtillery".Translate();
            optionalTitle = null;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Rect contentRect = SettingsUI.DrawWindowTitle(inRect, windowTitle);
            float contentWidth = contentRect.width - 24f;
            Rect scrollViewRect = new Rect(0f, 0f, contentWidth, 1600f);

            Widgets.BeginScrollView(contentRect, ref scrollPosition, scrollViewRect);

            Listing_Standard l = new Listing_Standard();
            l.Begin(scrollViewRect);
            var s = WorldDominationMod.settings;
            bool advanced = s.showAdvancedSettings;

            SettingsUI.DrawMenuTopBar(l, SettingsUI.ResetPageToDefaultsLabel, () => s.ResetPlayerArtillery(),
                () => { mortarExpanded = antiAirExpanded = rapidResponseExpanded = true; },
                () => { mortarExpanded = antiAirExpanded = rapidResponseExpanded = false; });

            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Outpost_HeaderMortar".Translate(), ref mortarExpanded, SettingsUI.SectionHeaderColor))
            {
                s.cooldownMortarDays = SettingsUI.LabeledSlider(l, "TSA_WD_Settings_CooldownMortarDays".Translate(), s.cooldownMortarDays, 0.1f, 20f,
                    "TSA_WD_Settings_CooldownMortarDaysTooltip".Translate(), 0.05f, SliderFormat.Fixed1, WorldDominationSettings.DefCooldownMortarDays);
                s.mortarBaseShellDamage = SettingsUI.LabeledSlider(l, "TSA_WD_Settings_MortarBaseShellDamage".Translate(), s.mortarBaseShellDamage, 50f, 800f,
                    "TSA_WD_Settings_MortarBaseShellDamageTooltip".Translate(), 5f, SliderFormat.Fixed0, WorldDominationSettings.DefMortarBaseShellDamage);
                s.mortarRange = SettingsUI.LabeledSlider(l, "TSA_WD_Settings_MortarRange".Translate(), s.mortarRange, 10f, 250f,
                    "TSA_WD_Settings_MortarRangeTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefMortarRange);
                s.mortarShellTicksPerMove = SettingsUI.LabeledSlider(l, "TSA_WD_Settings_MortarShellTicksPerMove".Translate(), s.mortarShellTicksPerMove, 1f, 40f,
                    "TSA_WD_Settings_MortarShellTicksPerMoveTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefMortarShellTicksPerMove);
                s.mortarHitChance0To50PctRange = SettingsUI.LabeledSlider(l, "TSA_WD_Settings_MortarHitBand0To50".Translate(), s.mortarHitChance0To50PctRange, 0f, 1f,
                    "TSA_WD_Settings_MortarHitBand0To50Tooltip".Translate(), 0.01f, SliderFormat.PercentDecimal, WorldDominationSettings.DefMortarHitChance0To50PctRange);
                s.mortarHitChance51To75PctRange = SettingsUI.LabeledSlider(l, "TSA_WD_Settings_MortarHitBand51To75".Translate(), s.mortarHitChance51To75PctRange, 0f, 1f,
                    "TSA_WD_Settings_MortarHitBand51To75Tooltip".Translate(), 0.01f, SliderFormat.PercentDecimal, WorldDominationSettings.DefMortarHitChance51To75PctRange);
                s.mortarHitChance76To100PctRange = SettingsUI.LabeledSlider(l, "TSA_WD_Settings_MortarHitBand76To100".Translate(), s.mortarHitChance76To100PctRange, 0f, 1f,
                    "TSA_WD_Settings_MortarHitBand76To100Tooltip".Translate(), 0.01f, SliderFormat.PercentDecimal, WorldDominationSettings.DefMortarHitChance76To100PctRange);
            }

            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Outpost_HeaderAntiAir".Translate(), ref antiAirExpanded, SettingsUI.SectionHeaderColor))
            {
                s.antiAirRange = SettingsUI.LabeledSlider(l, "TSA_WD_Settings_AntiAirRange".Translate(), s.antiAirRange, 10f, 250f,
                    "TSA_WD_Settings_AntiAirRangeTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefAntiAirRange);
                s.antiAirBaseDamage = SettingsUI.LabeledSlider(l, "TSA_WD_Settings_AntiAirBaseDamage".Translate(), s.antiAirBaseDamage, 100f, 2000f,
                    "TSA_WD_Settings_AntiAirBaseDamageTooltip".Translate(), 10f, SliderFormat.Fixed0, WorldDominationSettings.DefAntiAirBaseDamage);
                s.cooldownAntiAirSeconds = SettingsUI.LabeledSlider(l, "TSA_WD_Settings_CooldownAntiAirSeconds".Translate(), s.cooldownAntiAirSeconds, 20f, 300f,
                    "TSA_WD_Settings_CooldownAntiAirSecondsTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefCooldownAntiAirSeconds);
                s.antiAirCooldownFloorSeconds = SettingsUI.LabeledSlider(l, "TSA_WD_Settings_AntiAirCooldownFloorSeconds".Translate(), s.antiAirCooldownFloorSeconds, 5f, 120f,
                    "TSA_WD_Settings_AntiAirCooldownFloorSecondsTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefAntiAirCooldownFloorSeconds);
                s.antiAirHitChance0To50PctRange = SettingsUI.LabeledSlider(l, "TSA_WD_Settings_AntiAirHitBand0To50".Translate(), s.antiAirHitChance0To50PctRange, 0f, 1f,
                    "TSA_WD_Settings_AntiAirHitBand0To50Tooltip".Translate(), 0.01f, SliderFormat.PercentDecimal, WorldDominationSettings.DefAntiAirHitChance0To50PctRange);
                s.antiAirHitChance51To75PctRange = SettingsUI.LabeledSlider(l, "TSA_WD_Settings_AntiAirHitBand51To75".Translate(), s.antiAirHitChance51To75PctRange, 0f, 1f,
                    "TSA_WD_Settings_AntiAirHitBand51To75Tooltip".Translate(), 0.01f, SliderFormat.PercentDecimal, WorldDominationSettings.DefAntiAirHitChance51To75PctRange);
                s.antiAirHitChance76To100PctRange = SettingsUI.LabeledSlider(l, "TSA_WD_Settings_AntiAirHitBand76To100".Translate(), s.antiAirHitChance76To100PctRange, 0f, 1f,
                    "TSA_WD_Settings_AntiAirHitBand76To100Tooltip".Translate(), 0.01f, SliderFormat.PercentDecimal, WorldDominationSettings.DefAntiAirHitChance76To100PctRange);
                s.antiAirVsMortarHitChance = SettingsUI.LabeledSlider(l, "TSA_WD_Settings_AntiAirVsMortarHitChance".Translate(), s.antiAirVsMortarHitChance, 0f, 1f,
                    "TSA_WD_Settings_AntiAirVsMortarHitChanceTooltip".Translate(), 0.01f, SliderFormat.PercentDecimal, WorldDominationSettings.DefAntiAirVsMortarHitChance);
                s.flakShellTicksPerMove = SettingsUI.LabeledSlider(l, "TSA_WD_Settings_FlakShellTicksPerMove".Translate(), s.flakShellTicksPerMove, 1f, 20f,
                    "TSA_WD_Settings_FlakShellTicksPerMoveTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefFlakShellTicksPerMove);
            }

            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Outpost_HeaderRapidResponse".Translate(), ref rapidResponseExpanded, SettingsUI.SectionHeaderColor))
            {
                s.rapidResponseOffensiveStrengthBonus = SettingsUI.LabeledSlider(l, "TSA_WD_Outpost_RapidResponseOffenseBonus".Translate(), s.rapidResponseOffensiveStrengthBonus, 0f, 1f,
                    "TSA_WD_Outpost_RapidResponseOffenseBonusTooltip".Translate(), 0.01f, SliderFormat.PercentDecimal, WorldDominationSettings.DefRapidResponseOffensiveStrengthBonus);
                s.rapidResponseOffensiveRecoveryBonus = SettingsUI.LabeledSlider(l, "TSA_WD_Outpost_RapidResponseRecoveryBonus".Translate(), s.rapidResponseOffensiveRecoveryBonus, 0f, 1f,
                    "TSA_WD_Outpost_RapidResponseRecoveryBonusTooltip".Translate(), 0.01f, SliderFormat.PercentDecimal, WorldDominationSettings.DefRapidResponseOffensiveRecoveryBonus);
                s.rapidResponseAutoInterceptRange = SettingsUI.LabeledSlider(l, "TSA_WD_Outpost_RapidResponseAutoRange".Translate(), s.rapidResponseAutoInterceptRange, 1f, 100f,
                    "TSA_WD_Outpost_RapidResponseAutoRangeTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefRapidResponseAutoInterceptRange);
                s.rapidResponseDropPodRange = SettingsUI.LabeledSlider(l, "TSA_WD_Outpost_RapidResponseDropPodRange".Translate(), s.rapidResponseDropPodRange, 1f, 100f,
                    "TSA_WD_Outpost_RapidResponseDropPodRangeTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefRapidResponseDropPodRange);
                s.dropPodTicksPerMove = SettingsUI.LabeledSlider(l, "TSA_WD_Settings_DropPodTicksPerMove".Translate(), s.dropPodTicksPerMove, 1f, 40f,
                    "TSA_WD_Settings_DropPodTicksPerMoveTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefDropPodTicksPerMove);
                if (advanced)
                {
                    s.rapidResponseTicksPerMoveMultiplier = SettingsUI.LabeledSlider(l, "TSA_WD_Outpost_RapidResponseSpeedMult".Translate(), s.rapidResponseTicksPerMoveMultiplier, 0.25f, 1.25f,
                        "TSA_WD_Outpost_RapidResponseSpeedMultTooltip".Translate(), 0.01f, SliderFormat.PercentDecimal, WorldDominationSettings.DefRapidResponseTicksPerMoveMultiplier);
                }
            }

            l.End();
            Widgets.EndScrollView();
        }
    }
}
