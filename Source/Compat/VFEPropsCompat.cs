using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Optional integration with VFE Props and Decor (VanillaExpanded.VFEPropsandDecor).
    /// Reflection only — no compile-time reference to VFEProps.dll.
    /// </summary>
    public static class VFEPropsCompat
    {
        public const string PackageId = "VanillaExpanded.VFEPropsandDecor";

        private static bool lookupDone;
        private static bool modLoaded;
        private static Type propDefType;
        private static Type propCategoryDefType;
        private static FieldInfo propDefCategoryField;
        private static FieldInfo propDefCategoriesField;
        private static FieldInfo propDefPropField;
        private static MethodInfo getCategorySilentFail;
        private static PropertyInfo allPropDefs;

        public static bool IsModLoaded
        {
            get
            {
                EnsureLookup();
                return modLoaded;
            }
        }

        public static IEnumerable<ThingDef> ThingDefsInCategory(string categoryDefName)
        {
            if (categoryDefName.NullOrEmpty()) yield break;
            EnsureLookup();
            if (!modLoaded) yield break;

            object category = getCategorySilentFail.Invoke(null, new object[] { categoryDefName });
            if (category == null) yield break;

            var propDefs = allPropDefs.GetValue(null) as IList;
            if (propDefs == null) yield break;

            var seen = new HashSet<ThingDef>();
            for (int i = 0; i < propDefs.Count; i++)
            {
                object propDef = propDefs[i];
                if (propDef == null || !IsInCategory(propDef, category)) continue;

                if (propDefPropField.GetValue(propDef) is ThingDef thingDef && seen.Add(thingDef))
                    yield return thingDef;
            }
        }

        private static bool IsInCategory(object propDef, object category)
        {
            if (Equals(propDefCategoryField.GetValue(propDef), category))
                return true;

            if (propDefCategoriesField.GetValue(propDef) is IList extra)
            {
                for (int i = 0; i < extra.Count; i++)
                {
                    if (Equals(extra[i], category))
                        return true;
                }
            }

            return false;
        }

        private static void EnsureLookup()
        {
            if (lookupDone) return;
            lookupDone = true;

            if (ModLister.GetActiveModWithIdentifier(PackageId, false) == null)
                return;

            propDefType = AccessTools.TypeByName("VFEProps.PropDef");
            propCategoryDefType = AccessTools.TypeByName("VFEProps.PropCategoryDef");
            if (propDefType == null || propCategoryDefType == null)
                return;

            propDefCategoryField = AccessTools.Field(propDefType, "category");
            propDefCategoriesField = AccessTools.Field(propDefType, "categories");
            propDefPropField = AccessTools.Field(propDefType, "prop");
            if (propDefCategoryField == null || propDefCategoriesField == null || propDefPropField == null)
                return;

            getCategorySilentFail = AccessTools.Method(
                typeof(DefDatabase<>).MakeGenericType(propCategoryDefType),
                "GetNamedSilentFail",
                new[] { typeof(string) });

            allPropDefs = AccessTools.Property(
                typeof(DefDatabase<>).MakeGenericType(propDefType),
                "AllDefsListForReading");

            modLoaded = getCategorySilentFail != null && allPropDefs != null;
        }
    }
}
