namespace Horizon;

public enum SpectatorOverlay { Off, BotInfo, BotDebug }

internal sealed class OverlaySelection
{
    public SpectatorOverlay Mode { get; private set; }

    public void Select(SpectatorOverlay mode, bool botDebugAvailable)
        => Mode = mode == SpectatorOverlay.BotDebug && !botDebugAvailable ? SpectatorOverlay.Off : mode;

    // Respect BotDebug's own F12 toggle without drawing two overlays or rewriting its config.
    public void ObserveBotDebug(bool enabled)
    {
        if (enabled) Mode = SpectatorOverlay.BotDebug;
        else if (Mode == SpectatorOverlay.BotDebug) Mode = SpectatorOverlay.Off;
    }
}
