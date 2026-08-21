using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public class WITab_Outpost_Experts : WITab
    {
        private const float HeaderOffsetY = 0f;
        private const float ColGap = 18f;
        private const float IconColW = 56f;
        private const float IconPadding = 13f;
        private const float ListRowRightMargin = 8f;
        private const float RowPadding = 7f;
        private const float PortraitSize = 36f;
        private const float RoleIconSize = 36f;
        private const float RoleIconGap = 6f;
        private const float RoleRowLeftColW = IconPadding + RoleIconSize + RoleIconGap + PortraitSize + 8f;
        private const float PawnPortraitSize = 32f;
        private const float SmallLabelFloor = 24f;
        private const float TinyLabelFloor = 15f;
        private const float RoleLineGap = 2f;
        private const float RoleRowMinHeight = 54f;
        private static readonly Color NoneAssignedColor = new Color(1f, 0.92f, 0.35f);
        private static readonly Color NoneAssignedBlockedColor = new Color(0.55f, 0.55f, 0.55f);
        private const float ExpandedPanelTopPad = 0f;
        private const float PawnRowHeight = 32f;
        private const float PawnRowGap = 6f;
        private const float BenefitLabelMaxWidth = 170f;
        private const float BenefitColumnGap = 40f;
        private const float ConflictLabelWidth = 190f;

        private static readonly Color ConflictRowTint = new Color(0.85f, 0.22f, 0.22f, 0.22f);
        private static readonly Color CapacityColorRed = new Color(1f, 0.45f, 0.45f);
        private static readonly Color CapacityColorYellow = new Color(1f, 0.92f, 0.35f);
        private static readonly Color CapacityColorGreen = new Color(0.45f, 0.95f, 0.55f);

        private Vector2 leftScrollPosition;
        private Vector2 rightScrollPosition;
        private OutpostExpertRole? selectedRole;

        public WITab_Outpost_Experts()
        {
            size = new Vector2(1100f, 610f);
            labelKey = "TSA_WD_Experts_TabLabel";
        }

        private WorldObject_WD_Outpost SelOutpost => SelObject as WorldObject_WD_Outpost;

        public override bool IsVisible => SelOutpost != null && SelOutpost.Faction == Faction.OfPlayer;

        protected override void FillTab()
        {
            WorldObject_WD_Outpost outpost = SelOutpost;
            if (outpost == null) return;

            if (selectedRole.HasValue
                && !OutpostExpertUtility.IsRoleAvailableForOutpost(outpost, selectedRole.Value))
                selectedRole = null;

            Rect body = new Rect(0f, HeaderOffsetY, size.x, size.y - HeaderOffsetY).ContractedBy(10f);

            Text.Font = GameFont.Medium;
            string headline = OutpostTranslationUtil.TabHeadline(outpost, "TSA_WD_Experts_TabLabel");
            LabelAnchored(new Rect(body.x, body.y, body.width, 30f), headline, TextAnchor.MiddleLeft);
            Text.Font = GameFont.Small;
            Widgets.DrawLineHorizontal(body.x, body.y + 32f, body.width);

            float columnsTop = body.y + 38f;
            float columnsBottom = body.yMax;
            float leftW = Mathf.Max(260f, body.width * 0.42f);
            Rect leftArea = new Rect(body.x, columnsTop, leftW, columnsBottom - columnsTop);
            Rect rightArea = new Rect(body.x + leftW + ColGap, columnsTop, body.xMax - (body.x + leftW + ColGap), columnsBottom - columnsTop);
            Widgets.DrawLineVertical(body.x + leftW + ColGap * 0.5f, columnsTop, columnsBottom - columnsTop);

            DrawLeftColumn(leftArea, outpost);
            DrawRightColumn(rightArea, outpost);
        }

        private void DrawLeftColumn(Rect leftArea, WorldObject_WD_Outpost outpost)
        {
            float lx = leftArea.x;
            float lw = leftArea.width;
            float ly = leftArea.y;

            ly = Outpost_Expert_UI.DrawTotalBenefitsBox(lx, ly, lw, outpost);
            ly += Outpost_Dialog_UI.OutcomeBoxGap;

            GUI.color = new Color(0.75f, 0.82f, 1f);
            Widgets.Label(new Rect(lx, ly, lw, 22f), "TSA_WD_Experts_ChooseRoleHeader".Translate());
            GUI.color = Color.white;
            ly += 24f;

            int humanoids = OutpostExpertUtility.GetHumanoidOccupantCount(outpost);
            int maxSlots = OutpostExpertUtility.GetMaxExpertSlots(outpost);
            string capacityText = "TSA_WD_Experts_CapacityMax".Translate(maxSlots).ToString();
            string capacityTooltip = "TSA_WD_Experts_CapacityTooltip".Translate(humanoids, maxSlots).ToString();
            Text.Font = GameFont.Tiny;
            float capacityH = Text.CalcHeight(capacityText, lw);
            Rect capacityRect = new Rect(lx, ly, lw, capacityH);
            GUI.color = GetCapacityLabelColor(maxSlots);
            Widgets.Label(capacityRect, capacityText);
            TooltipHandler.TipRegion(capacityRect, capacityTooltip);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            ly += capacityH + 4f;

            float scrollHeight = leftArea.yMax - ly;
            float contentWidth = lw - 16f;
            float contentHeight = MeasureLeftScrollHeight(outpost, contentWidth);
            if (contentHeight < scrollHeight) contentHeight = scrollHeight;

            Rect scrollOuter = new Rect(lx, ly, lw, scrollHeight);
            Rect viewRect = new Rect(0f, 0f, contentWidth, contentHeight);
            Widgets.BeginScrollView(scrollOuter, ref leftScrollPosition, viewRect);

            float curY = 0f;
            foreach (OutpostExpertRole role in Enum.GetValues(typeof(OutpostExpertRole)))
            {
                if (!OutpostExpertUtility.IsRoleAvailableForOutpost(outpost, role))
                    continue;

                bool isSelected = selectedRole.HasValue && role == selectedRole.Value;
                float rowH = GetRoleRowHeight(outpost, role, isSelected, viewRect.width);
                Rect rowRect = new Rect(0f, curY, viewRect.width, rowH + RowPadding);
                Outpost_Dialog_UI.DrawSelectedRowTint(rowRect, isSelected);
                DrawRoleRow(new Rect(0f, curY, viewRect.width, rowH), outpost, role, isSelected);
                Outpost_Dialog_UI.FinishSelectableListRow(rowRect, isSelected);
                if (Widgets.ButtonInvisible(rowRect))
                    selectedRole = isSelected ? (OutpostExpertRole?)null : role;
                curY += rowH + RowPadding;
            }

            Widgets.EndScrollView();
        }

        private void DrawRightColumn(Rect rightArea, WorldObject_WD_Outpost outpost)
        {
            float rx = rightArea.x;
            float rw = rightArea.width;
            float ry = rightArea.y;

            if (!selectedRole.HasValue)
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(rx, ry, rw, 22f), "TSA_WD_Experts_SelectRoleHint".Translate());
                GUI.color = Color.white;
                return;
            }

            OutpostExpertRole role = selectedRole.Value;
            string roleLabel = OutpostExpertUtility.GetRoleLabel(role);
            GUI.color = new Color(0.75f, 0.82f, 1f);
            Widgets.Label(new Rect(rx, ry, rw, 22f), "TSA_WD_Experts_ChoosePawnHeader".Translate(roleLabel));
            GUI.color = Color.white;
            ry += 24f;

            var occupants = new List<Pawn>();
            foreach (Pawn p in OutpostExpertUtility.GetAllHumanoidOccupants(outpost))
                occupants.Add(p);

            occupants.Sort((a, b) =>
                OutpostExpertUtility.GetRoleSkillLevel(b, role)
                    .CompareTo(OutpostExpertUtility.GetRoleSkillLevel(a, role)));

            Pawn currentAssignee = outpost.GetAssignedExpert(role);
            bool roleBlocked = OutpostExpertUtility.IsRoleBlockedByCapacity(outpost, role);

            if (roleBlocked)
            {
                GUI.color = Color.gray;
                string blockedHint = "TSA_WD_Experts_RoleBlockedHint".Translate().ToString();
                float blockedH = Text.CalcHeight(blockedHint, rw);
                Widgets.Label(new Rect(rx, ry, rw, blockedH), blockedHint);
                GUI.color = Color.white;
                ry += blockedH + 4f;
            }

            float scrollHeight = rightArea.yMax - ry;
            float contentHeight = 8f;
            foreach (Pawn pawn in occupants)
                contentHeight += PawnRowHeight + PawnRowGap;
            if (contentHeight < scrollHeight) contentHeight = scrollHeight;

            Rect scrollOuter = new Rect(rx, ry, rw, scrollHeight);
            Rect viewRect = new Rect(0f, 0f, rw - 16f, contentHeight);
            Widgets.BeginScrollView(scrollOuter, ref rightScrollPosition, viewRect);

            float curY = 0f;
            int rowIndex = 0;
            foreach (Pawn pawn in occupants)
            {
                OutpostExpertRole? otherRole = OutpostExpertUtility.GetAssignedRoleForPawn(outpost, pawn, role);
                bool assignedElsewhere = otherRole.HasValue;
                float rowTotalH = PawnRowHeight + PawnRowGap;
                Rect rowRect = new Rect(0f, curY, viewRect.width, rowTotalH);
                if (rowIndex % 2 == 0) Widgets.DrawHighlight(rowRect);

                if (assignedElsewhere)
                {
                    GUI.color = ConflictRowTint;
                    GUI.DrawTexture(rowRect, BaseContent.WhiteTex);
                    GUI.color = Color.white;
                }

                bool isCurrent = currentAssignee == pawn;
                Outpost_Dialog_UI.DrawSelectedRowTint(rowRect, isCurrent);
                DrawPawnRow(new Rect(0f, curY, viewRect.width, rowTotalH), pawn, role, assignedElsewhere, otherRole);
                Outpost_Dialog_UI.FinishSelectableListRow(rowRect, isCurrent);

                if (Widgets.ButtonInvisible(rowRect))
                    TryAssignPawn(outpost, role, pawn, otherRole);

                curY += rowTotalH;
                rowIndex++;
            }

            Widgets.EndScrollView();
        }

        private float GetRoleRowHeight(WorldObject_WD_Outpost outpost, OutpostExpertRole role, bool expanded, float contentWidth)
        {
            float baseH = MeasureRoleRowBaseHeight(outpost, role, contentWidth);
            if (!expanded) return baseH;
            float panelW = contentWidth - IconPadding * 2f;
            return baseH + ExpandedPanelTopPad
                + OutpostExpertUtility.MeasureRoleExpandedPanelHeight(outpost, role, panelW);
        }

        private float MeasureLeftScrollHeight(WorldObject_WD_Outpost outpost, float contentWidth)
        {
            float h = 4f;
            foreach (OutpostExpertRole role in Enum.GetValues(typeof(OutpostExpertRole)))
            {
                if (!OutpostExpertUtility.IsRoleAvailableForOutpost(outpost, role))
                    continue;
                h += GetRoleRowHeight(outpost, role, selectedRole.HasValue && role == selectedRole.Value, contentWidth) + RowPadding;
            }
            return h;
        }

        private static float MeasureRoleRowBaseHeight(WorldObject_WD_Outpost outpost, OutpostExpertRole role, float contentWidth)
        {
            float benefitReserve = BenefitLabelMaxWidth + BenefitColumnGap + ListRowRightMargin;
            float textW = Mathf.Max(1f, contentWidth - RoleRowLeftColW - benefitReserve);
            GetRoleDetailLine(outpost, role, out _, out string detail);
            float nameH = Mathf.Max(SmallLabelFloor, Text.LineHeightOf(GameFont.Small));
            float detailH = MeasureRoleDetailHeight(detail, textW);
            float textBlock = nameH + RoleLineGap + detailH;
            return Mathf.Max(RoleRowMinHeight, PortraitSize + 12f, textBlock + 10f);
        }

        private static void GetRoleDetailLine(WorldObject_WD_Outpost outpost, OutpostExpertRole role, out Pawn assigned, out string detail)
        {
            assigned = outpost.GetAssignedExpert(role);
            if (assigned != null)
            {
                int skill = OutpostExpertUtility.GetRoleSkillLevel(assigned, role);
                string skillName = OutpostExpertUtility.GetRoleSkillNameForDisplay(role, assigned);
                detail = assigned.LabelShortCap + ", " + skillName + ": " + skill;
                return;
            }

            bool blocked = OutpostExpertUtility.IsRoleBlockedByCapacity(outpost, role);
            detail = blocked
                ? "TSA_WD_Experts_NoneAssignedBlocked".Translate().ToString()
                : "TSA_WD_Experts_NoneAssigned".Translate().ToString();
        }

        private static float MeasureRoleDetailHeight(string detail, float textW)
        {
            GameFont prev = Text.Font;
            Text.Font = GameFont.Tiny;
            float h = Mathf.Max(TinyLabelFloor, Text.CalcHeight(detail ?? "", textW));
            Text.Font = prev;
            return h;
        }

        private static void DrawRoleRow(Rect rect, WorldObject_WD_Outpost outpost, OutpostExpertRole role, bool expanded)
        {
            float pad = IconPadding;
            float benefitReserve = BenefitLabelMaxWidth + BenefitColumnGap + ListRowRightMargin;
            float textW = Mathf.Max(1f, rect.width - RoleRowLeftColW - benefitReserve);
            GetRoleDetailLine(outpost, role, out Pawn assigned, out string detailLine);
            float nameH = Mathf.Max(SmallLabelFloor, Text.LineHeightOf(GameFont.Small));
            float detailH = MeasureRoleDetailHeight(detailLine, textW);
            float textBlockH = nameH + RoleLineGap + detailH;
            float baseH = Mathf.Max(RoleRowMinHeight, PortraitSize + 12f, textBlockH + 10f);
            float contentY = rect.y + (baseH - PortraitSize) * 0.5f;
            string roleLabel = OutpostExpertUtility.GetRoleLabel(role);

            Rect roleIconRect = new Rect(rect.x + pad, contentY, RoleIconSize, RoleIconSize);
            Texture2D roleIcon = OutpostExpertRoleIcons.Get(role);
            if (roleIcon != null)
            {
                Color prev = GUI.color;
                GUI.color = Color.white;
                GUI.DrawTexture(roleIconRect, roleIcon);
                GUI.color = prev;
            }

            Rect portraitRect = new Rect(roleIconRect.xMax + RoleIconGap, contentY, PortraitSize, PortraitSize);
            if (assigned != null)
            {
                RenderTexture portrait = PortraitsCache.Get(assigned, new Vector2(PortraitSize, PortraitSize), Rot4.South, new Vector3(0f, 0f, 0.15f));
                if (portrait != null) GUI.DrawTexture(portraitRect, portrait);
            }
            else
            {
                GUI.color = new Color(1f, 1f, 1f, 0.15f);
                GUI.DrawTexture(portraitRect, BaseContent.WhiteTex);
                GUI.color = Color.gray;
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.Font = GameFont.Tiny;
                Widgets.Label(portraitRect, "-");
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
            }

            float midX = RoleRowLeftColW;
            float textTop = rect.y + (baseH - textBlockH) * 0.5f;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(new Rect(rect.x + midX, textTop, textW, nameH), roleLabel);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperLeft;
            float lineY = textTop + nameH + RoleLineGap;
            if (assigned != null)
            {
                GUI.color = Outpost_Dialog_UI.OutcomeValueColor;
                Widgets.Label(new Rect(rect.x + midX, lineY, textW, detailH), detailLine);
            }
            else
            {
                bool blocked = OutpostExpertUtility.IsRoleBlockedByCapacity(outpost, role);
                GUI.color = blocked ? NoneAssignedBlockedColor : NoneAssignedColor;
                Widgets.Label(new Rect(rect.x + midX, lineY, textW, detailH), detailLine);
            }

            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            GUI.color = Color.white;

            if (assigned != null)
            {
                float bonus = OutpostExpertUtility.GetExpertBonusFraction(outpost, role);
                string bonusText = OutpostExpertUtility.GetRoleRowBenefitText(outpost, role, bonus);
                Color bonusColor = bonus > 0f ? Outpost_Dialog_UI.OutcomeValueColor : Color.gray;
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = bonusColor;
                Rect bonusRect = new Rect(rect.xMax - benefitReserve, rect.y, BenefitLabelMaxWidth, baseH);
                Widgets.Label(bonusRect, bonusText);
                TooltipHandler.TipRegion(
                    bonusRect,
                    OutpostExpertUtility.BuildRoleRowBenefitTooltip(outpost, role, bonus));
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
            }

            if (expanded)
            {
                float descY = rect.y + baseH + ExpandedPanelTopPad;
                float panelW = rect.width - pad * 2f;
                string panelText = OutpostExpertUtility.BuildRoleExpandedPanelText(outpost, role);
                float panelH = OutpostExpertUtility.MeasureRoleExpandedPanelHeight(outpost, role, panelW) - 2f;
                Rect descRect = new Rect(rect.x + pad, descY, panelW, panelH);
                GUI.color = new Color(0.82f, 0.82f, 0.82f);
                Text.Font = GameFont.Tiny;
                Widgets.Label(descRect, panelText);
                string skillTip = OutpostExpertUtility.BuildRoleExpandedSkillTooltip(outpost, role);
                if (!string.IsNullOrEmpty(skillTip))
                    TooltipHandler.TipRegion(descRect, skillTip);
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
            }
        }

        private static void DrawPawnRow(
            Rect rect,
            Pawn pawn,
            OutpostExpertRole role,
            bool assignedElsewhere,
            OutpostExpertRole? otherRole)
        {
            Rect portraitRect = new Rect(IconPadding, rect.y + (rect.height - PawnPortraitSize) * 0.5f, PawnPortraitSize, PawnPortraitSize);
            RenderTexture portrait = PortraitsCache.Get(pawn, new Vector2(PawnPortraitSize, PawnPortraitSize), Rot4.South, new Vector3(0f, 0f, 0.15f));
            if (portrait != null) GUI.DrawTexture(portraitRect, portrait);

            float midX = IconColW;
            float rightReserve = assignedElsewhere ? ConflictLabelWidth + ListRowRightMargin : ListRowRightMargin;
            float textW = rect.width - midX - rightReserve;

            int skill = OutpostExpertUtility.GetRoleSkillLevel(pawn, role);
            string skillName = OutpostExpertUtility.GetRoleSkillNameForDisplay(role, pawn);
            string pawnLine = pawn.LabelShortCap + ", " + skillName + ": " + skill;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(new Rect(rect.x + midX, rect.y, textW, rect.height), pawnLine);
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            if (assignedElsewhere && otherRole.HasValue)
            {
                string otherLabel = OutpostExpertUtility.GetRoleLabel(otherRole.Value);
                string conflictText = "TSA_WD_Experts_AssignedAs".Translate(otherLabel).ToString();
                Text.Font = GameFont.Tiny;
                float conflictH = Text.CalcHeight(conflictText, ConflictLabelWidth);
                Rect conflictRect = new Rect(
                    rect.xMax - ListRowRightMargin - ConflictLabelWidth,
                    rect.y + (rect.height - conflictH) * 0.5f,
                    ConflictLabelWidth,
                    conflictH);
                GUI.color = new Color(1f, 0.55f, 0.55f);
                Widgets.Label(conflictRect, conflictText);
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
            }

            Rect infoRect = portraitRect.ExpandedBy(2f);
            if (Mouse.IsOver(infoRect)) Widgets.DrawHighlight(infoRect);
            if (Widgets.ButtonInvisible(infoRect))
                Find.WindowStack.Add(new Dialog_InfoCard(pawn));
        }

        private void TryAssignPawn(
            WorldObject_WD_Outpost outpost,
            OutpostExpertRole role,
            Pawn pawn,
            OutpostExpertRole? otherRole)
        {
            Pawn current = outpost.GetAssignedExpert(role);
            if (current == pawn)
            {
                outpost.ClearExpert(role);
                return;
            }

            if (otherRole.HasValue)
            {
                string msg = "TSA_WD_Experts_ReassignConfirm".Translate(
                    pawn.LabelShortCap,
                    OutpostExpertUtility.GetRoleLabel(otherRole.Value),
                    OutpostExpertUtility.GetRoleLabel(role)).ToString();
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(msg, () =>
                {
                    if (!OutpostExpertUtility.CanAssignExpertToRole(outpost, role, pawn))
                    {
                        Messages.Message(
                            "TSA_WD_Experts_AssignBlockedCapacity".Translate(),
                            outpost,
                            MessageTypeDefOf.RejectInput,
                            false);
                        return;
                    }
                    outpost.TryAssignExpert(role, pawn);
                }));
                return;
            }

            if (!OutpostExpertUtility.CanAssignExpertToRole(outpost, role, pawn))
            {
                Messages.Message(
                    "TSA_WD_Experts_AssignBlockedCapacity".Translate(),
                    outpost,
                    MessageTypeDefOf.RejectInput,
                    false);
                return;
            }

            outpost.TryAssignExpert(role, pawn);
        }

        private static Color GetCapacityLabelColor(int maxSlots)
        {
            if (maxSlots >= 4) return CapacityColorGreen;
            if (maxSlots >= 2) return CapacityColorYellow;
            return CapacityColorRed;
        }

        private static void LabelAnchored(Rect rect, string text, TextAnchor anchor)
        {
            Text.Anchor = anchor;
            Widgets.Label(rect, text);
            Text.Anchor = TextAnchor.UpperLeft;
        }
    }
}
