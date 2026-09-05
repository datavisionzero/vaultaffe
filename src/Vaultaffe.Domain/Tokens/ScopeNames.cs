namespace Vaultaffe.Domain.Tokens;

/// <summary>
/// The words a scope set is spelled with — <c>names</c>, <c>read</c>,
/// <c>write</c>, <c>delete</c>.
/// </summary>
/// <remarks>
/// A scope set is a <c>[Flags]</c> integer in the database, because that is what
/// makes "may write but never read" one column (<c>docs/storage.md</c>). It is
/// words everywhere a person or a client sees it, because reading <c>6</c> and
/// having to know which bits those are is a contract that fails silently the day
/// a flag moves.
/// <para>
/// The words are the specification's own (§5), which is why they are here and
/// not in the HTTP adapter: a refusal that says which scopes were wanted has to
/// spell them the same way the contract does, and there is one spelling.
/// </para>
/// </remarks>
public static class ScopeNames
{
    /// <summary>The scopes there are, in the order everything lists them.</summary>
    public static IReadOnlyList<Scopes> All { get; } =
        [Scopes.Names, Scopes.Read, Scopes.Write, Scopes.Delete];

    /// <summary>One scope, as its word.</summary>
    public static string NameOf(Scopes scope) => scope switch
    {
        Scopes.Names => "names",
        Scopes.Read => "read",
        Scopes.Write => "write",
        Scopes.Delete => "delete",
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "That is not one scope."),
    };

    /// <summary>A set, as the words a client reads. Ordered as the scopes are declared.</summary>
    public static IReadOnlyList<string> NamesOf(Scopes scopes) =>
        [.. All.Where(scope => scopes.HasFlag(scope)).Select(NameOf)];

    /// <summary>Which scope a word names, or null when it names none.</summary>
    public static Scopes? Parse(string? word) =>
        All.Cast<Scopes?>().FirstOrDefault(scope => NameOf(scope!.Value) == word);
}
