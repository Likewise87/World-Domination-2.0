using System;
using System.Collections.Generic;
using System.Diagnostics;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>SSoT for outpost type change Apply (Path A in-place / Path B soft replace).</summary>
    public static class OutpostTypeChangeUtility
    {
        public static bool NeedsPathB(WorldObjectDef oldDef, WorldObjectDef newDef)
        {
            if (oldDef == null || newDef == null) return true;
            if (oldDef.worldObjectClass != newDef.worldObjectClass) return true;
            if (Outpost_Production_Utils.IsWarehouseOutpost(oldDef) != Outpost_Production_Utils.IsWarehouseOutpost(newDef))
                return true;
            return false;
        }

        public static bool HasNonTransferableStock(WorldObject_WD_Outpost outpost, WorldObjectDef newDef)
        {
            if (outpost == null || newDef == null) return false;
            bool oldWh = Outpost_Production_Utils.IsWarehouseOutpost(outpost.def);
            bool newWh = Outpost_Production_Utils.IsWarehouseOutpost(newDef);
            if (oldWh && !newWh)
            {
                var wh = CompOutpostWarehouse.Get(outpost);
                if (wh?.storedItems != null)
                {
                    for (int i = 0; i < wh.storedItems.Count; i++)
                    {
                        var e = wh.storedItems[i];
                        if (e?.thingDef != null && e.count > 0) return true;
                    }
                }
            }
            return false;
        }

        public static bool CanLaunchTypeChange(WorldObject_WD_Outpost outpost, WorldObjectDef newDef, out string reason)
        {
            reason = null;
            if (outpost == null || outpost.Destroyed || newDef == null)
            {
                reason = "TSA_WD_TypeChange_Invalid".Translate();
                return false;
            }
            if (outpost.def == newDef || outpost.def?.defName == newDef.defName)
            {
                reason = "TSA_WD_TypeChange_SameType".Translate();
                return false;
            }
            if (outpost.ManualDefenseActive || WD_MapComponent_OutpostDefense.HasActiveEncounterFor(outpost))
            {
                reason = "TSA_WD_OutpostDefense_FrozenDuringManualDefense".Translate();
                return false;
            }
            if (!string.IsNullOrEmpty(outpost.PendingTypeChangeDefName))
            {
                reason = "TSA_WD_TypeChange_AlreadyPending".Translate();
                return false;
            }
            if (!string.IsNullOrEmpty(outpost.PendingUpgradeDefName))
            {
                reason = "TSA_WD_OutpostUpgrades_InTransit".Translate();
                return false;
            }
            if (!Outpost_EstablishmentRequirements.TileSatisfiesOutpostType(outpost.Tile.tileId, newDef, out reason))
                return false;
            if (Outpost_EstablishmentRequirements.EnforceBiome
                && !Outpost_EstablishmentRequirements.BiomeAllowedForOutpost(outpost.Tile.tileId, newDef, out reason))
                return false;
            if (!MeetsSkillsAndPawnsForTarget(outpost, newDef, out reason))
                return false;

            var cost = Outpost_EstablishmentRequirements.GetCost(newDef);
            Map colonyMap = Find.AnyPlayerHomeMap;
            var warehouses = ColonyWorldBuildMaterials.GetContributingWarehouses();
            if (!ColonyWarehouseStockUtility.HasCosts(colonyMap, warehouses, cost, null, out reason))
                return false;

            return true;
        }

        public static bool MeetsSkillsAndPawnsForTarget(WorldObject_WD_Outpost outpost, WorldObjectDef newDef, out string reason)
        {
            reason = null;
            if (outpost == null || newDef == null) return false;
            if (Outpost_EstablishmentRequirements.EnforceMinPawns)
            {
                int need = Outpost_EstablishmentRequirements.GetMinPawnsToFound(newDef);
                int have = outpost.PawnCount;
                if (have < need)
                {
                    reason = "TSA_WD_Establish_MinPawns".Translate(newDef.label ?? "Outpost", need, have);
                    return false;
                }
            }
            if (Outpost_EstablishmentRequirements.EnforceMinSkill)
            {
                var ext = newDef.GetModExtension<OutpostDefExtension>();
                if (ext?.MinCumulativeSkill != null)
                {
                    foreach (var set in ext.MinCumulativeSkill)
                    {
                        if (set == null || !set.HasAnyRequirement()) continue;
                        foreach (var kv in set.GetRequirements())
                        {
                            if (kv.Key == null || kv.Value <= 0) continue;
                            int have = Outpost_EstablishmentRequirements.GetCumulativeOutpostSkillForSkill(outpost, kv.Key);
                            if (have < kv.Value)
                            {
                                reason = "TSA_WD_Establish_MinSkill".Translate(kv.Value, kv.Key.LabelCap, have);
                                return false;
                            }
                        }
                    }
                }
            }
            return true;
        }

        public static bool TryLaunch(WorldObject_WD_Outpost outpost, WorldObjectDef newDef, out string reason)
        {
            reason = null;
            if (!CanLaunchTypeChange(outpost, newDef, out reason)) return false;

            var cost = Outpost_EstablishmentRequirements.GetCost(newDef);
            Map colonyMap = Find.AnyPlayerHomeMap;
            var warehouses = ColonyWorldBuildMaterials.GetContributingWarehouses();
            if (!DeductEstablishCost(colonyMap, warehouses, cost, out reason)) return false;

            if (!outpost.TryQueuePendingTypeChange(newDef.defName))
            {
                reason = "TSA_WD_TypeChange_QueueFailed".Translate();
                return false;
            }

            WorldObject origin = ResolveOrigin(outpost, colonyMap, warehouses);
            if (origin == null || !WorldActions_Traveler.SpawnOutpostTypeChangeTraveler(outpost, origin, newDef.defName))
            {
                outpost.ClearPendingTypeChange();
                reason = "TSA_WD_TypeChange_LaunchFailed".Translate();
                return false;
            }
            return true;
        }

        private static bool DeductEstablishCost(
            Map map,
            List<WorldObject_WD_Outpost> warehouses,
            List<ThingDefCountClass> cost,
            out string reason)
        {
            reason = null;
            if (cost == null || cost.Count == 0) return true;
            for (int i = 0; i < cost.Count; i++)
            {
                var c = cost[i];
                if (c?.thingDef == null || c.count <= 0) continue;
                if (!ColonyWarehouseStockUtility.TryConsume(map, warehouses, c.thingDef, c.count, out reason))
                    return false;
            }
            return true;
        }

        private static WorldObject ResolveOrigin(
            WorldObject_WD_Outpost outpost,
            Map colonyMap,
            List<WorldObject_WD_Outpost> warehouses)
        {
            if (outpost == null || Find.WorldGrid == null) return colonyMap?.Parent;
            WorldObject nearestWh = null;
            float best = float.MaxValue;
            if (warehouses != null)
            {
                for (int i = 0; i < warehouses.Count; i++)
                {
                    var wh = warehouses[i];
                    if (wh == null || wh.Tile == outpost.Tile) continue;
                    float d = Find.WorldGrid.ApproxDistanceInTiles(outpost.Tile, wh.Tile);
                    if (d < best) { best = d; nearestWh = wh; }
                }
            }
            WorldObject colony = colonyMap?.Parent;
            if (nearestWh == null) return colony;
            if (colony == null) return nearestWh;
            float dCol = Find.WorldGrid.ApproxDistanceInTiles(outpost.Tile, colony.Tile);
            return best <= dCol ? nearestWh : colony;
        }

        /// <summary>Apply pending or explicit type change. Synchronous. Returns false on preflight failure.</summary>
        public static bool Apply(WorldObject_WD_Outpost outpost, WorldObjectDef newDef, WorldObject_Traveler arrivingTraveler, out string reason)
        {
            reason = null;
            if (outpost == null || outpost.Destroyed || newDef == null)
            {
                reason = "TSA_WD_TypeChange_Invalid".Translate();
                return false;
            }

            var sw = Stopwatch.StartNew();
            WorldObjectDef oldDef = outpost.def;
            bool pathB = NeedsPathB(oldDef, newDef);
            int occBefore = outpost.Occupants?.Count ?? 0;
            int prisBefore = outpost.Prisoners?.Count ?? 0;

            if (!Preflight(outpost, out reason))
                return false;

            Log.Message($"[WD TypeChange] start path={(pathB ? "B" : "A")} {oldDef?.defName}→{newDef.defName} id={outpost.ID} occ={occBefore} pris={prisBefore}");

            try
            {
                bool ok = pathB
                    ? ApplyPathB(outpost, oldDef, newDef, arrivingTraveler, occBefore, prisBefore, out reason)
                    : ApplyPathA(outpost, oldDef, newDef, out reason);
                sw.Stop();
                if (ok)
                    Log.Message($"[WD TypeChange] ok path={(pathB ? "B" : "A")} {oldDef?.defName}→{newDef.defName} ms={sw.ElapsedMilliseconds}");
                else
                    Log.Warning($"[WD TypeChange] fail path={(pathB ? "B" : "A")} reason={reason}");
                return ok;
            }
            catch (Exception e)
            {
                Log.Error($"[WD TypeChange] exception: {e}");
                reason = e.Message;
                return false;
            }
        }

        /// <summary>Debug / free apply without traveler or cost.</summary>
        public static bool DebugForceApply(WorldObject_WD_Outpost outpost, WorldObjectDef newDef, out string reason)
        {
            reason = null;
            if (outpost == null || newDef == null)
            {
                reason = "TSA_WD_TypeChange_Invalid".Translate();
                return false;
            }
            if (outpost.ManualDefenseActive || WD_MapComponent_OutpostDefense.HasActiveEncounterFor(outpost))
            {
                reason = "TSA_WD_OutpostDefense_FrozenDuringManualDefense".Translate();
                return false;
            }
            return Apply(outpost, newDef, null, out reason);
        }

        private static bool Preflight(WorldObject_WD_Outpost outpost, out string reason)
        {
            reason = null;
            if (outpost.ManualDefenseActive || WD_MapComponent_OutpostDefense.HasActiveEncounterFor(outpost))
            {
                reason = "TSA_WD_OutpostDefense_FrozenDuringManualDefense".Translate();
                return false;
            }
            if (CountPlayerOutpostsOnTile(outpost.Tile) > 1)
            {
                reason = "TSA_WD_TypeChange_TileConflict".Translate();
                return false;
            }
            return true;
        }

        private static int CountPlayerOutpostsOnTile(PlanetTile tile)
        {
            int n = 0;
            var all = Find.WorldObjects?.AllWorldObjects;
            if (all == null) return 0;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is WorldObject_WD_Outpost op && !op.Destroyed && op.Faction == Faction.OfPlayer && op.Tile == tile)
                    n++;
            }
            return n;
        }

        private static bool ApplyPathA(WorldObject_WD_Outpost outpost, WorldObjectDef oldDef, WorldObjectDef newDef, out string reason)
        {
            reason = null;
            outpost.TypeChangeAssignDef(newDef);
            outpost.TypeChangePruneUpgrades(newDef, out _, out _);
            outpost.TypeChangeClearProductionAndTypeOnlyFields(oldDef, newDef);
            DestroyNonTransferableStock(outpost, oldDef, newDef);
            if (Outpost_Production_Utils.IsRapidResponseOutpost(newDef) && !Outpost_Production_Utils.IsRapidResponseOutpost(oldDef))
                WD_Outpost_RapidResponse.ApplyEstablishmentDefaults(outpost);
            outpost.ClearPendingTypeChange();
            Recompute(outpost);
            AssertPostconditions(outpost, null, outpost.Occupants?.Count ?? 0, outpost.Prisoners?.Count ?? 0, pathB: false);
            SendCompletedLetter(outpost, newDef);
            return true;
        }

        private static bool ApplyPathB(
            WorldObject_WD_Outpost oldOutpost,
            WorldObjectDef oldDef,
            WorldObjectDef newDef,
            WorldObject_Traveler allowTraveler,
            int occBefore,
            int prisBefore,
            out string reason)
        {
            reason = null;
            var newOutpost = (WorldObject_WD_Outpost)WorldObjectMaker.MakeWorldObject(newDef);
            newOutpost.Tile = oldOutpost.Tile;
            newOutpost.SetFaction(oldOutpost.Faction);
            newOutpost.Name = oldOutpost.Name;
            newOutpost.suppressBootstrapHooks = true;

            Find.WorldObjects.Add(newOutpost);
            if (newOutpost.Destroyed || newOutpost.ID < 0)
            {
                reason = "TSA_WD_TypeChange_SpawnFailed".Translate();
                if (!newOutpost.Destroyed) newOutpost.Destroy();
                return false;
            }

            try
            {
                MovePawnLists(oldOutpost, newOutpost);
                MoveStock(oldOutpost, newOutpost, oldDef, newDef);
                MoveLogistics(oldOutpost, newOutpost);
                MoveExpertsAndPrefs(oldOutpost, newOutpost);
                var upgrades = oldOutpost.TypeChangeCloneBuiltUpgrades();
                newOutpost.TypeChangeReplaceBuiltUpgrades(upgrades);
                newOutpost.TypeChangePruneUpgrades(newDef, out _, out _);
                newOutpost.TypeChangeClearProductionAndTypeOnlyFields(oldDef, newDef);
                MoveSkirmish(oldOutpost, newOutpost);

                var oldComp = oldOutpost.GetComponent<CompViralSpread>();
                var newComp = newOutpost.GetComponent<CompViralSpread>();
                oldComp?.CopyWorldBuildStateTo(newComp);
                oldComp?.ZeroWorldBuildProjectsWithoutClear();

                OutpostReplaceRetargetUtility.RemapId(oldOutpost.ID, newOutpost.ID);
                int atRetarget = OutpostReplaceRetargetUtility.RetargetLiveRefs(oldOutpost, newOutpost);
                _ = atRetarget;

                newOutpost.suppressBootstrapHooks = false;
                newOutpost.RunTypeChangeBootstrapHooks();
                if (Outpost_Production_Utils.IsRapidResponseOutpost(newDef) && !Outpost_Production_Utils.IsRapidResponseOutpost(oldDef))
                    WD_Outpost_RapidResponse.ApplyEstablishmentDefaults(newOutpost);

                bool wasSelected = Find.WorldSelector?.IsSelected(oldOutpost) == true;
                oldOutpost.typeChangeShellRemoval = true;
                oldOutpost.ClearPendingTypeChange();
                oldOutpost.Destroy();

                newOutpost.ClearPendingTypeChange();
                if (wasSelected)
                {
                    Find.WorldSelector.ClearSelection();
                    Find.WorldSelector.Select(newOutpost);
                }

                Recompute(newOutpost);
                AssertPostconditions(newOutpost, oldOutpost, occBefore, prisBefore, pathB: true);
                SendCompletedLetter(newOutpost, newDef);
                return true;
            }
            catch
            {
                newOutpost.suppressBootstrapHooks = false;
                if (!newOutpost.Destroyed)
                {
                    newOutpost.typeChangeShellRemoval = true;
                    newOutpost.Destroy();
                }
                throw;
            }
        }

        private static void SendCompletedLetter(WorldObject_WD_Outpost survivor, WorldObjectDef newDef)
        {
            if (survivor == null || survivor.Destroyed || newDef == null || Find.LetterStack == null) return;
            TaggedString text = "TSA_WD_TypeChange_Completed".Translate(survivor.LabelCap, newDef.LabelCap);
            Find.LetterStack.ReceiveLetter(text, text, LetterDefOf.PositiveEvent, survivor);
        }

        private static void MovePawnLists(WorldObject_WD_Outpost from, WorldObject_WD_Outpost to)
        {
            TransferList(from.Occupants, to.Occupants);
            TransferList(from.Prisoners, to.Prisoners);
            TransferList(from.StoredAnimalsAndVehicles, to.StoredAnimalsAndVehicles);
            TransferList(from.StoredMechanoids, to.StoredMechanoids);
            if (from.StoredPassengerShuttles != null && from.StoredPassengerShuttles.Count > 0)
            {
                for (int i = 0; i < from.StoredPassengerShuttles.Count; i++)
                {
                    var s = from.StoredPassengerShuttles[i];
                    if (s != null && !s.Destroyed)
                        to.StoredPassengerShuttles.Add(s);
                }
                from.StoredPassengerShuttles.Clear();
            }
        }

        private static void TransferList(List<Pawn> from, List<Pawn> to)
        {
            if (from == null || to == null) return;
            for (int i = 0; i < from.Count; i++)
            {
                Pawn p = from[i];
                if (p != null && !p.Destroyed)
                    to.Add(p);
            }
            from.Clear();
        }

        private static void MoveStock(WorldObject_WD_Outpost from, WorldObject_WD_Outpost to, WorldObjectDef oldDef, WorldObjectDef newDef)
        {
            bool oldWh = Outpost_Production_Utils.IsWarehouseOutpost(oldDef);
            bool newWh = Outpost_Production_Utils.IsWarehouseOutpost(newDef);
            var fromArmory = CompOutpostArmory.Get(from);
            var toArmory = CompOutpostArmory.Get(to);
            var fromWh = CompOutpostWarehouse.Get(from);
            var toWh = CompOutpostWarehouse.Get(to);

            if (oldWh && newWh && fromWh != null && toWh != null)
            {
                if (fromWh.storedItems != null && fromWh.storedItems.Count > 0)
                {
                    toWh.TryDeposit(fromWh.storedItems);
                    fromWh.storedItems.Clear();
                }
            }
            else if (oldWh && !newWh && fromWh != null)
            {
                fromWh.storedItems?.Clear();
            }
            else if (!oldWh && newWh && fromArmory != null && toWh != null)
            {
                var stock = fromArmory.Stock;
                if (stock != null && stock.Count > 0)
                {
                    toWh.TryDeposit(stock);
                    stock.Clear();
                }
            }

            if (fromArmory?.Uniques != null && toArmory?.Uniques != null)
            {
                var move = new List<Thing>(fromArmory.Uniques);
                for (int i = 0; i < move.Count; i++)
                {
                    Thing t = move[i];
                    if (t == null || t.Destroyed) continue;
                    fromArmory.Uniques.Remove(t);
                    toArmory.Uniques.TryAdd(t, canMergeWithExistingStacks: false);
                }
            }
        }

        private static void DestroyNonTransferableStock(WorldObject_WD_Outpost outpost, WorldObjectDef oldDef, WorldObjectDef newDef)
        {
            if (!HasNonTransferableStock(outpost, newDef)) return;
            var wh = CompOutpostWarehouse.Get(outpost);
            wh?.storedItems?.Clear();
        }

        private static void MoveLogistics(WorldObject_WD_Outpost from, WorldObject_WD_Outpost to)
        {
            var a = from.GetComponent<CompOutpostLogistics>();
            var b = to.GetComponent<CompOutpostLogistics>();
            if (a == null || b == null) return;
            b.currentFood = a.currentFood;
            b.mode = a.mode;
            b.produceForLogistics = a.produceForLogistics;
            a.currentFood = 0f;
            float max = b.EffectiveMaxFood;
            if (max > 0f && b.currentFood > max)
                b.currentFood = max;
        }

        private static void MoveExpertsAndPrefs(WorldObject_WD_Outpost from, WorldObject_WD_Outpost to)
        {
            to.dropPodFallbackToLand = from.dropPodFallbackToLand;
            to.pawnTravelViaDropPod = from.pawnTravelViaDropPod;
            to.TakePrisoners = from.TakePrisoners;
            to.autoAssignExpertsOnSlotUnlock = from.autoAssignExpertsOnSlotUnlock;
            foreach (OutpostExpertRole role in System.Enum.GetValues(typeof(OutpostExpertRole)))
            {
                string id = from.GetExpertThingId(role);
                if (!string.IsNullOrEmpty(id))
                    to.SetExpertThingId(role, id);
            }
        }

        private static void MoveSkirmish(WorldObject_WD_Outpost from, WorldObject_WD_Outpost to)
        {
            // Pending skirmish fields are private; leave on old and clear via shell skip.
            // If skirmish was pending, shell removal skips ClearPendingSkirmish — migrate via public API if available.
            from.ClearPendingSkirmishDefense();
        }

        private static void Recompute(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null || outpost.Destroyed) return;
            outpost.GetComponent<CompViralSpread>()?.UpdateOutpostStrengthLogically();
            OutpostExpertUtility.ValidateAssignments(outpost);
            outpost.SyncExpertSlotUnlockBaseline();
            outpost.RecomputeProductionRequirementCache();
            outpost.StartProductionTimerIfNeeded();
            outpost.RefreshInterceptorRegistration();
            var logistics = Find.World?.GetComponent<WorldComponent_LogisticsManager>();
            logistics?.NotifyLogisticsTopologyChanged();
            logistics?.NotifyFoodLogisticsInputsChanged();
            OutpostWarehouseAuraUtility.InvalidateCache();
            if (outpost.IsPowerPlantOutpost)
                Outpost_PowerPlant.NotifyRemotePowerDirty();
            ReinforcementNeighborCache.BumpGeneration();
            WdPlayerOutpostCache.Invalidate();
            outpost.InvalidateInspectCache();
            Window_OutpostOverview.InvalidateCache();
        }

        private static void AssertPostconditions(
            WorldObject_WD_Outpost survivor,
            WorldObject_WD_Outpost oldDestroyed,
            int occExpected,
            int prisExpected,
            bool pathB)
        {
            void Report(string msg)
            {
                if (Prefs.DevMode) Log.Error("[WD TypeChange] postcondition: " + msg);
                else Log.Warning("[WD TypeChange] postcondition: " + msg);
            }

            if (survivor == null || survivor.Destroyed)
            {
                Report("survivor missing");
                return;
            }
            if (survivor.suppressBootstrapHooks || survivor.typeChangeShellRemoval)
                Report("flags still set");
            if (!string.IsNullOrEmpty(survivor.PendingTypeChangeDefName))
                Report("pending type change still set");
            if ((survivor.Occupants?.Count ?? 0) != occExpected)
                Report($"occ {survivor.Occupants?.Count} != {occExpected}");
            if ((survivor.Prisoners?.Count ?? 0) != prisExpected)
                Report($"pris {survivor.Prisoners?.Count} != {prisExpected}");
            if (CountPlayerOutpostsOnTile(survivor.Tile) != 1)
                Report($"outposts on tile = {CountPlayerOutpostsOnTile(survivor.Tile)}");
            if (pathB && oldDestroyed != null && !oldDestroyed.Destroyed)
                Report("old outpost still alive");
            if (pathB && oldDestroyed != null)
            {
                var all = Find.WorldObjects?.AllWorldObjects;
                if (all != null)
                {
                    for (int i = 0; i < all.Count; i++)
                    {
                        if (all[i] is WorldObject_AT_Turret t
                            && !t.Destroyed
                            && ReferenceEquals(t.builtBySite, oldDestroyed))
                            Report("AT builtBySite still old");
                    }
                }
            }
        }
    }
}
