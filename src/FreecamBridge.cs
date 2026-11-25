using System;
using System.Reflection;
using EFT;
using EFT.CameraControl;
using UnityEngine;

namespace Horizon;

// Use Freecam's camera and movement implementation; do not clone it or teleport the dead body.
internal sealed class FreecamBridge
{
    private static FieldInfo _controllerField = null!, _activeField = null!;
    internal static Type SoloControllerType => _controllerField.FieldType;
    internal static Type SoloScriptType => _activeField.DeclaringType!;
    public Behaviour? Controller { get; private set; }
    public Behaviour? Script { get; private set; }
    public Camera? Camera { get; private set; }
    private Player? _player;
    private GamePlayerOwner? _owner;
    private GameObject? _hud;
    private bool _hudActive, _ownerEnabled, _scriptActive, _scriptEnabled;
    private EPointOfView _pointOfView;
    private CursorLockMode _cursorLock;
    private bool _cursorVisible;
    private DeathFade? _deathFade;
    private FastBlur? _deathBlur;
    private readonly SpectatorEffects _effects = new();
    private Action<Quaternion>? _alignFika;
    private bool _ownsCursor;
    private float? _originalFov;

    internal void SetSpectatorFov(bool enabled, float degrees)
    {
        if (Camera != null && enabled)
        {
            _originalFov ??= Camera.fieldOfView;
            if (Camera.fieldOfView != degrees) Camera.fieldOfView = degrees;
        }
        else if (_originalFov.HasValue)
        {
            if (Camera != null) Camera.fieldOfView = _originalFov.Value;
            _originalFov = null;
        }
    }
    public bool Ready => Script != null && Script.gameObject.activeInHierarchy &&
        (_alignFika != null ? Script.enabled && Camera != null : Controller != null);

    internal static void BindSolo(Type plugin)
    {
        _controllerField = plugin.GetField("FreecamControllerInstance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingFieldException(plugin.FullName, "FreecamControllerInstance");
        _activeField = plugin.Assembly.GetType("Terkoiz.Freecam.Freecam", true)!.GetField("IsActive")
            ?? throw new MissingFieldException("Freecam.IsActive");
    }

    internal void AttachFika(Behaviour script, Camera camera, Action<Quaternion> align)
    {
        Script = script;
        Camera = camera;
        _alignFika = align;
    }

    // Check readiness before deferring death without taking the camera away from Killcam.
    public static bool CanAttach(LocalGame game)
    {
        var player = game.LocalPlayer;
        var camera = player != null ? player.GetComponent<PlayerCameraController>()?.Camera : null;
        var script = camera != null ? camera.GetComponent(SoloScriptType) as Behaviour : null;
        return (_controllerField.GetValue(null) as Behaviour) != null && script != null &&
            script.gameObject.activeInHierarchy && game.PlayerOwner != null && game.GameUi != null;
    }

    public bool Attach(LocalGame game)
    {
        var controller = _controllerField.GetValue(null) as Behaviour;
        var player = game.LocalPlayer;
        var camera = player != null ? player.GetComponent<PlayerCameraController>()?.Camera : null;
        var script = camera != null ? camera.GetComponent(SoloScriptType) as Behaviour : null;
        if (controller == null || script == null || !script.gameObject.activeInHierarchy || player == null ||
            game.PlayerOwner == null || game.GameUi == null) return false;

        Controller = controller;
        Script = script;
        Camera = camera;
        _player = player;
        _owner = game.PlayerOwner;
        _ownerEnabled = _owner.enabled;
        _scriptActive = (bool)_activeField.GetValue(script)!;
        _scriptEnabled = script.enabled;
        _pointOfView = player.PointOfView;
        _cursorLock = Cursor.lockState;
        _cursorVisible = Cursor.visible;
        _hud = game.GameUi.gameObject;
        _hudActive = _hud.activeSelf;
        _deathFade = camera!.GetComponent<DeathFade>();
        _deathBlur = camera.GetComponent<FastBlur>();
        player.PointOfView = EPointOfView.FreeCamera;
        _owner.enabled = false;
        script.enabled = true;
        _activeField.SetValue(script, true);
        _hud.SetActive(false);
        ClearDeathEffects();
        _effects.Attach(camera);
        return true;
    }

    public void ClearDeathEffects()
    {
        // Called again after the death event has finished dispatching to all subscribers.
        if (_deathFade != null) _deathFade.DisableEffect();
        if (_deathBlur != null) _deathBlur.Reset();
    }

    public void MaintainEffects() => _effects.Maintain();

    public void SetMenu(bool open, bool following = false)
    {
        if (_alignFika != null)
        {
            if (open && !_ownsCursor)
            {
                _cursorLock = Cursor.lockState;
                _cursorVisible = Cursor.visible;
                _ownsCursor = true;
            }
            if (!open) { ReleaseCursor(); return; }
        }
        else if (Script != null) _activeField.SetValue(Script, !open && !following);
        Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = open;
    }

    public void MoveTo(Vector3 position, Quaternion rotation)
    {
        if (!Ready) return;
        _alignFika?.Invoke(rotation);
        Script!.transform.SetPositionAndRotation(position, rotation);
    }

    public void Zoom(float wheel, float speed)
    {
        if (!Ready) return;
        _alignFika?.Invoke(Script!.transform.rotation);
        Script!.transform.position += Script.transform.forward * (wheel * speed);
    }

    public void Release(bool ending = false)
    {
        SetSpectatorFov(false, 0);
        if (_alignFika != null)
        {
            ReleaseCursor();
            _alignFika = null;
            Camera = null;
            Script = null;
            return;
        }
        _effects.Restore();
        // Also used when entering spectator mode fails: restore exactly what we touched.
        if (Script != null)
        {
            _activeField.SetValue(Script, !ending && _scriptActive);
            Script.enabled = _scriptEnabled;
        }
        if (_player != null) _player.PointOfView = _pointOfView;
        if (_owner != null) _owner.enabled = _ownerEnabled;
        if (_hud != null) _hud.SetActive(_hudActive);
        if (Controller != null || Script != null)
        {
            Cursor.lockState = _cursorLock;
            Cursor.visible = _cursorVisible;
        }
        Controller = null;
        Script = null;
        Camera = null;
        _owner = null;
        _player = null;
        _hud = null;
        _deathFade = null;
        _deathBlur = null;
    }

    private void ReleaseCursor()
    {
        if (!_ownsCursor) return;
        Cursor.lockState = _cursorLock;
        Cursor.visible = _cursorVisible;
        _ownsCursor = false;
    }
}
