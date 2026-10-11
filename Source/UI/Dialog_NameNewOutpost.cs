using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>
    /// Shown right after a WD outpost is founded. Accept applies the typed name;
    /// empty Accept rolls a random name. Close-X leaves the auto-generated spawn name.
    /// </summary>
    [StaticConstructorOnStartup]
    public class Dialog_NameNewOutpost : Window
    {
        private static readonly Texture2D RandomizeTex =
            ContentFinder<Texture2D>.Get("UI/Commands/Randomize", true);

        private readonly WorldObject_WD_Outpost outpost;
        private readonly string titleText;
        private readonly string tipText;
        private readonly string diceTip;
        private readonly bool supportsTypeTag;
        private readonly string typeTag;

        private string rootName;
        private string curName;
        private bool includeTypeTag;
        private string lastFieldValue;

        private const float SidePad = 4f;
        private const float CloseXLeftInset = 22f;
        private const float HeadlineToFieldGap = 15f;
        private const float TextFieldHeight = 28f;
        private const float DiceSize = 28f;
        private const float DiceGap = 6f;
        private const float CheckboxRowHeight = 28f;

        public override Vector2 InitialSize =>
            new Vector2(500f, supportsTypeTag ? 275f : 240f);

        public Dialog_NameNewOutpost(WorldObject_WD_Outpost outpost)
        {
            this.outpost = outpost;

            supportsTypeTag = OutpostNameTagUtility.TryGetTypeTag(outpost?.def, out typeTag);
            includeTypeTag = supportsTypeTag;

            string spawnName = outpost?.Name ?? "";
            if (supportsTypeTag && OutpostNameTagUtility.TrySplitRoot(spawnName, typeTag, out string splitRoot))
                rootName = splitRoot;
            else
                rootName = spawnName;

            curName = spawnName;
            lastFieldValue = curName;

            string typeLabel = outpost?.def?.LabelCap ?? OutpostTranslationUtil.Key("TSA_WD_Outpost_GenericLabel");
            titleText = OutpostTranslationUtil.Key("TSA_WD_Outpost_NameNewDialogTitle", typeLabel);
            tipText = OutpostTranslationUtil.Key("TSA_WD_Outpost_NameNewDialogTip");
            diceTip = OutpostTranslationUtil.Key("TSA_WD_Outpost_NameNewDialogDiceTip");

            doCloseButton = false;
            doCloseX = true;
            absorbInputAroundWindow = true;
            forcePause = true;
            optionalTitle = null;
        }

        public static void Open(WorldObject_WD_Outpost outpost)
        {
            if (outpost == null || outpost.Destroyed) return;
            Find.WindowStack?.Add(new Dialog_NameNewOutpost(outpost));
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (outpost == null) return;

            float buttonsH = CloseButSize.y + 12f;
            Rect body = new Rect(0f, 0f, inRect.width, inRect.height - buttonsH);
            float contentWidth = body.width - SidePad * 2f;
            float y = 0f;

            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            float titleWidth = body.width - SidePad - CloseXLeftInset;
            Widgets.Label(new Rect(SidePad, y, titleWidth, Outpost_Dialog_UI.DialogTitleHeight), titleText);
            Text.Anchor = TextAnchor.UpperLeft;
            y += Outpost_Dialog_UI.DialogTitleRowAdvance + HeadlineToFieldGap;

            float fieldWidth = contentWidth - DiceSize - DiceGap;
            Rect fieldRect = new Rect(SidePad, y, fieldWidth, TextFieldHeight);
            Text.Font = GameFont.Tiny;
            string typed = ClampName(Widgets.TextField(fieldRect, curName));
            Text.Font = GameFont.Small;
            if (typed != lastFieldValue)
            {
                curName = typed;
                lastFieldValue = typed;
                // Manual edit is authoritative; keep root in sync with bare field when tag off.
                if (!includeTypeTag)
                    rootName = curName;
            }

            Rect diceRect = new Rect(SidePad + fieldWidth + DiceGap, y, DiceSize, DiceSize);
            if (RandomizeTex != null)
            {
                if (Widgets.ButtonImage(diceRect, RandomizeTex))
                    RerollName();
            }
            else if (Widgets.ButtonText(diceRect, "?"))
            {
                RerollName();
            }
            TooltipHandler.TipRegion(diceRect, diceTip);
            y += TextFieldHeight + 8f;

            if (supportsTypeTag)
            {
                bool prev = includeTypeTag;
                Rect checkRect = new Rect(SidePad, y, contentWidth, CheckboxRowHeight);
                Widgets.CheckboxLabeled(
                    checkRect,
                    OutpostTranslationUtil.Key("TSA_WD_Outpost_NameNewDialogAddType"),
                    ref includeTypeTag);
                if (includeTypeTag != prev)
                    OnTypeTagToggled();
                y += CheckboxRowHeight + 6f;
            }

            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Text.Anchor = TextAnchor.UpperLeft;
            float tipHeight = Mathf.Max(15f, body.height - y);
            Widgets.Label(new Rect(SidePad, y, contentWidth, tipHeight), tipText);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            Rect acceptRect = new Rect(
                (inRect.width - CloseButSize.x) * 0.5f,
                inRect.height - CloseButSize.y,
                CloseButSize.x,
                CloseButSize.y);
            string acceptLabel = OutpostTranslationUtil.Key("TSA_WD_Outpost_RenameAccept");
            if (Widgets.ButtonText(acceptRect, acceptLabel))
                ApplyAndClose();
        }

        private void OnTypeTagToggled()
        {
            if (includeTypeTag)
            {
                // Prefer split if the field already ends with this tag; else use field as root.
                if (OutpostNameTagUtility.TrySplitRoot(curName, typeTag, out string split))
                    rootName = split;
                else
                    rootName = (curName ?? "").Trim();
                curName = OutpostNameTagUtility.ComposeIfFits(rootName, typeTag);
            }
            // Turning off: leave field as-is (no clever strip).
            lastFieldValue = curName;
        }

        private void RerollName()
        {
            if (outpost?.def == null) return;
            int tileId = outpost.Tile.tileId;
            string generated = Dialog_OutpostSelection.GenerateOutpostNamePublic(
                outpost.def, tileId, includeTypeTag && supportsTypeTag, out rootName);
            curName = generated ?? "";
            // Generator never clips composed names; only clamp bare overflow roots.
            if (curName.Length > OutpostNameTagUtility.MaxNameLength
                && !(includeTypeTag && supportsTypeTag))
                curName = ClampName(curName);
            lastFieldValue = curName;
        }

        private void ApplyAndClose()
        {
            if (outpost != null && !outpost.Destroyed)
            {
                string trimmed = curName?.Trim();
                if (string.IsNullOrEmpty(trimmed))
                {
                    RerollName();
                    trimmed = curName?.Trim();
                }
                if (string.IsNullOrEmpty(trimmed))
                    trimmed = outpost.Name ?? outpost.Tile.tileId.ToString();
                if (trimmed.Length > OutpostNameTagUtility.MaxNameLength)
                    trimmed = trimmed.Substring(0, OutpostNameTagUtility.MaxNameLength);
                outpost.Name = trimmed;
                Window_OutpostOverview.InvalidateCache();
            }
            Close();
        }

        private static string ClampName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            int max = OutpostNameTagUtility.MaxNameLength;
            return name.Length <= max ? name : name.Substring(0, max);
        }
    }
}
