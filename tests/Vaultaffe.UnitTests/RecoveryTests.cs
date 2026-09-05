using Vaultaffe.Domain.Secrets;

namespace Vaultaffe.UnitTests;

/// <summary>
/// The window a deletion can be taken back in (Specification §6.5). The number
/// is a decision rather than a placeholder, and it lives in one line; this is
/// what that line means.
/// </summary>
public sealed class RecoveryTests
{
    private static readonly DateTimeOffset _deleted = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Inside_the_window_it_can_be_taken_back()
    {
        Assert.True(ValueHistory.IsStillRecoverable(_deleted, _deleted));
        Assert.True(ValueHistory.IsStillRecoverable(_deleted, _deleted.AddHours(71)));
        Assert.True(ValueHistory.IsStillRecoverable(_deleted, _deleted + ValueHistory.RecoveryWindow));
    }

    [Fact]
    public void And_a_moment_past_it_it_cannot() =>
        Assert.False(ValueHistory.IsStillRecoverable(
            _deleted, _deleted + ValueHistory.RecoveryWindow + TimeSpan.FromSeconds(1)));

    /// <summary>
    /// 72 hours, as Specification §6.5 and <c>docs/storage.md</c> both state it.
    /// The number is a decision rather than a placeholder, and a product whose
    /// documentation says one thing while its code says another has neither.
    /// </summary>
    [Fact]
    public void The_window_is_the_seventy_two_hours_the_specification_names() =>
        Assert.Equal(72, ValueHistory.RecoveryWindow.TotalHours);
}
