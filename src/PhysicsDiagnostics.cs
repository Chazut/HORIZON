using System;
using System.Collections.Generic;
using EFT;
using UnityEngine;

namespace RaidSpectator;

// Sparse observation only. Never teleport a bot, change its orders, enable a
// disabled controller, or turn on all map colliders/renderers to hide a failure.
internal sealed class PhysicsDiagnostics
{
    private sealed class Sample
    {
        public Vector3 Position;
        public float StillSince, NextWarning;
    }
    private readonly Dictionary<string, Sample> _samples = new();
    private readonly HashSet<string> _present = new();
    private readonly List<string> _removed = new();
    private float _nextSample, _nextSummary;

    public void Observe(GameWorld world, SpectatorPhysics sync, Action<string> info, Action<string> warning)
    {
        float now = Time.realtimeSinceStartup;
        if (now < _nextSample) return;
        _nextSample = now + 5f;
        _present.Clear();
        int alive = 0, drifted = 0;
        foreach (var player in world.AllAlivePlayersList)
        {
            if (player == null || !player.IsAI || player.HealthController?.IsAlive != true || !player.gameObject.activeInHierarchy) continue;
            alive++;
            var id = player.ProfileId;
            _present.Add(id);
            var position = player.Position;
            if (!_samples.TryGetValue(id, out var sample))
                _samples[id] = sample = new Sample { Position = position, StillSince = now };
            var delta = position - sample.Position;
            bool falling = delta.y < -15f;
            if (delta.sqrMagnitude > 0.25f) sample.StillSince = now;
            sample.Position = position;
            var controller = player.CharacterController;
            var collider = controller?.GetCollider();
            float drift = collider is CapsuleCollider capsule && capsule.enabled && capsule.gameObject.activeInHierarchy
                ? (capsule.bounds.center - capsule.transform.TransformPoint(capsule.center)).magnitude : 0f;
            if (drift > 0.25f) drifted++;
            var mover = player.AIData?.BotOwner?.Mover;
            bool stuck = now - sample.StillSince >= 15f && mover != null && mover.IsMoving;
            if ((falling || stuck || drift > 0.25f) && now >= sample.NextWarning)
            {
                sample.NextWarning = now + 30f;
                int mask = player.MovementContext.GroundMask;
                bool support = Physics.Raycast(position + Vector3.up * 0.5f, Vector3.down, out var hit, 2f, mask, QueryTriggerInteraction.Ignore);
                warning($"Spectator physics anomaly: bot={player.Profile.Nickname} profile={id} pos={position:F2} falling={falling} still={now - sample.StillSince:F1}s moving={mover?.IsMoving} controller={controller?.GetType().Name} enabled={controller?.isEnabled} grounded={controller?.isGrounded} colliderDrift={drift:F3}m support={support} ground={(support ? hit.collider.name : "none")} autoSync={Physics.autoSyncTransforms} simulation={PhysicsExtensions.Simulation.UpdateEnabled}. No tactical override applied.");
            }
        }
        _removed.Clear();
        foreach (var id in _samples.Keys) if (!_present.Contains(id)) _removed.Add(id);
        foreach (var id in _removed) _samples.Remove(id);
        if (now >= _nextSummary)
        {
            _nextSummary = now + 30f;
            info($"Spectator physics: aliveAI={alive} staleBounds={drifted} syncs={sync.Count} meanMs={(sync.Count > 0 ? sync.TotalMs / sync.Count : 0):F3} maxMs={sync.MaxMs:F3} autoSync={Physics.autoSyncTransforms} simulation={PhysicsExtensions.Simulation.UpdateEnabled}.");
        }
    }

    public void Reset() { _samples.Clear(); _present.Clear(); _removed.Clear(); _nextSample = _nextSummary = 0; }
}
