namespace Horizon;

public enum SpectatorOverlay { Off, BotInfo, BotDebug }

internal sealed class OverlaySelection
{
    public SpectatorOverlay Mode { get; private set; }
    private SpectatorOverlay _previousMode;

    public void Select(SpectatorOverlay mode, bool botDebugAvailable)
    {
        _previousMode = SpectatorOverlay.Off;
        Mode = mode == SpectatorOverlay.BotDebug && !botDebugAvailable ? SpectatorOverlay.Off : mode;
    }

    public void ToggleBotInfo()
    {
        if (Mode == SpectatorOverlay.BotInfo) Mode = _previousMode;
        else
        {
            _previousMode = Mode;
            Mode = SpectatorOverlay.BotInfo;
        }
    }

    // Respect BotDebug's own F12 toggle without drawing two overlays or rewriting its config.
    public void ObserveBotDebug(bool enabled)
    {
        if (enabled) Mode = SpectatorOverlay.BotDebug;
        else if (Mode == SpectatorOverlay.BotDebug) Mode = SpectatorOverlay.Off;
    }
}
