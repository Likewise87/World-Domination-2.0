using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>WD-native player outpost. Virtual pawns only (no map, no ticking). Primary skills come from <c>MinCumulativeSkill</c> in XML when present; otherwise defName heuristics (farming→Plants, etc.).</summary>
    [StaticConstructorOnStartup]
    public partial class WorldObject_WD_Outpost : WorldObject, IDefensiveInterceptor
    {
        // --- Mortar outpost: defensive auto-fire state (only used when IsMortarOutpost). ---
        private bool mortarDefenseActive = true;
        private int mortarDefenseMaskRaw = (int)(MissionMask.Raider | MissionMask.Expansion);
        private bool antiAirDefenseActive = true;
        private int antiAirGroupRaw = (int)AntiAirGroupLetter.Off;
        private int mortarRaidTargetMaskRaw = (int)(RaidTargetMask.Player | RaidTargetMask.Allies);
        private int antiAirKindMaskRaw = (int)AntiAirKindMask.All;
        private bool rapidResponseActive;
        private int rapidResponseMaskRaw = (int)MissionMask.All;
        /// <summary>-1 = use configured max. Otherwise absolute tiles, clamped to [1..max] at read time.</summary>
        private float rapidResponseRangeOverride = -1f;
        private float rapidResponseMinStrengthRatio = 0.9f;
        private float rapidResponseMaxStrengthRatio = RapidResponseUtility.DefaultMaxStrengthRatio;
        private int rapidResponseRaidTargetMaskRaw = (int)(RaidTargetMask.Player | RaidTargetMask.Allies);
        private float mortarRangeOverride = -1f;
        private float antiAirRangeOverride = -1f;
        public bool MortarDefenseActive => mortarDefenseActive;
        public MissionMask MortarDefenseMask => (MissionMask)mortarDefenseMaskRaw;
        public bool AntiAirDefenseActive => antiAirDefenseActive;
        public AntiAirGroupLetter AntiAirGroup => (AntiAirGroupLetter)antiAirGroupRaw;
        public RaidTargetMask MortarRaidTargetMask => (RaidTargetMask)mortarRaidTargetMaskRaw;
        public AntiAirKindMask AntiAirTargetKinds => (AntiAirKindMask)antiAirKindMaskRaw;
        public bool RapidResponseActive => rapidResponseActive;
        public MissionMask RapidResponseMask => (MissionMask)rapidResponseMaskRaw;
        public float RapidResponseRangeOverride => rapidResponseRangeOverride;
        public float RapidResponseMinStrengthRatio => rapidResponseMinStrengthRatio;
        public float RapidResponseMaxStrengthRatio => rapidResponseMaxStrengthRatio;
        public RaidTargetMask RapidResponseRaidTargetMask => (RaidTargetMask)rapidResponseRaidTargetMaskRaw;
        public float MortarRangeOverride => mortarRangeOverride;
        public float AntiAirRangeOverride => antiAirRangeOverride;

        public const string MortarOutpostTexturePath = "WorldObjects/WD_Outpost_Mortar";
        public const string MortarOutpostAntiAirTexturePath = "WorldObjects/WD_Outpost_Mortar_AA";

        public void SetMortarDefenseActive(bool on)
        {
            mortarDefenseActive = on;
            RefreshInterceptorRegistration();
        }
        public void SetMortarDefenseMask(MissionMask mask) => mortarDefenseMaskRaw = (int)mask;
        public void SetMortarRaidTargetMask(RaidTargetMask mask) => mortarRaidTargetMaskRaw = (int)mask;
        public void SetAntiAirKindMask(AntiAirKindMask mask) => antiAirKindMaskRaw = (int)mask;
        public void SetAntiAirDefenseActive(bool on)
        {
            antiAirDefenseActive = on;
            RefreshInterceptorRegistration();
            // Pods already airborne were only woken at spawn; re-check so enabling Auto AA engages them now.
            if (on)
                AntiAirFireUtils.EngageExistingAirborneTargets(this);
        }
        public void SetAntiAirGroup(AntiAirGroupLetter group)
        {
            antiAirGroupRaw = (int)group;
        }
        public void SetRapidResponseActive(bool on)
        {
            rapidResponseActive = on;
            RefreshInterceptorRegistration();
        }
        public void SetRapidResponseMask(MissionMask mask) => rapidResponseMaskRaw = (int)mask;
        public void SetRapidResponseMinStrengthRatio(float ratio) => rapidResponseMinStrengthRatio = Mathf.Clamp(ratio, 0f, 4f);
        public void SetRapidResponseMaxStrengthRatio(float ratio) => rapidResponseMaxStrengthRatio = Mathf.Clamp(ratio, RapidResponseUtility.MinMaxStrengthRatio, RapidResponseUtility.MaxMaxStrengthRatio);
        public void SetRapidResponseRaidTargetMask(RaidTargetMask mask) => rapidResponseRaidTargetMaskRaw = (int)mask;

        /// <summary>Set absolute auto-intercept tiles, or pass negative / at-max to clear override.</summary>
        public void SetRapidResponseRangeOverride(float tilesOrClear)
        {
            float max = RapidResponseUtility.GetConfiguredMaxRangeTiles();
            float min = Mathf.Min(Dialog_OutpostRangeAdjust.MinTiles, max);
            if (tilesOrClear < 0f || Mathf.Approximately(tilesOrClear, max))
                rapidResponseRangeOverride = -1f;
            else
                rapidResponseRangeOverride = Mathf.Clamp(tilesOrClear, min, max);
        }

        public void SetMortarRangeOverride(float tilesOrClear)
        {
            float max = MortarFireUtils.GetPlayerMortarConfiguredMaxRangeTiles(this);
            float min = Mathf.Min(Dialog_OutpostRangeAdjust.MinTiles, max);
            if (tilesOrClear < 0f || Mathf.Approximately(tilesOrClear, max))
                mortarRangeOverride = -1f;
            else
                mortarRangeOverride = Mathf.Clamp(tilesOrClear, min, max);
        }

        public void SetAntiAirRangeOverride(float tilesOrClear)
        {
            float max = AntiAirFireUtils.GetPlayerAntiAirConfiguredMaxRangeTiles(this);
            float min = Mathf.Min(Dialog_OutpostRangeAdjust.MinTiles, max);
            if (tilesOrClear < 0f || Mathf.Approximately(tilesOrClear, max))
                antiAirRangeOverride = -1f;
            else
                antiAirRangeOverride = Mathf.Clamp(tilesOrClear, min, max);
        }

        private void RefreshInterceptorRegistration()
        {
            var sched = WorldComponent_InterceptionScheduler.Current;
            if (sched != null)
            {
                if (ShouldRegisterAsInterceptor())
                    sched.RegisterInterceptor(this);
                else
                    sched.UnregisterInterceptor(this);
            }
            WorldComponent_SettlementWatchIndex.Get()?.Invalidate();
        }

        private bool ShouldRegisterAsInterceptor()
        {
            if (IsRapidResponseOutpost && rapidResponseActive) return true;
            if (IsMortarOutpost && mortarDefenseActive) return true;
            if (IsMortarOutpost && antiAirDefenseActive && AntiAirFireUtils.HasAntiAirUpgrade(this)) return true;
            return false;
        }

        public bool IsMortarOutpost => Outpost_Production_Utils.IsMortarOutpost(def);
        public bool IsRapidResponseOutpost => Outpost_Production_Utils.IsRapidResponseOutpost(def);
        public bool IsAcademyOutpost => Outpost_Production_Utils.IsAcademyOutpost(def);
        public bool IsResearchOutpost => Outpost_Production_Utils.IsResearchOutpost(def);
        public bool IsPowerPlantOutpost => Outpost_Production_Utils.IsPowerPlantOutpost(def);

        // --- IDefensiveInterceptor ---
        WorldObject IDefensiveInterceptor.Self => this;
        PlanetTile IDefensiveInterceptor.InterceptorTile => Tile;
        Faction IDefensiveInterceptor.InterceptorFaction => Faction;
        float IDefensiveInterceptor.InterceptorRange
        {
            get
            {
                if (IsRapidResponseOutpost) return RapidResponseUtility.GetRangeTiles(this);
                if (!IsMortarOutpost)
                    return WorldDominationMod.settings?.mortarRange ?? WorldDominationSettings.DefMortarRange;
                float mortar = MortarFireUtils.GetPlayerMortarMaxRangeTiles(this);
                float aa = AntiAirFireUtils.GetPlayerAntiAirMaxRangeTiles(this);
                bool mortarOn = mortarDefenseActive;
                bool aaOn = antiAirDefenseActive && AntiAirFireUtils.HasAntiAirUpgrade(this);
                if (mortarOn && aaOn) return Mathf.Max(mortar, aa);
                if (aaOn) return aa;
                return mortar;
            }
        }
        MissionMask IDefensiveInterceptor.InterceptorMissionMask
        {
            get
            {
                if (IsRapidResponseOutpost && rapidResponseActive)
                    return RapidResponseMask;
                if (!IsMortarOutpost)
                    return MissionMask.None;
                MissionMask mask = MissionMask.None;
                if (mortarDefenseActive)
                    mask |= MortarDefenseMask;
                if (antiAirDefenseActive && AntiAirFireUtils.HasAntiAirUpgrade(this))
                    mask |= MissionMask.Raider;
                return mask;
            }
        }
        bool IDefensiveInterceptor.InterceptorCanFireNow()
        {
            if (Destroyed) return false;
            var comp = GetComponent<CompViralSpread>();
            if (IsRapidResponseOutpost && rapidResponseActive)
                return RapidResponseUtility.GetDeployableStrength(this, comp) > 0f;
            if (IsMortarOutpost)
            {
                if (GetHighestVirtualPawnSkill(SkillDefOf.Shooting) <= 0f) return false;
                bool mortarReady = mortarDefenseActive && comp != null && !comp.IsMortarOnCooldown;
                bool aaReady = antiAirDefenseActive
                    && AntiAirFireUtils.HasAntiAirUpgrade(this)
                    && comp != null
                    && !comp.IsAntiAirOnCooldown;
                return mortarReady || aaReady;
            }
            return false;
        }
        bool IDefensiveInterceptor.InterceptorCanTargetPlayer => false;
        void IDefensiveInterceptor.InterceptorFire(WorldObject_Traveler target, float approxTileDist)
        {
            if (IsRapidResponseOutpost)
            {
                RapidResponseUtility.DispatchVirtualIntercept(this, target);
                return;
            }
            if (target != null && target.mission == TravelerMission.RaidDropPod)
            {
                if (antiAirDefenseActive)
                    AntiAirFireUtils.TryEngageDropPod(this, target);
                return;
            }
            if (!mortarDefenseActive) return;
            if (!RapidResponseUtility.IsEligibleAutoInterceptTarget(target, MortarRaidTargetMask)) return;
            MortarFireUtils.FireDefensiveAtTraveler(this, target, approxTileDist);
        }
        void IDefensiveInterceptor.InterceptorNoTargetFire() { }

        /// <summary>Sum of a single skill across assigned pawns (raw). Mortar hit uses best shooter separately.</summary>
        public float GetSkillSumRaw(SkillDef skillDef)
        {
            if (skillDef == null || VirtualPawns == null) return 0f;
            float sum = 0f;
            foreach (var v in VirtualPawns)
                sum += v.GetSkill(skillDef);
            return sum;
        }

        /// <summary>Effective cumulative skill for cooldown / capacity (diminishing returns applied).</summary>
        public float GetSkillSum(SkillDef skillDef) => OutpostSkillScaling.ToEffective(GetSkillSumRaw(skillDef));

        /// <summary>Highest level of a skill among virtual occupants (mortar accuracy uses best Shooting).</summary>
        public float GetHighestVirtualPawnSkill(SkillDef skillDef)
        {
            if (skillDef == null || VirtualPawns == null) return 0f;
            float max = 0f;
            foreach (var v in VirtualPawns)
                max = Mathf.Max(max, v.GetSkill(skillDef));
            return max;
        }

        /// <summary>Returns the skill defs that matter for this outpost type (for UI columns and production). Uses <see cref="Outpost_Production_Utils.GetSkillDefsFromMinCumulativeSkill"/> first when the def XML declares requirements.</summary>
        public static List<SkillDef> GetRelevantSkillDefs(WorldObjectDef def)
        {
            var list = new List<SkillDef>();
            if (def?.defName == null) return list;
            if (Outpost_Production_Utils.IsScavengingOutpost(def)) return list;
            var fromExt = Outpost_Production_Utils.GetSkillDefsFromMinCumulativeSkill(def);
            if (fromExt != null && fromExt.Count > 0)
            {
                list.AddRange(fromExt);
                return list;
            }
            string d = def.defName.ToLowerInvariant();
            if (d.Contains("farming")) list.Add(SkillDefOf.Plants);
            else if (d.Contains("hunting") || d.Contains("fishing") || d.Contains("ranch")) list.Add(SkillDefOf.Animals);
            else if (d.Contains("recruiting") || d.Contains("trading") || d.Contains("embassy") || d.Contains("town")) list.Add(SkillDefOf.Social);
            else if (d.Contains("mining")) list.Add(SkillDefOf.Mining);
            else if (d.Contains("fabrication") || d.Contains("production") || d.Contains("factory")) list.Add(SkillDefOf.Crafting);
            else if (d.Contains("research") || d.Contains("science") || d.Contains("academy")) list.Add(SkillDefOf.Intellectual);
            else if (d.Contains("construction") || d.Contains("mortar") || d.Contains("drilling")) list.Add(SkillDefOf.Construction);
            return list;
        }

        /// <summary>Skills for the pawns tab relevant column: academy uses the currently selected / locked teaching skill; other types use <see cref="GetRelevantSkillDefs"/>.</summary>
        public static List<SkillDef> GetRelevantSkillDefsForPawnsTab(WorldObject_WD_Outpost outpost)
        {
            var list = new List<SkillDef>();
            if (outpost?.def == null) return list;
            if (Outpost_Production_Utils.IsAcademyOutpost(outpost.def))
            {
                var sd = Outpost_Academy.GetSkillForCurrentCycle(outpost) ?? outpost.SelectedAcademySkill;
                if (sd != null) list.Add(sd);
                return list;
            }
            return GetRelevantSkillDefs(outpost.def);
        }

        /// <summary>Display name for the primary skill(s) of this outpost type (e.g. "Plants", "Animals").</summary>
        public static string GetRelevantSkillName(WorldObjectDef def)
        {
            if (Outpost_Production_Utils.IsScavengingOutpost(def))
            {
                string k = "TSA_WD_Outpost_RelevantStat_Pawns";
                string t = k.Translate().ToString();
                return (t == k || t.Contains("TSA_WD_")) ? "Pawns" : t;
            }
            var skills = GetRelevantSkillDefs(def);
            if (skills == null || skills.Count == 0) return "—";
            if (skills.Count == 1) return Outpost_Production_Utils.SkillLabelCap(skills[0]);
            return string.Join("/", skills.Select(sk => Outpost_Production_Utils.SkillLabelCap(sk)));
        }

        /// <summary>Raw sum of cumulative levels for each skill in <see cref="GetRelevantSkillDefs"/> (gates / avg labels).</summary>
        public float GetTotalRelevantSkillRaw()
        {
            if (def == null) return 0f;
            var skills = GetRelevantSkillDefs(def);
            if (skills == null || skills.Count == 0) return 0f;
            float total = 0f;
            foreach (var sd in skills)
                total += SumVirtualPawnSkillRaw(sd);
            return total;
        }

        /// <summary>Effective relevant skill (DR applied per skill, then summed).</summary>
        public float GetTotalRelevantSkill()
        {
            if (def == null) return 0f;
            var skills = GetRelevantSkillDefs(def);
            if (skills == null || skills.Count == 0) return 0f;
            float total = 0f;
            foreach (var sd in skills)
                total += SumVirtualPawnSkill(sd);
            return total;
        }

        /// <summary>Raw sum of <see cref="VirtualPawnSummary.GetSkill"/> for this skill across occupants and stored mechanoids.</summary>
        public float SumVirtualPawnSkillRaw(SkillDef skillDef)
        {
            if (skillDef == null) return 0f;
            float sum = 0f;
            var vps = VirtualPawns;
            if (vps != null)
            {
                foreach (var v in vps)
                    sum += v.GetSkill(skillDef);
            }
            var mechVps = MechanoidVirtualPawns;
            if (mechVps != null)
            {
                foreach (var v in mechVps)
                    sum += v.GetSkill(skillDef);
            }
            return sum;
        }

        /// <summary>Effective cumulative skill for production/capacity (diminishing returns on the raw sum).</summary>
        public float SumVirtualPawnSkill(SkillDef skillDef) => OutpostSkillScaling.ToEffective(SumVirtualPawnSkillRaw(skillDef));

        public string Name;
        /// <summary>Real pawns at this outpost. Owned only by this outpost (deep-scribed); never stored in WorldPawns so nothing else can discard them.</summary>
        private List<Pawn> occupants = new List<Pawn>();
        /// <summary>Stored non-worker pawns (pack animals and Vehicle Framework vehicle pawns). They are owned by the outpost but do not count as occupants.</summary>
        private List<Pawn> storedAnimalsAndVehicles = new List<Pawn>();
        /// <summary>Odyssey passenger shuttles held at the outpost (building things, not world pawns).</summary>
        private List<Thing> storedPassengerShuttles = new List<Thing>();
        /// <summary>Stored mechanoid workers: contribute production skills and combat strength but do not consume food or gain XP.</summary>
        private List<Pawn> storedMechanoids = new List<Pawn>();
        private Material cachedMaterial;
        private static Texture2D cachedAddToOutpostIcon;
        private static Texture2D cachedConvertFoodCaravanIcon;
        /// <summary>DefName of ThingDef to produce (set via Production gizmo). MVP: single choice.</summary>
        private string selectedProductionDefName;
        /// <summary>For hunting outposts: PawnKindDef.defName so we spawn meat + leather + wool for this animal.</summary>
        private string selectedPawnKindDefName;
        /// <summary>For fishing outposts: ThingDef.defName of the selected fish item.</summary>
        private string selectedFishDefName;
        private string lockedFishDefName;
        /// <summary>Ticks until current production completes; then a delivery traveler is spawned. Production time for testing: 1 in-game hour (2500 ticks).</summary>
        private int productionTicksLeft;
        /// <summary>For scavenging outposts: selected tier (persisted as enum int; -1 = none selected, production is paused).</summary>
        private int selectedScavengingKindRaw = -1;
        /// <summary>Locked "what" for this cycle (set when first 25% of cycle has elapsed). Used for delivery and UI "Current Cycle".</summary>
        private string lockedProductionDefName;
        private string lockedPawnKindDefName;
        private int lockedScavengingKindRaw = -1;
        /// <summary>Academy: <see cref="SkillDef.defName"/> locked for the current production cycle.</summary>
        private string lockedAcademySkillDefName;
        private bool lockedForThisCycle;
        /// <summary>Academy: selected skill to teach (<see cref="SkillDef.defName"/>).</summary>
        private string selectedAcademySkillDefName;

        /// <summary>Recruiting: optional skill priority for spawned recruits (null = any colonist).</summary>
        private string selectedRecruitPrioritySkillDefName;
        /// <summary>Time-weighted integral of delivery-driving capacity this cycle: sum of capacity × dt each production timer step (same units as spawn average).</summary>
        private float deliveryCapacityRunningSum;
        /// <summary>Total production-timer dt accumulated into <see cref="deliveryCapacityRunningSum"/> this cycle (ticks).</summary>
        private int deliveryCapacitySampleCount;
        /// <summary>When production time multiplier changes mid-cycle, rescale remaining ticks so the slider has immediate effect.</summary>
        private int cachedProductionIntervalForScale = -1;

        /// <summary>Set when the hourly slice zeros <see cref="productionTicksLeft"/> so the next idle pass runs immediately (not throttled).</summary>
        private bool forceIdleProductionCheck;
        /// <summary>Last tick we ran the heavy idle path (spawn attempt / recruiting / trading). Stuck-waiting outposts poll at <see cref="IdleProductionPollMinGapTicks"/>, not every sim tick.</summary>
        private int lastIdleProductionHeavyTick = -99999999;
        private const int IdleProductionPollMinGapTicks = 250;

        /// <summary>Cached: production paused due to runtime establishment requirements (min pawns, min skill, min nearby, colony skill, or recruiting/trading nearby). Updated on pawn change and optionally when UI opens or once per day for "min nearby".</summary>
        private bool cachedProductionPausedByRequirements;
        private List<string> cachedPauseReasons = new List<string>();
        /// <summary>Rebuilt when occupants change; avoids allocating every VirtualPawns read.</summary>
        private List<VirtualPawnSummary> cachedVirtualPawns;
        /// <summary>Rebuilt when stored mechanoids change.</summary>
        private List<VirtualPawnSummary> cachedMechanoidVirtualPawns;
        private string cachedInspectString;
        private int cachedInspectTick = -999;
        private int cachedResearchInspectFingerprint = int.MinValue;
        private bool manualDefenseActive;
        /// <summary>Soft second-chance after a failed auto-resolve; blocks stacking raids until the player commits.</summary>
        private bool pendingSkirmishDefense;
        private WorldObjectDef pendingSkirmishTravelerDef;
        private Faction pendingSkirmishEnemyFaction;
        private float pendingSkirmishTravelerStrength;
        private float pendingSkirmishInitialStrength;
        private int pendingSkirmishSpawnTick;
        private TravelerMission pendingSkirmishMission = TravelerMission.Raid;
        private RaidOrderOutcome pendingSkirmishRaidOrderOutcome = RaidOrderOutcome.PlayerOutpostConquestMenu;
        private int pendingSkirmishAlliedGoodwillPaid;
        private bool pendingSkirmishAlliedGoodwillRefunded;
        private WorldObject pendingSkirmishOrigin;
        private List<WorldObject> pendingSkirmishRaidAttackerList;
        private List<string> pendingSkirmishRaidAttackerDetails;
        private List<RaidForceLogRow> pendingSkirmishRaidAttackerForceRows;
        private List<RaidForceLogRow> pendingSkirmishRaidDefenderForceRows;
        private List<WorldObject> pendingSkirmishContributionKeys;
        private List<float> pendingSkirmishContributionValues;
        private float pendingSkirmishStrengthLost;
        private int pendingSkirmishPawnsHurt;
        /// <summary>When false, daily virtual healing skips hediff iteration on healthy garrisons.</summary>
        private bool occupantsNeedHealing;
        /// <summary>When false, daily virtual healing skips hediff iteration on healthy outpost prisoners.</summary>
        private bool prisonersNeedHealing;
        /// <summary>When true, auto-resolve and manual defense victories may take captives at this outpost.</summary>
        private bool takePrisoners = true;

        /// <summary>Recruitable captives held at this outpost (not Occupants until recruited). Deep-scribed; never in WorldPawns.</summary>
        private List<Pawn> prisoners = new List<Pawn>();

        private string expertStrategistThingId;
        private string expertEntertainerThingId;
        private string expertCookThingId;
        private string expertDoctorThingId;
        private string expertEngineerThingId;
        private string expertRecruiterThingId;

        /// <summary>Real pawns at this outpost. Only we own them (deep-scribed); we never put them in WorldPawns.</summary>
        public List<Pawn> Occupants => occupants ?? (occupants = new List<Pawn>());
        /// <summary>Outpost-held captives. Separate from Occupants: no combat or expert slots until recruited. Eating captives still consume virtual food.</summary>
        public List<Pawn> Prisoners => prisoners ?? (prisoners = new List<Pawn>());
        public List<Pawn> StoredAnimalsAndVehicles => storedAnimalsAndVehicles ?? (storedAnimalsAndVehicles = new List<Pawn>());

        public List<Thing> StoredPassengerShuttles => storedPassengerShuttles ?? (storedPassengerShuttles = new List<Thing>());
        public List<Pawn> StoredMechanoids => storedMechanoids ?? (storedMechanoids = new List<Pawn>());
        public int StoredMechanoidPawnCount
        {
            get
            {
                int count = 0;
                List<Pawn> list = StoredMechanoids;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    Pawn pawn = list[i];
                    if (pawn == null || pawn.Destroyed || pawn.Dead)
                    {
                        list.RemoveAt(i);
                        continue;
                    }
                    count++;
                }
                return count;
            }
        }
        public int StoredTransportPawnCount
        {
            get
            {
                int count = 0;
                List<Pawn> list = StoredAnimalsAndVehicles;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    Pawn pawn = list[i];
                    if (pawn == null || pawn.Destroyed || pawn.Dead)
                    {
                        list.RemoveAt(i);
                        continue;
                    }
                    count++;
                }
                if (ModsConfig.OdysseyActive)
                {
                    List<Thing> shuttles = StoredPassengerShuttles;
                    for (int i = 0; i < shuttles.Count; i++)
                    {
                        Thing t = shuttles[i];
                        if (t != null && !t.Destroyed)
                            count++;
                    }
                }
                return count;
            }
        }
        public bool ManualDefenseActive => manualDefenseActive;
        public bool PendingSkirmishDefense => pendingSkirmishDefense;
        public float PendingSkirmishStrengthLost => pendingSkirmishStrengthLost;
        public int PendingSkirmishPawnsHurt => pendingSkirmishPawnsHurt;
        public bool OccupantsNeedHealing => occupantsNeedHealing;
        public bool PrisonersNeedHealing => prisonersNeedHealing;
        /// <summary>Whether this outpost takes captives after winning a defense (auto-resolve or manual map).</summary>
        public bool TakePrisoners
        {
            get => takePrisoners;
            set => takePrisoners = value;
        }

        internal void SetOccupantsNeedHealing(bool value) => occupantsNeedHealing = value;
        internal void SetPrisonersNeedHealing(bool value) => prisonersNeedHealing = value;

        internal void NoteOccupantMaybeNeedsHealing(Pawn pawn)
        {
            if (Outpost_OccupantProgression.OccupantNeedsHealing(pawn))
                occupantsNeedHealing = true;
        }

        internal void NotePrisonerMaybeNeedsHealing(Pawn pawn)
        {
            if (Outpost_OccupantProgression.OccupantNeedsHealing(pawn))
                prisonersNeedHealing = true;
        }

        /// <summary>Flat injury severity healed per day at this outpost (settings base × hospital multiplier).</summary>
        public float GetEffectiveOccupantHealSeverityPerDay()
        {
            WorldDominationSettings settings = WorldDominationMod.settings;
            if (settings == null) return 0f;
            float baseSeverity = settings.outpostOccupantHealSeverityPerDay;
            if (baseSeverity <= 0f) return 0f;
            return baseSeverity * (1f + GetHospitalOccupantHealMultiplierBonus() + GetOutpostExpertOccupantHealMultiplierBonus());
        }

        public float GetOutpostExpertOccupantHealMultiplierBonus() =>
            OutpostExpertUtility.GetCombinedExpertOccupantHealBonus(this);

        /// <summary>Compatibility: summaries derived from Occupants for code that still expects VirtualPawns (e.g. production UI, establishment requirements).</summary>
        public List<VirtualPawnSummary> VirtualPawns
        {
            get
            {
                if (cachedVirtualPawns == null)
                    RebuildVirtualPawnsCache();
                return cachedVirtualPawns;
            }
        }

        /// <summary>Frozen skill snapshots for stored mechanoid workers.</summary>
        public List<VirtualPawnSummary> MechanoidVirtualPawns
        {
            get
            {
                if (cachedMechanoidVirtualPawns == null)
                    RebuildMechanoidVirtualPawnsCache();
                return cachedMechanoidVirtualPawns;
            }
        }

        private void RebuildVirtualPawnsCache()
        {
            if (cachedVirtualPawns == null)
                cachedVirtualPawns = new List<VirtualPawnSummary>();
            else
                cachedVirtualPawns.Clear();
            if (occupants == null) return;
            foreach (Pawn p in occupants)
            {
                VirtualPawnSummary v = VirtualPawnSummary.FromPawn(p);
                if (v != null) cachedVirtualPawns.Add(v);
            }
        }

        private void RebuildMechanoidVirtualPawnsCache()
        {
            if (cachedMechanoidVirtualPawns == null)
                cachedMechanoidVirtualPawns = new List<VirtualPawnSummary>();
            else
                cachedMechanoidVirtualPawns.Clear();
            if (storedMechanoids == null) return;
            foreach (Pawn p in storedMechanoids)
            {
                if (p == null || p.Destroyed || p.Dead) continue;
                VirtualPawnSummary v = VirtualPawnSummary.FromPawn(p);
                if (v != null) cachedMechanoidVirtualPawns.Add(v);
            }
        }

        /// <summary>Occupants plus mechanoid workers (for scavenging headcount and similar).</summary>
        public int WorkerPawnCount => Occupants.Count + StoredMechanoidPawnCount;

        /// <summary>Occupants and prisoners that consume virtual food (excludes androids and other non-eating humanlikes).</summary>
        public int CountOccupantsConsumingFood()
        {
            int count = 0;
            List<Pawn> list = Occupants;
            for (int i = 0; i < list.Count; i++)
            {
                if (OutpostPawnClassificationUtil.ConsumesVirtualFood(list[i]))
                    count++;
            }
            List<Pawn> captives = Prisoners;
            for (int i = 0; i < captives.Count; i++)
            {
                if (OutpostPawnClassificationUtil.ConsumesVirtualFood(captives[i]))
                    count++;
            }
            return count;
        }

        /// <summary>Add one pawn to this outpost. Removes from caravan and from WorldPawns so we are the sole owner; pawn is deep-scribed with the outpost.
        /// Pawn is despawned and not in WorldPawns, so it is not map-ticked (no needs, etc.); biological age and skills still advance via <see cref="Outpost_OccupantProgression"/>.</summary>
        public bool AddPawn(Pawn pawn, Caravan sourceCaravan = null!)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead) return false;

            pawn.ownership?.UnclaimAll();

            // Vehicle Framework: pawns in VehicleRoleHandler must leave VehiclePawn before Caravan.RemovePawn
            // (otherwise caravan needs tick / WorldGrid can throw — same order as VF StashedVehicle merge).
            VehicleFrameworkOutpostDissolveCompat.TryEjectPawnFromHostingVehicle(pawn);

            if (sourceCaravan != null)
                OdysseyShuttleOutpostEstablishmentCompat.TryStoreShuttlesFromPawnInventory(this, pawn, sourceCaravan);

            sourceCaravan?.RemovePawn(pawn);
            if (pawn.Spawned) pawn.DeSpawn();
            pawn.holdingOwner?.Remove(pawn);
            if (Find.WorldPawns != null && Find.WorldPawns.Contains(pawn))
                Find.WorldPawns.RemovePawn(pawn);

            if (!Occupants.Contains(pawn))
                Occupants.Add(pawn);
            ClearAutoAddBlockForPawn(pawn);
            NoteOccupantMaybeNeedsHealing(pawn);
            NotifyVirtualPawnsChanged();
            return true;
        }

        public bool StoreAnimalOrVehicle(Pawn pawn, Caravan sourceCaravan = null!)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead) return false;
            if (OutpostPawnClassificationUtil.IsMechanoidWorker(pawn)) return false;
            if (pawn.RaceProps?.Humanlike == true && !VehicleFrameworkOutpostDissolveCompat.IsVehicleFrameworkVehiclePawn(pawn))
                return false;

            VehicleFrameworkOutpostDissolveCompat.TryDetachVehiclePawnFromCaravanForStorage(sourceCaravan, pawn);
            VehicleFrameworkOutpostDissolveCompat.TryEjectPawnFromHostingVehicle(pawn);
            if (sourceCaravan != null && CaravanPawnListContains(sourceCaravan, pawn))
                sourceCaravan.RemovePawn(pawn);
            if (pawn.Spawned) pawn.DeSpawn();
            pawn.holdingOwner?.Remove(pawn);
            pawn.ownership?.UnclaimAll();
            if (Faction == Faction.OfPlayer && pawn.Faction != Faction.OfPlayer)
                pawn.SetFaction(Faction.OfPlayer);
            if (Find.WorldPawns != null && Find.WorldPawns.Contains(pawn))
                Find.WorldPawns.RemovePawn(pawn);

            if (!StoredAnimalsAndVehicles.Contains(pawn))
                StoredAnimalsAndVehicles.Add(pawn);
            GetComponent<CompViralSpread>()?.UpdateOutpostStrengthLogically();
            return true;
        }

        public bool StorePassengerShuttle(Building_PassengerShuttle shuttle, Caravan sourceCaravan = null!)
        {
            if (shuttle == null || shuttle.Destroyed) return false;
            if (!ModsConfig.OdysseyActive) return false;
            if (StoredPassengerShuttles.Contains(shuttle)) return true;

            if (sourceCaravan != null && !sourceCaravan.Destroyed)
            {
                Pawn owner = CaravanInventoryUtility.GetOwnerOf(sourceCaravan, shuttle);
                owner?.inventory?.innerContainer?.Remove(shuttle);
            }

            shuttle.holdingOwner?.Remove(shuttle);

            if (shuttle.Spawned)
                shuttle.DeSpawn(DestroyMode.Vanish);

            StoredPassengerShuttles.Add(shuttle);
            GetComponent<CompViralSpread>()?.UpdateOutpostStrengthLogically();
            return true;
        }

        public Building_PassengerShuttle RemoveStoredPassengerShuttle(Thing shuttle)
        {
            if (manualDefenseActive) return null;
            if (shuttle == null || !StoredPassengerShuttles.Contains(shuttle)) return null;
            StoredPassengerShuttles.Remove(shuttle);
            GetComponent<CompViralSpread>()?.UpdateOutpostStrengthLogically();
            return shuttle as Building_PassengerShuttle;
        }

        public void PruneStoredPassengerShuttles()
        {
            var list = StoredPassengerShuttles;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                Thing t = list[i];
                if (t == null || t.Destroyed || !OdysseyShuttleOutpostEstablishmentCompat.IsPassengerShuttle(t))
                    list.RemoveAt(i);
            }
        }

        public bool StoreMechanoid(Pawn pawn, Caravan sourceCaravan = null!)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead) return false;
            if (!OutpostPawnClassificationUtil.IsMechanoidWorker(pawn)) return false;

            VehicleFrameworkOutpostDissolveCompat.TryEjectPawnFromHostingVehicle(pawn);
            if (sourceCaravan != null && CaravanPawnListContains(sourceCaravan, pawn))
                sourceCaravan.RemovePawn(pawn);
            if (pawn.Spawned) pawn.DeSpawn();
            pawn.holdingOwner?.Remove(pawn);
            pawn.ownership?.UnclaimAll();
            if (Faction == Faction.OfPlayer && pawn.Faction != Faction.OfPlayer)
                pawn.SetFaction(Faction.OfPlayer);
            if (Find.WorldPawns != null && Find.WorldPawns.Contains(pawn))
                Find.WorldPawns.RemovePawn(pawn);

            if (!StoredMechanoids.Contains(pawn))
                StoredMechanoids.Add(pawn);
            NotifyMechanoidsChanged();
            return true;
        }

        public Pawn RemoveStoredMechanoid(Pawn pawn)
        {
            if (manualDefenseActive) return null;
            if (pawn == null || !StoredMechanoids.Contains(pawn)) return null;
            StoredMechanoids.Remove(pawn);
            pawn.holdingOwner?.Remove(pawn);
            if (Faction == Faction.OfPlayer && pawn.Faction != Faction.OfPlayer)
                pawn.SetFaction(Faction.OfPlayer);
            NotifyMechanoidsChanged();
            return pawn;
        }

        private bool StoreNonHumanlikeDissolvePawn(Pawn pawn, Caravan sourceCaravan)
        {
            if (OutpostPawnClassificationUtil.IsMechanoidWorker(pawn))
                return StoreMechanoid(pawn, sourceCaravan);
            return StoreAnimalOrVehicle(pawn, sourceCaravan);
        }

        internal void NotifyMechanoidsChanged()
        {
            cachedMechanoidVirtualPawns = null;
            InvalidateInspectCache();
            GetComponent<CompViralSpread>()?.UpdateOutpostStrengthLogically();
            RecomputeProductionRequirementCache();
            if (WorldDominationMod.settings != null && WorldDominationMod.settings.foodLogisticsActive)
            {
                var logi = GetComponent<CompOutpostLogistics>();
                if (logi != null)
                    Find.World?.GetComponent<WorldComponent_LogisticsManager>()?.NotifyFoodLogisticsInputsChanged();
            }
        }

        public Pawn RemoveStoredAnimalOrVehicle(Pawn pawn)
        {
            if (manualDefenseActive) return null;
            if (pawn == null || !StoredAnimalsAndVehicles.Contains(pawn)) return null;
            StoredAnimalsAndVehicles.Remove(pawn);
            pawn.holdingOwner?.Remove(pawn);
            if (Faction == Faction.OfPlayer && pawn.Faction != Faction.OfPlayer)
                pawn.SetFaction(Faction.OfPlayer);
            GetComponent<CompViralSpread>()?.UpdateOutpostStrengthLogically();
            return pawn;
        }

        public void RemoveStoredAnimalsOrVehiclesAsCaravan(IReadOnlyList<Pawn> pawns)
        {
            if (pawns == null || pawns.Count == 0) return;
            var removed = new List<Pawn>(pawns.Count);
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = RemoveStoredAnimalOrVehicle(pawns[i]);
                if (pawn != null && !pawn.Destroyed && !pawn.Dead)
                    removed.Add(pawn);
            }
            if (removed.Count == 0) return;
            Caravan caravan = CaravanMaker.MakeCaravan(removed, Faction ?? Faction.OfPlayer, Tile, true);
            SelectOnlyCreatedCaravan(caravan);
        }

        /// <summary>Evacuate a fraction of living occupants as a retreat caravan when the outpost falls to a non-decisive simulated defeat.</summary>
        public void SpawnRetreatCaravan(float survivalFraction)
        {
            survivalFraction = Mathf.Clamp01(survivalFraction);
            if (survivalFraction <= 0.01f) return;

            List<Pawn> living = new List<Pawn>();
            List<Pawn> occ = Occupants;
            for (int i = 0; i < occ.Count; i++)
            {
                Pawn p = occ[i];
                if (p != null && !p.Destroyed && !p.Dead)
                    living.Add(p);
            }
            if (living.Count == 0) return;

            int evacCount = Mathf.Clamp(Mathf.CeilToInt(survivalFraction * living.Count), 1, living.Count);
            living.Sort((a, b) => GetRetreatPriority(b).CompareTo(GetRetreatPriority(a)));

            var toEvac = new List<Pawn>(evacCount);
            for (int i = 0; i < evacCount && i < living.Count; i++)
                toEvac.Add(living[i]);

            var removed = new List<Pawn>(toEvac.Count);
            for (int i = 0; i < toEvac.Count; i++)
            {
                Pawn r = RemovePawn(toEvac[i]);
                if (r != null && !r.Destroyed && !r.Dead)
                    removed.Add(r);
            }
            if (removed.Count == 0) return;

            Caravan caravan = CaravanMaker.MakeCaravan(removed, Faction ?? Faction.OfPlayer, Tile, true);
            if (caravan != null)
            {
                SelectOnlyCreatedCaravan(caravan);
                Find.LetterStack.ReceiveLetter(
                    "TSA_WD_OutpostRetreat_Letter_Label".Translate(),
                    "TSA_WD_OutpostRetreat_Letter_Text".Translate(LabelCap, removed.Count),
                    LetterDefOf.NegativeEvent,
                    caravan);
            }
        }

        private static float GetRetreatPriority(Pawn pawn)
        {
            if (pawn == null) return 0f;
            int shoot = pawn.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0;
            int melee = pawn.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0;
            return shoot + melee + (pawn.kindDef?.combatPower ?? 0f);
        }

        public void AddGeneratedPawnToOutpost(Pawn pawn)
        {
            if (!AddPawn(pawn, null)) return;
            NotifyVirtualPawnsChanged();
        }

        public bool HasLivingManualDefensePawns()
        {
            if (manualDefenseActive) return true;
            List<Pawn> list = Occupants;
            for (int i = 0; i < list.Count; i++)
            {
                Pawn p = list[i];
                if (p != null && !p.Destroyed && !p.Dead)
                    return true;
            }
            return false;
        }

        public bool BlocksAutoRaidResolution()
            => manualDefenseActive
               || pendingSkirmishDefense
               || WD_MapComponent_OutpostDefense.HasActiveEncounterFor(this);

        public void SetPendingSkirmishLossSummary(float strengthLost, int pawnsHurt)
        {
            pendingSkirmishStrengthLost = Mathf.Max(0f, strengthLost);
            pendingSkirmishPawnsHurt = Mathf.Max(0, pawnsHurt);
        }

        public void CapturePendingSkirmishFromTraveler(WorldObject_Traveler traveler)
        {
            if (traveler == null) return;
            pendingSkirmishDefense = true;
            pendingSkirmishTravelerDef = traveler.def;
            pendingSkirmishEnemyFaction = traveler.Faction;
            pendingSkirmishTravelerStrength = traveler.travelerStrength;
            pendingSkirmishInitialStrength = traveler.initialStrength > 0f ? traveler.initialStrength : traveler.travelerStrength;
            pendingSkirmishSpawnTick = traveler.spawnTick > 0 ? traveler.spawnTick : Find.TickManager.TicksGame;
            pendingSkirmishMission = traveler.mission;
            pendingSkirmishRaidOrderOutcome = traveler.raidOrderOutcome;
            pendingSkirmishAlliedGoodwillPaid = traveler.alliedRaidOrderGoodwillPaid;
            pendingSkirmishAlliedGoodwillRefunded = traveler.alliedRaidOrderGoodwillRefunded;
            pendingSkirmishOrigin = traveler.originObject;
            pendingSkirmishRaidAttackerList = traveler.raidAttackerList != null
                ? new List<WorldObject>(traveler.raidAttackerList)
                : new List<WorldObject>();
            pendingSkirmishRaidAttackerDetails = traveler.raidAttackerDetails != null
                ? new List<string>(traveler.raidAttackerDetails)
                : new List<string>();
            pendingSkirmishRaidAttackerForceRows = RaidForceLogRow.CloneList(traveler.raidAttackerForceRows);
            pendingSkirmishRaidDefenderForceRows = RaidForceLogRow.CloneList(traveler.raidDefenderForceRows);
            pendingSkirmishContributionKeys = new List<WorldObject>();
            pendingSkirmishContributionValues = new List<float>();
            if (traveler.contributionFactors != null)
            {
                foreach (var kv in traveler.contributionFactors)
                {
                    pendingSkirmishContributionKeys.Add(kv.Key);
                    pendingSkirmishContributionValues.Add(kv.Value);
                }
            }
        }

        public WorldObject_Traveler RecreatePendingSkirmishTraveler()
        {
            if (!pendingSkirmishDefense) return null;
            WorldObjectDef def = pendingSkirmishTravelerDef
                ?? DefDatabase<WorldObjectDef>.GetNamedSilentFail("TSA_WD_Traveler_Outpost_Raid");
            if (def == null) return null;

            var traveler = (WorldObject_Traveler)WorldObjectMaker.MakeWorldObject(def);
            traveler.SetFaction(pendingSkirmishEnemyFaction);
            traveler.travelerStrength = pendingSkirmishTravelerStrength;
            traveler.initialStrength = pendingSkirmishInitialStrength > 0f
                ? pendingSkirmishInitialStrength
                : pendingSkirmishTravelerStrength;
            traveler.spawnTick = pendingSkirmishSpawnTick > 0 ? pendingSkirmishSpawnTick : Find.TickManager.TicksGame;
            traveler.mission = pendingSkirmishMission;
            traveler.originObject = pendingSkirmishOrigin;
            traveler.targetObject = this;
            traveler.raidOrderOutcome = pendingSkirmishRaidOrderOutcome;
            traveler.alliedRaidOrderGoodwillPaid = pendingSkirmishAlliedGoodwillPaid;
            traveler.alliedRaidOrderGoodwillRefunded = pendingSkirmishAlliedGoodwillRefunded;
            traveler.raidAttackerList = pendingSkirmishRaidAttackerList != null
                ? new List<WorldObject>(pendingSkirmishRaidAttackerList)
                : new List<WorldObject>();
            traveler.raidAttackerDetails = pendingSkirmishRaidAttackerDetails != null
                ? new List<string>(pendingSkirmishRaidAttackerDetails)
                : new List<string>();
            traveler.raidAttackerForceRows = RaidForceLogRow.CloneList(pendingSkirmishRaidAttackerForceRows);
            traveler.raidDefenderForceRows = RaidForceLogRow.CloneList(pendingSkirmishRaidDefenderForceRows);
            traveler.contributionFactors = new Dictionary<WorldObject, float>();
            if (pendingSkirmishContributionKeys != null && pendingSkirmishContributionValues != null)
            {
                int count = Math.Min(pendingSkirmishContributionKeys.Count, pendingSkirmishContributionValues.Count);
                for (int i = 0; i < count; i++)
                {
                    if (pendingSkirmishContributionKeys[i] != null)
                        traveler.contributionFactors[pendingSkirmishContributionKeys[i]] = pendingSkirmishContributionValues[i];
                }
            }
            return traveler;
        }

        public void ClearPendingSkirmishDefense()
        {
            pendingSkirmishDefense = false;
            pendingSkirmishTravelerDef = null;
            pendingSkirmishEnemyFaction = null;
            pendingSkirmishTravelerStrength = 0f;
            pendingSkirmishInitialStrength = 0f;
            pendingSkirmishSpawnTick = 0;
            pendingSkirmishMission = TravelerMission.Raid;
            pendingSkirmishRaidOrderOutcome = RaidOrderOutcome.PlayerOutpostConquestMenu;
            pendingSkirmishAlliedGoodwillPaid = 0;
            pendingSkirmishAlliedGoodwillRefunded = false;
            pendingSkirmishOrigin = null;
            pendingSkirmishRaidAttackerList = null;
            pendingSkirmishRaidAttackerDetails = null;
            pendingSkirmishRaidAttackerForceRows = null;
            pendingSkirmishRaidDefenderForceRows = null;
            pendingSkirmishContributionKeys = null;
            pendingSkirmishContributionValues = null;
            pendingSkirmishStrengthLost = 0f;
            pendingSkirmishPawnsHurt = 0;
        }

        public List<Pawn> ExtractManualDefensePawns(IReadOnlyList<Pawn> onlyThese = null)
        {
            if (manualDefenseActive) return new List<Pawn>();
            var extracted = new List<Pawn>();
            HashSet<int> filter = null;
            if (onlyThese != null && onlyThese.Count > 0)
            {
                filter = new HashSet<int>();
                for (int i = 0; i < onlyThese.Count; i++)
                {
                    Pawn want = onlyThese[i];
                    if (want != null) filter.Add(want.thingIDNumber);
                }
            }

            List<Pawn> list = Occupants;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                Pawn p = list[i];
                if (p == null || p.Destroyed || p.Dead) continue;
                if (filter != null && !filter.Contains(p.thingIDNumber)) continue;
                p.GetCaravan()?.RemovePawn(p);
                p.holdingOwner?.Remove(p);
                if (p.Spawned) p.DeSpawn();
                list.RemoveAt(i);
                if (Faction == Faction.OfPlayer && p.Faction != Faction.OfPlayer)
                    p.SetFaction(Faction.OfPlayer);
                extracted.Add(p);
            }

            if (extracted.Count > 0)
            {
                manualDefenseActive = true;
                NotifyVirtualPawnsChanged();
            }
            return extracted;
        }

        public List<Pawn> ExtractManualDefenseStoredTransportPawns()
        {
            var extracted = new List<Pawn>();
            List<Pawn> list = StoredAnimalsAndVehicles;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                Pawn p = list[i];
                if (p == null || p.Destroyed || p.Dead) continue;
                p.GetCaravan()?.RemovePawn(p);
                p.holdingOwner?.Remove(p);
                if (p.Spawned) p.DeSpawn();
                list.RemoveAt(i);
                if (Faction == Faction.OfPlayer && p.Faction != Faction.OfPlayer)
                    p.SetFaction(Faction.OfPlayer);
                extracted.Add(p);
            }

            if (extracted.Count > 0)
            {
                InvalidateInspectCache();
                GetComponent<CompViralSpread>()?.UpdateOutpostStrengthLogically();
            }
            return extracted;
        }

        public List<Pawn> ExtractManualDefenseMechanoids()
        {
            var extracted = new List<Pawn>();
            List<Pawn> list = StoredMechanoids;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                Pawn p = list[i];
                if (p == null || p.Destroyed || p.Dead) continue;
                p.GetCaravan()?.RemovePawn(p);
                p.holdingOwner?.Remove(p);
                if (p.Spawned) p.DeSpawn();
                list.RemoveAt(i);
                if (Faction == Faction.OfPlayer && p.Faction != Faction.OfPlayer)
                    p.SetFaction(Faction.OfPlayer);
                extracted.Add(p);
            }

            if (extracted.Count > 0)
                NotifyMechanoidsChanged();
            return extracted;
        }

        public int ReturnManualDefensePawns(IEnumerable<Pawn> pawns)
        {
            int returned = 0;
            if (pawns != null)
            {
                foreach (Pawn pawn in pawns)
                {
                    if (pawn == null || pawn.Destroyed || pawn.Dead) continue;
                    pawn.GetCaravan()?.RemovePawn(pawn);
                    if (pawn.Spawned) pawn.DeSpawn();
                    Outpost_OccupantProgression.TryRefreshOccupantHealthState(pawn);
                    if (AddPawn(pawn, null))
                        returned++;
                }
            }

            ClearManualDefenseActive();
            return returned;
        }
        public int ReturnManualDefenseStoredTransportPawns(IEnumerable<Pawn> pawns)
        {
            int returned = 0;
            if (pawns != null)
            {
                foreach (Pawn pawn in pawns)
                {
                    if (pawn == null || pawn.Destroyed || pawn.Dead) continue;
                    if (pawn.Spawned) pawn.DeSpawn();
                    if (StoreAnimalOrVehicle(pawn, null!))
                        returned++;
                }
            }
            return returned;
        }

        public int ReturnManualDefenseMechanoids(IEnumerable<Pawn> pawns)
        {
            int returned = 0;
            if (pawns != null)
            {
                foreach (Pawn pawn in pawns)
                {
                    if (pawn == null || pawn.Destroyed || pawn.Dead) continue;
                    if (pawn.Spawned) pawn.DeSpawn();
                    if (StoreMechanoid(pawn, null!))
                        returned++;
                }
            }
            return returned;
        }

        public void ClearManualDefenseActive()
        {
            if (!manualDefenseActive) return;
            manualDefenseActive = false;
            NotifyVirtualPawnsChanged();
        }

        public override void Destroy()
        {
            Log.Warning($"[TSA WD] Destroying outpost '{Label}' tile={Tile.tileId} def={def?.defName} occupants={Occupants?.Count ?? 0} prisoners={Prisoners?.Count ?? 0} manualDefense={manualDefenseActive}\n{StackTraceUtility.ExtractStackTrace()}");
            DestroyAllPrisoners();
            ClearPendingSkirmishDefense();
            if (IsPowerPlantOutpost)
                Outpost_PowerPlant.NotifyRemotePowerDirty();
            bool wasWarehouse = Outpost_Production_Utils.IsWarehouseOutpost(def);
            base.Destroy();
            if (wasWarehouse)
                OutpostWarehouseAuraUtility.InvalidateCache();
        }

        private static void PruneInvalidPawns(List<Pawn> list)
        {
            if (list == null) return;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                Pawn pawn = list[i];
                if (pawn == null || pawn.Destroyed || pawn.Dead)
                    list.RemoveAt(i);
            }
        }

        /// <summary>Drop null / dead / destroyed captives only. Keeps unwavering prisoners.</summary>
        private void PruneInvalidPrisonersRuntime()
        {
            List<Pawn> list = prisoners;
            if (list == null || list.Count == 0) return;
            int before = list.Count;
            PruneInvalidPawns(list);
            if (list.Count == before) return;
            NotifyVirtualPawnsChanged();
            Window_Prisoners.InvalidateCache();
        }

        private void RunPostLoadSanityRecovery()
        {
            PruneInvalidPawns(occupants);
            PruneInvalidPawns(prisoners);
            PruneInvalidPawns(storedAnimalsAndVehicles);
            PruneInvalidPawns(storedMechanoids);
            PruneStoredPassengerShuttles();

            if (manualDefenseActive && !WD_MapComponent_OutpostDefense.HasActiveEncounterFor(this))
            {
                Log.Warning($"[TSA WD] Outpost '{Label}' had orphaned manualDefenseActive on load; clearing flag.");
                ClearManualDefenseActive();
            }

            if (pendingSkirmishDefense)
            {
                LongEventHandler.ExecuteWhenFinished(() =>
                    WD_OutpostDefenseSkirmishUtility.TryReopenPendingSkirmishDialogs());
            }

            WDVerbose.Msg($"[TSA WD] Outpost load sanity: '{Label}' tile={Tile.tileId} def={def?.defName} occupants={Occupants.Count} stored={StoredAnimalsAndVehicles.Count} mechs={StoredMechanoids.Count} manualDefense={manualDefenseActive} pendingSkirmish={pendingSkirmishDefense}");
        }

        /// <summary>Outpost tier from def (1, 2, 3, or 4+ for mods). No clamping.</summary>
        public static int GetOutpostTier(WorldObjectDef outpostDef)
        {
            var ext = outpostDef?.GetModExtension<OutpostDefExtension>();
            return ext?.outpostTier ?? 1;
        }

        /// <summary>This outpost's tier from def.</summary>
        public int OutpostTier => GetOutpostTier(def);

        public override string Label => !string.IsNullOrEmpty(Name) ? Name : (def?.label ?? "Outpost");
        public override bool HasName => !string.IsNullOrEmpty(Name);

        public int PawnCount => Occupants.Count;

        /// <summary>Combat strength from occupants plus stored animals/vehicles and mechanoids (capped at 1500). WD trader arrivals refill CompViralSpread offensive toward this max only (no tier upgrades).</summary>
        public float GetTargetStrength()
            => Mathf.Min(GetTargetStrengthUncapped(), 1500f);

        /// <summary>Same outpost offensive strength calculation as <see cref="GetTargetStrength"/>, but without the 1500 cap.</summary>
        public float GetTargetStrengthUncapped()
        {
            float baseStrength = 100f;
            float workerStrength = 0f;
            var vps = VirtualPawns;
            if (vps != null && vps.Count > 0)
            {
                foreach (var v in vps)
                    workerStrength += v.CombatStrength;
            }
            var mechVps = MechanoidVirtualPawns;
            if (mechVps != null && mechVps.Count > 0)
            {
                foreach (var v in mechVps)
                    workerStrength += v.CombatStrength;
            }
            if (workerStrength > 0f)
                baseStrength = workerStrength;
            baseStrength += GetStoredTransportCombatStrength();
            if (IsRapidResponseOutpost)
                baseStrength *= 1f + GetRapidResponseOffensiveStrengthBonus();
            return baseStrength;
        }

        private float GetStoredTransportCombatStrength()
        {
            float total = 0f;
            List<Pawn> list = StoredAnimalsAndVehicles;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                Pawn pawn = list[i];
                if (pawn == null || pawn.Destroyed || pawn.Dead)
                {
                    list.RemoveAt(i);
                    continue;
                }
                if (pawn.RaceProps?.Humanlike == true) continue;
                total += GetStoredTransportCombatStrength(pawn);
            }
            return total;
        }

        public static float GetStoredTransportCombatStrength(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead) return 0f;
            if (pawn.RaceProps?.Humanlike == true) return 0f;
            return Mathf.Max(0f, pawn.kindDef?.combatPower ?? 0f);
        }

        private float SumWorkerSkill(System.Func<VirtualPawnSummary, float> getSkill)
        {
            float sum = 0f;
            var vps = VirtualPawns;
            if (vps != null)
            {
                foreach (var v in vps)
                    sum += getSkill(v);
            }
            var mechVps = MechanoidVirtualPawns;
            if (mechVps != null)
            {
                foreach (var v in mechVps)
                    sum += getSkill(v);
            }
            return sum;
        }

        private float SumOccupantSkill(System.Func<VirtualPawnSummary, float> getSkill)
        {
            return SumWorkerSkill(getSkill);
        }

        /// <summary>Raw total plants skill.</summary>
        public float TotalPlantsSkillRaw()
        {
            if (Occupants == null) return 0f;
            return SumOccupantSkill(v => v.ProductionSkill(true));
        }

        /// <summary>Effective plants skill (for farming production).</summary>
        public float TotalPlantsSkill() => OutpostSkillScaling.ToEffective(TotalPlantsSkillRaw());

        /// <summary>Raw total Animals skill.</summary>
        public float TotalHuntingSkillRaw()
        {
            if (Occupants == null) return 0f;
            return SumOccupantSkill(v => v.animals);
        }

        /// <summary>Effective Animals skill (for hunting / ranch production).</summary>
        public float TotalHuntingSkill() => OutpostSkillScaling.ToEffective(TotalHuntingSkillRaw());

        /// <summary>Food production capacity: effective sum of relevant skills. Non-food outposts return 0.</summary>
        public float GetFoodProductionCapacity()
        {
            if (Outpost_Production_Utils.IsFarmingOutpost(def)) return TotalPlantsSkill();
            if (Outpost_Production_Utils.IsHuntingOutpost(def)) return TotalHuntingSkill();
            if (Outpost_Production_Utils.IsFishingOutpost(def)) return TotalHuntingSkill();
            if (Outpost_Production_Utils.IsRanchOutpost(def)) return TotalHuntingSkill();
            return 0f;
        }

        public float GetFoodProductionCapacityRaw()
        {
            if (Outpost_Production_Utils.IsFarmingOutpost(def)) return TotalPlantsSkillRaw();
            if (Outpost_Production_Utils.IsHuntingOutpost(def)) return TotalHuntingSkillRaw();
            if (Outpost_Production_Utils.IsFishingOutpost(def)) return TotalHuntingSkillRaw();
            if (Outpost_Production_Utils.IsRanchOutpost(def)) return TotalHuntingSkillRaw();
            return 0f;
        }

        /// <summary>Raw Mining skill.</summary>
        public float TotalMiningSkillRaw()
        {
            if (Occupants == null) return 0f;
            return SumOccupantSkill(v => v.mining);
        }

        /// <summary>Effective Mining skill (for mining outpost production).</summary>
        public float TotalMiningSkill() => OutpostSkillScaling.ToEffective(TotalMiningSkillRaw());

        /// <summary>Raw construction skill.</summary>
        public float TotalConstructionSkillRaw()
        {
            if (Occupants == null) return 0f;
            return SumOccupantSkill(v => v.construction);
        }

        /// <summary>Effective construction skill (road progress speed).</summary>
        public float TotalConstructionSkill() => OutpostSkillScaling.ToEffective(TotalConstructionSkillRaw());

        /// <summary>Notify CompViralSpread that VirtualPawns changed so strength is updated (event-based, no tick polling). Also recomputes production requirement cache.</summary>
        internal void NotifyVirtualPawnsChanged()
        {
            RebuildVirtualPawnsCache();
            cachedProductionTicksInterval = -1;
            InvalidateInspectCache();
            OutpostExpertUtility.ValidateAssignments(this);
            GetComponent<CompViralSpread>()?.UpdateOutpostStrengthLogically();
            if (IsAcademyOutpost)
                Outpost_Academy.ValidateTeachingStateAfterOccupantsChanged(this);
            RecomputeProductionRequirementCache();
            if (WorldDominationMod.settings != null && WorldDominationMod.settings.foodLogisticsActive)
            {
                var logi = GetComponent<CompOutpostLogistics>();
                if (logi != null)
                {
                    var mgr = Find.World?.GetComponent<WorldComponent_LogisticsManager>();
                    // Any pawn change can shift demand/production; producers must re-run smart assignment.
                    mgr?.NotifyFoodLogisticsInputsChanged();
                }
            }
        }

        private void InvalidateInspectCache()
        {
            cachedInspectString = null;
            cachedInspectTick = -999;
            cachedResearchInspectFingerprint = int.MinValue;
        }

        internal void InvalidateInspectCachePublic() => InvalidateInspectCache();

        public string GetExpertThingId(OutpostExpertRole role) => role switch
        {
            OutpostExpertRole.Strategist => expertStrategistThingId,
            OutpostExpertRole.Entertainer => expertEntertainerThingId,
            OutpostExpertRole.Cook => expertCookThingId,
            OutpostExpertRole.Doctor => expertDoctorThingId,
            OutpostExpertRole.Engineer => expertEngineerThingId,
            OutpostExpertRole.Recruiter => expertRecruiterThingId,
            _ => null
        };

        internal void SetExpertThingId(OutpostExpertRole role, string thingId)
        {
            thingId = thingId.NullOrEmpty() ? null : thingId;
            switch (role)
            {
                case OutpostExpertRole.Strategist: expertStrategistThingId = thingId; break;
                case OutpostExpertRole.Entertainer: expertEntertainerThingId = thingId; break;
                case OutpostExpertRole.Cook: expertCookThingId = thingId; break;
                case OutpostExpertRole.Doctor: expertDoctorThingId = thingId; break;
                case OutpostExpertRole.Engineer: expertEngineerThingId = thingId; break;
                case OutpostExpertRole.Recruiter: expertRecruiterThingId = thingId; break;
            }
        }

        public Pawn GetAssignedExpert(OutpostExpertRole role)
        {
            string id = GetExpertThingId(role);
            if (id.NullOrEmpty()) return null;
            return FindOccupantByThingId(id);
        }

        public bool TryAssignExpert(OutpostExpertRole role, Pawn pawn) =>
            OutpostExpertUtility.TryAssignExpert(this, role, pawn);

        public void ClearExpert(OutpostExpertRole role) =>
            OutpostExpertUtility.ClearExpert(this, role);

        internal Pawn FindOccupantByThingId(string thingId)
        {
            if (thingId.NullOrEmpty() || occupants == null) return null;
            for (int i = 0; i < occupants.Count; i++)
            {
                Pawn p = occupants[i];
                if (p != null && !p.Destroyed && !p.Dead && p.ThingID == thingId)
                    return p;
            }
            return null;
        }

        internal bool IsExpertAssignedElsewhere(string thingId, OutpostExpertRole exceptRole)
        {
            if (thingId.NullOrEmpty()) return false;
            foreach (OutpostExpertRole role in Enum.GetValues(typeof(OutpostExpertRole)))
            {
                if (role == exceptRole) continue;
                if (GetExpertThingId(role) == thingId) return true;
            }
            return false;
        }

        internal void ClearExpertFromAllRoles(string thingId)
        {
            if (thingId.NullOrEmpty()) return;
            foreach (OutpostExpertRole role in Enum.GetValues(typeof(OutpostExpertRole)))
            {
                if (GetExpertThingId(role) == thingId)
                    SetExpertThingId(role, null);
            }
        }

        private string CacheInspectString(string inspect)
        {
            cachedInspectString = CompViralSpread.NormalizeRaidVulnerableLabelColor(inspect);
            return cachedInspectString;
        }

        /// <summary>Recomputes cached pause state (min pawns, min skill, min nearby, colony skill, recruiting/trading nearby). Call on pawn change; optionally when UI opens or once per day for "min nearby".</summary>
        public void RecomputeProductionRequirementCache()
        {
            if (cachedPauseReasons == null) cachedPauseReasons = new List<string>();
            cachedPauseReasons.Clear();
            if (IsResearchOutpost)
            {
                // Research ticks on CanResearchNow only (active project + Intellectual); keep UI in sync.
                if (!Outpost_Research.CanResearchNow(this, out string researchPause) && !string.IsNullOrEmpty(researchPause))
                    cachedPauseReasons.Add(researchPause);
            }
            else
            {
                Outpost_EstablishmentRequirements.GetProductionPauseReasons(this, cachedPauseReasons);
                if (Outpost_Production_Utils.IsFoodProducerOutpost(def) && Outpost_Production_Utils.GetSkillAssignedToPhysicalProduction(this) < 0.01f)
                    cachedPauseReasons.Add("TSA_WD_Outpost_Inspect_ProducingPausedNoPhysicalSkill".Translate().ToString());
                if ((Outpost_Production_Utils.IsRecruitingOutpost(def) || Outpost_Production_Utils.IsTradingOutpost(def)) && Outpost_Trading.GetNearbySettlementCount(this) == 0)
                    cachedPauseReasons.Add("TSA_WD_Outpost_Inspect_ProducingPausedNoNearbySettlements".Translate().ToString());
                if (Outpost_Production_Utils.IsEmbassyOutpost(def) && Outpost_Embassy.GetNearbySettlementCount(this) == 0)
                    cachedPauseReasons.Add("TSA_WD_Outpost_Inspect_ProducingPausedNoNearbySettlements".Translate().ToString());
            }
            cachedProductionPausedByRequirements = cachedPauseReasons.Count > 0;
        }

        /// <summary>True if production may run; false if paused with reason set (first reason only). For inspect pane.</summary>
        public bool GetProductionPauseReason(out string reason)
        {
            reason = (cachedPauseReasons != null && cachedPauseReasons.Count > 0) ? cachedPauseReasons[0] : null;
            return !cachedProductionPausedByRequirements;
        }

        /// <summary>Read-only list of all pause reasons. For production dialog. Ensure cache is fresh (e.g. RecomputeProductionRequirementCache when opening dialog) if "min nearby" might have changed.</summary>
        public IReadOnlyList<string> GetProductionPauseReasons()
        {
            if (cachedPauseReasons == null) cachedPauseReasons = new List<string>();
            return cachedPauseReasons;
        }

        public void AddVirtualPawn(VirtualPawnSummary summary)
        {
            if (summary?.pawn != null && !summary.pawn.Destroyed)
                AddPawn(summary.pawn, null);
        }

        public void AddVirtualPawnsFromCaravan(IEnumerable<Pawn> pawns)
        {
            if (pawns == null) return;
            foreach (var p in pawns)
                AddPawn(p, null);
        }

        public override Material Material
        {
            get
            {
                if (cachedMaterial == null && Faction != null)
                {
                    // Globe-mesh art is planet-tangent. Keep it settlement-style; outpost-type art belongs
                    // exclusively to ExpandingIcon so vanilla zoom crossfades never show a rotated duplicate.
                    string path = Faction.def?.settlementTexturePath
                        ?? "World/WorldObjects/Settlements/Settlement";
                    cachedMaterial = MaterialPool.MatFrom(
                        path,
                        ShaderDatabase.WorldOverlayTransparentLit,
                        Faction.Color,
                        WorldMaterials.WorldObjectRenderQueue);
                }
                return cachedMaterial ?? base.Material;
            }
        }

        /// <summary>AA upgrade swaps only the upright expanding icon, never the planet-tangent globe mesh.</summary>
        public override Texture2D ExpandingIcon
        {
            get
            {
                if (IsMortarOutpost && AntiAirFireUtils.HasAntiAirUpgrade(this))
                {
                    Texture2D aa = ContentFinder<Texture2D>.Get(MortarOutpostAntiAirTexturePath, false);
                    if (aa != null) return aa;
                }
                return base.ExpandingIcon;
            }
        }

        public void InvalidateWorldMapIconCache()
        {
            cachedMaterial = null;
            Patch_WdWorldObjectNoExpandingIcon.NotifyIconModeChanged();
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (var g in base.GetGizmos())
                yield return g;

            if (Faction != Faction.OfPlayer) yield break;

            foreach (var g in Action_Outpost_LaunchAttack.GetGizmos(this))
                yield return g;
            foreach (var g in WD_Outpost_Mortar.GetGizmos(this))
                yield return g;
            foreach (var g in WD_Outpost_RapidResponse.GetGizmos(this))
                yield return g;
            foreach (var g in Action_Outpost_Build.GetGizmos(this))
                yield return g;
            foreach (var g in OutpostLogisticsGizmos.GetGizmos(this))
                yield return g;

            if (!IsMortarOutpost && !IsRapidResponseOutpost && !Outpost_Production_Utils.IsWarehouseOutpost(def) && !IsResearchOutpost && !IsPowerPlantOutpost)
            {
                foreach (var g in Outpost_Production.GetGizmos(this))
                    yield return g;
            }
            foreach (var g in Outpost_Warehouse_Gizmos.GetGizmos(this))
                yield return g;

            foreach (var g in AllyRadiusGizmo.Get(this))
                yield return g;
            foreach (var g in RadiusHoverGizmos.GetForOutpost(this))
                yield return g;

            var autoAddComp = GetComponent<WorldObjectComp_AutoAddPawn>();
            if (autoAddComp != null)
                yield return autoAddComp.GetToggleGizmo();
            yield return GetTakePrisonersGizmo();
        }

        /// <summary>
        /// Food logistics: virtual food is empty but pawns still consume — kill one eater (once per accounting pulse).
        /// Prisoners are chosen first; then Ideology slaves among occupants; then any other eating occupant.
        /// </summary>
        internal bool TryKillOneOccupantFromStarvation()
        {
            if (TryKillOnePrisonerFromStarvation())
                return true;

            var list = Occupants;
            if (list == null || list.Count == 0) return false;
            Pawn slavePick = null;
            int slaveCount = 0;
            int validCount = 0;
            for (int i = 0; i < list.Count; i++)
            {
                Pawn cand = list[i];
                if (cand == null || cand.Destroyed || cand.Dead) continue;
                if (!OutpostPawnClassificationUtil.ConsumesVirtualFood(cand)) continue;
                validCount++;
                if (OutpostPawnIdeologyUtil.IsSlaveHumanlike(cand))
                {
                    if (slaveCount == 0 || Rand.Range(0, slaveCount + 1) == 0)
                        slavePick = cand;
                    slaveCount++;
                }
            }

            if (validCount == 0) return false;
            Pawn p = slavePick;
            if (p == null)
            {
                int pick = Rand.Range(0, validCount);
                for (int i = 0; i < list.Count; i++)
                {
                    Pawn cand = list[i];
                    if (cand == null || cand.Destroyed || cand.Dead) continue;
                    if (!OutpostPawnClassificationUtil.ConsumesVirtualFood(cand)) continue;
                    if (pick == 0)
                    {
                        p = cand;
                        break;
                    }
                    pick--;
                }
            }

            if (p == null) return false;

            string pawnLabel = p.LabelShortCap;
            p.GetCaravan()?.RemovePawn(p);
            p.holdingOwner?.Remove(p);
            Occupants.Remove(p);
            if (Faction == Faction.OfPlayer && p.Faction != Faction.OfPlayer)
                p.SetFaction(Faction.OfPlayer);
            NotifyVirtualPawnsChanged();

            if (!p.Destroyed)
            {
                if (Find.WorldPawns != null && !Find.WorldPawns.Contains(p))
                    Find.WorldPawns.PassToWorld(p, PawnDiscardDecideMode.KeepForever);
                p.Kill(null);
            }

            if (Faction == Faction.OfPlayer)
                Messages.Message("TSA_WD_OutpostStarvationDeath".Translate(LabelCap, pawnLabel), MessageTypeDefOf.NegativeEvent);
            return true;
        }

        /// <summary>Starvation: kill one eating prisoner at random. Prefer captives before occupants.</summary>
        private bool TryKillOnePrisonerFromStarvation()
        {
            List<Pawn> list = Prisoners;
            if (list == null || list.Count == 0) return false;

            int validCount = 0;
            for (int i = 0; i < list.Count; i++)
            {
                Pawn cand = list[i];
                if (cand == null || cand.Destroyed || cand.Dead) continue;
                if (!OutpostPawnClassificationUtil.ConsumesVirtualFood(cand)) continue;
                validCount++;
            }
            if (validCount == 0) return false;

            Pawn p = null;
            int pick = Rand.Range(0, validCount);
            for (int i = 0; i < list.Count; i++)
            {
                Pawn cand = list[i];
                if (cand == null || cand.Destroyed || cand.Dead) continue;
                if (!OutpostPawnClassificationUtil.ConsumesVirtualFood(cand)) continue;
                if (pick == 0)
                {
                    p = cand;
                    break;
                }
                pick--;
            }
            if (p == null) return false;

            string pawnLabel = p.LabelShortCap;
            list.Remove(p);
            WorldComponent_PrisonerRecruitSchedule.Get()?.Clear(p.ThingID);
            NotifyVirtualPawnsChanged();
            Window_Prisoners.InvalidateCache();

            if (!p.Destroyed)
            {
                if (Find.WorldPawns != null && !Find.WorldPawns.Contains(p))
                    Find.WorldPawns.PassToWorld(p, PawnDiscardDecideMode.KeepForever);
                p.Kill(null);
            }

            if (Faction == Faction.OfPlayer)
                Messages.Message("TSA_WD_OutpostStarvationDeath".Translate(LabelCap, pawnLabel), MessageTypeDefOf.NegativeEvent);
            return true;
        }

        /// <summary>Remove one pawn from the outpost and return it. Caller creates caravan with the returned pawn (vanilla Outposts style).</summary>
        public Pawn RemovePawn(Pawn p)
        {
            if (manualDefenseActive) return null;
            if (p == null || !Occupants.Contains(p)) return null;
            p.GetCaravan()?.RemovePawn(p);
            p.holdingOwner?.Remove(p);
            Occupants.Remove(p);
            if (Faction == Faction.OfPlayer && p.Faction != Faction.OfPlayer)
                p.SetFaction(Faction.OfPlayer);
            RegisterAutoAddBlockUntilPawnLeavesTile(p);
            NotifyVirtualPawnsChanged();
            return p;
        }

        /// <summary>Remove one pawn and create a 1-pawn caravan at this tile. Vanilla style: remove from list then MakeCaravan in same flow.</summary>
        public void RemovePawnAsCaravan(Pawn p)
        {
            Pawn removed = RemovePawn(p);
            if (removed == null) return;
            Caravan caravan = CaravanMaker.MakeCaravan(Gen.YieldSingle(removed), Faction, Tile, true);
            SelectOnlyCreatedCaravan(caravan);
        }

        /// <summary>Remove several pawns and create one caravan containing all of them at this tile (same pattern as <see cref="Outpost_Recruiting.Produce"/>).</summary>
        public void RemovePawnsAsCaravan(IReadOnlyList<Pawn> pawns)
        {
            if (pawns == null || pawns.Count == 0) return;
            var removed = new List<Pawn>(pawns.Count);
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p == null || !Occupants.Contains(p)) continue;
                Pawn r = RemovePawn(p);
                if (r != null) removed.Add(r);
            }
            if (removed.Count == 0) return;
            Caravan caravan = CaravanMaker.MakeCaravan(removed, Faction, Tile, true);
            SelectOnlyCreatedCaravan(caravan);
            if (Occupants != null && Occupants.Count == 0)
                Destroy();
        }

        public void RemovePawnsAndStoredTransportAndMechanoidsAsCaravan(
            IReadOnlyList<Pawn> pawns,
            IReadOnlyList<Pawn> storedTransportPawns,
            IReadOnlyList<Pawn> mechanoidPawns,
            IReadOnlyList<Building_PassengerShuttle> storedShuttles = null)
        {
            int cap = (pawns?.Count ?? 0) + (storedTransportPawns?.Count ?? 0) + (mechanoidPawns?.Count ?? 0);
            if (cap <= 0 && (storedShuttles == null || storedShuttles.Count == 0)) return;
            var removed = new List<Pawn>(cap);

            if (pawns != null)
            {
                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn p = pawns[i];
                    if (p == null || !Occupants.Contains(p)) continue;
                    Pawn r = RemovePawn(p);
                    if (r != null && !r.Destroyed && !r.Dead) removed.Add(r);
                }
            }

            if (storedTransportPawns != null)
            {
                for (int i = 0; i < storedTransportPawns.Count; i++)
                {
                    Pawn p = storedTransportPawns[i];
                    if (p == null || !StoredAnimalsAndVehicles.Contains(p)) continue;
                    Pawn r = RemoveStoredAnimalOrVehicle(p);
                    if (r != null && !r.Destroyed && !r.Dead) removed.Add(r);
                }
            }

            if (mechanoidPawns != null)
            {
                for (int i = 0; i < mechanoidPawns.Count; i++)
                {
                    Pawn p = mechanoidPawns[i];
                    if (p == null || !StoredMechanoids.Contains(p)) continue;
                    Pawn r = RemoveStoredMechanoid(p);
                    if (r != null && !r.Destroyed && !r.Dead) removed.Add(r);
                }
            }

            var detachedShuttles = new List<Building_PassengerShuttle>();
            if (storedShuttles != null)
            {
                for (int i = 0; i < storedShuttles.Count; i++)
                {
                    Building_PassengerShuttle shuttle = RemoveStoredPassengerShuttle(storedShuttles[i]);
                    if (shuttle != null && !shuttle.Destroyed)
                        detachedShuttles.Add(shuttle);
                }
            }

            if (removed.Count == 0 && detachedShuttles.Count == 0) return;
            Caravan caravan = CaravanMaker.MakeCaravan(removed, Faction, Tile, true);
            if (detachedShuttles.Count > 0)
                OdysseyShuttleOutpostEstablishmentCompat.AttachStoredShuttlesToCaravan(caravan, detachedShuttles);
            VehicleFrameworkOutpostDissolveCompat.TryAutoBoardPawnsIntoSelectedVehicles(caravan, removed);
            SelectOnlyCreatedCaravan(caravan);
            if (Occupants != null && Occupants.Count == 0)
                Destroy();
        }

        public void RemovePawnsAndStoredTransportAsCaravan(IReadOnlyList<Pawn> pawns, IReadOnlyList<Pawn> storedTransportPawns)
        {
            int cap = (pawns?.Count ?? 0) + (storedTransportPawns?.Count ?? 0);
            if (cap <= 0) return;
            var removed = new List<Pawn>(cap);

            if (pawns != null)
            {
                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn p = pawns[i];
                    if (p == null || !Occupants.Contains(p)) continue;
                    Pawn r = RemovePawn(p);
                    if (r != null && !r.Destroyed && !r.Dead) removed.Add(r);
                }
            }

            if (storedTransportPawns != null)
            {
                for (int i = 0; i < storedTransportPawns.Count; i++)
                {
                    Pawn p = storedTransportPawns[i];
                    if (p == null || !StoredAnimalsAndVehicles.Contains(p)) continue;
                    Pawn r = RemoveStoredAnimalOrVehicle(p);
                    if (r != null && !r.Destroyed && !r.Dead) removed.Add(r);
                }
            }

            if (removed.Count == 0) return;
            Caravan caravan = CaravanMaker.MakeCaravan(removed, Faction, Tile, true);
            VehicleFrameworkOutpostDissolveCompat.TryAutoBoardPawnsIntoSelectedVehicles(caravan, removed);
            SelectOnlyCreatedCaravan(caravan);
            if (Occupants != null && Occupants.Count == 0)
                Destroy();
        }

        private static void SelectOnlyCreatedCaravan(Caravan caravan)
        {
            if (caravan == null || Find.WorldSelector == null) return;
            Find.WorldSelector.ClearSelection();
            Find.WorldSelector.Select(caravan, false);
        }

        /// <summary>True while this pawn is on <see cref="Tile"/> after a manual remove — auto-add skips them until they leave the tile.</summary>
        internal bool IsPawnBlockedFromAutoAdd(Pawn p) =>
            p != null && !p.ThingID.NullOrEmpty()
                      && autoAddBlockedUntilOffTileThingIds != null
                      && autoAddBlockedUntilOffTileThingIds.Contains(p.ThingID);

        /// <summary>
        /// After founding without absorbing a caravan (conquest / buy / generated workers), keep any
        /// player caravan already on this tile from being auto-added until it leaves.
        /// </summary>
        internal void RegisterAutoAddBlockForPlayerCaravansOnThisTile()
        {
            var caravans = Find.WorldObjects?.Caravans;
            if (caravans == null) return;
            for (int i = 0; i < caravans.Count; i++)
            {
                Caravan caravan = caravans[i];
                if (caravan == null || caravan.Destroyed || !caravan.IsPlayerControlled) continue;
                if (caravan.Tile != Tile) continue;
                List<Pawn> pawns = caravan.PawnsListForReading;
                if (pawns == null) continue;
                for (int pi = 0; pi < pawns.Count; pi++)
                {
                    Pawn p = pawns[pi];
                    if (p == null || p.Destroyed || p.Dead) continue;
                    if (p.RaceProps?.Humanlike != true) continue;
                    RegisterAutoAddBlockUntilPawnLeavesTile(p);
                }
            }
        }

        /// <summary>Drop block entries for destroyed pawns or any pawn no longer on this outpost’s world tile.</summary>
        internal void PruneAutoAddBlocksWherePawnLeftTile()
        {
            if (autoAddBlockedUntilOffTileThingIds == null || autoAddBlockedUntilOffTileThingIds.Count == 0) return;

            var pawnIndex = BuildPlayerPawnIndex();
            List<string> drop = null;
            foreach (string id in autoAddBlockedUntilOffTileThingIds)
            {
                if (!pawnIndex.TryGetValue(id, out Pawn p) || p == null || p.Destroyed || p.Dead || p.Tile != Tile)
                {
                    drop ??= new List<string>();
                    drop.Add(id);
                }
            }
            if (drop == null) return;
            for (int i = 0; i < drop.Count; i++)
                autoAddBlockedUntilOffTileThingIds.Remove(drop[i]);
            if (autoAddBlockedUntilOffTileThingIds.Count == 0)
                autoAddBlockedUntilOffTileThingIds = null;
        }

        private void RegisterAutoAddBlockUntilPawnLeavesTile(Pawn p)
        {
            if (p == null || p.ThingID.NullOrEmpty()) return;
            if (autoAddBlockedUntilOffTileThingIds == null)
                autoAddBlockedUntilOffTileThingIds = new HashSet<string>();
            autoAddBlockedUntilOffTileThingIds.Add(p.ThingID);
        }

        private void ClearAutoAddBlockForPawn(Pawn pawn)
        {
            if (pawn == null || autoAddBlockedUntilOffTileThingIds == null) return;
            autoAddBlockedUntilOffTileThingIds.Remove(pawn.ThingID);
            if (autoAddBlockedUntilOffTileThingIds.Count == 0)
                autoAddBlockedUntilOffTileThingIds = null;
        }

        private static readonly Dictionary<string, Pawn> pawnIndexCache = new Dictionary<string, Pawn>();
        private static Dictionary<string, Pawn> BuildPlayerPawnIndex()
        {
            pawnIndexCache.Clear();
            List<WorldObject> objs = Find.WorldObjects?.AllWorldObjects;
            if (objs == null) return pawnIndexCache;
            for (int i = 0; i < objs.Count; i++)
            {
                if (objs[i] is Caravan c && c.Faction == Faction.OfPlayer)
                {
                    List<Pawn> pl = c.PawnsListForReading;
                    if (pl == null) continue;
                    for (int j = 0; j < pl.Count; j++)
                    {
                        Pawn p = pl[j];
                        if (p != null && !p.Destroyed && p.ThingID != null)
                            pawnIndexCache[p.ThingID] = p;
                    }
                }
                else if (objs[i] is WorldObject_WD_Outpost op)
                {
                    foreach (Pawn p in op.Occupants)
                        if (p != null && !p.Destroyed && p.ThingID != null)
                            pawnIndexCache[p.ThingID] = p;
                }
            }
            return pawnIndexCache;
        }

        /// <summary>Create things from frozen inventory list and add them to the caravan. Splits stacks over stackLimit.</summary>
        private static void GiveInventoryToCaravan(Caravan caravan, List<ThingDefCountClass> inventory)
        {
            if (caravan == null || inventory == null) return;
            foreach (ThingDefCountClass tc in inventory)
            {
                if (tc?.thingDef == null || tc.count <= 0) continue;
                ThingDef def = tc.thingDef;
                int remaining = tc.count;
                int stackLimit = def.stackLimit > 0 ? def.stackLimit : 75;
                while (remaining > 0)
                {
                    Thing thing = ThingMaker.MakeThing(def);
                    if (thing == null) break;
                    int take = Mathf.Min(remaining, stackLimit);
                    thing.stackCount = take;
                    caravan.AddPawnOrItem(thing, false);
                    remaining -= take;
                }
            }
        }

        /// <summary>Create a temporary pawn from a summary for UI (portrait, info card). Caller must destroy when done.</summary>
        public static Pawn CreateTempPawnForUI(VirtualPawnSummary summary, Faction faction)
        {
            return SpawnPawnFromSummary(summary, faction);
        }

        private static Pawn SpawnPawnFromSummary(VirtualPawnSummary summary, Faction faction)
        {
            if (summary == null || faction == null) return null;
            var req = new PawnGenerationRequest(PawnKindDefOf.Colonist, faction, PawnGenerationContext.NonPlayer, -1, forceGenerateNewPawn: true, canGeneratePawnRelations: false, mustBeCapableOfViolence: true);
            Pawn pawn = PawnGenerator.GeneratePawn(req);
            if (!summary.name.NullOrEmpty()) pawn.Name = new NameSingle(summary.name);
            ApplySkill(pawn, SkillDefOf.Shooting, summary.shooting);
            ApplySkill(pawn, SkillDefOf.Melee, summary.melee);
            ApplySkill(pawn, SkillDefOf.Plants, summary.plants);
            ApplySkill(pawn, SkillDefOf.Animals, summary.animals);
            ApplySkill(pawn, SkillDefOf.Construction, summary.construction);
            ApplySkill(pawn, SkillDefOf.Social, summary.social);
            ApplySkill(pawn, SkillDefOf.Mining, summary.mining);
            ApplySkill(pawn, SkillDefOf.Crafting, summary.crafting);
            return pawn;
        }

        private static void ApplySkill(Pawn pawn, SkillDef def, int level)
        {
            if (pawn.skills == null) return;
            var skill = pawn.skills.GetSkill(def);
            if (skill != null) skill.Level = WorldDominationMod.settings?.GetEffectiveOutpostSkillLevel(level) ?? Mathf.Max(0, level);
        }

        /// <summary>
        /// When a caravan is on this outpost's tile, show \"Add to outpost\" like VOE: pick a caravan pawn to add as a virtual pawn.
        /// Also exposes a food-conversion gizmo when food logistics are active so caravans can convert food items into virtual food.
        /// During manual defense, offers joining the temporary battlefield (covers ground caravans and VF aerial landings).
        /// </summary>
        public override IEnumerable<Gizmo> GetCaravanGizmos(Caravan caravan)
        {
            foreach (var g in base.GetCaravanGizmos(caravan))
                yield return g;

            if (caravan == null || caravan.Tile != Tile || Faction != Faction.OfPlayer) yield break;

            if (ManualDefenseActive)
            {
                Map defenseMap = WD_MapComponent_OutpostDefense.FindActiveMapFor(this);
                if (defenseMap != null)
                {
                    yield return new Command_Action
                    {
                        defaultLabel = "TSA_WD_OutpostDefense_JoinBattle".Translate(),
                        defaultDesc = "TSA_WD_OutpostDefense_JoinBattleDesc".Translate(LabelCap),
                        icon = TexCommand.Draft,
                        action = () =>
                        {
                            Map mapNow = WD_MapComponent_OutpostDefense.FindActiveMapFor(this);
                            if (mapNow == null || caravan == null || caravan.Destroyed)
                            {
                                Messages.Message("TSA_WD_OutpostDefense_FrozenDuringManualDefense".Translate(), MessageTypeDefOf.RejectInput, false);
                                return;
                            }
                            CaravanEnterMapUtility.Enter(caravan, mapNow, CaravanEnterMode.Edge, CaravanDropInventoryMode.DoNotDrop, draftColonists: true);
                        }
                    };
                }
            }

            var pawnsList = caravan.PawnsListForReading;
            if (pawnsList == null || pawnsList.Count == 0) yield break;
            List<Pawn> humanlike = null;
            for (int i = 0; i < pawnsList.Count; i++)
            {
                var p = pawnsList[i];
                if (p.RaceProps != null && p.RaceProps.Humanlike)
                {
                    humanlike ??= new List<Pawn>();
                    humanlike.Add(p);
                }
            }
            if (humanlike == null || humanlike.Count == 0) yield break;

            bool caravanParked = Outpost_EstablishmentRequirements.CaravanParkedOnTileForAddToOutpost(caravan, Tile, out string parkedReason);
            string addDesc = "TSA_WD_AddToOutpostDesc".Translate(Label).ToString();
            if (ManualDefenseActive)
                addDesc = "TSA_WD_OutpostDefense_FrozenDuringManualDefense".Translate() + "\n\n" + addDesc;
            else if (!caravanParked)
                addDesc = (parkedReason ?? "") + "\n\n" + addDesc;

            // Add-to-outpost gizmo: animals and vehicles are stored when the caravan dissolves or when selected in the dialog.
            var addCmd = new Command_Action
            {
                defaultLabel = "TSA_WD_AddToOutpost".Translate(),
                defaultDesc = addDesc,
                icon = cachedAddToOutpostIcon ??= ContentFinder<Texture2D>.Get("UI/Commands/AutoSendPawn", false) ?? TexButton.Add,
                action = () =>
                {
                    if (ManualDefenseActive)
                    {
                        Messages.Message("TSA_WD_OutpostDefense_FrozenDuringManualDefense".Translate(), MessageTypeDefOf.RejectInput, false);
                        return;
                    }
                    if (!Outpost_EstablishmentRequirements.CaravanParkedOnTileForAddToOutpost(caravan, Tile, out string r))
                    {
                        Messages.Message(r ?? "", MessageTypeDefOf.RejectInput, false);
                        return;
                    }
                    LongEventHandler.ExecuteWhenFinished(() =>
                    {
                        if (caravan == null || caravan.Destroyed) return;
                        if (ManualDefenseActive)
                        {
                            Messages.Message("TSA_WD_OutpostDefense_FrozenDuringManualDefense".Translate(), MessageTypeDefOf.RejectInput, false);
                            return;
                        }
                        if (!Outpost_EstablishmentRequirements.CaravanParkedOnTileForAddToOutpost(caravan, Tile, out string parked))
                        {
                            Messages.Message(parked ?? "", MessageTypeDefOf.RejectInput, false);
                            return;
                        }
                        Find.WindowStack.Add(new Dialog_AddCaravanPawnsToOutpost(this, caravan));
                    });
                }
            };
            if (ManualDefenseActive)
                addCmd.Disable("TSA_WD_OutpostDefense_FrozenDuringManualDefense".Translate());
            else if (!caravanParked)
                addCmd.Disable(parkedReason);
            yield return addCmd;

            // Food conversion gizmo for caravans parked on this outpost: convert caravan food
            // items into the outpost's virtual food pool (CompOutpostLogistics.currentFood).
            var logi = GetComponent<CompOutpostLogistics>();
            var settings = WorldDominationMod.settings;
            if (settings != null && settings.foodLogisticsActive && logi != null && logi.currentFood < logi.EffectiveMaxFood)
            {
                yield return new Command_Action
                {
                    defaultLabel = "TSA_WD_ConvertFood".Translate(),
                    defaultDesc = "TSA_WD_ConvertFoodDesc".Translate(),
                    icon = cachedConvertFoodCaravanIcon ??= ContentFinder<Texture2D>.Get("UI/Commands/ConvertFood", false) ?? TexCommand.Replant,
                    action = () => CompOutpostLogistics.ConvertCaravanFoodToVirtualFood(caravan, this, logi)
                };
            }
        }

        /// <summary>Right-click outpost while a caravan can path here: during manual defense, enter the temporary battlefield.</summary>
        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Caravan caravan)
        {
            foreach (var o in base.GetFloatMenuOptions(caravan))
                yield return o;

            if (Faction != Faction.OfPlayer || !ManualDefenseActive || caravan == null)
                yield break;

            Map defenseMap = WD_MapComponent_OutpostDefense.FindActiveMapFor(this);
            if (defenseMap?.Parent is not MapParent site || !site.HasMap)
                yield break;

            foreach (var o in CaravanArrivalAction_Enter.GetFloatMenuOptions(caravan, site))
                yield return o;
        }

        /// <summary>Transport pods targeting this tile: only "add to outpost" (no vanilla form-caravan option).
        /// During manual defense, same action reinforces the temporary battlefield instead.</summary>
        public override IEnumerable<FloatMenuOption> GetTransportersFloatMenuOptions(IEnumerable<IThingHolder> pods, Action<PlanetTile, TransportersArrivalAction> launchAction)
        {
            if (Faction == Faction.OfPlayer)
            {
                foreach (var o in TransportersArrivalAction_AddToWDOutpost.GetFloatMenuOptions(pods, launchAction, this))
                    yield return o;
                yield break;
            }
            foreach (var o in base.GetTransportersFloatMenuOptions(pods, launchAction))
                yield return o;
        }

        /// <summary>
        /// True if <paramref name="pawn"/> appears on this caravan's pawn list and is the only humanlike there.
        /// Uses a snapshot before any RemovePawn/WorldPawns work — reading the list after <see cref="AddPawn"/> can miss
        /// humanlikes still on the caravan and wrongly trigger inventory wipe.
        /// </summary>
        private static bool IsSoleHumanlikeOnCaravan(Caravan caravan, Pawn pawn)
        {
            if (caravan == null || pawn == null) return false;
            var readingList = caravan.PawnsListForReading;
            List<Pawn> snap = readingList != null ? new List<Pawn>(readingList) : null;
            if (snap == null || snap.Count == 0) return false;
            bool sawPawn = false;
            foreach (Pawn o in snap)
            {
                if (o == null || o.Destroyed) continue;
                if (ReferenceEquals(o, pawn))
                {
                    sawPawn = true;
                    continue;
                }
                if (o.RaceProps?.Humanlike == true)
                    return false;
            }
            return sawPawn;
        }

        /// <summary>
        /// Founding from caravan runs one <see cref="AddCaravanPawnToOutpost"/> per colonist; VF/mount edge cases can
        /// rarely leave pack animals on the caravan. Strip remainder without a second virtual-food credit (first
        /// dissolve already ran <see cref="CompOutpostLogistics.TryDissolveCaravanIntoOutpostVirtualFood"/>).
        /// </summary>
        public void TryFinishDissolveCaravanAfterFoundingIfStillPresent(Caravan caravan)
        {
            if (caravan == null || caravan.Destroyed) return;
            if (caravan.PawnsListForReading == null || caravan.PawnsListForReading.Count == 0) return;
            DissolveCaravanIntoOutpostPhysicalRemainder(caravan, creditVirtualFoodFromRemainder: false);
        }

        /// <summary>
        /// Last humanlike transferred to the outpost: eject mounts/cargo from VF vehicles, credit edible inventory as virtual food,
        /// store vehicle pawns and non-humanlikes, destroy remaining non-warehouse inventory,
        /// destroy caravan when empty.
        /// </summary>
        private void DissolveCaravanIntoOutpostPhysicalRemainder(Caravan caravan, bool creditVirtualFoodFromRemainder)
        {
            if (caravan == null || caravan.Destroyed) return;

            WDVerbose.Msg($"Outpost dissolve begin: caravan {caravan.LabelCap} (creditFood={creditVirtualFoodFromRemainder})");

            VehicleFrameworkOutpostDissolveCompat.EjectAllPawnsFromHostVehiclesForOutpostDissolve(caravan);

            // VF cleanup can remove animals from the caravan list without destroying them; strip then never sees them.
            var nonHumanDissolveSnapshot = new List<Pawn>();
            var dissolveSeen = new HashSet<Pawn>();
            VehicleFrameworkOutpostDissolveCompat.CollectNonHumanlikeDissolveSnapshotAfterEject(
                caravan,
                nonHumanDissolveSnapshot,
                dissolveSeen);

            if (creditVirtualFoodFromRemainder)
                CompOutpostLogistics.TryDissolveCaravanIntoOutpostVirtualFood(caravan, this, notifyPlayer: true);

            StoreAnyAliveNonHumanlikeDissolveTargets(caravan, nonHumanDissolveSnapshot);
            VehicleFrameworkOutpostDissolveCompat.DestroyStashedVehiclesAtTileForPlayer(Tile);
            OdysseyShuttleOutpostEstablishmentCompat.TryStoreShuttlesFromOccupants(this);
            OdysseyShuttleOutpostEstablishmentCompat.TryStoreShuttlesFromCaravan(this, caravan);
            WDVerbose.Msg("Outpost dissolve: stored non-humanlikes / vehicles and cleaned stashed vehicle world objects");

            StoreNonHumanlikeAndVehiclePawnsFromCaravanMultipass(caravan);
            WDVerbose.Msg("Outpost dissolve: multipass store non-humanlikes done");

                        var inventorySnapshot = new List<Thing>(CaravanInventoryUtility.AllInventoryItems(caravan));
            int invDestroyed = 0;
            if (Outpost_Production_Utils.IsWarehouseOutpost(def))
            {
                var whComp = CompOutpostWarehouse.Get(this);
                if (whComp != null && inventorySnapshot.Count > 0)
                {
                    whComp.TryDepositThings(inventorySnapshot);
                    for (int ti = 0; ti < inventorySnapshot.Count; ti++)
                    {
                        Thing thing = inventorySnapshot[ti];
                        if (thing == null || thing.Destroyed) continue;
                        thing.Destroy(DestroyMode.Vanish);
                        invDestroyed++;
                    }
                    if (WorldDominationMod.settings?.notifyWarehouseGoodsArrived ?? WorldDominationSettings.DefNotifyWarehouseGoodsArrived)
                        Messages.Message("TSA_WD_Warehouse_CaravanDeposit".Translate(invDestroyed, LabelCap), this, MessageTypeDefOf.PositiveEvent);
                }
            }
            else
            {
                for (int ti = 0; ti < inventorySnapshot.Count; ti++)
                {
                    Thing thing = inventorySnapshot[ti];
                    if (thing == null || thing.Destroyed) continue;
                    if (OdysseyShuttleOutpostEstablishmentCompat.IsPassengerShuttle(thing)) continue;
                    thing.Destroy(DestroyMode.Vanish);
                    invDestroyed++;
                }
            }

            if (invDestroyed > 0)
                WDVerbose.Msg($"Outpost dissolve: destroyed {invDestroyed} remaining caravan inventory thing(s) (non-edible or overflow)");

            StoreAnyAliveNonHumanlikeDissolveTargets(caravan, nonHumanDissolveSnapshot);

            if (caravan.PawnsListForReading == null || caravan.PawnsListForReading.Count == 0)
            {
                WDVerbose.Msg($"Outpost dissolve: caravan empty, destroying {caravan.LabelCap}");
                VehicleFrameworkOutpostDissolveCompat.DestroyCaravanWorldObjectAfterOutpostDissolve(caravan);
            }
        }

        /// <summary>
        /// Ensures every non-humanlike captured after eject is gone after VF cleanup + strip (orphans not on caravan list).
        /// </summary>
        private void StoreAnyAliveNonHumanlikeDissolveTargets(Caravan primaryCaravan, List<Pawn> snapshot)
        {
            if (snapshot == null) return;
            for (int i = 0; i < snapshot.Count; i++)
            {
                Pawn p = snapshot[i];
                if (p == null || p.Destroyed) continue;
                if (p.RaceProps?.Humanlike == true) continue;

                string label = p.LabelShortCap;
                if (StoreNonHumanlikeDissolvePawn(p, primaryCaravan))
                    WDVerbose.Msg($"Outpost dissolve: stored snapshot pawn {label}");
            }
        }

        private void MigrateStoredMechanoidsOnLoad()
        {
            if (occupants != null)
            {
                for (int i = occupants.Count - 1; i >= 0; i--)
                {
                    Pawn p = occupants[i];
                    if (p == null || p.Destroyed || p.Dead) continue;
                    if (!OutpostPawnClassificationUtil.IsMechanoidWorker(p)) continue;
                    occupants.RemoveAt(i);
                    if (!StoredMechanoids.Contains(p))
                        StoredMechanoids.Add(p);
                }
            }

            if (storedAnimalsAndVehicles != null)
            {
                for (int i = storedAnimalsAndVehicles.Count - 1; i >= 0; i--)
                {
                    Pawn p = storedAnimalsAndVehicles[i];
                    if (p == null || p.Destroyed || p.Dead) continue;
                    if (!OutpostPawnClassificationUtil.IsMechanoidWorker(p)) continue;
                    storedAnimalsAndVehicles.RemoveAt(i);
                    if (!StoredMechanoids.Contains(p))
                        StoredMechanoids.Add(p);
                }
            }

            if (StoredMechanoids.Count > 0)
            {
                cachedMechanoidVirtualPawns = null;
                GetComponent<CompViralSpread>()?.UpdateOutpostStrengthLogically();
            }
        }

        /// <summary>
        /// Removes a pack animal / vehicle pawn from caravan and container bookkeeping, then vanishes it.
        /// Calling <see cref="Pawn.Destroy"/> while the pawn is still a caravan member (especially after VF eject) leaves corrupt
        /// state — the pawn can remain on the player faction with colonist-style needs UI.
        /// </summary>
        private static void DissolveRemoveCaravanNonHumanlikePawnFromCaravanAndVanish(
            Caravan primaryCaravan,
            Pawn p,
            bool runEjectFromHostVehicle)
        {
            if (p == null || p.Destroyed) return;
            if (p.RaceProps?.Humanlike == true) return;

            if (runEjectFromHostVehicle)
                VehicleFrameworkOutpostDissolveCompat.TryEjectPawnFromHostingVehicle(p);
            if (p.Destroyed)
                return;

            void tryRemoveFrom(Caravan c)
            {
                if (c == null || c.Destroyed) return;
                try
                {
                    if (CaravanPawnListContains(c, p))
                        c.RemovePawn(p);
                }
                catch (Exception ex)
                {
                    Log.Warning($"[WD] Caravan.RemovePawn while dissolving '{p.LabelShort}': {ex.Message}");
                }
            }

            tryRemoveFrom(primaryCaravan);
            Caravan other = p.GetCaravan();
            if (other != null && !ReferenceEquals(other, primaryCaravan) && !other.Destroyed)
                tryRemoveFrom(other);

            try
            {
                p.holdingOwner?.Remove(p);
            }
            catch (Exception ex)
            {
                Log.Warning($"[WD] holdingOwner.Remove while dissolving '{p.LabelShort}': {ex.Message}");
            }

            if (p.Destroyed)
                return;

            try
            {
                p.Destroy(DestroyMode.Vanish);
            }
            catch (Exception ex)
            {
                Log.Warning($"[WD] Pawn.Destroy while dissolving '{p.LabelShort}': {ex.Message}");
            }
        }

        private void StoreNonHumanlikeAndVehiclePawnsFromCaravanMultipass(Caravan caravan)
        {
            const int maxPasses = 10;
            for (int pass = 0; pass < maxPasses && caravan != null && !caravan.Destroyed; pass++)
            {
                var reading = caravan.PawnsListForReading;
                if (reading == null || reading.Count == 0) break;

                var snapshot = new List<Pawn>(reading);
                bool removed = false;
                for (int i = 0; i < snapshot.Count; i++)
                {
                    Pawn p = snapshot[i];
                    if (p == null || p.Destroyed) continue;
                    if (p.RaceProps?.Humanlike == true) continue;

                    string stripLabel = p.LabelShortCap;
                    string stripKind = p.kindDef?.label ?? p.def?.label ?? "pawn";

                    bool onCaravanList = CaravanPawnListContains(caravan, p);
                    if (StoreNonHumanlikeDissolvePawn(p, caravan))
                    {
                        if (onCaravanList)
                            WDVerbose.Msg($"Outpost dissolve: removed and stored {stripLabel} ({stripKind})");
                        else
                            WDVerbose.Msg($"Outpost dissolve: stored {stripLabel} ({stripKind}) — was off caravan list after eject");
                    }
                    removed = true;
                }

                if (!removed) break;
            }
        }

        private static bool CaravanPawnListContains(Caravan caravan, Pawn p)
        {
            var list = caravan?.PawnsListForReading;
            if (list == null) return false;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == p) return true;
            }

            return false;
        }

        /// <summary>
        /// Add one caravan pawn to this outpost. Removes them from the caravan and WorldPawns; pawn is owned by the outpost.
        /// If they were the only humanlike on the caravan (decided from a pre-transfer snapshot), non-humanlike members and
        /// all caravan inventory are destroyed and the caravan object is removed.
        /// </summary>
        public void AddCaravanPawnToOutpost(Pawn pawn, Caravan caravan)
        {
            if (pawn == null || caravan == null) return;
            if (ManualDefenseActive)
            {
                Messages.Message("TSA_WD_OutpostDefense_FrozenDuringManualDefense".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }
            bool dissolveCaravan = IsSoleHumanlikeOnCaravan(caravan, pawn);
            if (!caravan.Destroyed && dissolveCaravan)
                OdysseyShuttleOutpostEstablishmentCompat.TryStoreShuttlesFromCaravan(this, caravan);

            if (!AddPawn(pawn, caravan))
            {
                Messages.Message("TSA_WD_AddToOutpost_AddFailed".Translate(pawn.LabelShort), MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (!caravan.Destroyed && dissolveCaravan)
                DissolveCaravanIntoOutpostPhysicalRemainder(caravan, creditVirtualFoodFromRemainder: true);

            NotifyVirtualPawnsChanged();
        }

        /// <summary>Route caravan pawn to occupants, mechanoid storage, or animal/vehicle storage.</summary>
        public void AddCaravanPawnToOutpostRouted(Pawn pawn, Caravan caravan)
        {
            if (pawn == null || caravan == null) return;
            if (ManualDefenseActive)
            {
                Messages.Message("TSA_WD_OutpostDefense_FrozenDuringManualDefense".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (pawn.RaceProps?.Humanlike == true && !OutpostPawnClassificationUtil.IsMechanoidWorker(pawn))
            {
                AddCaravanPawnToOutpost(pawn, caravan);
                return;
            }

            bool stored = false;
            if (OutpostPawnClassificationUtil.IsMechanoidWorker(pawn))
                stored = StoreMechanoid(pawn, caravan);
            else
                stored = StoreAnimalOrVehicle(pawn, caravan);

            if (!stored)
            {
                Messages.Message("TSA_WD_AddToOutpost_AddFailed".Translate(pawn.LabelShort), MessageTypeDefOf.RejectInput, false);
                return;
            }

            Messages.Message("TSA_WD_StoredTransportPawns_Message".Translate(1, LabelCap), this, MessageTypeDefOf.TaskCompletion, false);
            OdysseyShuttleOutpostEstablishmentCompat.TryStoreShuttlesFromCaravan(this, caravan);
            if (!caravan.Destroyed && (caravan.PawnsListForReading == null || caravan.PawnsListForReading.Count == 0))
                VehicleFrameworkOutpostDissolveCompat.DestroyCaravanWorldObjectAfterOutpostDissolve(caravan);
        }

        public override void DrawExtraSelectionOverlays()
        {
            base.DrawExtraSelectionOverlays();
            if (!Find.WorldSelector.IsSelected(this) || def == null) return;

            if (Dialog_OutpostRangeAdjust.TryGetPreview(this, out OutpostRangeAdjustMode previewMode, out float previewRadius))
            {
                OutpostCoverageFillKind fillKind = previewMode == OutpostRangeAdjustMode.RapidResponse
                    ? OutpostCoverageFillKind.Purple
                    : OutpostCoverageFillKind.Red;
                Material hop = previewMode == OutpostRangeAdjustMode.RapidResponse
                    ? WorldOverlayLineMaterials.RecruitTradingRadiusRing
                    : WorldOverlayLineMaterials.RadiusRed;
                WD_RadiusOverlayMode.DrawOrFill(this, previewRadius, fillKind, hop,
                    accuracyBands: fillKind == OutpostCoverageFillKind.Red);
                return;
            }

            if (Dialog_OutpostArtilleryConfigure.TryGetPreview(
                    this,
                    out ArtilleryConfigureTab artTab,
                    out float mortarRadius,
                    out float aaRadius,
                    out bool hasAa))
            {
                if (WD_RadiusOverlayMode.UseHopRadiusRings)
                {
                    PlanetLayer layer = PlanetSurfaceWorldActions.LayerOf(this);
                    PlanetTile tile = new PlanetTile(Tile, layer);
                    if (mortarRadius > 0f)
                        WorldMapRadiusVisual.DrawApproxRadiusRing(tile, mortarRadius, WorldOverlayLineMaterials.RadiusRed);
                    if (hasAa && aaRadius > 0f)
                        WorldMapRadiusVisual.DrawApproxRadiusRing(tile, aaRadius, WorldOverlayLineMaterials.RecruitTradingRadiusRing);
                }
                else if (artTab == ArtilleryConfigureTab.AntiAir && hasAa && aaRadius > 0f)
                {
                    WD_RadiusOverlayMode.DrawOrFill(this, aaRadius, OutpostCoverageFillKind.Red, WorldOverlayLineMaterials.RadiusRed, accuracyBands: true);
                }
                else if (mortarRadius > 0f)
                {
                    WD_RadiusOverlayMode.DrawOrFill(this, mortarRadius, OutpostCoverageFillKind.Red, WorldOverlayLineMaterials.RadiusRed, accuracyBands: true);
                }
                return;
            }

            // Rapid response select ring (legacy). Fills use the Rapid Response hover gizmo instead.
            if (WD_RadiusOverlayMode.UseHopRadiusRings && IsRapidResponseOutpost)
            {
                int radius = (int)Mathf.Ceil(RapidResponseUtility.GetRangeTiles(this));
                if (radius > 0 && Find.WorldGrid != null)
                {
                    PlanetLayer layer = PlanetSurfaceWorldActions.LayerOf(this);
                    WorldMapRadiusVisual.DrawApproxRadiusRing(new PlanetTile(Tile, layer), radius, WorldOverlayLineMaterials.RecruitTradingRadiusRing);
                }
            }
        }

        /// <summary>Prepends type label and production lines to <see cref="WorldObject.GetInspectString"/> (includes <see cref="CompViralSpread.CompInspectStringExtra"/> strength: <c>TSA_WD_Inspect_StrengthCombinedLine</c>, <c>TSA_WD_Inspect_OutpostRecoveryLine</c>).</summary>
        public override string GetInspectString()
        {
            int tick = Find.TickManager.TicksGame;
            int researchFingerprint = IsResearchOutpost ? Outpost_Research.GetInspectFingerprint(this) : int.MinValue;
            if (tick - cachedInspectTick < 60 && cachedInspectString != null && (!IsResearchOutpost || researchFingerprint == cachedResearchInspectFingerprint))
                return cachedInspectString;
            cachedInspectTick = tick;
            cachedResearchInspectFingerprint = researchFingerprint;
            string typeLine = def != null ? def.label : "Outpost";
            string baseStr = base.GetInspectString();
            if (!string.IsNullOrEmpty(baseStr))
                baseStr = typeLine + "\n" + baseStr;
            else
                baseStr = typeLine;
            if (IsResearchOutpost)
            {
                string researchLine = Outpost_Research.GetInspectProductLine(this);
                if (!string.IsNullOrEmpty(researchLine))
                {
                    bool active = Outpost_Research.CanResearchNow(this, out _);
                    baseStr += "\n" + researchLine.Colorize(active ? Color.cyan : Color.yellow);
                }
                return CacheInspectString(baseStr);
            }
            if (IsPowerPlantOutpost)
            {
                string powerLine = Outpost_PowerPlant.GetInspectProductLine(this);
                if (!string.IsNullOrEmpty(powerLine))
                    baseStr += "\n" + powerLine.Colorize(Color.cyan);
                return CacheInspectString(baseStr);
            }
            if (IsRapidResponseOutpost)
            {
                string responseLine = WD_Outpost_RapidResponse.GetInspectStatusLine(this);
                if (!string.IsNullOrEmpty(responseLine))
                    baseStr += "\n" + responseLine.Colorize(Color.cyan);
                return CacheInspectString(baseStr);
            }
            if (IsMortarOutpost)
            {
                var mortarComp = GetComponent<CompViralSpread>();
                if (mortarComp != null && mortarComp.IsMortarOnCooldown)
                {
                    float daysLeft = (mortarComp.mortarCooldownTick - Find.TickManager.TicksGame) / 60000f;
                    baseStr += "\n" + "TSA_WD_Inspect_MortarCD".Translate(daysLeft.ToString("F1")).Colorize(Color.cyan);
                }
                else
                    baseStr += "\n" + "TSA_WD_Inspect_MortarReady".Translate().Colorize(Color.cyan);

                if (AntiAirFireUtils.HasAntiAirUpgrade(this))
                {
                    if (!antiAirDefenseActive)
                        baseStr += "\n" + "TSA_WD_AntiAir_Auto_Off".Translate().Colorize(Color.gray);
                    else if (mortarComp != null && mortarComp.IsAntiAirOnCooldown)
                    {
                        float secLeft = (mortarComp.antiAirCooldownTick - Find.TickManager.TicksGame) / 60f;
                        baseStr += "\n" + "TSA_WD_Inspect_AntiAirCD".Translate(secLeft.ToString("F0")).Colorize(Color.cyan);
                    }
                    else
                        baseStr += "\n" + "TSA_WD_Inspect_AntiAirReady".Translate().Colorize(Color.cyan);
                }
                return CacheInspectString(baseStr);
            }
            if (!GetProductionPauseReason(out string pauseReason))
            {
                baseStr += "\n" + "TSA_WD_Outpost_Inspect_ProductionPaused".Translate(pauseReason).Colorize(Color.yellow);
                return CacheInspectString(baseStr);
            }
            // Same capacity as payout preview: time-weighted average during a cycle when sampled, else current driving capacity.
            if (Outpost_Production_Utils.IsWarehouseOutpost(def))
            {
                var whComp = CompOutpostWarehouse.Get(this);
                float auraPct = OutpostWarehouseAuraUtility.GetWarehouseAuraBonusFraction(this) * 100f;
                if (auraPct > 1e-6f)
                    baseStr += "\n" + "TSA_WD_Warehouse_InspectAuraBoost".Translate(auraPct.ToString("F0")).Colorize(Color.green);
                if (whComp != null)
                    baseStr += "\n" + whComp.GetInspectSummary();
                return CacheInspectString(baseStr);
            }
            float avgCap = GetCapacityForYieldPreview();
            string productLine;
            if (Outpost_Production_Utils.IsRecruitingOutpost(def))
                productLine = Outpost_Recruiting.GetInspectProductLine(this);
            else if (Outpost_Production_Utils.IsEmbassyOutpost(def))
                productLine = Outpost_Embassy.GetInspectProductLine(this);
            else if (Outpost_Production_Utils.IsTradingOutpost(def) && SelectedProductionDef != null)
                productLine = Outpost_Trading.GetTradingDeliveryProductLine(this);
            else if (Outpost_Production_Utils.IsTradingOutpost(def))
                productLine = "";
            else if (Outpost_Production_Utils.IsScavengingOutpost(def))
            {
                productLine = Outpost_Scavenging.GetInspectProductLine(this);
            }
            else if (IsAcademyOutpost)
            {
                productLine = Outpost_Academy.GetInspectProductLine(this);
            }
            else
            {
                var items = Outpost_Production.GetCurrentDeliveryItems(this, avgCap);
                productLine = Outpost_Production.FormatDeliveryProductLine(items);
            }
            var compViral = GetComponent<CompViralSpread>();
            if (Outpost_Warehouse_Delivery.UsesItemDeliveryTraveler(def) && itemDeliveryTargetWorldObjectId >= 0)
            {
                WorldObject explicitDest = Find.WorldObjects.AllWorldObjects.Find(o => o != null && o.ID == itemDeliveryTargetWorldObjectId);
                baseStr += "\n" + "TSA_WD_Warehouse_DeliveryDestInspect".Translate(Outpost_Warehouse_Delivery.GetDestinationLabel(explicitDest));
            }
            int storedTransport = StoredTransportPawnCount;
            if (storedTransport > 0)
                baseStr += "\n" + "TSA_WD_StoredTransportPawns_Inspect".Translate(storedTransport).Colorize(Color.cyan);
            int storedMechs = StoredMechanoidPawnCount;
            if (storedMechs > 0)
                baseStr += "\n" + "TSA_WD_StoredMechanoids_Inspect".Translate(storedMechs).Colorize(Color.cyan);
            if (!string.IsNullOrEmpty(productLine))
            {
                float strength = compViral?.strength ?? 0f;
                string timeStr;
                if (productionTicksLeft > 0)
                {
                    int capIv = GetProductionTicksIntervalCached();
                    int ticksForDisplay = productionTicksLeft > capIv ? capIv : productionTicksLeft;
                    float daysLeft = ticksForDisplay / 60000f;
                    string key = "TSA_WD_Outpost_Delivery_DaysLeft";
                    timeStr = key.Translate(daysLeft.ToString("F1")).ToString();
                    if (timeStr == key) timeStr = daysLeft.ToString("F1") + " days";
                }
                else if (strength < DeliveryMinStrength)
                    timeStr = "TSA_WD_Outpost_Delivery_Paused".Translate();
                else
                    timeStr = "TSA_WD_Outpost_Delivery_Delayed".Translate(strength.ToString("F0"));
                string lineKey = "TSA_WD_Outpost_Inspect_ProducingLine";
                string lineStr = lineKey.Translate(productLine, timeStr).ToString();
                if (lineStr == lineKey) lineStr = "Producing: " + productLine + " (" + timeStr + ")";
                baseStr += "\n" + lineStr.Colorize(Color.cyan);
                if (productionTicksLeft <= 0 && strength < DeliveryMinStrength)
                    baseStr += "\n" + "TSA_WD_Outpost_Delivery_StrengthTooLow".Translate().Colorize(Color.yellow);
            }
            else
                baseStr += "\n" + "TSA_WD_Outpost_Inspect_ProducingNone".Translate();
            return CacheInspectString(baseStr);
        }

        /// <summary>Same production line as inspect pane: what is being produced (or pause reason). For overview window.</summary>
        /// <summary>Shared: compute production product line and time-left status. Used by inspect, overview, and dashboard.</summary>
        public void BuildProductionStatus(out string productLine, out string timeLine)
        {
            productLine = "";
            timeLine = "";
            if (IsRapidResponseOutpost)
            {
                productLine = WD_Outpost_RapidResponse.GetInspectStatusLine(this);
                timeLine = "-";
                return;
            }
            if (IsResearchOutpost)
            {
                productLine = Outpost_Research.GetInspectProductLine(this);
                timeLine = Outpost_Research.CanResearchNow(this, out _)
                    ? "TSA_WD_Research_StatusActive".Translate()
                    : "TSA_WD_Research_StatusPaused".Translate();
                return;
            }
            if (!GetProductionPauseReason(out string pauseReason))
            {
                productLine = pauseReason ?? "TSA_WD_Outpost_Delivery_Paused".Translate().ToString();
                timeLine = "TSA_WD_Outpost_Delivery_Paused".Translate();
                return;
            }
            float avgCap = GetCapacityForYieldPreview();
            if (Outpost_Production_Utils.IsRecruitingOutpost(def))
                productLine = Outpost_Recruiting.GetInspectProductLine(this);
            else if (Outpost_Production_Utils.IsEmbassyOutpost(def))
                productLine = Outpost_Embassy.GetInspectProductLine(this);
            else if (Outpost_Production_Utils.IsTradingOutpost(def) && SelectedProductionDef != null)
                productLine = Outpost_Trading.GetTradingDeliveryProductLine(this);
            else if (Outpost_Production_Utils.IsTradingOutpost(def))
                productLine = "";
            else if (Outpost_Production_Utils.IsScavengingOutpost(def))
                productLine = Outpost_Scavenging.GetInspectProductLine(this);
            else if (IsAcademyOutpost)
                productLine = Outpost_Academy.GetInspectProductLine(this);
            else
            {
                var items = Outpost_Production.GetCurrentDeliveryItems(this, avgCap);
                productLine = Outpost_Production.FormatDeliveryProductLine(items) ?? "";
            }
            if (string.IsNullOrEmpty(productLine))
            {
                timeLine = "-";
                return;
            }
            var compViral = GetComponent<CompViralSpread>();
            float strength = compViral?.strength ?? 0f;
            if (productionTicksLeft > 0)
            {
                int capIv = GetProductionTicksIntervalCached();
                int ticksForDisplay = productionTicksLeft > capIv ? capIv : productionTicksLeft;
                float daysLeft = ticksForDisplay / 60000f;
                string key = "TSA_WD_Outpost_Delivery_DaysLeft";
                timeLine = key.Translate(daysLeft.ToString("F1")).ToString();
                if (timeLine == key) timeLine = daysLeft.ToString("F1") + " days";
            }
            else if (strength < DeliveryMinStrength)
                timeLine = "TSA_WD_Outpost_Delivery_Paused".Translate();
            else
                timeLine = "TSA_WD_Outpost_Delivery_Delayed".Translate(strength.ToString("F0"));
        }

        public string GetProductionLineForOverview()
        {
            if (IsPowerPlantOutpost)
                return Outpost_PowerPlant.GetOverviewProductLine(this);
            if (Outpost_Production_Utils.IsWarehouseOutpost(def))
            {
                var whComp = CompOutpostWarehouse.Get(this);
                return whComp != null
                    ? whComp.GetOverviewStoresLine()
                    : "TSA_WD_Warehouse_InspectEmpty".Translate().ToString();
            }
            BuildProductionStatus(out string productLine, out _);
            return productLine;
        }

        /// <summary>Same time-left as inspect pane: days until delivery, or Paused/Delayed. For overview window.</summary>
        public string GetProductionTimeLeftForOverview()
        {
            if (IsPowerPlantOutpost)
                return Outpost_PowerPlant.GetOverviewTimeLine();
            if (IsResearchOutpost)
            {
                BuildProductionStatus(out _, out string researchTime);
                return researchTime;
            }
            if (Outpost_Production_Utils.IsWarehouseOutpost(def))
                return "-";
            BuildProductionStatus(out _, out string timeLine);
            return timeLine;
        }

        /// <summary>Ticks until next production completion (for inspect string).</summary>
        public int ProductionTicksLeft => productionTicksLeft;

        /// <summary>Production ticks left capped to current cycle length (so UI never shows more than def's productionCycleDays).</summary>
        public int ProductionTicksLeftForDisplay
        {
            get
            {
                int capIv = GetProductionTicksIntervalCached();
                return productionTicksLeft > capIv ? capIv : productionTicksLeft;
            }
        }

        /// <summary>Production cycle length in ticks (for UI timer).</summary>
        public int ProductionTicksIntervalPublic => GetProductionTicksIntervalCached();

        /// <summary>True when "what" is locked for this cycle (after first 25% of cycle).</summary>
        public bool IsSelectionLockedForThisCycle => lockedForThisCycle;

        /// <summary>ThingDef produced this cycle (locked when lockedForThisCycle, else selected). Used for delivery and "Current Cycle" display.</summary>
        public ThingDef GetProducingDefForCurrentCycle()
        {
            if (lockedForThisCycle && !string.IsNullOrEmpty(lockedProductionDefName))
                return DefDatabase<ThingDef>.GetNamedSilentFail(lockedProductionDefName);
            return SelectedProductionDef;
        }

        /// <summary>Scavenging tier actively producing this cycle (locked when lockedForThisCycle, else selected). Null when no tier has been selected.</summary>
        public Outpost_Scavenging.ScavengingKind? GetProducingScavengingKindForCurrentCycle()
        {
            int raw = lockedForThisCycle ? lockedScavengingKindRaw : selectedScavengingKindRaw;
            if (raw < 0 || raw > (int)Outpost_Scavenging.ScavengingKind.Rare) return null;
            return (Outpost_Scavenging.ScavengingKind)raw;
        }

        /// <summary>Currently selected scavenging tier (pre-lock). Null when nothing has been selected yet.</summary>
        public Outpost_Scavenging.ScavengingKind? SelectedScavengingKind
        {
            get
            {
                int raw = selectedScavengingKindRaw;
                if (raw < 0 || raw > (int)Outpost_Scavenging.ScavengingKind.Rare) return null;
                return (Outpost_Scavenging.ScavengingKind)raw;
            }
        }

        /// <summary>True if the player has picked a scavenging tier for this outpost.</summary>
        public bool HasSelectedScavengingKind => SelectedScavengingKind.HasValue;

        /// <summary>PawnKindDef produced this cycle for hunting (locked when lockedForThisCycle, else selected).</summary>
        public PawnKindDef GetProducingPawnKindForCurrentCycle()
        {
            if (lockedForThisCycle && !string.IsNullOrEmpty(lockedPawnKindDefName))
                return DefDatabase<PawnKindDef>.GetNamedSilentFail(lockedPawnKindDefName);
            return SelectedPawnKindForHunting;
        }

        /// <summary>Time-weighted average of delivery-driving capacity this cycle (for UI and spawn).</summary>
        public float GetDeliveryCapacityRunningAverage()
        {
            if (deliveryCapacitySampleCount <= 0) return 0f;
            return deliveryCapacityRunningSum / deliveryCapacitySampleCount;
        }

        /// <summary>Capacity to use for yield previews: time average during an active cycle when we have samples; otherwise current driving capacity.</summary>
        public float GetCapacityForYieldPreview()
        {
            if (productionTicksLeft > 0 && deliveryCapacitySampleCount > 0)
                return GetDeliveryCapacityRunningAverage();
            return Outpost_Production.GetDeliveryDrivingCapacity(this);
        }

        /// <summary>Currently selected production (from Production gizmo). Null if none.</summary>
        public ThingDef SelectedProductionDef => string.IsNullOrEmpty(selectedProductionDefName)
            ? null
            : DefDatabase<ThingDef>.GetNamedSilentFail(selectedProductionDefName);

        /// <summary>For hunting: selected animal kind (meat + leather + wool). Null if not hunting or none selected.</summary>
        public PawnKindDef SelectedPawnKindForHunting => string.IsNullOrEmpty(selectedPawnKindDefName)
            ? null
            : DefDatabase<PawnKindDef>.GetNamedSilentFail(selectedPawnKindDefName);

        /// <summary>For fishing: selected fish ThingDef. Null if none selected.</summary>
        public ThingDef SelectedFishDef => string.IsNullOrEmpty(selectedFishDefName)
            ? null
            : DefDatabase<ThingDef>.GetNamedSilentFail(selectedFishDefName);

        /// <summary>Fish produced this cycle for fishing (locked when lockedForThisCycle, else selected).</summary>
        public ThingDef GetProducingFishForCurrentCycle()
        {
            if (lockedForThisCycle && !string.IsNullOrEmpty(lockedFishDefName))
                return DefDatabase<ThingDef>.GetNamedSilentFail(lockedFishDefName);
            return SelectedFishDef;
        }

        /// <summary>Academy: selected skill defName (serialized).</summary>
        public string SelectedAcademySkillDefName => selectedAcademySkillDefName;

        /// <summary>Academy: locked skill defName for the active cycle.</summary>
        public string LockedAcademySkillDefName => lockedAcademySkillDefName;

        /// <summary>Academy: currently selected skill (null if none).</summary>
        public SkillDef SelectedAcademySkill => string.IsNullOrEmpty(selectedAcademySkillDefName)
            ? null
            : DefDatabase<SkillDef>.GetNamedSilentFail(selectedAcademySkillDefName);

        /// <summary>Recruiting: skill priority for new recruits (null = any).</summary>
        public SkillDef SelectedRecruitPrioritySkill => string.IsNullOrEmpty(selectedRecruitPrioritySkillDefName)
            ? null
            : DefDatabase<SkillDef>.GetNamedSilentFail(selectedRecruitPrioritySkillDefName);

        /// <summary>Recruiting: set skill priority instantly (null clears to Any).</summary>
        public void SetSelectedRecruitPriority(SkillDef skill)
        {
            selectedRecruitPrioritySkillDefName = skill?.defName;
        }

        public void SetSelectedProduction(ThingDef def)
        {
            selectedProductionDefName = def?.defName;
            selectedPawnKindDefName = null;
            selectedFishDefName = null;
            if (def == null)
            {
                productionTicksLeft = 0; // reset timer when clearing (Reset button)
                selectedScavengingKindRaw = -1; // clear scavenging tier when Reset is pressed on a scavenging outpost
                lockedScavengingKindRaw = -1;
                lockedForThisCycle = false;
            }
            else if (productionTicksLeft <= 0)
            {
                productionTicksLeft = GetProductionTicksIntervalCached();
                deliveryCapacityRunningSum = 0f;
                deliveryCapacitySampleCount = 0;
                AddAdHocDeliveryCapacitySample(); // seed the fresh cycle's average once
            }
            RecomputeProductionRequirementCache();
        }

        /// <summary>Set production for scavenging: tier determines min-pawn gating, per-pawn market value, and reward generator.</summary>
        public void SetSelectedScavenging(Outpost_Scavenging.ScavengingKind kind)
        {
            selectedScavengingKindRaw = (int)kind;
            selectedProductionDefName = ThingDefOf.ComponentIndustrial?.defName;
            selectedPawnKindDefName = null;
            selectedFishDefName = null;
            if (productionTicksLeft <= 0)
            {
                productionTicksLeft = GetProductionTicksIntervalCached();
                deliveryCapacityRunningSum = 0f;
                deliveryCapacitySampleCount = 0;
                AddAdHocDeliveryCapacitySample(); // seed the fresh cycle's average once
            }
            RecomputeProductionRequirementCache();
        }

        /// <summary>Set production for hunting: animal kind (we spawn meat + leather + wool).</summary>
        public void SetSelectedHuntingAnimal(PawnKindDef kind)
        {
            selectedPawnKindDefName = kind?.defName;
            selectedFishDefName = null;
            selectedProductionDefName = kind?.RaceProps?.meatDef?.defName;
            if (kind == null)
                productionTicksLeft = 0; // reset timer when clearing (Reset button)
            else if (productionTicksLeft <= 0)
            {
                productionTicksLeft = GetProductionTicksIntervalCached();
                deliveryCapacityRunningSum = 0f;
                deliveryCapacitySampleCount = 0;
                AddAdHocDeliveryCapacitySample(); // seed the fresh cycle's average once
            }
            RecomputeProductionRequirementCache();
        }

        /// <summary>Set production for fishing: one fish ThingDef per delivery. Rejects fish the outpost cannot catch (Animals skill gate).</summary>
        public void SetSelectedFishingFish(ThingDef fish)
        {
            if (fish != null && !Outpost_Fishing.OutpostCanFish(this, fish))
                return;
            selectedFishDefName = fish?.defName;
            selectedPawnKindDefName = null;
            selectedProductionDefName = fish?.defName;
            if (fish == null)
                productionTicksLeft = 0;
            else if (productionTicksLeft <= 0)
            {
                productionTicksLeft = GetProductionTicksIntervalCached();
                deliveryCapacityRunningSum = 0f;
                deliveryCapacitySampleCount = 0;
                AddAdHocDeliveryCapacitySample();
            }
            RecomputeProductionRequirementCache();
        }

        /// <summary>Academy: set the skill taught each cycle. Null clears selection and stops the production timer.</summary>
        public void SetSelectedAcademySkill(SkillDef skill)
        {
            selectedAcademySkillDefName = skill?.defName;
            if (skill == null)
            {
                productionTicksLeft = 0;
                lockedAcademySkillDefName = null;
                lockedForThisCycle = false;
            }
            else if (productionTicksLeft <= 0)
            {
                productionTicksLeft = GetProductionTicksIntervalCached();
                deliveryCapacityRunningSum = 0f;
                deliveryCapacitySampleCount = 0;
                AddAdHocDeliveryCapacitySample(); // seed the fresh cycle's average once
            }
            RecomputeProductionRequirementCache();
        }

        /// <summary>Add one instant sample to the running average so the UI updates immediately when the user changes selection.</summary>
        private void AddAdHocDeliveryCapacitySample()
        {
            float capacity = Outpost_Production.GetDeliveryDrivingCapacity(this);
            int dt = ProductionTimerTickEvery * AverageSampleEveryNTimerTicks;
            deliveryCapacityRunningSum += capacity * dt;
            deliveryCapacitySampleCount += dt;
        }

        private int cachedProductionTicksInterval = -1;
        private int cachedProductionTicksIntervalBuiltAtGameTick = int.MinValue;
        private const int ProductionTicksIntervalCacheTtl = 60;

        /// <summary>Production cycle length in ticks (XML/default × global time multiplier). Cached for a short TTL — avoids def/settings reads every tick × outpost.</summary>
        private int GetProductionTicksIntervalCached()
        {
            int tg = Find.TickManager.TicksGame;
            if (cachedProductionTicksInterval < 1
                || tg - cachedProductionTicksIntervalBuiltAtGameTick >= ProductionTicksIntervalCacheTtl)
            {
                cachedProductionTicksInterval = Outpost_Production_Utils.GetProductionTicksInterval(def);
                cachedProductionTicksIntervalBuiltAtGameTick = tg;
            }
            return cachedProductionTicksInterval;
        }

        private static float DeliveryMinStrength => WorldDominationMod.settings?.outpostDeliveryMinStrength ?? 100f;

        /// <summary>Production timer updates every in-game hour (2500 ticks).</summary>
        private const int ProductionTimerTickEvery = 2500;
        /// <summary>Ad-hoc / post-delivery seed uses this many production-timer slices so UI updates immediately (legacy compatibility weight).</summary>
        private const int AverageSampleEveryNTimerTicks = 12;

        public override void PostAdd()
        {
            base.PostAdd();
            ReinforcementNeighborCache.BumpGeneration();
            if (IsRapidResponseOutpost && Faction == Faction.OfPlayer)
                WD_Outpost_RapidResponse.ApplyEstablishmentDefaults(this);
            if (ShouldRegisterAsInterceptor())
                WorldComponent_InterceptionScheduler.Current?.RegisterInterceptor(this);
            if (IsPowerPlantOutpost)
                Outpost_PowerPlant.NotifyRemotePowerDirty();
            if (Outpost_Production_Utils.IsWarehouseOutpost(def) && Faction == Faction.OfPlayer)
            {
                CompOutpostWarehouse.Get(this)?.TryApplyDefaultColonyShipDestination();
                OutpostWarehouseAuraUtility.InvalidateCache();
            }
            if (Faction != Faction.OfPlayer) return;
            if (GetComponent<CompOutpostLogistics>() == null) return;
            Find.World?.GetComponent<WorldComponent_LogisticsManager>()?.NotifyLogisticsTopologyChanged();
        }

        public override void PostRemove()
        {
            ReinforcementNeighborCache.BumpGeneration();
            if (IsMortarOutpost || IsRapidResponseOutpost)
            {
                WorldComponent_InterceptionScheduler.Current?.UnregisterInterceptor(this);
                WD_Outpost_Mortar.InvalidateFireGizmoCache(this);
            }
            if (IsPowerPlantOutpost)
                Outpost_PowerPlant.NotifyRemotePowerDirty();
            if (Outpost_Production_Utils.IsWarehouseOutpost(def))
                OutpostWarehouseAuraUtility.InvalidateCache();
            if (Faction == Faction.OfPlayer && GetComponent<CompOutpostLogistics>() != null)
                Find.World?.GetComponent<WorldComponent_LogisticsManager>()?.NotifyLogisticsTopologyChanged();
            base.PostRemove();
        }

        protected override void Tick()
        {
            base.Tick();
            int ticksGame = Find.TickManager.TicksGame;
            int prodTimerStagger = (ID ^ (ID >> 8)) & 0x7FF;

            // Biological aging / heal / recruit (once per in-game day, staggered).
            // Prisoner heal/recruit also runs during manual defense (no catch-up after).
            if (Faction == Faction.OfPlayer && (ticksGame + prodTimerStagger) % GenDate.TicksPerDay == 0)
            {
                if (!manualDefenseActive)
                {
                    if (Occupants.Count > 0)
                    {
                        Outpost_OccupantProgression.TickOccupantsBiologicalAgeOneDay(this);
                        Outpost_OccupantProgression.TickOccupantsVirtualHealingOneDay(this);
                    }
                    if (StoredAnimalsAndVehicles.Count > 0)
                        Outpost_OccupantProgression.TickStoredAnimalsBiologicalAgeOneDay(this);
                }
                if (Prisoners.Count > 0)
                {
                    PruneInvalidPrisonersRuntime();
                    Outpost_OccupantProgression.TickPrisonersVirtualHealingOneDay(this);
                    OutpostPrisonerUtility.TickPrisonerRecruitmentOneDay(this);
                }
            }

            if (manualDefenseActive)
                return;

            if (Outpost_Production_Utils.IsWarehouseOutpost(def))
                return;

            if (IsResearchOutpost)
            {
                Outpost_Research.TickResearch(this, ticksGame, prodTimerStagger);
                return;
            }
            if (IsPowerPlantOutpost)
                return;

            int interval = GetProductionTicksIntervalCached();
            if (cachedProductionIntervalForScale >= 0 && cachedProductionIntervalForScale != interval && productionTicksLeft > 0)
            {
                float progress = productionTicksLeft / (float)Mathf.Max(1, cachedProductionIntervalForScale);
                productionTicksLeft = Mathf.Clamp(Mathf.RoundToInt(progress * interval), 1, interval);
            }
            cachedProductionIntervalForScale = interval;

            if (productionTicksLeft > 0)
            {
                var ext = def?.GetModExtension<OutpostDefExtension>();
                bool hasMinNearby = ext != null && ext.minNearbySettlementsOrOutposts > 0;
                // Same cadence as production timer: at most once per in-game hour. (Old condition ran Recompute every tick for 2500 consecutive ticks per day.)
                if (hasMinNearby && (ticksGame + prodTimerStagger) % ProductionTimerTickEvery == 0)
                    RecomputeProductionRequirementCache();

                if (cachedProductionPausedByRequirements)
                    return;
                if ((ticksGame + prodTimerStagger) % ProductionTimerTickEvery != 0)
                    return;

                productionTicksLeft -= ProductionTimerTickEvery;
                if (productionTicksLeft < 0) productionTicksLeft = 0;

                int lockThreshold = (int)(interval * 0.75f);
                if (productionTicksLeft <= lockThreshold && !lockedForThisCycle)
                {
                    lockedProductionDefName = selectedProductionDefName;
                    lockedPawnKindDefName = selectedPawnKindDefName;
                    lockedFishDefName = selectedFishDefName;
                    lockedScavengingKindRaw = selectedScavengingKindRaw;
                    lockedAcademySkillDefName = selectedAcademySkillDefName;
                    lockedForThisCycle = true;
                }

                float capNow = Outpost_Production.GetDeliveryDrivingCapacity(this);
                deliveryCapacityRunningSum += capNow * ProductionTimerTickEvery;
                deliveryCapacitySampleCount += ProductionTimerTickEvery;

                if (productionTicksLeft == 0)
                    forceIdleProductionCheck = true;

                return;
            }

            // Idle / between cycles: vanilla calls Tick every sim tick; we only need heavy checks occasionally while
            // waiting (no items, low strength). Always run immediately after the hourly slice zeros the timer.
            if (!forceIdleProductionCheck && ticksGame - lastIdleProductionHeavyTick < IdleProductionPollMinGapTicks)
                return;
            forceIdleProductionCheck = false;
            lastIdleProductionHeavyTick = ticksGame;

            float avg = deliveryCapacitySampleCount > 0 ? deliveryCapacityRunningSum / deliveryCapacitySampleCount : 0f;
            deliveryCapacityRunningSum = 0f;
            deliveryCapacitySampleCount = 0;
            lockedForThisCycle = false;

            // Start new cycle with one weighted slice so inspect/UI match driving capacity until more ticks accumulate.
            float initialCapacity = Outpost_Production.GetDeliveryDrivingCapacity(this);
            if (initialCapacity > 0.01f)
            {
                int dt = ProductionTimerTickEvery * AverageSampleEveryNTimerTicks;
                deliveryCapacityRunningSum = initialCapacity * dt;
                deliveryCapacitySampleCount = dt;
            }

            if (Outpost_Production_Utils.IsRecruitingOutpost(def))
            {
                productionTicksLeft = interval;
                if (Outpost_Recruiting.Produce(this, avg))
                    Outpost_OccupantProgression.ApplyPayoutSkillXp(this);
                return;
            }
            if (Outpost_Production_Utils.IsTradingOutpost(def))
            {
                productionTicksLeft = interval;
                if (Outpost_Trading.Produce(this, avg))
                    Outpost_OccupantProgression.ApplyPayoutSkillXp(this);
                return;
            }
            if (Outpost_Production_Utils.IsEmbassyOutpost(def))
            {
                productionTicksLeft = interval;
                if (Outpost_Embassy.Produce(this, avg))
                    Outpost_OccupantProgression.ApplyPayoutSkillXp(this);
                return;
            }

            if (Outpost_Production_Utils.IsScavengingOutpost(def))
            {
                if (!HasSelectedScavengingKind) return; // no tier picked → no delivery, don't restart the timer
                productionTicksLeft = interval;
                if (Outpost_Scavenging.Produce(this, avg))
                    Outpost_OccupantProgression.ApplyPayoutSkillXp(this);
                return;
            }

            if (IsAcademyOutpost)
            {
                productionTicksLeft = interval;
                if (Outpost_Academy.TryCompleteProductionCycle(this))
                    Outpost_OccupantProgression.ApplyPayoutSkillXp(this);
                return;
            }

            List<ThingDefCountClass> items = Outpost_Production.GetCurrentDeliveryItems(this, avg);
            if (items == null || items.Count == 0) return;

            var compTick = GetComponent<CompViralSpread>();
            float strength = compTick?.strength ?? 0f;
            if (strength < DeliveryMinStrength)
                return;

            productionTicksLeft = interval;
            WorldActions_Traveler.SpawnOutpostDeliveryTraveler(this, items);
            Outpost_OccupantProgression.ApplyPayoutSkillXp(this);
        }

        /// <summary>Only for loading old saves that had "frozenPawns"; migrated into occupants then cleared.</summary>
        private List<Pawn> legacyFrozenPawnsForLoad;

        /// <summary>ThingIDs of pawns removed from this outpost who must not be auto-added again until their caravan leaves this tile (then they can return and be auto-added).</summary>
        private HashSet<string> autoAddBlockedUntilOffTileThingIds;

        /// <summary>Per-this-outpost upgrade progress (not empire-wide). Keys are <see cref="OutpostUpgradeDef.defName"/>; values are tier ownership flags (&gt;0 = built).</summary>
        private Dictionary<string, int> builtUpgradeLevels = new Dictionary<string, int>();
        /// <summary>In-flight upgrade for this outpost only; cleared after <see cref="ApplyPendingUpgrade"/>.</summary>
        private string pendingUpgradeDefName;
        private int pendingUpgradeLevel;

        /// <summary>When set, item delivery travelers from this outpost target this world object (warehouse or colony). -1 = nearest colony.</summary>
        public int itemDeliveryTargetWorldObjectId = -1;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref Name, "name");
            Scribe_Collections.Look(ref occupants, "occupants", LookMode.Deep);
            Scribe_Collections.Look(ref prisoners, "prisoners", LookMode.Deep);
            Scribe_Collections.Look(ref storedAnimalsAndVehicles, "storedAnimalsAndVehicles", LookMode.Deep);
            Scribe_Collections.Look(ref storedPassengerShuttles, "storedPassengerShuttles", LookMode.Deep);
            Scribe_Collections.Look(ref storedMechanoids, "storedMechanoids", LookMode.Deep);
            Scribe_Collections.Look(ref legacyFrozenPawnsForLoad, "frozenPawns", LookMode.Deep);
            Scribe_Values.Look(ref selectedProductionDefName, "selectedProductionDefName");
            float legacyCaravanBonusStrength = 0f;
            Scribe_Values.Look(ref legacyCaravanBonusStrength, "caravanBonusStrength", 0f);
            Scribe_Values.Look(ref selectedPawnKindDefName, "selectedPawnKindDefName");
            Scribe_Values.Look(ref selectedFishDefName, "selectedFishDefName");
            Scribe_Values.Look(ref productionTicksLeft, "productionTicksLeft", 0);
            Scribe_Values.Look(ref lockedProductionDefName, "lockedProductionDefName");
            Scribe_Values.Look(ref lockedPawnKindDefName, "lockedPawnKindDefName");
            Scribe_Values.Look(ref lockedFishDefName, "lockedFishDefName");
            Scribe_Values.Look(ref selectedScavengingKindRaw, "selectedScavengingKindRaw", -1);
            Scribe_Values.Look(ref lockedScavengingKindRaw, "lockedScavengingKindRaw", -1);
            Scribe_Values.Look(ref lockedAcademySkillDefName, "lockedAcademySkillDefName");
            Scribe_Values.Look(ref selectedAcademySkillDefName, "selectedAcademySkillDefName");
            Scribe_Values.Look(ref selectedRecruitPrioritySkillDefName, "selectedRecruitPrioritySkillDefName");
            Scribe_Values.Look(ref lockedForThisCycle, "lockedForThisCycle", false);
            Scribe_Values.Look(ref deliveryCapacityRunningSum, "deliveryCapacityRunningSum", 0f);
            Scribe_Values.Look(ref deliveryCapacitySampleCount, "deliveryCapacitySampleCount", 0);
            List<string> autoAddBlockedList = null;
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                autoAddBlockedList = new List<string>();
                if (autoAddBlockedUntilOffTileThingIds != null)
                    autoAddBlockedList.AddRange(autoAddBlockedUntilOffTileThingIds);
            }
            Scribe_Collections.Look(ref autoAddBlockedList, "autoAddBlockedUntilOffTileThingIds", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (autoAddBlockedList != null && autoAddBlockedList.Count > 0)
                    autoAddBlockedUntilOffTileThingIds = new HashSet<string>(autoAddBlockedList);
                else
                    autoAddBlockedUntilOffTileThingIds = null;
            }

            Scribe_Collections.Look(ref builtUpgradeLevels, "builtUpgradeLevels", LookMode.Value, LookMode.Value);
            Scribe_Values.Look(ref pendingUpgradeDefName, "pendingUpgradeDefName");
            Scribe_Values.Look(ref pendingUpgradeLevel, "pendingUpgradeLevel", 0);
            Scribe_Values.Look(ref itemDeliveryTargetWorldObjectId, "itemDeliveryTargetWorldObjectId", -1);
            Scribe_Values.Look(ref mortarDefenseActive, "mortarDefenseActive", false);
            Scribe_Values.Look(ref mortarDefenseMaskRaw, "mortarDefenseMaskRaw", (int)MissionMask.All);
            Scribe_Values.Look(ref antiAirDefenseActive, "antiAirDefenseActive", true);
            Scribe_Values.Look(ref antiAirGroupRaw, "antiAirGroupRaw", (int)AntiAirGroupLetter.Off);
            Scribe_Values.Look(ref mortarRaidTargetMaskRaw, "mortarRaidTargetMaskRaw", (int)(RaidTargetMask.Player | RaidTargetMask.Allies));
            Scribe_Values.Look(ref antiAirKindMaskRaw, "antiAirKindMaskRaw", (int)AntiAirKindMask.All);
            Scribe_Values.Look(ref rapidResponseActive, "rapidResponseActive", false);
            Scribe_Values.Look(ref rapidResponseMaskRaw, "rapidResponseMaskRaw", (int)MissionMask.All);
            Scribe_Values.Look(ref rapidResponseRangeOverride, "rapidResponseRangeOverride", -1f);
            Scribe_Values.Look(ref rapidResponseMinStrengthRatio, "rapidResponseMinStrengthRatio", 0.9f);
            Scribe_Values.Look(ref rapidResponseMaxStrengthRatio, "rapidResponseMaxStrengthRatio", RapidResponseUtility.DefaultMaxStrengthRatio);
            int legacyRaidTargetModeRaw = -1;
            Scribe_Values.Look(ref legacyRaidTargetModeRaw, "rapidResponseRaidTargetModeRaw", -1);
            Scribe_Values.Look(ref rapidResponseRaidTargetMaskRaw, "rapidResponseRaidTargetMaskRaw", -1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (rapidResponseRaidTargetMaskRaw < 0)
                {
                    rapidResponseRaidTargetMaskRaw = legacyRaidTargetModeRaw == 1
                        ? (int)RaidTargetMask.Player
                        : (int)(RaidTargetMask.Player | RaidTargetMask.Allies);
                }
            }
            Scribe_Values.Look(ref mortarRangeOverride, "mortarRangeOverride", -1f);
            Scribe_Values.Look(ref antiAirRangeOverride, "antiAirRangeOverride", -1f);
            Scribe_Values.Look(ref manualDefenseActive, "manualDefenseActive", false);
            Scribe_Values.Look(ref pendingSkirmishDefense, "pendingSkirmishDefense", false);
            Scribe_Defs.Look(ref pendingSkirmishTravelerDef, "pendingSkirmishTravelerDef");
            Scribe_References.Look(ref pendingSkirmishEnemyFaction, "pendingSkirmishEnemyFaction");
            Scribe_Values.Look(ref pendingSkirmishTravelerStrength, "pendingSkirmishTravelerStrength", 0f);
            Scribe_Values.Look(ref pendingSkirmishInitialStrength, "pendingSkirmishInitialStrength", 0f);
            Scribe_Values.Look(ref pendingSkirmishSpawnTick, "pendingSkirmishSpawnTick", 0);
            Scribe_Values.Look(ref pendingSkirmishMission, "pendingSkirmishMission", TravelerMission.Raid);
            Scribe_Values.Look(ref pendingSkirmishRaidOrderOutcome, "pendingSkirmishRaidOrderOutcome", RaidOrderOutcome.PlayerOutpostConquestMenu);
            Scribe_Values.Look(ref pendingSkirmishAlliedGoodwillPaid, "pendingSkirmishAlliedGoodwillPaid", 0);
            Scribe_Values.Look(ref pendingSkirmishAlliedGoodwillRefunded, "pendingSkirmishAlliedGoodwillRefunded", false);
            Scribe_References.Look(ref pendingSkirmishOrigin, "pendingSkirmishOrigin");
            Scribe_Collections.Look(ref pendingSkirmishRaidAttackerList, "pendingSkirmishRaidAttackerList", LookMode.Reference);
            Scribe_Collections.Look(ref pendingSkirmishRaidAttackerDetails, "pendingSkirmishRaidAttackerDetails", LookMode.Value);
            Scribe_Collections.Look(ref pendingSkirmishRaidAttackerForceRows, "pendingSkirmishRaidAttackerForceRows", LookMode.Deep);
            Scribe_Collections.Look(ref pendingSkirmishRaidDefenderForceRows, "pendingSkirmishRaidDefenderForceRows", LookMode.Deep);
            Scribe_Collections.Look(ref pendingSkirmishContributionKeys, "pendingSkirmishContributionKeys", LookMode.Reference);
            Scribe_Collections.Look(ref pendingSkirmishContributionValues, "pendingSkirmishContributionValues", LookMode.Value);
            Scribe_Values.Look(ref pendingSkirmishStrengthLost, "pendingSkirmishStrengthLost", 0f);
            Scribe_Values.Look(ref pendingSkirmishPawnsHurt, "pendingSkirmishPawnsHurt", 0);
            Scribe_Values.Look(ref occupantsNeedHealing, "occupantsNeedHealing", false);
            Scribe_Values.Look(ref prisonersNeedHealing, "prisonersNeedHealing", false);
            Scribe_Values.Look(ref takePrisoners, "takePrisoners", true);
            Scribe_Values.Look(ref expertStrategistThingId, "expertStrategistThingId");
            Scribe_Values.Look(ref expertEntertainerThingId, "expertEntertainerThingId");
            Scribe_Values.Look(ref expertCookThingId, "expertCookThingId");
            Scribe_Values.Look(ref expertDoctorThingId, "expertDoctorThingId");
            Scribe_Values.Look(ref expertEngineerThingId, "expertEngineerThingId");
            Scribe_Values.Look(ref expertRecruiterThingId, "expertRecruiterThingId");

            if (occupants == null) occupants = new List<Pawn>();
            if (prisoners == null) prisoners = new List<Pawn>();
            if (storedAnimalsAndVehicles == null) storedAnimalsAndVehicles = new List<Pawn>();
            if (storedPassengerShuttles == null) storedPassengerShuttles = new List<Thing>();
            if (storedMechanoids == null) storedMechanoids = new List<Pawn>();
            if (builtUpgradeLevels == null) builtUpgradeLevels = new Dictionary<string, int>();
            if (Scribe.mode == LoadSaveMode.LoadingVars && legacyFrozenPawnsForLoad != null && legacyFrozenPawnsForLoad.Count > 0)
            {
                if (occupants.Count == 0)
                {
                    foreach (var p in legacyFrozenPawnsForLoad)
                        if (p != null && !p.Destroyed) occupants.Add(p);
                }
                legacyFrozenPawnsForLoad = null;
            }
            if (cachedPauseReasons == null) cachedPauseReasons = new List<string>();

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                MigrateLegacyDeliveryCapacityWeights();
                int interval = GetProductionTicksIntervalCached();
                if (productionTicksLeft > interval)
                    productionTicksLeft = interval; // cap to current def's cycle (e.g. def changed from 30 to 1 day)
                if ((Outpost_Production_Utils.IsRecruitingOutpost(def) || Outpost_Production_Utils.IsTradingOutpost(def) || Outpost_Production_Utils.IsEmbassyOutpost(def)) && productionTicksLeft <= 0)
                    productionTicksLeft = interval;
                else if (Outpost_Production_Utils.IsScavengingOutpost(def) && productionTicksLeft <= 0 && HasSelectedScavengingKind)
                    productionTicksLeft = interval;
                else if (IsAcademyOutpost && productionTicksLeft <= 0 && SelectedAcademySkill != null)
                    productionTicksLeft = interval;
                cachedVirtualPawns = null;
                cachedMechanoidVirtualPawns = null;
                MigrateStoredMechanoidsOnLoad();
                PruneStoredPassengerShuttles();
                // Do not touch pawn hediffs/skills here: LoadingVars runs before genes/needs are
                // fully wired. Hediff_ChemicalDependency.Severity and Hediff_Addiction.Need NRE and
                // abort SaveableFromNode (outpost disappears on load). Deferred to PostLoadInit.
            }

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                RunPostLoadSanityRecovery();
                try
                {
                    RecomputeProductionRequirementCache();
                }
                catch (Exception e)
                {
                    Log.Warning($"[TSA WD] RecomputeProductionRequirementCache failed for {LabelCap}: {e.Message}");
                    cachedProductionPausedByRequirements = false;
                    cachedPauseReasons?.Clear();
                }
                if (!occupantsNeedHealing && occupants != null)
                {
                    try
                    {
                        for (int i = 0; i < occupants.Count; i++)
                        {
                            if (Outpost_OccupantProgression.OccupantNeedsHealing(occupants[i]))
                            {
                                occupantsNeedHealing = true;
                                break;
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Log.Warning($"[TSA WD] OccupantNeedsHealing rescan failed for {LabelCap}: {e.Message}");
                    }
                }
                OutpostExpertUtility.ValidateAssignments(this);
                GetComponent<CompViralSpread>()?.UpdateOutpostStrengthLogically();
                if (ShouldRegisterAsInterceptor())
                    WorldComponent_InterceptionScheduler.Current?.RegisterInterceptor(this);
            }
        }

        /// <summary>Pre-1.x saves stored sparse sample weights (multiples of 12), not production-timer dt. Convert so ratio matches the old mean.</summary>
        private void MigrateLegacyDeliveryCapacityWeights()
        {
            if (deliveryCapacitySampleCount <= 0) return;
            if (deliveryCapacitySampleCount % ProductionTimerTickEvery == 0) return;
            if (deliveryCapacitySampleCount % AverageSampleEveryNTimerTicks != 0) return;
            float legacyAvg = deliveryCapacityRunningSum / deliveryCapacitySampleCount;
            int legacyBlocks = deliveryCapacitySampleCount / AverageSampleEveryNTimerTicks;
            int w = ProductionTimerTickEvery * Mathf.Max(1, legacyBlocks);
            deliveryCapacityRunningSum = legacyAvg * w;
            deliveryCapacitySampleCount = w;
        }

        /// <summary>Call after creating a new outpost so recruiting/trading start with a full timer instead of producing immediately.</summary>
        public void StartProductionTimerIfNeeded()
        {
            if ((Outpost_Production_Utils.IsRecruitingOutpost(def) || Outpost_Production_Utils.IsTradingOutpost(def) || Outpost_Production_Utils.IsEmbassyOutpost(def)) && productionTicksLeft <= 0)
                productionTicksLeft = GetProductionTicksIntervalCached();
            else if (Outpost_Production_Utils.IsScavengingOutpost(def) && productionTicksLeft <= 0 && HasSelectedScavengingKind)
                productionTicksLeft = GetProductionTicksIntervalCached();
            else if (IsAcademyOutpost && productionTicksLeft <= 0 && SelectedAcademySkill != null)
                productionTicksLeft = GetProductionTicksIntervalCached();
        }

        public IReadOnlyDictionary<string, int> BuiltUpgradeLevels => builtUpgradeLevels;
        public string PendingUpgradeDefName => pendingUpgradeDefName;
        public int PendingUpgradeLevel => pendingUpgradeLevel;

        public int GetUpgradeLevel(string upgradeDefName)
        {
            if (string.IsNullOrEmpty(upgradeDefName) || builtUpgradeLevels == null) return 0;
            return builtUpgradeLevels.TryGetValue(upgradeDefName, out int lvl) ? lvl : 0;
        }

        /// <summary>Adds to defensive strength cap; inspect recovery uses <see cref="CompViralSpread.GetInspectDailyDefensiveRecovery"/> (fraction of this cap).</summary>
        public float GetOutpostUpgradeDefensiveBonus()
        {
            if (builtUpgradeLevels == null || builtUpgradeLevels.Count == 0) return 0f;
            float total = 0f;
            foreach (var kv in builtUpgradeLevels)
            {
                if (kv.Value <= 0) continue;
                var def = DefDatabase<OutpostUpgradeDef>.GetNamedSilentFail(kv.Key);
                if (def != null) total += def.defensiveStrengthBonus;
            }
            return total;
        }

        /// <summary>Multiplier bonus from built upgrades on daily offensive regen (includes Hospital; same field also feeds occupant heal for Hospital).</summary>
        public float GetOutpostOffensiveRecoveryUpgradeMultiplierBonus()
        {
            if (builtUpgradeLevels == null || builtUpgradeLevels.Count == 0) return 0f;
            float total = 0f;
            foreach (var kv in builtUpgradeLevels)
            {
                if (kv.Value <= 0) continue;
                var def = DefDatabase<OutpostUpgradeDef>.GetNamedSilentFail(kv.Key);
                if (def != null)
                    total += def.offensiveRecoveryBonus;
            }
            return total;
        }

        public float GetOutpostExpertOffensiveRecoveryMultiplierBonus() =>
            OutpostExpertUtility.GetCombinedExpertOffensiveRecoveryBonus(this);

        public float GetOutpostDefensiveRecoveryMultiplierBonus() =>
            OutpostExpertUtility.GetEngineerDefensiveRecoveryBonus(this);

        /// <summary>Multiplier bonus on daily offensive regen; shown on inspect via <see cref="CompViralSpread.GetInspectDailyOffensiveRecovery"/>.</summary>
        public float GetOutpostOffensiveRecoveryMultiplierBonus()
        {
            float total = GetOutpostOffensiveRecoveryUpgradeMultiplierBonus()
                + GetOutpostExpertOffensiveRecoveryMultiplierBonus();
            if (IsRapidResponseOutpost)
                total += GetRapidResponseOffensiveRecoveryBonus();
            return total;
        }

        /// <summary>Multiplier bonus on daily occupant healing; hospital upgrades only (excludes Rapid Response).</summary>
        public float GetHospitalOccupantHealMultiplierBonus()
        {
            if (builtUpgradeLevels == null || builtUpgradeLevels.Count == 0) return 0f;
            float total = 0f;
            foreach (var kv in builtUpgradeLevels)
            {
                if (kv.Value <= 0) continue;
                var def = DefDatabase<OutpostUpgradeDef>.GetNamedSilentFail(kv.Key);
                if (def != null && def.category == OutpostUpgradeCategory.Hospital)
                    total += def.offensiveRecoveryBonus;
            }
            return total;
        }

        /// <summary>Flat percentage points added to Rapid Response offensive cap. Upgrade-ready hook.</summary>
        public float GetRapidResponseOffensiveStrengthBonus()
        {
            if (!IsRapidResponseOutpost) return 0f;
            float total = WorldDominationMod.settings?.rapidResponseOffensiveStrengthBonus ?? WorldDominationSettings.DefRapidResponseOffensiveStrengthBonus;
            total += GetBuiltUpgradeRapidResponseOffensiveStrengthBonus();
            return Mathf.Max(0f, total);
        }

        /// <summary>Flat percentage points added to Rapid Response offensive recovery. Upgrade-ready hook.</summary>
        public float GetRapidResponseOffensiveRecoveryBonus()
        {
            if (!IsRapidResponseOutpost) return 0f;
            return Mathf.Max(0f, WorldDominationMod.settings?.rapidResponseOffensiveRecoveryBonus ?? WorldDominationSettings.DefRapidResponseOffensiveRecoveryBonus);
        }

        public float GetBuiltUpgradeRapidResponseOffensiveStrengthBonus()
        {
            if (builtUpgradeLevels == null || builtUpgradeLevels.Count == 0) return 0f;
            float total = 0f;
            foreach (var kv in builtUpgradeLevels)
            {
                if (kv.Value <= 0) continue;
                var def = DefDatabase<OutpostUpgradeDef>.GetNamedSilentFail(kv.Key);
                if (def != null) total += def.rapidResponseOffensiveStrengthBonus * kv.Value;
            }
            return total;
        }

        /// <summary>Σ <see cref="OutpostUpgradeDef.allyPullRadiusBonus"/> × built level (Tunnel Network, etc.).</summary>
        public float GetBuiltUpgradeAllyPullRadiusBonus()
        {
            if (builtUpgradeLevels == null || builtUpgradeLevels.Count == 0) return 0f;
            float total = 0f;
            foreach (var kv in builtUpgradeLevels)
            {
                if (kv.Value <= 0) continue;
                var def = DefDatabase<OutpostUpgradeDef>.GetNamedSilentFail(kv.Key);
                if (def != null) total += def.allyPullRadiusBonus * kv.Value;
            }
            return total;
        }

        /// <summary>Σ <see cref="OutpostUpgradeDef.foodStorageMaxBonus"/> × built level.</summary>
        public float GetBuiltUpgradeFoodStorageMaxBonus()
        {
            if (builtUpgradeLevels == null || builtUpgradeLevels.Count == 0) return 0f;
            float total = 0f;
            foreach (var kv in builtUpgradeLevels)
            {
                if (kv.Value <= 0) continue;
                var def = DefDatabase<OutpostUpgradeDef>.GetNamedSilentFail(kv.Key);
                if (def != null) total += def.foodStorageMaxBonus * kv.Value;
            }
            return total;
        }

        /// <summary>Σ <see cref="OutpostUpgradeDef.foodProductionFlatBonus"/> × built level.</summary>
        public float GetBuiltUpgradeFoodProductionFlatBonus()
        {
            if (builtUpgradeLevels == null || builtUpgradeLevels.Count == 0) return 0f;
            float total = 0f;
            foreach (var kv in builtUpgradeLevels)
            {
                if (kv.Value <= 0) continue;
                var def = DefDatabase<OutpostUpgradeDef>.GetNamedSilentFail(kv.Key);
                if (def != null) total += def.foodProductionFlatBonus * kv.Value;
            }
            return total;
        }

        /// <summary>Sum of <see cref="OutpostUpgradeDef.tileFertilityBonus"/> × built level for all active upgrades.</summary>
        public float GetBuiltUpgradeTileFertilityBonus()
        {
            if (builtUpgradeLevels == null || builtUpgradeLevels.Count == 0) return 0f;
            float total = 0f;
            foreach (var kv in builtUpgradeLevels)
            {
                if (kv.Value <= 0) continue;
                var def = DefDatabase<OutpostUpgradeDef>.GetNamedSilentFail(kv.Key);
                if (def != null) total += def.tileFertilityBonus * kv.Value;
            }
            return total;
        }

        /// <summary>Sum of <see cref="OutpostUpgradeDef.tileMiningBonus"/> × built level.</summary>
        public float GetBuiltUpgradeTileMiningBonus()
        {
            if (builtUpgradeLevels == null || builtUpgradeLevels.Count == 0) return 0f;
            float total = 0f;
            foreach (var kv in builtUpgradeLevels)
            {
                if (kv.Value <= 0) continue;
                var def = DefDatabase<OutpostUpgradeDef>.GetNamedSilentFail(kv.Key);
                if (def != null) total += def.tileMiningBonus * kv.Value;
            }
            return total;
        }

        /// <summary>Sum of <see cref="OutpostUpgradeDef.tileAnimalAbundanceBonus"/> × built level.</summary>
        public float GetBuiltUpgradeTileAnimalAbundanceBonus()
        {
            if (builtUpgradeLevels == null || builtUpgradeLevels.Count == 0) return 0f;
            float total = 0f;
            foreach (var kv in builtUpgradeLevels)
            {
                if (kv.Value <= 0) continue;
                var def = DefDatabase<OutpostUpgradeDef>.GetNamedSilentFail(kv.Key);
                if (def != null) total += def.tileAnimalAbundanceBonus * kv.Value;
            }
            return total;
        }

        /// <summary>Sum of <see cref="OutpostUpgradeDef.tileFishAbundanceBonus"/> × built level.</summary>
        public float GetBuiltUpgradeTileFishAbundanceBonus()
        {
            if (builtUpgradeLevels == null || builtUpgradeLevels.Count == 0) return 0f;
            float total = 0f;
            foreach (var kv in builtUpgradeLevels)
            {
                if (kv.Value <= 0) continue;
                var def = DefDatabase<OutpostUpgradeDef>.GetNamedSilentFail(kv.Key);
                if (def != null) total += def.tileFishAbundanceBonus * kv.Value;
            }
            return total;
        }

        /// <summary>Σ <see cref="OutpostUpgradeDef.mortarShellDamageBonus"/> × built level.</summary>
        public float GetBuiltUpgradeMortarShellDamageBonus()
        {
            if (builtUpgradeLevels == null || builtUpgradeLevels.Count == 0) return 0f;
            float total = 0f;
            foreach (var kv in builtUpgradeLevels)
            {
                if (kv.Value <= 0) continue;
                var def = DefDatabase<OutpostUpgradeDef>.GetNamedSilentFail(kv.Key);
                if (def != null) total += def.mortarShellDamageBonus * kv.Value;
            }
            return total;
        }

        /// <summary>True if any built upgrade has <see cref="OutpostUpgradeDef.enablesAntiAir"/>.</summary>
        public bool HasBuiltAntiAirUnlock()
        {
            if (builtUpgradeLevels == null || builtUpgradeLevels.Count == 0) return false;
            foreach (var kv in builtUpgradeLevels)
            {
                if (kv.Value <= 0) continue;
                var def = DefDatabase<OutpostUpgradeDef>.GetNamedSilentFail(kv.Key);
                if (def != null && def.enablesAntiAir) return true;
            }
            return false;
        }

        /// <summary>True if any built upgrade has <see cref="OutpostUpgradeDef.enablesDecontaminationCrew"/>.</summary>
        public bool HasBuiltDecontaminationUnlock()
        {
            if (builtUpgradeLevels == null || builtUpgradeLevels.Count == 0) return false;
            foreach (var kv in builtUpgradeLevels)
            {
                if (kv.Value <= 0) continue;
                var def = DefDatabase<OutpostUpgradeDef>.GetNamedSilentFail(kv.Key);
                if (def != null && def.enablesDecontaminationCrew) return true;
            }
            return false;
        }

        /// <summary>Σ <see cref="OutpostUpgradeDef.mortarHitChanceBonus"/> × built level (additive to final hit chance).</summary>
        public float GetBuiltUpgradeMortarHitChanceBonus()
        {
            if (builtUpgradeLevels == null || builtUpgradeLevels.Count == 0) return 0f;
            float total = 0f;
            foreach (var kv in builtUpgradeLevels)
            {
                if (kv.Value <= 0) continue;
                var def = DefDatabase<OutpostUpgradeDef>.GetNamedSilentFail(kv.Key);
                if (def != null) total += def.mortarHitChanceBonus * kv.Value;
            }
            return total;
        }

        /// <summary>Σ <see cref="OutpostUpgradeDef.mortarCooldownReduction"/> × built level (same units as cumulative Shooting for cooldown formula).</summary>
        public float GetBuiltUpgradeMortarCooldownReduction()
        {
            if (builtUpgradeLevels == null || builtUpgradeLevels.Count == 0) return 0f;
            float total = 0f;
            foreach (var kv in builtUpgradeLevels)
            {
                if (kv.Value <= 0) continue;
                var def = DefDatabase<OutpostUpgradeDef>.GetNamedSilentFail(kv.Key);
                if (def != null) total += def.mortarCooldownReduction * kv.Value;
            }
            return total;
        }

        /// <summary>Σ <see cref="OutpostUpgradeDef.mortarRangeBonus"/> × built level (additive world tiles to player mortar max range).</summary>
        public float GetBuiltUpgradeMortarRangeBonus()
        {
            if (builtUpgradeLevels == null || builtUpgradeLevels.Count == 0) return 0f;
            float total = 0f;
            foreach (var kv in builtUpgradeLevels)
            {
                if (kv.Value <= 0) continue;
                var def = DefDatabase<OutpostUpgradeDef>.GetNamedSilentFail(kv.Key);
                if (def != null) total += def.mortarRangeBonus * kv.Value;
            }
            return total;
        }

        /// <summary>Σ <see cref="OutpostUpgradeDef.researchEfficiencyBonus"/> × built level (flat percentage points on research efficiency).</summary>
        public float GetResearchUpgradeEfficiencyBonus()
        {
            if (builtUpgradeLevels == null || builtUpgradeLevels.Count == 0) return 0f;
            float total = 0f;
            foreach (var kv in builtUpgradeLevels)
            {
                if (kv.Value <= 0) continue;
                var def = DefDatabase<OutpostUpgradeDef>.GetNamedSilentFail(kv.Key);
                if (def != null) total += def.researchEfficiencyBonus * kv.Value;
            }
            return total;
        }

        /// <summary>Σ <see cref="OutpostUpgradeDef.productionEfficiencyBonus"/> × built level (flat percentage points on production output multiplier).</summary>
        public float GetProductionUpgradeEfficiencyBonus()
        {
            if (builtUpgradeLevels == null || builtUpgradeLevels.Count == 0) return 0f;
            float total = 0f;
            foreach (var kv in builtUpgradeLevels)
            {
                if (kv.Value <= 0) continue;
                var def = DefDatabase<OutpostUpgradeDef>.GetNamedSilentFail(kv.Key);
                if (def != null) total += def.productionEfficiencyBonus * kv.Value;
            }
            return total;
        }

        /// <summary>Σ <see cref="OutpostUpgradeDef.warehouseAuraBonus"/> × built level.</summary>
        public float GetWarehouseAuraBonusUpgradeBonus()
        {
            if (builtUpgradeLevels == null || builtUpgradeLevels.Count == 0) return 0f;
            float total = 0f;
            foreach (var kv in builtUpgradeLevels)
            {
                if (kv.Value <= 0) continue;
                var def = DefDatabase<OutpostUpgradeDef>.GetNamedSilentFail(kv.Key);
                if (def != null) total += def.warehouseAuraBonus * kv.Value;
            }
            return total;
        }

        /// <summary>Σ <see cref="OutpostUpgradeDef.warehouseAuraRadiusBonus"/> × built level.</summary>
        public float GetWarehouseAuraRadiusUpgradeBonus()
        {
            if (builtUpgradeLevels == null || builtUpgradeLevels.Count == 0) return 0f;
            float total = 0f;
            foreach (var kv in builtUpgradeLevels)
            {
                if (kv.Value <= 0) continue;
                var def = DefDatabase<OutpostUpgradeDef>.GetNamedSilentFail(kv.Key);
                if (def != null) total += def.warehouseAuraRadiusBonus * kv.Value;
            }
            return total;
        }

        /// <summary>Σ <see cref="OutpostUpgradeDef.remotePowerWattsBonus"/> × built level (flat remote colony watts).</summary>
        public float GetRemotePowerUpgradeBonus()
        {
            if (builtUpgradeLevels == null || builtUpgradeLevels.Count == 0) return 0f;
            float total = 0f;
            foreach (var kv in builtUpgradeLevels)
            {
                if (kv.Value <= 0) continue;
                var def = DefDatabase<OutpostUpgradeDef>.GetNamedSilentFail(kv.Key);
                if (def != null) total += def.remotePowerWattsBonus * kv.Value;
            }
            return total;
        }

        public bool TryQueuePendingUpgrade(string upgradeDefName, int targetLevel)
        {
            if (string.IsNullOrEmpty(upgradeDefName) || targetLevel <= 0) return false;
            if (!string.IsNullOrEmpty(pendingUpgradeDefName)) return false;
            pendingUpgradeDefName = upgradeDefName;
            pendingUpgradeLevel = targetLevel;
            return true;
        }

        public bool ClearPendingUpgrade()
        {
            if (string.IsNullOrEmpty(pendingUpgradeDefName) && pendingUpgradeLevel <= 0) return false;
            pendingUpgradeDefName = null;
            pendingUpgradeLevel = 0;
            return true;
        }

        public bool ClearPendingUpgradeIfMatches(string upgradeDefName, int level)
        {
            if (string.IsNullOrEmpty(pendingUpgradeDefName)) return false;
            if (!string.Equals(pendingUpgradeDefName, upgradeDefName, StringComparison.Ordinal)) return false;
            if (level > 0 && pendingUpgradeLevel != level) return false;
            return ClearPendingUpgrade();
        }

        public bool ApplyPendingUpgrade()
        {
            if (string.IsNullOrEmpty(pendingUpgradeDefName) || pendingUpgradeLevel <= 0) return false;
            bool ok = ApplyUpgrade(pendingUpgradeDefName, pendingUpgradeLevel);
            ClearPendingUpgrade();
            return ok;
        }

        public bool ApplyUpgrade(string upgradeDefName, int level)
        {
            var defUpgrade = DefDatabase<OutpostUpgradeDef>.GetNamedSilentFail(upgradeDefName);
            if (defUpgrade == null) return false;
            if (level <= 0) return false;

            var compSpread = GetComponent<CompViralSpread>();
            bool trackDefCap = compSpread != null && compSpread.IsOutpost;
            float structuralDefBefore = trackDefCap ? compSpread.GetBaseDefensiveStrength() : 0f;

            if (!string.IsNullOrEmpty(defUpgrade.upgradeLineId))
            {
                List<string> drop = null;
                foreach (var kv in builtUpgradeLevels)
                {
                    if (kv.Value <= 0) continue;
                    var other = DefDatabase<OutpostUpgradeDef>.GetNamedSilentFail(kv.Key);
                    if (other == null) continue;
                    if (other.upgradeLineId == defUpgrade.upgradeLineId && other.lineTier < defUpgrade.lineTier)
                    {
                        drop ??= new List<string>();
                        drop.Add(kv.Key);
                    }
                }
                if (drop != null)
                    for (int i = 0; i < drop.Count; i++)
                        builtUpgradeLevels.Remove(drop[i]);
            }

            builtUpgradeLevels[upgradeDefName] = level;
            if (trackDefCap && compSpread != null)
                compSpread.ApplyDefensiveCurrentForStructuralCapDelta(compSpread.GetBaseDefensiveStrength() - structuralDefBefore);
            compSpread?.UpdateOutpostStrengthLogically();
            if (WorldDominationMod.settings?.foodLogisticsActive == true && GetComponent<CompOutpostLogistics>() != null)
                Find.World?.GetComponent<WorldComponent_LogisticsManager>()?.NotifyFoodLogisticsInputsChanged();
            if (IsPowerPlantOutpost)
                Outpost_PowerPlant.NotifyRemotePowerDirty();
            if (Outpost_Production_Utils.IsWarehouseOutpost(def)
                && (defUpgrade.warehouseAuraBonus > 0f || defUpgrade.warehouseAuraRadiusBonus > 0f))
                OutpostWarehouseAuraUtility.InvalidateCache();
            if (IsMortarOutpost && defUpgrade.enablesAntiAir)
            {
                // AA unlock: default auto-target on, swap world icon, register interceptor.
                if (!antiAirDefenseActive)
                    antiAirDefenseActive = true;
                InvalidateWorldMapIconCache();
                RefreshInterceptorRegistration();
            }
            InvalidateInspectCache();
            return true;
        }
    }
}

