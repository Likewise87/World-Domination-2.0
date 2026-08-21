using RimWorld;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Right-side alert while silver upkeep is scheduled and Have &gt;= Need.
    /// </summary>
    public class Alert_WDOutpostUpkeepDue : Alert
    {
        public Alert_WDOutpostUpkeepDue()
        {
            defaultPriority = AlertPriority.Medium;
        }

        public override string GetLabel()
        {
            if (!TryRead(out int days, out int need, out int have, out _, out _))
                return "TSA_WD_Upkeep_AlertLabel".Translate(0, 0, 0);
            // {0}=days, {1}=need, {2}=have
            return "TSA_WD_Upkeep_AlertLabel".Translate(days, need, have);
        }

        public override TaggedString GetExplanation()
        {
            if (!TryRead(out _, out int need, out int have, out int occupants, out int leavers))
                return "";
            return "TSA_WD_Upkeep_AlertTip".Translate(occupants, need, have, leavers);
        }

        public override AlertReport GetReport()
        {
            if (Current.ProgramState != ProgramState.Playing) return false;
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (!EscalationOutpostUpkeep.TryGetAlertState(manager, out _, out int need, out int have, out _, out _))
                return false;
            if (have < need) return false; // critical alert owns the short case
            return AlertReport.Active;
        }

        private static bool TryRead(out int days, out int need, out int have, out int occupants, out int leavers)
        {
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            return EscalationOutpostUpkeep.TryGetAlertState(manager, out days, out need, out have, out occupants, out leavers);
        }
    }

    /// <summary>
    /// Pulsing red alert while silver upkeep is scheduled and Have &lt; Need.
    /// </summary>
    public class Alert_WDOutpostUpkeepCritical : Alert_Critical
    {
        public override string GetLabel()
        {
            if (!TryRead(out int days, out int need, out int have, out _, out _))
                return "TSA_WD_Upkeep_AlertLabel".Translate(0, 0, 0);
            // {0}=days, {1}=need, {2}=have
            return "TSA_WD_Upkeep_AlertLabel".Translate(days, need, have);
        }

        public override TaggedString GetExplanation()
        {
            if (!TryRead(out _, out int need, out int have, out int occupants, out int leavers))
                return "";
            return "TSA_WD_Upkeep_AlertTip".Translate(occupants, need, have, leavers);
        }

        public override AlertReport GetReport()
        {
            if (Current.ProgramState != ProgramState.Playing) return false;
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            if (!EscalationOutpostUpkeep.TryGetAlertState(manager, out _, out int need, out int have, out _, out _))
                return false;
            if (have >= need) return false;
            return AlertReport.Active;
        }

        private static bool TryRead(out int days, out int need, out int have, out int occupants, out int leavers)
        {
            var manager = Find.World?.GetComponent<WorldComponent_SpreadManager>();
            return EscalationOutpostUpkeep.TryGetAlertState(manager, out days, out need, out have, out occupants, out leavers);
        }
    }
}
