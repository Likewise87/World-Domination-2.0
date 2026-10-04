using LudeonTK;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Debug click tools that reuse World Setup road/bridge edit utilities.</summary>
    public static class DebugActions_RoadsBridges
    {
        [DebugAction("World Domination", "Destroy road links (Click tile)",
            actionType = DebugActionType.ToolWorld,
            allowedGameStates = AllowedGameStates.PlayingOnWorld)]
        public static void DestroyRoadLinksClick()
        {
            int tile = GenWorld.MouseTile();
            if (tile < 0 || !PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(tile))
            {
                Messages.Message("TSA_WD_WorldSetup_InvalidTile".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            if (!WD_WorldRoadEditUtility.TryRemoveRoadsAtTile(tile, out int removed) || removed <= 0)
            {
                Messages.Message("TSA_WD_WorldSetup_RemoveRoadNone".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            Messages.Message("TSA_WD_WorldSetup_RemoveRoadDone".Translate(removed), MessageTypeDefOf.PositiveEvent);
        }

        [DebugAction("World Domination", "Destroy bridge (Click bank/water)",
            actionType = DebugActionType.ToolWorld,
            allowedGameStates = AllowedGameStates.PlayingOnWorld)]
        public static void DestroyBridgeClick()
        {
            int tile = GenWorld.MouseTile();
            if (tile < 0 || !PlanetSurfaceWorldActions.IsPlanetSurfaceTileForWorldActions(tile))
            {
                Messages.Message("TSA_WD_WorldSetup_InvalidTile".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            if (!WD_WorldRoadEditUtility.TryDestroyBridgeAtTile(tile, out string fail))
            {
                Messages.Message(fail ?? "TSA_WD_WorldSetup_DestroyBridgeNone".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            Messages.Message("TSA_WD_WorldSetup_DestroyBridgeDone".Translate(), MessageTypeDefOf.PositiveEvent);
        }
    }
}
