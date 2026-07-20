using System.Text.Json;
using FluentAssertions;
using TesouroDireto.Application.Tributos;
using TesouroDireto.Domain.Tributos;

namespace TesouroDireto.API.Tests.Serialization;

/// <summary>
/// Não é um teste de integração HTTP (não sobe host nem toca o pipeline) — documenta apenas
/// o comportamento do serializer padrão usado pelo HttpClient de testes
/// (<see cref="JsonSerializerDefaults.Web"/> sem JsonStringEnumConverter), que ainda serializa
/// enums como número quando não configurado explicitamente.
/// A API (Program.cs) registra <c>JsonStringEnumConverter</c> via ConfigureHttpJsonOptions e,
/// na desserialização do POST /configuracoes/tributos, aceita AMBAS as representações dos
/// enums BaseCalculo/TipoCalculo: string (ex.: "Rendimento") e número (ex.: 0). Ver
/// TributosEndpointsTests.PostConfiguracoesTributos_WithStringEnums_ShouldReturn201 e
/// PostConfiguracoesTributos_WithNumericEnums_ShouldReturn201 para a prova empírica.
/// </summary>
public sealed class EnumSerializationTests
{
    [Fact]
    public void CreateTributoCommand_SerializesEnumsAsNumbers()
    {
        var command = new CreateTributoCommand(
            "IOF Numeric Check",
            BaseCalculo.PuBruto,
            TipoCalculo.AliquotaFixa,
            [new FaixaDto(null, null, null, 10m)],
            5,
            false);

        // Mesmas opções usadas internamente por HttpClient.PostAsJsonAsync (camelCase).
        var json = JsonSerializer.Serialize(command, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        json.Should().Contain("\"baseCalculo\":1"); // BaseCalculo.PuBruto == 1
        json.Should().Contain("\"tipoCalculo\":1"); // TipoCalculo.AliquotaFixa == 1
    }
}
