using RimWorld;
using Verse;

namespace TSA_WorldDomination
{
    [DefOf]
    public static class WD_BuildingDefOf
    {
        public static ThingDef TSA_WD_OutpostDeliverySpot = null!;
        /// <summary>1.7-only building; null if Def missing.</summary>
        public static ThingDef TSA_WD_RemotePowerSpot = null!;

        static WD_BuildingDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(WD_BuildingDefOf));
        }
    }
}
