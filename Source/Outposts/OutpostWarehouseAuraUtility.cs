using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Non-stacking warehouse productivity aura: best player warehouse in range grants a % production bonus
    /// to eligible receivers (goods path except research; virtual food; academy XP).
    /// </summary>
    public static class OutpostWarehouseAuraUtility
    {
        private static int cacheTick = -1;
        private static int cacheGeneration;
        private static readonly Dictionary<int, CachedAura> cacheByReceiverId = new Dictionary<int, CachedAura>(64);

        private struct CachedAura
        {
            public int generation;
            public float bonus;
            public int warehouseId;
        }

        public static void InvalidateCache()
        {
            cacheGeneration++;
            cacheByReceiverId.Clear();
            cacheTick = -1;
            Find.World?.GetComponent<WorldComponent_LogisticsManager>()?.NotifyFoodLogisticsInputsChanged();
        }

        public static bool ReceiverCanGetWarehouseAura(WorldObject_WD_Outpost receiver)
        {
            if (receiver == null || receiver.Destroyed) return false;
            if (Outpost_Production_Utils.IsWarehouseOutpost(receiver.def)) return false;
            if (receiver.IsResearchOutpost) return false;
            return OutpostExpertUtility.OutpostHasProductionBonusPath(receiver);
        }

        public static float GetWarehouseAuraRadiusTiles(WorldObject_WD_Outpost warehouse)
        {
            if (warehouse == null || !Outpost_Production_Utils.IsWarehouseOutpost(warehouse.def))
                return 0f;
            var s = WorldDominationMod.settings;
            float r = s?.warehouseAuraRadiusTiles ?? WorldDominationSettings.DefWarehouseAuraRadiusTiles;
            r += warehouse.GetWarehouseAuraRadiusUpgradeBonus();
            return Mathf.Max(0f, r);
        }

        public static float GetWarehouseAuraBonusFraction(WorldObject_WD_Outpost warehouse)
        {
            if (warehouse == null || !Outpost_Production_Utils.IsWarehouseOutpost(warehouse.def))
                return 0f;
            var s = WorldDominationMod.settings;
            float b = s?.warehouseAuraBonusPct ?? WorldDominationSettings.DefWarehouseAuraBonusPct;
            b += warehouse.GetWarehouseAuraBonusUpgradeBonus();
            return Mathf.Max(0f, b);
        }

        /// <summary>Best aura % for <paramref name="receiver"/>, or 0.</summary>
        public static float GetBestWarehouseAuraBonus(WorldObject_WD_Outpost receiver)
            => TryGetBestWarehouseAura(receiver, out _, out float bonus) ? bonus : 0f;

        public static bool TryGetBestWarehouseAura(
            WorldObject_WD_Outpost receiver,
            out WorldObject_WD_Outpost warehouse,
            out float bonus)
        {
            warehouse = null;
            bonus = 0f;
            if (!ReceiverCanGetWarehouseAura(receiver)) return false;

            int tick = Find.TickManager?.TicksGame ?? 0;
            if (tick != cacheTick)
            {
                cacheTick = tick;
                cacheByReceiverId.Clear();
            }

            if (cacheByReceiverId.TryGetValue(receiver.ID, out CachedAura cached)
                && cached.generation == cacheGeneration)
            {
                bonus = cached.bonus;
                if (cached.warehouseId >= 0)
                {
                    var all = Find.WorldObjects?.AllWorldObjects;
                    if (all != null)
                    {
                        for (int i = 0; i < all.Count; i++)
                        {
                            if (all[i] is WorldObject_WD_Outpost op && op.ID == cached.warehouseId)
                            {
                                warehouse = op;
                                break;
                            }
                        }
                    }
                }
                return bonus > 1e-6f;
            }

            float best = 0f;
            WorldObject_WD_Outpost bestWh = null;
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            var worldObjects = Find.WorldObjects?.AllWorldObjects;
            if (worldObjects != null)
            {
                for (int i = 0; i < worldObjects.Count; i++)
                {
                    if (!(worldObjects[i] is WorldObject_WD_Outpost wh) || wh.Destroyed) continue;
                    if (!Outpost_Production_Utils.IsWarehouseOutpost(wh.def)) continue;
                    if (wh.Faction == null || !wh.Faction.IsPlayer) continue;

                    float aura = GetWarehouseAuraBonusFraction(wh);
                    if (aura <= 1e-6f) continue;
                    float radius = GetWarehouseAuraRadiusTiles(wh);
                    if (radius <= 0f) continue;

                    float dist = manager != null
                        ? WorldActions_Utils.GetDistance(receiver.Tile, wh.Tile, manager)
                        : Find.WorldGrid.ApproxDistanceInTiles(receiver.Tile, wh.Tile);
                    if (dist > radius) continue;
                    if (aura <= best) continue;
                    best = aura;
                    bestWh = wh;
                }
            }

            cacheByReceiverId[receiver.ID] = new CachedAura
            {
                generation = cacheGeneration,
                bonus = best,
                warehouseId = bestWh?.ID ?? -1
            };
            warehouse = bestWh;
            bonus = best;
            return best > 1e-6f;
        }

        /// <summary>Experts + best warehouse as additive fraction (0.15 = +15%).</summary>
        public static float GetExpertAndWarehouseProductionBonusFraction(WorldObject_WD_Outpost outpost)
        {
            float f = 0f;
            if (outpost != null && OutpostExpertUtility.OutpostHasProductionBonusPath(outpost))
                f += OutpostExpertUtility.GetCombinedProductionBonus(outpost);
            f += GetBestWarehouseAuraBonus(outpost);
            return f;
        }

        /// <summary>1 + experts + warehouse. Used by virtual food and academy XP.</summary>
        public static float GetSoftProductionBonusMultiplier(WorldObject_WD_Outpost outpost)
            => Mathf.Max(0.01f, 1f + GetExpertAndWarehouseProductionBonusFraction(outpost));
    }
}
