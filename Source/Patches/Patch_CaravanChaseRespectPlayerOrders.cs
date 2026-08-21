using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Any player-driven path change ends chase; automated repaths (tick) are ignored via RepathSuppressionDepth.</summary>
    [HarmonyPatch]
    public static class Patch_CaravanChaseRespectPlayerOrders
    {
        private static WorldComponent_CaravanChaseTraveler cachedChaseComp;
        private static int cachedChaseCompWorldId = -1;
        internal static WorldComponent_CaravanChaseTraveler GetChaseComp()
        {
            int worldId = Find.World?.info?.Seed ?? -1;
            if (cachedChaseComp == null || cachedChaseCompWorldId != worldId)
            {
                cachedChaseComp = Find.World?.GetComponent<WorldComponent_CaravanChaseTraveler>();
                cachedChaseCompWorldId = worldId;
            }
            return cachedChaseComp;
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

        [HarmonyPostfix]
        public static void PostfixStartPath(object __instance, PlanetTile destTile, CaravanArrivalAction arrivalAction, bool repathImmediately, bool resetPauseStatus, bool __result)
        {
            if (__instance == null || !destTile.Valid) return;
            if (WorldComponent_CaravanChaseTraveler.RepathSuppressionDepth > 0) return;

            Caravan caravan = Traverse.Create(__instance).Field("caravan").GetValue<Caravan>();
            if (caravan == null || caravan.Faction == null || !caravan.Faction.IsPlayer) return;

            var comp = Patch_CaravanChaseRespectPlayerOrders.GetChaseComp();
            if (comp == null) return;

            if (WorldComponent_CaravanChaseTraveler.PendingInitialChaseStartPath.Contains(caravan))
            {
                WorldComponent_CaravanChaseTraveler.PendingInitialChaseStartPath.Remove(caravan);
                return;
            }

            if (comp.GetChaseTarget(caravan) != null)
                comp.RemoveChase(caravan);
        }
    }

    /// <summary>Vanilla stop (and other StopDead) ends chase unless it is part of automated repath or initial chase StartPath.</summary>
    [HarmonyPatch]
    public static class Patch_CaravanChaseStopDead
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            var f = AccessTools.Field(typeof(Caravan), "pather");
            if (f == null) yield break;
            foreach (var m in AccessTools.GetDeclaredMethods(f.FieldType))
            {
                if (m.Name != "StopDead" && m.Name != "StopDeadAndDestroyPath") continue;
                if (m.GetParameters().Length == 0)
                    yield return m;
            }
        }

        [HarmonyPostfix]
        public static void PostfixStopDead(object __instance)
        {
            if (WorldComponent_CaravanChaseTraveler.RepathSuppressionDepth > 0) return;

            Caravan caravan = Traverse.Create(__instance).Field("caravan").GetValue<Caravan>();
            if (caravan == null || caravan.Faction == null || !caravan.Faction.IsPlayer) return;

            if (WorldComponent_CaravanChaseTraveler.PendingInitialChaseStartPath.Contains(caravan))
                return;

            var comp = Patch_CaravanChaseRespectPlayerOrders.GetChaseComp();
            if (comp == null || comp.GetChaseTarget(caravan) == null) return;

            comp.RemoveChase(caravan);
        }
    }

    /// <summary>Explicit cancel pursuit gizmo on player caravans that are chasing a traveler.</summary>
    [StaticConstructorOnStartup]
    public static class Patch_CaravanChaseCancelGizmo
    {
        private static Texture2D cachedCancelIcon;

        public static IEnumerable<Gizmo> GetGizmos(Caravan caravan)
        {
            if (caravan == null || caravan.Destroyed || !caravan.IsPlayerControlled) yield break;

            var comp = Patch_CaravanChaseRespectPlayerOrders.GetChaseComp();
            if (comp == null || comp.GetChaseTarget(caravan) == null) yield break;

            if (cachedCancelIcon == null)
                cachedCancelIcon = ContentFinder<Texture2D>.Get("UI/Designators/Cancel", false)
                    ?? ContentFinder<Texture2D>.Get("UI/Commands/Cancel", false)
                    ?? Widgets.CheckboxOffTex;
            var icon = cachedCancelIcon;
            WorldComponent_CaravanChaseTraveler chaseComp = comp;

            yield return new Command_Action
            {
                defaultLabel = "TSA_WD_Traveler_CancelChase".Translate(),
                defaultDesc = "TSA_WD_Traveler_CancelChase_Desc".Translate(),
                icon = icon,
                action = () => chaseComp.CancelChaseAndStopCaravan(caravan)
            };
        }
    }
}
