using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Right-side alert while map reinforcements are inbound (replaces ticking ChoiceLetter spam).</summary>
    public class Alert_WDReinforcementsIncoming : Alert
    {
        private readonly List<GlobalTargetInfo> targets = new List<GlobalTargetInfo>();

        public Alert_WDReinforcementsIncoming()
        {
            defaultPriority = AlertPriority.High;
        }

        public override string GetLabel()
        {
            MapComponent_ReinforcementTimer timer = FindActiveTimer(out _);
            if (timer == null) return "TSA_WD_ReinforcementsIncoming_Label".Translate("?");
            return "TSA_WD_ReinforcementsIncoming_Label".Translate(timer.GetCountdownTimeString());
        }

        public override TaggedString GetExplanation()
        {
            MapComponent_ReinforcementTimer timer = FindActiveTimer(out Map map);
            if (timer == null) return "";
            string factionName = timer.ReinforcementFactionName;
            return "TSA_WD_ReinforcementsIncoming_Text".Translate(factionName, timer.GetCountdownTimeString());
        }

        public override AlertReport GetReport()
        {
            if (Current.ProgramState != ProgramState.Playing) return false;
            targets.Clear();
            var maps = Find.Maps;
            if (maps == null) return false;
            for (int i = 0; i < maps.Count; i++)
            {
                Map map = maps[i];
                var timer = map?.GetComponent<MapComponent_ReinforcementTimer>();
                if (timer == null || !timer.IsIncoming) continue;
                targets.Add(new GlobalTargetInfo(map.Tile));
            }
            if (targets.Count == 0) return false;
            return AlertReport.CulpritsAre(targets);
        }

        private static MapComponent_ReinforcementTimer FindActiveTimer(out Map map)
        {
            map = null;
            var maps = Find.Maps;
            if (maps == null) return null;
            for (int i = 0; i < maps.Count; i++)
            {
                Map m = maps[i];
                var timer = m?.GetComponent<MapComponent_ReinforcementTimer>();
                if (timer != null && timer.IsIncoming)
                {
                    map = m;
                    return timer;
                }
            }
            return null;
        }
    }
}
