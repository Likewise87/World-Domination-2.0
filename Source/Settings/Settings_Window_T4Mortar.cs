using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Dedicated settings for enemy tier-4 settlement mortars and anti-air.</summary>
    public class Dialog_T4MortarSettings : Window
    {
        private Vector2 scrollPosition;
        private readonly string windowTitle;
        private bool requirementsExpanded = true;
        private bool mortarExpanded;
        private bool antiAirExpanded;

        public override Vector2 InitialSize => new Vector2(850f, 850f);

        public Dialog_T4MortarSettings()
        {
            doCloseButton = true;
            forcePause = true;
            closeOnClickedOutside = true;
            windowTitle = "TSA_WD_BtnT4Mortar".Translate();
            optionalTitle = null;
        }

        public override void PreClose()
        {
            base.PreClose();
            WorldDominationMod.settings?.NormalizeEscalationT4Flags();
            if (Current.ProgramState != ProgramState.Playing) return;
            Find.World?.GetComponent<WorldComponent_SpreadManager>()?.Notify_WeightsChanged();
        }

        public override void DoWindowContents(Rect inRect)
        {
            Rect contentRect = SettingsUI.DrawWindowTitle(inRect, windowTitle);
            float contentWidth = contentRect.width - 24f;
            Rect scrollViewRect = new Rect(0f, 0f, contentWidth, 1900f);

            Widgets.BeginScrollView(contentRect, ref scrollPosition, scrollViewRect);

            Listing_Standard l = new Listing_Standard();
            l.Begin(scrollViewRect);
            var s = WorldDominationMod.settings;
            SettingsUI.DrawMenuTopBar(l, SettingsUI.ResetPageToDefaultsLabel, () => s.ResetT4Mortar(),
                () => { requirementsExpanded = mortarExpanded = antiAirExpanded = true; },
                () => { requirementsExpanded = mortarExpanded = antiAirExpanded = false; });

            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_T4Mortar_Header".Translate(), ref requirementsExpanded, SettingsUI.SectionHeaderColor))
            {

            SettingsUI.TechLevelDropdown(l, "TSA_WD_T4Mortar_MinTech".Translate(), s.npcT4MortarMinTechLevel,
                v => s.npcT4MortarMinTechLevel = v,
                "TSA_WD_T4Mortar_MinTechTip".Translate(), WorldDominationSettings.DefNpcT4MortarMinTechLevel);
            }
            l.GapLine();
            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Outpost_HeaderMortar".Translate(), ref mortarExpanded, SettingsUI.SectionHeaderColor))
            {

            l.CheckboxLabeled("TSA_WD_T4Mortar_EnableAll".Translate(), ref s.enableNpcT4Mortar,
                SettingsUI.TooltipWithDefault("TSA_WD_T4Mortar_EnableAllTooltip".Translate(), WorldDominationSettings.DefEnableNpcT4Mortar));

            if (s.enableNpcT4Mortar)
            {
                l.CheckboxLabeled("TSA_WD_T4Mortar_TargetPlayer".Translate(), ref s.enableT4SettlementMortar,
                    SettingsUI.TooltipWithDefault("TSA_WD_T4Mortar_TargetPlayerTooltip".Translate(), WorldDominationSettings.DefEnableT4SettlementMortar));
                s.NormalizeEscalationT4Flags();

                s.npcMortarRange = SettingsUI.LabeledSlider(l, "TSA_WD_T4Mortar_Range".Translate(), s.npcMortarRange, 10f, 250f,
                    "TSA_WD_T4Mortar_RangeTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefNpcMortarRange);

                s.npcMortarCooldownDays = SettingsUI.LabeledSlider(l, "TSA_WD_T4Mortar_Cooldown".Translate(), s.npcMortarCooldownDays, 0.1f, 20f,
                    "TSA_WD_T4Mortar_CooldownTooltip".Translate(), 0.05f, SliderFormat.Fixed1, WorldDominationSettings.DefNpcMortarCooldownDays);

                s.npcMortarDamage = SettingsUI.LabeledSlider(l, "TSA_WD_Settings_NpcMortarDamage".Translate(), s.npcMortarDamage, 0f, 600f,
                    "TSA_WD_Settings_NpcMortarDamageTooltip".Translate(), 10f, SliderFormat.Fixed0, WorldDominationSettings.DefNpcMortarDamage);

                s.npcMortarSkillEquivalent = SettingsUI.LabeledSlider(l, "TSA_WD_Settings_NpcMortarSkillEquivalent".Translate(), s.npcMortarSkillEquivalent, 0f, 40f,
                    "TSA_WD_Settings_NpcMortarSkillEquivalentTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefNpcMortarSkillEquivalent);

                s.npcMortarHitChance0To50PctRange = SettingsUI.LabeledSlider(l, "TSA_WD_T4Mortar_HitBand0To50".Translate(), s.npcMortarHitChance0To50PctRange, 0f, 1f,
                    "TSA_WD_T4Mortar_HitBand0To50Tooltip".Translate(), 0.01f, SliderFormat.PercentDecimal, WorldDominationSettings.DefNpcMortarHitChance0To50PctRange);

                s.npcMortarHitChance51To75PctRange = SettingsUI.LabeledSlider(l, "TSA_WD_T4Mortar_HitBand51To75".Translate(), s.npcMortarHitChance51To75PctRange, 0f, 1f,
                    "TSA_WD_T4Mortar_HitBand51To75Tooltip".Translate(), 0.01f, SliderFormat.PercentDecimal, WorldDominationSettings.DefNpcMortarHitChance51To75PctRange);

                s.npcMortarHitChance76To100PctRange = SettingsUI.LabeledSlider(l, "TSA_WD_T4Mortar_HitBand76To100".Translate(), s.npcMortarHitChance76To100PctRange, 0f, 1f,
                    "TSA_WD_T4Mortar_HitBand76To100Tooltip".Translate(), 0.01f, SliderFormat.PercentDecimal, WorldDominationSettings.DefNpcMortarHitChance76To100PctRange);
            }
            else
            {
                l.Gap(6f);
                GUI.color = Color.gray;
                l.Label("    <i>" + "TSA_WD_T4Mortar_DisabledHint".Translate() + "</i>");
                GUI.color = Color.white;
            }
            }
            l.GapLine();
            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Outpost_HeaderAntiAir".Translate(), ref antiAirExpanded, SettingsUI.SectionHeaderColor))
            {

            l.CheckboxLabeled("TSA_WD_T4AA_EnableAll".Translate(), ref s.enableNpcT4AntiAir,
                SettingsUI.TooltipWithDefault("TSA_WD_T4AA_EnableAllTooltip".Translate(), WorldDominationSettings.DefEnableNpcT4AntiAir));

            if (s.enableNpcT4AntiAir)
            {
                l.CheckboxLabeled("TSA_WD_T4AA_TargetPlayer".Translate(), ref s.enableT4SettlementAntiAir,
                    SettingsUI.TooltipWithDefault("TSA_WD_T4AA_TargetPlayerTooltip".Translate(), WorldDominationSettings.DefEnableT4SettlementAntiAir));
                s.NormalizeEscalationT4Flags();

                s.npcAntiAirRange = SettingsUI.LabeledSlider(l, "TSA_WD_T4AA_Range".Translate(), s.npcAntiAirRange, 10f, 250f,
                    "TSA_WD_T4AA_RangeTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefNpcAntiAirRange);

                s.npcAntiAirCooldownSeconds = SettingsUI.LabeledSlider(l, "TSA_WD_T4AA_Cooldown".Translate(), s.npcAntiAirCooldownSeconds, 5f, 300f,
                    "TSA_WD_T4AA_CooldownTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefNpcAntiAirCooldownSeconds);

                s.npcAntiAirDamage = SettingsUI.LabeledSlider(l, "TSA_WD_T4AA_Damage".Translate(), s.npcAntiAirDamage, 100f, 2000f,
                    "TSA_WD_T4AA_DamageTooltip".Translate(), 10f, SliderFormat.Fixed0, WorldDominationSettings.DefNpcAntiAirDamage);

                s.npcAntiAirSkillEquivalent = SettingsUI.LabeledSlider(l, "TSA_WD_T4AA_Skill".Translate(), s.npcAntiAirSkillEquivalent, 0f, 40f,
                    "TSA_WD_T4AA_SkillTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefNpcAntiAirSkillEquivalent);

                s.npcAntiAirHitChance0To50PctRange = SettingsUI.LabeledSlider(l, "TSA_WD_T4AA_HitBand0To50".Translate(), s.npcAntiAirHitChance0To50PctRange, 0f, 1f,
                    "TSA_WD_T4AA_HitBand0To50Tooltip".Translate(), 0.01f, SliderFormat.PercentDecimal, WorldDominationSettings.DefNpcAntiAirHitChance0To50PctRange);

                s.npcAntiAirHitChance51To75PctRange = SettingsUI.LabeledSlider(l, "TSA_WD_T4AA_HitBand51To75".Translate(), s.npcAntiAirHitChance51To75PctRange, 0f, 1f,
                    "TSA_WD_T4AA_HitBand51To75Tooltip".Translate(), 0.01f, SliderFormat.PercentDecimal, WorldDominationSettings.DefNpcAntiAirHitChance51To75PctRange);

                s.npcAntiAirHitChance76To100PctRange = SettingsUI.LabeledSlider(l, "TSA_WD_T4AA_HitBand76To100".Translate(), s.npcAntiAirHitChance76To100PctRange, 0f, 1f,
                    "TSA_WD_T4AA_HitBand76To100Tooltip".Translate(), 0.01f, SliderFormat.PercentDecimal, WorldDominationSettings.DefNpcAntiAirHitChance76To100PctRange);

                s.npcAntiAirVsMortarHitChance = SettingsUI.LabeledSlider(l, "TSA_WD_T4AA_VsMortar".Translate(), s.npcAntiAirVsMortarHitChance, 0f, 1f,
                    "TSA_WD_T4AA_VsMortarTooltip".Translate(), 0.01f, SliderFormat.PercentDecimal, WorldDominationSettings.DefNpcAntiAirVsMortarHitChance);
            }

            l.GapLine();
            GUI.color = Color.gray;
            l.Label("TSA_WD_T4Mortar_ScanSharedNote".Translate());
            GUI.color = Color.white;

            float scanSec = s.interceptionScanIntervalTicks / 60f;
            scanSec = SettingsUI.LabeledSlider(l, "TSA_WD_Settings_InterceptionScanIntervalSec".Translate(), scanSec, 5f, 120f,
                "TSA_WD_Settings_InterceptionScanIntervalSecTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefInterceptionScanIntervalTicks / 60f);
            s.interceptionScanIntervalTicks = Mathf.Max(60, Mathf.RoundToInt(scanSec * 60f));
            }
            l.End();
            Widgets.EndScrollView();
        }
    }
}
