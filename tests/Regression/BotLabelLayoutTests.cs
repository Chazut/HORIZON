using Horizon;
using UnityEngine;

internal static class BotLabelLayoutTests
{
    public static void Run(Action<bool, string> check)
    {
        var frame = new Rect(100, 100, 200, 60);
        check(BotLabelLayout.ConnectorEnd(frame, new Vector2(200, 210)) == new Vector2(200, 160),
            "A head below the caption joins its bottom border");
        check(BotLabelLayout.ConnectorEnd(frame, new Vector2(35, 15)) == frame.min,
            "A diagonally displaced caption joins the nearest corner");
        check(BotLabelLayout.ConnectorEnd(frame, new Vector2(295, 130)) == new Vector2(300, 130),
            "An on-screen caption surrounding the marker still joins the nearest frame edge");

        foreach (var screen in new[] { new Vector2(3440, 1440), new Vector2(3840, 2160), new Vector2(1920, 1080) })
        {
            var layout = new BotLabelLayout();
            float scale = SpectatorInterface.Scale((int)screen.y, true, 1);
            var size = new Vector2(220, 66) * scale;
            float gap = 12 * scale;
            foreach (bool vertical in new[] { false, true })
            foreach (float length in new[] { gap, size.x, size.y, 505 * scale })
            {
                Rect uv = BotLabelLayout.DashUV(length, scale, vertical);
                float phase = vertical ? uv.y : uv.x;
                float repeats = vertical ? uv.height : uv.width;
                bool Ink(float t) => (phase + t * repeats) % 1 < .5f;
                check(Ink(0) && Ink(1) && !Ink(.5f / repeats),
                    "Repeated strokes have ink at both ends and a transparent gap in each period");
                check((vertical ? uv.xMin : uv.yMin) >= 0 && (vertical ? uv.xMax : uv.yMax) < .5f,
                    "Stroke thickness samples only the texture row or column carrying the dash pattern");
                check(Math.Abs(length / repeats / scale - 6) < 1,
                    "UI scaling keeps a consistent dash length on borders and connectors");
            }
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
