using RimWorld;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Medieval Overhaul material helpers for C# defaults (scavenging product).
    /// XML costs remap via <c>Common/Patches/MO_WdCosts.xml</c>.
    /// </summary>
    public static class MedievalOverhaulCostsCompat
    {
        public const string MoPackageId = MedievalOverhaulHuntingCompat.MoPackageId;
        public const string ComponentBasicDefName = "DankPyon_ComponentBasic";

        public static bool IsMoActive => ModsConfig.IsActive(MoPackageId);

        /// <summary>Scavenging UI/default product: MO basic component when MO is active, else industrial.</summary>
        public static ThingDef GetScavengingProductDef()
        {
            if (IsMoActive)
            {
                ThingDef basic = DefDatabase<ThingDef>.GetNamedSilentFail(ComponentBasicDefName);
                if (basic != null) return basic;
            }

            return ThingDefOf.ComponentIndustrial;
        }

        /// <summary>True when <paramref name="def"/> is the scavenging sentinel product (vanilla or MO remapped).</summary>
        public static bool IsScavengingProductDef(ThingDef def)
        {
            if (def == null) return false;
            if (def == ThingDefOf.ComponentIndustrial) return true;
            return IsMoActive && def.defName == ComponentBasicDefName;
        }
    }
}
