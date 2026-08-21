using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Academy outpost: two-column dialog (cycle stats left, skill picker right) matching recruiting/production layout.</summary>
    public class Dialog_OutpostAcademyProduction : Window
    {
        private readonly WorldObject_WD_Outpost outpost;
        private Vector2 scrollPosition;
        private readonly string windowTitleText;
        private readonly int minTeacher;
        private readonly int capOffset;
        private readonly OutpostDefExtension academyExt;
        private readonly List<CachedAcademyRow> cachedRows = new List<CachedAcademyRow>();

        private struct CachedAcademyRow
        {
            public SkillDef Skill;
            public int BestLevel;
            public Pawn TeacherPawn;
            public string PortraitKey;
            public string HeadlineLine;
            public string SubLine;
            public string PortraitTooltip;
            public float RowHeight;
        }

        private const float ListRightMargin = 20f;
        private const float IconColW = 56f;
        private const float IconPadding = 8f;
        private const float ListRowRightMargin = 8f;
        private const float RowPadding = 6f;
        private const float NameLabelHeight = Outpost_Dialog_UI.ListRowNameHeight;
        private const float FormulaLineHeight = Outpost_Dialog_UI.ListRowFormulaLineHeight;
        private const float FormulaTopPadding = Outpost_Dialog_UI.ListRowFormulaTopPadding;
        private static float AcademyRowTextBlockHeight =>
            NameLabelHeight + FormulaTopPadding + FormulaLineHeight;
        private static readonly Vector2 PortraitSize = new Vector2(36f, 36f);
        private static readonly Vector2 LeftColumnPortraitSize = new Vector2(48f, 48f);
        private const int PortraitCacheMax = 64;
        private static readonly Dictionary<string, Texture> PortraitCache = new Dictionary<string, Texture>();

        public override Vector2 InitialSize => new Vector2(960f, 728f);

        public Dialog_OutpostAcademyProduction(WorldObject_WD_Outpost outpost)
        {
            this.outpost = outpost;
            doCloseButton = true;
            doCloseX = true;
            absorbInputAroundWindow = true;
            forcePause = false;
            optionalTitle = null;
            outpost.RecomputeProductionRequirementCache();

            windowTitleText = OutpostTranslationUtil.Key("TSA_WD_Academy_DialogTitle");

            if (!Outpost_Production_Utils.TryGetAcademyExtension(outpost?.def, out var ext))
            {
                academyExt = null;
                minTeacher = WorldDominationSettings.DefAcademyMinTeacherSkill;
                capOffset = WorldDominationSettings.DefAcademyTeachCapOffset;
            }
            else
            {
                academyExt = ext;
                minTeacher = Outpost_Academy.GetConfiguredMinTeacherSkill(ext);
                capOffset = Outpost_Academy.GetConfiguredTeachCapOffset(ext);
            }

            RebuildCachedRows();
        }

        public override void PreClose()
        {
            base.PreClose();
            Window_OutpostOverview.InvalidateCache();
        }

        private void RebuildCachedRows()
        {
            cachedRows.Clear();
            if (outpost == null || academyExt == null) return;

            var candidates = Outpost_Academy.GetCandidateSkills(outpost.def);
            var tmp = new List<CachedAcademyRow>();
            for (int i = 0; i < candidates.Count; i++)
            {
                var sd = candidates[i];
                if (sd == null) continue;
                int best = Outpost_Academy.GetBestTeacherLevel(outpost, sd, minTeacher);
                if (best < minTeacher) continue;

                var teacher = Outpost_Academy.GetPrimaryTeacherPawn(outpost, sd, best);
                int capExclusive = best - capOffset;
                float xpPerDay = Outpost_Academy.GetDisplayXpPerDayPool(academyExt, best, outpost);
                int xpPerDayInt = Mathf.RoundToInt(xpPerDay);
                string pawnName = teacher?.LabelShortCap ?? "?";
                string headline = OutpostTranslationUtil.Key(
                    "TSA_WD_Academy_RowHeadline",
                    sd.LabelCap,
                    pawnName,
                    best.ToString());
                string sub = OutpostTranslationUtil.Key(
                    "TSA_WD_Academy_RowSub",
                    xpPerDayInt.ToString(),
                    capExclusive.ToString());
                string portraitTip = teacher != null
                    ? OutpostTranslationUtil.Key(
                        "TSA_WD_Academy_PortraitTip",
                        teacher.LabelShortCap,
                        sd.LabelCap,
                        best.ToString())
                    : "";
                string softTip = Outpost_Production_Utils.BuildSoftProductionBonusTooltip(outpost);
                if (!string.IsNullOrEmpty(softTip))
                    portraitTip = string.IsNullOrEmpty(portraitTip) ? softTip : portraitTip + "\n\n" + softTip;

                tmp.Add(new CachedAcademyRow
                {
                    Skill = sd,
                    BestLevel = best,
                    TeacherPawn = teacher,
                    PortraitKey = BuildPortraitCacheKey(teacher),
                    HeadlineLine = headline,
                    SubLine = sub,
                    PortraitTooltip = portraitTip,
                    RowHeight = Mathf.Max(NameLabelHeight + FormulaLineHeight + 4f, AcademyRowTextBlockHeight + 8f)
                });
            }

            var highlightSkill = GetHighlightSkill();
            tmp.Sort((a, b) =>
            {
                if (highlightSkill != null)
                {
                    if (a.Skill == highlightSkill && b.Skill != highlightSkill) return -1;
                    if (b.Skill == highlightSkill && a.Skill != highlightSkill) return 1;
                }
                int c = b.BestLevel.CompareTo(a.BestLevel);
                if (c != 0) return c;
                string la = a.Skill?.LabelCap ?? a.Skill?.defName ?? "";
                string lb = b.Skill?.LabelCap ?? b.Skill?.defName ?? "";
                return string.CompareOrdinal(la, lb);
            });
            cachedRows.AddRange(tmp);
        }

        private SkillDef GetHighlightSkill()
            => outpost?.SelectedAcademySkill ?? Outpost_Academy.GetSkillForCurrentCycle(outpost);

        private void TrySelectAcademySkill(SkillDef skill)
        {
            if (skill == null) return;
            var producing = Outpost_Academy.GetSkillForCurrentCycle(outpost);
            if (outpost.IsSelectionLockedForThisCycle && producing != null && producing != skill)
            {
                outpost.SetSelectedAcademySkill(skill);
                Messages.Message(OutpostTranslationUtil.Key("TSA_WD_Production_NextCycle"), outpost, MessageTypeDefOf.NeutralEvent);
                return;
            }

            outpost.SetSelectedAcademySkill(skill);
            Close();
        }

        private static string BuildPortraitCacheKey(Pawn p)
        {
            if (p == null) return "";
            return PawnPortraitUIUtils.BuildCacheKey(p, VirtualPawnSummary.FromPawn(p));
        }

        private static Texture GetPortraitTexture(Pawn pawn, string key, Vector2 size)
        {
            return PawnPortraitUIUtils.GetPortrait(pawn, key, size, PortraitCache, PortraitCacheMax);
        }

        private static void OpenPawnInfoCard(Pawn pawn)
        {
            if (pawn == null) return;
            Find.WindowStack.Add(new Dialog_InfoCard(pawn));
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (outpost == null) return;

            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            float contentWidth = inRect.width - ListRightMargin;
            const float closeXLeftInset = 22f;
            const float rightScrollbarW = 16f;
            float rightContentRight = inRect.width - closeXLeftInset;

            float y = 0f;
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(0f, y, contentWidth, Outpost_Dialog_UI.DialogTitleHeight), windowTitleText);
            Text.Anchor = TextAnchor.UpperLeft;
            y += Outpost_Dialog_UI.DialogTitleRowAdvance;

            string outpostName = outpost.Name ?? outpost.Label;
            string typeLabel = outpost.def?.label ?? OutpostTranslationUtil.Key("TSA_WD_Outpost_GenericLabel");
            string subTitle = (outpostName + " (" + typeLabel + ")").Truncate(contentWidth);
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            const float subTitleHeight = 24f;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(0f, y, contentWidth, subTitleHeight), subTitle);
            Text.Anchor = TextAnchor.UpperLeft;
            y += subTitleHeight + 4f;

            y = Outpost_Dialog_UI.DrawProductionPauseBanner(0f, y, contentWidth, outpost);
            y += Outpost_Dialog_UI.AfterPauseBannerGap;

            const float bottomReserve = 44f;
            const float colGap = 18f;
            float columnsTop = y;
            float columnsBottom = inRect.height - bottomReserve;
            float leftW = Mathf.Max(260f, contentWidth * 0.42f);
            Rect leftArea = new Rect(0f, columnsTop, leftW, columnsBottom - columnsTop);
            float rightColRight = rightContentRight + rightScrollbarW;
            Rect rightArea = new Rect(leftW + colGap, columnsTop, rightColRight - (leftW + colGap), columnsBottom - columnsTop);
            Widgets.DrawLineVertical(leftW + colGap * 0.5f, columnsTop, columnsBottom - columnsTop);

            DrawLeftColumn(leftArea);
            DrawRightColumn(rightArea);
        }

        private void DrawLeftColumn(Rect leftArea)
        {
            float lx = leftArea.x;
            float lw = leftArea.width;
            float ly = leftArea.y;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;

            var cycleSkill = Outpost_Academy.GetSkillForCurrentCycle(outpost);
            int teacherLevel = 0;
            int teachCap = 0;
            int rawXpPerDay = 0;
            Pawn currentTeacher = null;
            if (cycleSkill != null && academyExt != null)
            {
                teacherLevel = Outpost_Academy.GetBestTeacherLevel(outpost, cycleSkill, minTeacher);
                if (teacherLevel >= minTeacher)
                {
                    currentTeacher = Outpost_Academy.GetPrimaryTeacherPawn(outpost, cycleSkill, teacherLevel);
                    teachCap = teacherLevel - capOffset;
                    rawXpPerDay = Mathf.RoundToInt(Outpost_Academy.GetDisplayXpPerDayPool(academyExt, teacherLevel));
                }
            }

            string detailedMath = Outpost_Academy.GetDetailedMathTooltip(outpost);
            string softSuffix = Outpost_Production_Utils.BuildSoftProductionBonusSuffix(outpost);
            if (!string.IsNullOrEmpty(softSuffix))
                detailedMath = (detailedMath ?? "") + "\n\n" + softSuffix.Trim();
            const float lineH = Outpost_Dialog_UI.OutcomeLineH;
            const float boxPad = Outpost_Dialog_UI.OutcomeBoxPad;
            float cycleDaysLeft = outpost.ProductionTicksLeftForDisplay / 60000f;
            float teacherBlockH = MeasureTeacherPortraitBlockHeight(cycleSkill, teacherLevel, includeHeader: true);
            float boxH = boxPad * 2f + (lineH + 2f) + teacherBlockH;
            Rect boxRect = new Rect(lx, ly, lw, boxH);
            Outpost_Dialog_UI.DrawOutcomeBox(boxRect);
            float cy = ly + boxPad;
            float ix = lx + boxPad;
            float iw = lw - boxPad * 2f;

            GUI.color = Outpost_Dialog_UI.CycleTimerColor;
            Rect cycleRect = new Rect(ix, cy, iw, lineH);
            Widgets.Label(cycleRect, OutpostTranslationUtil.Key("TSA_WD_Production_Info_CycleEndsIn", cycleDaysLeft.ToString("F1")));
            TooltipHandler.TipRegion(cycleRect, OutpostTranslationUtil.Key("TSA_WD_Production_Info_CycleEndsInTip"));
            GUI.color = Color.white;
            cy += lineH + 2f;

            cy = DrawTeacherPortraitSection(
                cy, ix, iw, currentTeacher, cycleSkill, teacherLevel, teachCap, rawXpPerDay,
                OutpostTranslationUtil.Key(
                    "TSA_WD_Academy_Dialog_CurrentlyTeaching",
                    cycleSkill?.LabelCap ?? OutpostTranslationUtil.Key("TSA_WD_Production_NoneLabel")),
                Color.white);
            TooltipHandler.TipRegion(boxRect, detailedMath);
            ly += boxH + Outpost_Dialog_UI.OutcomeBoxGap;

            DrawNextCycleSelectionBlock(ref ly, lx, lw);
            DrawRosterStatsSection(ref ly, lx, lw, cycleSkill);
        }

        private static float MeasureTeacherPortraitBlockHeight(SkillDef skill, int teacherLevel, bool includeHeader)
        {
            const float lineH = Outpost_Dialog_UI.OutcomeLineH;
            const float portraitSize = 48f;
            int textLines = (skill != null && teacherLevel > 0) ? 4 : 1;
            float contentH = Mathf.Max(portraitSize, textLines * lineH);
            float h = contentH + 8f;
            if (includeHeader) h += lineH + 2f;
            return h;
        }

        private float DrawTeacherPortraitSection(
            float ly, float lx, float lw, Pawn teacher, SkillDef skill,
            int teacherLevel, int teachCap, int rawXpPerDay, string header, Color headerColor,
            string sectionTooltip = null)
        {
            const float portraitSize = 48f;
            const float lineH = Outpost_Dialog_UI.OutcomeLineH;
            float blockTop = ly;

            GUI.color = headerColor;
            Widgets.Label(new Rect(lx, ly, lw, lineH), header);
            GUI.color = Color.white;
            ly += lineH + 2f;

            int textLines = (skill != null && teacherLevel > 0) ? 4 : 1;
            float textBlockH = textLines * lineH;
            float blockContentH = Mathf.Max(portraitSize, textBlockH);

            Rect portraitRect = new Rect(lx, ly, portraitSize, portraitSize);
            if (teacher != null)
            {
                string key = BuildPortraitCacheKey(teacher);
                Texture portrait = GetPortraitTexture(teacher, key, LeftColumnPortraitSize);
                if (portrait != null)
                    GUI.DrawTexture(portraitRect, portrait, ScaleMode.ScaleToFit);
                else
                    Widgets.DrawBoxSolid(portraitRect, new Color(0.3f, 0.3f, 0.35f, 1f));
                if (Widgets.ButtonInvisible(portraitRect))
                    OpenPawnInfoCard(teacher);
                if (skill != null)
                {
                    TooltipHandler.TipRegion(portraitRect, OutpostTranslationUtil.Key(
                        "TSA_WD_Academy_PortraitTip",
                        teacher.LabelShortCap,
                        skill.LabelCap,
                        teacherLevel.ToString()));
                }
            }
            else
            {
                Widgets.DrawBoxSolid(portraitRect, new Color(0.3f, 0.3f, 0.35f, 1f));
            }

            float textX = lx + portraitSize + 8f;
            float textW = lw - portraitSize - 8f;
            float textY = ly;
            string nameLine = OutpostTranslationUtil.Key(
                "TSA_WD_Academy_Dialog_TeacherName",
                teacher?.LabelShortCap ?? OutpostTranslationUtil.Key("TSA_WD_Production_NoneLabel"));
            Widgets.Label(new Rect(textX, textY, textW, lineH), nameLine);
            textY += lineH;
            if (skill != null && teacherLevel > 0)
            {
                Widgets.Label(new Rect(textX, textY, textW, lineH),
                    OutpostTranslationUtil.Key("TSA_WD_Academy_Info_TeacherSkillNamed", skill.LabelCap, teacherLevel.ToString()));
                textY += lineH;
                Widgets.Label(new Rect(textX, textY, textW, lineH),
                    OutpostTranslationUtil.Key("TSA_WD_Academy_Info_TeachCap", teachCap.ToString()));
                textY += lineH;
                Widgets.Label(new Rect(textX, textY, textW, lineH),
                    OutpostTranslationUtil.Key("TSA_WD_Academy_Info_RawXpPerDay", rawXpPerDay.ToString())
                    + Outpost_Production_Utils.BuildSoftProductionBonusSuffix(outpost));
            }

            ly += blockContentH + 8f;
            if (!string.IsNullOrEmpty(sectionTooltip))
                TooltipHandler.TipRegion(new Rect(lx, blockTop, lw, ly - blockTop), sectionTooltip);
            return ly;
        }

        private void DrawRosterStatsSection(ref float ly, float lx, float lw, SkillDef cycleSkill)
        {
            const float lineH = Outpost_Dialog_UI.OutcomeLineH;
            var stats = Outpost_Academy.GetRosterStats(outpost, cycleSkill);
            string skillLabel = cycleSkill?.LabelCap ?? "—";

            Widgets.DrawLineHorizontal(lx, ly, lw);
            ly += 8f;

            Widgets.Label(new Rect(lx, ly, lw, lineH),
                OutpostTranslationUtil.Key("TSA_WD_Academy_Dialog_Stats_Humanoids", stats.HumanoidOccupants.ToString()));
            ly += lineH;
            Widgets.Label(new Rect(lx, ly, lw, lineH),
                OutpostTranslationUtil.Key("TSA_WD_Academy_Dialog_Stats_Students", stats.StudentsTaught.ToString()));
            ly += lineH;
            Widgets.Label(new Rect(lx, ly, lw, lineH),
                OutpostTranslationUtil.Key("TSA_WD_Academy_Dialog_Stats_AvgSkill", skillLabel, stats.AvgStudentSkill.ToString("F0")));
            ly += lineH;
            Widgets.Label(new Rect(lx, ly, lw, lineH),
                OutpostTranslationUtil.Key("TSA_WD_Academy_Dialog_Stats_TooSkilled", stats.TooSkilled.ToString()));
            ly += lineH + 4f;
        }

        private void DrawNextCycleSelectionBlock(ref float ly, float lx, float lw)
        {
            if (!outpost.IsSelectionLockedForThisCycle) return;

            var cycleSkill = Outpost_Academy.GetSkillForCurrentCycle(outpost);
            var nextSkill = outpost.SelectedAcademySkill;
            if (nextSkill == null || nextSkill == cycleSkill) return;

            int nextTeacherLevel = Outpost_Academy.GetBestTeacherLevel(outpost, nextSkill, minTeacher);
            Pawn nextTeacher = nextTeacherLevel >= minTeacher
                ? Outpost_Academy.GetPrimaryTeacherPawn(outpost, nextSkill, nextTeacherLevel)
                : null;
            int nextTeachCap = nextTeacherLevel >= minTeacher ? nextTeacherLevel - capOffset : 0;
            int nextRawXp = academyExt != null && nextTeacherLevel >= minTeacher
                ? Mathf.RoundToInt(Outpost_Academy.GetDisplayXpPerDayPool(academyExt, nextTeacherLevel))
                : 0;

            ly += 4f;
            ly = DrawTeacherPortraitSection(
                ly, lx, lw, nextTeacher, nextSkill, nextTeacherLevel, nextTeachCap, nextRawXp,
                OutpostTranslationUtil.Key("TSA_WD_Production_SelectedForNextCycle", nextSkill.LabelCap),
                new Color(1f, 0.85f, 0.35f),
                OutpostTranslationUtil.Key("TSA_WD_Production_SelectedForNextCycleTip"));
        }

        private void DrawRightColumn(Rect rightArea)
        {
            float x = rightArea.x;
            float y = rightArea.y;
            float w = rightArea.width;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = new Color(0.75f, 0.82f, 1f);
            Widgets.Label(new Rect(x, y, w, 22f), OutpostTranslationUtil.Key("TSA_WD_Academy_ChooseTraining"));
            GUI.color = Color.white;
            y += 24f;

            const float clearBtnH = 36f;
            const float clearBtnW = 120f;
            float scrollBottom = rightArea.yMax - clearBtnH - 8f;
            float scrollH = Mathf.Max(60f, scrollBottom - y);

            float filteredScrollHeight = 8f;
            for (int i = 0; i < cachedRows.Count; i++)
                filteredScrollHeight += cachedRows[i].RowHeight + RowPadding;

            Rect scrollViewRect = new Rect(x, y, w, scrollH);
            Rect viewRect = new Rect(0f, 0f, w - 16f, Mathf.Max(filteredScrollHeight, 1f));
            Widgets.BeginScrollView(scrollViewRect, ref scrollPosition, viewRect);

            float curY = 0f;
            float midX = IconColW;
            float textW = viewRect.width - midX - ListRowRightMargin;
            var highlightSkill = GetHighlightSkill();
            Text.Font = GameFont.Small;

            for (int rowIdx = 0; rowIdx < cachedRows.Count; rowIdx++)
            {
                var row = cachedRows[rowIdx];
                if (row.Skill == null) continue;

                Rect rowRect = new Rect(0f, curY, viewRect.width, row.RowHeight + RowPadding);
                if (rowIdx % 2 == 0) Widgets.DrawHighlight(rowRect);
                bool isSelected = row.Skill == highlightSkill;
                Outpost_Dialog_UI.DrawSelectedRowTint(rowRect, isSelected);

                float rowContentY = curY + (rowRect.height - row.RowHeight) / 2f;

                Rect portraitCell = new Rect(IconPadding, rowContentY + (row.RowHeight - PortraitSize.y) / 2f, PortraitSize.x, PortraitSize.y);
                Texture portrait = row.TeacherPawn != null ? GetPortraitTexture(row.TeacherPawn, row.PortraitKey, PortraitSize) : null;
                if (portrait != null)
                    GUI.DrawTexture(portraitCell, portrait, ScaleMode.ScaleToFit);
                else
                    Widgets.DrawBoxSolid(portraitCell, new Color(0.3f, 0.3f, 0.35f, 1f));

                float textBlockH = AcademyRowTextBlockHeight;
                float textTop = rowContentY + (row.RowHeight - textBlockH) * 0.5f;
                Rect nameRect = new Rect(midX, textTop, textW, NameLabelHeight);
                GUI.color = Color.white;
                Widgets.Label(nameRect, row.HeadlineLine);

                Text.Font = GameFont.Tiny;
                GUI.color = Color.gray;
                float lineY = textTop + NameLabelHeight + FormulaTopPadding;
                Widgets.Label(new Rect(midX, lineY, textW, FormulaLineHeight), row.SubLine);
                Text.Font = GameFont.Small;
                GUI.color = Color.white;

                Outpost_Dialog_UI.FinishSelectableListRow(rowRect, isSelected);
                if (Widgets.ButtonInvisible(rowRect))
                    TrySelectAcademySkill(row.Skill);

                if (row.TeacherPawn != null)
                {
                    if (Widgets.ButtonInvisible(portraitCell))
                        OpenPawnInfoCard(row.TeacherPawn);
                    if (!string.IsNullOrEmpty(row.PortraitTooltip))
                        TooltipHandler.TipRegion(portraitCell, row.PortraitTooltip);
                }

                curY += row.RowHeight + RowPadding;
            }

            Widgets.EndScrollView();

            float clearY = rightArea.yMax - clearBtnH - 4f;
            if (Widgets.ButtonText(new Rect(x + w - clearBtnW - ListRightMargin, clearY, clearBtnW, clearBtnH),
                OutpostTranslationUtil.Key("TSA_WD_Production_Clear")))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    OutpostTranslationUtil.Key("TSA_WD_Production_ClearConfirm"),
                    () =>
                    {
                        outpost.SetSelectedAcademySkill(null);
                        RebuildCachedRows();
                    }));
            }

            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }
    }
}
