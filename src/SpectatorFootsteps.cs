using System;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using HarmonyLib;
using UnityEngine;

namespace Horizon;

// Observe the native sound bank used by walking, sprinting and landing, including
// silent windows. The callback never changes a source, a listener or a clip.
internal sealed class SpectatorFootsteps
{
    private FieldInfo? _stepField;
    private static SpectatorFootsteps? _watch;
    private Player? _target;
    private BetterSource? _source;
    private string? _targetId, _targetName;
    private bool _installAttempted;
    private bool _followed, _waited;
    private float _nextSample, _nextReport, _travel, _bankDistance, _bankVolume;
    private int _calls, _outOfRange, _surfaceChecks, _surfaceRejected;
    private float _lastRange;
    private Vector3 _last;
    public bool Active => ReferenceEquals(_watch, this);

    private void Install(Harmony harmony)
    {
        var field = AccessTools.Field(typeof(Player), "NestedStepSoundSource")
            ?? throw new MissingFieldException("Player.NestedStepSoundSource");
        harmony.Patch(AccessTools.DeclaredMethod(typeof(SoundBank), nameof(SoundBank.Play)),
            postfix: new HarmonyMethod(typeof(SpectatorFootsteps), nameof(BankPlayed)));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(Player), "CheckSurface", new[] { typeof(float) }),
            postfix: new HarmonyMethod(typeof(SpectatorFootsteps), nameof(SurfaceChecked)));
        _stepField = field;
    }

    public void Begin(Harmony harmony, bool waited)
    {
        Reset();
        if (!_installAttempted)
        {
            _installAttempted = true;
            Install(harmony);
        }
        _waited = waited;
        if (_stepField != null) _watch = this;
    }

    private static void BankPlayed(BetterSource __0, float __2, float __3)
    {
        var watch = _watch;
        if (watch == null || __0 == null || !ReferenceEquals(watch._source, __0)) return;
        watch._calls++;
        watch._bankDistance = __2;
        watch._bankVolume = __3;
        if (__2 > __0.MaxDistance) watch._outOfRange++;
    }

    private static void SurfaceChecked(Player __instance, float __0, bool __result)
    {
        var watch = _watch;
        if (watch == null || !ReferenceEquals(watch._target, __instance)) return;
        watch._surfaceChecks++;
        watch._lastRange = __0;
        if (!__result) watch._surfaceRejected++;
    }

    private void ClearWindow()
    {
        _travel = 0;
        _calls = _outOfRange = _surfaceChecks = _surfaceRejected = 0;
        _lastRange = 0;
    }

    public void Observe(GameWorld world, Camera camera, Player? followed, FollowTransition transition, Action<string> log)
    {
        float now = Time.unscaledTime;
        bool following = followed != null;
        if (transition == FollowTransition.None && _followed == following &&
            (!following || ReferenceEquals(followed, _target)) && now < _nextSample) return;
        _nextSample = now + 1f;
        var origin = camera.transform.position;
        var target = followed;
        if (target == null)
        {
            float nearest = float.MaxValue;
            foreach (var player in world.AllAlivePlayersList)
            {
                if (!PmcNavigator.IsLivingBot(player)) continue;
                float sq = (player.Position - origin).sqrMagnitude;
                if (sq >= nearest) continue;
                nearest = sq;
                target = player;
            }
        }
        if (transition != FollowTransition.None || !ReferenceEquals(_target, target) || _followed != following)
        {
            if (_targetId != null && (transition != FollowTransition.None || _calls > 0 || _travel > 0 || _surfaceChecks > 0))
                Report(camera, transition == FollowTransition.None ? "target-left" : $"follow-{transition}", log);
            _target = target;
            _targetId = target != null ? target.ProfileId : null;
            _targetName = target != null ? target.Profile?.Nickname : null;
            _followed = following;
            _last = target != null ? target.Position : origin;
            ClearWindow();
            _nextReport = 0;
        }
        if (target != null)
        {
            _travel += Vector3.Distance(_last, target.Position);
            _last = target.Position;
        }
        _source = target == null ? null : _stepField!.GetValue(target) as BetterSource;
        if (now < _nextReport) return;
        Report(camera, _nextReport == 0 ? "camera" : "window", log);
        _nextReport = now + 10f;
        ClearWindow();
    }

    private void Report(Camera camera, string reason, Action<string> log)
    {
        var origin = camera.transform.position;
        var manager = Singleton<AudioListenerConsistencyManager>.Instance;
        var listener = manager != null ? manager.GetComponent<AudioListener>() : null;
        var source = _source != null ? _source : null; // A dead Unity object can still have a managed reference.
        var audio = source != null && source.source1 != null ? source.source1 : null;
        var nativeCamera = CameraManager.Exist ? CameraManager.Instance.Camera : null;
        var betterAudio = MonoBehaviourSingleton<BetterAudio>.Instantiated ? MonoBehaviourSingleton<BetterAudio>.Instance : null;
        var mixer = betterAudio != null ? betterAudio.Master : null;
        var data = betterAudio != null ? betterAudio.AudioMixerData : null;
        string Level(string? parameter) => mixer != null && !string.IsNullOrEmpty(parameter) && mixer.GetFloat(parameter, out var value) ? $"{value:F1}" : "unknown";
        string bank = _calls == 0 ? "none" : $"distance={_bankDistance:F1} volume={_bankVolume:F2}";
        log($"Spectator footsteps: {reason} waitedForKillcam={_waited} target={_targetId} name={_targetName} followed={_followed} " +
            $"camera={origin:F1} listener={listener?.transform.position:F1} listenerEnabled={listener?.isActiveAndEnabled} listenerPaused={AudioListener.pause} listenerVolume={AudioListener.volume:F2} " +
            $"cameraListenerGap={(listener == null ? -1 : Vector3.Distance(origin, listener.transform.position)):F1}m " +
            $"nativeCameraGap={(nativeCamera == null ? -1 : Vector3.Distance(origin, nativeCamera.transform.position)):F1}m " +
            $"mainDb={Level(data?.MainMixerVolume)} worldDb={Level(data?.WorldMixerVolume)} lowpassHz={Level(data?.WorldMixerLowpassFreq)} " +
            $"botCameraGap={(_target == null ? -1 : Vector3.Distance(origin, _target.Position)):F1}m botTravel={_travel:F1}m " +
            $"surfaceChecks={_surfaceChecks} rangeRejected={_surfaceRejected} lastSpreadRange={_lastRange:F1} stepBankCalls={_calls} outOfRange={_outOfRange} lastBank=[{bank}] source={source?.isActiveAndEnabled} " +
            $"rolloff={source?.MaxDistance:F1} occlusion={source?.OcclusionVolumeFactor:F2} audioEnabled={audio?.isActiveAndEnabled} " +
            $"mute={audio?.mute} volume={audio?.volume:F2} playingAtSample={audio?.isPlaying}");
    }

    public void Reset()
    {
        if (Active) _watch = null;
        _target = null;
        _targetId = _targetName = null;
        _source = null;
        ClearWindow();
        _nextSample = _nextReport = 0;
        _followed = false;
    }
}
