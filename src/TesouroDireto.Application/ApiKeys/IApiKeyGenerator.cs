namespace TesouroDireto.Application.ApiKeys;

public interface IApiKeyGenerator
{
    GeneratedKeyMaterial Generate();
}

public sealed record GeneratedKeyMaterial(string RawKey, string Prefixo);
