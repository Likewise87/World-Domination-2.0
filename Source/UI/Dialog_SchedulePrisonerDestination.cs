using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace TSA_WorldDomination
{
    /// <summary>Pick a player colony or outpost as the scheduled post-recruit destination (no immediate transfer).</summary>
    public class Dialog_SchedulePrisonerDestination : Window
    {
        private readonly List<string> thingIds;
        private readonly Action onScheduled;
        private Vector2 scrollPos;
        private string searchTerm = "";
        private List<DestRow> colonyRows = new List<DestRow>();
        private List<DestRow> outpostRows = new List<DestRow>();

        private struct DestRow
        {
            public WorldObject_WD_Outpost outpost;
            public MapParent colony;
            public string label;
            public string subtitle;
            public Texture2D icon;
            public Color iconColor;
            public bool IsColony => colony != null;
        }

        public override Vector2 InitialSize => new Vector2(520f, 560f);

        public Dialog_SchedulePrisonerDestination(List<string> thingIds, Action onScheduled = null)
        {
            this.thingIds = thingIds ?? new List<string>();
            this.onScheduled = onScheduled;
            doCloseX = true;
            closeOnCancel = true;
            absorbInputAroundWindow = true;
            forcePause = false;
            RebuildRows();
        }

        public override void PostClose()
        {
            WdWindowEsc.ClearTextFocusOnClose();
            base.PostClose();
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (WdWindowEsc.TryCloseOnCancel(this))
                return;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 32f), "TSA_WD_Prisoners_ScheduleTitle".Translate());

            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(0f, 34f, inRect.width, 22f),
                "TSA_WD_Prisoners_ScheduleSubtitle".Translate(thingIds.Count.ToString()));

            string oldSearch = searchTerm;
            Rect searchRect = new Rect(0f, 60f, inRect.width, 28f);
            searchTerm = Widgets.TextField(searchRect, searchTerm);
            if (searchTerm != oldSearch) RebuildRows();

            if (string.IsNullOrEmpty(searchTerm))
            {
                GUI.color = new Color(1f, 1f, 1f, 0.4f);
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(searchRect, "  " + "TSA_WD_PawnTransfer_SearchDest".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
            }

            float listY = 96f;
            float clearH = 36f;
            Rect clearRect = new Rect(0f, listY, inRect.width, clearH - 4f);
            if (Widgets.ButtonText(clearRect, "TSA_WD_Prisoners_ClearDestination".Translate()))
            {
                WorldComponent_PrisonerRecruitSchedule.Get()?.ClearMany(thingIds);
                onScheduled?.Invoke();
                Close();
                SoundDefOf.Click.PlayOneShotOnCamera();
                return;
            }

            listY += clearH;
            float listH = inRect.height - listY - 10f;
            float rowH = 44f;
            float contentH = (colonyRows.Count + outpostRows.Count) * rowH + 100f;
            Rect viewRect = new Rect(0f, 0f, inRect.width - 20f, Mathf.Max(contentH, listH));
            Widgets.BeginScrollView(new Rect(0f, listY, inRect.width, listH), ref scrollPos, viewRect);

            float y = 0f;
            DrawSectionHeader(ref y, viewRect.width, "TSA_WD_PawnTransfer_Colonies".Translate());
            if (colonyRows.Count == 0)
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(8f, y, viewRect.width - 16f, 22f), "TSA_WD_PawnTransfer_NoColonies".Translate());
                GUI.color = Color.white;
                y += 26f;
            }
            else
            {
                for (int i = 0; i < colonyRows.Count; i++)
                    DrawRow(ref y, viewRect.width, rowH, colonyRows[i]);
            }

            DrawSectionHeader(ref y, viewRect.width, "TSA_WD_PawnTransfer_Outposts".Translate());
            if (outpostRows.Count == 0)
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(8f, y, viewRect.width - 16f, 22f), "TSA_WD_PawnTransfer_NoOutposts".Translate());
                GUI.color = Color.white;
            }
            else
            {
                for (int i = 0; i < outpostRows.Count; i++)
                    DrawRow(ref y, viewRect.width, rowH, outpostRows[i]);
            }

            Widgets.EndScrollView();
        }

        private void DrawSectionHeader(ref float y, float width, string title)
        {
            Text.Font = GameFont.Small;
            GUI.color = Widgets.SeparatorLabelColor;
            Widgets.Label(new Rect(0f, y, width, 24f), title);
            GUI.color = Color.white;
            y += 26f;
            Widgets.DrawLineHorizontal(0f, y, width);
            y += 6f;
        }

        private void DrawRow(ref float y, float width, float rowH, DestRow row)
        {
            Rect r = new Rect(0f, y, width, rowH - 2f);
            if (Mouse.IsOver(r))
                Widgets.DrawHighlight(r);

            Rect iconRect = new Rect(r.x + 8f, r.y + (r.height - 32f) / 2f, 32f, 32f);
            if (row.icon != null)
            {
                GUI.color = row.iconColor;
                GUI.DrawTexture(iconRect, row.icon, ScaleMode.ScaleToFit);
                GUI.color = Color.white;
            }

            Text.Anchor = TextAnchor.MiddleLeft;
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(iconRect.xMax + 10f, r.y, width - 120f, r.height * 0.55f), row.label);
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(iconRect.xMax + 10f, r.y + r.height * 0.45f, width - 120f, r.height * 0.5f), row.subtitle);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;

            if (Widgets.ButtonInvisible(r))
            {
                var schedule = WorldComponent_PrisonerRecruitSchedule.Get();
                if (row.IsColony)
                    schedule?.SetDestColonyForMany(thingIds, row.colony);
                else
                    schedule?.SetDestForMany(thingIds, row.outpost);
                onScheduled?.Invoke();
                Close();
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            y += rowH;
        }

        private void RebuildRows()
        {
            colonyRows.Clear();
            outpostRows.Clear();
            string searchLower = string.IsNullOrEmpty(searchTerm) ? null : searchTerm.ToLowerInvariant();
            Faction player = Faction.OfPlayer;
            if (player == null) return;

            var settlements = Find.WorldObjects?.Settlements;
            if (settlements != null)
            {
                for (int si = 0; si < settlements.Count; si++)
                {
                    if (settlements[si] is not MapParent mp || mp.Faction != player || !mp.HasMap) continue;
                    string label = PlayerPawnRosterUtility.FormatColonyLabelForDisplay(mp.LabelCap);
                    string typeLabel = "TSA_WD_AllPlayerPawns_LocColony".Translate();
                    if (searchLower != null
                        && !label.ToLowerInvariant().Contains(searchLower)
                        && !mp.LabelCap.ToLowerInvariant().Contains(searchLower)
                        && !typeLabel.ToLowerInvariant().Contains(searchLower))
                        continue;

                    colonyRows.Add(new DestRow
                    {
                        colony = mp,
                        label = label,
                        subtitle = typeLabel,
                        icon = player.def.FactionIcon,
                        iconColor = player.Color
                    });
                }
            }

            var allWo = Find.WorldObjects?.AllWorldObjects;
            if (allWo != null)
            {
                for (int wi = 0; wi < allWo.Count; wi++)
                {
                    if (allWo[wi] is not WorldObject_WD_Outpost outpost || outpost.Faction != player) continue;
                    string label = outpost.LabelCap;
                    string typeLabel = outpost.def?.LabelCap ?? "TSA_WD_AllPlayerPawns_LocOutpost".Translate();
                    if (searchLower != null
                        && !label.ToLowerInvariant().Contains(searchLower)
                        && !typeLabel.ToLowerInvariant().Contains(searchLower))
                        continue;

                    outpostRows.Add(new DestRow
                    {
                        outpost = outpost,
                        label = label,
                        subtitle = typeLabel,
                        icon = outpost.def?.ExpandingIconTexture,
                        iconColor = outpost.Faction?.Color ?? Color.white
                    });
                }
            }

            colonyRows.Sort((a, b) => string.Compare(a.label, b.label, StringComparison.OrdinalIgnoreCase));
            outpostRows.Sort((a, b) => string.Compare(a.label, b.label, StringComparison.OrdinalIgnoreCase));
        }
    }
}
