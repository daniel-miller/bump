using System.Text;
using static Bump.Api.ProblemsAuthFilter;

namespace Bump.Api.Tests;

// Every Bump.Sdk consumer holds the reporter key, so it may only report. A read
// key may only list and fetch. Neither may resolve or delete.
public class ProblemsAuthFilterTests
{
    private static readonly byte[] Reporter = Encoding.UTF8.GetBytes("reporter-key");
    private static readonly byte[][] Readers = [Encoding.UTF8.GetBytes("reader-one"), Encoding.UTF8.GetBytes("reader-two")];

    private static KeyRole Role(string token) => Match(Encoding.UTF8.GetBytes(token), Reporter, Readers);

    [Fact]
    public void Match_TellsTheReporterKeyFromEveryReadKey()
    {
        Assert.Equal(KeyRole.Reporter, Role("reporter-key"));
        Assert.Equal(KeyRole.Reader, Role("reader-one"));
        Assert.Equal(KeyRole.Reader, Role("reader-two"));
        Assert.Equal(KeyRole.None, Role("reader-three"));
        Assert.Equal(KeyRole.None, Role(""));
    }

    [Fact]
    public void Match_NeverMatchesAnUnsetReporterKey()
    {
        Assert.Equal(KeyRole.None, Match([], [], Readers));
    }

    [Fact]
    public void TheReporterKey_OnlyReports()
    {
        Assert.True(Permits(KeyRole.Reporter, "POST", ReportActionName));

        Assert.False(Permits(KeyRole.Reporter, "GET", "listProblems"));
        Assert.False(Permits(KeyRole.Reporter, "GET", "getProblem"));
        Assert.False(Permits(KeyRole.Reporter, "POST", "resolveProblem"));
        Assert.False(Permits(KeyRole.Reporter, "POST", "unresolveProblem"));
        Assert.False(Permits(KeyRole.Reporter, "DELETE", "deleteProblem"));
        Assert.False(Permits(KeyRole.Reporter, "POST", "deleteProblems"));
    }

    [Fact]
    public void AReadKey_OnlyListsAndFetches()
    {
        Assert.True(Permits(KeyRole.Reader, "GET", "listProblems"));
        Assert.True(Permits(KeyRole.Reader, "GET", "getProblem"));
        Assert.True(Permits(KeyRole.Reader, "HEAD", "listProblems"));

        Assert.False(Permits(KeyRole.Reader, "POST", ReportActionName));
        Assert.False(Permits(KeyRole.Reader, "POST", "resolveProblem"));
        Assert.False(Permits(KeyRole.Reader, "DELETE", "deleteProblem"));
        Assert.False(Permits(KeyRole.Reader, "POST", "deleteProblems"));
    }

    [Fact]
    public void NoKey_PermitsNothing()
    {
        Assert.False(Permits(KeyRole.None, "GET", "listProblems"));
        Assert.False(Permits(KeyRole.None, "POST", ReportActionName));
    }
}
