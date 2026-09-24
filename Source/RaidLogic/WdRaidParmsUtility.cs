using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace TSA_WorldDomination
{
    /// <summary>
    /// WD pins ImmediateAttack + a preferred arrival before <see cref="IncidentDefOf.RaidEnemy"/>.
    /// Some factions (e.g. Oberonia Aurea) disallow that strategy on Combat PawnGroupMakers and generate
    /// 0 pawns. This helper swaps to a faction-accepted strategy and derives arrival from
    /// <see cref="RaidStrategyDef.arriveModes"/> (no WD mapping table).
    /// </summary>
    public static class WdRaidParmsUtility
    {
        /// <summary>
        /// Ensure <paramref name="parms"/>.raidStrategy is usable with the faction's Combat makers;
        /// fix arrival from that strategy's <c>arriveModes</c> (or clear for vanilla resolve).
        /// </summary>
        public static void EnsureFactionCompatibleRaidParms(IncidentParms parms, PawnsArrivalModeDef preferredArrival = null)
        {
            if (parms?.faction == null) return;

            PawnGroupKindDef kind = PawnGroupKindDefOf.Combat;
            RaidStrategyDef beforeStrategy = parms.raidStrategy;
            PawnsArrivalModeDef beforeArrival = parms.raidArrivalMode;

            if (parms.raidStrategy == null
                || !StrategyUsable(parms, kind, parms.raidStrategy)
                || !FactionAcceptsStrategy(parms, kind, parms.raidStrategy))
            {
                RaidStrategyDef alt = PickAcceptedStrategy(parms, kind);
                if (alt != null)
                    parms.raidStrategy = alt;
            }

            if (parms.raidStrategy == null)
                return;

            FixArrivalForStrategy(parms, preferredArrival ?? beforeArrival);

            if (Prefs.DevMode
                && (parms.raidStrategy != beforeStrategy || parms.raidArrivalMode != beforeArrival))
            {
                Log.Message(
                    "[TSA WD] Raid parms adjusted for faction "
                    + parms.faction.def.defName
                    + ": strategy "
                    + (beforeStrategy?.defName ?? "(null)")
                    + " -> "
                    + (parms.raidStrategy?.defName ?? "(null)")
                    + ", arrival "
                    + (beforeArrival?.defName ?? "(null)")
                    + " -> "
                    + (parms.raidArrivalMode?.defName ?? "(null/vanilla)"));
            }
        }

        /// <summary>
        /// Last-resort edge spawn when <see cref="IncidentWorker_RaidEnemy.TryExecuteWorker"/> fails.
        /// Passes <paramref name="raidStrategy"/> into group parms so exclusive mod makers still generate.
        /// </summary>
        public static bool TryManualAssaultSpawn(
            Map map,
            Faction faction,
            float points,
            RaidStrategyDef raidStrategy,
            string letterLabel = null,
            string letterText = null)
        {
            if (map == null || faction == null) return false;

            PawnGroupMakerParms pgmParms = new PawnGroupMakerParms
            {
                groupKind = PawnGroupKindDefOf.Combat,
                points = Mathf.Max(points, faction.def.MinPointsToGeneratePawnGroup(PawnGroupKindDefOf.Combat) * 1.05f),
                faction = faction,
                raidStrategy = raidStrategy
            };
            List<Pawn> pawns = PawnGroupMakerUtility.GeneratePawns(pgmParms).ToList();
            if (pawns.Count == 0) return false;

            if (!CellFinder.TryFindRandomEdgeCellWith(
                    c => c.Standable(map) && !c.Fogged(map),
                    map,
                    CellFinder.EdgeRoadChance_Hostile,
                    out IntVec3 spawnCell))
            {
                if (!CellFinder.TryFindRandomEdgeCellWith(
                        c => c.Standable(map),
                        map,
                        CellFinder.EdgeRoadChance_Hostile,
                        out spawnCell))
                    return false;
            }

            for (int i = 0; i < pawns.Count; i++)
                GenSpawn.Spawn(pawns[i], spawnCell, map);
            LordMaker.MakeNewLord(faction, new LordJob_AssaultColony(faction), map, pawns);

            if (!letterLabel.NullOrEmpty() || !letterText.NullOrEmpty())
            {
                Find.LetterStack.ReceiveLetter(
                    letterLabel.NullOrEmpty() ? "Raid".Translate() : (TaggedString)letterLabel,
                    letterText.NullOrEmpty() ? faction.Name : (TaggedString)letterText,
                    LetterDefOf.ThreatBig,
                    new TargetInfo(spawnCell, map));
            }

            return true;
        }

        private static bool StrategyUsable(IncidentParms parms, PawnGroupKindDef kind, RaidStrategyDef strategy)
        {
            if (strategy?.Worker == null) return false;
            return strategy.Worker.CanUseWith(parms, kind);
        }

        private static bool FactionAcceptsStrategy(IncidentParms parms, PawnGroupKindDef kind, RaidStrategyDef strategy)
        {
            if (parms?.faction?.def?.pawnGroupMakers == null || strategy == null)
                return false;

            PawnGroupMakerParms gp = IncidentParmsUtility.GetDefaultPawnGroupMakerParms(kind, parms);
            gp.raidStrategy = strategy;
            List<PawnGroupMaker> makers = parms.faction.def.pawnGroupMakers;
            for (int i = 0; i < makers.Count; i++)
            {
                PawnGroupMaker gm = makers[i];
                if (gm == null || gm.kindDef != kind) continue;
                if (gm.CanGenerateFrom(gp))
                    return true;
            }
            return false;
        }

        private static RaidStrategyDef PickAcceptedStrategy(IncidentParms parms, PawnGroupKindDef kind)
        {
            Map map = parms.target as Map;
            List<RaidStrategyDef> accepted = new List<RaidStrategyDef>();
            List<RaidStrategyDef> all = DefDatabase<RaidStrategyDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                RaidStrategyDef s = all[i];
                if (s == null) continue;
                if (!StrategyUsable(parms, kind, s)) continue;
                if (!FactionAcceptsStrategy(parms, kind, s)) continue;
                accepted.Add(s);
            }

            if (accepted.Count == 0)
                return null;

            if (accepted.TryRandomElementByWeight(
                    s => Mathf.Max(0f, s.Worker.SelectionWeightForFaction(map, parms.faction, parms.points)),
                    out RaidStrategyDef weighted)
                && weighted != null)
            {
                return weighted;
            }

            // Exclusive mod strategies often have zero storyteller weight but are still the only Accepts match.
            return accepted.RandomElement();
        }

        private static void FixArrivalForStrategy(IncidentParms parms, PawnsArrivalModeDef preferredArrival)
        {
            RaidStrategyDef strategy = parms.raidStrategy;
            if (strategy?.arriveModes == null || strategy.arriveModes.Count == 0)
                return;

            if (ArrivalAllowed(parms, strategy, preferredArrival))
            {
                parms.raidArrivalMode = preferredArrival;
                return;
            }

            if (ArrivalAllowed(parms, strategy, parms.raidArrivalMode))
                return;

            // Let vanilla ResolveRaidArriveMode pick from strategy.arriveModes.
            parms.raidArrivalMode = null;
        }

        private static bool ArrivalAllowed(IncidentParms parms, RaidStrategyDef strategy, PawnsArrivalModeDef mode)
        {
            if (mode == null || strategy?.arriveModes == null) return false;
            if (!strategy.arriveModes.Contains(mode)) return false;
            return mode.Worker != null && mode.Worker.CanUseWith(parms);
        }
    }
}
