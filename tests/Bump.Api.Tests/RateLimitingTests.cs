namespace Bump.Api.Tests;

// A rejected caller is told about the limit it actually hit. Only the apps and
// problems limits are keyed by an API key; a person at the sign-in form holds
// none, and used to be told theirs was over its limit.
public class RateLimitingTests
{
    [Fact]
    public void SignIn_NamesSignInAttemptsAndTheWait()
    {
        Assert.Equal(
            "Too many sign-in attempts from this network. Try again in 14 minutes.",
            RateLimiting.RejectionDetail(RateLimiting.AuthLoginPolicy, TimeSpan.FromSeconds(13 * 60 + 5)));
    }

    [Fact]
    public void SignIn_NeverMentionsAnApiKey()
    {
        Assert.DoesNotContain("API key", RateLimiting.RejectionDetail(RateLimiting.AuthLoginPolicy, null));
    }

    [Theory]
    [InlineData(RateLimiting.AppsPolicy)]
    [InlineData(RateLimiting.ProblemsPolicy)]
    public void KeyedLimits_NameTheApiKey(string policy)
    {
        Assert.Contains("API key", RateLimiting.RejectionDetail(policy, TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void UnderAMinute_ReadsAsAMinute()
    {
        Assert.EndsWith("Try again in a minute.", RateLimiting.RejectionDetail(RateLimiting.StatusPolicy, TimeSpan.FromSeconds(20)));
    }

    [Fact]
    public void UnknownWait_ReadsAsLater()
    {
        Assert.EndsWith("Try again later.", RateLimiting.RejectionDetail(RateLimiting.SubscribePolicy, null));
    }
}
