using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Combat Extended compatibility for assault artillery.
    /// Uses reflection only so the shipped assembly loads without CE installed.
    /// Always compiled — do not wrap in #if COMBAT_EXTENDED (build-machine trap).
    /// </summary>
    internal static class AssaultArtilleryCeCompat
    {
        private const string CePackageId = "CETeam.CombatExtended";
        private const string AmmoSet120mm = "AmmoSet_120mmMortarShell";
        private const string AmmoCategory120mm = "Ammo120mmMortarShells";
        private const string ProjectileCeTypeName = "CombatExtended.ProjectileCE";
        private const string ProjectilePropertiesCeTypeName = "CombatExtended.ProjectilePropertiesCE";
        private const string AmmoSetDefTypeName = "CombatExtended.AmmoSetDef";
        private const string CeUtilityTypeName = "CombatExtended.CE_Utility";

        private static Type projectileCeType;
        private static Type projectilePropertiesCeType;
        private static Type ammoSetDefType;
        private static Type ceUtilityType;
        private static MethodInfo getProjectileMethod;
        private static MethodInfo getShotAngleMethod;
        private static MethodInfo launchProjectileMethod;

        public static bool IsActive =>
            ModsConfig.IsActive(CePackageId)
            && GetProjectileCeType() != null
            && GetCeUtilityType() != null;

        public static bool IsProjectileCe(ThingDef bulletDef)
        {
            Type ceProjectile = GetProjectileCeType();
            return bulletDef?.thingClass != null
                && ceProjectile != null
                && ceProjectile.IsAssignableFrom(bulletDef.thingClass);
        }

        /// <summary>Assault artillery with CE uses 120mm mortar ammo only.</summary>
        public static bool IsCeAssaultMortarShell(ThingDef shell)
        {
            if (shell == null || !IsActive) return false;

            if (shell.thingCategories != null)
            {
                for (int i = 0; i < shell.thingCategories.Count; i++)
                {
                    ThingCategoryDef cat = shell.thingCategories[i];
                    if (cat != null && cat.defName == AmmoCategory120mm)
                        return true;
                }
            }

            foreach (object link in GetAmmoLinks120mm())
            {
                if (ReferenceEquals(GetFieldObject<ThingDef>(link, "ammo"), shell))
                    return true;
            }
            return false;
        }

        public static void ForEach120mmMortarShell(Action<ThingDef> action)
        {
            if (action == null || !IsActive) return;

            foreach (object link in GetAmmoLinks120mm())
            {
                ThingDef ammo = GetFieldObject<ThingDef>(link, "ammo");
                if (ammo != null)
                    action(ammo);
            }

            ThingCategoryDef cat = DefDatabase<ThingCategoryDef>.GetNamedSilentFail(AmmoCategory120mm);
            if (cat == null) return;
            foreach (ThingDef d in cat.DescendantThingDefs)
                action(d);
        }

        public static ThingDef ResolveProjectileDef(ThingDef shellDef)
        {
            if (shellDef == null) return null;
            if (shellDef.projectileWhenLoaded != null)
                return shellDef.projectileWhenLoaded;
            if (!IsActive) return null;

            ThingDef detonateProjectile = GetFieldObject<ThingDef>(shellDef, "detonateProjectile");
            if (detonateProjectile != null)
                return detonateProjectile;

            foreach (object link in GetAmmoLinks120mm())
            {
                if (ReferenceEquals(GetFieldObject<ThingDef>(link, "ammo"), shellDef))
                    return GetFieldObject<ThingDef>(link, "projectile");
            }

            MethodInfo getProjectile = GetGetProjectileMethod();
            return getProjectile != null
                ? getProjectile.Invoke(null, new object[] { shellDef }) as ThingDef
                : null;
        }

        public static bool TryLaunch(
            Map map,
            IntVec3 edgeCell,
            IntVec3 impact,
            ThingDef bulletDef,
            Thing launcher)
        {
            if (map == null || bulletDef == null || launcher == null || !IsActive) return false;
            if (!IsProjectileCe(bulletDef)) return false;

            MethodInfo launchProjectile = GetLaunchProjectileMethod();
            if (launchProjectile == null) return false;

            Vector3 src = edgeCell.ToVector3Shifted();
            Vector2 origin = new Vector2(src.x, src.z);
            Vector3 dst = impact.ToVector3Shifted();
            Vector2 dest = new Vector2(dst.x, dst.z);
            Vector2 delta = dest - origin;
            float range = delta.magnitude;
            const float shotHeight = 1f;

            float shotSpeed = bulletDef.projectile?.speed ?? 0f;
            float shotAngle = 0f;
            if (range >= 0.01f)
            {
                object ceProj = GetProjectilePropertiesCe(bulletDef.projectile);
                if (ceProj != null)
                {
                    float gravityPerWidth = GetFloatProperty(ceProj, "GravityPerWidth");
                    if (shotSpeed <= 0.01f)
                        shotSpeed = ResolveMortarShotSpeed(range, shotHeight, gravityPerWidth);

                    object trajectoryWorker = GetPropertyObject<object>(ceProj, "TrajectoryWorker");
                    MethodInfo shotAngleMethod = trajectoryWorker?.GetType().GetMethod(
                        "ShotAngle",
                        BindingFlags.Instance | BindingFlags.Public,
                        null,
                        new[] { ceProj.GetType(), typeof(Vector3), typeof(Vector3), typeof(float) },
                        null);
                    if (shotAngleMethod != null)
                    {
                        Vector3 source = new Vector3(src.x, shotHeight, src.z);
                        Vector3 targetPos = new Vector3(dst.x, 0f, dst.z);
                        object angle = shotAngleMethod.Invoke(trajectoryWorker, new object[] { ceProj, source, targetPos, shotSpeed });
                        if (angle is float f)
                            shotAngle = f;
                    }
                }
                else
                {
                    MethodInfo getShotAngle = GetGetShotAngleMethod();
                    if (getShotAngle != null)
                    {
                        object angle = getShotAngle.Invoke(null, new object[]
                        {
                            shotSpeed > 0.01f ? shotSpeed : 30f,
                            range,
                            -shotHeight,
                            false,
                            GetGravityConst()
                        });
                        if (angle is float f)
                            shotAngle = f;
                    }
                }
            }

            float shotRotation = -Mathf.Atan2(delta.x, delta.y) * Mathf.Rad2Deg;
            launchProjectile.Invoke(null, new object[]
            {
                bulletDef,
                origin,
                new LocalTargetInfo(impact),
                launcher,
                shotAngle,
                shotRotation,
                shotHeight,
                shotSpeed > 0.01f ? shotSpeed : 30f
            });
            return true;
        }

        private static float ResolveMortarShotSpeed(float horizontalRange, float shotHeight, float gravityPerWidth)
        {
            ThingDef mortarDef = DefDatabase<ThingDef>.GetNamedSilentFail("Artillery_Mortar")
                ?? DefDatabase<ThingDef>.GetNamedSilentFail("Artillery_AutoMortar");
            if (mortarDef == null) return 30f;

            Thing mortar = ThingMaker.MakeThing(mortarDef);
            ThingComp charges = (mortar as ThingWithComps)?.AllComps?.FirstOrDefault(c => c.GetType().Name == "CompCharges");
            if (charges == null) return 30f;

            MethodInfo bracketMethod = charges.GetType().GetMethod(
                "GetChargeBracket",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new[] { typeof(float), typeof(float), typeof(float), typeof(Vector2).MakeByRefType() },
                null);
            if (bracketMethod == null) return 30f;

            object[] args = { horizontalRange, shotHeight, gravityPerWidth, default(Vector2) };
            if (bracketMethod.Invoke(charges, args) is bool ok && ok && args[3] is Vector2 bracket)
                return bracket.x;

            return 30f;
        }

        private static IEnumerable GetAmmoLinks120mm()
        {
            Type ammoSetType = GetAmmoSetDefType();
            if (ammoSetType == null) yield break;

            Type dbType = typeof(DefDatabase<>).MakeGenericType(ammoSetType);
            MethodInfo getNamed = dbType.GetMethod("GetNamedSilentFail", BindingFlags.Public | BindingFlags.Static);
            object ammoSet = getNamed?.Invoke(null, new object[] { AmmoSet120mm });
            object ammoTypes = GetFieldObject<object>(ammoSet, "ammoTypes");
            if (ammoTypes is not IEnumerable enumerable) yield break;

            foreach (object link in enumerable)
            {
                if (link != null)
                    yield return link;
            }
        }

        private static object GetProjectilePropertiesCe(object projectileProps)
        {
            Type propsType = GetProjectilePropertiesCeType();
            return projectileProps != null && propsType != null && propsType.IsInstanceOfType(projectileProps)
                ? projectileProps
                : null;
        }

        private static float GetGravityConst()
        {
            Type utility = GetCeUtilityType();
            FieldInfo field = utility?.GetField("GravityConst", BindingFlags.Public | BindingFlags.Static);
            object value = field?.GetValue(null);
            return value is float f ? f : 1f;
        }

        private static Type GetProjectileCeType() =>
            projectileCeType ??= AccessTools.TypeByName(ProjectileCeTypeName);

        private static Type GetProjectilePropertiesCeType() =>
            projectilePropertiesCeType ??= AccessTools.TypeByName(ProjectilePropertiesCeTypeName);

        private static Type GetAmmoSetDefType() =>
            ammoSetDefType ??= AccessTools.TypeByName(AmmoSetDefTypeName);

        private static Type GetCeUtilityType() =>
            ceUtilityType ??= AccessTools.TypeByName(CeUtilityTypeName);

        private static MethodInfo GetGetProjectileMethod() =>
            getProjectileMethod ??= AccessTools.Method(GetCeUtilityType(), "GetProjectile", new[] { typeof(ThingDef) });

        private static MethodInfo GetGetShotAngleMethod() =>
            getShotAngleMethod ??= AccessTools.Method(GetCeUtilityType(), "GetShotAngle");

        private static MethodInfo GetLaunchProjectileMethod() =>
            launchProjectileMethod ??= AccessTools.Method(GetCeUtilityType(), "LaunchProjectileCE");

        private static T GetFieldObject<T>(object obj, string fieldName) where T : class
        {
            if (obj == null) return null;
            FieldInfo field = AccessTools.Field(obj.GetType(), fieldName);
            return field?.GetValue(obj) as T;
        }

        private static T GetPropertyObject<T>(object obj, string propertyName) where T : class
        {
            if (obj == null) return null;
            PropertyInfo prop = AccessTools.Property(obj.GetType(), propertyName);
            return prop?.GetValue(obj) as T;
        }

        private static float GetFloatProperty(object obj, string propertyName)
        {
            if (obj == null) return 0f;
            PropertyInfo prop = AccessTools.Property(obj.GetType(), propertyName);
            if (prop?.GetValue(obj) is float f)
                return f;
            return 0f;
        }
    }
}
