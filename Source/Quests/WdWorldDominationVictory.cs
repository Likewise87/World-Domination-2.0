using RimWorld;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// World Domination victory helpers: leader check and Keep playing / See credits dialog.
    /// Progress is tracked by the active victory quest (<see cref="QuestPart_WdVictoryHold"/>).
    /// </summary>
    public static class WdWorldDominationVictory
    {
        public const int RequiredHoldDays = 15;

        public static GameComponent_WdWorldDominationVictoryQuest? Comp =>
            Current.Game?.GetComponent<GameComponent_WdWorldDominationVictoryQuest>();

        public static bool AlreadyWon => Comp?.alreadyWon ?? false;

        public static int HoldDaysStreak =>
            WdWorldDominationVictoryQuestHelper.FindActiveHoldPart()?.holdDaysStreak ?? 0;

        public static bool IsPlayerWorldLeader()
        {
            var stats = WorldStatsUtils.GetWorldPowerStats();
            if (stats?.FactionStats == null || stats.FactionStats.Count == 0)
                return false;
            Faction top = stats.FactionStats[0]?.faction;
            return top != null && top.IsPlayer;
        }

        public static void TryOpenVictoryDialog()
        {
            var c = Comp;
            if (c == null)
                return;
            if (c.victoryDialogOpen)
                return;

            c.alreadyWon = true;
            c.permanentlyDone = true;
            c.victoryDialogOpen = true;
            Find.WindowStack.Add(new Dialog_WdWorldDominationVictory());
        }

        public static void MarkWonAndCloseDialogPath()
        {
            var c = Comp;
            if (c == null) return;
            c.alreadyWon = true;
            c.permanentlyDone = true;
            c.victoryDialogOpen = false;
        }

        public static void KeepPlaying()
        {
            MarkWonAndCloseDialogPath();
            Find.LetterStack.ReceiveLetter(
                "TSA_WD_Victory_KeepPlayingLetterLabel".Translate(),
                "TSA_WD_Victory_KeepPlayingLetterText".Translate(),
                LetterDefOf.PositiveEvent);
        }

        public static void ShowCreditsThenContinue()
        {
            MarkWonAndCloseDialogPath();
            string intro = "TSA_WD_Victory_CreditsIntro".Translate();
            string ending = "TSA_WD_Victory_CreditsEnding".Translate();
            string text = GameVictoryUtility.MakeEndCredits(intro, ending, null, "GameOverColonistsEscaped", null);
            GameVictoryUtility.ShowCredits(text, SongDefOf.EndCreditsSong, exitToMainMenu: false, songStartDelay: 5f);
        }
    }

    /// <summary>Choice: keep playing or see end credits after holding world leadership.</summary>
    public class Dialog_WdWorldDominationVictory : Window
    {
        public Dialog_WdWorldDominationVictory()
        {
            doCloseButton = false;
            doCloseX = false;
            absorbInputAroundWindow = true;
            forcePause = true;
            closeOnClickedOutside = false;
            closeOnAccept = false;
            closeOnCancel = false;
        }

        public override Vector2 InitialSize => new Vector2(520f, 280f);

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 40f), "TSA_WD_Victory_DialogTitle".Translate());
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(0f, 48f, inRect.width, 100f), "TSA_WD_Victory_DialogBody".Translate(WdWorldDominationVictory.RequiredHoldDays));

            float btnW = (inRect.width - 12f) / 2f;
            float y = inRect.height - 40f;
            if (Widgets.ButtonText(new Rect(0f, y, btnW, 35f), "TSA_WD_Victory_KeepPlaying".Translate()))
            {
                WdWorldDominationVictory.KeepPlaying();
                Close();
            }
            if (Widgets.ButtonText(new Rect(btnW + 12f, y, btnW, 35f), "TSA_WD_Victory_SeeCredits".Translate()))
            {
                Close();
                WdWorldDominationVictory.ShowCreditsThenContinue();
            }
        }

        public override void PostClose()
        {
            base.PostClose();
            var c = WdWorldDominationVictory.Comp;
            if (c != null)
                c.victoryDialogOpen = false;
        }
    }
}
