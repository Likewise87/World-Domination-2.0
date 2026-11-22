using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// After vanilla colony-map recruit, chance to spill FoW on the closest unknown town of the prisoner's former faction.
    /// </summary>
    [HarmonyPatch(typeof(RecruitUtility), nameof(RecruitUtility.Recruit))]
    public static class Patch_RecruitUtility_Recruit_PrisonerIntelSpill
    {
        public struct SpillState
        {
            public bool eligible;
            public Faction formerFaction;
            public PlanetTile origin;
        }

        public static void Prefix(Pawn pawn, out SpillState __state)
        {
            __state = default;
            if (pawn == null || pawn.Destroyed) return;
            if (pawn.RaceProps?.Humanlike != true) return;
            if (!pawn.IsPrisonerOfColony) return;

            Faction former = pawn.Faction;
            if (former == null || former.IsPlayer) return;

            PlanetTile origin = default;
            if (pawn.Spawned && pawn.Map?.Parent != null)
                origin = pawn.Map.Parent.Tile;
            else if (pawn.Tile.Valid)
                origin = pawn.Tile;

            if (!origin.Valid) return;

            __state = new SpillState
            {
                eligible = true,
                formerFaction = former,
                origin = origin
            };
        }

        public static void Postfix(Pawn pawn, Faction faction, SpillState __state)
        {
            if (!__state.eligible) return;
            if (pawn == null || pawn.Destroyed || pawn.Dead) return;
            if (faction == null || !faction.IsPlayer) return;

            WdPrisonerIntelSpill.TrySpillOnRecruit(pawn, __state.formerFaction, __state.origin);
        }
    }
}
