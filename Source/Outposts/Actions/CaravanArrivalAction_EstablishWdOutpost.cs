using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Player caravan arrival: try to found a WD outpost. Soft-fails to a normal parked caravan.
    /// Any retarget/stop clears this action via vanilla pathing (mission forgotten).
    /// </summary>
    public class CaravanArrivalAction_EstablishWdOutpost : CaravanArrivalAction
    {
        private WorldObjectDef outpostDef;

        public CaravanArrivalAction_EstablishWdOutpost() { }

        public CaravanArrivalAction_EstablishWdOutpost(WorldObjectDef outpostDef)
        {
            this.outpostDef = outpostDef;
        }

        public WorldObjectDef OutpostDef => outpostDef;

        public override string Label => "TSA_WD_RemoteEstablish_ArrivalLabel".Translate(outpostDef?.LabelCap ?? "");

        public override string ReportString => "TSA_WD_RemoteEstablish_ArrivalReport".Translate(outpostDef?.LabelCap ?? "");

        public override void Arrived(Caravan caravan)
        {
            if (caravan == null || caravan.Destroyed) return;
            int tile = caravan.Tile.tileId;

            string reason = null;
            if (outpostDef == null
                || !Outpost_EstablishmentRequirements.CaravanFullyStoppedOnTileForEstablishment(caravan, tile, out reason)
                || !Outpost_EstablishmentRequirements.CanEstablishAt(tile, outpostDef, caravan, out reason))
            {
                Messages.Message(
                    "TSA_WD_RemoteEstablish_Failed".Translate(reason ?? ""),
                    caravan,
                    MessageTypeDefOf.RejectInput,
                    false);
                return;
            }

            if (!TryFinalizeFromCaravan(caravan, tile, outpostDef))
            {
                Messages.Message(
                    "TSA_WD_RemoteEstablish_Failed".Translate(outpostDef.LabelCap),
                    caravan,
                    MessageTypeDefOf.RejectInput,
                    false);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref outpostDef, "outpostDef");
        }

        public static string GetInspectLine(Caravan caravan)
        {
            if (caravan?.pather?.ArrivalAction is CaravanArrivalAction_EstablishWdOutpost action
                && action.outpostDef != null)
                return "TSA_WD_RemoteEstablish_Inspect".Translate(action.outpostDef.LabelCap).ToString();
            return null;
        }

        /// <summary>Shared finalize used by arrival (mirrors caravan establish in Dialog_OutpostSelection).</summary>
        public static bool TryFinalizeFromCaravan(Caravan fromCaravan, int tile, WorldObjectDef outpostDef)
        {
            if (fromCaravan == null || fromCaravan.Destroyed || outpostDef == null) return false;
            if (!Outpost_EstablishmentRequirements.TryDeductCost(fromCaravan, outpostDef))
                return false;

            ConquestOpportunityUtility.DestroyConquestRuinsAt(tile, -1);
            Outpost_EstablishmentRequirements.DestroyVanillaAbandonedCampsAt(tile);

            var outpost = (WorldObject_WD_Outpost)WorldObjectMaker.MakeWorldObject(outpostDef);
            outpost.Tile = tile;
            outpost.SetFaction(Faction.OfPlayer);
            outpost.Name = Dialog_OutpostSelection.GenerateOutpostNamePublic(outpostDef, tile);
            Find.WorldObjects.Add(outpost);
            outpost.StartProductionTimerIfNeeded();

            var pawnSource = fromCaravan.PawnsListForReading;
            var humanlike = new List<Pawn>();
            for (int pi = 0; pi < pawnSource.Count; pi++)
            {
                var p = pawnSource[pi];
                if (p != null && p.RaceProps != null && p.RaceProps.Humanlike && !p.Dead)
                    humanlike.Add(p);
            }
            for (int pi = 0; pi < humanlike.Count; pi++)
            {
                if (humanlike[pi] == null || humanlike[pi].Destroyed) continue;
                outpost.AddCaravanPawnToOutpost(humanlike[pi], fromCaravan);
            }

            if (VehicleFrameworkOutpostDissolveCompat.CaravanIsRegisteredOnWorld(fromCaravan))
                outpost.TryFinishDissolveCaravanAfterFoundingIfStillPresent(fromCaravan);
            VehicleFrameworkOutpostDissolveCompat.DestroyCaravanWorldObjectAfterOutpostDissolve(fromCaravan);

            var initialLogi = outpost.GetComponent<CompOutpostLogistics>();
            if (initialLogi != null && initialLogi.currentFood <= 0.01f)
            {
                int pawnCount = outpost.PawnCount;
                float baseFood = 50f;
                float perPawnFood = 20f * pawnCount;
                float initialFood = Mathf.Max(baseFood, perPawnFood);
                initialLogi.currentFood = Mathf.Min(initialLogi.EffectiveMaxFood, initialFood);
            }

            VehicleFrameworkOutpostDissolveCompat.DestroyAllPlayerCaravansOnTileAfterOutpostFounding(tile);

            CompViralSpread.ApplyPlayerOutpostFoundingShields(outpost);

            Find.WorldSelector?.ClearSelection();
            Find.WorldSelector?.Select(outpost, false);
            CameraJumper.TryJumpAndSelect(outpost);
            Window_AllPlayerPawns.InvalidateCache();
            Dialog_NameNewOutpost.Open(outpost);
            return true;
        }
    }
}
