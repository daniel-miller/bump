namespace Bump.Api.Tests;

public class FingerprintTests
{
    private static ProblemReportPayload Payload(
        string environment = "live",
        string application = "shop",
        string type = "https://example.com/errors/timeout",
        string title = "Request timed out") =>
        new() { Environment = environment, Application = application, Type = type, Title = title };

    [Fact]
    public void Compute_IsDeterministic()
    {
        Assert.Equal(Fingerprint.Compute(Payload()), Fingerprint.Compute(Payload()));
    }

    [Fact]
    public void Compute_Returns16LowercaseHexChars()
    {
        var fp = Fingerprint.Compute(Payload());
        Assert.Equal(16, fp.Length);
        Assert.Matches("^[0-9a-f]{16}$", fp);
    }

    [Theory]
    [InlineData("test", "shop", "https://example.com/errors/timeout", "Request timed out")]
    [InlineData("live", "api", "https://example.com/errors/timeout", "Request timed out")]
    [InlineData("live", "shop", "https://example.com/errors/db", "Request timed out")]
    [InlineData("live", "shop", "https://example.com/errors/timeout", "Connection refused")]
    public void Compute_ChangesWhenAnyComponentChanges(
        string environment, string application, string type, string title)
    {
        var baseline = Fingerprint.Compute(Payload());
        var variant = Fingerprint.Compute(Payload(environment, application, type, title));
        Assert.NotEqual(baseline, variant);
    }

    [Fact]
    public void Compute_IgnoresFieldsOutsideTheFingerprint()
    {
        var withDetail = Payload() with { Detail = "stack trace here", Status = 500 };
        Assert.Equal(Fingerprint.Compute(Payload()), Fingerprint.Compute(withDetail));
    }
}
