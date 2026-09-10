using Vaultaffe.Domain.Organizations;
using Vaultaffe.Domain.Tokens;

namespace Vaultaffe.Domain.Identities;

/// <summary>
/// One handover in progress (Specification §6.2): the CLI shows a short code, a
/// person confirms it in a browser on any machine, and the CLI polls with a long
/// one until something has happened.
/// </summary>
/// <remarks>
/// <see cref="Produces"/> says what is at the far end — a session for the person
/// who confirmed, or an agent token of the asking machine's own. One row type for
/// both, because everything up to that last step is identical and writing the
/// protocol twice is how two copies of it come apart
/// (<see href="../../../docs/adr/0021-an-agent-asks-for-its-own-token.md">ADR 0021</see>).
/// <para>
/// This is the only login that works everywhere the CLI runs — an SSH session, a
/// CI job, a container, an agent's sandbox — because it is the only one that does
/// not need a browser on the machine doing the asking.
/// </para>
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

    private readonly List<EnrollmentBinding> _approvedBindings = [];

    public DeviceAuthorization(
        Guid id,
        Guid organizationId,
        byte[] deviceCodeHash,
        string userCode,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        Handover produces = Handover.Session,
        string? requestedName = null)
    {
        ArgumentNullException.ThrowIfNull(deviceCodeHash);

        Id = id;
        OrganizationId = organizationId;
        DeviceCodeHash = deviceCodeHash;
        UserCode = userCode;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        Produces = produces;
        RequestedName = requestedName;
    }

    /// <summary>
    /// Begin one: the record, and the two codes that exist exactly once. The
    /// device code is returned rather than stored, for the reason a token value
    /// is.
    /// </summary>
    public static (DeviceAuthorization Authorization, string DeviceCode, string UserCode) Begin(
        Guid id,
        Guid organizationId,
        DateTimeOffset now) =>
        Begin(id, organizationId, Handover.Session, requestedName: null, now);

    /// <summary>
    /// The same, for a machine asking for a token of its own rather than for a
    /// person's session. <paramref name="requestedName"/> is what the asking
    /// client calls itself — shown to whoever confirms and believed by nobody:
    /// the instance cannot check it, and the person is the one who decides what
    /// this ends up being called.
    /// </summary>
    public static (DeviceAuthorization Authorization, string DeviceCode, string UserCode) BeginEnrollment(
        Guid id,
        Guid organizationId,
        string requestedName,
        DateTimeOffset now) =>
        Begin(id, organizationId, Handover.AgentToken, requestedName, now);

    private static (DeviceAuthorization Authorization, string DeviceCode, string UserCode) Begin(
        Guid id,
        Guid organizationId,
        Handover produces,
        string? requestedName,
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
                now + Lifetime,
                produces,
                requestedName),
            deviceCode,
            userCode);
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    /// <summary>The hash of the long code the CLI polls with, and never the code.</summary>
    public byte[] DeviceCodeHash { get; private set; }

    /// <summary>The short code a human reads out of a terminal and types into a browser.</summary>
    public string UserCode { get; private set; }

    /// <summary>A session for the person who confirms, or a token of the asker's own.</summary>
    public Handover Produces { get; private set; }

    /// <summary>
    /// What the asking client says it is, for an enrollment; null for a login,
    /// which produces a session and names nothing. It is untrusted text shown to
    /// a person — the instance has no way to check that a machine calling itself
    /// an agent on somebody's laptop is one — and the person who confirms settles
    /// what the token is finally called.
    /// </summary>
    public string? RequestedName { get; private set; }

    /// <summary>
    /// What the person who agreed settled on calling it. It is
    /// <see cref="RequestedName"/> unless they changed it, and it is a separate
    /// field rather than an overwrite because the two are different facts: one is
    /// what a machine claimed about itself, the other is what a person decided.
    /// </summary>
    public string? ApprovedName { get; private set; }

    /// <summary>
    /// What the person agreed this token may do, once somebody has agreed
    /// anything. Null until then, and for a login always.
    /// </summary>
    public Scopes? ApprovedScopes { get; private set; }

    /// <summary>
    /// And how far it may reach. Empty with an approval in place means the whole
    /// organization, exactly as it does on a token (§6.4).
    /// </summary>
    public IReadOnlyCollection<EnrollmentBinding> ApprovedBindings => _approvedBindings;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>When a human confirmed it, and who.</summary>
    public DateTimeOffset? ApprovedAt { get; private set; }

    public Guid? ApprovedByUserId { get; private set; }

    /// <summary>When a human said they did not start this login.</summary>
    public DateTimeOffset? DeniedAt { get; private set; }

    /// <summary>When the token was handed over. A device code works once.</summary>
    public DateTimeOffset? RedeemedAt { get; private set; }

    /// <summary>Which token this handover produced, once it has — a session or an agent's own.</summary>
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

    /// <summary>
    /// The same, for an enrollment: the person confirming also says what the
    /// token they are agreeing to is called and may do. The reach is laid down
    /// afterwards with <see cref="BindApprovalTo"/>, and none of it at all means
    /// the whole organization.
    /// </summary>
    public bool ApproveBy(Guid userId, string name, Scopes scopes, DateTimeOffset moment)
    {
        if (!ApproveBy(userId, moment))
        {
            return false;
        }

        ApprovedName = name;
        ApprovedScopes = scopes;

        return true;
    }

    /// <summary>
    /// Narrow what was approved to a project, or to one environment of it. The
    /// row is handed back as well as kept, for the reason a token's binding is:
    /// whoever is storing these has to be able to say which rows are new.
    /// </summary>
    public EnrollmentBinding BindApprovalTo(
        Guid bindingId, Guid projectId, Guid? environmentId = null)
    {
        var binding = new EnrollmentBinding(
            bindingId, OrganizationId, Id, projectId, environmentId);

        _approvedBindings.Add(binding);

        return binding;
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
