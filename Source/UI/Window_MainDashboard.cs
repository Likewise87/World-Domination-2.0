using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;
using RimWorld.Planet;
using Verse.Sound;

namespace TSA_WorldDomination
{
    /// <summary>Main tab "WD": navigation at top; three-column dashboard (threats, outposts, travelers) with per-section scroll.</summary>
    [StaticConstructorOnStartup]
    public class MainTabWindow_WorldDomination : MainTabWindow
    {
        private static readonly Texture2D ConfigIcon =
            ContentFinder<Texture2D>.Get("UI/Commands/Config", false)
            ?? TexButton.OpenInspectSettings
            ?? TexButton.Info;
        private static readonly Texture2D LeaderboardIcon =
            ContentFinder<Texture2D>.Get("UI/Commands/WD_Leaderboard", false);
        private static readonly Texture2D IconDiplomacy =
            ContentFinder<Texture2D>.Get("UI/Commands/Icon_Diplomacy", false);
        private static readonly Texture2D IconWorldStats =
            ContentFinder<Texture2D>.Get("UI/Commands/Icon_WorldStats", false);
        private static readonly Texture2D IconActionLog =
            ContentFinder<Texture2D>.Get("UI/Commands/Icon_ActionLog", false);
        private static readonly Texture2D IconActiveTravelers =
            ContentFinder<Texture2D>.Get("UI/Commands/Icon_ActiveTravelers", false);
        private static readonly Texture2D IconAllPlayerPawns =
            ContentFinder<Texture2D>.Get("UI/Commands/Icon_AllPlayerPawns", false);
        private static readonly Texture2D IconPrisoners =
            ContentFinder<Texture2D>.Get("UI/Commands/Icon_Prisoners", false);
        private static readonly Texture2D IconPlayerOutposts =
            ContentFinder<Texture2D>.Get("UI/Commands/Icon_PlayerOutposts", false)
            ?? ContentFinder<Texture2D>.Get("WorldObjects/Icon_PlayerOutposts", false);
        private static readonly Texture2D RaidersIcon =
            ContentFinder<Texture2D>.Get("WorldObjects/Caravan_Raiders", false);
        private const float FactionRankIconSize = 44f;
        private const float SectionPadding = 10f;
        private const float ColumnGap = 8f;
        private const float LineHeight = 26f;
        private const float IconSize = 28f;
        private const float ButtonHeight = 40f;
        private const float ButtonSpacing = 12f;
        /// <summary>Same subtle grey as the top nav bar / column chrome (not the navy slate used on buttons).</summary>
        private static readonly Color StatusBoxFill = new Color(1f, 1f, 1f, 0.04f);
        /// <summary>Darker panel fill for Nearby / Far threat halves.</summary>
        private static readonly Color ThreatHalfFill = new Color(0.10f, 0.11f, 0.14f, 0.72f);
        private const float ThreatHalfInnerPad = 4f;
        private static readonly Color RaidHeaderIconRed = new Color(1f, 0.35f, 0.35f);
        private const float StatusBoxPad = 8f;
        private const float GoodwillValueRightPad = 10f;
        /// <summary>UI_WINDOWS: Medium labels need ≥30px (was 28 and cropped descenders).</summary>
        private const float SectionHeaderHeight = 30f;
        /// <summary>UI_WINDOWS minima for single-line Labels.</summary>
        private const float TinyLabelHeight = 15f;
        private const float SmallLabelHeight = 24f;
        private const float ColumnPaddingLeft = 5f;
        /// <summary>Horizontal inset inside the top nav grey bar; also aligns column block with nav buttons.</summary>
        private const float NavBarPadX = 5f;
        /// <summary>Taken evenly from threats and outposts (10px each) and given to travelers.</summary>
        private const float TravelersExtraWidth = 20f;
        /// <summary>Threats and outposts columns use 85% of an equal third; remainder goes to travelers.</summary>
        private const float NarrowColumnWidthFactor = 0.85f;
        /// <summary>Current/start strength: fits 5-digit pairs like "12345 / 12345" at Small.</summary>
        private const float TravelerStrengthColWidth = 120f;
        private const float TravelerTypeColWidth = 32f;
        private const float TravelerTimeColWidth = 72f;
        /// <summary>Origin/destination name max length before ellipsis (full name on hover).</summary>
        private const int TravelerEndpointLabelMaxChars = 13;
        /// <summary>~10s at 60 game ticks/s—rebuild traveler row snapshots.</summary>
        private const int TravelerUiRefreshIntervalTicks = 600;
        /// <summary>Same interval as travelers; per-outpost cached lookup, no full-world scan.</summary>
        private const int OutpostDashRefreshIntervalTicks = 600;
        private const int MaxDashRows = 15;

        private struct TravelerDashRowData
        {
            public WorldObject_Traveler T;
            public Texture2D MissionIcon;
            public float ArrivalStrength;
            public int ExpansionDestTileId;
            public string ExpansionDestLabel;
            public string MissionTip;
            public string OriginTip;
            public string TargetTip;
            public string TimeLabel;
            public string TimeTip;
        }

        private List<TravelerDashRowData> travelerDashRows = new List<TravelerDashRowData>();
        private int travelerDashCacheTick = -999999;
        private int travelerDashTotalCount;
        private int cachedTravelerTargetingYouCount;
        private List<(WorldObject_WD_Outpost Outpost, string Label, float FoodCurrent, float FoodMax, float FoodNet, string DisplayLabel, Color DisplayColor, string Tooltip)> outpostDashCache;
        private int outpostDashCacheTick = -999999;
        private Vector2 outpostsScrollPos;
        private Vector2 travelersScrollPos;
        private Vector2 nearbyThreatsScrollPos;
        private Vector2 farThreatsScrollPos;
        private readonly List<ThreatSettlementEntry> nearbyThreatScratch = new List<ThreatSettlementEntry>();
        private readonly List<ThreatSettlementEntry> farThreatScratch = new List<ThreatSettlementEntry>();

        /// <summary>Session-only: false = storyteller %, true = absolute raid points.</summary>
        private static bool threatsShowAbsolutePoints;
        /// <summary>Session-only: false = raw strength, true = storyteller-band clamped points.</summary>
        private static bool threatsShowClamped;
        private enum OutpostDashSortMode { Food, Strength, PawnCount }
        /// <summary>Session-only outpost column sort: F food, S strength, P pawn count.</summary>
        private static OutpostDashSortMode outpostsSortMode = OutpostDashSortMode.Food;

        /// <summary>Dashboard Threats header %/# toggle. Shared with the right-side world threat alert.</summary>
        public static bool ThreatsShowAbsolutePoints => threatsShowAbsolutePoints;
        /// <summary>Dashboard Threats header R/C toggle. Shared with the right-side world threat alert.</summary>
        public static bool ThreatsShowClamped => threatsShowClamped;

        private const float ThreatsHeaderToggleSize = 24f;
        private const float ThreatsHeaderToggleGap = 3f;

        private static string dashDaysLabel;
        private static string dashUnknownLabel;
        private static string dashOpenLabel;
        private static string dashNoneLabel;
        private static string navDiplomacy, navOutpost, navWorldStats, navActionLog, navTravelers, navAllPlayerPawns, navPrisoners;
        private static string hdrThreats, hdrNearbyThreats, hdrFarThreats, hdrOutposts, hdrTravelers;
        private static string dashNoOutposts, dashNoTravelers, dashNoThreatSettlements, dashNoNearbyThreats, dashNoFarThreats, dashMoreEntries;
        private static string dashTravelerStartTip, dashTravelerDestTip;
        private static int dashTranslateFrame = -1;
        private static void EnsureDashTranslations()
        {
            int f = Time.frameCount;
            if (f == dashTranslateFrame) return;
            dashTranslateFrame = f;
            dashDaysLabel = "TSA_WD_Days".Translate();
            dashUnknownLabel = "TSA_WD_Traveller_Unknown".Translate();
            dashOpenLabel = "TSA_WD_ActiveTravelers_Open".Translate();
            dashNoneLabel = "TSA_WD_None".Translate();
            navDiplomacy = "TSA_WD_DiplomacyMatrix".Translate();
            navOutpost = "TSA_WD_OutpostManager".Translate();
            navWorldStats = "TSA_WD_WorldStats".Translate();
            navActionLog = "TSA_WD_ActionLog".Translate();
            navTravelers = dashOpenLabel;
            navAllPlayerPawns = "TSA_WD_AllPlayerPawns".Translate();
            navPrisoners = "TSA_WD_Prisoners".Translate();
            hdrThreats = "TSA_WD_Dash_Threats".Translate();
            hdrNearbyThreats = "TSA_WD_Dash_NearbyThreats".Translate();
            hdrFarThreats = "TSA_WD_Dash_FarThreats".Translate();
            hdrOutposts = "TSA_WD_Dash_YourOutposts".Translate();
            hdrTravelers = "TSA_WD_Dash_Travelers".Translate();
            dashNoThreatSettlements = "TSA_WD_Dash_NoThreatSettlements".Translate().Colorize(Color.gray);
            dashNoNearbyThreats = "TSA_WD_Dash_NoNearbyThreats".Translate().Colorize(Color.gray);
            dashNoFarThreats = "TSA_WD_Dash_NoFarThreats".Translate().Colorize(Color.gray);
            dashMoreEntries = "TSA_WD_Dash_MoreEntries".Translate().Colorize(Color.gray);
            dashNoOutposts = "TSA_WD_Dash_NoOutposts".Translate().Colorize(Color.gray);
            dashNoTravelers = "TSA_WD_Dash_NoTravelers".Translate().Colorize(Color.gray);
            dashTravelerStartTip = "TSA_WD_Dash_Traveler_StartTip".Translate();
            dashTravelerDestTip = "TSA_WD_Dash_Traveler_DestTip".Translate();
        }

        private WorldComponent_SpreadManager cachedSpreadManager;
        private WorldComponent_LogisticsManager cachedLogiManager;
        private string cachedTargetingYouLabel;
        private int cachedTargetingYouCount = -1;
        private int factionRankCacheTick = -999999;
        private int cachedPlayerFactionRank;
        private int cachedFactionRankTotal;
        private string cachedFactionRankLabel;
        private Color cachedFactionRankColor = Color.white;
        private int outpostPawnSummaryCacheTick = -999999;
        private string cachedOutpostPawnSummaryLabel;
        private string cachedOutpostPawnSummaryTip;

        public override Vector2 RequestedTabSize => new Vector2(1280f, 608f);

        public override void PreOpen()
        {
            base.PreOpen();
        }

        private static void NavOpenDiplomacy() => WdNavWindows.OpenExclusive(() => new Window_DiplomacyMatrix());
        private static void NavOpenOutpost() => WdNavWindows.OpenExclusive(() => new Window_OutpostOverview());
        private static void NavOpenWorldStats() => WdNavWindows.OpenExclusive(() => new Window_WorldStats());
        private static void NavOpenActionLog() => WdNavWindows.OpenExclusive(() => new Window_ActionLog());
        private static void NavOpenTravelers() => WdNavWindows.OpenExclusive(() => new Window_ActiveTravelers());
        private static void NavOpenAllPlayerPawns() => WdNavWindows.OpenExclusive(() => new Window_AllPlayerPawns());
        private static void NavOpenPrisoners() => WdNavWindows.OpenExclusive(() => new Window_Prisoners());

        private static void ClickOpenDiplomacy() { NavOpenDiplomacy(); SoundDefOf.Click.PlayOneShotOnCamera(); }
        private static void ClickOpenOutpost() { NavOpenOutpost(); SoundDefOf.Click.PlayOneShotOnCamera(); }
        private static void ClickOpenTravelers() { NavOpenTravelers(); SoundDefOf.Click.PlayOneShotOnCamera(); }
        private static void ClickOpenAllPlayerPawns() { NavOpenAllPlayerPawns(); SoundDefOf.Click.PlayOneShotOnCamera(); }
        private static void ClickOpenPrisoners() { NavOpenPrisoners(); SoundDefOf.Click.PlayOneShotOnCamera(); }

        private static Rect InsetIconDrawRect(Rect outer, float pad = 2f)
        {
            Rect r = outer.ContractedBy(pad);
            return r.width >= 1f && r.height >= 1f ? r : outer;
        }

        public override void DoWindowContents(Rect fillRect)
        {
            WdNavWindows.ProcessHotkeys();
            if (!IsOpen) return;

            EnsureDashTranslations();
            float y = 0f;
            Text.Font = GameFont.Small;

            // ---------- TOP: Navigation bar ----------
            const float statusRowPad = StatusBoxPad;
            const float storytellerHintGap = 10f;
            // Status boxes: measured Small/Tiny line heights (UI_WINDOWS floors) + pad + slack.
            float smallH = Mathf.Max(SmallLabelHeight, Text.LineHeightOf(GameFont.Small));
            float tinyH = Mathf.Max(TinyLabelHeight, Text.LineHeightOf(GameFont.Tiny));
            float raidsBoxH = smallH + tinyH * 3f + StatusBoxPad * 2f + 4f;
            float hintRowH = raidsBoxH;
            float navStackH = hintRowH + storytellerHintGap + ButtonHeight;
            Rect navBar = new Rect(0, y, fillRect.width, navStackH + 12f);
            Widgets.DrawBoxSolid(navBar, StatusBoxFill);
            y += 4f;

            float pawnsBtnY = y + hintRowH + storytellerHintGap;
            const float configBtnSize = 30f;
            float rightEdge = fillRect.width - NavBarPadX;

            // Label-sized right cluster: Prisoners | Your Pawns | Active Travelers | Action Log | Config.
            // Config sits flush to the right inset; leftover former centering pad widens Your Pawns + Action Log.
            // Smaller gap left of the gear goes to Active Travelers.
            Text.Font = GameFont.Small;
            float actionLogBtnW = MeasureNavButtonWidth(navActionLog);
            float travelersBtnW = MeasureNavButtonWidth(navTravelers);
            float allPawnsBtnW = MeasureNavButtonWidth(navAllPlayerPawns);
            float prisonersBtnW = MeasureNavButtonWidth(navPrisoners);
            const float oldConfigSlotW = ButtonSpacing + configBtnSize + ButtonSpacing;
            const float configGap = 4f;
            float freedFromConfig = oldConfigSlotW - (configGap + configBtnSize);
            // Half of the old centering pad → Your Pawns / Action Log; the rest of the reduced left gap → Travelers.
            float pawnsActionShare = ButtonSpacing * 0.5f;
            actionLogBtnW += pawnsActionShare;
            allPawnsBtnW += pawnsActionShare;
            travelersBtnW += freedFromConfig - pawnsActionShare * 2f;

            Rect configRect = new Rect(
                rightEdge - configBtnSize,
                pawnsBtnY + (ButtonHeight - configBtnSize) * 0.5f,
                configBtnSize,
                configBtnSize);
            Rect actionLogRect = new Rect(configRect.x - configGap - actionLogBtnW, pawnsBtnY, actionLogBtnW, ButtonHeight);
            Rect travelersRect = new Rect(actionLogRect.x - ButtonSpacing - travelersBtnW, pawnsBtnY, travelersBtnW, ButtonHeight);
            Rect allPawnsRect = new Rect(travelersRect.x - ButtonSpacing - allPawnsBtnW, pawnsBtnY, allPawnsBtnW, ButtonHeight);

            float navAreaW = allPawnsRect.x - ButtonSpacing - prisonersBtnW - NavBarPadX - ButtonSpacing;
            // Left group: Diplomacy | Outpost | World Stats. World Stats is narrower; freed width goes to Prisoners.
            const float outpostWeight = 0.92f;
            const float worldStatsWeight = 0.58f;
            float unitW = (navAreaW - ButtonSpacing * 2f) / (1f + outpostWeight + worldStatsWeight);
            float diplomacyW = unitW;
            float outpostW = unitW * outpostWeight;
            float worldStatsW = unitW * worldStatsWeight;
            // Transfer a slice of World Stats into Prisoners so Prisoners right edge stays flush with goodwill.
            const float prisonersFromWorldStats = 22f;
            float worldStatsSteal = Mathf.Min(prisonersFromWorldStats, Mathf.Max(0f, worldStatsW - 90f));
            worldStatsW -= worldStatsSteal;
            prisonersBtnW += worldStatsSteal;
            // Then move 20px from Outpost Overview back to World Stats.
            const float outpostToWorldStats = 20f;
            float outpostGive = Mathf.Min(outpostToWorldStats, Mathf.Max(0f, outpostW - 100f));
            outpostW -= outpostGive;
            worldStatsW += outpostGive;

            Rect prisonersRect = new Rect(allPawnsRect.x - ButtonSpacing - prisonersBtnW, pawnsBtnY, prisonersBtnW, ButtonHeight);
            float diplomacyX = NavBarPadX;
            float outpostX = diplomacyX + diplomacyW + ButtonSpacing;

            // Status boxes share left/right edges with the nav buttons under them.
            // Leaderboard left = Your Pawns left; goodwill right = Prisoners right.
            Rect raidsRect = new Rect(diplomacyX, y, diplomacyW, hintRowH);
            Rect goodwillRect = new Rect(outpostX, y, prisonersRect.xMax - outpostX, hintRowH);
            Rect rankRect = new Rect(allPawnsRect.x, y, rightEdge - allPawnsRect.x, hintRowH);

            DrawPlayerWdRaidLaunchCapsBox(raidsRect);
            DrawGoodwillHighlightsBox(goodwillRect, statusRowPad);
            DrawFactionRankStatusHint(rankRect, statusRowPad);

            float x = NavBarPadX;
            float navY = pawnsBtnY;
            string hotkeyDiplomacy = FormatWdWindowHotkey("D");
            string hotkeyWorldStats = FormatWdWindowHotkey("S");
            string hotkeyOutposts = FormatWdWindowHotkey("F");
            string hotkeyTravelers = FormatWdWindowHotkey("G");
            string hotkeyPawns = FormatWdWindowHotkey("A");
            string hotkeyPrisoners = FormatWdWindowHotkey("Y");
            DrawNavButton(ref x, navY, diplomacyW, navDiplomacy, NavOpenDiplomacy, IconDiplomacy,
                "TSA_WD_Dash_NavTip_Diplomacy".Translate(hotkeyDiplomacy));
            x += ButtonSpacing;
            DrawNavButton(ref x, navY, outpostW, navOutpost, NavOpenOutpost, IconPlayerOutposts,
                "TSA_WD_Dash_NavTip_Outposts".Translate(hotkeyOutposts));
            x += ButtonSpacing;
            DrawNavButton(ref x, navY, worldStatsW, navWorldStats, NavOpenWorldStats, IconWorldStats,
                "TSA_WD_Dash_NavTip_WorldStats".Translate(hotkeyWorldStats));
            DrawNavButtonAt(prisonersRect, navPrisoners, ClickOpenPrisoners, IconPrisoners,
                "TSA_WD_Dash_NavTip_Prisoners".Translate(hotkeyPrisoners));
            DrawNavButtonAt(allPawnsRect, navAllPlayerPawns, ClickOpenAllPlayerPawns, IconAllPlayerPawns,
                "TSA_WD_Dash_NavTip_YourPawns".Translate(hotkeyPawns));
            DrawNavButtonAt(travelersRect, navTravelers, ClickOpenTravelers, IconActiveTravelers,
                "TSA_WD_Dash_NavTip_Travelers".Translate(hotkeyTravelers));
            DrawNavButtonAt(actionLogRect, navActionLog, () =>
            {
                NavOpenActionLog();
                SoundDefOf.Click.PlayOneShotOnCamera();
            }, IconActionLog, "TSA_WD_Dash_NavTip_ActionLog".Translate());
            TooltipHandler.TipRegion(configRect, "TSA_WD_Dash_OpenModSettingsTip".Translate());
            if (Widgets.ButtonImage(configRect, ConfigIcon))
                WorldDominationMod.OpenModSettingsWindow();

            y += navStackH + SectionPadding * 2f;

            if (cachedSpreadManager == null)
                cachedSpreadManager = Find.World.GetComponent<WorldComponent_SpreadManager>();
            if (cachedLogiManager == null)
                cachedLogiManager = Find.World.GetComponent<WorldComponent_LogisticsManager>();
            var manager = cachedSpreadManager;
            var logi = cachedLogiManager;

            float contentY = y;
            float contentH = fillRect.height - contentY;
            // Match nav button insets so travelers column ends flush with the grey bar's right inset.
            float contentLeft = NavBarPadX;
            float contentRight = rightEdge;
            float totalColW = contentRight - contentLeft - ColumnGap * 2f;
            float baseThird = totalColW / 3f;
            float halfTravelersExtra = TravelersExtraWidth * 0.5f;
            float threatsW = baseThird * NarrowColumnWidthFactor - halfTravelersExtra;
            float outpostsW = baseThird * NarrowColumnWidthFactor - halfTravelersExtra;
            float travelersW = totalColW - threatsW - outpostsW;
            Rect threatsCol = new Rect(contentLeft, contentY, threatsW, contentH);
            Rect outpostsCol = new Rect(contentLeft + threatsW + ColumnGap, contentY, outpostsW, contentH);
            Rect travelersCol = new Rect(contentLeft + threatsW + ColumnGap + outpostsW + ColumnGap, contentY, travelersW, contentH);
            DrawColumnSeparator(outpostsCol.x - ColumnGap * 0.5f, contentY, contentH);
            DrawColumnSeparator(travelersCol.x - ColumnGap * 0.5f, contentY, contentH);
            DrawThreatsColumn(threatsCol, manager);
            DrawOutpostsColumn(outpostsCol, logi);
            DrawTravelersColumn(travelersCol);
        }

        private static Rect InsetColumnContent(Rect colRect) =>
            new Rect(colRect.x + ColumnPaddingLeft, colRect.y, colRect.width - ColumnPaddingLeft, colRect.height);

        private void DrawThreatsColumn(Rect colRect, WorldComponent_SpreadManager manager)
        {
            colRect = InsetColumnContent(colRect);
            float y = colRect.y;
            DrawThreatsColumnHeader(colRect, manager, ref y);

            WorldThreatDisplay.Partition(manager?.ThreatSettlements, manager, nearbyThreatScratch, farThreatScratch);

            float remainingH = colRect.yMax - y;
            float gap = 4f;
            float halfH = (remainingH - gap) * 0.5f;
            Rect nearbyRect = new Rect(colRect.x, y, colRect.width, halfH);
            Rect farRect = new Rect(colRect.x, y + halfH + gap, colRect.width, halfH);

            DrawThreatHalf(
                nearbyRect,
                hdrNearbyThreats,
                WorldThreatDisplay.BuildNearbySubheaderTip(),
                nearbyThreatScratch,
                manager,
                showTilesInLabel: true,
                emptyLabel: dashNoNearbyThreats,
                ref nearbyThreatsScrollPos);

            DrawThreatHalf(
                farRect,
                hdrFarThreats,
                WorldThreatDisplay.BuildFarSubheaderTip(),
                farThreatScratch,
                manager,
                showTilesInLabel: false,
                emptyLabel: dashNoFarThreats,
                ref farThreatsScrollPos);
        }

        private void DrawThreatHalf(
            Rect halfRect,
            string subheader,
            string subheaderTip,
            List<ThreatSettlementEntry> entries,
            WorldComponent_SpreadManager manager,
            bool showTilesInLabel,
            string emptyLabel,
            ref Vector2 scrollPos)
        {
            Widgets.DrawBoxSolid(halfRect, ThreatHalfFill);
            Rect inner = halfRect.ContractedBy(ThreatHalfInnerPad);
            float y = inner.y;
            float subHeaderH = Mathf.Max(SmallLabelHeight, Text.LineHeightOf(GameFont.Small));
            Rect titleRect = new Rect(inner.x, y, inner.width, subHeaderH);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(titleRect, subheader);
            Text.Anchor = TextAnchor.UpperLeft;
            TooltipHandler.TipRegion(titleRect, subheaderTip);
            y += subHeaderH;

            int count = entries?.Count ?? 0;
            Rect listRect = new Rect(inner.x, y, inner.width, Mathf.Max(0f, inner.yMax - y));

            if (count == 0)
            {
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.Font = GameFont.Small;
                Widgets.Label(new Rect(listRect.x, listRect.y, listRect.width, LineHeight), emptyLabel);
                Text.Anchor = TextAnchor.UpperLeft;
                return;
            }

            float contentH = count * LineHeight;
            float gutter = contentH > listRect.height ? 16f : 0f;
            Rect scrollRect = new Rect(0f, 0f, listRect.width - gutter, contentH);
            Widgets.BeginScrollView(listRect, ref scrollPos, scrollRect);

            float baseline = ResolveThreatDisplayBaseline(manager);
            float innerY = 0f;
            for (int i = 0; i < count; i++)
            {
                var entry = entries[i];
                if (entry.settlement == null) continue;
                Rect cellRect = new Rect(0f, innerY, scrollRect.width, LineHeight);
                if (Mouse.IsOver(cellRect)) Widgets.DrawLightHighlight(cellRect);
                Rect iconRect = new Rect(cellRect.x, innerY + (LineHeight - IconSize) / 2f, IconSize, IconSize);
                WorldDomination_UIUtils.DrawFactionIconWithColor(InsetIconDrawRect(iconRect), entry.faction);
                ResolveThreatDisplayValues(entry, baseline, out float points, out float pct);
                Color textColor = StorytellerPctColor(pct);
                string label = FormatThreatSettlementLine(entry.settlement.LabelCap, points, pct);
                if (showTilesInLabel)
                    label += " (" + "TSA_WD_Dash_ThreatTiles".Translate(Mathf.RoundToInt(entry.tilesToColony)).ToString() + ")";
                label = label.Colorize(textColor);
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(cellRect.x + IconSize + 6f, innerY, cellRect.width - IconSize - 10f, LineHeight), label.Truncate(cellRect.width - IconSize - 12f));
                Text.Anchor = TextAnchor.UpperLeft;
                TooltipHandler.TipRegion(cellRect, BuildThreatSettlementTooltip(entry, points, pct));
                if (Widgets.ButtonInvisible(cellRect))
                    JumpToWorldObject(entry.settlement);
                innerY += LineHeight;
            }

            Widgets.EndScrollView();
        }

        private static void DrawThreatsColumnHeader(Rect colRect, WorldComponent_SpreadManager manager, ref float y)
        {
            float headerBottom = y + SectionHeaderHeight;
            float togglesW = ThreatsHeaderToggleSize * 2f + ThreatsHeaderToggleGap;
            Rect modeBtn = new Rect(colRect.xMax - togglesW, y + (SectionHeaderHeight - ThreatsHeaderToggleSize) * 0.5f, ThreatsHeaderToggleSize, ThreatsHeaderToggleSize);
            Rect clampBtn = new Rect(modeBtn.xMax + ThreatsHeaderToggleGap, modeBtn.y, ThreatsHeaderToggleSize, ThreatsHeaderToggleSize);

            Text.Font = GameFont.Medium;
            float titleW = Mathf.Min(Text.CalcSize(hdrThreats).x + 8f, Mathf.Max(40f, colRect.width * 0.32f));
            float mediumH = Mathf.Max(SectionHeaderHeight * 0.85f, Text.LineHeightOf(GameFont.Medium));
            Rect titleRect = new Rect(colRect.x, headerBottom - mediumH, titleW, mediumH);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            Widgets.Label(titleRect, hdrThreats);
            // Full-height hit target for the row strip.
            Rect titleHit = new Rect(colRect.x, y, titleW, SectionHeaderHeight);
            TooltipHandler.TipRegion(titleHit, BuildThreatsHeaderTooltip(manager));
            if (Widgets.ButtonInvisible(titleHit))
                ClickOpenDiplomacy();

            Settlement playerColony = FindPrimaryPlayerColony();
            float cdLeft = titleRect.xMax + 6f;
            float cdRight = modeBtn.x - 6f;
            if (playerColony != null && cdRight > cdLeft + 20f)
            {
                var comp = playerColony.GetComponent<CompViralSpread>();
                string raidLine = FormatColonyRaidCdHeaderLine(comp);
                Text.Font = GameFont.Tiny;
                float tinyH = Mathf.Max(TinyLabelHeight, Text.LineHeightOf(GameFont.Tiny));
                // Shared bottom with Medium "Threats", then 4px up so Tiny does not sit below the headline.
                const float tinyBaselineNudge = -1f;
                Rect cdRect = new Rect(cdLeft, headerBottom - tinyH + tinyBaselineNudge, cdRight - cdLeft, tinyH);
                Text.Anchor = TextAnchor.UpperLeft;
                Widgets.Label(cdRect, raidLine.Truncate(cdRect.width));
                Text.Font = GameFont.Small;
                Rect cdHit = new Rect(cdLeft, y, cdRight - cdLeft, SectionHeaderHeight);
                string tip = (comp != null && comp.IsDefenseOnCooldown)
                    ? "TSA_WD_Dash_ColonyRaidProtectionTip_OnCooldown".Translate(playerColony.LabelCap).ToString()
                    : "TSA_WD_Dash_ColonyRaidProtectionTip_Vulnerable".Translate().ToString();
                TooltipHandler.TipRegion(cdHit, tip);
                if (Widgets.ButtonInvisible(cdHit))
                    JumpToWorldObject(playerColony);
            }

            string modeLabel = threatsShowAbsolutePoints ? "#" : "%";
            string modeTip = "TSA_WD_Dash_ThreatToggle_PercentAbsoluteTip".Translate().ToString();
            if (Widgets.ButtonText(modeBtn, modeLabel, true, true, true))
            {
                threatsShowAbsolutePoints = !threatsShowAbsolutePoints;
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
            TooltipHandler.TipRegion(modeBtn, modeTip);

            string clampLabel = threatsShowClamped ? "C" : "R";
            string clampTip = "TSA_WD_Dash_ThreatToggle_RawClampedTip".Translate().ToString();
            if (Widgets.ButtonText(clampBtn, clampLabel, true, true, true))
            {
                threatsShowClamped = !threatsShowClamped;
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
            TooltipHandler.TipRegion(clampBtn, clampTip);

            y += SectionHeaderHeight + SectionPadding;
        }

        private static string FormatColonyRaidCdHeaderLine(CompViralSpread comp)
        {
            if (comp != null && comp.IsDefenseOnCooldown)
            {
                float daysLeft = Mathf.Max(0f, (comp.defenseCooldownTick - Find.TickManager.TicksGame) / 60000f);
                return "TSA_WD_Dash_ColonyRaidCD".Translate(daysLeft.ToString("F1")).ToString().Colorize(Color.green);
            }
            return CompViralSpread.GetColonyRaidVulnerableLabel();
        }

        private static float ResolveThreatDisplayBaseline(WorldComponent_SpreadManager manager)
        {
            if (manager != null && manager.WorldThreatBaseline > 0f)
                return manager.WorldThreatBaseline;
            return SamplePrimaryColonyStorytellerBaseline();
        }

        private static void ResolveThreatDisplayValues(ThreatSettlementEntry entry, float baseline, out float points, out float pct)
        {
            points = threatsShowClamped ? entry.clampedPoints : entry.rawStrength;
            if (threatsShowClamped)
                pct = baseline > 0f ? points / baseline * 100f : 0f;
            else
                pct = entry.storytellerPct;
        }

        /// <summary>
        /// Same points/% numbers the Threats column would show for the current world-threat scariest settlement,
        /// using the session %/# and R/C toggles.
        /// </summary>
        public static bool TryResolveWorldThreatAlertDisplay(
            WorldComponent_SpreadManager manager,
            out float displayPoints,
            out int displayPct,
            out float rawPoints,
            out float clampedPoints,
            out float baseline,
            out string sourceLabel)
        {
            displayPoints = 0f;
            displayPct = 0;
            rawPoints = 0f;
            clampedPoints = 0f;
            baseline = 0f;
            sourceLabel = null;
            if (manager == null || manager.CurrentWorldThreatTier == WorldThreatTier.None)
                return false;

            baseline = ResolveThreatDisplayBaseline(manager);
            rawPoints = manager.WorldThreatMaxRaid;
            Map clampMap = manager.WorldThreatColony?.Map ?? FindPrimaryPlayerColony()?.Map;
            clampedPoints = RaidPointsHelper.ClampRaidPointsToStorytellerBand(rawPoints, clampMap);

            ThreatSettlementEntry? match = null;
            GlobalTargetInfo scariest = manager.WorldThreatScariest;
            var list = manager.ThreatSettlements;
            if (list != null && scariest.IsValid)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    ThreatSettlementEntry e = list[i];
                    if (e.settlement != null && scariest.WorldObject == e.settlement)
                    {
                        match = e;
                        break;
                    }
                }
            }

            if (match.HasValue)
            {
                ThreatSettlementEntry e = match.Value;
                rawPoints = e.rawStrength;
                // Live clamp against the same baseline colony the tip shows (cached entry.clampedPoints can lag hourly baseline drift).
                clampedPoints = RaidPointsHelper.ClampRaidPointsToStorytellerBand(rawPoints, clampMap);
                sourceLabel = e.settlement?.LabelCap ?? manager.WorldThreatScariestName;
                if (threatsShowClamped)
                {
                    displayPoints = clampedPoints;
                    displayPct = baseline > 0f ? Mathf.RoundToInt(displayPoints / baseline * 100f) : 0;
                }
                else
                {
                    displayPoints = rawPoints;
                    displayPct = baseline > 0f ? Mathf.RoundToInt(rawPoints / baseline * 100f) : 0;
                }
                return true;
            }

            sourceLabel = manager.WorldThreatScariestName;
            if (threatsShowClamped)
            {
                displayPoints = clampedPoints;
                displayPct = baseline > 0f ? Mathf.RoundToInt(displayPoints / baseline * 100f) : 0;
            }
            else
            {
                displayPoints = rawPoints;
                displayPct = manager.WorldThreatPercent;
            }
            return displayPoints > 0f || displayPct > 0;
        }

        private static string FormatThreatSettlementLine(string settlementLabel, float points, float pct)
        {
            if (threatsShowAbsolutePoints)
                return "TSA_WD_Dash_ThreatSettlementLineAbsolute".Translate(settlementLabel, points.ToString("F0")).ToString();
            return "TSA_WD_Dash_ThreatSettlementLinePercent".Translate(
                settlementLabel, Mathf.RoundToInt(pct)).ToString();
        }

        private void DrawOutpostsColumn(Rect colRect, WorldComponent_LogisticsManager logi)
        {
            colRect = InsetColumnContent(colRect);
            float y = colRect.y;
            DrawOutpostsColumnHeader(colRect, ref y);
            int nowTickDash = Find.TickManager.TicksGame;
            if (nowTickDash - outpostDashCacheTick >= OutpostDashRefreshIntervalTicks || outpostDashCache == null)
            {
                outpostDashCache = BuildPlayerOutpostsSorted(logi, outpostsSortMode);
                outpostDashCacheTick = nowTickDash;
            }
            var outpostList = outpostDashCache;
            int outpostCount = outpostList.Count;
            int outpostDisplay = outpostCount > 0 ? Mathf.Min(outpostCount, MaxDashRows) : 0;
            bool outpostsHasMore = outpostCount > MaxDashRows;
            Rect viewRect = new Rect(colRect.x, y, colRect.width, colRect.yMax - y);
            float contentH = outpostCount > 0 ? (outpostDisplay + (outpostsHasMore ? 1 : 0)) * LineHeight : LineHeight;
            Rect scrollRect = new Rect(0f, 0f, colRect.width - 16f, contentH);
            Widgets.BeginScrollView(viewRect, ref outpostsScrollPos, scrollRect);
            float innerY = 0f;
            if (outpostCount == 0)
            {
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(0f, innerY, scrollRect.width, LineHeight), dashNoOutposts);
                Text.Anchor = TextAnchor.UpperLeft;
            }
            else
            {
                for (int i = 0; i < outpostDisplay; i++)
                {
                    var entry = outpostList[i];
                    Rect cellRect = new Rect(0f, innerY, scrollRect.width, LineHeight);
                    if (Mouse.IsOver(cellRect)) Widgets.DrawLightHighlight(cellRect);
                    Rect iconRect = new Rect(cellRect.x, innerY + (LineHeight - IconSize) / 2f, IconSize, IconSize);
                    if (entry.Outpost.def != null)
                    {
                        GUI.color = entry.Outpost.Faction?.Color ?? Color.white;
                        GUI.DrawTexture(InsetIconDrawRect(iconRect), entry.Outpost.def.ExpandingIconTexture, ScaleMode.ScaleToFit);
                        GUI.color = Color.white;
                    }
                    Text.Anchor = TextAnchor.MiddleLeft;
                    Widgets.Label(new Rect(cellRect.x + IconSize + 6f, innerY, cellRect.width - IconSize - 10f, LineHeight), entry.DisplayLabel.Truncate(cellRect.width - IconSize - 12f));
                    Text.Anchor = TextAnchor.UpperLeft;
                    TooltipHandler.TipRegion(cellRect, entry.Tooltip);
                    if (Widgets.ButtonInvisible(cellRect))
                        JumpToWorldObject(entry.Outpost);
                    innerY += LineHeight;
                }
                if (outpostsHasMore)
                {
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(new Rect(0f, innerY, scrollRect.width, LineHeight), dashMoreEntries);
                    Text.Anchor = TextAnchor.UpperLeft;
                }
            }
            Widgets.EndScrollView();
        }

        private void DrawTravelersColumn(Rect colRect)
        {
            colRect = InsetColumnContent(colRect);
            float y = colRect.y;
            DrawColumnHeader(colRect, hdrTravelers, ClickOpenTravelers, ref y);
            int nowTick = Find.TickManager.TicksGame;
            if (nowTick - travelerDashCacheTick >= TravelerUiRefreshIntervalTicks)
                RebuildTravelerDashRows(nowTick);

            int targetingYou = cachedTravelerTargetingYouCount;
            if (targetingYou > 0)
            {
                if (cachedTargetingYouLabel == null || cachedTargetingYouCount != targetingYou)
                {
                    cachedTargetingYouLabel = "TSA_WD_Dash_TravelersTargetingYou".Translate(targetingYou).Colorize(Color.red);
                    cachedTargetingYouCount = targetingYou;
                }
                Rect bannerRect = new Rect(colRect.x, y, colRect.width, LineHeight);
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(bannerRect, cachedTargetingYouLabel);
                Text.Anchor = TextAnchor.UpperLeft;
                y += LineHeight;
            }

            int travelerCount = travelerDashTotalCount;
            int travelerDisplay = travelerDashRows.Count;
            bool travelersHasMore = travelerCount > MaxDashRows;
            Rect viewRect = new Rect(colRect.x, y, colRect.width, colRect.yMax - y);
            float contentH = travelerCount > 0 ? (travelerDisplay + (travelersHasMore ? 1 : 0)) * LineHeight : LineHeight;
            // Only reserve scrollbar gutter when needed so the strength column can reach the config button edge.
            float scrollBarGutter = contentH > viewRect.height ? 16f : 0f;
            Rect scrollRect = new Rect(0f, 0f, colRect.width - scrollBarGutter, contentH);
            Widgets.BeginScrollView(viewRect, ref travelersScrollPos, scrollRect);
            float innerY = 0f;
            if (travelerCount == 0)
            {
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(0f, innerY, scrollRect.width, LineHeight), dashNoTravelers);
                Text.Anchor = TextAnchor.UpperLeft;
            }
            else
            {
                float colType = TravelerTypeColWidth;
                float colTime = TravelerTimeColWidth;
                float colStrength = TravelerStrengthColWidth;
                float remaining = scrollRect.width - colType - colTime - colStrength;
                float colActor = remaining * 0.52f;
                float colTarget = remaining - colActor;
                for (int i = 0; i < travelerDisplay; i++)
                {
                    TravelerDashRowData row = travelerDashRows[i];
                    WorldObject_Traveler t = row.T;
                    Rect rowRect = new Rect(0f, innerY, scrollRect.width, LineHeight);
                    if (Mouse.IsOver(rowRect)) Widgets.DrawLightHighlight(rowRect);
                    Text.Anchor = TextAnchor.MiddleLeft;
                    float rowX = 0f;
                    Rect typeRect = new Rect(rowX, innerY, colType, LineHeight);
                    Rect iconDraw = new Rect(typeRect.x, innerY + (LineHeight - IconSize) / 2f, IconSize, IconSize);
                    if (row.MissionIcon != null)
                    {
                        GUI.color = t.Faction?.Color ?? Color.white;
                        GUI.DrawTexture(InsetIconDrawRect(iconDraw), row.MissionIcon, ScaleMode.ScaleToFit);
                        GUI.color = Color.white;
                    }
                    TooltipHandler.TipRegion(typeRect, row.MissionTip);
                    rowX += colType;
                    Rect actorRect = new Rect(rowX, innerY, colActor, LineHeight);
                    Rect targetRect = new Rect(rowX + colActor, innerY, colTarget, LineHeight);
                    if (t.mission == TravelerMission.Expansion)
                    {
                        DrawTravelerExpansionActorLikeActionLog(actorRect, t, row.OriginTip);
                        DrawTravelerExpansionDestFromCache(targetRect, row.ExpansionDestTileId, row.ExpansionDestLabel, row.TargetTip);
                    }
                    else if (UsesPathDestinationTile(t.mission))
                    {
                        DrawTravelerWorldObjectCell(actorRect, t.originObject, row.OriginTip);
                        DrawTravelerExpansionDestFromCache(targetRect, row.ExpansionDestTileId, row.ExpansionDestLabel, row.TargetTip);
                    }
                    else
                    {
                        DrawTravelerWorldObjectCell(actorRect, t.originObject, row.OriginTip);
                        DrawTravelerWorldObjectCell(targetRect, t.targetObject, row.TargetTip);
                    }
                    rowX += colActor + colTarget;
                    Rect timeRect = new Rect(rowX, innerY, colTime, LineHeight);
                    TooltipHandler.TipRegion(timeRect, row.TimeTip);
                    Text.Anchor = TextAnchor.MiddleLeft;
                    Widgets.Label(timeRect, row.TimeLabel);
                    rowX += colTime;
                    Rect strRect = new Rect(rowX, innerY, colStrength, LineHeight);
                    float curStr = t.travelerStrength;
                    float depStr = t.initialStrength > 0f ? t.initialStrength : t.travelerStrength;
                    string arrDisp = row.ArrivalStrength > 0f ? row.ArrivalStrength.ToString("F0") : "—";
                    string strLabel = curStr.ToString("F0") + " / " + depStr.ToString("F0");
                    string strTip = "TSA_WD_Dash_Traveler_StrengthTooltip".Translate(
                        curStr.ToString("F0"), depStr.ToString("F0"), arrDisp);
                    TooltipHandler.TipRegion(strRect, strTip);
                    Widgets.Label(strRect, strLabel);
                    if (Widgets.ButtonInvisible(rowRect))
                        JumpToWorldObject(t);
                    Text.Anchor = TextAnchor.UpperLeft;
                    innerY += LineHeight;
                }
                if (travelersHasMore)
                {
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(new Rect(0f, innerY, scrollRect.width, LineHeight), dashMoreEntries);
                    Text.Anchor = TextAnchor.UpperLeft;
                }
            }
            Widgets.EndScrollView();
        }

        private void RebuildTravelerDashRows(int nowTick)
        {
            var allT = new List<WorldObject_Traveler>();
            foreach (WorldObject wo in Find.WorldObjects.AllWorldObjects)
            {
                if (wo is WorldObject_Traveler tr) allT.Add(tr);
            }

            int targetingYou = 0;
            for (int i = 0; i < allT.Count; i++)
            {
                if (allT[i].IsHostileNpcTravelerTargetingPlayer) targetingYou++;
            }
            cachedTravelerTargetingYouCount = targetingYou;
            travelerDashTotalCount = allT.Count;

            var sorted = WorldObject_Traveler.SortTravelersForUi(allT, MaxDashRows);
            var seth = WorldDominationMod.settings;
            travelerDashRows.Clear();
            string daysStr = "TSA_WD_Days".Translate();
            string unknownStr = "TSA_WD_Traveller_Unknown".Translate();
            foreach (WorldObject_Traveler t in sorted)
            {
                float arrStr = ComputeTravelerArrivalStrengthForDash(t, seth);
                float daysSince = (nowTick - t.spawnTick) / 60000f;
                bool hasTotal = t.TryGetTotalExpectedTravelDays(out float totalDays);
                string totalStr = hasTotal ? totalDays.ToString("F1") + " " + daysStr : "—";
                string totalTipSecond = hasTotal ? totalStr : unknownStr;
                string timeTip = "TSA_WD_Dash_Traveler_TimeTooltip".Translate(daysSince.ToString("F1"), totalTipSecond);
                string timeLabel = daysSince.ToString("F1") + " / " + (hasTotal ? totalDays.ToString("F1") : "—");

                string originLabel = GetTravelerOriginLabel(t);
                string targetLabel = GetTravelerTargetLabel(t, out int destTileId, out string destLabel);
                TravelerDashRowData row = new TravelerDashRowData
                {
                    T = t,
                    MissionIcon = WorldDomination_UIUtils.CachedTravelerMissionIcon(t),
                    ArrivalStrength = arrStr,
                    ExpansionDestTileId = destTileId,
                    ExpansionDestLabel = destLabel,
                    MissionTip = GetMissionLabel(t.mission),
                    OriginTip = string.IsNullOrEmpty(originLabel) ? null : dashTravelerStartTip + originLabel,
                    TargetTip = string.IsNullOrEmpty(targetLabel) ? null : dashTravelerDestTip + targetLabel,
                    TimeLabel = timeLabel,
                    TimeTip = timeTip
                };
                travelerDashRows.Add(row);
            }
            travelerDashCacheTick = nowTick;
        }

        private static string GetTravelerOriginLabel(WorldObject_Traveler t)
        {
            if (t.mission == TravelerMission.Expansion)
            {
                WorldObject src = t.originObject;
                return TravelerEndpointUtility.IsLiveEndpoint(src) ? FormatWorldObjectLabelLikeActionLog(src) : null;
            }
            return TravelerEndpointUtility.IsLiveEndpoint(t.originObject) ? FormatWorldObjectLabelLikeActionLog(t.originObject) : null;
        }

        private static string GetTravelerTargetLabel(WorldObject_Traveler t, out int destTileId, out string destLabel)
        {
            destTileId = -1;
            destLabel = null;
            if (t.mission == TravelerMission.Expansion || UsesPathDestinationTile(t.mission))
            {
                if (t.pather != null && t.pather.destTile.tileId >= 0)
                {
                    destTileId = t.pather.destTile.tileId;
                    destLabel = WorldTileInfo.GetBiomeLabel(destTileId).CapitalizeFirst() + $" ({destTileId})";
                    return destLabel;
                }
                return null;
            }
            return TravelerEndpointUtility.IsLiveEndpoint(t.targetObject) ? FormatWorldObjectLabelLikeActionLog(t.targetObject) : null;
        }

        private static bool UsesPathDestinationTile(TravelerMission mission) =>
            mission == TravelerMission.RoadBuilding
            || mission == TravelerMission.RoadBlock
            || mission == TravelerMission.SpikeTrap
            || mission == TravelerMission.Decontamination
            || mission == TravelerMission.NpcFortify
            || mission == TravelerMission.NpcAtTurret
            || mission == TravelerMission.AtTurret;

        private static string GetMissionLabel(TravelerMission mission) =>
            WorldObject_Traveler.GetMissionTypeLabel(mission);

        private static void JumpToWorldObject(WorldObject wo)
        {
            if (wo == null) return;
            CameraJumper.TryJump(wo);
            Find.WorldSelector.ClearSelection();
            Find.WorldSelector.Select(wo);
            SoundDefOf.Click.PlayOneShotOnCamera();
            if (Find.MainTabsRoot.OpenTab != null) Find.MainTabsRoot.EscapeCurrentTab();
        }

        private static void DrawColumnHeader(Rect colRect, string title, Action onClick, ref float y, string tooltip = null)
        {
            Rect headerRect = new Rect(colRect.x, y, colRect.width, SectionHeaderHeight);
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(headerRect, title);
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            if (!string.IsNullOrEmpty(tooltip))
                TooltipHandler.TipRegion(headerRect, tooltip);
            if (onClick != null && Widgets.ButtonInvisible(headerRect))
                onClick();
            y += SectionHeaderHeight + SectionPadding;
        }

        private void DrawOutpostsColumnHeader(Rect colRect, ref float y)
        {
            float toggleW = ThreatsHeaderToggleSize;
            Rect titleRect = new Rect(colRect.x, y, Mathf.Max(40f, colRect.width - toggleW - 6f), SectionHeaderHeight);
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(titleRect, hdrOutposts);
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            if (Widgets.ButtonInvisible(titleRect))
                ClickOpenOutpost();

            Rect sortBtn = new Rect(
                colRect.xMax - toggleW,
                y + (SectionHeaderHeight - ThreatsHeaderToggleSize) * 0.5f,
                ThreatsHeaderToggleSize,
                ThreatsHeaderToggleSize);
            string sortLabel = outpostsSortMode switch
            {
                OutpostDashSortMode.Strength => "S",
                OutpostDashSortMode.PawnCount => "P",
                _ => "F"
            };
            string sortTip = outpostsSortMode switch
            {
                OutpostDashSortMode.Strength => "TSA_WD_Dash_OutpostSort_StrengthTip".Translate().ToString(),
                OutpostDashSortMode.PawnCount => "TSA_WD_Dash_OutpostSort_PawnTip".Translate().ToString(),
                _ => "TSA_WD_Dash_OutpostSort_FoodTip".Translate().ToString()
            };
            if (Widgets.ButtonText(sortBtn, sortLabel, true, true, true))
            {
                outpostsSortMode = outpostsSortMode switch
                {
                    OutpostDashSortMode.Food => OutpostDashSortMode.Strength,
                    OutpostDashSortMode.Strength => OutpostDashSortMode.PawnCount,
                    _ => OutpostDashSortMode.Food
                };
                outpostDashCacheTick = -999999;
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
            TooltipHandler.TipRegion(sortBtn, sortTip);

            y += SectionHeaderHeight + SectionPadding;
        }

        private static string BuildThreatsHeaderTooltip(WorldComponent_SpreadManager manager)
        {
            float baseline = manager != null && manager.WorldThreatBaseline > 0f
                ? manager.WorldThreatBaseline
                : SamplePrimaryColonyStorytellerBaseline();
            var seth = WorldDominationMod.settings;
            if (seth == null || seth.alwaysUseStrengthAsRaidPoints)
            {
                return "TSA_WD_Dash_ThreatsTooltip_NoClamp".Translate(baseline.ToString("F0")).ToString();
            }
            RaidPointsHelper.TryGetActiveClampPercents(out int minPct, out int maxPct, out string bandLabel);
            string lower = minPct + "%";
            string upper = maxPct + "%";
            if (!string.IsNullOrEmpty(bandLabel))
                return "TSA_WD_Dash_ThreatsTooltip_Staged".Translate(baseline.ToString("F0"), lower, upper, bandLabel).ToString();
            return "TSA_WD_Dash_ThreatsTooltip".Translate(baseline.ToString("F0"), lower, upper).ToString();
        }

        private static float SamplePrimaryColonyStorytellerBaseline()
        {
            Settlement colony = FindPrimaryPlayerColony();
            if (colony?.Map == null) return 0f;
            float baseline = StorytellerUtility.DefaultThreatPointsNow(colony.Map);
            return baseline > 0f ? baseline : 0f;
        }

        private static Settlement FindPrimaryPlayerColony()
        {
            Map current = Find.CurrentMap;
            if (current?.Parent is Settlement currentSettlement
                && currentSettlement.Faction?.IsPlayer == true
                && currentSettlement.HasMap)
                return currentSettlement;

            var settlements = Find.WorldObjects?.Settlements;
            if (settlements == null) return null;
            Faction player = Faction.OfPlayerSilentFail;
            if (player == null) return null;
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement s = settlements[i];
                if (s != null && s.Faction == player && s.HasMap)
                    return s;
            }
            return null;
        }

        private static void DrawColumnSeparator(float x, float y, float height)
        {
            GUI.color = new Color(1f, 1f, 1f, 0.15f);
            Widgets.DrawLineVertical(x, y, height);
            GUI.color = Color.white;
        }

        private static string BuildThreatSettlementTooltip(ThreatSettlementEntry entry, float displayPoints, float displayPct)
        {
            var sb = new System.Text.StringBuilder();
            if (entry.settlement != null)
                sb.AppendLine("TSA_WD_Dash_ThreatSettlementTipHeader".Translate(
                    entry.settlement.LabelCap, displayPoints.ToString("F0"), Mathf.RoundToInt(displayPct)).ToString());
            sb.AppendLine();
            sb.AppendLine("TSA_WD_Dash_ThreatAlliesHeader".Translate());
            if (!string.IsNullOrEmpty(entry.allyTooltip))
                sb.Append(entry.allyTooltip.TrimEnd());
            else
                sb.Append("TSA_WD_Dash_ThreatAlliesNone".Translate());
            sb.AppendLine();
            sb.AppendLine("TSA_WD_Dash_ThreatEta".Translate(entry.travelDays.ToString("F1")));
            if (!threatsShowClamped && entry.clampedPoints > 0f)
                sb.Append("TSA_WD_Dash_ThreatClamped".Translate(entry.clampedPoints.ToString("F0")));
            else if (threatsShowClamped && Mathf.Abs(entry.rawStrength - entry.clampedPoints) > 0.5f)
                sb.Append("TSA_WD_Dash_ThreatRaw".Translate(entry.rawStrength.ToString("F0")));
            return sb.ToString();
        }

        /// <summary>
        /// Arrival Strength is the "pre-raid analysis" projection locked in once by
        /// <see cref="WD_PathFollower.StartPath"/>: initialStrength × efficiency(launch-time path).
        /// It must not drift with current tile or current (decayed) strength — that's why there
        /// is no live recomputation fallback here. Zero means "not launched yet / unknown".
        /// </summary>
        private static float ComputeTravelerArrivalStrengthForDash(WorldObject_Traveler t, WorldDominationSettings seth)
        {
            return t.projectedArrivalStrength > 0f ? t.projectedArrivalStrength : 0f;
        }

        private static void DrawTravelerExpansionDestFromCache(Rect rect, int destTileId, string label, string tip)
        {
            if (destTileId < 0 || string.IsNullOrEmpty(label))
            {
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(rect, "---");
                Text.Anchor = TextAnchor.UpperLeft;
                return;
            }
            GlobalTargetInfo gti = new GlobalTargetInfo(destTileId);
            const float logIcon = 24f;
            Rect iconRect = new Rect(rect.x + 2f, rect.y + (rect.height - logIcon) / 2f, logIcon, logIcon);
            Rect iconDraw = InsetIconDrawRect(iconRect);
            if (!WorldDomination_UIUtils.TryDrawFactionIconForTarget(iconDraw, gti, out _))
            {
                Texture2D ph = WorldDomination_UIUtils.UnknownWorldTargetPlaceholderIcon;
                if (ph != null)
                    GUI.DrawTexture(iconDraw, ph, ScaleMode.ScaleToFit);
            }
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(
                new Rect(iconRect.xMax + 4f, rect.y, rect.width - (iconRect.width + 8f), rect.height),
                EllipsizeTravelerEndpointLabel(label));
            Text.Anchor = TextAnchor.UpperLeft;
            TooltipHandler.TipRegion(rect, tip ?? label);
        }

        /// <summary>Threat row color from storyteller % bands (matches world-threat tier thresholds).</summary>
        private static Color StorytellerPctColor(float pct)
        {
            if (pct < 80f) return Color.white;
            if (pct < 120f) return new Color(0.95f, 0.9f, 0.4f);       // moderate — yellow
            if (pct < 150f) return new Color(1f, 0.7f, 0.3f);          // heightened — orange
            if (pct < 200f) return new Color(1f, 0.35f, 0.35f);        // high — red
            return new Color(0.75f, 0.35f, 0.95f);                      // critical — purple
        }

        private void DrawNavButton(ref float x, float y, float width, string label, Action onClick, Texture2D icon, string tip)
        {
            DrawNavButtonAt(new Rect(x, y, width, ButtonHeight), label, () =>
            {
                onClick();
                SoundDefOf.Click.PlayOneShotOnCamera();
            }, icon, tip);
            x += width;
        }

        /// <summary>Width for an icon+label nav button from the current Small font metrics.</summary>
        private static float MeasureNavButtonWidth(string label)
        {
            Text.Font = GameFont.Small;
            float labelW = Text.CalcSize(label ?? string.Empty).x;
            return WorldDomination_UIUtils.SlateNavIconPad
                + WorldDomination_UIUtils.SlateNavIconSize
                + WorldDomination_UIUtils.SlateNavIconTextGap
                + labelW + 8f;
        }

        /// <summary>Slate fill + soft outline nav control. Optional tip includes hotkey when assigned.</summary>
        private static void DrawNavButtonAt(Rect r, string label, Action onClick, Texture2D icon, string tip)
        {
            if (WorldDomination_UIUtils.ButtonTextWithIcon(r, icon, label))
                onClick();
            if (!tip.NullOrEmpty())
                TooltipHandler.TipRegion(r, tip);
        }

        /// <summary>Hold-key chord for WD window shortcuts (Experimental settings). E.g. Left Alt+D.</summary>
        private static string FormatWdWindowHotkey(string letter)
        {
            KeyCode hold = WorldDominationMod.settings?.worldMapOverlayHoldKey
                ?? WorldDominationSettings.DefWorldMapOverlayHoldKey;
            return FormatOverlayHoldKeyLabel(hold) + "+" + letter;
        }

        private static string FormatOverlayHoldKeyLabel(KeyCode key)
        {
            switch (key)
            {
                case KeyCode.LeftAlt: return "Left Alt";
                case KeyCode.RightAlt: return "Right Alt";
                case KeyCode.LeftControl: return "Left Ctrl";
                case KeyCode.RightControl: return "Right Ctrl";
                case KeyCode.LeftShift: return "Left Shift";
                case KeyCode.RightShift: return "Right Shift";
                default: return key.ToString();
            }
        }

        private static void DrawGoodwillHighlightsBox(Rect rect, float pad)
        {
            if (rect.width < 80f || rect.height < 30f) return;
            Widgets.DrawBoxSolid(rect, StatusBoxFill);
            Rect inner = rect.ContractedBy(StatusBoxPad);
            if (inner.width < 40f || inner.height < 8f) return;

            Faction player = Faction.OfPlayerSilentFail;
            if (player == null) return;

            List<(Faction faction, int goodwill)> ranked = new List<(Faction, int)>();
            foreach (Faction f in Find.FactionManager.AllFactionsVisible)
            {
                if (f == null || f.IsPlayer || f.def.hidden || f.defeated) continue;
                ranked.Add((f, player.GoodwillWith(f)));
            }
            if (ranked.Count == 0)
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = Color.gray;
                Widgets.Label(inner, "TSA_WD_Dash_Goodwill_None".Translate().Truncate(inner.width));
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
                return;
            }

            ranked.Sort((a, b) => b.goodwill.CompareTo(a.goodwill));
            int take = Mathf.Min(3, ranked.Count);
            var best = new List<(Faction faction, int goodwill)>(take);
            var worst = new List<(Faction faction, int goodwill)>(take);
            for (int i = 0; i < take; i++)
                best.Add(ranked[i]);
            for (int i = 0; i < take; i++)
                worst.Add(ranked[ranked.Count - 1 - i]);

            float colGap = 10f;
            float colW = (inner.width - colGap) * 0.5f;
            Rect leftCol = new Rect(inner.x, inner.y, colW, inner.height);
            Rect rightCol = new Rect(inner.xMax - colW, inner.y, colW, inner.height);

            DrawGoodwillColumn(leftCol, "TSA_WD_Dash_Goodwill_Best".Translate(), best);
            DrawGoodwillColumn(rightCol, "TSA_WD_Dash_Goodwill_Worst".Translate(), worst);
            TooltipHandler.TipRegion(rect, "TSA_WD_Dash_Goodwill_Tip".Translate());
        }

        private static void DrawGoodwillColumn(
            Rect col,
            string header,
            List<(Faction faction, int goodwill)> rows)
        {
            const float iconSz = 14f;
            const float iconGap = 4f;
            // UI_WINDOWS: Small header ≥24, Tiny rows ≥15 (use measured line height when larger).
            float headerH = Mathf.Max(SmallLabelHeight, Text.LineHeightOf(GameFont.Small));
            float rowH = Mathf.Max(TinyLabelHeight, Text.LineHeightOf(GameFont.Tiny));
            float y = col.y;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(new Rect(col.x, y, col.width, headerH), header.Truncate(col.width));
            y += headerH;

            Text.Font = GameFont.Tiny;
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                Rect rowRect = new Rect(col.x, y, col.width, rowH);
                float x = rowRect.x;
                if (row.faction?.def != null)
                {
                    Rect iconRect = new Rect(x, rowRect.y + (rowH - iconSz) * 0.5f, iconSz, iconSz);
                    WorldDomination_UIUtils.DrawFactionIconWithColor(iconRect, row.faction);
                    x = iconRect.xMax + iconGap;
                }

                string gw = row.goodwill.ToString("+0;-#;0");
                float gwW = Text.CalcSize(gw).x + GoodwillValueRightPad;
                Rect gwRect = new Rect(rowRect.xMax - gwW, rowRect.y, gwW, rowH);
                float nameW = Mathf.Max(0f, gwRect.x - x - 4f);
                string name = (row.faction?.Name ?? "?").Truncate(nameW);

                GUI.color = ColorForRelationWithPlayer(row.faction);
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(x, rowRect.y, nameW, rowH), name);
                GUI.color = row.goodwill >= 0
                    ? new Color(0.45f, 0.85f, 0.45f)
                    : new Color(1f, 0.4f, 0.4f);
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(new Rect(gwRect.x, gwRect.y, gwRect.width - GoodwillValueRightPad + 2f, gwRect.height), gw);
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = Color.white;

                TooltipHandler.TipRegion(rowRect, $"{row.faction?.Name ?? "?"}: {gw}");
                y += rowH;
            }

            Text.Anchor = TextAnchor.UpperLeft;
        }

        /// <summary>Same colors as Diplomacy matrix / vanilla (Hostile red, Neutral cyan-blue, Ally green).</summary>
        private static Color ColorForRelationWithPlayer(Faction faction) =>
            WorldDomination_UIUtils.ColorForRelationWithPlayer(faction);

        private static Color RaidLaunchCapsRowColor(int count, int cap)
        {
            // Per request: red if no raid launched yet, yellow if some, green if max reached.
            if (count <= 0) return new Color(1f, 0.35f, 0.35f);
            if (cap <= 0) return Color.white;
            if (count >= cap) return new Color(0.28f, 0.78f, 0.32f); // match RaidUIUtils ForecastWinGreen
            return new Color(0.95f, 0.9f, 0.4f);
        }

        private static void DrawPlayerWdRaidLaunchCapsBox(Rect rect)
        {
            if (rect.width < 120f || rect.height < 30f) return;
            Rect boxRect = rect;

            Widgets.DrawBoxSolid(boxRect, StatusBoxFill);

            Rect inner = boxRect.ContractedBy(StatusBoxPad);
            if (inner.height <= 0f) return;

            var mgr = Find.World?.GetComponent<WorldComponent_SpreadManager>();

            WorldDominationSettings seth = WorldDominationMod.settings;
            int countDay = 0;
            int count4Days = 0;
            int count7Days = 0;
            int capDay = 0;
            int cap4Days = 0;
            int cap7Days = 0;

            if (mgr != null)
            {
                mgr.GetPlayerWdRaidLaunchCounts(
                    seth,
                    out countDay,
                    out count4Days,
                    out count7Days,
                    out capDay,
                    out cap4Days,
                    out cap7Days);
            }
            else
            {
                if (seth != null)
                {
                    seth.ClampPlayerWdRaidRateCaps();
                    capDay = seth.maxPlayerWdRaidsPerDay;
                    cap4Days = Mathf.Max(seth.maxPlayerWdRaidsPer4Days, capDay);
                    cap7Days = Mathf.Max(seth.maxPlayerWdRaidsPer7Days, cap4Days);
                }
                else
                {
                    capDay = WorldDominationSettings.DefMaxPlayerWdRaidsPerDay;
                    cap4Days = WorldDominationSettings.DefMaxPlayerWdRaidsPer4Days;
                    cap7Days = WorldDominationSettings.DefMaxPlayerWdRaidsPer7Days;
                }
            }

            float headerH = Mathf.Max(SmallLabelHeight, Text.LineHeightOf(GameFont.Small));
            float rowH = Mathf.Max(TinyLabelHeight, Text.LineHeightOf(GameFont.Tiny));
            float contentH = headerH + rowH * 3f;
            float contentY = inner.y + Mathf.Max(0f, (inner.height - contentH) * 0.5f);

            var prevFont = Text.Font;
            Text.Anchor = TextAnchor.MiddleLeft;

            string tip = "TSA_WD_Dash_RaidsLaunchedAtYou_Tip".Translate().ToString();
            string header = "TSA_WD_Dash_RaidsLaunchedAtYou_Header".Translate().ToString();
            // Bullet enumerator (not a minus). Shared left edge with the header icon.
            const string bullet = "\u2022 ";
            string l0 = bullet + "TSA_WD_Dash_RaidsLaunchedAtYou_Today".Translate(countDay, capDay);
            string l1 = bullet + "TSA_WD_Dash_RaidsLaunchedAtYou_Last4Days".Translate(count4Days, cap4Days);
            string l2 = bullet + "TSA_WD_Dash_RaidsLaunchedAtYou_Last7Days".Translate(count7Days, cap7Days);

            Rect headerRect = new Rect(inner.x, contentY, inner.width, headerH);
            Rect r0 = new Rect(inner.x, contentY + headerH, inner.width, rowH);
            Rect r1 = new Rect(inner.x, contentY + headerH + rowH, inner.width, rowH);
            Rect r2 = new Rect(inner.x, contentY + headerH + rowH * 2f, inner.width, rowH);

            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            float headerTextX = headerRect.x;
            if (RaidersIcon != null)
            {
                // Flush left with bullet list (no left pad). Keep 4px top/bottom/right so art still shrinks a bit.
                const float iconPad = 4f;
                float iconSlot = headerH;
                Rect iconRect = new Rect(
                    headerRect.x,
                    headerRect.y + iconPad,
                    iconSlot - iconPad,
                    iconSlot - iconPad * 2f);
                GUI.color = RaidHeaderIconRed;
                Widgets.DrawTextureFitted(iconRect, RaidersIcon, 1f);
                GUI.color = Color.white;
                headerTextX = headerRect.x + iconSlot;
            }
            float headerLabelW = Mathf.Max(0f, headerRect.xMax - headerTextX);
            Widgets.Label(new Rect(headerTextX, headerRect.y, headerLabelW, headerH), header.Truncate(headerLabelW));

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = RaidLaunchCapsRowColor(countDay, capDay);
            Widgets.Label(r0, l0.Truncate(inner.width));
            GUI.color = RaidLaunchCapsRowColor(count4Days, cap4Days);
            Widgets.Label(r1, l1.Truncate(inner.width));
            GUI.color = RaidLaunchCapsRowColor(count7Days, cap7Days);
            Widgets.Label(r2, l2.Truncate(inner.width));
            GUI.color = Color.white;

            TooltipHandler.TipRegion(boxRect, tip);

            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = prevFont;
        }

        private void DrawFactionRankStatusHint(Rect rect, float pad)
        {
            if (rect.width < 40f) return;
            EnsureFactionRankCache();
            EnsureOutpostPawnSummaryCache();
            // Rank label may be empty briefly before world stats exist; still draw icon + pawn summary when possible.
            if (string.IsNullOrEmpty(cachedFactionRankLabel) && string.IsNullOrEmpty(cachedOutpostPawnSummaryLabel)
                && LeaderboardIcon == null)
                return;
            Rect inner = rect.ContractedBy(pad);
            if (inner.width < 20f || inner.height < 8f) return;

            Widgets.DrawBoxSolid(rect, StatusBoxFill);

            // Equal vertical slots for rank / difficulty / pawns so spacing between labels matches.
            // Small needs ≥24; Tiny uses the same slot height and MiddleLeft centering.
            float labelSlotH = Mathf.Max(SmallLabelHeight, Text.LineHeightOf(GameFont.Small));
            const float iconLabelGap = 6f;

            var mgr = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            WdEscalationStage stage = mgr?.cachedEscalationStage ?? WdEscalationStage.None;
            bool showLate = stage == WdEscalationStage.Late;
            bool showMid = stage == WdEscalationStage.Mid;
            bool showDifficulty = showLate || showMid;
            bool showSummary = !string.IsNullOrEmpty(cachedOutpostPawnSummaryLabel);
            bool showRankLabel = !string.IsNullOrEmpty(cachedFactionRankLabel);

            Texture2D rankIcon = LeaderboardIcon;
            float labelX = inner.x;
            if (rankIcon != null)
            {
                float iconSize = Mathf.Min(inner.height, FactionRankIconSize);
                Rect iconRect = new Rect(
                    inner.x,
                    inner.y + (inner.height - iconSize) * 0.5f,
                    iconSize,
                    iconSize);
                GUI.DrawTexture(iconRect, rankIcon, ScaleMode.ScaleToFit);

                if (cachedPlayerFactionRank > 0)
                {
                    string ordinal = Find.ActiveLanguageWorker.OrdinalNumber(cachedPlayerFactionRank);
                    float ordH = Mathf.Max(TinyLabelHeight, Text.LineHeightOf(GameFont.Tiny));
                    Rect ordinalRect = new Rect(
                        iconRect.x,
                        iconRect.y + (iconRect.height - ordH) * 0.5f - 4f,
                        iconRect.width,
                        ordH);
                    var prevFontOrd = Text.Font;
                    Text.Font = GameFont.Tiny;
                    Text.Anchor = TextAnchor.MiddleCenter;
                    GUI.color = new Color(0.29f, 0.18f, 0.03f);
                    Widgets.Label(ordinalRect, ordinal);
                    GUI.color = Color.white;
                    Text.Anchor = TextAnchor.UpperLeft;
                    Text.Font = prevFontOrd;
                }

                labelX = iconRect.xMax + iconLabelGap;
            }

            float labelW = Mathf.Max(0f, inner.xMax - labelX);
            int lineCount = (showRankLabel ? 1 : 0) + (showDifficulty ? 1 : 0) + (showSummary ? 1 : 0);
            if (lineCount <= 0) return;
            float stackH = lineCount * labelSlotH;
            float y = inner.y + Mathf.Max(0f, (inner.height - stackH) * 0.5f);

            var prevFont = Text.Font;
            if (showRankLabel)
            {
                Rect rankRect = new Rect(labelX, y, labelW, labelSlotH);
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = cachedFactionRankColor;
                Widgets.Label(rankRect, cachedFactionRankLabel.Truncate(labelW));
                GUI.color = Color.white;
                y += labelSlotH;

                string strength = (mgr?.cachedPlayerOutpostStrength ?? 0f).ToString("F0");
                string sharePct = ((mgr?.cachedPlayerGlobalShare ?? 0f) * 100f).ToString("F0");
                TooltipHandler.TipRegion(rankRect, "TSA_WD_Dash_FactionRankTip".Translate(strength, sharePct).ToString());
            }

            if (showDifficulty)
            {
                Rect diffRect = new Rect(labelX, y, labelW, labelSlotH);
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = showLate ? Color.red : Color.yellow;
                string diffLabel = showLate
                    ? "TSA_WD_Dash_LateGameDifficultyActive".Translate().ToString()
                    : "TSA_WD_Dash_MidGameDifficultyActive".Translate().ToString();
                Widgets.Label(diffRect, diffLabel.Truncate(labelW));
                GUI.color = Color.white;
                string escalationTip = WdEscalation.BuildActiveEffectsTooltip(WorldDominationMod.settings, stage);
                if (!string.IsNullOrEmpty(escalationTip))
                    TooltipHandler.TipRegion(diffRect, escalationTip);
                y += labelSlotH;
            }

            if (showSummary)
            {
                Rect summaryRect = new Rect(labelX, y, labelW, labelSlotH);
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = Color.gray;
                Widgets.Label(summaryRect, cachedOutpostPawnSummaryLabel.Truncate(labelW));
                GUI.color = Color.white;
                if (!string.IsNullOrEmpty(cachedOutpostPawnSummaryTip))
                    TooltipHandler.TipRegion(summaryRect, cachedOutpostPawnSummaryTip);
            }

            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = prevFont;
        }

        private void EnsureOutpostPawnSummaryCache()
        {
            int now = Find.TickManager.TicksGame;
            if (now - outpostPawnSummaryCacheTick < OutpostDashRefreshIntervalTicks
                && cachedOutpostPawnSummaryLabel != null)
                return;
            outpostPawnSummaryCacheTick = now;
            cachedOutpostPawnSummaryLabel = "";
            cachedOutpostPawnSummaryTip = "";

            int outpostCount = 0;
            int humanoids = 0;
            int mechanoids = 0;
            int animals = 0;
            int vehicles = 0;
            var worldObjects = Find.WorldObjects?.AllWorldObjects;
            if (worldObjects != null)
            {
                for (int i = 0; i < worldObjects.Count; i++)
                {
                    if (!(worldObjects[i] is WorldObject_WD_Outpost o) || o.Faction != Faction.OfPlayer)
                        continue;
                    outpostCount++;
                    humanoids += o.PawnCount;
                    mechanoids += o.StoredMechanoidPawnCount;
                    var storedTransport = o.StoredAnimalsAndVehicles;
                    for (int si = 0; si < storedTransport.Count; si++)
                    {
                        Pawn sp = storedTransport[si];
                        if (sp == null || sp.Destroyed || sp.Dead) continue;
                        if (VehicleFrameworkOutpostDissolveCompat.IsVehicleFrameworkVehiclePawn(sp))
                            vehicles++;
                        else
                            animals++;
                    }
                }
            }

            AddColonyPawnCountsToSummary(ref humanoids, ref mechanoids, ref animals, ref vehicles);

            int totalPawns = humanoids + mechanoids + animals + vehicles;
            cachedOutpostPawnSummaryLabel = "TSA_WD_Dash_OutpostPawnSummary"
                .Translate(totalPawns, outpostCount).ToString();
            cachedOutpostPawnSummaryTip = string.Join("\n",
                "TSA_WD_Dash_OutpostPawnTip_Humanoid".Translate(humanoids).ToString(),
                "TSA_WD_Dash_OutpostPawnTip_Mechanoid".Translate(mechanoids).ToString(),
                "TSA_WD_Dash_OutpostPawnTip_Animal".Translate(animals).ToString(),
                "TSA_WD_Dash_OutpostPawnTip_Vehicle".Translate(vehicles).ToString());
        }

        /// <summary>Player colony map pawns (same kinds as the outpost summary tips), excluded outpost/defense/logistics maps.</summary>
        private static void AddColonyPawnCountsToSummary(ref int humanoids, ref int mechanoids, ref int animals, ref int vehicles)
        {
            Faction player = Faction.OfPlayer;
            if (player == null) return;

            var maps = Find.Maps;
            if (maps == null) return;
            for (int mi = 0; mi < maps.Count; mi++)
            {
                Map map = maps[mi];
                MapParent parent = map?.Parent;
                if (parent == null || parent.Faction != player) continue;
                if (parent is WorldObject_WD_Outpost) continue;
                if (parent.GetComponent<CompOutpostLogistics>() != null) continue;
                if (parent.def?.defName == "TSA_WD_OutpostDefenseSite") continue;

                var pawns = map.mapPawns?.AllPawnsSpawned;
                if (pawns == null) continue;
                for (int pi = 0; pi < pawns.Count; pi++)
                {
                    Pawn p = pawns[pi];
                    if (p == null || p.Faction != player || p.Dead) continue;
                    switch (PlayerPawnRosterUtility.ClassifyPawn(p))
                    {
                        case PlayerPawnSortCategory.Human:
                            humanoids++;
                            break;
                        case PlayerPawnSortCategory.Mechanoid:
                            mechanoids++;
                            break;
                        case PlayerPawnSortCategory.Vehicle:
                            vehicles++;
                            break;
                        default:
                            animals++;
                            break;
                    }
                }
            }
        }

        private void EnsureFactionRankCache()
        {
            int now = Find.TickManager.TicksGame;
            if (now - factionRankCacheTick < OutpostDashRefreshIntervalTicks && cachedFactionRankLabel != null)
                return;
            factionRankCacheTick = now;
            cachedPlayerFactionRank = 0;
            cachedFactionRankTotal = 0;
            cachedFactionRankLabel = null;
            cachedFactionRankColor = Color.white;

            // Live scan: daily snapshot can be built in FinalizeInit before occupant strength settles,
            // and must stay apples-to-apples with World Stats (same GetWorldPowerStats path).
            var list = WorldStatsUtils.GetWorldPowerStats()?.FactionStats;
            if (list == null || list.Count == 0) return;

            Faction player = Faction.OfPlayerSilentFail;
            if (player == null) return;

            int rank = -1;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i]?.faction == player)
                {
                    rank = i + 1;
                    break;
                }
            }
            if (rank < 1) return;

            cachedPlayerFactionRank = rank;
            cachedFactionRankTotal = list.Count;
            if (rank == 1)
            {
                cachedFactionRankLabel = "TSA_WD_Dash_FactionRankLeader".Translate().ToString();
                cachedFactionRankColor = Color.green;
            }
            else
            {
                string ordinal = Find.ActiveLanguageWorker.OrdinalNumber(rank);
                cachedFactionRankLabel = "TSA_WD_Dash_FactionRank".Translate(ordinal, cachedFactionRankTotal).ToString();
                // Per request: only green when leading; otherwise always plain white.
                cachedFactionRankColor = Color.white;
            }
        }

        /// <summary>Uses cached logistics registry instead of scanning AllWorldObjects. Pre-formats display strings.</summary>
        private static List<(WorldObject_WD_Outpost Outpost, string Label, float FoodCurrent, float FoodMax, float FoodNet, string DisplayLabel, Color DisplayColor, string Tooltip)> BuildPlayerOutpostsSorted(
            WorldComponent_LogisticsManager logi,
            OutpostDashSortMode sortMode)
        {
            var list = new List<(WorldObject_WD_Outpost, string, float, float, float, string, Color, string)>();
            if (logi == null) return list;
            var nodes = logi.GetCachedPlayerLogisticsNodes();
            for (int i = 0; i < nodes.Count; i++)
            {
                if (!(nodes[i].Obj is WorldObject_WD_Outpost wd)) continue;
                var lComp = wd.GetComponent<CompOutpostLogistics>();
                float foodCurrent = lComp?.currentFood ?? 0f;
                float foodMax = CompOutpostLogistics.GetEffectiveMaxFoodFor(wd);
                float net = logi.GetLogisticsNetDailyForOutpost(wd);
                string label = wd.LabelCap;
                int humanoids = wd.PawnCount;
                int totalPawns = wd.WorkerPawnCount;
                var viral = wd.GetComponent<CompViralSpread>();
                float strengthCur = viral?.strength ?? 0f;
                float strengthMax = viral?.GetMaxOffensiveStrength() ?? 0f;

                string displayLabel;
                string tooltip;
                Color rowColor;
                switch (sortMode)
                {
                    case OutpostDashSortMode.Strength:
                        rowColor = Color.white;
                        displayLabel = "TSA_WD_Dash_OutpostLine_Strength".Translate(
                            label,
                            strengthCur.ToString("F0"),
                            strengthMax.ToString("F0"),
                            humanoids.ToString(),
                            totalPawns.ToString()).ToString();
                        tooltip = "TSA_WD_Dash_OutpostTooltip_Strength".Translate(
                            label,
                            strengthCur.ToString("F0"),
                            strengthMax.ToString("F0"),
                            humanoids.ToString(),
                            totalPawns.ToString()).ToString();
                        break;
                    case OutpostDashSortMode.PawnCount:
                        rowColor = Color.white;
                        displayLabel = "TSA_WD_Dash_OutpostLine_Pawns".Translate(
                            label, humanoids.ToString(), totalPawns.ToString()).ToString();
                        tooltip = "TSA_WD_Dash_OutpostTooltip_Pawns".Translate(
                            label, humanoids.ToString(), totalPawns.ToString()).ToString();
                        break;
                    default:
                    {
                        string netSign = net >= 0 ? "+" : "";
                        rowColor = net > 0.1f ? Color.green : (net < -0.1f ? Color.red : Color.gray);
                        displayLabel = "TSA_WD_Dash_OutpostLine".Translate(
                            label, foodCurrent.ToString("F0"), foodMax.ToString("F0"), netSign + net.ToString("F0")).Colorize(rowColor);
                        tooltip = "TSA_WD_Dash_OutpostTooltip".Translate(
                            label, foodCurrent.ToString("F0"), foodMax.ToString("F0"), netSign + net.ToString("F1")).ToString();
                        break;
                    }
                }
                list.Add((wd, label, foodCurrent, foodMax, net, displayLabel, rowColor, tooltip));
            }

            switch (sortMode)
            {
                case OutpostDashSortMode.Strength:
                    list.Sort((a, b) =>
                    {
                        float sa = a.Item1.GetComponent<CompViralSpread>()?.strength ?? 0f;
                        float sb = b.Item1.GetComponent<CompViralSpread>()?.strength ?? 0f;
                        int c = sb.CompareTo(sa);
                        return c != 0 ? c : a.Item3.CompareTo(b.Item3);
                    });
                    break;
                case OutpostDashSortMode.PawnCount:
                    list.Sort((a, b) =>
                    {
                        int c = b.Item1.WorkerPawnCount.CompareTo(a.Item1.WorkerPawnCount);
                        return c != 0 ? c : a.Item3.CompareTo(b.Item3);
                    });
                    break;
                default:
                    // Food: lowest current food first (as before).
                    list.Sort((a, b) => a.Item3.CompareTo(b.Item3));
                    break;
            }
            return list;
        }

        private static string FormatWorldObjectLabelLikeActionLog(WorldObject obj) =>
            WorldDomination_UIUtils.FormatWorldObjectLabelLikeActionLog(obj);

        private static string EllipsizeTravelerEndpointLabel(string label)
        {
            if (string.IsNullOrEmpty(label) || label.Length <= TravelerEndpointLabelMaxChars)
                return label;
            return label.Substring(0, TravelerEndpointLabelMaxChars) + "...";
        }

        private static void DrawTravelerExpansionActorLikeActionLog(Rect rect, WorldObject_Traveler t, string tip)
        {
            WorldObject src = t.originObject;
            if (!TravelerEndpointUtility.IsLiveEndpoint(src))
            {
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(rect, "---");
                Text.Anchor = TextAnchor.UpperLeft;
                return;
            }
            const float logIcon = 24f;
            Rect iconRect = new Rect(rect.x + 2f, rect.y + (rect.height - logIcon) / 2f, logIcon, logIcon);
            WorldDomination_UIUtils.DrawFactionIconWithColor(InsetIconDrawRect(iconRect), new GlobalTargetInfo(src));
            string label = EllipsizeTravelerEndpointLabel(src.LabelCap);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(iconRect.xMax + 4f, rect.y, rect.width - (iconRect.width + 8f), rect.height), label);
            Text.Anchor = TextAnchor.UpperLeft;
            TooltipHandler.TipRegion(rect, tip ?? FormatWorldObjectLabelLikeActionLog(src));
        }

        private static void DrawTravelerWorldObjectCell(Rect rect, WorldObject wo, string tip)
        {
            if (!TravelerEndpointUtility.IsLiveEndpoint(wo))
            {
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(rect, dashNoneLabel ?? "—");
                Text.Anchor = TextAnchor.UpperLeft;
                return;
            }
            Rect iconRect = new Rect(rect.x + 2f, rect.y + (rect.height - IconSize) / 2f, IconSize, IconSize);
            Texture2D tex = null;
            if (wo is WorldObject_WD_Outpost op && op.def != null)
                tex = op.def.ExpandingIconTexture;
            if (tex == null && wo.Faction?.def?.FactionIcon != null)
                tex = wo.Faction.def.FactionIcon;
            if (tex != null)
            {
                GUI.color = wo.Faction != null ? wo.Faction.Color : Color.white;
                GUI.DrawTexture(InsetIconDrawRect(iconRect), tex, ScaleMode.ScaleToFit);
                GUI.color = Color.white;
            }
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(
                new Rect(iconRect.xMax + 4f, rect.y, rect.width - (iconRect.width + 6f), rect.height),
                EllipsizeTravelerEndpointLabel(wo.LabelCap));
            Text.Anchor = TextAnchor.UpperLeft;
            TooltipHandler.TipRegion(rect, tip ?? wo.LabelCap);
        }
    }
}
