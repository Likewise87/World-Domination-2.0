using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    public class WD_UpdateEntry
    {
        public string Version;
        public string TitleKey;
        public string BodyKey;
        public string ReleaseDate; // localized-friendly string
    }

    public static class WD_UpdateEntries
    {
        // Newest first
        public static readonly List<WD_UpdateEntry> Entries = new List<WD_UpdateEntry>
        {
            new WD_UpdateEntry
            {
                Version = "2.3.11",
                TitleKey = "TSA_WD_Update_2_3_11_Title",
                BodyKey = "TSA_WD_Update_2_3_11_Body",
                ReleaseDate = "August 19th 2026"
            },
            new WD_UpdateEntry
            {
                Version = "2.3.10",
                TitleKey = "TSA_WD_Update_2_3_10_Title",
                BodyKey = "TSA_WD_Update_2_3_10_Body",
                ReleaseDate = "August 18th 2026"
            },
            new WD_UpdateEntry
            {
                Version = "2.3.09",
                TitleKey = "TSA_WD_Update_2_3_09_Title",
                BodyKey = "TSA_WD_Update_2_3_09_Body",
                ReleaseDate = "August 17th 2026"
            },
            new WD_UpdateEntry
            {
                Version = "2.3.07",
                TitleKey = "TSA_WD_Update_2_3_07_Title",
                BodyKey = "TSA_WD_Update_2_3_07_Body",
                ReleaseDate = "August 16th 2026"
            },
            new WD_UpdateEntry
            {
                Version = "2.3.06",
                TitleKey = "TSA_WD_Update_2_3_06_Title",
                BodyKey = "TSA_WD_Update_2_3_06_Body",
                ReleaseDate = "August 15th 2026"
            },
            new WD_UpdateEntry
            {
                Version = "2.3.05",
                TitleKey = "TSA_WD_Update_2_3_05_Title",
                BodyKey = "TSA_WD_Update_2_3_05_Body",
                ReleaseDate = "August 14th 2026"
            },
            new WD_UpdateEntry
            {
                Version = "2.3.0",
                TitleKey = "TSA_WD_Update_2_3_0_Title",
                BodyKey = "TSA_WD_Update_2_3_0_Body",
                ReleaseDate = "August 13th 2026"
            },
            new WD_UpdateEntry
            {
                Version = "2.2.68",
                TitleKey = "TSA_WD_Update_2_2_68_Title",
                BodyKey = "TSA_WD_Update_2_2_68_Body",
                ReleaseDate = "August 9th 2026"
            },
            new WD_UpdateEntry
            {
                Version = "2.2.67",
                TitleKey = "TSA_WD_Update_2_2_67_Title",
                BodyKey = "TSA_WD_Update_2_2_67_Body",
                ReleaseDate = "August 8th 2026"
            },
            new WD_UpdateEntry
            {
                Version = "2.2.65",
                TitleKey = "TSA_WD_Update_2_2_65_Title",
                BodyKey = "TSA_WD_Update_2_2_65_Body",
                ReleaseDate = "August 6th 2026"
            },
            new WD_UpdateEntry
            {
                Version = "2.2.6",
                TitleKey = "TSA_WD_Update_2_2_6_Title",
                BodyKey = "TSA_WD_Update_2_2_6_Body",
                ReleaseDate = "August 4th 2026"
            },
            new WD_UpdateEntry
            {
                Version = "2.2.5",
                TitleKey = "TSA_WD_Update_2_2_5_Title",
                BodyKey = "TSA_WD_Update_2_2_5_Body",
                ReleaseDate = "August 3rd 2026"
            },
            new WD_UpdateEntry
            {
                Version = "2.2.45",
                TitleKey = "TSA_WD_Update_2_2_45_Title",
                BodyKey = "TSA_WD_Update_2_2_45_Body",
                ReleaseDate = "August 1st 2026"
            },
            new WD_UpdateEntry
            {
                Version = "2.2.4",
                TitleKey = "TSA_WD_Update_2_2_4_Title",
                BodyKey = "TSA_WD_Update_2_2_4_Body",
                ReleaseDate = "July 31st 2026"
            },
            new WD_UpdateEntry
            {
                Version = "2.2.3",
                TitleKey = "TSA_WD_Update_2_2_3_Title",
                BodyKey = "TSA_WD_Update_2_2_3_Body",
                ReleaseDate = "July 30th 2026"
            },
            new WD_UpdateEntry
            {
                Version = "2.2.2",
                TitleKey = "TSA_WD_Update_2_2_2_Title",
                BodyKey = "TSA_WD_Update_2_2_2_Body",
                ReleaseDate = "July 29th 2026"
            },
            new WD_UpdateEntry
            {
                Version = "2.2.1",
                TitleKey = "TSA_WD_Update_2_2_1_Title",
                BodyKey = "TSA_WD_Update_2_2_1_Body",
                ReleaseDate = "July 28th 2026"
            },
            new WD_UpdateEntry
            {
                Version = "2.2",
                TitleKey = "TSA_WD_Update_2_2_Title",
                BodyKey = "TSA_WD_Update_2_2_Body",
                ReleaseDate = "July 27th 2026"
            },
            new WD_UpdateEntry
            {
                Version = "2.1.2",
                TitleKey = "TSA_WD_Update_2_1_2_Title",
                BodyKey = "TSA_WD_Update_2_1_2_Body",
                ReleaseDate = "July 26th 2026"
            },
            new WD_UpdateEntry
            {
                Version = "2.1.1",
                TitleKey = "TSA_WD_Update_2_1_1_Title",
                BodyKey = "TSA_WD_Update_2_1_1_Body",
                ReleaseDate = "July 26th 2026"
            },
            new WD_UpdateEntry
            {
                Version = "2.1",
                TitleKey = "TSA_WD_Update_2_1_Title",
                BodyKey = "TSA_WD_Update_2_1_Body",
                ReleaseDate = "July 25th 2026"
            },
            new WD_UpdateEntry
            {
                Version = "2.03",
                TitleKey = "TSA_WD_Update_2_03_Title",
                BodyKey = "TSA_WD_Update_2_03_Body",
                ReleaseDate = "July 21st 2026"
            },
            new WD_UpdateEntry
            {
                Version = "1.98",
                TitleKey = "TSA_WD_Update_1_98_Title",
                BodyKey = "TSA_WD_Update_1_98_Body",
                ReleaseDate = "July 20th 2026"
            },
            new WD_UpdateEntry
            {
                Version = "1.97",
                TitleKey = "TSA_WD_Update_1_97_Title",
                BodyKey = "TSA_WD_Update_1_97_Body",
                ReleaseDate = "July 19th 2026"
            },
            new WD_UpdateEntry
            {
                Version = "1.96",
                TitleKey = "TSA_WD_Update_1_96_Title",
                BodyKey = "TSA_WD_Update_1_96_Body",
                ReleaseDate = "July 18th 2026"
            },
            new WD_UpdateEntry
            {
                Version = "1.95",
                TitleKey = "TSA_WD_Update_1_95_Title",
                BodyKey = "TSA_WD_Update_1_95_Body",
                ReleaseDate = "July 17th 2026"
            },
            new WD_UpdateEntry
            {
                Version = "1.94",
                TitleKey = "TSA_WD_Update_1_94_Title",
                BodyKey = "TSA_WD_Update_1_94_Body",
                ReleaseDate = "July 15th 2026"
            },
            new WD_UpdateEntry
            {
                Version = "1.93",
                TitleKey = "TSA_WD_Update_1_93_Title",
                BodyKey = "TSA_WD_Update_1_93_Body",
                ReleaseDate = "July 14th 2026"
            },
            new WD_UpdateEntry
            {
                Version = "1.92",
                TitleKey = "TSA_WD_Update_1_92_Title",
                BodyKey = "TSA_WD_Update_1_92_Body",
                ReleaseDate = "July 13th 2026"
            },
            new WD_UpdateEntry
            {
                Version = "1.91",
                TitleKey = "TSA_WD_Update_1_91_Title",
                BodyKey = "TSA_WD_Update_1_91_Body",
                ReleaseDate = "July 12th 2026"
            },
            new WD_UpdateEntry
            {
                Version = "1.90",
                TitleKey = "TSA_WD_Update_1_90_Title",
                BodyKey = "TSA_WD_Update_1_90_Body",
                ReleaseDate = "July 11th 2026"
            },
            new WD_UpdateEntry
            {
                Version = "1.80",
                TitleKey = "TSA_WD_Update_1_80_Title",
                BodyKey = "TSA_WD_Update_1_80_Body",
                ReleaseDate = "July 7th 2026"
            },
            new WD_UpdateEntry
            {
                Version = "1.71",
                TitleKey = "TSA_WD_Update_1_71_Title",
                BodyKey = "TSA_WD_Update_1_71_Body",
                ReleaseDate = "July 5th 2026"
            },
            new WD_UpdateEntry
            {
                Version = "1.70",
                TitleKey = "TSA_WD_Update_1_70_Title",
                BodyKey = "TSA_WD_Update_1_70_Body",
                ReleaseDate = "July 4th 2026"
            },
            new WD_UpdateEntry
            {
                Version = "1.60",
                TitleKey = "TSA_WD_Update_1_60_Title",
                BodyKey = "TSA_WD_Update_1_60_Body",
                ReleaseDate = "July 3rd 2026"
            },
            new WD_UpdateEntry
            {
                Version = "1.50",
                TitleKey = "TSA_WD_Update_1_50_Title",
                BodyKey = "TSA_WD_Update_1_50_Body",
                ReleaseDate = "June 29th 2026"
            },
            new WD_UpdateEntry
            {
                Version = "1.40",
                TitleKey = "TSA_WD_Update_1_40_Title",
                BodyKey = "TSA_WD_Update_1_40_Body",
                ReleaseDate = "June 28th 2026"
            },
            new WD_UpdateEntry
            {
                Version = "1.30",
                TitleKey = "TSA_WD_Update_1_30_Title",
                BodyKey = "TSA_WD_Update_1_30_Body",
                ReleaseDate = "June 27th 2026"
            },
            new WD_UpdateEntry
            {
                Version = "1.20",
                TitleKey = "TSA_WD_Update_1_20_Title",
                BodyKey = "TSA_WD_Update_1_20_Body",
                ReleaseDate = "June 24th 2026"
            },
            new WD_UpdateEntry
            {
                Version = "1.10",
                TitleKey = "TSA_WD_Update_1_10_Title",
                BodyKey = "TSA_WD_Update_1_10_Body",
                ReleaseDate = "June 23rd 2026"
            },
            new WD_UpdateEntry
            {
                Version = "1.0.0",
                TitleKey = "TSA_WD_Update_1_0_0_Title",
                BodyKey = "TSA_WD_Update_1_0_0_Body",
                ReleaseDate = "June 21st 2026"
            },
            new WD_UpdateEntry
            {
                Version = "0.9.110",
                TitleKey = "TSA_WD_Update_0_9_110_Title",
                BodyKey = "TSA_WD_Update_0_9_110_Body",
                ReleaseDate = "June 19th 2026"
            },
            new WD_UpdateEntry
            {
                Version = "0.9.100",
                TitleKey = "TSA_WD_Update_0_9_100_Title",
                BodyKey = "TSA_WD_Update_0_9_100_Body",
                ReleaseDate = "June 18th 2026"
            },
            new WD_UpdateEntry
            {
                Version = "0.9.90",
                TitleKey = "TSA_WD_Update_0_9_90_Title",
                BodyKey = "TSA_WD_Update_0_9_90_Body",
                ReleaseDate = "June 16th 2026"
            },
            new WD_UpdateEntry
            {
                Version = "0.9.80",
                TitleKey = "TSA_WD_Update_0_9_80_Title",
                BodyKey = "TSA_WD_Update_0_9_80_Body",
                ReleaseDate = "June 15th 2026"
            },
            new WD_UpdateEntry
            {
                Version = "0.9.70",
                TitleKey = "TSA_WD_Update_0_9_70_Title",
                BodyKey = "TSA_WD_Update_0_9_70_Body",
                ReleaseDate = "June 10th 2026"
            },
            new WD_UpdateEntry
            {
                Version = "0.9.60",
                TitleKey = "TSA_WD_Update_0_9_60_Title",
                BodyKey = "TSA_WD_Update_0_9_60_Body",
                ReleaseDate = "April 25th 2026"
            },
            new WD_UpdateEntry
            {
                Version = "0.9.50",
                TitleKey = "TSA_WD_Update_0_9_50_Title",
                BodyKey = "TSA_WD_Update_0_9_50_Body",
                ReleaseDate = "April 18th 2026"
            },
            new WD_UpdateEntry
            {
                Version = "0.9.40",
                TitleKey = "TSA_WD_Update_0_9_40_Title",
                BodyKey = "TSA_WD_Update_0_9_40_Body",
                ReleaseDate = "April 10th 2026"
            },
            new WD_UpdateEntry
            {
                Version = "0.9.30",
                TitleKey = "TSA_WD_Update_0_9_30_Title",
                BodyKey = "TSA_WD_Update_0_9_30_Body",
                ReleaseDate = "April 9th 2026"
            },
            new WD_UpdateEntry
            {
                Version = "0.9.20",
                TitleKey = "TSA_WD_Update_0_9_20_Title",
                BodyKey = "TSA_WD_Update_0_9_20_Body",
                ReleaseDate = "April 8th 2026"
            },
            new WD_UpdateEntry
            {
                Version = "0.9.10",
                TitleKey = "TSA_WD_Update_0_9_10_Title",
                BodyKey = "TSA_WD_Update_0_9_10_Body",
                ReleaseDate = "April 6th 2026"
            },
            new WD_UpdateEntry
            {
                Version = "0.9.9",
                TitleKey = "TSA_WD_Update_0_9_9_Title",
                BodyKey = "TSA_WD_Update_0_9_9_Body",
                ReleaseDate = "April 3rd 2026"
            },
            new WD_UpdateEntry
            {
                Version = "0.9.8",
                TitleKey = "TSA_WD_Update_0_9_8_Title",
                BodyKey = "TSA_WD_Update_0_9_8_Body",
                ReleaseDate = "March 27th 2026"
            },
            new WD_UpdateEntry
            {
                Version = "0.9.7",
                TitleKey = "TSA_WD_Update_0_9_7_Title",
                BodyKey = "TSA_WD_Update_0_9_7_Body",
                ReleaseDate = "March 23rd 2026"
            },
            new WD_UpdateEntry
            {
                Version = "0.9.6",
                TitleKey = "TSA_WD_Update_0_9_6_Title",
                BodyKey = "TSA_WD_Update_0_9_6_Body",
                ReleaseDate = "March 22nd 2026"
            },
            new WD_UpdateEntry
            {
                Version = "0.9.5",
                TitleKey = "TSA_WD_Update_0_9_5_Title",
                BodyKey = "TSA_WD_Update_0_9_5_Body",
                ReleaseDate = "March 21st 2026"
            },
            new WD_UpdateEntry
            {
                Version = "0.9.4",
                TitleKey = "TSA_WD_Update_0_9_4_Title",
                BodyKey = "TSA_WD_Update_0_9_4_Body",
                ReleaseDate = "March 20th 2026"
            },
            new WD_UpdateEntry
            {
                Version = "0.9.3",
                TitleKey = "TSA_WD_Update_0_9_3_Title",
                BodyKey = "TSA_WD_Update_0_9_3_Body",
                ReleaseDate = "March 18th 2026"
            },
            new WD_UpdateEntry
            {
                Version = "0.9.2",
                TitleKey = "TSA_WD_Update_0_9_2_Title",
                BodyKey = "TSA_WD_Update_0_9_2_Body",
                ReleaseDate = "March 16th 2026"
            },
            new WD_UpdateEntry
            {
                Version = "0.9.1",
                TitleKey = "TSA_WD_Update_0_9_1_Title",
                BodyKey = "TSA_WD_Update_0_9_1_Body",
                ReleaseDate = "March 16th 2026"
            },
            new WD_UpdateEntry
            {
                Version = "0.9.0",
                TitleKey = "TSA_WD_Update_0_9_0_Title",
                BodyKey = "TSA_WD_Update_0_9_0_Body",
                ReleaseDate = "March 15th 2026"
            }
        };

        public static int Count => Entries.Count;

        public static int IndexForVersion(string version)
        {
            if (string.IsNullOrEmpty(version)) return 0;
            for (int i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].Version == version) return i;
            }
            return 0;
        }
    }

    public class Dialog_WD_UpdateLog : Window
    {
        private Vector2 scrollPos;
        private Vector2 entryListScrollPos;
        private int index;
        private int cachedIndex = -1;
        private string cachedHeader;
        private string cachedDateLabel;
        private string[] cachedLines;

        private static readonly Color UpdateLogDateColor = new Color(0.65f, 0.85f, 1f);

        public override Vector2 InitialSize => new Vector2(1080f, 600f);

        public Dialog_WD_UpdateLog(string startVersion = null)
        {
            forcePause = true;
            doCloseButton = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;

            index = WD_UpdateEntries.IndexForVersion(startVersion);
        }

        private void RebuildEntryCache()
        {
            if (cachedIndex == index) return;
            cachedIndex = index;
            var entry = WD_UpdateEntries.Entries[index];
            cachedHeader = "TSA_WD_UpdateLog_Title".Translate(entry.Version, entry.TitleKey.Translate());
            cachedDateLabel = "TSA_WD_UpdateLog_ReleaseDate".Translate(entry.ReleaseDate ?? entry.Version);
            string body = entry.BodyKey.Translate();
            string[] allLines = body.Split('\n');
            int startIdx = 0;
            for (int i = 0; i < allLines.Length; i++)
            {
                string l = allLines[i].TrimEnd('\r');
                if (l.StartsWith("----------------"))
                {
                    startIdx = i + 1;
                    break;
                }
            }
            if (startIdx < 0 || startIdx >= allLines.Length) startIdx = 0;
            var processed = new List<string>();
            for (int i = startIdx; i < allLines.Length; i++)
            {
                string line = allLines[i].TrimEnd('\r');
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.StartsWith("• "))
                    line = line.Substring(2).TrimStart();
                else if (line.StartsWith("- "))
                    line = line.Substring(2).TrimStart();
                processed.Add(line);
            }
            cachedLines = processed.ToArray();
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (WD_UpdateEntries.Count == 0)
            {
                Widgets.Label(inRect, "No updates defined.");
                return;
            }

            RebuildEntryCache();

            // Vanilla draws doCloseButton at inRect.height - 55f; keep content above that row.
            const float closeRowReserve = 55f;
            Rect contentRect = new Rect(inRect.x, inRect.y, inRect.width, inRect.height - closeRowReserve);

            const float rightColW = 340f;
            const float colGap = 12f;
            Rect leftRect = new Rect(contentRect.x, contentRect.y, contentRect.width - rightColW - colGap, contentRect.height);
            Rect rightRect = new Rect(leftRect.xMax + colGap, contentRect.y, rightColW, contentRect.height);

            float sepX = leftRect.xMax + colGap * 0.5f;
            Widgets.DrawLineVertical(sepX, contentRect.y, contentRect.height);

            Text.Font = GameFont.Medium;
            Rect headerRect = leftRect.TopPartPixels(32f);
            Widgets.Label(headerRect, cachedHeader);

            Text.Font = GameFont.Small;
            Rect dateRect = new Rect(leftRect.x, headerRect.yMax + 4f, leftRect.width, 22f);
            GUI.color = UpdateLogDateColor;
            Widgets.Label(dateRect, cachedDateLabel);
            GUI.color = Color.white;

            Rect sepRect = new Rect(leftRect.x, dateRect.yMax + 6f, leftRect.width, 2f);
            Widgets.DrawLineHorizontal(sepRect.x, sepRect.y, sepRect.width);

            Rect mainRect = new Rect(leftRect.x, sepRect.y + 8f, leftRect.width, leftRect.height - (sepRect.y + 12f - leftRect.y));
            float viewWidth = mainRect.width - 16f;

            Text.Font = GameFont.Small;
            string[] lines = cachedLines;

            // Extra bottom pad so the last line's descenders are not clipped when scrolled to the end.
            const float linePad = 10f;
            const float scrollBottomPad = 14f;
            float curY = 0f;
            for (int i = 0; i < lines.Length; i++)
            {
                float textHeight = Text.CalcHeight(lines[i], viewWidth - 10f);
                curY += textHeight + linePad;
            }

            Rect viewRect = new Rect(0f, 0f, viewWidth, curY + scrollBottomPad);
            Widgets.BeginScrollView(mainRect, ref scrollPos, viewRect);

            curY = 0f;
            bool zebra = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                float textHeight = Text.CalcHeight(line, viewWidth - 10f);
                float lineHeight = textHeight + linePad;

                Rect lineRect = new Rect(0f, curY, viewWidth, lineHeight);

                zebra = !zebra;
                if (zebra)
                {
                    Color prev = GUI.color;
                    GUI.color = new Color(1f, 1f, 1f, 0.04f);
                    GUI.DrawTexture(lineRect, BaseContent.WhiteTex);
                    GUI.color = prev;
                }

                Rect textRect = new Rect(lineRect.x + 5f, lineRect.y + 5f, lineRect.width - 10f, textHeight);
                Widgets.Label(textRect, line);

                curY += lineHeight;
            }

            Widgets.EndScrollView();

            DrawEntryListColumn(rightRect);
        }

        private void DrawEntryListColumn(Rect rightRect)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.75f, 0.82f, 1f);
            Widgets.Label(new Rect(rightRect.x, rightRect.y, rightRect.width, 18f), "TSA_WD_UpdateLog_EntryListHeader".Translate());
            GUI.color = Color.white;

            const float entryH = 54f;
            const float entryGap = 4f;
            Rect listRect = new Rect(rightRect.x, rightRect.y + 22f, rightRect.width, rightRect.height - 22f);
            float contentH = WD_UpdateEntries.Count * (entryH + entryGap);
            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, contentH);
            Widgets.BeginScrollView(listRect, ref entryListScrollPos, viewRect);

            float y = 0f;
            for (int i = 0; i < WD_UpdateEntries.Count; i++)
            {
                var e = WD_UpdateEntries.Entries[i];
                Rect rowRect = new Rect(0f, y, viewRect.width, entryH);
                bool selected = i == index;
                if (selected)
                    Widgets.DrawBoxSolid(rowRect, new Color(0.2f, 0.45f, 0.25f, 0.35f));
                else if (Mouse.IsOver(rowRect))
                    Widgets.DrawHighlight(rowRect);

                string title = e.TitleKey.Translate().ToString();
                if (title == e.TitleKey || title.Contains("TSA_WD_"))
                    title = "Update " + e.Version;

                Text.Font = GameFont.Tiny;
                GUI.color = selected ? Color.white : new Color(0.9f, 0.9f, 0.9f);
                Widgets.Label(new Rect(rowRect.x + 6f, rowRect.y + 3f, rowRect.width - 12f, 16f), e.Version);
                GUI.color = selected ? new Color(0.92f, 0.92f, 0.92f) : new Color(0.8f, 0.8f, 0.8f);
                Widgets.Label(new Rect(rowRect.x + 6f, rowRect.y + 18f, rowRect.width - 12f, 16f), title);
                GUI.color = UpdateLogDateColor;
                Widgets.Label(new Rect(rowRect.x + 6f, rowRect.y + 33f, rowRect.width - 12f, 16f), e.ReleaseDate ?? e.Version);
                GUI.color = Color.white;

                if (Widgets.ButtonInvisible(rowRect))
                {
                    index = i;
                    cachedIndex = -1;
                    scrollPos = Vector2.zero;
                }

                y += entryH + entryGap;
            }

            Widgets.EndScrollView();
        }
    }

    public class Dialog_WD_UpdatePopup : Window
    {
        private readonly string version;

        public override Vector2 InitialSize => new Vector2(500f, 220f);

        public Dialog_WD_UpdatePopup(string version)
        {
            this.version = version ?? "";
            forcePause = true;
            doCloseButton = false;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = true;
        }

        public override void PreClose()
        {
            base.PreClose();
            var s = WorldDominationMod.settings;
            if (s != null && !string.IsNullOrEmpty(version))
            {
                s.lastSeenReleaseNotesVersion = version;
                WorldDominationMod.SaveSettingsToDisk();
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;
            string text = "TSA_WD_ModUpdated".Translate(version);
            Rect textRect = new Rect(inRect.x, inRect.y, inRect.width, inRect.height - 50f);
            Widgets.Label(textRect, text);

            Rect bottomRow = new Rect(inRect.x, inRect.yMax - 32f, inRect.width, 28f);
            float btnW = 120f;
            float btnWDisable = 150f;
            float gap = 10f;
            float x = bottomRow.x;

            if (Widgets.ButtonText(new Rect(x, bottomRow.y, btnW, bottomRow.height), "TSA_WD_UpdatePopup_Open".Translate()))
            {
                Close();
                Find.WindowStack.Add(new Dialog_WD_UpdateLog(version));
            }
            x += btnW + gap;

            if (Widgets.ButtonText(new Rect(x, bottomRow.y, btnW, bottomRow.height), "TSA_WD_UpdatePopup_Close".Translate()))
                Close();
            x += btnW + gap;

            if (Widgets.ButtonText(new Rect(x, bottomRow.y, btnWDisable, bottomRow.height), "TSA_WD_UpdatePopup_Disable".Translate()))
            {
                var s = WorldDominationMod.settings;
                if (s != null) s.showUpdatePopups = false;
                Close();
            }
        }
    }
}

