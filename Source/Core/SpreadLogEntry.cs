using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Tags entries the main WD dashboard may surface as highlights (full action log is unchanged).</summary>
    public enum SpreadLogHighlightKind : byte
    {
        None = 0,
        IncidentSettlementDestroyed = 1,
        RaidSuccess = 2,
        ExpansionSuccess = 3,
        Diplomacy = 4,
    }

    /// <summary>Single log entry for the action log and raid resolution UI. Persisted in save.</summary>
    public class SpreadLogEntry : IExposable
    {
        public string message;
        public string labelA;
        public string labelB;
        public GlobalTargetInfo targetA;
        public GlobalTargetInfo targetB;

        public int timestamp;

        public bool isRaid;
        public float attStr, defStr, ratio, winChance;
        public bool victory;
        public float attLossPct, defLossPct;
        public BattleMarginTier attSeverityTier = BattleMarginTier.Normal;
        public BattleMarginTier defCoalitionSeverityTier = BattleMarginTier.Normal;
        public BattleMarginTier marginTier = BattleMarginTier.Normal;

        public List<string> attDetails = new List<string>();
        public List<string> defDetails = new List<string>();
        /// <summary>Structured attacker force rows for Details UI (legacy saves may only have attDetails).</summary>
        public List<RaidForceLogRow> attForceRows = new List<RaidForceLogRow>();
        /// <summary>Structured defender force rows for Details UI (legacy saves may only have defDetails).</summary>
        public List<RaidForceLogRow> defForceRows = new List<RaidForceLogRow>();

        public float efficiencyFactor = 1.0f;
        public float targetDistance = 0f;
        /// <summary>Path-based world travel ticks at launch (<see cref="TravelUtils.SumFullPathTicks"/>). -1 = not stored (legacy log).</summary>
        public float pathTravelTicks = -1f;
        public List<string> contributionDNAKeys = new List<string>();
        public List<float> contributionDNAValues = new List<float>();
        public bool isAttempt;
        public bool isAborted;
        public bool isCaravanClash;
        public SpreadLogHighlightKind highlightKind;
        /// <summary>Raid path expected pollution exit damage &gt; 0.</summary>
        public bool pollutionDamageExpected;
        /// <summary>Raid path differed from pollution-blind route (or High repath used).</summary>
        public bool pollutionRouteAltered;
        /// <summary>Sum of expected pollution exit damage along the math path (-1 = unset).</summary>
        public float pollutionExpectedLoss = -1f;

        private Dictionary<string, float> cachedContributionDNA;

        public Dictionary<string, float> contributionDNA
        {
            get
            {
                if (cachedContributionDNA != null) return cachedContributionDNA;
                cachedContributionDNA = new Dictionary<string, float>();
                if (contributionDNAKeys == null || contributionDNAValues == null) return cachedContributionDNA;
                for (int i = 0; i < System.Math.Min(contributionDNAKeys.Count, contributionDNAValues.Count); i++)
                {
                    cachedContributionDNA[contributionDNAKeys[i]] = contributionDNAValues[i];
                }
                return cachedContributionDNA;
            }
        }

        public class GlobalWorldStats
        {
            public List<FactionStat> FactionStats = new List<FactionStat>();
            public float[] GlobalTierStr = new float[5];
            public float GlobalTotalStr = 0;
        }

        public class FactionStat
        {
            public Faction faction;
            public int[] counts = new int[5];
            public float[] strength = new float[5];
            public float TotalStr => strength[1] + strength[2] + strength[3] + strength[4];
            public int TotalCount => counts[1] + counts[2] + counts[3] + counts[4];
        }

        public SpreadLogEntry() { }

        public SpreadLogEntry(string msg, Faction initiator, WorldObject settlement, string oldFactionName, Faction oldFaction)
        {
            this.message = msg;
            this.labelA = $"{initiator.Name} (Rebels)";
            this.targetA = GlobalTargetInfo.Invalid;
            this.labelB = $"{settlement.LabelCap} ({oldFactionName})";
            this.targetB = GlobalTargetInfo.Invalid;
            this.timestamp = Find.TickManager.TicksGame;
        }

        public SpreadLogEntry(string msg, WorldObject a, WorldObject b = null)
        {
            this.message = msg;
            this.targetA = a;
            this.targetB = b;
            this.labelA = FormatLabel(a);
            this.labelB = FormatLabel(b);
            this.timestamp = Find.TickManager.TicksGame;
        }

        /// <summary>Actor A = world object; Actor B = jumpable world tile (road / fortify work sites).</summary>
        public SpreadLogEntry(string msg, WorldObject a, int tileB)
        {
            this.message = msg;
            this.targetA = a;
            this.labelA = FormatLabel(a);
            if (tileB >= 0)
            {
                this.targetB = new GlobalTargetInfo(tileB);
                this.labelB = "TSA_WD_Log_TileActor".Translate(tileB).ToString();
            }
            else
            {
                this.targetB = GlobalTargetInfo.Invalid;
                this.labelB = "---";
            }
            this.timestamp = Find.TickManager.TicksGame;
        }

        public SpreadLogEntry(string msg)
        {
            this.message = msg;
            this.timestamp = Find.TickManager.TicksGame;
            this.labelA = "---";
            this.labelB = "---";
        }

        private string FormatLabel(WorldObject obj)
        {
            if (obj == null || obj.Destroyed) return "---";

            var comp = obj.GetComponent<CompViralSpread>();
            string typeLabel = "";

            if (obj is WorldObject_Traveler)
                typeLabel = " (Expedition Force)";
            else if (obj is WorldObject_WD_Outpost)
                typeLabel = " (Outpost)";
            else if (obj is Settlement)
                typeLabel = comp != null ? $" ({comp.tier})" : " (Town)";

            return $"{obj.LabelCap}{typeLabel} ({obj.Faction?.Name ?? "No Faction"})";
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref message, "message");
            Scribe_Values.Look(ref labelA, "labelA");
            Scribe_Values.Look(ref labelB, "labelB");
            Scribe_Values.Look(ref timestamp, "timestamp", 0);
            Scribe_TargetInfo.Look(ref targetA, "targetA");
            Scribe_TargetInfo.Look(ref targetB, "targetB");

            Scribe_Values.Look(ref isRaid, "isRaid", false);
            Scribe_Values.Look(ref attStr, "attStr", 0f);
            Scribe_Values.Look(ref defStr, "defStr", 0f);
            Scribe_Values.Look(ref ratio, "ratio", 0f);
            Scribe_Values.Look(ref winChance, "winChance", 0f);
            Scribe_Values.Look(ref victory, "victory", false);
            Scribe_Values.Look(ref attLossPct, "attLossPct", 0f);
            Scribe_Values.Look(ref defLossPct, "defLossPct", 0f);
            Scribe_Values.Look(ref attSeverityTier, "attSeverityTier", BattleMarginTier.Normal);
            Scribe_Values.Look(ref defCoalitionSeverityTier, "defCoalitionSeverityTier", BattleMarginTier.Normal);
            Scribe_Values.Look(ref marginTier, "marginTier", BattleMarginTier.Normal);

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (attSeverityTier == BattleMarginTier.Normal && defCoalitionSeverityTier == BattleMarginTier.Normal
                    && marginTier != BattleMarginTier.Normal)
                {
                    attSeverityTier = marginTier;
                }
            }

            Scribe_Collections.Look(ref attDetails, "attDetails", LookMode.Value);
            Scribe_Collections.Look(ref defDetails, "defDetails", LookMode.Value);
            Scribe_Collections.Look(ref attForceRows, "attForceRows", LookMode.Deep);
            Scribe_Collections.Look(ref defForceRows, "defForceRows", LookMode.Deep);

            Scribe_Values.Look(ref isAttempt, "isAttempt", false);
            Scribe_Values.Look(ref isAborted, "isAborted", false);
            Scribe_Values.Look(ref isCaravanClash, "isCaravanClash", false);
            Scribe_Values.Look(ref highlightKind, "highlightKind", SpreadLogHighlightKind.None);
            Scribe_Values.Look(ref pollutionDamageExpected, "pollutionDamageExpected", false);
            Scribe_Values.Look(ref pollutionRouteAltered, "pollutionRouteAltered", false);
            Scribe_Values.Look(ref pollutionExpectedLoss, "pollutionExpectedLoss", -1f);
            Scribe_Values.Look(ref efficiencyFactor, "efficiencyFactor", 1.0f);
            Scribe_Values.Look(ref targetDistance, "targetDistance", 0f);
            Scribe_Values.Look(ref pathTravelTicks, "pathTravelTicks", -1f);
            Scribe_Collections.Look(ref contributionDNAKeys, "contributionDNAKeys", LookMode.Value);
            Scribe_Collections.Look(ref contributionDNAValues, "contributionDNAValues", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (attDetails == null) attDetails = new List<string>();
                if (defDetails == null) defDetails = new List<string>();
                if (attForceRows == null) attForceRows = new List<RaidForceLogRow>();
                if (defForceRows == null) defForceRows = new List<RaidForceLogRow>();
                if (contributionDNAKeys == null) contributionDNAKeys = new List<string>();
                if (contributionDNAValues == null) contributionDNAValues = new List<float>();
            }
        }
    }
}
