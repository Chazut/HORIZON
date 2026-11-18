using System;
using System.Collections.Generic;
using EFT;
using UnityEngine;

namespace Horizon;

internal enum FollowTransition { None, Detached, Switched }

internal sealed class PmcNavigator
{
    private readonly PmcCycle _cycle = new();
    private readonly List<string> _ids = new();
    private readonly Dictionary<string, Player> _players = new();
    public string Caption { get; private set; } = "Wheel: previous / next PMC";
    private BotFilter _filter;
    private Player? _target;
    public bool Following { get; private set; }
    public bool LockOrientation { get; private set; }
    public string? SelectedId => _target != null ? _target.ProfileId : null;
    private float _yaw, _pitch;
    private float _configuredDistance = float.NaN, _distance;

    private float Distance(float configured)
    {
        if (_configuredDistance != configured) { _configuredDistance = configured; _distance = configured; }
        return _distance;
    }

    public void Zoom(float wheel, float speed, float configuredDistance)
    {
        if (Following) _distance = Math.Max(1f, Math.Min(10f, Distance(configuredDistance) - wheel * speed));
    }

    public void ToggleOrientation()
    {
        LockOrientation = !LockOrientation;
        if (!LockOrientation && _target != null) SeedOrbit(_target);
    }

    public void Rotate(float horizontal, float vertical, float sensitivity)
    {
        if (!Following || LockOrientation) return;
        _yaw = (_yaw + horizontal * sensitivity) % 360f;
        _pitch = Math.Max(-70f, Math.Min(75f, _pitch - vertical * sensitivity));
    }

    private void SeedOrbit(Player target)
    {
        var direction = target.LookDirection;
        _yaw = (float)(Math.Atan2(direction.x, direction.z) * 180 / Math.PI);
        _pitch = 12f;
    }

    public void ToggleFollow(GameWorld world, FreecamBridge camera, float distance)
    {
        if (Following) { Following = false; return; }
        if (!TargetAlive(world)) Move(world, camera, 1, distance);
        Following = TargetAlive(world);
        if (Following)
        {
            LockOrientation = false;
            SeedOrbit(_target!);
        }
    }

    private bool TargetAlive(GameWorld world) => _target != null && _target.HealthController?.IsAlive == true &&
        _target.gameObject.activeInHierarchy && world.AllAlivePlayersList.Contains(_target);

    public FollowTransition Follow(GameWorld world, FreecamBridge camera, float distance, bool autoSwitch = false)
    {
        if ((Following || !ReferenceEquals(_target, null)) && !TargetAlive(world))
        {
            bool wasFollowing = Following;
            _target = null;
            if (wasFollowing && autoSwitch)
            {
                Move(world, camera, 1, distance);
                if (TargetAlive(world)) return FollowTransition.Switched;
            }
            Following = false;
            Caption = "Target lost | Free camera";
            return wasFollowing ? FollowTransition.Detached : FollowTransition.None;
        }
        if (!Following || _target == null) return FollowTransition.None;
        Place(camera, _target!, distance);
        return FollowTransition.None;
    }

    internal static bool IsLivingBot(Player player) => player != null && player.IsAI && !player.IsYourPlayer &&
        player.HealthController?.IsAlive == true && player.gameObject.activeInHierarchy && !string.IsNullOrEmpty(player.ProfileId);

    // The list deliberately includes all bot types, independently of the wheel filter.
    public bool Visit(GameWorld world, FreecamBridge camera, Player target, float distance)
    {
        if (!IsLivingBot(target) || !world.AllAlivePlayersList.Contains(target)) return false;
        _target = target;
        _cycle.Select(target.ProfileId);
        SeedOrbit(target);
        Place(camera, target, distance);
        Caption = target.Profile.Nickname;
        return true;
    }

    public void SetFilter(BotFilter filter)
    {
        if (_filter == filter) return;
        _filter = filter;
        Reset();
    }

    public void Move(GameWorld world, FreecamBridge camera, int direction, float distance = 2.5f)
    {
        _ids.Clear();
        _players.Clear();
        foreach (var player in world.AllAlivePlayersList)
        {
            if (!IsLivingBot(player)) continue;
            var role = player.Profile?.Info?.Settings?.Role;
            if (_filter != BotFilter.AllBots && role != WildSpawnType.pmcBEAR && role != WildSpawnType.pmcUSEC) continue;
            if (string.IsNullOrEmpty(player.ProfileId)) continue;
            if (_players.ContainsKey(player.ProfileId)) continue;
            _players.Add(player.ProfileId, player);
            _ids.Add(player.ProfileId);
        }
        var id = _cycle.Next(_ids, direction);
        if (id == null) { _target = null; Following = false; Caption = _filter == BotFilter.AllBots ? "No living bots to visit" : "No living PMCs to visit"; return; }
        var target = _players[id];
        _target = target;
        SeedOrbit(target);
        Place(camera, target, distance);
        Caption = $"{target.Profile.Nickname} ({_ids.IndexOf(id) + 1}/{_ids.Count})";
    }

    private void Place(FreecamBridge camera, Player target, float distance)
    {
        distance = Distance(distance);
        Vector3 focus = target.Position + Vector3.up * 1.5f;
        Vector3 direction = target.LookDirection;
        if (!LockOrientation && Following)
        {
            double yaw = _yaw * Math.PI / 180, pitch = _pitch * Math.PI / 180;
            direction = new Vector3((float)(Math.Sin(yaw) * Math.Cos(pitch)), (float)-Math.Sin(pitch), (float)(Math.Cos(yaw) * Math.Cos(pitch)));
        }
        Vector3 position = focus - direction * distance + Vector3.up * 0.6f;
        // Avoid placing the camera behind a wall when visiting an indoor PMC.
        if (Physics.Linecast(focus, position, out var hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
            !hit.transform.IsChildOf(target.Transform.Original)) position = hit.point + (focus - hit.point).normalized * 0.2f;
        Vector3 look = focus - position;
        camera.MoveTo(position, look.sqrMagnitude > 0.001f ? Quaternion.LookRotation(look) : target.Transform.rotation);
    }

    public void Reset()
    {
        _cycle.Reset(); _ids.Clear(); _players.Clear();
        _target = null; Following = false;
        LockOrientation = false; _yaw = _pitch = 0;
        _configuredDistance = float.NaN;
        Caption = _filter == BotFilter.AllBots ? "Wheel: previous / next bot" : "Wheel: previous / next PMC";
    }
}
