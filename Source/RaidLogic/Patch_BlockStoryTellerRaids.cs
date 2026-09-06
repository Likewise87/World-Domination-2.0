using System.Reflection;
using HarmonyLib;
using Verse;
using RimWorld;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Blocks random storyteller <see cref="IncidentWorker_RaidEnemy"/> per Allegiances Storyteller column.
    /// Faction is often null until <c>TryResolveRaidFaction</c>; gate pre-set factions in Prefix and
    /// drop after resolve (no reroll) so blocked factions cannot slip through.
    /// Quest/scripted/WD/clash fires are exempt (<see cref="IncidentParms.forced"/>, quest field, flags).
    /// </summary>
    [HarmonyPatch(typeof(IncidentWorker_RaidEnemy), "TryExecuteWorker")]
    public static class Patch_RaidEnemy_AdjustPoints
    {
        private static FieldInfo cachedIncidentParmsQuestField;
        private static bool cachedIncidentParmsQuestFieldResolved;

        internal static bool IncidentParmsQuestReferenceNonNull(IncidentParms parms)
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

        /// <summary>WD / clash / quest / forced — not random storyteller picks.</summary>
        internal static bool IsExemptFromStorytellerRaidGate(IncidentParms parms)
        {
            if (parms == null) return true;
            if (Raid_OnPlayerColony.IsWorldDominationRaid) return true;
            if (Raid_OnPlayerColony.IsCaravanClashInterception) return true;
            if (IncidentParmsQuestReferenceNonNull(parms)) return true;
            if (parms.forced) return true;
            return false;
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

        /// <summary>Always-on diagnostic for Allegiances storyteller drops (not WDVerbose-gated).</summary>
        internal static void LogStorytellerRaidDropped(Faction faction)
        {
            string name = faction?.Name ?? faction?.def?.defName ?? "(unknown)";
            Log.Message("[TSA WD] Picked Faction for Storyteller Raid: " + name + ", Blocked by mod settings -> Raid dropped");
        }

        [HarmonyPrefix]
        public static bool Prefix(IncidentParms parms)
        {
            if (parms == null)
            {
                LogRaidDecision("Noticed raid attempt with null parms. Left unchanged by WD", parms);
                return true;
            }

            if (IsExemptFromStorytellerRaidGate(parms))
            {
                if (Raid_OnPlayerColony.IsWorldDominationRaid)
                    LogRaidDecision("Noticed World Domination raid attempt. Left unchanged by WD", parms);
                else if (Raid_OnPlayerColony.IsCaravanClashInterception)
                    LogRaidDecision("Noticed caravan interception raid attempt. Left unchanged by WD", parms);
                else if (IncidentParmsQuestReferenceNonNull(parms))
                    LogRaidDecision("Noticed quest related raid attempt. Left unchanged by WD", parms);
                else if (parms.forced)
                    LogRaidDecision("Noticed scripted raid attempt. Left unchanged by WD", parms);
                return true;
            }

            // Null faction: vanilla resolves inside TryExecuteWorker — gated in TryResolveRaidFaction Postfix.
            if (parms.faction == null)
            {
                LogRaidDecision("Noticed Storyteller raid attempt without faction. Deferred gate until resolve", parms);
                return true;
            }

            if (WorldActions_Utils.IsStorytellerRaidAllowed(parms.faction))
            {
                LogRaidDecision("Noticed Storyteller raid attempt. Left unchanged by WD: allowed for faction", parms);
                return true;
            }

            LogStorytellerRaidDropped(parms.faction);
            return false;
        }
    }

    /// <summary>
    /// After vanilla picks a raid faction, drop if Allegiances Storyteller blocks it (no reroll).
    /// </summary>
    [HarmonyPatch(typeof(IncidentWorker_RaidEnemy), "TryResolveRaidFaction")]
    public static class Patch_RaidEnemy_TryResolveRaidFaction_StorytellerGate
    {
        [HarmonyPostfix]
        public static void Postfix(IncidentParms parms, ref bool __result)
        {
            if (!__result || parms?.faction == null) return;
            if (Patch_RaidEnemy_AdjustPoints.IsExemptFromStorytellerRaidGate(parms)) return;
            if (WorldActions_Utils.IsStorytellerRaidAllowed(parms.faction)) return;

            Patch_RaidEnemy_AdjustPoints.LogStorytellerRaidDropped(parms.faction);
            parms.faction = null;
            __result = false;
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
