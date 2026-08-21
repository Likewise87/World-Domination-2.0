using RimWorld;
using Verse;
using Verse.Sound;

namespace TSA_WorldDomination
{
    /// <summary>World-map combat oneshots gated by Experimental <c>enableWorldMapSounds</c> (on by default).</summary>
    public static class WdWorldMapSound
    {
        public const string AtLight = "TSA_WD_AT_Turret_Fire_Light";
        public const string AtMedium = "TSA_WD_AT_Turret_Fire_Medium";
        public const string AtHeavy = "TSA_WD_AT_Turret_Fire_Heavy";
        public const string Mortar = "TSA_WD_Mortar_Fire";
        public const string Flak = "TSA_WD_Flak_Fire";

        public static bool Enabled =>
            WorldDominationMod.settings?.enableWorldMapSounds
            ?? WorldDominationSettings.DefEnableWorldMapSounds;

        public static void Play(string defName)
        {
            if (!Enabled || defName.NullOrEmpty()) return;
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

        public static void PlayMortarFire() => Play(Mortar);

        public static void PlayFlakFire() => Play(Flak);
    }
}
