using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Optional integration with Worksites Expanded (godsfathermixtape.worksitesexpanded / MiningOutpost.dll).
    /// Uses reflection only — no compile-time reference. WD must not hijack KCSG layout generation for
    /// worksite maps or treat clearing a worksite as a WD settlement conquest.
    /// </summary>
    public static class WorksitesExpandedCompat
    {
        private const string AssemblyName = "MiningOutpost";
        private const string OdysseyCompatUtilTypeName = "MiningOutpost.OdysseyCompatUtil";
        private const string OutpostSiteUtilityTypeName = "MiningOutpost.Parley.OutpostSiteUtility";
        private const string AlertOutpostTurretsTypeName = "MiningOutpost.Alert_OutpostTurrets";
        private const string OrbitalDoorUtilTypeName = "MiningOutpost.Orbital.OrbitalDoorUtil";
        private const string OutpostDefNameCheckerTypeName = "MiningOutpost.Patch_GenHostility_InsectsNotBlockingExit";

        private static readonly HashSet<string> KnownSitePartDefNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "MiningOutpost",
            "HomesteadOutpost",
            "MercenaryOutpost",
            "VehiclesOutpost",
            "GunsmithingOutpost",
            "BlackMarketOutpost",
            "VehiclesTier3Outpost",
            "FarmingOutpost",
            "ComponentOutpost",
            "ClothesMakingOutpost",
            "OrbitalPlatform",
            "ResearchOutpost",
            "TradeHubOutpost"
        };

        private static bool lookupDone;
        private static bool modLoaded;
        private static MethodInfo isOurOutpostSiteOnMap;
        private static MethodInfo isOutpostMap;
        private static MethodInfo isOrbitalWorksiteMap;
        private static MethodInfo getOutpostSiteOnTile;
        private static MethodInfo isOurOutpostDefName;

        public static bool IsModLoaded
        {
            get
            {
                EnsureLookup();
                return modLoaded;
            }
        }

        public static bool IsWorksitesExpandedMap(Map map)
        {
            if (map == null) return false;
            if (IsWorksitesExpandedSite(map.Parent as Site)) return true;

            EnsureLookup();
            if (!modLoaded) return false;

            try
            {
                if (InvokeBool(isOurOutpostSiteOnMap, map)) return true;
                if (InvokeBool(isOutpostMap, map)) return true;
                if (InvokeBool(isOrbitalWorksiteMap, map)) return true;
            }
            catch (Exception ex)
            {
                Log.Warning($"[WorldDomination] Worksites Expanded compat: map check failed: {ex.Message}");
            }

            return false;
        }

        public static bool IsWorksitesExpandedSite(Site site)
        {
            if (site == null) return false;

            if (site.MainSitePartDef != null && IsWorksitesExpandedSitePartDef(site.MainSitePartDef.defName))
                return true;

            var parts = site.parts;
            if (parts != null)
            {
                for (int i = 0; i < parts.Count; i++)
                {
                    SitePart part = parts[i];
                    if (part?.def != null && IsWorksitesExpandedSitePartDef(part.def.defName))
                        return true;
                }
            }

            return false;
        }

        public static bool IsWorksitesExpandedSitePartDef(string defName)
        {
            if (defName.NullOrEmpty()) return false;

            EnsureLookup();
            if (modLoaded && isOurOutpostDefName != null)
            {
                try
                {
                    object result = isOurOutpostDefName.Invoke(null, new object[] { defName });
                    if (result is bool b) return b;
                }
                catch (Exception ex)
                {
                    Log.Warning($"[WorldDomination] Worksites Expanded compat: site-part check failed: {ex.Message}");
                }
            }

            return KnownSitePartDefNames.Contains(defName);
        }

        public static bool HasWorksitesExpandedSiteOnTile(int tile)
        {
            if (tile < 0) return false;

            EnsureLookup();
            if (modLoaded && getOutpostSiteOnTile != null)
            {
                try
                {
                    object site = getOutpostSiteOnTile.Invoke(null, new object[] { tile });
                    if (site is Site s && !s.Destroyed) return true;
                }
                catch (Exception ex)
                {
                    Log.Warning($"[WorldDomination] Worksites Expanded compat: tile lookup failed: {ex.Message}");
                }
            }

            return FindWorksitesExpandedSiteOnTile(tile) != null;
        }

        public static bool ShouldSkipWdSettlementConquest(Settlement settlement)
        {
            if (settlement == null) return false;
            if (!IsModLoaded) return false;

            Map map = settlement.Map;
            if (map != null && IsWorksitesExpandedMap(map))
                return true;

            return HasWorksitesExpandedSiteOnTile(settlement.Tile);
        }

        public static bool ShouldSkipWdKcsgInterference(Map map)
        {
            if (map == null) return false;
            if (IsWorksitesExpandedMap(map)) return true;
            return HasWorksitesExpandedSiteOnTile(map.Tile);
        }

        public static bool ShouldSkipWdKcsgInterference(Settlement settlement)
        {
            if (settlement == null) return false;
            if (HasWorksitesExpandedSiteOnTile(settlement.Tile)) return true;
            Map map = settlement.Map;
            return map != null && IsWorksitesExpandedMap(map);
        }

        private static Site FindWorksitesExpandedSiteOnTile(int tile)
        {
            var at = Find.WorldObjects?.ObjectsAt(tile);
            if (at == null) return null;

            foreach (WorldObject wo in at)
            {
                if (wo is Site site && !site.Destroyed && IsWorksitesExpandedSite(site))
                    return site;
            }

            return null;
        }

        private static bool InvokeBool(MethodInfo method, Map map)
        {
            if (method == null || map == null) return false;
            object result = method.Invoke(null, new object[] { map });
            return result is bool b && b;
        }

        private static void EnsureLookup()
        {
            if (lookupDone) return;
            lookupDone = true;

            Assembly asm = null;
            foreach (Assembly loaded in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (loaded.GetName().Name == AssemblyName)
                {
                    asm = loaded;
                    break;
                }
            }

            if (asm == null) return;
            modLoaded = true;

            Type odysseyCompat = asm.GetType(OdysseyCompatUtilTypeName, throwOnError: false);
            isOurOutpostSiteOnMap = odysseyCompat?.GetMethod(
                "IsOurOutpostSite",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[] { typeof(Map) },
                null);

            Type alertType = asm.GetType(AlertOutpostTurretsTypeName, throwOnError: false);
            isOutpostMap = alertType?.GetMethod(
                "IsOutpostMap",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[] { typeof(Map) },
                null);

            Type orbitalType = asm.GetType(OrbitalDoorUtilTypeName, throwOnError: false);
            isOrbitalWorksiteMap = orbitalType?.GetMethod(
                "IsOrbitalWorksiteMap",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[] { typeof(Map) },
                null);

            Type siteUtilityType = asm.GetType(OutpostSiteUtilityTypeName, throwOnError: false);
            getOutpostSiteOnTile = siteUtilityType?.GetMethod(
                "GetOutpostSiteOnTile",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[] { typeof(int) },
                null);

            Type defNameCheckerType = asm.GetType(OutpostDefNameCheckerTypeName, throwOnError: false);
            isOurOutpostDefName = defNameCheckerType?.GetMethod(
                "IsOurOutpostDefName",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[] { typeof(string) },
                null);

            if (Prefs.DevMode)
                Log.Message("[WorldDomination] Worksites Expanded compat: integration hooks resolved.");
        }
    }
}
