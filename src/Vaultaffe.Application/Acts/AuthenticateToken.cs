using Vaultaffe.Application.Ports;
using Vaultaffe.Domain.Tokens;

namespace Vaultaffe.Application.Acts;

/// <summary>
/// A token value in, the caller it stands for out — or nothing.
/// </summary>
/// <remarks>
/// This is the one act that runs before anybody is inside an organization, and
/// it is what decides which one they are in. It answers null rather than refusing
/// so that the adapter above it decides what a request with no usable token means:
/// for most endpoints that is a refusal, for the handshake it is nothing at all.
/// <para>
/// The value is never compared as a string. It is parsed for its prefix, hashed,
/// and looked up by exactly that column (<c>docs/storage.md</c>) — which is also
/// why a token this instance could read back does not exist.
/// </para>
/// </remarks>
public sealed class AuthenticateToken(IIdentityStore identities, TimeProvider clock)
{
    public async Task<Caller?> ExecuteAsync(string? presented, CancellationToken cancellationToken)
    {
        if (!TokenValue.TryParse(presented, out var value))
        {
            return null;
        }

        var found = await identities.FindTokenByHashAsync(value.Hash(), cancellationToken);

        if (found is null)
        {
            return null;
        }

        var (token, user) = found.Value;

        // The prefix said which kind this is before anything was looked up. A row
        // that disagrees with it is not a token this instance issued.
        //
        // And a token belonging to somebody who is out of the organization
        // authenticates nobody, whatever state the row itself is in: deactivating
        // a person takes their sessions with it, and the agent tokens they are
        // accountable for with them.
        return token.Kind == value.Kind && token.IsUsableAt(clock.GetUtcNow()) && user.IsActive
            ? Caller.Of(user, token)
            : null;
    }
}
