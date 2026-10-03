using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// The only place stored physical food becomes virtual food. Arrival paths never convert; the
    /// player converts picked rows from the Warehouse tab or everything from the Armory dialog.
    /// </summary>
    public static class OutpostFoodConversion
    {
        public static bool CanConvert(WorldObject_WD_Outpost outpost) =>
            outpost != null && !outpost.Destroyed && !outpost.ManualDefenseActive
            && outpost.GetComponent<CompOutpostLogistics>() != null
            && CompOutpostArmory.Get(outpost) != null;

        public static float PoolHeadroom(WorldObject_WD_Outpost outpost)
        {
            var logi = outpost?.GetComponent<CompOutpostLogistics>();
            return logi == null ? 0f : Mathf.Max(0f, logi.EffectiveMaxFood - logi.currentFood);
        }

        /// <summary>
        /// Converts up to each pick's count of nutrition rows, clamped to pool headroom. Non-food picks
        /// are skipped. Whatever does not fit stays stored. <paramref name="converted"/> lists the
        /// counts actually taken per pick so callers can shrink their selection.
        /// </summary>
        public static float ConvertRows(
            WorldObject_WD_Outpost outpost,
            List<ThingDefCountClass> picks,
            out List<ThingDefCountClass> converted)
        {
            converted = new List<ThingDefCountClass>();
            if (!CanConvert(outpost) || picks == null) return 0f;

            var logi = outpost.GetComponent<CompOutpostLogistics>();
            var armory = CompOutpostArmory.Get(outpost);
            float applied = 0f;

            for (int i = 0; i < picks.Count; i++)
            {
                ThingDefCountClass pick = picks[i];
                if (pick?.thingDef == null || pick.count <= 0) continue;
                if (!Outpost_Warehouse_Delivery.IsNutritionGivingStock(pick.thingDef)) continue;

                float per = pick.thingDef.GetStatValueAbstract(StatDefOf.Nutrition);
                if (per <= 0f) continue;

                float headroom = Mathf.Max(0f, logi.EffectiveMaxFood - logi.currentFood);
                if (headroom <= 0.01f) break;

                int affordable = Mathf.Min(pick.count, Mathf.FloorToInt(headroom / per));
                if (affordable <= 0) continue;

                int taken = armory.WithdrawUpToMatching(pick, affordable);
                if (taken <= 0) continue;

                applied += CompOutpostLogistics.AddVirtualFoodNutrition(logi, per * taken);
                converted.Add(CompOutpostWarehouse.PlainStockRow(pick, taken));
            }
            return applied;
        }

        /// <summary>Every stored food row, for the Armory dialog's convert-all button.</summary>
        public static List<ThingDefCountClass> AllFoodRows(WorldObject_WD_Outpost outpost)
        {
            var result = new List<ThingDefCountClass>();
            var armory = CompOutpostArmory.Get(outpost);
            if (armory == null) return result;
            List<ThingDefCountClass> rows = armory.ArmoryRows();
            for (int i = 0; i < rows.Count; i++)
            {
                ThingDefCountClass e = rows[i];
                if (!Outpost_Warehouse_Delivery.IsNutritionGivingStock(e.thingDef)) continue;
                result.Add(CompOutpostWarehouse.PlainStockRow(e, e.count));
            }
            return result;
        }

        public static float StoredNutrition(WorldObject_WD_Outpost outpost)
        {
            var armory = CompOutpostArmory.Get(outpost);
            if (armory == null) return 0f;
            float sum = 0f;
            List<ThingDefCountClass> rows = armory.ArmoryRows();
            for (int i = 0; i < rows.Count; i++)
            {
                ThingDefCountClass e = rows[i];
                if (!Outpost_Warehouse_Delivery.IsNutritionGivingStock(e.thingDef)) continue;
                sum += e.thingDef.GetStatValueAbstract(StatDefOf.Nutrition) * e.count;
            }
            return sum;
        }
    }
}
