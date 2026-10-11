using System;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Playing-time WD world debug overlay. Distinct from <see cref="Dialog_WdWorldSetup"/> (SSS layout).
    /// Opened from Dev Tools → World Domination → WD Debug Tools
    /// </summary>
    [StaticConstructorOnStartup]
    public class Dialog_WdWorldDebug : Window
    {
        private Vector2 scrollPosition;
        private static bool settlementsExpanded = true;
        private static bool fortsExpanded = true;
        private static bool raidsExpanded = true;
        private static bool progressionExpanded = true;
        private static bool questsExpanded = true;
        private static bool miscExpanded;

        private const float ToolButtonHeight = 36f;
        private const float NavButtonIconSize = 26f;
        private const float NavButtonIconPad = 8f;
        private const float NavButtonIconTextGap = 6f;
        private const float PlusMinusIconInnerPad = 3f;
        private const float ScrollbarWidth = 16f;
        /// <summary>Gap between chrome buttons and scrollbar (keep tight).</summary>
        private const float ScrollContentRightPad = 2f;
        private const float ExpandCollapseBtnW = 82f;
        private const float ExpandCollapseBtnH = 22f;

        private static readonly Color NavSlateFill = new Color(0.16f, 0.18f, 0.22f, 0.92f);
        private static readonly Color NavBtnBgHover = new Color(0.22f, 0.26f, 0.32f, 0.96f);
        private static readonly Color NavBtnBgPress = new Color(0.12f, 0.14f, 0.17f, 0.96f);
        private static readonly Color NavBtnBgSelected = new Color(0.26f, 0.32f, 0.40f, 0.98f);
        private static readonly Color NavBtnOutline = new Color(0.55f, 0.62f, 0.72f, 0.42f);
        private static readonly Color NavBtnOutlineHover = new Color(0.78f, 0.84f, 0.92f, 0.72f);
        private static readonly Color NavBtnOutlineSelected = new Color(0.55f, 0.85f, 1f, 0.70f);

        private static readonly Texture2D IconPlus = TexButton.Plus;
        private static readonly Texture2D IconMinus = TexButton.Minus;
        private static readonly Texture2D IconDelete = TexButton.Delete;
        private static readonly Texture2D IconTurret =
            ContentFinder<Texture2D>.Get(AtTurretUtility.TexturePathForTier(AtTurretTier.Medium), false);
        private static readonly Texture2D IconRoadBlock =
            ContentFinder<Texture2D>.Get("UI/Commands/Build_RoadBlock", false);
        private static readonly Texture2D IconTrap =
            ContentFinder<Texture2D>.Get("WorldObjects/WorldSpikeTrap", false);
        private static readonly Texture2D IconAdv =
            ContentFinder<Texture2D>.Get("WorldObjects/AdvWO_Silo", false) ?? IconRoadBlock;
        private static readonly Texture2D IconAttack = TexCommand.Attack;
        private static readonly Texture2D IconConfig =
            ContentFinder<Texture2D>.Get("UI/Commands/Config", false) ?? TexButton.Info;

        public override Vector2 InitialSize
        {
            get
            {
                float h = Mathf.Min(780f, UI.screenHeight - 48f);
                return new Vector2(430f, h);
            }
        }

        public Dialog_WdWorldDebug()
        {
            doCloseButton = true;
            doCloseX = true;
            draggable = true;
            absorbInputAroundWindow = false;
            forcePause = false;
            closeOnClickedOutside = false;
            preventCameraMotion = false;
            optionalTitle = null;
        }

        protected override void SetInitialSizeAndPosition()
        {
            Vector2 size = InitialSize;
            float x = 12f;
            float y = 12f;
            if (y + size.y > UI.screenHeight - 8f)
                y = Mathf.Max(8f, UI.screenHeight - size.y - 8f);
            windowRect = new Rect(x, y, size.x, size.y).Rounded();
        }

        public override void PreClose()
        {
            WD_WorldDebugOps.CancelActive();
            base.PreClose();
        }

        public override void DoWindowContents(Rect inRect)
        {
            WD_WorldDebugOps.TickClearIfIdle();

            float y = inRect.y;
            float titleH = Outpost_Dialog_UI.DialogTitleHeight;
            float midY = y + titleH * 0.5f;
            float bottomReserve = 44f;
            // Chrome listing width (same as scroll content); Collapse all right-aligns to this.
            float viewW = inRect.width - ScrollbarWidth - ScrollContentRightPad;
            float chromeRight = inRect.x + viewW;

            Rect collapseRect = new Rect(
                chromeRight - ExpandCollapseBtnW,
                midY - ExpandCollapseBtnH * 0.5f,
                ExpandCollapseBtnW,
                ExpandCollapseBtnH);
            Rect expandRect = new Rect(
                collapseRect.x - 4f - ExpandCollapseBtnW,
                collapseRect.y,
                ExpandCollapseBtnW,
                ExpandCollapseBtnH);
            float titleLabelW = Mathf.Max(0f, expandRect.x - inRect.x - 6f);
            Text.Font = GameFont.Medium;
            float titleTextH = Text.LineHeight;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(
                new Rect(inRect.x, midY - titleTextH * 0.5f, titleLabelW, titleTextH),
                "WD Debug Tools");
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Tiny;
            if (Widgets.ButtonText(expandRect, "TSA_WD_Settings_ExpandAll".Translate()))
                SetAllExpanded(true);
            if (Widgets.ButtonText(collapseRect, "TSA_WD_Settings_CollapseAll".Translate()))
                SetAllExpanded(false);
            y += Outpost_Dialog_UI.DialogTitleRowAdvance;
            Text.Font = GameFont.Small;

            Rect outRect = new Rect(inRect.x, y, inRect.width, inRect.height - (y - inRect.y) - bottomReserve);
            Rect viewRect = new Rect(0f, 0f, viewW, EstimateViewHeight());
            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
            var listing = new Listing_Standard();
            listing.Begin(viewRect);

            DrawSettlements(listing);
            DrawForts(listing);
            DrawRaids(listing);
            DrawProgression(listing);
            DrawQuests(listing);
            DrawMisc(listing);

            listing.End();
            Widgets.EndScrollView();
        }

        private static void SetAllExpanded(bool expanded)
        {
            settlementsExpanded = expanded;
            fortsExpanded = expanded;
            raidsExpanded = expanded;
            progressionExpanded = expanded;
            questsExpanded = expanded;
            miscExpanded = expanded;
        }

        private static void DrawSettlements(Listing_Standard listing)
        {
            if (!DrawSection(listing, "Settlements", ref settlementsExpanded)) return;

            DrawInstant(listing, "Spawn WD settlement (multi-place)...", IconConfig,
                "Open faction/tier menus, then multi-place WD settlements on the world map.",
                DebugActions_Settlements.SpawnWdSettlementMultiPlace);
            DrawClick(listing, "Upgrade tier", IconPlus,
                "WD debug: click a settlement or outpost to upgrade tier.",
                DebugActions_Settlements.UpgradeSettlementTier, PlusMinusIconInnerPad);
            DrawClick(listing, "Downgrade tier", IconMinus,
                "WD debug: click a settlement or outpost to downgrade tier.",
                DebugActions_Settlements.DowngradeSettlementTier, PlusMinusIconInnerPad);
            DrawClick(listing, "Strength +100", IconPlus,
                "WD debug: click a settlement or outpost to add strength.",
                DebugActions_Settlements.DebugStrengthPlus100, PlusMinusIconInnerPad);
            DrawClick(listing, "Strength -100", IconMinus,
                "WD debug: click a settlement or outpost to reduce strength.",
                DebugActions_Settlements.DebugStrengthMinus100, PlusMinusIconInnerPad);
            DrawClick(listing, "Clear raid protection CD", IconDelete,
                "WD debug: click a settlement, outpost, or colony to clear raid protection.",
                DebugActions_Settlements.ClearRaidProtectionCooldown);
        }

        private static void DrawForts(Listing_Standard listing)
        {
            if (!DrawSection(listing, "Fortifications and buildings", ref fortsExpanded)) return;

            DrawAction(listing, "Place road block", IconRoadBlock,
                "Faction → kind → multi-place road blocks on valid tiles.",
                DebugActions_Travelers.CreateRoadblockHere);
            DrawAction(listing, "Place spike trap", IconTrap,
                "Faction → kind → multi-place spike traps on valid tiles.",
                DebugActions_Travelers.CreateSpikeTrapHere);
            DrawAction(listing, "Place AT turret", IconTurret,
                "Faction → tier → multi-place AT turrets on valid tiles.",
                DebugActions_Travelers.CreateAtTurretHere);
            DrawSelfManaged(listing, "Place advanced building", IconAdv,
                "Pick building type, then click an NPC settlement to place nearby.",
                WD_WorldDebugOps.BeginPlaceAdvancedBuilding);
            DrawSelfManaged(listing, "Place road", IconConfig,
                "Pick road tier, then click start and end tiles to paint a path.",
                WD_WorldDebugOps.BeginPlaceRoad);
            DrawSelfManaged(listing, "Place bridge", IconConfig,
                "Click water-adjacent banks to place a stone bridge span.",
                WD_WorldDebugOps.BeginPlaceBridge);
            DrawAction(listing, "Delete fort / advanced building here", IconDelete,
                "Click a tile to remove a road block, spike trap, AT, or advanced building.",
                DebugActions_Travelers.DeleteRoadblockHere);
            DrawSelfManaged(listing, "Destroy road", IconDelete,
                "Click tiles to strip land road links.",
                WD_WorldDebugOps.BeginDestroyRoad);
            DrawSelfManaged(listing, "Destroy bridge", IconDelete,
                "Click a bank or water tile to destroy a bridge.",
                WD_WorldDebugOps.BeginDestroyBridge);
            DrawClick(listing, "Damage fortification 500", IconMinus,
                "WD debug: click a road block, spike trap, AT, or artillery turret (−500 HP).",
                WD_WorldDebugOps.DamageFortification500);
            DrawClick(listing, "Heal fortification 500", IconPlus,
                "WD debug: click a road block, spike trap, AT, or artillery turret (+500 HP).",
                WD_WorldDebugOps.HealFortification500);
            DrawClick(listing, "Force daily project now", IconAdv,
                "WD debug: click an NPC settlement to force a daily advanced building project.",
                DebugActions_AdvancedBuildings.ForceAttempt);
        }

        private static void DrawRaids(Listing_Standard listing)
        {
            if (!DrawSection(listing, "Raids and travelers", ref raidsExpanded)) return;

            DrawSelfManaged(listing, "Force raid from target", IconAttack,
                "Raid type → click an NPC settlement to force that raid style.",
                WD_WorldDebugOps.BeginForceRaidFromTarget);
            DrawClick(listing, "Force desperation raid", IconAttack,
                "WD debug: click an NPC settlement to force desperation.",
                DebugActions_Travelers.ForceDesperationRaidClickSettlement);
            DrawClick(listing, "Force Turtle", IconAttack,
                "WD debug: click an NPC settlement to force Turtle.",
                DebugActions_Travelers.ForceTurtleClickSettlement);
            DrawClick(listing, "Force Vanguard", IconAttack,
                "WD debug: click an NPC settlement to force Vanguard.",
                DebugActions_Travelers.ForceVanguardForwardAssault);
            DrawClick(listing, "Force Invasion", IconAttack,
                "WD debug: click an NPC settlement to force Invasion.",
                DebugActions_Travelers.ForceInvasionForwardAssault);
            DrawSelfManaged(listing, "Create raid", IconAttack,
                "Raid type → faction → strength → origin tile → dest tile (land bait if no target WO).",
                WD_WorldDebugOps.BeginCreateRaid);
            DrawSelfManaged(listing, "Create outpost / colony raid", IconAttack,
                "Click a player outpost or colony, then type → faction → strength → origin tile.",
                WD_WorldDebugOps.BeginCreateOutpostOrColonyRaid);
            DrawAction(listing, "Destroy travelers on tile", IconDelete,
                "Pick a tile and destroy all WD travelers there.",
                DebugActions_Travelers.DestroyTravelersOnTile);
            DrawInstant(listing, "Destroy all active travelers", IconDelete,
                "Immediately destroy every spawned WD traveler.",
                DebugActions_Travelers.DestroyAllActiveTravelers);
        }

        private static void DrawProgression(Listing_Standard listing)
        {
            if (!DrawSection(listing, "Game progression / Difficulty", ref progressionExpanded)) return;

            DrawInstant(listing, "Force Early Game", IconConfig,
                "Force escalation stage to Early.",
                DebugActions_Escalation.ForceEarlyGame);
            DrawInstant(listing, "Force Mid Game", IconConfig,
                "Force escalation stage to Mid.",
                DebugActions_Escalation.ForceMidGame);
            DrawInstant(listing, "Force Late Game", IconConfig,
                "Force escalation stage to Late.",
                DebugActions_Escalation.ForceLateGame);
            DrawClick(listing, "Show cluster stats", IconConfig,
                "WD debug: click a settlement for cluster stats.",
                DebugActions_Travelers.ShowClusterStats);
        }

        private static void DrawQuests(Listing_Standard listing)
        {
            if (!DrawSection(listing, "Quests", ref questsExpanded)) return;

            DrawInstant(listing, "Road building quest: force generate", IconConfig,
                "Force the colony road-link quest (ignores setting; needs colony + nearby ally/neutral town).",
                DebugActions_Quests.RoadBuildingForceGenerate);
            DrawInstant(listing, "Build outpost quest: force generate", IconConfig,
                "Force the first-outpost intro quest (ignores setting; needs nearby ally/neutral asker).",
                DebugActions_Quests.BuildOutpostForceGenerate);
            DrawInstant(listing, "Capture settlement quest: force generate", IconConfig,
                "Force the common-enemy settlement quest (ignores setting; needs asker + shared enemy town).",
                DebugActions_Quests.CaptureSettlementForceGenerate);
            DrawInstant(listing, "Victory: force generate quest", IconConfig,
                "Start / re-roll the World Domination victory quest.",
                DebugActions_Quests.VictoryForceGenerateQuest);
            DrawInstant(listing, "Victory: set streak to 14", IconConfig,
                "Set victory hold-day streak to 14 (near completion).",
                DebugActions_Quests.VictoryStreak14);
            DrawInstant(listing, "Victory: force complete", IconConfig,
                "Complete active victory quest or open victory dialog.",
                DebugActions_Quests.VictoryForceComplete);
        }

        private static void DrawMisc(Listing_Standard listing)
        {
            if (!DrawSection(listing, "Misc", ref miscExpanded)) return;

            DrawInstant(listing, "Force random diplomacy change", IconConfig,
                "Force a random diplomacy buff/nerf change.",
                DebugActions_Travelers.ForceRandomDiplomacyChange);
            DrawAction(listing, "Warehouse: add full stack...", IconConfig,
                "Pick a thing def and add a full stack to a warehouse.",
                DebugActions_Warehouse.AddFullStackToWarehouse);
            DrawAction(listing, "Unlock outpost upgrade delivery", IconConfig,
                "Click a player outpost to clear a stuck pending upgrade delivery.",
                DebugActions_Travelers.UnlockOutpostUpgradeSelection);
            DrawAction(listing, "Add unwavering prisoner to outpost", IconConfig,
                "Click a player outpost to add an unwavering prisoner.",
                DebugActions_Travelers.AddUnwaveringPrisonerToOutpost);
            DrawAction(listing, "Simulate settlement conquered here", IconConfig,
                "Open conquest outpost dialog at a clicked ruins/settlement tile.",
                DebugActions_Travelers.OpenConquestOutpostDialogAtTile);
            DrawClick(listing, "Raid bias → player (15d)", IconConfig,
                "Click NPC site: that faction biases raids toward the player for 15 days (not a quest).",
                DebugActions_Quests.RaidBiasClickedToPlayer);
            DrawClick(listing, "Raid bias: clear", IconConfig,
                "Click NPC site: clear that faction’s raid bias.",
                DebugActions_Quests.RaidBiasClearClicked);
        }

        private static bool DrawSection(Listing_Standard listing, string title, ref bool expanded)
        {
            return SettingsUI.DrawCollapsibleHeader(listing, title, ref expanded, SettingsUI.SectionHeaderColor);
        }

        private static void DrawClick(Listing_Standard listing, string label, Texture2D icon, string tip, Action tool,
            float iconInnerPad = 0f)
        {
            bool selected = WD_WorldDebugOps.ActiveToolLabel == label;
            DrawDashButton(listing, label, icon, selected, () =>
                WD_WorldDebugOps.BeginRepeatClick(label, tip, tool), tip, iconInnerPad);
            listing.Gap(4f);
        }

        private static void DrawAction(Listing_Standard listing, string label, Texture2D icon, string tip, Action begin,
            float iconInnerPad = 0f)
        {
            bool selected = WD_WorldDebugOps.ActiveToolLabel == label;
            DrawDashButton(listing, label, icon, selected, () =>
                WD_WorldDebugOps.BeginAction(label, begin), tip, iconInnerPad);
            listing.Gap(4f);
        }

        /// <summary>Ops that manage their own <see cref="WD_WorldDebugOps.ActiveToolLabel"/> / toggle.</summary>
        private static void DrawSelfManaged(Listing_Standard listing, string label, Texture2D icon, string tip, Action begin,
            float iconInnerPad = 0f)
        {
            bool selected = WD_WorldDebugOps.ActiveToolLabel == label;
            DrawDashButton(listing, label, icon, selected, begin, tip, iconInnerPad);
            listing.Gap(4f);
        }

        private static void DrawInstant(Listing_Standard listing, string label, Texture2D icon, string tip, Action action,
            float iconInnerPad = 0f)
        {
            DrawDashButton(listing, label, icon, false, () => WD_WorldDebugOps.RunInstant(action), tip, iconInnerPad);
            listing.Gap(4f);
        }

        private static float EstimateViewHeight()
        {
            float h = 24f;
            h += SectionHeight(settlementsExpanded, 6);
            h += SectionHeight(fortsExpanded, 12);
            h += SectionHeight(raidsExpanded, 9);
            h += SectionHeight(progressionExpanded, 4);
            h += SectionHeight(questsExpanded, 6);
            h += SectionHeight(miscExpanded, 7);
            return Mathf.Max(h, 200f);
        }

        private static float SectionHeight(bool expanded, int buttons) =>
            40f + (expanded ? buttons * 40f + 12f : 0f);

        private static void DrawDashButton(Listing_Standard listing, string label, Texture2D icon,
            bool selected, Action onClick, string tip, float iconInnerPad = 0f)
        {
            Rect r = listing.GetRect(ToolButtonHeight);
            bool mouseOver = Mouse.IsOver(r);
            bool pressed = mouseOver && Input.GetMouseButton(0);
            Color bg = selected ? NavBtnBgSelected : pressed ? NavBtnBgPress : mouseOver ? NavBtnBgHover : NavSlateFill;
            Widgets.DrawBoxSolid(r, bg);
            GUI.color = selected ? NavBtnOutlineSelected : mouseOver ? NavBtnOutlineHover : NavBtnOutline;
            Widgets.DrawBox(r, 1);
            GUI.color = Color.white;

            float textLeft = r.x + NavButtonIconPad;
            if (icon != null)
            {
                float pad = Mathf.Max(0f, iconInnerPad);
                Rect iconRect = new Rect(
                    r.x + NavButtonIconPad + pad,
                    r.y + (r.height - NavButtonIconSize) * 0.5f + pad,
                    NavButtonIconSize - pad * 2f,
                    NavButtonIconSize - pad * 2f);
                Widgets.DrawTextureFitted(iconRect, icon, 1f);
                textLeft = r.x + NavButtonIconPad + NavButtonIconSize + NavButtonIconTextGap;
            }

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            float labelW = Mathf.Max(0f, r.xMax - textLeft - 4f);
            Widgets.Label(new Rect(textLeft, r.y, labelW, r.height), label.Truncate(labelW));
            Text.Anchor = TextAnchor.UpperLeft;

            if (!tip.NullOrEmpty())
                TooltipHandler.TipRegion(r, tip);
            if (Widgets.ButtonInvisible(r))
            {
                DebugActions_FloatMenus.NotifyDialogButtonClicked(r);
                SoundDefOf.Click.PlayOneShotOnCamera();
                onClick();
            }
        }
    }
}
