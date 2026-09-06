using Vaultaffe.Domain.Secrets;

namespace Vaultaffe.UnitTests;

/// <summary>
/// The missing-key notice (Specification §6.1), and the reason it is a majority
/// rather than a comparison: a notice that cries wolf is clicked away, and then
/// it is worthless on the day it is right (§12).
/// </summary>
public sealed class MissingKeyTests
{
    private static IReadOnlyDictionary<string, IReadOnlySet<string>> Project(
        params (string Environment, string[] Keys)[] environments) =>
        environments.ToDictionary(
            one => one.Environment,
            one => (IReadOnlySet<string>)one.Keys.ToHashSet(StringComparer.Ordinal),
            StringComparer.Ordinal);

    /// <summary>The case the feature exists for: it was added everywhere but here.</summary>
    [Fact]
    public void A_key_the_others_agree_on_is_missing_here()
    {
        var missing = MissingKeys.In("prod", Project(
            ("dev", ["DB_URL", "STRIPE_KEY"]),
            ("staging", ["DB_URL", "STRIPE_KEY"]),
            ("prod", ["DB_URL"])));

        Assert.Equal(["STRIPE_KEY"], missing.Select(one => one.Name));
        Assert.Equal(["dev", "staging"], missing[0].PresentIn);
    }

    /// <summary>
    /// The largest class of false alarm, and the reason a pairwise rule is
    /// unusable: production legitimately has keys nowhere else has.
    /// </summary>
    [Fact]
    public void A_key_only_one_environment_has_is_nobody_elses_business()
    {
        var project = Project(
            ("dev", ["DB_URL"]),
            ("staging", ["DB_URL"]),
            ("prod", ["DB_URL", "SENTRY_DSN"]));

        Assert.Empty(MissingKeys.In("dev", project));
        Assert.Empty(MissingKeys.In("staging", project));
    }

    /// <summary>
    /// The question the ticket asks first. A personal environment is one voice
    /// among the others rather than a comparison partner of its own, so it
    /// neither silences the notice for everybody else nor is held against
    /// production on its own.
    /// </summary>
    [Fact]
    public void A_personal_environment_silences_nothing()
    {
        var project = Project(
            ("dev", ["DB_URL", "STRIPE_KEY"]),
            ("staging", ["DB_URL", "STRIPE_KEY"]),
            ("prod", ["DB_URL"]),
            ("dev-alex", ["DB_URL"]));

        Assert.Equal(["STRIPE_KEY"], MissingKeys.In("prod", project).Select(one => one.Name));
    }

    /// <summary>
    /// And a key only the personal environment has is not something the shared
    /// environments are missing.
    /// </summary>
    [Fact]
    public void And_a_key_only_it_has_is_not_missing_anywhere()
    {
        var project = Project(
            ("dev", ["DB_URL"]),
            ("staging", ["DB_URL"]),
            ("prod", ["DB_URL"]),
            ("dev-alex", ["DB_URL", "MY_LAPTOP_PROXY"]));

        Assert.Empty(MissingKeys.In("dev", project));
        Assert.Empty(MissingKeys.In("prod", project));
    }

    /// <summary>
    /// With two environments the majority of one is the other one, so the rule
    /// degrades to exactly what a person means by "the other one has it".
    /// </summary>
    [Fact]
    public void With_two_environments_it_is_the_other_one()
    {
        var project = Project(("dev", ["DB_URL", "STRIPE_KEY"]), ("prod", ["DB_URL"]));

        Assert.Equal(["STRIPE_KEY"], MissingKeys.In("prod", project).Select(one => one.Name));
        Assert.Empty(MissingKeys.In("dev", project));
    }

    /// <summary>Half is not a majority: two of four others is not the team's opinion.</summary>
    [Fact]
    public void A_tie_is_not_a_majority()
    {
        var project = Project(
            ("a", ["K"]),
            ("b", ["K"]),
            ("c", []),
            ("d", []),
            ("e", []));

        Assert.Empty(MissingKeys.In("e", project));

        var third = Project(
            ("a", ["K"]),
            ("b", ["K"]),
            ("c", ["K"]),
            ("d", []),
            ("e", []));

        Assert.Equal(["K"], MissingKeys.In("e", third).Select(one => one.Name));
    }

    /// <summary>
    /// One environment has nothing to be compared with, and neither has one the
    /// caller cannot see past. An empty notice is the honest answer there.
    /// </summary>
    [Fact]
    public void One_environment_alone_is_missing_nothing() =>
        Assert.Empty(MissingKeys.In("dev", Project(("dev", []))));

    [Fact]
    public void An_environment_that_is_not_in_the_project_says_nothing() =>
        Assert.Empty(MissingKeys.In("nowhere", Project(("dev", ["K"]), ("prod", ["K"]))));

    /// <summary>Names, sorted, so that a screen and a listing read the same twice.</summary>
    [Fact]
    public void The_notice_is_ordered_by_key()
    {
        var missing = MissingKeys.In("prod", Project(
            ("dev", ["ZULU", "ALPHA", "MIKE"]),
            ("staging", ["ZULU", "ALPHA", "MIKE"]),
            ("prod", [])));

        Assert.Equal(["ALPHA", "MIKE", "ZULU"], missing.Select(one => one.Name));
    }
}
