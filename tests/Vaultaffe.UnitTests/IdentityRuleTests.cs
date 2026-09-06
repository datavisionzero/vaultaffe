using Vaultaffe.Domain.Identities;

namespace Vaultaffe.UnitTests;

/// <summary>
/// The rules identity is held to: what an address is, what a password is, and
/// what the short code of a device login is allowed to look like.
/// </summary>
public sealed class IdentityRuleTests
{
    [Theory]
    [InlineData("maintainer@example.test")]
    [InlineData("a@b")]
    [InlineData("first.last+tag@example.co.uk")]
    public void An_address_is_deliberately_almost_anything_with_an_at_in_it(string address) =>
        Assert.True(EmailAddress.IsValid(address));

    [Theory]
    [InlineData("")]
    [InlineData("nobody")]
    [InlineData("two@at@signs")]
    [InlineData("with space@example.test")]
    [InlineData("maint\nainer@example.test")]
    public void And_still_refuses_what_an_index_could_not_hold(string address) =>
        Assert.False(EmailAddress.IsValid(address));

    /// <summary>
    /// Two spellings of one address are one person to everybody except an index,
    /// so there is one spelling and the index holds that. Surrounding whitespace
    /// is a spelling too: it is what a login form collects, not what somebody
    /// meant.
    /// </summary>
    [Fact]
    public void An_address_has_one_spelling()
    {
        Assert.Equal(
            "maintainer@example.test",
            EmailAddress.Normalize("  Maintainer@Example.TEST\n"));

        Assert.Equal(
            EmailAddress.Require("MAINTAINER@example.test", "email"),
            EmailAddress.Require("maintainer@example.test", "email"));
    }

    /// <summary>
    /// Changing the address a person signs in with is one column and nothing
    /// else. The hash carries its own salt, so the password they had is the
    /// password they have — which is what makes this safe to do to somebody who
    /// is not in the room.
    /// </summary>
    [Fact]
    public void An_address_changes_without_touching_anything_else()
    {
        var person = new User(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "maintainer@example.test",
            "Maintainer",
            "a-hash-standing-in-for-argon",
            isAdministrator: true,
            DateTimeOffset.UnixEpoch);

        person.ChangeEmailTo("  Maintainer@Elsewhere.test ");

        Assert.Equal("maintainer@elsewhere.test", person.Email);
        Assert.Equal("Maintainer", person.Name);
        Assert.Equal("a-hash-standing-in-for-argon", person.PasswordHash);
        Assert.True(person.IsAdministrator);

        // What an index could not hold is refused here too, and not at the
        // constraint.
        Assert.Throws<ArgumentException>(() => person.ChangeEmailTo("not an address"));
        Assert.Equal("maintainer@elsewhere.test", person.Email);
    }

    /// <summary>
    /// Length, and nothing else. Composition rules are what push people towards
    /// `Password1!` and towards using it in three places.
    /// </summary>
    [Theory]
    [InlineData("correct horse battery staple", true)]
    [InlineData("aaaaaaaaaaaa", true)]
    [InlineData("short", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void A_password_is_long_enough_or_it_is_not(string? password, bool valid) =>
        Assert.Equal(valid, Password.IsValid(password));

    [Fact]
    public void A_password_over_the_ceiling_is_refused_before_argon_sees_it() =>
        Assert.False(Password.IsValid(new string('a', Password.MaximumLength + 1)));

    /// <summary>
    /// No vowels, so a code can never come out as a word; and none of the pairs a
    /// person reads wrong, so it survives being read aloud across a room.
    /// </summary>
    [Fact]
    public void A_user_code_carries_nothing_that_can_be_misread()
    {
        // No vowel, so no code is ever a word; no digit, so not one of 0/O, 1/I,
        // 5/S or 2/Z has a second half to be confused with.
        Assert.DoesNotContain(UserCode.Alphabet, "AEIOU".Contains);
        Assert.DoesNotContain(UserCode.Alphabet, char.IsDigit);

        for (var attempt = 0; attempt < 200; attempt++)
        {
            var code = UserCode.Issue();

            Assert.Equal(UserCode.Length, code.Length);
            Assert.All(code, character => Assert.Contains(character, UserCode.Alphabet));
        }
    }

    /// <summary>
    /// Case, spaces and dashes are how the code was shown, not what it is. A
    /// person retyping one should not have to reproduce the dash.
    /// </summary>
    [Theory]
    [InlineData("BCDF-GHJK", "BCDFGHJK")]
    [InlineData("bcdfghjk", "BCDFGHJK")]
    [InlineData("  bcdf ghjk  ", "BCDFGHJK")]
    [InlineData("BCDF-GHJ", "")]
    [InlineData("BCDF-GHJA", "")]
    [InlineData(null, "")]
    public void A_typed_code_is_read_forgivingly_and_still_has_to_be_a_code(
        string? typed, string expected) =>
        Assert.Equal(expected, UserCode.Normalize(typed));

    [Fact]
    public void A_code_is_shown_in_two_halves() =>
        Assert.Equal("BCDF-GHJK", UserCode.ForReading("BCDFGHJK"));
}
