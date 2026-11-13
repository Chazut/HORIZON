using System;

namespace Horizon;

internal enum SessionPhase { Idle, Watching, Ending }
internal enum StopDecision { Original, Defer, Captured }
internal enum WatchOutcome { Killed, Survived, Runner }

// Preserve the completed player's result, never substitute time spent observing.
internal sealed class SpectatorSession
{
    public SessionPhase Phase { get; private set; }
    public string ProfileId { get; private set; } = "";
    public float PlayedSeconds { get; private set; }
    public TimeSpan PlayedDuration { get; private set; }
    public bool Watching => Phase == SessionPhase.Watching;
    public WatchOutcome Outcome { get; private set; }
    public string? ExitName { get; private set; }
    public bool Extracted => Outcome != WatchOutcome.Killed && Phase != SessionPhase.Idle;

    public bool Begin(bool eligible, bool completed, bool cameraReady, string profileId,
        float playedSeconds, DateTime sessionStart, DateTime completionTime,
        WatchOutcome outcome = WatchOutcome.Killed, string? exitName = null)
    {
        if (Phase != SessionPhase.Idle || !eligible || !completed || !cameraReady ||
            string.IsNullOrEmpty(profileId) || float.IsNaN(playedSeconds) || float.IsInfinity(playedSeconds))
            return false;
        ProfileId = profileId;
        PlayedSeconds = Math.Max(0, playedSeconds);
        PlayedDuration = completionTime > sessionStart ? completionTime - sessionStart : TimeSpan.Zero;
        Outcome = outcome;
        ExitName = outcome == WatchOutcome.Killed ? null : exitName;
        Phase = SessionPhase.Watching;
        return true;
    }

    public bool RequestExit()
    {
        if (!Watching) return false;
        Phase = SessionPhase.Ending;
        return true;
    }

    public StopDecision DecideStop(bool sameGame, string profileId, bool completionNotification)
    {
        if (!sameGame || Phase == SessionPhase.Idle || profileId != ProfileId) return StopDecision.Original;
        // Repeated death/extraction notifications must not terminate an established session.
        if (Watching && completionNotification) return StopDecision.Defer;
        Phase = SessionPhase.Ending;
        return StopDecision.Captured;
    }

    public DateTime StartForSave(DateTime now) => now - PlayedDuration;

    public void Reset()
    {
        Phase = SessionPhase.Idle;
        ProfileId = "";
        PlayedSeconds = 0;
        PlayedDuration = TimeSpan.Zero;
        Outcome = WatchOutcome.Killed;
        ExitName = null;
    }
}
