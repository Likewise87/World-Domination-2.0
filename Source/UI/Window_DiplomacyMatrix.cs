using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;
using RimWorld.Planet;
using Verse.Sound;

namespace TSA_WorldDomination
{
    public class Window_DiplomacyMatrix : Window
    {
        private Vector2 scrollPos;
        public override Vector2 InitialSize => new Vector2(UI.screenWidth, UI.screenHeight);

        // Session memory: survive close/reopen (same pattern as Window_AllPlayerPawns / Window_Prisoners).
        private const string DefaultSortColumn = "Name";
        private const bool DefaultSortAscending = true;
        private static string sortColumn = DefaultSortColumn;
        private static bool sortAscending = DefaultSortAscending;
        private static string searchTerm = "";

        private List<Faction> cachedFactions;
        private int lastUpdateTick = -9999;
        private static bool s_requestActionRebuild;

        private Dictionary<Faction, RelationLists> diplomacyRelationsByFaction;
        private List<float> diplomacyRowHeights;
        private Dictionary<Faction, int> diplomacyGoodwillByFaction;
        private Dictionary<Faction, string> diplomacyGoodwillLabelByFaction;
        private Dictionary<Faction, FactionRowActionCache> diplomacyRowActionsByFaction;
        private WorldComponent_SpreadManager cachedSpreadManager;

        private static bool s_diplomacyHeadersInit;
        private static string s_headerHostile, s_headerNeutral, s_headerAlly, s_headerGoodwill;
        private static string s_title, s_filterPlaceholder, s_wdCooldownTip, s_headerFactionName;
        private static string s_btnDetails, s_btnNegotiate, s_negotiatePendingTip;
        private static string s_orderedRoadActiveTip, s_buyActiveTip;

        private struct RelationLists
        {
            public List<Faction> Hostile;
            public List<Faction> Neutral;
            public List<Faction> Ally;
        }

        private struct FactionRowActionCache
        {
            public bool ShowNegotiate;
            public bool NegotiateOpenOk;
            public string NegotiateDisabledReason;
            public bool HasOrderedRoad;
            public bool HasActiveBuy;
            public bool HasPendingNegotiate;
        }

        // Left column is vertically centered: Details bottom is ~45px below row mid
        // (name 25 + type 20 + gaps + button 22). Need DetailsBottomPad under the button.
        private const float DetailsBottomPad = 8f;
        private const float DetailsBottomFromRowMid = 45f;
        private const float MinRowHeight = (DetailsBottomFromRowMid + DetailsBottomPad) * 2f; // 106
        /// <summary>Relation-column row pitch; matches dashboard list rows so Tiny labels are not cropped.</summary>
        private const float LineHeight = 26f;
        /// <summary>UI_WINDOWS minima for single-line Labels.</summary>
        private const float TinyLabelHeight = 15f;
        private const float SmallLabelHeight = 24f;
        private const float ColFaction = 210f; // was 300; shrink 30%
        private const float ColGoodwill = 100f; // was 110; -10px
        private const float MinSubColWidth = 140f;
        private const float MinColRel = MinSubColWidth * 2f;
        private const float ColPad = 20f;

        public Window_DiplomacyMatrix()
        {
            this.doCloseX = true;
            this.draggable = false;
            this.forcePause = true;
            this.absorbInputAroundWindow = true;
            this.closeOnCancel = true;
        }

        /// <summary>Call when negotiate/buy travelers change while this window may stay open under pause.</summary>
        public static void RequestRowActionRebuild() => s_requestActionRebuild = true;

        public override void PostClose()
        {
            base.PostClose();
            PawnRosterHeaderFilter.CloseDropdown();
            WdWindowEsc.ClearTextFocusOnClose();
        }

        public override void DoWindowContents(Rect inRect)
        {
            WdNavWindows.ProcessHotkeys();
            if (!IsOpen) return;
            if (PawnRosterHeaderFilter.TryCloseDropdownOnCancel())
                return;
            if (WdWindowEsc.TryCloseOnCancel(this))
                return;

            if (!s_diplomacyHeadersInit)
            {
                s_diplomacyHeadersInit = true;
                s_headerHostile = "TSA_WD_Diplomacy_HostileTo".Translate();
                s_headerNeutral = "TSA_WD_Diplomacy_NeutralWith".Translate();
                s_headerAlly = "TSA_WD_Diplomacy_AllyOf".Translate();
                s_headerGoodwill = "TSA_WD_Diplomacy_H_Goodwill".Translate();
                s_title = "TSA_WD_DiplomacyMatrix_Title".Translate();
                s_headerFactionName = "TSA_WD_Stats_H_Faction".Translate();
                s_filterPlaceholder = "TSA_WD_FilterByName".Translate();
                s_wdCooldownTip = "TSA_WD_Diplomacy_WdCooldownTip".Translate();
                s_btnDetails = "TSA_WD_Log_BtnDetails".Translate();
                s_btnNegotiate = "TSA_WD_Negotiate_Btn".Translate();
                s_negotiatePendingTip = "TSA_WD_Negotiate_Pending".Translate();
                s_orderedRoadActiveTip = "TSA_WD_OrderedRoad_FactionActiveTooltip".Translate();
                s_buyActiveTip = "TSA_WD_BuySettlement_FactionActiveTooltip".Translate();
            }

            float viewportW = inRect.width;
            float minContentW = ColFaction + ColGoodwill + (3f * MinColRel) + ColPad;
            float contentWidth = Mathf.Max(viewportW - 16f, minContentW);
            float leftover = contentWidth - ColFaction - ColGoodwill - ColPad;
            float colRel = leftover / 3f;

            Text.Font = GameFont.Medium;
            float titleH = 35f;
            float btn = WorldDomination_UIUtils.RosterIconBtnSize;
            float neutralCenterX = ColFaction + ColGoodwill + colRel * 1.5f;
            float restoreX = neutralCenterX - btn * 0.5f;
            Widgets.Label(new Rect(0, 0, Mathf.Max(80f, restoreX - 8f), titleH), s_title);
            WorldDomination_UIUtils.DrawTitleRestoreDefaultViewAt(restoreX, titleH, RestoreDefaultView);

            float tinyH = Mathf.Max(TinyLabelHeight, Text.LineHeightOf(GameFont.Tiny));
            float headerH = Mathf.Max(30f, tinyH);

            // Sticky headers: fixed vertically, X synced to body horizontal scroll.
            Text.Font = GameFont.Tiny;
            Rect hRect = new Rect(0, 40f, inRect.width, headerH);
            GUI.color = Color.gray;
            float factionHdrX = -scrollPos.x;
            PawnRosterHeaderFilter.DrawFilterableHeader(
                ref factionHdrX, hRect.y, ColFaction, headerH,
                s_headerFactionName,
                sortColumn == "Name", sortAscending,
                TextAnchor.MiddleCenter,
                !searchTerm.NullOrEmpty(),
                s_filterPlaceholder,
                icon => PawnRosterHeaderFilter.OpenTextDropdown(
                    icon,
                    s_filterPlaceholder,
                    s_filterPlaceholder,
                    () => searchTerm,
                    v => { searchTerm = v ?? ""; lastUpdateTick = -9999; },
                    () => { searchTerm = ""; lastUpdateTick = -9999; }),
                () => SetSort("Name"));
            float curX = ColFaction - scrollPos.x;
            GUI.color = Color.gray;
            PawnRosterHeaderFilter.DrawFilterableHeader(
                ref curX, hRect.y, ColGoodwill, headerH,
                s_headerGoodwill, sortColumn == "Goodwill", sortAscending,
                TextAnchor.MiddleCenter, false, null, null, () => SetSort("Goodwill"));
            GUI.color = FactionRelationKind.Hostile.GetColor();
            PawnRosterHeaderFilter.DrawFilterableHeader(
                ref curX, hRect.y, colRel, headerH,
                s_headerHostile, sortColumn == "Hostile", sortAscending,
                TextAnchor.MiddleCenter, false, null, null, () => SetSort("Hostile"));
            GUI.color = FactionRelationKind.Neutral.GetColor();
            PawnRosterHeaderFilter.DrawFilterableHeader(
                ref curX, hRect.y, colRel, headerH,
                s_headerNeutral, sortColumn == "Neutral", sortAscending,
                TextAnchor.MiddleCenter, false, null, null, () => SetSort("Neutral"));
            GUI.color = FactionRelationKind.Ally.GetColor();
            PawnRosterHeaderFilter.DrawFilterableHeader(
                ref curX, hRect.y, colRel, headerH,
                s_headerAlly, sortColumn == "Ally", sortAscending,
                TextAnchor.MiddleCenter, false, null, null, () => SetSort("Ally"));
            GUI.color = Color.white;
            Widgets.DrawLineHorizontal(0, hRect.yMax, inRect.width);
            // Column borders: start of Hostile, then Neutral / Ally (X synced to body scroll).
            DrawRelationColumnSeparators(ColFaction + ColGoodwill - scrollPos.x, colRel, hRect.y, hRect.height);

            if (Find.TickManager.TicksGame >= lastUpdateTick + 300 || cachedFactions == null)
            {
                if (cachedFactions == null) cachedFactions = new List<Faction>();
                else cachedFactions.Clear();

                Faction playerFaction = null;
                foreach (Faction f in Find.FactionManager.AllFactionsVisible)
                {
                    if (f == null || f.def == null || f.def.hidden) continue;
                    if (!f.IsPlayer && WorldActions_Utils.IsExcludedFaction(f)) continue;
                    if (!string.IsNullOrEmpty(searchTerm))
                    {
                        if (f.Name.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) < 0 &&
                            f.def.LabelCap.ToString().IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) < 0)
                            continue;
                    }
                    if (f.IsPlayer && string.IsNullOrEmpty(searchTerm))
                        playerFaction = f;
                    else
                        cachedFactions.Add(f);
                }
                RebuildRelationLists(cachedFactions, playerFaction);
                SortFactionList(cachedFactions);
                if (playerFaction != null) cachedFactions.Insert(0, playerFaction);
                cachedSpreadManager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
                lastUpdateTick = Find.TickManager.TicksGame;
                RebuildDiplomacyRowData();
                s_requestActionRebuild = false;
            }
            else if (s_requestActionRebuild)
            {
                s_requestActionRebuild = false;
                RebuildDiplomacyRowData();
            }

            float totalContentHeight = 0f;
            if (diplomacyRowHeights != null)
            {
                for (int ri = 0; ri < diplomacyRowHeights.Count; ri++)
                    totalContentHeight += diplomacyRowHeights[ri];
            }

            Rect scrollOuter = new Rect(0, hRect.yMax + 5f, inRect.width, inRect.height - (hRect.yMax + 5f) - 30f);
            Rect viewRect = new Rect(0, 0, contentWidth, Mathf.Max(totalContentHeight, scrollOuter.height - 1f));
            Widgets.BeginScrollView(scrollOuter, ref scrollPos, viewRect);

            float currentY = 0f;
            for (int i = 0; i < cachedFactions.Count; i++)
            {
                Faction f = cachedFactions[i];
                float h = diplomacyRowHeights[i];
                Rect row = new Rect(0, currentY, viewRect.width, h);

                if (f == Faction.OfPlayer)
                    Widgets.DrawBoxSolid(row, new Color(0.2f, 0.5f, 0.8f, 0.15f));
                else if (i % 2 == 0)
                    Widgets.DrawHighlight(row);
                if (Mouse.IsOver(row))
                    Widgets.DrawLightHighlight(row);

                Text.Anchor = TextAnchor.MiddleLeft;
                float iconY = currentY + Mathf.Max(7f, (h - 40f) * 0.5f);
                Rect iconRect = new Rect(row.x + 10f, iconY, 40, 40);
                WorldDomination_UIUtils.DrawFactionIconWithColor(iconRect, f);

                float smallH = Mathf.Max(SmallLabelHeight, Text.LineHeightOf(GameFont.Small));
                float typeH = Mathf.Max(TinyLabelHeight, Text.LineHeightOf(GameFont.Tiny));
                bool showDefeated = f != null && f.defeated && !f.IsPlayer;
                float identityH = smallH + typeH + (showDefeated ? typeH : 0f);
                float buttonsH = !f.IsPlayer ? 24f : 0f;
                float leftBlockH = identityH + (buttonsH > 0f ? buttonsH : 0f);
                float leftTop = currentY + Mathf.Max(0f, (h - leftBlockH) * 0.5f);

                Rect nameRect = new Rect(iconRect.xMax + 10f, leftTop, ColFaction - 70f, smallH);
                Text.Font = GameFont.Small;
                string nameLabel = f.Name.Colorize(f == Faction.OfPlayer ? Color.cyan : Color.white);
                if (Widgets.ButtonText(nameRect, nameLabel, false, true, true))
                {
                    searchTerm = f.Name;
                    lastUpdateTick = -9999;
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }

                Rect typeRect = new Rect(nameRect.x, nameRect.yMax, nameRect.width, typeH);
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = Color.gray;
                Widgets.Label(typeRect, f.def.LabelCap);
                GUI.color = Color.white;

                Rect afterTypeRect = typeRect;
                if (showDefeated)
                {
                    Rect defeatedRect = new Rect(nameRect.x, typeRect.yMax, nameRect.width, typeH);
                    Text.Font = GameFont.Tiny;
                    Text.Anchor = TextAnchor.MiddleLeft;
                    GUI.color = Color.red;
                    Widgets.Label(defeatedRect, "TSA_WD_Stats_FactionDefeated".Translate());
                    GUI.color = Color.white;
                    TooltipHandler.TipRegion(defeatedRect, "TSA_WD_Stats_FactionDefeatedTip".Translate());
                    afterTypeRect = defeatedRect;
                }

                if (!f.IsPlayer)
                {
                    float btnY = afterTypeRect.yMax + 2f;
                    Rect detailsBtn = new Rect(nameRect.x, btnY, 68f, 22f);
                    if (Widgets.ButtonText(detailsBtn, s_btnDetails))
                    {
                        Find.WindowStack.Add(new Window_FactionDetails(f));
                        SoundDefOf.Click.PlayOneShotOnCamera();
                    }

                    diplomacyRowActionsByFaction.TryGetValue(f, out FactionRowActionCache actions);
                    Rect negotiateBtn = new Rect(detailsBtn.xMax + 6f, btnY, 83f, 22f);
                    if (actions.ShowNegotiate)
                    {
                        GUI.enabled = actions.NegotiateOpenOk;
                        if (Widgets.ButtonText(negotiateBtn, s_btnNegotiate) && actions.NegotiateOpenOk)
                        {
                            Find.WindowStack.Add(new Dialog_DiplomacyNegotiateOverview(f));
                            SoundDefOf.Click.PlayOneShotOnCamera();
                        }
                        GUI.enabled = true;
                        if (!actions.NegotiateOpenOk && !actions.NegotiateDisabledReason.NullOrEmpty())
                            TooltipHandler.TipRegion(negotiateBtn, actions.NegotiateDisabledReason);
                    }

                    float iconX = (actions.ShowNegotiate ? negotiateBtn.xMax : detailsBtn.xMax) + 6f;
                    if (actions.HasOrderedRoad)
                    {
                        Rect roadIconRect = new Rect(iconX, btnY, 22f, 22f);
                        GUI.DrawTexture(roadIconRect, Action_Settlement_OrderRoad.BuildRoadIcon);
                        TooltipHandler.TipRegion(roadIconRect, s_orderedRoadActiveTip);
                        iconX += 28f;
                    }

                    if (actions.HasActiveBuy)
                    {
                        Rect buyIconRect = new Rect(iconX, btnY, 22f, 22f);
                        GUI.DrawTexture(buyIconRect, Action_Settlement_Buy.BuyIcon);
                        TooltipHandler.TipRegion(buyIconRect, s_buyActiveTip);
                        iconX += 28f;
                    }

                    if (actions.HasPendingNegotiate)
                    {
                        Rect negIconRect = new Rect(iconX, btnY, 22f, 22f);
                        GUI.color = Color.cyan;
                        Widgets.DrawBoxSolid(negIconRect, new Color(0.2f, 0.5f, 0.7f, 0.85f));
                        GUI.color = Color.white;
                        TooltipHandler.TipRegion(negIconRect, s_negotiatePendingTip);
                    }
                }

                diplomacyRelationsByFaction.TryGetValue(f, out RelationLists rel);
                float rX = ColFaction;

                Rect goodwillRect = new Rect(rX, currentY, ColGoodwill, h);
                Text.Anchor = TextAnchor.MiddleCenter;
                if (f != null && !f.IsPlayer)
                {
                    Text.Font = GameFont.Medium;
                    if (diplomacyGoodwillLabelByFaction == null || !diplomacyGoodwillLabelByFaction.TryGetValue(f, out string gwLbl))
                    {
                        int gw = GoodwillChangeNotifier.GetPlayerGoodwill(f);
                        gwLbl = FormatGoodwillLabel(gw).Colorize(ColorForRelationWithPlayer(f));
                    }
                    Widgets.Label(goodwillRect, gwLbl);
                    if (Mouse.IsOver(goodwillRect))
                    {
                        int gw = 0;
                        diplomacyGoodwillByFaction?.TryGetValue(f, out gw);
                        TooltipHandler.TipRegion(goodwillRect, "TSA_WD_Diplomacy_GoodwillTip".Translate(gw));
                    }
                }
                else
                {
                    Text.Font = GameFont.Tiny;
                    GUI.color = new Color(1f, 1f, 1f, 0.3f);
                    Widgets.Label(goodwillRect, "-");
                    GUI.color = Color.white;
                }
                rX += ColGoodwill;

                DrawRelationList(new Rect(rX, currentY + 5f, colRel, h - 10f), rel.Hostile, f);
                rX += colRel;
                DrawRelationList(new Rect(rX, currentY + 5f, colRel, h - 10f), rel.Neutral, f);
                rX += colRel;
                DrawRelationList(new Rect(rX, currentY + 5f, colRel, h - 10f), rel.Ally, f);

                Text.Anchor = TextAnchor.UpperLeft;
                currentY += h;
            }

            DrawRelationColumnSeparators(ColFaction + ColGoodwill, colRel, 0f, Mathf.Max(totalContentHeight, scrollOuter.height - 1f));

            Widgets.EndScrollView();
            Text.Anchor = TextAnchor.UpperLeft;
            PawnRosterHeaderFilter.DrawDropdownIfOpen();
        }

        private void RestoreDefaultView()
        {
            sortColumn = DefaultSortColumn;
            sortAscending = DefaultSortAscending;
            searchTerm = "";
            scrollPos = Vector2.zero;
            lastUpdateTick = -9999;
            PawnRosterHeaderFilter.CloseDropdown();
        }

        private void SetSort(string tag)
        {
            if (sortColumn == tag) sortAscending = !sortAscending;
            else { sortColumn = tag; sortAscending = true; }
            lastUpdateTick = -9999;
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        /// <summary>Vertical borders at Hostile start and between Neutral / Ally.</summary>
        private static void DrawRelationColumnSeparators(float hostileStartX, float colRel, float y, float height)
        {
            if (height <= 0f) return;
            Color prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.4f);
            Widgets.DrawLineVertical(hostileStartX, y, height);
            Widgets.DrawLineVertical(hostileStartX + colRel, y, height);
            Widgets.DrawLineVertical(hostileStartX + colRel * 2f, y, height);
            GUI.color = prev;
        }

        private void RebuildDiplomacyRowData()
        {
            if (diplomacyRowHeights == null)
                diplomacyRowHeights = new List<float>();
            else
                diplomacyRowHeights.Clear();
            if (diplomacyGoodwillByFaction == null)
                diplomacyGoodwillByFaction = new Dictionary<Faction, int>();
            else
                diplomacyGoodwillByFaction.Clear();
            if (diplomacyGoodwillLabelByFaction == null)
                diplomacyGoodwillLabelByFaction = new Dictionary<Faction, string>();
            else
                diplomacyGoodwillLabelByFaction.Clear();
            if (diplomacyRowActionsByFaction == null)
                diplomacyRowActionsByFaction = new Dictionary<Faction, FactionRowActionCache>();
            else
                diplomacyRowActionsByFaction.Clear();
            if (cachedFactions == null) return;

            HashSet<Faction> pendingNegotiate = BuildPendingNegotiateFactions();
            HashSet<Faction> activeBuy = BuildActiveBuyFactions();
            HashSet<Faction> orderedRoad = BuildOrderedRoadFactions();

            foreach (Faction f in cachedFactions)
            {
                diplomacyRelationsByFaction.TryGetValue(f, out RelationLists rel);
                int hc = rel.Hostile?.Count ?? 0;
                int nc = rel.Neutral?.Count ?? 0;
                int ac = rel.Ally?.Count ?? 0;
                int maxCount = Mathf.Max(hc, Mathf.Max(nc, ac));
                int rowLines = Mathf.Max(1, Mathf.CeilToInt(maxCount / 2f));
                float rowH = Mathf.Max(MinRowHeight, rowLines * LineHeight + 15f);
                if (f != null && f.defeated && !f.IsPlayer)
                    rowH += Mathf.Max(TinyLabelHeight, Text.LineHeightOf(GameFont.Tiny));
                diplomacyRowHeights.Add(rowH);
                if (f == null || f.IsPlayer) continue;

                int gw = GoodwillChangeNotifier.GetPlayerGoodwill(f);
                diplomacyGoodwillByFaction[f] = gw;
                diplomacyGoodwillLabelByFaction[f] = FormatGoodwillLabel(gw).Colorize(ColorForRelationWithPlayer(f));

                var actions = new FactionRowActionCache
                {
                    ShowNegotiate = !f.defeated
                        && DiplomacyNegotiateUtility.IsFeatureEnabled
                        && !WorldActions_Utils.IsExcludedFaction(f)
                        && SettlementBuyUtility.IsEligibleSellerRelation(f),
                    HasOrderedRoad = orderedRoad.Contains(f),
                    HasActiveBuy = activeBuy.Contains(f),
                    HasPendingNegotiate = pendingNegotiate.Contains(f)
                };
                if (actions.ShowNegotiate)
                {
                    actions.NegotiateOpenOk = DiplomacyNegotiateUtility.CanOpenNegotiate(f, out string negReason);
                    actions.NegotiateDisabledReason = negReason;
                }
                diplomacyRowActionsByFaction[f] = actions;
            }
        }

        private static HashSet<Faction> BuildPendingNegotiateFactions()
        {
            var set = new HashSet<Faction>();
            if (Find.WorldObjects == null) return set;
            var all = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is WorldObject_Traveler_DiplomacyNegotiate n
                    && !n.Destroyed
                    && !n.completed
                    && n.negotiatorFaction != null)
                    set.Add(n.negotiatorFaction);
            }
            return set;
        }

        private static HashSet<Faction> BuildActiveBuyFactions()
        {
            var set = new HashSet<Faction>();
            if (Find.WorldObjects == null) return set;
            var all = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < all.Count; i++)
            {
                if (!(all[i] is WorldObject_Traveler_SettlementBuy buy) || buy.Destroyed || buy.completed)
                    continue;
                if (buy.sellerFaction != null)
                    set.Add(buy.sellerFaction);
                if (buy.targetObject is Settlement s && s.Faction != null)
                    set.Add(s.Faction);
            }
            return set;
        }

        private static HashSet<Faction> BuildOrderedRoadFactions()
        {
            var set = new HashSet<Faction>();
            var settlements = Find.WorldObjects?.Settlements;
            if (settlements == null) return set;
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement s = settlements[i];
                if (s?.Faction == null || s.Faction.IsPlayer) continue;
                if (s.GetComponent<CompViralSpread>() is CompViralSpread comp && comp.HasActivePlayerOrderedRoadProject)
                    set.Add(s.Faction);
            }
            return set;
        }

        private static string FormatGoodwillLabel(int goodwill)
        {
            string sign = goodwill > 0 ? "+" : "";
            return sign + goodwill.ToString();
        }

        /// <summary>Same colors as vanilla faction/diplomacy UI (Hostile red, Neutral cyan-blue, Ally green).</summary>
        private static Color ColorForRelationWithPlayer(Faction faction) =>
            WorldDomination_UIUtils.ColorForRelationWithPlayer(faction);

        private void DrawRelationList(Rect rect, List<Faction> others, Faction rowFaction)
        {
            if (others == null || others.Count == 0)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.3f);
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.Font = GameFont.Tiny;
                Rect leftAlignedRect = new Rect(rect.x + 6f, rect.y, (rect.width * 0.5f) - 6f, rect.height);
                Widgets.Label(leftAlignedRect, "---");
                GUI.color = Color.white;
                return;
            }

            float half = rect.width * 0.5f;
            for (int i = 0; i < others.Count; i++)
            {
                int col = i % 2;
                int row = i / 2;
                Rect lineRect = new Rect(rect.x + (col * half) + 4f, rect.y + (row * LineHeight), half - 8f, LineHeight);
                Faction other = others[i];

                if (Widgets.ButtonInvisible(lineRect))
                {
                    searchTerm = other.Name;
                    lastUpdateTick = -9999;
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }

                if (Mouse.IsOver(lineRect)) Widgets.DrawHighlight(lineRect);

                GUI.color = other.Color;
                float iconSz = 16f;
                GUI.DrawTexture(new Rect(lineRect.x, lineRect.y + (LineHeight - iconSz) * 0.5f, iconSz, iconSz), other.def.FactionIcon);
                GUI.color = Color.white;

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                float textW = lineRect.width - 22f;
                WorldComponent_SpreadManager spread = cachedSpreadManager
                    ?? Find.World?.GetComponent<WorldComponent_SpreadManager>();

                // Name: ≤16 chars as-is, else 13 + "...". CD / bribe suffixes append after.
                bool showWdCd = WorldActions_DiplomacyBuffsNerfs.TryGetDiplomacyFreezeDaysRemaining(
                    rowFaction, other, spread, out float daysLeft);
                string wdCdPlain = null;
                if (showWdCd)
                {
                    int cdDays = Mathf.Max(0, Mathf.CeilToInt(daysLeft));
                    wdCdPlain = " " + "TSA_WD_Diplomacy_WdCooldown".Translate(cdDays);
                }

                string bribePlain = null;
                if (other.IsPlayer && spread != null
                    && spread.TryGetPlayerBribeCeasefireDaysRemaining(rowFaction, out float bribeDays))
                    bribePlain = " " + "TSA_WD_Diplomacy_BribeCeasefire".Translate(bribeDays.ToString("F1"));
                else if (rowFaction != null && rowFaction.IsPlayer && spread != null
                    && spread.TryGetPlayerBribeCeasefireDaysRemaining(other, out float bribeDaysFromPlayer))
                    bribePlain = " " + "TSA_WD_Diplomacy_BribeCeasefire".Translate(bribeDaysFromPlayer.ToString("F1"));

                string nameShown = TruncateFactionNameChars(other.Name);
                string label = nameShown.Colorize(other.Color);
                Color suffixColor = new Color(1f, 1f, 1f, 0.75f);
                if (wdCdPlain != null)
                    label += wdCdPlain.Colorize(suffixColor);
                if (bribePlain != null)
                    label += bribePlain.Colorize(suffixColor);
                Widgets.Label(new Rect(lineRect.x + 22, lineRect.y, textW, LineHeight), label);

                if (showWdCd)
                    TooltipHandler.TipRegion(lineRect, s_wdCooldownTip);
            }
        }

        /// <summary>Max 16 characters; longer names become 13 chars + "...".</summary>
        private static string TruncateFactionNameChars(string name)
        {
            if (name.NullOrEmpty()) return name;
            if (name.Length <= 16) return name;
            return name.Substring(0, 13) + "...";
        }

        private void SortFactionList(List<Faction> list)
        {
            list.Sort((a, b) =>
            {
                int cmp = sortColumn switch
                {
                    "Name" => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
                    "Hostile" => GetCachedRelationCount(a, FactionRelationKind.Hostile).CompareTo(GetCachedRelationCount(b, FactionRelationKind.Hostile)),
                    "Neutral" => GetCachedRelationCount(a, FactionRelationKind.Neutral).CompareTo(GetCachedRelationCount(b, FactionRelationKind.Neutral)),
                    "Ally" => GetCachedRelationCount(a, FactionRelationKind.Ally).CompareTo(GetCachedRelationCount(b, FactionRelationKind.Ally)),
                    "Goodwill" => GoodwillChangeNotifier.GetPlayerGoodwill(a).CompareTo(GoodwillChangeNotifier.GetPlayerGoodwill(b)),
                    _ => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase)
                };
                return sortAscending ? cmp : -cmp;
            });
        }

        private int GetCachedRelationCount(Faction f, FactionRelationKind kind)
        {
            if (diplomacyRelationsByFaction == null || !diplomacyRelationsByFaction.TryGetValue(f, out RelationLists rel))
                return 0;
            return kind switch
            {
                FactionRelationKind.Hostile => rel.Hostile?.Count ?? 0,
                FactionRelationKind.Neutral => rel.Neutral?.Count ?? 0,
                FactionRelationKind.Ally => rel.Ally?.Count ?? 0,
                _ => 0
            };
        }

        private void RebuildRelationLists(List<Faction> factions, Faction playerFaction)
        {
            if (diplomacyRelationsByFaction == null)
                diplomacyRelationsByFaction = new Dictionary<Faction, RelationLists>();
            else
                diplomacyRelationsByFaction.Clear();
            var allVisible = new List<Faction>();
            foreach (Faction f in Find.FactionManager.AllFactionsVisible)
            {
                if (!f.def.hidden) allVisible.Add(f);
            }
            for (int i = 0; i < factions.Count; i++)
                BuildRelationListsFor(factions[i], allVisible);
            if (playerFaction != null)
                BuildRelationListsFor(playerFaction, allVisible);
        }

        private void BuildRelationListsFor(Faction f, List<Faction> allVisible)
        {
            var hostile = new List<Faction>();
            var neutral = new List<Faction>();
            var ally = new List<Faction>();
            for (int i = 0; i < allVisible.Count; i++)
            {
                Faction other = allVisible[i];
                if (other == f) continue;
                FactionRelationKind rk = WorldActions_Utils.SafeRelationKindWith(f, other);
                if (rk == FactionRelationKind.Hostile) hostile.Add(other);
                else if (rk == FactionRelationKind.Neutral) neutral.Add(other);
                else if (rk == FactionRelationKind.Ally) ally.Add(other);
            }
            diplomacyRelationsByFaction[f] = new RelationLists { Hostile = hostile, Neutral = neutral, Ally = ally };
        }
    }
}
