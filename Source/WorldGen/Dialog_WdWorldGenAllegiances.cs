using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace TSA_WorldDomination
{
    /// <summary>
    /// World-gen allegiance editor (Select Starting Site). New UI; does not modify Dialog_AllegianceLock.
    /// </summary>
    public class Dialog_WdWorldGenAllegiances : Window
    {
        private static readonly Color NavSlateFill = new Color(0.16f, 0.18f, 0.22f, 0.92f);
        private static readonly Color NavBtnBgHover = new Color(0.22f, 0.26f, 0.32f, 0.96f);
        private static readonly Color NavBtnBgPress = new Color(0.12f, 0.14f, 0.17f, 0.96f);
        private static readonly Color NavBtnBgSelected = new Color(0.22f, 0.26f, 0.32f, 0.96f);
        private static readonly Color NavBtnOutline = new Color(0.55f, 0.62f, 0.72f, 0.42f);
        private static readonly Color NavBtnOutlineHover = new Color(0.78f, 0.84f, 0.92f, 0.72f);
        private static readonly Color NavBtnOutlineSelected = new Color(0.70f, 0.76f, 0.86f, 0.55f);

        private const float FactionChipW = 150f;
        private const float RelBtnW = 78f;
        private const float GoodwillW = 70f;
        private const float LockBtnW = 140f;
        private const float RowPadLeft = 8f;
        private const float ChipGap = 6f;
        private const float ArrowSlotW = 28f;
        private const float AfterSecondChipGap = 16f;
        private const float RelBtnGap = 4f;
        private const float AfterRelBtnsGap = 10f;
        private const float AfterGoodwillGap = 10f;
        private const float ScrollBarReserve = 24f;
        private const float ScrollViewInset = 4f;
        private const float WindowMarginX = 18f;
        /// <summary>Vanilla hostile floor. Max goodwill can rise via mod settings; the floor stays -100.</summary>
        private const int MinGoodwill = -100;

        /// <summary>Left pad through Lock button right edge for a full NPC×NPC row.</summary>
        private static float RowContentWidth =>
            RowPadLeft
            + FactionChipW + ChipGap
            + ArrowSlotW
            + FactionChipW + AfterSecondChipGap
            + RelBtnW * 3f + RelBtnGap * 2f + AfterRelBtnsGap
            + GoodwillW + AfterGoodwillGap
            + LockBtnW;

        private static float PreferredInRectWidth =>
            RowContentWidth + ScrollViewInset * 2f + ScrollBarReserve;

        private Vector2 scrollPosition = Vector2.zero;
        private string searchTerm = "";
        private string lastAppliedFilter;
        private readonly List<Pair<Faction, Faction>> factionPairs = new List<Pair<Faction, Faction>>();
        private readonly List<Pair<Faction, Faction>> filteredPairsCache = new List<Pair<Faction, Faction>>();
        private readonly Dictionary<string, string> goodwillEditBuffers = new Dictionary<string, string>();
        private bool freezeOnApply = true;
        private static string s_filterPlaceholder;

        public override Vector2 InitialSize =>
            new Vector2(PreferredInRectWidth + WindowMarginX * 2f, 728f);

        public Dialog_WdWorldGenAllegiances()
        {
            doCloseButton = true;
            doCloseX = true;
            absorbInputAroundWindow = true;
            forcePause = false;
            closeOnClickedOutside = true;
            optionalTitle = null;
            if (s_filterPlaceholder == null)
                s_filterPlaceholder = "TSA_WD_FilterByName".Translate();

            WorldDominationMod.settings?.EnsureInitialLaunchDefaults();
            RefreshFactionPairs();
        }

        private void RefreshFactionPairs()
        {
            factionPairs.Clear();
            var allFactions = Find.FactionManager.AllFactionsVisible
                .Where(f => f != null && (f.IsPlayer || !WorldActions_Utils.IsExcludedFaction(f)))
                .OrderBy(f => f.IsPlayer ? 0 : 1)
                .ThenBy(f => f.def.LabelCap.Resolve())
                .ToList();

            for (int i = 0; i < allFactions.Count; i++)
            {
                for (int j = i + 1; j < allFactions.Count; j++)
                    factionPairs.Add(new Pair<Faction, Faction>(allFactions[i], allFactions[j]));
            }

            // Player pairs first, then NPC×NPC.
            factionPairs.Sort((a, b) =>
            {
                int scoreA = InvolvesPlayer(a) ? 0 : 1;
                int scoreB = InvolvesPlayer(b) ? 0 : 1;
                if (scoreA != scoreB) return scoreA.CompareTo(scoreB);
                return 0;
            });

            lastAppliedFilter = null;
            goodwillEditBuffers.Clear();
        }

        private static bool InvolvesPlayer(Pair<Faction, Faction> p) =>
            p.First != null && p.Second != null && (p.First.IsPlayer || p.Second.IsPlayer);

        private static bool IsNpcNpcPair(Pair<Faction, Faction> p) =>
            p.First != null && p.Second != null && !p.First.IsPlayer && !p.Second.IsPlayer;

        private static bool IsPermanentHostileVsPlayer(Pair<Faction, Faction> p)
        {
            if (!InvolvesPlayer(p)) return false;
            Faction npc = p.First.IsPlayer ? p.Second : p.First;
            return WorldActions_Utils.IsPermanentEnemyOfPlayer(npc);
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
            float y = inRect.y;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, Outpost_Dialog_UI.DialogTitleHeight),
                "TSA_WD_WorldSetup_AllegiancesTitle".Translate());
            y += Outpost_Dialog_UI.DialogTitleRowAdvance;

            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.75f, 0.75f, 0.75f);
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 24f),
                "TSA_WD_WorldSetup_AllegiancesSubtitle".Translate(
                    WorldActions_DiplomacyBuffsNerfs.RandomDiplomacyFreezeDays));
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            y += 28f;

            var s = WorldDominationMod.settings;
            if (s == null) return;

            // Same X as the Lock button's right edge inside the scroll view.
            float contentRight = inRect.x + ScrollViewInset + RowContentWidth;

            // Row 1: Lock All … Reset Global left; Freeze right-aligned to Lock column.
            float btnW = 115f;
            float btnGap = 5f;
            float freezeW = 200f;
            float curX = inRect.x;

            Rect btnLockAllRect = new Rect(curX, y, btnW, 30f);
            if (Widgets.ButtonText(btnLockAllRect, "TSA_WD_BtnLockAll".Translate()))
            {
                foreach (var pair in factionPairs)
                    s.lockedAllegiancePairs.Add(s.GetFactionPairKey(pair.First, pair.Second));
                SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
            }
            TooltipHandler.TipRegion(btnLockAllRect, "TSA_WD_BtnLockAll_Tooltip".Translate());
            curX += btnW + btnGap;

            Rect btnAllowAllRect = new Rect(curX, y, btnW, 30f);
            if (Widgets.ButtonText(btnAllowAllRect, "TSA_WD_BtnAllowAll".Translate()))
            {
                s.lockedAllegiancePairs.Clear();
                SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
            }
            TooltipHandler.TipRegion(btnAllowAllRect, "TSA_WD_BtnAllowAll_Tooltip".Translate());
            curX += btnW + btnGap;

            Rect btnResetRect = new Rect(curX, y, btnW, 30f);
            if (Widgets.ButtonText(btnResetRect, "TSA_WD_BtnReset".Translate()))
            {
                s.ResetAllegianceLocks(false);
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
            }
            TooltipHandler.TipRegion(btnResetRect, "TSA_WD_BtnReset_Tooltip".Translate());
            curX += btnW + btnGap;

            Rect btnResetGlobalRect = new Rect(curX, y, btnW, 30f);
            if (Widgets.ButtonText(btnResetGlobalRect, "TSA_WD_BtnResetGlobal".Translate()))
            {
                s.ResetAllegianceLocks(true);
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
            }
            TooltipHandler.TipRegion(btnResetGlobalRect, "TSA_WD_BtnResetGlobal_Tooltip".Translate());

            Rect freezeRect = new Rect(contentRight - freezeW, y, freezeW, 30f);
            Widgets.CheckboxLabeled(freezeRect, "TSA_WD_WorldSetup_FreezeOnApply".Translate(
                WorldActions_DiplomacyBuffsNerfs.RandomDiplomacyFreezeDays), ref freezeOnApply);
            TooltipHandler.TipRegion(freezeRect, "TSA_WD_WorldSetup_FreezeOnApplyTooltip".Translate(
                WorldActions_DiplomacyBuffsNerfs.RandomDiplomacyFreezeDays));

            y += 36f;

            // Row 2: filter (left) + reset allegiances (right-aligned to Lock column)
            string oldSearch = searchTerm;
            float resetAllegW = 210f;
            float searchMax = Mathf.Max(120f, contentRight - resetAllegW - 65f - inRect.x);
            Rect searchRect = new Rect(inRect.x, y, Mathf.Min(280f, searchMax), 28f);
            searchTerm = Widgets.TextField(searchRect, searchTerm);
            if (string.IsNullOrEmpty(searchTerm))
            {
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = new Color(1f, 1f, 1f, 0.4f);
                Widgets.Label(searchRect.ContractedBy(4f, 0f), s_filterPlaceholder);
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
            }

            Rect clearBtnRect = new Rect(searchRect.xMax + 5f, y, 60f, 28f);
            if (Widgets.ButtonText(clearBtnRect, "TSA_WD_BtnClear".Translate()))
                searchTerm = "";

            Rect resetAllegRect = new Rect(contentRight - resetAllegW, y, resetAllegW, 28f);
            if (Widgets.ButtonText(resetAllegRect, "TSA_WD_WorldSetup_ResetAllegiancesDefault".Translate()))
            {
                ResetAllRelationsToDefaults();
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
            }
            TooltipHandler.TipRegion(resetAllegRect, "TSA_WD_WorldSetup_ResetAllegiancesDefaultTooltip".Translate());

            y += 36f;

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

            int lastPlayerPairIndex = -1;
            for (int i = 0; i < filteredPairsCache.Count; i++)
            {
                if (InvolvesPlayer(filteredPairsCache[i]))
                    lastPlayerPairIndex = i;
            }

            float bottomReserve = 44f;
            Rect outRect = new Rect(inRect.x, y, inRect.width, inRect.yMax - y - bottomReserve);
            Widgets.DrawMenuSection(outRect);

            float viewWidth = RowContentWidth;
            const float rowStep = 40f;
            float separatorExtra = lastPlayerPairIndex >= 0 ? 8f : 0f;
            Rect viewRect = new Rect(0f, 0f, viewWidth, filteredPairsCache.Count * rowStep + separatorExtra);
            Widgets.BeginScrollView(outRect.ContractedBy(ScrollViewInset), ref scrollPosition, viewRect);

            float drawY = 0f;
            for (int i = 0; i < filteredPairsCache.Count; i++)
            {
                var pair = filteredPairsCache[i];
                Rect rowRect = new Rect(0f, drawY, viewWidth, 36f);
                if (i % 2 == 0) Widgets.DrawHighlight(rowRect);
                if (Mouse.IsOver(rowRect)) Widgets.DrawLightHighlight(rowRect);

                string key = s.GetFactionPairKey(pair.First, pair.Second);
                FactionRelationKind kind = GetRelationKind(pair.First, pair.Second);
                int goodwill = GetGoodwill(pair.First, pair.Second);
                bool permVsPlayer = IsPermanentHostileVsPlayer(pair);
                bool npcNpc = IsNpcNpcPair(pair);

                float x = RowPadLeft;
                DrawFactionChip(new Rect(x, rowRect.y + 4f, FactionChipW, 28f), pair.First);
                x += FactionChipW + ChipGap;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(x, rowRect.y, ArrowSlotW - 4f, 36f), "↔");
                Text.Anchor = TextAnchor.UpperLeft;
                x += ArrowSlotW;
                DrawFactionChip(new Rect(x, rowRect.y + 4f, FactionChipW, 28f), pair.Second);
                x += FactionChipW + AfterSecondChipGap;

                float relBtnH = 28f;
                float relY = rowRect.y + 4f;

                if (permVsPlayer)
                {
                    // Same horizontal span as Neutral … Hostile toggles.
                    float labelW = RelBtnW * 3f + RelBtnGap * 2f;
                    Rect permRect = new Rect(x, relY, labelW, relBtnH);
                    Text.Font = GameFont.Tiny;
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(permRect, "TSA_WD_WorldSetup_PermanentlyHostile".Translate().Colorize(ColorLibrary.RedReadable));
                    Text.Anchor = TextAnchor.UpperLeft;
                    Text.Font = GameFont.Small;
                    x += labelW + AfterRelBtnsGap;

                    Rect gwReadOnly = new Rect(x, relY, GoodwillW, relBtnH);
                    Text.Font = GameFont.Tiny;
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(gwReadOnly, FormatGoodwill(goodwill).Colorize(FactionRelationKind.Hostile.GetColor()));
                    Text.Anchor = TextAnchor.UpperLeft;
                    Text.Font = GameFont.Small;
                    TooltipHandler.TipRegion(gwReadOnly, GoodwillFieldTooltip());
                }
                else
                {
                    if (DrawRelationToggle(new Rect(x, relY, RelBtnW, relBtnH),
                        "TSA_WD_WorldSetup_RelNeutral".Translate(), FactionRelationKind.Neutral, kind))
                        ApplyKind(pair.First, pair.Second, FactionRelationKind.Neutral);
                    x += RelBtnW + RelBtnGap;
                    if (DrawRelationToggle(new Rect(x, relY, RelBtnW, relBtnH),
                        "TSA_WD_WorldSetup_RelAllied".Translate(), FactionRelationKind.Ally, kind))
                        ApplyKind(pair.First, pair.Second, FactionRelationKind.Ally);
                    x += RelBtnW + RelBtnGap;
                    if (DrawRelationToggle(new Rect(x, relY, RelBtnW, relBtnH),
                        "TSA_WD_WorldSetup_RelHostile".Translate(), FactionRelationKind.Hostile, kind))
                        ApplyKind(pair.First, pair.Second, FactionRelationKind.Hostile);
                    x += RelBtnW + AfterRelBtnsGap;

                    Rect gwRect = new Rect(x, relY, GoodwillW, relBtnH);
                    DrawGoodwillField(gwRect, key, pair.First, pair.Second, goodwill);
                    x += GoodwillW + AfterGoodwillGap;

                    if (npcNpc)
                    {
                        bool isLocked = s.lockedAllegiancePairs.Contains(key);
                        Rect lockRect = new Rect(x, relY, LockBtnW, relBtnH);
                        if (Widgets.ButtonText(lockRect,
                            isLocked
                                ? "TSA_WD_StatusLocked".Translate().Colorize(Color.red)
                                : "TSA_WD_StatusPossible".Translate()))
                        {
                            if (isLocked) s.lockedAllegiancePairs.Remove(key);
                            else s.lockedAllegiancePairs.Add(key);
                            if (isLocked) SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
                            else SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
                        }
                    }
                }

                drawY += rowStep;

                if (i == lastPlayerPairIndex)
                {
                    float sepY = drawY + 2f;
                    Widgets.DrawLineHorizontal(8f, sepY, viewWidth - 16f);
                    GUI.color = Color.white;
                    drawY += 8f;
                }
            }

            Widgets.EndScrollView();
        }

        private bool DrawRelationToggle(Rect r, string label, FactionRelationKind forKind, FactionRelationKind current)
        {
            bool selected = forKind == current;
            bool mouseOver = Mouse.IsOver(r);
            bool pressed = mouseOver && Input.GetMouseButton(0);
            Color bg = selected ? NavBtnBgSelected : pressed ? NavBtnBgPress : mouseOver ? NavBtnBgHover : NavSlateFill;
            Widgets.DrawBoxSolid(r, bg);
            GUI.color = selected ? NavBtnOutlineSelected : mouseOver ? NavBtnOutlineHover : NavBtnOutline;
            Widgets.DrawBox(r, 1);
            GUI.color = Color.white;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(r, label.Colorize(forKind.GetColor()));
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            return Widgets.ButtonInvisible(r);
        }

        private static int MaxGoodwill => WorldActions_DiplomacyBuffsNerfs.MaxGoodwillAbs;

        private static string GoodwillFieldTooltip() =>
            "TSA_WD_WorldSetup_GoodwillFieldTooltip".Translate(MaxGoodwill);

        private static int ClampGoodwill(int goodwill) =>
            Mathf.Clamp(goodwill, MinGoodwill, MaxGoodwill);

        private void DrawGoodwillField(Rect rect, string key, Faction a, Faction b, int currentGoodwill)
        {
            string controlName = "wd_gw_" + key;
            bool focused = GUI.GetNameOfFocusedControl() == controlName;
            if (!focused)
                goodwillEditBuffers[key] = FormatGoodwill(currentGoodwill);

            if (!goodwillEditBuffers.TryGetValue(key, out string buffer) || buffer == null)
                buffer = FormatGoodwill(currentGoodwill);

            GUI.SetNextControlName(controlName);
            string next = Widgets.TextField(rect, buffer);
            goodwillEditBuffers[key] = next;

            bool returnPressed = focused
                && Event.current.type == EventType.KeyDown
                && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
            bool clickOutsideCommit = focused
                && Event.current.type == EventType.MouseDown
                && !Mouse.IsOver(rect);

            if ((returnPressed || clickOutsideCommit) && TryParseGoodwill(next, out int parsed))
            {
                int clamped = ClampGoodwill(parsed);
                if (clamped != currentGoodwill)
                    ApplyGoodwill(a, b, clamped);
                else
                    goodwillEditBuffers[key] = FormatGoodwill(currentGoodwill);

                if (returnPressed)
                {
                    Event.current.Use();
                    GUI.FocusControl(null);
                }
            }

            TooltipHandler.TipRegion(rect, GoodwillFieldTooltip());
        }

        private static bool TryParseGoodwill(string text, out int value)
        {
            value = 0;
            if (string.IsNullOrEmpty(text)) return false;
            text = text.Trim();
            if (text.StartsWith("+")) text = text.Substring(1);
            return int.TryParse(text, out value);
        }

        private static string FormatGoodwill(int goodwill)
        {
            if (goodwill > 0) return "+" + goodwill;
            return goodwill.ToString();
        }

        private static FactionRelationKind GetRelationKind(Faction a, Faction b) =>
            WorldActions_Utils.SafeRelationKindWith(a, b);

        private static int GetGoodwill(Faction a, Faction b)
        {
            FactionRelation rel = a?.RelationWith(b, true);
            return rel?.baseGoodwill ?? 0;
        }

        private void ApplyKind(Faction a, Faction b, FactionRelationKind next)
        {
            int gw = WorldActions_DiplomacyBuffsNerfs.GoodwillForKind(next);
            ApplyGoodwill(a, b, gw);
        }

        private void ApplyGoodwill(Faction a, Faction b, int goodwill)
        {
            goodwill = ClampGoodwill(goodwill);
            int ticks = Find.TickManager?.TicksGame ?? 0;
            int expiry = freezeOnApply
                ? ticks + WorldActions_DiplomacyBuffsNerfs.RandomDiplomacyFreezeDurationTicks
                : ticks;
            if (!WorldActions_DiplomacyBuffsNerfs.TrySetDiplomacyGoodwill(a, b, goodwill, expiry, out _))
            {
                Messages.Message("TSA_WD_WorldSetup_RelationFailed".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            string key = WorldDominationMod.settings.GetFactionPairKey(a, b);
            goodwillEditBuffers[key] = FormatGoodwill(goodwill);
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
        }

        private void ResetAllRelationsToDefaults()
        {
            int ticks = Find.TickManager?.TicksGame ?? 0;
            int expiry = freezeOnApply
                ? ticks + WorldActions_DiplomacyBuffsNerfs.RandomDiplomacyFreezeDurationTicks
                : ticks;

            for (int i = 0; i < factionPairs.Count; i++)
            {
                Faction a = factionPairs[i].First;
                Faction b = factionPairs[i].Second;
                if (IsPermanentHostileVsPlayer(factionPairs[i])) continue;
                int goodwill = DefaultGoodwillBetween(a, b);
                WorldActions_DiplomacyBuffsNerfs.TrySetDiplomacyGoodwill(a, b, goodwill, expiry, out _);
            }

            goodwillEditBuffers.Clear();
            Messages.Message("TSA_WD_WorldSetup_ResetAllegiancesDone".Translate(), MessageTypeDefOf.PositiveEvent, false);
        }

        private static int DefaultGoodwillBetween(Faction a, Faction b)
        {
            int goodwillA = GetNaturalGoodwill(a, b);
            int goodwillB = GetNaturalGoodwill(b, a);
            return Mathf.Min(goodwillA, goodwillB);
        }

        private static int GetNaturalGoodwill(Faction a, Faction b)
        {
            if (a?.def == null || b?.def == null) return 0;
            if (a.def.permanentEnemy) return -100;
            if (a.def.permanentEnemyToEveryoneExceptPlayer && !b.IsPlayer) return -100;
            if (a.def.permanentEnemyToEveryoneExcept != null && !a.def.permanentEnemyToEveryoneExcept.Contains(b.def))
                return -100;
            if (WorldActions_Utils.IsPermanentEnemyOfPlayer(a) && b.IsPlayer) return -100;
            if (a.def.naturalEnemy) return -80;
            return 0;
        }

        private static void DrawFactionChip(Rect rect, Faction faction)
        {
            float iconSize = 22f;
            Rect iconRect = new Rect(rect.x, rect.y + (rect.height - iconSize) / 2f, iconSize, iconSize);
            Rect textRect = new Rect(iconRect.xMax + 6f, rect.y, rect.width - (iconSize + 6f), rect.height);

            GUI.color = faction.Color;
            Widgets.DrawTextureFitted(iconRect, faction.def.FactionIcon, 1f);
            GUI.color = Color.white;

            Text.Anchor = TextAnchor.MiddleLeft;
            Text.Font = GameFont.Tiny;
            string name = faction.IsPlayer
                ? (faction.Name + " (" + "TSA_WD_Faction_Player".Translate() + ")")
                : faction.Name;
            Widgets.Label(textRect, name.Truncate(textRect.width));
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }
    }
}
