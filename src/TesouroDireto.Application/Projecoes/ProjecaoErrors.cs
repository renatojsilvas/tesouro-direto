using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.Projecoes;

public static class ProjecaoErrors
{
    public static readonly Error IndexadorNaoSuportado =
        new("Projecao.IndexadorNaoSuportado", "Indexador Prefixado does not have market projections.");

    public static readonly Error NotFound =
        new("Projecao.NotFound", "No projection found for the given indexador.");

    public static readonly Error HttpError =
        new("Projecao.HttpError", "Failed to fetch projection from BCB Focus API.");

    public static readonly Error UrlNotConfigured =
        new("Projecao.UrlNotConfigured", "FocusBcb:BaseUrl is not configured or is not HTTPS.");
}
