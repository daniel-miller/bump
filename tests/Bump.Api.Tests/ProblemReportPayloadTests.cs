namespace Bump.Api.Tests;

// Version is optional on the wire: a consumer on a client that predates it must
// keep reporting, and one that sends it must not be able to push an unbounded
// string into a varchar(100).
public class ProblemReportPayloadTests
{
    private static ProblemReportPayload Payload(string? version) =>
        new()
        {
            Environment = "live",
            Application = "shop",
            Type = "System.TimeoutException",
            Title = "TimeoutException",
            Version = version,
        };

    [Fact]
    public void Validate_AcceptsAReportWithNoVersion()
    {
        Assert.Null(Payload(null).Validate());
    }

    [Fact]
    public void Validate_AcceptsASemverWithACommitSuffix()
    {
        Assert.Null(Payload("1.3.174+db890ee").Validate());
    }

    [Fact]
    public void Validate_AcceptsAVersionAtTheCap()
    {
        Assert.Null(Payload(new string('9', Limits.AppVersionMaxLength)).Validate());
    }

    [Fact]
    public void Validate_RejectsAVersionOverTheCap()
    {
        Assert.NotNull(Payload(new string('9', Limits.AppVersionMaxLength + 1)).Validate());
    }
}
