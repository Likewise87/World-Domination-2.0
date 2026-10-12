using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Selection overlays: affiliation-colored great-circle lines between an NPC settlement
    /// and its owned AT turrets / advanced buildings (player-relative hostile/neutral/ally).
    /// </summary>
    public static class SettlementOwnedAssetSelectionLines
    {
        public static void DrawForSettlement(Settlement owner)
        {
            if (!CanDraw(owner)) return;

            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return;

            Material mat = MaterialForOwner(owner);
            if (mat == null) return;

            float lift = GenDraw_WorldLineSmooth.GetPathLineLift();
            int ownerTile = owner.Tile.tileId;

            List<WorldObject_WD_AdvancedBuilding> buildings =
                WorldComponent_AdvancedBuildings.Get()?.OwnedBy(owner);
            if (buildings != null)
            {
                for (int i = 0; i < buildings.Count; i++)
                {
                    WorldObject_WD_AdvancedBuilding b = buildings[i];
                    if (b == null || b.Destroyed || !b.Spawned) continue;
                    DrawChord(ownerTile, b.Tile.tileId, grid, mat, lift);
                }
            }

            IReadOnlyList<WorldObject_AT_Turret> turrets = WorldObject_AT_Turret.LiveTurrets;
            for (int i = 0; i < turrets.Count; i++)
            {
                WorldObject_AT_Turret t = turrets[i];
                if (t == null || t.Destroyed || !t.Spawned) continue;
                if (t.builtBySettlement != owner) continue;
                DrawChord(ownerTile, t.Tile.tileId, grid, mat, lift);
            }
        }

        public static void DrawToOwner(WorldObject asset, Settlement owner)
        {
            if (asset == null || asset.Destroyed || !asset.Spawned) return;
            if (!CanDraw(owner)) return;

            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return;

            Material mat = MaterialForOwner(owner);
            if (mat == null) return;

            DrawChord(asset.Tile.tileId, owner.Tile.tileId, grid, mat, GenDraw_WorldLineSmooth.GetPathLineLift());
        }

        private static bool CanDraw(Settlement owner)
        {
            if (!TravelerEndpointUtility.IsLiveEndpoint(owner)) return false;
            if (owner.Faction == null || owner.Faction.IsPlayer) return false;
            if (WD_WorldMapZoomUtil.IsZoomedTooFarOut(WD_WorldMapZoomUtil.TravelerPathHideAltitudePercent))
                return false;
            return true;
        }

        private static Material MaterialForOwner(Settlement owner)
        {
            Faction player = Faction.OfPlayerSilentFail;
            if (player == null || owner?.Faction == null) return WorldOverlayLineMaterials.AffiliationNeutral;

            return WorldActions_Utils.SafeRelationKindWith(owner.Faction, player) switch
            {
                FactionRelationKind.Hostile => WorldOverlayLineMaterials.AffiliationHostile,
                FactionRelationKind.Ally => WorldOverlayLineMaterials.AffiliationAlly,
                _ => WorldOverlayLineMaterials.AffiliationNeutral
            };
        }

        private static void DrawChord(int fromTile, int toTile, WorldGrid grid, Material mat, float lift)
        {
            if (fromTile < 0 || toTile < 0 || fromTile == toTile) return;
            GenDraw_WorldLineSmooth.DrawSmoothWorldLine(fromTile, toTile, grid, mat, 1f, lift);
        }
    }
}
