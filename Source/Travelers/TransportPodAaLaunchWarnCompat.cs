using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Soft confirm when the player picks a vanilla transport-pod / shuttle destination
    /// on the world map (CompLaunchable.TryLaunch), before skyfallers spawn.
    /// Mid/Late gates match RR drop-pod / VF aerial warn.
    /// Removable: delete this file (+ i18n keys).
    /// </summary>
    [StaticConstructorOnStartup]
    public static class TransportPodAaLaunchWarnCompat
    {
        private static bool active;
        private static bool allowNext;

        private static CompLaunchable pendingLaunchable;
        private static PlanetTile pendingDestination;
        private static TransportersArrivalAction pendingArrivalAction;

        static TransportPodAaLaunchWarnCompat()
        {
            try
            {
                MethodInfo tryLaunch = AccessTools.Method(
                    typeof(CompLaunchable),
                    nameof(CompLaunchable.TryLaunch),
                    new[] { typeof(PlanetTile), typeof(TransportersArrivalAction) });
                if (tryLaunch == null)
                {
                    Log.Warning("[TSA WD] Transport pod AA launch warn: TryLaunch not found; disabled.");
                    return;
                }

                var harmony = new Harmony("TSA.WorldDomination.TransportPodAaLaunchWarn");
                harmony.Patch(
                    tryLaunch,
                    prefix: new HarmonyMethod(typeof(TransportPodAaLaunchWarnCompat), nameof(TryLaunch_Prefix)));

                active = true;
                Log.Message("[TSA WD] Transport pod AA launch warning active.");
            }
            catch (Exception ex)
            {
                Log.Warning($"[TSA WD] Transport pod AA launch warning disabled: {ex.Message}");
            }
        }

        public static bool Active => active;

        /// <summary>
        /// Returns false to skip the original launch when a confirm dialog is shown.
        /// </summary>
        public static bool TryLaunch_Prefix(
            CompLaunchable __instance,
            PlanetTile destinationTile,
            TransportersArrivalAction arrivalAction)
        {
            if (!active || __instance?.parent == null) return true;

            if (allowNext)
            {
                allowNext = false;
                return true;
            }

            Thing parent = __instance.parent;
            if (parent.Faction == null || !parent.Faction.IsPlayer)
                return true;

            int originTile = -1;
            if (parent.Map != null && parent.Map.Tile.Valid)
                originTile = parent.Map.Tile.tileId;
            else if (parent.Tile.Valid)
                originTile = parent.Tile.tileId;

            if (originTile < 0 || !destinationTile.Valid)
                return true;

            int destTile = destinationTile.tileId;
            if (destTile < 0) return true;

            var threats = new List<Settlement>();
            if (!AntiAirFireUtils.TryGetHostileSettlementAaThreatsForDropPodFlight(originTile, destTile, threats)
                || threats.Count == 0)
                return true;

            string names = threats[0].LabelCap;
            for (int i = 1; i < threats.Count; i++)
                names += ", " + threats[i].LabelCap;

            pendingLaunchable = __instance;
            pendingDestination = destinationTile;
            pendingArrivalAction = arrivalAction;

            string key = parent.HasComp<CompShuttle>()
                ? "TSA_WD_AntiAir_Shuttle_AaWarning"
                : "TSA_WD_AntiAir_TransportPods_AaWarning";

            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                key.Translate(names),
                ConfirmAndReinvoke,
                destructive: true));

            return false;
        }

        private static void ConfirmAndReinvoke()
        {
            CompLaunchable launchable = pendingLaunchable;
            PlanetTile destination = pendingDestination;
            TransportersArrivalAction arrivalAction = pendingArrivalAction;
            pendingLaunchable = null;
            pendingArrivalAction = null;
            pendingDestination = PlanetTile.Invalid;

            if (launchable?.parent == null || launchable.parent.Destroyed)
                return;

            try
            {
                allowNext = true;
                launchable.TryLaunch(destination, arrivalAction);
            }
            catch (Exception ex)
            {
                allowNext = false;
                Log.Warning($"[TSA WD] Transport pod AA launch warn re-invoke failed: {ex.Message}");
            }
        }
    }
}
