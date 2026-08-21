using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;

namespace TSA_WorldDomination
{
    public class Window_RaidAttemptDetails : Window
    {
        private SpreadLogEntry entry;
        private Vector2 scrollAtk;
        private Vector2 scrollDef;
        public override Vector2 InitialSize => new Vector2(700f, 620f);

        public Window_RaidAttemptDetails(SpreadLogEntry entry)
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

            if (entry.isAborted)
            {
                Text.Font = GameFont.Medium;
                listing.Label("TSA_WD_Log_AttemptHeader_Aborted".Translate());
                Text.Font = GameFont.Small;
                listing.GapLine();
                listing.Label("TSA_WD_Log_Raid_Aborted_Detail".Translate());
                listing.Label(entry.message);
                listing.Gap(12f);
                listing.Label("TSA_WD_StrengthAtDeparture".Translate() + ": " + entry.attStr.ToString("F0"));
                listing.Label("TSA_WD_ResultingEfficiencyFactor".Translate() + ": " + entry.efficiencyFactor.ToStringPercent());
                listing.Gap(15f);
                if (Widgets.ButtonText(listing.GetRect(30f), "Close".Translate())) Close();
                listing.End();
                return;
            }

            // 1. Clean Header (settlement launched expedition)
            Text.Font = GameFont.Medium;
            listing.Label("TSA_WD_Log_AttemptHeader_Launched".Translate());
            Text.Font = GameFont.Small;
            listing.GapLine();

            // --- FORECAST POWER BOXES (Arriving Strength) ---
            float effectiveAtk = entry.attStr * entry.efficiencyFactor;
            Rect powers = listing.GetRect(70f);
            RaidUIUtils.DrawRaidPowerBoxes(powers, effectiveAtk, entry.defStr, "TSA_WD_Attackers", "TSA_WD_Defender");

            listing.Gap(12f);

            // --- TWO-COLUMN MATH BREAKDOWN ---
            Rect mathRect = listing.GetRect(50f);
            Rect leftCol = mathRect.LeftHalf();
            Rect rightCol = mathRect.RightHalf();

            float travelDays = entry.pathTravelTicks >= 0f
                ? entry.pathTravelTicks / 60000f
                : entry.targetDistance / 45f;

            // Column 1: Travel Math
            Widgets.Label(leftCol.TopHalf(), "TSA_WD_TimeToDestination".Translate() + ": " + travelDays.ToString("F1") + " " + "TSA_WD_Days".Translate());
            Widgets.Label(leftCol.BottomHalf(), "TSA_WD_ResultingEfficiencyFactor".Translate() + ": " + entry.efficiencyFactor.ToStringPercent());

            // Column 2: Strength Math
            Widgets.Label(rightCol.TopHalf(), "TSA_WD_StrengthAtDeparture".Translate() + ": " + entry.attStr.ToString("F0"));
            string arrivalMath = $"{entry.attStr:F0} x {entry.efficiencyFactor.ToStringPercent()} = {effectiveAtk:F0}";
            Widgets.Label(rightCol.BottomHalf(), "TSA_WD_StrengthAtArrival".Translate() + ": " + arrivalMath);

            listing.Gap(12f);
            PollutionRaidUi.DrawBanners(listing, entry.pollutionDamageExpected, entry.pollutionRouteAltered);

            listing.Gap(12f);

            // --- WIN CHANCE & RELATIVE STRENGTH ---
            float relativeStr = (entry.defStr > 0) ? (effectiveAtk / entry.defStr) : effectiveAtk;

            if (entry.ratio > 0f || entry.defStr > 0f)
            {
                float ratio = entry.ratio > 0f
                    ? entry.ratio
                    : (entry.attStr * entry.efficiencyFactor) / (entry.defStr > 0f ? entry.defStr : 1f);
                RaidOutcomeForecast forecast = RaidCasualtyModel.GetForecast(ratio, WorldDominationMod.settings);
                RaidUIUtils.DrawRaidForecast(listing, forecast, ratio, defenderPerspective: false,
                    "TSA_WD_RelativeStrength".Translate(relativeStr.ToString("F2")));
            }

            listing.Gap(15f);

            // --- COMPOSITION BREAKDOWN ---
            listing.Label("TSA_WD_ReinforcementBreakdown".Translate());
            Rect breakdownRect = listing.GetRect(180f);
            RaidUIUtils.DrawRaidForceBreakdownScrolls(
                breakdownRect,
                RaidForceLogRow.ToDisplayRows(entry.attForceRows),
                RaidForceLogRow.ToDisplayRows(entry.defForceRows),
                entry.attDetails,
                entry.defDetails,
                ref scrollAtk,
                ref scrollDef);

            listing.Gap(10f);
            if (Widgets.ButtonText(listing.GetRect(30f), "Close".Translate())) Close();
            listing.End();
        }
    }
}