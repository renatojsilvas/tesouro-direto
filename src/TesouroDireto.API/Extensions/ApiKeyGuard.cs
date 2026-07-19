using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace TesouroDireto.API.Extensions;

public static class ApiKeyGuard
{
    public const string DefaultKey = "CHANGE-ME-IN-PRODUCTION";

    public static void Validate(string environmentName, string? configuredKey)
    {
        if (string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(environmentName, "Testing", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var trimmedKey = configuredKey?.Trim();

        if (string.IsNullOrWhiteSpace(trimmedKey) || string.Equals(trimmedKey, DefaultKey, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Configuração inválida: 'ApiKey:Key' precisa ser definida com um valor seguro em ambiente '{environmentName}'. " +
                $"O valor não pode ser vazio nem o default '{DefaultKey}'.");
        }
    }

    public static void Validate(IConfiguration configuration, IHostEnvironment environment)
    {
        Validate(environment.EnvironmentName, configuration["ApiKey:Key"]);
    }
}
