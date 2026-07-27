using System.Text.Json.Serialization;
using TesouroDireto.Application.Titulos;

namespace TesouroDireto.API.Http;

public sealed record HateoasLink(
    string Href,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Method = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Templated = null);

public sealed record TituloResource(
    string TipoTitulo,
    string DataVencimento,
    string Indexador,
    bool PagaJurosSemestrais,
    bool Vencido,
    string Codigo,
    [property: JsonPropertyName("_links")] IReadOnlyDictionary<string, HateoasLink> Links);

public static class HateoasLinks
{
    public static TituloResource ToResource(this TituloDto titulo, HttpContext httpContext, LinkGenerator linkGenerator)
    {
        var links = new Dictionary<string, HateoasLink>
        {
            ["self"] = new HateoasLink(
                ResolveLink(linkGenerator, httpContext, "GetTitulo", new { codigo = titulo.Codigo })),
            ["precos"] = new HateoasLink(
                ResolveLink(linkGenerator, httpContext, "GetPrecosPorCodigo", new { codigo = titulo.Codigo })),
            ["preco-atual"] = new HateoasLink(
                ResolveLink(linkGenerator, httpContext, "GetPrecoAtualPorCodigo", new { codigo = titulo.Codigo })),
            ["simular"] = new HateoasLink(
                ResolveLink(linkGenerator, httpContext, "SimularInvestimento", null), "POST", true),
        };

        return new TituloResource(
            titulo.TipoTitulo,
            titulo.DataVencimento,
            titulo.Indexador,
            titulo.PagaJurosSemestrais,
            titulo.Vencido,
            titulo.Codigo,
            links);
    }

    private static string ResolveLink(
        LinkGenerator linkGenerator, HttpContext httpContext, string routeName, object? routeValues)
    {
        return linkGenerator.GetPathByName(httpContext, routeName, routeValues)
            ?? throw new InvalidOperationException($"Route '{routeName}' could not be resolved by LinkGenerator.");
    }
}
