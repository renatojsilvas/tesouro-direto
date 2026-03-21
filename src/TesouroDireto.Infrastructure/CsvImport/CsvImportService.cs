using System.Runtime.CompilerServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TesouroDireto.Application.Importacao;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Infrastructure.CsvImport;

public sealed class CsvImportService(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<CsvImportService> logger) : ICsvImportService
{
    public async IAsyncEnumerable<Result<CsvRecord>> GetRecordsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var url = configuration["CsvImport:Url"];
        if (string.IsNullOrWhiteSpace(url))
        {
            yield return ImportacaoErrors.InvalidLine("CsvImport:Url is not configured.");
            yield break;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            yield return ImportacaoErrors.InvalidLine("CsvImport:Url must be an absolute HTTPS URL.");
            yield break;
        }

        var response = await SendRequestAsync(uri, cancellationToken);
        if (response is null)
        {
            yield return ImportacaoErrors.InvalidLine("Failed to download CSV from source.");
            yield break;
        }

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("CSV download returned {StatusCode} from {Url}", response.StatusCode, url);
            response.Dispose();
            yield return ImportacaoErrors.InvalidLine($"CSV source returned HTTP {(int)response.StatusCode}.");
            yield break;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        var isFirstLine = true;

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (isFirstLine)
            {
                isFirstLine = false;
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            yield return CsvParserHelper.ParseLine(line);
        }

        response.Dispose();
    }

    private async Task<HttpResponseMessage?> SendRequestAsync(Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            return await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Failed to download CSV from {Url}", uri);
            return null;
        }
    }
}
