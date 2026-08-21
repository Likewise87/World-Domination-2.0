using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    [StaticConstructorOnStartup]
    public class WITab_Outpost_Upgrades : WITab
    {
        private const float HeaderOffsetY = 0f;
        private const float ColGap = 18f;
        private const float SearchRowHeight = 28f;
        private const float SearchRowGap = 6f;
        private const float FilterButtonWidth = 150f;
        private const float IconColW = 56f;
        private const float IconPadding = 8f;

        private Vector2 rightScrollPos;
        private Vector2 leftDetailScrollPos;
        private string upgradeSearchFilter = "";
        private UpgradeTabFilter upgradeFilter = UpgradeTabFilter.All;
        private string selectedUpgradeDefName;
        private WorldObject_WD_Outpost selectionOutpost;

        private List<List<OutpostUpgradeDef>> cachedUpgradeGroups;
        private WorldObject_WD_Outpost cachedUpgradeGroupsOutpost;
        private int cachedUpgradeGroupsFingerprint;

        private List<CachedUpgradeRow> cachedRows = new List<CachedUpgradeRow>();
        private int cachedRowsTick = -1;
        private int cachedRowsFingerprint;

        private struct CachedUpgradeRow
        {
            public OutpostUpgradeDef Def;
            public RowState State;
            public bool CanBuy;
            public bool IsPending;
            public OutpostUpgradeUtility.PurchaseCheck Check;
            public string RowTooltip;
        }

        private readonly struct RowState
        {
            public readonly bool builtHere;
            public readonly bool superseded;
            public readonly bool showBuy;
            public readonly bool deployed;
            public readonly bool futureTier;
            public readonly bool sequentialBlocked;

            public RowState(bool builtHere, bool superseded, bool showBuy, bool deployed, bool futureTier, bool sequentialBlocked)
            {
                this.builtHere = builtHere;
                this.superseded = superseded;
                this.showBuy = showBuy;
                this.deployed = deployed;
                this.futureTier = futureTier;
                this.sequentialBlocked = sequentialBlocked;
            }
        }

        private enum UpgradeTabFilter
        {
            All,
            Built,
            NotBuilt,
            Buildable
        }

        public WITab_Outpost_Upgrades()
        {
            size = new Vector2(960f, 560f);
            labelKey = "TSA_WD_OutpostUpgrades_TabLabel";
        }

        private WorldObject_WD_Outpost SelOutpost => SelObject as WorldObject_WD_Outpost;
        public override bool IsVisible => SelOutpost != null && SelOutpost.Faction == Faction.OfPlayer;

        protected override void FillTab()
        {
            WorldObject_WD_Outpost outpost = SelOutpost;
            if (outpost == null) return;

            Rect body = new Rect(0f, HeaderOffsetY, size.x, size.y - HeaderOffsetY).ContractedBy(10f);
            Text.Font = GameFont.Medium;
            string headline = OutpostTranslationUtil.TabHeadline(outpost, "TSA_WD_OutpostUpgrades_TabLabel");
            LabelAnchored(new Rect(body.x, body.y, body.width, 30f), headline, TextAnchor.MiddleLeft);
            Text.Font = GameFont.Small;
            Widgets.DrawLineHorizontal(body.x, body.y + 32f, body.width);

            List<List<OutpostUpgradeDef>> groups = GetUpgradeGroupsCached(outpost);
            EnsureCachedRows(outpost, groups);

            float columnsTop = body.y + 38f;
            float columnsBottom = body.yMax;
            float leftW = Mathf.Max(260f, body.width * 0.42f);
            Rect leftArea = new Rect(body.x, columnsTop, leftW, columnsBottom - columnsTop);
            Rect rightArea = new Rect(body.x + leftW + ColGap, columnsTop, body.xMax - (body.x + leftW + ColGap), columnsBottom - columnsTop);
            Widgets.DrawLineVertical(body.x + leftW + ColGap * 0.5f, columnsTop, columnsBottom - columnsTop);

            EnsureDefaultSelection(outpost);
            DrawLeftColumn(leftArea, outpost);
            DrawRightColumn(rightArea, outpost);
        }

        private void DrawLeftColumn(Rect leftArea, WorldObject_WD_Outpost outpost)
        {
            float lx = leftArea.x;
            float lw = leftArea.width;
            float ly = leftArea.y;

            ly = Outpost_Upgrade_UI.DrawAggregateBenefitsBox(lx, ly, lw, outpost);
            ly += Outpost_Dialog_UI.OutcomeBoxGap;

            if (!string.IsNullOrEmpty(outpost.PendingUpgradeDefName))
            {
                var pendingDef = DefDatabase<OutpostUpgradeDef>.GetNamedSilentFail(outpost.PendingUpgradeDefName);
                string pendingLabel = pendingDef != null ? pendingDef.LabelCap : outpost.PendingUpgradeDefName;
                GUI.color = new Color(0.95f, 0.75f, 0.35f);
                string pendingLine = OutpostTranslationUtil.Key("TSA_WD_OutpostUpgrades_PendingCaravan", pendingLabel);
                Rect pendingRect = new Rect(lx, ly, lw, Outpost_Dialog_UI.OutcomeLineH);
                Widgets.Label(pendingRect, pendingLine);
                GUI.color = Color.white;
                ly += Outpost_Dialog_UI.OutcomeLineH + 6f;
            }

            Widgets.DrawLineHorizontal(lx, ly, lw);
            ly += 8f;

            GUI.color = Outpost_Dialog_UI.TheoreticalLabelColor;
            Widgets.Label(new Rect(lx, ly, lw, Outpost_Dialog_UI.OutcomeLineH), OutpostTranslationUtil.Key("TSA_WD_OutpostUpgrades_SelectedDetail"));
            GUI.color = Color.white;
            ly += Outpost_Dialog_UI.OutcomeLineH + 4f;

            CachedUpgradeRow? selectedRow = FindCachedRow(selectedUpgradeDefName);
            float detailContentH = selectedRow.HasValue
                ? Outpost_Upgrade_UI.MeasureSelectedUpgradeDetailHeight(selectedRow.Value.Def, !string.IsNullOrEmpty(selectedRow.Value.Def?.description))
                : Outpost_Dialog_UI.OutcomeLineH;

            float detailScrollH = leftArea.yMax - ly - 4f;
            Rect detailOuter = new Rect(lx, ly, lw, detailScrollH);
            Rect detailView = new Rect(0f, 0f, lw - 16f, Mathf.Max(detailContentH, detailScrollH));
            Widgets.BeginScrollView(detailOuter, ref leftDetailScrollPos, detailView);

            if (selectedRow.HasValue)
            {
                var row = selectedRow.Value;
                Outpost_Upgrade_UI.DrawSelectedUpgradeDetail(
                    0f, 0f, detailView.width, outpost, row.Def,
                    row.State.deployed, row.State.superseded, row.State.sequentialBlocked,
                    row.State.futureTier, row.State.showBuy, row.CanBuy, row.IsPending,
                    row.Check, () => TryBuildUpgrade(outpost, row.Def));
            }
            else
            {
                Widgets.Label(new Rect(0f, 0f, detailView.width, Outpost_Dialog_UI.OutcomeLineH),
                    OutpostTranslationUtil.Key("TSA_WD_OutpostUpgrades_NoSelection"));
            }

            Widgets.EndScrollView();
        }

        private void DrawRightColumn(Rect rightArea, WorldObject_WD_Outpost outpost)
        {
            float y = rightArea.y;
            const float itemSearchBarH = SearchRowHeight;

            GUI.color = new Color(0.75f, 0.82f, 1f);
            Widgets.Label(new Rect(rightArea.x, y, rightArea.width, Outpost_Upgrade_UI.RightColHeaderH), OutpostTranslationUtil.Key("TSA_WD_OutpostUpgrades_ChooseHeader"));
            GUI.color = Color.white;
            y += Outpost_Upgrade_UI.RightColHeaderH + 2f;

            Rect filterRect = new Rect(rightArea.xMax - FilterButtonWidth, y, FilterButtonWidth, itemSearchBarH);
            Rect searchRect = new Rect(rightArea.x, y, rightArea.width - FilterButtonWidth - 8f, itemSearchBarH);

            string oldSearch = upgradeSearchFilter;
            upgradeSearchFilter = Widgets.TextField(searchRect, upgradeSearchFilter);
            if (upgradeSearchFilter != oldSearch)
                rightScrollPos = Vector2.zero;

            if (string.IsNullOrEmpty(upgradeSearchFilter))
            {
                GUI.color = new Color(1f, 1f, 1f, 0.4f);
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.Font = GameFont.Tiny;
                Widgets.Label(searchRect, OutpostTranslationUtil.Key("TSA_WD_OutpostUpgrades_SearchPlaceholder"));
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
            }

            if (Widgets.ButtonText(filterRect, GetFilterLabel(upgradeFilter)))
            {
                var opts = new List<FloatMenuOption>
                {
                    new FloatMenuOption(OutpostTranslationUtil.Key("TSA_WD_OutpostUpgrades_FilterAll"), () => SetFilter(UpgradeTabFilter.All)),
                    new FloatMenuOption(OutpostTranslationUtil.Key("TSA_WD_OutpostUpgrades_FilterBuilt"), () => SetFilter(UpgradeTabFilter.Built)),
                    new FloatMenuOption(OutpostTranslationUtil.Key("TSA_WD_OutpostUpgrades_FilterNotBuilt"), () => SetFilter(UpgradeTabFilter.NotBuilt)),
                    new FloatMenuOption(OutpostTranslationUtil.Key("TSA_WD_OutpostUpgrades_FilterBuildable"), () => SetFilter(UpgradeTabFilter.Buildable)),
                };
                Find.WindowStack.Add(new FloatMenu(opts));
            }

            y += itemSearchBarH + SearchRowGap;

            List<CachedUpgradeRow> ordered = BuildOrderedVisibleRows(outpost);
            float scrollHeight = 8f;
            foreach (var row in ordered)
                scrollHeight += Outpost_Upgrade_UI.CompactRowHeight + Outpost_Upgrade_UI.CompactRowPadding;

            Rect scrollOuter = new Rect(rightArea.x, y, rightArea.width, rightArea.yMax - y);
            Rect viewRect = new Rect(0f, 0f, rightArea.width - 16f, Mathf.Max(scrollHeight, 1f));
            Widgets.BeginScrollView(scrollOuter, ref rightScrollPos, viewRect);

            float curY = 0f;
            int visibleRow = 0;

            foreach (var row in ordered)
            {
                float rowH = Outpost_Upgrade_UI.CompactRowHeight + Outpost_Upgrade_UI.CompactRowPadding;
                Rect rowRect = new Rect(0f, curY, viewRect.width, rowH);
                if (visibleRow % 2 == 0) Widgets.DrawHighlight(rowRect);

                Color? bg = Outpost_Upgrade_UI.GetRowBackground(
                    row.State.deployed, row.State.superseded, row.State.sequentialBlocked,
                    row.State.futureTier, row.IsPending);
                if (bg.HasValue)
                    Widgets.DrawBoxSolid(rowRect, bg.Value);

                // Same light-red tint as production options that cannot be selected (materials / research / etc.).
                Outpost_Dialog_UI.DrawUnmetRequirementsRowTint(rowRect, row.State.showBuy && !row.CanBuy);

                bool isSelected = row.Def != null && row.Def.defName == selectedUpgradeDefName;
                if (isSelected)
                {
                    GUI.color = Outpost_Upgrade_UI.RowBgSelected;
                    GUI.DrawTexture(rowRect, BaseContent.WhiteTex);
                    GUI.color = Color.white;
                }

                float rowContentY = curY + (rowRect.height - Outpost_Upgrade_UI.CompactRowHeight) * 0.5f;
                Texture2D icon = Outpost_Upgrade_UI.GetUpgradeIcon(row.Def);
                float iconY = rowContentY + (Outpost_Upgrade_UI.CompactRowHeight - Outpost_Upgrade_UI.CompactRowIconSize) * 0.5f;
                Rect iconRect = new Rect(IconPadding, iconY, Outpost_Upgrade_UI.CompactRowIconSize, Outpost_Upgrade_UI.CompactRowIconSize);
                if (row.State.futureTier && !row.State.sequentialBlocked)
                    GUI.color = new Color(1f, 1f, 1f, 0.45f);
                if (icon != null) Outpost_Upgrade_UI.DrawTextureTopFit(iconRect.ContractedBy(2f), icon);
                GUI.color = Color.white;

                string label = row.Def?.LabelCap ?? "";
                if (row.Def != null && row.Def.lineTier > 1)
                    label += " (Lv " + row.Def.lineTier + ")";

                Rect labelRect = new Rect(IconColW, rowContentY, viewRect.width - IconColW - 8f, Outpost_Upgrade_UI.CompactRowHeight);
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(labelRect, label);
                Text.Anchor = TextAnchor.UpperLeft;

                if (Mouse.IsOver(rowRect)) Widgets.DrawHighlight(rowRect);
                if (isSelected)
                {
                    GUI.color = Color.white;
                    Widgets.DrawBox(rowRect, 1);
                    GUI.color = Color.white;
                }
                if (Widgets.ButtonInvisible(rowRect) && row.Def != null)
                    selectedUpgradeDefName = row.Def.defName;
                if (!string.IsNullOrEmpty(row.RowTooltip))
                    TooltipHandler.TipRegion(rowRect, row.RowTooltip);

                curY += rowH;
                visibleRow++;
            }

            Widgets.EndScrollView();
        }

        private void TryBuildUpgrade(WorldObject_WD_Outpost outpost, OutpostUpgradeDef def)
        {
            if (outpost == null || def == null) return;
            const int purchaseLevel = 1;
            if (!OutpostUpgradeUtility.TryPurchaseAndLaunch(outpost, def, purchaseLevel, out string failReason))
                Messages.Message(failReason ?? OutpostTranslationUtil.Key("TSA_WD_OutpostUpgrades_UnableBuy"), MessageTypeDefOf.RejectInput);
            else
            {
                cachedRowsTick = -1;
                cachedUpgradeGroupsFingerprint = -1;
            }
        }

        private void EnsureCachedRows(WorldObject_WD_Outpost outpost, List<List<OutpostUpgradeDef>> groups)
        {
            int fp = UpgradeGroupsFingerprint(outpost);
            int tick = Find.TickManager.TicksGame;
            if (cachedRows.Count > 0
                && cachedRowsFingerprint == fp
                && cachedRowsTick >= 0
                && tick - cachedRowsTick < Outpost_Dialog_UI.LiveRefreshIntervalTicks)
                return;

            cachedRows.Clear();
            cachedRowsFingerprint = fp;
            cachedRowsTick = tick;
            const int purchaseLevel = 1;

            for (int gi = 0; gi < groups.Count; gi++)
            {
                var group = groups[gi];
                for (int i = 0; i < group.Count; i++)
                {
                    OutpostUpgradeDef def = group[i];
                    if (def == null) continue;
                    RowState rs = ComputeRowState(outpost, def);
                    bool isPending = !string.IsNullOrEmpty(outpost.PendingUpgradeDefName)
                        && outpost.PendingUpgradeDefName == def.defName;

                    OutpostUpgradeUtility.PurchaseCheck check = default;
                    bool canBuy = false;
                    if (rs.showBuy || rs.sequentialBlocked || rs.futureTier || rs.superseded)
                    {
                        check = OutpostUpgradeUtility.EvaluatePurchase(outpost, def, purchaseLevel, ignoreLineTierForPreview: rs.sequentialBlocked || rs.futureTier || rs.superseded);
                        canBuy = check.canBuy;
                    }

                    cachedRows.Add(new CachedUpgradeRow
                    {
                        Def = def,
                        State = rs,
                        CanBuy = canBuy,
                        IsPending = isPending,
                        Check = check,
                        RowTooltip = BuildRowTooltip(rs, isPending, canBuy, check)
                    });
                }
            }
        }

        private static string BuildRowTooltip(RowState rs, bool isPending, bool canBuy, OutpostUpgradeUtility.PurchaseCheck check)
        {
            if (isPending)
                return OutpostTranslationUtil.Key("TSA_WD_OutpostUpgrades_AwaitingDelivery");
            if (rs.showBuy && !canBuy && !string.IsNullOrEmpty(check.reason))
                return check.reason;
            if (rs.sequentialBlocked)
                return OutpostTranslationUtil.Key("TSA_WD_OutpostUpgrades_PreviousUpgradeNeeded");
            if (rs.superseded)
                return OutpostTranslationUtil.Key("TSA_WD_OutpostUpgrades_InferiorOption");
            return null;
        }

        private List<CachedUpgradeRow> BuildOrderedVisibleRows(WorldObject_WD_Outpost outpost)
        {
            var visible = new List<CachedUpgradeRow>();
            for (int i = 0; i < cachedRows.Count; i++)
            {
                if (RowMatchesFilters(outpost, cachedRows[i]))
                    visible.Add(cachedRows[i]);
            }
            return visible;
        }

        private void EnsureDefaultSelection(WorldObject_WD_Outpost outpost)
        {
            if (!ReferenceEquals(selectionOutpost, outpost))
            {
                selectionOutpost = outpost;
                selectedUpgradeDefName = null;
            }

            if (!string.IsNullOrEmpty(selectedUpgradeDefName))
            {
                bool stillVisible = false;
                for (int i = 0; i < cachedRows.Count; i++)
                {
                    if (cachedRows[i].Def?.defName != selectedUpgradeDefName) continue;
                    if (!RowMatchesFilters(outpost, cachedRows[i])) break;
                    stillVisible = true;
                    break;
                }
                if (stillVisible) return;
            }

            if (!string.IsNullOrEmpty(outpost.PendingUpgradeDefName))
            {
                selectedUpgradeDefName = outpost.PendingUpgradeDefName;
                return;
            }

            for (int i = 0; i < cachedRows.Count; i++)
            {
                var row = cachedRows[i];
                if (!RowMatchesFilters(outpost, row)) continue;
                if (row.State.showBuy && row.CanBuy)
                {
                    selectedUpgradeDefName = row.Def.defName;
                    return;
                }
            }

            for (int i = 0; i < cachedRows.Count; i++)
            {
                var row = cachedRows[i];
                if (RowMatchesFilters(outpost, row) && row.Def != null)
                {
                    selectedUpgradeDefName = row.Def.defName;
                    return;
                }
            }

            selectedUpgradeDefName = null;
        }

        private CachedUpgradeRow? FindCachedRow(string defName)
        {
            if (string.IsNullOrEmpty(defName)) return null;
            for (int i = 0; i < cachedRows.Count; i++)
            {
                if (cachedRows[i].Def?.defName == defName)
                    return cachedRows[i];
            }
            return null;
        }

        private bool RowMatchesFilters(WorldObject_WD_Outpost outpost, CachedUpgradeRow row)
        {
            if (!UpgradeMatchesSearch(row.Def, upgradeSearchFilter)) return false;
            if (row.State.superseded) return false;

            switch (upgradeFilter)
            {
                case UpgradeTabFilter.Built:
                    return row.State.builtHere;
                case UpgradeTabFilter.NotBuilt:
                    return !row.State.builtHere;
                case UpgradeTabFilter.Buildable:
                    return OutpostUpgradeUtility.IsBuildableUpgrade(outpost, row.Def);
                default:
                    return true;
            }
        }

        private void SetFilter(UpgradeTabFilter filter)
        {
            upgradeFilter = filter;
            rightScrollPos = Vector2.zero;
            cachedRowsTick = -1;
        }

        private static string GetFilterLabel(UpgradeTabFilter filter)
        {
            switch (filter)
            {
                case UpgradeTabFilter.Built:
                    return OutpostTranslationUtil.Key("TSA_WD_OutpostUpgrades_FilterBuilt");
                case UpgradeTabFilter.NotBuilt:
                    return OutpostTranslationUtil.Key("TSA_WD_OutpostUpgrades_FilterNotBuilt");
                case UpgradeTabFilter.Buildable:
                    return OutpostTranslationUtil.Key("TSA_WD_OutpostUpgrades_FilterBuildable");
                default:
                    return OutpostTranslationUtil.Key("TSA_WD_OutpostUpgrades_FilterAll");
            }
        }

        private static int UpgradeGroupsFingerprint(WorldObject_WD_Outpost o)
        {
            if (o == null) return 0;
            unchecked
            {
                int h = RuntimeHelpers.GetHashCode(o);
                h = h * 31 + (o.def?.defName?.GetHashCode() ?? 0);
                h = h * 31 + o.Tile;
                h = h * 31 + DefDatabase<OutpostUpgradeDef>.AllDefsListForReading.Count;
                if (o.BuiltUpgradeLevels != null)
                    foreach (var kv in o.BuiltUpgradeLevels)
                    {
                        h = h * 31 + (kv.Key?.GetHashCode() ?? 0);
                        h = h * 31 + kv.Value;
                    }
                h = h * 31 + (o.PendingUpgradeDefName?.GetHashCode() ?? 0);
                return h;
            }
        }

        private List<List<OutpostUpgradeDef>> GetUpgradeGroupsCached(WorldObject_WD_Outpost outpost)
        {
            int fp = UpgradeGroupsFingerprint(outpost);
            if (cachedUpgradeGroups != null && ReferenceEquals(cachedUpgradeGroupsOutpost, outpost) && fp == cachedUpgradeGroupsFingerprint)
                return cachedUpgradeGroups;
            cachedUpgradeGroups = BuildUpgradeGroups(outpost);
            cachedUpgradeGroupsOutpost = outpost;
            cachedUpgradeGroupsFingerprint = fp;
            cachedRowsTick = -1;
            return cachedUpgradeGroups;
        }

        private static List<List<OutpostUpgradeDef>> BuildUpgradeGroups(WorldObject_WD_Outpost outpost)
        {
            var groups = new List<List<OutpostUpgradeDef>>();
            if (outpost?.def?.defName == null) return groups;

            string wdef = outpost.def.defName;
            var allDefs = DefDatabase<OutpostUpgradeDef>.AllDefsListForReading;
            var byLine = new Dictionary<string, List<OutpostUpgradeDef>>();
            var lineOrder = new List<string>();
            for (int i = 0; i < allDefs.Count; i++)
            {
                OutpostUpgradeDef d = allDefs[i];
                if (d == null || !d.AppliesToOutpost(wdef)) continue;
                string key = d.upgradeLineId ?? d.defName;
                if (!byLine.TryGetValue(key, out List<OutpostUpgradeDef> list))
                {
                    list = new List<OutpostUpgradeDef>();
                    byLine[key] = list;
                    lineOrder.Add(key);
                }
                list.Add(d);
            }

            lineOrder.Sort((a, b) =>
            {
                List<OutpostUpgradeDef> la = byLine[a];
                List<OutpostUpgradeDef> lb = byLine[b];
                int cmp = ((int)la[0].category).CompareTo((int)lb[0].category);
                if (cmp != 0) return cmp;
                int minA = int.MaxValue, minB = int.MaxValue;
                for (int i = 0; i < la.Count; i++) if (la[i].lineTier < minA) minA = la[i].lineTier;
                for (int i = 0; i < lb.Count; i++) if (lb[i].lineTier < minB) minB = lb[i].lineTier;
                cmp = minA.CompareTo(minB);
                if (cmp != 0) return cmp;
                return string.Compare(la[0].LabelCap.Resolve(), lb[0].LabelCap.Resolve(), StringComparison.OrdinalIgnoreCase);
            });

            for (int i = 0; i < lineOrder.Count; i++)
            {
                List<OutpostUpgradeDef> list = byLine[lineOrder[i]];
                list.Sort((a, b) =>
                {
                    int cmp = a.lineTier.CompareTo(b.lineTier);
                    return cmp != 0 ? cmp : string.Compare(a.LabelCap.Resolve(), b.LabelCap.Resolve(), StringComparison.OrdinalIgnoreCase);
                });
                groups.Add(list);
            }
            return groups;
        }

        private static RowState ComputeRowState(WorldObject_WD_Outpost outpost, OutpostUpgradeDef def)
        {
            int hi = OutpostUpgradeUtility.GetHighestBuiltLineTier(outpost, def.upgradeLineId);
            int lvl = outpost.GetUpgradeLevel(def.defName);
            bool builtHere = lvl > 0;
            bool superseded = hi > def.lineTier;
            bool showBuy = !superseded && !builtHere && OutpostUpgradeUtility.TierEligibleForPurchase(def, hi);
            bool deployed = builtHere && hi == def.lineTier;
            bool futureTier = !superseded && !builtHere && !showBuy && def.lineTier > hi;
            bool sequentialBlocked = def.requiresPreviousLineTier && !superseded && !builtHere && hi < def.lineTier - 1;
            return new RowState(builtHere, superseded, showBuy, deployed, futureTier, sequentialBlocked);
        }

        private static bool UpgradeMatchesSearch(OutpostUpgradeDef def, string filter)
        {
            string q = filter?.Trim();
            if (string.IsNullOrEmpty(q)) return true;
            return TokenMatches(def?.LabelCap.Resolve(), q)
                || TokenMatches(def?.label, q)
                || TokenMatches(def?.defName, q);
        }

        private static bool TokenMatches(string haystack, string needle)
        {
            return !haystack.NullOrEmpty()
                && !needle.NullOrEmpty()
                && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void LabelAnchored(Rect rect, string text, TextAnchor anchor)
        {
            TextAnchor prev = Text.Anchor;
            Text.Anchor = anchor;
            Widgets.Label(rect, text);
            Text.Anchor = prev;
        }
    }

    public static class OutpostUpgradeUtility
    {
        /// <summary>The single player colony map (we assume one player home map).</summary>
        private static Map GetColonyMap() => Find.AnyPlayerHomeMap;

        private static int warehouseCacheTick = -1;
        private static List<WorldObject_WD_Outpost> warehouseCache;

        /// <summary>All player warehouse outposts. Colony map plus every warehouse count toward upgrade payment.</summary>
        private static List<WorldObject_WD_Outpost> GetContributingWarehouses(int outpostTile)
        {
            _ = outpostTile;
            int t = Find.TickManager?.TicksGame ?? 0;
            if (warehouseCache != null && t == warehouseCacheTick)
                return warehouseCache;

            var result = new List<WorldObject_WD_Outpost>();
            if (Find.WorldObjects != null)
            {
                var all = Find.WorldObjects.AllWorldObjects;
                for (int i = 0; i < all.Count; i++)
                {
                    if (!(all[i] is WorldObject_WD_Outpost wo) || !Outpost_Warehouse_Delivery.IsWarehouseOutpost(wo)) continue;
                    if (CompOutpostWarehouse.Get(wo) == null) continue;
                    result.Add(wo);
                }
            }
            warehouseCache = result;
            warehouseCacheTick = t;
            return result;
        }

        private static int CountWarehouseStored(List<WorldObject_WD_Outpost> warehouses, ThingDef def)
        {
            if (warehouses == null || def == null) return 0;
            int total = 0;
            for (int i = 0; i < warehouses.Count; i++)
            {
                var comp = CompOutpostWarehouse.Get(warehouses[i]);
                if (comp != null) total += comp.GetStoredCount(def);
            }
            return total;
        }

        private static int CountWarehouseStoneBlocks(List<WorldObject_WD_Outpost> warehouses)
        {
            if (warehouses == null) return 0;
            int total = 0;
            for (int i = 0; i < warehouses.Count; i++)
            {
                var comp = CompOutpostWarehouse.Get(warehouses[i]);
                if (comp != null) total += comp.GetStoredStoneBlocksCount();
            }
            return total;
        }

        /// <summary>Origin for the symbolic upgrade caravan: nearest warehouse or colony, whichever is closer to the upgrading outpost.</summary>
        private static WorldObject ResolveUpgradeOrigin(
            WorldObject_WD_Outpost outpost,
            Map colonyMap,
            List<WorldObject_WD_Outpost> warehouses)
        {
            if (outpost == null || Find.WorldGrid == null)
                return colonyMap?.Parent;

            WorldObject nearestWh = FindNearestWarehouse(outpost.Tile, warehouses);
            WorldObject colony = colonyMap?.Parent;
            if (nearestWh == null) return colony;
            if (colony == null) return nearestWh;

            float dWh = Find.WorldGrid.ApproxDistanceInTiles(outpost.Tile, nearestWh.Tile);
            float dCol = Find.WorldGrid.ApproxDistanceInTiles(outpost.Tile, colony.Tile);
            return dWh <= dCol ? nearestWh : colony;
        }

        private static WorldObject_WD_Outpost FindNearestWarehouse(int outpostTile, List<WorldObject_WD_Outpost> warehouses)
        {
            if (warehouses == null || warehouses.Count == 0 || Find.WorldGrid == null) return null;
            WorldObject_WD_Outpost nearest = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < warehouses.Count; i++)
            {
                if (warehouses[i] == null) continue;
                // Do not ship from the outpost that is itself being upgraded.
                if (warehouses[i].Tile == outpostTile) continue;
                float d = Find.WorldGrid.ApproxDistanceInTiles(outpostTile, warehouses[i].Tile);
                if (d < bestDist) { bestDist = d; nearest = warehouses[i]; }
            }
            return nearest;
        }

        public struct PurchaseCheck
        {
            public bool canBuy;
            public string reason;
            public Dictionary<string, bool> costAvailableByDefName;
        }

        public static int GetHighestBuiltLineTier(WorldObject_WD_Outpost outpost, string upgradeLineId)
        {
            if (outpost == null || string.IsNullOrEmpty(upgradeLineId)) return 0;
            int best = 0;
            foreach (var kv in outpost.BuiltUpgradeLevels)
            {
                if (kv.Value <= 0) continue;
                var d = DefDatabase<OutpostUpgradeDef>.GetNamedSilentFail(kv.Key);
                if (d == null || d.upgradeLineId != upgradeLineId) continue;
                if (d.lineTier > best)
                    best = d.lineTier;
            }
            return best;
        }

        public static bool TierEligibleForPurchase(OutpostUpgradeDef def, int highestBuiltLineTier)
        {
            if (def == null) return false;
            if (def.requiresPreviousLineTier)
                return def.lineTier == highestBuiltLineTier + 1;
            return def.lineTier > highestBuiltLineTier;
        }

        public static IEnumerable<string> GetResearchRequirements(OutpostUpgradeDef upgrade)
        {
            if (upgrade == null) yield break;
            var seen = new HashSet<string>();
            foreach (var r in upgrade.GetEffectiveResearchRequirements())
            {
                if (string.IsNullOrEmpty(r) || seen.Contains(r)) continue;
                seen.Add(r);
                yield return r;
            }
        }

        public static int CountResearchRequirements(OutpostUpgradeDef upgrade)
        {
            if (upgrade == null) return 0;
            var seen = new HashSet<string>();
            int n = 0;
            foreach (var r in upgrade.GetEffectiveResearchRequirements())
            {
                if (string.IsNullOrEmpty(r) || seen.Contains(r)) continue;
                seen.Add(r);
                n++;
            }
            return n;
        }

        public static PurchaseCheck EvaluatePurchase(WorldObject_WD_Outpost outpost, OutpostUpgradeDef upgrade, int level, bool ignoreLineTierForPreview = false)
        {
            var result = new PurchaseCheck
            {
                canBuy = false,
                reason = null,
                costAvailableByDefName = new Dictionary<string, bool>()
            };

            if (outpost == null || upgrade == null) { result.reason = "TSA_WD_OutpostUpgrades_Invalid".Translate(); return result; }
            if (level <= 0) { result.reason = "TSA_WD_OutpostUpgrades_Invalid".Translate(); return result; }
            if (!string.IsNullOrEmpty(outpost.PendingUpgradeDefName)) { result.reason = "TSA_WD_OutpostUpgrades_InTransit".Translate(); return result; }
            if (!upgrade.AppliesToOutpost(outpost.def.defName)) { result.reason = "TSA_WD_OutpostUpgrades_Invalid".Translate(); return result; }
            if (outpost.GetUpgradeLevel(upgrade.defName) > 0)
            {
                result.reason = "TSA_WD_OutpostUpgrades_AlreadyOwned".Translate();
                return result;
            }
            if (!ignoreLineTierForPreview)
            {
                int hi = GetHighestBuiltLineTier(outpost, upgrade.upgradeLineId);
                if (upgrade.requiresPreviousLineTier)
                {
                    if (upgrade.lineTier != hi + 1)
                    {
                        result.reason = "TSA_WD_OutpostUpgrades_NotNextTier".Translate();
                        return result;
                    }
                }
                else if (upgrade.lineTier <= hi)
                {
                    result.reason = "TSA_WD_OutpostUpgrades_AlreadyHaveLineTier".Translate();
                    return result;
                }
            }

            foreach (var req in GetResearchRequirements(upgrade))
            {
                var rp = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(req);
                if (rp != null && (Find.ResearchManager == null || Find.ResearchManager.GetProgress(rp) < rp.baseCost))
                {
                    result.reason = "TSA_WD_OutpostUpgrades_MissingResearch".Translate(rp.LabelCap);
                    return result;
                }
            }

            Map colonyMap = GetColonyMap();
            var warehouses = GetContributingWarehouses(outpost.Tile);
            if (colonyMap == null && warehouses.Count == 0) { result.reason = "TSA_WD_OutpostUpgrades_NoColonyMap".Translate(); return result; }
            if (!HasCost(colonyMap, warehouses, upgrade.GetEffectiveCost(), out result.reason, result.costAvailableByDefName)) return result;

            result.canBuy = !ignoreLineTierForPreview;
            return result;
        }

        public static bool CanPurchaseUpgrade(WorldObject_WD_Outpost outpost, OutpostUpgradeDef upgrade, int level, out string reason)
        {
            var check = EvaluatePurchase(outpost, upgrade, level);
            reason = check.reason;
            return check.canBuy;
        }

        /// <summary>True when tier, research, materials, and queue state all allow purchase right now.</summary>
        public static bool IsBuildableUpgrade(WorldObject_WD_Outpost outpost, OutpostUpgradeDef upgrade)
            => CanPurchaseUpgrade(outpost, upgrade, 1, out _);

        public static bool MaterialsAvailableForUpgrade(WorldObject_WD_Outpost outpost, OutpostUpgradeDef upgrade)
        {
            if (outpost == null || upgrade == null) return false;
            List<OutpostUpgradeCostEntry> effectiveCost = upgrade.GetEffectiveCost();
            if (effectiveCost == null || effectiveCost.Count == 0) return true;
            Map colonyMap = GetColonyMap();
            var warehouses = GetContributingWarehouses(outpost.Tile);
            if (colonyMap == null && warehouses.Count == 0) return false;
            var availability = new Dictionary<string, bool>();
            return HasCost(colonyMap, warehouses, effectiveCost, out _, availability);
        }

        public static bool TryPurchaseAndLaunch(WorldObject_WD_Outpost outpost, OutpostUpgradeDef upgrade, int level, out string reason)
        {
            reason = null;
            if (!CanPurchaseUpgrade(outpost, upgrade, level, out reason)) return false;
            Map colonyMap = GetColonyMap();
            var warehouses = GetContributingWarehouses(outpost.Tile);
            if (colonyMap == null && warehouses.Count == 0) { reason = "TSA_WD_OutpostUpgrades_NoColonyMap".Translate(); return false; }
            if (!DeductCost(colonyMap, warehouses, upgrade.GetEffectiveCost(), out reason, out _)) return false;
            if (!outpost.TryQueuePendingUpgrade(upgrade.defName, level)) { reason = "TSA_WD_OutpostUpgrades_QueueFailed".Translate(); return false; }
            WorldObject origin = ResolveUpgradeOrigin(outpost, colonyMap, warehouses);
            if (origin == null || !WorldActions_Traveler.SpawnOutpostUpgradeTraveler(outpost, origin, upgrade.defName, level))
            {
                outpost.ClearPendingUpgradeIfMatches(upgrade.defName, level);
                reason = "TSA_WD_OutpostUpgrades_LaunchFailed".Translate();
                return false;
            }
            return true;
        }

        public static bool IsAnyStoneBlocksCost(OutpostUpgradeCostEntry c)
        {
            if (c == null) return false;
            if (c.costMode == OutpostUpgradeCostMode.AnyStoneBlocks) return true;
            return c.thingDef != null && c.thingDef.defName != null && c.thingDef.defName.StartsWith("Blocks");
        }

        public static string GetCostDisplayLabel(OutpostUpgradeCostEntry c)
        {
            if (c == null) return "";
            if (IsAnyStoneBlocksCost(c))
                return "TSA_WD_OutpostUpgrades_AnyStoneBlocks".Translate().ToString();
            return c.thingDef?.LabelCap ?? "—";
        }

        private static bool HasCost(Map map, List<WorldObject_WD_Outpost> warehouses, List<OutpostUpgradeCostEntry> cost, out string reason, Dictionary<string, bool> availabilityByDefName = null)
        {
            reason = null;
            if (cost == null || cost.Count == 0) return true;
            foreach (var c in cost)
            {
                if (c?.thingDef == null || c.count <= 0) continue;
                bool isStone = IsAnyStoneBlocksCost(c);
                int have = isStone
                    ? CountAnyStoneBlocks(map) + CountWarehouseStoneBlocks(warehouses)
                    : CountAvailable(map, c.thingDef) + CountWarehouseStored(warehouses, c.thingDef);
                if (availabilityByDefName != null)
                    availabilityByDefName[c.thingDef.defName] = have >= c.count;
                if (have < c.count)
                {
                    reason = "TSA_WD_OutpostUpgrades_NeedHave".Translate(c.count.ToString(), GetCostDisplayLabel(c), have.ToString());
                    return false;
                }
            }
            return true;
        }

        private static bool DeductCost(
            Map map,
            List<WorldObject_WD_Outpost> warehouses,
            List<OutpostUpgradeCostEntry> cost,
            out string reason,
            out HashSet<WorldObject_WD_Outpost> warehousesThatContributed)
        {
            reason = null;
            warehousesThatContributed = new HashSet<WorldObject_WD_Outpost>();
            if (cost == null || cost.Count == 0) return true;
            foreach (var c in cost)
            {
                if (c?.thingDef == null || c.count <= 0) continue;
                int toRemove = c.count;
                bool isStone = IsAnyStoneBlocksCost(c);

                if (map != null)
                    toRemove -= DeductFromMap(map, c.thingDef, isStone, toRemove);

                if (toRemove > 0 && warehouses != null)
                {
                    for (int i = 0; i < warehouses.Count && toRemove > 0; i++)
                    {
                        var comp = CompOutpostWarehouse.Get(warehouses[i]);
                        if (comp == null) continue;
                        int took = isStone ? comp.WithdrawStoneBlocksUpTo(toRemove) : comp.WithdrawUpTo(c.thingDef, toRemove);
                        if (took > 0)
                            warehousesThatContributed.Add(warehouses[i]);
                        toRemove -= took;
                    }
                }

                if (toRemove > 0)
                {
                    reason = "TSA_WD_OutpostUpgrades_DeductFailed".Translate(GetCostDisplayLabel(c));
                    return false;
                }
            }
            return true;
        }

        private static int DeductFromMap(Map map, ThingDef def, bool isStone, int amount)
        {
            if (map == null || amount <= 0) return 0;
            int toRemove = amount;
            var source = isStone ? map.listerThings.AllThings : map.listerThings.ThingsOfDef(def);
            var pool = new List<Thing>();
            for (int pi = 0; pi < source.Count; pi++)
            {
                var item = source[pi];
                if (!item.Spawned) continue;
                if (isStone && (item.def?.defName == null || !item.def.defName.StartsWith("Blocks"))) continue;
                pool.Add(item);
            }
            foreach (var t in pool)
            {
                if (toRemove <= 0) break;
                int take = Mathf.Min(toRemove, t.stackCount);
                if (take == t.stackCount) t.Destroy(DestroyMode.Vanish);
                else t.SplitOff(take).Destroy(DestroyMode.Vanish);
                toRemove -= take;
            }
            return amount - toRemove;
        }

        private static int CountAvailable(Map map, ThingDef def)
        {
            if (map == null || def == null) return 0;
            var things = map.listerThings.ThingsOfDef(def);
            int total = 0;
            for (int i = 0; i < things.Count; i++)
                if (things[i].Spawned) total += things[i].stackCount;
            return total;
        }

        private static int CountAnyStoneBlocks(Map map)
        {
            if (map == null) return 0;
            var things = map.listerThings.AllThings;
            int total = 0;
            for (int i = 0; i < things.Count; i++)
            {
                var t = things[i];
                if (t.Spawned && t.def?.defName != null && t.def.defName.StartsWith("Blocks"))
                    total += t.stackCount;
            }
            return total;
        }
    }
}
