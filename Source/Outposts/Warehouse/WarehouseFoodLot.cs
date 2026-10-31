using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// One dated slot of warehouse food. A ledger over <see cref="CompOutpostWarehouse.storedItems"/>:
    /// the rows there stay the source of truth for counts, and lots only add arrival day and
    /// refrigeration (see <see cref="CompOutpostWarehouse.ReconcileFoodLots"/>).
    /// </summary>
    public class WarehouseFoodLot : IExposable
    {
        public ThingDef def;
        public int count;
        /// <summary>Absolute day (<see cref="GenDate.DaysPassed"/>) of arrival; hours are ignored on purpose.</summary>
        public int arrivalDay;
        /// <summary>Refrigerated lots do not spoil while the fridge is powered.</summary>
        public bool refrigerated;

        public void ExposeData()
        {
            Scribe_Defs.Look(ref def, "def");
            Scribe_Values.Look(ref count, "count");
            Scribe_Values.Look(ref arrivalDay, "arrivalDay");
            Scribe_Values.Look(ref refrigerated, "refrigerated");
        }
    }
}
