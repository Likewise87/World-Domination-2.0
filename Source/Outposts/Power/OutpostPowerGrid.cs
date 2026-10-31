using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Where a power outpost's output goes. Exclusive: the colony's power nets, or other outposts.</summary>
    public enum PowerOutputRole
    {
        SupplyColony = 0,
        SupplyOutposts = 1,
    }

    public struct OutpostPowerStatus
    {
        public float demand;
        public float supplied;
        public bool powered;
    }

    /// <summary>
    /// Static facade for outpost power: settings, demand, and "is this outpost powered" lookups.
    /// Power plants set to <see cref="PowerOutputRole.SupplyOutposts"/> split their watts between outposts in range
    /// (see <see cref="WorldComponent_OutpostPowerGrid"/>); an unpowered outpost works at reduced effectiveness.
    /// </summary>
    public static class OutpostPowerGrid
    {
        private static WorldComponent_OutpostPowerGrid cached;

        private static WorldComponent_OutpostPowerGrid Comp
        {
            get
            {
                World world = Find.World;
                if (world == null) return null;
                if (cached == null || cached.world != world)
                    cached = world.GetComponent<WorldComponent_OutpostPowerGrid>();
                return cached;
            }
        }

        public static bool Enabled =>
            WorldDominationMod.settings?.enableOutpostPowerRequirement ?? WorldDominationSettings.DefEnableOutpostPowerRequirement;

        public static float BaseDemandWatts =>
            Mathf.Max(0f, WorldDominationMod.settings?.outpostPowerDemandBase ?? WorldDominationSettings.DefOutpostPowerDemandBase);

        /// <summary>Production / food / research multiplier for an unpowered outpost.</summary>
        public static float UnpoweredEffectiveness =>
            Mathf.Clamp01(WorldDominationMod.settings?.unpoweredEffectiveness ?? WorldDominationSettings.DefUnpoweredEffectiveness);

        /// <summary>Mortar range and recruit/trade radius multiplier for an unpowered outpost.</summary>
        public static float UnpoweredRangeFactor =>
            Mathf.Clamp(WorldDominationMod.settings?.unpoweredRangeFactor ?? WorldDominationSettings.DefUnpoweredRangeFactor, 0.05f, 1f);

        public static float UnpoweredAntiAirAccuracyLoss =>
            Mathf.Clamp01(WorldDominationMod.settings?.unpoweredAntiAirAccuracyLoss ?? WorldDominationSettings.DefUnpoweredAntiAirAccuracyLoss);

        public static float PowerlineRangeBonusTiles =>
            Mathf.Max(0f, WorldDominationMod.settings?.powerlineRangeBonusTiles ?? WorldDominationSettings.DefPowerlineRangeBonusTiles);

        /// <summary>Supply range of a power outpost (shares the food logistics range).</summary>
        public static float SupplyRangeTiles =>
            Mathf.Max(1f, WorldDominationMod.settings?.maxLogisticsRange ?? WorldDominationSettings.DefMaxLogisticsRange);

        /// <summary>Player outposts that draw power. Power plants power themselves; warehouses are handled separately.</summary>
        public static bool RequiresPower(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null || outpost.def == null || outpost.Faction != Faction.OfPlayer) return false;
            if (outpost.IsPowerPlantOutpost) return false;
            // Warehouses only draw power for upgrades that need it (industrial fridges).
            if (Outpost_Production_Utils.IsWarehouseOutpost(outpost.def))
                return outpost.GetBuiltUpgradePowerWattsNeed() > 0f;
            return true;
        }

        public static float GetDemandWatts(WorldObject_WD_Outpost outpost)
        {
            if (!RequiresPower(outpost)) return 0f;
            if (Outpost_Production_Utils.IsWarehouseOutpost(outpost.def))
                return Mathf.Max(0f, outpost.GetBuiltUpgradePowerWattsNeed());
            return Mathf.Max(0f, BaseDemandWatts + outpost.GetBuiltUpgradePowerWattsNeed());
        }

        public static bool IsPowered(WorldObject_WD_Outpost outpost)
        {
            if (!Enabled || !RequiresPower(outpost)) return true;
            return TryGetStatus(outpost, out OutpostPowerStatus status) && status.powered;
        }

        public static bool TryGetStatus(WorldObject_WD_Outpost outpost, out OutpostPowerStatus status)
        {
            status = default;
            if (outpost == null) return false;
            WorldComponent_OutpostPowerGrid comp = Comp;
            return comp != null && comp.TryGetStatus(outpost.ID, out status);
        }

        /// <summary>1 when powered (or power does not apply), otherwise the unpowered multiplier.</summary>
        public static float GetEffectiveness(WorldObject_WD_Outpost outpost) =>
            IsPowered(outpost) ? 1f : UnpoweredEffectiveness;

        public static float GetPlantUsedWatts(WorldObject_WD_Outpost plant) =>
            Comp?.GetPlantUsedWatts(plant?.ID ?? -1) ?? 0f;

        public static int GetPlantServedCount(WorldObject_WD_Outpost plant) =>
            Comp?.GetPlantServedCount(plant?.ID ?? -1) ?? 0;

        /// <summary>True when the tile lies in the current power range: a supplying plant's range, or a live powerline's radius.</summary>
        public static bool IsTileInPowerRange(int tile) => Comp != null && Comp.IsTileCovered(tile);

        /// <summary>Unallocated watts from Smart plants that can reach <paramref name="tile"/> (last allocation).</summary>
        public static float GetAvailableWattsAt(int tile) => Comp?.GetAvailableWattsAt(tile) ?? 0f;

        public static void MarkDirty() => Comp?.MarkDirty();

        /// <summary>Watts each consumer receives from this plant (last allocation).</summary>
        public static void GetOutgoing(int plantId, List<SupplyFlow> into) => Comp?.GetOutgoing(plantId, into);

        /// <summary>Watts this outpost receives, per plant (last allocation).</summary>
        public static void GetIncoming(int consumerId, List<SupplyFlow> into) => Comp?.GetIncoming(consumerId, into);

        /// <summary>Consumers a supplying plant can reach (directly or through a powerline), nearest first.</summary>
        public static void GetRecipients(WorldObject_WD_Outpost plant, List<WorldObject_WD_Outpost> into)
        {
            into.Clear();
            if (plant != null) Comp?.GetRecipients(plant.ID, into);
        }
    }

    /// <summary>
    /// Allocates outpost-grid watts. Plants set to supply outposts share their output with every outpost within the
    /// logistics range (or within the powerline bonus of a powerline that is itself in range of the plant). An outpost
    /// is powered when its full demand can be met, possibly from several plants. Nearest outposts are served first.
    /// </summary>
    public class WorldComponent_OutpostPowerGrid : WorldComponent
    {
        private const int RecomputeIntervalTicks = 600;
        private const float Epsilon = 0.01f;

        private struct Plant
        {
            public WorldObject_WD_Outpost obj;
            public int tile;
            public float remaining;
            public float capacity;
            public bool manual;
        }

        private struct Consumer
        {
            public WorldObject_WD_Outpost obj;
            public float demand;
            public float nearest;
            public float received;
        }

        private struct Source
        {
            public int plantIndex;
            public float dist;
        }

        private readonly Dictionary<int, OutpostPowerStatus> statusByOutpostId = new Dictionary<int, OutpostPowerStatus>();
        private readonly Dictionary<int, float> usedByPlantId = new Dictionary<int, float>();
        private readonly Dictionary<int, int> servedByPlantId = new Dictionary<int, int>();

        private readonly List<Plant> plants = new List<Plant>(8);
        private readonly List<Consumer> consumers = new List<Consumer>(32);
        private readonly List<int> powerlineTiles = new List<int>(8);
        /// <summary>Per plant (same index as <see cref="plants"/>): powerline tiles chained to it.</summary>
        private readonly List<List<int>> plantTowers = new List<List<int>>(8);
        /// <summary>Powerline tiles connected to at least one plant.</summary>
        private readonly List<int> liveTowerTiles = new List<int>(8);
        private readonly List<Source> sourcesScratch = new List<Source>(8);
        private readonly List<SupplyFlow> flows = new List<SupplyFlow>(32);
        private readonly Dictionary<long, int> flowIndex = new Dictionary<long, int>();
        private readonly List<KeyValuePair<int, float>> linkScratch = new List<KeyValuePair<int, float>>(8);

        private bool dirty = true;
        private int nextRecomputeTick;

        public WorldComponent_OutpostPowerGrid(World world) : base(world) { }

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
        }

        public override void WorldComponentTick()
        {
            base.WorldComponentTick();
            int tick = Find.TickManager.TicksGame;
            if (dirty || tick >= nextRecomputeTick)
                Recompute(tick);
        }

        public bool TryGetStatus(int outpostId, out OutpostPowerStatus status)
        {
            EnsureFresh();
            return statusByOutpostId.TryGetValue(outpostId, out status);
        }

        public float GetPlantUsedWatts(int plantId)
        {
            EnsureFresh();
            return usedByPlantId.TryGetValue(plantId, out float v) ? v : 0f;
        }

        public int GetPlantServedCount(int plantId)
        {
            EnsureFresh();
            return servedByPlantId.TryGetValue(plantId, out int v) ? v : 0;
        }

        public void GetOutgoing(int plantId, List<SupplyFlow> into)
        {
            EnsureFresh();
            into.Clear();
            for (int i = 0; i < flows.Count; i++)
            {
                if (flows[i].sourceId == plantId) into.Add(flows[i]);
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

        public void GetRecipients(int plantId, List<WorldObject_WD_Outpost> into)
        {
            EnsureFresh();
            into.Clear();
            int pi = -1;
            for (int p = 0; p < plants.Count; p++)
            {
                if (plants[p].obj.ID == plantId) { pi = p; break; }
            }
            if (pi < 0) return;

            WorldGrid grid = Find.WorldGrid;
            float range = OutpostPowerGrid.SupplyRangeTiles;
            float lineBonus = OutpostPowerGrid.PowerlineRangeBonusTiles;
            for (int c = 0; c < consumers.Count; c++)
            {
                if (TryReach(pi, consumers[c].obj.Tile, range, lineBonus, out _))
                    into.Add(consumers[c].obj);
            }
            int origin = plants[pi].tile;
            into.Sort((a, b) => grid.ApproxDistanceInTiles(origin, a.Tile).CompareTo(grid.ApproxDistanceInTiles(origin, b.Tile)));
        }

        public float GetAvailableWattsAt(int tile)
        {
            EnsureFresh();
            float range = OutpostPowerGrid.SupplyRangeTiles;
            float lineBonus = OutpostPowerGrid.PowerlineRangeBonusTiles;
            float total = 0f;
            for (int p = 0; p < plants.Count; p++)
            {
                if (plants[p].manual || plants[p].remaining <= Epsilon) continue;
                if (TryReach(p, tile, range, lineBonus, out _))
                    total += plants[p].remaining;
            }
            return total;
        }

        /// <summary>Tile is inside a supplying plant's range or a connected powerline's radius.</summary>
        public bool IsTileCovered(int tile)
        {
            EnsureFresh();
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || tile < 0) return false;
            float range = OutpostPowerGrid.SupplyRangeTiles;
            for (int p = 0; p < plants.Count; p++)
            {
                if (grid.ApproxDistanceInTiles(plants[p].tile, tile) <= range) return true;
            }
            float bonus = OutpostPowerGrid.PowerlineRangeBonusTiles;
            for (int i = 0; i < liveTowerTiles.Count; i++)
            {
                if (grid.ApproxDistanceInTiles(liveTowerTiles[i], tile) <= bonus) return true;
            }
            return false;
        }

        private void EnsureFresh()
        {
            if (dirty && Find.TickManager != null)
                Recompute(Find.TickManager.TicksGame);
        }

        private void Recompute(int tick)
        {
            dirty = false;
            nextRecomputeTick = tick + RecomputeIntervalTicks;

            if (!OutpostPowerGrid.Enabled)
            {
                // Rule switched off (possibly mid-game): drop every allocation so no stale status or flow lingers.
                bool hadState = statusByOutpostId.Count > 0 || flows.Count > 0;
                statusByOutpostId.Clear();
                usedByPlantId.Clear();
                servedByPlantId.Clear();
                plants.Clear();
                consumers.Clear();
                powerlineTiles.Clear();
                plantTowers.Clear();
                liveTowerTiles.Clear();
                flows.Clear();
                flowIndex.Clear();
                if (hadState) NotifyOutpostsChanged();
                return;
            }

            CollectPlants();
            CollectPowerlines();
            ConnectPowerlines();
            CollectConsumers();

            var previous = new Dictionary<int, OutpostPowerStatus>(statusByOutpostId);
            statusByOutpostId.Clear();
            usedByPlantId.Clear();
            servedByPlantId.Clear();

            float range = OutpostPowerGrid.SupplyRangeTiles;
            float lineBonus = OutpostPowerGrid.PowerlineRangeBonusTiles;

            // Pass 1: nearest reachable plant per consumer (serving order).
            for (int c = 0; c < consumers.Count; c++)
            {
                Consumer consumer = consumers[c];
                consumer.nearest = float.MaxValue;
                int tile = consumer.obj.Tile;
                for (int p = 0; p < plants.Count; p++)
                {
                    if (TryReach(p, tile, range, lineBonus, out float dist) && dist < consumer.nearest)
                        consumer.nearest = dist;
                }
                consumers[c] = consumer;
            }
            consumers.Sort((a, b) =>
            {
                int cmp = a.nearest.CompareTo(b.nearest);
                return cmp != 0 ? cmp : a.obj.ID.CompareTo(b.obj.ID);
            });

            flows.Clear();
            flowIndex.Clear();
            var consumerByTile = new Dictionary<int, int>(consumers.Count);
            for (int c = 0; c < consumers.Count; c++)
                consumerByTile[consumers[c].obj.Tile.tileId] = c;

            // Pass 2: Manual plants send only what is assigned to a consumer (capped by the plant's watts and reach).
            for (int p = 0; p < plants.Count; p++)
            {
                if (!plants[p].manual) continue;
                SupplyAllocations.GetAssignedLinks(SupplyResource.Power, plants[p].tile, linkScratch);
                for (int k = 0; k < linkScratch.Count; k++)
                {
                    if (!consumerByTile.TryGetValue(linkScratch[k].Key, out int c)) continue;
                    Consumer consumer = consumers[c];
                    if (!TryReach(p, consumer.obj.Tile, range, lineBonus, out _)) continue;
                    Plant plant = plants[p];
                    float give = Mathf.Min(linkScratch[k].Value, plant.remaining);
                    if (give <= Epsilon) continue;
                    plant.remaining -= give;
                    plants[p] = plant;
                    consumer.received += give;
                    consumers[c] = consumer;
                    RecordFlow(plant.obj, consumer.obj, give);
                }
            }

            // Pass 3: fill each consumer's remaining deficit completely from Smart plants, nearest plant first.
            for (int c = 0; c < consumers.Count; c++)
            {
                Consumer consumer = consumers[c];
                float deficit = consumer.demand - consumer.received;

                if (consumer.demand > Epsilon && deficit > Epsilon && consumer.nearest < float.MaxValue)
                {
                    sourcesScratch.Clear();
                    float available = 0f;
                    int tile = consumer.obj.Tile;
                    for (int p = 0; p < plants.Count; p++)
                    {
                        if (plants[p].manual || plants[p].remaining <= Epsilon) continue;
                        if (!TryReach(p, tile, range, lineBonus, out float dist)) continue;
                        sourcesScratch.Add(new Source { plantIndex = p, dist = dist });
                        available += plants[p].remaining;
                    }

                    if (available + Epsilon >= deficit)
                    {
                        sourcesScratch.Sort((a, b) => a.dist.CompareTo(b.dist));
                        float need = deficit;
                        for (int s = 0; s < sourcesScratch.Count && need > Epsilon; s++)
                        {
                            Plant plant = plants[sourcesScratch[s].plantIndex];
                            float take = Mathf.Min(plant.remaining, need);
                            plant.remaining -= take;
                            plants[sourcesScratch[s].plantIndex] = plant;
                            need -= take;
                            RecordFlow(plant.obj, consumer.obj, take);
                        }
                        consumer.received += deficit;
                    }
                }

                consumers[c] = consumer;
                statusByOutpostId[consumer.obj.ID] = new OutpostPowerStatus
                {
                    demand = consumer.demand,
                    supplied = consumer.received,
                    powered = consumer.demand <= Epsilon || consumer.received + Epsilon >= consumer.demand
                };
            }

            ApplyChanges(previous);
        }

        private void RecordFlow(WorldObject_WD_Outpost plant, WorldObject_WD_Outpost consumer, float amount)
        {
            AddTo(usedByPlantId, plant.ID, amount);
            long key = ((long)plant.ID << 32) | (uint)consumer.ID;
            if (flowIndex.TryGetValue(key, out int idx))
            {
                SupplyFlow existing = flows[idx];
                existing.amount += amount;
                flows[idx] = existing;
                return;
            }
            flowIndex[key] = flows.Count;
            flows.Add(new SupplyFlow
            {
                sourceId = plant.ID,
                destId = consumer.ID,
                sourceTile = plant.Tile.tileId,
                destTile = consumer.Tile.tileId,
                amount = amount
            });
            AddTo(servedByPlantId, plant.ID, 1);
        }

        private void ApplyChanges(Dictionary<int, OutpostPowerStatus> previous)
        {
            bool anyChanged = false;
            for (int c = 0; c < consumers.Count; c++)
            {
                WorldObject_WD_Outpost outpost = consumers[c].obj;
                bool nowPowered = statusByOutpostId[outpost.ID].powered;
                bool changed = !previous.TryGetValue(outpost.ID, out OutpostPowerStatus old) || old.powered != nowPowered;
                if (!changed) continue;

                anyChanged = true;
                outpost.InvalidateInspectCache();
                outpost.GetComponent<CompViralSpread>()?.UpdateOutpostStrengthLogically();
            }

            if (anyChanged)
                world.GetComponent<WorldComponent_LogisticsManager>()?.NotifyFoodLogisticsInputsChanged();
        }

        private void CollectPlants()
        {
            plants.Clear();
            IReadOnlyList<WorldObject_WD_Outpost> outposts = WdPlayerOutpostCache.PlayerOutposts;
            for (int i = 0; i < outposts.Count; i++)
            {
                WorldObject_WD_Outpost o = outposts[i];
                if (o == null || o.Destroyed || !o.IsPowerPlantOutpost) continue;
                if (o.PowerRole != PowerOutputRole.SupplyOutposts) continue;
                float watts = Outpost_PowerPlant.GetRemotePowerWatts(o);
                if (watts <= Epsilon) continue;
                plants.Add(new Plant
                {
                    obj = o,
                    tile = o.Tile,
                    remaining = watts,
                    capacity = watts,
                    manual = SupplyAllocations.GetMode(SupplyResource.Power, o.Tile.tileId) == SupplyMode.Manual
                });
            }
        }

        private void CollectPowerlines()
        {
            powerlineTiles.Clear();
            if (OutpostPowerGrid.PowerlineRangeBonusTiles <= 0f) return;
            List<WorldObject> all = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is WorldObject_WD_Powerline line && !line.Destroyed && line.Faction == Faction.OfPlayer)
                    powerlineTiles.Add(line.Tile);
            }
        }

        private void CollectConsumers()
        {
            consumers.Clear();
            IReadOnlyList<WorldObject_WD_Outpost> outposts = WdPlayerOutpostCache.PlayerOutposts;
            for (int i = 0; i < outposts.Count; i++)
            {
                WorldObject_WD_Outpost o = outposts[i];
                if (o == null || o.Destroyed || !OutpostPowerGrid.RequiresPower(o)) continue;
                consumers.Add(new Consumer { obj = o, demand = OutpostPowerGrid.GetDemandWatts(o) });
            }
        }

        /// <summary>
        /// Chains powerlines to each plant: a tower is connected when it is inside the plant's range or inside the
        /// radius of an already connected tower, so the covered area has no gaps.
        /// </summary>
        private void ConnectPowerlines()
        {
            plantTowers.Clear();
            liveTowerTiles.Clear();
            WorldGrid grid = Find.WorldGrid;
            float range = OutpostPowerGrid.SupplyRangeTiles;
            float bonus = OutpostPowerGrid.PowerlineRangeBonusTiles;
            var live = new HashSet<int>();

            for (int p = 0; p < plants.Count; p++)
            {
                var connected = new List<int>();
                plantTowers.Add(connected);
                if (powerlineTiles.Count == 0 || bonus <= 0f) continue;

                var queue = new Queue<int>();
                var seen = new HashSet<int>();
                for (int t = 0; t < powerlineTiles.Count; t++)
                {
                    int tower = powerlineTiles[t];
                    if (grid.ApproxDistanceInTiles(plants[p].tile, tower) <= range && seen.Add(tower))
                        queue.Enqueue(tower);
                }
                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    connected.Add(current);
                    live.Add(current);
                    for (int t = 0; t < powerlineTiles.Count; t++)
                    {
                        int next = powerlineTiles[t];
                        if (!seen.Contains(next) && grid.ApproxDistanceInTiles(current, next) <= bonus)
                        {
                            seen.Add(next);
                            queue.Enqueue(next);
                        }
                    }
                }
            }
            liveTowerTiles.AddRange(live);
        }

        /// <summary>Plant reaches a consumer directly, or through a powerline chained to the plant whose radius covers the consumer.</summary>
        private bool TryReach(int plantIndex, int consumerTile, float range, float lineBonus, out float dist)
        {
            WorldGrid grid = Find.WorldGrid;
            dist = grid.ApproxDistanceInTiles(plants[plantIndex].tile, consumerTile);
            if (dist <= range) return true;
            if (lineBonus <= 0f || plantIndex >= plantTowers.Count) return false;
            List<int> towers = plantTowers[plantIndex];
            for (int i = 0; i < towers.Count; i++)
            {
                if (grid.ApproxDistanceInTiles(towers[i], consumerTile) <= lineBonus)
                    return true;
            }
            return false;
        }

        private static void AddTo(Dictionary<int, float> dict, int key, float amount)
        {
            dict[key] = (dict.TryGetValue(key, out float v) ? v : 0f) + amount;
        }

        private static void AddTo(Dictionary<int, int> dict, int key, int amount)
        {
            dict[key] = (dict.TryGetValue(key, out int v) ? v : 0) + amount;
        }
    }
}
