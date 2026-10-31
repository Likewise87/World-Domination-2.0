using RimWorld;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// How much virtual tooling one unit of an item is worth, relative to the others (wood = 1, steel = 2, plasteel = 3 by default).
    /// Add more in XML (for example a pickaxe worth 50 wood). Players can also override values or add items in the mod settings.
    /// </summary>
    public class WdToolingValueDef : Def
    {
        public ThingDef thingDef;

        /// <summary>Tooling units per item. Relative ratio, 0 disables the item.</summary>
        public float value = 1f;

        public override System.Collections.Generic.IEnumerable<string> ConfigErrors()
        {
            foreach (string e in base.ConfigErrors())
                yield return e;
            if (thingDef == null)
                yield return "WdToolingValueDef " + defName + " has no thingDef.";
            if (value < 0f)
                yield return "WdToolingValueDef " + defName + " has a negative value.";
        }
    }
}
