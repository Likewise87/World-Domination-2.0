using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Relative share weight for a remote power spot (normalized among eligible peers).</summary>
    public class Dialog_WdRemotePowerSpotShare : Window
    {
        private readonly CompWdRemotePowerSpot spot;
        private float draftShare;

        private const float SliderH = 28f;
        private const float RowH = 24f;
        private const float BtnH = 28f;
        private const float BtnW = 120f;

        public override Vector2 InitialSize => new Vector2(460f, 320f);

        public Dialog_WdRemotePowerSpotShare(CompWdRemotePowerSpot spot)
        {
            this.spot = spot;
            draftShare = spot?.SharePercent ?? CompWdRemotePowerSpot.DefaultSharePercent;
            forcePause = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
            doCloseX = true;
            doCloseButton = false;
        }

        public static void Open(CompWdRemotePowerSpot spot)
        {
            if (spot?.parent == null || spot.parent.Destroyed) return;
            Find.WindowStack.Add(new Dialog_WdRemotePowerSpotShare(spot));
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (spot?.parent == null || spot.parent.Destroyed)
            {
                Close();
                return;
            }

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 30f), "TSA_WD_RemotePowerSpot_ShareTitle".Translate());
            Text.Font = GameFont.Small;

            float y = 40f;
            const float bodyH = 56f;
            Widgets.Label(new Rect(0f, y, inRect.width, bodyH), "TSA_WD_RemotePowerSpot_ShareBody".Translate());
            y += bodyH + 8f;

            Rect sliderRow = new Rect(0f, y, inRect.width, SliderH);
            Widgets.Label(sliderRow.LeftPart(0.42f), "TSA_WD_RemotePowerSpot_ShareSlider".Translate(draftShare.ToString("F0")));
            draftShare = Widgets.HorizontalSlider(
                sliderRow.RightPart(0.55f),
                draftShare,
                0f,
                100f,
                false,
                null,
                null,
                null,
                1f);
            y += SliderH + 10f;

            float preview = PreviewEffectiveFraction(draftShare);
            string effectivePct = preview >= 0f ? (preview * 100f).ToString("F0") : "0";
            Widgets.Label(new Rect(0f, y, inRect.width, RowH),
                "TSA_WD_RemotePowerSpot_ShareEffective".Translate(effectivePct));
            y += RowH + 6f;

            float totalWatts = Outpost_PowerPlant.GetTotalRemotePowerWatts();
            float effectiveWatts = preview >= 0f ? totalWatts * preview : 0f;
            Widgets.Label(new Rect(0f, y, inRect.width, RowH),
                "TSA_WD_RemotePowerSpot_ShareEffectiveWatts".Translate(
                    Outpost_PowerPlant.FormatWatts(effectiveWatts)));

            float btnY = inRect.height - BtnH;
            if (Widgets.ButtonText(new Rect(0f, btnY, BtnW, BtnH), "TSA_WD_OutpostRange_Reset".Translate()))
                draftShare = CompWdRemotePowerSpot.DefaultSharePercent;
            if (Widgets.ButtonText(new Rect(inRect.width - BtnW, btnY, BtnW, BtnH), "TSA_WD_OutpostRange_Confirm".Translate()))
            {
                spot.SetSharePercentAndDirty(draftShare);
                Close();
            }
        }

        private float PreviewEffectiveFraction(float draft)
        {
            draft = Mathf.Clamp(draft, 0f, 100f);
            Map map = spot.parent?.Map;
            if (map == null) return -1f;

            float sum = 0f;
            bool selfEligible = draft > 0f && spot.PowerNet != null
                && map.IsPlayerHome
                && !Outpost_EstablishmentRequirements.IsActiveCamp(map.Parent);
            if (selfEligible)
                sum += draft;

            ThingDef spotDef = Outpost_PowerPlant.ResolveRemotePowerSpotDef();
            var list = spotDef != null ? map.listerThings?.ThingsOfDef(spotDef) : null;
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    Thing t = list[i];
                    if (t == null || t.Destroyed || t == spot.parent) continue;
                    var other = t.TryGetComp<CompWdRemotePowerSpot>();
                    if (other == null || !other.IsEligible) continue;
                    sum += other.SharePercent;
                }
            }

            if (!selfEligible || sum <= 0f) return -1f;
            return draft / sum;
        }
    }
}
