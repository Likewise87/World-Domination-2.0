using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public class Dialog_RaidSettings : Window
    {
        private Vector2 scrollPosition;
        private readonly string windowTitle;
        private bool simulationExpanded = true;
        private bool rangeExpanded;
        private bool arrivalExpanded;
        private bool winChanceExpanded = true;
        private bool severityAttWinExpanded = true;
        private bool severityAttLossExpanded = true;
        private bool severityDefLossExpanded = true;
        private bool severityDefWinExpanded = true;
        private bool lossTablesExpanded = true;

        public override Vector2 InitialSize => new Vector2(900f, 800f);
        public Dialog_RaidSettings()
        {
            doCloseButton = true;
            forcePause = true;
            closeOnClickedOutside = true;
            windowTitle = "TSA_WD_BtnWorldRaids".Translate();
            optionalTitle = null;
        }

        public override void PreClose()
        {
            base.PreClose();
            WorldDominationMod.settings?.InvalidateRaidOutcomesCache();
        }

        private void SetAllSectionsExpanded(bool expanded)
        {
            simulationExpanded = rangeExpanded = arrivalExpanded = expanded;
            winChanceExpanded = lossTablesExpanded = expanded;
            severityAttWinExpanded = severityAttLossExpanded = severityDefWinExpanded = severityDefLossExpanded = expanded;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Rect contentRect = SettingsUI.DrawWindowTitle(inRect, windowTitle);
            float contentWidth = contentRect.width - 24f;
            Rect scrollViewRect = new Rect(0f, 0f, contentWidth, 4500f);

            Widgets.BeginScrollView(contentRect, ref scrollPosition, scrollViewRect);

            Listing_Standard l = new Listing_Standard();
            l.Begin(scrollViewRect);
            var s = WorldDominationMod.settings;
            bool advanced = s.showAdvancedSettings;
            SettingsUI.DrawMenuTopBar(l, SettingsUI.ResetPageToDefaultsLabel, () => s.ResetRaids(),
                () => SetAllSectionsExpanded(true),
                () => SetAllSectionsExpanded(false));

            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Raid_HeaderSim".Translate(), ref simulationExpanded, SettingsUI.SectionHeaderColor))
            {

            s.travelPrepExactPercent = SettingsUI.LabeledSlider(l, "TSA_WD_Raid_TravelPrepExactPct".Translate(), s.travelPrepExactPercent, 0f, 1f,
                "TSA_WD_Raid_TravelPrepExactPctTip".Translate(), 0.05f, SliderFormat.Percent, WorldDominationSettings.DefTravelPrepExactPercent);

            s.coalitionRaidPriorityBias = SettingsUI.LabeledSlider(l, "TSA_WD_Difficulty_CoalitionRaidBias".Translate(), s.coalitionRaidPriorityBias, 0f, 1f,
                "TSA_WD_Difficulty_CoalitionRaidBiasTooltip".Translate(), 0.05f, SliderFormat.Percent, WorldDominationSettings.DefCoalitionRaidPriorityBias);

            float prevAllyRad = s.raidAllyRadius;
            s.raidAllyRadius = SettingsUI.LabeledSlider(l, "TSA_WD_Raid_AllyRadius".Translate(), s.raidAllyRadius, 5f, 200f,
                "TSA_WD_Raid_AllyRadiusTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefRaidAllyRadius);
            if (!Mathf.Approximately(prevAllyRad, s.raidAllyRadius))
                ReinforcementNeighborCache.BumpGeneration();

            s.minRaidRatio = SettingsUI.LabeledSlider(l, "TSA_WD_Raid_MinRatio".Translate(), s.minRaidRatio, 0.5f, 2.0f,
                "TSA_WD_Raid_MinRatioTooltip".Translate(), 0.05f, SliderFormat.Multiplier, WorldDominationSettings.DefMinRaidRatio);

            s.razeChance = SettingsUI.LabeledSlider(l, "TSA_WD_Raid_RazeChance".Translate(), s.razeChance, 0f, 1f,
                "TSA_WD_Raid_RazeChanceTooltip".Translate(), 0.05f, SliderFormat.Percent, WorldDominationSettings.DefRazeChance);
            s.ruinLingerDays = SettingsUI.LabeledSlider(l, "TSA_WD_Raid_RuinLingerDays".Translate(), s.ruinLingerDays, 5f, 10f,
                "TSA_WD_Raid_RuinLingerDaysTooltip".Translate(), 0.5f, SliderFormat.Fixed1, WorldDominationSettings.DefRuinLingerDays);
            }
            l.Gap(4f);

            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Raid_AttackRangeHeader".Translate(), ref rangeExpanded, SettingsUI.SectionHeaderColor))
            {

            s.tier1AttackRangeBaseline = SettingsUI.LabeledSlider(l, "TSA_WD_Raid_T1AttackRange".Translate(), s.tier1AttackRangeBaseline, 5f, 60f,
                "TSA_WD_Raid_T1AttackRangeTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefTier1AttackRangeBaseline);
            s.tier2AttackRangeBaseline = SettingsUI.LabeledSlider(l, "TSA_WD_Raid_T2AttackRange".Translate(), s.tier2AttackRangeBaseline, 5f, 60f,
                "TSA_WD_Raid_T2AttackRangeTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefTier2AttackRangeBaseline);
            s.tier3AttackRangeBaseline = SettingsUI.LabeledSlider(l, "TSA_WD_Raid_T3AttackRange".Translate(), s.tier3AttackRangeBaseline, 5f, 60f,
                "TSA_WD_Raid_T3AttackRangeTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefTier3AttackRangeBaseline);
            s.tier4AttackRangeBaseline = SettingsUI.LabeledSlider(l, "TSA_WD_Raid_T4AttackRange".Translate(), s.tier4AttackRangeBaseline, 5f, 60f,
                "TSA_WD_Raid_T4AttackRangeTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefTier4AttackRangeBaseline);

            s.attackRangeTimeMaxBonusPct = SettingsUI.LabeledSlider(l, "TSA_WD_Raid_AttackRangeTimeBonus".Translate(), s.attackRangeTimeMaxBonusPct, 0f, 4f,
                "TSA_WD_Raid_AttackRangeTimeBonusTooltip".Translate(), 0.05f, SliderFormat.Percent, WorldDominationSettings.DefAttackRangeTimeMaxBonusPct);

            s.attackRangeDaysToMax = SettingsUI.LabeledSlider(l, "TSA_WD_Raid_AttackRangeDaysToMax".Translate(), s.attackRangeDaysToMax, 1f, 300f,
                "TSA_WD_Raid_AttackRangeDaysToMaxTooltip".Translate(), 1f, SliderFormat.Fixed0, WorldDominationSettings.DefAttackRangeDaysToMax);

            l.Gap(4f);

            s.garrisonRetainPct = SettingsUI.LabeledSlider(l, "TSA_WD_Raid_GarrisonRetain".Translate(), s.garrisonRetainPct, 0.05f, 0.75f,
                "TSA_WD_Raid_GarrisonRetainTooltip".Translate(), 0.05f, SliderFormat.Percent, WorldDominationSettings.DefGarrisonRetainPct);
            }
            l.Gap(6f);
            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Raid_HeaderArrivalStyles".Translate(), ref arrivalExpanded, SettingsUI.SectionHeaderColor))
            {
            s.dropPodRaidChanceT3 = SettingsUI.LabeledSlider(l, "TSA_WD_Raid_DropPodChanceT3".Translate(), s.dropPodRaidChanceT3, 0f, 1f,
                "TSA_WD_Raid_DropPodChanceT3Tip".Translate(), 0.05f, SliderFormat.Percent, WorldDominationSettings.DefDropPodRaidChanceT3);
            s.dropPodRaidChance = SettingsUI.LabeledSlider(l, "TSA_WD_Raid_DropPodChance".Translate(), s.dropPodRaidChance, 0f, 1f,
                "TSA_WD_Raid_DropPodChanceTip".Translate(), 0.05f, SliderFormat.Percent, WorldDominationSettings.DefDropPodRaidChance);
            SettingsUI.TechLevelDropdown(l, "TSA_WD_Raid_DropPodMinTech".Translate(), s.dropPodRaidMinTechLevel,
                v => s.dropPodRaidMinTechLevel = v,
                "TSA_WD_Raid_DropPodMinTechTip".Translate(), WorldDominationSettings.DefDropPodRaidMinTechLevel);
            s.dropPodRaidAttritionMult = SettingsUI.LabeledSlider(l, "TSA_WD_Raid_DropPodAttritionMult".Translate(), s.dropPodRaidAttritionMult, 1f, 10f,
                "TSA_WD_Raid_DropPodAttritionMultTip".Translate(), 0.5f, SliderFormat.Fixed1, WorldDominationSettings.DefDropPodRaidAttritionMult);
            s.colonySiegeRaidChance = SettingsUI.LabeledSlider(l, "TSA_WD_Raid_ColonySiegeChance".Translate(), s.colonySiegeRaidChance, 0f, 1f,
                "TSA_WD_Raid_ColonySiegeChanceTip".Translate(), 0.05f, SliderFormat.Percent, WorldDominationSettings.DefColonySiegeRaidChance);
            }

            l.Gap(6f);
            DrawSection1WinChance(l, s, ref winChanceExpanded);

            if (advanced)
            {
                DrawSeveritySection(l, s, "TSA_WD_Raid_Section2_Desc".Translate(), SeverityField.AttOnWin, ref severityAttWinExpanded);
                DrawSeveritySection(l, s, "TSA_WD_Raid_Section3_Desc".Translate(), SeverityField.AttOnLoss, ref severityAttLossExpanded);
                DrawSeveritySection(l, s, "TSA_WD_Raid_Section4_Desc".Translate(), SeverityField.DefOnWin, ref severityDefWinExpanded);
                DrawSeveritySection(l, s, "TSA_WD_Raid_Section5_Desc".Translate(), SeverityField.DefOnLoss, ref severityDefLossExpanded);
            }

            DrawSection6LossTables(l, s, ref lossTablesExpanded);

            l.End();
            Widgets.EndScrollView();
        }

        private static void DrawSection1WinChance(Listing_Standard l, WorldDominationSettings s, ref bool expanded)
        {
            if (!SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Raid_Section1_WinChance".Translate(), ref expanded, SettingsUI.SectionHeaderColor))
                return;
            DrawWinChanceHeaders(l.GetRect(24f));
            foreach (var outcome in s.raidOutcomes)
            {
                DrawWinChanceRow(l.GetRect(32f), outcome);
                l.Gap(2f);
            }
        }

        private static void DrawWinChanceHeaders(Rect r)
        {
            float w = r.width / 2f;
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(r.x, r.y, w - 10, r.height), "TSA_WD_Raid_Col0".Translate());
            Widgets.Label(new Rect(r.x + w, r.y, w - 10, r.height), "TSA_WD_Raid_Col1".Translate());
        }

        private static void DrawWinChanceRow(Rect r, RaidOutcome o)
        {
            float w = r.width / 2f;
            Rect threshRect = new Rect(r.x, r.y, w - 10, r.height);
            Widgets.DrawBoxSolidWithOutline(threshRect, new Color(1f, 1f, 1f, 0.05f), new Color(1f, 1f, 1f, 0.1f));
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(threshRect, (o.threshold.ToString("F2") + "x").Colorize(Color.yellow));
            Text.Anchor = TextAnchor.UpperLeft;

            string winStr = ((o.winChance * 100).ToString("F0") + "%").Colorize(Color.cyan);
            o.winChance = Widgets.HorizontalSlider(new Rect(r.x + w, r.y, w - 10, r.height), o.winChance, 0f, 1.0f, false, winStr);
        }

        private enum SeverityField { AttOnWin, AttOnLoss, DefOnWin, DefOnLoss }

        private static RaidMarginShares GetSeverityShares(RaidOutcome o, SeverityField field)
        {
            switch (field)
            {
                case SeverityField.AttOnLoss: return o.attSeverityOnAttLoss;
                case SeverityField.DefOnWin: return o.defCoalitionOnAttWin;
                case SeverityField.DefOnLoss: return o.defCoalitionOnAttLoss;
                default: return o.attSeverityOnAttWin;
            }
        }

        private static void SetSeverityShares(RaidOutcome o, SeverityField field, RaidMarginShares shares)
        {
            switch (field)
            {
                case SeverityField.AttOnLoss: o.attSeverityOnAttLoss = shares; break;
                case SeverityField.DefOnWin: o.defCoalitionOnAttWin = shares; break;
                case SeverityField.DefOnLoss: o.defCoalitionOnAttLoss = shares; break;
                default: o.attSeverityOnAttWin = shares; break;
            }
        }

        private static RaidMarginShares DefaultSeverityAt(float threshold, SeverityField field)
        {
            switch (field)
            {
                case SeverityField.AttOnLoss: return RaidSeverityDefaults.AttSeverityOnAttLossAt(threshold);
                case SeverityField.DefOnWin: return RaidSeverityDefaults.DefCoalitionOnAttWinAt(threshold);
                case SeverityField.DefOnLoss: return RaidSeverityDefaults.DefCoalitionOnAttLossAt(threshold);
                default: return RaidSeverityDefaults.AttSeverityOnAttWinAt(threshold);
            }
        }

        private static void DrawSeveritySection(Listing_Standard l, WorldDominationSettings s, string description, SeverityField field, ref bool expanded)
        {
            if (!SettingsUI.DrawCollapsibleHeader(l, description, ref expanded, SettingsUI.SectionHeaderColor))
                return;
            DrawSeverityHeaders(l.GetRect(24f), field);
            foreach (var outcome in s.raidOutcomes)
            {
                RaidMarginShares shares = GetSeverityShares(outcome, field);
                if (shares == null)
                {
                    shares = DefaultSeverityAt(outcome.threshold, field);
                    SetSeverityShares(outcome, field, shares);
                }
                DrawSeverityRow(l.GetRect(32f), outcome.threshold, shares);
                l.Gap(2f);
            }
        }

        private static void DrawSeverityHeaders(Rect r, SeverityField field)
        {
            float w = r.width / 4f;
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(r.x, r.y, w - 10, r.height), "TSA_WD_Raid_Col0".Translate());
            Widgets.Label(new Rect(r.x + w, r.y, w - 10, r.height), MarginHeaderKey(field, "Close").Translate());
            Widgets.Label(new Rect(r.x + w * 2, r.y, w - 10, r.height), MarginHeaderKey(field, "Normal").Translate());
            Widgets.Label(new Rect(r.x + w * 3, r.y, w - 10, r.height), MarginHeaderKey(field, "Decisive").Translate());
        }

        private static string MarginHeaderKey(SeverityField field, string margin)
        {
            bool win = field == SeverityField.AttOnWin || field == SeverityField.DefOnLoss;
            bool attacker = field == SeverityField.AttOnWin || field == SeverityField.AttOnLoss;
            string who = attacker ? "Attacker" : "Defender";
            string outcome = win ? "Win" : "Loss";
            return "TSA_WD_Raid_" + margin + who + outcome;
        }

        private static void DrawSeverityRow(Rect r, float threshold, RaidMarginShares shares)
        {
            float w = r.width / 4f;
            Rect threshRect = new Rect(r.x, r.y, w - 10, r.height);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(threshRect, (threshold.ToString("F2") + "x").Colorize(Color.yellow));
            Text.Anchor = TextAnchor.UpperLeft;

            shares.close = DrawShareSlider(new Rect(r.x + w, r.y, w - 10, r.height), shares.close);
            shares.normal = DrawShareSlider(new Rect(r.x + w * 2, r.y, w - 10, r.height), shares.normal);
            shares.decisive = DrawShareSlider(new Rect(r.x + w * 3, r.y, w - 10, r.height), shares.decisive);
            shares.Normalize();
        }

        private static float DrawShareSlider(Rect rect, float value)
        {
            string label = (value * 100f).ToString("F0") + "%";
            return Widgets.HorizontalSlider(rect, value, 0f, 1f, false, label.Colorize(Color.cyan));
        }

        private static readonly string[] LossRowKeys =
        {
            "TSA_WD_Raid_LossRow_Close",
            "TSA_WD_Raid_LossRow_Normal",
            "TSA_WD_Raid_LossRow_Decisive"
        };

        private static readonly string[] LossColumnKeys =
        {
            "TSA_WD_Raid_Section6_AttWin",
            "TSA_WD_Raid_Section6_AttLoss",
            "TSA_WD_Raid_Section6_DefWin",
            "TSA_WD_Raid_Section6_DefLoss"
        };

        private static void DrawSection6LossTables(Listing_Standard l, WorldDominationSettings s, ref bool expanded)
        {
            s.EnsureRaidLossTablesInitialized();
            if (!SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Raid_Section6_LossTables".Translate(), ref expanded, SettingsUI.SectionHeaderColor))
                return;
            l.Label("TSA_WD_Raid_Section6_Tip".Translate());
            l.Gap(4f);
            DrawUnifiedLossTable(l, s);
        }

        private static void DrawUnifiedLossTable(Listing_Standard l, WorldDominationSettings s)
        {
            List<RaidSideLossEntry>[] columns =
            {
                s.raidAttLossOnWin,
                s.raidAttLossOnLoss,
                s.raidDefLossOnWin,
                s.raidDefLossOnLoss
            };

            const float rowH = 36f;
            const float headerH = 24f;
            const float labelColFrac = 0.16f;
            const float colGap = 6f;

            Rect header = l.GetRect(headerH);
            float labelW = header.width * labelColFrac;
            float sliderAreaW = header.width - labelW;
            float colW = (sliderAreaW - colGap * (LossColumnKeys.Length - 1)) / LossColumnKeys.Length;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            for (int c = 0; c < LossColumnKeys.Length; c++)
            {
                Rect colRect = new Rect(header.x + labelW + c * (colW + colGap), header.y, colW, headerH);
                Widgets.Label(colRect, LossColumnKeys[c].Translate());
            }
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            l.Gap(2f);

            for (int r = 0; r < LossRowKeys.Length; r++)
            {
                Rect row = l.GetRect(rowH);
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(row.x, row.y, labelW - 4f, rowH), LossRowKeys[r].Translate());
                Text.Anchor = TextAnchor.UpperLeft;

                for (int c = 0; c < columns.Length; c++)
                {
                    List<RaidSideLossEntry> table = columns[c];
                    while (table.Count <= r)
                        table.Add(new RaidSideLossEntry());
                    RaidSideLossEntry entry = table[r] ?? (table[r] = new RaidSideLossEntry());

                    Rect cell = new Rect(row.x + labelW + c * (colW + colGap), row.y, colW, rowH);
                    string lossStr = ("-" + (entry.lossPct * 100f).ToString("F0") + "%").Colorize(Color.cyan);
                    entry.lossPct = Widgets.HorizontalSlider(cell, entry.lossPct, 0f, 1f, false, lossStr);
                }
                l.Gap(2f);
            }
        }
    }
}
