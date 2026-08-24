using System.Collections.Generic;
using KCSG;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Expands KCSG settlement scatter pools from VFE prop categories when VFEPD is active.
    /// Base settlement XML keeps explicit WD props; Patches/SettlementScatter_Storage_WithVFEPD.xml
    /// swaps those lists for category refs when the optional mod is present.
    /// </summary>
    public static class WdPropCategoryScatter
    {
        public static List<ThingDef> BuildScatterList(SettlementLayoutDef layout, List<ThingDef> explicitDefs)
        {
            var result = explicitDefs != null
                ? new List<ThingDef>(explicitDefs)
                : new List<ThingDef>();

            if (!VFEPropsCompat.IsModLoaded) return result;

            WdSettlementScatterPropsExtension ext = layout?.GetModExtension<WdSettlementScatterPropsExtension>();
            if (ext?.scatterPropCategories == null || ext.scatterPropCategories.Count == 0)
                return result;

            foreach (string categoryName in ext.scatterPropCategories)
            {
                if (string.IsNullOrEmpty(categoryName)) continue;
                foreach (ThingDef thingDef in VFEPropsCompat.ThingDefsInCategory(categoryName))
                {
                    if (thingDef == null || result.Contains(thingDef)) continue;
                    result.Add(thingDef);
                }
            }

            return result;
        }
    }
}
