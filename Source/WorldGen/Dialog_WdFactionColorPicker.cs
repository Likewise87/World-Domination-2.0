using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Vanilla color-picker chrome for Configure Scope faction recolor.</summary>
    public class Dialog_WdFactionColorPicker : Dialog_ColorPickerBase
    {
        private static readonly List<Color> EmptyPalette = new List<Color>();

        private readonly Faction faction;

        public Dialog_WdFactionColorPicker(Faction faction)
            // Match Dialog_AllowedAreaColorPicker: no RGB/HSV textfield rows (All overflows InitialSize 600x450).
            : base(Widgets.ColorComponents.None, Widgets.ColorComponents.None)
        {
            this.faction = faction;
            Color current = faction?.Color ?? Color.white;
            color = current;
            oldColor = current;
        }

        protected override bool ShowDarklight => false;

        protected override Color DefaultColor => WdFactionColorUtil.SpectrumColor(faction);

        protected override List<Color> PickableColors => EmptyPalette;

        protected override float ForcedColorValue => 0.5f;

        protected override bool ShowColorTemperatureBar => false;

        protected override void SaveColor(Color col)
        {
            if (faction == null) return;
            WdFactionColorUtil.Apply(faction, col);
        }
    }
}
