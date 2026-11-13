using System;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using UnityEngine;

namespace Horizon;

// Keep the profile/camera alive for the native result screen, remove the body from the raid.
// Health and manual player ticks are suspended separately by the plugin while this is active.
internal sealed class ExtractedPlayer
{
    private Player? _player;
    private GameWorld? _world;
    private BotSpawner? _spawner;
    private Collider[] _colliders = Array.Empty<Collider>();
    private Renderer[] _renderers = Array.Empty<Renderer>();
    private bool[] _colliderStates = Array.Empty<bool>(), _rendererStates = Array.Empty<bool>();
    private bool _registered, _removedFromBots;
    private bool _bodyActive, _detachedCamera;
    private Transform? _camera, _cameraParent;

    public void Remove(GameWorld world, Player player)
    {
        _world = world;
        _player = player;
        _bodyActive = player.gameObject.activeSelf;
        _camera = player.GetComponent<PlayerCameraController>()?.Camera?.transform
            ?? throw new InvalidOperationException("Extraction spectator camera unavailable");
        _cameraParent = _camera.parent;
        if (_camera.IsChildOf(player.Transform.Original))
        {
            _detachedCamera = true;
            _camera.SetParent(null, true);
        }
        _registered = world.RegisteredPlayers.Contains(player);
        _colliders = player.GetComponentsInChildren<Collider>(true);
        _renderers = player.GetComponentsInChildren<Renderer>(true);
        _colliderStates = new bool[_colliders.Length];
        _rendererStates = new bool[_renderers.Length];
        for (int i = 0; i < _colliders.Length; i++) { _colliderStates[i] = _colliders[i].enabled; _colliders[i].enabled = false; }
        for (int i = 0; i < _renderers.Length; i++) { _rendererStates[i] = _renderers[i].enabled; _renderers[i].enabled = false; }
        _spawner = Singleton<IBotGame>.Instance?.BotsController?.BotSpawner;
        if (_spawner != null) { _removedFromBots = true; _spawner.DeletePlayer(player); }
        world.UnregisterPlayer(player);
        // SAIN also tracks activation independently of GameWorld's player lists.
        // Disable the extracted body, not its detached spectator camera or its profile.
        player.gameObject.SetActive(false);
    }

    public void Release(bool rollback)
    {
        try
        {
            if (_detachedCamera && _camera != null)
            {
                if (_cameraParent != null) _camera.SetParent(_cameraParent, true);
                else if (!rollback) UnityEngine.Object.Destroy(_camera.gameObject);
            }
            if (rollback && _player != null && _world != null)
            {
                _player.gameObject.SetActive(_bodyActive);
                for (int i = 0; i < _colliders.Length; i++) if (_colliders[i] != null) _colliders[i].enabled = _colliderStates[i];
                for (int i = 0; i < _renderers.Length; i++) if (_renderers[i] != null) _renderers[i].enabled = _rendererStates[i];
                if (_registered && !_world.RegisteredPlayers.Contains(_player)) _world.RegisterPlayer(_player);
                if (_removedFromBots) _spawner?.AddPlayer(_player);
            }
        }
        finally
        {
            _player = null; _world = null; _spawner = null;
            _colliders = Array.Empty<Collider>(); _renderers = Array.Empty<Renderer>();
            _colliderStates = _rendererStates = Array.Empty<bool>();
            _registered = _removedFromBots = false;
            _detachedCamera = false;
            _camera = _cameraParent = null;
        }
    }
}
