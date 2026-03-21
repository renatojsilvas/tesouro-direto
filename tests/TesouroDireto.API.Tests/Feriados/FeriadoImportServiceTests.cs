using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TesouroDireto.Application.Feriados;
using TesouroDireto.Infrastructure.Feriados;

namespace TesouroDireto.API.Tests.Feriados;

public sealed class FeriadoImportServiceTests
{
    private const string TestXlsResource = "TesouroDireto.API.Tests.Feriados.feriados_test.xls";

    [Fact]
    public async Task GetFeriadosAsync_WithValidXls_ShouldParseAllRecords()
    {
        var service = CreateService(GetTestXlsBytes(), "https://example.com/feriados.xls");

        var records = new List<FeriadoRecord>();
        await foreach (var record in service.GetFeriadosAsync(CancellationToken.None))
        {
            records.Add(record);
        }

        records.Should().HaveCount(5);
        records.Should().Contain(r => r.Descricao == "Natal" && r.Data == new DateOnly(2024, 12, 25));
        records.Should().Contain(r => r.Descricao == "Confraternização Universal" && r.Data == new DateOnly(2024, 1, 1));
        records.Should().Contain(r => r.Descricao == "Carnaval" && r.Data == new DateOnly(2024, 2, 12));
        records.Should().Contain(r => r.Descricao == "Paixão de Cristo" && r.Data == new DateOnly(2024, 3, 29));
    }

    [Fact]
    public async Task GetFeriadosAsync_ShouldSkipFooterRows()
    {
        var service = CreateService(GetTestXlsBytes(), "https://example.com/feriados.xls");

        var records = new List<FeriadoRecord>();
        await foreach (var record in service.GetFeriadosAsync(CancellationToken.None))
        {
            records.Add(record);
        }

        records.Should().NotContain(r => r.Descricao.Contains("Fonte"));
    }

    [Fact]
    public async Task GetFeriadosAsync_WithNonSeekableStream_ShouldStillWork()
    {
        // This is the exact scenario that caused the bug:
        // HTTP streams are not seekable, ExcelDataReader requires seek
        var service = CreateServiceWithNonSeekableStream(
            GetTestXlsBytes(), "https://example.com/feriados.xls");

        var records = new List<FeriadoRecord>();
        await foreach (var record in service.GetFeriadosAsync(CancellationToken.None))
        {
            records.Add(record);
        }

        records.Should().HaveCount(5);
    }

    [Fact]
    public async Task GetFeriadosAsync_WithMissingUrl_ShouldReturnEmpty()
    {
        var service = CreateService([], "");

        var records = new List<FeriadoRecord>();
        await foreach (var record in service.GetFeriadosAsync(CancellationToken.None))
        {
            records.Add(record);
        }

        records.Should().BeEmpty();
    }

    [Fact]
    public async Task GetFeriadosAsync_WithHttpUrl_ShouldReturnEmpty()
    {
        var service = CreateService([], "http://example.com/feriados.xls");

        var records = new List<FeriadoRecord>();
        await foreach (var record in service.GetFeriadosAsync(CancellationToken.None))
        {
            records.Add(record);
        }

        records.Should().BeEmpty();
    }

    private static byte[] GetTestXlsBytes()
    {
        using var stream = typeof(FeriadoImportServiceTests).Assembly
            .GetManifestResourceStream(TestXlsResource)!;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static FeriadoImportService CreateService(byte[] xlsBytes, string url)
    {
        var handler = new FakeHttpMessageHandler(xlsBytes);
        var httpClient = new HttpClient(handler);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FeriadoImport:Url"] = url
            })
            .Build();
        var logger = Substitute.For<ILogger<FeriadoImportService>>();

        return new FeriadoImportService(httpClient, configuration, logger);
    }

    private static FeriadoImportService CreateServiceWithNonSeekableStream(byte[] xlsBytes, string url)
    {
        var handler = new NonSeekableHttpMessageHandler(xlsBytes);
        var httpClient = new HttpClient(handler);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FeriadoImport:Url"] = url
            })
            .Build();
        var logger = Substitute.For<ILogger<FeriadoImportService>>();

        return new FeriadoImportService(httpClient, configuration, logger);
    }

    private sealed class FakeHttpMessageHandler(byte[] content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content)
            };
            return Task.FromResult(response);
        }
    }

    private sealed class NonSeekableHttpMessageHandler(byte[] content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new NonSeekableStream(content))
            };
            return Task.FromResult(response);
        }
    }

    private sealed class NonSeekableStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override long Position
        {
            get => base.Position;
            set => throw new NotSupportedException();
        }
    }
}
