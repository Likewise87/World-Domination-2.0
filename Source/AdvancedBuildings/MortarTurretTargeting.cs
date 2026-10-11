using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// NPC Mortar Turret idle target priority:
    /// hostile advanced buildings (past shell grace) &gt; hostile expansion / trader travelers and player caravans
    /// &gt; AT turrets &gt; settlements / player outposts. Road blocks are never targeted.
    /// </summary>
    public static class MortarTurretTargeting
    {
        /// <summary>Extra tiles beyond mortar range in which an inbound raid makes the turret hold its shell.</summary>
        public const float ReserveShellExtraTiles = 3f;

        private static float Dist(WorldObject a, WorldObject b, WorldComponent_SpreadManager manager) =>
            manager != null
                ? WorldActions_Utils.GetDistance(a.Tile, b.Tile, manager)
                : Find.WorldGrid.ApproxDistanceInTiles(a.Tile, b.Tile);

        public static WorldObject FindIdleTarget(WorldObject_WD_MortarTurret turret, bool canTargetPlayer, out float bestDist)
        {
            bestDist = float.MaxValue;
            if (turret == null || turret.Destroyed || turret.Faction == null) return null;
            Faction iFaction = turret.Faction;
            float range = turret.RangeTiles;
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            var sched = WorldComponent_InterceptionScheduler.Current;
            Faction player = Faction.OfPlayerSilentFail;
            bool playerHostile = player != null && WorldActions_Utils.SafeHostileTo(player, iFaction);

            bool Usable(WorldObject wo, out float d)
            {
                d = float.MaxValue;
                if (wo == null || wo.Destroyed || wo == turret || wo.Faction == null) return false;
                if (wo.Faction == iFaction) return false;
                if (!WorldActions_Utils.SafeHostileTo(wo.Faction, iFaction)) return false;
                if (wo.Faction.IsPlayer && (!canTargetPlayer || !playerHostile)) return false;
                if (sched != null && sched.IsInboundMortarTarget(wo.ID)) return false;
                d = Dist(turret, wo, manager);
                return d <= range;
            }

            // 1. Hostile advanced buildings (economic and artillery), past their shell grace.
            WorldObject best = null;
            var registry = WorldComponent_AdvancedBuildings.Get();
            if (registry != null)
            {
                IReadOnlyList<WorldObject_WD_AdvancedBuilding> all = registry.All;
                for (int i = 0; i < all.Count; i++)
                {
                    var b = all[i];
                    if (b == null || b.IsInShellGrace) continue;
                    if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(b)) continue;
                    if (!Usable(b, out float d) || d >= bestDist) continue;
                    bestDist = d;
                    best = b;
                }
            }
            if (best != null) return best;

            // 2. Hostile expansion / trader travelers and player caravans.
            if (sched != null)
            {
                foreach (WorldObject_Traveler t in sched.RegisteredTravelers)
                {
                    if (t == null || t.Destroyed || !t.Spawned) continue;
                    if (t.mission != TravelerMission.Expansion && t.mission != TravelerMission.Trader) continue;
                    if (AntiAirFireUtils.IsAirborneAaTarget(t)) continue;
                    if (!Usable(t, out float d) || d >= bestDist) continue;
                    bestDist = d;
                    best = t;
                }
            }
            if (canTargetPlayer && playerHostile && AtTurretUtility.IsPlayerCaravanTargetingEnabled())
            {
                List<Caravan> caravans = Find.WorldObjects?.Caravans;
                if (caravans != null)
                {
                    for (int i = 0; i < caravans.Count; i++)
                    {
                        Caravan c = caravans[i];
                        if (c == null || c.Destroyed || !c.Spawned) continue;
                        if (!AtTurretUtility.CanEnemySystemsTargetPlayerCaravan(c)) continue;
                        if (!Usable(c, out float d) || d >= bestDist) continue;
                        bestDist = d;
                        best = c;
                    }
                }
            }
            if (best != null) return best;

            // 3. AT turrets.
            IReadOnlyList<WorldObject_AT_Turret> ats = WorldObject_AT_Turret.LiveTurrets;
            for (int i = 0; i < ats.Count; i++)
            {
                WorldObject_AT_Turret at = ats[i];
                if (at == null) continue;
                if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(at)) continue;
                if (!Usable(at, out float d) || d >= bestDist) continue;
                bestDist = d;
                best = at;
            }
            if (best != null) return best;

            // 4. Settlements, then player outposts.
            List<Settlement> settlements = Find.WorldObjects.Settlements;
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement s = settlements[i];
                if (s == null || s.Faction == null || s.Faction.IsPlayer) continue;
                if (!PlanetSurfaceWorldActions.IsPlanetSurfaceWorldObjectForWorldActions(s)) continue;
                if (!Usable(s, out float d) || d >= bestDist) continue;
                bestDist = d;
                best = s;
            }
            if (canTargetPlayer && playerHostile)
            {
                IReadOnlyList<WorldObject_WD_Outpost> outposts = WdPlayerOutpostCache.PlayerOutposts;
                for (int i = 0; i < outposts.Count; i++)
                {
                    WorldObject_WD_Outpost o = outposts[i];
                    if (o == null || o.Faction != player) continue;
                    if (!WorldActions_Utils.IsWdSurfaceWorldObject(o)) continue;
                    if (!Usable(o, out float d) || d >= bestDist) continue;
                    bestDist = d;
                    best = o;
                }
            }
            return best;
        }

        /// <summary>
        /// True while a hostile raid traveler within mortar range + <see cref="ReserveShellExtraTiles"/> is heading for the
        /// turret or its owner; the turret then skips non-raider targets to keep its shell.
        /// </summary>
        public static bool ShouldReserveShell(WorldObject_WD_MortarTurret turret)
        {
            if (turret == null || turret.Faction == null) return false;
            var sched = WorldComponent_InterceptionScheduler.Current;
            if (sched == null) return false;
            Faction iFaction = turret.Faction;
            Settlement owner = turret.builtBySettlement;
            int ownerTile = owner != null && !owner.Destroyed ? owner.Tile.tileId : -1;
            int turretTile = turret.Tile.tileId;
            float watch = turret.RangeTiles + ReserveShellExtraTiles;
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            bool canTargetPlayer = WdEscalation.CanTargetPlayerWithT4Mortar(WorldDominationMod.settings, WdEscalation.GetCachedStage(manager));

            foreach (WorldObject_Traveler t in sched.RegisteredTravelers)
            {
                if (t == null || t.Destroyed || !t.Spawned) continue;
                if (!WorldObject_Traveler.IsRaidMission(t.mission)) continue;
                if (t.Faction == null || t.Faction == iFaction) continue;
                if (!WorldActions_Utils.SafeHostileTo(t.Faction, iFaction)) continue;
                if (t.Faction.IsPlayer && !canTargetPlayer) continue;

                bool inbound = false;
                WorldObject tgt = t.targetObject;
                if (tgt != null && !tgt.Destroyed && (tgt == owner || tgt == turret))
                    inbound = true;
                else if (t.pather != null && t.pather.destTile.Valid)
                {
                    int dest = t.pather.destTile.tileId;
                    inbound = dest == ownerTile || dest == turretTile;
                }
                if (!inbound) continue;
                if (Dist(turret, t, manager) <= watch) return true;
            }
            return false;
        }
    }
}
