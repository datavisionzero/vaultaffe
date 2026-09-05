namespace Vaultaffe.Application.Ports;

/// <summary>
/// Which organization the caller is acting in. Answered by whatever
/// authenticated them — a token names its organization (Specification §5), and
/// so does a browser session.
/// </summary>
/// <remarks>
/// This is the port the multi-tenancy of Specification §9 hangs from. Every
/// query is organization-filtered, and the filter is applied in one central
/// place rather than in every handler, so this is the one thing that place has
/// to ask. A caller that is not inside an organization yet — the first run, a
/// login attempt — has no scope, and the store answers nothing rather than
/// everything.
/// </remarks>
public interface IOrganizationScope
{
    /// <summary>
    /// The organization the caller acts in, or null before anything has
    /// authenticated them.
    /// </summary>
    Guid? OrganizationId { get; }
}
