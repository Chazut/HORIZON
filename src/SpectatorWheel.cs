namespace Horizon;

internal enum SpectatorWheelAction { Native, Blocked, Cycle, Zoom }

internal static class SpectatorWheel
{
    public static SpectatorWheelAction Route(bool watching, bool menu, bool zoomHeld, bool cycle)
    {
        if (!watching) return SpectatorWheelAction.Native;
        if (menu) return SpectatorWheelAction.Blocked;
        if (zoomHeld) return SpectatorWheelAction.Zoom;
        return cycle ? SpectatorWheelAction.Cycle : SpectatorWheelAction.Native;
    }
}
