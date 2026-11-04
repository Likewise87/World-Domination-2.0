using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public sealed class WdSettingsSearchHit
    {
        public WdSettingsPage Page;
        public string Label;
        public string Tooltip;
        public string Section;
        public int Score;
    }

    /// <summary>Global settings search: collect-mode walk + token scoring.</summary>
    public static class WdSettingsSearch
    {
        public static readonly List<WdSettingsSearchHit> Registry = new List<WdSettingsSearchHit>(256);
        public static WdSettingsPage CollectPage;
        public static string CollectSection;
        public static bool IsCollecting;

        public static void BeginCollect(WdSettingsPage page)
        {
            IsCollecting = true;
            CollectPage = page;
            CollectSection = null;
        }

        public static void EndCollect()
        {
            IsCollecting = false;
            CollectSection = null;
        }

        public static void ClearRegistry()
        {
            Registry.Clear();
        }

        public static void Register(string label, string tooltip = null)
        {
            if (!IsCollecting) return;
            if (label.NullOrEmpty()) return;
            Registry.Add(new WdSettingsSearchHit
            {
                Page = CollectPage,
                Label = label,
                Tooltip = tooltip,
                Section = CollectSection,
                Score = 0
            });
        }

        public static void RebuildAll()
        {
            ClearRegistry();
            Rect dummy = new Rect(0f, 0f, 800f, 4000f);
            string prevFilter = SettingsUI.settingsSearchFilter;
            SettingsUI.settingsSearchFilter = null;
            try
            {
                foreach (WdSettingsPageDef page in WdSettingsPages.All)
                {
                    if (page.Id == WdSettingsPage.Overview) continue;
                    BeginCollect(page.Id);
                    try
                    {
                        page.Draw?.Invoke(dummy);
                    }
                    catch (Exception e)
                    {
                        Log.Warning($"[WD] Settings search collect failed for {page.Id}: {e.Message}");
                    }
                    EndCollect();
                }
            }
            finally
            {
                SettingsUI.settingsSearchFilter = prevFilter;
                IsCollecting = false;
            }
        }

        public static List<WdSettingsSearchHit> Query(string raw, int maxResults = 40)
        {
            var results = new List<WdSettingsSearchHit>();
            if (raw.NullOrEmpty()) return results;
            string q = raw.Trim().ToLowerInvariant();
            if (q.Length == 0) return results;

            string[] tokens = q.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < Registry.Count; i++)
            {
                WdSettingsSearchHit hit = Registry[i];
                int score = Score(hit, q, tokens);
                if (score <= 0) continue;
                hit.Score = score;
                results.Add(hit);
            }
            results.Sort((a, b) => b.Score.CompareTo(a.Score));
            if (results.Count > maxResults)
                results.RemoveRange(maxResults, results.Count - maxResults);
            return results;
        }

        private static int Score(WdSettingsSearchHit hit, string q, string[] tokens)
        {
            string label = (hit.Label ?? string.Empty).ToLowerInvariant();
            string tip = (hit.Tooltip ?? string.Empty).ToLowerInvariant();
            string section = (hit.Section ?? string.Empty).ToLowerInvariant();
            string pageLabel = WdSettingsPages.Get(hit.Page).LabelKey.Translate().ToString().ToLowerInvariant();
            string hay = label + " " + tip + " " + section + " " + pageLabel;

            int score = 0;
            if (label == q) score += 100;
            else if (label.StartsWith(q)) score += 70;
            else if (label.Contains(q)) score += 45;
            else if (hay.Contains(q)) score += 25;

            for (int t = 0; t < tokens.Length; t++)
            {
                string tok = tokens[t];
                if (label.Contains(tok)) score += 12;
                else if (hay.Contains(tok)) score += 5;
                else return 0;
            }

            // Light fuzzy: consecutive char subsequence in label
            if (score == 0 && FuzzyContains(label, q)) score = 8;
            return score;
        }

        private static bool FuzzyContains(string hay, string needle)
        {
            if (hay.NullOrEmpty() || needle.NullOrEmpty()) return false;
            int j = 0;
            for (int i = 0; i < hay.Length && j < needle.Length; i++)
            {
                if (hay[i] == needle[j]) j++;
            }
            return j == needle.Length;
        }

        public static float DrawResults(
            Rect rect,
            List<WdSettingsSearchHit> hits,
            ref Vector2 scroll,
            Action<WdSettingsSearchHit> onJump)
        {
            if (hits == null || hits.Count == 0)
            {
                Text.Font = GameFont.Small;
                GUI.color = new Color(1f, 1f, 1f, 0.5f);
                Widgets.Label(rect, "TSA_WD_Settings_SearchNoResults".Translate());
                GUI.color = Color.white;
                return 0f;
            }

            float rowH = 36f;
            float viewH = hits.Count * rowH + 8f;
            Rect view = new Rect(0f, 0f, SettingsUI.ScrollContentWidth(rect.width), viewH);
            Widgets.BeginScrollView(rect, ref scroll, view);
            float y = 0f;
            for (int i = 0; i < hits.Count; i++)
            {
                WdSettingsSearchHit hit = hits[i];
                Rect row = new Rect(0f, y, view.width, rowH - 2f);
                Widgets.DrawHighlightIfMouseover(row);
                if (Widgets.ButtonInvisible(row))
                    onJump?.Invoke(hit);

                Texture2D icon = WdSettingsPages.LoadIcon(WdSettingsPages.Get(hit.Page).IconPath);
                if (icon != null)
                    Widgets.DrawTextureFitted(new Rect(row.x + 4f, row.y + 6f, 22f, 22f), icon, 1f);

                Text.Font = GameFont.Small;
                string pageName = WdSettingsPages.Get(hit.Page).LabelKey.Translate();
                string line = hit.Label;
                if (!hit.Section.NullOrEmpty())
                    line = hit.Section + " · " + line;
                Widgets.Label(new Rect(row.x + 32f, row.y + 2f, row.width - 36f, 18f), line);
                GUI.color = new Color(1f, 1f, 1f, 0.45f);
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(row.x + 32f, row.y + 18f, row.width - 36f, 14f), pageName);
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                y += rowH;
            }
            Widgets.EndScrollView();
            return viewH;
        }
    }
}
