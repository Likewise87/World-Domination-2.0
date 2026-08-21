using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace TSA_WorldDomination
{
    public class WITab_Outpost_Logistics : WITab
    {
        private Vector2 scrollPosition;
        private float scrollViewHeight;
        private Vector2 consumerScrollPosition;
        private float consumerScrollViewHeight;

        private const int LogisticsRefreshThrottleTicks = 180; // 3 seconds
        private static int lastLogisticsRefreshTick = -9999;

        private const int LogisticsTabWorldScanIntervalTicks = 90;
        private int logisticsTabWorldScanTick = -999999;
        private WorldObject_WD_Outpost logisticsTabScanCachedSel;
        private readonly List<WorldObject> logisticsSupportOutpostsCache = new List<WorldObject>(8);
        private int logisticsConsumerLinksCacheTick = -999999;
        private WorldObject_WD_Outpost logisticsConsumerCacheSel;
        private readonly List<WorldComponent_LogisticsManager.LogisticsLink> logisticsConsumerInboundCache = new List<WorldComponent_LogisticsManager.LogisticsLink>(8);

        private static bool s_staticInit;
        private static string s_toggleLblSmart, s_toggleLblManual;
        private static string s_toggleTipSmart, s_toggleTipManual;
        private static string s_colonyPrefix, s_outpostInfoTip;
        private static string s_allocationStrategy, s_incomingSupplyLines, s_distributeTo;
        private static string s_keepHere, s_keepHereTip, s_totalNetTip;
        private static string s_resetAllocation, s_resetAllocationTip;

        private int foodSectionCacheTick = -999999;
        private int foodSectionCachedLogisticsGen = int.MinValue;
        private WorldObject_WD_Outpost foodSectionCachedSel;
        private OutpostStatsSection cachedFoodSection;

        private int producerRecipientScanCachedLogisticsGen = int.MinValue;
        private int consumerInboundCachedLogisticsGen = int.MinValue;

        private struct RowStrings
        {
            public string jump, name, info, status;
        }
        private readonly Dictionary<int, RowStrings> rowStrCache = new Dictionary<int, RowStrings>();
        private readonly Dictionary<int, RowStrings> consumerRowStrCache = new Dictionary<int, RowStrings>();

        /// <summary>Headline (medium) + horizontal rule + gap before the two-column body. Matches the Stats tab.</summary>
        private const float TabHeaderConsumedHeight = 38f;
        private const float ColumnGap = 20f;
        private const float LeftColumnFraction = 0.46f;
        private const float RightColumnBoost = 80f;
        private const float RowIconSize = 28f;
        private const float ProducerRowHeight = 58f;
        private const float ProducerRowStep = 62f;
        private const float AssignmentBtnW = 24f;
        private const float AssignmentValueW = 40f;
        private const float ProducerNetW = 60f;
        private const float ProducerScrollbarPad = 10f;

        public WITab_Outpost_Logistics() { this.size = new Vector2(860f, 560f); this.labelKey = "TSA_LogisticsTab"; }

        public override bool IsVisible =>
            WorldDominationMod.settings.foodLogisticsActive &&
            SelOutpost != null &&
            SelOutpost.Faction == Faction.OfPlayer;

        public WorldObject_WD_Outpost SelOutpost => base.SelObject as WorldObject_WD_Outpost;
        private CompOutpostLogistics _cachedLogi;
        private int _cachedLogiFrame = -1;
        public CompOutpostLogistics Logi
        {
            get
            {
                int frame = Time.frameCount;
                if (frame != _cachedLogiFrame)
                {
                    _cachedLogi = SelOutpost?.GetComponent<CompOutpostLogistics>();
                    _cachedLogiFrame = frame;
                }
                return _cachedLogi;
            }
        }

        private static void EnsureStaticInit()
        {
            if (s_staticInit) return;
            s_staticInit = true;
            string note = "TSA_AutoUpdateNote".Translate();
            s_toggleLblSmart = "TSA_LogisticsMode_Smart".Translate();
            s_toggleLblManual = "TSA_LogisticsMode_Manual".Translate();
            s_toggleTipSmart = "TSA_DeficitTooltip".Translate() + note;
            s_toggleTipManual = "TSA_ManualTooltip".Translate();
            string cl = "TSA_ColonyLabel".Translate().ToString();
            s_colonyPrefix = cl.Contains("TSA_ColonyLabel") ? "Colony" : cl;
            s_outpostInfoTip = "TSA_Logistics_CurrentMaxFood_Tooltip".Translate().ToString();
            s_allocationStrategy = "TSA_AllocationStrategy".Translate();
            s_incomingSupplyLines = "TSA_IncomingSupplyLines".Translate();
            s_distributeTo = "TSA_Logistics_SectionOtherOutposts".Translate();
            s_keepHere = "TSA_Logistics_KeepHere".Translate();
            s_keepHereTip = "TSA_Logistics_KeepHereTip".Translate();
            s_totalNetTip = "TSA_Logistics_TotalNetTip".Translate();
            s_resetAllocation = "TSA_Logistics_ResetAllocation".Translate();
            s_resetAllocationTip = "TSA_Logistics_ResetAllocationTip".Translate();
        }

        public override void OnOpen()
        {
            base.OnOpen();
            Find.World?.GetComponent<WorldComponent_LogisticsManager>()
                ?.TryReconcileSmartFromFingerprint("food tab open");
        }

        private static void LabelAnchored(Rect rect, string text, TextAnchor anchor)
        {
            TextAnchor prev = Text.Anchor;
            Text.Anchor = anchor;
            Widgets.Label(rect, text);
            Text.Anchor = prev;
        }

        /// <summary>Fertility / animal abundance header for farming, ranch, and hunting hubs (includes upgrade bonuses in tooltip).</summary>
        private static bool TryGetFoodProducerTileStat(
            WorldObject_WD_Outpost outpost,
            out string label,
            out string tooltip,
            out int pct)
        {
            label = null;
            tooltip = null;
            pct = 0;
            if (outpost?.def == null || !Outpost_Production_Utils.IsFoodProducerOutpost(outpost.def))
                return false;

            if (Outpost_Production_Utils.IsHuntingOutpost(outpost.def))
            {
                pct = Mathf.RoundToInt(Outpost_Production_Utils.GetHuntingTileProductionFactor(outpost) * 100f);
                label = "TSA_WD_Biome_ColAnimals".Translate() + ": " + "TSA_WD_Biome_AnimalsPercent".Translate(pct);
                tooltip = Outpost_Hunting.GetHuntingEfficiencyTooltip(outpost);
                return true;
            }

            if (Outpost_Production_Utils.IsFishingOutpost(outpost.def))
            {
                pct = Mathf.RoundToInt(Outpost_Production_Utils.GetFishingTileProductionFactor(outpost) * 100f);
                label = "TSA_WD_Biome_ColFish".Translate() + ": " + "TSA_WD_Biome_FishPercent".Translate(pct);
                tooltip = Outpost_Fishing.GetFishingEfficiencyTooltip(outpost);
                return true;
            }

            if (Outpost_Production_Utils.IsFarmingOutpost(outpost.def)
                || Outpost_Production_Utils.IsRanchOutpost(outpost.def))
            {
                pct = Outpost_Production_Utils.GetFarmingFertilityPercentInt(outpost);
                label = "TSA_WD_Biome_ColFertility".Translate() + ": " + "TSA_WD_Biome_FertilityPercent".Translate(pct);
                tooltip = Outpost_Farming.GetFarmingEfficiencyTooltip(outpost);
                return true;
            }

            return false;
        }

        private static string FormatNetColored(float net)
        {
            string netNum = net > 0.05f ? ("+" + net.ToString("F1")) : net.ToString("F1");
            return netNum.Colorize(net > 0.1f ? Color.green : (net < -0.1f ? Color.red : Color.yellow));
        }

        /// <summary>Shared column geometry for Keep row and destination rows (matches scroll content width).</summary>
        private static void GetProducerRowLayout(float rowWidth, out float nameX, out float nameW, out float netX, out float ctrlX, out float ctrlW)
        {
            ctrlW = AssignmentBtnW + AssignmentValueW + AssignmentBtnW;
            ctrlX = rowWidth - ctrlW - ProducerScrollbarPad;
            netX = ctrlX - ProducerNetW - 6f;
            nameX = RowIconSize + 6f;
            nameW = netX - nameX - 6f;
        }

        protected override void FillTab()
        {
            EnsureStaticInit();
            if (Logi == null) return;
            var manager = Find.World.GetComponent<WorldComponent_LogisticsManager>();
            bool isProducer = SelOutpost != null && Outpost_Production_Utils.IsFoodProducerOutpost(SelOutpost.def);

            // Producers need extra room for the recipient +/- table; receivers are lighter.
            this.size = new Vector2(isProducer ? 940f : 860f, 560f);

            Rect body = new Rect(0f, 0f, size.x, size.y).ContractedBy(10f);

            // Column geometry first so the header fertility/abundance slot can share the table's right edge.
            float leftColW = Mathf.Max(200f, body.width * LeftColumnFraction - RightColumnBoost);
            float rightX = body.x + leftColW + ColumnGap;
            float rightW = body.width - leftColW - ColumnGap;
            // Same content width / scrollbar pad as DrawSmartToggles / destination rows.
            float tableContentW = rightW - 16f;
            float tableRight = rightX + tableContentW - ProducerScrollbarPad;

            Text.Font = GameFont.Medium;
            string headline = OutpostTranslationUtil.TabHeadline(SelOutpost, "TSA_LogisticsTab");

            float headerSlotW = 0f;
            string biomeLabel = null;
            string biomeTip = null;
            Color biomeColor = Color.white;
            if (TryGetFoodProducerTileStat(SelOutpost, out biomeLabel, out biomeTip, out int biomePct))
            {
                headerSlotW = 175f;
                biomeColor = biomePct <= 30 ? Color.red : (biomePct <= 60 ? Color.yellow : Color.green);
            }

            float titleW = body.width - (headerSlotW > 0f ? headerSlotW + 8f : 0f);
            if (headerSlotW > 0f)
                titleW = Mathf.Max(80f, tableRight - headerSlotW - body.x - 8f);
            LabelAnchored(new Rect(body.x, body.y, titleW, 30f), headline, TextAnchor.MiddleLeft);
            if (headerSlotW > 0f)
            {
                Rect slotRect = new Rect(tableRight - headerSlotW, body.y, headerSlotW, 30f);
                Text.Font = GameFont.Small;
                GUI.color = biomeColor;
                LabelAnchored(slotRect, biomeLabel, TextAnchor.MiddleRight);
                GUI.color = Color.white;
                TooltipHandler.TipRegion(slotRect, biomeTip);
            }
            Text.Font = GameFont.Small;
            Widgets.DrawLineHorizontal(body.x, body.y + 32f, body.width);

            float contentY = body.y + TabHeaderConsumedHeight;
            float contentH = body.height - TabHeaderConsumedHeight;

            // --- Left column: DR banner (food producers) + food summary ---
            float leftY = contentY;
            if (isProducer && SelOutpost != null)
                leftY = Outpost_Dialog_UI.DrawSkillDiminishingReturnsBanner(body.x, leftY, leftColW, SelOutpost);
            var foodSection = EnsureFoodSectionFresh(manager);
            OutpostTabStatsUi.DrawSectionColumn(body.x, leftY, leftColW, foodSection);

            // Divider centered in the gap between columns.
            float dividerX = body.x + leftColW + ColumnGap * 0.5f;
            Widgets.DrawLineVertical(dividerX, contentY, contentH);

            // --- Right column: producer config (+/-) or receiver read-only list ---
            Rect rightRect = new Rect(rightX, contentY, rightW, contentH);
            if (isProducer)
                DrawProducerColumn(rightRect, manager);
            else
                DrawReceiverColumn(rightRect, manager);
        }

        private OutpostStatsSection EnsureFoodSectionFresh(WorldComponent_LogisticsManager manager)
        {
            int now = Find.TickManager.TicksGame;
            int gen = manager?.LogisticsNetDisplayGeneration ?? 0;
            bool dirty = cachedFoodSection == null
                || foodSectionCachedSel != SelOutpost
                || foodSectionCachedLogisticsGen != gen
                || now - foodSectionCacheTick >= LogisticsRefreshThrottleTicks;
            if (!dirty)
                return cachedFoodSection;

            foodSectionCacheTick = now;
            foodSectionCachedSel = SelOutpost;
            foodSectionCachedLogisticsGen = gen;
            cachedFoodSection = OutpostStatsSnapshot.BuildFoodSection(SelOutpost, manager, Logi, true);
            return cachedFoodSection;
        }

        private void DrawProducerColumn(Rect rightRect, WorldComponent_LogisticsManager manager)
        {
            EnsureLogisticsSupportListCached();
            manager.MergeDuplicateManualLinksFromSource(SelOutpost.Tile);
            // Do not ClampManualAssignmentsForSourceBudget every frame — that reshuffles other links
            // when the live budget wobbles under an unpaused game.

            float budget = manager.GetDistributionBudgetForSourceTile(SelOutpost.Tile);
            float outgoingFromHubAll = manager.GetOutgoingManualSumForTile(SelOutpost.Tile);
            float keepHere = Mathf.Max(0f, budget - outgoingFromHubAll);

            // Title only — no DrawSectionHeader underline (that line sat directly over Smart/Manual).
            Text.Font = GameFont.Small;
            GUI.color = Widgets.SeparatorLabelColor;
            Widgets.Label(new Rect(rightRect.x, rightRect.y, rightRect.width, 22f), s_allocationStrategy);
            GUI.color = Color.white;
            float y = rightRect.y + 24f;

            DrawSmartToggles(new Rect(rightRect.x, y, rightRect.width, 28f), manager);
            y += 32f;

            // Keep here: section headline + self row (same look as destinations, no +/-).
            GUI.color = Widgets.SeparatorLabelColor;
            Rect keepHeaderRect = new Rect(rightRect.x, y, rightRect.width, 22f);
            Widgets.Label(keepHeaderRect, s_keepHere);
            GUI.color = Color.white;
            TooltipHandler.TipRegion(keepHeaderRect, s_keepHereTip);
            y += 24f;

            float rowContentW = rightRect.width - 16f;
            Rect keepRow = new Rect(rightRect.x, y, rowContentW, ProducerRowHeight);
            DrawKeepHereRow(keepRow, manager, keepHere);
            y += ProducerRowStep;

            GUI.color = Widgets.SeparatorLabelColor;
            Widgets.Label(new Rect(rightRect.x, y, rightRect.width, 22f), s_distributeTo);
            GUI.color = Color.white;
            y += 24f;

            Rect listRect = new Rect(rightRect.x, y, rightRect.width, rightRect.yMax - y);
            DrawProducerInterface(listRect, manager, budget, outgoingFromHubAll, logisticsSupportOutpostsCache);
        }

        private void DrawKeepHereRow(Rect row, WorldComponent_LogisticsManager manager, float keepAmount)
        {
            Widgets.DrawHighlight(row);
            Widgets.DrawHighlightIfMouseover(row);

            GetProducerRowLayout(row.width, out float nameX, out float nameW, out float netX, out _, out _);

            float contentY = row.y + (row.height / 2f);
            Rect iconRect = new Rect(row.x, contentY - (RowIconSize / 2f), RowIconSize, RowIconSize);
            Texture2D iconTex = SelOutpost.def?.ExpandingIconTexture;
            if (iconTex != null)
            {
                GUI.color = SelOutpost.Faction?.Color ?? Color.white;
                GUI.DrawTexture(iconRect, iconTex, ScaleMode.ScaleToFit);
                GUI.color = Color.white;
            }

            string name = SelOutpost.LabelCap;
            Widgets.Label(new Rect(row.x + nameX, contentY - 21f, nameW, 24f), name.Truncate(nameW - 4f));

            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            string info = "TSA_OutpostInfoRow".Translate(
                Logi.currentFood.ToString("F1"),
                Logi.EffectiveMaxFood.ToString("F0")).ToString();
            Rect infoRect = new Rect(row.x + nameX, contentY + 2f, nameW, 20f);
            Widgets.Label(infoRect, info);
            TooltipHandler.TipRegion(infoRect, s_outpostInfoTip);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            // Same column/format as destination total-net: retained surplus as +/- colored value.
            Rect netRect = new Rect(row.x + netX, row.y, ProducerNetW, row.height);
            LabelAnchored(netRect, FormatNetColored(keepAmount), TextAnchor.MiddleCenter);
            TooltipHandler.TipRegion(netRect, s_keepHereTip);
        }

        private void DrawReceiverColumn(Rect rightRect, WorldComponent_LogisticsManager manager)
        {
            float y = OutpostTabStatsUi.DrawSectionHeader(rightRect.x, rightRect.y, rightRect.width, s_incomingSupplyLines);
            EnsureLogisticsConsumerInboundCached(manager);
            Rect listRect = new Rect(rightRect.x, y, rightRect.width, rightRect.yMax - y);
            DrawConsumerInterface(listRect, manager, logisticsConsumerInboundCache);
        }

        private void DrawSmartToggles(Rect rect, WorldComponent_LogisticsManager manager)
        {
            const float btnSize = 24f;
            const float labelGap = 4f;
            const float groupGap = 16f;
            const float resetBtnW = 140f;

            float smartLabelW = Text.CalcSize(s_toggleLblSmart).x;
            float smartGroupW = btnSize + labelGap + smartLabelW;
            float smartX = rect.x;
            Rect smartHit = new Rect(smartX, rect.y, smartGroupW, btnSize);
            if (Widgets.RadioButton(smartX, rect.y, LogisticsModeUtil.IsSmartMode(Logi.mode)))
            {
                Logi.mode = LogisticsMode.Smart;
                manager.NotifySmartLogisticsDirty();
                manager.CompleteSmartLogisticsRefreshNow();
            }
            LabelAnchored(new Rect(smartX + btnSize + labelGap, rect.y, smartLabelW, btnSize), s_toggleLblSmart, TextAnchor.MiddleLeft);
            TooltipHandler.TipRegion(smartHit, s_toggleTipSmart);

            float manualLabelW = Text.CalcSize(s_toggleLblManual).x;
            float manualGroupW = btnSize + labelGap + manualLabelW;
            float manualX = smartX + smartGroupW + groupGap;
            Rect manualHit = new Rect(manualX, rect.y, manualGroupW, btnSize);
            if (Widgets.RadioButton(manualX, rect.y, Logi.mode == LogisticsMode.Manual))
                Logi.mode = LogisticsMode.Manual;
            LabelAnchored(new Rect(manualX + btnSize + labelGap, rect.y, manualLabelW, btnSize), s_toggleLblManual, TextAnchor.MiddleLeft);
            TooltipHandler.TipRegion(manualHit, s_toggleTipManual);

            // Right edge matches destination rows (+ controls): content width minus scrollbar pad.
            float tableContentW = rect.width - 16f;
            float resetRight = rect.x + tableContentW - ProducerScrollbarPad;
            Rect resetRect = new Rect(resetRight - resetBtnW, rect.y, resetBtnW, btnSize);
            if (Widgets.ButtonText(resetRect, s_resetAllocation))
                ResetAllocationToManualZero(manager);
            TooltipHandler.TipRegion(resetRect, s_resetAllocationTip);
        }

        /// <summary>Switch to Manual and zero all outgoing food assignments from this producer.</summary>
        private void ResetAllocationToManualZero(WorldComponent_LogisticsManager manager)
        {
            if (SelOutpost == null || manager == null || Logi == null) return;
            Logi.mode = LogisticsMode.Manual;
            int hubTile = SelOutpost.Tile;
            bool changed = false;
            var links = manager.manualLinks;
            for (int i = 0; i < links.Count; i++)
            {
                if (links[i].sourceTile != hubTile) continue;
                if (links[i].manualAssignment <= 0.001f) continue;
                links[i].manualAssignment = 0f;
                changed = true;
            }
            if (changed)
                manager.NotifyManualLinksChanged();
            logisticsTabWorldScanTick = -999999;
            logisticsTabScanCachedSel = null;
            rowStrCache.Clear();
        }

        private void EnsureLogisticsSupportListCached()
        {
            int now = Find.TickManager.TicksGame;
            var mgr = Find.World?.GetComponent<WorldComponent_LogisticsManager>();
            int logiGen = mgr?.LogisticsNetDisplayGeneration ?? 0;
            if (logisticsTabScanCachedSel == SelOutpost
                && now - logisticsTabWorldScanTick < LogisticsTabWorldScanIntervalTicks
                && logiGen == producerRecipientScanCachedLogisticsGen)
                return;
            logisticsTabWorldScanTick = now;
            logisticsTabScanCachedSel = SelOutpost;
            producerRecipientScanCachedLogisticsGen = logiGen;
            logisticsSupportOutpostsCache.Clear();
            rowStrCache.Clear();
            var s = WorldDominationMod.settings;
            int hubTile = SelOutpost.Tile;
            if (mgr != null)
            {
                var nodes = mgr.GetCachedPlayerLogisticsNodes();
                for (int i = 0; i < nodes.Count; i++)
                {
                    var obj = nodes[i].Obj;
                    if (obj == SelOutpost) continue;
                    if (Outpost_Production_Utils.IsFoodProducerOutpost(obj.def)) continue;
                    if (Find.WorldGrid.ApproxDistanceInTiles(hubTile, obj.Tile) <= s.maxLogisticsRange)
                        logisticsSupportOutpostsCache.Add(obj);
                }
            }
            int hub = hubTile;
            logisticsSupportOutpostsCache.Sort((a, b) =>
                Find.WorldGrid.ApproxDistanceInTiles(hub, a.Tile).CompareTo(Find.WorldGrid.ApproxDistanceInTiles(hub, b.Tile)));
        }

        private void EnsureLogisticsConsumerInboundCached(WorldComponent_LogisticsManager manager)
        {
            int now = Find.TickManager.TicksGame;
            int logiGen = manager?.LogisticsNetDisplayGeneration ?? 0;
            if (logisticsConsumerCacheSel == SelOutpost
                && now - logisticsConsumerLinksCacheTick < LogisticsTabWorldScanIntervalTicks
                && logiGen == consumerInboundCachedLogisticsGen)
                return;
            logisticsConsumerLinksCacheTick = now;
            logisticsConsumerCacheSel = SelOutpost;
            consumerInboundCachedLogisticsGen = logiGen;
            logisticsConsumerInboundCache.Clear();
            consumerRowStrCache.Clear();
            foreach (WorldComponent_LogisticsManager.LogisticsLink l in manager.manualLinks)
            {
                if (l.destTile == SelOutpost.Tile && l.manualAssignment > 0.01f)
                {
                    logisticsConsumerInboundCache.Add(l);
                    var srcOp = Find.WorldObjects.WorldObjectAt<WorldObject>(l.sourceTile);
                    if (srcOp != null)
                    {
                        bool isCol = srcOp is MapParent && ((MapParent)srcOp).HasMap && srcOp.GetComponent<CompOutpostLogistics>() == null;
                        consumerRowStrCache[l.sourceTile] = new RowStrings
                        {
                            jump = "TSA_WD_JumpToOutpost".Translate(srcOp.LabelCap),
                            name = isCol
                                ? s_colonyPrefix + " (" + srcOp.LabelCap + ")"
                                : srcOp.LabelCap + " (" + srcOp.def.label + ")"
                        };
                    }
                }
            }
        }

        private void BuildRowStrings(WorldObject target, WorldComponent_LogisticsManager manager)
        {
            var rs = new RowStrings();
            rs.jump = "TSA_WD_JumpToOutpost".Translate(target.LabelCap);
            rs.name = target.LabelCap;
            var tl = target.GetComponent<CompOutpostLogistics>();
            rs.info = tl != null
                ? "TSA_OutpostInfoRow".Translate(tl.currentFood.ToString("F1"), tl.EffectiveMaxFood.ToString("F0")).ToString()
                : "";
            rowStrCache[target.Tile] = rs;
        }

        private void DrawIsolatedMessage(Rect listRect, bool producer)
        {
            if (SelOutpost == null) return;
            string typeLabel = SelOutpost.def != null
                ? SelOutpost.def.LabelCap.ToString()
                : SelOutpost.LabelCap;
            string msg = producer
                ? "TSA_Logistics_IsolatedProducer".Translate(typeLabel).ToString()
                : "TSA_Logistics_IsolatedConsumer".Translate(typeLabel).ToString();
            GUI.color = Color.gray;
            float h = Mathf.Min(listRect.height, Mathf.Max(48f, Text.CalcHeight(msg, listRect.width)));
            Widgets.Label(new Rect(listRect.x, listRect.y, listRect.width, h), msg);
            GUI.color = Color.white;
        }

        /// <param name="outgoingFromHubAll">Sum of all assignments from this hub (must match <see cref="WorldComponent_LogisticsManager.GetOutgoingManualSumForTile"/> for +/- budget).</param>
        private void DrawProducerInterface(Rect listRect, WorldComponent_LogisticsManager manager, float budget, float outgoingFromHubAll, List<WorldObject> supportOutpostsInRange)
        {
            if (supportOutpostsInRange == null || supportOutpostsInRange.Count == 0)
            {
                DrawIsolatedMessage(listRect, producer: true);
                return;
            }

            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, scrollViewHeight);
            Widgets.BeginScrollView(listRect, ref scrollPosition, viewRect);

            float vw = viewRect.width;
            GetProducerRowLayout(vw, out float nameX, out float nameW, out float netX, out float ctrlX, out float ctrlW);

            float innerY = 0f;
            int rowIndex = 0;
            foreach (var target in supportOutpostsInRange)
            {
                RowStrings rs;
                if (!rowStrCache.TryGetValue(target.Tile, out rs))
                {
                    BuildRowStrings(target, manager);
                    rowStrCache.TryGetValue(target.Tile, out rs);
                }

                Rect row = new Rect(0f, innerY, vw, ProducerRowHeight);
                if (rowIndex % 2 == 0) Widgets.DrawHighlight(row);
                Widgets.DrawHighlightIfMouseover(row);

                Rect jumpRect = new Rect(row.x, row.y, netX, row.height);
                if (Widgets.ButtonInvisible(jumpRect))
                {
                    CameraJumper.TryJump(target);
                    Find.WorldSelector.ClearSelection();
                    Find.WorldSelector.Select(target);
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    if (Find.MainTabsRoot.OpenTab != null) Find.MainTabsRoot.EscapeCurrentTab();
                }
                TooltipHandler.TipRegion(jumpRect, rs.jump);

                float contentY = innerY + (row.height / 2f);
                Rect iconRect = new Rect(row.x, contentY - (RowIconSize / 2f), RowIconSize, RowIconSize);
                Texture2D iconTex = target.def?.ExpandingIconTexture;
                if (iconTex != null)
                {
                    GUI.color = target.Faction?.Color ?? Color.white;
                    GUI.DrawTexture(iconRect, iconTex, ScaleMode.ScaleToFit);
                    GUI.color = Color.white;
                }

                Widgets.Label(new Rect(nameX, contentY - 21f, nameW, 24f), rs.name.Truncate(nameW - 4f));

                Text.Font = GameFont.Tiny;
                GUI.color = Color.gray;
                Rect infoRect = new Rect(nameX, contentY + 2f, nameW, 20f);
                Widgets.Label(infoRect, rs.info);
                TooltipHandler.TipRegion(infoRect, s_outpostInfoTip);
                GUI.color = Color.white;
                Text.Font = GameFont.Small;

                // Secondary: their total net (all sources). Primary editable value is the +/- assignment.
                float tNet = manager.GetLogisticsNetDailyForOutpost(target);
                Rect netRect = new Rect(netX, innerY, ProducerNetW, row.height);
                LabelAnchored(netRect, FormatNetColored(tNet), TextAnchor.MiddleCenter);
                TooltipHandler.TipRegion(netRect, s_totalNetTip);

                float btnY = innerY + (row.height / 2f) - 12f;
                float val = manager.GetOutgoingManualSumForDirectedLink(SelOutpost.Tile, target.Tile);
                string rowKey = target.Tile.tileId.ToString() + ":" + (target.Tile.Layer?.LayerID ?? 0);

                Rect minusRect = new Rect(ctrlX, btnY, AssignmentBtnW, 24f);
                if (WdDragSelectButtons.ButtonText(minusRect, "-", WdDragSelectButtons.Hash(rowKey, "minus")))
                    AdjustLink(manager, target.Tile, budget, true);

                val = manager.GetOutgoingManualSumForDirectedLink(SelOutpost.Tile, target.Tile);
                LabelAnchored(new Rect(ctrlX + AssignmentBtnW, innerY, AssignmentValueW, row.height),
                    val.ToString("F1"), TextAnchor.MiddleCenter);

                bool canAdd = (manager.GetOutgoingManualSumForTile(SelOutpost.Tile) + 0.05f) < budget;
                if (!canAdd) GUI.color = Color.gray;
                Rect plusRect = new Rect(ctrlX + ctrlW - AssignmentBtnW, btnY, AssignmentBtnW, 24f);
                if (WdDragSelectButtons.ButtonText(plusRect, "+", WdDragSelectButtons.Hash(rowKey, "plus"), active: canAdd) && canAdd)
                    AdjustLink(manager, target.Tile, budget, false);
                GUI.color = Color.white;

                innerY += ProducerRowStep;
                rowIndex++;
            }

            if (Event.current.type == EventType.Layout) scrollViewHeight = innerY;
            Widgets.EndScrollView();
        }

        private void DrawConsumerInterface(Rect listRect, WorldComponent_LogisticsManager manager, List<WorldComponent_LogisticsManager.LogisticsLink> sources)
        {
            if (sources == null || sources.Count == 0)
            {
                DrawIsolatedMessage(listRect, producer: false);
                return;
            }

            const float rowH = 40f;
            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, consumerScrollViewHeight);
            Widgets.BeginScrollView(listRect, ref consumerScrollPosition, viewRect);

            float vw = viewRect.width;
            const float iconColW = 40f;
            float amountColW = 60f;
            float nameColW = vw - iconColW - amountColW - 8f;

            float curY = 0f;
            int rowIndex = 0;
            foreach (var link in sources)
            {
                var srcOp = Find.WorldObjects.WorldObjectAt<WorldObject>(link.sourceTile);
                if (srcOp == null) continue;
                RowStrings crs;
                if (!consumerRowStrCache.TryGetValue(link.sourceTile, out crs))
                {
                    bool c = srcOp is MapParent && ((MapParent)srcOp).HasMap && srcOp.GetComponent<CompOutpostLogistics>() == null;
                    crs = new RowStrings
                    {
                        jump = "TSA_WD_JumpToOutpost".Translate(srcOp.LabelCap),
                        name = c ? s_colonyPrefix + " (" + srcOp.LabelCap + ")" : srcOp.LabelCap + " (" + srcOp.def.label + ")"
                    };
                    consumerRowStrCache[link.sourceTile] = crs;
                }

                float received = link.manualAssignment * manager.GetEfficiency(link.sourceTile, link.destTile);
                Rect rowRect = new Rect(0f, curY, vw, rowH);
                if (rowIndex % 2 == 0) Widgets.DrawHighlight(rowRect);
                Widgets.DrawHighlightIfMouseover(rowRect);

                if (Widgets.ButtonInvisible(rowRect))
                {
                    CameraJumper.TryJump(srcOp);
                    Find.WorldSelector.ClearSelection();
                    Find.WorldSelector.Select(srcOp);
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    if (Find.MainTabsRoot.OpenTab != null) Find.MainTabsRoot.EscapeCurrentTab();
                }
                TooltipHandler.TipRegion(rowRect, crs.jump);

                float contentY = curY + rowH / 2f;
                Rect iconRect = new Rect(4f, contentY - RowIconSize / 2f, RowIconSize, RowIconSize);
                bool isColony = srcOp is MapParent mp && mp.HasMap && srcOp.GetComponent<CompOutpostLogistics>() == null;
                if (isColony)
                    WorldDomination_UIUtils.DrawFactionIconWithColor(iconRect, srcOp.Faction ?? Faction.OfPlayer);
                else
                {
                    Texture2D iconTex = srcOp.def?.ExpandingIconTexture;
                    if (iconTex != null)
                    {
                        GUI.color = srcOp.Faction?.Color ?? Color.white;
                        GUI.DrawTexture(iconRect, iconTex, ScaleMode.ScaleToFit);
                        GUI.color = Color.white;
                    }
                }

                Rect nameRect = new Rect(iconColW + 4f, curY, nameColW - 8f, rowH);
                Rect amountRect = new Rect(iconColW + nameColW + 4f, curY, amountColW - 8f, rowH);
                LabelAnchored(nameRect, crs.name.Truncate(nameColW - 12f), TextAnchor.MiddleLeft);
                LabelAnchored(amountRect, received.ToString("F1").Colorize(Color.green), TextAnchor.MiddleLeft);

                curY += rowH;
                rowIndex++;
            }

            if (Event.current.type == EventType.Layout) consumerScrollViewHeight = curY;
            Widgets.EndScrollView();
        }

        private void AdjustLink(WorldComponent_LogisticsManager manager, int targetTile, float budget, bool isMinus)
        {
            manager.MergeDuplicateManualLinksFromSource(SelOutpost.Tile);
            float current = manager.GetOutgoingManualSumForDirectedLink(SelOutpost.Tile, targetTile);
            // Held Ctrl/Shift (×10 / ×100 / ×1000). GenUI alone uses IsDownEvent and often misses held modifiers.
            float step = WdQuantityUI.AdjustmentStep();

            float next;
            if (isMinus)
            {
                if (current <= 0f) return;
                float fractional = current - (float)Math.Floor(current);
                if (step <= 1.01f && fractional > 0.01f)
                    next = current - fractional;
                else
                    next = Mathf.Max(0f, current - step);
            }
            else
            {
                float remaining = budget - manager.GetOutgoingManualSumForTile(SelOutpost.Tile);
                if (remaining <= 0.001f) return;
                next = current + Mathf.Min(step, remaining);
            }
            SetLinkAssignment(manager, targetTile, next, budget);
        }

        /// <summary>Sets this hub→target assignment and switches to Manual. Does not rerun smart logistics.</summary>
        private void SetLinkAssignment(WorldComponent_LogisticsManager manager, int targetTile, float amount, float budget)
        {
            manager.MergeDuplicateManualLinksFromSource(SelOutpost.Tile);
            Logi.mode = LogisticsMode.Manual;
            var link = manager.manualLinks.FirstOrDefault(l => l.sourceTile == SelOutpost.Tile && l.destTile == targetTile);
            if (link == null)
            {
                link = new WorldComponent_LogisticsManager.LogisticsLink { sourceTile = SelOutpost.Tile, destTile = targetTile };
                manager.manualLinks.Add(link);
            }

            float otherOutgoing = manager.GetOutgoingManualSumForTile(SelOutpost.Tile) - manager.GetOutgoingManualSumForDirectedLink(SelOutpost.Tile, targetTile);
            float maxForLink = Mathf.Max(0f, budget - otherOutgoing);
            float clamped = (float)Math.Round(Mathf.Clamp(amount, 0f, maxForLink), 1);
            if (Mathf.Abs(link.manualAssignment - clamped) <= 0.0001f)
                return;

            link.manualAssignment = clamped;
            manager.ClampManualAssignmentsForSourceBudget(SelOutpost.Tile);
            // Do not NotifySmartLogisticsDirty / CompleteSmartLogisticsRefreshNow here — that rewrites
            // every smart hub and undoes nearby assignments while the player is editing this link.
            manager.NotifyManualLinksChanged();
            logisticsTabWorldScanTick = -999999;
            logisticsConsumerLinksCacheTick = -999999;
            logisticsTabScanCachedSel = null;
            logisticsConsumerCacheSel = null;
            int now = Find.TickManager.TicksGame;
            if (now - lastLogisticsRefreshTick >= LogisticsRefreshThrottleTicks)
            {
                lastLogisticsRefreshTick = now;
                SelOutpost?.RecomputeProductionRequirementCache();
                Window_OutpostOverview.InvalidateCache();
            }
        }
    }
}
