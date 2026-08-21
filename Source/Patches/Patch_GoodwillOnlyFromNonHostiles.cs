using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// When the player conquers/destroys a settlement, vanilla gives goodwill to other factions that were hostile to the destroyed faction.
    /// We only block that specific goodwill gain for factions that are hostile to the player. Gifts, trade, and other sources are unaffected.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_GoodwillOnlyFromNonHostiles
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Faction), nameof(Faction.TryAffectGoodwillWith),
                new[] { typeof(Faction), typeof(int), typeof(bool), typeof(bool), typeof(HistoryEventDef), typeof(GlobalTargetInfo?) });
        }

        /// <summary>True when this goodwill change is from conquering/destroying a settlement (vanilla HistoryEventDef.DestroyedEnemyBase).</summary>
        static bool IsConquestGoodwillReason(HistoryEventDef reason)
        {
            return reason?.defName == "DestroyedEnemyBase";
        }

        [HarmonyPrefix]
        public static bool Prefix(Faction __instance, Faction other, int goodwillChange,
            bool canSendMessage, bool canSendHostilityLetter, HistoryEventDef reason, GlobalTargetInfo? lookTarget)
        {
            if (!WorldDominationMod.settings.noGoodwillFromHostilesOnConquest) return true;
            if (__instance != Faction.OfPlayer) return true;
            if (goodwillChange <= 0) return true;
            if (other == null || !WorldActions_Utils.SafeHostileTo(other, Faction.OfPlayer)) return true;
            if (!IsConquestGoodwillReason(reason)) return true;

            Log.Message($"[TSA World Domination] Suppressing conquest goodwill: +{goodwillChange} to \"{other.Name}\" (hostile). Reason: {reason.defName}");
            return false;
        }
    }
}
