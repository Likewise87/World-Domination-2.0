using HarmonyLib;
using RimWorld;

namespace TSA_WorldDomination
{
    [HarmonyPatch(typeof(IncidentWorker_TraderCaravanArrival), "TryExecuteWorker")]
    public static class Patch_StorytellerTraderBlock
    {
        /// <summary>
        /// Block random storyteller trader caravans when the setting is on. Do not block:
        /// allied traders requested via comms (<c>FactionDialogMaker.RequestTraderOption</c> sets <c>parms.forced</c>),
        /// or WD's own spawns (<see cref="Trader_OnPlayerColony.IsWorldDominationTrader"/>).
        /// </summary>
        [HarmonyPrefix]
        public static bool Prefix(IncidentParms parms)
        {
            if (WorldDominationMod.settings == null) return true;
            if (!WorldDominationMod.settings.blockStorytellerTradersOnlyWD) return true;
            if (parms != null && parms.forced) return true;
            return Trader_OnPlayerColony.IsWorldDominationTrader;
        }
    }
}
