using Bump.Api.Services;

namespace Bump.Api.Tests;

// Outage, recovery and announcement emails rebuild the unsubscribe link from the
// stored token, so the token in the link must decode back to exactly what is
// stored. When the stored value was a hash, every such link failed.
public class SubscriberTokenTests
{
    [Fact]
    public void A_link_built_from_the_stored_token_decodes_back_to_it()
    {
        var (token, raw) = SubscriberRepository.NewUnsubscribeToken();

        Assert.Equal(token, SubscriberRepository.EncodeToken(raw));
        Assert.Equal(raw, SubscriberRepository.DecodeToken(token));
    }

    [Fact]
    public void Tokens_are_url_safe()
    {
        for (var i = 0; i < 50; i++)
        {
            var (token, _) = SubscriberRepository.NewUnsubscribeToken();
            Assert.DoesNotContain('+', token);
            Assert.DoesNotContain('/', token);
            Assert.DoesNotContain('=', token);
        }
    }

    [Theory]
    [InlineData("not base64!")]
    [InlineData("a")]
    public void A_mangled_token_decodes_to_nothing(string token)
    {
        Assert.Null(SubscriberRepository.DecodeToken(token));
    }
}
