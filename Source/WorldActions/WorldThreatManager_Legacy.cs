using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Save-compatibility stub for pre-refactor worlds. Threat state now lives on
    /// <see cref="WorldComponent_SpreadManager"/>. This type exists only so older saves
    /// can deserialize their world component list; it removes itself on <see cref="FinalizeInit"/>.
    /// </summary>
    public class WorldThreatManager : WorldComponent
    {
        private int lastThreatCategory;
        private int lastThreatLetterSentTick;

        internal int LegacyThreatCategory => lastThreatCategory;

        public WorldThreatManager(World world) : base(world) { }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref lastThreatCategory, "lastThreatCategory", 0);
            Scribe_Values.Look(ref lastThreatLetterSentTick, "lastThreatLetterSentTick", 0);
        }

        public override void FinalizeInit(bool fromLoad)
        {
            base.FinalizeInit(fromLoad);
            if (fromLoad)
                world?.GetComponent<WorldComponent_SpreadManager>()?.ImportLegacyWorldThreatTier(lastThreatCategory);
            DetachFromWorld();
        }

        private void DetachFromWorld()
        {
            world?.components?.Remove(this);
        }
    }
}
