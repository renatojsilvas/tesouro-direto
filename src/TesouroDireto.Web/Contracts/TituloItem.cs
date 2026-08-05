using System.Text.Json.Serialization;
using TesouroDireto.Web.Services;

namespace TesouroDireto.Web.Contracts;

public sealed record TituloItem(
    string Codigo,
    string TipoTitulo,
    string DataVencimento,
    string Indexador,
    bool PagaJurosSemestrais,
    bool Vencido,
    [property: JsonPropertyName("_links")] IReadOnlyDictionary<string, HateoasLinkDto>? Links);
