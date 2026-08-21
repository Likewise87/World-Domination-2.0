using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Hover ShowAllyRadius + AttackRadius on NPC settlements (player colony excluded; outposts wire their own).</summary>
    public static class Patch_SettlementAllyRadiusGizmo
    {
        public static IEnumerable<Gizmo> GetGizmos(Settlement settlement)
        {
            if (settlement == null || settlement.Destroyed)
                yield break;
            if (settlement.Faction == null || settlement.Faction.IsPlayer)
                yield break;
            if (settlement.GetComponent<CompViralSpread>() == null)
                yield break;

            foreach (var g in AllyRadiusGizmo.Get(settlement))
                yield return g;
            foreach (var g in RadiusHoverGizmos.GetAttackForSettlement(settlement))
                yield return g;
        }
    }
}
