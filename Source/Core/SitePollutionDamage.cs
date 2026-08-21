using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Daily average-pollution strength damage for NPC settlements and WD outposts.</summary>
    public static class SitePollutionDamage
    {
        private const float PollutionEpsilon = 0.0001f;
        private const float DeadOffenseEpsilon = 0.01f;

        private static readonly Queue<PlanetTile> OpenTiles = new Queue<PlanetTile>();
        private static readonly Dictionary<int, int> DistancesByTileId = new Dictionary<int, int>();
        private static readonly List<PlanetTile> NeighborTiles = new List<PlanetTile>();

        public static bool IsEligibleSite(WorldObject worldObject, CompViralSpread comp)
        {
            if (worldObject == null || comp == null) return false;
            if (PollutionImmunity.IsImmune(worldObject)) return false;
            if (comp.IsPlayerMapSettlement) return false;
            if (comp.IsOutpost) return true;
            return comp.IsSettlement && worldObject.Faction != null && !worldObject.Faction.IsPlayer;
        }

        /// <summary>Mean pollution 0..1 over all tiles in radius (clean tiles count as 0). Returns false if no tile is polluted.</summary>
        public static bool TryGetAveragePollution01(PlanetTile center, int radius, out float average01)
        {
            average01 = 0f;
            var grid = Find.WorldGrid;
            if (grid == null || !center.Valid) return false;

            int r = Mathf.Max(0, radius);
            float sum = 0f;
            int count = 0;
            bool anyPolluted = false;

            foreach (PlanetTile tile in EnumerateTilesInRadius(grid, center, r))
            {
                float p = Mathf.Clamp01(grid[tile].pollution);
                sum += p;
                count++;
                if (p > PollutionEpsilon)
                    anyPolluted = true;
            }

            if (!anyPolluted || count <= 0) return false;
            average01 = sum / count;
            return true;
        }

        /// <summary>Closest scrub-valid polluted tile in radius (home/center first when polluted), or -1.</summary>
        public static int FindClosestPollutedWorkTile(PlanetTile center, int radius)
        {
            var grid = Find.WorldGrid;
            if (grid == null || !center.Valid) return -1;

            int r = Mathf.Max(0, radius);
            int bestTile = -1;
            int bestDist = int.MaxValue;

            foreach (PlanetTile tile in EnumerateTilesInRadius(grid, center, r))
            {
                int id = tile.tileId;
                if (!WorldActions_Decontamination.IsValidWorkTile(id)) continue;
                if (!DistancesByTileId.TryGetValue(id, out int dist))
                    dist = 0;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestTile = id;
                }
            }

            return bestTile;
        }

        public static void TryApplyDaily(CompViralSpread comp)
        {
            if (comp?.parent == null || comp.parent.Destroyed) return;
            var s = WorldDominationMod.settings;
            if (s == null || !s.travelerPollutionDamageEnabled) return;
            if (!IsEligibleSite(comp.parent, comp)) return;

            // Outpost already at ~0 offense: only wipe if pollution is actually present.
            // Otherwise log diagnostics and leave it (regen / other systems may recover).
            // Do NOT attribute road/incident/raid zeroing to pollution.
            if (comp.IsOutpost && comp.offensiveStrength <= DeadOffenseEpsilon)
            {
                HandleOutpostAlreadyAtZeroOffense(comp, s);
                return;
            }

            int radius = s.pollutionDamageRadius;
            if (!TryGetAveragePollution01(comp.parent.Tile, radius, out float avg))
                return;

            float damage = s.GetPollutionExitDamage(avg);
            if (damage <= 0f) return;

            float half = damage * 0.5f;
            float offenseLoss = damage - half;
            float defenseLoss = half;

            float offBefore = comp.offensiveStrength;
            float defBefore = comp.defensiveStrength;
            ApplyAbsoluteDamage(comp, offenseLoss, defenseLoss);

            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            string text = "TSA_WD_Log_Pollution_DamagedSite".Translate(
                comp.parent.LabelCap,
                damage.ToString("F0"),
                Mathf.RoundToInt(avg * 100f));
            manager?.AddLog(new SpreadLogEntry(text, comp.parent));
            Log.Message(
                $"[WD] Site pollution dmg site={comp.parent.LabelCap} dmg={damage:F0} avgP={avg:F2} r={radius} " +
                $"off {offBefore:F1}->{comp.offensiveStrength:F1} def {defBefore:F1}->{comp.defensiveStrength:F1} " +
                FormatOutpostContext(comp));

            // Outpost "strength" is offensive; at 0 the site is dead (same as traveler wipe).
            // Do not wait on residual defensiveStrength or next-day regen will revive it.
            if (comp.IsOutpost && comp.offensiveStrength <= DeadOffenseEpsilon)
            {
                DestroyOutpostFromPollution(comp, manager, "pollution_damage", avg, radius);
                return;
            }

            if (comp.IsOutpost
                && comp.parent.Faction != null
                && comp.parent.Faction.IsPlayer
                && s.notifyOutpostPollutionDamage)
            {
                Messages.Message(
                    "TSA_WD_Message_OutpostPollutionDamage".Translate(
                        comp.parent.LabelCap,
                        damage.ToString("F0")),
                    comp.parent,
                    MessageTypeDefOf.NegativeEvent);
            }
        }

        /// <summary>
        /// Called when daily pollution tick finds an outpost already at ~0 offense.
        /// Wipes only if local pollution exists; otherwise logs and leaves the outpost alone.
        /// </summary>
        private static void HandleOutpostAlreadyAtZeroOffense(CompViralSpread comp, WorldDominationSettings s)
        {
            int radius = s.pollutionDamageRadius;
            bool hasPollution = TryGetAveragePollution01(comp.parent.Tile, radius, out float avg);
            string ctx = FormatOutpostContext(comp);

            if (!hasPollution)
            {
                Log.Warning(
                    $"[WD] Outpost offense~0 on pollution tick but no local pollution — not wiping " +
                    $"(likely incident/raid/road/other). {ctx} r={radius}");
                return;
            }

            Log.Message(
                $"[WD] Outpost offense~0 on pollution tick with local pollution — wiping husk. " +
                $"{ctx} avgP={avg:F2} r={radius}");
            DestroyOutpostFromPollution(
                comp,
                Find.World?.GetComponent<WorldComponent_SpreadManager>(),
                "husk_zero_offense",
                avg,
                radius);
        }

        private static void DestroyOutpostFromPollution(
            CompViralSpread comp,
            WorldComponent_SpreadManager manager,
            string wipeReason,
            float avgPollution01,
            int radius)
        {
            WorldObject site = comp?.parent;
            if (site == null || site.Destroyed) return;
            if (site is not WorldObject_WD_Outpost outpost) return;

            string label = outpost.LabelCap;
            PlanetTile tile = outpost.Tile;
            Faction outpostFaction = outpost.Faction;
            string ctx = FormatOutpostContext(comp);

            string destroyText = "TSA_WD_Log_Pollution_DestroyedSite".Translate(label);
            manager?.AddLog(new SpreadLogEntry(destroyText, outpost));
            // Always log wipe details, even when player notifications are off.
            Log.Message(
                $"[WD] Pollution destroyed outpost={label} tile={tile.tileId} reason={wipeReason} " +
                $"avgP={avgPollution01:F2} r={radius} {ctx}");

            var s = WorldDominationMod.settings;
            if (outpost.Faction != null
                && outpost.Faction.IsPlayer
                && (s?.notifyOutpostPollutionDamage ?? true))
            {
                Find.LetterStack.ReceiveLetter(
                    "TSA_WD_Letter_OutpostDestroyedByPollution_Label".Translate(),
                    "TSA_WD_Letter_OutpostDestroyedByPollution_Text".Translate(label),
                    LetterDefOf.NegativeEvent,
                    new GlobalTargetInfo(tile));
            }

            outpost.Destroy();
            WorldObject_WdSettlementRuin.Spawn(tile.tileId, label, outpostFaction);
        }

        private static string FormatOutpostContext(CompViralSpread comp)
        {
            if (comp?.parent == null) return "site=null";
            WorldObject site = comp.parent;
            int occupants = 0;
            int prisoners = 0;
            bool manualDefense = false;
            if (site is WorldObject_WD_Outpost outpost)
            {
                occupants = outpost.Occupants?.Count ?? 0;
                prisoners = outpost.Prisoners?.Count ?? 0;
                manualDefense = outpost.ManualDefenseActive;
            }

            float regenTarget = comp.IsOutpost ? comp.GetTargetOutpostStrength() : 0f;
            bool roadBuilder = WorldActions_Roads.HasActiveRoadBuilderFrom(site);
            bool decontam = WorldActions_Decontamination.HasActiveDecontaminationProject(comp);

            return
                $"def={site.def?.defName} off={comp.offensiveStrength:F1} defStr={comp.defensiveStrength:F1} " +
                $"regenTarget={regenTarget:F1} occupants={occupants} prisoners={prisoners} " +
                $"manualDefense={manualDefense} roadBuilder={roadBuilder} decontamProject={decontam}";
        }

        private static void ApplyAbsoluteDamage(CompViralSpread comp, float offenseLoss, float defenseLoss)
        {
            if (offenseLoss > 0f)
                comp.ReduceOffensiveByAmount(offenseLoss, allowDemotion: false);
            if (defenseLoss > 0f && !comp.IsPlayerMapSettlement)
                comp.defensiveStrength = Mathf.Max(0f, comp.defensiveStrength - defenseLoss);
        }

        private static IEnumerable<PlanetTile> EnumerateTilesInRadius(WorldGrid grid, PlanetTile root, int radius)
        {
            OpenTiles.Clear();
            DistancesByTileId.Clear();

            OpenTiles.Enqueue(root);
            DistancesByTileId[root.tileId] = 0;

            while (OpenTiles.Count > 0)
            {
                PlanetTile tile = OpenTiles.Dequeue();
                int distance = DistancesByTileId[tile.tileId];
                yield return tile;

                if (distance >= radius) continue;

                NeighborTiles.Clear();
                grid.GetTileNeighbors(tile, NeighborTiles);
                for (int i = 0; i < NeighborTiles.Count; i++)
                {
                    PlanetTile neighbor = NeighborTiles[i];
                    if (!neighbor.Valid || DistancesByTileId.ContainsKey(neighbor.tileId)) continue;
                    DistancesByTileId[neighbor.tileId] = distance + 1;
                    OpenTiles.Enqueue(neighbor);
                }
            }
        }
    }
}
