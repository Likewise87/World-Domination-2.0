using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public class Dialog_WorldGenSettings : Window
    {
        private readonly string windowTitle;
        private bool weightsExpanded = true;

        public override Vector2 InitialSize => new Vector2(850f, 750f);

        public Dialog_WorldGenSettings()
        {
            doCloseButton = true;
            forcePause = true;
            closeOnClickedOutside = true;
            windowTitle = "TSA_WD_BtnWorldGen".Translate();
            optionalTitle = null;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Rect contentRect = SettingsUI.DrawWindowTitle(inRect, windowTitle);
            Listing_Standard l = new Listing_Standard();
            l.Begin(contentRect);
            var s = WorldDominationMod.settings;

            SettingsUI.DrawMenuTopBar(l, SettingsUI.ResetPageToDefaultsLabel, () => s.ResetWorldGen(),
                () => { weightsExpanded = true; },
                () => { weightsExpanded = false; });
            if (SettingsUI.DrawCollapsibleHeader(l, "TSA_WD_Gen_HeaderWeights".Translate(), ref weightsExpanded, SettingsUI.SectionHeaderColor))
            {
            l.Gap(10f);

            // Use the WeightSlider style for a clear visual representation of the probability
            float total = s.genWeightT1 + s.genWeightT2 + s.genWeightT3 + s.genWeightT4;

            // Tier 1 (Outposts)
            s.genWeightT1 = SettingsUI.WeightSlider(l,
                "TSA_WD_Tier1".Translate(),
                s.genWeightT1, total, 0f, 200f,
                "TSA_WD_Gen_Tier1Tip".Translate(), WorldDominationSettings.DefGenWeightT1);

            // Tier 2 (Towns)
            s.genWeightT2 = SettingsUI.WeightSlider(l,
                "TSA_WD_Tier2".Translate(),
                s.genWeightT2, total, 0f, 200f,
                "TSA_WD_Gen_Tier2Tip".Translate(), WorldDominationSettings.DefGenWeightT2);

            // Tier 3 (Fortresses)
            s.genWeightT3 = SettingsUI.WeightSlider(l,
                "TSA_WD_Tier3".Translate(),
                s.genWeightT3, total, 0f, 200f,
                "TSA_WD_Gen_Tier3Tip".Translate(), WorldDominationSettings.DefGenWeightT3);

            // Tier 4 (Citadels)
            s.genWeightT4 = SettingsUI.WeightSlider(l,
                "TSA_WD_Tier4".Translate(),
                s.genWeightT4, total, 0f, 200f,
                "TSA_WD_Gen_Tier4Tip".Translate(), WorldDominationSettings.DefGenWeightT4);
            }

            // --- REROLL SECTION ---
            if (Current.ProgramState == ProgramState.Playing)
            {
                l.Gap(10f);
                GUI.color = Color.red;
                if (l.ButtonText("TSA_WD_Gen_RerollAll".Translate()))
                {
                    // SURGICAL FIX: Wrap execution in a confirmation box
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        "TSA_WD_ConfirmReroll".Translate(),
                        () => WorldActions_Utils.RerollAllSettlements(),
                        destructive: true));
                }
                GUI.color = Color.white;
            }

            l.End();
        }
    }
}