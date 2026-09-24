using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;
using RimWorld;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Blocks or redirects random storyteller <see cref="IncidentWorker_RaidEnemy"/> per Allegiances Storyteller column.
    /// Faction is often null until <c>TryResolveRaidFaction</c>; gate pre-set factions in Prefix and
    /// after resolve in Postfix. Quest/scripted/WD/clash fires are exempt.
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

        internal static void LogStorytellerRaidSwapped(Faction from, Faction to)
        {
            string fromName = from?.Name ?? from?.def?.defName ?? "(unknown)";
            string toName = to?.Name ?? to?.def?.defName ?? "(unknown)";
            Log.Message("[TSA WD] Picked Faction for Storyteller Raid: " + fromName + ", Blocked by mod settings -> Swapped to " + toName);
        }

        /// <summary>
        /// When <paramref name="parms"/>.faction is blocked by Allegiances Storyteller:
        /// Drop (default) or Swap to a vanilla-eligible allowed faction. Returns false if the raid should abort.
        /// </summary>
        internal static bool TryResolveBlockedStorytellerFaction(IncidentParms parms)
        {
            if (parms?.faction == null) return true;
            if (WorldActions_Utils.IsStorytellerRaidAllowed(parms.faction)) return true;

            var seth = WorldDominationMod.settings;
            WdStorytellerBlockedRaidMode mode = seth?.storytellerBlockedRaidMode
                ?? WorldDominationSettings.DefStorytellerBlockedRaidMode;

            if (mode == WdStorytellerBlockedRaidMode.SwapToValid
                && TrySwapToAllowedStorytellerFaction(parms, out Faction swap))
            {
                Faction blocked = parms.faction;
                parms.faction = swap;
                LogStorytellerRaidSwapped(blocked, swap);
                return true;
            }

            LogStorytellerRaidDropped(parms.faction);
            parms.faction = null;
            return false;
        }

        private static bool TrySwapToAllowedStorytellerFaction(IncidentParms parms, out Faction swap)
        {
            swap = null;
            if (parms?.target is not Map)
                return false;

            if (IncidentDefOf.RaidEnemy?.Worker is not IncidentWorker_RaidEnemy worker)
                return false;

            Faction blocked = parms.faction;
            // Only factions Configure Scope can toggle (not hidden mechanoids/insects, etc.).
            bool Candidate(Faction f) =>
                f != null
                && f != blocked
                && !WorldActions_Utils.IsHardExcludedFromWd(f)
                && WorldActions_Utils.IsStorytellerRaidAllowed(f)
                && worker.FactionCanBeGroupSource(f, parms);

            if (PawnGroupMakerUtility.TryGetRandomFactionForCombatPawnGroupWeighted(
                    parms,
                    out swap,
                    Candidate,
                    allowNonHostileToPlayer: true,
                    allowHidden: false,
                    allowDefeated: true)
                && swap != null)
                return true;

            bool DesperateCandidate(Faction f) =>
                f != null
                && f != blocked
                && !WorldActions_Utils.IsHardExcludedFromWd(f)
                && WorldActions_Utils.IsStorytellerRaidAllowed(f)
                && worker.FactionCanBeGroupSource(f, parms, desperate: true);

            return PawnGroupMakerUtility.TryGetRandomFactionForCombatPawnGroupWeighted(
                       parms,
                       out swap,
                       DesperateCandidate,
                       allowNonHostileToPlayer: true,
                       allowHidden: false,
                       allowDefeated: true)
                   && swap != null;
        }

        public static string StorytellerBlockedRaidModeLabel(WdStorytellerBlockedRaidMode mode) => mode switch
        {
            WdStorytellerBlockedRaidMode.SwapToValid => "TSA_WD_StorytellerBlockedRaid_Swap".Translate().ToString(),
            _ => "TSA_WD_StorytellerBlockedRaid_Drop".Translate().ToString()
        };

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

            if (TryResolveBlockedStorytellerFaction(parms))
            {
                LogRaidDecision("Noticed Storyteller raid attempt. Swapped blocked faction; continuing", parms);
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// After vanilla picks a raid faction, drop or swap if Allegiances Storyteller blocks it.
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

            if (Patch_RaidEnemy_AdjustPoints.TryResolveBlockedStorytellerFaction(parms))
            {
                __result = true;
                return;
            }

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
