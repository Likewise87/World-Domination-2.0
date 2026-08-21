using UnityEngine;
using Verse;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse.Sound;

namespace TSA_WorldDomination
{
    /// <summary>Per-pair locks for allowed diplomatic relation changes. Opened from mod settings → Miscellaneous (<see cref="WorldDominationMod.DoSettingsWindowContents"/>).</summary>
    public class Dialog_AllegianceLock : Window
    {
        private Vector2 scrollPosition = Vector2.zero;
        private string searchTerm = "";
        private string lastAppliedFilter;
        private List<Pair<Faction, Faction>> factionPairs = new List<Pair<Faction, Faction>>();
        private List<Pair<Faction, Faction>> filteredPairsCache = new List<Pair<Faction, Faction>>();
        private readonly string windowTitle;
        private static string s_filterPlaceholder;

        public override Vector2 InitialSize => new Vector2(850f, 750f);

        public Dialog_AllegianceLock()
        {
            if (Current.ProgramState != ProgramState.Playing)
            {
                Messages.Message("TSA_WD_AllegianceMatrixInGameOnly".Translate(), MessageTypeDefOf.RejectInput, false);
                this.Close();
                return;
            }

            doCloseButton = true;
            forcePause = true;
            closeOnClickedOutside = true;
            doWindowBackground = true;
            windowTitle = "TSA_WD_OpenAllegianceMatrix".Translate();
            optionalTitle = null;
            absorbInputAroundWindow = true;
            if (s_filterPlaceholder == null)
                s_filterPlaceholder = "TSA_WD_FilterByName".Translate();

            WorldDominationMod.settings.EnsureInitialLaunchDefaults();
            RefreshFactionPairs();
        }

        private void RefreshFactionPairs()
        {
            factionPairs.Clear();
            // Random WD diplomacy never touches the player; locks are NPC×NPC only (matches TryChangeAllegiances pool).
            var allFactions = Find.FactionManager.AllFactionsVisible
                .Where(f => f != null && !f.IsPlayer && !WorldActions_Utils.IsExcludedFaction(f))
                .OrderBy(f => f.def.LabelCap.Resolve())
                .ToList();

            for (int i = 0; i < allFactions.Count; i++)
            {
                for (int j = 0; j < allFactions.Count; j++)
                {
                    if (i == j) continue;
                    factionPairs.Add(new Pair<Faction, Faction>(allFactions[i], allFactions[j]));
                }
            }

            lastAppliedFilter = null;
        }

        private static bool FactionMatchesFilter(Faction f, string searchLower)
        {
            if (f == null) return false;
            if (!string.IsNullOrEmpty(f.Name) && f.Name.ToLowerInvariant().Contains(searchLower))
                return true;
            string label = f.def?.LabelCap.Resolve();
            return !string.IsNullOrEmpty(label) && label.ToLowerInvariant().Contains(searchLower);
        }

        public override void DoWindowContents(Rect inRect)
        {
            Rect contentRect = SettingsUI.DrawWindowTitle(inRect, windowTitle);
            var s = WorldDominationMod.settings;

            // BUTTON ROW
            float btnW = 115f;
            float btnGap = 5f;
            float curX = contentRect.xMax - btnW;

            Rect btnResetRect = new Rect(curX, contentRect.y, btnW, 30f);
            if (Widgets.ButtonText(btnResetRect, "TSA_WD_BtnReset".Translate()))
            {
                s.ResetAllegianceLocks(false);
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
            }
            TooltipHandler.TipRegion(btnResetRect, "TSA_WD_BtnReset_Tooltip".Translate());

            curX -= (btnW + btnGap);
            Rect btnResetGlobalRect = new Rect(curX, contentRect.y, btnW, 30f);
            if (Widgets.ButtonText(btnResetGlobalRect, "TSA_WD_BtnResetGlobal".Translate()))
            {
                s.ResetAllegianceLocks(true);
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
            }
            TooltipHandler.TipRegion(btnResetGlobalRect, "TSA_WD_BtnResetGlobal_Tooltip".Translate());

            curX -= (btnW + btnGap);
            Rect btnAllowAllRect = new Rect(curX, contentRect.y, btnW, 30f);
            if (Widgets.ButtonText(btnAllowAllRect, "TSA_WD_BtnAllowAll".Translate()))
            {
                s.lockedAllegiancePairs.Clear();
                SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
            }
            TooltipHandler.TipRegion(btnAllowAllRect, "TSA_WD_BtnAllowAll_Tooltip".Translate());

            curX -= (btnW + btnGap);
            Rect btnLockAllRect = new Rect(curX, contentRect.y, btnW, 30f);
            if (Widgets.ButtonText(btnLockAllRect, "TSA_WD_BtnLockAll".Translate()))
            {
                foreach (var pair in factionPairs)
                    s.lockedAllegiancePairs.Add(s.GetFactionPairKey(pair.First, pair.Second));
                SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
            }
            TooltipHandler.TipRegion(btnLockAllRect, "TSA_WD_BtnLockAll_Tooltip".Translate());

            // Filter row (same pattern as Window_OutpostOverview: TextField + gray placeholder when empty)
            Rect searchRowRect = new Rect(contentRect.x, contentRect.y + 45f, contentRect.width, 30f);
            Rect searchRect = searchRowRect.LeftPart(0.28f);
            string oldSearch = searchTerm;
            searchTerm = Widgets.TextField(searchRect, searchTerm);
            if (string.IsNullOrEmpty(searchTerm))
            {
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = new Color(1f, 1f, 1f, 0.4f);
                Widgets.Label(searchRect.ContractedBy(4f, 0f), s_filterPlaceholder);
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
            }

            float clearBtnWidth = 60f;
            Rect clearBtnRect = new Rect(searchRect.xMax + 5f, searchRowRect.y, clearBtnWidth, 30f);
            if (Widgets.ButtonText(clearBtnRect, "TSA_WD_BtnClear".Translate()))
                searchTerm = "";

            Rect infoLabelRect = new Rect(clearBtnRect.xMax + 15f, searchRowRect.y, searchRowRect.xMax - clearBtnRect.xMax - 15f, 30f);
            GUI.color = Color.gray;
            Text.Anchor = TextAnchor.MiddleLeft;
            Text.Font = GameFont.Tiny;
            Widgets.Label(infoLabelRect, "TSA_WD_AllegianceLock_ScopeInfo".Translate());
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;

            if (searchTerm != oldSearch || lastAppliedFilter != searchTerm)
            {
                lastAppliedFilter = searchTerm;
                filteredPairsCache.Clear();
                string searchLower = string.IsNullOrEmpty(searchTerm) ? null : searchTerm.ToLowerInvariant();
                for (int i = 0; i < factionPairs.Count; i++)
                {
                    var p = factionPairs[i];
                    if (searchLower == null
                        || FactionMatchesFilter(p.First, searchLower)
                        || FactionMatchesFilter(p.Second, searchLower))
                        filteredPairsCache.Add(p);
                }
            }

            // MAIN LIST AREA
            Rect outRect = new Rect(contentRect.x, searchRowRect.yMax + 10f, contentRect.width, contentRect.height - 130f);
            Widgets.DrawMenuSection(outRect);

            float viewWidth = outRect.width - 24f;
            const float rowStep = 36f;
            Rect viewRect = new Rect(0f, 0f, viewWidth, filteredPairsCache.Count * rowStep);
            Rect scrollRect = outRect.ContractedBy(4f);
            Widgets.BeginScrollView(scrollRect, ref scrollPosition, viewRect);

            for (int i = 0; i < filteredPairsCache.Count; i++)
            {
                var pair = filteredPairsCache[i];
                Rect rowRect = new Rect(0f, i * rowStep, viewWidth, 32f);
                if (i % 2 == 0) Widgets.DrawHighlight(rowRect);
                if (Mouse.IsOver(rowRect)) Widgets.DrawLightHighlight(rowRect);

                string key = s.GetFactionPairKey(pair.First, pair.Second);
                bool isLocked = s.lockedAllegiancePairs.Contains(key);

                float statusW = 180f;
                float sideW = (rowRect.width - statusW) / 2f;
                float pad = 40f;

                DrawFactionColumn(new Rect(rowRect.x + pad, rowRect.y, sideW - pad, rowRect.height), pair.First);

                Rect btnRect = new Rect(rowRect.x + sideW, rowRect.y + 2f, statusW, 28f);
                if (Widgets.ButtonText(btnRect, isLocked ? "TSA_WD_StatusLocked".Translate().Colorize(Color.red) : "TSA_WD_StatusPossible".Translate().Colorize(Color.white)))
                {
                    if (isLocked) s.lockedAllegiancePairs.Remove(key);
                    else s.lockedAllegiancePairs.Add(key);

                    if (isLocked) SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
                    else SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
                }

                DrawFactionColumn(new Rect(btnRect.xMax + pad, rowRect.y, sideW - pad, rowRect.height), pair.Second);
            }

            Widgets.EndScrollView();
        }

        private void DrawFactionColumn(Rect rect, Faction faction)
        {
            float iconSize = 22f;
            Rect iconRect = new Rect(rect.x, rect.y + (rect.height - iconSize) / 2f, iconSize, iconSize);
            Rect textRect = new Rect(iconRect.xMax + 6f, rect.y, rect.width - (iconSize + 6f), rect.height);

            GUI.color = faction.Color;
            Widgets.DrawTextureFitted(iconRect, faction.def.FactionIcon, 1f);
            GUI.color = Color.white;

            Text.Anchor = TextAnchor.MiddleLeft;
            Text.Font = GameFont.Tiny;
            Widgets.Label(textRect, (faction.IsPlayer ? $"{faction.Name} (Player)" : faction.Name).Truncate(textRect.width));
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }
    }

    /// <summary>Vanilla goodwill tweak rows used by <see cref="Dialog_DiplomacySettings"/>.</summary>
    internal static class VanillaGoodwillSettingsUI
    {
        internal static void DrawListingRows(Listing_Standard l, WorldDominationSettings s)
        {
            l.CheckboxLabeled(
                "TS_WD_Threat_NoGoodwillHostiles".Translate(),
                ref s.noGoodwillFromHostilesOnConquest,
                SettingsUI.TooltipWithDefault("TS_WD_Threat_NoGoodwillHostilesTooltip".Translate(), WorldDominationSettings.DefNoGoodwillFromHostilesOnConquest));
            l.CheckboxLabeled(
                "TSA_WD_DisableSettlementProximityGoodwill".Translate(),
                ref s.disableSettlementProximityGoodwill,
                SettingsUI.TooltipWithDefault("TSA_WD_DisableSettlementProximityGoodwillTooltip".Translate(), WorldDominationSettings.DefDisableSettlementProximityGoodwill));
        }
    }
}
