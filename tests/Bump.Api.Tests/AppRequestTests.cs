namespace Bump.Api.Tests;

// Every app belongs to an owner, so creating one without an owner must be
// refused before it reaches the database's NOT NULL. An alias is a former
// handle, so it must be a valid handle and must never shadow the app's own.
public class AppRequestTests
{
    private static UpdateAppRequest Update(string handle = "scoop", string? owner = null, string[]? aliases = null) =>
        new(handle, "Scoop", 1, 3, 312, owner, aliases);

    [Fact]
    public void Create_RejectsAMissingOwner()
    {
        Assert.NotNull(new CreateAppRequest("quad", "CMDS v4").Validate());
    }

    [Fact]
    public void Create_AcceptsAnOwner()
    {
        Assert.Null(new CreateAppRequest("quad", "CMDS v4", Owner: "cmds").Validate());
    }

    [Fact]
    public void Update_LeavesOwnerAndAliasesOptional()
    {
        Assert.Null(Update().Validate());
    }

    [Fact]
    public void Update_AcceptsFormerHandles()
    {
        Assert.Null(Update(owner: "openscorm", aliases: ["slate"]).Validate());
    }

    [Fact]
    public void Update_RejectsAnEmptyOwner()
    {
        Assert.NotNull(Update(owner: " ").Validate());
    }

    [Fact]
    public void Update_RejectsAnAliasRepeatingTheHandle()
    {
        Assert.NotNull(Update(aliases: ["scoop"]).Validate());
    }

    [Fact]
    public void Update_RejectsARepeatedAlias()
    {
        Assert.NotNull(Update(aliases: ["slate", "slate"]).Validate());
    }

    [Theory]
    [InlineData("Slate")]
    [InlineData("slate_live")]
    [InlineData("")]
    public void Update_RejectsAnAliasThatIsNotAHandle(string alias)
    {
        Assert.NotNull(Update(aliases: [alias]).Validate());
    }
}
