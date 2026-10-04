using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Command that opens outpost selection. Greyed out only when min distance to settlements/outposts/colonies is not met; other checks are done in the dialog.</summary>
    public class Command_EstablishOutpost : Command_Action
    {
        public bool meetsMinRadius;
        public int tile;
        public string defaultName;
        public SettlementTier tierFromCount;
        public Caravan fromCaravan;

        public override void ProcessInput(UnityEngine.Event ev)
        {
            if (!meetsMinRadius || Disabled) return;
            if (fromCaravan != null && !Outpost_EstablishmentRequirements.CaravanFullyStoppedOnTileForEstablishment(fromCaravan, tile, out string stopReason))
            {
                Messages.Message(stopReason ?? "", MessageTypeDefOf.RejectInput, false);
                return;
            }
            Find.WindowStack.Add(new Dialog_OutpostSelection(tile, defaultName, ruinsId: -1, tierFromCount, conquestContext: null, fromCaravan: fromCaravan));
        }
    }

    /// <summary>Adds "Establish outpost" to player caravans on the world map so they can found a TSA outpost at the current tile (using caravan pawns as virtual outpost pawns).</summary>
    [StaticConstructorOnStartup]
    public static class Patch_CaravanFoundOutpostGizmo
    {
        private static Texture2D cachedEstablishIcon;

        private const int TipCacheLifetimeTicks = 30;
        private static int tipCacheCaravanId = -1;
        private static int tipCacheTileId = -1;
        private static int tipCacheTick = -99999;
        private static int tipCacheHumanlikeCount = -1;
        private static bool tipCacheHasOutpostHere;
        private static bool tipCacheMeetsMin;
        private static bool tipCacheStopped;
        private static bool tipCacheActiveCamp;
        private static string tipCacheTooltip;
        private static string tipCacheDisableReason;
        private static string tipCacheDefaultName;
        private static SettlementTier tipCacheTier;

        public static IEnumerable<Gizmo> GetGizmos(Caravan caravan)
        {
            if (caravan == null || caravan.Destroyed) yield break;
            if (caravan.Faction != Faction.OfPlayer) yield break;

            var pawnsList = caravan.PawnsListForReading;
            if (pawnsList == null || pawnsList.Count == 0) yield break;

            int humanlikeCount = 0;
            for (int i = 0; i < pawnsList.Count; i++)
            {
                var p = pawnsList[i];
                if (p?.RaceProps?.Humanlike == true && !p.Dead) humanlikeCount++;
            }
            if (humanlikeCount == 0) yield break;

            EnsureTipCache(caravan, humanlikeCount);
            if (tipCacheHasOutpostHere) yield break;

            var cmd = new Command_EstablishOutpost
            {
                defaultLabel = "TSA_WD_EstablishOutpost".Translate(),
                defaultDesc = tipCacheTooltip,
                icon = cachedEstablishIcon ??= ContentFinder<Texture2D>.Get("UI/Commands/EstablishOutpost", false) ?? ContentFinder<Texture2D>.Get("UI/Commands/Settle", false) ?? TexCommand.Replant,
                defaultIconColor = WorldOverlayLineMaterials.DarkCyanColor,
                meetsMinRadius = tipCacheMeetsMin && tipCacheStopped && !tipCacheActiveCamp,
                tile = tipCacheTileId,
                defaultName = tipCacheDefaultName,
                tierFromCount = tipCacheTier,
                fromCaravan = caravan
            };
            if (!string.IsNullOrEmpty(tipCacheDisableReason))
                cmd.Disable(tipCacheDisableReason);
            yield return cmd;
        }

        private static void EnsureTipCache(Caravan caravan, int humanlikeCount)
        {
            int tick = Find.TickManager?.TicksGame ?? 0;
            int caravanId = caravan.ID;
            int tileId = caravan.Tile.tileId;
            if (caravanId == tipCacheCaravanId
                && tileId == tipCacheTileId
                && humanlikeCount == tipCacheHumanlikeCount
                && tick - tipCacheTick < TipCacheLifetimeTicks
                && tipCacheTooltip != null)
            {
                return;
            }

            tipCacheCaravanId = caravanId;
            tipCacheTileId = tileId;
            tipCacheHumanlikeCount = humanlikeCount;
            tipCacheTick = tick;

            tipCacheHasOutpostHere = false;
            foreach (var o in Find.WorldObjects.ObjectsAt(caravan.Tile))
            {
                if (o.Faction == Faction.OfPlayer && o is WorldObject_WD_Outpost)
                {
                    tipCacheHasOutpostHere = true;
                    tipCacheTooltip = "";
                    tipCacheDisableReason = null;
                    return;
                }
            }

            bool meetsMinRadius = Outpost_EstablishmentRequirements.MeetsMinDistanceOnly(tileId, out string minRadiusReason);
            int minTiles = Outpost_EstablishmentRequirements.MinDistanceTiles;
            bool caravanStopped = Outpost_EstablishmentRequirements.CaravanFullyStoppedOnTileForEstablishment(caravan, tileId, out string stoppedReason);
            bool activeCamp = Outpost_EstablishmentRequirements.TileHasActiveCamp(tileId);

            tipCacheMeetsMin = meetsMinRadius;
            tipCacheStopped = caravanStopped;
            tipCacheActiveCamp = activeCamp;
            tipCacheTier = humanlikeCount >= 20 ? SettlementTier.T4
                : humanlikeCount >= 12 ? SettlementTier.T3
                : humanlikeCount >= 7 ? SettlementTier.T2
                : SettlementTier.T1;

            tipCacheDefaultName = "TSA_WD_OutpostDefaultName".Translate(caravan.Tile).ToString();
            if (string.IsNullOrEmpty(tipCacheDefaultName)) tipCacheDefaultName = "Outpost";

            string tooltip = "TSA_WD_EstablishOutpostTooltip".Translate(minTiles).ToString();
            if (activeCamp)
                tooltip = "TSA_WD_Establish_ActiveCamp".Translate() + "\n\n" + tooltip;
            if (!meetsMinRadius)
                tooltip = (minRadiusReason ?? "TSA_WD_Establish_TooClose".Translate(minTiles, "?").ToString()) + "\n\n" + tooltip;
            if (!caravanStopped)
                tooltip = (stoppedReason ?? "") + "\n\n" + tooltip;
            tipCacheTooltip = tooltip.TrimStart();

            if (activeCamp)
                tipCacheDisableReason = "TSA_WD_Establish_ActiveCamp".Translate();
            else if (!caravanStopped)
                tipCacheDisableReason = stoppedReason;
            else if (!meetsMinRadius)
                tipCacheDisableReason = minRadiusReason;
            else
                tipCacheDisableReason = null;
        }
    }

}
