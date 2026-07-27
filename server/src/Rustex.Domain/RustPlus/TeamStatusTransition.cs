namespace Rustex.Domain.RustPlus;

public enum TeamStatusTransition
{
    Online,
    Offline,
    Down,
    Revived,
}

/// <summary>Pure online/alive state-transition detection for Team Tracking, pulled out of
/// RustPlusTeamTrackingWorker so the decision table is testable without a protobuf-shaped member
/// or a database. Alive/dead is checked before online/offline — a member who logs off while dead
/// (the common case) should report as a death, not a departure, on the tick where both flags
/// change if the caller feeds pre-death-tick state; in the one-flag-flips-per-tick steady state
/// this worker actually sees, only one branch below can ever match.</summary>
public static class TeamStatusDetector
{
    public static TeamStatusTransition? Detect(bool wasOnline, bool wasAlive, bool isOnline, bool isAlive)
    {
        if (!wasAlive && isAlive) return TeamStatusTransition.Revived;
        if (wasAlive && !isAlive) return TeamStatusTransition.Down;
        if (!wasOnline && isOnline) return TeamStatusTransition.Online;
        if (wasOnline && !isOnline) return TeamStatusTransition.Offline;
        return null;
    }
}
