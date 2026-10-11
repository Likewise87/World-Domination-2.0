using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Walk-over, overrun and destruction rules for <see cref="WorldObject_WD_AdvancedBuilding"/>.
    /// Economic buildings fall to any hostile ground traveler that crosses their tile; artillery turrets
    /// clash with hostile raids AT-style (<see cref="OpenFieldClashUtility.ResolveTravelerVsArtilleryTurret"/>).
    /// </summary>
    public static class AdvancedBuildingCombatUtility
    {
        /// <summary>"Silo of Kerbal Town" for advanced buildings with a live owner; LabelCap otherwise.</summary>
        public static string LabelWithOwner(WorldObject wo)
        {
            if (wo == null) return "?";
            if (wo is WorldObject_WD_AdvancedBuilding b)
            {
                Settlement owner = b.builtBySettlement;
                if (owner != null && !owner.Destroyed)
                    return "TSA_WD_AdvBuild_LabelWithOwner".Translate(b.LabelCap, owner.LabelCap).ToString();
            }
            return wo.LabelCap;
        }

        public static void LogBuildingDestroyed(WorldObject_WD_AdvancedBuilding building, WorldObject attacker)
        {
            if (building == null) return;
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (manager == null) return;
            string label = LabelWithOwner(building);
            string text = attacker != null
                ? "TSA_WD_AdvBuild_Log_DestroyedBy".Translate(label, attacker.LabelCap).ToString()
                : "TSA_WD_AdvBuild_Log_Destroyed".Translate(label).ToString();
            if (attacker != null)
                manager.AddLog(new SpreadLogEntry(text, attacker, building.Tile.tileId));
            else
                manager.AddLog(new SpreadLogEntry(text, building.builtBySettlement, building.Tile.tileId));
        }

        /// <summary>Destroy with one log line naming <paramref name="attacker"/> (no duplicate line from Destroy).</summary>
        public static void DestroyBuilding(WorldObject_WD_AdvancedBuilding building, WorldObject attacker, bool log = true)
        {
            if (building == null || building.Destroyed) return;
            if (log)
                LogBuildingDestroyed(building, attacker);
            building.suppressDestroyedLetter = true;
            building.Destroy();
        }

        private static bool IsGroundWalkOverMission(TravelerMission m)
        {
            switch (m)
            {
                case TravelerMission.MortarStrike:
                case TravelerMission.AntiAirStrike:
                case TravelerMission.RaidDropPod:
                case TravelerMission.RaidGravship:
                case TravelerMission.RapidResponseDropPod:
                    return false;
                default:
                    return true;
            }
        }

        /// <summary>
        /// Tile-entry hook (WD_PathFollower, non-ballistic): hostile ground travelers smash economic buildings;
        /// hostile raids clash with artillery turrets. The traveler's own destination building is left to arrival.
        /// </summary>
        public static void TryResolveWalkOver(WorldObject_Traveler traveler, PlanetTile tile)
        {
            if (traveler == null || traveler.Destroyed || !tile.Valid) return;
            if (traveler.Faction == null) return;
            if (!IsGroundWalkOverMission(traveler.mission)) return;
            WorldObject_WD_AdvancedBuilding b = WorldComponent_AdvancedBuildings.Get()?.At(tile.tileId);
            if (b == null || b.Destroyed || b.Faction == null) return;
            if (traveler.targetObject == b) return;
            if (!WorldActions_Utils.SafeHostileTo(traveler.Faction, b.Faction)) return;

            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (b is WorldObject_WD_ArtilleryTurret art)
            {
                if (!WorldObject_Traveler.IsRaidMission(traveler.mission)) return;
                OpenFieldClashUtility.ResolveTravelerVsArtilleryTurret(traveler, art, manager);
                return;
            }

            DestroyBuilding(b, traveler);
            if (traveler.Faction.IsPlayer)
                NotifyPlayerSmashed(b, traveler);
        }

        /// <summary>Player caravan on a hostile building's tile overruns it (same temporary rule as AT turrets).</summary>
        public static bool TryOverrunByPlayer(int tileId, Faction playerFaction, WorldObject logSubject)
        {
            if (tileId < 0 || playerFaction == null || !playerFaction.IsPlayer) return false;
            WorldObject_WD_AdvancedBuilding b = WorldComponent_AdvancedBuildings.Get()?.At(tileId);
            if (b == null || b.Destroyed || b.Faction == null || b.Faction.IsPlayer) return false;
            if (!WorldActions_Utils.SafeHostileTo(playerFaction, b.Faction)) return false;

            DestroyBuilding(b, logSubject);
            NotifyPlayerSmashed(b, logSubject);
            return true;
        }

        private static void NotifyPlayerSmashed(WorldObject_WD_AdvancedBuilding b, WorldObject attacker)
        {
            TaggedString text = "TSA_WD_AdvBuild_Msg_PlayerDestroyed".Translate(
                LabelWithOwner(b), attacker?.LabelCap ?? "?");
            Messages.Message(text, new GlobalTargetInfo(b.Tile), MessageTypeDefOf.PositiveEvent, false);
        }
    }
}
