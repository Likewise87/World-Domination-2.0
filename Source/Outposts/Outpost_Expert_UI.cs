using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    internal static class Outpost_Expert_UI
    {
        private const float ExpertBenefitLineH = 18f;

        public static float MeasureTotalBenefitsBoxHeight(WorldObject_WD_Outpost outpost)
        {
            const float lineH = Outpost_Dialog_UI.OutcomeLineH;
            const float boxPad = Outpost_Dialog_UI.OutcomeBoxPad;
            var lines = OutpostExpertUtility.BuildAggregateBenefitLines(outpost);
            if (lines.Count == 0)
                return boxPad * 2f + lineH + ExpertBenefitLineH;
            return boxPad * 2f + lineH + lines.Count * ExpertBenefitLineH;
        }

        public static float DrawTotalBenefitsBox(float x, float y, float w, WorldObject_WD_Outpost outpost)
        {
            var benefitLines = OutpostExpertUtility.BuildAggregateBenefitLines(outpost);
            float boxH = MeasureTotalBenefitsBoxHeight(outpost);
            Outpost_Dialog_UI.DrawOutcomeBox(new Rect(x, y, w, boxH));
            float cy = y + Outpost_Dialog_UI.OutcomeBoxPad;
            float ix = x + Outpost_Dialog_UI.OutcomeBoxPad;
            float iw = w - Outpost_Dialog_UI.OutcomeBoxPad * 2f;
            const float lineH = Outpost_Dialog_UI.OutcomeLineH;
            float valueX = ix + Outpost_Dialog_UI.OutcomeValueIndent;
            float valueW = iw - Outpost_Dialog_UI.OutcomeValueIndent;

            Widgets.Label(new Rect(ix, cy, iw, lineH), "TSA_WD_Experts_TotalBenefits".Translate());
            cy += lineH;

            if (benefitLines.Count == 0)
            {
                GUI.color = Color.gray;
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(valueX, cy, valueW, ExpertBenefitLineH),
                    "TSA_WD_Experts_SummaryEmpty".Translate());
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
            }
            else
            {
                Text.Font = GameFont.Tiny;
                for (int i = 0; i < benefitLines.Count; i++)
                {
                    var line = benefitLines[i];
                    Rect lineRect = new Rect(valueX, cy, valueW, ExpertBenefitLineH);
                    GUI.color = Outpost_Dialog_UI.OutcomeValueColor;
                    Widgets.Label(lineRect, line.DisplayText);
                    GUI.color = Color.white;
                    if (!string.IsNullOrEmpty(line.Tooltip))
                        TooltipHandler.TipRegion(lineRect, line.Tooltip);
                    cy += ExpertBenefitLineH;
                }
                Text.Font = GameFont.Small;
            }

            return y + boxH;
        }
    }
}
