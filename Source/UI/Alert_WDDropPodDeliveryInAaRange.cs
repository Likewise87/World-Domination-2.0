using System.Collections.Generic;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Right-side alert when a player warehouse in drop-pod dispatch mode sits within enemy AA turret range
    /// (origin tile only; no path/arc scan). Toggled by notifyDropPodDeliveryInAaRange.
    /// </summary>
    public class Alert_WDDropPodDeliveryInAaRange : Alert
    {
        private readonly List<WorldObject> culprits = new List<WorldObject>(8);
        private readonly StringBuilder explanationScratch = new StringBuilder();
        private string labelCached;

        public Alert_WDDropPodDeliveryInAaRange()
        {
            defaultPriority = AlertPriority.Medium;
        }

        public override string GetLabel() => labelCached ??= "TSA_WD_Alert_DropPodDeliveryInAaRange".Translate();

        public override TaggedString GetExplanation()
        {
            CollectCulprits(culprits);
            if (culprits.Count == 0) return "";

            explanationScratch.Clear();
            explanationScratch.AppendLine("TSA_WD_Alert_DropPodDeliveryInAaRangeDescHeader".Translate());
            for (int i = 0; i < culprits.Count; i++)
            {
                WorldObject wh = culprits[i];
                if (wh == null) continue;
                explanationScratch.AppendLine("TSA_WD_Alert_DropPodDeliveryInAaRangeDescLine".Translate(wh.LabelCap));
            }
            return explanationScratch.ToString().TrimEnd();
        }

        public override AlertReport GetReport()
        {
            if (Current.ProgramState != ProgramState.Playing) return false;
            if (WorldDominationMod.settings == null || !WorldDominationMod.settings.notifyDropPodDeliveryInAaRange)
                return false;

            CollectCulprits(culprits);
            if (culprits.Count == 0) return false;
            WorldObject first = culprits[0];
            return first != null && !first.Destroyed ? AlertReport.CulpritIs(first) : AlertReport.Active;
        }

        /// <summary>
        /// Cheap origin-vs-T4 AA range check for warehouses with drop-pod mode. Early outs when AA is inactive.
        /// </summary>
        public static void CollectCulprits(List<WorldObject> into)
        {
            into?.Clear();
            if (into == null) return;

            var seth = WorldDominationMod.settings;
            if (!(seth?.enableNpcT4AntiAir ?? WorldDominationSettings.DefEnableNpcT4AntiAir))
                return;

            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (!WdEscalation.CanTargetPlayerWithT4AntiAir(seth, WdEscalation.GetCachedStage(manager)))
                return;

            if (Find.WorldObjects == null) return;

            float aaRange = AntiAirFireUtils.GetNpcAntiAirMaxRangeTiles();
            var buildings = WorldComponent_AdvancedBuildings.Get()?.All;
            if (buildings == null || buildings.Count == 0) return;

            IReadOnlyList<WorldObject_WD_Outpost> outposts = WdPlayerOutpostCache.PlayerOutposts;
            for (int i = 0; i < outposts.Count; i++)
            {
                WorldObject_WD_Outpost warehouse = outposts[i];
                if (warehouse == null || warehouse.Destroyed) continue;
                if (!Outpost_Production_Utils.IsWarehouseOutpost(warehouse.def)) continue;

                CompOutpostWarehouse whComp = CompOutpostWarehouse.Get(warehouse);
                if (whComp == null || !whComp.dispatchViaDropPod) continue;

                int warehouseTile = warehouse.Tile.tileId;
                if (warehouseTile < 0) continue;

                for (int s = 0; s < buildings.Count; s++)
                {
                    if (!(buildings[s] is WorldObject_WD_AaTurret aa) || aa.Destroyed || !aa.Tile.Valid) continue;
                    if (aa.Faction == null || aa.Faction.IsPlayer) continue;
                    if (!WorldActions_Utils.SafeHostileTo(aa.Faction, Faction.OfPlayer)) continue;
                    if (!aa.IsFiringEnabled) continue;

                    int aaTile = aa.Tile.tileId;
                    float dist = manager != null
                        ? WorldActions_Utils.GetDistance(warehouseTile, aaTile, manager)
                        : Find.WorldGrid.ApproxDistanceInTiles(warehouseTile, aaTile);
                    if (dist > aaRange) continue;

                    into.Add(warehouse);
                    break;
                }
            }
        }
    }
}
