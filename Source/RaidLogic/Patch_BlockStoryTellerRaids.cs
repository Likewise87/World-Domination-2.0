using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;
using RimWorld;
using RimWorld.Planet;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Optionally blocks random storyteller raids when settings demand; does not modify storyteller raid points.
    /// Quest/scripted raids must not be blocked: vanilla uses <see cref="IncidentParms.forced"/> for many non-storyteller fires;
    /// Royalty+ may also set a <c>quest</c> field on <see cref="IncidentParms"/> (reflection, version-tolerant).
    /// </summary>
    [HarmonyPatch(typeof(IncidentWorker_RaidEnemy), "TryExecuteWorker")]
    public static class Patch_RaidEnemy_AdjustPoints
    {
        private static FieldInfo cachedIncidentParmsQuestField;
        private static bool cachedIncidentParmsQuestFieldResolved;
        private static int wdFactionCacheGeneration = -1;
        private static readonly Dictionary<int, bool> wdFactionManagedCache = new Dictionary<int, bool>(64);

        /// <summary>Some game versions store a quest link on parms for incidents spawned from quest script (QuestPart_Incident).</summary>
        private static bool IncidentParmsQuestReferenceNonNull(IncidentParms parms)
        {
            if (!cachedIncidentParmsQuestFieldResolved)
            {
                cachedIncidentParmsQuestField = typeof(IncidentParms).GetField("quest", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                cachedIncidentParmsQuestFieldResolved = true;
            }
            if (cachedIncidentParmsQuestField == null) return false;
            try
            {
                return cachedIncidentParmsQuestField.GetValue(parms) != null;
            }
            catch
            {
                return false;
            }
        }

        private static string RaidLogDetails(IncidentParms parms)
        {
            if (parms == null) return " parms=null";
            string faction = parms.faction?.Name ?? "(null faction)";
            string target = parms.target?.ToStringSafe() ?? "(null target)";
            string quest = IncidentParmsQuestReferenceNonNull(parms) ? "yes" : "no";
            return $" faction={faction} target={target} forced={parms.forced} quest={quest} points={parms.points:F0}";
        }

        private static void LogRaidDecision(string message, IncidentParms parms)
        {
            if (!Prefs.DevMode) return;
            Log.Message("[TSA WD] " + message + RaidLogDetails(parms));
        }

        /// <summary>
        /// WD-managed factions have at least one active surface world object participating in the WD simulation.
        /// Factions without physical WD holdings (for example mechanoids in normal starts) should remain eligible for vanilla storyteller raids.
        /// Cached per <see cref="ReinforcementNeighborCache"/> generation.
        /// </summary>
        private static bool IsFactionManagedByWorldDomination(Faction faction)
        {
            if (faction == null || faction.def == null || faction.def.hidden) return false;
            if (WorldActions_Utils.IsExcludedFaction(faction)) return false;

            int gen = ReinforcementNeighborCache.Generation;
            if (wdFactionCacheGeneration != gen)
            {
                wdFactionManagedCache.Clear();
                wdFactionCacheGeneration = gen;
            }

            int key = faction.loadID;
            if (wdFactionManagedCache.TryGetValue(key, out bool cached))
                return cached;

            bool managed = ScanFactionManagedByWorldDomination(faction);
            wdFactionManagedCache[key] = managed;
            return managed;
        }

        private static bool ScanFactionManagedByWorldDomination(Faction faction)
        {
            var all = Find.WorldObjects?.AllWorldObjects;
            if (all == null) return false;
            for (int i = 0; i < all.Count; i++)
            {
                WorldObject obj = all[i];
                if (obj?.Faction != faction) continue;
                if (!WorldActions_Utils.IsWdSurfaceWorldObject(obj)) continue;

                var comp = obj.GetComponent<CompViralSpread>();
                if (comp != null && comp.subType != "Excluded")
                    return true;
            }
            return false;
        }

        [HarmonyPrefix]
        public static bool Prefix(IncidentParms parms)
        {
            if (parms == null)
            {
                LogRaidDecision("Noticed raid attempt with null parms. Left unchanged by WD", parms);
                return true;
            }

            if (Raid_OnPlayerColony.IsWorldDominationRaid)
            {
                LogRaidDecision("Noticed World Domination raid attempt. Left unchanged by WD", parms);
                return true;
            }

            // Quest/scripted/dev fires are not random storyteller picks and must remain untouched.
            if (IncidentParmsQuestReferenceNonNull(parms))
            {
                LogRaidDecision("Noticed quest related raid attempt. Left unchanged by WD", parms);
                return true;
            }

            if (parms.forced)
            {
                LogRaidDecision("Noticed scripted raid attempt. Left unchanged by WD", parms);
                return true;
            }

            if (WorldDominationMod.settings.blockStorytellerRaidsOnlyWD &&
                !Raid_OnPlayerColony.IsCaravanClashInterception)
            {
                if (parms.faction == null)
                {
                    LogRaidDecision("Noticed Storyteller raid attempt. Blocked by WD setting: no faction set", parms);
                    return false;
                }

                if (parms.faction.def.hidden)
                {
                    LogRaidDecision("Noticed Storyteller raid attempt from hidden faction. Left unchanged by WD", parms);
                    return true;
                }

                if (WorldDominationMod.settings.allowStorytellerRaidsFromNonWdFactions &&
                    !IsFactionManagedByWorldDomination(parms.faction))
                {
                    LogRaidDecision("Noticed Storyteller raid attempt. Left unchanged by WD: non-WD faction allowed", parms);
                    return true;
                }

                LogRaidDecision("Noticed Storyteller raid attempt. Blocked by WD setting", parms);
                return false;
            }

            if (Raid_OnPlayerColony.IsCaravanClashInterception)
            {
                LogRaidDecision("Noticed caravan interception raid attempt. Left unchanged by WD", parms);
                return true;
            }

            if (parms.faction == null || parms.faction.def.hidden)
            {
                LogRaidDecision("Noticed Storyteller raid attempt. Left unchanged by WD: no resolved blockable faction", parms);
                return true;
            }

            // Random storyteller raid: do not modify parms.points (no WD scaling).
            LogRaidDecision("Noticed Storyteller raid attempt. Left unchanged by WD: storyteller raid blocking disabled", parms);
            return true;
        }
    }
    /// <summary>Dev console trace for WD raids: show chosen strategy and arrival mode after vanilla raid execution path.</summary>
    [HarmonyPatch(typeof(IncidentWorker_RaidEnemy), "TryExecuteWorker")]
    public static class Patch_RaidEnemy_LogWdRaidChoice
    {
        [HarmonyPostfix]
        public static void Postfix(bool __result, IncidentParms parms)
        {
            if (!Raid_OnPlayerColony.IsWorldDominationRaid || !Prefs.DevMode) return;
            string strategy = parms?.raidStrategy?.defName ?? "(null)";
            string arrival = parms?.raidArrivalMode?.defName ?? "(null)";
            string faction = parms?.faction?.Name ?? "?";
            string target = parms?.target?.ToStringSafe() ?? "?";
            string age = parms?.raidAgeRestriction?.defName ?? "(none)";
            string spawn = parms != null && parms.spawnCenter.IsValid ? parms.spawnCenter.ToString() : "(invalid)";
            Log.Message("[TSA WD] WD raid TryExecuteWorker result=" + (__result ? "success" : "failed")
                + " strategy=" + strategy
                + " arrival=" + arrival
                + " faction=" + faction
                + " target=" + target
                + " points=" + (parms?.points ?? 0f).ToString("F0")
                + " spawnCenter=" + spawn
                + " ageRestriction=" + age
                + " pawnCount=" + (parms?.pawnCount ?? 0));
        }
    }
}
