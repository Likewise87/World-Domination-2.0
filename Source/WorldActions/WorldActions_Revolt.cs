using RimWorld.Planet;
using RimWorld;
using System.Collections.Generic;
using Verse;
using UnityEngine;

namespace TSA_WorldDomination
{
    public static class WorldActions_Revolt
    {
        private const float WeightT1T2 = 3f;
        private const float WeightT3 = 1f;
        private const float WeightT4 = 0.5f;

        public static void TryTriggerRevolt(WorldComponent_SpreadManager manager, DailyWorldSnapshot snapshot = null)
        {
            var seth = WorldDominationMod.settings;
            if (seth == null || manager == null) return;

            List<Faction> defeatedFactions = CollectDefeatedRebelCandidates();
            if (defeatedFactions.Count == 0) return;
            if (WorldActions_SpecialEventCooldown.IsOnCooldown(manager, SpecialWorldEventKind.Revolt)) return;
            if (Rand.Value > seth.revoltChance) return;

            Faction rebelFaction = defeatedFactions.RandomElement();
            ExecuteRevolt(rebelFaction, forcedSeed: null, stampSpecialEventCooldown: true);
        }

        /// <summary>
        /// Debug: force a real revolt (3-7 weighted) for a chosen rebel, seeded by a clicked settlement.
        /// Skips chance and special-event cooldown. Still stamps comeback CD and sends the revolt letter.
        /// </summary>
        public static bool DebugForceRevolt(Faction rebelFaction, Settlement seed, out string failReason)
        {
            failReason = null;
            if (rebelFaction == null)
            {
                failReason = "WD debug: no rebel faction.";
                return false;
            }
            if (seed == null || seed.Destroyed)
            {
                failReason = "WD debug: click an eligible NPC settlement.";
                return false;
            }
            if (seed.Faction == null || seed.Faction.IsPlayer)
            {
                failReason = "WD debug: revolt seed must be an NPC settlement.";
                return false;
            }
            if (seed.Faction == rebelFaction)
            {
                failReason = "WD debug: rebel faction cannot be the seed owner.";
                return false;
            }
            if (!IsEligibleRevoltVictim(seed))
            {
                failReason = "WD debug: seed settlement is not an eligible revolt victim (protected, no CompViralSpread, etc.).";
                return false;
            }

            if (!ExecuteRevolt(rebelFaction, seed, stampSpecialEventCooldown: false))
            {
                failReason =
                    "WD debug: revolt failed (need at least 3 eligible towns on the seed faction, then fill to 3-7).";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Shared revolt execution: revive rebel, pick 3-7 targets, transfer, letter, comeback CD.
        /// When <paramref name="forcedSeed"/> is set, that settlement is forced into the set and its
        /// faction is the primary pool (instead of the largest empire).
        /// </summary>
        public static bool ExecuteRevolt(
            Faction rebelFaction,
            Settlement forcedSeed = null,
            bool stampSpecialEventCooldown = true)
        {
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (manager == null || rebelFaction == null) return false;

            Dictionary<Faction, List<Settlement>> byFaction = BuildEligibleSettlementsByFaction();
            if (!TryBuildRevoltTargets(byFaction, forcedSeed, out List<Settlement> targets))
                return false;

            if (targets.Count < 3) return false;

            rebelFaction.defeated = false;
            WDVerbose.Msg($"Revolt revive loadID={rebelFaction.loadID} name={rebelFaction.Name}");

            var victimNames = new List<string>();
            var seenVictims = new HashSet<Faction>();
            var liberatedNames = new List<string>();
            int lastTile = -1;

            for (int i = 0; i < targets.Count; i++)
            {
                Settlement target = targets[i];
                if (target == null || target.Destroyed) continue;
                if (!IsEligibleRevoltVictim(target)) continue;

                Faction victimFaction = target.Faction;
                string victimName = victimFaction?.Name ?? "?";
                if (victimFaction != null && seenVictims.Add(victimFaction))
                    victimNames.Add(victimName);

                int tile = target.Tile;
                var oldComp = target.GetComponent<CompViralSpread>();
                float oldOff = oldComp != null ? oldComp.offensiveStrength : 100f;
                float oldDef = oldComp != null ? oldComp.defensiveStrength : 0f;
                SettlementTier oldTier = oldComp != null ? oldComp.tier : SettlementTier.T1;
                string oldSubType = oldComp?.subType;
                int oldArtilleryRoll = oldComp != null ? oldComp.artilleryRoll : -1;
                bool oldFreeT4 = oldComp != null && oldComp.freeT4ArtilleryDone;

                liberatedNames.Add(target.Label);
                lastTile = tile;

                WorldActions_DesperationRaid.SuppressLossNotify(target);
                Find.WorldObjects.Remove(target);

                Settlement newS = (Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
                newS.SetFaction(rebelFaction);
                newS.Tile = tile;
                newS.Name = SettlementNameGenerator.GenerateSettlementName(newS);

                var nc = newS.GetComponent<CompViralSpread>();
                if (nc != null)
                    nc.ApplyCopiedSettlementState(oldTier, oldOff, oldDef, oldSubType, oldArtilleryRoll, oldFreeT4);

                WorldActions_RoadBlocks.ClearIfPresent(tile);
                WorldActions_SpikeTraps.ClearIfPresent(tile);
                Find.WorldObjects.Add(newS);

                float afterOff = nc != null ? nc.offensiveStrength : oldOff;
                string logText = "TSA_WD_Log_Revolt_Entry".Translate(
                    newS.LabelCap,
                    victimName,
                    oldOff.ToString("F0"),
                    afterOff.ToString("F0"),
                    "");
                manager.AddLog(new SpreadLogEntry(logText, rebelFaction, newS, victimName, victimFaction));
            }

            if (liberatedNames.Count == 0) return false;

            if (stampSpecialEventCooldown)
                WorldActions_SpecialEventCooldown.Stamp(manager, SpecialWorldEventKind.Revolt);
            StampComebackCooldown(manager, rebelFaction);

            string settlementList = liberatedNames.ToCommaList(true);
            string victimsList = victimNames.Count > 0
                ? victimNames.ToCommaList(true)
                : "?";
            string letterText = "TSA_WD_Letter_Revolt_Text".Translate(
                rebelFaction.Name.Colorize(Color.cyan),
                settlementList,
                victimsList);

            Find.LetterStack.ReceiveLetter(
                "TSA_WD_Letter_Revolt_Label".Translate(),
                letterText,
                LetterDefOf.NeutralEvent,
                new GlobalTargetInfo(lastTile));

            WorldActions_Utils.RefreshMap();
            Find.World?.GetComponent<Text_WorldTierOnSettlements>()?.NotifyTierLabelCacheDirty();
            return true;
        }

        private static bool TryBuildRevoltTargets(
            Dictionary<Faction, List<Settlement>> byFaction,
            Settlement forcedSeed,
            out List<Settlement> targets)
        {
            targets = new List<Settlement>();
            if (byFaction == null || byFaction.Count == 0) return false;

            List<Settlement> firstPool;
            List<Faction> topOthers;

            if (forcedSeed != null)
            {
                Faction primary = forcedSeed.Faction;
                if (primary == null || !byFaction.TryGetValue(primary, out firstPool)
                    || firstPool == null || firstPool.Count < 3)
                    return false;

                topOthers = RankNpcFactionsBySettlementCount(byFaction, 3, exclude: primary);
            }
            else
            {
                List<Faction> top = RankNpcFactionsBySettlementCount(byFaction, 3, exclude: null);
                if (top.Count == 0) return false;
                firstPool = byFaction[top[0]];
                if (firstPool == null || firstPool.Count < 3) return false;
                topOthers = new List<Faction>();
                for (int i = 1; i < top.Count; i++)
                    topOthers.Add(top[i]);
            }

            int want = Rand.RangeInclusive(3, 7);

            if (forcedSeed != null)
            {
                targets.Add(forcedSeed);
                var withoutSeed = new List<Settlement>();
                for (int i = 0; i < firstPool.Count; i++)
                {
                    Settlement s = firstPool[i];
                    if (s != null && s != forcedSeed)
                        withoutSeed.Add(s);
                }
                targets.AddRange(TakeWeighted(withoutSeed, 2));
            }
            else
            {
                targets.AddRange(TakeWeighted(firstPool, 3));
            }

            int remaining = want - targets.Count;
            if (remaining > 0 && topOthers.Count > 0)
            {
                var pool = new List<Settlement>();
                for (int i = 0; i < topOthers.Count; i++)
                {
                    if (!byFaction.TryGetValue(topOthers[i], out List<Settlement> list) || list == null)
                        continue;
                    for (int j = 0; j < list.Count; j++)
                    {
                        Settlement s = list[j];
                        if (s != null && !targets.Contains(s))
                            pool.Add(s);
                    }
                }
                targets.AddRange(TakeWeighted(pool, remaining));
            }

            return targets.Count >= 3;
        }

        /// <summary>
        /// Blocks Turtle, Desperation, and Forward Assault for a revived rebel while they settle.
        /// Duration = max(turtle, desperation, revolt) cooldown days.
        /// </summary>
        public static void StampComebackCooldown(WorldComponent_SpreadManager manager, Faction faction)
        {
            if (manager == null || faction == null) return;
            var seth = WorldDominationMod.settings;
            if (seth == null) return;

            float days = Mathf.Max(seth.turtleCooldownDays, seth.desperationCooldownDays);
            days = Mathf.Max(days, seth.revoltCooldownDays);
            days = Mathf.Max(0.1f, days);

            if (manager.factionComebackCooldownByFaction == null)
                manager.factionComebackCooldownByFaction = new Dictionary<int, int>();
            int until = Find.TickManager.TicksGame + CompViralSpread.CooldownTicksFromDays(days);
            manager.factionComebackCooldownByFaction[faction.loadID] = until;
            WDVerbose.Msg($"ComebackCD stamp faction={faction.Name} days={days:F1} until={until}");
        }

        public static bool IsFactionOnComebackCooldown(WorldComponent_SpreadManager manager, Faction faction)
        {
            if (manager?.factionComebackCooldownByFaction == null || faction == null) return false;
            if (!manager.factionComebackCooldownByFaction.TryGetValue(faction.loadID, out int until)) return false;
            return Find.TickManager.TicksGame < until;
        }

        private static List<Faction> CollectDefeatedRebelCandidates()
        {
            var list = new List<Faction>();
            List<Faction> all = Find.FactionManager?.AllFactionsListForReading;
            if (all == null) return list;
            for (int i = 0; i < all.Count; i++)
            {
                Faction f = all[i];
                if (f == null || !f.defeated || f.IsPlayer) continue;
                if (!WorldActions_Utils.IsWdParticipant(f)) continue;
                list.Add(f);
            }
            return list;
        }

        private static Dictionary<Faction, List<Settlement>> BuildEligibleSettlementsByFaction()
        {
            var byFaction = new Dictionary<Faction, List<Settlement>>();
            List<Settlement> settlements = Find.WorldObjects?.Settlements;
            if (settlements == null) return byFaction;

            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement s = settlements[i];
                if (!IsEligibleRevoltVictim(s)) continue;
                Faction f = s.Faction;
                if (!byFaction.TryGetValue(f, out List<Settlement> list))
                {
                    list = new List<Settlement>();
                    byFaction[f] = list;
                }
                list.Add(s);
            }
            return byFaction;
        }

        private static bool IsEligibleRevoltVictim(Settlement s)
        {
            if (s == null || s.Destroyed || !s.Spawned) return false;
            if (s.Faction == null || s.Faction.IsPlayer || s.Faction.defeated) return false;
            if (s.GetComponent<CompViralSpread>() == null) return false;
            // HasMap, quests, raid CDs, excluded/space, player on tile, etc.
            if (WorldActions_Utils.IsSettlementProtected(s)) return false;
            return true;
        }

        private static List<Faction> RankNpcFactionsBySettlementCount(
            Dictionary<Faction, List<Settlement>> byFaction,
            int take,
            Faction exclude)
        {
            var ranked = new List<Faction>();
            if (byFaction == null || byFaction.Count == 0 || take <= 0) return ranked;

            var entries = new List<KeyValuePair<Faction, List<Settlement>>>();
            foreach (var kv in byFaction)
            {
                if (kv.Key == null || kv.Key.IsPlayer || kv.Key.defeated) continue;
                if (exclude != null && kv.Key == exclude) continue;
                if (kv.Value == null || kv.Value.Count == 0) continue;
                entries.Add(kv);
            }

            entries.Sort((a, b) => b.Value.Count.CompareTo(a.Value.Count));
            int n = Mathf.Min(take, entries.Count);
            for (int i = 0; i < n; i++)
                ranked.Add(entries[i].Key);
            return ranked;
        }

        private static float TierPickWeight(SettlementTier tier)
        {
            if (tier == SettlementTier.T1 || tier == SettlementTier.T2) return WeightT1T2;
            if (tier == SettlementTier.T3) return WeightT3;
            return WeightT4;
        }

        private static List<Settlement> TakeWeighted(List<Settlement> source, int count)
        {
            var result = new List<Settlement>();
            if (source == null || source.Count == 0 || count <= 0) return result;

            var pool = new List<Settlement>(source);
            count = Mathf.Min(count, pool.Count);
            for (int n = 0; n < count; n++)
            {
                float total = 0f;
                for (int i = 0; i < pool.Count; i++)
                {
                    CompViralSpread c = pool[i]?.GetComponent<CompViralSpread>();
                    total += TierPickWeight(c != null ? c.tier : SettlementTier.T1);
                }
                if (total <= 0f) break;

                float roll = Rand.Value * total;
                int pick = pool.Count - 1;
                for (int i = 0; i < pool.Count; i++)
                {
                    CompViralSpread c = pool[i]?.GetComponent<CompViralSpread>();
                    roll -= TierPickWeight(c != null ? c.tier : SettlementTier.T1);
                    if (roll <= 0f)
                    {
                        pick = i;
                        break;
                    }
                }
                result.Add(pool[pick]);
                pool.RemoveAt(pick);
            }
            return result;
        }

    }
}
