using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Experimental: player colony Settlement as a world-construction actor (roads / blocks / traps).
    /// Progression from map Construction skill sum; no Engineer bonus; no WD strength cost.
    /// </summary>
    public static class ColonyWorldBuildUtility
    {
        public static bool IsFeatureEnabled =>
            WorldDominationMod.settings == null || WorldDominationMod.settings.experimentalColonyWorldBuild;

        public static bool IsPlayerColonyBuildActor(WorldObject actor)
        {
            if (!IsFeatureEnabled || actor == null || actor.Destroyed) return false;
            if (!(actor is Settlement settlement) || settlement.Faction?.IsPlayer != true) return false;
            return settlement.HasMap && settlement.Map != null;
        }

        public static bool WaivesExpeditionStrength(CompViralSpread comp) =>
            comp != null && comp.IsPlayerMapSettlement && IsFeatureEnabled;

        public static float GetConstructionSkillRaw(Settlement settlement)
        {
            if (settlement?.Map == null) return 0f;
            float sum = 0f;
            var pawns = settlement.Map.mapPawns?.FreeColonists;
            if (pawns == null) return 0f;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p == null || p.Dead || p.skills == null) continue;
                if (p.RaceProps?.Humanlike != true) continue;
                var sk = p.skills.GetSkill(SkillDefOf.Construction);
                if (sk != null) sum += sk.Level;
            }
            return sum;
        }

        public static float GetConstructionSkillEffective(Settlement settlement) =>
            OutpostSkillScaling.ToEffective(GetConstructionSkillRaw(settlement));

        /// <summary>Raw Construction for unlock gates (outpost occupants or colony map colonists).</summary>
        public static float GetActorConstructionSkillRaw(WorldObject actor)
        {
            if (actor is WorldObject_WD_Outpost wd) return wd.TotalConstructionSkillRaw();
            if (actor is Settlement settlement && IsPlayerColonyBuildActor(settlement))
                return GetConstructionSkillRaw(settlement);
            return 0f;
        }

        /// <summary>Clear active colony construction if the experimental flag is off.</summary>
        public static void ClearProjectsIfFeatureDisabled(CompViralSpread comp)
        {
            if (comp == null || !comp.IsPlayerMapSettlement) return;
            if (IsFeatureEnabled) return;

            if (comp.roadTargetTile != -1)
                WorldActions_Roads.ClearRoadProject(comp, RoadProjectClearReason.AbortedInvalidTarget);
            if (WorldActions_RoadBlocks.HasActiveRoadBlockProject(comp))
                WorldActions_RoadBlocks.ClearRoadBlockProject(comp);
            if (WorldActions_SpikeTraps.HasActiveSpikeTrapProject(comp))
                WorldActions_SpikeTraps.ClearSpikeTrapProject(comp);
        }
    }
}
