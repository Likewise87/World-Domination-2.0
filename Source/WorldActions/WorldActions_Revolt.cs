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
            if (Rand.Value > seth.revoltChance) return;

            Faction rebelFaction = defeatedFactions.RandomElement();

            Dictionary<Faction, List<Settlement>> byFaction = BuildEligibleSettlementsByFaction();
            List<Faction> top = RankNpcFactionsBySettlementCount(byFaction, 3);
            if (top.Count == 0) return;

            List<Settlement> firstPool = byFaction[top[0]];
            if (firstPool == null || firstPool.Count < 3) return;

            int want = Rand.RangeInclusive(3, 7);
            var targets = new List<Settlement>();
            targets.AddRange(TakeWeighted(firstPool, 3));

            int remaining = want - targets.Count;
            if (remaining > 0 && top.Count > 1)
            {
                var pool = new List<Settlement>();
                for (int i = 1; i < top.Count; i++)
                {
                    if (!byFaction.TryGetValue(top[i], out List<Settlement> list) || list == null) continue;
                    for (int j = 0; j < list.Count; j++)
                    {
                        Settlement s = list[j];
                        if (s != null && !targets.Contains(s))
                            pool.Add(s);
                    }
                }
                targets.AddRange(TakeWeighted(pool, remaining));
            }

            if (targets.Count < 3) return;

            rebelFaction.defeated = false;

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

                liberatedNames.Add(target.Label);
                lastTile = tile;

                Find.WorldObjects.Remove(target);

                Settlement newS = (Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
                newS.SetFaction(rebelFaction);
                newS.Tile = tile;
                newS.Name = SettlementNameGenerator.GenerateSettlementName(newS);

                var nc = newS.GetComponent<CompViralSpread>();
                if (nc != null)
                    ApplyCopiedSettlementState(nc, oldTier, oldOff, oldDef, oldSubType);

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

            if (liberatedNames.Count == 0) return;

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
                if (WorldActions_Utils.IsExcludedFaction(f)) continue;
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
            int take)
        {
            var ranked = new List<Faction>();
            if (byFaction == null || byFaction.Count == 0 || take <= 0) return ranked;

            var entries = new List<KeyValuePair<Faction, List<Settlement>>>();
            foreach (var kv in byFaction)
            {
                if (kv.Key == null || kv.Key.IsPlayer || kv.Key.defeated) continue;
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

        /// <summary>
        /// Initialize may have rolled a random tier; overwrite with the stolen settlement's full state.
        /// Set strength before <see cref="CompViralSpread.SetState"/> so it does not re-roll offense.
        /// </summary>
        private static void ApplyCopiedSettlementState(
            CompViralSpread nc,
            SettlementTier tier,
            float offensive,
            float defensive,
            string subType)
        {
            if (nc == null) return;
            FloatRange range = CompViralSpread.GetStrengthRange(tier);
            float clampedOff = Mathf.Clamp(Mathf.Max(offensive, 0.01f), range.min, range.max);
            nc.offensiveStrength = clampedOff;
            nc.SetState(tier);
            if (!string.IsNullOrEmpty(subType))
                nc.subType = subType;
            nc.offensiveStrength = clampedOff;
            float defMax = nc.GetBaseDefensiveStrength();
            nc.defensiveStrength = defensive > 0f ? Mathf.Min(defensive, defMax) : defMax;
        }
    }
}
