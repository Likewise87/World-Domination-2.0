using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public class Dialog_SabotageSettings : Window
    {
        private Vector2 scrollPosition;
        private readonly string windowTitle;
        private bool weightsExpanded = true;
        private bool modifiersExpanded;
        private bool savesExpanded;
        private bool outcomeExpanded;
        private bool simulationExpanded;

        public override Vector2 InitialSize => new Vector2(850f, 750f);
        public Dialog_SabotageSettings()
        {
            doCloseButton = true;
            forcePause = true;
            closeOnClickedOutside = true;
            windowTitle = "TSA_WD_BtnSabotage".Translate();
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
            SettingsUI.DrawMenuTopBar(l, SettingsUI.ResetPageToDefaultsLabel, () => s.ResetSabotage(),
                () => { weightsExpanded = modifiersExpanded = savesExpanded = outcomeExpanded = simulationExpanded = true; },
                () => { weightsExpanded = modifiersExpanded = savesExpanded = outcomeExpanded = simulationExpanded = false; });

            // ================= SECTION 1: BASE WEIGHTS =================
            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Sab_HeaderWeights".Translate(), ref weightsExpanded, SettingsUI.SectionHeaderColor))
            {
            float total = s.TotalSabWeight;

            s.weightSabSuccess = SettingsUI.WeightSlider(l, "TSA_WD_Sab_WeightSuccess".Translate(), s.weightSabSuccess, total, 0f, 200f,
                "TSA_WD_Sab_WeightSuccessTooltip".Translate(), WorldDominationSettings.DefWeightSabSuccess);

            s.weightSabCleanFail = SettingsUI.WeightSlider(l, "TSA_WD_Sab_WeightClean".Translate(), s.weightSabCleanFail, total, 0f, 200f,
                "TSA_WD_Sab_WeightCleanTooltip".Translate(), WorldDominationSettings.DefWeightSabCleanFail);

            s.weightSabInjuredFail = SettingsUI.WeightSlider(l, "TSA_WD_Sab_WeightInjured".Translate(), s.weightSabInjuredFail, total, 0f, 200f,
                "TSA_WD_Sab_WeightInjuredTooltip".Translate(), WorldDominationSettings.DefWeightSabInjuredFail);

            s.weightSabFatalFail = SettingsUI.WeightSlider(l, "TSA_WD_Sab_WeightFatal".Translate(), s.weightSabFatalFail, total, 0f, 200f,
                "TSA_WD_Sab_WeightFatalTooltip".Translate(), WorldDominationSettings.DefWeightSabFatalFail);
            }
            // ================= SECTION 2: Modifiers =================
            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Sab_HeaderModifiers".Translate(), ref modifiersExpanded, SettingsUI.SectionHeaderColor))
            {

            s.sabotageSkillSuccessWeightBonus = SettingsUI.LabeledSlider(l, "TSA_WD_Sab_SkillBonus".Translate(), s.sabotageSkillSuccessWeightBonus, 0f, 20f,
                "TSA_WD_Sab_SkillBonusTooltip".Translate(), 0.5f, SliderFormat.Fixed1, WorldDominationSettings.DefSabSkillSuccessWeightBonus);

            s.sabotageTierSuccessWeightPenalty = SettingsUI.LabeledSlider(l, "TSA_WD_Sab_TierPenalty".Translate(), s.sabotageTierSuccessWeightPenalty, 0f, 40f,
                "TSA_WD_Sab_TierPenaltyTooltip".Translate(), 1.0f, SliderFormat.Fixed1, WorldDominationSettings.DefSabTierSuccessWeightPenalty);

            s.sabotageHealthImpactWeight = SettingsUI.LabeledSlider(l, "TSA_WD_Sab_HealthImpact".Translate(), s.sabotageHealthImpactWeight, 0f, 1f,
                "TSA_WD_Sab_HealthImpactTooltip".Translate(), 0.05f, SliderFormat.Percent, WorldDominationSettings.DefSabHealthImpactWeight);
            }
            // ================= SECTION 3: SAVING THROWS =================
            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Sab_HeaderSaves".Translate(), ref savesExpanded, SettingsUI.SectionHeaderColor))
            {

            s.sabotageSocialCleanBonus = SettingsUI.LabeledSlider(l, "TSA_WD_Sab_SocialSave".Translate(), s.sabotageSocialCleanBonus, 0f, 0.05f,
                "TSA_WD_Sab_SocialSaveTooltip".Translate(), 0.002f, SliderFormat.PercentDecimal, WorldDominationSettings.DefSabSocialCleanBonus);

            s.sabotageCombatSurvivalBonus = SettingsUI.LabeledSlider(l, "TSA_WD_Sab_CombatSave".Translate(), s.sabotageCombatSurvivalBonus, 0f, 0.05f,
                "TSA_WD_Sab_CombatSaveTooltip".Translate(), 0.002f, SliderFormat.PercentDecimal, WorldDominationSettings.DefSabCombatSurvivalBonus);
            }
            // ================= SECTION 4: OUTCOME & COOLDOWN =================
            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Sab_HeaderOutcome".Translate(), ref outcomeExpanded, SettingsUI.SectionHeaderColor))
            {

            s.sabotageBaseReduction = SettingsUI.LabeledSlider(l, "TSA_WD_Sab_BaseReduc".Translate(), s.sabotageBaseReduction, 50f, 500f,
                "TSA_WD_Sab_BaseReducTooltip".Translate(), 5.0f, SliderFormat.Fixed0, WorldDominationSettings.DefSabBaseReduc);

            s.sabotageSkillReductionBonus = SettingsUI.LabeledSlider(l, "TSA_WD_Sab_SkillReduc".Translate(), s.sabotageSkillReductionBonus, 0f, 100f,
                "TSA_WD_Sab_SkillReducTooltip".Translate(), 1.0f, SliderFormat.Fixed0, WorldDominationSettings.DefSabSkillReductionBonus);

            s.sabotageCooldownDays = SettingsUI.LabeledSlider(l, "TSA_WD_Sab_Cooldown".Translate(), s.sabotageCooldownDays, 0.5f, 10f,
                "TSA_WD_Sab_CooldownTooltip".Translate(), 0.5f, SliderFormat.Fixed1, WorldDominationSettings.DefSabCdDays);
            }
            // ================= SECTION 5: REAL-TIME EXAMPLES =================
            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Sab_HeaderSimulation".Translate(), ref simulationExpanded, SettingsUI.SectionHeaderColor))
            {
            l.Gap(2f);

            DrawSimulation(l, "TSA_WD_Sab_SimLvl0".Translate(), 2, s);
            l.Gap(4f);
            DrawSimulation(l, "TSA_WD_Sab_SimLvl10".Translate(), 2, s);
            l.Gap(4f);
            DrawSimulation(l, "TSA_WD_Sab_SimLvl20".Translate(), 2, s);
            }
            l.End();
            Widgets.EndScrollView();
        }

        private void DrawSimulation(Listing_Standard l, string label, int tierLevel, WorldDominationSettings s)
        {
            int skill = label.Contains("10") ? 10 : label.Contains("20") ? 20 : 0;

            float wSuccess = Mathf.Max(1f, s.weightSabSuccess +
                                     (skill * s.sabotageSkillSuccessWeightBonus) -
                                     (tierLevel * s.sabotageTierSuccessWeightPenalty));

            float wClean = s.weightSabCleanFail;
            float wInjured = s.weightSabInjuredFail;
            float wFatal = s.weightSabFatalFail;

            float total = wSuccess + wClean + wInjured + wFatal;

            float pSucc = wSuccess / total;
            float pInitClean = wClean / total;
            float pInitInjured = wInjured / total;
            float pInitFatal = wFatal / total;

            float socialSaveChance = Mathf.Clamp01(skill * s.sabotageSocialCleanBonus);
            float combatSaveChance = Mathf.Clamp01(skill * s.sabotageCombatSurvivalBonus);

            float finalClean = pInitClean + (pInitInjured * socialSaveChance);
            float finalInjured = (pInitInjured * (1f - socialSaveChance)) + (pInitFatal * combatSaveChance);
            float finalFatal = pInitFatal * (1f - combatSaveChance);

            Rect r = l.GetRect(24f);
            GUI.color = new Color(1f, 1f, 1f, 0.1f);
            Widgets.DrawBox(r, 1);
            GUI.color = Color.white;

            // Translated format string
            string info = "TSA_WD_Sab_SimResult".Translate(label.Colorize(Color.cyan), pSucc.ToString("P2"), finalClean.ToString("P2"), finalInjured.ToString("P2"), finalFatal.ToString("P2"));
            Widgets.Label(new Rect(r.x + 5, r.y, r.width - 10, r.height), info);
        }
    }
}