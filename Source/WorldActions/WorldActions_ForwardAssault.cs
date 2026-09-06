using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Daily Vanguard / Invasion forward-assault orchestrator (shared CD / chance / pick).
    /// Vanguard packs and mass-relocates; Invasion launches coordinated raids with homes staying.
    /// Implementation: <see cref="WorldActions_Vanguard"/>, <see cref="WorldActions_Invasion"/>,
    /// <see cref="WorldActions_PackUp"/>. Scribed settings remain <c>forwardAssault*</c>.
    /// </summary>
    public static class WorldActions_ForwardAssault
    {
        public static void TryTrigger(WorldComponent_SpreadManager manager, DailyWorldSnapshot snapshot = null)
        {
            var seth = WorldDominationMod.settings;
            if (seth == null || manager == null) return;

            bool vanguardGateOk = WdEscalation.PassesGate(seth.gateThreatVanguard, manager);
            bool invasionGateOk = WdEscalation.PassesGate(seth.gateThreatInvasion, manager);
            if (!vanguardGateOk && !invasionGateOk)
            {
                WDVerbose.Msg(
                    $"ForwardAssault skipped reason=stage-gate vanguard={seth.gateThreatVanguard} invasion={seth.gateThreatInvasion}");
                return;
            }

            if (WorldActions_SpecialEventCooldown.IsOnCooldown(manager, SpecialWorldEventKind.ForwardAssault))
            {
                WDVerbose.Msg("ForwardAssault skipped reason=cooldown");
                return;
            }

            if (Rand.Value > Mathf.Clamp01(seth.forwardAssaultChance))
            {
                WDVerbose.Msg("ForwardAssault skipped reason=chance");
                return;
            }

            bool tryVanguard;
            if (vanguardGateOk && invasionGateOk)
                tryVanguard = Rand.Value <= Mathf.Clamp01(seth.vanguardVsInvasionPickChance);
            else
                tryVanguard = vanguardGateOk;

            if (tryVanguard)
            {
                WDVerbose.Msg("ForwardAssault TryVanguard");
                if (WorldActions_Vanguard.TryLaunch(manager, snapshot, seth))
                    return;

                if (seth.fallBackToInvasionIfVanguardClusterFails && invasionGateOk)
                {
                    WDVerbose.Msg("ForwardAssault Vanguard failed, fallback Invasion");
                    WorldActions_Invasion.TryLaunch(manager, snapshot, seth);
                }
                return;
            }

            WDVerbose.Msg("ForwardAssault TryInvasion");
            WorldActions_Invasion.TryLaunch(manager, snapshot, seth);
        }
    }
}
