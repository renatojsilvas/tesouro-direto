using System.Text.Json;
using FluentAssertions;

namespace TesouroDireto.Web.Tests;

public sealed class TituloItemHateoasDeserializationTests
{
    private sealed record TituloItem(
        string TipoTitulo,
        string DataVencimento,
        string Indexador,
        bool PagaJurosSemestrais,
        bool Vencido,
        string Codigo);

    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Deserialize_JsonWithExtraLinksField_ShouldSucceedAndIgnoreLinks()
    {
        const string json = """
        {
            "tipoTitulo": "Tesouro Selic",
            "dataVencimento": "2029-03-01",
            "indexador": "Selic",
            "pagaJurosSemestrais": false,
            "vencido": false,
            "codigo": "tesouro-selic-2029-03-01",
            "_links": {
                "self": { "href": "/titulos/tesouro-selic-2029-03-01" },
                "precos": { "href": "/titulos/tesouro-selic-2029-03-01/precos" },
                "preco-atual": { "href": "/titulos/tesouro-selic-2029-03-01/preco-atual" },
                "simular": { "href": "/simulador", "method": "POST", "templated": true }
            }
        }
        """;

        var item = JsonSerializer.Deserialize<TituloItem>(json, WebOptions);

        item.Should().NotBeNull();
        item!.TipoTitulo.Should().Be("Tesouro Selic");
        item.DataVencimento.Should().Be("2029-03-01");
        item.Indexador.Should().Be("Selic");
        item.PagaJurosSemestrais.Should().BeFalse();
        item.Vencido.Should().BeFalse();
        item.Codigo.Should().Be("tesouro-selic-2029-03-01");
    }

    [Fact]
    public void Deserialize_ArrayOfItemsWithLinksAndPaginationMetadataIgnored_ShouldSucceed()
    {
        const string json = """
        [
            {
                "tipoTitulo": "Tesouro Selic",
                "dataVencimento": "2029-03-01",
                "indexador": "Selic",
                "pagaJurosSemestrais": false,
                "vencido": false,
                "codigo": "tesouro-selic-2029-03-01",
                "_links": { "self": { "href": "/titulos/tesouro-selic-2029-03-01" } }
            }
        ]
        """;

        var items = JsonSerializer.Deserialize<List<TituloItem>>(json, WebOptions);

        items.Should().NotBeNull();
        items!.Should().HaveCount(1);
        items[0].Codigo.Should().Be("tesouro-selic-2029-03-01");
    }
}
