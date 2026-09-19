namespace Bump.Api.Tests;

// A bare date such as ?since=2026-09-19 binds as Kind=Unspecified, and Npgsql
// only writes Utc to timestamptz - without AsUtc the query is a 500.
public class ProblemReportFilterTests
{
    [Fact]
    public void AsUtc_ReadsABareDateAsUtcMidnight()
    {
        var result = ProblemReportFilter.AsUtc(new DateTime(2026, 9, 19));

        Assert.Equal(DateTimeKind.Utc, result!.Value.Kind);
        Assert.Equal(new DateTime(2026, 9, 19, 0, 0, 0, DateTimeKind.Utc), result.Value);
    }

    [Fact]
    public void AsUtc_LeavesAUtcValueAlone()
    {
        var utc = new DateTime(2026, 9, 19, 14, 32, 0, DateTimeKind.Utc);

        Assert.Equal(utc, ProblemReportFilter.AsUtc(utc));
    }

    [Fact]
    public void AsUtc_ConvertsALocalValue()
    {
        var local = new DateTime(2026, 9, 19, 14, 32, 0, DateTimeKind.Local);

        var result = ProblemReportFilter.AsUtc(local);

        Assert.Equal(DateTimeKind.Utc, result!.Value.Kind);
        Assert.Equal(local.ToUniversalTime(), result.Value);
    }

    [Fact]
    public void AsUtc_PassesNullThrough()
    {
        Assert.Null(ProblemReportFilter.AsUtc(null));
    }
}
