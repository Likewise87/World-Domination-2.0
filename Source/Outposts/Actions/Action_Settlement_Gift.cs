using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    [StaticConstructorOnStartup]
    public static class Action_Settlement_Gift
    {
        private static Texture2D cachedIcon;

        public static Texture2D GiftIcon =>
            cachedIcon ??= ContentFinder<Texture2D>.Get("UI/Commands/OfferGifts", false)
                ?? ContentFinder<Texture2D>.Get("UI/Commands/Trade", false)
                ?? TexCommand.Install;

        public static IEnumerable<Gizmo> GetGizmos(Settlement settlement)
        {
            if (!SettlementGiftUtility.CanShowGiftGizmo(settlement, out string disabledReason))
                yield break;

            var gift = new Command_Action
            {
                defaultLabel = "TSA_WD_GiftSettlement_GizmoLabel".Translate(),
                defaultDesc = "TSA_WD_GiftSettlement_GizmoDesc".Translate(),
                icon = GiftIcon,
                action = () => Find.WindowStack.Add(new Dialog_SettlementGiftDeal(settlement))
            };
            if (!disabledReason.NullOrEmpty())
                gift.Disable(disabledReason);
            yield return gift;
        }
    }
}
