namespace Vaultaffe.Domain.Tokens;

/// <summary>
/// One project, or one environment of one project, as a value: what a
/// <see cref="TokenBinding"/> says, without the row it was read from.
/// </summary>
public readonly record struct Reach(Guid ProjectId, Guid? EnvironmentId);

/// <summary>
/// Everything a token may touch (Specification §6.4). Empty means the whole
/// organization, which is what an agent token gets unless the human narrows it:
/// the point is attribution, not restriction.
/// </summary>
/// <remarks>
/// A value rather than the rows, so that the caller a request was authenticated
/// into can be asked what it reaches without a database behind it — and so that
/// the answer is the same one everywhere, because there is one
/// <see cref="Covers"/> and not one per handler.
/// <para>
/// Naming a project without an environment asks about the project itself: whether
/// this token reaches into it at all. A token bound to one environment of that
/// project does, which is why listing a project's environments is allowed to a
/// token that may only read one of them — the listing is names, and the values
/// behind them are a second question with a second answer.
/// </para>
/// </remarks>
public sealed class TokenReach
{
    private readonly Reach[] _bindings;

    private TokenReach(Reach[] bindings) => _bindings = bindings;

    /// <summary>No binding at all: everything in the organization.</summary>
    public static TokenReach WholeOrganization { get; } = new([]);

    public static TokenReach Of(IEnumerable<Reach> bindings)
    {
        var narrowed = bindings.ToArray();

        return narrowed.Length is 0 ? WholeOrganization : new TokenReach(narrowed);
    }

    /// <summary>What a token reaches, read off the bindings it carries.</summary>
    public static TokenReach Of(Token token) =>
        Of(token.Bindings.Select(binding => new Reach(binding.ProjectId, binding.EnvironmentId)));

    public bool IsTheWholeOrganization => _bindings.Length is 0;

    public IReadOnlyList<Reach> Bindings => _bindings;

    /// <summary>
    /// Whether this token may touch that project, or that environment of it.
    /// </summary>
    public bool Covers(Guid projectId, Guid? environmentId = null) =>
        IsTheWholeOrganization
        || _bindings.Any(binding =>
            binding.ProjectId == projectId
            && (binding.EnvironmentId is null
                || environmentId is null
                || binding.EnvironmentId == environmentId));
}
