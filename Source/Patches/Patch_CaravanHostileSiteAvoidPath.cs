using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Player caravan FindPath runs inside CaravanPather.StartPath. Scope hostile-site avoid and
    /// trap-avoid contexts around that call; dispose before hop CostToMove.
    /// </summary>
    [StaticConstructorOnStartup]
    [HarmonyPatch]
    public static class Patch_CaravanHostileSiteAvoidPath
    {
        public struct PathScopes
        {
            public WdHostileSiteAvoidPathContext.Scope site;
            public WdScoutTrapAvoidPathContext.Scope trap;
        }

        static IEnumerable<MethodBase> TargetMethods()
        {
            var f = AccessTools.Field(typeof(Caravan), "pather");
            if (f == null) yield break;
            foreach (var m in AccessTools.GetDeclaredMethods(f.FieldType))
            {
                if (m.Name != "StartPath") continue;
                var ps = m.GetParameters();
                if (ps.Length > 0 && ps[0].ParameterType == typeof(PlanetTile))
                    yield return m;
            }
        }

        [HarmonyPrefix]
        public static void Prefix(object __instance, PlanetTile destTile, ref PathScopes __state)
        {
            __state = default;
            if (__instance == null || !destTile.Valid) return;
            Caravan caravan = Traverse.Create(__instance).Field("caravan").GetValue<Caravan>();
            if (caravan?.Faction == null || !caravan.Faction.IsPlayer) return;
            var s = WorldDominationMod.settings;
            if (s == null || !s.playerPawnCaravanPathfindingAdjustments) return;
            __state.site = WdHostileSiteAvoidPathContext.TryActivateForPlayerPawnCaravan(caravan.Faction, destTile.tileId);
            __state.trap = WdScoutTrapAvoidPathContext.TryActivateForPlayerCaravan(caravan.Faction);
        }

        [HarmonyPostfix]
        public static void Postfix(PathScopes __state)
        {
            __state.site?.Dispose();
            __state.trap?.Dispose();
        }
    }
}
