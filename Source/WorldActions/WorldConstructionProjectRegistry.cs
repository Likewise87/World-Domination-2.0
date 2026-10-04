using System.Collections.Generic;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// In-flight bridge / AT turret construction comps (not travelers, not the 30s outpost cache).
    /// Lock and reserve walks these lists. Rebuild after load; mutate on commit/clear.
    /// </summary>
    public static class WorldConstructionProjectRegistry
    {
        private static readonly List<CompViralSpread> bridgeProjects = new List<CompViralSpread>();
        private static readonly List<CompViralSpread> atTurretProjects = new List<CompViralSpread>();

        public static IReadOnlyList<CompViralSpread> ActiveBridgeProjects => bridgeProjects;

        public static IReadOnlyList<CompViralSpread> ActiveAtTurretProjects => atTurretProjects;

        public static void NotifyBridgeChanged(CompViralSpread comp)
        {
            if (comp == null) return;
            bool should = WorldActions_BuildBridge.HasActiveBridgeProject(comp)
                && comp.parent != null
                && !comp.parent.Destroyed;
            SetListed(bridgeProjects, comp, should);
        }

        public static void NotifyAtTurretChanged(CompViralSpread comp)
        {
            if (comp == null) return;
            bool should = WorldActions_AtTurrets.HasActiveAtTurretProject(comp)
                && comp.parent != null
                && !comp.parent.Destroyed;
            SetListed(atTurretProjects, comp, should);
        }

        /// <summary>
        /// Authoritative rebuild after world objects spawn. Static lists are not per-World.
        /// </summary>
        public static void Rebuild()
        {
            bridgeProjects.Clear();
            atTurretProjects.Clear();
            List<WorldObject> all = Find.WorldObjects?.AllWorldObjects;
            if (all == null) return;
            for (int i = 0; i < all.Count; i++)
            {
                CompViralSpread c = all[i]?.GetComponent<CompViralSpread>();
                if (c == null || c.parent == null || c.parent.Destroyed) continue;
                if (WorldActions_BuildBridge.HasActiveBridgeProject(c))
                    bridgeProjects.Add(c);
                if (WorldActions_AtTurrets.HasActiveAtTurretProject(c))
                    atTurretProjects.Add(c);
            }
        }

        private static void SetListed(List<CompViralSpread> list, CompViralSpread comp, bool should)
        {
            int idx = list.IndexOf(comp);
            if (should)
            {
                if (idx < 0)
                    list.Add(comp);
            }
            else if (idx >= 0)
            {
                list.RemoveAt(idx);
            }
        }
    }
}
