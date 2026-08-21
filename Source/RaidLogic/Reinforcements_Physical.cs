using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using UnityEngine;
using Verse.AI.Group;

namespace TSA_WorldDomination
{
    public class MapComponent_ReinforcementTimer : MapComponent
    {
        private int ticksUntilArrival = -1;
        private float raidPoints = 0f;
        private bool initialized = false;
        private ChoiceLetter countdownLetter;
        private Faction reinforcementFaction;
        private List<string> cachedMathLog = new List<string>();

        // We update the UI in 30-second steps (30s * 60 ticks = 1800)
        private const int UpdateStepTicks = 1800;

        public MapComponent_ReinforcementTimer(Map map) : base(map) { }

        public override void MapComponentTick()
        {
            if (ticksUntilArrival > 0)
            {
                ticksUntilArrival--;

                // SURGICAL CHANGE: Update UI only on 30-second boundaries
                if (ticksUntilArrival % UpdateStepTicks == 0 && ticksUntilArrival > 0)
                {
                    UpdateCountdownLetter();
                }

                if (ticksUntilArrival == 0)
                {
                    if (countdownLetter != null) Find.LetterStack.RemoveLetter(countdownLetter);
                    ExecuteRaidReinforcements();
                }
            }
        }

        public void InitializeReinforcements()
        {
            if (initialized) return;

            // SURGICAL ADDITION: Check for active quests on the settlement
            var settlement = map.info.parent as Settlement;
            if (settlement != null && WorldActions_Utils.HasActiveQuest(settlement))
            {
                Log.Message($"[WorldDomination] Reinforcements: Aborting for {settlement.LabelCap}. Active quest detected.");
                return;
            }

            initialized = true;

            var seth = WorldDominationMod.settings;
            var manager = Find.World.GetComponent<WorldComponent_SpreadManager>();
            if (manager == null)
            {
                Log.Warning("[WorldDomination] Reinforcements: WorldComponent_SpreadManager not found, skipping.");
                return;
            }
            Faction defenderFaction = map.ParentFaction;

            if (defenderFaction == null || defenderFaction.IsPlayer) return;

            var lookup = WorldActions_Utils.GetWorldObjectsWithCompByFaction();

            float maxWaitTicks = 14400f; // 4 Minutes ceiling
            float pointsFromStrengthMult = 0.5f;

            float allyRadius = AllyRadiusUtil.GetEffective(map.Parent, seth, manager);
            var rawAllies = Raid_ReinforcementLogic.GetReinforcements(map.Parent, null, allyRadius, lookup, manager);
            var hostileAllies = new List<WorldObject>();
            for (int i = 0; i < rawAllies.Count; i++)
            {
                var a = rawAllies[i];
                if (a != map.Parent && a.Tile != map.Parent.Tile && a.Faction != null && WorldActions_Utils.SafeHostileTo(a.Faction, Faction.OfPlayer))
                    hostileAllies.Add(a);
            }

            if (hostileAllies.Count == 0)
            {
                ticksUntilArrival = -1;
                return;
            }

            Dictionary<Faction, float> factionContributions = new Dictionary<Faction, float>();
            float totalAlliedStrength = 0f;
            float totalSecondsSaved = 0f;

            cachedMathLog.Clear();
            cachedMathLog.Add($"--- TSA-WD Reinforcement Math: {map.Parent.Label} ---");
            cachedMathLog.Add($"raidAllyRadius={allyRadius}, maxWaitTicks={maxWaitTicks}, hostileAllies count={hostileAllies.Count}");

            foreach (var ally in hostileAllies)
            {
                var comp = ally.GetComponent<CompViralSpread>();
                int distTiles = WorldActions_Utils.GetDistance(map.Tile, ally.Tile, manager);
                // Only count allies that are actually elsewhere (dist > 0); same-tile must not reduce arrival time
                if (distTiles <= 0)
                {
                    cachedMathLog.Add($"  SKIP {ally.LabelCap} (tile {ally.Tile}): same tile as target");
                    continue;
                }

                float dist = (float)distTiles;
                float distPct = allyRadius > 1e-6f ? Mathf.Clamp01(dist / allyRadius) : 1f;
                float strengthDiscount = 1.0f - distPct;
                float contrib = comp != null ? comp.strength * strengthDiscount : 0f;
                float secondsSaved = (1.0f - distPct) * 45f;

                totalAlliedStrength += contrib;
                totalSecondsSaved += secondsSaved;

                cachedMathLog.Add($"  {ally.LabelCap}: dist={distTiles} tiles, distPct={distPct:F2}, strengthContrib={contrib:F0}, secSaved={secondsSaved:F1}");

                if (!factionContributions.ContainsKey(ally.Faction)) factionContributions[ally.Faction] = 0f;
                factionContributions[ally.Faction] += contrib;
            }

            // If every ally was same-tile (skipped), no real reinforcements
            if (factionContributions.Count == 0)
            {
                ticksUntilArrival = -1;
                return;
            }

            if (hostileAllies.Any(a => a.Faction == defenderFaction))
                reinforcementFaction = defenderFaction;
            else
                reinforcementFaction = factionContributions.OrderByDescending(x => x.Value).First().Key;

            raidPoints = Mathf.Clamp(totalAlliedStrength * pointsFromStrengthMult, seth.minRaidPoints, seth.maxRaidPoints);

            // Calculate base arrival time: 4 min ceiling minus (seconds saved × 60), floor 45s
            float calculatedTicks = Mathf.Max(maxWaitTicks - (totalSecondsSaved * 60f), 2700f);

            // Snap initial ticks to the nearest 30s step so the first letter doesn't show "1.8m"
            ticksUntilArrival = Mathf.RoundToInt(calculatedTicks / UpdateStepTicks) * UpdateStepTicks;

            cachedMathLog.Add($"totalSecondsSaved={totalSecondsSaved:F1}, calculatedTicks={calculatedTicks:F0}, snapped ticksUntilArrival={ticksUntilArrival} ({ticksUntilArrival / 60f:F1}s)");

            UpdateCountdownLetter(isInitial: true);
            Log.Message(string.Join("\n", cachedMathLog));
        }

        private void UpdateCountdownLetter(bool isInitial = false)
        {
            if (reinforcementFaction == null) return;

            // SNAP LOGIC: Ensure timeString always shows clean intervals
            float totalSeconds = (float)ticksUntilArrival / 60f;

            // Format to 0 decimal places for seconds, or .5 increments for minutes
            string timeString;
            if (totalSeconds >= 60)
            {
                float minutes = totalSeconds / 60f;
                // Snap to .5 precision (e.g. 1.5m, 2.0m)
                timeString = (Mathf.Round(minutes * 2) / 2f).ToString("0.#") + "m";
            }
            else
            {
                timeString = $"{totalSeconds:F0}s";
            }

            string label = "TSA_WD_ReinforcementsIncoming_Label".Translate(timeString);
            string text = "TSA_WD_ReinforcementsIncoming_Text".Translate(reinforcementFaction.Name, timeString);

            if (isInitial || countdownLetter == null || !Find.LetterStack.LettersListForReading.Contains(countdownLetter))
            {
                if (countdownLetter != null) Find.LetterStack.RemoveLetter(countdownLetter);
                countdownLetter = LetterMaker.MakeLetter(label, text, LetterDefOf.NeutralEvent, new GlobalTargetInfo(map.Tile));
                Find.LetterStack.ReceiveLetter(countdownLetter, playSound: isInitial);
            }
            else
            {
                countdownLetter.Label = label;
                countdownLetter.Text = text;
            }
        }

        private void ExecuteRaidReinforcements()
        {
            if (reinforcementFaction == null) return;

            IncidentParms parms = new IncidentParms
            {
                target = map,
                points = raidPoints,
                faction = reinforcementFaction,
                raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn,
                raidStrategy = RaidStrategyDefOf.ImmediateAttack,
                forced = true,
                customLetterLabel = "TSA_WD_ReinforcementsArrived".Translate(reinforcementFaction.Name)
            };

            if (!IncidentDefOf.RaidEnemy.Worker.TryExecute(parms))
            {
                ExecuteManualSpawnFallback(reinforcementFaction);
            }
        }

        private void ExecuteManualSpawnFallback(Faction faction)
        {
            PawnGroupMakerParms pgmParms = new PawnGroupMakerParms { groupKind = PawnGroupKindDefOf.Combat, points = raidPoints, faction = faction };
            IEnumerable<Pawn> pawns = PawnGroupMakerUtility.GeneratePawns(pgmParms);
            if (!pawns.Any()) return;

            if (!CellFinder.TryFindRandomEdgeCellWith(c => c.Standable(map) && !c.Fogged(map), map, CellFinder.EdgeRoadChance_Hostile, out IntVec3 spawnCell))
                CellFinder.TryFindRandomEdgeCellWith(c => c.Standable(map), map, CellFinder.EdgeRoadChance_Hostile, out spawnCell);

            foreach (Pawn p in pawns) GenSpawn.Spawn(p, spawnCell, map);
            LordMaker.MakeNewLord(faction, new LordJob_AssaultColony(faction), map, pawns);
            Messages.Message("TSA_WD_ReinforcementsArrived".Translate(faction.Name), MessageTypeDefOf.ThreatBig);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref ticksUntilArrival, "ticksUntilArrival", -1);
            Scribe_Values.Look(ref raidPoints, "raidPoints", 0f);
            Scribe_Values.Look(ref initialized, "initialized", false);
            Scribe_References.Look(ref reinforcementFaction, "reinforcementFaction");
        }
    }

    [HarmonyPatch(typeof(MapComponentUtility), "FinalizeInit")]
    public static class Patch_TriggerReinforcementTimer
    {
        public static void Postfix(Map map)
        {
            if (map.IsPlayerHome) return;
            var settlement = map.info.parent as Settlement;
            if (settlement != null && settlement.Faction != null && WorldActions_Utils.SafeHostileTo(settlement.Faction, Faction.OfPlayer))
            {
                map.GetComponent<MapComponent_ReinforcementTimer>()?.InitializeReinforcements();
            }
        }
    }
}