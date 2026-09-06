namespace Vaultaffe.Domain.Secrets;

/// <summary>
/// One key the notice mentions: a key this environment has not got, and the
/// environments of the same project that have.
/// </summary>
public sealed record MissingKey(string Name, IReadOnlyList<string> PresentIn);

/// <summary>
/// Keys that are missing here and present elsewhere in the same project
/// (Specification §6.1). <b>A display, never an action</b> — nothing in this
/// product creates a secret because of it.
/// </summary>
/// <remarks>
/// The rule is the whole ticket, because the obvious rule is unusable. Held
/// pairwise — "present in any other environment" — a notice reports every key
/// that `prod` has and `dev` does not, and every key of a project's three shared
/// environments against a personal `dev-alex`. Environment names are free (§5)
/// and nothing in the model says which of them is the complete one, so a notice
/// built on any single comparison partner is a notice somebody clicks away — and
/// then it is worthless on the one day it is right (§12).
/// <para>
/// <b>So the environments decide by majority.</b> A key is missing here when more
/// than half of this project's other environments hold it. That answers the
/// question the ticket asks first, without a new field and without asking anybody
/// to declare a reference environment:
/// </para>
/// <list type="bullet">
/// <item>A key only <c>prod</c> has is not reported anywhere: one environment out
/// of three is not a majority, and this is the largest class of false alarm.</item>
/// <item>A key <c>dev</c> and <c>staging</c> have and <c>prod</c> has not is
/// reported in <c>prod</c>, which is the case worth having a notice for.</item>
/// <item>A personal <c>dev-alex</c> does not silence anything: it is one voice
/// among the others rather than a comparison partner of its own.</item>
/// <item>With two environments a majority of one is the other one, so the rule
/// degrades to exactly what a person means by "the other environment has it".</item>
/// </list>
/// <para>
/// What is left over after that is what dismissal is for, per key and per
/// environment, and dismissal is the only thing a person can do about a notice
/// here.
/// </para>
/// <para>
/// A placeholder counts as present: the key exists there and a human still has to
/// fill it (§6.2), which is a different notice's business and not this one's. A
/// deleted secret counts as absent, because it is out of listings and use.
/// </para>
/// </remarks>
public static class MissingKeys
{
    /// <summary>
    /// The notice for <paramref name="environment"/>, given the key names of
    /// every environment of the project the caller may see — including that one.
    /// </summary>
    /// <remarks>
    /// Keys and not secrets, so that the rule can be read, tested and argued
    /// about without a database anywhere near it.
    /// </remarks>
    public static IReadOnlyList<MissingKey> In(
        string environment,
        IReadOnlyDictionary<string, IReadOnlySet<string>> keysByEnvironment)
    {
        ArgumentNullException.ThrowIfNull(keysByEnvironment);

        if (!keysByEnvironment.TryGetValue(environment, out var here))
        {
            return [];
        }

        var others = keysByEnvironment
            .Where(one => one.Key != environment)
            .ToList();

        if (others.Count == 0)
        {
            return [];
        }

        return
        [
            .. others
                .SelectMany(one => one.Value)
                .Distinct(StringComparer.Ordinal)
                .Where(name => !here.Contains(name))
                .Select(name => new MissingKey(
                    name,
                    [
                        .. others
                            .Where(one => one.Value.Contains(name))
                            .Select(one => one.Key)
                            .Order(StringComparer.Ordinal),
                    ]))
                // Strictly more than half, so a tie is not a majority: with four
                // other environments, two of them are not the team's opinion.
                .Where(missing => missing.PresentIn.Count * 2 > others.Count)
                .OrderBy(missing => missing.Name, StringComparer.Ordinal),
        ];
    }
}
