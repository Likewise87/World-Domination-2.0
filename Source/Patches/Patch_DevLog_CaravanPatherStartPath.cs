using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Logs every <see cref="Caravan"/> world path request (actual pathfind runs inside CaravanPather.StartPath).</summary>
    [StaticConstructorOnStartup]
    [HarmonyPatch]
    public static class Patch_DevLog_CaravanPatherStartPath
    {
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
        public static void Prefix(object __instance, PlanetTile destTile)
        {
            if (!Prefs.DevMode || __instance == null) return;
            Caravan caravan = Traverse.Create(__instance).Field("caravan").GetValue<Caravan>();
            if (caravan == null) return;
            WD_DevPerformanceSpikeLog.Msg(
                $"CaravanPather.StartPath caravan=\"{caravan.Label}\" faction={caravan.Faction?.Name ?? "?"} player={(caravan.Faction?.IsPlayer == true)} destTile={destTile.tileId} destValid={destTile.Valid}");
        }
    }
}
