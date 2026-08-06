using System.Security.Cryptography;
using System.Text;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Domain.ApiKeys;

public sealed record ApiKeyHash
{
    private ApiKeyHash(string value) => Value = value;

    public string Value { get; }

    public static ApiKeyHash FromRawKey(string rawKey)
    {
        ArgumentNullException.ThrowIfNull(rawKey);

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));

        return new ApiKeyHash(Convert.ToHexString(hashBytes).ToLowerInvariant());
    }

    public static Result<ApiKeyHash> Create(string hash)
    {
        if (string.IsNullOrEmpty(hash) || hash.Length != 64 || !hash.All(IsLowerHexDigit))
        {
            return new Error("ApiKeyHash.Invalid", "Hash must be a 64-character lowercase hex string.");
        }

        return new ApiKeyHash(hash);
    }

    private static bool IsLowerHexDigit(char c) => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
}
