using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Vanguard forward assault: strong far hostiles pack up and found a cluster near the player.</summary>
    public static class WorldActions_Vanguard
    {
        public static bool TryLaunch(WorldComponent_SpreadManager manager, DailyWorldSnapshot snapshot, WorldDominationSettings seth)
        {
            if (!WorldActions_PackUp.TryPickStrongHostileFaction(manager, snapshot, seth, out Faction faction, out List<Settlement> sources))
                return false;
            return LaunchCore(manager, seth, faction, sources);
        }

        /// <summary>Dev: force Vanguard pack-up (skips enable/chance/escalation/CD and settlement-count gates). Still needs a hostile with ≥1 site and a free cluster near the player.</summary>
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
                message = "Vanguard failed (no hostile WD settlement)";
                return false;
            }
            if (LaunchCore(manager, seth, faction, sources, forceDebug: true))
            {
                message = "Vanguard launched";
                return true;
            }

            message = "Vanguard failed (no player anchor or cluster reservation failed — check WDVerbose)";
            return false;
        }

        /// <summary>Dev: force a Vanguard pack-up seeded from a clicked NPC settlement (skips all gates including min settlement count).</summary>
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
            if (LaunchCore(manager, seth, seed.Faction, sources, forceDebug: true))
            {
                message = $"Vanguard launched for {seed.Faction.Name}";
                return true;
            }
            message = "Vanguard failed (no player anchor or cluster reservation failed — check WDVerbose)";
            return false;
        }

        private static bool LaunchCore(
            WorldComponent_SpreadManager manager,
            WorldDominationSettings seth,
            Faction faction,
            List<Settlement> sources,
            bool forceDebug = false)
        {
            if (faction == null || sources == null || sources.Count < 1) return false;

            Settlement playerAnchor = WorldActions_PackUp.FindNearestPlayerSettlement(sources[0].Tile.tileId, manager);
            if (playerAnchor == null)
            {
                WDVerbose.Msg("ForwardAssault Vanguard abort reason=no-player-anchor");
                return false;
            }

            // Live play packs 5–7; debug may launch fewer without the settlement-count gate.
            int need = forceDebug
                ? Mathf.Clamp(sources.Count, 1, WorldActions_PackUp.CountMax)
                : Mathf.Clamp(sources.Count, WorldActions_PackUp.CountMin, WorldActions_PackUp.CountMax);
            WDVerbose.Msg(
                $"ForwardAssault Vanguard cluster-seek need={need} anchor={playerAnchor.Label}@{playerAnchor.Tile.tileId} faction={faction.Name}");
            if (!WdSettlementClusterUtility.TryReserveClusterNear(
                    playerAnchor.Tile.tileId, need, faction, out List<int> tiles))
            {
                WDVerbose.Msg($"ForwardAssault Vanguard abort reason=cluster-fail need={need} near={playerAnchor.Label}");
                return false;
            }
            WDVerbose.Msg($"ForwardAssault Vanguard cluster reserved count={tiles.Count} tiles=[{string.Join(",", tiles)}]");

            int launched = 0;
            var lookTargets = new List<WorldObject>();
            for (int i = 0; i < sources.Count && i < tiles.Count; i++)
            {
                WorldObject_Traveler t = WorldActions_PackUp.LaunchMassRelocationTraveler(sources[i], tiles[i], manager);
                if (t == null) continue;
                launched++;
                lookTargets.Add(t);
            }

            if (launched < 1)
            {
                WDVerbose.Msg($"ForwardAssault Vanguard abort after zero launches");
                return false;
            }
            if (launched < WorldActions_PackUp.CountMin)
                WDVerbose.Msg($"ForwardAssault Vanguard partial launch={launched} (expected>={WorldActions_PackUp.CountMin})");

            WorldActions_SpecialEventCooldown.Stamp(manager, SpecialWorldEventKind.ForwardAssault);
            string msg = "TSA_WD_Log_ForwardAssault_Vanguard".Translate(faction.Name, launched);
            manager.AddLog(new SpreadLogEntry(msg, playerAnchor, null));
            WDVerbose.Msg($"ForwardAssault Vanguard launched faction={faction.Name} count={launched}");

            LookTargets letterLook = lookTargets.Count > 0
                ? new LookTargets(lookTargets)
                : new LookTargets(playerAnchor);
            Find.LetterStack.ReceiveLetter(
                "TSA_WD_Letter_Vanguard_Label".Translate(),
                "TSA_WD_Letter_Vanguard_Text".Translate(faction.Name.Colorize(Color.cyan), launched.ToString()),
                LetterDefOf.ThreatBig,
                letterLook);
            return true;
        }
    }
}
