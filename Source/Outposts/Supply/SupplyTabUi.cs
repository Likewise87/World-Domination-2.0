using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace TSA_WorldDomination
{
    /// <summary>What the shared right column needs to know about a tooling or power producer.</summary>
    internal sealed class SupplyProducerSpec
    {
        public SupplyResource resource;
        public WorldObject_WD_Outpost source;
        /// <summary>Base +/- step (1 tooling, 100 W power), multiplied by <see cref="WdQuantityUI.AdjustmentStep"/>.</summary>
        public float baseStep = 1f;
        public Func<float, string> format;
        public Func<WorldObject_WD_Outpost, string> info;
        public Func<WorldObject_WD_Outpost, float> net;
        public string roleLabel;
        public Action roleClick;
        /// <summary>False while the producer feeds the colony instead of other outposts (nothing to allocate).</summary>
        public bool supplying = true;
        public string notSupplyingMessage;
        public string sourceInfo;
    }

    /// <summary>Delegate-driven right column for the Supply tab's Tooling and Power pages (producer and consumer views).</summary>
    internal static class SupplyTabUi
    {
        private const float RowIconSize = 28f;
        private const float ProducerRowHeight = 58f;
        private const float ProducerRowStep = 62f;
        private const float AssignmentBtnW = 24f;
        private const float AssignmentValueW = 48f;
        private const float ProducerNetW = 60f;
        private const float ScrollbarPad = 10f;
        private const float RoleBtnW = 150f;

        private static readonly List<SupplyFlow> outgoingScratch = new List<SupplyFlow>(16);

        // --- role menus (same options as the gizmos) ---

        public static void OpenPowerRoleMenu(WorldObject_WD_Outpost outpost)
        {
            var options = new List<FloatMenuOption>();
            foreach (PowerOutputRole role in Enum.GetValues(typeof(PowerOutputRole)))
            {
                PowerOutputRole r = role;
                string label = ("TSA_WD_PowerRole_" + r).Translate().ToString()
                    + (r == outpost.PowerRole ? " " + "TSA_WD_FoodRole_CurrentTag".Translate().ToString() : "");
                options.Add(new FloatMenuOption(label, delegate { outpost.SetPowerRole(r); }));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        public static void OpenToolingRoleMenu(WorldObject_WD_Outpost outpost)
        {
            var options = new List<FloatMenuOption>();
            ToolingOutputRole selected = outpost.SelectedToolingRole;
            foreach (ToolingOutputRole role in Enum.GetValues(typeof(ToolingOutputRole)))
            {
                ToolingOutputRole r = role;
                string label = ("TSA_WD_ToolingRole_" + r).Translate().ToString()
                    + (r == selected ? " " + "TSA_WD_FoodRole_CurrentTag".Translate().ToString() : "");
                options.Add(new FloatMenuOption(label, delegate
                {
                    if (outpost.SelectedToolingRole == r) return;
                    outpost.SetSelectedToolingRole(r);
                    if (outpost.GetToolingRoleForCurrentCycle() != r)
                        Messages.Message("TSA_WD_FoodRole_AppliesNextCycle".Translate(outpost.LabelCap, ("TSA_WD_ToolingRole_" + r).Translate().ToString()),
                            outpost, MessageTypeDefOf.NeutralEvent, false);
                }));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        // --- shared bits ---

        private static void LabelAnchored(Rect rect, string text, TextAnchor anchor)
        {
            TextAnchor prev = Text.Anchor;
            Text.Anchor = anchor;
            Widgets.Label(rect, text);
            Text.Anchor = prev;
        }

        private static string FormatNet(float net, Func<float, string> format)
        {
            string text = net > 0.05f ? "+" + format(net) : format(net);
            return text.Colorize(net > 0.1f ? Color.green : (net < -0.1f ? Color.red : Color.yellow));
        }

        private static void GetRowLayout(float rowWidth, out float nameX, out float nameW, out float netX, out float ctrlX, out float ctrlW)
        {
            ctrlW = AssignmentBtnW + AssignmentValueW + AssignmentBtnW;
            ctrlX = rowWidth - ctrlW - ScrollbarPad;
            netX = ctrlX - ProducerNetW - 6f;
            nameX = RowIconSize + 6f;
            nameW = netX - nameX - 6f;
        }

        private static void JumpTo(WorldObject target)
        {
            CameraJumper.TryJump(target);
            Find.WorldSelector.ClearSelection();
            Find.WorldSelector.Select(target);
            SoundDefOf.Click.PlayOneShotOnCamera();
            if (Find.MainTabsRoot.OpenTab != null) Find.MainTabsRoot.EscapeCurrentTab();
        }

        private static void DrawOutpostIcon(Rect iconRect, WorldObject target)
        {
            Texture2D tex = target?.def?.ExpandingIconTexture;
            if (tex == null) return;
            GUI.color = target.Faction?.Color ?? Color.white;
            GUI.DrawTexture(iconRect, tex, ScaleMode.ScaleToFit);
            GUI.color = Color.white;
        }

        private static void DrawRoleButton(Rect rightRect, string label, Action click)
        {
            if (click == null) return;
            float right = rightRect.x + rightRect.width - 16f - ScrollbarPad;
            Rect btn = new Rect(right - RoleBtnW, rightRect.y, RoleBtnW, 22f);
            if (Widgets.ButtonText(btn, label)) click();
        }

        // --- producer ---

        public static void DrawProducerColumn(Rect rightRect, SupplyProducerSpec spec, List<WorldObject_WD_Outpost> recipients,
            ref Vector2 scroll, ref float scrollViewHeight)
        {
            Text.Font = GameFont.Small;
            GUI.color = Widgets.SeparatorLabelColor;
            Widgets.Label(new Rect(rightRect.x, rightRect.y, rightRect.width - RoleBtnW - 24f, 22f), "TSA_AllocationStrategy".Translate());
            GUI.color = Color.white;
            DrawRoleButton(rightRect, spec.roleLabel, spec.roleClick);
            float y = rightRect.y + 26f;

            if (!spec.supplying)
            {
                GUI.color = Color.gray;
                float h = Mathf.Min(rightRect.yMax - y, Mathf.Max(48f, Text.CalcHeight(spec.notSupplyingMessage, rightRect.width)));
                Widgets.Label(new Rect(rightRect.x, y, rightRect.width, h), spec.notSupplyingMessage);
                GUI.color = Color.white;
                return;
            }

            SupplyAllocations.GetOutgoing(spec.resource, spec.source, outgoingScratch);
            SupplyMode mode = SupplyAllocations.GetMode(spec.resource, spec.source);
            int sourceTile = spec.source.Tile.tileId;
            float budget = SupplyAllocations.GetBudget(spec.resource, spec.source);
            float used = 0f;
            for (int i = 0; i < outgoingScratch.Count; i++) used += outgoingScratch[i].amount;
            if (mode == SupplyMode.Manual) used = SupplyAllocations.GetAssignedSum(spec.resource, sourceTile);
            float unassigned = Mathf.Max(0f, budget - used);

            DrawModeToggles(new Rect(rightRect.x, y, rightRect.width, 28f), spec, mode);
            y += 32f;

            // Unassigned: budget not handed to any outpost (same look as the food "Keep here" row, no +/-).
            GUI.color = Widgets.SeparatorLabelColor;
            Rect headerRect = new Rect(rightRect.x, y, rightRect.width, 22f);
            Widgets.Label(headerRect, "TSA_Supply_Unassigned".Translate());
            GUI.color = Color.white;
            TooltipHandler.TipRegion(headerRect, "TSA_Supply_UnassignedTip".Translate());
            y += 24f;
            DrawUnassignedRow(new Rect(rightRect.x, y, rightRect.width - 16f, ProducerRowHeight), spec, unassigned);
            y += ProducerRowStep;

            GUI.color = Widgets.SeparatorLabelColor;
            Widgets.Label(new Rect(rightRect.x, y, rightRect.width, 22f), "TSA_Logistics_SectionOtherOutposts".Translate());
            GUI.color = Color.white;
            y += 24f;

            Rect listRect = new Rect(rightRect.x, y, rightRect.width, rightRect.yMax - y);
            DrawRecipients(listRect, spec, recipients, mode, used, budget, ref scroll, ref scrollViewHeight);
        }

        private static void DrawModeToggles(Rect rect, SupplyProducerSpec spec, SupplyMode mode)
        {
            const float btnSize = 24f;
            const float labelGap = 4f;
            const float groupGap = 16f;
            const float resetBtnW = 140f;

            string smart = "TSA_LogisticsMode_Smart".Translate();
            string manual = "TSA_LogisticsMode_Manual".Translate();

            float smartLabelW = Text.CalcSize(smart).x;
            float smartGroupW = btnSize + labelGap + smartLabelW;
            if (Widgets.RadioButton(rect.x, rect.y, mode == SupplyMode.Smart))
                SupplyAllocations.SetMode(spec.resource, spec.source, SupplyMode.Smart);
            LabelAnchored(new Rect(rect.x + btnSize + labelGap, rect.y, smartLabelW, btnSize), smart, TextAnchor.MiddleLeft);
            TooltipHandler.TipRegion(new Rect(rect.x, rect.y, smartGroupW, btnSize), "TSA_Supply_SmartTip".Translate());

            float manualLabelW = Text.CalcSize(manual).x;
            float manualGroupW = btnSize + labelGap + manualLabelW;
            float manualX = rect.x + smartGroupW + groupGap;
            if (Widgets.RadioButton(manualX, rect.y, mode == SupplyMode.Manual))
                SupplyAllocations.SetMode(spec.resource, spec.source, SupplyMode.Manual);
            LabelAnchored(new Rect(manualX + btnSize + labelGap, rect.y, manualLabelW, btnSize), manual, TextAnchor.MiddleLeft);
            TooltipHandler.TipRegion(new Rect(manualX, rect.y, manualGroupW, btnSize), "TSA_Supply_ManualTip".Translate());

            float resetRight = rect.x + rect.width - 16f - ScrollbarPad;
            Rect resetRect = new Rect(resetRight - resetBtnW, rect.y, resetBtnW, btnSize);
            if (Widgets.ButtonText(resetRect, "TSA_Logistics_ResetAllocation".Translate()))
                SupplyAllocations.Reset(spec.resource, spec.source);
            TooltipHandler.TipRegion(resetRect, "TSA_Supply_ResetTip".Translate());
        }

        private static void DrawUnassignedRow(Rect row, SupplyProducerSpec spec, float unassigned)
        {
            Widgets.DrawHighlight(row);
            Widgets.DrawHighlightIfMouseover(row);
            GetRowLayout(row.width, out float nameX, out float nameW, out float netX, out _, out _);

            float contentY = row.y + row.height / 2f;
            DrawOutpostIcon(new Rect(row.x, contentY - RowIconSize / 2f, RowIconSize, RowIconSize), spec.source);
            Widgets.Label(new Rect(row.x + nameX, contentY - 21f, nameW, 24f), spec.source.LabelCap.Truncate(nameW - 4f));

            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(row.x + nameX, contentY + 2f, nameW, 20f), spec.sourceInfo ?? "");
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            Rect netRect = new Rect(row.x + netX, row.y, ProducerNetW, row.height);
            LabelAnchored(netRect, FormatNet(unassigned, spec.format), TextAnchor.MiddleCenter);
            TooltipHandler.TipRegion(netRect, "TSA_Supply_UnassignedTip".Translate());
        }

        private static void DrawRecipients(Rect listRect, SupplyProducerSpec spec, List<WorldObject_WD_Outpost> recipients,
            SupplyMode mode, float used, float budget, ref Vector2 scroll, ref float scrollViewHeight)
        {
            if (recipients == null || recipients.Count == 0)
            {
                GUI.color = Color.gray;
                string msg = "TSA_Supply_NoRecipients".Translate(spec.source.def.LabelCap).ToString();
                Widgets.Label(new Rect(listRect.x, listRect.y, listRect.width, Mathf.Min(listRect.height, Mathf.Max(48f, Text.CalcHeight(msg, listRect.width)))), msg);
                GUI.color = Color.white;
                return;
            }

            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, scrollViewHeight);
            Widgets.BeginScrollView(listRect, ref scroll, viewRect);

            float vw = viewRect.width;
            GetRowLayout(vw, out float nameX, out float nameW, out float netX, out float ctrlX, out float ctrlW);
            int sourceTile = spec.source.Tile.tileId;

            float innerY = 0f;
            for (int i = 0; i < recipients.Count; i++)
            {
                WorldObject_WD_Outpost target = recipients[i];
                int destTile = target.Tile.tileId;

                Rect row = new Rect(0f, innerY, vw, ProducerRowHeight);
                if (i % 2 == 0) Widgets.DrawHighlight(row);
                Widgets.DrawHighlightIfMouseover(row);

                Rect jumpRect = new Rect(row.x, row.y, netX, row.height);
                if (Widgets.ButtonInvisible(jumpRect)) JumpTo(target);
                TooltipHandler.TipRegion(jumpRect, "TSA_WD_JumpToOutpost".Translate(target.LabelCap));

                float contentY = innerY + row.height / 2f;
                DrawOutpostIcon(new Rect(row.x, contentY - RowIconSize / 2f, RowIconSize, RowIconSize), target);
                Widgets.Label(new Rect(nameX, contentY - 21f, nameW, 24f), target.LabelCap.ToString().Truncate(nameW - 4f));

                Text.Font = GameFont.Tiny;
                GUI.color = Color.gray;
                Widgets.Label(new Rect(nameX, contentY + 2f, nameW, 20f), spec.info(target));
                GUI.color = Color.white;
                Text.Font = GameFont.Small;

                Rect netRect = new Rect(netX, innerY, ProducerNetW, row.height);
                LabelAnchored(netRect, FormatNet(spec.net(target), spec.format), TextAnchor.MiddleCenter);
                TooltipHandler.TipRegion(netRect, "TSA_Logistics_TotalNetTip".Translate());

                float current = ValueTo(spec, mode, sourceTile, destTile);
                string rowKey = destTile + ":" + (int)spec.resource;
                float step = WdQuantityUI.AdjustmentStep() * spec.baseStep;
                float btnY = innerY + row.height / 2f - 12f;

                Rect minusRect = new Rect(ctrlX, btnY, AssignmentBtnW, 24f);
                if (WdDragSelectButtons.ButtonText(minusRect, "-", WdDragSelectButtons.Hash(rowKey, "minus")) && current > 0f)
                    SupplyAllocations.SetAssigned(spec.resource, spec.source, destTile, Mathf.Max(0f, current - step));

                current = ValueTo(spec, mode, sourceTile, destTile);
                LabelAnchored(new Rect(ctrlX + AssignmentBtnW, innerY, AssignmentValueW, row.height), current.ToString("F0"), TextAnchor.MiddleCenter);

                bool canAdd = used + 0.05f < budget;
                if (!canAdd) GUI.color = Color.gray;
                Rect plusRect = new Rect(ctrlX + ctrlW - AssignmentBtnW, btnY, AssignmentBtnW, 24f);
                if (WdDragSelectButtons.ButtonText(plusRect, "+", WdDragSelectButtons.Hash(rowKey, "plus"), active: canAdd) && canAdd)
                    SupplyAllocations.SetAssigned(spec.resource, spec.source, destTile, current + Mathf.Min(step, budget - used));
                GUI.color = Color.white;

                innerY += ProducerRowStep;
            }

            if (Event.current.type == EventType.Layout) scrollViewHeight = innerY;
            Widgets.EndScrollView();
        }

        /// <summary>Manual: the assigned amount. Smart: what the last allocation actually sends.</summary>
        private static float ValueTo(SupplyProducerSpec spec, SupplyMode mode, int sourceTile, int destTile)
        {
            if (mode == SupplyMode.Manual)
                return SupplyAllocations.GetAssigned(spec.resource, sourceTile, destTile);
            for (int i = 0; i < outgoingScratch.Count; i++)
            {
                if (outgoingScratch[i].destTile == destTile) return outgoingScratch[i].amount;
            }
            return 0f;
        }

        // --- consumer ---

        private static readonly List<SupplyFlow> incomingScratch = new List<SupplyFlow>(8);

        public static void DrawConsumerColumn(Rect rightRect, SupplyResource res, WorldObject_WD_Outpost dest, string header,
            Func<float, string> format, string emptyMessage, string roleLabel, Action roleClick, ref Vector2 scroll, ref float scrollViewHeight)
        {
            float y = OutpostTabStatsUi.DrawSectionHeader(rightRect.x, rightRect.y, rightRect.width, header);
            DrawRoleButton(rightRect, roleLabel, roleClick);

            SupplyAllocations.GetIncoming(res, dest, incomingScratch);
            Rect listRect = new Rect(rightRect.x, y, rightRect.width, rightRect.yMax - y);
            if (incomingScratch.Count == 0)
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(listRect.x, listRect.y, listRect.width, Mathf.Min(listRect.height, Mathf.Max(48f, Text.CalcHeight(emptyMessage, listRect.width)))), emptyMessage);
                GUI.color = Color.white;
                return;
            }

            const float rowH = 40f;
            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, scrollViewHeight);
            Widgets.BeginScrollView(listRect, ref scroll, viewRect);

            float vw = viewRect.width;
            const float iconColW = 40f;
            const float amountColW = 80f;
            float nameColW = vw - iconColW - amountColW - 8f;

            float curY = 0f;
            for (int i = 0; i < incomingScratch.Count; i++)
            {
                SupplyFlow flow = incomingScratch[i];
                WorldObject src = Find.WorldObjects.WorldObjectAt<WorldObject>(flow.sourceTile);
                if (src == null) continue;

                Rect rowRect = new Rect(0f, curY, vw, rowH);
                if (i % 2 == 0) Widgets.DrawHighlight(rowRect);
                Widgets.DrawHighlightIfMouseover(rowRect);
                if (Widgets.ButtonInvisible(rowRect)) JumpTo(src);
                TooltipHandler.TipRegion(rowRect, "TSA_WD_JumpToOutpost".Translate(src.LabelCap));

                DrawOutpostIcon(new Rect(4f, curY + rowH / 2f - RowIconSize / 2f, RowIconSize, RowIconSize), src);
                string name = src.LabelCap + " (" + src.def.label + ")";
                LabelAnchored(new Rect(iconColW + 4f, curY, nameColW - 8f, rowH), name.Truncate(nameColW - 12f), TextAnchor.MiddleLeft);
                LabelAnchored(new Rect(iconColW + nameColW + 4f, curY, amountColW - 8f, rowH),
                    format(flow.amount).Colorize(Color.green), TextAnchor.MiddleLeft);

                curY += rowH;
            }

            if (Event.current.type == EventType.Layout) scrollViewHeight = curY;
            Widgets.EndScrollView();
        }
    }
}
