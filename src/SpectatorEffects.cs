using System;
using System.Collections.Generic;
using UnityEngine;

namespace Horizon;

// Only effects attached to the spectator camera and driven by the local player.
// Keep world lighting, weather, colour grading and graphics mods intact.
internal sealed class SpectatorEffects
{
    private static readonly HashSet<string> PlayerEffects = new(StringComparer.Ordinal)
    {
        "EffectsController", "DeathFade", "FastBlur", "BloodOnScreen", "BloodOnScreenOld",
        "PainScreen", "EyeBurn", "VisorEffect", "GrenadeFlashScreenEffect", "FrostbiteEffect",
        "CC_FastVignette", "CC_DoubleVision", "CC_HueFocus", "CC_RadialBlur", "CC_Blend", "CC_Wiggle",
        "NightVision", "ThermalVision"
    };
    private readonly Dictionary<Behaviour, bool> _original = new();
    private Camera? _camera;
    private float _nextDiscovery;

    internal static bool IsPlayerEffect(Type type) =>
        type.Assembly == typeof(DeathFade).Assembly && PlayerEffects.Contains(type.Name);

    public void Attach(Camera camera)
    {
        Restore();
        _camera = camera;
        _nextDiscovery = 0;
        Maintain();
    }

    public void Maintain()
    {
        if (_camera == null) return;
        // Effects can be added by the death/extraction callbacks after attachment.
        if (Time.unscaledTime >= _nextDiscovery)
        {
            _nextDiscovery = Time.unscaledTime + 1f;
            foreach (var effect in _camera.GetComponentsInChildren<Behaviour>(true))
                if (effect != null && IsPlayerEffect(effect.GetType()) && !_original.ContainsKey(effect))
                    _original.Add(effect, effect.enabled);
        }
        foreach (var pair in _original)
        {
            if (pair.Key == null) continue;
            if (pair.Key.enabled) pair.Key.enabled = false;
            // Disabling EFT's flash component restores tone mapping but leaves this
            // global shader value behind until the camera is destroyed.
            if (pair.Key is GrenadeFlashScreenEffect flash)
            {
                flash.EffectStrength = 0f;
                Shader.SetGlobalFloat("_BlindnessCoef", 0f);
            }
        }
    }

    public void Restore()
    {
        foreach (var pair in _original)
            if (pair.Key != null) pair.Key.enabled = pair.Value;
        _original.Clear();
        _camera = null;
    }
}
