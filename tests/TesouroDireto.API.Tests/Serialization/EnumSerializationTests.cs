using System.Text.Json;
using FluentAssertions;
using TesouroDireto.Application.Tributos;
using TesouroDireto.Domain.Tributos;

namespace TesouroDireto.API.Tests.Serialization;

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

        var json = JsonSerializer.Serialize(command, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        json.Should().Contain("\"baseCalculo\":1");
        json.Should().Contain("\"tipoCalculo\":1");
    }
}
