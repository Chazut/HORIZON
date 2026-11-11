using EFT;
using EFT.CameraControl;
using Terkoiz.Freecam;
using UnityEngine;
using CameraScript = Terkoiz.Freecam.Freecam;

namespace RaidSpectator;

// Use Freecam's camera and movement implementation; do not clone it or teleport the dead body.
internal sealed class FreecamBridge
{
    public FreecamController? Controller { get; private set; }
    public CameraScript? Script { get; private set; }
    private Player? _player;
    private GamePlayerOwner? _owner;
    private GameObject? _hud;
    private bool _hudActive, _ownerEnabled, _scriptActive, _scriptEnabled;
    private EPointOfView _pointOfView;
    private CursorLockMode _cursorLock;
    private bool _cursorVisible;
    private DeathFade? _deathFade;
    private FastBlur? _deathBlur;
    public bool Ready => Controller != null && Script != null && Script.gameObject.activeInHierarchy;

    // Check readiness before deferring death without taking the camera away from Killcam.
    public static bool CanAttach(LocalGame game)
    {
        var player = game.LocalPlayer;
        var camera = player != null ? player.GetComponent<PlayerCameraController>()?.Camera : null;
        var script = camera != null ? camera.GetComponent<CameraScript>() : null;
        return FreecamPlugin.FreecamControllerInstance != null && script != null &&
            script.gameObject.activeInHierarchy && game.PlayerOwner != null && game.GameUi != null;
    }

    public bool Attach(LocalGame game)
    {
        var controller = FreecamPlugin.FreecamControllerInstance;
        var player = game.LocalPlayer;
        var camera = player != null ? player.GetComponent<PlayerCameraController>()?.Camera : null;
        var script = camera != null ? camera.GetComponent<CameraScript>() : null;
        if (controller == null || script == null || !script.gameObject.activeInHierarchy || player == null ||
            game.PlayerOwner == null || game.GameUi == null) return false;

        Controller = controller;
        Script = script;
        _player = player;
        _owner = game.PlayerOwner;
        _ownerEnabled = _owner.enabled;
        _scriptActive = script.IsActive;
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
        script.IsActive = true;
        _hud.SetActive(false);
        ClearDeathEffects();
        return true;
    }

    public void ClearDeathEffects()
    {
        // Called again after the death event has finished dispatching to all subscribers.
        if (_deathFade != null) _deathFade.DisableEffect();
        if (_deathBlur != null) _deathBlur.Reset();
    }

    public void SetMenu(bool open)
    {
        if (Script != null) Script.IsActive = !open;
        Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = open;
    }

    public void Release(bool ending = false)
    {
        // Also used when entering spectator mode fails: restore exactly what we touched.
        if (Script != null)
        {
            Script.IsActive = !ending && _scriptActive;
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
        _owner = null;
        _player = null;
        _hud = null;
        _deathFade = null;
        _deathBlur = null;
    }
}
