using System.Runtime.CompilerServices;
using System.Text;
using ExcelDataReader;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TesouroDireto.Application.Feriados;

namespace TesouroDireto.Infrastructure.Feriados;

public sealed class FeriadoImportService(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<FeriadoImportService> logger) : IFeriadoImportService
{
    public async IAsyncEnumerable<FeriadoRecord> GetFeriadosAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var url = configuration["FeriadoImport:Url"];
        if (string.IsNullOrWhiteSpace(url))
        {
            logger.LogError("FeriadoImport:Url is not configured");
            yield break;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            logger.LogError("FeriadoImport:Url must be an absolute HTTPS URL");
            yield break;
        }

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Failed to download feriados XLS from {Url}", url);
            yield break;
        }

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("Feriados XLS download returned {StatusCode} from {Url}", response.StatusCode, url);
            response.Dispose();
            yield break;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = ExcelReaderFactory.CreateBinaryReader(stream);

        var isFirstRow = true;

        while (reader.Read())
        {
            if (isFirstRow)
            {
                isFirstRow = false;
                continue;
            }

            if (reader.GetFieldType(0) != typeof(DateTime))
            {
                continue;
            }

            var data = DateOnly.FromDateTime(reader.GetDateTime(0));
            var descricao = reader.GetString(2)?.Trim();

            if (string.IsNullOrWhiteSpace(descricao))
            {
                continue;
            }

            yield return new FeriadoRecord(data, descricao);
        }

        response.Dispose();
    }
}
