using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public enum SpecialWorldEventKind
    {
        StrongFactionWar,
        Revolt,
        ForwardAssault,
    }

    /// <summary>Independent or shared cooldowns for strong-faction war, revolt, and forward assault.</summary>
    public static class WorldActions_SpecialEventCooldown
    {
        public static bool IsOnCooldown(WorldComponent_SpreadManager manager, SpecialWorldEventKind kind)
        {
            if (manager == null) return false;
            int now = Find.TickManager.TicksGame;
            var seth = WorldDominationMod.settings;
            if (seth != null && seth.useSharedSpecialEventCooldown)
                return now < manager.specialWorldEventCooldownTick;

            return kind switch
            {
                SpecialWorldEventKind.StrongFactionWar => now < manager.strongFactionWarCooldownTick,
                SpecialWorldEventKind.Revolt => now < manager.revoltCooldownTick,
                SpecialWorldEventKind.ForwardAssault => now < manager.forwardAssaultCooldownTick,
                _ => false,
            };
        }

        public static void Stamp(WorldComponent_SpreadManager manager, SpecialWorldEventKind kind)
        {
            if (manager == null) return;
            var seth = WorldDominationMod.settings;
            if (seth == null) return;

            int now = Find.TickManager.TicksGame;
            if (seth.useSharedSpecialEventCooldown)
            {
                float days = Mathf.Max(0.5f, seth.sharedSpecialEventCooldownDays);
                manager.specialWorldEventCooldownTick = now + CompViralSpread.CooldownTicksFromDays(days);
                WDVerbose.Msg($"SpecialEventCD shared stamp kind={kind} days={days:F1} until={manager.specialWorldEventCooldownTick}");
                return;
            }

            float d = kind switch
            {
                SpecialWorldEventKind.StrongFactionWar => seth.strongFactionWarCooldownDays,
                SpecialWorldEventKind.Revolt => seth.revoltCooldownDays,
                SpecialWorldEventKind.ForwardAssault => seth.forwardAssaultCooldownDays,
                _ => 15f,
            };
            d = Mathf.Max(0.5f, d);
            int until = now + CompViralSpread.CooldownTicksFromDays(d);
            switch (kind)
            {
                case SpecialWorldEventKind.StrongFactionWar:
                    manager.strongFactionWarCooldownTick = until;
                    break;
                case SpecialWorldEventKind.Revolt:
                    manager.revoltCooldownTick = until;
                    break;
                case SpecialWorldEventKind.ForwardAssault:
                    manager.forwardAssaultCooldownTick = until;
                    break;
            }
            WDVerbose.Msg($"SpecialEventCD stamp kind={kind} days={d:F1} until={until}");
        }
    }
}
