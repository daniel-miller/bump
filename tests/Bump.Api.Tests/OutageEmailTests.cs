using Bump.Api.Mail.MailTemplates;

namespace Bump.Api.Tests;

// A recovery email tells the reader when the outage started, when it ended,
// and how long it lasted, in units a person reads at a glance.
public class OutageEmailTests
{
    [Theory]
    [InlineData(30, "under a minute")]
    [InlineData(41 * 60, "41 min")]
    [InlineData(3 * 3600, "3 h")]
    [InlineData(3 * 3600 + 41 * 60 + 20, "3 h 41 min")]
    [InlineData(2 * 86400 + 5 * 3600 + 30 * 60, "2 d 5 h")]
    public void Duration_reads_in_whole_units(int seconds, string expected)
    {
        Assert.Equal(expected, OutageDuration.Describe(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void Subscriber_email_carries_start_end_and_duration()
    {
        var mail = OutageResolved.Build("a@example.com", "CMDS", "CMDS v5 API - unreachable",
            "2026-10-22 10:00:00Z", "2026-10-22 10:41:00Z", "41 min", "https://bump.example/unsubscribe?token=t");
        Assert.Equal("[CMDS] Resolved: CMDS v5 API - unreachable", mail.Subject);
        Assert.Contains("Started:  2026-10-22 10:00:00Z", mail.Text);
        Assert.Contains("Down for: 41 min", mail.Text);
        Assert.Contains("unsubscribe?token=t", mail.Text);
    }

    [Fact]
    public void Contact_email_says_the_service_is_back()
    {
        var mail = OutageRecovered.Build("ops@example.com", "CMDS v5 API", "CMDS v5 API - unreachable",
            "2026-10-22 10:00:00Z", "2026-10-22 10:41:00Z", "41 min", "Resolved automatically.");
        Assert.Equal("[Bump] Recovered: CMDS v5 API - unreachable", mail.Subject);
        Assert.StartsWith("CMDS v5 API is responding again", mail.Text);
        Assert.Contains("Resolved automatically.", mail.Text);
    }
}
