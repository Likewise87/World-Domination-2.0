using System.Linq;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Shows the "mod updated" popup once when entering play, if this version has not been acknowledged.
    /// Uses <see cref="FinalizeInit"/> (fires once after world load) + a deferred callback so the
    /// window stack and map are ready. No per-tick overhead.
    /// Current mod version for the popup is <see cref="WD_UpdateEntries.Entries"/>[0] (newest, e.g. 2.3.11) in <c>Dialog_WD_UpdateWindows.cs</c>.
    /// </summary>
    public class WorldComponent_WD_UpdatePopup : WorldComponent
    {
        public WorldComponent_WD_UpdatePopup(World world) : base(world)
        {
        }

        public override void FinalizeInit(bool fromLoad)
        {
            base.FinalizeInit(fromLoad);
            LongEventHandler.ExecuteWhenFinished(TryShowPopup);
        }

        private void TryShowPopup()
        {
            var s = WorldDominationMod.settings;
            if (s == null || !s.showUpdatePopups) return;

            string currentVersion = WD_UpdateEntries.Entries.Count > 0 ? WD_UpdateEntries.Entries[0].Version : string.Empty;
            if (string.IsNullOrEmpty(currentVersion)) return;
            if (currentVersion == s.lastSeenReleaseNotesVersion) return;

            if (Find.WindowStack.Windows.Any(w => w is Dialog_WD_UpdatePopup)) return;

            Find.WindowStack.Add(new Dialog_WD_UpdatePopup(currentVersion));
        }
    }
}
