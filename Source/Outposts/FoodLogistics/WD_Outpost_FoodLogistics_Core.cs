using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
namespace TSA_WorldDomination
{
    public enum LogisticsMode
    {
        Manual = 0,
        /// <summary>Legacy save/UI value — normalized to <see cref="Smart"/> on load.</summary>
        Equal = 1,
        /// <summary>Legacy save/UI value — normalized to <see cref="Smart"/> on load.</summary>
        FullyFeed = 2,
        /// <summary>Prioritizes hungriest outposts, then shares remainder.</summary>
        Smart = 3,
        /// <summary>Legacy scribe name for <see cref="Smart"/> (same int) — required for old saves.</summary>
        BiggestDeficit = Smart,
        AllToColony = 4,
    }

    public static class LogisticsModeUtil
    {
        /// <summary>True for Smart (incl. legacy Equal/FullyFeed before normalize) and AllToColony.</summary>
        public static bool UsesAutomaticAssignment(LogisticsMode mode) => mode != LogisticsMode.Manual;

        /// <summary>UI / inspect: Smart selected when mode is Smart or any legacy smart algorithm.</summary>
        public static bool IsSmartMode(LogisticsMode mode) =>
            mode == LogisticsMode.Smart
            || mode == LogisticsMode.BiggestDeficit
            || mode == LogisticsMode.Equal
            || mode == LogisticsMode.FullyFeed;

        /// <summary>Map removed algorithms (incl. legacy AllToColony) to Smart so existing saves keep working.</summary>
        public static LogisticsMode Normalize(LogisticsMode mode)
        {
            if (mode == LogisticsMode.Equal || mode == LogisticsMode.FullyFeed || mode == LogisticsMode.BiggestDeficit
                || mode == LogisticsMode.AllToColony)
                return LogisticsMode.Smart;
            return mode;
        }

        public static string InspectModeLabelKey(LogisticsMode mode)
        {
            mode = Normalize(mode);
            return mode == LogisticsMode.Manual ? null : "TSA_LogisticsMode_" + mode;
        }
    }

    // FIXED: Resources defined in a startup constructor for proper shader initialization
    public static class LogisticsResources
    {
        public static Material LogisticsGreen => WorldOverlayLineMaterials.LogisticsGreen;
        public static Material LogisticsDarkCyan => WorldOverlayLineMaterials.LogisticsDarkCyan;
    }

    public class CompProperties_OutpostLogistics : WorldObjectCompProperties
    {
        public CompProperties_OutpostLogistics() => this.compClass = typeof(CompOutpostLogistics);
    }

    public class CompOutpostLogistics : WorldObjectComp
    {
        public float currentFood = 50f;
        /// <summary>Global base max virtual food per outpost (from mod settings).</summary>
        public static float MaxFood => WorldDominationMod.settings?.maxFoodPerOutpost ?? WorldDominationSettings.DefMaxFoodPerOutpost;
        public float lastNetChange = 0f;
        public LogisticsMode mode = LogisticsMode.Manual;

        public bool produceForLogistics = false;

        private WorldComponent_LogisticsManager cachedLogiManager;
        private string cachedLogiInspect;
        private int cachedLogiInspectTick = -999;

        /// <summary>Effective max food for the given world object (base max + outpost upgrade bonus).</summary>
        public static float GetEffectiveMaxFoodFor(WorldObject obj)
        {
            float baseMax = MaxFood;
            if (obj is WorldObject_WD_Outpost wd)
                return Mathf.Max(0f, baseMax + wd.GetBuiltUpgradeFoodStorageMaxBonus());
            return Mathf.Max(0f, baseMax);
        }

        /// <summary>Effective max food for this logistics node's parent object.</summary>
        public float EffectiveMaxFood => GetEffectiveMaxFoodFor(parent as WorldObject);

        public override void Initialize(WorldObjectCompProperties props)
        {
            base.Initialize(props);
            if (Outpost_Production_Utils.IsFoodProducerOutpost(parent.def))
            {
                this.mode = LogisticsMode.Smart;
                this.produceForLogistics = true;
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref currentFood, "currentFood", 50f);
            Scribe_Values.Look(ref lastNetChange, "lastNetChange", 0f);
            Scribe_Values.Look(ref mode, "mode", LogisticsMode.Manual);
            Scribe_Values.Look(ref produceForLogistics, "produceForLogistics", false);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
                mode = LogisticsModeUtil.Normalize(mode);
        }

        public override string CompInspectStringExtra()
        {
            if (!WorldDominationMod.settings.foodLogisticsActive) return null;
            int tick = Find.TickManager.TicksGame;
            if (tick - cachedLogiInspectTick < 60 && cachedLogiInspect != null)
                return cachedLogiInspect;
            cachedLogiInspectTick = tick;

            float netForDisplay = lastNetChange;
            if (cachedLogiManager == null)
                cachedLogiManager = Find.World?.GetComponent<WorldComponent_LogisticsManager>();
            var logiManager = cachedLogiManager;
            if (logiManager != null && parent is WorldObject wo)
                netForDisplay = logiManager.GetLogisticsNetDailyForOutpost(wo);

            string balanceStr = netForDisplay >= 0 ? $"+{netForDisplay:F1}" : $"{netForDisplay:F1}";
            Color col = netForDisplay > 0.1f ? Color.green : (netForDisplay < -0.1f ? Color.red : Color.yellow);
            string modeKey = LogisticsModeUtil.InspectModeLabelKey(mode);
            string modeStr = string.IsNullOrEmpty(modeKey) ? "" : $" [{modeKey.Translate()}]";

            cachedLogiInspect = "TSA_FoodSupplyInspect".Translate(
                currentFood.ToString("F1"),
                EffectiveMaxFood.ToString("F0"),
                balanceStr.Colorize(col)
            ) + modeStr;
            return cachedLogiInspect;
        }

        public override void PostDrawExtraSelectionOverlays()
        {
            base.PostDrawExtraSelectionOverlays();

            if (parent is WorldObject_WD_Outpost outpost)
            {
                if (cachedLogiManager == null)
                    cachedLogiManager = Find.World.GetComponent<WorldComponent_LogisticsManager>();
                var manager = cachedLogiManager;
                if (manager == null) return;

                bool isSelected = Find.WorldSelector.IsSelected(outpost);

                if (isSelected
                    && !Outpost_Warehouse_Delivery.ShouldHideFoodLogisticsOverlay
                    && !RadiusFillHoverController.IsActive
                    && !WD_WorldMapZoomUtil.IsZoomedTooFarOut(WD_WorldMapZoomUtil.TravelerPathHideAltitudePercent))
                {
                    int hubTile = outpost.Tile;
                    float lift = GenDraw_WorldLineSmooth.GetPathLineLift();
                    List<int> outgoing = manager.GetOutgoingDestTilesForDraw(hubTile);
                    for (int i = 0; i < outgoing.Count; i++)
                        DrawLogisticsLine(hubTile, outgoing[i], LogisticsResources.LogisticsGreen, lift);
                    List<int> incoming = manager.GetIncomingSourceTilesForDraw(hubTile);
                    for (int i = 0; i < incoming.Count; i++)
                        DrawLogisticsLine(incoming[i], hubTile, LogisticsResources.LogisticsDarkCyan, lift);
                }
            }
        }

        private void DrawLogisticsLine(int startTile, int endTile, Material mat, float lift)
        {
            // Adaptive segments (-1): short slerp pieces stay near the surface with LogisticsLift.
            // segments:1 was a food-rework regression — one long chord dives under hills and loses depth.
            GenDraw_WorldLineSmooth.DrawSmoothWorldLine(
                startTile, endTile, Find.WorldGrid, mat, 1f, lift);
        }

        /// <summary>
        /// Legacy hook for VOE-style outposts with a physical inventory. In this mod, TSA outposts have
        /// no map/inventory, so conversion is driven from caravans instead via ConvertCaravanFoodToVirtualFood.
        /// </summary>
        public void ConvertInventoryToVirtualFood()
        {
            Messages.Message("TSA_WD_NoFoodInInventory".Translate(), MessageTypeDefOf.RejectInput, false);
        }

        /// <summary>Adds nutrition to <paramref name="comp"/>.currentFood, clamped by per-outpost effective max. Returns amount applied.</summary>
        public static float AddVirtualFoodNutrition(CompOutpostLogistics comp, float nutrition)
        {
            if (comp == null || nutrition <= 0f) return 0f;
            float availableCapacity = Mathf.Max(0f, comp.EffectiveMaxFood - comp.currentFood);
            if (availableCapacity <= 0f) return 0f;
            float used = Mathf.Min(availableCapacity, nutrition);
            comp.currentFood += used;
            return used;
        }

        /// <summary>
        /// Sums nutrition from nutrition-giving ingestibles in caravan inventory and lists those things
        /// (same rules as <see cref="ConvertCaravanFoodToVirtualFood"/>).
        /// </summary>
        private static float SumEdibleCaravanInventoryNutrition(Caravan caravan, out List<Thing> edibleItems)
        {
            edibleItems = new List<Thing>();
            float totalNutrition = 0f;
            if (caravan == null) return 0f;
            foreach (Thing thing in CaravanInventoryUtility.AllInventoryItems(caravan))
            {
                if (thing == null || thing.Destroyed) continue;
                if (thing.def?.ingestible == null || !thing.def.IsNutritionGivingIngestible) continue;
                edibleItems.Add(thing);
                float perUnit = thing.GetStatValue(StatDefOf.Nutrition, true);
                if (perUnit <= 0f) continue;
                totalNutrition += perUnit * thing.stackCount;
            }
            return totalNutrition;
        }

        /// <summary>
        /// When a caravan is dissolved into an outpost (last humanlike transferred): credit virtual food
        /// from edible inventory, then destroy counted edible stacks. Animals are stored by the outpost caller.
        /// Clamps like <see cref="ConvertCaravanFoodToVirtualFood"/>.
        /// </summary>
        public static void TryDissolveCaravanIntoOutpostVirtualFood(Caravan caravan, WorldObject_WD_Outpost outpost, bool notifyPlayer)
        {
            var comp = outpost?.GetComponent<CompOutpostLogistics>();
            if (comp == null || caravan == null || caravan.Destroyed) return;

            float fromItems = SumEdibleCaravanInventoryNutrition(caravan, out List<Thing> edibleItems);
            float totalNutrition = fromItems;
            if (totalNutrition <= 0f) return;

            float usedNutrition = AddVirtualFoodNutrition(comp, totalNutrition);
            if (usedNutrition <= 0f) return;

            if (fromItems > 0f && edibleItems.Count > 0)
                WDVerbose.Msg($"Outpost dissolve: destroying {edibleItems.Count} edible inventory stack(s), up to {fromItems:F1} nutrition credited (capped by capacity)");

            WDVerbose.Msg($"Outpost dissolve: virtual food +{usedNutrition:F1} (inventory {fromItems:F1}); pool now {comp.currentFood:F1}/{comp.EffectiveMaxFood:F0}");

            for (int i = 0; i < edibleItems.Count; i++)
            {
                Thing thing = edibleItems[i];
                if (thing == null || thing.Destroyed) continue;
                thing.Destroy(DestroyMode.Vanish);
            }

            if (notifyPlayer && usedNutrition > 0f)
            {
                Messages.Message("TSA_FoodSupplyConvertResult".Translate(
                    comp.currentFood.ToString("F1"),
                    comp.EffectiveMaxFood.ToString("F0")),
                    MessageTypeDefOf.TaskCompletion,
                    false);
            }
        }

        /// <summary>
        /// Convert all nutrition-bearing items in the given caravan's inventory into this outpost's
        /// virtual food pool. Items with ingestible nutrition are destroyed; 1 nutrition becomes 1 unit
        /// of virtual food, clamped by effective max for this outpost. Non-food items are left untouched.
        /// </summary>
        public static void ConvertCaravanFoodToVirtualFood(Caravan caravan, WorldObject_WD_Outpost outpost, CompOutpostLogistics comp)
        {
            if (caravan == null || outpost == null || comp == null) return;

            float availableCapacity = Mathf.Max(0f, comp.EffectiveMaxFood - comp.currentFood);
            if (availableCapacity <= 0f)
            {
                Messages.Message("TSA_WD_NoFoodInInventory".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            float totalNutrition = SumEdibleCaravanInventoryNutrition(caravan, out var edibleItems);
            if (totalNutrition <= 0f || edibleItems.Count == 0)
            {
                Messages.Message("TSA_WD_NoFoodInInventory".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            float usedNutrition = Mathf.Min(availableCapacity, totalNutrition);
            comp.currentFood += usedNutrition;

            // Destroy all edible stacks we counted; this may waste some nutrition if we exceeded capacity,
            // but keeps the implementation simple and predictable: all caravan food is sacrificed.
            foreach (var thing in edibleItems)
            {
                if (thing.Destroyed) continue;
                thing.Destroy(DestroyMode.Vanish);
            }

            if (usedNutrition > 0f)
            {
                Messages.Message("TSA_FoodSupplyConvertResult".Translate(
                    comp.currentFood.ToString("F1"),
                    comp.EffectiveMaxFood.ToString("F0")),
                    MessageTypeDefOf.TaskCompletion,
                    false);
            }
        }

        /// <summary>
        /// Convert nutrition-bearing items from a loose list (e.g. transport pod cargo) into the
        /// outpost's virtual food pool. Edible items are removed from <paramref name="items"/>
        /// and destroyed; non-food items are left in the list untouched.
        /// Returns total nutrition added.
        /// </summary>
        public static float ConvertLooseItemsToVirtualFood(List<Thing> items, CompOutpostLogistics comp)
        {
            if (items == null || comp == null) return 0f;

            float availableCapacity = Mathf.Max(0f, comp.EffectiveMaxFood - comp.currentFood);
            if (availableCapacity <= 0f) return 0f;

            float totalNutrition = 0f;
            var edibleItems = new List<Thing>();

            for (int i = items.Count - 1; i >= 0; i--)
            {
                var thing = items[i];
                if (thing == null || thing.Destroyed) continue;
                if (thing.def?.ingestible == null || !thing.def.IsNutritionGivingIngestible) continue;

                float perUnit = thing.GetStatValue(StatDefOf.Nutrition, true);
                if (perUnit <= 0f) continue;

                totalNutrition += perUnit * thing.stackCount;
                edibleItems.Add(thing);
            }

            if (totalNutrition <= 0f) return 0f;

            float usedNutrition = Mathf.Min(availableCapacity, totalNutrition);
            comp.currentFood += usedNutrition;

            foreach (var thing in edibleItems)
            {
                if (thing.Destroyed) continue;
                items.Remove(thing);
                thing.Destroy(DestroyMode.Vanish);
            }

            return usedNutrition;
        }
    }

    [StaticConstructorOnStartup]
    public static class OutpostLogisticsGizmos
    {
        private static Texture2D cachedConvertFoodIcon;

        public static IEnumerable<Gizmo> GetGizmos(WorldObject outpost)
        {
            if (outpost == null || outpost.Faction != Faction.OfPlayer) yield break;
            if (!WorldDominationMod.settings.foodLogisticsActive) yield break;

            var logiComp = outpost.GetComponent<CompOutpostLogistics>();
            if (logiComp == null) yield break;

            if (!(outpost is WorldObject_WD_Outpost) && logiComp.currentFood < logiComp.EffectiveMaxFood)
            {
                yield return new Command_Action
                {
                    defaultLabel = "TSA_WD_ConvertFood".Translate(),
                    defaultDesc = "TSA_WD_ConvertFoodDesc".Translate(),
                    icon = cachedConvertFoodIcon ??= ContentFinder<Texture2D>.Get("UI/Commands/ConvertFood", false) ?? TexCommand.Replant,
                    action = delegate { logiComp.ConvertInventoryToVirtualFood(); }
                };
            }
        }
    }

    public class WorldComponent_LogisticsManager : WorldComponent
    {
        public class LogisticsLink : IExposable
        {
            public int sourceTile;
            public int destTile;
            public float manualAssignment;
            public void ExposeData()
            {
                Scribe_Values.Look(ref sourceTile, "sourceTile");
                Scribe_Values.Look(ref destTile, "destTile");
                Scribe_Values.Look(ref manualAssignment, "manualAssignment");
            }
        }

        public List<LogisticsLink> manualLinks = new List<LogisticsLink>();
        private int nextTick = -1;
        private const int CheckInterval = 30000;

        /// <summary>When true, all Smart hubs are rewritten atomically via <see cref="CompleteSmartLogisticsRefreshNow"/>.</summary>
        private bool smartLogisticsDirty = true;
        private long lastSmartCompletedSoftFingerprint = long.MinValue;
        /// <summary>True after an explicit <see cref="NotifySmartLogisticsDirty"/> until a complete finishes; DevMode uses this to detect heal-only refreshes.</summary>
        private bool smartDirtyFromExplicitNotify;

        /// <summary>Per world tick: set when a full Smart batch runs (for food-pulse perf log).</summary>
        private int pulseDiag_smartStaggerHubsThisTick;
        private int pulseDiag_smartImmediateHubsLastBatch;
        private int pulseDiag_smartImmediateLastGameTick = -1;
        /// <summary>DevMode: last pulse only compared fingerprints (no Smart execute).</summary>
        public bool LastPulseSmartCompareOnly { get; private set; }
        /// <summary>DevMode: last Smart complete was triggered by fingerprint heal without prior explicit dirty.</summary>
        public bool LastSmartCompleteWasFingerprintHeal { get; private set; }

        private readonly Dictionary<int, List<(WorldObject Obj, CompOutpostLogistics Logi)>> pulseNodesByTile = new Dictionary<int, List<(WorldObject, CompOutpostLogistics)>>();
        private readonly Dictionary<CompOutpostLogistics, float> pulsePendingChanges = new Dictionary<CompOutpostLogistics, float>();

        /// <summary>Colony tiles: at most one full-world scan per Unity frame.</summary>
        private HashSet<int> colonyTilesCache;
        private int colonyTilesCacheFrame = -1;

        private int manualLinksMutationVersion;
        /// <summary>While &gt; 0, <see cref="SetAssignment"/> coalesces <see cref="NotifyManualLinksChanged"/> into one bump at <see cref="EndManualLinkNotifySuppress"/> (smart batch).</summary>
        private int manualLinkNotifySuppressDepth;
        private bool manualLinkNotifyPendingAfterSuppress;
        private int manualLinkAggBuiltVersion = -1;
        private Dictionary<int, float> manualOutgoingSumBySourceTile;
        private Dictionary<int, float> manualIncomingWeightedSumByDestTile;
        /// <summary>Draw index: source tile → dest tiles with assignment &gt; 0.01 (same version as aggregates).</summary>
        private Dictionary<int, List<int>> manualOutgoingDestsBySourceTile;
        /// <summary>Draw index: dest tile → source tiles with assignment &gt; 0.01.</summary>
        private Dictionary<int, List<int>> manualIncomingSourcesByDestTile;
        private static readonly List<int> EmptyDrawTileList = new List<int>(0);

        private int logisticsCacheGeneration;
        private int logisticsCacheBuiltGeneration = -1;

        private struct LogisticsCacheEntry
        {
            public float DailyProduction;
            public float DailyDemand;
            public float NetLogistics;
        }

        private Dictionary<int, LogisticsCacheEntry> logisticsCacheByObjectId;

        private bool playerLogisticsRegistryDirty = true;
        private List<(WorldObject Obj, CompOutpostLogistics Logi)> playerLogisticsNodesOrdered;
        private List<WorldObject> playerLogisticsColoniesOrdered;

        private bool settingsWatchFoodActive;
        private float settingsWatchFoodCons;
        private float settingsWatchFoodProdBase;
        private int settingsWatchMaxRange;
        private float settingsWatchTileFloor;
        private bool foodSettingsSnapshotInitialized;

        public WorldComponent_LogisticsManager(World world) : base(world) { }

        /// <summary>
        /// Increments when anything that affects displayed <b>daily net</b> may change: manual/smart links, topology, pawns/skills (via <see cref="BumpLogisticsDataVersion"/>), food settings snapshot.
        /// World map net labels compare this to avoid calling <see cref="GetLogisticsNetDailyForOutpost"/> every frame. (Virtual <c>currentFood</c> is refreshed separately.)
        /// </summary>
        public int LogisticsNetDisplayGeneration => logisticsCacheGeneration;

        /// <summary>Fills <paramref name="into"/> with player map colonies (no logistics comp), excluding <paramref name="hub"/>.</summary>
        public void GetLogisticsColoniesExcludingHub(WorldObject hub, List<WorldObject> into)
        {
            into.Clear();
            RebuildPlayerLogisticsRegistryIfNeeded();
            for (int i = 0; i < playerLogisticsColoniesOrdered.Count; i++)
            {
                var c = playerLogisticsColoniesOrdered[i];
                if (c != hub) into.Add(c);
            }
        }

        /// <summary>Non–food-producer logistics nodes in range of hub with cached net (registry scan, no full-world LINQ).</summary>
        public void GetSupportLogisticsTargetsWithNet(WorldObject hub, WorldDominationSettings s, List<(WorldObject Obj, float Net)> into)
        {
            into.Clear();
            if (hub == null || s == null) return;
            RebuildPlayerLogisticsRegistryIfNeeded();
            EnsureLogisticsCacheFresh();
            float maxR = s.maxLogisticsRange;
            int hubTile = hub.Tile;
            for (int i = 0; i < playerLogisticsNodesOrdered.Count; i++)
            {
                var wo = playerLogisticsNodesOrdered[i].Obj;
                if (wo == hub) continue;
                if (Outpost_Production_Utils.IsFoodProducerOutpost(wo.def)) continue;
                if (Find.WorldGrid.ApproxDistanceInTiles(hubTile, wo.Tile) > maxR) continue;
                into.Add((wo, GetLogisticsNetDailyForOutpost(wo)));
            }
        }

        /// <summary>Immediate version bump (cleared outgoing links before smart recomputes nets). Not deferred.</summary>
        public void BumpManualLinkStructureVersionImmediate()
        {
            manualLinksMutationVersion++;
            logisticsCacheGeneration++;
        }

        /// <summary>Invalidate cached aggregates after direct edits to <see cref="manualLinks"/> (e.g. logistics UI).</summary>
        /// <remarks>
        /// While <see cref="BeginManualLinkNotifySuppress"/> is active, bumps are deferred to
        /// <see cref="EndManualLinkNotifySuppress"/> so smart-batch passes only rebuild once.
        /// </remarks>
        public void NotifyManualLinksChanged()
        {
            if (manualLinkNotifySuppressDepth > 0)
            {
                manualLinkNotifyPendingAfterSuppress = true;
                // Keep aggregates/cache honest mid-batch so Smart sees live nets after clears/assigns.
                manualLinkAggBuiltVersion = -1;
                logisticsCacheBuiltGeneration = -1;
                return;
            }
            BumpManualLinkStructureVersionImmediate();
        }

        /// <summary>Force rebuild of link aggregates and numeric net cache on next read (Smart mid-pass).</summary>
        public void InvalidateLogisticsNumericCaches()
        {
            manualLinkAggBuiltVersion = -1;
            logisticsCacheBuiltGeneration = -1;
        }

        public void BeginManualLinkNotifySuppress()
        {
            manualLinkNotifySuppressDepth++;
        }

        public void EndManualLinkNotifySuppress()
        {
            manualLinkNotifySuppressDepth--;
            if (manualLinkNotifySuppressDepth < 0) manualLinkNotifySuppressDepth = 0;
            if (manualLinkNotifySuppressDepth == 0 && manualLinkNotifyPendingAfterSuppress)
            {
                manualLinkNotifyPendingAfterSuppress = false;
                BumpManualLinkStructureVersionImmediate();
            }
        }

        /// <summary>Bump logistics numeric cache when topology, pawns, upgrades, or food settings change (link rows use <see cref="NotifyManualLinksChanged"/>).</summary>
        public void BumpLogisticsDataVersion()
        {
            logisticsCacheGeneration++;
        }

        /// <summary>Cache + Smart dirty when food logistics inputs change (pawns, production, settings that affect surplus/demand).</summary>
        public void NotifyFoodLogisticsInputsChanged()
        {
            BumpLogisticsDataVersion();
            NotifySmartLogisticsDirty();
        }

        /// <summary>Player logistics node registry must rebuild; also bumps cache and colony tile hash.</summary>
        public void NotifyLogisticsTopologyChanged()
        {
            playerLogisticsRegistryDirty = true;
            InvalidateColonyTilesCache();
            logisticsCacheGeneration++;
            NotifySmartLogisticsDirty();
        }

        /// <summary>Player settlement tiles (MapParent with map, no <see cref="CompOutpostLogistics"/>). Cached per frame.</summary>
        public HashSet<int> GetColonyTilesCached()
        {
            int f = Time.frameCount;
            if (colonyTilesCache != null && colonyTilesCacheFrame == f)
                return colonyTilesCache;
            colonyTilesCacheFrame = f;
            if (colonyTilesCache == null)
                colonyTilesCache = new HashSet<int>();
            else
                colonyTilesCache.Clear();
            if (Find.WorldObjects == null) return colonyTilesCache;
            var colonies = GetCachedPlayerLogisticsColonies();
            for (int i = 0; i < colonies.Count; i++)
                colonyTilesCache.Add(colonies[i].Tile);
            return colonyTilesCache;
        }

        public void InvalidateColonyTilesCache()
        {
            colonyTilesCache = null;
            colonyTilesCacheFrame = -1;
        }

        private void EnsureManualLinkAggregatesFresh()
        {
            if (manualLinkAggBuiltVersion == manualLinksMutationVersion && manualOutgoingSumBySourceTile != null)
                return;
            manualLinkAggBuiltVersion = manualLinksMutationVersion;
            if (manualOutgoingSumBySourceTile == null)
                manualOutgoingSumBySourceTile = new Dictionary<int, float>();
            else
                manualOutgoingSumBySourceTile.Clear();
            if (manualIncomingWeightedSumByDestTile == null)
                manualIncomingWeightedSumByDestTile = new Dictionary<int, float>();
            else
                manualIncomingWeightedSumByDestTile.Clear();
            if (manualOutgoingDestsBySourceTile == null)
                manualOutgoingDestsBySourceTile = new Dictionary<int, List<int>>();
            else
                ClearDrawIndexLists(manualOutgoingDestsBySourceTile);
            if (manualIncomingSourcesByDestTile == null)
                manualIncomingSourcesByDestTile = new Dictionary<int, List<int>>();
            else
                ClearDrawIndexLists(manualIncomingSourcesByDestTile);

            foreach (LogisticsLink l in manualLinks)
            {
                if (manualOutgoingSumBySourceTile.TryGetValue(l.sourceTile, out float o))
                    manualOutgoingSumBySourceTile[l.sourceTile] = o + l.manualAssignment;
                else
                    manualOutgoingSumBySourceTile[l.sourceTile] = l.manualAssignment;
                float w = l.manualAssignment * GetEfficiency(l.sourceTile, l.destTile);
                if (manualIncomingWeightedSumByDestTile.TryGetValue(l.destTile, out float i))
                    manualIncomingWeightedSumByDestTile[l.destTile] = i + w;
                else
                    manualIncomingWeightedSumByDestTile[l.destTile] = w;

                if (l.manualAssignment <= 0.01f) continue;
                AddDrawIndexTile(manualOutgoingDestsBySourceTile, l.sourceTile, l.destTile);
                AddDrawIndexTile(manualIncomingSourcesByDestTile, l.destTile, l.sourceTile);
            }
        }

        private static void ClearDrawIndexLists(Dictionary<int, List<int>> dict)
        {
            foreach (var kv in dict)
                kv.Value.Clear();
            dict.Clear();
        }

        private static void AddDrawIndexTile(Dictionary<int, List<int>> dict, int key, int tile)
        {
            if (!dict.TryGetValue(key, out List<int> list))
            {
                list = new List<int>(4);
                dict[key] = list;
            }
            list.Add(tile);
        }

        /// <summary>Dest tiles for logistics overlay lines leaving <paramref name="sourceTile"/> (assignment &gt; 0.01).</summary>
        public List<int> GetOutgoingDestTilesForDraw(int sourceTile)
        {
            EnsureManualLinkAggregatesFresh();
            if (manualOutgoingDestsBySourceTile != null
                && manualOutgoingDestsBySourceTile.TryGetValue(sourceTile, out List<int> list))
                return list;
            return EmptyDrawTileList;
        }

        /// <summary>Source tiles for logistics overlay lines arriving at <paramref name="destTile"/> (assignment &gt; 0.01).</summary>
        public List<int> GetIncomingSourceTilesForDraw(int destTile)
        {
            EnsureManualLinkAggregatesFresh();
            if (manualIncomingSourcesByDestTile != null
                && manualIncomingSourcesByDestTile.TryGetValue(destTile, out List<int> list))
                return list;
            return EmptyDrawTileList;
        }

        /// <summary>Sum of manualAssignment for links originating at tile (food logistics overlay).</summary>
        public float GetOutgoingManualSumForTile(int tile)
        {
            EnsureManualLinkAggregatesFresh();
            return manualOutgoingSumBySourceTile != null && manualOutgoingSumBySourceTile.TryGetValue(tile, out float v) ? v : 0f;
        }

        public float GetDistributionBudgetForSourceTile(int sourceTile)
        {
            RebuildPlayerLogisticsRegistryIfNeeded();
            for (int i = 0; i < playerLogisticsNodesOrdered.Count; i++)
            {
                var node = playerLogisticsNodesOrdered[i];
                if (node.Obj.Tile == sourceTile)
                    return Mathf.Max(0f, GetDailyProduction(node.Obj) - GetDailyDemand(node.Obj));
            }
            return 0f;
        }

        public bool ClampManualAssignmentsForSourceBudget(int sourceTile)
        {
            MergeDuplicateManualLinksFromSource(sourceTile);
            float budget = GetDistributionBudgetForSourceTile(sourceTile);
            float total = GetOutgoingManualSumForTile(sourceTile);
            if (total <= budget + 0.001f) return false;

            float remaining = budget;
            bool changed = false;
            for (int i = 0; i < manualLinks.Count; i++)
            {
                LogisticsLink link = manualLinks[i];
                if (link.sourceTile != sourceTile || link.manualAssignment <= 0f) continue;

                float clamped = Mathf.Min(link.manualAssignment, remaining);
                clamped = (float)Math.Round(clamped, 1);
                if (clamped > remaining) clamped = remaining;
                if (Mathf.Abs(link.manualAssignment - clamped) > 0.0001f)
                {
                    link.manualAssignment = Mathf.Max(0f, clamped);
                    changed = true;
                }
                remaining = Mathf.Max(0f, remaining - link.manualAssignment);
            }

            if (changed)
                NotifyManualLinksChanged();
            return changed;
        }

        private bool ClampAllManualAssignmentsToSourceBudgets()
        {
            var sources = new HashSet<int>();
            for (int i = 0; i < manualLinks.Count; i++)
            {
                if (manualLinks[i].manualAssignment > 0.001f)
                    sources.Add(manualLinks[i].sourceTile);
            }

            bool changed = false;
            foreach (int source in sources)
                changed |= ClampManualAssignmentsForSourceBudget(source);
            return changed;
        }

        /// <summary>Sum of food/day actually delivered on links ending at tile (assignment × efficiency; colonies always full efficiency).</summary>
        public float GetIncomingManualWeightedSumForTile(int tile)
        {
            EnsureManualLinkAggregatesFresh();
            return manualIncomingWeightedSumByDestTile != null && manualIncomingWeightedSumByDestTile.TryGetValue(tile, out float v) ? v : 0f;
        }

        /// <summary>Sum of manualAssignment for links from sourceTile to destinations in allowedDestTiles. Null set = all destinations (same as <see cref="GetOutgoingManualSumForTile"/>).</summary>
        public float GetOutgoingManualSumForSourceToDestinationsIn(int sourceTile, HashSet<int> allowedDestTiles)
        {
            if (allowedDestTiles == null)
                return GetOutgoingManualSumForTile(sourceTile);
            float sum = 0f;
            foreach (LogisticsLink l in manualLinks)
            {
                if (l.sourceTile != sourceTile || l.manualAssignment <= 0.01f) continue;
                if (allowedDestTiles.Contains(l.destTile))
                    sum += l.manualAssignment;
            }
            return sum;
        }

        /// <summary>Total assignment from one hub tile to one destination (all link rows; duplicates are merged when opening the logistics UI).</summary>
        public float GetOutgoingManualSumForDirectedLink(int sourceTile, int destTile)
        {
            float sum = 0f;
            foreach (LogisticsLink l in manualLinks)
            {
                if (l.sourceTile == sourceTile && l.destTile == destTile)
                    sum += l.manualAssignment;
            }
            return sum;
        }

        /// <summary>
        /// Collapses multiple <see cref="LogisticsLink"/> entries with the same (sourceTile, destTile) into one row.
        /// Prevents UI showing one assignment while <see cref="GetOutgoingManualSumForTile"/> / budget use the sum — + then appears to do nothing.
        /// </summary>
        public void MergeDuplicateManualLinksFromSource(int sourceTile)
        {
            var byDest = new Dictionary<int, List<LogisticsLink>>();
            for (int i = 0; i < manualLinks.Count; i++)
            {
                var l = manualLinks[i];
                if (l.sourceTile != sourceTile) continue;
                if (!byDest.TryGetValue(l.destTile, out var group))
                {
                    group = new List<LogisticsLink>();
                    byDest[l.destTile] = group;
                }
                group.Add(l);
            }
            bool changed = false;
            foreach (var kv in byDest)
            {
                var group = kv.Value;
                if (group.Count <= 1) continue;
                float sum = 0f;
                for (int i = 0; i < group.Count; i++)
                    sum += group[i].manualAssignment;
                for (int i = 1; i < group.Count; i++)
                {
                    manualLinks.Remove(group[i]);
                    changed = true;
                }
                float merged = (float)Math.Round(Mathf.Max(0f, sum), 1);
                if (Mathf.Abs(group[0].manualAssignment - merged) > 0.0001f)
                {
                    group[0].manualAssignment = merged;
                    changed = true;
                }
            }
            if (changed)
                NotifyManualLinksChanged();
        }

        /// <summary>One-time cleanup after load or def edits: same (hub tile -> dest tile) must not appear in multiple link rows.</summary>
        public void MergeAllDuplicateManualLinks()
        {
            var sources = new HashSet<int>();
            for (int i = 0; i < manualLinks.Count; i++)
                sources.Add(manualLinks[i].sourceTile);
            foreach (int src in sources)
                MergeDuplicateManualLinksFromSource(src);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref manualLinks, "manualLinks", LookMode.Deep);
            if (manualLinks == null) manualLinks = new List<LogisticsLink>();
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                manualLinksMutationVersion++;
                logisticsCacheGeneration++;
                logisticsCacheBuiltGeneration = -1;
                playerLogisticsRegistryDirty = true;
                InvalidateColonyTilesCache();
                Outpost_Trading.ClearAllTradingRadiusProbeCaches();
                Outpost_Embassy.ClearAllProbeCaches();
                smartLogisticsDirty = true;
                smartDirtyFromExplicitNotify = true;
                lastSmartCompletedSoftFingerprint = long.MinValue;
                foodSettingsSnapshotInitialized = false;
                MergeAllDuplicateManualLinks();
            }
        }

        public override void WorldComponentTick()
        {
            var s = WorldDominationMod.settings;
            if (!s.foodLogisticsActive) return;

            pulseDiag_smartStaggerHubsThisTick = 0;
            TickFoodSettingsSnapshot();
            if (smartLogisticsDirty)
                CompleteSmartLogisticsRefreshNow();

            if (Find.TickManager.TicksGame >= nextTick || nextTick == -1)
            {
                RunFoodAccountingPulse();
                nextTick = Find.TickManager.TicksGame + CheckInterval;
            }
        }

        /// <summary>Mark smart assignment out of date (player edits, pawn/production change, new hub).</summary>
        public void NotifySmartLogisticsDirty()
        {
            smartLogisticsDirty = true;
            smartDirtyFromExplicitNotify = true;
        }

        /// <summary>Runs all non-manual smart hubs in one go (UI: mode changes, input changes, fingerprint heal).</summary>
        public void CompleteSmartLogisticsRefreshNow()
        {
            if (!smartLogisticsDirty) return;

            bool wasHeal = !smartDirtyFromExplicitNotify;
            LastSmartCompleteWasFingerprintHeal = wasHeal;
            if (Prefs.DevMode && wasHeal)
                Log.Message("[TSA WD] Smart logistics fingerprint heal (no explicit dirty since last complete).");

            RebuildPlayerLogisticsRegistryIfNeeded();
            var list = new List<(WorldObject Obj, CompOutpostLogistics Logi)>();
            for (int i = 0; i < playerLogisticsNodesOrdered.Count; i++)
            {
                var pair = playerLogisticsNodesOrdered[i];
                if (LogisticsModeUtil.UsesAutomaticAssignment(pair.Logi.mode))
                    list.Add(pair);
            }
            list.Sort((a, b) => a.Obj.ID.CompareTo(b.Obj.ID));
            pulseDiag_smartImmediateHubsLastBatch = list.Count;
            pulseDiag_smartImmediateLastGameTick = Find.TickManager?.TicksGame ?? -1;
            pulseDiag_smartStaggerHubsThisTick = list.Count;
            BeginManualLinkNotifySuppress();
            try
            {
                LogisticsSmartAssignment.ExecuteAllSmartHubs(this, list);
            }
            finally
            {
                EndManualLinkNotifySuppress();
            }

            smartLogisticsDirty = false;
            smartDirtyFromExplicitNotify = false;
            lastSmartCompletedSoftFingerprint = ComputeSmartSoftFingerprint();
            if (WorldDominationMod.settings != null && WorldDominationMod.settings.verboseLogging)
                AssertSmartInvariantsAfterComplete(list);
        }

        /// <summary>
        /// Compare soft fingerprint to last Smart complete. On mismatch, mark dirty and refresh.
        /// Quiet path (match): compare only — no Smart rewrite.
        /// </summary>
        public void TryReconcileSmartFromFingerprint(string reason)
        {
            LastPulseSmartCompareOnly = true;
            if (!AnySmartHub()) return;

            long soft = ComputeSmartSoftFingerprint();
            if (soft == lastSmartCompletedSoftFingerprint)
                return;

            LastPulseSmartCompareOnly = false;
            // Heal path: dirty without counting as "explicit" notify so DevMode can flag call-site gaps.
            smartLogisticsDirty = true;
            CompleteSmartLogisticsRefreshNow();
            if (Prefs.DevMode)
                Log.Message($"[TSA WD] Smart logistics reconciled ({reason}).");
        }

        private bool AnySmartHub()
        {
            RebuildPlayerLogisticsRegistryIfNeeded();
            for (int i = 0; i < playerLogisticsNodesOrdered.Count; i++)
            {
                if (LogisticsModeUtil.UsesAutomaticAssignment(playerLogisticsNodesOrdered[i].Logi.mode))
                    return true;
            }
            return false;
        }

        private void AssertSmartInvariantsAfterComplete(List<(WorldObject Obj, CompOutpostLogistics Logi)> smartHubs)
        {
            var s = WorldDominationMod.settings;
            if (s == null) return;
            for (int h = 0; h < smartHubs.Count; h++)
            {
                var hub = smartHubs[h].Obj;
                float budget = GetDistributionBudgetForSourceTile(hub.Tile);
                float outgoing = GetOutgoingManualSumForTile(hub.Tile);
                float keep = Mathf.Max(0f, budget - outgoing);
                if (Mathf.Abs(keep + outgoing - budget) > 0.15f)
                    WDVerbose.Msg($"Smart invariant: Keep+outgoing != budget at {hub.LabelCap} (keep={keep:F1} out={outgoing:F1} budget={budget:F1}).");

                supportInvariantScratch.Clear();
                GetSupportLogisticsTargetsWithNet(hub, s, supportInvariantScratch);
                if (supportInvariantScratch.Count < 2) continue;
                float minN = float.MaxValue, maxN = float.MinValue;
                for (int i = 0; i < supportInvariantScratch.Count; i++)
                {
                    float n = supportInvariantScratch[i].Net;
                    if (n < minN) minN = n;
                    if (n > maxN) maxN = n;
                }
                if (maxN - minN > 0.15f)
                    WDVerbose.Msg($"Smart invariant: recipient nets span {maxN - minN:F2} at hub {hub.LabelCap} (want ≤ 0.15).");
            }
        }

        private static readonly List<(WorldObject Obj, float Net)> supportInvariantScratch = new List<(WorldObject Obj, float Net)>(16);

        /// <summary>Hubs / producers / pawns / modes / producer surplus — excludes link mutation version.</summary>
        private long ComputeSmartSoftFingerprint()
        {
            RebuildPlayerLogisticsRegistryIfNeeded();
            EnsureLogisticsCacheFresh();
            int hubCount = 0;
            int prodCount = 0;
            int pawnPacked = 0;
            int modePacked = 0;
            int surplusPacked = 0;
            for (int i = 0; i < playerLogisticsNodesOrdered.Count; i++)
            {
                var pair = playerLogisticsNodesOrdered[i];
                var wo = pair.Obj;
                hubCount++;
                if (Outpost_Production_Utils.IsFoodProducerOutpost(wo.def))
                {
                    prodCount++;
                    float surplus = Mathf.Max(0f, GetDailyProduction(wo) - GetDailyDemand(wo));
                    surplusPacked = unchecked(surplusPacked + wo.ID * 31 + Mathf.RoundToInt(surplus * 10f));
                }
                if (wo is WorldObject_WD_Outpost op)
                    pawnPacked = unchecked(pawnPacked + op.PawnCount * 397 + wo.ID);
                if (LogisticsModeUtil.UsesAutomaticAssignment(pair.Logi.mode))
                    modePacked ^= wo.ID * 17 + (int)LogisticsModeUtil.Normalize(pair.Logi.mode) * 31;
            }
            unchecked
            {
                return (uint)hubCount ^ ((long)prodCount << 16) ^ pawnPacked ^ modePacked ^ ((long)surplusPacked << 8);
            }
        }

        /// <summary>
        /// In-game days of food left while net is draining. Burn rate is -net (time until empty).
        /// Returns null when net is flat/positive, so surplus or stable outposts do not count as critical.
        /// </summary>
        public static float? TryGetDaysUntilStarvation(CompOutpostLogistics logi, float dailyDemand)
        {
            if (logi == null) return null;
            if (logi.currentFood < 0f)
                return 0f;

            float burnRate = logi.lastNetChange < -0.001f
                ? -logi.lastNetChange
                : 0f;
            if (burnRate <= 0.001f)
                return null;
            if (logi.currentFood <= 0.001f)
                return 0f;
            return logi.currentFood / burnRate;
        }

        /// <summary>
        /// True when stock is negative, or when net is draining and time-until-empty
        /// is within <paramref name="withinDays"/>.
        /// </summary>
        public static bool IsCriticalFoodLevel(CompOutpostLogistics logi, float dailyDemand, float withinDays = 3f)
        {
            if (logi == null) return false;
            if (logi.currentFood < 0f) return true;
            float? days = TryGetDaysUntilStarvation(logi, dailyDemand);
            return days != null && days.Value <= withinDays;
        }

        /// <summary>
        /// Fills <paramref name="into"/> with player logistics outposts at critical food
        /// (negative stock, or ≤3 days of food left while net is draining),
        /// sorted most severe first (fewest days, then lowest food).
        /// </summary>
        public void CollectCriticalFoodOutposts(List<(WorldObject Obj, CompOutpostLogistics Logi, float DaysUntilStarvation)> into)
        {
            into.Clear();
            if (WorldDominationMod.settings == null || !WorldDominationMod.settings.foodLogisticsActive) return;
            RebuildPlayerLogisticsRegistryIfNeeded();
            for (int i = 0; i < playerLogisticsNodesOrdered.Count; i++)
            {
                var pair = playerLogisticsNodesOrdered[i];
                if (!(pair.Obj is WorldObject_WD_Outpost)) continue;
                float demand = GetDailyDemand(pair.Obj);
                if (!IsCriticalFoodLevel(pair.Logi, demand)) continue;
                float days = TryGetDaysUntilStarvation(pair.Logi, demand) ?? 0f;
                into.Add((pair.Obj, pair.Logi, days));
            }
            into.Sort((a, b) =>
            {
                int c = a.DaysUntilStarvation.CompareTo(b.DaysUntilStarvation);
                if (c != 0) return c;
                return a.Logi.currentFood.CompareTo(b.Logi.currentFood);
            });
        }

        private void TickFoodSettingsSnapshot()
        {
            var s = WorldDominationMod.settings;
            if (s == null) return;
            if (!foodSettingsSnapshotInitialized)
            {
                foodSettingsSnapshotInitialized = true;
                settingsWatchFoodActive = s.foodLogisticsActive;
                settingsWatchFoodCons = s.foodConsumptionPerPawn;
                settingsWatchFoodProdBase = s.foodProductionPerOutpostBase;
                settingsWatchMaxRange = s.maxLogisticsRange;
                settingsWatchTileFloor = s.virtualFoodTileMultiplierFloor;
                return;
            }

            if (s.foodLogisticsActive != settingsWatchFoodActive
                || Mathf.Abs(s.foodConsumptionPerPawn - settingsWatchFoodCons) > 0.0001f
                || Mathf.Abs(s.foodProductionPerOutpostBase - settingsWatchFoodProdBase) > 0.0001f
                || s.maxLogisticsRange != settingsWatchMaxRange
                || Mathf.Abs(s.virtualFoodTileMultiplierFloor - settingsWatchTileFloor) > 0.0001f)
            {
                settingsWatchFoodActive = s.foodLogisticsActive;
                settingsWatchFoodCons = s.foodConsumptionPerPawn;
                settingsWatchFoodProdBase = s.foodProductionPerOutpostBase;
                settingsWatchMaxRange = s.maxLogisticsRange;
                settingsWatchTileFloor = s.virtualFoodTileMultiplierFloor;
                logisticsCacheGeneration++;
                InvalidateColonyTilesCache();
                NotifySmartLogisticsDirty();
            }
        }

        /// <returns>True if player logistics registry lists were rebuilt from world objects.</returns>
        private bool RebuildPlayerLogisticsRegistryIfNeeded()
        {
            if (!playerLogisticsRegistryDirty && playerLogisticsNodesOrdered != null && playerLogisticsColoniesOrdered != null)
                return false;

            playerLogisticsNodesOrdered ??= new List<(WorldObject Obj, CompOutpostLogistics Logi)>();
            playerLogisticsColoniesOrdered ??= new List<WorldObject>();
            playerLogisticsNodesOrdered.Clear();
            playerLogisticsColoniesOrdered.Clear();
            if (Find.WorldObjects == null)
            {
                playerLogisticsRegistryDirty = false;
                return true;
            }
            foreach (WorldObject wo in Find.WorldObjects.AllWorldObjects)
            {
                if (wo.Faction != Faction.OfPlayer) continue;
                var logi = wo.GetComponent<CompOutpostLogistics>();
                if (logi != null)
                    playerLogisticsNodesOrdered.Add((wo, logi));
                else if (wo is MapParent mp && mp.HasMap)
                    playerLogisticsColoniesOrdered.Add(wo);
            }
            playerLogisticsNodesOrdered.Sort((a, b) => a.Obj.ID.CompareTo(b.Obj.ID));
            playerLogisticsColoniesOrdered.Sort((a, b) => a.ID.CompareTo(b.ID));
            playerLogisticsRegistryDirty = false;
            return true;
        }

        /// <summary>Player WD outposts (and any object) with logistics comp, sorted by ID. Rebuilt on topology notify.</summary>
        public IReadOnlyList<(WorldObject Obj, CompOutpostLogistics Logi)> GetCachedPlayerLogisticsNodes()
        {
            RebuildPlayerLogisticsRegistryIfNeeded();
            return playerLogisticsNodesOrdered;
        }

        /// <summary>Player colonies with maps and no logistics comp (smart + tab).</summary>
        public IReadOnlyList<WorldObject> GetCachedPlayerLogisticsColonies()
        {
            RebuildPlayerLogisticsRegistryIfNeeded();
            return playerLogisticsColoniesOrdered;
        }

        private void RebuildLogisticsCacheEager()
        {
            RebuildPlayerLogisticsRegistryIfNeeded();
            EnsureManualLinkAggregatesFresh();
            logisticsCacheByObjectId ??= new Dictionary<int, LogisticsCacheEntry>();
            logisticsCacheByObjectId.Clear();
            for (int i = 0; i < playerLogisticsNodesOrdered.Count; i++)
            {
                var wo = playerLogisticsNodesOrdered[i].Obj;
                float prod = ComputeDailyProductionUncached(wo, null);
                float dem = ComputeDailyDemandUncached(wo);
                float inc = GetIncomingManualWeightedSumForTile(wo.Tile);
                float outg = GetOutgoingManualSumForTile(wo.Tile);
                float net = prod - dem + inc - outg;
                logisticsCacheByObjectId[wo.ID] = new LogisticsCacheEntry
                {
                    DailyProduction = prod,
                    DailyDemand = dem,
                    NetLogistics = net
                };
            }
            logisticsCacheBuiltGeneration = logisticsCacheGeneration;
        }

        private void EnsureLogisticsCacheFresh()
        {
            EnsureLogisticsCacheFreshReturnRebuilt();
        }

        /// <returns>True if <see cref="RebuildLogisticsCacheEager"/> ran (numeric cache was stale).</returns>
        private bool EnsureLogisticsCacheFreshReturnRebuilt()
        {
            if (logisticsCacheBuiltGeneration == logisticsCacheGeneration) return false;
            RebuildLogisticsCacheEager();
            return true;
        }

        /// <summary>
        /// Full delivery (1) to player colony settlements at any distance; for other destinations, 1 only within max logistics range.
        /// </summary>
        public float GetEfficiency(int start, int end)
        {
            if (GetColonyTilesCached().Contains(end))
                return 1f;
            var s = WorldDominationMod.settings;
            float dist = Find.WorldGrid.ApproxDistanceInTiles(start, end);
            return dist <= (float)s.maxLogisticsRange ? 1f : 0f;
        }

        /// <summary>Raw tile multiplier for virtual food (same helpers as physical farming/hunting). Non–food-producer or null: 1.</summary>
        public static float GetVirtualFoodRawTileMultiplier(WorldObject_WD_Outpost op)
        {
            if (op == null || !Outpost_Production_Utils.IsFoodProducerOutpost(op.def)) return 1f;
            if (Outpost_Production_Utils.IsFarmingOutpost(op.def))
                return Outpost_Production_Utils.GetFarmingTileProductionFactor(op);
            if (Outpost_Production_Utils.IsRanchOutpost(op.def))
                return Outpost_Production_Utils.GetRanchTileProductionFactor(op);
            if (Outpost_Production_Utils.IsFishingOutpost(op.def))
                return Outpost_Production_Utils.GetFishingTileProductionFactor(op);
            return Outpost_Production_Utils.GetHuntingTileProductionFactor(op);
        }

        /// <summary>Clamped mod setting for minimum virtual food tile multiplier (same bounds as settings slider).</summary>
        public static float GetVirtualFoodTileMultiplierFloorClamped()
        {
            float v = WorldDominationMod.settings?.virtualFoodTileMultiplierFloor ?? WorldDominationSettings.DefVirtualFoodTileMultiplierFloor;
            return Mathf.Clamp(v, WorldDominationSettings.DefVirtualFoodTileMultiplierFloor, 1f);
        }

        /// <summary>Virtual food tile multiplier after applying minimum floor: max(raw, floor).</summary>
        public static float GetVirtualFoodEffectiveTileMultiplier(WorldObject_WD_Outpost op)
        {
            return Mathf.Max(GetVirtualFoodTileMultiplierFloorClamped(), GetVirtualFoodRawTileMultiplier(op));
        }

        private static float ComputeDailyProductionUncached(WorldObject obj, float? effectiveVirtualFoodTileOverride)
        {
            var s = WorldDominationMod.settings;
            float baseProd = s?.foodProductionPerOutpostBase ?? WorldDominationSettings.DefFoodProductionPerOutpostBase;

            if (!(obj is WorldObject_WD_Outpost op)) return baseProd;
            baseProd += op.GetBuiltUpgradeFoodProductionFlatBonus();

            bool isFoodProducer = Outpost_Production_Utils.IsFoodProducerOutpost(op.def);
            if (isFoodProducer)
            {
                float totalSkill = Outpost_Production_Utils.IsFarmingOutpost(op.def) ? op.TotalPlantsSkill() : op.TotalHuntingSkill();
                float effective = effectiveVirtualFoodTileOverride ?? GetVirtualFoodEffectiveTileMultiplier(op);
                // Base production is flat (any tile), tile efficiency only scales the skill-based food on top.
                float raw = baseProd + totalSkill * effective;
                return raw * OutpostWarehouseAuraUtility.GetSoftProductionBonusMultiplier(op);
            }
            return baseProd;
        }

        private static float ComputeDailyDemandUncached(WorldObject obj)
        {
            var s = WorldDominationMod.settings;
            float count = 0f;
            if (obj is MapParent mapParent && mapParent.HasMap) count = (float)mapParent.Map.mapPawns.AllPawnsCount;
            else if (obj is WorldObject_WD_Outpost outpost) count = (float)outpost.CountOccupantsConsumingFood();

            return count * s.foodConsumptionPerPawn;
        }

        /// <summary>Daily production: base + skill for farming/hunting, scaled by tile (fertility / animal abundance) with mod-settings floor. Non-WD objects use legacy fallback.</summary>
        /// <param name="effectiveVirtualFoodTileOverride">When set (e.g. UI that already computed <see cref="GetVirtualFoodEffectiveTileMultiplier"/>), skips a second expensive farming yield pass.</param>
        public float GetDailyProduction(WorldObject obj, float? effectiveVirtualFoodTileOverride = null)
        {
            if (obj == null) return 0f;
            if (effectiveVirtualFoodTileOverride.HasValue)
                return ComputeDailyProductionUncached(obj, effectiveVirtualFoodTileOverride);
            EnsureLogisticsCacheFresh();
            if (logisticsCacheByObjectId != null && logisticsCacheByObjectId.TryGetValue(obj.ID, out LogisticsCacheEntry e))
                return e.DailyProduction;
            return ComputeDailyProductionUncached(obj, null);
        }

        public float GetDailyDemand(WorldObject obj)
        {
            if (obj == null) return 0f;
            EnsureLogisticsCacheFresh();
            if (logisticsCacheByObjectId != null && logisticsCacheByObjectId.TryGetValue(obj.ID, out LogisticsCacheEntry e))
                return e.DailyDemand;
            return ComputeDailyDemandUncached(obj);
        }

        /// <summary>Net food/day at an outpost: local production − demand + all incoming shipments − outgoing. Same as logistics UI. After the current producer hub zeroes its outgoing links, this reflects other producers already feeding this outpost.</summary>
        public float GetLogisticsNetDailyForOutpost(WorldObject target)
        {
            if (target == null) return 0f;
            EnsureManualLinkAggregatesFresh();
            EnsureLogisticsCacheFresh();
            if (logisticsCacheByObjectId != null && logisticsCacheByObjectId.TryGetValue(target.ID, out LogisticsCacheEntry e))
                return e.NetLogistics;
            float incoming = GetIncomingManualWeightedSumForTile(target.Tile);
            float outgoing = GetOutgoingManualSumForTile(target.Tile);
            return GetDailyProduction(target) - GetDailyDemand(target) + incoming - outgoing;
        }

        private static void BuildNodesByTile(
            List<(WorldObject Obj, CompOutpostLogistics Logi)> nodes,
            Dictionary<int, List<(WorldObject Obj, CompOutpostLogistics Logi)>> byTile)
        {
            byTile.Clear();
            for (int i = 0; i < nodes.Count; i++)
            {
                var n = nodes[i];
                int t = n.Obj.Tile;
                if (!byTile.TryGetValue(t, out var list))
                {
                    list = new List<(WorldObject Obj, CompOutpostLogistics Logi)>(2);
                    byTile[t] = list;
                }
                list.Add(n);
            }
        }

        /// <summary>Same rules as legacy TryResolveManualShipmentRecipient: prefer non–food-producer at dest, then lowest ID; never the hub.</summary>
        private static bool TryResolveRecipientFromTileMap(
            Dictionary<int, List<(WorldObject Obj, CompOutpostLogistics Logi)>> byTile,
            WorldObject hubObj,
            int destTile,
            out (WorldObject Obj, CompOutpostLogistics Logi) recipient)
        {
            recipient = default;
            if (!byTile.TryGetValue(destTile, out var atDest) || atDest.Count == 0) return false;
            int bestNonProdId = int.MaxValue;
            (WorldObject Obj, CompOutpostLogistics Logi) bestNonProd = default;
            bool hasNonProd = false;
            int bestAnyId = int.MaxValue;
            (WorldObject Obj, CompOutpostLogistics Logi) bestAny = default;
            for (int i = 0; i < atDest.Count; i++)
            {
                var cand = atDest[i];
                if (ReferenceEquals(cand.Obj, hubObj)) continue;
                if (cand.Obj.ID < bestAnyId)
                {
                    bestAnyId = cand.Obj.ID;
                    bestAny = cand;
                }
                if (!Outpost_Production_Utils.IsFoodProducerOutpost(cand.Obj.def) && cand.Obj.ID < bestNonProdId)
                {
                    bestNonProdId = cand.Obj.ID;
                    bestNonProd = cand;
                    hasNonProd = true;
                }
            }
            if (bestAnyId == int.MaxValue) return false;
            recipient = hasNonProd ? bestNonProd : bestAny;
            return true;
        }

        /// <summary>Virtual food drift from current <see cref="manualLinks"/>; Smart assignment runs atomically on dirty / fingerprint heal.</summary>
        private void RunFoodAccountingPulse()
        {
            TryReconcileSmartFromFingerprint("food accounting pulse");

            bool registryRebuilt = RebuildPlayerLogisticsRegistryIfNeeded();
            ClampAllManualAssignmentsToSourceBudgets();
            bool numCacheRebuilt = EnsureLogisticsCacheFreshReturnRebuilt();
            int tick = Find.TickManager.TicksGame;
            var registryNodes = playerLogisticsNodesOrdered;
            var nodes = new List<(WorldObject Obj, CompOutpostLogistics Logi)>(registryNodes.Count);
            for (int i = 0; i < registryNodes.Count; i++)
                nodes.Add(registryNodes[i]);

            bool pulseNetFromCache = logisticsCacheByObjectId != null;
            if (pulseNetFromCache)
            {
                for (int pi = 0; pi < nodes.Count; pi++)
                {
                    if (!logisticsCacheByObjectId.TryGetValue(nodes[pi].Obj.ID, out _))
                    {
                        pulseNetFromCache = false;
                        break;
                    }
                }
            }

            pulsePendingChanges.Clear();
            int prodDemFromDict = 0;
            int prodDemUncachedFallback = 0;
            int linkRowsFromProducers = 0;
            int getEfficiencyCalls = 0;
            int transferApplies = 0;

            if (pulseNetFromCache)
            {
                // Same daily net as logistics UI: prod − demand + Σ(incoming×eff) − Σ(outgoing), already in
                // <see cref="RebuildLogisticsCacheEager"/> / <see cref="EnsureManualLinkAggregatesFresh"/>.
                // Avoids re-walking every link row and re-calling GetEfficiency each pulse (GetEfficiency was applied when the cache was built).
                for (int pi = 0; pi < nodes.Count; pi++)
                {
                    var entry = logisticsCacheByObjectId[nodes[pi].Obj.ID];
                    pulsePendingChanges[nodes[pi].Logi] = entry.NetLogistics;
                    prodDemFromDict++;
                }
            }
            else
            {
                pulseNodesByTile.Clear();
                BuildNodesByTile(nodes, pulseNodesByTile);
                var nodesByTile = pulseNodesByTile;

                for (int pi = 0; pi < nodes.Count; pi++)
                {
                    var wo = nodes[pi].Obj;
                    float prod;
                    float dem;
                    if (logisticsCacheByObjectId != null && logisticsCacheByObjectId.TryGetValue(wo.ID, out var entry))
                    {
                        prod = entry.DailyProduction;
                        dem = entry.DailyDemand;
                        prodDemFromDict++;
                    }
                    else
                    {
                        prod = ComputeDailyProductionUncached(wo, null);
                        dem = ComputeDailyDemandUncached(wo);
                        prodDemUncachedFallback++;
                    }
                    pulsePendingChanges[nodes[pi].Logi] = prod - dem;
                }

                for (int hi = 0; hi < nodes.Count; hi++)
                {
                    var hub = nodes[hi];
                    if (!Outpost_Production_Utils.IsFoodProducerOutpost(hub.Obj.def)) continue;
                    int hubTile = hub.Obj.Tile;
                    for (int li = 0; li < manualLinks.Count; li++)
                    {
                        LogisticsLink link = manualLinks[li];
                        if (link.sourceTile != hubTile) continue;
                        linkRowsFromProducers++;
                        pulsePendingChanges[hub.Logi] -= link.manualAssignment;
                        getEfficiencyCalls++;
                        float eff = GetEfficiency(hubTile, link.destTile);
                        if (eff <= 0f) continue;
                        if (!TryResolveRecipientFromTileMap(nodesByTile, hub.Obj, link.destTile, out var consumer))
                            continue;
                        pulsePendingChanges[consumer.Logi] += link.manualAssignment * eff;
                        transferApplies++;
                    }
                }
            }

            var pendingChanges = pulsePendingChanges;

            float pulseFractionOfDay = CheckInterval / 60000f;
            foreach (var node in nodes)
            {
                node.Logi.lastNetChange = pendingChanges[node.Logi];
                float maxFood = CompOutpostLogistics.GetEffectiveMaxFoodFor(node.Obj);
                node.Logi.currentFood = Mathf.Clamp(node.Logi.currentFood + (node.Logi.lastNetChange * pulseFractionOfDay), 0, maxFood);
            }

            int starvationDemandUncached = 0;
            foreach (var node in nodes)
            {
                // Only kill when stored food is empty *and* this accounting pulse is not adding food (lastNetChange > 0
                // can leave currentFood below 0.001 due to tiny per-pulse gains while net is still recovering).
                if (node.Logi.currentFood > 0.001f || node.Logi.lastNetChange > 0f)
                    continue;
                float demCheck;
                if (logisticsCacheByObjectId != null && logisticsCacheByObjectId.TryGetValue(node.Obj.ID, out var entStarve))
                    demCheck = entStarve.DailyDemand;
                else
                {
                    demCheck = ComputeDailyDemandUncached(node.Obj);
                    starvationDemandUncached++;
                }
                if (demCheck > 0f && node.Obj is WorldObject_WD_Outpost wd)
                    wd.TryKillOneOccupantFromStarvation();
            }

            bool pulseIsCheapPath = !registryRebuilt && !numCacheRebuilt && prodDemUncachedFallback == 0 && starvationDemandUncached == 0;
            int smartImmediateSameTick = pulseDiag_smartImmediateLastGameTick == tick ? pulseDiag_smartImmediateHubsLastBatch : 0;
            WD_DevPerformanceSpikeLog.Msg(
                $"FoodAccountingPulse interval={CheckInterval} nodes={nodes.Count} pulseCheapPath={pulseIsCheapPath} " +
                $"pulseNetFromCache={pulseNetFromCache} registryRebuilt={registryRebuilt} logisticsNumCacheRebuilt={numCacheRebuilt} " +
                $"prodDemFromDict={prodDemFromDict} prodDemUncachedFallback={prodDemUncachedFallback} " +
                $"linkRows={linkRowsFromProducers} getEfficiencyCalls={getEfficiencyCalls} transferApplies={transferApplies} " +
                $"starvationDemandUncached={starvationDemandUncached} " +
                $"smartStaggerHubsThisTick={pulseDiag_smartStaggerHubsThisTick} smartImmediateSameTick={smartImmediateSameTick}");
        }

        public void ExecuteSmartLogic(WorldObject hub, CompOutpostLogistics logi)
        {
            LogisticsSmartAssignment.ExecuteSmartLogic(this, hub, logi);
        }

        /// <param name="markSmartDirty">Player-facing link edit; ignored by smart algorithms (default false).</param>
        /// <returns>True if a link row was added or <c>manualAssignment</c> actually changed.</returns>
        public bool SetAssignment(int src, int dst, float amt, bool markSmartDirty = false)
        {
            MergeDuplicateManualLinksFromSource(src);
            float budget = GetDistributionBudgetForSourceTile(src);
            float assignedToOtherDestinations = GetOutgoingManualSumForTile(src) - GetOutgoingManualSumForDirectedLink(src, dst);
            amt = Mathf.Min(Mathf.Max(0f, amt), Mathf.Max(0f, budget - assignedToOtherDestinations));
            const float eps = 0.01f;
            var link = manualLinks.FirstOrDefault(l => l.sourceTile == src && l.destTile == dst);
            if (link == null)
            {
                if (amt <= eps) return false;
                manualLinks.Add(new LogisticsLink { sourceTile = src, destTile = dst, manualAssignment = amt });
                NotifyManualLinksChanged();
                if (markSmartDirty) NotifySmartLogisticsDirty();
                return true;
            }
            if (Mathf.Abs(link.manualAssignment - amt) <= eps) return false;
            link.manualAssignment = amt;
            NotifyManualLinksChanged();
            if (markSmartDirty) NotifySmartLogisticsDirty();
            return true;
        }
    }
}