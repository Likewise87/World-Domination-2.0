using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace TSA_WorldDomination
{
    /// <summary>Experts-style master/detail: outpost picker (left) drives shell list (right).</summary>
    [StaticConstructorOnStartup]
    public class Dialog_WdAssaultArtillery : Window
    {
        private static readonly Color HeaderTint = new Color(0.75f, 0.82f, 1f);

        private readonly Map map;
        private Vector2 outpostScroll;
        private Vector2 shellScroll;

        private bool selectedIsGhostRelay;
        private int selectedOutpostId = -1;

        private List<ThingDef> cachedShells = new List<ThingDef>();
        private bool shellCacheIsGhost;
        private int shellCacheOutpostId = int.MinValue;
        private WD_AssaultArtillerySupport.ShellUnlockTier shellCacheTier =
            (WD_AssaultArtillerySupport.ShellUnlockTier)255;
        private string cachedRightHeader = string.Empty;

        private const float ColGap = 18f;
        private const float ScrollBarW = 16f;
        private const float IconColW = 44f;
        private const float IconPadding = 6f;
        private const float ListRightMargin = 8f;
        private const float RowPadding = Outpost_Upgrade_UI.CompactRowPadding;
        private const float NameLabelHeight = Outpost_Dialog_UI.ListRowNameHeight;
        private const float FormulaLineHeight = Outpost_Dialog_UI.ListRowFormulaLineHeight;
        private const float FormulaTopPadding = Outpost_Dialog_UI.ListRowFormulaTopPadding;
        private const float OutpostRowContentH = NameLabelHeight + Outpost_Dialog_UI.ListRowFormulaBlockHeight;
        private const float ListRowH = OutpostRowContentH + RowPadding;
        private const float UpgradeIconSize = 18f;
        private const float OutpostIconSize = Outpost_Upgrade_UI.CompactRowIconSize;

        public override Vector2 InitialSize => new Vector2(900f, 560f);

        public Dialog_WdAssaultArtillery(Map map)
        {
            this.map = map;
            doCloseX = true;
            doCloseButton = false;
            draggable = true;
            absorbInputAroundWindow = false;
            forcePause = false;
            closeOnClickedOutside = true;
            preventCameraMotion = false;
            optionalTitle = null;

            Settlement? settlement = WD_AssaultArtillerySupport.GetAssaultedSettlement(map);
            AssaultArtilleryMenuData menu = WD_AssaultArtillerySupport.GetAssaultArtilleryMenuCached(settlement, force: true);
            ApplySelectionKey(menu.FirstSelectable);
            RefreshShellCache(ResolveSelected(menu));
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (map == null || map.Disposed)
            {
                Close();
                return;
            }

            Settlement? settlement = WD_AssaultArtillerySupport.GetAssaultedSettlement(map);
            AssaultArtilleryMenuData menu = WD_AssaultArtillerySupport.GetAssaultArtilleryMenuCached(settlement);
            AssaultArtilleryOutpostEntry? selected = ResolveSelected(menu);
            if (selected == null && menu.FirstSelectable != null)
            {
                ApplySelectionKey(menu.FirstSelectable);
                selected = menu.FirstSelectable;
            }
            RefreshShellCache(selected);

            float y = 0f;
            Text.Font = GameFont.Medium;
            LabelAnchored(
                new Rect(0f, y, inRect.width, Outpost_Dialog_UI.DialogTitleHeight),
                "TSA_WD_AssaultArtillery_Title".Translate(),
                TextAnchor.MiddleLeft);
            y += Outpost_Dialog_UI.DialogTitleRowAdvance;
            Text.Font = GameFont.Small;
            Widgets.DrawLineHorizontal(0f, y, inRect.width);
            y += 6f;

            WD_MapComponent_AssaultArtillery? tracker = map.GetComponent<WD_MapComponent_AssaultArtillery>();
            if (tracker != null && tracker.HasPendingStrike)
            {
                GUI.color = new Color(0.85f, 0.85f, 0.55f);
                LabelAnchored(
                    new Rect(0f, y, inRect.width, Outpost_Dialog_UI.PauseHeaderHeight),
                    "TSA_WD_AssaultArtillery_StrikeInboundInfo".Translate(),
                    TextAnchor.MiddleLeft);
                GUI.color = Color.white;
                y += Outpost_Dialog_UI.PauseHeaderHeight + 4f;
            }

            float columnsTop = y;
            float columnsBottom = inRect.height;
            float leftW = Mathf.Max(260f, inRect.width * 0.42f);
            Rect leftArea = new Rect(0f, columnsTop, leftW, columnsBottom - columnsTop);
            Rect rightArea = new Rect(leftW + ColGap, columnsTop, inRect.width - leftW - ColGap, columnsBottom - columnsTop);
            Widgets.DrawLineVertical(leftW + ColGap * 0.5f, columnsTop, columnsBottom - columnsTop);

            DrawLeftColumn(leftArea, menu, selected);
            DrawRightColumn(rightArea, selected);
        }

        private void DrawLeftColumn(Rect leftArea, AssaultArtilleryMenuData menu, AssaultArtilleryOutpostEntry? selected)
        {
            float lx = leftArea.x;
            float lw = leftArea.width;
            float ly = leftArea.y;

            GUI.color = HeaderTint;
            Widgets.Label(new Rect(lx, ly, lw, 24f), "TSA_WD_AssaultArtillery_ColumnOutposts".Translate());
            GUI.color = Color.white;
            ly += 26f;

            float scrollHeight = leftArea.yMax - ly;
            float contentH = 4f;
            contentH += (menu.GhostRelay.Count + menu.Ready.Count + menu.Disabled.Count) * ListRowH;
            if (menu.Disabled.Count > 0)
                contentH += 22f;
            if (contentH < scrollHeight) contentH = scrollHeight;

            bool needsScroll = contentH > scrollHeight + 0.01f;
            float viewW = needsScroll ? lw - ScrollBarW : lw;
            Rect scrollOuter = new Rect(lx, ly, lw, scrollHeight);
            Rect view = new Rect(0f, 0f, viewW, contentH);
            Widgets.BeginScrollView(scrollOuter, ref outpostScroll, view);

            float rowY = 0f;
            int rowIndex = 0;
            for (int i = 0; i < menu.GhostRelay.Count; i++)
            {
                DrawOutpostRow(new Rect(0f, rowY, view.width, ListRowH), menu.GhostRelay[i], selected, rowIndex);
                rowY += ListRowH;
                rowIndex++;
            }
            for (int i = 0; i < menu.Ready.Count; i++)
            {
                DrawOutpostRow(new Rect(0f, rowY, view.width, ListRowH), menu.Ready[i], selected, rowIndex);
                rowY += ListRowH;
                rowIndex++;
            }
            if (menu.Disabled.Count > 0)
            {
                rowY += 4f;
                Text.Font = GameFont.Tiny;
                GUI.color = Color.gray;
                Widgets.Label(new Rect(0f, rowY, view.width, 18f), "────────────");
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                rowY += 22f;
            }
            for (int i = 0; i < menu.Disabled.Count; i++)
            {
                DrawOutpostRow(new Rect(0f, rowY, view.width, ListRowH), menu.Disabled[i], selected, rowIndex);
                rowY += ListRowH;
                rowIndex++;
            }

            Widgets.EndScrollView();
        }

        private void DrawOutpostRow(
            Rect rowRect,
            AssaultArtilleryOutpostEntry entry,
            AssaultArtilleryOutpostEntry? selected,
            int rowIndex)
        {
            bool isSelected = IsSameSelection(entry, selected);
            bool enabled = entry.IsSelectable;

            if (rowIndex % 2 == 0)
                Widgets.DrawHighlight(rowRect);
            if (!enabled)
                Outpost_Dialog_UI.DrawUnmetRequirementsRowTint(rowRect, true);
            Outpost_Dialog_UI.DrawSelectedRowTint(rowRect, isSelected);

            float rowContentY = rowRect.y + (rowRect.height - OutpostRowContentH) * 0.5f;
            float textRight = rowRect.xMax - ListRightMargin;
            List<(Texture2D icon, string tooltip)> upgrades = null;
            if (!entry.IsGhostRelay && entry.Outpost != null)
            {
                upgrades = WD_AssaultArtillerySupport.GetShellUpgradeIcons(entry.Outpost);
                textRight -= upgrades.Count * (UpgradeIconSize + 2f);
            }

            Texture2D? icon = entry.DisplayIcon;
            Rect iconRect = new Rect(
                rowRect.x + IconPadding,
                rowContentY + (OutpostRowContentH - OutpostIconSize) * 0.5f,
                OutpostIconSize,
                OutpostIconSize);
            if (icon != null)
            {
                Color iconTint = entry.IsGhostRelay
                    ? (Faction.OfPlayer?.Color ?? Color.cyan)
                    : (entry.Outpost?.Faction?.Color ?? Color.cyan);
                if (!enabled)
                    iconTint.a = 0.55f;
                GUI.color = iconTint;
                Widgets.DrawTextureFitted(iconRect, icon, 1f);
                GUI.color = Color.white;
            }

            float labelX = rowRect.x + IconColW;
            float labelW = Mathf.Max(1f, textRight - labelX);
            LabelAnchored(
                new Rect(labelX, rowContentY, labelW, NameLabelHeight),
                entry.DisplayLabel.Truncate((int)labelW),
                TextAnchor.MiddleLeft);

            Text.Font = GameFont.Tiny;
            GUI.color = enabled ? Color.gray : new Color(0.65f, 0.65f, 0.65f);
            LabelAnchored(
                new Rect(labelX, rowContentY + NameLabelHeight + FormulaTopPadding, labelW, FormulaLineHeight),
                entry.CachedEtaSpread,
                TextAnchor.MiddleLeft);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            if (upgrades != null && upgrades.Count > 0)
            {
                float ux = rowRect.xMax - ListRightMargin - UpgradeIconSize;
                float iconY = rowContentY + (OutpostRowContentH - UpgradeIconSize) * 0.5f;
                for (int i = upgrades.Count - 1; i >= 0; i--)
                {
                    Rect uRect = new Rect(ux, iconY, UpgradeIconSize, UpgradeIconSize);
                    Widgets.DrawTextureFitted(uRect, upgrades[i].icon, 1f);
                    TooltipHandler.TipRegion(uRect, upgrades[i].tooltip);
                    ux -= UpgradeIconSize + 2f;
                }
            }

            TooltipHandler.TipRegion(rowRect, entry.CachedTip);
            Outpost_Dialog_UI.FinishSelectableListRow(rowRect, isSelected);

            if (!Widgets.ButtonInvisible(rowRect)) return;
            if (!enabled)
            {
                if (entry.OnCooldown)
                    Messages.Message("TSA_WD_Mortar_ReasonCooldown".Translate(entry.CooldownDaysLeft.ToString("F1")), MessageTypeDefOf.RejectInput, false);
                else if (entry.NoShootingSkill)
                    Messages.Message("TSA_WD_AssaultArtillery_NoShootingSkill".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            SoundDefOf.Click.PlayOneShotOnCamera();
            ApplySelectionKey(entry);
            RefreshShellCache(entry, force: true);
        }

        private void DrawRightColumn(Rect rightArea, AssaultArtilleryOutpostEntry? selected)
        {
            float rx = rightArea.x;
            float rw = rightArea.width;
            float ry = rightArea.y;

            if (selected == null || !selected.IsSelectable)
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(rx, ry, rw, 24f), "TSA_WD_AssaultArtillery_SelectOutpost".Translate());
                GUI.color = Color.white;
                return;
            }

            GUI.color = HeaderTint;
            Widgets.Label(new Rect(rx, ry, rw, 24f), cachedRightHeader);
            GUI.color = Color.white;
            ry += 26f;

            if (cachedShells.Count == 0)
            {
                GUI.color = Color.yellow;
                Widgets.Label(new Rect(rx, ry, rw, 24f), "TSA_WD_AssaultArtillery_NoShells".Translate());
                GUI.color = Color.white;
                return;
            }

            bool canFire = WD_AssaultArtillerySupport.CanIssueStrike(map, selected, out _);
            float scrollHeight = rightArea.yMax - ry;
            float contentH = cachedShells.Count * ListRowH + 4f;
            if (contentH < scrollHeight) contentH = scrollHeight;

            bool needsScroll = contentH > scrollHeight + 0.01f;
            float viewW = needsScroll ? rw - ScrollBarW : rw;
            Rect scrollOuter = new Rect(rx, ry, rw, scrollHeight);
            Rect view = new Rect(0f, 0f, viewW, contentH);
            Widgets.BeginScrollView(scrollOuter, ref shellScroll, view);

            float rowY = 0f;
            for (int i = 0; i < cachedShells.Count; i++)
            {
                DrawShellButton(new Rect(0f, rowY, view.width, ListRowH), cachedShells[i], selected, canFire, i);
                rowY += ListRowH;
            }

            Widgets.EndScrollView();
        }

        private void DrawShellButton(
            Rect rowRect,
            ThingDef shell,
            AssaultArtilleryOutpostEntry selected,
            bool enabled,
            int rowIndex)
        {
            if (rowIndex % 2 == 0)
                Widgets.DrawHighlight(rowRect);
            if (!enabled)
                Outpost_Dialog_UI.DrawUnmetRequirementsRowTint(rowRect, true);

            float rowContentY = rowRect.y + (rowRect.height - OutpostRowContentH) * 0.5f;
            Texture2D? icon = shell.uiIcon;
            float textLeft = rowRect.x + IconPadding;
            if (icon != null)
            {
                Rect iconRect = new Rect(
                    rowRect.x + IconPadding,
                    rowContentY + (OutpostRowContentH - 24f) * 0.5f,
                    24f,
                    24f);
                if (!enabled)
                    GUI.color = new Color(1f, 1f, 1f, 0.55f);
                Widgets.DrawTextureFitted(iconRect, icon, 1f);
                GUI.color = Color.white;
                textLeft = iconRect.xMax + IconPadding;
            }

            LabelAnchored(
                new Rect(textLeft, rowContentY, rowRect.xMax - textLeft - ListRightMargin, OutpostRowContentH),
                shell.LabelCap,
                TextAnchor.MiddleLeft);
            TooltipHandler.TipRegion(rowRect, shell.description);
            Outpost_Dialog_UI.FinishSelectableListRow(rowRect, false);

            if (!Widgets.ButtonInvisible(rowRect)) return;
            SoundDefOf.Click.PlayOneShotOnCamera();
            if (!enabled)
            {
                if (!WD_AssaultArtillerySupport.CanIssueStrike(map, selected, out string deny))
                    Messages.Message(deny, MessageTypeDefOf.RejectInput, false);
                return;
            }

            WD_AssaultArtillerySupport.BeginTargeting(map, selected, shell);
        }

        private void ApplySelectionKey(AssaultArtilleryOutpostEntry? entry)
        {
            if (entry == null)
            {
                selectedIsGhostRelay = false;
                selectedOutpostId = -1;
                return;
            }
            selectedIsGhostRelay = entry.IsGhostRelay;
            selectedOutpostId = entry.IsGhostRelay ? -1 : (entry.Outpost?.ID ?? -1);
        }

        private AssaultArtilleryOutpostEntry? ResolveSelected(AssaultArtilleryMenuData menu)
        {
            if (selectedIsGhostRelay)
            {
                for (int i = 0; i < menu.GhostRelay.Count; i++)
                {
                    if (menu.GhostRelay[i].IsGhostRelay)
                        return menu.GhostRelay[i];
                }
                return null;
            }

            if (selectedOutpostId < 0) return null;
            for (int i = 0; i < menu.Ready.Count; i++)
            {
                if (menu.Ready[i].Outpost?.ID == selectedOutpostId)
                    return menu.Ready[i];
            }
            for (int i = 0; i < menu.Disabled.Count; i++)
            {
                if (menu.Disabled[i].Outpost?.ID == selectedOutpostId)
                    return menu.Disabled[i];
            }
            return null;
        }

        private static bool IsSameSelection(AssaultArtilleryOutpostEntry entry, AssaultArtilleryOutpostEntry? selected)
        {
            if (selected == null) return false;
            if (entry.IsGhostRelay || selected.IsGhostRelay)
                return entry.IsGhostRelay && selected.IsGhostRelay;
            return entry.Outpost != null && selected.Outpost != null && entry.Outpost.ID == selected.Outpost.ID;
        }

        private void RefreshShellCache(AssaultArtilleryOutpostEntry? entry, bool force = false)
        {
            if (entry == null || !entry.IsSelectable)
            {
                cachedShells.Clear();
                cachedRightHeader = string.Empty;
                shellCacheOutpostId = int.MinValue;
                return;
            }

            int id = entry.IsGhostRelay ? -1 : (entry.Outpost?.ID ?? -1);
            if (!force
                && shellCacheIsGhost == entry.IsGhostRelay
                && shellCacheOutpostId == id
                && shellCacheTier == entry.Tier
                && cachedShells.Count > 0)
                return;

            shellCacheIsGhost = entry.IsGhostRelay;
            shellCacheOutpostId = id;
            shellCacheTier = entry.Tier;
            cachedShells = WD_AssaultArtillerySupport.GetUnlockedShellDefsForSupport(entry);
            cachedRightHeader = "TSA_WD_AssaultArtillery_ColumnShellsFrom".Translate().ToString();
        }

        private static void LabelAnchored(Rect rect, string text, TextAnchor anchor)
        {
            TextAnchor prev = Text.Anchor;
            Text.Anchor = anchor;
            Widgets.Label(rect, text);
            Text.Anchor = prev;
        }
    }
}
