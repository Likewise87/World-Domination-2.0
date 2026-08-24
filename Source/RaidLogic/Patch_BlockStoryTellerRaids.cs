using System.Reflection;
using HarmonyLib;
using Verse;
using RimWorld;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Blocks random storyteller raids per faction via Allegiances settings; does not modify storyteller raid points.
    /// Quest/scripted raids must not be blocked: vanilla uses <see cref="IncidentParms.forced"/> for many non-storyteller fires;
    /// Royalty+ may also set a <c>quest</c> field on <see cref="IncidentParms"/> (reflection, version-tolerant).
    /// </summary>
    [HarmonyPatch(typeof(IncidentWorker_RaidEnemy), "TryExecuteWorker")]
    public static class Patch_RaidEnemy_AdjustPoints
    {
        private static FieldInfo cachedIncidentParmsQuestField;
        private static bool cachedIncidentParmsQuestFieldResolved;

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

            if (Raid_OnPlayerColony.IsCaravanClashInterception)
            {
                LogRaidDecision("Noticed caravan interception raid attempt. Left unchanged by WD", parms);
                return true;
            }

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

            if (parms.faction == null)
            {
                LogRaidDecision("Noticed Storyteller raid attempt without faction. Left unchanged by WD", parms);
                return true;
            }

            if (WorldActions_Utils.IsStorytellerRaidAllowed(parms.faction))
            {
                LogRaidDecision("Noticed Storyteller raid attempt. Left unchanged by WD: allowed for faction", parms);
                return true;
            }

            LogRaidDecision("Noticed Storyteller raid attempt. Blocked by per-faction WD setting", parms);
            return false;
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
