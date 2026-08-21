using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Per-origin land vs drop-pod dispatch preference (colony settlements and warehouses).</summary>
    public static class OutpostDispatchMode
    {
        public static bool GetViaDropPod(WorldObject origin)
        {
            if (origin == null) return false;
            if (origin is WorldObject_WD_Outpost outpost)
            {
                CompOutpostWarehouse wh = CompOutpostWarehouse.Get(outpost);
                if (wh != null)
                    return wh.dispatchViaDropPod;
            }

            return CompPlayerDispatchMode.Get(origin)?.dispatchViaDropPod ?? false;
        }

        public static void SetViaDropPod(WorldObject origin, bool viaDropPod)
        {
            if (origin == null) return;
            if (origin is WorldObject_WD_Outpost outpost)
            {
                CompOutpostWarehouse wh = CompOutpostWarehouse.Get(outpost);
                if (wh != null)
                {
                    wh.dispatchViaDropPod = viaDropPod;
                    return;
                }
            }

            CompPlayerDispatchMode colony = CompPlayerDispatchMode.Get(origin);
            if (colony != null)
                colony.dispatchViaDropPod = viaDropPod;
        }

        /// <summary>Sets pod mode only when Transport Pods is researched; otherwise shows reject message.</summary>
        public static bool TrySetViaDropPod(WorldObject origin, bool viaDropPod)
        {
            if (viaDropPod && !RapidResponseUtility.TransportPodsResearched())
            {
                Messages.Message("TSA_WD_RapidResponse_DropPodsNeedResearch".Translate(), MessageTypeDefOf.RejectInput);
                return false;
            }

            SetViaDropPod(origin, viaDropPod);
            return true;
        }

        public static bool IsPlayerCargoDropPod(WorldObject target)
        {
            if (target is WorldObject_Traveler_Outpost_Delivery delivery && delivery.deliveryViaDropPod)
                return true;
            if (target is WorldObject_Traveler_Outpost_Upgrade upgrade && upgrade.upgradeViaDropPod)
                return true;
            if (target is WorldObject_Traveler_TradePayment trade && trade.tradeViaDropPod)
                return true;
            return false;
        }
    }
}
