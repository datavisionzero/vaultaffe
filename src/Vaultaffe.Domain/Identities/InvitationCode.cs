using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Vaultaffe.Domain.Identities;

/// <summary>
/// The secret inside an invitation link: what an administrator copies, hands
/// over, and what the person on the other end proves they were invited with
/// (Specification §6.1).
/// </summary>
/// <remarks>
/// It is the device code's sibling (<see cref="DeviceCode"/>) and made the same
/// way for the same reasons: no <c>vaultaffe_</c> prefix, because nothing should
/// teach a secret scanner to look for a string that is worthless three days
/// later and nothing should be able to mistake one for a token; and stored as a
/// hash, because the instance holds no credential it could read back.
/// <para>
/// It travels in the <b>fragment</b> of the link and is sent to the instance in
/// a request body, never in a path or a query. A fragment does not leave the
/// browser, and this repository's own rule is that no secret value ends up in
/// anything this product writes — an access log full of working invitation links
/// is exactly that.
/// </para>
/// </remarks>
public static class InvitationCode
{
    /// <summary>256 bits, for the same reason a token carries 256.</summary>
    public const int RandomBytes = 32;

    /// <summary>A new invitation code. The only place one is made.</summary>
    public static string Issue() =>
        Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(RandomBytes));

    /// <summary>What the instance stores and looks one up by.</summary>
    public static byte[] Hash(string code) => SHA256.HashData(Encoding.UTF8.GetBytes(code));
}
