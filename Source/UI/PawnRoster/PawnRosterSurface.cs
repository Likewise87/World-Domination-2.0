#nullable disable
using System;
using System.Collections.Generic;
using RimWorld;

namespace TSA_WorldDomination
{
    /// <summary>What a roster surface's rows can provide. Gates special columns and some common ones.</summary>
    [Flags]
    public enum PawnRosterCapability
    {
        None = 0,
        PrisonerRows = 1,
        RecruitSchedule = 2,
        PlayerOwned = 4,
        MultiLocation = 8,
        OutpostContext = 16,
        ArmoryGear = 32,
        StrengthBudget = 64,
        TransferRows = 128
    }

    /// <summary>
    /// Per-window capabilities, the common columns each window already draws itself, and
    /// view-state lifetime. Registry extras are allowed columns minus native ones.
    /// </summary>
    public static class PawnRosterSurface
    {
        private static readonly Dictionary<PawnRosterColumnWindow, HashSet<string>> nativeIds =
            new Dictionary<PawnRosterColumnWindow, HashSet<string>>();

        public static PawnRosterCapability CapabilitiesFor(PawnRosterColumnWindow window)
        {
            switch (window)
            {
                case PawnRosterColumnWindow.AllPlayerPawns:
                case PawnRosterColumnWindow.RemoteEstablish:
                    return PawnRosterCapability.PlayerOwned | PawnRosterCapability.MultiLocation;
                case PawnRosterColumnWindow.Prisoners:
                    return PawnRosterCapability.PrisonerRows | PawnRosterCapability.RecruitSchedule
                        | PawnRosterCapability.MultiLocation;
                case PawnRosterColumnWindow.OutpostPawns:
                    return PawnRosterCapability.PlayerOwned | PawnRosterCapability.PrisonerRows
                        | PawnRosterCapability.OutpostContext;
                case PawnRosterColumnWindow.OutpostArmory:
                    return PawnRosterCapability.PlayerOwned | PawnRosterCapability.OutpostContext
                        | PawnRosterCapability.ArmoryGear;
                case PawnRosterColumnWindow.StrengthWithdraw:
                case PawnRosterColumnWindow.DefenseDeploy:
                    return PawnRosterCapability.PlayerOwned | PawnRosterCapability.OutpostContext
                        | PawnRosterCapability.StrengthBudget;
                case PawnRosterColumnWindow.SmartSend:
                    return PawnRosterCapability.PlayerOwned | PawnRosterCapability.TransferRows;
                case PawnRosterColumnWindow.AddCaravanPawns:
                    return PawnRosterCapability.PlayerOwned | PawnRosterCapability.OutpostContext;
                default:
                    return PawnRosterCapability.None;
            }
        }

        public static bool Has(PawnRosterColumnWindow window, PawnRosterCapability cap) =>
            cap == PawnRosterCapability.None || (CapabilitiesFor(window) & cap) == cap;

        /// <summary>
        /// Static view state (survives close): APP, PRS, ARM, SMS. Others reset on open.
        /// </summary>
        public static bool ViewStatePersists(PawnRosterColumnWindow window) =>
            window == PawnRosterColumnWindow.AllPlayerPawns
            || window == PawnRosterColumnWindow.Prisoners
            || window == PawnRosterColumnWindow.OutpostArmory
            || window == PawnRosterColumnWindow.SmartSend;

        /// <summary>True when <paramref name="window"/> draws common column <paramref name="id"/> itself.</summary>
        public static bool IsNative(PawnRosterColumnWindow window, string id)
        {
            if (id == null) return false;
            return NativeIds(window).Contains(id);
        }

        private static HashSet<string> NativeIds(PawnRosterColumnWindow window)
        {
            if (nativeIds.TryGetValue(window, out HashSet<string> set))
                return set;
            set = new HashSet<string>();
            switch (window)
            {
                case PawnRosterColumnWindow.AllPlayerPawns:
                case PawnRosterColumnWindow.RemoteEstablish:
                    AddAll(set, PawnRosterColumnIds.Type, PawnRosterColumnIds.Star, PawnRosterColumnIds.New,
                        PawnRosterColumnIds.Age, PawnRosterColumnIds.Health, PawnRosterColumnIds.Traits,
                        PawnRosterColumnIds.Xenotype, PawnRosterColumnIds.Psycasts, PawnRosterColumnIds.Ideology);
                    AddSkills(set);
                    break;
                case PawnRosterColumnWindow.Prisoners:
                    AddAll(set, PawnRosterColumnIds.Age, PawnRosterColumnIds.Health, PawnRosterColumnIds.Hurt,
                        PawnRosterColumnIds.Traits, PawnRosterColumnIds.Xenotype, PawnRosterColumnIds.Psycasts,
                        PawnRosterColumnIds.Ideology);
                    AddSkills(set);
                    break;
                case PawnRosterColumnWindow.OutpostPawns:
                    // Skills are native as Shooting/Melee plus Skill_* full-grid ids.
                    AddAll(set, PawnRosterColumnIds.Type, PawnRosterColumnIds.Star, PawnRosterColumnIds.New,
                        PawnRosterColumnIds.Age, PawnRosterColumnIds.Traits, PawnRosterColumnIds.Xenotype,
                        PawnRosterColumnIds.Psycasts, PawnRosterColumnIds.Ideology, PawnRosterColumnIds.Strength,
                        PawnRosterColumnIds.Hurt);
                    AddSkills(set);
                    break;
                case PawnRosterColumnWindow.OutpostArmory:
                    AddAll(set, PawnRosterColumnIds.Type, PawnRosterColumnIds.Star, PawnRosterColumnIds.New,
                        PawnRosterColumnIds.Age, PawnRosterColumnIds.Health, PawnRosterColumnIds.Traits,
                        PawnRosterColumnIds.Xenotype, PawnRosterColumnIds.Psycasts, PawnRosterColumnIds.Ideology,
                        PawnRosterColumnIds.Mass, PawnRosterColumnIds.Bulk);
                    AddSkills(set);
                    break;
                case PawnRosterColumnWindow.StrengthWithdraw:
                case PawnRosterColumnWindow.DefenseDeploy:
                    AddAll(set, PawnRosterColumnIds.Gear, PawnRosterColumnIds.Shooting, PawnRosterColumnIds.Melee,
                        PawnRosterColumnIds.Health, PawnRosterColumnIds.Strength);
                    break;
                case PawnRosterColumnWindow.AddCaravanPawns:
                    AddAll(set, PawnRosterColumnIds.Type, PawnRosterColumnIds.Age, PawnRosterColumnIds.Shooting,
                        PawnRosterColumnIds.Melee, PawnRosterColumnIds.Strength);
                    break;
            }
            nativeIds[window] = set;
            return set;
        }

        private static void AddAll(HashSet<string> set, params string[] ids)
        {
            for (int i = 0; i < ids.Length; i++)
                set.Add(ids[i]);
        }

        private static void AddSkills(HashSet<string> set)
        {
            SkillDef[] skills = PlayerPawnRosterUtility.AllSkillColumns;
            for (int i = 0; i < skills.Length; i++)
            {
                if (skills[i] != null)
                    set.Add(PawnRosterColumnIds.Skill(skills[i]));
            }
        }
    }
}
