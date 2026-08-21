using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Collect clash loot/prisoner candidates, apply mass-gated picks, then reform the player caravan and tear down the encounter map.</summary>
    public static class WD_CaravanClashLootUtility
    {
        private const int MaxItemRows = 80;

        public sealed class LootItemRow
        {
            public Thing Thing;
            public int MaxCount;
            public int SelectedCount;
        }

        public sealed class LootPrisonerRow
        {
            public Pawn Pawn;
            public bool Selected;
        }

        public static bool SettingEnabled =>
            WorldDominationMod.settings?.showCaravanClashLootDialog ?? WorldDominationSettings.DefShowCaravanClashLootDialog;

        public static bool TryBuildCandidates(Map map, out List<LootPrisonerRow> prisoners, out List<LootItemRow> items)
        {
            prisoners = new List<LootPrisonerRow>();
            items = new List<LootItemRow>();
            if (map?.mapPawns == null) return false;

            Faction player = Faction.OfPlayer;
            IReadOnlyList<Pawn> spawned = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < spawned.Count; i++)
            {
                Pawn p = spawned[i];
                if (!IsCapturableClashPrisoner(p, player)) continue;
                prisoners.Add(new LootPrisonerRow { Pawn = p, Selected = false });
            }

            var thingRows = new List<LootItemRow>();
            List<Thing> allThings = map.listerThings?.AllThings;
            if (allThings != null)
            {
                for (int i = 0; i < allThings.Count; i++)
                {
                    Thing t = allThings[i];
                    if (!IsLooseLootThing(t)) continue;
                    thingRows.Add(new LootItemRow
                    {
                        Thing = t,
                        MaxCount = t.stackCount,
                        SelectedCount = 0
                    });
                }
            }

            thingRows.Sort((a, b) =>
            {
                float va = a.Thing?.MarketValue * a.MaxCount ?? 0f;
                float vb = b.Thing?.MarketValue * b.MaxCount ?? 0f;
                return vb.CompareTo(va);
            });
            if (thingRows.Count > MaxItemRows)
                thingRows.RemoveRange(MaxItemRows, thingRows.Count - MaxItemRows);
            items = thingRows;

            return prisoners.Count > 0 || items.Count > 0;
        }

        public static bool IsCapturableClashPrisoner(Pawn pawn, Faction playerFaction)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead) return false;
            if (pawn.RaceProps?.Humanlike != true) return false;
            if (OutpostPawnClassificationUtil.IsMechanoidWorker(pawn)) return false;
            if (VehicleFrameworkOutpostDissolveCompat.IsVehicleFrameworkVehiclePawn(pawn)) return false;
            if (pawn.Faction != null && pawn.Faction.IsPlayer) return false;
            if (pawn.IsPrisonerOfColony) return true;
            if (!pawn.Downed) return false;
            if (playerFaction == null) return false;
            // Unlike outpost harvest, do not require guest.Recruitable. Unwavering pawns are still
            // valid caravan prisoners (they just cannot be recruited later).
            return pawn.HostileTo(playerFaction)
                || (pawn.Faction != null && !pawn.Faction.IsPlayer);
        }

        public static bool IsLooseLootThing(Thing t)
        {
            if (t == null || t.Destroyed) return false;
            if (t is Pawn || t is Corpse) return false;
            if (t.def == null || t.def.category != ThingCategory.Item) return false;
            if (!t.def.EverHaulable) return false;
            if (t.Faction != null && t.Faction.IsPlayer) return false;
            if (!t.Spawned) return false;
            return true;
        }

        public static void GetMassTotals(
            Map map,
            List<LootPrisonerRow> prisoners,
            List<LootItemRow> items,
            out float capacity,
            out float usage)
        {
            capacity = 0f;
            usage = 0f;
            if (map?.mapPawns == null) return;

            IReadOnlyList<Pawn> spawned = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < spawned.Count; i++)
            {
                Pawn p = spawned[i];
                if (p == null || p.Destroyed || p.Dead) continue;
                if (p.Faction == null || !p.Faction.IsPlayer) continue;
                if (VehicleFrameworkOutpostDissolveCompat.IsVehicleFrameworkVehiclePawn(p)) continue;
                capacity += MassUtility.Capacity(p);
                usage += MassUtility.GearAndInventoryMass(p);
            }

            if (prisoners != null)
            {
                for (int i = 0; i < prisoners.Count; i++)
                {
                    LootPrisonerRow row = prisoners[i];
                    if (row == null || !row.Selected) continue;
                    Pawn p = row.Pawn;
                    if (p == null || p.Destroyed || p.Dead) continue;
                    usage += p.GetStatValue(StatDefOf.Mass);
                    usage += MassUtility.GearAndInventoryMass(p);
                }
            }

            if (items != null)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    LootItemRow row = items[i];
                    if (row == null || row.SelectedCount <= 0 || row.Thing == null || row.Thing.Destroyed) continue;
                    float unit = row.Thing.GetStatValue(StatDefOf.Mass);
                    if (unit <= 0f) unit = row.Thing.def?.BaseMass ?? 0f;
                    usage += unit * row.SelectedCount;
                }
            }
        }

        public static float FreeMass(Map map, List<LootPrisonerRow> prisoners, List<LootItemRow> items)
        {
            GetMassTotals(map, prisoners, items, out float capacity, out float usage);
            return capacity - usage;
        }

        public static int MaxCountAffordable(Thing thing, int maxWanted, float freeMass)
        {
            if (thing == null || maxWanted <= 0 || freeMass <= 0.01f) return 0;
            float unit = thing.GetStatValue(StatDefOf.Mass);
            if (unit <= 0f) unit = thing.def?.BaseMass ?? 0f;
            if (unit <= 0.0001f) return maxWanted;
            return Mathf.Clamp(Mathf.FloorToInt(freeMass / unit + 0.001f), 0, maxWanted);
        }

        /// <summary>Capture selected prisoners, move selected items into inventories, discard leftovers, reform caravan, destroy map.</summary>
        public static void ApplyAndLeave(Map map, WD_MapComponent_CaravanClash tracker, List<LootPrisonerRow> prisoners, List<LootItemRow> items)
        {
            if (map == null) return;

            try
            {
                CaptureSelectedPrisoners(prisoners);
                TransferSelectedItems(map, items);
            }
            catch (Exception ex)
            {
                Log.Warning($"[TSA WD] Clash loot apply failed: {ex.Message}");
            }

            tracker?.DiscardEncounterLeftoversPublic();

            try
            {
                ReformPlayerCaravanAndDestroyMap(map);
            }
            catch (Exception ex)
            {
                Log.Warning($"[TSA WD] Clash loot caravan reform failed: {ex.Message}");
                TryDestroyEncounterMap(map);
            }
        }

        private static void CaptureSelectedPrisoners(List<LootPrisonerRow> prisoners)
        {
            if (prisoners == null) return;
            Faction player = Faction.OfPlayer;
            for (int i = 0; i < prisoners.Count; i++)
            {
                LootPrisonerRow row = prisoners[i];
                if (row == null || !row.Selected) continue;
                Pawn p = row.Pawn;
                if (p == null || p.Destroyed || p.Dead) continue;
                if (p.guest != null)
                    p.guest.SetGuestStatus(player, GuestStatus.Prisoner);
                else if (p.Faction != player)
                    p.SetFaction(player);
            }
        }

        private static void TransferSelectedItems(Map map, List<LootItemRow> items)
        {
            if (items == null || map?.mapPawns == null) return;

            List<Pawn> carriers = new List<Pawn>();
            IReadOnlyList<Pawn> spawned = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < spawned.Count; i++)
            {
                Pawn p = spawned[i];
                if (p == null || p.Destroyed || p.Dead) continue;
                if (p.Faction == null || !p.Faction.IsPlayer) continue;
                if (p.Downed) continue;
                if (!MassUtility.CanEverCarryAnything(p)) continue;
                if (VehicleFrameworkOutpostDissolveCompat.IsVehicleFrameworkVehiclePawn(p)) continue;
                carriers.Add(p);
            }

            if (carriers.Count == 0) return;

            for (int i = 0; i < items.Count; i++)
            {
                LootItemRow row = items[i];
                if (row == null || row.SelectedCount <= 0) continue;
                Thing source = row.Thing;
                if (source == null || source.Destroyed || !source.Spawned) continue;

                int remaining = Mathf.Min(row.SelectedCount, source.stackCount);
                while (remaining > 0 && !source.Destroyed)
                {
                    Pawn carrier = FindBestCarrier(carriers, source);
                    if (carrier == null) break;

                    int canTake = MassUtility.CountToPickUpUntilOverEncumbered(carrier, source);
                    if (canTake <= 0)
                    {
                        carriers.Remove(carrier);
                        if (carriers.Count == 0) break;
                        continue;
                    }

                    int take = Mathf.Min(remaining, canTake, source.stackCount);
                    Thing moved = source.SplitOff(take);
                    if (moved == null) break;
                    if (!carrier.inventory.innerContainer.TryAdd(moved, true))
                    {
                        GenPlace.TryPlaceThing(moved, carrier.Position, map, ThingPlaceMode.Near);
                        break;
                    }
                    remaining -= take;
                }
            }
        }

        private static Pawn FindBestCarrier(List<Pawn> carriers, Thing thing)
        {
            Pawn best = null;
            float bestFree = -1f;
            for (int i = 0; i < carriers.Count; i++)
            {
                Pawn p = carriers[i];
                if (p == null || p.Destroyed || p.Dead || p.inventory == null) continue;
                float free = MassUtility.FreeSpace(p);
                if (free <= 0.01f) continue;
                if (MassUtility.CountToPickUpUntilOverEncumbered(p, thing) <= 0) continue;
                if (free > bestFree)
                {
                    bestFree = free;
                    best = p;
                }
            }
            return best;
        }

        private static void ReformPlayerCaravanAndDestroyMap(Map map)
        {
            if (map == null || !Current.Game.Maps.Contains(map)) return;

            List<Pawn> leave = new List<Pawn>();
            IReadOnlyList<Pawn> spawned = map.mapPawns?.AllPawnsSpawned;
            if (spawned != null)
            {
                for (int i = 0; i < spawned.Count; i++)
                {
                    Pawn p = spawned[i];
                    if (p == null || p.Destroyed || p.Dead) continue;
                    if (VehicleFrameworkOutpostDissolveCompat.IsVehicleFrameworkVehiclePawn(p))
                    {
                        // Player vehicles leave with the group when owned by the player.
                        if (p.Faction != null && p.Faction.IsPlayer)
                            leave.Add(p);
                        continue;
                    }
                    if (p.Faction != null && p.Faction.IsPlayer)
                        leave.Add(p);
                    else if (p.IsPrisonerOfColony)
                        leave.Add(p);
                }
            }

            MapParent parent = map.Parent;
            PlanetTile tile = map.Tile;

            Caravan caravan = null;
            if (leave.Count > 0)
            {
                caravan = CaravanExitMapUtility.ExitMapAndCreateCaravan(
                    leave,
                    Faction.OfPlayer,
                    tile,
                    tile,
                    PlanetTile.Invalid,
                    sendMessage: true);
            }

            TryDestroyEncounterMap(map, parent);

            if (caravan != null && !caravan.Destroyed)
                CameraJumper.TryJumpAndSelect(caravan);
        }

        private static void TryDestroyEncounterMap(Map map, MapParent parent = null)
        {
            parent ??= map?.Parent;
            if (parent == null || parent.Destroyed) return;
            // Never tear down player homes / gravship landing maps — only Ambush clash sites.
            if (parent.def != WorldObjectDefOf.Ambush) return;
            try
            {
                if (parent.HasMap && Current.Game.Maps.Contains(parent.Map))
                    Current.Game.DeinitAndRemoveMap(parent.Map, false);
                if (!parent.Destroyed)
                    parent.Destroy();
            }
            catch (Exception ex)
            {
                Log.Warning($"[TSA WD] Clash encounter map destroy failed: {ex.Message}");
            }
        }
    }
}
