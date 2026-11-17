using Horizon;
using UnityEngine;

internal static class BotLabelLayoutTests
{
    public static void Run(Action<bool, string> check)
    {
        foreach (var screen in new[] { new Vector2(3440, 1440), new Vector2(3840, 2160), new Vector2(1920, 1080) })
        {
            var layout = new BotLabelLayout();
            float scale = SpectatorInterface.Scale((int)screen.y, true, 1);
            var size = new Vector2(220, 66) * scale;
            float gap = 8 * scale;
            var head = screen / 2;
            var first = layout.Add(head, size, screen, gap, "First", Color.white);
            check(Math.Abs(first.center.x - head.x) < .01f && Math.Abs(first.yMax + gap - head.y) < .01f,
                "An isolated label sits directly above its projected head");
            for (int i = 1; i < 11; i++)
            {
                // Distant bots can be a few pixels apart even when separated by metres in the raid.
                var otherHead = head + new Vector2(i % 3 - 1, i % 5 - 2);
                var rect = layout.Add(otherHead, size, screen, gap, i.ToString(), Color.red);
                check(rect.yMax <= otherHead.y - gap + .01f && rect.yMin >= otherHead.y - 3 * (size.y + gap) - .01f,
                    "A cluster cannot push a caption below its bot or stack indefinitely upward");
                check(layout.Labels.Take(i).All(previous => !rect.Overlaps(previous.Bounds)),
                    "Eleven clustered bots fit in separate slots when the viewport has room");
            }
            check(layout.Labels.Count == 11 && layout.Labels[0].Text == "First" && layout.Labels[10].Tint == Color.red,
                "Layout preserves every bot's full caption and color");
            foreach (var corner in new[] { Vector2.zero, new Vector2(screen.x, 0), new Vector2(0, screen.y), screen })
            {
                layout.Labels.Clear();
                for (int i = 0; i < 20; i++)
                {
                    var rect = layout.Add(corner, size, screen, gap, "Crowded edge", Color.white);
                    check(rect.xMin >= 8 && rect.yMin >= 8 && rect.xMax <= screen.x - 8 + .01f && rect.yMax <= screen.y - 8 + .01f,
                        "Every crowded edge label stays inside the viewport");
                    check(corner.y > 0 || rect.yMin == 8, "At the top edge labels cannot pile down the screen");
                }
                check(layout.Labels.Count == 20, "Even a saturated edge retains every label");
            }
        }
    }
}
