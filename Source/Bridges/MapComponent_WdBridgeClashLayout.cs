using System.Collections.Generic;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Bridge deck cells + spawn ends for <see cref="GenStep_WD_BridgeClash"/> maps.
    /// <see cref="AngleDegrees"/> is the undirected local-map axis (0 = east, 90 = north),
    /// from globe tangent-plane heading snapped to nearest 22.5°.
    /// <see cref="AxisEndA"/> / <see cref="AxisEndB"/> are geometric deck ends (stable);
    /// <see cref="PlayerEnd"/> / <see cref="HostileEnd"/> are approach-polarity assignments.
    /// </summary>
    public class MapComponent_WdBridgeClashLayout : MapComponent
    {
        public float AngleDegrees = 0f;
        public HashSet<IntVec3> BridgeCells = new HashSet<IntVec3>();
        public IntVec3 AxisEndA = IntVec3.Invalid;
        public IntVec3 AxisEndB = IntVec3.Invalid;
        public IntVec3 PlayerEnd = IntVec3.Invalid;
        public IntVec3 HostileEnd = IntVec3.Invalid;

        public MapComponent_WdBridgeClashLayout(Map map) : base(map) { }

        public bool IsOnBridge(IntVec3 c) =>
            BridgeCells != null && BridgeCells.Contains(c);

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref AngleDegrees, "wdBridgeAngle", 0f);
            List<IntVec3> cells = null;
            if (Scribe.mode == LoadSaveMode.Saving && BridgeCells != null)
                cells = new List<IntVec3>(BridgeCells);
            Scribe_Collections.Look(ref cells, "wdBridgeCells", LookMode.Value);
            Scribe_Values.Look(ref AxisEndA, "wdBridgeAxisEndA");
            Scribe_Values.Look(ref AxisEndB, "wdBridgeAxisEndB");
            Scribe_Values.Look(ref PlayerEnd, "wdBridgePlayerEnd");
            Scribe_Values.Look(ref HostileEnd, "wdBridgeHostileEnd");
            if (Scribe.mode != LoadSaveMode.Saving)
            {
                BridgeCells = new HashSet<IntVec3>();
                if (cells != null)
                {
                    for (int i = 0; i < cells.Count; i++)
                    {
                        if (cells[i].IsValid)
                            BridgeCells.Add(cells[i]);
                    }
                }
            }
        }
    }
}
