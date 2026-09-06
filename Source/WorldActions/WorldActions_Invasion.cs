using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Invasion forward assault: strong far hostiles launch 5–7 coordinated raids at player colonies/outposts.
    /// Homes stay; virtual allies reinforce each host. Pick gates align with Vanguard.
    /// </summary>
    public static class WorldActions_Invasion
    {
        public static bool TryLaunch(WorldComponent_SpreadManager manager, DailyWorldSnapshot snapshot, WorldDominationSettings seth)
        {
            if (!WorldActions_PackUp.TryPickStrongHostileFaction(manager, snapshot, seth, out Faction faction, out List<Settlement> sources))
                return false;
            return LaunchCore(manager, seth, faction, sources);
        }

        /// <summary>Dev: force Invasion coordinated raids at player targets (skips enable/chance/escalation/CD and settlement-count gates).</summary>
        public static bool DebugForce(out string message)
        {
            message = null;
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            var seth = WorldDominationMod.settings;
            if (manager == null || seth == null)
            {
                message = "no manager/settings";
                return false;
            }

            WorldActions_PackUp.ClearForwardAssaultCooldown(manager);
            DailyWorldSnapshot snapshot = DailyWorldSnapshot.Build();
            if (!WorldActions_PackUp.TryPickAnyHostileFactionForDebug(snapshot, out Faction faction, out List<Settlement> sources))
            {
                message = "Invasion failed (no hostile WD settlement)";
                return false;
            }
            if (LaunchCore(manager, seth, faction, sources))
            {
                message = "Invasion launched";
                return true;
            }

            message = "Invasion failed (no player targets — check WDVerbose)";
            return false;
        }

        /// <summary>Dev: force an Invasion seeded from a clicked NPC settlement (skips all gates including min settlement count).</summary>
        public static bool DebugForceFromSettlement(Settlement seed, out string message)
        {
            message = null;
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            var seth = WorldDominationMod.settings;
            if (manager == null || seth == null) { message = "no manager/settings"; return false; }
            if (seed == null || seed.Destroyed || seed.Faction == null || seed.Faction.IsPlayer || seed.Faction.defeated)
            {
                message = "click an NPC settlement";
                return false;
            }

            WorldActions_PackUp.ClearForwardAssaultCooldown(manager);
            DailyWorldSnapshot snapshot = DailyWorldSnapshot.Build();
            List<Settlement> sources = WorldActions_PackUp.BuildDebugForcedSources(seed.Faction, seed, snapshot);
            if (LaunchCore(manager, seth, seed.Faction, sources))
            {
                message = $"Invasion launched for {seed.Faction.Name}";
                return true;
            }
            message = "Invasion failed (no player targets — check WDVerbose)";
            return false;
        }

        private static bool LaunchCore(WorldComponent_SpreadManager manager, WorldDominationSettings seth, Faction faction, List<Settlement> sources)
        {
            if (faction == null || sources == null || sources.Count < 1) return false;

            List<WorldObject> targets = WorldActions_PackUp.CollectDistinctPlayerTargets();
            if (targets.Count == 0)
            {
                WDVerbose.Msg("ForwardAssault Invasion abort reason=no-player-targets");
                return false;
            }

            var assignments = WorldActions_PackUp.AssignEveryHostToPlayerTargets(sources, targets, manager, seth);
            if (assignments.Count < 1)
            {
                WDVerbose.Msg("ForwardAssault Invasion abort reason=no-assignments");
                return false;
            }

            var excludeHostIds = new HashSet<int>();
            for (int i = 0; i < sources.Count; i++)
            {
                Settlement s = sources[i];
                if (s != null && !s.Destroyed)
                    excludeHostIds.Add(s.ID);
            }

            int launched = 0;
            var lookTargets = new List<WorldObject>();
            for (int i = 0; i < assignments.Count; i++)
            {
                var a = assignments[i];
                if (!a.ratioOk)
                    WDVerbose.Msg($"ForwardAssault Invasion ratio-bypass src={a.src.Label} tgt={a.tgt.Label}");

                WorldObject_Traveler t = WorldActions_Raid.TryLaunchCoordinatedInvasionRaid(
                    a.src, a.tgt, manager, excludeHostIds);
                if (t == null) continue;
                launched++;
                lookTargets.Add(t);
                if (a.tgt != null && !a.tgt.Destroyed)
                    lookTargets.Add(a.tgt);
            }

            if (launched < 1)
            {
                WDVerbose.Msg("ForwardAssault Invasion abort reason=zero-launched");
                return false;
            }

            WorldActions_SpecialEventCooldown.Stamp(manager, SpecialWorldEventKind.ForwardAssault);
            string msg = "TSA_WD_Log_ForwardAssault_Invasion".Translate(faction.Name, launched);
            manager.AddLog(new SpreadLogEntry(msg, WorldActions_PackUp.FindNearestPlayerSettlement(-1, manager), null));
            WDVerbose.Msg(
                $"ForwardAssault Invasion launched faction={faction.Name} raids={launched} hosts={sources.Count}");

            LookTargets letterLook = lookTargets.Count > 0
                ? new LookTargets(lookTargets)
                : LookTargets.Invalid;
            Find.LetterStack.ReceiveLetter(
                "TSA_WD_Letter_Invasion_Label".Translate(),
                "TSA_WD_Letter_Invasion_Text".Translate(
                    faction.Name.Colorize(Color.cyan),
                    launched.ToString()),
                LetterDefOf.ThreatBig,
                letterLook);
            return true;
        }
    }
}
