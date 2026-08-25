using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Spawns visible mortar arcs on assault maps (vanilla + CE).</summary>
    public static class AssaultArtilleryProjectileLaunch
    {
        public static bool TryLaunchAssaultShell(
            Map map,
            int originTile,
            IntVec3 impact,
            ThingDef shellDef,
            CellRect? lockedIngressBand = null)
        {
            if (map == null || shellDef == null || !impact.InBounds(map)) return false;

            ThingDef? bulletDef = ResolveProjectileDef(shellDef);
            if (bulletDef == null)
            {
                if (Prefs.DevMode)
                    Log.Warning($"[TSA WD] Assault artillery: no projectile for shell {shellDef.defName}");
                return false;
            }

            IntVec3 edgeCell;
            if (lockedIngressBand.HasValue
                && AssaultArtilleryIngressDirection.TryPickCellInBand(lockedIngressBand.Value, map, out edgeCell))
            {
                // locked band for this volley
            }
            else if (!AssaultArtilleryIngressDirection.TryPickIngressCell(map, originTile, out edgeCell))
            {
                edgeCell = CellFinder.RandomEdgeCell(map);
            }

            if (AssaultArtilleryCeCompat.IsActive && AssaultArtilleryCeCompat.IsProjectileCe(bulletDef))
            {
                Thing? launcher = FindLauncherProxy(map, edgeCell);
                if (launcher != null && AssaultArtilleryCeCompat.TryLaunch(map, edgeCell, impact, bulletDef, launcher))
                    return true;
                if (Prefs.DevMode)
                    Log.Warning($"[TSA WD] Assault artillery CE launch failed for {bulletDef.defName}");
                return false;
            }

            if (bulletDef.thingClass == null || !typeof(Projectile).IsAssignableFrom(bulletDef.thingClass))
                return false;

            Thing spawned = GenSpawn.Spawn(bulletDef, edgeCell, map);
            if (spawned is not Projectile projectile)
                return false;

            Thing? vanillaLauncher = FindLauncherProxy(map, edgeCell);
            LocalTargetInfo target = new LocalTargetInfo(impact);
            projectile.Launch(
                vanillaLauncher,
                target,
                target,
                ProjectileHitFlags.IntendedTarget,
                preventFriendlyFire: true);
            return true;
        }

        public static ThingDef? ResolveProjectileDef(ThingDef shellDef)
        {
            if (shellDef == null) return null;
            if (shellDef.projectileWhenLoaded != null)
                return shellDef.projectileWhenLoaded;
            return AssaultArtilleryCeCompat.ResolveProjectileDef(shellDef);
        }

        private static Thing? FindLauncherProxy(Map map, IntVec3 near)
        {
            if (map == null) return null;
            foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
            {
                if (p != null && !p.Dead && !p.Downed)
                    return p;
            }

            foreach (Thing t in map.listerThings.AllThings)
            {
                if (t == null || t.Destroyed) continue;
                if (t.Faction == Faction.OfPlayer && t.def.category == ThingCategory.Pawn)
                    return t;
            }
            return null;
        }
    }
}
