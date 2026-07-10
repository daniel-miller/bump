using Bump.Api.Auth;

namespace Bump.Api.Tests;

public class PasswordHasherTests
{
    [Fact]
    public void Hash_ProducesExpectedSizes()
    {
        var (hash, salt) = PasswordHasher.Hash("correct horse battery staple");
        Assert.Equal(32, hash.Length);
        Assert.Equal(16, salt.Length);
    }

    [Fact]
    public void Verify_AcceptsCorrectPassword()
    {
        var (hash, salt) = PasswordHasher.Hash("correct horse battery staple");
        Assert.True(PasswordHasher.Verify("correct horse battery staple", hash, salt));
    }

    [Fact]
    public void Verify_RejectsWrongPassword()
    {
        var (hash, salt) = PasswordHasher.Hash("correct horse battery staple");
        Assert.False(PasswordHasher.Verify("Correct horse battery staple", hash, salt));
    }

    [Fact]
    public void Hash_SaltsEachCallDifferently()
    {
        var (hash1, salt1) = PasswordHasher.Hash("same password");
        var (hash2, salt2) = PasswordHasher.Hash("same password");
        Assert.NotEqual(salt1, salt2);
        Assert.NotEqual(hash1, hash2);
    }
}
