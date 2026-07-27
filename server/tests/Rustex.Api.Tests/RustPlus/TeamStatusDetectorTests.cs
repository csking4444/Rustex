using Rustex.Domain.RustPlus;
using Xunit;

namespace Rustex.Api.Tests.RustPlus;

public class TeamStatusDetectorTests
{
    [Fact]
    public void CameOnline_ReportsOnline()
    {
        var result = TeamStatusDetector.Detect(wasOnline: false, wasAlive: true, isOnline: true, isAlive: true);
        Assert.Equal(TeamStatusTransition.Online, result);
    }

    [Fact]
    public void WentOffline_ReportsOffline()
    {
        var result = TeamStatusDetector.Detect(wasOnline: true, wasAlive: true, isOnline: false, isAlive: true);
        Assert.Equal(TeamStatusTransition.Offline, result);
    }

    [Fact]
    public void Died_ReportsDown()
    {
        var result = TeamStatusDetector.Detect(wasOnline: true, wasAlive: true, isOnline: true, isAlive: false);
        Assert.Equal(TeamStatusTransition.Down, result);
    }

    [Fact]
    public void Respawned_ReportsRevived()
    {
        var result = TeamStatusDetector.Detect(wasOnline: true, wasAlive: false, isOnline: true, isAlive: true);
        Assert.Equal(TeamStatusTransition.Revived, result);
    }

    [Fact]
    public void NoChange_ReportsNull()
    {
        Assert.Null(TeamStatusDetector.Detect(wasOnline: true, wasAlive: true, isOnline: true, isAlive: true));
        Assert.Null(TeamStatusDetector.Detect(wasOnline: false, wasAlive: false, isOnline: false, isAlive: false));
    }

    [Fact]
    public void DeathTakesPriorityOverOfflineWhenBothFlip()
    {
        // A player dying and disconnecting in the same tick should report as a death, not a
        // departure — dying is the more actionable/urgent signal for a teammate to see.
        var result = TeamStatusDetector.Detect(wasOnline: true, wasAlive: true, isOnline: false, isAlive: false);
        Assert.Equal(TeamStatusTransition.Down, result);
    }

    [Fact]
    public void RevivedTakesPriorityOverOnlineWhenBothFlip()
    {
        var result = TeamStatusDetector.Detect(wasOnline: false, wasAlive: false, isOnline: true, isAlive: true);
        Assert.Equal(TeamStatusTransition.Revived, result);
    }
}
