using System.Diagnostics;
using MediatR;
using Prometheus;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Infrastructure.Observability;

/// <summary>
/// Behavior do MediatR (O6) que expõe latência e desfecho por caso de uso via
/// prometheus-net, no default registry (o mesmo já scrapeado por app.MapMetrics()
/// em Program.cs — sem fiação extra). O label request_type é sempre
/// typeof(TRequest).Name, nunca dado do request, para não estourar cardinalidade.
/// </summary>
public sealed class MetricsBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private static readonly Histogram RequestDuration = Metrics.CreateHistogram(
        "mediatr_request_duration_seconds",
        "Duração do processamento de um request MediatR, em segundos, por tipo de request.",
        new HistogramConfiguration
        {
            LabelNames = ["request_type"]
        });

    private static readonly Counter RequestsTotal = Metrics.CreateCounter(
        "mediatr_requests_total",
        "Total de requests MediatR processados, por tipo de request e desfecho (success|failure|exception).",
        new CounterConfiguration
        {
            LabelNames = ["request_type", "outcome"]
        });

    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var name = typeof(TRequest).Name;
        var outcome = "success";
        var sw = Stopwatch.StartNew();

        try
        {
            var response = await next(cancellationToken);
            outcome = response is IResult { IsFailure: true } ? "failure" : "success";
            return response;
        }
        catch
        {
            outcome = "exception";
            throw;
        }
        finally
        {
            sw.Stop();
            RequestDuration.WithLabels(name).Observe(sw.Elapsed.TotalSeconds);
            RequestsTotal.WithLabels(name, outcome).Inc();
        }
    }
}
