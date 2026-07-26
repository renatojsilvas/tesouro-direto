using FluentAssertions;
using MediatR;
using Prometheus;
using TesouroDireto.Domain.Common;
using TesouroDireto.Infrastructure.Observability;

namespace TesouroDireto.Infrastructure.Tests.Observability;

public sealed class MetricsBehaviorTests
{
    [Fact]
    public async Task Success_ShouldRecordSuccessOutcome()
    {
        var behavior = new MetricsBehavior<SuccessRequest, Result>();

        await behavior.Handle(
            new SuccessRequest(),
            _ => Task.FromResult(Result.Success()),
            CancellationToken.None);

        var text = await ExportMetricsAsync();

        text.Should().Contain(RequestsTotalLine(nameof(SuccessRequest), "success"));
    }

    [Fact]
    public async Task Failure_ShouldRecordFailureOutcome_NeverException()
    {
        var behavior = new MetricsBehavior<FailureRequest, Result>();

        await behavior.Handle(
            new FailureRequest(),
            _ => Task.FromResult(Result.Failure(new Error("Test", "falhou"))),
            CancellationToken.None);

        var text = await ExportMetricsAsync();

        text.Should().Contain(RequestsTotalLine(nameof(FailureRequest), "failure"));
        text.Should().NotContain(RequestsTotalLine(nameof(FailureRequest), "exception"));
    }

    [Fact]
    public async Task Exception_ShouldRethrowAndRecordExceptionOutcome()
    {
        var behavior = new MetricsBehavior<ExceptionRequest, Result>();

        Func<Task> act = () => behavior.Handle(
            new ExceptionRequest(),
            _ => throw new InvalidOperationException("boom"),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");

        var text = await ExportMetricsAsync();

        text.Should().Contain(RequestsTotalLine(nameof(ExceptionRequest), "exception"));
    }

    [Fact]
    public async Task AnyCall_ShouldRecordDurationSample()
    {
        var behavior = new MetricsBehavior<DurationRequest, Result>();

        await behavior.Handle(
            new DurationRequest(),
            _ => Task.FromResult(Result.Success()),
            CancellationToken.None);

        var text = await ExportMetricsAsync();

        text.Should().Contain("mediatr_request_duration_seconds_count");
        text.Should().Contain(RequestTypeLabel(nameof(DurationRequest)));
    }

    private static string RequestsTotalLine(string requestType, string outcome) =>
        $"mediatr_requests_total{{request_type=\"{requestType}\",outcome=\"{outcome}\"}}";

    private static string RequestTypeLabel(string requestType) =>
        $"request_type=\"{requestType}\"";

    private static async Task<string> ExportMetricsAsync()
    {
        using var stream = new MemoryStream();
        await Metrics.DefaultRegistry.CollectAndExportAsTextAsync(stream);
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private sealed record SuccessRequest : IRequest<Result>;
    private sealed record FailureRequest : IRequest<Result>;
    private sealed record ExceptionRequest : IRequest<Result>;
    private sealed record DurationRequest : IRequest<Result>;
}
