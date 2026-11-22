using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.Sound;

namespace TSA_WorldDomination
{
    /// <summary>Incoming WD raid letter with jump-to-attacker and jump-to-target choices.</summary>
    public class ChoiceLetter_IncomingWdRaid : ChoiceLetter
    {
        public WorldObject? attacker;
        public WorldObject? target;
        public WorldObject_Traveler? traveler;

        public override IEnumerable<DiaOption> Choices
        {
            get
            {
                WorldObject jumpAttacker = WdPlayerIntel.CanJumpOrLookTarget(attacker) ? attacker : null;
                WorldObject jumpOrigin = traveler != null && WdPlayerIntel.CanJumpOrLookTarget(traveler.originObject)
                    ? traveler.originObject
                    : null;
                WorldObject jumpTraveler = traveler != null && WdPlayerIntel.CanJumpOrLookTarget(traveler)
                    ? (WorldObject)traveler
                    : null;
                if (jumpAttacker != null || jumpOrigin != null || jumpTraveler != null)
                {
                    yield return MakeJumpOption(
                        "TSA_WD_Letter_IncomingRaid_JumpAttacker".Translate(),
                        jumpAttacker ?? jumpTraveler,
                        jumpOrigin);
                }
                yield return MakeJumpOption(
                    "TSA_WD_Letter_IncomingRaid_JumpTarget".Translate(),
                    target,
                    traveler != null ? traveler.targetObject : null);
                yield return Option_Close;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref attacker, "attacker");
            Scribe_References.Look(ref target, "target");
            Scribe_References.Look(ref traveler, "traveler");
        }

        private DiaOption MakeJumpOption(string label, WorldObject? primary, WorldObject? fallback)
        {
            return new DiaOption(label)
            {
                action = () => JumpToPreferred(primary, fallback),
                resolveTree = true
            };
        }

        private static void JumpToPreferred(WorldObject? primary, WorldObject? fallback)
        {
            WorldObject prefer = WdPlayerIntel.CanJumpOrLookTarget(primary) ? primary : null;
            WorldObject fall = WdPlayerIntel.CanJumpOrLookTarget(fallback) ? fallback : null;
            if (TryJumpWorldObject(prefer))
                return;
            if (TryJumpWorldObject(fall))
                return;
            // Undiscovered: no silent tile jump that reveals origin.
        }

        private static bool TryJumpWorldObject(WorldObject? wo)
        {
            if (wo == null || wo.Destroyed || !wo.Spawned)
                return false;
            if (!WdPlayerIntel.CanJumpOrLookTarget(wo))
                return false;
            WorldDomination_UIUtils.JumpToWorldObjectOnMap(wo);
            return true;
        }
    }
}
