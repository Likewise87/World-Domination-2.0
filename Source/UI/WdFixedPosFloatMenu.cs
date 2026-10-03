using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Float menu that opens at a given screen position instead of the cursor. Use when a menu
    /// rebuilds itself after a click (checkbox rows) so it stays where the player opened it.
    /// For the parent/child build cascade use <see cref="WdCascadingFloatMenu"/> instead.
    /// </summary>
    public class WdFixedPosFloatMenu : FloatMenu
    {
        private readonly Vector2? forcedPos;

        public WdFixedPosFloatMenu(List<FloatMenuOption> options, Vector2? forcedPos = null)
            : base(options)
        {
            this.forcedPos = forcedPos;
        }

        protected override void SetInitialSizeAndPosition()
        {
            if (!forcedPos.HasValue)
            {
                base.SetInitialSizeAndPosition();
                return;
            }

            Vector2 pos = forcedPos.Value;
            Vector2 size = InitialSize;
            if (pos.x + size.x > UI.screenWidth) pos.x = UI.screenWidth - size.x;
            if (pos.y + size.y > UI.screenHeight) pos.y = UI.screenHeight - size.y;
            if (pos.x < 0f) pos.x = 0f;
            if (pos.y < 0f) pos.y = 0f;
            windowRect = new Rect(pos.x, pos.y, size.x, size.y);
        }
    }
}
