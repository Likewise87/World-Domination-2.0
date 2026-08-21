using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Empty Surface tiles expose gizmos via <see cref="Tile.GetGizmos"/>.
    /// Adds tile-first remote establish (type dialog then colony pawn picker).
    /// </summary>
    [StaticConstructorOnStartup]
    [HarmonyPatch(typeof(Tile), nameof(Tile.GetGizmos))]
    public static class Patch_Tile_EstablishOutpostGizmo
    {
        private static Texture2D cachedEstablishIcon;

        [HarmonyPostfix]
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Tile __instance)
        {
            if (__result != null)
            {
                foreach (Gizmo g in __result)
                    yield return g;
            }

            if (__instance == null) yield break;
            if (Current.ProgramState != ProgramState.Playing) yield break;

            PlanetTile planetTile = __instance.tile;
            if (!PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(planetTile))
                yield break;

            int tile = planetTile.tileId;
            if (tile < 0) yield break;

            bool occupied = IsOccupiedBySettlementOrWdOutpost(tile);
            bool activeCamp = Outpost_EstablishmentRequirements.TileHasActiveCamp(tile);
            bool meetsMinRadius = Outpost_EstablishmentRequirements.MeetsMinDistanceOnly(tile, out string minRadiusReason);
            Map colonyMap = Outpost_PowerPlant.GetPlayerColonyMap();
            bool hasColony = colonyMap != null;

            string tooltip = "TSA_WD_TileFirstEstablish_GizmoTip".Translate(
                Outpost_EstablishmentRequirements.MinDistanceTiles).ToString();
            if (occupied)
                tooltip = "TSA_WD_TileFirstEstablish_Occupied".Translate() + "\n\n" + tooltip;
            else if (activeCamp)
                tooltip = "TSA_WD_Establish_ActiveCamp".Translate() + "\n\n" + tooltip;
            else if (!meetsMinRadius)
                tooltip = (minRadiusReason ?? "TSA_WD_Establish_TooClose".Translate(
                    Outpost_EstablishmentRequirements.MinDistanceTiles, "?").ToString()) + "\n\n" + tooltip;
            else if (!hasColony)
                tooltip = "TSA_WD_TileFirstEstablish_NoColony".Translate() + "\n\n" + tooltip;

            var cmd = new Command_Action
            {
                defaultLabel = "TSA_WD_TileFirstEstablish_Gizmo".Translate(),
                defaultDesc = tooltip.TrimStart(),
                icon = cachedEstablishIcon ??= ContentFinder<Texture2D>.Get("UI/Commands/EstablishOutpost", false)
                    ?? ContentFinder<Texture2D>.Get("UI/Commands/Settle", false)
                    ?? TexCommand.Replant,
                action = () => OpenTileFirstDialog(tile)
            };

            if (occupied)
                cmd.Disable("TSA_WD_TileFirstEstablish_Occupied".Translate());
            else if (activeCamp)
                cmd.Disable("TSA_WD_Establish_ActiveCamp".Translate());
            else if (!meetsMinRadius)
                cmd.Disable(minRadiusReason ?? "TSA_WD_Establish_TooClose".Translate(
                    Outpost_EstablishmentRequirements.MinDistanceTiles, "?").ToString());
            else if (!hasColony)
                cmd.Disable("TSA_WD_TileFirstEstablish_NoColony".Translate());

            yield return cmd;
        }

        private static bool IsOccupiedBySettlementOrWdOutpost(int tile)
        {
            foreach (WorldObject o in Find.WorldObjects.ObjectsAt(tile))
            {
                if (o is Settlement || o is WorldObject_WD_Outpost)
                    return true;
            }
            return false;
        }

        private static void OpenTileFirstDialog(int tile)
        {
            if (Dialog_OutpostSelection.IsEstablishmentPreviewOverlayActive)
                Dialog_OutpostSelection.SetEstablishmentPreviewOverlayActive(false);
            if (WorldComponent_WDVisualizerToggle.IsWorldTargeterActive())
                Find.WorldTargeter.StopTargeting();
            RemoteOutpostEstablishSession.Clear();

            Find.WindowStack.Add(new Dialog_OutpostSelection(
                tile,
                "",
                -1,
                SettlementTier.T1,
                null,
                fromCaravan: null,
                requirementsPreviewOnly: false,
                remoteEstablishEntries: null,
                remoteEstablishSource: null,
                tileFirstRemoteEstablish: true));
        }
    }
}
