using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Where a tooling-capable outpost's output goes. Exclusive: physical items to the colony, or virtual tooling for outposts.</summary>
    public enum ToolingOutputRole
    {
        DeliverToColony = 0,
        GenerateTooling = 1,
    }

    public struct OutpostToolingStatus
    {
        /// <summary>Tooling used per day.</summary>
        public float demand;
        /// <summary>Tooling arriving per day from generators (can exceed demand while the stock refills).</summary>
        public float supplied;
        /// <summary>True while the outpost's stock is not empty.</summary>
        public bool tooled;
    }

    /// <summary>
    /// Virtual tooling. Mining and production outposts set to <see cref="ToolingOutputRole.GenerateTooling"/> turn their output
    /// into tooling (item count x item value, per day) instead of delivering it. Working outposts (mining, farming, hunting,
    /// fishing, ranch, production) keep a stockpile (<see cref="WorldObject_WD_Outpost.currentTooling"/>, like virtual food)
    /// that drains by their daily need; when it is empty their output is cut. Generators fill needs first (nearest outposts
    /// first, within the logistics range), then refill the emptiest stockpiles.
    /// </summary>
    public static class OutpostTooling
    {
        private static WorldComponent_OutpostToolingGrid cached;
        private static Dictionary<ThingDef, float> xmlValues;

        private static WorldComponent_OutpostToolingGrid Comp
        {
            get
            {
                World world = Find.World;
                if (world == null) return null;
                if (cached == null || cached.world != world)
                    cached = world.GetComponent<WorldComponent_OutpostToolingGrid>();
                return cached;
            }
        }

        public static bool Enabled =>
            WorldDominationMod.settings?.enableOutpostTooling ?? WorldDominationSettings.DefEnableOutpostTooling;

        /// <summary>Flat tooling per day by outpost tier 1, 2, 3 (tier 4+ uses the last entry).</summary>
        private static readonly float[] BaseDemandByTier = { 10f, 20f, 30f };

        /// <summary>Extra tooling per day for each occupant by outpost tier (prisoners are not occupants and do not count).</summary>
        private static readonly float[] PerPawnDemandByTier = { 3f, 5f, 7f };

        private static int TierIndex(WorldObject_WD_Outpost outpost) =>
            Mathf.Clamp(outpost.OutpostTier - 1, 0, BaseDemandByTier.Length - 1);

        public static float BaseDemand(WorldObject_WD_Outpost outpost) => BaseDemandByTier[TierIndex(outpost)];

        public static float PerPawnDemand(WorldObject_WD_Outpost outpost) => PerPawnDemandByTier[TierIndex(outpost)];

        /// <summary>The flat demand range across tiers, for tooltips.</summary>
        public static string BaseDemandRangeLabel =>
            BaseDemandByTier[0].ToString("F0") + "-" + BaseDemandByTier[BaseDemandByTier.Length - 1].ToString("F0");

        /// <summary>Share of output lost without tooling (default 66%).</summary>
        public static float UntooledPenalty =>
            Mathf.Clamp01(WorldDominationMod.settings?.untooledOutputPenalty ?? WorldDominationSettings.DefUntooledOutputPenalty);

        /// <summary>Stockpile capacity per outpost.</summary>
        public static float MaxPerOutpost =>
            Mathf.Max(1f, WorldDominationMod.settings?.maxToolingPerOutpost ?? WorldDominationSettings.DefMaxToolingPerOutpost);

        /// <summary>While a stockpile is below capacity a generator may send up to this many times the outpost's daily need.</summary>
        public const float RefillRateFactor = 3f;

        /// <summary>Tooling is shared within the food logistics range.</summary>
        public static float RangeTiles =>
            Mathf.Max(1f, WorldDominationMod.settings?.maxLogisticsRange ?? WorldDominationSettings.DefMaxLogisticsRange);

        // --- item values ---

        public static void InvalidateValueCache() => xmlValues = null;

        private static Dictionary<ThingDef, float> XmlValues
        {
            get
            {
                if (xmlValues != null) return xmlValues;
                var map = new Dictionary<ThingDef, float>();
                foreach (WdToolingValueDef d in DefDatabase<WdToolingValueDef>.AllDefsListForReading)
                {
                    if (d.thingDef != null) map[d.thingDef] = d.value;
                }
                xmlValues = map;
                return map;
            }
        }

        /// <summary>XML default for an item, or null when the item has no XML entry.</summary>
        public static float? GetXmlValue(ThingDef def) =>
            def != null && XmlValues.TryGetValue(def, out float v) ? v : (float?)null;

        /// <summary>Tooling per item: the settings override, else the XML value, else 0 (not tooling).</summary>
        public static float GetValue(ThingDef def)
        {
            if (def == null) return 0f;
            Dictionary<string, float> overrides = WorldDominationMod.settings?.toolingValueOverrides;
            if (overrides != null && overrides.TryGetValue(def.defName, out float o))
                return Mathf.Max(0f, o);
            return Mathf.Max(0f, GetXmlValue(def) ?? 0f);
        }

        /// <summary>
        /// Delivered goods become tooling in the outpost's stockpile. Takes as many items as fit under the cap and removes them
        /// from <paramref name="thing"/>; returns the item count taken (0 when the outpost needs none or the item has no value).
        /// </summary>
        public static int TryAbsorb(WorldObject_WD_Outpost outpost, Thing thing)
        {
            if (!Enabled || thing == null || thing.Destroyed || !RequiresTooling(outpost)) return 0;
            float value = GetValue(thing.def);
            if (value <= 0f) return 0;
            int count = Mathf.Min(thing.stackCount, Mathf.FloorToInt((MaxPerOutpost - outpost.currentTooling) / value + 0.0001f));
            if (count <= 0) return 0;

            outpost.currentTooling = Mathf.Min(MaxPerOutpost, outpost.currentTooling + count * value);
            if (count >= thing.stackCount)
                thing.Destroy(DestroyMode.Vanish);
            else
                thing.SplitOff(count).Destroy(DestroyMode.Vanish);
            outpost.InvalidateInspectCache();
            MarkDirty();
            return count;
        }

        // --- roles ---

        /// <summary>Only mining outposts can generate tooling instead of delivering items (other outputs are not mapped yet).</summary>
        public static bool CanGenerate(WorldObjectDef def)
        {
            if (def == null) return false;
            if (Outpost_Production_Utils.IsFoodProducerOutpost(def)) return false;
            return Outpost_Production_Utils.IsMiningOutpost(def);
        }

        public static bool IsGenerator(WorldObject_WD_Outpost outpost) =>
            outpost != null
            && outpost.Faction == Faction.OfPlayer
            && CanGenerate(outpost.def)
            && outpost.GetToolingRoleForCurrentCycle() == ToolingOutputRole.GenerateTooling;

        /// <summary>Working outposts that draw tooling. Outposts generating tooling are exempt (they make it).</summary>
        public static bool RequiresTooling(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null || outpost.def == null || outpost.Faction != Faction.OfPlayer) return false;
            WorldObjectDef def = outpost.def;
            bool worker = Outpost_Production_Utils.IsMiningOutpost(def)
                || Outpost_Production_Utils.IsFoodProducerOutpost(def)
                || Outpost_Production_Utils.IsProductionOrTradingOutpost(def);
            if (!worker) return false;
            return !IsGenerator(outpost);
        }

        /// <summary>Def-level version of <see cref="RequiresTooling(WorldObject_WD_Outpost)"/> for outposts not yet founded.</summary>
        public static bool RequiresToolingDef(WorldObjectDef def) =>
            def != null
            && (Outpost_Production_Utils.IsMiningOutpost(def)
                || Outpost_Production_Utils.IsFoodProducerOutpost(def)
                || Outpost_Production_Utils.IsProductionOrTradingOutpost(def));

        /// <summary>Daily tooling a new outpost of <paramref name="def"/> would need with <paramref name="occupants"/> pawns (no upgrades).</summary>
        public static float EstimateDemand(WorldObjectDef def, int occupants)
        {
            int i = Mathf.Clamp(WorldObject_WD_Outpost.GetOutpostTier(def) - 1, 0, BaseDemandByTier.Length - 1);
            return BaseDemandByTier[i] + PerPawnDemandByTier[i] * occupants;
        }

        /// <summary>
        /// Rough free tooling per day at <paramref name="tile"/>: output of Smart generators in range minus the demand of
        /// tooling-hungry outposts in range. Estimate only (the real allocator also refills stockpiles).
        /// </summary>
        public static float EstimateAvailableAt(int tile)
        {
            WorldGrid grid = Find.WorldGrid;
            float range = RangeTiles;
            float output = 0f;
            float demand = 0f;
            IReadOnlyList<WorldObject_WD_Outpost> outposts = WdPlayerOutpostCache.PlayerOutposts;
            for (int i = 0; i < outposts.Count; i++)
            {
                WorldObject_WD_Outpost o = outposts[i];
                if (o == null || o.Destroyed || grid.ApproxDistanceInTiles(tile, o.Tile) > range) continue;
                if (IsGenerator(o))
                {
                    if (SupplyAllocations.GetMode(SupplyResource.Tooling, o.Tile.tileId) != SupplyMode.Manual)
                        output += GetGeneratorOutput(o);
                }
                else if (RequiresTooling(o))
                    demand += GetDemand(o);
            }
            return Mathf.Max(0f, output - demand);
        }

        public static float GetDemand(WorldObject_WD_Outpost outpost)
        {
            if (!RequiresTooling(outpost)) return 0f;
            return BaseDemand(outpost) + PerPawnDemand(outpost) * outpost.Occupants.Count + outpost.GetBuiltUpgradeToolingNeed();
        }

        /// <summary>Tooling per day this outpost would generate from its selected product (item count x value / cycle days).</summary>
        public static float ComputeDailyGeneration(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null) return 0f;
            // Generating outposts have no product picker, and tooling per item is the same for every ore, so a fixed ore stands in.
            List<ThingDefCountClass> items;
            if (Outpost_Production_Utils.IsMiningOutpost(outpost.def))
            {
                List<ThingDef> options = Outpost_Mining.GetProducibleOptions(outpost);
                ThingDef ore = options != null && options.Count > 0 ? options[0] : null;
                items = Outpost_Mining.GetDeliveryItems(outpost, ore, outpost.GetCapacityForYieldPreview());
                if (items != null && items.Count > 0)
                    Outpost_Production_Utils.ApplyOutputMultiplierToDeliveryItems(items, outpost);
            }
            else
                items = Outpost_Production.GetCurrentDeliveryItems(outpost);
            if (items == null || items.Count == 0) return 0f;
            float perCycle = 0f;
            for (int i = 0; i < items.Count; i++)
            {
                ThingDefCountClass row = items[i];
                if (row?.thingDef == null || row.count <= 0) continue;
                perCycle += row.count * GetValue(row.thingDef);
            }
            float days = Mathf.Max(0.1f, Outpost_Production_Utils.GetProductionCycleDays(outpost));
            return perCycle / days;
        }

        // --- status ---

        public static bool IsTooled(WorldObject_WD_Outpost outpost)
        {
            if (!Enabled || !RequiresTooling(outpost)) return true;
            return TryGetStatus(outpost, out OutpostToolingStatus status) && status.tooled;
        }

        public static bool TryGetStatus(WorldObject_WD_Outpost outpost, out OutpostToolingStatus status)
        {
            status = default;
            WorldComponent_OutpostToolingGrid comp = outpost != null ? Comp : null;
            return comp != null && comp.TryGetStatus(outpost.ID, out status);
        }

        /// <summary>1 when tooled (or tooling does not apply), otherwise 1 minus the untooled penalty (default 34% output).</summary>
        public static float GetOutputFactor(WorldObject_WD_Outpost outpost) =>
            IsTooled(outpost) ? 1f : 1f - UntooledPenalty;

        /// <summary>Net tooling change per day for this outpost (arriving minus used); 0 when untracked.</summary>
        public static float GetNetPerDay(WorldObject_WD_Outpost outpost) =>
            TryGetStatus(outpost, out OutpostToolingStatus status) ? status.supplied - status.demand : 0f;

        public static float GetGeneratorOutput(WorldObject_WD_Outpost outpost) =>
            Comp?.GetGeneratorOutput(outpost?.ID ?? -1) ?? 0f;

        public static float GetGeneratorUsed(WorldObject_WD_Outpost outpost) =>
            Comp?.GetGeneratorUsed(outpost?.ID ?? -1) ?? 0f;

        public static int GetGeneratorServedCount(WorldObject_WD_Outpost outpost) =>
            Comp?.GetGeneratorServedCount(outpost?.ID ?? -1) ?? 0;

        public static void MarkDirty() => Comp?.MarkDirty();

        /// <summary>Tooling per day each consumer receives from this generator (last allocation).</summary>
        public static void GetOutgoing(int generatorId, List<SupplyFlow> into) => Comp?.GetOutgoing(generatorId, into);

        /// <summary>Tooling per day this outpost receives, per generator (last allocation).</summary>
        public static void GetIncoming(int consumerId, List<SupplyFlow> into) => Comp?.GetIncoming(consumerId, into);

        /// <summary>Consumers within range of a generator, nearest first.</summary>
        public static void GetRecipients(WorldObject_WD_Outpost generator, List<WorldObject_WD_Outpost> into)
        {
            into.Clear();
            if (generator == null) return;
            WorldGrid grid = Find.WorldGrid;
            float range = RangeTiles;
            IReadOnlyList<WorldObject_WD_Outpost> outposts = WdPlayerOutpostCache.PlayerOutposts;
            for (int i = 0; i < outposts.Count; i++)
            {
                WorldObject_WD_Outpost o = outposts[i];
                if (o == null || o.Destroyed || o == generator || !RequiresTooling(o)) continue;
                if (grid.ApproxDistanceInTiles(generator.Tile, o.Tile) <= range) into.Add(o);
            }
            int origin = generator.Tile;
            into.Sort((a, b) => grid.ApproxDistanceInTiles(origin, a.Tile).CompareTo(grid.ApproxDistanceInTiles(origin, b.Tile)));
        }
    }

    /// <summary>
    /// Keeps the tooling stockpiles. Every few hundred ticks the stockpiles are integrated with the last allocation (arriving
    /// minus used per day); a recompute then re-allocates generator output: first to cover each outpost's need (nearest first,
    /// partial supply allowed because the stockpile buffers it), then to refill the emptiest stockpiles.
    /// </summary>
    public class WorldComponent_OutpostToolingGrid : WorldComponent
    {
        private const int RecomputeIntervalTicks = 600;
        private const int IntegrateIntervalTicks = 250;
        private const int MaxIntegrateTicks = 3 * GenDate.TicksPerDay;
        private const float Epsilon = 0.01f;

        private struct Generator
        {
            public WorldObject_WD_Outpost obj;
            public int tile;
            public float remaining;
            public bool manual;
        }

        private struct Consumer
        {
            public WorldObject_WD_Outpost obj;
            public float demand;
            public float nearest;
            public float fill;
        }

        private struct Source
        {
            public int generatorIndex;
            public float dist;
        }

        private readonly Dictionary<int, OutpostToolingStatus> statusByOutpostId = new Dictionary<int, OutpostToolingStatus>();
        private readonly Dictionary<int, float> outputByGeneratorId = new Dictionary<int, float>();
        private readonly Dictionary<int, float> usedByGeneratorId = new Dictionary<int, float>();
        private readonly Dictionary<int, int> servedByGeneratorId = new Dictionary<int, int>();
        private readonly HashSet<long> servedPairs = new HashSet<long>();

        private readonly List<Generator> generators = new List<Generator>(8);
        private readonly List<Consumer> consumers = new List<Consumer>(32);
        private readonly List<Source> sourcesScratch = new List<Source>(8);
        private readonly List<SupplyFlow> flows = new List<SupplyFlow>(32);
        private readonly Dictionary<long, int> flowIndex = new Dictionary<long, int>();
        private readonly List<KeyValuePair<int, float>> linkScratch = new List<KeyValuePair<int, float>>(8);

        private bool dirty = true;
        private int nextRecomputeTick;
        private int lastIntegrateTick = -1;

        public WorldComponent_OutpostToolingGrid(World world) : base(world) { }

        public void MarkDirty() => dirty = true;

        private void NotifyOutpostsChanged()
        {
            IReadOnlyList<WorldObject_WD_Outpost> outposts = WdPlayerOutpostCache.PlayerOutposts;
            for (int i = 0; i < outposts.Count; i++)
                outposts[i]?.InvalidateInspectCache();
            world.GetComponent<WorldComponent_LogisticsManager>()?.NotifyFoodLogisticsInputsChanged();
        }

        public override void FinalizeInit(bool fromLoad)
        {
            base.FinalizeInit(fromLoad);
            dirty = true;
            lastIntegrateTick = -1;
            OutpostTooling.InvalidateValueCache();
        }

        public override void WorldComponentTick()
        {
            base.WorldComponentTick();
            int tick = Find.TickManager.TicksGame;
            if (!dirty && OutpostTooling.Enabled && tick - lastIntegrateTick >= IntegrateIntervalTicks)
                IntegrateStockpiles(tick);
            if (dirty || tick >= nextRecomputeTick)
                Recompute(tick);
        }

        public bool TryGetStatus(int outpostId, out OutpostToolingStatus status)
        {
            EnsureFresh();
            return statusByOutpostId.TryGetValue(outpostId, out status);
        }

        public void GetOutgoing(int generatorId, List<SupplyFlow> into)
        {
            EnsureFresh();
            into.Clear();
            for (int i = 0; i < flows.Count; i++)
            {
                if (flows[i].sourceId == generatorId) into.Add(flows[i]);
            }
        }

        public void GetIncoming(int consumerId, List<SupplyFlow> into)
        {
            EnsureFresh();
            into.Clear();
            for (int i = 0; i < flows.Count; i++)
            {
                if (flows[i].destId == consumerId) into.Add(flows[i]);
            }
        }

        public float GetGeneratorOutput(int id)
        {
            EnsureFresh();
            return outputByGeneratorId.TryGetValue(id, out float v) ? v : 0f;
        }

        public float GetGeneratorUsed(int id)
        {
            EnsureFresh();
            return usedByGeneratorId.TryGetValue(id, out float v) ? v : 0f;
        }

        public int GetGeneratorServedCount(int id)
        {
            EnsureFresh();
            return servedByGeneratorId.TryGetValue(id, out int v) ? v : 0;
        }

        private void EnsureFresh()
        {
            if (dirty && Find.TickManager != null)
                Recompute(Find.TickManager.TicksGame);
        }

        /// <summary>Applies the last allocation to every stockpile for the ticks elapsed since the previous integration.</summary>
        private void IntegrateStockpiles(int tick)
        {
            if (lastIntegrateTick < 0)
            {
                lastIntegrateTick = tick;
                return;
            }
            int dt = Mathf.Clamp(tick - lastIntegrateTick, 0, MaxIntegrateTicks);
            lastIntegrateTick = tick;
            if (dt <= 0) return;

            float days = dt / (float)GenDate.TicksPerDay;
            float max = OutpostTooling.MaxPerOutpost;
            bool anyChanged = false;
            for (int c = 0; c < consumers.Count; c++)
            {
                WorldObject_WD_Outpost outpost = consumers[c].obj;
                if (outpost == null || outpost.Destroyed) continue;
                if (!statusByOutpostId.TryGetValue(outpost.ID, out OutpostToolingStatus status)) continue;

                outpost.currentTooling = Mathf.Clamp(outpost.currentTooling + (status.supplied - status.demand) * days, 0f, max);
                bool tooled = status.demand <= Epsilon || outpost.currentTooling > Epsilon;
                if (tooled == status.tooled) continue;

                status.tooled = tooled;
                statusByOutpostId[outpost.ID] = status;
                outpost.InvalidateInspectCache();
                anyChanged = true;
            }
            if (anyChanged)
                world.GetComponent<WorldComponent_LogisticsManager>()?.NotifyFoodLogisticsInputsChanged();
        }

        private void Recompute(int tick)
        {
            if (!OutpostTooling.Enabled)
            {
                // Rule switched off (possibly mid-game): stockpiles freeze and every allocation is dropped.
                bool hadState = statusByOutpostId.Count > 0 || flows.Count > 0;
                dirty = false;
                nextRecomputeTick = tick + RecomputeIntervalTicks;
                lastIntegrateTick = tick;
                statusByOutpostId.Clear();
                outputByGeneratorId.Clear();
                usedByGeneratorId.Clear();
                servedByGeneratorId.Clear();
                servedPairs.Clear();
                generators.Clear();
                consumers.Clear();
                flows.Clear();
                flowIndex.Clear();
                if (hadState) NotifyOutpostsChanged();
                return;
            }

            IntegrateStockpiles(tick);
            dirty = false;
            nextRecomputeTick = tick + RecomputeIntervalTicks;

            var previous = new Dictionary<int, OutpostToolingStatus>(statusByOutpostId);
            statusByOutpostId.Clear();
            outputByGeneratorId.Clear();
            usedByGeneratorId.Clear();
            servedByGeneratorId.Clear();
            servedPairs.Clear();
            flows.Clear();
            flowIndex.Clear();

            CollectGenerators();
            CollectConsumers();

            WorldGrid grid = Find.WorldGrid;
            float range = OutpostTooling.RangeTiles;
            float max = OutpostTooling.MaxPerOutpost;

            for (int c = 0; c < consumers.Count; c++)
            {
                Consumer consumer = consumers[c];
                consumer.nearest = float.MaxValue;
                for (int g = 0; g < generators.Count; g++)
                {
                    float d = grid.ApproxDistanceInTiles(generators[g].tile, consumer.obj.Tile);
                    if (d <= range && d < consumer.nearest) consumer.nearest = d;
                }
                consumer.fill = Mathf.Clamp01(consumer.obj.currentTooling / max);
                consumers[c] = consumer;
            }

            // Phase 1: cover each outpost's daily need, nearest first. Partial supply is fine, the stockpile buffers it.
            consumers.Sort((a, b) =>
            {
                int cmp = a.nearest.CompareTo(b.nearest);
                return cmp != 0 ? cmp : a.obj.ID.CompareTo(b.obj.ID);
            });

            // Manual generators send only what is assigned to a consumer (capped by their output and the range).
            var supply0 = new float[consumers.Count];
            var consumerByTile = new Dictionary<int, int>(consumers.Count);
            for (int c = 0; c < consumers.Count; c++)
                consumerByTile[consumers[c].obj.Tile.tileId] = c;
            for (int g = 0; g < generators.Count; g++)
            {
                if (!generators[g].manual) continue;
                SupplyAllocations.GetAssignedLinks(SupplyResource.Tooling, generators[g].tile, linkScratch);
                for (int k = 0; k < linkScratch.Count; k++)
                {
                    if (!consumerByTile.TryGetValue(linkScratch[k].Key, out int c)) continue;
                    if (grid.ApproxDistanceInTiles(generators[g].tile, consumers[c].obj.Tile) > range) continue;
                    Generator gen = generators[g];
                    float give = Mathf.Min(linkScratch[k].Value, gen.remaining);
                    if (give <= Epsilon) continue;
                    gen.remaining -= give;
                    generators[g] = gen;
                    supply0[c] += give;
                    RecordFlow(gen, consumers[c].obj, give);
                }
            }

            var supply1 = new float[consumers.Count];
            for (int c = 0; c < consumers.Count; c++)
            {
                float deficit = consumers[c].demand - supply0[c];
                if (deficit <= Epsilon || consumers[c].nearest >= float.MaxValue) continue;
                supply1[c] = Take(consumers[c], deficit, grid, range);
            }

            // Phase 2: leftover output refills the emptiest stockpiles first.
            var order = new List<int>(consumers.Count);
            for (int c = 0; c < consumers.Count; c++)
            {
                if (consumers[c].nearest < float.MaxValue && consumers[c].fill < 1f - Epsilon)
                    order.Add(c);
            }
            order.Sort((a, b) =>
            {
                int cmp = consumers[a].fill.CompareTo(consumers[b].fill);
                return cmp != 0 ? cmp : consumers[a].obj.ID.CompareTo(consumers[b].obj.ID);
            });
            var supply2 = new float[consumers.Count];
            for (int k = 0; k < order.Count; k++)
            {
                int c = order[k];
                float want = Mathf.Max(1f, consumers[c].demand) * OutpostTooling.RefillRateFactor;
                supply2[c] = Take(consumers[c], want, grid, range);
            }

            for (int c = 0; c < consumers.Count; c++)
            {
                Consumer consumer = consumers[c];
                statusByOutpostId[consumer.obj.ID] = new OutpostToolingStatus
                {
                    demand = consumer.demand,
                    supplied = supply0[c] + supply1[c] + supply2[c],
                    tooled = consumer.demand <= Epsilon || consumer.obj.currentTooling > Epsilon
                };
            }

            ApplyChanges(previous);
        }

        /// <summary>Takes up to <paramref name="want"/> per day from the generators in range, nearest first. Returns the amount taken.</summary>
        private float Take(Consumer consumer, float want, WorldGrid grid, float range)
        {
            sourcesScratch.Clear();
            for (int g = 0; g < generators.Count; g++)
            {
                if (generators[g].manual || generators[g].remaining <= Epsilon) continue;
                float d = grid.ApproxDistanceInTiles(generators[g].tile, consumer.obj.Tile);
                if (d <= range) sourcesScratch.Add(new Source { generatorIndex = g, dist = d });
            }
            if (sourcesScratch.Count == 0) return 0f;
            sourcesScratch.Sort((a, b) => a.dist.CompareTo(b.dist));

            float taken = 0f;
            for (int s = 0; s < sourcesScratch.Count && want - taken > Epsilon; s++)
            {
                int gi = sourcesScratch[s].generatorIndex;
                Generator gen = generators[gi];
                float take = Mathf.Min(gen.remaining, want - taken);
                if (take <= 0f) continue;
                gen.remaining -= take;
                generators[gi] = gen;
                taken += take;
                RecordFlow(gen, consumer.obj, take);
            }
            return taken;
        }

        private void RecordFlow(Generator gen, WorldObject_WD_Outpost consumer, float amount)
        {
            AddTo(usedByGeneratorId, gen.obj.ID, amount);
            long pair = ((long)gen.obj.ID << 32) | (uint)consumer.ID;
            if (flowIndex.TryGetValue(pair, out int idx))
            {
                SupplyFlow existing = flows[idx];
                existing.amount += amount;
                flows[idx] = existing;
                return;
            }
            flowIndex[pair] = flows.Count;
            flows.Add(new SupplyFlow
            {
                sourceId = gen.obj.ID,
                destId = consumer.ID,
                sourceTile = gen.obj.Tile.tileId,
                destTile = consumer.Tile.tileId,
                amount = amount
            });
            if (servedPairs.Add(pair))
                AddTo(servedByGeneratorId, gen.obj.ID, 1);
        }

        private void ApplyChanges(Dictionary<int, OutpostToolingStatus> previous)
        {
            bool anyChanged = false;
            for (int c = 0; c < consumers.Count; c++)
            {
                WorldObject_WD_Outpost outpost = consumers[c].obj;
                bool now = statusByOutpostId[outpost.ID].tooled;
                bool changed = !previous.TryGetValue(outpost.ID, out OutpostToolingStatus old) || old.tooled != now;
                if (!changed) continue;
                anyChanged = true;
                outpost.InvalidateInspectCache();
            }
            if (anyChanged)
                world.GetComponent<WorldComponent_LogisticsManager>()?.NotifyFoodLogisticsInputsChanged();
        }

        private void CollectGenerators()
        {
            generators.Clear();
            IReadOnlyList<WorldObject_WD_Outpost> outposts = WdPlayerOutpostCache.PlayerOutposts;
            for (int i = 0; i < outposts.Count; i++)
            {
                WorldObject_WD_Outpost o = outposts[i];
                if (o == null || o.Destroyed || !OutpostTooling.IsGenerator(o)) continue;
                float output = OutpostTooling.ComputeDailyGeneration(o);
                outputByGeneratorId[o.ID] = output;
                if (output <= Epsilon) continue;
                generators.Add(new Generator
                {
                    obj = o,
                    tile = o.Tile,
                    remaining = output,
                    manual = SupplyAllocations.GetMode(SupplyResource.Tooling, o.Tile.tileId) == SupplyMode.Manual
                });
            }
        }

        private void CollectConsumers()
        {
            consumers.Clear();
            IReadOnlyList<WorldObject_WD_Outpost> outposts = WdPlayerOutpostCache.PlayerOutposts;
            for (int i = 0; i < outposts.Count; i++)
            {
                WorldObject_WD_Outpost o = outposts[i];
                if (o == null || o.Destroyed || !OutpostTooling.RequiresTooling(o)) continue;
                consumers.Add(new Consumer { obj = o, demand = OutpostTooling.GetDemand(o) });
            }
        }

        private static void AddTo(Dictionary<int, float> dict, int key, float amount) =>
            dict[key] = (dict.TryGetValue(key, out float v) ? v : 0f) + amount;

        private static void AddTo(Dictionary<int, int> dict, int key, int amount) =>
            dict[key] = (dict.TryGetValue(key, out int v) ? v : 0) + amount;
    }
}
