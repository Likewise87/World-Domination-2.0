using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Medieval Overhaul: hunting leatherDef is often an MO hide. Resolve to processed leather for
    /// delivery/UI without referencing the MO assembly (reflection + DefDatabase only).
    /// </summary>
    public static class MedievalOverhaulHuntingCompat
    {
        public const string MoPackageId = "DankPyon.Medieval.Overhaul";

        private static bool isHideLookupDone;
        private static MethodInfo isHideMethod;

        private static readonly Dictionary<int, (ThingDef leather, int perHide)?> Cache =
            new Dictionary<int, (ThingDef, int)?>();

        public static bool IsMoActive => ModsConfig.IsActive(MoPackageId);

        /// <summary>
        /// When MO is active and <paramref name="leatherOrHide"/> is an MO hide, sets
        /// <paramref name="leather"/> to the processed leather product and
        /// <paramref name="leatherPerHide"/> to yield per hide (extension amount or 1).
        /// </summary>
        public static bool TryResolveProcessedLeather(ThingDef leatherOrHide, out ThingDef leather, out int leatherPerHide)
        {
            leather = null;
            leatherPerHide = 1;
            if (!IsMoActive || leatherOrHide == null) return false;

            int key = leatherOrHide.shortHash != 0 ? leatherOrHide.shortHash : leatherOrHide.GetHashCode();
            if (Cache.TryGetValue(key, out var cached))
            {
                if (cached == null) return false;
                leather = cached.Value.leather;
                leatherPerHide = cached.Value.perHide;
                return leather != null;
            }

            if (!IsMoHide(leatherOrHide))
            {
                Cache[key] = null;
                return false;
            }

            if (!TryResolveFromExtension(leatherOrHide, out leather, out leatherPerHide)
                && !TryResolveFromButcherProducts(leatherOrHide, out leather, out _)
                && !TryResolveFromRecipes(leatherOrHide, out leather))
            {
                Cache[key] = null;
                return false;
            }

            // Plan default: process yield 1 unless MO extension supplies processedLeatherAmount.
            if (leatherPerHide < 1) leatherPerHide = 1;
            Cache[key] = (leather, leatherPerHide);
            return true;
        }

        /// <summary>Leather ThingDef for hunting UI/delivery: processed leather when MO hide, else original.</summary>
        public static ThingDef ResolveHuntingLeatherProduct(ThingDef leatherOrHide)
        {
            if (TryResolveProcessedLeather(leatherOrHide, out ThingDef leather, out _))
                return leather;
            return leatherOrHide;
        }

        /// <summary>Delivery count: hide units × leather per hide when remapped; else unchanged.</summary>
        public static int ScaleLeatherDeliveryCount(ThingDef leatherOrHide, int hideOrLeatherCount)
        {
            if (hideOrLeatherCount <= 0) return 0;
            if (!TryResolveProcessedLeather(leatherOrHide, out _, out int perHide))
                return hideOrLeatherCount;
            return Mathf.Max(0, hideOrLeatherCount * perHide);
        }

        /// <summary>True when product matches race leatherDef or its MO processed leather.</summary>
        public static bool MatchesLeatherProduct(ThingDef raceLeatherDef, ThingDef product)
        {
            if (raceLeatherDef == null || product == null) return false;
            if (raceLeatherDef == product) return true;
            return TryResolveProcessedLeather(raceLeatherDef, out ThingDef leather, out _) && leather == product;
        }

        private static bool IsMoHide(ThingDef def)
        {
            if (def?.defName != null && def.defName.StartsWith("DankPyon_Hide", StringComparison.Ordinal))
                return true;

            if (def?.thingCategories != null)
            {
                for (int i = 0; i < def.thingCategories.Count; i++)
                {
                    ThingCategoryDef cat = def.thingCategories[i];
                    if (cat != null && cat.defName == "DankPyon_Hides")
                        return true;
                }
            }

            EnsureIsHideMethod();
            if (isHideMethod != null)
            {
                try
                {
                    object result = isHideMethod.Invoke(null, new object[] { def });
                    if (result is bool b) return b;
                }
                catch
                {
                    // Soft-fail: defName / category already checked.
                }
            }

            return false;
        }

        private static void EnsureIsHideMethod()
        {
            if (isHideLookupDone) return;
            isHideLookupDone = true;
            try
            {
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type gen = null;
                    try { gen = asm.GetType("MedievalOverhaul.ThingDefGenerator_Hide", throwOnError: false); }
                    catch { continue; }
                    MethodInfo mi = gen?.GetMethod("IsHide", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (mi == null) continue;
                    isHideMethod = mi;
                    return;
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[WD] MO hide IsHide lookup soft-fail: {ex.Message}");
            }
        }

        private static bool TryResolveFromExtension(ThingDef hide, out ThingDef leather, out int amount)
        {
            leather = null;
            amount = 1;
            List<DefModExtension> exts = hide.modExtensions;
            if (exts == null) return false;

            for (int i = 0; i < exts.Count; i++)
            {
                DefModExtension ext = exts[i];
                if (ext == null) continue;
                Type t = ext.GetType();
                if (t.Namespace == null || t.Namespace.IndexOf("MedievalOverhaul", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                ThingDef fromField = ReadThingDefField(ext, t, "leatherDef")
                    ?? ReadThingDefField(ext, t, "processedLeather")
                    ?? ReadThingDefField(ext, t, "leather");
                if (fromField == null) continue;

                leather = fromField;
                amount = ReadIntField(ext, t, "processedLeatherAmount", 1);
                if (amount < 1) amount = 1;
                return true;
            }

            return false;
        }

        private static bool TryResolveFromButcherProducts(ThingDef hide, out ThingDef leather, out int amount)
        {
            leather = null;
            amount = 1;
            List<ThingDefCountClass> products = hide.butcherProducts;
            if (products == null) return false;

            for (int i = 0; i < products.Count; i++)
            {
                ThingDefCountClass tc = products[i];
                if (tc?.thingDef == null || tc.count <= 0) continue;
                if (!IsLeatherish(tc.thingDef)) continue;
                leather = tc.thingDef;
                // Identity only here — delivery yield stays plan default (1) unless extension set it.
                amount = 1;
                return true;
            }

            return false;
        }

        private static bool TryResolveFromRecipes(ThingDef hide, out ThingDef leather)
        {
            leather = null;
            List<RecipeDef> recipes = DefDatabase<RecipeDef>.AllDefsListForReading;
            for (int i = 0; i < recipes.Count; i++)
            {
                RecipeDef recipe = recipes[i];
                if (recipe?.products == null || recipe.products.Count == 0) continue;
                if (!RecipeTakesHide(recipe, hide)) continue;

                for (int p = 0; p < recipe.products.Count; p++)
                {
                    ThingDefCountClass prod = recipe.products[p];
                    if (prod?.thingDef == null || prod.count <= 0) continue;
                    if (!IsLeatherish(prod.thingDef)) continue;
                    leather = prod.thingDef;
                    return true;
                }
            }

            return false;
        }

        private static bool RecipeTakesHide(RecipeDef recipe, ThingDef hide)
        {
            if (recipe.ingredients != null)
            {
                for (int i = 0; i < recipe.ingredients.Count; i++)
                {
                    IngredientCount ing = recipe.ingredients[i];
                    if (ing?.filter != null && ing.filter.Allows(hide))
                        return true;
                }
            }

            if (recipe.fixedIngredientFilter != null && recipe.fixedIngredientFilter.Allows(hide))
                return true;

            return false;
        }

        private static bool IsLeatherish(ThingDef def)
        {
            if (def?.defName == null) return false;
            if (def.defName.StartsWith("Leather_", StringComparison.Ordinal)) return true;
            if (def.defName.StartsWith("DankPyon_Leather_", StringComparison.Ordinal)) return true;
            if (def.IsLeather) return true;
            return false;
        }

        private static ThingDef ReadThingDefField(object ext, Type t, string name)
        {
            FieldInfo f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null && typeof(ThingDef).IsAssignableFrom(f.FieldType))
                return f.GetValue(ext) as ThingDef;
            PropertyInfo p = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (p != null && typeof(ThingDef).IsAssignableFrom(p.PropertyType))
                return p.GetValue(ext) as ThingDef;
            return null;
        }

        private static int ReadIntField(object ext, Type t, string name, int fallback)
        {
            FieldInfo f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null)
            {
                object v = f.GetValue(ext);
                if (v is int i) return i;
            }

            PropertyInfo p = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (p != null)
            {
                object v = p.GetValue(ext);
                if (v is int i) return i;
            }

            return fallback;
        }
    }
}
