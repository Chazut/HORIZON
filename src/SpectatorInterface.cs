using System;

namespace Horizon;

internal static class SpectatorInterface
{
    public static float Scale(int screenHeight, bool automatic, float preference)
    {
        if (float.IsNaN(preference) || float.IsInfinity(preference)) preference = 1;
        float resolution = automatic ? Math.Max(1f, screenHeight / 1080f) : 1f;
        return Math.Min(3f, resolution) * Math.Max(.75f, Math.Min(2.5f, preference));
    }

    // Physical screen coordinates are used for both drawing and dragging.
    // Saved positions remain relative when scale or resolution changes.
    public static float Available(float screen, float panel, float margin) => Math.Max(0, screen - panel - margin * 2);
    public static float Position(float normalized, float available, float margin) => margin + Unit(normalized) * available;
    public static float Normalize(float position, float available, float margin) => available > 0 ? Unit((position - margin) / available) : 0;
    private static float Unit(float value) => float.IsNaN(value) ? 0 : Math.Max(0, Math.Min(1, value));
}
