using System;
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
            if (lines.Count > 0)
                return boxPad * 2f + lineH + lines.Count * ExpertBenefitLineH;

            int assignedRows = CountAssignedRoleSummaryRows(outpost);
            if (assignedRows > 0)
                return boxPad * 2f + lineH + assignedRows * ExpertBenefitLineH;

            return boxPad * 2f + lineH + ExpertBenefitLineH;
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

            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(new Rect(ix, cy, iw, lineH), "TSA_WD_Experts_TotalBenefits".Translate());
            cy += lineH;

            if (benefitLines.Count > 0)
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
            else if (DrawAssignedRoleSummaries(valueX, cy, valueW, outpost, out float afterY))
            {
                cy = afterY;
            }
            else
            {
                GUI.color = Color.gray;
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(valueX, cy, valueW, ExpertBenefitLineH),
                    "TSA_WD_Experts_SummaryEmpty".Translate());
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
            }

            return y + boxH;
        }

        private static int CountAssignedRoleSummaryRows(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return 0;
            int n = 0;
            foreach (OutpostExpertRole role in Enum.GetValues(typeof(OutpostExpertRole)))
            {
                if (!OutpostExpertUtility.IsRoleAvailableForOutpost(outpost, role)) continue;
                if (outpost.GetAssignedExpert(role) != null)
                    n++;
            }
            return n;
        }

        /// <summary>
        /// Fallback when effect aggregate is empty but roles are assigned (usually skill 0 / max bonus 0%).
        /// Uses SummaryActive / SummaryNoBonus so the box never lies with "No expert roles assigned."
        /// </summary>
        private static bool DrawAssignedRoleSummaries(float x, float y, float w, WorldObject_WD_Outpost outpost, out float afterY)
        {
            afterY = y;
            if (outpost == null) return false;
            bool any = false;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperLeft;
            foreach (OutpostExpertRole role in Enum.GetValues(typeof(OutpostExpertRole)))
            {
                if (!OutpostExpertUtility.IsRoleAvailableForOutpost(outpost, role)) continue;
                Pawn pawn = outpost.GetAssignedExpert(role);
                if (pawn == null) continue;

                any = true;
                int skill = OutpostExpertUtility.GetRoleSkillLevel(pawn, role);
                float bonus = OutpostExpertUtility.GetExpertBonusFraction(outpost, role);
                int pct = Mathf.RoundToInt(bonus * 100f);
                string roleLabel = OutpostExpertUtility.GetRoleLabel(role);
                Rect lineRect = new Rect(x, afterY, w, ExpertBenefitLineH);
                if (pct > 0)
                {
                    GUI.color = Outpost_Dialog_UI.OutcomeValueColor;
                    Widgets.Label(lineRect, "TSA_WD_Experts_SummaryActive".Translate(roleLabel, pct, skill));
                }
                else
                {
                    GUI.color = Color.gray;
                    Widgets.Label(lineRect, "TSA_WD_Experts_SummaryNoBonus".Translate(
                        roleLabel,
                        OutpostExpertUtility.GetRoleSkillNameForDisplay(role, pawn) + " " + skill));
                }
                string tip = OutpostExpertUtility.BuildRoleRowBenefitTooltip(outpost, role, bonus);
                if (!string.IsNullOrEmpty(tip))
                    TooltipHandler.TipRegion(lineRect, tip);
                GUI.color = Color.white;
                afterY += ExpertBenefitLineH;
            }
            Text.Font = GameFont.Small;
            return any;
        }
    }
}
