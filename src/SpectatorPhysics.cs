using System;
using System.Diagnostics;

namespace RaidSpectator;

// EFT normally syncs PhysX transforms from the LOCAL player's ArmsLateUpdate.
// That source disappears when the extracted body is deactivated (or hands are gone
// after death). Simulation is also allowed to stop when no supported rigidbody is
// visible. Neither a spectator camera nor awake AI replaces the missing sync.
internal sealed class SpectatorPhysics
{
    private int _frame = -1;
    private float _fixedTime = float.NaN;
    public int Count { get; private set; }
    public double TotalMs { get; private set; }
    public double MaxMs { get; private set; }

    public bool Sync(bool watching, bool automatic, int frame, bool fixedStep, float fixedTime, Action synchronize)
    {
        if (!watching || automatic) return false;
        if (fixedStep ? _fixedTime == fixedTime : _frame == frame) return false;
        long started = Stopwatch.GetTimestamp();
        synchronize();
        // A failed call must remain retryable; do not mark it as synchronized.
        if (fixedStep) _fixedTime = fixedTime;
        else _frame = frame;
        double ms = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
        Count++;
        TotalMs += ms;
        MaxMs = Math.Max(MaxMs, ms);
        return true;
    }

    public void Reset()
    {
        _frame = -1;
        _fixedTime = float.NaN;
        Count = 0;
        TotalMs = MaxMs = 0;
    }
}
