namespace QuotesLockedApi.Tests;

public sealed class JwtOptionsTests
{
    [Fact]
    public void Validate_KeyShorterThan32Bytes_Throws()
    {
        var options = new JwtOptions("issuer", "audience", "too-short-key");

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void Validate_KeyAtLeast32Bytes_DoesNotThrow()
    {
        var options = new JwtOptions("issuer", "audience", "exactly-32-byte-minimum-signing!");

        options.Validate();
    }
}
