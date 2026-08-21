using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>AT Turret build/combat tier (Light / Medium / Heavy).</summary>
    public enum AtTurretTier : byte
    {
        Light = 0,
        Medium = 1,
        Heavy = 2
    }

    /// <summary>
    /// Spawn, ownership caps, and tile legality for AT Turrets.
    /// NPC: per <see cref="WorldObject_AT_Turret.builtBySettlement"/> and settlement tier (T1–T4 settings).
    /// Player: global cap plus per colony/outpost site cap (Experimental settings).
    /// </summary>
    public static class AtTurretUtility
    {
        public const string DefName = "TSA_WD_AT_Turret";
        /// <summary>Legacy alias; all tiers share one WorldObjectDef.</summary>
        public const string MediumDefName = DefName;

        public static int PlayerGlobalMax =>
            Mathf.Max(0, WorldDominationMod.settings?.atTurretPlayerGlobalMax
                ?? WorldDominationSettings.DefAtTurretPlayerGlobalMax);

        public static int PlayerPerSiteMax =>
            Mathf.Max(0, WorldDominationMod.settings?.atTurretPlayerPerSiteMax
                ?? WorldDominationSettings.DefAtTurretPlayerPerSiteMax);

        public static int MaxTurretsForSettlementTier(SettlementTier tier)
        {
            var s = WorldDominationMod.settings;
            switch (tier)
            {
                case SettlementTier.T4:
                    return Mathf.Max(0, s?.atTurretMaxT4 ?? WorldDominationSettings.DefAtTurretMaxT4);
                case SettlementTier.T3:
                    return Mathf.Max(0, s?.atTurretMaxT3 ?? WorldDominationSettings.DefAtTurretMaxT3);
                case SettlementTier.T2:
                    return Mathf.Max(0, s?.atTurretMaxT2 ?? WorldDominationSettings.DefAtTurretMaxT2);
                default:
                    return Mathf.Max(0, s?.atTurretMaxT1 ?? WorldDominationSettings.DefAtTurretMaxT1);
            }
        }

        /// <summary>T1/T2 Light, T3 Medium, T4 Heavy.</summary>
        public static AtTurretTier PreferredTierForSettlementTier(SettlementTier settlementTier)
        {
            switch (settlementTier)
            {
                case SettlementTier.T4: return AtTurretTier.Heavy;
                case SettlementTier.T3: return AtTurretTier.Medium;
                default: return AtTurretTier.Light;
            }
        }

        public static bool IsTierBuildable(AtTurretTier tier) =>
            tier == AtTurretTier.Light || tier == AtTurretTier.Medium || tier == AtTurretTier.Heavy;

        public static SettlementTier SettlementTierOf(Settlement settlement)
        {
            if (settlement == null) return SettlementTier.T1;
            return settlement.GetComponent<CompViralSpread>()?.tier ?? SettlementTier.T1;
        }

        public static bool IsTierAllowedForSettlement(Settlement settlement, AtTurretTier turretTier)
        {
            if (settlement == null || settlement.Destroyed) return false;
            return turretTier == PreferredTierForSettlementTier(SettlementTierOf(settlement));
        }

        /// <summary>Player-facing settlement tier requirement for build-menu disable labels.</summary>
        public static string AllowedSettlementTiersLabelKey(AtTurretTier turretTier)
        {
            switch (turretTier)
            {
                case AtTurretTier.Heavy: return "TSA_WD_Tier4";
                case AtTurretTier.Medium: return "TSA_WD_Tier3";
                default: return "TSA_WD_AT_TurretTier_SettlementT1T2";
            }
        }

        public static string TexturePathForTier(AtTurretTier tier)
        {
            switch (tier)
            {
                case AtTurretTier.Light: return "WorldObjects/AT_Gun_Light";
                case AtTurretTier.Heavy: return "WorldObjects/AT_Gun_Heavy";
                default: return "WorldObjects/AT_Gun_Medium";
            }
        }

        public static string DefNameForTier(AtTurretTier tier) => DefName;

        public static int CountTurretsBuiltBy(Settlement settlement)
        {
            if (settlement == null) return 0;
            int count = 0;
            var all = Find.WorldObjects?.AllWorldObjects;
            if (all == null) return 0;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is WorldObject_AT_Turret t && !t.Destroyed && t.builtBySettlement == settlement)
                    count++;
            }
            return count;
        }

        public static int CountPlayerTurrets()
        {
            int count = 0;
            var all = Find.WorldObjects?.AllWorldObjects;
            if (all == null) return 0;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is WorldObject_AT_Turret t && !t.Destroyed && t.Faction?.IsPlayer == true)
                    count++;
            }
            return count;
        }

        /// <summary>
        /// Turrets ordered by this player site. Prefers <see cref="WorldObject_AT_Turret.builtBySite"/>;
        /// older saves without it count via <see cref="WorldObject_AT_Turret.builtBySettlement"/>.
        /// </summary>
        public static int CountTurretsBuiltBySite(WorldObject site)
        {
            if (site == null || site.Destroyed) return 0;
            int count = 0;
            var all = Find.WorldObjects?.AllWorldObjects;
            if (all == null) return 0;
            for (int i = 0; i < all.Count; i++)
            {
                if (!(all[i] is WorldObject_AT_Turret t) || t.Destroyed) continue;
                if (t.builtBySite != null)
                {
                    if (t.builtBySite == site)
                        count++;
                }
                else if (t.builtBySettlement == site)
                {
                    count++;
                }
            }
            return count;
        }

        public static bool IsPlayerBuildSite(WorldObject site) =>
            site != null
            && !site.Destroyed
            && site.Faction?.IsPlayer == true
            && (site is Settlement || site is WorldObject_WD_Outpost);

        /// <summary>
        /// Ground WD traveler missions NPC AT turrets may engage when the player-traveler Experimental flag is on.
        /// Excludes shells and airborne pods (drop pods / ballistic cargo).
        /// </summary>
        public static bool IsGroundAtTurretTravelerTarget(WorldObject_Traveler traveler)
        {
            if (traveler == null || traveler.Destroyed) return false;
            if (traveler.IsAtTurretShell()) return false;
            if (AntiAirFireUtils.IsAirborneAaTargetMission(traveler.mission)) return false;
            if (OutpostDispatchMode.IsPlayerCargoDropPod(traveler)) return false;
            if (WD_PathFollower.IsBallisticWorldFlight(traveler)) return false;
            return true;
        }

        public static bool IsPlayerTravelerTargetingEnabled()
        {
            var s = WorldDominationMod.settings;
            return s == null || s.enableAtTurretTargetPlayerTravelers;
        }

        public static bool IsPlayerCaravanTargetingEnabled()
        {
            var s = WorldDominationMod.settings;
            return s == null || s.enableAtTurretTargetPlayerCaravans;
        }

        /// <summary>Player ground traveler eligible for an NPC (or any hostile) AT auto-fire when the traveler flag is on.</summary>
        public static bool CanAutoTargetPlayerTraveler(WorldObject_AT_Turret turret, WorldObject_Traveler traveler)
        {
            if (turret == null || turret.Destroyed || traveler == null || traveler.Destroyed) return false;
            if (!IsPlayerTravelerTargetingEnabled()) return false;
            if (traveler.Faction?.IsPlayer != true) return false;
            if (turret.Faction == null || traveler.Faction == turret.Faction) return false;
            if (!WorldActions_Utils.SafeHostileTo(traveler.Faction, turret.Faction)) return false;
            return IsGroundAtTurretTravelerTarget(traveler);
        }

        /// <summary>Player caravan eligible for AT auto-fire when the caravan flag is on (independent of traveler flag).</summary>
        public static bool CanAutoTargetPlayerCaravan(WorldObject_AT_Turret turret, Caravan caravan)
        {
            if (turret == null || turret.Destroyed || caravan == null || caravan.Destroyed || !caravan.Spawned)
                return false;
            if (!IsPlayerCaravanTargetingEnabled()) return false;
            if (caravan.Faction?.IsPlayer != true) return false;
            if (turret.Faction == null || caravan.Faction == turret.Faction) return false;
            if (!WorldActions_Utils.SafeHostileTo(caravan.Faction, turret.Faction)) return false;
            return caravan.Tile.tileId >= 0;
        }

        /// <summary>NPC settlement tier caps. Player settlements use <see cref="CanPlayerSiteBuildAnother"/>.</summary>
        public static bool CanBuildAnother(Settlement settlement)
        {
            if (settlement == null || settlement.Destroyed) return false;
            if (settlement.Faction?.IsPlayer == true)
                return CanPlayerSiteBuildAnother(settlement);
            var comp = settlement.GetComponent<CompViralSpread>();
            SettlementTier tier = comp?.tier ?? SettlementTier.T1;
            return CountTurretsBuiltBy(settlement) < MaxTurretsForSettlementTier(tier);
        }

        public static bool CanPlayerSiteBuildAnother(WorldObject site)
        {
            if (!IsPlayerBuildSite(site)) return false;
            if (CountPlayerTurrets() + CountInFlightPlayerTurretCrews() >= PlayerGlobalMax) return false;
            if (CountTurretsBuiltBySite(site) + CountInFlightTurretCrewsFrom(site) >= PlayerPerSiteMax) return false;
            return true;
        }

        /// <summary>Placed-turret caps only (no in-flight crews). Used when a crew arrives to spawn.</summary>
        public static bool CanPlayerSiteAcceptPlacedTurret(WorldObject site)
        {
            if (!IsPlayerBuildSite(site)) return false;
            if (CountPlayerTurrets() >= PlayerGlobalMax) return false;
            if (CountTurretsBuiltBySite(site) >= PlayerPerSiteMax) return false;
            return true;
        }

        /// <summary>Which player cap blocks building, for disabled Build menu labels.</summary>
        public static string PlayerAtCapLabel(WorldObject site)
        {
            if (!IsPlayerBuildSite(site))
                return "TSA_WD_AT_Turret_AtCap".Translate(PlayerPerSiteMax).ToString();

            if (CountPlayerTurrets() + CountInFlightPlayerTurretCrews() >= PlayerGlobalMax)
                return "TSA_WD_AT_Turret_AtCapGlobal".Translate(PlayerGlobalMax).ToString();

            return "TSA_WD_AT_Turret_AtCapSite".Translate(PlayerPerSiteMax).ToString();
        }

        /// <summary>Includes in-flight NPC/player AT crews so Fortify does not over-schedule past the cap.</summary>
        public static bool CanScheduleAnother(Settlement settlement)
        {
            if (settlement == null || settlement.Destroyed) return false;
            if (settlement.Faction?.IsPlayer == true)
                return CanPlayerSiteBuildAnother(settlement);
            var comp = settlement.GetComponent<CompViralSpread>();
            SettlementTier tier = comp?.tier ?? SettlementTier.T1;
            int max = MaxTurretsForSettlementTier(tier);
            return CountTurretsBuiltBy(settlement) + CountInFlightTurretCrews(settlement) < max;
        }

        public static int CountInFlightTurretCrews(Settlement settlement)
        {
            return CountInFlightTurretCrewsFrom(settlement);
        }

        public static int CountInFlightTurretCrewsFrom(WorldObject origin)
        {
            if (origin == null || Find.WorldObjects == null) return 0;
            int count = 0;
            var all = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < all.Count; i++)
            {
                if (!(all[i] is WorldObject_Traveler t) || t.Destroyed) continue;
                if (t.originObject != origin) continue;
                if (t.mission == TravelerMission.NpcAtTurret || t.mission == TravelerMission.AtTurret)
                    count++;
            }
            return count;
        }

        public static int CountInFlightPlayerTurretCrews()
        {
            if (Find.WorldObjects == null) return 0;
            int count = 0;
            var all = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < all.Count; i++)
            {
                if (!(all[i] is WorldObject_Traveler t) || t.Destroyed) continue;
                if (t.Faction?.IsPlayer != true) continue;
                if (t.mission == TravelerMission.AtTurret)
                    count++;
            }
            return count;
        }

        public static void DestroyTurretsBuiltBy(Settlement settlement)
        {
            if (settlement == null) return;
            var all = Find.WorldObjects?.AllWorldObjects;
            if (all == null) return;
            List<WorldObject_AT_Turret> doomed = null;
            for (int i = 0; i < all.Count; i++)
            {
                if (!(all[i] is WorldObject_AT_Turret t) || t.Destroyed) continue;
                if (t.builtBySettlement != settlement) continue;
                doomed ??= new List<WorldObject_AT_Turret>();
                doomed.Add(t);
            }
            if (doomed == null) return;
            for (int i = 0; i < doomed.Count; i++)
            {
                if (doomed[i] != null && !doomed[i].Destroyed)
                {
                    doomed[i].suppressDestroyedLetter = true;
                    doomed[i].Destroy();
                }
            }
        }

        public static bool TileHasRoad(int tileId)
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || tileId < 0 || !grid.InBounds(tileId)) return false;
            if (!(grid[tileId] is SurfaceTile surface)) return false;
            var roads = surface.Roads;
            return roads != null && roads.Count > 0;
        }

        public static bool TileHasAtTurret(int tileId)
        {
            if (tileId < 0) return false;
            foreach (WorldObject wo in Find.WorldObjects.ObjectsAt(tileId))
            {
                if (wo is WorldObject_AT_Turret t && !t.Destroyed)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Off-road land tile, empty of settlement/outpost/block/trap/turret,
        /// adjacent to a same-faction road block.
        /// </summary>
        public static bool IsLegalTurretTile(int tileId, Faction faction)
        {
            if (!IsEmptyOffRoadTurretSite(tileId)) return false;
            return HasAdjacentSameFactionRoadBlock(tileId, faction);
        }

        /// <summary>
        /// Player build eligibility: passable land without settlement/outpost/block/trap/turret.
        /// Roads are allowed; no road-block adjacency required.
        /// </summary>
        public static bool IsPlayerBuildableTurretTile(int tileId)
        {
            if (!WorldActions_RoadBlocks.IsTileBaseEligibleForRoadBlock(tileId)) return false;
            if (WorldComponent_RoadBlocks.Get()?.HasBlockAt(tileId) == true) return false;
            if (WorldComponent_SpikeTraps.Get()?.HasTrapAt(tileId) == true) return false;
            if (TileHasAtTurret(tileId)) return false;
            return true;
        }

        public static int RemainingPlayerSlotsForSite(WorldObject site)
        {
            if (!IsPlayerBuildSite(site)) return 0;
            int siteLeft = PlayerPerSiteMax - CountTurretsBuiltBySite(site) - CountInFlightTurretCrewsFrom(site);
            int globalLeft = PlayerGlobalMax - CountPlayerTurrets() - CountInFlightPlayerTurretCrews();
            return Mathf.Max(0, Mathf.Min(siteLeft, globalLeft));
        }

        public static WorldObject_AT_Turret FindTurretAt(int tileId)
        {
            if (tileId < 0 || Find.WorldObjects == null) return null;
            foreach (WorldObject wo in Find.WorldObjects.ObjectsAt(tileId))
            {
                if (wo is WorldObject_AT_Turret t && !t.Destroyed)
                    return t;
            }
            return null;
        }

        /// <summary>
        /// Temporary: a player caravan overruns a hostile AT Turret on its tile (no defense map yet).
        /// Returns true when a turret was destroyed.
        /// </summary>
        public static bool TryOverrunHostileAtTurret(Caravan caravan)
        {
            if (caravan == null || caravan.Destroyed || caravan.Faction?.IsPlayer != true) return false;
            return TryOverrunHostileAtTurretOnTile(caravan.Tile.tileId, caravan.Faction, caravan);
        }

        /// <summary>
        /// Temporary: clear a hostile non-player AT Turret on <paramref name="tileId"/> when the player digs into that tile
        /// (caravan clash / RR drop-pod fight). Same overrun rules as <see cref="TryOverrunHostileAtTurret"/>.
        /// </summary>
        public static bool TryOverrunHostileAtTurretOnTile(int tileId, Faction playerFaction, WorldObject logSubject = null)
        {
            if (tileId < 0 || playerFaction == null || !playerFaction.IsPlayer) return false;
            WorldObject_AT_Turret turret = FindTurretAt(tileId);
            if (turret == null || turret.Destroyed) return false;
            if (turret.Faction == null || turret.Faction.IsPlayer) return false;
            if (!WorldActions_Utils.SafeHostileTo(playerFaction, turret.Faction)) return false;

            string gunLabel = turret.LabelCap;
            turret.suppressDestroyedLetter = true;
            turret.Destroy();

            TaggedString text = "TSA_WD_AT_Turret_PlayerOverran_Text".Translate(gunLabel);
            Find.World?.GetComponent<WorldComponent_SpreadManager>()?.AddLog(
                new SpreadLogEntry(text.Resolve(), logSubject, tileId));
            Messages.Message(text, MessageTypeDefOf.NeutralEvent, false);
            return true;
        }

        public static bool TileHasPlayerAtTurret(int tileId)
        {
            WorldObject_AT_Turret t = FindTurretAt(tileId);
            return t != null && !t.Destroyed && t.Faction?.IsPlayer == true;
        }

        /// <summary>Player lost a caravan clash fought on their AT tile: destroy the gun with one destroyed letter.</summary>
        public static void DestroyPlayerAtTurretOnTileAfterClashDefeat(int tileId, WorldObject attacker = null)
        {
            WorldObject_AT_Turret turret = FindTurretAt(tileId);
            if (turret == null || turret.Destroyed || turret.Faction?.IsPlayer != true) return;
            AtTurretNotifyUtility.NotifyPlayerTurretDestroyed(turret, attacker);
            turret.suppressDestroyedLetter = true;
            turret.Destroy();
        }

        public static bool IsEmptyOffRoadTurretSite(int tileId)
        {
            if (!WorldActions_RoadBlocks.IsTileBaseEligibleForRoadBlock(tileId)) return false;
            if (TileHasRoad(tileId)) return false;
            if (WorldComponent_RoadBlocks.Get()?.HasBlockAt(tileId) == true) return false;
            if (WorldComponent_SpikeTraps.Get()?.HasTrapAt(tileId) == true) return false;
            if (TileHasAtTurret(tileId)) return false;
            return true;
        }

        public static bool HasAdjacentSameFactionRoadBlock(int tileId, Faction faction)
        {
            if (faction == null) return false;
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || tileId < 0 || !grid.InBounds(tileId)) return false;
            var blocks = WorldComponent_RoadBlocks.Get();
            if (blocks == null) return false;

            List<PlanetTile> neighbors = new List<PlanetTile>();
            grid.GetTileNeighbors(new PlanetTile(tileId, grid[tileId].Layer), neighbors);
            for (int i = 0; i < neighbors.Count; i++)
            {
                if (!blocks.TryGet(neighbors[i].tileId, out RoadBlockRecord rec) || rec == null) continue;
                if (rec.builtByFaction == faction) return true;
            }
            return false;
        }

        /// <summary>
        /// Pick an empty off-road neighbor of a road-block tile.
        /// Prefers sites where the block sits between the turret and the threat / road.
        /// </summary>
        public static bool TryFindTileBesideRoadBlock(
            int roadBlockTileId,
            Faction faction,
            WorldObject preferToward,
            out int turretTileId)
        {
            turretTileId = -1;
            return TryScoreNeighborsOfBlock(roadBlockTileId, preferToward, out turretTileId, out _);
        }

        /// <summary>
        /// Best legal AT site beside any road block owned by <paramref name="owner"/>,
        /// within <paramref name="maxTravelTiles"/> of <paramref name="travelFrom"/>.
        /// </summary>
        public static bool TryFindBestTurretSiteForSettlement(
            Settlement owner,
            WorldObject threat,
            Settlement travelFrom,
            float maxTravelTiles,
            out int blockTileId,
            out int turretTileId)
        {
            blockTileId = -1;
            turretTileId = -1;
            if (owner == null || owner.Destroyed || owner.Faction == null) return false;
            WorldGrid grid = Find.WorldGrid;
            var blocks = WorldComponent_RoadBlocks.Get();
            if (grid == null || blocks?.Records == null) return false;

            int fromTile = travelFrom != null && !travelFrom.Destroyed ? travelFrom.Tile.tileId : owner.Tile.tileId;
            float maxTravel = Mathf.Max(1f, maxTravelTiles);

            float bestScore = float.MinValue;
            int bestBlock = -1;
            int bestTurret = -1;
            var records = blocks.Records;
            for (int i = 0; i < records.Count; i++)
            {
                RoadBlockRecord rec = records[i];
                if (rec == null || rec.tileId < 0) continue;
                if (rec.builtBySettlement != owner
                    && !(rec.builtBySettlement == null && rec.builtByFaction == owner.Faction))
                    continue;

                if (!TryScoreNeighborsOfBlock(rec.tileId, threat, out int site, out float score))
                    continue;
                if (fromTile >= 0 && grid.ApproxDistanceInTiles(fromTile, site) > maxTravel + 0.01f)
                    continue;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestBlock = rec.tileId;
                    bestTurret = site;
                }
            }

            if (bestTurret < 0) return false;
            blockTileId = bestBlock;
            turretTileId = bestTurret;
            return true;
        }

        private static bool TryScoreNeighborsOfBlock(
            int roadBlockTileId,
            WorldObject preferToward,
            out int turretTileId,
            out float bestScore)
        {
            turretTileId = -1;
            bestScore = float.MinValue;
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || roadBlockTileId < 0 || !grid.InBounds(roadBlockTileId)) return false;

            List<PlanetTile> neighbors = new List<PlanetTile>();
            grid.GetTileNeighbors(new PlanetTile(roadBlockTileId, grid[roadBlockTileId].Layer), neighbors);

            int threatTile = preferToward != null && !preferToward.Destroyed ? preferToward.Tile.tileId : -1;
            bool blockOnRoad = TileHasRoad(roadBlockTileId);
            Vector3 blockPos = grid.GetTileCenter(roadBlockTileId);
            Vector3 threatPos = threatTile >= 0 ? grid.GetTileCenter(threatTile) : Vector3.zero;
            int best = -1;

            for (int i = 0; i < neighbors.Count; i++)
            {
                int n = neighbors[i].tileId;
                if (!IsEmptyOffRoadTurretSite(n)) continue;

                float score = Rand.Value;
                if (blockOnRoad)
                    score += 200f;

                if (threatTile >= 0)
                {
                    float distN = grid.ApproxDistanceInTiles(n, threatTile);
                    float distB = grid.ApproxDistanceInTiles(roadBlockTileId, threatTile);
                    if (distB < distN - 0.01f)
                        score += 500f;

                    Vector3 nPos = grid.GetTileCenter(n);
                    Vector3 toThreat = threatPos - nPos;
                    Vector3 toBlock = blockPos - nPos;
                    if (toThreat.sqrMagnitude > 0.0001f && toBlock.sqrMagnitude > 0.0001f)
                    {
                        float align = Vector3.Dot(toThreat.normalized, toBlock.normalized);
                        if (align > 0.25f)
                            score += 300f * align;
                    }

                    score += 100f - distN;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = n;
                }
            }

            if (best < 0) return false;
            turretTileId = best;
            return true;
        }

        public static WorldObject_AT_Turret TrySpawn(
            int tileId,
            Faction faction,
            AtTurretTier tier,
            Settlement builtBySettlement,
            WorldObject builtBySite = null,
            bool requirePlayerBuildSite = true)
        {
            if (!IsTierBuildable(tier)) return null;
            if (faction == null) return null;
            if (tileId < 0 || Find.WorldGrid == null || !Find.WorldGrid.InBounds(tileId)) return null;
            if (faction.IsPlayer)
            {
                if (!IsPlayerBuildableTurretTile(tileId)) return null;
            }
            else if (!IsEmptyOffRoadTurretSite(tileId))
            {
                return null;
            }

            WorldObject site = builtBySite ?? builtBySettlement;
            if (faction.IsPlayer)
            {
                if (requirePlayerBuildSite && !CanPlayerSiteAcceptPlacedTurret(site)) return null;
            }
            else if (builtBySettlement != null && !CanBuildAnother(builtBySettlement))
            {
                return null;
            }

            var def = DefDatabase<WorldObjectDef>.GetNamedSilentFail(DefNameForTier(tier));
            if (def == null)
            {
                Log.Warning($"[TSA WD] Missing WorldObjectDef {DefNameForTier(tier)} for AT Turret spawn.");
                return null;
            }

            var turret = (WorldObject_AT_Turret)WorldObjectMaker.MakeWorldObject(def);
            turret.Tile = new PlanetTile(tileId, Find.WorldGrid[tileId].Layer);
            turret.SetFaction(faction);
            turret.tier = tier;
            turret.builtBySettlement = builtBySettlement;
            turret.builtBySite = site;
            var s = WorldDominationMod.settings;
            turret.strength = s != null
                ? s.GetAtTurretMaxStrength(tier)
                : WorldDominationSettings.GetAtTurretMaxStrengthDefault(tier);
            // New guns start on full fire CD (player and NPC) so PostAdd cannot snap-fire.
            turret.ApplyCooldown();
            Find.WorldObjects.Add(turret);
            return turret;
        }

        public static string LabelKey(AtTurretTier tier)
        {
            switch (tier)
            {
                case AtTurretTier.Light: return "TSA_WD_AT_TurretTier_Light";
                case AtTurretTier.Heavy: return "TSA_WD_AT_TurretTier_Heavy";
                default: return "TSA_WD_AT_TurretTier_Medium";
            }
        }
    }
}

