using Vaultaffe.Domain.Projects;
using Vaultaffe.Domain.References;
using Vaultaffe.Domain.Secrets;

namespace Vaultaffe.UnitTests;

/// <summary>
/// The two name rules of Specification §5. Both are as unreversible as the token
/// format: a name that is valid today is a name somebody has written into a
/// reference, a script and a `.env` by tomorrow.
/// </summary>
public sealed class NameTests
{
    [Theory]
    [InlineData("DATABASE_URL")]
    [InlineData("_PRIVATE")]
    [InlineData("A")]
    [InlineData("STRIPE_KEY_2")]
    public void A_secret_name_is_what_an_environment_variable_may_be_called(string name) =>
        Assert.True(SecretName.IsValid(name));

    [Theory]
    [InlineData("lower")]
    [InlineData("2LEADING")]
    [InlineData("HAS-DASH")]
    [InlineData("HAS SPACE")]
    [InlineData("")]
    [InlineData(null)]
    // A pattern anchored with ^ and $ alone would let this through in .NET,
    // because $ also matches before a trailing newline. The rule is anchored so
    // that it does not.
    [InlineData("DATABASE_URL\n")]
    public void Everything_else_is_not_a_secret_name(string? name) =>
        Assert.False(SecretName.IsValid(name));

    [Fact]
    public void A_secret_name_has_a_ceiling() =>
        Assert.False(SecretName.IsValid(new string('A', SecretName.Limit + 1)));

    [Theory]
    [InlineData("webshop-api")]
    [InlineData("landing-page")]
    [InlineData("dev")]
    [InlineData("dev-alex")]
    [InlineData("v2.1")]
    [InlineData("a")]
    public void A_project_or_environment_name_may_appear_in_a_reference(string name) =>
        Assert.True(ReferenceName.IsValid(name));

    [Theory]
    [InlineData("has space")]
    [InlineData("Webshop-API")]
    [InlineData("slash/inside")]
    [InlineData("-leading")]
    [InlineData("trailing-")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("dev\n")]
    public void Everything_else_may_not(string? name) =>
        Assert.False(ReferenceName.IsValid(name));

    [Fact]
    public void A_project_refuses_a_name_it_could_not_be_referenced_by() =>
        Assert.Throws<ArgumentException>(() =>
            new Project(Guid.NewGuid(), Guid.NewGuid(), "Web Shop", DateTimeOffset.UnixEpoch));

    [Fact]
    public void A_secret_refuses_a_name_that_is_not_an_environment_variable() =>
        Assert.Throws<ArgumentException>(() =>
            new Secret(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "db-url", DateTimeOffset.UnixEpoch));

    /// <summary>
    /// The defaults an operator gets when they create a project (Specification
    /// §5). They have to satisfy the rule they are handed to.
    /// </summary>
    [Fact]
    public void The_default_environments_are_nameable() =>
        Assert.All(Project.DefaultEnvironments, name => Assert.True(ReferenceName.IsValid(name)));
}
