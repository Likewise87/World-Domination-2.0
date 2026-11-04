using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Hover-help state for the 3-pane settings shell.
    /// Null / inactive when SettingsUI helpers are used outside the shell (World Setup, Debug).
    /// Float-menu hover is sticky across frames: FloatMenu draws after the shell, and
    /// Mouse.IsOver still hits widgets under the menu, so OfferFloat outranks Offer while set.
    /// </summary>
    public sealed class WdSettingsHelpState
    {
        public string Title;
        public string Body;
        public string DefaultLine;
        public bool Pinned;

        string floatTitle;
        string floatBody;

        public void Offer(string title, string body, string defaultLine = null)
        {
            if (title.NullOrEmpty() && body.NullOrEmpty()) return;
            Title = title ?? string.Empty;
            Body = body ?? string.Empty;
            DefaultLine = defaultLine;
            Pinned = true;
        }

        /// <summary>
        /// Help from a FloatMenuOption mouseover. Survives ActiveHelp=null and outranks
        /// widget Offers that fire under the open menu.
        /// </summary>
        public void OfferFloat(string title, string body)
        {
            if (title.NullOrEmpty() && body.NullOrEmpty()) return;
            floatTitle = title ?? string.Empty;
            floatBody = body ?? string.Empty;
        }

        public void ClearFloat()
        {
            floatTitle = null;
            floatBody = null;
        }

        public void Clear()
        {
            Title = null;
            Body = null;
            DefaultLine = null;
            Pinned = false;
            ClearFloat();
        }

        public void Draw(Rect rect)
        {
            string drawTitle = !floatTitle.NullOrEmpty() || !floatBody.NullOrEmpty() ? floatTitle : Title;
            string drawBody = !floatTitle.NullOrEmpty() || !floatBody.NullOrEmpty() ? floatBody : Body;
            string drawDefault = !floatTitle.NullOrEmpty() || !floatBody.NullOrEmpty() ? null : DefaultLine;

            // Top-aligned with the nav search field (no top inset); side/bottom padding only.
            Rect inner = new Rect(rect.x + 10f, rect.y, Mathf.Max(0f, rect.width - 20f), Mathf.Max(0f, rect.height - 10f));
            if (drawTitle.NullOrEmpty() && drawBody.NullOrEmpty())
            {
                Text.Font = GameFont.Small;
                GUI.color = new Color(1f, 1f, 1f, 0.45f);
                Widgets.Label(inner, "TSA_WD_Settings_HelpEmpty".Translate());
                GUI.color = Color.white;
                return;
            }

            float y = inner.y;
            if (!drawTitle.NullOrEmpty())
            {
                Text.Font = GameFont.Medium;
                float th = Text.CalcHeight(drawTitle, inner.width);
                Widgets.Label(new Rect(inner.x, y, inner.width, th), drawTitle);
                y += th + 6f;
            }

            Text.Font = GameFont.Small;
            if (!drawBody.NullOrEmpty())
            {
                float bh = Text.CalcHeight(drawBody, inner.width);
                Widgets.Label(new Rect(inner.x, y, inner.width, bh), drawBody);
                y += bh + 8f;
            }

            if (!drawDefault.NullOrEmpty())
            {
                GUI.color = new Color(1f, 1f, 1f, 0.55f);
                float dh = Text.CalcHeight(drawDefault, inner.width);
                Widgets.Label(new Rect(inner.x, y, inner.width, dh), drawDefault);
                GUI.color = Color.white;
            }
        }
    }
}
