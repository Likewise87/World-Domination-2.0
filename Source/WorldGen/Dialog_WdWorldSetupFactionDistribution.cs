using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace TSA_WorldDomination
{
    /// <summary>World Setup: per-faction settlement share for recreate (Negotiate Overview layout).</summary>
    public class Dialog_WdWorldSetupFactionDistribution : Window
    {
        private List<Faction> factions = new List<Faction>();
        private Vector2 scrollPosition;

        private const float RowH = 48f;
        private const float IconSize = 32f;
        private const float HeaderH = 25f;
        private const float ColFaction = 220f;
        private const float ColShare = 280f;
        private const float ColPct = 70f;
        private const float ColCount = 90f;
        private const float BottomH = 44f;
        private const float ShareMin = 0f;
        private const float ShareMax = 200f;

        private static bool s_labelsInit;
        private static string s_title, s_tip, s_hdrFaction, s_hdrShare, s_hdrPct, s_hdrCount, s_reset, s_empty;

        public override Vector2 InitialSize => new Vector2(720f, 640f);

        public Dialog_WdWorldSetupFactionDistribution()
        {
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            doCloseX = true;
            doCloseButton = false;
            Refresh();
        }

        private void Refresh()
        {
            WD_SettlementLayoutUtility.EnsureFactionSharesInitialized();
            factions = WD_SettlementLayoutUtility.ListRecreateEligibleFactions();
        }

        private static void EnsureLabels()
        {
            if (s_labelsInit) return;
            s_labelsInit = true;
            s_title = "TSA_WD_WorldSetup_FactionSharesTitle".Translate();
            s_tip = "TSA_WD_WorldSetup_FactionSharesTip".Translate();
            s_hdrFaction = "TSA_WD_WorldSetup_FactionShares_H_Faction".Translate();
            s_hdrShare = "TSA_WD_WorldSetup_FactionShares_H_Share".Translate();
            s_hdrPct = "TSA_WD_WorldSetup_FactionShares_H_Pct".Translate();
            s_hdrCount = "TSA_WD_WorldSetup_FactionShares_H_Count".Translate();
            s_reset = "TSA_WD_WorldSetup_FactionShares_Reset".Translate();
            s_empty = "TSA_WD_WorldSetup_FactionShares_Empty".Translate();
        }

        private static void LabelAnchored(Rect rect, string text, TextAnchor anchor)
        {
            TextAnchor prev = Text.Anchor;
            Text.Anchor = anchor;
            Widgets.Label(rect, text);
            Text.Anchor = prev;
        }

        private static void DrawColHeader(Rect rect, string label, TextAnchor anchor)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            LabelAnchored(rect, label, anchor);
            GUI.color = Color.white;
        }

        public override void DoWindowContents(Rect inRect)
        {
            EnsureLabels();

            float y = 0f;
            Text.Font = GameFont.Medium;
            LabelAnchored(new Rect(0f, y, inRect.width, 32f), s_title, TextAnchor.MiddleLeft);
            y += 36f;

            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(0f, y, inRect.width, 34f), s_tip);
            GUI.color = Color.white;
            y += 38f;

            float contentW = ColFaction + ColShare + ColPct + ColCount;
            Rect hRect = new Rect(0f, y, inRect.width, HeaderH);
            float hx = 0f;
            DrawColHeader(new Rect(hx, hRect.y, ColFaction, HeaderH), s_hdrFaction, TextAnchor.LowerLeft);
            hx += ColFaction;
            DrawColHeader(new Rect(hx, hRect.y, ColShare, HeaderH), s_hdrShare, TextAnchor.LowerCenter);
            hx += ColShare;
            DrawColHeader(new Rect(hx, hRect.y, ColPct, HeaderH), s_hdrPct, TextAnchor.LowerCenter);
            hx += ColPct;
            DrawColHeader(new Rect(hx, hRect.y, ColCount, HeaderH), s_hdrCount, TextAnchor.LowerCenter);
            Widgets.DrawLineHorizontal(0f, hRect.yMax, inRect.width);

            float listTop = hRect.yMax + 4f;
            Rect listOut = new Rect(0f, listTop, inRect.width, inRect.height - listTop - BottomH - 8f);

            if (factions == null || factions.Count == 0)
            {
                Text.Font = GameFont.Small;
                GUI.color = Color.gray;
                LabelAnchored(listOut, s_empty, TextAnchor.MiddleCenter);
                GUI.color = Color.white;
            }
            else
            {
                float pool = WD_SettlementLayoutUtility.GetFactionSharePool();
                Dictionary<Faction, int> assigned = WD_SettlementLayoutUtility.GetScaledNpcSettlementCounts();
                float viewH = Mathf.Max(listOut.height, factions.Count * RowH + 4f);
                float viewW = Mathf.Max(listOut.width - 16f, contentW);
                Rect view = new Rect(0f, 0f, viewW, viewH);
                Widgets.BeginScrollView(listOut, ref scrollPosition, view);
                for (int i = 0; i < factions.Count; i++)
                {
                    Faction f = factions[i];
                    if (f == null) continue;
                    float share = WD_SettlementLayoutUtility.GetFactionShare(f);
                    int count = 0;
                    if (assigned != null)
                        assigned.TryGetValue(f, out count);
                    float pct = pool > 0f ? share / pool : 0f;
                    float next = DrawRow(0f, i * RowH, view.width, f, share, pct, count, i);
                    if (!Mathf.Approximately(next, share))
                        WD_SettlementLayoutUtility.SetFactionShare(f, next);
                }
                Widgets.EndScrollView();
            }

            float btnW = (inRect.width - 12f) / 2f;
            if (Widgets.ButtonText(new Rect(0f, inRect.height - BottomH, btnW, 36f), s_reset))
            {
                WD_SettlementLayoutUtility.ResetFactionSharesToVanillaSnapshot();
                Refresh();
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
            if (Widgets.ButtonText(new Rect(btnW + 12f, inRect.height - BottomH, btnW, 36f), "Close".Translate()))
                Close();
        }

        private static float DrawRow(
            float x,
            float y,
            float width,
            Faction faction,
            float share,
            float pct,
            int expectedCount,
            int index)
        {
            Rect rowRect = new Rect(x, y, width, RowH);
            if (index % 2 == 0) Widgets.DrawHighlight(rowRect);
            if (Mouse.IsOver(rowRect)) Widgets.DrawLightHighlight(rowRect);

            float curX = x;
            float iconY = y + (RowH - IconSize) * 0.5f;
            Rect iconRect = new Rect(curX + 6f, iconY, IconSize, IconSize);
            WorldDomination_UIUtils.DrawFactionIconWithColor(iconRect, faction);

            float nameX = iconRect.xMax + 8f;
            float nameW = ColFaction - (nameX - curX) - 4f;
            Rect nameRect = new Rect(nameX, y + 4f, nameW, 24f);
            Text.Font = GameFont.Small;
            string name = faction.Name ?? "?";
            Color nameColor = WorldDomination_UIUtils.ColorForRelationWithPlayer(faction);
            LabelAnchored(nameRect, name.Truncate(nameW).Colorize(nameColor), TextAnchor.MiddleLeft);
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            LabelAnchored(new Rect(nameX, y + 26f, nameW, 16f),
                (faction.def?.LabelCap ?? "").Truncate(nameW), TextAnchor.MiddleLeft);
            GUI.color = Color.white;
            curX += ColFaction;

            Rect shareRect = new Rect(curX + 8f, y + (RowH - 22f) * 0.5f, ColShare - 16f, 22f);
            float next = Widgets.HorizontalSlider(shareRect, share, ShareMin, ShareMax, middleAlignment: false, null, null, null, 1f);
            next = Mathf.Round(next);
            curX += ColShare;

            Text.Font = GameFont.Small;
            LabelAnchored(new Rect(curX, y, ColPct, RowH), (pct * 100f).ToString("F0") + "%", TextAnchor.MiddleCenter);
            curX += ColPct;

            LabelAnchored(new Rect(curX, y, ColCount, RowH), expectedCount.ToString(), TextAnchor.MiddleCenter);
            return next;
        }
    }
}
