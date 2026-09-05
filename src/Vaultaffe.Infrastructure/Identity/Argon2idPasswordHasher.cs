using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using Vaultaffe.Application.Ports;

namespace Vaultaffe.Infrastructure.Identity;

/// <summary>
/// Argon2id, in the PHC encoding that carries its own parameters.
/// </summary>
/// <remarks>
/// The encoded value reads
/// <c>$argon2id$v=19$m=65536,t=3,p=1$&lt;salt&gt;$&lt;hash&gt;</c>, which is what
/// makes the cost a decision rather than a schema: a hash written under the old
/// parameters still verifies, and the next sign-in can write a new one. Nothing
/// in the database says which parameters were used, because the value does.
/// <para>
/// The numbers are the ones OWASP names for Argon2id — 64 MiB, three passes, one
/// lane — and they are a decision, not a placeholder. A verification refuses
/// parameters far outside them so that a tampered row cannot ask this process for
/// four gigabytes.
/// </para>
/// </remarks>
public sealed class Argon2idPasswordHasher : IPasswordHasher
{
    private const int MemoryKiB = 65536;

    private const int Iterations = 3;

    private const int Parallelism = 1;

    private const int HashBytes = 32;

    private const int SaltBytes = 16;

    /// <inheritdoc />
    /// <remarks>
    /// A salt of zeroes and a hash of zeroes: this is verified against, never
    /// compared to anything real, and what it costs is the point of it.
    /// </remarks>
    public string DecoyHash { get; } =
        Encode(new byte[SaltBytes], new byte[HashBytes], MemoryKiB, Iterations, Parallelism);

    public Task<string> HashAsync(string password, CancellationToken cancellationToken) =>
        HashAsync(
            password, RandomNumberGenerator.GetBytes(SaltBytes),
            MemoryKiB, Iterations, Parallelism, cancellationToken);

    public async Task<bool> VerifyAsync(
        string encoded, string password, CancellationToken cancellationToken)
    {
        if (!TryRead(encoded, out var salt, out var expected, out var memory, out var iterations, out var parallelism))
        {
            return false;
        }

        var actual = await HashAsync(
            password, salt, memory, iterations, parallelism, cancellationToken);

        TryRead(actual, out _, out var computed, out _, out _, out _);

        return CryptographicOperations.FixedTimeEquals(computed, expected);
    }

    private static async Task<string> HashAsync(
        string password,
        byte[] salt,
        int memory,
        int iterations,
        int parallelism,
        CancellationToken cancellationToken)
    {
        // Argon2 refuses an empty password outright, and an empty password does
        // reach here: a sign-in verifies whatever arrived in the body, and a
        // request that sent "" is a failed sign-in rather than a failed request.
        // One zero byte stands in for it, and can never collide with a password
        // anybody set — those are at least Password.MinimumLength characters.
        var bytes = Encoding.UTF8.GetBytes(password ?? string.Empty);

        using var argon = new Argon2id(bytes.Length == 0 ? [0] : bytes)
        {
            Salt = salt,
            MemorySize = memory,
            Iterations = iterations,
            DegreeOfParallelism = parallelism,
        };

        cancellationToken.ThrowIfCancellationRequested();

        return Encode(salt, await argon.GetBytesAsync(HashBytes), memory, iterations, parallelism);
    }

    private static string Encode(
        byte[] salt, byte[] hash, int memory, int iterations, int parallelism) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"$argon2id$v=19$m={memory},t={iterations},p={parallelism}"
            + $"${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}");

    /// <summary>
    /// Read an encoded value. False for anything this build cannot make sense
    /// of: a hash written by another product, a truncated column, a row somebody
    /// edited. None of those is a crashed request — all of them are a sign-in
    /// that did not work.
    /// </summary>
    private static bool TryRead(
        string? encoded,
        out byte[] salt,
        out byte[] hash,
        out int memory,
        out int iterations,
        out int parallelism)
    {
        salt = [];
        hash = [];
        memory = iterations = parallelism = 0;

        var parts = (encoded ?? string.Empty).Split('$');

        if (parts.Length != 6 || parts[1] != "argon2id" || parts[2] != "v=19")
        {
            return false;
        }

        var settings = parts[3].Split(',')
            .Select(setting => setting.Split('='))
            .Where(setting => setting.Length == 2)
            .ToDictionary(setting => setting[0], setting => setting[1], StringComparer.Ordinal);

        if (!Number(settings, "m", out memory)
            || !Number(settings, "t", out iterations)
            || !Number(settings, "p", out parallelism)
            || memory is < 8192 or > 262144
            || iterations is < 1 or > 10
            || parallelism is < 1 or > 16)
        {
            return false;
        }

        return Bytes(parts[4], SaltBytes, out salt) && Bytes(parts[5], HashBytes, out hash);
    }

    private static bool Number(IReadOnlyDictionary<string, string> settings, string name, out int value)
    {
        value = 0;

        return settings.TryGetValue(name, out var written)
            && int.TryParse(written, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static bool Bytes(string encoded, int expectedLength, out byte[] decoded)
    {
        decoded = [];

        Span<byte> buffer = stackalloc byte[expectedLength];

        if (!Convert.TryFromBase64String(encoded, buffer, out var written) || written != expectedLength)
        {
            return false;
        }

        decoded = buffer.ToArray();

        return true;
    }
}
