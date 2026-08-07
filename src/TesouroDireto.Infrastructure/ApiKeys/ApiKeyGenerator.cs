using System.Security.Cryptography;
using TesouroDireto.Application.ApiKeys;

namespace TesouroDireto.Infrastructure.ApiKeys;

public sealed class ApiKeyGenerator : IApiKeyGenerator
{
    private const string Prefix = "td_";
    private const int RawKeyBytesLength = 32;
    private const int PrefixoLength = 8;

    public GeneratedKeyMaterial Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(RawKeyBytesLength);
        var rawKey = Prefix + ToBase64Url(bytes);
        var prefixo = rawKey[..Math.Min(PrefixoLength, rawKey.Length)];

        return new GeneratedKeyMaterial(rawKey, prefixo);
    }

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
