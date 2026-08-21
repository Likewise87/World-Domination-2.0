using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Player colony toggle: auto-convert map food to travel pemmican for transferred pawns.</summary>
    [StaticConstructorOnStartup]
    public static class Action_Settlement_AutoTravelFood
    {
        private static Texture2D cachedConvertFoodIcon;

        public static Texture2D ConvertFoodIcon =>
            cachedConvertFoodIcon ??= ContentFinder<Texture2D>.Get("UI/Commands/ConvertFood", false) ?? TexCommand.Replant;

        public static IEnumerable<Gizmo> GetGizmos(Settlement settlement)
        {
            if (settlement == null || settlement.Destroyed) yield break;
            if (settlement.Faction == null || !settlement.Faction.IsPlayer) yield break;
            if (!settlement.HasMap) yield break;

            CompViralSpread comp = settlement.GetComponent<CompViralSpread>();
            if (comp == null) yield break;

            yield return new Command_Toggle
            {
                defaultLabel = "TSA_WD_ColonyAutoTravelFood_Label".Translate(),
                defaultDesc = "TSA_WD_ColonyAutoTravelFood_Desc".Translate(),
                icon = ConvertFoodIcon,
                isActive = () => comp.autoFeedTransferredPawns,
                toggleAction = () => comp.autoFeedTransferredPawns = !comp.autoFeedTransferredPawns
            };
        }
    }
}
