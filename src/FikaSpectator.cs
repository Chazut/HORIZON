using System;
using System.Reflection;
using EFT;
using UnityEngine;

namespace Horizon;

// Bind the camera and server settings from the loaded Fika build. Solo installs need no Fika assemblies.
internal sealed class FikaSpectator
{
    private const string Backend = "Fika.Core.Main.Utils.FikaBackendUtils";
    internal readonly Type ScriptType;
    private readonly Func<bool> _headless, _transit, _allowFree;
    internal readonly Func<bool> IsHost, AllowSpectateBots;
    private readonly PropertyInfo _active, _extracted;
    private readonly FieldInfo _observedAi, _following, _inputDisabled, _yaw, _pitch;
    private readonly MethodInfo _detach;

    private static Func<bool> ReadBool(Type type, string name, object? instance = null)
    {
        var getter = type.GetProperty(name)?.GetMethod ?? throw new MissingMemberException(type.FullName, name);
        return (Func<bool>)getter.CreateDelegate(typeof(Func<bool>), instance);
    }

    internal static bool IsHeadless(Assembly assembly) => ReadBool(assembly.GetType(Backend, true)!, "IsHeadless")();

    internal FikaSpectator(object plugin)
    {
        var type = plugin.GetType();
        var assembly = type.Assembly;
        var backend = assembly.GetType(Backend, true)!;
        _headless = ReadBool(backend, "IsHeadless");
        IsHost = ReadBool(backend, "IsServer");
        _transit = ReadBool(backend, "IsTransit");
        var settings = type.GetProperty("Settings")?.GetValue(plugin) ?? throw new MissingMemberException(type.FullName, "Settings");
        _allowFree = ReadBool(settings.GetType(), "AllowSpectateFreeCam", settings);
        AllowSpectateBots = ReadBool(settings.GetType(), "AllowSpectateBots", settings);
        var player = assembly.GetType("Fika.Core.Main.Players.FikaPlayer", true)!;
        _observedAi = player.GetField("IsObservedAI") ?? throw new MissingFieldException(player.FullName, "IsObservedAI");
        ScriptType = assembly.GetType("Fika.Core.Main.FreeCamera.FreeCamera", true)!;
        _active = ScriptType.GetProperty("IsActive") ?? throw new MissingMemberException(ScriptType.FullName, "IsActive");
        _extracted = ScriptType.GetProperty("Extracted") ?? throw new MissingMemberException(ScriptType.FullName, "Extracted");
        _detach = ScriptType.GetMethod("DetachCamera", Type.EmptyTypes) ?? throw new MissingMethodException(ScriptType.FullName, "DetachCamera");
        FieldInfo LookField(string name) => ScriptType.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingFieldException(ScriptType.FullName, name);
        _following = LookField("_isFollowing");
        _inputDisabled = LookField("_disableInput");
        _yaw = LookField("_yaw");
        _pitch = LookField("_pitch");
    }

    internal bool IsObservedBot(Player player) =>
        _observedAi.DeclaringType!.IsInstanceOfType(player) && (bool)_observedAi.GetValue(player)!;

    internal bool CanWatch(Behaviour script, Player? player, bool afterDeath, bool afterExtraction)
    {
        if (!(bool)_active.GetValue(script)! || (bool)_inputDisabled.GetValue(script)! ||
            _headless() || _transit() || !_allowFree()) return false;
        if (player == null || !player.IsYourPlayer || player.IsAI || player.HealthController == null) return false;
        return (bool)_extracted.GetValue(script)! ? afterExtraction : !player.HealthController.IsAlive && afterDeath;
    }

    internal void Align(Behaviour script, Quaternion rotation)
    {
        // Fika accumulates its own yaw/pitch. Resume native mouse look from the pose chosen by HORIZON.
        if ((bool)_following.GetValue(script)!) _detach.Invoke(script, null);
        var angles = rotation.eulerAngles;
        _yaw.SetValue(script, angles.y);
        _pitch.SetValue(script, Mathf.DeltaAngle(0, angles.x));
    }
}
