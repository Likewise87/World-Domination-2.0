using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.Sound;

namespace TSA_WorldDomination
{
    /// <summary>
    /// World-map combat oneshots gated by Experimental <c>enableWorldMapSounds</c> (on by default)
    /// and by the world map being visible. Assault-map mortar fire is the exception (map oneshot).
    /// </summary>
    public static class WdWorldMapSound
    {
        public const string AtLight = "TSA_WD_AT_Turret_Fire_Light";
        public const string AtMedium = "TSA_WD_AT_Turret_Fire_Medium";
        public const string AtHeavy = "TSA_WD_AT_Turret_Fire_Heavy";
        public const string Mortar = "TSA_WD_Mortar_Fire";
        public const string MortarMap = "TSA_WD_Mortar_Fire_Map";
        public const string Flak = "TSA_WD_Flak_Fire";

        public static bool Enabled =>
            WorldDominationMod.settings?.enableWorldMapSounds
            ?? WorldDominationSettings.DefEnableWorldMapSounds;

        /// <summary>True while the camera is on the world map (not a local map / menus).</summary>
        public static bool WorldMapOpen => WorldRendererUtility.WorldRendered;

        public static void Play(string defName)
        {
            if (!Enabled || !WorldMapOpen || defName.NullOrEmpty()) return;
            DefDatabase<SoundDef>.GetNamedSilentFail(defName)?.PlayOneShotOnCamera();
        }

        public static void PlayAtTurretFire(AtTurretTier tier)
        {
            switch (tier)
            {
                case AtTurretTier.Light:
                    Play(AtLight);
                    break;
                case AtTurretTier.Heavy:
                    Play(AtHeavy);
                    break;
                default:
                    Play(AtMedium);
                    break;
            }
        }

        /// <summary>
        /// World-map mortar / AT shell launch. Always uses the WorldOnly oneshot —
        /// do not pick <see cref="MortarMap"/> via <c>Find.CurrentMap != null</c>: a colony
        /// map stays current while the camera is on the world, and MapOnly audio is discarded.
        /// </summary>
        public static void PlayMortarFire() => Play(Mortar);

        /// <summary>
        /// Assault artillery support fired from a settlement map: play even when the world map is closed.
        /// Uses the map mortar oneshot only (not AA/AT/other world mortars).
        /// </summary>
        public static void PlayAssaultMortarFire()
        {
            if (!Enabled) return;
            DefDatabase<SoundDef>.GetNamedSilentFail(MortarMap)?.PlayOneShotOnCamera();
        }

        public static void PlayFlakFire() => Play(Flak);
    }
}
