using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Combat Extended: passive basic ammo for outpost Occupants (reflection only; no CE assembly reference).
    /// Grants <see cref="MagazinesPerDay"/> magazines per day of the Primary weapon's basic ammo, capped at <see cref="MagazineCap"/>.
    /// </summary>
    public static class OutpostCeAmmoCompat
    {
        private const string CePackageId = "CETeam.CombatExtended";
        private const string CompAmmoUserSimpleName = "CompAmmoUser";
        private const string CompVariableAmmoUserSimpleName = "CompVariableAmmoUser";
        private const string CompInventoryTypeName = "CombatExtended.CompInventory";
        private const string AmmoDefSimpleName = "AmmoDef";

        private const BindingFlags Inst =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>Magazines granted per in-game day when under the cap.</summary>
        public const int MagazinesPerDay = 2;

        /// <summary>Stop granting when inventory holds this many magazines of the basic ammo.</summary>
        public const int MagazineCap = 8;

        /// <summary>Armory auto-assign helper: magazines to give per pawn from store stock.</summary>
        public const int AutoAssignMagazines = 4;

        private static bool inventoryLookupDone;
        private static Type compInventoryType;
        private static MethodInfo canFitInInventoryDef;
        private static MethodInfo canFitInInventoryThing;
        private static MethodInfo updateInventory;
        private static PropertyInfo availableBulkProp;
        private static PropertyInfo availableWeightProp;
        private static PropertyInfo currentWeightProp;
        private static PropertyInfo capacityWeightProp;
        private static PropertyInfo currentBulkProp;
        private static PropertyInfo capacityBulkProp;

        /// <summary>True when Combat Extended is active (package id only; types resolved on use).</summary>
        public static bool IsCeActive => ModsConfig.IsActive(CePackageId);

        /// <summary>
        /// Once per in-game day: for each humanlike Occupant with a CE-ranged Primary, add up to
        /// <see cref="MagazinesPerDay"/> magazines of basic ammo into inventory (cap <see cref="MagazineCap"/>).
        /// No-op when CE is off or the experimental setting is off.
        /// </summary>
        public static void TickOccupantsPassiveAmmoOneDay(WorldObject_WD_Outpost outpost)
        {
            if (outpost?.Occupants == null || outpost.Occupants.Count == 0) return;
            if (!IsCeActive) return;

            WorldDominationSettings settings = WorldDominationMod.settings;
            if (settings == null || !settings.experimentalOutpostCePassiveAmmo) return;

            bool any = false;
            List<Pawn> occupants = outpost.Occupants;
            for (int i = 0; i < occupants.Count; i++)
            {
                try
                {
                    if (TryGrantPassiveAmmoForPawn(occupants[i]))
                        any = true;
                }
                catch (Exception ex)
                {
                    Log.Warning($"[WD] CE outpost passive ammo soft-fail: {ex.Message}");
                }
            }

            if (any)
                outpost.NotifyVirtualPawnsChanged();
        }

        private static bool TryGrantPassiveAmmoForPawn(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead) return false;
            if (pawn.RaceProps == null || !pawn.RaceProps.Humanlike) return false;
            if (pawn.equipment?.Primary == null) return false;

            ThingWithComps primary = pawn.equipment.Primary;
            if (primary.def == null || !primary.def.IsRangedWeapon) return false;

            ThingComp ammoUser = FindAmmoUserComp(primary);
            if (ammoUser == null) return false;

            if (!IsUseAmmo(ammoUser)) return false;

            object props = ammoUser.props;
            if (props == null) return false;

            object ammoSet = GetFieldValue(props, "ammoSet");
            if (ammoSet == null) return false;

            ThingDef basicAmmo = ResolveBasicAmmoDef(ammoSet);
            if (basicAmmo == null) return false;

            int oneMagRounds = ResolveOneMagazineRounds(props, basicAmmo);
            if (oneMagRounds <= 0) return false;

            int have = CountInventoryOfDef(pawn, basicAmmo);
            int capRounds = oneMagRounds * MagazineCap;
            if (have >= capRounds) return false;

            int want = Math.Min(oneMagRounds * MagazinesPerDay, capRounds - have);
            if (want <= 0) return false;

            want = ClampToInventoryFit(pawn, basicAmmo, want);
            if (want <= 0) return false;

            return TryAddAmmoToInventory(pawn, basicAmmo, want);
        }

        /// <summary>CE ammo defs use the CombatExtended.AmmoDef class; matched by name to avoid a hard reference.</summary>
        internal static bool IsCeAmmoDef(ThingDef def)
        {
            if (def == null || !IsCeActive) return false;
            Type t = def.GetType();
            while (t != null)
            {
                if (t.Name == AmmoDefSimpleName) return true;
                t = t.BaseType;
            }
            return false;
        }

        /// <summary>
        /// Empties a CE weapon's magazine into <paramref name="credit"/> so the weapon itself stays an
        /// ordinary stacking item. No-op without CE or when the magazine is already empty.
        /// </summary>
        internal static void TryUnloadMagazineInto(Thing thing, Action<ThingDefCountClass> credit)
        {
            if (!IsCeActive || credit == null) return;
            if (!(thing is ThingWithComps gun)) return;

            try
            {
                ThingComp ammoUser = FindAmmoUserComp(gun);
                if (ammoUser == null) return;

                int magCount = GetIntProperty(ammoUser, "CurMagCount", 0);
                if (magCount <= 0) return;

                ThingDef loaded = GetPropertyValue(ammoUser, "CurrentAmmo") as ThingDef;
                if (loaded == null)
                {
                    object props = ammoUser.props;
                    object ammoSet = props != null ? GetFieldValue(props, "ammoSet") : null;
                    loaded = ammoSet != null ? ResolveBasicAmmoDef(ammoSet) : null;
                }
                if (loaded == null) return;

                int ammoCount = GetIntField(loaded, "ammoCount", 1);
                if (ammoCount < 1) ammoCount = 1;
                int items = magCount / ammoCount;
                if (items <= 0) return;

                credit(new ThingDefCountClass(loaded, items));
                TrySetIntProperty(ammoUser, "CurMagCount", 0);
            }
            catch (Exception ex)
            {
                Log.Warning($"[WD] CE magazine unload soft-fail: {ex.Message}");
            }
        }

        internal static ThingComp FindAmmoUserComp(ThingWithComps gun)
        {
            if (gun?.AllComps == null) return null;
            for (int i = 0; i < gun.AllComps.Count; i++)
            {
                ThingComp comp = gun.AllComps[i];
                if (comp == null) continue;
                string name = comp.GetType().Name;
                if (name == CompAmmoUserSimpleName || name == CompVariableAmmoUserSimpleName)
                    return comp;
            }
            return null;
        }

        private static bool IsUseAmmo(ThingComp ammoUser)
        {
            PropertyInfo prop = ammoUser.GetType().GetProperty("UseAmmo", Inst);
            if (prop == null || prop.PropertyType != typeof(bool)) return true;
            try
            {
                return (bool)prop.GetValue(ammoUser);
            }
            catch
            {
                return true;
            }
        }

        /// <summary>
        /// Primary ranged weapon with a CE ammo user that uses ammo. Soft-fails without CE or when
        /// the pawn has no suitable primary.
        /// </summary>
        internal static bool TryGetPrimaryAmmoUser(Pawn pawn, out ThingComp ammoUser, out object props, out object ammoSet)
        {
            ammoUser = null;
            props = null;
            ammoSet = null;
            if (!IsCeActive || pawn?.equipment?.Primary == null) return false;

            ThingWithComps primary = pawn.equipment.Primary;
            if (primary.def == null || !primary.def.IsRangedWeapon) return false;

            ammoUser = FindAmmoUserComp(primary);
            if (ammoUser == null) return false;
            if (!IsUseAmmo(ammoUser)) return false;

            props = ammoUser.props;
            if (props == null) return false;

            ammoSet = GetFieldValue(props, "ammoSet");
            return ammoSet != null;
        }

        /// <summary>Every ammo def linked from an AmmoSet (reflection; order preserved).</summary>
        internal static List<ThingDef> ListAmmoDefsFromSet(object ammoSet)
        {
            var result = new List<ThingDef>();
            if (ammoSet == null) return result;

            object ammoTypes = GetFieldValue(ammoSet, "ammoTypes");
            if (ammoTypes == null) return result;

            if (ammoTypes is IDictionary dict)
            {
                foreach (object key in dict.Keys)
                {
                    ThingDef ammo = key as ThingDef
                        ?? GetFieldValue(key, "ammo") as ThingDef;
                    if (ammo != null && !result.Contains(ammo))
                        result.Add(ammo);
                }
                return result;
            }

            if (ammoTypes is IEnumerable enumerable)
            {
                foreach (object link in enumerable)
                {
                    if (link == null) continue;
                    ThingDef ammo = link as ThingDef
                        ?? GetFieldValue(link, "ammo") as ThingDef;
                    if (ammo != null && !result.Contains(ammo))
                        result.Add(ammo);
                }
            }

            return result;
        }

        /// <summary>Prefer first non-advanced ammoClass link; else first link (CE default).</summary>
        internal static ThingDef ResolveBasicAmmoDef(object ammoSet)
        {
            List<ThingDef> all = ListAmmoDefsFromSet(ammoSet);
            if (all.Count == 0) return null;

            for (int i = 0; i < all.Count; i++)
            {
                if (!IsAdvancedAmmoClass(all[i]))
                    return all[i];
            }
            return all[0];
        }

        private static bool IsAdvancedAmmoClass(ThingDef ammo)
        {
            object ammoClass = GetFieldValue(ammo, "ammoClass");
            if (ammoClass == null) return false;
            object advanced = GetFieldValue(ammoClass, "advanced");
            return advanced is bool b && b;
        }

        internal static int ResolveOneMagazineRounds(object props, ThingDef basicAmmo)
        {
            int magazineSize = GetIntField(props, "magazineSize", 0);
            if (magazineSize > 0)
            {
                int ammoCount = GetIntField(basicAmmo, "ammoCount", 1);
                if (ammoCount < 1) ammoCount = 1;
                int magAmmoCount = magazineSize / ammoCount;
                return magAmmoCount > 0 ? magAmmoCount : 1;
            }

            int genOverride = GetIntField(props, "AmmoGenPerMagOverride", 0);
            return genOverride > 0 ? genOverride : 1;
        }

        internal static int CountInventoryOfDef(Pawn pawn, ThingDef def)
        {
            if (pawn.inventory?.innerContainer == null || def == null) return 0;
            int sum = 0;
            List<Thing> list = pawn.inventory.innerContainer.InnerListForReading;
            for (int i = 0; i < list.Count; i++)
            {
                Thing t = list[i];
                if (t != null && !t.Destroyed && t.def == def)
                    sum += t.stackCount;
            }
            return sum;
        }

        internal static int ClampToInventoryFit(Pawn pawn, ThingDef ammoDef, int want)
        {
            EnsureInventoryLookup();
            if (pawn == null || ammoDef == null || want <= 0) return 0;
            if (compInventoryType == null) return want;

            ThingComp inv = FindCompOfType(pawn, compInventoryType);
            if (inv == null) return want;
            RefreshInventory(inv);

            // Stack-aware Thing overload first: the def overload has no stack size and can report 1.
            int fit = TryCanFitThing(inv, ammoDef, want);
            if (fit < 0)
                fit = TryCanFitDef(inv, ammoDef);
            if (fit < 0)
                return want;
            return Math.Min(want, fit);
        }

        /// <summary>
        /// CE caches carried bulk and weight and only recalculates on tick. Outpost pawns are not
        /// spawned, so the cache is stale after every armory move unless refreshed here.
        /// </summary>
        internal static void RefreshPawnInventory(Pawn pawn)
        {
            if (pawn == null || !IsCeActive) return;
            EnsureInventoryLookup();
            if (compInventoryType == null) return;
            ThingComp inv = FindCompOfType(pawn, compInventoryType);
            RefreshInventory(inv);
        }

        private static void RefreshInventory(ThingComp inv)
        {
            if (inv == null || updateInventory == null) return;
            try { updateInventory.Invoke(inv, null); }
            catch { /* stale cache is a soft failure */ }
        }

        /// <summary>Free CE bulk and weight on <paramref name="pawn"/>, for rejection messages.</summary>
        internal static bool TryGetFreeCapacity(Pawn pawn, out float freeBulk, out float freeWeight)
        {
            freeBulk = 0f;
            freeWeight = 0f;
            EnsureInventoryLookup();
            if (pawn == null || compInventoryType == null || availableBulkProp == null || availableWeightProp == null)
                return false;
            ThingComp inv = FindCompOfType(pawn, compInventoryType);
            if (inv == null) return false;
            RefreshInventory(inv);
            try
            {
                freeBulk = Convert.ToSingle(availableBulkProp.GetValue(inv));
                freeWeight = Convert.ToSingle(availableWeightProp.GetValue(inv));
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// CE carry load after <see cref="RefreshInventory"/>: current/capacity weight and bulk.
        /// False when CE is off or the pawn has no CompInventory.
        /// </summary>
        internal static bool TryGetCarryLoad(
            Pawn pawn,
            out float currentWeight,
            out float capacityWeight,
            out float currentBulk,
            out float capacityBulk)
        {
            currentWeight = capacityWeight = currentBulk = capacityBulk = 0f;
            EnsureInventoryLookup();
            if (pawn == null || !IsCeActive || compInventoryType == null) return false;
            if (currentWeightProp == null || capacityWeightProp == null
                || currentBulkProp == null || capacityBulkProp == null)
                return false;

            ThingComp inv = FindCompOfType(pawn, compInventoryType);
            if (inv == null) return false;
            RefreshInventory(inv);
            try
            {
                currentWeight = Convert.ToSingle(currentWeightProp.GetValue(inv));
                capacityWeight = Convert.ToSingle(capacityWeightProp.GetValue(inv));
                currentBulk = Convert.ToSingle(currentBulkProp.GetValue(inv));
                capacityBulk = Convert.ToSingle(capacityBulkProp.GetValue(inv));
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <returns>Fit count, or -1 if the CE call could not be made.</returns>
        private static int TryCanFitDef(ThingComp inv, ThingDef def)
        {
            if (canFitInInventoryDef == null) return -1;
            try
            {
                object[] args = { def, 0, false, false };
                object result = canFitInInventoryDef.Invoke(inv, args);
                if (result is bool ok && ok)
                    return args[1] is int n ? Math.Max(0, n) : -1;
                if (result is bool)
                    return 0;
            }
            catch
            {
                // Soft-fail to Thing overload / uncapped want.
            }
            return -1;
        }

        /// <returns>Fit count, or -1 if the CE call could not be made.</returns>
        private static int TryCanFitThing(ThingComp inv, ThingDef def, int want)
        {
            if (canFitInInventoryThing == null) return -1;
            Thing probe = null;
            try
            {
                probe = ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
                if (probe == null) return -1;
                probe.stackCount = Math.Max(1, want);

                object[] args = { probe, 0, false, false };
                object result = canFitInInventoryThing.Invoke(inv, args);
                if (result is bool ok && ok)
                    return args[1] is int n ? Math.Max(0, n) : -1;
                if (result is bool)
                    return 0;
            }
            catch
            {
                // Soft-fail: uncapped want.
            }
            finally
            {
                if (probe != null && !probe.Destroyed)
                    probe.Destroy(DestroyMode.Vanish);
            }
            return -1;
        }

        internal static bool TryAddAmmoToInventory(Pawn pawn, ThingDef ammoDef, int count)
        {
            if (pawn.inventory?.innerContainer == null || ammoDef == null || count <= 0) return false;

            int remaining = count;
            int stackLimit = ammoDef.stackLimit > 0 ? ammoDef.stackLimit : count;
            bool any = false;

            while (remaining > 0)
            {
                Thing ammo = ThingMaker.MakeThing(ammoDef);
                if (ammo == null) break;

                int chunk = Math.Min(remaining, stackLimit);
                ammo.stackCount = chunk;

                if (!pawn.inventory.innerContainer.TryAdd(ammo, canMergeWithExistingStacks: true))
                {
                    if (!ammo.Destroyed)
                        ammo.Destroy();
                    break;
                }

                any = true;
                remaining -= chunk;
            }

            return any;
        }

        private static void EnsureInventoryLookup()
        {
            if (inventoryLookupDone) return;
            inventoryLookupDone = true;
            try
            {
                compInventoryType = GenTypes.GetTypeInAnyAssembly(CompInventoryTypeName, "CombatExtended");
                if (compInventoryType == null) return;

                // bool CanFitInInventory(ThingDef|Thing, out int count, bool ignoreEquipment = false, bool useApparelCalculations = false)
                foreach (MethodInfo mi in compInventoryType.GetMethods(Inst))
                {
                    if (mi.Name != "CanFitInInventory") continue;
                    ParameterInfo[] ps = mi.GetParameters();
                    if (ps.Length < 2) continue;
                    if (!ps[1].ParameterType.IsByRef) continue;
                    Type first = ps[0].ParameterType;
                    if (first == typeof(ThingDef) && canFitInInventoryDef == null)
                        canFitInInventoryDef = mi;
                    else if (typeof(Thing).IsAssignableFrom(first) && canFitInInventoryThing == null)
                        canFitInInventoryThing = mi;
                }

                updateInventory = compInventoryType.GetMethod("UpdateInventory", Inst, null, Type.EmptyTypes, null);
                availableBulkProp = compInventoryType.GetProperty("availableBulk", Inst);
                availableWeightProp = compInventoryType.GetProperty("availableWeight", Inst);
                currentWeightProp = compInventoryType.GetProperty("currentWeight", Inst);
                capacityWeightProp = compInventoryType.GetProperty("capacityWeight", Inst);
                currentBulkProp = compInventoryType.GetProperty("currentBulk", Inst);
                capacityBulkProp = compInventoryType.GetProperty("capacityBulk", Inst);
            }
            catch
            {
                compInventoryType = null;
                canFitInInventoryDef = null;
            }
        }

        private static ThingComp FindCompOfType(ThingWithComps thing, Type compType)
        {
            if (thing?.AllComps == null || compType == null) return null;
            for (int i = 0; i < thing.AllComps.Count; i++)
            {
                ThingComp c = thing.AllComps[i];
                if (c != null && compType.IsInstanceOfType(c))
                    return c;
            }
            return null;
        }

        private static object GetFieldValue(object obj, string fieldName)
        {
            if (obj == null || string.IsNullOrEmpty(fieldName)) return null;
            FieldInfo fi = obj.GetType().GetField(fieldName, Inst);
            return fi?.GetValue(obj);
        }

        private static int GetIntField(object obj, string fieldName, int fallback)
        {
            object v = GetFieldValue(obj, fieldName);
            if (v is int i) return i;
            return fallback;
        }

        private static object GetPropertyValue(object obj, string propertyName)
        {
            if (obj == null || string.IsNullOrEmpty(propertyName)) return null;
            PropertyInfo pi = obj.GetType().GetProperty(propertyName, Inst);
            if (pi != null && pi.CanRead) return pi.GetValue(obj);
            return GetFieldValue(obj, propertyName);
        }

        private static int GetIntProperty(object obj, string propertyName, int fallback)
        {
            object v = GetPropertyValue(obj, propertyName);
            if (v is int i) return i;
            return fallback;
        }

        private static void TrySetIntProperty(object obj, string propertyName, int value)
        {
            if (obj == null) return;
            PropertyInfo pi = obj.GetType().GetProperty(propertyName, Inst);
            if (pi != null && pi.CanWrite)
            {
                pi.SetValue(obj, value);
                return;
            }
            // CE exposes CurMagCount as a property over a private backing field in some versions.
            FieldInfo fi = obj.GetType().GetField("curMagCountInt", Inst)
                ?? obj.GetType().GetField("curMagCount", Inst);
            fi?.SetValue(obj, value);
        }
    }
}
