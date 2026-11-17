using System.Collections.Generic;
using UnityEngine;

namespace Horizon;

internal sealed class BotLabelLayout
{
    public readonly List<(Rect Bounds, string Text, Color Tint)> Labels = new();
    private static readonly int[] Columns = { 0, -1, 1, -2, 2 };

    public Rect Add(Vector2 head, Vector2 size, Vector2 screen, float gap, string text, Color tint)
    {
        const float margin = 8;
        float width = Mathf.Min(size.x, screen.x - 2 * margin);
        float height = Mathf.Min(size.y, screen.y - 2 * margin);
        var best = default(Rect);
        float bestOverlap = float.PositiveInfinity;
        float bestDistance = float.PositiveInfinity;

        // Every slot is relative to this head. A neighbour can never push a label
        // another row down the screen. At the top edge the label stays readable.
        for (int row = 0; row < 3; row++)
        {
            float y = Mathf.Clamp(head.y - (row + 1) * (height + gap), margin, screen.y - height - margin);
            foreach (int column in Columns)
            {
                float x = Mathf.Clamp(head.x - width / 2 + column * (width + gap), margin, screen.x - width - margin);
                var slot = new Rect(x, y, width, height);
                float distance = (NearestPoint(slot, head) - head).sqrMagnitude;
                if (bestOverlap == 0 && distance >= bestDistance) continue;
                float overlap = 0;
                foreach (var placed in Labels)
                {
                    var other = placed.Bounds;
                    if (slot.Overlaps(other))
                        overlap += (Mathf.Min(slot.xMax, other.xMax) - Mathf.Max(slot.xMin, other.xMin)) *
                                   (Mathf.Min(slot.yMax, other.yMax) - Mathf.Max(slot.yMin, other.yMin));
                }
                if (row == 0 && column == 0 && overlap == 0)
                {
                    Labels.Add((slot, text, tint));
                    return slot;
                }
                if (overlap < bestOverlap || (overlap == bestOverlap && distance < bestDistance))
                {
                    best = slot;
                    bestOverlap = overlap;
                    bestDistance = distance;
                }
            }
        }
        Labels.Add((best, text, tint));
        return best;
    }

    private static Vector2 NearestPoint(Rect rect, Vector2 head) =>
        new(Mathf.Clamp(head.x, rect.xMin, rect.xMax), Mathf.Clamp(head.y, rect.yMin, rect.yMax));

    public static Vector2 ConnectorEnd(Rect rect, Vector2 head)
    {
        var end = NearestPoint(rect, head);
        // A label clamped to the screen edge can contain its own head marker.
        if (rect.Contains(head))
        {
            float edgeX = head.x < rect.center.x ? rect.xMin : rect.xMax;
            float edgeY = head.y < rect.center.y ? rect.yMin : rect.yMax;
            if (Mathf.Abs(edgeX - head.x) < Mathf.Abs(edgeY - head.y)) end.x = edgeX;
            else end.y = edgeY;
        }
        return end;
    }

    public static Rect DashUV(float length, float scale, bool vertical)
    {
        // Whole periods start and end on the opaque texel, including at every corner.
        float periods = Mathf.Max(1, Mathf.RoundToInt(length / (6f * scale)));
        return vertical ? new Rect(.125f, .25f, .25f, periods) : new Rect(.25f, .125f, periods, .25f);
    }
}
