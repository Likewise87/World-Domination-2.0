using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    public class CompProperties_PlayerDispatchMode : WorldObjectCompProperties
    {
        public CompProperties_PlayerDispatchMode() => compClass = typeof(CompPlayerDispatchMode);
    }

    /// <summary>Per-settlement land vs drop-pod preference for outpost upgrade launches from that colony.</summary>
    public class CompPlayerDispatchMode : WorldObjectComp
    {
        public bool dispatchViaDropPod;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref dispatchViaDropPod, "dispatchViaDropPod", false);
        }

        public static CompPlayerDispatchMode Get(WorldObject wo) =>
            wo?.GetComponent<CompPlayerDispatchMode>();
    }
}
