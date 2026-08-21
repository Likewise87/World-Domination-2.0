using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Post-victory clash loot picker (Ally Gift row style, mass-gated).</summary>
    public class Dialog_WdCaravanClashLoot : Window
    {
        private readonly WD_MapComponent_CaravanClash tracker;
        private readonly Map map;
        private readonly List<WD_CaravanClashLootUtility.LootPrisonerRow> prisoners;
        private readonly List<WD_CaravanClashLootUtility.LootItemRow> items;
        private readonly Dictionary<string, string> countEditBuffers = new Dictionary<string, string>();
        private Vector2 scrollPosition;
        private string searchTerm = "";
        private bool resolved;

        private const float PrisonerRowHeight = 52f;
        private const float ItemRowHeight = 36f;
        private const float RowGap = 3f;
        private const float IconSize = 28f;
        private const float PrisonerIconSize = 36f;
        private const float FooterHeight = 78f;
        private const float BtnW = 28f;
        private const float MaxBtnW = 40f;
        private const float CountColW = 56f;
        private const float BtnGap = 4f;

        private static float ControlsWidth => BtnW + CountColW + BtnW + BtnGap + BtnW + BtnGap + MaxBtnW;

        public override Vector2 InitialSize => new Vector2(760f, 720f);

        public Dialog_WdCaravanClashLoot(
            WD_MapComponent_CaravanClash tracker,
            Map map,
            List<WD_CaravanClashLootUtility.LootPrisonerRow> prisoners,
            List<WD_CaravanClashLootUtility.LootItemRow> items)
        {
            this.tracker = tracker;
            this.map = map;
            this.prisoners = prisoners ?? new List<WD_CaravanClashLootUtility.LootPrisonerRow>();
            this.items = items ?? new List<WD_CaravanClashLootUtility.LootItemRow>();
            doCloseX = true;
            doCloseButton = false;
            absorbInputAroundWindow = true;
            forcePause = true;
            closeOnAccept = false;
            closeOnCancel = false;
        }

        public override void PostClose()
        {
            base.PostClose();
            if (!resolved)
                Resolve(takeNothing: true);
        }

        public override void DoWindowContents(Rect inRect)
        {
            float y = 0f;

            Text.Font = GameFont.Medium;
            Rect titleRect = new Rect(0f, y, inRect.width, 34f);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(titleRect, "TSA_WD_ClashLoot_Title".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            y += 36f;

            string desc = "TSA_WD_ClashLoot_Desc".Translate();
            float descH = Mathf.Max(24f, Text.CalcHeight(desc, inRect.width));
            Widgets.Label(new Rect(0f, y, inRect.width, descH), desc);
            y += descH + 8f;

            // Same-row filter + reset (GiftDeal layout).
            const float filterFieldH = 24f;
            const float resetW = 110f;
            const float btnH = 28f;
            Rect searchRect = new Rect(0f, y, inRect.width - resetW - 8f, filterFieldH);
            searchTerm = Widgets.TextField(searchRect, searchTerm ?? "");
            if (string.IsNullOrEmpty(searchTerm))
            {
                GUI.color = new Color(1f, 1f, 1f, 0.4f);
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.Font = GameFont.Tiny;
                Widgets.Label(searchRect, "TSA_WD_ClashLoot_SearchPlaceholder".Translate());
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
            }

            float btnTop = y + (filterFieldH - btnH) / 2f;
            Rect resetRect = new Rect(inRect.width - resetW, btnTop, resetW, btnH);
            if (Widgets.ButtonText(resetRect, "TSA_WD_ClashLoot_ResetAll".Translate()))
                ResetAllSelections();
            y += 30f;

            WD_CaravanClashLootUtility.GetMassTotals(map, prisoners, items, out float capacity, out float usage);
            float free = capacity - usage;
            bool over = usage > capacity + 0.05f;

            float listBottom = inRect.height - FooterHeight;
            Rect outRect = new Rect(0f, y, inRect.width, Mathf.Max(40f, listBottom - y));

            float viewHeight = 8f;
            for (int i = 0; i < prisoners.Count; i++)
            {
                if (MatchesSearch(prisoners[i]))
                    viewHeight += PrisonerRowHeight + RowGap;
            }
            for (int i = 0; i < items.Count; i++)
            {
                if (MatchesSearch(items[i]))
                    viewHeight += ItemRowHeight + RowGap;
            }

            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, Mathf.Max(viewHeight, outRect.height));

            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
            float ry = 0f;

            for (int i = 0; i < prisoners.Count; i++)
            {
                if (!MatchesSearch(prisoners[i])) continue;
                DrawPrisonerRow(new Rect(0f, ry, viewRect.width, PrisonerRowHeight), prisoners[i], free, over);
                ry += PrisonerRowHeight + RowGap;
            }

            for (int i = 0; i < items.Count; i++)
            {
                if (!MatchesSearch(items[i])) continue;
                DrawItemRow(0f, ry, viewRect.width, items[i], free);
                ry += ItemRowHeight + RowGap;
            }

            Widgets.EndScrollView();

            WD_CaravanClashLootUtility.GetMassTotals(map, prisoners, items, out capacity, out usage);
            over = usage > capacity + 0.05f;

            Rect footer = new Rect(0f, inRect.height - FooterHeight, inRect.width, FooterHeight);
            DrawFooter(footer, usage, capacity, over);
        }

        private bool MatchesSearch(WD_CaravanClashLootUtility.LootPrisonerRow entry)
        {
            if (string.IsNullOrEmpty(searchTerm)) return true;
            if (entry?.Pawn == null) return false;
            string name = entry.Pawn.LabelShortCap ?? "";
            string faction = entry.Pawn.Faction?.Name ?? entry.Pawn.Faction?.def?.LabelCap ?? "";
            return name.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) >= 0
                || faction.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool MatchesSearch(WD_CaravanClashLootUtility.LootItemRow entry)
        {
            if (string.IsNullOrEmpty(searchTerm)) return true;
            if (entry?.Thing == null) return false;
            string label = entry.Thing.LabelCap ?? entry.Thing.def?.label ?? "";
            return label.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ResetAllSelections()
        {
            for (int i = 0; i < prisoners.Count; i++)
            {
                if (prisoners[i] != null) prisoners[i].Selected = false;
            }
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] != null) items[i].SelectedCount = 0;
            }
            countEditBuffers.Clear();
        }

        private static string ItemKey(WD_CaravanClashLootUtility.LootItemRow entry)
        {
            Thing thing = entry?.Thing;
            if (thing == null) return "item";
            return thing.GetUniqueLoadID() ?? thing.ThingID ?? thing.def?.defName ?? "item";
        }

        private int GetPick(WD_CaravanClashLootUtility.LootItemRow entry)
        {
            if (entry == null) return 0;
            return Mathf.Clamp(entry.SelectedCount, 0, entry.MaxCount);
        }

        /// <summary>Same as <see cref="Dialog_SettlementGiftDeal"/> SetOffer: keep buffer in sync with buttons.</summary>
        private void SetPick(WD_CaravanClashLootUtility.LootItemRow entry, string key, int value)
        {
            if (entry == null) return;
            value = Mathf.Clamp(value, 0, entry.MaxCount);
            entry.SelectedCount = value;
            countEditBuffers[key] = value.ToString();
        }

        private void DrawPrisonerRow(Rect row, WD_CaravanClashLootUtility.LootPrisonerRow entry, float freeMass, bool currentlyOver)
        {
            if (entry?.Pawn == null || entry.Pawn.Destroyed) return;
            Pawn pawn = entry.Pawn;

            Widgets.DrawMenuSection(row);
            if (Mouse.IsOver(row)) Widgets.DrawHighlight(row);

            Rect portraitRect = new Rect(row.x + 8f, row.y + (row.height - PrisonerIconSize) / 2f, PrisonerIconSize, PrisonerIconSize);
            Texture portrait = PawnPortraitUIUtils.GetPortrait(pawn, new Vector2(PrisonerIconSize, PrisonerIconSize));
            if (portrait != null)
                GUI.DrawTexture(portraitRect, portrait, ScaleMode.ScaleToFit);
            else
                Widgets.DrawBoxSolid(portraitRect, new Color(0.25f, 0.25f, 0.3f, 1f));

            float textX = portraitRect.xMax + 8f;
            if (pawn.Faction != null)
            {
                Rect facIcon = new Rect(textX, row.y + (row.height - 22f) / 2f, 22f, 22f);
                WorldDomination_UIUtils.DrawFactionIconWithColor(facIcon, pawn.Faction);
                textX = facIcon.xMax + 8f;
            }

            float pawnMass = pawn.GetStatValue(StatDefOf.Mass) + MassUtility.GearAndInventoryMass(pawn);
            string factionName = pawn.Faction?.Name ?? pawn.Faction?.def?.LabelCap ?? "";

            Rect nameRect = new Rect(textX, row.y + 4f, row.width - textX - 120f, 24f);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(nameRect, pawn.LabelShortCap);

            Rect subRect = new Rect(textX, row.y + 28f, nameRect.width, 18f);
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Widgets.Label(subRect, "TSA_WD_ClashLoot_PrisonerSub".Translate(factionName, pawnMass.ToString("F0")));
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;

            // Portrait / name open vanilla info card (Take button stays its own hit target).
            if (Widgets.ButtonInvisible(portraitRect) || Widgets.ButtonInvisible(nameRect))
                Find.WindowStack.Add(new Dialog_InfoCard(pawn));

            Rect btn = new Rect(row.xMax - 108f, row.y + (row.height - 28f) / 2f, 100f, 28f);
            bool canSelect = entry.Selected || (!currentlyOver && pawnMass <= freeMass + 0.05f);
            string label = entry.Selected
                ? "TSA_WD_ClashLoot_Selected".Translate()
                : "TSA_WD_ClashLoot_Take".Translate();
            if (!canSelect && !entry.Selected)
                GUI.enabled = false;
            if (Widgets.ButtonText(btn, label))
            {
                if (entry.Selected)
                    entry.Selected = false;
                else if (canSelect)
                    entry.Selected = true;
            }
            GUI.enabled = true;
        }

        /// <summary>Quantity controls copied from <see cref="Dialog_SettlementGiftDeal"/> DrawItemRow.</summary>
        private void DrawItemRow(float x, float y, float width, WD_CaravanClashLootUtility.LootItemRow entry, float freeMass)
        {
            if (entry?.Thing == null || entry.Thing.Destroyed) return;
            Thing thing = entry.Thing;
            string key = ItemKey(entry);
            int have = entry.MaxCount;

            float unitMass = thing.GetStatValue(StatDefOf.Mass);
            if (unitMass <= 0f) unitMass = thing.def?.BaseMass ?? 0f;
            float freeIgnoringThis = freeMass + unitMass * entry.SelectedCount;
            int maxAffordable = WD_CaravanClashLootUtility.MaxCountAffordable(thing, have, freeIgnoringThis);

            int pick = Mathf.Clamp(GetPick(entry), 0, have);
            if (pick > maxAffordable)
            {
                SetPick(entry, key, maxAffordable);
                pick = maxAffordable;
            }

            Rect rowRect = new Rect(x, y, width, ItemRowHeight);
            Widgets.DrawMenuSection(rowRect);
            if (Mouse.IsOver(rowRect)) Widgets.DrawHighlight(rowRect);

            float contentY = y + ItemRowHeight / 2f;
            Rect iconRect = new Rect(x + 8f, contentY - IconSize / 2f, IconSize, IconSize);
            Widgets.ThingIcon(iconRect, thing);

            float controlsX = x + width - ControlsWidth - 8f;
            float nameW = controlsX - iconRect.xMax - 12f;
            Text.Font = GameFont.Small;
            TextAnchor prev = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(iconRect.xMax + 10f, y, nameW * 0.62f, ItemRowHeight), thing.LabelCap.Truncate(nameW * 0.62f - 4f));
            GUI.color = Color.gray;
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(iconRect.xMax + 10f + nameW * 0.62f, y, nameW * 0.38f, ItemRowHeight),
                "TSA_WD_ClashLoot_ItemSub".Translate(have.ToString(), unitMass.ToString("F1")));
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Text.Anchor = prev;

            float btnY = y + (ItemRowHeight - BtnW) / 2f;
            float cx = controlsX;
            Rect minusRect = new Rect(cx, btnY, BtnW, BtnW);
            cx += BtnW;
            Rect countRect = new Rect(cx, y + 4f, CountColW, ItemRowHeight - 8f);
            cx += CountColW;
            Rect plusRect = new Rect(cx, btnY, BtnW, BtnW);
            cx += BtnW + BtnGap;
            Rect zeroRect = new Rect(cx, btnY, BtnW, BtnW);
            cx += BtnW + BtnGap;
            Rect maxRect = new Rect(cx, btnY, MaxBtnW, BtnW);

            int step = WdQuantityUI.AdjustmentStep();
            if (WdDragSelectButtons.ButtonText(minusRect, "-", WdDragSelectButtons.Hash(key, "minus")) && pick > 0)
                SetPick(entry, key, Mathf.Max(0, pick - step));
            if (WdDragSelectButtons.ButtonText(plusRect, "+", WdDragSelectButtons.Hash(key, "plus")) && pick < maxAffordable)
                SetPick(entry, key, Mathf.Min(maxAffordable, pick + step));
            if (WdDragSelectButtons.ButtonText(zeroRect, "0", WdDragSelectButtons.Hash(key, "zero")))
                SetPick(entry, key, 0);
            if (WdDragSelectButtons.ButtonText(maxRect, "Max", WdDragSelectButtons.Hash(key, "max")))
                SetPick(entry, key, maxAffordable);
            TooltipHandler.TipRegion(minusRect, "TSA_WD_QuantityAdjustTip".Translate());
            TooltipHandler.TipRegion(plusRect, "TSA_WD_QuantityAdjustTip".Translate());

            pick = GetPick(entry);
            if (!countEditBuffers.TryGetValue(key, out string buffer) || buffer == null)
                buffer = pick.ToString();
            int edited = pick;
            TextAnchor prevAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.TextFieldNumeric(countRect, ref edited, ref buffer, 0f, maxAffordable);
            Text.Anchor = prevAnchor;
            countEditBuffers[key] = buffer;
            if (edited != pick)
                SetPick(entry, key, Mathf.Clamp(edited, 0, maxAffordable));
        }

        private void DrawFooter(Rect footer, float usage, float capacity, bool over)
        {
            Text.Font = GameFont.Small;
            Rect massRect = new Rect(footer.x, footer.y + 4f, footer.width, 24f);
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = over ? ColorLibrary.RedReadable : Color.white;
            Widgets.Label(massRect, "TSA_WD_ClashLoot_Mass".Translate(usage.ToString("F0"), capacity.ToString("F0")));
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;

            float btnW = 150f;
            float btnH = 36f;
            Rect nothingRect = new Rect(footer.x, footer.yMax - btnH - 4f, btnW, btnH);
            Rect confirmRect = new Rect(footer.xMax - btnW, footer.yMax - btnH - 4f, btnW, btnH);

            if (Widgets.ButtonText(nothingRect, "TSA_WD_ClashLoot_TakeNothing".Translate()))
                Resolve(takeNothing: true);

            if (over) GUI.enabled = false;
            if (Widgets.ButtonText(confirmRect, "TSA_WD_ClashLoot_Confirm".Translate()))
                Resolve(takeNothing: false);
            GUI.enabled = true;
        }

        private void Resolve(bool takeNothing)
        {
            if (resolved) return;
            resolved = true;

            if (takeNothing)
                ResetAllSelections();

            tracker?.CompleteVictoryLoot(prisoners, items);
            if (IsOpen)
                Close(doCloseSound: false);
        }
    }
}
