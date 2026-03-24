using MediatR;
using TesouroDireto.Application.Feriados;
using TesouroDireto.Application.Importacao;
using TesouroDireto.Application.Tributos;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Infrastructure.Caching;

public sealed class CacheInvalidationBehavior<TRequest, TResponse>(
    MemoryCacheInvalidator invalidator)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var response = await next(cancellationToken);

        if (IsSuccess(response))
        {
            Invalidate(request);
        }

        return response;
    }

    private void Invalidate(TRequest request)
    {
        switch (request)
        {
            case ImportCsvCommand:
                invalidator.InvalidateTitulos();
                invalidator.InvalidatePrecos();
                break;
            case ImportFeriadosCommand:
                invalidator.InvalidateFeriados();
                break;
            case CreateTributoCommand:
            case UpdateTributoCommand:
                invalidator.InvalidateTributos();
                break;
        }
    }

    private static bool IsSuccess(TResponse response) => response switch
    {
        Result r => r.IsSuccess,
        Result<ImportResult> r => r.IsSuccess,
        Result<ImportFeriadosResult> r => r.IsSuccess,
        Result<Guid> r => r.IsSuccess,
        _ => false
    };
}
