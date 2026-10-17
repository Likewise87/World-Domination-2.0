using RimWorld;
using Verse;

namespace TSA_WorldDomination
{
    public class WorldObject_Traveler_Outpost_TypeChange : WorldObject_Traveler
    {
        public const string DropPodIconTexturePath = "WorldObjects/Travelers/DropPod_OutpostUpgrade";

        public string typeChangeDefName;
        public bool typeChangeViaDropPod;
        public bool typeChangeResolved;

        public bool UsesBallisticWorldFlight => typeChangeViaDropPod;

        public override void Destroy()
        {
            if (!typeChangeResolved && targetObject is WorldObject_WD_Outpost outpost && !outpost.Destroyed)
            {
                if (outpost.ClearPendingTypeChangeAfterLoss() && Faction == Faction.OfPlayer)
                {
                    string typeLabel = DefDatabase<WorldObjectDef>
                        .GetNamedSilentFail(typeChangeDefName)?.LabelCap ?? typeChangeDefName ?? "?";
                    Find.LetterStack?.ReceiveLetter(
                        "TSA_WD_TypeChange_LostLabel".Translate(),
                        "TSA_WD_TypeChange_Lost".Translate(typeLabel, outpost.LabelCap),
                        LetterDefOf.NegativeEvent,
                        new LookTargets(outpost));
                }
            }
            base.Destroy();
        }

        public override string ResolveIconTexturePath()
        {
            if (typeChangeViaDropPod)
                return DropPodIconTexturePath;
            return base.ResolveIconTexturePath();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref typeChangeDefName, "typeChangeDefName");
            Scribe_Values.Look(ref typeChangeViaDropPod, "typeChangeViaDropPod", false);
            Scribe_Values.Look(ref typeChangeResolved, "typeChangeResolved", false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && typeChangeViaDropPod)
                InvalidateTravelerMaterialCache();
        }
    }
}
