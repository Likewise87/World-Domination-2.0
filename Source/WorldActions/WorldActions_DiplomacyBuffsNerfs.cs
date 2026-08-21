using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using Verse;
using UnityEngine;

namespace TSA_WorldDomination
{
    public class AntiLeaderCoalitionPriorRelation : IExposable
    {
        public Faction factionA;
        public Faction factionB;
        public FactionRelationKind previousKind;

        public AntiLeaderCoalitionPriorRelation() { }

        public AntiLeaderCoalitionPriorRelation(Faction a, Faction b, FactionRelationKind kind)
        {
            factionA = a;
            factionB = b;
            previousKind = kind;
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref factionA, "factionA");
            Scribe_References.Look(ref factionB, "factionB");
            Scribe_Values.Look(ref previousKind, "previousKind", FactionRelationKind.Neutral);
        }
    }

    public static class WorldActions_DiplomacyBuffsNerfs
    {
        private const int GameLaunchGracePeriodTicks = 600; //60000

        /// <summary>Freeze duration after random diplomacy changes (also used by World Setup allegiance editor).</summary>
        public const int RandomDiplomacyFreezeDays = 10;
        public static int RandomDiplomacyFreezeDurationTicks => GenDate.TicksPerDay * RandomDiplomacyFreezeDays;

        /// <summary>Symmetric goodwill clamp from mod settings (default ±200).</summary>
        public static int MaxGoodwillAbs =>
            Mathf.Max(1, WorldDominationMod.settings?.maxGoodwill ?? WorldDominationSettings.DefMaxGoodwill);

        private static bool IsGracePeriodActive => Find.TickManager.TicksGame < GameLaunchGracePeriodTicks;

        /// <summary>On a fresh world, all diplomacy buffs/debuffs start on cooldown so none fire immediately.</summary>
        public static void InitializeNewGameBuffCooldowns(WorldComponent_SpreadManager manager)
        {
            if (manager == null) return;
            var seth = WorldDominationMod.settings;
            if (seth == null) return;

            int tickNow = Find.TickManager.TicksGame;

            manager.currentWorldLeader = null;
            manager.leaderHandicapExpiryTick = -1;
            manager.leaderHandicapCooldownTick = tickNow + Mathf.RoundToInt(seth.cdLeaderHandicapDays * 60000f);

            manager.currentWeakestUnderdog = null;
            manager.underdogBuffExpiryTick = -1;
            manager.underdogBuffCooldownTick = tickNow + Mathf.RoundToInt(seth.cdUnderdogBuffDays * 60000f);

            manager.expansionistZealFaction = null;
            manager.expansionistZealExpiryTick = -1;
            manager.expansionistZealCooldownTick = tickNow + Mathf.RoundToInt(seth.cdExpansionistZealDays * 60000f);

            manager.antiLeaderCoalitionTarget = null;
            manager.antiLeaderCoalitionMembers?.Clear();
            manager.antiLeaderCoalitionExpiryTick = -1;
            manager.antiLeaderCoalitionPriorRelations?.Clear();
            manager.antiLeaderCoalitionCooldownTick = tickNow + Mathf.RoundToInt(seth.cdAntiLeaderCoalitionDays * 60000f);
        }

        // 1. WORLD LEADER DEBUFF
        public static void ApplyLeaderHandicap(WorldComponent_SpreadManager manager, SpreadLogEntry.GlobalWorldStats precomputedStats = null)
        {
            if (IsGracePeriodActive) return;
            var seth = WorldDominationMod.settings;
            if (!seth.enableLeaderHandicap) return;

            // CHECK: Active status or Cooldown
            if (manager.currentWorldLeader != null && Find.TickManager.TicksGame < manager.leaderHandicapExpiryTick) return;
            if (Find.TickManager.TicksGame < manager.leaderHandicapCooldownTick) return;

            // Per-day likelihood (from Diplomacy settings: Trigger chance)
            if (Rand.Value > seth.leaderHandicapTriggerChance) return;

            var stats = precomputedStats ?? WorldStatsUtils.GetWorldPowerStats();
            var npcStats = new List<SpreadLogEntry.FactionStat>();
            for (int i = 0; i < stats.FactionStats.Count; i++)
            {
                if (stats.FactionStats[i].faction != Faction.OfPlayer)
                    npcStats.Add(stats.FactionStats[i]);
            }

            if (npcStats.Count < 2) return;

            var leaderStat = npcStats[0];
            var runnerUp = npcStats[1];

            float worldAvg = stats.GlobalTotalStr / stats.FactionStats.Count;
            float leadRatio = leaderStat.TotalStr / runnerUp.TotalStr;

            bool isDominant = leaderStat.TotalStr > worldAvg * 1.3f && leadRatio > 1.1f;
            if (!isDominant) return;

            Faction leader = leaderStat.faction;
            manager.currentWorldLeader = leader;

            int durationTicks = Mathf.RoundToInt(seth.durLeaderHandicapDays * 60000f);
            int cooldownTicks = Mathf.RoundToInt(seth.cdLeaderHandicapDays * 60000f);

            manager.leaderHandicapExpiryTick = Find.TickManager.TicksGame + durationTicks;
            manager.leaderHandicapCooldownTick = Find.TickManager.TicksGame + durationTicks + cooldownTicks;

            string msg = "TSA_WD_Diplo_LeaderHandicap_Msg".Translate(leader.Name.Colorize(Color.cyan), seth.durLeaderHandicapDays.ToString("F0"));
            var anchorSettlement = manager.GetAnchorSettlementForFaction(leader);
            var leaderLog = new SpreadLogEntry(msg, anchorSettlement, null);
            leaderLog.highlightKind = SpreadLogHighlightKind.Diplomacy;
            manager.AddLog(leaderLog); // action log lists a random settlement of that faction (targetA)

            if (seth.notifyLeaderHandicap)
            {
                Find.LetterStack.ReceiveLetter("TSA_WD_Diplo_LeaderHandicap_Label".Translate(), msg, LetterDefOf.NeutralEvent);
            }
        }

        // 2. WEAKEST FACTION BUFF
        public static void ApplyUnderdogBuff(WorldComponent_SpreadManager manager, SpreadLogEntry.GlobalWorldStats precomputedStats = null)
        {
            if (IsGracePeriodActive) return;
            var seth = WorldDominationMod.settings;
            if (!seth.enableUnderdogBuff) return;

            // CHECK: Active status or Cooldown
            if (manager.currentWeakestUnderdog != null && Find.TickManager.TicksGame < manager.underdogBuffExpiryTick) return;
            if (Find.TickManager.TicksGame < manager.underdogBuffCooldownTick) return;

            // Per-day likelihood (from Diplomacy settings: Trigger chance)
            if (Rand.Value > seth.underdogBuffTriggerChance) return;

            var stats = precomputedStats ?? WorldStatsUtils.GetWorldPowerStats();
            var npcStats = new List<SpreadLogEntry.FactionStat>();
            for (int i = 0; i < stats.FactionStats.Count; i++)
            {
                if (stats.FactionStats[i].faction != Faction.OfPlayer)
                    npcStats.Add(stats.FactionStats[i]);
            }

            if (npcStats.Count < 2) return;

            var weaklingStat = npcStats[npcStats.Count - 1];
            if (weaklingStat == null) return;

            float worldAvg = stats.GlobalTotalStr / stats.FactionStats.Count;

            bool isPathetic = weaklingStat.TotalStr < worldAvg * 0.8f;
            if (!isPathetic) return;

            Faction underdog = weaklingStat.faction;
            manager.currentWeakestUnderdog = underdog;

            int durationTicks = Mathf.RoundToInt(seth.durUnderdogBuffDays * 60000f);
            int cooldownTicks = Mathf.RoundToInt(seth.cdUnderdogBuffDays * 60000f);

            manager.underdogBuffExpiryTick = Find.TickManager.TicksGame + durationTicks;
            manager.underdogBuffCooldownTick = Find.TickManager.TicksGame + durationTicks + cooldownTicks;

            string msg = "TSA_WD_Diplo_UnderdogBuff_Msg".Translate(underdog.Name.Colorize(Color.cyan), seth.durUnderdogBuffDays.ToString("F0"));
            var anchorSettlement = manager.GetAnchorSettlementForFaction(underdog);
            var underdogLog = new SpreadLogEntry(msg, anchorSettlement, null);
            underdogLog.highlightKind = SpreadLogHighlightKind.Diplomacy;
            manager.AddLog(underdogLog); // action log lists a random settlement of that faction (targetA)

            if (seth.notifyUnderdogBuff)
            {
                Find.LetterStack.ReceiveLetter("TSA_WD_Diplo_UnderdogBuff_Label".Translate(), msg, LetterDefOf.NeutralEvent);
            }
        }

        // 3. COALITION AGAINST LEADER (NPC or player when world #1)
        public static void FormAntiLeaderCoalition(WorldComponent_SpreadManager manager, SpreadLogEntry.GlobalWorldStats precomputedStats = null)
        {
            if (IsGracePeriodActive) return;
            var seth = WorldDominationMod.settings;
            if (!seth.enableAntiLeaderCoalition) return;

            if (Find.TickManager.TicksGame < manager.antiLeaderCoalitionCooldownTick) return;

            if (Rand.Value > seth.antiLeaderCoalitionTriggerChance) return;

            var stats = precomputedStats ?? WorldStatsUtils.GetWorldPowerStats();
            if (stats.FactionStats == null || stats.FactionStats.Count < 2) return;

            var leaderStat = stats.FactionStats[0];
            var runnerUpStat = stats.FactionStats[1];
            if (leaderStat?.faction == null || runnerUpStat == null) return;

            float worldAvg = stats.GlobalTotalStr / stats.FactionStats.Count;
            float leadRatio = runnerUpStat.TotalStr > 0f ? leaderStat.TotalStr / runnerUpStat.TotalStr : 999f;
            bool isDominant = leaderStat.TotalStr > worldAvg * 1.4f && leadRatio > 1.1f;
            if (!isDominant) return;

            Faction leader = leaderStat.faction;
            bool leaderIsPlayer = leader.IsPlayer;

            var npcStats = new List<SpreadLogEntry.FactionStat>();
            for (int i = 0; i < stats.FactionStats.Count; i++)
            {
                if (stats.FactionStats[i].faction != null && !stats.FactionStats[i].faction.IsPlayer)
                    npcStats.Add(stats.FactionStats[i]);
            }

            if (leaderIsPlayer)
            {
                if (npcStats.Count < 3) return;
            }
            else if (npcStats.Count < 4)
            {
                return;
            }

            var statByFaction = new Dictionary<Faction, SpreadLogEntry.FactionStat>();
            for (int i = 0; i < npcStats.Count; i++)
                statByFaction[npcStats[i].faction] = npcStats[i];

            Faction skipFaction;
            if (leaderIsPlayer)
            {
                skipFaction = runnerUpStat.faction != null && !runnerUpStat.faction.IsPlayer
                    ? runnerUpStat.faction
                    : (npcStats.Count > 0 ? npcStats[0].faction : null);
            }
            else
            {
                skipFaction = npcStats.Count > 1 ? npcStats[1].faction : null;
            }

            var candidates = new List<Faction>();
            int startIdx = leaderIsPlayer ? 0 : 2;
            for (int i = npcStats.Count - 1; i >= startIdx; i--)
            {
                Faction f = npcStats[i].faction;
                if (f == skipFaction) continue;
                candidates.Add(f);
            }

            List<Faction> coalitionMembers = new List<Faction>();
            float combinedCoalitionStr = 0f;
            int targetMemberCount = 3;

            foreach (var candidate in candidates)
            {
                if (coalitionMembers.Count >= targetMemberCount) break;
                if (seth.IsPairLocked(candidate, leader) || IsFrozen(candidate, leader, manager)) continue;

                bool compatible = true;
                foreach (var member in coalitionMembers)
                {
                    if (seth.IsPairLocked(candidate, member) || IsFrozen(candidate, member, manager))
                    {
                        compatible = false;
                        break;
                    }
                }

                if (compatible)
                {
                    coalitionMembers.Add(candidate);
                    if (statByFaction.TryGetValue(candidate, out var cs))
                        combinedCoalitionStr += cs.TotalStr;
                }
            }

            if (coalitionMembers.Count < 2 || combinedCoalitionStr < (leaderStat.TotalStr * 0.5f))
            {
                if (Prefs.DevMode && coalitionMembers.Count >= 2)
                    Log.Message($"[WorldDomination] Coalition formed but too weak ({combinedCoalitionStr} vs {leaderStat.TotalStr}). Skipping.");
                return;
            }

            int durationTicks = Mathf.RoundToInt(seth.durAntiLeaderCoalitionDays * 60000f);
            int freezeExpiry = Find.TickManager.TicksGame + durationTicks;
            manager.antiLeaderCoalitionCooldownTick = freezeExpiry + Mathf.RoundToInt(seth.cdAntiLeaderCoalitionDays * 60000f);

            manager.antiLeaderCoalitionTarget = leader;
            manager.antiLeaderCoalitionMembers = new List<Faction>(coalitionMembers);
            manager.antiLeaderCoalitionExpiryTick = freezeExpiry;
            if (manager.antiLeaderCoalitionPriorRelations == null)
                manager.antiLeaderCoalitionPriorRelations = new List<AntiLeaderCoalitionPriorRelation>();
            else
                manager.antiLeaderCoalitionPriorRelations.Clear();

            for (int i = 0; i < coalitionMembers.Count; i++)
            {
                for (int j = i + 1; j < coalitionMembers.Count; j++)
                    TryForceAndSnapshot(coalitionMembers[i], coalitionMembers[j], FactionRelationKind.Ally, manager, freezeExpiry);
                TryForceAndSnapshot(coalitionMembers[i], leader, FactionRelationKind.Hostile, manager, freezeExpiry);
            }

            var memberNamesList = new List<string>();
            for (int i = 0; i < coalitionMembers.Count; i++)
                memberNamesList.Add(coalitionMembers[i].Name.Colorize(Color.cyan));
            string memberNames = memberNamesList.ToCommaList(true);
            string msg = "TSA_WD_Diplo_Coalition_Msg".Translate(memberNames, leader.Name.Colorize(Color.red));
            Settlement logAnchor = leaderIsPlayer
                ? manager.GetAnchorSettlementForFaction(coalitionMembers[0])
                : manager.GetAnchorSettlementForFaction(leader);
            var coalitionLog = new SpreadLogEntry(msg, logAnchor, null);
            coalitionLog.highlightKind = SpreadLogHighlightKind.Diplomacy;
            manager.AddLog(coalitionLog);

            if (seth.notifyAntiLeaderCoalition)
                Find.LetterStack.ReceiveLetter("TSA_WD_Diplo_Coalition_Label".Translate(), msg, LetterDefOf.NeutralEvent);
        }

        private static void TryForceAndSnapshot(Faction a, Faction b, FactionRelationKind next, WorldComponent_SpreadManager manager, int freezeExpiry)
        {
            if (!ForceDiplomacy(a, b, next, manager, freezeExpiry, out FactionRelationKind prior))
                return;
            manager.antiLeaderCoalitionPriorRelations.Add(new AntiLeaderCoalitionPriorRelation(a, b, prior));
        }

        /// <summary>
        /// Restore pre-coalition relations when the coalition is no longer active (expiry or leader defeated).
        /// Clears coalition state. Old saves with no prior list only clear refs (no letter).
        /// </summary>
        public static void DissolveAntiLeaderCoalition(WorldComponent_SpreadManager manager)
        {
            if (manager == null) return;

            var priors = manager.antiLeaderCoalitionPriorRelations;
            Faction leader = manager.antiLeaderCoalitionTarget;
            var members = manager.antiLeaderCoalitionMembers;
            bool leaderIsPlayer = leader != null && leader.IsPlayer;

            if (priors == null || priors.Count == 0)
            {
                ClearCoalitionState(manager);
                return;
            }

            int freezeEnded = Find.TickManager.TicksGame;
            var detailLines = new List<string>();
            for (int i = 0; i < priors.Count; i++)
            {
                var entry = priors[i];
                if (entry == null) continue;
                Faction a = entry.factionA;
                Faction b = entry.factionB;
                if (a == null || b == null || a.defeated || b.defeated) continue;
                if (!ForceDiplomacy(a, b, entry.previousKind, manager, freezeEnded, out _))
                    continue;

                string colorA = a.Name.Colorize(Color.cyan);
                string colorB = b.Name.Colorize(Color.cyan);
                detailLines.Add("TSA_WD_Diplo_CoalitionAbandoned_Pair".Translate(colorA, colorB, entry.previousKind.GetLabel()));
            }

            if (detailLines.Count > 0)
            {
                string details = string.Join("\n", detailLines);
                string msg = "TSA_WD_Diplo_CoalitionAbandoned_Msg".Translate(details);
                Settlement logAnchor = null;
                if (leaderIsPlayer && members != null && members.Count > 0)
                    logAnchor = manager.GetAnchorSettlementForFaction(members[0]);
                else if (leader != null && !leader.defeated)
                    logAnchor = manager.GetAnchorSettlementForFaction(leader);
                else if (members != null && members.Count > 0)
                    logAnchor = manager.GetAnchorSettlementForFaction(members[0]);

                var abandonedLog = new SpreadLogEntry(msg, logAnchor, null);
                abandonedLog.highlightKind = SpreadLogHighlightKind.Diplomacy;
                manager.AddLog(abandonedLog);

                var seth = WorldDominationMod.settings;
                if (seth != null && seth.notifyAntiLeaderCoalition)
                    Find.LetterStack.ReceiveLetter("TSA_WD_Diplo_CoalitionAbandoned_Label".Translate(), msg, LetterDefOf.NeutralEvent);
            }

            ClearCoalitionState(manager);
        }

        private static void ClearCoalitionState(WorldComponent_SpreadManager manager)
        {
            manager.antiLeaderCoalitionTarget = null;
            manager.antiLeaderCoalitionMembers?.Clear();
            manager.antiLeaderCoalitionExpiryTick = -1;
            manager.antiLeaderCoalitionPriorRelations?.Clear();
        }

        private static bool IsFrozen(Faction a, Faction b, WorldComponent_SpreadManager manager)
        {
            return manager.diplomacyFreezeTicks.TryGetValue(GetPairKey(a, b), out int expiry) && Find.TickManager.TicksGame < expiry;
        }

        /// <summary>Days left on the WD diplomacy freeze for this NPC pair (after a random change or coalition ForceDiplomacy). Symmetric.</summary>
        public static bool TryGetDiplomacyFreezeDaysRemaining(Faction a, Faction b, WorldComponent_SpreadManager manager, out float daysRemaining)
        {
            daysRemaining = 0f;
            if (a == null || b == null || a == b || manager?.diplomacyFreezeTicks == null) return false;
            if (!manager.diplomacyFreezeTicks.TryGetValue(GetPairKey(a, b), out int expiry)) return false;
            int remaining = expiry - Find.TickManager.TicksGame;
            if (remaining <= 0) return false;
            daysRemaining = remaining / 60000f;
            return true;
        }

        /// <summary>Highest goodwill that stays Neutral (<see cref="KindFromGoodwill"/> Ally starts at 75).</summary>
        public const int FrozenPairMaxGoodwillWithoutAlly = 74;

        /// <summary>
        /// Starts the same 10-day pair freeze as random diplomacy without rewriting relation kind or goodwill.
        /// </summary>
        public static void ApplyRandomDiplomacyFreeze(Faction facA, Faction facB, WorldComponent_SpreadManager manager = null)
        {
            if (facA == null || facB == null || facA == facB) return;
            manager ??= Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (manager == null) return;
            if (manager.diplomacyFreezeTicks == null)
                manager.diplomacyFreezeTicks = new Dictionary<long, int>();
            manager.diplomacyFreezeTicks[GetPairKey(facA, facB)] =
                Find.TickManager.TicksGame + RandomDiplomacyFreezeDurationTicks;
        }

        /// <summary>
        /// NPC–NPC trader goodwill. Frozen pairs cannot cross into Ally (cap 74, kind stays Neutral).
        /// Unfrozen Neutral → Ally starts a 10-day freeze, action-log, and optional letter.
        /// </summary>
        public static void ApplyNpcTraderGoodwill(Faction sender, Faction receiver, int goodwill)
        {
            if (sender == null || receiver == null || sender == receiver || goodwill <= 0) return;
            if (sender.IsPlayer || receiver.IsPlayer) return;

            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            FactionRelation relA = sender.RelationWith(receiver, true);
            FactionRelation relB = receiver.RelationWith(sender, true);
            if (relA == null || relB == null) return;

            FactionRelationKind beforeKind = WorldActions_Utils.SafeRelationKindWith(sender, receiver);
            bool frozen = TryGetDiplomacyFreezeDaysRemaining(sender, receiver, manager, out _);
            int current = relA.baseGoodwill;
            int projected = Mathf.Clamp(current + goodwill, -MaxGoodwillAbs, MaxGoodwillAbs);

            if (frozen
                && beforeKind != FactionRelationKind.Ally
                && KindFromGoodwill(projected) == FactionRelationKind.Ally)
            {
                if (current >= FrozenPairMaxGoodwillWithoutAlly)
                    return;
                relA.baseGoodwill = FrozenPairMaxGoodwillWithoutAlly;
                relB.baseGoodwill = FrozenPairMaxGoodwillWithoutAlly;
                return;
            }

            sender.TryAffectGoodwillWith(receiver, goodwill);
            FactionRelationKind afterKind = WorldActions_Utils.SafeRelationKindWith(sender, receiver);
            if (beforeKind == afterKind || afterKind != FactionRelationKind.Ally)
                return;

            ApplyRandomDiplomacyFreeze(sender, receiver, manager);
            ReinforcementNeighborCache.BumpGeneration();
            NotifyNpcTradeAlly(sender, receiver, manager);
        }

        private static void NotifyNpcTradeAlly(Faction facA, Faction facB, WorldComponent_SpreadManager manager)
        {
            if (facA == null || facB == null) return;
            string colorA = facA.Name.Colorize(Color.cyan);
            string colorB = facB.Name.Colorize(Color.cyan);
            string msg = "TSA_WD_Diplo_Flavor_AllyByTrade".Translate(colorA, colorB);
            WorldObject anchor = manager?.GetAnchorSettlementForFaction(facA);
            if (manager != null)
            {
                var entry = new SpreadLogEntry(msg, anchor, null);
                entry.highlightKind = SpreadLogHighlightKind.Diplomacy;
                manager.AddLog(entry);
            }

            if (WorldDominationMod.settings?.notifyTradeAllyDiplomacy ?? WorldDominationSettings.DefNotifyTradeAllyDiplomacy)
            {
                Find.LetterStack.ReceiveLetter(
                    "TSA_WD_Diplo_Change_Label".Translate(),
                    msg,
                    LetterDefOf.NeutralEvent,
                    anchor);
            }
        }

        // 4. EXPANSIONIST ZEAL BUFF (+50% raid range)
        public static void ApplyExpansionistZeal(WorldComponent_SpreadManager manager)
        {
            if (IsGracePeriodActive) return;
            var seth = WorldDominationMod.settings;
            if (!seth.enableExpansionistZeal) return;

            // CHECK: Active status or Cooldown
            if (manager.expansionistZealFaction != null && Find.TickManager.TicksGame < manager.expansionistZealExpiryTick) return;
            if (Find.TickManager.TicksGame < manager.expansionistZealCooldownTick) return;

            // Per-day likelihood (from Diplomacy settings: Trigger chance)
            if (Rand.Value > seth.zealTriggerChance) return;

            var factions = new List<Faction>();
            foreach (var f in Find.FactionManager.AllFactionsVisible)
            {
                if (!f.IsPlayer && !f.defeated && !WorldActions_Utils.IsExcludedFaction(f))
                    factions.Add(f);
            }

            if (factions.Count == 0) return;

            Faction zealot = factions.RandomElement();
            manager.expansionistZealFaction = zealot;

            int durationTicks = Mathf.RoundToInt(seth.durExpansionistZealDays * 60000f);
            int cooldownTicks = Mathf.RoundToInt(seth.cdExpansionistZealDays * 60000f);

            manager.expansionistZealExpiryTick = Find.TickManager.TicksGame + durationTicks;
            manager.expansionistZealCooldownTick = Find.TickManager.TicksGame + durationTicks + cooldownTicks;

            string zealotName = zealot.Name.Colorize(Color.cyan);
            string label = "TSA_WD_Diplo_Zeal_Label".Translate(zealotName);
            string msg = "TSA_WD_Diplo_ExpansionistZeal_Msg".Translate(zealotName, seth.durExpansionistZealDays.ToString("F0"));

            var anchor = manager.GetAnchorSettlementForFaction(zealot);
            var zealLog = new SpreadLogEntry(msg, anchor, null);
            zealLog.highlightKind = SpreadLogHighlightKind.Diplomacy;
            manager.AddLog(zealLog); // action log lists a random settlement of that faction (targetA)

            if (seth.notifyExpansionistZeal)
            {
                LetterDef letterDef = WorldActions_Utils.SafeHostileTo(zealot, Faction.OfPlayer) ? LetterDefOf.NegativeEvent : LetterDefOf.NeutralEvent;
                Find.LetterStack.ReceiveLetter(label, msg, letterDef, new GlobalTargetInfo(anchor));
            }
        }
        /// <summary>Returns true if the change was applied (both factions had a relation); false if either relation was null (fail gracefully).</summary>
        private static bool ForceDiplomacy(Faction facA, Faction facB, FactionRelationKind next, WorldComponent_SpreadManager manager, int expiry, out FactionRelationKind previousKind)
        {
            int targetGoodwill = next == FactionRelationKind.Hostile ? -80 : next == FactionRelationKind.Ally ? 80 : 0;
            return ForceDiplomacyGoodwill(facA, facB, targetGoodwill, next, manager, expiry, out previousKind);
        }

        /// <summary>World Setup / editor: set exact goodwill and relation kind (e.g. ±75).</summary>
        private static bool ForceDiplomacyGoodwill(
            Faction facA,
            Faction facB,
            int targetGoodwill,
            FactionRelationKind next,
            WorldComponent_SpreadManager manager,
            int expiry,
            out FactionRelationKind previousKind)
        {
            previousKind = FactionRelationKind.Neutral;
            if (facA == null || facB == null || facA == facB || manager == null) return false;
            FactionRelation relA = facA.RelationWith(facB, true);
            FactionRelation relB = facB.RelationWith(facA, true);
            if (relA == null || relB == null)
                return false;

            previousKind = relA.kind;
            bool previousWasAlly = previousKind == FactionRelationKind.Ally;
            targetGoodwill = Mathf.Clamp(targetGoodwill, -MaxGoodwillAbs, MaxGoodwillAbs);
            relA.baseGoodwill = targetGoodwill;
            relA.kind = next;
            relB.baseGoodwill = targetGoodwill;
            relB.kind = next;

            bool flag;
            relA.CheckKindThresholds(facA, false, null, GlobalTargetInfo.Invalid, out flag);
            relB.CheckKindThresholds(facB, false, null, GlobalTargetInfo.Invalid, out flag);

            if (next == FactionRelationKind.Ally || previousWasAlly)
                ReinforcementNeighborCache.BumpGeneration();

            if (manager.diplomacyFreezeTicks == null)
                manager.diplomacyFreezeTicks = new Dictionary<long, int>();
            long key = GetPairKey(facA, facB);
            manager.diplomacyFreezeTicks[key] = expiry;
            return true;
        }

        /// <summary>Player negotiate / shared surgical relation write with pair freeze. Expiry tick inclusive end.</summary>
        public static bool TryForceDiplomacyWithFreeze(
            Faction facA,
            Faction facB,
            FactionRelationKind next,
            int freezeExpiryTick,
            out FactionRelationKind previousKind)
        {
            previousKind = FactionRelationKind.Neutral;
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (manager == null) return false;
            if (manager.diplomacyFreezeTicks == null)
                manager.diplomacyFreezeTicks = new Dictionary<long, int>();
            return ForceDiplomacy(facA, facB, next, manager, freezeExpiryTick, out previousKind);
        }

        /// <summary>World Setup: Neutral=0, Ally=+75, Hostile=-75 (or any typed goodwill).</summary>
        public static bool TrySetDiplomacyGoodwill(
            Faction facA,
            Faction facB,
            int goodwill,
            int freezeExpiryTick,
            out FactionRelationKind previousKind)
        {
            previousKind = FactionRelationKind.Neutral;
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (manager == null) return false;
            goodwill = Mathf.Clamp(goodwill, -MaxGoodwillAbs, MaxGoodwillAbs);
            FactionRelationKind next = KindFromGoodwill(goodwill);
            return ForceDiplomacyGoodwill(facA, facB, goodwill, next, manager, freezeExpiryTick, out previousKind);
        }

        public static FactionRelationKind KindFromGoodwill(int goodwill)
        {
            if (goodwill <= -75) return FactionRelationKind.Hostile;
            if (goodwill >= 75) return FactionRelationKind.Ally;
            return FactionRelationKind.Neutral;
        }

        public static int GoodwillForKind(FactionRelationKind kind)
        {
            if (kind == FactionRelationKind.Hostile) return -75;
            if (kind == FactionRelationKind.Ally) return 75;
            return 0;
        }

        /// <summary>Negotiate success freeze: 15 days from now.</summary>
        public static int NegotiateFreezeExpiryTick() =>
            Find.TickManager.TicksGame + GenDate.TicksPerDay * 15;

        public static void TryChangeAllegiances(WorldComponent_SpreadManager manager)
        {
            if (IsGracePeriodActive) return;
            if (!WorldDominationMod.settings.enableRandomDiplomacy) return;
            if (Current.ProgramState != ProgramState.Playing || Faction.OfPlayer == null) return;

            var seth = WorldDominationMod.settings;
            if (Rand.Value > seth.diplomacyChangeChance) return;

            var validFactions = new List<Faction>();
            foreach (var f in Find.FactionManager.AllFactionsVisible)
            {
                if (f != null && !f.IsPlayer && !WorldActions_Utils.IsExcludedFaction(f))
                    validFactions.Add(f);
            }

            if (validFactions.Count < 2) return;

            int tickNow = Find.TickManager.TicksGame;
            var changeablePairs = new List<Pair<Faction, Faction>>();
            for (int i = 0; i < validFactions.Count; i++)
            {
                for (int j = i + 1; j < validFactions.Count; j++)
                {
                    Faction a = validFactions[i];
                    Faction b = validFactions[j];
                    if (seth.IsPairLocked(a, b)) continue;
                    if (manager.diplomacyFreezeTicks.TryGetValue(GetPairKey(a, b), out int expiry) && tickNow < expiry) continue;
                    changeablePairs.Add(new Pair<Faction, Faction>(a, b));
                }
            }

            if (changeablePairs.Count == 0) return;

            Pair<Faction, Faction> pick = changeablePairs.RandomElement();
            Faction facA = pick.First;
            Faction facB = pick.Second;

            // Use safe lookup to avoid "null relation, returning dummy" when factions have no relation entry
            FactionRelationKind current = WorldActions_Utils.SafeRelationKindWith(facA, facB);
            FactionRelationKind next;

            if (current == FactionRelationKind.Hostile) next = FactionRelationKind.Neutral;
            else if (current == FactionRelationKind.Ally) next = FactionRelationKind.Neutral;
            else next = Rand.Value < 0.5f ? FactionRelationKind.Hostile : FactionRelationKind.Ally;

            if (!ForceDiplomacy(facA, facB, next, manager, Find.TickManager.TicksGame + RandomDiplomacyFreezeDurationTicks, out _))
                return;

            string msg = GetDiplomacyFlavorText(facA, facB, current, next);
            var setA = manager.GetAnchorSettlementForFaction(facA);

            var allegianceLog = new SpreadLogEntry(msg, setA, null);
            allegianceLog.highlightKind = SpreadLogHighlightKind.Diplomacy;
            manager.AddLog(allegianceLog); // action log lists a random settlement of that faction (targetA)

            if (seth.notifyRandomDiplomacy)
            {
                Find.LetterStack.ReceiveLetter("TSA_WD_Diplo_Change_Label".Translate(), msg, LetterDefOf.NeutralEvent, setA);
            }
        }

        /// <summary>
        /// Daily chance among the strongest NPC factions (top share of world strength ranking)
        /// to escalate one eligible pair: Ally → Neutral, or Neutral → Hostile.
        /// Never Ally → Hostile in one step. Shares pair locks and diplomacy freezes.
        /// </summary>
        public static void TryStrongFactionWar(WorldComponent_SpreadManager manager, SpreadLogEntry.GlobalWorldStats stats)
        {
            if (IsGracePeriodActive) return;
            if (manager == null) return;
            var seth = WorldDominationMod.settings;
            if (seth == null || !seth.enableStrongFactionWar) return;
            if (Current.ProgramState != ProgramState.Playing || Faction.OfPlayer == null) return;

            if (seth.strongFactionWarRequireMidOrLate && !WdEscalation.IsMidOrLate(manager))
                return;

            if (stats?.FactionStats == null || stats.FactionStats.Count < 2) return;

            var npcFactions = new List<Faction>();
            for (int i = 0; i < stats.FactionStats.Count; i++)
            {
                Faction f = stats.FactionStats[i]?.faction;
                if (f == null || f.IsPlayer || f.defeated || WorldActions_Utils.IsExcludedFaction(f))
                    continue;
                npcFactions.Add(f);
            }

            if (npcFactions.Count < 2) return;

            float topPct = Mathf.Clamp(seth.strongFactionWarTopPct, 0.05f, 1f);
            int take = Mathf.Max(2, Mathf.CeilToInt(npcFactions.Count * topPct));
            take = Mathf.Min(take, npcFactions.Count);

            int tickNow = Find.TickManager.TicksGame;
            if (manager.diplomacyFreezeTicks == null)
                manager.diplomacyFreezeTicks = new Dictionary<long, int>();

            // Eligible pairs only: Neutral or Ally, unlocked, not on diplomacy cooldown.
            var pairs = new List<Pair<Faction, Faction>>();
            for (int i = 0; i < take; i++)
            {
                for (int j = i + 1; j < take; j++)
                {
                    Faction a = npcFactions[i];
                    Faction b = npcFactions[j];
                    if (seth.IsPairLocked(a, b)) continue;
                    if (manager.diplomacyFreezeTicks.TryGetValue(GetPairKey(a, b), out int expiry) && tickNow < expiry)
                        continue;
                    FactionRelationKind kind = WorldActions_Utils.SafeRelationKindWith(a, b);
                    if (kind != FactionRelationKind.Neutral && kind != FactionRelationKind.Ally)
                        continue;
                    pairs.Add(new Pair<Faction, Faction>(a, b));
                }
            }

            if (pairs.Count == 0) return;
            if (Rand.Value > Mathf.Clamp01(seth.strongFactionWarChance)) return;

            Pair<Faction, Faction> pick = pairs.RandomElement();
            Faction facA = pick.First;
            Faction facB = pick.Second;
            FactionRelationKind current = WorldActions_Utils.SafeRelationKindWith(facA, facB);
            // Never Ally → Hostile in one step; cool to Neutral first (same 10-day freeze).
            FactionRelationKind next = current == FactionRelationKind.Ally
                ? FactionRelationKind.Neutral
                : FactionRelationKind.Hostile;

            // Same 10-day pair freeze as random diplomacy (Window_Diplomacy CD chip).
            int freezeExpiry = tickNow + RandomDiplomacyFreezeDurationTicks;
            if (!ForceDiplomacy(facA, facB, next, manager, freezeExpiry, out _))
                return;

            string colorA = facA.Name.Colorize(Color.cyan);
            string colorB = facB.Name.Colorize(Color.cyan);
            string msg = next == FactionRelationKind.Hostile
                ? "TSA_WD_Diplo_StrongFactionWar_Msg".Translate(colorA, colorB)
                : "TSA_WD_Diplo_StrongFactionWar_Cool_Msg".Translate(colorA, colorB);

            var setA = manager.GetAnchorSettlementForFaction(facA);
            var log = new SpreadLogEntry(msg, setA, null);
            log.highlightKind = SpreadLogHighlightKind.Diplomacy;
            manager.AddLog(log);

            if (seth.notifyStrongFactionWar)
            {
                LetterDef letterDef = next == FactionRelationKind.Hostile
                    ? LetterDefOf.NegativeEvent
                    : LetterDefOf.NeutralEvent;
                string label = next == FactionRelationKind.Hostile
                    ? "TSA_WD_Diplo_StrongFactionWar_Label".Translate()
                    : "TSA_WD_Diplo_StrongFactionWar_Cool_Label".Translate();
                Find.LetterStack.ReceiveLetter(label, msg, letterDef, setA);
            }
        }

        /// <summary>
        /// Debug: apply one random-diplomacy change immediately (ignores enable flag, chance, grace, locks, and existing freeze).
        /// Returns a short status string for the debug message.
        /// </summary>
        public static string DebugForceRandomDiplomacyChange(WorldComponent_SpreadManager manager)
        {
            if (manager == null) return "WD debug: no spread manager.";
            if (Current.ProgramState != ProgramState.Playing || Faction.OfPlayer == null)
                return "WD debug: not in a playing game.";

            var validFactions = new List<Faction>();
            foreach (var f in Find.FactionManager.AllFactionsVisible)
            {
                if (f != null && !f.IsPlayer && !WorldActions_Utils.IsExcludedFaction(f))
                    validFactions.Add(f);
            }

            if (validFactions.Count < 2)
                return "WD debug: need at least 2 NPC factions.";

            var pairs = new List<Pair<Faction, Faction>>();
            for (int i = 0; i < validFactions.Count; i++)
            {
                for (int j = i + 1; j < validFactions.Count; j++)
                    pairs.Add(new Pair<Faction, Faction>(validFactions[i], validFactions[j]));
            }

            // Prefer pairs that can actually change relation (have mutual relation entries).
            pairs.Shuffle();
            Faction facA = null;
            Faction facB = null;
            FactionRelationKind current = FactionRelationKind.Neutral;
            FactionRelationKind next = FactionRelationKind.Neutral;
            bool applied = false;
            for (int i = 0; i < pairs.Count; i++)
            {
                facA = pairs[i].First;
                facB = pairs[i].Second;
                current = WorldActions_Utils.SafeRelationKindWith(facA, facB);
                if (current == FactionRelationKind.Hostile) next = FactionRelationKind.Neutral;
                else if (current == FactionRelationKind.Ally) next = FactionRelationKind.Neutral;
                else next = Rand.Value < 0.5f ? FactionRelationKind.Hostile : FactionRelationKind.Ally;

                if (ForceDiplomacy(facA, facB, next, manager, Find.TickManager.TicksGame + RandomDiplomacyFreezeDurationTicks, out _))
                {
                    applied = true;
                    break;
                }
            }

            if (!applied)
                return "WD debug: no pair had a valid relation to change.";

            string msg = GetDiplomacyFlavorText(facA, facB, current, next);
            var setA = manager.GetAnchorSettlementForFaction(facA);
            var allegianceLog = new SpreadLogEntry(msg, setA, null);
            allegianceLog.highlightKind = SpreadLogHighlightKind.Diplomacy;
            manager.AddLog(allegianceLog);

            if (WorldDominationMod.settings.notifyRandomDiplomacy)
                Find.LetterStack.ReceiveLetter("TSA_WD_Diplo_Change_Label".Translate(), msg, LetterDefOf.NeutralEvent, setA);

            return $"WD debug: {facA.Name} ↔ {facB.Name}: {current} → {next} (10-day freeze).";
        }

        private static string GetDiplomacyFlavorText(Faction facA, Faction facB, FactionRelationKind current, FactionRelationKind next)
        {
            string colorA = facA.Name.Colorize(Color.cyan);
            string colorB = facB.Name.Colorize(Color.cyan);
            if (next == FactionRelationKind.Hostile) return "TSA_WD_Diplo_Flavor_War".Translate(colorA, colorB);
            if (next == FactionRelationKind.Ally) return "TSA_WD_Diplo_Flavor_Ally".Translate(colorA, colorB);
            return "TSA_WD_Diplo_Flavor_Peace".Translate(colorA, colorB);
        }

        private static long GetPairKey(Faction a, Faction b)
        {
            int id1 = a.loadID;
            int id2 = b.loadID;
            return id1 < id2 ? (long)id1 << 32 | (uint)id2 : (long)id2 << 32 | (uint)id1;
        }
    }
}