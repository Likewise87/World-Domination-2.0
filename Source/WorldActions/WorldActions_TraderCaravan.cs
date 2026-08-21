using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Holds the state for a trader caravan destination evaluation spread across multiple ticks.
    /// One pathfinding operation per tick eliminates freezes when many neutral/allied targets are in range.
    /// </summary>
    internal class PendingTraderEvaluation
    {
        internal struct CandidateEntry
        {
            public WorldObject target;
            /// <summary>Cached tile distance from sender at queue build (for near-first ordering).</summary>
            public float dist;
        }

        internal Settlement sender;
        internal CompViralSpread senderComp;
        internal WorldComponent_SpreadManager manager;
        internal WorldDominationSettings seth;

        internal readonly List<CandidateEntry> pending = new List<CandidateEntry>();
        internal int nextIdx;
        internal readonly List<WorldObject> viable = new List<WorldObject>();

        /// <summary>Done when list exhausted or one suitable destination found (stop-at-first, like raids).</summary>
        internal bool IsComplete => nextIdx >= pending.Count || viable.Count > 0;

        /// <summary>Evaluate one candidate (at most one pathfind). Returns true when we should finalize.</summary>
        internal bool EvaluateNext()
        {
            if (IsComplete) return true;
            if (sender == null || sender.Destroyed || !sender.Spawned) return true;

            WorldObject wo = pending[nextIdx++].target;
            if (wo == null || wo.Destroyed || !wo.Spawned)
                return IsComplete;

            if (!WorldActions_TraderCaravan.IsValidTraderDestination(wo))
                return IsComplete;

            int dist = WorldActions_Utils.GetDistance(sender.Tile, wo.Tile, manager);
            if (dist > seth.traderDestinationSearchRadius)
                return IsComplete;

            float eff = TravelUtils.ResolvePrepEfficiency(sender.Tile, wo.Tile, seth, sender.Faction, WorldObject_Traveler.DefaultTicksPerMove);
            if (eff > 0f)
                viable.Add(wo);

            return IsComplete;
        }
    }

    public static class WorldActions_TraderCaravan
    {
        /// <summary>
        /// Begins a staggered trader destination evaluation. Cheap pre-filtering is done immediately;
        /// pathfinding is deferred to subsequent ticks via <see cref="PendingTraderEvaluation"/>.
        /// Returns true to claim the action slot.
        /// </summary>
        public static bool AttemptTraderCaravan(Settlement sender, CompViralSpread senderComp, WorldComponent_SpreadManager manager)
        {
            var seth = WorldDominationMod.settings;
            if (sender == null || senderComp == null || seth == null || sender.Faction == null) return false;

            if (!IsValidTraderDestination(sender))
            {
                WDVerbose.Msg($"AttemptTraderCaravan: skip non-surface/space sender {sender.LabelCap}");
                return false;
            }

            if (senderComp.IsTraderOnCooldown)
            {
                manager?.AddLog(new SpreadLogEntry("TSA_WD_Log_Trader_SkippedCooldown".Translate(sender.LabelCap), sender));
                return false;
            }

            float cost = Mathf.Max(1f, seth.traderCaravanCostStrength);
            float retainFloor = WorldActions_Utils.GetGarrisonRetainFloor(senderComp, seth);
            if (senderComp.strength - cost < retainFloor)
            {
                manager?.AddLog(new SpreadLogEntry("TSA_WD_Log_Trader_SkippedGarrison".Translate(sender.LabelCap, cost.ToString("F0"), retainFloor.ToString("F0"), senderComp.strength.ToString("F0")), sender));
                return false;
            }

            if (manager.pendingTrader != null) return false;

            var eval = new PendingTraderEvaluation
            {
                sender = sender,
                senderComp = senderComp,
                manager = manager,
                seth = seth
            };

            foreach (Settlement s in Find.WorldObjects.Settlements)
            {
                if (s == null || s == sender || s.Destroyed || s.Faction == null) continue;
                if (s.Faction == sender.Faction) continue;
                if (!IsValidTraderDestination(s)) continue;
                if (!IsNeutralOrAllied(sender.Faction, s.Faction)) continue;
                if (!TryGetDistanceIfInRange(sender, s, manager, seth, out float dist)) continue;
                eval.pending.Add(new PendingTraderEvaluation.CandidateEntry { target = s, dist = dist });
            }

            // Player WD outposts: same goodwill/neutral-allied rules as player colonies; strength reward capped, no tier up.
            var worldObjects = Find.WorldObjects?.AllWorldObjects;
            if (worldObjects != null)
            {
                for (int i = 0; i < worldObjects.Count; i++)
                {
                    if (!(worldObjects[i] is WorldObject_WD_Outpost op)) continue;
                    if (op.Destroyed || op.Faction == null || !op.Faction.IsPlayer) continue;
                    if (!IsValidTraderDestination(op)) continue;
                    if (!IsNeutralOrAllied(sender.Faction, op.Faction)) continue;
                    if (!TryGetDistanceIfInRange(sender, op, manager, seth, out float dist)) continue;
                    eval.pending.Add(new PendingTraderEvaluation.CandidateEntry { target = op, dist = dist });
                }
            }

            if (eval.pending.Count == 0)
            {
                WDVerbose.Msg($"AttemptTraderCaravan: no neutral/allied candidates in range {sender.LabelCap}");
                manager?.AddLog(new SpreadLogEntry("TSA_WD_Log_Trader_SkippedNoTarget".Translate(sender.LabelCap), sender));
                return false;
            }

            OrderTraderCandidates(eval.pending, seth);
            WDVerbose.Msg($"AttemptTraderCaravan: staggered eval queued count={eval.pending.Count} sender={sender.LabelCap} travelPrepExactPct={seth.travelPrepExactPercent} (distance bands of search R={seth.traderDestinationSearchRadius:F0})");
            manager.pendingTrader = eval;
            return true;
        }

        private static void OrderTraderCandidates(List<PendingTraderEvaluation.CandidateEntry> list, WorldDominationSettings seth)
        {
            float maxRange = Mathf.Max(0.001f, seth?.traderDestinationSearchRadius ?? 1f);
            WD_TargetDistanceBandOrder.OrderWeightedPreferredThenCloserThenFarther(
                list,
                e => e.dist,
                maxRange,
                band => band.Shuffle());
        }

        /// <summary>Called by the orchestrator when staggered evaluation completes.</summary>
        internal static void FinalizeTrader(PendingTraderEvaluation eval)
        {
            var sender = eval.sender;
            var senderComp = eval.senderComp;
            var manager = eval.manager;
            var seth = eval.seth;

            if (sender == null || sender.Destroyed || !sender.Spawned || senderComp == null)
                return;

            if (eval.viable.Count == 0)
            {
                manager?.AddLog(new SpreadLogEntry("TSA_WD_Log_Trader_SkippedNoTarget".Translate(sender.LabelCap), sender));
                return;
            }

            for (int vi = eval.viable.Count - 1; vi >= 0; vi--)
            {
                if (!IsValidTraderDestination(eval.viable[vi]))
                    eval.viable.RemoveAt(vi);
            }

            if (eval.viable.Count == 0)
            {
                manager?.AddLog(new SpreadLogEntry("TSA_WD_Log_Trader_SkippedNoTarget".Translate(sender.LabelCap), sender));
                return;
            }

            WorldObject target = eval.viable[0];
            if (!IsValidTraderDestination(target))
                return;
            var def = DefDatabase<WorldObjectDef>.GetNamedSilentFail("TSA_WD_Traveler_Trader");
            if (def == null)
            {
                manager?.AddLog(new SpreadLogEntry("TSA_WD_Log_Trader_SkippedNoDef".Translate(sender.LabelCap, target.LabelCap), sender, target));
                return;
            }

            float cost = Mathf.Max(1f, seth.traderCaravanCostStrength);
            if (senderComp.strength - cost < WorldActions_Utils.GetGarrisonRetainFloor(senderComp, seth))
                return;

            senderComp.strength = Mathf.Max(0f, senderComp.strength - cost);
            senderComp.traderCooldownTick = Find.TickManager.TicksGame + CompViralSpread.CooldownTicksFromDays(seth.cooldownTraderDays);

            ApplyPlayerDestinationCooldownIfNeeded(target, seth);

            var traveler = (WorldObject_Traveler)WorldObjectMaker.MakeWorldObject(def);
            traveler.Tile = sender.Tile;
            traveler.SetFaction(sender.Faction);
            traveler.mission = TravelerMission.Trader;
            traveler.originObject = sender;
            traveler.targetObject = target;

            // Feature E: escort strength for interception/combat math only; the resource cost deducted above stays unchanged.
            float escortStrength = senderComp.IsCaravanEscortRecentlyIntercepted()
                ? senderComp.GetMaxOffensiveStrength()
                : senderComp.GetTraderEscortFloor();
            traveler.travelerStrength = Mathf.Max(cost, escortStrength);
            traveler.initialStrength = traveler.travelerStrength;

            Find.WorldObjects.Add(traveler);
            traveler.pather.StartPath(PlanetSurfaceWorldActions.PlanetTileForWdTravel(target.Tile, sender));

            manager?.AddLog(new SpreadLogEntry("TSA_WD_Log_TraderLaunched".Translate(sender.LabelCap, target.LabelCap), sender, target));
        }

        private static bool TryGetDistanceIfInRange(
            Settlement sender,
            WorldObject target,
            WorldComponent_SpreadManager manager,
            WorldDominationSettings seth,
            out float dist)
        {
            dist = WorldActions_Utils.GetDistance(sender.Tile, target.Tile, manager);
            if (dist > seth.traderDestinationSearchRadius) return false;
            if (seth.cooldownPlayerColonyTraderDays > 0f
                && target.Faction != null
                && target.Faction.IsPlayer)
            {
                var tComp = target.GetComponent<CompViralSpread>();
                if (tComp != null && tComp.IsPlayerColonyWdTraderTargetOnCooldown) return false;
            }
            return true;
        }

        private static void ApplyPlayerDestinationCooldownIfNeeded(WorldObject target, WorldDominationSettings seth)
        {
            if (seth.cooldownPlayerColonyTraderDays <= 0f) return;
            if (target?.Faction == null || !target.Faction.IsPlayer) return;
            var targetComp = target.GetComponent<CompViralSpread>();
            if (targetComp == null) return;
            targetComp.playerColonyWdTraderCooldownTick =
                Find.TickManager.TicksGame + CompViralSpread.CooldownTicksFromDays(seth.cooldownPlayerColonyTraderDays);
        }

        private static bool IsNeutralOrAllied(Faction a, Faction b)
        {
            FactionRelationKind kind = WorldActions_Utils.SafeRelationKindWith(a, b);
            return kind == FactionRelationKind.Neutral || kind == FactionRelationKind.Ally;
        }

        /// <summary>Planet-surface only; excludes orbit, off-surface layers, and space-like settlements (SOS2 etc.).</summary>
        internal static bool IsValidTraderDestination(Settlement s) =>
            s != null && s.Spawned && !s.Destroyed
            && !WorldActions_Utils.IsSpace(s)
            && PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(s);

        /// <summary>Settlements (any eligible faction) or player WD outposts on the planet surface.</summary>
        internal static bool IsValidTraderDestination(WorldObject wo)
        {
            if (wo is Settlement s)
                return IsValidTraderDestination(s);
            if (wo is WorldObject_WD_Outpost op)
            {
                return op.Spawned && !op.Destroyed
                    && op.Faction != null && op.Faction.IsPlayer
                    && !WorldActions_Utils.IsSpace(op)
                    && PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(op);
            }
            return false;
        }
    }
}
