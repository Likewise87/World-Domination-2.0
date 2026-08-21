using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    [StaticConstructorOnStartup]
    public static class Action_Settlement_Bribe
    {
        private static Texture2D cachedIcon;

        /// <summary>Softer than full Color.cyan so a white Bribe texture matches the Build hammer brightness.</summary>
        private static readonly Color BribeIconColor = new Color(0.35f, 0.78f, 0.85f);

        public static Texture2D BribeIcon =>
            cachedIcon ??= ContentFinder<Texture2D>.Get("UI/Commands/Bribe", false)
                ?? Action_Settlement_Gift.GiftIcon
                ?? TexCommand.Install;

        public static IEnumerable<Gizmo> GetSettlementGizmos(Settlement settlement)
        {
            if (!SettlementBribeUtility.CanShowSettlementBribeGizmo(settlement, out string disabledReason))
                yield break;

            var bribe = new Command_Action
            {
                defaultLabel = "TSA_WD_Bribe_SettlementGizmoLabel".Translate(),
                defaultDesc = "TSA_WD_Bribe_SettlementGizmoDesc".Translate(),
                icon = BribeIcon,
                defaultIconColor = BribeIconColor,
                action = () => Find.WindowStack.Add(new Dialog_SettlementBribeDeal(settlement))
            };
            if (!disabledReason.NullOrEmpty())
                bribe.Disable(disabledReason);
            yield return bribe;
        }

        public static IEnumerable<Gizmo> GetRaidGizmos(WorldObject_Traveler traveler)
        {
            if (!SettlementBribeUtility.CanShowRaidBribeGizmo(traveler, out string disabledReason))
                yield break;

            var bribe = new Command_Action
            {
                defaultLabel = "TSA_WD_Bribe_RaidGizmoLabel".Translate(),
                defaultDesc = "TSA_WD_Bribe_RaidGizmoDesc".Translate(),
                icon = BribeIcon,
                defaultIconColor = BribeIconColor,
                action = () => Find.WindowStack.Add(new Dialog_SettlementBribeDeal(traveler))
            };
            if (!disabledReason.NullOrEmpty())
                bribe.Disable(disabledReason);
            yield return bribe;
        }
    }

    public static class Patch_SettlementBribeGizmo
    {
        public static IEnumerable<Gizmo> GetGizmos(Settlement settlement)
        {
            foreach (Gizmo gizmo in Action_Settlement_Bribe.GetSettlementGizmos(settlement))
                yield return gizmo;
        }
    }
}
