using Vaultaffe.Domain.References;

namespace Vaultaffe.UnitTests;

/// <summary>
/// <c>vaultaffe://&lt;project&gt;/&lt;environment&gt;/&lt;KEY&gt;</c>. Nothing in
/// the MVP resolves one; the scheme is settled now because it will be as
/// immutable as the token format later (Specification §5).
/// </summary>
public sealed class SecretReferenceTests
{
    [Fact]
    public void A_reference_reads_back_as_what_it_was_written_as()
    {
        var reference = SecretReference.To("webshop-api", "prod", "DATABASE_URL");

        Assert.Equal("vaultaffe://webshop-api/prod/DATABASE_URL", reference.ToString());
        Assert.True(SecretReference.TryParse(reference.ToString(), out var read));
        Assert.Equal(reference, read);
    }

    /// <summary>
    /// The organization is deliberately not in the reference: it is implied by
    /// the token that resolves it, which is why project names are unique per
    /// organization rather than globally (Specification §5).
    /// </summary>
    [Fact]
    public void A_reference_names_no_organization() =>
        Assert.DoesNotContain(
            "default",
            SecretReference.To("webshop-api", "prod", "KEY").ToString(),
            StringComparison.OrdinalIgnoreCase);

    [Theory]
    [InlineData("vaultaffe://webshop-api/prod")]
    [InlineData("vaultaffe://webshop-api/prod/KEY/extra")]
    [InlineData("vaultaffe://webshop-api/prod/lowercase")]
    [InlineData("vaultaffe://Web Shop/prod/KEY")]
    [InlineData("op://webshop-api/prod/KEY")]
    [InlineData("webshop-api/prod/KEY")]
    [InlineData("")]
    [InlineData(null)]
    public void What_is_not_a_reference_is_refused(string? text) =>
        Assert.False(SecretReference.TryParse(text, out _));

    /// <summary>
    /// Nothing is escaped and nothing needs to be, because the three parts
    /// cannot contain a separator. That is the whole reason ADR 0003 narrows
    /// them, and 1Password tripping over its own template syntax is the reason
    /// it is worth a test.
    /// </summary>
    [Fact]
    public void No_part_of_a_reference_can_contain_its_separator() =>
        Assert.Throws<ArgumentException>(() =>
            SecretReference.To("webshop-api/prod", "dev", "KEY"));
}
