using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Mid/Late experimental silver upkeep for outpost Occupants.
    /// Schedule / resolve from the daily budget; alerts read live Need/Have/days left.
    /// </summary>
    public static class EscalationOutpostUpkeep
    {
        public static bool IsFeatureEnabled
        {
            get
            {
                var seth = WorldDominationMod.settings;
                return seth != null
                    && seth.enableOutpostUpkeep
                    && seth.enableLateGameScaling;
            }
        }

        public static bool IsStageActive(WorldComponent_SpreadManager manager) =>
            WdEscalation.IsMidOrLate(manager);

        public static int SilverPerOccupant =>
            Mathf.Max(1, WorldDominationMod.settings?.upkeepSilverPerOccupant
                ?? WorldDominationSettings.DefUpkeepSilverPerOccupant);

        public static int IntervalDays =>
            Mathf.Max(1, WorldDominationMod.settings?.upkeepIntervalDays
                ?? WorldDominationSettings.DefUpkeepIntervalDays);

        /// <summary>Call from CalculateDailyBudget after escalation metrics are fresh.</summary>
        public static void TryDaily(WorldComponent_SpreadManager manager)
        {
            if (manager == null) return;

            if (!IsFeatureEnabled || !IsStageActive(manager))
            {
                CancelDeadline(manager);
                return;
            }

            int now = Find.TickManager?.TicksGame ?? 0;
            EnsureDeadlineScheduled(manager, now);

            if (manager.outpostUpkeepNextTick >= 0 && now >= manager.outpostUpkeepNextTick)
                ResolveDeadline(manager);
        }

        public static void CancelDeadline(WorldComponent_SpreadManager manager)
        {
            if (manager == null) return;
            manager.outpostUpkeepNextTick = -1;
        }

        public static void ScheduleNext(WorldComponent_SpreadManager manager, int fromTick)
        {
            if (manager == null) return;
            manager.outpostUpkeepNextTick = fromTick + IntervalDays * GenDate.TicksPerDay;
        }

        /// <summary>
        /// Starts the upkeep clock as soon as Mid/Late + feature are active.
        /// Without this, alerts stay hidden until the next daily budget tick after enabling.
        /// </summary>
        public static void EnsureDeadlineScheduled(WorldComponent_SpreadManager manager, int fromTick = -1)
        {
            if (manager == null) return;
            if (manager.outpostUpkeepNextTick >= 0) return;
            if (fromTick < 0)
                fromTick = Find.TickManager?.TicksGame ?? 0;
            ScheduleNext(manager, fromTick);
        }

        public static float DaysRemaining(WorldComponent_SpreadManager manager)
        {
            if (manager == null || manager.outpostUpkeepNextTick < 0) return -1f;
            int now = Find.TickManager?.TicksGame ?? 0;
            return Mathf.Max(0f, (manager.outpostUpkeepNextTick - now) / (float)GenDate.TicksPerDay);
        }

        public static int DaysRemainingCeil(WorldComponent_SpreadManager manager) =>
            Mathf.CeilToInt(DaysRemaining(manager));

        /// <summary>
        /// Live snapshot for alerts. Returns false when the alert should not show.
        /// Shows for the full upkeep period (not only the last N days) while a deadline is scheduled
        /// and there is at least one billable outpost occupant.
        /// </summary>
        public static bool TryGetAlertState(
            WorldComponent_SpreadManager manager,
            out int daysLeft,
            out int need,
            out int have,
            out int occupantCount,
            out int projectedLeavers)
        {
            daysLeft = 0;
            need = have = occupantCount = projectedLeavers = 0;
            if (!IsFeatureEnabled || !IsStageActive(manager)) return false;
            if (WorldDominationMod.settings != null && !WorldDominationMod.settings.notifyOutpostUpkeep)
                return false;
            if (manager == null) return false;

            EnsureDeadlineScheduled(manager);

            if (manager.outpostUpkeepNextTick < 0) return false;

            daysLeft = DaysRemainingCeil(manager);

            CollectBill(out occupantCount, out need);
            if (need <= 0 || occupantCount <= 0) return false;

            have = CountPlayerSilver();
            projectedLeavers = ProjectedLeavers(need, have);
            return true;
        }

        public static int ProjectedLeavers(int need, int have)
        {
            int unpaid = Mathf.Max(0, need - Mathf.Max(0, have));
            int per = SilverPerOccupant;
            return per > 0 ? unpaid / per : 0;
        }

        public static void CollectBill(out int occupantCount, out int needSilver)
        {
            occupantCount = 0;
            needSilver = 0;
            int per = SilverPerOccupant;
            foreach (WorldObject_WD_Outpost o in EnumeratePlayerSurfaceOutposts())
            {
                List<Pawn> occ = o.Occupants;
                if (occ == null) continue;
                for (int i = 0; i < occ.Count; i++)
                {
                    Pawn p = occ[i];
                    if (p == null || p.Destroyed || p.Dead) continue;
                    occupantCount++;
                    needSilver += per;
                }
            }
        }

        public static int CountPlayerSilver()
        {
            int total = 0;
            ThingDef silver = ThingDefOf.Silver;
            if (silver == null) return 0;

            List<Map> maps = Find.Maps;
            if (maps != null)
            {
                for (int m = 0; m < maps.Count; m++)
                {
                    Map map = maps[m];
                    if (map == null || !map.IsPlayerHome) continue;
                    var list = map.listerThings.ThingsOfDef(silver);
                    for (int i = 0; i < list.Count; i++)
                    {
                        Thing t = list[i];
                        if (t == null || !t.Spawned || t.Destroyed) continue;
                        total += t.stackCount > 0 ? t.stackCount : 1;
                    }
                }
            }

            var warehouses = SettlementBuyUtility.GetContributingWarehouses(-1);
            var match = new ThingDefCountClass(silver, 1);
            for (int w = 0; w < warehouses.Count; w++)
            {
                var comp = CompOutpostWarehouse.Get(warehouses[w]);
                if (comp == null) continue;
                total += comp.GetStoredCountMatching(match);
            }

            return total;
        }

        private static void ResolveDeadline(WorldComponent_SpreadManager manager)
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            CollectBill(out int occupants, out int need);
            int haveBefore = CountPlayerSilver();
            int take = Mathf.Min(haveBefore, need);
            int paid = 0;
            if (take > 0)
                paid = DeductSilverUpTo(take);

            int unpaid = Mathf.Max(0, need - paid);
            int leavers = ProjectedLeavers(need, paid);
            var leftNames = new List<string>();
            if (leavers > 0)
                ApplyLeavers(leavers, leftNames);

            ScheduleNext(manager, now);

            if (leftNames.Count > 0)
            {
                string names = string.Join(", ", leftNames);
                Find.LetterStack.ReceiveLetter(
                    "TSA_WD_Upkeep_LeaveLetterLabel".Translate(leftNames.Count),
                    "TSA_WD_Upkeep_LeaveLetterText".Translate(names),
                    LetterDefOf.NegativeEvent);
            }
            else if (need > 0 && paid >= need)
            {
                Messages.Message(
                    "TSA_WD_Upkeep_PaidMsg".Translate(paid, occupants),
                    MessageTypeDefOf.NeutralEvent,
                    false);
            }
            else if (need > 0 && unpaid > 0 && leavers <= 0)
            {
                // Paid partial but remainder &lt; one colonist rate.
                Messages.Message(
                    "TSA_WD_Upkeep_PartialPaidMsg".Translate(paid, need),
                    MessageTypeDefOf.NeutralEvent,
                    false);
            }
        }

        private static int DeductSilverUpTo(int amount)
        {
            if (amount <= 0 || ThingDefOf.Silver == null) return 0;
            int remaining = amount;
            ThingDef silver = ThingDefOf.Silver;

            List<Map> maps = Find.Maps;
            if (maps != null)
            {
                for (int m = 0; m < maps.Count && remaining > 0; m++)
                {
                    Map map = maps[m];
                    if (map == null || !map.IsPlayerHome) continue;
                    remaining -= DeductSilverFromMap(map, silver, remaining);
                }
            }

            if (remaining > 0)
            {
                var match = new ThingDefCountClass(silver, remaining);
                var warehouses = SettlementBuyUtility.GetContributingWarehouses(-1);
                for (int w = 0; w < warehouses.Count && remaining > 0; w++)
                {
                    var comp = CompOutpostWarehouse.Get(warehouses[w]);
                    if (comp == null) continue;
                    int took = comp.WithdrawUpToMatching(match, remaining);
                    remaining -= took;
                    match.count = remaining;
                }
            }

            return amount - remaining;
        }

        private static int DeductSilverFromMap(Map map, ThingDef silver, int amount)
        {
            if (map == null || silver == null || amount <= 0) return 0;
            int toRemove = amount;
            var list = map.listerThings.ThingsOfDef(silver);
            // Copy refs because Destroy mutates the lister
            var pool = new List<Thing>(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                Thing t = list[i];
                if (t != null && t.Spawned && !t.Destroyed)
                    pool.Add(t);
            }

            for (int i = 0; i < pool.Count && toRemove > 0; i++)
            {
                Thing t = pool[i];
                if (t == null || t.Destroyed) continue;
                int stack = t.stackCount > 0 ? t.stackCount : 1;
                int take = Mathf.Min(toRemove, stack);
                if (take >= stack)
                    t.Destroy(DestroyMode.Vanish);
                else
                    t.SplitOff(take).Destroy(DestroyMode.Vanish);
                toRemove -= take;
            }
            return amount - toRemove;
        }

        private static void ApplyLeavers(int count, List<string> leftNames)
        {
            if (count <= 0) return;
            var preferred = new List<(WorldObject_WD_Outpost outpost, Pawn pawn)>();
            var protectedList = new List<(WorldObject_WD_Outpost outpost, Pawn pawn)>();

            foreach (WorldObject_WD_Outpost o in EnumeratePlayerSurfaceOutposts())
            {
                List<Pawn> occ = o.Occupants;
                if (occ == null) continue;
                for (int i = 0; i < occ.Count; i++)
                {
                    Pawn p = occ[i];
                    if (p == null || p.Destroyed || p.Dead) continue;
                    if (IsProtectedOccupant(p))
                        protectedList.Add((o, p));
                    else
                        preferred.Add((o, p));
                }
            }

            preferred.Shuffle();
            protectedList.Shuffle();

            int need = count;
            need -= TakeLeavers(preferred, need, leftNames);
            if (need > 0)
                TakeLeavers(protectedList, need, leftNames);
        }

        private static int TakeLeavers(
            List<(WorldObject_WD_Outpost outpost, Pawn pawn)> pool,
            int count,
            List<string> leftNames)
        {
            int taken = 0;
            for (int i = 0; i < pool.Count && taken < count; i++)
            {
                var (outpost, pawn) = pool[i];
                if (outpost == null || outpost.Destroyed || pawn == null || pawn.Destroyed)
                    continue;
                if (!outpost.Occupants.Contains(pawn)) continue;

                string name = pawn.LabelShortCap ?? pawn.LabelCap ?? "?";
                Pawn removed = outpost.RemovePawn(pawn);
                if (removed == null) continue;

                leftNames.Add(name);
                if (!removed.Destroyed)
                    removed.Destroy(DestroyMode.Vanish);
                taken++;

                if (outpost.Occupants != null && outpost.Occupants.Count == 0 && !outpost.Destroyed)
                    outpost.Destroy();
            }
            return taken;
        }

        private static bool IsProtectedOccupant(Pawn pawn)
        {
            if (pawn == null) return true;
            if (pawn.IsQuestLodger()) return true;
            if (ModsConfig.RoyaltyActive && pawn.royalty != null && pawn.royalty.HasAnyTitleIn(Faction.OfPlayer))
                return true;
            return false;
        }

        private static IEnumerable<WorldObject_WD_Outpost> EnumeratePlayerSurfaceOutposts()
        {
            if (Find.WorldObjects == null) yield break;
            var all = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < all.Count; i++)
            {
                if (!(all[i] is WorldObject_WD_Outpost o) || o.Destroyed) continue;
                if (o.Faction == null || !o.Faction.IsPlayer) continue;
                if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(o)) continue;
                yield return o;
            }
        }
    }
}
