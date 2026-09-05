using Vaultaffe.Domain.Secrets;

namespace Vaultaffe.UnitTests;

/// <summary>
/// The <c>.env</c> format this product reads and writes (Specification §6.2).
/// There is no standard for it and every tool differs at the edges, so what this
/// one does is a decision — and these are the edges.
/// </summary>
public sealed class DotEnvTests
{
    [Fact]
    public void The_ordinary_shape_of_a_file()
    {
        var (settings, complaints) = DotEnv.Read(
            """
            # a comment
            DATABASE_URL=postgres://localhost/app

            export STRIPE_KEY=not-a-key-either
            QUOTED="a value"
            LITERAL='a $value'
            """);

        Assert.Empty(complaints);
        Assert.Equal(
            [
                new DotEnv.Setting("DATABASE_URL", "postgres://localhost/app"),
                new DotEnv.Setting("STRIPE_KEY", "not-a-key-either"),
                new DotEnv.Setting("QUOTED", "a value"),
                new DotEnv.Setting("LITERAL", "a $value"),
            ],
            settings);
    }

    /// <summary>
    /// A PEM key is the reason a double-quoted value may span lines. It is also
    /// the value most likely to be in the file somebody is migrating.
    /// </summary>
    [Fact]
    public void A_double_quoted_value_may_span_lines()
    {
        var (settings, complaints) = DotEnv.Read(
            "KEY=\"-----BEGIN-----\nmiddle\n-----END-----\"\nAFTER=yes\n");

        Assert.Empty(complaints);
        Assert.Equal(
            [
                new DotEnv.Setting("KEY", "-----BEGIN-----\nmiddle\n-----END-----"),
                new DotEnv.Setting("AFTER", "yes"),
            ],
            settings);
    }

    [Fact]
    public void Escapes_inside_double_quotes_mean_what_they_look_like()
    {
        var (settings, _) = DotEnv.Read("""KEY="a\nb\tc\\d\"e" """);

        Assert.Equal("a\nb\tc\\d\"e", settings.Single().Value);
    }

    /// <summary>
    /// An unknown escape keeps both characters. A backslash inside a password is
    /// a backslash, and dropping it would hand back a credential that no longer
    /// works for a reason nobody could see.
    /// </summary>
    [Fact]
    public void An_escape_this_format_does_not_know_is_left_alone()
    {
        var (settings, _) = DotEnv.Read("""KEY="a\qb" """);

        Assert.Equal("""a\qb""", settings.Single().Value);
    }

    /// <summary>
    /// An unquoted value keeps its '#'. A trailing comment cannot be told from a
    /// password containing one, and guessing wrong loses a character of a
    /// credential — which is the failure that costs an afternoon.
    /// </summary>
    [Fact]
    public void An_unquoted_value_keeps_everything_that_is_not_blank()
    {
        var (settings, _) = DotEnv.Read("KEY=  pa#ss word  \n");

        Assert.Equal("pa#ss word", settings.Single().Value);
    }

    [Fact]
    public void A_line_it_cannot_read_is_reported_rather_than_refusing_the_file()
    {
        var (settings, complaints) = DotEnv.Read(
            """
            GOOD=yes
            not a setting
            lower_case=no
            ALSO_GOOD=yes
            """);

        Assert.Equal(["GOOD", "ALSO_GOOD"], settings.Select(setting => setting.Key));
        Assert.Equal([2, 3], complaints.Select(complaint => complaint.Line));
    }

    [Fact]
    public void An_unclosed_quote_is_a_complaint_and_not_an_exception()
    {
        var (settings, complaints) = DotEnv.Read("KEY=\"never closed\n");

        Assert.Empty(settings);
        Assert.Single(complaints);
    }

    /// <summary>
    /// What is written can be read: every value is quoted and escaped, so the
    /// format is never one unusual value away from producing a file it cannot
    /// read back.
    /// </summary>
    [Fact]
    public void Everything_written_comes_back_the_way_it_went_in()
    {
        DotEnv.Setting[] written =
        [
            new("SIMPLE", "value"),
            new("SPACED", "two words"),
            new("QUOTED", """he said "no" """),
            new("MULTILINE", "-----BEGIN-----\nbody\n-----END-----"),
            new("BACKSLASH", @"C:\not\a\path"),
            new("HASH", "pa#ss"),
            new("TABBED", "a\tb"),
        ];

        var (read, complaints) = DotEnv.Read(DotEnv.Write(written));

        Assert.Empty(complaints);
        Assert.Equal(written, read);
    }

    /// <summary>
    /// <c>KEY=</c> is what a file says when a key is there and its value is not,
    /// and that is exactly what an empty placeholder means (§6.2). The format
    /// reads it as the empty value it is; what an import makes of it is the
    /// import's decision.
    /// </summary>
    [Fact]
    public void A_key_with_nothing_after_the_equals_reads_as_empty()
    {
        var (settings, complaints) = DotEnv.Read("SMTP_PASSWORD=\nOTHER=\"\"\n");

        Assert.Empty(complaints);
        Assert.All(settings, setting => Assert.Equal(string.Empty, setting.Value));
        Assert.Equal(2, settings.Count);
    }
}
