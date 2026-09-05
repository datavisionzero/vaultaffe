using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Vaultaffe.Domain.Identities;

/// <summary>
/// The credential half of the device-code flow: the long secret the CLI keeps to
/// itself and polls with, while the human reads out the short
/// <see cref="UserCode"/>.
/// </summary>
/// <remarks>
/// It is not a <see cref="Tokens.TokenValue"/> and deliberately carries no
/// <c>vaultaffe_</c> prefix: nothing should teach a secret scanner to look for a
/// string that is worthless ten minutes after it was made, and nothing should be
/// able to mistake one for a token. Like a token it is stored as its hash — the
/// instance holds no credential it could read back.
/// </remarks>
public static class DeviceCode
{
    /// <summary>256 bits, for the same reason a token carries 256.</summary>
    public const int RandomBytes = 32;

    /// <summary>A new device code. The only place one is made.</summary>
    public static string Issue() =>
        Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(RandomBytes));

    /// <summary>What the instance stores and looks one up by.</summary>
    public static byte[] Hash(string code) => SHA256.HashData(Encoding.UTF8.GetBytes(code));
}
