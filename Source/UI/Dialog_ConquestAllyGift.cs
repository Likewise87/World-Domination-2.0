using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Lists allied/neutral factions that may be gifted the conquered settlement (same tier). Picking one spawns their settlement and improves goodwill.</summary>
    public class Dialog_ConquestAllyGift : Window
    {
        private readonly int tile;
        private readonly int ruinsId;
        private readonly SettlementTier tier;
        private readonly Faction conqueredFaction;
        private readonly ConquestOpportunityContext conquestContext;
        private Vector2 scrollPosition;
        private bool giftGiven;

        private const float RowHeight = 86f;
        private const float RowGap = 6f;

        public override Vector2 InitialSize => new Vector2(760f, 560f);

        public Dialog_ConquestAllyGift(int tile, int ruinsId, SettlementTier tier, Faction conqueredFaction, ConquestOpportunityContext conquestContext)
        {
            this.tile = tile;
            this.ruinsId = ruinsId;
            this.tier = tier;
            this.conqueredFaction = conqueredFaction;
            this.conquestContext = conquestContext;
            doCloseX = true;
            doCloseButton = false;
            absorbInputAroundWindow = true;
        }

        public override void PostClose()
        {
            base.PostClose();
            if (!giftGiven && conquestContext != null)
                conquestContext.ReopenMenuIfActive();
        }

        public override void DoWindowContents(Rect inRect)
        {
            float y = 0f;
            Text.Font = GameFont.Medium;
            Rect titleRect = new Rect(0f, y, inRect.width, 34f);
            Widgets.Label(titleRect, "TSA_WD_Conquest_AllyGiftTitle".Translate());
            Text.Font = GameFont.Small;
            y += 38f;

            string desc = "TSA_WD_Conquest_AllyGiftDesc".Translate((int)tier + 1);
            float descH = Mathf.Max(64f, Text.CalcHeight(desc, inRect.width));
            Rect descRect = new Rect(0f, y, inRect.width, descH);
            Widgets.Label(descRect, desc);
            y += descH + 10f;

            List<WD_Outpost_ConquestChoices.ConquestGiftFactionOption> options =
                WD_Outpost_ConquestChoices.GetGiftFactionOptions(conqueredFaction);

            float listTop = y;
            float listBottom = inRect.height - CloseButSize.y - 10f;
            Rect outRect = new Rect(0f, listTop, inRect.width, listBottom - listTop);
            float viewHeight = options.Count * (RowHeight + RowGap) + 4f;
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, viewHeight);

            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
            Faction factionToGift = null;
            float ry = 0f;
            for (int i = 0; i < options.Count; i++)
            {
                WD_Outpost_ConquestChoices.ConquestGiftFactionOption option = options[i];
                Faction f = option.Faction;
                if (f == null) continue;
                bool blocked = option.BlockedAlliedToConquered;

                Rect row = new Rect(0f, ry, viewRect.width, RowHeight);
                Widgets.DrawMenuSection(row);
                if (Mouse.IsOver(row)) Widgets.DrawHighlight(row);

                Color prev = GUI.color;
                if (blocked)
                    GUI.color = new Color(1f, 1f, 1f, 0.45f);

                Rect iconRect = new Rect(row.x + 10f, row.y + 12f, 40f, 40f);
                WorldDomination_UIUtils.DrawFactionIconWithColor(iconRect, f);
                TooltipHandler.TipRegion(iconRect, f.Name ?? f.def.LabelCap);

                Rect nameRect = new Rect(iconRect.xMax + 12f, row.y + 6f, 260f, 24f);
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.Font = GameFont.Small;
                Widgets.Label(nameRect, f.Name ?? f.def.LabelCap);

                Rect typeRect = new Rect(nameRect.x, nameRect.yMax - 2f, nameRect.width, 20f);
                Text.Font = GameFont.Tiny;
                GUI.color = blocked ? new Color(1f, 1f, 1f, 0.45f) : Color.gray;
                Widgets.Label(typeRect, f.def.LabelCap);
                GUI.color = blocked ? new Color(1f, 1f, 1f, 0.45f) : Color.white;

                Faction player = Faction.OfPlayerSilentFail;
                FactionRelationKind relationKind = WorldActions_Utils.SafeRelationKindWith(f, player);
                FactionRelation relation = player != null ? f.RelationWith(player, true) : null;
                int goodwill = relation?.baseGoodwill ?? 0;

                Rect relationRect = new Rect(row.x + 340f, row.y + 6f, 110f, 24f);
                Text.Font = GameFont.Small;
                GUI.color = blocked ? new Color(1f, 1f, 1f, 0.45f) : RelationColor(relationKind);
                Widgets.Label(relationRect, relationKind.GetLabel());
                GUI.color = blocked ? new Color(1f, 1f, 1f, 0.45f) : Color.white;

                Rect goodwillLabelRect = new Rect(relationRect.x, relationRect.yMax - 2f, 130f, 20f);
                Text.Font = GameFont.Tiny;
                string goodwillLabel = "TSA_WD_GoodwillLabel".Translate(FormatSigned(goodwill)).ToString();
                Widgets.Label(goodwillLabelRect, goodwillLabel);

                Rect goodwillBarRect = new Rect(row.x + 470f, row.y + 18f, 110f, 16f);
                DrawGoodwillBar(goodwillBarRect, goodwill);
                TooltipHandler.TipRegion(goodwillBarRect, goodwillLabel);

                GUI.color = prev;
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;

                if (blocked)
                {
                    string warn = "TSA_WD_Conquest_AllyGiftBlockedAlliedToConquered".Translate().ToString();
                    Rect warnRect = new Rect(row.x + 10f, row.yMax - 28f, row.width - 136f, 24f);
                    Text.Font = GameFont.Tiny;
                    GUI.color = new Color(1f, 0.75f, 0.35f, 1f);
                    Widgets.Label(warnRect, warn);
                    GUI.color = Color.white;
                    Text.Font = GameFont.Small;
                    TooltipHandler.TipRegion(row, warn);

                    Rect btnRect = new Rect(row.xMax - 116f, row.y + 25f, 110f, 36f);
                    GUI.enabled = false;
                    Widgets.ButtonText(btnRect, "TSA_WD_Conquest_AllyGiftGive".Translate());
                    GUI.enabled = true;
                }
                else
                {
                    Rect btnRect = new Rect(row.xMax - 116f, row.y + 25f, 110f, 36f);
                    if (Widgets.ButtonText(btnRect, "TSA_WD_Conquest_AllyGiftGive".Translate()))
                        factionToGift = f;
                }

                ry += RowHeight + RowGap;
            }
            Widgets.EndScrollView();

            if (factionToGift != null)
            {
                giftGiven = true;
                WD_Outpost_ConquestChoices.GiveSettlementToAlly(tile, ruinsId, tier, factionToGift);
                if (conquestContext != null) conquestContext.consumed = true;
                Close();
                return;
            }

            Rect backRect = new Rect(0f, inRect.height - CloseButSize.y, CloseButSize.x, CloseButSize.y);
            if (Widgets.ButtonText(backRect, "TSA_WD_Conquest_Back".Translate()))
                Close();
        }

        private static Color RelationColor(FactionRelationKind kind)
        {
            if (kind == FactionRelationKind.Ally) return ColorLibrary.LightGreen;
            if (kind == FactionRelationKind.Hostile) return ColorLibrary.RedReadable;
            return Color.white;
        }

        private static void DrawGoodwillBar(Rect rect, int goodwill)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.18f, 0.18f, 0.18f, 1f));
            float centerX = rect.x + rect.width / 2f;
            float scale = goodwill >= 0 ? GoodwillCapUtility.MaxGoodwillCap() : 100f;
            float amount = Mathf.Clamp(Mathf.Abs(goodwill) / Mathf.Max(1f, scale), 0f, 1f);
            Color color = goodwill >= 0 ? ColorLibrary.LightGreen : ColorLibrary.RedReadable;
            Rect fill = goodwill >= 0
                ? new Rect(centerX, rect.y, rect.width / 2f * amount, rect.height)
                : new Rect(centerX - rect.width / 2f * amount, rect.y, rect.width / 2f * amount, rect.height);
            Widgets.DrawBoxSolid(fill, color);
            Widgets.DrawLineVertical(centerX, rect.y, rect.height);
            Widgets.DrawBox(rect);
        }

        private static string FormatSigned(int value)
        {
            return value > 0 ? "+" + value : value.ToString();
        }
    }
}
