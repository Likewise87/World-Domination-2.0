using System;
using System.Collections.Generic;

using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>When the player defeats an NPC settlement, swap to ruins and offer "Establish Outpost?" after they leave the map. Patches are applied by <c>HarmonyLoader</c> (same assembly).</summary>
    [HarmonyPatch(typeof(SettlementDefeatUtility), "CheckDefeated")]
    public static class Patch_InterceptDefeat
    {
        [HarmonyPriority(Priority.High)]
        public static bool Prefix(Settlement factionBase)
        {
            if (factionBase == null || factionBase.Faction == null || factionBase.Faction.IsPlayer) return true;

            // Vanilla TickInterval calls CheckDefeated on every trader settlement; Map==null is free.
            Map map = factionBase.Map;
            if (map == null) return true;

            if (WorksitesExpandedCompat.ShouldSkipWdSettlementConquest(factionBase)) return true;

            // Player on-site wipe/conquer: attribute common-enemy quest even if outpost-after-conquest is off.
            bool outpostAfterConquest = WorldDominationMod.settings == null
                || WorldDominationMod.settings.outpostAfterConquestEnabled;
            if (!outpostAfterConquest)
            {
                QuestPart_WdTrackedSettlement tracked = WdCommonEnemySettlementQuestHelper.FindActiveTrackedPart();
                if (tracked == null || tracked.settlement != factionBase)
                    return true;
            }

            if (!SettlementDefeatUtility.IsDefeated(map, factionBase.Faction)) return true;

            WdCommonEnemySettlementQuestHelper.NotifyPlayerDefeatOfTrackedSettlement(factionBase);

            if (!outpostAfterConquest) return true;

            SettlementTier capturedTier = SettlementTier.T1;
            var viralComp = factionBase.GetComponent<CompViralSpread>();
            if (viralComp != null) capturedTier = viralComp.tier;

            DestroyedSettlement ruins = (DestroyedSettlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.DestroyedSettlement);
            ruins.Tile = factionBase.Tile;
            ruins.SetFaction(factionBase.Faction);
            map.info.parent = ruins;
            Find.WorldObjects.Add(ruins);

            OutpostDataTracker.Register(ruins.Tile, factionBase.Name ?? factionBase.LabelCap, capturedTier);

            factionBase.Destroy();
            return false;
        }
    }

    /// <summary>
    /// After leave: open the conquest opportunity menu only after DeinitAndRemoveMap finishes.
    /// Do not Destroy ruins here — that re-enters DeinitAndRemoveMap while the map is still in
    /// Game.maps if done from MapDeiniter.Deinit. Vanilla removes ruins via alsoRemoveWorldObject
    /// after this method returns; choice paths keep a defensive DestroyConquestRuinsAt.
    /// </summary>
    [HarmonyPatch(typeof(Game), nameof(Game.DeinitAndRemoveMap))]
    public static class Patch_DeinitAndRemoveMap_ConquestOpportunity
    {
        public static void Postfix(Map map)
        {
            if (WorldDominationMod.settings != null && !WorldDominationMod.settings.outpostAfterConquestEnabled) return;
            if (WorksitesExpandedCompat.IsWorksitesExpandedMap(map)) return;
            if (map?.Parent is DestroyedSettlement ruins && OutpostDataTracker.IsPending(ruins.Tile))
            {
                var data = OutpostDataTracker.GetData(ruins.Tile);

                var context = new ConquestOpportunityContext(
                    data.tile,
                    data.name,
                    ruins.ID,
                    data.tier,
                    ruins.Faction,
                    FindPlayerCaravanIdAt(ruins.Tile));

                OutpostDataTracker.MarkFinished(ruins.Tile);
                // One more deferral so CheckRemoveMapNow can finish its alsoRemoveWorldObject Destroy
                // before we push UI (OpenMenu itself does not destroy ruins).
                LongEventHandler.ExecuteWhenFinished(() => ConquestOpportunityUtility.OpenMenu(context));
            }
        }

        private static int FindPlayerCaravanIdAt(int tile)
        {
            var caravans = Find.WorldObjects?.Caravans;
            if (caravans == null) return -1;
            for (int i = 0; i < caravans.Count; i++)
            {
                var c = caravans[i];
                if (c != null && !c.Destroyed && c.IsPlayerControlled && c.Tile == tile)
                    return c.ID;
            }
            return -1;
        }
    }

    public class OutpostDataTracker : GameComponent
    {
        private Dictionary<int, PendingOutpostEntry> pending = new Dictionary<int, PendingOutpostEntry>();

        public OutpostDataTracker(Game game) : base() { }

        private static OutpostDataTracker GetTracker() => Current.Game?.GetComponent<OutpostDataTracker>();

        public static void Register(int tile, string name, SettlementTier tier)
        {
            var t = GetTracker();
            if (t != null) t.pending[tile] = new PendingOutpostEntry(tile, name, false, tier);
        }

        public static bool IsPending(int tile)
        {
            var t = GetTracker();
            return t != null && t.pending.TryGetValue(tile, out var e) && !e.used;
        }

        public static void MarkFinished(int tile)
        {
            var t = GetTracker();
            if (t != null && t.pending.TryGetValue(tile, out var e))
                t.pending[tile] = new PendingOutpostEntry(e.tile, e.name, true, e.tier);
        }

        public static (int tile, string name, SettlementTier tier) GetData(int tile)
        {
            var t = GetTracker();
            if (t == null || !t.pending.TryGetValue(tile, out var e))
                return (tile, "", SettlementTier.T1);
            return (e.tile, e.name, e.tier);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref pending, "outpostMVP_pending", LookMode.Value, LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && pending == null)
                pending = new Dictionary<int, PendingOutpostEntry>();
        }

        public struct PendingOutpostEntry : IExposable
        {
            public int tile;
            public string name;
            public bool used;
            public SettlementTier tier;

            public PendingOutpostEntry(int tile, string name, bool used, SettlementTier tier)
            {
                this.tile = tile;
                this.name = name ?? "";
                this.used = used;
                this.tier = tier;
            }

            public void ExposeData()
            {
                Scribe_Values.Look(ref tile, "tile");
                Scribe_Values.Look(ref name, "name", "");
                Scribe_Values.Look(ref used, "used");
                Scribe_Values.Look(ref tier, "tier", SettlementTier.T1);
            }
        }
    }

    public class ConquestOpportunityContext
    {
        public int tile;
        public string originalName;
        public int ruinsId;
        public SettlementTier tier;
        public Faction conqueredFaction;
        /// <summary>Player caravan that conquered this settlement (physical raids); -1 when none (simulated WD-outpost raids).</summary>
        public int conqueringCaravanId = -1;
        /// <summary>True when opened after a peaceful settlement purchase (not conquest ruins).</summary>
        public bool fromSettlementBuy;
        public bool recruitsDelivered;
        public bool consumed;

        public ConquestOpportunityContext(int tile, string originalName, int ruinsId, SettlementTier tier, Faction conqueredFaction, int conqueringCaravanId, bool fromSettlementBuy = false)
        {
            this.tile = tile;
            this.originalName = originalName ?? "";
            this.ruinsId = ruinsId;
            this.tier = tier;
            this.conqueredFaction = conqueredFaction;
            this.conqueringCaravanId = conqueringCaravanId;
            this.fromSettlementBuy = fromSettlementBuy;
        }

        public string Label => fromSettlementBuy
            ? "TSA_WD_BuyOutcome_Label".Translate()
            : "TSA_WD_ConquestLetter_Label".Translate();

        public string Text => fromSettlementBuy
            ? "TSA_WD_BuyOutcome_Text".Translate(originalName, (int)tier + 1)
            : "TSA_WD_ConquestLetter_Text".Translate(originalName, (int)tier + 1);

        public bool TileStillAvailable(out string reason)
        {
            return ConquestOpportunityUtility.IsConquestTileStillAvailable(tile, out reason);
        }

        public void ReopenMenuIfActive()
        {
            if (!consumed)
                ConquestOpportunityUtility.OpenMenu(this);
        }
    }

    public static class ConquestOpportunityUtility
    {
        public static void OpenMenu(ConquestOpportunityContext context)
        {
            if (context == null || context.consumed) return;
            if (!context.fromSettlementBuy
                && TryApplyExperimentalPlayerConquestRaze(context.tile, context.originalName, context.conqueredFaction, context.ruinsId))
            {
                context.consumed = true;
                return;
            }
            Find.WindowStack.Add(new Dialog_OutpostOpportunityChoices(context));
        }

        /// <summary>Tracks a simulated WD raid conquest without leaving permanent ruins on the world map.</summary>
        public static void RegisterSimulatedConquest(int tile, string originalName, SettlementTier tier)
        {
            OutpostDataTracker.Register(tile, originalName, tier);
        }

        public static void RegisterSimulatedConquestAndOpenMenu(int tile, string originalName, SettlementTier tier, Faction conqueredFaction, int conqueringCaravanId = -1)
        {
            RegisterSimulatedConquest(tile, originalName, tier);
            OpenMenu(new ConquestOpportunityContext(tile, originalName, -1, tier, conqueredFaction, conqueringCaravanId));
        }

        /// <summary>
        /// Experimental: roll the same raze chance as NPC raids. On success, skip conquest menu and leave timed WD ruins.
        /// </summary>
        public static bool TryApplyExperimentalPlayerConquestRaze(int tile, string originalName, Faction conqueredFaction, int ruinsId = -1)
        {
            var seth = WorldDominationMod.settings;
            if (seth == null || !seth.experimentalPlayerConquestRaze) return false;
            float razeChance = Mathf.Clamp01(seth.razeChance);
            if (Rand.Value >= razeChance) return false;

            DestroyConquestRuinsAt(tile, ruinsId);
            OutpostDataTracker.MarkFinished(tile);
            WorldObject_WdSettlementRuin.Spawn(tile, originalName, conqueredFaction);

            Messages.Message(
                "TSA_WD_PlayerConquestRazed_Message".Translate(originalName ?? "Settlement"),
                new GlobalTargetInfo(tile),
                MessageTypeDefOf.NeutralEvent);

            if (seth.notifySettlementRazed && WD_NotifyProximity.IsWithinPlayerNotificationRadius(tile))
            {
                Find.LetterStack.ReceiveLetter(
                    "TSA_WD_Letter_PlayerConquestRazed_Label".Translate(),
                    "TSA_WD_Letter_PlayerConquestRazed_Text".Translate(originalName ?? "Settlement"),
                    LetterDefOf.NeutralEvent,
                    new GlobalTargetInfo(tile));
            }
            return true;
        }

        public static bool IsConquestTileStillAvailable(int tile, out string reason)
        {
            reason = null;
            if (Find.WorldGrid == null || tile < 0 || tile >= Find.WorldGrid.TilesCount)
            {
                reason = "Invalid tile.";
                return false;
            }

            var all = Find.WorldObjects?.AllWorldObjects;
            if (all == null) return true;
            for (int i = 0; i < all.Count; i++)
            {
                WorldObject wo = all[i];
                if (wo == null || wo.Destroyed || wo.Tile != tile) continue;
                if (wo is DestroyedSettlement) continue;
                if (wo is Caravan) continue;
                if (wo is WorldObject_Traveler) continue;
                if (wo is WorldObject_WdSettlementRuin
                    || wo is Settlement
                    || wo is WorldObject_WD_Outpost
                    || wo is MapParent)
                {
                    reason = "TSA_WD_Conquest_RuinsGone".Translate().ToString();
                    return false;
                }
            }
            return true;
        }

        public static void DestroyConquestRuinsAt(int tile, int preferredRuinsId = -1)
        {
            var all = Find.WorldObjects?.AllWorldObjects;
            if (all == null) return;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                WorldObject wo = all[i];
                if (wo == null || wo.Destroyed) continue;
                bool preferred = preferredRuinsId >= 0 && wo.ID == preferredRuinsId;
                bool sameTileRuins = wo.Tile == tile && wo is DestroyedSettlement;
                if (preferred || sameTileRuins)
                    wo.Destroy();
            }
        }
    }

    public class Dialog_OutpostOpportunityChoices : Window
    {
        private readonly ConquestOpportunityContext context;
        private readonly bool allowLeave;

        public override Vector2 InitialSize => allowLeave ? new Vector2(560f, 420f) : new Vector2(560f, 360f);

        public Dialog_OutpostOpportunityChoices(ConquestOpportunityContext context, bool allowLeave = true)
        {
            this.context = context;
            this.allowLeave = allowLeave;
            doCloseX = false;
            doCloseButton = false;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            bool guiEnabledBefore = GUI.enabled;
            GUI.enabled = true;

            if (context == null || context.consumed)
            {
                Close();
                GUI.enabled = guiEnabledBefore;
                return;
            }

            const float boxPad = Outpost_Dialog_UI.OutcomeBoxPad;
            const float buttonH = 38f;
            // Extra air above the small Leave control so it reads as a dismiss, not a fourth option.
            const float leaveTopGap = 28f;
            float leaveReserve = allowLeave ? leaveTopGap + CloseButSize.y : 0f;

            float y = 0f;
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(new Rect(0f, y, inRect.width, Outpost_Dialog_UI.DialogTitleHeight), context.Label);
            y += Outpost_Dialog_UI.DialogTitleRowAdvance;

            Text.Font = GameFont.Small;
            float innerTextW = inRect.width - boxPad * 2f;
            float textH = Mathf.Max(SmallLabelFloor(), Text.CalcHeight(context.Text, innerTextW));
            float boxH = boxPad * 2f + textH;
            Rect boxRect = new Rect(0f, y, inRect.width, boxH);
            Outpost_Dialog_UI.DrawOutcomeBox(boxRect);
            Rect textRect = boxRect.ContractedBy(boxPad);
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(textRect, context.Text);
            y += boxH + Outpost_Dialog_UI.OutcomeBoxGap;

            bool tileAvailable = context.TileStillAvailable(out string tileUnavailableReason);
            float buttonW = inRect.width;
            float optionsBottom = inRect.height - leaveReserve;

            DrawOptionButton(ref y, buttonW, buttonH,
                context.fromSettlementBuy
                    ? "TSA_WD_Buy_OptConvertOutpost".Translate()
                    : "TSA_WD_Conquest_OptEstablish".Translate(),
                tileAvailable, tileUnavailableReason, () =>
            {
                Close();
                Find.WindowStack.Add(new Dialog_OutpostSelection(context.tile, context.originalName, context.ruinsId, context.tier, context));
            });

            bool canRecruit = tileAvailable && !context.recruitsDelivered;
            string recruitDisabled = null;
            if (!tileAvailable) recruitDisabled = tileUnavailableReason;
            else if (context.recruitsDelivered) recruitDisabled = "TSA_WD_Conquest_RecruitAlready".Translate().ToString();
            DrawOptionButton(ref y, buttonW, buttonH,
                context.fromSettlementBuy
                    ? "TSA_WD_Buy_OptRecruit".Translate()
                    : "TSA_WD_Conquest_OptRecruit".Translate(),
                canRecruit, recruitDisabled, () =>
            {
                WD_Outpost_ConquestChoices.DeliverRecruits(context.tile, context.tier, context.conqueringCaravanId, context.conqueredFaction, context.ruinsId);
                context.recruitsDelivered = true;
                context.consumed = true;
                Close();
            });

            var giftFactions = WD_Outpost_ConquestChoices.GetEligibleGiftFactions(context.conqueredFaction);
            bool canGift = tileAvailable && giftFactions.Count > 0;
            string giftDisabled = null;
            if (!tileAvailable) giftDisabled = tileUnavailableReason;
            else if (giftFactions.Count == 0) giftDisabled = "TSA_WD_Conquest_NoEligibleAllies".Translate().ToString();
            DrawOptionButton(ref y, buttonW, buttonH,
                context.fromSettlementBuy
                    ? "TSA_WD_Buy_OptGiveAlly".Translate()
                    : "TSA_WD_Conquest_OptGiveAlly".Translate(),
                canGift, giftDisabled, () =>
            {
                Close();
                Find.WindowStack.Add(new Dialog_ConquestAllyGift(context.tile, context.ruinsId, context.tier, context.conqueredFaction, context));
            });

            if (allowLeave)
            {
                Rect leaveRect = new Rect(
                    (inRect.width - CloseButSize.x) * 0.5f,
                    Mathf.Max(y + leaveTopGap, optionsBottom - CloseButSize.y),
                    CloseButSize.x,
                    CloseButSize.y);
                if (Widgets.ButtonText(leaveRect, "TSA_WD_Conquest_OptLeave".Translate()))
                {
                    ConquestOpportunityUtility.DestroyConquestRuinsAt(context.tile, context.ruinsId);
                    context.consumed = true;
                    Close();
                }
            }

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.enabled = guiEnabledBefore;
        }

        private static float SmallLabelFloor() => Mathf.Max(24f, Text.LineHeightOf(GameFont.Small));

        private static void DrawOptionButton(ref float y, float width, float height, string label, bool enabled, string disabledReason, Action action)
        {
            Rect rect = new Rect(0f, y, width, height);
            if (Widgets.ButtonText(rect, label))
            {
                if (enabled)
                    action?.Invoke();
                else if (!disabledReason.NullOrEmpty())
                    Messages.Message(disabledReason, MessageTypeDefOf.RejectInput, false);
            }
            if (!enabled && !disabledReason.NullOrEmpty())
                TooltipHandler.TipRegion(rect, disabledReason);
            y += height + 8f;
        }
    }
}
