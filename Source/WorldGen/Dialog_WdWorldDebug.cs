using System;
using System.Collections.Generic;
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
        private enum DebugDashKind : byte
        {
            Click,
            Action,
            SelfManaged,
            Instant
        }

        private sealed class DebugDashItem
        {
            public string Id;
            public Texture2D Icon;
            public string Tip;
            public DebugDashKind Kind;
            public Action Action;
            public float IconInnerPad;
        }

        private Vector2 scrollPosition;
        private static bool favoritesExpanded = true;
        private static bool settlementsExpanded = true;
        private static bool fortsExpanded = true;
        private static bool raidsExpanded = true;
        private static bool progressionExpanded = true;
        private static bool questsExpanded = true;
        private static bool miscExpanded;

        private const float ToolButtonHeight = 36f;
        private const float StarColWidth = 36f;
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
        private static readonly Color StarFavorited = new Color(1f, 0.85f, 0.2f);
        private static readonly Color StarUnfavorited = new Color(0.55f, 0.55f, 0.55f, 0.7f);

        private static readonly Texture2D IconPlus = TexButton.Plus;
        private static readonly Texture2D IconMinus = TexButton.Minus;
        private static readonly Texture2D IconDelete = TexButton.Delete;
        private static readonly Texture2D IconTurret =
            ContentFinder<Texture2D>.Get(AtTurretUtility.TexturePathForTier(AtTurretTier.Medium), false);
        private static readonly Texture2D IconRoadBlock =
            ContentFinder<Texture2D>.Get("UI/Commands/Build_RoadBlock", false);
        private static readonly Texture2D IconTrap =
            ContentFinder<Texture2D>.Get("WorldObjects/Fortifications/WorldSpikeTrap", false);
        private static readonly Texture2D IconAdv =
            ContentFinder<Texture2D>.Get("WorldObjects/AdvancedBuildings/AdvWO_Silo", false) ?? IconRoadBlock;
        private static readonly Texture2D IconAttack = TexCommand.Attack;
        private static readonly Texture2D IconConfig =
            ContentFinder<Texture2D>.Get("UI/Commands/Config", false) ?? TexButton.Info;

        private static Dictionary<string, DebugDashItem> catalogById;
        private static List<DebugDashItem> settlementsTools;
        private static List<DebugDashItem> fortsTools;
        private static List<DebugDashItem> raidsTools;
        private static List<DebugDashItem> progressionTools;
        private static List<DebugDashItem> questsTools;
        private static List<DebugDashItem> miscTools;

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
            EnsureCatalog();
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

            DrawFavorites(listing);
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
            favoritesExpanded = expanded;
            settlementsExpanded = expanded;
            fortsExpanded = expanded;
            raidsExpanded = expanded;
            progressionExpanded = expanded;
            questsExpanded = expanded;
            miscExpanded = expanded;
        }

        private static void DrawFavorites(Listing_Standard listing)
        {
            if (!DrawSection(listing, "Favorites", ref favoritesExpanded)) return;

            var ids = WorldDominationMod.settings?.wdDebugFavoriteIds;
            if (ids == null || ids.Count == 0) return;

            for (int i = 0; i < ids.Count; i++)
            {
                if (catalogById != null && catalogById.TryGetValue(ids[i], out DebugDashItem item) && item != null)
                    DrawDashItem(listing, item);
            }
        }

        private static void DrawSettlements(Listing_Standard listing)
        {
            if (!DrawSection(listing, "Settlements", ref settlementsExpanded)) return;
            DrawToolList(listing, settlementsTools);
        }

        private static void DrawForts(Listing_Standard listing)
        {
            if (!DrawSection(listing, "Fortifications and buildings", ref fortsExpanded)) return;
            DrawToolList(listing, fortsTools);
        }

        private static void DrawRaids(Listing_Standard listing)
        {
            if (!DrawSection(listing, "Raids and travelers", ref raidsExpanded)) return;
            DrawToolList(listing, raidsTools);
        }

        private static void DrawProgression(Listing_Standard listing)
        {
            if (!DrawSection(listing, "Game progression / Difficulty", ref progressionExpanded)) return;
            DrawToolList(listing, progressionTools);
        }

        private static void DrawQuests(Listing_Standard listing)
        {
            if (!DrawSection(listing, "Quests", ref questsExpanded)) return;
            DrawToolList(listing, questsTools);
        }

        private static void DrawMisc(Listing_Standard listing)
        {
            if (!DrawSection(listing, "Misc", ref miscExpanded)) return;
            DrawToolList(listing, miscTools);
        }

        private static void DrawToolList(Listing_Standard listing, List<DebugDashItem> tools)
        {
            if (tools == null) return;
            for (int i = 0; i < tools.Count; i++)
                DrawDashItem(listing, tools[i]);
        }

        private static bool DrawSection(Listing_Standard listing, string title, ref bool expanded)
        {
            return SettingsUI.DrawCollapsibleHeader(listing, title, ref expanded, SettingsUI.SectionHeaderColor);
        }

        private static float EstimateViewHeight()
        {
            int favCount = WorldDominationMod.settings?.wdDebugFavoriteIds?.Count ?? 0;
            float h = 24f;
            h += SectionHeight(favoritesExpanded, favCount);
            h += SectionHeight(settlementsExpanded, settlementsTools?.Count ?? 6);
            h += SectionHeight(fortsExpanded, fortsTools?.Count ?? 12);
            h += SectionHeight(raidsExpanded, raidsTools?.Count ?? 9);
            h += SectionHeight(progressionExpanded, progressionTools?.Count ?? 4);
            h += SectionHeight(questsExpanded, questsTools?.Count ?? 6);
            h += SectionHeight(miscExpanded, miscTools?.Count ?? 10);
            return Mathf.Max(h, 200f);
        }

        private static float SectionHeight(bool expanded, int buttons) =>
            40f + (expanded ? buttons * 40f + 12f : 0f);

        private static void DrawDashItem(Listing_Standard listing, DebugDashItem item)
        {
            if (item == null) return;

            Rect row = listing.GetRect(ToolButtonHeight);
            Rect starCell = new Rect(row.x, row.y, StarColWidth, row.height);
            Rect btn = new Rect(row.x + StarColWidth, row.y, Mathf.Max(0f, row.width - StarColWidth), row.height);

            var settings = WorldDominationMod.settings;
            bool favorited = settings != null && settings.IsWdDebugFavorite(item.Id);

            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Medium;
            GUI.color = favorited ? StarFavorited : StarUnfavorited;
            Widgets.Label(starCell, favorited ? "★" : "☆");
            GUI.color = Color.white;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperLeft;
            TooltipHandler.TipRegion(starCell, "Favorite");
            if (Widgets.ButtonInvisible(starCell))
            {
                settings?.ToggleWdDebugFavorite(item.Id);
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            bool selected = item.Kind != DebugDashKind.Instant
                && WD_WorldDebugOps.ActiveToolLabel == item.Id;
            DrawDashButton(btn, item.Id, item.Icon, selected, () => InvokeItem(item), item.Tip, item.IconInnerPad);

            listing.Gap(4f);
        }

        private static void InvokeItem(DebugDashItem item)
        {
            if (item?.Action == null) return;
            switch (item.Kind)
            {
                case DebugDashKind.Click:
                    WD_WorldDebugOps.BeginRepeatClick(item.Id, item.Tip, item.Action);
                    break;
                case DebugDashKind.Action:
                    WD_WorldDebugOps.BeginAction(item.Id, item.Action);
                    break;
                case DebugDashKind.SelfManaged:
                    item.Action();
                    break;
                case DebugDashKind.Instant:
                    WD_WorldDebugOps.RunInstant(item.Action);
                    break;
            }
        }

        private static void DrawDashButton(Rect r, string label, Texture2D icon,
            bool selected, Action onClick, string tip, float iconInnerPad = 0f)
        {
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

        private static void EnsureCatalog()
        {
            if (catalogById != null) return;

            catalogById = new Dictionary<string, DebugDashItem>();
            settlementsTools = new List<DebugDashItem>();
            fortsTools = new List<DebugDashItem>();
            raidsTools = new List<DebugDashItem>();
            progressionTools = new List<DebugDashItem>();
            questsTools = new List<DebugDashItem>();
            miscTools = new List<DebugDashItem>();

            void Add(List<DebugDashItem> section, string id, Texture2D icon, string tip, DebugDashKind kind,
                Action action, float iconInnerPad = 0f)
            {
                var item = new DebugDashItem
                {
                    Id = id,
                    Icon = icon,
                    Tip = tip,
                    Kind = kind,
                    Action = action,
                    IconInnerPad = iconInnerPad
                };
                section.Add(item);
                catalogById[id] = item;
            }

            Add(settlementsTools, "Spawn WD settlement (multi-place)...", IconConfig,
                "Open faction/tier menus, then multi-place WD settlements on the world map.",
                DebugDashKind.Instant, DebugActions_Settlements.SpawnWdSettlementMultiPlace);
            Add(settlementsTools, "Upgrade tier", IconPlus,
                "WD debug: click a settlement or outpost to upgrade tier.",
                DebugDashKind.Click, DebugActions_Settlements.UpgradeSettlementTier, PlusMinusIconInnerPad);
            Add(settlementsTools, "Downgrade tier", IconMinus,
                "WD debug: click a settlement or outpost to downgrade tier.",
                DebugDashKind.Click, DebugActions_Settlements.DowngradeSettlementTier, PlusMinusIconInnerPad);
            Add(settlementsTools, "Strength +100", IconPlus,
                "WD debug: click a settlement or outpost to add strength.",
                DebugDashKind.Click, DebugActions_Settlements.DebugStrengthPlus100, PlusMinusIconInnerPad);
            Add(settlementsTools, "Strength -100", IconMinus,
                "WD debug: click a settlement or outpost to reduce strength.",
                DebugDashKind.Click, DebugActions_Settlements.DebugStrengthMinus100, PlusMinusIconInnerPad);
            Add(settlementsTools, "Clear raid protection CD", IconDelete,
                "WD debug: click a settlement, outpost, or colony to clear raid protection.",
                DebugDashKind.Click, DebugActions_Settlements.ClearRaidProtectionCooldown);

            Add(fortsTools, "Place road block", IconRoadBlock,
                "Faction → kind → multi-place road blocks on valid tiles.",
                DebugDashKind.Action, DebugActions_Travelers.CreateRoadblockHere);
            Add(fortsTools, "Place spike trap", IconTrap,
                "Faction → kind → multi-place spike traps on valid tiles.",
                DebugDashKind.Action, DebugActions_Travelers.CreateSpikeTrapHere);
            Add(fortsTools, "Place AT turret", IconTurret,
                "Faction → tier → multi-place AT turrets on valid tiles.",
                DebugDashKind.Action, DebugActions_Travelers.CreateAtTurretHere);
            Add(fortsTools, "Place advanced building", IconAdv,
                "Pick building type, then click an NPC settlement to place nearby.",
                DebugDashKind.SelfManaged, WD_WorldDebugOps.BeginPlaceAdvancedBuilding);
            Add(fortsTools, "Place road", IconConfig,
                "Pick road tier, then click start and end tiles to paint a path.",
                DebugDashKind.SelfManaged, WD_WorldDebugOps.BeginPlaceRoad);
            Add(fortsTools, "Place bridge", IconConfig,
                "Click water-adjacent banks to place a stone bridge span.",
                DebugDashKind.SelfManaged, WD_WorldDebugOps.BeginPlaceBridge);
            Add(fortsTools, "Delete Fortification/Building", IconDelete,
                "Click a tile to remove a road block, spike trap, AT, or advanced building.",
                DebugDashKind.Action, DebugActions_Travelers.DeleteRoadblockHere);
            Add(fortsTools, "Destroy road", IconDelete,
                "Click tiles to strip land road links.",
                DebugDashKind.SelfManaged, WD_WorldDebugOps.BeginDestroyRoad);
            Add(fortsTools, "Destroy bridge", IconDelete,
                "Click a bank or water tile to destroy a bridge.",
                DebugDashKind.SelfManaged, WD_WorldDebugOps.BeginDestroyBridge);
            Add(fortsTools, "Damage fortification 500", IconMinus,
                "WD debug: click a road block, spike trap, AT, or artillery turret (−500 HP).",
                DebugDashKind.Click, WD_WorldDebugOps.DamageFortification500);
            Add(fortsTools, "Heal fortification 500", IconPlus,
                "WD debug: click a road block, spike trap, AT, or artillery turret (+500 HP).",
                DebugDashKind.Click, WD_WorldDebugOps.HealFortification500);
            Add(fortsTools, "Force daily project now", IconAdv,
                "WD debug: click an NPC settlement to force a daily advanced building project.",
                DebugDashKind.Click, DebugActions_AdvancedBuildings.ForceAttempt);

            Add(raidsTools, "Force raid from target", IconAttack,
                "Raid type → click an NPC settlement to force that raid style.",
                DebugDashKind.SelfManaged, WD_WorldDebugOps.BeginForceRaidFromTarget);
            Add(raidsTools, "Force desperation raid", IconAttack,
                "WD debug: click an NPC settlement to force desperation.",
                DebugDashKind.Click, DebugActions_Travelers.ForceDesperationRaidClickSettlement);
            Add(raidsTools, "Force Turtle", IconAttack,
                "WD debug: click an NPC settlement to force Turtle.",
                DebugDashKind.Click, DebugActions_Travelers.ForceTurtleClickSettlement);
            Add(raidsTools, "Force Vanguard", IconAttack,
                "WD debug: click an NPC settlement to force Vanguard.",
                DebugDashKind.Click, DebugActions_Travelers.ForceVanguardForwardAssault);
            Add(raidsTools, "Force Invasion", IconAttack,
                "WD debug: click an NPC settlement to force Invasion.",
                DebugDashKind.Click, DebugActions_Travelers.ForceInvasionForwardAssault);
            Add(raidsTools, "Create raid", IconAttack,
                "Raid type → faction → strength → origin tile → dest tile (land bait if no target WO).",
                DebugDashKind.SelfManaged, WD_WorldDebugOps.BeginCreateRaid);
            Add(raidsTools, "Create outpost / colony raid", IconAttack,
                "Click a player outpost or colony, then type → faction → strength → origin tile.",
                DebugDashKind.SelfManaged, WD_WorldDebugOps.BeginCreateOutpostOrColonyRaid);
            Add(raidsTools, "Destroy travelers on tile", IconDelete,
                "Pick a tile and destroy all WD travelers there.",
                DebugDashKind.Action, DebugActions_Travelers.DestroyTravelersOnTile);
            Add(raidsTools, "Destroy all active travelers", IconDelete,
                "Immediately destroy every spawned WD traveler.",
                DebugDashKind.Instant, DebugActions_Travelers.DestroyAllActiveTravelers);

            Add(progressionTools, "Force Early Game", IconConfig,
                "Force escalation stage to Early.",
                DebugDashKind.Instant, DebugActions_Escalation.ForceEarlyGame);
            Add(progressionTools, "Force Mid Game", IconConfig,
                "Force escalation stage to Mid.",
                DebugDashKind.Instant, DebugActions_Escalation.ForceMidGame);
            Add(progressionTools, "Force Late Game", IconConfig,
                "Force escalation stage to Late.",
                DebugDashKind.Instant, DebugActions_Escalation.ForceLateGame);
            Add(progressionTools, "Show cluster stats", IconConfig,
                "WD debug: click a settlement for cluster stats.",
                DebugDashKind.Click, DebugActions_Travelers.ShowClusterStats);

            Add(questsTools, "Road building quest: force generate", IconConfig,
                "Force the colony road-link quest (ignores setting; needs colony + nearby ally/neutral town).",
                DebugDashKind.Instant, DebugActions_Quests.RoadBuildingForceGenerate);
            Add(questsTools, "Build outpost quest: force generate", IconConfig,
                "Force the first-outpost intro quest (ignores setting; needs nearby ally/neutral asker).",
                DebugDashKind.Instant, DebugActions_Quests.BuildOutpostForceGenerate);
            Add(questsTools, "Capture settlement quest: force generate", IconConfig,
                "Force the common-enemy settlement quest (ignores setting; needs asker + shared enemy town).",
                DebugDashKind.Instant, DebugActions_Quests.CaptureSettlementForceGenerate);
            Add(questsTools, "Victory: force generate quest", IconConfig,
                "Start / re-roll the World Domination victory quest.",
                DebugDashKind.Instant, DebugActions_Quests.VictoryForceGenerateQuest);
            Add(questsTools, "Victory: set streak to 14", IconConfig,
                "Set victory hold-day streak to 14 (near completion).",
                DebugDashKind.Instant, DebugActions_Quests.VictoryStreak14);
            Add(questsTools, "Victory: force complete", IconConfig,
                "Complete active victory quest or open victory dialog.",
                DebugDashKind.Instant, DebugActions_Quests.VictoryForceComplete);

            Add(miscTools, "Force random diplomacy change", IconConfig,
                "Force a random diplomacy buff/nerf change.",
                DebugDashKind.Instant, DebugActions_Travelers.ForceRandomDiplomacyChange);
            Add(miscTools, "Trigger revolt", IconConfig,
                "Pick rebel faction (defeated labeled), then click a seed settlement for a real 3-7 revolt.",
                DebugDashKind.SelfManaged, DebugActions_Settlements.BeginTriggerRevolt);
            Add(miscTools, "Defeat Faction", IconConfig,
                "Click an NPC WD settlement: destroy all of that faction's towns and mark defeated.",
                DebugDashKind.SelfManaged, DebugActions_Settlements.BeginDefeatFaction);
            Add(miscTools, "Change Settlement Owner", IconConfig,
                "Click an NPC WD settlement, then pick a new WD owner (defeated labeled). Replaces the settlement (name/type/strength kept); respawns owned AT/advanced buildings; restamps road blocks and spike traps.",
                DebugDashKind.SelfManaged, DebugActions_Settlements.BeginChangeSettlementOwner);
            Add(miscTools, "Warehouse: add full stack...", IconConfig,
                "Pick a thing def and add a full stack to a warehouse.",
                DebugDashKind.Action, DebugActions_Warehouse.AddFullStackToWarehouse);
            Add(miscTools, "Unlock outpost upgrade delivery", IconConfig,
                "Click a player outpost to clear a stuck pending upgrade delivery.",
                DebugDashKind.Action, DebugActions_Travelers.UnlockOutpostUpgradeSelection);
            Add(miscTools, "Add unwavering prisoner to outpost", IconConfig,
                "Click a player outpost to add an unwavering prisoner.",
                DebugDashKind.Action, DebugActions_Travelers.AddUnwaveringPrisonerToOutpost);
            Add(miscTools, "Simulate settlement conquered here", IconConfig,
                "Open conquest outpost dialog at a clicked ruins/settlement tile.",
                DebugDashKind.Action, DebugActions_Travelers.OpenConquestOutpostDialogAtTile);
            Add(miscTools, "Raid bias → player (15d)", IconConfig,
                "Click NPC site: that faction biases raids toward the player for 15 days (not a quest).",
                DebugDashKind.Click, DebugActions_Quests.RaidBiasClickedToPlayer);
            Add(miscTools, "Raid bias: clear", IconConfig,
                "Click NPC site: clear that faction’s raid bias.",
                DebugDashKind.Click, DebugActions_Quests.RaidBiasClearClicked);
        }
    }
}
