using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Converts silver-equivalent payment/gift value into nearby same-faction settlement strength.
    /// Repeatedly fills towns to their current tier max (nearest / preferred first within radius),
    /// then spends leftover silver on tier-ups, then fills again, until the budget cannot buy more.
    /// </summary>
    public static class FactionSettlementInvestment
    {
        private const float CapEpsilon = 0.01f;

        public struct AwardResult
        {
            public float SilverBudget;
            public float SilverSpentOnStrength;
            public float SilverSpentOnUpgrades;
            public int SettlementsStrengthened;
            public int SettlementsUpgraded;
            public int SettlementsUpgradeFailed;
            public bool HadCandidates;
            public bool Applied;
            /// <summary>True when candidates existed but every town was already at T4 max (or budget could not buy any fill/upgrade).</summary>
            public bool FullyCapped;
            /// <summary>Player-facing detail lines (loot letter); null when unused.</summary>
            public List<string> DetailLines;
        }

        public enum NotifyKind
        {
            None,
            Buy,
            Gift,
            Loot,
            Bribe
        }

        private struct Candidate
        {
            public Settlement Settlement;
            public CompViralSpread Comp;
            public int Distance;
            public bool PreferFirst;
        }

        public static AwardResult AwardFromSilverBudget(
            Faction faction,
            int originTile,
            float silverBudget,
            Settlement? preferFirst = null,
            NotifyKind notify = NotifyKind.None,
            int? radiusOverride = null)
        {
            var result = new AwardResult { SilverBudget = Mathf.Max(0f, silverBudget) };
            var s = WorldDominationMod.settings;
            if (s != null && !s.enableFactionSettlementInvestment)
            {
                if (ShouldLogInvestmentDev(notify))
                    Action_Settlement_Buy.LogInvestmentDevConsole(faction, originTile, result, null, "faction settlement investment disabled", notify);
                return result;
            }
            if (faction == null || faction.IsPlayer || originTile < 0 || result.SilverBudget <= 0f)
            {
                if (ShouldLogInvestmentDev(notify))
                    Action_Settlement_Buy.LogInvestmentDevConsole(faction, originTile, result, null, "invalid faction, tile, or zero silver budget", notify);
                return result;
            }
            if (Find.WorldGrid == null || Find.WorldObjects?.Settlements == null)
            {
                if (ShouldLogInvestmentDev(notify))
                    Action_Settlement_Buy.LogInvestmentDevConsole(faction, originTile, result, null, "world grid/settlements unavailable", notify);
                return result;
            }

            int radius = radiusOverride
                ?? s?.factionInvestmentRadiusTiles
                ?? WorldDominationSettings.DefFactionInvestmentRadiusTiles;
            radius = Mathf.Max(0, radius);
            float per100 = s?.factionInvestmentStrengthPer100Silver
                ?? WorldDominationSettings.DefFactionInvestmentStrengthPer100Silver;
            if (per100 <= 0f)
            {
                if (ShouldLogInvestmentDev(notify))
                    Action_Settlement_Buy.LogInvestmentDevConsole(faction, originTile, result, null, "strength-per-100-silver is 0", notify);
                return result;
            }

            var candidates = CollectCandidates(faction, originTile, radius, preferFirst);
            result.HadCandidates = candidates.Count > 0;
            if (candidates.Count == 0)
            {
                LogNoneInRange(faction, result.SilverBudget);
                if (ShouldLogInvestmentDev(notify))
                    Action_Settlement_Buy.LogInvestmentDevConsole(faction, originTile, result, null,
                        $"no same-faction settlements in range ({radius} tiles)", notify);
                return result;
            }

            float silverLeft = result.SilverBudget;
            var strengthBySettlementId = new Dictionary<int, float>();
            var upgradeBySettlementId = new Dictionary<int, string>();
            var settlementById = new Dictionary<int, Settlement>();
            var strengthenedIds = new HashSet<int>();
            var upgradedIds = new HashSet<int>();
            var upgradeFailedIds = new HashSet<int>();
            var failedUpgradeLines = new List<string>();

            // Fill → upgrade → fill again until the budget cannot buy more room or tier-ups.
            const int maxRounds = 64;
            for (int round = 0; round < maxRounds && silverLeft > 0.01f; round++)
            {
                bool progress = false;

                for (int i = 0; i < candidates.Count && silverLeft > 0.01f; i++)
                {
                    CompViralSpread comp = candidates[i].Comp;
                    Settlement settlement = candidates[i].Settlement;
                    if (comp == null || settlement == null) continue;
                    float max = CompViralSpread.GetStrengthRange(comp.tier).max;
                    float room = Mathf.Max(0f, max - comp.offensiveStrength);
                    if (room <= CapEpsilon) continue;

                    float needSilver = StrengthToSilver(room, per100);
                    float spend = Mathf.Min(needSilver, silverLeft);
                    float gain = SilverToStrength(spend, per100);
                    if (gain <= 0f) continue;

                    float before = comp.offensiveStrength;
                    comp.AddStrengthNoTierUpgrade(gain);
                    float after = comp.offensiveStrength;
                    float actual = Mathf.Max(0f, after - before);
                    if (actual <= 0f) continue;

                    float spent = StrengthToSilver(actual, per100);
                    silverLeft -= spent;
                    result.SilverSpentOnStrength += spent;
                    result.Applied = true;
                    progress = true;

                    strengthenedIds.Add(settlement.ID);
                    strengthBySettlementId.TryGetValue(settlement.ID, out float priorGain);
                    strengthBySettlementId[settlement.ID] = priorGain + actual;
                    settlementById[settlement.ID] = settlement;

                    WDVerbose.Msg(
                        $"Filled Strength of {settlement.LabelCap} from {before:F1} to {after:F1}. Budget cost {spent:F0} Silver. Remaining {silverLeft:F0} Silver");
                }

                for (int i = 0; i < candidates.Count && silverLeft > 0.01f; i++)
                {
                    Settlement settlement = candidates[i].Settlement;
                    CompViralSpread comp = candidates[i].Comp;
                    if (settlement == null || comp == null) continue;
                    if (comp.tier >= SettlementTier.T4) continue;

                    float max = CompViralSpread.GetStrengthRange(comp.tier).max;
                    if (comp.offensiveStrength < max - CapEpsilon) continue;

                    float cost = s != null
                        ? s.GetFactionInvestmentUpgradeSilver(comp.tier)
                        : DefaultUpgradeSilver(comp.tier);
                    if (cost <= 0f || silverLeft + 0.01f < cost) continue;

                    SettlementTier fromTier = comp.tier;
                    // Pay first, then roll. Fail exhausts this settlement for the award (no retry).
                    silverLeft -= cost;
                    result.SilverSpentOnUpgrades += cost;
                    result.Applied = true;
                    progress = true;

                    var promote = comp.TryPromoteTierFromInvestment();
                    if (promote != CompViralSpread.InvestmentPromoteResult.Promoted)
                    {
                        upgradeFailedIds.Add(settlement.ID);
                        failedUpgradeLines.Add(
                            $"{settlement.LabelCap} upgrade failed at {fromTier} (upgrade silver {cost:F0}).");
                        WDVerbose.Msg(
                            $"Investment upgrade failed for {settlement.LabelCap} at {fromTier}. Budget cost {cost:F0} Silver. Remaining {silverLeft:F0} Silver");
                        // Exhaust: remove from further upgrade attempts this award.
                        candidates.RemoveAt(i);
                        i--;
                        continue;
                    }

                    upgradedIds.Add(settlement.ID);
                    settlementById[settlement.ID] = settlement;
                    if (upgradeBySettlementId.TryGetValue(settlement.ID, out string priorUp))
                        upgradeBySettlementId[settlement.ID] =
                            priorUp + $"; {fromTier} → {comp.tier} (upgrade silver {cost:F0})";
                    else
                        upgradeBySettlementId[settlement.ID] =
                            $"upgraded {fromTier} → {comp.tier} (upgrade silver {cost:F0})";

                    WDVerbose.Msg(
                        $"Upgraded {settlement.LabelCap} from {fromTier} to {comp.tier}. Budget cost {cost:F0} Silver. Remaining {silverLeft:F0} Silver");
                    // Success can chain: leave candidate in list so a later round may promote again toward T4.
                }

                if (!progress) break;
            }

            result.SettlementsStrengthened = strengthenedIds.Count;
            result.SettlementsUpgraded = upgradedIds.Count;
            result.SettlementsUpgradeFailed = upgradeFailedIds.Count;
            result.FullyCapped = !result.Applied && AllCandidatesAtT4Max(candidates);
            result.DetailLines = BuildDetailLines(settlementById, strengthBySettlementId, upgradeBySettlementId);

            if (ShouldLogInvestmentDev(notify))
            {
                List<string> consoleLines = CombineDevConsoleLines(result.DetailLines, failedUpgradeLines);
                if (consoleLines == null || consoleLines.Count == 0)
                    Action_Settlement_Buy.LogInvestmentDevConsole(faction, originTile, result, null,
                        "candidates in range but none received strength or upgrades (already at cap / budget too low)", notify);
                else
                    Action_Settlement_Buy.LogInvestmentDevConsole(faction, originTile, result, consoleLines, null, notify);
            }

            MaybeNotify(faction, originTile, preferFirst, result, notify);
            return result;
        }

        private static List<string> BuildDetailLines(
            Dictionary<int, Settlement> settlementById,
            Dictionary<int, float> strengthBySettlementId,
            Dictionary<int, string> upgradeBySettlementId)
        {
            if (settlementById == null || settlementById.Count == 0) return null;
            var lines = new List<string>();
            foreach (var kv in settlementById)
            {
                Settlement settlement = kv.Value;
                if (settlement == null) continue;
                strengthBySettlementId.TryGetValue(kv.Key, out float strengthGain);
                upgradeBySettlementId.TryGetValue(kv.Key, out string upgradeNote);
                if (strengthGain <= 0f && upgradeNote.NullOrEmpty()) continue;

                string name = settlement.LabelCap;
                if (strengthGain > 0f)
                {
                    string gainLine = "TSA_WD_SettlementCaravan_LootDetailStrength".Translate(
                        name, strengthGain.ToString("F0")).ToString();
                    if (gainLine.Contains("TSA_WD_"))
                        gainLine = name + " gained " + strengthGain.ToString("F0") + " strength.";
                    lines.Add(gainLine);
                }
                if (!upgradeNote.NullOrEmpty())
                {
                    // upgradeNote is internal "upgraded T1 → T2 (...)"; extract new tier if possible.
                    string tierLabel = upgradeNote;
                    int arrow = upgradeNote.LastIndexOf('→');
                    if (arrow >= 0 && arrow + 1 < upgradeNote.Length)
                    {
                        string after = upgradeNote.Substring(arrow + 1).Trim();
                        int paren = after.IndexOf('(');
                        if (paren > 0) after = after.Substring(0, paren).Trim();
                        tierLabel = after;
                    }
                    string upLine = "TSA_WD_SettlementCaravan_LootDetailUpgrade".Translate(name, tierLabel).ToString();
                    if (upLine.Contains("TSA_WD_"))
                        upLine = name + " upgraded to " + tierLabel + ".";
                    lines.Add(upLine);
                }
            }
            return lines.Count > 0 ? lines : null;
        }

        private static List<string> CombineDevConsoleLines(List<string> successLines, List<string> failedUpgradeLines)
        {
            if ((successLines == null || successLines.Count == 0)
                && (failedUpgradeLines == null || failedUpgradeLines.Count == 0))
                return null;
            var lines = new List<string>();
            if (successLines != null)
            {
                for (int i = 0; i < successLines.Count; i++)
                    lines.Add(successLines[i]);
            }
            if (failedUpgradeLines != null)
            {
                for (int i = 0; i < failedUpgradeLines.Count; i++)
                    lines.Add(failedUpgradeLines[i]);
            }
            return lines;
        }

        private static bool ShouldLogInvestmentDev(NotifyKind notify)
            => notify == NotifyKind.Buy || notify == NotifyKind.Gift || notify == NotifyKind.Bribe;

        private static bool AllCandidatesAtT4Max(List<Candidate> candidates)
        {
            if (candidates == null || candidates.Count == 0) return false;
            for (int i = 0; i < candidates.Count; i++)
            {
                CompViralSpread comp = candidates[i].Comp;
                if (comp == null) return false;
                if (comp.tier < SettlementTier.T4) return false;
                float max = CompViralSpread.GetStrengthRange(SettlementTier.T4).max;
                if (comp.offensiveStrength < max - CapEpsilon) return false;
            }
            return true;
        }

        public static float SumTradeableMarketValue(List<Tradeable> tradeables)
        {
            if (tradeables == null) return 0f;
            float total = 0f;
            for (int i = 0; i < tradeables.Count; i++)
            {
                Tradeable t = tradeables[i];
                if (t == null || t.ActionToDo == TradeAction.None) continue;
                Thing sample = t.AnyThing;
                if (sample == null) continue;
                total += Mathf.Max(0f, sample.MarketValue) * Mathf.Abs(t.CountToTransfer);
            }
            return total;
        }

        public static float SumPodMarketValue(List<ActiveTransporterInfo> pods)
        {
            if (pods == null) return 0f;
            float total = 0f;
            for (int i = 0; i < pods.Count; i++)
            {
                ThingOwner container = pods[i]?.innerContainer;
                if (container == null) continue;
                for (int j = 0; j < container.Count; j++)
                {
                    Thing thing = container[j];
                    if (thing == null) continue;
                    // MarketValue is per unit; stacks (e.g. silver) must multiply by stackCount.
                    int count = Mathf.Max(1, thing.stackCount);
                    total += Mathf.Max(0f, thing.MarketValue) * count;
                }
            }
            return total;
        }

        public static Settlement? FindNearestFactionSettlement(Faction faction, int originTile, int radius)
        {
            if (faction == null || originTile < 0 || Find.WorldGrid == null || Find.WorldObjects?.Settlements == null)
                return null;
            Settlement? best = null;
            int bestDist = int.MaxValue;
            var list = Find.WorldObjects.Settlements;
            for (int i = 0; i < list.Count; i++)
            {
                Settlement s = list[i];
                if (s == null || s.Destroyed || s.Faction != faction || s.Tile < 0) continue;
                if (s.GetComponent<CompViralSpread>() == null) continue;
                int dist = (int)Find.WorldGrid.ApproxDistanceInTiles(originTile, s.Tile);
                if (dist > radius) continue;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = s;
                }
            }
            return best;
        }

        private static List<Candidate> CollectCandidates(Faction faction, int originTile, int radius, Settlement? preferFirst)
        {
            var list = new List<Candidate>();
            var settlements = Find.WorldObjects.Settlements;
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement s = settlements[i];
                if (s == null || s.Destroyed || s.Faction != faction || s.Tile < 0) continue;
                // Sold settlement may still be alive for a moment; preferFirst can be that town for gifts only.
                CompViralSpread comp = s.GetComponent<CompViralSpread>();
                if (comp == null || !comp.IsSettlement) continue;
                int dist = (int)Find.WorldGrid.ApproxDistanceInTiles(originTile, s.Tile);
                if (dist > radius) continue;
                list.Add(new Candidate
                {
                    Settlement = s,
                    Comp = comp,
                    Distance = dist,
                    PreferFirst = preferFirst != null && s == preferFirst
                });
            }

            list.Sort((a, b) =>
            {
                int c = b.PreferFirst.CompareTo(a.PreferFirst);
                if (c != 0) return c;
                c = a.Distance.CompareTo(b.Distance);
                if (c != 0) return c;
                string la = a.Settlement?.LabelCap ?? "";
                string lb = b.Settlement?.LabelCap ?? "";
                return string.Compare(la, lb, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        private static float SilverToStrength(float silver, float per100) =>
            (Mathf.Max(0f, silver) / 100f) * per100;

        private static float StrengthToSilver(float strength, float per100) =>
            per100 > 0f ? (Mathf.Max(0f, strength) / per100) * 100f : 0f;

        private static float DefaultUpgradeSilver(SettlementTier tier)
        {
            switch (tier)
            {
                case SettlementTier.T1: return WorldDominationSettings.DefFactionInvestmentUpgradeT1ToT2Silver;
                case SettlementTier.T2: return WorldDominationSettings.DefFactionInvestmentUpgradeT2ToT3Silver;
                case SettlementTier.T3: return WorldDominationSettings.DefFactionInvestmentUpgradeT3ToT4Silver;
                default: return 0f;
            }
        }

        private static void LogNoneInRange(Faction faction, float silverBudget)
        {
            Find.World.GetComponent<WorldComponent_SpreadManager>()?.AddLog(new SpreadLogEntry(
                "TSA_WD_FactionInvestment_LogNoneInRange".Translate(
                    faction?.Name ?? "?",
                    silverBudget.ToString("F0")).ToString()));
        }

        private static void MaybeNotify(
            Faction faction,
            int originTile,
            Settlement? preferFirst,
            AwardResult result,
            NotifyKind notify)
        {
            if (notify == NotifyKind.None || notify == NotifyKind.Loot || notify == NotifyKind.Bribe) return;

            LookTargets look = preferFirst != null && !preferFirst.Destroyed
                ? new LookTargets(preferFirst)
                : new LookTargets(new GlobalTargetInfo(originTile));

            if (!result.Applied)
            {
                // Large gifts that could not buy more strength/tier-ups used to stay silent.
                float minSilver = WorldDominationSettings.DefFactionInvestmentNotifyMinSilver;
                if (notify == NotifyKind.Gift && result.HadCandidates && result.SilverBudget >= minSilver)
                {
                    string cappedText = result.FullyCapped
                        ? "TSA_WD_FactionInvestment_LetterGiftFullyCapped".Translate(faction?.Name ?? "?").ToString()
                        : "TSA_WD_FactionInvestment_LetterGiftNoEffect".Translate(
                            faction?.Name ?? "?",
                            result.SilverBudget.ToString("F0")).ToString();
                    Find.LetterStack.ReceiveLetter(
                        "TSA_WD_FactionInvestment_LetterLabel".Translate(),
                        cappedText,
                        LetterDefOf.NeutralEvent,
                        look);
                    Find.World.GetComponent<WorldComponent_SpreadManager>()?.AddLog(new SpreadLogEntry(cappedText,
                        preferFirst != null && !preferFirst.Destroyed ? preferFirst : null));
                }
                return;
            }

            if (result.SettlementsStrengthened <= 0 && result.SettlementsUpgraded <= 0) return;

            float notifyMin = WorldDominationSettings.DefFactionInvestmentNotifyMinSilver;
            bool meaningful = result.SilverBudget >= notifyMin || result.SettlementsUpgraded > 0;
            if (!meaningful && notify == NotifyKind.Gift) return;

            string textKey = notify == NotifyKind.Buy
                ? "TSA_WD_FactionInvestment_LetterBuy"
                : "TSA_WD_FactionInvestment_LetterGift";
            string text = textKey.Translate(
                faction?.Name ?? "?",
                result.SettlementsStrengthened.ToString(),
                result.SettlementsUpgraded.ToString()).ToString();

            Find.LetterStack.ReceiveLetter(
                "TSA_WD_FactionInvestment_LetterLabel".Translate(),
                text,
                LetterDefOf.PositiveEvent,
                look);

            if (preferFirst != null && !preferFirst.Destroyed)
                Find.World.GetComponent<WorldComponent_SpreadManager>()?.AddLog(new SpreadLogEntry(text, preferFirst));
            else
                Find.World.GetComponent<WorldComponent_SpreadManager>()?.AddLog(new SpreadLogEntry(text));
        }
    }
}
