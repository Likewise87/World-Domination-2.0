using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    public enum RaidContribRole { AttackerPrimary, AttackerAlly, DefenderPrimary, DefenderAlly }

    /// <summary>One participant's structured contribution to a raid side. Single source of truth shared by preview, launch, and arrival.</summary>
    public class RaidContribEntry
    {
        public WorldObject obj;
        public RaidContribRole role;
        /// <summary>Amount this participant contributes to its side total (defender primary = full local defense; everyone else = available raid strength).</summary>
        public float committed;
        /// <summary>Offensive strength at snapshot time (display / "of current strength").</summary>
        public float currentOffensive;
        /// <summary>Offensive + defensive at snapshot time (the defender primary fights with everything).</summary>
        public float totalLocalDefense;
        public bool hitGarrisonCap;

        public bool IsPrimary => role == RaidContribRole.AttackerPrimary || role == RaidContribRole.DefenderPrimary;
    }

    /// <summary>Structured defender side of a raid. Total never double-counts the primary target.</summary>
    public class RaidDefenderSnapshot
    {
        public WorldObject target;
        public RaidContribEntry primary;
        public readonly List<RaidContribEntry> allies = new List<RaidContribEntry>();

        /// <summary>target.GetTotalLocalDefensePower() + sum(ally committed). The primary is counted exactly once.</summary>
        public float Total;

        /// <summary>Factions that actually contribute to the in-range defense (target faction + in-range ally factions). Used to gate attacker allies.</summary>
        public List<Faction> CoalitionFactions()
        {
            var list = new List<Faction>();
            if (target?.Faction != null) list.Add(target.Faction);
            foreach (var a in allies)
            {
                var f = a.obj?.Faction;
                if (f != null && !list.Contains(f)) list.Add(f);
            }
            return list;
        }

        /// <summary>Builds display|tooltip detail lines: [0] = target, then one per ally. Same format consumed everywhere by RaidUIUtils.DrawDetailScroll.</summary>
        public List<string> BuildDetails(WorldDominationSettings seth)
        {
            char delim = Raid_ReinforcementLogic.DetailTooltipDelimiter;
            var lines = new List<string>
            {
                target.LabelCap + " (" + "TSA_WD_Target".Translate() + "): "
                    + "TSA_WD_ContribStrength".Translate(primary.totalLocalDefense.ToString("F0"))
            };
            foreach (var a in allies)
            {
                string display = a.obj.LabelCap + ": " + "TSA_WD_ContribStrength".Translate(a.committed.ToString("F0"));
                float retainFloor = WorldActions_Utils.GetGarrisonRetainFloor(a.obj?.GetComponent<CompViralSpread>(), seth);
                string tip = Raid_ReinforcementLogic.BuildContribTooltip(a.committed, a.currentOffensive, a.hitGarrisonCap, retainFloor);
                lines.Add(display + delim + tip);
            }
            return lines;
        }

        /// <summary>Icon-row breakdown parallel to <see cref="BuildDetails"/> for action-log Details.</summary>
        public List<RaidForceRow> BuildForceRows(WorldDominationSettings seth)
        {
            var rows = new List<RaidForceRow>();
            if (primary != null)
            {
                RaidForceRow primaryRow = RaidForceRow.FromDefenderEntry(primary, seth);
                if (primaryRow != null) rows.Add(primaryRow);
            }
            for (int i = 0; i < allies.Count; i++)
            {
                RaidForceRow allyRow = RaidForceRow.FromDefenderEntry(allies[i], seth);
                if (allyRow != null) rows.Add(allyRow);
            }
            return rows;
        }
    }

    /// <summary>
    /// Shared virtual-raid math. Resolves both coalitions with one consistent rule so preview, launch, and arrival cannot diverge:
    /// 1) Defenders = allies of the target hostile to the attacker (in defender radius). Depends only on the primary attacker (acyclic).
    /// 2) Attacker allies = allies of the attacker hostile to EVERY faction in the actual in-range defender coalition (in attacker radius).
    /// </summary>
    public static class Raid_MathSnapshot
    {
        /// <summary>Defender side for an (attacker -> target) raid. Pass the attacker world object so cross-faction allies are eligible.</summary>
        public static RaidDefenderSnapshot BuildDefenders(
            WorldObject target,
            WorldObject attacker,
            Faction attackerFaction,
            Dictionary<Faction, List<WorldObject>> lookup,
            WorldComponent_SpreadManager manager,
            WorldDominationSettings seth)
        {
            var snap = new RaidDefenderSnapshot { target = target };
            var targetComp = target?.GetComponent<CompViralSpread>();

            snap.primary = new RaidContribEntry
            {
                obj = target,
                role = RaidContribRole.DefenderPrimary,
                currentOffensive = targetComp?.strength ?? 0f,
                totalLocalDefense = targetComp?.GetTotalLocalDefensePower() ?? 0f,
            };
            snap.primary.committed = snap.primary.totalLocalDefense;
            snap.Total = snap.primary.totalLocalDefense;

            var excluded = attackerFaction != null ? new List<Faction> { attackerFaction } : null;
            // GetReinforcements reuses an internal scratch list; snapshot immediately.
            var raw = Raid_ReinforcementLogic.GetReinforcements(target, attacker, AllyRadiusUtil.GetEffective(target, seth, manager), lookup, manager, excluded);
            var defAllies = new List<WorldObject>(raw);

            foreach (var wo in defAllies)
            {
                var c = wo.GetComponent<CompViralSpread>();
                if (c == null) continue;
                float available = WorldActions_Utils.GetAvailableRaidStrength(c, seth);
                var entry = new RaidContribEntry
                {
                    obj = wo,
                    role = RaidContribRole.DefenderAlly,
                    committed = available,
                    currentOffensive = c.strength,
                    totalLocalDefense = c.GetTotalLocalDefensePower(),
                    hitGarrisonCap = Raid_ReinforcementLogic.HitMinGarrisonCap(c.strength, available, seth),
                };
                snap.allies.Add(entry);
                snap.Total += available;
            }
            return snap;
        }

        /// <summary>
        /// Attacker allies for an (attacker -> target) raid, gated by the resolved defender coalition (two-phase, acyclic).
        /// Returns a fresh list (already snapshotted off the internal scratch).
        /// </summary>
        public static List<WorldObject> GetAttackerAllies(
            WorldObject attacker,
            WorldObject target,
            List<Faction> defenderCoalitionFactions,
            Dictionary<Faction, List<WorldObject>> lookup,
            WorldComponent_SpreadManager manager,
            WorldDominationSettings seth)
        {
            var raw = Raid_ReinforcementLogic.GetReinforcements(attacker, target, AllyRadiusUtil.GetEffective(attacker, seth, manager), lookup, manager, defenderCoalitionFactions);
            return new List<WorldObject>(raw);
        }
    }
}
