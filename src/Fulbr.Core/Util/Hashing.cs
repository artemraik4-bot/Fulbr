using System.Security.Cryptography;
using System.Text;

namespace Fulbr.Core.Util;

public static class Hashing
{
    public static string Fingerprint(params string?[] parts)
    {
        var canonical = string.Join('|', parts.Select(p =>
            (p ?? string.Empty).Trim().ToLowerInvariant()));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    public static string HashSecret(string secret)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    public static string Sha256Hex(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}
