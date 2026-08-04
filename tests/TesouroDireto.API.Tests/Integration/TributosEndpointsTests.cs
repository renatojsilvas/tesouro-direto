using System.Net;
using System.Net.Http.Json;
using System.Net.Mime;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TesouroDireto.Application.Tributos;
using TesouroDireto.Domain.Tributos;
using TesouroDireto.Infrastructure.Persistence;

namespace TesouroDireto.API.Tests.Integration;

[Collection("api")]
public sealed class TributosEndpointsTests(ApiTestFactory factory) : IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateAuthenticatedClient();

    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<Guid> SeedTributoAsync(string nome, int ordem)
    {
        var id = Guid.Empty;

        await factory.SeedAsync(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            var faixas = new[] { Faixa.Create(0, 180, null, 22.5m).Value };
            var tributo = Tributo.Create(nome, BaseCalculo.Rendimento, TipoCalculo.FaixaPorDias, faixas, ordem, false).Value;
            await db.Tributos.AddAsync(tributo);
            await db.SaveChangesAsync();
            id = tributo.Id;
        });

        return id;
    }

    [Fact]
    public async Task GetConfiguracoesTributos_WithSeed_ShouldReturnOrderedByOrdem()
    {
        await SeedTributoAsync("IR", 3);
        await SeedTributoAsync("IOF", 1);
        await SeedTributoAsync("Taxa Custodia", 2);

        var response = await _client.GetAsync("/configuracoes/tributos", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dtos = await response.Content.ReadFromJsonAsync<List<TributoDto>>(cancellationToken: CancellationToken.None);
        dtos.Should().HaveCount(3);
        dtos.Select(d => d.Nome).Should().ContainInOrder("IOF", "Taxa Custodia", "IR");
    }

    [Fact]
    public async Task PostConfiguracoesTributos_WithValidPayload_ShouldReturn201WithLocationAndId()
    {
        var command = new CreateTributoCommand(
            "IOF",
            BaseCalculo.Rendimento,
            TipoCalculo.TabelaDiaria,
            [new FaixaDto(0, 29, null, 96m), new FaixaDto(null, null, 29, 0m)],
            1,
            false);

        var response = await _client.PostAsJsonAsync("/configuracoes/tributos", command, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().Contain("/configuracoes/tributos/");

        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("id").GetGuid().Should().NotBeEmpty();
    }

    [Fact]
    public async Task PostConfiguracoesTributos_ThenGetOnLocation_ShouldReturn200WithCreatedTributo()
    {
        var command = new CreateTributoCommand(
            "IOF Location",
            BaseCalculo.Rendimento,
            TipoCalculo.TabelaDiaria,
            [new FaixaDto(0, 29, null, 96m), new FaixaDto(null, null, 29, 0m)],
            1,
            false);

        var postResponse = await _client.PostAsJsonAsync("/configuracoes/tributos", command, CancellationToken.None);

        postResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var location = postResponse.Headers.Location;
        location.Should().NotBeNull();

        var getResponse = await _client.GetAsync(location, CancellationToken.None);

        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await getResponse.Content.ReadFromJsonAsync<TributoDto>(cancellationToken: CancellationToken.None);
        dto.Should().NotBeNull();
        dto!.Nome.Should().Be("IOF Location");
    }

    [Fact]
    public async Task GetConfiguracoesTributosById_WithUnknownId_ShouldReturn404()
    {
        var response = await _client.GetAsync($"/configuracoes/tributos/{Guid.NewGuid()}", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PostConfiguracoesTributos_WithStringEnums_ShouldReturn201()
    {
        var json = """
        {"nome":"IOF String","baseCalculo":"Rendimento","tipoCalculo":"TabelaDiaria","faixas":[{"diasMin":0,"diasMax":29,"dia":null,"aliquota":96},{"diasMin":null,"diasMax":null,"dia":29,"aliquota":0}],"ordem":1,"cumulativo":false}
        """;
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/configuracoes/tributos", content, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task PostConfiguracoesTributos_WithNumericEnums_ShouldReturn201()
    {
        var json = """
        {"nome":"IOF Numerico","baseCalculo":0,"tipoCalculo":2,"faixas":[{"diasMin":0,"diasMax":29,"dia":null,"aliquota":96},{"diasMin":null,"diasMax":null,"dia":29,"aliquota":0}],"ordem":1,"cumulativo":false}
        """;
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/configuracoes/tributos", content, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task PostConfiguracoesTributos_WithoutFaixas_ShouldReturn400()
    {
        var command = new CreateTributoCommand(
            "Tributo Sem Faixas",
            BaseCalculo.Rendimento,
            TipoCalculo.AliquotaFixa,
            [],
            1,
            false);

        var response = await _client.PostAsJsonAsync("/configuracoes/tributos", command, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PutConfiguracoesTributos_WithExistingIdAndValidPayload_ShouldReturn204()
    {
        var id = await SeedTributoAsync("IR", 1);

        var payload = new
        {
            Ativo = false,
            Faixas = new[] { new FaixaDto(0, 360, null, 15m) }
        };

        var response = await _client.PutAsJsonAsync($"/configuracoes/tributos/{id}", payload, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task PutConfiguracoesTributos_WithUnknownId_ShouldReturn404()
    {
        var payload = new
        {
            Ativo = true,
            Faixas = new[] { new FaixaDto(0, 360, null, 15m) }
        };

        var response = await _client.PutAsJsonAsync($"/configuracoes/tributos/{Guid.NewGuid()}", payload, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PutConfiguracoesTributos_WithInvalidFaixas_ShouldReturn400()
    {
        var id = await SeedTributoAsync("IR", 1);

        var payload = new
        {
            Ativo = true,
            Faixas = Array.Empty<FaixaDto>()
        };

        var response = await _client.PutAsJsonAsync($"/configuracoes/tributos/{id}", payload, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostConfiguracoesTributos_WithDuplicateNome_ShouldReturn409()
    {
        var nome = $"Tributo Dup {Guid.NewGuid()}";
        var command = new CreateTributoCommand(
            nome,
            BaseCalculo.Rendimento,
            TipoCalculo.TabelaDiaria,
            [new FaixaDto(0, 29, null, 96m), new FaixaDto(null, null, 29, 0m)],
            1,
            false);

        var firstResponse = await _client.PostAsJsonAsync("/configuracoes/tributos", command, CancellationToken.None);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var secondResponse = await _client.PostAsJsonAsync("/configuracoes/tributos", command, CancellationToken.None);

        secondResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        secondResponse.Content.Headers.ContentType?.MediaType.Should().Be(MediaTypeNames.Application.ProblemJson);

        var body = await secondResponse.Content.ReadAsStringAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("code").GetString().Should().Be("Tributo.AlreadyExists");
    }
}
