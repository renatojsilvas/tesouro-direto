using System.Globalization;
using MediatR;
using TesouroDireto.API.Extensions;
using TesouroDireto.API.Http;
using TesouroDireto.Application.Common;
using TesouroDireto.Application.PrecosTaxas;
using TesouroDireto.Application.Titulos;

namespace TesouroDireto.API.Endpoints;

public static class TituloEndpoints
{
    private const string CodigoRouteSegment = "{codigo:regex(^[a-z][a-z0-9-]*-\\d{{4}}-\\d{{2}}-\\d{{2}}$)}";

    public static void MapTituloEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapReadGet("/titulos", async (
            string? indexador,
            bool? vencido,
            HttpContext httpContext,
            LinkGenerator linkGenerator,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new GetTitulosQuery(indexador, vencido), cancellationToken);

            return result.ToHttpResult(v => Results.Ok(
                v.Select(titulo => titulo.ToResource(httpContext, linkGenerator)).ToList()));
        })
        .WithName("GetTitulos")
        .WithTags("Titulos")
        .WithSummary("Lista os títulos do Tesouro Direto")
        .WithDescription("Retorna os títulos cadastrados, com filtro opcional por indexador (nome do indexador) " +
            "e por vencido (true/false). Sem filtros, retorna todos os títulos. Cada item traz _links de navegação.")
        .Produces<IReadOnlyCollection<TituloResource>>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized);

        app.MapReadGet($"/titulos/{CodigoRouteSegment}", async (
            string codigo,
            HttpContext httpContext,
            LinkGenerator linkGenerator,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new GetTituloByCodigoQuery(codigo), cancellationToken);

            return result.ToHttpResult(v => Results.Ok(v.ToResource(httpContext, linkGenerator)));
        })
        .WithName("GetTitulo")
        .WithTags("Titulos")
        .WithSummary("Retorna um título pelo codigo")
        .WithDescription("Retorna o título identificado pelo codigo (identificador público recomendado, derivado " +
            "do tipo e da data de vencimento), com _links de navegação. 400 se o codigo estiver mal formado; " +
            "404 se nenhum título com esse codigo existir.")
        .Produces<TituloResource>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapReadGet("/titulos/preco-atual", async (
            string nome,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new GetPrecoAtualByNomeQuery(nome), cancellationToken);

            return result.ToHttpResult(v => Results.Ok(v));
        })
        .WithName("GetPrecoAtualPorNome")
        .WithTags("Titulos")
        .WithSummary("Retorna o preço/taxa mais recente de um título pelo nome")
        .WithDescription("Retorna o preço e taxa mais recentes do título cujo nome (query obrigatória) é informado. " +
            "400 se nome for vazio; 404 se nenhum título com esse nome existir.")
        .Produces<PrecoTaxaDto>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapReadGet("/titulos/precos", async (
            string nome,
            DateOnly? dataInicio,
            DateOnly? dataFim,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new GetPrecosByNomeQuery(nome, dataInicio, dataFim), cancellationToken);

            return result.ToHttpResult(v => Results.Ok(v));
        })
        .WithName("GetPrecosPorNome")
        .WithTags("Titulos")
        .WithSummary("Lista o histórico de preços/taxas de um título pelo nome")
        .WithDescription("Retorna o histórico de preços e taxas do título cujo nome (query obrigatória) é informado, " +
            "com filtro opcional por intervalo de datas (dataInicio, dataFim). " +
            "400 se nome for vazio; 404 se nenhum título com esse nome existir.")
        .Produces<IReadOnlyCollection<PrecoTaxaDto>>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapReadGet($"/titulos/{CodigoRouteSegment}/precos", async (
            string codigo,
            DateOnly? dataInicio,
            DateOnly? dataFim,
            int? page,
            int? pageSize,
            HttpContext httpContext,
            LinkGenerator linkGenerator,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(
                new GetPrecosByCodigoQuery(codigo, dataInicio, dataFim, page, pageSize), cancellationToken);

            return result.ToHttpResult(paged =>
            {
                httpContext.Response.Headers["X-Total-Count"] =
                    paged.TotalCount.ToString(CultureInfo.InvariantCulture);

                if (page is not null)
                {
                    httpContext.Response.Headers["Link"] =
                        BuildPaginationLinkHeader(httpContext, linkGenerator, codigo, page, pageSize, paged.TotalCount);
                }

                return Results.Ok(paged.Items);
            });
        })
        .WithName("GetPrecosPorCodigo")
        .WithTags("Titulos")
        .WithSummary("Lista o histórico de preços/taxas de um título pelo codigo")
        .WithDescription("Retorna o histórico de preços e taxas do título identificado pelo codigo (identificador " +
            "público recomendado, derivado do tipo e da data de vencimento), com filtro opcional por intervalo de " +
            "datas (dataInicio, dataFim) e paginação opcional (page/pageSize). Sem page, retorna a coleção inteira " +
            "(comportamento idêntico ao anterior à paginação). 400 se o codigo estiver mal formado; 404 se nenhum " +
            "título com esse codigo existir.")
        .Produces<IReadOnlyCollection<PrecoTaxaDto>>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapReadGet($"/titulos/{CodigoRouteSegment}/preco-atual", async (
            string codigo,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new GetPrecoAtualByCodigoQuery(codigo), cancellationToken);

            return result.ToHttpResult(v => Results.Ok(v));
        })
        .WithName("GetPrecoAtualPorCodigo")
        .WithTags("Titulos")
        .WithSummary("Retorna o preço/taxa mais recente de um título pelo codigo")
        .WithDescription("Retorna o preço e taxa mais recentes do título identificado pelo codigo (identificador " +
            "público recomendado, derivado do tipo e da data de vencimento). 400 se o codigo estiver mal formado; " +
            "404 se nenhum título com esse codigo existir.")
        .Produces<PrecoTaxaDto>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static string BuildPaginationLinkHeader(
        HttpContext httpContext,
        LinkGenerator linkGenerator,
        string codigo,
        int? page,
        int? pageSize,
        int totalCount)
    {
        var (normalizedPage, effectivePageSize) = PaginationDefaults.Normalize(page, pageSize);
        var lastPage = Math.Max(1, (int)Math.Ceiling(totalCount / (double)effectivePageSize));
        var effectivePage = Math.Min(normalizedPage, lastPage);

        string Href(int targetPage) => linkGenerator.GetPathByName(
            httpContext,
            "GetPrecosPorCodigo",
            new { codigo, page = targetPage, pageSize = effectivePageSize })!;

        var links = new List<string> { $"<{Href(1)}>; rel=\"first\"" };

        if (effectivePage > 1)
        {
            links.Add($"<{Href(effectivePage - 1)}>; rel=\"prev\"");
        }

        if (effectivePage < lastPage)
        {
            links.Add($"<{Href(effectivePage + 1)}>; rel=\"next\"");
        }

        links.Add($"<{Href(lastPage)}>; rel=\"last\"");

        return string.Join(", ", links);
    }
}
