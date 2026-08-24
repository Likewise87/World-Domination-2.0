using System.Collections.Generic;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// VFEPD-only (Patches/SettlementScatter_Storage_WithVFEPD.xml). KCSG scatter expands
    /// VFE prop categories at runtime via WdPropCategoryScatter + VFEPropsCompat (reflection).
    /// </summary>
    public class WdSettlementScatterPropsExtension : DefModExtension
    {
        public List<string> scatterPropCategories = new List<string>();
    }
}
