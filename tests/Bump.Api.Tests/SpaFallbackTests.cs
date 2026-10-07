using System.Text.RegularExpressions;

namespace Bump.Api.Tests;

// The server answers 404 for any path outside the client route table, so a
// route added to router.tsx but not to SpaFallback.Routes would 404 on a
// hard refresh. These tests keep the two lists in step.
public class SpaFallbackTests
{
    [Fact]
    public void Routes_MatchTheRouterTable()
    {
        var router = File.ReadAllText(Path.Combine(RepoRoot(), "web", "src", "router.tsx"));
        var paths = Regex.Matches(router, """path:\s*"([^"]+)"\s*""")
            .Select(m => m.Groups[1].Value)
            .Where(p => p != "*") // the catch-all is the not-found page, never a 200
            .Order()
            .ToArray();

        Assert.NotEmpty(paths);
        Assert.Equal(paths, SpaFallback.Routes.Order().ToArray());
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/dashboard")]
    [InlineData("/Dashboard/")]
    [InlineData("/boards/openscorm")]
    [InlineData("/problems/42")]
    [InlineData("/login/mfa")]
    public void IsClientRoute_AcceptsKnownRoutes(string path)
    {
        Assert.True(SpaFallback.IsClientRoute(path));
    }

    [Theory]
    [InlineData("/tenants/openscorm")]
    [InlineData("/nope")]
    [InlineData("/boards")]
    [InlineData("/boards/openscorm/extra")]
    [InlineData("/admin/dashboard")]
    public void IsClientRoute_RejectsUnknownPaths(string path)
    {
        Assert.False(SpaFallback.IsClientRoute(path));
    }

    [Fact]
    public void BoardHandle_ReadsOnlyBoardPaths()
    {
        Assert.Equal("openscorm", SpaFallback.BoardHandle("/boards/openscorm"));
        Assert.Equal("openscorm", SpaFallback.BoardHandle("/Boards/openscorm/"));
        Assert.Null(SpaFallback.BoardHandle("/owners/openscorm"));
        Assert.Null(SpaFallback.BoardHandle("/boards"));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "bump.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("bump.sln not found above the test output.");
    }
}
