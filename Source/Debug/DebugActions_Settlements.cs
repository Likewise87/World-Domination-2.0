using System;
using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public static class DebugActions_Settlements
    {
        [DebugAction("World Domination", "Spawn WD Settlement (multi-place)...",
            allowedGameStates = AllowedGameStates.PlayingOnWorld)]
        public static void SpawnWdSettlementMultiPlace()
        {
            var tierOpts = new List<FloatMenuOption>();
            foreach (SettlementTier tier in Enum.GetValues(typeof(SettlementTier)))
            {
                SettlementTier captured = tier;
                tierOpts.Add(new FloatMenuOption(
                    captured.ToString(),
                    () => OpenFactionMenuForSpawn(captured)));
            }
            OpenCenteredFloatMenu(tierOpts);
        }

        private static void OpenFactionMenuForSpawn(SettlementTier tier)
        {
            var factionOpts = new List<FloatMenuOption>();
            var factions = Find.FactionManager?.AllFactionsListForReading;
            if (factions == null || factions.Count == 0)
            {
                Messages.Message("WD debug: no factions available.", MessageTypeDefOf.RejectInput);
                return;
            }

            for (int i = 0; i < factions.Count; i++)
            {
                Faction f = factions[i];
                if (f == null || f.IsPlayer || f.defeated || f.def == null || f.def.hidden)
                    continue;
                if (WorldActions_Utils.IsExcludedFaction(f))
                    continue;

                Faction captured = f;
                Texture2D icon = captured.def.FactionIcon;
                factionOpts.Add(new FloatMenuOption(
                    captured.Name,
                    () => BeginSpawnSettlementTargeting(tier, captured),
                    icon,
                    captured.Color));
            }

            if (factionOpts.Count == 0)
            {
                Messages.Message("WD debug: no placeable NPC factions found.", MessageTypeDefOf.RejectInput);
                return;
            }

            OpenCenteredFloatMenu(factionOpts);
        }

        private static void OpenCenteredFloatMenu(List<FloatMenuOption> options) =>
            DebugActions_FloatMenus.OpenCentered(options);

        private static void BeginSpawnSettlementTargeting(SettlementTier tier, Faction faction)
        {
            Messages.Message(
                $"WD debug: click tiles to place {tier} settlements for {faction.Name}. Right-click or Esc to stop.",
                MessageTypeDefOf.NeutralEvent);

            Find.WorldTargeter.BeginTargeting(
                (GlobalTargetInfo target) =>
                {
                    // Return false so targeting stays active for multi-place (same as roadblock debug).
                    if (!TryGetValidDebugSettlementTile(target, out int tile))
                        return false;

                    if (!TrySpawnWdSettlementAt(tile, tier, faction, out string fail))
                    {
                        Messages.Message(fail ?? "WD debug: spawn failed.", MessageTypeDefOf.RejectInput);
                        return false;
                    }

                    Messages.Message(
                        $"WD debug: placed {tier} {faction.Name} settlement on tile {tile}.",
                        MessageTypeDefOf.PositiveEvent);
                    return false;
                },
                true,
                null,
                false,
                null,
                null,
                t => TryGetValidDebugSettlementTile(t, out _));
        }

        private static bool TryGetValidDebugSettlementTile(GlobalTargetInfo target, out int tile) =>
            WD_SettlementEditUtility.TryGetValidSettlementTile(target, out tile, enforceMinDistance: false, out _);

        private static bool TrySpawnWdSettlementAt(int tile, SettlementTier tier, Faction faction, out string failReason) =>
            WD_SettlementEditUtility.TrySpawnWdSettlementAt(tile, tier, faction, out failReason, enforceMinDistance: false);

        [DebugAction("World Domination", "Upgrade Settlement Tier (Click)",
            actionType = DebugActionType.ToolWorld,
            allowedGameStates = AllowedGameStates.PlayingOnWorld)]
        public static void UpgradeSettlementTier()
        {
            int tile = GenWorld.MouseTile();
            WorldObject obj = Find.WorldObjects.ObjectsAt(tile).FirstOrDefault(x => x is Settlement || x is WorldObject_WD_Outpost);

            if (obj == null) return;

            var comp = obj.GetComponent<CompViralSpread>();
            if (comp == null) return;

            if (comp.tier == SettlementTier.T1) comp.SetState(SettlementTier.T2);
            else if (comp.tier == SettlementTier.T2) comp.SetState(SettlementTier.T3);
            else if (comp.tier == SettlementTier.T3) comp.SetState(SettlementTier.T4);

            comp.strength = CompViralSpread.GetStrengthRange(comp.tier).min;

            Messages.Message($"Debug: {obj.LabelCap} upgraded to {comp.tier}.", MessageTypeDefOf.CautionInput);
            Find.World.GetComponent<WorldComponent_SpreadManager>()?.Notify_WeightsChanged();
        }

        [DebugAction("World Domination", "Downgrade Settlement Tier (Click)",
            actionType = DebugActionType.ToolWorld,
            allowedGameStates = AllowedGameStates.PlayingOnWorld)]
        public static void DowngradeSettlementTier()
        {
            int tile = GenWorld.MouseTile();
            WorldObject obj = Find.WorldObjects.ObjectsAt(tile).FirstOrDefault(x => x is Settlement || x is WorldObject_WD_Outpost);

            if (obj == null) return;

            var comp = obj.GetComponent<CompViralSpread>();
            if (comp == null) return;

            if (comp.tier == SettlementTier.T4) comp.SetState(SettlementTier.T3);
            else if (comp.tier == SettlementTier.T3) comp.SetState(SettlementTier.T2);
            else if (comp.tier == SettlementTier.T2) comp.SetState(SettlementTier.T1);

            comp.strength = CompViralSpread.GetStrengthRange(comp.tier).min;

            Messages.Message($"Debug: {obj.LabelCap} downgraded to {comp.tier}.", MessageTypeDefOf.CautionInput);
            Find.World.GetComponent<WorldComponent_SpreadManager>()?.Notify_WeightsChanged();
        }

        [DebugAction("World Domination", "Increase Strength +100",
            actionType = DebugActionType.ToolWorld,
            allowedGameStates = AllowedGameStates.PlayingOnWorld)]
        public static void DebugStrengthPlus100()
        {
            int tile = GenWorld.MouseTile();
            WorldObject obj = Find.WorldObjects.ObjectsAt(tile).FirstOrDefault(x => x is Settlement || x is WorldObject_WD_Outpost);
            if (obj == null) return;
            var comp = obj.GetComponent<CompViralSpread>();
            if (comp == null) return;
            comp.AdjustStrengthWithinTier(100f);
            Messages.Message($"Debug: {obj.LabelCap} strength → {comp.strength:F0} (tier {comp.tier}, no tier change).", MessageTypeDefOf.CautionInput);
            Find.World.GetComponent<WorldComponent_SpreadManager>()?.Notify_WeightsChanged();
        }

        [DebugAction("World Domination", "Decrease Strength -100",
            actionType = DebugActionType.ToolWorld,
            allowedGameStates = AllowedGameStates.PlayingOnWorld)]
        public static void DebugStrengthMinus100()
        {
            int tile = GenWorld.MouseTile();
            WorldObject obj = Find.WorldObjects.ObjectsAt(tile).FirstOrDefault(x => x is Settlement || x is WorldObject_WD_Outpost);
            if (obj == null) return;
            var comp = obj.GetComponent<CompViralSpread>();
            if (comp == null) return;
            comp.AdjustStrengthWithinTier(-100f);
            Messages.Message($"Debug: {obj.LabelCap} strength → {comp.strength:F0} (tier {comp.tier}, no tier change).", MessageTypeDefOf.CautionInput);
            Find.World.GetComponent<WorldComponent_SpreadManager>()?.Notify_WeightsChanged();
        }

        /// <summary>
        /// Force this NPC settlement to pick the player colony, then run experimental local-executor handoff
        /// (same faction, closer base launches). Requires experimental weighted target bands.
        /// </summary>
        [DebugAction("World Domination", "Force colony raid + executor handoff (Click NPC)",
            actionType = DebugActionType.ToolWorld,
            allowedGameStates = AllowedGameStates.PlayingOnWorld)]
        public static void DebugForceColonyRaidExecutorHandoff()
        {
            int tile = GenWorld.MouseTile();
            if (tile < 0) return;

            Settlement settlement = Find.WorldObjects.ObjectsAt(tile).OfType<Settlement>().FirstOrDefault();
            if (settlement == null)
            {
                Messages.Message("WD debug: click an NPC settlement.", MessageTypeDefOf.RejectInput);
                return;
            }

            if (!WorldActions_Raid.DebugForceColonyRaidForExecutorHandoff(settlement, out string failReason, out bool handedOff))
            {
                Messages.Message(
                    "WD debug colony executor raid failed (" + settlement.LabelCap + "): " + (failReason ?? "unknown"),
                    MessageTypeDefOf.RejectInput);
                return;
            }

            if (handedOff)
            {
                Messages.Message(
                    "WD debug: " + settlement.LabelCap + " picked colony and delegated to a closer ally (see action log).",
                    MessageTypeDefOf.PositiveEvent);
            }
            else
            {
                Messages.Message(
                    "WD debug: " + settlement.LabelCap + " launched at colony with no handoff (no closer eligible same-faction executor).",
                    MessageTypeDefOf.CautionInput);
            }
        }

        /// <summary>Watch-index build/perf inspection for target-of-opportunity/marauding/ambush/Feature D tuning.</summary>
        [DebugAction("World Domination", "Force-Rebuild Settlement Watch Index",
            allowedGameStates = AllowedGameStates.PlayingOnWorld)]
        public static void ForceRebuildSettlementWatchIndex()
        {
            var idx = WorldComponent_SettlementWatchIndex.Get();
            if (idx == null)
            {
                Messages.Message("WD debug: no SettlementWatchIndex on the current world.", MessageTypeDefOf.RejectInput);
                return;
            }
            string summary = idx.DebugForceRebuildAndSummarize();
            Log.Message($"[TSA WD] SettlementWatchIndex debug rebuild: {summary}");
            Messages.Message($"WD debug: watch index rebuilt ({summary})", MessageTypeDefOf.NeutralEvent);
        }
    }
}
