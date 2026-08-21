namespace TSA_WorldDomination.Settings
{
    /// <summary>
    /// Backward compatibility: old saves and mod options reference TSA_WorldDomination.Settings.WorldDominationSettings.
    /// This empty subclass allows the game to resolve that type and deserialize into it; the instance is a full
    /// WorldDominationSettings and works everywhere the base type is used.
    /// </summary>
    public class WorldDominationSettings : TSA_WorldDomination.WorldDominationSettings
    {
    }
}
