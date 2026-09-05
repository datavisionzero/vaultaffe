using Vaultaffe.Domain.Identities;

namespace Vaultaffe.UnitTests;

/// <summary>
/// One `vaultaffe login` in progress: five states, four of which end the polling,
/// and the order between them — which is the order of what already happened.
/// </summary>
public sealed class DeviceAuthorizationTests
{
    private static readonly DateTimeOffset _now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid _human = Guid.NewGuid();

    private static DeviceAuthorization ALogin() =>
        DeviceAuthorization.Begin(Guid.NewGuid(), Guid.NewGuid(), _now).Authorization;

    [Fact]
    public void Beginning_one_hands_over_two_codes_and_stores_only_the_hash_of_the_long_one()
    {
        var (authorization, deviceCode, userCode) =
            DeviceAuthorization.Begin(Guid.NewGuid(), Guid.NewGuid(), _now);

        Assert.Equal(DeviceCode.Hash(deviceCode), authorization.DeviceCodeHash);
        Assert.Equal(userCode, authorization.UserCode);
        Assert.Equal(DeviceAuthorizationState.Pending, authorization.StateAt(_now));

        // The device code is the credential and never leaves the answer that made
        // it; the record has no way back to it.
        Assert.NotEqual(deviceCode, System.Text.Encoding.UTF8.GetString(authorization.DeviceCodeHash));
    }

    [Fact]
    public void Nobody_confirming_in_time_is_expired_rather_than_pending_forever()
    {
        var authorization = ALogin();

        Assert.Equal(
            DeviceAuthorizationState.Pending,
            authorization.StateAt(_now + DeviceAuthorization.Lifetime - TimeSpan.FromSeconds(1)));

        Assert.Equal(
            DeviceAuthorizationState.Expired,
            authorization.StateAt(_now + DeviceAuthorization.Lifetime));
    }

    [Fact]
    public void Confirming_names_who_did_it()
    {
        var authorization = ALogin();

        Assert.True(authorization.ApproveBy(_human, _now));
        Assert.Equal(_human, authorization.ApprovedByUserId);
        Assert.Equal(DeviceAuthorizationState.Approved, authorization.StateAt(_now));
    }

    /// <summary>
    /// An approval nobody collected in time is expired, not approved — otherwise a
    /// device code left in a CI log would still be worth something an hour later.
    /// </summary>
    [Fact]
    public void An_approval_nobody_collected_expires_with_the_login()
    {
        var authorization = ALogin();

        authorization.ApproveBy(_human, _now);

        Assert.Equal(
            DeviceAuthorizationState.Expired,
            authorization.StateAt(_now + DeviceAuthorization.Lifetime));
    }

    [Fact]
    public void A_device_code_hands_over_one_token_and_never_a_second()
    {
        var authorization = ALogin();
        var token = Guid.NewGuid();

        authorization.ApproveBy(_human, _now);

        Assert.True(authorization.RedeemTo(token, _now));
        Assert.Equal(token, authorization.IssuedTokenId);
        Assert.False(authorization.RedeemTo(Guid.NewGuid(), _now));
        Assert.Equal(token, authorization.IssuedTokenId);
    }

    [Fact]
    public void A_redeemed_login_stays_redeemed_after_it_would_have_expired()
    {
        var authorization = ALogin();

        authorization.ApproveBy(_human, _now);
        authorization.RedeemTo(Guid.NewGuid(), _now);

        Assert.Equal(
            DeviceAuthorizationState.Redeemed,
            authorization.StateAt(_now + DeviceAuthorization.Lifetime));
    }

    [Fact]
    public void Nothing_is_confirmed_after_it_was_refused()
    {
        var authorization = ALogin();

        Assert.True(authorization.Deny(_now));
        Assert.False(authorization.ApproveBy(_human, _now));
        Assert.False(authorization.Deny(_now));
        Assert.Equal(DeviceAuthorizationState.Denied, authorization.StateAt(_now));
    }

    [Fact]
    public void Nothing_is_handed_over_for_a_login_nobody_confirmed()
    {
        var authorization = ALogin();

        Assert.False(authorization.RedeemTo(Guid.NewGuid(), _now));
        Assert.Null(authorization.IssuedTokenId);
    }
}
