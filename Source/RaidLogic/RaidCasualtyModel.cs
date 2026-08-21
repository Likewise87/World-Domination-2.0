using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public struct RaidResolvedOutcome
    {
        public bool attackerWon;
        public BattleMarginTier attSeverity;
        public BattleMarginTier defCoalitionSeverity;
        public float winChance;
        public float attLossPct;
        public float defLossPct;
        public RaidMarginShares attSeverityShares;
        public RaidMarginShares defCoalitionSeverityShares;
    }

    public struct RaidOutcomeForecast
    {
        public float winChance;
        public RaidMarginShares attWinAttSeverity;
        public RaidMarginShares attWinDefCoalition;
        public RaidMarginShares attLossAttSeverity;
        public RaidMarginShares attLossDefCoalition;
        public float attWinAttLossMin;
        public float attWinAttLossMax;
        public float attWinAttLossExpected;
        public float attWinDefLossMin;
        public float attWinDefLossMax;
        public float attWinDefLossExpected;
        public float attLossAttLossMin;
        public float attLossAttLossMax;
        public float attLossAttLossExpected;
        public float attLossDefLossMin;
        public float attLossDefLossMax;
        public float attLossDefLossExpected;
    }

    public static class RaidCasualtyModel
    {
        public static RaidResolvedOutcome Resolve(float ratio, WorldDominationSettings seth, bool? forceAttackerWon = null)
        {
            InterpolateOutcomeRow(ratio, seth, out float winChance,
                out RaidMarginShares attWinAttSev, out RaidMarginShares attWinDefCoal,
                out RaidMarginShares attLossAttSev, out RaidMarginShares attLossDefCoal);

            bool attackerWon = forceAttackerWon ?? (Rand.Value < winChance);
            RaidMarginShares attShares = attackerWon ? attWinAttSev : attLossAttSev;
            RaidMarginShares defShares = attackerWon ? attWinDefCoal : attLossDefCoal;

            BattleMarginTier attSeverity = RollMarginTier(attShares);
            BattleMarginTier defCoalitionSeverity = RollMarginTier(defShares);

            float attLossPct = Mathf.Clamp01(seth.GetAttCasualtyLoss(attSeverity, attackerWon));
            float defLossPct = Mathf.Clamp01(seth.GetDefCoalitionCasualtyLoss(defCoalitionSeverity, !attackerWon));

            return new RaidResolvedOutcome
            {
                attackerWon = attackerWon,
                attSeverity = attSeverity,
                defCoalitionSeverity = defCoalitionSeverity,
                winChance = winChance,
                attLossPct = attLossPct,
                defLossPct = defLossPct,
                attSeverityShares = attShares,
                defCoalitionSeverityShares = defShares
            };
        }

        public static RaidOutcomeForecast GetForecast(float ratio, WorldDominationSettings seth)
        {
            InterpolateOutcomeRow(ratio, seth, out float winChance,
                out RaidMarginShares attWinAttSev, out RaidMarginShares attWinDefCoal,
                out RaidMarginShares attLossAttSev, out RaidMarginShares attLossDefCoal);

            attWinAttSev.Normalize();
            attWinDefCoal.Normalize();
            attLossAttSev.Normalize();
            attLossDefCoal.Normalize();

            // attWin branch: attacker won, defender lost. attLoss branch: attacker lost, defender won.
            ComputeSeverityLossStats(seth, attWinAttSev, true, true, out float winAttMin, out float winAttMax, out float winAttExp);
            ComputeSeverityLossStats(seth, attWinDefCoal, false, false, out float winDefMin, out float winDefMax, out float winDefExp);
            ComputeSeverityLossStats(seth, attLossAttSev, true, false, out float loseAttMin, out float loseAttMax, out float loseAttExp);
            ComputeSeverityLossStats(seth, attLossDefCoal, false, true, out float loseDefMin, out float loseDefMax, out float loseDefExp);

            return new RaidOutcomeForecast
            {
                winChance = winChance,
                attWinAttSeverity = attWinAttSev,
                attWinDefCoalition = attWinDefCoal,
                attLossAttSeverity = attLossAttSev,
                attLossDefCoalition = attLossDefCoal,
                attWinAttLossMin = winAttMin,
                attWinAttLossMax = winAttMax,
                attWinAttLossExpected = winAttExp,
                attWinDefLossMin = winDefMin,
                attWinDefLossMax = winDefMax,
                attWinDefLossExpected = winDefExp,
                attLossAttLossMin = loseAttMin,
                attLossAttLossMax = loseAttMax,
                attLossAttLossExpected = loseAttExp,
                attLossDefLossMin = loseDefMin,
                attLossDefLossMax = loseDefMax,
                attLossDefLossExpected = loseDefExp
            };
        }

        private static void InterpolateOutcomeRow(float ratio, WorldDominationSettings seth, out float winChance,
            out RaidMarginShares attWinAttSev, out RaidMarginShares attWinDefCoal,
            out RaidMarginShares attLossAttSev, out RaidMarginShares attLossDefCoal)
        {
            var outcomes = seth.GetRaidOutcomesSorted();
            winChance = 0.42f;
            attWinAttSev = RaidSeverityDefaults.AttSeverityOnAttWinAt(1f);
            attWinDefCoal = RaidSeverityDefaults.DefCoalitionOnAttWinAt(1f);
            attLossAttSev = RaidSeverityDefaults.AttSeverityOnAttLossAt(1f);
            attLossDefCoal = RaidSeverityDefaults.DefCoalitionOnAttLossAt(1f);

            if (outcomes.Count == 0) return;

            if (ratio <= outcomes[0].threshold)
            {
                ApplyRow(outcomes[0], out winChance, out attWinAttSev, out attWinDefCoal, out attLossAttSev, out attLossDefCoal);
                return;
            }
            if (ratio >= outcomes[outcomes.Count - 1].threshold)
            {
                ApplyRow(outcomes[outcomes.Count - 1], out winChance, out attWinAttSev, out attWinDefCoal, out attLossAttSev, out attLossDefCoal);
                return;
            }

            for (int i = 0; i < outcomes.Count - 1; i++)
            {
                var lower = outcomes[i];
                var upper = outcomes[i + 1];
                if (ratio >= lower.threshold && ratio <= upper.threshold)
                {
                    float t = (ratio - lower.threshold) / (upper.threshold - lower.threshold);
                    winChance = Mathf.Lerp(lower.winChance, upper.winChance, t);
                    attWinAttSev = LerpShares(lower.attSeverityOnAttWin, upper.attSeverityOnAttWin, t, ratio, SeverityCurve.AttOnWin);
                    attWinDefCoal = LerpShares(lower.defCoalitionOnAttWin, upper.defCoalitionOnAttWin, t, ratio, SeverityCurve.DefOnWin);
                    attLossAttSev = LerpShares(lower.attSeverityOnAttLoss, upper.attSeverityOnAttLoss, t, ratio, SeverityCurve.AttOnLoss);
                    attLossDefCoal = LerpShares(lower.defCoalitionOnAttLoss, upper.defCoalitionOnAttLoss, t, ratio, SeverityCurve.DefOnLoss);
                    return;
                }
            }

            ApplyRow(outcomes[outcomes.Count - 1], out winChance, out attWinAttSev, out attWinDefCoal, out attLossAttSev, out attLossDefCoal);
        }

        private enum SeverityCurve { AttOnWin, AttOnLoss, DefOnWin, DefOnLoss }

        private static void ApplyRow(RaidOutcome row, out float winChance,
            out RaidMarginShares attWinAttSev, out RaidMarginShares attWinDefCoal,
            out RaidMarginShares attLossAttSev, out RaidMarginShares attLossDefCoal)
        {
            winChance = row.winChance;
            attWinAttSev = row.attSeverityOnAttWin?.Copy() ?? RaidSeverityDefaults.AttSeverityOnAttWinAt(row.threshold);
            attWinDefCoal = row.defCoalitionOnAttWin?.Copy() ?? RaidSeverityDefaults.DefCoalitionOnAttWinAt(row.threshold);
            attLossAttSev = row.attSeverityOnAttLoss?.Copy() ?? RaidSeverityDefaults.AttSeverityOnAttLossAt(row.threshold);
            attLossDefCoal = row.defCoalitionOnAttLoss?.Copy() ?? RaidSeverityDefaults.DefCoalitionOnAttLossAt(row.threshold);
            attWinAttSev.Normalize();
            attWinDefCoal.Normalize();
            attLossAttSev.Normalize();
            attLossDefCoal.Normalize();
        }

        private static RaidMarginShares LerpShares(RaidMarginShares lower, RaidMarginShares upper, float t, float threshold, SeverityCurve curve)
        {
            if (lower == null) lower = DefaultSharesAt(threshold, curve);
            if (upper == null) upper = lower;
            var result = new RaidMarginShares
            {
                close = Mathf.Lerp(lower.close, upper.close, t),
                normal = Mathf.Lerp(lower.normal, upper.normal, t),
                decisive = Mathf.Lerp(lower.decisive, upper.decisive, t)
            };
            result.Normalize();
            return result;
        }

        private static RaidMarginShares DefaultSharesAt(float threshold, SeverityCurve curve)
        {
            switch (curve)
            {
                case SeverityCurve.AttOnLoss: return RaidSeverityDefaults.AttSeverityOnAttLossAt(threshold);
                case SeverityCurve.DefOnWin: return RaidSeverityDefaults.DefCoalitionOnAttWinAt(threshold);
                case SeverityCurve.DefOnLoss: return RaidSeverityDefaults.DefCoalitionOnAttLossAt(threshold);
                default: return RaidSeverityDefaults.AttSeverityOnAttWinAt(threshold);
            }
        }

        private static BattleMarginTier RollMarginTier(RaidMarginShares shares)
        {
            shares.Normalize();
            float roll = Rand.Value;
            if (roll < shares.close) return BattleMarginTier.Close;
            if (roll < shares.close + shares.normal) return BattleMarginTier.Normal;
            return BattleMarginTier.Decisive;
        }

        private static void ComputeSeverityLossStats(WorldDominationSettings seth, RaidMarginShares shares, bool attackerSide, bool sideWon, out float min, out float max, out float expected)
        {
            min = float.MaxValue;
            max = float.MinValue;
            expected = 0f;
            foreach (BattleMarginTier tier in new[] { BattleMarginTier.Close, BattleMarginTier.Normal, BattleMarginTier.Decisive })
            {
                float loss = attackerSide ? seth.GetAttCasualtyLoss(tier, sideWon) : seth.GetDefCoalitionCasualtyLoss(tier, sideWon);
                min = Mathf.Min(min, loss);
                max = Mathf.Max(max, loss);
                float weight = tier == BattleMarginTier.Close ? shares.close
                    : tier == BattleMarginTier.Normal ? shares.normal : shares.decisive;
                expected += weight * loss;
            }
            if (min == float.MaxValue) min = 0f;
            if (max == float.MinValue) max = 0f;
        }
    }
}
