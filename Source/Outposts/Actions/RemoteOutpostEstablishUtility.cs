using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public static class RemoteOutpostEstablishUtility
    {
        public static bool TryValidateColonySelection(
            IReadOnlyList<PlayerPawnRosterEntry> selected,
            out MapParent source,
            out List<PlayerPawnRosterEntry> entries,
            out string failReason,
            bool colonyOnlyRoster = false)
        {
            source = null;
            entries = new List<PlayerPawnRosterEntry>();
            failReason = null;

            if (selected == null || selected.Count == 0)
            {
                failReason = "TSA_WD_PawnTransfer_NoSelection".Translate();
                return false;
            }

            bool anyHumanlike = false;
            for (int i = 0; i < selected.Count; i++)
            {
                PlayerPawnRosterEntry e = selected[i];
                if (e == null || !e.isMovable || e.pawn == null) continue;
                if (e.locationKind != PlayerPawnLocationKind.Colony)
                {
                    if (colonyOnlyRoster) continue;
                    failReason = "TSA_WD_RemoteEstablish_ColonyOnly".Translate();
                    return false;
                }
                if (e.mapParent == null || e.mapParent.Map == null)
                {
                    failReason = "TSA_WD_RemoteEstablish_ColonyMapNotLoaded".Translate();
                    return false;
                }
                if (source == null) source = e.mapParent;
                else if (source != e.mapParent)
                {
                    failReason = "TSA_WD_RemoteEstablish_SingleColony".Translate();
                    return false;
                }
                if (!PlayerPawnTransferUtility.IsCapableOfImmediateTransfer(e.pawn, out string readyReason))
                {
                    failReason = readyReason;
                    return false;
                }
                if (e.pawn.RaceProps?.Humanlike == true)
                    anyHumanlike = true;
                entries.Add(e);
            }

            if (source == null || entries.Count == 0)
            {
                failReason = "TSA_WD_RemoteEstablish_InvalidSelection".Translate();
                return false;
            }
            if (!anyHumanlike)
            {
                failReason = "TSA_WD_RemoteEstablish_NeedHumanlike".Translate();
                return false;
            }

            if (!PlayerPawnTransferUtility.TryValidateColonyLeavingGroup(source, entries, out string leaveReason))
            {
                failReason = leaveReason;
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
            MapParent source,
            IReadOnlyList<PlayerPawnRosterEntry> entries,
            System.Action onSuccess,
            System.Action<string> onFail,
            System.Action onCancel = null)
        {
            List<Pawn> pawns = CollectPawns(entries);
            if (TryGetEstablishCarryWarning(pawns, outpostDef, out string warn))
            {
                Find.WindowStack.Add(new Dialog_MessageBox(
                    warn,
                    "Confirm".Translate(),
                    () =>
                    {
                        if (TryLaunch(tile, outpostDef, source, entries, out string fail))
                            onSuccess?.Invoke();
                        else
                            onFail?.Invoke(fail);
                    },
                    "GoBack".Translate(),
                    onCancel,
                    title: null,
                    buttonADestructive: false,
                    acceptAction: null,
                    cancelAction: onCancel));
                return;
            }

            if (TryLaunch(tile, outpostDef, source, entries, out string failImmediate))
                onSuccess?.Invoke();
            else
                onFail?.Invoke(failImmediate);
        }

        public static bool TryLaunch(
            int tile,
            WorldObjectDef outpostDef,
            MapParent source,
            IReadOnlyList<PlayerPawnRosterEntry> entries,
            out string failReason)
        {
            failReason = null;
            if (outpostDef == null || source?.Map == null)
            {
                failReason = "TSA_WD_PawnTransfer_CaravanFailed".Translate();
                return false;
            }
            if (!TryValidateColonySelection(entries, out MapParent validatedSource, out List<PlayerPawnRosterEntry> validated, out failReason, colonyOnlyRoster: true))
                return false;
            if (validatedSource != source)
            {
                failReason = "TSA_WD_RemoteEstablish_SingleColony".Translate();
                return false;
            }

            List<Pawn> pawns = CollectPawns(validated);
            Map map = source.Map;
            if (!CanEstablishAtRemote(tile, outpostDef, pawns, map, out failReason))
                return false;

            var cost = Outpost_EstablishmentRequirements.GetCost(outpostDef);
            var warehouses = ColonyWarehouseStockUtility.GetAllWarehouses();
            var goods = new List<Thing>();
            if (Outpost_EstablishmentRequirements.EnforceCost
                && !ColonyWarehouseStockUtility.TryDeductDeficitAsThings(map, warehouses, cost, pawns, goods, out failReason))
                return false;

            var removed = new List<Pawn>(pawns.Count);
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p == null || p.Destroyed || p.Dead) continue;
                if (!PrepareMapPawnForTransfer(p)) continue;
                removed.Add(p);
            }
            if (removed.Count == 0 || CountHumanlikes(removed) == 0)
            {
                // Refund goods we just created if launch failed before caravan exists.
                for (int i = 0; i < goods.Count; i++)
                    if (goods[i] != null && !goods[i].Destroyed)
                        goods[i].Destroy(DestroyMode.Vanish);
                failReason = "TSA_WD_PawnTransfer_CaravanFailed".Translate();
                return false;
            }

            Caravan caravan = CaravanMaker.MakeCaravan(removed, Faction.OfPlayer, source.Tile, true);
            VehicleFrameworkOutpostDissolveCompat.TryAutoBoardPawnsIntoSelectedVehicles(caravan, removed);
            if (caravan == null || caravan.Destroyed)
            {
                for (int i = 0; i < goods.Count; i++)
                    if (goods[i] != null && !goods[i].Destroyed)
                        goods[i].Destroy(DestroyMode.Vanish);
                failReason = "TSA_WD_PawnTransfer_CaravanFailed".Translate();
                return false;
            }

            for (int i = 0; i < goods.Count; i++)
            {
                Thing t = goods[i];
                if (t == null || t.Destroyed) continue;
                caravan.AddPawnOrItem(t, false);
            }

            Find.WorldSelector?.ClearSelection();
            Find.WorldSelector?.Select(caravan, false);

            PlanetTile destTile = PlanetSurfaceWorldActions.PlanetTileForWdTravel(tile, caravan);
            caravan.pather.StartPath(destTile, new CaravanArrivalAction_EstablishWdOutpost(outpostDef), false, false);

            Messages.Message(
                "TSA_WD_RemoteEstablish_Launched".Translate(outpostDef.LabelCap),
                caravan,
                MessageTypeDefOf.TaskCompletion,
                false);
            Window_AllPlayerPawns.InvalidateCache();
            RemoteOutpostEstablishSession.Clear();
            return true;
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
