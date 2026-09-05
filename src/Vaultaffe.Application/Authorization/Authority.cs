using Vaultaffe.Application.Ports;
using Vaultaffe.Domain.Authorization;
using Vaultaffe.Domain.Refusals;
using Vaultaffe.Domain.Tokens;

namespace Vaultaffe.Application.Authorization;

/// <summary>
/// The one place that says no. Every rule of Specification §6.4 — the scope set,
/// the binding, and the short list only a person may do — is decided here and
/// nowhere else.
/// </summary>
/// <remarks>
/// <b>One enforcement, not a check in every handler.</b> An act or an endpoint
/// says what it needs; what that means, and what the refusal looks like when it
/// is not met, is this class. The difference is not cosmetic: a rule spelled out
/// in twenty handlers is nineteen chances to spell it differently, and in a
/// secrets manager the one that is spelled wrong is the one nobody notices.
/// <para>
/// For humans it is deliberately trivial (§6.4): every user of an organization
/// sees and changes everything in it, and a session reaches the whole
/// organization. <b>The granularity is the tokens'</b> — a service or an agent
/// token carries a binding and a scope set, and both are enforced here. What is
/// left for a person is the human-only list, which is not a permission a token
/// can be given.
/// </para>
/// <para>
/// The organization is not one of the questions. It is not asked because it
/// cannot be answered wrongly: the caller names their organization and the query
/// filter reads the same object (§9), so a row from another one never reaches an
/// act to be refused.
/// </para>
/// </remarks>
public sealed class Authority(ICallerIdentity identity)
{
    /// <summary>
    /// The caller, for an act that needs one and asks nothing else. Refuses when
    /// nothing authenticated this request.
    /// </summary>
    public Caller Caller => identity.Required;

    /// <summary>
    /// One of the short list only a person may do (§6.4). The refusal names the
    /// action so that the client can name the command it has (ADR 0010).
    /// </summary>
    public Caller RequiresAHuman(HumanAction action)
    {
        var acting = Caller;

        return acting.IsHumanSession ? acting : throw Refusal.HumanOnly(action);
    }

    /// <summary>Every scope in <paramref name="wanted"/>, or a refusal naming what is missing.</summary>
    public Caller Requires(Scopes wanted)
    {
        var acting = Caller;

        return acting.Allows(wanted)
            ? acting
            : throw Refusal.InsufficientScope(wanted, acting.Scopes);
    }

    /// <summary>
    /// Reach into that project, or that environment of it. Naming the project
    /// alone asks whether the token reaches into it at all
    /// (<see cref="TokenReach"/>).
    /// </summary>
    public Caller RequiresReachInto(Guid projectId, Guid? environmentId = null)
    {
        var acting = Caller;

        return acting.Reaches(projectId, environmentId)
            ? acting
            : throw Refusal.OutOfReach(projectId, environmentId);
    }

    /// <summary>
    /// Reach into every environment of that project, including ones that do not
    /// exist yet — what renaming or deleting a project asks, and what adding an
    /// environment to it asks.
    /// </summary>
    public Caller RequiresAllOf(Guid projectId)
    {
        var acting = Caller;

        return acting.ReachesAllOf(projectId) ? acting : throw Refusal.OutOfReach(projectId);
    }

    /// <summary>
    /// Reach across the whole organization: what creating a project asks, since
    /// there is no project yet for a binding to have named.
    /// </summary>
    public Caller RequiresTheWholeOrganization()
    {
        var acting = Caller;

        return acting.Reach.IsTheWholeOrganization ? acting : throw Refusal.OutOfReach();
    }

    /// <summary>
    /// Both at once, which is what every act touching a secret asks: this scope,
    /// in this environment of this project.
    /// </summary>
    /// <remarks>
    /// The binding is checked first on purpose. A token bound to staging that
    /// asks about production should be told it is pointed at the wrong place,
    /// not that it is missing a scope it may well have — and the other order
    /// would tell it which scopes it would have needed for a project it may not
    /// know exists.
    /// </remarks>
    public Caller Requires(Scopes wanted, Guid projectId, Guid? environmentId = null)
    {
        RequiresReachInto(projectId, environmentId);

        return Requires(wanted);
    }
}
