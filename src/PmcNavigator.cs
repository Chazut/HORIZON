using System.Collections.Generic;
using EFT;
using UnityEngine;

namespace RaidSpectator;

internal sealed class PmcNavigator
{
    private readonly PmcCycle _cycle = new();
    private readonly List<string> _ids = new();
    private readonly Dictionary<string, Player> _players = new();
    public string Caption { get; private set; } = "Wheel: previous / next PMC";

    public void Move(GameWorld world, FreecamBridge camera, int direction)
    {
        _ids.Clear();
        _players.Clear();
        foreach (var player in world.AllAlivePlayersList)
        {
            if (player == null || !player.IsAI || player.IsYourPlayer || player.HealthController?.IsAlive != true ||
                !player.gameObject.activeInHierarchy) continue;
            var role = player.Profile?.Info?.Settings?.Role;
            if (role != WildSpawnType.pmcBEAR && role != WildSpawnType.pmcUSEC) continue;
            if (_players.ContainsKey(player.ProfileId)) continue;
            _players.Add(player.ProfileId, player);
            _ids.Add(player.ProfileId);
        }
        var id = _cycle.Next(_ids, direction);
        if (id == null) { Caption = "No living PMCs to visit"; return; }
        var target = _players[id];
        Vector3 focus = target.Position + Vector3.up * 1.5f;
        Vector3 position = focus - target.Transform.forward * 2.5f + Vector3.up * 0.6f;
        // Avoid placing the camera behind a wall when visiting an indoor PMC.
        if (Physics.Linecast(focus, position, out var hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
            !hit.transform.IsChildOf(target.Transform.Original)) position = hit.point + (focus - hit.point).normalized * 0.2f;
        Vector3 look = focus - position;
        camera.MoveTo(position, look.sqrMagnitude > 0.001f ? Quaternion.LookRotation(look) : target.Transform.rotation);
        Caption = $"{target.Profile.Nickname} ({_ids.IndexOf(id) + 1}/{_ids.Count})";
    }

    public void Reset() { _cycle.Reset(); _ids.Clear(); _players.Clear(); Caption = "Wheel: previous / next PMC"; }
}
