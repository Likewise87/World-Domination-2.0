using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;

namespace TSA_WorldDomination
{
    public class Window_RaidResolutionDetails : Window
    {
        private SpreadLogEntry entry;
        private Vector2 scrollAtk;
        private Vector2 scrollDef;
        public override Vector2 InitialSize => new Vector2(700f, 720f);

        public Window_RaidResolutionDetails(SpreadLogEntry entry)
        {
            this.entry = entry;
            this.doCloseX = true;
            this.draggable = true;
            this.absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            Text.Font = GameFont.Medium;
            listing.Label("TSA_WD_RaidResolution_Header".Translate() + ": " + entry.labelB);
            Text.Font = GameFont.Small;
            listing.GapLine();

            if (entry.pathTravelTicks >= 0f)
            {
                listing.Label("TSA_WD_TimeToDestination".Translate() + ": " + (entry.pathTravelTicks / 60000f).ToString("F1") + " " + "TSA_WD_Days".Translate());
                listing.Gap(6f);
            }

            float effectiveAtk = entry.attStr * entry.efficiencyFactor;
            Rect powers = listing.GetRect(70f);
            RaidUIUtils.DrawRaidPowerBoxes(powers, effectiveAtk, entry.defStr, "TSA_WD_Attackers", "TSA_WD_Defender");

            listing.Gap(12f);

            Rect mathRect = listing.GetRect(50f);
            Rect leftCol = mathRect.LeftHalf();
            Rect rightCol = mathRect.RightHalf();

            Widgets.Label(leftCol.TopHalf(), "TSA_WD_StrengthAtDeparture".Translate() + ": " + entry.attStr.ToString("F0"));
            Widgets.Label(leftCol.BottomHalf(), "TSA_WD_ResultingEfficiencyFactor".Translate() + ": " + entry.efficiencyFactor.ToStringPercent());

            string arrivalMath = $"{entry.attStr:F0} x {entry.efficiencyFactor.ToStringPercent()} = {effectiveAtk:F0}";
            Widgets.Label(rightCol.TopHalf(), "TSA_WD_StrengthAtArrival".Translate() + ": " + arrivalMath);
            Widgets.Label(rightCol.BottomHalf(), "TSA_WD_WinChance".Translate() + ": " + entry.winChance.ToStringPercent());

            listing.Gap(12f);
            PollutionRaidUi.DrawBanners(listing, entry.pollutionDamageExpected, entry.pollutionRouteAltered);

            listing.Gap(12f);

            float displayWin = Mathf.Clamp01(entry.winChance);
            Rect winBar = listing.GetRect(22f);
            RaidUIUtils.DrawWinChanceBar(winBar, displayWin);

            listing.Gap(4f);
            bool attWon = entry.victory;
            bool defenderPerspective = RaidUIUtils.IsPlayerOutpostDefense(entry);
            bool headlineIsWin = defenderPerspective ? !attWon : attWon;
            Color headlineColor = headlineIsWin ? Color.green : ColorLibrary.RedReadable;
            Text.Font = GameFont.Medium;
            listing.Label(RaidUIUtils.FormatRaidOutcomeHeadline(entry).Colorize(headlineColor));
            Text.Font = GameFont.Small;
            listing.Gap(6f);

            BattleMarginTier attTier = RaidUIUtils.GetAttSeverityTier(entry);
            BattleMarginTier defTier = entry.defCoalitionSeverityTier;
            Color attColor = RaidUIUtils.GetMarginTierColor(attTier, attWon);
            Color defColor = RaidUIUtils.GetMarginTierColor(defTier, !attWon);

            if (defenderPerspective)
            {
                listing.Label(RaidUIUtils.FormatResolutionMarginLine(defTier, false, !attWon, entry.defLossPct, useVictoryLabel: true).Colorize(defColor));
                listing.Label(RaidUIUtils.FormatResolutionMarginLine(attTier, true, attWon, entry.attLossPct).Colorize(attColor));
            }
            else
            {
                listing.Label(RaidUIUtils.FormatResolutionMarginLine(attTier, true, attWon, entry.attLossPct, useVictoryLabel: true).Colorize(attColor));
                listing.Label(RaidUIUtils.FormatResolutionMarginLine(defTier, false, !attWon, entry.defLossPct).Colorize(defColor));
            }

            listing.Gap(15f);

            listing.Label("TSA_WD_ReinforcementBreakdown".Translate());
            Rect breakdownRect = listing.GetRect(220f);

            float survivalPct = 1.0f - entry.attLossPct;
            List<RaidForceRow> displayAtkRows = BuildResolutionAttackerRows(entry, survivalPct);
            List<string> displayAtkLines = new List<string>();
            if (displayAtkRows.Count == 0)
            {
                displayAtkLines.Add($"<b>{"TSA_WD_VictorySurvival".Translate()}: {survivalPct:P0}</b>");
                if (entry.contributionDNAKeys != null)
                {
                    for (int i = 0; i < entry.contributionDNAKeys.Count; i++)
                    {
                        string key = entry.contributionDNAKeys[i];
                        float val = entry.contributionDNAValues[i];
                        float refunded = (entry.attStr * entry.efficiencyFactor * survivalPct) * val;
                        if (refunded > 0.01f)
                            displayAtkLines.Add($"{key}: +{refunded:F0} ({"TSA_WD_StrengthRefunded".Translate()})".Colorize(Color.cyan));
                    }
                }
                if (displayAtkLines.Count == 1 && survivalPct <= 0.01f)
                {
                    displayAtkLines.Clear();
                    displayAtkLines.Add("TSA_WD_ExpeditionForce".Translate() + ": " + "TSA_WD_ForceNeutralized".Translate().Colorize(ColorLibrary.RedReadable));
                }
            }

            RaidUIUtils.DrawRaidForceBreakdownScrolls(
                breakdownRect,
                displayAtkRows,
                RaidForceLogRow.ToDisplayRows(entry.defForceRows),
                displayAtkLines,
                entry.defDetails,
                ref scrollAtk,
                ref scrollDef);

            listing.Gap(15f);
            if (Widgets.ButtonText(listing.GetRect(30f), "Close".Translate())) Close();
            listing.End();
        }

        /// <summary>Builds icon rows with compact +refunded strength; tip keeps survival / refunded detail.</summary>
        private static List<RaidForceRow> BuildResolutionAttackerRows(SpreadLogEntry entry, float survivalPct)
        {
            var rows = new List<RaidForceRow>();
            if (entry?.attForceRows == null || entry.attForceRows.Count == 0) return rows;

            if (survivalPct <= 0.01f)
            {
                rows.Add(new RaidForceRow
                {
                    Label = "TSA_WD_ExpeditionForce".Translate(),
                    Faction = null,
                    Committed = 0f,
                    DisplayStrength = 0f,
                    Included = true,
                    Tooltip = "TSA_WD_ForceNeutralized".Translate(),
                });
                return rows;
            }

            for (int i = 0; i < entry.attForceRows.Count; i++)
            {
                RaidForceLogRow lr = entry.attForceRows[i];
                if (lr == null) continue;
                float share = 0f;
                if (entry.contributionDNAKeys != null)
                {
                    for (int d = 0; d < entry.contributionDNAKeys.Count; d++)
                    {
                        if (entry.contributionDNAKeys[d] == lr.label)
                        {
                            share = entry.contributionDNAValues[d];
                            break;
                        }
                    }
                }
                float refunded = (entry.attStr * entry.efficiencyFactor * survivalPct) * share;
                if (refunded <= 0.01f && share <= 0f)
                    refunded = lr.committed * entry.efficiencyFactor * survivalPct;

                string tip = lr.tooltip ?? "";
                if (!tip.NullOrEmpty()) tip += "\n";
                tip += "TSA_WD_VictorySurvival".Translate() + ": " + survivalPct.ToStringPercent() + "\n";
                tip += "TSA_WD_StrengthRefunded".Translate() + ": +" + refunded.ToString("F0");

                rows.Add(new RaidForceRow
                {
                    Label = lr.label ?? "?",
                    Faction = lr.faction,
                    Committed = refunded,
                    DisplayStrength = refunded,
                    Included = true,
                    Tooltip = tip.TrimEnd(),
                });
            }
            return rows;
        }
    }
}
