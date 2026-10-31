using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Soft pre-establish check: warns (one combined popup) when the tile cannot supply the new outpost's power, food or tooling.
    /// The player may still continue.
    /// </summary>
    public static class Outpost_EstablishmentSupplyCheck
    {
        private const float Epsilon = 0.01f;

        public static List<string> GetWarnings(int tile, WorldObjectDef def, IReadOnlyList<Pawn> pawns)
        {
            var warnings = new List<string>(3);
            if (def == null || tile < 0 || Find.WorldGrid == null) return warnings;

            if (LacksPower(tile, def))
                warnings.Add("TSA_WD_Supply_NoPower".Translate());
            if (LacksFood(tile, def, pawns))
                warnings.Add("TSA_WD_Supply_NoFood".Translate());
            if (LacksTooling(tile, def, pawns))
                warnings.Add("TSA_WD_Supply_NoTooling".Translate());
            return warnings;
        }

        /// <summary>Runs <paramref name="proceed"/> straight away when nothing is short, otherwise after the player confirms.</summary>
        public static void ConfirmThen(int tile, WorldObjectDef def, IReadOnlyList<Pawn> pawns, Action proceed, Action cancel = null)
        {
            List<string> warnings = GetWarnings(tile, def, pawns);
            if (warnings.Count == 0)
            {
                proceed();
                return;
            }
            Find.WindowStack.Add(new Dialog_MessageBox(
                string.Join("\n\n", warnings),
                "TSA_WD_Supply_Continue".Translate(),
                proceed,
                "TSA_WD_Supply_Cancel".Translate(),
                cancel,
                title: null,
                buttonADestructive: false,
                acceptAction: null,
                cancelAction: cancel));
        }

        private static bool LacksPower(int tile, WorldObjectDef def)
        {
            if (!OutpostPowerGrid.Enabled) return false;
            // Power plants power themselves; warehouses only draw power for upgrades.
            if (Outpost_Production_Utils.IsPowerPlantOutpost(def) || Outpost_Production_Utils.IsWarehouseOutpost(def)) return false;
            float demand = OutpostPowerGrid.BaseDemandWatts;
            if (demand <= Epsilon) return false;
            return OutpostPowerGrid.GetAvailableWattsAt(tile) + Epsilon < demand;
        }

        private static bool LacksFood(int tile, WorldObjectDef def, IReadOnlyList<Pawn> pawns)
        {
            // Food producers feed themselves.
            if (Outpost_Production_Utils.IsFoodProducerOutpost(def)) return false;
            var s = WorldDominationMod.settings;
            if (s == null || pawns == null) return false;

            int eaters = 0;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p != null && !p.Dead && OutpostPawnClassificationUtil.ConsumesVirtualFood(p)) eaters++;
            }
            float demand = eaters * s.foodConsumptionPerPawn;
            if (demand <= Epsilon) return false;

            float available = s.foodProductionPerOutpostBase;
            var manager = Find.World?.GetComponent<WorldComponent_LogisticsManager>();
            if (manager != null)
            {
                WorldGrid grid = Find.WorldGrid;
                IReadOnlyList<(WorldObject Obj, CompOutpostLogistics Logi)> nodes = manager.GetCachedPlayerLogisticsNodes();
                for (int i = 0; i < nodes.Count; i++)
                {
                    WorldObject node = nodes[i].Obj;
                    if (node == null || node.Destroyed || !Outpost_Production_Utils.IsFoodProducerOutpost(node.def)) continue;
                    if (grid.ApproxDistanceInTiles(tile, node.Tile) > s.maxLogisticsRange) continue;
                    float budget = manager.GetFoodExportBudget(node);
                    if (budget <= Epsilon) continue;
                    available += Mathf.Min(budget, Mathf.Max(0f, manager.GetLogisticsNetDailyForOutpost(node)));
                }
            }
            return available + Epsilon < demand;
        }

        private static bool LacksTooling(int tile, WorldObjectDef def, IReadOnlyList<Pawn> pawns)
        {
            if (!OutpostTooling.Enabled || !OutpostTooling.RequiresToolingDef(def)) return false;
            int occupants = 0;
            if (pawns != null)
            {
                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn p = pawns[i];
                    if (p != null && !p.Dead && p.RaceProps?.Humanlike == true) occupants++;
                }
            }
            float demand = OutpostTooling.EstimateDemand(def, occupants);
            return OutpostTooling.EstimateAvailableAt(tile) + Epsilon < demand;
        }
    }
}
