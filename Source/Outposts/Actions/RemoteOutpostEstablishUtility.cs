using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public static class RemoteOutpostEstablishUtility
    {
        /// <summary>
        /// Single-origin founding selection: one colony map or one WD outpost. Mixed origins rejected.
        /// </summary>
        public static bool TryValidateFoundingSelection(
            IReadOnlyList<PlayerPawnRosterEntry> selected,
            out WorldObject origin,
            out List<PlayerPawnRosterEntry> entries,
            out string failReason)
        {
            origin = null;
            entries = new List<PlayerPawnRosterEntry>();
            failReason = null;

            if (selected == null || selected.Count == 0)
            {
                failReason = "TSA_WD_PawnTransfer_NoSelection".Translate();
                return false;
            }

            MapParent colonyOrigin = null;
            WorldObject_WD_Outpost outpostOrigin = null;
            bool anyHumanlike = false;

            for (int i = 0; i < selected.Count; i++)
            {
                PlayerPawnRosterEntry e = selected[i];
                if (e == null || !e.isMovable) continue;

                bool isShuttle = e.outpostRole == PlayerPawnOutpostRole.StoredShuttle && e.shuttle != null;
                if (!isShuttle && e.pawn == null) continue;

                if (e.locationKind == PlayerPawnLocationKind.Colony)
                {
                    if (outpostOrigin != null)
                    {
                        failReason = "TSA_WD_RemoteEstablish_SingleOrigin".Translate();
                        return false;
                    }
                    if (e.mapParent == null || e.mapParent.Map == null)
                    {
                        failReason = "TSA_WD_RemoteEstablish_ColonyMapNotLoaded".Translate();
                        return false;
                    }
                    if (colonyOrigin == null) colonyOrigin = e.mapParent;
                    else if (colonyOrigin != e.mapParent)
                    {
                        failReason = "TSA_WD_RemoteEstablish_SingleOrigin".Translate();
                        return false;
                    }
                }
                else if (e.locationKind == PlayerPawnLocationKind.Outpost)
                {
                    if (colonyOrigin != null)
                    {
                        failReason = "TSA_WD_RemoteEstablish_SingleOrigin".Translate();
                        return false;
                    }
                    WorldObject_WD_Outpost op = e.sourceOutpost;
                    if (op == null || op.Destroyed)
                    {
                        failReason = "TSA_WD_RemoteEstablish_InvalidSelection".Translate();
                        return false;
                    }
                    if (outpostOrigin == null) outpostOrigin = op;
                    else if (outpostOrigin != op)
                    {
                        failReason = "TSA_WD_RemoteEstablish_SingleOrigin".Translate();
                        return false;
                    }
                }
                else
                {
                    failReason = "TSA_WD_RemoteEstablish_WrongLocationKind".Translate();
                    return false;
                }

                if (e.pawn != null)
                {
                    if (!PlayerPawnTransferUtility.IsCapableOfImmediateTransfer(e.pawn, out string readyReason))
                    {
                        failReason = readyReason;
                        return false;
                    }
                    if (e.pawn.RaceProps?.Humanlike == true)
                        anyHumanlike = true;
                }

                entries.Add(e);
            }

            if (entries.Count == 0 || (colonyOrigin == null && outpostOrigin == null))
            {
                failReason = "TSA_WD_RemoteEstablish_InvalidSelection".Translate();
                return false;
            }
            if (!anyHumanlike)
            {
                failReason = "TSA_WD_RemoteEstablish_NeedHumanlike".Translate();
                return false;
            }

            if (colonyOrigin != null)
            {
                if (!PlayerPawnTransferUtility.TryValidateColonyLeavingGroup(colonyOrigin, entries, out string leaveReason))
                {
                    failReason = leaveReason;
                    return false;
                }
                origin = colonyOrigin;
                return true;
            }

            if (!PlayerPawnTransferUtility.TryValidateOutpostLeavingGroup(outpostOrigin, entries, out string outpostLeave))
            {
                failReason = outpostLeave;
                return false;
            }
            origin = outpostOrigin;
            return true;
        }

        /// <summary>Legacy name; redirects to <see cref="TryValidateFoundingSelection"/> and requires a colony map parent.</summary>
        public static bool TryValidateColonySelection(
            IReadOnlyList<PlayerPawnRosterEntry> selected,
            out MapParent source,
            out List<PlayerPawnRosterEntry> entries,
            out string failReason,
            bool colonyOnlyRoster = false)
        {
            source = null;
            if (!TryValidateFoundingSelection(selected, out WorldObject origin, out entries, out failReason))
                return false;
            if (origin is WorldObject_WD_Outpost)
            {
                failReason = colonyOnlyRoster
                    ? "TSA_WD_RemoteEstablish_WrongLocationKind".Translate()
                    : "TSA_WD_RemoteEstablish_SingleOrigin".Translate();
                return false;
            }
            source = origin as MapParent;
            if (source == null)
            {
                failReason = "TSA_WD_RemoteEstablish_InvalidSelection".Translate();
                return false;
            }
            return true;
        }

        public static List<Pawn> CollectPawns(IReadOnlyList<PlayerPawnRosterEntry> entries)
        {
            var list = new List<Pawn>();
            if (entries == null) return list;
            for (int i = 0; i < entries.Count; i++)
            {
                Pawn p = entries[i]?.pawn;
                if (p != null && !p.Destroyed && !p.Dead)
                    list.Add(p);
            }
            return list;
        }

        public static int GetCumulativeSkill(IReadOnlyList<Pawn> pawns, SkillDef skillDef)
        {
            if (pawns == null || skillDef == null) return 0;
            int total = 0;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p?.skills == null || p.RaceProps?.Humanlike != true || p.Dead) continue;
                var sk = p.skills.GetSkill(skillDef);
                if (sk != null) total += sk.Level;
            }
            return total;
        }

        public static int CountHumanlikes(IReadOnlyList<Pawn> pawns)
        {
            if (pawns == null) return 0;
            int n = 0;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p != null && p.RaceProps?.Humanlike == true && !p.Dead) n++;
            }
            return n;
        }

        public static bool CanEstablishAtRemote(
            int tile,
            WorldObjectDef outpostDef,
            IReadOnlyList<Pawn> pawns,
            Map colonyMap,
            out string reason)
        {
            reason = null;
            if (!Outpost_EstablishmentRequirements.CanEstablishAt(tile, outpostDef, null, out reason))
                return false;

            if (Outpost_EstablishmentRequirements.EnforceMinPawns)
            {
                int need = Outpost_EstablishmentRequirements.GetMinPawnsToFound(outpostDef);
                int have = CountHumanlikes(pawns);
                if (have < need)
                {
                    reason = "TSA_WD_Establish_MinPawns".Translate(outpostDef?.label ?? "Outpost", need, have);
                    return false;
                }
            }

            if (Outpost_EstablishmentRequirements.EnforceMinSkill)
            {
                var ext = outpostDef?.GetModExtension<OutpostDefExtension>();
                if (ext?.MinCumulativeSkill != null)
                {
                    foreach (var set in ext.MinCumulativeSkill)
                    {
                        if (set == null) continue;
                        foreach (var kv in set.GetRequirements())
                        {
                            if (kv.Key == null || kv.Value <= 0) continue;
                            int have = GetCumulativeSkill(pawns, kv.Key);
                            if (have < kv.Value)
                            {
                                reason = "TSA_WD_Establish_MinSkill".Translate(kv.Value, kv.Key.LabelCap, have);
                                return false;
                            }
                        }
                    }
                }
            }

            if (Outpost_EstablishmentRequirements.EnforceCost)
            {
                var cost = Outpost_EstablishmentRequirements.GetCost(outpostDef);
                var warehouses = ColonyWarehouseStockUtility.GetAllWarehouses();
                if (!ColonyWarehouseStockUtility.HasCosts(colonyMap, warehouses, cost, pawns, out reason))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// True when selected pawns lack free carry capacity for establishment deficit goods
        /// (same load that <see cref="TryLaunch"/> adds after deducting from colony/warehouses).
        /// Soft warning only; caller may still proceed.
        /// </summary>
        public static bool TryGetEstablishCarryWarning(
            IReadOnlyList<Pawn> pawns,
            WorldObjectDef outpostDef,
            out string message)
        {
            message = null;
            if (!Outpost_EstablishmentRequirements.EnforceCost) return false;
            if (pawns == null || pawns.Count == 0 || outpostDef == null) return false;

            var cost = Outpost_EstablishmentRequirements.GetCost(outpostDef);
            if (cost == null || cost.Count == 0) return false;

            float capacity = 0f;
            float usage = 0f;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p == null || p.Destroyed || p.Dead) continue;
                capacity += MassUtility.Capacity(p);
                usage += MassUtility.GearAndInventoryMass(p);
            }

            float payloadMass = 0f;
            for (int i = 0; i < cost.Count; i++)
            {
                ThingDefCountClass c = cost[i];
                if (c?.thingDef == null || c.count <= 0) continue;
                int onPawns = ColonyWarehouseStockUtility.CountOnPawns(pawns, c.thingDef);
                int deficit = Mathf.Max(0, c.count - onPawns);
                if (deficit <= 0) continue;
                float unitMass = c.thingDef.GetStatValueAbstract(StatDefOf.Mass);
                if (unitMass <= 0f) continue;
                payloadMass += unitMass * deficit;
            }

            if (payloadMass <= 0.01f) return false;

            float freeCapacity = Mathf.Max(0f, capacity - usage);
            const float epsilon = 0.05f;
            if (payloadMass <= freeCapacity + epsilon) return false;

            float shortBy = payloadMass - freeCapacity;
            message = "TSA_WD_RemoteEstablish_CarryTooHeavy".Translate(
                payloadMass.ToString("F0"),
                freeCapacity.ToString("F0"),
                shortBy.ToString("F0"));
            return true;
        }

        /// <summary>
        /// Soft confirm when overweight, then <see cref="TryLaunch"/>.
        /// <paramref name="onCancel"/> runs when the player backs out of the overweight dialog
        /// (e.g. return to pawn selection).
        /// </summary>
        public static void LaunchAfterOptionalCarryConfirm(
            int tile,
            WorldObjectDef outpostDef,
            WorldObject origin,
            IReadOnlyList<PlayerPawnRosterEntry> entries,
            System.Action onSuccess,
            System.Action<string> onFail,
            System.Action onCancel = null,
            bool viaDropPod = false)
        {
            void ProceedLaunch()
            {
                if (viaDropPod)
                {
                    var origins = new List<WorldObject>();
                    PlayerPawnDropPodUtility.AddOriginSlots(origins, origin, entries?.Count ?? 0);
                    PlayerPawnDropPodUtility.ConfirmIfShortThen(origins, alloc =>
                    {
                        if (alloc.abortCount > 0 && alloc.podCount == 0 && alloc.landCount == 0)
                        {
                            onFail?.Invoke("TSA_WD_PawnDropPod_AllWouldAbort".Translate());
                            return;
                        }
                        // Establish party stays together: pod only when every pawn can pod.
                        bool usePod = alloc.need > 0 && alloc.podCount == alloc.need;
                        System.Action launch = () =>
                        {
                            if (TryLaunch(tile, outpostDef, origin, entries, out string fail, viaDropPod: usePod))
                                onSuccess?.Invoke();
                            else
                                onFail?.Invoke(fail);
                        };
                        if (usePod
                            && origin != null
                            && !origin.Destroyed
                            && PlayerPawnDropPodUtility.ConfirmHostileAaThen(
                                origin.Tile.tileId, tile, launch))
                            return;
                        launch();
                    }, onCancel);
                    return;
                }

                if (TryLaunch(tile, outpostDef, origin, entries, out string failImmediate, viaDropPod: false))
                    onSuccess?.Invoke();
                else
                    onFail?.Invoke(failImmediate);
            }

            List<Pawn> pawns = CollectPawns(entries);
            if (TryGetEstablishCarryWarning(pawns, outpostDef, out string warn) && !viaDropPod)
            {
                Find.WindowStack.Add(new Dialog_MessageBox(
                    warn,
                    "Confirm".Translate(),
                    ProceedLaunch,
                    "GoBack".Translate(),
                    onCancel,
                    title: null,
                    buttonADestructive: false,
                    acceptAction: null,
                    cancelAction: onCancel));
                return;
            }

            ProceedLaunch();
        }

        public static bool TryLaunch(
            int tile,
            WorldObjectDef outpostDef,
            WorldObject origin,
            IReadOnlyList<PlayerPawnRosterEntry> entries,
            out string failReason,
            bool viaDropPod = false)
        {
            failReason = null;
            if (outpostDef == null || origin == null || origin.Destroyed)
            {
                failReason = "TSA_WD_PawnTransfer_CaravanFailed".Translate();
                return false;
            }

            if (!TryValidateFoundingSelection(entries, out WorldObject validatedOrigin, out List<PlayerPawnRosterEntry> validated, out failReason))
                return false;
            if (validatedOrigin != origin)
            {
                failReason = "TSA_WD_RemoteEstablish_SingleOrigin".Translate();
                return false;
            }

            Map colonyMap = Outpost_PowerPlant.GetPlayerColonyMap();
            if (colonyMap == null)
            {
                failReason = "TSA_WD_TileFirstEstablish_NoColony".Translate();
                return false;
            }

            List<Pawn> pawns = CollectPawns(validated);
            if (!CanEstablishAtRemote(tile, outpostDef, pawns, colonyMap, out failReason))
                return false;

            var outpostOrigin = origin as WorldObject_WD_Outpost;
            var colonyOrigin = origin as MapParent;
            if (colonyOrigin is WorldObject_WD_Outpost) colonyOrigin = null;

            if (viaDropPod)
            {
                if (SelectionHasShuttle(validated))
                {
                    failReason = "TSA_WD_RemoteEstablish_ShuttleNeedsLand".Translate();
                    return false;
                }
                if (!RapidResponseUtility.TransportPodsResearched())
                {
                    failReason = "TSA_WD_RapidResponse_DropPodsNeedResearch".Translate();
                    return false;
                }
                if (!PlayerPawnDropPodUtility.InDropPodRange(origin, tile))
                {
                    failReason = "TSA_WD_PawnDropPod_OutOfRange".Translate();
                    return false;
                }
            }

            var cost = Outpost_EstablishmentRequirements.GetCost(outpostDef);
            var warehouses = ColonyWarehouseStockUtility.GetAllWarehouses();
            var goods = new List<Thing>();
            if (Outpost_EstablishmentRequirements.EnforceCost
                && !ColonyWarehouseStockUtility.TryDeductDeficitAsThings(colonyMap, warehouses, cost, pawns, goods, out failReason))
                return false;

            if (viaDropPod)
            {
                int podCost = pawns.Count * PlayerPawnDropPodUtility.ComponentCostPerLaunch;
                if (!PlayerPawnDropPodUtility.TryConsumeComponents(podCost, out failReason))
                {
                    DestroyGoods(goods);
                    return false;
                }
            }

            if (outpostOrigin != null)
            {
                if (!TryLaunchFromOutpost(tile, outpostDef, outpostOrigin, validated, goods, viaDropPod, out failReason))
                {
                    DestroyGoods(goods);
                    return false;
                }
            }
            else if (colonyOrigin != null)
            {
                if (!TryLaunchFromColony(tile, outpostDef, colonyOrigin, validated, goods, viaDropPod, out failReason))
                {
                    DestroyGoods(goods);
                    return false;
                }
            }
            else
            {
                DestroyGoods(goods);
                failReason = "TSA_WD_PawnTransfer_CaravanFailed".Translate();
                return false;
            }

            Window_AllPlayerPawns.InvalidateCache();
            WITab_Outpost_Pawns.InvalidateCache();
            Window_Prisoners.InvalidateCache();
            RemoteOutpostEstablishSession.Clear();
            return true;
        }

        private static bool TryLaunchFromColony(
            int tile,
            WorldObjectDef outpostDef,
            MapParent source,
            List<PlayerPawnRosterEntry> validated,
            List<Thing> goods,
            bool viaDropPod,
            out string failReason)
        {
            failReason = null;
            if (source?.Map == null)
            {
                failReason = "TSA_WD_RemoteEstablish_ColonyMapNotLoaded".Translate();
                return false;
            }

            var removed = new List<Pawn>();
            for (int i = 0; i < validated.Count; i++)
            {
                Pawn p = validated[i].pawn;
                if (p == null || p.Destroyed || p.Dead) continue;
                if (!PrepareMapPawnForTransfer(p)) continue;
                removed.Add(p);
            }
            if (removed.Count == 0 || CountHumanlikes(removed) == 0)
            {
                failReason = "TSA_WD_PawnTransfer_CaravanFailed".Translate();
                return false;
            }

            if (viaDropPod)
            {
                var deliveryItems = ThingsToDeliveryCounts(goods);
                DestroyGoods(goods);
                var traveler = WorldActions_Traveler.SpawnPlayerPawnDropPodTraveler(
                    source, tile, removed, target: null, deliveryItems, outpostDef);
                if (traveler == null)
                {
                    for (int i = 0; i < removed.Count; i++)
                    {
                        Pawn p = removed[i];
                        if (p == null || p.Destroyed) continue;
                        if (source.HasMap)
                            GenSpawn.Spawn(p, CellFinder.RandomEdgeCell(source.Map), source.Map);
                    }
                    failReason = "TSA_WD_PawnTransfer_CaravanFailed".Translate();
                    return false;
                }
                AnnounceLaunched(outpostDef, traveler);
                return true;
            }

            Caravan caravan = CaravanMaker.MakeCaravan(removed, Faction.OfPlayer, source.Tile, true);
            VehicleFrameworkOutpostDissolveCompat.TryAutoBoardPawnsIntoSelectedVehicles(caravan, removed);
            if (caravan == null || caravan.Destroyed)
            {
                failReason = "TSA_WD_PawnTransfer_CaravanFailed".Translate();
                return false;
            }

            AttachGoodsAndPath(caravan, goods, tile, outpostDef);
            AnnounceLaunched(outpostDef, caravan);
            return true;
        }

        private static bool TryLaunchFromOutpost(
            int tile,
            WorldObjectDef outpostDef,
            WorldObject_WD_Outpost source,
            List<PlayerPawnRosterEntry> validated,
            List<Thing> goods,
            bool viaDropPod,
            out string failReason)
        {
            failReason = null;
            SplitOutpostGroup(source, validated,
                out List<Pawn> occupants,
                out List<Pawn> stored,
                out List<Pawn> mechs,
                out List<Pawn> captives,
                out List<Building_PassengerShuttle> shuttles);

            if (viaDropPod)
            {
                var removed = new List<Pawn>();
                for (int i = 0; i < occupants.Count; i++)
                {
                    Pawn r = source.RemovePawn(occupants[i]);
                    if (r != null && !r.Destroyed && !r.Dead) removed.Add(r);
                }
                for (int i = 0; i < stored.Count; i++)
                {
                    Pawn r = source.RemoveStoredAnimalOrVehicle(stored[i]);
                    if (r != null && !r.Destroyed && !r.Dead) removed.Add(r);
                }
                for (int i = 0; i < mechs.Count; i++)
                {
                    Pawn r = source.RemoveStoredMechanoid(mechs[i]);
                    if (r != null && !r.Destroyed && !r.Dead) removed.Add(r);
                }
                for (int i = 0; i < captives.Count; i++)
                {
                    if (!source.TryDetachPrisonerForTransfer(captives[i], out Pawn detached)) continue;
                    if (detached != null && !detached.Destroyed && !detached.Dead)
                        removed.Add(detached);
                }

                if (removed.Count == 0 || CountHumanlikes(removed) == 0)
                {
                    failReason = "TSA_WD_PawnTransfer_CaravanFailed".Translate();
                    return false;
                }

                var deliveryItems = ThingsToDeliveryCounts(goods);
                DestroyGoods(goods);
                var traveler = WorldActions_Traveler.SpawnPlayerPawnDropPodTraveler(
                    source, tile, removed, target: null, deliveryItems, outpostDef);
                if (traveler == null)
                {
                    for (int i = 0; i < removed.Count; i++)
                    {
                        Pawn p = removed[i];
                        if (p == null || p.Destroyed) continue;
                        source.AddPawn(p, null!);
                    }
                    failReason = "TSA_WD_PawnTransfer_CaravanFailed".Translate();
                    return false;
                }

                if (source.Occupants != null && source.Occupants.Count == 0 && !source.Destroyed)
                    source.Destroy();
                AnnounceLaunched(outpostDef, traveler);
                return true;
            }

            // Land: shared remove helper creates the caravan at the outpost tile.
            source.RemovePawnsAndStoredTransportAndMechanoidsAsCaravan(
                occupants, stored, mechs, shuttles, captives);

            Caravan caravan = Find.WorldSelector?.SingleSelectedObject as Caravan;
            if (caravan == null || caravan.Destroyed || caravan.Tile != source.Tile)
                caravan = Find.WorldObjects.PlayerControlledCaravanAt(source.Tile);

            if (caravan == null || caravan.Destroyed)
            {
                failReason = "TSA_WD_PawnTransfer_CaravanFailed".Translate();
                return false;
            }

            int moved = occupants.Count + stored.Count + mechs.Count + captives.Count;
            PlayerPawnTransferUtility.PackTravelPemmicanFromOutpost(caravan, moved, source);

            AttachGoodsAndPath(caravan, goods, tile, outpostDef);
            AnnounceLaunched(outpostDef, caravan);
            return true;
        }

        private static void SplitOutpostGroup(
            WorldObject_WD_Outpost source,
            List<PlayerPawnRosterEntry> group,
            out List<Pawn> occupants,
            out List<Pawn> stored,
            out List<Pawn> mechs,
            out List<Pawn> captives,
            out List<Building_PassengerShuttle> shuttles)
        {
            occupants = new List<Pawn>();
            stored = new List<Pawn>();
            mechs = new List<Pawn>();
            captives = new List<Pawn>();
            shuttles = new List<Building_PassengerShuttle>();
            if (source == null || group == null) return;

            for (int i = 0; i < group.Count; i++)
            {
                PlayerPawnRosterEntry entry = group[i];
                if (entry.outpostRole == PlayerPawnOutpostRole.StoredShuttle)
                {
                    if (entry.shuttle is Building_PassengerShuttle shuttle
                        && !shuttle.Destroyed
                        && source.StoredPassengerShuttles.Contains(shuttle))
                        shuttles.Add(shuttle);
                    continue;
                }

                Pawn p = entry.pawn;
                if (p == null) continue;
                switch (entry.outpostRole)
                {
                    case PlayerPawnOutpostRole.Occupant:
                        if (source.Occupants.Contains(p)) occupants.Add(p);
                        break;
                    case PlayerPawnOutpostRole.StoredTransport:
                        if (source.StoredAnimalsAndVehicles.Contains(p)) stored.Add(p);
                        break;
                    case PlayerPawnOutpostRole.StoredMechanoid:
                        if (source.StoredMechanoids.Contains(p)) mechs.Add(p);
                        break;
                    case PlayerPawnOutpostRole.Prisoner:
                        if (source.Prisoners.Contains(p)) captives.Add(p);
                        break;
                }
            }
        }

        private static bool SelectionHasShuttle(IReadOnlyList<PlayerPawnRosterEntry> entries)
        {
            if (entries == null) return false;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i]?.outpostRole == PlayerPawnOutpostRole.StoredShuttle)
                    return true;
            }
            return false;
        }

        private static void AttachGoodsAndPath(
            Caravan caravan,
            List<Thing> goods,
            int tile,
            WorldObjectDef outpostDef)
        {
            for (int i = 0; i < goods.Count; i++)
            {
                Thing t = goods[i];
                if (t == null || t.Destroyed) continue;
                caravan.AddPawnOrItem(t, false);
            }
            goods.Clear();

            Find.WorldSelector?.ClearSelection();
            Find.WorldSelector?.Select(caravan, false);

            PlanetTile destTile = PlanetSurfaceWorldActions.PlanetTileForWdTravel(tile, caravan);
            caravan.pather.StartPath(destTile, new CaravanArrivalAction_EstablishWdOutpost(outpostDef), false, false);
        }

        private static void AnnounceLaunched(WorldObjectDef outpostDef, WorldObject lookTarget)
        {
            Find.WorldSelector?.ClearSelection();
            if (lookTarget != null)
                Find.WorldSelector?.Select(lookTarget, false);
            Messages.Message(
                "TSA_WD_RemoteEstablish_Launched".Translate(outpostDef.LabelCap),
                lookTarget,
                MessageTypeDefOf.TaskCompletion,
                false);
        }

        private static void DestroyGoods(List<Thing> goods)
        {
            if (goods == null) return;
            for (int i = 0; i < goods.Count; i++)
            {
                if (goods[i] != null && !goods[i].Destroyed)
                    goods[i].Destroy(DestroyMode.Vanish);
            }
            goods.Clear();
        }

        private static List<ThingDefCountClass> ThingsToDeliveryCounts(List<Thing> goods)
        {
            var merged = new Dictionary<ThingDef, int>();
            if (goods == null) return new List<ThingDefCountClass>();
            for (int i = 0; i < goods.Count; i++)
            {
                Thing t = goods[i];
                if (t?.def == null || t.Destroyed || t.stackCount <= 0) continue;
                if (!merged.ContainsKey(t.def)) merged[t.def] = 0;
                merged[t.def] += t.stackCount;
            }
            var list = new List<ThingDefCountClass>(merged.Count);
            foreach (var kv in merged)
                list.Add(new ThingDefCountClass(kv.Key, kv.Value));
            return list;
        }

        private static bool PrepareMapPawnForTransfer(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead) return false;
            pawn.ownership?.UnclaimAll();
            VehicleFrameworkOutpostDissolveCompat.TryEjectPawnFromHostingVehicle(pawn);
            if (pawn.Spawned)
                pawn.DeSpawn();
            pawn.holdingOwner?.Remove(pawn);
            return !pawn.Destroyed && !pawn.Dead;
        }
    }
}
