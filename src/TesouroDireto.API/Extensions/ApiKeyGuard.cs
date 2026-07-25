using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace TesouroDireto.API.Extensions;

public static class ApiKeyGuard
{
    public const string DefaultKey = "CHANGE-ME-IN-PRODUCTION";

    /// <summary>
    /// Valores de placeholder que nunca podem chegar a produção, incluindo
    /// fallbacks de desenvolvimento usados em arquivos como docker-compose.yml.
    /// </summary>
    private static readonly string[] BlockedKeys =
    {
        DefaultKey,
        "dev-local-key",
    };

    public static void Validate(string environmentName, string? configuredKey)
    {
        if (string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(environmentName, "Testing", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var trimmedKey = configuredKey?.Trim();

        if (string.IsNullOrWhiteSpace(trimmedKey) ||
            BlockedKeys.Any(blocked => string.Equals(trimmedKey, blocked, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Configuração inválida: 'ApiKey:Key' precisa ser definida com um valor seguro em ambiente '{environmentName}'. " +
                $"O valor não pode ser vazio nem um dos placeholders proibidos ({string.Join(", ", BlockedKeys)}).");
        }
    }

    public static void Validate(IConfiguration configuration, IHostEnvironment environment)
    {
        Validate(environment.EnvironmentName, configuration["ApiKey:Key"]);
    }
}
