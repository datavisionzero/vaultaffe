using Vaultaffe.Application.Authorization;
using Vaultaffe.Application.Ports;
using Vaultaffe.Domain.Authorization;
using Vaultaffe.Domain.Refusals;
using Vaultaffe.Domain.Tokens;

namespace Vaultaffe.UnitTests;

/// <summary>
/// Specification §6.4, as the one place that enforces it: the scope set, the
/// binding, and the short list only a person may do.
/// </summary>
public sealed class AuthorizationTests
{
    /// <summary>
    /// For humans it is deliberately trivial: every user of an organization sees
    /// and changes everything in it, whatever the project or the environment.
    /// </summary>
    [Fact]
    public void A_session_reaches_the_whole_organization()
    {
        var authority = Acting(A.Session());

        Assert.True(authority.Caller.Reaches(Guid.NewGuid(), Guid.NewGuid()));
        Assert.Equal(Scopes.Everything, authority.Requires(Scopes.Everything).Scopes);
    }

    /// <summary>
    /// The default of an agent token: attribution, not restriction. Nothing about
    /// being an agent narrows anything on its own.
    /// </summary>
    [Fact]
    public void An_agent_token_reaches_the_whole_organization_too_by_default()
    {
        var authority = Acting(A.Agent());

        Assert.True(authority.Caller.Reaches(Guid.NewGuid()));
        authority.Requires(Scopes.Write, Guid.NewGuid(), Guid.NewGuid());
    }

    [Fact]
    public void A_missing_scope_says_which_one_was_missing()
    {
        var authority = Acting(A.Agent(scopes: Scopes.Names | Scopes.Read));

        var refusal = Assert.Throws<Refusal>(() => authority.Requires(Scopes.Write));

        Assert.Equal(RefusalCode.InsufficientScope, refusal.Code);
        Assert.Equal(
            ["names", "read"], (IReadOnlyList<string>)refusal.Extensions!["grantedScopes"]!);
        Assert.Equal(
            ["write"], (IReadOnlyList<string>)refusal.Extensions!["requiredScopes"]!);
        Assert.Contains("write", refusal.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// "May write but never read" is the case a read/read-write switch cannot
    /// express, and asking for two scopes is one question rather than two.
    /// </summary>
    [Fact]
    public void A_scope_set_is_carried_whole_or_not_at_all()
    {
        var authority = Acting(A.Agent(scopes: Scopes.Names | Scopes.Write));

        authority.Requires(Scopes.Names | Scopes.Write);

        Assert.Throws<Refusal>(() => authority.Requires(Scopes.Read | Scopes.Write));
    }

    [Fact]
    public void A_bound_token_reaches_its_project_and_nothing_else()
    {
        var project = Guid.NewGuid();
        var elsewhere = Guid.NewGuid();
        var authority = Acting(A.Service(new Reach(project, null)));

        authority.RequiresReachInto(project);
        authority.RequiresReachInto(project, Guid.NewGuid());

        var refusal = Assert.Throws<Refusal>(() => authority.RequiresReachInto(elsewhere));

        Assert.Equal(RefusalCode.OutOfReach, refusal.Code);
        Assert.Equal(elsewhere, refusal.Extensions!["projectId"]);
    }

    /// <summary>
    /// Excluding production is the first switch offered when a human creates an
    /// agent token (§6.4), and this is what that switch has to mean.
    /// </summary>
    [Fact]
    public void A_token_bound_to_one_environment_does_not_reach_its_neighbour()
    {
        var project = Guid.NewGuid();
        var staging = Guid.NewGuid();
        var production = Guid.NewGuid();
        var authority = Acting(A.Agent(new Reach(project, staging)));

        authority.RequiresReachInto(project, staging);

        Assert.Throws<Refusal>(() => authority.RequiresReachInto(project, production));

        // Naming the project alone asks whether the token reaches into it at
        // all — which it does, or its binding would name a project it cannot see.
        authority.RequiresReachInto(project);
    }

    /// <summary>
    /// Reaching into a project and reaching all of it are two questions, and
    /// renaming it, deleting it or adding an environment to it asks the second.
    /// A token bound to staging that could create the production environment
    /// beside it would make its own binding a suggestion.
    /// </summary>
    [Fact]
    public void Reaching_into_a_project_is_not_reaching_all_of_it()
    {
        var project = Guid.NewGuid();
        var staging = Guid.NewGuid();
        var narrowed = Acting(A.Agent(new Reach(project, staging)));

        narrowed.RequiresReachInto(project);

        var refusal = Assert.Throws<Refusal>(() => narrowed.RequiresAllOf(project));

        Assert.Equal(RefusalCode.OutOfReach, refusal.Code);

        // Bound to the project rather than to one environment of it, and it does.
        Acting(A.Agent(new Reach(project, null))).RequiresAllOf(project);
        Acting(A.Session()).RequiresAllOf(project);
    }

    /// <summary>
    /// Creating a project asks for the whole organization: a bound token is
    /// narrowed to projects that exist, and a new one is by definition not among
    /// them. The refusal carries no project id, because there is no project.
    /// </summary>
    [Fact]
    public void Anything_bound_at_all_is_short_of_the_whole_organization()
    {
        Acting(A.Session()).RequiresTheWholeOrganization();
        Acting(A.Agent()).RequiresTheWholeOrganization();

        var refusal = Assert.Throws<Refusal>(
            () => Acting(A.Agent(new Reach(Guid.NewGuid(), null))).RequiresTheWholeOrganization());

        Assert.Equal(RefusalCode.OutOfReach, refusal.Code);
        Assert.Null(refusal.Extensions!["projectId"]);
    }

    /// <summary>
    /// A token pointed at the wrong place is told that, not that it is missing a
    /// scope it may well have — and it is not told which scopes a project it may
    /// not know exists would have wanted.
    /// </summary>
    [Fact]
    public void Being_bound_elsewhere_is_answered_before_the_scope_is_looked_at()
    {
        var authority = Acting(A.Service(new Reach(Guid.NewGuid(), null)));

        var refusal = Assert.Throws<Refusal>(
            () => authority.Requires(Scopes.Everything, Guid.NewGuid()));

        Assert.Equal(RefusalCode.OutOfReach, refusal.Code);
    }

    [Theory]
    [InlineData(HumanAction.Purge)]
    [InlineData(HumanAction.CreateToken)]
    [InlineData(HumanAction.RevokeToken)]
    [InlineData(HumanAction.AdministerOrganization)]
    public void The_human_only_list_is_closed_to_every_machine_token(HumanAction action)
    {
        Acting(A.Session()).RequiresAHuman(action);

        foreach (var machine in new[] { A.Agent(), A.Service() })
        {
            var refusal = Assert.Throws<Refusal>(() => Acting(machine).RequiresAHuman(action));

            Assert.Equal(RefusalCode.HumanOnly, refusal.Code);
            Assert.Equal(HumanActions.NameOf(action), refusal.Extensions!["humanAction"]);
        }
    }

    /// <summary>
    /// Being human-only is not a scope: an agent token with every scope there is
    /// still does not get one of these, and a token cannot be given them.
    /// </summary>
    [Fact]
    public void Every_scope_there_is_does_not_add_up_to_being_a_person() =>
        Assert.Throws<Refusal>(
            () => Acting(A.Agent(scopes: Scopes.Everything)).RequiresAHuman(HumanAction.Purge));

    /// <summary>
    /// Specification §8, scenario 5: the agent hands the human a command, and a
    /// command that does not exist is worse than none. The server names the
    /// action and lets the client name the command (ADR 0010) — so no refusal
    /// here spells one, and this is what keeps it that way.
    /// </summary>
    [Fact]
    public void No_refusal_puts_a_command_in_an_agent_s_hands()
    {
        foreach (var action in Enum.GetValues<HumanAction>())
        {
            var refusal = HumanActions.RefusalOf(action);

            Assert.DoesNotContain("vaultaffe ", refusal, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("`", refusal, StringComparison.Ordinal);

            // What it does say is that a person does it, which is the whole
            // content of the refusal a client renders its own command from.
            Assert.Contains("human", refusal, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void An_act_that_needs_a_caller_refuses_when_nothing_authenticated_the_request()
    {
        var refusal = Assert.Throws<Refusal>(() => new Authority(new Nobody()).Caller);

        Assert.Equal(RefusalCode.Unauthenticated, refusal.Code);
    }

    private static Authority Acting(Caller caller) => new(new Whoever(caller));

    /// <summary>Callers, as the three kinds of token produce them.</summary>
    private static class A
    {
        public static Caller Session() =>
            Of(TokenKind.Session, Scopes.Everything, TokenReach.WholeOrganization);

        public static Caller Agent(params Reach[] reach) =>
            Agent(Scopes.Everything, reach);

        public static Caller Agent(Scopes scopes, params Reach[] reach) =>
            Of(TokenKind.Agent, scopes, TokenReach.Of(reach));

        public static Caller Service(params Reach[] reach) =>
            Of(TokenKind.Service, Scopes.ServiceDefault, TokenReach.Of(reach));

        private static Caller Of(TokenKind kind, Scopes scopes, TokenReach reach) =>
            new(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Maintainer",
                IsAdministrator: true,
                Guid.NewGuid(),
                kind.ToString(),
                kind,
                scopes,
                reach);
    }

    private sealed class Whoever(Caller caller) : ICallerIdentity
    {
        public Caller? Caller => caller;
    }

    private sealed class Nobody : ICallerIdentity
    {
        public Caller? Caller => null;
    }
}
