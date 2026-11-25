using RimWorld;
using UnityEngine;
using Verse;

namespace SlotReservations
{
    /// <summary>Editor window: rule list on the left, filter of the selected rule on the right.</summary>
    public class Dialog_SlotRules : Window
    {
        readonly CompSlotRules comp;
        SlotRule selected;
        Vector2 listScroll;
        readonly ThingFilterUI.UIState filterState = new ThingFilterUI.UIState();

        public override Vector2 InitialSize => new Vector2(860f, 640f);

        public Dialog_SlotRules(CompSlotRules comp)
        {
            this.comp = comp;
            selected = comp.rules.FirstOrDefault();
            doCloseX = true;
            doCloseButton = true;
            draggable = true;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = false;
            preventCameraMotion = false;
        }

        // Filter of the building itself (the outer filter), used to limit what can be picked.
        ThingFilter ParentFilter => ((IStoreSettingsParent)comp.Storage).GetParentStoreSettings()?.filter;

        public override void DoWindowContents(Rect inRect)
        {
            if (comp.parent.Destroyed) { Close(); return; }

            int total = SlotRuleSolver.TotalSlots(comp.Storage);
            int reserved = comp.ReservedSlots;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 34f), "SR_DialogTitle".Translate(comp.parent.LabelCap));
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(inRect.x, inRect.y + 34f, inRect.width, 24f),
                "SR_DialogSummary".Translate(reserved, total, total - reserved, comp.Storage.slotGroup?.HeldThingsCount ?? 0));

            var body = new Rect(inRect.x, inRect.y + 64f, inRect.width, inRect.height - 64f - CloseButSize.y - 10f);
            var left = body.LeftPartPixels(400f);
            var right = new Rect(left.xMax + 12f, body.y, body.width - left.width - 12f, body.height);

            DrawRuleList(left, total, reserved);

            if (selected != null && comp.rules.Contains(selected))
            {
                Widgets.Label(right.TopPartPixels(24f), "SR_FilterFor".Translate(selected.count));
                ThingFilterUI.DoThingFilterConfigWindow(new Rect(right.x, right.y + 26f, right.width, right.height - 26f),
                    filterState, selected.filter, ParentFilter, map: comp.parent.MapHeld);
            }
            else
            {
                Widgets.Label(right, "SR_SelectOrAdd".Translate());
            }
        }

        void DrawRuleList(Rect rect, int total, int reserved)
        {
            const float rowH = 30f;
            var addRect = new Rect(rect.x, rect.y, 140f, 28f);
            if (Widgets.ButtonText(addRect, "SR_AddRule".Translate()))
            {
                var rule = new SlotRule { count = reserved < total ? 1 : 0 };
                comp.rules.Add(rule);
                selected = rule;
                comp.Notify_RulesChanged();
            }

            var outRect = new Rect(rect.x, rect.y + 34f, rect.width, rect.height - 34f);
            var viewRect = new Rect(0f, 0f, outRect.width - 16f, comp.rules.Count * rowH);
            Widgets.BeginScrollView(outRect, ref listScroll, viewRect);
            for (int i = 0; i < comp.rules.Count; i++)
            {
                var rule = comp.rules[i];
                var row = new Rect(0f, i * rowH, viewRect.width, rowH - 2f);
                if (rule == selected) Widgets.DrawHighlightSelected(row);
                else if (Mouse.IsOver(row)) Widgets.DrawHighlight(row);

                float x = row.x;
                // [-] count [+]
                if (Widgets.ButtonText(new Rect(x, row.y, 26f, row.height), "-") && rule.count > 0)
                { rule.count--; comp.Notify_RulesChanged(); }
                x += 28f;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(x, row.y, 34f, row.height), rule.count.ToString());
                Text.Anchor = TextAnchor.UpperLeft;
                x += 36f;
                if (Widgets.ButtonText(new Rect(x, row.y, 26f, row.height), "+") && reserved < total)
                { rule.count++; reserved++; comp.Notify_RulesChanged(); }
                x += 32f;

                // label (click to select)
                var labelRect = new Rect(x, row.y, row.xMax - x - 32f, row.height);
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(labelRect, rule.Label.Truncate(labelRect.width));
                Text.Anchor = TextAnchor.UpperLeft;
                if (Widgets.ButtonInvisible(labelRect)) selected = rule;

                // delete (tooltip uses the game's own "Delete" text)
                var delRect = new Rect(row.xMax - 28f, row.y, 28f, row.height);
                TooltipHandler.TipRegion(delRect, "Delete".Translate());
                if (Widgets.ButtonText(delRect, "X"))
                {
                    comp.rules.RemoveAt(i);
                    if (selected == rule) selected = comp.rules.FirstOrDefault();
                    comp.Notify_RulesChanged();
                    break;
                }
            }
            Widgets.EndScrollView();
        }

        public override void PostClose()
        {
            base.PostClose();
            comp.Notify_RulesChanged(); // filter edits don't notify individually
        }
    }

    static class ListExt
    {
        public static T FirstOrDefault<T>(this System.Collections.Generic.List<T> l) where T : class => l.Count > 0 ? l[0] : null;
    }
}
