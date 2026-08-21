using System;
using System.Reflection;
using RimWorld;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Classifies outpost pawns for food demand, storage routing, and UI.</summary>
    public static class OutpostPawnClassificationUtil
    {
        private const string VreSyntheticBodyGeneDefName = "VREA_SyntheticBody";
        private const string VreFoodSuppressedNeedDefName = "VREA_FoodSuppressed";

        private static bool vreAndroidLookupDone;
        private static Func<Pawn, bool> vreIsAndroid;

        public static bool IsMechanoidWorker(Pawn pawn)
        {
            return pawn?.RaceProps != null && pawn.RaceProps.IsMechanoid;
        }

        /// <summary>True when this pawn should count toward virtual outpost food demand and starvation.</summary>
        public static bool ConsumesVirtualFood(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead) return false;
            var race = pawn.RaceProps;
            if (race == null) return false;
            if (!race.EatsFood) return false;
            if (HasSuppressedFoodNeed(pawn)) return false;
            if (IsVreAndroid(pawn)) return false;
            if (pawn.needs?.food == null) return false;
            return true;
        }

        /// <summary>VRE Androids replace Need_Food with Need_FoodSuppressed; needs.food is non-null but never consumes.</summary>
        private static bool HasSuppressedFoodNeed(Pawn pawn)
        {
            Need food = pawn.needs?.food;
            if (food == null) return false;
            string defName = food.def?.defName;
            if (defName == VreFoodSuppressedNeedDefName) return true;
            return food.GetType().Name.IndexOf("FoodSuppressed", StringComparison.Ordinal) >= 0;
        }

        private static bool IsVreAndroid(Pawn pawn)
        {
            if (pawn?.genes == null) return false;
            if (pawn.genes.HasActiveGene(DefDatabase<GeneDef>.GetNamedSilentFail(VreSyntheticBodyGeneDefName)))
                return true;
            TryInitVreAndroidReflection();
            return vreIsAndroid != null && vreIsAndroid(pawn);
        }

        private static void TryInitVreAndroidReflection()
        {
            if (vreAndroidLookupDone) return;
            vreAndroidLookupDone = true;
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type utilsType = asm.GetType("VREAndroids.Utils");
                if (utilsType == null) continue;
                MethodInfo method = utilsType.GetMethod("IsAndroid", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Pawn) }, null);
                if (method == null) continue;
                vreIsAndroid = pawn =>
                {
                    try
                    {
                        return method.Invoke(null, new object[] { pawn }) is bool b && b;
                    }
                    catch
                    {
                        return false;
                    }
                };
                break;
            }
        }
    }
}
