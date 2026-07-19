using System.Text.Json;
using FluentAssertions;
using TesouroDireto.Application.Tributos;
using TesouroDireto.Domain.Tributos;

namespace TesouroDireto.API.Tests.Serialization;

/// <summary>
/// Não é um teste de integração HTTP (não sobe host nem toca o pipeline) — só documenta
/// o contrato de serialização assumido pelos testes de integração de
/// /configuracoes/tributos: como o projeto não registra JsonStringEnumConverter, os
/// enums BaseCalculo/TipoCalculo precisam ir como número no corpo JSON do POST/PUT.
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
