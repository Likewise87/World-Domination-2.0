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
    /// Adds tile-first remote establish (type dialog then same-origin pawn picker). Colony map still required to pay costs.
    /// </summary>
    [StaticConstructorOnStartup]
    [HarmonyPatch(typeof(Tile), nameof(Tile.GetGizmos))]
    public static class Patch_Tile_EstablishOutpostGizmo
    {
        private static Texture2D cachedEstablishIcon;

        private const int TipCacheLifetimeTicks = 30;
        private static int tipCacheTileId = -1;
        private static int tipCacheTick = -99999;
        private static bool tipCacheOccupied;
        private static bool tipCacheActiveCamp;
        private static bool tipCacheMeetsMin;
        private static bool tipCacheHasColony;
        private static string tipCacheTooltip;
        private static string tipCacheDisableReason;
        private static string tipCacheMinRadiusReason;

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

            EnsureTipCache(tile);

            var cmd = new Command_Action
            {
                defaultLabel = "TSA_WD_TileFirstEstablish_Gizmo".Translate(),
                defaultDesc = tipCacheTooltip,
                icon = cachedEstablishIcon ??= ContentFinder<Texture2D>.Get("UI/Commands/EstablishOutpost", false)
                    ?? ContentFinder<Texture2D>.Get("UI/Commands/Settle", false)
                    ?? TexCommand.Replant,
                defaultIconColor = WorldOverlayLineMaterials.DarkCyanColor,
                action = () => OpenTileFirstDialog(tile)
            };

            if (!string.IsNullOrEmpty(tipCacheDisableReason))
                cmd.Disable(tipCacheDisableReason);

            yield return cmd;
        }

        private static void EnsureTipCache(int tile)
        {
            int tick = Find.TickManager?.TicksGame ?? 0;
            if (tile == tipCacheTileId
                && tick - tipCacheTick < TipCacheLifetimeTicks
                && tipCacheTooltip != null)
            {
                return;
            }

            tipCacheTileId = tile;
            tipCacheTick = tick;

            tipCacheOccupied = IsOccupiedBySettlementOrWdOutpost(tile);
            tipCacheActiveCamp = Outpost_EstablishmentRequirements.TileHasActiveCamp(tile);
            tipCacheMeetsMin = Outpost_EstablishmentRequirements.MeetsMinDistanceOnly(tile, out tipCacheMinRadiusReason);
            tipCacheHasColony = Outpost_PowerPlant.GetPlayerColonyMap() != null;

            string tooltip = "TSA_WD_TileFirstEstablish_GizmoTip".Translate(
                Outpost_EstablishmentRequirements.MinDistanceTiles).ToString();
            if (tipCacheOccupied)
                tooltip = "TSA_WD_TileFirstEstablish_Occupied".Translate() + "\n\n" + tooltip;
            else if (tipCacheActiveCamp)
                tooltip = "TSA_WD_Establish_ActiveCamp".Translate() + "\n\n" + tooltip;
            else if (!tipCacheMeetsMin)
                tooltip = (tipCacheMinRadiusReason ?? "TSA_WD_Establish_TooClose".Translate(
                    Outpost_EstablishmentRequirements.MinDistanceTiles, "?").ToString()) + "\n\n" + tooltip;
            else if (!tipCacheHasColony)
                tooltip = "TSA_WD_TileFirstEstablish_NoColony".Translate() + "\n\n" + tooltip;
            tipCacheTooltip = tooltip.TrimStart();

            if (tipCacheOccupied)
                tipCacheDisableReason = "TSA_WD_TileFirstEstablish_Occupied".Translate();
            else if (tipCacheActiveCamp)
                tipCacheDisableReason = "TSA_WD_Establish_ActiveCamp".Translate();
            else if (!tipCacheMeetsMin)
                tipCacheDisableReason = tipCacheMinRadiusReason ?? "TSA_WD_Establish_TooClose".Translate(
                    Outpost_EstablishmentRequirements.MinDistanceTiles, "?").ToString();
            else if (!tipCacheHasColony)
                tipCacheDisableReason = "TSA_WD_TileFirstEstablish_NoColony".Translate();
            else
                tipCacheDisableReason = null;
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
                remoteEstablishOrigin: null,
                tileFirstRemoteEstablish: true));
        }
    }
}
