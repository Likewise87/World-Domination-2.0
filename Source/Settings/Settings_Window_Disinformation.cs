using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public class Dialog_DisinformationSettings : Window
    {
        private Vector2 scrollPosition;
        private readonly string windowTitle;
        private bool weightsExpanded = true;
        private bool modifiersExpanded;
        private bool savesExpanded;
        private bool outcomeExpanded;
        private bool simulationExpanded;

        public override Vector2 InitialSize => new Vector2(850f, 750f);
        public Dialog_DisinformationSettings()
        {
            doCloseButton = true;
            forcePause = true;
            closeOnClickedOutside = true;
            windowTitle = "TSA_WD_BtnDisinformation".Translate();
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
            SettingsUI.DrawMenuTopBar(l, SettingsUI.ResetPageToDefaultsLabel, () => s.ResetDisinformation(),
                () => { weightsExpanded = modifiersExpanded = savesExpanded = outcomeExpanded = simulationExpanded = true; },
                () => { weightsExpanded = modifiersExpanded = savesExpanded = outcomeExpanded = simulationExpanded = false; });

            // ================= SECTION 1: BASE WEIGHTS =================
            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Dis_HeaderWeights".Translate(), ref weightsExpanded, SettingsUI.SectionHeaderColor))
            {
            float total = s.TotalDisWeight;

            s.weightDisSuccess = SettingsUI.WeightSlider(l, "TSA_WD_Dis_WeightSuccess".Translate(), s.weightDisSuccess, total, 0f, 200f,
                "TSA_WD_Dis_WeightSuccessTooltip".Translate(), WorldDominationSettings.DefWeightDisSuccess);

            s.weightDisCleanFail = SettingsUI.WeightSlider(l, "TSA_WD_Dis_WeightClean".Translate(), s.weightDisCleanFail, total, 0f, 200f,
                "TSA_WD_Dis_WeightCleanTooltip".Translate(), WorldDominationSettings.DefWeightDisCleanFail);

            s.weightDisInjuredFail = SettingsUI.WeightSlider(l, "TSA_WD_Dis_WeightInjured".Translate(), s.weightDisInjuredFail, total, 0f, 200f,
                "TSA_WD_Dis_WeightInjuredTooltip".Translate(), WorldDominationSettings.DefWeightDisInjuredFail);

            s.weightDisFatalFail = SettingsUI.WeightSlider(l, "TSA_WD_Dis_WeightFatal".Translate(), s.weightDisFatalFail, total, 0f, 200f,
                "TSA_WD_Dis_WeightFatalTooltip".Translate(), WorldDominationSettings.DefWeightDisFatalFail);
            }
            // ================= SECTION 2: SUCCESS =================
            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Dis_HeaderModifiers".Translate(), ref modifiersExpanded, SettingsUI.SectionHeaderColor))
            {

            s.disSkillSuccessWeightBonus = SettingsUI.LabeledSlider(l, "TSA_WD_Dis_SkillBonus".Translate(), s.disSkillSuccessWeightBonus, 0f, 20f,
                "TSA_WD_Dis_SkillBonusTooltip".Translate(), 0.5f, SliderFormat.Fixed1, WorldDominationSettings.DefDisSkillSuccessWeightBonus);

            s.disTierSuccessWeightPenalty = SettingsUI.LabeledSlider(l, "TSA_WD_Dis_TierPenalty".Translate(), s.disTierSuccessWeightPenalty, 0f, 40f,
                "TSA_WD_Dis_TierPenaltyTooltip".Translate(), 1.0f, SliderFormat.Fixed0, WorldDominationSettings.DefDisTierSuccessWeightPenalty);

            s.disHealthImpactWeight = SettingsUI.LabeledSlider(l, "TSA_WD_Dis_HealthImpact".Translate(), s.disHealthImpactWeight, 0f, 1f,
                "TSA_WD_Dis_HealthImpactTooltip".Translate(), 0.05f, SliderFormat.Percent, WorldDominationSettings.DefDisHealthImpactWeight);
            }
            // ================= SECTION 3: SAVING THROWS =================
            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Dis_HeaderSaves".Translate(), ref savesExpanded, SettingsUI.SectionHeaderColor))
            {

            s.disSocialCleanBonus = SettingsUI.LabeledSlider(l, "TSA_WD_Dis_SocialSave".Translate(), s.disSocialCleanBonus, 0f, 0.10f,
                "TSA_WD_Dis_SocialSaveTooltip".Translate(), 0.002f, SliderFormat.PercentDecimal, WorldDominationSettings.DefDisSocialCleanBonus);

            s.disCombatSurvivalBonus = SettingsUI.LabeledSlider(l, "TSA_WD_Dis_CombatSave".Translate(), s.disCombatSurvivalBonus, 0f, 0.10f,
                "TSA_WD_Dis_CombatSaveTooltip".Translate(), 0.002f, SliderFormat.PercentDecimal, WorldDominationSettings.DefDisCombatSurvivalBonus);
            }
            // ================= SECTION 4: MISC =================
            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Dis_HeaderOutcome".Translate(), ref outcomeExpanded, SettingsUI.SectionHeaderColor))
            {

            s.disBaseReduction = SettingsUI.LabeledSlider(l, "TSA_WD_Dis_BaseReduc".Translate(), s.disBaseReduction, 50f, 500f,
                "TSA_WD_Dis_BaseReducTooltip".Translate(), 5.0f, SliderFormat.Fixed0, WorldDominationSettings.DefDisBaseReduc);

            s.disSkillReductionBonus = SettingsUI.LabeledSlider(l, "TSA_WD_Dis_SkillReduc".Translate(), s.disSkillReductionBonus, 0f, 100f,
                "TSA_WD_Dis_SkillReducTooltip".Translate(), 1.0f, SliderFormat.Fixed0, WorldDominationSettings.DefDisSkillReductionBonus);

            s.disCooldownDays = SettingsUI.LabeledSlider(l, "TSA_WD_Dis_Cooldown".Translate(), s.disCooldownDays, 0.5f, 10f,
                "TSA_WD_Dis_CooldownTooltip".Translate(), 0.5f, SliderFormat.Fixed1, WorldDominationSettings.DefDisCdDays);
            }
            // ================= SECTION 5: REAL-TIME EXAMPLES =================
            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Dis_HeaderSimulation".Translate(), ref simulationExpanded, SettingsUI.SectionHeaderColor))
            {
            l.Gap(2f);

            DrawSimulation(l, "TSA_WD_Dis_SimLvl0".Translate(), 2, s);
            l.Gap(4f);
            DrawSimulation(l, "TSA_WD_Dis_SimLvl10".Translate(), 2, s);
            l.Gap(4f);
            DrawSimulation(l, "TSA_WD_Dis_SimLvl20".Translate(), 2, s);
            }
            l.End();
            Widgets.EndScrollView();
        }

        private void DrawSimulation(Listing_Standard l, string label, int tier, WorldDominationSettings s)
        {
            int lvl = label.Contains("10") ? 10 : label.Contains("20") ? 20 : 0;

            float wSucc = Mathf.Max(1f, s.weightDisSuccess + (lvl * s.disSkillSuccessWeightBonus) - (tier * s.disTierSuccessWeightPenalty));
            float wClean = s.weightDisCleanFail;
            float wInjured = s.weightDisInjuredFail;
            float wFatal = s.weightDisFatalFail;

            float total = wSucc + wClean + wInjured + wFatal;

            float pSucc = wSucc / total;
            float pInitClean = wClean / total;
            float pInitInjured = wInjured / total;
            float pInitFatal = wFatal / total;

            float socialSaveChance = Mathf.Clamp01(lvl * s.disSocialCleanBonus);
            float combatSaveChance = Mathf.Clamp01(lvl * s.disCombatSurvivalBonus);

            float finalClean = pInitClean + (pInitInjured * socialSaveChance);
            float finalInjured = (pInitInjured * (1f - socialSaveChance)) + (pInitFatal * combatSaveChance);
            float finalFatal = pInitFatal * (1f - combatSaveChance);

            Rect r = l.GetRect(24f);
            GUI.color = new Color(1f, 1f, 1f, 0.1f);
            Widgets.DrawBox(r, 1);
            GUI.color = Color.white;

            string info = "TSA_WD_Dis_SimResult".Translate(label.Colorize(Color.yellow), pSucc.ToString("P2"), finalClean.ToString("P2"), finalInjured.ToString("P2"), finalFatal.ToString("P2"));
            Widgets.Label(new Rect(r.x + 5, r.y, r.width - 10, r.height), info);
        }
    }
}