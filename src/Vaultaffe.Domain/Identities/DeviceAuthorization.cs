using Vaultaffe.Domain.Organizations;

namespace Vaultaffe.Domain.Identities;

/// <summary>
/// One `vaultaffe login` in progress (Specification §6.2): the CLI shows a short
/// code, the human confirms it in a browser on any machine, and the CLI polls
/// with a long one until something has happened.
/// </summary>
/// <remarks>
/// This is the only login that works everywhere the CLI runs — an SSH session, a
/// CI job, a container, an agent's sandbox — because it is the only one that does
/// not need a browser on the machine doing the asking.
/// <para>
/// The row carries the hash of the device code and never the code. What the
/// instance can read back is the short user code, which is not a credential: it
/// identifies a pending request to the human confirming it, and confirming still
/// takes their password.
/// </para>
/// <para>
/// It carries an organization like every other domain table (§9). At the moment
/// it is created nobody has authenticated, so the organization is the instance's
/// only one — which the MVP guarantees there is exactly one of (§5). The day
/// there are several, what resolves it is the human who confirms, and this column
/// is where that answer already goes.
/// </para>
/// </remarks>
public sealed class DeviceAuthorization : IBelongToAnOrganization
{
    /// <summary>
    /// How long a human has. Long enough to walk to another machine, short
    /// enough that an abandoned code is not lying around for an afternoon.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    /// <summary>How often the CLI should poll. Seconds, and the server says so.</summary>
    public static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(5);

    public DeviceAuthorization(
        Guid id,
        Guid organizationId,
        byte[] deviceCodeHash,
        string userCode,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
    {
        ArgumentNullException.ThrowIfNull(deviceCodeHash);

        Id = id;
        OrganizationId = organizationId;
        DeviceCodeHash = deviceCodeHash;
        UserCode = userCode;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    /// <summary>
    /// Begin one: the record, and the two codes that exist exactly once. The
    /// device code is returned rather than stored, for the reason a token value
    /// is.
    /// </summary>
    public static (DeviceAuthorization Authorization, string DeviceCode, string UserCode) Begin(
        Guid id,
        Guid organizationId,
        DateTimeOffset now)
    {
        var deviceCode = Identities.DeviceCode.Issue();
        var userCode = Identities.UserCode.Issue();

        return (
            new DeviceAuthorization(
                id,
                organizationId,
                Identities.DeviceCode.Hash(deviceCode),
                userCode,
                now,
                now + Lifetime),
            deviceCode,
            userCode);
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    /// <summary>The hash of the long code the CLI polls with, and never the code.</summary>
    public byte[] DeviceCodeHash { get; private set; }

    /// <summary>The short code a human reads out of a terminal and types into a browser.</summary>
    public string UserCode { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>When a human confirmed it, and who.</summary>
    public DateTimeOffset? ApprovedAt { get; private set; }

    public Guid? ApprovedByUserId { get; private set; }

    /// <summary>When a human said they did not start this login.</summary>
    public DateTimeOffset? DeniedAt { get; private set; }

    /// <summary>When the token was handed over. A device code works once.</summary>
    public DateTimeOffset? RedeemedAt { get; private set; }

    /// <summary>Which session token this login produced, once it has.</summary>
    public Guid? IssuedTokenId { get; private set; }

    /// <summary>Where this login has got to at <paramref name="moment"/>.</summary>
    /// <remarks>
    /// Order matters and is the order of what already happened: a redeemed login
    /// stays redeemed after it expires, and an approval that nobody collected in
    /// time is expired rather than approved — otherwise a device code left in a
    /// CI log would still be worth something an hour later.
    /// </remarks>
    public DeviceAuthorizationState StateAt(DateTimeOffset moment) =>
        RedeemedAt is not null ? DeviceAuthorizationState.Redeemed
        : DeniedAt is not null ? DeviceAuthorizationState.Denied
        : moment >= ExpiresAt ? DeviceAuthorizationState.Expired
        : ApprovedAt is not null ? DeviceAuthorizationState.Approved
        : DeviceAuthorizationState.Pending;

    /// <summary>
    /// A human confirms it. Only a pending one can be confirmed — repeating it,
    /// or confirming one somebody already refused, does nothing and says so.
    /// </summary>
    public bool ApproveBy(Guid userId, DateTimeOffset moment)
    {
        if (StateAt(moment) is not DeviceAuthorizationState.Pending)
        {
            return false;
        }

        ApprovedAt = moment;
        ApprovedByUserId = userId;

        return true;
    }

    /// <summary>A human says they did not start this login.</summary>
    public bool Deny(DateTimeOffset moment)
    {
        if (StateAt(moment) is not DeviceAuthorizationState.Pending)
        {
            return false;
        }

        DeniedAt = moment;

        return true;
    }

    /// <summary>
    /// Hand the token over, once. Only an approved login can be redeemed, and
    /// only the first poll after the approval gets anything.
    /// </summary>
    public bool RedeemTo(Guid tokenId, DateTimeOffset moment)
    {
        if (StateAt(moment) is not DeviceAuthorizationState.Approved)
        {
            return false;
        }

        RedeemedAt = moment;
        IssuedTokenId = tokenId;

        return true;
    }
}
