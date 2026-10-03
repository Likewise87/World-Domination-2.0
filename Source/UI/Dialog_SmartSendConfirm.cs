using System;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Confirm step before Smart Send dispatches. States the global travel mode and drop-pod
    /// Total Cost on one line; the mode itself is chosen from the All Player Pawns toolbar.
    /// </summary>
    public class Dialog_SmartSendConfirm : Window
    {
        private readonly int launchCount;
        private readonly int pawnCount;
        private readonly Action onConfirm;

        public override Vector2 InitialSize => new Vector2(480f, 180f);

        public Dialog_SmartSendConfirm(int launchCount, int pawnCount, Action onConfirm)
        {
            this.launchCount = Mathf.Max(1, launchCount);
            this.pawnCount = Mathf.Max(0, pawnCount);
            this.onConfirm = onConfirm;
            doCloseX = true;
            closeOnCancel = true;
            absorbInputAroundWindow = true;
            forcePause = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (WdWindowEsc.TryCloseOnCancel(this))
                return;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 32f), "TSA_WD_PawnDropPod_SmartSendTitle".Translate());
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(0f, 34f, inRect.width, 24f),
                "TSA_WD_PawnTransfer_DialogSubtitle".Translate(pawnCount.ToString()));

            float y = 62f;
            y += PlayerPawnDropPodUtility.DrawModeReadoutWithTotalCost(
                new Rect(0f, y, inRect.width, PlayerPawnDropPodUtility.ModeIconSize),
                PlayerPawnDropPodUtility.AdHocViaDropPod,
                launchCount);

            y += 12f;
            if (Widgets.ButtonText(new Rect(0f, y, inRect.width * 0.48f, 32f), "Cancel".Translate()))
            {
                Close();
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
            if (Widgets.ButtonText(new Rect(inRect.width * 0.52f, y, inRect.width * 0.48f, 32f),
                    "TSA_WD_PawnDropPod_SmartSendConfirm".Translate()))
            {
                onConfirm?.Invoke();
                Close();
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
        }
    }
}
