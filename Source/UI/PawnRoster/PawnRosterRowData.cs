#nullable disable
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Row types that can feed registry columns (cells + sort) without live pawn queries per draw.</summary>
    public interface IPawnRosterRow
    {
        PawnRosterRowData RosterData { get; }
    }

    /// <summary>
    /// Cached per-row values for registry columns. Built once per row rebuild (lazily, on first use),
    /// never per frame.
    /// </summary>
    public sealed class PawnRosterRowData
    {
        public Pawn pawn;
        public string thingId = "";
        public string nameLabel = "";
        public PlayerPawnSortCategory category;
        public string typeLabel = "";
        public bool isStarred;
        public int daysSinceJoin = -1;
        public string daysSinceJoinLabel = "";
        public int ageYears;
        public string ageLabel = "";
        public float healthPercent;
        public string healthLabel = "";
        public bool needsHealing;
        public string traitsDisplay = "";
        public string traitsTip = "";
        public string xenotypeDisplay = "";
        public string xenotypeTip = "";
        public string psycastsDisplay = "";
        public string psycastsTip = "";
        public string ideologyDisplay = "";
        public string ideologyTip = "";
        /// <summary>Indexed like <see cref="PlayerPawnRosterUtility.AllSkillColumns"/>.</summary>
        public int[] skillLevels;
        public int bestSkillLevel;
        /// <summary>Animals / vehicles: skill cells show a dash.</summary>
        public bool sparseSkills;
        public int gearCount;
        public float massCur;
        public float massCap;
        public string massLabel = "";
        public bool hasBulk;
        public float bulkCur;
        public float bulkCap;
        public string bulkLabel = "";
        public float strength;
        public string strengthLabel = "";

        public static PawnRosterRowData Get(
            ref PawnRosterRowData cache,
            Pawn pawn,
            VirtualPawnSummary summary = null,
            PlayerPawnOutpostRole role = PlayerPawnOutpostRole.None)
        {
            if (cache == null || cache.pawn != pawn)
                cache = Build(pawn, summary, role);
            return cache;
        }

        public static PawnRosterRowData Build(
            Pawn pawn,
            VirtualPawnSummary summary = null,
            PlayerPawnOutpostRole role = PlayerPawnOutpostRole.None)
        {
            var d = new PawnRosterRowData { pawn = pawn };
            if (pawn == null)
            {
                d.skillLevels = new int[PlayerPawnRosterUtility.AllSkillColumns.Length];
                return d;
            }

            d.thingId = pawn.ThingID ?? "";
            d.nameLabel = pawn.Name?.ToStringShort ?? pawn.LabelShortCap ?? "";
            d.category = PlayerPawnRosterUtility.ClassifyPawn(pawn, role);
            d.typeLabel = PlayerPawnRosterUtility.GetPawnTypeLabel(d.category);
            d.isStarred = !d.thingId.NullOrEmpty()
                && WorldComponent_PlayerPawnFavorites.Get()?.IsStarred(d.thingId) == true;
            d.daysSinceJoin = PlayerPawnRosterUtility.GetDaysSinceJoin(pawn);
            d.daysSinceJoinLabel = d.daysSinceJoin >= 0 ? d.daysSinceJoin.ToString() : "";
            d.ageYears = pawn.ageTracker != null ? pawn.ageTracker.AgeBiologicalYears : 0;
            d.ageLabel = d.ageYears.ToString();
            d.healthPercent = OutpostStrengthBudgetUi.GetHealthPercent(pawn);
            d.healthLabel = Mathf.RoundToInt(d.healthPercent).ToString() + "%";
            d.needsHealing = Outpost_OccupantProgression.OccupantShowsHurtIcon(pawn);

            PrisonerRosterUtility.FormatTraits(pawn, out d.traitsDisplay, out d.traitsTip);
            PawnRosterTraitFilter.FormatXenotype(pawn, out d.xenotypeDisplay, out d.xenotypeTip);
            PawnRosterTraitFilter.FormatPsycasts(pawn, out d.psycastsDisplay, out d.psycastsTip);
            PawnRosterTraitFilter.FormatIdeology(pawn, out d.ideologyDisplay, out d.ideologyTip);

            VirtualPawnSummary s = summary ?? VirtualPawnSummary.FromPawn(pawn);
            SkillDef[] skills = PlayerPawnRosterUtility.AllSkillColumns;
            d.skillLevels = new int[skills.Length];
            d.sparseSkills = pawn.skills == null && pawn.RaceProps?.IsMechanoid != true;
            if (s != null && !d.sparseSkills)
            {
                for (int i = 0; i < skills.Length; i++)
                    d.skillLevels[i] = s.GetSkill(skills[i]);
            }
            d.bestSkillLevel = PlayerPawnRosterUtility.GetBestSkillLevel(d.skillLevels);

            int gear = 0;
            if (pawn.equipment?.AllEquipmentListForReading != null)
                gear += pawn.equipment.AllEquipmentListForReading.Count;
            if (pawn.apparel?.WornApparel != null)
                gear += pawn.apparel.WornApparel.Count;
            d.gearCount = gear;

            if (OutpostCeAmmoCompat.TryGetCarryLoad(pawn,
                    out float massCur, out float massCap, out float bulkCur, out float bulkCap))
            {
                d.massCur = massCur;
                d.massCap = massCap;
                d.bulkCur = bulkCur;
                d.bulkCap = bulkCap;
                d.hasBulk = true;
            }
            else
            {
                d.massCur = MassUtility.GearAndInventoryMass(pawn);
                d.massCap = MassUtility.Capacity(pawn);
            }
            d.massLabel = FormatCarryPair(d.massCur, d.massCap);
            d.bulkLabel = d.hasBulk ? FormatCarryPair(d.bulkCur, d.bulkCap) : "-";

            d.strength = s?.CombatStrength ?? 0f;
            d.strengthLabel = d.strength.ToString("F0");
            return d;
        }

        public int SkillLevel(int skillIndex) =>
            skillLevels != null && skillIndex >= 0 && skillIndex < skillLevels.Length ? skillLevels[skillIndex] : 0;

        public static string FormatCarryPair(float cur, float cap) =>
            cur.ToString("F0") + "/" + cap.ToString("F0");

        /// <summary>99%+ red, 90%+ orange, 70%+ yellow, else green.</summary>
        public static Color CarryLoadTextColor(float cur, float cap)
        {
            if (cap <= 0f)
                return cur > 0f ? Color.red : new Color(0.45f, 0.85f, 0.45f);
            float ratio = cur / cap;
            if (ratio >= 0.99f) return Color.red;
            if (ratio >= 0.9f) return new Color(1f, 0.55f, 0.15f);
            if (ratio >= 0.7f) return new Color(1f, 0.9f, 0.25f);
            return new Color(0.45f, 0.85f, 0.45f);
        }

        /// <summary>Reuses one data object per pawn across a rebuild (dialogs whose rows are plain pawns).</summary>
        public static PawnRosterRowData ForPawn(Dictionary<Pawn, PawnRosterRowData> cache, Pawn pawn)
        {
            if (pawn == null) return null;
            if (cache.TryGetValue(pawn, out PawnRosterRowData d))
                return d;
            d = Build(pawn);
            cache[pawn] = d;
            return d;
        }
    }
}
