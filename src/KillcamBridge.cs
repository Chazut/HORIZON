using System;
using System.Reflection;

namespace Horizon;

// Observe the optional plugin's public state. Do not stop its replay, change its
// settings or touch its actors. PendingReplayKiller covers the pre-play coroutine delay.
internal sealed class KillcamBridge
{
    public const string PluginId = "com.maschine.Killcam";
    private FieldInfo? _controller, _pending;
    private PropertyInfo? _playing;

    public void Bind(Type pluginType)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
        var controller = pluginType.GetField("ReplayCtrl", flags)
            ?? throw new MissingFieldException(pluginType.FullName, "ReplayCtrl");
        var pending = controller.FieldType.GetField("PendingReplayKiller", flags)
            ?? throw new MissingFieldException(controller.FieldType.FullName, "PendingReplayKiller");
        var playing = controller.FieldType.GetProperty("IsPlaying", BindingFlags.Public | BindingFlags.Instance);
        if (playing?.PropertyType != typeof(bool) || playing.GetMethod == null || playing.GetIndexParameters().Length != 0)
            throw new MissingMemberException(controller.FieldType.FullName, "IsPlaying");
        _controller = controller;
        _pending = pending;
        _playing = playing;
    }

    public bool IsBusy()
    {
        if (_controller == null) return false; // Killcam is not installed.
        if (_pending!.GetValue(null) != null) return true;
        var controller = _controller.GetValue(null);
        if (controller == null) return false;
        return _playing!.GetValue(controller) is bool playing
            ? playing : throw new InvalidOperationException("Killcam playback state is not a boolean");
    }
}
