using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public class CompProperties_WdRemotePowerSpot : CompProperties
    {
        public CompProperties_WdRemotePowerSpot()
        {
            compClass = typeof(CompWdRemotePowerSpot);
        }
    }

    /// <summary>
    /// Colony marker that claims a relative share of Keep remote watts for its PowerNet.
    /// Delivery is via sibling <see cref="CompPowerTrader"/>.PowerOutput (exact CompPowerTrader
    /// type required so <see cref="ThingDef.ConnectToPower"/> is true and soft wires draw).
    /// </summary>
    public class CompWdRemotePowerSpot : ThingComp
    {
        public const float DefaultSharePercent = 100f;

        private float sharePercent = DefaultSharePercent;
        private float cachedDesiredWatts;
        /// <summary>Legacy; forever Look'd (removed Subtle appearance gizmo).</summary>
        private bool subtleAppearance;
        private PowerNet lastSeenNet;

        public float SharePercent
        {
            get => sharePercent;
            set => sharePercent = Mathf.Clamp(value, 0f, 100f);
        }

        public float CachedDesiredWatts => cachedDesiredWatts;

        public CompPowerTrader PowerComp => parent.TryGetComp<CompPowerTrader>();

        public PowerNet PowerNet => PowerComp?.PowerNet;

        public bool IsEligible
        {
            get
            {
                if (parent == null || parent.Destroyed || !parent.Spawned) return false;
                if (parent.Map == null || !parent.Map.IsPlayerHome) return false;
                if (Outpost_EstablishmentRequirements.IsActiveCamp(parent.Map.Parent)) return false;
                if (sharePercent <= 0f) return false;
                return PowerNet != null;
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref sharePercent, "wdRemotePowerSharePercent", DefaultSharePercent);
            Scribe_Values.Look(ref subtleAppearance, "wdRemotePowerSubtleAppearance", false);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
                sharePercent = Mathf.Clamp(sharePercent, 0f, 100f);
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            lastSeenNet = PowerNet;
            ApplyPowerOutputToTrader();
            Outpost_PowerPlant.NotifyRemotePowerDirty();
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            cachedDesiredWatts = 0f;
            CompPowerTrader trader = PowerComp;
            if (trader != null)
                trader.PowerOutput = 0f;
            lastSeenNet = null;
            Outpost_PowerPlant.NotifyRemotePowerDirty();
            base.PostDeSpawn(map, mode);
        }

        public override void CompTick()
        {
            if (!parent.IsHashIntervalTick(60)) return;
            PowerNet net = PowerNet;
            if (!ReferenceEquals(net, lastSeenNet))
            {
                lastSeenNet = net;
                Outpost_PowerPlant.NotifyRemotePowerDirty();
            }
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra())
                yield return g;

            yield return new Command_Action
            {
                defaultLabel = "TSA_WD_RemotePowerSpot_GizmoShare".Translate(),
                defaultDesc = "TSA_WD_RemotePowerSpot_GizmoShareDesc".Translate(),
                icon = TexCommand.DesirePower,
                action = () => Dialog_WdRemotePowerSpotShare.Open(this)
            };
        }

        public override string CompInspectStringExtra()
        {
            var sb = new StringBuilder();
            float effective = Outpost_PowerPlant.GetSpotEffectiveShareFraction(this);
            if (effective < 0f)
                sb.Append("TSA_WD_RemotePowerSpot_InspectIneligible".Translate(SharePercent.ToString("F0")));
            else
            {
                sb.Append("TSA_WD_RemotePowerSpot_InspectShare".Translate(
                    SharePercent.ToString("F0"),
                    (effective * 100f).ToString("F0")));
            }
            return sb.ToString();
        }

        public void SetSharePercentAndDirty(float value)
        {
            SharePercent = value;
            Outpost_PowerPlant.NotifyRemotePowerDirty();
        }

        /// <summary>Store Keep watts and push to CompPowerTrader (O(1) for CompTick / SetUpPowerVars).</summary>
        public void ApplyCachedAllocatedWatts(float watts)
        {
            cachedDesiredWatts = watts > 0f ? watts : 0f;
            ApplyPowerOutputToTrader();
        }

        internal void ApplyPowerOutputToTrader()
        {
            CompPowerTrader trader = PowerComp;
            if (trader == null) return;
            trader.PowerOutput = cachedDesiredWatts;
            if (cachedDesiredWatts > 0f && FlickUtility.WantsToBeOn(parent) && !trader.PowerOn)
                trader.PowerOn = true;
        }
    }

    /// <summary>
    /// Vanilla SetUpPowerVars resets PowerOutput to -basePowerConsumption (0). Re-apply cached Keep watts.
    /// </summary>
    [HarmonyPatch(typeof(CompPowerTrader), nameof(CompPowerTrader.SetUpPowerVars))]
    public static class Patch_CompPowerTrader_SetUpPowerVars_RemotePowerSpot
    {
        [HarmonyPostfix]
        public static void Postfix(CompPowerTrader __instance)
        {
            var spot = __instance?.parent?.TryGetComp<CompWdRemotePowerSpot>();
            spot?.ApplyPowerOutputToTrader();
        }
    }
}
