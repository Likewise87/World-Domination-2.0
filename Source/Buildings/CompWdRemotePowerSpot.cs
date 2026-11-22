using System.Collections.Generic;
using System.Text;
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
    /// </summary>
    public class CompWdRemotePowerSpot : ThingComp
    {
        public const float DefaultSharePercent = 100f;

        private float sharePercent = DefaultSharePercent;
        private PowerNet lastSeenNet;

        public float SharePercent
        {
            get => sharePercent;
            set => sharePercent = Mathf.Clamp(value, 0f, 100f);
        }

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
            if (Scribe.mode == LoadSaveMode.LoadingVars)
                sharePercent = Mathf.Clamp(sharePercent, 0f, 100f);
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            lastSeenNet = PowerNet;
            Outpost_PowerPlant.NotifyRemotePowerDirty();
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            lastSeenNet = null;
            Outpost_PowerPlant.NotifyRemotePowerDirty();
            base.PostDeSpawn(map, mode);
        }

        public override void CompTickRare()
        {
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
            {
                sb.Append("TSA_WD_RemotePowerSpot_InspectIneligible".Translate(SharePercent.ToString("F0")));
            }
            else
            {
                sb.Append("TSA_WD_RemotePowerSpot_InspectShare".Translate(
                    SharePercent.ToString("F0"),
                    (effective * 100f).ToString("F0")));
                float watts = Outpost_PowerPlant.GetSpotAllocatedWatts(this);
                if (watts > 0f)
                    sb.AppendInNewLine("TSA_WD_RemotePowerSpot_InspectWatts".Translate(Outpost_PowerPlant.FormatWatts(watts)));
            }
            return sb.ToString();
        }

        public void SetSharePercentAndDirty(float value)
        {
            SharePercent = value;
            Outpost_PowerPlant.NotifyRemotePowerDirty();
        }
    }
}
