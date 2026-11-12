using System;
using System.Reflection;
using BepInEx.Configuration;

namespace RaidSpectator;

internal sealed class BotDebugBridge
{
    public const string PluginId = "xyz.drakia.botdebug";
    private ConfigEntry<bool>? _enable;
    private Action? _ensureEnabled;
    private bool _owned;

    public void Bind(Assembly assembly)
    {
        var settings = assembly.GetType("DrakiaXYZ.BotDebug.Helpers.Settings", true)!;
        var entry = settings.GetField("Enable", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null) as ConfigEntry<bool>
            ?? throw new MissingFieldException("BotDebug.Settings.Enable");
        var component = assembly.GetType("DrakiaXYZ.BotDebug.Components.BotDebugComponent", true)!;
        _ensureEnabled = (Action)Delegate.CreateDelegate(typeof(Action), component.GetMethod("Enable", BindingFlags.Public | BindingFlags.Static)
            ?? throw new MissingMethodException("BotDebugComponent.Enable"));
        _enable = entry;
    }

    public bool Start()
    {
        if (_enable == null) return false;
        _owned = true;
        SetTemporary(true);
        _ensureEnabled!(); // Also handles Enable already true before this raid.
        return true;
    }

    public void Stop()
    {
        if (!_owned) return;
        _owned = false;
        SetTemporary(false);
    }

    private void SetTemporary(bool value)
    {
        if (_enable == null) return;
        var config = _enable.ConfigFile;
        bool save = config.SaveOnConfigSet;
        try { config.SaveOnConfigSet = false; _enable.Value = value; }
        finally { config.SaveOnConfigSet = save; }
    }
}
