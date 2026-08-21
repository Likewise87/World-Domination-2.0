using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Shared untinted expert-role command icons (main-thread load).</summary>
    [StaticConstructorOnStartup]
    public static class OutpostExpertRoleIcons
    {
        private static readonly Texture2D IconStrategist;
        private static readonly Texture2D IconEntertainer;
        private static readonly Texture2D IconCook;
        private static readonly Texture2D IconDoctor;
        private static readonly Texture2D IconEngineer;
        private static readonly Texture2D IconWarden;

        static OutpostExpertRoleIcons()
        {
            IconStrategist = ContentFinder<Texture2D>.Get("UI/Commands/Expert_Strategist", false) ?? BaseContent.BadTex;
            IconEntertainer = ContentFinder<Texture2D>.Get("UI/Commands/Expert_Artist", false) ?? BaseContent.BadTex;
            IconCook = ContentFinder<Texture2D>.Get("UI/Commands/Expert_Cook", false) ?? BaseContent.BadTex;
            IconDoctor = ContentFinder<Texture2D>.Get("UI/Commands/Expert_Doctor", false) ?? BaseContent.BadTex;
            IconEngineer = ContentFinder<Texture2D>.Get("UI/Commands/Expert_Engineer", false) ?? BaseContent.BadTex;
            IconWarden = ContentFinder<Texture2D>.Get("UI/Commands/Expert_Warden", false) ?? BaseContent.BadTex;
        }

        public static Texture2D Get(OutpostExpertRole role) => role switch
        {
            OutpostExpertRole.Strategist => IconStrategist,
            OutpostExpertRole.Entertainer => IconEntertainer,
            OutpostExpertRole.Cook => IconCook,
            OutpostExpertRole.Doctor => IconDoctor,
            OutpostExpertRole.Engineer => IconEngineer,
            OutpostExpertRole.Recruiter => IconWarden,
            _ => BaseContent.BadTex
        };
    }
}
