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
                yield return MakeJumpOption(
                    "TSA_WD_Letter_IncomingRaid_JumpAttacker".Translate(),
                    attacker,
                    traveler != null ? traveler.originObject : null);
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
            if (TryJumpWorldObject(primary))
                return;
            if (TryJumpWorldObject(fallback))
                return;
            if (primary != null && primary.Tile.Valid)
            {
                CameraJumper.TryJump(new GlobalTargetInfo(primary.Tile));
                WorldDomination_UIUtils.DismissWorldDominationUiForWorldMap();
                SoundDefOf.Click.PlayOneShotOnCamera();
                return;
            }
            if (fallback != null && fallback.Tile.Valid)
            {
                CameraJumper.TryJump(new GlobalTargetInfo(fallback.Tile));
                WorldDomination_UIUtils.DismissWorldDominationUiForWorldMap();
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
        }

        private static bool TryJumpWorldObject(WorldObject? wo)
        {
            if (wo == null || wo.Destroyed || !wo.Spawned)
                return false;
            WorldDomination_UIUtils.JumpToWorldObjectOnMap(wo);
            return true;
        }
    }
}
