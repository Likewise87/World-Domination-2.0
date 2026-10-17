using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    [StaticConstructorOnStartup]
    public static class Action_Outpost_ChangeType
    {
        private static Texture2D cachedIcon;

        private static Texture2D Icon =>
            cachedIcon ??= ContentFinder<Texture2D>.Get("UI/Commands/EstablishOutpost", false)
                ?? ContentFinder<Texture2D>.Get("UI/Commands/Settle", false)
                ?? TexCommand.Install;

        public static IEnumerable<Gizmo> GetGizmos(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null || outpost.Faction != Faction.OfPlayer) yield break;

            var cmd = new Command_Action
            {
                defaultLabel = "TSA_WD_ChangeOutpostType".Translate(),
                defaultDesc = "TSA_WD_ChangeOutpostTypeTip".Translate(),
                icon = Icon,
                defaultIconColor = WorldOverlayLineMaterials.DarkCyanColor,
                action = () => Find.WindowStack.Add(new Dialog_OutpostSelection(outpost))
            };

            if (outpost.ManualDefenseActive || WD_MapComponent_OutpostDefense.HasActiveEncounterFor(outpost))
                cmd.Disable("TSA_WD_OutpostDefense_FrozenDuringManualDefense".Translate());
            else if (!string.IsNullOrEmpty(outpost.PendingTypeChangeDefName))
                cmd.Disable("TSA_WD_TypeChange_AlreadyPending".Translate());
            else if (!string.IsNullOrEmpty(outpost.PendingUpgradeDefName))
                cmd.Disable("TSA_WD_OutpostUpgrades_InTransit".Translate());

            yield return cmd;
        }
    }
}
