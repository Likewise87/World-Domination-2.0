using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;
using RimWorld.Planet;

namespace TSA_WorldDomination
{
    public class Window_ActionLog : Window
    {
        private Vector2 scrollPos;
        public override Vector2 InitialSize => new Vector2(UI.screenWidth, UI.screenHeight);

        private string filterActor = "";
        private string filterTarget = "";
        private string filterMessage = "";

        private WorldComponent_SpreadManager cachedManager;

        private readonly List<SpreadLogEntry> viewEntries = new List<SpreadLogEntry>();
        private int viewLogFpCount = -1;
        private int viewLogFpHeadTs;
        private int viewLogFpTailTs;
        private string viewCachedFilterActor = "\u0001";
        private string viewCachedFilterTarget = "\u0001";
        private string viewCachedFilterMessage = "\u0001";
        private readonly List<string> viewTimeLabels = new List<string>();

        private static string labelWindowTitle, labelFilterByActor, labelFilterByTarget, labelFilterByMessage;
        private static string labelHeaderTime, labelHeaderActor, labelHeaderTarget, labelHeaderAction;
        private static string labelBtnDetails;

        public Window_ActionLog()
        {
            this.doCloseX = true;
            this.draggable = false;
            this.forcePause = false;
            this.absorbInputAroundWindow = false;
            this.preventCameraMotion = false;
            this.closeOnCancel = true;
        }

        public override void PostClose()
        {
            base.PostClose();
            WdWindowEsc.ClearTextFocusOnClose();
        }

        public override void DoWindowContents(Rect inRect)
        {
            WdNavWindows.ProcessHotkeys();
            if (!IsOpen) return;
            if (WdWindowEsc.TryCloseOnCancel(this))
                return;

            Text.Font = GameFont.Medium;
            if (labelWindowTitle == null)
            {
                labelWindowTitle = "TSA_WD_Log_WindowTitle".Translate();
                labelFilterByActor = "TSA_WD_FilterByActor".Translate();
                labelFilterByTarget = "TSA_WD_FilterByTarget".Translate();
                labelFilterByMessage = "TSA_WD_FilterByMessage".Translate();
                labelHeaderTime = "  " + (string)"TSA_WD_Log_HeaderTime".Translate();
                labelHeaderActor = "TSA_WD_Log_HeaderActor".Translate();
                labelHeaderTarget = "TSA_WD_Log_HeaderTarget".Translate();
                labelHeaderAction = "TSA_WD_Log_HeaderAction".Translate();
                labelBtnDetails = "TSA_WD_Log_BtnDetails".Translate();
            }
            Widgets.Label(new Rect(0, 0, inRect.width, 35f), labelWindowTitle);

            float colTime = 120f;
            float col12Width = 350f;
            float col3Width = inRect.width - colTime - (col12Width * 2) - 120f;
            float btnWidth = 90f;

            float filterY = 40f;
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;

            filterActor = Widgets.TextField(new Rect(colTime + 30f, filterY, col12Width - 30f, 20f), filterActor);
            if (filterActor.NullOrEmpty()) Widgets.Label(new Rect(colTime + 35f, filterY, col12Width - 35f, 20f), labelFilterByActor);

            filterTarget = Widgets.TextField(new Rect(colTime + col12Width + 40f, filterY, col12Width - 30f, 20f), filterTarget);
            if (filterTarget.NullOrEmpty()) Widgets.Label(new Rect(colTime + col12Width + 45f, filterY, col12Width - 35f, 20f), labelFilterByTarget);

            filterMessage = Widgets.TextField(new Rect(colTime + (col12Width * 2) + 20f, filterY, col3Width, 20f), filterMessage);
            if (filterMessage.NullOrEmpty()) Widgets.Label(new Rect(colTime + (col12Width * 2) + 25f, filterY, col3Width - 5f, 20f), labelFilterByMessage);

            Rect headerRect = new Rect(0, 65f, inRect.width, 25f);
            Text.Anchor = TextAnchor.MiddleLeft;

            Widgets.Label(new Rect(0, headerRect.y, colTime, 25f), labelHeaderTime);
            Widgets.Label(new Rect(colTime + 30f, headerRect.y, col12Width - 30f, 25f), labelHeaderActor);
            Widgets.Label(new Rect(colTime + col12Width + 40f, headerRect.y, col12Width - 30f, 25f), labelHeaderTarget);
            Widgets.Label(new Rect(colTime + (col12Width * 2) + 20f, headerRect.y, col3Width, 25f), labelHeaderAction);

            GUI.color = Color.white; Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.DrawLineHorizontal(0, headerRect.yMax, inRect.width);

            if (cachedManager == null)
                cachedManager = Find.World.GetComponent<WorldComponent_SpreadManager>();
            if (cachedManager == null) return;

            List<SpreadLogEntry> raw = cachedManager.GetLog();
            EnsureActionLogView(raw);

            const float rowStep = 44f;
            const float rowInner = 42f;
            Rect viewRect = new Rect(0, 0, inRect.width - 25f, viewEntries.Count * rowStep);
            Widgets.BeginScrollView(new Rect(0, 95f, inRect.width, inRect.height - 110f), ref scrollPos, viewRect);

            for (int i = 0; i < viewEntries.Count; i++)
            {
                SpreadLogEntry entry = viewEntries[i];
                Rect row = new Rect(0, i * rowStep, viewRect.width, rowInner);
                if (i % 2 == 0) Widgets.DrawHighlight(row);

                Rect timeRect = new Rect(row.x + 5f, row.y, colTime, row.height);
                GUI.color = Color.gray; Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(timeRect, viewTimeLabels[i]);
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white; Text.Font = GameFont.Small;

                Rect iconARect = new Rect(row.x + colTime, row.y + 9f, 24f, 24f);
                WorldDomination_UIUtils.DrawFactionIconWithColor(iconARect, entry.targetA);
                DrawJumpLabel(new Rect(row.x + colTime + 30f, row.y, col12Width - 30f, rowInner), entry.labelA, entry.targetA);

                Rect iconBRect = new Rect(row.x + colTime + col12Width + 10f, row.y + 9f, 24f, 24f);
                WorldDomination_UIUtils.DrawFactionIconWithColor(iconBRect, entry.targetB);
                DrawJumpLabel(new Rect(row.x + colTime + col12Width + 40f, row.y, col12Width - 30f, rowInner), entry.labelB, entry.targetB);

                Rect colCRect = new Rect(row.x + colTime + (col12Width * 2) + 20f, row.y, col3Width, rowInner);
                Text.Anchor = TextAnchor.MiddleLeft;
                string suffix = RaidUIUtils.FormatRaidLogSuffix(entry);
                string displayMessage = entry.message;
                if (!suffix.NullOrEmpty())
                    displayMessage += suffix;
                bool useTinyAction = !suffix.NullOrEmpty() || MessageHasBandPickSuffix(displayMessage);
                if (useTinyAction)
                    Text.Font = GameFont.Tiny;
                Widgets.Label(colCRect, displayMessage);
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;

                bool isCaravanClash = IsCaravanClashEntry(entry);
                if (entry.isRaid || isCaravanClash)
                {
                    Rect btnRect = new Rect(row.xMax - btnWidth - 10f, row.y + 9f, btnWidth, 24f);
                    if (Widgets.ButtonText(btnRect, labelBtnDetails))
                    {
                        if (isCaravanClash)
                        {
                            Find.WindowStack.Add(new Window_CaravanClashDetails(entry));
                        }
                        else if (entry.isAttempt)
                        {
                            Find.WindowStack.Add(new Window_RaidAttemptDetails(entry));
                        }
                        else
                        {
                            Find.WindowStack.Add(new Window_RaidResolutionDetails(entry));
                        }
                    }
                }
            }
            Widgets.EndScrollView();
        }

        private static bool MessageHasBandPickSuffix(string message)
        {
            if (message.NullOrEmpty()) return false;
            return message.IndexOf("Band ", StringComparison.Ordinal) >= 0
                || message.IndexOf("Banda ", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("距离段", StringComparison.Ordinal) >= 0;
        }

        private static bool IsCaravanClashEntry(SpreadLogEntry entry)
        {
            if (entry == null) return false;
            if (entry.isCaravanClash) return true;
            string clashLabel = "TSA_WD_Log_TravelerClash".Translate().ToString();
            return entry.isRaid
                && !entry.isAttempt
                && !entry.isAborted
                && entry.message == clashLabel
                && entry.attDetails != null
                && entry.defDetails != null
                && entry.attDetails.Count == 1
                && entry.defDetails.Count == 1;
        }

        private void EnsureActionLogView(List<SpreadLogEntry> raw)
        {
            if (raw == null || raw.Count == 0)
            {
                viewEntries.Clear();
                viewTimeLabels.Clear();
                viewLogFpCount = raw?.Count ?? 0;
                viewCachedFilterActor = filterActor;
                viewCachedFilterTarget = filterTarget;
                viewCachedFilterMessage = filterMessage;
                return;
            }
            int c = raw.Count;
            int headTs = raw[0].timestamp;
            int tailTs = raw[c - 1].timestamp;
            bool fpSame = c == viewLogFpCount && headTs == viewLogFpHeadTs && tailTs == viewLogFpTailTs;
            bool filtersSame = filterActor == viewCachedFilterActor && filterTarget == viewCachedFilterTarget && filterMessage == viewCachedFilterMessage;
            if (fpSame && filtersSame) return;

            viewEntries.Clear();
            viewTimeLabels.Clear();
            viewLogFpCount = c;
            viewLogFpHeadTs = headTs;
            viewLogFpTailTs = tailTs;
            viewCachedFilterActor = filterActor;
            viewCachedFilterTarget = filterTarget;
            viewCachedFilterMessage = filterMessage;

            bool fa = !filterActor.NullOrEmpty();
            bool ft = !filterTarget.NullOrEmpty();
            bool fm = !filterMessage.NullOrEmpty();
            for (int i = c - 1; i >= 0; i--)
            {
                SpreadLogEntry l = raw[i];
                if (fa && ((l.labelA ?? "").IndexOf(filterActor, StringComparison.OrdinalIgnoreCase) < 0)) continue;
                if (ft && ((l.labelB ?? "").IndexOf(filterTarget, StringComparison.OrdinalIgnoreCase) < 0)) continue;
                if (fm && ((l.message ?? "").IndexOf(filterMessage, StringComparison.OrdinalIgnoreCase) < 0)) continue;
                viewEntries.Add(l);
            }
            for (int j = 0; j < viewEntries.Count; j++)
                viewTimeLabels.Add(FormatTicks(viewEntries[j].timestamp));
        }

        private string FormatTicks(int ticks)
        {
            if (ticks <= 0) return "---";
            int day = (ticks / 60000) + 1;
            int hour = (ticks % 60000) / 2500;
            return $"Day {day}, {hour}h";
        }

        private void DrawJumpLabel(Rect rect, string label, GlobalTargetInfo target)
        {
            Text.Anchor = TextAnchor.MiddleLeft;
            if (target.IsValid)
            {
                Widgets.DrawHighlightIfMouseover(rect);
                if (Widgets.ButtonInvisible(rect))
                {
                    CameraJumper.TryJumpAndSelect(target);
                    this.Close();
                    if (Find.MainTabsRoot.OpenTab != null) Find.MainTabsRoot.EscapeCurrentTab();
                }
            }
            Widgets.Label(rect, label);
            Text.Anchor = TextAnchor.UpperLeft;
        }
    }
}