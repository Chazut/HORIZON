using System;

namespace RaidSpectator;

internal enum SessionPhase { Idle, Watching, Ending }
internal enum StopDecision { Original, Defer, Killed }

// The death outcome belongs to one local game and one profile. No health or bot state is modified.
internal sealed class SpectatorSession
{
    public SessionPhase Phase { get; private set; }
    public string ProfileId { get; private set; } = "";
    public float PlayedSeconds { get; private set; }
    public TimeSpan PlayedDuration { get; private set; }
    public bool Watching => Phase == SessionPhase.Watching;

    public bool Begin(bool eligible, bool dead, bool cameraReady, string profileId,
        float playedSeconds, DateTime sessionStart, DateTime deathTime)
    {
        if (Phase != SessionPhase.Idle || !eligible || !dead || !cameraReady ||
            string.IsNullOrEmpty(profileId) || float.IsNaN(playedSeconds) || float.IsInfinity(playedSeconds))
            return false;
        ProfileId = profileId;
        PlayedSeconds = Math.Max(0, playedSeconds);
        PlayedDuration = deathTime > sessionStart ? deathTime - sessionStart : TimeSpan.Zero;
        Phase = SessionPhase.Watching;
        return true;
    }

    public bool RequestExit()
    {
        if (!Watching) return false;
        Phase = SessionPhase.Ending;
        return true;
    }

    public StopDecision DecideStop(bool sameGame, string profileId, bool killed)
    {
        if (!sameGame || Phase == SessionPhase.Idle || profileId != ProfileId) return StopDecision.Original;
        // Repeated death notifications must not terminate an established spectator session.
        if (Watching && killed) return StopDecision.Defer;
        Phase = SessionPhase.Ending;
        return StopDecision.Killed;
    }

    public DateTime StartForSave(DateTime now) => now - PlayedDuration;

    public void Reset()
    {
        Phase = SessionPhase.Idle;
        ProfileId = "";
        PlayedSeconds = 0;
        PlayedDuration = TimeSpan.Zero;
    }
}
