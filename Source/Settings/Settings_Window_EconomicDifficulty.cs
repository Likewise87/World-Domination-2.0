using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public class Dialog_EconomicDifficultySettings : Window
    {
        private const float BandEndMin = 1f;
        private const float BandEndMax = 500f;
        private const float BandWeightMinPct = 10f;
        private const float BandWeightMaxPct = 100f;

        private Vector2 scrollPosition;
        private readonly string windowTitle;
        private bool upkeepExpanded = true;
        private bool skillExpanded = true;
        private bool goodwillExpanded = true;
        private bool bribeExpanded = true;
        private float previewRaw = 100f;
        private bool bandsSanitized;

        public override Vector2 InitialSize => new Vector2(850f, 750f);

        public Dialog_EconomicDifficultySettings()
        {
            doCloseButton = true;
            forcePause = true;
            closeOnClickedOutside = true;
            windowTitle = "TSA_WD_BtnEconomicDifficulty".Translate();
            optionalTitle = null;
        }

        private void SetAllExpanded(bool expanded)
        {
            upkeepExpanded = skillExpanded = goodwillExpanded = bribeExpanded = expanded;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Rect contentRect = SettingsUI.DrawWindowTitle(inRect, windowTitle);
            float contentWidth = contentRect.width - 24f;
            Rect scrollViewRect = new Rect(0f, 0f, contentWidth, 2800f);

            Widgets.BeginScrollView(contentRect, ref scrollPosition, scrollViewRect);

            Listing_Standard l = new Listing_Standard();
            l.Begin(scrollViewRect);
            var s = WorldDominationMod.settings;
            OutpostSkillScaling.EnsureArrays(s);
            if (!bandsSanitized)
            {
                OutpostSkillScaling.NormalizeBands(s);
                bandsSanitized = true;
            }

            SettingsUI.DrawMenuTopBar(l, SettingsUI.ResetPageToDefaultsLabel, () =>
            {
                s.ResetEconomicDifficulty();
                previewRaw = 100f;
            },
                () => SetAllExpanded(true),
                () => SetAllExpanded(false));
            SettingsUI.DrawSettingsSearchBar(l);

            DrawUpkeep(l, s);
            DrawSkillScaling(l, s);
            DrawGoodwillDrain(l, s);
            DrawBribeRates(l, s);

            l.End();
            Widgets.EndScrollView();
        }

        private void DrawUpkeep(Listing_Standard l, WorldDominationSettings s)
        {
            if (!SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Experimental_HeaderUpkeep".Translate(), ref upkeepExpanded, SettingsUI.SectionHeaderColor))
                return;

            SettingsUI.DrawCheckbox(l, "TSA_WD_Upkeep_Enable".Translate(),
                ref s.enableOutpostUpkeep,
                "TSA_WD_Upkeep_EnableTip".Translate(),
                defaultValue: WorldDominationSettings.DefEnableOutpostUpkeep);
            if (s.enableOutpostUpkeep)
            {
                s.upkeepSilverPerOccupant = (int)SettingsUI.LabeledSlider(l,
                    "TSA_WD_Upkeep_SilverPerOccupant".Translate(),
                    s.upkeepSilverPerOccupant, 1f, 200f,
                    "TSA_WD_Upkeep_SilverPerOccupantTip".Translate(),
                    1f, SliderFormat.Fixed0, WorldDominationSettings.DefUpkeepSilverPerOccupant);
                s.upkeepIntervalDays = (int)SettingsUI.LabeledSlider(l,
                    "TSA_WD_Upkeep_IntervalDays".Translate(),
                    s.upkeepIntervalDays, 1f, 60f,
                    "TSA_WD_Upkeep_IntervalDaysTip".Translate(),
                    1f, SliderFormat.Fixed0, WorldDominationSettings.DefUpkeepIntervalDays);
            }
        }

        private void DrawSkillScaling(Listing_Standard l, WorldDominationSettings s)
        {
            if (!SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_BtnOutpostSkillScaling".Translate(), ref skillExpanded, SettingsUI.SectionHeaderColor,
                "TSA_WD_DescOutpostSkillScaling".Translate()))
                return;

            l.Label("TSA_WD_SkillScaling_Intro".Translate());
            l.Gap(6f);

            SettingsUI.DrawCheckbox(l, "TSA_WD_SkillScaling_Enable".Translate(), ref s.enableOutpostSkillDiminishingReturns,
                "TSA_WD_SkillScaling_EnableTip".Translate(), defaultValue: OutpostSkillScaling.DefEnableDiminishingReturns);

            float hardCapIn = Mathf.Round(s.outpostSkillHardCapRaw);
            float hardCapOut = SettingsUI.LabeledSlider(l, "TSA_WD_SkillScaling_HardCap".Translate(),
                hardCapIn, 60f, 500f, "TSA_WD_SkillScaling_HardCapTip".Translate(), 1f, SliderFormat.Fixed0,
                OutpostSkillScaling.DefHardCapRaw);
            bool bandsDirty = !Mathf.Approximately(hardCapOut, hardCapIn);
            if (bandsDirty)
                s.outpostSkillHardCapRaw = hardCapOut;

            SettingsUI.DrawHeader(l, "TSA_WD_SkillScaling_BandsHeader".Translate(), SettingsUI.SectionHeaderColor);
            for (int i = 0; i < OutpostSkillScaling.BandCount; i++)
            {
                float start = i == 0 ? 0f : Mathf.Round(s.outpostSkillBandEnds[i - 1]) + 1f;
                float endIn = Mathf.Clamp(Mathf.Round(s.outpostSkillBandEnds[i]), BandEndMin, BandEndMax);
                float wIn = Mathf.Clamp(Mathf.Round(s.outpostSkillBandWeights[i] * 100f), BandWeightMinPct, BandWeightMaxPct);

                string rangeLabel = "TSA_WD_SkillScaling_BandRange".Translate(
                    start.ToString("F0").Colorize(Color.cyan),
                    endIn.ToString("F0").Colorize(Color.cyan));
                string effLabel = "TSA_WD_SkillScaling_BandEfficiency".Translate(
                    wIn.ToString("F0").Colorize(Color.cyan));

                l.Gap(4f);
                Rect row = l.GetRect(52f);
                float gap = 12f;
                float colW = (row.width - gap) * 0.5f;
                Rect left = new Rect(row.x, row.y, colW, row.height);
                Rect right = new Rect(row.x + colW + gap, row.y, colW, row.height);

                TooltipHandler.TipRegion(left, SettingsUI.TooltipWithDefault(
                    "TSA_WD_SkillScaling_BandEndTip".Translate(),
                    OutpostSkillScaling.DefBandEnds[i], SliderFormat.Fixed0));
                TooltipHandler.TipRegion(right, SettingsUI.TooltipWithDefault(
                    "TSA_WD_SkillScaling_BandWeightTip".Translate(),
                    OutpostSkillScaling.DefBandWeights[i] * 100f, SliderFormat.Fixed0));

                Text.Font = GameFont.Small;
                Widgets.Label(left.TopPartPixels(24f), rangeLabel);
                Widgets.Label(right.TopPartPixels(24f), effLabel);

                float endOut = Widgets.HorizontalSlider(left.BottomPartPixels(22f), endIn, BandEndMin, BandEndMax, false, null, null, null, 1f);
                float wOut = Widgets.HorizontalSlider(right.BottomPartPixels(22f), wIn, BandWeightMinPct, BandWeightMaxPct, false, null, null, null, 1f);

                if (!Mathf.Approximately(endOut, endIn))
                {
                    s.outpostSkillBandEnds[i] = endOut;
                    bandsDirty = true;
                }
                if (!Mathf.Approximately(wOut, wIn))
                {
                    s.outpostSkillBandWeights[i] = wOut / 100f;
                    bandsDirty = true;
                }
            }
            if (bandsDirty)
                OutpostSkillScaling.NormalizeBands(s);

            SettingsUI.DrawHeader(l, "TSA_WD_SkillScaling_PreviewHeader".Translate(), SettingsUI.SectionHeaderColor);
            float previewIn = Mathf.Round(previewRaw);
            float previewOut = SettingsUI.LabeledSlider(l, "TSA_WD_SkillScaling_PreviewRaw".Translate(),
                previewIn, 0f, 400f, tooltip: null, 1f, SliderFormat.Fixed0, 100f);
            if (!Mathf.Approximately(previewOut, previewIn))
                previewRaw = previewOut;
            float eff = OutpostSkillScaling.ToEffective(previewRaw);
            Text.Font = GameFont.Small;
            l.Label("TSA_WD_SkillScaling_PreviewResult".Translate(eff.ToString("F0")));
            l.Gap(4f);
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            l.Label(OutpostSkillScaling.BuildBandBreakdownTip(previewRaw));
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private void DrawGoodwillDrain(Listing_Standard l, WorldDominationSettings s)
        {
            if (!SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Combat_HeaderGoodwillDrain".Translate(), ref goodwillExpanded, SettingsUI.SectionHeaderColor))
                return;

            l.CheckboxLabeled("TSA_WD_Difficulty_EnableGoodwillDrain".Translate(), ref s.enableGoodwillDrain,
                SettingsUI.TooltipWithDefault("TSA_WD_Difficulty_EnableGoodwillDrainTooltip".Translate(), WorldDominationSettings.DefEnableGoodwillDrain));

            if (!s.enableGoodwillDrain)
                return;

            s.goodwillDrainIntervalDays = Mathf.RoundToInt(SettingsUI.LabeledSlider(l, "TSA_WD_Difficulty_GoodwillDrainInterval".Translate(), s.goodwillDrainIntervalDays, 1f, 60f,
                "TSA_WD_Difficulty_GoodwillDrainIntervalTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefGoodwillDrainIntervalDays));
            s.midGameGoodwillDrainAmount = Mathf.RoundToInt(SettingsUI.LabeledSlider(l, "TSA_WD_MidGame_GoodwillDrainAmount".Translate(), s.midGameGoodwillDrainAmount, 0f, 50f,
                "TSA_WD_MidGame_GoodwillDrainAmountTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefMidGameGoodwillDrainAmount));
            s.lateGameGoodwillDrainAmount = Mathf.RoundToInt(SettingsUI.LabeledSlider(l, "TSA_WD_LateGame_GoodwillDrainAmount".Translate(), s.lateGameGoodwillDrainAmount, 0f, 50f,
                "TSA_WD_LateGame_GoodwillDrainAmountTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefLateGameGoodwillDrainAmount));
        }

        private void DrawBribeRates(Listing_Standard l, WorldDominationSettings s)
        {
            if (!SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Bribe_CostHeader".Translate(), ref bribeExpanded, SettingsUI.SectionHeaderColor))
                return;

            s.bribeSettlementSilverPerStrength = SettingsUI.LabeledSlider(l, "TSA_WD_Bribe_SettlementSilverPerStr".Translate(), s.bribeSettlementSilverPerStrength, 0.5f, 10f,
                "TSA_WD_Bribe_SettlementSilverPerStrTip".Translate(), 0.1f, SliderFormat.Fixed1, WorldDominationSettings.DefBribeSettlementSilverPerStrength);
            s.bribeCaravanSilverPerStrengthEarly = SettingsUI.LabeledSlider(l, "TSA_WD_Bribe_CaravanSilverPerStrEarly".Translate(), s.bribeCaravanSilverPerStrengthEarly, 0.5f, 10f,
                "TSA_WD_Bribe_CaravanSilverPerStrEarlyTip".Translate(), 0.1f, SliderFormat.Fixed1, WorldDominationSettings.DefBribeCaravanSilverPerStrengthEarly);
            s.bribeCaravanSilverPerStrengthMid = SettingsUI.LabeledSlider(l, "TSA_WD_Bribe_CaravanSilverPerStrMid".Translate(), s.bribeCaravanSilverPerStrengthMid, 0.5f, 10f,
                "TSA_WD_Bribe_CaravanSilverPerStrMidTip".Translate(), 0.1f, SliderFormat.Fixed1, WorldDominationSettings.DefBribeCaravanSilverPerStrengthMid);
            s.bribeCaravanSilverPerStrengthLate = SettingsUI.LabeledSlider(l, "TSA_WD_Bribe_CaravanSilverPerStrLate".Translate(), s.bribeCaravanSilverPerStrengthLate, 0.5f, 10f,
                "TSA_WD_Bribe_CaravanSilverPerStrLateTip".Translate(), 0.1f, SliderFormat.Fixed1, WorldDominationSettings.DefBribeCaravanSilverPerStrengthLate);
        }
    }
}
