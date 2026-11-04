using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Cached textures for the mod-settings shell (brand wordmark + Overview preset icons).
    /// Isolated holder so <see cref="StaticConstructorOnStartupAttribute"/> loads them on the main thread
    /// without putting Texture2D fields on <see cref="WorldDominationMod"/> / <see cref="WdSettingsShell"/>.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class WdSettingsChromeAssets
    {
        internal static readonly Texture2D BrandTitle;
        internal static readonly Texture2D ApplySave;
        internal static readonly Texture2D PerfBest;
        internal static readonly Texture2D PerfNormal;
        internal static readonly Texture2D PerfReduced;
        internal static readonly Texture2D DiffEasy;
        internal static readonly Texture2D DiffMedium;
        internal static readonly Texture2D DiffHard;

        static WdSettingsChromeAssets()
        {
            BrandTitle = ContentFinder<Texture2D>.Get("UI/Settings/WD_BrandTitle", false);
            ApplySave = ContentFinder<Texture2D>.Get("UI/Commands/Save", false);
            PerfBest = ContentFinder<Texture2D>.Get("UI/Commands/Nimble", false);
            PerfNormal = ContentFinder<Texture2D>.Get("UI/Commands/Expert_Strategist", false);
            PerfReduced = ContentFinder<Texture2D>.Get("UI/Commands/Icon_ActiveTravelers", false);
            DiffEasy = ContentFinder<Texture2D>.Get("WorldObjects/Travelers/Caravan_Raiders", false);
            DiffMedium = ContentFinder<Texture2D>.Get("WorldObjects/Travelers/Caravan_RapidResponse", false);
            DiffHard = ContentFinder<Texture2D>.Get("WorldObjects/Travelers/Caravan_Invasion", false);
        }
    }
}
