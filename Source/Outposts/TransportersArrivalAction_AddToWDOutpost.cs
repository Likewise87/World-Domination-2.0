using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>OE-style: transport pods land directly into a WD outpost (virtual pawns), no caravan stop gate.
    /// During manual defense, pods instead drop onto the temporary battlefield as extra fighters.</summary>
    public class TransportersArrivalAction_AddToWDOutpost : TransportersArrivalAction
    {
        private WorldObject_WD_Outpost outpost;

        public override bool GeneratesMap => false;

        /// <summary>Avoid long-event / map-load flow so launching stays on the world layer.</summary>
        public override bool ShouldUseLongEvent(List<ActiveTransporterInfo> transporters, PlanetTile tile) => false;

        public TransportersArrivalAction_AddToWDOutpost() { }

        public TransportersArrivalAction_AddToWDOutpost(WorldObject_WD_Outpost addTo)
        {
            outpost = addTo;
        }

        public override void Arrived(List<ActiveTransporterInfo> transporters, PlanetTile tile)
        {
            if (outpost == null || !outpost.Spawned) return;

            List<Thing> list = new List<Thing>();
            for (int i = 0; i < transporters.Count; i++)
            {
                foreach (Thing thing in transporters[i].innerContainer)
                {
                    if (thing != null)
                        list.Add(thing);
                }
            }

            // Mid-flight / reinforce: drop onto the active defense map as extras (not into virtual garrison).
            if (outpost.ManualDefenseActive)
            {
                if (TryDropOntoActiveDefense(outpost, list))
                    return;

                Messages.Message("TSA_WD_OutpostDefense_FrozenDuringManualDefense".Translate(), MessageTypeDefOf.RejectInput, false);
                DumpTransporterThingsAsCaravan(list, outpost.Tile);
                return;
            }

            var humanlikes = new List<Pawn>();
            var mechanoids = new List<Pawn>();
            var animals = new List<Pawn>();
            var nonPawns = new List<Thing>();
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] is Pawn pawn)
                {
                    if (pawn.RaceProps != null && pawn.RaceProps.Humanlike && !OutpostPawnClassificationUtil.IsMechanoidWorker(pawn))
                        humanlikes.Add(pawn);
                    else if (OutpostPawnClassificationUtil.IsMechanoidWorker(pawn))
                        mechanoids.Add(pawn);
                    else
                        animals.Add(pawn);
                }
                else
                    nonPawns.Add(list[i]);
            }

            foreach (Pawn pawn in humanlikes)
            {
                if (outpost.AddPawn(pawn, null))
                    Messages.Message("TSA_WD_Pod_AddedPawn".Translate(pawn.LabelShortCap, outpost.LabelCap), outpost, MessageTypeDefOf.TaskCompletion, true);
            }

            if (mechanoids.Count > 0)
            {
                int storedMechs = 0;
                for (int i = 0; i < mechanoids.Count; i++)
                {
                    if (outpost.StoreMechanoid(mechanoids[i]))
                        storedMechs++;
                }
                if (storedMechs > 0)
                    Messages.Message("TSA_WD_StoredMechanoids_Message".Translate(storedMechs, outpost.LabelCap), outpost, MessageTypeDefOf.TaskCompletion, false);
            }

            if (animals.Count > 0)
            {
                int stored = 0;
                for (int i = 0; i < animals.Count; i++)
                {
                    if (outpost.StoreAnimalOrVehicle(animals[i]))
                        stored++;
                }

                if (stored > 0)
                    Messages.Message("TSA_WD_StoredTransportPawns_Message".Translate(stored, outpost.LabelCap), outpost, MessageTypeDefOf.TaskCompletion, false);
            }

            bool isWarehouse = Outpost_Production_Utils.IsWarehouseOutpost(outpost.def);
            if (isWarehouse)
            {
                var wh = CompOutpostWarehouse.Get(outpost);
                if (wh != null && nonPawns.Count > 0)
                {
                    int kinds = nonPawns.Count;
                    wh.TryDepositThings(nonPawns);
                    for (int i = 0; i < nonPawns.Count; i++)
                    {
                        Thing thing = nonPawns[i];
                        if (thing == null || thing.Destroyed) continue;
                        thing.Destroy(DestroyMode.Vanish);
                    }
                    Messages.Message("TSA_WD_Warehouse_PodDeposit".Translate(kinds, outpost.LabelCap),
                        outpost, MessageTypeDefOf.TaskCompletion, false);
                }
            }
            else
            {
                var logi = outpost.GetComponent<CompOutpostLogistics>();
                var settings = WorldDominationMod.settings;
                if (logi != null && settings != null && settings.foodLogisticsActive)
                {
                    float added = CompOutpostLogistics.ConvertLooseItemsToVirtualFood(nonPawns, logi);
                    if (added > 0f)
                        Messages.Message("TSA_WD_PodArrival_FoodConverted".Translate(
                            added.ToString("F1"), outpost.LabelCap),
                            outpost, MessageTypeDefOf.TaskCompletion, false);
                }

                foreach (Thing thing in nonPawns)
                {
                    if (thing == null || thing.Destroyed) continue;
                    Messages.Message("TSA_WD_PodArrival_ItemNotStored".Translate(thing.Label), MessageTypeDefOf.NeutralEvent);
                    thing.Destroy(DestroyMode.Vanish);
                }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref outpost, "wdOutpost");
        }

        public override FloatMenuAcceptanceReport StillValid(IEnumerable<IThingHolder> pods, PlanetTile destinationTile)
        {
            if (outpost == null || !outpost.Spawned || outpost.Faction != Faction.OfPlayer)
                return false;
            if (outpost.ManualDefenseActive)
            {
                if (WD_MapComponent_OutpostDefense.FindActiveMapFor(outpost) == null)
                    return FloatMenuAcceptanceReport.WithFailMessage("TSA_WD_OutpostDefense_FrozenDuringManualDefense".Translate());
                return outpost.Tile == destinationTile.tileId;
            }
            return outpost.Tile == destinationTile.tileId;
        }

        /// <summary>Same pattern as Outposts Expanded <c>GetFloatMenuOptions</c>.</summary>
        public static IEnumerable<FloatMenuOption> GetFloatMenuOptions(IEnumerable<IThingHolder> pods, Action<PlanetTile, TransportersArrivalAction> launchAction, WorldObject_WD_Outpost targetOutpost)
        {
            PlanetLayer layer = PlanetSurfaceWorldActions.LayerOf(targetOutpost);
            PlanetTile pt = new PlanetTile(targetOutpost.Tile, layer);
            bool reinforcing = targetOutpost != null && targetOutpost.ManualDefenseActive;
            string label = reinforcing
                ? "TSA_WD_Pod_ReinforceDefense".Translate(targetOutpost.LabelCap)
                : "TSA_WD_Pod_AddToOutpost".Translate(targetOutpost.LabelCap);
            return TransportersArrivalActionUtility.GetFloatMenuOptions<TransportersArrivalAction_AddToWDOutpost>(
                () => CanOfferAddOrReinforce(targetOutpost),
                () => new TransportersArrivalAction_AddToWDOutpost(targetOutpost),
                label,
                launchAction,
                pt,
                launch => launch());
        }

        private static bool CanOfferAddOrReinforce(WorldObject_WD_Outpost targetOutpost)
        {
            if (targetOutpost == null || !targetOutpost.Spawned || targetOutpost.Faction != Faction.OfPlayer)
                return false;
            if (!targetOutpost.ManualDefenseActive)
                return true;
            return WD_MapComponent_OutpostDefense.FindActiveMapFor(targetOutpost) != null;
        }

        /// <summary>Drop pod contents onto the temporary defense map. Extras return as a caravan on win/loss.</summary>
        private static bool TryDropOntoActiveDefense(WorldObject_WD_Outpost outpost, List<Thing> things)
        {
            if (outpost == null || things == null || things.Count == 0)
                return false;

            Map map = WD_MapComponent_OutpostDefense.FindActiveMapFor(outpost);
            if (map == null)
                return false;

            var toDrop = new List<Thing>(things.Count);
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if (thing == null || thing.Destroyed) continue;
                thing.holdingOwner?.Remove(thing);
                if (thing is Pawn pawn)
                {
                    if (pawn.Spawned) pawn.DeSpawn();
                    if (pawn.Faction != Faction.OfPlayer)
                        pawn.SetFaction(Faction.OfPlayer);
                }
                toDrop.Add(thing);
            }

            if (toDrop.Count == 0)
                return false;

            IntVec3 dropCell = FindDefenseDropCell(map);
            DropPodUtility.DropThingsNear(dropCell, map, toDrop);
            Messages.Message(
                "TSA_WD_Pod_ReinforcedDefense".Translate(outpost.LabelCap),
                new LookTargets(dropCell, map),
                MessageTypeDefOf.PositiveEvent,
                false);
            CameraJumper.TryJump(new GlobalTargetInfo(dropCell, map));
            return true;
        }

        private static IntVec3 FindDefenseDropCell(Map map)
        {
            IntVec3 cell;
            if (CellFinderLoose.TryGetRandomCellWith(
                    c => c.InBounds(map) && c.Standable(map) && !c.Fogged(map),
                    map,
                    1000,
                    out cell))
                return cell;
            return DropCellFinder.TradeDropSpot(map);
        }

        /// <summary>When reinforce fails mid-flight, dump pod contents as a caravan on the tile.</summary>
        private static void DumpTransporterThingsAsCaravan(List<Thing> things, int tile)
        {
            if (things == null || things.Count == 0) return;
            var pawns = new List<Pawn>();
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i] is not Pawn pawn || pawn.Destroyed) continue;
                pawn.holdingOwner?.Remove(pawn);
                if (pawn.Spawned) pawn.DeSpawn();
                if (pawn.Faction != Faction.OfPlayer)
                    pawn.SetFaction(Faction.OfPlayer);
                pawns.Add(pawn);
            }
            if (pawns.Count == 0) return;

            Caravan caravan = CaravanMaker.MakeCaravan(pawns, Faction.OfPlayer, tile, true);
            if (caravan == null) return;

            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if (thing == null || thing.Destroyed || thing is Pawn) continue;
                thing.holdingOwner?.Remove(thing);
                caravan.AddPawnOrItem(thing, true);
            }
        }

        /// <summary>Legacy cleanup for non-humanlike pod pawns; kept as a safety fallback for older call sites.</summary>
        private static void VanishPodArrivalPawn(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed) return;

            VehicleFrameworkOutpostDissolveCompat.TryEjectPawnFromHostingVehicle(pawn);
            if (pawn.Destroyed) return;

            pawn.ownership?.UnclaimAll();
            if (pawn.Spawned) pawn.DeSpawn();
            pawn.holdingOwner?.Remove(pawn);
            if (Find.WorldPawns != null && Find.WorldPawns.Contains(pawn))
                Find.WorldPawns.RemovePawn(pawn);
            if (!pawn.Destroyed)
                pawn.Destroy(DestroyMode.Vanish);
        }
    }
}
