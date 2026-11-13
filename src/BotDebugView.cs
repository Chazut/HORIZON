using System;
using Comfort.Common;
using EFT;
using UnityEngine;

namespace Horizon;

// Display-only access: the extracted player must stay unregistered and untargetable.
internal sealed class BotDebugView
{
    private GameWorld? _world;
    private Player? _observer;
    private Camera? _camera;
    private bool _reported, _failed;
    private readonly Action<string> _info, _warning;

    public BotDebugView(Action<string> info, Action<string> warning) { _info = info; _warning = warning; }
    public Camera? Camera => _world != null && ReferenceEquals(_world, Singleton<GameWorld>.Instance) &&
        _camera != null && _camera.isActiveAndEnabled ? _camera : null;

    public void Attach(GameWorld world, Player observer, Camera camera)
    {
        _world = world; _observer = observer; _camera = camera;
        _reported = _failed = false;
    }

    public DebugBotStruct Read(BotSpawner spawner, IPlayer observer, string profileId)
    {
        if (Camera == null) return spawner.GetBotDebugData(observer, profileId);
        try
        {
            var player = _world!.GetAlivePlayerByProfileID(profileId);
            var owner = player?.AIData?.BotOwner;
            if (player == null || !player.IsAI || player.HealthController?.IsAlive != true || owner == null || _observer == null)
                return default;
            // The native accessor returns default when BotSpawner has no humans left.
            // Construct the same native snapshot from the living bot, without adding
            // the spectator back to the bot spawner or altering the bot's state.
            var data = new DebugBotStruct(owner, 0f, _observer);
            if (data.InitedBotData && !_reported)
            {
                _reported = true;
                _info("BotDebug spectator data active; overlay positions and distances use Freecam.");
            }
            return data;
        }
        catch (Exception e)
        {
            if (!_failed) _warning("BotDebug spectator snapshot failed: " + e);
            _failed = true;
            return default;
        }
    }

    public Vector3 Origin(Player player) => Camera is Camera camera ? camera.transform.position : player.Transform.position;
    public void Clear() { _world = null; _observer = null; _camera = null; _reported = _failed = false; }
}
