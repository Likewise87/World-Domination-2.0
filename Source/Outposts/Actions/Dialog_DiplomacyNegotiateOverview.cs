using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace TSA_WorldDomination
{
    /// <summary>Overview of negotiate counterparts: declare war, cease fire, or form an alliance. Existing alliances stay locked.</summary>
    [StaticConstructorOnStartup]
    public class Dialog_DiplomacyNegotiateOverview : Window
    {
        private readonly Faction negotiator;
        private List<DiplomacyNegotiateUtility.CounterpartRow> rows;
        private Vector2 scrollPosition;
        private bool pendingCached;
        private string nameFilter = "";

        private const float RowH = 76f;
        private const float IconSize = 32f;
        private const float HeaderH = 36f;
        private const float ColFaction = 200f;
        private const float ColRelation = 80f;
        private const float ColStrength = 100f;
        private const float ColWarFoes = 100f;
        private const float ColAsk = 90f;
        private const float ColAction = 160f;
        private const float BottomH = 48f;
        private const float CloseBtnH = 40f;
        private const float ActionBtnH = 32f;
        private const float ActionBtnGap = 4f;

        private static Texture2D iconDeclareWar;
        private static Texture2D iconCeaseFire;
        private static Texture2D iconBecomeAlly;

        private static Texture2D IconDeclareWar =>
            iconDeclareWar ??= ContentFinder<Texture2D>.Get("UI/Commands/Launch_Raid", false);
        private static Texture2D IconCeaseFire =>
            iconCeaseFire ??= ContentFinder<Texture2D>.Get("UI/Commands/Neutral", false);
        private static Texture2D IconBecomeAlly =>
            iconBecomeAlly ??= ContentFinder<Texture2D>.Get("UI/Commands/Peace", false);

        private static bool s_labelsInit;
        private static string s_hdrFaction, s_hdrRelation, s_hdrStrength, s_hdrWarFoes, s_hdrAsk, s_hdrAction;
        private static string s_btnWar, s_btnPeace, s_btnAlly, s_btnNone, s_pending, s_cannotAsk;
        private static string s_hdrFactionTip, s_hdrRelationTip, s_hdrStrengthTip, s_hdrWarFoesTip, s_hdrAskTip, s_hdrActionTip;

        public override Vector2 InitialSize => new Vector2(830f, 680f);

        public Dialog_DiplomacyNegotiateOverview(Faction negotiator)
        {
            this.negotiator = negotiator;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            doCloseX = true;
            doCloseButton = false;
            RefreshRows();
        }

        public override void PostClose()
        {
            base.PostClose();
            PawnRosterHeaderFilter.CloseDropdown();
            WdWindowEsc.ClearTextFocusOnClose();
            Window_DiplomacyMatrix.RequestRowActionRebuild();
        }

        private void RefreshRows()
        {
            rows = DiplomacyNegotiateUtility.BuildCounterpartRows(negotiator);
            pendingCached = DiplomacyNegotiateUtility.HasPendingNegotiateForFaction(negotiator);
        }

        private static void EnsureLabels()
        {
            if (s_labelsInit) return;
            s_labelsInit = true;
            s_hdrFaction = "TSA_WD_Negotiate_H_Faction".Translate();
            s_hdrRelation = "TSA_WD_Negotiate_H_Relation".Translate();
            s_hdrStrength = "TSA_WD_Negotiate_H_Strength".Translate();
            s_hdrWarFoes = "TSA_WD_Negotiate_H_WarFoes".Translate();
            s_hdrAsk = "TSA_WD_Negotiate_H_Ask".Translate();
            s_hdrAction = "TSA_WD_Negotiate_H_Action".Translate();
            s_btnWar = "TSA_WD_Negotiate_BtnWar".Translate();
            s_btnPeace = "TSA_WD_Negotiate_BtnPeace".Translate();
            s_btnAlly = "TSA_WD_Negotiate_BtnAlly".Translate();
            s_btnNone = "TSA_WD_Negotiate_BtnNone".Translate();
            s_pending = "TSA_WD_Negotiate_Pending".Translate();
            s_cannotAsk = "TSA_WD_Negotiate_CannotAsk".Translate();
            s_hdrFactionTip = "TSA_WD_Negotiate_AllegianceColorTip".Translate();
            s_hdrRelationTip = "TSA_WD_Negotiate_RelationTip".Translate();
            s_hdrStrengthTip = "TSA_WD_Negotiate_StrengthTip".Translate();
            s_hdrWarFoesTip = "TSA_WD_Negotiate_WarFoesTip".Translate();
            s_hdrAskTip = "TSA_WD_Negotiate_AskTip".Translate();
            s_hdrActionTip = "TSA_WD_Negotiate_ActionTip".Translate();
        }

        private static void LabelAnchored(Rect rect, string text, TextAnchor anchor)
        {
            TextAnchor prev = Text.Anchor;
            Text.Anchor = anchor;
            Widgets.Label(rect, text);
            Text.Anchor = prev;
        }

        private static void DrawHeaderCell(ref float curX, float y, float width, float height, string label, TextAnchor anchor, string tip)
        {
            Rect rect = new Rect(curX, y, width, height);
            bool wrap = Text.WordWrap;
            Text.WordWrap = true;
            LabelAnchored(rect, label, anchor);
            Text.WordWrap = wrap;
            if (!tip.NullOrEmpty())
                TooltipHandler.TipRegion(rect, tip);
            curX += width;
        }

        private void DrawFactionHeader(ref float curX, float y, float width, float height)
        {
            const float gap = 2f;
            float iconSlot = PawnRosterHeaderFilter.FilterIconSize + 4f;
            float labelW = Mathf.Max(8f, width - iconSlot - gap);
            Rect labelRect = new Rect(curX, y, labelW, height);
            bool wrap = Text.WordWrap;
            Text.WordWrap = true;
            LabelAnchored(labelRect, s_hdrFaction, TextAnchor.MiddleCenter);
            Text.WordWrap = wrap;
            if (!s_hdrFactionTip.NullOrEmpty())
                TooltipHandler.TipRegion(new Rect(curX, y, width, height), s_hdrFactionTip);

            float fx = curX + labelW + gap;
            PawnRosterHeaderFilter.DrawFilterableHeader(
                ref fx, y, iconSlot, height,
                "",
                false, false,
                TextAnchor.MiddleCenter,
                !nameFilter.NullOrEmpty(),
                "TSA_WD_FilterByName".Translate(),
                icon => PawnRosterHeaderFilter.OpenTextDropdown(
                    icon,
                    "TSA_WD_FilterByName".Translate(),
                    "TSA_WD_FilterByName".Translate(),
                    () => nameFilter,
                    v => nameFilter = v ?? "",
                    () => nameFilter = ""),
                null);
            curX += width;
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (negotiator == null)
            {
                Close();
                return;
            }

            if (PawnRosterHeaderFilter.TryCloseDropdownOnCancel())
                return;
            if (WdWindowEsc.TryCloseOnCancel(this))
                return;

            EnsureLabels();

            float y = 0f;
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            float titleIcon = 28f;
            WorldDomination_UIUtils.DrawFactionIconWithColor(new Rect(0f, y + 2f, titleIcon, titleIcon), negotiator);
            Widgets.Label(new Rect(titleIcon + 8f, y, inRect.width - titleIcon - 8f, 32f),
                "TSA_WD_Negotiate_OverviewTitle".Translate(negotiator.Name));
            Text.Anchor = TextAnchor.UpperLeft;
            y += 36f;

            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(0f, y, inRect.width, 30f),
                "TSA_WD_Negotiate_OverviewTip".Translate());
            GUI.color = Color.white;
            y += 34f;

            if (pendingCached)
            {
                Text.Font = GameFont.Small;
                GUI.color = Color.yellow;
                LabelAnchored(new Rect(0f, y, inRect.width, 24f), s_pending, TextAnchor.MiddleLeft);
                GUI.color = Color.white;
                y += 28f;
            }

            float tableTop = y;
            float contentW = ColFaction + ColRelation + ColStrength + ColWarFoes + ColAsk + ColAction;
            Rect hRect = new Rect(0f, tableTop, inRect.width, HeaderH);
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            float hx = 0f;
            DrawFactionHeader(ref hx, hRect.y, ColFaction, HeaderH);
            DrawHeaderCell(ref hx, hRect.y, ColRelation, HeaderH, s_hdrRelation, TextAnchor.MiddleCenter, s_hdrRelationTip);
            DrawHeaderCell(ref hx, hRect.y, ColStrength, HeaderH, s_hdrStrength, TextAnchor.MiddleCenter, s_hdrStrengthTip);
            DrawHeaderCell(ref hx, hRect.y, ColWarFoes, HeaderH, s_hdrWarFoes, TextAnchor.MiddleCenter, s_hdrWarFoesTip);
            DrawHeaderCell(ref hx, hRect.y, ColAsk, HeaderH, s_hdrAsk, TextAnchor.MiddleCenter, s_hdrAskTip);
            DrawHeaderCell(ref hx, hRect.y, ColAction, HeaderH, s_hdrAction, TextAnchor.MiddleCenter, s_hdrActionTip);
            GUI.color = Color.white;
            Widgets.DrawLineHorizontal(0f, hRect.yMax, inRect.width);

            float listTop = hRect.yMax + 4f;
            Rect listOut = new Rect(0f, listTop, inRect.width, inRect.height - listTop - BottomH - 8f);
            int visibleCount = 0;
            if (rows != null)
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    if (PassesNameFilter(rows[i])) visibleCount++;
                }
            }
            float viewH = Mathf.Max(listOut.height, visibleCount * RowH + 4f);
            float viewW = Mathf.Max(listOut.width - 16f, contentW);
            Rect view = new Rect(0f, 0f, viewW, viewH);
            Widgets.BeginScrollView(listOut, ref scrollPosition, view);
            if (rows != null)
            {
                int drawIndex = 0;
                for (int i = 0; i < rows.Count; i++)
                {
                    if (!PassesNameFilter(rows[i])) continue;
                    DrawRow(0f, drawIndex * RowH, view.width, rows[i], drawIndex);
                    drawIndex++;
                }
            }
            Widgets.EndScrollView();

            float closeW = inRect.width * 0.4f;
            float closeX = (inRect.width - closeW) * 0.5f;
            float closeY = inRect.height - CloseBtnH;
            if (Widgets.ButtonText(new Rect(closeX, closeY, closeW, CloseBtnH), "Close".Translate()))
                Close();
            PawnRosterHeaderFilter.DrawDropdownIfOpen();
        }

        private bool PassesNameFilter(DiplomacyNegotiateUtility.CounterpartRow row)
        {
            if (nameFilter.NullOrEmpty()) return true;
            string name = row.Target?.Name;
            if (name.NullOrEmpty()) return false;
            return name.IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void DrawRow(float x, float y, float width, DiplomacyNegotiateUtility.CounterpartRow row, int index)
        {
            Rect rowRect = new Rect(x, y, width, RowH);
            if (index % 2 == 0) Widgets.DrawHighlight(rowRect);
            if (Mouse.IsOver(rowRect)) Widgets.DrawLightHighlight(rowRect);

            Faction target = row.Target;
            float curX = x;

            // Faction: icon + colorized name (player allegiance) + type subtitle
            float iconY = y + (RowH - IconSize) * 0.5f;
            Rect iconRect = new Rect(curX + 6f, iconY, IconSize, IconSize);
            if (target != null)
                WorldDomination_UIUtils.DrawFactionIconWithColor(iconRect, target);

            float nameX = iconRect.xMax + 8f;
            float nameW = ColFaction - (nameX - curX) - 4f;
            float nameBlockH = 40f;
            float nameTop = y + (RowH - nameBlockH) * 0.5f;
            Rect nameRect = new Rect(nameX, nameTop, nameW, 24f);
            Text.Font = GameFont.Small;
            string name = target?.Name ?? "?";
            Color nameColor = target != null
                ? WorldDomination_UIUtils.ColorForRelationWithPlayer(target)
                : Color.white;
            LabelAnchored(nameRect, name.Truncate(nameW).Colorize(nameColor), TextAnchor.MiddleLeft);
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            LabelAnchored(new Rect(nameX, nameTop + 24f, nameW, 16f),
                (target?.def.LabelCap ?? "").Truncate(nameW), TextAnchor.MiddleLeft);
            GUI.color = Color.white;
            curX += ColFaction;

            // Relation with negotiator (same GetColor red/green/blue as diplomacy matrix)
            Text.Font = GameFont.Small;
            string rel = row.Relation switch
            {
                FactionRelationKind.Hostile => "Hostile".Translate().ToString(),
                FactionRelationKind.Ally => "Ally".Translate().ToString(),
                _ => "Neutral".Translate().ToString()
            };
            LabelAnchored(new Rect(curX, y, ColRelation, RowH),
                rel.Colorize(row.Relation.GetColor()), TextAnchor.MiddleCenter);
            curX += ColRelation;

            // This faction vs. target faction (negotiator / target)
            string strengthLabel = row.StrengthRatio.ToString("0.00") + "×";
            LabelAnchored(new Rect(curX, y, ColStrength, RowH), strengthLabel, TextAnchor.MiddleCenter);
            curX += ColStrength;

            // This faction vs. combined war-foe strength (N/E). Dash when they have no war foes.
            string warFoesLabel = row.WarEnemyPower <= 0.01f
                ? "-"
                : row.WarFoeRatio.ToString("0.00") + "×";
            LabelAnchored(new Rect(curX, y, ColWarFoes, RowH), warFoesLabel, TextAnchor.MiddleCenter);
            curX += ColWarFoes;

            // Ask / CD / cannot be asked
            Rect askRect = new Rect(curX, y, ColAsk, RowH);
            if (row.FreezeDays > 0.01f)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.55f);
                LabelAnchored(askRect,
                    "TSA_WD_Negotiate_CdShort".Translate(row.FreezeDays.ToString("F0")), TextAnchor.MiddleCenter);
                GUI.color = Color.white;
            }
            else
                DrawAskValues(askRect, row);
            curX += ColAsk;

            DrawActionButtons(new Rect(curX + 4f, y, ColAction - 8f, RowH), row);
        }

        private void DrawAskValues(Rect askRect, DiplomacyNegotiateUtility.CounterpartRow row)
        {
            var offers = row.Offers;
            int n = offers != null ? offers.Count : 0;
            if (n == 0)
            {
                GUI.color = Color.gray;
                LabelAnchored(askRect, s_cannotAsk, TextAnchor.MiddleCenter);
                GUI.color = Color.white;
                if (!row.RejectReason.NullOrEmpty())
                    TooltipHandler.TipRegion(askRect, row.RejectReason);
                return;
            }

            if (n == 1)
            {
                DiplomacyNegotiateUtility.ActionOffer o = offers[0];
                if (o.CanAct)
                    LabelAnchored(askRect, o.AskSilver.ToString("F0"), TextAnchor.MiddleCenter);
                else
                {
                    GUI.color = Color.gray;
                    LabelAnchored(askRect, s_cannotAsk, TextAnchor.MiddleCenter);
                    GUI.color = Color.white;
                    if (!o.RejectReason.NullOrEmpty())
                        TooltipHandler.TipRegion(askRect, o.RejectReason);
                }
                return;
            }

            float lineH = askRect.height / n;
            bool anyCan = false;
            for (int i = 0; i < n; i++)
            {
                DiplomacyNegotiateUtility.ActionOffer o = offers[i];
                Rect line = new Rect(askRect.x, askRect.y + i * lineH, askRect.width, lineH);
                if (o.CanAct)
                {
                    anyCan = true;
                    LabelAnchored(line, o.AskSilver.ToString("F0"), TextAnchor.MiddleCenter);
                }
                else
                {
                    GUI.color = Color.gray;
                    LabelAnchored(line, s_cannotAsk, TextAnchor.MiddleCenter);
                    GUI.color = Color.white;
                    if (!o.RejectReason.NullOrEmpty())
                        TooltipHandler.TipRegion(line, o.RejectReason);
                }
            }
            if (!anyCan && !row.RejectReason.NullOrEmpty())
                TooltipHandler.TipRegion(askRect, row.RejectReason);
        }

        private void DrawActionButtons(Rect col, DiplomacyNegotiateUtility.CounterpartRow row)
        {
            var offers = row.Offers;
            int n = offers != null ? offers.Count : 0;
            if (n <= 0)
            {
                ResolveActionButton(null, out string noneLabel, out Texture2D noneIcon, out Color? noneTint);
                Rect noneBtn = new Rect(col.x, col.y + (col.height - ActionBtnH) * 0.5f, col.width, ActionBtnH);
                GUI.enabled = false;
                WorldDomination_UIUtils.ButtonTextWithIcon(noneBtn, noneIcon, noneLabel, iconTint: noneTint, centerContents: true);
                GUI.enabled = true;
                if (!row.RejectReason.NullOrEmpty())
                    TooltipHandler.TipRegion(noneBtn, row.RejectReason);
                return;
            }

            float stackH = n * ActionBtnH + (n - 1) * ActionBtnGap;
            float top = col.y + (col.height - stackH) * 0.5f;
            for (int i = 0; i < n; i++)
            {
                DiplomacyNegotiateUtility.ActionOffer offer = offers[i];
                ResolveActionButton(offer.Action, out string btnLabel, out Texture2D btnIcon, out Color? btnTint);
                Rect btn = new Rect(col.x, top + i * (ActionBtnH + ActionBtnGap), col.width, ActionBtnH);
                bool canClick = offer.CanAct && !pendingCached && row.FreezeDays <= 0.01f;
                GUI.enabled = canClick;
                if (WorldDomination_UIUtils.ButtonTextWithIcon(btn, btnIcon, btnLabel, iconTint: btnTint, centerContents: true)
                    && canClick)
                {
                    Find.WindowStack.Add(new Dialog_DiplomacyNegotiateDeal(
                        negotiator, row.Target, offer.Action, offer.AskSilver));
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }
                GUI.enabled = true;
                if (pendingCached)
                    TooltipHandler.TipRegion(btn, s_pending);
                else if (!offer.CanAct && !offer.RejectReason.NullOrEmpty())
                    TooltipHandler.TipRegion(btn, offer.RejectReason);
                else if (row.FreezeDays > 0.01f)
                    TooltipHandler.TipRegion(btn, "TSA_WD_Negotiate_PairFrozen".Translate(row.FreezeDays.ToString("F0")));
            }
        }

        private static void ResolveActionButton(
            DiplomacyNegotiateAction? action,
            out string label,
            out Texture2D icon,
            out Color? iconTint)
        {
            switch (action)
            {
                case DiplomacyNegotiateAction.DeclareWar:
                    label = s_btnWar;
                    icon = IconDeclareWar;
                    iconTint = null;
                    return;
                case DiplomacyNegotiateAction.BecomeNeutral:
                    label = s_btnPeace;
                    icon = IconCeaseFire;
                    iconTint = FactionRelationKind.Neutral.GetColor();
                    return;
                case DiplomacyNegotiateAction.BecomeAlly:
                    label = s_btnAlly;
                    icon = IconBecomeAlly;
                    iconTint = FactionRelationKind.Ally.GetColor();
                    return;
                default:
                    label = s_btnNone;
                    icon = null;
                    iconTint = null;
                    return;
            }
        }
    }
}
