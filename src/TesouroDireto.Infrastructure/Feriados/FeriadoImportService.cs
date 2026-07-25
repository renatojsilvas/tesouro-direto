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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cancelamento pedido por quem chamou (ex.: shutdown do Quartz) é legítimo —
            // não é uma falha do download, então propaga limpo em vez de virar degradação
            // graciosa.
            throw;
        }
        catch (Exception ex)
        {
            // Catch amplo de propósito (mesmo padrão de FocusBcbService.cs): cobre não só
            // HttpRequestException, mas também o que a resiliência (tarefa 13) pode lançar
            // em cima do mesmo GetAsync — TimeoutRejectedException (AttemptTimeout de 45s
            // esgotado após os retries) e, no futuro, BrokenCircuitException. O timeout do
            // Polly cancela um CancellationToken INTERNO dele (não o cancellationToken do
            // chamador), então a exceção de cancelamento gerada por ele NÃO passa no
            // filtro do catch acima e cai aqui — degradação graciosa em vez de exceção não
            // tratada subindo para o handler/job.
            logger.LogError(ex, "Failed to download feriados XLS from {Url}", url);
            yield break;
        }

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("Feriados XLS download returned {StatusCode} from {Url}", response.StatusCode, url);
            response.Dispose();
            yield break;
        }

        using var memoryStream = new MemoryStream();
        await using (var httpStream = await response.Content.ReadAsStreamAsync(cancellationToken))
        {
            await httpStream.CopyToAsync(memoryStream, cancellationToken);
        }

        memoryStream.Position = 0;
        using var reader = ExcelReaderFactory.CreateBinaryReader(memoryStream);

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
