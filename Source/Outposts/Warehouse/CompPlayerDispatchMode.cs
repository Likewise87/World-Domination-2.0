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
        /// <summary>When drop-pod components are missing: land instead of abort. Default on.</summary>
        public bool dropPodFallbackToLand = true;
        /// <summary>Land vs drop pod for automated pawn sends from this settlement.</summary>
        public bool pawnTravelViaDropPod;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref dispatchViaDropPod, "dispatchViaDropPod", false);
            Scribe_Values.Look(ref dropPodFallbackToLand, "dropPodFallbackToLand", true);
            Scribe_Values.Look(ref pawnTravelViaDropPod, "pawnTravelViaDropPod", false);
        }

        public static CompPlayerDispatchMode Get(WorldObject wo) =>
            wo?.GetComponent<CompPlayerDispatchMode>();
    }
}
