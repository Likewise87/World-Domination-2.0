using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;
using RimWorld.Planet;
using Verse.Sound;

namespace TSA_WorldDomination
{
    [StaticConstructorOnStartup]
    public class Window_FactionDetails : Window
    {
        private readonly Faction faction;
        private Vector2 scrollPos;
        private string sortColumn = "Strength";
        private bool sortAscending;
        private string typeFilter = "";
        private string nameFilter = "";
        private int lastUpdateTick = -9999;
        private const int UpdateIntervalTicks = 300;

        private readonly List<FactionDetailEntry> cachedList = new List<FactionDetailEntry>();
        private readonly string cachedTitle;
        private readonly string cachedViewBtnLabel;
        private readonly string cachedRoadHeader;
        private readonly string cachedBuyPendingTip;
        private readonly string cachedFilterByTypeHint;
        private readonly string cachedFilterByNameHint;
        private readonly string cachedHdrTier;
        private readonly string cachedHdrType;
        private readonly string cachedHdrName;
        private readonly string cachedHdrStrength;
        private readonly string cachedHdrDist;
        private readonly string cachedDistTip;
        private readonly string cachedStrengthTip;
        private static Texture2D facDetRoadBarTex;

        private const float BuyIconSize = 22f;
        private const float RowHeight = 34f;
        private const float HeaderHeight = 30f;
        private const float FilterHeight = 25f;

        private const float ColTier = 48f;
        private const float ColType = 145f;
        private const float ColName = 171f;
        private const float ColStr = 72f;
        private const float ColDist = 64f;
        private const float ColRoad = 175f;
        private const float ColView = 88f;
        private const float TableWidth = ColTier + ColType + ColName + ColStr + ColDist + ColRoad + ColView;
        /// <summary>Reserved inside the scroll view so the vertical bar does not cover the View column.</summary>
        private const float ScrollbarPad = 16f;
        /// <summary>RimWorld Window default margin is 18 on each side.</summary>
        private const float WindowChromeX = 36f;

        private class FactionDetailEntry
        {
            public WorldObject Obj;
            public CompViralSpread Comp;
            public string TierDisplay;
            public string TypeDisplay;
            public string NameDisplay;
            public string StrengthDisplay;
            public float Strength;
            public int Distance;
            public string DistanceDisplay;
            public bool BeingPurchased;
            public int SortTier;
        }

        static Window_FactionDetails()
        {
            facDetRoadBarTex = SolidColorMaterials.NewSolidColorTexture(new Color(0.4f, 0.6f, 0.9f));
        }

        public override Vector2 InitialSize => new Vector2(TableWidth + WindowChromeX + ScrollbarPad + 10f, 640f);

        public Window_FactionDetails(Faction faction)
        {
            this.faction = faction;
            doCloseX = true;
            draggable = true;
            closeOnCancel = true;
            cachedTitle = "TSA_WD_FacDet_Title".Translate(faction.Name);
            cachedViewBtnLabel = "TSA_WD_FacDet_BtnView".Translate();
            cachedRoadHeader = "TSA_WD_FacDet_HeaderRoadForPlayer".Translate();
            cachedBuyPendingTip = "TSA_WD_BuySettlement_Pending".Translate();
            cachedFilterByTypeHint = "TSA_WD_FilterByType".Translate();
            cachedFilterByNameHint = "TSA_WD_FilterByName".Translate();
            cachedHdrTier = "TSA_WD_FacDet_HeaderTier".Translate();
            cachedHdrType = "TSA_WD_FacDet_HeaderSettlementType".Translate();
            cachedHdrName = "TSA_WD_FacDet_HeaderName".Translate();
            cachedHdrStrength = "TSA_WD_FacDet_HeaderStrength".Translate();
            cachedHdrDist = "TSA_WD_FacDet_HeaderDistance".Translate();
            cachedDistTip = "TSA_WD_FacDet_DistanceTip".Translate();
            cachedStrengthTip = "TSA_WD_FacDet_StrengthTip".Translate();
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
            Widgets.Label(new Rect(0f, 0f, inRect.width, 35f), cachedTitle);
            Text.Font = GameFont.Small;

            // Fill available width; Name absorbs leftover so we never need a horizontal scrollbar.
            float colTier = ColTier;
            float colType = ColType;
            float colStr = ColStr;
            float colDist = ColDist;
            float colRoad = ColRoad;
            float colView = ColView;
            float fixedCols = colTier + colType + colStr + colDist + colRoad + colView;
            float contentWidth = Mathf.Max(TableWidth, inRect.width - ScrollbarPad);
            float colName = Mathf.Max(ColName, contentWidth - fixedCols);
            float tableWidth = fixedCols + colName;

            float filterY = 40f;
            DrawFilterField(new Rect(colTier, filterY, colType - 4f, FilterHeight), ref typeFilter, cachedFilterByTypeHint);
            DrawFilterField(new Rect(colTier + colType, filterY, colName - 4f, FilterHeight), ref nameFilter, cachedFilterByNameHint);

            if (Find.TickManager.TicksGame - lastUpdateTick > UpdateIntervalTicks || lastUpdateTick < 0)
                RebuildCache();

            Rect hRect = new Rect(0f, 68f, tableWidth, HeaderHeight);
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            float curX = 0f;
            DrawHeader(ref curX, colTier, cachedHdrTier, "Tier", hRect, TextAnchor.MiddleCenter);
            DrawHeader(ref curX, colType, cachedHdrType, "Type", hRect, TextAnchor.MiddleLeft);
            DrawHeader(ref curX, colName, cachedHdrName, "Name", hRect, TextAnchor.MiddleLeft);
            DrawHeader(ref curX, colStr, cachedHdrStrength, "Strength", hRect, TextAnchor.MiddleCenter, cachedStrengthTip);
            DrawHeader(ref curX, colDist, cachedHdrDist, "Dist", hRect, TextAnchor.MiddleCenter, cachedDistTip);
            DrawHeader(ref curX, colRoad, cachedRoadHeader, "Road", hRect, TextAnchor.MiddleCenter);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Widgets.DrawLineHorizontal(0f, hRect.yMax, tableWidth);

            float listTop = hRect.yMax + 4f;
            Rect scrollOuter = new Rect(0f, listTop, inRect.width, inRect.height - listTop - 8f);
            Rect viewRect = new Rect(0f, 0f, tableWidth, cachedList.Count * RowHeight);
            Widgets.BeginScrollView(scrollOuter, ref scrollPos, viewRect);

            for (int i = 0; i < cachedList.Count; i++)
            {
                FactionDetailEntry e = cachedList[i];
                Rect row = new Rect(0f, i * RowHeight, tableWidth, RowHeight);
                if (i % 2 == 0) Widgets.DrawHighlight(row);

                float x = row.x;

                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(x, row.y, colTier, RowHeight), e.TierDisplay);
                x += colTier;

                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(x + 4f, row.y, colType - 4f, RowHeight), e.TypeDisplay.Truncate(colType - 8f));
                x += colType;

                float nameW = e.BeingPurchased ? colName - BuyIconSize - 6f : colName;
                Widgets.Label(new Rect(x + 4f, row.y, nameW - 4f, RowHeight), e.NameDisplay.Truncate(nameW - 8f));
                if (e.BeingPurchased)
                {
                    Rect buyIconRect = new Rect(x + nameW + 2f, row.y + (RowHeight - BuyIconSize) / 2f, BuyIconSize, BuyIconSize);
                    Texture2D buyIcon = Action_Settlement_Buy.BuyIcon;
                    if (buyIcon != null)
                        GUI.DrawTexture(buyIconRect, buyIcon, ScaleMode.ScaleToFit);
                    TooltipHandler.TipRegion(buyIconRect, cachedBuyPendingTip);
                }
                x += colName;

                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(x, row.y, colStr, RowHeight), e.StrengthDisplay);
                x += colStr;

                Rect distRect = new Rect(x, row.y, colDist, RowHeight);
                Widgets.Label(distRect, e.DistanceDisplay);
                TooltipHandler.TipRegion(distRect, cachedDistTip);
                x += colDist;

                Rect roadRect = new Rect(x + 2f, row.y + 4f, colRoad - 4f, RowHeight - 8f);
                DrawRoadForPlayerCell(roadRect, e.Comp);
                x += colRoad;

                Rect goBtn = new Rect(x + 4f, row.y + 3f, colView - 16f, RowHeight - 6f);
                if (Widgets.ButtonText(goBtn, cachedViewBtnLabel))
                    WorldDomination_UIUtils.JumpToWorldObjectOnMap(e.Obj);
            }

            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.EndScrollView();
        }

        private void DrawFilterField(Rect rect, ref string value, string hint)
        {
            string old = value;
            value = Widgets.TextField(rect, value ?? "");
            if (value != old) lastUpdateTick = -9999;
            if (string.IsNullOrEmpty(value))
            {
                GUI.color = new Color(1f, 1f, 1f, 0.4f);
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(rect.x + 4f, rect.y, rect.width - 4f, rect.height), hint);
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
            }
        }

        private void RebuildCache()
        {
            cachedList.Clear();
            var manager = Find.World.GetComponent<WorldComponent_SpreadManager>();
            int playerTile = Find.AnyPlayerHomeMap != null ? Find.AnyPlayerHomeMap.Tile : -1;
            string typeLower = string.IsNullOrEmpty(typeFilter) ? null : typeFilter.Trim().ToLowerInvariant();
            string nameLower = string.IsNullOrEmpty(nameFilter) ? null : nameFilter.Trim().ToLowerInvariant();

            if (faction.IsPlayer)
            {
                var all = Find.WorldObjects.AllWorldObjects;
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i] is WorldObject_WD_Outpost o)
                        TryAdd(o, o.GetComponent<CompViralSpread>(), playerTile, manager, typeLower, nameLower);
                }
            }
            else
            {
                var settlements = Find.WorldObjects.Settlements;
                for (int i = 0; i < settlements.Count; i++)
                {
                    Settlement s = settlements[i];
                    if (s.Faction != faction) continue;
                    TryAdd(s, s.GetComponent<CompViralSpread>(), playerTile, manager, typeLower, nameLower);
                }
            }

            SortCachedList();
            lastUpdateTick = Find.TickManager.TicksGame;
        }

        private void TryAdd(WorldObject obj, CompViralSpread comp, int playerTile, WorldComponent_SpreadManager manager, string typeLower, string nameLower)
        {
            string name = obj.LabelCap;
            string type = GetSettlementTypeLabel(obj, comp);
            if (typeLower != null && (type == null || !type.ToLowerInvariant().Contains(typeLower)))
                return;
            if (nameLower != null && (name == null || !name.ToLowerInvariant().Contains(nameLower)))
                return;

            float strength = WorldStatsUtils.GetOutpostStatsStrength(comp);
            int dist = playerTile >= 0 ? WorldActions_Utils.GetDistance(obj.Tile, playerTile, manager) : 999;
            string tierDisplay;
            int sortTier;
            if (!faction.IsPlayer)
            {
                sortTier = (int)(comp?.tier ?? SettlementTier.T1);
                tierDisplay = comp?.tier.ToString() ?? "??";
            }
            else
            {
                int tierIndex = WorldStatsUtils.TierIndexFromWorldStrengthTotal(strength);
                tierDisplay = "T" + tierIndex;
                sortTier = tierIndex - 1;
            }

            cachedList.Add(new FactionDetailEntry
            {
                Obj = obj,
                Comp = comp,
                TierDisplay = tierDisplay,
                TypeDisplay = type,
                NameDisplay = name,
                Strength = strength,
                StrengthDisplay = strength.ToString("F0"),
                Distance = dist,
                DistanceDisplay = dist.ToString(),
                BeingPurchased = obj is Settlement settlement && SettlementBuyUtility.HasPendingBuyForSettlement(settlement),
                SortTier = sortTier
            });
        }

        /// <summary>NPC specialty from CompViralSpread.subType (Mining, Farming, Slavery, …); outposts fall back to def label.</summary>
        private static string GetSettlementTypeLabel(WorldObject obj, CompViralSpread comp)
        {
            if (comp != null && !string.IsNullOrEmpty(comp.subType)
                && comp.subType != "Excluded"
                && comp.subType != "Outpost"
                && comp.subType != "Colony")
            {
                string key = "TSA_WD_SubType_" + comp.subType;
                TaggedString translated = key.Translate();
                if (translated.RawText != key)
                    return translated.Resolve();
                return comp.subType;
            }

            if (obj is WorldObject_WD_Outpost outpost && outpost.def != null)
            {
                string label = outpost.def.label;
                if (!label.NullOrEmpty())
                {
                    const string suffix = " outpost";
                    if (label.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                        return label.Substring(0, label.Length - suffix.Length).CapitalizeFirst();
                    return outpost.def.LabelCap;
                }
            }

            return "-";
        }

        private void DrawHeader(ref float curX, float width, string label, string tag, Rect hRect, TextAnchor anchor, string tip = null)
        {
            Rect headerRect = new Rect(curX, hRect.y, width, hRect.height);
            if (Mouse.IsOver(headerRect)) Widgets.DrawHighlight(headerRect);
            Text.Anchor = anchor;
            string headerText = label + (sortColumn == tag ? (sortAscending ? " ▲" : " ▼") : "");
            Rect labelRect = anchor == TextAnchor.MiddleLeft
                ? new Rect(headerRect.x + 4f, headerRect.y, headerRect.width - 4f, headerRect.height)
                : headerRect;
            Widgets.Label(labelRect, headerText.Truncate(labelRect.width - 4f));
            Text.Anchor = TextAnchor.UpperLeft;
            if (!string.IsNullOrEmpty(tip))
                TooltipHandler.TipRegion(headerRect, tip);
            if (Widgets.ButtonInvisible(headerRect)) SetSort(tag);
            curX += width;
        }

        private void SetSort(string col)
        {
            if (sortColumn == col) sortAscending = !sortAscending;
            else { sortColumn = col; sortAscending = true; }
            lastUpdateTick = -9999;
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        private void SortCachedList()
        {
            cachedList.Sort((a, b) =>
            {
                int cmp = sortColumn switch
                {
                    "Tier" => a.SortTier.CompareTo(b.SortTier),
                    "Type" => string.Compare(a.TypeDisplay, b.TypeDisplay, StringComparison.OrdinalIgnoreCase),
                    "Name" => string.Compare(a.NameDisplay, b.NameDisplay, StringComparison.OrdinalIgnoreCase),
                    "Strength" => a.Strength.CompareTo(b.Strength),
                    "Dist" => a.Distance.CompareTo(b.Distance),
                    "Road" => GetRoadProgress(a.Comp).CompareTo(GetRoadProgress(b.Comp)),
                    _ => a.Strength.CompareTo(b.Strength)
                };
                if (cmp == 0)
                    cmp = string.Compare(a.NameDisplay, b.NameDisplay, StringComparison.OrdinalIgnoreCase);
                return sortAscending ? cmp : -cmp;
            });
        }

        private static float GetRoadProgress(CompViralSpread comp)
        {
            if (comp == null) return 0f;
            if (comp.roadTargetTile != -1) return Mathf.Min(1f, comp.roadProgress);
            if (WorldActions_RoadBlocks.HasActiveRoadBlockProject(comp)) return Mathf.Min(1f, comp.roadBlockProgress);
            return 0f;
        }

        private static void DrawRoadForPlayerCell(Rect rect, CompViralSpread comp)
        {
            TextAnchor prev = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;

            if (comp != null && comp.roadTargetTile != -1)
            {
                float barH = Mathf.Min(rect.height - 2f, 14f * 0.6f);
                float barY = rect.y + (rect.height - barH) * 0.5f;
                Rect barRect = new Rect(rect.x, barY, rect.width, barH);
                Widgets.FillableBar(barRect, Mathf.Clamp01(comp.roadProgress), facDetRoadBarTex);

                Text.Font = GameFont.Tiny;
                string insufficient = comp.GetInsufficientStrengthConstructionMessage();
                string label = insufficient
                    ?? (comp.playerOrderedRoad
                        ? OrderedRoadUtility.FormatRoadProgressLabel(comp)
                        : (comp.roadIsClearing
                            ? "TSA_WD_Inspect_RoadClear".Translate().ToString()
                            : (comp.roadTargetName ?? "")));
                if (string.IsNullOrEmpty(label))
                    label = (Mathf.Min(1f, comp.roadProgress) * 100f).ToString("F0") + "%";
                Widgets.Label(rect, label.Truncate((int)rect.width));
                Text.Font = GameFont.Small;
            }
            else if (comp != null && WorldActions_RoadBlocks.HasActiveRoadBlockProject(comp))
            {
                float barH = Mathf.Min(rect.height - 2f, 14f * 0.6f);
                float barY = rect.y + (rect.height - barH) * 0.5f;
                Rect barRect = new Rect(rect.x, barY, rect.width, barH);
                Widgets.FillableBar(barRect, Mathf.Clamp01(comp.roadBlockProgress), facDetRoadBarTex);

                Text.Font = GameFont.Tiny;
                string insufficient = comp.GetInsufficientStrengthConstructionMessage();
                string label = insufficient;
                if (label == null)
                {
                    label = comp.GetActiveRoadBlockProjectLabel()
                        + " (" + (Mathf.Min(1f, comp.roadBlockProgress) * 100f).ToString("F0") + "%)";
                }
                Widgets.Label(rect, label.Truncate((int)rect.width));
                Text.Font = GameFont.Small;
            }
            else
            {
                Widgets.Label(rect, "-");
            }

            Text.Anchor = prev;
        }
    }
}
