using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    [StaticConstructorOnStartup]
    public static class Outpost_Warehouse_Gizmos
    {
        private static readonly Texture2D DeliveryTargetIcon;

        static Outpost_Warehouse_Gizmos()
        {
            DeliveryTargetIcon = Outpost_Warehouse_Delivery.GetDeliveryTargetMouseIcon();
        }

        public static IEnumerable<Gizmo> GetGizmos(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null || outpost.Faction != Faction.OfPlayer) yield break;

            if (Outpost_Production_Utils.IsWarehouseOutpost(outpost.def))
            {
                foreach (var g in GetWarehouseGizmos(outpost))
                    yield return g;
                yield break;
            }

            if (!Outpost_Warehouse_Delivery.UsesItemDeliveryTraveler(outpost.def)) yield break;

            WorldObject deliveryDest = Outpost_Warehouse_Delivery.ResolveDisplayDeliveryTarget(outpost);
            string destLabel = Outpost_Warehouse_Delivery.GetDestinationLabel(deliveryDest);

            yield return new Command_Action
            {
                defaultLabel = deliveryDest != null
                    ? "TSA_WD_Warehouse_DeliveryDestGizmo".Translate(destLabel).ToString()
                    : "TSA_WD_Warehouse_SetDeliveryDest".Translate().ToString(),
                defaultDesc = "TSA_WD_Warehouse_SetDeliveryDestDesc".Translate().ToString(),
                icon = DeliveryTargetIcon,
                action = () => Outpost_Warehouse_Delivery.BeginItemDeliveryDestinationChoice(outpost),
                onHover = () => Outpost_Warehouse_Delivery.DrawHoverOverlayLines(outpost)
            };
        }

        private static IEnumerable<Gizmo> GetWarehouseGizmos(WorldObject_WD_Outpost warehouse)
        {
            var comp = CompOutpostWarehouse.Get(warehouse);
            if (comp == null) yield break;

            foreach (Gizmo g in Outpost_DispatchMode_Gizmos.GetWarehouseGizmos(warehouse))
                yield return g;

            WorldObject shipDest = comp.ResolveShipDestination();
            string destLabel = Outpost_Warehouse_Delivery.GetDestinationLabelWithKind(shipDest);

            yield return new Command_Action
            {
                defaultLabel = shipDest != null
                    ? "TSA_WD_Warehouse_ShipDestGizmo".Translate(destLabel).ToString()
                    : "TSA_WD_Warehouse_SetShipDest".Translate().ToString(),
                defaultDesc = "TSA_WD_Warehouse_SetShipDestDesc".Translate().ToString(),
                icon = DeliveryTargetIcon,
                action = () => Outpost_Warehouse_Delivery.BeginShipDestinationChoice(comp, warehouse),
                onHover = () => Outpost_Warehouse_Delivery.DrawHoverOverlayLines(warehouse)
            };
        }
    }
}
