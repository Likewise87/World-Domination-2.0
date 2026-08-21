using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using Verse;
using UnityEngine;

namespace TSA_WorldDomination
{
    public static class Raid_ReinforcementLogic
    {
        /// <summary>Threat-distance multipliers were removed; reinforcements use full available strength within radius.</summary>
        public static float GetDistanceMultiplier(float distance, float maxRange, WorldDominationSettings seth) => 1f;

        // --- Helper for Defender Retrieval (Used at Arrival) ---
        /// <summary>Uses a fresh world lookup. For hot paths (e.g. raid arrival), prefer the overload that accepts a prebuilt lookup.</summary>
        public static List<WorldObject> GetDefenders(WorldObject target)
        {
            var seth = WorldDominationMod.settings;
            var manager = Find.World.GetComponent<WorldComponent_SpreadManager>();
            var objectsWithComp = WorldActions_Utils.GetWorldObjectsWithCompByFaction();
            return GetDefenders(target, objectsWithComp, manager);
        }

        /// <summary>Same as GetDefenders(target) but uses the provided lookup to avoid a full world scan per call.</summary>
        public static List<WorldObject> GetDefenders(WorldObject target, Dictionary<Faction, List<WorldObject>> objectsWithComp, WorldComponent_SpreadManager manager)
        {
            var seth = WorldDominationMod.settings;
            if (seth == null) return new List<WorldObject>();
            return GetReinforcements(target, null, AllyRadiusUtil.GetEffective(target, seth, manager), objectsWithComp, manager);
        }

        private static readonly List<WorldObject> reinfScratch = new List<WorldObject>();

        public static List<WorldObject> GetReinforcements(WorldObject primary, WorldObject enemy, float radius, Dictionary<Faction, List<WorldObject>> lookup, WorldComponent_SpreadManager manager, List<Faction> excludedFactions = null)
        {
            List<WorldObject> allies = reinfScratch;
            ReinforcementNeighborCache.FillNeighbors(primary, enemy, radius, lookup, manager, excludedFactions, allies);
            return allies;
        }

        /// <summary>Live scan into <paramref name="into"/> (no cache). Used by cache rebuild and excluded-faction paths.</summary>
        internal static void ScanReinforcementsLive(
            WorldObject primary,
            WorldObject enemy,
            float radius,
            Dictionary<Faction, List<WorldObject>> lookup,
            WorldComponent_SpreadManager manager,
            List<Faction> excludedFactions,
            List<WorldObject> into)
        {
            into.Clear();
            if (primary == null) return;

            foreach (var f in Find.FactionManager.AllFactionsListForReading)
            {
                if (f == null || f.defeated || f.def.hidden) continue;

                if (excludedFactions != null && excludedFactions.Count > 0)
                {
                    bool isHostileToAllOpponents = true;
                    foreach (var opponentFaction in excludedFactions)
                    {
                        if (f == opponentFaction || !WorldActions_Utils.SafeHostileTo(f, opponentFaction))
                        {
                            isHostileToAllOpponents = false;
                            break;
                        }
                    }
                    if (!isHostileToAllOpponents) continue;
                }

                bool isPrimaryFaction = f == primary.Faction;
                bool isValidAllyFaction = false;

                if (!isPrimaryFaction && primary.Faction != null && enemy != null && enemy.Faction != null && f != enemy.Faction)
                {
                    isValidAllyFaction = WorldActions_Utils.SafeRelationKindWith(f, primary.Faction) == FactionRelationKind.Ally;
                }

                if (isPrimaryFaction || isValidAllyFaction)
                {
                    foreach (var s in WorldActions_Utils.GetFactionObjects(lookup, f))
                    {
                        if (s == primary || s == enemy) continue;
                        if (!PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(s.Tile)) continue;

                        if (s.Faction != null && s.Faction.IsPlayer && s is Settlement playerS && playerS.HasMap)
                            continue;

                        if (WorldActions_Utils.GetDistance(primary.Tile, s.Tile, manager) <= radius)
                            into.Add(s);
                    }
                }
            }
        }

        /// <summary>Live scan of neighbor world-object IDs only (for cache storage).</summary>
        internal static void ScanReinforcementIdsLive(
            WorldObject primary,
            WorldObject enemy,
            float radius,
            Dictionary<Faction, List<WorldObject>> lookup,
            WorldComponent_SpreadManager manager,
            List<int> intoIds)
        {
            intoIds.Clear();
            ScanReinforcementsLive(primary, enemy, radius, lookup, manager, null, idScanScratch);
            for (int i = 0; i < idScanScratch.Count; i++)
            {
                if (idScanScratch[i] != null)
                    intoIds.Add(idScanScratch[i].ID);
            }
        }

        private static readonly List<WorldObject> idScanScratch = new List<WorldObject>();

        /// <summary>Character used to separate display line from tooltip in detail strings. Tooltip is everything after first occurrence.</summary>
        public const char DetailTooltipDelimiter = '|';

        // --- Simplified Power Calculation (For Attacker "Gather at Hub" Heuristic) ---
        // Applies a flat multiplier (based on the primary attacker's travel) to the entire army.
        public static float CalculateTotalReinforcementPowerSimplified(List<WorldObject> allies, WorldObject primary, float flatMultiplier, out List<string> details)
        {
            var seth = WorldDominationMod.settings;
            details = new List<string>();

            var primaryComp = primary.GetComponent<CompViralSpread>();
            float primaryRaw = WorldActions_Utils.GetAvailableRaidStrength(primaryComp, seth);
            float primaryEff = primaryRaw * flatMultiplier;
            float primaryCurrent = primaryComp != null ? primaryComp.strength : 0f;
            float primaryRetain = WorldActions_Utils.GetGarrisonRetainFloor(primaryComp, seth);
            bool primaryGarrison = Raid_ReinforcementLogic.HitMinGarrisonCap(primaryCurrent, primaryRaw, seth);
            string primaryDisplay = primary.LabelCap + " (" + "TSA_WD_Primary".Translate() + "): " + "TSA_WD_ContribStrength".Translate(primaryEff.ToString("F0"));
            string primaryTip = BuildContribTooltip(primaryEff, primaryCurrent, primaryGarrison, primaryRetain);
            details.Add(primaryDisplay + DetailTooltipDelimiter + primaryTip);

            float totalReinforcementsEff = 0f;
            foreach (var a in allies)
            {
                var aComp = a.GetComponent<CompViralSpread>();
                float aStr = WorldActions_Utils.GetAvailableRaidStrength(aComp, seth);
                float aEff = aStr * flatMultiplier;
                float aCurrent = aComp != null ? aComp.strength : 0f;
                float aRetain = WorldActions_Utils.GetGarrisonRetainFloor(aComp, seth);
                bool aGarrison = Raid_ReinforcementLogic.HitMinGarrisonCap(aCurrent, aStr, seth);
                string aDisplay = a.LabelCap + " (" + "TSA_WD_Ally".Translate() + "): " + "TSA_WD_ContribStrength".Translate(aEff.ToString("F0"));
                string aTip = BuildContribTooltip(aEff, aCurrent, aGarrison, aRetain);
                totalReinforcementsEff += aEff;
                details.Add(aDisplay + DetailTooltipDelimiter + aTip);
            }

            return primaryEff + totalReinforcementsEff;
        }

        /// <summary>Builds tooltip with minus-prefixed list lines (primary attacker and allies). Target (defender) has no tooltip.</summary>
        public static string BuildContribTooltip(float contributed, float currentStrength, bool hitGarrisonCap, float retainFloor)
        {
            const string rowPrefix = "− ";
            var parts = new List<string>();
            parts.Add(rowPrefix + "TSA_WD_ContribStrength".Translate(contributed.ToString("F0")));
            parts.Add(rowPrefix + "TSA_WD_OfCurrentStrength".Translate(currentStrength.ToString("F0")));
            if (hitGarrisonCap)
            {
                string capText = "TSA_WD_MinGarrisonCap".Translate(retainFloor.ToString("F0")).Colorize(Color.yellow);
                parts.Add(rowPrefix + capText);
            }
            return string.Join("\n", parts);
        }

        /// <summary>True when garrison retain floor left some strength at home (available &lt; current).</summary>
        public static bool HitMinGarrisonCap(float currentStrength, float available, WorldDominationSettings seth)
        {
            if (seth == null || currentStrength <= 0f) return false;
            return available + 0.01f < currentStrength;
        }

        // --- Standard Power Calculation (For Defenders / Localized Response) ---
        public static float CalculateTotalReinforcementPower(List<WorldObject> allies, int centerTile, float maxRadius, WorldDominationSettings seth, WorldComponent_SpreadManager manager, out List<string> details, WorldObject primary, string label)
        {
            details = new List<string>();

            var primaryComp = primary.GetComponent<CompViralSpread>();
            float primaryStr = WorldActions_Utils.GetAvailableRaidStrength(primaryComp, seth);
            string primaryDisplay = primary.LabelCap + " (" + label + "): " + "TSA_WD_ContribStrength".Translate(primaryStr.ToString("F0"));
            details.Add(primaryDisplay);

            float totalReinforcements = 0f;
            foreach (var a in allies)
            {
                var comp = a.GetComponent<CompViralSpread>();
                if (comp == null) continue;

                float availableStr = WorldActions_Utils.GetAvailableRaidStrength(comp, seth);
                float dist = WorldActions_Utils.GetDistance(centerTile, a.Tile, manager);
                float mult = GetDistanceMultiplier(dist, maxRadius, seth);
                float effectiveContrib = availableStr * mult;
                totalReinforcements += effectiveContrib;

                float currentStr = comp.strength;
                float retainFloor = WorldActions_Utils.GetGarrisonRetainFloor(comp, seth);
                bool garrison = Raid_ReinforcementLogic.HitMinGarrisonCap(currentStr, availableStr, seth);
                string aDisplay = a.LabelCap + ": " + "TSA_WD_ContribStrength".Translate(effectiveContrib.ToString("F0"));
                string aTip = BuildContribTooltip(effectiveContrib, currentStr, garrison, retainFloor);
                details.Add(aDisplay + DetailTooltipDelimiter + aTip);
            }
            return primaryStr + totalReinforcements;
        }

        // Helper overload for quick math without details
        public static float CalculateTotalReinforcementPower(List<WorldObject> allies, int centerTile, float maxRadius, WorldDominationSettings seth, WorldComponent_SpreadManager manager)
        {
            float total = 0f;
            foreach (var a in allies)
            {
                var comp = a.GetComponent<CompViralSpread>();
                if (comp == null) continue;

                // SURGICAL: Respect 50%/250 rule
                float availableStr = WorldActions_Utils.GetAvailableRaidStrength(comp, seth);

                float dist = WorldActions_Utils.GetDistance(centerTile, a.Tile, manager);
                total += availableStr * GetDistanceMultiplier(dist, maxRadius, seth);
            }
            return total;
        }
    }
}