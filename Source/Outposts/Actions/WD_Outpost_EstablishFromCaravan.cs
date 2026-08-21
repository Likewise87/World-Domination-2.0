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

        public static IEnumerable<Gizmo> GetGizmos(Caravan caravan)
        {
            if (caravan == null || caravan.Destroyed) yield break;
            if (caravan.Faction != Faction.OfPlayer) yield break;

            var pawnsList = caravan.PawnsListForReading;
            if (pawnsList == null || pawnsList.Count == 0) yield break;
            var humanlike = new List<Pawn>();
            for (int i = 0; i < pawnsList.Count; i++)
            {
                var p = pawnsList[i];
                if (p?.RaceProps?.Humanlike == true && !p.Dead) humanlike.Add(p);
            }
            if (humanlike.Count == 0) yield break;

            bool hasOutpostHere = false;
            foreach (var o in Find.WorldObjects.ObjectsAt(caravan.Tile))
            {
                if (o.Faction == Faction.OfPlayer && o is WorldObject_WD_Outpost)
                {
                    hasOutpostHere = true;
                    break;
                }
            }
            if (hasOutpostHere) yield break;

            int tileId = caravan.Tile.tileId;
            bool meetsMinRadius = Outpost_EstablishmentRequirements.MeetsMinDistanceOnly(tileId, out string minRadiusReason);
            int minTiles = Outpost_EstablishmentRequirements.MinDistanceTiles;
            bool caravanStopped = Outpost_EstablishmentRequirements.CaravanFullyStoppedOnTileForEstablishment(caravan, tileId, out string stoppedReason);
            bool activeCamp = Outpost_EstablishmentRequirements.TileHasActiveCamp(tileId);

            SettlementTier tierFromCount = humanlike.Count >= 20 ? SettlementTier.T4
                : humanlike.Count >= 12 ? SettlementTier.T3
                : humanlike.Count >= 7 ? SettlementTier.T2
                : SettlementTier.T1;

            string defaultName = "TSA_WD_OutpostDefaultName".Translate(caravan.Tile).ToString();
            if (string.IsNullOrEmpty(defaultName)) defaultName = "Outpost";

            string tooltip = "TSA_WD_EstablishOutpostTooltip".Translate(minTiles).ToString();
            if (activeCamp)
                tooltip = "TSA_WD_Establish_ActiveCamp".Translate() + "\n\n" + tooltip;
            if (!meetsMinRadius)
                tooltip = (minRadiusReason ?? "TSA_WD_Establish_TooClose".Translate(minTiles, "?").ToString()) + "\n\n" + tooltip;
            if (!caravanStopped)
                tooltip = (stoppedReason ?? "") + "\n\n" + tooltip;

            var cmd = new Command_EstablishOutpost
            {
                defaultLabel = "TSA_WD_EstablishOutpost".Translate(),
                defaultDesc = tooltip.TrimStart(),
                icon = cachedEstablishIcon ??= ContentFinder<Texture2D>.Get("UI/Commands/EstablishOutpost", false) ?? ContentFinder<Texture2D>.Get("UI/Commands/Settle", false) ?? TexCommand.Replant,
                meetsMinRadius = meetsMinRadius && caravanStopped && !activeCamp,
                tile = tileId,
                defaultName = defaultName,
                tierFromCount = tierFromCount,
                fromCaravan = caravan
            };
            if (activeCamp)
                cmd.Disable("TSA_WD_Establish_ActiveCamp".Translate());
            else if (!caravanStopped)
                cmd.Disable(stoppedReason);
            else if (!meetsMinRadius)
                cmd.Disable(minRadiusReason);
            yield return cmd;
        }
    }

}
